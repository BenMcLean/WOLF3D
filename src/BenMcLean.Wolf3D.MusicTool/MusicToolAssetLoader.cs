using System.IO.Compression;
using System.Xml.Linq;
using BenMcLean.Wolf3D.Assets;

namespace BenMcLean.Wolf3D.MusicTool;

internal static class MusicToolAssetLoader
{
	private const string DefaultSharewareZip = "godot\\BenMcLean.Wolf3D.Shared\\Resources\\Wolfenstein3dV14sw.ZIP";

	public static AssetManager Load(string gameXml)
	{
		string fullXmlPath = Path.GetFullPath(gameXml);
		XElement xml = GameXmlResolver.Load(fullXmlPath);
		string xmlDirectory = Path.GetDirectoryName(fullXmlPath) ?? Directory.GetCurrentDirectory();
		string gameDataDirectory = Path.Combine(xmlDirectory, xml.Attribute("Path")?.Value ?? string.Empty);

		if (HasRequiredExternalData(xml, gameDataDirectory))
			return new AssetManager(xml, gameDataDirectory);

		string sharewareZip = Path.GetFullPath(Path.Combine(xmlDirectory, "..", DefaultSharewareZip));
		if (CanUseLocalSharewareZip(xml, sharewareZip))
		{
			Dictionary<string, byte[]> files = LoadZipEntries(sharewareZip);
			return AssetManager.Load(
				xml,
				gameDataDirectory,
				path => OpenZipEntry(files, path),
				path => files.ContainsKey(Path.GetFileName(path)),
				loggerFactory: null);
		}

		return new AssetManager(xml, gameDataDirectory);
	}

	private static bool CanUseLocalSharewareZip(XElement xml, string zipPath) =>
		File.Exists(zipPath) &&
		string.Equals(xml.Attribute("Extension")?.Value, "WL1", StringComparison.OrdinalIgnoreCase);

	private static bool HasRequiredExternalData(XElement xml, string folder) =>
		Directory.Exists(folder) &&
		RequiredFilesExist(xml.Element("VgaGraph"), folder, "VgaDict", "VgaGraph", "VgaHead") &&
		RequiredFilesExist(xml.Element("Maps"), folder, "MapHead", "GameMaps") &&
		RequiredFilesExist(xml.Element("Audio"), folder, "AudioHead", "AudioT") &&
		RequiredFilesExist(xml.Element("VSwap"), folder, "Name");

	private static bool RequiredFilesExist(XElement? element, string folder, params string[] attributeNames)
	{
		if (element is null)
			return true;

		foreach (string attributeName in attributeNames)
		{
			string? fileName = element.Attribute(attributeName)?.Value;
			if (string.IsNullOrWhiteSpace(fileName))
				continue;
			if (!File.Exists(Path.Combine(folder, fileName)))
				return false;
		}

		return true;
	}

	private static Dictionary<string, byte[]> LoadZipEntries(string zipPath)
	{
		using ZipArchive archive = ZipFile.OpenRead(zipPath);
		Dictionary<string, byte[]> files = new(StringComparer.OrdinalIgnoreCase);
		foreach (ZipArchiveEntry entry in archive.Entries)
		{
			if (string.IsNullOrEmpty(entry.Name))
				continue;
			using Stream entryStream = entry.Open();
			using MemoryStream memoryStream = new();
			entryStream.CopyTo(memoryStream);
			files[entry.Name] = memoryStream.ToArray();
		}

		return files;
	}

	private static Stream OpenZipEntry(Dictionary<string, byte[]> files, string path)
	{
		string name = Path.GetFileName(path);
		if (!files.TryGetValue(name, out byte[]? bytes))
			throw new FileNotFoundException($"Embedded shareware file not found: {name}", path);
		return new MemoryStream(bytes, writable: false);
	}
}
