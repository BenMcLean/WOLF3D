using System;
using Godot;

namespace BenMcLean.Wolf3D.VR.VR;

/// <summary>
/// Controls how the VR camera height is handled.
/// </summary>
public enum VRPlayMode
{
	/// <summary>
	/// Camera height is locked to Constants.HalfTileHeight regardless of real-world head height.
	/// XROrigin Y is adjusted each frame to compensate for the tracked HMD height.
	/// Matches classic Wolfenstein 3D's fixed eye height.
	/// </summary>
	FiveDOF,

	/// <summary>
	/// Camera height tracks the player's real-world head position.
	/// Allows crouching, standing tall, etc.
	/// </summary>
	Roomscale,
}

/// <summary>
/// VR display mode implementation using OpenXR.
/// Creates XROrigin3D with XRCamera3D and controller nodes.
/// Hand 0 = right controller, hand 1 = left controller.
/// </summary>
public class VRDisplayMode : IDisplayMode
{
	private XRInterface _xrInterface;
	private XROrigin3D _origin;
	private XRCamera3D _camera;
	private XRController3D _leftController;
	private XRController3D _rightController;
	private Node _parent;
	private readonly VRPlayMode _playMode;

	// Movement validation for collision detection
	private Func<float, float, float, float, (float X, float Z)> _validateMovement;
	private Vector3 _lastValidHmdPosition;

	// Joystick locomotion speed in meters per second
	// Wolf3D PLAYERSPEED = 3000 fixed-point units per tic at 70 tics/sec ≈ 7.8 m/s max
	// Using a moderate speed for VR comfort
	private const float VRMovementSpeed = 4f;

	// Snap turn: 45-degree increments, threshold at half deflection
	private const float SnapTurnAngle = Mathf.Pi / 4f;
	private const float SnapTurnThreshold = 0.5f;
	private bool _snapTurnReady = true;

	// Smooth turn: continuous rotation at up to 90 degrees per second at full stick deflection
	private const float SmoothTurnSpeed = Mathf.Pi / 2f;

	// Run speed: 2.5× walk speed, matching flatscreen Shift multiplier
	private const float RunSpeedMultiplier = 2.5f;

	private bool _smoothTurnEnabled = false;
	private bool _isRunning = false;
	private bool _teleportModeActive = false;

	// Teleportation mode activates when right thumbstick Y exceeds this threshold
	private const float TeleportThreshold = 0.5f;

	public bool LocomotionEnabled { get; set; } = true;

	/// <summary>
	/// True when the right thumbstick Y axis is pushed beyond TeleportThreshold.
	/// Turning is suppressed while teleportation mode is active.
	/// </summary>
	public bool IsTeleportModeActive => _teleportModeActive;

	public event Action<int, string> HandButtonPressed;
	public event Action<int, string> HandButtonReleased;

	public bool IsVRActive => true;

	public Vector3 ViewerPosition => _camera?.GlobalPosition ?? Vector3.Zero;

	public float ViewerYRotation => _camera?.GlobalRotation.Y ?? 0f;

	public Vector3 GetHandPosition(int handIndex) => handIndex == 1
		? _leftController?.GlobalPosition ?? ViewerPosition
		: _rightController?.GlobalPosition ?? ViewerPosition;

	public Vector3 GetHandForward(int handIndex)
	{
		if (handIndex == 1)
			return _leftController != null
				? -_leftController.GlobalBasis.Z
				: _camera != null ? -_camera.GlobalBasis.Z : Vector3.Forward;
		return _rightController != null
			? -_rightController.GlobalBasis.Z
			: _camera != null ? -_camera.GlobalBasis.Z : Vector3.Forward;
	}

	public Node3D GetHandNode(int handIndex) => handIndex == 1 ? _leftController : _rightController;

	public Camera3D Camera => _camera;

	public Node3D Origin => _origin;

	public VRDisplayMode(XRInterface xrInterface, VRPlayMode playMode = VRPlayMode.FiveDOF)
	{
		_xrInterface = xrInterface;
		_playMode = playMode;
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
			Current = true,
		};
		_origin.AddChild(_camera);

		// Create left controller
		_leftController = new XRController3D
		{
			Name = "LeftController",
			Tracker = "left_hand",
			Pose = "aim",
		};
		_origin.AddChild(_leftController);

		// Create right controller
		_rightController = new XRController3D
		{
			Name = "RightController",
			Tracker = "right_hand",
			Pose = "aim",
		};
		_origin.AddChild(_rightController);

		// Connect to controller button signals for event-driven input
		// Hand 0 = right controller, hand 1 = left controller
		_rightController.ButtonPressed += name => HandButtonPressed?.Invoke(0, name);
		_rightController.ButtonReleased += name => HandButtonReleased?.Invoke(0, name);
		_leftController.ButtonPressed += name => HandButtonPressed?.Invoke(1, name);
		_leftController.ButtonReleased += name => HandButtonReleased?.Invoke(1, name);

		// Enable XR on the viewport and disable object picking, which is incompatible
		// with stereo rendering and would trigger a warning every frame otherwise.
		if (_parent is Node node)
		{
			Viewport vp = node.GetViewport();
			vp.UseXR = true;
			vp.PhysicsObjectPicking = false;
		}
	}

	public void Update(double delta)
	{
		// Apply joystick locomotion and turning before height and collision corrections
		if (LocomotionEnabled)
		{
			ApplyLocomotion((float)delta);
			ApplyTurn((float)delta);
		}

		// VR tracking is handled automatically by OpenXR
		// In 5DOF mode, lock the camera height to HalfTileHeight by adjusting the origin Y.
		// Formula: origin.Y = HalfTileHeight - camera.Position.Y (local Y relative to origin)
		// This keeps camera.GlobalPosition.Y == HalfTileHeight regardless of real head height.
		if (_playMode == VRPlayMode.FiveDOF && _origin != null && _camera != null)
		{
			Vector3 originPos = _origin.Position;
			_origin.Position = new Vector3(originPos.X, Constants.HalfTileHeight - _camera.Position.Y, originPos.Z);
		}

		// Validate HMD position against collision and push origin back if needed
		ValidateVRPosition();
	}

	/// <summary>
	/// Moves the XROrigin based on the left thumbstick.
	/// Forward/back on stick Y, strafe on stick X.
	/// Direction is derived from the camera's horizontal facing (HMD yaw only, ignoring pitch).
	/// </summary>
	private void ApplyLocomotion(float delta)
	{
		if (_origin == null || _leftController == null || _camera == null)
			return;

		Vector2 stick = _leftController.GetVector2("primary");
		if (stick.LengthSquared() < 0.01f)
			return;

		// Derive horizontal forward/right vectors from the camera's actual world-space basis,
		// projected onto the XZ plane so head pitch doesn't affect locomotion direction.
		// Using GlobalBasis directly is more reliable than Euler decomposition for VR poses.
		Vector3 forward = new Vector3(-_camera.GlobalBasis.Z.X, 0f, -_camera.GlobalBasis.Z.Z).Normalized();
		Vector3 right = new Vector3(_camera.GlobalBasis.X.X, 0f, _camera.GlobalBasis.X.Z).Normalized();

		float speed = _isRunning ? VRMovementSpeed * RunSpeedMultiplier : VRMovementSpeed;
		Vector3 movement = (forward * stick.Y + right * stick.X) * speed * delta;
		_origin.GlobalPosition += movement;
	}

	/// <summary>
	/// Dispatches to snap or smooth turning, or enters teleportation mode.
	/// Teleportation mode requires the thumbstick to be pushed beyond TeleportThreshold
	/// on the Y axis AND within TeleportThreshold on the X axis. This prevents accidental
	/// teleport activation when the player pushes slightly forward while turning.
	/// When teleportation mode is active, turning is suppressed entirely.
	/// </summary>
	private void ApplyTurn(float delta)
	{
		if (_rightController == null)
			return;

		Vector2 stick = _rightController.GetVector2("primary");

		if (Mathf.Abs(stick.Y) > TeleportThreshold && Mathf.Abs(stick.X) < TeleportThreshold)
		{
			// Thumbstick pushed forward or backward with little horizontal deflection —
			// enter teleport mode, suppress turning
			_teleportModeActive = true;
			return;
		}

		// Thumbstick Y returned to center (or X exceeded threshold) — exit teleport mode
		_teleportModeActive = false;

		if (_smoothTurnEnabled)
			ApplySmoothTurn(delta);
		else
			ApplySnapTurn();
	}

	/// <summary>
	/// Rotates the XROrigin by 45° increments based on the right thumbstick.
	/// Snap turn is debounced: stick must return to center before the next snap fires.
	/// The rotation is performed around the HMD world position to avoid positional drift.
	/// </summary>
	private void ApplySnapTurn()
	{
		if (_origin == null || _rightController == null || _camera == null)
			return;

		float x = _rightController.GetVector2("primary").X;

		if (Mathf.Abs(x) < SnapTurnThreshold)
		{
			_snapTurnReady = true;
			return;
		}

		if (!_snapTurnReady)
			return;

		_snapTurnReady = false;

		// Snap by 45° in the direction of stick deflection
		float angle = x > 0f ? -SnapTurnAngle : SnapTurnAngle;

		// Rotate around the HMD world position so the player's viewpoint doesn't shift
		Vector3 hmdBefore = _camera.GlobalPosition;
		_origin.RotateY(angle);
		Vector3 hmdAfter = _camera.GlobalPosition;
		_origin.GlobalPosition -= hmdAfter - hmdBefore;
	}

	/// <summary>
	/// Continuously rotates the XROrigin based on the right thumbstick at SmoothTurnSpeed.
	/// The rotation is performed around the HMD world position to avoid positional drift.
	/// </summary>
	private void ApplySmoothTurn(float delta)
	{
		if (_origin == null || _rightController == null || _camera == null)
			return;

		float x = _rightController.GetVector2("primary").X;

		if (Mathf.Abs(x) < SnapTurnThreshold)
			return;

		float angle = -x * SmoothTurnSpeed * delta;

		// Rotate around the HMD world position so the player's viewpoint doesn't shift
		Vector3 hmdBefore = _camera.GlobalPosition;
		_origin.RotateY(angle);
		Vector3 hmdAfter = _camera.GlobalPosition;
		_origin.GlobalPosition -= hmdAfter - hmdBefore;
	}

	public void ToggleRunning() => _isRunning = !_isRunning;

	public void ToggleTurnMode()
	{
		_smoothTurnEnabled = !_smoothTurnEnabled;
		// Reset snap turn debounce so the first snap after switching modes fires cleanly
		_snapTurnReady = true;
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

	/// <summary>
	/// Adjusts XROrigin rotation and position so the HMD ends up at spawnWorldPos facing panelWorldPos.
	/// Used to reset the player's orientation when entering the MenuRoom or pressing the menu button.
	/// </summary>
	public void ResetPositionFacing(Vector3 panelWorldPos, Vector3 spawnWorldPos)
	{
		if (_origin == null || _camera == null)
			return;

		// Desired yaw: camera should face from spawn toward panel (XZ plane only)
		float desiredYaw = Mathf.Atan2(
			panelWorldPos.X - spawnWorldPos.X,
			-(panelWorldPos.Z - spawnWorldPos.Z));

		// Set origin yaw so camera's global yaw = desiredYaw
		// camera.GlobalRotation.Y ≈ origin.Rotation.Y + camera.Rotation.Y
		_origin.Rotation = new Vector3(0f, desiredYaw - _camera.Rotation.Y, 0f);

		// Adjust origin XZ so camera global XZ lands on spawnWorldPos XZ
		Vector3 rotatedOffset = _origin.Basis * _camera.Position;
		_origin.Position = new Vector3(
			spawnWorldPos.X - rotatedOffset.X,
			_origin.Position.Y,
			spawnWorldPos.Z - rotatedOffset.Z);

		// Reset so ValidateVRPosition re-initializes from the new position
		_lastValidHmdPosition = Vector3.Zero;
	}
}
