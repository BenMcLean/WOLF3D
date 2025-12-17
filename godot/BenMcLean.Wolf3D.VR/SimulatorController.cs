using Godot;
using System;
using System.Collections.Generic;
using BenMcLean.Wolf3D.Simulator;
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
	private Bonuses bonuses;

	/// <summary>
	/// Initializes the simulator with door and bonus data from MapAnalysis.
	/// Call this after adding the controller to the scene tree.
	/// </summary>
	/// <param name="mapAnalysis">Map analysis containing all spawn data</param>
	/// <param name="doorsNode">The Doors node that will render the doors</param>
	/// <param name="bonusesNode">The Bonuses node that will render bonus items</param>
	public void Initialize(
		MapAnalysis mapAnalysis,
		Doors doorsNode,
		Bonuses bonusesNode)
	{
		simulator = new Simulator.Simulator();
		doors = doorsNode ?? throw new ArgumentNullException(nameof(doorsNode));
		bonuses = bonusesNode ?? throw new ArgumentNullException(nameof(bonusesNode));

		// Load doors into simulator
		simulator.LoadDoorsFromMapAnalysis(mapAnalysis.Doors);

		// Load static bonuses into simulator for gameplay tracking
		// VR layer displays them directly from MapAnalysis - no events needed
		simulator.LoadBonusesFromMapAnalysis(mapAnalysis);

		// Subscribe presentation layers to simulator events
		doors.Subscribe(simulator);
		bonuses.Subscribe(simulator);
	}

	/// <summary>
	/// Godot process loop - drives the simulator forward in time.
	/// WL_PLAY.C:PlayLoop equivalent
	/// </summary>
	public override void _Process(double delta)
	{
		if (simulator == null)
			return;

		// Update simulator with elapsed time
		// Events are automatically dispatched to subscribers (Doors, Bonuses, etc.)
		simulator.Update(delta);
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
	/// Gets the current simulation time in tics.
	/// Useful for debugging or time-based game mechanics.
	/// </summary>
	public long CurrentTic => simulator?.CurrentTic ?? 0;

	/// <summary>
	/// Gets the list of doors in the simulator.
	/// Read-only access to door states.
	/// </summary>
	public IReadOnlyList<Simulator.Door> Doors => simulator?.Doors;
}
