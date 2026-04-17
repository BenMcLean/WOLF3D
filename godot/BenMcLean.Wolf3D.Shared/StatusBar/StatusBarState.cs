using System;
using System.Collections.Generic;
using BenMcLean.Wolf3D.Assets.Gameplay;
using BenMcLean.Wolf3D.Assets.Menu;

namespace BenMcLean.Wolf3D.Shared.StatusBar;

/// <summary>
/// Runtime state container for status bar display.
/// Holds text labels and named pictures updated by action scripts via SetText/SetPicture.
/// Display-only — all updates are script-driven.
/// </summary>
public class StatusBarState
{
	private readonly StatusBarDefinition _definition;
	// Named text states (id → current text content)
	private readonly Dictionary<string, string> _texts = [];
	// Named picture states (id → current VgaGraph pic name)
	private readonly Dictionary<string, string> _pictures = [];
	/// <summary>
	/// Event fired when a named text label changes.
	/// Parameters: (id, newContent)
	/// </summary>
	public event Action<string, string> TextChanged;
	/// <summary>
	/// Event fired when a named picture changes.
	/// Parameters: (id, newVgaGraphPicName)
	/// </summary>
	public event Action<string, string> PicChanged;
	/// <summary>
	/// Creates a new StatusBarState initialized from a StatusBarDefinition.
	/// </summary>
	/// <param name="definition">The status bar definition containing initial values</param>
	public StatusBarState(StatusBarDefinition definition)
	{
		_definition = definition ?? throw new ArgumentNullException(nameof(definition));
		// Initialize texts from definition (content keyed by Id)
		foreach (TextDefinition text in _definition.Texts)
			if (!string.IsNullOrEmpty(text.Id))
				_texts[text.Id] = text.Content ?? string.Empty;
		// Initialize pictures from definition (only those with an Id can be targeted by Lua)
		foreach (PictureDefinition picture in _definition.Pictures)
			if (!string.IsNullOrEmpty(picture.Id))
				_pictures[picture.Id] = picture.Name ?? string.Empty;
	}
	/// <summary>
	/// Sets the content for a named status bar text label.
	/// Fires TextChanged for the presentation layer to update the displayed text.
	/// </summary>
	/// <param name="id">Text Id (e.g., "Health", "Ammo")</param>
	/// <param name="content">New text content</param>
	public void SetText(string id, string content)
	{
		if (_texts.TryGetValue(id, out string current) && current == content)
			return;
		_texts[id] = content;
		TextChanged?.Invoke(id, content);
	}
	/// <summary>
	/// Gets the current content for a named status bar text label.
	/// </summary>
	/// <param name="id">Text Id (e.g., "Health", "Ammo")</param>
	/// <returns>Current text content, or empty string if not found</returns>
	public string GetText(string id) =>
		_texts.TryGetValue(id, out string text) ? text : string.Empty;
	/// <summary>
	/// Sets the current pic name for a named status bar picture.
	/// Fires PicChanged for the presentation layer to swap the displayed texture.
	/// </summary>
	/// <param name="name">Picture Id (e.g., "Face", "GoldKey")</param>
	/// <param name="picName">New VgaGraph pic name (e.g., "FACE1APIC")</param>
	public void SetPic(string name, string picName)
	{
		if (_pictures.TryGetValue(name, out string current) && current == picName)
			return;
		_pictures[name] = picName;
		PicChanged?.Invoke(name, picName);
	}
	/// <summary>
	/// Gets the current pic name for a named status bar picture.
	/// </summary>
	/// <param name="name">Picture Id (e.g., "Face")</param>
	/// <returns>Current VgaGraph pic name, or empty string if not found</returns>
	public string GetPic(string name) =>
		_pictures.TryGetValue(name, out string pic) ? pic : string.Empty;
	/// <summary>
	/// Gets all current picture states as a read-only dictionary.
	/// </summary>
	public IReadOnlyDictionary<string, string> Pictures => _pictures;
	/// <summary>
	/// Gets the status bar definition this state is based on.
	/// </summary>
	public StatusBarDefinition Definition => _definition;
}
