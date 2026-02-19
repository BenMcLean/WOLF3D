namespace BenMcLean.Wolf3D.Simulator.Lua;

/// <summary>
/// Action stage script context interface.
/// Extends base context with deterministic RNG and GameClock.
/// Used by action stage scripts that require strict determinism.
/// </summary>
public interface IActionScriptContext : IScriptContext
{
	/// <summary>
	/// Deterministic RNG for action stage scripts.
	/// Ensures reproducible simulation behavior.
	/// </summary>
	RNG RNG { get; }
	/// <summary>
	/// Deterministic game clock for action stage scripts.
	/// Ensures reproducible simulation timing.
	/// </summary>
	GameClock GameClock { get; }
}
