using System;
using System.Buffers.Binary;
using System.IO;

namespace BenMcLean.Wolf3D.Assets.Graphics;

/// <summary>
/// Parses Wolfenstein 3-D VGAGRAPH chunk fonts
/// Stores glyphs as RGBA8888 textures, colored white
/// </summary>
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
		for (int i = 0; i < location.Length; i++)
			location[i] = binaryReader.ReadUInt16();
		Width = new byte[location.Length];
		for (int i = 0; i < Width.Length; i++)
			Width[i] = binaryReader.ReadByte();
		Character = new byte[Width.Length][];
		int height4 = Height << 2;
		for (int i = 0; i < Character.Length; i++)
		{
			Character[i] = new byte[Width[i] * height4];
			stream.Seek(location[i], 0);
			for (int j = 0; j < Character[i].Length; j += 4)
				if (binaryReader.ReadByte() != 0)
					BinaryPrimitives.WriteUInt32BigEndian(
						destination: Character[i].AsSpan(
							start: j,
							length: 4),
						value: 0xFFFFFFFFu);
		}
	}
	public byte[] Text(string input, ushort padding = 0)
	{
		if (string.IsNullOrWhiteSpace(input))
			return null;
		int width = CalcWidth(input);
		string[] lines = input.Split('\n');
		byte[] bytes = new byte[width * (Height + padding) * lines.Length << 2];
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
			for (int y = 0; y < Height; y++)
				Array.Copy(
					sourceArray: Character[c],
					sourceIndex: y * Width[c] << 2,
					destinationArray: bytes,
					destinationIndex: y * width + rowStart,
					length: Width[c] << 2);
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
