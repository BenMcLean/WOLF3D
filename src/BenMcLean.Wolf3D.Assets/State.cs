using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace BenMcLean.Wolf3D.Assets;

/// <summary>
/// Represents a state in the Wolfenstein 3D state machine.
/// Based on WL_DEF.H:statestruct
/// States are used for enemies, weapons, player animations, and other game objects.
/// This class only stores data - actual execution happens elsewhere.
/// </summary>
public class State
{
	/// <summary>
	/// Unique identifier for this state (e.g., "s_grdstand", "s_knife1")
	/// </summary>
	public string Name { get; set; }
	/// <summary>
	/// WL_DEF.H:statestruct:rotate (original: boolean)
	/// Whether the sprite should rotate to face the player (8-directional sprites)
	/// </summary>
	public bool Rotate { get; set; } = false;
	/// <summary>
	/// WL_DEF.H:statestruct:shapenum (original: int = 16-bit)
	/// Sprite/shape number to display in this state.
	/// Can be -1 to indicate dynamic sprite selection.
	/// </summary>
	public short Shape { get; set; }
	/// <summary>
	/// WL_DEF.H:statestruct:tictime (original: int = 16-bit)
	/// Duration of this state in tics (1/70th of a second).
	/// 0 means the state lasts indefinitely until explicitly changed.
	/// </summary>
	public short Tics { get; set; }
	/// <summary>
	/// WL_DEF.H:statestruct:think (original: void (*think)())
	/// Name/reference to the Think function.
	/// Think is called every frame while in this state.
	/// Will eventually be a Lua script name or inline Lua code.
	/// </summary>
	public string Think { get; set; }
	/// <summary>
	/// WL_DEF.H:statestruct:action (original: void (*action)())
	/// Name/reference to the Action function.
	/// Action is called once when entering this state.
	/// Will eventually be a Lua script name or inline Lua code.
	/// </summary>
	public string Action { get; set; }
	/// <summary>
	/// Name of the next state to transition to.
	/// The actual State reference will be resolved after all states are loaded.
	/// </summary>
	public string NextStateName { get; set; }
	/// <summary>
	/// WL_DEF.H:statestruct:next (original: struct statestruct*)
	/// Resolved reference to the next state (set during post-load linking phase)
	/// </summary>
	public State Next { get; set; }
	/// <summary>
	/// Optional speed parameter used by some states (e.g., enemy movement speed)
	/// Uses fixed-point 16.16 integer arithmetic (original: long = 32-bit)
	/// </summary>
	public int Speed { get; set; } = 0;
	/// <summary>
	/// Additional custom properties that can be defined in XML.
	/// Allows for extensibility without modifying the core State class.
	/// </summary>
	public Dictionary<string, string> CustomProperties { get; set; } = [];
	/// <summary>
	/// Creates a State instance from an XElement.
	/// Phase 1: Parse all attributes and set Next to this if referencing self.
	/// Phase 2: Use StateCollection.LinkStates() to resolve Next references.
	/// </summary>
	/// <param name="element">The XElement containing state data</param>
	/// <param name="spriteResolver">Optional function to resolve sprite names to numbers. If null, Shape will be set to -1.</param>
	/// <returns>A new State instance</returns>
	public static State FromXElement(XElement element, Func<string, short>? spriteResolver = null)
	{
		State state = new()
		{
			Name = element.Attribute("Name")?.Value ?? throw new ArgumentException("State element must have a Name attribute"),
			Rotate = bool.TryParse(element.Attribute("Rotate")?.Value, out bool rotate) && rotate,
			Tics = short.TryParse(element.Attribute("Tics")?.Value, out short tics) ? tics : (short)0,
			Think = element.Attribute("Think")?.Value,
			Action = element.Attribute("Action")?.Value,
			Speed = int.TryParse(element.Attribute("Speed")?.Value, out int speed) ? speed : 0
		};

		// Handle Shape attribute - can be either a number or a sprite name
		string? shapeAttr = element.Attribute("Shape")?.Value;
		if (!string.IsNullOrEmpty(shapeAttr))
		{
			if (short.TryParse(shapeAttr, out short shapeNum))
			{
				// Direct numeric value
				state.Shape = shapeNum;
			}
			else if (spriteResolver != null)
			{
				// Sprite name - resolve using provided function
				state.Shape = spriteResolver(shapeAttr);
			}
			else
			{
				// No resolver provided, store in custom properties
				state.Shape = -1;
				state.CustomProperties["ShapeName"] = shapeAttr;
			}
		}
		else
		{
			state.Shape = -1;
		}

		// Handle Next attribute - store name for linking phase
		string? nextAttr = element.Attribute("Next")?.Value;
		if (!string.IsNullOrEmpty(nextAttr))
		{
			state.NextStateName = nextAttr;
			// Phase 1: Set Next to this if it references itself
			if (nextAttr == state.Name)
			{
				state.Next = state;
			}
			// Otherwise, Next will be resolved in LinkStates() (phase 2)
		}
		else
		{
			// No next state specified - loop to self (common pattern in original code)
			state.Next = state;
		}

		// Store any additional attributes as custom properties
		foreach (XAttribute attr in element.Attributes())
		{
			string attrName = attr.Name.LocalName;
			// Skip standard attributes we've already processed
			if (attrName is not ("Name" or "Rotate" or "Shape" or "Tics" or "Think" or "Action" or "Speed" or "Next"))
			{
				state.CustomProperties[attrName] = attr.Value;
			}
		}

		return state;
	}
}
/// <summary>
/// Represents a reusable state function (Think or Action).
/// Allows DRY principle by defining functions once and referencing them by name.
/// </summary>
public class StateFunction
{
	/// <summary>
	/// Unique identifier for this function (e.g., "T_Stand", "A_DeathScream")
	/// </summary>
	public string Name { get; set; }
	/// <summary>
	/// The function body - will eventually be Lua script code.
	/// For now, just stored as a string.
	/// </summary>
	public string Code { get; set; }
	/// <summary>
	/// Optional description/comment for this function
	/// </summary>
	public string Description { get; set; }
}
/// <summary>
/// Container for all state-related data loaded from XML.
/// </summary>
public class StateCollection
{
	/// <summary>
	/// All states, indexed by name for fast lookup
	/// </summary>
	public Dictionary<string, State> States { get; set; } = [];
	/// <summary>
	/// All reusable state functions, indexed by name
	/// </summary>
	public Dictionary<string, StateFunction> Functions { get; set; } = [];
	/// <summary>
	/// Adds a state from an XElement.
	/// Use this in Phase 1, then call LinkStates() for Phase 2.
	/// </summary>
	/// <param name="element">The XElement containing state data</param>
	/// <param name="spriteResolver">Optional function to resolve sprite names to numbers</param>
	public void AddStateFromXml(XElement element, Func<string, short>? spriteResolver = null)
	{
		State state = State.FromXElement(element, spriteResolver);
		States[state.Name] = state;
	}

	/// <summary>
	/// Loads multiple states from XML elements.
	/// Phase 1: Creates all state objects.
	/// Phase 2: Call LinkStates() to resolve Next references.
	/// </summary>
	/// <param name="elements">Collection of XElements representing states</param>
	/// <param name="spriteResolver">Optional function to resolve sprite names to numbers</param>
	public void LoadStatesFromXml(IEnumerable<XElement> elements, Func<string, short>? spriteResolver = null)
	{
		foreach (XElement element in elements)
		{
			AddStateFromXml(element, spriteResolver);
		}
	}

	/// <summary>
	/// Resolves NextStateName references to actual State objects.
	/// Call this after loading all states from XML (Phase 2).
	/// </summary>
	public void LinkStates()
	{
		foreach (State state in States.Values)
		{
			// Skip if Next is already set to self (handled in FromXElement phase 1)
			if (state.Next == state)
				continue;

			if (!string.IsNullOrEmpty(state.NextStateName))
			{
				if (States.TryGetValue(state.NextStateName, out State? nextState))
					state.Next = nextState;
				else
					throw new InvalidOperationException($"State '{state.Name}' references unknown next state '{state.NextStateName}'");
			}
			else
			{
				// If no next state specified, loop to self (common pattern in original code)
				state.Next = state;
			}
		}
	}
}
