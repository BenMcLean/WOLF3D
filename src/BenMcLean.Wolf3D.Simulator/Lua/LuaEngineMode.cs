namespace BenMcLean.Wolf3D.Simulator.Lua;

/// <summary>
/// Execution mode for LuaScriptEngine.
/// Determines sandboxing and determinism enforcement.
/// </summary>
public enum LuaEngineMode
{
	/// <summary>
	/// Permissive mode: Full Lua standard library, real time/random, minimal sandboxing.
	/// Used for menu scripts where determinism is not required.
	/// </summary>
	Permissive,

	/// <summary>
	/// Strict mode: Sandboxed environment, deterministic RNG/clock, no global variables.
	/// Used for action stage scripts where determinism is critical for simulation.
	/// </summary>
	Strict
}
