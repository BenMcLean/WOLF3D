namespace BenMcLean.Wolf3D.Simulator.State;

/// <summary>
/// Serializable snapshot of a PushWall's dynamic state.
/// Only mutable fields are captured; static properties (Shape, InitialTileX,
/// InitialTileY) come from the map data and are loaded by LoadPushWallsFromMapAnalysis.
///
/// PushWalls are matched by index (position in the pushWalls list), which assumes
/// the same map is loaded in the same order on restore.
///
/// Enums stored as underlying numeric types for format-agnostic safety.
/// </summary>
public record PushWallSnapshot
{
	/// <summary>
	/// Current pushwall state as byte (PushWallAction enum underlying type).
	/// 0=Idle, 1=Pushing
	/// </summary>
	public byte Action { get; init; }

	/// <summary>
	/// Direction the pushwall is moving as byte (Direction enum underlying type).
	/// Only meaningful when Action == Pushing.
	/// </summary>
	public byte Direction { get; init; }

	/// <summary>
	/// Current X position in 16.16 fixed-point format.
	/// </summary>
	public int X { get; init; }

	/// <summary>
	/// Current Y position in 16.16 fixed-point format.
	/// </summary>
	public int Y { get; init; }

	/// <summary>
	/// Tics remaining until pushwall reaches final position.
	/// Counts down from PushTics to 0.
	/// </summary>
	public short TicCount { get; init; }
}
