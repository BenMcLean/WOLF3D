using System;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace BenMcLean.Wolf3D.Assets.Graphics;

/// <summary>
/// VSWAP file loader - Wolf3D graphics/sound resource format
/// File format: 16-bit NumPages, 16-bit SpritePage, 16-bit SoundPage,
/// followed by 32-bit page offsets and 16-bit page lengths
/// </summary>
public sealed class VSwap
{
	public static VSwap Load(XElement xml, string folder = "")
	{
		if (!Directory.Exists(folder))
			throw new DirectoryNotFoundException(folder);
		using FileStream vSwap = new(Path.Combine(folder, xml.Element("VSwap").Attribute("Name").Value), FileMode.Open);
		return new VSwap(
			xml: xml,
			palettes: [.. LoadPalettes(xml)],
			stream: vSwap,
			tileSqrt: ushort.TryParse(xml?.Element("VSwap")?.Attribute("Sqrt")?.Value, out ushort tileSqrt) ? tileSqrt : (ushort)64);
	}
	#region Data
	public uint[][] Palettes { get; private init; }
	public byte[][] Pages { get; private init; }
	public byte[][] DigiSounds { get; private init; }
	public Dictionary<string, byte[]> DigiSoundsByName { get; private init; }
	public Dictionary<string, ushort> SpritesByName { get; private init; }
	public BitArray[] Masks { get; private init; }
	public ushort SpritePage { get; private init; }
	public ushort NumPages { get; private init; }
	public int SoundPage => Pages.Length;
	public ushort TileSqrt { get; private init; } = 64;
	#endregion
	public byte[] Sprite(ushort number) => Pages[SpritePage + number];
	public static uint GetOffset(ushort x, ushort y, ushort tileSqrt = 64) => (uint)((tileSqrt * y + x) * 4);
	public uint GetOffset(ushort x, ushort y) => GetOffset(x, y, TileSqrt);
	public byte GetR(ushort page, ushort x, ushort y) => Pages[page][GetOffset(x, y)];
	public byte GetG(ushort page, ushort x, ushort y) => Pages[page][GetOffset(x, y) + 1];
	public byte GetB(ushort page, ushort x, ushort y) => Pages[page][GetOffset(x, y) + 2];
	public byte GetA(ushort page, ushort x, ushort y) => Pages[page][GetOffset(x, y) + 3];
	public bool IsTransparent(ushort page, ushort x, ushort y) =>
		page >= Pages.Length
		|| Pages[page] is null
		|| page >= SpritePage // We know walls aren't transparent
		&& GetOffset(x, y) + 3 is uint offset
		&& offset < Pages[page].Length
		&& Pages[page][offset] > 128;
	public VSwap(XElement xml, uint[][] palettes, Stream stream, ushort tileSqrt = 64)
	{
		Palettes = palettes;
		TileSqrt = tileSqrt;
		if (Palettes is null || Palettes.Length < 1)
			throw new InvalidDataException("Must load a palette before loading a VSWAP!");
		using BinaryReader binaryReader = new(stream);
		#region parse header info
		NumPages = binaryReader.ReadUInt16();
		SpritePage = binaryReader.ReadUInt16();
		Pages = new byte[binaryReader.ReadUInt16()][]; // SoundPage
		Masks = new BitArray[SoundPage - SpritePage];
		uint[] pageOffsets = new uint[NumPages];
		uint dataStart = 0;
		for (ushort i = 0; i < pageOffsets.Length; i++)
		{
			pageOffsets[i] = binaryReader.ReadUInt32();
			if (i == 0)
				dataStart = pageOffsets[0];
			if (pageOffsets[i] != 0 && pageOffsets[i] < dataStart || pageOffsets[i] > stream.Length)
				throw new InvalidDataException("VSWAP contains invalid page offsets.");
		}
		ushort[] pageLengths = new ushort[NumPages];
		for (int page = 0; page < pageLengths.Length; page++)
			pageLengths[page] = binaryReader.ReadUInt16();
		#endregion parse header info
		byte[][] rawPages = new byte[SoundPage][];
		for (int page = 0; page < SoundPage; page++)
			if (pageOffsets[page] > 0)
			{
				stream.Seek(pageOffsets[page], SeekOrigin.Begin);
				rawPages[page] = binaryReader.ReadBytes(pageLengths[page]);
			}
		Enumerable.Range(0, SoundPage)
			.AsParallel()
			.ForAll(page =>
			{
				if (rawPages[page] is null) return;
				uint[] palette = palettes[PaletteNumber(page, xml)];
				if (page < SpritePage)
					Pages[page] = ProcessWall(rawPages[page], palette, TileSqrt);
				else
				{
					(byte[] rgba, BitArray mask) = ProcessSprite(rawPages[page], palette, TileSqrt);
					Pages[page] = rgba;
					Masks[page - SpritePage] = mask;
				}
			});
		// Parse sprite name->page mappings
		// Note: XML Page attributes are absolute VSWAP page numbers
		SpritesByName = [];
		foreach (XElement spriteElement in xml.Element("VSwap")?.Element("Sprites")?.Elements("Sprite") ?? [])
		{
			string name = spriteElement.Attribute("Name")?.Value;
			if (!string.IsNullOrWhiteSpace(name)
				&& ushort.TryParse(spriteElement.Attribute("Page")?.Value, out ushort spritePage))
				SpritesByName.Add(name, spritePage);
		}
		#region read in digisounds
		uint soundDataStart = pageOffsets[Pages.Length];
		int soundDataLength = (int)(stream.Length - soundDataStart);
		byte[] soundData = new byte[soundDataLength];
		stream.Seek(soundDataStart, SeekOrigin.Begin);
		stream.Read(soundData, 0, soundDataLength);
		uint tableOffsetInBlob = pageOffsets[NumPages - 1] - soundDataStart;
		List<(int Index, ushort Page, ushort Length)> soundJobs = [];
		{
			// Ensure we don't cast outside the bounds of the actual data
			int tableByteCount = soundDataLength - (int)tableOffsetInBlob;
			if (tableByteCount > 0)
			{
				ReadOnlySpan<ushort> soundTable = MemoryMarshal.Cast<byte, ushort>(soundData.AsSpan((int)tableOffsetInBlob, tableByteCount));
				for (int i = 0; i < soundTable.Length / 2; i++)
				{
					ushort len = soundTable[i * 2 + 1];
					if (len > 0)
						soundJobs.Add((i, Page: soundTable[i * 2], len));
				}
			}
		}
		DigiSounds = new byte[soundJobs.Count > 0 ? soundJobs.Max(s => s.Index) + 1 : 0][];
		soundJobs.AsParallel().ForAll(job =>
		{
			// Calculate start relative to the start of the sound blob
			long absoluteStart = (long)pageOffsets[Pages.Length + job.Page] - soundDataStart;
			// SAFETY CHECK: Ensure the chunk is actually inside our soundData buffer
			if (absoluteStart >= 0 && absoluteStart + job.Length <= soundData.Length)
			{
				byte[] buffer = new byte[job.Length];
				ReadOnlySpan<byte> source = soundData.AsSpan((int)absoluteStart, job.Length);
				Span<byte> dest = buffer;
				for (int j = 0; j < source.Length; j++)
					dest[j] = (byte)(source[j] - 128);
				DigiSounds[job.Index] = buffer;
			}
		});
		DigiSoundsByName = [];
		foreach (XElement digiSoundElement in xml.Element("VSwap")?.Element("DigiSounds")?.Elements("DigiSound") ?? [])
			if (ushort.TryParse(digiSoundElement.Attribute("Number")?.Value, out ushort number)
				&& number < DigiSounds.Length
				&& DigiSounds[number] is byte[] bytes)
			{
				string name = digiSoundElement.Attribute("Name")?.Value;
				if (!string.IsNullOrWhiteSpace(name))
					DigiSoundsByName[name] = bytes;
			}
		#endregion read in digisounds
	}
	/// <summary>
	/// Converts from Wolfenstein 3-D's column-major palette indexed format to modern row-major RGBA8888.
	/// </summary>
	private static byte[] ProcessWall(byte[] rawData, uint[] palette, ushort tileSqrt = 64)
	{
		byte[] rgbaData = new byte[tileSqrt * tileSqrt << 2];
		Span<uint> destination = MemoryMarshal.Cast<byte, uint>(rgbaData.AsSpan());
		int sourceIndex = 0;
		for (int col = 0; col < tileSqrt; col++)
			for (int row = 0, destinationIndex = col; row < tileSqrt; row++, destinationIndex += tileSqrt)
				destination[destinationIndex] = BinaryPrimitives.ReverseEndianness(palette[rawData[sourceIndex++]]);
		return rgbaData;
	}
	private static (byte[] rgba, BitArray mask) ProcessSprite(byte[] rawData, uint[] palette, ushort tileSqrt = 64)
	{
		byte[] rgbaData = new byte[tileSqrt * tileSqrt << 2];
		BitArray mask = new(tileSqrt * tileSqrt);
		Span<uint> dest = MemoryMarshal.Cast<byte, uint>(rgbaData.AsSpan());
		dest.Fill(BinaryPrimitives.ReverseEndianness(palette[255]));
		ReadOnlySpan<byte> raw = rawData;
		ushort left = BinaryPrimitives.ReadUInt16LittleEndian(raw[0..2]),
			right = BinaryPrimitives.ReadUInt16LittleEndian(raw[2..4]);
		int columnCount = right - left + 1;
		if (columnCount <= 0) return (rgbaData, mask);
		// The pixel data pool starts immediately after the column offset table.
		// Each column has a 2-byte offset.
		int pixelPoolPtr = 4 + (columnCount << 1);
		for (int col = 0; col < columnCount; col++)
		{
			int offsetIdx = 4 + (col << 1),
				commandsOffset = BinaryPrimitives.ReadUInt16LittleEndian(raw[offsetIdx..(offsetIdx + 2)]);
			while (true)
			{
				if (commandsOffset + 6 > raw.Length) break;
				ushort endY = BinaryPrimitives.ReadUInt16LittleEndian(raw[commandsOffset..(commandsOffset + 2)]);
				if (endY == 0) break;
				// Wolf3D format: endY and startY are stored as 2 * pixel_coordinate
				int actualEndY = endY >> 1,
					// The 2 bytes at commandsOffset + 2 are the offset to the next column/command 
					// (Standard Wolf3D doesn't really use this for much, we ignore it)
					actualStartY = BinaryPrimitives.ReadUInt16LittleEndian(raw[(commandsOffset + 4)..(commandsOffset + 6)]) >> 1;
				commandsOffset += 6;
				for (int row = actualStartY; row < actualEndY; row++)
				{
					int pixelIdx = row * tileSqrt + col + left;
					if (pixelIdx < dest.Length && pixelPoolPtr < raw.Length)
					{
						dest[pixelIdx] = BinaryPrimitives.ReverseEndianness(palette[raw[pixelPoolPtr++]]);
						mask[pixelIdx] = true;
					}
				}
			}
		}
		ApplyTransparentBorder(rgbaData, tileSqrt);
		return (rgbaData, mask);
	}
	#region ApplyTransparentBorder
	public static void ApplyTransparentBorder(byte[] rgbaData, ushort width)
	{
		Span<uint> dest = MemoryMarshal.Cast<byte, uint>(rgbaData.AsSpan());
		int length = dest.Length,
			height = length / width;
		Span<uint> src = stackalloc uint[length];
		dest.CopyTo(src);
		Span<uint> neighbors = stackalloc uint[8];
		for (int x = 0; x < width; x++)
		{
			int columnOffset = x * width;
			for (int y = 0; y < height; y++)
			{
				int idx = columnOffset + y;
				if (!IsOpaque(src[idx]))
				{
					int count = 0;
					TryAdd(src, x - 1, y, width, height, neighbors, ref count);
					TryAdd(src, x + 1, y, width, height, neighbors, ref count);
					TryAdd(src, x, y - 1, width, height, neighbors, ref count);
					TryAdd(src, x, y + 1, width, height, neighbors, ref count);
					if (count == 0)
					{
						TryAdd(src, x - 1, y - 1, width, height, neighbors, ref count);
						TryAdd(src, x + 1, y - 1, width, height, neighbors, ref count);
						TryAdd(src, x - 1, y + 1, width, height, neighbors, ref count);
						TryAdd(src, x + 1, y + 1, width, height, neighbors, ref count);
					}
					if (count > 0)
					{
						// We must average channels without letting them "bleed" into each other
						// We'll use masks to keep R, G, and B separate during the sum.
						uint r = 0, g = 0, b = 0;
						for (int i = 0; i < count; i++)
						{
							uint p = neighbors[i];
							// Extract channels based on your palette's byte order
							// This assumes your reversed palette results in [R, G, B, A] in memory
							r += (p >> 0) & 0xFF;
							g += (p >> 8) & 0xFF;
							b += (p >> 16) & 0xFF;
						}
						// Reconstruct the transparent pixel with the average color
						// Alpha is set to 0.
						dest[idx] = ((r / (uint)count) << 0) |
									((g / (uint)count) << 8) |
									((b / (uint)count) << 16) |
									(0x00u << 24);
					}
					else dest[idx] = 0u; // Total transparent black
				}
			}
		}
	}
	// Helper to determine opacity based on your reversed palette structure
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsOpaque(uint color) => (color >> 24) > 128;
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void TryAdd(ReadOnlySpan<uint> src, int x, int y, int w, int h, Span<uint> neighbors, ref int count)
	{
		if (x >= 0 && x < w && y >= 0 && y < h)
		{
			uint p = src[x * w + y];
			if (IsOpaque(p)) neighbors[count++] = p;
		}
	}
	#endregion ApplyTransparentBorder
	#region Palettes
	public static uint PaletteNumber(int pageNumber, XElement xml) =>
		xml?.Element("VSwap")?.Descendants()?.Where(
			e => ushort.TryParse(e.Attribute("Page")?.Value, out ushort page) && page == pageNumber
			)?.Select(e => uint.TryParse(e.Attribute("Palette")?.Value, out uint palette) ? palette : 0)
		?.FirstOrDefault() ?? 0;
	public static IEnumerable<uint[]> LoadPalettes(XElement xml) =>
		xml.Element("Palette") is XElement p
			? [ParseXmlPalette(p)]
			: [];
	private static uint[] ParseXmlPalette(XElement paletteElement)
	{
		uint[] result = new uint[256];
		List<XElement> colorElements = paletteElement.Elements("Color").ToList();
		for (int i = 0; i < 256 && i < colorElements.Count; i++)
		{
			string hexAttr = colorElements[i].Attribute("Hex")?.Value;
			if (hexAttr != null && hexAttr.Length == 7 && hexAttr[0] == '#' &&
				uint.TryParse(hexAttr[1..], System.Globalization.NumberStyles.HexNumber, null, out uint rgb))
				result[i] = (rgb << 8) | (i == 255 ? 0u : 0xFFu);
		}
		return result;
	}
	#endregion Palettes
}
