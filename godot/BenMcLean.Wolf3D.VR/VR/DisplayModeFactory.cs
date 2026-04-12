using System;
using System.Linq;
using Godot;

namespace BenMcLean.Wolf3D.VR.VR;

/// <summary>
/// Factory for creating the appropriate display mode (VR or flatscreen).
/// Attempts to initialize OpenXR, falls back to flatscreen if unavailable.
///
/// Supports command-line arguments to override behavior:
///   --flatscreen or --no-vr : Force flatscreen mode (skip OpenXR initialization)
///   --5dof                  : Force VR mode, lock camera height to HalfTileHeight (default for VR)
///   --roomscale             : Force VR mode, allow real-world head height
///
/// Also checks environment variable WOLF3D_VR_PLAY_MODE:
///   5dof      : Force VR mode with 5DOF play (default)
///   roomscale : Force VR mode with roomscale play
/// </summary>
public static class DisplayModeFactory
{
	/// <summary>
	/// Creates the appropriate display mode based on command-line args,
	/// environment variables, and available XR interfaces.
	/// </summary>
	/// <returns>VRDisplayMode if OpenXR is available and initializes, otherwise FlatscreenDisplayMode.</returns>
	public static IDisplayMode Create()
	{
		// Check command-line arguments first (highest priority)
		string[] args = OS.GetCmdlineArgs();
		bool forceFlatscreen = args.Contains("--flatscreen") || args.Contains("--no-vr");

		// Determine VR play mode from args, then env var, then default to 5DOF
		VRPlayMode playMode = VRPlayMode.FiveDOF;
		bool forceVR = false;
		if (args.Contains("--roomscale"))
		{
			playMode = VRPlayMode.Roomscale;
			forceVR = true;
		}
		else if (args.Contains("--5dof"))
		{
			forceVR = true;
		}
		else
		{
			string envPlayMode = System.Environment.GetEnvironmentVariable("WOLF3D_VR_PLAY_MODE");
			if (!string.IsNullOrEmpty(envPlayMode))
			{
				if (envPlayMode.Equals("roomscale", StringComparison.OrdinalIgnoreCase))
				{
					playMode = VRPlayMode.Roomscale;
					forceVR = true;
				}
				else if (envPlayMode.Equals("5dof", StringComparison.OrdinalIgnoreCase))
				{
					forceVR = true;
				}
			}
		}

		// Handle forced flatscreen mode
		if (forceFlatscreen)
		{
			GD.Print("Flatscreen mode forced via command-line or environment variable");
			return new FlatscreenDisplayMode();
		}

		// Try to find and initialize OpenXR
		XRInterface xrInterface = XRServer.FindInterface("OpenXR");

		if (xrInterface is not null)
		{
			if (xrInterface.Initialize())
			{
				GD.Print("OpenXR initialized successfully");
				return new VRDisplayMode(playMode);
			}
			else
			{
				if (forceVR)
					GD.PrintErr("ERROR: VR mode forced but OpenXR failed to initialize");
				GD.PrintErr("Warning: OpenXR found but failed to initialize, falling back to flatscreen mode");
			}
		}
		else
		{
			if (forceVR)
				GD.PrintErr("ERROR: VR mode forced but OpenXR interface not found");
			GD.Print("OpenXR not available, using flatscreen mode");
		}

		return new FlatscreenDisplayMode();
	}
}
