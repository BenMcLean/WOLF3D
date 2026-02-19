using BenMcLean.Wolf3D.Assets.Gameplay;
using BenMcLean.Wolf3D.Simulator.State;

namespace BenMcLean.Wolf3D.Simulator.Entities;

/// <summary>
/// Pushwall state in the simulation. Mutable.
/// Based on WL_DEF.H:pwallstruct and related pushwall logic in WL_ACT1.C.
/// Pushwalls are special walls that can be pushed by the player to reveal secrets.
/// </summary>
public class PushWall : IStateSavable<PushWallSnapshot>
{
	/// <summary>
	/// How long a pushwall takes to move one full tile (in tics).
	/// Redirects to Constants.PushTicsPerTile.
	/// </summary>
	public const short PushTics = Constants.PushTicsPerTile;

	// Static properties (from MapAnalysis.PushWallSpawn - not serialized, loaded from map)

	/// <summary>
	/// Wall texture number (VSWAP page number).
	/// Even page for horizontal faces, odd page (Shape+1) for vertical faces.
	/// </summary>
	public ushort Shape { get; }

	/// <summary>
	/// Initial tile X coordinate where the pushwall starts.
	/// Used to identify the pushwall and calculate final position.
	/// Intentional extension: Using ushort to support maps > 64×64
	/// </summary>
	public ushort InitialTileX { get; }

	/// <summary>
	/// Initial tile Y coordinate where the pushwall starts.
	/// Used to identify the pushwall and calculate final position.
	/// Intentional extension: Using ushort to support maps > 64×64
	/// </summary>
	public ushort InitialTileY { get; }

	// Dynamic state (serialized for save games)

	/// <summary>
	/// Current pushwall state (idle or pushing).
	/// </summary>
	public PushWallAction Action { get; set; }

	/// <summary>
	/// Direction the pushwall is moving.
	/// Only valid when Action == PushWallAction.Pushing.
	/// </summary>
	public Direction Direction { get; set; }

	/// <summary>
	/// Current X position in 16.16 fixed-point format.
	/// High 16 bits = tile, low 16 bits = fractional position within tile.
	/// </summary>
	public int X { get; set; }

	/// <summary>
	/// Current Y position in 16.16 fixed-point format.
	/// High 16 bits = tile, low 16 bits = fractional position within tile.
	/// </summary>
	public int Y { get; set; }

	/// <summary>
	/// Number of tics remaining until pushwall reaches final position.
	/// Counts down from PushTics to 0.
	/// </summary>
	public short TicCount { get; set; }

	/// <summary>
	/// Whether this pushwall has ever been activated (pushed).
	/// Used for statistics tracking (secret count).
	/// A pushwall pushed multiple times still counts as only one secret found.
	/// </summary>
	public bool HasBeenActivated { get; set; }

	/// <summary>
	/// Creates a new PushWall instance at its initial position.
	/// </summary>
	/// <param name="shape">Wall texture number (VSWAP page)</param>
	/// <param name="initialTileX">Starting tile X coordinate</param>
	/// <param name="initialTileY">Starting tile Y coordinate</param>
	public PushWall(ushort shape, ushort initialTileX, ushort initialTileY)
	{
		Shape = shape;
		InitialTileX = initialTileX;
		InitialTileY = initialTileY;

		// Start at tile center in fixed-point (tile * 65536 + 32768)
		X = (initialTileX << 16) + 0x8000;
		Y = (initialTileY << 16) + 0x8000;

		Action = PushWallAction.Idle;
		Direction = Direction.N; // Doesn't matter when idle
		TicCount = 0;
	}

	/// <summary>
	/// Get the current tile coordinates from fixed-point position.
	/// Useful for collision detection and spatial queries.
	/// </summary>
	public (ushort tileX, ushort tileY) GetTilePosition() =>
		((ushort)(X >> 16), (ushort)(Y >> 16));

	/// <summary>
	/// Check if this pushwall currently occupies a specific tile.
	/// During movement, a pushwall may occupy its start tile or destination tile.
	/// </summary>
	public bool OccupiesTile(ushort x, ushort y)
	{
		(ushort currentX, ushort currentY) = GetTilePosition();
		return currentX == x && currentY == y;
	}

	/// <summary>
	/// Captures only dynamic pushwall state. Static properties (Shape, InitialTileX,
	/// InitialTileY) come from map data on restore via LoadPushWallsFromMapAnalysis.
	/// </summary>
	public PushWallSnapshot SaveState() => new()
	{
		Action = (byte)Action,
		Direction = (byte)Direction,
		X = X,
		Y = Y,
		TicCount = TicCount,
	};

	/// <summary>
	/// Restores dynamic pushwall state from a snapshot.
	/// Static properties are not modified (they come from LoadPushWallsFromMapAnalysis).
	/// </summary>
	public void LoadState(PushWallSnapshot state)
	{
		Action = (PushWallAction)state.Action;
		Direction = (Direction)state.Direction;
		X = state.X;
		Y = state.Y;
		TicCount = state.TicCount;
	}
}

/// <summary>
/// Pushwall action states.
/// Based on Wolf3D pushwall behavior in WL_ACT1.C.
/// </summary>
public enum PushWallAction : byte
{
	/// <summary>Pushwall is stationary at its initial position</summary>
	Idle,
	/// <summary>Pushwall is currently moving one tile in a direction</summary>
	Pushing
}
