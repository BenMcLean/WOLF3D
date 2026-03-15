using System.Buffers.Binary;
using System.Linq;
using System.Text;
using System.Text.Json;
using VoxReader.Interfaces;

namespace BenMcLean.Wolf3D.VoxelBaker;

public class Program
{
	public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, ushort>> SpriteMap =
	new Dictionary<string, IReadOnlyDictionary<string, ushort>>
	{
		["knife"] = new Dictionary<string, ushort>()
		{
			["SPR_KNIFEREADY"] = 522,
			["SPR_KNIFEATK1"] = 523,
			["SPR_KNIFEATK2"] = 524,
			["SPR_KNIFEATK3"] = 525,
			["SPR_KNIFEATK4"] = 526,
		}.AsReadOnly(),
		["pistol"] = new Dictionary<string, ushort>()
		{
			["SPR_PISTOLREADY"] = 527,
			["SPR_PISTOLATK1"] = 528,
			["SPR_PISTOLATK2"] = 529,
			["SPR_PISTOLATK3"] = 530,
			["SPR_PISTOLATK4"] = 531,
		}.AsReadOnly(),
		["machinegun"] = new Dictionary<string, ushort>()
		{
			["SPR_MACHINEGUNREADY"] = 532,
			["SPR_MACHINEGUNATK1"] = 533,
			["SPR_MACHINEGUNATK2"] = 534,
			["SPR_MACHINEGUNATK3"] = 535,
			["SPR_MACHINEGUNATK4"] = 536,
		}.AsReadOnly(),
		["chaingun"] = new Dictionary<string, ushort>()
		{
			["SPR_CHAINREADY"] = 537,
			["SPR_CHAINATK1"] = 538,
			["SPR_CHAINATK2"] = 539,
			["SPR_CHAINATK3"] = 540,
			["SPR_CHAINATK4"] = 541,
		}.AsReadOnly(),
	}.AsReadOnly();
	public record Model(ushort[] XYZ, Dictionary<string, ushort> Names);
	public static void Main(string[] args)
	{
		string folderPath = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
		(string Key, IModel Model, uint[] Palette)[] loaded = SpriteMap.Keys
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
			})
			.ToArray();
		uint[] palette = loaded[0].Palette;
		if (loaded.FirstOrDefault(x => !palette.SequenceEqual(x.Palette)).Key is string mismatch)
			throw new InvalidDataException($"Palette doesn't match for {Path.Combine(folderPath, mismatch + ".vox")}");
		Dictionary<string, IModel> models = loaded.ToDictionary(x => x.Key, x => x.Model);
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
		VoxelAtlasPacker<string>.PackResult packResult = VoxelAtlasPacker<string>.Pack(models
			.Select(kvp => new VoxelAtlasPacker<string>.Cuboid(
				Id: kvp.Key,
				X: kvp.Value.GlobalSize.X,
				Y: kvp.Value.GlobalSize.Y,
				Z: kvp.Value.GlobalSize.Z)));
		string outputPath = Path.Combine(folderPath, "VOXELS.W3D");
		using FileStream output = new(
			path: outputPath,
			mode: FileMode.OpenOrCreate,
			access: FileAccess.Write);
		BinaryWriter writer = new(output);
		Console.WriteLine("Writing metadata.");
		Dictionary<string, ushort[]> placements = packResult.Placements
			.Select(cuboid => new KeyValuePair<string, ushort[]>(cuboid.Id, [(ushort)cuboid.X, (ushort)cuboid.Y, (ushort)cuboid.Z]))
			.ToDictionary();
		writer.Write(JsonSerializer.Serialize(SpriteMap.Select(kvp =>
		{
			IModel model = models[kvp.Key];
			ushort[] placement = placements[kvp.Key];
			return new KeyValuePair<string, Model>(kvp.Key, new Model(
				XYZ: [
					(ushort)model.GlobalPosition.X,
					(ushort)model.GlobalPosition.Y,
					(ushort)model.GlobalPosition.Z,
					placement[0],
					placement[1],
					placement[2]],
				Names: kvp.Value.ToDictionary()));
		}).ToDictionary()));
		Console.WriteLine("Writing palette.");
		WriteVgaPalette(output, palette);
		Console.WriteLine("Writing atlas.");
		writer.Write(packResult.Width);
		writer.Write(packResult.Depth);
		writer.Write(packResult.Height);
		output.Write(BakeAtlas(packResult, models));
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
	public static void WriteVgaPalette(Stream stream, uint[]? palette)
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
		stream.Write(buffer);
	}
	#endregion Palette
	#region Utilities
	public static string ReadString(BinaryReader reader) => Encoding.UTF8.GetString(reader.ReadBytes((int)reader.ReadUInt32()));
	public static void WriteString(BinaryWriter writer, string s)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(s);
		writer.Write((uint)bytes.Length);
		writer.Write(bytes);
	}
	#endregion Utilities
}
