using BenMcLean.Wolf3D.Shared.Menu.Input;
using Godot;

namespace BenMcLean.Wolf3D.VR.Menu;

/// <summary>
/// Pointer provider for flatscreen mode.
/// Tracks mouse position and converts to menu viewport coordinates.
/// Uses event-driven input for button detection.
/// </summary>
public class FlatscreenMenuPointerProvider : IMenuPointerProvider
{
	private PointerState _primaryPointer;
	private Vector2 _menuPosition;
	private Vector2 _menuSize;
	private bool _selectPressed;
	private bool _cancelPressed;

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
	public void HandleInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mb && mb.Pressed)
		{
			if (mb.ButtonIndex == MouseButton.Left)
				_selectPressed = true;
			else if (mb.ButtonIndex == MouseButton.Right)
				_cancelPressed = true;
		}
	}

	/// <inheritdoc/>
	public void Update(float delta)
	{
		// Get mouse position in window coordinates
		Vector2I screenPos = DisplayServer.MouseGetPosition();
		Vector2I windowPos = DisplayServer.WindowGetPosition();
		Vector2 mousePos = screenPos - windowPos;

		// Capture and clear button states (they were set by HandleInput)
		bool selectPressed = _selectPressed;
		bool cancelPressed = _cancelPressed;
		_selectPressed = false;
		_cancelPressed = false;

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
