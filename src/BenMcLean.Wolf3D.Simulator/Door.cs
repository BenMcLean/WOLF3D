namespace BenMcLean.Wolf3D.Simulator;

/// <summary>
/// Door state in the simulation. Mutable.
/// Based on WL_DEF.H:doorstruct (lines 964-975) and related door logic in WL_ACT1.C.
/// </summary>
public class Door
{
	/// <summary>
	/// How long a door stays open before auto-closing (in tics).
	/// WL_ACT1.C:OPENTICS = 300 tics (~4.3 seconds at 70Hz)
	/// </summary>
	public const short OpenTics = 300;

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

	public Door(ushort tileX, ushort tileY, bool facesEastWest, ushort tileNumber)
	{
		TileX = tileX;
		TileY = tileY;
		FacesEastWest = facesEastWest;
		TileNumber = tileNumber;
		Action = DoorAction.Closed;
		Position = 0;
		TicCount = 0;
	}

	/// <summary>
	/// Get the current normalized open amount (0.0 = closed, 1.0 = fully open).
	/// Useful for rendering/interpolation.
	/// </summary>
	public float GetOpenAmount() => Position / 65535f;
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
