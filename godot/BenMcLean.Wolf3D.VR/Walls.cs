using Godot;
using System.Collections.Generic;
using System.Linq;
using static BenMcLean.Wolf3D.Assets.MapAnalyzer;

namespace BenMcLean.Wolf3D.VR;

/// <summary>
/// Generates MultiMesh instances for all walls in a Wolfenstein 3D map.
/// Uses MultiMesh for efficient rendering of many wall quads in VR.
/// Supports pushwalls as 4-sided cubes that can move.
/// This node contains all wall multimeshes as children - just add it to the scene tree.
/// </summary>
public partial class Walls : Node3D
{
	/// <summary>
	/// Dictionary of MultiMeshInstance3D nodes, indexed by texture number.
	/// </summary>
	public Dictionary<ushort, MultiMeshInstance3D> MeshInstances { get; private init; }

	// Pushwall tracking
	private readonly List<PushWallData> pushWalls = [];
	private readonly StandardMaterial3D[] wallMaterials;
	private readonly Dictionary<ushort, int> nextInstanceIndex = []; // Tracks next available instance per texture

	private class PushWallData
	{
		public ushort ShapeTexture;      // Horizontal/lighter texture (north/south faces)
		// DarkSide texture is always ShapeTexture + 1 (vertical/darker, east/west faces)

		public int NorthInstanceIndex;   // In MeshInstances for ShapeTexture
		public int SouthInstanceIndex;   // In MeshInstances for ShapeTexture
		public int EastInstanceIndex;    // In MeshInstances for ShapeTexture + 1
		public int WestInstanceIndex;    // In MeshInstances for ShapeTexture + 1
	}

	/// <summary>
	/// Creates wall geometry from map data.
	/// </summary>
	/// <param name="wallMaterials">Array of wall materials from GodotResources</param>
	/// <param name="mapAnalysis">Map analysis containing wall and pushwall spawn data</param>
	public Walls(StandardMaterial3D[] wallMaterials, MapAnalysis mapAnalysis)
	{
		this.wallMaterials = wallMaterials;

		// Group walls by texture (Shape = VSwap page number)
		Dictionary<ushort, List<MapAnalysis.WallSpawn>> wallsByTexture = mapAnalysis.Walls
			.GroupBy(w => w.Shape)
			.ToDictionary(g => g.Key, g => g.ToList());

		// Calculate exact instance counts needed (walls + pushwall faces)
		Dictionary<ushort, int> instanceCounts = wallsByTexture.ToDictionary(
			kvp => kvp.Key,
			kvp => kvp.Value.Count);

		if (mapAnalysis.PushWalls is not null)
			foreach (MapAnalysis.PushWallSpawn pw in mapAnalysis.PushWalls)
			{
				// Each pushwall needs 2 instances in Shape texture (north + south)
				instanceCounts[pw.Shape] = instanceCounts.GetValueOrDefault(pw.Shape) + 2;
				// Each pushwall needs 2 instances in DarkSide texture (east + west)
				ushort darkSide = pw.DarkSide;
				instanceCounts[darkSide] = instanceCounts.GetValueOrDefault(darkSide) + 2;
			}

		// Track starting index for each texture (after regular walls)
		foreach (KeyValuePair<ushort, List<MapAnalysis.WallSpawn>> kvp in wallsByTexture)
			nextInstanceIndex[kvp.Key] = kvp.Value.Count;

		// Create MultiMesh for each unique texture with exact size
		MeshInstances = instanceCounts.Keys.ToDictionary(
			shape => shape,
			shape => CreateMultiMeshForTexture(
				shape,
				wallMaterials[shape],
				wallsByTexture.GetValueOrDefault(shape, []),
				instanceCounts[shape]));

		// Add all multimeshes as children of this node
		foreach (MultiMeshInstance3D meshInstance in MeshInstances.Values)
			AddChild(meshInstance);

		// Automatically spawn all pushwalls in order
		// The pushwall ID will match its index in the mapAnalysis.PushWalls collection
		if (mapAnalysis.PushWalls is not null)
		{
			int pwCount = 0;
			foreach (MapAnalysis.PushWallSpawn pw in mapAnalysis.PushWalls)
			{
				GD.Print($"Spawning pushwall {pwCount}: Shape={pw.Shape}, DarkSide={pw.DarkSide}, Pos=({pw.X}, {pw.Z})");
				ushort id = AddPushWall(pw.Shape, new Vector2(pw.X, pw.Z));
				GD.Print($"  -> Assigned ID {id}");
				pwCount++;
			}
		}

		int totalWalls = mapAnalysis.Walls.Count,
			totalPushwallFaces = mapAnalysis.PushWalls?.Count * 4 ?? 0;
		GD.Print($"Walls: Created {MeshInstances.Count} MultiMesh instances for {totalWalls} walls + {totalPushwallFaces} pushwall faces");
		GD.Print($"Available MultiMeshes: {string.Join(", ", MeshInstances.Keys.OrderBy(k => k).Select(k => $"[{k}]"))}");
	}

	/// <summary>
	/// Adds a pushwall to the rendering system.
	/// A pushwall is a 4-sided cube (north, south, east, west faces).
	/// NOTE: Usually called automatically by constructor. Pushwall IDs match their index in MapAnalysis.PushWalls.
	/// </summary>
	/// <param name="wallTexture">The wall texture number (horizontal page)</param>
	/// <param name="gridPosition">Grid position (X, Z) of the pushwall</param>
	/// <returns>Unique ID for this pushwall (used with MovePushWall)</returns>
	public ushort AddPushWall(ushort wallTexture, Vector2 gridPosition)
	{
		ushort shapeTexture = wallTexture;
		ushort darkSideTexture = (ushort)(wallTexture + 1);

		PushWallData data = new()
		{
			ShapeTexture = shapeTexture,
			NorthInstanceIndex = AllocateInstance(shapeTexture),
			SouthInstanceIndex = AllocateInstance(shapeTexture),
			EastInstanceIndex = AllocateInstance(darkSideTexture),
			WestInstanceIndex = AllocateInstance(darkSideTexture),
		};

		// Set initial transforms at grid position
		UpdatePushWallTransforms(data, gridPosition);

		ushort pushWallId = (ushort)pushWalls.Count;
		pushWalls.Add(data);

		return pushWallId;
	}

	/// <summary>
	/// Moves a pushwall to a new position. Call during gameplay as pushwall moves.
	/// </summary>
	/// <param name="pushWallId">The ID returned from AddPushWall</param>
	/// <param name="gridPosition">New position in grid coordinates (can be fractional for smooth movement)</param>
	public void MovePushWall(ushort pushWallId, Vector2 gridPosition)
	{
		if (pushWallId >= pushWalls.Count)
		{
			GD.PrintErr($"Invalid pushwall ID: {pushWallId}");
			return;
		}

		PushWallData data = pushWalls[pushWallId];
		UpdatePushWallTransforms(data, gridPosition);
	}

	/// <summary>
	/// Swaps the material for all walls using a specific texture.
	/// Useful for elevator switches or other texture-based state changes.
	/// </summary>
	/// <param name="oldTexture">The current texture number</param>
	/// <param name="newTexture">The new texture number to display</param>
	public void SwapTexture(ushort oldTexture, ushort newTexture)
	{
		if (MeshInstances.TryGetValue(oldTexture, out MultiMeshInstance3D meshInstance))
			meshInstance.MaterialOverride = wallMaterials[newTexture];
		else
			GD.PrintErr($"Cannot swap texture: No MultiMesh found for texture {oldTexture}");
	}

	/// <summary>
	/// Updates all 4 face transforms for a pushwall based on its position.
	/// </summary>
	private void UpdatePushWallTransforms(PushWallData data, Vector2 gridPosition)
	{
		Vector3 centerPosition = new(
			Constants.FloatCoordinate((int)gridPosition.X) + Constants.HalfWallWidth,
			Constants.HalfWallHeight,
			Constants.FloatCoordinate((int)gridPosition.Y) + Constants.HalfWallWidth
		);

		GD.Print($"Updating pushwall at grid ({gridPosition.X}, {gridPosition.Y}) -> world {centerPosition}");

		// North face (Shape texture, facing north at -Z edge)
		GD.Print($"  North face: texture {data.ShapeTexture}, instance {data.NorthInstanceIndex}");
		SetInstanceTransform(data.ShapeTexture, data.NorthInstanceIndex,
			centerPosition + new Vector3(0, 0, -Constants.HalfWallWidth), Mathf.Pi);

		// South face (Shape texture, facing south at +Z edge)
		GD.Print($"  South face: texture {data.ShapeTexture}, instance {data.SouthInstanceIndex}");
		SetInstanceTransform(data.ShapeTexture, data.SouthInstanceIndex,
			centerPosition + new Vector3(0, 0, Constants.HalfWallWidth), 0f);

		// East face (DarkSide texture, facing east at +X edge)
		GD.Print($"  East face: texture {(ushort)(data.ShapeTexture + 1)}, instance {data.EastInstanceIndex}");
		SetInstanceTransform((ushort)(data.ShapeTexture + 1), data.EastInstanceIndex,
			centerPosition + new Vector3(Constants.HalfWallWidth, 0, 0), Constants.HalfPi);

		// West face (DarkSide texture, facing west at -X edge)
		GD.Print($"  West face: texture {(ushort)(data.ShapeTexture + 1)}, instance {data.WestInstanceIndex}");
		SetInstanceTransform((ushort)(data.ShapeTexture + 1), data.WestInstanceIndex,
			centerPosition + new Vector3(-Constants.HalfWallWidth, 0, 0), -Constants.HalfPi);
	}

	/// <summary>
	/// Sets the transform for a specific instance in a MultiMesh.
	/// </summary>
	private void SetInstanceTransform(ushort textureIndex, int instanceIndex, Vector3 position, float rotationY)
	{
		if (!MeshInstances.TryGetValue(textureIndex, out MultiMeshInstance3D meshInstance))
		{
			GD.PrintErr($"No MultiMesh found for texture {textureIndex}");
			GD.PrintErr($"  Available: {string.Join(", ", MeshInstances.Keys.OrderBy(k => k))}");
			return;
		}

		Transform3D transform = Transform3D.Identity.Rotated(Vector3.Up, rotationY);
		transform.Origin = position;
		meshInstance.Multimesh.SetInstanceTransform(instanceIndex, transform);

		GD.Print($"Set instance {instanceIndex} in texture {textureIndex} to position {position}, rotation {rotationY * 180 / Mathf.Pi}°");
	}

	/// <summary>
	/// Allocates the next available instance index for a texture.
	/// </summary>
	private int AllocateInstance(ushort textureIndex)
	{
		if (!nextInstanceIndex.ContainsKey(textureIndex))
			nextInstanceIndex[textureIndex] = 0;

		return nextInstanceIndex[textureIndex]++;
	}

	/// <summary>
	/// Creates a MultiMeshInstance3D for all walls using a specific texture.
	/// </summary>
	private static MultiMeshInstance3D CreateMultiMeshForTexture(
		ushort shape,
		StandardMaterial3D material,
		List<MapAnalysis.WallSpawn> walls,
		int totalInstanceCount)
	{
		// Debug: Check material validity
		if (material == null)
			GD.PrintErr($"ERROR: Material is null for shape {shape}");
		else if (material.AlbedoTexture == null)
			GD.PrintErr($"WARNING: Material for shape {shape} has null AlbedoTexture");

		// Create MultiMesh with exact size (walls + reserved pushwall faces)
		MultiMesh multiMesh = new()
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
			Mesh = Constants.WallMesh,
			InstanceCount = totalInstanceCount,
		};

		// Set transforms for regular wall instances
		for (int i = 0; i < walls.Count; i++)
		{
			Transform3D transform = CalculateWallTransform(walls[i]);
			multiMesh.SetInstanceTransform(i, transform);
		}

		// Initialize remaining slots (for pushwalls) to identity transform
		// They'll be positioned when AddPushWall is called
		for (int i = walls.Count; i < totalInstanceCount; i++)
			multiMesh.SetInstanceTransform(i, Transform3D.Identity);

		// Create MultiMeshInstance3D
		MultiMeshInstance3D meshInstance = new()
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
