using System;
using Godot;

namespace BenMcLean.Wolf3D.VR;

/// <summary>
/// First-person shooter style camera for flatscreen mode.
/// Fixed height, WASD movement, always-on mouse look, mouse buttons for shoot/push.
/// </summary>
public partial class FPSCamera : Camera3D
{
	// Speed modifiers for Shift/Alt keys
	private const float SHIFT_MULTIPLIER = 2.5f;
	private const float ALT_MULTIPLIER = 1.0f / SHIFT_MULTIPLIER;

	[Export(PropertyHint.Range, "0.0f,1.0f")]
	public float Sensitivity { get; set; } = 0.25f;

	/// <summary>
	/// Event fired when left mouse button is pressed (shoot).
	/// </summary>
	public event Action LeftClickPressed;

	/// <summary>
	/// Event fired when left mouse button is released.
	/// </summary>
	public event Action LeftClickReleased;

	/// <summary>
	/// Event fired when right mouse button is pressed (push/use).
	/// </summary>
	public event Action RightClickPressed;

	// Mouse state
	private Vector2 _mouseMotion = Vector2.Zero;
	private float _totalPitch = 0.0f;

	// Movement state
	private Vector3 _velocity = Vector3.Zero;
	private const float ACCELERATION = 30f;
	private const float DECELERATION = -10f;
	private float _velMultiplier = 4f;

	// Keyboard state
	private bool _w = false;
	private bool _s = false;
	private bool _a = false;
	private bool _d = false;
	private bool _shift = false;
	private bool _alt = false;

	public override void _Ready()
	{
		// Capture mouse immediately for FPS-style control
		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	public override void _Input(InputEvent @event)
	{
		// Mouse motion for look
		if (@event is InputEventMouseMotion mouseMotion)
		{
			_mouseMotion = mouseMotion.Relative;
		}

		// Mouse buttons for shoot/push
		if (@event is InputEventMouseButton mouseButton)
		{
			switch (mouseButton.ButtonIndex)
			{
				case MouseButton.Left:
					if (mouseButton.Pressed)
						LeftClickPressed?.Invoke();
					else
						LeftClickReleased?.Invoke();
					break;

				case MouseButton.Right:
					if (mouseButton.Pressed)
						RightClickPressed?.Invoke();
					break;

				case MouseButton.WheelUp:
					_velMultiplier = Mathf.Clamp(_velMultiplier * 1.1f, 0.2f, 20f);
					break;

				case MouseButton.WheelDown:
					_velMultiplier = Mathf.Clamp(_velMultiplier / 1.1f, 0.2f, 20f);
					break;
			}
		}

		// Keyboard for movement
		if (@event is InputEventKey keyEvent)
		{
			switch (keyEvent.Keycode)
			{
				case Key.W:
					_w = keyEvent.Pressed;
					break;
				case Key.S:
					_s = keyEvent.Pressed;
					break;
				case Key.A:
					_a = keyEvent.Pressed;
					break;
				case Key.D:
					_d = keyEvent.Pressed;
					break;
				case Key.Shift:
					_shift = keyEvent.Pressed;
					break;
				case Key.Alt:
					_alt = keyEvent.Pressed;
					break;
				case Key.Escape:
					// Toggle mouse capture with Escape
					if (keyEvent.Pressed)
					{
						Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
							? Input.MouseModeEnum.Visible
							: Input.MouseModeEnum.Captured;
					}
					break;
			}
		}
	}

	public override void _Process(double delta)
	{
		UpdateMouseLook();
		UpdateMovement((float)delta);
	}

	/// <summary>
	/// Updates camera rotation based on mouse movement.
	/// Always active when mouse is captured.
	/// </summary>
	private void UpdateMouseLook()
	{
		if (Input.MouseMode != Input.MouseModeEnum.Captured)
			return;

		Vector2 motion = _mouseMotion * Sensitivity;
		_mouseMotion = Vector2.Zero;

		float yaw = motion.X;
		float pitch = motion.Y;

		// Clamp pitch to prevent looking too far up/down
		pitch = Mathf.Clamp(pitch, -90 - _totalPitch, 90 - _totalPitch);
		_totalPitch += pitch;

		RotateY(Mathf.DegToRad(-yaw));
		RotateObjectLocal(Vector3.Right, Mathf.DegToRad(-pitch));
	}

	/// <summary>
	/// Updates camera movement based on WASD keys.
	/// Movement is horizontal only (Y position is fixed by parent).
	/// </summary>
	private void UpdateMovement(float delta)
	{
		// Compute desired direction from key states (horizontal only)
		Vector3 direction = Vector3.Zero;
		if (_d) direction.X += 1.0f;
		if (_a) direction.X -= 1.0f;
		if (_s) direction.Z += 1.0f;
		if (_w) direction.Z -= 1.0f;

		// Compute velocity change with acceleration and drag
		Vector3 offset = direction.Normalized() * ACCELERATION * _velMultiplier * delta
					   + _velocity.Normalized() * DECELERATION * _velMultiplier * delta;

		// Speed multipliers
		float speedMulti = 1.0f;
		if (_shift) speedMulti *= SHIFT_MULTIPLIER;
		if (_alt) speedMulti *= ALT_MULTIPLIER;

		// Check if we should stop to prevent jittering
		if (direction == Vector3.Zero && offset.LengthSquared() > _velocity.LengthSquared())
		{
			_velocity = Vector3.Zero;
		}
		else
		{
			// Clamp velocity components
			_velocity.X = Mathf.Clamp(_velocity.X + offset.X, -_velMultiplier, _velMultiplier);
			_velocity.Z = Mathf.Clamp(_velocity.Z + offset.Z, -_velMultiplier, _velMultiplier);

			// Translate in camera's local space (horizontal only)
			Translate(new Vector3(_velocity.X, 0, _velocity.Z) * delta * speedMulti);
		}

		// Enforce fixed height - reset Y to 0 (parent handles world Y position)
		Vector3 pos = Position;
		pos.Y = 0;
		Position = pos;
	}
}
