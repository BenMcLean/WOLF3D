using Microsoft.Extensions.Logging;
using BenMcLean.Wolf3D.Simulator.Entities;

namespace BenMcLean.Wolf3D.Simulator.Lua;

/// <summary>
/// Script context for bonus (item pickup) scripts.
/// Extends EntityScriptContext with bonus-specific properties.
/// Bonus scripts return true to consume the bonus, false to leave it.
///
/// Uses generic inventory API inherited from ActionScriptContext:
/// - GetValue(name), SetValue(name, value), AddValue(name, delta)
/// - GetMax(name), Has(name)
///
/// Example Lua script for health pickup:
/// <code>
/// if GetValue("Health") &lt; GetMax("Health") then
///     AddValue("Health", 10)
///     PlayLocalDigiSound("HEALTH1SND")
///     return true
/// end
/// return false
/// </code>
///
/// Example Lua script for key pickup:
/// <code>
/// SetValue("Gold Key", 1)
/// PlayLocalDigiSound("GETKEYSND")
/// return true
/// </code>
///
/// Example Lua script for weapon pickup:
/// <code>
/// local isNew = not Has("Weapon2")
/// SetValue("Weapon2", 1)
/// AddValue("Ammo", 6)
/// if isNew then SwitchToWeapon("machinegun") end
/// PlayAdLibSound("GETMACHINESND")
/// FlashScreen(0xFFF800)
/// return true
/// </code>
/// </summary>
public class BonusScriptContext(
	Simulator simulator,
	StatObj item,
	RNG rng,
	GameClock gameClock,
	ILogger logger = null) : EntityScriptContext(simulator, rng, gameClock, item.TileX, item.TileY, logger)
{
	/// <summary>
	/// Tile X coordinate of the bonus (exposed to Lua).
	/// </summary>
	public int BonusTileX => item.TileX;
	/// <summary>
	/// Tile Y coordinate of the bonus (exposed to Lua).
	/// </summary>
	public int BonusTileY => item.TileY;
	/// <summary>
	/// Item number/type (e.g., bo_food, bo_clip - exposed to Lua).
	/// WL_DEF.H:bo_* constants
	/// </summary>
	public int BonusNumber => item.ItemNumber;
	/// <summary>
	/// Shape number of the bonus sprite (exposed to Lua).
	/// </summary>
	public int BonusShape => item.ShapeNum;
	#region Weapon Switching
	/// <summary>
	/// Switch to the named weapon in all weapon slots.
	/// Used when the player first picks up a new weapon.
	/// Equips the weapon in all slots without specifying a particular slot.
	/// Requires the weapon to already be in inventory (SetValue must be called first).
	/// </summary>
	/// <param name="weaponName">Weapon name (e.g., "machinegun", "chaingun")</param>
	public void SwitchToWeapon(string weaponName)
	{
		for (int i = 0; i < simulator.WeaponSlots.Count; i++)
			simulator.EquipWeapon(i, weaponName);
	}
	#endregion Weapon Switching
}
