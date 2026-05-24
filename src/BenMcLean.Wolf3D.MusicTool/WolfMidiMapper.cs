using System.Text.Json;
using System.Text.RegularExpressions;
using BenMcLean.Wolf3D.Assets;
using BenMcLean.Wolf3D.Assets.Sound;

namespace BenMcLean.Wolf3D.MusicTool;

internal static class WolfMidiMapper
{
	public static void ExportSongMapping(string gameXml, string songName, string instPath, string outJson)
	{
		AssetManager assets = MusicToolAssetLoader.Load(gameXml);
		if (!assets.AudioT.Songs.TryGetValue(songName, out AudioT.Music? song) || song.Imf is null)
			throw new InvalidOperationException($"Song '{songName}' was not found as IMF data in '{gameXml}'.");

		List<WolfMidiInstrumentDefinition> definitions = LoadDefinitions(instPath);
		List<MappedPatch> mappedPatches = BuildMappedPatches(songName, song.Imf, definitions);

		object manifest = new
		{
			GameXml = PortablePath.ToStoredPath(outJson, gameXml),
			Song = songName,
			InstrumentMap = PortablePath.ToStoredPath(outJson, instPath),
			TotalMappedPatches = mappedPatches.Count,
			Channels = mappedPatches
				.GroupBy(patch => patch.Channel)
				.OrderBy(group => group.Key)
				.Select(group => new
				{
					AdlibChannelZeroBased = group.Key,
					AdlibChannelOneBased = group.Key + 1,
					Patches = group.Select(patch => new
					{
						patch.ChannelPatchIndex,
						patch.FirstSeenAtEvent,
						patch.NoteOnsObserved,
						OriginalRegisters = patch.OriginalRegisters,
						WolfMidiMatch = patch.Definition is null
							? null
							: new
							{
								patch.Definition.Type,
								patch.Definition.Patch,
								patch.Definition.LowPatch,
								patch.Definition.LowNote,
								patch.Definition.Drum,
								patch.Definition.Transpose,
								patch.Definition.MinTime,
								patch.Definition.Kit,
								patch.Definition.Comment,
								patch.Definition.CommentReferences,
								ReferencedProgramForSong = patch.Definition.GetReferencedProgramForSong(songName),
								patch.Definition.IsManuallyAssigned,
								MentionsSong = patch.Definition.CommentMentionsSong(songName)
							}
					})
				}),
			UniqueTimbres = mappedPatches
				.GroupBy(
					patch => patch.NormalizedRegisters,
					patch => patch,
					WolfMidiNormalizedRegistersComparer.Instance)
				.Select(group =>
				{
					MappedPatch representative = group.First();
					return new
					{
						NormalizedRegisters = representative.NormalizedRegisters,
						ChannelsZeroBased = group.Select(p => p.Channel).Distinct().Order().ToArray(),
						ChannelsOneBased = group.Select(p => p.Channel + 1).Distinct().Order().ToArray(),
						ChannelPatchIndices = group.Select(p => $"{p.Channel}:{p.ChannelPatchIndex}").ToArray(),
						Match = representative.Definition is null
							? null
							: new
							{
								representative.Definition.Type,
								representative.Definition.Patch,
								representative.Definition.LowPatch,
								representative.Definition.LowNote,
								representative.Definition.Drum,
								representative.Definition.Transpose,
								representative.Definition.MinTime,
								representative.Definition.Kit,
								representative.Definition.Comment,
								representative.Definition.CommentReferences,
								ReferencedProgramForSong = representative.Definition.GetReferencedProgramForSong(songName),
								representative.Definition.IsManuallyAssigned,
								MentionsSong = representative.Definition.CommentMentionsSong(songName)
							}
					};
				})
		};

		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outJson))!);
		File.WriteAllText(outJson, JsonSerializer.Serialize(manifest, new JsonSerializerOptions
		{
			WriteIndented = true
		}));
	}

	private static List<MappedPatch> BuildMappedPatches(
		string songName,
		Imf[] imf,
		List<WolfMidiInstrumentDefinition> definitions)
	{
		byte[] state = new byte[256];
		Dictionary<(int Channel, WolfMidiNormalizedRegisters Registers), MappedPatch> patches = [];
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
			if (!keyOn || (rhythmMode && channel >= 6))
				continue;

			WolfPatchRegisters original = WolfOp2Extractor.ReadPatchFromState(state, channel);
			if (original.IsSilent)
				continue;

			WolfMidiNormalizedRegisters normalized = WolfMidiNormalizedRegisters.FromPatch(original);
			var key = (channel, normalized);
			if (!patches.TryGetValue(key, out MappedPatch? patch))
			{
				int channelPatchIndex = patches.Keys.Count(existing => existing.Channel == channel) + 1;
				patch = new MappedPatch
				{
					SongName = songName,
					Channel = channel,
					ChannelPatchIndex = channelPatchIndex,
					OriginalRegisters = original,
					NormalizedRegisters = normalized,
					FirstSeenAtEvent = eventIndex,
					Definition = definitions.FirstOrDefault(definition => definition.Type == "NO" &&
						WolfMidiNormalizedRegistersComparer.Instance.Equals(definition.Registers, normalized))
				};
				patches[key] = patch;
			}

			patch.NoteOnsObserved++;
		}

		return [.. patches.Values.OrderBy(patch => patch.FirstSeenAtEvent)];
	}

	private static List<WolfMidiInstrumentDefinition> LoadDefinitions(string instPath)
	{
		List<WolfMidiInstrumentDefinition> definitions = [];
		foreach (string rawLine in File.ReadLines(instPath))
		{
			string line = rawLine.Trim();
			if (line.Length == 0 || line.StartsWith('#'))
				continue;

			int colonIndex = line.IndexOf(':');
			if (colonIndex < 0)
				continue;

			string left = line[..colonIndex].Trim();
			if (!left.StartsWith("NO ", StringComparison.Ordinal))
				continue;

			string right = line[(colonIndex + 1)..].Trim();
			int commentIndex = right.IndexOf('#');
			string optionsText = commentIndex >= 0 ? right[..commentIndex].Trim() : right;
			string comment = commentIndex >= 0 ? right[(commentIndex + 1)..].Trim() : string.Empty;

			string[] leftParts = left.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
			string[] registerParts = leftParts[1].Split('/');
			if (registerParts.Length != 6)
				continue;

			string[] reg20 = registerParts[0].Split('-');
			string[] reg40 = registerParts[1].Split('-');
			string[] reg60 = registerParts[2].Split('-');
			string[] reg80 = registerParts[3].Split('-');
			string[] regE0 = registerParts[5].Split('-');

			WolfMidiNormalizedRegisters registers = new()
			{
				ModChar = Convert.ToByte(reg20[0], 16),
				CarChar = Convert.ToByte(reg20[1], 16),
				ModScale = Convert.ToByte(reg40[0], 16),
				CarScale = Convert.ToByte(reg40[1], 16),
				ModAttack = Convert.ToByte(reg60[0], 16),
				CarAttack = Convert.ToByte(reg60[1], 16),
				ModSustain = Convert.ToByte(reg80[0], 16),
				CarSustain = Convert.ToByte(reg80[1], 16),
				Feedback = Convert.ToByte(registerParts[4], 16),
				ModWave = Convert.ToByte(regE0[0], 16),
				CarWave = Convert.ToByte(regE0[1], 16)
			};

			WolfMidiInstrumentDefinition definition = new()
			{
				Type = leftParts[0],
				Registers = registers,
				Comment = comment,
				CommentReferences = WolfMidiInstrumentDefinition.ParseCommentReferences(comment)
			};

			foreach (string option in optionsText.Split(' ', StringSplitOptions.RemoveEmptyEntries))
			{
				if (option.StartsWith("patch=", StringComparison.Ordinal))
					definition.Patch = int.Parse(option["patch=".Length..]);
				else if (option.StartsWith("lowpatch=", StringComparison.Ordinal))
					definition.LowPatch = int.Parse(option["lowpatch=".Length..]);
				else if (option.StartsWith("lownote=", StringComparison.Ordinal))
					definition.LowNote = int.Parse(option["lownote=".Length..]);
				else if (option.StartsWith("drum=", StringComparison.Ordinal))
					definition.Drum = int.Parse(option["drum=".Length..]);
				else if (option.StartsWith("transpose=", StringComparison.Ordinal))
					definition.Transpose = int.Parse(option["transpose=".Length..]);
				else if (option.StartsWith("mintime=", StringComparison.Ordinal))
					definition.MinTime = int.Parse(option["mintime=".Length..]);
				else if (option.StartsWith("kit=", StringComparison.Ordinal))
					definition.Kit = int.Parse(option["kit=".Length..]);
			}

			definitions.Add(definition);
		}

		return definitions;
	}

	private sealed class MappedPatch
	{
		public required string SongName { get; init; }
		public required int Channel { get; init; }
		public required int ChannelPatchIndex { get; init; }
		public required WolfPatchRegisters OriginalRegisters { get; init; }
		public required WolfMidiNormalizedRegisters NormalizedRegisters { get; init; }
		public required int FirstSeenAtEvent { get; init; }
		public required WolfMidiInstrumentDefinition? Definition { get; init; }
		public int NoteOnsObserved { get; set; }
	}
}

internal sealed class WolfMidiInstrumentDefinition
{
	private static readonly Regex CommentReferenceRegex = new(@"(?<prefix>d?)(?<program>\d+)(?:/\d+)? \((?<song>[A-Z0-9_]+)\)", RegexOptions.Compiled);

	public required string Type { get; init; }
	public required WolfMidiNormalizedRegisters Registers { get; init; }
	public required string Comment { get; init; }
	public Dictionary<string, string> CommentReferences { get; init; } = [];
	public int? Patch { get; set; }
	public int? LowPatch { get; set; }
	public int? LowNote { get; set; }
	public int? Drum { get; set; }
	public int? Transpose { get; set; }
	public int? MinTime { get; set; }
	public int? Kit { get; set; }
	public bool IsManuallyAssigned => Comment.Contains("manually-assigned", StringComparison.OrdinalIgnoreCase);
	public bool CommentMentionsSong(string songName)
	{
		string baseName = songName.EndsWith("_MUS", StringComparison.OrdinalIgnoreCase)
			? songName[..^4]
			: songName;
		return CommentReferences.ContainsKey(baseName);
	}

	public string? GetReferencedProgramForSong(string songName)
	{
		string baseName = songName.EndsWith("_MUS", StringComparison.OrdinalIgnoreCase)
			? songName[..^4]
			: songName;
		return CommentReferences.TryGetValue(baseName, out string? program) ? program : null;
	}

	public static Dictionary<string, string> ParseCommentReferences(string comment)
	{
		Dictionary<string, string> references = [];
		foreach (Match match in CommentReferenceRegex.Matches(comment))
		{
			string prefix = match.Groups["prefix"].Value;
			string program = match.Groups["program"].Value;
			string song = match.Groups["song"].Value;
			references[song] = prefix + program;
		}

		return references;
	}
}

internal sealed class WolfMidiNormalizedRegisters : IEquatable<WolfMidiNormalizedRegisters>
{
	public byte ModChar { get; init; }
	public byte CarChar { get; init; }
	public byte ModScale { get; init; }
	public byte CarScale { get; init; }
	public byte ModAttack { get; init; }
	public byte CarAttack { get; init; }
	public byte ModSustain { get; init; }
	public byte CarSustain { get; init; }
	public byte Feedback { get; init; }
	public byte ModWave { get; init; }
	public byte CarWave { get; init; }

	public static WolfMidiNormalizedRegisters FromPatch(WolfPatchRegisters patch) => new()
	{
		ModChar = patch.ModChar,
		CarChar = patch.CarChar,
		ModScale = patch.ModScale,
		CarScale = (byte)(patch.CarScale & 0xC0),
		ModAttack = patch.ModAttack,
		CarAttack = patch.CarAttack,
		ModSustain = patch.ModSustain,
		CarSustain = patch.CarSustain,
		Feedback = patch.Feedback,
		ModWave = patch.ModWave,
		CarWave = patch.CarWave
	};

	public bool Equals(WolfMidiNormalizedRegisters? other) =>
		WolfMidiNormalizedRegistersComparer.Instance.Equals(this, other);

	public override bool Equals(object? obj) => Equals(obj as WolfMidiNormalizedRegisters);

	public override int GetHashCode() => WolfMidiNormalizedRegistersComparer.Instance.GetHashCode(this);
}

internal sealed class WolfMidiNormalizedRegistersComparer : IEqualityComparer<WolfMidiNormalizedRegisters>
{
	public static readonly WolfMidiNormalizedRegistersComparer Instance = new();

	public bool Equals(WolfMidiNormalizedRegisters? x, WolfMidiNormalizedRegisters? y) =>
		x is not null &&
		y is not null &&
		x.ModChar == y.ModChar &&
		x.CarChar == y.CarChar &&
		x.ModScale == y.ModScale &&
		x.CarScale == y.CarScale &&
		x.ModAttack == y.ModAttack &&
		x.CarAttack == y.CarAttack &&
		x.ModSustain == y.ModSustain &&
		x.CarSustain == y.CarSustain &&
		x.Feedback == y.Feedback &&
		x.ModWave == y.ModWave &&
		x.CarWave == y.CarWave;

	public int GetHashCode(WolfMidiNormalizedRegisters obj) =>
		HashCode.Combine(
			HashCode.Combine(obj.ModChar, obj.CarChar, obj.ModScale, obj.CarScale, obj.ModAttack, obj.CarAttack),
			HashCode.Combine(obj.ModSustain, obj.CarSustain, obj.Feedback, obj.ModWave, obj.CarWave));
}
