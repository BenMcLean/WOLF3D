namespace BenMcLean.Wolf3D.Simulator.State;

/// <summary>
/// Serializable snapshot of an Actor's mutable state.
/// ActorType is included to identify which actor definition to use on restore.
/// CurrentState is serialized as a string name, resolved via StateCollection
/// by the Simulator after LoadState() restores value-type fields.
///
/// Actors are matched by index (position in the actors list), which assumes
/// the same map is loaded in the same order on restore.
///
/// Enums stored as underlying numeric types for format-agnostic safety.
/// </summary>
public record ActorSnapshot
{
	/// <summary>
	/// Actor type identifier (e.g., "guard", "ss", "dog").
	/// Used to look up ActorDefinition for death/chase/attack states on restore.
	/// </summary>
	public string ActorType { get; init; }

	/// <summary>
	/// Name of the current state in the actor's state machine (e.g., "s_grdstand").
	/// Resolved to a State object reference via StateCollection on restore.
	/// </summary>
	public string CurrentStateName { get; init; }

	/// <summary>
	/// WL_DEF.H:objstruct:ticcount (original: int = 16-bit signed)
	/// Tics remaining in current state before transitioning to next.
	/// </summary>
	public short TicCount { get; init; }

	/// <summary>
	/// WL_DEF.H:objstruct:tilex (original: byte, extended to ushort)
	/// Current tile X coordinate.
	/// </summary>
	public ushort TileX { get; init; }

	/// <summary>
	/// WL_DEF.H:objstruct:tiley (original: byte, extended to ushort)
	/// Current tile Y coordinate.
	/// </summary>
	public ushort TileY { get; init; }

	/// <summary>
	/// WL_DEF.H:objstruct:x (original: fixed = long = 32-bit signed)
	/// Fixed-point 16.16 X coordinate.
	/// </summary>
	public int X { get; init; }

	/// <summary>
	/// WL_DEF.H:objstruct:y (original: fixed = long = 32-bit signed)
	/// Fixed-point 16.16 Y coordinate.
	/// </summary>
	public int Y { get; init; }

	/// <summary>
	/// WL_DEF.H:objstruct:dir (original: dirtype enum, stored as byte)
	/// Current facing direction (8-way). Null represents "nodir".
	/// Stored as nullable byte for format-agnostic serialization.
	/// </summary>
	public byte? Facing { get; init; }

	/// <summary>
	/// WL_DEF.H:objstruct:hitpoints (original: int = 16-bit signed)
	/// Current hit points.
	/// </summary>
	public short HitPoints { get; init; }

	/// <summary>
	/// WL_DEF.H:objstruct:speed (original: long = 32-bit signed)
	/// Movement speed in fixed-point 16.16 format.
	/// </summary>
	public int Speed { get; init; }

	/// <summary>
	/// Current sprite/shape number. -1 = invisible.
	/// </summary>
	public short ShapeNum { get; init; }

	/// <summary>
	/// WL_DEF.H:objstruct:flags (ActorFlags enum, stored as int)
	/// Actor flags bitfield (shootable, ambush, patrol, etc.).
	/// </summary>
	public int Flags { get; init; }

	/// <summary>
	/// WL_DEF.H:objstruct:distance (original: long = 32-bit signed)
	/// Distance remaining in current movement. Negative encodes door waiting.
	/// </summary>
	public int Distance { get; init; }

	/// <summary>
	/// WL_STATE.C: Reaction timer (ob->temp2 in original code).
	/// Counts down tics before actor reacts to seeing player.
	/// </summary>
	public short ReactionTimer { get; init; }
}
