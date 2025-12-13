using BenMcLean.Wolf3D.Assets;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using static BenMcLean.Wolf3D.Assets.MapAnalyzer;

namespace BenMcLean.Wolf3D.VR;

/// <summary>
/// Generates MultiMesh instances for all static fixture sprites (dressing and block objects) in a Wolfenstein 3D map.
/// Uses MultiMesh for efficient rendering of many billboarded sprite quads in VR.
/// Sprites rotate each frame to face opposite of the player's Y-axis orientation (billboard effect).
/// This node contains all fixture multimeshes as children - just add it to the scene tree.
/// </summary>
public partial class Fixtures : Node3D
{
	/// <summary>
	/// Dictionary of MultiMeshInstance3D nodes, indexed by sprite page number.
	/// </summary>
	public Dictionary<ushort, MultiMeshInstance3D> MeshInstances { get; private init; }

	/// <summary>
	/// Delegate that returns the camera's Y-axis rotation angle for billboard effect.
	/// </summary>
	private Func<float> _getCameraYRotation;

	private readonly IReadOnlyDictionary<ushort, StandardMaterial3D> _spriteMaterials;
	private readonly Dictionary<ushort, List<int>> _instancesByPage = [];

	/// <summary>
	/// Creates fixture sprite geometry from map data.
	/// </summary>
	/// <param name="spriteMaterials">Dictionary of sprite materials from GodotResources.SpriteMaterials</param>
	/// <param name="mapAnalysis">Map analysis containing static spawn data</param>
	/// <param name="getCameraYRotation">Delegate that returns camera's Y rotation in radians</param>
	/// <param name="spritePageOffset">VSwap.SpritePage offset (first sprite page number) - no longer used with Dictionary</param>
	public Fixtures(IReadOnlyDictionary<ushort, StandardMaterial3D> spriteMaterials, IEnumerable<MapAnalysis.StaticSpawn> staticSpawns, Func<float> getCameraYRotation, ushort spritePageOffset)
	{
		_spriteMaterials = spriteMaterials ?? throw new ArgumentNullException(nameof(spriteMaterials));
		_getCameraYRotation = getCameraYRotation ?? throw new ArgumentNullException(nameof(getCameraYRotation));

		// Filter for only dressing and block objects (exclude bonus/pickup items)
		List<MapAnalysis.StaticSpawn> fixtureSpawns = [.. staticSpawns.Where(s => s.StatType == StatType.dressing || s.StatType == StatType.block)];

		// Group fixtures by sprite page number
		Dictionary<ushort, MapAnalysis.StaticSpawn[]> fixturesByPage = fixtureSpawns
			.GroupBy(s => s.Shape)
			.ToDictionary(g => g.Key, g => g.ToArray());

		// Create MultiMesh for each unique sprite page
		MeshInstances = fixturesByPage.Keys.ToDictionary(
			page => page,
			page => CreateMultiMeshForPage(page, fixturesByPage[page], spritePageOffset));

		// Track instance indices for rotation updates
		foreach (KeyValuePair<ushort, MapAnalysis.StaticSpawn[]> kvp in fixturesByPage)
			_instancesByPage[kvp.Key] = [.. Enumerable.Range(0, kvp.Value.Length)];

		// Add all multimeshes as children of this node
		foreach (MultiMeshInstance3D meshInstance in MeshInstances.Values)
			AddChild(meshInstance);
	}

	/// <summary>
	/// Creates a MultiMeshInstance3D for all fixtures using a specific sprite page.
	/// </summary>
	private MultiMeshInstance3D CreateMultiMeshForPage(
		ushort page,
		MapAnalysis.StaticSpawn[] fixtures,
		ushort spritePageOffset)
	{
		// Get material directly by page number (will throw KeyNotFoundException if missing)
		StandardMaterial3D material = _spriteMaterials[page];

		// Create MultiMesh
		MultiMesh multiMesh = new()
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
			Mesh = Constants.WallMesh,  // Reuse wall quad mesh for sprites
			InstanceCount = fixtures.Length,
		};

		// Set initial transforms for all fixtures
		// They will be rotated each frame in _Process()
		for (int i = 0; i < fixtures.Length; i++)
		{
			MapAnalysis.StaticSpawn fixture = fixtures[i];
			Vector3 position = new(
				Constants.CenterSquare(fixture.X),
				Constants.HalfWallHeight,
				Constants.CenterSquare(fixture.Y)
			);

			// Initial rotation (will be updated each frame)
			Transform3D transform = Transform3D.Identity;
			transform.Origin = position;
			multiMesh.SetInstanceTransform(i, transform);
		}

		// Precompute custom AABB that encompasses all billboards at all Y-axis rotations
		// This prevents incorrect frustum culling and avoids expensive RecomputeAabb() each frame
		multiMesh.CustomAabb = ComputeBillboardAabb(fixtures);

		// Create MultiMeshInstance3D
		MultiMeshInstance3D meshInstance = new()
		{
			Multimesh = multiMesh,
			MaterialOverride = material,
			Name = $"Fixtures_Page_{page}",
		};

		return meshInstance;
	}

	/// <summary>
	/// Computes an AABB that fully encloses all billboard fixtures across all Y-axis rotations.
	/// When rotating around Y-axis, a billboard extends ±HalfWallWidth in XZ, ±HalfWallHeight in Y.
	/// </summary>
	private static Aabb ComputeBillboardAabb(params MapAnalysis.StaticSpawn[] fixtures)
	{
		if (fixtures.Length == 0)
			return new(Vector3.Zero, Vector3.Zero);
		ushort minX = fixtures.Min(fixture => fixture.X),
			maxX = fixtures.Max(fixture => fixture.X),
			minZ = fixtures.Min(fixture => fixture.Y),
			maxZ = fixtures.Max(fixture => fixture.Y);
		return new Aabb(
			position: new(Constants.FloatCoordinate(minX), 0f, Constants.FloatCoordinate(minZ)),
			size: new(Constants.FloatCoordinate(maxX - minX + 1), Constants.WallHeight, Constants.FloatCoordinate(maxZ - minZ + 1)));
	}

	/// <summary>
	/// Updates all billboard rotations to face the camera.
	/// Call this every frame from _Process().
	/// </summary>
	public override void _Process(double delta)
	{
		float billboardRotation = _getCameraYRotation();

		foreach (KeyValuePair<ushort, MultiMeshInstance3D> kvp in MeshInstances)
		{
			ushort pageNumber = kvp.Key;
			MultiMeshInstance3D meshInstance = kvp.Value;

			if (!_instancesByPage.TryGetValue(pageNumber, out List<int> instances))
				continue;

			// Update rotation for each instance (position stays the same)
			for (int i = 0; i < instances.Count; i++)
			{
				Transform3D transform = meshInstance.Multimesh.GetInstanceTransform(i);
				// Keep position, update only rotation
				Vector3 position = transform.Origin;
				transform = Transform3D.Identity.Rotated(Vector3.Up, billboardRotation);
				transform.Origin = position;
				meshInstance.Multimesh.SetInstanceTransform(i, transform);
			}
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
}
