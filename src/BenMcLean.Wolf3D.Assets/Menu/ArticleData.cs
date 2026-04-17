using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace BenMcLean.Wolf3D.Assets.Menu;

/// <summary>
/// Defines a text chunk in the XML configuration.
/// A text chunk is a Wolf3D PageLayout-format text article stored either as a
/// VGAGRAPH chunk or an external file (e.g., HELPART.WL1).
/// WL_TEXT.C: CA_CacheGrChunk(T_HELPART) or CA_LoadFile(helpfilename)
/// </summary>
public class TextChunkDefinition
{
	/// <summary>Logical name for this chunk (e.g., "T_HELPART", "T_ENDART1").</summary>
	public string Name { get; set; }
	/// <summary>
	/// Absolute VGAGRAPH chunk index.
	/// Used when the text is embedded in VGAGRAPH (registered/WL6 builds).
	/// </summary>
	public ushort? Number { get; set; }
	/// <summary>
	/// External filename relative to the game data folder (e.g., "HELPART.WL1").
	/// Used when the text is stored separately (shareware WL1 builds).
	/// WL_TEXT.C: char helpfilename[13] = "HELPART."
	/// </summary>
	public string File { get; set; }

	public static TextChunkDefinition FromXElement(XElement element) => new()
	{
		Name = element.Attribute("Name")?.Value
			?? throw new ArgumentException("TextChunk must have a Name attribute"),
		Number = ushort.TryParse(element.Attribute("Number")?.Value, out ushort n) ? n : null,
		File = element.Attribute("File")?.Value,
	};
}

/// <summary>
/// Defines a navigable article screen in the menu system.
/// Articles display Wolf3D PageLayout-format text with page-flip navigation.
/// WL_TEXT.C: HelpScreens() / ShowArticle() / CP_ReadThis()
/// </summary>
public class ArticleDefinition
{
	/// <summary>Unique identifier for this article (e.g., "ReadThis", "EndArt1").</summary>
	public string Name { get; set; }
	/// <summary>Name of the TextChunk to display (e.g., "T_HELPART").</summary>
	public string ChunkName { get; set; }
	/// <summary>Music track to play while showing this article (e.g., "CORNER_MUS").</summary>
	public string Music { get; set; }
	/// <summary>
	/// VGA palette index for the article background fill and viewport border.
	/// Null = use ArticleLayoutEngine.BackColor (0x11, dark blue).
	/// WL_TEXT.C: BACKCOLOR = 0x11
	/// </summary>
	public byte? BackColor { get; set; }
	/// <summary>
	/// Fixed pictures rendered on every page (window borders, decorations).
	/// WL_TEXT.C: H_TOPWINDOWPIC / H_LEFTWINDOWPIC / H_RIGHTWINDOWPIC / H_BOTTOMINFOPIC
	/// </summary>
	public List<PictureDefinition> Pictures { get; set; } = [];
	/// <summary>
	/// Lua script executed when the player presses ESC/Cancel.
	/// WL_TEXT.C: ShowArticle exits on sc_Escape.
	/// </summary>
	public string OnCancel { get; set; }

	public static ArticleDefinition FromXElement(XElement element)
	{
		ArticleDefinition def = new()
		{
			Name = element.Attribute("Name")?.Value
				?? throw new ArgumentException("Article must have a Name attribute"),
			ChunkName = element.Attribute("ChunkName")?.Value
				?? throw new ArgumentException("Article must have a ChunkName attribute"),
			Music = element.Attribute("Music")?.Value,
			OnCancel = element.Element("OnCancel")?.Value?.Trim(),
		};
		if (byte.TryParse(element.Attribute("BackColor")?.Value, out byte bc))
			def.BackColor = bc;
		foreach (XElement picEl in element.Elements("Picture"))
			def.Pictures.Add(PictureDefinition.FromXElement(picEl));
		return def;
	}
}

/// <summary>
/// A single positioned text run on an article page.
/// Represents one word (or sequence of characters placed by ^L).
/// WL_TEXT.C: HandleWord / VWB_DrawPropString
/// </summary>
public class ArticleTextRun
{
	/// <summary>X pixel coordinate (left edge of first character).</summary>
	public int X { get; set; }
	/// <summary>Y pixel coordinate (top of character row).</summary>
	public int Y { get; set; }
	/// <summary>
	/// VGA palette color index for this run.
	/// WL_TEXT.C: fontcolor — initially 0, changed by ^C command.
	/// </summary>
	public byte Color { get; set; }
	/// <summary>The word/text content (no leading/trailing spaces).</summary>
	public string Text { get; set; }
}

/// <summary>
/// A graphic element placed on an article page.
/// WL_TEXT.C: ^G command / VWB_DrawPic(picx&~7, picy, picnum)
/// </summary>
public class ArticleGraphicItem
{
	/// <summary>X pixel coordinate, aligned to 8-pixel boundary (picx&~7).</summary>
	public int X { get; set; }
	/// <summary>Y pixel coordinate.</summary>
	public int Y { get; set; }
	/// <summary>
	/// VgaGraph pic name resolved from the absolute chunk index.
	/// Null if the chunk index could not be resolved (unknown pic).
	/// </summary>
	public string PicName { get; set; }
}

/// <summary>
/// Pre-computed layout of a single article page.
/// Produced by ArticleLayoutEngine, consumed by ArticleRenderer.
/// WL_TEXT.C: one call to PageLayout(true)
/// </summary>
public class ArticlePageLayout
{
	/// <summary>1-based page number (matches Wolf3D pagenum variable).</summary>
	public int PageNumber { get; set; }
	/// <summary>Total page count in this article.</summary>
	public int TotalPages { get; set; }
	/// <summary>
	/// VGA palette index for the background fill of this page.
	/// WL_TEXT.C: VWB_Bar(0,0,320,200,BACKCOLOR)
	/// </summary>
	public byte BackColor { get; set; }
	/// <summary>Text runs in draw order.</summary>
	public List<ArticleTextRun> TextRuns { get; } = [];
	/// <summary>Graphics in draw order.</summary>
	public List<ArticleGraphicItem> Graphics { get; } = [];
}
