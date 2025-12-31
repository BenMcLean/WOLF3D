namespace BenMcLean.Wolf3D.Simulator.Lua;

/// <summary>
/// Base interface for all script execution contexts.
/// Provides shared API available to all Lua scripts (audio playback).
/// Does NOT include RNG/GameClock - those are only in action stage contexts.
/// </summary>
public interface IScriptContext
{
	/// <summary>
	/// Play a digitized sound effect by name.
	/// Non-local (global) - plays in player's "headphones" without spatial positioning.
	/// </summary>
	void PlayDigiSound(string soundName);

	/// <summary>
	/// Play an AdLib sound effect by name.
	/// AdLib sounds have no spatial positioning.
	/// </summary>
	void PlayAdLibSound(string soundName);

	/// <summary>
	/// Play background music by name.
	/// </summary>
	void PlayMusic(string musicName);

	/// <summary>
	/// Stop currently playing music.
	/// </summary>
	void StopMusic();
}
