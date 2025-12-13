using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using static BenMcLean.Wolf3D.Assets.MapAnalyzer;

namespace BenMcLean.Wolf3D.VR;

/// <summary>
/// Generates MultiMesh instances for all doors in a Wolfenstein 3D map.
/// Uses MultiMesh for efficient rendering of door quads in VR.
/// Doors automatically flip orientation each frame to face the player based on their position.
/// This node contains all door multimeshes as children - just add it to the scene tree.
/// </summary>
public partial class Doors : Node3D
{
	/// <summary>
	/// Dictionary of MultiMeshInstance3D nodes with normal UV materials, indexed by texture number.
	/// </summary>
	public Dictionary<ushort, MultiMeshInstance3D> NormalMeshes { get; private init; }

	/// <summary>
	/// Dictionary of MultiMeshInstance3D nodes with flipped/mirrored UV materials, indexed by texture number.
	/// </summary>
	public Dictionary<ushort, MultiMeshInstance3D> FlippedMeshes { get; private init; }

	// Door tracking
	private readonly List<DoorData> doors = [];
	private readonly IReadOnlyDictionary<ushort, StandardMaterial3D> opaqueMaterials;
	private readonly IReadOnlyDictionary<ushort, ShaderMaterial> flippedMaterials;
	private readonly Dictionary<ushort, int> nextInstanceIndex = []; // Tracks next available instance per texture

	private class DoorData
	{
		public ushort TextureIndex;           // VSwap page number
		public int InstanceIndex;             // Instance index (same in both back-cull and front-cull multimeshes)
		public bool FacesEastWest;            // true = door can face E/W (runs N-S), false = door can face N/S (runs E-W)
		public uint BaseGridX, BaseGridZ;     // Fixed-point 16:16 base coordinates
		public uint CurrentX, CurrentZ;       // Fixed-point 16:16 current coordinates (for sliding)
	}

	/// <summary>
	/// Calculates which texture indices are needed for a collection of door spawns.
	/// Accounts for FacesEastWest orientation (even=horizontal/light, odd=vertical/dark).
	/// </summary>
	public static IEnumerable<ushort> GetRequiredTextureIndices(IEnumerable<MapAnalysis.DoorSpawn> doorSpawns)
	{
		return doorSpawns
			.Select(d => d.FacesEastWest ? (ushort)(d.Shape + 1) : d.Shape)
			.Distinct();
	}

	/// <summary>
	/// Creates door geometry from map data using two quads per door for proper UV handling.
	/// Both use back-face culling, with flipped materials having mirrored UVs.
	/// </summary>
	/// <param name="opaqueMaterials">Dictionary of opaque materials with normal UVs from GodotResources.OpaqueMaterials</param>
	/// <param name="flippedMaterials">Dictionary of flipped materials by texture index</param>
	/// <param name="doorSpawns">Collection of door spawn data from map analysis</param>
	public Doors(IReadOnlyDictionary<ushort, StandardMaterial3D> opaqueMaterials, IReadOnlyDictionary<ushort, ShaderMaterial> flippedMaterials, IEnumerable<MapAnalysis.DoorSpawn> doorSpawns)
	{
		this.opaqueMaterials = opaqueMaterials ?? throw new ArgumentNullException(nameof(opaqueMaterials));
		this.flippedMaterials = flippedMaterials ?? throw new ArgumentNullException(nameof(flippedMaterials));

		if (doorSpawns is null || !doorSpawns.Any())
		{
			NormalMeshes = [];
			FlippedMeshes = [];
			return;
		}

		// Group doors by CALCULATED texture (accounting for FacesEastWest orientation)
		// Horizontal doors (FacesEastWest=false) use Shape (even)
		// Vertical doors (FacesEastWest=true) use Shape+1 (odd)
		Dictionary<ushort, List<MapAnalysis.DoorSpawn>> doorsByTexture = doorSpawns
			.GroupBy(d => d.FacesEastWest ? (ushort)(d.Shape + 1) : d.Shape)
			.ToDictionary(g => g.Key, g => g.ToList());

		// Calculate instance counts needed (one instance per door in each multimesh)
		Dictionary<ushort, int> instanceCounts = doorsByTexture.ToDictionary(
			kvp => kvp.Key,
			kvp => kvp.Value.Count);

		// Track starting index for each texture (shared between normal and flipped)
		foreach (KeyValuePair<ushort, List<MapAnalysis.DoorSpawn>> kvp in doorsByTexture)
			nextInstanceIndex[kvp.Key] = 0;

		// Create normal UV MultiMeshes for each unique texture
		NormalMeshes = instanceCounts.Keys.ToDictionary(
			shape => shape,
			shape => CreateMultiMeshForTexture(
				shape,
				opaqueMaterials[shape],
				instanceCounts[shape],
				"Normal"));

		// Create flipped UV MultiMeshes for each unique texture
		FlippedMeshes = instanceCounts.Keys.ToDictionary(
			shape => shape,
			shape => CreateMultiMeshForTexture(
				shape,
				flippedMaterials[shape],
				instanceCounts[shape],
				"Flipped"));

		// Add all multimeshes as children of this node
		foreach (MultiMeshInstance3D meshInstance in NormalMeshes.Values)
			AddChild(meshInstance);
		foreach (MultiMeshInstance3D meshInstance in FlippedMeshes.Values)
			AddChild(meshInstance);

		// Initialize all doors (2 quads per door - one for each side)
		int i = 0;
		foreach (MapAnalysis.DoorSpawn doorSpawn in doorSpawns)
		{

			// Convert grid coordinates to fixed-point 16:16
			// Add 0x8000 (half tile) to center the door in its tile
			uint baseX = ((uint)doorSpawn.X << 16) + 0x8000;
			uint baseZ = ((uint)doorSpawn.Y << 16) + 0x8000;

			// Calculate texture based on orientation (doors are paired: even=horizontal/light, odd=vertical/dark)
			// doorSpawn.Shape is always even (base texture), add 1 for vertical doors
			ushort textureIndex = doorSpawn.FacesEastWest
				? (ushort)(doorSpawn.Shape + 1)  // Vertical door: use odd page (darker, like vertwall)
				: doorSpawn.Shape;                // Horizontal door: use even page (lighter, like horizwall)

			// Allocate ONE instance for this door (same index used in both back-cull and front-cull multimeshes)
			int instanceIndex = AllocateInstance(textureIndex);

			DoorData data = new()
			{
				TextureIndex = textureIndex,
				InstanceIndex = instanceIndex,
				FacesEastWest = doorSpawn.FacesEastWest,
				BaseGridX = baseX,
				BaseGridZ = baseZ,
				CurrentX = baseX,
				CurrentZ = baseZ,
			};

			doors.Add(data);

			// Set initial transforms for both instances (back-cull and front-cull)
			UpdateDoorTransforms(data);

			i++;
		}
	}

	// No _Process needed! Back-face culling automatically shows the correct side

	/// <summary>
	/// Updates a door's position during sliding animation.
	/// Called by the discrete event simulator as the door opens/closes.
	/// </summary>
	/// <param name="index">Index in the door spawns collection</param>
	/// <param name="newPosition">New position coordinate (16:16 fixed-point) along the door's sliding axis</param>
	public void MoveDoor(ushort index, uint newPosition)
	{
		if (index >= doors.Count)
		{
			GD.PrintErr($"Invalid door index: {index}");
			return;
		}

		DoorData door = doors[index];

		// Update the position along the door's sliding axis
		// Door facing E/W (runs N-S) slides along X (E-W), door facing N/S (runs E-W) slides along Z (N-S)
		if (door.FacesEastWest)
			door.CurrentX = newPosition;  // Door faces E/W (runs N-S), slides E-W along X axis
		else
			door.CurrentZ = newPosition;  // Door faces N/S (runs E-W), slides N-S along Z axis

		// Update both instances (they're always at the same position)
		UpdateDoorTransforms(door);
	}

	/// <summary>
	/// Updates transforms for BOTH instances of a door (front and back quads).
	/// Both instances are at the same position but with different scales to handle UV flipping.
	/// </summary>
	private void UpdateDoorTransforms(DoorData door)
	{
		// Convert fixed-point to float coordinates
		float worldX = door.CurrentX / 65536f;
		float worldZ = door.CurrentZ / 65536f;

		Vector3 position = new(
			Constants.FloatCoordinate((int)(worldX)) + Constants.HalfWallWidth,
			Constants.HalfWallHeight,
			Constants.FloatCoordinate((int)(worldZ)) + Constants.HalfWallWidth
		);

		// Determine base rotation based on door orientation
		float rotationY;
		if (door.FacesEastWest)
		{
			// Door runs N-S, faces E or W
			rotationY = Constants.HalfPi;  // Base rotation faces East
		}
		else
		{
			// Door runs E-W, faces N or S
			rotationY = 0f;  // Base rotation faces South
		}

		// Set transforms for both instances at same position/rotation
		// For North-South doors: normal material on normal scale, flipped material on flipped scale
		// For East-West doors: SWAP them - flipped material on normal scale, normal material on flipped scale
		if (door.FacesEastWest)
		{
			// East-West doors: swap materials
			SetInstanceTransform(door.TextureIndex, door.InstanceIndex, position, rotationY, new Vector3(1, 1, 1), useFlipped: true);
			SetInstanceTransform(door.TextureIndex, door.InstanceIndex, position, rotationY, new Vector3(-1, 1, 1), useFlipped: false);
		}
		else
		{
			// North-South doors: normal order
			SetInstanceTransform(door.TextureIndex, door.InstanceIndex, position, rotationY, new Vector3(1, 1, 1), useFlipped: false);
			SetInstanceTransform(door.TextureIndex, door.InstanceIndex, position, rotationY, new Vector3(-1, 1, 1), useFlipped: true);
		}
	}

	/// <summary>
	/// Sets the transform for a specific instance in a MultiMesh.
	/// </summary>
	private void SetInstanceTransform(ushort textureIndex, int instanceIndex, Vector3 position, float rotationY, Vector3 scale, bool useFlipped)
	{
		Dictionary<ushort, MultiMeshInstance3D> meshes = useFlipped ? FlippedMeshes : NormalMeshes;
		string meshType = useFlipped ? "flipped" : "normal";

		if (!meshes.TryGetValue(textureIndex, out MultiMeshInstance3D meshInstance))
		{
			GD.PrintErr($"No {meshType} MultiMesh found for texture {textureIndex}");
			GD.PrintErr($"  Available: {string.Join(", ", meshes.Keys.OrderBy(k => k))}");
			return;
		}

		// Build transform: Scale → Rotate → Translate
		Transform3D transform = Transform3D.Identity.Scaled(scale).Rotated(Vector3.Up, rotationY);
		transform.Origin = position;
		meshInstance.Multimesh.SetInstanceTransform(instanceIndex, transform);
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
	/// Creates a MultiMeshInstance3D for all doors using a specific texture.
	/// </summary>
	private static MultiMeshInstance3D CreateMultiMeshForTexture(
		ushort shape,
		Material material,
		int instanceCount,
		string cullTypeSuffix)
	{
		// Debug: Check material validity
		if (material == null)
			GD.PrintErr($"ERROR: Material is null for shape {shape}");

		// Create MultiMesh with exact size
		MultiMesh multiMesh = new()
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
			Mesh = Constants.WallMesh,
			InstanceCount = instanceCount,
		};

		// Initialize all instances to identity transform
		// They'll be positioned when doors are initialized
		for (int i = 0; i < instanceCount; i++)
			multiMesh.SetInstanceTransform(i, Transform3D.Identity);

		// Create MultiMeshInstance3D
		MultiMeshInstance3D meshInstance = new()
		{
			Multimesh = multiMesh,
			MaterialOverride = material,
			Name = $"Doors_Texture_{shape}_{cullTypeSuffix}",
		};

		return meshInstance;
	}

}
