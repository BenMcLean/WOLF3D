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
		IReadOnlyList<ISimulationEvent> events = simulator.Update(delta);

		// Process all events that occurred this frame
		ProcessEvents(events);
	}

	/// <summary>
	/// Processes simulation events and updates VR presentation.
	/// </summary>
	private void ProcessEvents(IReadOnlyList<ISimulationEvent> events)
	{
		foreach (ISimulationEvent evt in events)
		{
			switch (evt)
			{
				case DoorOpeningEvent opening:
					HandleDoorOpening(opening);
					break;

				case DoorOpenedEvent opened:
					HandleDoorOpened(opened);
					break;

				case DoorPositionChangedEvent positionChanged:
					HandleDoorPositionChanged(positionChanged);
					break;

				case DoorClosingEvent closing:
					HandleDoorClosing(closing);
					break;

				case DoorClosedEvent closed:
					HandleDoorClosed(closed);
					break;

				case BonusSpawnedEvent bonusSpawned:
					HandleBonusSpawned(bonusSpawned);
					break;

				case BonusPickedUpEvent bonusPickedUp:
					HandleBonusPickedUp(bonusPickedUp);
					break;
			}
		}
	}

	private void HandleDoorOpening(DoorOpeningEvent evt)
	{
		// Update VR door position
		UpdateDoorPosition(evt.DoorIndex);

		// TODO: Play OPENDOORSND at (evt.TileX, evt.TileY)
		// PlaySoundLocTile(OPENDOORSND, evt.TileX, evt.TileY);
	}

	private void HandleDoorOpened(DoorOpenedEvent evt)
	{
		// Update VR door to fully open position
		UpdateDoorPosition(evt.DoorIndex);
	}

	private void HandleDoorPositionChanged(DoorPositionChangedEvent evt)
	{
		// Update VR door position during animation
		UpdateDoorPosition(evt.DoorIndex);
	}

	private void HandleDoorClosing(DoorClosingEvent evt)
	{
		// Update VR door position
		UpdateDoorPosition(evt.DoorIndex);

		// TODO: Play CLOSEDOORSND at (evt.TileX, evt.TileY)
		// PlaySoundLocTile(CLOSEDOORSND, evt.TileX, evt.TileY);
	}

	private void HandleDoorClosed(DoorClosedEvent evt)
	{
		// Update VR door to fully closed position
		UpdateDoorPosition(evt.DoorIndex);
	}


	/// <summary>
	/// Updates a door's visual position from the simulator state.
	/// Called whenever a door moves (opening, closing, or position changes).
	/// </summary>
	private void UpdateDoorPosition(ushort doorIndex)
	{
		if (doorIndex >= simulator.Doors.Count)
		{
			GD.PrintErr($"ERROR: doorIndex {doorIndex} >= simulator.Doors.Count {simulator.Doors.Count}");
			return;
		}

		Door door = simulator.Doors[doorIndex];

		if (doors == null)
		{
			GD.PrintErr($"ERROR: doors node is null!");
			return;
		}

		doors.MoveDoor(doorIndex, door.Position);
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

	private void HandleBonusSpawned(BonusSpawnedEvent evt)
	{
		// Update VR bonus display
		bonuses?.ShowBonus(evt.StatObjIndex, evt.Shape, evt.TileX, evt.TileY);

		// TODO: Play bonus spawn sound if applicable
		// PlaySoundLocTile(spawnSound, evt.TileX, evt.TileY);
	}

	private void HandleBonusPickedUp(BonusPickedUpEvent evt)
	{
		// Hide bonus from VR display
		bonuses?.HideBonus(evt.StatObjIndex);

		// TODO: Play pickup sound based on item type (WL_AGENT.C:GetBonus)
		// PlaySoundLocTile(pickupSound, evt.TileX, evt.TileY);

		// TODO: Show bonus flash effect
		// StartBonusFlash();
	}
}
