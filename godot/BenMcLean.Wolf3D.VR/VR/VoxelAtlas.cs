using Godot;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BenMcLean.Wolf3D.VR.VR;

public class VoxelAtlas : IDisposable
{
	public record Model(int[] XYZ, Dictionary<string, ushort> Sprites);
	public Dictionary<string, Model> Metadata { get; }
	/// <summary>R8 3D texture of raw palette indices. 0 = transparent, 1-255 = palette index.</summary>
	public ImageTexture3D Texture { get; }
	/// <summary>256x1 RGBA8 palette LUT. Sample with index/255.0 in shader.</summary>
	public ImageTexture PaletteTexture { get; }
	public static VoxelAtlas Load(string path)
	{
		using FileStream fileStream = new(
			path: path,
			mode: FileMode.Open,
			access: System.IO.FileAccess.Read);
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
		Metadata = JsonSerializer.Deserialize<Dictionary<string, Model>>(ReadString(decompressedReader));
		PaletteTexture = BuildPaletteTexture(ReadVgaPalette(decompressedStream));
		int width = decompressedReader.ReadInt32(),
			depth = decompressedReader.ReadInt32(),
			height = decompressedReader.ReadInt32();
		byte[] atlas = new byte[width * depth * height];
		decompressedReader.Read(atlas);
		Texture = BuildTexture3D(atlas, width, depth, height);
	}
	public void Dispose()
	{
		Texture.Dispose();
		PaletteTexture.Dispose();
		GC.SuppressFinalize(this);
	}
	#region Textures
	/// <summary>
	/// Builds an R8 ImageTexture3D storing raw palette indices.
	/// Atlas layout: index = x + y * width + z * (width * depth)
	/// 0 = transparent, 1-255 = palette index. Shader does palette lookup via PaletteTexture.
	/// Godot slices: height layers of width x depth (atlas depth = Godot slice height).
	///
	/// Coordinate system: W3DV uses MagicaVoxel's Z-up convention. The texture axes map as:
	///   Texture U (Godot width)        = W3DV width  = MagicaVoxel X = Godot world X  (no swap)
	///   Texture V (Godot height)       = W3DV depth  = MagicaVoxel Y = Godot world Z  (horizontal depth)
	///   Texture W (Godot depth/slices) = W3DV height = MagicaVoxel Z = Godot world Y  (up)
	/// MagicaVoxel's up axis (Z) ends up in the texture's slice/W axis, which maps to Godot's Y (up).
	/// In the DDA shader, swap Y and Z when converting Godot local-space position to texture UVW:
	///   vec3 uvw = vec3(local.x / atlas.x, local.z / atlas.z, local.y / atlas.y);
	/// The same swap applies to Metadata offsets and sizes, which are stored in MagicaVoxel (X, Y, Z) order.
	/// </summary>
	public static ImageTexture3D BuildTexture3D(byte[] atlas, int width, int depth, int height)
	{
		int sliceSize = width * depth;
		Image[] images = new Image[height];
		Parallel.For(0, height, z =>
			images[z] = Image.CreateFromData(
				width: width,
				height: depth,
				useMipmaps: false,
				format: Image.Format.R8,
				data: atlas[(z * sliceSize)..((z + 1) * sliceSize)]));
		ImageTexture3D texture = new();
		texture.Create(
			format: Image.Format.R8,
			width: width,
			height: depth,
			depth: height,
			useMipmaps: false,
			data: [.. images]);
		foreach (Image image in images)
			image.Dispose();
		return texture;
	}
	/// <summary>
	/// Builds a 256x1 Rgba8 palette LUT texture.
	/// Each uint 0xRRGGBBAA is written big-endian → bytes [RR, GG, BB, AA] matching Godot's Rgba8 layout.
	/// Transparency (index 0) must be handled in the shader by checking the 3D texture index,
	/// since all palette entries have AA=0xFF (from ParseVgaPalette's | 0xFFu).
	/// In shader: float idx = texture(voxel_atlas, uvw).r; if (idx == 0.0) discard;
	///            vec4 color = texture(palette, vec2(idx, 0.5));
	/// </summary>
	public static ImageTexture BuildPaletteTexture(uint[] palette)
	{
		byte[] imageData = new byte[256 << 2];
		Span<byte> span = imageData;
		for (int i = 0; i < 256; i++)
			BinaryPrimitives.WriteUInt32BigEndian(span[(i << 2)..], palette[i]);
		return ImageTexture.CreateFromImage(
			Image.CreateFromData(
				width: 256,
				height: 1,
				useMipmaps: false,
				format: Image.Format.Rgba8,
				data: imageData));
	}
	#endregion Textures
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
