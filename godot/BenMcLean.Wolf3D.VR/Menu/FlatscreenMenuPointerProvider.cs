using BenMcLean.Wolf3D.Shared.Menu.Input;
using Godot;

namespace BenMcLean.Wolf3D.VR.Menu;

/// <summary>
/// Pointer provider for flatscreen mode.
/// Tracks mouse position and converts to menu viewport coordinates.
/// </summary>
public class FlatscreenMenuPointerProvider : IMenuPointerProvider
{
	private PointerState _primaryPointer;
	private Vector2 _menuPosition;
	private Vector2 _menuSize;
	private bool _wasLeftPressed;
	private bool _wasRightPressed;

	/// <inheritdoc/>
	public PointerState PrimaryPointer => _primaryPointer;

	/// <inheritdoc/>
	public PointerState SecondaryPointer => default; // No secondary pointer in flatscreen mode

	/// <summary>
	/// Sets the menu display area in screen coordinates.
	/// Used to convert mouse position to menu viewport coordinates.
	/// </summary>
	/// <param name="position">Top-left corner of the menu display area.</param>
	/// <param name="size">Size of the menu display area.</param>
	public void SetMenuDisplayArea(Vector2 position, Vector2 size)
	{
		_menuPosition = position;
		_menuSize = size;
	}

	/// <inheritdoc/>
	public void Update(float delta)
	{
		// Get mouse position in window coordinates
		// DisplayServer.MouseGetPosition() returns global screen coords
		// Subtract window position to get window-local coordinates
		Vector2I screenPos = DisplayServer.MouseGetPosition();
		Vector2I windowPos = DisplayServer.WindowGetPosition();
		Vector2 mousePos = screenPos - windowPos;

		// Check mouse button states using direct mouse button detection
		bool selectPressed = Input.IsMouseButtonPressed(MouseButton.Left) && !_wasLeftPressed;
		bool cancelPressed = Input.IsMouseButtonPressed(MouseButton.Right) && !_wasRightPressed;
		_wasLeftPressed = Input.IsMouseButtonPressed(MouseButton.Left);
		_wasRightPressed = Input.IsMouseButtonPressed(MouseButton.Right);

		// Check if mouse is within the menu display area
		if (mousePos.X < _menuPosition.X || mousePos.X >= _menuPosition.X + _menuSize.X ||
			mousePos.Y < _menuPosition.Y || mousePos.Y >= _menuPosition.Y + _menuSize.Y)
		{
			// Mouse is outside menu area - still report cancel press
			_primaryPointer = new PointerState
			{
				IsActive = false,
				CancelPressed = cancelPressed
			};
			return;
		}

		// Convert to menu viewport coordinates (0-320 x 0-200)
		Vector2 relativePos = mousePos - _menuPosition;
		Vector2 viewportPos = new(
			relativePos.X / _menuSize.X * 320f,
			relativePos.Y / _menuSize.Y * 200f
		);

		_primaryPointer = new PointerState
		{
			IsActive = true,
			Position = viewportPos,
			SelectPressed = selectPressed,
			CancelPressed = cancelPressed
		};
	}
}
