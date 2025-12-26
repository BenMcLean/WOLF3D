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
	public const double TicRate = 70.0, // Hz
		TicDuration = 1.0 / TicRate; // ~14.2857ms
	public const int MaxTicsPerUpdate = 10; // Prevent spiral of death
	private double accumulatedTime;
	private readonly List<PlayerAction> pendingActions = [];
	public IReadOnlyList<Door> Doors => doors;
	private readonly List<Door> doors = [];
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
	private readonly Scripting.LuaScriptEngine luaScriptEngine;
	// RNG and GameClock for deterministic simulation
	private readonly RNG rng;
	private readonly GameClock gameClock;
	// Map analyzer for accessing door metadata (sounds, etc.)
	private MapAnalyzer mapAnalyzer;
	// Map analysis for line-of-sight calculations and navigation
	private MapAnalysis mapAnalysis;
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
	#endregion
	/// <summary>
	/// Creates a new Simulator instance.
	/// </summary>
	/// <param name="stateCollection">State machine data for actors</param>
	/// <param name="rng">Deterministic random number generator</param>
	/// <param name="gameClock">Deterministic game clock</param>
	public Simulator(StateCollection stateCollection, RNG rng, GameClock gameClock)
	{
		this.stateCollection = stateCollection ?? throw new ArgumentNullException(nameof(stateCollection));
		this.rng = rng ?? throw new ArgumentNullException(nameof(rng));
		this.gameClock = gameClock ?? throw new ArgumentNullException(nameof(gameClock));
		// Initialize Lua script engine and compile all state functions
		luaScriptEngine = new Scripting.LuaScriptEngine();
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
	public void QueueAction(PlayerAction action)=>pendingActions.Add(action);
	private void ProcessTic()
	{
		// Process queued player actions
		foreach (PlayerAction action in pendingActions)
			ProcessAction(action);
		pendingActions.Clear();
		// WL_ACT1.C:MoveDoors - update all doors
		for (int i = 0; i < doors.Count; i++)
			UpdateDoor(i);
		// WL_ACT2.C:DoActor - update all actors
		for (int i = 0; i < actors.Count; i++)
			UpdateActor(i);
	}
	private void ProcessAction(PlayerAction action)
	{
		if (action is OperateDoorAction operateDoor)
			OperateDoor(operateDoor.DoorIndex);
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
			// Door already open, just reset the timer (WL_ACT1.C:549)
			door.TicCount = 0;
		else
		{
			// Start opening (WL_ACT1.C:551)
			door.Action = DoorAction.Opening;
			// Emit opening event and sound immediately (matches CloseDoor behavior)
			DoorOpening?.Invoke(new DoorOpeningEvent
			{
				Timestamp = CurrentTic * TicDuration,
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
		if (door.TicCount >= Door.OpenTics)
			CloseDoor((ushort)doorIndex);
	}
	/// <summary>
	/// WL_ACT1.C:DoorOpening (line 700)
	/// </summary>
	private void UpdateDoorOpening(int doorIndex)
	{
		Door door = doors[doorIndex];
		int newPosition = door.Position;
		// Note: DoorOpening event and sound are now emitted in OpenDoor() immediately
		// when the door starts opening, regardless of position (matches CloseDoor behavior)
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
			door.Position = (ushort)newPosition;
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
			door.Position = (ushort)newPosition;
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
	#region Actor Update Logic
	/// <summary>
	/// WL_PLAY.C actor update loop (lines 1690-1774)
	/// Handles both transitional (tictime > 0) and non-transitional (tictime == 0) states
	/// </summary>
	private void UpdateActor(int actorIndex)
	{
		Actor actor = actors[actorIndex];

		// WL_PLAY.C:1696 - Non-transitional object (tictime == 0)
		// These states execute Think every frame but never auto-transition
		if (actor.TicCount == 0)
		{
			// Execute Think function if present
			if (!string.IsNullOrEmpty(actor.CurrentState.Think))
			{
				Scripting.ActorScriptContext context = new Scripting.ActorScriptContext(this, actor, actorIndex, rng, gameClock, mapAnalysis);
				try
				{
					luaScriptEngine.ExecuteStateFunction(actor.CurrentState.Think, context);
				}
				catch (Exception ex)
				{
					// Silently ignore - likely unimplemented function
				}
			}
			// WL_PLAY.C:1726 - Return early, don't transition
			return;
		}

		// WL_PLAY.C:1732 - Transitional object (tictime > 0)
		// Decrement tic counter
		actor.TicCount--;

		// WL_PLAY.C:1733 - Check for state transition
		while (actor.TicCount <= 0)
		{
			// Execute Action function if present (end of state action)
			if (!string.IsNullOrEmpty(actor.CurrentState.Action))
			{
				Scripting.ActorScriptContext context = new Scripting.ActorScriptContext(this, actor, actorIndex, rng, gameClock, mapAnalysis);
				try
				{
					luaScriptEngine.ExecuteStateFunction(actor.CurrentState.Action, context);
				}
				catch (Exception ex)
				{
					// Silently ignore - likely unimplemented function
				}
			}

			// WL_PLAY.C:1746 - Transition to next state
			if (actor.CurrentState.Next == null)
				break; // No next state, stop transitioning

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
			Scripting.ActorScriptContext context = new(this, actor, actorIndex, rng, gameClock, mapAnalysis);
			try
			{
				luaScriptEngine.ExecuteStateFunction(actor.CurrentState.Think, context);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Error executing Think function '{actor.CurrentState.Think}' for actor {actorIndex}: {ex.Message}");
			}
		}
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
			Scripting.ActorScriptContext context = new Scripting.ActorScriptContext(this, actor, actorIndex, rng, gameClock, mapAnalysis);
			try
			{
				luaScriptEngine.ExecuteStateFunction(nextState.Action, context);
			}
			catch (Exception ex)
			{
				// Silently ignore - likely unimplemented function
			}
		}
		// Fire sprite changed event if sprite actually changed
		if (oldShape != actor.ShapeNum)
		{
			ActorSpriteChanged?.Invoke(new ActorSpriteChangedEvent
			{
				Timestamp = CurrentTic * TicDuration,
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
		IEnumerable<MapAnalysis.DoorSpawn> doorSpawns)
	{
		// Store MapAnalyzer for looking up door sounds when emitting events
		this.mapAnalyzer = mapAnalyzer ?? throw new ArgumentNullException(nameof(mapAnalyzer));

		doors.Clear();
		foreach (MapAnalysis.DoorSpawn spawn in doorSpawns)
			doors.Add(new Door(spawn.X, spawn.Y, spawn.FacesEastWest, spawn.TileNumber));
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
			BonusSpawned?.Invoke(new BonusSpawnedEvent
			{
				Timestamp = CurrentTic,
				StatObjIndex = lastStatObj,
				Shape = spawn.Shape,
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
		// Store map analysis for line-of-sight calculations
		this.mapAnalysis = mapAnalysis;

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
			// Convert 4-way cardinal direction from map data to 8-way simulator direction
			Direction facing = spawn.Facing.ToSimulatorDirection();
			// Create actor instance
			Actor actor = new Actor(
				spawn.ActorType,
				initialState,
				spawn.X,
				spawn.Y,
				facing,
				hitPoints);
			// Set additional flags from spawn data
			if (spawn.Ambush)
				actor.Flags |= ActorFlags.Ambush;
			if (spawn.Patrol)
				actor.Flags |= ActorFlags.Patrolling;
			actors.Add(actor);
			// Execute initial Action function if present
			if (!string.IsNullOrEmpty(initialState.Action))
			{
				Scripting.ActorScriptContext context = new Scripting.ActorScriptContext(this, actor, actorIndex, rng, gameClock, mapAnalysis);
				try
				{
					luaScriptEngine.ExecuteStateFunction(initialState.Action, context);
				}
				catch (Exception ex)
				{
					// Silently ignore - likely unimplemented function
				}
			}
			// Fire spawn event - presentation layer will create visual representation
			ActorSpawned?.Invoke(new ActorSpawnedEvent
			{
				Timestamp = CurrentTic * TicDuration,
				ActorIndex = actorIndex,
				TileX = spawn.X,
				TileY = spawn.Y,
				Facing = facing,
				Shape = (ushort)actor.ShapeNum,
				IsRotated = initialState.Rotate
			});
			actorIndex++;
		}
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
				Timestamp = CurrentTic,
				DoorIndex = (ushort)i,
				TileX = door.TileX,
				TileY = door.TileY,
				Position = door.Position,
				Action = door.Action
			});
		}

		// TODO: Emit push wall position events when push walls are implemented
		// Push walls are fixed-count entities (created from MapAnalysis.PushWalls)
		// but need position events to show movement after being pushed
		// for (int i = 0; i < pushWalls.Count; i++)
		// {
		//     PushWall pushWall = pushWalls[i];
		//     if (pushWall.HasMoved)
		//     {
		//         PushWallPositionChanged?.Invoke(new PushWallPositionChangedEvent
		//         {
		//             Timestamp = CurrentTic,
		//             InitialTileX = pushWall.InitialTileX,
		//             InitialTileY = pushWall.InitialTileY,
		//             Direction = pushWall.Direction,
		//             Progress = pushWall.Progress
		//         });
		//     }
		// }

		// Emit bonus SPAWN events for all active bonuses
		for (int i = 0; i < StatObjList.Length; i++)
		{
			if (StatObjList[i] is not null && !StatObjList[i].IsFree)
			{
				BonusSpawned?.Invoke(new BonusSpawnedEvent
				{
					Timestamp = CurrentTic,
					StatObjIndex = i,
					Shape = (ushort)StatObjList[i].ShapeNum,
					TileX = StatObjList[i].TileX,
					TileY = StatObjList[i].TileY,
					ItemNumber = StatObjList[i].ItemNumber
				});
			}
		}

		// Emit actor SPAWN events for all active actors
		for (int i = 0; i < actors.Count; i++)
		{
			Actor actor = actors[i];
			ActorSpawned?.Invoke(new ActorSpawnedEvent
			{
				Timestamp = CurrentTic * TicDuration,
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
				Timestamp = CurrentTic * TicDuration,
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
			Timestamp = CurrentTic * TicDuration,
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
			Timestamp = CurrentTic * TicDuration,
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
			Timestamp = CurrentTic * TicDuration,
			SoundName = soundName,
			SoundId = -1
		});
	}
}
