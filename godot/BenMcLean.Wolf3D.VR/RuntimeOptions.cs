using System;
using System.Linq;
using Godot;

namespace BenMcLean.Wolf3D.VR;

/// <summary>
/// Centralized runtime option parsing for command-line flags and environment overrides.
/// </summary>
public static class RuntimeOptions
{
	public static readonly Vector2I SpectatorResolution = new(1920, 1080);

	/// <summary>
	/// Returns true when the desktop spectator view should replace the default VR mirror.
	/// Disabled by default because it adds an extra 3D render pass.
	/// Enable with --spectator or WOLF3D_VR_SPECTATOR=1/true/yes/on.
	/// </summary>
	public static bool SpectatorViewEnabled
	{
		get
		{
			string[] args = OS.GetCmdlineArgs();
			if (args.Contains("--spectator"))
				return true;
			if (args.Contains("--no-spectator"))
				return false;

			string env = System.Environment.GetEnvironmentVariable("WOLF3D_VR_SPECTATOR");
			return IsTruthy(env);
		}
	}

	/// <summary>
	/// Applies the spectator capture window size when spectator mode is enabled.
	/// This keeps the main viewport at a stable 1080p for window capture and MovieWriter.
	/// </summary>
	public static void ApplyWindowConfiguration()
	{
		if (!SpectatorViewEnabled)
			return;

		DisplayServer.WindowSetSize(SpectatorResolution);
		DisplayServer.WindowSetMinSize(SpectatorResolution);
		DisplayServer.WindowSetMaxSize(SpectatorResolution);
		DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.ResizeDisabled, true);
		DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.MaximizeDisabled, true);
	}

	private static bool IsTruthy(string value) =>
		!string.IsNullOrWhiteSpace(value) &&
		value.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";
}
