using System;
using System.Buffers.Binary;
using System.IO;

namespace BenMcLean.Wolf3D.Assets.Graphics;

/// <summary>
/// Parses Wolfenstein 3-D VGAGRAPH chunk fonts
/// Stores glyphs as RGBA8888 textures, colored white
/// </summary>
public class Font
{
	public ushort Height { get; private init; }
	public byte[] Widths { get; private init; }
	public byte[][] Glyphs { get; private init; }
	public Font(Stream stream)
	{
		using BinaryReader binaryReader = new(stream);
		Height = binaryReader.ReadUInt16();
		ushort[] offsets = new ushort[256];
		Buffer.BlockCopy(
			src: binaryReader.ReadBytes(512),
			srcOffset: 0,
			dst: offsets,
			dstOffset: 0,
			count: 512);
		Widths = binaryReader.ReadBytes(256);
		Glyphs = new byte[256][];
		int height4 = Height << 2;
		for (int glyph = 0; glyph < 256; glyph++)
		{
			if (Widths[glyph] == 0)
				continue;
			Glyphs[glyph] = new byte[Widths[glyph] * height4];
			stream.Seek(
				offset: offsets[glyph],
				origin: SeekOrigin.Begin);
			byte[] glyphData = binaryReader.ReadBytes(Widths[glyph] * Height);
			for (int byteIndex = 0, rgbaIndex = 0;
				byteIndex < glyphData.Length;
				byteIndex++, rgbaIndex += 4)
				if (glyphData[byteIndex] != 0)
					BinaryPrimitives.WriteUInt32BigEndian(
						destination: Glyphs[glyph].AsSpan(
							start: rgbaIndex,
							length: 4),
						value: 0xFFFFFFFFu);
		}
	}
}
