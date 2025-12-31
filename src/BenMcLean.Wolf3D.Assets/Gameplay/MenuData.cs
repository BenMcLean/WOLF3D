using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace BenMcLean.Wolf3D.Assets.Gameplay;

/// <summary>
/// Represents a menu function (Lua script) for menu behavior.
/// Similar to StateFunction but for menus instead of Actor AI.
/// </summary>
public class MenuFunction
{
	/// <summary>
	/// Unique identifier for this function (e.g., "OnNewGame", "OnSelectEpisode1")
	/// </summary>
	public string Name { get; set; }
	/// <summary>
	/// The function body - Lua script code stored as a string.
	/// Will be compiled to bytecode by MenuManager at startup.
	/// </summary>
	public string Code { get; set; }
	/// <summary>
	/// Optional description/comment for this function
	/// </summary>
	public string Description { get; set; }
	/// <summary>
	/// Creates a MenuFunction instance from an XElement.
	/// </summary>
	/// <param name="element">The XElement containing function data (&lt;MenuFunction&gt;)</param>
	/// <returns>A new MenuFunction instance</returns>
	public static MenuFunction FromXElement(XElement element)
	{
		string name = element.Attribute("Name")?.Value ?? throw new ArgumentException("MenuFunction element must have a Name attribute");
		string code = element.Value?.Trim() ?? string.Empty;
		string description = element.Attribute("Description")?.Value;

		return new MenuFunction
		{
			Name = name,
			Code = code,
			Description = description
		};
	}
}

/// <summary>
/// Represents a single menu item within a menu.
/// Maps to a &lt;MenuItem&gt; element in XML.
/// </summary>
public class MenuItemDefinition
{
	/// <summary>
	/// Display text for this menu item
	/// </summary>
	public string Text { get; set; }
	/// <summary>
	/// Action to execute when this item is selected.
	/// References a MenuFunction by name, or a built-in action.
	/// </summary>
	public string Action { get; set; }
	/// <summary>
	/// Optional argument passed to the action (e.g., menu name for navigation)
	/// </summary>
	public string Argument { get; set; }
	/// <summary>
	/// Optional condition for visibility/enabled state.
	/// Can reference Lua expressions or state flags.
	/// </summary>
	public string Condition { get; set; }
	/// <summary>
	/// Additional custom properties that can be defined in XML.
	/// Allows for extensibility without modifying the core class.
	/// </summary>
	public Dictionary<string, string> CustomProperties { get; set; } = [];
	/// <summary>
	/// Creates a MenuItemDefinition instance from an XElement.
	/// </summary>
	/// <param name="element">The XElement containing menu item data (&lt;MenuItem&gt;)</param>
	/// <returns>A new MenuItemDefinition instance</returns>
	public static MenuItemDefinition FromXElement(XElement element)
	{
		MenuItemDefinition item = new()
		{
			Text = element.Attribute("Text")?.Value ?? string.Empty,
			Action = element.Attribute("Action")?.Value,
			Argument = element.Attribute("Argument")?.Value,
			Condition = element.Attribute("Condition")?.Value ?? element.Attribute("InGame")?.Value
		};

		// Store any additional attributes as custom properties
		foreach (XAttribute attr in element.Attributes())
		{
			string attrName = attr.Name.LocalName;
			// Skip standard attributes we've already processed
			if (attrName is not ("Text" or "Action" or "Argument" or "Condition" or "InGame"))
			{
				item.CustomProperties[attrName] = attr.Value;
			}
		}

		return item;
	}
}

/// <summary>
/// Represents a menu screen definition.
/// Maps to a &lt;Menu&gt; element in XML.
/// </summary>
public class MenuDefinition
{
	/// <summary>
	/// Unique identifier for this menu (e.g., "Main", "Episodes", "Sound")
	/// </summary>
	public string Name { get; set; }
	/// <summary>
	/// Background image name from VgaGraph (e.g., "C_OPTIONSPIC", "TITLEPIC")
	/// </summary>
	public string Background { get; set; }
	/// <summary>
	/// Background color index (VGA palette 0-255)
	/// </summary>
	public byte? BackgroundColor { get; set; }
	/// <summary>
	/// Font name to use for this menu (e.g., "BIG", "SMALL")
	/// </summary>
	public string Font { get; set; }
	/// <summary>
	/// Music track name to play while in this menu
	/// </summary>
	public string Music { get; set; }
	/// <summary>
	/// List of menu items in display order
	/// </summary>
	public List<MenuItemDefinition> Items { get; set; } = [];
	/// <summary>
	/// Additional custom properties that can be defined in XML.
	/// Allows for extensibility without modifying the core class.
	/// </summary>
	public Dictionary<string, string> CustomProperties { get; set; } = [];
	/// <summary>
	/// Creates a MenuDefinition instance from an XElement.
	/// </summary>
	/// <param name="element">The XElement containing menu data (&lt;Menu&gt;)</param>
	/// <returns>A new MenuDefinition instance</returns>
	public static MenuDefinition FromXElement(XElement element)
	{
		MenuDefinition menu = new()
		{
			Name = element.Attribute("Name")?.Value ?? throw new ArgumentException("Menu element must have a Name attribute"),
			Background = element.Attribute("Background")?.Value,
			Font = element.Attribute("Font")?.Value,
			Music = element.Attribute("Music")?.Value ?? element.Attribute("Song")?.Value
		};

		// Parse BackgroundColor if present
		string bgColorAttr = element.Attribute("BackgroundColor")?.Value ?? element.Attribute("BkgdColor")?.Value;
		if (!string.IsNullOrEmpty(bgColorAttr) && byte.TryParse(bgColorAttr, out byte bgColor))
		{
			menu.BackgroundColor = bgColor;
		}

		// Parse menu items
		IEnumerable<XElement> menuItemElements = element.Elements("MenuItem");
		if (menuItemElements != null)
		{
			menu.Items = [.. menuItemElements.Select(MenuItemDefinition.FromXElement)];
		}

		// Store any additional attributes as custom properties
		foreach (XAttribute attr in element.Attributes())
		{
			string attrName = attr.Name.LocalName;
			// Skip standard attributes we've already processed
			if (attrName is not ("Name" or "Background" or "BackgroundColor" or "BkgdColor" or "Font" or "Music" or "Song"))
			{
				menu.CustomProperties[attrName] = attr.Value;
			}
		}

		return menu;
	}
}

/// <summary>
/// Container for all menu-related data loaded from XML.
/// Follows the same pattern as StateCollection for consistency.
/// </summary>
public class MenuCollection
{
	/// <summary>
	/// All menus, indexed by name for fast lookup
	/// </summary>
	public Dictionary<string, MenuDefinition> Menus { get; set; } = [];
	/// <summary>
	/// All menu functions (Lua scripts), indexed by name
	/// </summary>
	public Dictionary<string, MenuFunction> Functions { get; set; } = [];
	/// <summary>
	/// Name of the initial/starting menu (e.g., "Intro")
	/// </summary>
	public string StartMenu { get; set; }
	/// <summary>
	/// Adds a menu function to the collection.
	/// </summary>
	/// <param name="function">The MenuFunction to add</param>
	public void AddFunction(MenuFunction function)
	{
		if (function == null)
			throw new ArgumentNullException(nameof(function));
		if (string.IsNullOrEmpty(function.Name))
			throw new ArgumentException("MenuFunction must have a non-empty Name");

		Functions[function.Name] = function;
	}
	/// <summary>
	/// Adds a menu to the collection.
	/// </summary>
	/// <param name="menu">The MenuDefinition to add</param>
	public void AddMenu(MenuDefinition menu)
	{
		if (menu == null)
			throw new ArgumentNullException(nameof(menu));
		if (string.IsNullOrEmpty(menu.Name))
			throw new ArgumentException("MenuDefinition must have a non-empty Name");

		Menus[menu.Name] = menu;
	}
	/// <summary>
	/// Loads menu functions from XML elements.
	/// </summary>
	/// <param name="functionElements">Collection of &lt;MenuFunction&gt; elements</param>
	public void LoadFunctionsFromXml(IEnumerable<XElement> functionElements)
	{
		if (functionElements == null)
			return;

		foreach (XElement element in functionElements)
		{
			MenuFunction function = MenuFunction.FromXElement(element);
			AddFunction(function);
		}
	}
	/// <summary>
	/// Loads menus from XML elements.
	/// </summary>
	/// <param name="menuElements">Collection of &lt;Menu&gt; elements</param>
	public void LoadMenusFromXml(IEnumerable<XElement> menuElements)
	{
		if (menuElements == null)
			return;

		foreach (XElement element in menuElements)
		{
			MenuDefinition menu = MenuDefinition.FromXElement(element);
			AddMenu(menu);
		}
	}
	/// <summary>
	/// Validates that all menu item actions reference valid functions.
	/// Should be called after all menus and functions are loaded.
	/// </summary>
	public void ValidateFunctionReferences()
	{
		foreach (MenuDefinition menu in Menus.Values)
		{
			foreach (MenuItemDefinition item in menu.Items)
			{
				// Skip validation for built-in actions or items without actions
				if (string.IsNullOrEmpty(item.Action))
					continue;

				// Built-in actions that don't require function definitions
				if (item.Action is "NavigateToMenu" or "BackToPreviousMenu" or "CloseAllMenus" or "Resume" or "Quit")
					continue;

				// Check if the action references a valid function
				if (!Functions.ContainsKey(item.Action))
				{
					throw new InvalidOperationException($"Menu '{menu.Name}' item '{item.Text}' references unknown function '{item.Action}'");
				}
			}
		}
	}
	/// <summary>
	/// Loads a complete MenuCollection from a &lt;Menus&gt; XML element.
	/// </summary>
	/// <param name="menusElement">The &lt;Menus&gt; root element</param>
	/// <returns>A populated MenuCollection</returns>
	public static MenuCollection Load(XElement menusElement)
	{
		if (menusElement == null)
			return new MenuCollection(); // Return empty collection if no menus defined

		MenuCollection collection = new()
		{
			StartMenu = menusElement.Attribute("Start")?.Value
		};

		// Load menu functions first
		IEnumerable<XElement> functionElements = menusElement.Elements("MenuFunction");
		collection.LoadFunctionsFromXml(functionElements);

		// Load menu definitions
		IEnumerable<XElement> menuElements = menusElement.Elements("Menu");
		collection.LoadMenusFromXml(menuElements);

		// Validate function references
		collection.ValidateFunctionReferences();

		return collection;
	}
}
