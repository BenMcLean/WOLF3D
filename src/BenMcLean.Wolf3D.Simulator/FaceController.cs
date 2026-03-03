namespace BenMcLean.Wolf3D.Simulator;

/// <summary>
/// Owns the facecount tic logic from WL_AGENT.C:UpdateFace.
/// Fires an ActionFunction on each facecount expiry to update the status bar face picture.
/// The ActionFunction is data-driven (from XML StatusBar OnFace attribute).
/// </summary>
public class FaceController
{
	// WL_AGENT.C:facecount - countdown timer; face updates when it reaches 0
	private int facecount;
	private readonly RNG rng;
	private readonly Lua.LuaScriptEngine engine;
	private readonly string functionName;

	/// <summary>
	/// Creates a FaceController with the given RNG, script engine, and Lua function name.
	/// </summary>
	/// <param name="rng">Deterministic RNG for facecount threshold (WL_AGENT.C:US_RndT())</param>
	/// <param name="engine">Lua script engine to execute the face function</param>
	/// <param name="functionName">Name of the compiled ActionFunction to call (e.g., "UpdateFace")</param>
	public FaceController(RNG rng, Lua.LuaScriptEngine engine, string functionName)
	{
		this.rng = rng;
		this.engine = engine;
		this.functionName = functionName;
	}

	/// <summary>
	/// Advances facecount and fires the face function when threshold is exceeded.
	/// Call once per tic from the simulation update loop.
	/// WL_AGENT.C:UpdateFace — facecount logic (count down, then US_RndT() threshold check).
	/// </summary>
	/// <param name="context">Current action script context for Lua execution</param>
	public void Update(Lua.ActionScriptContext context)
	{
		facecount++;
		// WL_AGENT.C:UpdateFace — update face when facecount exceeds random threshold
		if (facecount > rng.NextInt(256))
		{
			engine.ExecuteActionFunction(functionName, context);
			facecount = 0;
		}
	}

	/// <summary>
	/// Runs the face function immediately, resetting the facecount timer.
	/// Call when health changes, on item pickup, or other events that should
	/// update the face without waiting for the facecount timer.
	/// WL_AGENT.C:UpdateFace — immediate face update on damage/pickup.
	/// </summary>
	/// <param name="context">Current action script context for Lua execution</param>
	public void RunNow(Lua.ActionScriptContext context)
	{
		engine.ExecuteActionFunction(functionName, context);
		facecount = 0;
	}
}
