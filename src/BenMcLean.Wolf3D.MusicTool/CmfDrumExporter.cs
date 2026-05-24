using System.Text;
using System.Text.Json;

namespace BenMcLean.Wolf3D.MusicTool;

internal static class CmfDrumExporter
{
	public static void Export(string cmfPath, string outOp2, string outJson)
	{
		CmfSong song = CmfSong.Load(cmfPath);
		Op2Bank bank = Op2Bank.CreateSilent();
		List<object> reportDrums = [];

		foreach (CmfRhythmUsage usage in song.GetRhythmUsages())
		{
			DrumMapping mapping = usage.Channel switch
			{
				11 => new DrumMapping(
					"Bass Drum",
					[35, 36],
					36,
					"Bass drum uses the full two-operator CMF patch and is structurally faithful in OP2.",
					CreateFullPatch(usage.Patch, noteNumber: 36)),
				12 => new DrumMapping(
					"Snare Drum",
					[38, 40],
					38,
					"Snare is a rhythm-mode single-operator voice. OP2 export approximates it as a carrier-only melodic patch using the CMF carrier operator.",
					CreateCarrierOnlyPatch(usage.Patch, noteNumber: 38)),
				13 => new DrumMapping(
					"Tom",
					[45, 47, 48, 50],
					45,
					"Tom is a rhythm-mode single-operator voice. OP2 export approximates it as a carrier-only melodic patch using the CMF modulator operator.",
					CreateModulatorOnlyPatch(usage.Patch, noteNumber: 45)),
				14 => new DrumMapping(
					"Top Cymbal",
					[49, 52, 55, 57],
					49,
					"Cymbal is a rhythm-mode single-operator voice. OP2 export approximates it as a carrier-only melodic patch using the CMF carrier operator.",
					CreateCarrierOnlyPatch(usage.Patch, noteNumber: 49)),
				15 => new DrumMapping(
					"Hi-Hat",
					[42, 44, 46],
					42,
					"Hi-hat is a rhythm-mode single-operator voice. OP2 export approximates it as a carrier-only melodic patch using the CMF modulator operator.",
					CreateModulatorOnlyPatch(usage.Patch, noteNumber: 42)),
				_ => throw new InvalidOperationException($"Unexpected CMF rhythm channel {usage.Channel}.")
			};

			foreach (int midiNote in mapping.MidiNotes)
			{
				int percussionIndex = Op2Bank.MelodicCount + (midiNote - 35);
				bank.Patches[percussionIndex] = mapping.Patch;
				bank.Names[percussionIndex] = $"Funky {mapping.Name}";
			}

			reportDrums.Add(new
			{
				mapping.Name,
				CmfChannel = usage.Channel,
				GmMidiNotes = mapping.MidiNotes,
				Op2NoteNumber = mapping.Op2NoteNumber,
				Program = usage.Program,
				NoteNumberUsedInCmf = usage.Note,
				NoteOnCount = usage.NoteOnCount,
				VelocityValues = usage.Velocities,
				SourceRegisters = usage.Patch.ToReportObject(),
				mapping.ApproximationNote
			});
		}

		bank.Save(outOp2);
		WriteJson(outJson, new
		{
			CmfPath = PortablePath.ToStoredPath(outJson, cmfPath),
			SongTitle = song.Title,
			RhythmModeEnabled = song.RhythmModeEnabled,
			InstrumentCount = song.Instruments.Count,
			Warning = "Bass drum exports cleanly. Snare, tom, hi-hat, and cymbal are rhythm-mode single-operator voices, so OP2 output is an approximation rather than an exact container.",
			Drums = reportDrums
		});
	}

	private static Op2Patch CreateFullPatch(CmfInstrument patch, byte noteNumber) =>
		Op2Patch.FromFullOplRegisters(
			patch.ModChar, patch.CarChar, patch.ModScale, patch.CarScale,
			patch.ModAttack, patch.CarAttack, patch.ModSustain, patch.CarSustain,
			patch.ModWave, patch.CarWave, patch.Feedback,
			flags: 0x01, noteNumber: noteNumber);

	private static Op2Patch CreateCarrierOnlyPatch(CmfInstrument patch, byte noteNumber) =>
		Op2Patch.FromFullOplRegisters(
			modChar: 0x00, carChar: patch.CarChar,
			modScale: 0x3F, carScale: patch.CarScale,
			modAttack: 0x00, carAttack: patch.CarAttack,
			modSustain: 0xFF, carSustain: patch.CarSustain,
			modWave: 0x00, carWave: patch.CarWave,
			feedback: 0x01,
			flags: 0x01, noteNumber: noteNumber);

	private static Op2Patch CreateModulatorOnlyPatch(CmfInstrument patch, byte noteNumber) =>
		Op2Patch.FromFullOplRegisters(
			modChar: 0x00, carChar: patch.ModChar,
			modScale: 0x3F, carScale: patch.ModScale,
			modAttack: 0x00, carAttack: patch.ModAttack,
			modSustain: 0xFF, carSustain: patch.ModSustain,
			modWave: 0x00, carWave: patch.ModWave,
			feedback: 0x01,
			flags: 0x01, noteNumber: noteNumber);

	private static void WriteJson(string path, object value)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
		File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions
		{
			WriteIndented = true
		}));
	}

	private sealed record DrumMapping(
		string Name,
		int[] MidiNotes,
		int Op2NoteNumber,
		string ApproximationNote,
		Op2Patch Patch);
}

internal sealed class CmfSong
{
	private CmfSong(
		string path,
		string title,
		bool rhythmModeEnabled,
		List<CmfInstrument> instruments,
		List<CmfRhythmUsage> rhythmUsages)
	{
		Path = path;
		Title = title;
		RhythmModeEnabled = rhythmModeEnabled;
		Instruments = instruments;
		_rhythmUsages = rhythmUsages;
	}

	private readonly List<CmfRhythmUsage> _rhythmUsages;

	public string Path { get; }
	public string Title { get; }
	public bool RhythmModeEnabled { get; }
	public List<CmfInstrument> Instruments { get; }

	public static CmfSong Load(string path)
	{
		byte[] data = File.ReadAllBytes(path);
		if (Encoding.ASCII.GetString(data, 0, 4) != "CTMF")
			throw new InvalidDataException("Input is not a CTMF/CMF file.");

		ushort instrumentOffset = ReadUInt16(data, 6);
		ushort musicOffset = ReadUInt16(data, 8);
		ushort titleOffset = ReadUInt16(data, 12);
		ushort instrumentCount = ReadUInt16(data, 36);
		List<CmfInstrument> instruments = [];
		for (int i = 0; i < instrumentCount; i++)
		{
			int offset = instrumentOffset + (i * 16);
			instruments.Add(new CmfInstrument(
				i,
				data[offset + 0], data[offset + 1], data[offset + 2], data[offset + 3],
				data[offset + 4], data[offset + 5], data[offset + 6], data[offset + 7],
				data[offset + 8], data[offset + 9], data[offset + 10]));
		}

		string title = ReadCString(data, titleOffset);
		CmfParseResult parseResult = ParseMusic(data, musicOffset);
		List<CmfRhythmUsage> usages = parseResult.RhythmUsages
			.Select(usage => usage with
			{
				Patch = instruments[usage.Program]
			})
			.ToList();
		return new CmfSong(System.IO.Path.GetFullPath(path), title, parseResult.RhythmModeEnabled, instruments, usages);
	}

	public IReadOnlyList<CmfRhythmUsage> GetRhythmUsages() => _rhythmUsages;

	private static CmfParseResult ParseMusic(byte[] data, int musicOffset)
	{
		int position = musicOffset;
		byte? runningStatus = null;
		int tick = 0;
		bool rhythmModeEnabled = false;
		Dictionary<int, int?> programs = new();
		Dictionary<int, RhythmAccumulator> usage = new();

		while (position < data.Length)
		{
			tick += ReadVariableLength(data, ref position);
			byte statusByte = data[position];
			byte status;
			if ((statusByte & 0x80) != 0)
			{
				status = statusByte;
				runningStatus = status;
				position++;
			}
			else if (runningStatus.HasValue)
			{
				status = runningStatus.Value;
			}
			else
			{
				throw new InvalidDataException("CMF stream used running status before a status byte appeared.");
			}

			if (status == 0xFF)
			{
				byte metaType = data[position++];
				int length = ReadVariableLength(data, ref position);
				if (metaType == 0x2F)
					break;
				position += length;
				continue;
			}

			if (status is 0xF0 or 0xF7)
			{
				int length = ReadVariableLength(data, ref position);
				position += length;
				continue;
			}

			int eventType = status >> 4;
			int channel = status & 0x0F;
			switch (eventType)
			{
				case 0x8:
				case 0xA:
				case 0xE:
					position += 2;
					break;
				case 0x9:
				{
					byte note = data[position++];
					byte velocity = data[position++];
					if (velocity == 0 || channel is < 11 or > 15)
						break;

					if (!programs.TryGetValue(channel, out int? program) || program is null)
						throw new InvalidDataException($"Rhythm note on channel {channel} appeared before a program change.");

					if (!usage.TryGetValue(channel, out RhythmAccumulator? accumulator))
					{
						accumulator = new RhythmAccumulator(channel, program.Value, note);
						usage[channel] = accumulator;
					}
					else
					{
						accumulator.Program = RequireConsistent(accumulator.Program, program.Value, "program", channel);
						accumulator.Note = RequireConsistent(accumulator.Note, note, "note", channel);
					}

					accumulator.NoteOnCount++;
					accumulator.Velocities.Add(velocity);
					break;
				}
				case 0xB:
				{
					byte controller = data[position++];
					byte value = data[position++];
					if (controller == 0x67)
						rhythmModeEnabled = value != 0;
					break;
				}
				case 0xC:
					programs[channel] = data[position++];
					break;
				case 0xD:
					position++;
					break;
				default:
					throw new InvalidDataException($"Unsupported CMF event type 0x{eventType:X}.");
			}
		}

		List<CmfRhythmUsage> result = usage
			.OrderBy(kvp => kvp.Key)
			.Select(kvp => kvp.Value.ToUsage())
			.ToList();
		return new CmfParseResult(rhythmModeEnabled, result);
	}

	private static int RequireConsistent(int previous, int current, string field, int channel)
	{
		if (previous != current)
			throw new InvalidDataException($"CMF rhythm channel {channel} changed {field} from {previous} to {current}; exporter expects one patch per rhythm voice.");
		return previous;
	}

	private static ushort ReadUInt16(byte[] data, int offset) =>
		(ushort)(data[offset] | (data[offset + 1] << 8));

	private static string ReadCString(byte[] data, int offset)
	{
		if (offset <= 0 || offset >= data.Length)
			return string.Empty;

		int end = offset;
		while (end < data.Length && data[end] != 0)
			end++;
		return Encoding.ASCII.GetString(data, offset, end - offset);
	}

	private static int ReadVariableLength(byte[] data, ref int position)
	{
		int value = 0;
		while (true)
		{
			byte current = data[position++];
			value = (value << 7) | (current & 0x7F);
			if ((current & 0x80) == 0)
				return value;
		}
	}

	private sealed record CmfParseResult(bool RhythmModeEnabled, List<CmfRhythmUsage> RhythmUsages);

	private sealed class RhythmAccumulator
	{
		public RhythmAccumulator(int channel, int program, int note)
		{
			Channel = channel;
			Program = program;
			Note = note;
		}

		public int Channel { get; }
		public int Program { get; set; }
		public int Note { get; set; }
		public int NoteOnCount { get; set; }
		public HashSet<int> Velocities { get; } = [];

		public CmfRhythmUsage ToUsage() =>
			new(Channel, Program, Note, NoteOnCount, [.. Velocities.OrderBy(v => v)]);
	}
}

internal sealed record CmfRhythmUsage(
	int Channel,
	int Program,
	int Note,
	int NoteOnCount,
	int[] Velocities)
{
	public CmfInstrument Patch { get; init; } = null!;
}

internal sealed record CmfInstrument(
	int Index,
	byte ModChar,
	byte CarChar,
	byte ModScale,
	byte CarScale,
	byte ModAttack,
	byte CarAttack,
	byte ModSustain,
	byte CarSustain,
	byte ModWave,
	byte CarWave,
	byte Feedback)
{
	public object ToReportObject() => new
	{
		Index,
		Bytes = string.Join(" ", new[]
		{
			ModChar, CarChar, ModScale, CarScale, ModAttack, CarAttack,
			ModSustain, CarSustain, ModWave, CarWave, Feedback
		}.Select(value => value.ToString("X2"))),
		Modulator = new
		{
			ModChar,
			ModScale,
			ModAttack,
			ModSustain,
			ModWave
		},
		Carrier = new
		{
			CarChar,
			CarScale,
			CarAttack,
			CarSustain,
			CarWave
		},
		Feedback
	};
}
