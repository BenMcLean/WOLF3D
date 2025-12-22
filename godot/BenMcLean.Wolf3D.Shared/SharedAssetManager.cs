using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using RectpackSharp;

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
	public static Assets.Assets CurrentGame { get; private set; }
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
	/// Dictionary of Godot FontFile objects, keyed by font name.
	/// Includes both chunk fonts (from VGAGRAPH) and pic fonts (prefix-based).
	/// </summary>
	public static IReadOnlyDictionary<string, Godot.FontFile> Fonts => _fonts;
	private static Dictionary<string, Godot.FontFile> _fonts;
	/// <summary>
	/// Maps DigiSound names to AudioStreamWAV resources ready for playback.
	/// </summary>
	public static IReadOnlyDictionary<string, Godot.AudioStreamWav> DigiSounds => _digiSounds;
	private static Dictionary<string, Godot.AudioStreamWav> _digiSounds;
	#endregion Data
	#region Textures
	/// <summary>
	/// Generates packing rectangles for all VSwap and VgaGraph textures.
	/// </summary>
	private static PackingRectangle[] GenerateRectangles()
	{
		List<PackingRectangle> rectangles = [];
		// Add rectangles for VSwap pages (walls and sprites)
		uint tileSqrt = CurrentGame.VSwap.TileSqrt;
		for (int page = 0; page < CurrentGame.VSwap.Pages.Length; page++)
			if (CurrentGame.VSwap.Pages[page] is not null)
				rectangles.Add(new PackingRectangle(
					x: 0,
					y: 0,
					width: tileSqrt + 2,
					height: tileSqrt + 2,
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
			for (uint fontIndex = 0; fontIndex < CurrentGame.VgaGraph.Fonts.Length; fontIndex++)
			{
				Assets.VgaGraph.Font font = CurrentGame.VgaGraph.Fonts[fontIndex];
				for (uint charCode = 0; charCode < font.Character.Length; charCode++)
					if (font.Width[charCode] > 0)
						rectangles.Add(new PackingRectangle(
							x: 0,
							y: 0,
							width: (uint)(font.Width[charCode] + 2),
							height: (uint)(font.Height + 2),
							id: rectangles.Count));
			}
			// Add rectangles for PicFont space characters
			foreach ((string fontName, Assets.VgaGraph.PicFont picFont) in
				CurrentGame.VgaGraph.PicFonts
				.Where(kvp => kvp.Value.SpaceWidth > 0))
			{
				// Get height from first character in the pic font
				int firstPicIndex = picFont.Characters.Values.First();
				ushort height = CurrentGame.VgaGraph.Sizes[firstPicIndex][1];
				rectangles.Add(new PackingRectangle(
					x: 0,
					y: 0,
					width: (uint)(picFont.SpaceWidth + 2),
					height: (uint)(height + 2),
					id: rectangles.Count));
			}
		}
		return [.. rectangles];
	}
	/// <summary>
	/// Builds the texture atlas from all VSwap pages, VgaGraph Pics, and Font characters.
	/// </summary>
	private static void BuildAtlas()
	{
		// Initialize texture dictionaries
		_vswap = [];
		_vgaGraph = [];
		// Create temporary region dictionaries for building
		Dictionary<int, Godot.Rect2I> vswapRegions = [];
		Dictionary<int, Godot.Rect2I> vgaGraphRegions = [];
		// Font regions use uint keys with composite format: (fontIndex << 16) | charCode
		// This packs font index in upper 16 bits and character code in lower 16 bits
		// uint prevents sign-extension issues with bit-shift operations
		Dictionary<uint, Godot.Rect2I> vgaGraphFontRegions = [];
		Dictionary<string, Godot.Rect2I> picFontSpaceRegions = [];
		// Generate and pack rectangles
		PackingRectangle[] rectangles = GenerateRectangles();
		RectanglePacker.Pack(rectangles, out PackingRectangle bounds, PackingHints.TryByBiggerSide);
		// Calculate atlas size as next power of 2
		uint atlasSize = Math.Max(bounds.Width, bounds.Height).NextPowerOf2();
		// Create atlas canvas
		byte[] atlas = new byte[(atlasSize * atlasSize) << 2];
		// Insert textures into atlas
		uint rectIndex = 0;
		// Insert VSwap pages
		for (int page = 0; page < CurrentGame.VSwap.Pages.Length; page++)
			if (CurrentGame.VSwap.Pages[page] is not null)
			{
				PackingRectangle rect = rectangles[rectIndex++];
				atlas.DrawInsert(
					x: (ushort)(rect.X + 1),
					y: (ushort)(rect.Y + 1),
					insert: CurrentGame.VSwap.Pages[page],
					insertWidth: CurrentGame.VSwap.TileSqrt,
					width: (ushort)atlasSize);
				vswapRegions[page] = new Godot.Rect2I(
					x: (int)(rect.X + 1),
					y: (int)(rect.Y + 1),
					width: CurrentGame.VSwap.TileSqrt,
					height: CurrentGame.VSwap.TileSqrt);
			}
		// Insert VgaGraph Pics
		if (CurrentGame.VgaGraph is not null)
		{
			for (int i = 0; i < CurrentGame.VgaGraph.Pics.Length; i++)
			{
				PackingRectangle rect = rectangles[rectIndex++];
				ushort width = CurrentGame.VgaGraph.Sizes[i][0],
					height = CurrentGame.VgaGraph.Sizes[i][1];
				atlas.DrawInsert(
					x: (ushort)(rect.X + 1),
					y: (ushort)(rect.Y + 1),
					insert: CurrentGame.VgaGraph.Pics[i],
					insertWidth: width,
					width: (ushort)atlasSize);
				vgaGraphRegions[i] = new Godot.Rect2I(
					x: (int)(rect.X + 1),
					y: (int)(rect.Y + 1),
					width: width,
					height: height);
			}
			// Insert VgaGraph Chunk Font characters
			for (uint fontIndex = 0; fontIndex < CurrentGame.VgaGraph.Fonts.Length; fontIndex++)
			{
				Assets.VgaGraph.Font font = CurrentGame.VgaGraph.Fonts[fontIndex];
				for (uint charCode = 0; charCode < font.Character.Length; charCode++)
					if (font.Width[charCode] > 0)
					{
						PackingRectangle rect = rectangles[rectIndex++];
						byte width = font.Width[charCode];
						ushort height = font.Height;
						atlas.DrawInsert(
							x: (ushort)(rect.X + 1),
							y: (ushort)(rect.Y + 1),
							insert: font.Character[charCode],
							insertWidth: width,
							width: (ushort)atlasSize);
						// Composite key: fontIndex in upper 16 bits, charCode in lower 16 bits
						vgaGraphFontRegions[(fontIndex << 16) | charCode] = new Godot.Rect2I(
							x: (int)(rect.X + 1),
							y: (int)(rect.Y + 1),
							width: width,
							height: height);
					}
			}
			// Insert PicFont space characters
			foreach ((string fontName, Assets.VgaGraph.PicFont picFont) in
				CurrentGame.VgaGraph.PicFonts
				.Where(kvp => kvp.Value.SpaceWidth > 0))
			{
				PackingRectangle rect = rectangles[rectIndex++];
				// Get height from first character in the pic font
				int firstPicIndex = picFont.Characters.Values.First();
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
					width: (ushort)atlasSize);
				picFontSpaceRegions[fontName] = new Godot.Rect2I(
					x: (int)(rect.X + 1),
					y: (int)(rect.Y + 1),
					width: width,
					height: height);
			}
		}
		// Create Godot texture from atlas
		AtlasImage = Godot.Image.CreateFromData(
			width: (int)atlasSize,
			height: (int)atlasSize,
			useMipmaps: false,
			format: Godot.Image.Format.Rgba8,
			data: atlas);
		AtlasTexture = Godot.ImageTexture.CreateFromImage(AtlasImage);
		// Build AtlasTextures for 2D display
		BuildAtlasTextures(vswapRegions, vgaGraphRegions);
		// Build all fonts (both regular and prefix-based)
		BuildFonts(vgaGraphRegions, vgaGraphFontRegions, picFontSpaceRegions);
	}
	/// <summary>
	/// Builds AtlasTextures for 2D display from VSwap and VgaGraph regions.
	/// </summary>
	private static void BuildAtlasTextures(
		Dictionary<int, Godot.Rect2I> vswapRegions,
		Dictionary<int, Godot.Rect2I> vgaGraphRegions)
	{
		// Build VSwap AtlasTextures (indexed by page number)
		foreach (KeyValuePair<int, Godot.Rect2I> kvp in vswapRegions)
		{
			Godot.AtlasTexture atlasTexture = new()
			{
				Atlas = AtlasTexture,
				Region = new Godot.Rect2(kvp.Value.Position, kvp.Value.Size),
			};
			_vswap[kvp.Key] = atlasTexture;
		}
		// Build VgaGraph AtlasTextures by name
		if (CurrentGame?.VgaGraph?.PicsByName is not null)
			foreach ((string name, int picIndex) in CurrentGame.VgaGraph.PicsByName)
			{
				if (!vgaGraphRegions.TryGetValue(picIndex, out Godot.Rect2I region))
					continue;
				Godot.AtlasTexture atlasTexture = new()
				{
					Atlas = AtlasTexture,
					Region = new Godot.Rect2(region.Position, region.Size),
				};
				_vgaGraph[name] = atlasTexture;
			}
	}
	#endregion Textures
	#region Fonts
	/// <summary>
	/// Builds Godot FontFile objects for all fonts defined in XML.
	/// Handles both chunk fonts (from VGAGRAPH) and pic fonts (prefix-based).
	/// </summary>
	private static void BuildFonts(
		Dictionary<int, Godot.Rect2I> vgaGraphRegions,
		Dictionary<uint, Godot.Rect2I> vgaGraphFontRegions,
		Dictionary<string, Godot.Rect2I> picFontSpaceRegions)
	{
		if (CurrentGame?.VgaGraph is null)
			return;
		_fonts = [];
		// Build chunk fonts
		foreach ((string name, int index) in CurrentGame.VgaGraph.ChunkFontsByName)
		{
			Godot.FontFile font = BuildChunkFont(index, vgaGraphFontRegions);
			if (font is not null)
				_fonts[name] = font;
		}
		// Build pic fonts
		foreach ((string name, Assets.VgaGraph.PicFont picFont) in CurrentGame.VgaGraph.PicFonts)
		{
			Godot.FontFile font = BuildPicFont(name, picFont, vgaGraphRegions, picFontSpaceRegions);
			if (font is not null)
				_fonts[name] = font;
		}
	}
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
		Assets.VgaGraph.Font sourceFont = CurrentGame.VgaGraph.Fonts[fontIndex];
		Godot.FontFile font = new()
		{
			FixedSize = sourceFont.Height,
		};
		font.SetTextureImage(
			cacheIndex: 0,
			size: new Godot.Vector2I(sourceFont.Height, 0),
			textureIndex: 0,
			image: AtlasImage);
		for (int charCode = 0; charCode < sourceFont.Character.Length; charCode++)
		{
			if (sourceFont.Width[charCode] == 0)
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
				uVRect: new Godot.Rect2(
					position: new Godot.Vector2(
						x: region.Position.X / (float)AtlasTexture.GetWidth(),
						y: region.Position.Y / (float)AtlasTexture.GetHeight()),
					size: new Godot.Vector2(
						x: region.Size.X / (float)AtlasTexture.GetWidth(),
						y: region.Size.Y / (float)AtlasTexture.GetHeight())));
			font.SetGlyphAdvance(
				cacheIndex: 0,
				size: sourceFont.Height,
				glyph: charCode,
				advance: new Godot.Vector2(sourceFont.Width[charCode], 0));
			font.SetGlyphSize(
				cacheIndex: 0,
				size: new Godot.Vector2I(sourceFont.Height, 0),
				glyph: charCode,
				glSize: region.Size);
			font.SetGlyphOffset(
				cacheIndex: 0,
				size: new Godot.Vector2I(sourceFont.Height, 0),
				glyph: charCode,
				offset: new Godot.Vector2I(0, 0));
		}
		return font;
	}
	/// <summary>
	/// Builds a prefix-based Godot Font using VgaGraph.PicFonts.
	/// </summary>
	private static Godot.FontFile BuildPicFont(
		string fontName,
		Assets.VgaGraph.PicFont picFont,
		Dictionary<int, Godot.Rect2I> vgaGraphRegions,
		Dictionary<string, Godot.Rect2I> picFontSpaceRegions)
	{
		Godot.FontFile font = new()
		{
			FixedSize = CurrentGame.VgaGraph.Sizes[picFont.Characters.Values.First()][1],
		};
		font.SetTextureImage(
			cacheIndex: 0,
			size: new Godot.Vector2I(font.FixedSize, 0),
			textureIndex: 0,
			image: AtlasImage);
		foreach ((char character, int picNumber) in picFont.Characters)
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
			int spaceCode = ' ';
			font.SetGlyphTextureIdx(
				cacheIndex: 0,
				size: new Godot.Vector2I(font.FixedSize, 0),
				glyph: spaceCode,
				textureIdx: 0);
			font.SetGlyphUVRect(
				cacheIndex: 0,
				size: new Godot.Vector2I(font.FixedSize, 0),
				glyph: spaceCode,
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
				glyph: spaceCode,
				advance: new Godot.Vector2(spaceRegion.Size.X, 0));
			font.SetGlyphSize(
				cacheIndex: 0,
				size: new Godot.Vector2I(font.FixedSize, 0),
				glyph: spaceCode,
				glSize: spaceRegion.Size);
			font.SetGlyphOffset(
				cacheIndex: 0,
				size: new Godot.Vector2I(font.FixedSize, 0),
				glyph: spaceCode,
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
	/// <summary>
	/// Loads a game from the user's games folder.
	/// </summary>
	/// <param name="xmlPath">Path to the game's XML definition file (e.g., "WL6.xml")</param>
	public static void LoadGame(string xmlPath)
	{
		Cleanup();
		CurrentGame = Assets.Assets.Load(xmlPath);
		BuildAtlas();
		BuildDigiSounds();
	}
	/// <summary>
	/// Disposes all loaded resources to free memory.
	/// Called before loading a new game or on program exit.
	/// </summary>
	public static void Cleanup()
	{
		if (_digiSounds is not null)
			foreach (Godot.AudioStreamWav sound in _digiSounds.Values)
				sound?.Dispose();
		_digiSounds?.Clear();
		if (_fonts is not null)
			foreach (Godot.FontFile font in _fonts.Values)
				font?.Dispose();
		_fonts?.Clear();
		if (_vswap is not null)
			foreach (AtlasTexture atlasTexture in _vswap.Values)
				atlasTexture.Dispose();
		_vswap?.Clear();
		if (_vgaGraph is not null)
			foreach (AtlasTexture atlasTexture in _vgaGraph.Values)
				atlasTexture.Dispose();
		_vgaGraph?.Clear();
		AtlasTexture?.Dispose();
		AtlasTexture = null;
		AtlasImage?.Dispose();
		AtlasImage = null;
		CurrentGame = null;
	}
}
