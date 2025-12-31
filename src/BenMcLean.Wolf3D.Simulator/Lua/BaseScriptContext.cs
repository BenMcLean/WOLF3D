using Microsoft.Extensions.Logging;

namespace BenMcLean.Wolf3D.Simulator.Lua;

/// <summary>
/// Base script context providing audio API.
/// Can be used standalone or inherited by more specialized contexts.
/// Provides non-local (global) sound playback - no spatial positioning.
/// </summary>
public class BaseScriptContext : IScriptContext
{
	protected readonly ILogger _logger;

	public BaseScriptContext(ILogger logger = null)
	{
		_logger = logger;
	}

	/// <summary>
	/// Play a digitized sound effect globally (non-positional).
	/// Plays in player's "headphones" without 3D positioning.
	/// </summary>
	public virtual void PlayDigiSound(string soundName)
	{
		// TODO: Implement sound playback
		_logger?.LogDebug("BaseScriptContext: PlayDigiSound({soundName})", soundName);
	}

	/// <summary>
	/// Play an AdLib sound effect.
	/// AdLib sounds have no spatial positioning.
	/// </summary>
	public virtual void PlayAdLibSound(string soundName)
	{
		// TODO: Implement AdLib sound playback
		_logger?.LogDebug("BaseScriptContext: PlayAdLibSound({soundName})", soundName);
	}

	/// <summary>
	/// Play background music by name.
	/// </summary>
	public virtual void PlayMusic(string musicName)
	{
		// TODO: Implement music playback
		_logger?.LogDebug("BaseScriptContext: PlayMusic({musicName})", musicName);
	}

	/// <summary>
	/// Stop currently playing music.
	/// </summary>
	public virtual void StopMusic()
	{
		// TODO: Implement music stop
		_logger?.LogDebug("BaseScriptContext: StopMusic()");
	}
}
