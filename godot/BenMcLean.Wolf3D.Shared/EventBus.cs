using Godot;
using System;
using System.Collections.Generic;

namespace BenMcLean.Wolf3D.Shared;

/// <summary>
/// Types of events that can be emitted through the event bus.
/// </summary>
public enum GameEvent
{
	// Audio Events
	PlaySound,
	PlayMusic,
	StopMusic,
	// Future: Add other event types as needed
	// e.g., LoadLevel, ShowDialog, TransitionScene, etc.
}

/// <summary>
/// Global event bus for decoupled communication between systems.
/// Provides type-safe event emission and subscription using enums.
/// </summary>
/// <remarks>
/// Usage:
/// - Emit events: EventBus.Emit(GameEvent.PlaySound, "HITWALLSND");
/// - Subscribe: EventBus.Subscribe(GameEvent.PlaySound, OnPlaySound);
/// - Unsubscribe: EventBus.Unsubscribe(GameEvent.PlaySound, OnPlaySound);
/// </remarks>
public static class EventBus
{
	/// <summary>
	/// Registered event handlers for each event type.
	/// </summary>
	private static readonly Dictionary<GameEvent, List<Action<object>>> _handlers = [];
	/// <summary>
	/// Subscribes to an event type.
	/// </summary>
	/// <param name="eventType">The type of event to subscribe to</param>
	/// <param name="handler">Callback to invoke when event is emitted. Parameter is event-specific data.</param>
	public static void Subscribe(GameEvent eventType, Action<object> handler)
	{
		if (!_handlers.ContainsKey(eventType))
			_handlers[eventType] = [];
		if (!_handlers[eventType].Contains(handler))
			_handlers[eventType].Add(handler);
	}
	/// <summary>
	/// Unsubscribes from an event type.
	/// </summary>
	/// <param name="eventType">The type of event to unsubscribe from</param>
	/// <param name="handler">The callback to remove</param>
	public static void Unsubscribe(GameEvent eventType, Action<object> handler)
	{
		if (_handlers.ContainsKey(eventType))
			_handlers[eventType].Remove(handler);
	}
	/// <summary>
	/// Emits an event with optional data.
	/// </summary>
	/// <param name="eventType">The type of event to emit</param>
	/// <param name="data">Optional event-specific data (e.g., sound name string)</param>
	public static void Emit(GameEvent eventType, object data = null)
	{
		if (!_handlers.ContainsKey(eventType))
			return;
		// Create a copy to avoid issues if handlers modify the list during iteration
		List<Action<object>> handlersCopy = [.. _handlers[eventType]];
		foreach (Action<object> handler in handlersCopy)
		{
			try
			{
				handler?.Invoke(data);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"ERROR: Exception in event handler for {eventType}: {ex.Message}");
				GD.PrintErr(ex.StackTrace);
			}
		}
	}
	/// <summary>
	/// Clears all event subscriptions. Use with caution!
	/// </summary>
	public static void ClearAll()
	{
		_handlers.Clear();
	}
	/// <summary>
	/// Gets the number of handlers registered for an event type.
	/// Useful for debugging.
	/// </summary>
	public static int GetHandlerCount(GameEvent eventType) =>
		_handlers.ContainsKey(eventType) ? _handlers[eventType].Count : 0;
}
