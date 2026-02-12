using System;
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

	private Node _currentScene;
	private Node _pendingScene;
	private Shared.DosScreen _errorScreen;
	private bool _errorMode = false;
	private ScreenFadeOverlay _fadeOverlay;
	private TransitionState _transitionState = TransitionState.Idle;

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

			// Boot to MenuRoom - no fade for initial scene
			MenuRoom menuRoom = new(DisplayMode);
			TransitionToImmediate(menuRoom);
		}
		catch (Exception ex)
		{
			ExceptionHandler.HandleException(ex);
		}
	}

	public override void _Process(double delta)
	{
		// Skip movement processing during transitions to prevent input leaking through
		if (_transitionState == TransitionState.Idle)
			DisplayMode?.Update(delta);

		// Don't poll for new transitions while one is in progress
		if (_transitionState != TransitionState.Idle)
			return;

		// Poll MenuRoom for game start signal
		if (_currentScene is MenuRoom menuRoom)
		{
			if (menuRoom.ShouldStartGame)
			{
				// Get selected episode and difficulty from menu
				int episode = menuRoom.SelectedEpisode;
				int difficulty = menuRoom.SelectedDifficulty;
				// TODO: Use episode and difficulty when creating ActionStage
				ActionStage actionStage = new(DisplayMode) { LevelIndex = CurrentLevelIndex };
				TransitionTo(actionStage);
			}
		}
		// Poll ActionStage for level transition requests (elevator)
		else if (_currentScene is ActionStage actionStage)
		{
			if (actionStage.PendingTransition is ActionStage.LevelTransitionRequest request)
			{
				ActionStage newStage = new(DisplayMode, request.SavedInventory, request.SavedWeaponType)
				{
					LevelIndex = request.LevelIndex
				};
				TransitionTo(newStage);
			}
		}
	}

	/// <summary>
	/// Transitions to a new scene with fade-to-black and fade-from-black.
	/// </summary>
	public void TransitionTo(Node newScene)
	{
		if (_errorMode || _transitionState != TransitionState.Idle)
			return;

		_pendingScene = newScene;
		_transitionState = TransitionState.FadingOut;
		GetTree().Paused = true;
		_fadeOverlay.FadeToBlack(FadeDuration);
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
	/// Called when fade-to-black completes. Swaps scenes and starts fade-in.
	/// </summary>
	private void OnFadeOutComplete()
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
