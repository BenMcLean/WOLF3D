using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace BenMcLean.Wolf3D.Assets.Gameplay;

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
	/// Font name for number display (e.g., "N", "SMALL")
	/// </summary>
	public string Font { get; set; }
	/// <summary>
	/// Text label elements (score, health, ammo, floor, lives, etc.).
	/// Updated at runtime by action scripts via SetText().
	/// </summary>
	public List<TextDefinition> Texts { get; set; } = [];
	/// <summary>
	/// Named picture elements (e.g., face display, weapon, keys).
	/// Updated at runtime by action scripts via SetPicture().
	/// </summary>
	public List<PictureDefinition> Pictures { get; set; } = [];
	/// <summary>
	/// Name of the ActionFunction to call on each facecount tick.
	/// WL_AGENT.C:UpdateFace — face frame selection logic.
	/// Optional: if absent, no face controller runs.
	/// </summary>
	public string OnFace { get; set; }
	/// <summary>
	/// Tics between face update checks. Default 4 (≈17.5Hz, approximating original 286 frame rate).
	/// Values smaller than 2 are treated as 1 (every tic). Larger values slow the face animation.
	/// </summary>
	public int FaceTics { get; set; } = 4;
	/// <summary>
	/// Name of the ActionFunction to call when the player dies (health reaches 0).
	/// WL_GAME.C:Died() — decrements lives, resets inventory, restarts level or game over.
	/// Returns "restart" or a menu name (e.g., "HighScores") for game over.
	/// </summary>
	public string OnDeath { get; set; }
	/// <summary>
	/// Name of the ActionFunction to call when starting a new game.
	/// WL_GAME.C:NewGame — initializes all inventory values, maxes, and display.
	/// </summary>
	public string OnNewGame { get; set; }
	/// <summary>
	/// Creates a StatusBarDefinition instance from an XElement.
	/// </summary>
	/// <param name="element">The XElement containing status bar data (&lt;StatusBar&gt;)</param>
	/// <returns>A new StatusBarDefinition instance</returns>
	public static StatusBarDefinition FromXElement(XElement element) => new()
	{
		BackgroundPic = element.Attribute("Pic")?.Value,
		Font = element.Attribute("Font")?.Value,
		Texts = [.. element.Elements("Text").Select(TextDefinition.FromXElement)],
		Pictures = [.. element.Elements("Picture").Select(PictureDefinition.FromXElement)],
		OnFace = element.Attribute("OnFace")?.Value,
		OnDeath = element.Attribute("OnDeath")?.Value,
		OnNewGame = element.Attribute("OnNewGame")?.Value,
		FaceTics = int.TryParse(element.Attribute("FaceTics")?.Value, out int faceTics) ? faceTics : 2
	};
}
