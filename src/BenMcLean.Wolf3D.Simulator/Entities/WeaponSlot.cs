using System;
using BenMcLean.Wolf3D.Assets.Gameplay;

namespace BenMcLean.Wolf3D.Simulator.Entities;

/// <summary>
/// Weapon slot state in the simulation. Mutable.
/// Based on original Wolf3D player weapon tracking (WL_DEF.H:gametype:weapon/attackframe/attackcount)
/// and WL_AGENT.C:attackinfo structure.
/// Each slot runs an independent state machine for weapon animations.
/// </summary>
public class WeaponSlot
{
	/// <summary>
	/// Slot index (0 = left hand VR / primary traditional, 1 = right hand VR, etc.)
	/// Identifies which slot this is for multi-weapon configurations.
	/// </summary>
	public int SlotIndex { get; }

	/// <summary>
	/// Currently equipped weapon type identifier (e.g., "knife", "pistol", "chaingun").
	/// Null if slot is empty or weapon is holstered.
	/// Maps to WeaponInfo.Name in WeaponCollection.
	/// </summary>
	public string WeaponType { get; set; }

	/// <summary>
	/// Current state in the weapon's state machine.
	/// Based on WL_DEF.H:objstruct:state pattern (same as actors).
	/// Determines sprite, duration, and behavior functions.
	/// </summary>
	public State CurrentState { get; set; }

	/// <summary>
	/// WL_AGENT.C:attackcount (original: int = 16-bit signed)
	/// Number of tics remaining in the current state.
	/// When this reaches 0, transition to CurrentState.Next.
	/// Decremented each simulation tic (70Hz).
	/// </summary>
	public short TicCount { get; set; }

	/// <summary>
	/// Current sprite/shape number being displayed.
	/// Derived from CurrentState.Shape.
	/// -1 indicates no sprite (slot empty or weapon holstered).
	/// Based on WL_AGENT.C:weaponframe (original: int)
	/// </summary>
	public short ShapeNum { get; set; }

	/// <summary>
	/// WL_AGENT.C:attackframe (original: int = 16-bit signed)
	/// Current frame index in attack sequence (for tracking multi-frame attacks).
	/// Used to coordinate animation timing with damage application.
	/// Incremented as weapon progresses through fire animation states.
	/// </summary>
	public short AttackFrame { get; set; }

	/// <summary>
	/// Weapon slot state flags (ready to fire, trigger held, attacking, etc.)
	/// Used for fire mode logic (semi-auto vs full-auto).
	/// </summary>
	public WeaponSlotFlags Flags { get; set; }

	/// <summary>
	/// Creates a new weapon slot.
	/// Slot starts empty (no weapon equipped).
	/// </summary>
	/// <param name="slotIndex">Slot identifier (0-based index)</param>
	public WeaponSlot(int slotIndex)
	{
		SlotIndex = slotIndex;
		WeaponType = null;
		CurrentState = null;
		TicCount = 0;
		ShapeNum = -1;
		AttackFrame = 0;
		Flags = WeaponSlotFlags.None;
	}
}

/// <summary>
/// Weapon slot state flags.
/// Based on WL_AGENT.C:buttonheld[] tracking and attack state logic.
/// </summary>
[Flags]
public enum WeaponSlotFlags : int
{
	None = 0,
	/// <summary>
	/// Ready to fire (weapon in idle state, not currently attacking).
	/// Based on WL_AGENT.C:T_Attack completion logic.
	/// </summary>
	Ready = 1 << 0,
	/// <summary>
	/// Trigger is currently held down.
	/// Used for semi-auto fire mode to prevent re-fire until trigger is released.
	/// Based on WL_AGENT.C:buttonheld[bt_attack] (line 2266-2268).
	/// </summary>
	TriggerHeld = 1 << 1,
	/// <summary>
	/// Currently playing attack animation (in fire state sequence).
	/// Set when transitioning to fire state, cleared when returning to idle.
	/// </summary>
	Attacking = 1 << 2,
}
