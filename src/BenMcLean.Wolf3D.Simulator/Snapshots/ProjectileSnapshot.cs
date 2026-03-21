namespace BenMcLean.Wolf3D.Simulator.Snapshots;

/// <summary>
/// Serializable snapshot of a Projectile's mutable state.
/// Projectiles are matched by position in the projectiles list on restore.
/// CurrentState is serialized as a string name, resolved via StateCollection
/// by the Simulator after Load() restores value-type fields.
/// </summary>
public record ProjectileSnapshot
{
	/// <summary>
	/// Unique projectile identifier. Restored so presentation layer can reconcile events.
	/// </summary>
	public long ProjectileId { get; init; }

	/// <summary>
	/// Projectile type name (e.g., "rocket", "watermelon").
	/// Used to look up ProjectileDefinition on restore.
	/// </summary>
	public string ProjectileType { get; init; }

	/// <summary>
	/// Name of the current state. Resolved to a State reference via StateCollection on restore.
	/// </summary>
	public string CurrentStateName { get; init; }

	/// <summary>
	/// WL_DEF.H:objstruct:ticcount (original: int = 16-bit signed)
	/// </summary>
	public short TicCount { get; init; }

	/// <summary>
	/// WL_DEF.H:objstruct:tilex (original: byte, extended to ushort)
	/// </summary>
	public ushort TileX { get; init; }

	/// <summary>
	/// WL_DEF.H:objstruct:tiley (original: byte, extended to ushort)
	/// </summary>
	public ushort TileY { get; init; }

	/// <summary>
	/// WL_DEF.H:objstruct:x (original: fixed = long = 32-bit signed, 16.16 fixed-point)
	/// </summary>
	public int X { get; init; }

	/// <summary>
	/// WL_DEF.H:objstruct:y (original: fixed = long = 32-bit signed, 16.16 fixed-point)
	/// </summary>
	public int Y { get; init; }

	/// <summary>
	/// Direction of travel in degrees 0-359.
	/// WL_DEF.H:objstruct:angle (original: int = 16-bit signed)
	/// </summary>
	public short Angle { get; init; }

	/// <summary>
	/// Movement speed per tic in 16.16 fixed-point.
	/// WL_DEF.H:objstruct:speed (original: long = 32-bit signed)
	/// </summary>
	public int Speed { get; init; }

	/// <summary>
	/// True = fired by player, false = fired by enemy.
	/// </summary>
	public bool IsPlayerOwned { get; init; }

	/// <summary>
	/// True = in explosion animation (no movement).
	/// </summary>
	public bool IsExploding { get; init; }
}
