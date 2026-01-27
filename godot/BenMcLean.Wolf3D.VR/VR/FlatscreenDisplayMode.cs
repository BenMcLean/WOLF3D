using System;
using Godot;

namespace BenMcLean.Wolf3D.VR.VR;

/// <summary>
/// Flatscreen display mode for desktop gameplay.
/// Uses FPSCamera for WASD movement and mouse look.
/// Left click fires weapon, right click uses/pushes objects.
/// </summary>
public class FlatscreenDisplayMode : IDisplayMode
{
	private FPSCamera _camera;
	private Node3D _cameraHolder;
	private Node _parent;

	public event Action<string> PrimaryButtonPressed;
	public event Action<string> SecondaryButtonPressed;

	/// <summary>
	/// Event fired when primary button (left click) is released.
	/// Used for semi-auto weapon trigger release.
	/// </summary>
	public event Action<string> PrimaryButtonReleased;

	public bool IsVRActive => false;

	public Vector3 ViewerPosition => _camera?.GlobalPosition ?? Vector3.Zero;

	public float ViewerYRotation => _camera?.GlobalRotation.Y ?? 0f;

	// In flatscreen mode, "hand" is calculated from camera position
	// Offset slightly forward and down-right to simulate holding a weapon
	public Vector3 PrimaryHandPosition
	{
		get
		{
			if (_camera == null)
				return Vector3.Zero;

			// Position hand slightly forward and to the right of camera
			return _camera.GlobalPosition
				+ _camera.GlobalBasis.Z * -0.3f   // Forward
				+ _camera.GlobalBasis.X * 0.2f    // Right
				+ _camera.GlobalBasis.Y * -0.2f;  // Down
		}
	}

	public Vector3 PrimaryHandForward => _camera != null
		? -_camera.GlobalBasis.Z
		: Vector3.Forward;

	public Vector3 SecondaryHandPosition => PrimaryHandPosition + (_camera?.GlobalBasis.X ?? Vector3.Right) * -0.4f;

	public Vector3 SecondaryHandForward => PrimaryHandForward;

	public Camera3D Camera => _camera;

	public Node3D Origin => _cameraHolder;

	public void Initialize(Node parent)
	{
		_parent = parent;

		// Create a holder node for the camera (acts like XROrigin for positioning)
		_cameraHolder = new Node3D
		{
			Name = "CameraHolder"
		};
		parent.AddChild(_cameraHolder);

		// Create the FPSCamera
		_camera = new FPSCamera
		{
			Name = "FPSCamera",
			Current = true
		};
		_cameraHolder.AddChild(_camera);

		// Wire up mouse button events to IDisplayMode events
		// Left click = primary button (shoot)
		_camera.LeftClickPressed += () => PrimaryButtonPressed?.Invoke("trigger_click");
		_camera.LeftClickReleased += () => PrimaryButtonReleased?.Invoke("trigger_click");

		// Right click = secondary button (use/push)
		_camera.RightClickPressed += () => SecondaryButtonPressed?.Invoke("grip_click");
	}

	public void Update(double delta)
	{
		// FPSCamera handles its own input processing via _Input and _Process
	}

	public Vector2 GetMovementInput()
	{
		// WASD is handled by FPSCamera internally
		// For external queries, return based on key states
		Vector2 input = Vector2.Zero;
		if (Input.IsKeyPressed(Key.W)) input.Y -= 1;
		if (Input.IsKeyPressed(Key.S)) input.Y += 1;
		if (Input.IsKeyPressed(Key.A)) input.X -= 1;
		if (Input.IsKeyPressed(Key.D)) input.X += 1;
		return input;
	}

	public Vector2 GetTurnInput()
	{
		// Mouse look is handled by FPSCamera internally
		// Return zero since FPSCamera manages its own rotation
		return Vector2.Zero;
	}

	public void SetMovementValidator(Func<float, float, float, float, (float X, float Z)> validator)
	{
		_camera?.SetMovementValidator(validator);
	}
}
