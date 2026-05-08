using System.Text.Json;
using BenMcLean.Wolf3D.Assets;
using BenMcLean.Wolf3D.Assets.Sound;

namespace BenMcLean.Wolf3D.MusicTool;

internal static class WolfOp2Extractor
{
	private static readonly int[] ModulatorOffsets = [0, 1, 2, 8, 9, 10, 16, 17, 18];
	private static readonly int[] CarrierOffsets = [3, 4, 5, 11, 12, 13, 19, 20, 21];

	public static void Export(string gameXml, string songName, string midiPath, string outOp2, string outJson)
	{
		AssetManager assets = AssetManager.Load(Path.GetFullPath(gameXml));
		if (!assets.AudioT.Songs.TryGetValue(songName, out AudioT.Music? song) || song.Imf is null)
			throw new InvalidOperationException($"Song '{songName}' was not found as IMF data in '{gameXml}'.");
		if (!File.Exists(midiPath))
			throw new FileNotFoundException("Target MIDI not found.", midiPath);

		List<ExtractedPatch> patches = ExtractPatches(song.Imf);
		if (patches.Count == 0)
			throw new InvalidOperationException($"No melodic patches were detected in '{songName}'.");

		using FileStream midiStream = File.OpenRead(midiPath);
		Midi midi = Midi.Parse(midiStream);
		List<byte> usedPrograms = CollectUsedPrograms(midi);

		Op2Bank bank = Op2Bank.CreateSilent();
		Dictionary<byte, int> assignments = new();
		for (int i = 0; i < usedPrograms.Count; i++)
		{
			byte program = usedPrograms[i];
			int patchIndex = i % patches.Count;
			assignments[program] = patchIndex;
			bank.Patches[program] = patches[patchIndex].ToOp2Patch();
			bank.Names[program] = $"{songName} Patch {patchIndex + 1:D2}";
		}

		bank.Save(outOp2);

		object manifest = new
		{
			GameXml = Path.GetFullPath(gameXml),
			Song = songName,
			SourceMidi = Path.GetFullPath(midiPath),
			ExtractedPatches = patches.Select((patch, index) => new
			{
				Index = index,
				patch.FirstSeenAtEvent,
				patch.FirstSeenChannel,
				patch.NoteOnsObserved,
				Registers = patch.Registers
			}),
			UsedPrograms = usedPrograms.Select(program => new
			{
				Program = program,
				AssignedPatchIndex = assignments[program],
				AssignedPatchName = bank.Names[program]
			})
		};

		WriteJson(outJson, manifest);
	}

	private static List<byte> CollectUsedPrograms(Midi midi)
	{
		byte[] channelPrograms = new byte[16];
		HashSet<byte> seen = [];
		List<byte> ordered = [];

		foreach (Midi.MidiEvent midiEvent in midi.Events)
			switch (midiEvent)
			{
				case Midi.ProgramChangeEvent programChange:
					channelPrograms[programChange.Channel] = programChange.Program;
					break;
				case Midi.NoteOnEvent noteOn when noteOn.Channel != 9 && noteOn.Velocity > 0:
					byte program = channelPrograms[noteOn.Channel];
					if (seen.Add(program))
						ordered.Add(program);
					break;
			}

		if (ordered.Count == 0)
			ordered.Add(0);

		return ordered;
	}

	private static List<ExtractedPatch> ExtractPatches(Imf[] imf)
	{
		byte[] state = new byte[256];
		Dictionary<WolfPatchRegisters, ExtractedPatch> patches = [];
		bool rhythmMode = false;

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
			if (!keyOn || channel == 0 || (rhythmMode && channel >= 6))
				continue;

			WolfPatchRegisters registers = ReadPatch(state, channel);
			if (registers.IsSilent)
				continue;

			if (!patches.TryGetValue(registers, out ExtractedPatch? patch))
			{
				patch = new ExtractedPatch
				{
					Registers = registers,
					FirstSeenAtEvent = eventIndex,
					FirstSeenChannel = channel
				};
				patches[registers] = patch;
			}

			patch.NoteOnsObserved++;
		}

		return [.. patches.Values.OrderBy(p => p.FirstSeenAtEvent)];
	}

	private static WolfPatchRegisters ReadPatch(byte[] state, int channel)
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
