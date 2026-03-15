using FileToVoxCore.Vox;

namespace BenMcLean.Wolf3D.VoxelBaker;

public class Program
{
	public record SpriteMapping(ushort Page, string Name);
	public static readonly ILookup<string, SpriteMapping> Map = new Dictionary<string, SpriteMapping[]>()
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
		string[] files = Directory.GetFiles(folderPath, "*.vox");
		uint[]? referencePalette = null;
		int filesChecked = 0;
		foreach (string file in files)
		{
			try
			{
				using FileStream stream = File.OpenRead(file);
				VoxelData[] models = Models(stream, out uint[] currentPalette);
				// 1. Check frame count
				if (models.Length != 1)
				{
					Console.WriteLine($"\nFail: '{Path.GetFileName(file)}' has {models.Length} frames (expected 1).");
					return;
				}
				// 2. Check palette consistency
				if (referencePalette is null)
					referencePalette = currentPalette;
				else if (!currentPalette.SequenceEqual(referencePalette))
				{
					Console.WriteLine($"\nFail: '{Path.GetFileName(file)}' has a different palette than previous files.");
					return;
				}
				filesChecked++;
				Console.Write($"\rChecked {filesChecked} files...");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"\nError reading '{Path.GetFileName(file)}': {ex.Message}");
				return;
			}
		}
		Console.WriteLine($"\nSuccess! All {filesChecked} files passed.");
	}
	public static uint Color(FileToVoxCore.Drawing.Color color) => Argb2rgba((uint)color.ToArgb());
	/// <param name="rgba">argb8888, Big Endian</param>
	/// <returns>rgba8888, Big Endian</returns>
	public static uint Argb2rgba(uint argb) => argb << 8 | argb >> 24;
	public static VoxelData[] Models(Stream stream, out uint[] palette)
	{
		VoxModel model = new VoxReader().LoadModel(stream);
		palette = new uint[256];
		uint[] sourceArray = [.. model.Palette.Take(palette.Length).Select(Color)];
		Array.Copy(
			sourceArray: sourceArray,
			sourceIndex: 0,
			destinationArray: palette,
			destinationIndex: 1,
			length: Math.Min(palette.Length, sourceArray.Length) - 1);
		return [.. model.VoxelFrames];
	}
}
