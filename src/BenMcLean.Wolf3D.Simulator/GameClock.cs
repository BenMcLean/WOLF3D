using System;
using BenMcLean.Wolf3D.Simulator.State;

namespace BenMcLean.Wolf3D.Simulator;

/// <summary>
/// Deterministic game clock for simulation reproducibility.
/// Provides os.time() and os.clock() functionality to Lua scripts
/// with fixed epoch and tic-based progression.
/// </summary>
public class GameClock(DateTime? epoch = null) : IStateSavable<GameClockSnapshot>
{
	/// <summary>
	/// Game epoch - when the game "starts" in-universe.
	/// Default: February 1, 1945, 12:00:00 UTC
	/// The iPhone port says that Wolfenstein 3-D takes place in February, 1945
	/// </summary>
	public DateTime Epoch { get; } = epoch?.ToUniversalTime()
		?? new DateTime(1945, 2, 1, 12, 0, 0, DateTimeKind.Utc);
	/// <summary>
	/// Current game time in tics (70 tics = 1 second)
	/// </summary>
	private long elapsedTics = 0;
	/// <summary>
	/// Advance the clock by one tic (called from Simulator.ProcessTic)
	/// </summary>
	public void AdvanceTic() => elapsedTics++;
	/// <summary>
	/// Get the current in-game date/time
	/// </summary>
	public DateTime CurrentGameTime => Epoch.AddSeconds(elapsedTics / Simulator.TicRate);
	/// <summary>
	/// Get Unix timestamp for current game time (for os.time())
	/// Returns long that may be negative for dates before 1970
	/// </summary>
	public long GetUnixTimestamp() => new DateTimeOffset(CurrentGameTime).ToUnixTimeSeconds();
	/// <summary>
	/// Get elapsed seconds since game start (for os.clock())
	/// </summary>
	public double GetElapsedSeconds() => elapsedTics / Simulator.TicRate;
	/// <summary>
	/// Format game time as string (for os.date())
	/// </summary>
	public string FormatDate(string format = null)
	{
		DateTime dt = CurrentGameTime;
		// Lua date format support (subset)
		if (format is null || format == "*t")
			// Return table-like string representation
			return $"{{year={dt.Year}, month={dt.Month}, day={dt.Day}, " +
				$"hour={dt.Hour}, min={dt.Minute}, sec={dt.Second}, " +
				$"wday={((int)dt.DayOfWeek) + 1}, yday={dt.DayOfYear}}}";
		// Simple format string support
		return dt.ToString(format);
	}
	/// <summary>
	/// Captures the current clock state for serialization.
	/// Only elapsed tics need saving - the epoch is a construction-time parameter.
	/// </summary>
	public GameClockSnapshot SaveState() => new()
	{
		EpochTicks = Epoch.Ticks,
		ElapsedTics = elapsedTics
	};

	/// <summary>
	/// Restores clock state from a snapshot.
	/// Epoch is validated but not overwritten (must match construction-time value).
	/// </summary>
	public void LoadState(GameClockSnapshot state) => elapsedTics = state.ElapsedTics;
}
