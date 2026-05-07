namespace BenMcLean.Wolf3D.Simulator.Lua;

/// <summary>
/// Base interface for all script execution contexts.
/// Provides shared API available to all Lua scripts (audio playback).
/// Does NOT include RNG/GameClock - those are only in action stage contexts.
/// </summary>
public interface IScriptContext
{
	/// <summary>
	/// Play a sound effect by logical sound name.
	/// The host resolves the logical sound to the active output device automatically.
	/// </summary>
	void PlaySound(string soundName);
	/// <summary>
	/// Returns true if the requested sound name can be resolved by the active game/audio mapping.
	/// </summary>
	bool HasSound(string soundName);
	/// <summary>
	/// Returns the first playable sound name from the requested primary/fallback pair.
	/// If neither resolves, returns the original primary name unchanged.
	/// </summary>
	string ResolveSound(string soundName, string fallbackSoundName);
	/// <summary>
	/// Play background music by name.
	/// </summary>
	void PlayMusic(string musicName);
	/// <summary>
	/// Stop currently playing music.
	/// </summary>
	void StopMusic();
}
