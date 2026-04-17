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
		XElement xml = XDocument.Load(xmlPath).Root;
		return new(xml, Path.Combine(Path.GetDirectoryName(xmlPath), xml.Attribute("Path")?.Value), loggerFactory);
	}
	public AssetManager(XElement xml, string folder = "", ILoggerFactory loggerFactory = null)
	{
		if (!Directory.Exists(folder))
			throw new DirectoryNotFoundException(folder);
		XML = xml ?? throw new ArgumentNullException(nameof(xml));
		AudioT audioT = null;
		VgaGraph vgaGraph = null;
		VSwap vSwap = null;
		GameMap[] maps = null;
		Parallel.ForEach(
			source: new Action[] {
					() => audioT = AudioT.Load(xml, folder),
					() => vgaGraph = VgaGraph.Load(xml, folder),
					() => vSwap = VSwap.Load(xml, folder),
					() => maps = GameMap.Load(xml, folder),
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
			if (File.Exists(wallSpawnsPath))
				wallSpawnsByLevel = Gameplay.WallSpawns.LoadAll(wallSpawnsPath);
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
		TextChunks = LoadTextChunks(xml, folder);
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
		XElement vswapElement = xml.Element("VSwap");
		XElement actorsElement = vswapElement?.Element("Actors");
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
		XElement vswapElement = xml.Element("VSwap");
		XElement weaponsElement = vswapElement?.Element("GameplayWeapons");
		if (weaponsElement is null)
			return weaponCollection; // No weapons defined
									 // Load weapon definitions
		IEnumerable<XElement> weaponElements = weaponsElement.Elements("GameplayWeapon");
		if (weaponElements is not null)
			weaponCollection.LoadFromXml(weaponElements);
		return weaponCollection;
	}
	private Dictionary<string, string> LoadTextChunks(XElement xml, string folder)
	{
		// Start with any embedded chunks already extracted by VgaGraph
		Dictionary<string, string> chunks = VgaGraph?.TextChunks is not null
			? new(VgaGraph.TextChunks)
			: [];
		// Load external file chunks (WL_TEXT.C: CA_LoadFile path for WL1 shareware)
		XElement vgaGraphElement = xml.Element("VgaGraph");
		foreach (XElement chunkEl in vgaGraphElement?.Element("TextChunks")?.Elements("TextChunk") ?? [])
		{
			string name = chunkEl.Attribute("Name")?.Value;
			string fileName = chunkEl.Attribute("File")?.Value;
			if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(fileName))
				continue;
			string filePath = Path.Combine(folder, fileName);
			if (!File.Exists(filePath))
			{
				Console.Error.WriteLine($"Warning: TextChunk '{name}' file not found: {filePath}");
				continue;
			}
			chunks[name] = File.ReadAllText(filePath, System.Text.Encoding.ASCII);
		}
		return chunks;
	}
	private static MenuCollection LoadMenuCollection(XElement xml) =>
		xml?.Element("VgaGraph")?.Element("Menus") is XElement menusElement
			? MenuCollection.Load(menusElement)
			: new MenuCollection();
}
