using System;
using System.IO;
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
	/// Returns command-line arguments from both Godot's normal argument list and the
	/// user-argument list that appears after `--`.
	/// This allows project-specific options to coexist with engine options such as
	/// MovieWriter flags.
	/// </summary>
	public static string[] GetAllCommandLineArgs() => [.. OS.GetCmdlineArgs(), .. OS.GetCmdlineUserArgs()];

	/// <summary>
	/// Returns true when the desktop spectator view should replace the default VR mirror.
	/// Disabled by default because it adds an extra 3D render pass.
	/// Enable with --spectator or WOLF3D_VR_SPECTATOR=1/true/yes/on.
	/// </summary>
	public static bool SpectatorViewEnabled
	{
		get
		{
			string[] args = GetAllCommandLineArgs();
			if (args.Contains("--spectator"))
				return true;
			if (args.Contains("--no-spectator"))
				return false;
			return IsTruthy(System.Environment.GetEnvironmentVariable("WOLF3D_VR_SPECTATOR"));
		}
	}

	/// <summary>
	/// Returns the directory containing game XML definition files and game data subdirectories.
	/// Override with --path &lt;path&gt; or just a bare positional argument (absolute or relative
	/// to the executable directory). --path takes priority over a bare argument.
	/// Defaults:
	///   Android (Quest): /sdcard/WOLF3D
	///   Editor: ../../games relative to CWD (resolves to repo games/ folder)
	///   PC export: games/ subfolder next to the executable
	/// </summary>
	public static string Path
	{
		get
		{
			string[] args = GetAllCommandLineArgs();
			string positional = null;
			for (int i = 0; i < args.Length; i++)
			{
				if (args[i] == "--path" && i < args.Length - 1)
					return ResolveGamesPath(args[++i]);
				if (!args[i].StartsWith("--") && !args[i].StartsWith("uid:"))
					positional ??= args[i];
			}
			return positional != null ? ResolveGamesPath(positional) : DefaultGamesDir();
		}
	}

	private static string ResolveGamesPath(string path) =>
		System.IO.Path.IsPathRooted(path)
			? path
			: System.IO.Path.GetFullPath(path, System.IO.Path.GetDirectoryName(OS.GetExecutablePath()));

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

	private static string DefaultGamesDir() =>
		OS.HasFeature("android") ? "/sdcard/WOLF3D"
		: OS.HasFeature("editor") ? System.IO.Path.GetFullPath(@"..\..\games")
		: System.IO.Path.Combine(System.IO.Path.GetDirectoryName(OS.GetExecutablePath()), "games");

	private static bool IsTruthy(string value) =>
		!string.IsNullOrWhiteSpace(value) &&
		value.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";
}
