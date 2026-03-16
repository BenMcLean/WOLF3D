using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace BenMcLean.Wolf3D.VR.VR;

public class VoxelAtlas
{
	public record Model(int[] XYZ, Dictionary<string, ushort> Sprites);
	public static VoxelAtlas Load(string path)
	{
		using FileStream fileStream = new(
			path: path,
			mode: FileMode.Open,
			access: FileAccess.Read);
		return new VoxelAtlas(fileStream);
	}
	public VoxelAtlas(Stream stream)
	{
		using BinaryReader reader = new(
			input: stream,
			encoding: Encoding.UTF8,
			leaveOpen: true);
		if (!Encoding.UTF8.GetString(reader.ReadBytes(4)).Equals("W3DV"))
			throw new InvalidDataException("Expected: \"W3DV\"");
		string version = ReadString(reader);
		int totalLength = reader.ReadInt32();
		using MemoryStream decompressedStream = new();
		using (MemoryStream compressedStream = new(reader.ReadBytes(totalLength)))
		using (DeflateStream deflateStream = new(
			stream: compressedStream,
			mode: CompressionMode.Decompress))
		{
			deflateStream.CopyTo(decompressedStream);
		}
		decompressedStream.Position = 0;
		using BinaryReader decompressedReader = new(
			input: decompressedStream,
			encoding: Encoding.UTF8);
		Dictionary<string, Model> metadata = JsonSerializer.Deserialize<Dictionary<string, Model>>(ReadString(decompressedReader));
		uint[] palette = ReadVgaPalette(decompressedStream);
		int width = decompressedReader.ReadInt32(),
			depth = decompressedReader.ReadInt32(),
			height = decompressedReader.ReadInt32();
		byte[] atlas = new byte[width * depth * height];
		decompressedReader.Read(atlas);
	}
	#region Palette
	public static uint[] ReadVgaPalette(Stream stream)
	{
		Span<byte> buffer = stackalloc byte[768];
		if (stream.Read(buffer) < 768)
			throw new EndOfStreamException();
		return ParseVgaPalette(buffer);
	}
	public static uint[] ParseVgaPalette(ReadOnlySpan<byte> chunk)
	{
		uint[] palette = new uint[256];
		for (int index = 0, offset = 0; index < 255; index++, offset += 3)
			palette[index] = BinaryPrimitives.ReadUInt32BigEndian(chunk[offset..]) << 2 & 0xFCFCFC00u | 0xFFu;
		palette[255] = (uint)chunk[765] << 26 |
			(uint)chunk[766] << 18 |
			(uint)chunk[767] << 10 |
			0xFFu;
		return palette;
	}
	#endregion Palette
	public static string ReadString(BinaryReader reader) => Encoding.UTF8.GetString(reader.ReadBytes((int)reader.ReadUInt32()));
}
