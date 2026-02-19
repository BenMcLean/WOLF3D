using Microsoft.Extensions.Logging;

namespace BenMcLean.Wolf3D.Simulator.Lua;

/// <summary>
/// Script context for door interaction scripts.
/// Extends EntityScriptContext with door-specific API.
/// Called when player attempts to open a door, script returns true to allow opening.
/// Inherits PlayLocalDigiSound for positional audio at door location.
///
/// Uses generic inventory API inherited from ActionScriptContext:
/// - GetValue(name), SetValue(name, value), AddValue(name, delta)
/// - GetMax(name), Has(name)
///
/// Example Lua script for gold key door:
/// <code>
/// if Has("Gold Key") then
///     return true
/// end
/// PlayLocalDigiSound("NOWAYSND")
/// return false
/// </code>
/// </summary>
public class DoorScriptContext(
	Simulator simulator,
	RNG rng,
	GameClock gameClock,
	int doorTileX,
	int doorTileY,
	ILogger logger = null) : EntityScriptContext(simulator, rng, gameClock, doorTileX, doorTileY, logger)
{
	/// <summary>
	/// Tile X coordinate of the door (exposed to Lua).
	/// </summary>
	public int DoorTileX { get; } = doorTileX;
	/// <summary>
	/// Tile Y coordinate of the door (exposed to Lua).
	/// </summary>
	public int DoorTileY { get; } = doorTileY;
}
