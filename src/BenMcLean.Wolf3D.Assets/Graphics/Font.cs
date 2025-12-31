using System;
using System.IO;

namespace BenMcLean.Wolf3D.Assets.Graphics;

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
					Array.Copy(
						sourceArray: whitePixel,
						sourceIndex: 0,
						destinationArray: Character[i],
						destinationIndex: j << 2,
						length: 4);
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
