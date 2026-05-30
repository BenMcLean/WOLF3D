using System;
using System.Collections.Generic;
using System.Linq;
using BenMcLean.Wolf3D.Assets.Gameplay;
using BenMcLean.Wolf3D.Assets.Menu;
using BenMcLean.Wolf3D.Shared.Layout;
using BenMcLean.Wolf3D.Shared.Text;
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
	private readonly CanvasLayoutRenderer _layoutRenderer;
	private readonly List<Rect2> _menuItemBounds = [];
	private readonly List<ClickablePictureBounds> _clickablePictureBounds = [];
	private readonly Dictionary<string, Label> _namedTextLabels = [];
	private readonly Dictionary<string, (TextDefinition Definition, Theme Theme)> _namedTextLayout = [];
	private readonly Dictionary<string, string> _textOverrides = [];
	private readonly Dictionary<string, Label> _tickerLabels = [];
	private readonly List<AnimatedPictureState> _animatedPictures = [];
	private readonly List<ActorAnimationState> _actorAnimations = [];
	private Color _currentBordColor = Colors.Black;
	/// <summary>
	/// Delegate for resolving VSWAP sprite page numbers to Godot textures.
	/// Set by the VR layer (MenuRoom) so the Shared renderer can display actor sprites.
	/// </summary>
	public Func<ushort, Texture2D> SpriteTextureProvider { get; set; }
	/// <summary>
	/// Delegate for resolving VSWAP sprite names to page numbers.
	/// Set by the VR layer (MenuRoom) so StaticSprite elements can look up pages by name.
	/// </summary>
	public Func<string, ushort?> SpritePageByNameProvider { get; set; }
	/// <summary>
	/// Native display size of a Wolf3D sprite in menu canvas pixels (default 64).
	/// Corrects for VR upscaling: textures may be 512px but should display at 64px on a 320x200 canvas.
	/// Set by MenuRoom from VSwap.TileSqrt.
	/// </summary>
	public int SpriteNativeSize { get; set; } = 64;
	private Input.PointerState _primaryPointer,
		_secondaryPointer;
	private TextureRect _primaryCrosshair,
		_secondaryCrosshair;
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
		_layoutRenderer = new CanvasLayoutRenderer(_canvas, Constants.MenuScreenWidth, Constants.MenuScreenHeight);
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
		// Clear menu item bounds and clickable picture bounds
		_menuItemBounds.Clear();
		_clickablePictureBounds.Clear();
		// Clear named text and ticker tracking
		_namedTextLabels.Clear();
		_namedTextLayout.Clear();
		_tickerLabels.Clear();
		_animatedPictures.Clear();
		_actorAnimations.Clear();
	}
	/// <summary>
	/// Render a menu definition to the canvas.
	/// Phase 1 basic implementation: background + text items.
	/// </summary>
	/// <param name="menuDef">Menu definition from AssetManager.Menus</param>
	/// <param name="selectedIndex">Currently selected item index (into visibleItems)</param>
	/// <summary>
	/// Clear the canvas and render an article page with its fixed border pictures.
	/// Fires BordColorChanged with the article's background color.
	/// Delegates to ArticleRenderer.RenderPage using the shared viewport canvas.
	/// WL_TEXT.C: PageLayout rendering per page.
	/// </summary>
	public void RenderArticle(ArticleDefinition articleDef, ArticlePageLayout page)
	{
		Clear();
		byte backColorIndex = articleDef?.BackColor ?? ArticleLayoutEngine.BackColor;
		Color backColor = SharedAssetManager.GetPaletteColor(backColorIndex);
		if (_currentBordColor != backColor)
		{
			_currentBordColor = backColor;
			BordColorChanged?.Invoke(backColor);
		}
		ArticleRenderer.RenderPage(_canvas, page, articleDef?.Pictures);
	}
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
		// Render shared non-interactive canvas primitives.
		int pictureIndex = -1;
		_layoutRenderer.RenderAll(menuDef, new CanvasLayoutRenderOptions
		{
			DefaultDeactive = 0x2b,
			DefaultBorder2Color = 0x23,
			BoxFillZIndex = 6,
			BoxBorderZIndex = 7,
			PictureZIndex = 5,
			FallbackFontName = "BIG",
			AfterAddPicture = (pictureDef, picture) =>
			{
				pictureIndex++;
				if (!string.IsNullOrEmpty(pictureDef.Script))
					_clickablePictureBounds.Add(new ClickablePictureBounds(
						new Rect2(picture.Position, picture.Size),
						pictureIndex));
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
							Elapsed = 0f,
						});
				}
			},
			TextContentProvider = textDef => _textOverrides.TryGetValue(textDef.Id ?? string.Empty, out string overrideText)
				? overrideText
				: textDef.Content,
			TextColorProvider = textDef => textDef.Color ?? menuDef.TextColor,
			AfterAddText = (textDef, label, theme, content) =>
			{
				label.ZIndex = 8;
				label.PivotOffset = Vector2.Zero;
				if (!string.IsNullOrEmpty(textDef.Id))
				{
					_namedTextLabels[textDef.Id] = label;
					_namedTextLayout[textDef.Id] = (textDef, theme);
					if (_textOverrides.ContainsKey(textDef.Id)
						&& (textDef.CenterX
							|| textDef.RightX
							|| textDef.Align?.Equals("Right", StringComparison.OrdinalIgnoreCase) == true))
						label.Position = TextLayoutHelper.GetPosition(
							textDef,
							theme,
							content,
							Constants.MenuScreenWidth,
							Constants.MenuScreenHeight);
				}
			},
		});
		// Render static VSWAP sprites (e.g., SPR_DEATHCAM title - WL_ACT2.C:A_StartDeathCam)
		RenderStaticSprites(menuDef);
		// Render actor animations (e.g., boss death cam - WL_ACT2.C:A_StartDeathCam)
		RenderActorAnimations(menuDef);
		// Render tickers (intermission screen percent counters)
		RenderTickers(menuDef);
		// Render menu items (visible items only)
		RenderMenuItems(menuDef, selectedIndex, visibleItems);
		// Render cursor (WL_MENU.C:DrawMenuGun)
		RenderCursor(menuDef, selectedIndex, visibleItems);
		// Render pointer crosshairs (VR controllers / mouse)
		RenderCrosshairs();
		// Render modal overlay if active (on top of everything except crosshairs)
		if (modal is not null && modal.IsPending)
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
	/// Render actor animations defined in the menu (e.g., boss death cam).
	/// WL_ACT2.C:A_StartDeathCam - renders sprite frames from VSWAP at tic rate (1/70s).
	/// </summary>
	private void RenderActorAnimations(MenuDefinition menuDef)
	{
		if (menuDef.ActorAnimations is null || menuDef.ActorAnimations.Count == 0 || SpriteTextureProvider is null)
			return;
		StateCollection stateCollection = SharedAssetManager.CurrentGame?.StateCollection;
		if (stateCollection is null)
			return;
		foreach (ActorAnimationDefinition animDef in menuDef.ActorAnimations)
		{
			if (!stateCollection.States.TryGetValue(animDef.StartState, out State startState))
			{
				GD.PrintErr($"ERROR: ActorAnimation StartState '{animDef.StartState}' not found in StateCollection");
				continue;
			}
			if (startState.Shape < 0)
				continue;
			Texture2D texture = SpriteTextureProvider((ushort)startState.Shape);
			if (texture is null)
				continue;
			float displaySize = SpriteNativeSize * animDef.Scale,
				// Scale the node transform so the upscaled texture renders at native display size.
				// Size property alone doesn't constrain TextureRect when texture is larger than Size.
				nodeScale = texture.GetWidth() > 0 ? displaySize / texture.GetWidth() : 1f;
			TextureRect rect = new()
			{
				Texture = texture,
				Position = new Vector2(animDef.X - displaySize / 2f, animDef.Y - displaySize / 2f),
				Scale = new Vector2(nodeScale, nodeScale),
				TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
				ZIndex = 5,
			};
			_canvas.AddChild(rect);
			_actorAnimations.Add(new ActorAnimationState
			{
				TextureRect = rect,
				CurrentState = startState,
				Elapsed = 0f,
			});
		}
	}
	/// <summary>
	/// Render static VSWAP sprites (non-animated) on the menu panel.
	/// WL_ACT2.C:A_StartDeathCam - SPR_DEATHCAM title displayed during death replay.
	/// </summary>
	private void RenderStaticSprites(MenuDefinition menuDef)
	{
		if (menuDef.StaticSprites is null || menuDef.StaticSprites.Count == 0
			|| SpriteTextureProvider is null || SpritePageByNameProvider is null)
			return;
		foreach (StaticSpriteDefinition spriteDef in menuDef.StaticSprites)
		{
			ushort? page = SpritePageByNameProvider(spriteDef.Name);
			if (page is null)
			{
				GD.PrintErr($"ERROR: StaticSprite '{spriteDef.Name}' not found in sprite name map");
				continue;
			}
			Texture2D texture = SpriteTextureProvider(page.Value);
			if (texture is null)
				continue;
			float displaySize = SpriteNativeSize * spriteDef.Scale,
				nodeScale = texture.GetWidth() > 0 ? displaySize / texture.GetWidth() : 1f;
			TextureRect rect = new()
			{
				Texture = texture,
				Position = new Vector2(spriteDef.X - displaySize / 2f, spriteDef.Y - displaySize / 2f),
				Scale = new Vector2(nodeScale, nodeScale),
				TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
				ZIndex = 5,
			};
			_canvas.AddChild(rect);
		}
	}
	/// <summary>
	/// Render decorative horizontal stripes by stretching the leftmost pixel column.
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
			// WL_MENU.C:color_norml[active] / color_hlite[active] — per-item color overrides
			// TextColor and Highlight on the item mirror the Menus-level attributes.
			byte colorIndex = isSelected
				? (item.CustomProperties.TryGetValue("Highlight", out string hStr) && byte.TryParse(hStr, out byte h) ? h : (menuDef.Highlight ?? 0x13))
				: (item.CustomProperties.TryGetValue("TextColor", out string tcStr) && byte.TryParse(tcStr, out byte tc) ? tc : (menuDef.TextColor ?? 0x17));
			Color textColor = SharedAssetManager.GetPaletteColor(colorIndex);
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
		if (_primaryCrosshair is not null)
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
					FontColor = textColor,
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
				label.Position = new Vector2(x, y);
			// Track by name for dynamic updates
			_tickerLabels[tickerDef.Name] = label;
		}
	}
	/// <summary>
	/// Clears all text overrides set by UpdateText.
	/// Call when navigating to a new menu so stale overrides from the previous menu are not reapplied.
	/// </summary>
	public void ClearTextOverrides() => _textOverrides.Clear();
	/// <summary>
	/// Updates a named text label's content dynamically.
	/// Persists the value so it survives subsequent RefreshMenu calls within the same menu session.
	/// Called from Lua via SetText(id, value).
	/// </summary>
	/// <param name="name">The Id attribute of the text element</param>
	/// <param name="value">New text content</param>
	public void UpdateText(string name, string value)
	{
		_textOverrides[name] = value;
		if (_namedTextLabels.TryGetValue(name, out Label label))
		{
			label.Text = value;
			if (_namedTextLayout.TryGetValue(name, out (TextDefinition Definition, Theme Theme) layout))
				label.Position = TextLayoutHelper.GetPosition(
					textDef: layout.Definition,
					theme: layout.Theme,
					content: value,
					canvasWidth: Constants.MenuScreenWidth,
					canvasHeight: Constants.MenuScreenHeight);
		}
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
				float textWidth = font.GetStringSize(value, fontSize: theme.DefaultFontSize).X,
					anchorX = tickerDef.CenterX ? Constants.MenuScreenWidth / 2f
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
		if (SpriteTextureProvider is null)
			return;
		for (int i = 0; i < _actorAnimations.Count; i++)
		{
			ActorAnimationState anim = _actorAnimations[i];
			State current = anim.CurrentState;
			// Terminal state: Tics==0 and Next==self (loops to itself)
			bool isTerminal = current.Tics == 0 && current.Next == current;
			if (isTerminal)
				continue;
			if (current.Tics <= 0)
				continue;
			anim.Elapsed += delta;
			float stateDuration = current.Tics / 70f;
			if (anim.Elapsed >= stateDuration)
			{
				anim.Elapsed -= stateDuration;
				State next = current.Next;
				if (next is null)
					continue;
				anim.CurrentState = next;
				if (next.Shape >= 0)
				{
					Texture2D newTexture = SpriteTextureProvider((ushort)next.Shape);
					if (newTexture is not null)
						anim.TextureRect.Texture = newTexture;
				}
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
		MenuCollection menuCollection = SharedAssetManager.CurrentGame?.MenuCollection;
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
		bool isMessage = modal.Kind == ModalDialog.ModalKind.Message;
		// Size and position the message box (contains only text, no buttons inside)
		const float boxPad = 8f,   // inner padding for message box
			btnPad = 4f;   // inner padding for Yes/No boxes
		float msgBoxW = maxLineWidth + boxPad * 2f,
			msgBoxH = textHeight + boxPad * 2f;
		// For confirm dialogs, the Yes/No button row sits flush below the message box.
		// For message-only dialogs, only the message box is shown.
		float totalH = isMessage ? msgBoxH : msgBoxH + lineHeight + btnPad * 2f,
			msgBoxY = Mathf.Round((Constants.MenuScreenHeight - totalH) / 2f),
			msgBoxX = Mathf.Round((Constants.MenuScreenWidth - msgBoxW) / 2f);
		// Message box: fill + single PixelRect-style bevel (NWColor top/left, SEColor bottom/right)
		CanvasLayoutRenderHelper.DrawBevelledBox(_canvas, msgBoxX, msgBoxY, msgBoxW, msgBoxH, bgColor, colorNW, colorSE, 50, 51);
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
			},
		});
		if (!isMessage)
		{
			float btnBoxH = lineHeight + btnPad * 2f,
				btnBoxY = msgBoxY + msgBoxH + 1f,
				yesW = font.GetStringSize("Yes", fontSize: (int)fontSize).X,
				noW = font.GetStringSize("No", fontSize: (int)fontSize).X,
				yesBoxW = yesW + btnPad * 2f,
				noBoxW = noW + btnPad * 2f,
				// Yes is left-aligned with the message box; No is right-aligned
				yesBoxX = msgBoxX,
				noBoxX = msgBoxX + msgBoxW - noBoxW;
			// "Yes" box — outside/below the message box, left-aligned with it
			CanvasLayoutRenderHelper.DrawBevelledBox(_canvas, yesBoxX, btnBoxY, yesBoxW, btnBoxH, bgColor, colorNW, colorSE, 53, 54);
			_canvas.AddChild(new Label
			{
				Text = "Yes",
				Theme = theme,
				Position = new Vector2(yesBoxX + btnPad, btnBoxY + btnPad),
				ZIndex = 60,
				LabelSettings = new LabelSettings { Font = font, FontSize = (int)fontSize, FontColor = textColor },
			});
			// "No" box — outside/below the message box, right-aligned with it
			CanvasLayoutRenderHelper.DrawBevelledBox(_canvas, noBoxX, btnBoxY, noBoxW, btnBoxH, bgColor, colorNW, colorSE, 53, 54);
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
/// <summary>
/// Tracks state for an actor animation walking the state machine chain.
/// Used for boss death cam sequences (WL_ACT2.C:A_StartDeathCam).
/// </summary>
internal class ActorAnimationState
{
	/// <summary>The TextureRect node being animated.</summary>
	public TextureRect TextureRect { get; set; }
	/// <summary>Current state in the actor's state machine.</summary>
	public State CurrentState { get; set; }
	/// <summary>Time elapsed in the current state (seconds).</summary>
	public float Elapsed { get; set; }
}
