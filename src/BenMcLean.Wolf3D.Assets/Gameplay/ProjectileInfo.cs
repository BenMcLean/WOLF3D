using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace BenMcLean.Wolf3D.Assets.Gameplay;

/// <summary>
/// Defines a projectile type and its properties.
/// Covers both enemy projectiles (WL_FPROJ.C:T_Projectile) and player projectiles
/// (WL_ACT2.C:T_Missile / MissileTryMove).
/// </summary>
public class ProjectileDefinition
{
	/// <summary>
	/// Unique projectile type identifier (e.g., "rocket", "needle", "fireball", "watermelon").
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// Initial state name when the projectile is spawned (e.g., "s_rocket", "s_missile").
	/// Resolved via StateCollection on spawn.
	/// </summary>
	public string InitialState { get; set; }

	/// <summary>
	/// State to transition to on wall collision (e.g., "s_boom1" for rockets).
	/// Null means the projectile despawns immediately on wall hit (needles, fireballs).
	/// WL_FPROJ.C:T_Projectile: ob->state = &amp;s_boom1 for rocketobj, NULL for others.
	/// </summary>
	public string ExplodeState { get; set; }

	/// <summary>
	/// Minimum damage dealt per hit.
	/// WL_FPROJ.C damage ranges (combined with MaxDamage for interpolation):
	/// needle: 20-51, rocket/hrocket/spark: 30-61, fire: 0-31
	/// player missile: 16-64, player flame: 1-16
	/// </summary>
	public short MinDamage { get; set; }

	/// <summary>
	/// Maximum damage dealt per hit.
	/// </summary>
	public short MaxDamage { get; set; }

	/// <summary>
	/// Half-size of the wall collision bounding box in 16.16 fixed-point.
	/// WL_FPROJ.C:PROJSIZE = 0x2000 (enemy projectiles).
	/// </summary>
	public int WallCollisionSize { get; set; } = 0x2000;

	/// <summary>
	/// Proximity radius for hitting actors/player in 16.16 fixed-point.
	/// WL_FPROJ.C:PROJECTILESIZE = 0xC000 for enemy projectiles hitting player.
	/// WL_ACT2.C:MINMISSILEDIST = 0x8000 for player projectiles hitting actors.
	/// </summary>
	public int ActorCollisionSize { get; set; } = 0xC000;

	/// <summary>
	/// Movement speed per tic in 16.16 fixed-point.
	/// WL_FPROJ.C: rocketobj/needleobj = 0x2000, fireobj = 0x1200.
	/// WL_ACT2.C: player missileobj/flameobj = 0x3000.
	/// </summary>
	public int Speed { get; set; } = 0x2000;

	/// <summary>
	/// Creates a ProjectileDefinition from an XElement.
	/// </summary>
	public static ProjectileDefinition FromXElement(XElement element)
	{
		return new ProjectileDefinition
		{
			Name = element.Attribute("Name")?.Value
				?? throw new ArgumentException("Projectile element must have a Name attribute"),
			InitialState = element.Attribute("InitialState")?.Value
				?? throw new ArgumentException("Projectile element must have an InitialState attribute"),
			ExplodeState = element.Attribute("ExplodeState")?.Value,
			MinDamage = short.TryParse(element.Attribute("MinDamage")?.Value, out short minDmg)
				? minDmg : (short)0,
			MaxDamage = short.TryParse(element.Attribute("MaxDamage")?.Value, out short maxDmg)
				? maxDmg : (short)0,
			WallCollisionSize = int.TryParse(element.Attribute("WallCollisionSize")?.Value, out int wallSize)
				? wallSize : 0x2000,
			ActorCollisionSize = int.TryParse(element.Attribute("ActorCollisionSize")?.Value, out int actorSize)
				? actorSize : 0xC000,
			Speed = int.TryParse(element.Attribute("Speed")?.Value, out int speed)
				? speed : 0x2000,
		};
	}
}
