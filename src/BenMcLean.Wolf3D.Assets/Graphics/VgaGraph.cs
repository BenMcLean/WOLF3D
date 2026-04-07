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
		string head  = el?.Attribute("VgaHead")?.Value;
		string graph = el?.Attribute("VgaGraph")?.Value;
		string dict  = el?.Attribute("VgaDict")?.Value;
		if (head == null || graph == null || dict == null)
			return null;
		if (!Directory.Exists(folder))
			throw new DirectoryNotFoundException(folder);
		using FileStream vgaHead = new(Path.Combine(folder, head),  FileMode.Open);
		using FileStream vgaGraphStream = new(Path.Combine(folder, graph), FileMode.Open);
		using FileStream vgaDict = new(Path.Combine(folder, dict),  FileMode.Open);
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
	public ushort[][] Sizes { get; private init; }
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
	public VgaGraph(Stream vgaHead, Stream vgaGraph, Stream dictionary, XElement xml) : this(SplitWithTileInfo(vgaHead, vgaGraph, dictionary, xml), xml)
	{ }
	/// <summary>
	/// Parses the optional Tiles element from the XML and calls SplitFile with tile chunk
	/// info so that the tile chunk (which has NO 4-byte size prefix, unlike Pics/Fonts) is
	/// read correctly. ID_CA.C: tile 8s have an implicit expanded size (BLOCK*NUMTILE8),
	/// not an explicit longword — SplitFile must not consume 4 bytes as a length prefix for them.
	/// </summary>
	private static byte[][] SplitWithTileInfo(Stream vgaHead, Stream vgaGraph, Stream dictionary, XElement xml)
	{
		uint[] head = ParseHead(vgaHead);
		ushort[][] dict = Load16BitPairs(dictionary);
		// Build tile-chunk map: chunk index → known expanded size (Count * BLOCK where BLOCK=64)
		Dictionary<uint, uint> tileChunks = [];
		if (xml?.Element("VgaGraph")?.Element("Tiles") is XElement tilesEl
			&& uint.TryParse(tilesEl.Attribute("Start")?.Value, out uint tileStart)
			&& uint.TryParse(tilesEl.Attribute("Count")?.Value, out uint tileCount))
			tileChunks[tileStart] = tileCount * 64u; // BLOCK=64 (ID_CA.C:#define BLOCK 64)
		return SplitFile(head, vgaGraph, dict, tileChunks);
	}
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
				return Deplanify(file[startPic + i], Sizes[i][0])
					.Indices2ByteArray(int.TryParse(e.Attribute("PaletteChunk")?.Value, out int c)
						? ParseVgaPalette(file[c])
						: Palette);
			})];
		// Extract 8x8 automap tiles from single packed chunk (STARTTILE8/NUMTILE8)
		// All tiles are packed sequentially: tile k starts at byte offset k*64
		// Each tile is 64 bytes of planar Mode X VGA data — same deplanify path as Pics
		if (vgaGraph.Element("Tiles") is XElement tilesEl)
		{
			uint startTile8 = (uint)tilesEl.Attribute("Start");
			int numTile8 = (int)(uint)tilesEl.Attribute("Count");
			byte[] tileChunk = file[startTile8];
			// Use actual decompressed length to derive tile count — the XML Count may differ
			// from what a specific binary produces (e.g. different N3D versions), and the
			// Huffman decoder terminates at source exhaustion so partial tiles are impossible.
			int actualTileCount = (tileChunk?.Length ?? 0) / 64;
			if (actualTileCount == 0)
			{
				Console.Error.WriteLine($"Warning: tile chunk {startTile8} decompressed to {tileChunk?.Length ?? 0} bytes (expected ~{numTile8 * 64}). Tiles will be empty.");
				Tiles = [];
			}
			else
			{
				if (actualTileCount != numTile8)
					Console.Error.WriteLine($"Warning: tile chunk {startTile8} yielded {actualTileCount} tiles, XML specifies {numTile8}.");
				Tiles = [.. Enumerable.Range(0, actualTileCount)
					.Parallelize(i => Deplanify(tileChunk[(i * 64)..((i + 1) * 64)], 8)
						.Indices2ByteArray(Palette))];
			}
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
	/// <param name="tileChunks">
	/// Optional map of chunk index → known expanded size for chunks that have NO 4-byte
	/// size prefix (tile 8 chunks). ID_CA.C: pics/fonts store an explicit longword;
	/// tiles use an implicit size = BLOCK*NUMTILE8 that is NOT written to the file.
	/// </param>
	public static byte[][] SplitFile(uint[] head, Stream file, ushort[][] dictionary, Dictionary<uint, uint> tileChunks = null)
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
					if (tileChunks?.TryGetValue(i, out uint expandedSize) == true)
					{
						// Tile chunk: no size longword prefix — read all bytes as compressed data
						// ID_CA.C: "tile 8s are all in one chunk!" with implicit expanded size
						binaryReader.Read(
							buffer: split[i] = new byte[size],
							index: 0,
							count: (int)size);
						lengths[i] = expandedSize; // BLOCK*NUMTILE8 = 64*Count
					}
					else
					{
						lengths[i] = binaryReader.ReadUInt32();
						binaryReader.Read(
							buffer: split[i] = new byte[size - 2],
							index: 0,
							count: split[i].Length);
					}
				}
			}
		return [.. split.Parallelize((slice, index) => CAL_HuffExpand(slice, dictionary, lengths[index]))];
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
		bool running = true;
		// Original ID_CA.C had no source-exhaustion guard — it ran purely until longcount
		// reached zero. We must process ALL 8 bits of the last loaded byte before stopping,
		// otherwise we exit as soon as the last byte is loaded (mask resets to 1) without
		// consuming any of its bits, producing up to 8 decoded symbols too few.
		while (running && (length <= 0 || dest.Count < length))
		{
			nodeVal = huffNode[(val & mask) == 0 ? 0 : 1];
			if (mask == 0x80)
			{
				if (read < source.Length)
				{
					val = source[read++];
					mask = 1;
				}
				else
					running = false; // finish processing this nodeVal, then stop
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
