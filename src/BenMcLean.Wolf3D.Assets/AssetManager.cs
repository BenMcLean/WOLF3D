using System;
using System.IO;
using System.Xml.Linq;
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
	public readonly MapAnalyzer.MapAnalysis[] MapAnalyses;
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
		MapAnalyses = [.. new MapAnalyzer(xml, VSwap.SpritesByName, mapAnalyzerLogger).Analyze(Maps)];
	}
}
