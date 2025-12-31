using Microsoft.Extensions.Logging;

namespace BenMcLean.Wolf3D.Simulator.Lua;

/// <summary>
/// Script context for the action stage (gameplay).
/// Extends BaseScriptContext with deterministic RNG/GameClock and action-specific API.
/// Provides action methods like spawn actors, damage player, inventory management.
/// </summary>
public abstract class ActionScriptContext : BaseScriptContext, IActionScriptContext
{
	protected readonly Simulator simulator;
	protected readonly RNG rng;
	protected readonly GameClock gameClock;

	public RNG RNG => rng;
	public GameClock GameClock => gameClock;

	protected ActionScriptContext(Simulator simulator, RNG rng, GameClock gameClock, ILogger logger = null)
		: base(logger)
	{
		this.simulator = simulator;
		this.rng = rng;
		this.gameClock = gameClock;
	}

	// Action-specific API (to be implemented by derived classes or Simulator integration)
	public abstract void SpawnActor(int type, int x, int y);
	public abstract void DespawnActor(int actorId);
	public abstract int GetPlayerHealth();
	public abstract int GetPlayerMaxHealth();
	public abstract void HealPlayer(int amount);
	public abstract void DamagePlayer(int amount);
	public abstract void GivePlayerAmmo(int weaponType, int amount);
	public abstract void GivePlayerKey(int keyColor);
	public abstract bool PlayerHasKey(int keyColor);
}

/// <summary>
/// Script context for bonus objects (pickups).
/// Extends EntityScriptContext with bonus-specific API.
/// Inherits PlayLocalDigiSound for positional audio at bonus location.
/// </summary>
public class BonusScriptContext : EntityScriptContext
{
	public BonusScriptContext(
		Simulator simulator,
		RNG rng,
		GameClock gameClock,
		int bonusX,
		int bonusY,
		Microsoft.Extensions.Logging.ILogger logger = null)
		: base(simulator, rng, gameClock, bonusX, bonusY, logger)
	{
	}

	// Action API stubs (to be implemented as simulator evolves)
	public override void SpawnActor(int type, int x, int y)
	{
		// TODO: Implement actor spawning
	}

	public override void DespawnActor(int actorId)
	{
		// TODO: Implement despawning
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
