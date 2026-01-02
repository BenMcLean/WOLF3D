using System;
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

	/// <summary>
	/// Action to invoke when PlayDigiSound is called.
	/// Set by the host (e.g., MenuManager, ActionStage) to wire up sound playback.
	/// </summary>
	public Action<string> PlayDigiSoundAction { get; set; }

	/// <summary>
	/// Action to invoke when PlayAdLibSound is called.
	/// Set by the host (e.g., MenuManager, ActionStage) to wire up AdLib sound playback.
	/// </summary>
	public Action<string> PlayAdLibSoundAction { get; set; }

	/// <summary>
	/// Action to invoke when PlayMusic is called.
	/// Set by the host (e.g., MenuManager, ActionStage) to wire up music playback.
	/// </summary>
	public Action<string> PlayMusicAction { get; set; }

	/// <summary>
	/// Action to invoke when StopMusic is called.
	/// Set by the host (e.g., MenuManager, ActionStage) to wire up music stopping.
	/// </summary>
	public Action StopMusicAction { get; set; }

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
		if (PlayDigiSoundAction != null)
			PlayDigiSoundAction(soundName);
		else
			_logger?.LogDebug("BaseScriptContext: PlayDigiSound({soundName}) - no handler wired", soundName);
	}

	/// <summary>
	/// Play an AdLib sound effect.
	/// AdLib sounds have no spatial positioning.
	/// </summary>
	public virtual void PlayAdLibSound(string soundName)
	{
		if (PlayAdLibSoundAction != null)
			PlayAdLibSoundAction(soundName);
		else
			_logger?.LogDebug("BaseScriptContext: PlayAdLibSound({soundName}) - no handler wired", soundName);
	}

	/// <summary>
	/// Play background music by name.
	/// </summary>
	public virtual void PlayMusic(string musicName)
	{
		if (PlayMusicAction != null)
			PlayMusicAction(musicName);
		else
			_logger?.LogDebug("BaseScriptContext: PlayMusic({musicName}) - no handler wired", musicName);
	}

	/// <summary>
	/// Stop currently playing music.
	/// </summary>
	public virtual void StopMusic()
	{
		if (StopMusicAction != null)
			StopMusicAction();
		else
			_logger?.LogDebug("BaseScriptContext: StopMusic() - no handler wired");
	}
}
