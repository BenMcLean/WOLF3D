using System;
using BenMcLean.Wolf3D.Assets.Gameplay;
using Microsoft.Extensions.Logging;

namespace BenMcLean.Wolf3D.Shared.Menu;

/// <summary>
/// Script context for menu Lua functions.
/// Extends BaseScriptContext with menu-specific API for navigation, settings, and user interaction.
/// Does NOT include RNG/GameClock - menus don't require determinism.
/// All public methods are automatically exposed to Lua via reflection.
/// </summary>
/// <remarks>
/// Creates a new MenuScriptContext.
/// </remarks>
/// <param name="sessionState">Menu session state (episode/difficulty selections)</param>
/// <param name="config">Game configuration (sound/music/input settings)</param>
/// <param name="logger">Logger for debug output</param>
public class MenuScriptContext(
	MenuState sessionState,
	Config config,
	ILogger logger = null) : Simulator.Lua.BaseScriptContext(logger)
{
	// Sound methods (PlayDigiSound, PlayAdLibSound, PlayMusic, StopMusic) inherited from BaseScriptContext
	#region Logging
	/// <summary>
	/// Log a debug message from Lua script.
	/// Routes to Microsoft.Extensions.Logging.
	/// </summary>
	/// <param name="message">Message to log</param>
	public void Log(string message) => _logger?.LogDebug("Menu Lua: {message}", message);
	#endregion Logging
	#region Navigation (to be implemented by MenuManager)
	// These methods will be wired up by MenuManager after construction
	// Using delegates allows MenuManager to inject its navigation logic
	/// <summary>
	/// Delegate for navigating to a menu by name.
	/// Set by MenuManager after context creation.
	/// </summary>
	public Action<string> NavigateToMenuAction { get; set; }
	/// <summary>
	/// Delegate for navigating back to previous menu.
	/// Set by MenuManager after context creation.
	/// </summary>
	public Action BackToPreviousMenuAction { get; set; }
	/// <summary>
	/// Delegate for closing all menus and returning to game.
	/// Set by MenuManager after context creation.
	/// </summary>
	public Action CloseAllMenusAction { get; set; }
	/// <summary>
	/// Delegate for checking if game is in progress.
	/// Set by MenuManager after context creation.
	/// </summary>
	public Func<bool> IsGameInProgressFunc { get; set; }
	/// <summary>
	/// Navigate to a menu by name.
	/// Exposed to Lua. Delegates to MenuManager.
	/// </summary>
	/// <param name="menuName">Name of menu to navigate to (e.g., "Main", "Episodes")</param>
	public void NavigateToMenu(string menuName) => NavigateToMenuAction?.Invoke(menuName);
	/// <summary>
	/// Navigate back to the previous menu.
	/// Exposed to Lua. Delegates to MenuManager.
	/// </summary>
	public void BackToPreviousMenu() => BackToPreviousMenuAction?.Invoke();
	/// <summary>
	/// Close all menus and return to game.
	/// Exposed to Lua. Delegates to MenuManager.
	/// </summary>
	public void CloseAllMenus() => CloseAllMenusAction?.Invoke();
	/// <summary>
	/// Check if a game is currently in progress.
	/// Exposed to Lua. Delegates to MenuManager.
	/// </summary>
	/// <returns>True if game is running, false if at title screen</returns>
	public bool IsGameInProgress() => IsGameInProgressFunc?.Invoke() ?? false;
	#endregion Navigation
	#region Session State (Episode/Difficulty Selection)
	/// <summary>
	/// Get the currently selected episode number.
	/// </summary>
	/// <returns>Episode number (1-6)</returns>
	public int GetEpisode() => sessionState.SelectedEpisode;
	/// <summary>
	/// Set the selected episode number.
	/// </summary>
	/// <param name="episode">Episode number (1-6)</param>
	public void SetEpisode(int episode) => sessionState.SelectedEpisode = episode;
	/// <summary>
	/// Get the currently selected difficulty.
	/// </summary>
	/// <returns>Difficulty level (0=Baby, 1=Easy, 2=Normal, 3=Hard)</returns>
	public int GetDifficulty() => sessionState.SelectedDifficulty;
	/// <summary>
	/// Set the selected difficulty.
	/// </summary>
	/// <param name="difficulty">Difficulty level (0=Baby, 1=Easy, 2=Normal, 3=Hard)</param>
	public void SetDifficulty(int difficulty) => sessionState.SelectedDifficulty = difficulty;
	/// <summary>
	/// Trigger game start.
	/// Sets the StartGame flag which Root.cs detects to transition to ActionStage.
	/// </summary>
	/// <param name="start">True to start game, false to cancel</param>
	public void SetStartGame(bool start) => sessionState.StartGame = start;
	#endregion Session State
	#region Settings (Config.cs Integration)
	// These methods read/write directly to Config.cs
	// Config is saved on menu exit or game quit
	/// <summary>
	/// Get the current sound mode setting.
	/// </summary>
	/// <returns>Sound mode name ("Off", "PC", "AdLib")</returns>
	public string GetSoundMode() => config.SoundMode.ToString();
	/// <summary>
	/// Set the sound mode.
	/// </summary>
	/// <param name="mode">Sound mode name ("Off", "PC", "AdLib")</param>
	public void SetSoundMode(string mode)
	{
		if (Enum.TryParse(mode, true, out Config.SDMode sdMode))
			config.SoundMode = sdMode;
		else
			_logger?.LogWarning("Menu Lua: Unknown sound mode '{mode}'", mode);
	}
	/// <summary>
	/// Get the music enabled setting.
	/// </summary>
	/// <returns>True if music is enabled</returns>
	public bool GetMusicEnabled() => config.MusicEnabled;
	/// <summary>
	/// Set the music enabled setting.
	/// </summary>
	/// <param name="enabled">True to enable music</param>
	public void SetMusicEnabled(bool enabled) => config.MusicEnabled = enabled;
	/// <summary>
	/// Get the digital sound mode setting.
	/// </summary>
	/// <returns>Digi mode name ("Off", "SoundBlaster")</returns>
	public string GetDigiMode() => config.DigiMode.ToString();
	/// <summary>
	/// Set the digital sound mode.
	/// </summary>
	/// <param name="mode">Digi mode name ("Off", "SoundBlaster")</param>
	public void SetDigiMode(string mode)
	{
		if (Enum.TryParse(mode, true, out Config.SDSMode sdsMode))
			config.DigiMode = sdsMode;
		else
			_logger?.LogWarning("Menu Lua: Unknown digi mode '{mode}'", mode);
	}
	/// <summary>
	/// Get the mouse enabled setting.
	/// </summary>
	/// <returns>True if mouse is enabled</returns>
	public bool GetMouseEnabled() => config.MouseEnabled;
	/// <summary>
	/// Set the mouse enabled setting.
	/// </summary>
	/// <param name="enabled">True to enable mouse</param>
	public void SetMouseEnabled(bool enabled) => config.MouseEnabled = enabled;
	#endregion Settings (Config.cs)
	#region VR Settings (MenuState - New, not in CONFIG format)
	/// <summary>
	/// Get the current VR mode setting.
	/// This is a new setting not present in the original CONFIG file format.
	/// </summary>
	/// <returns>VR mode name ("Roomscale", "FiveDOF")</returns>
	public string GetVRMode() => sessionState.VRMode.ToString();
	/// <summary>
	/// Set the VR mode.
	/// </summary>
	/// <param name="mode">VR mode name ("Roomscale", "FiveDOF")</param>
	public void SetVRMode(string mode)
	{
		if (Enum.TryParse(mode, true, out VRMode vrMode))
			sessionState.VRMode = vrMode;
		else
			_logger?.LogWarning("Menu Lua: Unknown VR mode '{mode}'", mode);
	}
	#endregion VR Settings
	#region Menu Item Selection and Dynamic Content
	/// <summary>
	/// Delegate for getting the currently selected menu item index.
	/// Set by MenuManager after context creation.
	/// </summary>
	public Func<int> GetSelectedIndexFunc { get; set; }
	/// <summary>
	/// Delegate for updating a picture in the current menu.
	/// Set by MenuManager after context creation.
	/// Parameters: pictureName (e.g., "C_BABYMODEPIC"), pictureIndex (0-based index in Pictures list)
	/// </summary>
	public Action<string, int> SetPictureAction { get; set; }
	/// <summary>
	/// Get the currently selected menu item index.
	/// Exposed to Lua. Delegates to MenuManager.
	/// Matches original Wolf3D's DrawNewGameDiff(int w) parameter.
	/// </summary>
	/// <returns>Zero-based index of selected item</returns>
	public int GetSelectedIndex() => GetSelectedIndexFunc?.Invoke() ?? 0;
	/// <summary>
	/// Update a picture in the current menu.
	/// Used for dynamic picture changes like difficulty faces in NewGame menu.
	/// Matches original Wolf3D's VWB_DrawPic behavior in DrawNewGameDiff.
	/// </summary>
	/// <param name="pictureName">Name of the VgaGraph picture (e.g., "C_BABYMODEPIC")</param>
	/// <param name="pictureIndex">Zero-based index of the picture in the menu's Pictures list (defaults to 0)</param>
	public void SetPicture(string pictureName, int pictureIndex = 0) => SetPictureAction?.Invoke(pictureName, pictureIndex);
	#endregion Menu Item Selection and Dynamic Content
	#region UI Control (to be implemented by MenuManager)
	/// <summary>
	/// Delegate for showing a confirmation dialog.
	/// Set by MenuManager after context creation.
	/// </summary>
	public Func<string, bool> ShowConfirmFunc { get; set; }
	/// <summary>
	/// Delegate for showing an info message.
	/// Set by MenuManager after context creation.
	/// </summary>
	public Action<string> ShowMessageAction { get; set; }
	/// <summary>
	/// Delegate for refreshing the current menu display.
	/// Set by MenuManager after context creation.
	/// </summary>
	public Action RefreshMenuAction { get; set; }
	/// <summary>
	/// Show a confirmation dialog (Yes/No).
	/// Exposed to Lua. Delegates to MenuManager.
	/// </summary>
	/// <param name="message">Message to display</param>
	/// <returns>True if user confirmed, false if cancelled</returns>
	public bool ShowConfirm(string message) => ShowConfirmFunc?.Invoke(message) ?? false;
	/// <summary>
	/// Show an informational message.
	/// Exposed to Lua. Delegates to MenuManager.
	/// </summary>
	/// <param name="message">Message to display</param>
	public void ShowMessage(string message) => ShowMessageAction?.Invoke(message);
	/// <summary>
	/// Refresh the current menu display.
	/// Used after changing settings to update radio buttons, etc.
	/// Exposed to Lua. Delegates to MenuManager.
	/// </summary>
	public void RefreshMenu() => RefreshMenuAction?.Invoke();
	#endregion UI Control
}
