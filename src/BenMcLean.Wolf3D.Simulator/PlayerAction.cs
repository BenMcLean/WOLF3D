using BenMcLean.Wolf3D.Assets.Gameplay;

namespace BenMcLean.Wolf3D.Simulator;

/// <summary>
/// Base class for player actions that can be queued.
/// </summary>
public abstract class PlayerAction
{
}

/// <summary>
/// Player attempts to operate (open/close) a door.
/// </summary>
public class OperateDoorAction : PlayerAction
{
	public required ushort DoorIndex { get; init; }
}

/// <summary>
/// Player attempts to push a pushwall.
/// Based on WL_ACT1.C pushwall activation logic.
/// </summary>
public class ActivatePushWallAction : PlayerAction
{
	/// <summary>Tile X coordinate of the wall being pushed</summary>
	public required ushort TileX { get; init; }
	/// <summary>Tile Y coordinate of the wall being pushed</summary>
	public required ushort TileY { get; init; }
	/// <summary>Direction the pushwall should move (away from player)</summary>
	public required Direction Direction { get; init; }
}

/// <summary>
/// Player attempts to activate an elevator switch.
/// Based on WL_AGENT.C:Cmd_Use elevator activation logic (line 1767).
/// </summary>
public class ActivateElevatorAction : PlayerAction
{
	/// <summary>Tile X coordinate of the elevator switch being activated</summary>
	public required ushort TileX { get; init; }
	/// <summary>Tile Y coordinate of the elevator switch being activated</summary>
	public required ushort TileY { get; init; }
	/// <summary>Direction the player is facing (determines which face is being activated)</summary>
	public required Direction Direction { get; init; }
}

/// <summary>
/// Player attempts to fire a weapon from a specific slot.
/// Hit detection is handled by the weapon state machine via HitDetection callback.
/// Based on WL_AGENT.C:Cmd_Fire (line 1629).
/// </summary>
public class FireWeaponAction : PlayerAction
{
	/// <summary>
	/// Weapon slot index (0 = left hand VR / primary traditional, 1 = right hand VR / secondary).
	/// Identifies which weapon slot is firing.
	/// </summary>
	public required int SlotIndex { get; init; }
}

/// <summary>
/// Player releases weapon trigger (for semi-auto fire mode).
/// Based on WL_AGENT.C:buttonheld[] tracking (line 2266-2268).
/// Required to re-enable firing for semi-automatic weapons (pistol, knife).
/// </summary>
public class ReleaseWeaponTriggerAction : PlayerAction
{
	/// <summary>Weapon slot index</summary>
	public required int SlotIndex { get; init; }
}

/// <summary>
/// Player equips a weapon to a specific slot.
/// Based on WL_AGENT.C weapon selection logic (bt_readyknife, bt_readypistol, etc.).
/// </summary>
public class EquipWeaponAction : PlayerAction
{
	/// <summary>Weapon slot to equip to (0 = left/primary, 1 = right/secondary)</summary>
	public required int SlotIndex { get; init; }

	/// <summary>Weapon type identifier (e.g., "knife", "pistol", "machinegun", "chaingun")</summary>
	public required string WeaponType { get; init; }
}
