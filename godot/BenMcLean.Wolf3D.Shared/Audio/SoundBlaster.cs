using System;
using System.IO;
using BenMcLean.Wolf3D.Assets.Sound;
using BenMcLean.Wolf3D.Shared.Audio.OPL;
using Godot;
using NScumm.Audio.OPL.Woody;
using NScumm.Core.Audio.OPL;

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
	public readonly AdlSignaller IdAdlSignaller = new();
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
	public readonly AudioStreamPlayer DirectionlessBusSpeaker = new()
	{
		Name = "DirectionlessBusSpeaker",
		Bus = "Directionless",
	};
	#endregion Audio Players
	private string currentMusicName = null;
	private Assets.Gameplay.Config _subscribedConfig = null;
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
		AddChild(DirectionlessBusSpeaker);
		// Subscribe to ConfigReplaced so we re-subscribe whenever the Config object changes.
		// Config is null at _Ready() time (loaded later by SetupRoom), so we can't
		// subscribe to its events here directly.
		SharedAssetManager.ConfigReplaced += OnConfigReplaced;
		SubscribeToConfig(SharedAssetManager.Config);
		// Subscribe to event bus
		EventBus.Subscribe(GameEvent.PlaySound, OnPlaySoundEvent);
		EventBus.Subscribe(GameEvent.PlayMusic, OnPlayMusicEvent);
		EventBus.Subscribe(GameEvent.StopMusic, OnStopMusicEvent);
	}
	public override void _ExitTree()
	{
		SharedAssetManager.ConfigReplaced -= OnConfigReplaced;
		SubscribeToConfig(null);
		// Unsubscribe from event bus
		EventBus.Unsubscribe(GameEvent.PlaySound, OnPlaySoundEvent);
		EventBus.Unsubscribe(GameEvent.PlayMusic, OnPlayMusicEvent);
		EventBus.Unsubscribe(GameEvent.StopMusic, OnStopMusicEvent);
	}
	private void OnConfigReplaced(Assets.Gameplay.Config newConfig) => SubscribeToConfig(newConfig);
	private void SubscribeToConfig(Assets.Gameplay.Config config)
	{
		if (_subscribedConfig is not null)
		{
			_subscribedConfig.MusicEnabledChanged -= OnMusicEnabledChanged;
			_subscribedConfig.SoundModeChanged -= OnSoundModeChanged;
			_subscribedConfig.DigiModeChanged -= OnDigiModeChanged;
		}
		_subscribedConfig = config;
		if (_subscribedConfig is not null)
		{
			_subscribedConfig.MusicEnabledChanged += OnMusicEnabledChanged;
			_subscribedConfig.SoundModeChanged += OnSoundModeChanged;
			_subscribedConfig.DigiModeChanged += OnDigiModeChanged;
		}
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
	private void OnDigiModeChanged(object sender, EventArgs e)
	{
		if (SharedAssetManager.Config?.DigiMode == Assets.Gameplay.Config.SDSMode.Off)
			DirectionlessBusSpeaker.Stop();
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
		// Check pre-loaded raw IMF songs (no game/AudioT needed)
		if (SharedAssetManager.RawImfSongs.TryGetValue(musicName, out Assets.Sound.Imf[] rawImf))
		{
			ImfSignaller.ImfQueue.Enqueue(rawImf);
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
	/// Plays a sound effect by name. Automatically routes based on SoundMode.
	/// Priority: DigiSound (Sound Blaster) > PC Speaker > AdLib
	/// </summary>
	/// <param name="soundName">Name of the sound effect (e.g., "HITWALLSND", "ATKPISTOLSND")</param>
	/// <remarks>
	/// Sound playback priority (authentic Wolf3D behavior):
	/// 1. DigiSound (digitized Sound Blaster audio) - if available and SoundMode != Off
	/// 2. PC Speaker - if SoundMode == PC and DigiSound not available
	/// 3. AdLib (FM synthesis) - fallback if neither DigiSound nor PC Speaker available
	/// </remarks>
	public void PlaySound(string soundName)
	{
		if (SharedAssetManager.Config?.SoundMode == Assets.Gameplay.Config.SDMode.Off ||
			string.IsNullOrWhiteSpace(soundName))
			return;

		// Try DigiSound first (highest priority - digitized Sound Blaster audio)
		if (SharedAssetManager.Config.DigiMode is Assets.Gameplay.Config.SDSMode.SoundBlaster or Assets.Gameplay.Config.SDSMode.SoundSource &&
			SharedAssetManager.DigiSounds.TryGetValue(soundName, out AudioStreamWav stream))
		{
			DirectionlessBusSpeaker.Stream = stream;
			DirectionlessBusSpeaker.Play();
			return;
		}

		// Route to PC Speaker if in PC mode and DigiSound not available
		if (SharedAssetManager.Config.SoundMode == Assets.Gameplay.Config.SDMode.PC &&
			SharedAssetManager.CurrentGame?.AudioT?.PcSounds is not null &&
			SharedAssetManager.CurrentGame.AudioT.PcSounds.TryGetValue(soundName, out PcSpeaker pcSound))
		{
			PcSpeakerPlayer.PlaySound(pcSound);
			return;
		}

		// Fallback to AdLib (FM synthesis beeps)
		if (SharedAssetManager.CurrentGame?.AudioT?.Sounds is not null &&
			SharedAssetManager.CurrentGame.AudioT.Sounds.TryGetValue(soundName, out Adl adlSound))
			AdlSignaller.IdAdlQueue.Enqueue(adlSound);
		else
			GD.PrintErr($"ERROR: Sound '{soundName}' not found");
	}
	#endregion Sound Effects API
}
