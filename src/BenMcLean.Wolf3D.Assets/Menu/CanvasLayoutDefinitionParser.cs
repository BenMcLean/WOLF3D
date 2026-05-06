using System;
using System.Linq;
using System.Xml.Linq;

namespace BenMcLean.Wolf3D.Assets.Menu;

/// <summary>
/// Parses the shared visual primitives that can appear on menu and status-bar canvases.
/// </summary>
public static class CanvasLayoutDefinitionParser
{
	/// <summary>
	/// Populates shared layout fields from an XML element.
	/// </summary>
	/// <param name="element">The source element containing shared layout children.</param>
	/// <param name="layout">The layout definition to populate.</param>
	public static void PopulateLayout(XElement element, CanvasLayoutDefinition layout)
	{
		ArgumentNullException.ThrowIfNull(element);
		ArgumentNullException.ThrowIfNull(layout);

		layout.Font = element.Attribute("Font")?.Value;
		layout.Boxes = [.. element.Elements("Box").Select(MenuBoxDefinition.FromXElement)];
		layout.Pictures = [.. element.Elements("Picture").Select(PictureDefinition.FromXElement)];
		layout.Texts = [.. element.Elements("Text").Select(TextDefinition.FromXElement)];
	}
}
