using System;
using System.Collections.Generic;
using BenMcLean.Wolf3D.Assets.Gameplay;
using BenMcLean.Wolf3D.Simulator;
using BenMcLean.Wolf3D.Simulator.Entities;
using Godot;

namespace BenMcLean.Wolf3D.Shared.Automap;

/// <summary>
/// Owns an AutomapRenderer and its SubViewport, and wires simulator pushwall events
/// so the automap overlay stays current as walls move.
/// Presentation layers (flatscreen HUD, VR wrist screen, etc.) create one of these,
/// call Init(), then position the viewport texture however they like.
/// WL_MAP.C:AutoMap — the entry point for the Wolf3D automap screen.
/// </summary>
public class AutomapController
{
	private readonly SubViewport _viewport;
	private readonly AutomapRenderer _renderer;
	private Simulator.Simulator _simulator;
	private Action<PushWallPositionChangedEvent> _onPushWallPositionChanged;
	// Last confirmed idle tile per pushwall index — avoids relying on Direction when Action==Idle
	private readonly Dictionary<ushort, (ushort tileX, ushort tileY)> _pushWallTiles = [];
	private int _lastFogVersion = -1;

	public AutomapController()
	{
		_viewport = new SubViewport
		{
			Size = new Vector2I(AutomapRenderer.ViewWidth, AutomapRenderer.ViewHeight),
			Disable3D = true,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
		};
		_renderer = new AutomapRenderer();
		_viewport.AddChild(_renderer);
	}

	/// <summary>
	/// The SubViewport containing the rendered automap.
	/// Add this to your scene tree so the renderer gets Godot process callbacks.
	/// </summary>
	public SubViewport Viewport => _viewport;

	/// <summary>
	/// Viewport texture for display — use as a TextureRect.Texture or on a 3D mesh.
	/// </summary>
	public ViewportTexture ViewportTexture => _viewport.GetTexture();

	/// <summary>
	/// Initialises the automap and subscribes to simulator pushwall events.
	/// Safe to call multiple times (e.g. on level change) — re-subscribes cleanly.
	/// </summary>
	public void Init(MapAnalyzer.MapAnalysis mapAnalysis, Simulator.Simulator simulator)
	{
		_renderer.Init(mapAnalysis);
		_renderer.UpdateBonuses(simulator.StatObjList);
		_lastFogVersion = -1;

		// Unsubscribe from any previous simulator before switching
		if (_simulator is not null && _onPushWallPositionChanged is not null)
			_simulator.PushWallPositionChanged -= _onPushWallPositionChanged;

		_simulator = simulator;

		_pushWallTiles.Clear();
		for (int i = 0; i < simulator.PushWalls.Count; i++)
		{
			PushWall pw = simulator.PushWalls[i];
			_pushWallTiles[(ushort)i] = (pw.InitialTileX, pw.InitialTileY);
		}

		_onPushWallPositionChanged = OnPushWallPositionChanged;
		_simulator.PushWallPositionChanged += _onPushWallPositionChanged;
	}

	/// <summary>
	/// Updates the player marker on the automap and advances fog of war if needed.
	/// Call each frame from the presentation layer's _Process.
	/// </summary>
	public void UpdatePlayer(ushort tileX, ushort tileY, short angle)
	{
		_simulator?.RecomputeVisibilityIfNeeded();
		if (_simulator is not null && _simulator.FogVersion != _lastFogVersion)
		{
			_renderer.UpdateFog(_simulator.EverSeen, _simulator.CurrentlyVisible);
			_lastFogVersion = _simulator.FogVersion;
		}
		_renderer.UpdatePlayer(tileX, tileY, angle);
	}

	/// <summary>
	/// Unsubscribes from simulator events. Call when the presentation layer exits the tree.
	/// Does not free the viewport — that is the scene tree's responsibility.
	/// </summary>
	public void Unsubscribe()
	{
		if (_simulator is not null && _onPushWallPositionChanged is not null)
			_simulator.PushWallPositionChanged -= _onPushWallPositionChanged;
	}

	private void OnPushWallPositionChanged(PushWallPositionChangedEvent evt)
	{
		if (evt.Action != PushWallAction.Idle)
			return;
		ushort newTileX = (ushort)(evt.X >> 16);
		ushort newTileY = (ushort)(evt.Y >> 16);
		if (!_pushWallTiles.TryGetValue(evt.PushWallIndex, out (ushort tileX, ushort tileY) old))
			return;
		if (old.tileX == newTileX && old.tileY == newTileY)
			return; // position unchanged, nothing to do
		_renderer.OnPushWallMoved(
			old.tileX, old.tileY, newTileX, newTileY,
			_simulator.PushWalls[evt.PushWallIndex].Shape);
		_pushWallTiles[evt.PushWallIndex] = (newTileX, newTileY);
	}
}
