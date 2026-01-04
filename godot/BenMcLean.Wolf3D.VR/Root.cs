using System;
using Godot;

namespace BenMcLean.Wolf3D.VR;

/// <summary>
/// Root node for the VR application.
/// Manages global VR environment (sky, lighting) and scene transitions.
/// </summary>
public partial class Root : Node3D
{
	[Export]
	public int CurrentLevelIndex { get; set; } = 0;

	private Node _currentScene;
	private Shared.DosScreen _errorScreen;
	private bool _errorMode = false;

	public override void _Ready()
	{
		// Register error display callback
		ExceptionHandler.DisplayCallback = ShowErrorScreen;

		try
		{
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

			// TEMPORARY TEST: Load AudioT from N3D.xml and play the first MIDI song
			//System.Xml.Linq.XDocument n3dXml = System.Xml.Linq.XDocument.Load(@"..\..\games\N3D.xml");
			//Assets.AudioT n3dAudioT = Assets.AudioT.Load(n3dXml.Root, @"..\..\games\N3D");
			//if (n3dAudioT.Songs.Count > 0)
			//{
			//	Assets.AudioT.Song firstSong = n3dAudioT.Songs.Values.First();
			//	Shared.OPL.SoundBlaster.Song = firstSong;
			//}

			// Boot to MenuStage
			Shared.Menu.MenuStage menuStage = new();
			TransitionTo(menuStage);
		}
		catch (Exception ex)
		{
			ExceptionHandler.HandleException(ex);
		}
	}

	public override void _Process(double delta)
	{
		// Poll MenuStage for game start signal
		if (_currentScene is Shared.Menu.MenuStage menuStage)
		{
			if (menuStage.SessionState?.StartGame ?? false)
			{
				// Get selected episode and difficulty from menu
				int episode = menuStage.SessionState.SelectedEpisode;
				int difficulty = menuStage.SessionState.SelectedDifficulty;
				// TODO: Use episode and difficulty when creating ActionStage
				ActionStage actionStage = new() { LevelIndex = CurrentLevelIndex };
				TransitionTo(actionStage);
			}
		}
	}
	/// <summary>
	/// Transitions to a new scene, replacing the current one.
	/// </summary>
	/// <param name="newScene">The scene node to transition to</param>
	public void TransitionTo(Node newScene)
	{
		// Don't allow transitions in error mode
		if (_errorMode)
			return;

		// Remove and free the current scene
		if (_currentScene != null)
		{
			RemoveChild(_currentScene);
			_currentScene.QueueFree();
		}

		// Add the new scene
		_currentScene = newScene;
		AddChild(_currentScene);
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
