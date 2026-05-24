using System.Text.Json;
using BenMcLean.Wolf3D.Assets;
using BenMcLean.Wolf3D.Assets.Sound;

namespace BenMcLean.Wolf3D.MusicTool;

internal static class WolfOp2Extractor
{
	private static readonly int[] ModulatorOffsets = [0, 1, 2, 8, 9, 10, 16, 17, 18];
	private static readonly int[] CarrierOffsets = [3, 4, 5, 11, 12, 13, 19, 20, 21];

	public static void Export(string gameXml, string songName, string outOp2, string outJson)
	{
		AssetManager assets = MusicToolAssetLoader.Load(gameXml);
		if (!assets.AudioT.Songs.TryGetValue(songName, out AudioT.Music? song) || song.Imf is null)
			throw new InvalidOperationException($"Song '{songName}' was not found as IMF data in '{gameXml}'.");

		List<ExtractedPatch> patches = ExtractPatches(songName, song.Imf);
		if (patches.Count == 0)
			throw new InvalidOperationException($"No melodic patches were detected in '{songName}'.");
		if (patches.Count > Op2Bank.MelodicCount)
			throw new InvalidOperationException(
				$"Detected {patches.Count} melodic patches in '{songName}', but OP2 only has room for {Op2Bank.MelodicCount} melodic entries.");

		Op2Bank bank = Op2Bank.CreateSilent();
		for (int i = 0; i < patches.Count; i++)
		{
			ExtractedPatch patch = patches[i];
			bank.Patches[i] = patch.ToOp2Patch();
			bank.Names[i] = patch.BankName;
		}

		bank.Save(outOp2);

		IReadOnlyList<object> channelManifest = patches
			.GroupBy(p => p.FirstSeenChannel)
			.OrderBy(group => group.Key)
			.Select(group => new
			{
				AdlibChannel = group.Key,
				Patches = group
					.OrderBy(p => p.BankIndex)
					.Select(p => new
					{
						p.BankIndex,
						p.BankName,
						p.FirstSeenAtEvent,
						p.NoteOnsObserved,
						Registers = p.Registers
					})
			})
			.Cast<object>()
			.ToArray();

		object manifest = new
		{
			GameXml = PortablePath.ToStoredPath(outJson, gameXml),
			Song = songName,
			TotalExtractedPatches = patches.Count,
			BankLayout = patches.Select(patch => new
			{
				patch.BankIndex,
				patch.BankName,
				patch.FirstSeenAtEvent,
				patch.FirstSeenChannel,
				patch.NoteOnsObserved,
				Registers = patch.Registers
			}),
			Channels = channelManifest
		};

		WriteJson(outJson, manifest);
	}

	private static List<ExtractedPatch> ExtractPatches(string songName, Imf[] imf)
	{
		byte[] state = new byte[256];
		Dictionary<(int Channel, WolfPatchRegisters Registers), ExtractedPatch> patches = [];
		bool rhythmMode = false;
		int bankIndex = 0;

		for (int eventIndex = 0; eventIndex < imf.Length; eventIndex++)
		{
			Imf step = imf[eventIndex];
			state[step.Register] = step.Data;
			if (step.Register == 0xBD)
			{
				rhythmMode = (step.Data & 0x20) != 0;
				continue;
			}

			if (step.Register < 0xB0 || step.Register > 0xB8)
				continue;

			int channel = step.Register - 0xB0;
			bool keyOn = (step.Data & 0x20) != 0;
			if (!keyOn || (rhythmMode && channel >= 6))
				continue;

			WolfPatchRegisters registers = ReadPatchFromState(state, channel);
			if (registers.IsSilent)
				continue;

			var patchKey = (channel, registers);
			if (!patches.TryGetValue(patchKey, out ExtractedPatch? patch))
			{
				patch = new ExtractedPatch
				{
					BankIndex = bankIndex++,
					BankName = $"{songName} Ch{channel + 1:D2} Patch {patches.Count(p => p.Key.Channel == channel) + 1:D2}",
					Registers = registers,
					FirstSeenAtEvent = eventIndex,
					FirstSeenChannel = channel
				};
				patches[patchKey] = patch;
			}

			patch.NoteOnsObserved++;
		}

		return [.. patches.Values.OrderBy(p => p.FirstSeenAtEvent)];
	}

	internal static WolfPatchRegisters ReadPatchFromState(byte[] state, int channel)
	{
		int mod = ModulatorOffsets[channel];
		int car = CarrierOffsets[channel];
		return new WolfPatchRegisters
		{
			ModChar = state[0x20 + mod],
			CarChar = state[0x20 + car],
			ModScale = state[0x40 + mod],
			CarScale = state[0x40 + car],
			ModAttack = state[0x60 + mod],
			CarAttack = state[0x60 + car],
			ModSustain = state[0x80 + mod],
			CarSustain = state[0x80 + car],
			ModWave = state[0xE0 + mod],
			CarWave = state[0xE0 + car],
			Feedback = state[0xC0 + channel]
		};
	}

	private static void WriteJson(string path, object value)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
		File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions
		{
			WriteIndented = true
		}));
	}

	internal sealed class ExtractedPatch
	{
		public required int BankIndex { get; init; }
		public required string BankName { get; init; }
		public required WolfPatchRegisters Registers { get; init; }
		public required int FirstSeenAtEvent { get; init; }
		public required int FirstSeenChannel { get; init; }
		public int NoteOnsObserved { get; set; }

		public Op2Patch ToOp2Patch() => Op2Patch.FromFullOplRegisters(
			Registers.ModChar,
			Registers.CarChar,
			Registers.ModScale,
			Registers.CarScale,
			Registers.ModAttack,
			Registers.CarAttack,
			Registers.ModSustain,
			Registers.CarSustain,
			Registers.ModWave,
			Registers.CarWave,
			Registers.Feedback);
	}
}

internal sealed class WolfPatchRegisters : IEquatable<WolfPatchRegisters>
{
	public byte ModChar { get; init; }
	public byte CarChar { get; init; }
	public byte ModScale { get; init; }
	public byte CarScale { get; init; }
	public byte ModAttack { get; init; }
	public byte CarAttack { get; init; }
	public byte ModSustain { get; init; }
	public byte CarSustain { get; init; }
	public byte ModWave { get; init; }
	public byte CarWave { get; init; }
	public byte Feedback { get; init; }

	public bool IsSilent =>
		ModChar == 0 && CarChar == 0 && ModScale == 0 && CarScale == 0 &&
		ModAttack == 0 && CarAttack == 0 && ModSustain == 0 && CarSustain == 0 &&
		ModWave == 0 && CarWave == 0 && Feedback == 0;

	public bool Equals(WolfPatchRegisters? other) =>
		other is not null &&
		ModChar == other.ModChar &&
		CarChar == other.CarChar &&
		ModScale == other.ModScale &&
		CarScale == other.CarScale &&
		ModAttack == other.ModAttack &&
		CarAttack == other.CarAttack &&
		ModSustain == other.ModSustain &&
		CarSustain == other.CarSustain &&
		ModWave == other.ModWave &&
		CarWave == other.CarWave &&
		Feedback == other.Feedback;

	public override bool Equals(object? obj) => Equals(obj as WolfPatchRegisters);

	public override int GetHashCode() =>
		HashCode.Combine(
			HashCode.Combine(ModChar, CarChar, ModScale, CarScale, ModAttack, CarAttack),
			HashCode.Combine(ModSustain, CarSustain, ModWave, CarWave, Feedback));
}
