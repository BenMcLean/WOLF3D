using BenMcLean.Wolf3D.Assets;
using Godot;
using NScumm.Audio.OPL.Woody;
using NScumm.Core.Audio.OPL;
using System;

namespace BenMcLean.Wolf3D.Shared.OPL;

public static class SoundBlaster
{
	public static readonly AudioStreamPlayer AudioStreamPlayer = new()
	{
		Name = "AudioStreamPlayer",
		Bus = "Directionless",
	};
	public static readonly ImfSignaller ImfSignaller = new();
	public static readonly IdAdlSignaller IdAdlSignaller = new();
	public static readonly MidiSignaller MidiSignaller = new();
	public static readonly OplPlayer OplPlayer = new()
	{
		Opl = new WoodyEmulatorOpl(OplType.Opl2),
		AdlibSignaller = new AdlibMultiplexer(ImfSignaller, IdAdlSignaller),
		Bus = "OPL",
	};
	//public static readonly Node MidiPlayer = (Node)GD.Load<GDScript>("res://addons/midi/MidiPlayer.gd").New();
	//public static readonly Reference SMF = (Reference)GD.Load<GDScript>("res://addons/midi/SMF.gd").New();

	static SoundBlaster()
	{
		//MidiPlayer.Name = "MidiPlayer";
		//MidiPlayer.Set("soundfont", "res://1mgm.sf2");
		//MidiPlayer.Set("loop", true);
		//MidiPlayer.Set("bus", "Directionless");
	}

	public static AudioT.Song Song
	{
		get => song;
		set
		{
			if (//Settings.MusicMuted ||
				(song = value) is not AudioT.Song s)
			{
				ImfSignaller.ImfQueue.Enqueue(null);
				MidiSignaller.Midi = null;
				OplPlayer.AdlibSignaller = new AdlibMultiplexer(ImfSignaller, IdAdlSignaller);
			}
			else if (s.IsImf)
			{
				ImfSignaller.ImfQueue.Enqueue(s.Imf);
				OplPlayer.AdlibSignaller = new AdlibMultiplexer(ImfSignaller, IdAdlSignaller);
			}
			else
			{
				MidiSignaller.Midi = s.Midi;
				OplPlayer.AdlibSignaller = new AdlibMultiplexer(MidiSignaller, IdAdlSignaller);
			}
		}
	}
	private static AudioT.Song song = null;

	public static Adl Adl
	{
		get => throw new NotImplementedException();
		set => IdAdlSignaller.IdAdlQueue.Enqueue(/*Settings.FXMuted ? null :*/ value);
	}
	/*
	public static void Play(XElement xml, ISpeaker iSpeaker = null)
	{
		if (//!Settings.MusicMuted &&
			xml?.Attribute("Song")?.Value is string songName
			&& !string.IsNullOrWhiteSpace(songName)
			&& SharedAssetManager.CurrentGame.AudioT.Songs[songName] is AudioT.Song song
			&& (Song != song || xml.IsTrue("OverrideSong")))
			Song = song;
		if (//!Settings.DigiSoundMuted &&
			xml?.Attribute("DigiSound")?.Value is string digiSound
			&& !string.IsNullOrWhiteSpace(digiSound)
			&& SharedAssetManager.DigiSound(digiSound) is AudioStreamSample audioStreamSample)
			if (iSpeaker != null)
				iSpeaker.Play = audioStreamSample;
			else
			{
				AudioStreamPlayer.Stream = audioStreamSample;
				AudioStreamPlayer.Play();
			}
		else if (//!Settings.FXMuted &&
			xml?.Attribute("Sound")?.Value is string sound
			&& !string.IsNullOrWhiteSpace(sound)
			&& Assets.Sound(sound) is Adl adl)
			Adl = adl;
	}
	*/
}
