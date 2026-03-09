using System;
using Godot;

namespace BenMcLean.Wolf3D.VR.VR;

/// <summary>
/// Abstraction interface for display mode (VR vs Flatscreen).
/// Allows consistent camera/input handling across both modes.
/// </summary>
public interface IDisplayMode
{
	/// <summary>
	/// True if VR mode is active and initialized.
	/// </summary>
	bool IsVRActive { get; }

	/// <summary>
	/// Gets the current viewer position in world space.
	/// In VR: XRCamera3D position. In flatscreen: FreeLookCamera position.
	/// </summary>
	Vector3 ViewerPosition { get; }

	/// <summary>
	/// Gets the current viewer Y rotation in radians for billboard calculations.
	/// </summary>
	float ViewerYRotation { get; }

	/// <summary>
	/// Gets the world-space position of the specified hand.
	/// In VR: hand 0 = right controller, hand 1 = left controller.
	/// In flatscreen: both hands calculated from camera.
	/// </summary>
	/// <param name="handIndex">0 = right/primary, 1 = left/secondary</param>
	Vector3 GetHandPosition(int handIndex);

	/// <summary>
	/// Gets the forward direction of the specified hand (for aiming).
	/// In VR: controller orientation. In flatscreen: camera forward.
	/// </summary>
	/// <param name="handIndex">0 = right/primary, 1 = left/secondary</param>
	Vector3 GetHandForward(int handIndex);

	/// <summary>
	/// Gets the scene node for the specified hand.
	/// In VR: the XRController3D for that hand. In flatscreen: null (no controller node).
	/// Attach WeaponHandMesh as a child to position it with the hand.
	/// </summary>
	/// <param name="handIndex">0 = right/primary, 1 = left/secondary</param>
	Node3D GetHandNode(int handIndex);

	/// <summary>
	/// The camera node (XRCamera3D or Camera3D).
	/// </summary>
	Camera3D Camera { get; }

	/// <summary>
	/// The root node for the display mode (XROrigin3D or Camera3D parent).
	/// Position this to move the player in world space.
	/// </summary>
	Node3D Origin { get; }

	/// <summary>
	/// Initialize the display mode. Called from _Ready().
	/// </summary>
	/// <param name="parent">Parent node to add camera rig to.</param>
	void Initialize(Node parent);

	/// <summary>
	/// Process input and update state. Called from _Process().
	/// </summary>
	/// <param name="delta">Time since last frame.</param>
	void Update(double delta);

	/// <summary>
	/// Event fired when a button is pressed on any hand controller.
	/// In VR: hand 0 = right controller, hand 1 = left controller.
	/// In flatscreen: left click fires hand 0 trigger_click; right click fires hand 0 grip_click.
	/// Parameters: handIndex (0/1), buttonName (e.g. "trigger_click", "grip_click").
	/// </summary>
	event Action<int, string> HandButtonPressed;

	/// <summary>
	/// Event fired when a button is released on any hand controller.
	/// In VR: hand 0 = right controller, hand 1 = left controller.
	/// In flatscreen: left click release fires hand 0 trigger_click.
	/// Parameters: handIndex (0/1), buttonName (e.g. "trigger_click", "grip_click").
	/// </summary>
	event Action<int, string> HandButtonReleased;

	/// <summary>
	/// Get movement input vector from thumbstick or keyboard.
	/// </summary>
	Vector2 GetMovementInput();

	/// <summary>
	/// Get turn input from right thumbstick or mouse.
	/// </summary>
	Vector2 GetTurnInput();

	/// <summary>
	/// Sets the movement validator callback for collision detection.
	/// When set, movement will be validated against walls, doors, and enemies.
	/// </summary>
	/// <param name="validator">Callback that takes (currentX, currentZ, desiredX, desiredZ) and returns validated (X, Z)</param>
	void SetMovementValidator(Func<float, float, float, float, (float X, float Z)> validator);

	/// <summary>
	/// Toggles between walk and run speed.
	/// In VR: multiplies locomotion speed by RunSpeedMultiplier.
	/// In flatscreen: no-op (Shift key handles running).
	/// </summary>
	void ToggleRunning();

	/// <summary>
	/// Toggles between snap turn (45° increments, default) and smooth continuous turn.
	/// In VR: switches turn mode. In flatscreen: no-op.
	/// </summary>
	void ToggleTurnMode();

	/// <summary>
	/// When false, thumbstick locomotion and turning are disabled.
	/// In VR, room-scale physical movement may still occur subject to the movement validator.
	/// Default: true.
	/// </summary>
	bool LocomotionEnabled { get; set; }

	/// <summary>
	/// Resets the player's position and orientation so the viewer ends up at spawnWorldPos
	/// facing toward panelWorldPos.
	/// In VR: adjusts XROrigin to place the HMD at spawnWorldPos and face the panel.
	/// In flatscreen: no-op.
	/// </summary>
	void ResetPositionFacing(Vector3 panelWorldPos, Vector3 spawnWorldPos);
}
