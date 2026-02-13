namespace BenMcLean.Wolf3D.Simulator.State;

/// <summary>
/// Serializable snapshot of GameClock state.
/// Only captures elapsed tics and epoch - derived values (CurrentGameTime, etc.)
/// are recalculated from these on restore.
///
/// EpochTicks stores DateTime.Ticks (100-nanosecond intervals since 0001-01-01 UTC)
/// for format-agnostic serialization instead of DateTime directly.
/// </summary>
public record GameClockSnapshot
{
	/// <summary>
	/// Game epoch as DateTime.Ticks (100-nanosecond intervals since 0001-01-01 UTC).
	/// Stored as long for format-agnostic serialization.
	/// Default: February 1, 1945, 12:00:00 UTC (the month Wolf3D takes place).
	/// </summary>
	public long EpochTicks { get; init; }

	/// <summary>
	/// Number of simulation tics elapsed since game start.
	/// 70 tics = 1 second (matching Wolf3D's original tic rate).
	/// All time-derived values are calculated from this on restore.
	/// </summary>
	public long ElapsedTics { get; init; }
}
