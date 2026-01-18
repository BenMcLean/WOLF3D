using Godot;

namespace BenMcLean.Wolf3D.Shared.Menu.Input;

/// <summary>
/// State of a single pointer (mouse or VR controller).
/// </summary>
public struct PointerState
{
	/// <summary>
	/// True if this pointer is active and should be displayed.
	/// </summary>
	public bool IsActive;
	/// <summary>
	/// Position in menu viewport coordinates (0-320 x 0-200).
	/// Only valid if IsActive is true.
	/// </summary>
	public Vector2 Position;
	/// <summary>
	/// True if select button was just pressed this frame (LMB / VR trigger).
	/// </summary>
	public bool SelectPressed;
	/// <summary>
	/// True if cancel button was just pressed this frame (RMB / VR grip).
	/// </summary>
	public bool CancelPressed;
}

/// <summary>
/// Interface for providing pointer positions to the menu system.
/// Supports up to two pointers for VR dual-controller input.
/// </summary>
public interface IMenuPointerProvider
{
	/// <summary>
	/// Gets the state of the primary pointer.
	/// In flatscreen mode: mouse position.
	/// In VR mode: right controller ray intersection.
	/// </summary>
	PointerState PrimaryPointer { get; }
	/// <summary>
	/// Gets the state of the secondary pointer.
	/// In flatscreen mode: always inactive.
	/// In VR mode: left controller ray intersection.
	/// </summary>
	PointerState SecondaryPointer { get; }
	/// <summary>
	/// Update pointer positions. Called each frame.
	/// </summary>
	/// <param name="delta">Time since last frame in seconds.</param>
	void Update(float delta);
}
