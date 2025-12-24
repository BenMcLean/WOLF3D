using System;
using System.Collections.Generic;
using System.Linq;
using BenMcLean.Wolf3D.Assets;
using static BenMcLean.Wolf3D.Assets.MapAnalyzer;

namespace BenMcLean.Wolf3D.Simulator;

/// <summary>
/// Discrete event simulator for Wolfenstein 3D game logic.
/// Runs at 70Hz tic rate matching the original game (WL_DRAW.C:CalcTics).
/// </summary>
public class Simulator
{
	public const double TicRate = 70.0; // Hz
	public const double TicDuration = 1.0 / TicRate; // ~14.2857ms
	public const int MaxTicsPerUpdate = 10; // Prevent spiral of death

	private double accumulatedTime;
	private readonly List<PlayerAction> pendingActions = [];

	public IReadOnlyList<Door> Doors => doors;
	private readonly List<Door> doors = [];

	// WL_ACT1.C:statobjlist[MAXSTATS]
	// Array of bonus/pickup objects (not fixtures - those are display-only)
	public StatObj[] StatObjList { get; private set; } = new StatObj[StatObj.MAXSTATS];

	// WL_ACT1.C:laststatobj - pointer to next free slot
	private int lastStatObj;

	// WL_PLAY.C:tics (unsigned = 16-bit in original DOS, but we accumulate as long)
	// Current simulation time in tics
	public long CurrentTic { get; private set; }

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
	#endregion

	/// <summary>
	/// Update the simulation with elapsed real time.
	/// Events are dispatched to subscribers via C# events as they occur.
	/// Based on WL_PLAY.C:PlayLoop and WL_DRAW.C:CalcTics.
	/// </summary>
	public void Update(double deltaTime)
	{
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
	public void QueueAction(PlayerAction action)
	{
		pendingActions.Add(action);
	}

	private void ProcessTic()
	{
		// Process queued player actions
		foreach (PlayerAction action in pendingActions)
		{
			ProcessAction(action);
		}
		pendingActions.Clear();

		// WL_ACT1.C:MoveDoors - update all doors
		for (int i = 0; i < doors.Count; i++)
		{
			UpdateDoor(i);
		}
	}

	private void ProcessAction(PlayerAction action)
	{
		if (action is OperateDoorAction operateDoor)
		{
			OperateDoor(operateDoor.DoorIndex);
		}
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
	private void OpenDoor(ushort doorIndex)
	{
		Door door = doors[doorIndex];

		if (door.Action == DoorAction.Open)
		{
			// Door already open, just reset the timer (WL_ACT1.C:549)
			door.TicCount = 0;
		}
		else
		{
			// Start opening (WL_ACT1.C:551)
			door.Action = DoorAction.Opening;
		}
	}

	/// <summary>
	/// WL_ACT1.C:CloseDoor (line 563)
	/// </summary>
	private void CloseDoor(ushort doorIndex)
	{
		Door door = doors[doorIndex];

		// TODO: Check for blocking actors/player (WL_ACT1.C:574-611)
		// For now, just start closing
		door.Action = DoorAction.Closing;

		DoorClosing?.Invoke(new DoorClosingEvent
		{
			Timestamp = CurrentTic * TicDuration,
			DoorIndex = doorIndex,
			TileX = door.TileX,
			TileY = door.TileY
		});
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
		if (door.TicCount >= Door.OpenTics)
		{
			CloseDoor((ushort)doorIndex);
		}
	}

	/// <summary>
	/// WL_ACT1.C:DoorOpening (line 700)
	/// </summary>
	private void UpdateDoorOpening(int doorIndex)
	{
		Door door = doors[doorIndex];
		int newPosition = door.Position;

		// WL_ACT1.C:707 - door just starting to open
		if (newPosition == 0)
		{
			// Emit opening event
			DoorOpening?.Invoke(new DoorOpeningEvent
			{
				Timestamp = CurrentTic * TicDuration,
				DoorIndex = (ushort)doorIndex,
				TileX = door.TileX,
				TileY = door.TileY
			});

			// TODO: WL_ACT1.C:710-733 - connect areas for sound/sight
		}

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
				Timestamp = CurrentTic * TicDuration,
				DoorIndex = (ushort)doorIndex,
				TileX = door.TileX,
				TileY = door.TileY
			});

			// TODO: WL_ACT1.C:748 - clear actorat for door tile
		}
		else
		{
			door.Position = (ushort)newPosition;
		}

		// Emit position changed event every tic
		DoorPositionChanged?.Invoke(new DoorPositionChangedEvent
		{
			Timestamp = CurrentTic * TicDuration,
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
		int newPosition = door.Position;

		// TODO: WL_ACT1.C:773-778 - check if something is blocking the door
		// If blocked, call OpenDoor(doorIndex) and return

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

			DoorClosed?.Invoke(new DoorClosedEvent
			{
				Timestamp = CurrentTic * TicDuration,
				DoorIndex = (ushort)doorIndex,
				TileX = door.TileX,
				TileY = door.TileY
			});

			// TODO: WL_ACT1.C:795-813 - disconnect areas
		}
		else
		{
			door.Position = (ushort)newPosition;
		}

		// Emit position changed event every tic
		DoorPositionChanged?.Invoke(new DoorPositionChangedEvent
		{
			Timestamp = CurrentTic * TicDuration,
			DoorIndex = (ushort)doorIndex,
			TileX = door.TileX,
			TileY = door.TileY,
			Position = door.Position,
			Action = door.Action
		});
	}

	/// <summary>
	/// Initialize doors from MapAnalyzer data.
	/// Looks up door properties directly from MapAnalyzer.Doors dictionary.
	/// </summary>
	public void LoadDoorsFromMapAnalysis(
		IEnumerable<MapAnalysis.DoorSpawn> doorSpawns)
	{
		doors.Clear();
		foreach (MapAnalysis.DoorSpawn spawn in doorSpawns)
			doors.Add(new Door(spawn.X, spawn.Y, spawn.FacesEastWest));
	}

	/// <summary>
	/// Initialize static bonus objects from MapAnalyzer data.
	/// Based on WL_GAME.C:ScanInfoPlane and WL_ACT1.C:InitStaticList
	/// Populates StatObjList for gameplay (collision/pickup detection).
	/// Does NOT emit events - VR layer displays static bonuses directly from MapAnalysis.
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
			{
				// Too many static objects - this would be a Quit() in original
				// For now, just stop spawning (should never happen with proper maps)
				break;
			}

			// Create the bonus object in StatObjList for gameplay tracking
			// WL_DEF.H:statstruct - note: shapenum is signed, can be -1
			StatObjList[lastStatObj] = new StatObj(
				spawn.X,
				spawn.Y,
				(short)spawn.Shape,  // Cast ushort to short (safe, shape numbers are small)
				0,  // flags (FL_BONUS would be set here, but we'll set it when needed)
				(byte)spawn.Type);  // itemnumber (ObClass enum -> byte)

			lastStatObj++;
		}
	}

	/// <summary>
	/// Initialize actors from MapAnalyzer data - fires ActorSpawnedEvent for each.
	/// Based on WL_GAME.C:ScanInfoPlane
	/// NOTE: This is a DUMMY implementation - just fires events, no actual Actor objects yet.
	/// Full actor simulation logic (state machine, AI, movement) will be added later.
	/// </summary>
	public void LoadActorsFromMapAnalysis(MapAnalysis mapAnalysis)
	{
		// int actorIndex = 0;
		// foreach (MapAnalysis.ActorSpawn spawn in mapAnalysis.ActorSpawns)
		// {
		// 	// Use initial sprite page from spawn data (from XML ObjectType Page attribute)
		// 	// Later: Look up sprite from initial actor state when state machine is implemented
		// 	ushort initialShape = spawn.Page;
		// 	// TODO: Determine IsRotated from actor state (walking/standing = true, shooting/dying = false)
		// 	// For now, assume standing sprites are rotated (8-directional)
		// 	bool isRotated = true;  // PLACEHOLDER - will be determined by initial state
		// 	// Convert 4-way cardinal direction from map data to 8-way simulator direction
		// 	Direction facing = ConvertCardinalToSimulatorDirection(spawn.Facing);
		// 	// Fire spawn event - presentation layer will create visual representation
		// 	ActorSpawned?.Invoke(new ActorSpawnedEvent
		// 	{
		// 		Timestamp = CurrentTic * TicDuration,
		// 		ActorIndex = actorIndex,
		// 		TileX = spawn.X,
		// 		TileY = spawn.Y,
		// 		Facing = facing,
		// 		Shape = initialShape,
		// 		IsRotated = isRotated
		// 	});
		// 	actorIndex++;
		// }
	}
}
