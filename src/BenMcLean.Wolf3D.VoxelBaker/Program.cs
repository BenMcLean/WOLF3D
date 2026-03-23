using System.IO.Compression;
using System.Text;
using System.Text.Json;
using VoxReader.Interfaces;

namespace BenMcLean.Wolf3D.VoxelBaker;

public static class Program
{
	public static readonly Dictionary<string, Dictionary<string, ushort>> Sprites =
	new()
	{
		["knife"] = new()
		{
			["SPR_KNIFEREADY"] = 522,
			["SPR_KNIFEATK1"] = 523,
			["SPR_KNIFEATK2"] = 524,
			["SPR_KNIFEATK3"] = 525,
			["SPR_KNIFEATK4"] = 526,
		},
		["pistol"] = new()
		{
			["SPR_PISTOLREADY"] = 527,
			["SPR_PISTOLATK1"] = 528,
			["SPR_PISTOLATK2"] = 529,
			["SPR_PISTOLATK3"] = 530,
			["SPR_PISTOLATK4"] = 531,
		},
		["machinegun"] = new()
		{
			["SPR_MACHINEGUNREADY"] = 532,
			["SPR_MACHINEGUNATK1"] = 533,
			["SPR_MACHINEGUNATK2"] = 534,
			["SPR_MACHINEGUNATK3"] = 535,
			["SPR_MACHINEGUNATK4"] = 536,
		},
		["chaingun"] = new()
		{
			["SPR_CHAINREADY"] = 537,
			["SPR_CHAINATK1"] = 538,
			["SPR_CHAINATK2"] = 539,
			["SPR_CHAINATK3"] = 540,
			["SPR_CHAINATK4"] = 541,
		},
	};
	public record Model(int[] XYZ, Dictionary<string, ushort> Sprites);
	public static void Main(string[] args)
	{
		string folderPath = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
		Console.WriteLine("Reading .vox files.");
		(string Key, IModel Model, uint[] Palette)[] loaded = [.. Sprites.Keys
			.AsParallel()
			.Select(key =>
			{
				string path = Path.Combine(folderPath, key + ".vox");
				using FileStream fs = new(
					path: path,
					mode: FileMode.Open,
					access: FileAccess.Read);
				IModel model = Models(fs, out uint[] currentPalette)[0];
				return (Key: key, Model: model, Palette: currentPalette);
			})];
		Console.WriteLine("Done reading .vox files.");
		uint[] palette = loaded[0].Palette;
		if (loaded.FirstOrDefault(x => !palette.SequenceEqual(x.Palette)).Key is string mismatch)
			throw new InvalidDataException($"Palette doesn't match for {Path.Combine(folderPath, mismatch + ".vox")}");
		if (palette.Length == 255)
		{
			uint[] destination = new uint[256];
			Array.Copy(
				sourceArray: palette,
				sourceIndex: 0,
				destinationArray: destination,
				destinationIndex: 1,
				length: palette.Length);
			palette = destination;
		}
		Dictionary<string, IModel> models = loaded.ToDictionary(x => x.Key, x => x.Model);
		VoxelAtlasPacker<string>.PackResult packResult = VoxelAtlasPacker<string>.Pack(models
			.Select(kvp => new VoxelAtlasPacker<string>.Cuboid(
				Id: kvp.Key,
				X: kvp.Value.LocalSize.X,
				Y: kvp.Value.LocalSize.Y,
				Z: kvp.Value.LocalSize.Z)));
		string outputPath = Path.Combine(folderPath, "VOXELS.W3D");
		using MemoryStream compressedStream = new();
		using (DeflateStream deflateStream = new(
			stream: compressedStream,
			compressionLevel: CompressionLevel.SmallestSize,
			leaveOpen: true))
		using (BinaryWriter deflateWriter = new(
			output: deflateStream,
			encoding: Encoding.UTF8,
			leaveOpen: true))
		{
			Console.WriteLine("Compressing metadata.");
			WriteString(deflateWriter, JsonSerializer.Serialize(
				packResult.Placements.ToDictionary(
					c => c.Id,
					c => new Model(
						XYZ: [
							c.X,
							c.Y,
							c.Z,
							models[c.Id].LocalSize.X,
							models[c.Id].LocalSize.Y,
							models[c.Id].LocalSize.Z,
							Math.Abs(models[c.Id].GlobalPosition.X),
							Math.Abs(models[c.Id].GlobalPosition.Y),
							Math.Abs(models[c.Id].GlobalPosition.Z),
						],
						Sprites: Sprites[c.Id]))));
			Console.WriteLine("Compressing palette.");
			WriteVgaPalette(deflateWriter, palette);
			Console.WriteLine("Compressing atlas.");
			deflateWriter.Write(packResult.Width);
			deflateWriter.Write(packResult.Depth);
			deflateWriter.Write(packResult.Height);
			deflateWriter.Write(BakeAtlas(packResult, models));
		}
		Console.WriteLine($"Opening {outputPath} for writing.");
		using FileStream output = new(
			path: outputPath,
			mode: FileMode.Create,
			access: FileAccess.Write);
		BinaryWriter writer = new(output);
		writer.Write(Encoding.UTF8.GetBytes("W3DV"));
		WriteString(writer, "0");//Version
		writer.Write((int)compressedStream.Length);
		writer.Write(compressedStream.ToArray());
		Console.WriteLine($"{models.Count} models packed in atlas size {packResult.Width}, {packResult.Depth}, {packResult.Height} written to {outputPath}");
	}
	/// <summary>
	/// Bake all packed voxel models into a flat byte array for a 3D texture.
	/// Layout: index = x + y * width + z * (width * depth)
	/// 0 = transparent, 1-255 = palette color index.
	/// </summary>
	public static byte[] BakeAtlas(
		VoxelAtlasPacker<string>.PackResult packResult,
		Dictionary<string, IModel> models)
	{
		int width = packResult.Width,
			depth = packResult.Depth,
			height = packResult.Height;
		byte[] atlas = new byte[width * depth * height];
		Parallel.ForEach(packResult.Placements, placement =>
		{
			IModel model = models[placement.Id];
			foreach (VoxReader.Voxel voxel in model.Voxels)
			{
				int ax = placement.X + voxel.LocalPosition.X,
					ay = placement.Y + voxel.LocalPosition.Y,
					az = placement.Z + voxel.LocalPosition.Z;
				atlas[ax + ay * width + az * width * depth] = (byte)voxel.ColorIndex;
			}
		});
		return atlas;
	}
	#region VoxReader
	public static uint ToRgba(VoxReader.Color color) =>
		(uint)color.R << 24 |
		(uint)color.G << 16 |
		(uint)color.B << 8 |
		color.A;
	public static IModel[] Models(Stream stream, out uint[] palette)
	{
		using MemoryStream ms = new();
		stream.CopyTo(ms);
		IVoxFile voxFile = VoxReader.VoxReader.Read(ms.ToArray());
		palette = [.. voxFile.Palette.Colors.Select(ToRgba)];
		return voxFile.Models;
	}
	#endregion VoxReader
	#region Utilities
	public static void WriteVgaPalette(BinaryWriter writer, uint[]? palette)
	{
		ArgumentNullException.ThrowIfNull(palette);
		if (palette.Length < 256)
			throw new ArgumentException($"Palette too short. Actual: {palette.Length}, Expected: 256.", nameof(palette));
		Span<byte> buffer = stackalloc byte[768];
		for (int i = 0, offset = 0; i < 256; i++, offset += 3)
		{
			buffer[offset] = (byte)((palette[i] >> 26) & 0x3F);
			buffer[offset + 1] = (byte)((palette[i] >> 18) & 0x3F);
			buffer[offset + 2] = (byte)((palette[i] >> 10) & 0x3F);
		}
		writer.Write(buffer);
	}
	public static void WriteString(BinaryWriter writer, string s)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(s);
		writer.Write(bytes.Length);
		writer.Write(bytes);
	}
	#endregion Utilities
}
