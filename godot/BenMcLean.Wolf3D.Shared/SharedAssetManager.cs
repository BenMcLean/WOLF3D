using System;

namespace BenMcLean.Wolf3D.Shared;

/// <summary>
/// Singleton asset manager that provides global access to the currently loaded game assets.
/// Serves as the single source of truth for Assets.Assets across all display layers.
/// </summary>
public static class SharedAssetManager
{
	/// <summary>
	/// The currently loaded game data (VSwap, Maps, etc.).
	/// </summary>
	public static Assets.Assets CurrentGame { get; private set; }

	// TODO: Add DigiSounds (AudioStreamWAV) when implementing audio
	// public static Dictionary<int, AudioStreamWAV> DigiSounds { get; private set; }

	// TODO: Add Music player when implementing audio
	// public static IMusicPlayer MusicPlayer { get; private set; }

	// TODO: Add VgaGraph texture atlas and UI integration when implementing menus
	// Will use texture atlas approach + Godot UI system

	/// <summary>
	/// Loads the game selection menu using embedded WL1 shareware assets.
	/// This is displayed before the user selects which game to play.
	/// </summary>
	public static void LoadGameSelectionMenu()
	{
		// TODO: Load embedded WL1 shareware from Resources folder
		throw new NotImplementedException("LoadGameSelectionMenu - need embedded WL1 shareware resource");
	}

	/// <summary>
	/// Loads a game from the user's games folder.
	/// </summary>
	/// <param name="xmlPath">Path to the game's XML definition file (e.g., "WL6.xml")</param>
	public static void LoadGame(string xmlPath)
	{
		// Cleanup old resources
		Cleanup();

		// Load game data
		CurrentGame = Assets.Assets.Load(xmlPath);

		// TODO: Create AudioStreamWAV from DigiSounds when implementing audio
		// TODO: Initialize music player when implementing audio
		// TODO: Create VgaGraph texture atlas when implementing menus
	}

	/// <summary>
	/// Disposes all loaded resources to free memory.
	/// Called before loading a new game or on program exit.
	/// </summary>
	public static void Cleanup()
	{
		// TODO: Dispose audio resources when implemented
		// foreach (var sound in DigiSounds?.Values ?? [])
		//     sound?.Dispose();
		// DigiSounds?.Clear();
		// MusicPlayer?.Dispose();

		// TODO: Dispose VgaGraph texture atlas when implemented

		CurrentGame = null;
	}
}
