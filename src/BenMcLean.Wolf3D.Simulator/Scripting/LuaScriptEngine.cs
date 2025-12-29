using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
	private Table baseEnvironment;
	private Table proxyEnvironment;
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

		// Create read-only proxy environment
		// Use standard Lua metatable approach: __index points to base table directly
		proxyEnvironment = new Table(luaScript);
		Table proxyMeta = new Table(luaScript);

		// NOTE: We'll set __index after baseEnvironment is created
		// For now, just set up __newindex to catch write attempts

		// Forbid writes - environment is read-only
		proxyMeta["__newindex"] = DynValue.NewCallback((ctx, args) =>
		{
			// If a script tries to set a global variable, throw a helpful error
			if (args.Count >= 1 && args[0].Type == DataType.String)
			{
				string key = args[0].String;
				if (!string.IsNullOrEmpty(key))
				{
					throw new ScriptRuntimeException(
						$"Cannot set global variable '{key}'. " +
						$"Use 'local {key} = ...' instead. " +
						"Global variables are forbidden for determinism.");
				}
			}
			return DynValue.Nil;
		});

		proxyEnvironment.MetaTable = proxyMeta;

		// Initialize base environment early (before compilation)
		// We'll add compiled functions to it later in CompileAllStateFunctions
		baseEnvironment = new Table(luaScript);

		// Add standard library references as READ-ONLY proxies
		// This prevents scripts from modifying stdlib tables (e.g., overriding math.random)
		baseEnvironment["math"] = CreateReadOnlyTableProxy(luaScript.Globals.Get("math").Table);
		baseEnvironment["string"] = CreateReadOnlyTableProxy(luaScript.Globals.Get("string").Table);
		baseEnvironment["table"] = CreateReadOnlyTableProxy(luaScript.Globals.Get("table").Table);
		baseEnvironment["print"] = luaScript.Globals["print"];
		baseEnvironment["os"] = CreateReadOnlyTableProxy(luaScript.Globals.Get("os").Table);

		// Expose all ActorScriptContext methods using reflection
		// These callbacks reference CurrentContext, which is set per-execution
		ExposeContextMethodsInEnvironment(baseEnvironment);

		// Now set proxy's __index to baseEnvironment (standard Lua inheritance)
		proxyMeta["__index"] = baseEnvironment;
	}

	/// <summary>
	/// Add compiled state functions to the base environment.
	/// Called after CompileAllStateFunctions to make functions available to each other.
	/// </summary>
	private void AddCompiledFunctionsToBaseEnvironment()
	{
		// Add all compiled state functions so they can call each other
		foreach (KeyValuePair<string, DynValue> kvp in compiledStateFunctions)
			if (kvp.Value.Type != DataType.Nil)
				baseEnvironment[kvp.Key] = kvp.Value;
	}

	/// <summary>
	/// Creates a read-only proxy for a standard library table.
	/// Allows reads but prevents modifications that could break determinism.
	/// </summary>
	/// <param name="sourceTable">The stdlib table to wrap (e.g., math, string, os)</param>
	/// <returns>A DynValue wrapping the read-only proxy table</returns>
	private DynValue CreateReadOnlyTableProxy(Table sourceTable)
	{
		Table proxy = new Table(luaScript);
		Table meta = new Table(luaScript);

		// Allow reads from source table (standard Lua pattern)
		meta["__index"] = sourceTable;

		// Forbid writes - prevent scripts from undoing deterministic overrides
		meta["__newindex"] = DynValue.NewCallback((ctx, args) =>
		{
			if (args.Count >= 1 && args[0].Type == DataType.String)
			{
				string key = args[0].String;
				if (!string.IsNullOrEmpty(key))
				{
					throw new ScriptRuntimeException(
						$"Cannot modify standard library table. " +
						$"Attempt to set '{key}' is forbidden for determinism. " +
						"Standard library tables are read-only.");
				}
			}
			return DynValue.Nil;
		});

		proxy.MetaTable = meta;
		return DynValue.NewTable(proxy);
	}

	/// <summary>
	/// Expose ActorScriptContext methods in the given environment using reflection.
	/// Callbacks invoke methods on CurrentContext (set per-execution).
	/// </summary>
	private void ExposeContextMethodsInEnvironment(Table env)
	{
		MethodInfo[] methods = typeof(ActorScriptContext).GetMethods(
			BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

		foreach (MethodInfo method in methods)
		{
			// Skip property getters/setters and special methods
			if (method.IsSpecialName)
				continue;

			string methodName = method.Name;
			ParameterInfo[] parameters = method.GetParameters();
			Type returnType = method.ReturnType;

			// Create a callback that invokes the method on CurrentContext
			env[methodName] = DynValue.NewCallback((c, args) =>
			{
				// CurrentContext is set per-execution
				if (CurrentContext is not ActorScriptContext actorCtx)
					return DynValue.Nil;

				// Convert Lua arguments to C# types
				object[] invokeArgs = new object[parameters.Length];
				for (int i = 0; i < parameters.Length; i++)
				{
					if (i < args.Count)
					{
						DynValue luaArg = args[i];
						Type paramType = parameters[i].ParameterType;

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

				// Invoke the method on CurrentContext
				object result = method.Invoke(actorCtx, invokeArgs);

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

		// Add compiled functions to base environment so they can call each other
		AddCompiledFunctionsToBaseEnvironment();
	}
	/// <summary>
	/// Compiles a single state function to bytecode without executing it.
	/// Validates that the function has no upvalues (captured local variables),
	/// which would create persistent state across executions.
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
			// Compile with read-only proxy environment
			// Proxy allows reads from baseEnvironment but forbids writes (throws error)
			DynValue compiled = luaScript.LoadString(luaCode, proxyEnvironment, functionName);

			// Validate that the function has no upvalues (persistent state)
			if (compiled.Type == DataType.Function)
			{
				Closure closure = compiled.Function;
				Closure.UpvaluesType upvaluesType = closure.GetUpvaluesType();

				if (upvaluesType == Closure.UpvaluesType.Closure)
				{
					int upvalueCount = closure.GetUpvaluesCount();
					throw new InvalidOperationException(
						$"State function '{functionName}' captures {upvalueCount} local variable(s) as upvalues. " +
						"This creates persistent state across executions, breaking determinism. " +
						"Remove top-level 'local' variables - all state must come from C# via the context API.");
				}
			}

			compiledStateFunctions[functionName] = compiled;
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException($"Failed to compile state function '{functionName}': {ex.Message}", ex);
		}
	}
	/// <summary>
	/// Executes a pre-compiled state function with the given context.
	/// Functions are compiled with a read-only proxy environment that:
	/// - Allows reads from baseEnvironment (stdlib + compiled functions + context methods)
	/// - Forbids writes (throws error if script tries to set global variables)
	/// This provides perfect determinism with zero per-execution overhead.
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
			// Set context for this execution (context methods read from CurrentContext)
			IScriptContext previousContext = CurrentContext;
			CurrentContext = context;

			// Execute the function (compiled with read-only proxy environment)
			// Zero allocations - just pointer swap + function call
			DynValue result = luaScript.Call(compiled);

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
