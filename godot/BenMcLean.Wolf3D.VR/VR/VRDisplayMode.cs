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

		// Enable XR on the viewport
		if (_parent is Node node)
		{
			node.GetViewport().UseXR = true;
		}
	}

	public void Update(double delta)
	{
		// VR tracking is handled automatically by OpenXR
		// This method can be used for any per-frame VR-specific logic
	}

	public bool IsPrimaryTriggerPressed()
	{
		if (_rightController == null)
			return false;

		// OpenXR trigger is typically "trigger" action with value 0-1
		return _rightController.GetFloat("trigger") > 0.8f;
	}

	public bool IsGripPressed()
	{
		if (_rightController == null)
			return false;

		return _rightController.GetFloat("grip") > 0.8f;
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

	public bool IsPrimaryHandTriggerPressed() =>
		_rightController != null && _rightController.GetFloat("trigger") > 0.8f;

	public bool IsPrimaryHandGripPressed() =>
		_rightController != null && _rightController.GetFloat("grip") > 0.8f;

	public bool IsSecondaryHandTriggerPressed() =>
		_leftController != null && _leftController.GetFloat("trigger") > 0.8f;

	public bool IsSecondaryHandGripPressed() =>
		_leftController != null && _leftController.GetFloat("grip") > 0.8f;
}
