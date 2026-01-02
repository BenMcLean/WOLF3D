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
	private readonly List<AtlasTexture> _temporaryAtlasTextures = [];
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
	/// Removes all child nodes from the canvas and disposes temporary resources.
	/// </summary>
	public void Clear()
	{
		// Remove all children from canvas
		foreach (Node child in _canvas.GetChildren())
			child.QueueFree();
		// Dispose temporary AtlasTextures created for stripes
		foreach (AtlasTexture texture in _temporaryAtlasTextures)
			texture?.Dispose();
		_temporaryAtlasTextures.Clear();
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
		// Render background color if specified
		if (menuDef.BorderColor.HasValue)
			RenderBackgroundColor(menuDef.BorderColor.Value);
		// Render pictures (backgrounds and decorative images - WL_MENU.C:DrawMainMenu)
		RenderPictures(menuDef);
		// Render menu boxes (WL_MENU.C:DrawWindow)
		RenderMenuBoxes(menuDef);
		// Render menu items
		RenderMenuItems(menuDef, selectedIndex);
		// Render cursor (WL_MENU.C:DrawMenuGun)
		RenderCursor(menuDef, selectedIndex);
	}
	/// <summary>
	/// Render a solid background color.
	/// </summary>
	/// <param name="colorIndex">VGA palette color index (0-255)</param>
	private void RenderBackgroundColor(byte colorIndex)
	{
		// Get color from palette
		Color color = SharedAssetManager.GetPaletteColor(colorIndex);
		ColorRect background = new()
		{
			Color = color,
			Position = Vector2.Zero,
			Size = new Vector2(320, 200),
		};
		_canvas.AddChild(background);
	}
	/// <summary>
	/// Render decorative pictures from VgaGraph.
	/// WL_MENU.C:DrawMainMenu - VWB_DrawPic(112,GAMEVER_MOUSELBACKY,C_MOUSELBACKPIC)
	/// </summary>
	/// <param name="menuDef">Menu definition containing pictures list</param>
	private void RenderPictures(MenuDefinition menuDef)
	{
		if (menuDef.Pictures is null || menuDef.Pictures.Count == 0)
			return;
		foreach (MenuPictureDefinition pictureDef in menuDef.Pictures)
		{
			// Get texture from SharedAssetManager
			if (!SharedAssetManager.VgaGraph.TryGetValue(pictureDef.Name, out AtlasTexture texture))
			{
				GD.PrintErr($"ERROR: Picture '{pictureDef.Name}' not found in VgaGraph");
				continue;
			}
			// Render horizontal stripes if enabled
			if (pictureDef.Stripes)
				RenderStripes(texture, pictureDef.Y);
			// Render the normal picture
			TextureRect picture = new()
			{
				Texture = texture,
				Position = new Vector2(pictureDef.X, pictureDef.Y),
				Size = texture.Region.Size,
				TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
				ZIndex = 5, // Draw pictures below menu box but above background
			};
			_canvas.AddChild(picture);
		}
	}
	/// <summary>
	/// Render decorative horizontal stripes by stretching the leftmost pixel column.
	/// Used for C_OPTIONSPIC background stripes in Options menu.
	/// </summary>
	/// <param name="texture">Source texture to extract leftmost column from</param>
	/// <param name="y">Y coordinate to render stripes at</param>
	private void RenderStripes(AtlasTexture texture, int y)
	{
		// Get the region within the atlas
		Rect2 region = texture.Region;
		// Create a 1-pixel-wide AtlasTexture for the leftmost column
		AtlasTexture stripeTexture = new()
		{
			Atlas = texture.Atlas,
			Region = new Rect2(
				region.Position.X, // Same X position
				region.Position.Y, // Same Y position
				1,                 // Width = 1 pixel
				region.Size.Y      // Same height
			)
		};
		// Track for disposal
		_temporaryAtlasTextures.Add(stripeTexture);
		// Render the stripe, stretched to 320 pixels wide
		TextureRect stripes = new()
		{
			Texture = stripeTexture,
			Position = new Vector2(0, y),
			Size = new Vector2(320, region.Size.Y),
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			ZIndex = 4 // Draw stripes below the picture itself
		};
		_canvas.AddChild(stripes);
	}
	/// <summary>
	/// Render 3D beveled boxes.
	/// WL_MENU.C:DrawWindow - VWB_Bar + DrawOutline
	/// WL_MENU.C:DrawOutline - horizontal and vertical lines for 3D effect
	/// </summary>
	/// <param name="menuDef">Menu definition containing box list</param>
	private void RenderMenuBoxes(MenuDefinition menuDef)
	{
		if (menuDef.Boxes is null || menuDef.Boxes.Count == 0)
			return;
		foreach (MenuBoxDefinition boxDef in menuDef.Boxes)
		{
			// WL_MENU.C:DrawWindow - VWB_Bar(x,y,w,h,BKGDCOLOR)
			if (boxDef.BackgroundColor.HasValue)
			{
				Color boxColor = SharedAssetManager.GetPaletteColor(boxDef.BackgroundColor.Value);
				ColorRect boxFill = new()
				{
					Color = boxColor,
					Position = new Vector2(boxDef.X, boxDef.Y),
					Size = new Vector2(boxDef.W, boxDef.H),
					ZIndex = 6, // Draw box above pictures
				};
				_canvas.AddChild(boxFill);
			}
			// WL_MENU.C:DrawOutline - 3D beveled border effect
			// Top and left edges use DEACTIVE color (lighter)
			// Bottom and right edges use BORD2COLOR (darker)
			byte deactiveColor = boxDef.Deactive ?? 0x2b, // DEACTIVE default
				border2Color = boxDef.Border2Color ?? 0x23; // BORD2COLOR default
			Color colorDeactive = SharedAssetManager.GetPaletteColor(deactiveColor),
				colorBorder2 = SharedAssetManager.GetPaletteColor(border2Color);
			// Top edge (DEACTIVE)
			ColorRect topLine = new()
			{
				Color = colorDeactive,
				Position = new Vector2(boxDef.X, boxDef.Y),
				Size = new Vector2(boxDef.W + 1, 1), // +1 to connect corner
				ZIndex = 7,
			};
			_canvas.AddChild(topLine);
			// Left edge (DEACTIVE)
			ColorRect leftLine = new()
			{
				Color = colorDeactive,
				Position = new Vector2(boxDef.X, boxDef.Y),
				Size = new Vector2(1, boxDef.H + 1), // +1 to connect corner
				ZIndex = 7,
			};
			_canvas.AddChild(leftLine);
			// Bottom edge (BORD2COLOR)
			ColorRect bottomLine = new()
			{
				Color = colorBorder2,
				Position = new Vector2(boxDef.X, boxDef.Y + boxDef.H),
				Size = new Vector2(boxDef.W + 1, 1),
				ZIndex = 7,
			};
			_canvas.AddChild(bottomLine);
			// Right edge (BORD2COLOR)
			ColorRect rightLine = new()
			{
				Color = colorBorder2,
				Position = new Vector2(boxDef.X + boxDef.W, boxDef.Y),
				Size = new Vector2(1, boxDef.H + 1),
				ZIndex = 7,
			};
			_canvas.AddChild(rightLine);
		}
	}
	/// <summary>
	/// Render menu items as text labels.
	/// Uses layout coordinates from menuDef (matching original Wolf3D layout).
	/// </summary>
	/// <param name="menuDef">Menu definition containing layout info</param>
	/// <param name="selectedIndex">Currently selected item index</param>
	private void RenderMenuItems(MenuDefinition menuDef, int selectedIndex)
	{
		List<MenuItemDefinition> items = menuDef.Items;
		string fontName = menuDef.Font ?? "BIG";
		// Get theme from SharedAssetManager
		if (!SharedAssetManager.Themes.TryGetValue(fontName, out Theme theme))
		{
			GD.PrintErr($"ERROR: Theme '{fontName}' not found in SharedAssetManager");
			return;
		}
		// Layout coordinates (WL_MENU.H: CP_iteminfo)
		// Defaults match original MainItems if not specified
		float x = menuDef.X ?? 76, // MENU_X from WL_MENU.H
			y = menuDef.Y ?? 55, // MENU_Y from WL_MENU.H
			indent = menuDef.Indent ?? 24, // MainItems.indent
			spacing = menuDef.Spacing ?? 13; // Original uses 13 (DrawMenu: PrintY=item_i->y+i*13)
		for (int i = 0; i < items.Count; i++)
		{
			MenuItemDefinition item = items[i];
			bool isSelected = i == selectedIndex;
			// WL_MENU.C:DrawMenu - WindowX=PrintX=item_i->x+item_i->indent
			float itemX = x + indent,
				itemY = y + i * spacing;
			Label label = new()
			{
				Text = item.Text,
				Position = new Vector2(itemX, itemY),
				PivotOffset = Vector2.Zero,
				Theme = theme,
				ZIndex = 10,
			};
			// Set color based on selection using theme override
			// WL_MENU.C:SetTextColor - HIGHLIGHT vs TEXTCOLOR
			// Use menu-specific colors if defined, otherwise use defaults from WL_MENU.H
			Color textColor = isSelected
				? SharedAssetManager.GetPaletteColor(menuDef.Highlight ?? 0x13)
				: SharedAssetManager.GetPaletteColor(menuDef.TextColor ?? 0x17);
			label.AddThemeColorOverride("font_color", textColor);
			_canvas.AddChild(label);
		}
	}
	/// <summary>
	/// Render the menu cursor at the selected item position.
	/// WL_MENU.C:DrawMenuGun - draws cursor at x, y+curpos*13-2
	/// </summary>
	/// <param name="menuDef">Menu definition containing layout info</param>
	/// <param name="selectedIndex">Currently selected item index</param>
	private void RenderCursor(MenuDefinition menuDef, int selectedIndex)
	{
		GD.Print($"DEBUG RenderCursor: CursorPic='{menuDef.CursorPic}'");
		// Skip rendering if no cursor picture is specified
		if (string.IsNullOrEmpty(menuDef.CursorPic))
		{
			GD.Print("DEBUG RenderCursor: CursorPic is null/empty, skipping");
			return;
		}
		// Get cursor texture from VgaGraph
		GD.Print($"DEBUG RenderCursor: Looking up '{menuDef.CursorPic}' in VgaGraph (count: {SharedAssetManager.VgaGraph.Count})");
		if (!SharedAssetManager.VgaGraph.TryGetValue(menuDef.CursorPic, out AtlasTexture cursorTexture))
		{
			GD.PrintErr($"ERROR: Cursor image '{menuDef.CursorPic}' not found in VgaGraph");
			GD.PrintErr($"Available VgaGraph keys: {string.Join(", ", SharedAssetManager.VgaGraph.Keys)}");
			return;
		}
		GD.Print($"DEBUG RenderCursor: Found texture, region={cursorTexture.Region}");
		// Layout coordinates (matching original DrawMenuGun)
		float x = menuDef.X ?? 76, // MENU_X from WL_MENU.H
			y = menuDef.Y ?? 55, // MENU_Y from WL_MENU.H
			spacing = menuDef.Spacing ?? 13, // Original uses 13
		// WL_MENU.C:DrawMenuGun
		// x=iteminfo->x;
		// y=iteminfo->y+iteminfo->curpos*13-2;
			cursorX = x,
			cursorY = y + selectedIndex * spacing - 2;
		GD.Print($"DEBUG RenderCursor: Rendering at ({cursorX}, {cursorY}), selectedIndex={selectedIndex}");
		TextureRect cursor = new()
		{
			Texture = cursorTexture,
			Position = new Vector2(cursorX, cursorY),
			Size = cursorTexture.Region.Size,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			ZIndex = 20, // Draw cursor on top of everything
		};
		_canvas.AddChild(cursor);
		GD.Print("DEBUG RenderCursor: Cursor added to canvas");
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
