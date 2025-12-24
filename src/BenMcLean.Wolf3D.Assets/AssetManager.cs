using System;
using System.IO;
using System.Xml.Linq;

namespace BenMcLean.Wolf3D.Assets;

public class AssetManager
{
	public readonly XElement XML;
	public readonly AudioT AudioT;
	public readonly VgaGraph VgaGraph;
	public readonly VSwap VSwap;
	public readonly GameMap[] Maps;
	public readonly MapAnalyzer.MapAnalysis[] MapAnalyses;
	public static AssetManager Load(string xmlPath)
	{
		XElement xml = XDocument.Load(xmlPath).Root;
		return new(xml, Path.Combine(Path.GetDirectoryName(xmlPath), xml.Attribute("Path")?.Value));
	}
	public AssetManager(XElement xml, string folder = "")
	{
		if (!Directory.Exists(folder))
			throw new DirectoryNotFoundException(folder);
		XML = xml ?? throw new ArgumentNullException(nameof(xml));
		AudioT = AudioT.Load(xml, folder);
		VgaGraph = VgaGraph.Load(xml, folder);
		VSwap = VSwap.Load(xml, folder);
		Maps = GameMap.Load(xml, folder);
		MapAnalyses = [.. new MapAnalyzer(xml).Analyze(Maps)];
	}
}
