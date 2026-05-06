using BenMcLean.Wolf3D.Assets.Menu;
using Godot;

namespace BenMcLean.Wolf3D.Shared.Layout;

/// <summary>
/// Shared rendering helpers for non-interactive canvas layout primitives.
/// </summary>
public static class CanvasLayoutRenderHelper
{
	public static Vector2 GetPicturePosition(
		PictureDefinition pictureDef,
		Texture2D texture,
		float canvasWidth,
		float canvasHeight)
	{
		Vector2 textureSize = GetTextureSize(texture);
		return new Vector2(
			x: pictureDef.CenterX ? (canvasWidth - textureSize.X) / 2f
				: pictureDef.RightX ? canvasWidth - textureSize.X
				: pictureDef.XValue,
			y: pictureDef.CenterY ? (canvasHeight - textureSize.Y) / 2f
				: pictureDef.BottomY ? canvasHeight - textureSize.Y
				: pictureDef.YValue);
	}

	public static TextureRect CreatePictureRect(
		PictureDefinition pictureDef,
		Texture2D texture,
		float canvasWidth,
		float canvasHeight,
		int defaultZIndex)
	{
		return new TextureRect
		{
			Texture = texture,
			Position = GetPicturePosition(pictureDef, texture, canvasWidth, canvasHeight),
			Size = GetTextureSize(texture),
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			ZIndex = pictureDef.ZIndex ?? defaultZIndex,
		};
	}

	public static void AddBox(
		Control canvas,
		MenuBoxDefinition boxDef,
		byte defaultDeactive,
		byte defaultBorder2Color,
		int fillZIndex,
		int borderZIndex)
	{
		Color? bgColor = boxDef.BkgdColor.HasValue
			? SharedAssetManager.GetPaletteColor(boxDef.BkgdColor.Value)
			: null;

		if (boxDef.Bevel)
		{
			int backgroundZIndex = boxDef.ZIndex ?? fillZIndex;
			int outlineZIndex = boxDef.ZIndex.HasValue ? boxDef.ZIndex.Value + 1 : borderZIndex;
			DrawBevelledBox(
				canvas,
				boxDef.X,
				boxDef.Y,
				boxDef.W,
				boxDef.H,
				bgColor,
				SharedAssetManager.GetPaletteColor(boxDef.Deactive ?? defaultDeactive),
				SharedAssetManager.GetPaletteColor(boxDef.Bord2Color ?? defaultBorder2Color),
				backgroundZIndex,
				outlineZIndex);
			return;
		}

		if (bgColor.HasValue)
		{
			canvas.AddChild(new ColorRect
			{
				Color = bgColor.Value,
				Position = new Vector2(boxDef.X, boxDef.Y),
				Size = new Vector2(boxDef.W, boxDef.H),
				ZIndex = boxDef.ZIndex ?? fillZIndex,
			});
		}
	}

	public static void DrawBevelledBox(
		Control canvas,
		float x,
		float y,
		float w,
		float h,
		Color? bgColor,
		Color colorNW,
		Color colorSE,
		int bgZIndex,
		int borderZIndex)
	{
		if (bgColor.HasValue)
		{
			canvas.AddChild(new ColorRect
			{
				Color = bgColor.Value,
				Position = new Vector2(x, y),
				Size = new Vector2(w, h),
				ZIndex = bgZIndex,
			});
		}

		AddOutline(canvas, x, y, w, h, colorNW, colorSE, borderZIndex);
	}

	public static void AddOutline(
		Control canvas,
		float x,
		float y,
		float w,
		float h,
		Color topLeft,
		Color bottomRight,
		int zIndex)
	{
		canvas.AddChild(new ColorRect { Color = topLeft, Position = new Vector2(x, y), Size = new Vector2(w + 1f, 1f), ZIndex = zIndex });
		canvas.AddChild(new ColorRect { Color = topLeft, Position = new Vector2(x, y), Size = new Vector2(1f, h + 1f), ZIndex = zIndex });
		canvas.AddChild(new ColorRect { Color = bottomRight, Position = new Vector2(x, y + h), Size = new Vector2(w + 1f, 1f), ZIndex = zIndex });
		canvas.AddChild(new ColorRect { Color = bottomRight, Position = new Vector2(x + w, y), Size = new Vector2(1f, h + 1f), ZIndex = zIndex });
	}

	private static Vector2 GetTextureSize(Texture2D texture) =>
		texture is AtlasTexture atlasTexture
			? atlasTexture.Region.Size
			: texture.GetSize();
}
