using Microsoft.Extensions.Logging;

namespace BenMcLean.Wolf3D.Simulator.Lua;

/// <summary>
/// Script context for entities that have a position in the game world.
/// Extends ActionScriptContext with local (positional) sound playback.
/// Base class for Actor, Door, and Bonus contexts.
/// </summary>
public abstract class EntityScriptContext : ActionScriptContext
{
	protected readonly int entityX;
	protected readonly int entityY;

	protected EntityScriptContext(
		Simulator simulator,
		RNG rng,
		GameClock gameClock,
		int entityX,
		int entityY,
		ILogger logger = null)
		: base(simulator, rng, gameClock, logger)
	{
		this.entityX = entityX;
		this.entityY = entityY;
	}

	/// <summary>
	/// Play a digitized sound effect at this entity's position.
	/// Uses spatial audio - sound will be positioned in 3D space.
	/// </summary>
	public virtual void PlayLocalDigiSound(string soundName)
	{
		// TODO: Emit event for VR layer to play positional sound
		// simulator.EmitPlaySoundEvent(soundName, isPositional: true, x: entityX, y: entityY);
		_logger?.LogDebug("EntityScriptContext: PlayLocalDigiSound({soundName}) at ({x}, {y})",
			soundName, entityX, entityY);
	}
}
