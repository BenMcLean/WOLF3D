using System.Buffers.Binary;
using VoxReader.Interfaces;

namespace BenMcLean.Wolf3D.VoxelBaker;

public class Program
{
	public record SpriteMapping(ushort Page, string Name);
	public static readonly ILookup<string, SpriteMapping> SpriteMap = new Dictionary<string, SpriteMapping[]>()
	{
		["knife"] = [
			new(522,"SPR_KNIFEREADY"),
			new(523,"SPR_KNIFEATK1"),
			new(524,"SPR_KNIFEATK2"),
			new(525,"SPR_KNIFEATK3"),
			new(526,"SPR_KNIFEATK4")],
		["pistol"] = [
			new(527,"SPR_PISTOLREADY"),
			new(528,"SPR_PISTOLATK1"),
			new(529,"SPR_PISTOLATK2"),
			new(530,"SPR_PISTOLATK3"),
			new(531,"SPR_PISTOLATK4")],
		["machinegun"] = [
			new(532,"SPR_MACHINEGUNREADY"),
			new(533,"SPR_MACHINEGUNATK1"),
			new(534,"SPR_MACHINEGUNATK2"),
			new(535,"SPR_MACHINEGUNATK3"),
			new(536,"SPR_MACHINEGUNATK4")],
		["chaingun"] = [
			new(537,"SPR_CHAINREADY"),
			new(538,"SPR_CHAINATK1"),
			new(539,"SPR_CHAINATK2"),
			new(540,"SPR_CHAINATK3"),
			new(541,"SPR_CHAINATK4")]
	}
	.SelectMany(pair => pair.Value, (pair, mapping) => new { pair.Key, mapping })
	.ToLookup(x => x.Key, x => x.mapping);
	public static void Main(string[] args)
	{
		string folderPath = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
		uint[]? palette = null;
		Dictionary<string, IModel> models = [];
		foreach (IGrouping<string, SpriteMapping> grouping in SpriteMap)
		{
			string path = Path.Combine(folderPath, grouping.Key + ".vox");
			using FileStream fs = new(
				path: path,
				mode: FileMode.Open,
				access: FileAccess.Read);
			models[grouping.Key] = Models(fs, out uint[] currentPalette)[0];
			if (palette is null)
				palette = currentPalette;
			else if (!palette.SequenceEqual(currentPalette))
				throw new InvalidDataException($"Palette doesn't match for {path}");
		}
		VoxelAtlasPacker<string>.PackResult packResult = VoxelAtlasPacker<string>.Pack(models
			.Select(kvp => new VoxelAtlasPacker<string>.Cuboid(
				Id: kvp.Key,
				Width: kvp.Value.GlobalSize.X,
				Height: kvp.Value.GlobalSize.Y,
				Depth: kvp.Value.GlobalSize.Z)));
		byte[] atlasBytes = BakeAtlas(packResult, models);
		Console.WriteLine($"Atlas: {packResult.Width}x{packResult.Height}x{packResult.Depth} = {atlasBytes.Length} bytes");
	}
	/// <summary>
	/// Bake all packed voxel models into a flat byte array for a 3D texture.
	/// Layout: index = x + y * width + z * (width * height)
	/// 0 = transparent, 1-255 = palette color index.
	/// </summary>
	public static byte[] BakeAtlas(
		VoxelAtlasPacker<string>.PackResult packResult,
		Dictionary<string, IModel> models)
	{
		int width = packResult.Width,
			height = packResult.Height,
			depth = packResult.Depth;
		byte[] atlas = new byte[width * height * depth];
		Parallel.ForEach(packResult.Placements, placement =>
		{
			IModel model = models[placement.Id];
			foreach (VoxReader.Voxel voxel in model.Voxels)
			{
				int ax = placement.X + voxel.LocalPosition.X,
					ay = placement.Y + voxel.LocalPosition.Y,
					az = placement.Z + voxel.LocalPosition.Z;
				atlas[ax + ay * width + az * (width * height)] = (byte)voxel.ColorIndex;
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
	public static void WriteVgaPalette(Stream stream, uint[] palette)
	{
		if (palette.Length < 256)
			throw new ArgumentException("Palette too short.", nameof(palette));
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
	/// <summary>
	/// Compute power of two greater than or equal to n.
	/// </summary>
	public static ushort NextPowerOf2(ushort n)
	{
		if (n == 0) return 1;
		n--;
		n |= (ushort)(n >> 1);
		n |= (ushort)(n >> 2);
		n |= (ushort)(n >> 4);
		n |= (ushort)(n >> 8);
		return (ushort)(n + 1);
	}
}
