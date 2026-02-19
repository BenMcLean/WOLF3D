using Microsoft.Extensions.Logging;

namespace BenMcLean.Wolf3D.Simulator.Lua;

/// <summary>
/// Script context for entities that have a position in the game world.
/// Extends ActionScriptContext with local (positional) sound playback.
/// Base class for Actor, Door, and Bonus contexts.
/// </summary>
public abstract class EntityScriptContext(
	Simulator simulator,
	RNG rng,
	GameClock gameClock,
	int entityX,
	int entityY,
	ILogger logger = null) : ActionScriptContext(simulator, rng, gameClock, logger)
{
	protected readonly int entityX = entityX,
		entityY = entityY;
	/// <summary>
	/// Play a digitized sound effect at this entity's position.
	/// Uses spatial audio - sound will be positioned in 3D space.
	/// Uses PlayDigiSoundAction if wired (which should emit a positional sound event).
	/// </summary>
	public virtual void PlayLocalDigiSound(string soundName)
	{
		// Use PlayDigiSoundAction - the caller should wire this to emit positional sound
		if (PlayDigiSoundAction is not null)
			PlayDigiSoundAction(soundName);
		else
			_logger?.LogDebug("EntityScriptContext: PlayLocalDigiSound({soundName}) at ({x}, {y}) - no handler wired",
				soundName, entityX, entityY);
	}
}
