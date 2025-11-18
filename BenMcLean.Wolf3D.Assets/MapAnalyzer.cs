using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace BenMcLean.Wolf3D.Assets;

public class MapAnalyzer
{
	#region Data
	public XElement XML;
	public HashSet<ushort> Walls { get; private set; }
	public HashSet<ushort> Doors { get; private set; }
	public HashSet<ushort> Elevators { get; private set; }
	public HashSet<ushort> PushWalls { get; private set; }
	public ushort FloorCodeFirst { get; private set; }
	public ushort FloorCodes { get; private set; }
	#endregion Data
	public MapAnalyzer(XElement xml)
	{
		XML = xml;
		Walls = [.. XML.Element("VSwap")?.Element("Walls")?.Elements("Wall").Select(e => ushort.Parse(e.Attribute("Number").Value))];
		Doors = [.. XML.Element("VSwap")?.Element("Walls")?.Elements("Door")?.Select(e => ushort.Parse(e.Attribute("Number").Value))];
		Elevators = [.. XML.Element("VSwap")?.Element("Walls")?.Elements("Elevator")?.Select(e => ushort.Parse(e.Attribute("Number").Value))];
		PushWalls = [.. PushWall?.Select(e => ushort.Parse(e.Attribute("Number").Value))];
		if (ushort.TryParse(XML?.Element("VSwap")?.Element("Walls")?.Attribute("FloorCodeFirst")?.Value, out ushort floorCodeFirst))
			FloorCodeFirst = floorCodeFirst;
		if (ushort.TryParse(XML?.Element("VSwap")?.Element("Walls")?.Attribute("FloorCodeLast")?.Value, out ushort floorCodeLast))
			FloorCodes = (ushort)(1 + floorCodeLast - FloorCodeFirst);
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
	#region Inner classes
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
		#region Data
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
		public record struct Wall(ushort Shape, bool Western, ushort X, ushort Z, bool Flip = false);
		public ReadOnlyCollection<Wall> Walls { get; private set; }
		public record struct PushWall(ushort Shape, ushort DarkSide, ushort X, ushort Z);
		public ReadOnlyCollection<PushWall> PushWalls { get; private set; }
		public ReadOnlyCollection<Tuple<ushort, ushort>> Elevators { get; private set; }
		public record struct Door(ushort Shape, ushort X, ushort Z, bool Western = false);
		public ReadOnlyCollection<Door> Doors { get; private set; }
		#endregion Data
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
			List<PushWall> pushWalls = [];
			// realWalls replaces pushwalls with floors.
			ushort[] realWalls = new ushort[gameMap.MapData.Length];
			Array.Copy(gameMap.MapData, realWalls, realWalls.Length);
			for (int i = 0; i < realWalls.Length; i++)
				if (mapAnalyzer.PushWalls.Contains(gameMap.ObjectData[i]))
				{
					realWalls[i] = mapAnalyzer.FloorCodeFirst;
					ushort wall = gameMap.MapData[i];
					pushWalls.Add(new PushWall(mapAnalyzer.WallPage(wall), mapAnalyzer.DarkSide(wall), gameMap.X(i), gameMap.Z(i)));
				}
			ushort GetMapData(ushort x, ushort z) => realWalls[gameMap.GetIndex(x, z)];
			XElement doorFrameX = mapAnalyzer.XML.Element("VSwap")?.Element("Walls")?.Element("DoorFrame")
				?? throw new NullReferenceException("Could not find \"DoorFrame\" tag in walls!");
			ushort doorFrame = (ushort)(uint)doorFrameX.Attribute("Page"),
				darkFrame = (ushort)(uint)doorFrameX.Attribute("DarkSide");
			List<Wall> walls = [];
			List<Tuple<ushort, ushort>> elevators = [];
			List<Door> doors = [];
			void EastWest(ushort x, ushort z)
			{
				ushort wall;
				if (x < Width - 1 && mapAnalyzer.Walls.Contains(wall = GetMapData((ushort)(x + 1), z)))
					walls.Add(new Wall(mapAnalyzer.DarkSide(wall), false, (ushort)(x + 1), z));
				if (x > 0 && mapAnalyzer.Walls.Contains(wall = GetMapData((ushort)(x - 1), z)))
					walls.Add(new Wall(mapAnalyzer.DarkSide(wall), false, x, z, true));
			}
			void NorthSouth(ushort x, ushort z)
			{
				ushort wall;
				if (z > 0 && mapAnalyzer.Walls.Contains(wall = GetMapData(x, (ushort)(z - 1))))
					walls.Add(new Wall(mapAnalyzer.WallPage(wall), true, x, (ushort)(z - 1), true));
				if (z < Depth - 1 && mapAnalyzer.Walls.Contains(wall = GetMapData(x, (ushort)(z + 1))))
					walls.Add(new Wall(mapAnalyzer.WallPage(wall), true, x, z));
			}
			for (ushort i = 0; i < gameMap.MapData.Length; i++)
			{
				ushort x = gameMap.X(i), z = gameMap.Z(i), here = GetMapData(x, z);
				if (mapAnalyzer.Doors.Contains(here))
				{
					if (here % 2 == 0) // Even numbered doors face east
					{
						walls.Add(new Wall(doorFrame, true, x, z));
						walls.Add(new Wall(doorFrame, true, x, (ushort)(z - 1), true));
						EastWest(x, z);
						doors.Add(new Door(here, x, z, true));
					}
					else // Odd numbered doors face north
					{
						walls.Add(new Wall(darkFrame, false, x, z, true));
						walls.Add(new Wall(darkFrame, false, (ushort)(x + 1), z));
						NorthSouth(x, z);
						doors.Add(new Door(here, x, z));
					}
				}
				else if (mapAnalyzer.Elevators.Contains(here))
					elevators.Add(new(x, z));
				else if (!mapAnalyzer.Walls.Contains(here))
				{
					EastWest(x, z);
					NorthSouth(x, z);
				}
			}
			Walls = Array.AsReadOnly([.. walls]);
			Elevators = Array.AsReadOnly([.. elevators]);
			Doors = Array.AsReadOnly([.. doors]);
		}
	}
	#endregion Inner classes
}
