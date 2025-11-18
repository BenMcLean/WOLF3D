using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace BenMcLean.Wolf3D.Assets;

public class MapAnalyzer
{
	public XElement XML;
	public ushort[] Walls { get; private set; }
	public ushort[] Doors { get; private set; }
	public ushort[] Elevators { get; private set; }
	public ushort[] PushWalls { get; private set; }
	public MapAnalyzer(XElement xml)
	{
		XML = xml;
		Walls = XML.Element("VSwap")?.Element("Walls")?.Elements("Wall").Select(e => ushort.Parse(e.Attribute("Number").Value)).ToArray();
		Doors = XML.Element("VSwap")?.Element("Walls")?.Elements("Door")?.Select(e => ushort.Parse(e.Attribute("Number").Value))?.ToArray();
		Elevators = XML.Element("VSwap")?.Element("Walls")?.Elements("Elevator")?.Select(e => ushort.Parse(e.Attribute("Number").Value))?.ToArray();
		PushWalls = PushWall?.Select(e => ushort.Parse(e.Attribute("Number").Value))?.ToArray();
	}
	public ushort MapNumber(ushort episode, ushort floor) => ushort.Parse(XML.Element("Maps").Elements("Map").Where(map =>
			ushort.TryParse(map.Attribute("Episode")?.Value, out ushort e) && e == episode
			&& ushort.TryParse(map.Attribute("Floor")?.Value, out ushort f) && f == floor
		).First().Attribute("Number")?.Value);
	public XElement Elevator(ushort number) => XML?.Element("VSwap")?.Element("Walls")?.Elements("Elevator")?.Where(e => ushort.TryParse(e.Attribute("Number")?.Value, out ushort elevator) && elevator == number)?.FirstOrDefault();
	public XElement Wall(ushort number) => XML?.Element("VSwap")?.Element("Walls")?.Elements("Wall")?.Where(e => ushort.TryParse(e.Attribute("Number")?.Value, out ushort wall) && wall == number)?.FirstOrDefault();
	public IEnumerable<XElement> PushWall => XML?.Element("VSwap")?.Element("Objects")?.Elements("Pushwall");
	public ushort WallPage(ushort cell) =>
		ushort.TryParse(Wall(cell)?.Attribute("Page")?.Value, out ushort result) ? result : throw new InvalidDataException("Could not find wall texture " + cell + "!");
	public bool IsNavigable(ushort mapData, ushort objectData) =>
		IsTransparent(mapData, objectData) && (
			!(XML?.Element("VSwap")?.Element("Objects").Elements("Billboard")
				.Where(e => uint.TryParse(e.Attribute("Number")?.Value, out uint number) && number == objectData).FirstOrDefault() is XElement mapObject)
			|| mapObject.IsTrue("Walk"));
	public bool IsTransparent(ushort mapData, ushort objectData) =>
		(!Walls.Contains(mapData) || PushWalls.Contains(objectData))
		&& !Elevators.Contains(mapData);
	public bool IsMappable(GameMap map, ushort x, ushort z) =>
		IsTransparent(map.GetMapData(x, z), map.GetObjectData(x, z))
		|| (x > 0 && IsTransparent(map.GetMapData((ushort)(x - 1), z), map.GetObjectData((ushort)(x - 1), z)))
		|| (x < map.Width - 1 && IsTransparent(map.GetMapData((ushort)(x + 1), z), map.GetObjectData((ushort)(x + 1), z)))
		|| (z > 0 && IsTransparent(map.GetMapData(x, (ushort)(z - 1)), map.GetObjectData(x, (ushort)(z - 1))))
		|| (z < map.Depth - 1 && IsTransparent(map.GetMapData(x, (ushort)(z + 1)), map.GetObjectData(x, (ushort)(z + 1))));
	/// <summary>
	/// "If you only knew the power of the Dark Side." - Darth Vader
	/// </summary>
	public ushort DarkSide(ushort cell) =>
		ushort.TryParse(XWall(cell).FirstOrDefault()?.Attribute("DarkSide")?.Value, out ushort result) ? result : WallPage(cell);
	public IEnumerable<XElement> XWall(ushort cell) =>
		XML?.Element("VSwap")?.Element("Walls")?.Elements()
		?.Where(e => (uint)e.Attribute("Number") == cell);
	public IEnumerable<XElement> XDoor(ushort cell) =>
		XML?.Element("VSwap")?.Element("Walls")?.Elements("Door")
		?.Where(e => (uint)e.Attribute("Number") == cell);
	public ushort DoorTexture(ushort cell) =>
		(ushort)(uint)XDoor(cell).FirstOrDefault()?.Attribute("Page");
	public MapAnalysis Analyze(GameMap map) => new(this, map);
	public IEnumerable<MapAnalysis> Analyze(params GameMap[] maps) => maps.Select(Analyze);
	public sealed class MapAnalysis
	{
		#region XML Attributes
		public byte Episode { get; private init; }
		public byte Floor { get; private init; }
		public byte ElevatorTo { get; private init; }
		public byte? Ground { get; private init; }
		public ushort? GroundTile { get; private init; }
		public byte? Ceiling { get; private init; }
		public ushort? CeilingTile { get; private init; }
		public byte Border { get; private init; }
		public TimeSpan Par { get; private init; }
		public string Song { get; private init; }
		#endregion XML Attributes
		#region Masks
		public ushort Width { get; private init; }
		public const ushort Height = 0; // Vertical
		public ushort Depth { get; private init; }
		private readonly BitArray Navigable;
		public bool IsNavigable(int x, int z) =>
			x >= 0 && z >= 0 && x < Width && z < Depth
			&& Navigable[x * Depth + z];
		private readonly BitArray Transparent;
		public bool IsTransparent(int x, int z) =>
			x >= 0 && z >= 0 && x < Width && z < Depth
			&& Transparent[x * Depth + z];
		private readonly BitArray Mappable;
		public bool IsMappable(int x, int z) =>
			x >= 0 && z >= 0 && x < Width && z < Depth
			&& Mappable[x * Depth + z];
		#endregion Masks
		public MapAnalysis(MapAnalyzer mapAnalyzer, GameMap gameMap)
		{
			#region XML Attributes
			XElement xml = mapAnalyzer.XML.Element("Maps").Elements("Map").Where(m => ushort.TryParse(m.Attribute("Number")?.Value, out ushort mu) && mu == gameMap.Number).FirstOrDefault()
				?? throw new InvalidDataException($"XML tag for map \"{gameMap.Name}\" was not found!");
			Episode = byte.TryParse(xml?.Attribute("Episode")?.Value, out byte episode) ? episode : (byte)0;
			Floor = byte.TryParse(xml?.Attribute("Floor")?.Value, out byte floor) ? floor : (byte)0;
			ElevatorTo = byte.TryParse(xml.Attribute("ElevatorTo")?.Value, out byte elevatorTo) ? elevatorTo : (byte)(Floor + 1);
			Ground = byte.TryParse(xml?.Attribute("Ground")?.Value, out byte ground) ? ground : null;
			GroundTile = byte.TryParse(xml?.Attribute("GroundTile")?.Value, out byte groundTile) ? groundTile : (byte?)null;
			Ceiling = byte.TryParse(xml?.Attribute("Ceiling")?.Value, out byte ceiling) ? ceiling : null;
			CeilingTile = byte.TryParse(xml?.Attribute("CeilingTile")?.Value, out byte ceilingTile) ? ceilingTile : null;
			Border = byte.TryParse(xml?.Attribute("Border")?.Value, out byte border) ? border : (byte)0;
			Par = TimeSpan.TryParse(xml?.Attribute("Par")?.Value, out TimeSpan par) ? par : TimeSpan.Zero;
			Song = xml.Attribute("Song")?.Value;
			#endregion XML Attributes
			#region Masks
			Width = gameMap.Width;
			Depth = gameMap.Depth;
			Navigable = new BitArray(Width * Depth);
			Transparent = new BitArray(Navigable.Length);
			for (ushort x = 0; x < Width; x++)
				for (ushort z = 0; z < Depth; z++)
				{
					Navigable[x * Depth + z] = mapAnalyzer.IsNavigable(gameMap.GetMapData(x, z), gameMap.GetObjectData(x, z));
					Transparent[x * Depth + z] = mapAnalyzer.IsTransparent(gameMap.GetMapData(x, z), gameMap.GetObjectData(x, z));
				}
			Mappable = new BitArray(Navigable.Length);
			for (ushort x = 0; x < Width; x++)
				for (ushort z = 0; z < Depth; z++)
					Mappable[x * Depth + z] = Transparent[x * Depth + z]
						|| (x > 0 && Transparent[(x - 1) * Depth + z])
						|| (x < Width - 1 && Transparent[(x + 1) * Depth + z])
						|| (z > 0 && Transparent[x * Depth + (z - 1)])
						|| (z < Depth - 1 && Transparent[x * Depth + (z + 1)]);
			#endregion Masks
		}
	}
}
