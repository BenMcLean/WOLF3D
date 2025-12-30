using System;
using Godot;

namespace BenMcLean.Wolf3D.VR;

/// <summary>
/// VR-specific exception handler.
/// Logs exceptions and displays them to the user via DOS screen in VR.
/// </summary>
public static class ExceptionHandler
{
	/// <summary>
	/// Callback for displaying exceptions to the VR user.
	/// Root registers this callback during initialization.
	/// </summary>
	public static Action<Exception>? DisplayCallback { get; set; }

	/// <summary>
	/// Central exception handling method.
	/// Logs the exception first (always works), then attempts to display it via callback.
	/// </summary>
	/// <param name="ex">The exception to handle</param>
	public static void HandleException(Exception ex)
	{
		// Always log first - most reliable, never fails
		GD.PrintErr($"ERROR: Unhandled exception: {ex}");

		// Attempt to display via callback (best-effort)
		try
		{
			DisplayCallback?.Invoke(ex);
		}
		catch (Exception displayEx)
		{
			// If display fails, log that too
			GD.PrintErr($"ERROR: Failed to display error screen: {displayEx}");
		}
	}
}
