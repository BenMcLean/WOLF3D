using System;
using System.Collections.Generic;
using System.Linq;
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
	#region Data
	private readonly SubViewport _viewport;
	private readonly Control _canvas;
	private readonly List<AtlasTexture> _temporaryAtlasTextures = [];
	private readonly List<Rect2> _menuItemBounds = [];
	private readonly List<ClickablePictureBounds> _clickablePictureBounds = [];
	private readonly Dictionary<string, Label> _namedTextLabels = [];
	private readonly Dictionary<string, Label> _tickerLabels = [];
	private readonly List<AnimatedPictureState> _animatedPictures = [];
	private Color _currentBordColor = Colors.Black;
	private Input.PointerState _primaryPointer;
	private Input.PointerState _secondaryPointer;
	private TextureRect _primaryCrosshair;
	private TextureRect _secondaryCrosshair;
	#endregion Data
	#region Events
	/// <summary>
	/// Event fired when the border color changes.
	/// Allows presentation layer to update margins to match border color.
	/// </summary>
	public event Action<Color> BordColorChanged;
	#endregion Events
	/// <summary>
	/// Gets the current border color.
	/// This is the color used for the menu background/border (SVGA mode 13h border color).
	/// </summary>
	public Color CurrentBordColor => _currentBordColor;
	/// <summary>
	/// Creates a new MenuRenderer with a 320x200 virtual canvas.
	/// </summary>
	public MenuRenderer()
	{
		// Create 320x200 SubViewport for pixel-perfect rendering
		_viewport = new SubViewport
		{
			Size = new Vector2I(Constants.MenuScreenWidth, Constants.MenuScreenHeight),
			Disable3D = true,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
		};
		// Create root Control for UI elements
		_canvas = new Control
		{
			CustomMinimumSize = new Vector2(Constants.MenuScreenWidth, Constants.MenuScreenHeight),
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
		// Clear menu item bounds and clickable picture bounds
		_menuItemBounds.Clear();
		_clickablePictureBounds.Clear();
		// Clear named text and ticker tracking
		_namedTextLabels.Clear();
		_tickerLabels.Clear();
		_animatedPictures.Clear();
	}
	/// <summary>
	/// Render a menu definition to the canvas.
	/// Phase 1 basic implementation: background + text items.
	/// </summary>
	/// <param name="menuDef">Menu definition from AssetManager.Menus</param>
	/// <param name="selectedIndex">Currently selected item index (into visibleItems)</param>
	/// <param name="visibleItems">Pre-filtered list of visible menu items. If null, uses all items in menuDef.</param>
	/// <param name="modal">Optional active modal dialog to render over the menu.</param>
	public void RenderMenu(
		MenuDefinition menuDef,
		int selectedIndex,
		System.Collections.Generic.List<MenuItemDefinition> visibleItems = null,
		ModalDialog modal = null)
	{
		// Clear previous frame
		Clear();
		visibleItems ??= menuDef.Items;
		// Render background color if specified, otherwise use black
		if (menuDef.BordColor.HasValue)
			RenderBkgdColor(menuDef.BordColor.Value);
		else
		{
			// No border color specified - use black and fire event if changed
			Color black = Colors.Black;
			if (_currentBordColor != black)
			{
				_currentBordColor = black;
				BordColorChanged?.Invoke(black);
			}
		}
		// Render pictures (backgrounds and decorative images - WL_MENU.C:DrawMainMenu)
		RenderPictures(menuDef);
		// Render menu boxes (WL_MENU.C:DrawWindow)
		RenderMenuBoxes(menuDef);
		// Render text labels (WL_MENU.C:US_Print - "How tough are you?")
		RenderTexts(menuDef);
		// Render tickers (intermission screen percent counters)
		RenderTickers(menuDef);
		// Render menu items (visible items only)
		RenderMenuItems(menuDef, selectedIndex, visibleItems);
		// Render cursor (WL_MENU.C:DrawMenuGun)
		RenderCursor(menuDef, selectedIndex, visibleItems);
		// Render pointer crosshairs (VR controllers / mouse)
		RenderCrosshairs();
		// Render modal overlay if active (on top of everything except crosshairs)
		if (modal != null && modal.IsPending)
			RenderModal(modal, menuDef);
	}
	/// <summary>
	/// Render a solid background color.
	/// </summary>
	/// <param name="colorIndex">VGA palette color index (0-255)</param>
	private void RenderBkgdColor(byte colorIndex)
	{
		// Get color from palette
		Color color = SharedAssetManager.GetPaletteColor(colorIndex);
		ColorRect background = new()
		{
			Color = color,
			Position = Vector2.Zero,
			Size = new Vector2(Constants.MenuScreenWidth, Constants.MenuScreenHeight),
		};
		_canvas.AddChild(background);
		// Update current border color and fire event if changed
		if (_currentBordColor != color)
		{
			_currentBordColor = color;
			BordColorChanged?.Invoke(color);
		}
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
		for (int pictureIndex = 0; pictureIndex < menuDef.Pictures.Count; pictureIndex++)
		{
			MenuPictureDefinition pictureDef = menuDef.Pictures[pictureIndex];
			// Get texture from SharedAssetManager
			if (!SharedAssetManager.VgaGraph.TryGetValue(pictureDef.Name, out AtlasTexture texture))
			{
				GD.PrintErr($"ERROR: Picture '{pictureDef.Name}' not found in VgaGraph");
				continue;
			}
			// Render horizontal stripes if enabled
		// Resolve centering using the actual texture dimensions
			float picX = pictureDef.CenterX ? (Constants.MenuScreenWidth - texture.Region.Size.X) / 2f
				: pictureDef.RightX ? Constants.MenuScreenWidth - texture.Region.Size.X
				: pictureDef.XValue;
			float picY = pictureDef.CenterY ? (Constants.MenuScreenHeight - texture.Region.Size.Y) / 2f
				: pictureDef.BottomY ? Constants.MenuScreenHeight - texture.Region.Size.Y
				: pictureDef.YValue;
			if (pictureDef.Stripes)
				RenderStripes(texture, (int)picY);
			// Render the normal picture
			TextureRect picture = new()
			{
				Texture = texture,
				Position = new Vector2(picX, picY),
				Size = texture.Region.Size,
				TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
				ZIndex = pictureDef.ZIndex ?? 5, // Default 5 (below boxes), or custom value (e.g., 9 for difficulty pic)
			};
			_canvas.AddChild(picture);
			// Track clickable pictures (those with Lua scripts)
			if (!string.IsNullOrEmpty(pictureDef.Script))
				_clickablePictureBounds.Add(new ClickablePictureBounds(
					new Rect2(new Vector2(picX, picY), texture.Region.Size),
					pictureIndex));
			// Track animated pictures for frame cycling
			if (!string.IsNullOrEmpty(pictureDef.Frames))
			{
				string[] frameNames = pictureDef.Frames.Split(',');
				if (frameNames.Length > 1)
					_animatedPictures.Add(new AnimatedPictureState
					{
						TextureRect = picture,
						FrameNames = frameNames,
						FrameInterval = pictureDef.FrameInterval,
						CurrentFrame = 0,
						Elapsed = 0f
					});
			}
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
			Size = new Vector2(Constants.MenuScreenWidth, region.Size.Y),
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			ZIndex = 4, // Draw stripes below the picture itself
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
			// WL_MENU.C:DrawWindow + DrawOutline - fill then beveled border
			// Top and left edges use DEACTIVE color (lighter); bottom and right use BORD2COLOR (darker)
			byte deactiveColor = boxDef.Deactive ?? 0x2b, // DEACTIVE default
				border2Color = boxDef.Bord2Color ?? 0x23; // BORD2COLOR default
			Color? bgColor = boxDef.BkgdColor.HasValue
				? SharedAssetManager.GetPaletteColor(boxDef.BkgdColor.Value)
				: null;
			DrawBevelledBox(boxDef.X, boxDef.Y, boxDef.W, boxDef.H,
				bgColor,
				SharedAssetManager.GetPaletteColor(deactiveColor),
				SharedAssetManager.GetPaletteColor(border2Color),
				bgZIndex: 6, borderZIndex: 7);
		}
	}
	/// <summary>
	/// Render text labels (non-interactive text).
	/// WL_MENU.C:US_Print - Used for static text like "How tough are you?"
	/// </summary>
	/// <param name="menuDef">Menu definition containing text list</param>
	private void RenderTexts(MenuDefinition menuDef)
	{
		if (menuDef.Texts is null || menuDef.Texts.Count == 0)
			return;
		foreach (MenuTextDefinition textDef in menuDef.Texts)
		{
			// Determine font: textDef.Font > menuDef.Font > "BIG" (default)
			string fontName = textDef.Font ?? menuDef.Font ?? "BIG";
			// Get theme from SharedAssetManager
			if (!SharedAssetManager.Themes.TryGetValue(fontName, out Theme theme))
			{
				GD.PrintErr($"ERROR: Theme '{fontName}' not found in SharedAssetManager");
				continue;
			}
			// Determine color: textDef.Color > menuDef.TextColor > 0x17 (TEXTCOLOR default)
			byte colorIndex = textDef.Color ?? menuDef.TextColor ?? 0x17;
			Color textColor = SharedAssetManager.GetPaletteColor(colorIndex);
			// Create label with text
			Label label = new()
			{
				Text = textDef.Content,
				Theme = theme,
				ZIndex = 8, // Draw text labels below menu items but above boxes
							// Explicitly set Font and FontSize so LabelSettings never falls back to the
							// project's default dynamic font (which would be blurry/wrong for bitmap fonts).
							// LineSpacing is extra inter-line spacing; 0 gives tight single-line spacing.
				LabelSettings = new LabelSettings
				{
					Font = theme.DefaultFont,
					FontSize = theme.DefaultFontSize,
					LineSpacing = 0,
					FontColor = textColor,
				}
			};
			// Measure text size from font directly — label.Size is zero until Godot lays out the node
			Font font = theme.DefaultFont;
			Vector2 textSize = font.GetStringSize(textDef.Content, fontSize: theme.DefaultFontSize);
			// Set position before adding to canvas
			label.Position = new Vector2(
				x: textDef.CenterX ? (Constants.MenuScreenWidth - textSize.X) / 2f
					: textDef.RightX ? Constants.MenuScreenWidth - textSize.X
					: textDef.XValue,
				y: textDef.CenterY ? (Constants.MenuScreenHeight - textSize.Y) / 2f
					: textDef.BottomY ? Constants.MenuScreenHeight - textSize.Y
					: textDef.YValue);
			_canvas.AddChild(label);
			label.PivotOffset = Vector2.Zero;
			// Track named text labels for dynamic updates from Lua
			if (!string.IsNullOrEmpty(textDef.Name))
				_namedTextLabels[textDef.Name] = label;
		}
	}
	/// <summary>
	/// Render menu items as text labels.
	/// Uses layout coordinates from menuDef (matching original Wolf3D layout).
	/// </summary>
	/// <param name="menuDef">Menu definition containing layout info</param>
	/// <param name="selectedIndex">Currently selected item index (into visibleItems)</param>
	/// <param name="visibleItems">Pre-filtered list of visible menu items to render.</param>
	private void RenderMenuItems(MenuDefinition menuDef, int selectedIndex, List<MenuItemDefinition> visibleItems)
	{
		List<MenuItemDefinition> items = visibleItems;
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
			indent = menuDef.Indent ?? 24, // MainItems.indent
			spacing = menuDef.Spacing ?? 13, // Original uses 13 (DrawMenu: PrintY=item_i->y+i*13)
											 // Use accumulated Y position to support per-item spacing via ExtraSpacing custom property
			currentY = menuDef.Y ?? 55; // MENU_Y from WL_MENU.H
		for (int i = 0; i < items.Count; i++)
		{
			MenuItemDefinition item = items[i];
			bool isSelected = i == selectedIndex;
			// Check for ExtraSpacing custom property (for gaps between menu sections)
			if (item.CustomProperties.TryGetValue("ExtraSpacing", out string extraSpacingStr) &&
				int.TryParse(extraSpacingStr, out int extraSpacing))
				currentY += extraSpacing;
			// WL_MENU.C:DrawMenu - WindowX=PrintX=item_i->x+item_i->indent
			float itemX = x + indent;
			// Determine color based on selection
			// WL_MENU.C:SetTextColor - HIGHLIGHT vs TEXTCOLOR
			// Use menu-specific colors if defined, otherwise use defaults from WL_MENU.H
			Color textColor = isSelected
				? SharedAssetManager.GetPaletteColor(menuDef.Highlight ?? 0x13)
				: SharedAssetManager.GetPaletteColor(menuDef.TextColor ?? 0x17);
			Label label = new()
			{
				Text = item.Text,
				Position = new Vector2(itemX, currentY),
				PivotOffset = Vector2.Zero,
				Theme = theme,
				ZIndex = 10,
				// Explicitly set Font and FontSize so LabelSettings never falls back to the
				// project's default dynamic font (which would be blurry/wrong for bitmap fonts).
				// LineSpacing is extra inter-line spacing; 0 gives tight single-line spacing.
				LabelSettings = new LabelSettings
				{
					Font = theme.DefaultFont,
					FontSize = theme.DefaultFontSize,
					LineSpacing = 0,
					FontColor = textColor,
				}
			};
			_canvas.AddChild(label);
			// Store bounds for hover detection - measure text size from font
			// For multi-line strings, use the maximum width of all lines
			Font font = theme.DefaultFont;
			float maxWidth = item.Text.Split('\n')
				.Max(line => font.GetStringSize(line, fontSize: theme.DefaultFontSize).X);
			// Use max line width, spacing for height (bitmap fonts return 0 height)
			_menuItemBounds.Add(new Rect2(new Vector2(itemX, currentY), new Vector2(maxWidth, spacing)));
			// Move to next item position
			currentY += spacing;
		}
	}
	/// <summary>
	/// Render the menu cursor at the selected item position.
	/// WL_MENU.C:DrawMenuGun - draws cursor at x, y+curpos*13-2
	/// </summary>
	/// <param name="menuDef">Menu definition containing layout info</param>
	/// <param name="selectedIndex">Currently selected item index (into visibleItems)</param>
	/// <param name="visibleItems">Pre-filtered list of visible menu items.</param>
	private void RenderCursor(MenuDefinition menuDef, int selectedIndex, List<MenuItemDefinition> visibleItems)
	{
		// Skip rendering if no cursor picture is specified or no menu items to select
		if (string.IsNullOrEmpty(menuDef.CursorPic) || visibleItems.Count == 0)
			return;
		// Get cursor texture from VgaGraph
		if (!SharedAssetManager.VgaGraph.TryGetValue(menuDef.CursorPic, out AtlasTexture cursorTexture))
		{
			GD.PrintErr($"ERROR: Cursor image '{menuDef.CursorPic}' not found in VgaGraph");
			return;
		}
		// Layout coordinates (matching original DrawMenuGun)
		// Calculate cursor Y position using accumulated spacing (same logic as RenderMenuItems)
		float x = menuDef.X ?? 76, // MENU_X from WL_MENU.H
			spacing = menuDef.Spacing ?? 13, // Original uses 13
			currentY = menuDef.Y ?? 55; // Start Y position
										// Accumulate Y position up to selectedIndex (same as RenderMenuItems)
		for (int i = 0; i < selectedIndex && i < visibleItems.Count; i++)
		{
			MenuItemDefinition item = visibleItems[i];
			// Check for ExtraSpacing custom property
			if (item.CustomProperties.TryGetValue("ExtraSpacing", out string extraSpacingStr) &&
				int.TryParse(extraSpacingStr, out int extraSpacing))
				currentY += extraSpacing;
			currentY += spacing;
		}
		// Add ExtraSpacing for the selected item itself
		if (selectedIndex < visibleItems.Count &&
			visibleItems[selectedIndex].CustomProperties.TryGetValue("ExtraSpacing", out string selectedExtraSpacing) &&
			int.TryParse(selectedExtraSpacing, out int selectedExtra))
			currentY += selectedExtra;
		// WL_MENU.C:DrawMenuGun - cursor is at y-2 (2 pixels above the item)
		float cursorX = x,
			cursorY = currentY - 2;
		TextureRect cursor = new()
		{
			Texture = cursorTexture,
			Position = new Vector2(cursorX, cursorY),
			Size = cursorTexture.Region.Size,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			ZIndex = 20, // Draw cursor on top of everything
		};
		_canvas.AddChild(cursor);
	}
	/// <summary>
	/// Get bounding rectangles of all rendered menu items.
	/// Used for hover detection - hitboxes match text size like hyperlinks.
	/// </summary>
	/// <returns>Array of bounding rectangles in viewport coordinates (320x200)</returns>
	public Rect2[] GetMenuItemBounds() => [.. _menuItemBounds];
	/// <summary>
	/// Get bounding rectangles of all clickable pictures (those with Lua scripts).
	/// Used for click detection - hitboxes match the picture texture size.
	/// </summary>
	/// <returns>Array of clickable picture bounds with picture indices</returns>
	public ClickablePictureBounds[] GetClickablePictureBounds() => [.. _clickablePictureBounds];
	/// <summary>
	/// Sets the pointer states for crosshair rendering.
	/// Call this each frame before Update to provide current pointer positions.
	/// </summary>
	/// <param name="primary">Primary pointer state (mouse or right VR controller).</param>
	/// <param name="secondary">Secondary pointer state (left VR controller, inactive for flatscreen).</param>
	public void SetPointers(Input.PointerState primary, Input.PointerState secondary)
	{
		_primaryPointer = primary;
		_secondaryPointer = secondary;
	}
	/// <summary>
	/// Updates crosshair positions without re-rendering the entire menu.
	/// Call this each frame after SetPointers to update crosshair display.
	/// </summary>
	public void UpdateCrosshairs()
	{
		// Update primary crosshair
		if (_primaryCrosshair != null)
		{
			if (_primaryPointer.IsActive && IsPositionOnScreen(_primaryPointer.Position))
			{
				_primaryCrosshair.Visible = true;
				// Center the crosshair on the pointer position
				_primaryCrosshair.Position = _primaryPointer.Position - _primaryCrosshair.Size / 2;
			}
			else
				_primaryCrosshair.Visible = false;
		}
		// Update secondary crosshair
		if (_secondaryCrosshair is not null)
			if (_secondaryPointer.IsActive && IsPositionOnScreen(_secondaryPointer.Position))
			{
				_secondaryCrosshair.Visible = true;
				// Center the crosshair on the pointer position
				_secondaryCrosshair.Position = _secondaryPointer.Position - _secondaryCrosshair.Size / 2;
			}
			else
				_secondaryCrosshair.Visible = false;
	}
	/// <summary>
	/// Render ticker labels (for intermission screen percent counters).
	/// Tickers display as text labels that can be updated dynamically from Lua.
	/// </summary>
	/// <param name="menuDef">Menu definition containing ticker definitions</param>
	private void RenderTickers(MenuDefinition menuDef)
	{
		if (menuDef.Tickers is null || menuDef.Tickers.Count == 0)
			return;
		foreach (MenuTickerDefinition tickerDef in menuDef.Tickers)
		{
			// Determine font: tickerDef.Font > menuDef.Font > "BIG" (default)
			string fontName = tickerDef.Font ?? menuDef.Font ?? "BIG";
			if (!SharedAssetManager.Themes.TryGetValue(fontName, out Theme theme))
			{
				GD.PrintErr($"ERROR: Theme '{fontName}' not found in SharedAssetManager");
				continue;
			}
			// Determine color: tickerDef.Color > menuDef.TextColor > 0x17
			byte colorIndex = tickerDef.Color ?? menuDef.TextColor ?? 0x17;
			Color textColor = SharedAssetManager.GetPaletteColor(colorIndex);
			Label label = new()
			{
				Text = "0", // Start at 0, updated by Lua via UpdateTicker
				Theme = theme,
				ZIndex = 8,
				LabelSettings = new LabelSettings
				{
					Font = theme.DefaultFont,
					FontSize = theme.DefaultFontSize,
					LineSpacing = 0,
					FontColor = textColor
				},
			};
			_canvas.AddChild(label);
			// Position the ticker
			float x = tickerDef.CenterX ? Constants.MenuScreenWidth / 2f
					: tickerDef.RightX ? Constants.MenuScreenWidth
					: tickerDef.XValue,
				y = tickerDef.CenterY ? Constants.MenuScreenHeight / 2f
					: tickerDef.BottomY ? Constants.MenuScreenHeight
					: tickerDef.YValue;
			// Right-align: position is the right edge, offset left by text width
			if (tickerDef.Align?.Equals("Right", StringComparison.OrdinalIgnoreCase) ?? false)
			{
				Font font = theme.DefaultFont;
				float textWidth = font.GetStringSize(label.Text, fontSize: theme.DefaultFontSize).X;
				label.Position = new Vector2(x - textWidth, y);
			}
			else
			{
				label.Position = new Vector2(x, y);
			}
			// Track by name for dynamic updates
			_tickerLabels[tickerDef.Name] = label;
		}
	}
	/// <summary>
	/// Updates a named text label's content dynamically.
	/// Called from Lua via SetText(name, value).
	/// </summary>
	/// <param name="name">The Name attribute of the text element</param>
	/// <param name="value">New text content</param>
	public void UpdateText(string name, string value)
	{
		if (_namedTextLabels.TryGetValue(name, out Label label))
			label.Text = value;
	}
	/// <summary>
	/// Updates a ticker label's displayed value.
	/// Called from the ticker animation system.
	/// </summary>
	/// <param name="name">The Name of the ticker element</param>
	/// <param name="value">New display value (e.g., "75")</param>
	/// <param name="menuDef">Menu definition for layout recalculation</param>
	public void UpdateTicker(string name, string value, MenuDefinition menuDef)
	{
		if (!_tickerLabels.TryGetValue(name, out Label label))
			return;
		label.Text = value;
		// Find the ticker definition for alignment info
		MenuTickerDefinition tickerDef = null;
		if (menuDef?.Tickers is not null)
			for (int i = 0; i < menuDef.Tickers.Count; i++)
				if (menuDef.Tickers[i].Name == name)
				{
					tickerDef = menuDef.Tickers[i];
					break;
				}
		// Recalculate position for right-aligned tickers
		if (tickerDef?.Align?.Equals("Right", StringComparison.OrdinalIgnoreCase) == true)
		{
			string fontName = tickerDef.Font ?? menuDef?.Font ?? "BIG";
			if (SharedAssetManager.Themes.TryGetValue(fontName, out Theme theme))
			{
				Font font = theme.DefaultFont;
				float textWidth = font.GetStringSize(value, fontSize: theme.DefaultFontSize).X;
				float anchorX = tickerDef.CenterX ? Constants.MenuScreenWidth / 2f
				: tickerDef.RightX ? Constants.MenuScreenWidth
				: tickerDef.XValue;
			label.Position = new Vector2(anchorX - textWidth, label.Position.Y);
			}
		}
	}
	/// <summary>
	/// Updates animated pictures by cycling frames based on elapsed time.
	/// Called each frame from MenuManager.
	/// </summary>
	/// <param name="delta">Time elapsed since last frame in seconds</param>
	public void UpdateAnimations(float delta)
	{
		for (int i = 0; i < _animatedPictures.Count; i++)
		{
			AnimatedPictureState state = _animatedPictures[i];
			state.Elapsed += delta;
			if (state.Elapsed >= state.FrameInterval)
			{
				state.Elapsed -= state.FrameInterval;
				state.CurrentFrame = (state.CurrentFrame + 1) % state.FrameNames.Length;
				string frameName = state.FrameNames[state.CurrentFrame];
				if (SharedAssetManager.VgaGraph.TryGetValue(frameName, out AtlasTexture texture))
					state.TextureRect.Texture = texture;
			}
		}
	}
	/// <summary>
	/// Render a modal confirmation dialog over the current menu.
	/// Draws a beveled box with the message text and Yes/No buttons.
	/// Also sets YesButtonBounds and NoButtonBounds on the dialog for pointer hit-testing.
	/// WL_MENU.C:Confirm() - draws Message box, then waits for Y/N/Escape.
	/// </summary>
	/// <param name="modal">The modal dialog to render.</param>
	/// <param name="menuDef">Current menu definition (for font/color lookup).</param>
	private void RenderModal(ModalDialog modal, MenuDefinition menuDef)
	{
		string fontName = menuDef.Font ?? "BIG";
		if (!SharedAssetManager.Themes.TryGetValue(fontName, out Theme theme))
			return;
		Font font = theme.DefaultFont;
		float fontSize = theme.DefaultFontSize,
			lineHeight = fontSize;

		// Modal-specific colors from XML (PixelRect: Color/NWColor/SEColor/TextColor)
		// Each has a dedicated ModalXxx attribute; falls back to menu default if absent.
		Assets.Gameplay.MenuCollection menuCollection = SharedAssetManager.CurrentGame?.MenuCollection;
		// PixelRect.Color — box fill
		byte bgColorIndex = menuCollection?.DefaultModalColor ?? 0x17,
			// PixelRect text label color
			textColorIndex = menuCollection?.DefaultModalTextColor ?? 0,
			// PixelRect.NWColor — top/left bevel (lighter, old BordColor)
			nwColorIndex = menuCollection?.DefaultModalHighlight ?? menuCollection?.DefaultHighlight ?? 0x13,
			// PixelRect.SEColor — bottom/right bevel (darker, old Bord2Color)
			seColorIndex = menuCollection?.DefaultModalBord2Color ?? menuCollection?.DefaultBord2Color ?? 0;
		Color bgColor = SharedAssetManager.GetPaletteColor(bgColorIndex),
			textColor = SharedAssetManager.GetPaletteColor(textColorIndex),
			colorNW = SharedAssetManager.GetPaletteColor(nwColorIndex),
			colorSE = SharedAssetManager.GetPaletteColor(seColorIndex);
		// Compute message text dimensions
		string[] lines = modal.Message.Split('\n');
		float maxLineWidth = 0f;
		for (int i = 0; i < lines.Length; i++)
		{
			float w = font.GetStringSize(lines[i], fontSize: (int)fontSize).X;
			if (w > maxLineWidth)
				maxLineWidth = w;
		}
		float textHeight = lines.Length * lineHeight;

		// Size and position the message box (contains only text, no buttons inside)
		const float boxPad = 8f,   // inner padding for message box
			btnPad = 4f;   // inner padding for Yes/No boxes
		float msgBoxW = maxLineWidth + boxPad * 2f,
			msgBoxH = textHeight + boxPad * 2f,
		// Size the Yes/No button boxes
			yesW = font.GetStringSize("Yes", fontSize: (int)fontSize).X,
			noW = font.GetStringSize("No", fontSize: (int)fontSize).X,
			yesBoxW = yesW + btnPad * 2f,
			noBoxW = noW + btnPad * 2f,
			btnBoxH = lineHeight + btnPad * 2f,
		// Center the whole group (message box + buttons) vertically on 320x200
		// Buttons sit flush against the bottom of the message box (outside it)
			totalH = msgBoxH + btnBoxH,
			msgBoxY = Mathf.Round((Constants.MenuScreenHeight - totalH) / 2f),
			msgBoxX = Mathf.Round((Constants.MenuScreenWidth - msgBoxW) / 2f),
			btnBoxY = msgBoxY + msgBoxH + 1f,
		// Yes is left-aligned with the message box; No is right-aligned
			yesBoxX = msgBoxX,
			noBoxX = msgBoxX + msgBoxW - noBoxW;
		// Semi-transparent dark overlay to dim the menu behind the dialog
		_canvas.AddChild(new ColorRect
		{
			Color = new Color(0f, 0f, 0f, 0.5f),
			Position = Vector2.Zero,
			Size = new Vector2(Constants.MenuScreenWidth, Constants.MenuScreenHeight),
			ZIndex = 48,
		});
		// Message box: fill + single PixelRect-style bevel (NWColor top/left, SEColor bottom/right)
		DrawBevelledBox(msgBoxX, msgBoxY, msgBoxW, msgBoxH, bgColor, colorNW, colorSE, 50, 51);
		// Message text
		_canvas.AddChild(new Label
		{
			Text = modal.Message,
			Theme = theme,
			Position = new Vector2(msgBoxX + boxPad, msgBoxY + boxPad),
			ZIndex = 60,
			LabelSettings = new LabelSettings
			{
				Font = font,
				FontSize = (int)fontSize,
				LineSpacing = 0,
				FontColor = textColor,
			}
		});
		// "Yes" box — outside/below the message box, left-aligned with it
		DrawBevelledBox(yesBoxX, btnBoxY, yesBoxW, btnBoxH, bgColor, colorNW, colorSE, 53, 54);
		_canvas.AddChild(new Label
		{
			Text = "Yes",
			Theme = theme,
			Position = new Vector2(yesBoxX + btnPad, btnBoxY + btnPad),
			ZIndex = 60,
			LabelSettings = new LabelSettings { Font = font, FontSize = (int)fontSize, FontColor = textColor },
		});
		// "No" box — outside/below the message box, right-aligned with it
		DrawBevelledBox(noBoxX, btnBoxY, noBoxW, btnBoxH, bgColor, colorNW, colorSE, 53, 54);
		_canvas.AddChild(new Label
		{
			Text = "No",
			Theme = theme,
			Position = new Vector2(noBoxX + btnPad, btnBoxY + btnPad),
			ZIndex = 60,
			LabelSettings = new LabelSettings { Font = font, FontSize = (int)fontSize, FontColor = textColor },
		});
		// Set button bounds for pointer hit-testing
		const float hitExtra = 2f;
		modal.YesButtonBounds = new Rect2(
			yesBoxX - hitExtra, btnBoxY - hitExtra,
			yesBoxW + hitExtra * 2f, btnBoxH + hitExtra * 2f);
		modal.NoButtonBounds = new Rect2(
			noBoxX - hitExtra, btnBoxY - hitExtra,
			noBoxW + hitExtra * 2f, btnBoxH + hitExtra * 2f);
	}
	/// <summary>
	/// Draws a filled rectangle with a PixelRect-style bevelled border.
	/// Used by RenderMenuBoxes and RenderModal for boxes and Yes/No buttons.
	/// colorNW = top/left bevel (lighter); colorSE = bottom/right bevel (darker).
	/// bgColor may be null to skip the fill and draw only the border.
	/// </summary>
	private void DrawBevelledBox(float x, float y, float w, float h,
		Color? bgColor, Color colorNW, Color colorSE,
		int bgZIndex, int borderZIndex)
	{
		if (bgColor.HasValue)
			_canvas.AddChild(new ColorRect
			{
				Color = bgColor.Value,
				Position = new Vector2(x, y),
				Size = new Vector2(w, h),
				ZIndex = bgZIndex,
			});
		AddOutline(x, y, w, h, colorNW, colorSE, borderZIndex);
	}
	/// <summary>
	/// Draws a bevelled outline (WL_MENU.C:DrawOutline / PixelRect bevel).
	/// topLeft is used for top and left edges; bottomRight for bottom and right edges.
	/// </summary>
	private void AddOutline(float x, float y, float w, float h,
		Color topLeft, Color bottomRight, int zIndex)
	{
		// Top edge (topLeft color, WL_MENU.C:VWB_Hlin top)
		_canvas.AddChild(new ColorRect { Color = topLeft, Position = new Vector2(x, y), Size = new Vector2(w + 1f, 1f), ZIndex = zIndex });
		// Left edge (topLeft color, WL_MENU.C:VWB_Vlin left)
		_canvas.AddChild(new ColorRect { Color = topLeft, Position = new Vector2(x, y), Size = new Vector2(1f, h + 1f), ZIndex = zIndex });
		// Bottom edge (bottomRight color, WL_MENU.C:VWB_Hlin bottom)
		_canvas.AddChild(new ColorRect { Color = bottomRight, Position = new Vector2(x, y + h), Size = new Vector2(w + 1f, 1f), ZIndex = zIndex });
		// Right edge (bottomRight color, WL_MENU.C:VWB_Vlin right)
		_canvas.AddChild(new ColorRect { Color = bottomRight, Position = new Vector2(x + w, y), Size = new Vector2(1f, h + 1f), ZIndex = zIndex });
	}
	/// <summary>
	/// Checks if a position is within the visible screen area.
	/// </summary>
	/// <param name="position">Position in viewport coordinates (320x200).</param>
	/// <returns>True if the position is on screen.</returns>
	private static bool IsPositionOnScreen(Vector2 position) =>
		position.X >= 0 && position.X < Constants.MenuScreenWidth && position.Y >= 0 && position.Y < Constants.MenuScreenHeight;
	/// <summary>
	/// Creates crosshair TextureRects for active pointers.
	/// Called during menu rendering to set up crosshair nodes.
	/// </summary>
	private void RenderCrosshairs()
	{
		// Get the crosshair texture from SharedAssetManager
		if (SharedAssetManager.Crosshair is not AtlasTexture crosshairTexture)
			return;
		// Create primary crosshair (always created, visibility controlled in UpdateCrosshairs)
		_primaryCrosshair = new TextureRect
		{
			Name = "PrimaryCrosshair",
			Texture = crosshairTexture,
			Size = crosshairTexture.Region.Size,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			ZIndex = 100, // Draw crosshairs on top of everything
			Visible = false, // Start hidden, UpdateCrosshairs will show if active
		};
		_canvas.AddChild(_primaryCrosshair);
		// Create secondary crosshair
		_secondaryCrosshair = new TextureRect
		{
			Name = "SecondaryCrosshair",
			Texture = crosshairTexture,
			Size = crosshairTexture.Region.Size,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			ZIndex = 100,
			Visible = false,
		};
		_canvas.AddChild(_secondaryCrosshair);
		// Initial update to set positions
		UpdateCrosshairs();
	}
}

/// <summary>
/// Tracks a clickable picture's bounding rectangle and its index in the menu's Pictures list.
/// Used for click/hover detection on pictures that have Lua scripts.
/// </summary>
public readonly struct ClickablePictureBounds(Rect2 bounds, int pictureIndex)
{
	/// <summary>Bounding rectangle in viewport coordinates (320x200).</summary>
	public Rect2 Bounds { get; } = bounds;
	/// <summary>Index into MenuDefinition.Pictures list.</summary>
	public int PictureIndex { get; } = pictureIndex;
}

/// <summary>
/// Tracks state for an animated picture that cycles through multiple VgaGraph frames.
/// Used for BJ breathing animation on the intermission screen.
/// </summary>
internal class AnimatedPictureState
{
	/// <summary>The TextureRect node being animated.</summary>
	public TextureRect TextureRect { get; set; }
	/// <summary>Array of VgaGraph picture names to cycle through.</summary>
	public string[] FrameNames { get; set; }
	/// <summary>Time in seconds between frame changes.</summary>
	public float FrameInterval { get; set; }
	/// <summary>Index of the currently displayed frame.</summary>
	public int CurrentFrame { get; set; }
	/// <summary>Time elapsed since last frame change.</summary>
	public float Elapsed { get; set; }
}
