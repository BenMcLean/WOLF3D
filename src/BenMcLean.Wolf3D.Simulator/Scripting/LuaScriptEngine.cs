using System;
using System.Collections.Generic;
using BenMcLean.Wolf3D.Assets;
using MoonSharp.Interpreter;
using Microsoft.Extensions.Logging;

namespace BenMcLean.Wolf3D.Simulator.Scripting;

/// <summary>
/// Manages MoonSharp Lua VM with deterministic sandboxing and context injection.
/// Provides controlled, stateless script execution for simulation reproducibility.
/// </summary>
public class LuaScriptEngine
{
	private readonly Script luaScript;
	private readonly Dictionary<string, DynValue> cachedFunctions = [];
	private readonly Dictionary<string, DynValue> compiledStateFunctions = [];
	private readonly ILogger logger;
	/// <summary>
	/// Current execution context (injected before each script call)
	/// </summary>
	public IScriptContext CurrentContext { get; set; }
	public LuaScriptEngine(ILogger logger = null)
	{
		this.logger = logger;
		// Register C# types for MoonSharp UserData
		UserData.RegisterType<ActorScriptContext>();
		UserData.RegisterType<ActionScriptContext>();
		// Create MoonSharp script instance
		luaScript = new Script(CoreModules.Preset_SoftSandbox);
		// Additional sandboxing and deterministic overrides
		// Remove dangerous/non-deterministic functions from Lua environment
		luaScript.Globals["io"] = DynValue.Nil;
		luaScript.Globals["dofile"] = DynValue.Nil;
		luaScript.Globals["loadfile"] = DynValue.Nil;
		luaScript.Globals["package"] = DynValue.Nil;
		luaScript.Globals["require"] = DynValue.Nil;
		luaScript.Globals["debug"] = DynValue.Nil;
		// Override print() to route to logger
		luaScript.Globals["print"] = DynValue.NewCallback(LuaPrint);
		// Get existing math table and override only random/randomseed (keep existing math functions)
		Table mathTable = luaScript.Globals.Get("math").Table;
		// Override random functions with deterministic versions
		mathTable["random"] = DynValue.NewCallback(MathRandom);
		// Ignore seed attempts - RNG is controlled by simulator
		mathTable["randomseed"] = DynValue.Nil;
		// Override os library with deterministic clock
		// Keep it minimal - only the functions we need
		luaScript.Globals["os"] = DynValue.NewTable(luaScript);
		Table osTable = luaScript.Globals.Get("os").Table;
		osTable["time"] = DynValue.NewCallback(OsTime);
		osTable["clock"] = DynValue.NewCallback(OsClock);
		osTable["date"] = DynValue.NewCallback(OsDate);
		// Note: Wolf3D API functions are exposed via reflection in ExposeContextMethodsAsGlobals
	}
	private readonly HashSet<string> exposedMethodNames = [];

	/// <summary>
	/// Cleanup ActorScriptContext globals after script execution.
	/// </summary>
	private void CleanupActorScriptContextGlobals()
	{
		foreach (string name in exposedMethodNames)
			luaScript.Globals[name] = DynValue.Nil;
		exposedMethodNames.Clear();
	}

	/// <summary>
	/// Expose ActorScriptContext methods as global Lua functions using reflection.
	/// This allows Lua code to call CheckSight() instead of ctx:CheckSight().
	/// </summary>
	private void ExposeContextMethodsAsGlobals(ActorScriptContext ctx)
	{
		// Get all public instance methods from ActorScriptContext
		var methods = typeof(ActorScriptContext).GetMethods(
			System.Reflection.BindingFlags.Public |
			System.Reflection.BindingFlags.Instance |
			System.Reflection.BindingFlags.DeclaredOnly);

		foreach (var method in methods)
		{
			// Skip property getters/setters and special methods
			if (method.IsSpecialName)
				continue;

			string methodName = method.Name;
			var parameters = method.GetParameters();
			var returnType = method.ReturnType;

			// Create a callback that invokes the method via reflection
			luaScript.Globals[methodName] = DynValue.NewCallback((c, args) =>
			{
				// Convert Lua arguments to C# types
				object[] invokeArgs = new object[parameters.Length];
				for (int i = 0; i < parameters.Length; i++)
				{
					if (i < args.Count)
					{
						var luaArg = args[i];
						var paramType = parameters[i].ParameterType;

						// Convert based on parameter type
						if (paramType == typeof(int))
							invokeArgs[i] = (int)luaArg.Number;
						else if (paramType == typeof(string))
							invokeArgs[i] = luaArg.String;
						else if (paramType == typeof(bool))
							invokeArgs[i] = luaArg.Boolean;
						else if (paramType == typeof(double))
							invokeArgs[i] = luaArg.Number;
						else
							invokeArgs[i] = luaArg.ToObject();
					}
				}

				// Invoke the method
				object result = method.Invoke(ctx, invokeArgs);

				// Convert return value to Lua type
				if (returnType == typeof(void))
					return DynValue.Nil;
				else if (returnType == typeof(int) || returnType == typeof(short) || returnType == typeof(ushort))
					return DynValue.NewNumber(Convert.ToDouble(result));
				else if (returnType == typeof(bool))
					return DynValue.NewBoolean((bool)result);
				else if (returnType == typeof(string))
					return DynValue.NewString((string)result);
				else
					return DynValue.FromObject(luaScript, result);
			});

			exposedMethodNames.Add(methodName);
		}
	}

	#region Deterministic Standard Library Overrides
	/// <summary>
	/// Lua print() function - routes to Microsoft.Extensions.Logging
	/// </summary>
	private DynValue LuaPrint(ScriptExecutionContext ctx, CallbackArguments args)
	{
		if (args.Count == 0)
		{
			logger?.LogInformation("");
			return DynValue.Nil;
		}
		// Concatenate all arguments with tabs (Lua print behavior)
		string[] parts = new string[args.Count];
		for (int i = 0; i < args.Count; i++)
		{
			DynValue arg = args[i];
			parts[i] = arg.Type == DataType.Nil ? "nil" : arg.ToString();
		}
		string message = string.Join("\t", parts);
		logger?.LogInformation("{LuaOutput}", message);
		return DynValue.Nil;
	}
	private DynValue MathRandom(ScriptExecutionContext ctx, CallbackArguments args)
	{
		if (CurrentContext is null)
			return DynValue.NewNumber(0);
		RNG rng = CurrentContext.RNG;
		if (args.Count == 0)
		{
			// math.random() -> [0, 1)
			return DynValue.NewNumber(rng.NextDouble());
		}
		else if (args.Count == 1)
		{
			// math.random(m) -> [1, m]
			int m = (int)args[0].Number;
			return DynValue.NewNumber(rng.NextInt(1, m + 1));
		}
		else
		{
			// math.random(m, n) -> [m, n]
			int m = (int)args[0].Number,
				n = (int)args[1].Number;
			return DynValue.NewNumber(rng.NextInt(m, n + 1));
		}
	}
	private DynValue OsTime(ScriptExecutionContext ctx, CallbackArguments args) =>
		CurrentContext is null ?
			DynValue.NewNumber(0)
			: DynValue.NewNumber(CurrentContext.GameClock.GetUnixTimestamp());
	private DynValue OsClock(ScriptExecutionContext ctx, CallbackArguments args) =>
		CurrentContext is null ?
			DynValue.NewNumber(0)
			: DynValue.NewNumber(CurrentContext.GameClock.GetElapsedSeconds());
	private DynValue OsDate(ScriptExecutionContext ctx, CallbackArguments args) =>
		CurrentContext is null ?
			DynValue.NewString("")
			: DynValue.NewString(CurrentContext.GameClock.FormatDate(args.Count > 0 ? args[0].String : null));
	#endregion
	/// <summary>
	/// Load and compile a Lua script (done once at startup)
	/// </summary>
	public void LoadScript(string scriptId, string luaCode)
	{
		try
		{
			DynValue result = luaScript.DoString(luaCode);
			cachedFunctions[scriptId] = result;
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException($"Failed to load script '{scriptId}': {ex.Message}", ex);
		}
	}
	/// <summary>
	/// Call a cached Lua function with the current context
	/// </summary>
	public DynValue CallFunction(string scriptId, string functionName, params object[] args)
	{
		if (!cachedFunctions.ContainsKey(scriptId))
			throw new InvalidOperationException($"Script '{scriptId}' not loaded");
		try
		{
			DynValue func = luaScript.Globals.Get(functionName);
			if (func.Type != DataType.Function)
				throw new InvalidOperationException($"Function '{functionName}' not found in script '{scriptId}'");
			return luaScript.Call(func, args);
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException($"Error calling '{functionName}' in '{scriptId}': {ex.Message}", ex);
		}
	}
	/// <summary>
	/// Eagerly compiles all state functions from a StateCollection.
	/// Call this once at simulator startup to pre-compile all Lua bytecode.
	/// </summary>
	/// <param name="stateCollection">The collection of states and functions to compile</param>
	public void CompileAllStateFunctions(StateCollection stateCollection)
	{
		compiledStateFunctions.Clear();
		foreach (StateFunction function in stateCollection.Functions.Values)
		{
			CompileStateFunction(function.Name, function.Code);
		}
	}
	/// <summary>
	/// Compiles a single state function to bytecode without executing it.
	/// </summary>
	/// <param name="functionName">Unique name for this function (e.g., "T_Stand", "A_DeathScream")</param>
	/// <param name="luaCode">The Lua code to compile</param>
	public void CompileStateFunction(string functionName, string luaCode)
	{
		if (string.IsNullOrWhiteSpace(luaCode))
		{
			// Empty functions are allowed - just store nil
			compiledStateFunctions[functionName] = DynValue.Nil;
			return;
		}
		try
		{
			// Load (compile) the code without executing it
			DynValue compiled = luaScript.LoadString(luaCode, null, functionName);
			compiledStateFunctions[functionName] = compiled;
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException($"Failed to compile state function '{functionName}': {ex.Message}", ex);
		}
	}
	/// <summary>
	/// Executes a pre-compiled state function with the given context.
	/// </summary>
	/// <param name="functionName">Name of the function to execute</param>
	/// <param name="context">Execution context providing game state access</param>
	/// <returns>The result of the Lua script execution</returns>
	public DynValue ExecuteStateFunction(string functionName, IScriptContext context)
	{
		// TODO: In final release, missing functions should throw hard errors with helpful messages for modders.
		// For now, silently skip missing functions to allow testing with incomplete function sets.
		// Final behavior should be:
		//   throw new InvalidOperationException($"State function '{functionName}' not compiled. Did you call CompileAllStateFunctions?");

		if (!compiledStateFunctions.TryGetValue(functionName, out DynValue compiled))
		{
			// TODO: Restore hard error for final release (see method summary)
			return DynValue.Nil; // Silently skip missing function for testing
		}
		if (compiled.Type == DataType.Nil)
		{
			// Empty function - nothing to execute
			return DynValue.Nil;
		}
		try
		{
			// Set context for this execution
			IScriptContext previousContext = CurrentContext;
			CurrentContext = context;

			// Expose context methods as global functions in Lua
			// This makes ActorScriptContext methods directly callable (e.g., CheckSight(), GetSpeed())
			if (context is ActorScriptContext actorCtx)
			{
				luaScript.Globals["ctx"] = UserData.Create(actorCtx);
				// Expose all methods as globals for direct calling
				ExposeContextMethodsAsGlobals(actorCtx);
			}

			// Execute the compiled function
			DynValue result = luaScript.Call(compiled);

			// Clean up globals
			if (context is ActorScriptContext)
			{
				luaScript.Globals["ctx"] = DynValue.Nil;
				CleanupActorScriptContextGlobals();
			}

			// Restore previous context
			CurrentContext = previousContext;
			return result;
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException($"Error executing state function '{functionName}': {ex.Message}", ex);
		}
	}
}
