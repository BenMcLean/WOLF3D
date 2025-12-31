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
		ushort[] location = new ushort[256];
		for (int i = 0; i < location.Length; i++)
			location[i] = binaryReader.ReadUInt16();
		Widths = new byte[location.Length];
		for (int i = 0; i < Widths.Length; i++)
			Widths[i] = binaryReader.ReadByte();
		Glyphs = new byte[Widths.Length][];
		int height4 = Height << 2;
		for (int i = 0; i < Glyphs.Length; i++)
		{
			Glyphs[i] = new byte[Widths[i] * height4];
			stream.Seek(location[i], 0);
			for (int j = 0; j < Glyphs[i].Length; j += 4)
				if (binaryReader.ReadByte() != 0)
					BinaryPrimitives.WriteUInt32BigEndian(
						destination: Glyphs[i].AsSpan(
							start: j,
							length: 4),
						value: 0xFFFFFFFFu);
		}
	}
}
