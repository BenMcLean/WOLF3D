using Godot;

namespace BenMcLean.Wolf3D.Shared.Menu.Input;

/// <summary>
/// Input state snapshot for menu navigation.
/// Captures all menu-relevant input in a single frame.
/// </summary>
public struct MenuInputState
{
	/// <summary>
	/// Cursor position in screen coordinates (for mouse/VR ray).
	/// Can be used for hover detection.
	/// </summary>
	public Vector2 CursorPosition;
	/// <summary>
	/// True if select/accept button was just pressed this frame.
	/// Maps to: Enter key, Space, LMB click, VR trigger
	/// </summary>
	public bool SelectPressed;
	/// <summary>
	/// True if cancel/back button was just pressed this frame.
	/// Maps to: Escape key, RMB, VR grip
	/// </summary>
	public bool CancelPressed;
	/// <summary>
	/// True if up navigation was just pressed this frame.
	/// Maps to: Up arrow, scroll up, gamepad up
	/// </summary>
	public bool UpPressed;
	/// <summary>
	/// True if down navigation was just pressed this frame.
	/// Maps to: Down arrow, scroll down, gamepad down
	/// </summary>
	public bool DownPressed;
	/// <summary>
	/// True if left navigation was just pressed this frame.
	/// Maps to: Left arrow, gamepad left
	/// </summary>
	public bool LeftPressed;
	/// <summary>
	/// True if right navigation was just pressed this frame.
	/// Maps to: Right arrow, gamepad right
	/// </summary>
	public bool RightPressed;
	/// <summary>
	/// Menu item index currently under cursor (for hover).
	/// -1 if no item is hovered.
	/// Set by input implementation based on CursorPosition.
	/// </summary>
	public int HoveredItemIndex;
}
/// <summary>
/// Interface for menu input providers.
/// Abstracts keyboard, mouse, gamepad, and VR controller input.
/// </summary>
public interface IMenuInput
{
	/// <summary>
	/// Get the current input state for this frame.
	/// Called once per frame by MenuManager.
	/// </summary>
	/// <returns>Input state snapshot</returns>
	MenuInputState GetState();
	/// <summary>
	/// Update input state (called each frame before GetState).
	/// Allows input implementation to perform per-frame updates.
	/// </summary>
	/// <param name="delta">Time since last frame in seconds</param>
	void Update(float delta);
	/// <summary>
	/// Set the menu item bounding rectangles for hover detection.
	/// Called by MenuRenderer when menu layout changes.
	/// </summary>
	/// <param name="itemBounds">Array of bounding rectangles for each menu item (in viewport coordinates 320x200)</param>
	void SetMenuItemBounds(Rect2[] itemBounds);
}
