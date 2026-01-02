using BenMcLean.Wolf3D.Assets.Sound;
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
	public static AudioT.Music Music
	{
		get => music;
		set
		{
			// System-wide rule: Don't restart music if same song is already playing
			// To restart a song, caller must first call StopMusic (set to null) then PlayMusic
			if (value is not null && value.Equals(music))
				return;
			music = value;
			if (//Settings.MusicMuted ||
				music is not AudioT.Music s)
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
	private static AudioT.Music music = null;
	public static Adl Adl
	{
		get => throw new NotImplementedException();
		set => IdAdlSignaller.IdAdlQueue.Enqueue(/*Settings.FXMuted ? null :*/ value);
	}
}
