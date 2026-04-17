using BenMcLean.Wolf3D.Assets.Menu;
using Godot;

namespace BenMcLean.Wolf3D.Shared.Menu;

/// <summary>
/// Renders Wolf3D article page content into a Godot Control canvas.
/// Used by MenuRenderer.RenderArticle() — does not own a SubViewport.
/// WL_TEXT.C: VWB_Bar(BACKCOLOR) + VWB_DrawPic + VWB_DrawPropString
///
/// Color mapping: fontcolor=0 (black) is remapped to palette index 15 (white)
/// so text is visible against BackColor=0x11 (dark blue) background.
/// </summary>
public static class ArticleRenderer
{
	/// <summary>
	/// Clear the canvas and render the given article page layout.
	/// Called by MenuRenderer.RenderArticle().
	/// WL_TEXT.C: VWB_Bar(0, 0, 320, 200, BACKCOLOR) before drawing each page.
	/// </summary>
	public static void RenderPage(Control canvas, ArticlePageLayout page)
	{
		if (canvas is null || page is null) return;

		// Background fill — WL_TEXT.C: BACKCOLOR = 0x11 (VGA palette index 17, dark blue)
		canvas.AddChild(new ColorRect
		{
			Position = Vector2.Zero,
			Size = new Vector2(ArticleLayoutEngine.ScreenPixWidth, 200),
			Color = SharedAssetManager.GetPaletteColor(ArticleLayoutEngine.BackColor),
			ZIndex = 0,
		});

		// Draw graphics (^G / ^T commands)
		foreach (ArticleGraphicItem graphic in page.Graphics)
			DrawGraphic(canvas, graphic);

		// Draw text runs (words and page number)
		Godot.Theme smallTheme = null;
		SharedAssetManager.Themes?.TryGetValue("SMALL", out smallTheme);
		foreach (ArticleTextRun run in page.TextRuns)
			DrawTextRun(canvas, run, smallTheme);
	}

	private static void DrawGraphic(Control canvas, ArticleGraphicItem graphic)
	{
		if (string.IsNullOrEmpty(graphic.PicName)) return;
		if (SharedAssetManager.VgaGraph?.TryGetValue(graphic.PicName, out AtlasTexture tex) != true) return;
		canvas.AddChild(new TextureRect
		{
			Texture = tex,
			Position = new Vector2(graphic.X, graphic.Y),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.Scale,
			Size = new Vector2(tex.Region.Size.X, tex.Region.Size.Y),
			ZIndex = 5,
		});
	}

	private static void DrawTextRun(Control canvas, ArticleTextRun run, Godot.Theme theme)
	{
		// WL_TEXT.C: fontcolor=0 (black) would be invisible on dark blue — use palette 15 (white)
		byte colorIndex = run.Color == 0 ? (byte)15 : run.Color;
		Color color = SharedAssetManager.GetPaletteColor(colorIndex);

		Label label = new()
		{
			Text = run.Text,
			Position = new Vector2(run.X, run.Y),
			AutowrapMode = TextServer.AutowrapMode.Off,
			ClipText = false,
			ZIndex = 10,
		};
		label.AddThemeColorOverride("font_color", color);
		if (theme is not null)
			label.Theme = theme;
		canvas.AddChild(label);
	}
}
