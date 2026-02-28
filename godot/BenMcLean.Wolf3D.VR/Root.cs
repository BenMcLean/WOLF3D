using System;
using System.Collections.Generic;
using BenMcLean.Wolf3D.Assets.Gameplay;
using BenMcLean.Wolf3D.Shared;
using BenMcLean.Wolf3D.Shared.Menu;
using BenMcLean.Wolf3D.Simulator.State;
using BenMcLean.Wolf3D.VR.VR;
using Godot;

namespace BenMcLean.Wolf3D.VR;

/// <summary>
/// Root node for the VR application.
/// Manages global VR environment (sky, lighting) and scene transitions.
/// Owns the ScreenFadeOverlay for fade-to-black/fade-from-black transitions.
/// </summary>
public partial class Root : Node3D
{
	private enum TransitionState
	{
		Idle,
		FadingOut,
		FadingIn,
	}

	[Export]
	public int CurrentLevelIndex { get; set; } = 0;

	/// <summary>
	/// Captures the state needed to resume a suspended game.
	/// The Simulator is a pure C# object (no Godot dependencies) and survives ActionStage destruction.
	/// Player position and angle are stored in the Simulator's PlayerX/PlayerY/PlayerAngle.
	/// </summary>
	private record SuspendedGameState(
		Simulator.Simulator Simulator);

	private Node _currentScene;
	private Node _pendingScene;
	private Action _pendingMidFadeAction;
	private Shared.DosScreen _errorScreen;
	private bool _errorMode = false;
	private ScreenFadeOverlay _fadeOverlay;
	private TransitionState _transitionState = TransitionState.Idle;
	private bool _skipFadeIn = false;
	private SuspendedGameState _suspendedGame;

	/// <summary>
	/// The active display mode (VR or flatscreen).
	/// Initialized first before any other systems.
	/// </summary>
	public IDisplayMode DisplayMode { get; private set; }

	public override void _Ready()
	{
		// Root must keep processing during pause so fade overlay animates
		ProcessMode = ProcessModeEnum.Always;

		// Register error display callback
		ExceptionHandler.DisplayCallback = ShowErrorScreen;

		try
		{
			// Initialize display mode FIRST (VR or flatscreen)
			// This must happen before anything else that needs the camera
			DisplayMode = DisplayModeFactory.Create();

			// Create fade overlay for scene transitions
			_fadeOverlay = new ScreenFadeOverlay();
			AddChild(_fadeOverlay);
			_fadeOverlay.FadeOutComplete += OnFadeOutComplete;
			_fadeOverlay.FadeInComplete += OnFadeInComplete;

			// Add SoundBlaster to scene tree (manages both AdLib and PC Speaker audio)
			AddChild(new Shared.Audio.SoundBlaster());

			// Boot to SetupRoom which loads the shareware assets and shows the
			// DosScreen progress log. After completion, _Process() transitions to
			// the game selection MenuRoom.
			SetupRoom setupRoom = new(DisplayMode, @"..\..\games\WL1.xml", isInitialLoad: true);
			TransitionToImmediate(setupRoom);
		}
		catch (Exception ex)
		{
			ExceptionHandler.HandleException(ex);
		}
	}

	public override void _Process(double delta)
	{
		// Always update display mode so VR head tracking and camera continue during fades.
		// Only SimulatorController is Pausable — presentation nodes keep rendering.
		DisplayMode?.Update(delta);

		// Don't poll for new transitions while one is in progress
		if (_transitionState != TransitionState.Idle)
			return;

		// Poll SetupRoom: transitions after assets finish loading
		if (_currentScene is SetupRoom setupRoom && setupRoom.IsComplete)
		{
			if (setupRoom.IsInitialLoad)
			{
				// Shareware assets loaded → show the game selection menu
				string gamesDir = System.IO.Path.GetFullPath(@"..\..\games");
				MenuCollection gameSelectMenu = GameSelectionMenuFactory.Build(gamesDir);
				MenuRoom selectionRoom = new(DisplayMode) { MenuCollectionOverride = gameSelectMenu };
				TransitionTo(selectionRoom);
			}
			else
			{
				// Selected game assets loaded → initialize VR materials, play music, enter game
				// scaleFactor: 4 for better performance, 8 for maximum quality
				try
				{
					VRAssetManager.Initialize(scaleFactor: 8);
					string songName = Shared.SharedAssetManager.CurrentGame.MapAnalyses[CurrentLevelIndex].Music;
					if (!string.IsNullOrWhiteSpace(songName))
						Shared.EventBus.Emit(Shared.GameEvent.PlayMusic, songName);
					TransitionTo(new MenuRoom(DisplayMode));
				}
				catch (Exception ex)
				{
					ExceptionHandler.HandleException(ex);
				}
			}
			return;
		}

		// Poll MenuRoom for game start, resume, or intermission completion
		if (_currentScene is MenuRoom menuRoom)
		{
			// Game selected from the procedural game selection menu
			if (menuRoom.SelectedGameXmlPath != null)
			{
				TransitionTo(new SetupRoom(DisplayMode, menuRoom.SelectedGameXmlPath, isInitialLoad: false));
				return;
			}

			if (menuRoom.PendingEndGame)
			{
				// User confirmed "End Game" — discard suspended game and go to main menu
				_suspendedGame = null;
				MenuRoom freshMenu = new(DisplayMode);
				TransitionTo(freshMenu);
			}
			else if (menuRoom.PendingLoadGame is int loadSlot)
			{
				LoadSavedGame(loadSlot);
			}
			else if (menuRoom.PendingResumeGame && _suspendedGame != null)
			{
				ResumeGame();
			}
			else if (menuRoom.PendingLevelTransition is ActionStage.LevelTransitionRequest intermissionRequest)
			{
				// Intermission dismissed — continue to next level
				_suspendedGame = null;
				ActionStage newStage = new(DisplayMode,
					levelIndex: intermissionRequest.LevelIndex,
					savedInventory: intermissionRequest.SavedInventory,
					savedLevelStats: intermissionRequest.AllLevelStats);
				TransitionTo(newStage);
			}
			else if (menuRoom.ShouldStartGame)
			{
				// Starting a new game discards any suspended game
				_suspendedGame = null;
				// Get selected episode and difficulty from menu
				int episode = menuRoom.SelectedEpisode;
				int difficulty = menuRoom.SelectedDifficulty;
				ActionStage actionStage = new(DisplayMode, levelIndex: CurrentLevelIndex, difficulty: difficulty);
				TransitionTo(actionStage);
			}
		}
		// Poll ActionStage for level transition or return-to-menu requests
		else if (_currentScene is ActionStage actionStage)
		{
			if (actionStage.PendingDeathFadeOut)
			{
				// WL_GAME.C:Died() — fade to black first, then run OnDeath script
				// so player sees health=0 during fadeout, not the reset values
				Simulator.Simulator sim = actionStage.SimulatorController.Simulator;
				_suspendedGame = null;
				StartFade(() =>
				{
					// Screen is now fully black — run OnDeath script to reset inventory
					string deathResult = sim.ExecuteOnDeathScript();
					// Swap to appropriate scene based on script result
					if (_currentScene != null)
					{
						RemoveChild(_currentScene);
						_currentScene.QueueFree();
					}
					if (deathResult == "restart")
					{
						// WL_GAME.C:Died() with lives remaining — restart same level
						Dictionary<string, int> savedInventory = sim.Inventory.SaveState();
						int currentLevel = sim.Inventory.GetValue("MapOn");
						int difficulty = sim.Inventory.GetValue("Difficulty");
						ActionStage newStage = new(DisplayMode,
							levelIndex: currentLevel,
							difficulty: difficulty,
							savedInventory: savedInventory);
						_currentScene = newStage;
						_pendingScene = null;
						AddChild(newStage);
					}
					else
					{
						// WL_GAME.C:Died() with no lives — game over, show high scores then menu
						// Extract final score before sim is discarded
						int finalScore = sim.Inventory.GetValue("Score");
						ushort completedLevel = (ushort)sim.Inventory.GetValue("MapOn");
						// Inventory stores episode 1-indexed; HighScoreEntry uses 0-indexed (matches original)
						ushort episode = (ushort)Math.Max(0, sim.Inventory.GetValue("Episode") - 1);
						MenuRoom gameOverRoom = new(DisplayMode)
						{
							StartMenuOverride = !string.IsNullOrEmpty(deathResult) && deathResult != "gameover"
								? deathResult : null,
							PendingHighScoreScore = finalScore,
							PendingHighScoreCompleted = completedLevel,
							PendingHighScoreEpisode = episode,
						};
						_currentScene = gameOverRoom;
						_pendingScene = null;
						AddChild(gameOverRoom);
						gameOverRoom.SetFadeTransitionHandler(StartMenuFade);
					}
				});
			}
			else if (actionStage.PendingReturnToMenu)
			{
				SuspendToMenu(actionStage);
			}
			else if (actionStage.PendingTransition is ActionStage.LevelTransitionRequest request)
			{
				// Level transitions discard the suspended game (new level = new state)
				_suspendedGame = null;
				// Route through intermission/victory screen
				MenuRoom intermissionRoom = new(DisplayMode)
				{
					StartMenuOverride = request.MenuName ?? "LevelComplete",
					LevelTransition = request,
				};
				// For Victory (episode complete), pass final score for high score check
				if (request.MenuName == "Victory" && request.AllLevelStats?.Count > 0)
				{
					Simulator.LevelCompletionStats lastStats =
						request.AllLevelStats[request.AllLevelStats.Count - 1];
					ushort ep = (ushort)Math.Max(0,
						(request.SavedInventory?.TryGetValue("Episode", out int episodeVal) == true
							? episodeVal : 1) - 1);
					intermissionRoom.PendingHighScoreScore = lastStats.Score;
					intermissionRoom.PendingHighScoreCompleted = (ushort)lastStats.FloorNumber;
					intermissionRoom.PendingHighScoreEpisode = ep;
				}
				TransitionTo(intermissionRoom);
			}
		}
	}

	/// <summary>
	/// Suspends the current game and transitions to the main menu.
	/// Captures the Simulator and player state before destroying the ActionStage.
	/// </summary>
	private void SuspendToMenu(ActionStage actionStage)
	{
		// Capture state before ActionStage is destroyed
		// Player position/angle are already in the Simulator (updated each frame)
		_suspendedGame = new SuspendedGameState(
			actionStage.SimulatorController.Simulator);

		MenuRoom menuRoom = new(DisplayMode)
		{
			HasSuspendedGame = true,
			StartMenuOverride = SharedAssetManager.CurrentGame?.MenuCollection?.PauseMenu,
			SuspendedSimulator = _suspendedGame.Simulator,
			SuspendedLevelIndex = _suspendedGame.Simulator.Inventory.GetValue("MapOn"),
		};
		_pendingScene = menuRoom;
		StartFade(() =>
		{
			if (_currentScene != null)
			{
				RemoveChild(_currentScene);
				_currentScene.QueueFree();
			}

			_currentScene = menuRoom;
			_pendingScene = null;
			AddChild(menuRoom);

			// Wire up fade handler after MenuRoom._Ready has run
			menuRoom.SetFadeTransitionHandler(StartMenuFade);
		});
	}

	/// <summary>
	/// Resumes a suspended game from the menu.
	/// Creates a new ActionStage that reuses the existing Simulator.
	/// </summary>
	private void ResumeGame()
	{
		if (_suspendedGame == null)
			return;

		SuspendedGameState state = _suspendedGame;
		ActionStage actionStage = new(
			DisplayMode,
			state.Simulator);

		_suspendedGame = null;
		TransitionTo(actionStage);
	}

	/// <summary>
	/// Loads a saved game from a slot and transitions to an ActionStage.
	/// Creates a fresh simulator, loads the level, then applies the saved state.
	/// </summary>
	/// <param name="slot">Save game slot index (0-9)</param>
	private void LoadSavedGame(int slot)
	{
		SaveGameFile saveFile = SaveGameManager.Load(slot);
		if (saveFile?.Snapshot == null)
		{
			GD.PrintErr($"ERROR: Failed to load save game from slot {slot}");
			return;
		}

		// Discard any suspended game
		_suspendedGame = null;

		// Create new ActionStage with the saved snapshot
		// Level index is read from the snapshot's MapOn inventory value
		ActionStage actionStage = new(DisplayMode, saveFile.Snapshot);
		TransitionTo(actionStage);
	}

	/// <summary>
	/// Transitions to a new scene with fade-to-black and fade-from-black.
	/// Skips fade-out when the current scene has IRoom.SkipFade (already black background).
	/// Skips fade-in when the incoming scene has IRoom.SkipFade (already black background).
	/// </summary>
	public void TransitionTo(Node newScene)
	{
		if (_errorMode || _transitionState != TransitionState.Idle)
			return;

		bool skipFadeOut = (_currentScene as IRoom)?.SkipFade ?? false;
		bool skipFadeIn = (newScene as IRoom)?.SkipFade ?? false;

		_pendingScene = newScene;

		if (skipFadeOut)
		{
			// Current scene already has a black background — swap immediately then fade in.
			if (_currentScene != null)
			{
				RemoveChild(_currentScene);
				_currentScene.QueueFree();
			}
			_currentScene = _pendingScene;
			_pendingScene = null;
			AddChild(_currentScene);
			(_currentScene as IRoom)?.SetFadeTransitionHandler(StartMenuFade);

			_transitionState = TransitionState.FadingIn;
			GetTree().Paused = true;
			_fadeOverlay.FadeFromBlack();
		}
		else
		{
			StartFade(() =>
			{
				// Swap scenes while screen is fully black
				if (_currentScene != null)
				{
					RemoveChild(_currentScene);
					_currentScene.QueueFree();
				}
				_currentScene = _pendingScene;
				_pendingScene = null;
				AddChild(_currentScene);
				(_currentScene as IRoom)?.SetFadeTransitionHandler(StartMenuFade);
			}, skipFadeIn: skipFadeIn);
		}
	}

	/// <summary>
	/// Transitions to a new scene immediately without fading.
	/// Used for the initial boot scene.
	/// </summary>
	private void TransitionToImmediate(Node newScene)
	{
		if (_errorMode)
			return;

		if (_currentScene != null)
		{
			RemoveChild(_currentScene);
			_currentScene.QueueFree();
		}

		_currentScene = newScene;
		AddChild(_currentScene);
	}

	/// <summary>
	/// Starts a fade-out, executes the action at mid-fade, then fades back in.
	/// Pass skipFadeIn=true when the incoming scene has a black background (IRoom.SkipFade).
	/// Used for both scene transitions and menu screen navigations.
	/// </summary>
	private void StartFade(Action midFadeAction, bool skipFadeIn = false)
	{
		_skipFadeIn = skipFadeIn;
		_pendingMidFadeAction = midFadeAction;
		_transitionState = TransitionState.FadingOut;
		GetTree().Paused = true;
		_fadeOverlay.FadeToBlack();
	}

	/// <summary>
	/// Callback for menu screen navigations (Main → Episodes, etc.).
	/// Wraps the menu navigation in a fade transition.
	/// </summary>
	private void StartMenuFade(Action menuNavigation)
	{
		if (_errorMode || _transitionState != TransitionState.Idle)
		{
			// If we can't fade, just navigate immediately
			menuNavigation();
			return;
		}

		StartFade(menuNavigation);
	}

	/// <summary>
	/// Called when fade-to-black completes. Executes mid-fade action and starts fade-in,
	/// unless skipFadeIn was set (incoming scene has a black background).
	/// </summary>
	private void OnFadeOutComplete()
	{
		try
		{
			_pendingMidFadeAction?.Invoke();
			_pendingMidFadeAction = null;

			if (_skipFadeIn)
			{
				_skipFadeIn = false;
				_transitionState = TransitionState.Idle;
				GetTree().Paused = false;
				_fadeOverlay.SetTransparent();
			}
			else
			{
				_transitionState = TransitionState.FadingIn;
				_fadeOverlay.FadeFromBlack();
			}
		}
		catch (Exception ex)
		{
			_pendingMidFadeAction = null;
			_skipFadeIn = false;
			_transitionState = TransitionState.Idle;
			GetTree().Paused = false;
			ExceptionHandler.HandleException(ex);
		}
	}

	/// <summary>
	/// Called when fade-from-black completes. Returns to idle and unpauses.
	/// </summary>
	private void OnFadeInComplete()
	{
		_transitionState = TransitionState.Idle;
		GetTree().Paused = false;
	}

	/// <summary>
	/// Displays an exception to the user via DOS screen.
	/// Called by ExceptionHandler when an unhandled exception occurs.
	/// </summary>
	/// <param name="ex">The exception to display</param>
	private void ShowErrorScreen(Exception ex)
	{
		// Enter error mode - prevent further scene transitions
		_errorMode = true;

		// Remove current scene if it exists
		if (_currentScene != null)
		{
			RemoveChild(_currentScene);
			_currentScene.QueueFree();
			_currentScene = null;
		}

		// Create error screen if not already created
		if (_errorScreen == null)
		{
			_errorScreen = new Shared.DosScreen();
			AddChild(_errorScreen);
		}

		// Display exception information
		_errorScreen.WriteLine("=".PadRight(80, '='));
		_errorScreen.WriteLine("UNHANDLED EXCEPTION");
		_errorScreen.WriteLine("=".PadRight(80, '='));
		_errorScreen.WriteLine("");
		_errorScreen.WriteLine($"Type: {ex.GetType().FullName}");
		_errorScreen.WriteLine("");
		_errorScreen.WriteLine($"Message: {ex.Message}");
		_errorScreen.WriteLine("");
		_errorScreen.WriteLine("Stack Trace:");
		_errorScreen.WriteLine(ex.StackTrace ?? "(no stack trace available)");

		// Include inner exceptions if present
		Exception innerEx = ex.InnerException;
		int innerCount = 1;
		while (innerEx != null)
		{
			_errorScreen.WriteLine("");
			_errorScreen.WriteLine($"--- Inner Exception #{innerCount} ---");
			_errorScreen.WriteLine($"Type: {innerEx.GetType().FullName}");
			_errorScreen.WriteLine($"Message: {innerEx.Message}");
			_errorScreen.WriteLine(innerEx.StackTrace ?? "(no stack trace available)");
			innerEx = innerEx.InnerException;
			innerCount++;
		}
	}
}
