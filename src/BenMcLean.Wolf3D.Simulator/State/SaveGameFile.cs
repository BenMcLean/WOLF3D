using System.Text.Json;
using System.Text.Json.Serialization;

namespace BenMcLean.Wolf3D.Simulator.State;

/// <summary>
/// Wrapper record for saved game files.
/// Contains the full SimulatorSnapshot plus metadata for display in menus.
/// Serialized to/from JSON for human-readable save files.
/// </summary>
public record SaveGameFile
{
	/// <summary>
	/// The complete simulator state snapshot.
	/// </summary>
	public SimulatorSnapshot Snapshot { get; init; }

	/// <summary>
	/// Map display name (e.g., "Wolf1 Map1") from GameMap.Name.
	/// </summary>
	public string MapName { get; init; }

	/// <summary>
	/// UTC timestamp when the game was saved, in ISO 8601 format.
	/// </summary>
	public string SavedAt { get; init; }

	/// <summary>
	/// Pre-formatted display string for menu slot display.
	/// Format: "{MapName} {localTime}" (e.g., "Wolf1 Map1 2026-02-16 3:45 PM EST")
	/// </summary>
	public string DisplayName { get; init; }

	private static readonly JsonSerializerOptions SerializerOptions = new()
	{
		WriteIndented = true,
		NumberHandling = JsonNumberHandling.AllowReadingFromString,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};

	/// <summary>
	/// Serializes a SaveGameFile to a JSON string.
	/// </summary>
	public static string Serialize(SaveGameFile saveGame) =>
		JsonSerializer.Serialize(saveGame, SerializerOptions);

	/// <summary>
	/// Deserializes a SaveGameFile from a JSON string.
	/// </summary>
	public static SaveGameFile Deserialize(string json) =>
		JsonSerializer.Deserialize<SaveGameFile>(json, SerializerOptions);
}
