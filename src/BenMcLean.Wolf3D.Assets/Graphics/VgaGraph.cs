using System;
using System.Buffers.Binary;
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
		XElement el = xml.Element("VgaGraph");
		string head = el?.Attribute("VgaHead")?.Value;
		string graph = el?.Attribute("VgaGraph")?.Value;
		string dict = el?.Attribute("VgaDict")?.Value;
		if (head is null || graph is null || dict is null)
			return null;
		if (!Directory.Exists(folder))
			throw new DirectoryNotFoundException(folder);
		using FileStream vgaHead = new(Path.Combine(folder, head), FileMode.Open);
		using FileStream vgaGraphStream = new(Path.Combine(folder, graph), FileMode.Open);
		using FileStream vgaDict = new(Path.Combine(folder, dict), FileMode.Open);
		return new VgaGraph(vgaHead, vgaGraphStream, vgaDict, xml);
	}
	public Font[] Fonts { get; private init; }
	public byte[][] Pics { get; private init; }
	/// <summary>
	/// 8x8 tile graphics for the automap display, loaded from the single packed
	/// STARTTILE8 chunk. Each entry is an RGBA8888 byte array (256 bytes: 8x8x4).
	/// Tile index matches the tile argument passed to VWB_DrawTile8() in WL_MAP.C.
	/// Source: GFXV_*.H defines STARTTILE8 (chunk) and NUMTILE8 (count).
	/// </summary>
	public byte[][] Tiles { get; private init; }
	public ushort[] Sizes { get; private init; }
	public uint[] Palette { get; private init; }
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
	public VgaGraph(Stream vgaHead, Stream vgaGraph, Stream dictionary, XElement xml)
		: this(SplitFile(vgaHead, vgaGraph, dictionary,
			uint.TryParse(xml?.Element("VgaGraph")?.Attribute("TileChunk")?.Value, out uint tc) ? tc : null), xml)
	{ }
	public VgaGraph(byte[][] file, XElement xml)
	{
		Palette = VSwap.LoadPalette(xml);
		XElement vgaGraph = xml.Element("VgaGraph");
		using (MemoryStream sizes = new(file[(uint?)vgaGraph.Attribute("Sizes") ?? 0]))
			Sizes = Load16BitPairs(sizes);
		uint startFont = (uint)vgaGraph.Element("Fonts").Attribute("Start"),
			startPic = (uint)vgaGraph.Element("Pics").Attribute("Start");
		Fonts = [.. Enumerable.Range(0, (int)(startPic - startFont))
			.Parallelize(i =>
			{
				using MemoryStream fontStream = new(file[startFont + i]);
				return new Font(fontStream);
			})];
		Pics = [.. (vgaGraph.Element("Pics")?.Elements("Pic") ?? [])
			.Parallelize(e => {
				if (!int.TryParse(e.Attribute("Number")?.Value, out int i))
					throw new InvalidDataException("<Pic> element missing Number attribute!");
				return Deplanify(file[startPic + i], Sizes[i << 1])
					.Indices2ByteArray(int.TryParse(e.Attribute("PaletteChunk")?.Value, out int c)
						? ParseVgaPalette(file[c])
						: Palette);
			})];
		// Extract 8x8 automap tiles from single packed chunk (STARTTILE8 in GFXV_*.H)
		// All tiles are packed sequentially: tile k starts at byte offset k*64
		// Each tile is 64 bytes of planar Mode X VGA data — same deplanify path as Pics
		// Tile count is derived from decompressed size rather than XML to handle version skew.
		if (uint.TryParse(vgaGraph.Attribute("TileChunk")?.Value, out uint startTile8))
		{
			byte[] tileChunk = file[startTile8];
			int actualTileCount = (tileChunk?.Length ?? 0) / 64;
			Tiles = actualTileCount == 0
				? []
				: [.. Enumerable.Range(0, actualTileCount)
					.Parallelize(i => Deplanify(tileChunk[(i * 64)..((i + 1) * 64)], 8)
						.Indices2ByteArray(Palette))];
		}
		else
			Tiles = [];
		// Build PicsByName dictionary from XML
		PicsByName = [];
		foreach (XElement picElement in vgaGraph.Element("Pics")?.Elements("Pic") ?? [])
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
		foreach (XElement fontElement in vgaGraph.Element("Fonts")?.Elements("Font") ?? [])
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
				foreach (XElement picElement in vgaGraph.Element("Pics")?.Elements("Pic") ?? [])
				{
					string picName = picElement.Attribute("Name")?.Value;
					if (string.IsNullOrEmpty(picName) || !picName.StartsWith(prefix))
						continue;
					string charAttr = picElement.Attribute("Glyph")?.Value;
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
		if (vgaGraph.Element("StatusBar") is XElement statusBarElement)
			StatusBar = StatusBarDefinition.FromXElement(statusBarElement);
	}
	public static uint[] ParseVgaPalette(ReadOnlySpan<byte> chunk)
	{
		uint[] palette = new uint[256];
		for (int index = 0, offset = 0; index < 255; index++, offset += 3)
			palette[index] = BinaryPrimitives.ReadUInt32BigEndian(chunk[offset..]) << 2 & 0xFCFCFC00u | 0xFFu;
		palette[255] = (uint)chunk[765] << 26 |
			(uint)chunk[766] << 18 |
			(uint)chunk[767] << 10; // Last color is transparent
		return palette;
	}
	public static uint[] ParseHead(Stream stream)
	{
		uint[] head = new uint[stream.Length / 3];
		for (uint i = 0; i < head.Length; i++)
			head[i] = Read24Bits(stream);
		return head;
	}
	public static uint Read24Bits(Stream stream) => (uint)(stream.ReadByte() | stream.ReadByte() << 8 | stream.ReadByte() << 16);
	/// <param name="tileChunkIndex">
	/// Optional chunk index for the STARTTILE8 chunk, which has NO 4-byte size prefix.
	/// ID_CA.C: pics/fonts store an explicit longword; tiles use an implicit expanded size.
	/// SplitFile skips that slot (leaves it null); the stream overload reads and decompresses
	/// it correctly afterwards so it is only ever processed once, and correctly.
	/// </param>
	public static byte[][] SplitFile(Stream vgaHead, Stream vgaGraph, Stream dictionary, uint? tileChunkIndex = null)
	{
		uint[] head = ParseHead(vgaHead);
		ushort[] dict = Load16BitPairs(dictionary);
		byte[][] file = SplitFile(head, vgaGraph, dict, tileChunkIndex);
		if (tileChunkIndex is uint tileIdx)
		{
			uint size = head[tileIdx + 1] - head[tileIdx];
			byte[] compressed = new byte[size];
			vgaGraph.Seek(head[tileIdx], SeekOrigin.Begin);
			vgaGraph.ReadExactly(compressed, 0, (int)size);
			file[tileIdx] = CAL_HuffExpand(compressed, dict);
		}
		return file;
	}
	public static byte[][] SplitFile(uint[] head, Stream file, ushort[] dictionary, uint? tileChunkIndex = null)
	{
		byte[][] split = new byte[head.Length - 1][];
		uint[] lengths = new uint[split.Length];
		using (BinaryReader binaryReader = new(file, System.Text.Encoding.Default, leaveOpen: true))
			for (uint i = 0; i < split.Length; i++)
			{
				uint size = head[i + 1] - head[i];
				if (size > 0 && i != tileChunkIndex)
				{
					file.Seek(head[i], 0);
					lengths[i] = binaryReader.ReadUInt32();
					binaryReader.Read(
						buffer: split[i] = new byte[size - 2],
						index: 0,
						count: split[i].Length);
				}
			}
		return [.. split.Parallelize((slice, index) => slice is null ? null : CAL_HuffExpand(slice, dictionary, lengths[index]))];
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
	/// <param name="dictionary">The Huffman dictionary as a flat ushort[] — pairs of (bit0,bit1) values,
	/// node n at indices n*2 and n*2+1. 255 nodes total; root is node 254.</param>
	public static byte[] CAL_HuffExpand(byte[] source, ushort[] dictionary, uint length = 0)
	{
		List<byte> dest = [];
		int nodeIdx = 508; // root node
		uint read = 0;
		ushort nodeVal;
		byte val = source[read++], mask = 1;
		bool running = true;
		// Original ID_CA.C had no source-exhaustion guard — it ran purely until longcount
		// reached zero. We must process ALL 8 bits of the last loaded byte before stopping,
		// otherwise we exit as soon as the last byte is loaded (mask resets to 1) without
		// consuming any of its bits, producing up to 8 decoded symbols too few.
		while (running && (length <= 0 || dest.Count < length))
		{
			nodeVal = dictionary[nodeIdx + ((val & mask) == 0 ? 0 : 1)];
			if (mask == 0x80)
				if (read < source.Length)
				{
					val = source[read++];
					mask = 1;
				}
				else
					running = false; // finish processing this nodeVal, then stop
			else
				mask <<= 1;
			if (nodeVal < 256)
			{ // 0-255 is a character, > is a pointer to a node
				dest.Add((byte)nodeVal);
				nodeIdx = 508; // reset to root
			}
			else
				nodeIdx = (nodeVal - 256) << 1;
		}
		return [.. dest];
	}
	public static ushort[] Load16BitPairs(Stream stream)
	{
		byte[] bytes = new byte[stream.Length];
		stream.ReadExactly(bytes);
		ushort[] dest = new ushort[bytes.Length >> 1];
		for (int i = 0; i < dest.Length; i++)
			dest[i] = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(i << 1));
		return dest;
	}
}
