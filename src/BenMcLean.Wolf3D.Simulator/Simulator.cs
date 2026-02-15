using System;
using System.Collections.Generic;
using System.Linq;
using BenMcLean.Wolf3D.Assets.Gameplay;
using BenMcLean.Wolf3D.Simulator.Entities;
using BenMcLean.Wolf3D.Simulator.State;
using GameplayState = BenMcLean.Wolf3D.Assets.Gameplay.State;
using Microsoft.Extensions.Logging;
using static BenMcLean.Wolf3D.Assets.Gameplay.MapAnalyzer;

namespace BenMcLean.Wolf3D.Simulator;

/// <summary>
/// Discrete event simulator for Wolfenstein 3D game logic.
/// Runs at 70Hz tic rate matching the original game (WL_DRAW.C:CalcTics).
/// </summary>
public class Simulator : IStateSavable<SimulatorSnapshot>
{
	public const double TicRate = 70.0, // Hz
		TicDuration = 1.0 / TicRate; // ~14.2857ms
	public const int MaxTicsPerUpdate = 10; // Prevent spiral of death
	private double accumulatedTime;
	/// <summary>
	/// Game identifier (from XML Game Name attribute, e.g., "Wolfenstein 3-D").
	/// Set by the caller after construction. Used to validate save game compatibility.
	/// </summary>
	public string GameName { get; set; }
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
	// Weapon slots (configurable count - VR uses 2, traditional uses 1)
	// Based on WL_DEF.H:gametype:weapon/attackframe/attackcount
	public IReadOnlyList<WeaponSlot> WeaponSlots => weaponSlots;
	private readonly List<WeaponSlot> weaponSlots = [];
	// Weapon definitions (loaded from XML)
	private WeaponCollection weaponCollection;
	/// <summary>
	/// Gets the weapon definitions loaded from XML.
	/// Used by presentation layer for data-driven weapon mapping.
	/// </summary>
	public WeaponCollection WeaponCollection => weaponCollection;
	// State machine data for actors and weapons
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
	/// <summary>
	/// When true, player movement bypasses all collision checks.
	/// Based on original Wolf3D "MLI" debug/cheat command.
	/// </summary>
	public bool NoClip { get; set; } = false;
	/// <summary>
	/// When true, player is immune to all damage.
	/// Based on original Wolf3D god mode cheat.
	/// WL_PLAY.C:godmode
	/// </summary>
	public bool GodMode { get; set; } = false;
	/// <summary>
	/// When true, applies original Wolf3D distance-based miss chance on top of hit detection.
	/// VR sets this to false (pixel-perfect aiming handles accuracy).
	/// Traditional/Iso presentation layers set this to true.
	/// Based on WL_AGENT.C:GunAttack distance check.
	/// </summary>
	public bool UseAccuracyFalloff { get; set; } = true;
	// Derived player tile coordinates
	public ushort PlayerTileX => (ushort)(PlayerX >> 16);
	public ushort PlayerTileY => (ushort)(PlayerY >> 16);
	// Player inventory (health, score, lives, keys, ammo, weapons)
	// Unified dictionary-based system for moddability and serialization
	public Inventory Inventory { get; } = new();
	/// <summary>
	/// Callback for weapon hit detection. Presentation layer sets this to perform
	/// a raycast when the weapon state machine reaches a fire action.
	/// Called by A_PistolAttack, A_MachineGunAttack, A_ChainGunAttack, etc.
	/// Parameter: weapon slot index. Returns: hit actor index or null if miss.
	/// Based on WL_AGENT.C:GunAttack performing raycast on the fire frame in T_Attack.
	/// </summary>
	public Func<int, int?> HitDetection { get; set; }
	// Item scripts loaded from game config (maps script name to Lua code)
	private Dictionary<string, string> itemScripts = new();
	// Map from ItemNumber (byte) to script name for lookup
	private Dictionary<byte, string> itemNumberToScript = new();
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
	/// <summary>
	/// Fired when a weapon slot's sprite changes (animation frame).
	/// WL_AGENT.C:T_Attack animation progression
	/// </summary>
	public event Action<WeaponSpriteChangedEvent> WeaponSpriteChanged;
	/// <summary>
	/// Fired when a weapon is fired from a slot.
	/// WL_AGENT.C:Cmd_Fire
	/// </summary>
	public event Action<WeaponFiredEvent> WeaponFired;
	/// <summary>
	/// Fired when a weapon is equipped to a slot.
	/// WL_AGENT.C weapon selection logic
	/// </summary>
	public event Action<WeaponEquippedEvent> WeaponEquipped;
	/// <summary>
	/// Fired when an elevator switch is activated, triggering level completion.
	/// WL_AGENT.C:Cmd_Use elevator activation (line 1767)
	/// </summary>
	public event Action<ElevatorActivatedEvent> ElevatorActivated;
	/// <summary>
	/// Fired when an elevator switch texture should flip to pressed state.
	/// WL_AGENT.C:Cmd_Use - tilemap[checkx][checky]++
	/// </summary>
	public event Action<ElevatorSwitchFlippedEvent> ElevatorSwitchFlipped;
	/// <summary>
	/// Fired when player state changes (health, ammo, score, lives, keys, weapons).
	/// Used by presentation layer to update status bar/HUD.
	/// </summary>
	public event Action<PlayerStateChangedEvent> PlayerStateChanged;
	/// <summary>
	/// Fired when a bonus item plays a sound.
	/// WL_AGENT.C:GetBonus sound effects
	/// Presentation layer plays sound at item's position.
	/// </summary>
	public event Action<BonusPlaySoundEvent> BonusPlaySound;
	/// <summary>
	/// Fired when a screen flash effect should be displayed.
	/// WL_PLAY.C: Bonus flash, damage flash, etc.
	/// Presentation layer renders a color overlay that fades out.
	/// </summary>
	public event Action<ScreenFlashEvent> ScreenFlash;
	/// <summary>
	/// Fired when player health reaches zero.
	/// WL_AGENT.C:TakeDamage death check.
	/// Presentation layer handles death sequence and restart.
	/// </summary>
	public event Action<PlayerDiedEvent> PlayerDied;
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
		// Wire Inventory.ValueChanged to PlayerStateChanged for backwards compatibility
		Inventory.ValueChanged += (name, value) => EmitPlayerStateChanged();
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
		// Store previous position before update
		int prevX = PlayerX;
		int prevY = PlayerY;

		// Update player position from presentation layer
		PlayerX = playerX;
		PlayerY = playerY;

		// Check for item pickups when player position changes
		// WL_AGENT.C:ClipMove checks for bonus collision
		if (PlayerX != prevX || PlayerY != prevY)
			CheckItemPickups();

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
		// WL_AGENT.C:T_Attack - update all weapon slots
		for (int i = 0; i < weaponSlots.Count; i++)
			UpdateWeaponSlot(i);
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
		else if (action is ActivateElevatorAction activateElevator)
			ActivateElevator(activateElevator.TileX, activateElevator.TileY, activateElevator.Direction);
		else if (action is FireWeaponAction fireWeapon)
			ProcessFireWeapon(fireWeapon);
		else if (action is ReleaseWeaponTriggerAction releaseTrigger)
			ProcessReleaseTrigger(releaseTrigger);
		else if (action is EquipWeaponAction equipWeapon)
			EquipWeapon(equipWeapon.SlotIndex, equipWeapon.WeaponType);
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
				// Check door script before opening (for locked doors)
				if (CanOpenDoor(doorIndex, door))
					OpenDoor(doorIndex);
				break;
			case DoorAction.Open:
			case DoorAction.Opening:
				CloseDoor(doorIndex);
				break;
		}
	}

	/// <summary>
	/// Check if a door can be opened by running its script (if any).
	/// Scripts can check inventory for keys using Has("Gold Key"), etc.
	/// If no script is defined, the door can always be opened.
	/// </summary>
	private bool CanOpenDoor(ushort doorIndex, Door door)
	{
		// Look up door info to get script
		if (mapAnalyzer?.Doors.TryGetValue(door.TileNumber, out DoorInfo doorInfo) != true)
			return true; // No door info, allow open

		// If no script defined, door can be opened
		if (string.IsNullOrWhiteSpace(doorInfo.Script))
			return true;

		// Create script context for this door
		Lua.DoorScriptContext context = new(
			this,
			rng,
			gameClock,
			door.TileX,
			door.TileY,
			logger);

		// Wire up sound callbacks
		context.PlayAdLibSoundAction = soundName =>
			EmitDoorPlaySound(doorIndex, soundName);
		context.PlayDigiSoundAction = soundName =>
			EmitDoorPlaySound(doorIndex, soundName);

		try
		{
			// Execute the door script
			MoonSharp.Interpreter.DynValue result = luaScriptEngine.DoString(doorInfo.Script, context);

			// Script returns true if door can open, false if locked
			if (result.Type == MoonSharp.Interpreter.DataType.Boolean)
			{
				if (!result.Boolean)
				{
					// Door is locked - fire event (script handles sound via PlayLocalDigiSound)
					DoorLocked?.Invoke(new DoorLockedEvent
					{
						DoorIndex = doorIndex,
						RequiredKey = doorInfo.Key ?? "unknown"
					});
				}
				return result.Boolean;
			}

			// If script doesn't return a boolean, allow open
			return true;
		}
		catch (Exception ex)
		{
			logger?.LogError(ex, "Error executing door script for door at ({TileX}, {TileY})",
				door.TileX, door.TileY);
			// On error, allow open (safer default - don't trap player)
			return true;
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

		// Restore door to spatial index immediately when closing starts
		// This ensures collision detection blocks passage during the entire closing animation
		// (doorAtTile was cleared when door became fully Open)
		int tileIdx = GetTileIndex(door.TileX, door.TileY);
		doorAtTile[tileIdx] = (short)doorIndex;

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

		// Calculate trailing edge position BEFORE moving
		// Trailing edge is half a tile behind center, opposite to movement direction
		// This determines which tile the back of the pushwall is still in
		// NOTE: Use 0x7FFF when adding to stay INSIDE the current tile (not at boundary)
		// A position at exactly N*65536 is counted as tile N, so +0x8000 would be next tile
		int oldTrailingX = pushWall.X;
		int oldTrailingY = pushWall.Y;
		switch (pushWall.Direction)
		{
			case Direction.N: oldTrailingY += 0x7FFF; break; // Moving north, trailing edge is south (stay inside tile)
			case Direction.S: oldTrailingY -= 0x8000; break; // Moving south, trailing edge is north
			case Direction.E: oldTrailingX -= 0x8000; break; // Moving east, trailing edge is west
			case Direction.W: oldTrailingX += 0x7FFF; break; // Moving west, trailing edge is east (stay inside tile)
		}
		ushort oldTrailingTileX = (ushort)(oldTrailingX >> 16);
		ushort oldTrailingTileY = (ushort)(oldTrailingY >> 16);

		// Track leading edge tile BEFORE moving (for claiming new tiles)
		// NOTE: Use 0x7FFF when adding to stay INSIDE the current tile (not at boundary)
		int oldLeadingX = pushWall.X;
		int oldLeadingY = pushWall.Y;
		switch (pushWall.Direction)
		{
			case Direction.N: oldLeadingY -= 0x8000; break; // Moving north, leading edge is north
			case Direction.S: oldLeadingY += 0x7FFF; break; // Moving south, leading edge is south (stay inside tile)
			case Direction.E: oldLeadingX += 0x7FFF; break; // Moving east, leading edge is east (stay inside tile)
			case Direction.W: oldLeadingX -= 0x8000; break; // Moving west, leading edge is west
		}
		ushort oldLeadingTileX = (ushort)(oldLeadingX >> 16);
		ushort oldLeadingTileY = (ushort)(oldLeadingY >> 16);

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

		// Calculate trailing edge position AFTER moving
		// NOTE: Use 0x7FFF when adding to stay INSIDE the current tile (not at boundary)
		int newTrailingX = pushWall.X;
		int newTrailingY = pushWall.Y;
		switch (pushWall.Direction)
		{
			case Direction.N: newTrailingY += 0x7FFF; break; // Stay inside tile
			case Direction.S: newTrailingY -= 0x8000; break;
			case Direction.E: newTrailingX -= 0x8000; break;
			case Direction.W: newTrailingX += 0x7FFF; break; // Stay inside tile
		}
		ushort newTrailingTileX = (ushort)(newTrailingX >> 16);
		ushort newTrailingTileY = (ushort)(newTrailingY >> 16);

		// Calculate leading edge position AFTER moving
		// NOTE: Use 0x7FFF when adding to stay INSIDE the current tile (not at boundary)
		int newLeadingX = pushWall.X;
		int newLeadingY = pushWall.Y;
		switch (pushWall.Direction)
		{
			case Direction.N: newLeadingY -= 0x8000; break;
			case Direction.S: newLeadingY += 0x7FFF; break; // Stay inside tile
			case Direction.E: newLeadingX += 0x7FFF; break; // Stay inside tile
			case Direction.W: newLeadingX -= 0x8000; break;
		}
		ushort newLeadingTileX = (ushort)(newLeadingX >> 16);
		ushort newLeadingTileY = (ushort)(newLeadingY >> 16);

		// Claim new tile when LEADING EDGE enters it
		if (oldLeadingTileX != newLeadingTileX || oldLeadingTileY != newLeadingTileY)
		{
			int newIdx = GetTileIndex(newLeadingTileX, newLeadingTileY);
			pushWallAtTile[newIdx] = (short)pushWallIndex;
		}

		// Release old tile only when TRAILING EDGE has fully left it
		if (oldTrailingTileX != newTrailingTileX || oldTrailingTileY != newTrailingTileY)
		{
			int oldIdx = GetTileIndex(oldTrailingTileX, oldTrailingTileY);
			pushWallAtTile[oldIdx] = -1;
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

			// Clean up: release the initial tile and middle tile, keep only final tile
			// Pushwall moved 2 tiles from initial position, so clear initial and initial+1
			ushort initialX = pushWall.InitialTileX;
			ushort initialY = pushWall.InitialTileY;
			ushort middleX = initialX;
			ushort middleY = initialY;
			switch (pushWall.Direction)
			{
				case Direction.N: middleY--; break;
				case Direction.S: middleY++; break;
				case Direction.E: middleX++; break;
				case Direction.W: middleX--; break;
			}

			// Release initial and middle tiles (they may already be released, but ensure it)
			int initialIdx = GetTileIndex(initialX, initialY);
			int middleIdx = GetTileIndex(middleX, middleY);
			int finalIdx = GetTileIndex(finalX, finalY);

			if (pushWallAtTile[initialIdx] == pushWallIndex)
				pushWallAtTile[initialIdx] = -1;
			if (pushWallAtTile[middleIdx] == pushWallIndex)
				pushWallAtTile[middleIdx] = -1;

			// Ensure final tile is claimed
			pushWallAtTile[finalIdx] = (short)pushWallIndex;

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
	#region Elevator Logic
	/// <summary>
	/// Player attempts to activate an elevator switch.
	/// Based on WL_AGENT.C:Cmd_Use elevator activation logic (line 1767).
	/// </summary>
	/// <param name="tileX">Tile X coordinate of elevator switch</param>
	/// <param name="tileY">Tile Y coordinate of elevator switch</param>
	/// <param name="direction">Direction player is facing (determines which face is being activated)</param>
	private void ActivateElevator(ushort tileX, ushort tileY, Direction direction)
	{
		// Find elevator at this position and get its tile number for config lookup
		MapAnalysis.ElevatorSpawn? elevatorSpawn = null;
		foreach (var elev in mapAnalysis.Elevators)
		{
			if (elev.X == tileX && elev.Y == tileY)
			{
				elevatorSpawn = elev;
				break;
			}
		}

		if (!elevatorSpawn.HasValue)
			return; // No elevator at this position

		// Look up elevator configuration by tile number (supports multiple elevator types)
		if (!mapAnalyzer.Elevators.TryGetValue(elevatorSpawn.Value.Tile, out ElevatorConfig elevatorConfig))
			return; // No config for this elevator tile type

		// Check if player is facing the correct direction for this elevator
		// WL_AGENT.C:Cmd_Use - elevatorok is true for east/west, false for north/south
		bool facingAllowed = elevatorConfig.Faces switch
		{
			ElevatorFaces.All => true,
			ElevatorFaces.EastWest => direction == Direction.E || direction == Direction.W,
			ElevatorFaces.NorthSouth => direction == Direction.N || direction == Direction.S,
			_ => true
		};

		if (!facingAllowed)
			return; // Can't activate from this direction

		// Check if player is standing on an AltElevator tile (for secret/alternate level)
		// AltElevators contains packed coordinates (x | y << 16) of alt elevator positions
		uint playerPackedPos = PlayerTileX | ((uint)PlayerTileY << 16);
		bool isAltElevator = mapAnalysis.AltElevators.Contains(playerPackedPos);

		// Determine destination level
		byte destinationLevel = isAltElevator && mapAnalysis.AltElevatorTo.HasValue
			? mapAnalysis.AltElevatorTo.Value
			: mapAnalysis.ElevatorTo;

		// Emit switch flip events only if a pressed texture is configured
		// WL_AGENT.C: tilemap[checkx][checky]++
		if (elevatorConfig.PressedTile.HasValue)
		{
			// Wolf3D wall texture formula: horizwall[i]=(i-1)*2, vertwall[i]=(i-1)*2+1
			// Only swap the faces specified by Faces (e.g., EastWest = vertwall only)
			if (elevatorConfig.Faces == ElevatorFaces.EastWest || elevatorConfig.Faces == ElevatorFaces.All)
			{
				// vertwall formula for E/W-facing surfaces
				ElevatorSwitchFlipped?.Invoke(new ElevatorSwitchFlippedEvent
				{
					TileX = tileX,
					TileY = tileY,
					OldTexture = (ushort)((elevatorConfig.Tile - 1) * 2 + 1),
					NewTexture = (ushort)((elevatorConfig.PressedTile.Value - 1) * 2 + 1)
				});
			}

			if (elevatorConfig.Faces == ElevatorFaces.NorthSouth || elevatorConfig.Faces == ElevatorFaces.All)
			{
				// horizwall formula for N/S-facing surfaces
				ElevatorSwitchFlipped?.Invoke(new ElevatorSwitchFlippedEvent
				{
					TileX = tileX,
					TileY = tileY,
					OldTexture = (ushort)((elevatorConfig.Tile - 1) * 2),
					NewTexture = (ushort)((elevatorConfig.PressedTile.Value - 1) * 2)
				});
			}
		}

		// Emit elevator activated event
		ElevatorActivated?.Invoke(new ElevatorActivatedEvent
		{
			TileX = tileX,
			TileY = tileY,
			IsAltElevator = isAltElevator,
			DestinationLevel = destinationLevel,
			SoundName = elevatorConfig.Sound
		});
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
		if (stateCollection.States.TryGetValue(stateName, out GameplayState nextState))
			TransitionActorState(actorIndex, nextState);
	}
	/// <summary>
	/// Transitions an actor to a new state.
	/// Updates sprite, tics, executes Action function, and fires events.
	/// </summary>
	private void TransitionActorState(int actorIndex, GameplayState nextState)
	{
		Actor actor = actors[actorIndex];
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
		// Always fire sprite changed event to update visual
		ActorSpriteChanged?.Invoke(new ActorSpriteChangedEvent
		{
			ActorIndex = actorIndex,
			Shape = (ushort)actor.ShapeNum,
			IsRotated = nextState.Rotate
		});
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
				(byte)spawn.ObjectCode);  // itemnumber - ObjectType Number for script lookup

			// Emit spawn event for VR layer to display the bonus
			// Shape values: -2 = invisible trigger (Noah's Ark), >= 0 = visible
			BonusSpawned?.Invoke(new BonusSpawnedEvent
			{
				StatObjIndex = lastStatObj,
				Shape = (short)spawn.Shape,
				TileX = spawn.X,
				TileY = spawn.Y,
				ItemNumber = (byte)spawn.ObjectCode
			});

			lastStatObj++;
		}
	}
	/// <summary>
	/// Spawn a pickup item at a tile location during gameplay.
	/// WL_ACT1.C:PlaceItemType - scans StatObjList for a free slot, populates it,
	/// and emits BonusSpawnedEvent for the presentation layer.
	/// Called from Lua death scripts (e.g., guards dropping ammo clips).
	/// </summary>
	/// <param name="objectCode">Item number (ObjectType Number from XML, e.g., 49 for ammo clip)</param>
	/// <param name="page">VSwap sprite page number</param>
	/// <param name="tileX">Tile X coordinate to place item at</param>
	/// <param name="tileY">Tile Y coordinate to place item at</param>
	/// <returns>True if item was placed, false if no free slots available</returns>
	public bool PlaceItemType(ushort objectCode, ushort page, ushort tileX, ushort tileY)
	{
		// WL_ACT1.C:PlaceItemType - scan for free slot
		for (int i = 0; i < lastStatObj; i++)
		{
			if (StatObjList[i].IsFree)
			{
				StatObjList[i] = new StatObj(tileX, tileY, (short)page, 0, (byte)objectCode);
				BonusSpawned?.Invoke(new BonusSpawnedEvent
				{
					StatObjIndex = i,
					Shape = (short)page,
					TileX = tileX,
					TileY = tileY,
					ItemNumber = (byte)objectCode
				});
				return true;
			}
		}
		// No free slot found - try appending if space remains
		if (lastStatObj < StatObj.MAXSTATS)
		{
			StatObjList[lastStatObj] = new StatObj(tileX, tileY, (short)page, 0, (byte)objectCode);
			BonusSpawned?.Invoke(new BonusSpawnedEvent
			{
				StatObjIndex = lastStatObj,
				Shape = (short)page,
				TileX = tileX,
				TileY = tileY,
				ItemNumber = (byte)objectCode
			});
			lastStatObj++;
			return true;
		}
		return false;
	}
	/// <summary>
	/// Initialize actors from MapAnalyzer data - creates Actor instances and fires ActorSpawnedEvent for each.
	/// HP and initial state are read from ActorDefinition (parsed from XML).
	/// Based on WL_GAME.C:ScanInfoPlane
	/// </summary>
	/// <param name="mapAnalysis">Map analysis containing actor spawn data</param>
	public void LoadActorsFromMapAnalysis(MapAnalysis mapAnalysis)
	{
		// Note: mapAnalysis, mapWidth, mapHeight, and spatial arrays are already initialized by LoadDoorsFromMapAnalysis
		actors.Clear();
		int difficulty = Inventory.GetValue("Difficulty");
		int actorIndex = 0;
		foreach (MapAnalysis.ActorSpawn spawn in mapAnalysis.ActorSpawns)
		{
			// Use initial state from spawn data (from ObjectType XML)
			string initialStateName = spawn.InitialState;

			// Fall back to ActorDefinition.InitialState if no state specified in spawn
			if (string.IsNullOrEmpty(initialStateName))
			{
				if (stateCollection.ActorDefinitions.TryGetValue(spawn.ActorType, out ActorDefinition def)
					&& !string.IsNullOrEmpty(def.InitialState))
				{
					initialStateName = def.InitialState;
				}
				else
				{
					System.Diagnostics.Debug.WriteLine($"Warning: No initial state defined for actor type '{spawn.ActorType}', skipping");
					continue;
				}
			}

			if (!stateCollection.States.TryGetValue(initialStateName, out GameplayState initialState))
			{
				System.Diagnostics.Debug.WriteLine($"Warning: Initial state '{initialStateName}' not found for actor type '{spawn.ActorType}', skipping");
				continue;
			}
			// Look up initial hit points from ActorDefinition
			short hitPoints = 1; // Default
			if (stateCollection.ActorDefinitions.TryGetValue(spawn.ActorType, out ActorDefinition actorDef)
				&& actorDef.HitPointsByDifficulty != null
				&& difficulty >= 0 && difficulty < actorDef.HitPointsByDifficulty.Length)
			{
				hitPoints = actorDef.GetHitPoints(difficulty);
			}
			// Create actor instance
			Actor actor = new(
				actorType: spawn.ActorType,
				initialState: initialState,
				tileX: spawn.X,
				tileY: spawn.Y,
				facing: spawn.Facing,
				hitPoints: hitPoints);
			// Set additional flags from spawn data
			// WL_ACT1.C:SpawnStand/SpawnPatrol - enemies are FL_SHOOTABLE if they have a death state
			if (actorDef != null && !string.IsNullOrEmpty(actorDef.DeathState))
				actor.Flags |= ActorFlags.Shootable;
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

	#region Weapon Slot Management
	/// <summary>
	/// Initialize weapon slots (call during game setup).
	/// Based on WL_DEF.H:gametype initialization.
	/// </summary>
	/// <param name="slotCount">Number of weapon slots (1 for traditional, 2 for VR dual-wield)</param>
	/// <param name="weapons">Weapon definitions loaded from XML</param>
	public void InitializeWeaponSlots(int slotCount, WeaponCollection weapons)
	{
		weaponCollection = weapons ?? throw new ArgumentNullException(nameof(weapons));
		weaponSlots.Clear();
		for (int i = 0; i < slotCount; i++)
			weaponSlots.Add(new WeaponSlot(i));
	}

	/// <summary>
	/// Equip a weapon to a specific slot.
	/// Based on WL_AGENT.C weapon selection logic (bt_readyknife, bt_readypistol, etc.).
	/// </summary>
	/// <param name="slotIndex">Weapon slot to equip to</param>
	/// <param name="weaponType">Weapon type identifier (e.g., "knife", "pistol")</param>
	public void EquipWeapon(int slotIndex, string weaponType)
	{
		if (slotIndex < 0 || slotIndex >= weaponSlots.Count)
		{
			logger?.LogError("Invalid weapon slot index: {SlotIndex}", slotIndex);
			return;
		}

		if (!weaponCollection.Weapons.TryGetValue(weaponType, out WeaponInfo weaponInfo))
		{
			logger?.LogError("Unknown weapon type: {WeaponType}", weaponType);
			return;
		}

		// WL_AGENT.C:CheckWeaponChange - player must have the weapon to equip it
		if (!PlayerHasWeapon(weaponType))
			return;

		if (!stateCollection.States.TryGetValue(weaponInfo.IdleState, out GameplayState idleState))
		{
			logger?.LogError("Weapon {WeaponType} idle state {IdleState} not found", weaponType, weaponInfo.IdleState);
			return;
		}

		WeaponSlot slot = weaponSlots[slotIndex];
		slot.WeaponType = weaponType;
		slot.CurrentState = idleState;
		slot.TicCount = idleState.Tics;
		slot.ShapeNum = idleState.Shape;
		slot.AttackFrame = 0;
		slot.Flags = WeaponSlotFlags.Ready;

		// Track selected weapon per slot and update status bar weapon in inventory
		// Inventory is the single source of truth for the presentation layer
		Inventory.SetValue($"SelectedWeapon{slotIndex}", weaponInfo.Number);
		Inventory.SetValue("StatusBarWeapon", weaponInfo.Number);

		WeaponEquipped?.Invoke(new WeaponEquippedEvent
		{
			SlotIndex = slotIndex,
			WeaponType = weaponType,
			Shape = (ushort)slot.ShapeNum
		});
	}

	/// <summary>
	/// Get the weapon type currently equipped in a slot.
	/// Used to preserve weapon selection across level transitions.
	/// </summary>
	/// <param name="slotIndex">Weapon slot index</param>
	/// <returns>Weapon type identifier (e.g., "pistol"), or null if slot is empty or invalid</returns>
	public string GetEquippedWeaponType(int slotIndex)
	{
		if (slotIndex < 0 || slotIndex >= weaponSlots.Count)
			return null;
		return weaponSlots[slotIndex].WeaponType;
	}

	/// <summary>
	/// Set ammo count for a specific ammo type.
	/// Based on WL_DEF.H:gametype:ammo[4].
	/// </summary>
	/// <param name="ammoType">Ammo type identifier (e.g., "bullets")</param>
	/// <param name="amount">Amount of ammo</param>
	public void SetAmmo(string ammoType, int amount) =>
		Inventory.SetValue("Ammo", amount);

	/// <summary>
	/// Get ammo count for a specific ammo type.
	/// </summary>
	/// <param name="ammoType">Ammo type identifier</param>
	/// <returns>Current ammo count (0 if type not found)</returns>
	public int GetAmmo(string ammoType) =>
		Inventory.GetValue("Ammo");

	/// <summary>
	/// Update a single weapon slot's state machine.
	/// Based on WL_AGENT.C:T_Attack (line 2101) and Actor update pattern.
	/// </summary>
	/// <param name="slotIndex">Weapon slot to update</param>
	private void UpdateWeaponSlot(int slotIndex)
	{
		WeaponSlot slot = weaponSlots[slotIndex];

		// Empty slot or no state - nothing to update
		if (slot.CurrentState == null || slot.WeaponType == null)
			return;

		// Non-transitional state (tics == 0) - stays in state, no auto-transition
		// These are idle/ready states (WL_AGENT.C:T_Attack early return for tictime==0)
		if (slot.TicCount == 0)
			return;

		// Transitional state - decrement and check for transition
		slot.TicCount--;

		while (slot.TicCount <= 0)
		{
			if (slot.CurrentState.Next == null)
				break;

			TransitionWeaponState(slotIndex, slot.CurrentState.Next);

			// If new state is non-transitional, stop
			if (slot.CurrentState.Tics == 0)
			{
				slot.TicCount = 0;
				// Clear attacking flag when returning to idle
				slot.Flags &= ~WeaponSlotFlags.Attacking;
				break;
			}

			break;  // Simplified - full timing accuracy can be added later
		}
	}

	/// <summary>
	/// Transition weapon slot to a new state.
	/// Updates sprite, executes Action function if present, fires events.
	/// Based on actor state transition pattern.
	/// </summary>
	/// <param name="slotIndex">Weapon slot index</param>
	/// <param name="nextState">New state to transition to</param>
	private void TransitionWeaponState(int slotIndex, GameplayState nextState)
	{
		WeaponSlot slot = weaponSlots[slotIndex];
		short oldShape = slot.ShapeNum;

		slot.CurrentState = nextState;
		slot.TicCount = nextState.Tics;
		slot.ShapeNum = nextState.Shape;
		slot.AttackFrame++;  // Increment attack frame counter

		// Execute Action function if present (optional Lua support)
		// TODO: Full Lua script execution for weapon actions
		// For now, handle weapon actions directly in C#
		if (!string.IsNullOrEmpty(nextState.Action)
			&& weaponCollection.Weapons.TryGetValue(slot.WeaponType, out WeaponInfo weaponInfo))
		{
			switch (nextState.Action)
			{
				// WL_AGENT.C:KnifeAttack - melee attack action
				case "A_KnifeAttack":
					ExecuteKnifeAttack(slotIndex, slot, weaponInfo);
					break;

				// WL_AGENT.C:GunAttack - hitscan attack actions
				// Each performs raycast via HitDetection callback, consumes ammo, applies damage
				case "A_PistolAttack":
				case "A_MachineGunAttack":
				case "A_ChainGunAttack":
					ExecuteGunAttack(slotIndex, slot, weaponInfo);
					break;

				// WL_AGENT.C:T_Attack cases 3 & 4 - loop back to fire state if trigger held
				case "A_RapidFire":
					if (slot.Flags.HasFlag(WeaponSlotFlags.TriggerHeld))
					{
						bool hasAmmo = true;
						if (weaponInfo.AmmoPerShot > 0 && !string.IsNullOrEmpty(weaponInfo.AmmoType))
							hasAmmo = Inventory.GetValue("Ammo") >= weaponInfo.AmmoPerShot;

						if (hasAmmo)
						{
							string fireFrameState = weaponInfo.FireState;
							if (fireFrameState != null && stateCollection.States.TryGetValue(fireFrameState, out GameplayState fireState))
							{
								slot.CurrentState = fireState;
								slot.TicCount = fireState.Tics;
								slot.ShapeNum = fireState.Shape;
								slot.AttackFrame = 1;
							}
						}
					}
					break;
			}
		}

		// Update flags based on state
		if (nextState.Tics == 0)
		{
			// Returning to idle state
			slot.Flags |= WeaponSlotFlags.Ready;
			slot.Flags &= ~WeaponSlotFlags.Attacking;
			slot.AttackFrame = 0;  // Reset attack frame when idle
		}
		else
		{
			// In animation
			slot.Flags &= ~WeaponSlotFlags.Ready;
		}

		// Fire sprite changed event
		if (oldShape != slot.ShapeNum)
		{
			WeaponSpriteChanged?.Invoke(new WeaponSpriteChangedEvent
			{
				SlotIndex = slotIndex,
				Shape = (ushort)slot.ShapeNum
			});
		}
	}

	/// <summary>
	/// Process weapon fire action from presentation layer.
	/// Based on WL_AGENT.C:Cmd_Fire (line 1629) and T_Attack.
	/// </summary>
	/// <param name="action">Fire weapon action containing hit detection results</param>
	private void ProcessFireWeapon(FireWeaponAction action)
	{
		if (action.SlotIndex < 0 || action.SlotIndex >= weaponSlots.Count)
			return;

		WeaponSlot slot = weaponSlots[action.SlotIndex];

		// Validate: slot must have a weapon
		if (slot.WeaponType == null)
			return;

		if (!weaponCollection.Weapons.TryGetValue(slot.WeaponType, out WeaponInfo weaponInfo))
			return;

		// Check fire mode: semi-auto requires trigger release first
		// Based on WL_AGENT.C:buttonheld[] tracking (line 2266-2268)
		if (weaponInfo.FireMode == "semi" && slot.Flags.HasFlag(WeaponSlotFlags.TriggerHeld))
			return;

		// Check ammo before starting animation (dry fire check)
		if (weaponInfo.AmmoPerShot > 0 && !string.IsNullOrEmpty(weaponInfo.AmmoType))
		{
			if (Inventory.GetValue("Ammo") < weaponInfo.AmmoPerShot)
			{
				EmitGlobalSound("NOITEMSND");
				return;
			}
		}

		// Start fire animation - ammo consumption, hit detection, and damage
		// are handled by the attack action (A_PistolAttack, etc.) on the fire frame
		if (!stateCollection.States.TryGetValue(weaponInfo.FireState, out GameplayState fireState))
			return;

		TransitionWeaponState(action.SlotIndex, fireState);

		// Set flags
		slot.Flags |= WeaponSlotFlags.TriggerHeld | WeaponSlotFlags.Attacking;
		slot.Flags &= ~WeaponSlotFlags.Ready;
		slot.AttackFrame = 0;
	}

	/// <summary>
	/// Process release weapon trigger action.
	/// Clears TriggerHeld flag for semi-auto fire mode.
	/// Based on WL_AGENT.C:buttonheld[] tracking.
	/// </summary>
	/// <param name="action">Release trigger action</param>
	private void ProcessReleaseTrigger(ReleaseWeaponTriggerAction action)
	{
		if (action.SlotIndex < 0 || action.SlotIndex >= weaponSlots.Count)
			return;

		WeaponSlot slot = weaponSlots[action.SlotIndex];
		slot.Flags &= ~WeaponSlotFlags.TriggerHeld;
	}

	/// <summary>
	/// Execute a gun attack action (A_PistolAttack, A_MachineGunAttack, A_ChainGunAttack).
	/// Consumes ammo, performs hit detection via callback, applies damage, emits event.
	/// Based on WL_AGENT.C:GunAttack - called on the fire frame of every weapon.
	/// </summary>
	private void ExecuteGunAttack(int slotIndex, WeaponSlot slot, WeaponInfo weaponInfo)
	{
		// Check and consume ammo
		if (weaponInfo.AmmoPerShot > 0 && !string.IsNullOrEmpty(weaponInfo.AmmoType))
		{
			int currentAmmo = Inventory.GetValue("Ammo");
			if (currentAmmo < weaponInfo.AmmoPerShot)
				return;
			Inventory.SetValue("Ammo", currentAmmo - weaponInfo.AmmoPerShot);
		}

		// Perform hit detection via presentation layer callback
		int? hitActorIndex = HitDetection?.Invoke(slotIndex);

		// Apply damage if hit
		if (hitActorIndex.HasValue)
			ApplyWeaponDamage(hitActorIndex.Value, weaponInfo);

		// Emit weapon fired event (for sound, muzzle flash)
		WeaponFired?.Invoke(new WeaponFiredEvent
		{
			SlotIndex = slotIndex,
			WeaponType = slot.WeaponType,
			SoundName = weaponInfo.FireSound ?? string.Empty,
			DidHit = hitActorIndex.HasValue,
			HitActorIndex = hitActorIndex
		});

		// WL_AGENT.C:GunAttack - madenoise = true
		PropagateNoise();
	}

	/// <summary>
	/// Execute a knife attack action (A_KnifeAttack).
	/// Performs hit detection via callback, applies melee damage.
	/// No ammo consumed. No noise propagation (knife is silent).
	/// Based on WL_AGENT.C:KnifeAttack.
	/// </summary>
	private void ExecuteKnifeAttack(int slotIndex, WeaponSlot slot, WeaponInfo weaponInfo)
	{
		// WL_AGENT.C:KnifeAttack - position-based melee, no raycast needed
		int? hitActorIndex = null;
		int closestDist = int.MaxValue;

		for (int i = 0; i < actors.Count; i++)
		{
			Actor actor = actors[i];
			if (!actor.Flags.HasFlag(ActorFlags.Shootable)) continue;

			int dx = Math.Abs(PlayerTileX - actor.TileX);
			int dy = Math.Abs(PlayerTileY - actor.TileY);
			int chebyshev = Math.Max(dx, dy); // Chebyshev distance

			// WL_AGENT.C:KnifeAttack - range check (0x18000 ≈ 1.5 tiles)
			if (chebyshev > 1) continue;

			// Line of sight check (replaces FL_VISABLE + shootdelta from original)
			if (!HasLineOfSight(PlayerTileX, PlayerTileY, actor.TileX, actor.TileY))
				continue;

			if (chebyshev < closestDist)
			{
				closestDist = chebyshev;
				hitActorIndex = i;
			}
		}

		// Apply damage if hit
		if (hitActorIndex.HasValue)
			ApplyWeaponDamage(hitActorIndex.Value, weaponInfo);

		// Emit weapon fired event (for sound, animation)
		WeaponFired?.Invoke(new WeaponFiredEvent
		{
			SlotIndex = slotIndex,
			WeaponType = slot.WeaponType,
			SoundName = weaponInfo.FireSound ?? string.Empty,
			DidHit = hitActorIndex.HasValue,
			HitActorIndex = hitActorIndex
		});

		// No PropagateNoise() - knife is silent (WL_AGENT.C:KnifeAttack doesn't set madenoise)
	}

	/// <summary>
	/// WL_AGENT.C:GunAttack sets madenoise = true
	/// WL_STATE.C:SightPlayer checks madenoise to alert non-ambush enemies in connected areas
	/// Instead of a per-tic flag, we immediately alert on the fire frame.
	/// </summary>
	private void PropagateNoise()
	{
		if (mapAnalysis is null) return;

		short playerArea = mapAnalysis.GetAreaNumber(PlayerTileX, PlayerTileY);
		if (playerArea < 0) return;

		List<ushort> connectedAreas = AreaConnect.FloorCodes((ushort)playerArea);

		for (int i = 0; i < actors.Count; i++)
		{
			Actor actor = actors[i];

			if (!actor.Flags.HasFlag(ActorFlags.Shootable)) continue;
			if (actor.Flags.HasFlag(ActorFlags.AttackMode)) continue;
			if (actor.Flags.HasFlag(ActorFlags.Ambush)) continue;

			short actorArea = mapAnalysis.GetAreaNumber(actor.TileX, actor.TileY);
			if (actorArea < 0 || !connectedAreas.Contains((ushort)actorArea)) continue;

			// WL_STATE.C:SightPlayer - ob->temp2 = 1 + US_RndT()/4
			actor.ReactionTimer = (short)(1 + rng.Next(256) / 4);

			if (stateCollection.ActorDefinitions.TryGetValue(actor.ActorType, out ActorDefinition actorDef)
				&& !string.IsNullOrEmpty(actorDef.ChaseState))
			{
				TransitionActorStateByName(i, actorDef.ChaseState);
				actor.Flags |= ActorFlags.AttackMode;

				if (!string.IsNullOrEmpty(actorDef.AlertDigiSound))
					EmitActorPlaySound(i, actorDef.AlertDigiSound);
			}
		}
	}

	/// <summary>
	/// Apply damage to an actor from a weapon hit.
	/// Calculates damage based on distance and weapon type, matching original Wolf3D formulas.
	/// Based on WL_AGENT.C:GunAttack (damage tiers), WL_AGENT.C:KnifeAttack (melee),
	/// and WL_STATE.C:DamageActor (surprise bonus, pain states, death transition).
	/// </summary>
	/// <param name="actorIndex">Index of actor to damage</param>
	/// <param name="weaponInfo">Weapon used for the attack</param>
	private void ApplyWeaponDamage(int actorIndex, WeaponInfo weaponInfo)
	{
		if (actorIndex < 0 || actorIndex >= actors.Count)
			return;

		Actor actor = actors[actorIndex];

		// WL_AGENT.C:DamageActor - only damage shootable (alive) actors
		if (!actor.Flags.HasFlag(ActorFlags.Shootable))
			return;

		bool isMelee = weaponInfo.AmmoPerShot <= 0 || string.IsNullOrEmpty(weaponInfo.AmmoType);
		int damage;

		if (isMelee)
		{
			// WL_AGENT.C:KnifeAttack - damage = US_RndT() >> 4 (0-15)
			damage = rng.Next(256) >> 4;
		}
		else
		{
			// WL_AGENT.C:GunAttack - Chebyshev distance between player and actor tiles
			int dx = Math.Abs(PlayerTileX - actor.TileX);
			int dy = Math.Abs(PlayerTileY - actor.TileY);
			int dist = Math.Max(dx, dy);

			// WL_AGENT.C:GunAttack - accuracy falloff at distance
			if (UseAccuracyFalloff && dist >= 4)
			{
				if (rng.Next(256) / 12 < dist)
					return; // Miss
			}

			// WL_AGENT.C:GunAttack - distance-based damage tiers
			if (dist < 2)
				damage = rng.Next(256) / 4;   // 0-63
			else if (dist < 4)
				damage = rng.Next(256) / 6;   // 0-42
			else
				damage = rng.Next(256) / 6;   // 0-42
		}

		// WL_STATE.C:DamageActor - surprise bonus (FL_ATTACKMODE not set = unaware)
		if (!actor.Flags.HasFlag(ActorFlags.AttackMode))
			damage *= 2;

		// Apply damage
		actor.HitPoints -= (short)damage;

		// Look up actor definition for state transitions
		stateCollection.ActorDefinitions.TryGetValue(actor.ActorType, out ActorDefinition actorDef);

		if (actor.HitPoints <= 0)
		{
			// WL_STATE.C:DamageActor - actor killed
			actor.HitPoints = 0;

			if (actorDef != null && !string.IsNullOrEmpty(actorDef.DeathState))
			{
				TransitionActorStateByName(actorIndex, actorDef.DeathState);
				// Clear shootable flag - dead actors can't be shot again
				actor.Flags &= ~ActorFlags.Shootable;
			}
		}
		else
		{
			// WL_STATE.C:DamageActor - actor hurt but alive

			// If not in attack mode, trigger first sighting (wake up)
			if (!actor.Flags.HasFlag(ActorFlags.AttackMode) && actorDef != null)
			{
				if (!string.IsNullOrEmpty(actorDef.ChaseState))
				{
					TransitionActorStateByName(actorIndex, actorDef.ChaseState);
					actor.Flags |= ActorFlags.AttackMode;

					if (!string.IsNullOrEmpty(actorDef.AlertDigiSound))
						EmitActorPlaySound(actorIndex, actorDef.AlertDigiSound);
				}
			}

			// Transition to pain state (if actor has one)
			// WL_STATE.C:DamageActor - alternates between two pain frames based on HP parity
			if (actorDef != null)
			{
				string painState = (actor.HitPoints & 1) != 0
					? actorDef.PainState
					: actorDef.PainState1 ?? actorDef.PainState;

				if (!string.IsNullOrEmpty(painState))
					TransitionActorStateByName(actorIndex, painState);
			}
		}
	}
	#endregion

	#region Player State Management
	/// <summary>
	/// Get player's current health.
	/// WL_DEF.H:gametype:health
	/// </summary>
	public int GetPlayerHealth() => Inventory.GetValue("Health");

	/// <summary>
	/// Get player's maximum health capacity.
	/// </summary>
	public int GetPlayerMaxHealth() => Inventory.GetMax("Health");

	/// <summary>
	/// Get player's current score.
	/// WL_DEF.H:gametype:score
	/// </summary>
	public int GetPlayerScore() => Inventory.GetValue("Score");

	/// <summary>
	/// Get player's current lives.
	/// WL_DEF.H:gametype:lives
	/// </summary>
	public int GetPlayerLives() => Inventory.GetValue("Lives");

	/// <summary>
	/// Get maximum ammo capacity for an ammo type.
	/// </summary>
	public int GetMaxAmmo(string ammoType) => Inventory.GetMax("Ammo");

	/// <summary>
	/// Check if player has a specific key.
	/// WL_DEF.H:gametype:keys
	/// </summary>
	public bool PlayerHasKey(int keyType) => keyType switch
	{
		0 => Inventory.Has("Gold Key"),
		1 => Inventory.Has("Silver Key"),
		_ => false
	};

	/// <summary>
	/// Check if player has a specific weapon.
	/// WL_DEF.H:gametype:bestweapon
	/// </summary>
	public bool PlayerHasWeapon(string weaponType)
	{
		if (weaponCollection != null && weaponCollection.TryGetWeapon(weaponType, out WeaponInfo weaponInfo))
			return Inventory.Has(WeaponCollection.GetInventoryKey(weaponInfo.Number));
		return false;
	}

	/// <summary>
	/// Heal the player (capped at max health).
	/// WL_AGENT.C:GetBonus health logic
	/// </summary>
	public void HealPlayer(int amount) =>
		Inventory.AddValue("Health", amount);

	/// <summary>
	/// Damage the player.
	/// WL_AGENT.C:TakeDamage
	/// </summary>
	public void DamagePlayer(int amount)
	{
		// WL_AGENT.C:TakeDamage - god mode check
		if (GodMode)
			return;
		// WL_AGENT.C:TakeDamage - baby difficulty halves damage twice (>>2)
		if (Inventory.GetValue("Difficulty") == 0)
			amount >>= 2;
		if (amount <= 0)
			return;
		Inventory.AddValue("Health", -amount);
		// WL_AGENT.C:TakeDamage - red screen flash proportional to damage
		EmitScreenFlash(0xFF0000, (short)Math.Min(amount, 60));
		// WL_AGENT.C:TakeDamage - check for death
		if (Inventory.GetValue("Health") <= 0)
			PlayerDied?.Invoke(new PlayerDiedEvent());
	}

	/// <summary>
	/// Give ammo to player (capped at max).
	/// WL_AGENT.C:GetBonus ammo logic
	/// </summary>
	public void GiveAmmo(string ammoType, int amount) =>
		Inventory.AddValue("Ammo", amount);

	/// <summary>
	/// Give a key to the player.
	/// WL_AGENT.C:GetBonus key logic
	/// </summary>
	public void GiveKey(int keyType)
	{
		string keyName = keyType switch
		{
			0 => "Gold Key",
			1 => "Silver Key",
			_ => null
		};
		if (keyName != null)
			Inventory.SetValue(keyName, 1);
	}

	/// <summary>
	/// Give a weapon to the player.
	/// WL_AGENT.C:GetBonus weapon logic
	/// </summary>
	public void GiveWeapon(string weaponType)
	{
		if (weaponCollection != null && weaponCollection.TryGetWeapon(weaponType, out WeaponInfo weaponInfo))
			Inventory.SetValue(WeaponCollection.GetInventoryKey(weaponInfo.Number), 1);
	}

	/// <summary>
	/// Add to player's score.
	/// WL_AGENT.C:GetBonus score logic
	/// </summary>
	public void AddScore(int points)
	{
		Inventory.AddValue("Score", points);
		// TODO: Check for extra life at score thresholds
	}

	/// <summary>
	/// Give player an extra life.
	/// WL_AGENT.C:GetBonus extra life
	/// </summary>
	public void GiveExtraLife()
	{
		Inventory.AddValue("Lives", 1);
		// Also typically fills health
		Inventory.SetValue("Health", Inventory.GetMax("Health"));
	}

	/// <summary>
	/// Emit a PlayerStateChanged event to update HUD/status bar.
	/// </summary>
	private void EmitPlayerStateChanged()
	{
		// Compute key flags as bitmask
		int keyFlags = 0;
		if (Inventory.Has("Gold Key"))
			keyFlags |= 1;
		if (Inventory.Has("Silver Key"))
			keyFlags |= 2;

		PlayerStateChanged?.Invoke(new PlayerStateChangedEvent
		{
			Health = Inventory.GetValue("Health"),
			Score = Inventory.GetValue("Score"),
			Lives = Inventory.GetValue("Lives"),
			Ammo = GetAmmo("bullets"),
			KeyFlags = keyFlags
		});
	}

	/// <summary>
	/// Emit a BonusPlaySound event for positional audio at an item's location.
	/// </summary>
	public void EmitBonusPlaySound(int statObjIndex, ushort tileX, ushort tileY, string soundName, bool isDigiSound)
	{
		BonusPlaySound?.Invoke(new BonusPlaySoundEvent
		{
			StatObjIndex = statObjIndex,
			TileX = tileX,
			TileY = tileY,
			SoundName = soundName,
			IsDigiSound = isDigiSound
		});
	}

	/// <summary>
	/// Emit a PlayGlobalSound event for non-positional audio.
	/// </summary>
	public void EmitPlayGlobalSound(string soundName)
	{
		PlayGlobalSound?.Invoke(new PlayGlobalSoundEvent
		{
			SoundName = soundName
		});
	}

	/// <summary>
	/// Set player health directly (for initialization/save loading).
	/// </summary>
	public void SetPlayerHealth(int health) => Inventory.SetValue("Health", health);

	/// <summary>
	/// Set player score directly (for initialization/save loading).
	/// </summary>
	public void SetPlayerScore(int score) => Inventory.SetValue("Score", score);

	/// <summary>
	/// Set player lives directly (for initialization/save loading).
	/// </summary>
	public void SetPlayerLives(int lives) => Inventory.SetValue("Lives", lives);

	/// <summary>
	/// Load item scripts from game configuration.
	/// Called during level load to set up item pickup behavior.
	/// </summary>
	/// <param name="scripts">Dictionary mapping script name to Lua code</param>
	/// <param name="itemNumberToScriptMap">Dictionary mapping ItemNumber (byte) to script name</param>
	public void LoadItemScripts(Dictionary<string, string> scripts, Dictionary<byte, string> itemNumberToScriptMap)
	{
		itemScripts = scripts ?? new();
		itemNumberToScript = itemNumberToScriptMap ?? new();

		logger?.LogInformation("LoadItemScripts: Loaded {ScriptCount} scripts, {MappingCount} item->script mappings",
			itemScripts.Count, itemNumberToScript.Count);

		foreach (KeyValuePair<byte, string> mapping in itemNumberToScript)
			logger?.LogDebug("  ItemNumber {ItemNumber} -> Script '{ScriptName}'", mapping.Key, mapping.Value);
	}

	/// <summary>
	/// Check for item pickups based on player position.
	/// Items have a 1-tile bounding box (half tile from center to each edge).
	/// Executes item script if collision detected - script returns true to consume item.
	/// WL_AGENT.C:ClipMove checks for bonus collision with same box model.
	/// </summary>
	private void CheckItemPickups()
	{
		// Player position in fixed-point 16.16
		// Items are centered on their tile - collision box extends half tile in each direction
		const int HALF_TILE = 0x8000; // Half tile in fixed-point (0.5 * 0x10000)

		for (int i = 0; i < lastStatObj; i++)
		{
			StatObj item = StatObjList[i];

			// Skip empty/despawned slots
			if (item == null || item.IsFree)
				continue;

			// Item center in fixed-point coordinates
			int itemCenterX = (item.TileX << 16) + HALF_TILE;
			int itemCenterY = (item.TileY << 16) + HALF_TILE;

			// Check if player is within 1 tile of item center (box collision)
			// Distance must be < 1 tile in both X and Y
			int deltaX = Math.Abs(PlayerX - itemCenterX);
			int deltaY = Math.Abs(PlayerY - itemCenterY);

			if (deltaX < 0x10000 && deltaY < 0x10000)
			{
				// Player is touching item - try to pick it up
				if (TryPickupItem(i, item))
				{
					// Item was consumed - mark as despawned
					item.ShapeNum = -1;

					// Emit pickup event for presentation layer
					BonusPickedUp?.Invoke(new BonusPickedUpEvent
					{
						StatObjIndex = i,
						TileX = item.TileX,
						TileY = item.TileY,
						ItemNumber = item.ItemNumber
					});
				}
			}
		}
	}

	/// <summary>
	/// Attempt to pick up an item using its Lua script.
	/// Script returns true if item should be consumed, false otherwise.
	/// </summary>
	/// <param name="itemIndex">Index in StatObjList</param>
	/// <param name="item">The item to pick up</param>
	/// <returns>True if item was consumed, false if not (e.g., health at max)</returns>
	private bool TryPickupItem(int itemIndex, StatObj item)
	{
		// Look up script name by ItemNumber
		if (!itemNumberToScript.TryGetValue(item.ItemNumber, out string scriptName))
		{
			// No script mapping - item has no script, consume by default
			logger?.LogWarning("TryPickupItem: No script mapping for ItemNumber {ItemNumber} (index {Index}). Available keys: {Keys}",
				item.ItemNumber, itemIndex,
				string.Join(", ", itemNumberToScript.Keys.Select(k => k.ToString())));
			return true;
		}

		if (!itemScripts.TryGetValue(scriptName, out string script))
		{
			// Script name not found - consume by default
			logger?.LogWarning("TryPickupItem: Script '{ScriptName}' not found in itemScripts. Available: {Scripts}",
				scriptName, string.Join(", ", itemScripts.Keys));
			return true;
		}

		if (string.IsNullOrWhiteSpace(script))
		{
			// Empty script - consume by default
			logger?.LogWarning("TryPickupItem: Script '{ScriptName}' is empty or whitespace", scriptName);
			return true;
		}

		logger?.LogDebug("TryPickupItem: Running script '{ScriptName}' for ItemNumber {ItemNumber}", scriptName, item.ItemNumber);

		// Create script context for this item
		Lua.ItemScriptContext context = new(
			this,
			item,
			itemIndex,
			rng,
			gameClock,
			logger);

		// Wire up sound callbacks - these emit events for the VR layer
		context.PlayAdLibSoundAction = soundName =>
			EmitBonusPlaySound(itemIndex, item.TileX, item.TileY, soundName, isDigiSound: false);
		context.PlayDigiSoundAction = soundName =>
			EmitBonusPlaySound(itemIndex, item.TileX, item.TileY, soundName, isDigiSound: true);

		try
		{
			// Execute the item script
			MoonSharp.Interpreter.DynValue result = luaScriptEngine.DoString(script, context);

			// Script returns true to consume, false to leave
			if (result.Type == MoonSharp.Interpreter.DataType.Boolean)
				return result.Boolean;

			// If script doesn't return a boolean, default to consume
			return true;
		}
		catch (Exception ex)
		{
			logger?.LogError(ex, "Error executing item script '{ScriptName}' at ({TileX}, {TileY})",
				scriptName, item.TileX, item.TileY);
			// On error, don't consume (safer default)
			return false;
		}
	}
	#endregion

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

			// Emit precise 16.16 fixed-point position
			// ActorSpawnedEvent only has tile coordinates; actors may be mid-tile
			// (e.g., dead actors that stopped mid-movement)
			ActorMoved?.Invoke(new ActorMovedEvent
			{
				ActorIndex = i,
				X = actor.X,
				Y = actor.Y,
				Facing = actor.Facing
			});

			// Also emit current actor sprite state
			ActorSpriteChanged?.Invoke(new ActorSpriteChangedEvent
			{
				ActorIndex = i,
				Shape = (ushort)actor.ShapeNum,
				IsRotated = actor.CurrentState?.Rotate ?? false
			});
		}

		// Emit weapon equipped events for all active weapon slots
		for (int i = 0; i < weaponSlots.Count; i++)
		{
			WeaponSlot slot = weaponSlots[i];
			if (!string.IsNullOrEmpty(slot.WeaponType))
			{
				WeaponEquipped?.Invoke(new WeaponEquippedEvent
				{
					SlotIndex = i,
					WeaponType = slot.WeaponType,
					Shape = (ushort)slot.ShapeNum
				});

				// Also emit current weapon sprite state
				if (slot.ShapeNum >= 0)
				{
					WeaponSpriteChanged?.Invoke(new WeaponSpriteChangedEvent
					{
						SlotIndex = i,
						Shape = (ushort)slot.ShapeNum
					});
				}
			}
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

	/// <summary>
	/// Emits a screen flash event for palette-shift effects.
	/// WL_PLAY.C: Bonus flash, damage flash, etc.
	/// Called from Lua scripts via FlashScreen() or internally by game events.
	/// </summary>
	/// <param name="color">24-bit RGB color (e.g., 0xFF0000 for red)</param>
	/// <param name="duration">Duration in tics (default 18, matching original Wolf3D bonus flash)</param>
	internal void EmitScreenFlash(uint color, short duration)
	{
		ScreenFlash?.Invoke(new ScreenFlashEvent { Color = color, Duration = duration });
	}

	#region Spatial Index
	/// <summary>
	/// Converts tile coordinates to array index for spatial index arrays.
	/// Based on WL_DEF.H actorat array indexing.
	/// </summary>
	private int GetTileIndex(ushort x, ushort y) => y * mapWidth + x;

	/// <summary>
	/// Bresenham tile-based line-of-sight check between two tile positions.
	/// Based on WL_STATE.C:CheckLine.
	/// </summary>
	public bool HasLineOfSight(ushort fromX, ushort fromY, ushort toX, ushort toY)
	{
		int dx = Math.Abs(toX - fromX),
			dy = Math.Abs(toY - fromY),
			sx = fromX < toX ? 1 : -1,
			sy = fromY < toY ? 1 : -1,
			err = dx - dy,
			x = fromX,
			y = fromY;
		while (true)
		{
			if (x == toX && y == toY)
				return true;
			if (!(x == fromX && y == fromY)
				&& !IsTileTransparentForSight((ushort)x, (ushort)y))
				return false;
			int e2 = 2 * err;
			bool movedX = false, movedY = false;
			if (e2 > -dy)
			{
				err -= dy;
				x += sx;
				movedX = true;
			}
			if (e2 < dx)
			{
				err += dx;
				y += sy;
				movedY = true;
			}
			// Prevent seeing through corners
			if (movedX && movedY)
			{
				if (!IsTileTransparentForSight((ushort)(x - sx), (ushort)y))
					return false;
			}
		}
	}

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
	/// Checks if a tile is navigable (can be moved onto by actors).
	/// Combines static map analysis with dynamic state (doors, pushwalls, actors, player).
	/// Based on Wolf3D collision detection logic.
	/// WL_STATE.C:TryWalk
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

		// 5. Check for player - actors cannot move onto tiles the player occupies
		// WL_STATE.C:TryWalk checks player->tilex, player->tiley
		if (PlayerTileX == x && PlayerTileY == y)
			return false;

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

	#region Player Movement Validation
	/// <summary>
	/// Validates a desired player position against collision and returns an adjusted position.
	/// Implements wall-sliding: if direct movement is blocked, tries X-only then Y-only.
	/// Uses AABB collision with square bounding box.
	/// Based on WL_AGENT.C:ClipMove combined with Level.PlayerWalk from old Godot 3 version.
	/// </summary>
	/// <param name="currentX">Current X position (16.16 fixed-point)</param>
	/// <param name="currentY">Current Y position (16.16 fixed-point)</param>
	/// <param name="desiredX">Desired X position (16.16 fixed-point)</param>
	/// <param name="desiredY">Desired Y position (16.16 fixed-point)</param>
	/// <param name="headSize">Half-width of collision box (16.16 fixed-point)</param>
	/// <returns>Validated position (X, Y) in 16.16 fixed-point</returns>
	public (int X, int Y) ValidatePlayerMove(int currentX, int currentY, int desiredX, int desiredY, int headSize)
	{
		// NoClip bypass
		if (NoClip)
			return (desiredX, desiredY);

		// Check if full movement is valid
		if (IsPositionValidForPlayer(desiredX, desiredY, headSize))
			return (desiredX, desiredY);

		// Wall sliding: try X-only movement
		if (IsPositionValidForPlayer(desiredX, currentY, headSize))
			return (desiredX, currentY);

		// Wall sliding: try Y-only movement
		if (IsPositionValidForPlayer(currentX, desiredY, headSize))
			return (currentX, desiredY);

		// Completely blocked, stay at current position
		return (currentX, currentY);
	}

	/// <summary>
	/// Checks if a position is valid for the player by testing all four corners of the AABB.
	/// </summary>
	/// <param name="centerX">Center X position (16.16 fixed-point)</param>
	/// <param name="centerY">Center Y position (16.16 fixed-point)</param>
	/// <param name="headSize">Half-width of collision box (16.16 fixed-point)</param>
	/// <returns>True if all corners are in valid tiles</returns>
	private bool IsPositionValidForPlayer(int centerX, int centerY, int headSize)
	{
		// Calculate AABB corners
		int left = centerX - headSize;
		int right = centerX + headSize;
		int top = centerY - headSize;
		int bottom = centerY + headSize;

		// Convert to tile coordinates
		int leftTile = left >> 16;
		int rightTile = right >> 16;
		int topTile = top >> 16;
		int bottomTile = bottom >> 16;

		// Check all four corners
		if (!IsTileValidForPlayer(leftTile, topTile)) return false;
		if (!IsTileValidForPlayer(rightTile, topTile)) return false;
		if (!IsTileValidForPlayer(leftTile, bottomTile)) return false;
		if (!IsTileValidForPlayer(rightTile, bottomTile)) return false;

		return true;
	}

	/// <summary>
	/// Checks if a tile is valid for player movement.
	/// Similar to IsTileNavigable but uses int params for bounds checking.
	/// </summary>
	/// <param name="tileX">Tile X coordinate</param>
	/// <param name="tileY">Tile Y coordinate</param>
	/// <returns>True if the tile can be walked on by the player</returns>
	private bool IsTileValidForPlayer(int tileX, int tileY)
	{
		// Bounds check (handles negative coords and overflow)
		if (tileX < 0 || tileY < 0 || tileX >= mapWidth || tileY >= mapHeight)
			return false;

		ushort x = (ushort)tileX;
		ushort y = (ushort)tileY;

		// Check static navigability (from MapAnalysis BitArray)
		if (!mapAnalysis.IsNavigable(x, y))
			return false;

		int tileIdx = GetTileIndex(x, y);

		// Check for doors that aren't fully open
		if (doorAtTile[tileIdx] >= 0)
		{
			Door door = doors[doorAtTile[tileIdx]];
			// Only allow passing through fully open doors
			if (door.Action != DoorAction.Open)
				return false;
		}

		// Check for push walls
		if (pushWallAtTile[tileIdx] >= 0)
			return false;

		// Check for living actors
		if (actorAtTile[tileIdx] >= 0)
		{
			Actor actor = actors[actorAtTile[tileIdx]];
			if (actor.HitPoints > 0)
				return false;
		}

		return true;
	}
	#endregion

	#region Save/Load State
	/// <summary>
	/// Captures the entire simulator state into a serializable snapshot.
	/// Only mutable runtime state is captured; static/structural data loaded from
	/// game files is not included (see SimulatorSnapshot documentation for details).
	/// </summary>
	public SimulatorSnapshot SaveState()
	{
		// Capture door dynamic state (matched by index on restore)
		DoorSnapshot[] doorSnapshots = new DoorSnapshot[doors.Count];
		for (int i = 0; i < doors.Count; i++)
			doorSnapshots[i] = doors[i].SaveState();

		// Capture pushwall dynamic state (matched by index on restore)
		PushWallSnapshot[] pushWallSnapshots = new PushWallSnapshot[pushWalls.Count];
		for (int i = 0; i < pushWalls.Count; i++)
			pushWallSnapshots[i] = pushWalls[i].SaveState();

		// Capture actor state (CurrentStateName resolved on restore)
		ActorSnapshot[] actorSnapshots = new ActorSnapshot[actors.Count];
		for (int i = 0; i < actors.Count; i++)
			actorSnapshots[i] = actors[i].SaveState();

		// Capture weapon slot state (CurrentStateName resolved on restore)
		WeaponSlotSnapshot[] weaponSlotSnapshots = new WeaponSlotSnapshot[weaponSlots.Count];
		for (int i = 0; i < weaponSlots.Count; i++)
			weaponSlotSnapshots[i] = weaponSlots[i].SaveState();

		// Capture StatObjList (StatObj is its own snapshot type)
		StatObj[] statObjSnapshots = new StatObj[StatObjList.Length];
		for (int i = 0; i < StatObjList.Length; i++)
			statObjSnapshots[i] = StatObjList[i]?.SaveState();

		// Capture patrol directions with enum stored as byte
		Dictionary<uint, byte> patrolDirs = null;
		if (patrolDirectionAtTile != null)
		{
			patrolDirs = new Dictionary<uint, byte>(patrolDirectionAtTile.Count);
			foreach (KeyValuePair<uint, Direction> kvp in patrolDirectionAtTile)
				patrolDirs[kvp.Key] = (byte)kvp.Value;
		}

		return new SimulatorSnapshot
		{
			GameName = GameName,
			CurrentTic = CurrentTic,
			AccumulatedTime = accumulatedTime,
			PlayerX = PlayerX,
			PlayerY = PlayerY,
			NoClip = NoClip,
			GodMode = GodMode,
			Doors = doorSnapshots,
			PushWalls = pushWallSnapshots,
			AnyPushWallMoving = anyPushWallMoving,
			Actors = actorSnapshots,
			WeaponSlots = weaponSlotSnapshots,
			StatObjList = statObjSnapshots,
			LastStatObj = lastStatObj,
			PatrolDirectionAtTile = patrolDirs,
			InventoryValues = Inventory.SaveState(),
			RngStateA = rng.StateA,
			RngStateB = rng.StateB,
			GameClock = gameClock.SaveState()
		};
	}

	/// <summary>
	/// Restores the simulator to a previously saved state.
	///
	/// Prerequisites (caller must do before calling this):
	/// 1. Simulator constructed with same stateCollection, rng, gameClock
	/// 2. Level loaded (LoadDoorsFromMapAnalysis, LoadPushWallsFromMapAnalysis,
	///    LoadActorsFromMapAnalysis, LoadBonusesFromMapAnalysis, LoadItemScripts)
	///
	/// This method:
	/// 1. Validates GameName matches
	/// 2. Restores all entity dynamic state
	/// 3. Resolves CurrentState references for actors/weapons via StateCollection
	/// 4. Rebuilds spatial indices from entity positions
	/// 5. Rebuilds AreaConnect from door states
	/// 6. Restores RNG, GameClock, inventory, and simulation time
	/// 7. Clears pending actions
	/// </summary>
	public void LoadState(SimulatorSnapshot snapshot)
	{
		if (snapshot == null)
			throw new ArgumentNullException(nameof(snapshot));

		// Validate game compatibility
		if (!string.IsNullOrEmpty(GameName) && !string.IsNullOrEmpty(snapshot.GameName)
			&& GameName != snapshot.GameName)
		{
			throw new InvalidOperationException(
				$"Save game is for '{snapshot.GameName}' but current game is '{GameName}'");
		}

		// Restore simulation time
		CurrentTic = snapshot.CurrentTic;
		accumulatedTime = snapshot.AccumulatedTime;

		// Restore player position
		PlayerX = snapshot.PlayerX;
		PlayerY = snapshot.PlayerY;
		NoClip = snapshot.NoClip;
		GodMode = snapshot.GodMode;

		// Restore door dynamic state (doors already created by LoadDoorsFromMapAnalysis)
		if (snapshot.Doors != null)
		{
			for (int i = 0; i < Math.Min(doors.Count, snapshot.Doors.Length); i++)
				doors[i].LoadState(snapshot.Doors[i]);
		}

		// Restore pushwall dynamic state
		if (snapshot.PushWalls != null)
		{
			for (int i = 0; i < Math.Min(pushWalls.Count, snapshot.PushWalls.Length); i++)
				pushWalls[i].LoadState(snapshot.PushWalls[i]);
		}
		anyPushWallMoving = snapshot.AnyPushWallMoving;

		// Restore actor state (value fields only)
		if (snapshot.Actors != null)
		{
			for (int i = 0; i < Math.Min(actors.Count, snapshot.Actors.Length); i++)
				actors[i].LoadState(snapshot.Actors[i]);
		}

		// Resolve CurrentState references for actors via StateCollection
		if (snapshot.Actors != null)
		{
			for (int i = 0; i < Math.Min(actors.Count, snapshot.Actors.Length); i++)
			{
				string stateName = snapshot.Actors[i].CurrentStateName;
				if (stateName != null && stateCollection.States.TryGetValue(stateName, out GameplayState state))
					actors[i].CurrentState = state;
			}
		}

		// Restore weapon slot state (value fields only)
		if (snapshot.WeaponSlots != null)
		{
			for (int i = 0; i < Math.Min(weaponSlots.Count, snapshot.WeaponSlots.Length); i++)
				weaponSlots[i].LoadState(snapshot.WeaponSlots[i]);
		}

		// Resolve CurrentState references for weapon slots via StateCollection
		if (snapshot.WeaponSlots != null)
		{
			for (int i = 0; i < Math.Min(weaponSlots.Count, snapshot.WeaponSlots.Length); i++)
			{
				string stateName = snapshot.WeaponSlots[i].CurrentStateName;
				if (stateName != null && stateCollection.States.TryGetValue(stateName, out GameplayState state))
					weaponSlots[i].CurrentState = state;
			}
		}

		// Restore StatObjList
		if (snapshot.StatObjList != null)
		{
			for (int i = 0; i < Math.Min(StatObjList.Length, snapshot.StatObjList.Length); i++)
			{
				if (snapshot.StatObjList[i] != null)
				{
					if (StatObjList[i] == null)
						StatObjList[i] = new StatObj();
					StatObjList[i].LoadState(snapshot.StatObjList[i]);
				}
				else
				{
					StatObjList[i] = new StatObj(); // Free slot
				}
			}
		}
		lastStatObj = snapshot.LastStatObj;

		// Restore patrol directions
		if (snapshot.PatrolDirectionAtTile != null)
		{
			patrolDirectionAtTile = new Dictionary<uint, Direction>(snapshot.PatrolDirectionAtTile.Count);
			foreach (KeyValuePair<uint, byte> kvp in snapshot.PatrolDirectionAtTile)
				patrolDirectionAtTile[kvp.Key] = (Direction)kvp.Value;
		}

		// Restore inventory
		if (snapshot.InventoryValues != null)
			Inventory.LoadState(snapshot.InventoryValues);

		// Restore RNG state
		rng.StateA = snapshot.RngStateA;
		rng.StateB = snapshot.RngStateB;

		// Restore GameClock
		if (snapshot.GameClock != null)
			gameClock.LoadState(snapshot.GameClock);

		// Clear pending actions (transient per-frame queue)
		pendingActions.Clear();

		// Rebuild spatial indices from entity positions
		RebuildSpatialIndices();

		// Rebuild AreaConnect from door states
		RebuildAreaConnect();
	}

	/// <summary>
	/// Rebuilds all spatial index arrays (actorAtTile, doorAtTile, pushWallAtTile)
	/// from current entity positions. Called after LoadState to reconstruct derived data.
	/// </summary>
	private void RebuildSpatialIndices()
	{
		int tileCount = mapWidth * mapHeight;

		// Clear all spatial indices
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

		// Rebuild door spatial index
		// Doors that are fully Open have their doorAtTile cleared (allows actor pathfinding through)
		// All other states (Closed, Opening, Closing) occupy the tile
		for (int i = 0; i < doors.Count; i++)
		{
			if (doors[i].Action != DoorAction.Open)
			{
				int tileIdx = GetTileIndex(doors[i].TileX, doors[i].TileY);
				doorAtTile[tileIdx] = (short)i;
			}
		}

		// Rebuild pushwall spatial index from current positions
		for (int i = 0; i < pushWalls.Count; i++)
		{
			(ushort tileX, ushort tileY) = pushWalls[i].GetTilePosition();
			int tileIdx = GetTileIndex(tileX, tileY);
			pushWallAtTile[tileIdx] = (short)i;
		}

		// Rebuild actor spatial index
		for (int i = 0; i < actors.Count; i++)
		{
			int tileIdx = GetTileIndex(actors[i].TileX, actors[i].TileY);
			actorAtTile[tileIdx] = (short)i;
		}
	}

	/// <summary>
	/// Rebuilds the AreaConnect symmetric matrix from current door states.
	/// Open doors increment the connection count between the two areas they connect.
	/// Called after LoadState to reconstruct derived data.
	/// </summary>
	private void RebuildAreaConnect()
	{
		if (AreaConnect == null || mapAnalysis == null)
			return;

		// Reset all connections to zero
		AreaConnect = new SymmetricMatrix(mapAnalysis.FloorCodeCount);

		// Open doors connect their two areas
		for (int i = 0; i < doors.Count; i++)
		{
			Door door = doors[i];
			if (door.Action == DoorAction.Open && door.Area1 >= 0 && door.Area2 >= 0)
			{
				short currentCount = AreaConnect[(ushort)door.Area1, (ushort)door.Area2];
				AreaConnect[(ushort)door.Area1, (ushort)door.Area2] = (short)(currentCount + 1);
			}
		}
	}
	#endregion
}
