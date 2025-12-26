namespace BenMcLean.Wolf3D.Shared;

public static class Constants
{
	#region Colors
	public const uint Red = 0xFF0000FFu,
		Yellow = 0xFFFF00FFu,
		Black = 0x000000FFu,
		White = 0xFFFFFFFFu,
		Green = 0x00FF00FFu,
		Blue = 0x0000FFFFu,
		Orange = 0xFFA500FFu,
		Indigo = 0x4B0082FFu,
		Violet = 0x8F00FFFFu,
		Purple = 0x800080FFu,
		Magenta = 0xFF00FFFFu,
		Cyan = 0x00FFFFFFu;
	#endregion Colors
	#region Time
	/// <summary>
	/// Wolf3D runs at 70 tics per second
	/// </summary>
	public const double TicsPerSecond = 70.0,
		/// <summary>
		/// Duration of one Wolf3D tic in seconds
		/// </summary>
		SecondsPerTic = 1.0 / TicsPerSecond;
	/// <summary>
	/// C# TimeSpan uses 100-nanosecond ticks (10,000,000 per second)
	/// </summary>
	public const long TimeSpanTicksPerSecond = 10_000_000L,
		/// <summary>
		/// Number of TimeSpan ticks in one Wolf3D tic
		/// </summary>
		TimeSpanTicksPerTic = (long)(TimeSpanTicksPerSecond / TicsPerSecond);
	/// <summary>
	/// Number of Wolf3D tics per TimeSpan tick
	/// </summary>
	public const double TicsPerTimeSpanTick = TicsPerSecond / TimeSpanTicksPerSecond;
	#endregion Time
}
