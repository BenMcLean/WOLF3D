using System.Collections.Generic;
using BenMcLean.Wolf3D.Assets.Menu;
using Godot;

namespace BenMcLean.Wolf3D.Shared.Menu;

/// <summary>
/// Renders Wolf3D article page content into a Godot Control canvas.
/// Used by MenuRenderer.RenderArticle() — does not own a SubViewport.
/// WL_TEXT.C: VWB_Bar(BACKCOLOR) + VWB_DrawPic + VWB_DrawPropString
/// </summary>
public static class ArticleRenderer
{
	/// <summary>
	/// Render the given article page layout plus fixed per-article pictures.
	/// Called by MenuRenderer.RenderArticle().
	/// WL_TEXT.C: VWB_Bar(0, 0, 320, 200, BACKCOLOR) then window border pics then text.
	/// </summary>
	public static void RenderPage(Control canvas, ArticlePageLayout page, IReadOnlyList<PictureDefinition> fixedPictures = null)
	{
		if (canvas is null || page is null) return;

		// Background fill — WL_TEXT.C: VWB_Bar(0,0,320,200,BACKCOLOR)
		canvas.AddChild(new ColorRect
		{
			Position = Vector2.Zero,
			Size = new Vector2(ArticleLayoutEngine.ScreenPixWidth, 200),
			Color = SharedAssetManager.GetPaletteColor(page.BackColor),
			ZIndex = 0,
		});

		// Fixed window-border pictures (H_TOPWINDOWPIC etc.) — WL_TEXT.C: drawn before text loop
		if (fixedPictures is not null)
			foreach (PictureDefinition pic in fixedPictures)
				DrawFixedPicture(canvas, pic);

		// Draw in-text graphics (^G / ^T commands)
		foreach (ArticleGraphicItem graphic in page.Graphics)
			DrawGraphic(canvas, graphic);

		// Draw text runs (words and page number)
		Godot.Theme smallTheme = null;
		SharedAssetManager.Themes?.TryGetValue("SMALL", out smallTheme);
		foreach (ArticleTextRun run in page.TextRuns)
			DrawTextRun(canvas, run, smallTheme);
	}

	private static void DrawFixedPicture(Control canvas, PictureDefinition pic)
	{
		if (string.IsNullOrEmpty(pic.Name)) return;
		if (SharedAssetManager.VgaGraph?.TryGetValue(pic.Name, out AtlasTexture tex) != true)
		{
			GD.PrintErr($"ERROR: Article fixed picture '{pic.Name}' not found in VgaGraph");
			return;
		}
		canvas.AddChild(new TextureRect
		{
			Texture = tex,
			Position = new Vector2(pic.XValue, pic.YValue),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.Scale,
			Size = tex.Region.Size,
			ZIndex = 3,
		});
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
			Size = tex.Region.Size,
			ZIndex = 5,
		});
	}

	private static void DrawTextRun(Control canvas, ArticleTextRun run, Godot.Theme theme)
	{
		Color color = SharedAssetManager.GetPaletteColor(run.Color);

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
