using System;
using Microsoft.Extensions.Logging;

namespace BenMcLean.Wolf3D.Simulator.Lua;

/// <summary>
/// Base script context providing audio API.
/// Can be used standalone or inherited by more specialized contexts.
/// Provides non-local (global) sound playback - no spatial positioning.
/// </summary>
public class BaseScriptContext(ILogger logger = null) : IScriptContext
{
	protected readonly ILogger _logger = logger;
	/// <summary>
	/// Action to invoke when PlaySound is called.
	/// Set by the host to resolve the requested logical sound on the active sound hardware.
	/// </summary>
	public Action<string> PlaySoundAction { get; set; }
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
	/// <summary>
	/// Play a sound effect globally (non-positional).
	/// The host resolves the logical sound to the active output device automatically.
	/// </summary>
	public virtual void PlaySound(string soundName)
	{
		if (PlaySoundAction is not null)
			PlaySoundAction(soundName);
		else
			_logger?.LogDebug("BaseScriptContext: PlaySound({soundName}) - no handler wired", soundName);
	}
	/// <summary>
	/// Play background music by name.
	/// </summary>
	public virtual void PlayMusic(string musicName)
	{
		if (PlayMusicAction is not null)
			PlayMusicAction(musicName);
		else
			_logger?.LogDebug("BaseScriptContext: PlayMusic({musicName}) - no handler wired", musicName);
	}
	/// <summary>
	/// Stop currently playing music.
	/// </summary>
	public virtual void StopMusic()
	{
		if (StopMusicAction is not null)
			StopMusicAction();
		else
			_logger?.LogDebug("BaseScriptContext: StopMusic() - no handler wired");
	}
}
