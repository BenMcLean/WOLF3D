using System;
using System.Collections.Generic;
using BenMcLean.Wolf3D.Shared;
using BenMcLean.Wolf3D.Simulator;
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
	// VW_FadeOut/VW_FadeIn: 30 interpolation steps at 70Hz
	private const float FadeDuration = 30f / 70f;

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

			// Load game assets
			// TODO: Eventually this will be done from menu selection, not hardcoded
			Shared.SharedAssetManager.LoadGame(@"..\..\games\WL1.xml");

			// Create VR-specific 3D materials
			// Try scaleFactor: 4 for better performance, or 8 for maximum quality
			VRAssetManager.Initialize(scaleFactor: 8);

			// Add SoundBlaster to scene tree (manages both AdLib and PC Speaker audio)
			AddChild(new Shared.Audio.SoundBlaster());

			// Play the first level's music
			string songName = Shared.SharedAssetManager.CurrentGame.MapAnalyses[CurrentLevelIndex].Music;
			if (!string.IsNullOrWhiteSpace(songName))
				Shared.EventBus.Emit(Shared.GameEvent.PlayMusic, songName);

			// Boot to MenuRoom and fade in from black
			MenuRoom menuRoom = new(DisplayMode);
			TransitionToImmediate(menuRoom);

			// Wire up fade transitions for menu screen navigations
			menuRoom.SetFadeTransitionHandler(StartMenuFade);

			// Fade in the initial screen (all screens should fade in)
			_transitionState = TransitionState.FadingIn;
			_fadeOverlay.FadeFromBlack(FadeDuration);
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

		// Poll MenuRoom for game start, resume, or intermission completion
		if (_currentScene is MenuRoom menuRoom)
		{
			if (menuRoom.PendingLoadGame is int loadSlot)
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
			if (actionStage.PendingDeath)
			{
				// WL_GAME.C:Died() with lives remaining — restart same level
				// OnDeath script has already reset inventory (health, ammo, keys, weapons)
				Dictionary<string, int> savedInventory = actionStage.SimulatorController.Simulator.Inventory.SaveState();
				int currentLevel = actionStage.SimulatorController.Simulator.Inventory.GetValue("MapOn");
				int difficulty = actionStage.SimulatorController.Simulator.Inventory.GetValue("Difficulty");
				_suspendedGame = null;
				ActionStage newStage = new(DisplayMode,
					levelIndex: currentLevel,
					difficulty: difficulty,
					savedInventory: savedInventory);
				TransitionTo(newStage);
			}
			else if (actionStage.PendingGameOver)
			{
				// WL_GAME.C:Died() with no lives — game over, return to menu
				_suspendedGame = null;
				TransitionTo(new MenuRoom(DisplayMode));
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
	/// </summary>
	public void TransitionTo(Node newScene)
	{
		if (_errorMode || _transitionState != TransitionState.Idle)
			return;

		_pendingScene = newScene;
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

			// Wire up fade handler for any MenuRoom (intermission, victory, etc.)
			if (_currentScene is MenuRoom mr)
				mr.SetFadeTransitionHandler(StartMenuFade);
		});
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
	/// Used for both scene transitions and menu screen navigations.
	/// </summary>
	private void StartFade(Action midFadeAction)
	{
		_pendingMidFadeAction = midFadeAction;
		_transitionState = TransitionState.FadingOut;
		GetTree().Paused = true;
		_fadeOverlay.FadeToBlack(FadeDuration);
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
	/// Called when fade-to-black completes. Executes mid-fade action and starts fade-in.
	/// </summary>
	private void OnFadeOutComplete()
	{
		_pendingMidFadeAction?.Invoke();
		_pendingMidFadeAction = null;

		// Start fade-in
		_transitionState = TransitionState.FadingIn;
		_fadeOverlay.FadeFromBlack(FadeDuration);
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
