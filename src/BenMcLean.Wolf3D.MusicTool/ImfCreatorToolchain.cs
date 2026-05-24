using System.Text.Json;
using System.Diagnostics;

namespace BenMcLean.Wolf3D.MusicTool;

internal static class ImfCreatorToolchain
{
	public const string DefaultRepoUrl = "https://github.com/adambiser/imf-creator";

	public static void ConvertMidiToWlf(
		string midiPath,
		string outWlf,
		string op2Path,
		string imfCreatorRepoPath,
		string? imfCreatorRepoUrl,
		string format,
		string pythonCommand,
		string? title,
		string? composer,
		string? remarks,
		string? program)
	{
		EnsureImfCreatorRepo(imfCreatorRepoPath, imfCreatorRepoUrl ?? DefaultRepoUrl);
		string cliPath = Path.Combine(imfCreatorRepoPath, "midi2imf.py");
		if (!File.Exists(cliPath))
			throw new FileNotFoundException("midi2imf.py was not found in the imf-creator repository.", cliPath);

		List<string> arguments =
		[
			Quote(cliPath),
			"-b", Quote(Path.GetFullPath(op2Path)),
			"-o", Quote(Path.GetFullPath(outWlf)),
			Quote(Path.GetFullPath(midiPath)),
			format
		];

		if (string.Equals(format, "imf1", StringComparison.OrdinalIgnoreCase))
		{
			AddOptionalArgument(arguments, "--title", title);
			AddOptionalArgument(arguments, "--composer", composer);
			AddOptionalArgument(arguments, "--remarks", remarks);
			AddOptionalArgument(arguments, "--program", program);
		}

		RunProcess(
			pythonCommand,
			string.Join(' ', arguments),
			Path.GetFullPath(imfCreatorRepoPath),
			"imf-creator midi2imf conversion failed.");
	}

	public static WolfMidiAssetsManifest LoadManifest(string path) =>
		ResolveManifestPaths(
			path,
			JsonSerializer.Deserialize<WolfMidiAssetsManifest>(
			File.ReadAllText(path),
			new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true
			}) ?? throw new InvalidOperationException($"Failed to parse asset manifest '{path}'."));

	private static WolfMidiAssetsManifest ResolveManifestPaths(string manifestPath, WolfMidiAssetsManifest manifest) =>
		manifest with
		{
			Op2Bank = PortablePath.ResolveFromStoredPath(manifestPath, manifest.Op2Bank),
			PercussionOverlay = PortablePath.ResolveFromStoredPathOrNull(manifestPath, manifest.PercussionOverlay),
			ImfCreatorRepo = PortablePath.ResolveFromStoredPathOrNull(manifestPath, manifest.ImfCreatorRepo)
		};

	private static void EnsureImfCreatorRepo(string repoPath, string repoUrl)
	{
		GitRepoToolchain.EnsureRepo(repoPath, repoUrl, "midi2imf.py", "adambiser/imf-creator");
	}

	private static void RunProcess(string fileName, string arguments, string workingDirectory, string errorMessage)
	{
		ProcessStartInfo startInfo = new()
		{
			FileName = fileName,
			Arguments = arguments,
			WorkingDirectory = workingDirectory,
			UseShellExecute = false
		};

		using Process process = Process.Start(startInfo)
			?? throw new InvalidOperationException($"Failed to start process '{fileName}'.");
		process.WaitForExit();
		if (process.ExitCode != 0)
			throw new InvalidOperationException($"{errorMessage} Exit code: {process.ExitCode}.");
	}

	private static void AddOptionalArgument(List<string> arguments, string name, string? value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			arguments.Add(name);
			arguments.Add(Quote(value));
		}
	}

	private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}

internal sealed record WolfMidiAssetsManifest
{
	public required string Song { get; init; }
	public required string Op2Bank { get; init; }
	public string? PercussionOverlay { get; init; }
	public string? ImfCreatorRepo { get; init; }
	public string? ImfCreatorRepoUrl { get; init; }
	public required List<WolfMidiExpectedProgram> FlStudioPrograms { get; init; }
}

internal sealed class WolfMidiExpectedProgram
{
	public required int Program { get; init; }
	public required string PatchName { get; init; }
	public required int[] AdlibChannels { get; init; }
	public int? Transpose { get; init; }
	public int? MinTime { get; init; }
	public int? WolfMidiPatch { get; init; }
	public required string Comment { get; init; }
}
