using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace BenMcLean.Wolf3D.Assets.Sound;

/// <summary>
/// AUDIOT file loader - Wolf3D audio resource format
/// File uses 32-bit offsets to locate AdLib sounds and IMF music
/// </summary>
public sealed class AudioT
{
	public sealed class SoundInfo
	{
		public required uint Number { get; init; }
		public required string Name { get; init; }
		public string DigiSoundName { get; init; }
	}
	public static AudioT Load(XElement xml, string folder = "")
	{
		XElement el   = xml.Element("Audio");
		string head   = el?.Attribute("AudioHead")?.Value;
		string audioT = el?.Attribute("AudioT")?.Value;
		if (head is null || audioT is null)
			return null;
		using FileStream audioHead    = new(Path.Combine(folder, head),   FileMode.Open);
		using FileStream audioTStream = new(Path.Combine(folder, audioT), FileMode.Open);
		return new AudioT(audioHead, audioTStream, el);
	}
	public Dictionary<string, SoundInfo> LogicalSounds { get; private init; }
	public Dictionary<string, string> LogicalToDigiSoundName { get; private init; }
	public Dictionary<string, string> DigiToLogicalSoundName { get; private init; }
	public Dictionary<string, PcSpeaker> PcSounds { get; private init; }
	public Dictionary<string, Adl> Sounds { get; private init; }
	public Dictionary<string, Music> Songs { get; private init; }
	public bool HasLogicalSound(string soundName) =>
		!string.IsNullOrWhiteSpace(soundName) && LogicalSounds.ContainsKey(soundName);
	public string ResolveLogicalSoundName(string requestedSoundName)
	{
		if (string.IsNullOrWhiteSpace(requestedSoundName) || HasLogicalSound(requestedSoundName))
			return requestedSoundName;
		return DigiToLogicalSoundName.TryGetValue(requestedSoundName, out string logicalSoundName)
			? logicalSoundName
			: requestedSoundName;
	}
	public bool HasPlayableSound(string requestedSoundName) =>
		TryResolvePlayableSoundName(requestedSoundName, out _);
	public string ResolvePlayableSoundName(string requestedSoundName) =>
		TryResolvePlayableSoundName(requestedSoundName, out string resolvedSoundName)
			? resolvedSoundName
			: null;
	public bool TryResolvePlayableSoundName(string requestedSoundName, out string resolvedSoundName)
	{
		resolvedSoundName = null;
		if (string.IsNullOrWhiteSpace(requestedSoundName))
			return false;
		string logicalSoundName = ResolveLogicalSoundName(requestedSoundName);
		if (!HasLogicalSound(logicalSoundName))
			return false;
		resolvedSoundName = logicalSoundName;
		return true;
	}
	public bool TryGetMappedDigiSoundName(string requestedSoundName, out string digiSoundName)
	{
		digiSoundName = null;
		if (string.IsNullOrWhiteSpace(requestedSoundName))
			return false;
		return TryResolvePlayableSoundName(requestedSoundName, out string logicalSoundName) &&
			LogicalToDigiSoundName.TryGetValue(logicalSoundName, out digiSoundName);
	}
	public static uint[] ParseHead(Stream stream)
	{
		List<uint> list = [];
		using (BinaryReader binaryReader = new(stream))
			while (stream.Position < stream.Length)
				list.Add(binaryReader.ReadUInt32());
		return [.. list];
	}
	public static byte[][] SplitFile(Stream head, Stream file) =>
		SplitFile(ParseHead(head), file);
	public static byte[][] SplitFile(uint[] head, Stream file)
	{
		byte[][] split = new byte[head.Length - 1][];
		for (uint chunk = 0; chunk < split.Length; chunk++)
		{
			uint size = head[chunk + 1] - head[chunk];
			if (size > 0)
			{
				split[chunk] = new byte[size];
				file.Seek(head[chunk], 0);
				file.Read(split[chunk], 0, split[chunk].Length);
			}
		}
		return split;
	}
	public AudioT(Stream audioHedStream, Stream audioTStream, XElement xml) : this(SplitFile(audioHedStream, audioTStream), xml) { }
	public class Music
	{
		public string Name { get; set; }
		public Midi Midi { get; set; }
		public Imf[] Imf { get; set; }
		public bool IsImf => Imf is not null;
		public override bool Equals(object obj) => obj is Music song && (Name?.Equals(song.Name) ?? false);
		public override int GetHashCode() => base.GetHashCode();
		public override string ToString() => Name;
	}
	public AudioT(byte[][] file, XElement xml)
	{
		SoundInfo[] logicalSounds = [..
			xml.Elements("Sound")
				.Select(soundElement =>
				{
					if (!uint.TryParse(soundElement.Attribute("Number")?.Value, out uint number))
						return null;
					string name = soundElement.Attribute("Name")?.Value;
					if (string.IsNullOrWhiteSpace(name))
						return null;
					return new SoundInfo
					{
						Number = number,
						Name = name,
						DigiSoundName = soundElement.Attribute("DigiSound")?.Value
					};
				})
				.Where(sound => sound is not null)];
		LogicalSounds = logicalSounds.ToDictionary(sound => sound.Name, StringComparer.OrdinalIgnoreCase);
		LogicalToDigiSoundName = logicalSounds
			.Where(sound => !string.IsNullOrWhiteSpace(sound.DigiSoundName))
			.ToDictionary(sound => sound.Name, sound => sound.DigiSoundName, StringComparer.OrdinalIgnoreCase);
		DigiToLogicalSoundName = new(StringComparer.OrdinalIgnoreCase);
		foreach (SoundInfo sound in logicalSounds)
			if (!string.IsNullOrWhiteSpace(sound.DigiSoundName) &&
				!DigiToLogicalSoundName.ContainsKey(sound.DigiSoundName))
				DigiToLogicalSoundName[sound.DigiSoundName] = sound.Name;
		// Load PC Speaker sounds (if StartPCSounds attribute exists)
		// From AUDIOWL1.H: #define STARTPCSOUNDS 0
		// PC Speaker sounds are stored first in AUDIOT, before AdLib sounds
		PcSounds = uint.TryParse(xml.Attribute("StartPCSounds")?.Value, out uint startPcSounds)
			? logicalSounds
				.AsParallel()
				.Select(sound =>
				{
					byte[] bytes = file[startPcSounds + sound.Number];
					if (bytes is null)
						return null;
					using MemoryStream pcSound = new(bytes);
					return new
					{
						Name = sound.Name,
						Sound = new PcSpeaker(pcSound)
					};
				})
				.Where(x => x is not null)
				.ToDictionary(x => x.Name, x => x.Sound, StringComparer.OrdinalIgnoreCase)
			: [];
		// Load AdLib sounds
		// From AUDIOWL1.H: #define STARTADLIBSOUNDS 69
		Sounds = uint.TryParse(xml.Attribute("StartAdlibSounds")?.Value, out uint startAdlibSounds)
			? logicalSounds
				.AsParallel()
				.Select(sound =>
				{
					byte[] bytes = file[startAdlibSounds + sound.Number];
					if (bytes is null)
						return null;
					using MemoryStream adlSound = new(bytes);
					return new
					{
						Name = sound.Name,
						Sound = new Adl(adlSound)
					};
				})
				.Where(x => x is not null)
				.ToDictionary(x => x.Name, x => x.Sound, StringComparer.OrdinalIgnoreCase)
			: [];
		uint startMusic = (uint)xml.Attribute("StartMusic"),
			endMusic = (uint)file.Length - startMusic;
		bool midi = xml.Elements("Midi").Any();
		Songs = Enumerable.Range(0, (int)endMusic)
			.AsParallel()
			.Where(i => file[startMusic + i] is not null)
			.Select(i =>
			{
				byte[] data = file[startMusic + i];
				using MemoryStream song = new(data);
				if (midi)
				{
					song.Position = 2;
					return new Music
					{
						Name = xml.Elements("Midi")
							.Where(e => uint.TryParse(e.Attribute("Number")?.Value, out uint n) && n == i)
							.Select(e => e.Attribute("Name")?.Value)
							.FirstOrDefault()
							?? i.ToString(),
						Midi = Midi.Parse(song),
					};
				}
				else
					return new Music
					{
						Name = xml.Elements("Imf")
							.Where(e => uint.TryParse(e.Attribute("Number")?.Value, out uint n) && n == i)
							.Select(e => e.Attribute("Name")?.Value)
							.FirstOrDefault()
							?? i.ToString(),
						Imf = Imf.ReadImf(song),
					};
			})
			.ToDictionary(song => song.Name, song => song, StringComparer.OrdinalIgnoreCase);
	}
}
