using System.Collections.Generic;
using BenMcLean.Wolf3D.Assets.Gameplay;
using Godot;

namespace BenMcLean.Wolf3D.Shared.Menu;

/// <summary>
/// Renders menus using Godot Control nodes.
/// Phase 1: Basic rendering with SubViewport for 320x200 pixel-perfect display.
/// Later phases will add cursor animation, layout helpers, and advanced features.
/// </summary>
public class MenuRenderer
{
	private readonly SubViewport _viewport;
	private readonly Control _canvas;
	/// <summary>
	/// Creates a new MenuRenderer with a 320x200 virtual canvas.
	/// </summary>
	public MenuRenderer()
	{
		// Create 320x200 SubViewport for pixel-perfect rendering
		_viewport = new SubViewport
		{
			Size = new Vector2I(320, 200),
			Disable3D = true,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
		};
		// Create root Control for UI elements
		_canvas = new Control
		{
			CustomMinimumSize = new Vector2(320, 200),
			AnchorsPreset = (int)Control.LayoutPreset.FullRect,
		};
		_viewport.AddChild(_canvas);
	}
	/// <summary>
	/// Gets the SubViewport containing the rendered menu.
	/// Can be used as a texture in 3D space for VR.
	/// </summary>
	public SubViewport Viewport => _viewport;
	/// <summary>
	/// Gets the viewport texture for display.
	/// </summary>
	public ViewportTexture ViewportTexture => _viewport.GetTexture();
	/// <summary>
	/// Clear the menu display.
	/// Removes all child nodes from the canvas.
	/// </summary>
	public void Clear()
	{
		// Remove all children from canvas
		foreach (Node child in _canvas.GetChildren())
			child.QueueFree();
	}
	/// <summary>
	/// Render a menu definition to the canvas.
	/// Phase 1 basic implementation: background + text items.
	/// </summary>
	/// <param name="menuDef">Menu definition from AssetManager.Menus</param>
	/// <param name="selectedIndex">Currently selected item index</param>
	public void RenderMenu(MenuDefinition menuDef, int selectedIndex)
	{
		// Clear previous frame
		Clear();
		// Render background
		if (!string.IsNullOrEmpty(menuDef.Background))
			RenderBackground(menuDef.Background);
		else if (menuDef.BackgroundColor.HasValue)
			RenderBackgroundColor(menuDef.BackgroundColor.Value);
		// Render menu items
		RenderMenuItems(menuDef.Items, selectedIndex, menuDef.Font ?? "BIG");
	}
	/// <summary>
	/// Render a background image from VgaGraph.
	/// </summary>
	/// <param name="imageName">VgaGraph image name (e.g., "TITLEPIC", "C_OPTIONSPIC")</param>
	private void RenderBackground(string imageName)
	{
		// Get texture from SharedAssetManager
		if (!SharedAssetManager.VgaGraph.TryGetValue(imageName, out AtlasTexture texture))
		{
			GD.PrintErr($"ERROR: Background image '{imageName}' not found in VgaGraph");
			return;
		}
		TextureRect background = new()
		{
			Texture = texture,
			Position = Vector2.Zero,
			CustomMinimumSize = new Vector2(320, 200),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.Scale,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
		};
		_canvas.AddChild(background);
	}
	/// <summary>
	/// Render a solid background color.
	/// </summary>
	/// <param name="colorIndex">VGA palette color index (0-255)</param>
	private void RenderBackgroundColor(byte colorIndex)
	{
		// TODO: Convert VGA palette index to Godot Color
		// For now, use a placeholder color
		ColorRect background = new()
		{
			Color = new Color(0.1f, 0.1f, 0.1f), // Dark gray placeholder
			Position = Vector2.Zero,
			Size = new Vector2(320, 200),
		};
		_canvas.AddChild(background);
	}
	/// <summary>
	/// Render menu items as text labels.
	/// Phase 1 basic implementation: simple vertical list.
	/// </summary>
	/// <param name="items">Menu items to render</param>
	/// <param name="selectedIndex">Currently selected item index</param>
	/// <param name="fontName">Font name from SharedAssetManager.Fonts</param>
	private void RenderMenuItems(List<MenuItemDefinition> items, int selectedIndex, string fontName)
	{
		// Get theme from SharedAssetManager
		if (!SharedAssetManager.Themes.TryGetValue(fontName, out Theme theme))
		{
			GD.PrintErr($"ERROR: Theme '{fontName}' not found in SharedAssetManager");
			return;
		}
		// Simple vertical layout for Phase 1
		// Center horizontally, start at Y=80, 16 pixel spacing
		float startY = 80f,
			spacing = 16f;
		for (int i = 0; i < items.Count; i++)
		{
			MenuItemDefinition item = items[i];
			bool isSelected = i == selectedIndex;
			Label label = new()
			{
				Text = item.Text,
				Position = new Vector2(160, startY + i * spacing),
				PivotOffset = Vector2.Zero,
				Theme = theme,
				ZIndex = 10
			};
			// Set color based on selection using theme override
			Color textColor = isSelected
				? new Color(1.0f, 0.0f, 0.0f) // Red for selected
				: new Color(0.9f, 0.9f, 0.9f); // Light gray for normal
			label.AddThemeColorOverride("font_color", textColor);
			// Center the label horizontally
			// We'll adjust position after adding to get text width
			_canvas.AddChild(label);
			// Center the text (Godot calculates size after adding to tree)
			label.Position = new Vector2(160 - label.Size.X / 2, startY + i * spacing);
		}
	}
	/// <summary>
	/// Get screen positions of all rendered menu items.
	/// Used by input system for hover detection.
	/// </summary>
	/// <returns>Array of screen positions (center of each item)</returns>
	public Vector2[] GetMenuItemPositions()
	{
		List<Vector2> positions = [];
		// Find all Label nodes (menu items)
		foreach (Node child in _canvas.GetChildren())
			if (child is Label label)
				positions.Add(label.Position + label.Size / 2);
		return [.. positions];
	}
}
