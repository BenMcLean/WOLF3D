using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace BenMcLean.Wolf3D.Assets;

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
	public sealed class Font
	{
		public ushort Height { get; private init; }
		public byte[] Width { get; private init; }
		public byte[][] Character { get; private init; }
		public Font(Stream stream)
		{
			using BinaryReader binaryReader = new(stream);
			Height = binaryReader.ReadUInt16();
			ushort[] location = new ushort[256];
			for (uint i = 0; i < location.Length; i++)
				location[i] = binaryReader.ReadUInt16();
			Width = new byte[location.Length];
			for (uint i = 0; i < Width.Length; i++)
				Width[i] = binaryReader.ReadByte();
			Character = new byte[Width.Length][];
			byte[] whitePixel = [255, 255, 255, 255];
			for (uint i = 0; i < Character.Length; i++)
			{
				Character[i] = new byte[Width[i] * Height * 4];
				stream.Seek(location[i], 0);
				for (uint j = 0; j < Character[i].Length / 4; j++)
					if (binaryReader.ReadByte() != 0)
						//for (uint k = 0; k < 4; k++)
						//    Character[i][j * 4 + k] = 255;
						Array.Copy(whitePixel, 0, Character[i], j * 4, 4);
			}
		}
		public byte[] Text(string input, ushort padding = 0)
		{
			if (string.IsNullOrWhiteSpace(input))
				return null;
			int width = CalcWidth(input);
			string[] lines = input.Split('\n');
			byte[] bytes = new byte[(width * (Height + padding) * lines.Length) << 2];
			for (int line = 0; line < lines.Length; line++)
			{
				int lineWidth = CalcWidthLine(lines[line]),
					lineStart = line * (Height + padding);
				byte[] lineBytes = Line(lines[line]);
				for (int y = 0; y < Height; y++)
					Array.Copy(
						sourceArray: lineBytes,
						sourceIndex: y * lineWidth << 2,
						destinationArray: bytes,
						destinationIndex: (lineStart + y) * width << 2,
						length: lineWidth << 2);
			}
			return bytes;
		}
		public byte[] Line(string input)
		{
			if (string.IsNullOrWhiteSpace(input))
				return null;
			int width = CalcWidth(input) << 2;
			byte[] bytes = new byte[width * Height];
			int rowStart = 0;
			foreach (char c in input)
			{
				//for (int x = 0; x < Width[c] * 4; x++)
				//    for (int y = 0; y < Height; y++)
				//        bytes[y * width + rowStart + x] = Character[c][y * Width[c] * 4 + x];
				for (int y = 0; y < Height; y++)
					Array.Copy(
						sourceArray: Character[c],
						sourceIndex: (y * Width[c]) << 2,
						destinationArray: bytes,
						destinationIndex: y * width + rowStart,
						length: Width[c] << 2
						);
				rowStart += Width[c] << 2;
			}
			return bytes;
		}
		public int CalcHeight(string input, ushort padding = 0) => (Height + padding) * (input == null ? 0 : input.Split('\n').Length);
		public int CalcWidth(string input)
		{
			if (string.IsNullOrWhiteSpace(input))
				return 0;
			int longest = 0;
			foreach (string line in input.Split('\n'))
				longest = Math.Max(longest, CalcWidthLine(line));
			return longest;
		}
		public int CalcWidthLine(string input)
		{
			if (string.IsNullOrWhiteSpace(input))
				return 0;
			int result = 0;
			foreach (char c in input)
				result += Width[c];
			return result;
		}
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
	/// Array of prefix-based fonts, where each font is a dictionary mapping characters to pic indices.
	/// Index 0 is the first prefix font in the XML file.
	/// Use the pic index to look up in Pics[] array.
	/// </summary>
	public Dictionary<char, int>[] PrefixFonts { get; private init; }
	public ushort[] PrefixSpaceWidths { get; private init; }
	public byte[] PrefixSpaceColors { get; private init; }
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
		Fonts = new Font[startPic - startFont];
		for (int i = 0; i < Fonts.Length; i++)
			using (MemoryStream font = new(file[startFont + i]))
				Fonts[i] = new Font(font);
		Pics = new byte[vgaGraph.Elements("Pic")?.Count() ?? 0][];
		for (int i = 0; i < Pics.Length; i++)
			Pics[i] = Deplanify(file[startPic + i], Sizes[i][0])
				.Indices2ByteArray(Palettes[PaletteNumber(i, xml)]);
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
		// Build PrefixFonts array from Font elements with Prefix attribute
		List<XElement> prefixFontElements = [.. vgaGraph
			.Elements("Font")
			.Where(e => !string.IsNullOrEmpty(e.Attribute("Prefix")?.Value))];
		PrefixFonts = new Dictionary<char, int>[prefixFontElements.Count];
		for (int i = 0; i < prefixFontElements.Count; i++)
		{
			XElement fontElement = prefixFontElements[i];
			string prefix = fontElement.Attribute("Prefix").Value;
			PrefixFonts[i] = [];
			foreach (XElement picElement in vgaGraph.Elements("Pic"))
			{
				string name = picElement.Attribute("Name")?.Value;
				if (string.IsNullOrEmpty(name) || !name.StartsWith(prefix))
					continue;
				string charAttr = picElement.Attribute("Character")?.Value;
				if (string.IsNullOrEmpty(charAttr))
					continue;
				if (ushort.TryParse(picElement.Attribute("Number")?.Value, out ushort picNumber))
					if (picNumber < Pics.Length)
						PrefixFonts[i][charAttr[0]] = picNumber;
			}
		}
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
	public static uint Read24Bits(Stream stream) => (uint)(stream.ReadByte() | (stream.ReadByte() << 8) | (stream.ReadByte() << 16));
	public static byte[][] SplitFile(uint[] head, Stream file, ushort[][] dictionary)
	{
		byte[][] split = new byte[head.Length - 1][];
		using (BinaryReader binaryReader = new(file))
			for (uint i = 0; i < split.Length; i++)
			{
				uint size = head[i + 1] - head[i];
				if (size > 0)
				{
					file.Seek(head[i], 0);
					uint length = binaryReader.ReadUInt32();
					binaryReader.Read(split[i] = new byte[size - 2], 0, split[i].Length);
					split[i] = CAL_HuffExpand(split[i], dictionary, length);
				}
			}
		return split;
	}
	public static byte[] Deplanify(byte[] input, ushort width) =>
		Deplanify(input, width, (ushort)(input.Length / width));
	public static byte[] Deplanify(byte[] input, ushort width, ushort height)
	{
		byte[] bytes = new byte[input.Length];
		int linewidth = width << 2;
		for (int i = 0; i < bytes.Length; i++)
		{
			int plane = i / ((width * height) << 2),
				sx = ((i % linewidth) >> 2) + plane,
				sy = (i / linewidth % height);
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
