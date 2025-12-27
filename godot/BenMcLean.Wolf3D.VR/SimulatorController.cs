using Godot;
using System;
using System.Collections.Generic;
using BenMcLean.Wolf3D.Simulator;
using BenMcLean.Wolf3D.Assets;
using static BenMcLean.Wolf3D.Assets.MapAnalyzer;

namespace BenMcLean.Wolf3D.VR;

/// <summary>
/// Godot controller that embeds the Wolf3D discrete event simulator.
/// Drives simulation from Godot's _Process loop and updates VR presentation based on events.
/// </summary>
public partial class SimulatorController : Node3D
{
	private Simulator.Simulator simulator;
	private Doors doors;
	private Walls walls;
	private Bonuses bonuses;
	private Actors actors;
	private Func<(int X, int Y)> getPlayerPosition; // Delegate returns Wolf3D 16.16 fixed-point coordinates

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
	/// <param name="stateCollection">State collection loaded from game XML (e.g., WL1.xml)</param>
	/// <param name="getPlayerPosition">Delegate that returns player position in Wolf3D 16.16 fixed-point coordinates (X, Y)</param>
	public void Initialize(
		MapAnalyzer mapAnalyzer,
		MapAnalysis mapAnalysis,
		Doors doorsNode,
		Walls wallsNode,
		Bonuses bonusesNode,
		Actors actorsNode,
		StateCollection stateCollection,
		Func<(int X, int Y)> getPlayerPosition)
	{
		// TODO: Load stateCollection from WL1.xml or game data file
		// For now, create a placeholder if none provided
		if (stateCollection == null)
		{
			GD.PrintErr("WARNING: No StateCollection provided - creating empty collection (actors will not function)");
			stateCollection = new StateCollection();
		}

		// Create deterministic RNG and GameClock
		RNG rng = new(0); // TODO: Use seed from game settings or save file
		GameClock gameClock = new();

		simulator = new Simulator.Simulator(stateCollection, rng, gameClock);
		doors = doorsNode ?? throw new ArgumentNullException(nameof(doorsNode));
		walls = wallsNode ?? throw new ArgumentNullException(nameof(wallsNode));
		bonuses = bonusesNode ?? throw new ArgumentNullException(nameof(bonusesNode));
		actors = actorsNode ?? throw new ArgumentNullException(nameof(actorsNode));
		this.getPlayerPosition = getPlayerPosition ?? throw new ArgumentNullException(nameof(getPlayerPosition));

		// CRITICAL: Subscribe presentation layers to simulator events BEFORE loading data
		// This ensures they receive spawn events during initialization
		doors.Subscribe(simulator);
		walls.Subscribe(simulator);
		bonuses.Subscribe(simulator);
		actors.Subscribe(simulator);

		// Load doors into simulator (no spawn events - doors are fixed count)
		// IMPORTANT: This must be called first to initialize spatial index arrays
		simulator.LoadDoorsFromMapAnalysis(mapAnalyzer, mapAnalysis, mapAnalysis.Doors);
		// Load pushwalls into simulator (no spawn events - pushwalls are fixed count)
		simulator.LoadPushWallsFromMapAnalysis(mapAnalysis.PushWalls);

		// Load bonuses into simulator - emits BonusSpawnedEvent for each bonus
		// VR layer receives these events and displays bonuses
		simulator.LoadBonusesFromMapAnalysis(mapAnalysis);

		// Load actors into simulator - emits ActorSpawnedEvent for each actor
		// VR layer receives these events and creates actor visuals
		// TODO: Load actorInitialStates and actorHitPoints from game data
		Dictionary<string, string> actorInitialStates = new Dictionary<string, string>
		{
			// TODO: These mappings should come from game data file
			{ "guard", "s_grdstand" },
			{ "ss", "s_ssstand" },
			{ "dog", "s_dogstand" },
			{ "officer", "s_ofcstand" },
			{ "mutant", "s_mutstand" }
		};
		Dictionary<string, short> actorHitPoints = new Dictionary<string, short>
		{
			// TODO: These values should come from game data file
			{ "guard", 25 },
			{ "ss", 100 },
			{ "dog", 1 },
			{ "officer", 50 },
			{ "mutant", 55 }
		};
		simulator.LoadActorsFromMapAnalysis(mapAnalysis, actorInitialStates, actorHitPoints);
	}

	/// <summary>
	/// Godot process loop - drives the simulator forward in time.
	/// WL_PLAY.C:PlayLoop equivalent
	/// </summary>
	public override void _Process(double delta)
	{
		if (simulator == null || getPlayerPosition == null)
			return;

		// Get player position from delegate (Wolf3D 16.16 fixed-point coordinates)
		(int playerX, int playerY) = getPlayerPosition();

		// Update simulator with elapsed time and player position
		// Events are automatically dispatched to subscribers (Doors, Bonuses, etc.)
		simulator.Update(delta, playerX, playerY);
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
	public void ActivatePushWall(ushort tileX, ushort tileY, Simulator.Direction direction)
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
	/// Gets the current simulation time in tics.
	/// Useful for debugging or time-based game mechanics.
	/// </summary>
	public long CurrentTic => simulator?.CurrentTic ?? 0;

	/// <summary>
	/// Gets the list of doors in the simulator.
	/// Read-only access to door states.
	/// </summary>
	public IReadOnlyList<Simulator.Door> Doors => simulator?.Doors;
	/// <summary>
	/// Gets the list of pushwalls in the simulator.
	/// Read-only access to pushwall states.
	/// </summary>
	public IReadOnlyList<Simulator.PushWall> PushWalls => simulator?.PushWalls;
}
