using System;
using Godot;

namespace BenMcLean.Wolf3D.VR.VR;

/// <summary>
/// Flatscreen display mode for desktop gameplay.
/// Uses FPSCamera for WASD movement and mouse look.
/// Left click fires weapon (hand 0 trigger_click), right click uses/pushes objects (hand 0 grip_click).
/// Only hand 0 is used; there is no hand 1 in flatscreen.
/// </summary>
public class FlatscreenDisplayMode : IDisplayMode
{
	private FPSCamera _camera;
	private Node3D _cameraHolder;
	private Node _parent;
	private bool _locomotionEnabled = true;

	public event Action<int, string> HandButtonPressed;
	public event Action<int, string> HandButtonReleased;

	public bool IsVRActive => false;

	public Vector3 ViewerPosition => _camera?.GlobalPosition ?? Vector3.Zero;

	public float ViewerYRotation => _camera?.GlobalRotation.Y ?? 0f;

	// In flatscreen mode, "hand" is calculated from camera position.
	// Hand 0 is offset slightly forward and down-right to simulate holding a weapon.
	// Hand 1 is offset to the left for asymmetric dual-wield layout.
	public Vector3 GetHandPosition(int handIndex)
	{
		if (_camera == null)
			return Vector3.Zero;

		Vector3 base0 = _camera.GlobalPosition
			+ _camera.GlobalBasis.Z * -0.3f   // Forward
			+ _camera.GlobalBasis.X * 0.2f    // Right
			+ _camera.GlobalBasis.Y * -0.2f;  // Down

		return handIndex == 1
			? base0 + _camera.GlobalBasis.X * -0.4f
			: base0;
	}

	public Vector3 GetHandForward(int handIndex) => _camera != null
		? -_camera.GlobalBasis.Z
		: Vector3.Forward;

	// No controller nodes in flatscreen mode
	public Node3D GetHandNode(int handIndex) => null;

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

		// Apply any locomotion state that was set before Initialize was called
		_camera.MovementEnabled = _locomotionEnabled;

		// Wire up mouse button events to IDisplayMode events
		// Left click = hand 0 trigger (shoot)
		_camera.LeftClickPressed += () => HandButtonPressed?.Invoke(0, "trigger_click");
		_camera.LeftClickReleased += () => HandButtonReleased?.Invoke(0, "trigger_click");

		// Right click = hand 0 grip (use/push)
		_camera.RightClickPressed += () => HandButtonPressed?.Invoke(0, "grip_click");
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

	// Running is handled by Shift key in flatscreen mode
	public void ToggleRunning() { }

	// Turn mode only applies in VR
	public void ToggleTurnMode() { }

	public bool LocomotionEnabled
	{
		get => _locomotionEnabled;
		set
		{
			_locomotionEnabled = value;
			if (_camera != null)
				_camera.MovementEnabled = value;
		}
	}

	// Position reset only applies in VR
	public void ResetPositionFacing(Vector3 panelWorldPos, Vector3 spawnWorldPos) { }

	// Teleportation only applies in VR
	public bool IsTeleportModeActive => false;

	// Grip controller nodes only exist in VR
	public Node3D GetGripHandNode(int handIndex) => null;
}
