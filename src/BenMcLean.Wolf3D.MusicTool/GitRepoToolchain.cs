using System.Diagnostics;

namespace BenMcLean.Wolf3D.MusicTool;

internal static class GitRepoToolchain
{
	public static void EnsureRepo(string repoPath, string repoUrl, string markerFileRelativePath, string errorLabel)
	{
		string fullRepoPath = Path.GetFullPath(repoPath);
		string markerPath = Path.Combine(fullRepoPath, markerFileRelativePath);
		if (Directory.Exists(fullRepoPath) && File.Exists(markerPath))
			return;

		Directory.CreateDirectory(Path.GetDirectoryName(fullRepoPath)!);
		RunProcess(
			"git",
			$"clone {Quote(repoUrl)} {Quote(fullRepoPath)}",
			Path.GetDirectoryName(fullRepoPath)!,
			$"Failed to clone {errorLabel}.");
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

	private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}
