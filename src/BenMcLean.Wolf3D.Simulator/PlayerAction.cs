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
/// Player attempts to fire a weapon from a specific slot.
/// Presentation layer performs hit detection and provides results.
/// Based on WL_AGENT.C:Cmd_Fire (line 1629) and T_Attack (line 2101).
/// </summary>
public class FireWeaponAction : PlayerAction
{
	/// <summary>
	/// Weapon slot index (0 = left hand VR / primary traditional, 1 = right hand VR / secondary).
	/// Identifies which weapon slot is firing.
	/// </summary>
	public required int SlotIndex { get; init; }

	/// <summary>
	/// Actor that was hit (from presentation's raycast), or null if missed.
	/// Presentation layer is authoritative for hit detection (pixel-perfect in VR, traditional in 2D).
	/// Based on WL_AGENT.C:GunAttack/KnifeAttack raycast results.
	/// </summary>
	public int? HitActorIndex { get; init; }

	/// <summary>
	/// 3D hit point location (for blood sprites, impacts).
	/// Uses Wolf3D coordinate system (X, Y in 16.16 fixed-point).
	/// Null if no hit occurred.
	/// </summary>
	public (int x, int y)? HitPoint { get; init; }  // 16.16 fixed-point coordinates
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
