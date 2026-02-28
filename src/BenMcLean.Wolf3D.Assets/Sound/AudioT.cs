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
	public static AudioT Load(XElement xml, string folder = "")
	{
		using FileStream audioHead = new(Path.Combine(folder, xml.Element("Audio").Attribute("AudioHead").Value), FileMode.Open);
		using FileStream audioTStream = new(Path.Combine(folder, xml.Element("Audio").Attribute("AudioT").Value), FileMode.Open);
		return new AudioT(audioHead, audioTStream, xml.Element("Audio"));
	}
	public Dictionary<string, PcSpeaker> PcSounds { get; private init; }
	public Dictionary<string, Adl> Sounds { get; private init; }
	public Dictionary<string, Music> Songs { get; private init; }
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
		// Load PC Speaker sounds (if StartPCSounds attribute exists)
		// From AUDIOWL1.H: #define STARTPCSOUNDS 0
		// PC Speaker sounds are stored first in AUDIOT, before AdLib sounds
		PcSounds = uint.TryParse(xml.Attribute("StartPCSounds")?.Value, out uint startPcSounds)
			? xml.Elements("Sound")
				.AsParallel()
				.Select(soundElement =>
				{
					uint number = (uint)soundElement.Attribute("Number");
					string name = soundElement.Attribute("Name")?.Value;
					if (string.IsNullOrWhiteSpace(name))
						return null;
					byte[] bytes = file[startPcSounds + number];
					if (bytes is null)
						return null;
					using MemoryStream sound = new(bytes);
					return new
					{
						Name = name,
						Sound = new PcSpeaker(sound)
					};
				})
				.Where(x => x is not null)
				.ToDictionary(x => x.Name, x => x.Sound)
			: [];
		// Load AdLib sounds
		// From AUDIOWL1.H: #define STARTADLIBSOUNDS 69
		Sounds = uint.TryParse(xml.Attribute("StartAdlibSounds")?.Value, out uint startAdlibSounds)
			? xml.Elements("Sound")
				.AsParallel()
				.Select(soundElement =>
				{
					uint number = (uint)soundElement.Attribute("Number");
					string name = soundElement.Attribute("Name")?.Value;
					if (string.IsNullOrWhiteSpace(name))
						return null;
					byte[] bytes = file[startAdlibSounds + number];
					if (bytes is null)
						return null;
					using MemoryStream sound = new(bytes);
					return new
					{
						Name = name,
						Sound = new Adl(sound)
					};
				})
				.Where(x => x is not null)
				.ToDictionary(x => x.Name, x => x.Sound)
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
			.ToDictionary(song => song.Name, song => song);
	}
}
