namespace BenMcLean.Wolf3D.Simulator.State;

/// <summary>
/// Interface for types that can save and load their mutable runtime state.
/// Used for save/load game functionality.
///
/// Design notes:
/// - Only mutable runtime state is serialized; static/structural data loaded from
///   game files (map geometry, door metadata, actor definitions) is NOT included.
/// - State references (e.g., Actor.CurrentState) are serialized as string names
///   and resolved via StateCollection on restore.
/// - Spatial indices and derived data are rebuilt from entity states on restore.
///
/// Wolf3D only saves one level at a time; games that persist multiple levels
/// simultaneously (Blake Stone, Corridor 7) would need a snapshot per level
/// plus cross-level state.
/// </summary>
/// <typeparam name="T">The snapshot type used for serialization</typeparam>
public interface IStateSavable<T>
{
	/// <summary>
	/// Captures the current mutable state into a serializable snapshot.
	/// </summary>
	/// <returns>A snapshot of the current state</returns>
	T SaveState();

	/// <summary>
	/// Restores mutable state from a previously captured snapshot.
	/// Static/structural properties are not modified.
	/// </summary>
	/// <param name="state">The snapshot to restore from</param>
	void LoadState(T state);
}
