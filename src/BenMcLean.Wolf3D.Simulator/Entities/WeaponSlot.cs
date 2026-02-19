using System;
using BenMcLean.Wolf3D.Simulator.State;
using GameplayState = BenMcLean.Wolf3D.Assets.Gameplay.State;

namespace BenMcLean.Wolf3D.Simulator.Entities;

/// <summary>
/// Weapon slot state in the simulation. Mutable.
/// Based on original Wolf3D player weapon tracking (WL_DEF.H:gametype:weapon/attackframe/attackcount)
/// and WL_AGENT.C:attackinfo structure.
/// Each slot runs an independent state machine for weapon animations.
/// </summary>
/// <remarks>
/// Creates a new weapon slot.
/// Slot starts empty (no weapon equipped).
/// </remarks>
/// <param name="slotIndex">Slot identifier (0-based index)</param>
public class WeaponSlot(int slotIndex) : IStateSavable<WeaponSlotSnapshot>
{
	/// <summary>
	/// Slot index (0 = left hand VR / primary traditional, 1 = right hand VR, etc.)
	/// Identifies which slot this is for multi-weapon configurations.
	/// </summary>
	public int SlotIndex { get; } = slotIndex;

	/// <summary>
	/// Currently equipped weapon type identifier (e.g., "knife", "pistol", "chaingun").
	/// Null if slot is empty or weapon is holstered.
	/// Maps to WeaponInfo.Name in WeaponCollection.
	/// </summary>
	public string WeaponType { get; set; } = null;

	/// <summary>
	/// Current state in the weapon's state machine.
	/// Based on WL_DEF.H:objstruct:state pattern (same as actors).
	/// Determines sprite, duration, and behavior functions.
	/// </summary>
	public GameplayState CurrentState { get; set; } = null;

	/// <summary>
	/// WL_AGENT.C:attackcount (original: int = 16-bit signed)
	/// Number of tics remaining in the current state.
	/// When this reaches 0, transition to CurrentState.Next.
	/// Decremented each simulation tic (70Hz).
	/// </summary>
	public short TicCount { get; set; } = 0;

	/// <summary>
	/// Current sprite/shape number being displayed.
	/// Derived from CurrentState.Shape.
	/// -1 indicates no sprite (slot empty or weapon holstered).
	/// Based on WL_AGENT.C:weaponframe (original: int)
	/// </summary>
	public short ShapeNum { get; set; } = -1;

	/// <summary>
	/// WL_AGENT.C:attackframe (original: int = 16-bit signed)
	/// Current frame index in attack sequence (for tracking multi-frame attacks).
	/// Used to coordinate animation timing with damage application.
	/// Incremented as weapon progresses through fire animation states.
	/// </summary>
	public short AttackFrame { get; set; } = 0;

	/// <summary>
	/// Weapon slot state flags (ready to fire, trigger held, attacking, etc.)
	/// Used for fire mode logic (semi-auto vs full-auto).
	/// </summary>
	public WeaponSlotFlags Flags { get; set; } = WeaponSlotFlags.None;

	/// <summary>
	/// Captures all mutable weapon slot state. CurrentState is stored as its Name string;
	/// the Simulator resolves it back to a State reference via StateCollection after
	/// calling LoadState() on all weapon slots.
	/// </summary>
	public WeaponSlotSnapshot SaveState() => new()
	{
		SlotIndex = SlotIndex,
		WeaponType = WeaponType,
		CurrentStateName = CurrentState?.Name,
		TicCount = TicCount,
		ShapeNum = ShapeNum,
		AttackFrame = AttackFrame,
		Flags = (int)Flags
	};

	/// <summary>
	/// Restores all value-type fields from a snapshot.
	/// CurrentState is NOT restored here - it requires resolution via StateCollection,
	/// which is done by Simulator.LoadState() after calling this method.
	/// </summary>
	public void LoadState(WeaponSlotSnapshot state)
	{
		WeaponType = state.WeaponType;
		// CurrentState resolved separately by Simulator via StateCollection
		TicCount = state.TicCount;
		ShapeNum = state.ShapeNum;
		AttackFrame = state.AttackFrame;
		Flags = (WeaponSlotFlags)state.Flags;
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
