using System;
using System.Collections.Generic;
using BenMcLean.Wolf3D.Assets.Gameplay;
using Godot;

namespace BenMcLean.Wolf3D.Shared.StatusBar;

/// <summary>
/// Renders the status bar using Godot Control nodes.
/// Creates a 320x40 SubViewport for pixel-perfect display.
/// Follows the MenuRenderer pattern for consistency.
/// </summary>
public class StatusBarRenderer
{
	private readonly SubViewport _viewport;
	private readonly Control _canvas;
	private readonly StatusBarState _state;
	private readonly StatusBarDefinition _definition;
	private readonly Dictionary<string, Label> _textLabels = [];
	private readonly Dictionary<string, TextureRect> _pictureTextures = [];
	/// <summary>
	/// Creates a new StatusBarRenderer with a 320x40 virtual canvas.
	/// </summary>
	/// <param name="state">The status bar state to render</param>
	public StatusBarRenderer(StatusBarState state)
	{
		_state = state ?? throw new ArgumentNullException(nameof(state));
		_definition = _state.Definition ?? throw new ArgumentException("State must have a definition", nameof(state));
		// Create 320x40 SubViewport for pixel-perfect rendering
		_viewport = new SubViewport
		{
			Size = new Vector2I(320, 40),
			Disable3D = true,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
		};
		// Create root Control for UI elements
		_canvas = new Control
		{
			CustomMinimumSize = new Vector2(320, 40),
			AnchorsPreset = (int)Control.LayoutPreset.FullRect,
		};
		_viewport.AddChild(_canvas);
		// Subscribe to state changes
		_state.TextChanged += UpdateText;
		_state.PicChanged += UpdatePicture;
		// Initial render
		RenderAll();
	}
	/// <summary>
	/// Gets the SubViewport containing the rendered status bar.
	/// Can be used as a texture in 3D space for VR.
	/// </summary>
	public SubViewport Viewport => _viewport;
	/// <summary>
	/// Gets the viewport texture for display.
	/// </summary>
	public ViewportTexture ViewportTexture => _viewport.GetTexture();
	/// <summary>
	/// Gets the current state being rendered.
	/// </summary>
	public StatusBarState State => _state;
	/// <summary>
	/// Performs full re-render of the status bar.
	/// Called on initialization and when major state changes occur.
	/// </summary>
	public void RenderAll()
	{
		Clear();
		RenderBackground();
		RenderTexts();
		RenderPictures();
	}
	/// <summary>
	/// Clears all rendered elements.
	/// </summary>
	private void Clear()
	{
		foreach (Node child in _canvas.GetChildren())
			child.QueueFree();
		_textLabels.Clear();
		_pictureTextures.Clear();
	}
	/// <summary>
	/// Renders the status bar background image.
	/// </summary>
	private void RenderBackground()
	{
		if (string.IsNullOrEmpty(_definition.BackgroundPic))
			return;
		if (!SharedAssetManager.VgaGraph.TryGetValue(_definition.BackgroundPic, out AtlasTexture texture))
		{
			GD.PrintErr($"ERROR: Status bar background '{_definition.BackgroundPic}' not found in VgaGraph");
			return;
		}
		TextureRect background = new()
		{
			Texture = texture,
			Position = Vector2.Zero,
			Size = new Vector2(320, 40),
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			ZIndex = 0,
		};
		_canvas.AddChild(background);
	}
	/// <summary>
	/// Renders all text label elements from the StatusBar definition.
	/// Each label's initial content comes from the definition; runtime updates
	/// arrive via UpdateText() when the simulator fires StatusBarTextChangedEvent.
	/// </summary>
	private void RenderTexts()
	{
		if (_definition.Texts is null || _definition.Texts.Count == 0)
			return;
		string fontName = _definition.Font != 0
			? _definition.Font.ToString()
			: "N";
		if (!SharedAssetManager.Themes.TryGetValue(fontName, out Theme theme))
		{
			GD.PrintErr($"ERROR: Theme '{fontName}' not found in SharedAssetManager - cannot render status bar texts");
			return;
		}
		foreach (TextDefinition textDef in _definition.Texts)
		{
			string content = !string.IsNullOrEmpty(textDef.Id)
				? _state.GetText(textDef.Id)
				: textDef.Content;
			Label label = new()
			{
				Text = content ?? textDef.Content ?? string.Empty,
				Theme = theme,
				ZIndex = 10,
				LabelSettings = new LabelSettings
				{
					Font = theme.DefaultFont,
					FontSize = theme.DefaultFontSize,
					LineSpacing = 0,
				}
			};
			label.Position = new Vector2(textDef.XValue, textDef.YValue);
			_canvas.AddChild(label);
			label.PivotOffset = Vector2.Zero;
			if (!string.IsNullOrEmpty(textDef.Id))
				_textLabels[textDef.Id] = label;
		}
	}
	/// <summary>
	/// Renders all named picture elements from the StatusBar definition.
	/// Each picture's initial pic name comes from the definition; runtime updates
	/// arrive via UpdatePicture() when the simulator fires StatusBarPicChangedEvent.
	/// </summary>
	private void RenderPictures()
	{
		foreach (PictureDefinition picDef in _definition.Pictures)
		{
			string picName = !string.IsNullOrEmpty(picDef.Id) ? _state.GetPic(picDef.Id) : string.Empty;
			if (string.IsNullOrEmpty(picName))
				picName = picDef.Name;
			if (string.IsNullOrEmpty(picName))
				continue;
			if (!SharedAssetManager.VgaGraph.TryGetValue(picName, out AtlasTexture texture))
			{
				GD.PrintErr($"ERROR: Status bar picture '{picName}' (Id='{picDef.Id}') not found in VgaGraph");
				continue;
			}
			TextureRect pictureRect = new()
			{
				Texture = texture,
				Position = new Vector2(picDef.XValue, picDef.YValue),
				Size = texture.Region.Size,
				TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
				ZIndex = 10,
			};
			_canvas.AddChild(pictureRect);
			if (!string.IsNullOrEmpty(picDef.Id))
				_pictureTextures[picDef.Id] = pictureRect;
		}
	}
	/// <summary>
	/// Updates a named text label's content.
	/// Called when the simulator fires StatusBarTextChangedEvent via _state.TextChanged.
	/// </summary>
	private void UpdateText(string id, string content)
	{
		if (_textLabels.TryGetValue(id, out Label label))
			label.Text = content;
	}
	/// <summary>
	/// Updates a named picture texture to a new VgaGraph pic.
	/// Called when the simulator fires StatusBarPicChangedEvent via _state.PicChanged.
	/// </summary>
	private void UpdatePicture(string name, string picName)
	{
		if (!_pictureTextures.TryGetValue(name, out TextureRect pictureRect))
			return;
		if (SharedAssetManager.VgaGraph.TryGetValue(picName, out AtlasTexture texture))
		{
			pictureRect.Texture = texture;
			pictureRect.Size = texture.Region.Size;
		}
	}
	/// <summary>
	/// Disposes resources and unsubscribes from events.
	/// </summary>
	public void Dispose()
	{
		_state.TextChanged -= UpdateText;
		_state.PicChanged -= UpdatePicture;
		_viewport?.QueueFree();
	}
}
