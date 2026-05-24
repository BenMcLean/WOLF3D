using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BenMcLean.Wolf3D.MusicTool;

internal static class WolfMidiSongAssetExporter
{
	public static void Export(
		string gameXml,
		string songName,
		string instPath,
		string patchNamesPath,
		string outDir,
		string? percussionOp2Path,
		string imfCreatorRepoPath,
		string imfCreatorRepoUrl)
	{
		string mappingJson = Path.Combine(outDir, $"{songName}.wolfmidi.json");
		WolfMidiMapper.ExportSongMapping(gameXml, songName, instPath, mappingJson);

		WolfMidiSongMappingReport report = JsonSerializer.Deserialize<WolfMidiSongMappingReport>(
			File.ReadAllText(mappingJson),
			new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true
			}) ?? throw new InvalidOperationException("Failed to read back generated wolfmidi mapping JSON.");

		Dictionary<int, string> gmPatchNames = LoadPatchNames(patchNamesPath);
		Op2Bank bank = Op2Bank.CreateSilent();

		if (!string.IsNullOrWhiteSpace(percussionOp2Path))
		{
			Op2Bank percussionBank = Op2Bank.Load(percussionOp2Path);
			for (int i = Op2Bank.MelodicCount; i < Op2Bank.TotalCount; i++)
			{
				bank.Patches[i] = percussionBank.Patches[i];
				bank.Names[i] = percussionBank.Names[i];
			}
		}

		List<FlInstrumentRow> instrumentRows = [];
		foreach (WolfMidiSongChannel channel in report.Channels.OrderBy(channel => channel.AdlibChannelOneBased))
		{
			foreach (WolfMidiSongChannelPatch patch in channel.Patches.OrderBy(patch => patch.ChannelPatchIndex))
			{
				if (patch.WolfMidiMatch is null)
					continue;

				int? gmProgram = ResolveSongProgram(patch.WolfMidiMatch);
				if (gmProgram is null)
					continue;

				int bankIndex = gmProgram.Value - 1;
				Op2Patch op2Patch = Op2Patch.FromFullOplRegisters(
					patch.OriginalRegisters.ModChar,
					patch.OriginalRegisters.CarChar,
					patch.OriginalRegisters.ModScale,
					patch.OriginalRegisters.CarScale,
					patch.OriginalRegisters.ModAttack,
					patch.OriginalRegisters.CarAttack,
					patch.OriginalRegisters.ModSustain,
					patch.OriginalRegisters.CarSustain,
					patch.OriginalRegisters.ModWave,
					patch.OriginalRegisters.CarWave,
					patch.OriginalRegisters.Feedback,
					noteOffset: (short)(patch.WolfMidiMatch.Transpose ?? 0));

				if (bank.Patches[bankIndex].IsSilent())
				{
					bank.Patches[bankIndex] = op2Patch;
					bank.Names[bankIndex] = BuildBankName(songName, channel, patch, gmProgram.Value, gmPatchNames);
				}

				instrumentRows.Add(new FlInstrumentRow
				{
					AdlibChannel = channel.AdlibChannelOneBased,
					GmProgram = gmProgram.Value,
					GmPatchName = gmPatchNames.GetValueOrDefault(gmProgram.Value, $"Program {gmProgram.Value}"),
					WolfMidiPatch = patch.WolfMidiMatch.Patch,
					Transpose = patch.WolfMidiMatch.Transpose,
					MinTime = patch.WolfMidiMatch.MinTime,
					Comment = patch.WolfMidiMatch.Comment
				});
			}
		}

		string op2Path = Path.Combine(outDir, $"{songName}.wolfmidi.op2");
		string flListPath = Path.Combine(outDir, $"{songName}.fl-instruments.txt");
		bank.Save(op2Path);
		WriteFlList(flListPath, songName, instrumentRows);
		WriteCombinedJson(
			mappingJson,
			gameXml,
			songName,
			instPath,
			patchNamesPath,
			op2Path,
			percussionOp2Path,
			imfCreatorRepoPath,
			imfCreatorRepoUrl,
			instrumentRows);

		string legacyAssetsJsonPath = Path.Combine(outDir, $"{songName}.wolfmidi.assets.json");
		if (File.Exists(legacyAssetsJsonPath))
			File.Delete(legacyAssetsJsonPath);
	}

	private static int? ResolveSongProgram(WolfMidiMatch match)
	{
		if (!string.IsNullOrWhiteSpace(match.ReferencedProgramForSong) &&
			int.TryParse(match.ReferencedProgramForSong.TrimStart('d'), out int referencedProgram))
			return referencedProgram;
		return match.Patch;
	}

	private static string BuildBankName(
		string songName,
		WolfMidiSongChannel channel,
		WolfMidiSongChannelPatch patch,
		int gmProgram,
		Dictionary<int, string> gmPatchNames)
	{
		string patchName = gmPatchNames.GetValueOrDefault(gmProgram, $"Program {gmProgram}");
		return $"{songName} Ch{channel.AdlibChannelOneBased:D2} {patchName} [{gmProgram:D3}]";
	}

	private static Dictionary<int, string> LoadPatchNames(string patchNamesPath)
	{
		Dictionary<int, string> result = [];
		foreach (string rawLine in File.ReadLines(patchNamesPath))
		{
			string line = rawLine.Trim();
			if (line.Length == 0 || line.StartsWith('#'))
				continue;
			int equalsIndex = line.IndexOf('=');
			if (equalsIndex <= 0)
				continue;
			if (!int.TryParse(line[..equalsIndex], out int program))
				continue;
			result[program] = line[(equalsIndex + 1)..].Trim();
		}

		return result;
	}

	private static void WriteFlList(string path, string songName, List<FlInstrumentRow> rows)
	{
		StringBuilder builder = new();
		builder.AppendLine($"{songName} FL Studio General MIDI Setup");
		builder.AppendLine();
		builder.AppendLine("Configure one MIDI Out per line below using the listed Program number.");
		builder.AppendLine("These program numbers match the generated OP2 bank slots one-to-one.");
		builder.AppendLine();

		var displayRows = rows
			.GroupBy(row => new { row.GmProgram, row.GmPatchName, row.WolfMidiPatch, row.Transpose, row.MinTime, row.Comment })
			.OrderBy(group => group.Key.GmProgram)
			.Select(group => new
			{
				group.Key.GmProgram,
				group.Key.GmPatchName,
				group.Key.WolfMidiPatch,
				group.Key.Transpose,
				group.Key.MinTime,
				group.Key.Comment,
				Channels = string.Join(", ", group.Select(row => row.AdlibChannel).Distinct().OrderBy(channel => channel))
			});

		foreach (var row in displayRows)
		{
			builder.AppendLine($"Program {row.GmProgram:D3}: {row.GmPatchName}");
			builder.AppendLine($"  AdLib channels: {row.Channels}");
			builder.AppendLine($"  wolfmidi base patch: {(row.WolfMidiPatch?.ToString() ?? "n/a")}");
			if (row.Transpose is not null)
				builder.AppendLine($"  transpose: {row.Transpose:+#;-#;0}");
			if (row.MinTime is not null)
				builder.AppendLine($"  min note time: {row.MinTime}");
			builder.AppendLine($"  note: {row.Comment}");
			builder.AppendLine();
		}

		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
		File.WriteAllText(path, builder.ToString());
	}

	private static void WriteCombinedJson(
		string path,
		string gameXml,
		string songName,
		string instPath,
		string patchNamesPath,
		string op2Path,
		string? percussionOp2Path,
		string imfCreatorRepoPath,
		string imfCreatorRepoUrl,
		List<FlInstrumentRow> rows)
	{
		JsonObject root = JsonNode.Parse(File.ReadAllText(path))?.AsObject()
			?? throw new InvalidOperationException("Failed to re-open generated wolfmidi JSON.");

		root["GameXml"] = PortablePath.ToStoredPath(path, gameXml);
		root["Song"] = songName;
		root["InstrumentMap"] = PortablePath.ToStoredPath(path, instPath);
		root["PatchNames"] = PortablePath.ToStoredPath(path, patchNamesPath);
		root["Op2Bank"] = PortablePath.ToStoredPath(path, op2Path);
		root["PercussionOverlay"] = PortablePath.ToStoredPathOrNull(path, percussionOp2Path);
		root["ImfCreatorRepo"] = PortablePath.ToStoredPath(path, imfCreatorRepoPath);
		root["ImfCreatorRepoUrl"] = imfCreatorRepoUrl;
		root["FlStudioPrograms"] = JsonSerializer.SerializeToNode(
			rows
				.GroupBy(row => row.GmProgram)
				.OrderBy(group => group.Key)
				.Select(group => new
				{
					Program = group.Key,
					PatchName = group.First().GmPatchName,
					AdlibChannels = group.Select(row => row.AdlibChannel).Distinct().OrderBy(channel => channel).ToArray(),
					Transpose = group.First().Transpose,
					MinTime = group.First().MinTime,
					WolfMidiPatch = group.First().WolfMidiPatch,
					Comment = group.First().Comment
				}));

		File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions
		{
			WriteIndented = true
		}));
	}

	private sealed class FlInstrumentRow
	{
		public required int AdlibChannel { get; init; }
		public required int GmProgram { get; init; }
		public required string GmPatchName { get; init; }
		public int? WolfMidiPatch { get; init; }
		public int? Transpose { get; init; }
		public int? MinTime { get; init; }
		public required string Comment { get; init; }
	}

	private sealed class WolfMidiSongMappingReport
	{
		public required List<WolfMidiSongChannel> Channels { get; init; }
	}

	private sealed class WolfMidiSongChannel
	{
		public required int AdlibChannelOneBased { get; init; }
		public required List<WolfMidiSongChannelPatch> Patches { get; init; }
	}

	private sealed class WolfMidiSongChannelPatch
	{
		public required int ChannelPatchIndex { get; init; }
		public required WolfPatchRegisters OriginalRegisters { get; init; }
		public required WolfMidiMatch? WolfMidiMatch { get; init; }
	}

	private sealed class WolfMidiMatch
	{
		public int? Patch { get; init; }
		public int? Transpose { get; init; }
		public int? MinTime { get; init; }
		public required string? ReferencedProgramForSong { get; init; }
		public required string Comment { get; init; }
	}
}
