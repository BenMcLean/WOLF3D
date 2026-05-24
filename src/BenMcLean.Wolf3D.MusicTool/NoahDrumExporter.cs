using System.Text.Json;
using BenMcLean.Wolf3D.Assets;
using BenMcLean.Wolf3D.Assets.Sound;
using NScumm.Audio.OPL.Woody;
using NScumm.Core.Audio.OPL;

namespace BenMcLean.Wolf3D.MusicTool;

internal static class NoahDrumExporter
{
	private const int SampleRate = 44100;

	public static void Export(string gameXml, string outOp2, string outJson, string? previewDir)
	{
		AssetManager assets = MusicToolAssetLoader.Load(gameXml);

		Dictionary<string, Dictionary<int, int>> songUsage = BuildSongUsage(assets.AudioT.Songs);
		NoahDrumKit kit = NoahDrumKit.Create();
		Op2Bank bank = BuildApproximationBank(kit);
		bank.Save(outOp2);

		WriteJson(outJson, new
		{
			GameXml = PortablePath.ToStoredPath(outJson, gameXml),
			ExactSource = "Super 3-D Noah's Ark MIDI-to-AdLib rhythm-mode playback",
			Warning = "The WAV previews are exact to the Noah rhythm-mode code path. The OP2 bank is a best-effort export for IMFCreator-style tooling because OP2 is not a perfect container for Noah's hardcoded rhythm operators.",
			DrumMappings = kit.DrumMappings.Select(mapping => new
			{
				mapping.Name,
				mapping.MidiNotes,
				mapping.ExactOrigin,
				mapping.ApproximationNote
			}),
			SongUsage = songUsage
		});

		if (string.IsNullOrWhiteSpace(previewDir))
			return;

		Directory.CreateDirectory(previewDir);
		foreach (NoahDrumMapping mapping in kit.DrumMappings)
		{
			short[] samples = NoahDrumPreviewRenderer.RenderBoostedPreview(mapping.Drum, mapping.Approximation);
			WavWriter.WriteMono16(Path.Combine(previewDir, $"{mapping.FileSafeName}.wav"), SampleRate, samples);
		}

		WavWriter.WriteMono16(
			Path.Combine(previewDir, "noah-kit-demo.wav"),
			SampleRate,
			AudioPostProcessor.NormalizePeak(
				NoahDrumPreviewRenderer.RenderDemoTrack(kit.DrumMappings.Select(m => m.Drum).ToArray()),
				30000));
	}

	private static Dictionary<string, Dictionary<int, int>> BuildSongUsage(Dictionary<string, AudioT.Music> songs)
	{
		Dictionary<string, Dictionary<int, int>> usage = new(StringComparer.OrdinalIgnoreCase);
		foreach ((string songName, AudioT.Music song) in songs.OrderBy(kvp => kvp.Key))
		{
			if (song.Midi is null)
				continue;
			Dictionary<int, int> counts = [];
			foreach (Midi.MidiEvent midiEvent in song.Midi.Events)
				if (midiEvent is Midi.NoteOnEvent noteOn && noteOn.Channel == 9 && noteOn.Velocity > 0)
					counts[noteOn.Note] = counts.GetValueOrDefault(noteOn.Note) + 1;
			if (counts.Count > 0)
				usage[songName] = counts.OrderBy(kvp => kvp.Key).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
		}

		return usage;
	}

	private static Op2Bank BuildApproximationBank(NoahDrumKit kit)
	{
		Op2Bank bank = Op2Bank.CreateSilent();
		foreach (NoahDrumMapping mapping in kit.DrumMappings)
		{
			foreach (int midiNote in mapping.MidiNotes)
			{
				int percussionIndex = Op2Bank.MelodicCount + (midiNote - 35);
				bank.Patches[percussionIndex] = mapping.Approximation;
				bank.Names[percussionIndex] = mapping.Name;
			}
		}

		return bank;
	}

	private static void WriteJson(string path, object value)
	{
		File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions
		{
			WriteIndented = true
		}));
	}
}

internal sealed class NoahDrumKit
{
	private NoahDrumKit(NoahDrumMapping[] drumMappings) => DrumMappings = drumMappings;

	public NoahDrumMapping[] DrumMappings { get; }

	public static NoahDrumKit Create()
	{
		Op2Patch bassApprox = Op2Patch.FromFullOplRegisters(
			modChar: 0x10, carChar: 0x00, modScale: 0x00, carScale: 0x00,
			modAttack: 0xD8, carAttack: 0x87, modSustain: 0x4A, carSustain: 0x3C,
			modWave: 0x00, carWave: 0x00, feedback: 0x00,
			flags: 0x01, noteNumber: 36);

		Op2Patch snareApprox = Op2Patch.FromFullOplRegisters(
			modChar: 0x00, carChar: 0x00, modScale: 0x3F, carScale: 0x00,
			modAttack: 0x00, carAttack: 0xF8, modSustain: 0xFF, carSustain: 0xB5,
			modWave: 0x00, carWave: 0x00, feedback: 0x00,
			flags: 0x01, noteNumber: 38);

		Op2Patch hiHatApprox = Op2Patch.FromFullOplRegisters(
			modChar: 0x00, carChar: 0x00, modScale: 0x11, carScale: 0x3F,
			modAttack: 0xFA, carAttack: 0x00, modSustain: 0xB5, carSustain: 0xFF,
			modWave: 0x00, carWave: 0x00, feedback: 0x00,
			flags: 0x01, noteNumber: 42);

		Op2Patch tomApprox = Op2Patch.FromFullOplRegisters(
			modChar: 0x00, carChar: 0x21, modScale: 0x3F, carScale: 0x04,
			modAttack: 0x00, carAttack: 0xF2, modSustain: 0xFF, carSustain: 0x38,
			modWave: 0x00, carWave: 0x00, feedback: 0x00,
			flags: 0x01, noteNumber: 40);

		return new NoahDrumKit(
		[
			new NoahDrumMapping(
				NoahDrum.Bass,
				"Noah Bass Drum",
				"noah-bass-drum",
				[35, 36],
				"Bass drum is exact two-operator channel 6 rhythm voice from instrument[9].",
				"OP2 export is structurally faithful for the bass drum because Noah uses a full two-operator rhythm voice here.",
				bassApprox),
			new NoahDrumMapping(
				NoahDrum.Snare,
				"Noah Snare",
				"noah-snare",
				[38],
				"Snare uses the carrier-side rhythm operator configured from instrument[11].",
				"OP2 export is an approximation because Noah's snare is a rhythm-mode single-operator voice, not a standalone two-operator patch.",
				snareApprox),
			new NoahDrumMapping(
				NoahDrum.Tom,
				"Noah Tom",
				"noah-tom",
				[40],
				"Tom uses the modulator-side rhythm operator configured from instrument[12].",
				"OP2 export is an approximation because Noah's tom is a rhythm-mode single-operator voice, not a standalone two-operator patch.",
				tomApprox),
			new NoahDrumMapping(
				NoahDrum.HiHat,
				"Noah Hi-Hat",
				"noah-hi-hat",
				[42],
				"Hi-hat uses the modulator-side rhythm operator configured from instrument[10].",
				"OP2 export is an approximation because Noah's hi-hat is a rhythm-mode single-operator voice, not a standalone two-operator patch.",
				hiHatApprox)
		]);
	}
}

internal sealed record NoahDrumMapping(
	NoahDrum Drum,
	string Name,
	string FileSafeName,
	int[] MidiNotes,
	string ExactOrigin,
	string ApproximationNote,
	Op2Patch ApproximationPatch)
{
	public Op2Patch Approximation => ApproximationPatch;
}

internal enum NoahDrum
{
	Bass,
	Snare,
	HiHat,
	Tom
}

internal static class NoahDrumPreviewRenderer
{
	public static short[] RenderBoostedPreview(NoahDrum drum, Op2Patch approximation)
	{
		short[] exact = RenderExactPreview(drum);
		if (AudioPostProcessor.GetPeak(exact) == 0)
			exact = drum == NoahDrum.Tom
				? RenderSyntheticTomPreview()
				: RenderApproximationPreview(approximation);

		return AudioPostProcessor.NormalizePeak(exact, 30000);
	}

	public static short[] RenderExactPreview(NoahDrum drum)
	{
		using OplRenderer renderer = new(44100, new WoodyEmulatorOpl(OplType.Opl2));
		renderer.InitializeNoahKit();
		renderer.RenderMilliseconds(40);
		renderer.Trigger(drum, on: true);
		renderer.RenderMilliseconds(160);
		renderer.Trigger(drum, on: false);
		renderer.RenderMilliseconds(500);
		return renderer.ToArray();
	}

	public static short[] RenderApproximationPreview(Op2Patch patch)
	{
		using OplRenderer renderer = new(44100, new WoodyEmulatorOpl(OplType.Opl2));
		renderer.InitializeMelodicPatch(0, patch);
		renderer.RenderMilliseconds(40);
		renderer.TriggerMelodic(0, patch.NoteNumber == 0 ? 60 : patch.NoteNumber, on: true);
		renderer.RenderMilliseconds(160);
		renderer.TriggerMelodic(0, patch.NoteNumber == 0 ? 60 : patch.NoteNumber, on: false);
		renderer.RenderMilliseconds(500);
		return renderer.ToArray();
	}

	public static short[] RenderSyntheticTomPreview()
	{
		using OplRenderer renderer = new(44100, new WoodyEmulatorOpl(OplType.Opl2));
		renderer.InitializeNoahKit();
		renderer.ApplySyntheticTomOperator();
		renderer.RenderMilliseconds(40);
		renderer.Trigger(NoahDrum.Tom, on: true);
		renderer.RenderMilliseconds(160);
		renderer.Trigger(NoahDrum.Tom, on: false);
		renderer.RenderMilliseconds(500);
		return renderer.ToArray();
	}

	public static short[] RenderDemoTrack(NoahDrum[] drums)
	{
		using OplRenderer renderer = new(44100, new WoodyEmulatorOpl(OplType.Opl2));
		renderer.InitializeNoahKit();
		renderer.RenderMilliseconds(80);
		foreach (NoahDrum drum in drums)
		{
			renderer.Trigger(drum, on: true);
			renderer.RenderMilliseconds(120);
			renderer.Trigger(drum, on: false);
			renderer.RenderMilliseconds(220);
		}
		return renderer.ToArray();
	}
}

internal sealed class OplRenderer : IDisposable
{
	private static readonly int[] OperatorOffsets = [0, 1, 2, 8, 9, 10, 16, 17, 18];
	private static readonly int[] CarrierOffsets = [3, 4, 5, 11, 12, 13, 19, 20, 21];
	private readonly IOpl _opl;
	private readonly List<short> _samples = [];
	private byte _drumBits;
	private static readonly ushort[] NoteTable =
	[
		0x157, 0x16b, 0x181, 0x198, 0x1b0, 0x1ca, 0x1e5, 0x202,
		0x220, 0x241, 0x263, 0x287
	];

	public OplRenderer(int sampleRate, IOpl opl)
	{
		SampleRate = sampleRate;
		_opl = opl;
		_opl.Init(sampleRate);
	}

	public int SampleRate { get; }

	public void InitializeNoahKit()
	{
		_opl.WriteReg(1, 32);
		ApplyBassDrumInstrument();
		SetFixedPercussionPitch(6, 24);
		SetFixedPercussionPitch(7, 24);
		SetFixedPercussionPitch(8, 24);
		ApplyHiHatOperator();
		ApplyTomOperator();
		ApplySnareOperator();
		ApplyCymbalOperator();
		_drumBits = 0;
		_opl.WriteReg(0xBD, 0x20);
	}

	public void InitializeMelodicPatch(int channel, Op2Patch patch)
	{
		_opl.WriteReg(1, 32);
		WriteMelodicVoice(channel, patch.Voice1);
	}

	public void Trigger(NoahDrum drum, bool on)
	{
		byte bit = drum switch
		{
			NoahDrum.Bass => 0x10,
			NoahDrum.Snare => 0x08,
			NoahDrum.Tom => 0x04,
			NoahDrum.HiHat => 0x01,
			_ => 0
		};

		if (on)
			_drumBits |= bit;
		else
			_drumBits &= (byte)~bit;

		_opl.WriteReg(0xBD, 0x20 | _drumBits);
	}

	public void TriggerMelodic(int channel, int midiNote, bool on)
	{
		ushort fNumber = NoteTable[midiNote % 12];
		int octave = ((midiNote / 12) & 7) << 2;
		_opl.WriteReg(0xA0 + channel, fNumber & 0xFF);
		_opl.WriteReg(0xB0 + channel, octave + ((fNumber >> 8) & 3) | (on ? 0x20 : 0x00));
	}

	public void RenderMilliseconds(int milliseconds)
	{
		int samplesToRead = SampleRate * milliseconds / 1000;
		short[] buffer = new short[Math.Max(1, samplesToRead)];
		_opl.ReadBuffer(buffer, 0, buffer.Length);
		_samples.AddRange(buffer);
	}

	public short[] ToArray() => [.. _samples];

	public void Dispose()
	{
		if (_opl is IDisposable disposable)
			disposable.Dispose();
	}

	private void ApplyBassDrumInstrument()
	{
		WriteFullVoice(channel: 6, modChar: 0x10, carChar: 0x00, modScale: 0x00, carScale: 0x00,
			modAttack: 0xD8, carAttack: 0x87, modSustain: 0x4A, carSustain: 0x3C,
			modWave: 0x00, carWave: 0x00, feedback: 0x00);
	}

	private void ApplyHiHatOperator()
	{
		_opl.WriteReg(0x31, 0x00);
		_opl.WriteReg(0x51, 0x11);
		_opl.WriteReg(0x71, 0xFA);
		_opl.WriteReg(0x91, 0xB5);
		_opl.WriteReg(0xF1, 0x00);
		_opl.WriteReg(0xC7, 0x00);
	}

	private void ApplyTomOperator()
	{
		_opl.WriteReg(0x32, 0x15);
		_opl.WriteReg(0x52, 0x00);
		_opl.WriteReg(0x72, 0x00);
		_opl.WriteReg(0x92, 0x00);
		_opl.WriteReg(0xF2, 0x00);
	}

	public void ApplySyntheticTomOperator()
	{
		_opl.WriteReg(0x32, 0x21);
		_opl.WriteReg(0x52, 0x04);
		_opl.WriteReg(0x72, 0xF2);
		_opl.WriteReg(0x92, 0x38);
		_opl.WriteReg(0xF2, 0x00);
	}

	private void ApplySnareOperator()
	{
		_opl.WriteReg(0x34, 0x00);
		_opl.WriteReg(0x54, 0x00);
		_opl.WriteReg(0x74, 0xF8);
		_opl.WriteReg(0x94, 0xB5);
		_opl.WriteReg(0xF4, 0x00);
		_opl.WriteReg(0xC8, 0x00);
	}

	private void ApplyCymbalOperator()
	{
		_opl.WriteReg(0x35, 0x00);
		_opl.WriteReg(0x55, 0x11);
		_opl.WriteReg(0x75, 0xFA);
		_opl.WriteReg(0x95, 0xB5);
		_opl.WriteReg(0xF5, 0x00);
	}

	private void SetFixedPercussionPitch(int channel, int midiNote)
	{
		ushort fNumber = NoteTable[midiNote % 12];
		int octave = ((midiNote / 12) & 7) << 2;
		_opl.WriteReg(0xA0 + channel, fNumber & 0xFF);
		_opl.WriteReg(0xB0 + channel, octave + ((fNumber >> 8) & 3));
	}

	private void WriteFullVoice(
		int channel,
		byte modChar,
		byte carChar,
		byte modScale,
		byte carScale,
		byte modAttack,
		byte carAttack,
		byte modSustain,
		byte carSustain,
		byte modWave,
		byte carWave,
		byte feedback)
	{
		int modOffset = channel switch
		{
			6 => 16,
			7 => 17,
			8 => 18,
			_ => throw new ArgumentOutOfRangeException(nameof(channel))
		};
		int carOffset = modOffset + 3;

		_opl.WriteReg(0x20 + modOffset, modChar);
		_opl.WriteReg(0x20 + carOffset, carChar);
		_opl.WriteReg(0x40 + modOffset, modScale);
		_opl.WriteReg(0x40 + carOffset, carScale);
		_opl.WriteReg(0x60 + modOffset, modAttack);
		_opl.WriteReg(0x60 + carOffset, carAttack);
		_opl.WriteReg(0x80 + modOffset, modSustain);
		_opl.WriteReg(0x80 + carOffset, carSustain);
		_opl.WriteReg(0xE0 + modOffset, modWave);
		_opl.WriteReg(0xE0 + carOffset, carWave);
		_opl.WriteReg(0xC0 + channel, feedback);
	}

	private void WriteMelodicVoice(int channel, Op2Voice voice)
	{
		int modOffset = OperatorOffsets[channel];
		int carOffset = CarrierOffsets[channel];
		_opl.WriteReg(0x20 + modOffset, voice.ModChar);
		_opl.WriteReg(0x20 + carOffset, voice.CarChar);
		_opl.WriteReg(0x40 + modOffset, voice.ModScale | voice.ModLevel);
		_opl.WriteReg(0x40 + carOffset, voice.CarScale | voice.CarLevel);
		_opl.WriteReg(0x60 + modOffset, voice.ModAttack);
		_opl.WriteReg(0x60 + carOffset, voice.CarAttack);
		_opl.WriteReg(0x80 + modOffset, voice.ModSustain);
		_opl.WriteReg(0x80 + carOffset, voice.CarSustain);
		_opl.WriteReg(0xE0 + modOffset, voice.ModWave);
		_opl.WriteReg(0xE0 + carOffset, voice.CarWave);
		_opl.WriteReg(0xC0 + channel, voice.Feedback);
	}
}

internal static class AudioPostProcessor
{
	public static int GetPeak(short[] samples)
	{
		int peak = 0;
		foreach (short sample in samples)
		{
			int amplitude = Math.Abs((int)sample);
			if (amplitude > peak)
				peak = amplitude;
		}

		return peak;
	}

	public static short[] NormalizePeak(short[] samples, int targetPeak)
	{
		int peak = GetPeak(samples);
		if (peak == 0 || peak == targetPeak)
			return samples;

		double scale = (double)targetPeak / peak;
		short[] normalized = new short[samples.Length];
		for (int i = 0; i < samples.Length; i++)
		{
			int scaled = (int)Math.Round(samples[i] * scale);
			normalized[i] = (short)Math.Clamp(scaled, short.MinValue, short.MaxValue);
		}

		return normalized;
	}
}

internal static class WavWriter
{
	public static void WriteMono16(string path, int sampleRate, short[] samples)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
		using FileStream stream = File.Create(path);
		using BinaryWriter writer = new(stream);
		int dataLength = samples.Length * sizeof(short);
		writer.Write("RIFF"u8.ToArray());
		writer.Write(36 + dataLength);
		writer.Write("WAVE"u8.ToArray());
		writer.Write("fmt "u8.ToArray());
		writer.Write(16);
		writer.Write((short)1);
		writer.Write((short)1);
		writer.Write(sampleRate);
		writer.Write(sampleRate * sizeof(short));
		writer.Write((short)sizeof(short));
		writer.Write((short)16);
		writer.Write("data"u8.ToArray());
		writer.Write(dataLength);
		foreach (short sample in samples)
			writer.Write(sample);
	}
}
