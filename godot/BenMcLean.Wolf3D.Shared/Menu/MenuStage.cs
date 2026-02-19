using BenMcLean.Wolf3D.Assets.Gameplay;
using Godot;
using Microsoft.Extensions.Logging;

namespace BenMcLean.Wolf3D.Shared.Menu;

/// <summary>
/// Godot scene root for the menu system.
/// Hosts MenuManager and displays the menu viewport.
/// Similar to ActionStage but for menus instead of gameplay.
/// </summary>
public partial class MenuStage : Node
{
	private MenuManager _menuManager;
	private ColorRect _marginBackground;
	/// <summary>
	/// Gets the menu manager instance.
	/// </summary>
	public MenuManager MenuManager => _menuManager;
	/// <summary>
	/// Gets the current menu state.
	/// Used by Root.cs to detect StartGame flag.
	/// </summary>
	public MenuState SessionState => _menuManager?.SessionState;
	/// <summary>
	/// Called when the node is added to the scene tree.
	/// Initializes the menu system.
	/// </summary>
	public override void _Ready()
	{
		// Get menu data from SharedAssetManager
		if (SharedAssetManager.CurrentGame?.MenuCollection is not MenuCollection menuCollection)
		{
			GD.PrintErr("ERROR: No MenuCollection in SharedAssetManager.CurrentGame");
			return;
		}
		// Get config from SharedAssetManager (or create default)
		// Initialize SharedAssetManager.Config if not already set
		SharedAssetManager.Config ??= new Config();
		// Create logger (TODO: Wire up to actual logger factory)
		ILogger logger = null; // Use null logger for now
		// Create MenuManager (menus don't need RNG/GameClock - not deterministic)
		_menuManager = new MenuManager(
			menuCollection,
			SharedAssetManager.Config,
			logger);
		// Add the SubViewport to scene tree (required for rendering, but not as child of container)
		AddChild(_menuManager.Renderer.Viewport);
		// Create CanvasLayer to render 2D menu on top of 3D scene
		CanvasLayer canvasLayer = new()
		{
			Layer = 1, // Render above 3D scene
		};
		AddChild(canvasLayer);
		// Get window size
		Vector2I windowSize = DisplayServer.WindowGetSize();
		// Create margin background ColorRect that fills the entire window
		// This will be colored with the menu's border color
		_marginBackground = new ColorRect
		{
			Color = Colors.Black, // Default to black, will be updated by border color events
			Size = windowSize,
		};
		canvasLayer.AddChild(_marginBackground);
		// Calculate size for 4:3 aspect ratio display
		// SVGA Mode 13h is 320x200, which is 4:3 aspect ratio (with square pixels)
		const float menuAspectRatio = 4.0f / 3.0f;
		float windowAspectRatio = (float)windowSize.X / windowSize.Y;
		Vector2 menuSize,
			menuPosition;
		if (windowAspectRatio > menuAspectRatio)
		{
			// Window is wider than 4:3 (widescreen) - fit to height with pillarbox margins
			menuSize = new Vector2(windowSize.Y * menuAspectRatio, windowSize.Y);
			menuPosition = new Vector2((windowSize.X - menuSize.X) / 2, 0);
		}
		else
		{
			// Window is narrower than 4:3 (or exactly 4:3) - fit to width with letterbox margins
			menuSize = new Vector2(windowSize.X, windowSize.X / menuAspectRatio);
			menuPosition = new Vector2(0, (windowSize.Y - menuSize.Y) / 2);
		}
		// Create TextureRect to display the viewport texture with manual sizing
		TextureRect textureRect = new()
		{
			Texture = _menuManager.Renderer.ViewportTexture,
			Size = menuSize,
			Position = menuPosition,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.Scale, // Scale to fill the rect
			TextureFilter = Control.TextureFilterEnum.Nearest, // Sharp pixel-perfect rendering
		};
		canvasLayer.AddChild(textureRect);
		// Subscribe to border color change events
		_menuManager.Renderer.BorderColorChanged += OnBorderColorChanged;
		// Set initial margin color
		OnBorderColorChanged(_menuManager.Renderer.CurrentBorderColor);
		// Note: MenuManager constructor already calls RefreshMenu via NavigateToMenu
	}
	/// <summary>
	/// Called when the menu border color changes.
	/// Updates the margin background to match the new border color.
	/// </summary>
	/// <param name="color">New border color from the menu</param>
	private void OnBorderColorChanged(Color color)
	{
		if (_marginBackground is not null)
			_marginBackground.Color = color;
	}
	/// <summary>
	/// Called each frame.
	/// Updates the menu system.
	/// </summary>
	/// <param name="delta">Time since last frame in seconds</param>
	public override void _Process(double delta) => _menuManager?.Update((float)delta);
}
