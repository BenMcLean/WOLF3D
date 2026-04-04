using System;
using System.Collections.Generic;
using BenMcLean.Wolf3D.Assets.Gameplay;
using Godot;

namespace BenMcLean.Wolf3D.Shared.Automap;

/// <summary>
/// Renders the Wolf3D automap (WL_MAP.C:AutoMap / DrawAutoMap) in a 320×160 Control node.
/// Matches the Super 3-D Noah's Ark / Wolf3D automap layout:
/// 8 pixels per tile, 40×20 tile viewport, scrolling centered on player,
/// clamped at map edges so no black border appears past the map.
/// Wall tiles are nearest-neighbour downsampled from VSwap pages —
/// works for any game, not just those with dedicated automap graphics.
/// </summary>
public partial class AutomapRenderer : Control
{
	// WL_MAP.C: automap is 40 tiles wide × 20 tiles tall, each tile 8×8 px = 320×160
	public const int TilePixels  = 8;
	public const int ViewWidth   = 320;
	public const int ViewHeight  = 160;

	private Color _floorColor = new(0.12f, 0.12f, 0.12f);

	// Full-map baked image (mapWidth*8 × mapDepth*8 pixels)
	private Image        _backgroundImage;
	private ImageTexture _backgroundTexture;
	private int          _mapPixelWidth;
	private int          _mapPixelHeight;
	private int          _mapWidth;   // tile count, needed to index into _automapData

	// Flat array parallel to MapData: VSwap page per tile, ushort.MaxValue = floor/empty
	// Mirrors MapAnalysis.AutomapData — walls and doors baked here, pushwalls excluded
	private IReadOnlyList<ushort> _automapData;

	// Pushwall tiles — drawn dynamically in _Draw() so moves update without rebaking
	// Key: current tile position; Value: VSwap page number
	private readonly Dictionary<(ushort, ushort), ushort> _pushWallTextures = [];

	// Player state — set by UpdatePlayer(), drives viewport scroll and marker draw
	private ushort _playerTileX;
	private ushort _playerTileY;
	private short  _playerAngle;  // WL_DEF.H:player->angle (0-359, 0=east, counter-clockwise)

	private bool _initialized;

	/// <summary>
	/// Initialises the automap from map analysis data.
	/// Builds wall/door lookup tables and bakes the static background image.
	/// Call once after the node is added to the scene tree, before the first frame.
	/// </summary>
	public void Init(MapAnalyzer.MapAnalysis mapAnalysis)
	{
		if (mapAnalysis is null)
		{
			GD.PrintErr("ERROR: AutomapRenderer.Init called with null mapAnalysis");
			return;
		}
		_pushWallTextures.Clear();

		// Floor color from level data — VGA palette index if defined, dark grey fallback
		_floorColor = mapAnalysis.Floor is byte floorIndex
			? SharedAssetManager.GetPaletteColor(floorIndex)
			: new Color(0.12f, 0.12f, 0.12f);

		// Snapshot the flat automap array from MapAnalysis — walls + doors, no pushwalls
		_automapData = mapAnalysis.AutomapData;
		_mapWidth    = mapAnalysis.Width;

		// PushWalls are NOT baked into the static background — they move, so they're drawn
		// dynamically in _Draw().  MapAnalyzer.cs already excludes them from AutomapData
		// (replaces with FloorCodeFirst before scanning), so the background correctly shows floor.
		foreach (MapAnalyzer.MapAnalysis.PushWallSpawn pushWall in mapAnalysis.PushWalls)
			_pushWallTextures[(pushWall.X, pushWall.Y)] = pushWall.Shape;

		_mapPixelWidth  = mapAnalysis.Width * TilePixels;
		_mapPixelHeight = mapAnalysis.Depth * TilePixels;

		CustomMinimumSize = new Vector2(ViewWidth, ViewHeight);

		BakeBackground(mapAnalysis.Width, mapAnalysis.Depth);
		_initialized = true;
		QueueRedraw();
	}

	// ---------------------------------------------------------------------------
	// Background baking
	// ---------------------------------------------------------------------------

	private void BakeBackground(ushort mapWidth, ushort mapDepth)
	{
		_backgroundImage = Image.CreateEmpty(_mapPixelWidth, _mapPixelHeight, false, Image.Format.Rgba8);
		_backgroundImage.Fill(_floorColor);

		Image atlasImage = SharedAssetManager.AtlasImage;

		// WL_MAP.C:DrawMapWalls — iterate flat array, skip floor/empty sentinel
		for (int i = 0; i < _automapData.Count; i++)
		{
			ushort page = _automapData[i];
			if (page == ushort.MaxValue)
				continue; // floor — already filled above
			int x = i % mapWidth, y = i / mapWidth;
			if (SharedAssetManager.VSwap.TryGetValue(page, out AtlasTexture atlasTexture))
				PaintWallTile(x, y, atlasTexture, atlasImage);
		}

		_backgroundTexture = ImageTexture.CreateFromImage(_backgroundImage);
	}

	private void PaintWallTile(int tileX, int tileY, AtlasTexture atlasTexture, Image atlasImage)
	{
		Rect2 region = atlasTexture.Region;
		int   destX  = tileX * TilePixels,
			  destY  = tileY * TilePixels;
		float scaleX = region.Size.X / TilePixels,
			  scaleY = region.Size.Y / TilePixels;
		for (int dy = 0; dy < TilePixels; dy++)
		{
			for (int dx = 0; dx < TilePixels; dx++)
			{
				int srcX = (int)(dx * scaleX) + (int)region.Position.X;
				int srcY = (int)(dy * scaleY) + (int)region.Position.Y;
				_backgroundImage.SetPixel(destX + dx, destY + dy, atlasImage.GetPixel(srcX, srcY));
			}
		}
	}

	// ---------------------------------------------------------------------------
	// Public update API — called by AutomapController
	// ---------------------------------------------------------------------------

	/// <summary>
	/// Updates the player's tile position and facing angle.
	/// Convert from simulator: tileX = PlayerX >> 16, tileY = PlayerY >> 16.
	/// WL_DEF.H:player->angle — 0-359 degrees, 0 = east, counter-clockwise.
	/// </summary>
	public void UpdatePlayer(ushort tileX, ushort tileY, short angle)
	{
		_playerTileX = tileX;
		_playerTileY = tileY;
		_playerAngle = angle;
		QueueRedraw();
	}

	/// <summary>
	/// Updates the automap when a pushwall finishes moving.
	/// Call when PushWallPositionChangedEvent fires with Action == PushWallAction.Idle.
	/// Moves the pushwall entry in the dynamic overlay; the static background is untouched
	/// because pushwalls are never baked into it.
	/// </summary>
	public void OnPushWallMoved(ushort oldX, ushort oldY, ushort newX, ushort newY, ushort shape)
	{
		if (!_initialized)
			return;

		_pushWallTextures.Remove((oldX, oldY));
		_pushWallTextures[(newX, newY)] = shape;
		QueueRedraw();
	}

	// ---------------------------------------------------------------------------
	// Rendering
	// ---------------------------------------------------------------------------

	public override void _Draw()
	{
		if (!_initialized || _backgroundTexture is null)
			return;

		// Scroll viewport to keep player centred, clamped so we never go past map edge
		int viewX = Math.Clamp(
			_playerTileX * TilePixels - ViewWidth  / 2,
			0, Math.Max(0, _mapPixelWidth  - ViewWidth));
		int viewY = Math.Clamp(
			_playerTileY * TilePixels - ViewHeight / 2,
			0, Math.Max(0, _mapPixelHeight - ViewHeight));

		// Black background — visible only when the map is smaller than 320×160
		DrawRect(new Rect2(0, 0, ViewWidth, ViewHeight), Colors.Black);

		// Background map region (one draw call for the entire map background)
		int drawW = Math.Min(ViewWidth,  _mapPixelWidth  - viewX);
		int drawH = Math.Min(ViewHeight, _mapPixelHeight - viewY);
		if (drawW > 0 && drawH > 0)
			DrawTextureRectRegion(
				_backgroundTexture,
				new Rect2(0, 0, drawW, drawH),
				new Rect2(viewX, viewY, drawW, drawH));

		// Pushwall overlay — drawn on top of the static background, below the player marker
		foreach (((ushort pwX, ushort pwY), ushort pwShape) in _pushWallTextures)
		{
			int pwScreenX = pwX * TilePixels - viewX;
			int pwScreenY = pwY * TilePixels - viewY;
			// Skip tiles fully outside the viewport
			if (pwScreenX + TilePixels <= 0 || pwScreenX >= ViewWidth
				|| pwScreenY + TilePixels <= 0 || pwScreenY >= ViewHeight)
				continue;
			if (SharedAssetManager.VSwap.TryGetValue(pwShape, out AtlasTexture pwAtlas))
				DrawTextureRect(
					pwAtlas,
					new Rect2(pwScreenX, pwScreenY, TilePixels, TilePixels),
					false);
			else
				DrawRect(new Rect2(pwScreenX, pwScreenY, TilePixels, TilePixels), _floorColor);
		}

		// Player tile marker
		int    screenX  = _playerTileX * TilePixels - viewX;
		int    screenY  = _playerTileY * TilePixels - viewY;
		Rect2  tileRect = new(screenX, screenY, TilePixels, TilePixels);
		DrawRect(tileRect, Colors.LimeGreen);

		// Direction indicator: line extending from tile centre in the player's facing direction.
		// Engine angle convention (ExtensionMethods.cs:ToWolf3DAngle): wolf3dAngle = 90 - godotDegrees
		// so wolf3dAngle 0 = West on screen — negate cos to flip X axis to match.
		Vector2 center = tileRect.GetCenter();
		float   rad    = _playerAngle * Mathf.Pi / 180f;
		Vector2 dir    = new(-Mathf.Cos(rad), -Mathf.Sin(rad));
		DrawLine(center, center + dir * (TilePixels * 1.5f), Colors.White, 1f);
	}

	// ---------------------------------------------------------------------------
	// Cleanup
	// ---------------------------------------------------------------------------

	/// <summary>
	/// Frees the baked texture resource when this node leaves the scene tree.
	/// </summary>
	public override void _ExitTree()
	{
		_backgroundTexture?.Dispose();
		_backgroundTexture = null;
		_backgroundImage?.Dispose();
		_backgroundImage   = null;
		_initialized       = false;
	}
}
