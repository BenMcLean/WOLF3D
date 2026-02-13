namespace BenMcLean.Wolf3D.Simulator.State;

/// <summary>
/// Serializable snapshot of a WeaponSlot's mutable state.
/// CurrentState is serialized as a string name, resolved via StateCollection
/// by the Simulator after LoadState() restores value-type fields.
///
/// WeaponSlots are matched by SlotIndex on restore.
///
/// Enums stored as underlying numeric types for format-agnostic safety.
/// </summary>
public record WeaponSlotSnapshot
{
	/// <summary>
	/// Slot index (0 = left hand VR / primary traditional, 1 = right hand VR, etc.)
	/// </summary>
	public int SlotIndex { get; init; }

	/// <summary>
	/// Currently equipped weapon type (e.g., "knife", "pistol").
	/// Null if slot is empty.
	/// </summary>
	public string WeaponType { get; init; }

	/// <summary>
	/// Name of the current state in the weapon's state machine.
	/// Resolved to a State object reference via StateCollection on restore.
	/// Null if slot is empty.
	/// </summary>
	public string CurrentStateName { get; init; }

	/// <summary>
	/// WL_AGENT.C:attackcount (original: int = 16-bit signed)
	/// Tics remaining in current state.
	/// </summary>
	public short TicCount { get; init; }

	/// <summary>
	/// Current sprite/shape number. -1 = no sprite.
	/// </summary>
	public short ShapeNum { get; init; }

	/// <summary>
	/// WL_AGENT.C:attackframe (original: int = 16-bit signed)
	/// Current frame index in attack sequence.
	/// </summary>
	public short AttackFrame { get; init; }

	/// <summary>
	/// Weapon slot flags as int (WeaponSlotFlags enum underlying type).
	/// Bitfield: Ready, TriggerHeld, Attacking.
	/// </summary>
	public int Flags { get; init; }
}
