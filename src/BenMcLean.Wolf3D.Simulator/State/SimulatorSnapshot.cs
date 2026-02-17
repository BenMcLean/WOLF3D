using System.Collections.Generic;
using BenMcLean.Wolf3D.Simulator.Entities;

namespace BenMcLean.Wolf3D.Simulator.State;

/// <summary>
/// Top-level serializable snapshot of the entire Simulator state.
/// Contains all mutable runtime state needed to restore a saved game.
///
/// What is NOT included (and where it comes from on restore):
/// - Map geometry/dimensions: Loaded from map data files
/// - Door static properties (TileX/Y, FacesEastWest, etc.): From LoadDoorsFromMapAnalysis
/// - PushWall static properties (Shape, InitialTile): From LoadPushWallsFromMapAnalysis
/// - Actor definitions: From StateCollection (loaded from XML)
/// - Weapon definitions: From WeaponCollection (loaded from XML)
/// - Spatial indices (actorAtTile, doorAtTile, pushWallAtTile): Rebuilt from entity positions
/// - AreaConnect matrix: Rebuilt from door open/closed states
/// - Events/delegates: Presentation layer re-subscribes independently
/// - HitDetection callback: Set by presentation layer
/// - Lua script engine: Recreated from stateCollection
/// - Item scripts: Reloaded from game config
/// - pendingActions: Transient per-frame queue, cleared on restore
///
/// Restore flow:
/// 1. Caller deserializes SimulatorSnapshot from file
/// 2. Caller checks GameName to load correct game XML
/// 3. Caller creates Simulator normally (constructor with stateCollection, rng, gameClock)
/// 4. Caller loads level indicated by InventoryValues["MapOn"]
/// 5. Caller calls simulator.LoadState(snapshot) which overwrites freshly-loaded state
/// 6. Caller re-subscribes events and sets HitDetection callback
///
/// Wolf3D only saves one level at a time; games that persist multiple levels
/// simultaneously (Blake Stone, Corridor 7) would need a snapshot per level
/// plus cross-level state.
/// </summary>
public record SimulatorSnapshot
{
	/// <summary>
	/// Game identifier (from XML Game Name attribute, e.g., "Wolfenstein 3-D").
	/// Used to validate that the correct game config is loaded before restoring.
	/// </summary>
	public string GameName { get; init; }

	/// <summary>
	/// Current simulation time in tics (70Hz).
	/// WL_PLAY.C:tics - accumulated from game start.
	/// </summary>
	public long CurrentTic { get; init; }

	/// <summary>
	/// Fractional time accumulated but not yet processed as a tic.
	/// Preserved for deterministic resumption at sub-tic precision.
	/// </summary>
	public double AccumulatedTime { get; init; }

	/// <summary>
	/// Player X position in 16.16 fixed-point.
	/// WL_DEF.H:player->x
	/// </summary>
	public int PlayerX { get; init; }

	/// <summary>
	/// Player Y position in 16.16 fixed-point.
	/// WL_DEF.H:player->y
	/// </summary>
	public int PlayerY { get; init; }

	/// <summary>
	/// Player angle in degrees (0-359).
	/// WL_DEF.H:player->angle
	/// </summary>
	public short PlayerAngle { get; init; }

	/// <summary>
	/// Whether noclip cheat is active.
	/// Based on original Wolf3D "MLI" debug command.
	/// </summary>
	public bool NoClip { get; init; }

	/// <summary>
	/// Whether god mode cheat is active.
	/// Based on original Wolf3D god mode cheat.
	/// WL_PLAY.C:godmode
	/// </summary>
	public bool GodMode { get; init; }

	/// <summary>
	/// Dynamic state of all doors. Matched by index to doors loaded from map.
	/// Static door properties come from LoadDoorsFromMapAnalysis.
	/// </summary>
	public DoorSnapshot[] Doors { get; init; }

	/// <summary>
	/// Dynamic state of all pushwalls. Matched by index to pushwalls loaded from map.
	/// Static pushwall properties come from LoadPushWallsFromMapAnalysis.
	/// </summary>
	public PushWallSnapshot[] PushWalls { get; init; }

	/// <summary>
	/// Global pushwall lock: only one can move at a time per level.
	/// WL_ACT1.C global pushwall state.
	/// </summary>
	public bool AnyPushWallMoving { get; init; }

	/// <summary>
	/// State of all actors. Matched by index to actors loaded from map.
	/// CurrentStateName is resolved to State references via StateCollection on restore.
	/// </summary>
	public ActorSnapshot[] Actors { get; init; }

	/// <summary>
	/// State of all weapon slots. Matched by SlotIndex.
	/// CurrentStateName is resolved to State references via StateCollection on restore.
	/// </summary>
	public WeaponSlotSnapshot[] WeaponSlots { get; init; }

	/// <summary>
	/// Bonus/pickup objects. StatObj is directly serializable (all props are public+settable).
	/// No separate DTO needed.
	/// </summary>
	public StatObj[] StatObjList { get; init; }

	/// <summary>
	/// WL_ACT1.C:laststatobj - next free slot index in StatObjList.
	/// </summary>
	public int LastStatObj { get; init; }

	/// <summary>
	/// Patrol direction lookup (WL_ACT2.C:SelectPathDir).
	/// Key encoding: (Y &lt;&lt; 16) | X. Value: Direction enum as byte.
	/// </summary>
	public Dictionary<uint, byte> PatrolDirectionAtTile { get; init; }

	/// <summary>
	/// All player inventory values (health, score, lives, keys, ammo, weapons, MapOn).
	/// </summary>
	public Dictionary<string, int> InventoryValues { get; init; }

	/// <summary>
	/// Accumulated level completion stats across the episode.
	/// Grows as the player progresses through levels; used by Victory screen.
	/// </summary>
	public LevelCompletionStats[] CompletedLevelStats { get; init; }

	/// <summary>
	/// RNG state A (part of TangleRNG two-state PRNG).
	/// </summary>
	public ulong RngStateA { get; init; }

	/// <summary>
	/// RNG state B (part of TangleRNG two-state PRNG, always odd).
	/// </summary>
	public ulong RngStateB { get; init; }

	/// <summary>
	/// GameClock state (epoch + elapsed tics).
	/// </summary>
	public GameClockSnapshot GameClock { get; init; }
}
