using BenMcLean.Wolf3D.Shared;
using BenMcLean.Wolf3D.Shared.Menu;
using BenMcLean.Wolf3D.Shared.Menu.Input;
using BenMcLean.Wolf3D.VR.Menu;
using BenMcLean.Wolf3D.VR.VR;
using Godot;

namespace BenMcLean.Wolf3D.VR;

/// <summary>
/// VR-aware menu room that wraps MenuStage with VR support.
/// In VR mode: Displays menu on a floating 3D panel.
/// In flatscreen mode: Uses existing 2D overlay approach.
/// </summary>
public partial class MenuRoom : Node3D
{
	private readonly IDisplayMode _displayMode;
	private MenuManager _menuManager;
	private MeshInstance3D _menuPanel;
	private ColorRect _marginBackground;
	private bool _menuPanelPositioned;
	private VRMenuPointerProvider _vrPointerProvider;
	private FlatscreenMenuPointerProvider _flatscreenPointerProvider;

	// Menu panel positioning in VR (in meters)
	private const float PanelDistance = 2.5f;         // Distance from camera
	private const float PanelWidth = 1.6f;            // Width of panel (4:3 aspect)
	private const float PanelHeight = 1.2f;           // Height of panel
	private const float PanelTiltDegrees = -10f;      // Slight downward tilt

	/// <summary>
	/// True when the menu signals to start the game.
	/// Polled by Root._Process().
	/// </summary>
	public bool ShouldStartGame => _menuManager?.SessionState?.StartGame ?? false;

	/// <summary>
	/// Selected episode from menu.
	/// </summary>
	public int SelectedEpisode => _menuManager?.SessionState?.SelectedEpisode ?? 0;

	/// <summary>
	/// Selected difficulty from menu.
	/// </summary>
	public int SelectedDifficulty => _menuManager?.SessionState?.SelectedDifficulty ?? 0;

	/// <summary>
	/// Creates a new MenuRoom with the specified display mode.
	/// </summary>
	/// <param name="displayMode">The active display mode (VR or flatscreen).</param>
	public MenuRoom(IDisplayMode displayMode)
	{
		_displayMode = displayMode;
		Name = "MenuRoom";
	}

	public override void _Ready()
	{
		// Initialize the display mode camera rig
		_displayMode.Initialize(this);

		// Create menu manager
		if (SharedAssetManager.CurrentGame?.MenuCollection is not Assets.Gameplay.MenuCollection menuCollection)
		{
			GD.PrintErr("ERROR: No MenuCollection in SharedAssetManager.CurrentGame");
			return;
		}

		SharedAssetManager.Config ??= new Assets.Gameplay.Config();
		_menuManager = new MenuManager(
			menuCollection,
			SharedAssetManager.Config,
			logger: null);

		// Add the menu viewport to the scene tree (required for rendering)
		AddChild(_menuManager.Renderer.Viewport);

		if (_displayMode.IsVRActive)
		{
			SetupVRMenuPanel();
		}
		else
		{
			SetupFlatscreenMenu();
		}
	}

	/// <summary>
	/// Sets up the menu as a floating 3D panel for VR.
	/// </summary>
	private void SetupVRMenuPanel()
	{
		// Create a quad mesh for the menu panel
		QuadMesh quadMesh = new()
		{
			Size = new Vector2(PanelWidth, PanelHeight),
		};

		// Create material that displays the menu viewport texture
		// UV1Scale.X = -1 flips the texture horizontally to correct for viewing the back of the quad
		StandardMaterial3D material = new()
		{
			AlbedoTexture = _menuManager.Renderer.ViewportTexture,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			DisableReceiveShadows = true,
			DisableAmbientLight = true,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled, // Visible from both sides
			Transparency = BaseMaterial3D.TransparencyEnum.Disabled,
			Uv1Scale = new Vector3(-1, 1, 1), // Flip horizontally to correct mirroring
		};

		// Create the mesh instance
		_menuPanel = new MeshInstance3D
		{
			Name = "MenuPanel",
			Mesh = quadMesh,
			MaterialOverride = material,
		};

		// Add panel to scene; position will be set once XR tracking is active
		AddChild(_menuPanel);

		// Create VR pointer provider for crosshair tracking
		_vrPointerProvider = new VRMenuPointerProvider(_displayMode);
		_vrPointerProvider.SetMenuPanel(_menuPanel, PanelWidth, PanelHeight);
		_menuManager.SetPointerProvider(_vrPointerProvider);

		// Add simple environment lighting for the VR space
		WorldEnvironment worldEnvironment = new()
		{
			Environment = new Godot.Environment
			{
				BackgroundMode = Godot.Environment.BGMode.Color,
				BackgroundColor = new Color(0.1f, 0.1f, 0.15f), // Dark blue-gray
				AmbientLightSource = Godot.Environment.AmbientSource.Color,
				AmbientLightColor = Colors.White,
				AmbientLightEnergy = 0.3f,
			}
		};
		AddChild(worldEnvironment);
	}

	/// <summary>
	/// Sets up the menu as a 2D overlay for flatscreen mode.
	/// Similar to the original MenuStage implementation.
	/// </summary>
	private void SetupFlatscreenMenu()
	{
		// Create CanvasLayer to render 2D menu on top of 3D scene
		CanvasLayer canvasLayer = new()
		{
			Layer = 1,
		};
		AddChild(canvasLayer);

		// Get window size
		Vector2I windowSize = DisplayServer.WindowGetSize();

		// Create margin background
		_marginBackground = new ColorRect
		{
			Color = Colors.Black,
			Size = windowSize,
		};
		canvasLayer.AddChild(_marginBackground);

		// Calculate size for 4:3 aspect ratio display
		const float menuAspectRatio = 4.0f / 3.0f;
		float windowAspectRatio = (float)windowSize.X / windowSize.Y;
		Vector2 menuSize;
		Vector2 menuPosition;

		if (windowAspectRatio > menuAspectRatio)
		{
			// Widescreen - pillarbox
			menuSize = new Vector2(windowSize.Y * menuAspectRatio, windowSize.Y);
			menuPosition = new Vector2((windowSize.X - menuSize.X) / 2, 0);
		}
		else
		{
			// Taller - letterbox
			menuSize = new Vector2(windowSize.X, windowSize.X / menuAspectRatio);
			menuPosition = new Vector2(0, (windowSize.Y - menuSize.Y) / 2);
		}

		// Create TextureRect to display the viewport texture
		TextureRect textureRect = new()
		{
			Texture = _menuManager.Renderer.ViewportTexture,
			Size = menuSize,
			Position = menuPosition,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.Scale,
			TextureFilter = Control.TextureFilterEnum.Nearest,
		};
		canvasLayer.AddChild(textureRect);

		// Subscribe to border color changes
		_menuManager.Renderer.BorderColorChanged += OnBorderColorChanged;
		OnBorderColorChanged(_menuManager.Renderer.CurrentBorderColor);

		// Create flatscreen pointer provider for mouse crosshair tracking
		_flatscreenPointerProvider = new FlatscreenMenuPointerProvider();
		_flatscreenPointerProvider.SetMenuDisplayArea(menuPosition, menuSize);
		_menuManager.SetPointerProvider(_flatscreenPointerProvider);
	}

	/// <summary>
	/// Positions the menu panel in front of the VR camera.
	/// Called once after XR tracking becomes active.
	/// </summary>
	private void UpdateMenuPanelPosition()
	{
		if (_menuPanel == null || _displayMode.Camera == null)
			return;

		// Get camera position and forward direction
		Vector3 cameraPos = _displayMode.ViewerPosition;
		float cameraYRotation = _displayMode.ViewerYRotation;

		// Calculate panel position: in front of camera at fixed distance
		// Use only Y rotation so panel stays vertical
		Vector3 forward = new Vector3(
			Mathf.Sin(cameraYRotation),
			0,
			-Mathf.Cos(cameraYRotation)
		).Normalized();

		Vector3 panelPosition = cameraPos + forward * PanelDistance;
		// Adjust height to be at eye level (slightly below center of view)
		panelPosition.Y = cameraPos.Y - 0.1f;

		_menuPanel.GlobalPosition = panelPosition;

		// Rotate panel to face the camera (around Y axis only)
		_menuPanel.Rotation = new Vector3(
			Mathf.DegToRad(PanelTiltDegrees),  // Slight downward tilt
			cameraYRotation + Mathf.Pi,        // Face toward camera
			0
		);
	}

	private void OnBorderColorChanged(Color color)
	{
		if (_marginBackground != null)
			_marginBackground.Color = color;
	}

	public override void _Process(double delta)
	{
		// Update menu manager
		_menuManager?.Update((float)delta);

		// In VR mode, position the menu panel once after XR tracking is active
		// (Camera position is zero until tracking starts)
		if (_displayMode.IsVRActive && !_menuPanelPositioned && _displayMode.ViewerPosition != Vector3.Zero)
		{
			UpdateMenuPanelPosition();
			_menuPanelPositioned = true;
		}
	}
}
