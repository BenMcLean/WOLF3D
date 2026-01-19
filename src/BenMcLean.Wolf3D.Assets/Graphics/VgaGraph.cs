using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using BenMcLean.Wolf3D.Assets.Gameplay;

namespace BenMcLean.Wolf3D.Assets.Graphics;

/// <summary>
/// VGAGRAPH file loader - Wolf3D graphics resource format
/// Huffman-compressed graphics with 24-bit file offsets
/// Font data uses 16-bit height and 8-bit character widths
/// </summary>
public sealed class VgaGraph
{
	public static VgaGraph Load(XElement xml, string folder = "")
	{
		if (!Directory.Exists(folder))
			throw new DirectoryNotFoundException(folder);
		using FileStream vgaHead = new(Path.Combine(folder, xml.Element("VgaGraph").Attribute("VgaHead").Value), FileMode.Open);
		using FileStream vgaGraphStream = new(Path.Combine(folder, xml.Element("VgaGraph").Attribute("VgaGraph").Value), FileMode.Open);
		using FileStream vgaDict = new(Path.Combine(folder, xml.Element("VgaGraph").Attribute("VgaDict").Value), FileMode.Open);
		return new VgaGraph(vgaHead, vgaGraphStream, vgaDict, xml);
	}
	public Font[] Fonts { get; private init; }
	public byte[][] Pics { get; private init; }
	public ushort[][] Sizes { get; private init; }
	public uint[][] Palettes { get; private init; }
	/// <summary>
	/// Dictionary mapping Pic names to their indices in the Pics[] array.
	/// </summary>
	public Dictionary<string, int> PicsByName { get; private init; }
	/// <summary>
	/// Dictionary mapping chunk font names to their indices in the Fonts[] array.
	/// Chunk fonts are loaded from VGAGRAPH file chunks.
	/// </summary>
	public Dictionary<string, int> ChunkFontsByName { get; private init; }
	/// <summary>
	/// Dictionary mapping pic font names to their PicFont definitions.
	/// Pic fonts are constructed from Pics using prefix matching.
	/// </summary>
	public record PicFont(Dictionary<char, int> Glyphs, ushort SpaceWidth, byte SpaceColor);
	public Dictionary<string, PicFont> PicFonts { get; private init; }
	/// <summary>
	/// Status bar definition parsed from XML.
	/// </summary>
	public StatusBarDefinition StatusBar { get; private init; }
	public VgaGraph(Stream vgaHead, Stream vgaGraph, Stream dictionary, XElement xml) : this(SplitFile(ParseHead(vgaHead), vgaGraph, Load16BitPairs(dictionary)), xml)
	{ }
	public VgaGraph(byte[][] file, XElement xml)
	{
		Palettes = [.. VSwap.LoadPalettes(xml)];
		XElement vgaGraph = xml.Element("VgaGraph");
		using (MemoryStream sizes = new(file[(uint?)vgaGraph.Element("Sizes").Attribute("Chunk") ?? 0]))
			Sizes = Load16BitPairs(sizes);
		uint startFont = (uint)vgaGraph.Element("Sizes").Attribute("StartFont"),
			startPic = (uint)vgaGraph.Element("Sizes").Attribute("StartPic");
		Fonts = [.. Enumerable.Range(0, (int)(startPic - startFont))
			.Parallelize(i =>
			{
				using MemoryStream fontStream = new(file[startFont + i]);
				return new Font(fontStream);
			})];
		Pics = [.. Enumerable.Range(0, vgaGraph.Elements("Pic")?.Count() ?? 0)
			.Parallelize(i =>
				Deplanify(file[startPic + i], Sizes[i][0])
					.Indices2ByteArray(Palettes[PaletteNumber(i, xml)]))];
		// Build PicsByName dictionary from XML
		PicsByName = [];
		foreach (XElement picElement in vgaGraph.Elements("Pic"))
		{
			string name = picElement.Attribute("Name")?.Value;
			if (string.IsNullOrEmpty(name))
				continue;
			if (!ushort.TryParse(picElement.Attribute("Number")?.Value, out ushort picNumber))
				continue;
			if (picNumber < Pics.Length)
				PicsByName[name] = picNumber;
		}
		// Build font dictionaries
		ChunkFontsByName = [];
		PicFonts = [];
		foreach (XElement fontElement in vgaGraph.Elements("Font"))
		{
			// Name is required - crash if missing
			string name = fontElement.Attribute("Name")?.Value;
			if (string.IsNullOrEmpty(name))
				throw new InvalidDataException("Font element missing required Name attribute");
			// Check for duplicate names across both dictionaries
			if (ChunkFontsByName.ContainsKey(name) || PicFonts.ContainsKey(name))
				throw new InvalidDataException($"Duplicate font name '{name}' found");
			bool isPicFont = !string.IsNullOrEmpty(fontElement.Attribute("Prefix")?.Value);
			if (isPicFont)
			{
				// Pic font: build from Pics using prefix matching
				string prefix = fontElement.Attribute("Prefix").Value;
				Dictionary<char, int> characters = [];
				foreach (XElement picElement in vgaGraph.Elements("Pic"))
				{
					string picName = picElement.Attribute("Name")?.Value;
					if (string.IsNullOrEmpty(picName) || !picName.StartsWith(prefix))
						continue;
					string charAttr = picElement.Attribute("Character")?.Value;
					if (string.IsNullOrEmpty(charAttr))
						continue;
					if (ushort.TryParse(picElement.Attribute("Number")?.Value, out ushort picNumber))
						if (picNumber < Pics.Length)
							characters[charAttr[0]] = picNumber;
				}
				// Parse space character metadata
				ushort spaceWidth = ushort.TryParse(fontElement.Attribute("SpaceWidth")?.Value, out ushort sw) ? sw : (ushort)0;
				byte spaceColor = byte.TryParse(fontElement.Attribute("SpaceColor")?.Value, out byte sc) ? sc : (byte)0;
				PicFonts[name] = new PicFont(characters, spaceWidth, spaceColor);
			}
			else
			{
				// Chunk font: parse Number to get index in Fonts array
				if (!ushort.TryParse(fontElement.Attribute("Number")?.Value, out ushort fontNumber))
					throw new InvalidDataException($"Chunk font '{name}' missing or invalid Number attribute");
				// Number should be offset from startFont
				int index = fontNumber;
				if (index >= 0 && index < Fonts.Length)
					ChunkFontsByName[name] = index;
			}
		}
		// Parse StatusBar definition
		XElement statusBarElement = vgaGraph.Element("StatusBar");
		if (statusBarElement != null)
			StatusBar = StatusBarDefinition.FromXElement(statusBarElement);
	}
	public static int PaletteNumber(int picNumber, XElement xml) =>
		xml?.Element("VgaGraph")?.Elements("Pic")?.Where(
			e => ushort.TryParse(e.Attribute("Number")?.Value, out ushort number) && number == picNumber
			)?.Select(e => ushort.TryParse(e.Attribute("Palette")?.Value, out ushort palette) ? palette : (ushort)0)
		?.FirstOrDefault() ?? 0;
	public static uint[] ParseHead(Stream stream)
	{
		uint[] head = new uint[stream.Length / 3];
		for (uint i = 0; i < head.Length; i++)
			head[i] = Read24Bits(stream);
		return head;
	}
	public static uint Read24Bits(Stream stream) => (uint)(stream.ReadByte() | stream.ReadByte() << 8 | stream.ReadByte() << 16);
	public static byte[][] SplitFile(uint[] head, Stream file, ushort[][] dictionary)
	{
		byte[][] split = new byte[head.Length - 1][];
		uint[] lengths = new uint[split.Length];
		using (BinaryReader binaryReader = new(file))
			for (uint i = 0; i < split.Length; i++)
			{
				uint size = head[i + 1] - head[i];
				if (size > 0)
				{
					file.Seek(head[i], 0);
					lengths[i] = binaryReader.ReadUInt32();
					binaryReader.Read(
						buffer: split[i] = new byte[size - 2],
						index: 0,
						count: split[i].Length);
				}
			}
		return [.. split
			.Select((slice, index) => (slice, index))
			.AsParallel()
			.Select(work => (result: CAL_HuffExpand(work.slice, dictionary, lengths[work.index]), work.index))
			.OrderBy(resultTuple => resultTuple.index)
			.AsEnumerable()
			.Select(resultTuple => resultTuple.result)];
	}
	public static byte[] Deplanify(byte[] input, ushort width) =>
		Deplanify(input, width, (ushort)(input.Length / width));
	public static byte[] Deplanify(byte[] input, ushort width, ushort height)
	{
		byte[] bytes = new byte[input.Length];
		int linewidth = width >> 2;
		for (int i = 0; i < bytes.Length; i++)
		{
			int plane = i / (width * height >> 2),
				sx = (i % linewidth << 2) + plane,
				sy = i / linewidth % height;
			bytes[sy * width + sx] = input[i];
		}
		return bytes;
	}
	/// <summary>
	/// Implementing Huffman decompression. http://www.shikadi.net/moddingwiki/Huffman_Compression#Huffman_implementation_in_ID_Software_games
	/// Translated from https://github.com/mozzwald/wolf4sdl/blob/master/id_ca.cpp#L214-L260
	/// </summary>
	/// <param name="length">When to stop. Default 0 indicates to keep going until source is exhausted.</param>
	/// <param name="dictionary">The Huffman dictionary is a ushort[255][2]</param>
	public static byte[] CAL_HuffExpand(byte[] source, ushort[][] dictionary, uint length = 0)
	{
		List<byte> dest = [];
		ushort[] huffNode = dictionary[254];
		uint read = 0;
		ushort nodeVal;
		byte val = source[read++], mask = 1;
		while (read < source.Length && (length <= 0 || dest.Count < length))
		{
			nodeVal = huffNode[(val & mask) == 0 ? 0 : 1];
			if (mask == 0x80)
			{
				val = source[read++];
				mask = 1;
			}
			else
				mask <<= 1;
			if (nodeVal < 256)
			{ // 0-255 is a character, > is a pointer to a node
				dest.Add((byte)nodeVal);
				huffNode = dictionary[254];
			}
			else
				huffNode = dictionary[nodeVal - 256];
		}
		return [.. dest];
	}
	public static ushort[][] Load16BitPairs(Stream stream)
	{
		ushort[][] dest = new ushort[stream.Length >> 2][];
		using (BinaryReader binaryReader = new(stream))
			for (uint i = 0; i < dest.Length; i++)
				dest[i] = [binaryReader.ReadUInt16(), binaryReader.ReadUInt16()];
		return dest;
	}
}
