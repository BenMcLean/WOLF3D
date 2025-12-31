using System;
using System.IO;
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
		AudioT = AudioT.Load(xml, folder);
		VgaGraph = VgaGraph.Load(xml, folder);
		VSwap = VSwap.Load(xml, folder);
		Maps = GameMap.Load(xml, folder);
		// Create logger for MapAnalyzer
		ILogger<MapAnalyzer> mapAnalyzerLogger = loggerFactory?.CreateLogger<MapAnalyzer>()
			?? NullLogger<MapAnalyzer>.Instance;
		MapAnalyzer = new MapAnalyzer(xml, VSwap.SpritesByName, mapAnalyzerLogger);
		MapAnalyses = [.. MapAnalyzer.Analyze(Maps)];
		// Load StateCollection from XML
		StateCollection = LoadStateCollection(xml);
		// Load MenuCollection from XML
		MenuCollection = LoadMenuCollection(xml);
	}
	private StateCollection LoadStateCollection(XElement xml)
	{
		StateCollection stateCollection = new StateCollection();
		// Sprite name resolver - uses VSwap.SpritesByName dictionary
		short ResolveSpriteNameToNumber(string spriteName)
		{
			if (VSwap?.SpritesByName != null && VSwap.SpritesByName.TryGetValue(spriteName, out ushort spriteNum))
				return (short)spriteNum;
			return -1; // Unknown sprite
		}
		// Find the StatInfo element inside VSwap
		XElement vswapElement = xml.Element("VSwap");
		XElement statInfoElement = vswapElement?.Element("StatInfo");
		if (statInfoElement == null)
			return stateCollection; // No states defined
		// Load state functions first
		var functionElements = statInfoElement.Elements("Function");
		if (functionElements != null)
			stateCollection.LoadFunctionsFromXml(functionElements);
		// Load states (Phase 1: Create all state objects)
		var stateElements = statInfoElement.Elements("State");
		if (stateElements != null)
			stateCollection.LoadStatesFromXml(stateElements, ResolveSpriteNameToNumber);
		// Phase 2: Link state references
		stateCollection.LinkStates();
		// Phase 3: Validate function references
		stateCollection.ValidateFunctionReferences();
		return stateCollection;
	}
	private MenuCollection LoadMenuCollection(XElement xml)
	{
		// Find the Menus element inside VgaGraph
		XElement vgaGraphElement = xml.Element("VgaGraph");
		XElement menusElement = vgaGraphElement?.Element("Menus");
		if (menusElement == null)
			return new MenuCollection(); // No menus defined
		// Use MenuCollection.Load static method to parse entire structure
		return MenuCollection.Load(menusElement);
	}
}
