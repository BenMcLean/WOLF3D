using System.Collections.Generic;

namespace BenMcLean.Wolf3D.Simulator.Snapshots;

/// <summary>
/// Serializable snapshot of the Inventory's dynamic state.
/// Captures both current values and runtime-modified max values so that
/// capacity upgrades (e.g., ammo bag pickups increasing max feed capacity)
/// survive save/load.
///
/// MaxValues includes all max values present at save time, including those
/// set from XML defaults and any modified by item scripts via SetMax.
/// </summary>
public record InventorySnapshot
{
	/// <summary>
	/// Current inventory values (health, score, lives, keys, ammo, weapons, MapOn, etc.).
	/// </summary>
	public Dictionary<string, int> Values { get; init; }

	/// <summary>
	/// Maximum capacity values for inventory items.
	/// Includes both XML-configured defaults and runtime modifications from item scripts.
	/// </summary>
	public Dictionary<string, int> MaxValues { get; init; }
}
