using Godot;
using System.IO;
using System.Reflection;

namespace BenMcLean.Wolf3D.Shared.Setup;

/// <summary>
/// Provides access to the embedded IBM PC BIOS font for pixel-perfect text mode rendering.
/// VGA 9x16 font as a bitmap sprite sheet (16x16 grid, 256 characters).
/// </summary>
public static class BiosFont
{
	public const byte CharacterWidth = 9,
		CharacterHeight = 16,
		CharactersPerRow = 16;
	public const string ResourceName = "BenMcLean.Wolf3D.Shared.Resources.Bm437_IBM_VGA_9x16.png";
	private static ImageTexture _cachedTexture = null;
	private static Image _cachedImage = null;
	private static FontFile _cachedFontFile = null;
	private static Theme _cachedTheme = null;
	#region Getters
	/// <summary>
	/// Loads the embedded VGA 9x16 BIOS font as an ImageTexture.
	/// Returns a cached instance on subsequent calls.
	/// Use this for rendering characters from the sprite sheet.
	/// </summary>
	public static ImageTexture GetFontTexture()
	{
		if (_cachedTexture is not null)
			return _cachedTexture;
		Image img = GetFontImage();
		_cachedTexture = ImageTexture.CreateFromImage(img);
		return _cachedTexture;
	}
	/// <summary>
	/// Loads the embedded VGA 9x16 BIOS font as an Image.
	/// Returns a cached instance on subsequent calls.
	/// </summary>
	public static Image GetFontImage()
	{
		if (_cachedImage is not null)
			return _cachedImage;
		Assembly assembly = Assembly.GetExecutingAssembly();
		using Stream stream = assembly.GetManifestResourceStream(ResourceName)
			?? throw new FileNotFoundException($"Embedded font resource not found: \"{ResourceName}\"");
		using MemoryStream memoryStream = new();
		stream.CopyTo(memoryStream);
		Image image = new();
		Error error = image.LoadPngFromBuffer(memoryStream.ToArray());
		if (error != Error.Ok)
			throw new System.Exception($"Failed to load PNG font: {error}");
		return _cachedImage = image;
	}
	/// <summary>
	/// Gets the source rectangle for a character in the sprite sheet.
	/// </summary>
	/// <param name="charCode">ASCII/CP437 character code (0-255)</param>
	/// <returns>Rectangle coordinates in the sprite sheet</returns>
	public static Rect2I GetCharRect(int charCode) => new(
		x: charCode % CharactersPerRow * CharacterWidth,
		y: charCode / CharactersPerRow * CharacterHeight,
		width: CharacterWidth,
		height: CharacterHeight);
	/// <summary>
	/// Creates a FontFile that works with Godot's UI system (Label, RichTextLabel, etc.).
	/// Returns a cached instance on subsequent calls.
	/// Uses GDScript workaround because C# API doesn't expose glyph setup methods.
	/// </summary>
	public static FontFile GetFont()
	{
		if (_cachedFontFile is not null)
			return _cachedFontFile;
		Image image = GetFontImage();
		FontFile font = new()
		{
			FixedSize = CharacterHeight,
			Antialiasing = TextServer.FontAntialiasing.None,
		};
		Vector2I size = new(CharactersPerRow, 0),
			glSize = new(CharacterWidth, CharactersPerRow),
			advance = new(CharacterWidth, 0);
		font.SetTextureImage(0, size, 0, image);
		for (char glyph = (char)0; glyph < 256; glyph++)
		{
			int column = glyph % CharactersPerRow,
				row = glyph / CharactersPerRow;
			font.SetGlyphTextureIdx(
				cacheIndex: 0,
				size: size,
				glyph: glyph,
				textureIdx: 0);
			font.SetGlyphUVRect(
				cacheIndex: 0,
				size: size,
				glyph: glyph,
				uVRect: new(column * CharacterWidth, row * CharacterHeight, CharacterWidth, CharacterHeight));
			font.SetGlyphSize(
				cacheIndex: 0,
				size: size,
				glyph: glyph,
				glSize: glSize);
			font.SetGlyphOffset(
				cacheIndex: 0,
				size: size,
				glyph: glyph,
				offset: Vector2.Zero);
			font.SetGlyphAdvance(
				cacheIndex: 0,
				size: CharactersPerRow,
				glyph: glyph,
				advance: advance);
		}
		return _cachedFontFile = font;
	}
	/// <summary>
	/// Creates a Theme with the BIOS font configured.
	/// Returns a cached instance on subsequent calls.
	/// </summary>
	public static Theme GetTheme() =>
		_cachedTheme is not null ?
			_cachedTheme
			: _cachedTheme = new()
			{
				DefaultFont = GetFont(),
				DefaultFontSize = CharacterHeight,
			};
	#endregion Getters
	#region Drawing
	/// <summary>
	/// Helper to draw a character at a specific position using a CanvasItem.
	/// </summary>
	/// <param name="canvas">The CanvasItem to draw on (e.g., a TextureRect, Sprite2D, or custom node)</param>
	/// <param name="charCode">ASCII/CP437 character code (0-255)</param>
	/// <param name="position">Position to draw the character</param>
	/// <param name="modulate">Optional color modulation (default: white)</param>
	public static void DrawChar(CanvasItem canvas, int charCode, Vector2 position, Color? modulate = null)
	{
		ImageTexture texture = GetFontTexture();
		Rect2I srcRect = GetCharRect(charCode);
		canvas.DrawTextureRectRegion(
			texture: texture,
			rect: new Rect2(position, new Vector2(CharacterWidth, CharacterHeight)),
			srcRect: srcRect,
			modulate: modulate ?? Colors.White);
	}
	/// <summary>
	/// Helper to draw a string at a specific position using a CanvasItem.
	/// </summary>
	/// <param name="canvas">The CanvasItem to draw on</param>
	/// <param name="text">Text to draw (CP437 encoding)</param>
	/// <param name="position">Starting position</param>
	/// <param name="modulate">Optional color modulation (default: white)</param>
	public static void DrawString(CanvasItem canvas, string text, Vector2 position, Color? modulate = null)
	{
		float x = position.X,
			y = position.Y;
		foreach (char c in text)
		{
			if (c == '\n')
			{
				y += CharacterHeight;
				x = position.X;
				continue;
			}
			DrawChar(canvas, c, new Vector2(x, y), modulate);
			x += CharacterWidth;
		}
	}
	#endregion Drawing
}
