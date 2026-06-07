using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace BenMcLean.Wolf3D.Shared;

/// <summary>
/// Resolves the effective set of game XML definitions by combining embedded official XML
/// with optional user XML overrides/additions from the writable games directory.
/// </summary>
public static class GameCatalog
{
	private const string EmbeddedResourcePrefix = "BenMcLean.Wolf3D.Shared.Resources.",
		SharewareFileName = "WL1.xml",
		SharewareZipResource = "BenMcLean.Wolf3D.Shared.Resources.Wolfenstein3dV14sw.ZIP";
	public sealed record GameDefinition(string XmlPath, XElement Xml, bool IsEmbedded)
	{
		public string FileName => Path.GetFileName(XmlPath);
		public string DisplayName => Xml.Attribute("Name")?.Value ?? Path.GetFileNameWithoutExtension(XmlPath);
		public string GameDataDirectory => Path.Combine(
			Path.GetDirectoryName(XmlPath) ?? string.Empty,
			Xml.Attribute("Path")?.Value ?? string.Empty);
	}
	private static readonly Lazy<IReadOnlyDictionary<string, string>> EmbeddedXmlResources = new(() =>
		Assembly.GetExecutingAssembly()
			.GetManifestResourceNames()
			.Where(name =>
				name.StartsWith(EmbeddedResourcePrefix, StringComparison.OrdinalIgnoreCase) &&
				name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
			.ToDictionary(
				keySelector: name => name[EmbeddedResourcePrefix.Length..],
				elementSelector: name => name,
				comparer: StringComparer.OrdinalIgnoreCase));
	private static readonly Lazy<HashSet<string>> EmbeddedSharewareFiles = new(LoadEmbeddedSharewareFiles);
	/// <summary>
	/// Returns the merged, playable game list for the selection menu.
	/// Embedded official XML is considered first, then user XML files override/add by filename.
	/// </summary>
	public static IReadOnlyList<GameDefinition> GetAvailableGames(string gamesDirectory)
	{
		string fullGamesDirectory = Path.GetFullPath(gamesDirectory);
		Dictionary<string, GameDefinition> merged = new(StringComparer.OrdinalIgnoreCase);
		foreach ((string fileName, string resourceName) in EmbeddedXmlResources.Value.OrderBy(kvp => kvp.Key))
			if (TryLoadEmbeddedGameDefinition(fullGamesDirectory, fileName, resourceName, out GameDefinition definition) &&
				IsPlayable(definition))
				merged[fileName] = definition;
		foreach (string xmlPath in EnumerateUserXmlFiles(fullGamesDirectory))
		{
			string fileName = Path.GetFileName(xmlPath);
			if (TryLoadFileGameDefinition(xmlPath, out GameDefinition definition) &&
				IsPlayable(definition))
				merged[fileName] = definition;
			else
				merged.Remove(fileName);
		}
		return [.. merged.Values
			.OrderBy(def => !Path.GetFileNameWithoutExtension(def.XmlPath).Equals("WL1", StringComparison.OrdinalIgnoreCase))
			.ThenBy(def => Path.GetFileNameWithoutExtension(def.XmlPath), StringComparer.OrdinalIgnoreCase)];
	}
	/// <summary>
	/// Resolves the effective XML definition for a logical path inside the games directory.
	/// User XML files take precedence; otherwise embedded official XML is used when available.
	/// </summary>
	public static GameDefinition Resolve(string xmlPath, bool preferEmbeddedOfficial = false)
	{
		string fullPath = Path.GetFullPath(xmlPath),
			fileName = Path.GetFileName(fullPath);
		if (preferEmbeddedOfficial &&
			EmbeddedXmlResources.Value.TryGetValue(fileName, out string preferredResourceName) &&
			TryLoadEmbeddedGameDefinition(Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory(), fileName, preferredResourceName, out GameDefinition preferredDefinition))
			return preferredDefinition;
		if (File.Exists(fullPath))
		{
			if (TryLoadFileGameDefinition(fullPath, out GameDefinition fileDefinition))
				return fileDefinition;
			throw new FileNotFoundException($"Unable to parse game XML: {fullPath}", fullPath);
		}
		if (EmbeddedXmlResources.Value.TryGetValue(fileName, out string resourceName) &&
			TryLoadEmbeddedGameDefinition(Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory(), fileName, resourceName, out GameDefinition embeddedDefinition))
			return embeddedDefinition;
		throw new FileNotFoundException($"Unable to locate game XML: {fullPath}", fullPath);
	}
	private static IEnumerable<string> EnumerateUserXmlFiles(string gamesDirectory)
	{
		try
		{
			return [.. Directory.EnumerateFiles(gamesDirectory, "*.xml")
				.Where(path => !Path.GetFileName(path).Equals("WOLF3D.xsd", StringComparison.OrdinalIgnoreCase))
				.Select(Path.GetFullPath)];
		}
		catch
		{
			return [];
		}
	}
	private static bool TryLoadFileGameDefinition(string xmlPath, out GameDefinition definition)
	{
		try
		{
			XElement xml = Assets.GameXmlResolver.Load(
				xmlPath,
				baseReference =>
				{
					string baseFileName = Path.GetFileName(baseReference);
					if (string.IsNullOrWhiteSpace(baseFileName) ||
						!EmbeddedXmlResources.Value.TryGetValue(baseFileName, out string baseResourceName))
						throw new FileNotFoundException(
							$"Unable to resolve embedded base game XML '{baseReference}' for '{xmlPath}'.",
							baseReference);
					return LoadEmbeddedGameDefinitionXml(
						fileName: baseFileName,
						resourceName: baseResourceName,
						visited: new HashSet<string>(StringComparer.OrdinalIgnoreCase));
				});
			definition = new(xmlPath, xml, IsEmbedded: false);
			return true;
		}
		catch
		{
			definition = default!;
			return false;
		}
	}
	private static bool TryLoadEmbeddedGameDefinition(string gamesDirectory, string fileName, string resourceName, out GameDefinition definition)
	{
		try
		{
			XElement xml = LoadEmbeddedGameDefinitionXml(fileName, resourceName, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
			definition = new(Path.Combine(gamesDirectory, fileName), xml, IsEmbedded: true);
			return true;
		}
		catch
		{
			definition = default!;
			return false;
		}
	}
	private static XElement LoadEmbeddedGameDefinitionXml(string fileName, string resourceName, HashSet<string> visited)
	{
		if (!visited.Add(fileName))
			throw new InvalidDataException($"Circular embedded game XML inheritance detected involving '{fileName}'.");
		try
		{
			using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
				?? throw new InvalidDataException($"Missing embedded resource {resourceName}.");
			XElement xml = XDocument.Load(stream).Root
				?? throw new InvalidDataException($"Missing root element in embedded resource {resourceName}.");
			return Assets.GameXmlResolver.Resolve(xml, baseReference =>
			{
				string baseFileName = Path.GetFileName(baseReference);
				if (string.IsNullOrWhiteSpace(baseFileName) ||
					!EmbeddedXmlResources.Value.TryGetValue(baseFileName, out string baseResourceName))
					throw new FileNotFoundException(
						$"Unable to resolve embedded base game XML '{baseReference}' for '{fileName}'.",
						baseReference);
				return LoadEmbeddedGameDefinitionXml(baseFileName, baseResourceName, visited);
			});
		}
		finally
		{
			visited.Remove(fileName);
		}
	}
	private static bool IsPlayable(GameDefinition definition) =>
		CanUseEmbeddedSharewareData(definition)
		|| HasExternalGameData(definition);
	public static bool HasExternalGameData(GameDefinition definition) =>
		Directory.Exists(definition.GameDataDirectory) &&
		RequiredFilesExist(definition.Xml.Element("VgaGraph"), definition.GameDataDirectory, "VgaDict", "VgaGraph", "VgaHead") &&
		RequiredFilesExist(definition.Xml.Element("Maps"), definition.GameDataDirectory, "MapHead", "GameMaps") &&
		RequiredFilesExist(definition.Xml.Element("Audio"), definition.GameDataDirectory, "AudioHead", "AudioT") &&
		RequiredFilesExist(definition.Xml.Element("VSwap"), definition.GameDataDirectory, "Name");
	private static bool RequiredFilesExist(XElement element, string folder, params string[] attributeNames)
	{
		if (element is null)
			return true;
		foreach (string attributeName in attributeNames)
		{
			string fileName = element.Attribute(attributeName)?.Value;
			if (string.IsNullOrWhiteSpace(fileName))
				continue;
			if (!File.Exists(Path.Combine(folder, fileName)))
				return false;
		}
		return true;
	}
	public static bool CanUseEmbeddedSharewareData(GameDefinition definition) =>
		definition.FileName.Equals(SharewareFileName, StringComparison.OrdinalIgnoreCase) &&
		RequiredFilesExist(definition.Xml.Element("VgaGraph"), string.Empty, EmbeddedSharewareFileExists, "VgaDict", "VgaGraph", "VgaHead") &&
		RequiredFilesExist(definition.Xml.Element("Maps"), string.Empty, EmbeddedSharewareFileExists, "MapHead", "GameMaps") &&
		RequiredFilesExist(definition.Xml.Element("Audio"), string.Empty, EmbeddedSharewareFileExists, "AudioHead", "AudioT") &&
		RequiredFilesExist(definition.Xml.Element("VSwap"), string.Empty, EmbeddedSharewareFileExists, "Name");
	private static bool EmbeddedSharewareFileExists(string path) => EmbeddedSharewareFiles.Value.Contains(Path.GetFileName(path));
	private static bool RequiredFilesExist(XElement element, string folder, Func<string, bool> fileExists, params string[] attributeNames)
	{
		if (element is null)
			return true;
		foreach (string attributeName in attributeNames)
		{
			string fileName = element.Attribute(attributeName)?.Value;
			if (string.IsNullOrWhiteSpace(fileName))
				continue;
			if (!fileExists(Path.Combine(folder, fileName)))
				return false;
		}
		return true;
	}
	private static HashSet<string> LoadEmbeddedSharewareFiles()
	{
		HashSet<string> files = new(StringComparer.OrdinalIgnoreCase);
		using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(SharewareZipResource);
		if (stream is null)
			return files;
		using System.IO.Compression.ZipArchive archive = new(stream, System.IO.Compression.ZipArchiveMode.Read);
		foreach (System.IO.Compression.ZipArchiveEntry entry in archive.Entries)
			if (!string.IsNullOrEmpty(entry.Name))
				files.Add(entry.Name);
		return files;
	}
}
