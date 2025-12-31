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

	// Action API implementations (doors may need limited subset)
	public override void SpawnActor(int type, int x, int y)
	{
		// TODO: Implement or disable for door context
	}

	public override void DespawnActor(int actorId)
	{
		// TODO: Implement or disable for door context
	}

	public override int GetPlayerHealth()
	{
		// TODO: Return actual player health
		return 100;
	}

	public override int GetPlayerMaxHealth()
	{
		return 100;
	}

	public override void HealPlayer(int amount)
	{
		// TODO: Implement healing
	}

	public override void DamagePlayer(int amount)
	{
		// TODO: Implement damage
	}

	public override void GivePlayerAmmo(int weaponType, int amount)
	{
		// TODO: Implement ammo
	}

	public override void GivePlayerKey(int keyColor)
	{
		// TODO: Implement keys
	}

	public override bool PlayerHasKey(int keyColor)
	{
		// TODO: Return actual key state
		return false;
	}
}
