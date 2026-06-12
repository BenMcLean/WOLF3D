using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using BenMcLean.Wolf3D.Assets.Gameplay;
using BenMcLean.Wolf3D.Assets.Menu;
using BenMcLean.Wolf3D.Assets.Graphics;
using BenMcLean.Wolf3D.Assets.Sound;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BenMcLean.Wolf3D.Assets;

public class AssetManager
{
	public readonly XElement XML;
	public readonly AudioT AudioT;
	public readonly VgaGraph VgaGraph;
	public readonly VSwap VSwap;
	public readonly GameMap[] Maps;
	public readonly MapAnalyzer MapAnalyzer;
	public readonly MapAnalyzer.MapAnalysis[] MapAnalyses;
	public readonly StateCollection StateCollection;
	public readonly WeaponCollection WeaponCollection;
	public readonly MenuCollection MenuCollection;
	/// <summary>
	/// Raw ASCII text of all loaded text chunks, keyed by logical name (e.g., "T_HELPART").
	/// Populated from external files (WL1 shareware) and embedded VGAGRAPH chunks (APO/WL6).
	/// WL_TEXT.C: CA_LoadFile(helpfilename) / CA_CacheGrChunk(T_HELPART)
	/// </summary>
	public readonly Dictionary<string, string> TextChunks;
	public static AssetManager Load(string xmlPath, ILoggerFactory loggerFactory = null)
	{
		XElement xml = GameXmlResolver.Load(xmlPath);
		return new(xml, Path.Combine(Path.GetDirectoryName(xmlPath), xml.Attribute("Path")?.Value), loggerFactory);
	}
	public static AssetManager Load(
		XElement xml,
		string folder,
		Func<string, Stream> openRead,
		Func<string, bool> fileExists,
		ILoggerFactory loggerFactory = null) =>
		new(xml, folder, openRead, fileExists, loggerFactory);
	public AssetManager(XElement xml, string folder = "", ILoggerFactory loggerFactory = null) :
		this(xml, folder, openRead: null, fileExists: null, loggerFactory)
	{ }
	private AssetManager(
		XElement xml,
		string folder,
		Func<string, Stream> openRead,
		Func<string, bool> fileExists,
		ILoggerFactory loggerFactory = null)
	{
		if (openRead is null && !Directory.Exists(folder))
			throw new DirectoryNotFoundException(folder);
		// Always wrap for case-insensitive resolution. Transparent on Windows (filesystem is
		// already case-insensitive); needed on Linux where e.g. Noah's Ark ships lowercase
		// filenames on some releases and uppercase on others.
		Func<string, bool> baseFileExists = fileExists ?? File.Exists;
		Func<string, Stream> baseOpenRead = openRead ?? File.OpenRead;
		fileExists = path => baseFileExists(ResolveCaseInsensitive(path, baseFileExists));
		openRead = path => baseOpenRead(ResolveCaseInsensitive(path, baseFileExists));
		// When multiple comma-separated extensions are listed, also try each in order.
		// E.g. Extension="SOD,SD2" supports both GOG (*.SOD) and original retail (*.SD2).
		string[] extensions = ParseExtensions(xml);
		if (extensions.Length > 1)
		{
			Func<string, bool> ciFileExists = fileExists;
			Func<string, Stream> ciOpenRead = openRead;
			fileExists = path => FileExistsWithExtensionFallback(path, extensions, ciFileExists);
			openRead = path => OpenReadWithExtensionFallback(path, extensions, ciFileExists, ciOpenRead);
		}
		XML = xml ?? throw new ArgumentNullException(nameof(xml));
		AudioT audioT = null;
		VgaGraph vgaGraph = null;
		VSwap vSwap = null;
		GameMap[] maps = null;
		Parallel.ForEach(
			source: new Action[] {
					() => audioT = LoadAudioT(xml, folder, openRead),
					() => vgaGraph = LoadVgaGraph(xml, folder, openRead),
					() => vSwap = LoadVSwap(xml, folder, openRead),
					() => maps = LoadMaps(xml, folder, openRead),
				},
			body: action => action());
		AudioT = audioT;
		VgaGraph = vgaGraph;
		VSwap = vSwap;
		Maps = maps;
		ILogger<MapAnalyzer> mapAnalyzerLogger = loggerFactory?.CreateLogger<MapAnalyzer>()
			?? NullLogger<MapAnalyzer>.Instance;
		MapAnalyzer = new MapAnalyzer(xml, VSwap?.SpritesByName, mapAnalyzerLogger);
		// Load pre-baked wall spawns if this game uses a WALLSPAWNS file.
		// Games like KOD assign different textures to each face of a block (n_wall, e_wall,
		// s_wall, w_wall in .BLK files). The standard Wolf3D tile-to-page formula can't
		// represent that, so those games bake wall spawns at conversion time instead.
		MapAnalyzer.MapAnalysis.WallSpawn[][] wallSpawnsByLevel = null;
		if (!string.IsNullOrEmpty(MapAnalyzer.WallSpawnsFile))
		{
			string wallSpawnsPath = Path.Combine(folder, MapAnalyzer.WallSpawnsFile);
			if (FileExists(wallSpawnsPath, fileExists))
				using (Stream wallSpawnsStream = OpenRead(wallSpawnsPath, openRead))
					wallSpawnsByLevel = Gameplay.WallSpawns.LoadAll(wallSpawnsStream);
		}
		MapAnalyses = wallSpawnsByLevel is not null
			? [.. maps.Select(map =>
				map.Number < wallSpawnsByLevel.Length
					? MapAnalyzer.Analyze(map, wallSpawnsByLevel[map.Number])
					: MapAnalyzer.Analyze(map))]
			: [.. MapAnalyzer.Analyze(maps)];
		// Load StateCollection from XML
		StateCollection = LoadStateCollection(xml);
		// Load WeaponCollection from XML
		WeaponCollection = LoadWeaponCollection(xml);
		// Load MenuCollection from XML
		MenuCollection = LoadMenuCollection(xml);
		// Load text chunks (external files + embedded VGAGRAPH chunks)
		TextChunks = LoadTextChunks(xml, folder, openRead, fileExists);
	}
	private static AudioT LoadAudioT(XElement xml, string folder, Func<string, Stream> openRead)
	{
		if (openRead is null)
			return AudioT.Load(xml, folder);
		XElement el = xml.Element("Audio");
		string head = el?.Attribute("AudioHead")?.Value,
			audioT = el?.Attribute("AudioT")?.Value;
		if (head is null || audioT is null)
			return null;
		using Stream audioHeadStream = OpenRead(Path.Combine(folder, head), openRead);
		using Stream audioTStream = OpenRead(Path.Combine(folder, audioT), openRead);
		return new AudioT(audioHeadStream, audioTStream, el);
	}
	private static VgaGraph LoadVgaGraph(XElement xml, string folder, Func<string, Stream> openRead)
	{
		if (openRead is null)
			return VgaGraph.Load(xml, folder);
		XElement el = xml.Element("VgaGraph");
		string head = el?.Attribute("VgaHead")?.Value,
			graph = el?.Attribute("VgaGraph")?.Value,
			dict = el?.Attribute("VgaDict")?.Value;
		if (head is null || graph is null || dict is null)
			return null;
		using Stream vgaHeadStream = OpenRead(Path.Combine(folder, head), openRead);
		using Stream vgaGraphStream = OpenRead(Path.Combine(folder, graph), openRead);
		using Stream vgaDictStream = OpenRead(Path.Combine(folder, dict), openRead);
		return new VgaGraph(vgaHeadStream, vgaGraphStream, vgaDictStream, xml);
	}
	private static VSwap LoadVSwap(XElement xml, string folder, Func<string, Stream> openRead)
	{
		if (openRead is null)
			return VSwap.Load(xml, folder);
		string name = xml.Element("VSwap")?.Attribute("Name")?.Value;
		if (name is null)
			return null;
		using Stream vSwapStream = OpenRead(Path.Combine(folder, name), openRead);
		return new VSwap(
			xml: xml,
			palette: VSwap.LoadPalette(xml),
			stream: vSwapStream,
			tileSqrt: ushort.TryParse(xml?.Element("VSwap")?.Attribute("Sqrt")?.Value, out ushort sqrt) ? sqrt : (ushort)64,
			fourBytePageLengths: bool.TryParse(xml?.Element("VSwap")?.Attribute("FourBytePageLengths")?.Value, out bool fourBytePageLengths) && fourBytePageLengths,
			rleSprites: !bool.TryParse(xml?.Element("VSwap")?.Attribute("RleSprites")?.Value, out bool rleSprites) || rleSprites);
	}
	private static GameMap[] LoadMaps(XElement xml, string folder, Func<string, Stream> openRead)
	{
		if (openRead is null)
			return GameMap.Load(xml, folder);
		XElement el = xml.Element("Maps");
		string head = el?.Attribute("MapHead")?.Value,
			gameMaps = el?.Attribute("GameMaps")?.Value;
		if (head is null || gameMaps is null)
			return [];
		using Stream mapHeadStream = OpenRead(Path.Combine(folder, head), openRead);
		using Stream gameMapsStream = OpenRead(Path.Combine(folder, gameMaps), openRead);
		return GameMap.Load(mapHeadStream, gameMapsStream);
	}
	private StateCollection LoadStateCollection(XElement xml)
	{
		StateCollection stateCollection = new();
		// Sprite name resolver - uses VSwap.SpritesByName dictionary
		short ResolveSpriteNameToNumber(string spriteName)
		{
			if (VSwap?.SpritesByName is not null && VSwap.SpritesByName.TryGetValue(spriteName, out ushort spriteNum))
				return (short)spriteNum;
			return -1; // Unknown sprite
		}
		// Find the Actors element inside VSwap
		XElement vswapElement = xml.Element("VSwap"),
			actorsElement = vswapElement?.Element("Actors");
		if (actorsElement is null)
			return stateCollection; // No states defined
									// Load state functions first
		IEnumerable<XElement> functionElements = actorsElement.Elements("Function");
		if (functionElements is not null)
			stateCollection.LoadFunctionsFromXml(functionElements);
		// Load states (Phase 1: Create all state objects)
		IEnumerable<XElement> stateElements = actorsElement.Elements("State");
		if (stateElements is not null)
			stateCollection.LoadStatesFromXml(stateElements, ResolveSpriteNameToNumber);
		// Load actor definitions (for death states, chase states, etc.)
		IEnumerable<XElement> actorElements = actorsElement.Elements("Actor");
		if (actorElements is not null)
			stateCollection.LoadActorDefinitionsFromXml(actorElements);
		// Load projectile definitions used by actor/weapon Lua SpawnProjectile() calls.
		IEnumerable<XElement> projectileElements = vswapElement?.Element("Projectiles")?.Elements("Projectile");
		if (projectileElements is not null)
			stateCollection.LoadProjectileDefinitionsFromXml(projectileElements);
		// Phase 2: Link state references
		stateCollection.LinkStates();
		// Note: ValidateFunctionReferences is NOT called here.
		// It runs in the Simulator constructor after default scripts are merged in,
		// so references to default scripts are valid before validation occurs.
		return stateCollection;
	}
	private static WeaponCollection LoadWeaponCollection(XElement xml)
	{
		WeaponCollection weaponCollection = new();
		// Find the GameplayWeapons element inside VSwap
		XElement vswapElement = xml.Element("VSwap"),
			weaponsElement = vswapElement?.Element("GameplayWeapons");
		if (weaponsElement is null)
			return weaponCollection; // No weapons defined
									 // Load weapon definitions
		IEnumerable<XElement> weaponElements = weaponsElement.Elements("GameplayWeapon");
		if (weaponElements is not null)
			weaponCollection.LoadFromXml(weaponElements);
		return weaponCollection;
	}
	private Dictionary<string, string> LoadTextChunks(XElement xml, string folder, Func<string, Stream> openRead, Func<string, bool> fileExists)
	{
		// Start with any embedded chunks already extracted by VgaGraph
		Dictionary<string, string> chunks = VgaGraph?.TextChunks is not null
			? new(VgaGraph.TextChunks)
			: [];
		// Load external file chunks (WL_TEXT.C: CA_LoadFile path for WL1 shareware)
		XElement vgaGraphElement = xml.Element("VgaGraph");
		foreach (XElement chunkEl in vgaGraphElement?.Element("TextChunks")?.Elements("TextChunk") ?? [])
		{
			string name = chunkEl.Attribute("Name")?.Value,
				fileName = chunkEl.Attribute("File")?.Value;
			if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(fileName))
				continue;
			string filePath = Path.Combine(folder, fileName);
			if (!FileExists(filePath, fileExists))
			{
				Console.Error.WriteLine($"Warning: TextChunk '{name}' file not found: {filePath}");
				continue;
			}
			using Stream stream = OpenRead(filePath, openRead);
			using StreamReader reader = new(stream, System.Text.Encoding.ASCII);
			chunks[name] = reader.ReadToEnd();
		}
		return chunks;
	}
	private static string ResolveCaseInsensitive(string path, Func<string, bool> fileExists)
	{
		if (fileExists(path)) return path;
		string dir = Path.GetDirectoryName(path);
		string fileName = Path.GetFileName(path);
		if (string.IsNullOrEmpty(fileName)) return path;
		string effectiveDir = string.IsNullOrEmpty(dir) ? "." : dir;
		if (!Directory.Exists(effectiveDir)) return path;
		try
		{
			return Directory.EnumerateFiles(effectiveDir)
				.FirstOrDefault(f => string.Equals(Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase))
				?? path;
		}
		catch { return path; }
	}
	private static string[] ParseExtensions(XElement xml) =>
		(xml.Attribute("Extension")?.Value ?? "")
			.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
	private static bool FileExistsWithExtensionFallback(string path, string[] extensions, Func<string, bool> fileExists) =>
		fileExists(path) ||
		extensions.Any(ext => fileExists(Path.Combine(
			Path.GetDirectoryName(path) ?? "",
			Path.GetFileNameWithoutExtension(path) + "." + ext)));
	private static Stream OpenReadWithExtensionFallback(string path, string[] extensions, Func<string, bool> fileExists, Func<string, Stream> openRead)
	{
		if (fileExists(path)) return openRead(path);
		foreach (string ext in extensions)
		{
			string candidate = Path.Combine(
				Path.GetDirectoryName(path) ?? "",
				Path.GetFileNameWithoutExtension(path) + "." + ext);
			if (fileExists(candidate)) return openRead(candidate);
		}
		return openRead(path); // let original path produce the FileNotFoundException
	}
	private static bool FileExists(string path, Func<string, bool> fileExists) => fileExists?.Invoke(path) ?? File.Exists(path);
	private static Stream OpenRead(string path, Func<string, Stream> openRead) => openRead?.Invoke(path) ?? File.OpenRead(path);
	private static MenuCollection LoadMenuCollection(XElement xml) =>
		xml?.Element("VgaGraph")?.Element("Menus") is XElement menusElement
			? MenuCollection.Load(menusElement)
			: new MenuCollection();
}
