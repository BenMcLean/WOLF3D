namespace BenMcLean.Wolf3D.Simulator;

/// <summary>
/// Core simulation constants for Wolf3D timing and gameplay.
/// </summary>
public static class Constants
{
	#region Gameplay Timing
	/// <summary>
	/// How long a pushwall takes to move one full tile (in tics).
	/// WL_ACT1.C: Pushwalls move at a consistent speed over 128 tics per tile.
	/// Two-tile push takes 256 tics total to maintain original speed.
	/// </summary>
	public const short PushTicsPerTile = 128,
		/// <summary>
		/// How long a door stays open before auto-closing (in tics).
		/// WL_ACT1.C: Doors stay open for 300 tics (~4.3 seconds at 70 Hz).
		/// </summary>
		DoorOpenTics = 300;
	#endregion Gameplay Timing
}
