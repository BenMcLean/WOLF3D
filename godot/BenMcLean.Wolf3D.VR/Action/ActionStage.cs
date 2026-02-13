using System;
using System.Collections.Generic;
using BenMcLean.Wolf3D.Assets.Gameplay;
using BenMcLean.Wolf3D.Assets.Graphics;
using BenMcLean.Wolf3D.Shared;
using BenMcLean.Wolf3D.Shared.StatusBar;
using BenMcLean.Wolf3D.Simulator;
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
	/// <summary>
	/// Represents a pending level transition requested by elevator activation.
	/// Root polls this to initiate a fade transition.
	/// </summary>
	public class LevelTransitionRequest
	{
		public int LevelIndex { get; }
		public Dictionary<string, int> SavedInventory { get; }
		public string SavedWeaponType { get; }

		public LevelTransitionRequest(int levelIndex, Dictionary<string, int> savedInventory, string savedWeaponType)
		{
			LevelIndex = levelIndex;
			SavedInventory = savedInventory;
			SavedWeaponType = savedWeaponType;
		}
	}

	/// <summary>
	/// Set when an elevator is activated. Root polls this to initiate a fade transition.
	/// </summary>
	public LevelTransitionRequest PendingTransition { get; private set; }

	/// <summary>
	/// Set when the player presses ESC. Root polls this to transition back to the main menu.
	/// </summary>
	public bool PendingReturnToMenu { get; private set; }

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
	private readonly Dictionary<string, int> _savedInventory;
	private readonly string _savedWeaponType;
	private readonly Simulator.Simulator _existingSimulator;
	private readonly Vector3? _resumePosition;
	private readonly float? _resumeYRotation;
	private Walls _walls;
	private DebugMarkers _debugMarkers;
	private Fixtures _fixtures;
	private Bonuses _bonuses;
	private Actors _actors;
	private Doors _doors;
	private Weapons _weapons;
	private SimulatorController _simulatorController;
	private ScreenFlashOverlay _screenFlashOverlay;
	private PixelPerfectAiming _pixelPerfectAiming;
	private AimIndicator _aimIndicator;
	private StatusBarState _statusBarState;
	private StatusBarRenderer _statusBarRenderer;
	private CanvasLayer _statusBarCanvas;

	/// <summary>
	/// Creates a new ActionStage with the specified display mode.
	/// </summary>
	/// <param name="displayMode">The active display mode (VR or flatscreen).</param>
	/// <param name="savedInventory">Optional saved inventory from level transition (null for new game).</param>
	/// <param name="savedWeaponType">Optional saved weapon type from level transition (null for new game).</param>
	public ActionStage(IDisplayMode displayMode, Dictionary<string, int> savedInventory = null, string savedWeaponType = null)
	{
		_displayMode = displayMode ?? throw new ArgumentNullException(nameof(displayMode));
		_savedInventory = savedInventory;
		_savedWeaponType = savedWeaponType;
	}

	/// <summary>
	/// Creates a new ActionStage that resumes from an existing simulator state.
	/// Used when returning from the menu to a suspended game.
	/// </summary>
	/// <param name="displayMode">The active display mode (VR or flatscreen).</param>
	/// <param name="existingSimulator">The existing simulator with preserved game state.</param>
	/// <param name="levelIndex">The level index to display.</param>
	/// <param name="resumePosition">Player position to restore.</param>
	/// <param name="resumeYRotation">Player Y rotation to restore.</param>
	public ActionStage(IDisplayMode displayMode, Simulator.Simulator existingSimulator, int levelIndex, Vector3 resumePosition, float resumeYRotation)
	{
		_displayMode = displayMode ?? throw new ArgumentNullException(nameof(displayMode));
		_existingSimulator = existingSimulator ?? throw new ArgumentNullException(nameof(existingSimulator));
		LevelIndex = levelIndex;
		_resumePosition = resumePosition;
		_resumeYRotation = resumeYRotation;
	}

	public override void _Ready()
	{
		try
		{
			// Get current level analysis
			MapAnalysis = Shared.SharedAssetManager.CurrentGame.MapAnalyses[LevelIndex];

		// Play level music
		if (!string.IsNullOrWhiteSpace(MapAnalysis.Music))
			Shared.EventBus.Emit(Shared.GameEvent.PlayMusic, MapAnalysis.Music);

		// Setup sky with floor/ceiling colors from map
		SetupSky(MapAnalysis);

		// Initialize display mode camera rig
		_displayMode.Initialize(this);

		// Position camera at player start or resume position
		Vector3 cameraPosition;
		float cameraRotationY = 0f;
		if (_resumePosition.HasValue)
		{
			// Resuming from suspended game - restore exact position/rotation
			cameraPosition = _resumePosition.Value;
			cameraRotationY = _resumeYRotation ?? 0f;
		}
		else if (MapAnalysis.PlayerStart.HasValue)
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
			Shared.SharedAssetManager.DigiSounds,      // Digi sounds for pickup sounds
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

		// Create simulator controller
		_simulatorController = new SimulatorController();
		AddChild(_simulatorController);

		if (_existingSimulator != null)
		{
			// Resuming from suspended game - reuse existing simulator
			_simulatorController.InitializeFromExisting(
				_existingSimulator,
				_doors,
				_walls,
				_bonuses,
				_actors,
				_weapons,
				() => (_displayMode.ViewerPosition.X.ToFixedPoint(), _displayMode.ViewerPosition.Z.ToFixedPoint()));
		}
		else
		{
			// New game or level transition - create fresh simulator
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
				() => (_displayMode.ViewerPosition.X.ToFixedPoint(), _displayMode.ViewerPosition.Z.ToFixedPoint()));
		}

		// Wire up hit detection callback for weapon state machine
		// WL_AGENT.C:GunAttack is called on the fire frame - this provides the raycast
		_simulatorController.Simulator.HitDetection = PerformWeaponRaycast;

		// Wire up movement validation for collision detection
		// This enables wall collision and wall-sliding behavior
		_displayMode.SetMovementValidator(_simulatorController.ValidateMovement);

		// Create screen flash overlay (palette-shift effects: damage flash, bonus flash, fades)
		// Uses CanvasLayer so it covers everything on screen regardless of 3D depth
		_screenFlashOverlay = new ScreenFlashOverlay();
		AddChild(_screenFlashOverlay);
		_screenFlashOverlay.Subscribe(_simulatorController);

		// Subscribe to elevator activation for level transitions
		_simulatorController.ElevatorActivated += OnElevatorActivated;

		// Initialize inventory (skip for resumed games - simulator already has state)
		if (_existingSimulator == null && SharedAssetManager.StatusBar != null)
		{
			_simulatorController.Simulator.Inventory.InitializeFromDefinition(SharedAssetManager.StatusBar);

			// If this is a level transition (saved state exists), restore player state
			if (_savedInventory != null)
			{
				// Restore inventory values (ammo, health, score, lives, weapons)
				_simulatorController.Simulator.Inventory.RestoreState(_savedInventory);
				// Reset only level-specific values (keys)
				_simulatorController.Simulator.Inventory.OnLevelChange();
			}

			// Equip starting weapon from inventory (SelectedWeapon0 is set by XML Init or restored state)
			// Must happen after inventory initialization so SelectedWeapon0 has its value
			WeaponCollection weaponCollection = _simulatorController.Simulator.WeaponCollection;
			if (weaponCollection != null)
			{
				int startingWeaponNumber = _simulatorController.Simulator.Inventory.GetValue("SelectedWeapon0");
				if (weaponCollection.TryGetWeaponByNumber(startingWeaponNumber, out WeaponInfo startWeapon))
					_simulatorController.Simulator.EquipWeapon(0, startWeapon.Name);
			}
		}

		// Subscribe to display mode button events for shooting and using objects
		// Left click (flatscreen) or right trigger (VR) = shoot
		_displayMode.PrimaryButtonPressed += OnPrimaryButtonPressed;
		_displayMode.PrimaryButtonReleased += OnPrimaryButtonReleased;
		// Right click (flatscreen) or left grip (VR) = use/push
		_displayMode.SecondaryButtonPressed += OnSecondaryButtonPressed;

		// Capture mouse for FPS controls in flatscreen mode
		if (!_displayMode.IsVRActive)
			Input.MouseMode = Input.MouseModeEnum.Captured;

		// Create pixel-perfect aiming system
		_pixelPerfectAiming = new PixelPerfectAiming(this);

		// Create debug aim indicator (temporary - won't be in final game)
		_aimIndicator = new AimIndicator(_pixelPerfectAiming, _displayMode.Camera);
		AddChild(_aimIndicator);
		// Create status bar for flatscreen mode
		if (!_displayMode.IsVRActive && SharedAssetManager.StatusBar != null)
		{
			_statusBarState = new StatusBarState(SharedAssetManager.StatusBar);
			_statusBarRenderer = new StatusBarRenderer(_statusBarState);

			// Subscribe status bar directly to Inventory for automatic updates
			_statusBarState.SubscribeToInventory(_simulatorController.Simulator.Inventory);

			// Sync status bar with current inventory values (important for level transitions
			// where inventory was restored before status bar was created)
			// CurrentWeapon is derived from StatusBarWeapon in the inventory values
			_statusBarState.SyncFromSimulator(
				_simulatorController.Simulator.Inventory.Values);

			// Create CanvasLayer to display status bar at bottom of screen
			_statusBarCanvas = new CanvasLayer
			{
				Name = "StatusBarCanvas",
				Layer = 10 // On top of game view
			};
			AddChild(_statusBarCanvas);

			// Add the status bar viewport as a child so it processes
			_statusBarCanvas.AddChild(_statusBarRenderer.Viewport);

			// Create TextureRect to display the status bar viewport texture
			TextureRect statusBarDisplay = new()
			{
				Name = "StatusBarDisplay",
				Texture = _statusBarRenderer.ViewportTexture,
				// Anchor to bottom of screen, centered horizontally
				AnchorLeft = 0.5f,
				AnchorRight = 0.5f,
				AnchorTop = 1.0f,
				AnchorBottom = 1.0f,
				// Scale 3x for visibility (320x40 -> 960x120)
				CustomMinimumSize = new Vector2(960, 120),
				Size = new Vector2(960, 120),
				// Center horizontally, position at bottom
				OffsetLeft = -480,
				OffsetRight = 480,
				OffsetTop = -120,
				OffsetBottom = 0,
				TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			};
			_statusBarCanvas.AddChild(statusBarDisplay);

			// Set floor number (not part of Inventory as it's level metadata, not player state)
			_statusBarState.SetValue("Floor", LevelIndex + 1);
		}
		}
		catch (Exception ex)
		{
			ExceptionHandler.HandleException(ex);
		}
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventKey keyEvent && keyEvent.Pressed)
		{
			// ESC returns to main menu
			if (keyEvent.Keycode == Key.Escape)
			{
				PendingReturnToMenu = true;
				return;
			}

			// Weapon switching - number keys map to weapon numbers from XML
			// Based on WL_AGENT.C weapon selection (bt_readyknife, bt_readypistol, etc.)
			int weaponNumber = keyEvent.Keycode switch
			{
				Key.Key1 => 0,
				Key.Key2 => 1,
				Key.Key3 => 2,
				Key.Key4 => 3,
				_ => -1
			};
			if (weaponNumber >= 0
				&& _simulatorController.Simulator?.WeaponCollection != null
				&& _simulatorController.Simulator.WeaponCollection.TryGetWeaponByNumber(weaponNumber, out WeaponInfo weaponInfo))
			{
				SwitchWeapon(weaponInfo.Name);
			}
		}
	}

	/// <summary>
	/// Handles primary button press (left click in flatscreen, right trigger in VR).
	/// Fires the equipped weapon.
	/// </summary>
	private void OnPrimaryButtonPressed(string buttonName)
	{
		if (buttonName == "trigger_click")
			FireWeapon();
	}

	/// <summary>
	/// Handles primary button release (left click release in flatscreen, trigger release in VR).
	/// Required for semi-auto weapons to fire again.
	/// </summary>
	private void OnPrimaryButtonReleased(string buttonName)
	{
		if (buttonName == "trigger_click")
			ReleaseWeaponTrigger();
	}

	/// <summary>
	/// Handles secondary button press (right click in flatscreen, left grip in VR).
	/// Uses doors, pushwalls, or elevators.
	/// </summary>
	private void OnSecondaryButtonPressed(string buttonName)
	{
		// Accept either grip_click (flatscreen right-click) or any secondary button
		UseObjectPlayerIsFacing();
	}

	/// <summary>
	/// Fires the weapon in the primary slot (slot 0).
	/// Signals the simulator that the trigger was pulled.
	/// Hit detection is handled by the weapon state machine via HitDetection callback.
	/// </summary>
	private void FireWeapon()
	{
		_simulatorController.FireWeapon(0);
	}

	/// <summary>
	/// Performs a raycast for weapon hit detection.
	/// Called by the simulator's weapon state machine on fire frames (A_PistolAttack, etc.).
	/// Based on WL_AGENT.C:GunAttack performing raycast on each fire frame in T_Attack.
	/// </summary>
	/// <param name="slotIndex">Weapon slot index</param>
	/// <returns>Hit actor index, or null if miss</returns>
	private int? PerformWeaponRaycast(int slotIndex)
	{
		Vector3 rayOrigin = _displayMode.Camera.GlobalPosition;
		Vector3 rayDirection = -_displayMode.Camera.GlobalTransform.Basis.Z;
		Vector3 cameraForward = rayDirection;

		PixelPerfectAiming.AimHitResult hitResult = _pixelPerfectAiming.Raycast(rayOrigin, rayDirection, cameraForward);

		return (hitResult.IsHit && hitResult.Type == PixelPerfectAiming.HitType.Actor)
			? hitResult.ActorIndex
			: null;
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
	/// Finds and operates the door, pushwall, or elevator the player is facing.
	/// Projects 1 WallWidth forward from camera position/rotation and checks that tile.
	/// WL_AGENT.C:Cmd_Use - checks for doors, pushwalls, and elevators.
	/// </summary>
	private void UseObjectPlayerIsFacing()
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

		// Determine facing direction from camera rotation
		Direction dir = rotationY.ToCardinalDirection();

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
			_simulatorController.ActivatePushWall(tileX, tileY, dir);
			return;
		}

		// Try elevator third
		if (IsElevatorAtTile(tileX, tileY))
			_simulatorController.ActivateElevator(tileX, tileY, dir);
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
	/// Checks if an elevator switch exists at the specified tile coordinates.
	/// </summary>
	private bool IsElevatorAtTile(ushort tileX, ushort tileY)
	{
		foreach (var elev in MapAnalysis.Elevators)
		{
			if (elev.X == tileX && elev.Y == tileY)
				return true;
		}
		return false;
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

	public override void _ExitTree()
	{
		// Unsubscribe from display mode events to prevent ObjectDisposedException
		// when old ActionStage is freed during level transitions
		_displayMode.PrimaryButtonPressed -= OnPrimaryButtonPressed;
		_displayMode.PrimaryButtonReleased -= OnPrimaryButtonReleased;
		_displayMode.SecondaryButtonPressed -= OnSecondaryButtonPressed;

		// Unsubscribe from simulator events
		if (_simulatorController != null)
			_simulatorController.ElevatorActivated -= OnElevatorActivated;

		// Clear HitDetection delegate (points at this ActionStage's method)
		if (_simulatorController?.Simulator != null)
			_simulatorController.Simulator.HitDetection = null;

		// Unsubscribe status bar from inventory to prevent dangling references
		_statusBarState?.UnsubscribeFromInventory();

		// Release mouse when leaving action stage (returning to menu, etc.)
		if (!_displayMode.IsVRActive)
			Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	/// <summary>
	/// Handles elevator activation - captures player state and sets PendingTransition
	/// for Root to poll and initiate a fade transition.
	/// WL_AGENT.C:Cmd_Use elevator completion triggers gamestate.victoryflag.
	/// </summary>
	private void OnElevatorActivated(ElevatorActivatedEvent e)
	{
		// Play elevator sound
		if (!string.IsNullOrEmpty(e.SoundName))
			EventBus.Emit(GameEvent.PlaySound, e.SoundName);

		// Capture player inventory state before transition
		Dictionary<string, int> savedInventory = _simulatorController?.Simulator?.Inventory?.CaptureState();
		string savedWeaponType = _simulatorController?.Simulator?.GetEquippedWeaponType(0);

		PendingTransition = new LevelTransitionRequest(e.DestinationLevel, savedInventory, savedWeaponType);
	}
}
