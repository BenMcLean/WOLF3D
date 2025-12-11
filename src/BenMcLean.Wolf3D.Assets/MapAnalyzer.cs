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

	// Wall configuration (from WOLF3D.xsd Walls attributes)
	public ushort MaxWallTiles { get; private set; }
	public ushort DoorWall { get; private set; }

	// Special tiles (from WOLF3D.xsd special tile elements - can have multiple)
	public HashSet<ushort> ElevatorTiles { get; private set; }
	public HashSet<ushort> PushableTiles { get; private set; }
	public HashSet<ushort> ExitTiles { get; private set; }
	public HashSet<ushort> AmbushTiles { get; private set; }
	public HashSet<ushort> AltElevatorTiles { get; private set; }

	// Door types (Object number -> door info)
	public Dictionary<ushort, DoorInfo> Doors { get; private set; }

	// Object metadata
	public Dictionary<ushort, ObjectInfo> Objects { get; private set; }

	// Patrol tile metadata (map layer)
	public Dictionary<ushort, Direction> PatrolTiles { get; private set; }

	public ushort FloorCodeFirst { get; private set; }
	public ushort FloorCodes { get; private set; }
	#endregion Data
	public MapAnalyzer(XElement xml)
	{
		XML = xml ?? throw new ArgumentNullException(nameof(xml));

		// Parse WallPlane element attributes (required)
		XElement wallsElement = XML.Element("VSwap")?.Element("WallPlane")
			?? throw new InvalidDataException("Missing VSwap/WallPlane element in XML");

		MaxWallTiles = ushort.Parse(wallsElement.Attribute("MaxWallTiles")?.Value
			?? throw new InvalidDataException("Missing MaxWallTiles attribute"));
		DoorWall = ushort.Parse(wallsElement.Attribute("DoorWall")?.Value
			?? throw new InvalidDataException("Missing DoorWall attribute"));

		// Parse special floor tiles (can have multiple of each type)
		ElevatorTiles = [.. wallsElement.Elements("Elevator")
			.Select(e => ushort.Parse(e.Attribute("Tile")?.Value
				?? throw new InvalidDataException("Elevator element missing Tile attribute")))];
		PushableTiles = [.. wallsElement.Elements("Pushable")
			.Select(e => ushort.Parse(e.Attribute("Tile")?.Value
				?? throw new InvalidDataException("Pushable element missing Tile attribute")))];
		ExitTiles = [.. wallsElement.Elements("Exit")
			.Select(e => ushort.Parse(e.Attribute("Tile")?.Value
				?? throw new InvalidDataException("Exit element missing Tile attribute")))];
		AmbushTiles = [.. wallsElement.Elements("Ambush")
			.Select(e => ushort.Parse(e.Attribute("Tile")?.Value
				?? throw new InvalidDataException("Ambush element missing Tile attribute")))];
		AltElevatorTiles = [.. wallsElement.Elements("AltElevator")
			.Select(e => ushort.Parse(e.Attribute("Tile")?.Value
				?? throw new InvalidDataException("AltElevator element missing Tile attribute")))];

		// Parse door types
		Doors = new Dictionary<ushort, DoorInfo>();
		foreach (XElement doorElem in wallsElement.Elements("Door"))
		{
			ushort tileNum = ushort.Parse(doorElem.Attribute("Tile")?.Value
				?? throw new InvalidDataException("Door element missing Tile attribute"));
			DoorInfo info = new()
			{
				TileNumber = tileNum,
				Name = doorElem.Attribute("Name")?.Value
					?? throw new InvalidDataException("Door element missing Name attribute"),
				Key = doorElem.Attribute("Key")?.Value,  // Optional
				Page = ushort.Parse(doorElem.Attribute("Page")?.Value
					?? throw new InvalidDataException("Door element missing Page attribute")),
				OpenSound = doorElem.Attribute("OpenSound")?.Value,  // Optional
				CloseSound = doorElem.Attribute("CloseSound")?.Value  // Optional
			};
			// Store both even (vertical) and odd (horizontal) tile numbers
			Doors[tileNum] = info;
			Doors[(ushort)(tileNum + 1)] = info;
		}

		// Parse object metadata - unified <ObjectType> elements
		IEnumerable<XElement> objectElements = XML.Element("VSwap")?.Element("StatInfo")?.Elements("ObjectType") ?? [];
		Objects = [];

		foreach (XElement obj in objectElements)
		{
			if (!ushort.TryParse(obj.Attribute("Number")?.Value, out ushort number))
				continue;

			Direction? facing = null;
			if (Enum.TryParse<Direction>(obj.Attribute("Facing")?.Value, out Direction dir))
				facing = dir;

			// Parse ObClass case-insensitively
			ObClass? objectClass = null;
			string obclassStr = obj.Attribute("ObClass")?.Value;
			if (!string.IsNullOrEmpty(obclassStr) && Enum.TryParse<ObClass>(obclassStr, ignoreCase: true, out ObClass parsedClass))
				objectClass = parsedClass;

			// Parse sprite page number (optional for some object types like player start)
			ushort.TryParse(obj.Attribute("Page")?.Value, out ushort page);

			ObjectInfo info = new()
			{
				Number = number,
				ObjectClass = objectClass,
				Page = page,
				Facing = facing,
				Patrol = obj.IsTrue("Patrol"),
				Ambush = obj.IsTrue("Ambush"),
				IsEnemy = false,  // TODO: Set based on object class when enemy types are added
				IsActive = false  // TODO: Set based on object class when active types are added
			};

			Objects[number] = info;
		}

		// Patrol tiles (map layer)
		PatrolTiles = new Dictionary<ushort, Direction>();
		IEnumerable<XElement> patrolElements = XML.Element("Maps")?.Element("PatrolTiles")?.Elements("Tile") ?? [];
		foreach (XElement tile in patrolElements)
		{
			if (ushort.TryParse(tile.Attribute("Number")?.Value, out ushort tileNum)
				&& Enum.TryParse<Direction>(tile.Attribute("Turn")?.Value, out Direction turnDir))
			{
				PatrolTiles[tileNum] = turnDir;
			}
		}

		if (ushort.TryParse(XML?.Element("VSwap")?.Element("WallPlane")?.Attribute("FloorCodeFirst")?.Value, out ushort floorCodeFirst))
			FloorCodeFirst = floorCodeFirst;
		if (ushort.TryParse(XML?.Element("VSwap")?.Element("WallPlane")?.Attribute("FloorCodeLast")?.Value, out ushort floorCodeLast))
			FloorCodes = (ushort)(1 + floorCodeLast - FloorCodeFirst);
	}
	public ushort MapNumber(ushort episode, ushort level) => ushort.Parse(XML.Element("Maps").Elements("Map").Where(map =>
			ushort.TryParse(map.Attribute("Episode")?.Value, out ushort e) && e == episode
			&& ushort.TryParse(map.Attribute("Level")?.Value, out ushort l) && l == level
		).First().Attribute("Number")?.Value);

	// Wall formula from WL_MAIN.C SetupWalls():
	// horizwall[i] = (i-1) * 2
	// vertwall[i] = (i-1) * 2 + 1
	public ushort GetWallPage(ushort wallNumber, bool vertical) =>
		(ushort)((wallNumber - 1) * 2 + (vertical ? 1 : 0));

	// Check if tile number is a wall
	public bool IsWall(ushort tile) => tile > 0 && tile <= MaxWallTiles;

	public bool IsNavigable(ushort mapData, ushort objectData) =>
		IsTransparent(mapData, objectData) && (
			!Objects.TryGetValue(objectData, out ObjectInfo objInfo)
			|| objInfo.ObjectClass == ObClass.dressing);

	public bool IsTransparent(ushort mapData, ushort objectData) =>
		(!IsWall(mapData) || PushableTiles.Contains(objectData))
		&& !ElevatorTiles.Contains(mapData);

	public bool IsMappable(GameMap map, ushort x, ushort z) =>
		IsTransparent(map.GetMapData(x, z), map.GetObjectData(x, z))
		|| (x > 0 && IsTransparent(map.GetMapData((ushort)(x - 1), z), map.GetObjectData((ushort)(x - 1), z)))
		|| (x < map.Width - 1 && IsTransparent(map.GetMapData((ushort)(x + 1), z), map.GetObjectData((ushort)(x + 1), z)))
		|| (z > 0 && IsTransparent(map.GetMapData(x, (ushort)(z - 1)), map.GetObjectData(x, (ushort)(z - 1))))
		|| (z < map.Depth - 1 && IsTransparent(map.GetMapData(x, (ushort)(z + 1)), map.GetObjectData(x, (ushort)(z + 1))));
	public ObjectInfo GetObjectInfo(ushort objectCode) =>
		Objects.TryGetValue(objectCode, out ObjectInfo info) ? info : null;

	// Wolf3D object class categorization (using enum)
	private static bool IsPlayerStart(ObClass? obclass) => obclass == ObClass.playerobj;
	private static bool IsEnemy(ObClass? obclass) => false; // TODO: Add enemy types to enum
	private static bool IsStatic(ObClass? obclass) => obclass == ObClass.dressing || obclass == ObClass.block;

	private static StatType GetStatType(ObClass? obclass) => obclass switch
	{
		ObClass.dressing => StatType.dressing,
		ObClass.block => StatType.block,
		// bonus items will be added to enum as needed
		_ => StatType.dressing // default
	};

	public MapAnalysis Analyze(GameMap map) => new(this, map);
	public IEnumerable<MapAnalysis> Analyze(params GameMap[] maps) => maps.Select(Analyze);
	#region Inner classes
	public sealed class MapAnalysis
	{
		#region XML Attributes
		public byte Episode { get; private init; }
		public byte Level { get; private init; }
		public byte ElevatorTo { get; private init; }
		public byte? Floor { get; private init; }
		public ushort? FloorTile { get; private init; }
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

		// Spawn data
		public readonly record struct PlayerSpawn(ushort X, ushort Z, Direction Facing);
		public PlayerSpawn? PlayerStart { get; private set; }

		public readonly record struct EnemySpawn(ObClass Type, ushort X, ushort Z, Direction Facing, bool Ambush, bool Patrol);
		public ReadOnlyCollection<EnemySpawn> EnemySpawns { get; private set; }

		public readonly record struct StaticSpawn(StatType StatType, ObClass Type, ushort Page, ushort X, ushort Z);
		public ReadOnlyCollection<StaticSpawn> StaticSpawns { get; private set; }

		public readonly record struct PatrolPoint(ushort X, ushort Z, Direction Turn);
		public ReadOnlyCollection<PatrolPoint> PatrolPoints { get; private set; }

		public readonly record struct WallSpawn(ushort Shape, bool Western, ushort X, ushort Z, bool Flip = false);
		public ReadOnlyCollection<WallSpawn> Walls { get; private set; }
		public readonly record struct PushWallSpawn(ushort Shape, ushort DarkSide, ushort X, ushort Z);
		public ReadOnlyCollection<PushWallSpawn> PushWalls { get; private set; }
		public ReadOnlyCollection<Tuple<ushort, ushort>> Elevators { get; private set; }
		public readonly record struct DoorSpawn(ushort Shape, ushort X, ushort Z, bool Western = false);
		public ReadOnlyCollection<DoorSpawn> Doors { get; private set; }
		#endregion Data
		public MapAnalysis(MapAnalyzer mapAnalyzer, GameMap gameMap)
		{
			#region XML Attributes
			XElement xml = mapAnalyzer.XML.Element("Maps").Elements("Map").Where(m => ushort.TryParse(m.Attribute("Number")?.Value, out ushort mu) && mu == gameMap.Number).FirstOrDefault()
				?? throw new InvalidDataException($"XML tag for map \"{gameMap.Name}\" was not found!");
			Episode = byte.TryParse(xml?.Attribute("Episode")?.Value, out byte episode) ? episode : (byte)0;
			Level = byte.TryParse(xml?.Attribute("Level")?.Value, out byte level) ? level : (byte)0;
			ElevatorTo = byte.TryParse(xml.Attribute("ElevatorTo")?.Value, out byte elevatorTo) ? elevatorTo : (byte)(Level + 1);
			Floor = byte.TryParse(xml?.Attribute("Floor")?.Value, out byte floor) ? floor : null;
			FloorTile = byte.TryParse(xml?.Attribute("FloorTile")?.Value, out byte floorTile) ? floorTile : (byte?)null;
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
			#region Object Layer Parsing
			List<EnemySpawn> enemies = [];
			List<StaticSpawn> statics = [];
			List<PatrolPoint> patrolPoints = [];
			PlayerSpawn? playerStart = null;

			// Scan object layer
			for (int i = 0; i < gameMap.ObjectData.Length; i++)
			{
				ushort objectCode = gameMap.ObjectData[i];
				if (objectCode == 0)
					continue;

				ObjectInfo objInfo = mapAnalyzer.GetObjectInfo(objectCode);
				if (objInfo == null || !objInfo.ObjectClass.HasValue)
					continue;

				ushort x = gameMap.X(i);
				ushort z = gameMap.Z(i);
				ObClass objectClass = objInfo.ObjectClass.Value;

				// Player start
				if (MapAnalyzer.IsPlayerStart(objectClass))
				{
					if (objInfo.Facing.HasValue)
						playerStart = new PlayerSpawn(x, z, objInfo.Facing.Value);
				}
				// Enemies
				else if (MapAnalyzer.IsEnemy(objectClass))
				{
					if (objInfo.Facing.HasValue)
					{
						enemies.Add(new EnemySpawn(
							objectClass,
							x, z,
							objInfo.Facing.Value,
							objInfo.Ambush,
							objInfo.Patrol));
					}
				}
				// Static objects (dressing, block, bonus items)
				else if (MapAnalyzer.IsStatic(objectClass))
				{
					StatType statType = MapAnalyzer.GetStatType(objectClass);
					statics.Add(new StaticSpawn(statType, objectClass, objInfo.Page, x, z));
				}
			}

			// Scan map layer for patrol tiles
			for (int i = 0; i < gameMap.MapData.Length; i++)
			{
				ushort mapCode = gameMap.MapData[i];

				if (mapAnalyzer.PatrolTiles.TryGetValue(mapCode, out Direction turn))
				{
					ushort x = gameMap.X(i);
					ushort z = gameMap.Z(i);
					patrolPoints.Add(new PatrolPoint(x, z, turn));
				}
			}

			PlayerStart = playerStart;
			EnemySpawns = Array.AsReadOnly([.. enemies]);
			StaticSpawns = Array.AsReadOnly([.. statics]);
			PatrolPoints = Array.AsReadOnly([.. patrolPoints]);
			#endregion Object Layer Parsing
			#region Wall/Door/PushWall Parsing
			List<PushWallSpawn> pushWalls = [];
			// realWalls replaces pushwalls with floors.
			ushort[] realWalls = new ushort[gameMap.MapData.Length];
			Array.Copy(gameMap.MapData, realWalls, realWalls.Length);
			for (int i = 0; i < realWalls.Length; i++)
				if (mapAnalyzer.PushableTiles.Contains(gameMap.ObjectData[i]))
				{
					realWalls[i] = mapAnalyzer.FloorCodeFirst;
					ushort wall = gameMap.MapData[i];
					ushort horizPage = mapAnalyzer.GetWallPage(wall, false);
					ushort vertPage = mapAnalyzer.GetWallPage(wall, true);
					pushWalls.Add(new PushWallSpawn(horizPage, vertPage, gameMap.X(i), gameMap.Z(i)));
				}
			ushort GetMapData(ushort x, ushort z) => realWalls[gameMap.GetIndex(x, z)];

			// Door frames use specific pages near sprite start
			// In original: doorFrame at DOORWALL+2, but we'll use a calculated value
			ushort doorFrameHoriz = (ushort)(mapAnalyzer.DoorWall + 2);  // Horizontal door frame
			ushort doorFrameVert = (ushort)(mapAnalyzer.DoorWall + 3);   // Vertical door frame

			List<WallSpawn> walls = [];
			List<Tuple<ushort, ushort>> elevators = [];
			List<DoorSpawn> doors = [];

			void EastWest(ushort x, ushort z)
			{
				ushort wall;
				// East/West walls are vertical (darker)
				// Coordinates now consistently use the wall block's position
				// Skip if adjacent tile is a door (doors have their own frames)
				if (x < Width - 1 && mapAnalyzer.IsWall(wall = GetMapData((ushort)(x + 1), z))
					&& !mapAnalyzer.Doors.ContainsKey(wall))
					walls.Add(new WallSpawn(mapAnalyzer.GetWallPage(wall, true), false, (ushort)(x + 1), z, false));
				if (x > 0 && mapAnalyzer.IsWall(wall = GetMapData((ushort)(x - 1), z))
					&& !mapAnalyzer.Doors.ContainsKey(wall))
					walls.Add(new WallSpawn(mapAnalyzer.GetWallPage(wall, true), false, (ushort)(x - 1), z, true));
			}

			void NorthSouth(ushort x, ushort z)
			{
				ushort wall;
				// North/South walls are horizontal (lighter)
				// Coordinates now consistently use the wall block's position
				// Skip if adjacent tile is a door (doors have their own frames)
				if (z > 0 && mapAnalyzer.IsWall(wall = GetMapData(x, (ushort)(z - 1)))
					&& !mapAnalyzer.Doors.ContainsKey(wall))
					walls.Add(new WallSpawn(mapAnalyzer.GetWallPage(wall, false), true, x, (ushort)(z - 1), false));
				if (z < Depth - 1 && mapAnalyzer.IsWall(wall = GetMapData(x, (ushort)(z + 1)))
					&& !mapAnalyzer.Doors.ContainsKey(wall))
					walls.Add(new WallSpawn(mapAnalyzer.GetWallPage(wall, false), true, x, (ushort)(z + 1), true));
			}

			// Scan wall plane (MapData) for doors, walls, and elevators
			for (int i = 0; i < gameMap.MapData.Length; i++)
			{
				ushort x = gameMap.X(i), z = gameMap.Z(i);
				ushort tile = GetMapData(x, z);

				// Check if this tile is a door (tiles 90-101 from wall plane)
				if (mapAnalyzer.Doors.TryGetValue(tile, out DoorInfo doorInfo))
				{
					ushort doorPage = (ushort)(mapAnalyzer.DoorWall + doorInfo.Page);

					// Even tile numbers = vertical doors, odd = horizontal
					if (tile % 2 == 0)  // Vertical door (East/West opening)
					{
						// Door frames on north and south sides, facing inward toward doorway
						walls.Add(new WallSpawn(doorFrameHoriz, true, x, (ushort)(z - 1), false));
						walls.Add(new WallSpawn(doorFrameHoriz, true, x, (ushort)(z + 1), true));
						doors.Add(new DoorSpawn(doorPage, x, z, true));
					}
					else  // Horizontal door (North/South opening)
					{
						// Door frames on west and east sides, facing inward toward doorway
						walls.Add(new WallSpawn(doorFrameVert, false, (ushort)(x - 1), z, true));
						walls.Add(new WallSpawn(doorFrameVert, false, (ushort)(x + 1), z, false));
						doors.Add(new DoorSpawn(doorPage, x, z));
					}
				}
				else if (mapAnalyzer.ElevatorTiles.Contains(tile))
				{
					elevators.Add(new(x, z));
				}
				else if (!mapAnalyzer.IsWall(tile))
				{
					// For empty spaces, check adjacent cells for walls
					EastWest(x, z);
					NorthSouth(x, z);
				}
			}

			Walls = Array.AsReadOnly([.. walls]);
			PushWalls = Array.AsReadOnly([.. pushWalls]);
			Elevators = Array.AsReadOnly([.. elevators]);
			Doors = Array.AsReadOnly([.. doors]);
			#endregion Wall/Door/PushWall Parsing
		}
	}
	#endregion Inner classes
}

// Door metadata from XML
public record DoorInfo
{
	public ushort TileNumber { get; init; }    // Base tile number from wall plane (even, odd is +1)
	public string Name { get; init; }          // Door type name (e.g., "normal", "gold")
	public string Key { get; init; }           // Required key item (null if no key needed)
	public ushort Page { get; init; }          // Page offset from DoorWall
	public string OpenSound { get; init; }     // Sound when opening
	public string CloseSound { get; init; }    // Sound when closing
}

// Object metadata - unified Wolf3D object representation
public record ObjectInfo
{
	public ushort Number { get; init; }      // Tile number in map
	public ObClass? ObjectClass { get; init; } // Wolf3D obclass (classtype or stat_t)
	public ushort Page { get; init; }        // VSwap sprite page number for rendering
	public Direction? Facing { get; init; }  // N/S/E/W (for player & enemies)
	public bool Patrol { get; init; }        // Enemy patrols (vs. standing still)
	public bool Ambush { get; init; }        // Enemy doesn't move until spotted (FL_AMBUSH flag)
	public bool IsEnemy { get; init; }       // Counts toward kill percentage (gamestate.killcount/killtotal)
	public bool IsActive { get; init; }      // Active object (classtype vs stat_t)
}

// Direction enum (Wolf3D: NORTH=0, EAST=1, SOUTH=2, WEST=3)
public enum Direction : byte
{
	N = 0,  // North (negative Z)
	E = 1,  // East (positive X)
	S = 2,  // South (positive Z)
	W = 3   // West (negative X)
}

// Object class enum (Wolf3D classtype and stat_t enums combined)
public enum ObClass
{
	// Special
	playerobj,   // Player start position

	// Static objects (stat_t)
	dressing,    // Non-interactive decoration (walkable)
	block,       // Blocking scenery (not walkable)

	// Bonus/pickup items (stat_t with bo_ prefix)
	// These will be added as needed for dynamic objects
}

// Static object type (Wolf3D stat_t enum)
public enum StatType : byte
{
	dressing,    // Non-interactive decoration
	block,       // Blocking object
	bonus        // Pickup item
}
