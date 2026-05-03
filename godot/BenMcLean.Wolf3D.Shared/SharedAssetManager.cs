using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using Godot;
using RectpackSharp;
using Microsoft.Extensions.Logging;
using static BenMcLean.Wolf3D.Shared.GodotLogger;
using BenMcLean.Wolf3D.Assets.Gameplay;
using BenMcLean.Wolf3D.Assets.Graphics;

namespace BenMcLean.Wolf3D.Shared;

/// <summary>
/// Singleton asset manager that provides global access to the currently loaded game assets.
/// Serves as the single source of truth for Assets.Assets across all display layers.
/// </summary>
public static class SharedAssetManager
{
	private const string EmbeddedSharewareZipResource = "BenMcLean.Wolf3D.Shared.Resources.Wolfenstein3dV14sw.ZIP";
	private const string SharewareXmlFileName = "WL1.xml";
	#region Data
	/// <summary>
	/// The currently loaded game data (VSwap, Maps, etc.).
	/// </summary>
	public static Assets.AssetManager CurrentGame { get; private set; }
	/// <summary>
	/// Game configuration (sound/music/input settings).
	/// Persisted to CONFIG file and used by menus and gameplay.
	/// Fires ConfigReplaced when the Config object itself is swapped (e.g., on game load).
	/// </summary>
	public static Assets.Gameplay.Config Config
	{
		get => _config;
		set
		{
			_config = value;
			ConfigReplaced?.Invoke(value);
		}
	}
	private static Assets.Gameplay.Config _config;
	/// <summary>
	/// Fired when the Config object is replaced (new game loaded or config cleared).
	/// The argument is the new Config, or null when cleared.
	/// </summary>
	public static event Action<Assets.Gameplay.Config> ConfigReplaced;
	/// <summary>
	/// Path to the CONFIG file for saving changes.
	/// </summary>
	private static string _configPath;
	/// <summary>
	/// Path to the game's XML definition file.
	/// Stored for save game file path derivation.
	/// </summary>
	public static string XmlPath { get; private set; }
	/// <summary>
	/// The master texture atlas containing VgaGraph Pics and Font characters.
	/// </summary>
	public static Godot.ImageTexture AtlasTexture { get; private set; }
	public static Godot.Image AtlasImage { get; private set; }
	/// <summary>
	/// Maps VgaGraph Pic names to AtlasTextures ready for 2D display.
	/// </summary>
	public static IReadOnlyDictionary<string, Godot.AtlasTexture> VgaGraph => _vgaGraph;
	private static Dictionary<string, Godot.AtlasTexture> _vgaGraph;
	/// <summary>
	/// Pre-computed 8×8 RGBA8888 automap tiles for VSwap sprite pages, keyed by page number.
	/// Each tile is cropped to the sprite's opaque content before downsampling, so the subject
	/// fills the tile rather than appearing small against a transparent border.
	/// Built once on load; used by AutomapRenderer as a fallback when no VgaGraph tile is set.
	/// </summary>
	public static IReadOnlyDictionary<ushort, byte[]> BonusAutomapTiles => _bonusAutomapTiles;
	private static Dictionary<ushort, byte[]> _bonusAutomapTiles;
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
	/// Maps DigiSound names to their logical Wolf3D sound names for non-digi fallback.
	/// </summary>
	public static IReadOnlyDictionary<string, string> DigiToLogicalSoundName => _digiToLogicalSoundName;
	private static Dictionary<string, string> _digiToLogicalSoundName;
	/// <summary>
	/// Maps logical Wolf3D sound names to their DigiSound names when a digi variant exists.
	/// </summary>
	public static IReadOnlyDictionary<string, string> LogicalToDigiSoundName => _logicalToDigiSoundName;
	private static Dictionary<string, string> _logicalToDigiSoundName;
	/// <summary>
	/// Pre-loaded IMF songs embedded in the Shared DLL (no game/AudioT required).
	/// Keyed by music name (e.g., "GAMESELECT_MUS").
	/// </summary>
	public static IReadOnlyDictionary<string, Assets.Sound.Imf[]> RawImfSongs => _rawImfSongs;
	private static readonly Dictionary<string, Assets.Sound.Imf[]> _rawImfSongs = LoadRawImfSongs();
	private static Dictionary<string, Assets.Sound.Imf[]> LoadRawImfSongs()
	{
		Dictionary<string, Assets.Sound.Imf[]> result = new(StringComparer.OrdinalIgnoreCase);
		Assembly assembly = Assembly.GetExecutingAssembly();
		string wlfName = Array.Find(assembly.GetManifestResourceNames(),
			n => n.EndsWith(".wlf", StringComparison.OrdinalIgnoreCase));
		if (wlfName is null)
			return result;
		try
		{
			using Stream stream = assembly.GetManifestResourceStream(wlfName);
			if (stream is not null)
				result["GAMESELECT_MUS"] = Assets.Sound.Imf.ReadImf(stream);
		}
		catch { }
		return result;
	}
	/// <summary>
	/// Crosshair AtlasTexture ready for display in VR and other modes.
	/// </summary>
	public static Godot.AtlasTexture Crosshair { get; private set; }
	/// <summary>
	/// Status bar definition from VgaGraph.
	/// </summary>
	public static StatusBarDefinition StatusBar => CurrentGame?.VgaGraph?.StatusBar;
	/// <summary>
	/// Logger factory configured to route logs to Godot.
	/// </summary>
	private static ILoggerFactory _loggerFactory;
	private static readonly Lazy<IReadOnlyDictionary<string, byte[]>> _embeddedSharewareFiles = new(LoadEmbeddedSharewareFiles);
	#endregion Data
	#region Textures
	/// <summary>
	/// Generates packing rectangles for all VSwap and VgaGraph textures.
	/// </summary>
	private static PackingRectangle[] GenerateRectangles()
	{
		List<PackingRectangle> rectangles = [];
		// Add rectangles for VgaGraph Pics
		if (CurrentGame.VgaGraph is not null)
		{
			rectangles.AddRange(
				Enumerable.Range(0, CurrentGame.VgaGraph.Pics.Length)
				.Select(i => new PackingRectangle(
					x: 0,
					y: 0,
					width: (uint)(CurrentGame.VgaGraph.Sizes[i << 1] + 2),
					height: (uint)(CurrentGame.VgaGraph.Sizes[(i << 1) + 1] + 2),
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
					height: (uint)(CurrentGame.VgaGraph.Sizes[picFont.Glyphs.Values.First() * 2 + 1] + 2),
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
		// Insert VgaGraph Pics
		Dictionary<int, Godot.Rect2I> vgaGraphRegions = CurrentGame?.VgaGraph?.Pics
			.Select((pic, picIndex) => (pic, picIndex))
			.Where(x => x.pic is not null)
			.Select((x, rectIndex) => new { x.pic, x.picIndex, rect = rectangles[rectIndex] })
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
			.Select((x, rectIndex) => new { x.font, x.fontIndex, x.glyphIndex, rect = rectangles[vgaGraphRegions.Count + rectIndex] })
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
		int rectIndex = vgaGraphRegions.Count + vgaGraphFontRegions.Count;
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
				height = CurrentGame.VgaGraph.Sizes[firstPicIndex * 2 + 1];
			// Draw solid color rectangle directly onto atlas
			uint color = CurrentGame.VgaGraph.Palette[picFont.SpaceColor];
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
				size: new Vector2(13, 11)),
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
		// Glyphs render from the baseline downward (offset Vector2.Zero), so they live
		// entirely in the descent region. Ascent = 0, descent = full font height.
		// Without these, get_height() returns 0 and multiline Labels collapse all lines
		// to the same Y position.
		font.SetCacheAscent(cacheIndex: 0, size: sourceFont.Height, ascent: 0);
		font.SetCacheDescent(cacheIndex: 0, size: sourceFont.Height, descent: sourceFont.Height);
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
			FixedSize = CurrentGame.VgaGraph.Sizes[picFont.Glyphs.Values.First() * 2 + 1],
			FixedSizeScaleMode = Godot.TextServer.FixedSizeScaleMode.IntegerOnly,
		};
		font.SetTextureImage(
			cacheIndex: 0,
			size: new Godot.Vector2I(font.FixedSize, 0),
			textureIndex: 0,
			image: AtlasImage);
		// See BuildChunkFont for rationale: ascent=0, descent=full height.
		font.SetCacheAscent(cacheIndex: 0, size: font.FixedSize, ascent: 0);
		font.SetCacheDescent(cacheIndex: 0, size: font.FixedSize, descent: font.FixedSize);
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
				uVRect: new Godot.Rect2(region.Position.X, region.Position.Y, region.Size.X, region.Size.Y));
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
				uVRect: new Godot.Rect2(spaceRegion.Position.X, spaceRegion.Position.Y, spaceRegion.Size.X, spaceRegion.Size.Y));
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
		_digiSounds = new(StringComparer.OrdinalIgnoreCase);
		_digiToLogicalSoundName = new(StringComparer.OrdinalIgnoreCase);
		_logicalToDigiSoundName = new(StringComparer.OrdinalIgnoreCase);
		if (CurrentGame?.VSwap?.DigiSoundsByName is null)
			return;
		foreach (System.Xml.Linq.XElement soundElement in CurrentGame.XML.Element("Audio")?.Elements("Sound") ?? [])
		{
			string logicalName = soundElement.Attribute("Name")?.Value;
			if (string.IsNullOrWhiteSpace(logicalName))
				continue;

			string digiName = soundElement.Attribute("Digi")?.Value;
			if (string.IsNullOrWhiteSpace(digiName))
				digiName = CurrentGame.VSwap.DigiSoundsByName.ContainsKey(logicalName) ? logicalName : null;

			if (string.IsNullOrWhiteSpace(digiName))
				continue;

			_digiToLogicalSoundName[digiName] = logicalName;
			if (!_logicalToDigiSoundName.ContainsKey(logicalName))
				_logicalToDigiSoundName[logicalName] = digiName;
		}
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

	public static bool IsDigitizedSoundEnabled =>
		Config?.DigiMode is Assets.Gameplay.Config.SDSMode.SoundBlaster
			or Assets.Gameplay.Config.SDSMode.SoundSource;

	public static string ResolveLogicalSoundName(string requestedSoundName)
	{
		if (string.IsNullOrWhiteSpace(requestedSoundName))
			return requestedSoundName;

		if (HasLogicalSound(requestedSoundName))
			return requestedSoundName;

		return _digiToLogicalSoundName is not null &&
			_digiToLogicalSoundName.TryGetValue(requestedSoundName, out string fallbackName) &&
			!string.IsNullOrWhiteSpace(fallbackName)
			? fallbackName
			: requestedSoundName;
	}

	public static bool TryGetDigiSound(
		string requestedSoundName,
		out Godot.AudioStreamWav stream,
		out string logicalSoundName)
	{
		logicalSoundName = ResolveLogicalSoundName(requestedSoundName);
		stream = null;

		if (string.IsNullOrWhiteSpace(requestedSoundName) || _digiSounds is null)
			return false;

		if (_digiSounds.TryGetValue(requestedSoundName, out stream))
			return true;

		return _logicalToDigiSoundName is not null &&
			_logicalToDigiSoundName.TryGetValue(logicalSoundName, out string digiSoundName) &&
			_digiSounds.TryGetValue(digiSoundName, out stream);
	}

	private static bool HasLogicalSound(string soundName) =>
		CurrentGame?.AudioT?.Sounds?.ContainsKey(soundName) == true ||
		CurrentGame?.AudioT?.PcSounds?.ContainsKey(soundName) == true;
	#endregion DigiSounds
	#region BonusAutomapTiles
	/// <summary>
	/// Builds pre-computed 8×8 RGBA automap tiles for all VSwap sprite pages.
	/// Each page is first cropped to its opaque content bounds (CropToContent), then
	/// downsampled to 8×8 so the subject fills the tile rather than appearing tiny.
	/// </summary>
	private static void BuildBonusAutomapTiles()
	{
		_bonusAutomapTiles = [];
		if (CurrentGame?.VSwap?.Pages is not byte[][] pages)
			return;
		// Only build tiles for ObjectType entries that have no AutomapTile attribute —
		// those are the only ones that will use the VSwap fallback path in the automap.
		HashSet<ushort> bonusPages = [..
			CurrentGame.XML.Element("VSwap")?.Element("StatInfo")?.Elements("ObjectType")
			.Where(e => e.Attribute("ObClass")?.Value == "bonus"
				&& e.Attribute("AutomapTile") is null
				&& ushort.TryParse(e.Attribute("Page")?.Value, out _))
			.Select(e => ushort.Parse(e.Attribute("Page").Value))
			?? []];
		if (bonusPages.Count == 0)
			return;
		ushort tileSqrt = CurrentGame.VSwap.TileSqrt;
		_bonusAutomapTiles = bonusPages
			.Where(pageNum => pageNum < pages.Length && pages[pageNum] is not null)
			.AsParallel()
			.Select(pageNum =>
			{
				byte[] cropped = pages[pageNum].CropToContent(
					out _, out _,
					out ushort w, out ushort h,
					tileSqrt);
				return new KeyValuePair<ushort, byte[]>(
					pageNum,
					w > 0 && h > 0 ? SampleToTile(cropped, w) : pages[pageNum]);
			})
			.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
	}

	/// <summary>
	/// Nearest-neighbour downsamples an RGBA8888 byte array of arbitrary size to an 8×8 tile.
	/// </summary>
	private static byte[] SampleToTile(byte[] rgba, ushort width=0)
	{
		ushort srcWidth = width < 1 ? (ushort)Math.Sqrt(rgba.Length >> 2) : width,
			srcHeight = (ushort)(width < 1 ? srcWidth : (rgba.Length >> 2) / width);
		const int size = Automap.AutomapRenderer.TilePixels;
		byte[] tile = new byte[size * size << 2];
		float scaleX = (float)srcWidth / size,
			scaleY = (float)srcHeight / size;
		for (int dy = 0; dy < size; dy++)
			for (int dx = 0; dx < size; dx++)
			{
				int srcX = (int)((dx + 0.5f) * scaleX),
					srcY = (int)((dy + 0.5f) * scaleY),
					src = (srcY * srcWidth + srcX) << 2,
					dst = (dy * size + dx) << 2;
				tile[dst]     = rgba[src];
				tile[dst + 1] = rgba[src + 1];
				tile[dst + 2] = rgba[src + 2];
				tile[dst + 3] = rgba[src + 3];
			}
		return tile;
	}
	#endregion BonusAutomapTiles
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
		Directory.CreateDirectory(gameFolder);
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
	private static IReadOnlyDictionary<string, byte[]> LoadEmbeddedSharewareFiles()
	{
		using Stream stream = Assembly.GetExecutingAssembly()
			.GetManifestResourceStream(EmbeddedSharewareZipResource);
		Dictionary<string, byte[]> files = new(StringComparer.OrdinalIgnoreCase);
		if (stream is null)
			return files;
		using ZipArchive archive = new(stream, ZipArchiveMode.Read);
		foreach (ZipArchiveEntry entry in archive.Entries)
		{
			if (string.IsNullOrEmpty(entry.Name))
				continue;
			using Stream entryStream = entry.Open();
			using MemoryStream memoryStream = new();
			entryStream.CopyTo(memoryStream);
			files[entry.Name] = memoryStream.ToArray();
		}
		return files;
	}
	/// <summary>
	/// Loads a game from the user's games folder.
	/// </summary>
	/// <param name="xmlPath">Path to the game's XML definition file (e.g., "WL6.xml")</param>
	public static void LoadGame(string xmlPath, bool preferEmbeddedShareware = false)
	{
		Cleanup();
		// Configure logging to route to Godot
		_loggerFactory = LoggerFactory.Create(builder =>
		{
			builder.AddProvider(new GodotLoggerProvider());
			builder.SetMinimumLevel(LogLevel.Warning);  // Only show warnings and errors
		});
		GameCatalog.GameDefinition gameDefinition = GameCatalog.Resolve(
			xmlPath,
			preferEmbeddedOfficial: preferEmbeddedShareware);
		XmlPath = System.IO.Path.GetFullPath(gameDefinition.XmlPath);
		bool usingEmbeddedSharewareData = ShouldUseEmbeddedSharewareData(xmlPath, gameDefinition, preferEmbeddedShareware);
		CurrentGame = usingEmbeddedSharewareData
			? Assets.AssetManager.Load(
				gameDefinition.Xml,
				gameDefinition.GameDataDirectory,
				OpenEmbeddedSharewareFile,
				EmbeddedSharewareFileExists,
				_loggerFactory)
			: new Assets.AssetManager(gameDefinition.Xml, gameDefinition.GameDataDirectory, _loggerFactory);
		BuildAtlas();
		BuildBonusAutomapTiles();
		BuildDigiSounds();
		LoadConfig(XmlPath);
		// On the initial boot flow we use embedded shareware assets to host the game-selection
		// menu. Force classic audio defaults for that temporary session so menu music/SFX still
		// work even if an external CONFIG.WL1 exists with sound disabled.
		if (preferEmbeddedShareware && usingEmbeddedSharewareData && Config is not null)
		{
			Config.SoundMode = Assets.Gameplay.Config.SDMode.AdLib;
			Config.MusicEnabled = true;
			Config.DigiMode = Assets.Gameplay.Config.SDSMode.SoundBlaster;
		}
	}
	public static bool RequiresExternalStorage(string xmlPath, bool preferEmbeddedShareware = false)
	{
		GameCatalog.GameDefinition gameDefinition = GameCatalog.Resolve(
			xmlPath,
			preferEmbeddedOfficial: preferEmbeddedShareware);
		return !ShouldUseEmbeddedSharewareData(xmlPath, gameDefinition, preferEmbeddedShareware);
	}
	private static bool ShouldUseEmbeddedSharewareData(string xmlPath, GameCatalog.GameDefinition gameDefinition, bool preferEmbeddedShareware)
	{
		if (!GameCatalog.CanUseEmbeddedSharewareData(gameDefinition))
			return false;
		if (preferEmbeddedShareware)
			return true;
		if (Path.GetFileName(xmlPath).Equals(SharewareXmlFileName, StringComparison.OrdinalIgnoreCase) &&
			!GameCatalog.HasExternalGameData(gameDefinition))
			return true;
		return !GameCatalog.HasExternalGameData(gameDefinition);
	}
	private static bool EmbeddedSharewareFileExists(string path) =>
		_embeddedSharewareFiles.Value.ContainsKey(Path.GetFileName(path));
	private static Stream OpenEmbeddedSharewareFile(string path)
	{
		if (!_embeddedSharewareFiles.Value.TryGetValue(Path.GetFileName(path), out byte[] bytes))
			throw new FileNotFoundException($"Embedded shareware file not found: {Path.GetFileName(path)}", path);
		return new MemoryStream(bytes, writable: false);
	}
	/// <summary>
	/// Get a Godot Color from a VGA palette index.
	/// Uses palette 0 (the default game palette).
	/// </summary>
	/// <param name="colorIndex">VGA palette color index (0-255)</param>
	/// <returns>Godot Color corresponding to the palette entry</returns>
	public static Godot.Color GetPaletteColor(byte colorIndex) => CurrentGame.VgaGraph.Palette[colorIndex].ToColor();
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
		XmlPath = null;
		if (_digiSounds is not null)
			foreach (Godot.AudioStreamWav sound in _digiSounds.Values)
				sound?.Dispose();
		_digiSounds?.Clear();
		_digiToLogicalSoundName?.Clear();
		_logicalToDigiSoundName?.Clear();
		if (_themes is not null)
			foreach (Godot.Theme theme in _themes.Values)
			{
				theme?.DefaultFont?.Dispose();
				theme?.Dispose();
			}
		_themes?.Clear();
		_bonusAutomapTiles?.Clear();
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
