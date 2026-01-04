using BenMcLean.Wolf3D.Assets.Sound;
using BenMcLean.Wolf3D.Shared.Audio.OPL;
using Godot;
using NScumm.Audio.OPL.Woody;
using NScumm.Core.Audio.OPL;
using System;

namespace BenMcLean.Wolf3D.Shared.Audio;

/// <summary>
/// Central audio management node for Wolfenstein 3D.
/// Manages both AdLib/OPL and PC Speaker audio playback.
/// Automatically routes sounds to the appropriate player based on SoundMode setting.
/// </summary>
public partial class SoundBlaster : Node
{
	#region Audio Players
	public readonly ImfSignaller ImfSignaller = new();
	public readonly IdAdlSignaller IdAdlSignaller = new();
	public readonly MidiSignaller MidiSignaller = new();
	public readonly OplPlayer OplPlayer = new()
	{
		Name = "OplPlayer",
		Opl = new WoodyEmulatorOpl(OplType.Opl2),
		Bus = "OPL",
	};
	public readonly PcSpeakerPlayer PcSpeakerPlayer = new()
	{
		Name = "PcSpeakerPlayer",
		Bus = "PcSpeaker",
	};
	#endregion Audio Players
	private string currentMusicName = null;
	public SoundBlaster()
	{
		Name = "SoundBlaster";
		OplPlayer.AdlibSignaller = new AdlibMultiplexer(ImfSignaller, IdAdlSignaller);
	}
	public override void _Ready()
	{
		// Add audio players as children
		AddChild(OplPlayer);
		AddChild(PcSpeakerPlayer);
		// Subscribe to config events
		if (SharedAssetManager.Config is not null)
		{
			SharedAssetManager.Config.MusicEnabledChanged += OnMusicEnabledChanged;
			SharedAssetManager.Config.SoundModeChanged += OnSoundModeChanged;
		}
		// Subscribe to event bus
		EventBus.Subscribe(GameEvent.PlaySound, OnPlaySoundEvent);
		EventBus.Subscribe(GameEvent.PlayMusic, OnPlayMusicEvent);
		EventBus.Subscribe(GameEvent.StopMusic, OnStopMusicEvent);
	}
	public override void _ExitTree()
	{
		// Unsubscribe from config events
		if (SharedAssetManager.Config is not null)
		{
			SharedAssetManager.Config.MusicEnabledChanged -= OnMusicEnabledChanged;
			SharedAssetManager.Config.SoundModeChanged -= OnSoundModeChanged;
		}
		// Unsubscribe from event bus
		EventBus.Unsubscribe(GameEvent.PlaySound, OnPlaySoundEvent);
		EventBus.Unsubscribe(GameEvent.PlayMusic, OnPlayMusicEvent);
		EventBus.Unsubscribe(GameEvent.StopMusic, OnStopMusicEvent);
	}
	#region Event Handlers
	private void OnMusicEnabledChanged(object sender, EventArgs e)
	{
		if (SharedAssetManager.Config is not null &&
			!SharedAssetManager.Config.MusicEnabled)
			StopMusic();
	}
	private void OnSoundModeChanged(object sender, EventArgs e)
	{
		// Sound effects are fire-and-forget, no action needed
	}
	private void OnPlaySoundEvent(object data)
	{
		if (data is string soundName)
			PlaySound(soundName);
	}
	private void OnPlayMusicEvent(object data)
	{
		if (data is string musicName)
			PlayMusic(musicName);
	}
	private void OnStopMusicEvent(object data) => StopMusic();
	#endregion Event Handlers
	#region Music API
	/// <summary>
	/// Plays a music track by name.
	/// </summary>
	/// <param name="musicName">Name of the music track (e.g., "CORNER_MUS"), or null to stop music</param>
	/// <remarks>
	/// Won't restart if the same song is already playing.
	/// To restart a song, call StopMusic() first, then PlayMusic().
	/// </remarks>
	public void PlayMusic(string musicName)
	{
		// Don't restart if same song is already playing
		if (musicName == currentMusicName)
			return;
		currentMusicName = musicName;
		// Stop music if null or music disabled
		if (string.IsNullOrEmpty(musicName) || SharedAssetManager.Config?.MusicEnabled != true)
		{
			ImfSignaller.ImfQueue.Enqueue(null);
			MidiSignaller.Midi = null;
			OplPlayer.AdlibSignaller = new AdlibMultiplexer(ImfSignaller, IdAdlSignaller);
			return;
		}
		// Look up the music
		if (SharedAssetManager.CurrentGame?.AudioT?.Songs == null)
		{
			GD.PrintErr($"ERROR: AudioT not loaded");
			return;
		}
		if (!SharedAssetManager.CurrentGame.AudioT.Songs.TryGetValue(musicName, out AudioT.Music song))
		{
			GD.PrintErr($"ERROR: Music '{musicName}' not found in AudioT");
			return;
		}
		// Play the music
		if (song.IsImf)
		{
			ImfSignaller.ImfQueue.Enqueue(song.Imf);
			OplPlayer.AdlibSignaller = new AdlibMultiplexer(ImfSignaller, IdAdlSignaller);
		}
		else
		{
			MidiSignaller.Midi = song.Midi;
			OplPlayer.AdlibSignaller = new AdlibMultiplexer(MidiSignaller, IdAdlSignaller);
		}
	}
	/// <summary>
	/// Stops the currently playing music.
	/// </summary>
	public void StopMusic()
	{
		currentMusicName = null;
		ImfSignaller.ImfQueue.Enqueue(null);
		MidiSignaller.Midi = null;
		OplPlayer.AdlibSignaller = new AdlibMultiplexer(ImfSignaller, IdAdlSignaller);
	}
	#endregion Music API
	#region Sound Effects API
	/// <summary>
	/// Plays a sound effect by name. Automatically routes to PC Speaker or AdLib based on SoundMode.
	/// </summary>
	/// <param name="soundName">Name of the sound effect (e.g., "HITWALLSND")</param>
	/// <remarks>
	/// When SoundMode is PC, plays the PC Speaker version.
	/// When SoundMode is AdLib, plays the AdLib version.
	/// When SoundMode is Off, no sound plays.
	/// </remarks>
	public void PlaySound(string soundName)
	{
		if (SharedAssetManager.Config?.SoundMode == Assets.Gameplay.Config.SDMode.Off ||
			string.IsNullOrWhiteSpace(soundName))
			return;
		// Route to PC Speaker if in PC mode
		if (SharedAssetManager.Config.SoundMode == Assets.Gameplay.Config.SDMode.PC)
		{
			if (SharedAssetManager.CurrentGame?.AudioT?.PcSounds is not null &&
				SharedAssetManager.CurrentGame.AudioT.PcSounds.TryGetValue(soundName, out PcSpeaker pcSound))
				PcSpeakerPlayer.PlaySound(pcSound);
			else
				// If PC sound not found, warn and fall through to AdLib (degraded mode)
				GD.PrintErr($"Warning: PC Speaker sound '{soundName}' not found, falling back to AdLib");
		}
		// Route to AdLib
		if (SharedAssetManager.CurrentGame?.AudioT?.Sounds is not null &&
			SharedAssetManager.CurrentGame.AudioT.Sounds.TryGetValue(soundName, out Adl adlSound))
			IdAdlSignaller.IdAdlQueue.Enqueue(adlSound);
		else
			GD.PrintErr($"ERROR: Sound '{soundName}' not found in AudioT");
	}
	#endregion Sound Effects API
}
