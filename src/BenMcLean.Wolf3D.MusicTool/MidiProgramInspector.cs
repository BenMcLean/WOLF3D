using BenMcLean.Wolf3D.Assets.Sound;

namespace BenMcLean.Wolf3D.MusicTool;

internal static class MidiProgramInspector
{
	public static MidiProgramUsage Inspect(string midiPath)
	{
		using FileStream stream = File.OpenRead(midiPath);
		Midi midi = Midi.Parse(stream);

		byte[] channelPrograms = new byte[16];
		HashSet<int> usedPrograms = [];
		Dictionary<int, HashSet<int>> programChannels = [];

		foreach (Midi.MidiEvent midiEvent in midi.Events)
		{
			switch (midiEvent)
			{
				case Midi.ProgramChangeEvent programChange:
					channelPrograms[programChange.Channel] = programChange.Program;
					break;
				case Midi.NoteOnEvent noteOn when noteOn.Channel != 9 && noteOn.Velocity > 0:
				{
					int program = channelPrograms[noteOn.Channel] + 1;
					usedPrograms.Add(program);
					if (!programChannels.TryGetValue(program, out HashSet<int>? channels))
					{
						channels = [];
						programChannels[program] = channels;
					}

					channels.Add(noteOn.Channel + 1);
					break;
				}
			}
		}

		return new MidiProgramUsage(
			[.. usedPrograms.OrderBy(program => program)],
			programChannels.ToDictionary(
				kvp => kvp.Key,
				kvp => (IReadOnlyList<int>)[.. kvp.Value.OrderBy(channel => channel)]));
	}

	public static IReadOnlyList<string> BuildWarnings(MidiProgramUsage actual, IReadOnlyList<int> expectedPrograms)
	{
		HashSet<int> actualSet = [.. actual.Programs];
		HashSet<int> expectedSet = [.. expectedPrograms];
		List<string> warnings = [];

		int[] missing = [.. expectedSet.Except(actualSet).OrderBy(program => program)];
		int[] unexpected = [.. actualSet.Except(expectedSet).OrderBy(program => program)];

		if (missing.Length > 0)
			warnings.Add($"MIDI is missing expected GM programs: {string.Join(", ", missing.Select(program => program.ToString("D3")))}");
		if (unexpected.Length > 0)
			warnings.Add($"MIDI uses unexpected GM programs: {string.Join(", ", unexpected.Select(program => program.ToString("D3")))}");

		return warnings;
	}
}

internal sealed record MidiProgramUsage(
	IReadOnlyList<int> Programs,
	IReadOnlyDictionary<int, IReadOnlyList<int>> ProgramChannels);
