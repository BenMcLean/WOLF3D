using System;
using System.Collections.Generic;
using BenMcLean.Wolf3D.Assets.Gameplay;
using BenMcLean.Wolf3D.Simulator;

namespace BenMcLean.Wolf3D.Shared.StatusBar;

/// <summary>
/// Runtime state container for status bar values.
/// Holds a string-keyed dictionary of integers representing player inventory/stats.
/// Display-only - syncs from the authoritative Simulator state.
/// </summary>
public class StatusBarState
{
	private readonly Dictionary<string, int> _values = [];
	private readonly StatusBarDefinition _definition;
	private Inventory _subscribedInventory;
	private Action<string, int> _inventoryHandler;
	/// <summary>
	/// Event fired when a value changes.
	/// Parameters: (name, newValue)
	/// </summary>
	public event Action<string, int> ValueChanged;
	/// <summary>
	/// When true, inventory change events are ignored.
	/// Used to freeze the status bar display during death fadeout so the player
	/// doesn't see inventory reset values while the death animation plays.
	/// </summary>
	public bool Frozen { get; set; }
	/// <summary>
	/// Current weapon slot (0-3)
	/// </summary>
	public int CurrentWeapon { get; set; }
	/// <summary>
	/// Current face animation frame.
	/// Face pattern: FACE{level}{frame}PIC where level is 1-7 based on health, frame is A/B/C
	/// Level 8 = dead face (FACE8APIC)
	/// </summary>
	public int FaceFrame { get; set; }
	/// <summary>
	/// Creates a new StatusBarState initialized from a StatusBarDefinition.
	/// </summary>
	/// <param name="definition">The status bar definition containing initial values</param>
	public StatusBarState(StatusBarDefinition definition)
	{
		_definition = definition ?? throw new ArgumentNullException(nameof(definition));
		// Initialize values from definition
		foreach (StatusBarNumberDefinition number in _definition.Numbers)
			_values[number.Name] = number.Init;
		// Initialize weapon display from StatusBarWeapon inventory value
		StatusBarNumberDefinition statusBarWeapon = _definition.GetNumber("StatusBarWeapon");
		if (statusBarWeapon != null)
			CurrentWeapon = statusBarWeapon.Init;
	}
	/// <summary>
	/// Gets the value for a named status bar item.
	/// </summary>
	/// <param name="name">The name of the value (e.g., "Health", "Ammo")</param>
	/// <returns>The current value, or 0 if not found</returns>
	public int GetValue(string name) =>
		_values.TryGetValue(name, out int value) ? value : 0;
	/// <summary>
	/// Sets the value for a named status bar item.
	/// Value is clamped to Max if specified in the definition.
	/// </summary>
	/// <param name="name">The name of the value (e.g., "Health", "Ammo")</param>
	/// <param name="value">The new value</param>
	public void SetValue(string name, int value)
	{
		// Clamp to max if defined
		StatusBarNumberDefinition numberDef = _definition.GetNumber(name);
		if (numberDef?.Max.HasValue == true && value > numberDef.Max.Value)
			value = numberDef.Max.Value;
		// Clamp minimum to 0
		if (value < 0)
			value = 0;
		// Only update and fire event if value changed
		if (!_values.TryGetValue(name, out int oldValue) || oldValue != value)
		{
			_values[name] = value;
			ValueChanged?.Invoke(name, value);
		}
	}
	/// <summary>
	/// Adds a delta to a named status bar item.
	/// Convenience method that calls GetValue and SetValue.
	/// </summary>
	/// <param name="name">The name of the value (e.g., "Health", "Ammo")</param>
	/// <param name="delta">The amount to add (can be negative)</param>
	public void AddValue(string name, int delta) =>
		SetValue(name, GetValue(name) + delta);
	/// <summary>
	/// Resets values that have LevelReset defined.
	/// Called when transitioning between levels.
	/// </summary>
	public void OnLevelChange()
	{
		foreach (StatusBarNumberDefinition number in _definition.Numbers)
			if (number.LevelReset.HasValue)
				SetValue(number.Name, number.LevelReset.Value);
	}
	/// <summary>
	/// Resets all values to their initial state.
	/// Called when starting a new game.
	/// </summary>
	public void Reset()
	{
		foreach (StatusBarNumberDefinition number in _definition.Numbers)
			SetValue(number.Name, number.Init);
		// Reset weapon display from StatusBarWeapon inventory value
		StatusBarNumberDefinition statusBarWeapon = _definition.GetNumber("StatusBarWeapon");
		if (statusBarWeapon != null)
			CurrentWeapon = statusBarWeapon.Init;
		FaceFrame = 0;
	}
	/// <summary>
	/// Syncs status bar state from simulator values.
	/// Called each frame or when game state changes.
	/// Supports custom values defined by modders in XML.
	/// CurrentWeapon is derived from the StatusBarWeapon inventory value.
	/// </summary>
	/// <param name="values">Dictionary of value names to their current values</param>
	public void SyncFromSimulator(IReadOnlyDictionary<string, int> values)
	{
		foreach (KeyValuePair<string, int> kvp in values)
			SetValue(kvp.Key, kvp.Value);
		if (values.TryGetValue("StatusBarWeapon", out int weapon))
			CurrentWeapon = weapon;
	}
	/// <summary>
	/// Subscribes to an Inventory's ValueChanged event for automatic sync.
	/// When inventory values change, this StatusBarState will be updated automatically.
	/// This enables direct subscription from the status bar to the simulator's inventory.
	/// </summary>
	/// <param name="inventory">The Inventory to subscribe to</param>
	public void SubscribeToInventory(Inventory inventory)
	{
		ArgumentNullException.ThrowIfNull(inventory);
		// Unsubscribe from previous inventory if any
		UnsubscribeFromInventory();
		_subscribedInventory = inventory;
		_inventoryHandler = (name, value) =>
		{
			if (Frozen)
				return;
			SetValue(name, value);
			if (name == "StatusBarWeapon")
				CurrentWeapon = value;
		};
		inventory.ValueChanged += _inventoryHandler;
	}
	/// <summary>
	/// Unsubscribes from the previously subscribed Inventory.
	/// Called during cleanup to prevent dangling references.
	/// </summary>
	public void UnsubscribeFromInventory()
	{
		if (_subscribedInventory is not null && _inventoryHandler is not null)
		{
			_subscribedInventory.ValueChanged -= _inventoryHandler;
			_subscribedInventory = null;
			_inventoryHandler = null;
		}
	}
	/// <summary>
	/// Gets all current values as a read-only dictionary.
	/// </summary>
	public IReadOnlyDictionary<string, int> Values => _values;
	/// <summary>
	/// Gets the status bar definition this state is based on.
	/// </summary>
	public StatusBarDefinition Definition => _definition;
}
