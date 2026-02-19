using System;
using System.IO;
using BenMcLean.Wolf3D.Simulator.State;
using Godot;

namespace BenMcLean.Wolf3D.Shared;

/// <summary>
/// Manages save game file I/O.
/// Save files are stored alongside game data files (same folder as CONFIG).
/// File naming follows original Wolf3D pattern: SAVEGAM{slot}.{Extension}
/// </summary>
public static class SaveGameManager
{
	/// <summary>
	/// Number of save game slots (matches original Wolf3D).
	/// </summary>
	public const byte SlotCount = 10;

	/// <summary>
	/// Gets the folder where save game files are stored.
	/// Same folder as game data files (derived from XML Path attribute).
	/// </summary>
	public static string GetSaveGameFolder() =>
		SharedAssetManager.CurrentGame?.XML is null ||
		SharedAssetManager.XmlPath is null
		? null
		: Path.Combine(
			Path.GetDirectoryName(SharedAssetManager.XmlPath),
			SharedAssetManager.CurrentGame.XML.Attribute("Path")?.Value ?? "");
	/// <summary>
	/// Gets the file extension for save games from the XML Extension attribute.
	/// Falls back to the Path attribute if Extension is not defined.
	/// </summary>
	private static string GetExtension() => SharedAssetManager.CurrentGame?.XML?.Attribute("Extension")?.Value ?? "SAV";
	/// <summary>
	/// Gets the full path for a save game file.
	/// </summary>
	/// <param name="slot">Slot index (0-9)</param>
	public static string GetSaveFilePath(int slot) =>
		GetSaveGameFolder() is string folder
		? Path.Combine(folder, $"SAVEGAM{slot}.{GetExtension()}")
		: null;
	/// <summary>
	/// Gets the display name for a save slot (or null if empty).
	/// </summary>
	/// <param name="slot">Slot index (0-9)</param>
	/// <returns>Display name string, or null if slot is empty</returns>
	public static string GetSlotDisplayName(int slot)
	{
		if (GetSaveFilePath(slot) is not string path ||
			!File.Exists(path))
			return null;
		try
		{
			string json = File.ReadAllText(path);
			SaveGameFile saveFile = SaveGameFile.Deserialize(json);
			return saveFile?.DisplayName;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"ERROR: Failed to read save slot {slot}: {ex.Message}");
			return null;
		}
	}

	/// <summary>
	/// Saves a game to the specified slot.
	/// </summary>
	/// <param name="slot">Slot index (0-9)</param>
	/// <param name="snapshot">Simulator snapshot to save</param>
	/// <param name="mapName">Map display name (e.g., "Wolf1 Map1")</param>
	public static void Save(int slot, SimulatorSnapshot snapshot, string mapName)
	{
		if (GetSaveFilePath(slot) is not string path)
		{
			GD.PrintErr("ERROR: Cannot determine save file path");
			return;
		}

		DateTime utcNow = DateTime.UtcNow;
		string displayName = FormatDisplayName(mapName, utcNow);

		SaveGameFile saveFile = new()
		{
			Snapshot = snapshot,
			MapName = mapName,
			SavedAt = utcNow.ToString("o"), // ISO 8601
			DisplayName = displayName,
		};

		try
		{
			string json = SaveGameFile.Serialize(saveFile);
			File.WriteAllText(path, json);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"ERROR: Failed to save game to slot {slot}: {ex.Message}");
		}
	}

	/// <summary>
	/// Loads a game from the specified slot.
	/// </summary>
	/// <param name="slot">Slot index (0-9)</param>
	/// <returns>The loaded SaveGameFile, or null on failure</returns>
	public static SaveGameFile Load(int slot)
	{
		if (GetSaveFilePath(slot) is not string path ||
			!File.Exists(path))
			return null;
		try
		{
			return SaveGameFile.Deserialize(File.ReadAllText(path));
		}
		catch (Exception ex)
		{
			GD.PrintErr($"ERROR: Failed to load save slot {slot}: {ex.Message}");
			return null;
		}
	}

	/// <summary>
	/// Formats the display name for a save slot.
	/// Format: "{mapName} {localTime:yyyy-MM-dd h:mm tt} {timeZoneAbbr}"
	/// </summary>
	private static string FormatDisplayName(string mapName, DateTime utcTime) => $"{mapName} {utcTime.ToLocalTime():yyyy-MM-dd h:mm tt} {GetTimeZoneAbbreviation()}";
	/// <summary>
	/// Gets a short time zone abbreviation from the local time zone.
	/// Extracts capital letters from the display name (e.g., "Eastern Standard Time" → "EST").
	/// </summary>
	private static string GetTimeZoneAbbreviation()
	{
		string displayName = TimeZoneInfo.Local.StandardName;
		System.Text.StringBuilder abbr = new();
		foreach (char c in displayName)
			if (char.IsUpper(c))
				abbr.Append(c);
		string result = abbr.ToString();
		return result.Length >= 2 ? result : TimeZoneInfo.Local.StandardName;
	}
}
