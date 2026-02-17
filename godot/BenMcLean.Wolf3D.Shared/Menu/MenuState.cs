namespace BenMcLean.Wolf3D.Shared.Menu;

/// <summary>
/// VR mode options for menu settings.
/// This is a new setting not present in the original Wolfenstein 3D CONFIG file format.
/// </summary>
public enum VRMode
{//TODO: Move VRMode into the VR project somehow
	/// <summary>
	/// Full 6 degrees of freedom VR - player can move and look around freely.
	/// </summary>
	Roomscale,
	/// <summary>
	/// 5 degrees of freedom VR - ignores vertical movement (seated play).
	/// Player can look around and move horizontally but height is fixed.
	/// </summary>
	FiveDOF
}

/// <summary>
/// Menu session state - holds temporary menu navigation state and selections.
/// Does NOT duplicate settings from Config.cs (sound, music, input settings).
/// Only holds menu-specific session data:
/// - Episode/difficulty selections (temporary until game starts)
/// - VR-specific settings (new, not in original CONFIG format)
/// - Start game trigger
/// </summary>
public class MenuState
{
	/// <summary>
	/// Selected episode for new game (1-6 for Wolfenstein 3D).
	/// This is temporary session state, not persisted to CONFIG file.
	/// </summary>
	public int SelectedEpisode { get; set; } = 1;
	/// <summary>
	/// Selected difficulty for new game (0-3: Baby, Easy, Normal, Hard).
	/// This is temporary session state, not persisted to CONFIG file.
	/// </summary>
	public int SelectedDifficulty { get; set; } = 2; // Default: Normal
	/// <summary>
	/// Flag to trigger game start after menu navigation completes.
	/// Set to true by difficulty selection, detected by Root.cs to transition to ActionStage.
	/// </summary>
	public bool StartGame { get; set; } = false;
	/// <summary>
	/// VR mode setting - new feature not in original CONFIG format.
	/// Stored here instead of Config.cs because it doesn't fit the original file structure.
	/// May be persisted to a separate settings file in the future.
	/// </summary>
	public VRMode VRMode { get; set; } = VRMode.Roomscale;
	/// <summary>
	/// Accumulated bonus points from the intermission screen.
	/// Set by Lua GivePoints(), consumed by Root.cs when transitioning to next level.
	/// </summary>
	public int BonusPoints { get; set; } = 0;
	/// <summary>
	/// Pending load game slot index. Null when no load is pending.
	/// Set by Lua LoadGame(slot), polled by Root.cs to transition to loaded game.
	/// </summary>
	public int? LoadGameSlot { get; set; }
}
