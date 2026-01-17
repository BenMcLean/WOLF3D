using System;
using System.Collections.Generic;
using BenMcLean.Wolf3D.Assets.Gameplay;
using BenMcLean.Wolf3D.Assets.Graphics;
using BenMcLean.Wolf3D.Shared;
using BenMcLean.Wolf3D.Simulator.Entities;
using BenMcLean.Wolf3D.VR.VR;
using Godot;

namespace BenMcLean.Wolf3D.VR;

/// <summary>
/// The VR action stage - loads and displays a Wolfenstein 3D level in VR.
/// Handles camera, walls, doors, sprites, and gameplay simulation.
/// Supports both VR and flatscreen display modes.
/// </summary>
public partial class ActionStage : Node3D
{
	[Export]
	public int LevelIndex { get; set; } = 0;

	// Public accessors for level components - used by systems like PixelPerfectAiming
	public MapAnalyzer.MapAnalysis MapAnalysis { get; private set; }
	public Walls Walls => _walls;
	public Doors Doors => _doors;
	public SimulatorController SimulatorController => _simulatorController;
	public Actors Actors => _actors;
	public Fixtures Fixtures => _fixtures;
	public Bonuses Bonuses => _bonuses;
	public IReadOnlyDictionary<ushort, StandardMaterial3D> SpriteMaterials => VRAssetManager.SpriteMaterials;
	public VSwap VSwap => Shared.SharedAssetManager.CurrentGame.VSwap;
	public PixelPerfectAiming PixelPerfectAiming => _pixelPerfectAiming;

	/// <summary>
	/// The active display mode (VR or flatscreen).
	/// Used for camera positioning, billboard rotation, and input handling.
	/// </summary>
	public IDisplayMode DisplayMode => _displayMode;

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

	private readonly IDisplayMode _displayMode;
	private Walls _walls;
	private DebugMarkers _debugMarkers;
	private Fixtures _fixtures;
	private Bonuses _bonuses;
	private Actors _actors;
	private Doors _doors;
	private Weapons _weapons;
	private SimulatorController _simulatorController;
	private PixelPerfectAiming _pixelPerfectAiming;
	private AimIndicator _aimIndicator;

	/// <summary>
	/// Creates a new ActionStage with the specified display mode.
	/// </summary>
	/// <param name="displayMode">The active display mode (VR or flatscreen).</param>
	public ActionStage(IDisplayMode displayMode)
	{
		_displayMode = displayMode ?? throw new ArgumentNullException(nameof(displayMode));
	}

	public override void _Ready()
	{
		try
		{
			// Get current level analysis
			MapAnalysis = Shared.SharedAssetManager.CurrentGame.MapAnalyses[LevelIndex];

		// Setup sky with floor/ceiling colors from map
		SetupSky(MapAnalysis);

		// Initialize display mode camera rig
		_displayMode.Initialize(this);

		// Position camera at player start
		Vector3 cameraPosition;
		float cameraRotationY = 0f;
		if (MapAnalysis.PlayerStart.HasValue)
		{
			MapAnalyzer.MapAnalysis.PlayerSpawn playerStart = MapAnalysis.PlayerStart.Value;
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

		// Position the display mode's origin (XROrigin or CameraHolder) at player start
		if (_displayMode.Origin != null)
		{
			_displayMode.Origin.Position = cameraPosition;
			_displayMode.Origin.RotationDegrees = new Vector3(0, Mathf.RadToDeg(cameraRotationY), 0);
		}

		// Create walls for the current level and add to scene
		_walls = new Walls(
			VRAssetManager.OpaqueMaterials,
			MapAnalysis,
			Shared.SharedAssetManager.DigiSounds);  // Sound library for pushwall sounds
		AddChild(_walls);

		// Create debug markers for patrol points and ambush actors
		_debugMarkers = new DebugMarkers(MapAnalysis);
		AddChild(_debugMarkers);

		// Create fixtures (billboarded sprites) for the current level and add to scene
		_fixtures = new Fixtures(
			VRAssetManager.SpriteMaterials,
			MapAnalysis.StaticSpawns,
			() => _displayMode.ViewerYRotation,  // Delegate returns camera Y rotation for billboard effect
			Shared.SharedAssetManager.CurrentGame.VSwap.SpritePage);
		AddChild(_fixtures);

		// Create bonuses (bonus/pickup items with game logic) for the current level and add to scene
		_bonuses = new Bonuses(
			VRAssetManager.SpriteMaterials,
			() => _displayMode.ViewerYRotation);  // Delegate returns camera Y rotation for billboard effect
		AddChild(_bonuses);

		// Create actors (dynamic actor sprites with game logic) for the current level and add to scene
		_actors = new Actors(
			VRAssetManager.SpriteMaterials,
			Shared.SharedAssetManager.DigiSounds,      // Digi sounds for actor alert sounds
			() => _displayMode.ViewerPosition,         // Viewer position for directional sprites
			() => _displayMode.ViewerYRotation);       // Camera Y rotation for billboard effect
		AddChild(_actors);

		// Create weapons display (shows player weapons at bottom of screen like original Wolf3D)
		// TODO: For VR, weapons should attach to controllers instead of camera
		_weapons = new Weapons(
			VRAssetManager.SpriteMaterials,            // Sprite materials for weapon sprites
			_displayMode.Camera);                      // Attach to camera so weapon moves with player
		AddChild(_weapons);

		// Create doors for the current level and add to scene
		IEnumerable<ushort> doorTextureIndices = Doors.GetRequiredTextureIndices(MapAnalysis.Doors);
		Dictionary<ushort, ShaderMaterial> flippedDoorMaterials = VRAssetManager.CreateFlippedMaterialsForDoors(doorTextureIndices);
		_doors = new Doors(
			VRAssetManager.OpaqueMaterials,  // Materials with normal UVs (shared with walls)
			flippedDoorMaterials,  // Flipped materials (only for door textures)
			MapAnalysis.Doors,
			Shared.SharedAssetManager.DigiSounds);  // Sound library for door sounds
		AddChild(_doors);

		// Create simulator controller and initialize with map data
		_simulatorController = new SimulatorController();
		AddChild(_simulatorController);
		// Initialize with StateCollection and WeaponCollection from game assets
		_simulatorController.Initialize(
			Shared.SharedAssetManager.CurrentGame.MapAnalyzer,
			MapAnalysis,
			_doors,
			_walls,
			_bonuses,
			_actors,
			_weapons,
			Shared.SharedAssetManager.CurrentGame.StateCollection,
			Shared.SharedAssetManager.CurrentGame.WeaponCollection,
			() => (_displayMode.ViewerPosition.X.ToFixedPoint(), _displayMode.ViewerPosition.Z.ToFixedPoint()));  // Delegate returns Wolf3D 16.16 fixed-point coordinates

		// Create pixel-perfect aiming system
		_pixelPerfectAiming = new PixelPerfectAiming(this);

		// Create debug aim indicator (temporary - won't be in final game)
		_aimIndicator = new AimIndicator(_pixelPerfectAiming, _displayMode.Camera);
		AddChild(_aimIndicator);
		}
		catch (Exception ex)
		{
			ExceptionHandler.HandleException(ex);
		}
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventKey keyEvent)
		{
			if (keyEvent.Keycode == Key.R && keyEvent.Pressed)
			{
				// Use door or pushwall player is facing
				OperateDoorOrPushWallPlayerIsFacing();
			}
			else if (keyEvent.Keycode == Key.X)
			{
				if (keyEvent.Pressed)
				{
					// Fire weapon (X key for now, VR trigger later)
					FireWeapon();
				}
				else
				{
					// Release trigger - allows semi-auto weapons to fire again
					ReleaseWeaponTrigger();
				}
			}
			else if (keyEvent.Pressed)
			{
				// Weapon switching - number keys 1-4
				// Based on WL_AGENT.C weapon selection (bt_readyknife, bt_readypistol, etc.)
				switch (keyEvent.Keycode)
				{
					case Key.Key1:
						SwitchWeapon("knife");
						break;
					case Key.Key2:
						SwitchWeapon("pistol");
						break;
					case Key.Key3:
						SwitchWeapon("machinegun");
						break;
					case Key.Key4:
						SwitchWeapon("chaingun");
						break;
				}
			}
		}
	}

	/// <summary>
	/// Fires the weapon in the primary slot (slot 0).
	/// For now, no hit detection - just triggers the animation and sound.
	/// </summary>
	private void FireWeapon()
	{
		// Fire weapon slot 0 with no hit (basic implementation)
		_simulatorController.FireWeapon(0, null, null);
	}

	/// <summary>
	/// Releases the trigger for the primary weapon slot.
	/// Required for semi-auto weapons to allow firing again.
	/// </summary>
	private void ReleaseWeaponTrigger()
	{
		_simulatorController.ReleaseWeaponTrigger(0);
	}

	/// <summary>
	/// Switches to a different weapon in the primary slot (slot 0).
	/// Based on WL_AGENT.C weapon selection (bt_readyknife, bt_readypistol, etc.)
	/// </summary>
	/// <param name="weaponType">Weapon type identifier (e.g., "knife", "pistol", "machinegun", "chaingun")</param>
	private void SwitchWeapon(string weaponType)
	{
		_simulatorController.SwitchWeapon(0, weaponType);
	}

	/// <summary>
	/// Finds and operates the door or pushwall the player is facing.
	/// Projects 1 WallWidth forward from camera position/rotation and checks that tile.
	/// </summary>
	private void OperateDoorOrPushWallPlayerIsFacing()
	{
		// Get camera's position and Y rotation from display mode
		Vector3 cameraPos = _displayMode.ViewerPosition;
		float rotationY = _displayMode.ViewerYRotation;

		// Project forward 1.5 tiles to reliably detect doors/pushwalls in front
		// In Godot: Y rotation of 0 = facing -Z (North), rotating clockwise
		float forwardX = Mathf.Sin(rotationY),
			forwardZ = -Mathf.Cos(rotationY);
		Vector3 forwardPoint = cameraPos + new Vector3(
			forwardX * -Constants.TileWidth,
			0f,
			forwardZ * Constants.TileWidth);

		// Convert world position to tile coordinates
		ushort tileX = (ushort)forwardPoint.X.ToTile(),
			tileY = (ushort)forwardPoint.Z.ToTile();

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
			Door door = _simulatorController.Doors[i];
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
			PushWall pushWall = _simulatorController.PushWalls[i];
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
		Color floorColor = level.Floor is byte floor ? palette[floor].ToColor() : new Color(0.33f, 0.33f, 0.33f), // Default: dark gray
			ceilingColor = level.Ceiling is byte ceiling ? palette[ceiling].ToColor() : new Color(0.2f, 0.2f, 0.2f); // Default: darker gray
		// Create world environment with divided skybox
		WorldEnvironment worldEnvironment = new()
		{
			Environment = new()
			{
				Sky = new()
				{
					SkyMaterial = _skyMaterial,
				},
				BackgroundMode = Godot.Environment.BGMode.Sky,
			}
		};
		AddChild(worldEnvironment);

		// Set shader parameters for floor and ceiling
		_skyMaterial.SetShaderParameter("floor_color", floorColor);
		_skyMaterial.SetShaderParameter("ceiling_color", ceilingColor);
	}

	public override void _Process(double delta)
	{
		// Fixtures updates billboard rotations automatically in its own _Process
		// Bonuses updates billboard rotations automatically in its own _Process
		// Doors use two-quad approach with back-face culling - no per-frame updates needed
		// SimulatorController drives the simulator and updates door/bonus states automatically in its own _Process
	}
}
