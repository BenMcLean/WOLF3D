using System;
using System.Collections.Generic;
using BenMcLean.Wolf3D.Assets.Gameplay;
using BenMcLean.Wolf3D.Simulator.State;

namespace BenMcLean.Wolf3D.Simulator;

/// <summary>
/// Unified inventory system for all player state (health, score, lives, keys, ammo, weapons).
/// Uses a string-keyed dictionary of integers, enabling:
/// - Moddability: Values defined in XML, modders can add custom inventory items
/// - Easy serialization: Dictionary<string, int> is trivially JSON-serializable
/// - Event-driven updates: ValueChanged event enables direct UI subscription
/// </summary>
public class Inventory : IStateSavable<Dictionary<string, int>>
{
	private readonly Dictionary<string, int> _values = [];
	private readonly Dictionary<string, int> _maxValues = [];
	private readonly Dictionary<string, int> _initValues = [];
	private readonly Dictionary<string, int?> _levelResetValues = [];

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
	/// Fires ValueChanged event if value actually changed.
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
	/// Initializes the inventory from a StatusBarDefinition.
	/// Sets up initial values, maximums, and level reset values from XML config.
	/// </summary>
	/// <param name="definition">The status bar definition containing value configurations</param>
	public void InitializeFromDefinition(StatusBarDefinition definition)
	{
		if (definition == null)
			throw new ArgumentNullException(nameof(definition));

		_values.Clear();
		_maxValues.Clear();
		_initValues.Clear();
		_levelResetValues.Clear();

		// Initialize from Number definitions (health, score, ammo, keys, etc.)
		foreach (StatusBarNumberDefinition number in definition.Numbers)
		{
			_initValues[number.Name] = number.Init;
			_values[number.Name] = number.Init;

			if (number.Max.HasValue)
				_maxValues[number.Name] = number.Max.Value;

			_levelResetValues[number.Name] = number.LevelReset;

			// Fire initial value event
			ValueChanged?.Invoke(number.Name, number.Init);
		}

		// Initialize from Weapon definitions (Weapon0, Weapon1, etc.)
		if (definition.Weapons?.Weapons != null)
		{
			foreach (StatusBarWeaponDefinition weapon in definition.Weapons.Weapons)
			{
				string weaponKey = $"Weapon{weapon.Number}";
				_initValues[weaponKey] = weapon.Init;
				_values[weaponKey] = weapon.Init;
				_maxValues[weaponKey] = 1; // Weapons are binary (0 or 1)

				// Fire initial value event
				ValueChanged?.Invoke(weaponKey, weapon.Init);
			}
		}
	}

	/// <summary>
	/// Resets values that have LevelReset defined.
	/// Called when transitioning between levels.
	/// </summary>
	public void OnLevelChange()
	{
		foreach (KeyValuePair<string, int?> kvp in _levelResetValues)
		{
			if (kvp.Value.HasValue)
				SetValue(kvp.Key, kvp.Value.Value);
		}
	}

	/// <summary>
	/// Resets all values to their initial state.
	/// Called when starting a new game or dying.
	/// </summary>
	public void Reset()
	{
		foreach (KeyValuePair<string, int> kvp in _initValues)
			SetValue(kvp.Key, kvp.Value);
	}

	/// <summary>
	/// Captures the current inventory state for preservation across level transitions
	/// and save games. Returns a snapshot of all current values.
	/// </summary>
	/// <returns>Dictionary containing all current inventory values</returns>
	public Dictionary<string, int> SaveState() =>
		new(_values);

	/// <summary>
	/// Restores inventory state from a previously captured snapshot.
	/// Used during level transitions and save game loading to preserve player state.
	/// </summary>
	/// <param name="savedState">Dictionary containing saved inventory values</param>
	public void LoadState(Dictionary<string, int> savedState)
	{
		if (savedState == null)
			return;

		foreach (KeyValuePair<string, int> kvp in savedState)
			SetValue(kvp.Key, kvp.Value);
	}
}
