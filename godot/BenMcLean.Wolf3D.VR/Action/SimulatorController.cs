using Godot;
using System;
using System.Collections.Generic;
using BenMcLean.Wolf3D.Simulator;
using BenMcLean.Wolf3D.Shared;
using Microsoft.Extensions.Logging;
using BenMcLean.Wolf3D.Simulator.Entities;
using BenMcLean.Wolf3D.Assets.Gameplay;

namespace BenMcLean.Wolf3D.VR;

/// <summary>
/// Godot controller that embeds the Wolf3D discrete event simulator.
/// Drives simulation from Godot's _Process loop and updates VR presentation based on events.
/// </summary>
public partial class SimulatorController : Node3D
{
	private Simulator.Simulator simulator;
	/// <summary>
	/// Gets the underlying simulator instance.
	/// Exposes Inventory for direct status bar subscription.
	/// </summary>
	public Simulator.Simulator Simulator => simulator;
	private Doors doors;
	private Walls walls;
	private Bonuses bonuses;
	private Actors actors;
	private Weapons weapons;
	private Func<(int X, int Y, short Angle)> getPlayerPosition; // Delegate returns Wolf3D 16.16 fixed-point coordinates and angle (0-359)

	// Named event handlers for cleanup in _ExitTree
	private Action<ElevatorActivatedEvent> _elevatorHandler;
	private Action<PlayerStateChangedEvent> _playerStateHandler;
	private Action<ScreenFlashEvent> _screenFlashHandler;
	private Action<NavigateToMenuEvent> _navigateToMenuHandler;

	/// <summary>
	/// Initializes the simulator with door, bonus, and actor data from MapAnalysis.
	/// Call this after adding the controller to the scene tree.
	/// </summary>
	/// <param name="mapAnalyzer">Map analyzer containing metadata (door sounds, etc.)</param>
	/// <param name="mapAnalysis">Map analysis containing all spawn data</param>
	/// <param name="doorsNode">The Doors node that will render the doors</param>
	/// <param name="wallsNode">The Walls node that will render the walls and pushwalls</param>
	/// <param name="bonusesNode">The Bonuses node that will render bonus items</param>
	/// <param name="actorsNode">The Actors node that will render actors</param>
	/// <param name="weaponsNode">The Weapons node that will render player weapons</param>
	/// <param name="stateCollection">State collection loaded from game XML (e.g., WL1.xml)</param>
	/// <param name="weaponCollection">Weapon collection loaded from game XML (e.g., WL1.xml)</param>
	/// <param name="getPlayerPosition">Delegate that returns player position in Wolf3D 16.16 fixed-point coordinates (X, Y) and angle (0-359)</param>
	public void Initialize(
		MapAnalyzer mapAnalyzer,
		MapAnalyzer.MapAnalysis mapAnalysis,
		Doors doorsNode,
		Walls wallsNode,
		Bonuses bonusesNode,
		Actors actorsNode,
		Weapons weaponsNode,
		StateCollection stateCollection,
		WeaponCollection weaponCollection,
		Func<(int X, int Y, short Angle)> getPlayerPosition,
		StatusBarDefinition statusBar,
		int difficulty,
		Dictionary<string, int> savedInventory = null,
		IReadOnlyList<LevelCompletionStats> savedLevelStats = null)
	{
		// TODO: Load stateCollection from WL1.xml or game data file
		// For now, create a placeholder if none provided
		if (stateCollection == null)
		{
			GD.PrintErr("WARNING: No StateCollection provided - creating empty collection (actors will not function)");
			stateCollection = new StateCollection();
		}

		// Simulation pauses during fade transitions while presentation layer keeps rendering
		// (VR head tracking and camera must continue, only gameplay logic pauses)
		ProcessMode = ProcessModeEnum.Pausable;

		// Create deterministic RNG and GameClock
		RNG rng = new(0); // TODO: Use seed from game settings or save file
		GameClock gameClock = new();

		// Create logger that routes to Godot console
		ILogger logger = new GodotLogger("Simulator");
		simulator = new Simulator.Simulator(stateCollection, rng, gameClock, logger);
		doors = doorsNode ?? throw new ArgumentNullException(nameof(doorsNode));
		walls = wallsNode ?? throw new ArgumentNullException(nameof(wallsNode));
		bonuses = bonusesNode ?? throw new ArgumentNullException(nameof(bonusesNode));
		actors = actorsNode ?? throw new ArgumentNullException(nameof(actorsNode));
		weapons = weaponsNode ?? throw new ArgumentNullException(nameof(weaponsNode));
		this.getPlayerPosition = getPlayerPosition ?? throw new ArgumentNullException(nameof(getPlayerPosition));

		// CRITICAL: Subscribe presentation layers to simulator events BEFORE loading data
		// This ensures they receive spawn events during initialization
		doors.Subscribe(simulator);
		walls.Subscribe(simulator);
		bonuses.Subscribe(simulator);
		actors.Subscribe(simulator);
		weapons.Subscribe(simulator);

		// Subscribe to elevator activation for level transitions
		_elevatorHandler = e => ElevatorActivated?.Invoke(e);
		simulator.ElevatorActivated += _elevatorHandler;

		// Forward player state changes to presentation layer for HUD updates
		_playerStateHandler = e => PlayerStateChanged?.Invoke(e);
		simulator.PlayerStateChanged += _playerStateHandler;

		// Forward screen flash events to presentation layer
		_screenFlashHandler = e => ScreenFlash?.Invoke(e);
		simulator.ScreenFlash += _screenFlashHandler;

		// Forward menu navigation events (VictoryTile, quiz triggers, etc.)
		_navigateToMenuHandler = e => NavigateToMenu?.Invoke(e);
		simulator.NavigateToMenu += _navigateToMenuHandler;

		// Initialize inventory and weapon slots before loading actors
		// (difficulty filtering depends on inventory, EquipWeapon depends on slots)
		if (statusBar != null)
			simulator.InitializeInventory(statusBar, difficulty, 1, weaponCollection, savedInventory, savedLevelStats);

		// Load doors into simulator (no spawn events - doors are fixed count)
		// IMPORTANT: This must be called first to initialize spatial index arrays
		simulator.LoadDoorsFromMapAnalysis(mapAnalyzer, mapAnalysis, mapAnalysis.Doors);
		// Load pushwalls into simulator (no spawn events - pushwalls are fixed count)
		simulator.LoadPushWallsFromMapAnalysis(mapAnalysis.PushWalls);

		// Load item scripts from game configuration
		// Scripts define conditional pickup behavior (e.g., health only if needed)
		(System.Collections.Generic.Dictionary<string, string> scripts,
		 System.Collections.Generic.Dictionary<byte, string> itemNumberToScript) = mapAnalyzer.GetItemScripts();
		simulator.LoadItemScripts(scripts, itemNumberToScript);

		// Load bonuses into simulator - emits BonusSpawnedEvent for each bonus
		// VR layer receives these events and displays bonuses
		simulator.LoadBonusesFromMapAnalysis(mapAnalysis);

		// VR uses pixel-perfect aiming - disable distance-based miss chance
		simulator.UseAccuracyFalloff = false;

		// Load actors into simulator - emits ActorSpawnedEvent for each actor
		// VR layer receives these events and creates actor visuals
		// HP and initial states are now read from ActorDefinition in XML
		simulator.LoadActorsFromMapAnalysis(mapAnalysis);

		if (weaponCollection == null || weaponCollection.Weapons.Count == 0)
			GD.PrintErr("WARNING: No WeaponCollection provided - weapons will not function");
	}

	/// <summary>
	/// Initializes the controller with an existing simulator (for resuming a suspended game).
	/// Does NOT create new RNG/GameClock/Simulator - reuses the existing one.
	/// Subscribes presentation layers and replays current state.
	/// </summary>
	public void InitializeFromExisting(
		Simulator.Simulator existingSimulator,
		Doors doorsNode,
		Walls wallsNode,
		Bonuses bonusesNode,
		Actors actorsNode,
		Weapons weaponsNode,
		Func<(int X, int Y, short Angle)> getPlayerPosition)
	{
		// Simulation pauses during fade transitions while presentation layer keeps rendering
		ProcessMode = ProcessModeEnum.Pausable;

		simulator = existingSimulator ?? throw new ArgumentNullException(nameof(existingSimulator));
		doors = doorsNode ?? throw new ArgumentNullException(nameof(doorsNode));
		walls = wallsNode ?? throw new ArgumentNullException(nameof(wallsNode));
		bonuses = bonusesNode ?? throw new ArgumentNullException(nameof(bonusesNode));
		actors = actorsNode ?? throw new ArgumentNullException(nameof(actorsNode));
		weapons = weaponsNode ?? throw new ArgumentNullException(nameof(weaponsNode));
		this.getPlayerPosition = getPlayerPosition ?? throw new ArgumentNullException(nameof(getPlayerPosition));

		// Subscribe presentation layers to existing simulator
		doors.Subscribe(simulator);
		walls.Subscribe(simulator);
		bonuses.Subscribe(simulator);
		actors.Subscribe(simulator);
		weapons.Subscribe(simulator);

		// Subscribe event forwarding handlers
		_elevatorHandler = e => ElevatorActivated?.Invoke(e);
		simulator.ElevatorActivated += _elevatorHandler;
		_playerStateHandler = e => PlayerStateChanged?.Invoke(e);
		simulator.PlayerStateChanged += _playerStateHandler;
		_screenFlashHandler = e => ScreenFlash?.Invoke(e);
		simulator.ScreenFlash += _screenFlashHandler;
		_navigateToMenuHandler = e => NavigateToMenu?.Invoke(e);
		simulator.NavigateToMenu += _navigateToMenuHandler;

		// Replay current state to newly subscribed presentation layers
		simulator.EmitAllEntityState();
	}

	public override void _ExitTree()
	{
		if (simulator != null)
		{
			if (_elevatorHandler != null)
				simulator.ElevatorActivated -= _elevatorHandler;
			if (_playerStateHandler != null)
				simulator.PlayerStateChanged -= _playerStateHandler;
			if (_screenFlashHandler != null)
				simulator.ScreenFlash -= _screenFlashHandler;
			if (_navigateToMenuHandler != null)
				simulator.NavigateToMenu -= _navigateToMenuHandler;
		}
	}

	/// <summary>
	/// Godot process loop - drives the simulator forward in time.
	/// WL_PLAY.C:PlayLoop equivalent
	/// </summary>
	public override void _Process(double delta)
	{
		if (simulator == null || getPlayerPosition == null)
			return;

		// Get player position and angle from delegate (Wolf3D 16.16 fixed-point coordinates, 0-359 angle)
		(int playerX, int playerY, short playerAngle) = getPlayerPosition();

		// Update simulator with elapsed time, player position, and angle
		// Events are automatically dispatched to subscribers (Doors, Bonuses, etc.)
		simulator.Update(delta, playerX, playerY, playerAngle);
	}

	/// <summary>
	/// Player attempts to operate (open/close) a door.
	/// Call this from player input handling (e.g., "use" button pressed near a door).
	/// WL_ACT1.C:OperateDoor
	/// </summary>
	/// <param name="doorIndex">Index of the door to operate</param>
	public void OperateDoor(ushort doorIndex)
	{
		if (simulator == null)
			return;

		simulator.QueueAction(new OperateDoorAction
		{
			DoorIndex = doorIndex
		});
	}

	/// <summary>
	/// Player attempts to push a pushwall.
	/// Call this from player input handling (e.g., "use" button pressed near a pushwall).
	/// Based on WL_ACT1.C pushwall activation logic.
	/// </summary>
	/// <param name="tileX">Tile X coordinate of the pushwall</param>
	/// <param name="tileY">Tile Y coordinate of the pushwall</param>
	/// <param name="direction">Direction the player is facing (pushwall moves away from player)</param>
	public void ActivatePushWall(ushort tileX, ushort tileY, Direction direction)
	{
		if (simulator == null)
			return;

		simulator.QueueAction(new ActivatePushWallAction
		{
			TileX = tileX,
			TileY = tileY,
			Direction = direction
		});
	}

	/// <summary>
	/// Player attempts to activate an elevator switch.
	/// Call this from player input handling (e.g., "use" button pressed near an elevator).
	/// Based on WL_AGENT.C:Cmd_Use elevator activation logic.
	/// </summary>
	/// <param name="tileX">Tile X coordinate of the elevator switch</param>
	/// <param name="tileY">Tile Y coordinate of the elevator switch</param>
	/// <param name="direction">Direction the player is facing (determines which face is activated)</param>
	public void ActivateElevator(ushort tileX, ushort tileY, Direction direction)
	{
		if (simulator == null)
			return;

		simulator.QueueAction(new ActivateElevatorAction
		{
			TileX = tileX,
			TileY = tileY,
			Direction = direction
		});
	}

	/// <summary>
	/// Player fires weapon in specified slot.
	/// Call this from player input handling (e.g., trigger press or X key).
	/// Hit detection is handled by the weapon state machine via HitDetection callback.
	/// Based on WL_AGENT.C:Cmd_Fire.
	/// </summary>
	/// <param name="slotIndex">Weapon slot index (0 = primary/left, 1 = secondary/right)</param>
	public void FireWeapon(int slotIndex)
	{
		if (simulator == null)
			return;

		simulator.QueueAction(new FireWeaponAction
		{
			SlotIndex = slotIndex,
		});
	}

	/// <summary>
	/// Player releases weapon trigger in specified slot.
	/// Call this from player input handling when trigger/button is released.
	/// Required for semi-auto weapons to fire again.
	/// Based on WL_AGENT.C button held tracking.
	/// </summary>
	/// <param name="slotIndex">Weapon slot index (0 = primary/left, 1 = secondary/right)</param>
	public void ReleaseWeaponTrigger(int slotIndex)
	{
		if (simulator == null)
			return;

		simulator.QueueAction(new ReleaseWeaponTriggerAction
		{
			SlotIndex = slotIndex
		});
	}

	/// <summary>
	/// Player switches to a different weapon in specified slot.
	/// Call this from player input handling (e.g., number keys 1-4).
	/// Based on WL_AGENT.C weapon selection (bt_readyknife, bt_readypistol, etc.)
	/// </summary>
	/// <param name="slotIndex">Weapon slot index (0 = primary/left, 1 = secondary/right)</param>
	/// <param name="weaponType">Weapon type identifier (e.g., "knife", "pistol", "machinegun", "chaingun")</param>
	public void SwitchWeapon(int slotIndex, string weaponType)
	{
		if (simulator == null)
			return;

		simulator.QueueAction(new EquipWeaponAction
		{
			SlotIndex = slotIndex,
			WeaponType = weaponType
		});
	}

	/// <summary>
	/// Gets the current simulation time in tics.
	/// Useful for debugging or time-based game mechanics.
	/// </summary>
	public long CurrentTic => simulator?.CurrentTic ?? 0;

	/// <summary>
	/// Gets the list of doors in the simulator.
	/// Read-only access to door states.
	/// </summary>
	public IReadOnlyList<Door> Doors => simulator?.Doors;
	/// <summary>
	/// Gets the list of pushwalls in the simulator.
	/// Read-only access to pushwall states.
	/// </summary>
	public IReadOnlyList<PushWall> PushWalls => simulator?.PushWalls;

	#region Player Movement Validation
	/// <summary>
	/// Validates a desired movement and returns the adjusted position.
	/// Call this before applying position changes to check for collisions.
	/// Implements wall-sliding: if blocked in one axis, tries sliding along the other.
	/// </summary>
	/// <param name="currentX">Current X position in meters (Godot X)</param>
	/// <param name="currentZ">Current Z position in meters (Godot Z = Wolf3D Y)</param>
	/// <param name="desiredX">Desired X position in meters</param>
	/// <param name="desiredZ">Desired Z position in meters</param>
	/// <returns>Validated position (X, Z) in meters</returns>
	public (float X, float Z) ValidateMovement(float currentX, float currentZ, float desiredX, float desiredZ)
	{
		if (simulator == null)
			return (desiredX, desiredZ);

		// Convert meters to fixed-point
		int currentFixedX = currentX.ToFixedPoint();
		int currentFixedY = currentZ.ToFixedPoint();  // Godot Z = Wolf3D Y
		int desiredFixedX = desiredX.ToFixedPoint();
		int desiredFixedY = desiredZ.ToFixedPoint();

		// HeadXZ in fixed-point (meters to fixed-point)
		int headSize = Constants.HeadXZ.ToFixedPoint();

		// Validate through simulator
		(int validX, int validY) = simulator.ValidatePlayerMove(
			currentFixedX, currentFixedY,
			desiredFixedX, desiredFixedY,
			headSize);

		// Convert back to meters (Wolf3D Y back to Godot Z)
		return (validX.ToMeters(), validY.ToMeters());
	}

	/// <summary>
	/// Sets or clears NoClip mode. When enabled, player can walk through walls.
	/// Based on original Wolf3D "MLI" debug/cheat command.
	/// </summary>
	/// <param name="enabled">True to enable NoClip, false to disable</param>
	public void SetNoClip(bool enabled)
	{
		if (simulator != null)
			simulator.NoClip = enabled;
	}

	/// <summary>
	/// Gets the current NoClip mode state.
	/// </summary>
	/// <returns>True if NoClip is enabled</returns>
	public bool GetNoClip() => simulator?.NoClip ?? false;
	#endregion

	/// <summary>
	/// Event fired when an elevator is activated, triggering level transition.
	/// WL_AGENT.C:Cmd_Use elevator activation logic (line 1767).
	/// </summary>
	public event Action<ElevatorActivatedEvent> ElevatorActivated;

	/// <summary>
	/// Event fired when player state changes (health, ammo, score, lives, keys).
	/// Used to update HUD/status bar in presentation layer.
	/// </summary>
	public event Action<PlayerStateChangedEvent> PlayerStateChanged;

	/// <summary>
	/// Event fired when a screen flash effect should be displayed.
	/// WL_PLAY.C: Bonus flash, damage flash, etc.
	/// </summary>
	public event Action<ScreenFlashEvent> ScreenFlash;

	/// <summary>
	/// Event fired when an item script requests navigation to a named menu screen.
	/// Generic mechanism for VictoryTile, Bible quiz, or any menu-triggering item.
	/// </summary>
	public event Action<NavigateToMenuEvent> NavigateToMenu;
}
