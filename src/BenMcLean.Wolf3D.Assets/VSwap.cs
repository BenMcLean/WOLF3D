using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace BenMcLean.Wolf3D.Assets;

/// <summary>
/// VSWAP file loader - Wolf3D graphics/sound resource format
/// File format: 16-bit NumPages, 16-bit SpritePage, 16-bit SoundPage,
/// followed by 32-bit page offsets and 16-bit page lengths
/// </summary>
public sealed class VSwap
{
	public static VSwap Load(XElement xml, string folder="")
	{
		if (!Directory.Exists(folder))
			throw new DirectoryNotFoundException(folder);
		using FileStream vSwap = new(System.IO.Path.Combine(folder, xml.Element("VSwap").Attribute("Name").Value), FileMode.Open);
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
		|| (page >= SpritePage // We know walls aren't transparent
		&& GetOffset(x, y) + 3 is uint offset
		&& offset < Pages[page].Length
		&& Pages[page][offset] > 128);
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
			if ((pageOffsets[i] != 0 && pageOffsets[i] < dataStart) || pageOffsets[i] > stream.Length)
				throw new InvalidDataException("VSWAP contains invalid page offsets.");
		}
		ushort[] pageLengths = new ushort[NumPages];
		for (ushort i = 0; i < pageLengths.Length; i++)
			pageLengths[i] = binaryReader.ReadUInt16();
		ushort page;
		#endregion parse header info
		#region read in walls
		for (page = 0; page < SpritePage; page++)
			if (pageOffsets[page] > 0)
			{
				stream.Seek(pageOffsets[page], 0);
				byte[] wall = new byte[TileSqrt * TileSqrt];
				for (ushort col = 0; col < TileSqrt; col++)
					for (ushort row = 0; row < TileSqrt; row++)
						wall[TileSqrt * row + col] = (byte)stream.ReadByte();
				Pages[page] = wall.Indices2ByteArray(palettes[PaletteNumber(page, xml)]);
			}
		#endregion read in walls
		#region read in sprites
		for (; page < SoundPage; page++)
			if (pageOffsets[page] > 0)
			{
				stream.Seek(pageOffsets[page], 0);
				ushort leftExtent = binaryReader.ReadUInt16(),
					rightExtent = binaryReader.ReadUInt16(),
					startY, endY;
				byte[] sprite = new byte[TileSqrt * TileSqrt];
				for (ushort i = 0; i < sprite.Length; i++)
					sprite[i] = 255; // set transparent
				long[] columnDataOffsets = new long[rightExtent - leftExtent + 1];
				for (ushort i = 0; i < columnDataOffsets.Length; i++)
					columnDataOffsets[i] = pageOffsets[page] + binaryReader.ReadUInt16();
				long trexels = stream.Position;
				for (ushort column = 0; column <= rightExtent - leftExtent; column++)
				{
					long commands = columnDataOffsets[column];
					stream.Seek(commands, 0);
					while ((endY = binaryReader.ReadUInt16()) != 0)
					{
						endY >>= 1;
						binaryReader.ReadUInt16(); // Not using this value for anything. Don't know why it's here!
						startY = binaryReader.ReadUInt16();
						startY >>= 1;
						commands = stream.Position;
						stream.Seek(trexels, 0);
						for (ushort row = startY; row < endY; row++)
							sprite[(row * TileSqrt - 1) + column + leftExtent - 1] = binaryReader.ReadByte();
						trexels = stream.Position;
						stream.Seek(commands, 0);
					}
				}
				Pages[page] = TransparentBorder(sprite.Indices2UIntArray(palettes[PaletteNumber(page, xml)])).UInt2ByteArray();
				BitArray mask = new(sprite.Length);
				for (int i = 0; i < sprite.Length; i++)
					mask[i] = sprite[i] != 0;
				Masks[page - SpritePage] = mask;
			}
		#endregion read in sprites
		#region read in digisounds
		byte[] soundData = new byte[stream.Length - pageOffsets[Pages.Length]];
		stream.Seek(pageOffsets[Pages.Length], 0);
		stream.Read(soundData, 0, soundData.Length);
		uint start = pageOffsets[NumPages - 1] - pageOffsets[Pages.Length];
		ushort[][] soundTable;
		using (MemoryStream memoryStream = new(soundData, (int)start, soundData.Length - (int)start))
			soundTable = VgaGraph.Load16BitPairs(memoryStream);
		uint numDigiSounds = 0;
		while (numDigiSounds < soundTable.Length && soundTable[numDigiSounds][1] > 0)
			numDigiSounds++;
		DigiSounds = new byte[numDigiSounds][];
		for (uint sound = 0; sound < DigiSounds.Length; sound++)
			if (soundTable[sound][1] > 0 && pageOffsets[Pages.Length + soundTable[sound][0]] > 0)
			{
				DigiSounds[sound] = new byte[soundTable[sound][1]];
				start = pageOffsets[Pages.Length + soundTable[sound][0]] - pageOffsets[Pages.Length];
				for (uint bite = 0; bite < DigiSounds[sound].Length; bite++)
					DigiSounds[sound][bite] = (byte)(soundData[start + bite] - 128); // Godot makes some kind of oddball conversion from the unsigned byte to a signed byte
			}
		#endregion read in digisounds
	}
	public static uint PaletteNumber(int pageNumber, XElement xml) =>
		xml?.Element("VSwap")?.Descendants()?.Where(
			e => ushort.TryParse(e.Attribute("Page")?.Value, out ushort page) && page == pageNumber
			)?.Select(e => uint.TryParse(e.Attribute("Palette")?.Value, out uint palette) ? palette : 0)
		?.FirstOrDefault() ?? 0;
	public static IEnumerable<uint[]> LoadPalettes(XElement xml) => xml.Elements("Palette").Select(LoadPalette);
	public static uint[] TransparentBorder(uint[] texture, ushort width = 0)
	{
		if (width == 0)
			width = (ushort)Math.Sqrt(texture.Length);
		uint[] result = new uint[texture.Length];
		Array.Copy(texture, result, result.Length);
		int height = texture.Length / width;
		int Index(int x, int y) => x * width + y;
		List<uint> neighbors = new(9);
		void Add(int x, int y)
		{
			if (x >= 0 && y >= 0 && x < width && y < height
				&& texture[Index(x, y)] is uint pixel
				&& pixel.A() > 128)
				neighbors.Add(pixel);
		}
		uint Average()
		{
			int count = neighbors.Count;
			if (count == 1)
				return neighbors.First() & 0xFFFFFF00u;
			uint r = 0, g = 0, b = 0;
			foreach (uint color in neighbors)
			{
				r += color.R();
				g += color.G();
				b += color.B();
			}
			return Color((byte)(r / count), (byte)(g / count), (byte)(b / count), 0);
		}
		for (int x = 0; x < width; x++)
			for (int y = 0; y < height; y++)
				if (texture[Index(x, y)].A() < 128)
				{
					neighbors.Clear();
					Add(x - 1, y);
					Add(x + 1, y);
					Add(x, y - 1);
					Add(x, y + 1);
					if (neighbors.Count > 0)
						result[Index(x, y)] = Average();
					else
					{
						Add(x - 1, y - 1);
						Add(x + 1, y - 1);
						Add(x - 1, y + 1);
						Add(x + 1, y + 1);
						if (neighbors.Count > 0)
							result[Index(x, y)] = Average();
						else // Make non-border transparent pixels transparent black
							result[Index(x, y)] = 0;
					}
				}
		return result;
	}
	public static uint Color(byte r, byte g, byte b, byte a) => (uint)r << 24 | (uint)g << 16 | (uint)b << 8 | a;
	#region Palette
	public static uint[] LoadPalette(XElement paletteElement)
	{
		XElement[] colorElements = paletteElement.Elements("Color").ToArray();
		if (colorElements.Length != 256)
			throw new InvalidDataException($"Palette must contain exactly 256 colors, found {colorElements.Length}");
		uint[] result = new uint[256];
		for (int i = 0; i < 256; i++)
		{
			string hexValue = colorElements[i].Attribute("Hex")?.Value;
			if (string.IsNullOrEmpty(hexValue) || !hexValue.StartsWith("#") || hexValue.Length != 7)
				throw new InvalidDataException($"Color {i} has invalid Hex attribute: '{hexValue}'. Expected format: #RRGGBB");
			// Parse hex color (remove # and parse as hex)
			if (!uint.TryParse(hexValue[1..], System.Globalization.NumberStyles.HexNumber, null, out uint rgb24))
				throw new InvalidDataException($"Color {i} has invalid hex value: '{hexValue}'");
			// Convert 24-bit RGB to 32-bit RGBA: left-shift by 8 bits and OR with opaque alpha
			// (except index 255 which is transparent)
			result[i] = (rgb24 << 8) | (i == 255 ? 0u : 0xFFu);
		}
		return result;
	}
	#endregion Palette
}
