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
	/// <summary>
	/// Gets the current menu state.
	/// </summary>
	public MenuState SessionState => _sessionState;
	/// <summary>
	/// Gets the menu renderer.
	/// </summary>
	public MenuRenderer Renderer => _renderer;
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
			// Wire up sound playback
			PlayAdLibSoundAction = PlayAdLibSoundImpl,
			PlayMusicAction = PlayMusicImpl,
			StopMusicAction = StopMusicImpl,
		};
		// Create renderer
		_renderer = new MenuRenderer();
		// Create input handler (keyboard-only for Phase 1)
		_input = new KeyboardMenuInput();
		// Navigate to start menu
		if (!string.IsNullOrEmpty(_menuCollection.StartMenu))
			NavigateToMenu(_menuCollection.StartMenu);
		else
			_logger?.LogWarning("No StartMenu defined in MenuCollection");
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
	/// Pushes the current menu onto the stack and displays the new menu.
	/// Automatically plays the menu's music if defined.
	/// </summary>
	/// <param name="menuName">Name of menu to navigate to</param>
	public void NavigateToMenu(string menuName)
	{
		if (!_menuCollection.Menus.TryGetValue(menuName, out MenuDefinition menuDef))
		{
			_logger?.LogError("Menu '{menuName}' not found", menuName);
			return;
		}
		// Push current menu onto stack if not empty
		if (!string.IsNullOrEmpty(_currentMenuName))
			_menuStack.Push(_currentMenuName);
		// Set new current menu
		_currentMenuName = menuName;
		_selectedItemIndex = 0; // Reset to first item
		// Play menu music if defined
		if (!string.IsNullOrEmpty(menuDef.Music))
			_scriptContext.PlayMusic(menuDef.Music);
		// Render the menu
		RefreshMenu();
		_logger?.LogDebug("Navigated to menu: {menuName}", menuName);
	}
	/// <summary>
	/// Navigate back to the previous menu.
	/// Pops the menu stack.
	/// </summary>
	public void BackToPreviousMenu()
	{
		if (_menuStack.Count > 0)
		{
			_currentMenuName = _menuStack.Pop();
			_selectedItemIndex = 0;
			RefreshMenu();
			_logger?.LogDebug("Navigated back to menu: {menuName}", _currentMenuName);
		}
		else
			_logger?.LogDebug("No previous menu to navigate to");
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
	/// </summary>
	public void RefreshMenu()
	{
		if (string.IsNullOrEmpty(_currentMenuName))
			return;
		if (!_menuCollection.Menus.TryGetValue(_currentMenuName, out MenuDefinition menuDef))
			return;
		_renderer.RenderMenu(menuDef, _selectedItemIndex);
		// Update input with menu item positions for hover detection
		Vector2[] itemPositions = _renderer.GetMenuItemPositions();
		_input.SetMenuItemPositions(itemPositions);
	}
	/// <summary>
	/// Update menu system (called each frame).
	/// Handles input and executes menu actions.
	/// </summary>
	/// <param name="delta">Time since last frame in seconds</param>
	public void Update(float delta)
	{
		if (string.IsNullOrEmpty(_currentMenuName))
		{
			GD.Print("DEBUG: Update() - _currentMenuName is null or empty");
			return;
		}
		if (!_menuCollection.Menus.TryGetValue(_currentMenuName, out MenuDefinition menuDef))
		{
			GD.Print($"DEBUG: Update() - Menu '{_currentMenuName}' not found in collection");
			return;
		}
		// Update input
		_input.Update(delta);
		MenuInputState inputState = _input.GetState();
		// Handle navigation
		if (inputState.UpPressed && _selectedItemIndex > 0)
		{
			GD.Print($"DEBUG: Up pressed, changing selection from {_selectedItemIndex} to {_selectedItemIndex - 1}");
			_selectedItemIndex--;
			PlayCursorMoveSound(menuDef);
			RefreshMenu();
		}
		else if (inputState.DownPressed && _selectedItemIndex < menuDef.Items.Count - 1)
		{
			GD.Print($"DEBUG: Down pressed, changing selection from {_selectedItemIndex} to {_selectedItemIndex + 1}");
			_selectedItemIndex++;
			PlayCursorMoveSound(menuDef);
			RefreshMenu();
		}
		// Handle selection
		if (inputState.SelectPressed)
		{
			GD.Print($"DEBUG: Select pressed, executing action for item {_selectedItemIndex}: {menuDef.Items[_selectedItemIndex].Text}");
			ExecuteMenuItemAction(menuDef.Items[_selectedItemIndex]);
		}
		// Handle cancel/back
		if (inputState.CancelPressed)
		{
			GD.Print("DEBUG: Cancel pressed, going back");
			BackToPreviousMenu();
		}
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
	/// Play the cursor move sound for the given menu.
	/// Uses the menu's CursorMoveSound if defined, otherwise silent.
	/// </summary>
	/// <param name="menuDef">Menu definition containing sound name</param>
	private void PlayCursorMoveSound(MenuDefinition menuDef)
	{
		if (!string.IsNullOrEmpty(menuDef.CursorMoveSound))
			_scriptContext.PlayAdLibSound(menuDef.CursorMoveSound);
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

		if (SharedAssetManager.CurrentGame.AudioT.Sounds.TryGetValue(soundName, out Adl adl))
		{
			OPL.SoundBlaster.Adl = adl;
		}
		else
		{
			_logger?.LogWarning("AdLib sound '{soundName}' not found in AudioT", soundName);
		}
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

		if (SharedAssetManager.CurrentGame.AudioT.Songs.TryGetValue(musicName, out AudioT.Music song))
		{
			OPL.SoundBlaster.Music = song;
		}
		else
		{
			_logger?.LogWarning("Music '{musicName}' not found in AudioT", musicName);
		}
	}

	/// <summary>
	/// Stop currently playing music.
	/// </summary>
	private void StopMusicImpl()
	{
		OPL.SoundBlaster.Music = null;
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
