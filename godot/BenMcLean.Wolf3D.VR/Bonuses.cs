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
///
/// Shape number convention (WL_DEF.H:statstruct:shapenum):
/// -1 = despawned/removed (Wolf3D standard - no event emitted)
/// -2 = invisible trigger (Super 3-D Noah's Ark - used for stairs/Lua triggers, no visual)
/// >= 0 = visible bonus sprite (normal bonus items)
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
	/// Tracks tile positions of visible bonuses for AABB computation.
	/// Key: (spriteShape, multiMeshIndex), Value: list of (tileX, tileY, localIndex) tuples.
	/// </summary>
	private readonly Dictionary<(ushort, int), List<(ushort tileX, ushort tileY, int localIndex)>> _visibleBonusPositions = [];
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
	/// <param name="getCameraYRotation">Delegate that returns camera's Y rotation in radians</param>
	public Bonuses(
		IReadOnlyDictionary<ushort, StandardMaterial3D> spriteMaterials,
		Func<float> getCameraYRotation)
	{
		_spriteMaterials = spriteMaterials ?? throw new ArgumentNullException(nameof(spriteMaterials));
		_getCameraYRotation = getCameraYRotation ?? throw new ArgumentNullException(nameof(getCameraYRotation));

		// Initialize with empty lists - MultiMeshes will be created on-demand as bonuses spawn
		MeshInstances = [];

		// All bonuses (both static and dynamic) now spawn via BonusSpawnedEvent
		// Subscribe to Simulator events before calling Simulator.LoadBonusesFromMapAnalysis()
	}

	/// <summary>
	/// Shows a bonus (both static from map and dynamically spawned from enemies).
	/// Called from SimulatorController when BonusSpawnedEvent fires.
	/// All bonuses now spawn via events - this is the unified display path.
	/// </summary>
	/// <param name="statObjIndex">Index in simulator's StatObjList</param>
	/// <param name="shape">VSwap sprite page number (-2 = invisible trigger, >= 0 = visible)</param>
	/// <param name="tileX">Tile X coordinate</param>
	/// <param name="tileY">Tile Y coordinate (Wolf3D Y, becomes Godot Z)</param>
	public void ShowBonus(int statObjIndex, short shape, ushort tileX, ushort tileY)
	{
		// Shape -2 means invisible trigger (Noah's Ark stairs/triggers - no visual sprite)
		// Used in Super 3-D Noah's Ark for stairs and Lua-scripted triggers
		if (shape == -2)
			return;

		// Shape must be >= 0 for visible bonuses
		if (shape < 0)
		{
			GD.PrintErr($"ERROR: Invalid shape {shape} for bonus at ({tileX}, {tileY})");
			return;
		}

		ShowBonusInternal(statObjIndex, (ushort)shape, tileX, tileY);
	}

	/// <summary>
	/// Internal implementation for showing a bonus.
	/// Handles dynamic MultiMesh creation and growth.
	/// PRECONDITION: shape must be >= 0 (caller must check for invisible/invalid shapes).
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

		// Track position for AABB computation
		(ushort, int) meshKey = (shape, multiMeshIndex);
		if (!_visibleBonusPositions.TryGetValue(meshKey, out List<(ushort, ushort, int)> positions))
		{
			positions = [];
			_visibleBonusPositions[meshKey] = positions;
		}
		positions.Add((tileX, tileY, localIndex));

		// Set transform to show it (position only, rotation updated in _Process)
		Vector3 position = new(
			x: tileX.ToMetersCentered(),
			y: Constants.HalfTileHeight,
			z: tileY.ToMetersCentered());
		Transform3D transform = Transform3D.Identity;
		transform.Origin = position;
		instances[multiMeshIndex].Multimesh.SetInstanceTransform(localIndex, transform);

		// Recompute tight AABB for this MultiMesh based on visible bonuses
		instances[multiMeshIndex].Multimesh.CustomAabb = ComputeBillboardAabb(positions);
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
		MultiMeshInstance3D meshInstance = MeshInstances[renderData.SpriteShape][renderData.MultiMeshIndex];
		meshInstance.Multimesh.SetInstanceTransform(renderData.LocalIndex, transform);

		// Remove position from tracking
		(ushort, int) meshKey = (renderData.SpriteShape, renderData.MultiMeshIndex);
		if (_visibleBonusPositions.TryGetValue(meshKey, out List<(ushort, ushort, int)> positions))
		{
			// Remove the position entry with matching localIndex
			positions.RemoveAll(p => p.Item3 == renderData.LocalIndex);

			// Recompute tight AABB for this MultiMesh based on remaining visible bonuses
			meshInstance.Multimesh.CustomAabb = ComputeBillboardAabb(positions);
		}

		// Remove mapping (slot stays allocated but hidden)
		_simulatorToRenderMap.Remove(statObjIndex);
	}

	/// <summary>
	/// Computes an AABB that fully encloses all visible billboard bonuses across all Y-axis rotations.
	/// When rotating around Y-axis, a billboard extends ±HalfWallWidth in XZ, ±HalfWallHeight in Y.
	/// </summary>
	private static Aabb ComputeBillboardAabb(List<(ushort tileX, ushort tileY, int localIndex)> positions)
	{
		if (positions.Count == 0)
			return new Aabb(Vector3.Zero, Vector3.Zero);

		ushort minX = positions.Min(p => p.tileX);
		ushort maxX = positions.Max(p => p.tileX);
		ushort minZ = positions.Min(p => p.tileY);
		ushort maxZ = positions.Max(p => p.tileY);

		return new Aabb(
			position: new Vector3(
				x: minX.ToMeters(),
				y: 0f,
				z: minZ.ToMeters()),
			size: new Vector3(
				x: (maxX - minX + 1) * Constants.TileWidth,
				y: Constants.TileHeight,
				z: (maxZ - minZ + 1) * Constants.TileWidth));
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

		// Start with empty AABB - will be recomputed when first bonus spawns
		// This prevents incorrect frustum culling and avoids expensive RecomputeAabb() each frame
		multiMesh.CustomAabb = new Aabb(Vector3.Zero, Vector3.Zero);

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
	/// Shape -2 indicates an invisible trigger (no visual sprite) - used in Super 3-D Noah's Ark stairs.
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
