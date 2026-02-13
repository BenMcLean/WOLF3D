using BenMcLean.Wolf3D.Simulator.State;

namespace BenMcLean.Wolf3D.Simulator.Entities;

/// <summary>
/// Static object state in the simulation - ONLY bonus/pickup items (has game logic).
/// Fixtures (scenery) are display-only and not simulated.
/// Based on WL_DEF.H:statstruct (lines 948-955).
/// </summary>
public class StatObj : IStateSavable<StatObj>
{
	/// <summary>
	/// Maximum number of static objects (bonus items) per level.
	/// WL_DEF.H:MAXSTATS = 400 (or 200 for Noah3D)
	/// </summary>
	public const int MAXSTATS = 400;

	// WL_DEF.H:statstruct:tilex (original: byte)
	// Intentional extension: Using ushort to support maps > 64×64
	public ushort TileX { get; set; }

	// WL_DEF.H:statstruct:tiley (original: byte)
	// Intentional extension: Using ushort to support maps > 64×64
	public ushort TileY { get; set; }

	// WL_DEF.H:statstruct:shapenum (int = 16-bit signed)
	// -1 indicates the object has been removed (free slot)
	public short ShapeNum { get; set; }

	// WL_DEF.H:statstruct:flags
	public byte Flags { get; set; }

	// WL_DEF.H:statstruct:itemnumber
	// Item type: bo_clip, bo_food, bo_key1, etc.
	public byte ItemNumber { get; set; }

	/// <summary>
	/// Creates a new static object (bonus item).
	/// </summary>
	public StatObj(ushort tileX, ushort tileY, short shapeNum, byte flags, byte itemNumber)
	{
		TileX = tileX;
		TileY = tileY;
		ShapeNum = shapeNum;
		Flags = flags;
		ItemNumber = itemNumber;
	}

	/// <summary>
	/// Creates an empty/removed static object slot.
	/// ShapeNum = -1 indicates a free slot.
	/// </summary>
	public StatObj()
	{
		ShapeNum = -1;
	}

	/// <summary>
	/// Check if this slot is free (object has been removed).
	/// WL_ACT1.C:PlaceItemType checks for shapenum == -1
	/// </summary>
	public bool IsFree => ShapeNum == -1;

	/// <summary>
	/// Returns a shallow copy of this StatObj.
	/// StatObj is its own snapshot type since all properties are already public+settable
	/// and no type conversions are needed.
	/// </summary>
	public StatObj SaveState() => new(TileX, TileY, ShapeNum, Flags, ItemNumber);

	/// <summary>
	/// Copies all field values from another StatObj instance.
	/// </summary>
	public void LoadState(StatObj other)
	{
		TileX = other.TileX;
		TileY = other.TileY;
		ShapeNum = other.ShapeNum;
		Flags = other.Flags;
		ItemNumber = other.ItemNumber;
	}
}
