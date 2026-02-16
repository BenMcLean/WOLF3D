using System;

namespace BenMcLean.Wolf3D.Simulator;

/// <summary>
/// Snapshot of level completion statistics, derived from game state at level end.
/// Used by the intermission screen to display kill/secret/treasure ratios and time.
/// </summary>
public record LevelCompletionStats(
	int FloorNumber,
	bool IsSecretLevel,
	int KillCount,
	int KillTotal,
	int SecretCount,
	int SecretTotal,
	int TreasureCount,
	int TreasureTotal,
	long ElapsedTics,
	TimeSpan ParTime,
	int Score);
