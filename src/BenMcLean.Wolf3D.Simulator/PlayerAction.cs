namespace BenMcLean.Wolf3D.Simulator;

/// <summary>
/// Base class for player actions that can be queued.
/// </summary>
public abstract class PlayerAction
{
}

/// <summary>
/// Player attempts to operate (open/close) a door.
/// </summary>
public class OperateDoorAction : PlayerAction
{
	public required ushort DoorIndex { get; init; }
}
