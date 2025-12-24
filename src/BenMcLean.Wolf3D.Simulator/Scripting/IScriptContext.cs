namespace BenMcLean.Wolf3D.Simulator.Scripting;

/// <summary>
/// Base interface for all script execution contexts.
/// Provides shared API available to all Lua scripts (audio, timing, etc.)
/// </summary>
public interface IScriptContext
{
	/// <summary>
	/// Deterministic RNG for this context
	/// </summary>
	RNG RNG { get; }

	/// <summary>
	/// Deterministic game clock for this context
	/// </summary>
	GameClock GameClock { get; }

	// Shared API methods (available in both action and menu stages)

	/// <summary>
	/// Play a digitized sound effect.
	/// Implementation varies by context (positional vs global).
	/// </summary>
	void PlayDigiSound(int soundId);

	/// <summary>
	/// Play background music.
	/// </summary>
	void PlayMusic(int musicId);

	/// <summary>
	/// Stop currently playing music.
	/// </summary>
	void StopMusic();
}
