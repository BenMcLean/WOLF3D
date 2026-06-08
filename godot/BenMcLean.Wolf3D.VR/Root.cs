using System;
using System.Collections.Generic;
using BenMcLean.Wolf3D.Assets.Gameplay;
using BenMcLean.Wolf3D.Assets.Menu;
using BenMcLean.Wolf3D.Assets.Graphics;
using BenMcLean.Wolf3D.Shared;
using BenMcLean.Wolf3D.Shared.Menu;
using BenMcLean.Wolf3D.Shared.StatusBar;
using BenMcLean.Wolf3D.Simulator.Snapshots;
using BenMcLean.Wolf3D.VR.MenuStage;
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
		/// <summary>
		/// Fade-out just completed (alpha=1). Waiting one full render frame with the old room
		/// still in the scene tree so the VR compositor presents at least one guaranteed
		/// fully-black frame before the heavy scene-swap work begins.
		/// </summary>
		BlackBeforeSwap,
		/// <summary>
		/// Scene has been swapped. Waiting one full render frame with the new room behind the
		/// fully-black overlay so the VR compositor presents at least one guaranteed
		/// fully-black frame after scene-swap GPU work completes, before fade-in begins.
		/// </summary>
		BlackAfterSwap,
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
	private Node _currentScene,
		_pendingScene;
	private Action _pendingMidFadeAction;
	private bool _errorMode = false;
	private ScreenFadeOverlay _fadeOverlay;
	private SpectatorView _spectatorView;
	private TransitionState _transitionState = TransitionState.Idle;
	private bool _skipFadeIn = false;
	private SuspendedGameState _suspendedGame;
	private StatusBarController _statusBarController;
	private StatusBarRenderer _statusBarRenderer;
	/// <summary>
	/// The active display mode (VR or flatscreen).
	/// Initialized first before any other systems.
	/// </summary>
	public IDisplayMode DisplayMode { get; private set; }
	private bool _debugMarkersEnabled = false,
		_cheatModeEnabled = false,
		_useVoxelWeapons = true;
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
			// Optional spectator compositor for VR capture footage.
			// It is opt-in because it adds an extra 3D render pass.
			if (DisplayMode.IsVRActive && RuntimeOptions.SpectatorViewEnabled)
			{
				_spectatorView = new SpectatorView();
				AddChild(_spectatorView);
			}
			// Add SoundBlaster to scene tree (manages both AdLib and PC Speaker audio)
			AddChild(new Shared.Audio.SoundBlaster());
			// Boot to SetupRoom which loads the shareware assets and shows the
			// DosScreen progress log. After completion, _Process() transitions to
			// the game selection MenuRoom.
			SetupRoom setupRoom = new(DisplayMode, System.IO.Path.Combine(RuntimeOptions.Path, "WL1.xml"), isInitialLoad: true);
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
		// Skip in error mode — the error screen is head-locked so no update needed.
		if (!_errorMode)
		{
			try
			{
				DisplayMode?.Update(delta);
			}
			catch (Exception ex)
			{
				ExceptionHandler.HandleException(ex);
			}
		}
		// BlackBeforeSwap: one fully-black frame has been rendered with the old room behind the
		// overlay. Now execute the scene swap while still fully black, then wait one more frame.
		if (_transitionState == TransitionState.BlackBeforeSwap)
		{
			try
			{
				_pendingMidFadeAction?.Invoke();
				_pendingMidFadeAction = null;
			}
			catch (Exception ex)
			{
				_pendingMidFadeAction = null;
				_skipFadeIn = false;
				_transitionState = TransitionState.Idle;
				Simulator.Simulator.Paused = false;
				GetTree().Paused = false;
				ExceptionHandler.HandleException(ex);
				return;
			}
			if (_skipFadeIn)
			{
				_skipFadeIn = false;
				_transitionState = TransitionState.Idle;
				Simulator.Simulator.Paused = false;
				GetTree().Paused = false;
				_fadeOverlay.SetTransparent();
			}
			else
			{
				// Wait one more frame so the new room renders at least one fully-black frame
				// before the fade-in animation begins.
				_transitionState = TransitionState.BlackAfterSwap;
			}
			return;
		}
		// BlackAfterSwap: screen is fully black and the new room is rendered behind the overlay.
		// Poll PrepareForFadeIn() each frame until the room signals it is ready (e.g. VR
		// tracking has reported a non-zero position). Once ready, start the fade-in.
		if (_transitionState == TransitionState.BlackAfterSwap)
		{
			if (!((_currentScene as IRoom)?.PrepareForFadeIn() ?? true))
				return; // hold black, try again next frame
			_transitionState = TransitionState.FadingIn;
			_fadeOverlay.FadeFromBlack();
			return;
		}
		// Don't poll for new transitions while one is in progress
		if (_transitionState != TransitionState.Idle)
			return;
		// Poll SetupRoom: transitions after assets finish loading
		if (_currentScene is SetupRoom setupRoom && setupRoom.IsComplete)
		{
			if (setupRoom.IsInitialLoad)
			{
				// Shareware assets loaded → initialize VR materials, then show the game selection menu
				try
				{
					InitializeVRAssets();
					MenuCollection gameSelectMenu = GameSelectionMenuFactory.Build(RuntimeOptions.Path);
					MenuRoom selectionRoom = new(DisplayMode)
					{
						MenuCollectionOverride = gameSelectMenu,
						StartMenuOverride = "_GameSelect0",
						MenuWeaponSprite = "SPR_PISTOLREADY",
						InitialVRMode = CurrentVRMode(),
						InitialDebugMarkersEnabled = _debugMarkersEnabled,
						InitialCheatModeEnabled = _cheatModeEnabled,
						InitialUseVoxelWeapons = _useVoxelWeapons,
					};
					TransitionTo(selectionRoom);
				}
				catch (Exception ex)
				{
					ExceptionHandler.HandleException(ex);
				}
			}
			else
			{
				// Selected game assets loaded → initialize VR materials, then run OnStartup script
				try
				{
					InitializeVRAssets();
					RunOnStartup();
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
			_debugMarkersEnabled = menuRoom.DebugMarkersEnabled;
			_cheatModeEnabled = menuRoom.CheatModeEnabled;
			_useVoxelWeapons = menuRoom.UseVoxelWeapons;
			// Game selected from the procedural game selection menu
			if (menuRoom.SelectedGameXmlPath is not null)
			{
				TransitionTo(new SetupRoom(DisplayMode, menuRoom.SelectedGameXmlPath, isInitialLoad: false));
				return;
			}
			if (menuRoom.PendingEndGame)
			{
				// User confirmed "End Game" — discard suspended game and re-run OnStartup
				_suspendedGame = null;
				RunOnStartup();
			}
			else if (menuRoom.PendingLoadGame is int loadSlot)
				LoadSavedGame(loadSlot);
			else if (menuRoom.PendingResumeGame && _suspendedGame is not null)
				ResumeGame();
			else if (menuRoom.PendingLevelTransition is ActionRoom.LevelTransitionRequest intermissionRequest)
			{
				// Intermission dismissed — continue to next level
				_suspendedGame = null;
				ActionRoom newStage = new(DisplayMode,
					levelIndex: intermissionRequest.LevelIndex,
					savedInventory: intermissionRequest.SavedInventory,
					savedLevelStats: intermissionRequest.AllLevelStats,
					debugMarkersEnabled: _debugMarkersEnabled,
					cheatModeEnabled: _cheatModeEnabled,
					useVoxelWeapons: _useVoxelWeapons,
					statusBarController: GetOrCreateStatusBarController(),
					statusBarRenderer: GetOrCreateStatusBarRenderer());
				TransitionTo(newStage);
			}
			else if (menuRoom.ShouldStartGame)
			{
				// Starting a new game discards any suspended game
				_suspendedGame = null;
				// Get selected episode and difficulty from menu
				int selectedEpisode = menuRoom.SelectedEpisode;
				ushort episode = (ushort)selectedEpisode;
				int difficulty = menuRoom.SelectedDifficulty,
					levelIndex = SharedAssetManager.CurrentGame.MapAnalyzer.MapNumber(episode, 1);
				ActionRoom actionStage = new(DisplayMode, levelIndex: levelIndex, difficulty: difficulty, debugMarkersEnabled: _debugMarkersEnabled, cheatModeEnabled: _cheatModeEnabled, useVoxelWeapons: _useVoxelWeapons, statusBarController: GetOrCreateStatusBarController(), statusBarRenderer: GetOrCreateStatusBarRenderer());
				TransitionTo(actionStage);
			}
		}
		// Poll ActionStage for level transition or return-to-menu requests
		else if (_currentScene is ActionRoom actionStage)
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
					if (_currentScene is not null)
					{
						RemoveChild(_currentScene);
						_currentScene.QueueFree();
					}
					if (deathResult == "restart")
					{
						// WL_GAME.C:Died() with lives remaining — restart same level
						InventorySnapshot savedInventory = sim.Inventory.Save();
						int currentLevel = sim.Inventory.GetValue("MapOn"),
							difficulty = sim.Inventory.GetValue("Difficulty");
						ActionRoom newStage = new(DisplayMode,
							levelIndex: currentLevel,
							difficulty: difficulty,
							savedInventory: savedInventory,
							debugMarkersEnabled: _debugMarkersEnabled,
							cheatModeEnabled: _cheatModeEnabled,
							useVoxelWeapons: _useVoxelWeapons,
							statusBarController: GetOrCreateStatusBarController(),
							statusBarRenderer: GetOrCreateStatusBarRenderer());
						_currentScene = newStage;
						_pendingScene = null;
						AddChild(newStage);
						OnSceneAdded();
					}
					else
					{
						// WL_GAME.C:Died() with no lives — game over, show high scores then menu
						// Extract final score before sim is discarded
						MapAnalyzer.MapAnalysis mapAnalysis = sim.MapAnalysis;
						MenuRoom gameOverRoom = new(DisplayMode)
						{
							StartMenuOverride = !string.IsNullOrEmpty(deathResult) && deathResult != "gameover"
								? deathResult : null,
							PendingHighScoreScore = sim.Inventory.GetValue("Score"),
							PendingHighScoreCompleted = (ushort)mapAnalysis.Floor,
							PendingHighScoreEpisode = mapAnalysis.Episode,
							MenuWeaponSprite = CurrentGameMenuWeaponSprite(),
							InitialCheatModeEnabled = _cheatModeEnabled,
							InitialUseVoxelWeapons = _useVoxelWeapons,
						};
						_currentScene = gameOverRoom;
						_pendingScene = null;
						AddChild(gameOverRoom);
						OnSceneAdded();
						gameOverRoom.SetFadeTransitionHandler(StartMenuFade);
					}
				});
			}
			else if (actionStage.PendingReturnToMenu)
			{
				SuspendToMenu(actionStage,
					menuOverride: actionStage.PendingMenuOverride,
					equippedWeaponShapes: actionStage.PendingEquippedWeaponShapes,
					completionStats: actionStage.PendingCompletionStats,
					allLevelStats: actionStage.PendingAllLevelStats);
			}
			else if (actionStage.PendingTransition is ActionRoom.LevelTransitionRequest request)
			{
				_suspendedGame = null;
				if (!request.ShowIntermission)
				{
					ActionRoom newStage = new(
						DisplayMode,
						levelIndex: request.LevelIndex,
						savedInventory: request.SavedInventory,
						savedLevelStats: request.AllLevelStats,
						debugMarkersEnabled: _debugMarkersEnabled,
						cheatModeEnabled: _cheatModeEnabled,
						useVoxelWeapons: _useVoxelWeapons,
						playerXOverride: request.PlayerXOverride,
						playerYOverride: request.PlayerYOverride,
						playerAngleOverride: request.PlayerAngleOverride,
						statusBarController: GetOrCreateStatusBarController(),
						statusBarRenderer: GetOrCreateStatusBarRenderer());
					TransitionTo(newStage);
				}
				else
				{
					// Route through intermission/victory screen
					MenuRoom intermissionRoom = new(DisplayMode)
					{
						StartMenuOverride = request.MenuName ?? "LevelComplete",
						LevelTransition = request,
						MenuWeaponSprite = CurrentGameMenuWeaponSprite(),
						InitialVRMode = CurrentVRMode(),
						InitialDebugMarkersEnabled = _debugMarkersEnabled,
						InitialCheatModeEnabled = _cheatModeEnabled,
						InitialUseVoxelWeapons = _useVoxelWeapons,
						StatusBarRenderer = GetOrCreateStatusBarRenderer(),
					};
					// For Victory (episode complete), pass final score for high score check
					if (request.MenuName == "Victory" && request.AllLevelStats?.Count > 0)
					{
						Simulator.LevelCompletionStats lastStats =
							request.AllLevelStats[request.AllLevelStats.Count - 1];
						ushort ep = (ushort)Math.Max(0,
							(request.SavedInventory?.Values?.TryGetValue("Episode", out int episodeVal) ?? false
								? episodeVal : 1) - 1);
						intermissionRoom.PendingHighScoreScore = lastStats.Score;
						intermissionRoom.PendingHighScoreCompleted = (ushort)lastStats.FloorNumber;
						intermissionRoom.PendingHighScoreEpisode = ep;
					}
					TransitionTo(intermissionRoom);
				}
			}
		}
	}
	/// <summary>
	/// Returns the current StatusBarController, creating it if needed.
	/// Creates a fresh controller each time a new game XML is loaded so the state
	/// is initialised from the correct StatusBarDefinition. Returns null when the
	/// current game has no StatusBar defined.
	/// </summary>
	private StatusBarController GetOrCreateStatusBarController()
	{
		StatusBarDefinition statusBarDef = Shared.SharedAssetManager.StatusBar;
		if (statusBarDef is null)
		{
			_statusBarController = null;
			return null;
		}
		_statusBarController ??= new StatusBarController(statusBarDef);
		return _statusBarController;
	}
	/// <summary>
	/// Reads the WeaponSprite attribute from the current game's Menus XML element.
	/// Returns null when no game is loaded or the attribute is absent.
	/// </summary>
	private static string CurrentGameMenuWeaponSprite() =>
		Shared.SharedAssetManager.CurrentGame?.XML
			.Element("VgaGraph")?.Element("Menus")?.Attribute("WeaponSprite")?.Value;
	/// <summary>
	/// Returns the current StatusBarRenderer, creating it if needed.
	/// Creates a fresh renderer each time a new game XML is loaded (controller is reset in InitializeVRAssets).
	/// Returns null when the current game has no StatusBar defined.
	/// </summary>
	private StatusBarRenderer GetOrCreateStatusBarRenderer()
	{
		StatusBarController controller = GetOrCreateStatusBarController();
		if (controller is null)
		{
			_statusBarRenderer = null;
			return null;
		}
		_statusBarRenderer ??= new StatusBarRenderer(controller.State);
		return _statusBarRenderer;
	}
	/// <summary>
	/// Initializes VR rendering assets from the currently loaded game.
	/// Reads the Scale attribute from the VSwap XML element, defaulting to a value
	/// that produces 512-pixel textures. Called after every game load, including the
	/// initial shareware load before the game selection menu is shown.
	/// </summary>
	private void InitializeVRAssets()
	{
		// Reset the status bar renderer and controller so GetOrCreate* rebuilds them
		// from the newly loaded game's StatusBarDefinition on next access.
		_statusBarRenderer?.Dispose();
		_statusBarRenderer = null;
		_statusBarController?.Unsubscribe();
		_statusBarController = null;
		VSwap vswap = Shared.SharedAssetManager.CurrentGame.VSwap;
		byte scaleFactor = byte.TryParse(
			Shared.SharedAssetManager.CurrentGame.XML
				.Element("VSwap")?.Attribute("Scale")?.Value,
			out byte xmlScale)
			? xmlScale
			: (byte)Math.Max(1, 512 / vswap.TileSqrt);
		VRAssetManager.Initialize(scaleFactor: scaleFactor);
	}
	/// <summary>
	/// Reads and executes the mandatory &lt;OnStartup&gt; Lua element from the current game's XML.
	/// Throws if the element is missing or the script fails (hard-crash policy).
	/// </summary>
	private void RunOnStartup()
	{
		_ = Shared.SharedAssetManager.CurrentGame.XML
			.Element("OnStartup")?.Value
			?? throw new InvalidOperationException(
				"Missing required <OnStartup> element in game XML.");
		MenuScriptContext startupCtx = new(new Shared.Menu.MenuState(), Shared.SharedAssetManager.Config)
		{
			NavigateToMenuAction = menuName =>
			{
				if (Shared.SharedAssetManager.CurrentGame.VgaGraph is null)
					throw new InvalidOperationException(
						$"LoadMenu(\"{menuName}\") called but game has no VgaGraph.");
				if (Shared.SharedAssetManager.CurrentGame.MenuCollection?.GetMenu(menuName) is null)
					throw new InvalidOperationException(
						$"LoadMenu(\"{menuName}\"): menu \"{menuName}\" not found.");
				TransitionTo(CreateMenuRoom(menuName));
			},
			StartLevelAction = mapIndex =>
			{
				MapAnalyzer.MapAnalysis[] analyses = Shared.SharedAssetManager.CurrentGame.MapAnalyses;
				if (analyses is null || analyses.Length == 0)
					throw new InvalidOperationException(
						$"StartLevel({mapIndex}) called but game has no maps.");
				if (mapIndex < 0 || mapIndex >= analyses.Length)
					throw new InvalidOperationException(
						$"StartLevel({mapIndex}): index out of range (0\u2013{analyses.Length - 1}).");
				_suspendedGame = null;
				TransitionTo(new ActionRoom(DisplayMode, levelIndex: mapIndex, debugMarkersEnabled: _debugMarkersEnabled, cheatModeEnabled: _cheatModeEnabled, useVoxelWeapons: _useVoxelWeapons, statusBarController: GetOrCreateStatusBarController(), statusBarRenderer: GetOrCreateStatusBarRenderer()));
			},
		};
		(SharedAssetManager.MenuLuaEngine
			?? throw new InvalidOperationException("SharedAssetManager.MenuLuaEngine is null. LoadGame must precompile menu Lua before OnStartup runs."))
			.ExecuteCompiledScript(SharedAssetManager.OnStartupScriptId, startupCtx);
	}
	/// <summary>
	/// Suspends the current game and transitions to the main menu.
	/// Captures the Simulator and player state before destroying the ActionStage.
	/// </summary>
	private void SuspendToMenu(ActionRoom actionStage, string menuOverride = null, IReadOnlyList<ushort?> equippedWeaponShapes = null, Simulator.LevelCompletionStats completionStats = null, IReadOnlyList<Simulator.LevelCompletionStats> allLevelStats = null)
	{
		// Capture state before ActionStage is destroyed
		// Player position/angle are already in the Simulator (updated each frame)
		actionStage.SimulatorController.PreserveSimulator = true;
		_suspendedGame = new SuspendedGameState(
			actionStage.SimulatorController.Simulator);
		MenuRoom menuRoom = new(DisplayMode)
		{
			HasSuspendedGame = true,
			StartMenuOverride = menuOverride ?? SharedAssetManager.CurrentGame?.MenuCollection?.PauseMenu,
			SuspendedSimulator = _suspendedGame.Simulator,
			SuspendedLevelIndex = _suspendedGame.Simulator.Inventory.GetValue("MapOn"),
			MenuWeaponSprite = CurrentGameMenuWeaponSprite(),
			EquippedWeaponShapes = equippedWeaponShapes,
			PendingCompletionStats = completionStats,
			PendingAllLevelStats = allLevelStats,
			PendingQuiz = actionStage.PendingQuiz,
			LevelTransition = actionStage.PendingLevelTransitionForMenu,
			InitialVRMode = CurrentVRMode(),
			InitialDebugMarkersEnabled = _debugMarkersEnabled,
			InitialCheatModeEnabled = _cheatModeEnabled,
			InitialUseVoxelWeapons = _useVoxelWeapons,
			StatusBarRenderer = _statusBarRenderer,
		};
		_pendingScene = menuRoom;
		StartFade(() =>
		{
			if (_currentScene is not null)
			{
				RemoveChild(_currentScene);
				_currentScene.QueueFree();
			}
			_currentScene = menuRoom;
			_pendingScene = null;
			AddChild(menuRoom);
			OnSceneAdded();
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
		if (_suspendedGame is null)
			return;
		SuspendedGameState state = _suspendedGame;
		ActionRoom actionStage = new(
			DisplayMode,
			state.Simulator,
			debugMarkersEnabled: _debugMarkersEnabled,
			cheatModeEnabled: _cheatModeEnabled,
			useVoxelWeapons: _useVoxelWeapons,
			statusBarController: _statusBarController,
			statusBarRenderer: _statusBarRenderer);
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
		if (saveFile?.Snapshot is null)
		{
			GD.PrintErr($"ERROR: Failed to load save game from slot {slot}");
			return;
		}
		// Discard any suspended game
		_suspendedGame = null;
		// Create new ActionStage with the saved snapshot
		// Level index is read from the snapshot's MapOn inventory value
		ActionRoom actionStage = new(DisplayMode, saveFile.Snapshot, debugMarkersEnabled: _debugMarkersEnabled, cheatModeEnabled: _cheatModeEnabled, useVoxelWeapons: _useVoxelWeapons, statusBarController: GetOrCreateStatusBarController(), statusBarRenderer: GetOrCreateStatusBarRenderer());
		TransitionTo(actionStage);
	}
	private MenuRoom CreateMenuRoom(string startMenuOverride)
	{
		return new MenuRoom(DisplayMode)
		{
			StartMenuOverride = startMenuOverride,
			MenuWeaponSprite = CurrentGameMenuWeaponSprite(),
			InitialVRMode = CurrentVRMode(),
			InitialDebugMarkersEnabled = _debugMarkersEnabled,
			InitialCheatModeEnabled = _cheatModeEnabled,
			InitialUseVoxelWeapons = _useVoxelWeapons,
		};
	}
	private VRMode CurrentVRMode() =>
		DisplayMode is VRDisplayMode vrDisplayMode && vrDisplayMode.PlayMode == VRPlayMode.FiveDOF
			? VRMode.FiveDOF
			: VRMode.Roomscale;
	/// <summary>
	/// Transitions to a new scene with fade-to-black and fade-from-black.
	/// Skips fade-out when the current scene has IRoom.SkipFade (already black background).
	/// Skips fade-in when the incoming scene has IRoom.SkipFade (already black background).
	/// </summary>
	public void TransitionTo(Node newScene)
	{
		if (_errorMode || _transitionState != TransitionState.Idle)
			return;
		bool skipFadeOut = (_currentScene as IRoom)?.SkipFade ?? false,
			skipFadeIn = (newScene as IRoom)?.SkipFade ?? false;
		_pendingScene = newScene;
		if (skipFadeOut)
		{
			// Current scene already has a black background — swap immediately then fade in.
			if (_currentScene is not null)
			{
				RemoveChild(_currentScene);
				_currentScene.QueueFree();
			}
			_currentScene = _pendingScene;
			_pendingScene = null;
			AddChild(_currentScene);
			OnSceneAdded();
			(_currentScene as IRoom)?.SetFadeTransitionHandler(StartMenuFade);
			// Ensure the overlay is opaque while we wait for the room to become ready,
			// then let BlackAfterSwap poll PrepareForFadeIn() before starting the fade-in.
			_fadeOverlay.SetBlack();
			Simulator.Simulator.Paused = true;
			GetTree().Paused = true;
			_transitionState = TransitionState.BlackAfterSwap;
		}
		else
		{
			StartFade(() =>
			{
				// Swap scenes while screen is fully black
				if (_currentScene is not null)
				{
					RemoveChild(_currentScene);
					_currentScene.QueueFree();
				}
				_currentScene = _pendingScene;
				_pendingScene = null;
				AddChild(_currentScene);
				OnSceneAdded();
				(_currentScene as IRoom)?.SetFadeTransitionHandler(StartMenuFade);
			}, skipFadeIn: skipFadeIn);
		}
	}
	/// <summary>
	/// Updates the fade overlay's VR camera reference after a scene is added.
	/// Every room calls _displayMode.Initialize() in _Ready() (triggered by AddChild),
	/// so DisplayMode.Camera is valid by the time this runs.
	/// </summary>
	private void OnSceneAdded()
	{
		_fadeOverlay.SetVRCamera(DisplayMode.IsVRActive ? DisplayMode.Camera : null);
		UpdateSpectatorView();
	}
	/// <summary>
	/// Transitions to a new scene immediately without fading.
	/// Used for the initial boot scene.
	/// </summary>
	private void TransitionToImmediate(Node newScene)
	{
		if (_errorMode)
			return;
		if (_currentScene is not null)
		{
			RemoveChild(_currentScene);
			_currentScene.QueueFree();
		}
		_currentScene = newScene;
		AddChild(_currentScene);
		OnSceneAdded();
	}
	private void UpdateSpectatorView()
	{
		if (_spectatorView is null)
			return;
		if (_currentScene is ActionRoom && DisplayMode.IsVRActive)
			_spectatorView.AttachTo(DisplayMode.Origin, DisplayMode.Camera);
		else if (_currentScene is SetupRoom setupRoom && DisplayMode.IsVRActive)
			_spectatorView.AttachTexture(setupRoom.SpectatorTexture);
		else if (_currentScene is MenuRoom menuRoom && DisplayMode.IsVRActive)
			_spectatorView.AttachTexture(menuRoom.SpectatorTexture);
		else
			_spectatorView.Detach();
	}
	/// <summary>
	/// Starts a fade-out, executes the action at mid-fade, then fades back in.
	/// Pass skipFadeIn=true when the incoming scene has a black background (IRoom.SkipFade).
	/// Used for both scene transitions and menu screen navigations.
	/// </summary>
	private void StartFade(System.Action midFadeAction, bool skipFadeIn = false)
	{
		_skipFadeIn = skipFadeIn;
		_pendingMidFadeAction = midFadeAction;
		_transitionState = TransitionState.FadingOut;
		Simulator.Simulator.Paused = true;
		GetTree().Paused = true;
		// Disable VR locomotion so the player stays put during the fade.
		// Each room's _Ready() sets LocomotionEnabled to its own desired value when it loads.
		DisplayMode.LocomotionEnabled = false;
		_fadeOverlay.FadeToBlack();
	}
	/// <summary>
	/// Callback for menu screen navigations (Main → Episodes, etc.).
	/// Wraps the menu navigation in a fade transition.
	/// </summary>
	private void StartMenuFade(System.Action menuNavigation)
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
	/// Called when fade-to-black completes (alpha = 1).
	/// Transitions to BlackBeforeSwap so _Process guarantees at least one fully-black
	/// rendered frame before the scene swap, and at least one more after it, before
	/// starting the fade-in. This prevents the VR compositor from displaying partial
	/// frames during the heavy scene-swap work.
	/// </summary>
	private void OnFadeOutComplete() =>
		_transitionState = TransitionState.BlackBeforeSwap;
	/// <summary>
	/// Called when fade-from-black completes. Returns to idle and unpauses.
	/// </summary>
	private void OnFadeInComplete()
	{
		_transitionState = TransitionState.Idle;
		Simulator.Simulator.Paused = false;
		GetTree().Paused = false;
	}
	/// <summary>
	/// Displays an exception to the user via DOS screen.
	/// Called by ExceptionHandler when an unhandled exception occurs.
	/// Routes to the current SetupRoom if one is active; otherwise creates a fresh SetupRoom.
	/// SetupRoom owns the DosScreen and all display setup (VR quad or flatscreen canvas).
	/// </summary>
	/// <param name="ex">The exception to display</param>
	private void ShowErrorScreen(Exception ex)
	{
		_errorMode = true;
		if (_currentScene is SetupRoom setupRoom)
		{
			setupRoom.ShowError(ex);
			return;
		}
		// Not in a SetupRoom (e.g. error during ActionRoom/MenuRoom) — discard the current
		// scene and create a fresh SetupRoom solely for error display.
		if (_currentScene is not null)
		{
			RemoveChild(_currentScene);
			_currentScene.QueueFree();
			_currentScene = null;
		}
		try
		{
			SetupRoom errorRoom = new(DisplayMode, xmlPath: string.Empty, isInitialLoad: false);
			_currentScene = errorRoom;
			AddChild(errorRoom); // triggers _Ready(): initializes display and DosScreen
			OnSceneAdded();
			errorRoom.ShowError(ex);
		}
		catch (Exception displayEx)
		{
			GD.PrintErr($"ERROR: Failed to display error screen: {displayEx}");
			System.Environment.FailFast($"Unhandled exception: {ex.Message}", ex);
		}
	}
}
