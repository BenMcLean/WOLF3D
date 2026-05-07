using System;
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
	/// Action to invoke when PlayLocalSound is called.
	/// Set by the host to wire up positional playback requests.
	/// </summary>
	public Action<string> PlayLocalSoundAction { get; set; }
	/// <summary>
	/// Play a sound effect at this entity's position.
	/// The host should prefer enhanced positional playback when possible and otherwise
	/// fall back to the normal logical sound path.
	/// </summary>
	public virtual void PlayLocalSound(string soundName)
	{
		soundName = ResolveForPlayback(soundName);
		if (PlayLocalSoundAction is not null)
			PlayLocalSoundAction(soundName);
		else
			_logger?.LogDebug("EntityScriptContext: PlayLocalSound({soundName}) at ({x}, {y}) - no handler wired",
				soundName, entityX, entityY);
	}
}
