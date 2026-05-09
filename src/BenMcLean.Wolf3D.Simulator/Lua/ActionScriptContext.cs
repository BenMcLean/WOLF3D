using System;
using Microsoft.Extensions.Logging;

namespace BenMcLean.Wolf3D.Simulator.Lua;

/// <summary>
/// Script context for the action stage (gameplay).
/// Extends BaseScriptContext with deterministic RNG/GameClock and generic inventory API.
/// All player state (health, ammo, keys, weapons, score, lives) is accessed via the Inventory.
/// </summary>
public class ActionScriptContext(
	Simulator simulator,
	RNG rng,
	GameClock gameClock,
	ILogger logger = null) : BaseScriptContext(logger), IActionScriptContext
{
	protected readonly Simulator simulator = simulator;
	protected readonly RNG rng = rng;
	protected readonly GameClock gameClock = gameClock;
	public RNG RNG => rng;
	public GameClock GameClock => gameClock;
	#region Generic Inventory API (exposed to Lua)
	/// <summary>
	/// Get the value of any inventory item.
	/// Examples: GetValue("Health"), GetValue("bullets"), GetValue("Gold Key"), GetValue("Weapon2")
	/// </summary>
	/// <param name="name">The inventory item name</param>
	/// <returns>Current value, or 0 if not found</returns>
	public int GetValue(string name) => simulator.Inventory.GetValue(name);
	/// <summary>
	/// Set the value of any inventory item.
	/// Value is clamped to [0, Max] if a maximum is defined.
	/// Examples: SetValue("Health", 100), SetValue("Gold Key", 1)
	/// </summary>
	/// <param name="name">The inventory item name</param>
	/// <param name="value">The new value</param>
	public void SetValue(string name, int value) => simulator.Inventory.SetValue(name, value);
	/// <summary>
	/// Add to any inventory item value.
	/// Examples: AddValue("Health", 10), AddValue("bullets", -1), AddValue("Score", 500)
	/// </summary>
	/// <param name="name">The inventory item name</param>
	/// <param name="delta">Amount to add (can be negative)</param>
	public void AddValue(string name, int delta) => simulator.Inventory.AddValue(name, delta);
	/// <summary>
	/// Get the maximum value for any inventory item.
	/// Examples: GetMax("Health") returns 100, GetMax("bullets") returns 99
	/// </summary>
	/// <param name="name">The inventory item name</param>
	/// <returns>Maximum value, or int.MaxValue if no max defined</returns>
	public int GetMax(string name) => simulator.Inventory.GetMax(name);
	/// <summary>
	/// Set the maximum value for any inventory item.
	/// Used by capacity upgrade pickups (e.g., ammo bags increasing max feed capacity).
	/// Examples: SetMax("bullets", 150)
	/// </summary>
	/// <param name="name">The inventory item name</param>
	/// <param name="max">The new maximum value</param>
	public void SetMax(string name, int max) => simulator.Inventory.SetMax(name, max);
	/// <summary>
	/// Check if player has an inventory item (value > 0).
	/// Examples: Has("Gold Key"), Has("Weapon2"), Has("bullets")
	/// </summary>
	/// <param name="name">The inventory item name</param>
	/// <returns>True if value > 0</returns>
	public bool Has(string name) => simulator.Inventory.Has(name);
	#endregion
	#region Screen Flash API (exposed to Lua)
	/// <summary>
	/// Triggers a full-screen color flash effect.
	/// WL_PLAY.C: Bonus flash (white/yellow), damage flash (red).
	/// </summary>
	/// <param name="color">24-bit RGB color (e.g., 0xFF0000 for red, 0xFFFF00 for yellow)</param>
	/// <param name="duration">Duration in tics (default 18 = ~257ms, matching original Wolf3D bonus flash)</param>
	public void FlashScreen(int color, int duration = 18) => simulator.EmitScreenFlash((uint)color, (short)duration);
	/// <summary>
	/// Marks every tile on the map as permanently seen, revealing it on the automap.
	/// WL_AGENT.C:#ifdef GAMEVER_NOAH3D bo_map pickup (gamestate.automap = true).
	/// </summary>
	public void RevealEntireMap() => simulator.RevealEntireMap();
	#endregion
	#region Menu Navigation API (exposed to Lua)
	/// <summary>
	/// Delegate for navigating to a named menu screen.
	/// Wired by Simulator when creating item script contexts.
	/// Generic mechanism — any BonusScript can trigger any menu.
	/// </summary>
	public Action<string> NavigateToMenuAction { get; set; }
	public Action<int, bool> RequestGameplayMapTransitionAction { get; set; }
	/// <summary>
	/// Navigate to a named menu screen.
	/// Exposed to Lua. Used by VictoryTile, Bible quiz triggers, elevator switches, etc.
	/// WL_AGENT.C:VictoryTile → gamestate.victoryflag → Victory screen.
	/// </summary>
	/// <param name="menuName">Menu name as defined in XML (e.g., "Victory", "LevelComplete")</param>
	public void NavigateToMenu(string menuName) => NavigateToMenuAction?.Invoke(menuName);
	public void RequestGameplayMapTransition(int destinationLevel, bool preservePlayerTransform = false)
	{
		if (RequestGameplayMapTransitionAction is not null)
			RequestGameplayMapTransitionAction(destinationLevel, preservePlayerTransform);
		else
			simulator.RequestGameplayMapTransition((byte)destinationLevel, preservePlayerTransform);
	}
	#endregion
	#region Player Query & Actor API (exposed to Lua)
	/// <summary>
	/// Get player tile X coordinate.
	/// WL_DEF.H:player->tilex
	/// </summary>
	public int GetPlayerTileX() => simulator.PlayerTileX;
	/// <summary>
	/// Get player tile Y coordinate.
	/// WL_DEF.H:player->tiley
	/// </summary>
	public int GetPlayerTileY() => simulator.PlayerTileY;
	/// <summary>
	/// Get the player's current X position (16.16 fixed-point).
	/// WL_DEF.H:player->x
	/// </summary>
	public int GetPlayerX() => simulator.PlayerX;
	/// <summary>
	/// Get the current game identifier (for example, "WL6" or "N3D").
	/// Exposed to Lua for game-specific behavioral differences that exist in the original codebase.
	/// </summary>
	public string GetGameName() => simulator.GameName;
	/// <summary>
	/// Get the player's current Y position (16.16 fixed-point).
	/// WL_DEF.H:player->y
	/// </summary>
	public int GetPlayerY() => simulator.PlayerY;
	public int GetPlayerAngle() => simulator.PlayerAngle;
	/// <summary>
	/// Check whether a tile is navigable (not a wall, closed door, or occupied tile).
	/// WL_ACT2.C:TryWalk uses actorat[] and tilemap[]; this wraps the same logic.
	/// </summary>
	public bool IsTileNavigable(int x, int y) => simulator.IsTileNavigable((ushort)x, (ushort)y);
	/// <summary>
	/// Dynamically spawn an actor at the given tile, facing the given direction (0–7, default 2=north).
	/// The actor type must have an InitialState defined in its XML Actor element.
	/// WL_ACT2.C:SpawnNewObj — generic actor allocator used for BJ and mod-driven spawning.
	/// </summary>
	/// <returns>Index of the spawned actor, or -1 on failure.</returns>
	public int SpawnActor(string actorType, int tileX, int tileY, int facing = 2) =>
		simulator.SpawnActorAtTile(actorType, (ushort)tileX, (ushort)tileY, (Assets.Gameplay.Direction)facing);
	/// <summary>
	/// Set VictoryFlag and teleport the player to the viewing tile.
	/// Called from A_InitBJRun during BJ's spawn action.
	/// WL_ACT2.C:SpawnBJVictory → WL_AGENT.C:VictoryTile → gamestate.victoryflag
	/// </summary>
	public void TriggerVictory(int viewTileX, int viewTileY) =>
		simulator.TriggerVictory((ushort)viewTileX, (ushort)viewTileY);
	#endregion Player Query & Actor API
	#region Picture API (exposed to Lua)
	/// <summary>
	/// Update a named status bar picture.
	/// Exposed to Lua. Used by face update and other status bar animations.
	/// Fires StatusBarPicChangedEvent for the presentation layer.
	/// </summary>
	/// <param name="id">Picture Id as defined in the StatusBar &lt;Picture Id="..."&gt; element</param>
	/// <param name="picName">New VgaGraph picture name to display (e.g., "FACE1APIC")</param>
	public void SetPicture(string id, string picName) =>
		simulator.EmitStatusBarPicChanged(id, picName);
	/// <summary>
	/// Check whether the active status bar defines a picture with the given Id.
	/// Useful for mods that want to drive optional HUD elements conditionally.
	/// </summary>
	/// <param name="id">Picture Id as defined in the StatusBar &lt;Picture Id="..."&gt; element</param>
	/// <returns>True if that picture Id exists in the current status bar definition</returns>
	public bool HasPicture(string id) => simulator.HasStatusBarPicture(id);
	#endregion
	#region Text API (exposed to Lua)
	/// <summary>
	/// Update a named status bar text label.
	/// Exposed to Lua. Used to update displayed values (health, ammo, score, etc.).
	/// Fires StatusBarTextChangedEvent for the presentation layer.
	/// </summary>
	/// <param name="id">Text Id as defined in the StatusBar &lt;Text Id="..."&gt; element</param>
	/// <param name="content">New text content to display</param>
	public void SetText(string id, string content) =>
		simulator.EmitStatusBarTextChanged(id, content);
	#endregion
}
