using System;
using System.Collections.Generic;
using BenMcLean.Wolf3D.Assets.Gameplay;
using BenMcLean.Wolf3D.Simulator.Entities;
using Godot;

namespace BenMcLean.Wolf3D.Shared.Automap;

/// <summary>
/// Renders the Wolf3D automap (WL_MAP.C:AutoMap / DrawAutoMap) in a 320×160 Control node.
/// Matches the Super 3-D Noah's Ark / Wolf3D automap layout:
/// 8 pixels per tile, 40×20 tile viewport, scrolling centered on player,
/// clamped at map edges so no black border appears past the map.
/// Wall tiles use VgaGraph 8×8 tiles when available (WL_MAP.C:VWB_DrawTile8),
/// otherwise nearest-neighbour downsampled directly from raw VSwap RGBA pages.
/// </summary>
public partial class AutomapRenderer : Control
{
	// WL_MAP.C: automap is 40 tiles wide × 20 tiles tall, each tile 8×8 px = 320×160
	public const int TilePixels = 8;
	public const int ViewWidth = 320;
	public const int ViewHeight = 160;

	private Color _floorColor = new(0.12f, 0.12f, 0.12f);
	private Color _ceilingColor = new(0.08f, 0.08f, 0.08f);

	// Two baked images (init-time only):
	//   _litImage  — floor color bg + light-side (NS) wall pages
	//   _dimImage  — ceiling color bg + dark-side (EW) wall pages
	// One composite image (rebuilt when fog changes, CPU-side only):
	//   _compositeImage — per-tile selection from lit/dim/black based on fog state
	private Image _litImage;
	private Image _dimImage;
	private Image _compositeImage;
	private ImageTexture _compositeTexture;
	private bool _compositeDirty;
	private int _mapPixelWidth;
	private int _mapPixelHeight;
	private int _mapWidth;   // tile count, needed to index into _automapData

	// Flat array parallel to MapData: VSwap page per tile, ushort.MaxValue = floor/empty
	// Mirrors MapAnalysis.AutomapData — walls and doors baked here, pushwalls excluded
	private IReadOnlyList<ushort> _automapData;
	private MapAnalyzer.MapAnalysis _mapAnalysis; // kept for AutomapTileHasDimVariant

	// Fog-of-war snapshots — updated by UpdateFog(), consumed when composite is rebuilt
	private IReadOnlyList<bool> _fogEverSeen = [];
	private IReadOnlyList<bool> _fogCurrentlyVisible = [];

	// Pushwall tiles — painted into composite on moves; Value is VgaGraph tile index when
	// UsesVgaGraphWallTiles, otherwise VSwap page number (same as AutomapData convention).
	private readonly Dictionary<(ushort, ushort), ushort> _pushWallTextures = [];

	// Bonus items — set by UpdateBonuses() once on level load; read each _Draw()
	// WL_MAP.C:DrawMapPrizes — items with ShapeNum == -1 have been collected and are skipped
	private StatObj[] _statObjList;

	// Player state — set by UpdatePlayer(), drives viewport scroll and marker draw
	private ushort _playerTileX;
	private ushort _playerTileY;
	private short _playerAngle;  // WL_DEF.H:player->angle (0-359, 0=east, counter-clockwise)

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
		// Ceiling color for the dim (discovered-but-not-visible) layer
		_ceilingColor = mapAnalysis.Ceiling is byte ceilingIndex
			? SharedAssetManager.GetPaletteColor(ceilingIndex)
			: new Color(0.08f, 0.08f, 0.08f);

		// Snapshot the flat automap array from MapAnalysis — walls + doors, no pushwalls
		_automapData = mapAnalysis.AutomapData;
		_mapAnalysis = mapAnalysis;
		_mapWidth = mapAnalysis.Width;

		// PushWalls are NOT baked into the static background — they move, so their tile is
		// painted into the composite on each rebuild.  MapAnalyzer.cs already excludes them
		// from AutomapData (replaces with FloorCodeFirst), so the background shows floor.
		// Store VgaGraph tile index for VgaGraph games, VSwap page for others.
		foreach (MapAnalyzer.MapAnalysis.PushWallSpawn pushWall in mapAnalysis.PushWalls)
			_pushWallTextures[(pushWall.X, pushWall.Y)] = mapAnalysis.UsesVgaGraphWallTiles
				? mapAnalysis.VgaGraphTileForWallShape(pushWall.Shape)
				: pushWall.Shape;

		_mapPixelWidth = mapAnalysis.Width * TilePixels;
		_mapPixelHeight = mapAnalysis.Depth * TilePixels;
		_fogEverSeen = [];
		_fogCurrentlyVisible = [];

		CustomMinimumSize = new Vector2(ViewWidth, ViewHeight);

		BakeBackground(mapAnalysis.Width);
		_initialized = true;
		QueueRedraw();
	}

	// ---------------------------------------------------------------------------
	// Background baking
	// ---------------------------------------------------------------------------

	private void BakeBackground(ushort mapWidth)
	{
		// Lit image: floor color background, light-side (NS/even) wall pages
		_litImage = Image.CreateEmpty(_mapPixelWidth, _mapPixelHeight, false, Image.Format.Rgba8);
		_litImage.Fill(_floorColor);

		// Dim image: ceiling color background, dark-side (EW/odd) wall pages where available
		_dimImage = Image.CreateEmpty(_mapPixelWidth, _mapPixelHeight, false, Image.Format.Rgba8);
		_dimImage.Fill(_ceilingColor);

		// WL_MAP.C:DrawMapWalls — iterate flat array, skip floor/empty sentinel
		byte[][] vgaGraphTiles = SharedAssetManager.CurrentGame?.VgaGraph?.Tiles;
		bool useVgaGraphWalls = _mapAnalysis.UsesVgaGraphWallTiles && vgaGraphTiles != null;
		byte[][] vswapPages = SharedAssetManager.CurrentGame?.VSwap?.Pages;
		int pageSqrt = SharedAssetManager.CurrentGame?.VSwap?.TileSqrt ?? 64;
		for (int i = 0; i < _automapData.Count; i++)
		{
			ushort tileOrPage = _automapData[i];
			if (tileOrPage == ushort.MaxValue)
				continue; // floor — already filled above
			int x = i % mapWidth, y = i / mapWidth;
			if (useVgaGraphWalls)
			{
				// VgaGraph tile path (WL_MAP.C:VWB_DrawTile8): 8×8 pixels, no light/dark pairing.
				// Both lit and dim images get the same tile — background color differs.
				if (tileOrPage < vgaGraphTiles.Length && vgaGraphTiles[tileOrPage] is byte[] tileData)
				{
					PaintTileFromRgbaBytes(_litImage, x, y, tileData);
					PaintTileFromRgbaBytes(_dimImage, x, y, tileData);
				}
			}
			else if (vswapPages != null)
			{
				// VSwap path: downsample wall/door texture directly from raw RGBA page to 8×8.
				if (tileOrPage < vswapPages.Length && vswapPages[tileOrPage] is byte[] litPage)
					PaintTileFromRgbaPage(_litImage, x, y, litPage, pageSqrt);
				ushort dimPageIdx = _mapAnalysis.AutomapTileHasDimVariant(i)
					? (ushort)(tileOrPage + 1) : tileOrPage;
				if (dimPageIdx < vswapPages.Length && vswapPages[dimPageIdx] is byte[] dimPage)
					PaintTileFromRgbaPage(_dimImage, x, y, dimPage, pageSqrt);
			}
		}

		// Composite starts fully black (all undiscovered); rebuilt by RebuildComposite() on fog update
		_compositeImage = Image.CreateEmpty(_mapPixelWidth, _mapPixelHeight, false, Image.Format.Rgba8);
		_compositeImage.Fill(Colors.Black);
		_compositeTexture = ImageTexture.CreateFromImage(_compositeImage);
		_compositeDirty = false;
	}


	/// <summary>
	/// Paints an 8x8 tile into the target image by nearest-neighbour downsampling a square
	/// RGBA8888 VSwap page. Transparent pixels are skipped.
	/// </summary>
	private static void PaintTileFromRgbaPage(Image target, int tileX, int tileY, byte[] rgba, int pageSqrt)
	{
		int destX = tileX * TilePixels, destY = tileY * TilePixels;
		float scale = (float)pageSqrt / TilePixels;
		for (int dy = 0; dy < TilePixels; dy++)
			for (int dx = 0; dx < TilePixels; dx++)
			{
				int srcX = (int)((dx + 0.5f) * scale);
				int srcY = (int)((dy + 0.5f) * scale);
				int srcOffset = (srcY * pageSqrt + srcX) * 4;
				byte a = rgba[srcOffset + 3];
				if (a == 0) continue;
				target.SetPixel(destX + dx, destY + dy, new Color(
					rgba[srcOffset] / 255f,
					rgba[srcOffset + 1] / 255f,
					rgba[srcOffset + 2] / 255f,
					a / 255f));
			}
	}

	/// <summary>
	/// Paints an 8x8 tile from raw RGBA8888 byte data directly into the target image.
	/// Used for VgaGraph tiles (WL_MAP.C:VWB_DrawTile8) which are already 8x8 — no scaling needed.
	/// Transparent pixels (alpha == 0) are skipped, preserving the background.
	/// </summary>
	private static void PaintTileFromRgbaBytes(Image target, int tileX, int tileY, byte[] rgba8x8)
	{
		int destX = tileX * TilePixels;
		int destY = tileY * TilePixels;
		for (int dy = 0; dy < TilePixels; dy++)
			for (int dx = 0; dx < TilePixels; dx++)
			{
				int srcOffset = (dy * TilePixels + dx) * 4;
				byte a = rgba8x8[srcOffset + 3];
				if (a == 0)
					continue;
				target.SetPixel(destX + dx, destY + dy, new Color(
					rgba8x8[srcOffset] / 255f,
					rgba8x8[srcOffset + 1] / 255f,
					rgba8x8[srcOffset + 2] / 255f,
					a / 255f));
			}
	}

	private void RebuildComposite()
	{
		int tileCount = _automapData.Count;
		for (int i = 0; i < tileCount; i++)
		{
			int x = i % _mapWidth, y = i / _mapWidth;
			Rect2I tileRect = new(x * TilePixels, y * TilePixels, TilePixels, TilePixels);
			Vector2I tileDst = new(x * TilePixels, y * TilePixels);

			bool visible = i < _fogCurrentlyVisible.Count && _fogCurrentlyVisible[i];
			bool seen = i < _fogEverSeen.Count && _fogEverSeen[i];

			if (visible)
				_compositeImage.BlitRect(_litImage, tileRect, tileDst);
			else if (seen)
				_compositeImage.BlitRect(_dimImage, tileRect, tileDst);
			else
				_compositeImage.FillRect(tileRect, Colors.Black);
		}

		// Paint active bonus sprites onto seen tiles.
		// WL_MAP.C:DrawMapPrizes — items with ShapeNum == -1 have been collected and are skipped.
		if (_statObjList is not null)
		{
			byte[][] vgaGraphTiles = SharedAssetManager.CurrentGame?.VgaGraph?.Tiles;
			byte[][] vswapPages = SharedAssetManager.CurrentGame?.VSwap?.Pages;
			int pageSqrt = SharedAssetManager.CurrentGame?.VSwap?.TileSqrt ?? 64;
			foreach (StatObj stat in _statObjList)
			{
				if (stat is null || stat.IsFree || stat.ShapeNum < 0)
					continue;
				int tileIdx = stat.TileY * _mapWidth + stat.TileX;
				bool seen = tileIdx < _fogEverSeen.Count && _fogEverSeen[tileIdx];
				if (!seen)
					continue;
				// Prefer VgaGraph 8x8 tile (WL_MAP.C:VWB_DrawTile8); fall back to raw VSwap page.
				if (stat.AutomapTile is short automapTileIdx
					&& vgaGraphTiles is not null
					&& automapTileIdx >= 0 && automapTileIdx < vgaGraphTiles.Length
					&& vgaGraphTiles[automapTileIdx] is byte[] tileData)
					PaintTileFromRgbaBytes(_compositeImage, stat.TileX, stat.TileY, tileData);
				else if (vswapPages != null)
				{
					ushort shapeNum = (ushort)stat.ShapeNum;
					if (shapeNum < vswapPages.Length && vswapPages[shapeNum] is byte[] spritePage)
						PaintTileFromRgbaPage(_compositeImage, stat.TileX, stat.TileY, spritePage, pageSqrt);
				}
			}
		}

		// Paint pushwalls — checked against fog so unseen tiles stay black.
		// _pushWallTextures stores VgaGraph tile indices for VgaGraph games, VSwap pages otherwise.
		if (_pushWallTextures.Count > 0)
		{
			byte[][] vgaGraphTiles = SharedAssetManager.CurrentGame?.VgaGraph?.Tiles;
			byte[][] vswapPages = SharedAssetManager.CurrentGame?.VSwap?.Pages;
			int pageSqrt = SharedAssetManager.CurrentGame?.VSwap?.TileSqrt ?? 64;
			foreach (((ushort pwX, ushort pwY), ushort tileOrPage) in _pushWallTextures)
			{
				int idx = pwY * _mapWidth + pwX;
				bool visible = idx < _fogCurrentlyVisible.Count && _fogCurrentlyVisible[idx];
				bool seen = idx < _fogEverSeen.Count && _fogEverSeen[idx];
				if (!seen) continue;
				Rect2I rect = new(pwX * TilePixels, pwY * TilePixels, TilePixels, TilePixels);
				if (_mapAnalysis.UsesVgaGraphWallTiles && vgaGraphTiles != null)
				{
					if (tileOrPage < vgaGraphTiles.Length && vgaGraphTiles[tileOrPage] is byte[] tileData)
						PaintTileFromRgbaBytes(_compositeImage, pwX, pwY, tileData);
					else
						_compositeImage.FillRect(rect, visible ? _floorColor : _ceilingColor);
				}
				else if (vswapPages != null)
				{
					ushort page = (!visible && _mapAnalysis.PushWallsHaveDimVariant)
						? (ushort)(tileOrPage + 1) : tileOrPage;
					if (page < vswapPages.Length && vswapPages[page] is byte[] pageData)
						PaintTileFromRgbaPage(_compositeImage, pwX, pwY, pageData, pageSqrt);
					else
						_compositeImage.FillRect(rect, visible ? _floorColor : _ceilingColor);
				}
				else
					_compositeImage.FillRect(rect, visible ? _floorColor : _ceilingColor);
			}
		}

		_compositeTexture.Update(_compositeImage);
		_compositeDirty = false;
	}

	// ---------------------------------------------------------------------------
	// Public update API — called by AutomapController
	// ---------------------------------------------------------------------------

	/// <summary>
	/// Sets the bonus item list used by RebuildComposite() to paint uncollected pickups.
	/// Call once after Init() with simulator.StatObjList.
	/// WL_MAP.C:DrawMapPrizes — only items with ShapeNum >= 0 are shown.
	/// </summary>
	public void UpdateBonuses(StatObj[] statObjList)
	{
		_statObjList = statObjList;
		_compositeDirty = true;
		QueueRedraw();
	}

	/// <summary>
	/// Marks the composite dirty so the next _Draw() rebuilds it without the collected bonus.
	/// Call when a bonus is picked up or a new one is spawned (e.g. enemy drop).
	/// </summary>
	public void OnBonusChanged()
	{
		_compositeDirty = true;
		QueueRedraw();
	}

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
		_pushWallTextures[(newX, newY)] = _mapAnalysis.UsesVgaGraphWallTiles
			? _mapAnalysis.VgaGraphTileForWallShape(shape)
			: shape;
		_compositeDirty = true;
		QueueRedraw();
	}

	/// <summary>
	/// Snapshots the current fog state from the simulator and marks the composite dirty.
	/// The composite is rebuilt lazily at the start of the next _Draw() call.
	/// </summary>
	public void UpdateFog(IReadOnlyList<bool> everSeen, IReadOnlyList<bool> currentlyVisible)
	{
		_fogEverSeen = everSeen;
		_fogCurrentlyVisible = currentlyVisible;
		_compositeDirty = true;
		QueueRedraw();
	}

	// ---------------------------------------------------------------------------
	// Rendering
	// ---------------------------------------------------------------------------

	public override void _Draw()
	{
		if (!_initialized || _compositeTexture is null)
			return;

		// Rebuild composite if fog state changed since last frame
		if (_compositeDirty)
			RebuildComposite();

		// Scroll viewport to keep player centred, clamped so we never go past map edge
		int viewX = Math.Clamp(
			_playerTileX * TilePixels - ViewWidth / 2,
			0, Math.Max(0, _mapPixelWidth - ViewWidth));
		int viewY = Math.Clamp(
			_playerTileY * TilePixels - ViewHeight / 2,
			0, Math.Max(0, _mapPixelHeight - ViewHeight));

		// Black background — visible only for maps smaller than 320×160
		DrawRect(new Rect2(0, 0, ViewWidth, ViewHeight), Colors.Black);

		// Composite map region — one draw call; encodes lit/dim/black per tile
		int drawW = Math.Min(ViewWidth, _mapPixelWidth - viewX);
		int drawH = Math.Min(ViewHeight, _mapPixelHeight - viewY);
		if (drawW > 0 && drawH > 0)
			DrawTextureRectRegion(
				_compositeTexture,
				new Rect2(0, 0, drawW, drawH),
				new Rect2(viewX, viewY, drawW, drawH));

		// Player tile marker
		int screenX = _playerTileX * TilePixels - viewX;
		int screenY = _playerTileY * TilePixels - viewY;
		Rect2 tileRect = new(screenX, screenY, TilePixels, TilePixels);
		DrawRect(tileRect, Colors.LimeGreen);

		// Direction indicator: line extending from tile centre in the player's facing direction.
		// Engine angle convention (ExtensionMethods.cs:ToWolf3DAngle): wolf3dAngle = 90 - godotDegrees
		// so wolf3dAngle 0 = West on screen — negate cos to flip X axis to match.
		Vector2 center = tileRect.GetCenter();
		float rad = _playerAngle * Mathf.Pi / 180f;
		Vector2 dir = new(-Mathf.Cos(rad), -Mathf.Sin(rad));
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
		_compositeTexture?.Dispose();
		_compositeTexture = null;
		_compositeImage?.Dispose();
		_compositeImage = null;
		_litImage?.Dispose();
		_litImage = null;
		_dimImage?.Dispose();
		_dimImage = null;
		_initialized = false;
	}
}
