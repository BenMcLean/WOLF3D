using System;
using BenMcLean.Wolf3D.Assets.Gameplay;
using BenMcLean.Wolf3D.Simulator;

namespace BenMcLean.Wolf3D.Shared.StatusBar;

/// <summary>
/// Owns a StatusBarState and manages its subscription to simulator events.
/// Lives at the Root level and is shared between ActionRoom and MenuRoom so quiz menus
/// can display the live status bar without re-subscribing to the simulator.
/// </summary>
public class StatusBarController(StatusBarDefinition definition)
{
	public StatusBarState State { get; } = new StatusBarState(definition ?? throw new ArgumentNullException(nameof(definition)));
	private Action<StatusBarPicChangedEvent> _onPicChanged;
	private Action<StatusBarTextChangedEvent> _onTextChanged;
	private Simulator.Simulator _simulator;
	/// <summary>
	/// Subscribes to a simulator's status bar events and syncs current state.
	/// Automatically unsubscribes from any previous simulator first.
	/// </summary>
	public void Subscribe(Simulator.Simulator simulator)
	{
		Unsubscribe();
		_simulator = simulator ?? throw new ArgumentNullException(nameof(simulator));
		_onPicChanged = evt => State.SetPic(evt.Name, evt.PicName);
		_onTextChanged = evt => State.SetText(evt.Id, evt.Content);
		_simulator.StatusBarPicChanged += _onPicChanged;
		_simulator.StatusBarTextChanged += _onTextChanged;
		_simulator.SyncStatusBarState();
	}
	/// <summary>
	/// Removes all event subscriptions from the current simulator.
	/// </summary>
	public void Unsubscribe()
	{
		if (_simulator is null)
			return;
		if (_onPicChanged is not null)
			_simulator.StatusBarPicChanged -= _onPicChanged;
		if (_onTextChanged is not null)
			_simulator.StatusBarTextChanged -= _onTextChanged;
		_simulator = null;
		_onPicChanged = null;
		_onTextChanged = null;
	}
}
