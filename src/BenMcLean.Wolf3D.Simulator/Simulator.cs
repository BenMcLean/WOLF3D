using System;
using System.Collections.Generic;
using System.Linq;
using BenMcLean.Wolf3D.Assets.Gameplay;
using BenMcLean.Wolf3D.Simulator.Entities;
using Microsoft.Extensions.Logging;
using static BenMcLean.Wolf3D.Assets.Gameplay.MapAnalyzer;

namespace BenMcLean.Wolf3D.Simulator;

/// <summary>
/// Discrete event simulator for Wolfenstein 3D game logic.
/// Runs at 70Hz tic rate matching the original game (WL_DRAW.C:CalcTics).
/// </summary>
public class Simulator
{
	public const double TicRate = 70.0, // Hz
		TicDuration = 1.0 / TicRate; // ~14.2857ms
	public const int MaxTicsPerUpdate = 10; // Prevent spiral of death
	private double accumulatedTime;
	private readonly List<PlayerAction> pendingActions = [];
	public IReadOnlyList<Door> Doors => doors;
	private readonly List<Door> doors = [];
	public IReadOnlyList<PushWall> PushWalls => pushWalls;
	private readonly List<PushWall> pushWalls = [];
	// Global lock: only one pushwall can move at a time in the entire level
	private bool anyPushWallMoving = false;
	// WL_ACT1.C:statobjlist[MAXSTATS]
	// Array of bonus/pickup objects (not fixtures - those are not simulated)
	public StatObj[] StatObjList { get; private set; } = new StatObj[StatObj.MAXSTATS];
	// WL_ACT1.C:laststatobj - pointer to next free slot
	private int lastStatObj;
	// WL_DEF.H:objlist[MAXACTORS] - array of active actors
	// Using List instead of fixed array for modern flexibility
	public IReadOnlyList<Actor> Actors => actors;
	private readonly List<Actor> actors = [];
	// State machine data for actors
	private readonly StateCollection stateCollection;
	// Lua script engine for state functions
	private readonly Lua.LuaScriptEngine luaScriptEngine;
	// RNG and GameClock for deterministic simulation
	private readonly RNG rng;
	private readonly GameClock gameClock;
	// Logger for debug output
	private readonly Microsoft.Extensions.Logging.ILogger logger;
	// Map analyzer for accessing door metadata (sounds, etc.)
	private MapAnalyzer mapAnalyzer;
	// Map analysis for line-of-sight calculations and navigation
	private MapAnalysis mapAnalysis;
	// Patrol direction lookup (WL_ACT2.C:SelectPathDir reads from map layer 1)
	// Key encoding: (Y << 16) | X
	private Dictionary<uint, Direction> patrolDirectionAtTile;
	// Map dimensions
	private ushort mapWidth, mapHeight;
	// WL_ACT1.C:areaconnect[NUMAREAS][NUMAREAS]
	// Symmetric matrix tracking how many doors connect each pair of areas/rooms
	// Each entry is a count: 0 = disconnected, >0 = number of doors connecting the areas
	public SymmetricMatrix AreaConnect { get; private set; }
	// Spatial index arrays - track what occupies each tile (WL_DEF.H:actorat equivalent)
	// Index calculation: y * mapWidth + x
	// -1 = tile is empty, >= 0 = index into respective collection
	// NOTE: actorAtTile[actor.TileX][actor.TileY] points to the actor's DESTINATION tile,
	// not its current position! Updated via clear-think-set pattern in UpdateActors.
	private short[] actorAtTile;     // Actor index occupying this tile (-1 if none)
	private short[] doorAtTile;      // Door index at this tile (-1 if none)
	private short[] pushWallAtTile;  // PushWall index at this tile (-1 if none)
									 // WL_PLAY.C:tics (unsigned = 16-bit in original DOS, but we accumulate as long)
									 // Current simulation time in tics
	public long CurrentTic { get; private set; }
	// Player position (updated each Update call by presentation layer)
	// WL_DEF.H:player->x, player->y (16.16 fixed-point)
	public int PlayerX { get; private set; }
	public int PlayerY { get; private set; }
	// Derived player tile coordinates
	public ushort PlayerTileX => (ushort)(PlayerX >> 16);
	public ushort PlayerTileY => (ushort)(PlayerY >> 16);
	#region C# Events for Observer Pattern
	/// <summary>
	/// Fired when a door starts opening (position was 0).
	/// WL_ACT1.C:DoorOpening
	/// </summary>
	public event Action<DoorOpeningEvent> DoorOpening;
	/// <summary>
	/// Fired when a door finishes opening (position reached 0xFFFF).
	/// WL_ACT1.C:DoorOpening
	/// </summary>
	public event Action<DoorOpenedEvent> DoorOpened;
	/// <summary>
	/// Fired every tic while a door is moving (opening or closing).
	/// WL_ACT1.C:DoorOpening and DoorClosing
	/// </summary>
	public event Action<DoorPositionChangedEvent> DoorPositionChanged;
	/// <summary>
	/// Fired when a door starts closing.
	/// WL_ACT1.C:CloseDoor
	/// </summary>
	public event Action<DoorClosingEvent> DoorClosing;
	/// <summary>
	/// Fired when a door finishes closing (position reached 0).
	/// WL_ACT1.C:DoorClosing
	/// </summary>
	public event Action<DoorClosedEvent> DoorClosed;
	/// <summary>
	/// Fired when player tries to open a locked door without the required key.
	/// WL_ACT1.C:OperateDoor
	/// </summary>
	public event Action<DoorLockedEvent> DoorLocked;
	/// <summary>
	/// Fired when a door is blocked from closing by an actor or player.
	/// WL_ACT1.C:DoorClosing
	/// </summary>
	public event Action<DoorBlockedEvent> DoorBlocked;
	/// <summary>
	/// Fired when a bonus object spawns (static placement or enemy drop).
	/// WL_GAME.C:ScanInfoPlane or WL_ACT1.C:PlaceItemType
	/// </summary>
	public event Action<BonusSpawnedEvent> BonusSpawned;
	/// <summary>
	/// Fired when a bonus object is picked up by the player.
	/// WL_AGENT.C:GetBonus
	/// </summary>
	public event Action<BonusPickedUpEvent> BonusPickedUp;
	/// <summary>
	/// Fired when an actor spawns in the world.
	/// WL_GAME.C:ScanInfoPlane
	/// </summary>
	public event Action<ActorSpawnedEvent> ActorSpawned;
	/// <summary>
	/// Fired when an actor moves to a new position.
	/// WL_ACT2.C movement logic
	/// </summary>
	public event Action<ActorMovedEvent> ActorMoved;
	/// <summary>
	/// Fired when an actor's sprite changes (animation, state, rotation).
	/// WL_DEF.H:statestruct - fires very frequently
	/// </summary>
	public event Action<ActorSpriteChangedEvent> ActorSpriteChanged;
	/// <summary>
	/// Fired when an actor is removed from the world (death, despawn).
	/// WL_ACT1.C:KillActor
	/// </summary>
	public event Action<ActorDespawnedEvent> ActorDespawned;
	/// <summary>
	/// Fired when an actor plays a sound.
	/// WL_STATE.C:PlaySoundLocActor
	/// Presentation layer attaches sound to actor - sound moves with actor.
	/// </summary>
	public event Action<ActorPlaySoundEvent> ActorPlaySound;
	/// <summary>
	/// Fired when a door plays a sound.
	/// WL_ACT1.C:DoorOpening, DoorClosing
	/// Presentation layer can sweep sound across doorframe.
	/// </summary>
	public event Action<DoorPlaySoundEvent> DoorPlaySound;
	/// <summary>
	/// Fired when a global (non-positional) sound should play.
	/// For UI sounds, music, narrator - bypasses spatial audio.
	/// </summary>
	public event Action<PlayGlobalSoundEvent> PlayGlobalSound;
	/// <summary>
	/// Fired every tic while a pushwall is moving.
	/// WL_ACT1.C pushwall movement logic
	/// </summary>
	public event Action<PushWallPositionChangedEvent> PushWallPositionChanged;
	/// <summary>
	/// Fired when a pushwall plays a sound.
	/// WL_ACT1.C pushwall activation
	/// Presentation layer attaches sound to pushwall - sound moves with pushwall.
	/// </summary>
	public event Action<PushWallPlaySoundEvent> PushWallPlaySound;
	#endregion
	/// <summary>
	/// Creates a new Simulator instance.
	/// </summary>
	/// <param name="stateCollection">State machine data for actors</param>
	/// <param name="rng">Deterministic random number generator</param>
	/// <param name="gameClock">Deterministic game clock</param>
	/// <param name="logger">Optional logger for Lua script output</param>
	public Simulator(StateCollection stateCollection, RNG rng, GameClock gameClock, Microsoft.Extensions.Logging.ILogger logger = null)
	{
		this.stateCollection = stateCollection ?? throw new ArgumentNullException(nameof(stateCollection));
		this.rng = rng ?? throw new ArgumentNullException(nameof(rng));
		this.gameClock = gameClock ?? throw new ArgumentNullException(nameof(gameClock));
		this.logger = logger;
		// Initialize Lua script engine and compile all state functions
		luaScriptEngine = new Lua.LuaScriptEngine(logger);
		luaScriptEngine.CompileAllStateFunctions(stateCollection);
	}
	/// <summary>
	/// Update the simulation with elapsed real time.
	/// Events are dispatched to subscribers via C# events as they occur.
	/// Based on WL_PLAY.C:PlayLoop and WL_DRAW.C:CalcTics.
	/// </summary>
	/// <param name="deltaTime">Elapsed real time in seconds</param>
	/// <param name="playerX">Player X position in 16.16 fixed-point (from HMD tracking)</param>
	/// <param name="playerY">Player Y position in 16.16 fixed-point (from HMD tracking)</param>
	public void Update(double deltaTime, int playerX, int playerY)
	{
		// Update player position from presentation layer
		PlayerX = playerX;
		PlayerY = playerY;

		accumulatedTime += deltaTime;
		// WL_DRAW.C:CalcTics - calculate tics since last refresh
		int ticsToProcess = (int)(accumulatedTime / TicDuration);
		if (ticsToProcess > MaxTicsPerUpdate)
			ticsToProcess = MaxTicsPerUpdate;
		accumulatedTime -= ticsToProcess * TicDuration;
		// WL_PLAY.C:PlayLoop - process each tic
		for (int i = 0; i < ticsToProcess; i++)
		{
			ProcessTic();
			CurrentTic++;
		}
	}

	/// <summary>
	/// Queue a player action to be processed on the next tic.
	/// Ensures determinism by quantizing inputs to tic boundaries.
	/// </summary>
	public void QueueAction(PlayerAction action) => pendingActions.Add(action);
	private void ProcessTic()
	{
		// Process queued player actions
		foreach (PlayerAction action in pendingActions)
			ProcessAction(action);
		pendingActions.Clear();
		// WL_ACT1.C:MoveDoors - update all doors
		for (int i = 0; i < doors.Count; i++)
			UpdateDoor(i);
		// WL_ACT1.C:MovePWalls - update all pushwalls
		for (int i = 0; i < pushWalls.Count; i++)
			UpdatePushWall(i);
		// WL_ACT2.C:DoActor - update all actors
		for (int i = 0; i < actors.Count; i++)
			UpdateActor(i);
	}
	private void ProcessAction(PlayerAction action)
	{
		if (action is OperateDoorAction operateDoor)
			OperateDoor(operateDoor.DoorIndex);
		else if (action is ActivatePushWallAction activatePushWall)
			ActivatePushWall(activatePushWall.TileX, activatePushWall.TileY, activatePushWall.Direction);
	}
	/// <summary>
	/// WL_ACT1.C:OperateDoor (line 644)
	/// The player wants to change the door's direction
	/// </summary>
	private void OperateDoor(ushort doorIndex)
	{
		if (doorIndex >= doors.Count)
			return;
		Door door = doors[doorIndex];
		// WL_ACT1.C:OperateDoor lines 658-668 - toggle door state
		switch (door.Action)
		{
			case DoorAction.Closed:
			case DoorAction.Closing:
				OpenDoor(doorIndex);
				break;
			case DoorAction.Open:
			case DoorAction.Opening:
				CloseDoor(doorIndex);
				break;
		}
	}
	/// <summary>
	/// WL_ACT1.C:OpenDoor (line 546)
	/// </summary>
	public void OpenDoor(ushort doorIndex)
	{
		Door door = doors[doorIndex];
		if (door.Action == DoorAction.Open)
		{
			// Door already open, just reset the timer (WL_ACT1.C:549)
			door.TicCount = 0;
		}
		else
		{
			// Door is Closed or Closing - start/restart opening (WL_ACT1.C:551)
			door.Action = DoorAction.Opening;
			// Emit opening event and sound when transitioning to Opening state
			DoorOpening?.Invoke(new DoorOpeningEvent
			{
				DoorIndex = doorIndex,
				TileX = door.TileX,
				TileY = door.TileY
			});
			// Emit door opening sound
			if (mapAnalyzer?.Doors.TryGetValue(door.TileNumber, out DoorInfo doorInfo) == true
				&& !string.IsNullOrEmpty(doorInfo.OpenSound))
				EmitDoorPlaySound(doorIndex, doorInfo.OpenSound);
		}
	}
	/// <summary>
	/// Check if a door is blocked by actors, player, or objects.
	/// WL_ACT1.C:574-611, 773-778
	/// </summary>
	private bool IsDoorBlocked(Door door)
	{
		int tileIdx = GetTileIndex(door.TileX, door.TileY);
		// Check if an actor is standing in the doorway (quick check via spatial index)
		if (actorAtTile[tileIdx] >= 0)
			return true;
		// Check if player is in the doorway
		if (PlayerTileX == door.TileX && PlayerTileY == door.TileY)
			return true;
		// Check if any spawned bonus object is on the door tile
		// WL_ACT1.C:773 - actorat check includes static objects (values 1-127)
		// Only spawned objects (ShapeNum != -1) block the door
		// Bonuses only have tile coordinates, so exact tile match is the only check needed
		for (int i = 0; i < lastStatObj; i++)
		{
			if (StatObjList[i] != null && !StatObjList[i].IsFree
				&& StatObjList[i].TileX == door.TileX
				&& StatObjList[i].TileY == door.TileY)
				return true;
		}
		// Comprehensive actor collision check - check if any actor's collision box overlaps door tile
		// This catches actors whose center is in an adjacent tile but body extends into doorway
		// Fixed-point 16.16 door tile center for proximity checks
		int doorCenterX = (door.TileX << 16) + 0x8000;
		int doorCenterY = (door.TileY << 16) + 0x8000;
		const int TILE_SIZE = 0x10000; // One tile in fixed-point 16.16
		for (int i = 0; i < actors.Count; i++)
		{
			Actor actor = actors[i];
			// Check if actor's collision box overlaps with door tile (box is 1 tile centered on actor)
			int deltaX = Math.Abs(actor.X - doorCenterX);
			int deltaY = Math.Abs(actor.Y - doorCenterY);
			if (deltaX < TILE_SIZE && deltaY < TILE_SIZE)
				return true;
		}
		return false;
	}
	/// <summary>
	/// WL_ACT1.C:CloseDoor (line 563)
	/// </summary>
	private void CloseDoor(ushort doorIndex)
	{
		Door door = doors[doorIndex];
		// WL_ACT1.C:574-611 - don't close on anything solid
		if (IsDoorBlocked(door))
			return;
		// Nothing blocking, start closing
		door.Action = DoorAction.Closing;
		DoorClosing?.Invoke(new DoorClosingEvent
		{
			DoorIndex = doorIndex,
			TileX = door.TileX,
			TileY = door.TileY
		});
		// Emit door closing sound
		if (mapAnalyzer?.Doors.TryGetValue(door.TileNumber, out DoorInfo doorInfo) ?? false
			&& !string.IsNullOrEmpty(doorInfo.CloseSound))
			EmitDoorPlaySound(doorIndex, doorInfo.CloseSound);
	}
	/// <summary>
	/// WL_ACT1.C:MoveDoors (line 832)
	/// Called from PlayLoop
	/// </summary>
	private void UpdateDoor(int doorIndex)
	{
		Door door = doors[doorIndex];
		// WL_ACT1.C:MoveDoors lines 842-856
		switch (door.Action)
		{
			case DoorAction.Open:
				UpdateDoorOpen(doorIndex);
				break;
			case DoorAction.Opening:
				UpdateDoorOpening(doorIndex);
				break;
			case DoorAction.Closing:
				UpdateDoorClosing(doorIndex);
				break;
			case DoorAction.Closed:
				// Nothing to do
				break;
		}
	}
	/// <summary>
	/// WL_ACT1.C:DoorOpen (line 684)
	/// Close the door after three seconds
	/// </summary>
	private void UpdateDoorOpen(int doorIndex)
	{
		Door door = doors[doorIndex];
		// WL_ACT1.C:686 - accumulate tics
		door.TicCount += 1; // Always 1 tic per call in our simplified version
							// WL_ACT1.C:686 - check if time to close
		if (door.TicCount >= Constants.DoorOpenTics)
			CloseDoor((ushort)doorIndex);
	}
	/// <summary>
	/// WL_ACT1.C:DoorOpening (line 700)
	/// </summary>
	private void UpdateDoorOpening(int doorIndex)
	{
		Door door = doors[doorIndex];
		int newPosition = door.Position;
		// Note: DoorOpening event and sound are emitted in OpenDoor() when transitioning to Opening state
		// WL_ACT1.C:739 - slide the door by an adaptive amount
		// position += tics<<10 (we use 1 tic per update)
		newPosition += 1 << 10;
		// WL_ACT1.C:740 - check if fully open
		if (newPosition >= 0xFFFF)
		{
			// Door fully open (WL_ACT1.C:742-748)
			newPosition = 0xFFFF;
			door.Position = (ushort)newPosition;
			door.TicCount = 0;
			door.Action = DoorAction.Open;
			DoorOpened?.Invoke(new DoorOpenedEvent
			{
				DoorIndex = (ushort)doorIndex,
				TileX = door.TileX,
				TileY = door.TileY
			});
			// WL_ACT1.C:748 - clear actorat for door tile (allows actors to pathfind through)
			int tileIdx = GetTileIndex(door.TileX, door.TileY);
			doorAtTile[tileIdx] = -1;
			// WL_ACT1.C:723-726 - increment area connection count for hearing propagation
			if (door.Area1 >= 0 && door.Area2 >= 0)
			{
				short currentCount = AreaConnect[(ushort)door.Area1, (ushort)door.Area2];
				AreaConnect[(ushort)door.Area1, (ushort)door.Area2] = (short)(currentCount + 1);
			}
		}
		else
			door.Position = (ushort)newPosition;
		// Emit position changed event every tic
		DoorPositionChanged?.Invoke(new DoorPositionChangedEvent
		{
			DoorIndex = (ushort)doorIndex,
			TileX = door.TileX,
			TileY = door.TileY,
			Position = door.Position,
			Action = door.Action
		});
	}
	/// <summary>
	/// WL_ACT1.C:DoorClosing (line 763)
	/// </summary>
	private void UpdateDoorClosing(int doorIndex)
	{
		Door door = doors[doorIndex];

		// WL_ACT1.C:773-778 - check if something moved into the doorway while closing
		if (IsDoorBlocked(door))
		{
			// Something blocking door - reopen it
			OpenDoor((ushort)doorIndex);
			return;
		}

		int tileIdx = GetTileIndex(door.TileX, door.TileY);
		int newPosition = door.Position;
		// WL_ACT1.C:785 - slide the door by an adaptive amount
		// position -= tics<<10 (we use 1 tic per update)
		newPosition -= 1 << 10;
		// WL_ACT1.C:786 - check if fully closed
		if (newPosition <= 0)
		{
			// Door fully closed (WL_ACT1.C:788-813)
			newPosition = 0;
			door.Position = (ushort)newPosition;
			door.Action = DoorAction.Closed;
			// Restore door to spatial index (blocks movement again)
			doorAtTile[tileIdx] = (short)doorIndex;
			DoorClosed?.Invoke(new DoorClosedEvent
			{
				DoorIndex = (ushort)doorIndex,
				TileX = door.TileX,
				TileY = door.TileY
			});
			// WL_ACT1.C:798-811 - decrement area connection count for hearing propagation
			if (door.Area1 >= 0 && door.Area2 >= 0)
			{
				short currentCount = AreaConnect[(ushort)door.Area1, (ushort)door.Area2];
				AreaConnect[(ushort)door.Area1, (ushort)door.Area2] = (short)(currentCount - 1);
			}
		}
		else
			door.Position = (ushort)newPosition;
		// Emit position changed event every tic
		DoorPositionChanged?.Invoke(new DoorPositionChangedEvent
		{
			DoorIndex = (ushort)doorIndex,
			TileX = door.TileX,
			TileY = door.TileY,
			Position = door.Position,
			Action = door.Action
		});
	}
	#region PushWall Logic
	/// <summary>
	/// Player attempts to activate a pushwall.
	/// Based on WL_ACT1.C pushwall activation logic.
	/// </summary>
	/// <param name="tileX">Tile X coordinate of wall being pushed</param>
	/// <param name="tileY">Tile Y coordinate of wall being pushed</param>
	/// <param name="direction">Direction pushwall should move (away from player)</param>
	private void ActivatePushWall(ushort tileX, ushort tileY, Direction direction)
	{
		// Rule 3: Only one pushwall can move at a time in the entire level
		if (anyPushWallMoving)
			return; // Another pushwall is already moving

		int tileIdx = GetTileIndex(tileX, tileY);

		// Check if there's a pushwall at this location
		if (pushWallAtTile[tileIdx] < 0)
			return; // No pushwall here

		int pushWallIndex = pushWallAtTile[tileIdx];
		PushWall pushWall = pushWalls[pushWallIndex];

		// Check if pushwall is already moving
		if (pushWall.Action == PushWallAction.Pushing)
			return; // Already moving

		// Rule 1: Pushwalls move TWO tiles, not one
		// Calculate first and second destination tiles based on direction
		ushort dest1X = tileX, dest1Y = tileY;
		ushort dest2X = tileX, dest2Y = tileY;
		switch (direction)
		{
			case Direction.N: dest1Y--; dest2Y -= 2; break;
			case Direction.S: dest1Y++; dest2Y += 2; break;
			case Direction.E: dest1X++; dest2X += 2; break;
			case Direction.W: dest1X--; dest2X -= 2; break;
			default: return; // Invalid direction (only cardinal directions for pushwalls)
		}

		// Check if both destination tiles are navigable
		if (!IsTileNavigable(dest1X, dest1Y) || !IsTileNavigable(dest2X, dest2Y))
			return; // Can't push - destination blocked

		// Start pushing!
		pushWall.Action = PushWallAction.Pushing;
		pushWall.Direction = direction;
		pushWall.TicCount = (short)(2 * PushWall.PushTics); // 2 tiles × 128 tics/tile = 256 tics
		anyPushWallMoving = true; // Set global lock

		// Play pushwall sound
		EmitPushWallPlaySound((ushort)pushWallIndex, "PUSHWALLSND");
	}

	/// <summary>
	/// Updates a single pushwall during movement.
	/// Based on WL_ACT1.C:MovePWalls
	/// </summary>
	private void UpdatePushWall(int pushWallIndex)
	{
		PushWall pushWall = pushWalls[pushWallIndex];

		// Only update if actively pushing
		if (pushWall.Action != PushWallAction.Pushing)
			return;

		// Rule 4: Track which tile is currently blocked before moving
		(ushort oldTileX, ushort oldTileY) = pushWall.GetTilePosition();

		// Decrement tic counter
		pushWall.TicCount--;

		// Rule 1: Calculate movement delta per tic (TWO full tiles over 2*PushTics tics)
		// Two tiles = 2 * 65536 = 131072, duration = 256 tics, so delta = 131072 / 256 = 512
		// This maintains original Wolf3D speed of 128 tics per tile
		int delta = (2 << 16) / (2 * PushWall.PushTics);

		// Move in the specified direction
		switch (pushWall.Direction)
		{
			case Direction.N: pushWall.Y -= delta; break;
			case Direction.S: pushWall.Y += delta; break;
			case Direction.E: pushWall.X += delta; break;
			case Direction.W: pushWall.X -= delta; break;
		}

		// Rule 4: Check if pushwall crossed into a new tile
		(ushort newTileX, ushort newTileY) = pushWall.GetTilePosition();
		if (oldTileX != newTileX || oldTileY != newTileY)
		{
			// Pushwall crossed tile boundary - update spatial index
			int oldIdx = GetTileIndex(oldTileX, oldTileY);
			int newIdx = GetTileIndex(newTileX, newTileY);
			pushWallAtTile[oldIdx] = -1; // Old tile is now navigable
			pushWallAtTile[newIdx] = (short)pushWallIndex; // New tile is now blocked
		}

		// Fire position changed event
		PushWallPositionChanged?.Invoke(new PushWallPositionChangedEvent
		{
			PushWallIndex = (ushort)pushWallIndex,
			X = pushWall.X,
			Y = pushWall.Y,
			Action = pushWall.Action
		});

		// Check if movement complete
		if (pushWall.TicCount <= 0)
		{
			// Rule 2: Snap to exact final tile center (recenter)
			(ushort finalX, ushort finalY) = pushWall.GetTilePosition();
			pushWall.X = (finalX << 16) + 0x8000; // Center in tile
			pushWall.Y = (finalY << 16) + 0x8000;
			pushWall.Action = PushWallAction.Idle;
			anyPushWallMoving = false; // Rule 3: Release global lock

			// Fire final position event
			PushWallPositionChanged?.Invoke(new PushWallPositionChangedEvent
			{
				PushWallIndex = (ushort)pushWallIndex,
				X = pushWall.X,
				Y = pushWall.Y,
				Action = pushWall.Action
			});
		}
	}
	#endregion
	#region Actor Update Logic
	/// <summary>
	/// WL_PLAY.C actor update loop (lines 1690-1774)
	/// Handles both transitional (tictime > 0) and non-transitional (tictime == 0) states
	/// </summary>
	private void UpdateActor(int actorIndex)
	{
		Actor actor = actors[actorIndex];

		// WL_PLAY.C:DoActor - Clear actorat before executing Think
		// This implements the clear-think-set pattern from original Wolf3D
		int tileIdx = GetTileIndex(actor.TileX, actor.TileY);
		actorAtTile[tileIdx] = -1;
		// WL_PLAY.C:1696 - Non-transitional object (tictime == 0)
		// These states execute Think every frame but never auto-transition
		if (actor.TicCount == 0)
		{
			// Execute Think function if present
			if (!string.IsNullOrEmpty(actor.CurrentState.Think))
			{
				Lua.ActorScriptContext context = new(this, actor, actorIndex, rng, gameClock, mapAnalysis, logger);
				try
				{
					luaScriptEngine.ExecuteStateFunction(actor.CurrentState.Think, context);
				}
				catch (Exception ex)
				{
					logger?.LogError(ex, "Error executing Lua function for actor {ActorIndex}: {ErrorMessage}", actorIndex, ex.Message);
				}
			}
			// Set actorat after Think (actor.TileX/TileY may have changed)
			tileIdx = GetTileIndex(actor.TileX, actor.TileY);
			actorAtTile[tileIdx] = (short)actorIndex;
			// WL_PLAY.C:1726 - Return early, don't transition
			return;
		}

		// WL_PLAY.C:1732 - Transitional object (tictime > 0)
		// Decrement tic counter
		actor.TicCount--;

		// WL_PLAY.C:1733 - Check for state transition
		while (actor.TicCount <= 0)
		{
			// WL_PLAY.C:1746 - Transition to next state
			if (actor.CurrentState.Next == null)
				break; // No next state, stop transitioning

			// TransitionActorState executes the Action function when entering the new state
			TransitionActorState(actorIndex, actor.CurrentState.Next);

			// WL_PLAY.C:1754-1758 - If new state is non-transitional, set ticcount=0 and execute Think
			if (actor.CurrentState.Tics == 0)
			{
				actor.TicCount = 0;
				break; // Will execute Think on next update
			}

			// WL_PLAY.C:1760 - Add new state's tictime (keeps negative remainder for timing accuracy)
			// Note: In original C, this is +=, not =, to preserve sub-tic timing
			// For now we simplified to just break out of loop - full accuracy can be added later
			break;
		}

		// WL_PLAY.C:1763-1770 - Execute Think function
		if (!string.IsNullOrEmpty(actor.CurrentState.Think))
		{
			Lua.ActorScriptContext context = new(this, actor, actorIndex, rng, gameClock, mapAnalysis, logger);
			try
			{
				luaScriptEngine.ExecuteStateFunction(actor.CurrentState.Think, context);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Error executing Think function '{actor.CurrentState.Think}' for actor {actorIndex}: {ex.Message}");
			}
		}
		// Set actorat after Think (actor.TileX/TileY may have changed)
		tileIdx = GetTileIndex(actor.TileX, actor.TileY);
		actorAtTile[tileIdx] = (short)actorIndex;
	}
	/// <summary>
	/// Transitions an actor to a new state by state name.
	/// Public wrapper for Lua scripts to call.
	/// </summary>
	public void TransitionActorStateByName(int actorIndex, string stateName)
	{
		if (stateCollection.States.TryGetValue(stateName, out State nextState))
			TransitionActorState(actorIndex, nextState);
	}
	/// <summary>
	/// Transitions an actor to a new state.
	/// Updates sprite, tics, executes Action function, and fires events.
	/// </summary>
	private void TransitionActorState(int actorIndex, State nextState)
	{
		Actor actor = actors[actorIndex];
		short oldShape = actor.ShapeNum;
		// Update state
		actor.CurrentState = nextState;
		actor.TicCount = nextState.Tics;
		actor.Speed = nextState.Speed;
		// Update sprite (may be modified by rotation logic later)
		actor.ShapeNum = nextState.Shape;
		// Execute Action function if present
		if (!string.IsNullOrEmpty(nextState.Action))
		{
			Lua.ActorScriptContext context = new(this, actor, actorIndex, rng, gameClock, mapAnalysis, logger);
			try
			{
				luaScriptEngine.ExecuteStateFunction(nextState.Action, context);
			}
			catch (Exception ex)
			{
				logger?.LogError(ex, "Error executing Action function '{ActionFunction}' for actor {ActorIndex}", nextState.Action, actorIndex);
			}
		}
		// Fire sprite changed event if sprite actually changed
		if (oldShape != actor.ShapeNum)
		{
			ActorSpriteChanged?.Invoke(new ActorSpriteChangedEvent
			{
				ActorIndex = actorIndex,
				Shape = (ushort)actor.ShapeNum,
				IsRotated = nextState.Rotate
			});
		}
	}
	#endregion
	/// <summary>
	/// Initialize doors from MapAnalyzer data.
	/// Stores MapAnalyzer reference for looking up door metadata (sounds, etc.).
	/// </summary>
	public void LoadDoorsFromMapAnalysis(
		MapAnalyzer mapAnalyzer,
		MapAnalysis mapAnalysis,
		IEnumerable<MapAnalysis.DoorSpawn> doorSpawns)
	{
		// Store MapAnalyzer for looking up door sounds when emitting events
		this.mapAnalyzer = mapAnalyzer ?? throw new ArgumentNullException(nameof(mapAnalyzer));

		// Initialize spatial index arrays and map dimensions
		// Must be done before loading any entities (doors, pushwalls, actors)
		this.mapAnalysis = mapAnalysis;
		mapWidth = mapAnalysis.Width;
		mapHeight = mapAnalysis.Depth;

		int tileCount = mapWidth * mapHeight;
		actorAtTile = new short[tileCount];
		doorAtTile = new short[tileCount];
		pushWallAtTile = new short[tileCount];

		// Fill with -1 (empty)
#if NET6_0_OR_GREATER
		Array.Fill(actorAtTile, (short)-1);
		Array.Fill(doorAtTile, (short)-1);
		Array.Fill(pushWallAtTile, (short)-1);
#else
		for (int i = 0; i < tileCount; i++)
		{
			actorAtTile[i] = -1;
			doorAtTile[i] = -1;
			pushWallAtTile[i] = -1;
		}
#endif

		// Initialize area connectivity matrix (WL_ACT1.C:areaconnect)
		// Size is based on number of floor codes defined in the game configuration
		AreaConnect = new SymmetricMatrix(mapAnalysis.FloorCodeCount);
		doors.Clear();
		int doorIndex = 0;
		foreach (MapAnalysis.DoorSpawn spawn in doorSpawns)
		{
			// Create door with area connectivity from spawn data (calculated by MapAnalysis)
			doors.Add(new Door(spawn.X, spawn.Y, spawn.FacesEastWest, spawn.TileNumber, spawn.Area1, spawn.Area2));

			// Update spatial index - door occupies its tile
			int tileIdx = GetTileIndex(spawn.X, spawn.Y);
			doorAtTile[tileIdx] = (short)doorIndex;

			doorIndex++;
		}
	}

	/// <summary>
	/// Initialize pushwalls from MapAnalysis data.
	/// Pushwalls start in their initial positions and can be activated during gameplay.
	/// Based on WL_GAME.C:ScanInfoPlane pushwall initialization.
	/// </summary>
	public void LoadPushWallsFromMapAnalysis(IEnumerable<MapAnalysis.PushWallSpawn> pushWallSpawns)
	{
		pushWalls.Clear();
		int pushWallIndex = 0;
		foreach (MapAnalysis.PushWallSpawn spawn in pushWallSpawns)
		{
			pushWalls.Add(new PushWall(spawn.Shape, spawn.X, spawn.Y));

			// Update spatial index - pushwall occupies its initial tile
			int tileIdx = GetTileIndex(spawn.X, spawn.Y);
			pushWallAtTile[tileIdx] = (short)pushWallIndex;
			pushWallIndex++;
		}
	}
	/// <summary>
	/// Initialize static bonus objects from MapAnalyzer data.
	/// Based on WL_GAME.C:ScanInfoPlane and WL_ACT1.C:InitStaticList
	/// Populates StatObjList for gameplay (collision/pickup detection).
	/// Emits BonusSpawnedEvent for each bonus so VR layer can display them.
	/// </summary>
	public void LoadBonusesFromMapAnalysis(MapAnalysis mapAnalysis)
	{
		// WL_ACT1.C:InitStaticList - reset to beginning
		lastStatObj = 0;
		// Initialize all slots as free (ShapeNum = -1)
		for (int i = 0; i < StatObj.MAXSTATS; i++)
			StatObjList[i] = new StatObj();
		// WL_GAME.C:ScanInfoPlane - spawn static bonus objects from map
		IEnumerable<MapAnalysis.StaticSpawn> staticBonuses = mapAnalysis.StaticSpawns
			.Where(s => s.StatType == StatType.bonus);
		foreach (MapAnalysis.StaticSpawn spawn in staticBonuses)
		{
			if (lastStatObj >= StatObj.MAXSTATS)
				// Too many static objects - this would be a Quit() in original
				// For now, just stop spawning (should never happen with proper maps)
				break;
			// Create the bonus object in StatObjList for gameplay tracking
			// WL_DEF.H:statstruct - note: shapenum is signed, can be -1
			StatObjList[lastStatObj] = new StatObj(
				spawn.X,
				spawn.Y,
				(short)spawn.Shape,  // Cast ushort to short (safe, shape numbers are small)
				0,  // flags (FL_BONUS would be set here, but we'll set it when needed)
				(byte)spawn.Type);  // itemnumber (ObClass enum -> byte)

			// Emit spawn event for VR layer to display the bonus
			// Shape values: -2 = invisible trigger (Noah's Ark), >= 0 = visible
			BonusSpawned?.Invoke(new BonusSpawnedEvent
			{
				StatObjIndex = lastStatObj,
				Shape = (short)spawn.Shape,
				TileX = spawn.X,
				TileY = spawn.Y,
				ItemNumber = (byte)spawn.Type
			});

			lastStatObj++;
		}
	}
	/// <summary>
	/// Initialize actors from MapAnalyzer data - creates Actor instances and fires ActorSpawnedEvent for each.
	/// Based on WL_GAME.C:ScanInfoPlane
	/// </summary>
	/// <param name="mapAnalysis">Map analysis containing actor spawn data</param>
	/// <param name="actorInitialStates">Dictionary mapping actor types to their initial state names</param>
	/// <param name="actorHitPoints">Dictionary mapping actor types to their initial hit points</param>
	public void LoadActorsFromMapAnalysis(
		MapAnalysis mapAnalysis,
		Dictionary<string, string> actorInitialStates,
		Dictionary<string, short> actorHitPoints)
	{
		// Note: mapAnalysis, mapWidth, mapHeight, and spatial arrays are already initialized by LoadDoorsFromMapAnalysis
		actors.Clear();
		int actorIndex = 0;
		foreach (MapAnalysis.ActorSpawn spawn in mapAnalysis.ActorSpawns)
		{
			// Use initial state from spawn data (from ObjectType XML)
			string initialStateName = spawn.InitialState;

			// Fall back to actorInitialStates dictionary if no state specified in spawn
			if (string.IsNullOrEmpty(initialStateName))
			{
				if (!actorInitialStates.TryGetValue(spawn.ActorType, out initialStateName))
				{
					System.Diagnostics.Debug.WriteLine($"Warning: No initial state defined for actor type '{spawn.ActorType}', skipping");
					continue;
				}
			}

			if (!stateCollection.States.TryGetValue(initialStateName, out State initialState))
			{
				System.Diagnostics.Debug.WriteLine($"Warning: Initial state '{initialStateName}' not found for actor type '{spawn.ActorType}', skipping");
				continue;
			}
			// Look up initial hit points for this actor type
			if (!actorHitPoints.TryGetValue(spawn.ActorType, out short hitPoints))
				// Default to 1 hitpoint if not defined
				hitPoints = 1;
			// Create actor instance
			Actor actor = new(
				actorType: spawn.ActorType,
				initialState: initialState,
				tileX: spawn.X,
				tileY: spawn.Y,
				facing: spawn.Facing,
				hitPoints: hitPoints);
			// Set additional flags from spawn data
			if (spawn.Ambush)
				actor.Flags |= ActorFlags.Ambush;
			if (spawn.Patrol)
				actor.Flags |= ActorFlags.Patrolling;
			actors.Add(actor);

			// WL_ACT2.C: Two spawn patterns based on initial state's movement properties
			// SpawnPatrol (line 1246): Speed > 0, initializes for immediate movement
			//   - Clear spawn tile, move TileX/TileY to adjacent, set Distance=TILEGLOBAL
			// SpawnStand: Speed = 0, stationary until state changes
			//   - Stay at spawn tile, Distance remains 0
			bool needsMovementInit = initialState.Speed > 0;
			if (needsMovementInit)
			{
				// WL_ACT2.C:1246-1262 - Initialize actor ready to move
				int spawnTileIdx = GetTileIndex(actor.TileX, actor.TileY);
				actorAtTile[spawnTileIdx] = -1; // Clear spawn tile
				// Move TileX/TileY to destination (adjacent tile in facing direction)
				// This sets up ob->tilex/tiley to point to the first destination
				switch (spawn.Facing)
				{
					case Direction.E: actor.TileX++; break;
					case Direction.NE: actor.TileX++; actor.TileY--; break;
					case Direction.N: actor.TileY--; break;
					case Direction.NW: actor.TileX--; actor.TileY--; break;
					case Direction.W: actor.TileX--; break;
					case Direction.SW: actor.TileX--; actor.TileY++; break;
					case Direction.S: actor.TileY++; break;
					case Direction.SE: actor.TileX++; actor.TileY++; break;
				}
				// WL_ACT2.C:1242 - Set distance to move one full tile
				actor.Distance = 0x10000; // TILEGLOBAL
				// Claim destination tile
				int destTileIdx = GetTileIndex(actor.TileX, actor.TileY);
				actorAtTile[destTileIdx] = (short)actorIndex;
			}
			// Note: Stationary actors don't claim spawn tile - they'll be added to actorat
			// when they first move (via the clear-think-set pattern in UpdateActors)

			// Execute initial Action function if present
			if (!string.IsNullOrEmpty(initialState.Action))
			{
				Lua.ActorScriptContext context = new(this, actor, actorIndex, rng, gameClock, mapAnalysis, logger);
				try
				{
					luaScriptEngine.ExecuteStateFunction(initialState.Action, context);
				}
				catch (Exception ex)
				{
					logger?.LogError(ex, "Error executing Lua function for actor {ActorIndex}: {ErrorMessage}", actorIndex, ex.Message);
				}
			}
			// Fire spawn event - presentation layer will create visual representation
			ActorSpawned?.Invoke(new ActorSpawnedEvent
			{
				ActorIndex = actorIndex,
				TileX = spawn.X,
				TileY = spawn.Y,
				Facing = spawn.Facing,
				Shape = (ushort)actor.ShapeNum,
				IsRotated = initialState.Rotate
			});
			actorIndex++;
		}
		// Load patrol points automatically (actors need them for patrol movement)
		LoadPatrolPoints(mapAnalysis);
	}
	/// <summary>
	/// Initialize patrol direction lookup from MapAnalysis.PatrolPoints.
	/// Called automatically by LoadActorsFromMapAnalysis.
	/// WL_ACT2.C:SelectPathDir reads patrol arrows from map layer 1 via MAPSPOT macro.
	/// </summary>
	private void LoadPatrolPoints(MapAnalysis mapAnalysis)
	{
		patrolDirectionAtTile = [];
		foreach (MapAnalysis.PatrolPoint patrolPoint in mapAnalysis.PatrolPoints)
		{
			uint tileKey = ((uint)patrolPoint.Y << 16) | patrolPoint.X;
			patrolDirectionAtTile[tileKey] = patrolPoint.Turn;
		}
	}
	/// <summary>
	/// Helper for ActorScriptContext.SelectPathDir() - looks up patrol direction at a tile.
	/// </summary>
	public bool TryGetPatrolDirection(ushort tileX, ushort tileY, out Direction direction)
	{
		if (patrolDirectionAtTile is not null)
		{
			uint tileKey = ((uint)tileY << 16) | tileX;
			return patrolDirectionAtTile.TryGetValue(tileKey, out direction);
		}
		direction = default;
		return false;
	}
	/// <summary>
	/// Helper for ActorScriptContext.MoveObj() - fires ActorMovedEvent.
	/// </summary>
	public void FireActorMovedEvent(int actorIndex, int x, int y, Direction? facing)
	{
		ActorMoved?.Invoke(new ActorMovedEvent
		{
			ActorIndex = actorIndex,
			X = x,
			Y = y,
			Facing = facing
		});
	}

	/// <summary>
	/// Emits current state events for all entities after deserialization.
	/// Fixed-count entities (doors, push walls) get state change events.
	/// Variable-count entities (bonuses, actors) get spawn events.
	/// Used for restoring presentation layer after loading a saved game.
	/// </summary>
	public void EmitAllEntityState()
	{
		// Emit door position events (NOT spawn events - doors are created from MapAnalysis)
		for (int i = 0; i < doors.Count; i++)
		{
			Door door = doors[i];
			// Emit current door position/state event
			DoorPositionChanged?.Invoke(new DoorPositionChangedEvent
			{
				DoorIndex = (ushort)i,
				TileX = door.TileX,
				TileY = door.TileY,
				Position = door.Position,
				Action = door.Action
			});
		}

		// Emit push wall position events for pushwalls that have moved
		// Push walls are fixed-count entities (created from MapAnalysis.PushWalls)
		// but need position events to show movement after being pushed
		for (int i = 0; i < pushWalls.Count; i++)
		{
			PushWall pushWall = pushWalls[i];
			// Only emit if pushwall has moved from initial position
			if (pushWall.Action == PushWallAction.Pushing ||
				pushWall.X != ((pushWall.InitialTileX << 16) + 0x8000) ||
				pushWall.Y != ((pushWall.InitialTileY << 16) + 0x8000))
			{
				PushWallPositionChanged?.Invoke(new PushWallPositionChangedEvent
				{
					PushWallIndex = (ushort)i,
					X = pushWall.X,
					Y = pushWall.Y,
					Action = pushWall.Action
				});
			}
		}

		// Emit bonus SPAWN events for all active bonuses
		// WL_DEF.H: shapenum = -1 means despawned (entire object removed - skip event)
		// Noah's Ark: shapenum = -2 means invisible trigger (emit event, no visual)
		// shapenum >= 0 means visible bonus (emit event, render sprite)
		for (int i = 0; i < StatObjList.Length; i++)
		{
			// Skip despawned bonuses: null, IsFree, or ShapeNum == -1
			if (StatObjList[i] is null || StatObjList[i].IsFree || StatObjList[i].ShapeNum == -1)
				continue;

			// Emit event for both invisible triggers (-2) and visible bonuses (>= 0)
			BonusSpawned?.Invoke(new BonusSpawnedEvent
			{
				StatObjIndex = i,
				Shape = StatObjList[i].ShapeNum,
				TileX = StatObjList[i].TileX,
				TileY = StatObjList[i].TileY,
				ItemNumber = StatObjList[i].ItemNumber
			});
		}

		// Emit actor SPAWN events for all active actors
		for (int i = 0; i < actors.Count; i++)
		{
			Actor actor = actors[i];
			ActorSpawned?.Invoke(new ActorSpawnedEvent
			{
				ActorIndex = i,
				TileX = actor.TileX,
				TileY = actor.TileY,
				Facing = actor.Facing,
				Shape = (ushort)actor.ShapeNum,
				IsRotated = actor.CurrentState?.Rotate ?? false
			});

			// Also emit current actor sprite state
			ActorSpriteChanged?.Invoke(new ActorSpriteChangedEvent
			{
				ActorIndex = i,
				Shape = (ushort)actor.ShapeNum,
				IsRotated = actor.CurrentState?.Rotate ?? false
			});
		}
	}

	/// <summary>
	/// Emits an actor sound event - sound will be attached to the actor.
	/// Called from ActorScriptContext.
	/// WL_STATE.C:PlaySoundLocActor
	/// </summary>
	/// <param name="actorIndex">Index of the actor playing the sound</param>
	/// <param name="soundName">Sound name (e.g., "HALTSND")</param>
	public void EmitActorPlaySound(int actorIndex, string soundName)
	{
		ActorPlaySound?.Invoke(new ActorPlaySoundEvent
		{
			ActorIndex = actorIndex,
			SoundName = soundName,
			SoundId = -1 // Name-based lookup
		});
	}

	/// <summary>
	/// Emits a door sound event - sound will be attached to the door.
	/// WL_ACT1.C:DoorOpening, DoorClosing
	/// </summary>
	/// <param name="doorIndex">Index of the door playing the sound</param>
	/// <param name="soundName">Sound name (e.g., "OPENDOORSND")</param>
	public void EmitDoorPlaySound(ushort doorIndex, string soundName)
	{
		DoorPlaySound?.Invoke(new DoorPlaySoundEvent
		{
			DoorIndex = doorIndex,
			SoundName = soundName,
			SoundId = -1
		});
	}

	/// <summary>
	/// Emits a global (non-positional) sound event.
	/// For UI sounds, music, narrator, etc.
	/// </summary>
	/// <param name="soundName">Sound name (e.g., "BONUS1SND")</param>
	public void EmitGlobalSound(string soundName)
	{
		PlayGlobalSound?.Invoke(new PlayGlobalSoundEvent
		{
			SoundName = soundName,
			SoundId = -1
		});
	}

	/// <summary>
	/// Emits a pushwall sound event - sound will be attached to the pushwall.
	/// WL_ACT1.C pushwall activation
	/// </summary>
	/// <param name="pushWallIndex">Index of the pushwall playing the sound</param>
	/// <param name="soundName">Sound name (e.g., "PUSHWALLSND")</param>
	public void EmitPushWallPlaySound(ushort pushWallIndex, string soundName)
	{
		PushWallPlaySound?.Invoke(new PushWallPlaySoundEvent
		{
			PushWallIndex = pushWallIndex,
			SoundName = soundName,
			SoundId = -1
		});
	}

	#region Spatial Index
	/// <summary>
	/// Converts tile coordinates to array index for spatial index arrays.
	/// Based on WL_DEF.H actorat array indexing.
	/// </summary>
	private int GetTileIndex(ushort x, ushort y) => y * mapWidth + x;

	/// <summary>
	/// Checks if a tile is transparent for line-of-sight purposes.
	/// Combines static map transparency with dynamic obstacle checks.
	/// WL_STATE.C:CheckLine - used for enemy vision and hearing
	/// </summary>
	/// <param name="x">Tile X coordinate</param>
	/// <param name="y">Tile Y coordinate</param>
	/// <returns>True if the tile doesn't block line of sight</returns>
	public bool IsTileTransparentForSight(ushort x, ushort y)
	{
		// 1. Check static map transparency (walls, etc.) from MapAnalysis
		if (!mapAnalysis.IsTransparent(x, y))
			return false;
		int tileIdx = GetTileIndex(x, y);
		// 2. Check for pushwalls - they block sight
		if (pushWallAtTile[tileIdx] >= 0)
			return false;
		// 3. Check for closed doors - they block sight
		// Only fully closed doors block line of sight
		if (doorAtTile[tileIdx] >= 0)
		{
			Door door = doors[doorAtTile[tileIdx]];
			// A door in the Closed state blocks sight
			// Opening, Open, and Closing states can be seen (and shot) through
			if (door.Action == DoorAction.Closed)
				return false;
		}
		return true;
	}
	/// <summary>
	/// Checks if a tile is navigable (can be moved onto).
	/// Combines static map analysis with dynamic state (doors, pushwalls, actors).
	/// Based on Wolf3D collision detection logic.
	/// </summary>
	/// <param name="x">Tile X coordinate</param>
	/// <param name="y">Tile Y coordinate</param>
	/// <returns>True if the tile can be moved onto</returns>
	public bool IsTileNavigable(ushort x, ushort y)
	{
		// 1. Check static navigability (from MapAnalysis BitArray)
		if (!mapAnalysis.IsNavigable(x, y))
			return false;

		int tileIdx = GetTileIndex(x, y);

		// 2. Check for closed doors
		if (doorAtTile[tileIdx] >= 0)
		{
			Door door = doors[doorAtTile[tileIdx]];
			if (door.Action == DoorAction.Closed)
				return false;
		}

		// 3. Check for pushwalls
		if (pushWallAtTile[tileIdx] >= 0)
			return false;

		// 4. Check for living actors
		if (actorAtTile[tileIdx] >= 0)
		{
			Actor actor = actors[actorAtTile[tileIdx]];
			if (actor.HitPoints > 0)
				return false;
		}

		return true;
	}

	/// <summary>
	/// Get the door index at a specific tile.
	/// WL_STATE.C:TryWalk uses this to detect doors during pathfinding.
	/// </summary>
	/// <param name="x">Tile X coordinate</param>
	/// <param name="y">Tile Y coordinate</param>
	/// <returns>Door index (0-based) or -1 if no door at this tile</returns>
	public int GetDoorIndexAtTile(ushort x, ushort y)
	{
		int tileIdx = GetTileIndex(x, y);
		return doorAtTile[tileIdx];
	}
	#endregion
}
