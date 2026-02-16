using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace BenMcLean.Wolf3D.Assets.Gameplay;

/// <summary>
/// Defines a weapon type and its properties.
/// Based on WL_AGENT.C:attackinfo[] structure but XML-driven for moddability.
/// Each weapon type has a unique identifier, states, damage, and ammo properties.
/// </summary>
public class WeaponInfo
{
	/// <summary>
	/// Unique weapon identifier (e.g., "knife", "pistol", "machinegun", "chaingun").
	/// Used to reference this weapon from inventory, equip actions, etc.
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// Weapon number for ordering and keyboard mapping.
	/// WL_DEF.H: wp_knife=0, wp_pistol=1, wp_machinegun=2, wp_chaingun=3
	/// Parsed from WeaponNumber attribute in XML.
	/// </summary>
	public int Number { get; set; }

	/// <summary>
	/// Initial/idle state name (e.g., "s_knife_idle", "s_pistol_ready").
	/// The weapon stays in this state when not attacking.
	/// Based on WL_AGENT.C:s_player state pattern.
	/// </summary>
	public string IdleState { get; set; }

	/// <summary>
	/// Fire/attack state name (e.g., "s_knife_attack1", "s_pistol_fire1").
	/// The weapon transitions to this state when fired.
	/// Based on WL_AGENT.C:s_attack state pattern.
	/// </summary>
	public string FireState { get; set; }

	/// <summary>
	/// Base damage per hit (can be modified by scripts or difficulty).
	/// Based on WL_AGENT.C damage calculations:
	/// - Knife: ~10 damage (close range melee)
	/// - Pistol: ~5 damage per shot
	/// - Machinegun/Chaingun: ~5 damage per shot (high rate of fire)
	/// </summary>
	public short BaseDamage { get; set; }

	/// <summary>
	/// Ammo consumed per shot.
	/// 0 for melee weapons like knife (unlimited use).
	/// 1 for standard guns (one bullet per shot).
	/// Based on WL_AGENT.C ammo tracking.
	/// </summary>
	public short AmmoPerShot { get; set; }

	/// <summary>
	/// Ammo type required (e.g., "bullets", "shells", null for melee).
	/// Maps to ammo inventory keys.
	/// Allows for multiple ammo types in mods.
	/// </summary>
	public string AmmoType { get; set; }

	/// <summary>
	/// Whether the weapon fires continuously while the trigger is held.
	/// Based on WL_AGENT.C:T_Attack cases 3 &amp; 4 (machine gun/chain gun loop-back).
	/// false: Must release trigger between shots (knife, pistol)
	/// true: Continues firing while trigger held (machine gun, chain gun)
	/// Parsed from RapidFire attribute in XML.
	/// </summary>
	public bool RapidFire { get; set; }

	/// <summary>
	/// Sound to play when firing (e.g., "ATKKNIFESND", "ATKPISTOLSND", "ATKGATGUNSND").
	/// Based on WL_AGENT.C:PlaySoundLocActor calls during attacks.
	/// </summary>
	public string FireSound { get; set; }

	/// <summary>
	/// Creates a WeaponInfo instance from an XML element.
	/// </summary>
	/// <param name="element">The XML element containing weapon data</param>
	/// <returns>A new WeaponInfo instance</returns>
	/// <exception cref="ArgumentException">If required attributes are missing</exception>
	public static WeaponInfo FromXElement(XElement element)
	{
		return new WeaponInfo
		{
			Name = element.Attribute("Name")?.Value ?? throw new ArgumentException("Weapon element must have Name attribute"),
			Number = int.TryParse(element.Attribute("WeaponNumber")?.Value, out int num) ? num : 0,
			IdleState = element.Attribute("IdleState")?.Value ?? throw new ArgumentException("Weapon element must have IdleState attribute"),
			FireState = element.Attribute("FireState")?.Value ?? throw new ArgumentException("Weapon element must have FireState attribute"),
			BaseDamage = short.TryParse(element.Attribute("BaseDamage")?.Value, out short dmg) ? dmg : (short)0,
			AmmoPerShot = short.TryParse(element.Attribute("AmmoPerShot")?.Value, out short ammo) ? ammo : (short)0,
			AmmoType = element.Attribute("AmmoType")?.Value,
			RapidFire = string.Equals(element.Attribute("RapidFire")?.Value, "true", StringComparison.OrdinalIgnoreCase),
			FireSound = element.Attribute("FireSound")?.Value
		};
	}
}

/// <summary>
/// Collection of weapon definitions loaded from XML.
/// Provides lookup by weapon name for equip/fire operations.
/// </summary>
public class WeaponCollection
{
	/// <summary>
	/// All weapons, indexed by name for fast lookup.
	/// </summary>
	public Dictionary<string, WeaponInfo> Weapons { get; } = new();

	/// <summary>
	/// All weapons, indexed by weapon number for keyboard mapping and status bar.
	/// </summary>
	private Dictionary<int, WeaponInfo> weaponsByNumber = new();

	/// <summary>
	/// Loads weapon definitions from XML elements.
	/// Typically called during game initialization from &lt;Weapons&gt; section.
	/// </summary>
	/// <param name="weaponElements">Collection of &lt;Weapon&gt; XML elements</param>
	public void LoadFromXml(IEnumerable<XElement> weaponElements)
	{
		foreach (XElement element in weaponElements)
		{
			WeaponInfo weapon = WeaponInfo.FromXElement(element);
			Weapons[weapon.Name] = weapon;
			weaponsByNumber[weapon.Number] = weapon;
		}
	}

	/// <summary>
	/// Attempts to get weapon info by name.
	/// </summary>
	/// <param name="weaponName">The weapon identifier (e.g., "pistol")</param>
	/// <param name="weaponInfo">The weapon info if found</param>
	/// <returns>True if weapon exists, false otherwise</returns>
	public bool TryGetWeapon(string weaponName, out WeaponInfo weaponInfo)
	{
		return Weapons.TryGetValue(weaponName, out weaponInfo);
	}

	/// <summary>
	/// Attempts to get weapon info by weapon number.
	/// Used for keyboard mapping (Key1→weapon 0, Key2→weapon 1, etc.).
	/// </summary>
	/// <param name="number">The weapon number (e.g., 0 for knife, 1 for pistol)</param>
	/// <param name="weaponInfo">The weapon info if found</param>
	/// <returns>True if weapon exists with that number, false otherwise</returns>
	public bool TryGetWeaponByNumber(int number, out WeaponInfo weaponInfo)
	{
		return weaponsByNumber.TryGetValue(number, out weaponInfo);
	}

	/// <summary>
	/// Gets the inventory key for a weapon number.
	/// Centralizes the key format used for weapon ownership tracking.
	/// </summary>
	/// <param name="weaponNumber">The weapon number</param>
	/// <returns>Inventory key string (e.g., "Weapon0", "Weapon1")</returns>
	public static string GetInventoryKey(int weaponNumber) => $"Weapon{weaponNumber}";
}
