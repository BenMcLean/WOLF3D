using BenMcLean.Wolf3D.Assets.Gameplay;
using BenMcLean.Wolf3D.Simulator.Entities;
using Microsoft.Extensions.Logging;

namespace BenMcLean.Wolf3D.Simulator.Lua;

/// <summary>
/// Script context for weapon Action functions.
/// Provides API for weapon scripts to play sounds, consume ammo, check button state, and control state flow.
/// Used when executing weapon state Action scripts (WL_AGENT.C:T_Attack equivalent).
/// </summary>
public class WeaponScriptContext(
	Simulator simulator,
	WeaponSlot weaponSlot,
	int slotIndex,
	WeaponInfo weaponInfo,
	RNG rng,
	GameClock gameClock,
	ILogger logger = null) : ActionScriptContext(simulator, rng, gameClock, logger)
{
	private string nextStateOverride = null; // For rapid fire looping
	#region Logging
	/// <summary>
	/// Log a debug message from Lua script.
	/// Routes to Microsoft.Extensions.Logging.
	/// </summary>
	public void Log(string message) => _logger?.LogDebug("Lua Weapon: {message}", message);
	#endregion Logging
	#region Weapon Property Accessors
	/// <summary>
	/// Get weapon type identifier (e.g., "knife", "pistol", "machinegun", "chaingun").
	/// </summary>
	public string GetWeaponType() => weaponSlot.WeaponType;
	/// <summary>
	/// Get weapon slot index (0 = primary/left, 1 = secondary/right).
	/// </summary>
	public int GetSlotIndex() => slotIndex;
	/// <summary>
	/// Get current state name.
	/// Used by A_RapidFire to determine which weapon is firing.
	/// </summary>
	public string GetCurrentStateName() => weaponSlot.CurrentState?.Name;
	/// <summary>
	/// Get weapon property from WeaponInfo.
	/// Generic accessor for any weapon property defined in XML.
	/// </summary>
	/// <param name="propertyName">Property name (e.g., "BaseDamage", "MaxRange")</param>
	/// <returns>Property value as string, or null if not found</returns>
	public string GetWeaponProperty(string propertyName) =>
		// Use reflection to get property value from weaponInfo
		typeof(WeaponInfo).GetProperty(propertyName)
			?.GetValue(weaponInfo)
			?.ToString();
	#endregion Weapon Property Accessors
	#region Sound Playback
	/// <summary>
	/// Play a weapon sound effect globally (non-positional).
	/// WL_AGENT.C:SD_PlaySound equivalent.
	/// Sounds like ATKKNIFESND, ATKPISTOLSND play in player's "headphones".
	/// </summary>
	/// <param name="soundName">Sound name (e.g., "ATKKNIFESND", "ATKPISTOLSND")</param>
	public void PlaySound(string soundName)
	{
		simulator.EmitGlobalSound(soundName);
		_logger?.LogDebug("WeaponScriptContext: PlaySound({soundName}) for slot {slotIndex}",
			soundName, slotIndex);
	}
	/// <summary>
	/// Play the weapon's configured fire sound.
	/// Convenience method that uses WeaponInfo.FireSound.
	/// </summary>
	public void PlayWeaponSound()
	{
		if (!string.IsNullOrEmpty(weaponInfo.FireSound))
			PlaySound(weaponInfo.FireSound);
	}
	#endregion Sound Playback
	#region Ammo Management
	/// <summary>
	/// Check if player has enough ammo for this weapon.
	/// WL_AGENT.C ammo check equivalent.
	/// Uses generic inventory API: GetValue("Ammo")
	/// </summary>
	/// <param name="amount">Amount of ammo required (default: weapon's AmmoPerShot)</param>
	/// <returns>True if enough ammo available (or weapon doesn't require ammo)</returns>
	public bool HasAmmo(int? amount = null)
	{
		int required = amount ?? weaponInfo.AmmoPerShot;
		// Weapons that don't require ammo always return true
		return required <= 0 ||
			string.IsNullOrEmpty(weaponInfo.AmmoType) ||
			GetValue("Ammo") >= required;
	}
	/// <summary>
	/// Consume ammo for this weapon.
	/// WL_AGENT.C:gamestate.ammo-- equivalent.
	/// Uses generic inventory API: AddValue("Ammo", -amount)
	/// </summary>
	/// <param name="amount">Amount of ammo to consume (default: weapon's AmmoPerShot)</param>
	public void ConsumeAmmo(int? amount = null)
	{
		int toConsume = amount ?? weaponInfo.AmmoPerShot;
		if (toConsume <= 0 || string.IsNullOrEmpty(weaponInfo.AmmoType))
			return;
		AddValue("Ammo", -toConsume);
		_logger?.LogDebug("WeaponScriptContext: ConsumeAmmo({amount}) for {weaponType}, remaining: {remaining}",
			toConsume, weaponSlot.WeaponType, GetValue("Ammo"));
	}
	/// <summary>
	/// Get current ammo count for this weapon's ammo type.
	/// Uses generic inventory API: GetValue("Ammo")
	/// </summary>
	/// <returns>Current ammo count (0 if weapon doesn't use ammo)</returns>
	public int GetAmmoCount() => string.IsNullOrEmpty(weaponInfo.AmmoType) ? 0 : GetValue("Ammo");
	#endregion Ammo Management
	#region Input State
	/// <summary>
	/// Check if attack button is currently held down.
	/// WL_AGENT.C:buttonheld[bt_attack] equivalent.
	/// Used for rapid fire weapons to check if player is still holding trigger.
	/// </summary>
	/// <param name="buttonName">Button name ("attack" for fire button)</param>
	/// <returns>True if button is held</returns>
	public bool IsButtonHeld(string buttonName)
	{
		// For now, only support "attack" button
		if (buttonName == "attack")
		{
			return weaponSlot.Flags.HasFlag(WeaponSlotFlags.TriggerHeld);
		}
		return false;
	}
	#endregion Input State
	#region State Flow Control
	/// <summary>
	/// Override the next state for this weapon slot.
	/// Used by rapid fire weapons to loop back to fire frame.
	/// WL_AGENT.C cases 3 & 4 (machine gun, chain gun) equivalent.
	/// </summary>
	/// <param name="stateName">State name to transition to (e.g., "s_machinegun_1")</param>
	public void SetNextState(string stateName)
	{
		nextStateOverride = stateName;
		_logger?.LogDebug("WeaponScriptContext: SetNextState({stateName}) for slot {slotIndex}",
			stateName, slotIndex);
	}
	/// <summary>
	/// Get the overridden next state (if any).
	/// Called by Simulator after script execution to check if state flow was changed.
	/// </summary>
	/// <returns>Next state name, or null if no override</returns>
	public string GetNextStateOverride() => nextStateOverride;
	/// <summary>
	/// Clear the next state override.
	/// Called by Simulator after processing the override.
	/// </summary>
	public void ClearNextStateOverride() => nextStateOverride = null;
	#endregion State Flow Control
	#region Attack Requests
	/// <summary>
	/// Request a hitscan attack (raycast).
	/// Presentation layer will perform raycasting and return hit results to simulator.
	/// WL_AGENT.C:GunAttack equivalent.
	/// </summary>
	/// <param name="parameters">Attack parameters (range, spread, damage, etc.)</param>
	public void RequestHitScan(object parameters)
	{
		// TODO: Parse parameters table and emit attack request event
		// For now, log the request
		_logger?.LogDebug("WeaponScriptContext: RequestHitScan() for {weaponType} (not yet implemented)",
			weaponSlot.WeaponType);
	}
	/// <summary>
	/// Request a melee attack (close range).
	/// Presentation layer will check for nearby enemies and return hit results.
	/// WL_AGENT.C:KnifeAttack equivalent.
	/// </summary>
	/// <param name="parameters">Attack parameters (range, arc, damage, etc.)</param>
	public void RequestMelee(object parameters)
	{
		// TODO: Parse parameters table and emit attack request event
		// For now, log the request
		_logger?.LogDebug("WeaponScriptContext: RequestMelee() for {weaponType} (not yet implemented)",
			weaponSlot.WeaponType);
	}
	#endregion Attack Requests
	#region Weapon Switching
	/// <summary>
	/// Switch to the lowest-numbered weapon (out of ammo fallback).
	/// WL_AGENT.C case -1: gamestate.weapon = wp_knife equivalent.
	/// Data-driven: finds weapon with lowest Number in WeaponCollection.
	/// </summary>
	public void SwitchToLowestWeapon()
	{
		WeaponCollection weaponCollection = simulator.WeaponCollection;
		if (weaponCollection != null)
		{
			WeaponInfo lowest = null;
			foreach (WeaponInfo weapon in weaponCollection.Weapons.Values)
			{
				if (lowest == null || weapon.Number < lowest.Number)
					lowest = weapon;
			}
			if (lowest != null)
			{
				simulator.EquipWeapon(slotIndex, lowest.Name);
				_logger?.LogDebug("WeaponScriptContext: SwitchToLowestWeapon() -> {weaponName} for slot {slotIndex}",
					lowest.Name, slotIndex);
			}
		}
	}
	#endregion Weapon Switching
}
