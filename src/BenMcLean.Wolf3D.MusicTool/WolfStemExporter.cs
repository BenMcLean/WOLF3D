using System.Text.Json;
using BenMcLean.Wolf3D.Assets;
using BenMcLean.Wolf3D.Assets.Sound;
using NScumm.Audio.OPL.Woody;
using NScumm.Core.Audio.OPL;

namespace BenMcLean.Wolf3D.MusicTool;

internal static class WolfStemExporter
{
	private const int SampleRate = 44100;
	private const double StemGainDb = 17.0;
	private static readonly int[] ModulatorOffsets = [0, 1, 2, 8, 9, 10, 16, 17, 18];
	private static readonly int[] CarrierOffsets = [3, 4, 5, 11, 12, 13, 19, 20, 21];
	private static readonly Dictionary<int, int> OperatorChannelByOffset = BuildOperatorChannelByOffset();

	public static void Export(string gameXml, string songName, string mapJsonPath, string outDir)
	{
		AssetManager assets = MusicToolAssetLoader.Load(gameXml);
		if (!assets.AudioT.Songs.TryGetValue(songName, out AudioT.Music? song) || song.Imf is null)
			throw new InvalidOperationException($"Song '{songName}' was not found as IMF data in '{gameXml}'.");

		WolfStemMap map = JsonSerializer.Deserialize<WolfStemMap>(
			File.ReadAllText(mapJsonPath),
			new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true
			}) ?? throw new InvalidOperationException($"Failed to parse wolfmidi JSON '{mapJsonPath}'.");

		Directory.CreateDirectory(outDir);
		foreach (WolfStemGroup group in BuildGroups(map))
		{
			short[] samples = RenderStem(song.Imf, group.AdlibChannels);
			string fileName = $"{group.Program:D3}-{SanitizeFileName(group.PatchName)}-ch{string.Join("+", group.AdlibChannels.Select(channel => channel.ToString("D2")))}.wav";
			WavWriter.WriteMono16(Path.Combine(outDir, fileName), SampleRate, AudioPostProcessor.ApplyGainDb(samples, StemGainDb));
		}
	}

	private static IReadOnlyList<WolfStemGroup> BuildGroups(WolfStemMap map) =>
		[.. map.FlStudioPrograms
			.OrderBy(program => program.Program)
			.Select(program => new WolfStemGroup(
				program.Program,
				program.PatchName,
				[.. program.AdlibChannels.OrderBy(channel => channel)]))];

	private static short[] RenderStem(Imf[] imf, IReadOnlyList<int> adlibChannelsOneBased)
	{
		HashSet<int> targetChannels = [.. adlibChannelsOneBased.Select(channel => channel - 1)];
		WoodyEmulatorOpl opl = new(OplType.Opl2);
		opl.Init(SampleRate);

		List<short> samples = [];
		double pendingSamples = 0;

		foreach (Imf step in imf)
		{
			if (ShouldApplyRegister(step.Register, targetChannels))
				opl.WriteReg(step.Register, step.Data);

			pendingSamples += step.Delay * (SampleRate / 700.0);
			int wholeSamples = (int)pendingSamples;
			if (wholeSamples <= 0)
				continue;

			short[] buffer = new short[wholeSamples];
			opl.ReadBuffer(buffer, 0, buffer.Length);
			samples.AddRange(buffer);
			pendingSamples -= wholeSamples;
		}

		// Let final note releases decay audibly.
		short[] tail = new short[(int)(SampleRate * 0.75)];
		opl.ReadBuffer(tail, 0, tail.Length);
		samples.AddRange(tail);
		return [.. samples];
	}

	private static bool ShouldApplyRegister(int register, HashSet<int> targetChannels)
	{
		int? channel = TryGetChannel(register);
		return channel is null || targetChannels.Contains(channel.Value);
	}

	private static int? TryGetChannel(int register)
	{
		if (register is >= 0xA0 and <= 0xA8)
			return register - 0xA0;
		if (register is >= 0xB0 and <= 0xB8)
			return register - 0xB0;
		if (register is >= 0xC0 and <= 0xC8)
			return register - 0xC0;

		if (register is >= 0x20 and <= 0x35 ||
			register is >= 0x40 and <= 0x55 ||
			register is >= 0x60 and <= 0x75 ||
			register is >= 0x80 and <= 0x95 ||
			register is >= 0xE0 and <= 0xF5)
		{
			int operatorOffset = register & 0x1F;
			if (OperatorChannelByOffset.TryGetValue(operatorOffset, out int channel))
				return channel;
		}

		return null;
	}

	private static Dictionary<int, int> BuildOperatorChannelByOffset()
	{
		Dictionary<int, int> result = [];
		for (int channel = 0; channel < 9; channel++)
		{
			result[ModulatorOffsets[channel]] = channel;
			result[CarrierOffsets[channel]] = channel;
		}

		return result;
	}

	private static string SanitizeFileName(string value)
	{
		char[] invalid = Path.GetInvalidFileNameChars();
		char[] chars = value
			.Select(character => invalid.Contains(character) ? '_' : character)
			.ToArray();
		return new string(chars).Replace(' ', '_');
	}

	private sealed record WolfStemGroup(int Program, string PatchName, IReadOnlyList<int> AdlibChannels);

	private sealed class WolfStemMap
	{
		public required List<WolfStemProgram> FlStudioPrograms { get; init; }
	}

	private sealed class WolfStemProgram
	{
		public required int Program { get; init; }
		public required string PatchName { get; init; }
		public required int[] AdlibChannels { get; init; }
	}
}
