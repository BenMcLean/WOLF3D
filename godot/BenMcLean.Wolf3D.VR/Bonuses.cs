using BenMcLean.Wolf3D.Assets;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using static BenMcLean.Wolf3D.Assets.MapAnalyzer;

namespace BenMcLean.Wolf3D.VR;

/// <summary>
/// Manages dynamic rendering of bonus/pickup objects in a Wolfenstein 3D map using MultiMesh.
/// Uses MultiMesh for efficient rendering of billboarded bonus sprites in VR.
/// Supports unlimited bonus spawning - dynamically creates additional MultiMeshes as needed.
/// Each sprite type starts with 256-instance capacity, growing by adding more MultiMeshes.
/// Only handles bonus items with game logic (not fixtures/scenery).
/// Sprites rotate each frame to face opposite of the player's Y-axis orientation (billboard effect).
/// This node contains all bonus multimeshes as children - just add it to the scene tree.
/// </summary>
public partial class Bonuses : Node3D
{
	/// <summary>
	/// Dictionary of MultiMeshInstance3D lists, indexed by sprite page number.
	/// Each sprite type can have multiple MultiMeshes for dynamic capacity growth.
	/// </summary>
	public Dictionary<ushort, List<MultiMeshInstance3D>> MeshInstances { get; private init; }

	/// <summary>
	/// Delegate that returns the camera's Y-axis rotation angle for billboard effect.
	/// </summary>
	private Func<float> _getCameraYRotation;

	private readonly IReadOnlyDictionary<ushort, StandardMaterial3D> _spriteMaterials;

	// Simulator reference for event subscription
	private Simulator.Simulator _simulator;

	/// <summary>
	/// Maps simulator StatObjList index to rendering location.
	/// </summary>
	private readonly Dictionary<int, BonusRenderData> _simulatorToRenderMap = [];

	/// <summary>
	/// Tracks the next available slot index per sprite page.
	/// Sequential allocation, no slot reuse (simulator handles slot reuse via ShapeNum = -1).
	/// </summary>
	private readonly Dictionary<ushort, int> _nextSlotIndex = [];

	/// <summary>
	/// Rendering location for a bonus item.
	/// </summary>
	private struct BonusRenderData
	{
		public ushort SpriteShape;   // Which sprite page number
		public int MultiMeshIndex;   // Which MultiMesh in the list for this sprite
		public int LocalIndex;       // Index within that MultiMesh
	}

	/// <summary>
	/// Initial and growth capacity for MultiMesh instances.
	/// Generous enough to handle most scenarios without needing additional MultiMeshes.
	/// </summary>
	private const int MultiMeshCapacity = 256;

	/// <summary>
	/// Creates bonus sprite geometry that grows dynamically as bonuses spawn.
	/// All slots start hidden (scaled to zero) until BonusSpawnedEvent fires.
	/// </summary>
	/// <param name="spriteMaterials">Dictionary of sprite materials from GodotResources.SpriteMaterials</param>
	/// <param name="mapAnalysis">Map analysis containing static bonus spawn data</param>
	/// <param name="getCameraYRotation">Delegate that returns camera's Y rotation in radians</param>
	public Bonuses(
		IReadOnlyDictionary<ushort, StandardMaterial3D> spriteMaterials,
		MapAnalysis mapAnalysis,
		Func<float> getCameraYRotation)
	{
		_spriteMaterials = spriteMaterials ?? throw new ArgumentNullException(nameof(spriteMaterials));
		_getCameraYRotation = getCameraYRotation ?? throw new ArgumentNullException(nameof(getCameraYRotation));

		// Initialize with empty lists - MultiMeshes will be created on-demand as bonuses spawn
		MeshInstances = [];

		// Static bonuses from map - materialize to list to avoid re-enumeration issues
		List<MapAnalysis.StaticSpawn> staticBonuses = [.. mapAnalysis
			.StaticSpawns
			.Where(s => s.StatType == StatType.bonus)];

		// Display all static bonuses immediately (no need for events)
		int globalStaticIndex = 0;
		foreach (MapAnalysis.StaticSpawn bonus in staticBonuses)
		{
			// Map using global index (negative to distinguish from dynamic simulator indices)
			// Each static bonus gets a unique negative index
			int staticIndex = -(globalStaticIndex + 1);
			globalStaticIndex++;

			// ShowBonus will create the MultiMesh if needed
			ShowBonusInternal(staticIndex, bonus.Shape, bonus.X, bonus.Y);
		}
	}

	/// <summary>
	/// Shows a dynamically spawned bonus (enemy drops).
	/// Called from SimulatorController when BonusSpawnedEvent fires.
	/// Static bonuses are displayed directly in the constructor - this is only for dynamic spawns.
	/// </summary>
	/// <param name="statObjIndex">Index in simulator's StatObjList</param>
	/// <param name="shape">VSwap sprite page number</param>
	/// <param name="tileX">Tile X coordinate</param>
	/// <param name="tileY">Tile Y coordinate (Wolf3D Y, becomes Godot Z)</param>
	public void ShowBonus(int statObjIndex, ushort shape, ushort tileX, ushort tileY)
	{
		ShowBonusInternal(statObjIndex, shape, tileX, tileY);
	}

	/// <summary>
	/// Internal implementation for showing a bonus.
	/// Handles dynamic MultiMesh creation and growth.
	/// </summary>
	private void ShowBonusInternal(int statObjIndex, ushort shape, ushort tileX, ushort tileY)
	{
		// Ensure we have a list for this sprite type
		if (!MeshInstances.TryGetValue(shape, out List<MultiMeshInstance3D> instances))
		{
			instances = [];
			MeshInstances[shape] = instances;
			_nextSlotIndex[shape] = 0;
		}

		// Get current allocation index
		int totalAllocated = _nextSlotIndex[shape];

		// Calculate which MultiMesh and local index to use
		int multiMeshIndex = totalAllocated / MultiMeshCapacity;
		int localIndex = totalAllocated % MultiMeshCapacity;

		// Create new MultiMesh if needed
		if (multiMeshIndex >= instances.Count)
		{
			MultiMeshInstance3D newInstance = CreateMultiMeshForPage(shape);
			instances.Add(newInstance);
			AddChild(newInstance);
		}

		// Increment for next allocation
		_nextSlotIndex[shape]++;

		// Map simulator index -> render location
		_simulatorToRenderMap[statObjIndex] = new BonusRenderData
		{
			SpriteShape = shape,
			MultiMeshIndex = multiMeshIndex,
			LocalIndex = localIndex
		};
		// Set transform to show it (position only, rotation updated in _Process)
		Vector3 position = new(
			x: tileX.ToMetersCentered(),
			y: Constants.HalfWallHeight,
			z: tileY.ToMetersCentered());
		Transform3D transform = Transform3D.Identity;
		transform.Origin = position;
		instances[multiMeshIndex].Multimesh.SetInstanceTransform(localIndex, transform);
	}

	/// <summary>
	/// Hides a bonus (player picked it up).
	/// Called from SimulatorController when BonusPickedUpEvent fires.
	/// </summary>
	/// <param name="statObjIndex">Index in simulator's StatObjList that was removed</param>
	public void HideBonus(int statObjIndex)
	{
		if (!_simulatorToRenderMap.TryGetValue(statObjIndex, out BonusRenderData renderData))
		{
			GD.PrintErr($"Warning: Tried to hide bonus at simulator index {statObjIndex} but no render mapping exists");
			return;
		}

		// Hide by scaling to zero
		Transform3D transform = Transform3D.Identity.Scaled(Vector3.Zero);
		MeshInstances[renderData.SpriteShape][renderData.MultiMeshIndex].Multimesh
			.SetInstanceTransform(renderData.LocalIndex, transform);

		// Remove mapping (slot stays allocated but hidden)
		_simulatorToRenderMap.Remove(statObjIndex);
	}

	/// <summary>
	/// Creates a MultiMeshInstance3D for bonuses using a specific sprite page.
	/// All instances start hidden (scaled to zero) until shown by simulator events.
	/// Uses fixed capacity - multiple MultiMeshes created if needed for dynamic growth.
	/// </summary>
	private MultiMeshInstance3D CreateMultiMeshForPage(ushort page)
	{
		// Get material directly by page number (will throw KeyNotFoundException if missing)
		StandardMaterial3D material = _spriteMaterials[page];

		// Create MultiMesh with fixed capacity
		MultiMesh multiMesh = new()
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
			Mesh = Constants.WallMesh,
			InstanceCount = MultiMeshCapacity,
		};

		// Initialize all instances as hidden (scaled to zero)
		// They'll be shown when bonuses spawn
		Transform3D hiddenTransform = Transform3D.Identity.Scaled(Vector3.Zero);
		for (int i = 0; i < MultiMeshCapacity; i++)
			multiMesh.SetInstanceTransform(i, hiddenTransform);

		// Precompute custom AABB that encompasses all potential billboards
		// Use a generous AABB since bonuses can spawn anywhere on the map
		// This prevents incorrect frustum culling and avoids expensive RecomputeAabb() each frame
		multiMesh.CustomAabb = new Aabb(Vector3.Zero, new Vector3(1000, 100, 1000));

		// Count how many MultiMeshes exist for this page to create unique names
		int meshCount = MeshInstances.TryGetValue(page, out List<MultiMeshInstance3D> existing)
			? existing.Count
			: 0;

		// Create MultiMeshInstance3D
		MultiMeshInstance3D meshInstance = new()
		{
			Multimesh = multiMesh,
			MaterialOverride = material,
			Name = $"Bonuses_Page_{page}_{meshCount}",
		};

		return meshInstance;
	}

	/// <summary>
	/// Updates all billboard rotations to face the camera.
	/// Call this every frame from _Process().
	/// </summary>
	public override void _Process(double delta)
	{
		float billboardRotation = _getCameraYRotation();

		// Update rotation for all visible bonuses
		foreach (KeyValuePair<int, BonusRenderData> kvp in _simulatorToRenderMap)
		{
			BonusRenderData renderData = kvp.Value;

			if (!MeshInstances.TryGetValue(renderData.SpriteShape, out List<MultiMeshInstance3D> instances))
			{
				GD.PrintErr($"ERROR in _Process: Shape {renderData.SpriteShape} not in MeshInstances!");
				continue;
			}

			if (renderData.MultiMeshIndex >= instances.Count)
			{
				GD.PrintErr($"ERROR in _Process: MultiMeshIndex {renderData.MultiMeshIndex} out of range for shape {renderData.SpriteShape} (count: {instances.Count})");
				continue;
			}

			MultiMeshInstance3D meshInstance = instances[renderData.MultiMeshIndex];
			Transform3D transform = meshInstance.Multimesh.GetInstanceTransform(renderData.LocalIndex);

			// Keep position, update only rotation
			Vector3 position = transform.Origin;
			transform = Transform3D.Identity.Rotated(Vector3.Up, billboardRotation);
			transform.Origin = position;
			meshInstance.Multimesh.SetInstanceTransform(renderData.LocalIndex, transform);
		}
	}

	/// <summary>
	/// Sets the delegate for retrieving camera Y rotation.
	/// Useful if camera changes during gameplay.
	/// </summary>
	public void SetCameraRotationDelegate(Func<float> getCameraYRotation)
	{
		_getCameraYRotation = getCameraYRotation ?? throw new ArgumentNullException(nameof(getCameraYRotation));
	}

	/// <summary>
	/// Determines what sprite page an enemy drops when killed.
	/// TODO: Look up from XML what this enemy type drops.
	/// </summary>
	private static ushort GetEnemyDropShape(ObClass enemyType)
	{
		// TODO: Parse from WOLF3D.xml Actor/PlaceItem definitions
		// For now, return 0 (no drop) since enemy system isn't implemented yet
		return 0;
	}

	/// <summary>
	/// Subscribes to simulator events to automatically show/hide bonus objects.
	/// Call this after both Bonuses and Simulator are initialized.
	/// </summary>
	/// <param name="sim">The simulator instance to subscribe to</param>
	public void Subscribe(Simulator.Simulator sim)
	{
		ArgumentNullException.ThrowIfNull(sim);

		// Unsubscribe from previous simulator if any
		Unsubscribe();

		_simulator = sim;

		// Subscribe to bonus-related events
		_simulator.BonusSpawned += OnBonusSpawned;
		_simulator.BonusPickedUp += OnBonusPickedUp;
	}

	/// <summary>
	/// Unsubscribes from simulator events.
	/// Automatically called when subscribing to a new simulator or when this node is freed.
	/// </summary>
	public void Unsubscribe()
	{
		if (_simulator == null)
			return;

		_simulator.BonusSpawned -= OnBonusSpawned;
		_simulator.BonusPickedUp -= OnBonusPickedUp;

		_simulator = null;
	}

	/// <summary>
	/// Handles BonusSpawnedEvent - shows a dynamically spawned bonus.
	/// </summary>
	private void OnBonusSpawned(BenMcLean.Wolf3D.Simulator.BonusSpawnedEvent evt)
	{
		ShowBonus(evt.StatObjIndex, evt.Shape, evt.TileX, evt.TileY);

		// TODO: Play bonus spawn sound if applicable
		// PlaySoundLocTile(spawnSound, evt.TileX, evt.TileY);
	}

	/// <summary>
	/// Handles BonusPickedUpEvent - hides a picked-up bonus.
	/// </summary>
	private void OnBonusPickedUp(BenMcLean.Wolf3D.Simulator.BonusPickedUpEvent evt)
	{
		HideBonus(evt.StatObjIndex);

		// TODO: Play pickup sound based on item type (WL_AGENT.C:GetBonus)
		// PlaySoundLocTile(pickupSound, evt.TileX, evt.TileY);

		// TODO: Show bonus flash effect
		// StartBonusFlash();
	}

	/// <summary>
	/// Cleanup when the node is removed from the scene tree.
	/// </summary>
	protected override void Dispose(bool disposing)
	{
		if (disposing)
			Unsubscribe();

		base.Dispose(disposing);
	}
}
