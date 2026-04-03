using System;
using System.Collections.Generic;
using BenMcLean.Wolf3D.Assets.Gameplay;
using Godot;

namespace BenMcLean.Wolf3D.Shared.Minimap;

/// <summary>
/// Renders a Wolf3D-style minimap in a 320×160 Control node.
/// Matches the Super 3-D Noah's Ark / Wolf3D automap layout:
/// 8 pixels per tile, 40×20 tile viewport, scrolling centered on player,
/// clamped at map edges so no black border appears past the map.
/// Wall tiles are nearest-neighbour downsampled from VSwap pages —
/// works for any game, not just those with dedicated automap graphics.
/// </summary>
public partial class MinimapRenderer : Control
{
	// WL_MAP.C: automap is 40 tiles wide × 20 tiles tall, each tile 8×8 px = 320×160
	public const int TilePixels  = 8;
	public const int ViewWidth   = 320;
	public const int ViewHeight  = 160;

	private static readonly Color _floorColor = new(0.12f, 0.12f, 0.12f);
	private static readonly Color _doorColor  = new(0.50f, 0.50f, 0.50f);

	// Full-map baked image (mapWidth*8 × mapDepth*8 pixels)
	private Image        _backgroundImage;
	private ImageTexture _backgroundTexture;
	private int          _mapPixelWidth;
	private int          _mapPixelHeight;

	// Player state — set by UpdatePlayer(), drives viewport scroll and marker draw
	private ushort _playerTileX;
	private ushort _playerTileY;
	private short  _playerAngle;  // WL_DEF.H:player->angle (0-359, 0=east, counter-clockwise)

	// Lookup tables built from MapAnalysis in Init()
	// Key: (ushort tileX, ushort tileY) — value: VSwap page number for the wall texture
	private readonly Dictionary<(ushort, ushort), ushort> _wallTextures = [];
	private readonly HashSet<(ushort, ushort)>            _doorTiles    = [];

	private bool _initialized;

	/// <summary>
	/// Initialises the minimap from map analysis data.
	/// Builds wall/door lookup tables and bakes the static background image.
	/// Call once after the node is added to the scene tree, before the first frame.
	/// </summary>
	public void Init(MapAnalyzer.MapAnalysis mapAnalysis)
	{
		if (mapAnalysis is null)
		{
			GD.PrintErr("ERROR: MinimapRenderer.Init called with null mapAnalysis");
			return;
		}
		_wallTextures.Clear();
		_doorTiles.Clear();

		// WallSpawn list may have up to 4 entries per tile (one per visible face).
		// Dedup by position — first entry wins; any face texture is good enough for the minimap.
		foreach (MapAnalyzer.MapAnalysis.WallSpawn wall in mapAnalysis.Walls)
			_wallTextures.TryAdd((wall.X, wall.Y), wall.Shape);

		foreach (MapAnalyzer.MapAnalysis.DoorSpawn door in mapAnalysis.Doors)
			_doorTiles.Add((door.X, door.Y));

		// PushWalls are replaced with floor tiles in MapAnalysis.Walls (MapAnalyzer.cs replaces
		// them with FloorCodeFirst in realWalls before wall scanning).  Add them here so they
		// show as walls in the initial bake.  OnPushWallMoved() updates them after movement.
		foreach (MapAnalyzer.MapAnalysis.PushWallSpawn pushWall in mapAnalysis.PushWalls)
			_wallTextures.TryAdd((pushWall.X, pushWall.Y), pushWall.Shape);

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

		// C# idiom: int loop variables; cast to ushort only for dictionary key lookup
		for (int y = 0; y < mapDepth; y++)
		{
			for (int x = 0; x < mapWidth; x++)
			{
				(ushort, ushort) key = ((ushort)x, (ushort)y);
				if (_doorTiles.Contains(key))
					PaintSolidTile(x, y, _doorColor);
				else if (_wallTextures.TryGetValue(key, out ushort shape)
					&& SharedAssetManager.VSwap.TryGetValue(shape, out AtlasTexture atlasTexture))
					PaintWallTile(x, y, atlasTexture, atlasImage);
				// else: floor colour already filled by Image.Fill above
			}
		}

		_backgroundTexture = ImageTexture.CreateFromImage(_backgroundImage);
	}

	private void PaintSolidTile(int tileX, int tileY, Color color)
	{
		int destX = tileX * TilePixels,
			destY = tileY * TilePixels;
		for (int dy = 0; dy < TilePixels; dy++)
			for (int dx = 0; dx < TilePixels; dx++)
				_backgroundImage.SetPixel(destX + dx, destY + dy, color);
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
	// Public update API — called by the presentation layer from simulator events
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
	/// Updates the minimap when a pushwall finishes moving.
	/// Call when PushWallPositionChangedEvent fires with Action == PushWallAction.Idle.
	/// Repaints the vacated tile as floor and the destination tile as the wall texture,
	/// then uploads the updated image to the GPU.
	/// </summary>
	public void OnPushWallMoved(ushort oldX, ushort oldY, ushort newX, ushort newY, ushort shape)
	{
		if (!_initialized)
			return;

		_wallTextures.Remove((oldX, oldY));
		PaintSolidTile(oldX, oldY, _floorColor);

		_wallTextures[(newX, newY)] = shape;
		if (SharedAssetManager.VSwap.TryGetValue(shape, out AtlasTexture atlasTexture))
			PaintWallTile(newX, newY, atlasTexture, SharedAssetManager.AtlasImage);
		else
			PaintSolidTile(newX, newY, _floorColor);

		_backgroundTexture.Update(_backgroundImage);
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

		// Player tile marker
		int    screenX  = _playerTileX * TilePixels - viewX;
		int    screenY  = _playerTileY * TilePixels - viewY;
		Rect2  tileRect = new(screenX, screenY, TilePixels, TilePixels);
		DrawRect(tileRect, Colors.LimeGreen);

		// Direction indicator: line extending from tile centre in the player's facing direction
		// WL_DEF.H:player->angle 0=east; screen Y is down so negate sin
		Vector2 center = tileRect.GetCenter();
		float   rad    = _playerAngle * Mathf.Pi / 180f;
		Vector2 dir    = new(Mathf.Cos(rad), -Mathf.Sin(rad));
		DrawLine(center, center + dir * (TilePixels * 1.5f), Colors.White, 1f);
	}

	// ---------------------------------------------------------------------------
	// Cleanup
	// ---------------------------------------------------------------------------

	/// <summary>
	/// Frees the baked image resources when this node leaves the scene tree.
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
