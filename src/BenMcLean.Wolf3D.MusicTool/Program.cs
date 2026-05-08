using BenMcLean.Wolf3D.MusicTool;

if (args.Length == 0 || HasFlag(args, "--help", "-h"))
{
	PrintUsage();
	return 0;
}

try
{
	string command = args[0];
	string[] rest = args[1..];
	return command switch
	{
		"extract-wolf-op2" => RunExtractWolfOp2(rest),
		"export-noah-drums" => RunExportNoahDrums(rest),
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
	string gameXml = GetOption(args, "--game") ?? Path.Combine("games", "WL6.xml");
	string song = GetOption(args, "--song") ?? "WONDERIN_MUS";
	string midiPath = GetOption(args, "--midi")
		?? Path.Combine("godot", "BenMcLean.Wolf3D.Shared", "Resources", "Wondering About My Remix.mid");
	string outOp2 = GetOption(args, "--out-op2")
		?? Path.Combine(".tmp", "music", "Wondering About My Remix.wonderin.op2");
	string outJson = GetOption(args, "--out-json")
		?? Path.Combine(".tmp", "music", "Wondering About My Remix.wonderin.json");

	WolfOp2Extractor.Export(gameXml, song, midiPath, outOp2, outJson);
	Console.WriteLine($"Wrote {outOp2}");
	Console.WriteLine($"Wrote {outJson}");
	return 0;
}

static int RunExportNoahDrums(string[] args)
{
	string gameXml = GetOption(args, "--game") ?? Path.Combine("games", "N3D.xml");
	string outOp2 = GetOption(args, "--out-op2")
		?? Path.Combine(".tmp", "music", "NoahDrums.op2");
	string outDir = GetOption(args, "--out-dir")
		?? Path.Combine(".tmp", "music", "NoahDrums");

	NoahDrumExporter.Export(gameXml, outOp2, outDir);
	Console.WriteLine($"Wrote {outOp2}");
	Console.WriteLine($"Wrote preview assets to {outDir}");
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

static void PrintUsage()
{
	Console.WriteLine("""
Usage:
  BenMcLean.Wolf3D.MusicTool extract-wolf-op2 [options]
  BenMcLean.Wolf3D.MusicTool export-noah-drums [options]

Commands:
  extract-wolf-op2
    Extracts melodic OPL patches from a Wolf3D IMF/WLF song and emits an OP2 bank
    plus a JSON manifest. The bank auto-assigns the extracted patches to the melodic
    programs actually used by the target MIDI.

    Options:
      --game <xml>       Game XML to load (default: games/WL6.xml)
      --song <name>      IMF song name (default: WONDERIN_MUS)
      --midi <path>      MIDI used to determine which melodic programs need mapping
      --out-op2 <path>   Output OP2 file path
      --out-json <path>  Output JSON manifest path

  export-noah-drums
    Emits a Noah percussion OP2 approximation bank, an exact drum-usage report, and
    exact rendered WAV previews generated from the Noah AdLib rhythm-mode playback logic.

    Options:
      --game <xml>       Game XML to load (default: games/N3D.xml)
      --out-op2 <path>   Output OP2 file path
      --out-dir <dir>    Output directory for JSON and WAV previews
""");
}
