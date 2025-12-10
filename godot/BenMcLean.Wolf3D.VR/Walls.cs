using Godot;
using System.Collections.Generic;
using System.Linq;
using static BenMcLean.Wolf3D.Assets.MapAnalyzer;

namespace BenMcLean.Wolf3D.VR;

/// <summary>
/// Generates MultiMesh instances for all walls in a Wolfenstein 3D map.
/// Uses MultiMesh for efficient rendering of many wall quads in VR.
/// </summary>
public class Walls
{
	/// <summary>
	/// Array of MultiMeshInstance3D nodes, one per unique wall texture.
	/// </summary>
	public MultiMeshInstance3D[] MeshInstances { get; private init; }

	/// <summary>
	/// Creates wall geometry from map data.
	/// </summary>
	/// <param name="wallMaterials">Array of wall materials from GodotResources</param>
	/// <param name="wallSpawns">Array of wall spawn data from MapAnalyzer</param>
	public Walls(StandardMaterial3D[] wallMaterials, IEnumerable<MapAnalysis.WallSpawn> wallSpawns)
	{
		// Group walls by texture (Shape = VSwap page number)
		Dictionary<ushort, List<MapAnalysis.WallSpawn>> wallsByTexture = wallSpawns
			.GroupBy(w => w.Shape)
			.ToDictionary(g => g.Key, g => g.ToList());

		// Create MultiMesh for each unique texture
		MeshInstances = [.. wallsByTexture.Keys
			.Select(shape => CreateMultiMeshForTexture(
				shape,
				wallMaterials[shape],
				wallsByTexture[shape]))];

		GD.Print($"Walls: Created {MeshInstances.Length} MultiMesh instances for {wallSpawns.Count()} total walls");
	}

	/// <summary>
	/// Creates a MultiMeshInstance3D for all walls using a specific texture.
	/// </summary>
	private static MultiMeshInstance3D CreateMultiMeshForTexture(
		ushort shape,
		StandardMaterial3D material,
		List<MapAnalysis.WallSpawn> walls)
	{
		// Debug: Check material validity
		if (material == null)
			GD.PrintErr($"ERROR: Material is null for shape {shape}");
		else if (material.AlbedoTexture == null)
			GD.PrintErr($"WARNING: Material for shape {shape} has null AlbedoTexture");

		// Create MultiMesh
		MultiMesh multiMesh = new()
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
			Mesh = Constants.WallMesh,
			InstanceCount = walls.Count,
		};

		// Set transforms for each wall instance
		for (int i = 0; i < walls.Count; i++)
		{
			Transform3D transform = CalculateWallTransform(walls[i]);
			multiMesh.SetInstanceTransform(i, transform);
		}

		// Create MultiMeshInstance3D
		MultiMeshInstance3D meshInstance = new MultiMeshInstance3D
		{
			Multimesh = multiMesh,
			MaterialOverride = material,
			Name = $"Walls_Texture_{shape}",
		};

		return meshInstance;
	}

	/// <summary>
	/// Calculates the 3D transform for a wall based on its spawn data.
	/// WallSpawn coordinates represent the wall block's grid position.
	/// Flip indicates which face: false=west/north, true=east/south
	/// </summary>
	private static Transform3D CalculateWallTransform(MapAnalysis.WallSpawn wall)
	{
		Vector3 position;
		float rotationY;

		if (wall.Western) // North/South wall (runs along X axis, perpendicular to Z)
		{
			// Wall block at (X, Z) - show south face (Flip=false) or north face (Flip=true)
			position = new Vector3(
				Constants.CenterSquare(wall.X),
				Constants.HalfWallHeight,
				wall.Flip ? Constants.FloatCoordinate(wall.Z) : Constants.FloatCoordinate(wall.Z) + Constants.WallWidth
			);
			// South face looks south (0°), North face looks north (180°)
			rotationY = wall.Flip ? Mathf.Pi : 0f;
		}
		else // East/West wall (runs along Z axis, perpendicular to X)
		{
			// Wall block at (X, Z) - show west face (Flip=false) or east face (Flip=true)
			position = new Vector3(
				wall.Flip ? Constants.FloatCoordinate(wall.X) + Constants.WallWidth : Constants.FloatCoordinate(wall.X),
				Constants.HalfWallHeight,
				Constants.CenterSquare(wall.Z)
			);
			// West face looks west (-90°), East face looks east (90°)
			rotationY = wall.Flip ? Constants.HalfPi : -Constants.HalfPi;
		}

		Transform3D transform = Transform3D.Identity.Rotated(Vector3.Up, rotationY);
		transform.Origin = position;

		return transform;
	}
}
