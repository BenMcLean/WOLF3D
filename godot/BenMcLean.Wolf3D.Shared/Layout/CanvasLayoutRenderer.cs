using System;
using BenMcLean.Wolf3D.Assets.Menu;
using BenMcLean.Wolf3D.Shared.Text;
using Godot;

namespace BenMcLean.Wolf3D.Shared.Layout;

/// <summary>
/// Composition-first renderer for shared non-interactive canvas layout primitives.
/// MenuRenderer and StatusBarRenderer can both use this without sharing behavior that
/// belongs to only one of those surfaces.
/// </summary>
public class CanvasLayoutRenderer(Control canvas, float canvasWidth, float canvasHeight)
{
	private readonly Control _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
	private readonly float _canvasWidth = canvasWidth,
		_canvasHeight = canvasHeight;
	public void RenderAll(CanvasLayoutDefinition layout, CanvasLayoutRenderOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		RenderBackgroundPicture(options.BackgroundPictureName, options.BackgroundZIndex);
		RenderBoxes(layout, options.DefaultDeactive, options.DefaultBorder2Color, options.BoxFillZIndex, options.BoxBorderZIndex);
		if (options.RenderTextsBeforePictures)
		{
			RenderTexts(layout, options.FallbackFontName, options.TextContentProvider, options.TextColorProvider, options.AfterAddText);
			RenderPictures(layout, options.PictureZIndex, options.PictureNameProvider, options.AfterAddPicture);
			return;
		}
		RenderPictures(layout, options.PictureZIndex, options.PictureNameProvider, options.AfterAddPicture);
		RenderTexts(layout, options.FallbackFontName, options.TextContentProvider, options.TextColorProvider, options.AfterAddText);
	}
	public void RenderBackgroundPicture(string pictureName, int defaultZIndex = 0)
	{
		if (string.IsNullOrEmpty(pictureName))
			return;
		if (!SharedAssetManager.VgaGraph.TryGetValue(pictureName, out AtlasTexture texture))
		{
			GD.PrintErr($"ERROR: Background picture '{pictureName}' not found in VgaGraph");
			return;
		}
		PictureDefinition backgroundDef = new() { Name = pictureName, X = "0", Y = "0" };
		TextureRect background = CanvasLayoutRenderHelper.CreatePictureRect(
			backgroundDef,
			texture,
			_canvasWidth,
			_canvasHeight,
			defaultZIndex);
		background.Size = new Vector2(_canvasWidth, _canvasHeight);
		_canvas.AddChild(background);
	}
	public void RenderBoxes(
		CanvasLayoutDefinition layout,
		byte defaultDeactive,
		byte defaultBorder2Color,
		int fillZIndex,
		int borderZIndex)
	{
		if (layout?.Boxes is null || layout.Boxes.Count == 0)
			return;
		foreach (MenuBoxDefinition boxDef in layout.Boxes)
			CanvasLayoutRenderHelper.AddBox(_canvas, boxDef, defaultDeactive, defaultBorder2Color, fillZIndex, borderZIndex);
	}
	public void RenderPictures(
		CanvasLayoutDefinition layout,
		int defaultZIndex,
		Func<PictureDefinition, string> pictureNameProvider = null,
		Action<PictureDefinition, TextureRect> afterAdd = null)
	{
		if (layout?.Pictures is null || layout.Pictures.Count == 0)
			return;
		foreach (PictureDefinition pictureDef in layout.Pictures)
		{
			string pictureName = pictureNameProvider?.Invoke(pictureDef) ?? pictureDef.Name;
			if (string.IsNullOrEmpty(pictureName))
				continue;
			if (!SharedAssetManager.VgaGraph.TryGetValue(pictureName, out AtlasTexture texture))
			{
				GD.PrintErr($"ERROR: Picture '{pictureName}' (Id='{pictureDef.Id}') not found in VgaGraph");
				continue;
			}
			TextureRect pictureRect = CanvasLayoutRenderHelper.CreatePictureRect(
				pictureDef,
				texture,
				_canvasWidth,
				_canvasHeight,
				defaultZIndex);
			_canvas.AddChild(pictureRect);
			afterAdd?.Invoke(pictureDef, pictureRect);
		}
	}
	public void RenderTexts(
		CanvasLayoutDefinition layout,
		string fallbackFontName,
		Func<TextDefinition, string> contentProvider = null,
		Func<TextDefinition, byte?> colorProvider = null,
		Action<TextDefinition, Label, Theme, string> afterAdd = null)
	{
		if (layout?.Texts is null || layout.Texts.Count == 0)
			return;
		foreach (TextDefinition textDef in layout.Texts)
		{
			string fontName = textDef.Font ?? layout.Font ?? fallbackFontName;
			if (!SharedAssetManager.Themes.TryGetValue(fontName, out Theme theme))
			{
				GD.PrintErr($"ERROR: Theme '{fontName}' not found in SharedAssetManager");
				continue;
			}
			string content = contentProvider?.Invoke(textDef) ?? textDef.Content;
			byte? colorIndex = colorProvider?.Invoke(textDef);
			Color? textColor = colorIndex.HasValue
				? SharedAssetManager.GetPaletteColor(colorIndex.Value)
				: null;
			Label label = TextLayoutHelper.CreateLabel(textDef, theme, content, textColor);
			label.Position = TextLayoutHelper.GetPosition(textDef, theme, content, _canvasWidth, _canvasHeight);
			_canvas.AddChild(label);
			afterAdd?.Invoke(textDef, label, theme, content);
		}
	}
}
public class CanvasLayoutRenderOptions
{
	public string BackgroundPictureName { get; set; }
	public int BackgroundZIndex { get; set; }
	public byte DefaultDeactive { get; set; } = 0x2b;
	public byte DefaultBorder2Color { get; set; } = 0x23;
	public int BoxFillZIndex { get; set; } = 6;
	public int BoxBorderZIndex { get; set; } = 7;
	public int PictureZIndex { get; set; } = 5;
	public string FallbackFontName { get; set; } = "BIG";
	public bool RenderTextsBeforePictures { get; set; }
	public Func<PictureDefinition, string> PictureNameProvider { get; set; }
	public Action<PictureDefinition, TextureRect> AfterAddPicture { get; set; }
	public Func<TextDefinition, string> TextContentProvider { get; set; }
	public Func<TextDefinition, byte?> TextColorProvider { get; set; }
	public Action<TextDefinition, Label, Theme, string> AfterAddText { get; set; }
}
