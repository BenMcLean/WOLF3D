using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using BenMcLean.Wolf3D.Assets.Gameplay;
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
	private MenuCollection LoadMenuCollection(XElement xml)
	{
		// Find the Menus element inside VgaGraph
		XElement vgaGraphElement = xml.Element("VgaGraph");
		XElement menusElement = vgaGraphElement?.Element("Menus");
		if (menusElement is null)
			return new MenuCollection(); // No menus defined
										 // Use MenuCollection.Load static method to parse entire structure
		return MenuCollection.Load(menusElement);
	}
}
