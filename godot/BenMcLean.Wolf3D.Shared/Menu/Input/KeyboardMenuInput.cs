using Godot;

namespace BenMcLean.Wolf3D.Shared.Menu.Input;

/// <summary>
/// Keyboard-based menu input implementation.
/// Provides traditional arrow key + Enter/Escape navigation.
/// This is the basic input mode for Phase 1.
/// Mouse and VR input will be added in later phases.
/// </summary>
public class KeyboardMenuInput : IMenuInput
{
	private Rect2[] _menuItemBounds = [];
	/// <summary>
	/// Get the current input state for this frame.
	/// Reads keyboard input via Godot's Input singleton.
	/// </summary>
	/// <returns>Input state snapshot</returns>
	public MenuInputState GetState() => new()
	{
		CursorPosition = Vector2.Zero,
		SelectPressed = Godot.Input.IsActionJustPressed("ui_accept"),
		CancelPressed = Godot.Input.IsActionJustPressed("ui_cancel"),
		UpPressed = Godot.Input.IsActionJustPressed("ui_up"),
		DownPressed = Godot.Input.IsActionJustPressed("ui_down"),
		LeftPressed = Godot.Input.IsActionJustPressed("ui_left"),
		RightPressed = Godot.Input.IsActionJustPressed("ui_right"),
		HoveredItemIndex = -1
	};
	/// <summary>
	/// Update input state (currently no per-frame updates needed for keyboard).
	/// </summary>
	/// <param name="delta">Time since last frame in seconds</param>
	public void Update(float delta)
	{
		// Keyboard input doesn't require per-frame updates
		// All state is read directly from Godot.Input in GetState()
	}
	/// <summary>
	/// Set the menu item bounding rectangles for hover detection.
	/// Not used for keyboard-only input, but stored for future mouse integration.
	/// </summary>
	/// <param name="itemBounds">Array of bounding rectangles for each menu item</param>
	public void SetMenuItemBounds(Rect2[] itemBounds)
	{
		_menuItemBounds = itemBounds;
	}
}
