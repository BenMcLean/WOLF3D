using BenMcLean.Wolf3D.Assets.Gameplay;
using BenMcLean.Wolf3D.Simulator.Snapshots;

namespace BenMcLean.Wolf3D.Simulator.Entities;

/// <summary>
/// A projectile in flight (rocket, needle, fireball, watermelon, etc.).
/// Covers both enemy-fired (WL_FPROJ.C:T_Projectile) and player-fired
/// (WL_ACT2.C:T_Missile) projectiles.
/// Unlike actors, projectiles are transient and removed from the list when they despawn.
/// Unique identity is tracked by ProjectileId, not by list index.
/// </summary>
public class Projectile : ISnapshot<ProjectileSnapshot>
{
	/// <summary>
	/// Unique monotonically-increasing identifier for this projectile instance.
	/// Used by the presentation layer to match spawned/moved/despawned events.
	/// Stable across the projectile's lifetime; never reused.
	/// </summary>
	public long ProjectileId { get; set; }

	/// <summary>
	/// Projectile type name (e.g., "rocket", "needle", "watermelon").
	/// Used to look up ProjectileDefinition for damage, collision sizes, etc.
	/// </summary>
	public string ProjectileType { get; set; }

	/// <summary>
	/// Current state in the animation/behavior state machine.
	/// WL_DEF.H:objstruct:state (original: statetype*)
	/// Null indicates the projectile should be removed.
	/// </summary>
	public State CurrentState { get; set; }

	/// <summary>
	/// Tics remaining in the current state before transitioning.
	/// WL_DEF.H:objstruct:ticcount (original: int = 16-bit signed)
	/// 0 = non-transitional (active flight); > 0 = countdown to next state.
	/// </summary>
	public short TicCount { get; set; }

	/// <summary>
	/// Tile X coordinate (destination tile).
	/// WL_DEF.H:objstruct:tilex (original: byte, extended to ushort)
	/// Intentional extension: ushort to support maps > 64×64.
	/// </summary>
	public ushort TileX { get; set; }

	/// <summary>
	/// Tile Y coordinate (destination tile).
	/// WL_DEF.H:objstruct:tiley (original: byte, extended to ushort)
	/// Intentional extension: ushort to support maps > 64×64.
	/// </summary>
	public ushort TileY { get; set; }

	/// <summary>
	/// X position in 16.16 fixed-point.
	/// WL_DEF.H:objstruct:x (original: fixed = long = 32-bit signed)
	/// </summary>
	public int X { get; set; }

	/// <summary>
	/// Y position in 16.16 fixed-point.
	/// WL_DEF.H:objstruct:y (original: fixed = long = 32-bit signed)
	/// </summary>
	public int Y { get; set; }

	/// <summary>
	/// Direction of travel in degrees (0-359).
	/// WL_DEF.H:objstruct:angle (original: int = 16-bit signed)
	/// 0 = east, 90 = north, 180 = west, 270 = south.
	/// </summary>
	public short Angle { get; set; }

	/// <summary>
	/// Movement speed per tic in 16.16 fixed-point.
	/// WL_DEF.H:objstruct:speed (original: long = 32-bit signed)
	/// </summary>
	public int Speed { get; set; }

	/// <summary>
	/// True if this projectile was fired by the player; false if fired by an enemy.
	/// Determines collision target: player-owned projectiles check actors,
	/// enemy-owned projectiles check the player.
	/// WL_FPROJ.C:T_Projectile (enemy) vs WL_ACT2.C:T_Missile (player).
	/// </summary>
	public bool IsPlayerOwned { get; set; }

	/// <summary>
	/// True when the projectile has hit a wall and is playing its explosion animation.
	/// Movement is suppressed while exploding.
	/// </summary>
	public bool IsExploding { get; set; }

	public ProjectileSnapshot Save() => new()
	{
		ProjectileId = ProjectileId,
		ProjectileType = ProjectileType,
		CurrentStateName = CurrentState?.Name,
		TicCount = TicCount,
		TileX = TileX,
		TileY = TileY,
		X = X,
		Y = Y,
		Angle = Angle,
		Speed = Speed,
		IsPlayerOwned = IsPlayerOwned,
		IsExploding = IsExploding,
	};

	public void Load(ProjectileSnapshot state)
	{
		// ProjectileId and ProjectileType restored by Simulator.Load
		TicCount = state.TicCount;
		TileX = state.TileX;
		TileY = state.TileY;
		X = state.X;
		Y = state.Y;
		Angle = state.Angle;
		Speed = state.Speed;
		IsPlayerOwned = state.IsPlayerOwned;
		IsExploding = state.IsExploding;
		// CurrentState resolved separately by Simulator via StateCollection
	}
}
