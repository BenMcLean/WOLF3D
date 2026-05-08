using BenMcLean.Wolf3D.Assets.Menu;
using System;
using Godot;

namespace BenMcLean.Wolf3D.Shared.Text;

/// <summary>
/// Shared text layout helper used by menus and the status bar so both honor the same
/// alignment and fixed-field rules.
/// </summary>
public static class TextLayoutHelper
{
	private static bool IsRightAligned(TextDefinition textDef) =>
		textDef.Align?.Equals("Right", StringComparison.OrdinalIgnoreCase) == true;
	private static bool HasFixedRightAlignedField(TextDefinition textDef) =>
		IsRightAligned(textDef) && !string.IsNullOrWhiteSpace(textDef.Content);
	private static float GetRightAlignedFieldWidth(TextDefinition textDef, Font font, int fontSize)
	{
		string fieldTemplate = textDef.Content ?? string.Empty;
		float fieldWidth = font.GetStringSize(fieldTemplate, fontSize: fontSize).X;
		return fieldWidth > 0
			? fieldWidth
			: font.GetStringSize(" ", fontSize: fontSize).X;
	}
	public static Label CreateLabel(TextDefinition textDef, Theme theme, string content, Color? textColor = null)
	{
		Font font = theme.DefaultFont;
		int fontSize = theme.DefaultFontSize;
		Label label = new()
		{
			Text = content ?? textDef.Content ?? string.Empty,
			Theme = theme,
			LabelSettings = new LabelSettings
			{
				Font = font,
				FontSize = fontSize,
				LineSpacing = 0,
			}
		};

		if (textColor.HasValue)
			label.LabelSettings.FontColor = textColor.Value;

		if (textDef.MaxWidth.HasValue)
		{
			label.AutowrapMode = TextServer.AutowrapMode.Word;
			label.CustomMinimumSize = new Vector2(textDef.MaxWidth.Value, 0);
		}

		if (HasFixedRightAlignedField(textDef))
		{
			float fieldWidth = GetRightAlignedFieldWidth(textDef, font, fontSize);
			label.Size = new Vector2(fieldWidth, fontSize);
			label.CustomMinimumSize = new Vector2(
				x: Math.Max(label.CustomMinimumSize.X, fieldWidth),
				y: Math.Max(label.CustomMinimumSize.Y, fontSize));
			label.HorizontalAlignment = HorizontalAlignment.Right;
		}

		return label;
	}

	public static Vector2 GetPosition(
		TextDefinition textDef,
		Theme theme,
		string content,
		float canvasWidth,
		float canvasHeight)
	{
		Font font = theme.DefaultFont;
		int fontSize = theme.DefaultFontSize;
		Vector2 textSize = font.GetStringSize(content ?? textDef.Content ?? string.Empty, fontSize: fontSize);
		float x = textDef.CenterX ? (canvasWidth - textSize.X) / 2f
			: textDef.RightX ? canvasWidth - textSize.X
			: textDef.XValue;
		if (IsRightAligned(textDef) && !textDef.CenterX && !textDef.RightX)
			x -= HasFixedRightAlignedField(textDef)
				? GetRightAlignedFieldWidth(textDef, font, fontSize)
				: textSize.X;
		return new Vector2(
			x: x,
			y: textDef.CenterY ? (canvasHeight - textSize.Y) / 2f
				: textDef.BottomY ? canvasHeight - textSize.Y
				: textDef.YValue);
	}
}
