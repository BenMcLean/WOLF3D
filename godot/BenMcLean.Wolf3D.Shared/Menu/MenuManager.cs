using System;
using System.Collections.Generic;
using BenMcLean.Wolf3D.Shared.Menu.Input;
using BenMcLean.Wolf3D.Simulator.Lua;
using Godot;
using Microsoft.Extensions.Logging;
using BenMcLean.Wolf3D.Assets.Gameplay;
using BenMcLean.Wolf3D.Assets.Sound;

namespace BenMcLean.Wolf3D.Shared.Menu;

/// <summary>
/// Manages menu navigation, Lua script execution, and rendering.
/// Orchestrates the entire menu system following the plan's architecture.
/// Phase 1: Basic navigation with Lua function execution.
/// </summary>
public class MenuManager
{
	private readonly MenuCollection _menuCollection;
	private readonly MenuState _sessionState;
	private readonly Config _config;
	private readonly LuaScriptEngine _luaEngine;
	private readonly MenuRenderer _renderer;
	private readonly IMenuInput _input;
	private readonly MenuScriptContext _scriptContext;
	private readonly ILogger _logger;
	private readonly Stack<string> _menuStack = new();
	private string _currentMenuName;
	private int _selectedItemIndex = 0;
	private string _currentMenuMusic = null;
	private IMenuPointerProvider _pointerProvider;
	private Rect2[] _currentMenuItemBounds = [];
	private ClickablePictureBounds[] _currentClickablePictureBounds = [];
	private MenuSequence _activeSequence;
	/// <summary>
	/// Gets the current menu state.
	/// </summary>
	public MenuState SessionState => _sessionState;
	/// <summary>
	/// Gets the script context for setting intermission data.
	/// </summary>
	public MenuScriptContext ScriptContext => _scriptContext;
	/// <summary>
	/// Set when ESC/Cancel is pressed at the root menu (no parent to go back to).
	/// Consumer must clear via <see cref="ClearCancelAtRoot"/>.
	/// </summary>
	public bool CancelAtRootRequested { get; private set; }
	/// <summary>
	/// Optional callback to wrap menu navigations in a fade transition.
	/// The callback receives an Action (the actual navigation work) to execute at mid-fade.
	/// If null, navigations happen immediately without fading.
	/// </summary>
	public Action<Action> FadeTransitionCallback { get; set; }
	/// <summary>
	/// Gets the menu renderer.
	/// </summary>
	public MenuRenderer Renderer => _renderer;
	/// <summary>
	/// Sets the pointer provider for crosshair display.
	/// </summary>
	/// <param name="provider">The pointer provider to use, or null to disable crosshairs.</param>
	public void SetPointerProvider(IMenuPointerProvider provider)
	{
		_pointerProvider = provider;
	}
	/// <summary>
	/// Creates a new MenuManager.
	/// </summary>
	/// <param name="menuCollection">Menu data from AssetManager.MenuCollection</param>
	/// <param name="config">Game configuration</param>
	/// <param name="logger">Logger for debug output</param>
	public MenuManager(
		MenuCollection menuCollection,
		Config config,
		ILogger logger = null)
	{
		_menuCollection = menuCollection ?? throw new ArgumentNullException(nameof(menuCollection));
		_config = config ?? throw new ArgumentNullException(nameof(config));
		_logger = logger;
		// Create session state
		_sessionState = new MenuState();
		// Create Lua engine with MenuScriptContext support only
		_luaEngine = new LuaScriptEngine([typeof(MenuScriptContext)], logger);
		// Compile all menu functions
		CompileMenuFunctions();
		// Create script context (menus don't need RNG/GameClock - not deterministic)
		_scriptContext = new MenuScriptContext(_sessionState, _config, logger)
		{
			// Wire up script context delegates
			NavigateToMenuAction = NavigateToMenu,
			BackToPreviousMenuAction = BackToPreviousMenu,
			CloseAllMenusAction = CloseAllMenus,
			IsGameInProgressFunc = () => false, // TODO: Wire up to actual game state
			ShowConfirmFunc = ShowConfirm,
			ShowMessageAction = ShowMessage,
			RefreshMenuAction = RefreshMenu,
			// Wire up menu item selection and dynamic content
			GetSelectedIndexFunc = () => _selectedItemIndex,
			SetPictureAction = SetPicture,
			// Wire up sound playback
			PlayAdLibSoundAction = PlayAdLibSoundImpl,
			PlayMusicAction = PlayMusicImpl,
			StopMusicAction = StopMusicImpl,
			// Wire up dynamic content updates (for intermission screen)
			SetTextAction = SetText,
			UpdateTickerAction = UpdateTicker,
			// Wire up ticker definition lookup (for sequence step sound configuration)
			GetTickerDefinitionFunc = GetTickerDefinition,
		};
		// Create renderer
		_renderer = new MenuRenderer();
		// Create input handler (keyboard-only for Phase 1)
		_input = new KeyboardMenuInput();
		// Subscribe to music enabled changes to restart menu music when re-enabled
		_config.MusicEnabledChanged += OnMusicEnabledChanged;
		// Navigate to start menu
		if (!string.IsNullOrEmpty(_menuCollection.StartMenu))
			NavigateToMenu(_menuCollection.StartMenu);
		else
			_logger?.LogWarning("No StartMenu defined in MenuCollection");
	}
	/// <summary>
	/// Handles MusicEnabledChanged event to restart menu music when re-enabled.
	/// </summary>
	private void OnMusicEnabledChanged(object sender, EventArgs e)
	{
		// If music is now enabled and we have a current menu music, play it
		if (_config.MusicEnabled && !string.IsNullOrEmpty(_currentMenuMusic))
			PlayMusicImpl(_currentMenuMusic);
	}
	/// <summary>
	/// Compiles all menu functions from the MenuCollection.
	/// Similar to LuaScriptEngine.CompileAllStateFunctions but for menus.
	/// </summary>
	private void CompileMenuFunctions()
	{
		foreach (MenuFunction function in _menuCollection.Functions.Values)
			if (!string.IsNullOrEmpty(function.Code))
				try
				{
					_luaEngine.CompileStateFunction(function.Name, function.Code);
					_logger?.LogDebug("Compiled menu function: {name}", function.Name);
				}
				catch (Exception ex)
				{
					_logger?.LogError(ex, "Failed to compile menu function '{name}'", function.Name);
				}
	}
	/// <summary>
	/// Navigate to a menu by name.
	/// If FadeTransitionCallback is set, wraps the navigation in a fade transition.
	/// Pushes the current menu onto the stack and displays the new menu.
	/// Automatically plays the menu's music if defined.
	/// </summary>
	/// <param name="menuName">Name of menu to navigate to</param>
	public void NavigateToMenu(string menuName)
	{
		if (!_menuCollection.Menus.ContainsKey(menuName))
		{
			_logger?.LogError("Menu '{menuName}' not found", menuName);
			return;
		}
		if (FadeTransitionCallback != null)
		{
			FadeTransitionCallback(() => NavigateToMenuImmediate(menuName));
			return;
		}
		NavigateToMenuImmediate(menuName);
	}
	/// <summary>
	/// Navigate to a menu immediately without fading.
	/// </summary>
	private void NavigateToMenuImmediate(string menuName)
	{
		if (!_menuCollection.Menus.TryGetValue(menuName, out MenuDefinition menuDef))
			return;
		// Push current menu onto stack if not empty
		if (!string.IsNullOrEmpty(_currentMenuName))
			_menuStack.Push(_currentMenuName);
		// Set new current menu
		_currentMenuName = menuName;
		_selectedItemIndex = 0; // Reset to first item
		// Handle menu music
		// null = no music attribute (do nothing), "" = explicit stop, otherwise play
		if (menuDef.Music != null)
		{
			if (menuDef.Music == "")
			{
				_currentMenuMusic = null;
				_scriptContext.StopMusic();
			}
			else
			{
				_currentMenuMusic = menuDef.Music;
				_scriptContext.PlayMusic(menuDef.Music);
			}
		}
		// Render the menu
		RefreshMenu();
		// Create a sequence for OnShow to queue steps into
		MenuSequence sequence = new();
		_scriptContext.ActiveSequence = sequence;
		// Execute OnShow script if defined (runs once when menu first appears)
		// OnShow may queue sequence steps (StartTicker, QueueDelay, QueueWaitForInput)
		ExecuteOnShow(menuDef);
		_scriptContext.ActiveSequence = null;
		// Queue Pause elements after OnShow steps (tickers/delays run first, then pauses)
		if (menuDef.Pauses != null)
		{
			foreach (Assets.Gameplay.MenuPauseDefinition pauseDef in menuDef.Pauses)
			{
				// Capture pauseDef for the closure
				Assets.Gameplay.MenuPauseDefinition captured = pauseDef;
				float? duration = captured.Duration.HasValue
					? (float)captured.Duration.Value.TotalSeconds
					: null;
				sequence.Enqueue(new PauseSequenceStep(duration, () =>
				{
					if (!string.IsNullOrEmpty(captured.Script))
						ExecutePauseScript(captured.Script);
				}));
			}
		}
		// Activate the sequence if it has steps, otherwise discard
		_activeSequence = sequence.HasSteps ? sequence : null;
		_logger?.LogDebug("Navigated to menu: {menuName}", menuName);
	}
	/// <summary>
	/// Navigate back to the previous menu.
	/// If FadeTransitionCallback is set, wraps the navigation in a fade transition.
	/// Pops the menu stack.
	/// </summary>
	public void BackToPreviousMenu()
	{
		if (_menuStack.Count == 0)
		{
			CancelAtRootRequested = true;
			return;
		}
		if (FadeTransitionCallback != null)
		{
			FadeTransitionCallback(BackToPreviousMenuImmediate);
			return;
		}
		BackToPreviousMenuImmediate();
	}
	/// <summary>
	/// Navigate back immediately without fading.
	/// </summary>
	private void BackToPreviousMenuImmediate()
	{
		if (_menuStack.Count > 0)
		{
			_currentMenuName = _menuStack.Pop();
			_selectedItemIndex = 0;
			RefreshMenu();
			_logger?.LogDebug("Navigated back to menu: {menuName}", _currentMenuName);
		}
	}
	/// <summary>
	/// Close all menus and return to game.
	/// </summary>
	public void CloseAllMenus()
	{
		_menuStack.Clear();
		_currentMenuName = null;
		_logger?.LogDebug("Closed all menus");
		// TODO: Signal to Root.cs or game manager that menus are closed
	}
	/// <summary>
	/// Refresh the current menu display.
	/// Renders the menu with current state.
	/// Also executes OnSelectionChanged script if defined.
	/// </summary>
	public void RefreshMenu()
	{
		if (string.IsNullOrEmpty(_currentMenuName))
			return;
		if (!_menuCollection.Menus.TryGetValue(_currentMenuName, out MenuDefinition menuDef))
			return;
		_renderer.RenderMenu(menuDef, _selectedItemIndex);
		// Update input with menu item bounds for hover detection
		_currentMenuItemBounds = _renderer.GetMenuItemBounds();
		_currentClickablePictureBounds = _renderer.GetClickablePictureBounds();
		_input.SetMenuItemBounds(_currentMenuItemBounds);
		// Execute OnSelectionChanged script if defined (WL_MENU.C:1518 - HandleMenu callback)
		ExecuteOnSelectionChanged(menuDef);
	}
	/// <summary>
	/// Execute the OnSelectionChanged Lua script for a menu.
	/// Matches original Wolf3D's DrawNewGameDiff callback in HandleMenu.
	/// WL_MENU.C:1518: which=HandleMenu(&NewItems,&NewMenu[0],DrawNewGameDiff);
	/// </summary>
	/// <param name="menuDef">Menu definition containing OnSelectionChanged script</param>
	private void ExecuteOnSelectionChanged(MenuDefinition menuDef)
	{
		if (string.IsNullOrEmpty(menuDef.OnSelectionChanged))
			return;
		try
		{
			_luaEngine.DoString(menuDef.OnSelectionChanged, _scriptContext);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"ERROR: Failed to execute OnSelectionChanged for menu '{menuDef.Name}': {ex.Message}");
			_logger?.LogError(ex, "Failed to execute OnSelectionChanged for menu '{name}'", menuDef.Name);
		}
	}
	/// <summary>
	/// Execute the OnShow Lua script for a menu.
	/// Runs once when a menu first appears (not on refresh).
	/// Used by intermission screen to set up initial values and start tickers.
	/// </summary>
	/// <param name="menuDef">Menu definition containing OnShow script</param>
	private void ExecuteOnShow(MenuDefinition menuDef)
	{
		if (string.IsNullOrEmpty(menuDef.OnShow))
			return;
		try
		{
			_luaEngine.DoString(menuDef.OnShow, _scriptContext);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"ERROR: Failed to execute OnShow for menu '{menuDef.Name}': {ex.Message}");
			_logger?.LogError(ex, "Failed to execute OnShow for menu '{name}'", menuDef.Name);
		}
	}
	/// <summary>
	/// Execute a Pause element's Lua script.
	/// Called when a PauseSequenceStep completes (button pressed or timeout).
	/// </summary>
	/// <param name="script">Lua script to execute</param>
	private void ExecutePauseScript(string script)
	{
		try
		{
			_luaEngine.DoString(script, _scriptContext);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"ERROR: Failed to execute Pause script: {ex.Message}");
			_logger?.LogError(ex, "Failed to execute Pause script");
		}
	}
	/// <summary>
	/// Update a picture in the current menu's Pictures list.
	/// Used by OnSelectionChanged scripts to change pictures dynamically (e.g., difficulty faces).
	/// </summary>
	/// <param name="pictureName">Name of the VgaGraph picture</param>
	/// <param name="pictureIndex">Index of the picture to update in the Pictures list</param>
	private void SetPicture(string pictureName, int pictureIndex)
	{
		if (string.IsNullOrEmpty(_currentMenuName))
			return;
		if (!_menuCollection.Menus.TryGetValue(_currentMenuName, out MenuDefinition menuDef))
			return;
		if (pictureIndex < 0 || pictureIndex >= menuDef.Pictures.Count)
		{
			_logger?.LogWarning("SetPicture: Picture index {index} out of range for menu '{menu}'", pictureIndex, _currentMenuName);
			return;
		}
		// Update the picture definition
		menuDef.Pictures[pictureIndex].Name = pictureName;
		// Re-render the menu to show the updated picture
		_renderer.RenderMenu(menuDef, _selectedItemIndex);
	}
	/// <summary>
	/// Update menu system (called each frame).
	/// Handles input and executes menu actions.
	/// </summary>
	/// <param name="delta">Time since last frame in seconds</param>
	/// <summary>
	/// Clears the <see cref="CancelAtRootRequested"/> flag after the consumer has acted on it.
	/// </summary>
	public void ClearCancelAtRoot() => CancelAtRootRequested = false;

	public void Update(float delta)
	{
		if (string.IsNullOrEmpty(_currentMenuName))
			return;
		if (!_menuCollection.Menus.TryGetValue(_currentMenuName, out MenuDefinition menuDef))
			return;
		// Update animated pictures (e.g., BJ breathing on intermission screen)
		_renderer.UpdateAnimations(delta);
		// Update pointer provider and crosshairs
		if (_pointerProvider != null)
		{
			_pointerProvider.Update(delta);
			_renderer.SetPointers(_pointerProvider.PrimaryPointer, _pointerProvider.SecondaryPointer);
			_renderer.UpdateCrosshairs();
		}
		// Update input
		_input.Update(delta);
		MenuInputState inputState = _input.GetState();
		// Check for "any button" from pointers as well
		bool anyButtonPressed = inputState.AnyButtonPressed;
		if (_pointerProvider != null)
		{
			Input.PointerState primary = _pointerProvider.PrimaryPointer;
			Input.PointerState secondary = _pointerProvider.SecondaryPointer;
			if (primary.SelectPressed || primary.CancelPressed ||
				secondary.SelectPressed || secondary.CancelPressed)
				anyButtonPressed = true;
		}
		// If a presentation sequence is active, process it instead of normal menu input
		if (_activeSequence != null && !_activeSequence.IsComplete)
		{
			_activeSequence.Update(delta, anyButtonPressed);
			// Sequence callback (e.g., Pause script) may navigate to a new menu,
			// which replaces _activeSequence. Null-check before accessing.
			if (_activeSequence != null && _activeSequence.IsComplete)
				_activeSequence = null;
			return;
		}
		// Normal interactive menu mode
		if (_pointerProvider != null)
			HandlePointerHover(menuDef);
		// Handle navigation
		if (inputState.UpPressed && _selectedItemIndex > 0)
		{
			_selectedItemIndex--;
			PlayCursorMoveSound(menuDef);
			RefreshMenu();
		}
		else if (inputState.DownPressed && _selectedItemIndex < menuDef.Items.Count - 1)
		{
			_selectedItemIndex++;
			PlayCursorMoveSound(menuDef);
			RefreshMenu();
		}
		// Handle selection
		if (inputState.SelectPressed && menuDef.Items.Count > 0)
			ExecuteMenuItemAction(menuDef.Items[_selectedItemIndex]);
		// Handle cancel/back
		if (inputState.CancelPressed)
			BackToPreviousMenu();
	}
	/// <summary>
	/// Handle pointer-based hover selection and button presses.
	/// Updates selection when a pointer is over a different menu item than currently selected.
	/// Handles select (click/trigger) only when pointer is over an item.
	/// Handles cancel (right-click/grip) regardless of position.
	/// </summary>
	/// <param name="menuDef">Current menu definition</param>
	private void HandlePointerHover(MenuDefinition menuDef)
	{
		Input.PointerState primary = _pointerProvider.PrimaryPointer;
		Input.PointerState secondary = _pointerProvider.SecondaryPointer;

		// Check for cancel from either pointer (works regardless of position)
		if (primary.CancelPressed || secondary.CancelPressed)
		{
			BackToPreviousMenu();
			return;
		}

		// Check primary pointer for hover and select
		if (primary.IsActive)
		{
			// Check for clickable picture clicks first
			if (primary.SelectPressed)
			{
				int clickedPicIndex = FindClickedPictureIndex(primary.Position);
				if (clickedPicIndex >= 0)
				{
					ExecutePictureScript(menuDef.Pictures[clickedPicIndex]);
					return;
				}
			}
			int hoveredIndex = FindHoveredMenuItemIndex(primary.Position);
			if (hoveredIndex >= 0)
			{
				// Update selection if hovering over a different item
				if (hoveredIndex != _selectedItemIndex)
				{
					_selectedItemIndex = hoveredIndex;
					PlayCursorMoveSound(menuDef);
					RefreshMenu();
				}
				// Handle select only if pointer is over an item
				if (primary.SelectPressed)
				{
					ExecuteMenuItemAction(menuDef.Items[hoveredIndex]);
					return;
				}
			}
		}

		// Check secondary pointer for hover and select
		if (secondary.IsActive)
		{
			// Check for clickable picture clicks first
			if (secondary.SelectPressed)
			{
				int clickedPicIndex = FindClickedPictureIndex(secondary.Position);
				if (clickedPicIndex >= 0)
				{
					ExecutePictureScript(menuDef.Pictures[clickedPicIndex]);
					return;
				}
			}
			int hoveredIndex = FindHoveredMenuItemIndex(secondary.Position);
			if (hoveredIndex >= 0)
			{
				// Update selection if hovering over a different item
				if (hoveredIndex != _selectedItemIndex)
				{
					_selectedItemIndex = hoveredIndex;
					PlayCursorMoveSound(menuDef);
					RefreshMenu();
				}
				// Handle select only if pointer is over an item
				if (secondary.SelectPressed)
				{
					ExecuteMenuItemAction(menuDef.Items[hoveredIndex]);
					return;
				}
			}
		}
	}
	/// <summary>
	/// Find the menu item index at the given position.
	/// </summary>
	/// <param name="position">Position in viewport coordinates (320x200)</param>
	/// <returns>Menu item index, or -1 if no item at position</returns>
	private int FindHoveredMenuItemIndex(Vector2 position)
	{
		for (int i = 0; i < _currentMenuItemBounds.Length; i++)
			if (_currentMenuItemBounds[i].HasPoint(position))
				return i;
		return -1;
	}
	/// <summary>
	/// Execute a menu item's inline Lua script.
	/// Uses DoString to execute script directly (not pre-cached as bytecode).
	/// </summary>
	/// <param name="item">Menu item to execute</param>
	private void ExecuteMenuItemAction(MenuItemDefinition item)
	{
		if (string.IsNullOrEmpty(item.Script))
			return;

		try
		{
			_luaEngine.DoString(item.Script, _scriptContext);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"ERROR: Failed to execute menu script for '{item.Text}': {ex.Message}");
			_logger?.LogError(ex, "Failed to execute menu script for '{text}'", item.Text);
		}
	}
	/// <summary>
	/// Find the clickable picture index at the given position.
	/// </summary>
	/// <param name="position">Position in viewport coordinates (320x200)</param>
	/// <returns>Index into MenuDefinition.Pictures, or -1 if no clickable picture at position</returns>
	private int FindClickedPictureIndex(Vector2 position)
	{
		for (int i = 0; i < _currentClickablePictureBounds.Length; i++)
			if (_currentClickablePictureBounds[i].Bounds.HasPoint(position))
				return _currentClickablePictureBounds[i].PictureIndex;
		return -1;
	}
	/// <summary>
	/// Execute a picture's inline Lua script.
	/// Uses DoString to execute script directly (same pattern as ExecuteMenuItemAction).
	/// </summary>
	/// <param name="pictureDef">Picture definition containing the script</param>
	private void ExecutePictureScript(MenuPictureDefinition pictureDef)
	{
		if (string.IsNullOrEmpty(pictureDef.Script))
			return;
		try
		{
			_luaEngine.DoString(pictureDef.Script, _scriptContext);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"ERROR: Failed to execute picture script for '{pictureDef.Name}': {ex.Message}");
			_logger?.LogError(ex, "Failed to execute picture script for '{name}'", pictureDef.Name);
		}
	}
	/// <summary>
	/// Play the cursor move sound for the given menu.
	/// Uses the menu's CursorMoveSound if defined, otherwise silent.
	/// </summary>
	/// <param name="menuDef">Menu definition containing sound name</param>
	private void PlayCursorMoveSound(MenuDefinition menuDef)
	{
		if (!string.IsNullOrEmpty(menuDef.CursorMoveSound))
			_scriptContext.PlayAdLibSound(menuDef.CursorMoveSound);
	}

	/// <summary>
	/// Update a named text label's content.
	/// Called from Lua via SetText(name, value).
	/// </summary>
	/// <param name="name">The Name attribute of the text element</param>
	/// <param name="value">New text content</param>
	private void SetText(string name, string value)
	{
		_renderer.UpdateText(name, value);
	}
	/// <summary>
	/// Update a ticker label's displayed value.
	/// Called from Lua via UpdateTicker(name, value).
	/// </summary>
	/// <param name="name">The Name of the ticker element</param>
	/// <param name="value">New display value</param>
	private void UpdateTicker(string name, string value)
	{
		if (string.IsNullOrEmpty(_currentMenuName))
			return;
		if (!_menuCollection.Menus.TryGetValue(_currentMenuName, out MenuDefinition menuDef))
			return;
		_renderer.UpdateTicker(name, value, menuDef);
	}
	/// <summary>
	/// Look up a ticker definition by name from the current menu.
	/// Used by MenuScriptContext to get sound configuration when creating TickerSequenceSteps.
	/// </summary>
	/// <param name="name">Ticker name (e.g., "KillRatio")</param>
	/// <returns>The ticker definition, or null if not found</returns>
	private MenuTickerDefinition GetTickerDefinition(string name)
	{
		if (string.IsNullOrEmpty(_currentMenuName))
			return null;
		if (!_menuCollection.Menus.TryGetValue(_currentMenuName, out MenuDefinition menuDef))
			return null;
		if (menuDef.Tickers == null)
			return null;
		for (int i = 0; i < menuDef.Tickers.Count; i++)
			if (menuDef.Tickers[i].Name == name)
				return menuDef.Tickers[i];
		return null;
	}
	#region Sound Playback Implementation
	/// <summary>
	/// Play an AdLib sound effect by name.
	/// Looks up the sound in the current game's AudioT and plays it via SoundBlaster.
	/// </summary>
	/// <param name="soundName">Name of the AdLib sound (e.g., "MOVEGUN2SND")</param>
	private void PlayAdLibSoundImpl(string soundName)
	{
		if (SharedAssetManager.CurrentGame?.AudioT?.Sounds == null)
		{
			_logger?.LogWarning("Cannot play AdLib sound '{soundName}' - no AudioT loaded", soundName);
			return;
		}

		EventBus.Emit(GameEvent.PlaySound, soundName);
	}

	/// <summary>
	/// Play background music by name.
	/// </summary>
	/// <param name="musicName">Name of the music track</param>
	private void PlayMusicImpl(string musicName)
	{
		if (SharedAssetManager.CurrentGame?.AudioT?.Songs == null)
		{
			_logger?.LogWarning("Cannot play music '{musicName}' - no AudioT loaded", musicName);
			return;
		}

		EventBus.Emit(GameEvent.PlayMusic, musicName);
	}

	/// <summary>
	/// Stop currently playing music.
	/// </summary>
	private void StopMusicImpl()
	{
		_currentMenuMusic = null;
		EventBus.Emit(GameEvent.StopMusic);
	}
	#endregion Sound Playback Implementation

	#region UI Dialog Helpers
	/// <summary>
	/// Show a confirmation dialog (Yes/No).
	/// Phase 1: Placeholder implementation.
	/// </summary>
	/// <param name="message">Message to display</param>
	/// <returns>True if user confirmed, false if cancelled</returns>
	private bool ShowConfirm(string message)
	{
		// TODO: Implement actual confirmation dialog
		_logger?.LogDebug("ShowConfirm: {message} (TODO: implement)", message);
		return true; // Placeholder: always confirm
	}
	/// <summary>
	/// Show an informational message.
	/// Phase 1: Placeholder implementation.
	/// </summary>
	/// <param name="message">Message to display</param>
	private void ShowMessage(string message)
	{
		// TODO: Implement actual message dialog
		_logger?.LogDebug("ShowMessage: {message} (TODO: implement)", message);
	}
	#endregion UI Dialog Helpers
}
