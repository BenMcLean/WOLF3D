using Microsoft.Extensions.Logging;

namespace BenMcLean.Wolf3D.Simulator.Lua;

/// <summary>
/// Script context for door interaction scripts.
/// Extends EntityScriptContext with door-specific API.
/// Called when player attempts to open a door, allows checking for custom key types.
/// Inherits PlayLocalDigiSound for positional audio at door location.
/// </summary>
public class DoorScriptContext : EntityScriptContext
{
	protected readonly int doorX;
	protected readonly int doorY;
	// TODO: Add door-specific state (door type, lock state, etc.)

	public DoorScriptContext(
		Simulator simulator,
		RNG rng,
		GameClock gameClock,
		int doorX,
		int doorY,
		ILogger logger = null)
		: base(simulator, rng, gameClock, doorX, doorY, logger)
	{
		this.doorX = doorX;
		this.doorY = doorY;
	}

	// Door-specific API methods will be added here
	// Examples:
	// - CheckCustomKey(string keyName)
	// - GetDoorType()
	// - IsLocked()
	// - etc.

	// Actor API stubs (not used by doors)
	public override void SpawnActor(int type, int x, int y) { }
	public override void DespawnActor(int actorId) { }
}
