using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BenMcLean.Wolf3D.Assets.Gameplay;

public class MapAnalyzer
{
	private readonly ILogger<MapAnalyzer> _logger;
	#region Data
	public XElement XML;

	// Wall configuration (from WOLF3D.xsd Walls attributes)
	public ushort MaxWallTiles { get; private set; }
	public ushort DoorWall { get; private set; }

	// Special tiles (from WOLF3D.xsd special tile elements - can have multiple)
	public Dictionary<ushort, ElevatorConfig> Elevators { get; private set; }
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

	// State definitions (state name -> shape name)
	public Dictionary<string, string> States { get; private set; }
	// Sprite definitions (sprite name -> page number) - provided by VSwap
	public IReadOnlyDictionary<string, ushort> Sprites { get; private set; }
	public ushort FloorCodeFirst { get; private set; }
	public ushort FloorCodes { get; private set; }
	#endregion Data
	public MapAnalyzer(XElement xml, IReadOnlyDictionary<string, ushort> spritesByName, ILogger<MapAnalyzer> logger = null)
	{
		XML = xml ?? throw new ArgumentNullException(nameof(xml));
		Sprites = spritesByName ?? throw new ArgumentNullException(nameof(spritesByName));
		_logger = logger ?? NullLogger<MapAnalyzer>.Instance;

		// Parse WallPlane element attributes (required)
		XElement wallsElement = XML.Element("VSwap")?.Element("WallPlane")
			?? throw new InvalidDataException("Missing VSwap/WallPlane element in XML");

		MaxWallTiles = ushort.Parse(wallsElement.Attribute("MaxWallTiles")?.Value
			?? throw new InvalidDataException("Missing MaxWallTiles attribute"));
		DoorWall = ushort.Parse(wallsElement.Attribute("DoorWall")?.Value
			?? throw new InvalidDataException("Missing DoorWall attribute"));

		// Parse elevator configurations
		Elevators = [];
		foreach (XElement elevElem in wallsElement.Elements("Elevator"))
		{
			ushort tile = ushort.Parse(elevElem.Attribute("Tile")?.Value
				?? throw new InvalidDataException("Elevator element missing Tile attribute"));
			// PressedTile is optional - null means no texture swap on activation
			ushort? pressedTile = ushort.TryParse(elevElem.Attribute("PressedTile")?.Value, out ushort pt)
				? pt : null;
			ElevatorFaces faces = Enum.TryParse(elevElem.Attribute("Faces")?.Value, out ElevatorFaces f)
				? f : ElevatorFaces.All;  // Default: All (for modding flexibility)
			string sound = elevElem.Attribute("Sound")?.Value ?? "LEVELDONESND";

			Elevators[tile] = new ElevatorConfig
			{
				Tile = tile,
				PressedTile = pressedTile,
				Faces = faces,
				Sound = sound
			};
		}
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
		Doors = [];
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

		// Parse states BEFORE objects (needed for state->sprite lookup)
		States = [];
		IEnumerable<XElement> stateElements = XML.Element("VSwap")?.Element("StatInfo")?.Elements("State") ?? [];
		foreach (XElement state in stateElements)
		{
			string name = state.Attribute("Name")?.Value;
			string shape = state.Attribute("Shape")?.Value;
			if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(shape))
				States[name] = shape;
		}
		// Parse object metadata - unified <ObjectType> elements
		IEnumerable<XElement> objectElements = XML.Element("VSwap")?.Element("StatInfo")?.Elements("ObjectType") ?? [];
		Objects = [];

		foreach (XElement obj in objectElements)
		{
			if (!ushort.TryParse(obj.Attribute("Number")?.Value, out ushort number))
				continue;

			Direction? facing = null;
			if (Enum.TryParse(obj.Attribute("Direction")?.Value, out Direction dir))
				facing = dir;

			// Parse ObClass case-insensitively
			ObClass? objectClass = null;
			string obclassStr = obj.Attribute("ObClass")?.Value;
			if (!string.IsNullOrEmpty(obclassStr) && Enum.TryParse(obclassStr, ignoreCase: true, out ObClass parsedClass))
				objectClass = parsedClass;

			// Parse sprite page number
			// Try explicit Page attribute first, then fall back to State->Shape->Page lookup
			if (!ushort.TryParse(obj.Attribute("Page")?.Value, out ushort page))
			{
				// No explicit Page - try looking up from State attribute
				string stateName = obj.Attribute("State")?.Value;
				if (!string.IsNullOrEmpty(stateName)
					&& States.TryGetValue(stateName, out string shapeName)
					&& Sprites.TryGetValue(shapeName, out ushort pageFromState))
				{
					page = pageFromState;
				}
			}
			// Parse actor type (for ObClass.actor - guard, ss, dog, etc.)
			string actorType = obj.Attribute("Actor")?.Value,
			// Parse initial state (for actors - s_grdstand, s_grdpath1, etc.)
				initialState = obj.Attribute("State")?.Value;
			ObjectInfo info = new()
			{
				Number = number,
				ObjectClass = objectClass,
				Actor = actorType,
				Page = page,
				Facing = facing,
				Patrol = obj.IsTrue("Patrol"),
				Ambush = obj.IsTrue("Ambush"),
				IsEnemy = objectClass == ObClass.actor,  // Actors are enemies
				IsActive = objectClass == ObClass.actor,  // Actors are active objects
				State = initialState
			};
			Objects[number] = info;
		}

		// Patrol tiles (map layer) - parse from <Turn> elements in StatInfo
		PatrolTiles = [];
		IEnumerable<XElement> turnElements = XML.Element("VSwap")?.Element("StatInfo")?.Elements("Turn") ?? [];
		foreach (XElement turn in turnElements)
			if (ushort.TryParse(turn.Attribute("Number")?.Value, out ushort tileNum)
				&& Enum.TryParse(turn.Attribute("Direction")?.Value, out Direction turnDir))
				PatrolTiles[tileNum] = turnDir;

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
	public static ushort GetWallPage(ushort wall, bool facesEastWest) =>
		(ushort)((wall - 1) * 2 + (facesEastWest ? 1 : 0));

	// Check if tile number is a wall
	public bool IsWall(ushort tile) => tile > 0 && tile <= MaxWallTiles;

	public bool IsNavigable(ushort mapData, ushort objectData) =>
		IsTransparent(mapData, objectData) && (
			!Objects.TryGetValue(objectData, out ObjectInfo objInfo)
			|| objInfo.ObjectClass != ObClass.block); // Only blocking objects block navigation

	public bool IsTransparent(ushort mapData, ushort objectData) =>
		(!IsWall(mapData) || PushableTiles.Contains(objectData))
		&& !Elevators.ContainsKey(mapData);

	public bool IsMappable(GameMap map, ushort x, ushort y) =>
		IsTransparent(map.GetMapData(x, y), map.GetObjectData(x, y))
		|| x > 0 && IsTransparent(map.GetMapData((ushort)(x - 1), y), map.GetObjectData((ushort)(x - 1), y))
		|| x < map.Width - 1 && IsTransparent(map.GetMapData((ushort)(x + 1), y), map.GetObjectData((ushort)(x + 1), y))
		|| y > 0 && IsTransparent(map.GetMapData(x, (ushort)(y - 1)), map.GetObjectData(x, (ushort)(y - 1)))
		|| y < map.Depth - 1 && IsTransparent(map.GetMapData(x, (ushort)(y + 1)), map.GetObjectData(x, (ushort)(y + 1)));
	public ObjectInfo GetObjectInfo(ushort objectCode) =>
		Objects.TryGetValue(objectCode, out ObjectInfo info) ? info : null;

	// Wolf3D object class categorization (using enum)
	private static bool IsPlayerStart(ObClass? obclass) => obclass == ObClass.playerobj;
	private static bool IsStatic(ObClass? obclass) => obclass == ObClass.dressing || obclass == ObClass.block || obclass == ObClass.bonus;

	private static StatType GetStatType(ObClass? obclass) => obclass switch
	{
		ObClass.dressing => StatType.dressing,
		ObClass.block => StatType.block,
		ObClass.bonus => StatType.bonus,
		_ => StatType.dressing // default
	};

	public MapAnalysis Analyze(GameMap map) => new(this, map, _logger);
	public IEnumerable<MapAnalysis> Analyze(params GameMap[] maps) => maps.Parallelize(Analyze);
	#region Inner classes
	public sealed class MapAnalysis
	{
		#region XML Attributes
		public byte Episode { get; private init; }
		public byte Level { get; private init; }
		public byte ElevatorTo { get; private init; }
		/// <summary>
		/// Destination map when using elevator while standing on AltElevator tile.
		/// If null, uses ElevatorTo (no alternate destination for this map).
		/// Typically used for secret level access in the original game.
		/// </summary>
		public byte? AltElevatorTo { get; private init; }
		public byte? Floor { get; private init; }
		public ushort? FloorTile { get; private init; }
		public byte? Ceiling { get; private init; }
		public ushort? CeilingTile { get; private init; }
		public TimeSpan Par { get; private init; }
		public string Music { get; private init; }
		#endregion XML Attributes
		#region Data
		// Assets layer - faithful to Wolf3D coordinate system (X, Y)
		// NOTE: Wolf3D maps are horizontal (2D floor plans)
		// X = width (east-west), Y = depth (north-south, NOT vertical height!)
		public ushort Width { get; private init; }
		public ushort Depth { get; private init; }  // Wolf3D called this "height" but it's actually depth!
		private readonly BitArray Navigable;
		public bool IsNavigable(int x, int y) =>
			x >= 0 && y >= 0 && x < Width && y < Depth
			&& Navigable[y * Width + x];
		private readonly BitArray Transparent;
		public bool IsTransparent(int x, int y) =>
			x >= 0 && y >= 0 && x < Width && y < Depth
			&& Transparent[y * Width + x];
		private readonly BitArray Mappable;
		public bool IsMappable(int x, int y) =>
			x >= 0 && y >= 0 && x < Width && y < Depth
			&& Mappable[y * Width + x];

		// Reference to the underlying GameMap for accessing raw map data
		private readonly GameMap gameMap;

		// WL_ACT1.C floor code/area number tracking for enemy hearing propagation
		// Floor codes are special tile numbers in the "other" layer used to identify rooms
		// Original Wolf3D: AREATILE constant defines first floor code tile number
		public ushort FloorCodeFirst { get; private init; }  // First floor code tile number (AREATILE)
		public ushort FloorCodeCount { get; private init; }  // Number of distinct floor codes

		/// <summary>
		/// Gets the raw floor code tile at the specified position.
		/// WL_ACT1.C uses mapsegs[0] to read floor codes from the map's "other" layer.
		/// </summary>
		public ushort GetFloorCode(int x, int y) =>
			x >= 0 && y >= 0 && x < Width && y < Depth
			? gameMap.OtherData[y * Width + x]
			: (ushort)0;

		/// <summary>
		/// Gets the area number (0-based) at the specified position.
		/// Converts floor code tile to area index by subtracting FloorCodeFirst (AREATILE).
		/// WL_ACT1.C: area1 = *(map+1) - AREATILE
		/// Returns -1 if the tile is not a valid floor code.
		/// </summary>
		public short GetAreaNumber(int x, int y)
		{
			ushort floorCode = GetFloorCode(x, y);
			if (floorCode >= FloorCodeFirst && floorCode < FloorCodeFirst + FloorCodeCount)
				return (short)(floorCode - FloorCodeFirst);
			return -1;
		}

		// Spawn data (using X, Y coordinate system for Assets layer)
		// WL_DEF.H:objstruct:tilex,tiley (original: unsigned = 16-bit)
		public readonly record struct PlayerSpawn(ushort X, ushort Y, Direction Facing);
		public PlayerSpawn? PlayerStart { get; private set; }

		// WL_DEF.H:objstruct:tilex,tiley (original: unsigned = 16-bit)
		// WL_DEF.H:objstruct:dir (dirtype), obclass (classtype), flags (byte with FL_AMBUSH)
		public readonly record struct ActorSpawn(string ActorType, ushort Page, ushort X, ushort Y, Direction Facing, bool Ambush, bool Patrol, string InitialState);
		public ReadOnlyCollection<ActorSpawn> ActorSpawns { get; private set; }

		// WL_DEF.H:statstruct:tilex,tiley (original: byte), shapenum (int)
		public readonly record struct StaticSpawn(StatType StatType, ObClass Type, ushort Shape, ushort X, ushort Y);
		public ReadOnlyCollection<StaticSpawn> StaticSpawns { get; private set; }

		public readonly record struct PatrolPoint(ushort X, ushort Y, Direction Turn);
		public ReadOnlyCollection<PatrolPoint> PatrolPoints { get; private set; }

		// WL_DEF.H:doorstruct:tilex,tiley (original: byte), vertical (boolean)
		// WL_DEF.H:doorstruct:vertical renamed to FacesEastWest for semantic clarity
		public readonly record struct WallSpawn(ushort Shape, bool FacesEastWest, ushort X, ushort Y, bool Flip = false);
		public ReadOnlyCollection<WallSpawn> Walls { get; private set; }

		// Shape uses VSWAP even/odd pairing convention (even=horizontal, odd=vertical)
		public readonly record struct PushWallSpawn(ushort Shape, ushort X, ushort Y);
		public ReadOnlyCollection<PushWallSpawn> PushWalls { get; private set; }

		/// <summary>
		/// Elevator switch spawn data. Tile is the wall tile number (e.g., 21) used to
		/// look up ElevatorConfig from MapAnalyzer.Elevators dictionary.
		/// </summary>
		public readonly record struct ElevatorSpawn(ushort Tile, ushort X, ushort Y);
		public ReadOnlyCollection<ElevatorSpawn> Elevators { get; private set; }
		public ReadOnlyCollection<uint> AltElevators { get; private set; }
		public ReadOnlyCollection<uint> Ambushes { get; private set; }

		// WL_DEF.H:doorstruct:tilex,tiley (original: byte), vertical (boolean)
		// WL_DEF.H:doorstruct:vertical renamed to FacesEastWest for semantic clarity
		// Area1/Area2: WL_ACT1.C:DoorOpening lines 715-728 - which floor codes this door connects
		public readonly record struct DoorSpawn(
			ushort Shape,
			ushort X,
			ushort Y,
			bool FacesEastWest = false,
			ushort TileNumber = 0,
			short Area1 = -1,
			short Area2 = -1);
		public ReadOnlyCollection<DoorSpawn> Doors { get; private set; }
		#endregion Data
		public MapAnalysis(MapAnalyzer mapAnalyzer, GameMap gameMap, ILogger logger = null)
		{
			this.gameMap = gameMap ?? throw new ArgumentNullException(nameof(gameMap));
			logger ??= NullLogger.Instance;
			#region XML Attributes
			XElement mapsXml = mapAnalyzer.XML.Element("Maps")
				?? throw new InvalidDataException($"XML tag for Maps was not found!"),
				xml = mapsXml.Elements("Map").Where(m => ushort.TryParse(m.Attribute("Number")?.Value, out ushort mu) && mu == gameMap.Number).FirstOrDefault()
				?? throw new InvalidDataException($"XML tag for map \"{gameMap.Name}\" was not found!");
			Episode = byte.TryParse(xml?.Attribute("Episode")?.Value, out byte episode) ? episode : (byte)0;
			Level = byte.TryParse(xml?.Attribute("Level")?.Value, out byte level) ? level : (byte)0;
			ElevatorTo = byte.TryParse(xml.Attribute("ElevatorTo")?.Value, out byte elevatorTo) ? elevatorTo : (byte)(Level + 1);
			AltElevatorTo = byte.TryParse(xml.Attribute("AltElevatorTo")?.Value, out byte secretElevatorTo) ? secretElevatorTo : null;
			// Floor/Ceiling: Map element overrides Maps element default
			Floor = byte.TryParse(xml?.Attribute("Floor")?.Value, out byte floor) ? floor
				: byte.TryParse(mapsXml?.Attribute("Floor")?.Value, out byte defaultFloor) ? defaultFloor : null;
			FloorTile = ushort.TryParse(xml?.Attribute("FloorTile")?.Value, out ushort floorTile) ? floorTile
				: ushort.TryParse(mapsXml?.Attribute("FloorTile")?.Value, out ushort defaultFloorTile) ? defaultFloorTile : null;
			Ceiling = byte.TryParse(xml?.Attribute("Ceiling")?.Value, out byte ceiling) ? ceiling
				: byte.TryParse(mapsXml?.Attribute("Ceiling")?.Value, out byte defaultCeiling) ? defaultCeiling : null;
			CeilingTile = ushort.TryParse(xml?.Attribute("CeilingTile")?.Value, out ushort ceilingTile) ? ceilingTile
				: ushort.TryParse(mapsXml?.Attribute("CeilingTile")?.Value, out ushort defaultCeilingTile) ? defaultCeilingTile : null;
			Par = TimeSpan.TryParse(xml?.Attribute("Par")?.Value, out TimeSpan par) ? par : TimeSpan.Zero;
			Music = xml.Attribute("Music")?.Value;
			#endregion XML Attributes
			#region Masks
			Width = gameMap.Width;
			Depth = gameMap.Depth;
			Navigable = new BitArray(Width * Depth);
			Transparent = new BitArray(Navigable.Length);
			for (ushort x = 0; x < Width; x++)
				for (ushort y = 0; y < Depth; y++)
				{
					Navigable[y * Width + x] = mapAnalyzer.IsNavigable(gameMap.GetMapData(x, y), gameMap.GetObjectData(x, y));
					Transparent[y * Width + x] = mapAnalyzer.IsTransparent(gameMap.GetMapData(x, y), gameMap.GetObjectData(x, y));
				}
			Mappable = new BitArray(Navigable.Length);
			for (ushort x = 0; x < Width; x++)
				for (ushort y = 0; y < Depth; y++)
					Mappable[y * Width + x] = Transparent[y * Width + x]
						|| x > 0 && Transparent[y * Width + (x - 1)]
						|| x < Width - 1 && Transparent[y * Width + x + 1]
						|| y > 0 && Transparent[(y - 1) * Width + x]
						|| y < Depth - 1 && Transparent[(y + 1) * Width + x];

			// Initialize floor code data for enemy hearing propagation (WL_ACT1.C:areaconnect)
			// Floor codes come from the "other" layer (mapsegs[0] in original Wolf3D)
			FloorCodeFirst = mapAnalyzer.FloorCodeFirst;
			FloorCodeCount = mapAnalyzer.FloorCodes;
			#endregion Masks
			#region Object Layer Parsing
			List<ActorSpawn> enemies = [];
			List<StaticSpawn> statics = [];
			List<PatrolPoint> patrolPoints = [];
			PlayerSpawn? playerStart = null;

			// Scan object layer
			logger.LogInformation("Starting object scan: {TileCount} total tiles", gameMap.ObjectData.Length);
			for (int i = 0; i < gameMap.ObjectData.Length; i++)
			{
				ushort objectCode = gameMap.ObjectData[i];
				if (objectCode == 0)
					continue;

				ObjectInfo objInfo = mapAnalyzer.GetObjectInfo(objectCode);
				if (objInfo == null || !objInfo.ObjectClass.HasValue)
					continue;

				ushort x = gameMap.X(i);
				ushort y = gameMap.Y(i);
				ObClass objectClass = objInfo.ObjectClass.Value;

				// Player start
				if (IsPlayerStart(objectClass))
				{
					if (objInfo.Facing.HasValue)
						playerStart = new PlayerSpawn(x, y, objInfo.Facing.Value);
				}
				// Enemies/Actors
				else if (objectClass == ObClass.actor)
				{
					logger.LogDebug("Found actor: Code={ObjectCode}, Actor={Actor}, Facing={Facing}, Page={Page} at ({X},{Y})",
						objectCode, objInfo.Actor, objInfo.Facing, objInfo.Page, x, y);
					if (objInfo.Facing.HasValue && !string.IsNullOrEmpty(objInfo.Actor))
					{
						logger.LogDebug("Adding actor spawn: {Actor}", objInfo.Actor);
						enemies.Add(new ActorSpawn(
							objInfo.Actor,
							objInfo.Page,
							x, y,
							objInfo.Facing.Value,
							objInfo.Ambush,
							objInfo.Patrol,
							objInfo.State));
					}
					else
					{
						logger.LogWarning("Skipped actor: Facing={HasFacing}, Actor present={HasActor}",
							objInfo.Facing.HasValue, !string.IsNullOrEmpty(objInfo.Actor));
					}
				}
				// Static objects (dressing, block, bonus items)
				else if (IsStatic(objectClass))
				{
					StatType statType = GetStatType(objectClass);
					statics.Add(new StaticSpawn(statType, objectClass, objInfo.Page, x, y));
				}
			}

			logger.LogInformation("Object scan complete: {ActorCount} actors, {StaticCount} statics", enemies.Count, statics.Count);
			// Scan object layer for patrol point tiles (Turn markers 90-97)
			for (int i = 0; i < gameMap.ObjectData.Length; i++)
			{
				ushort objectCode = gameMap.ObjectData[i];

				if (mapAnalyzer.PatrolTiles.TryGetValue(objectCode, out Direction turn))
				{
					ushort x = gameMap.X(i);
					ushort y = gameMap.Y(i);
					patrolPoints.Add(new PatrolPoint(x, y, turn));
				}
			}

			PlayerStart = playerStart;
			ActorSpawns = Array.AsReadOnly([.. enemies]);
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
					ushort page = GetWallPage(wall, false);
					// VSWAP even/odd pairing: page (even) for one orientation, page+1 (odd) for perpendicular
					pushWalls.Add(new PushWallSpawn(page, gameMap.X(i), gameMap.Y(i)));
				}
			ushort GetMapData(ushort x, ushort y) => realWalls[gameMap.GetIndex(x, y)];

			// Door frames use specific pages near sprite start
			// In original: doorFrame at DOORWALL+2, but we'll use a calculated value
			ushort doorFrameHoriz = (ushort)(mapAnalyzer.DoorWall + 2);  // Horizontal door frame
			ushort doorFrameVert = (ushort)(mapAnalyzer.DoorWall + 3);   // Vertical door frame

			List<WallSpawn> walls = [];
			List<ElevatorSpawn> elevators = [];
			List<uint> altElevators = [];
			List<uint> ambushes = [];
			List<DoorSpawn> doors = [];

			void EastWest(ushort x, ushort y)
			{
				ushort wall;
				// East/West facing walls (run N-S, perpendicular to X, use vertwall/odd pages)
				// Coordinates now consistently use the wall block's position
				// Skip if adjacent tile is a door (doors have their own frames)
				if (x < Width - 1 && mapAnalyzer.IsWall(wall = GetMapData((ushort)(x + 1), y))
					&& !mapAnalyzer.Doors.ContainsKey(wall))
					walls.Add(new WallSpawn(GetWallPage(wall, true), true, (ushort)(x + 1), y, false));
				if (x > 0 && mapAnalyzer.IsWall(wall = GetMapData((ushort)(x - 1), y))
					&& !mapAnalyzer.Doors.ContainsKey(wall))
					walls.Add(new WallSpawn(GetWallPage(wall, true), true, (ushort)(x - 1), y, true));
			}

			void NorthSouth(ushort x, ushort y)
			{
				ushort wall;
				// North/South facing walls (run E-W, perpendicular to Y, use horizwall/even pages)
				// Coordinates now consistently use the wall block's position
				// Skip if adjacent tile is a door (doors have their own frames)
				if (y > 0 && mapAnalyzer.IsWall(wall = GetMapData(x, (ushort)(y - 1)))
					&& !mapAnalyzer.Doors.ContainsKey(wall))
					walls.Add(new WallSpawn(GetWallPage(wall, false), false, x, (ushort)(y - 1), false));
				if (y < Depth - 1 && mapAnalyzer.IsWall(wall = GetMapData(x, (ushort)(y + 1)))
					&& !mapAnalyzer.Doors.ContainsKey(wall))
					walls.Add(new WallSpawn(GetWallPage(wall, false), false, x, (ushort)(y + 1), true));
			}

			// Scan wall plane (MapData) for doors, walls, and elevators
			for (int i = 0; i < gameMap.MapData.Length; i++)
			{
				ushort x = gameMap.X(i), y = gameMap.Y(i);
				ushort tile = GetMapData(x, y);

				// Check if this tile is a door (tiles 90-101 from wall plane)
				if (mapAnalyzer.Doors.TryGetValue(tile, out DoorInfo doorInfo))
				{
					ushort doorPage = (ushort)(mapAnalyzer.DoorWall + doorInfo.Page);

					// Even tile numbers = vertical doors (FacesEastWest=true), odd = horizontal (FacesEastWest=false)
					if (tile % 2 == 0)  // Vertical door (runs N-S, faces E/W)
					{
						// Door frames on north and south sides (run E-W, face N/S)
						walls.Add(new WallSpawn(doorFrameHoriz, false, x, (ushort)(y - 1), false));
						walls.Add(new WallSpawn(doorFrameHoriz, false, x, (ushort)(y + 1), true));
						// Calculate area connectivity: FacesEastWest doors connect left (X-1) and right (X+1)
						short area1 = GetAreaNumber(x - 1, y);
						short area2 = GetAreaNumber(x + 1, y);
						doors.Add(new DoorSpawn(doorPage, x, y, true, tile, area1, area2));
					}
					else  // Horizontal door (runs E-W, faces N/S)
					{
						// Door frames on west and east sides (run N-S, face E/W)
						walls.Add(new WallSpawn(doorFrameVert, true, (ushort)(x - 1), y, true));
						walls.Add(new WallSpawn(doorFrameVert, true, (ushort)(x + 1), y, false));
						// Calculate area connectivity: horizontal doors connect above (Y-1) and below (Y+1)
						short area1 = GetAreaNumber(x, y - 1);
						short area2 = GetAreaNumber(x, y + 1);
						doors.Add(new DoorSpawn(doorPage, x, y, false, tile, area1, area2));
					}
				}
				else if (mapAnalyzer.Elevators.ContainsKey(tile))
				{
					// Elevator tiles are walls - store tile number for config lookup
					elevators.Add(new ElevatorSpawn(tile, x, y));
				}
				else if (!mapAnalyzer.IsWall(tile))
				{
					// Track alternate elevator tiles (these are floor tiles that modify adjacent elevator behavior)
					// Use original map data, not realWalls (which replaces pushwalls with FloorCodeFirst)
					if (mapAnalyzer.AltElevatorTiles.Contains(gameMap.GetMapData(x, y)))
						altElevators.Add(x | (uint)y << 16);
					// Track ambush tiles (these are floor tiles, not walls)
					// Use original map data, not realWalls (which replaces pushwalls with FloorCodeFirst)
					if (mapAnalyzer.AmbushTiles.Contains(gameMap.GetMapData(x, y)))
						ambushes.Add(x | (uint)y << 16);
					// For empty spaces (including ambush/alternate elevator tiles), check adjacent cells for walls
					EastWest(x, y);
					NorthSouth(x, y);
				}
			}

			Walls = Array.AsReadOnly([.. walls]);
			PushWalls = Array.AsReadOnly([.. pushWalls]);
			Elevators = Array.AsReadOnly([.. elevators]);
			AltElevators = Array.AsReadOnly([.. altElevators]);
			Ambushes = Array.AsReadOnly([.. ambushes]);
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

/// <summary>
/// Elevator switch metadata from XML.
/// WL_AGENT.C:Cmd_Use elevator activation logic.
/// </summary>
public record ElevatorConfig
{
	/// <summary>Wall tile number for the elevator switch (e.g., 21)</summary>
	public ushort Tile { get; init; }
	/// <summary>Wall tile number for pressed state. Null means no texture swap on activation.</summary>
	public ushort? PressedTile { get; init; }
	/// <summary>Which faces the switch can be activated from</summary>
	public ElevatorFaces Faces { get; init; }
	/// <summary>Sound to play when activated (defaults to "LEVELDONESND")</summary>
	public string Sound { get; init; }
}

// Object metadata - unified Wolf3D object representation
public record ObjectInfo
{
	public ushort Number { get; init; }      // Tile number in map
	public ObClass? ObjectClass { get; init; } // Wolf3D obclass (classtype or stat_t)
	public string Actor { get; init; }       // Actor type for ObClass.actor (guard, ss, dog, etc.)
	public ushort Page { get; init; }        // VSwap sprite page number for rendering
	public Direction? Facing { get; init; }  // N/S/E/W (for player & enemies)
	public bool Patrol { get; init; }        // Enemy patrols (vs. standing still)
	public bool Ambush { get; init; }        // Enemy doesn't move until spotted (FL_AMBUSH flag)
	public bool IsEnemy { get; init; }       // Counts toward kill percentage (gamestate.killcount/killtotal)
	public bool IsActive { get; init; }      // Active object (classtype vs stat_t)
	public string State { get; init; }       // Initial state for actors (e.g., "s_grdstand", "s_grdpath1")
}

/// <summary>
/// Eight-way direction enum for Wolf3D actors and patrol points.
/// WL_DEF.H:dirtype - values match original Wolf3D: east=0, northeast=1, north=2, etc.
/// In Wolf3D coordinates: +X is east, -X is west, +Y is south (down the map), -Y is north (up the map).
/// </summary>
public enum Direction : byte
{
	/// <summary>East - 0 degrees, facing +X in Wolf3D coordinates</summary>
	E = 0,
	/// <summary>Northeast - 45 degrees, facing +X/-Y</summary>
	NE = 1,
	/// <summary>North - 90 degrees, facing -Y in Wolf3D coordinates (up the map)</summary>
	N = 2,
	/// <summary>Northwest - 135 degrees, facing -X/-Y</summary>
	NW = 3,
	/// <summary>West - 180 degrees, facing -X in Wolf3D coordinates</summary>
	W = 4,
	/// <summary>Southwest - 225 degrees, facing -X/+Y</summary>
	SW = 5,
	/// <summary>South - 270 degrees, facing +Y in Wolf3D coordinates (down the map)</summary>
	S = 6,
	/// <summary>Southeast - 315 degrees, facing +X/+Y</summary>
	SE = 7
}

/// <summary>
/// Specifies which wall faces an elevator switch can be activated from.
/// In original Wolf3D, elevator switches only work from east/west faces (WL_AGENT.C:Cmd_Use elevatorok).
/// </summary>
public enum ElevatorFaces : byte
{
	/// <summary>Elevator can be activated from any direction (default for modding flexibility)</summary>
	All = 0,
	/// <summary>Elevator can only be activated from east or west faces (original Wolf3D behavior)</summary>
	EastWest = 1,
	/// <summary>Elevator can only be activated from north or south faces</summary>
	NorthSouth = 2
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
	bonus,       // Pickup items (health, ammo, keys, treasure, etc.)
	// Active objects (enemies/NPCs) (classtype)
	actor,       // Enemies and NPCs - specific type defined by Actor attribute
}

// Static object type (Wolf3D stat_t enum)
public enum StatType : byte
{
	dressing,    // Non-interactive decoration
	block,       // Blocking object
	bonus        // Pickup item
}
