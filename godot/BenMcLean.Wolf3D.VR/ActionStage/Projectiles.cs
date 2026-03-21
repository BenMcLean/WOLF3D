using System;
using System.Collections.Generic;
using Godot;

namespace BenMcLean.Wolf3D.VR.ActionStage;

/// <summary>
/// Manages dynamic rendering of projectile sprites in a Wolfenstein 3D map.
/// Each projectile is rendered as an individual MeshInstance3D node (NOT MultiMesh).
/// Projectiles change sprites on state transitions (flight → explosion animation),
/// so individual nodes allow efficient material swaps.
/// Sprites rotate each frame to face opposite of the player's Y-axis orientation (billboard effect).
/// This node contains all projectile nodes as children — just add it to the scene tree.
/// </summary>
public partial class Projectiles : Node3D
{
	// Maps ProjectileId -> MeshInstance3D node
	private readonly Dictionary<long, MeshInstance3D> _projectileNodes = [];
	// Maps ProjectileId -> projectile rendering state
	private readonly Dictionary<long, ProjectileRenderData> _projectileData = [];
	// Sprite materials from VRAssetManager
	private readonly IReadOnlyDictionary<ushort, StandardMaterial3D> _spriteMaterials;
	// Viewer position for directional sprite calculation (normally player, could be MR camera)
	private readonly Func<Vector3> _getViewerPosition;
	// Camera Y rotation delegate for billboard effect
	private readonly Func<float> _getCameraYRotation;
	// Simulator reference for event subscription
	private Simulator.Simulator _simulator;

	/// <summary>
	/// Projectile rendering state (position, sprite info, Wolf3D travel angle).
	/// </summary>
	private struct ProjectileRenderData
	{
		public Vector3 Position;  // World position (Godot coordinates)
		public short Angle;       // WL_DEF.H:objstruct:angle — Wolf3D 0-359 (0=East, 90=North)
		public ushort BaseShape;  // Base sprite page
		public bool IsRotated;    // True = 8-directional sprite group, False = single sprite
	}

	/// <summary>
	/// Creates projectile sprite rendering system.
	/// Projectiles are spawned dynamically via ProjectileSpawnedEvent — no initial projectiles shown.
	/// </summary>
	/// <param name="spriteMaterials">Dictionary of sprite materials from VRAssetManager.SpriteMaterials</param>
	/// <param name="getViewerPosition">Delegate that returns viewer position for directional sprites</param>
	/// <param name="getCameraYRotation">Delegate that returns camera's Y rotation in radians</param>
	public Projectiles(
		IReadOnlyDictionary<ushort, StandardMaterial3D> spriteMaterials,
		Func<Vector3> getViewerPosition,
		Func<float> getCameraYRotation)
	{
		_spriteMaterials = spriteMaterials ?? throw new ArgumentNullException(nameof(spriteMaterials));
		_getViewerPosition = getViewerPosition ?? throw new ArgumentNullException(nameof(getViewerPosition));
		_getCameraYRotation = getCameraYRotation ?? throw new ArgumentNullException(nameof(getCameraYRotation));
	}

	/// <summary>
	/// Shows a newly spawned projectile.
	/// Called from event handler when ProjectileSpawnedEvent fires.
	/// </summary>
	private void ShowProjectile(long projectileId, ushort shape, bool isRotated, int fixedX, int fixedY, short angle)
	{
		// If projectile already has a node (e.g., re-emitting after LoadState), remove old one
		if (_projectileNodes.TryGetValue(projectileId, out MeshInstance3D existingNode))
		{
			RemoveChild(existingNode);
			existingNode.QueueFree();
			_projectileNodes.Remove(projectileId);
			_projectileData.Remove(projectileId);
		}
		// Projectiles fly at mid-wall height (WL_FPROJ.C: T_Projectile at wall-center Z)
		// Wolf3D X → Godot X, Wolf3D Y → Godot Z
		Vector3 position = new(
			fixedX.ToMeters(),
			Constants.HalfTileHeight,
			fixedY.ToMeters()
		);
		// Create MeshInstance3D for this projectile
		MeshInstance3D node = new()
		{
			Mesh = Constants.WallMesh,  // Shared quad mesh
			Name = $"Projectile_{projectileId}",
			Position = position,
			Rotation = new Vector3(0, _getCameraYRotation(), 0)  // Initial billboard rotation
		};
		// Set material based on sprite type
		if (isRotated)
		{
			// 8-directional sprite: pick frame based on viewer angle relative to travel direction
			ushort directionalSprite = CalculateDirectionalSprite(position, shape, _getViewerPosition(), angle);
			if (!_spriteMaterials.TryGetValue(directionalSprite, out StandardMaterial3D material))
			{
				GD.PrintErr($"ERROR: Sprite material {directionalSprite} not found!");
				return;
			}
			node.MaterialOverride = material;
		}
		else
		{
			// Single sprite for all viewing angles
			if (!_spriteMaterials.TryGetValue(shape, out StandardMaterial3D material))
			{
				GD.PrintErr($"ERROR: Sprite material {shape} not found!");
				return;
			}
			node.MaterialOverride = material;
		}
		// Add to scene and track
		AddChild(node);
		_projectileNodes[projectileId] = node;
		_projectileData[projectileId] = new ProjectileRenderData
		{
			Position = position,
			Angle = angle,
			BaseShape = shape,
			IsRotated = isRotated,
		};
	}

	/// <summary>
	/// Moves a projectile to a new position.
	/// Called from event handler when ProjectileMovedEvent fires.
	/// WL_FPROJ.C:T_Projectile movement per tic.
	/// </summary>
	private void MoveProjectile(long projectileId, int fixedX, int fixedY)
	{
		if (!_projectileNodes.TryGetValue(projectileId, out MeshInstance3D node))
			return;
		// Convert 16.16 fixed-point to Godot world coordinates
		// Wolf3D X → Godot X, Wolf3D Y → Godot Z
		Vector3 newPosition = new(
			x: fixedX.ToMeters(),
			y: Constants.HalfTileHeight,
			z: fixedY.ToMeters());
		node.Position = newPosition;
		// Update stored position for directional sprite calculation
		if (_projectileData.TryGetValue(projectileId, out ProjectileRenderData data))
		{
			data.Position = newPosition;
			_projectileData[projectileId] = data;
		}
	}

	/// <summary>
	/// Changes a projectile's sprite (explosion animation frame, state transition).
	/// Called from event handler when ProjectileSpriteChangedEvent fires.
	/// For explosion states IsRotated will be false.
	/// </summary>
	private void ChangeProjectileSprite(long projectileId, ushort newShape, bool isRotated)
	{
		if (!_projectileNodes.TryGetValue(projectileId, out MeshInstance3D node))
			return;
		if (!_projectileData.TryGetValue(projectileId, out ProjectileRenderData data))
			return;
		// Update projectile data with new sprite info
		data.BaseShape = newShape;
		data.IsRotated = isRotated;
		_projectileData[projectileId] = data;
		// Update material
		if (!isRotated)
		{
			// Non-rotated: set the material directly (explosion frames, single-sprite projectiles)
			if (_spriteMaterials.TryGetValue(newShape, out StandardMaterial3D material))
				node.MaterialOverride = material;
		}
		else
		{
			// Rotated: calculate directional sprite (will also be updated in _Process)
			ushort directionalSprite = CalculateDirectionalSprite(
				data.Position, newShape, _getViewerPosition(), data.Angle);
			if (_spriteMaterials.TryGetValue(directionalSprite, out StandardMaterial3D material))
				node.MaterialOverride = material;
		}
	}

	/// <summary>
	/// Removes a projectile from the scene (wall hit, target hit, or explosion finished).
	/// Called from event handler when ProjectileDespawnedEvent fires.
	/// WL_FPROJ.C:T_Projectile: ob->state = NULL removes from object list.
	/// </summary>
	private void HideProjectile(long projectileId)
	{
		if (_projectileNodes.TryGetValue(projectileId, out MeshInstance3D node))
		{
			node.QueueFree();
			_projectileNodes.Remove(projectileId);
		}
		_projectileData.Remove(projectileId);
	}

	/// <summary>
	/// Calculates which of 8 directional sprites to show based on viewer angle relative to travel direction.
	/// Only used for rotated sprites (e.g., rockets that visually rotate as they travel).
	/// Sprite offset (0-7) is added to baseShape; same convention as actor sprites.
	/// </summary>
	private static ushort CalculateDirectionalSprite(Vector3 projectilePosition, ushort baseShape, Vector3 viewerPosition, short wolf3dAngle)
	{
		// Calculate viewing angle from viewer to projectile
		Vector3 toProjectile = projectilePosition - viewerPosition;
		float viewAngle = Mathf.Atan2(toProjectile.Z, toProjectile.X);
		// Convert to [0, 2π) range
		if (viewAngle < 0) viewAngle += Mathf.Tau;
		// Convert Wolf3D travel angle to Godot Y rotation radians
		// ToGodotYRotation: wolf3dAngle=0(East)→Godot π/2, wolf3dAngle=90(North)→Godot 0, etc.
		float projectileAngle = wolf3dAngle.ToGodotYRotation();
		// Relative angle (viewer's perspective looking at projectile)
		// Add π to flip from "projectile to viewer" to "viewer to projectile"
		// Swap order to reverse clockwise/counter-clockwise direction (matches actor convention)
		float relativeAngle = projectileAngle - viewAngle + Mathf.Pi;
		// Normalize to [0, 2π)
		while (relativeAngle < 0) relativeAngle += Mathf.Tau;
		while (relativeAngle >= Mathf.Tau) relativeAngle -= Mathf.Tau;
		// Map to 8 directions (0-7); Direction 0 = viewing from front of travel
		int direction = (int)Mathf.Round(relativeAngle / (Mathf.Tau / 8)) % 8;
		return (ushort)(baseShape + direction);
	}

	/// <summary>
	/// Updates all billboard rotations and directional sprites.
	/// Called every frame from _Process().
	/// </summary>
	public override void _Process(double delta)
	{
		float billboardRotation = _getCameraYRotation();
		Vector3 viewerPosition = _getViewerPosition();
		foreach (KeyValuePair<long, MeshInstance3D> kvp in _projectileNodes)
		{
			long projectileId = kvp.Key;
			MeshInstance3D node = kvp.Value;
			// Always update billboard rotation
			node.Rotation = new Vector3(0, billboardRotation, 0);
			// Update directional sprite if this is a rotated sprite
			if (_projectileData.TryGetValue(projectileId, out ProjectileRenderData data) && data.IsRotated)
			{
				ushort directionalSprite = CalculateDirectionalSprite(
					data.Position, data.BaseShape, viewerPosition, data.Angle);
				node.MaterialOverride = _spriteMaterials[directionalSprite];
			}
		}
	}

	/// <summary>
	/// Subscribes to simulator events to automatically show/hide/update projectiles.
	/// Call this after both Projectiles and Simulator are initialized.
	/// </summary>
	public void Subscribe(Simulator.Simulator sim)
	{
		ArgumentNullException.ThrowIfNull(sim);
		Unsubscribe();
		_simulator = sim;
		_simulator.ProjectileSpawned += OnProjectileSpawned;
		_simulator.ProjectileMoved += OnProjectileMoved;
		_simulator.ProjectileSpriteChanged += OnProjectileSpriteChanged;
		_simulator.ProjectileDespawned += OnProjectileDespawned;
	}

	/// <summary>
	/// Unsubscribes from simulator events.
	/// </summary>
	public void Unsubscribe()
	{
		if (_simulator is null)
			return;
		_simulator.ProjectileSpawned -= OnProjectileSpawned;
		_simulator.ProjectileMoved -= OnProjectileMoved;
		_simulator.ProjectileSpriteChanged -= OnProjectileSpriteChanged;
		_simulator.ProjectileDespawned -= OnProjectileDespawned;
		_simulator = null;
	}

	/// <summary>
	/// Handles ProjectileSpawnedEvent — shows a newly spawned projectile.
	/// </summary>
	private void OnProjectileSpawned(Simulator.ProjectileSpawnedEvent evt) =>
		ShowProjectile(evt.ProjectileId, evt.Shape, evt.IsRotated, evt.X, evt.Y, evt.Angle);

	/// <summary>
	/// Handles ProjectileMovedEvent — moves a projectile to its new position.
	/// </summary>
	private void OnProjectileMoved(Simulator.ProjectileMovedEvent evt) =>
		MoveProjectile(evt.ProjectileId, evt.X, evt.Y);

	/// <summary>
	/// Handles ProjectileSpriteChangedEvent — swaps the projectile's sprite.
	/// </summary>
	private void OnProjectileSpriteChanged(Simulator.ProjectileSpriteChangedEvent evt) =>
		ChangeProjectileSprite(evt.ProjectileId, evt.Shape, evt.IsRotated);

	/// <summary>
	/// Handles ProjectileDespawnedEvent — removes the projectile from the scene.
	/// </summary>
	private void OnProjectileDespawned(Simulator.ProjectileDespawnedEvent evt) =>
		HideProjectile(evt.ProjectileId);

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
