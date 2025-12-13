using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace BenMcLean.Wolf3D.Assets;

/// <summary>
/// AUDIOT file loader - Wolf3D audio resource format
/// File uses 32-bit offsets to locate AdLib sounds and IMF music
/// </summary>
public sealed class AudioT
{
	public static AudioT Load(string folder, XElement xml)
	{
		using FileStream audioHead = new(System.IO.Path.Combine(folder, xml.Element("Audio").Attribute("AudioHead").Value), FileMode.Open);
		using FileStream audioTStream = new(System.IO.Path.Combine(folder, xml.Element("Audio").Attribute("AudioT").Value), FileMode.Open);
		return new AudioT(audioHead, audioTStream, xml.Element("Audio"));
	}
	public Adl[] Sounds { get; private init; }
	public Dictionary<string, Song> Songs { get; private init; }
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
	public class Song
	{
		public string Name { get; set; }
		public byte[] Bytes { get; set; }
		public Imf[] Imf { get; set; }
		public bool IsImf => Imf != null;
		public override bool Equals(object obj) => obj is Song song && (Name?.Equals(song.Name) ?? false);
		public override int GetHashCode() => base.GetHashCode();
		public override string ToString() => Name;
	}
	public AudioT(byte[][] file, XElement xml)
	{
		uint startAdlibSounds = (uint)xml.Attribute("StartAdlibSounds");
		Sounds = new Adl[xml.Elements("Sound").Count()];
		for (uint i = 0; i < Sounds.Length; i++)
			if (file[startAdlibSounds + i] != null)
				using (MemoryStream sound = new(file[startAdlibSounds + i]))
					Sounds[i] = new Adl(sound);
		uint startMusic = (uint)xml.Attribute("StartMusic"),
			endMusic = (uint)file.Length - startMusic;
		Songs = [];
		bool midi = xml?.Elements("MIDI")?.Any() ?? false;
		for (uint i = 0; i < endMusic; i++)
			if (file[startMusic + i] is not null)
				using (MemoryStream song = new(file[startMusic + i]))
				{
					if (midi)
					{
						Song newSong = new()
						{
							Name = (xml.Elements("MIDI").Where(
									e => uint.TryParse(e.Attribute("Number")?.Value, out uint number) && number == i
								)?.Select(e => e.Attribute("Name")?.Value)
								?.FirstOrDefault() is string name
								&& !string.IsNullOrWhiteSpace(name)) ?
									name
									: i.ToString(),
							Bytes = new byte[file[startMusic + i].Length - 2],
						};
						// Super 3D Noah's Ark adds two bytes of junk data to the start of all its MIDI songs and I don't know why.
						Array.Copy(
							sourceArray: file[startMusic + i],
							sourceIndex: 2,
							destinationArray: newSong.Bytes,
							destinationIndex: 0,
							length: newSong.Bytes.Length
							);
						Songs.Add(newSong.Name, newSong);
					}
					else
					{
						Imf[] imf = Imf.ReadImf(song);
						Song newSong = new()
						{
							Name = (xml.Elements("Imf")?.Where(
									e => uint.TryParse(e.Attribute("Number")?.Value, out uint number) && number == i
								)?.Select(e => e.Attribute("Name")?.Value)
								?.FirstOrDefault() is string name
								&& !string.IsNullOrWhiteSpace(name)) ?
									name
									: i.ToString(),
							//Bytes = file[startMusic + i],
							Imf = imf,
						};
						Songs.Add(newSong.Name, newSong);
					}
				}
	}
}
