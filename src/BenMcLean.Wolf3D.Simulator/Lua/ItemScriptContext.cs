using Microsoft.Extensions.Logging;
using BenMcLean.Wolf3D.Simulator.Entities;

namespace BenMcLean.Wolf3D.Simulator.Lua;

/// <summary>
/// Script context for item pickup scripts.
/// Extends EntityScriptContext with item-specific API for conditional pickups.
/// Item scripts return true to consume the item, false to leave it.
///
/// Example Lua script for health pickup:
/// <code>
/// if GetPlayerHealth() &lt; GetPlayerMaxHealth() then
///     HealPlayer(10)
///     PlayLocalDigiSound("HEALTH1SND")
///     return true
/// end
/// return false
/// </code>
/// </summary>
public class ItemScriptContext : EntityScriptContext
{
	private readonly StatObj item;
	private readonly int itemIndex;

	/// <summary>
	/// Tile X coordinate of the item (exposed to Lua).
	/// </summary>
	public int ItemTileX => item.TileX;

	/// <summary>
	/// Tile Y coordinate of the item (exposed to Lua).
	/// </summary>
	public int ItemTileY => item.TileY;

	/// <summary>
	/// Item number/type (e.g., bo_food, bo_clip - exposed to Lua).
	/// </summary>
	public int ItemNumber => item.ItemNumber;

	/// <summary>
	/// Shape number of the item sprite (exposed to Lua).
	/// </summary>
	public int ItemShape => item.ShapeNum;

	public ItemScriptContext(
		Simulator simulator,
		StatObj item,
		int itemIndex,
		RNG rng,
		GameClock gameClock,
		ILogger logger = null)
		: base(simulator, rng, gameClock, item.TileX, item.TileY, logger)
	{
		this.item = item;
		this.itemIndex = itemIndex;
	}

	#region Player State Query API

	/// <summary>
	/// Get the player's current health.
	/// </summary>
	public override int GetPlayerHealth() => simulator.GetPlayerHealth();

	/// <summary>
	/// Get the player's maximum health.
	/// </summary>
	public override int GetPlayerMaxHealth() => simulator.GetPlayerMaxHealth();

	/// <summary>
	/// Get the player's current ammo for a specific weapon/ammo type.
	/// </summary>
	/// <param name="ammoType">Ammo type name (e.g., "bullets")</param>
	public int GetPlayerAmmo(string ammoType) => simulator.GetAmmo(ammoType);

	/// <summary>
	/// Get the player's maximum ammo capacity for a specific type.
	/// </summary>
	/// <param name="ammoType">Ammo type name (e.g., "bullets")</param>
	public int GetPlayerMaxAmmo(string ammoType) => simulator.GetMaxAmmo(ammoType);

	/// <summary>
	/// Check if the player has a specific key.
	/// </summary>
	/// <param name="keyType">Key identifier (e.g., "gold", "silver")</param>
	public override bool PlayerHasKey(int keyType) => simulator.PlayerHasKey(keyType);

	/// <summary>
	/// Get the player's current score.
	/// </summary>
	public int GetPlayerScore() => simulator.GetPlayerScore();

	/// <summary>
	/// Get the player's current lives.
	/// </summary>
	public int GetPlayerLives() => simulator.GetPlayerLives();

	/// <summary>
	/// Check if the player has a specific weapon.
	/// </summary>
	/// <param name="weaponType">Weapon type name (e.g., "machinegun", "chaingun")</param>
	public bool PlayerHasWeapon(string weaponType) => simulator.PlayerHasWeapon(weaponType);

	#endregion

	#region Player State Modification API

	/// <summary>
	/// Heal the player by a specified amount (capped at max health).
	/// </summary>
	/// <param name="amount">Amount of health to restore</param>
	public override void HealPlayer(int amount) => simulator.HealPlayer(amount);

	/// <summary>
	/// Damage the player by a specified amount.
	/// </summary>
	/// <param name="amount">Amount of damage to deal</param>
	public override void DamagePlayer(int amount) => simulator.DamagePlayer(amount);

	/// <summary>
	/// Give ammo to the player (capped at max ammo).
	/// </summary>
	/// <param name="ammoType">Ammo type name (e.g., "bullets")</param>
	/// <param name="amount">Amount of ammo to give</param>
	public void GiveAmmo(string ammoType, int amount) => simulator.GiveAmmo(ammoType, amount);

	/// <summary>
	/// Give ammo to the player using weapon type index (legacy API).
	/// </summary>
	public override void GivePlayerAmmo(int weaponType, int amount) =>
		simulator.GiveAmmo("bullets", amount);

	/// <summary>
	/// Give a key to the player.
	/// </summary>
	/// <param name="keyType">Key identifier (e.g., 0=gold, 1=silver)</param>
	public override void GivePlayerKey(int keyType) => simulator.GiveKey(keyType);

	/// <summary>
	/// Give a weapon to the player.
	/// </summary>
	/// <param name="weaponType">Weapon type name (e.g., "machinegun", "chaingun")</param>
	public void GiveWeapon(string weaponType) => simulator.GiveWeapon(weaponType);

	/// <summary>
	/// Add to the player's score.
	/// </summary>
	/// <param name="points">Points to add</param>
	public void GiveScore(int points) => simulator.AddScore(points);

	/// <summary>
	/// Give an extra life to the player.
	/// </summary>
	public void GiveExtraLife() => simulator.GiveExtraLife();

	#endregion

	#region Unused Stubs (Actor-specific API not used by items)

	/// <summary>
	/// Not used for items - provided for interface compatibility.
	/// </summary>
	public override void SpawnActor(int type, int x, int y) { }

	/// <summary>
	/// Not used for items - provided for interface compatibility.
	/// </summary>
	public override void DespawnActor(int actorId) { }

	#endregion
}
