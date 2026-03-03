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
	private readonly Dictionary<string, Label> _numberLabels = [];
	private readonly Dictionary<string, TextureRect> _keyTextures = [];
	private readonly Dictionary<string, TextureRect> _pictureTextures = [];
	private TextureRect _weaponTexture;
	private int _lastWeapon = -1;
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
		_state.ValueChanged += OnValueChanged;
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
		RenderNumbers();
		RenderKeys();
		RenderPictures();
		RenderWeapon();
	}
	/// <summary>
	/// Clears all rendered elements.
	/// </summary>
	private void Clear()
	{
		foreach (Node child in _canvas.GetChildren())
			child.QueueFree();
		_numberLabels.Clear();
		_keyTextures.Clear();
		_pictureTextures.Clear();
		_weaponTexture = null;
		_lastWeapon = -1;
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
	/// Renders all numeric displays.
	/// Uses the "N" PicFont for digit rendering.
	/// </summary>
	private void RenderNumbers()
	{
		// Get the "N" PicFont theme for number display
		if (!SharedAssetManager.Themes.TryGetValue("N", out Theme numberTheme))
		{
			GD.PrintErr("ERROR: PicFont 'N' not found in SharedAssetManager - cannot render status bar numbers");
			return;
		}
		foreach (StatusBarNumberDefinition numberDef in _definition.Numbers)
		{
			// Skip non-rendered values (internal only) and key-style displays
			if (!numberDef.IsRendered || numberDef.IsKeyDisplay)
				continue;
			int value = _state.GetValue(numberDef.Name);
			string text = FormatNumber(value, numberDef.Digits);
			// X coordinate in XML is the RIGHT edge of the number field (for right-justified display)
			// Each digit is 8 pixels wide, so offset left by Digits * 8
			int startX = numberDef.X.Value - (numberDef.Digits * 8);
			Label label = new()
			{
				Text = text,
				Theme = numberTheme,
				Position = new Vector2(startX, numberDef.Y.Value),
				TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
				ZIndex = 10,
			};
			_canvas.AddChild(label);
			_numberLabels[numberDef.Name] = label;
		}
	}
	/// <summary>
	/// Renders key displays (Gold Key, Silver Key).
	/// Uses Have/Empty pics based on value.
	/// </summary>
	private void RenderKeys()
	{
		foreach (StatusBarNumberDefinition numberDef in _definition.Numbers)
		{
			// Only process key-style displays
			if (!numberDef.IsRendered || !numberDef.IsKeyDisplay)
				continue;
			int value = _state.GetValue(numberDef.Name);
			string picName = value > 0 ? numberDef.Have : numberDef.Empty;
			if (string.IsNullOrEmpty(picName))
				continue;
			if (!SharedAssetManager.VgaGraph.TryGetValue(picName, out AtlasTexture texture))
			{
				GD.PrintErr($"ERROR: Key picture '{picName}' not found in VgaGraph");
				continue;
			}
			TextureRect keyTexture = new()
			{
				Texture = texture,
				Position = new Vector2(numberDef.X.Value, numberDef.Y.Value),
				Size = texture.Region.Size,
				TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
				ZIndex = 10,
			};
			_canvas.AddChild(keyTexture);
			_keyTextures[numberDef.Name] = keyTexture;
		}
	}
	/// <summary>
	/// Renders all named picture elements from the StatusBar definition.
	/// Each picture's initial pic name comes from the definition; runtime updates
	/// arrive via UpdatePicture() when the simulator fires StatusBarPicChangedEvent.
	/// </summary>
	private void RenderPictures()
	{
		foreach (MenuPictureDefinition picDef in _definition.Pictures)
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
	/// Renders the current weapon icon.
	/// </summary>
	private void RenderWeapon()
	{
		if (_definition.Weapons is null)
			return;
		StatusBarWeaponDefinition weaponDef = null;
		foreach (StatusBarWeaponDefinition w in _definition.Weapons.Weapons)
			if (w.Number == _state.CurrentWeapon)
			{
				weaponDef = w;
				break;
			}
		if (string.IsNullOrEmpty(weaponDef?.Pic))
			return;
		if (!SharedAssetManager.VgaGraph.TryGetValue(weaponDef.Pic, out AtlasTexture texture))
		{
			GD.PrintErr($"ERROR: Weapon picture '{weaponDef.Pic}' not found in VgaGraph");
			return;
		}
		_weaponTexture = new TextureRect
		{
			Texture = texture,
			Position = new Vector2(_definition.Weapons.X, _definition.Weapons.Y),
			Size = texture.Region.Size,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			ZIndex = 10,
		};
		_canvas.AddChild(_weaponTexture);
		_lastWeapon = _state.CurrentWeapon;
	}
	/// <summary>
	/// Updates the display. Call this each frame.
	/// Efficiently updates only changed elements.
	/// </summary>
	public void Update()
	{
		// Update weapon if changed
		if (_state.CurrentWeapon != _lastWeapon)
			UpdateWeapon();
	}
	/// <summary>
	/// Handles value change events from the state.
	/// Updates only the affected display element.
	/// </summary>
	private void OnValueChanged(string name, int newValue)
	{
		// Update number label if exists
		if (_numberLabels.TryGetValue(name, out Label label))
		{
			StatusBarNumberDefinition numberDef = _definition.GetNumber(name);
			if (numberDef is not null)
				label.Text = FormatNumber(newValue, numberDef.Digits);
		}
		// Update key texture if exists
		if (_keyTextures.TryGetValue(name, out TextureRect keyTexture))
		{
			StatusBarNumberDefinition numberDef = _definition.GetNumber(name);
			if (numberDef is not null)
			{
				string picName = newValue > 0 ? numberDef.Have : numberDef.Empty;
				if (!string.IsNullOrEmpty(picName) && SharedAssetManager.VgaGraph.TryGetValue(picName, out AtlasTexture texture))
					keyTexture.Texture = texture;
			}
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
	/// Updates the weapon texture based on current weapon.
	/// </summary>
	private void UpdateWeapon()
	{
		if (_weaponTexture is null || _definition.Weapons is null)
			return;
		StatusBarWeaponDefinition weaponDef = null;
		foreach (StatusBarWeaponDefinition w in _definition.Weapons.Weapons)
			if (w.Number == _state.CurrentWeapon)
			{
				weaponDef = w;
				break;
			}
		if (weaponDef != null && !string.IsNullOrEmpty(weaponDef.Pic) &&
			SharedAssetManager.VgaGraph.TryGetValue(weaponDef.Pic, out AtlasTexture texture))
		{
			_weaponTexture.Texture = texture;
			_weaponTexture.Size = texture.Region.Size;
		}
		_lastWeapon = _state.CurrentWeapon;
	}
	/// <summary>
	/// Formats a number for display with right-justification.
	/// Uses space padding to maintain consistent width.
	/// </summary>
	/// <param name="value">The numeric value</param>
	/// <param name="digits">Number of digits to display</param>
	/// <returns>Formatted string with space padding</returns>
	private static string FormatNumber(int value, int digits)
	{
		if (digits <= 0)
			return value.ToString();
		return value.ToString().PadLeft(digits, ' ');
	}
	/// <summary>
	/// Disposes resources and unsubscribes from events.
	/// </summary>
	public void Dispose()
	{
		_state.ValueChanged -= OnValueChanged;
		_state.PicChanged -= UpdatePicture;
		_viewport?.QueueFree();
	}
}
