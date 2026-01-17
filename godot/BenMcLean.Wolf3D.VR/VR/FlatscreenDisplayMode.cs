using Godot;

namespace BenMcLean.Wolf3D.VR.VR;

/// <summary>
/// Flatscreen display mode for desktop debugging.
/// Uses FreeLookCamera for movement and mouse look.
/// </summary>
public class FlatscreenDisplayMode : IDisplayMode
{
	private FreeLookCamera _camera;
	private Node3D _cameraHolder;
	private Node _parent;

	// Track key states for input
	private bool _firePressed;
	private bool _usePressed;

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

		// Create the FreeLookCamera
		_camera = new FreeLookCamera
		{
			Name = "FreeLookCamera",
			Current = true
		};
		_cameraHolder.AddChild(_camera);

		// Enable the free look camera immediately
		_camera.Enabled = true;
	}

	public void Update(double delta)
	{
		// FreeLookCamera handles its own input processing via _Input and _Process
		// We just need to track our action inputs here
		_firePressed = Input.IsActionPressed("ui_accept") || Input.IsKeyPressed(Key.X) || Input.IsMouseButtonPressed(MouseButton.Left);
		_usePressed = Input.IsKeyPressed(Key.R);
	}

	public bool IsPrimaryTriggerPressed()
	{
		return _firePressed;
	}

	public bool IsGripPressed()
	{
		return _usePressed;
	}

	public Vector2 GetMovementInput()
	{
		// WASD is handled by FreeLookCamera internally
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
		// Mouse look is handled by FreeLookCamera internally
		// Return zero since FreeLookCamera manages its own rotation
		return Vector2.Zero;
	}
}
