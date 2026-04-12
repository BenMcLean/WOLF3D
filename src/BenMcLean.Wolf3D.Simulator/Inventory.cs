using System;
using System.Collections.Generic;
using BenMcLean.Wolf3D.Simulator.Snapshots;

namespace BenMcLean.Wolf3D.Simulator;

/// <summary>
/// Unified inventory system for all player state (health, score, lives, keys, ammo, weapons).
/// Uses a string-keyed dictionary of integers, enabling:
/// - Moddability: Values defined in XML, modders can add custom inventory items
/// - Easy serialization: Dictionary&lt;string, int&gt; is trivially JSON-serializable
/// </summary>
public class Inventory : ISnapshot<InventorySnapshot>
{
	private readonly Dictionary<string, int> _values = [];
	private readonly Dictionary<string, int> _maxValues = [];

	/// <summary>
	/// Fired when a value changes.
	/// Parameters: (name, newValue)
	/// </summary>
	public event Action<string, int> ValueChanged;

	/// <summary>
	/// Gets all current values as a read-only dictionary.
	/// </summary>
	public IReadOnlyDictionary<string, int> Values => _values;

	/// <summary>
	/// Gets the value for a named inventory item.
	/// </summary>
	/// <param name="name">The name of the value (e.g., "Health", "Ammo", "Gold Key")</param>
	/// <returns>The current value, or 0 if not found</returns>
	public int GetValue(string name) =>
		_values.TryGetValue(name, out int value) ? value : 0;

	/// <summary>
	/// Sets the value for a named inventory item.
	/// Value is clamped to [0, Max] if a maximum is defined.
	/// </summary>
	/// <param name="name">The name of the value (e.g., "Health", "Ammo")</param>
	/// <param name="value">The new value</param>
	public void SetValue(string name, int value)
	{
		// Clamp to max if defined
		if (_maxValues.TryGetValue(name, out int max) && value > max)
			value = max;

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
	/// Adds a delta to a named inventory item.
	/// Convenience method that calls GetValue and SetValue.
	/// </summary>
	/// <param name="name">The name of the value (e.g., "Health", "Ammo")</param>
	/// <param name="delta">The amount to add (can be negative)</param>
	public void AddValue(string name, int delta) =>
		SetValue(name, GetValue(name) + delta);

	/// <summary>
	/// Gets the maximum value for a named inventory item.
	/// </summary>
	/// <param name="name">The name of the value</param>
	/// <returns>The maximum value, or int.MaxValue if not defined</returns>
	public int GetMax(string name) =>
		_maxValues.TryGetValue(name, out int max) ? max : int.MaxValue;

	/// <summary>
	/// Sets the maximum value for a named inventory item.
	/// If current value exceeds new max, it will be clamped on next SetValue call.
	/// </summary>
	/// <param name="name">The name of the value</param>
	/// <param name="max">The maximum value</param>
	public void SetMax(string name, int max) =>
		_maxValues[name] = max;

	/// <summary>
	/// Checks if the player has a named item (value > 0).
	/// Useful for keys, weapons, and boolean flags.
	/// </summary>
	/// <param name="name">The name of the value (e.g., "Gold Key", "Weapon2")</param>
	/// <returns>True if value > 0</returns>
	public bool Has(string name) =>
		GetValue(name) > 0;

	/// <summary>
	/// Captures the current inventory state for preservation across level transitions
	/// and save games. Includes both current values and max values so that runtime
	/// capacity upgrades (e.g., ammo bag pickups) survive save/load.
	/// </summary>
	/// <returns>Snapshot containing all current inventory values and max values</returns>
	public InventorySnapshot Save() =>
		new()
		{
			Values = new(_values),
			MaxValues = new(_maxValues)
		};

	/// <summary>
	/// Restores inventory state from a previously captured snapshot.
	/// Max values are restored before current values so that clamping is applied correctly.
	/// Used during level transitions and save game loading to preserve player state.
	/// </summary>
	/// <param name="savedState">Snapshot containing saved inventory values and max values</param>
	public void Load(InventorySnapshot savedState)
	{
		if (savedState is null)
			return;

		if (savedState.MaxValues is not null)
		{
			foreach (KeyValuePair<string, int> kvp in savedState.MaxValues)
				_maxValues[kvp.Key] = kvp.Value;
		}

		if (savedState.Values is not null)
		{
			foreach (KeyValuePair<string, int> kvp in savedState.Values)
				SetValue(kvp.Key, kvp.Value);
		}
	}
}
