using System;
using System.Collections.Generic;
using BenMcLean.Wolf3D.Assets;
using static BenMcLean.Wolf3D.Assets.MapAnalyzer;

namespace BenMcLean.Wolf3D.Simulator;

/// <summary>
/// Discrete event simulator for Wolfenstein 3D game logic.
/// Runs at 70Hz tic rate matching the original game (WL_DRAW.C:CalcTics).
/// </summary>
public class Simulator
{
	public const double TicRate = 70.0; // Hz
	public const double TicDuration = 1.0 / TicRate; // ~14.2857ms
	public const int MaxTicsPerUpdate = 10; // Prevent spiral of death

	private double accumulatedTime;
	private readonly List<PlayerAction> pendingActions = [];
	private readonly List<ISimulationEvent> eventQueue = [];

	public IReadOnlyList<Door> Doors => doors;
	private readonly List<Door> doors = [];

	// WL_PLAY.C:tics (unsigned = 16-bit in original DOS, but we accumulate as long)
	// Current simulation time in tics
	public long CurrentTic { get; private set; }

	/// <summary>
	/// Update the simulation with elapsed real time.
	/// Returns all events that occurred during this update.
	/// Based on WL_PLAY.C:PlayLoop and WL_DRAW.C:CalcTics.
	/// </summary>
	public IReadOnlyList<ISimulationEvent> Update(double deltaTime)
	{
		eventQueue.Clear();
		accumulatedTime += deltaTime;

		// WL_DRAW.C:CalcTics - calculate tics since last refresh
		int ticsToProcess = (int)(accumulatedTime / TicDuration);
		if (ticsToProcess > MaxTicsPerUpdate)
			ticsToProcess = MaxTicsPerUpdate;

		accumulatedTime -= ticsToProcess * TicDuration;

		// WL_PLAY.C:PlayLoop - process each tic
		for (int i = 0; i < ticsToProcess; i++)
		{
			ProcessTic();
			CurrentTic++;
		}

		return eventQueue;
	}

	/// <summary>
	/// Queue a player action to be processed on the next tic.
	/// Ensures determinism by quantizing inputs to tic boundaries.
	/// </summary>
	public void QueueAction(PlayerAction action)
	{
		pendingActions.Add(action);
	}

	private void ProcessTic()
	{
		// Process queued player actions
		foreach (var action in pendingActions)
		{
			ProcessAction(action);
		}
		pendingActions.Clear();

		// WL_ACT1.C:MoveDoors - update all doors
		for (int i = 0; i < doors.Count; i++)
		{
			UpdateDoor(i);
		}
	}

	private void ProcessAction(PlayerAction action)
	{
		if (action is OperateDoorAction operateDoor)
		{
			OperateDoor(operateDoor.DoorIndex);
		}
	}

	/// <summary>
	/// WL_ACT1.C:OperateDoor (line 644)
	/// The player wants to change the door's direction
	/// </summary>
	private void OperateDoor(ushort doorIndex)
	{
		if (doorIndex >= doors.Count)
			return;

		var door = doors[doorIndex];

		// WL_ACT1.C:OperateDoor lines 658-668 - toggle door state
		switch (door.Action)
		{
			case DoorAction.Closed:
			case DoorAction.Closing:
				OpenDoor(doorIndex);
				break;

			case DoorAction.Open:
			case DoorAction.Opening:
				CloseDoor(doorIndex);
				break;
		}
	}

	/// <summary>
	/// WL_ACT1.C:OpenDoor (line 546)
	/// </summary>
	private void OpenDoor(ushort doorIndex)
	{
		var door = doors[doorIndex];

		if (door.Action == DoorAction.Open)
		{
			// Door already open, just reset the timer (WL_ACT1.C:549)
			door.TicCount = 0;
		}
		else
		{
			// Start opening (WL_ACT1.C:551)
			door.Action = DoorAction.Opening;
		}
	}

	/// <summary>
	/// WL_ACT1.C:CloseDoor (line 563)
	/// </summary>
	private void CloseDoor(ushort doorIndex)
	{
		var door = doors[doorIndex];

		// TODO: Check for blocking actors/player (WL_ACT1.C:574-611)
		// For now, just start closing
		door.Action = DoorAction.Closing;

		eventQueue.Add(new DoorClosingEvent
		{
			Timestamp = CurrentTic * TicDuration,
			DoorIndex = doorIndex,
			TileX = door.TileX,
			TileY = door.TileY
		});
	}

	/// <summary>
	/// WL_ACT1.C:MoveDoors (line 832)
	/// Called from PlayLoop
	/// </summary>
	private void UpdateDoor(int doorIndex)
	{
		var door = doors[doorIndex];

		// WL_ACT1.C:MoveDoors lines 842-856
		switch (door.Action)
		{
			case DoorAction.Open:
				UpdateDoorOpen(doorIndex);
				break;

			case DoorAction.Opening:
				UpdateDoorOpening(doorIndex);
				break;

			case DoorAction.Closing:
				UpdateDoorClosing(doorIndex);
				break;

			case DoorAction.Closed:
				// Nothing to do
				break;
		}
	}

	/// <summary>
	/// WL_ACT1.C:DoorOpen (line 684)
	/// Close the door after three seconds
	/// </summary>
	private void UpdateDoorOpen(int doorIndex)
	{
		var door = doors[doorIndex];

		// WL_ACT1.C:686 - accumulate tics
		door.TicCount += 1; // Always 1 tic per call in our simplified version

		// WL_ACT1.C:686 - check if time to close
		if (door.TicCount >= Door.OpenTics)
		{
			CloseDoor((ushort)doorIndex);
		}
	}

	/// <summary>
	/// WL_ACT1.C:DoorOpening (line 700)
	/// </summary>
	private void UpdateDoorOpening(int doorIndex)
	{
		var door = doors[doorIndex];
		int newPosition = door.Position;

		// WL_ACT1.C:707 - door just starting to open
		if (newPosition == 0)
		{
			// Emit opening event
			eventQueue.Add(new DoorOpeningEvent
			{
				Timestamp = CurrentTic * TicDuration,
				DoorIndex = (ushort)doorIndex,
				TileX = door.TileX,
				TileY = door.TileY
			});

			// TODO: WL_ACT1.C:710-733 - connect areas for sound/sight
		}

		// WL_ACT1.C:739 - slide the door by an adaptive amount
		// position += tics<<10 (we use 1 tic per update)
		newPosition += 1 << 10;

		// WL_ACT1.C:740 - check if fully open
		if (newPosition >= 0xFFFF)
		{
			// Door fully open (WL_ACT1.C:742-748)
			newPosition = 0xFFFF;
			door.Position = (ushort)newPosition;
			door.TicCount = 0;
			door.Action = DoorAction.Open;

			eventQueue.Add(new DoorOpenedEvent
			{
				Timestamp = CurrentTic * TicDuration,
				DoorIndex = (ushort)doorIndex,
				TileX = door.TileX,
				TileY = door.TileY
			});

			// TODO: WL_ACT1.C:748 - clear actorat for door tile
		}
		else
		{
			door.Position = (ushort)newPosition;
		}

		// Emit position changed event every tic
		eventQueue.Add(new DoorPositionChangedEvent
		{
			Timestamp = CurrentTic * TicDuration,
			DoorIndex = (ushort)doorIndex,
			TileX = door.TileX,
			TileY = door.TileY,
			Position = door.Position,
			Action = door.Action
		});
	}

	/// <summary>
	/// WL_ACT1.C:DoorClosing (line 763)
	/// </summary>
	private void UpdateDoorClosing(int doorIndex)
	{
		var door = doors[doorIndex];
		int newPosition = door.Position;

		// TODO: WL_ACT1.C:773-778 - check if something is blocking the door
		// If blocked, call OpenDoor(doorIndex) and return

		// WL_ACT1.C:785 - slide the door by an adaptive amount
		// position -= tics<<10 (we use 1 tic per update)
		newPosition -= 1 << 10;

		// WL_ACT1.C:786 - check if fully closed
		if (newPosition <= 0)
		{
			// Door fully closed (WL_ACT1.C:788-813)
			newPosition = 0;
			door.Position = (ushort)newPosition;
			door.Action = DoorAction.Closed;

			eventQueue.Add(new DoorClosedEvent
			{
				Timestamp = CurrentTic * TicDuration,
				DoorIndex = (ushort)doorIndex,
				TileX = door.TileX,
				TileY = door.TileY
			});

			// TODO: WL_ACT1.C:795-813 - disconnect areas
		}
		else
		{
			door.Position = (ushort)newPosition;
		}

		// Emit position changed event every tic
		eventQueue.Add(new DoorPositionChangedEvent
		{
			Timestamp = CurrentTic * TicDuration,
			DoorIndex = (ushort)doorIndex,
			TileX = door.TileX,
			TileY = door.TileY,
			Position = door.Position,
			Action = door.Action
		});
	}

	/// <summary>
	/// Initialize doors from MapAnalyzer data.
	/// Looks up door properties directly from MapAnalyzer.Doors dictionary.
	/// </summary>
	public void LoadDoorsFromMapAnalysis(
		IEnumerable<MapAnalysis.DoorSpawn> doorSpawns)
	{
		doors.Clear();
		foreach (MapAnalysis.DoorSpawn spawn in doorSpawns)
			doors.Add(new Door(spawn.X, spawn.Y, spawn.FacesEastWest));
	}
}
