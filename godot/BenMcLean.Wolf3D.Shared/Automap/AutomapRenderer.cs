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
/// Wall tiles are nearest-neighbour downsampled from VSwap pages —
/// works for any game, not just those with dedicated automap graphics.
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

	// Pushwall tiles — drawn dynamically in _Draw() so moves update without rebaking
	// Key: current tile position; Value: VSwap page number
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

		// PushWalls are NOT baked into the static background — they move, so they're drawn
		// dynamically in _Draw().  MapAnalyzer.cs already excludes them from AutomapData
		// (replaces with FloorCodeFirst before scanning), so the background correctly shows floor.
		foreach (MapAnalyzer.MapAnalysis.PushWallSpawn pushWall in mapAnalysis.PushWalls)
			_pushWallTextures[(pushWall.X, pushWall.Y)] = pushWall.Shape;

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
		Image atlasImage = SharedAssetManager.AtlasImage;

		// Lit image: floor color background, light-side (NS/even) wall pages
		_litImage = Image.CreateEmpty(_mapPixelWidth, _mapPixelHeight, false, Image.Format.Rgba8);
		_litImage.Fill(_floorColor);

		// Dim image: ceiling color background, dark-side (EW/odd) wall pages where available
		_dimImage = Image.CreateEmpty(_mapPixelWidth, _mapPixelHeight, false, Image.Format.Rgba8);
		_dimImage.Fill(_ceilingColor);

		// WL_MAP.C:DrawMapWalls — iterate flat array, skip floor/empty sentinel
		for (int i = 0; i < _automapData.Count; i++)
		{
			ushort litPage = _automapData[i];
			if (litPage == ushort.MaxValue)
				continue; // floor — already filled above
			int x = i % mapWidth, y = i / mapWidth;
			if (SharedAssetManager.VSwap.TryGetValue(litPage, out AtlasTexture litAtlas))
				PaintTile(_litImage, x, y, litAtlas, atlasImage);
			ushort dimPage = _mapAnalysis.AutomapTileHasDimVariant(i)
				? (ushort)(litPage + 1) : litPage;
			if (SharedAssetManager.VSwap.TryGetValue(dimPage, out AtlasTexture dimAtlas))
				PaintTile(_dimImage, x, y, dimAtlas, atlasImage);
		}

		// Composite starts fully black (all undiscovered); rebuilt by RebuildComposite() on fog update
		_compositeImage = Image.CreateEmpty(_mapPixelWidth, _mapPixelHeight, false, Image.Format.Rgba8);
		_compositeImage.Fill(Colors.Black);
		_compositeTexture = ImageTexture.CreateFromImage(_compositeImage);
		_compositeDirty = false;
	}

	private static void PaintTile(
		Image target,
		int tileX,
		int tileY,
		AtlasTexture atlasTexture,
		Image atlasImage)
	{
		Rect2 region = atlasTexture.Region;
		int destX = tileX * TilePixels,
			  destY = tileY * TilePixels;
		float scaleX = region.Size.X / TilePixels,
			  scaleY = region.Size.Y / TilePixels;
		for (int dy = 0; dy < TilePixels; dy++)
		{
			for (int dx = 0; dx < TilePixels; dx++)
			{
				// Sample the centre of each source block, not the top-left corner
				int srcX = (int)((dx + 0.5f) * scaleX) + (int)region.Position.X;
				int srcY = (int)((dy + 0.5f) * scaleY) + (int)region.Position.Y;
				Color src = atlasImage.GetPixel(srcX, srcY);
				if (src.A > 0f)
					target.SetPixel(destX + dx, destY + dy, src);
				// else: transparent pixel — leave background colour showing through
			}
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
		_compositeTexture.Update(_compositeImage);
		_compositeDirty = false;
	}

	// ---------------------------------------------------------------------------
	// Public update API — called by AutomapController
	// ---------------------------------------------------------------------------

	/// <summary>
	/// Sets the bonus item list read each frame by _Draw() to render uncollected pickups.
	/// Call once after Init() with simulator.StatObjList — the renderer reads it live,
	/// so collected items (ShapeNum == -1) automatically stop appearing without further calls.
	/// WL_MAP.C:DrawMapPrizes — only items with ShapeNum >= 0 are shown.
	/// </summary>
	public void UpdateBonuses(StatObj[] statObjList)
	{
		_statObjList = statObjList;
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
		_pushWallTextures[(newX, newY)] = shape;
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

		// Pushwall overlay — drawn on top with the same lit/dim logic as baked wall tiles.
		// The composite image already shows the correct background color (floor for lit,
		// ceiling for dim) because pushwall tiles are floor-sentinel in AutomapData.
		foreach (((ushort pwX, ushort pwY), ushort pwShape) in _pushWallTextures)
		{
			int tileIdx = pwY * _mapWidth + pwX;
			bool visible = tileIdx < _fogCurrentlyVisible.Count && _fogCurrentlyVisible[tileIdx];
			bool seen    = tileIdx < _fogEverSeen.Count        && _fogEverSeen[tileIdx];
			if (!seen) continue;

			int pwScreenX = pwX * TilePixels - viewX;
			int pwScreenY = pwY * TilePixels - viewY;
			if (pwScreenX + TilePixels <= 0 || pwScreenX >= ViewWidth
				|| pwScreenY + TilePixels <= 0 || pwScreenY >= ViewHeight)
				continue;

			// Use lit page when currently visible; dark-side page (Shape+1) when dim
			ushort page = (!visible && _mapAnalysis.PushWallsHaveDimVariant)
				? (ushort)(pwShape + 1) : pwShape;
			if (SharedAssetManager.VSwap.TryGetValue(page, out AtlasTexture pwAtlas))
				DrawTextureRect(
					pwAtlas,
					new Rect2(pwScreenX, pwScreenY, TilePixels, TilePixels),
					false);
			else
				DrawRect(new Rect2(pwScreenX, pwScreenY, TilePixels, TilePixels),
					visible ? _floorColor : _ceilingColor);
		}

		// Bonus overlay — drawn dynamically so collected items disappear automatically.
		// WL_MAP.C:DrawMapPrizes — only items with ShapeNum >= 0 are shown; ShapeNum == -1 = collected.
		// ShapeNum == -2 = invisible trigger (no sprite), also skipped.
		// Only draw bonuses on tiles the player has already seen (fog-of-war consistent with walls).
		if (_statObjList is not null)
		{
			foreach (StatObj stat in _statObjList)
			{
				if (stat is null || stat.IsFree || stat.ShapeNum < 0)
					continue;
				int tileIdx = stat.TileY * _mapWidth + stat.TileX;
				bool seen = tileIdx < _fogEverSeen.Count && _fogEverSeen[tileIdx];
				if (!seen)
					continue;
				int bScreenX = stat.TileX * TilePixels - viewX;
				int bScreenY = stat.TileY * TilePixels - viewY;
				if (bScreenX + TilePixels <= 0 || bScreenX >= ViewWidth
					|| bScreenY + TilePixels <= 0 || bScreenY >= ViewHeight)
					continue;
				if (SharedAssetManager.VSwap.TryGetValue((ushort)stat.ShapeNum, out AtlasTexture bonusAtlas))
					DrawTextureRect(bonusAtlas, new Rect2(bScreenX, bScreenY, TilePixels, TilePixels), false);
			}
		}

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
