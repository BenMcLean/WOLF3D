using System.Collections.Generic;
using BenMcLean.Wolf3D.Assets;
using Godot;

namespace BenMcLean.Wolf3D.VR;

public partial class Root : Node3D
{
	private Camera3D _camera;
	private FreeLookCamera _freeLookCamera;
	private Sky _sky;
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
	public WorldEnvironment WorldEnvironment;
	public Assets.Assets Assets;
	public GodotResources GodotResources;
	public Walls Walls;
	public Fixtures Fixtures;
	public Doors Doors;
	public SimulatorController SimulatorController;

	public override void _Ready()
	{
		AddChild(WorldEnvironment = new()
		{
			Environment = new()
			{
				Sky = new()
				{
					SkyMaterial = _skyMaterial,
				},
				BackgroundMode = Environment.BGMode.Sky,
			}
		});
		_skyMaterial.SetShaderParameter("floor_color", new Color(0f, 1f, 0f, 1f));
		_skyMaterial.SetShaderParameter("ceiling_color", new Color(0f, 0f, 1f, 1f));

		// Load game assets
		Assets = BenMcLean.Wolf3D.Assets.Assets.Load(@"..\..\games\WL1.xml");

		// Create Godot resources (textures and materials)
		// Try scaleFactor: 4 for better performance, or 8 for maximum quality
		GodotResources = new GodotResources(Assets.VSwap, scaleFactor: 8);

		// Get first level analysis
		Assets.MapAnalyzer.MapAnalysis firstLevel = Assets.MapAnalyses[0];

		// Position camera at player start
		Vector3 cameraPosition;
		float cameraRotationY = 0f;
		if (firstLevel.PlayerStart.HasValue)
		{
			Assets.MapAnalyzer.MapAnalysis.PlayerSpawn playerStart = firstLevel.PlayerStart.Value;
			// Center of the player's starting grid square
			cameraPosition = new Vector3(
				Constants.CenterSquare(playerStart.X),
				Constants.HalfWallHeight,
				Constants.CenterSquare(playerStart.Y)
			);
			// Convert Direction enum to rotation (N=0, E=1, S=2, W=3)
			// In Godot, Y rotation: 0=North(-Z), 90=East(+X), 180=South(+Z), 270=West(-X)
			cameraRotationY = (float)playerStart.Facing * Constants.HalfPi;
		}
		else
		{
			// Fallback to origin if no player start found
			cameraPosition = new Vector3(0, Constants.HalfWallHeight, 0);
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

		// Create walls for the first level and add to scene
		Walls = new Walls(GodotResources.OpaqueMaterials, firstLevel);
		AddChild(Walls);

		// Create fixtures (billboarded sprites) for the first level and add to scene
		Fixtures = new Fixtures(
			GodotResources.SpriteMaterials,
			firstLevel.StaticSpawns,
			() => _freeLookCamera.GlobalRotation.Y,  // Delegate returns camera Y rotation for billboard effect
			Assets.VSwap.SpritePage);
		AddChild(Fixtures);

		// Create doors for the first level and add to scene
		IEnumerable<ushort> doorTextureIndices = Doors.GetRequiredTextureIndices(firstLevel.Doors);
		Dictionary<ushort, ShaderMaterial> flippedDoorMaterials = GodotResources.CreateFlippedMaterialsForDoors(doorTextureIndices);
		Doors = new Doors(
			GodotResources.OpaqueMaterials,  // Materials with normal UVs (shared with walls)
			flippedDoorMaterials,  // Flipped materials (only for door textures)
			firstLevel.Doors);
		AddChild(Doors);

		// Create simulator controller and initialize with door data
		SimulatorController = new SimulatorController();
		AddChild(SimulatorController);
		SimulatorController.Initialize(
			firstLevel.Doors,
			Doors);
	}
	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventKey keyEvent && keyEvent.Pressed)
		{
			if (keyEvent.Keycode == Key.R)
			{
				// Use door player is facing
				OperateDoorPlayerIsFacing();
			}
		}
	}

	/// <summary>
	/// Finds and operates the door the player is facing.
	/// Projects 1 WallWidth forward from camera position/rotation and checks that tile.
	/// </summary>
	private void OperateDoorPlayerIsFacing()
	{
		// Get camera's position and Y rotation
		Vector3 cameraPos = _freeLookCamera.GlobalPosition;
		float rotationY = _freeLookCamera.GlobalRotation.Y;

		// Project forward 1.5 tiles to reliably detect doors in front
		// In Godot: Y rotation of 0 = facing -Z (North), rotating clockwise
		float forwardX = Mathf.Sin(rotationY);
		float forwardZ = -Mathf.Cos(rotationY);
		Vector3 forwardPoint = cameraPos + new Vector3(
			forwardX * -Constants.WallWidth,
			0f,
			forwardZ * Constants.WallWidth);

		// Convert world position to tile coordinates
		ushort tileX = (ushort)(forwardPoint.X / Constants.WallWidth);
		ushort tileY = (ushort)(forwardPoint.Z / Constants.WallWidth);

		// Find door at this tile
		ushort? doorIndex = FindDoorAtTile(tileX, tileY);

		if (doorIndex.HasValue)
		{
			SimulatorController.OperateDoor(doorIndex.Value);
		}
	}

	/// <summary>
	/// Finds the door index at the specified tile coordinates.
	/// Returns null if no door exists at that tile.
	/// </summary>
	private ushort? FindDoorAtTile(ushort tileX, ushort tileY)
	{
		// Search through all doors in the simulator
		for (int i = 0; i < SimulatorController.Doors.Count; i++)
		{
			Simulator.Door door = SimulatorController.Doors[i];
			if (door.TileX == tileX && door.TileY == tileY)
				return (ushort)i;
		}
		return null;
	}

	public override void _Process(double delta)
	{
		// Fixtures updates billboard rotations automatically in its own _Process
		// Doors use two-quad approach with back-face culling - no per-frame updates needed
		// SimulatorController drives the simulator and updates door positions automatically in its own _Process
	}
}
