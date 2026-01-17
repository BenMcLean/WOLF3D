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
///   --vr                    : Force VR mode (fail if OpenXR unavailable)
///
/// Also checks environment variable WOLF3D_DISPLAY_MODE:
///   flatscreen : Force flatscreen mode
///   vr         : Force VR mode
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
		bool forceVR = args.Contains("--vr");

		// Check environment variable if no command-line override
		if (!forceFlatscreen && !forceVR)
		{
			string envMode = System.Environment.GetEnvironmentVariable("WOLF3D_DISPLAY_MODE");
			if (!string.IsNullOrEmpty(envMode))
			{
				forceFlatscreen = envMode.Equals("flatscreen", StringComparison.OrdinalIgnoreCase);
				forceVR = envMode.Equals("vr", StringComparison.OrdinalIgnoreCase);
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

		if (xrInterface != null)
		{
			if (xrInterface.Initialize())
			{
				GD.Print("OpenXR initialized successfully");
				return new VRDisplayMode(xrInterface);
			}
			else
			{
				if (forceVR)
				{
					GD.PrintErr("ERROR: VR mode forced but OpenXR failed to initialize");
					// Could throw here, but falling back is safer for development
				}
				GD.PrintErr("Warning: OpenXR found but failed to initialize, falling back to flatscreen mode");
			}
		}
		else
		{
			if (forceVR)
			{
				GD.PrintErr("ERROR: VR mode forced but OpenXR interface not found");
			}
			GD.Print("OpenXR not available, using flatscreen mode");
		}

		return new FlatscreenDisplayMode();
	}
}
