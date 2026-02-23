using System;
using System.Linq;
using BenMcLean.Wolf3D.Assets.Gameplay;
using BenMcLean.Wolf3D.Simulator;
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
	/// <summary>
	/// Delegate for selecting a game by its XML definition path.
	/// Set by the presentation layer (MenuRoom) when hosting a game selection menu.
	/// </summary>
	public Action<string> SelectGameAction { get; set; }
	/// <summary>
	/// Select a game to load by its XML definition file path.
	/// Exposed to Lua. Called by game selection menu item scripts.
	/// Root polls MenuRoom.SelectedGameXmlPath and transitions to SetupRoom.
	/// </summary>
	/// <param name="xmlPath">Absolute path (forward slashes) to the game's XML file</param>
	public void SelectGame(string xmlPath) => SelectGameAction?.Invoke(xmlPath);
	/// <summary>
	/// Request to quit the application, showing a confirmation dialog.
	/// Exposed to Lua. MenuManager intercepts this flag in Update() and shows a modal.
	/// WL_MENU.C:CP_Quit - shows confirm dialog then calls Quit(NULL)
	/// </summary>
	public void RequestQuit() => QuitRequested = true;
	/// <summary>
	/// Set by RequestQuit(). Cleared by MenuManager after showing the modal.
	/// </summary>
	public bool QuitRequested { get; set; }
	/// <summary>
	/// Request to end the current game, showing a confirmation dialog.
	/// Exposed to Lua. MenuManager intercepts this flag in Update() and shows a modal.
	/// WL_MENU.C:CP_EndGame - shows confirm dialog then sets playstate = ex_died
	/// </summary>
	public void RequestEndGame() => EndGameRequested = true;
	/// <summary>
	/// Set by RequestEndGame(). Cleared by MenuManager after showing the modal.
	/// </summary>
	public bool EndGameRequested { get; set; }
	/// <summary>
	/// Resume the suspended game (return to gameplay).
	/// Exposed to Lua. Delegates to CloseAllMenusAction which signals MenuRoom.
	/// </summary>
	public void ResumeGame() => CloseAllMenusAction?.Invoke();
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
	#region Dynamic Content Updates
	/// <summary>
	/// Delegate for updating a named text label.
	/// Set by MenuManager after context creation.
	/// </summary>
	public Action<string, string> SetTextAction { get; set; }
	/// <summary>
	/// Delegate for updating a ticker label's displayed value.
	/// Set by MenuManager after context creation.
	/// </summary>
	public Action<string, string> UpdateTickerAction { get; set; }
	/// <summary>
	/// Update a named text label's content.
	/// Exposed to Lua. Delegates to MenuManager → MenuRenderer.
	/// </summary>
	/// <param name="name">The Name attribute of the text element</param>
	/// <param name="value">New text content</param>
	public void SetText(string name, string value) => SetTextAction?.Invoke(name, value);
	/// <summary>
	/// Delegate for updating a menu item's text by index.
	/// Set by MenuManager after context creation.
	/// </summary>
	public Action<int, string> SetItemTextAction { get; set; }
	/// <summary>
	/// Update a menu item's display text by index.
	/// Exposed to Lua. Delegates to MenuManager.
	/// Used by OnShow scripts to populate save slot names dynamically.
	/// </summary>
	/// <param name="index">Zero-based menu item index</param>
	/// <param name="text">New display text</param>
	public void SetItemText(int index, string text) => SetItemTextAction?.Invoke(index, text);
	#endregion Dynamic Content Updates
	#region Save/Load Game
	/// <summary>
	/// Delegate for saving the game to a slot.
	/// Set by MenuManager after context creation.
	/// </summary>
	public Action<int> SaveGameAction { get; set; }
	/// <summary>
	/// Delegate for loading a game from a slot.
	/// Set by MenuManager after context creation.
	/// </summary>
	public Action<int> LoadGameAction { get; set; }
	/// <summary>
	/// Delegate for getting the display name of a save slot.
	/// Set by MenuManager after context creation.
	/// </summary>
	public Func<int, string> GetSaveSlotNameFunc { get; set; }
	/// <summary>
	/// Save the current game to a slot.
	/// Exposed to Lua. Delegates to MenuManager.
	/// </summary>
	/// <param name="slot">Slot index (0-9)</param>
	public void SaveGame(int slot) => SaveGameAction?.Invoke(slot);
	/// <summary>
	/// Load a game from a slot.
	/// Exposed to Lua. Delegates to MenuManager.
	/// </summary>
	/// <param name="slot">Slot index (0-9)</param>
	public void LoadGame(int slot) => LoadGameAction?.Invoke(slot);
	/// <summary>
	/// Get the display name for a save slot.
	/// Exposed to Lua. Returns empty string if slot is empty.
	/// </summary>
	/// <param name="slot">Slot index (0-9)</param>
	/// <returns>Display name or empty string</returns>
	public string GetSaveSlotName(int slot) => GetSaveSlotNameFunc?.Invoke(slot) ?? "";
	#endregion Save/Load Game
	#region Menu Sequence (Presentation Mode)
	/// <summary>
	/// The active menu sequence, set by MenuManager before OnShow executes.
	/// Lua scripts queue steps onto this sequence via StartTicker, QueueDelay, etc.
	/// Null when no sequence is available.
	/// </summary>
	public MenuSequence ActiveSequence { get; set; }
	/// <summary>
	/// Delegate to look up a ticker definition by name from the current menu.
	/// Set by MenuManager after context creation.
	/// </summary>
	public Func<string, MenuTickerDefinition> GetTickerDefinitionFunc { get; set; }
	/// <summary>
	/// Queue a delay step in the current menu sequence.
	/// The sequence pauses for the specified duration before advancing.
	/// Pressing any button skips the delay.
	/// </summary>
	/// <param name="seconds">Duration to wait in seconds</param>
	public void QueueDelay(double seconds)
	{
		ActiveSequence?.Enqueue(new DelaySequenceStep((float)seconds));
	}
	/// <summary>
	/// Set the skip behavior for the current menu sequence.
	/// "all" = pressing button during ticker skips ALL remaining steps (Wolf3D intermission).
	/// "current" = pressing button during ticker skips only the current step.
	/// </summary>
	/// <param name="behavior">"all" or "current"</param>
	public void SetSkipBehavior(string behavior)
	{
		if (ActiveSequence is null)
			return;
		ActiveSequence.SkipBehavior = behavior?.Equals("current", StringComparison.OrdinalIgnoreCase) ?? false
			? SequenceSkipBehavior.SkipCurrent
			: SequenceSkipBehavior.SkipAll;
	}
	#endregion Menu Sequence
	#region Intermission (Level Completion Screen)
	/// <summary>
	/// Level completion statistics, set when showing the intermission screen.
	/// Null when not in intermission mode.
	/// </summary>
	public LevelCompletionStats CompletionStats { get; set; }
	/// <summary>
	/// Flag set by Lua when the player dismisses the intermission screen.
	/// Polled by Root.cs to trigger level transition.
	/// </summary>
	public bool ContinueToNextLevelRequested { get; set; }
	/// <summary>
	/// Get the floor number for the completed level.
	/// Exposed to Lua for intermission screen display.
	/// </summary>
	/// <returns>Floor number</returns>
	public int GetFloor() => CompletionStats?.FloorNumber ?? 0;
	/// <summary>
	/// Get the number of enemies killed.
	/// </summary>
	/// <returns>Kill count</returns>
	public int GetKillCount() => CompletionStats?.KillCount ?? 0;
	/// <summary>
	/// Get the total number of enemies on the level.
	/// </summary>
	/// <returns>Total enemy count</returns>
	public int GetKillTotal() => CompletionStats?.KillTotal ?? 0;
	/// <summary>
	/// Get the number of secrets found.
	/// </summary>
	/// <returns>Secret count</returns>
	public int GetSecretCount() => CompletionStats?.SecretCount ?? 0;
	/// <summary>
	/// Get the total number of secrets on the level.
	/// </summary>
	/// <returns>Total secret count</returns>
	public int GetSecretTotal() => CompletionStats?.SecretTotal ?? 0;
	/// <summary>
	/// Get the number of treasures collected.
	/// </summary>
	/// <returns>Treasure count</returns>
	public int GetTreasureCount() => CompletionStats?.TreasureCount ?? 0;
	/// <summary>
	/// Get the total number of treasures on the level.
	/// </summary>
	/// <returns>Total treasure count</returns>
	public int GetTreasureTotal() => CompletionStats?.TreasureTotal ?? 0;
	/// <summary>
	/// Get the level completion time in seconds.
	/// </summary>
	/// <returns>Time in seconds</returns>
	public double GetLevelTime() => CompletionStats is null
		? 0
		: CompletionStats.ElapsedTics / Constants.TicsPerSecond;
	/// <summary>
	/// Get the par time in seconds.
	/// </summary>
	/// <returns>Par time in seconds</returns>
	public double GetParTime() => CompletionStats?.ParTime.TotalSeconds ?? 0;
	/// <summary>
	/// Format a time in seconds as "MM:SS".
	/// Exposed to Lua for display formatting.
	/// </summary>
	/// <param name="seconds">Time in seconds</param>
	/// <returns>Formatted string "MM:SS"</returns>
	public static string FormatTime(double seconds)
	{
		int totalSeconds = (int)seconds,
			minutes = totalSeconds / 60,
			secs = totalSeconds % 60;
		return $"{minutes}:{secs:D2}";
	}
	/// <summary>
	/// Start a ticker animating from 0 to the target value.
	/// If an active sequence exists, queues a TickerSequenceStep for animated counting.
	/// Otherwise, sets the value immediately.
	/// WL_INTER.C: Counts from 0 to target at 70Hz, one per RollDelay tick.
	/// </summary>
	/// <param name="name">Ticker name (e.g., "KillRatio")</param>
	/// <param name="targetValue">Target value to display</param>
	public void StartTicker(string name, int targetValue)
	{
		if (ActiveSequence != null && UpdateTickerAction != null)
		{
			// Look up the ticker definition for sound configuration
			MenuTickerDefinition tickerDef = GetTickerDefinitionFunc?.Invoke(name);
			ActiveSequence.Enqueue(new TickerSequenceStep(
				name,
				targetValue,
				tickerDef,
				UpdateTickerAction,
				PlayAdLibSoundAction));
		}
		else
			// No sequence active - set value immediately
			UpdateTickerAction?.Invoke(name, targetValue.ToString());
	}
	/// <summary>
	/// Add bonus points to the player's score.
	/// Updates the score in CompletionStats for display.
	/// </summary>
	/// <param name="amount">Points to add</param>
	public void GivePoints(int amount) =>
		// Score bonus is tracked in session state for Root.cs to apply
		sessionState.BonusPoints += amount;
	/// <summary>
	/// Signal that the player wants to continue to the next level.
	/// Sets a flag polled by Root.cs.
	/// </summary>
	public void ContinueToNextLevel() => ContinueToNextLevelRequested = true;
	#endregion Intermission
	#region Accumulated Stats (Victory Screen)
	/// <summary>
	/// Accumulated level completion stats from all completed levels in the episode.
	/// Set when showing the Victory screen.
	/// Null when not in victory mode.
	/// </summary>
	public System.Collections.Generic.IReadOnlyList<LevelCompletionStats> AllLevelStats { get; set; }
	/// <summary>
	/// Get the average kill ratio across all completed levels.
	/// WL_INTER.C:Victory averaged stats display.
	/// </summary>
	/// <returns>Average kill percentage (0-100)</returns>
	public int GetAverageKillRatio() =>
		AllLevelStats is null || !AllLevelStats.Any()
			? 0
			: (int)AllLevelStats.Average(s => s.KillTotal > 0
				? (s.KillCount * 100L / s.KillTotal)
				: 0);
	/// <summary>
	/// Get the average secret ratio across all completed levels.
	/// </summary>
	/// <returns>Average secret percentage (0-100)</returns>
	public int GetAverageSecretRatio() =>
		AllLevelStats is null || !AllLevelStats.Any()
			? 0
			: (int)AllLevelStats.Average(s => s.SecretTotal > 0
				? s.SecretCount * 100L / s.SecretTotal
				: 0);
	/// <summary>
	/// Get the average treasure ratio across all completed levels.
	/// </summary>
	/// <returns>Average treasure percentage (0-100)</returns>
	public int GetAverageTreasureRatio() =>
		AllLevelStats is null || !AllLevelStats.Any()
			? 0
			: (int)AllLevelStats.Average(s => s.TreasureTotal > 0
				? s.TreasureCount * 100L / s.TreasureTotal
				: 0);
	/// <summary>
	/// Get the total time across all completed levels in seconds.
	/// WL_INTER.C:Victory total time display.
	/// </summary>
	/// <returns>Total time in seconds</returns>
	public double GetTotalTime() => AllLevelStats?.Sum(s => s.ElapsedTics) / Constants.TicsPerSecond ?? 0;
	#endregion Accumulated Stats
	#region High Scores
	/// <summary>
	/// Get the total number of high score entries.
	/// Always returns Config.MaxScores (7).
	/// </summary>
	public int GetHighScoreCount() => Config.MaxScores;
	/// <summary>
	/// Get the name for a high score entry.
	/// </summary>
	/// <param name="index">Zero-based entry index (0-6)</param>
	public string GetHighScoreName(int index) => config.Scores[index].Name;
	/// <summary>
	/// Get the score value for a high score entry.
	/// </summary>
	/// <param name="index">Zero-based entry index (0-6)</param>
	public int GetHighScoreScore(int index) => config.Scores[index].Score;
	/// <summary>
	/// Get the level completed for a high score entry.
	/// </summary>
	/// <param name="index">Zero-based entry index (0-6)</param>
	public int GetHighScoreCompleted(int index) => (int)config.Scores[index].Completed;
	/// <summary>
	/// Get the episode (0-based) for a high score entry.
	/// Add 1 in Lua when displaying to the player.
	/// </summary>
	/// <param name="index">Zero-based entry index (0-6)</param>
	public int GetHighScoreEpisode(int index) => (int)config.Scores[index].Episode;
	/// <summary>
	/// Get the score from the pending high score data, or 0 if none.
	/// </summary>
	public int GetPendingScore() => sessionState.PendingHighScore?.Score ?? 0;
	/// <summary>
	/// Get the level completed from the pending high score data, or 0 if none.
	/// </summary>
	public int GetPendingCompleted() => (int)(sessionState.PendingHighScore?.Completed ?? 0);
	/// <summary>
	/// Get the episode (0-based) from the pending high score data, or 0 if none.
	/// </summary>
	public int GetPendingEpisode() => (int)(sessionState.PendingHighScore?.Episode ?? 0);
	/// <summary>
	/// Check the pending score against the high score table and add it if it qualifies.
	/// Clears PendingHighScore to prevent double-add.
	/// Saves the config to disk if a new entry was added.
	/// WL_HSCOR.C:CheckHighScore
	/// </summary>
	public void CheckAndAddHighScore()
	{
		if (sessionState.PendingHighScore is null)
			return;
		PendingHighScoreData pending = sessionState.PendingHighScore;
		sessionState.PendingHighScore = null; // prevent double-add
		Config.HighScoreEntry entry = new()
		{
			Name = GetPlayerName(),
			Score = pending.Score,
			Completed = pending.Completed,
			Episode = pending.Episode,
		};
		int rank = config.AddScore(entry);
		sessionState.LastAddedHighScoreIndex = rank;
		if (rank >= 0)
			SaveHighScores();
	}
	/// <summary>
	/// Returns true if the last CheckAndAddHighScore() call added a new entry.
	/// </summary>
	public bool IsNewHighScore() => sessionState.LastAddedHighScoreIndex >= 0;
	/// <summary>
	/// Returns the 0-based rank of the newly added high score, or -1 if none was added.
	/// </summary>
	public int GetNewHighScoreIndex() => sessionState.LastAddedHighScoreIndex;
	/// <summary>
	/// Delegate for saving the config (high scores) to disk.
	/// Set by MenuManager after context creation.
	/// </summary>
	public Action SaveConfigAction { get; set; }
	/// <summary>
	/// Save the high score table to disk.
	/// Delegates to SaveConfigAction (wired to SharedAssetManager.SaveConfig).
	/// </summary>
	public void SaveHighScores() => SaveConfigAction?.Invoke();
	/// <summary>
	/// Delegate for generating the player name for new high score entries.
	/// Set by the presentation layer (e.g., MenuRoom._Ready in VR).
	/// Default: returns "Player".
	/// </summary>
	public Func<string> GetPlayerNameFunc { get; set; }
	/// <summary>
	/// Get the player name for a new high score entry.
	/// Calls GetPlayerNameFunc if set, otherwise returns "Player".
	/// </summary>
	public string GetPlayerName() => GetPlayerNameFunc?.Invoke() ?? "Player";
	#endregion High Scores
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
