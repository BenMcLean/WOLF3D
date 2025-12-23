using System.Linq;
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

	public override void _Ready()
	{
		// Load game assets
		// TODO: Eventually this will be done from menu selection, not hardcoded
		Shared.SharedAssetManager.LoadGame(@"..\..\games\WL1.xml");

		// Create VR-specific 3D materials
		// Try scaleFactor: 4 for better performance, or 8 for maximum quality
		VRAssetManager.Initialize(scaleFactor: 8);

		// Add OplPlayer to scene tree for music
		AddChild(Shared.OPL.SoundBlaster.OplPlayer);

		// Play the first level's music
		string songName = Shared.SharedAssetManager.CurrentGame.MapAnalyses[CurrentLevelIndex].Song;
		if (!string.IsNullOrWhiteSpace(songName)
			&& Shared.SharedAssetManager.CurrentGame.AudioT.Songs.TryGetValue(songName, out Assets.AudioT.Song song))
			Shared.OPL.SoundBlaster.Song = song;

		// TEMPORARY TEST: Load AudioT from N3D.xml and play the first MIDI song
		//System.Xml.Linq.XDocument n3dXml = System.Xml.Linq.XDocument.Load(@"..\..\games\N3D.xml");
		//Assets.AudioT n3dAudioT = Assets.AudioT.Load(n3dXml.Root, @"..\..\games\N3D");
		//if (n3dAudioT.Songs.Count > 0)
		//{
		//	Assets.AudioT.Song firstSong = n3dAudioT.Songs.Values.First();
		//	Shared.OPL.SoundBlaster.Song = firstSong;
		//}

		// For now, boot directly into ActionStage
		// TODO: Boot to DOSScreen → MenuStage → ActionStage
		ActionStage actionStage = new() { LevelIndex = CurrentLevelIndex };
		TransitionTo(actionStage);
	}

	/// <summary>
	/// Transitions to a new scene, replacing the current one.
	/// </summary>
	/// <param name="newScene">The scene node to transition to</param>
	public void TransitionTo(Node newScene)
	{
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
}
