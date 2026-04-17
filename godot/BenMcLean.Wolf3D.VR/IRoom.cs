using System;

namespace BenMcLean.Wolf3D.VR;

/// <summary>
/// Implemented by all room-level scenes managed by Root.
/// </summary>
public interface IRoom
{
	/// <summary>
	/// When true, Root skips fade transitions into and out of this room.
	/// Used for rooms that already have a black background, where fading is not visible.
	/// </summary>
	bool SkipFade { get; }

	/// <summary>
	/// Sets the handler that wraps internal screen navigations in a fade transition.
	/// Called by Root after the room is added to the scene tree.
	/// Rooms that do not navigate internally can ignore this (default no-op).
	/// </summary>
	void SetFadeTransitionHandler(Action<Action> handler) { }

	/// <summary>
	/// Called by Root each frame while the screen is fully black, before the fade-in begins.
	/// Returns true when the room is ready to start fading in.
	/// Return false to hold the black screen for another frame (e.g. waiting for VR tracking).
	/// The default implementation is always ready.
	/// </summary>
	bool PrepareForFadeIn() => true;
}
