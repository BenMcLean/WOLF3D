namespace BenMcLean.Wolf3D.MusicTool;

internal static class Op2ProgramInspector
{
	public static IReadOnlyList<string> BuildWarnings(MidiProgramUsage midiPrograms, string op2Path)
	{
		Op2Bank bank = Op2Bank.Load(op2Path);
		List<string> warnings = [];

		foreach (int program in midiPrograms.Programs.OrderBy(program => program))
		{
			int bankIndex = program - 1;
			if (bankIndex < 0 || bankIndex >= Op2Bank.MelodicCount)
			{
				warnings.Add($"MIDI uses out-of-range GM program {program:D3}, which cannot map to a melodic OP2 slot.");
				continue;
			}

			if (!bank.Patches[bankIndex].IsSilent())
				continue;

			string channels = midiPrograms.ProgramChannels.TryGetValue(program, out IReadOnlyList<int>? usedChannels)
				? string.Join(", ", usedChannels)
				: "unknown";
			warnings.Add($"OP2 bank has no melodic patch for GM program {program:D3} (used on MIDI channels {channels}).");
		}

		return warnings;
	}
}
