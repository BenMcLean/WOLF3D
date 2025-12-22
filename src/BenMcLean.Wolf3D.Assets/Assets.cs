using System;
using System.IO;
using System.Xml.Linq;

namespace BenMcLean.Wolf3D.Assets;

public class Assets
{
	public readonly XElement XML;
	public readonly AudioT AudioT;
	public readonly GameMap[] Maps;
	public readonly VgaGraph VgaGraph;
	public readonly VSwap VSwap;
	public readonly MapAnalyzer.MapAnalysis[] MapAnalyses;
	public static Assets Load(string xmlPath)
	{
		XElement xml = XDocument.Load(xmlPath).Root;
		return new(xml, Path.Combine(Path.GetDirectoryName(xmlPath), xml.Attribute("Path")?.Value));
	}
	public Assets(XElement xml, string folder = "")
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
