using BenMcLean.Wolf3D.Shared;
using BenMcLean.Wolf3D.Simulator;
using Godot;
using System.Collections.Generic;
using System.Linq;
using static BenMcLean.Wolf3D.Assets.Gameplay.MapAnalyzer;

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
	/// Shader for tiling floor/ceiling textures.
	/// UV coordinates are scaled by tile_repeat uniform to tile the texture across the map.
	/// </summary>
	private const string TilingShaderCode = @"
shader_type spatial;
render_mode unshaded, cull_back;

uniform sampler2D albedo_texture : source_color, filter_nearest, repeat_enable;
uniform vec2 tile_repeat = vec2(1.0, 1.0);

void fragment() {
	vec2 uv = UV * tile_repeat;
	ALBEDO = texture(albedo_texture, uv).rgb;
}
";
	private static Shader _tilingShader;
	/// <summary>
	/// Dictionary of MultiMeshInstance3D nodes, indexed by texture number.
	/// </summary>
	public Dictionary<ushort, MultiMeshInstance3D> MeshInstances { get; private init; }
	/// <summary>
	/// Floor mesh covering the entire map at Y=0, facing upward.
	/// </summary>
	public MeshInstance3D Floor { get; private init; }
	/// <summary>
	/// Ceiling mesh covering the entire map at Y=TileHeight, facing downward.
	/// </summary>
	public MeshInstance3D Ceiling { get; private init; }
	// Pushwall tracking
	private readonly List<PushWallData> pushWalls = [];
	private readonly IReadOnlyDictionary<ushort, StandardMaterial3D> wallMaterials;
	private readonly Dictionary<ushort, int> nextInstanceIndex = []; // Tracks next available instance per texture
	private readonly IReadOnlyDictionary<string, AudioStreamWav> digiSounds; // Sound library
	private Simulator.Simulator simulator;
	private class PushWallData
	{
		public ushort ShapeTexture;      // Horizontal/lighter texture (north/south faces)
		// DarkSide texture is always ShapeTexture + 1 (vertical/darker, east/west faces)
		public int NorthInstanceIndex,   // In MeshInstances for ShapeTexture
			SouthInstanceIndex,   // In MeshInstances for ShapeTexture
			EastInstanceIndex,    // In MeshInstances for ShapeTexture + 1
			WestInstanceIndex;    // In MeshInstances for ShapeTexture + 1
		public AudioStreamPlayer3D Speaker; // 3D audio speaker for this pushwall
	}
	/// <summary>
	/// Creates wall geometry from map data.
	/// </summary>
	/// <param name="wallMaterials">Dictionary of wall materials from GodotResources.OpaqueMaterials</param>
	/// <param name="mapAnalysis">Map analysis containing wall and pushwall spawn data</param>
	/// <param name="digiSounds">Dictionary of digi sounds from SharedAssetManager</param>
	public Walls(
		IReadOnlyDictionary<ushort, StandardMaterial3D> wallMaterials,
		MapAnalysis mapAnalysis,
		IReadOnlyDictionary<string, AudioStreamWav> digiSounds)
	{
		this.wallMaterials = wallMaterials;
		this.digiSounds = digiSounds ?? throw new System.ArgumentNullException(nameof(digiSounds));
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
				instanceCounts[(ushort)(pw.Shape + 1)] = instanceCounts.GetValueOrDefault((ushort)(pw.Shape + 1)) + 2;
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
			foreach (MapAnalysis.PushWallSpawn pw in mapAnalysis.PushWalls)
				AddPushWall(pw.Shape, pw.X, pw.Y);
		// Create floor and ceiling
		float mapWidthMeters = mapAnalysis.Width * Constants.TileWidth,
			mapDepthMeters = mapAnalysis.Depth * Constants.TileWidth;
		Vector3 mapCenter = new(mapWidthMeters / 2f, 0f, mapDepthMeters / 2f);
		// Floor at Y=0, facing upward
		Floor = new MeshInstance3D
		{
			Name = "Floor",
			Mesh = new QuadMesh { Size = new Vector2(mapWidthMeters, mapDepthMeters) },
			MaterialOverride = mapAnalysis.FloorTile.HasValue
				? CreateTilingMaterial(wallMaterials[mapAnalysis.FloorTile.Value], mapAnalysis.Width, mapAnalysis.Depth)
				: new StandardMaterial3D
				{
					ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
					AlbedoColor = SharedAssetManager.GetPaletteColor(mapAnalysis.Floor.Value),
				},
		};
		Floor.RotateX(-Mathf.Pi / 2f); // Rotate to face upward
		Floor.Position = mapCenter;
		AddChild(Floor);
		// Ceiling at Y=TileHeight, facing downward
		Ceiling = new MeshInstance3D
		{
			Name = "Ceiling",
			Mesh = new QuadMesh { Size = new Vector2(mapWidthMeters, mapDepthMeters) },
			MaterialOverride = mapAnalysis.CeilingTile.HasValue
				? CreateTilingMaterial(wallMaterials[mapAnalysis.CeilingTile.Value], mapAnalysis.Width, mapAnalysis.Depth)
				: new StandardMaterial3D
				{
					ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
					AlbedoColor = SharedAssetManager.GetPaletteColor(mapAnalysis.Ceiling.Value),
				},
		};
		Ceiling.RotateX(Mathf.Pi / 2f); // Rotate to face downward
		Ceiling.Position = mapCenter with { Y = Constants.TileHeight };
		AddChild(Ceiling);
	}
	/// <summary>
	/// Subscribes to simulator events for pushwall movement, sounds, and elevator switches.
	/// Call this after the simulator is initialized.
	/// </summary>
	public void Subscribe(Simulator.Simulator simulator)
	{
		Unsubscribe();
		this.simulator = simulator;
		simulator.PushWallPositionChanged += OnPushWallPositionChanged;
		simulator.PushWallPlaySound += OnPushWallPlaySound;
		simulator.ElevatorSwitchFlipped += OnElevatorSwitchFlipped;
	}
	/// <summary>
	/// Unsubscribes from simulator events.
	/// Automatically called when subscribing to a new simulator or when this node is freed.
	/// </summary>
	public void Unsubscribe()
	{
		if (simulator == null)
			return;
		simulator.PushWallPositionChanged -= OnPushWallPositionChanged;
		simulator.PushWallPlaySound -= OnPushWallPlaySound;
		simulator.ElevatorSwitchFlipped -= OnElevatorSwitchFlipped;
	}
	public override void _ExitTree()
	{
		Unsubscribe();
	}
	/// <summary>
	/// Handles pushwall position changes from the simulator.
	/// </summary>
	private void OnPushWallPositionChanged(Simulator.PushWallPositionChangedEvent evt)
	{
		MovePushWall(evt.PushWallIndex, evt.X, evt.Y);
	}
	/// <summary>
	/// Handles pushwall sound playback requests from the simulator.
	/// </summary>
	private void OnPushWallPlaySound(Simulator.PushWallPlaySoundEvent evt)
	{
		if (evt.PushWallIndex >= pushWalls.Count)
		{
			GD.PrintErr($"Invalid pushwall index for sound: {evt.PushWallIndex}");
			return;
		}

		PushWallData data = pushWalls[evt.PushWallIndex];
		if (digiSounds.TryGetValue(evt.SoundName, out AudioStreamWav stream))
		{
			data.Speaker.Stream = stream;
			data.Speaker.Play();
		}
		else
			// Fall back to global playback (AdLib/PC Speaker) if digi sound not available
			EventBus.Emit(GameEvent.PlaySound, evt.SoundName);
	}
	/// <summary>
	/// Handles elevator switch texture flip from the simulator.
	/// Swaps the wall texture from unpressed to pressed state.
	/// WL_AGENT.C: tilemap[checkx][checky]++ flips the switch visually.
	/// </summary>
	private void OnElevatorSwitchFlipped(Simulator.ElevatorSwitchFlippedEvent evt) =>
		SwapTexture(evt.OldTexture, evt.NewTexture);
	/// <summary>
	/// Adds a pushwall to the rendering system.
	/// A pushwall is a 4-sided cube (north, south, east, west faces).
	/// NOTE: Usually called automatically by constructor. Pushwall IDs match their index in MapAnalysis.PushWalls.
	/// </summary>
	/// <param name="wallTexture">The wall texture number (horizontal page)</param>
	/// <param name="tileX">Tile X coordinate of initial pushwall position</param>
	/// <param name="tileY">Tile Y coordinate of initial pushwall position</param>
	/// <returns>Unique ID for this pushwall (used with MovePushWall)</returns>
	public ushort AddPushWall(ushort wallTexture, ushort tileX, ushort tileY)
	{
		ushort shapeTexture = wallTexture,
			darkSideTexture = (ushort)(wallTexture + 1);
		// Create audio speaker for this pushwall
		// Position will be updated in UpdatePushWallTransforms as the pushwall moves
		ushort pushWallId = (ushort)pushWalls.Count;
		AudioStreamPlayer3D speaker = new()
		{
			Name = $"PushWallSpeaker_{pushWallId}",
			Bus = "DigiSounds",
		};
		AddChild(speaker);
		PushWallData data = new()
		{
			ShapeTexture = shapeTexture,
			NorthInstanceIndex = AllocateInstance(shapeTexture),
			SouthInstanceIndex = AllocateInstance(shapeTexture),
			EastInstanceIndex = AllocateInstance(darkSideTexture),
			WestInstanceIndex = AllocateInstance(darkSideTexture),
			Speaker = speaker,
		};
		// Set initial transforms at grid position (convert tiles to fixed-point centered)
		UpdatePushWallTransforms(data, tileX.ToFixedPointCenter(), tileY.ToFixedPointCenter());
		pushWalls.Add(data);
		return pushWallId;
	}
	/// <summary>
	/// Moves a pushwall to a new position. Call during gameplay as pushwall moves.
	/// </summary>
	/// <param name="pushWallId">The ID returned from AddPushWall</param>
	/// <param name="fixedX">Wolf3D 16.16 fixed-point X coordinate (can be off-grid for smooth movement)</param>
	/// <param name="fixedY">Wolf3D 16.16 fixed-point Y coordinate (can be off-grid for smooth movement)</param>
	public void MovePushWall(ushort pushWallId, int fixedX, int fixedY)
	{
		if (pushWallId >= pushWalls.Count)
		{
			GD.PrintErr($"Invalid pushwall ID: {pushWallId}");
			return;
		}
		PushWallData data = pushWalls[pushWallId];
		UpdatePushWallTransforms(data, fixedX, fixedY);
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
	/// <param name="data">Pushwall data containing instance indices</param>
	/// <param name="fixedX">Wolf3D 16.16 fixed-point X coordinate</param>
	/// <param name="fixedY">Wolf3D 16.16 fixed-point Y coordinate</param>
	private void UpdatePushWallTransforms(PushWallData data, int fixedX, int fixedY)
	{
		Vector3 centerPosition = new(
			x: fixedX.ToMeters(),
			y: Constants.HalfTileHeight,
			z: fixedY.ToMeters());
		// Update audio speaker position
		data.Speaker.Position = centerPosition;
		// North face (Shape texture, facing north at -Z edge)
		SetInstanceTransform(data.ShapeTexture, data.NorthInstanceIndex,
			centerPosition + new Vector3(0, 0, -Constants.HalfTileWidth), Mathf.Pi);
		// South face (Shape texture, facing south at +Z edge)
		SetInstanceTransform(data.ShapeTexture, data.SouthInstanceIndex,
			centerPosition + new Vector3(0, 0, Constants.HalfTileWidth), 0f);
		// East face (DarkSide texture, facing east at +X edge)
		SetInstanceTransform((ushort)(data.ShapeTexture + 1), data.EastInstanceIndex,
			centerPosition + new Vector3(Constants.HalfTileWidth, 0, 0), Constants.HalfPi);
		// West face (DarkSide texture, facing west at -X edge)
		SetInstanceTransform((ushort)(data.ShapeTexture + 1), data.WestInstanceIndex,
			centerPosition + new Vector3(-Constants.HalfTileWidth, 0, 0), -Constants.HalfPi);
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
	/// Creates a ShaderMaterial that tiles a texture across the floor or ceiling.
	/// Each tile will be exactly Constants.TileWidth in size.
	/// </summary>
	/// <param name="sourceMaterial">Source material containing the texture to tile</param>
	/// <param name="tilesX">Number of tiles in X direction (map width)</param>
	/// <param name="tilesZ">Number of tiles in Z direction (map depth)</param>
	private static ShaderMaterial CreateTilingMaterial(StandardMaterial3D sourceMaterial, ushort tilesX, ushort tilesZ)
	{
		_tilingShader ??= new Shader { Code = TilingShaderCode };
		ShaderMaterial material = new() { Shader = _tilingShader };
		material.SetShaderParameter("albedo_texture", sourceMaterial.AlbedoTexture);
		material.SetShaderParameter("tile_repeat", new Vector2(tilesX, tilesZ));
		return material;
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
		if (material is null)
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
		if (wall.FacesEastWest) // East/West facing wall (runs N-S, perpendicular to X, vertwall)
		{
			// Wall block at (X, Z) - show west face (Flip=false) or east face (Flip=true)
			position = new Vector3(
				x: wall.Flip ?
					wall.X.ToMeters() + Constants.TileWidth
					: wall.X.ToMeters(),
				y: Constants.HalfTileHeight,
				z: wall.Y.ToMetersCentered());
			// West face looks west (-90°), East face looks east (90°)
			rotationY = wall.Flip ? Constants.HalfPi : -Constants.HalfPi;
		}
		else // North/South facing wall (runs E-W, perpendicular to Z, horizwall)
		{
			// Wall block at (X, Z) - show south face (Flip=false) or north face (Flip=true)
			position = new Vector3(
				wall.X.ToMetersCentered(),
				Constants.HalfTileHeight,
				wall.Flip ?
					wall.Y.ToMeters()
					: wall.Y.ToMeters() + Constants.TileWidth);
			// South face looks south (0°), North face looks north (180°)
			rotationY = wall.Flip ? Mathf.Pi : 0f;
		}
		Transform3D transform = Transform3D.Identity.Rotated(Vector3.Up, rotationY);
		transform.Origin = position;
		return transform;
	}
}
