namespace BenMcLean.Wolf3D.MusicTool;

internal static class PortablePath
{
	public static string ToStoredPath(string metadataFilePath, string targetPath)
	{
		string baseDirectory = Path.GetDirectoryName(Path.GetFullPath(metadataFilePath))
			?? Directory.GetCurrentDirectory();
		return Path.GetRelativePath(baseDirectory, Path.GetFullPath(targetPath)).Replace('/', '\\');
	}

	public static string? ToStoredPathOrNull(string metadataFilePath, string? targetPath) =>
		string.IsNullOrWhiteSpace(targetPath) ? null : ToStoredPath(metadataFilePath, targetPath);

	public static string ResolveFromStoredPath(string metadataFilePath, string storedPath)
	{
		if (Path.IsPathRooted(storedPath))
			return storedPath;

		string baseDirectory = Path.GetDirectoryName(Path.GetFullPath(metadataFilePath))
			?? Directory.GetCurrentDirectory();
		return Path.GetFullPath(Path.Combine(baseDirectory, storedPath));
	}

	public static string? ResolveFromStoredPathOrNull(string metadataFilePath, string? storedPath) =>
		string.IsNullOrWhiteSpace(storedPath) ? null : ResolveFromStoredPath(metadataFilePath, storedPath);
}
