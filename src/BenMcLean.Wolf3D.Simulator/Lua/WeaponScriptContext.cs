using System;
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
	public override void PlaySound(string soundName)
	{
		soundName = ResolveForPlayback(soundName);
		simulator.EmitGlobalSound(soundName);
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
	public void SetNextState(string stateName) => nextStateOverride = stateName;
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
	#region Noise
	/// <summary>
	/// Propagate noise from weapon fire to alert nearby enemies.
	/// WL_AGENT.C:madenoise equivalent. Called by Lua for noisy weapons (guns, projectiles).
	/// NOT called for silent weapons (knife).
	/// </summary>
	public void PropagateNoise() => simulator.PropagateNoise();
	#endregion Noise
	#region Attack Requests
	/// <summary>
	/// Hitscan attack: raycast via HitDetection callback, applies damage, emits WeaponFired, propagates noise.
	/// WL_AGENT.C:GunAttack equivalent. Sound is played by the Lua script before calling this.
	/// </summary>
	/// <param name="parameters">Attack parameters (range, spread, damage, etc.) — reserved for future data-driven damage</param>
	public void RequestHitScan(object parameters)
	{
		simulator.ExecuteOnWeaponFireScript(slotIndex, weaponInfo.Number, weaponSlot.WeaponType);
		int? hitActorIndex = simulator.HitDetection?.Invoke(slotIndex);
		if (hitActorIndex.HasValue)
			simulator.ApplyWeaponDamage(hitActorIndex.Value, weaponInfo);
		simulator.EmitWeaponFired(new WeaponFiredEvent
		{
			SlotIndex = slotIndex,
			WeaponType = weaponSlot.WeaponType,
			SoundName = string.Empty,
			DidHit = hitActorIndex.HasValue,
			HitActorIndex = hitActorIndex
		});
	}
	/// <summary>
	/// Melee attack: Chebyshev range check across all actors, applies damage to closest, emits WeaponFired.
	/// WL_AGENT.C:KnifeAttack equivalent. No noise propagation (knife is silent).
	/// </summary>
	/// <param name="parameters">Attack parameters (range, arc, damage, etc.) — reserved for future data-driven damage</param>
	public void RequestMelee(object parameters)
	{
		simulator.ExecuteOnWeaponFireScript(slotIndex, weaponInfo.Number, weaponSlot.WeaponType);
		int? hitActorIndex = null;
		int closestDist = int.MaxValue;
		for (int i = 0; i < simulator.Actors.Count; i++)
		{
			Actor actor = simulator.Actors[i];
			if (!actor.Flags.HasFlag(ActorFlags.Shootable)) continue;
			int dx = Math.Abs(simulator.PlayerTileX - actor.TileX);
			int dy = Math.Abs(simulator.PlayerTileY - actor.TileY);
			int chebyshev = Math.Max(dx, dy);
			// WL_AGENT.C:KnifeAttack - range check (0x18000 ≈ 1.5 tiles)
			if (chebyshev > 1) continue;
			if (!simulator.HasLineOfSight(simulator.PlayerTileX, simulator.PlayerTileY, actor.TileX, actor.TileY)) continue;
			if (chebyshev < closestDist)
			{
				closestDist = chebyshev;
				hitActorIndex = i;
			}
		}
		if (hitActorIndex.HasValue)
			simulator.ApplyWeaponDamage(hitActorIndex.Value, weaponInfo);
		simulator.EmitWeaponFired(new WeaponFiredEvent
		{
			SlotIndex = slotIndex,
			WeaponType = weaponSlot.WeaponType,
			SoundName = string.Empty,
			DidHit = hitActorIndex.HasValue,
			HitActorIndex = hitActorIndex
		});
		// No PropagateNoise - knife is silent (WL_AGENT.C:KnifeAttack doesn't set madenoise)
	}
	/// <summary>
	/// Projectile weapon attack: spawns projectile, emits WeaponFired, propagates noise.
	/// WL_AGENT.C:MissileAttack/FlameAttack equivalent. Sound played by Lua script before calling this.
	/// </summary>
	/// <param name="projectileType">Projectile type name (must match a Projectile XML element)</param>
	public void RequestProjectile(string projectileType)
	{
		simulator.ExecuteOnWeaponFireScript(slotIndex, weaponInfo.Number, weaponSlot.WeaponType);
		simulator.SpawnPlayerProjectile(slotIndex, projectileType);
		simulator.EmitWeaponFired(new WeaponFiredEvent
		{
			SlotIndex = slotIndex,
			WeaponType = weaponSlot.WeaponType,
			SoundName = string.Empty,
			DidHit = false,
			HitActorIndex = null
		});
	}
	#endregion Attack Requests
	#region Projectile Spawning
	/// <summary>
	/// Spawn a player-fired projectile from the player's position aimed in the player's direction.
	/// WL_AGENT.C:MissileAttack (watermelon), FlameAttack (cantaloupe).
	/// Ammo should be consumed separately via ConsumeAmmo() before calling this.
	/// The projectile spawns slightly ahead of the player to avoid immediate self-collision.
	/// </summary>
	/// <param name="projectileType">Projectile type name (must match a Projectile XML element)</param>
	public void SpawnProjectile(string projectileType)
	{
		simulator.SpawnPlayerProjectile(slotIndex, projectileType);
	}
	#endregion Projectile Spawning

	#region Weapon Switching
	/// <summary>
	/// Switch to the lowest-numbered weapon (out of ammo fallback).
	/// WL_AGENT.C case -1: gamestate.weapon = wp_knife equivalent.
	/// Data-driven: finds weapon with lowest Number in WeaponCollection.
	/// </summary>
	public void SwitchToLowestWeapon()
	{
		WeaponCollection weaponCollection = simulator.WeaponCollection;
		if (weaponCollection is not null)
		{
			WeaponInfo lowest = null;
			foreach (WeaponInfo weapon in weaponCollection.Weapons.Values)
				if (lowest is null || weapon.Number < lowest.Number)
					lowest = weapon;
			if (lowest is not null)
				simulator.EquipWeapon(slotIndex, lowest.Name);
		}
	}
	#endregion Weapon Switching
}
