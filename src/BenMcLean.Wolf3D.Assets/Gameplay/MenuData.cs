using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
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
	/// X coordinate for picture placement, or "Center" for horizontal centering
	/// </summary>
	public string X { get; set; }
	/// <summary>
	/// Y coordinate for picture placement, or "Center" for vertical centering
	/// </summary>
	public string Y { get; set; }
	/// <summary>
	/// Returns true if X coordinate should be centered horizontally
	/// </summary>
	public bool CenterX => X?.Equals("Center", StringComparison.OrdinalIgnoreCase) == true;
	/// <summary>
	/// Returns true if Y coordinate should be centered vertically
	/// </summary>
	public bool CenterY => Y?.Equals("Center", StringComparison.OrdinalIgnoreCase) == true;
	/// <summary>
	/// Gets the X coordinate as an integer, or 0 if set to "Center"
	/// </summary>
	public int XValue => CenterX ? 0 : (int.TryParse(X, out int x) ? x : 0);
	/// <summary>
	/// Gets the Y coordinate as an integer, or 0 if set to "Center"
	/// </summary>
	public int YValue => CenterY ? 0 : (int.TryParse(Y, out int y) ? y : 0);
	/// <summary>
	/// If true, renders the leftmost column of pixels stretched horizontally across the screen
	/// before rendering the normal picture. Used for decorative stripe backgrounds.
	/// </summary>
	public bool Stripes { get; set; }
	/// <summary>
	/// Z-index for controlling draw order.
	/// Default is 5 (below boxes at 7). Use 9 for pictures that should appear above boxes (e.g., difficulty face).
	/// </summary>
	public int? ZIndex { get; set; }
	/// <summary>
	/// Comma-separated list of VgaGraph picture names to cycle through for animation.
	/// If null or empty, the picture is static (uses Name only).
	/// Example: "L_GUYPIC,L_GUY2PIC" for BJ breathing animation.
	/// </summary>
	public string Frames { get; set; }
	/// <summary>
	/// Time in seconds between animation frames. Default 0.5.
	/// Only used when Frames is specified.
	/// </summary>
	public float FrameInterval { get; set; } = 0.5f;
	/// <summary>
	/// Inline Lua script to execute when this picture is clicked.
	/// Stored as element content in XML, executed via DoString (not pre-cached).
	/// If null or empty, the picture is not clickable.
	/// </summary>
	public string Script { get; set; }
	/// <summary>
	/// Creates a MenuPictureDefinition instance from an XElement.
	/// </summary>
	/// <param name="element">The XElement containing picture data (&lt;Picture&gt;)</param>
	/// <returns>A new MenuPictureDefinition instance</returns>
	public static MenuPictureDefinition FromXElement(XElement element)
	{
		MenuPictureDefinition picture = new()
		{
			Name = element.Attribute("Name")?.Value ?? throw new ArgumentException("Picture element must have a Name attribute"),
			X = element.Attribute("X")?.Value ?? "0",
			Y = element.Attribute("Y")?.Value ?? "0",
			Stripes = bool.TryParse(element.Attribute("Stripes")?.Value, out bool stripes) && stripes,
			Frames = element.Attribute("Frames")?.Value,
			Script = element.Value?.Trim()
		};
		if (int.TryParse(element.Attribute("ZIndex")?.Value, out int zIndex))
			picture.ZIndex = zIndex;
		if (float.TryParse(element.Attribute("FrameInterval")?.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float frameInterval))
			picture.FrameInterval = frameInterval;
		return picture;
	}
}
/// <summary>
/// Represents a text label in a menu.
/// Maps to a &lt;Text&gt; element in XML.
/// Used for non-interactive text like "How tough are you?" in the NewGame menu.
/// WL_MENU.C:1639-1649: US_Print("How tough are you?") with READHCOLOR
/// </summary>
public class MenuTextDefinition
{
	/// <summary>
	/// Optional identifier for Lua to update text dynamically.
	/// When set, the text can be updated via SetText(name, value) from Lua scripts.
	/// </summary>
	public string Name { get; set; }
	/// <summary>
	/// Text content to display
	/// </summary>
	public string Content { get; set; }
	/// <summary>
	/// X coordinate for text placement, or "Center" for horizontal centering
	/// </summary>
	public string X { get; set; }
	/// <summary>
	/// Y coordinate for text placement, or "Center" for vertical centering
	/// </summary>
	public string Y { get; set; }
	/// <summary>
	/// Font name (e.g., "BIG", "SMALL").
	/// If not specified, uses menu Font, then default Font.
	/// </summary>
	public string Font { get; set; }
	/// <summary>
	/// Text color index (VGA palette 0-255).
	/// If not specified, uses menu TextColor, then default TextColor.
	/// Example: READHCOLOR = 0x47 (71) for yellow "How tough are you?" text
	/// </summary>
	public byte? Color { get; set; }
	/// <summary>
	/// Returns true if X coordinate should be centered horizontally
	/// </summary>
	public bool CenterX => X?.Equals("Center", StringComparison.OrdinalIgnoreCase) == true;
	/// <summary>
	/// Returns true if Y coordinate should be centered vertically
	/// </summary>
	public bool CenterY => Y?.Equals("Center", StringComparison.OrdinalIgnoreCase) == true;
	/// <summary>
	/// Gets the X coordinate as an integer, or 0 if set to "Center"
	/// </summary>
	public int XValue => CenterX ? 0 : (int.TryParse(X, out int x) ? x : 0);
	/// <summary>
	/// Gets the Y coordinate as an integer, or 0 if set to "Center"
	/// </summary>
	public int YValue => CenterY ? 0 : (int.TryParse(Y, out int y) ? y : 0);
	/// <summary>
	/// Creates a MenuTextDefinition instance from an XElement.
	/// </summary>
	/// <param name="element">The XElement containing text data (&lt;Text&gt;)</param>
	/// <returns>A new MenuTextDefinition instance</returns>
	public static MenuTextDefinition FromXElement(XElement element)
	{
		MenuTextDefinition text = new()
		{
			Name = element.Attribute("Name")?.Value,
			Content = element.Value?.Trim() ?? string.Empty,
			X = element.Attribute("X")?.Value ?? "0",
			Y = element.Attribute("Y")?.Value ?? "0",
			Font = element.Attribute("Font")?.Value
		};
		if (byte.TryParse(element.Attribute("Color")?.Value, out byte color))
			text.Color = color;
		return text;
	}
}

/// <summary>
/// Represents a percent ticker in a menu that animates counting from 0 to a target value.
/// Maps to a &lt;Ticker&gt; element in XML.
/// Used for intermission screen statistics (kill%, secret%, treasure%).
/// </summary>
public class MenuTickerDefinition
{
	/// <summary>
	/// Identifier for Lua (e.g., "KillRatio"). Used to start and control the ticker from scripts.
	/// </summary>
	public string Name { get; set; }
	/// <summary>
	/// X coordinate for ticker placement (supports "Center").
	/// </summary>
	public string X { get; set; }
	/// <summary>
	/// Y coordinate for ticker placement (supports "Center").
	/// </summary>
	public string Y { get; set; }
	/// <summary>
	/// Font name override. If not specified, uses menu Font.
	/// </summary>
	public string Font { get; set; }
	/// <summary>
	/// Color override. If not specified, uses menu TextColor.
	/// </summary>
	public byte? Color { get; set; }
	/// <summary>
	/// Text alignment ("Right" for right-aligned numbers).
	/// </summary>
	public string Align { get; set; }
	/// <summary>
	/// Sound name to play per increment (e.g., "ENDBONUS1SND").
	/// </summary>
	public string TickSound { get; set; }
	/// <summary>
	/// Sound name to play when counting finishes (e.g., "ENDBONUS2SND").
	/// </summary>
	public string DoneSound { get; set; }
	/// <summary>
	/// Sound name to play when value reaches 100% (e.g., "PERCENT100SND").
	/// </summary>
	public string PerfectSound { get; set; }
	/// <summary>
	/// Sound name to play when the target value is 0 (e.g., "NOBONUSSND").
	/// WL_INTER.C: plays NOBONUSSND when ratio is 0%.
	/// </summary>
	public string NoBonusSound { get; set; }
	/// <summary>
	/// Increment every N% to trigger sound (e.g., 10 means sound plays every 10%).
	/// </summary>
	public int TickInterval { get; set; } = 10;
	/// <summary>
	/// Gets the X coordinate as an integer, or 0 if "Center".
	/// </summary>
	public int XValue => int.TryParse(X, out int x) ? x : 0;
	/// <summary>
	/// Gets the Y coordinate as an integer, or 0 if "Center".
	/// </summary>
	public int YValue => int.TryParse(Y, out int y) ? y : 0;
	/// <summary>
	/// Creates a MenuTickerDefinition instance from an XElement.
	/// </summary>
	public static MenuTickerDefinition FromXElement(XElement element)
	{
		MenuTickerDefinition ticker = new()
		{
			Name = element.Attribute("Name")?.Value ?? throw new ArgumentException("Ticker element must have a Name attribute"),
			X = element.Attribute("X")?.Value ?? "0",
			Y = element.Attribute("Y")?.Value ?? "0",
			Font = element.Attribute("Font")?.Value,
			Align = element.Attribute("Align")?.Value
		};
		if (byte.TryParse(element.Attribute("Color")?.Value, out byte color))
			ticker.Color = color;
		ticker.TickSound = element.Attribute("TickSound")?.Value;
		ticker.DoneSound = element.Attribute("DoneSound")?.Value;
		ticker.PerfectSound = element.Attribute("PerfectSound")?.Value;
		ticker.NoBonusSound = element.Attribute("NoBonusSound")?.Value;
		if (int.TryParse(element.Attribute("TickInterval")?.Value, out int tickInterval))
			ticker.TickInterval = tickInterval;
		return ticker;
	}
}

/// <summary>
/// Represents a pause step in a menu's presentation sequence.
/// Maps to a &lt;Pause&gt; element in XML.
/// Waits for any button press (or optional timeout), then executes its Lua script.
/// Runs after tickers complete but before interactive MenuItems.
/// </summary>
public class MenuPauseDefinition
{
	/// <summary>
	/// Lua script to execute when the pause completes (button pressed or timeout).
	/// Stored as element text content.
	/// </summary>
	public string Script { get; set; }
	/// <summary>
	/// Optional timeout duration. If set, the pause auto-completes after this time.
	/// Any button press also completes the pause before the timeout.
	/// If null, waits indefinitely for any button press.
	/// </summary>
	public TimeSpan? Duration { get; set; }
	/// <summary>
	/// Creates a MenuPauseDefinition instance from an XElement.
	/// </summary>
	/// <param name="element">The XElement containing pause data (&lt;Pause&gt;)</param>
	/// <returns>A new MenuPauseDefinition instance</returns>
	public static MenuPauseDefinition FromXElement(XElement element) => new()
	{
		Script = element.Value?.Trim(),
		Duration = TimeSpan.TryParse(element.Attribute("Duration")?.Value, out TimeSpan duration) ? duration : null,
	};
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
	/// Lua script to execute when menu selection changes (including initial menu construction).
	/// Similar to original Wolf3D's DrawNewGameDiff callback in HandleMenu.
	/// WL_MENU.C:1518: which=HandleMenu(&NewItems,&NewMenu[0],DrawNewGameDiff);
	/// WL_MENU.C:1669: void DrawNewGameDiff(int w) { VWB_DrawPic(...,w+C_BABYMODEPIC); }
	/// The script can call GetSelectedIndex() to query current selection and SetPicture() to update pictures.
	/// </summary>
	public string OnSelectionChanged { get; set; }
	/// <summary>
	/// Lua script to execute once when the menu first appears.
	/// Used for intermission screen initialization (setting text values, starting tickers).
	/// </summary>
	public string OnShow { get; set; }
	/// <summary>
	/// 3D beveled boxes to display (WL_MENU.C:DrawWindow)
	/// </summary>
	public List<MenuBoxDefinition> Boxes { get; set; } = [];
	/// <summary>
	/// Decorative pictures to display (e.g., logos, title graphics)
	/// </summary>
	public List<MenuPictureDefinition> Pictures { get; set; } = [];
	/// <summary>
	/// Text labels to display (e.g., "How tough are you?" in NewGame menu)
	/// WL_MENU.C:1639-1649: US_Print() for non-interactive text
	/// </summary>
	public List<MenuTextDefinition> Texts { get; set; } = [];
	/// <summary>
	/// Animated percent tickers (e.g., kill%, secret%, treasure% on intermission screen).
	/// </summary>
	public List<MenuTickerDefinition> Tickers { get; set; } = [];
	/// <summary>
	/// Pause steps that run after tickers complete but before interactive MenuItems.
	/// Each pause waits for any button press (or optional timeout), then executes its Lua script.
	/// </summary>
	public List<MenuPauseDefinition> Pauses { get; set; } = [];
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
			CursorMoveSound = element.Attribute("CursorMoveSound")?.Value,
			OnSelectionChanged = element.Element("OnSelectionChanged")?.Value?.Trim(),
			OnShow = element.Element("OnShow")?.Value?.Trim()
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

		// Parse text labels
		IEnumerable<XElement> textElements = element.Elements("Text");
		if (textElements != null)
			menu.Texts = [.. textElements.Select(MenuTextDefinition.FromXElement)];
		// Parse tickers
		IEnumerable<XElement> tickerElements = element.Elements("Ticker");
		if (tickerElements != null)
			menu.Tickers = [.. tickerElements.Select(MenuTickerDefinition.FromXElement)];
		// Parse pause steps
		IEnumerable<XElement> pauseElements = element.Elements("Pause");
		if (pauseElements != null)
			menu.Pauses = [.. pauseElements.Select(MenuPauseDefinition.FromXElement)];
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
	/// Name of the menu to show when pausing during gameplay (e.g., "Main").
	/// If null, falls back to StartMenu.
	/// </summary>
	public string PauseMenu { get; set; }
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
	/// Background fill color for modal dialog boxes (WL_MENU.C:Message DrawWindow wcolor).
	/// Separate from DefaultTextColor — other games may use different colors for each.
	/// WL1.xml: ModalColor="23" (TEXTCOLOR grey, 0x17)
	/// </summary>
	public byte? DefaultModalColor { get; set; }
	/// <summary>
	/// Text color inside modal dialog boxes (WL_MENU.C:SETFONTCOLOR foreground).
	/// In original Wolf3D: always palette 0 (black). Other games may differ.
	/// WL1.xml: ModalTextColor="0"
	/// </summary>
	public byte? DefaultModalTextColor { get; set; }
	/// <summary>
	/// Top/left bevel color for modal boxes (PixelRect.NWColor = NorthWest bevel).
	/// Corresponds to old BordColor attribute. Separate from DefaultHighlight.
	/// WL1.xml: ModalHighlight="19"
	/// </summary>
	public byte? DefaultModalHighlight { get; set; }
	/// <summary>
	/// Bottom/right bevel color for modal boxes (PixelRect.SEColor = SouthEast bevel).
	/// Corresponds to old Bord2Color attribute. Separate from DefaultBorder2Color.
	/// WL1.xml: ModalBorder2Color="0"
	/// </summary>
	public byte? DefaultModalBorder2Color { get; set; }
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
	/// Default font for menus (e.g., "BIG", "SMALL")
	/// Individual menus can override this with their Font attribute
	/// </summary>
	public string DefaultFont { get; set; }
	/// <summary>
	/// Default vertical spacing between menu items in pixels (original uses 13)
	/// Individual menus can override this with their Spacing attribute
	/// </summary>
	public int? DefaultSpacing { get; set; }
	/// <summary>
	/// Quit confirmation messages, randomly selected when user chooses to quit.
	/// WL_MENU.C:endStrings[] - randomly picked with US_RndT()
	/// Defined as &lt;EndString&gt; children of &lt;Menus&gt; in XML.
	/// </summary>
	public List<string> EndStrings { get; set; } = [];
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

			// Apply default sound, music, cursor, and font
			// Note: Empty string ("") won't be replaced - allows explicit "no cursor/sound/music/font"
			menu.CursorMoveSound ??= DefaultCursorMoveSound;
			menu.Music ??= DefaultMusic;
			menu.CursorPic ??= DefaultCursorPic;
			menu.Font ??= DefaultFont;
			// Apply default spacing
			menu.Spacing ??= DefaultSpacing;

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
			PauseMenu = menusElement.Attribute("Pause")?.Value,
			DefaultCursorMoveSound = menusElement.Attribute("CursorMoveSound")?.Value,
			DefaultMusic = menusElement.Attribute("Music")?.Value,
			DefaultCursorPic = menusElement.Attribute("CursorPic")?.Value,
			DefaultFont = menusElement.Attribute("Font")?.Value
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
		if (byte.TryParse(menusElement.Attribute("ModalColor")?.Value, out byte modalColor))
			collection.DefaultModalColor = modalColor;
		if (byte.TryParse(menusElement.Attribute("ModalTextColor")?.Value, out byte modalTextColor))
			collection.DefaultModalTextColor = modalTextColor;
		if (byte.TryParse(menusElement.Attribute("ModalHighlight")?.Value, out byte modalHighlight))
			collection.DefaultModalHighlight = modalHighlight;
		if (byte.TryParse(menusElement.Attribute("ModalBorder2Color")?.Value, out byte modalBorder2))
			collection.DefaultModalBorder2Color = modalBorder2;

		// Parse default spacing
		if (int.TryParse(menusElement.Attribute("Spacing")?.Value, out int spacing))
			collection.DefaultSpacing = spacing;
		// Load quit end strings (WL_MENU.C:endStrings[])
		collection.EndStrings = [.. menusElement.Elements("EndString")
			.Select(e => e.Value?.Trim())
			.Where(s => !string.IsNullOrEmpty(s))];
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
