using System;
using BenMcLean.Wolf3D.Assets.Sound;
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
	/// Active audio table used to resolve playable sound names for this script context.
	/// Returns null when the current game has no matching logical sound.
	/// </summary>
	public AudioT AudioT { get; set; }
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
		soundName = ResolveForPlayback(soundName);
		if (PlaySoundAction is not null)
			PlaySoundAction(soundName);
		else
			_logger?.LogDebug("BaseScriptContext: PlaySound({soundName}) - no handler wired", soundName);
	}
	/// <summary>
	/// Returns true if the requested sound name can be resolved by the host's sound lookup rules.
	/// </summary>
	public virtual bool HasSound(string soundName) =>
		TryResolveSoundName(soundName) is not null;
	/// <summary>
	/// Returns the first playable sound name from the requested primary/fallback pair.
	/// If neither resolves, returns the original primary name unchanged so callers preserve current behavior.
	/// </summary>
	public virtual string ResolveSound(string soundName, string fallbackSoundName)
	{
		if (TryResolveSoundName(soundName) is string resolvedPrimary)
			return resolvedPrimary;
		if (TryResolveSoundName(fallbackSoundName) is string resolvedFallback)
			return resolvedFallback;
		return soundName;
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
	protected virtual string TryResolveSoundName(string soundName) =>
		string.IsNullOrWhiteSpace(soundName)
			? null
			: AudioT?.ResolvePlayableSoundName(soundName);
	protected string ResolveForPlayback(string soundName) =>
		TryResolveSoundName(soundName) ?? soundName;
	#region Bitwise Utilities
	/// <summary>
	/// Bitwise right shift for Lua (value >> bits).
	/// Lua doesn't have native bit shift operators, so we provide them here.
	/// </summary>
	public int BitShiftRight(int value, int bits) => value >> bits;
	/// <summary>
	/// Bitwise left shift for Lua (value << bits).
	/// Lua doesn't have native bit shift operators, so we provide them here.
	/// </summary>
	public int BitShiftLeft(int value, int bits) => value << bits;
	#endregion Bitwise Utilities
}
