using System;
using System.Collections.Generic;
using BenMcLean.Wolf3D.Assets.Gameplay;

namespace BenMcLean.Wolf3D.Shared.Menu;

/// <summary>
/// Controls what happens when a button is pressed during a ticker step.
/// </summary>
public enum SequenceSkipBehavior
{
	/// <summary>
	/// Pressing any button during a ticker completes ALL remaining steps.
	/// Matches Wolf3D's "goto done" behavior in WL_INTER.C.
	/// </summary>
	SkipAll,
	/// <summary>
	/// Pressing any button during a ticker completes only the current step.
	/// Useful for credits, title screens, and other non-intermission sequences.
	/// </summary>
	SkipCurrent
}

/// <summary>
/// A step in a menu presentation sequence.
/// </summary>
public interface ISequenceStep
{
	/// <summary>
	/// Whether this step has finished.
	/// </summary>
	bool IsComplete { get; }

	/// <summary>
	/// Update this step. Called each frame while the step is active.
	/// </summary>
	/// <param name="delta">Time since last frame in seconds</param>
	/// <param name="anyButtonPressed">True if any button was pressed this frame</param>
	/// <param name="skipBehavior">The sequence's skip behavior setting</param>
	/// <returns>True if the sequence should skip all remaining steps (SkipAll triggered)</returns>
	bool Update(float delta, bool anyButtonPressed, SequenceSkipBehavior skipBehavior);

	/// <summary>
	/// Immediately complete this step, jumping to its final state.
	/// Called when the sequence is being skipped.
	/// </summary>
	void Complete();
}

/// <summary>
/// Animates a ticker counting from 0 to a target value at 70Hz.
/// Matches WL_INTER.C's ratio counting loops.
/// </summary>
public class TickerSequenceStep : ISequenceStep
{
	#region Data
	private readonly string _tickerName;
	private readonly int _targetValue;
	private readonly MenuTickerDefinition _tickerDef;
	private readonly Action<string, string> _updateTickerAction;
	private readonly Action<string> _playSoundAction;
	private int _currentValue;
	private float _elapsed;
	private bool _isComplete,
		_doneHandled;
	#endregion Data
	/// <summary>
	/// Effective tick rate for ticker animation.
	/// WL_INTER.C's loop calls VW_UpdateScreen (vsync) + RollDelay (waits for TimeCount),
	/// making the effective rate roughly half of TickBase=70, i.e., ~35 increments/second.
	/// </summary>
	//TODO Use Constants.TicsPerSecond and make it a double to match Godot 4's time
	private const float TickInterval = 1.0f / 35.0f;

	public bool IsComplete => _isComplete;

	/// <summary>
	/// Creates a new TickerSequenceStep.
	/// </summary>
	/// <param name="tickerName">Name of the ticker to animate</param>
	/// <param name="targetValue">Final value (0-100 for ratios)</param>
	/// <param name="tickerDef">Ticker definition with sound names and tick interval</param>
	/// <param name="updateTickerAction">Delegate to update the ticker display</param>
	/// <param name="playSoundAction">Delegate to play a sound by name</param>
	public TickerSequenceStep(
		string tickerName,
		int targetValue,
		MenuTickerDefinition tickerDef,
		Action<string, string> updateTickerAction,
		Action<string> playSoundAction)
	{
		_tickerName = tickerName;
		_targetValue = targetValue;
		_tickerDef = tickerDef;
		_updateTickerAction = updateTickerAction;
		_playSoundAction = playSoundAction;
		_currentValue = 0;
		_elapsed = 0f;
		_isComplete = false;
		_doneHandled = false;

		// Display initial value
		_updateTickerAction?.Invoke(_tickerName, "0");
	}

	public bool Update(float delta, bool anyButtonPressed, SequenceSkipBehavior skipBehavior)
	{
		if (_isComplete)
			return false;

		if (anyButtonPressed)
		{
			Complete();
			return skipBehavior == SequenceSkipBehavior.SkipAll;
		}

		_elapsed += delta;

		// Advance one tick per 1/70th second (matching Wolf3D's RollDelay)
		while (_elapsed >= TickInterval && _currentValue < _targetValue)
		{
			_currentValue++;
			_elapsed -= TickInterval;

			// Update display
			_updateTickerAction?.Invoke(_tickerName, _currentValue.ToString());

			// Play tick sound at configured intervals (e.g., every 10%)
			if (_tickerDef?.TickSound is not null &&
				_tickerDef.TickInterval > 0 &&
				_currentValue % _tickerDef.TickInterval == 0)
				_playSoundAction?.Invoke(_tickerDef.TickSound);
		}

		// Check if counting finished naturally
		if (_currentValue >= _targetValue && !_doneHandled)
			HandleDone();

		return false;
	}

	public void Complete()
	{
		_currentValue = _targetValue;
		_updateTickerAction?.Invoke(_tickerName, _targetValue.ToString());
		if (!_doneHandled)
			HandleDone();
	}

	/// <summary>
	/// Plays the appropriate completion sound.
	/// WL_INTER.C: PERCENT100SND for 100%, NOBONUSSND for 0%, D_INCSND otherwise.
	/// </summary>
	private void HandleDone()
	{
		_doneHandled = true;
		_isComplete = true;
		if (_targetValue == 100 && _tickerDef?.PerfectSound is not null)
			_playSoundAction?.Invoke(_tickerDef.PerfectSound);
		else if (_targetValue == 0 && _tickerDef?.NoBonusSound is not null)
			_playSoundAction?.Invoke(_tickerDef.NoBonusSound);
		else if (_tickerDef?.DoneSound is not null)
			_playSoundAction?.Invoke(_tickerDef.DoneSound);
	}
}

/// <summary>
/// Waits for a specified duration. Skippable by pressing any button.
/// Matches WL_INTER.C's timed pauses (e.g., 2*TickBase between phases).
/// </summary>
/// <remarks>
/// Creates a new DelaySequenceStep.
/// </remarks>
/// <param name="Seconds">Duration to wait in seconds</param>
public class DelaySequenceStep(float Seconds) : ISequenceStep
{
	private float _elapsed = 0f;
	public bool IsComplete { get; private set; }
	public bool Update(float delta, bool anyButtonPressed, SequenceSkipBehavior skipBehavior)
	{
		if (IsComplete)
			return false;
		if (anyButtonPressed)
		{
			Complete();
			return false;
		}
		_elapsed += delta;
		if (_elapsed >= Seconds)
			Complete();
		return false;
	}
	public void Complete() => IsComplete = true;
}

/// <summary>
/// Waits for any button press (or optional timeout), then executes a callback.
/// Used for &lt;Pause&gt; elements that run Lua scripts on completion.
/// Runs after tickers complete but before interactive MenuItems.
/// </summary>
/// <remarks>
/// Creates a new PauseSequenceStep.
/// </remarks>
/// <param name="Duration">Optional timeout in seconds. If null or &lt;= 0, waits indefinitely for input.</param>
/// <param name="OnComplete">Callback to execute when the pause completes (e.g., Lua script execution).</param>
public class PauseSequenceStep(float? Duration, Action OnComplete) : ISequenceStep
{
	private readonly float _duration = Duration ?? 0f;
	private readonly bool _hasTimeout = Duration.HasValue && Duration.Value > 0f;
	private float _elapsed;
	public bool IsComplete { get; private set; }
	public bool Update(float delta, bool anyButtonPressed, SequenceSkipBehavior skipBehavior)
	{
		if (IsComplete)
			return false;
		if (anyButtonPressed)
		{
			Complete();
			return false;
		}
		if (_hasTimeout)
		{
			_elapsed += delta;
			if (_elapsed >= _duration)
				Complete();
		}
		return false;
	}
	public void Complete()
	{
		if (IsComplete)
			return;
		IsComplete = true;
		OnComplete?.Invoke();
	}
}

/// <summary>
/// Manages a sequence of presentation steps that run before interactive menu mode.
/// Supports ticker animations, timed delays, and press-any-button pauses.
/// The sequence is processed frame-by-frame and blocks normal menu input until complete.
/// </summary>
public class MenuSequence
{
	private readonly Queue<ISequenceStep> _steps = new();
	private ISequenceStep _currentStep;

	/// <summary>
	/// Controls what happens when a button is pressed during a ticker step.
	/// Default is SkipAll (Wolf3D intermission behavior).
	/// </summary>
	public SequenceSkipBehavior SkipBehavior { get; set; } = SequenceSkipBehavior.SkipAll;

	/// <summary>
	/// Whether the sequence has finished (no more steps to process).
	/// </summary>
	public bool IsComplete => _currentStep == null && _steps.Count == 0;

	/// <summary>
	/// Whether the sequence has any steps queued.
	/// </summary>
	public bool HasSteps => _currentStep != null || _steps.Count > 0;

	/// <summary>
	/// Add a step to the end of the sequence.
	/// </summary>
	/// <param name="step">The step to enqueue</param>
	public void Enqueue(ISequenceStep step) => _steps.Enqueue(step);

	/// <summary>
	/// Process the current step. Called each frame by MenuManager.
	/// </summary>
	/// <param name="delta">Time since last frame in seconds</param>
	/// <param name="anyButtonPressed">True if any button was pressed this frame</param>
	public void Update(float delta, bool anyButtonPressed)
	{
		// Advance to next step if needed
		if (_currentStep is null || _currentStep.IsComplete)
			if (_steps.Count > 0)
				_currentStep = _steps.Dequeue();
			else
			{
				_currentStep = null;
				return;
			}
		// Update current step
		bool skipAll = _currentStep.Update(delta, anyButtonPressed, SkipBehavior);
		if (skipAll)
		{
			CompleteAll();
			return;
		}
		// If current step finished, try to advance immediately
		if (_currentStep.IsComplete && _steps.Count > 0)
			_currentStep = _steps.Dequeue();
		else if (_currentStep.IsComplete)
			_currentStep = null;
	}

	/// <summary>
	/// Complete all remaining steps immediately.
	/// Sets all tickers to their final values and clears the queue.
	/// Matches WL_INTER.C's "goto done" behavior.
	/// </summary>
	public void CompleteAll()
	{
		// Complete current step
		if (_currentStep is not null && !_currentStep.IsComplete)
			_currentStep.Complete();
		_currentStep = null;
		// Complete all queued steps
		while (_steps.Count > 0)
		{
			ISequenceStep step = _steps.Dequeue();
			step.Complete();
		}
	}
}
