namespace BenMcLean.Wolf3D.Simulator.State;

/// <summary>
/// Serializable snapshot of a Door's dynamic state.
/// Only mutable fields are captured; static door properties (TileX, TileY,
/// FacesEastWest, Lock, TileNumber, Area1, Area2) come from the map data
/// and are loaded by LoadDoorsFromMapAnalysis.
///
/// Doors are matched by index (position in the doors list), which assumes
/// the same map is loaded in the same order on restore.
///
/// Enums stored as underlying numeric types for format-agnostic safety.
/// </summary>
public record DoorSnapshot
{
	/// <summary>
	/// WL_DEF.H:doorstruct:action
	/// Current door state as byte (DoorAction enum underlying type).
	/// 0=Open, 1=Closed, 2=Opening, 3=Closing
	/// </summary>
	public byte Action { get; init; }

	/// <summary>
	/// WL_ACT1.C:doorposition[MAXDOORS] (unsigned)
	/// Door slide position: 0 = fully closed, 0xFFFF = fully open.
	/// </summary>
	public ushort Position { get; init; }

	/// <summary>
	/// WL_DEF.H:doorstruct:ticcount (int = 16-bit signed)
	/// Timer for auto-close countdown when door is open.
	/// </summary>
	public short TicCount { get; init; }
}
