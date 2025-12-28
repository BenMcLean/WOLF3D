using System.Collections.Generic;
using BenMcLean.Wolf3D.Assets;
using BenMcLean.Wolf3D.Simulator;
using Godot;

namespace BenMcLean.Wolf3D.VR;

/// <summary>
/// The VR action stage - loads and displays a Wolfenstein 3D level in VR.
/// Handles camera, walls, doors, sprites, and gameplay simulation.
/// </summary>
public partial class ActionStage : Node3D
{
	[Export]
	public int LevelIndex { get; set; } = 0;

	private readonly ShaderMaterial _skyMaterial = new()
	{
		Shader = new() { Code = """
shader_type sky;

uniform vec4 floor_color;
uniform vec4 ceiling_color;

void sky() {
	COLOR = mix(floor_color.rgb, ceiling_color.rgb, step(0.0, EYEDIR.y));
}
""", }
	};

	private Camera3D _camera;
	private FreeLookCamera _freeLookCamera;
	private Walls _walls;
	private DebugMarkers _debugMarkers;
	private Fixtures _fixtures;
	private Bonuses _bonuses;
	private Actors _actors;
	private Doors _doors;
	private SimulatorController _simulatorController;

	public override void _Ready()
	{
		// Get current level analysis
		MapAnalyzer.MapAnalysis currentLevel = Shared.SharedAssetManager.CurrentGame.MapAnalyses[LevelIndex];

		// Setup sky with floor/ceiling colors from map
		SetupSky(currentLevel);

		// Position camera at player start
		Vector3 cameraPosition;
		float cameraRotationY = 0f;
		if (currentLevel.PlayerStart.HasValue)
		{
			MapAnalyzer.MapAnalysis.PlayerSpawn playerStart = currentLevel.PlayerStart.Value;
			// Center of the player's starting grid square
			cameraPosition = new Vector3(
				playerStart.X.ToMetersCentered(),
				Constants.HalfTileHeight,
				playerStart.Y.ToMetersCentered()
			);
			// Convert Direction enum to Godot rotation using ToAngle extension method
			// Handles Wolf3D coordinate system → Godot coordinate system conversion
			cameraRotationY = playerStart.Facing.ToAngle();
		}
		else
		{
			// Fallback to origin if no player start found
			cameraPosition = new Vector3(0, Constants.HalfTileHeight, 0);
			GD.PrintErr("Warning: No player start found in map!");
		}

		_camera = new()
		{
			Position = cameraPosition,
			RotationDegrees = new Vector3(0, Mathf.RadToDeg(cameraRotationY), 0),
			Current = true,
		};
		AddChild(_camera);
		_freeLookCamera = new();
		_camera.AddChild(_freeLookCamera);
		_freeLookCamera.Enabled = true;

		// Create walls for the current level and add to scene
		_walls = new Walls(
			VRAssetManager.OpaqueMaterials,
			currentLevel,
			Shared.SharedAssetManager.DigiSounds);  // Sound library for pushwall sounds
		AddChild(_walls);

		// Create debug markers for patrol points and ambush actors
		_debugMarkers = new DebugMarkers(currentLevel);
		AddChild(_debugMarkers);

		// Create fixtures (billboarded sprites) for the current level and add to scene
		_fixtures = new Fixtures(
			VRAssetManager.SpriteMaterials,
			currentLevel.StaticSpawns,
			() => _freeLookCamera.GlobalRotation.Y,  // Delegate returns camera Y rotation for billboard effect
			Shared.SharedAssetManager.CurrentGame.VSwap.SpritePage);
		AddChild(_fixtures);

		// Create bonuses (bonus/pickup items with game logic) for the current level and add to scene
		_bonuses = new Bonuses(
			VRAssetManager.SpriteMaterials,
			() => _freeLookCamera.GlobalRotation.Y);  // Delegate returns camera Y rotation for billboard effect
		AddChild(_bonuses);

		// Create actors (dynamic actor sprites with game logic) for the current level and add to scene
		_actors = new Actors(
			VRAssetManager.SpriteMaterials,
			Shared.SharedAssetManager.DigiSounds,      // Digi sounds for actor alert sounds
			() => _freeLookCamera.GlobalPosition,      // Viewer position for directional sprites (player pos, or future MR camera)
			() => _freeLookCamera.GlobalRotation.Y);   // Camera Y rotation for billboard effect
		AddChild(_actors);
		// Create doors for the current level and add to scene
		IEnumerable<ushort> doorTextureIndices = Doors.GetRequiredTextureIndices(currentLevel.Doors);
		Dictionary<ushort, ShaderMaterial> flippedDoorMaterials = VRAssetManager.CreateFlippedMaterialsForDoors(doorTextureIndices);
		_doors = new Doors(
			VRAssetManager.OpaqueMaterials,  // Materials with normal UVs (shared with walls)
			flippedDoorMaterials,  // Flipped materials (only for door textures)
			currentLevel.Doors,
			Shared.SharedAssetManager.DigiSounds);  // Sound library for door sounds
		AddChild(_doors);

		// Create simulator controller and initialize with map data
		_simulatorController = new SimulatorController();
		AddChild(_simulatorController);
		// Initialize with StateCollection from game assets
		_simulatorController.Initialize(
			Shared.SharedAssetManager.CurrentGame.MapAnalyzer,
			currentLevel,
			_doors,
			_walls,
			_bonuses,
			_actors,
			Shared.SharedAssetManager.CurrentGame.StateCollection,
			() => (_freeLookCamera.GlobalPosition.X.ToFixedPoint(), _freeLookCamera.GlobalPosition.Z.ToFixedPoint()));  // Delegate returns Wolf3D 16.16 fixed-point coordinates
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventKey keyEvent && keyEvent.Pressed)
		{
			if (keyEvent.Keycode == Key.R)
			{
				// Use door or pushwall player is facing
				OperateDoorOrPushWallPlayerIsFacing();
			}
		}
	}

	/// <summary>
	/// Finds and operates the door or pushwall the player is facing.
	/// Projects 1 WallWidth forward from camera position/rotation and checks that tile.
	/// </summary>
	private void OperateDoorOrPushWallPlayerIsFacing()
	{
		// Get camera's position and Y rotation
		Vector3 cameraPos = _freeLookCamera.GlobalPosition;
		float rotationY = _freeLookCamera.GlobalRotation.Y;

		// Project forward 1.5 tiles to reliably detect doors/pushwalls in front
		// In Godot: Y rotation of 0 = facing -Z (North), rotating clockwise
		float forwardX = Mathf.Sin(rotationY);
		float forwardZ = -Mathf.Cos(rotationY);
		Vector3 forwardPoint = cameraPos + new Vector3(
			forwardX * -Constants.TileWidth,
			0f,
			forwardZ * Constants.TileWidth);

		// Convert world position to tile coordinates
		ushort tileX = (ushort)(forwardPoint.X / Constants.TileWidth);
		ushort tileY = (ushort)(forwardPoint.Z / Constants.TileWidth);

		// Try door first
		ushort? doorIndex = FindDoorAtTile(tileX, tileY);
		if (doorIndex.HasValue)
		{
			_simulatorController.OperateDoor(doorIndex.Value);
			return;
		}

		// Try pushwall second
		ushort? pushWallIndex = FindPushWallAtTile(tileX, tileY);
		if (pushWallIndex.HasValue)
		{
			// Determine push direction from camera rotation (extension method in ExtensionMethods.cs)
			Direction dir = rotationY.ToCardinalDirection();
			_simulatorController.ActivatePushWall(tileX, tileY, dir);
		}
	}

	/// <summary>
	/// Finds the door index at the specified tile coordinates.
	/// Returns null if no door exists at that tile.
	/// </summary>
	private ushort? FindDoorAtTile(ushort tileX, ushort tileY)
	{
		// Search through all doors in the simulator
		for (int i = 0; i < _simulatorController.Doors.Count; i++)
		{
			Simulator.Door door = _simulatorController.Doors[i];
			if (door.TileX == tileX && door.TileY == tileY)
				return (ushort)i;
		}
		return null;
	}

	/// <summary>
	/// Finds the pushwall index at the specified tile coordinates.
	/// Returns null if no pushwall exists at that tile.
	/// </summary>
	private ushort? FindPushWallAtTile(ushort tileX, ushort tileY)
	{
		// Search through all pushwalls in the simulator
		for (int i = 0; i < _simulatorController.PushWalls.Count; i++)
		{
			Simulator.PushWall pushWall = _simulatorController.PushWalls[i];
			(ushort pwTileX, ushort pwTileY) = pushWall.GetTilePosition();
			if (pwTileX == tileX && pwTileY == tileY)
				return (ushort)i;
		}
		return null;
	}
	/// <summary>
	/// Sets up the VR sky with floor and ceiling colors from the map properties.
	/// Converts VGA palette indices to RGB colors.
	/// </summary>
	private void SetupSky(MapAnalyzer.MapAnalysis level)
	{
		// Get VGA palette (usually palette 0)
		uint[] palette = Shared.SharedAssetManager.CurrentGame.VSwap.Palettes[0];

		// Get floor and ceiling colors from map, or use defaults
		Color floorColor = GetColorFromPalette(palette, level.Floor, new Color(0.33f, 0.33f, 0.33f)); // Default: dark gray
		Color ceilingColor = GetColorFromPalette(palette, level.Ceiling, new Color(0.2f, 0.2f, 0.2f)); // Default: darker gray

		// Create world environment with divided skybox
		WorldEnvironment worldEnvironment = new()
		{
			Environment = new()
			{
				Sky = new()
				{
					SkyMaterial = _skyMaterial,
				},
				BackgroundMode = Environment.BGMode.Sky,
			}
		};
		AddChild(worldEnvironment);

		// Set shader parameters for floor and ceiling
		_skyMaterial.SetShaderParameter("floor_color", floorColor);
		_skyMaterial.SetShaderParameter("ceiling_color", ceilingColor);
	}

	/// <summary>
	/// Converts a VGA palette index to a Godot Color.
	/// Returns defaultColor if paletteIndex is null or invalid.
	/// </summary>
	private static Color GetColorFromPalette(uint[] palette, byte? paletteIndex, Color defaultColor)
	{
		if (paletteIndex == null || paletteIndex >= palette.Length)
			return defaultColor;

		// VGA palette entries are stored as uint (from VSwap.Indices2ByteArray)
		uint colorValue = palette[paletteIndex.Value];

		// Try ABGR byte order (common for VGA palettes)
		byte a = (byte)(colorValue & 0xFF);
		byte b = (byte)((colorValue >> 8) & 0xFF);
		byte g = (byte)((colorValue >> 16) & 0xFF);
		byte r = (byte)((colorValue >> 24) & 0xFF);

		return new Color(r / 255f, g / 255f, b / 255f, a / 255f);
	}

	public override void _Process(double delta)
	{
		// Fixtures updates billboard rotations automatically in its own _Process
		// Bonuses updates billboard rotations automatically in its own _Process
		// Doors use two-quad approach with back-face culling - no per-frame updates needed
		// SimulatorController drives the simulator and updates door/bonus states automatically in its own _Process
	}
}
