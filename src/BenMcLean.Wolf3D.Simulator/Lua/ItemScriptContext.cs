using Microsoft.Extensions.Logging;
using BenMcLean.Wolf3D.Simulator.Entities;

namespace BenMcLean.Wolf3D.Simulator.Lua;

/// <summary>
/// Script context for item pickup scripts.
/// Extends EntityScriptContext with item-specific properties.
/// Item scripts return true to consume the item, false to leave it.
///
/// Uses generic inventory API inherited from ActionScriptContext:
/// - GetValue(name), SetValue(name, value), AddValue(name, delta)
/// - GetMax(name), Has(name)
///
/// Example Lua script for health pickup:
/// <code>
/// if GetValue("Health") &lt; GetMax("Health") then
///     AddValue("Health", 10)
///     PlayLocalDigiSound("HEALTH1SND")
///     return true
/// end
/// return false
/// </code>
///
/// Example Lua script for key pickup:
/// <code>
/// SetValue("Gold Key", 1)
/// PlayLocalDigiSound("GETKEYSND")
/// return true
/// </code>
///
/// Example Lua script for weapon pickup:
/// <code>
/// if not Has("Weapon2") then
///     SetValue("Weapon2", 1)
///     AddValue("Ammo", 6)
///     PlayLocalDigiSound("GETMACHINESND")
///     return true
/// end
/// return false
/// </code>
/// </summary>
public class ItemScriptContext : EntityScriptContext
{
	private readonly StatObj item;
	private readonly int itemIndex;

	/// <summary>
	/// Tile X coordinate of the item (exposed to Lua).
	/// </summary>
	public int ItemTileX => item.TileX;

	/// <summary>
	/// Tile Y coordinate of the item (exposed to Lua).
	/// </summary>
	public int ItemTileY => item.TileY;

	/// <summary>
	/// Item number/type (e.g., bo_food, bo_clip - exposed to Lua).
	/// </summary>
	public int ItemNumber => item.ItemNumber;

	/// <summary>
	/// Shape number of the item sprite (exposed to Lua).
	/// </summary>
	public int ItemShape => item.ShapeNum;

	public ItemScriptContext(
		Simulator simulator,
		StatObj item,
		int itemIndex,
		RNG rng,
		GameClock gameClock,
		ILogger logger = null)
		: base(simulator, rng, gameClock, item.TileX, item.TileY, logger)
	{
		this.item = item;
		this.itemIndex = itemIndex;
	}

	// Actor API stubs (not used by items)
	public override void SpawnActor(int type, int x, int y) { }
	public override void DespawnActor(int actorId) { }
}
