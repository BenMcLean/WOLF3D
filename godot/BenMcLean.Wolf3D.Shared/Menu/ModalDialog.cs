using BenMcLean.Wolf3D.Shared.Menu.Input;

namespace BenMcLean.Wolf3D.Shared.Menu;

/// <summary>
/// Represents a modal confirmation dialog (Yes/No).
/// Shown over the menu when the user tries to quit or end the current game.
/// WL_MENU.C:Confirm() — draws a message box and waits for Y or N keypress.
/// </summary>
/// <remarks>
/// Creates a new modal dialog with the given message and kind.
/// </remarks>
/// <param name="message">Message to display. May contain newlines.</param>
/// <param name="kind">The action being confirmed (Quit or EndGame).</param>
public class ModalDialog(string message, ModalDialog.ModalKind kind)
{
	/// <summary>
	/// Possible outcomes of a modal dialog.
	/// </summary>
	public enum ModalResult { Pending, Confirmed, Cancelled }

	/// <summary>
	/// The action that will be taken on confirmation.
	/// Allows MenuManager to know which flag to set when confirmed.
	/// </summary>
	public enum ModalKind { Quit, EndGame, Message }

	/// <summary>
	/// Message text to display in the dialog. May contain newlines.
	/// </summary>
	public string Message { get; } = message;

	/// <summary>
	/// What action this modal is confirming.
	/// </summary>
	public ModalKind Kind { get; } = kind;

	/// <summary>
	/// Current result of the dialog.
	/// Pending until the user presses Y/N or clicks Yes/No.
	/// </summary>
	public ModalResult Result { get; private set; } = ModalResult.Pending;

	/// <summary>
	/// Whether the dialog is still waiting for input.
	/// </summary>
	public bool IsPending => Result == ModalResult.Pending;

	// Manual edge detection for Y/N keys (not in Godot's default input map).
	// Tracks whether each key was held on the previous HandleInput call so we only
	// respond to the rising edge — matching IsActionJustPressed behaviour.
	private bool _yWasPressed, _nWasPressed;

	/// <summary>
	/// Bounding rect of the "Yes" button in 320x200 viewport coordinates.
	/// Set by MenuRenderer after laying out the dialog. Used for pointer hit-testing.
	/// </summary>
	public Godot.Rect2 YesButtonBounds { get; set; }

	/// <summary>
	/// Bounding rect of the "No" button in 320x200 viewport coordinates.
	/// Set by MenuRenderer after laying out the dialog. Used for pointer hit-testing.
	/// </summary>
	public Godot.Rect2 NoButtonBounds { get; set; }

	/// <summary>
	/// Confirm the dialog (user pressed Y or clicked Yes).
	/// WL_MENU.C:Confirm - sc_Y branch → xit=1, ShootSnd()
	/// </summary>
	public void Confirm() => Result = ModalResult.Confirmed;

	/// <summary>
	/// Cancel the dialog (user pressed N, Escape, or clicked No).
	/// WL_MENU.C:Confirm - sc_N / sc_Escape branch → xit=0
	/// </summary>
	public void Cancel() => Result = ModalResult.Cancelled;

	/// <summary>
	/// Process keyboard and pointer input for this dialog.
	/// Should be called each frame while the dialog is pending.
	/// Uses IsKeyJustPressed (edge-triggered) so a key held when the modal opened
	/// does not immediately dismiss it — the player must release and press again.
	/// WL_MENU.C:IN_ClearKeysDown() equivalent behaviour.
	/// </summary>
	/// <param name="inputState">Current keyboard/controller input state.</param>
	/// <param name="primary">Primary pointer state (mouse or right VR controller).</param>
	/// <param name="secondary">Secondary pointer state (left VR controller).</param>
	public void HandleInput(
		MenuInputState inputState,
		Input.PointerState primary,
		Input.PointerState secondary)
	{
		if (Result != ModalResult.Pending)
			return;

		// WL_MENU.C:Message() — dismiss on any input; no Yes/No distinction.
		if (Kind == ModalKind.Message)
		{
			if (inputState.AnyButtonPressed || primary.SelectPressed || secondary.SelectPressed)
				Confirm();
			return;
		}

		// WL_MENU.C:Confirm loop - Keyboard[sc_Y], Keyboard[sc_N], Keyboard[sc_Escape]
		// inputState uses IsActionJustPressed (edge-triggered), so Escape via ui_cancel
		// and Enter/Space via ui_accept do not fire when held from a prior action.
		// Y/N use manual edge detection for the same reason.
		// Controller: select (trigger/face buttons) = confirm; cancel (grip) = cancel
		bool yIsPressed = Godot.Input.IsKeyPressed(Godot.Key.Y),
			nIsPressed = Godot.Input.IsKeyPressed(Godot.Key.N),
			yJustPressed = yIsPressed && !_yWasPressed,
			nJustPressed = nIsPressed && !_nWasPressed;
		_yWasPressed = yIsPressed;
		_nWasPressed = nIsPressed;
		if (yJustPressed || inputState.SelectPressed)
		{
			Confirm();
			return;
		}
		if (nJustPressed || inputState.CancelPressed)
		{
			Cancel();
			return;
		}

		// Pointer: click on Yes or No button
		if (primary.SelectPressed)
		{
			if (YesButtonBounds.HasPoint(primary.Position))
			{
				Confirm();
				return;
			}
			if (NoButtonBounds.HasPoint(primary.Position))
			{
				Cancel();
				return;
			}
		}
		if (secondary.SelectPressed)
		{
			if (YesButtonBounds.HasPoint(secondary.Position))
			{
				Confirm();
				return;
			}
			if (NoButtonBounds.HasPoint(secondary.Position))
			{
				Cancel();
				return;
			}
		}
	}
}
