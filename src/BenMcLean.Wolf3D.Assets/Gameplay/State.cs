using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace BenMcLean.Wolf3D.Assets.Gameplay;

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
	public static State FromXElement(XElement element, Func<string, short> spriteResolver = null)
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
		string shapeAttr = element.Attribute("Shape")?.Value;
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
		string nextAttr = element.Attribute("Next")?.Value;
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
	/// The function body - Lua script code stored as a string.
	/// Will be compiled to bytecode by Simulator at startup.
	/// </summary>
	public string Code { get; set; }
	/// <summary>
	/// Optional description/comment for this function
	/// </summary>
	public string Description { get; set; }
	/// <summary>
	/// Creates a StateFunction instance from an XElement.
	/// </summary>
	/// <param name="element">The XElement containing function data (either &lt;Function&gt; or &lt;ThinkFunction&gt;/&lt;ActionFunction&gt;)</param>
	/// <returns>A new StateFunction instance</returns>
	public static StateFunction FromXElement(XElement element)
	{
		string name = element.Attribute("Name")?.Value ?? throw new ArgumentException($"{element.Name.LocalName} element must have a Name attribute");
		string code = element.Value?.Trim() ?? string.Empty;
		string description = element.Attribute("Description")?.Value;

		return new StateFunction
		{
			Name = name,
			Code = code,
			Description = description
		};
	}
}
/// <summary>
/// Stores actor class metadata loaded from XML Actor elements.
/// Used to look up death states, chase states, etc. by actor type name.
/// </summary>
public class ActorDefinition
{
	/// <summary>
	/// Actor type name (e.g., "Guard", "Dog", "SS").
	/// Maps to ActorSpawn.ActorType from map analysis.
	/// </summary>
	public string Name { get; set; }
	/// <summary>
	/// State name to transition to when actor dies (e.g., "s_grddie1").
	/// </summary>
	public string DeathState { get; set; }
	/// <summary>
	/// State name for chase behavior (e.g., "s_grdchase1").
	/// </summary>
	public string ChaseState { get; set; }
	/// <summary>
	/// State name for attack behavior (e.g., "s_grdshoot1").
	/// </summary>
	public string AttackState { get; set; }
	/// <summary>
	/// Alert sound to play when actor spots player (e.g., "HALTSND").
	/// </summary>
	public string AlertDigiSound { get; set; }
	/// <summary>
	/// Hit points by difficulty level, parsed from HP="25,25,25,25" attribute.
	/// Index is 0-based difficulty (e.g., 0=Can I Play Daddy?, 3=Death Incarnate).
	/// </summary>
	public short[] HitPointsByDifficulty { get; set; }
	/// <summary>
	/// Pain state name (e.g., "s_grdpain"). Used when hit but not killed.
	/// Alternates with PainState1 based on HP parity.
	/// Bosses may have no pain state (null).
	/// </summary>
	public string PainState { get; set; }
	/// <summary>
	/// Alternate pain state name (e.g., "s_grdpain1").
	/// Used when HP is even after damage. Null if only one pain sprite.
	/// </summary>
	public string PainState1 { get; set; }
	/// <summary>
	/// Initial standing state name (e.g., "s_grdstand").
	/// Used by Simulator to spawn actors without hardcoded state dictionaries.
	/// </summary>
	public string InitialState { get; set; }
	/// <summary>
	/// Returns hit points for the given difficulty level.
	/// No clamping — throws IndexOutOfRangeException on bad index (informative crash).
	/// </summary>
	public short GetHitPoints(int difficulty) => HitPointsByDifficulty[difficulty];
	/// <summary>
	/// Creates an ActorDefinition from an XElement.
	/// </summary>
	public static ActorDefinition FromXElement(XElement element)
	{
		string hpAttr = element.Attribute("HP")?.Value;
		short[] hitPoints = null;
		if (!string.IsNullOrEmpty(hpAttr))
			hitPoints = hpAttr.Split(',').Select(s => short.Parse(s.Trim())).ToArray();

		return new ActorDefinition
		{
			Name = element.Attribute("Name")?.Value ?? throw new ArgumentException("Actor element must have a Name attribute"),
			DeathState = element.Attribute("Death")?.Value,
			ChaseState = element.Attribute("Chase")?.Value,
			AttackState = element.Attribute("Attack")?.Value,
			AlertDigiSound = element.Attribute("AlertDigiSound")?.Value,
			HitPointsByDifficulty = hitPoints,
			PainState = element.Attribute("Pain")?.Value,
			PainState1 = element.Attribute("Pain1")?.Value,
			InitialState = element.Attribute("Stand")?.Value
		};
	}
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
	/// Actor class definitions, indexed by actor type name (e.g., "Guard", "Dog").
	/// Used to look up death states, chase states, etc.
	/// </summary>
	public Dictionary<string, ActorDefinition> ActorDefinitions { get; set; } = [];
	/// <summary>
	/// Adds a state function to the collection.
	/// </summary>
	/// <param name="function">The StateFunction to add</param>
	public void AddFunction(StateFunction function)
	{
		if (Functions.ContainsKey(function.Name))
			throw new InvalidOperationException($"Duplicate state function name: '{function.Name}'");
		Functions[function.Name] = function;
	}
	/// <summary>
	/// Adds a state function from an XElement.
	/// </summary>
	/// <param name="element">The XElement containing function data</param>
	public void AddFunctionFromXml(XElement element)
	{
		StateFunction function = StateFunction.FromXElement(element);
		AddFunction(function);
	}
	/// <summary>
	/// Loads multiple state functions from XML elements.
	/// </summary>
	/// <param name="elements">Collection of XElements representing functions</param>
	public void LoadFunctionsFromXml(IEnumerable<XElement> elements)
	{
		foreach (XElement element in elements)
			AddFunctionFromXml(element);
	}
	/// <summary>
	/// Adds a state from an XElement.
	/// Use this in Phase 1, then call LinkStates() for Phase 2.
	/// Automatically registers any inline ThinkFunction or ActionFunction elements.
	/// </summary>
	/// <param name="element">The XElement containing state data</param>
	/// <param name="spriteResolver">Optional function to resolve sprite names to numbers</param>
	public void AddStateFromXml(XElement element, Func<string, short> spriteResolver = null)
	{
		// Check for inline ThinkFunction and ActionFunction elements
		XElement thinkFunctionElement = element.Element("ThinkFunction");
		XElement actionFunctionElement = element.Element("ActionFunction");

		// Register inline functions if they exist
		if (thinkFunctionElement != null)
		{
			StateFunction thinkFunc = StateFunction.FromXElement(thinkFunctionElement);
			AddFunction(thinkFunc);
		}
		if (actionFunctionElement != null)
		{
			StateFunction actionFunc = StateFunction.FromXElement(actionFunctionElement);
			AddFunction(actionFunc);
		}

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
	public void LoadStatesFromXml(IEnumerable<XElement> elements, Func<string, short> spriteResolver = null)
	{
		foreach (XElement element in elements)
			AddStateFromXml(element, spriteResolver);
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
				if (States.TryGetValue(state.NextStateName, out State nextState))
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
	/// <summary>
	/// Validates that all Think and Action function references in states exist in the Functions collection.
	/// Call this after loading all states and functions from XML (Phase 3).
	/// </summary>
	public void ValidateFunctionReferences()
	{
		// TODO: In final release, this should throw hard errors with helpful messages for modders.
		// For now, just log warnings to allow testing with incomplete function sets.
		// Final behavior should be:
		//   throw new InvalidOperationException($"State '{state.Name}' references unknown Think function '{state.Think}'. Please add a <Function Name=\"{state.Think}\"> element to define this function.");

		foreach (State state in States.Values)
		{
			// Validate Think function reference if present
			if (!string.IsNullOrEmpty(state.Think))
			{
				if (!Functions.ContainsKey(state.Think))
				{
					// TODO: Restore hard error for final release (see method summary)
					System.Diagnostics.Debug.WriteLine($"WARNING: State '{state.Name}' references unknown Think function '{state.Think}' - function will be skipped during execution");
				}
			}

			// Validate Action function reference if present
			if (!string.IsNullOrEmpty(state.Action))
			{
				if (!Functions.ContainsKey(state.Action))
				{
					// TODO: Restore hard error for final release (see method summary)
					System.Diagnostics.Debug.WriteLine($"WARNING: State '{state.Name}' references unknown Action function '{state.Action}' - function will be skipped during execution");
				}
			}
		}
	}
	/// <summary>
	/// Loads actor definitions from XML Actor elements.
	/// </summary>
	/// <param name="elements">Collection of Actor XElements</param>
	public void LoadActorDefinitionsFromXml(IEnumerable<XElement> elements)
	{
		foreach (XElement element in elements)
		{
			ActorDefinition actorDef = ActorDefinition.FromXElement(element);
			ActorDefinitions[actorDef.Name] = actorDef;
		}
	}
}
