using BenMcLean.Wolf3D.Simulator.State;

namespace BenMcLean.Wolf3D.Simulator.Entities;

/// <summary>
/// Door state in the simulation. Mutable.
/// Based on WL_DEF.H:doorstruct (lines 964-975) and related door logic in WL_ACT1.C.
/// </summary>
public class Door : IStateSavable<DoorSnapshot>
{
	// Static properties (from MapAnalysis.DoorSpawn - not serialized, loaded from map)
	// WL_DEF.H:doorstruct:tilex (original: byte)
	// Intentional extension: Using ushort to support maps > 64×64
	public ushort TileX { get; }

	// WL_DEF.H:doorstruct:tiley (original: byte)
	// Intentional extension: Using ushort to support maps > 64×64
	public ushort TileY { get; }

	// WL_DEF.H:doorstruct:vertical
	// Renamed to FacesEastWest for semantic clarity
	public bool FacesEastWest { get; }

	// WL_DEF.H:doorstruct:lock (byte in original)
	// Extended to string for modding: null = unlocked, "gold key" = requires gold key, etc.
	// Modders can define unlimited custom keys in WOLF3D.xml
	public string Lock { get; }

	// Door type identifier for looking up metadata (sounds, etc.) from MapAnalyzer.Doors
	public ushort TileNumber { get; }

	// WL_ACT1.C:DoorOpening lines 715-728 - area connectivity for hearing propagation
	// These are the two area numbers (floor codes) this door connects
	// For FacesEastWest doors: Area1 is left (X-1), Area2 is right (X+1)
	// For horizontal doors: Area1 is above (Y-1), Area2 is below (Y+1)
	// -1 indicates no valid area on that side
	public short Area1 { get; }
	public short Area2 { get; }
	// Dynamic state (serialized for save games)

	// WL_DEF.H:doorstruct:action
	// Current door state: dr_open, dr_closed, dr_opening, dr_closing
	public DoorAction Action { get; set; }

	// WL_ACT1.C:doorposition[MAXDOORS] (unsigned)
	// 0 = closed, 0xFFFF = fully open
	public ushort Position { get; set; }

	// WL_DEF.H:doorstruct:ticcount (int = 16-bit signed)
	// Used for auto-close timer when door is open
	public short TicCount { get; set; }

	public Door(ushort tileX, ushort tileY, bool facesEastWest, ushort tileNumber, short area1 = -1, short area2 = -1)
	{
		TileX = tileX;
		TileY = tileY;
		FacesEastWest = facesEastWest;
		TileNumber = tileNumber;
		Area1 = area1;
		Area2 = area2;
		Action = DoorAction.Closed;
		Position = 0;
		TicCount = 0;
	}

	/// <summary>
	/// Captures only dynamic door state. Static properties (TileX, TileY, FacesEastWest,
	/// Lock, TileNumber, Area1, Area2) come from map data on restore.
	/// </summary>
	public DoorSnapshot SaveState() => new()
	{
		Action = (byte)Action,
		Position = Position,
		TicCount = TicCount
	};

	/// <summary>
	/// Restores dynamic door state from a snapshot.
	/// Static properties are not modified (they come from LoadDoorsFromMapAnalysis).
	/// </summary>
	public void LoadState(DoorSnapshot state)
	{
		Action = (DoorAction)state.Action;
		Position = state.Position;
		TicCount = state.TicCount;
	}
}

/// <summary>
/// Door action states matching original Wolf3D.
/// WL_DEF.H:doorstruct:action (enum within struct)
/// </summary>
public enum DoorAction : byte
{
	Open,      // dr_open - door fully open, waiting to close
	Closed,    // dr_closed - door fully closed
	Opening,   // dr_opening - door sliding open
	Closing    // dr_closing - door sliding closed
}
