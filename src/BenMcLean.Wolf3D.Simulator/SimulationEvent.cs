using BenMcLean.Wolf3D.Assets.Gameplay;
using BenMcLean.Wolf3D.Simulator.Entities;

namespace BenMcLean.Wolf3D.Simulator;

/// <summary>
/// A door started opening.
/// Triggered in WL_ACT1.C:DoorOpening when position==0
/// </summary>
public struct DoorOpeningEvent
{
	public required ushort DoorIndex { get; init; }
	public required ushort TileX { get; init; }
	public required ushort TileY { get; init; }
}

/// <summary>
/// A door finished opening and is now fully open.
/// Triggered in WL_ACT1.C:DoorOpening when position>=0xFFFF
/// </summary>
public struct DoorOpenedEvent
{
	public required ushort DoorIndex { get; init; }
	public required ushort TileX { get; init; }
	public required ushort TileY { get; init; }
}

/// <summary>
/// A door's position changed during opening or closing animation.
/// Triggered every tic in WL_ACT1.C:DoorOpening and DoorClosing when position changes.
/// </summary>
public struct DoorPositionChangedEvent
{
	public required ushort DoorIndex { get; init; }
	public required ushort TileX { get; init; }
	public required ushort TileY { get; init; }
	public required ushort Position { get; init; }  // Current position (0=closed, 0xFFFF=open)
	public required DoorAction Action { get; init; }  // Opening or Closing
}

/// <summary>
/// A door started closing.
/// Triggered in WL_ACT1.C:CloseDoor
/// </summary>
public struct DoorClosingEvent
{
	public required ushort DoorIndex { get; init; }
	public required ushort TileX { get; init; }
	public required ushort TileY { get; init; }
}

/// <summary>
/// A door finished closing and is now fully closed.
/// Triggered in WL_ACT1.C:DoorClosing when position<=0
/// </summary>
public struct DoorClosedEvent
{
	public required ushort DoorIndex { get; init; }
	public required ushort TileX { get; init; }
	public required ushort TileY { get; init; }
}

/// <summary>
/// Player tried to open a locked door without the required key.
/// Triggered in WL_ACT1.C:OperateDoor when key check fails (plays NOWAYSND)
/// </summary>
public struct DoorLockedEvent
{
	public required ushort DoorIndex { get; init; }

	// WL_DEF.H:doorstruct:lock (byte in original)
	// Extended to string for modding: "gold key", "silver key", etc.
	public required string RequiredKey { get; init; }
}

/// <summary>
/// A door was blocked from closing (actor or player in the way).
/// Would be triggered in WL_ACT1.C:DoorClosing when blocking check fails
/// </summary>
public struct DoorBlockedEvent
{
	public required ushort DoorIndex { get; init; }
	public required ushort TileX { get; init; }
	public required ushort TileY { get; init; }
}

/// <summary>
/// A bonus object spawned in the world (static placement or enemy drop).
/// Triggered in WL_GAME.C:ScanInfoPlane (static) or WL_ACT1.C:PlaceItemType (dynamic).
/// </summary>
public struct BonusSpawnedEvent
{
	// Index in StatObjList array (WL_ACT1.C:statobjlist index)
	public required int StatObjIndex { get; init; }

	// VSwap sprite page number (WL_DEF.H:statstruct:shapenum)
	// -1 = despawned/removed (Wolf3D), -2 = invisible trigger (Noah's Ark), >= 0 = visible
	public required short Shape { get; init; }

	// WL_DEF.H:statstruct:tilex (original: byte)
	public required ushort TileX { get; init; }

	// WL_DEF.H:statstruct:tiley (original: byte)
	public required ushort TileY { get; init; }

	// WL_DEF.H:statstruct:itemnumber (bo_clip, bo_food, etc.)
	public required byte ItemNumber { get; init; }
}

/// <summary>
/// A bonus object was picked up by the player.
/// Triggered in WL_AGENT.C:GetBonus when player touches a bonus item.
/// </summary>
public struct BonusPickedUpEvent
{
	// Index in StatObjList array that was removed
	public required int StatObjIndex { get; init; }

	// WL_DEF.H:statstruct:itemnumber
	public required byte ItemNumber { get; init; }

	// Position where item was picked up
	public required ushort TileX { get; init; }
	public required ushort TileY { get; init; }
}
/// <summary>
/// An actor spawned in the world.
/// Triggered during map initialization or dynamically during gameplay.
/// WL_GAME.C:ScanInfoPlane (static) or dynamic spawning during gameplay.
/// </summary>
public struct ActorSpawnedEvent
{
	// Index in actor array (WL_DEF.H:objstruct linked list)
	public required int ActorIndex { get; init; }
	// WL_DEF.H:objstruct:tilex (original: unsigned = 16-bit)
	public required ushort TileX { get; init; }
	// WL_DEF.H:objstruct:tiley (original: unsigned = 16-bit)
	public required ushort TileY { get; init; }
	// WL_DEF.H:objstruct:dir (dirtype)
	public required Direction? Facing { get; init; }
	// Base sprite page number (WL_DEF.H:statestruct:shapenum)
	// If IsRotated=true, this is the base page for 8-directional sprites
	public required ushort Shape { get; init; }
	// True = 8-directional sprite group, False = single sprite from all angles
	public required bool IsRotated { get; init; }
}
/// <summary>
/// An actor moved to a new position.
/// Triggered during actor movement (WL_ACT2.C movement logic).
/// </summary>
public struct ActorMovedEvent
{
	public required int ActorIndex { get; init; }
	// WL_DEF.H:objstruct:x,y (32-bit signed, 16.16 fixed-point in original)
	// Upper 16 bits = tile coordinate, lower 16 bits = fractional position
	public required int X { get; init; }  // 16.16 fixed-point X coordinate
	public required int Y { get; init; }  // 16.16 fixed-point Y coordinate
	// WL_DEF.H:objstruct:dir
	public required Direction? Facing { get; init; }
}
/// <summary>
/// An actor's sprite changed (animation, state change, rotation).
/// Triggered when actor state changes (WL_DEF.H:statestruct determines sprite).
/// Fires VERY frequently - every state change, rotation, animation frame.
/// </summary>
public struct ActorSpriteChangedEvent
{
	public required int ActorIndex { get; init; }
	// New sprite page number (WL_DEF.H:statestruct:shapenum)
	// If IsRotated=true, this is the base page for 8-directional sprites
	public required ushort Shape { get; init; }
	// True = 8-directional sprite group, False = single sprite from all angles
	public required bool IsRotated { get; init; }
}
/// <summary>
/// An actor was removed from the world (died, despawned).
/// Triggered when actor is killed or removed (WL_ACT1.C:KillActor).
/// </summary>
public struct ActorDespawnedEvent
{
	public required int ActorIndex { get; init; }
	// Position where actor was despawned (for death animations, item drops)
	public required ushort TileX { get; init; }
	public required ushort TileY { get; init; }
}

/// <summary>
/// An actor should play a digi sound.
/// Triggered by actor scripts (WL_STATE.C:PlaySoundLocActor).
/// Presentation layer attaches sound to the actor - sound moves with actor during playback.
/// </summary>
public struct ActorPlaySoundEvent
{
	// Index of the actor playing the sound
	public required int ActorIndex { get; init; }
	// Sound name (e.g., "HALTSND", "SCHUTZSND", "DEATHSND")
	// String-based for modding flexibility and Lua compatibility
	public required string SoundName { get; init; }
	// Sound ID for presentation layer lookup if needed
	// Can be -1 if using name-based lookup only
	public int SoundId { get; init; }
}

/// <summary>
/// A door should play a digi sound.
/// Triggered by door events (WL_ACT1.C:DoorOpening, DoorClosing).
/// Presentation layer attaches sound to the door - can sweep across doorframe as it opens/closes.
/// </summary>
public struct DoorPlaySoundEvent
{
	// Index of the door playing the sound
	public required ushort DoorIndex { get; init; }
	// Sound name (e.g., "OPENDOORSND", "CLOSEDOORSND")
	public required string SoundName { get; init; }
	// Sound ID for presentation layer lookup if needed
	public int SoundId { get; init; }
}

/// <summary>
/// Play a global (non-positional) sound directly.
/// For UI sounds, music, narrator, or other sounds that bypass spatial audio.
/// </summary>
public struct PlayGlobalSoundEvent
{
	// Sound name (e.g., "BONUS1SND", "NOWAYSND" for UI feedback)
	public required string SoundName { get; init; }
	// Sound ID for presentation layer lookup if needed
	public int SoundId { get; init; }
}

/// <summary>
/// A pushwall's position changed during movement animation.
/// Triggered every tic in WL_ACT1.C while pushwall is moving.
/// </summary>
public struct PushWallPositionChangedEvent
{
	// Index in pushwall array
	public required ushort PushWallIndex { get; init; }
	// Current position in 16.16 fixed-point
	public required int X { get; init; }
	public required int Y { get; init; }
	// Current action state
	public required PushWallAction Action { get; init; }
}

/// <summary>
/// A pushwall should play a digi sound.
/// Triggered when pushwall starts moving (WL_ACT1.C).
/// Presentation layer attaches sound to the pushwall - sound moves with pushwall during playback.
/// </summary>
public struct PushWallPlaySoundEvent
{
	// Index of the pushwall playing the sound
	public required ushort PushWallIndex { get; init; }
	// Sound name (e.g., "PUSHWALLSND")
	public required string SoundName { get; init; }
	// Sound ID for presentation layer lookup if needed
	public int SoundId { get; init; }
}

/// <summary>
/// A weapon slot's sprite changed (animation frame update).
/// Triggered when weapon state changes (WL_AGENT.C:T_Attack animation progression).
/// Fires frequently during weapon animations.
/// </summary>
public struct WeaponSpriteChangedEvent
{
	// Weapon slot index (0 = left/primary, 1 = right/secondary)
	public required int SlotIndex { get; init; }
	// New sprite page number (WL_AGENT.C:weaponframe)
	// -1 indicates no sprite (weapon holstered or slot empty)
	public required ushort Shape { get; init; }
}

/// <summary>
/// A weapon was fired from a slot.
/// Triggered in WL_AGENT.C:Cmd_Fire when player presses attack button.
/// Used for muzzle flash, weapon sound, and hit effects.
/// </summary>
public struct WeaponFiredEvent
{
	// Weapon slot index that fired
	public required int SlotIndex { get; init; }
	// Weapon type that fired (e.g., "knife", "pistol", "chaingun")
	public required string WeaponType { get; init; }
	// Sound to play (e.g., "ATKKNIFESND", "ATKPISTOLSND", "ATKGATGUNSND")
	public required string SoundName { get; init; }
	// True if weapon hit an actor, false if missed
	public required bool DidHit { get; init; }
	// Actor index that was hit (null if DidHit is false)
	public int? HitActorIndex { get; init; }
}

/// <summary>
/// A weapon was equipped to a slot.
/// Triggered when player selects a weapon or game initializes player weapons.
/// Based on WL_AGENT.C weapon selection (bt_readyknife, bt_readypistol, etc.).
/// </summary>
public struct WeaponEquippedEvent
{
	// Weapon slot index
	public required int SlotIndex { get; init; }
	// Weapon type equipped (e.g., "knife", "pistol")
	public required string WeaponType { get; init; }
	// Initial sprite for equipped weapon (idle state)
	public required ushort Shape { get; init; }
}

/// <summary>
/// An elevator switch was activated, triggering level completion.
/// Triggered in WL_AGENT.C:Cmd_Use when player uses elevator switch (line 1767).
/// </summary>
public struct ElevatorActivatedEvent
{
	/// <summary>Tile X coordinate of the elevator switch</summary>
	public required ushort TileX { get; init; }
	/// <summary>Tile Y coordinate of the elevator switch</summary>
	public required ushort TileY { get; init; }
	/// <summary>True if using alternate elevator (player standing on AltElevator tile)</summary>
	public required bool IsAltElevator { get; init; }
	/// <summary>Destination map number</summary>
	public required byte DestinationLevel { get; init; }
	/// <summary>Sound to play (e.g., "LEVELDONESND")</summary>
	public required string SoundName { get; init; }
}

/// <summary>
/// An elevator switch texture should be flipped to show the pressed state.
/// Triggered when elevator is activated (WL_AGENT.C: tilemap[checkx][checky]++).
/// Presentation layer should swap the wall texture from unpressed to pressed.
/// </summary>
public struct ElevatorSwitchFlippedEvent
{
	/// <summary>Tile X coordinate of the elevator switch</summary>
	public required ushort TileX { get; init; }
	/// <summary>Tile Y coordinate of the elevator switch</summary>
	public required ushort TileY { get; init; }
	/// <summary>Original (unpressed) wall texture page number</summary>
	public required ushort OldTexture { get; init; }
	/// <summary>New (pressed) wall texture page number</summary>
	public required ushort NewTexture { get; init; }
}

/// <summary>
/// Player state changed (health, ammo, score, lives, keys, weapons).
/// Triggered by item pickups, damage, scoring, etc.
/// Presentation layer uses this to update HUD/status bar.
/// </summary>
public struct PlayerStateChangedEvent
{
	/// <summary>Current health (0-100 typical)</summary>
	public required int Health { get; init; }
	/// <summary>Current score</summary>
	public required int Score { get; init; }
	/// <summary>Current lives</summary>
	public required int Lives { get; init; }
	/// <summary>Current ammo for primary ammo type ("bullets")</summary>
	public required int Ammo { get; init; }
	/// <summary>Which keys the player has (bitmask or list)</summary>
	public required int KeyFlags { get; init; }
}

/// <summary>
/// A bonus item should play a sound at its position.
/// Triggered by item pickup scripts (WL_AGENT.C:GetBonus).
/// Presentation layer plays positional sound at item location.
/// </summary>
public struct BonusPlaySoundEvent
{
	/// <summary>Index in StatObjList array</summary>
	public required int StatObjIndex { get; init; }
	/// <summary>Tile X coordinate of the item</summary>
	public required ushort TileX { get; init; }
	/// <summary>Tile Y coordinate of the item</summary>
	public required ushort TileY { get; init; }
	/// <summary>Sound name to play (e.g., "HEALTH1SND", "GETKEYSND")</summary>
	public required string SoundName { get; init; }
	/// <summary>True if this is a digitized sound, false for AdLib/PC speaker</summary>
	public required bool IsDigiSound { get; init; }
}

/// <summary>
/// Screen flash effect (palette shift).
/// WL_PLAY.C: Bonus flash (NUMWHITESHIFTS * WHITETICS = 18 tics), damage flash, etc.
/// Triggered by Lua scripts via FlashScreen() or internally by game events.
/// Presentation layer renders a full-screen color overlay that fades out over the duration.
/// </summary>
public struct ScreenFlashEvent
{
	/// <summary>24-bit RGB color (e.g., 0xFF0000 for red)</summary>
	public required uint Color { get; init; }
	/// <summary>Duration in tics (default 18, matching original Wolf3D bonus flash)</summary>
	public required short Duration { get; init; }
}

/// <summary>
/// Player health reached zero.
/// WL_AGENT.C:TakeDamage death check.
/// Presentation layer handles death sequence and restart.
/// </summary>
public struct PlayerDiedEvent { }

/// <summary>
/// An item script requested navigation to a named menu screen.
/// Generic mechanism — any BonusScript can trigger any menu defined in XML.
/// WL_AGENT.C:VictoryTile sets gamestate.victoryflag → Victory screen.
/// Noah's Ark uses this for Bible quiz menus triggered by item pickups.
/// </summary>
public struct NavigateToMenuEvent
{
	/// <summary>Menu name as defined in XML (e.g., "Victory", "Quiz")</summary>
	public required string MenuName { get; init; }
}
