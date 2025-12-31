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
		// TODO: Wire up to actual config loading
		Config config = new();
		// Create logger (TODO: Wire up to actual logger factory)
		ILogger logger = null; // Use null logger for now
		// Create MenuManager (menus don't need RNG/GameClock - not deterministic)
		_menuManager = new MenuManager(
			menuCollection,
			config,
			logger);
		// Add the SubViewport to scene tree (required for rendering, but not as child of container)
		AddChild(_menuManager.Renderer.Viewport);
		// Create CanvasLayer to render 2D menu on top of 3D scene
		CanvasLayer canvasLayer = new()
		{
			Layer = 1, // Render above 3D scene
		};
		AddChild(canvasLayer);
		// Get window size for fullscreen display
		Vector2I windowSize = DisplayServer.WindowGetSize();
		// Create TextureRect to display the viewport texture, scaled to window size
		TextureRect textureRect = new()
		{
			Texture = _menuManager.Renderer.ViewportTexture,
			Size = windowSize,
			ExpandMode = TextureRect.ExpandModeEnum.FitWidth, // Scale to fit window
			StretchMode = TextureRect.StretchModeEnum.Scale, // Scale without keeping aspect ratio (will fill)
			TextureFilter = Control.TextureFilterEnum.Nearest // Sharp pixel-perfect rendering
		};
		canvasLayer.AddChild(textureRect);
		// Note: MenuManager constructor already calls RefreshMenu via NavigateToMenu
	}
	/// <summary>
	/// Called each frame.
	/// Updates the menu system.
	/// </summary>
	/// <param name="delta">Time since last frame in seconds</param>
	public override void _Process(double delta) => _menuManager?.Update((float)delta);
}
