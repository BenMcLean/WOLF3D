using System;
using Godot;

namespace BenMcLean.Wolf3D.VR.VR;

/// <summary>
/// VR display mode implementation using OpenXR.
/// Creates XROrigin3D with XRCamera3D and controller nodes.
/// </summary>
public class VRDisplayMode : IDisplayMode
{
	private XRInterface _xrInterface;
	private XROrigin3D _origin;
	private XRCamera3D _camera;
	private XRController3D _leftController;
	private XRController3D _rightController;
	private Node _parent;

	// Movement validation for collision detection
	private Func<float, float, float, float, (float X, float Z)> _validateMovement;
	private Vector3 _lastValidHmdPosition;

	public event Action<string> PrimaryButtonPressed;
	public event Action<string> PrimaryButtonReleased;
	public event Action<string> SecondaryButtonPressed;

	public bool IsVRActive => true;

	public Vector3 ViewerPosition => _camera?.GlobalPosition ?? Vector3.Zero;

	public float ViewerYRotation => _camera?.GlobalRotation.Y ?? 0f;

	public Vector3 PrimaryHandPosition => _rightController?.GlobalPosition ?? ViewerPosition;

	public Vector3 PrimaryHandForward => _rightController != null
		? -_rightController.GlobalBasis.Z
		: _camera != null ? -_camera.GlobalBasis.Z : Vector3.Forward;

	public Vector3 SecondaryHandPosition => _leftController?.GlobalPosition ?? ViewerPosition;

	public Vector3 SecondaryHandForward => _leftController != null
		? -_leftController.GlobalBasis.Z
		: _camera != null ? -_camera.GlobalBasis.Z : Vector3.Forward;

	public Camera3D Camera => _camera;

	public Node3D Origin => _origin;

	public VRDisplayMode(XRInterface xrInterface)
	{
		_xrInterface = xrInterface;
	}

	public void Initialize(Node parent)
	{
		_parent = parent;

		// Create XR origin (the movable root for VR)
		_origin = new XROrigin3D
		{
			Name = "XROrigin"
		};
		parent.AddChild(_origin);

		// Create XR camera (tracks headset)
		_camera = new XRCamera3D
		{
			Name = "XRCamera",
			Current = true
		};
		_origin.AddChild(_camera);

		// Create left controller
		_leftController = new XRController3D
		{
			Name = "LeftController",
			Tracker = "left_hand"
		};
		_origin.AddChild(_leftController);

		// Create right controller
		_rightController = new XRController3D
		{
			Name = "RightController",
			Tracker = "right_hand"
		};
		_origin.AddChild(_rightController);

		// Connect to controller button signals for event-driven input
		_leftController.ButtonPressed += name => SecondaryButtonPressed?.Invoke(name);
		_rightController.ButtonPressed += name => PrimaryButtonPressed?.Invoke(name);
		_rightController.ButtonReleased += name => PrimaryButtonReleased?.Invoke(name);

		// Enable XR on the viewport
		if (_parent is Node node)
		{
			node.GetViewport().UseXR = true;
		}
	}

	public void Update(double delta)
	{
		// VR tracking is handled automatically by OpenXR
		// Validate HMD position against collision and push origin back if needed
		ValidateVRPosition();
	}

	/// <summary>
	/// Validates the VR headset position against collision.
	/// If the player physically moves into a wall, the XROrigin is pushed back to compensate.
	/// </summary>
	private void ValidateVRPosition()
	{
		if (_validateMovement == null || _camera == null || _origin == null)
			return;

		// Get current HMD world position
		Vector3 hmdGlobal = _camera.GlobalPosition;

		// On first frame, initialize last valid position
		if (_lastValidHmdPosition == Vector3.Zero)
		{
			_lastValidHmdPosition = hmdGlobal;
			return;
		}

		// Validate the movement from last valid position to current HMD position
		(float validX, float validZ) = _validateMovement(
			_lastValidHmdPosition.X, _lastValidHmdPosition.Z,
			hmdGlobal.X, hmdGlobal.Z);

		// If position was adjusted, push XROrigin to compensate
		float deltaX = validX - hmdGlobal.X;
		float deltaZ = validZ - hmdGlobal.Z;

		if (Mathf.Abs(deltaX) > 0.001f || Mathf.Abs(deltaZ) > 0.001f)
		{
			// Push origin by the correction amount
			Vector3 originPos = _origin.GlobalPosition;
			_origin.GlobalPosition = new Vector3(
				originPos.X + deltaX,
				originPos.Y,
				originPos.Z + deltaZ);
		}

		// Update last valid position (the validated position, not the raw HMD)
		_lastValidHmdPosition = new Vector3(validX, hmdGlobal.Y, validZ);
	}

	public Vector2 GetMovementInput()
	{
		if (_leftController == null)
			return Vector2.Zero;

		// Left thumbstick for movement
		return _leftController.GetVector2("primary");
	}

	public Vector2 GetTurnInput()
	{
		if (_rightController == null)
			return Vector2.Zero;

		// Right thumbstick for turning
		return _rightController.GetVector2("primary");
	}

	public void SetMovementValidator(Func<float, float, float, float, (float X, float Z)> validator)
	{
		_validateMovement = validator;
		// Reset last valid position so it gets initialized on next Update
		_lastValidHmdPosition = Vector3.Zero;
	}
}
