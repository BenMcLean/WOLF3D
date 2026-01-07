using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using RectpackSharp;
using Microsoft.Extensions.Logging;
using static BenMcLean.Wolf3D.Shared.GodotLogger;
using BenMcLean.Wolf3D.Assets.Graphics;

namespace BenMcLean.Wolf3D.Shared;

/// <summary>
/// Singleton asset manager that provides global access to the currently loaded game assets.
/// Serves as the single source of truth for Assets.Assets across all display layers.
/// </summary>
public static class SharedAssetManager
{
	#region Data
	/// <summary>
	/// The currently loaded game data (VSwap, Maps, etc.).
	/// </summary>
	public static Assets.AssetManager CurrentGame { get; private set; }
	/// <summary>
	/// Game configuration (sound/music/input settings).
	/// Persisted to CONFIG file and used by menus and gameplay.
	/// </summary>
	public static Assets.Gameplay.Config Config { get; set; }
	/// <summary>
	/// Path to the CONFIG file for saving changes.
	/// </summary>
	private static string _configPath;
	/// <summary>
	/// The master texture atlas containing all VSwap pages, VgaGraph Pics, and Font characters.
	/// </summary>
	public static Godot.ImageTexture AtlasTexture { get; private set; }
	public static Godot.Image AtlasImage { get; private set; }
	/// <summary>
	/// Maps VSwap page indices to AtlasTextures ready for 2D display.
	/// </summary>
	public static IReadOnlyDictionary<int, Godot.AtlasTexture> VSwap => _vswap;
	private static Dictionary<int, Godot.AtlasTexture> _vswap;
	/// <summary>
	/// Maps VgaGraph Pic names to AtlasTextures ready for 2D display.
	/// </summary>
	public static IReadOnlyDictionary<string, Godot.AtlasTexture> VgaGraph => _vgaGraph;
	private static Dictionary<string, Godot.AtlasTexture> _vgaGraph;
	/// <summary>
	/// Dictionary of Godot Theme objects with fonts configured, keyed by font name.
	/// Includes both chunk fonts (from VGAGRAPH) and pic fonts (prefix-based).
	/// </summary>
	public static IReadOnlyDictionary<string, Godot.Theme> Themes => _themes;
	private static Dictionary<string, Godot.Theme> _themes;
	/// <summary>
	/// Maps DigiSound names to AudioStreamWAV resources ready for playback.
	/// </summary>
	public static IReadOnlyDictionary<string, Godot.AudioStreamWav> DigiSounds => _digiSounds;
	private static Dictionary<string, Godot.AudioStreamWav> _digiSounds;
	/// <summary>
	/// Crosshair AtlasTexture ready for display in VR and other modes.
	/// </summary>
	public static Godot.AtlasTexture Crosshair { get; private set; }
	/// <summary>
	/// Logger factory configured to route logs to Godot.
	/// </summary>
	private static ILoggerFactory _loggerFactory;
	#endregion Data
	#region Textures
	/// <summary>
	/// Generates packing rectangles for all VSwap and VgaGraph textures.
	/// </summary>
	private static PackingRectangle[] GenerateRectangles()
	{
		List<PackingRectangle> rectangles = [];
		// Add rectangles for VSwap pages (walls and sprites)
		uint vSwapSize = (uint)CurrentGame?.VSwap?.TileSqrt + 2;
		if (CurrentGame?.VSwap?.Pages is not null)
			foreach (byte[] page in CurrentGame.VSwap.Pages
				.Where(page => page is not null))
					rectangles.Add(new PackingRectangle(
						x: 0,
						y: 0,
						width: vSwapSize,
						height: vSwapSize,
						id: rectangles.Count));
		// Add rectangles for VgaGraph Pics
		if (CurrentGame.VgaGraph is not null)
		{
			rectangles.AddRange(
				Enumerable.Range(0, CurrentGame.VgaGraph.Pics.Length)
				.Select(i => new PackingRectangle(
					x: 0,
					y: 0,
					width: (uint)(CurrentGame.VgaGraph.Sizes[i][0] + 2),
					height: (uint)(CurrentGame.VgaGraph.Sizes[i][1] + 2),
					id: rectangles.Count)));
			// Add rectangles for VgaGraph Font characters
			foreach (Assets.Graphics.Font font in CurrentGame.VgaGraph.Fonts)
				for (uint charCode = 0; charCode < font.Glyphs.Length; charCode++)
					if (font.Widths[charCode] > 0)
						rectangles.Add(new PackingRectangle(
							x: 0,
							y: 0,
							width: (uint)(font.Widths[charCode] + 2),
							height: (uint)(font.Height + 2),
							id: rectangles.Count));
			// Add rectangles for PicFont space characters
			foreach ((string fontName, VgaGraph.PicFont picFont) in
				CurrentGame.VgaGraph.PicFonts
				.Where(kvp => kvp.Value.SpaceWidth > 0))
				rectangles.Add(new PackingRectangle(
					x: 0,
					y: 0,
					width: (uint)(picFont.SpaceWidth + 2),
					height: (uint)(CurrentGame.VgaGraph.Sizes[picFont.Glyphs.Values.First()][1] + 2),
					id: rectangles.Count));
		}
		// Add rectangle for crosshair
		rectangles.Add(new PackingRectangle(
			x: 0,
			y: 0,
			width: 15,
			height: 13,
			id: rectangles.Count));
		return [.. rectangles];
	}
	/// <summary>
	/// Builds the texture atlas from all VSwap pages, VgaGraph Pics, and Font characters.
	/// </summary>
	private static void BuildAtlas()
	{
		// Initialize texture dictionaries
		// Generate and pack rectangles
		PackingRectangle[] rectangles = GenerateRectangles();
		RectanglePacker.Pack(rectangles, out PackingRectangle bounds, PackingHints.TryByBiggerSide);
		rectangles = [.. rectangles.OrderBy(r => r.Id)];
		// Calculate atlas size as next power of 2
		ushort atlasSize = ((ushort)Math.Max(bounds.Width, bounds.Height)).NextPowerOf2();
		// Create atlas canvas
		byte[] atlas = new byte[atlasSize * atlasSize << 2];
		// Insert textures into atlas
		// Insert VSwap pages
		Dictionary<int, Godot.Rect2I> vswapRegions = CurrentGame?.VSwap?.Pages
			.Select((page, pageIndex) => (page, pageIndex))
			.Where(x => x.page is not null)
			.Select((x, rectIndex) => new { x.page, x.pageIndex, rect = rectangles[rectIndex] })
			.AsParallel()
			.Select(work =>
			{
				atlas.DrawInsert(
					x: (ushort)(work.rect.X + 1),
					y: (ushort)(work.rect.Y + 1),
					insert: work.page,
					insertWidth: CurrentGame.VSwap.TileSqrt,
					width: atlasSize);
				return new KeyValuePair<int, Godot.Rect2I>(
					key: work.pageIndex,
					value: new Godot.Rect2I(
						x: (int)(work.rect.X + 1),
						y: (int)(work.rect.Y + 1),
						width: CurrentGame.VSwap.TileSqrt,
						height: CurrentGame.VSwap.TileSqrt));
			})
			.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
		// Insert VgaGraph Pics
		Dictionary<int, Godot.Rect2I> vgaGraphRegions = CurrentGame?.VgaGraph?.Pics
			.Select((pic, picIndex) => (pic, picIndex))
			.Where(x => x.pic is not null)
			.Select((x, rectIndex) => new { x.pic, x.picIndex, rect = rectangles[vswapRegions.Count + rectIndex] })
			.AsParallel()
			.Select(work =>
			{
				atlas.DrawInsert(
					x: (ushort)(work.rect.X + 1),
					y: (ushort)(work.rect.Y + 1),
					insert: work.pic,
					insertWidth: (ushort)(work.rect.Width - 2),
					width: atlasSize);
				return new KeyValuePair<int, Godot.Rect2I>(
					key: work.picIndex,
					value: new Godot.Rect2I(
						x: (int)work.rect.X + 1,
						y: (int)work.rect.Y + 1,
						width: (int)work.rect.Width - 2,
						height: (int)work.rect.Height - 2));
			})
			.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
		// Insert VgaGraph Chunk Font characters
		// Font regions use uint keys with composite format: (fontIndex << 16) | charCode
		// This packs font index in upper 16 bits and character code in lower 16 bits
		// uint prevents sign-extension issues with bit-shift operations
		Dictionary<uint, Rect2I> vgaGraphFontRegions = CurrentGame.VgaGraph.Fonts
			.SelectMany((font, fontIndex) => font.Glyphs.Select((glyph, glyphIndex) => new { font, fontIndex, glyph, glyphIndex }))
			.Where(x => x.font.Widths[x.glyphIndex] > 0)
			.Select((x, rectIndex) => new { x.font, x.fontIndex, x.glyphIndex, rect = rectangles[vswapRegions.Count + vgaGraphRegions.Count + rectIndex] })
			.AsParallel()
			.Select(work =>
			{
				atlas.DrawInsert(
					x: (ushort)(work.rect.X + 1),
					y: (ushort)(work.rect.Y + 1),
					insert: work.font.Glyphs[work.glyphIndex],
					insertWidth: work.font.Widths[work.glyphIndex],
					width: atlasSize);
				return new KeyValuePair<uint, Rect2I>(
					key: ((uint)work.fontIndex << 16) | (uint)work.glyphIndex,
					value: new Godot.Rect2I(
						x: (int)(work.rect.X + 1),
						y: (int)(work.rect.Y + 1),
						width: work.font.Widths[work.glyphIndex],
						height: work.font.Height));
			})
			.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
		// Update rectIndex for subsequent single-threaded operations
		int rectIndex = vswapRegions.Count + vgaGraphRegions.Count + vgaGraphFontRegions.Count;
		// Insert PicFont space characters
		Dictionary<string, Godot.Rect2I> picFontSpaceRegions = [];
		foreach ((string fontName, VgaGraph.PicFont picFont) in
			CurrentGame.VgaGraph.PicFonts
			.Where(kvp => kvp.Value.SpaceWidth > 0))
		{
			PackingRectangle rect = rectangles[rectIndex++];
			// Get height from first character in the pic font
			int firstPicIndex = picFont.Glyphs.Values.First();
			ushort width = picFont.SpaceWidth,
				height = CurrentGame.VgaGraph.Sizes[firstPicIndex][1];
			// Draw solid color rectangle directly onto atlas
			uint color = CurrentGame.VgaGraph.Palettes[0][picFont.SpaceColor]; // Use default palette
			atlas.DrawRectangle(
				x: (int)(rect.X + 1),
				y: (int)(rect.Y + 1),
				color: color,
				rectWidth: width,
				rectHeight: height,
				width: atlasSize);
			picFontSpaceRegions[fontName] = new Godot.Rect2I(
				x: (int)(rect.X + 1),
				y: (int)(rect.Y + 1),
				width: width,
				height: height);
		}
		PackingRectangle crosshair = rectangles[rectIndex];
		atlas.DrawCrosshair(
			x: (int)(crosshair.X + 1),
			y: (int)(crosshair.Y + 1),
			width: atlasSize);
		// Create Godot texture from atlas
		AtlasImage = Godot.Image.CreateFromData(
			width: atlasSize,
			height: atlasSize,
			useMipmaps: false,
			format: Godot.Image.Format.Rgba8,
			data: atlas);
		AtlasTexture = Godot.ImageTexture.CreateFromImage(AtlasImage);
		// Build AtlasTextures for 2D display
		// Build VSwap AtlasTextures (indexed by page number)
		_vswap = vswapRegions.ToDictionary(kvp => kvp.Key, kvp => new Godot.AtlasTexture()
		{
			Atlas = AtlasTexture,
			Region = new Godot.Rect2(kvp.Value.Position, kvp.Value.Size),
		});
		// Build VgaGraph AtlasTextures by name
		_vgaGraph = [];
		if (CurrentGame?.VgaGraph?.PicsByName is not null)
			foreach ((string name, int picIndex) in CurrentGame.VgaGraph.PicsByName)
				if (vgaGraphRegions.TryGetValue(picIndex, out Godot.Rect2I region))
					_vgaGraph[name] = new()
					{
						Atlas = AtlasTexture,
						Region = new Godot.Rect2(region.Position, region.Size),
					};
		// Build chunk fonts
		_themes = [];
		foreach ((string name, int index) in CurrentGame.VgaGraph.ChunkFontsByName)
			if (BuildChunkFont(index, vgaGraphFontRegions) is Godot.FontFile font)
				_themes[name] = new()
				{
					DefaultFont = font,
					DefaultFontSize = font.FixedSize,
				};
		// Build pic fonts
		foreach ((string name, VgaGraph.PicFont picFont) in CurrentGame.VgaGraph.PicFonts)
			if (BuildPicFont(name, picFont, vgaGraphRegions, picFontSpaceRegions) is Godot.FontFile font)
				_themes[name] = new()
				{
					DefaultFont = font,
					DefaultFontSize = font.FixedSize,
				};
		// Build Crosshair AtlasTexture
		Crosshair = new()
		{
			Atlas = AtlasTexture,
			Region = new Godot.Rect2(
				position: new Vector2(
					x: (int)(crosshair.X + 1),
					y: (int)(crosshair.Y + 1)),
				size: new Vector2(11, 13)),
		};
	}
	#endregion Textures
	#region Fonts
	/// <summary>
	/// Builds a regular Godot Font from a VgaGraph font chunk.
	/// </summary>
	private static Godot.FontFile BuildChunkFont(
		int fontIndex,
		Dictionary<uint, Godot.Rect2I> vgaGraphFontRegions)
	{
		if (CurrentGame?.VgaGraph?.Fonts is null
			|| fontIndex >= CurrentGame.VgaGraph.Fonts.Length)
			return null;
		Assets.Graphics.Font sourceFont = CurrentGame.VgaGraph.Fonts[fontIndex];
		Godot.FontFile font = new()
		{
			Antialiasing = Godot.TextServer.FontAntialiasing.None,
			FixedSize = sourceFont.Height,
			FixedSizeScaleMode = Godot.TextServer.FixedSizeScaleMode.IntegerOnly,
		};
		font.SetTextureImage(
			cacheIndex: 0,
			size: new Godot.Vector2I(sourceFont.Height, 0),
			textureIndex: 0,
			image: AtlasImage);
		for (int charCode = 0; charCode < sourceFont.Glyphs.Length; charCode++)
		{
			if (sourceFont.Widths[charCode] == 0)
				continue;
			// Composite key: fontIndex in upper 16 bits, charCode in lower 16 bits
			uint key = ((uint)fontIndex << 16) | (char)charCode;
			if (!vgaGraphFontRegions.TryGetValue(key, out Godot.Rect2I region))
				continue;
			font.SetGlyphTextureIdx(
				cacheIndex: 0,
				size: new Godot.Vector2I(sourceFont.Height, 0),
				glyph: charCode,
				textureIdx: 0);
			font.SetGlyphUVRect(
				cacheIndex: 0,
				size: new Godot.Vector2I(sourceFont.Height, 0),
				glyph: charCode,
				uVRect: new Godot.Rect2(region.Position.X, region.Position.Y, region.Size.X, region.Size.Y));
			font.SetGlyphAdvance(
				cacheIndex: 0,
				size: sourceFont.Height,
				glyph: charCode,
				advance: new Godot.Vector2(sourceFont.Widths[charCode], 0));
			font.SetGlyphSize(
				cacheIndex: 0,
				size: new Godot.Vector2I(sourceFont.Height, 0),
				glyph: charCode,
				glSize: region.Size);
			font.SetGlyphOffset(
				cacheIndex: 0,
				size: new Godot.Vector2I(sourceFont.Height, 0),
				glyph: charCode,
				offset: Godot.Vector2.Zero);
		}
		return font;
	}
	/// <summary>
	/// Builds a prefix-based Godot Font using VgaGraph.PicFonts.
	/// </summary>
	private static Godot.FontFile BuildPicFont(
		string fontName,
		VgaGraph.PicFont picFont,
		Dictionary<int, Godot.Rect2I> vgaGraphRegions,
		Dictionary<string, Godot.Rect2I> picFontSpaceRegions)
	{
		Godot.FontFile font = new()
		{
			Antialiasing = Godot.TextServer.FontAntialiasing.None,
			FixedSize = CurrentGame.VgaGraph.Sizes[picFont.Glyphs.Values.First()][1],
			FixedSizeScaleMode = Godot.TextServer.FixedSizeScaleMode.IntegerOnly,
		};
		font.SetTextureImage(
			cacheIndex: 0,
			size: new Godot.Vector2I(font.FixedSize, 0),
			textureIndex: 0,
			image: AtlasImage);
		foreach ((char character, int picNumber) in picFont.Glyphs)
		{
			if (!vgaGraphRegions.TryGetValue(picNumber, out Godot.Rect2I region))
				continue;
			int charCode = character;
			font.SetGlyphTextureIdx(
				cacheIndex: 0,
				size: new Godot.Vector2I(font.FixedSize, 0),
				glyph: charCode,
				textureIdx: 0);
			font.SetGlyphUVRect(
				cacheIndex: 0,
				size: new Godot.Vector2I(font.FixedSize, 0),
				glyph: charCode,
				uVRect: new Godot.Rect2(
					position: new Godot.Vector2(
						x: region.Position.X / (float)AtlasTexture.GetWidth(),
						y: region.Position.Y / (float)AtlasTexture.GetHeight()),
					size: new Godot.Vector2(
						x: region.Size.X / (float)AtlasTexture.GetWidth(),
						y: region.Size.Y / (float)AtlasTexture.GetHeight())));
			font.SetGlyphAdvance(
				cacheIndex: 0,
				size: font.FixedSize,
				glyph: charCode,
				advance: new Godot.Vector2(region.Size.X, 0));
			font.SetGlyphSize(
				cacheIndex: 0,
				size: new Godot.Vector2I(font.FixedSize, 0),
				glyph: charCode,
				glSize: region.Size);
			font.SetGlyphOffset(
				cacheIndex: 0,
				size: new Godot.Vector2I(font.FixedSize, 0),
				glyph: charCode,
				offset: new Godot.Vector2I(0, 0));
		}
		// Add space character if it has a textured region
		if (picFontSpaceRegions.TryGetValue(fontName, out Godot.Rect2I spaceRegion))
		{
			const char space = ' ';
			font.SetGlyphTextureIdx(
				cacheIndex: 0,
				size: new Godot.Vector2I(font.FixedSize, 0),
				glyph: space,
				textureIdx: 0);
			font.SetGlyphUVRect(
				cacheIndex: 0,
				size: new Godot.Vector2I(font.FixedSize, 0),
				glyph: space,
				uVRect: new Godot.Rect2(
					position: new Godot.Vector2(
						x: spaceRegion.Position.X / (float)AtlasTexture.GetWidth(),
						y: spaceRegion.Position.Y / (float)AtlasTexture.GetHeight()),
					size: new Godot.Vector2(
						x: spaceRegion.Size.X / (float)AtlasTexture.GetWidth(),
						y: spaceRegion.Size.Y / (float)AtlasTexture.GetHeight())));
			font.SetGlyphAdvance(
				cacheIndex: 0,
				size: font.FixedSize,
				glyph: space,
				advance: new Godot.Vector2(spaceRegion.Size.X, 0));
			font.SetGlyphSize(
				cacheIndex: 0,
				size: new Godot.Vector2I(font.FixedSize, 0),
				glyph: space,
				glSize: spaceRegion.Size);
			font.SetGlyphOffset(
				cacheIndex: 0,
				size: new Godot.Vector2I(font.FixedSize, 0),
				glyph: space,
				offset: new Godot.Vector2I(0, 0));
		}
		return font;
	}
	#endregion Fonts
	#region DigiSounds
	/// <summary>
	/// Builds AudioStreamWAV resources from VSwap DigiSounds.
	/// Wolfenstein 3D uses 8-bit PCM audio at 7000Hz sample rate.
	/// </summary>
	private static void BuildDigiSounds()
	{
		_digiSounds = [];
		if (CurrentGame?.VSwap?.DigiSoundsByName is null)
			return;
		foreach ((string name, byte[] pcmData) in CurrentGame.VSwap.DigiSoundsByName)
		{
			if (pcmData is null || pcmData.Length == 0)
				continue;
			Godot.AudioStreamWav audioStream = new()
			{
				Format = Godot.AudioStreamWav.FormatEnum.Format8Bits,
				MixRate = 7042, // Adam Biser said 7042 Hz is the correct frequency
				Stereo = false,
				Data = pcmData,
			};
			_digiSounds[name] = audioStream;
		}
	}
	#endregion DigiSounds
	#region Config
	/// <summary>
	/// Loads the CONFIG file for the current game.
	/// If the file doesn't exist, creates a new Config with default values.
	/// </summary>
	/// <param name="xmlPath">Path to the game's XML definition file</param>
	private static void LoadConfig(string xmlPath)
	{
		// Get the CONFIG file path from XML attribute HighScores
		string configFileName = CurrentGame.XML.Attribute("HighScores")?.Value;
		if (string.IsNullOrWhiteSpace(configFileName))
		{
			// No config file specified, create default
			Config = new Assets.Gameplay.Config(Assets.Gameplay.Config.ConfigFormat.Wolf3D);
			return;
		}
		// Determine ConfigFormat based on game name
		string gameName = CurrentGame.XML.Attribute("Name")?.Value ?? "";
		Assets.Gameplay.Config.ConfigFormat format = gameName.Contains("Noah", StringComparison.OrdinalIgnoreCase)
			? Assets.Gameplay.Config.ConfigFormat.NoahsArk
			: Assets.Gameplay.Config.ConfigFormat.Wolf3D;
		// Get the full path to the CONFIG file
		string gameFolder = System.IO.Path.Combine(
			System.IO.Path.GetDirectoryName(xmlPath),
			CurrentGame.XML.Attribute("Path")?.Value ?? "");
		_configPath = System.IO.Path.Combine(gameFolder, configFileName);
		// Load or create config
		if (System.IO.File.Exists(_configPath))
		{
			using System.IO.FileStream stream = System.IO.File.OpenRead(_configPath);
			Config = Assets.Gameplay.Config.Load(stream, format);
		}
		else
		{
			// Create default config if file doesn't exist
			Config = new Assets.Gameplay.Config(format);
		}
	}
	/// <summary>
	/// Saves the current Config to disk.
	/// Should be called when exiting the game or when important settings change.
	/// </summary>
	public static void SaveConfig()
	{
		if (Config is null || string.IsNullOrWhiteSpace(_configPath))
			return;
		try
		{
			using System.IO.FileStream stream = System.IO.File.Create(_configPath);
			Config.Save(stream);
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"ERROR: Failed to save config to {_configPath}: {ex.Message}");
		}
	}
	#endregion Config
	/// <summary>
	/// Loads a game from the user's games folder.
	/// </summary>
	/// <param name="xmlPath">Path to the game's XML definition file (e.g., "WL6.xml")</param>
	public static void LoadGame(string xmlPath)
	{
		Cleanup();
		// Configure logging to route to Godot
		_loggerFactory = LoggerFactory.Create(builder =>
		{
			builder.AddProvider(new GodotLoggerProvider());
			builder.SetMinimumLevel(LogLevel.Warning);  // Only show warnings and errors
		});
		CurrentGame = Assets.AssetManager.Load(xmlPath, _loggerFactory);
		BuildAtlas();
		BuildDigiSounds();
		LoadConfig(xmlPath);
	}
	/// <summary>
	/// Get a Godot Color from a VGA palette index.
	/// Uses palette 0 (the default game palette).
	/// </summary>
	/// <param name="colorIndex">VGA palette color index (0-255)</param>
	/// <returns>Godot Color corresponding to the palette entry</returns>
	public static Godot.Color GetPaletteColor(byte colorIndex) => CurrentGame.VgaGraph.Palettes[0][colorIndex].ToColor();
	/// <summary>
	/// Disposes all loaded resources to free memory.
	/// Called before loading a new game or on program exit.
	/// </summary>
	public static void Cleanup()
	{
		// Save config before cleanup
		SaveConfig();
		Config = null;
		_configPath = null;
		if (_digiSounds is not null)
			foreach (Godot.AudioStreamWav sound in _digiSounds.Values)
				sound?.Dispose();
		_digiSounds?.Clear();
		if (_themes is not null)
			foreach (Godot.Theme theme in _themes.Values)
			{
				theme?.DefaultFont?.Dispose();
				theme?.Dispose();
			}
		_themes?.Clear();
		if (_vswap is not null)
			foreach (AtlasTexture atlasTexture in _vswap.Values)
				atlasTexture.Dispose();
		_vswap?.Clear();
		if (_vgaGraph is not null)
			foreach (AtlasTexture atlasTexture in _vgaGraph.Values)
				atlasTexture.Dispose();
		_vgaGraph?.Clear();
		Crosshair?.Dispose();
		Crosshair = null;
		AtlasTexture?.Dispose();
		AtlasTexture = null;
		AtlasImage?.Dispose();
		AtlasImage = null;
		CurrentGame = null;
		_loggerFactory?.Dispose();
		_loggerFactory = null;
	}
}
