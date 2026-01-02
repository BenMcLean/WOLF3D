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
/// Represents a 3D beveled box in a menu.
/// Maps to a &lt;Box&gt; element in XML.
/// WL_MENU.C:DrawWindow - draws a filled box with 3D outline
/// </summary>
public class MenuBoxDefinition
{
	/// <summary>
	/// X coordinate for box (WL_MENU.C:DrawWindow - MENU_X-8)
	/// </summary>
	public int X { get; set; }
	/// <summary>
	/// Y coordinate for box (WL_MENU.C:DrawWindow - MENU_Y-3)
	/// </summary>
	public int Y { get; set; }
	/// <summary>
	/// Width of box (WL_MENU.H:MENU_W = 178)
	/// </summary>
	public int W { get; set; }
	/// <summary>
	/// Height of box (WL_MENU.H:MENU_H = 13*10+6 = 136)
	/// </summary>
	public int H { get; set; }
	/// <summary>
	/// Background color index for box fill (WL_MENU.H:BKGDCOLOR = 0x2d)
	/// </summary>
	public byte? BackgroundColor { get; set; }
	/// <summary>
	/// Top/left border color (WL_MENU.H:DEACTIVE = 0x2b)
	/// </summary>
	public byte? Deactive { get; set; }
	/// <summary>
	/// Bottom/right border color (WL_MENU.H:BORD2COLOR = 0x23)
	/// </summary>
	public byte? Border2Color { get; set; }
	/// <summary>
	/// Creates a MenuBoxDefinition instance from an XElement.
	/// </summary>
	/// <param name="element">The XElement containing box data (&lt;Box&gt;)</param>
	/// <returns>A new MenuBoxDefinition instance</returns>
	public static MenuBoxDefinition FromXElement(XElement element)
	{
		MenuBoxDefinition box = new()
		{
			X = int.TryParse(element.Attribute("X")?.Value, out int x) ? x : 0,
			Y = int.TryParse(element.Attribute("Y")?.Value, out int y) ? y : 0,
			W = int.TryParse(element.Attribute("W")?.Value, out int w) ? w : 0,
			H = int.TryParse(element.Attribute("H")?.Value, out int h) ? h : 0
		};

		// Parse colors using semantic attribute names from WL_MENU.H
		if (byte.TryParse(element.Attribute("BackgroundColor")?.Value, out byte bgColor))
			box.BackgroundColor = bgColor;
		if (byte.TryParse(element.Attribute("Deactive")?.Value, out byte deactive))
			box.Deactive = deactive;
		if (byte.TryParse(element.Attribute("Border2Color")?.Value, out byte border2))
			box.Border2Color = border2;

		return box;
	}
}

/// <summary>
/// Represents a decorative picture in a menu.
/// Maps to a &lt;Picture&gt; element in XML.
/// </summary>
public class MenuPictureDefinition
{
	/// <summary>
	/// Name of the VgaGraph image (e.g., "C_MOUSELBACKPIC")
	/// </summary>
	public string Name { get; set; }
	/// <summary>
	/// X coordinate for picture placement
	/// </summary>
	public int X { get; set; }
	/// <summary>
	/// Y coordinate for picture placement
	/// </summary>
	public int Y { get; set; }
	/// <summary>
	/// If true, renders the leftmost column of pixels stretched horizontally across the screen
	/// before rendering the normal picture. Used for decorative stripe backgrounds.
	/// </summary>
	public bool Stripes { get; set; }
	/// <summary>
	/// Creates a MenuPictureDefinition instance from an XElement.
	/// </summary>
	/// <param name="element">The XElement containing picture data (&lt;Picture&gt;)</param>
	/// <returns>A new MenuPictureDefinition instance</returns>
	public static MenuPictureDefinition FromXElement(XElement element)
	{
		return new MenuPictureDefinition
		{
			Name = element.Attribute("Name")?.Value ?? throw new ArgumentException("Picture element must have a Name attribute"),
			X = int.TryParse(element.Attribute("X")?.Value, out int x) ? x : 0,
			Y = int.TryParse(element.Attribute("Y")?.Value, out int y) ? y : 0,
			Stripes = bool.TryParse(element.Attribute("Stripes")?.Value, out bool stripes) && stripes
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
	/// Inline Lua script to execute when this item is selected.
	/// Stored as element content in XML, executed via DoString (not pre-cached).
	/// </summary>
	public string Script { get; set; }
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
			Script = element.Value?.Trim(),
			Condition = element.Attribute("Condition")?.Value ?? element.Attribute("InGame")?.Value
		};

		// Store any additional attributes as custom properties
		foreach (XAttribute attr in element.Attributes())
		{
			string attrName = attr.Name.LocalName;
			// Skip standard attributes we've already processed
			if (attrName is not ("Text" or "Condition" or "InGame"))
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
	/// X coordinate for background image (WL_MENU.C: VWB_DrawPic(84,0,C_OPTIONSPIC))
	/// Default is 0 for full-screen backgrounds like TITLEPIC
	/// </summary>
	public int? BackgroundX { get; set; }
	/// <summary>
	/// Y coordinate for background image
	/// Default is 0
	/// </summary>
	public int? BackgroundY { get; set; }
	/// <summary>
	/// Background color index (VGA palette 0-255) - WL_MENU.H:BORDCOLOR = 0x29
	/// </summary>
	public byte? BorderColor { get; set; }
	/// <summary>
	/// Normal (unselected) menu item text color (WL_MENU.H:TEXTCOLOR = 0x17)
	/// </summary>
	public byte? TextColor { get; set; }
	/// <summary>
	/// Selected menu item text color (WL_MENU.H:HIGHLIGHT = 0x13)
	/// </summary>
	public byte? Highlight { get; set; }
	/// <summary>
	/// Font name to use for this menu (e.g., "BIG", "SMALL")
	/// </summary>
	public string Font { get; set; }
	/// <summary>
	/// Music track name to play while in this menu
	/// </summary>
	public string Music { get; set; }
	/// <summary>
	/// Sound to play when cursor moves to a different menu item (e.g., "MOVEGUN2SND")
	/// </summary>
	public string CursorMoveSound { get; set; }
	/// <summary>
	/// X coordinate for menu items (matches original CP_iteminfo.x)
	/// </summary>
	public int? X { get; set; }
	/// <summary>
	/// Y coordinate for first menu item (matches original CP_iteminfo.y)
	/// </summary>
	public int? Y { get; set; }
	/// <summary>
	/// Horizontal indent for menu text (matches original CP_iteminfo.indent)
	/// </summary>
	public int? Indent { get; set; }
	/// <summary>
	/// Vertical spacing between menu items in pixels (original uses 13)
	/// </summary>
	public int? Spacing { get; set; }
	/// <summary>
	/// Name of the cursor picture from VgaGraph (e.g., "C_CURSOR1PIC")
	/// If not specified, no cursor will be rendered.
	/// WL_MENU.C:DrawMenuGun uses C_CURSOR1PIC
	/// </summary>
	public string CursorPic { get; set; }
	/// <summary>
	/// 3D beveled boxes to display (WL_MENU.C:DrawWindow)
	/// </summary>
	public List<MenuBoxDefinition> Boxes { get; set; } = [];
	/// <summary>
	/// Decorative pictures to display (e.g., logos, title graphics)
	/// </summary>
	public List<MenuPictureDefinition> Pictures { get; set; } = [];
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
			Music = element.Attribute("Music")?.Value ?? element.Attribute("Song")?.Value,
			CursorPic = element.Attribute("CursorPic")?.Value,
			CursorMoveSound = element.Attribute("CursorMoveSound")?.Value
		};

		// Parse color attributes using semantic names from WL_MENU.H
		if (byte.TryParse(element.Attribute("BorderColor")?.Value, out byte borderColor))
			menu.BorderColor = borderColor;
		if (byte.TryParse(element.Attribute("TextColor")?.Value, out byte textColor))
			menu.TextColor = textColor;
		if (byte.TryParse(element.Attribute("Highlight")?.Value, out byte highlight))
			menu.Highlight = highlight;

		// Parse background position (for images like C_OPTIONSPIC at (84,0))
		if (int.TryParse(element.Attribute("BackgroundX")?.Value, out int bgX))
			menu.BackgroundX = bgX;
		if (int.TryParse(element.Attribute("BackgroundY")?.Value, out int bgY))
			menu.BackgroundY = bgY;

		// Parse layout coordinates (X, Y, Indent, Spacing)
		if (int.TryParse(element.Attribute("X")?.Value, out int x))
			menu.X = x;
		if (int.TryParse(element.Attribute("Y")?.Value, out int y))
			menu.Y = y;
		if (int.TryParse(element.Attribute("Indent")?.Value, out int indent))
			menu.Indent = indent;
		if (int.TryParse(element.Attribute("Spacing")?.Value, out int spacing))
			menu.Spacing = spacing;

		// Parse boxes
		IEnumerable<XElement> boxElements = element.Elements("Box");
		if (boxElements != null)
			menu.Boxes = [.. boxElements.Select(MenuBoxDefinition.FromXElement)];

		// Parse decorative pictures
		IEnumerable<XElement> pictureElements = element.Elements("Picture");
		if (pictureElements != null)
			menu.Pictures = [.. pictureElements.Select(MenuPictureDefinition.FromXElement)];

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
			if (attrName is not ("Name" or "Background" or "BackgroundX" or "BackgroundY" or "BorderColor"
				or "TextColor" or "Highlight" or "Font" or "Music" or "Song" or "X" or "Y" or "Indent" or "Spacing" or "CursorPic" or "CursorMoveSound"))
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
	/// Default text color for menus (WL_MENU.H:TEXTCOLOR = 0x17 / 23)
	/// </summary>
	public byte? DefaultTextColor { get; set; }
	/// <summary>
	/// Default highlight color for selected items (WL_MENU.H:HIGHLIGHT = 0x13 / 19)
	/// </summary>
	public byte? DefaultHighlight { get; set; }
	/// <summary>
	/// Default border/background color for menus (WL_MENU.H:BORDCOLOR = 0x29 / 41)
	/// </summary>
	public byte? DefaultBorderColor { get; set; }
	/// <summary>
	/// Default background color for menu boxes (WL_MENU.H:BKGDCOLOR = 0x2d / 45)
	/// </summary>
	public byte? DefaultBoxBackgroundColor { get; set; }
	/// <summary>
	/// Default DEACTIVE color for box borders (WL_MENU.H:DEACTIVE = 0x2b / 43)
	/// </summary>
	public byte? DefaultDeactive { get; set; }
	/// <summary>
	/// Default BORD2COLOR for box borders (WL_MENU.H:BORD2COLOR = 0x23 / 35)
	/// </summary>
	public byte? DefaultBorder2Color { get; set; }
	/// <summary>
	/// Default sound to play when cursor moves to different menu item (e.g., "MOVEGUN2SND")
	/// </summary>
	public string DefaultCursorMoveSound { get; set; }
	/// <summary>
	/// Default music to play in menus (e.g., "WONDERIN_MUS")
	/// </summary>
	public string DefaultMusic { get; set; }
	/// <summary>
	/// Default cursor picture for menus (e.g., "C_CURSOR1PIC")
	/// </summary>
	public string DefaultCursorPic { get; set; }
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
	/// Applies default colors, sounds, and music from the collection to menus and boxes that don't specify their own.
	/// Note: Uses ??= operator, so empty string ("") is considered an explicit value and won't be overridden.
	/// This allows menus to explicitly disable features by setting attributes to empty strings.
	/// Should be called after all menus are loaded but before validation.
	/// </summary>
	private void ApplyDefaults()
	{
		foreach (MenuDefinition menu in Menus.Values)
		{
			// Apply default colors to menus
			menu.BorderColor ??= DefaultBorderColor;
			menu.TextColor ??= DefaultTextColor;
			menu.Highlight ??= DefaultHighlight;

			// Apply default sound, music, and cursor
			// Note: Empty string ("") won't be replaced - allows explicit "no cursor/sound/music"
			menu.CursorMoveSound ??= DefaultCursorMoveSound;
			menu.Music ??= DefaultMusic;
			menu.CursorPic ??= DefaultCursorPic;

			// Apply default colors to boxes
			foreach (MenuBoxDefinition box in menu.Boxes)
			{
				// Boxes use BKGDCOLOR for background, not BORDCOLOR
				box.BackgroundColor ??= DefaultBoxBackgroundColor;
				box.Deactive ??= DefaultDeactive;
				box.Border2Color ??= DefaultBorder2Color;
			}
		}
	}
	/// <summary>
	/// Validates menu data (currently a no-op since menu items use inline Lua scripts).
	/// Kept for potential future validation needs (e.g., syntax checking scripts).
	/// Should be called after all menus and functions are loaded.
	/// </summary>
	public void ValidateFunctionReferences()
	{
		// No validation needed - menu items use inline Lua scripts executed via DoString
		// Future: Could add Lua syntax validation here if needed
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
			StartMenu = menusElement.Attribute("Start")?.Value,
			DefaultCursorMoveSound = menusElement.Attribute("CursorMoveSound")?.Value,
			DefaultMusic = menusElement.Attribute("Music")?.Value,
			DefaultCursorPic = menusElement.Attribute("CursorPic")?.Value
		};

		// Parse default colors from <Menus> element (WL_MENU.H values)
		if (byte.TryParse(menusElement.Attribute("TextColor")?.Value, out byte textColor))
			collection.DefaultTextColor = textColor;
		if (byte.TryParse(menusElement.Attribute("Highlight")?.Value, out byte highlight))
			collection.DefaultHighlight = highlight;
		if (byte.TryParse(menusElement.Attribute("BorderColor")?.Value, out byte borderColor))
			collection.DefaultBorderColor = borderColor;
		if (byte.TryParse(menusElement.Attribute("BoxBackgroundColor")?.Value, out byte boxBgColor))
			collection.DefaultBoxBackgroundColor = boxBgColor;
		if (byte.TryParse(menusElement.Attribute("Deactive")?.Value, out byte deactive))
			collection.DefaultDeactive = deactive;
		if (byte.TryParse(menusElement.Attribute("Border2Color")?.Value, out byte border2))
			collection.DefaultBorder2Color = border2;

		// Load menu functions first
		IEnumerable<XElement> functionElements = menusElement.Elements("MenuFunction");
		collection.LoadFunctionsFromXml(functionElements);

		// Load menu definitions
		IEnumerable<XElement> menuElements = menusElement.Elements("Menu");
		collection.LoadMenusFromXml(menuElements);

		// Apply defaults to menus that don't have their own colors specified
		collection.ApplyDefaults();

		// Validate function references
		collection.ValidateFunctionReferences();

		return collection;
	}
}
