using Microsoft.Extensions.Logging;

namespace BenMcLean.Wolf3D.Simulator.Lua;

/// <summary>
/// Script context for the action stage (gameplay).
/// Extends BaseScriptContext with deterministic RNG/GameClock and generic inventory API.
/// All player state (health, ammo, keys, weapons, score, lives) is accessed via the Inventory.
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

	#region Generic Inventory API (exposed to Lua)

	/// <summary>
	/// Get the value of any inventory item.
	/// Examples: GetValue("Health"), GetValue("Ammo"), GetValue("Gold Key"), GetValue("Weapon2")
	/// </summary>
	/// <param name="name">The inventory item name</param>
	/// <returns>Current value, or 0 if not found</returns>
	public int GetValue(string name) => simulator.Inventory.GetValue(name);

	/// <summary>
	/// Set the value of any inventory item.
	/// Value is clamped to [0, Max] if a maximum is defined.
	/// Examples: SetValue("Health", 100), SetValue("Gold Key", 1)
	/// </summary>
	/// <param name="name">The inventory item name</param>
	/// <param name="value">The new value</param>
	public void SetValue(string name, int value) => simulator.Inventory.SetValue(name, value);

	/// <summary>
	/// Add to any inventory item value.
	/// Examples: AddValue("Health", 10), AddValue("Ammo", -1), AddValue("Score", 500)
	/// </summary>
	/// <param name="name">The inventory item name</param>
	/// <param name="delta">Amount to add (can be negative)</param>
	public void AddValue(string name, int delta) => simulator.Inventory.AddValue(name, delta);

	/// <summary>
	/// Get the maximum value for any inventory item.
	/// Examples: GetMax("Health") returns 100, GetMax("Ammo") returns 99
	/// </summary>
	/// <param name="name">The inventory item name</param>
	/// <returns>Maximum value, or int.MaxValue if no max defined</returns>
	public int GetMax(string name) => simulator.Inventory.GetMax(name);

	/// <summary>
	/// Check if player has an inventory item (value > 0).
	/// Examples: Has("Gold Key"), Has("Weapon2"), Has("Ammo")
	/// </summary>
	/// <param name="name">The inventory item name</param>
	/// <returns>True if value > 0</returns>
	public bool Has(string name) => simulator.Inventory.Has(name);

	#endregion

	#region Actor API (to be implemented by derived classes)

	public abstract void SpawnActor(int type, int x, int y);
	public abstract void DespawnActor(int actorId);

	#endregion
}

/// <summary>
/// Script context for bonus objects (pickups).
/// Extends EntityScriptContext with bonus-specific API.
/// Inherits PlayLocalDigiSound for positional audio at bonus location.
/// Uses generic inventory API from ActionScriptContext.
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

	// Actor API stubs (not used by bonuses)
	public override void SpawnActor(int type, int x, int y) { }
	public override void DespawnActor(int actorId) { }
}
