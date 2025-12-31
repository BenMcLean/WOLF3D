using BenMcLean.Wolf3D.Assets.Gameplay;

namespace BenMcLean.Wolf3D.Simulator;

/// <summary>
/// Base class for player actions that can be queued.
/// </summary>
public abstract class PlayerAction
{
}

/// <summary>
/// Player attempts to operate (open/close) a door.
/// </summary>
public class OperateDoorAction : PlayerAction
{
	public required ushort DoorIndex { get; init; }
}

/// <summary>
/// Player attempts to push a pushwall.
/// Based on WL_ACT1.C pushwall activation logic.
/// </summary>
public class ActivatePushWallAction : PlayerAction
{
	/// <summary>Tile X coordinate of the wall being pushed</summary>
	public required ushort TileX { get; init; }
	/// <summary>Tile Y coordinate of the wall being pushed</summary>
	public required ushort TileY { get; init; }
	/// <summary>Direction the pushwall should move (away from player)</summary>
	public required Direction Direction { get; init; }
}
