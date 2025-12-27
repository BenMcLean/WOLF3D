using System;
using BenMcLean.Wolf3D.Assets;

namespace BenMcLean.Wolf3D.Simulator;

/// <summary>
/// Actor (enemy/NPC) state in the simulation. Mutable.
/// Based on WL_DEF.H:objstruct (lines 884-933) and related actor logic.
/// Actors use state machines defined in State.cs, with Think/Action functions in Lua.
/// </summary>
public class Actor
{
	// Static properties (from MapAnalysis.ActorSpawn - not serialized, loaded from map)

	/// <summary>
	/// Actor type identifier (e.g., "guard", "ss", "dog", "mutant").
	/// Maps to ObjectInfo.Actor from MapAnalyzer.
	/// </summary>
	public string ActorType { get; }

	// Dynamic state (serialized for save games)

	/// <summary>
	/// WL_DEF.H:objstruct:state (original: statetype*)
	/// Current state in the actor's state machine.
	/// Determines sprite, duration, Think/Action functions.
	/// </summary>
	public State CurrentState { get; set; }

	/// <summary>
	/// WL_DEF.H:objstruct:ticcount (original: int = 16-bit signed)
	/// Number of tics remaining in the current state.
	/// When this reaches 0, transition to CurrentState.Next.
	/// </summary>
	public short TicCount { get; set; }

	/// <summary>
	/// WL_DEF.H:objstruct:tilex (original: byte)
	/// Intentional extension: Using ushort to support maps > 64×64
	/// Current tile X coordinate (integer tile position).
	/// </summary>
	public ushort TileX { get; set; }

	/// <summary>
	/// WL_DEF.H:objstruct:tiley (original: byte)
	/// Intentional extension: Using ushort to support maps > 64×64
	/// Current tile Y coordinate (integer tile position).
	/// </summary>
	public ushort TileY { get; set; }

	/// <summary>
	/// WL_DEF.H:objstruct:x (original: long = 32-bit signed)
	/// Fixed-point 16.16 X coordinate.
	/// High 16 bits = tile, low 16 bits = fractional position within tile.
	/// </summary>
	public int X { get; set; }

	/// <summary>
	/// WL_DEF.H:objstruct:y (original: long = 32-bit signed)
	/// Fixed-point 16.16 Y coordinate.
	/// High 16 bits = tile, low 16 bits = fractional position within tile.
	/// </summary>
	public int Y { get; set; }

	/// <summary>
	/// WL_DEF.H:objstruct:dir (original: dirtype enum)
	/// Current facing direction (8-way).
	/// Used for sprite rotation and movement.
	/// Null represents "nodir" (blocked, no valid direction).
	/// </summary>
	public Direction? Facing { get; set; }

	/// <summary>
	/// WL_DEF.H:objstruct:hitpoints (original: int = 16-bit signed)
	/// Current hit points. When <= 0, actor dies.
	/// </summary>
	public short HitPoints { get; set; }

	/// <summary>
	/// WL_DEF.H:objstruct:speed (original: long = 32-bit signed)
	/// Movement speed in fixed-point 16.16 format.
	/// Higher values = faster movement.
	/// </summary>
	public int Speed { get; set; }

	/// <summary>
	/// Current sprite/shape number being displayed.
	/// Derived from CurrentState.Shape, but can be overridden by rotation.
	/// -1 indicates no sprite (invisible actor).
	/// </summary>
	public short ShapeNum { get; set; }

	/// <summary>
	/// WL_DEF.H:objstruct:flags (original: int = 16-bit signed, used as bitfield)
	/// Actor flags (shootable, ambush, patrol, etc.).
	/// </summary>
	public ActorFlags Flags { get; set; }

	/// <summary>
	/// WL_DEF.H:objstruct:distance (original: long = 32-bit signed)
	/// Distance remaining in current movement to next tile center (TILEGLOBAL = 0x10000).
	/// If negative, encodes door waiting: value = -(doorIndex+1), e.g. -1 = door 0, -2 = door 1
	/// </summary>
	public int Distance { get; set; }

	/// <summary>
	/// WL_STATE.C: Reaction timer (ob->temp2 in original code).
	/// Counts down tics before actor reacts to seeing player.
	/// Set when player first spotted, decrements each tic until 0.
	/// </summary>
	public short ReactionTimer { get; set; }

	/// <summary>
	/// Creates a new Actor instance.
	/// </summary>
	/// <param name="actorType">Actor type identifier (e.g., "guard", "ss")</param>
	/// <param name="initialState">Starting state in the state machine</param>
	/// <param name="tileX">Starting tile X coordinate</param>
	/// <param name="tileY">Starting tile Y coordinate</param>
	/// <param name="facing">Starting facing direction</param>
	/// <param name="hitPoints">Starting hit points</param>
	public Actor(string actorType, State initialState, ushort tileX, ushort tileY, Direction facing, short hitPoints)
	{
		ActorType = actorType;
		CurrentState = initialState;
		TicCount = initialState.Tics;
		TileX = tileX;
		TileY = tileY;
		// Convert tile coordinates to fixed-point (center of tile)
		X = (tileX << 16) + 0x8000; // Tile center = tile * 65536 + 32768
		Y = (tileY << 16) + 0x8000;
		Facing = facing;
		HitPoints = hitPoints;
		Speed = initialState.Speed;
		ShapeNum = initialState.Shape;
		Flags = ActorFlags.None;
		Distance = 0;
	}

	/// <summary>
	/// Get the current tile coordinates from fixed-point position.
	/// Useful for collision detection and pathfinding.
	/// </summary>
	public (ushort tileX, ushort tileY) GetTilePosition() => (TileX, TileY);
}

/// <summary>
/// Actor flags matching original Wolf3D objstruct.flags bitfield.
/// WL_DEF.H:objstruct:flags and related FL_* constants
/// </summary>
[Flags]
public enum ActorFlags : int
{
	None = 0,
	/// <summary>FL_SHOOTABLE - Can be damaged by player</summary>
	Shootable = 1 << 0,
	/// <summary>FL_BONUS - Is a bonus item (pickup)</summary>
	Bonus = 1 << 1,
	/// <summary>FL_NEVERMARK - Never mark as seen on automap</summary>
	NeverMark = 1 << 2,
	/// <summary>FL_VISABLE - Currently visible to player</summary>
	Visible = 1 << 3,
	/// <summary>FL_ATTACKMODE - Currently attacking player</summary>
	AttackMode = 1 << 4,
	/// <summary>FL_FIRSTATTACK - First attack in attack sequence</summary>
	FirstAttack = 1 << 5,
	/// <summary>FL_AMBUSH - Ambush mode (doesn't activate until player seen)</summary>
	Ambush = 1 << 6,
	/// <summary>FL_NONMARK - Don't mark on automap this frame</summary>
	NonMark = 1 << 7,
	/// <summary>FL_FULLBRIGHT - Render at full brightness</summary>
	FullBright = 1 << 8,
	/// <summary>Custom: Actor has line-of-sight to player</summary>
	CanSeePlayer = 1 << 16,
	/// <summary>Custom: Actor is patrolling</summary>
	Patrolling = 1 << 17,
}
