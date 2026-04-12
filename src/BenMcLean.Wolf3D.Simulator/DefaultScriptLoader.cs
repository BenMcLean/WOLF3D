using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace BenMcLean.Wolf3D.Simulator;

/// <summary>
/// Loads default Lua scripts embedded in the Simulator assembly.
/// Scripts live in Lua/DefaultScripts/Actors/, Lua/DefaultScripts/Weapons/, or
/// Lua/DefaultScripts/Bonuses/, named for their function (e.g. T_Chase.lua).
/// The subfolder is organisational only — the function name is always just the filename stem.
/// Game description XML files may override any default by defining a script with the same name.
/// </summary>
internal static class DefaultScriptLoader
{
	private const string ResourcePrefix = "BenMcLean.Wolf3D.Simulator.Lua.DefaultScripts.";
	private const string BonusesSubPrefix = "BenMcLean.Wolf3D.Simulator.Lua.DefaultScripts.Bonuses.";
	private const string ResourceSuffix = ".lua";

	/// <summary>
	/// Loads all actor and weapon default scripts (Actors/ and Weapons/ subfolders).
	/// These are merged into StateCollection.Functions before compilation.
	/// </summary>
	public static IEnumerable<(string Name, string Code)> LoadActorAndWeaponScripts() =>
		LoadFromSubfolders(exclude: BonusesSubPrefix);

	/// <summary>
	/// Loads all bonus default scripts (Bonuses/ subfolder).
	/// These are merged into the item scripts dictionary in LoadItemScripts.
	/// </summary>
	public static IEnumerable<(string Name, string Code)> LoadBonusScripts() =>
		LoadFromSubfolders(only: BonusesSubPrefix);

	private static IEnumerable<(string Name, string Code)> LoadFromSubfolders(
		string only = null, string exclude = null)
	{
		Assembly assembly = typeof(DefaultScriptLoader).Assembly;
		foreach (string resourceName in assembly.GetManifestResourceNames())
		{
			if (!resourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal) ||
				!resourceName.EndsWith(ResourceSuffix, StringComparison.Ordinal))
				continue;
			if (only != null && !resourceName.StartsWith(only, StringComparison.Ordinal))
				continue;
			if (exclude != null && resourceName.StartsWith(exclude, StringComparison.Ordinal))
				continue;
			// Strip prefix and suffix to get "SubFolder.FunctionName",
			// then take only the last segment so the subfolder is not part of the function name.
			string relative = resourceName.Substring(
				ResourcePrefix.Length,
				resourceName.Length - ResourcePrefix.Length - ResourceSuffix.Length);
			int lastDot = relative.LastIndexOf('.');
			string functionName = lastDot >= 0 ? relative.Substring(lastDot + 1) : relative;
			using Stream stream = assembly.GetManifestResourceStream(resourceName);
			using StreamReader reader = new(stream);
			yield return (functionName, reader.ReadToEnd());
		}
	}
}
