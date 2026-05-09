using System;
using System.Collections.Generic;
using System.Linq;
using BenMcLean.Wolf3D.Assets.Gameplay;
using BenMcLean.Wolf3D.Assets.Menu;
using BenMcLean.Wolf3D.Shared;
using BenMcLean.Wolf3D.Shared.Automap;
using BenMcLean.Wolf3D.Shared.StatusBar;
using BenMcLean.Wolf3D.Simulator;
using BenMcLean.Wolf3D.Simulator.Entities;
using BenMcLean.Wolf3D.Simulator.Snapshots;
using BenMcLean.Wolf3D.VR.ActionStage;
using BenMcLean.Wolf3D.VR.VR;
using Godot;

namespace BenMcLean.Wolf3D.VR;

/// <summary>
/// The VR action stage - loads and displays a Wolfenstein 3D level in VR.
/// Handles camera, walls, doors, sprites, and gameplay simulation.
/// Supports both VR and flatscreen display modes.
/// </summary>
public partial class ActionRoom : Node3D, IRoom
{
	public bool SkipFade => false;

	/// <summary>
	/// Represents a pending level transition requested by elevator activation.
	/// Root polls this to initiate a fade transition.
	/// </summary>
	public class LevelTransitionRequest(
		int levelIndex,
		InventorySnapshot savedInventory,
		LevelCompletionStats completionStats = null,
		string menuName = "LevelComplete",
		IReadOnlyList<LevelCompletionStats> allLevelStats = null,
		byte? floorColor = null,
		byte? ceilingColor = null,
		ushort? floorTilePage = null,
		ushort? ceilingTilePage = null,
		IReadOnlyList<ushort?> equippedWeaponShapes = null,
		bool showIntermission = true,
		int? playerXOverride = null,
		int? playerYOverride = null,
		short? playerAngleOverride = null)
	{
		public int LevelIndex { get; } = levelIndex;
		public InventorySnapshot SavedInventory { get; } = savedInventory;
		public LevelCompletionStats CompletionStats { get; } = completionStats;
		// MapAnalysis.Floor/Ceiling palette indices and optional VSwap tile pages for the previous level.
		// Used by MenuRoom to recreate the elevator environment on LevelComplete/Victory screens in VR.
		public byte? FloorColor { get; } = floorColor;
		public byte? CeilingColor { get; } = ceilingColor;
		public ushort? FloorTilePage { get; } = floorTilePage;
		public ushort? CeilingTilePage { get; } = ceilingTilePage;
		// VSwap page numbers for each weapon slot at transition time.
		// Used by MenuRoom elevator environment to show the same weapons the player had equipped.
		public IReadOnlyList<ushort?> EquippedWeaponShapes { get; } = equippedWeaponShapes;
		public bool ShowIntermission { get; } = showIntermission;
		public int? PlayerXOverride { get; } = playerXOverride;
		public int? PlayerYOverride { get; } = playerYOverride;
		public short? PlayerAngleOverride { get; } = playerAngleOverride;

		/// <summary>
		/// Menu to show for this transition. Defaults to "LevelComplete" for elevator transitions.
		/// Set to "Victory" by VictoryTile, or any other menu name by item scripts.
		/// </summary>
		public string MenuName { get; } = menuName;

		/// <summary>
		/// Accumulated stats from all completed levels in this episode.
		/// Read from Simulator.LevelRatios at transition time.
		/// Used by Victory screen to display averaged stats.
		/// </summary>
		public IReadOnlyList<LevelCompletionStats> AllLevelStats { get; } = allLevelStats ?? [];
	}

	/// <summary>
	/// Set when an elevator is activated. Root polls this to initiate a fade transition.
	/// </summary>
	public LevelTransitionRequest PendingTransition { get; private set; }

	/// <summary>
	/// Set when the player presses ESC. Root polls this to transition back to the main menu.
	/// </summary>
	public bool PendingReturnToMenu { get; private set; }

	/// <summary>
	/// When set, overrides the default pause menu with a specific menu name.
	/// Used by NavigateToMenu events from Lua (e.g., DeathCam_Hans).
	/// </summary>
	public string PendingMenuOverride { get; private set; }

	/// <summary>
	/// Equipped weapon shapes to preserve when transitioning to a menu with KeepWeapons=true.
	/// </summary>
	public IReadOnlyList<ushort?> PendingEquippedWeaponShapes { get; private set; }

	/// <summary>
	/// Completion stats captured at the time of NavigateToMenu (e.g., Victory screen).
	/// Null when navigating to menus that don't need stats (e.g., DeathCam_Hans, pause menu).
	/// </summary>
	public LevelCompletionStats PendingCompletionStats { get; private set; }

	/// <summary>
	/// Accumulated level stats captured at NavigateToMenu time (for Victory screen total).
	/// </summary>
	public IReadOnlyList<LevelCompletionStats> PendingAllLevelStats { get; private set; }

	/// <summary>
	/// Set when NavigateToMenu is called from Lua (e.g., elevator switch, N3D stairs).
	/// Captures the level transition data so MenuRoom can trigger it after ContinueToNextLevel().
	/// Null when navigating to menus that don't complete a level (pause, death cam, etc.).
	/// </summary>
	public LevelTransitionRequest PendingLevelTransitionForMenu { get; private set; }

	/// <summary>
	/// Pending quiz payload captured at NavigateToMenu time for the Quiz menu.
	/// </summary>
	public PendingQuizData PendingQuiz { get; private set; }

	/// <summary>
	/// Set when the player dies. Root polls this to initiate the death fadeout.
	/// OnDeath script runs after fadeout completes (while screen is black)
	/// to determine restart vs game over.
	/// WL_GAME.C:Died() → fade out → reset inventory → restart or game over.
	/// </summary>
	public bool PendingDeathFadeOut { get; private set; }

	/// <summary>
	/// The level index for this stage, set once at construction time.
	/// After simulator initialization, this is also stored as "MapOn" in the inventory.
	/// WL_DEF.H:gamestate.mapon
	/// </summary>
	private readonly int _initialLevelIndex;
	private static bool IsSpearCampaign() =>
		SharedAssetManager.CurrentGame?.XML?.Attribute("Path")?.Value is "M1" or "M2" or "M3";
	private static bool UsesSpearSpecialIntermission(LevelCompletionStats stats) =>
		IsSpearCampaign()
		&& stats is not null
		&& stats.FloorNumber is 5 or 10 or 16 or 18 or 19 or 20;
	private static string GetLevelCompleteMenuName(LevelCompletionStats stats) =>
		UsesSpearSpecialIntermission(stats) ? "LevelCompleteSpecial" : "LevelComplete";

	// Public accessors for level components - used by systems like PixelPerfectAiming
	public MapAnalyzer.MapAnalysis MapAnalysis { get; private set; }
	public Walls Walls => _walls;
	public Doors Doors => _doors;
	public SimulatorController SimulatorController => _simulatorController;
	public Actors Actors => _actors;
	public Projectiles Projectiles => _projectiles;
	public Fixtures Fixtures => _fixtures;
	public Bonuses Bonuses => _bonuses;
	public static IReadOnlyDictionary<ushort, StandardMaterial3D> SpriteMaterials => VRAssetManager.SpriteMaterials;
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
	private readonly int _difficulty;
	private readonly bool _debugMarkersEnabled;
	private readonly bool _cheatModeEnabled;
	private readonly InventorySnapshot _savedInventory;
	private readonly IReadOnlyList<LevelCompletionStats> _savedLevelStats;
	private readonly int? _playerXOverride;
	private readonly int? _playerYOverride;
	private readonly short? _playerAngleOverride;
	private readonly Simulator.Simulator _existingSimulator;
	private readonly SimulatorSnapshot _loadSnapshot;
	private Walls _walls;
	private DebugMarkers _debugMarkers;
	private Fixtures _fixtures;
	private Bonuses _bonuses;
	private Actors _actors;
	private Projectiles _projectiles;
	private Doors _doors;
	private Weapons _weapons;
	private SimulatorController _simulatorController;
	private ScreenFlashOverlay _screenFlashOverlay;
	private DeathFizzleOverlay _deathFizzleOverlay;
	private PixelPerfectAiming _pixelPerfectAiming;
	private AimIndicator _aimIndicator;
	private TeleportationOverlay _teleportOverlay;
	private readonly StatusBarController _statusBarController;
	private StatusBarRenderer _statusBarRenderer;
	private CanvasLayer _statusBarCanvas;
	private AutomapController _automapController;
	private WristwatchDisplay _wristwatchDisplay;
	private bool _rightAxPressed;
	private bool _rightByPressed;
	private bool _leftAxPressed;
	private bool _leftByPressed;
	private int _rightPendingCycleDirection;
	private int _leftPendingCycleDirection;
	private bool _rightComboTriggered;
	private bool _leftComboTriggered;

	/// <summary>
	/// Creates a new ActionStage with the specified display mode.
	/// </summary>
	/// <param name="displayMode">The active display mode (VR or flatscreen).</param>
	/// <param name="difficulty">Difficulty level (0-3). Default is 2 ("Bring 'em on!").</param>
	/// <param name="savedInventory">Optional saved inventory from level transition (null for new game).</param>
	/// <param name="savedLevelStats">Optional accumulated level stats from previous levels (null for new game).</param>
	public ActionRoom(IDisplayMode displayMode, int levelIndex = 0, int difficulty = 2, InventorySnapshot savedInventory = null, IReadOnlyList<LevelCompletionStats> savedLevelStats = null, bool debugMarkersEnabled = false, bool cheatModeEnabled = false, int? playerXOverride = null, int? playerYOverride = null, short? playerAngleOverride = null, StatusBarController statusBarController = null)
	{
		_displayMode = displayMode ?? throw new ArgumentNullException(nameof(displayMode));
		_initialLevelIndex = levelIndex;
		_difficulty = savedInventory?.Values is not null && savedInventory.Values.TryGetValue("Difficulty", out int savedDifficulty)
			? savedDifficulty
			: difficulty;
		_debugMarkersEnabled = debugMarkersEnabled;
		_cheatModeEnabled = cheatModeEnabled;
		_savedInventory = savedInventory;
		_savedLevelStats = savedLevelStats;
		_playerXOverride = playerXOverride;
		_playerYOverride = playerYOverride;
		_playerAngleOverride = playerAngleOverride;
		_statusBarController = statusBarController;
	}

	/// <summary>
	/// Creates a new ActionStage that resumes from an existing simulator state.
	/// Used when returning from the menu to a suspended game.
	/// Player position and angle are restored from the Simulator's PlayerX/PlayerY/PlayerAngle.
	/// </summary>
	/// <param name="displayMode">The active display mode (VR or flatscreen).</param>
	/// <param name="existingSimulator">The existing simulator with preserved game state.</param>
	public ActionRoom(IDisplayMode displayMode, Simulator.Simulator existingSimulator, bool debugMarkersEnabled = false, bool cheatModeEnabled = false, StatusBarController statusBarController = null)
	{
		_displayMode = displayMode ?? throw new ArgumentNullException(nameof(displayMode));
		_existingSimulator = existingSimulator ?? throw new ArgumentNullException(nameof(existingSimulator));
		_initialLevelIndex = existingSimulator.Inventory.GetValue("MapOn");
		_debugMarkersEnabled = debugMarkersEnabled;
		_cheatModeEnabled = cheatModeEnabled;
		_statusBarController = statusBarController;
	}

	/// <summary>
	/// Creates a new ActionStage from a saved game snapshot.
	/// Creates a fresh simulator, loads the level, then applies the saved state via LoadState.
	/// Used when loading a saved game from the menu.
	/// </summary>
	/// <param name="displayMode">The active display mode (VR or flatscreen).</param>
	/// <param name="snapshot">The saved simulator state to restore.</param>
	public ActionRoom(IDisplayMode displayMode, SimulatorSnapshot snapshot, bool debugMarkersEnabled = false, bool cheatModeEnabled = false, StatusBarController statusBarController = null)
	{
		_displayMode = displayMode ?? throw new ArgumentNullException(nameof(displayMode));
		_loadSnapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
		_debugMarkersEnabled = debugMarkersEnabled;
		_cheatModeEnabled = cheatModeEnabled;
		// Level index and difficulty are stored in the snapshot's inventory
		_initialLevelIndex = snapshot.InventoryValues?.Values is not null
			&& snapshot.InventoryValues.Values.TryGetValue("MapOn", out int mapOn) ? mapOn : 0;
		_difficulty = snapshot.InventoryValues?.Values is not null
			&& snapshot.InventoryValues.Values.TryGetValue("Difficulty", out int d) ? d : 2;
		_statusBarController = statusBarController;
	}

	public override void _Ready()
	{
		try
		{
			// Get current level analysis
			MapAnalysis = Shared.SharedAssetManager.CurrentGame.MapAnalyses[_initialLevelIndex];

			// Play level music
			if (!string.IsNullOrWhiteSpace(MapAnalysis.Music))
				Shared.EventBus.Emit(Shared.GameEvent.PlayMusic, MapAnalysis.Music);

			// Setup sky with floor/ceiling colors from map
			SetupSky(MapAnalysis);

			// Initialize display mode camera rig
			_displayMode.Initialize(this);
			_displayMode.LocomotionEnabled = true;

			// Camera positioning is deferred until after simulator initialization
			// so that LoadState/resume can override the spawn position

			// Create walls for the current level and add to scene
			_walls = new Walls(
				VRAssetManager.OpaqueMaterials,
				MapAnalysis,
				Shared.SharedAssetManager.DigiSounds);  // Sound library for pushwall sounds
			AddChild(_walls);

			// Create debug markers for patrol points and ambush actors when enabled.
			if (_debugMarkersEnabled)
			{
				_debugMarkers = new DebugMarkers(MapAnalysis);
				AddChild(_debugMarkers);
			}

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
				() => _displayMode.ViewerYRotation,        // Camera Y rotation for billboard effect
				Shared.SharedAssetManager.CurrentGame?.VSwap?.SpritesByName);
			AddChild(_actors);

			// Create projectiles (in-flight rockets, needles, fireballs, etc.) for the current level
			_projectiles = new Projectiles(
				VRAssetManager.SpriteMaterials,
				() => _displayMode.ViewerPosition,         // Viewer position for directional sprites
				() => _displayMode.ViewerYRotation);       // Camera Y rotation for billboard effect
			AddChild(_projectiles);

			// Create weapons: flatscreen shows a camera-attached sprite; VR uses WeaponHandMesh on controllers.
			// Weapons still subscribes for WeaponFired sound in both modes.
			// In flatscreen, the visual is attached later to the status bar HUD.
			_weapons = new Weapons(VRAssetManager.SpriteTextures);
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

			if (_existingSimulator is not null)
			{
				// Resuming from suspended game - reuse existing simulator
				_simulatorController.InitializeFromExisting(
					_existingSimulator,
					_doors,
					_walls,
					_bonuses,
					_actors,
					_weapons,
					_projectiles,
					() => (_displayMode.ViewerPosition.X.ToFixedPoint(), _displayMode.ViewerPosition.Z.ToFixedPoint(), _displayMode.ViewerYRotation.ToWolf3DAngle()));
			}
			else
			{
				// New game, level transition, or loading saved game - create fresh simulator
				_simulatorController.Initialize(
					Shared.SharedAssetManager.CurrentGame.MapAnalyzer,
					MapAnalysis,
					_doors,
					_walls,
					_bonuses,
					_actors,
					_weapons,
					_projectiles,
					Shared.SharedAssetManager.CurrentGame.StateCollection,
					Shared.SharedAssetManager.CurrentGame.WeaponCollection,
					() => (_displayMode.ViewerPosition.X.ToFixedPoint(), _displayMode.ViewerPosition.Z.ToFixedPoint(), _displayMode.ViewerYRotation.ToWolf3DAngle()),
					SharedAssetManager.StatusBar,
					_difficulty,
					_savedInventory,
					_savedLevelStats,
					_loadSnapshot);
			}

			if (_simulatorController.Simulator is not null)
			{
				_simulatorController.Simulator.QuizQuestions =
					SharedAssetManager.CurrentGame?.VgaGraph?.Questions;
				if (_existingSimulator is null)
					_simulatorController.Simulator.CurrentQuestionNum =
						SharedAssetManager.Config?.QuestionNum ?? 0;
			}

			// Position camera: use restored state (load/resume) or map spawn point
			Vector3 cameraPosition;
			float cameraRotationY = 0f;
			if (_existingSimulator is not null || _loadSnapshot is not null)
			{
				// Restore position/angle from simulator (which has been loaded/resumed)
				cameraPosition = new Vector3(
					_simulatorController.Simulator.PlayerX.ToMeters(),
					Constants.HalfTileHeight,
					_simulatorController.Simulator.PlayerY.ToMeters());
				cameraRotationY = _simulatorController.Simulator.PlayerAngle.ToGodotYRotation();
			}
			else if (_playerXOverride.HasValue && _playerYOverride.HasValue && _playerAngleOverride.HasValue)
			{
				cameraPosition = new Vector3(
					_playerXOverride.Value.ToMeters(),
					Constants.HalfTileHeight,
					_playerYOverride.Value.ToMeters());
				cameraRotationY = _playerAngleOverride.Value.ToGodotYRotation();
				_simulatorController.Simulator.PlacePlayer(
					_playerXOverride.Value,
					_playerYOverride.Value,
					_playerAngleOverride.Value);
			}
			else if (MapAnalysis.PlayerStart.HasValue)
			{
				MapAnalyzer.MapAnalysis.PlayerSpawn playerStart = MapAnalysis.PlayerStart.Value;
				// Center of the player's starting grid square
				cameraPosition = new Vector3(
					playerStart.X.ToMetersCentered(),
					Constants.HalfTileHeight,
					playerStart.Y.ToMetersCentered());
				// Convert Direction enum to Godot rotation using ToAngle extension method
				// Handles Wolf3D coordinate system → Godot coordinate system conversion
				cameraRotationY = playerStart.Facing.ToAngle();
				// Pre-warm the simulator's player position so the first Update() call sees no
				// positional delta and doesn't sweep the path from (0,0) to the spawn point.
				// PlacePlayer is the right call here: this is system-driven placement, not travel.
				_simulatorController.Simulator.PlacePlayer(
					playerStart.X.ToMetersCentered().ToFixedPoint(),
					playerStart.Y.ToMetersCentered().ToFixedPoint(),
					cameraRotationY.ToWolf3DAngle());
			}
			else
			{
				// Fallback to origin if no player start found
				cameraPosition = new Vector3(0, Constants.HalfTileHeight, 0);
				GD.PrintErr("Warning: No player start found in map!");
			}

			// Position the display mode's origin (XROrigin or CameraHolder) at player start.
			// VR: Y is set to 0 (floor level): in 5DOF mode Update() locks camera to HalfTileHeight
			// each frame; in roomscale mode the origin stays at floor level by design.
			// Flatscreen: Y is set to cameraPosition.Y (HalfTileHeight) because CameraHolder IS
			// the camera's parent and there is no HMD offset to provide vertical elevation.
			if (_displayMode.Origin is not null)
			{
				float originY = _displayMode.IsVRActive ? 0f : cameraPosition.Y;
				_displayMode.Origin.Position = new Vector3(cameraPosition.X, originY, cameraPosition.Z);
				_displayMode.Origin.RotationDegrees = new Vector3(0, Mathf.RadToDeg(cameraRotationY), 0);
			}
			// In VR, compensate for the physical HMD Y rotation so the camera faces the correct
			// spawn direction regardless of which way the player is physically facing.
			// ResetPositionFacing subtracts camera.Rotation.Y from the desired yaw, matching
			// the same compensation used when entering the MenuRoom.
			// In flatscreen mode this is a no-op.
			// Panel direction vector for cameraRotationY: (sin θ, 0, -cos θ)
			Vector3 spawnXZ = new(cameraPosition.X, 0f, cameraPosition.Z);
			_displayMode.ResetPositionFacing(
				spawnXZ + new Vector3(Mathf.Sin(cameraRotationY), 0f, -Mathf.Cos(cameraRotationY)),
				spawnXZ);

			// Attach weapon mesh to each VR controller so the weapon renders at the hand position.
			// Uses VoxelWeapon (3D grip-pose) when VoxelAtlas is available, WeaponHandMesh (sprite aim-pose) otherwise.
			// Hand 0 = right controller (slot 0), hand 1 = left controller (slot 1).
			if (_displayMode.IsVRActive)
				for (int hand = 0; hand <= 1; hand++)
					if (_displayMode.GetHandNode(hand) is Node3D aimNode)
						if (VRAssetManager.VoxelAtlas is not null)
						{
							VoxelWeapon voxelWeapon = new(VRAssetManager.VoxelAtlas, hand, _displayMode.GetGripHandNode(hand)) { Name = $"VoxelWeapon{hand}" };
							aimNode.AddChild(voxelWeapon);
							voxelWeapon.Subscribe(_simulatorController.Simulator);
						}
						else
						{
							WeaponHandMesh handMesh = new(VRAssetManager.SpriteTextures, hand) { Name = $"WeaponHandMesh{hand}" };
							aimNode.AddChild(handMesh);
							handMesh.Subscribe(_simulatorController.Simulator);
						}
			_simulatorController.Simulator.EmitWeaponState();

			// Store level index in inventory so save/load can determine which level to restore
			// Keep the status/inventory metadata aligned with the currently loaded map.
			_simulatorController.Simulator.Inventory.SetValue("MapOn", _initialLevelIndex);
			_simulatorController.Simulator.Inventory.SetValue("Episode", MapAnalysis.Episode);
			_simulatorController.Simulator.Inventory.SetValue("Floor", MapAnalysis.Level);

		// Wire up hit detection callback for weapon state machine
		// WL_AGENT.C:GunAttack is called on the fire frame - this provides the raycast
		_simulatorController.Simulator.HitDetection = PerformWeaponRaycast;
		_simulatorController.Simulator.WeaponFirePoseProvider = GetWeaponFirePose;

			// Wire up movement validation for collision detection
			// This enables wall collision and wall-sliding behavior
			_displayMode.SetMovementValidator(_simulatorController.ValidateMovement);

			// Create screen flash overlay (palette-shift effects: damage flash, bonus flash, fades)
			// Uses CanvasLayer for flatscreen and a veil quad on the XRCamera3D for VR headset
			_screenFlashOverlay = new ScreenFlashOverlay();
			AddChild(_screenFlashOverlay);
			_screenFlashOverlay.Subscribe(_simulatorController);
			_screenFlashOverlay.SetVRCamera(_displayMode.IsVRActive ? _displayMode.Camera : null);

			// Create death fizzle overlay (ID_VH.C:FizzleFade to red on player death)
			// WL_GAME.C:Died: VW_Bar fills view with color 4 (red), FizzleFade reveals it over 70 tics
			_deathFizzleOverlay = new DeathFizzleOverlay();
			AddChild(_deathFizzleOverlay);
			_deathFizzleOverlay.SetVRCamera(_displayMode.IsVRActive ? _displayMode.Camera : null);
			_deathFizzleOverlay.FizzleComplete += OnDeathFizzleComplete;

			// Subscribe to menu navigation events (VictoryTile, quiz triggers, etc.)
			_simulatorController.NavigateToMenu += OnNavigateToMenu;

			// Subscribe to direct gameplay map transition events (e.g., Spear pickup jump)
			_simulatorController.GameplayMapTransitionRequested += OnGameplayMapTransitionRequested;

			// Subscribe to player death events
			_simulatorController.PlayerDied += OnPlayerDied;

			// Subscribe to victory started event for BJ animation teleport and tile confinement
			_simulatorController.VictoryStarted += OnVictoryStarted;

			// Subscribe to display mode button events for shooting and using objects
			_displayMode.HandButtonPressed += OnHandButtonPressed;
			_displayMode.HandButtonReleased += OnHandButtonReleased;

			// Capture mouse for FPS controls in flatscreen mode
			if (!_displayMode.IsVRActive)
				Input.MouseMode = Input.MouseModeEnum.Captured;

			// Create pixel-perfect aiming system
			_pixelPerfectAiming = new PixelPerfectAiming(this);

			// Create debug aim indicator (temporary - won't be in final game)
			_aimIndicator = new AimIndicator(_pixelPerfectAiming, _displayMode);
			AddChild(_aimIndicator);

			// Create VR teleportation overlay (arc and circle showing teleport destination)
			// Only needed in VR mode; flatscreen has no thumbstick teleportation
			if (_displayMode.IsVRActive)
			{
				_teleportOverlay = new TeleportationOverlay();
				AddChild(_teleportOverlay);
			}
			// Wire status bar events before OnNewGame script runs.
			// Script fires StatusBarTextChanged/StatusBarPicChanged to initialize the display.
			// Both VR (wristwatch) and flatscreen need the renderer and events wired here.
			if (_statusBarController is not null)
			{
				// For new/loaded games, subscribe to the fresh simulator and sync initial state.
				// For resumed games (_existingSimulator != null), the controller is already
				// subscribed and state is current — Subscribe re-wires and re-syncs safely.
				_statusBarController.Subscribe(_simulatorController.Simulator);
				_statusBarRenderer = new StatusBarRenderer(_statusBarController.State);
			}

			// Execute OnNewGame script for new games (VR and flatscreen).
			// WL_GAME.C:NewGame — sets initial inventory values (health, ammo, weapons, etc.)
			// Must run after status bar events are wired so display updates are received.
			// Must run before EquipStartingWeapon so weapon ownership is set correctly.
			if (_existingSimulator is null && _loadSnapshot is null && _savedInventory is null)
				_simulatorController.Simulator.ExecuteOnNewGameScript();

			// Execute OnMapStart only when constructing a fresh simulator for a map.
			// Skip it for resumed games and save restores so existing state is preserved exactly.
			if (_existingSimulator is null && _loadSnapshot is null)
				_simulatorController.Simulator.ExecuteOnMapStartScript();

			// Equip starting weapon based on inventory (new game or level transition).
			// New game: SelectedWeapon0 set by OnNewGame script above.
			// Level transition: SelectedWeapon0 restored from savedInventory.
			if (_existingSimulator is null && _loadSnapshot is null)
				_simulatorController.Simulator.EquipStartingWeapon();

			// Create automap renderer — needed in both VR (wristwatch) and flatscreen modes.
			_automapController = new AutomapController();
			_automapController.Init(MapAnalysis, _simulatorController.Simulator);

			if (!_displayMode.IsVRActive)
			{
				// Flatscreen: display status bar at bottom of screen and automap in top-right corner.
				// Each gets its own SubViewport created here; the renderers provide only the Control.
				if (_statusBarRenderer is not null)
				{
					SubViewport statusBarVP = new()
					{
						Name = "StatusBarViewport",
						Size = new Vector2I(320, 40),
						Disable3D = true,
						RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
					};
					statusBarVP.AddChild(_statusBarRenderer.Canvas);

					_statusBarCanvas = new CanvasLayer
					{
						Name = "StatusBarCanvas",
						Layer = 10,
					};
					AddChild(_statusBarCanvas);
					_statusBarCanvas.AddChild(statusBarVP);

					TextureRect statusBarDisplay = new()
					{
						Name = "StatusBarDisplay",
						Texture = statusBarVP.GetTexture(),
						AnchorLeft = 0.5f,
						AnchorRight = 0.5f,
						AnchorTop = 1.0f,
						AnchorBottom = 1.0f,
						CustomMinimumSize = new Vector2(960, 120),
						Size = new Vector2(960, 120),
						OffsetLeft = -480,
						OffsetRight = 480,
						OffsetTop = -120,
						OffsetBottom = 0,
						TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
					};
					_statusBarCanvas.AddChild(statusBarDisplay);

					Control weaponHudOverlay = new()
					{
						Name = "WeaponHudOverlay",
						AnchorLeft = 0.5f,
						AnchorRight = 0.5f,
						AnchorTop = 1.0f,
						AnchorBottom = 1.0f,
						OffsetLeft = -480,
						OffsetRight = 480,
						OffsetTop = -220,
						OffsetBottom = 0,
						MouseFilter = Control.MouseFilterEnum.Ignore,
						ZIndex = 20,
					};
					_statusBarCanvas.AddChild(weaponHudOverlay);
					_weapons.AttachHud(weaponHudOverlay);
				}

				SubViewport automapVP = new()
				{
					Name = "AutomapViewport",
					Size = new Vector2I(AutomapRenderer.ViewWidth, AutomapRenderer.ViewHeight),
					Disable3D = true,
					RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
				};
				automapVP.AddChild(_automapController.Renderer);

				CanvasLayer automapCanvas = new()
				{
					Name = "AutomapCanvas",
					Layer = 10,
				};
				AddChild(automapCanvas);
				automapCanvas.AddChild(automapVP);

				TextureRect automapDisplay = new()
				{
					Name = "AutomapDisplay",
					Texture = automapVP.GetTexture(),
					AnchorLeft = 1f,
					AnchorRight = 1f,
					AnchorTop = 0f,
					AnchorBottom = 0f,
					CustomMinimumSize = new Vector2(AutomapRenderer.ViewWidth * 2, AutomapRenderer.ViewHeight * 2),
					Size = new Vector2(AutomapRenderer.ViewWidth * 2, AutomapRenderer.ViewHeight * 2),
					OffsetLeft = -AutomapRenderer.ViewWidth * 2,
					OffsetRight = 0,
					OffsetTop = 0,
					OffsetBottom = AutomapRenderer.ViewHeight * 2,
					TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
				};
				automapCanvas.AddChild(automapDisplay);
			}
			else if (_statusBarRenderer is not null)
			{
				// VR: display status bar and automap as a wristwatch on the left hand.
				if (_displayMode.GetGripHandNode(1) is Node3D leftGripNode)
				{
					_wristwatchDisplay = new WristwatchDisplay(
						_automapController,
						_statusBarRenderer,
						_displayMode.Camera);
					leftGripNode.AddChild(_wristwatchDisplay);
				}
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
			// ESC returns to main menu (blocked during BJ victory animation)
			if (keyEvent.Keycode == Key.Escape)
			{
				if (!(_simulatorController?.VictoryFlag ?? false))
					PendingReturnToMenu = true;
				return;
			}

			if (_cheatModeEnabled)
			{
				// Cheat: N skips to the next level (like activating the elevator).
				if (keyEvent.Keycode == Key.N)
				{
					TriggerNextLevelCheat();
					return;
				}

				// Cheat: M refills all ammo types and health to max.
				if (keyEvent.Keycode == Key.M)
				{
					TriggerMaxResourcesCheat();
					return;
				}
			}

			// Weapon switching - number keys map directly to weapon numbers from XML
			// Based on WL_AGENT.C weapon selection (bt_readyknife, bt_readypistol, etc.)
			int weaponNumber = keyEvent.Keycode switch
			{
				Key.Key1 => 0,
				Key.Key2 => 1,
				Key.Key3 => 2,
				Key.Key4 => 3,
				Key.Key5 => 4,
				Key.Key6 => 5,
				Key.Key7 => 6,
				Key.Key8 => 7,
				Key.Key9 => 8,
				Key.Key0 => 9,
				_ => -1
			};
			if (weaponNumber >= 0
				&& _simulatorController.Simulator?.WeaponCollection is not null
				&& _simulatorController.Simulator.WeaponCollection.TryGetWeaponByNumber(weaponNumber, out WeaponInfo weaponInfo))
			{
				SwitchWeapon(weaponInfo.Name);
			}
		}
	}

	/// <summary>
	/// Handles a button press on any hand controller.
	/// trigger_click fires that hand's weapon slot; grip_click uses/pushes objects.
	/// ax_button cycles weapon backward; by_button cycles weapon forward (per hand).
	/// menu_button (left hand) returns to main menu.
	/// primary_click (left hand) toggles running; primary_click (right hand) toggles turn mode.
	/// In VR, grip uses the controller's own position and pointing direction.
	/// In flatscreen, grip uses the camera direction (single cursor).
	/// </summary>
	private void OnHandButtonPressed(int handIndex, string buttonName)
	{
		UpdateCheatButtonState(handIndex, buttonName, pressed: true);

		if (buttonName == "trigger_click")
			FireWeapon(handIndex);
		else if (buttonName == "grip_click")
		{
			if (_displayMode.IsVRActive)
				UseObjectHandIsFacing(handIndex);
			else
				UseObjectPlayerIsFacing();
		}
		else if (buttonName == "ax_button")
		{
			if (HandleVrFaceButtonPress(handIndex, -1))
				return;
			if (TryTriggerVrCheatCombo(handIndex))
				return;
			CycleWeapon(handIndex, -1);
		}
		else if (buttonName == "by_button")
		{
			if (HandleVrFaceButtonPress(handIndex, +1))
				return;
			if (TryTriggerVrCheatCombo(handIndex))
				return;
			CycleWeapon(handIndex, +1);
		}
		else if (buttonName == "menu_button" && handIndex == 1)
		{
			// Blocked during BJ victory animation
			if (!(_simulatorController?.VictoryFlag ?? false))
				PendingReturnToMenu = true;
		}
		else if (buttonName == "primary_click")
		{
			if (handIndex == 0)
			{
				if (_teleportOverlay is not null
					&& _displayMode.IsTeleportModeActive
					&& _teleportOverlay.DestinationTile.HasValue
					&& _teleportOverlay.IsDestinationNavigable)
					ExecuteTeleport(_teleportOverlay.DestinationTile.Value);
				else
					_displayMode.ToggleTurnMode();
			}
			else
				_displayMode.ToggleRunning();
		}
	}

	/// <summary>
	/// Handles a button release on any hand controller.
	/// Releases the trigger for the corresponding weapon slot (required for semi-auto).
	/// </summary>
	private void OnHandButtonReleased(int handIndex, string buttonName)
	{
		UpdateCheatButtonState(handIndex, buttonName, pressed: false);

		if (HandleVrFaceButtonRelease(handIndex, buttonName))
			return;

		if (buttonName == "trigger_click")
			ReleaseWeaponTrigger(handIndex);
	}

	private void UpdateCheatButtonState(int handIndex, string buttonName, bool pressed)
	{
		if (handIndex == 0)
		{
			if (buttonName == "ax_button")
				_rightAxPressed = pressed;
			else if (buttonName == "by_button")
				_rightByPressed = pressed;
		}
		else if (handIndex == 1)
		{
			if (buttonName == "ax_button")
				_leftAxPressed = pressed;
			else if (buttonName == "by_button")
				_leftByPressed = pressed;
		}
	}

	private bool TryTriggerVrCheatCombo(int handIndex)
	{
		if (!_displayMode.IsVRActive || !_cheatModeEnabled)
			return false;

		if (handIndex == 0 && _rightAxPressed && _rightByPressed)
		{
			TriggerNextLevelCheat();
			return true;
		}

		if (handIndex == 1 && _leftAxPressed && _leftByPressed)
		{
			TriggerMaxResourcesCheat();
			return true;
		}

		return false;
	}

	private bool HandleVrFaceButtonPress(int handIndex, int direction)
	{
		if (!_displayMode.IsVRActive || !_cheatModeEnabled)
			return false;

		ref int pendingDirection = ref handIndex == 0
			? ref _rightPendingCycleDirection
			: ref _leftPendingCycleDirection;
		ref bool comboTriggered = ref handIndex == 0
			? ref _rightComboTriggered
			: ref _leftComboTriggered;

		pendingDirection = direction;
		if (TryTriggerVrCheatCombo(handIndex))
		{
			comboTriggered = true;
			pendingDirection = 0;
		}

		return true;
	}

	private bool HandleVrFaceButtonRelease(int handIndex, string buttonName)
	{
		if (!_displayMode.IsVRActive || !_cheatModeEnabled || (buttonName != "ax_button" && buttonName != "by_button"))
			return false;

		ref int pendingDirection = ref handIndex == 0
			? ref _rightPendingCycleDirection
			: ref _leftPendingCycleDirection;
		ref bool comboTriggered = ref handIndex == 0
			? ref _rightComboTriggered
			: ref _leftComboTriggered;
		bool otherButtonStillPressed = handIndex == 0
			? _rightAxPressed || _rightByPressed
			: _leftAxPressed || _leftByPressed;

		if (comboTriggered)
		{
			if (!otherButtonStillPressed)
				comboTriggered = false;
			pendingDirection = 0;
			return true;
		}

		int releasedDirection = buttonName == "ax_button" ? -1 : +1;
		if (pendingDirection == releasedDirection)
		{
			CycleWeapon(handIndex, pendingDirection);
			pendingDirection = 0;
			return true;
		}

		return false;
	}

	private void ApplyCheatPenalty()
	{
		_simulatorController?.Simulator?.Inventory?.SetValue("Score", 0);
	}

	private void TriggerNextLevelCheat()
	{
		if (_simulatorController?.Simulator is not Simulator.Simulator sim)
			return;

		ApplyCheatPenalty();

		InventorySnapshot savedInventory = sim.Inventory.Save();
		byte destinationLevel = MapAnalysis.ElevatorTo;
		LevelCompletionStats stats = sim.GetCompletionStats(_initialLevelIndex + 1, false, MapAnalysis.Par);
		sim.AddCompletionStats(stats);
		PendingTransition = new LevelTransitionRequest(
			destinationLevel, savedInventory, stats,
			menuName: GetLevelCompleteMenuName(stats),
			allLevelStats: sim.LevelRatios,
			floorColor: MapAnalysis.Floor,
			ceilingColor: MapAnalysis.Ceiling,
			floorTilePage: MapAnalysis.FloorTile,
			ceilingTilePage: MapAnalysis.CeilingTile,
			equippedWeaponShapes: GetEquippedWeaponShapes());
	}

	private void TriggerMaxResourcesCheat()
	{
		if (_simulatorController?.Simulator is not Simulator.Simulator sim)
			return;

		ApplyCheatPenalty();
		sim.ExecuteOnCheatScript();
	}

	/// <summary>
	/// Fires the weapon in the specified slot.
	/// Signals the simulator that the trigger was pulled.
	/// Hit detection is handled by the weapon state machine via HitDetection callback.
	/// </summary>
	private void FireWeapon(int slotIndex)
	{
		_simulatorController.FireWeapon(slotIndex);
	}

	private WeaponFirePose? GetWeaponFirePose(int slotIndex)
	{
		Vector3 forward = _displayMode.IsVRActive
			? _displayMode.GetHandForward(slotIndex)
			: -_displayMode.Camera.GlobalTransform.Basis.Z;
		Vector3 origin = _displayMode.IsVRActive
			? _displayMode.GetHandPosition(slotIndex)
			: _displayMode.Camera.GlobalPosition;

		Vector2 horizontalForward = new(forward.X, forward.Z);
		if (horizontalForward.LengthSquared() < 0.0001f)
			return null;

		float wolfAngle = Mathf.RadToDeg(Mathf.Atan2(-forward.Z, forward.X));
		wolfAngle = ((wolfAngle % 360f) + 360f) % 360f;

		return new WeaponFirePose
		{
			X = origin.X.ToFixedPoint(),
			Y = origin.Z.ToFixedPoint(),
			Angle = (short)Mathf.RoundToInt(wolfAngle)
		};
	}

	/// <summary>
	/// Performs a raycast for weapon hit detection.
	/// Called by the simulator's weapon state machine on fire frames (A_PistolAttack, etc.).
	/// Based on WL_AGENT.C:GunAttack performing raycast on each fire frame in T_Attack.
	/// In VR: slot 0 = right controller, slot 1 = left controller.
	/// In flatscreen: always uses the camera.
	/// </summary>
	/// <param name="slotIndex">Weapon slot index</param>
	/// <returns>Hit actor index, or null if miss</returns>
	private int? PerformWeaponRaycast(int slotIndex)
	{
		Vector3 cameraForward = -_displayMode.Camera.GlobalTransform.Basis.Z;
		Vector3 rayOrigin = _displayMode.IsVRActive
			? _displayMode.GetHandPosition(slotIndex)
			: _displayMode.Camera.GlobalPosition;
		Vector3 rayDirection = _displayMode.IsVRActive
			? _displayMode.GetHandForward(slotIndex)
			: cameraForward;

		PixelPerfectAiming.AimHitResult hitResult = _pixelPerfectAiming.Raycast(rayOrigin, rayDirection, cameraForward);

		return (hitResult.IsHit && hitResult.Type == PixelPerfectAiming.HitType.Actor)
			? hitResult.ActorIndex
			: null;
	}

	/// <summary>
	/// Releases the trigger for the specified weapon slot.
	/// Required for semi-auto weapons to allow firing again.
	/// </summary>
	private void ReleaseWeaponTrigger(int slotIndex)
	{
		_simulatorController.ReleaseWeaponTrigger(slotIndex);
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
	/// Cycles to the previous (direction = -1) or next (direction = +1) weapon the player owns
	/// for the specified hand slot. Wraps around. Skips weapons not in inventory.
	/// </summary>
	private void CycleWeapon(int handIndex, int direction)
	{
		WeaponCollection collection = _simulatorController.Simulator?.WeaponCollection;
		if (collection is null)
			return;

		List<int> weaponNumbers = [.. collection.Weapons.Values
			.OrderBy(w => w.Number)
			.Select(w => w.Number)];
		if (weaponNumbers.Count == 0)
			return;

		int currentNumber = _simulatorController.Simulator.Inventory.GetValue($"SelectedWeapon{handIndex}");
		int currentIdx = weaponNumbers.IndexOf(currentNumber);
		if (currentIdx < 0)
			currentIdx = 0;

		// Step through weapons in the requested direction, skipping unowned ones
		for (int i = 1; i <= weaponNumbers.Count; i++)
		{
			int nextIdx = ((currentIdx + direction * i) % weaponNumbers.Count + weaponNumbers.Count) % weaponNumbers.Count;
			int nextNumber = weaponNumbers[nextIdx];
			if (collection.TryGetWeaponByNumber(nextNumber, out WeaponInfo weaponInfo)
				&& _simulatorController.Simulator.Inventory.Has(WeaponCollection.GetInventoryKey(nextNumber)))
			{
				_simulatorController.SwitchWeapon(handIndex, weaponInfo.Name);
				return;
			}
		}
	}

	/// <summary>
	/// Teleports the player to the center of the destination tile.
	/// Moves the XROrigin so the HMD lands at the tile center, then registers
	/// the new position with the simulator (triggering item pickup at the destination).
	/// </summary>
	/// <param name="destTile">Destination tile (X = Godot X = Wolf3D X, Z = Godot Z = Wolf3D Y)</param>
	private void ExecuteTeleport((ushort X, ushort Z) destTile)
	{
		float destWorldX = destTile.X.ToMetersCentered();
		float destWorldZ = destTile.Z.ToMetersCentered();

		// Shift XROrigin so the HMD ends up at the tile center (XZ only; Y is height-locked)
		Vector3 hmdPos = _displayMode.ViewerPosition;
		Vector3 originPos = _displayMode.Origin.GlobalPosition;
		_displayMode.Origin.GlobalPosition = new Vector3(
			originPos.X + (destWorldX - hmdPos.X),
			originPos.Y,
			originPos.Z + (destWorldZ - hmdPos.Z));

		// Register the new position with the simulator via TeleportPlayer, which sweeps
		// all tiles along the straight-line path from the previous position using a
		// Bresenham DDA, collecting items along the way.
		short currentAngle = _displayMode.ViewerYRotation.ToWolf3DAngle();
		_simulatorController.TeleportPlayer(
			destWorldX.ToFixedPoint(),
			destWorldZ.ToFixedPoint(),
			currentAngle);

		// Reset movement validation so ValidateVRPosition re-initializes from the new location,
		// preventing it from attempting to validate the large positional jump as a wall collision.
		_displayMode.SetMovementValidator(_simulatorController.ValidateMovement);
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

		// Convert world positions to tile coordinates
		int playerTileXInt = cameraPos.X.ToTile(), playerTileYInt = cameraPos.Z.ToTile();
		ushort tileX = (ushort)forwardPoint.X.ToTile(),
			tileY = (ushort)forwardPoint.Z.ToTile();

		// Try door first (no adjacency restriction — doors open on any forward hit)
		ushort? doorIndex = FindDoorAtTile(tileX, tileY);
		if (doorIndex.HasValue)
		{
			_simulatorController.OperateDoor(doorIndex.Value);
			return;
		}

		// Pushwalls and elevators require the player to be cardinally adjacent to the target tile.
		// This prevents diagonal peeking from triggering switches that should be blocked by walls.
		// WL_AGENT.C:Cmd_Use — only the tile directly in front is valid.
		if (playerTileXInt < 0 || playerTileYInt < 0)
			return;
		ushort playerTileX = (ushort)playerTileXInt, playerTileY = (ushort)playerTileYInt;
		if (GetCardinalDirection(playerTileX, playerTileY, tileX, tileY) is not Direction cardinalDir)
		{
			_simulatorController.UseNormalWall();
			return;
		}

		// Try pushwall second
		ushort? pushWallIndex = FindPushWallAtTile(tileX, tileY);
		if (pushWallIndex.HasValue)
		{
			_simulatorController.ActivatePushWall(tileX, tileY, cardinalDir);
			return;
		}

		// Try elevator third
		if (IsElevatorAtTile(tileX, tileY))
			_simulatorController.ActivateElevator(tileX, tileY, cardinalDir);
		else
			_simulatorController.UseNormalWall();
	}

	/// <summary>
	/// VR: finds and operates the door, pushwall, or elevator reachable by the specified hand.
	///
	/// Scenario 2 — controller extended into the target tile itself (checked first):
	///   The controller's tile contains a door, pushwall, or elevator switch.
	///   Valid when the headset is in a navigable tile cardinally adjacent to the controller tile.
	///   Push direction is determined solely by the tile relationship (headset tile → controller tile),
	///   preventing sideways pushes regardless of where the controller is pointing.
	///   Note: door tiles are navigable in MapAnalysis, so we must check for objects in the
	///   controller's tile before falling through to the pointer-based scenario.
	///
	/// Scenario 1 — controller in a navigable tile, pointing at the target:
	///   The controller's pointing direction (snapped to cardinal) selects the adjacent target tile.
	///   The push direction equals the tile-relationship direction (controller tile → target tile).
	/// </summary>
	private void UseObjectHandIsFacing(int handIndex)
	{
		Vector3 handPos = _displayMode.GetHandPosition(handIndex);
		int handTileXInt = handPos.X.ToTile(), handTileYInt = handPos.Z.ToTile();
		if (handTileXInt < 0 || handTileYInt < 0)
			return;
		ushort handTileX = (ushort)handTileXInt, handTileY = (ushort)handTileYInt;

		// Scenario 2: controller is in the target tile itself.
		// Checked first because door tiles are navigable, and we must not look past them.
		if (HasUsableObject(handTileX, handTileY))
		{
			Vector3 viewerPos = _displayMode.ViewerPosition;
			int viewerTileXInt = viewerPos.X.ToTile(), viewerTileYInt = viewerPos.Z.ToTile();
			if (viewerTileXInt >= 0 && viewerTileYInt >= 0)
			{
				ushort viewerTileX = (ushort)viewerTileXInt, viewerTileY = (ushort)viewerTileYInt;
				if (MapAnalysis.IsNavigable(viewerTileX, viewerTileY)
					&& GetCardinalDirection(viewerTileX, viewerTileY, handTileX, handTileY) is Direction pushDir)
					TryUseObjectAtTile(handTileX, handTileY, pushDir);
			}
			return; // Controller is in an object tile; Scenario 1 does not apply
		}

		// Scenario 1: controller in a navigable tile, pointing at the adjacent target.
		if (!MapAnalysis.IsNavigable(handTileX, handTileY))
			return;

		Vector3 handForward = _displayMode.GetHandForward(handIndex);
		Vector3 horizontal = new(handForward.X, 0f, handForward.Z);
		if (horizontal.LengthSquared() < 0.0001f)
			return; // Pointing straight up or down

		Direction dir = horizontal.ToCardinalDirection();
		if (TryGetAdjacentTile(handTileX, handTileY, dir, out ushort targetX, out ushort targetY)
			&& !TryUseObjectAtTile(targetX, targetY, dir))
			_simulatorController.UseNormalWall();
	}

	/// <summary>
	/// Returns true if the given tile contains a door, pushwall, or elevator switch.
	/// </summary>
	private bool HasUsableObject(ushort tileX, ushort tileY) =>
		FindDoorAtTile(tileX, tileY).HasValue
		|| FindPushWallAtTile(tileX, tileY).HasValue
		|| IsElevatorAtTile(tileX, tileY);

	/// <summary>
	/// Tries to operate a door, pushwall, or elevator at the given tile.
	/// Returns true if something was found and activated.
	/// </summary>
	private bool TryUseObjectAtTile(ushort tileX, ushort tileY, Direction dir)
	{
		ushort? doorIndex = FindDoorAtTile(tileX, tileY);
		if (doorIndex.HasValue)
		{
			_simulatorController.OperateDoor(doorIndex.Value);
			return true;
		}

		ushort? pushWallIndex = FindPushWallAtTile(tileX, tileY);
		if (pushWallIndex.HasValue)
		{
			_simulatorController.ActivatePushWall(tileX, tileY, dir);
			return true;
		}

		if (IsElevatorAtTile(tileX, tileY))
		{
			_simulatorController.ActivateElevator(tileX, tileY, dir);
			return true;
		}

		return false;
	}

	/// <summary>
	/// Returns the cardinal direction from one tile to an immediately adjacent tile.
	/// Returns null if the tiles are not cardinally adjacent (diagonal or non-adjacent).
	/// </summary>
	private static Direction? GetCardinalDirection(ushort fromX, ushort fromY, ushort toX, ushort toY)
	{
		int dx = (int)toX - fromX, dy = (int)toY - fromY;
		if (dx == 1 && dy == 0) return Direction.E;
		if (dx == -1 && dy == 0) return Direction.W;
		if (dx == 0 && dy == -1) return Direction.N;
		if (dx == 0 && dy == 1) return Direction.S;
		return null;
	}

	/// <summary>
	/// Returns the tile one step in the given cardinal direction, if in bounds.
	/// Returns false for diagonal directions or when the step would go out of bounds.
	/// </summary>
	private static bool TryGetAdjacentTile(ushort tileX, ushort tileY, Direction dir, out ushort adjX, out ushort adjY)
	{
		int ax = tileX, ay = tileY;
		switch (dir)
		{
			case Direction.E: ax++; break;
			case Direction.W: ax--; break;
			case Direction.N: ay--; break;
			case Direction.S: ay++; break;
			default: adjX = tileX; adjY = tileY; return false;
		}
		if (ax < 0 || ay < 0)
		{
			adjX = 0; adjY = 0;
			return false;
		}
		adjX = (ushort)ax;
		adjY = (ushort)ay;
		return true;
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
		uint[] palette = Shared.SharedAssetManager.CurrentGame.VSwap.Palette;
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
		// DisplayMode.Update is called from Root._Process (ProcessMode.Always) so it continues
		// during fade transitions. Calling it here too would apply locomotion twice per frame,
		// causing the player to drift away from the level while the fade plays.

		// Update teleportation overlay when in VR teleport mode
		if (_teleportOverlay is not null)
		{
			if (_displayMode.IsTeleportModeActive)
			{
				Vector3 controllerPos = _displayMode.GetHandPosition(0);   // Right controller
				Vector3 controllerForward = _displayMode.GetHandForward(0);
				Vector3 viewerPos = _displayMode.ViewerPosition;
				ushort playerTileX = (ushort)Mathf.Max(0, viewerPos.X.ToTile());
				ushort playerTileZ = (ushort)Mathf.Max(0, viewerPos.Z.ToTile());
				_teleportOverlay.UpdateOverlay(
					controllerPos,
					controllerForward,
					(x, z) => x < MapAnalysis.Width && z < MapAnalysis.Depth
						&& _simulatorController.IsTileNavigable(x, z)
						&& _simulatorController.HasClearTeleportPath(playerTileX, playerTileZ, x, z));
			}
			else
			{
				_teleportOverlay.HideOverlay();
			}
		}

		// Fixtures updates billboard rotations automatically in its own _Process
		// Bonuses updates billboard rotations automatically in its own _Process
		// Doors use two-quad approach with back-face culling - no per-frame updates needed
		// SimulatorController drives the simulator and updates door/bonus states automatically in its own _Process

		// Update automap player marker every frame (QueueRedraw is coalesced by Godot if nothing changed)
		if (_automapController is not null && _simulatorController?.Simulator is not null)
			_automapController.UpdatePlayer(
				_simulatorController.Simulator.PlayerTileX,
				_simulatorController.Simulator.PlayerTileY,
				_simulatorController.Simulator.PlayerAngle);
	}

	public override void _ExitTree()
	{
		// Unsubscribe from display mode events to prevent ObjectDisposedException
		// when old ActionStage is freed during level transitions
		_displayMode.HandButtonPressed -= OnHandButtonPressed;
		_displayMode.HandButtonReleased -= OnHandButtonReleased;

		// Unsubscribe from simulator events
		if (_simulatorController is not null)
		{
			_simulatorController.NavigateToMenu -= OnNavigateToMenu;
			_simulatorController.GameplayMapTransitionRequested -= OnGameplayMapTransitionRequested;
			_simulatorController.PlayerDied -= OnPlayerDied;
			_simulatorController.VictoryStarted -= OnVictoryStarted;
		}

		// Unsubscribe death fizzle overlay
		if (_deathFizzleOverlay is not null)
			_deathFizzleOverlay.FizzleComplete -= OnDeathFizzleComplete;

		// StatusBarRenderer subscribes UpdateText/UpdatePicture to state events in its constructor;
		// Dispose() unsubscribes them so they cannot fire against freed Godot Label/TextureRect nodes.
		// The StatusBarController itself remains subscribed to the simulator (owned by Root)
		// so the state stays current while a quiz menu is displayed during suspension.
		_statusBarRenderer?.Dispose();

		// Unsubscribe automap from simulator events
		_automapController?.Unsubscribe();

		// Clear presentation-owned firing delegates (point at this ActionStage's methods)
		if (_simulatorController?.Simulator is not null)
		{
			_simulatorController.Simulator.HitDetection = null;
			_simulatorController.Simulator.WeaponFirePoseProvider = null;
		}


		// Release mouse when leaving action stage (returning to menu, etc.)
		if (!_displayMode.IsVRActive)
			Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	/// <summary>
	/// Returns the current VSwap page number for each weapon slot, or null if the slot is empty.
	/// Used to preserve the player's equipped weapons on the elevator intermission screen.
	/// </summary>
	private ushort?[] GetEquippedWeaponShapes()
	{
		IReadOnlyList<Simulator.Entities.WeaponSlot> slots = _simulatorController?.Simulator?.WeaponSlots;
		if (slots is null)
			return null;
		Assets.Gameplay.WeaponCollection weapons = Shared.SharedAssetManager.CurrentGame?.WeaponCollection;
		Assets.Gameplay.StateCollection states = Shared.SharedAssetManager.CurrentGame?.StateCollection;
		ushort?[] shapes = new ushort?[slots.Count];
		for (int i = 0; i < slots.Count; i++)
		{
			string weaponType = slots[i].WeaponType;
			if (weaponType is null)
				continue;
			// Use IdleState shape so menu shows weapon at rest, not mid-animation (e.g., not firing frame)
			if (weapons?.TryGetWeapon(weaponType, out Assets.Gameplay.WeaponInfo info) == true
				&& info?.IdleState is not null
				&& states?.States.TryGetValue(info.IdleState, out Assets.Gameplay.State idleState) == true
				&& idleState.Shape >= 0)
				shapes[i] = (ushort)idleState.Shape;
		}
		return shapes;
	}

	/// <summary>
	/// Handles player death event.
	/// If FizzleFadeColor is defined in XML, starts the fizzle-to-color animation (PendingDeathFadeOut
	/// is set after fizzle completes). If absent, skips fizzle and sets PendingDeathFadeOut immediately.
	/// WL_GAME.C:Died: VW_Bar fills view with VGA color 4 (red), FizzleFade reveals it over 70 tics.
	/// S3DNA skips FizzleFade (#ifndef GAMEVER_NOAH3D) and fades to black instead.
	/// </summary>
	private void OnPlayerDied(PlayerDiedEvent e)
	{
		byte? fizzleColorIndex = Shared.SharedAssetManager.StatusBar?.FizzleFadeColor;
		if (fizzleColorIndex.HasValue)
		{
			// WL_GAME.C:Died: VW_Bar fills view with the configured palette color, then FizzleFade
			Color fizzleColor = Shared.SharedAssetManager.GetPaletteColor(fizzleColorIndex.Value);
			_deathFizzleOverlay?.TriggerFizzle(fizzleColor);
		}
		else
		{
			// No FizzleFadeColor defined: skip fizzle, go straight to fade to black
			PendingDeathFadeOut = true;
		}
	}

	/// <summary>
	/// Handles fizzle animation complete: screen is now fully red.
	/// Sets PendingDeathFadeOut for Root to initiate the fade to black.
	/// WL_GAME.C:Died: after FizzleFade, IN_UserInput(100) pauses, then SD_WaitSoundDone, then lives--.
	/// </summary>
	private void OnDeathFizzleComplete()
	{
		PendingDeathFadeOut = true;
	}

	/// <summary>
	/// Handles the start of the BJ Blazkowicz victory animation.
	/// Teleports the player to the viewing tile (northernmost navigable tile in their column),
	/// faces them south toward BJ, and confines them to that tile via movement validator.
	/// WL_ACT2.C:SpawnBJVictory → Simulator.VictoryStarted → here
	/// </summary>
	private void OnVictoryStarted(VictoryStartedEvent e)
	{
		float viewWorldX = e.ViewTileX.ToMetersCentered();
		float viewWorldZ = e.ViewTileY.ToMetersCentered();

		// Shift XROrigin so HMD lands at the viewing tile center
		if (_displayMode.Origin is not null)
		{
			Vector3 hmdPos = _displayMode.ViewerPosition;
			Vector3 originPos = _displayMode.Origin.GlobalPosition;
			_displayMode.Origin.GlobalPosition = new Vector3(
				originPos.X + (viewWorldX - hmdPos.X),
				originPos.Y,
				originPos.Z + (viewWorldZ - hmdPos.Z));
		}

		// Orient player to face south (toward BJ running north from below)
		// Godot Y rotation π = facing +Z = south in Wolf3D
		float southRotation = Mathf.Pi;
		if (_displayMode.IsVRActive)
		{
			// VR: ResetPositionFacing compensates for HMD physical rotation
			Vector3 viewXZ = new(viewWorldX, 0f, viewWorldZ);
			_displayMode.ResetPositionFacing(
				viewXZ + new Vector3(Mathf.Sin(southRotation), 0f, -Mathf.Cos(southRotation)),
				viewXZ);
		}
		else
		{
			// Flatscreen: ResetPositionFacing is a no-op; set camera global rotation directly
			if (_displayMode.Camera is not null)
				_displayMode.Camera.GlobalRotation = new Vector3(0, southRotation, 0);
		}

		// Register new position with simulator (system-driven, no item sweep)
		_simulatorController.Simulator.PlacePlayer(
			viewWorldX.ToFixedPoint(),
			viewWorldZ.ToFixedPoint(),
			southRotation.ToWolf3DAngle());

		// Confine player to viewing tile with the same HeadXZ buffer the wall system uses,
		// so the player cannot approach the tile edge any closer than a normal wall allows.
		float tileMinX = e.ViewTileX.ToMeters() + Constants.HeadXZ;
		float tileMaxX = tileMinX + Constants.TileWidth - Constants.HeadXZ * 2f;
		float tileMinZ = e.ViewTileY.ToMeters() + Constants.HeadXZ;
		float tileMaxZ = tileMinZ + Constants.TileWidth - Constants.HeadXZ * 2f;
		_displayMode.SetMovementValidator((currentX, currentZ, desiredX, desiredZ) =>
		{
			(float validX, float validZ) = _simulatorController.ValidateMovement(currentX, currentZ, desiredX, desiredZ);
			return (Mathf.Clamp(validX, tileMinX, tileMaxX),
			        Mathf.Clamp(validZ, tileMinZ, tileMaxZ));
		});
		_displayMode.LocomotionEnabled = false;
	}

	/// <summary>
	/// Handles menu navigation events from Lua scripts (e.g., VictoryTile, A_DeathScream, elevator switches).
	/// Always suspends the game and shows the requested menu.
	/// For level-exit menus (e.g., "LevelComplete"), also computes PendingLevelTransitionForMenu so that
	/// MenuRoom can trigger the level transition when Lua calls ContinueToNextLevel().
	/// WL_AGENT.C:VictoryTile → gamestate.victoryflag; WL_ACT2.C:A_StartDeathCam.
	/// </summary>
	private void OnNavigateToMenu(NavigateToMenuEvent e)
	{
		Simulator.Simulator sim = _simulatorController?.Simulator;
		// Release all weapon triggers so weapons don't continue firing after resuming.
		// Equivalent to the player releasing all fire buttons before the menu appears.
		if (sim?.WeaponSlots is not null)
			for (int i = 0; i < sim.WeaponSlots.Count; i++)
				_simulatorController.ReleaseWeaponTrigger(i);
		MenuDefinition menuDef = SharedAssetManager.CurrentGame?.MenuCollection?.GetMenu(e.MenuName);

		// Capture level-transition data when the map has an elevator destination.
		// This is stored in PendingLevelTransitionForMenu and passed to MenuRoom via SuspendToMenu.
		// ContinueToNextLevel() in Lua is the sole mechanism that triggers the actual transition.
		if (MapAnalysis?.ElevatorTo != 0 || MapAnalysis?.AltElevatorTo.HasValue == true)
		{
			uint playerPos = sim is not null
				? (uint)sim.PlayerTileX | ((uint)sim.PlayerTileY << 16)
				: 0u;
			bool isAlt = MapAnalysis.AltElevators.Contains(playerPos);
			byte destination = isAlt && MapAnalysis.AltElevatorTo.HasValue
				? MapAnalysis.AltElevatorTo.Value
				: MapAnalysis.ElevatorTo;
			InventorySnapshot savedInventory = sim?.Inventory?.Save();
			LevelCompletionStats stats = sim?.GetCompletionStats(
				_initialLevelIndex + 1, isAlt, MapAnalysis.Par);
			if (stats is not null)
				sim.AddCompletionStats(stats);
			IReadOnlyList<ushort?> weaponShapes = menuDef?.KeepWeapons == true ? GetEquippedWeaponShapes() : null;
			PendingLevelTransitionForMenu = new LevelTransitionRequest(
				destination, savedInventory, stats,
				menuName: e.MenuName,
				allLevelStats: sim?.LevelRatios,
				floorColor: MapAnalysis.Floor,
				ceilingColor: MapAnalysis.Ceiling,
				floorTilePage: MapAnalysis.FloorTile,
				ceilingTilePage: MapAnalysis.CeilingTile,
				equippedWeaponShapes: weaponShapes);
		}

		PendingMenuOverride = e.MenuName;
		PendingQuiz = sim?.PendingQuiz;
		if (sim is not null && SharedAssetManager.Config is not null)
		{
			SharedAssetManager.Config.QuestionNum = (short)sim.CurrentQuestionNum;
			SharedAssetManager.SaveConfig();
		}
		PendingEquippedWeaponShapes = menuDef?.KeepWeapons == true ? GetEquippedWeaponShapes() : null;
		// Capture and record completion stats for menus that show level stats (e.g., Victory)
		if (PendingLevelTransitionForMenu is null)
		{
			LevelCompletionStats menuStats = sim?.GetCompletionStats(
				_initialLevelIndex + 1, false, MapAnalysis.Par);
			if (menuStats is not null)
			{
				sim.AddCompletionStats(menuStats);
				PendingCompletionStats = menuStats;
				PendingAllLevelStats = sim.LevelRatios;
			}
		}
		else
		{
			PendingCompletionStats = PendingLevelTransitionForMenu.CompletionStats;
			PendingAllLevelStats = PendingLevelTransitionForMenu.AllLevelStats;
		}
		PendingReturnToMenu = true;
	}

	private void OnGameplayMapTransitionRequested(GameplayMapTransitionRequestedEvent e)
	{
		Simulator.Simulator sim = _simulatorController?.Simulator;
		InventorySnapshot savedInventory = sim?.Inventory?.Save();
		PendingTransition = new LevelTransitionRequest(
			e.DestinationLevel,
			savedInventory,
			allLevelStats: sim?.LevelRatios,
			showIntermission: false,
			playerXOverride: e.PreservePlayerTransform ? sim?.PlayerX : null,
			playerYOverride: e.PreservePlayerTransform ? sim?.PlayerY : null,
			playerAngleOverride: e.PreservePlayerTransform ? sim?.PlayerAngle : null);
	}
}
