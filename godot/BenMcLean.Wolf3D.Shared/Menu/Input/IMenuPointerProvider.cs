using Godot;

namespace BenMcLean.Wolf3D.Shared.Menu.Input;

/// <summary>
/// State of a single pointer (mouse or VR controller).
/// Used by IMenuInput implementations that support pointing (flatscreen mouse, VR ray cast).
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
