using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace BenMcLean.Wolf3D.Assets.Gameplay;

/// <summary>
/// Represents a single weapon definition for the status bar.
/// Maps to a &lt;Weapon&gt; element in XML.
/// </summary>
public class StatusBarWeaponDefinition
{
	/// <summary>
	/// Weapon slot number (0-3 for Knife, Pistol, Machine Gun, Chain Gun)
	/// </summary>
	public int Number { get; set; }
	/// <summary>
	/// Name of the VgaGraph weapon picture (e.g., "KNIFEPIC", "GUNPIC")
	/// </summary>
	public string Pic { get; set; }
	/// <summary>
	/// Initial availability (1 = have weapon, 0 = don't have it)
	/// </summary>
	public int Init { get; set; }
	/// <summary>
	/// Creates a StatusBarWeaponDefinition instance from an XElement.
	/// </summary>
	/// <param name="element">The XElement containing weapon data (&lt;Weapon&gt;)</param>
	/// <returns>A new StatusBarWeaponDefinition instance</returns>
	public static StatusBarWeaponDefinition FromXElement(XElement element) => new()
	{
		Number = int.TryParse(element.Attribute("Number")?.Value, out int number) ? number : 0,
		Pic = element.Attribute("Pic")?.Value,
		Init = int.TryParse(element.Attribute("Init")?.Value, out int init) ? init : 0
	};
}

/// <summary>
/// Represents the weapons display area in the status bar.
/// Maps to a &lt;Weapons&gt; element in XML.
/// </summary>
public class StatusBarWeaponsDefinition
{
	/// <summary>
	/// X coordinate for weapon display
	/// </summary>
	public int X { get; set; }
	/// <summary>
	/// Y coordinate for weapon display
	/// </summary>
	public int Y { get; set; }
	/// <summary>
	/// List of weapon definitions
	/// </summary>
	public List<StatusBarWeaponDefinition> Weapons { get; set; } = [];
	/// <summary>
	/// Creates a StatusBarWeaponsDefinition instance from an XElement.
	/// </summary>
	/// <param name="element">The XElement containing weapons data (&lt;Weapons&gt;)</param>
	/// <returns>A new StatusBarWeaponsDefinition instance</returns>
	public static StatusBarWeaponsDefinition FromXElement(XElement element) => new()
	{
		X = int.TryParse(element.Attribute("X")?.Value, out int x) ? x : 0,
		Y = int.TryParse(element.Attribute("Y")?.Value, out int y) ? y : 0,
		Weapons = [.. element.Elements("Weapon").Select(StatusBarWeaponDefinition.FromXElement)]
	};
}

/// <summary>
/// Represents the face display position in the status bar.
/// Maps to a &lt;Face&gt; element in XML.
/// </summary>
public class StatusBarFaceDefinition
{
	/// <summary>
	/// X coordinate for face display
	/// </summary>
	public int X { get; set; }
	/// <summary>
	/// Y coordinate for face display
	/// </summary>
	public int Y { get; set; }
	/// <summary>
	/// Creates a StatusBarFaceDefinition instance from an XElement.
	/// </summary>
	/// <param name="element">The XElement containing face data (&lt;Face&gt;)</param>
	/// <returns>A new StatusBarFaceDefinition instance</returns>
	public static StatusBarFaceDefinition FromXElement(XElement element) => new()
	{
		X = int.TryParse(element.Attribute("X")?.Value, out int x) ? x : 0,
		Y = int.TryParse(element.Attribute("Y")?.Value, out int y) ? y : 0
	};
}

/// <summary>
/// Represents a number display in the status bar.
/// Maps to a &lt;Number&gt; element in XML.
/// Can represent either a numeric display (with X, Y, Digits) or a key display (with Have, Empty pics).
/// </summary>
public class StatusBarNumberDefinition
{
	/// <summary>
	/// Unique identifier for this value (e.g., "Health", "Ammo", "Gold Key")
	/// </summary>
	public string Name { get; set; }
	/// <summary>
	/// X coordinate for display. Null if this is an internal-only value.
	/// </summary>
	public int? X { get; set; }
	/// <summary>
	/// Y coordinate for display. Null if this is an internal-only value.
	/// </summary>
	public int? Y { get; set; }
	/// <summary>
	/// Number of digits to display (for right-justified padding)
	/// </summary>
	public int Digits { get; set; }
	/// <summary>
	/// Initial value at game start
	/// </summary>
	public int Init { get; set; }
	/// <summary>
	/// Maximum value (for clamping). Null means no maximum.
	/// </summary>
	public int? Max { get; set; }
	/// <summary>
	/// Picture name when value > 0 (for key display mode)
	/// </summary>
	public string Have { get; set; }
	/// <summary>
	/// Picture name when value = 0 (for key display mode)
	/// </summary>
	public string Empty { get; set; }
	/// <summary>
	/// Value to reset to on level change. Null means no reset.
	/// </summary>
	public int? LevelReset { get; set; }
	/// <summary>
	/// Returns true if this is a key-style display (uses Have/Empty pics instead of digits)
	/// </summary>
	public bool IsKeyDisplay => !string.IsNullOrEmpty(Have);
	/// <summary>
	/// Returns true if this value should be rendered (has display coordinates)
	/// </summary>
	public bool IsRendered => X.HasValue && Y.HasValue;
	/// <summary>
	/// Creates a StatusBarNumberDefinition instance from an XElement.
	/// </summary>
	/// <param name="element">The XElement containing number data (&lt;Number&gt;)</param>
	/// <returns>A new StatusBarNumberDefinition instance</returns>
	public static StatusBarNumberDefinition FromXElement(XElement element)
	{
		StatusBarNumberDefinition number = new()
		{
			Name = element.Attribute("Name")?.Value ?? string.Empty,
			Digits = int.TryParse(element.Attribute("Digits")?.Value, out int digits) ? digits : 0,
			Init = int.TryParse(element.Attribute("Init")?.Value, out int init) ? init : 0,
			Have = element.Attribute("Have")?.Value,
			Empty = element.Attribute("Empty")?.Value
		};
		if (int.TryParse(element.Attribute("X")?.Value, out int x))
			number.X = x;
		if (int.TryParse(element.Attribute("Y")?.Value, out int y))
			number.Y = y;
		if (int.TryParse(element.Attribute("Max")?.Value, out int max))
			number.Max = max;
		if (int.TryParse(element.Attribute("LevelReset")?.Value, out int levelReset))
			number.LevelReset = levelReset;
		return number;
	}
}

/// <summary>
/// Represents the complete status bar definition.
/// Maps to a &lt;StatusBar&gt; element in XML.
/// </summary>
public class StatusBarDefinition
{
	/// <summary>
	/// Name of the background picture (e.g., "STATUSBARPIC")
	/// </summary>
	public string BackgroundPic { get; set; }
	/// <summary>
	/// Font index for number display (chunk font number)
	/// </summary>
	public int Font { get; set; }
	/// <summary>
	/// Number definitions (health, ammo, score, etc.)
	/// </summary>
	public List<StatusBarNumberDefinition> Numbers { get; set; } = [];
	/// <summary>
	/// Face display definition (position only)
	/// </summary>
	public StatusBarFaceDefinition Face { get; set; }
	/// <summary>
	/// Weapons display definition
	/// </summary>
	public StatusBarWeaponsDefinition Weapons { get; set; }
	/// <summary>
	/// Lua script to execute when the player dies (health reaches 0).
	/// WL_GAME.C:Died() — decrements lives, resets inventory, restarts level or game over.
	/// Returns "restart" or "gameover".
	/// </summary>
	public string OnDeathScript { get; set; }
	/// <summary>
	/// Gets a number definition by name.
	/// </summary>
	/// <param name="name">The name of the number (e.g., "Health", "Ammo")</param>
	/// <returns>The number definition, or null if not found</returns>
	public StatusBarNumberDefinition GetNumber(string name) =>
		Numbers.FirstOrDefault(n => n.Name == name);
	/// <summary>
	/// Creates a StatusBarDefinition instance from an XElement.
	/// </summary>
	/// <param name="element">The XElement containing status bar data (&lt;StatusBar&gt;)</param>
	/// <returns>A new StatusBarDefinition instance</returns>
	public static StatusBarDefinition FromXElement(XElement element)
	{
		StatusBarDefinition statusBar = new()
		{
			BackgroundPic = element.Attribute("Pic")?.Value,
			Font = int.TryParse(element.Attribute("Font")?.Value, out int font) ? font : 0,
			Numbers = [.. element.Elements("Number").Select(StatusBarNumberDefinition.FromXElement)]
		};
		XElement faceElement = element.Element("Face");
		if (faceElement != null)
			statusBar.Face = StatusBarFaceDefinition.FromXElement(faceElement);
		XElement weaponsElement = element.Element("Weapons");
		if (weaponsElement != null)
			statusBar.Weapons = StatusBarWeaponsDefinition.FromXElement(weaponsElement);
		statusBar.OnDeathScript = element.Element("OnDeath")?.Value;
		return statusBar;
	}
}
