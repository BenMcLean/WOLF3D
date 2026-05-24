using BenMcLean.Wolf3D.MusicTool;

const string DefaultSong = "WONDERIN_MUS";
const string DefaultRemixBaseName = "Wondering About My Remix";
const string DefaultGameXml = "games/WL1.xml";
const string DefaultWolfMidiRepo = "src\\ThirdParty\\wolfmidi";
const string DefaultWolfMidiInst = "src\\ThirdParty\\wolfmidi\\inst.txt";
const string DefaultWolfMidiPatchNames = "src\\ThirdParty\\wolfmidi\\patch.txt";
const string DefaultWolfMidiRepoUrl = "https://github.com/ericvids/wolfmidi.git";
const string DefaultImfCreatorRepo = "src\\ThirdParty\\imf-creator";
const string DefaultPromoRemixDir = "promo\\remix";
const string DefaultFinalWlf = "godot\\BenMcLean.Wolf3D.Shared\\Resources\\Wondering About My Remix.wlf";
const string DefaultCustomOp2 = "promo\\remix\\Wondering About My Remix.op2";

if (HasFlag(args, "--help", "-h"))
{
	Console.WriteLine("""
Usage:
  BenMcLean.Wolf3D.MusicTool build-remix [options]
  BenMcLean.Wolf3D.MusicTool extract-wolf-op2 [options]
  BenMcLean.Wolf3D.MusicTool map-wolfmidi-song [options]
  BenMcLean.Wolf3D.MusicTool export-wolfmidi-assets [options]
  BenMcLean.Wolf3D.MusicTool convert-midi-to-wlf [options]
  BenMcLean.Wolf3D.MusicTool export-noah-drums [options]
  BenMcLean.Wolf3D.MusicTool export-cmf-drums [options]

Commands:
  build-remix
    Runs the repeatable WONDERIN remix pipeline using repo-relative defaults:
    read the exported MIDI from promo/remix, inspect it for GM-program
    mismatches, and write the final WLF into the shared Godot Resources folder.
    This command does not rebuild the asset bundle; run export-wolfmidi-assets
    separately when you want to regenerate the starter bank/manifest.

    Options:
      --promo-dir <dir>        Promo remix working directory (default: promo/remix)
      --midi <path>            Input MIDI file path
      --op2 <path>             OP2 bank path (default: promo/remix/Wondering About My Remix.op2)
      --out-wlf <path>         Final WLF output path
      --format <name>          imf1 or imf0wlf (default: imf1)
      --python <command>       Python command to invoke (default: python)
      --imfcreator-repo <dir>  Local adambiser/imf-creator checkout path
      --imfcreator-url <url>   Git URL used if the repo must be cloned
      --title <text>           IMF type-1 title tag
      --composer <text>        IMF type-1 composer tag
      --remarks <text>         IMF type-1 remarks tag
      --program <text>         IMF type-1 program tag

  extract-wolf-op2
    Extracts melodic OPL patches from a Wolf3D IMF/WLF song and emits an OP2 bank
    plus a JSON manifest. The extracted patches are grouped by AdLib channel and
    written sequentially into the OP2 melodic bank for later manual naming/mapping.

    Options:
      --game <xml>       Game XML to load (default: games/WL6.xml)
      --song <name>      IMF song name (default: WONDERIN_MUS)
      --out-op2 <path>   Output OP2 file path
      --out-json <path>  Output JSON manifest path

  map-wolfmidi-song
    Extracts a Wolf3D IMF/WLF song, normalizes melodic patches the same way
    wolfmidi's inst.txt matcher does, and emits a JSON report mapping AdLib
    channels to wolfmidi's GM patch assignments.

    Options:
      --game <xml>       Game XML to load (default: games/WL6.xml)
      --song <name>      IMF song name (default: WONDERIN_MUS)
      --inst <path>      wolfmidi inst.txt path
      --out-json <path>  Output JSON report path

  export-wolfmidi-assets
    Builds the reproducible song asset bundle for a Wolf3D remix workflow:
    wolfmidi mapping JSON, FL Studio GM program list, an OP2 bank laid out on
    those same GM program numbers, plus reference JSON for the working bundle.

    Options:
      --game <xml>             Game XML to load (default: games/WL6.xml)
      --song <name>            IMF song name (default: WONDERIN_MUS)
      --inst <path>            wolfmidi inst.txt path
      --patch-names <path>     wolfmidi patch.txt path
      --percussion-op2 <path>  Optional OP2 bank whose percussion slots should be copied in
      --out-dir <dir>          Output directory for generated assets (default: promo/remix)
      --imfcreator-repo <dir>  Local adambiser/imf-creator checkout path
      --imfcreator-url <url>   Git URL used if the repo must be cloned later

  convert-midi-to-wlf
    Converts an exported MIDI file to Wolf3D WLF/IMF using the open-source
    adambiser/imf-creator Python CLI and a prepared OP2 bank. If the target
    repo is missing, it is cloned automatically. You may point it at the
    reference wolfmidi JSON, but that JSON is optional and not required for
    the main build-remix workflow.

    Options:
      --midi <path>            Input MIDI file path (default: promo/remix/Wondering About My Remix.mid)
      --assets-json <path>     Generated wolfmidi JSON from export-wolfmidi-assets (default: promo/remix/WONDERIN_MUS.wolfmidi.json)
      --op2 <path>             OP2 bank path (used when --assets-json is omitted)
      --out-wlf <path>         Output WLF/IMF path (default: godot/BenMcLean.Wolf3D.Shared/Resources/Wondering About My Remix.wlf)
      --format <name>          imf1 or imf0wlf (default: imf1)
      --python <command>       Python command to invoke (default: python)
      --imfcreator-repo <dir>  Local adambiser/imf-creator checkout path
      --imfcreator-url <url>   Git URL used if the repo must be cloned
      --title <text>           IMF type-1 title tag
      --composer <text>        IMF type-1 composer tag
      --remarks <text>         IMF type-1 remarks tag
      --program <text>         IMF type-1 program tag

  export-noah-drums
    Emits a clearly-labeled Noah percussion source bundle for remix work:
    a percussion OP2 approximation bank plus a JSON report. WAV previews are
    optional and only generated when explicitly requested.

    Options:
      --game <xml>          Game XML to load (default: games/N3D.xml)
      --out-op2 <path>      Output OP2 file path (default: promo/remix/NoahDrums.op2)
      --out-json <path>     Output JSON report path (default: promo/remix/NoahDrums.json)
      --preview-dir <dir>   Optional output directory for WAV previews

  export-cmf-drums
    Extracts the embedded CMF rhythm instruments, maps the used percussion voices to
    standard GM drum notes in an OP2 bank, and emits a JSON report describing the
    exact source patches plus any approximation decisions.

    Options:
      --cmf <path>       Input CMF file path
      --out-op2 <path>   Output OP2 file path
      --out-json <path>  Output JSON report path
""");
	return 0;
}
else if (args.Length == 0)
{
	return RunBuildRemix([]);
}

try
{
	string command = args[0];
	string[] rest = args[1..];
	return command switch
	{
		"build-remix" => RunBuildRemix(rest),
		"extract-wolf-op2" => RunExtractWolfOp2(rest),
		"map-wolfmidi-song" => RunMapWolfMidiSong(rest),
		"export-wolfmidi-assets" => RunExportWolfMidiAssets(rest),
		"convert-midi-to-wlf" => RunConvertMidiToWlf(rest),
		"export-noah-drums" => RunExportNoahDrums(rest),
		"export-cmf-drums" => RunExportCmfDrums(rest),
		_ => throw new ArgumentException($"Unknown command '{command}'.")
	};
}
catch (Exception ex)
{
	Console.Error.WriteLine(ex.Message);
	return 1;
}

static int RunExtractWolfOp2(string[] args)
{
	string gameXml = GetOption(args, "--game") ?? DefaultGameXml;
	string song = GetOption(args, "--song") ?? DefaultSong;
	string outOp2 = GetOption(args, "--out-op2")
		?? Path.Combine(".tmp", "music", "WONDERIN_MUS.op2");
	string outJson = GetOption(args, "--out-json")
		?? Path.Combine(".tmp", "music", "WONDERIN_MUS.json");

	WolfOp2Extractor.Export(gameXml, song, outOp2, outJson);
	Console.WriteLine($"Wrote {outOp2}");
	Console.WriteLine($"Wrote {outJson}");
	return 0;
}

static int RunMapWolfMidiSong(string[] args)
{
	string gameXml = GetOption(args, "--game") ?? DefaultGameXml;
	string song = GetOption(args, "--song") ?? DefaultSong;
	string inst = GetOption(args, "--inst")
		?? DefaultWolfMidiInst;
	EnsureDefaultWolfMidiRepo(inst, patchNames: null);
	string outJson = GetOption(args, "--out-json")
		?? Path.Combine(".tmp", "music", "WONDERIN_MUS.wolfmidi.json");

	WolfMidiMapper.ExportSongMapping(gameXml, song, inst, outJson);
	Console.WriteLine($"Wrote {outJson}");
	return 0;
}

static int RunBuildRemix(string[] args)
{
	string promoDir = GetOption(args, "--promo-dir") ?? DefaultPromoRemixDir;
	string midi = GetOption(args, "--midi") ?? Path.Combine(promoDir, $"{DefaultRemixBaseName}.mid");
	string customOp2 = GetOption(args, "--op2") ?? DefaultCustomOp2;
	string outWlf = GetOption(args, "--out-wlf") ?? DefaultFinalWlf;
	string format = GetOption(args, "--format") ?? "imf1";
	string python = GetOption(args, "--python") ?? "python";

	if (!File.Exists(midi))
	{
		Console.Error.WriteLine($"WARNING: MIDI source not found yet: {Path.GetFullPath(midi)}");
		Console.Error.WriteLine("WARNING: Skipping WLF conversion for now. Export the MIDI to that path and run build-remix again.");
		return 0;
	}
	if (!File.Exists(customOp2))
		throw new FileNotFoundException(
			$"Remix OP2 bank not found at '{Path.GetFullPath(customOp2)}'. Build or copy the bank there before running build-remix.",
			Path.GetFullPath(customOp2));

	MidiProgramUsage midiPrograms = MidiProgramInspector.Inspect(midi);
	Console.WriteLine($"MIDI GM programs in use: {string.Join(", ", midiPrograms.Programs.Select(program => program.ToString("D3")))}");
	foreach (string warning in Op2ProgramInspector.BuildWarnings(midiPrograms, customOp2))
		Console.Error.WriteLine($"WARNING: {warning}");

	string? title = GetOption(args, "--title") ?? DefaultRemixBaseName;
	string? composer = GetOption(args, "--composer");
	string? remarks = GetOption(args, "--remarks");
	string? program = GetOption(args, "--program") ?? "FLStudio";

	ImfCreatorToolchain.ConvertMidiToWlf(
		midi,
		outWlf,
		customOp2,
		GetOption(args, "--imfcreator-repo") ?? DefaultImfCreatorRepo,
		GetOption(args, "--imfcreator-url") ?? ImfCreatorToolchain.DefaultRepoUrl,
		format,
		python,
		title,
		composer,
		remarks,
		program);

	Console.WriteLine($"Wrote {outWlf}");
	return 0;
}

static int RunExportWolfMidiAssets(string[] args)
{
	string gameXml = GetOption(args, "--game") ?? DefaultGameXml;
	string song = GetOption(args, "--song") ?? DefaultSong;
	string inst = GetOption(args, "--inst")
		?? DefaultWolfMidiInst;
	string patchNames = GetOption(args, "--patch-names")
		?? DefaultWolfMidiPatchNames;
	EnsureDefaultWolfMidiRepo(inst, patchNames);
	string? percussionOp2 = GetOption(args, "--percussion-op2");
	string outDir = GetOption(args, "--out-dir")
		?? DefaultPromoRemixDir;
	string imfCreatorRepo = GetOption(args, "--imfcreator-repo")
		?? DefaultImfCreatorRepo;
	string imfCreatorUrl = GetOption(args, "--imfcreator-url")
		?? ImfCreatorToolchain.DefaultRepoUrl;

	WolfMidiSongAssetExporter.Export(
		gameXml,
		song,
		inst,
		patchNames,
		outDir,
		percussionOp2,
		imfCreatorRepo,
		imfCreatorUrl);
	Console.WriteLine($"Wrote assets to {outDir}");
	return 0;
}

static int RunConvertMidiToWlf(string[] args)
{
	string midi = GetOption(args, "--midi")
		?? Path.Combine(DefaultPromoRemixDir, $"{DefaultRemixBaseName}.mid");
	string? assetsJson = GetOption(args, "--assets-json");
	string? explicitOp2 = GetOption(args, "--op2");
	string format = GetOption(args, "--format") ?? "imf1";
	string python = GetOption(args, "--python") ?? "python";
	string outWlf = GetOption(args, "--out-wlf")
		?? DefaultFinalWlf;

	string op2;
	string imfCreatorRepo;
	string imfCreatorUrl;
	string? defaultTitle = null;

	if (!string.IsNullOrWhiteSpace(assetsJson))
	{
		WolfMidiAssetsManifest manifest = ImfCreatorToolchain.LoadManifest(assetsJson);
		op2 = manifest.Op2Bank;
		imfCreatorRepo = GetOption(args, "--imfcreator-repo")
			?? manifest.ImfCreatorRepo
			?? Path.Combine("..", "..", "imf-creator");
		imfCreatorUrl = GetOption(args, "--imfcreator-url")
			?? manifest.ImfCreatorRepoUrl
			?? ImfCreatorToolchain.DefaultRepoUrl;
		defaultTitle = manifest.Song;

		MidiProgramUsage midiPrograms = MidiProgramInspector.Inspect(midi);
		Console.WriteLine($"MIDI GM programs in use: {string.Join(", ", midiPrograms.Programs.Select(program => program.ToString("D3")))}");
		foreach (string warning in MidiProgramInspector.BuildWarnings(
			midiPrograms,
			[.. manifest.FlStudioPrograms.Select(program => program.Program).OrderBy(program => program)]))
			Console.Error.WriteLine($"WARNING: {warning}");
	}
	else
	{
		assetsJson = Path.Combine(DefaultPromoRemixDir, $"{DefaultSong}.wolfmidi.json");
		if (File.Exists(assetsJson))
		{
			WolfMidiAssetsManifest manifest = ImfCreatorToolchain.LoadManifest(assetsJson);
			op2 = explicitOp2 ?? manifest.Op2Bank;
			imfCreatorRepo = GetOption(args, "--imfcreator-repo")
				?? manifest.ImfCreatorRepo
				?? DefaultImfCreatorRepo;
			imfCreatorUrl = GetOption(args, "--imfcreator-url")
				?? manifest.ImfCreatorRepoUrl
				?? ImfCreatorToolchain.DefaultRepoUrl;
			defaultTitle = manifest.Song;

			MidiProgramUsage midiPrograms = MidiProgramInspector.Inspect(midi);
			Console.WriteLine($"MIDI GM programs in use: {string.Join(", ", midiPrograms.Programs.Select(program => program.ToString("D3")))}");
			foreach (string warning in MidiProgramInspector.BuildWarnings(
				midiPrograms,
				[.. manifest.FlStudioPrograms.Select(program => program.Program).OrderBy(program => program)]))
				Console.Error.WriteLine($"WARNING: {warning}");
		}
		else
		{
		op2 = explicitOp2
			?? throw new ArgumentException("Missing required option '--op2' when '--assets-json' is not provided.");
		imfCreatorRepo = GetOption(args, "--imfcreator-repo")
			?? DefaultImfCreatorRepo;
		imfCreatorUrl = GetOption(args, "--imfcreator-url")
			?? ImfCreatorToolchain.DefaultRepoUrl;
		}
	}

	string? title = GetOption(args, "--title") ?? defaultTitle;
	string? composer = GetOption(args, "--composer");
	string? remarks = GetOption(args, "--remarks");
	string? program = GetOption(args, "--program") ?? "FLStudio";

	ImfCreatorToolchain.ConvertMidiToWlf(
		midi,
		outWlf,
		op2,
		imfCreatorRepo,
		imfCreatorUrl,
		format,
		python,
		title,
		composer,
		remarks,
		program);
	Console.WriteLine($"Wrote {outWlf}");
	return 0;
}

static int RunExportNoahDrums(string[] args)
{
	string gameXml = GetOption(args, "--game") ?? Path.Combine("games", "N3D.xml");
	string outOp2 = GetOption(args, "--out-op2")
		?? Path.Combine(DefaultPromoRemixDir, "NoahDrums.op2");
	string outJson = GetOption(args, "--out-json")
		?? Path.Combine(DefaultPromoRemixDir, "NoahDrums.json");
	string? previewDir = GetOption(args, "--preview-dir");

	NoahDrumExporter.Export(gameXml, outOp2, outJson, previewDir);
	Console.WriteLine($"Wrote {outOp2}");
	Console.WriteLine($"Wrote {outJson}");
	if (!string.IsNullOrWhiteSpace(previewDir))
		Console.WriteLine($"Wrote preview assets to {previewDir}");
	return 0;
}

static int RunExportCmfDrums(string[] args)
{
	string cmf = GetOption(args, "--cmf")
		?? throw new ArgumentException("Missing required option '--cmf'.");
	string outOp2 = GetOption(args, "--out-op2")
		?? Path.Combine(".tmp", "music", $"{Path.GetFileNameWithoutExtension(cmf)}.drums.op2");
	string outJson = GetOption(args, "--out-json")
		?? Path.Combine(".tmp", "music", $"{Path.GetFileNameWithoutExtension(cmf)}.drums.json");

	CmfDrumExporter.Export(cmf, outOp2, outJson);
	Console.WriteLine($"Wrote {outOp2}");
	Console.WriteLine($"Wrote {outJson}");
	return 0;
}

static string? GetOption(string[] args, string name)
{
	for (int i = 0; i < args.Length - 1; i++)
		if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
			return args[i + 1];
	return null;
}

static bool HasFlag(string[] args, params string[] names) =>
	args.Any(arg => names.Contains(arg, StringComparer.OrdinalIgnoreCase));

static void EnsureDefaultWolfMidiRepo(string instPath, string? patchNames)
{
	bool usesDefaultInst = Path.GetFullPath(instPath).Equals(Path.GetFullPath(DefaultWolfMidiInst), StringComparison.OrdinalIgnoreCase);
	bool usesDefaultPatchNames = patchNames is null ||
		Path.GetFullPath(patchNames).Equals(Path.GetFullPath(DefaultWolfMidiPatchNames), StringComparison.OrdinalIgnoreCase);
	if (!usesDefaultInst && !usesDefaultPatchNames)
		return;

	GitRepoToolchain.EnsureRepo(DefaultWolfMidiRepo, DefaultWolfMidiRepoUrl, "inst.txt", "ericvids/wolfmidi");
}
