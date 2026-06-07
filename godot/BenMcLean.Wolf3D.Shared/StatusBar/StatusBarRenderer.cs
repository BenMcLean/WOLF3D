using System;
using System.Collections.Generic;
using BenMcLean.Wolf3D.Assets.Gameplay;
using BenMcLean.Wolf3D.Assets.Menu;
using BenMcLean.Wolf3D.Shared.Layout;
using BenMcLean.Wolf3D.Shared.Text;
using Godot;

namespace BenMcLean.Wolf3D.Shared.StatusBar;

/// <summary>
/// Renders the status bar using Godot Control nodes into a 320×40 canvas.
/// Add Canvas to a SubViewport of your choice to display it.
/// </summary>
public class StatusBarRenderer
{
	private readonly Control _canvas;
	private readonly CanvasLayoutRenderer _layoutRenderer;
	private readonly StatusBarState _state;
	private readonly StatusBarDefinition _definition;
	private readonly Dictionary<string, Label> _textLabels = [];
	private readonly Dictionary<string, (TextDefinition Definition, Theme Theme)> _textLayout = [];
	private readonly Dictionary<string, TextureRect> _pictureTextures = [];
	/// <summary>
	/// Creates a new StatusBarRenderer with a 320×40 canvas.
	/// Add Canvas to a SubViewport of your choice to render it.
	/// </summary>
	/// <param name="state">The status bar state to render</param>
	public StatusBarRenderer(StatusBarState state)
	{
		_state = state ?? throw new ArgumentNullException(nameof(state));
		_definition = _state.Definition ?? throw new ArgumentException("State must have a definition", nameof(state));
		// Create root Control for UI elements — no anchors so callers can position it freely
		_canvas = new Control
		{
			CustomMinimumSize = new Vector2(320, 40),
			Size = new Vector2(320, 40),
		};
		_layoutRenderer = new CanvasLayoutRenderer(_canvas, 320, 40);
		// Subscribe to state changes
		_state.TextChanged += UpdateText;
		_state.PicChanged += UpdatePicture;
		// Initial render
		RenderAll();
	}
	/// <summary>
	/// The 320×40 Control containing the rendered status bar UI.
	/// Add this to a SubViewport of your choice so it renders into that viewport.
	/// In a combined viewport position it at (0, AutomapRenderer.ViewHeight).
	/// </summary>
	public Control Canvas => _canvas;
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
		_layoutRenderer.RenderAll(_definition, new CanvasLayoutRenderOptions
		{
			BackgroundPictureName = _definition.BackgroundPic,
			BackgroundZIndex = 0,
			BoxFillZIndex = 5,
			BoxBorderZIndex = 6,
			PictureZIndex = 10,
			FallbackFontName = "N",
			RenderTextsBeforePictures = true,
			PictureNameProvider = picDef =>
			{
				string picName = !string.IsNullOrEmpty(picDef.Id) ? _state.GetPic(picDef.Id) : string.Empty;
				return string.IsNullOrEmpty(picName) ? picDef.Name : picName;
			},
			AfterAddPicture = (picDef, pictureRect) =>
			{
				if (!string.IsNullOrEmpty(picDef.Id))
					_pictureTextures[picDef.Id] = pictureRect;
			},
			TextContentProvider = textDef => !string.IsNullOrEmpty(textDef.Id) ? _state.GetText(textDef.Id) : textDef.Content,
			TextColorProvider = textDef => textDef.Color,
			AfterAddText = (textDef, label, theme, _) =>
			{
				label.ZIndex = 10;
				label.PivotOffset = Vector2.Zero;
				if (!string.IsNullOrEmpty(textDef.Id))
				{
					_textLabels[textDef.Id] = label;
					_textLayout[textDef.Id] = (textDef, theme);
				}
			}
		});
	}
	/// <summary>
	/// Clears all rendered elements.
	/// </summary>
	private void Clear()
	{
		foreach (Node child in _canvas.GetChildren())
			child.QueueFree();
		_textLabels.Clear();
		_textLayout.Clear();
		_pictureTextures.Clear();
	}
	/// <summary>
	/// Updates a named text label's content.
	/// Called when the simulator fires StatusBarTextChangedEvent via _state.TextChanged.
	/// </summary>
	private void UpdateText(string id, string content)
	{
		if (_textLabels.TryGetValue(id, out Label label))
		{
			label.Text = content;
			if (_textLayout.TryGetValue(id, out (TextDefinition Definition, Theme Theme) layout) && layout.Theme is not null)
				label.Position = TextLayoutHelper.GetPosition(
					textDef: layout.Definition,
					theme: layout.Theme,
					content: content,
					canvasWidth: 320,
					canvasHeight: 40);
		}
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
	/// Unsubscribes from state events. Call when the presentation layer exits the tree.
	/// The canvas node itself is freed by whichever SubViewport owns it.
	/// </summary>
	public void Dispose()
	{
		_state.TextChanged -= UpdateText;
		_state.PicChanged -= UpdatePicture;
	}
}
