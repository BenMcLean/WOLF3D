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
	/// Gets the primary (right) hand position in world space.
	/// In VR: Right controller position. In flatscreen: Calculated from camera.
	/// </summary>
	Vector3 PrimaryHandPosition { get; }

	/// <summary>
	/// Gets the primary hand forward direction (for aiming).
	/// </summary>
	Vector3 PrimaryHandForward { get; }

	/// <summary>
	/// Gets the secondary (left) hand position in world space.
	/// In VR: Left controller position. In flatscreen: Same as primary.
	/// </summary>
	Vector3 SecondaryHandPosition { get; }

	/// <summary>
	/// Gets the secondary hand forward direction.
	/// </summary>
	Vector3 SecondaryHandForward { get; }

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
	/// Check if the primary trigger (fire button) is pressed.
	/// In VR: Right trigger. In flatscreen: Left mouse or X key.
	/// </summary>
	bool IsPrimaryTriggerPressed();

	/// <summary>
	/// Check if the grip button is pressed (use/interact).
	/// In VR: Right grip. In flatscreen: R key.
	/// </summary>
	bool IsGripPressed();

	/// <summary>
	/// Get movement input vector from thumbstick or keyboard.
	/// </summary>
	Vector2 GetMovementInput();

	/// <summary>
	/// Get turn input from right thumbstick or mouse.
	/// </summary>
	Vector2 GetTurnInput();
}
