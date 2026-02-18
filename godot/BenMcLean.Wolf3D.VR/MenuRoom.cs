using System;
using BenMcLean.Wolf3D.Shared;
using BenMcLean.Wolf3D.Shared.Menu;
using BenMcLean.Wolf3D.Shared.Menu.Input;
using BenMcLean.Wolf3D.Simulator;
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

	// Menu panel sizing in VR (in meters)
	private const float PanelWidth = Constants.TileWidth;                // One tile wide
	private const float PanelHeight = Constants.TileWidth * 3f / 4f;     // 4:3 aspect ratio

	/// <summary>
	/// True when the menu signals to start the game.
	/// Polled by Root._Process().
	/// </summary>
	public bool ShouldStartGame => _menuManager?.SessionState?.StartGame ?? false;

	/// <summary>
	/// Set by Root when a game is suspended in the background.
	/// When true, pressing ESC at the root menu will resume the game.
	/// </summary>
	public bool HasSuspendedGame { get; set; }

	/// <summary>
	/// Set when the player wants to resume a suspended game.
	/// Set when ESC/Cancel is pressed at the root menu while HasSuspendedGame is true.
	/// Polled by Root._Process(); Root is responsible for acting on this.
	/// </summary>
	public bool PendingResumeGame { get; private set; }
	/// <summary>
	/// Set when the user confirmed the End Game dialog.
	/// Polled by Root._Process(); Root discards the suspended game and transitions to a fresh menu.
	/// </summary>
	public bool PendingEndGame { get; private set; }

	/// <summary>
	/// Selected episode from menu.
	/// </summary>
	public int SelectedEpisode => _menuManager?.SessionState?.SelectedEpisode ?? 0;

	/// <summary>
	/// Selected difficulty from menu.
	/// </summary>
	public int SelectedDifficulty => _menuManager?.SessionState?.SelectedDifficulty ?? 0;

	/// <summary>
	/// Optional level transition request for intermission mode.
	/// When set, the MenuRoom shows the "LevelComplete" menu instead of "Main".
	/// </summary>
	public ActionStage.LevelTransitionRequest LevelTransition { get; set; }

	/// <summary>
	/// Optional override for the starting menu name.
	/// When set, navigates to this menu instead of the collection's default StartMenu.
	/// Used for intermission screen ("LevelComplete").
	/// </summary>
	public string StartMenuOverride { get; set; }

	/// <summary>
	/// Pending level transition after intermission is dismissed.
	/// Set by Lua ContinueToNextLevel(), polled by Root._Process().
	/// </summary>
	public ActionStage.LevelTransitionRequest PendingLevelTransition { get; private set; }

	/// <summary>
	/// Pending load game slot. Null when no load is pending.
	/// Set by Lua LoadGame(slot), polled by Root._Process().
	/// </summary>
	public int? PendingLoadGame => _menuManager?.SessionState?.LoadGameSlot;

	/// <summary>
	/// The suspended simulator instance, set by Root when a game is suspended.
	/// Used by SaveGameManager to capture the simulator state for saving.
	/// </summary>
	public Simulator.Simulator SuspendedSimulator { get; set; }

	/// <summary>
	/// The level index of the suspended game.
	/// Used by SaveGameManager to determine the map name for display.
	/// </summary>
	public int SuspendedLevelIndex { get; set; }

	/// <summary>
	/// Sets the fade transition handler for menu screen navigations.
	/// The callback receives an Action (the actual navigation work) to execute at mid-fade.
	/// Must be called after _Ready (when MenuManager exists).
	/// </summary>
	public void SetFadeTransitionHandler(Action<Action> handler)
	{
		if (_menuManager != null)
			_menuManager.FadeTransitionCallback = handler;
	}

	/// <summary>
	/// Creates a new MenuRoom with the specified display mode.
	/// </summary>
	/// <param name="displayMode">The active display mode (VR or flatscreen).</param>
	public MenuRoom(IDisplayMode displayMode)
	{
		_displayMode = displayMode;
		Name = "MenuRoom";
		// Must be Pausable so menu input stops during fade transitions
		// (Root is ProcessMode.Always, so Inherit would resolve to Always)
		ProcessMode = ProcessModeEnum.Pausable;
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
		// Wire up in-game state so menu items with InGame conditions show/hide correctly
		_menuManager.ScriptContext.IsGameInProgressFunc = () => HasSuspendedGame;
		// Wire up ResumeGame so "Back to Game" menu item signals Root to resume
		_menuManager.ScriptContext.CloseAllMenusAction = () =>
		{
			_menuManager.CloseAllMenus();
			if (HasSuspendedGame)
				PendingResumeGame = true;
		};

		// Wire up save/load game delegates
		_menuManager.SaveSimulatorFunc = () => SuspendedSimulator;
		_menuManager.GetMapNameFunc = () =>
		{
			if (SuspendedSimulator == null)
				return "Unknown";
			int levelIndex = SuspendedLevelIndex;
			if (levelIndex >= 0 && levelIndex < SharedAssetManager.CurrentGame.Maps.Length)
				return SharedAssetManager.CurrentGame.Maps[levelIndex]?.Name ?? $"Level {levelIndex + 1}";
			return $"Level {levelIndex + 1}";
		};

		// If in intermission mode, override start menu and pass completion stats
		if (!string.IsNullOrEmpty(StartMenuOverride))
		{
			// Pass completion stats to the script context for Lua access
			if (LevelTransition?.CompletionStats != null)
				_menuManager.ScriptContext.CompletionStats = LevelTransition.CompletionStats;
			// Pass accumulated stats for Victory screen
			if (LevelTransition?.AllLevelStats != null)
				_menuManager.ScriptContext.AllLevelStats = LevelTransition.AllLevelStats;
			// Navigate to the override menu (e.g., "LevelComplete") instead of default
			_menuManager.NavigateToMenu(StartMenuOverride);
		}

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
		StandardMaterial3D material = new()
		{
			AlbedoTexture = _menuManager.Renderer.ViewportTexture,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			DisableReceiveShadows = true,
			DisableAmbientLight = true,
			CullMode = BaseMaterial3D.CullModeEnum.Back,
			Transparency = BaseMaterial3D.TransparencyEnum.Disabled,
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
	private void PositionMenuPanel()
	{
		if (_menuPanel == null || _displayMode.Camera == null)
			return;

		// Get initial camera position and forward direction
		Vector3 cameraPos = _displayMode.ViewerPosition;
		float cameraYRotation = _displayMode.ViewerYRotation;

		// Calculate panel position: one tile width in front of camera's initial position
		Vector3 forward = new Vector3(
			Mathf.Sin(cameraYRotation),
			0,
			-Mathf.Cos(cameraYRotation)
		).Normalized();

		Vector3 panelPosition = cameraPos + forward * Constants.TileWidth;
		// Center panel between floor (0) and ceiling (TileHeight)
		panelPosition.Y = Constants.HalfTileHeight;

		_menuPanel.GlobalPosition = panelPosition;
	}

	/// <summary>
	/// Rotates the menu panel to face the camera's current position.
	/// Called every frame. Rotates only on Y axis (no tilt).
	/// </summary>
	private void UpdateMenuPanelRotation()
	{
		if (_menuPanel == null || _displayMode.Camera == null)
			return;

		Vector3 cameraPos = _displayMode.ViewerPosition;
		Vector3 panelPos = _menuPanel.GlobalPosition;

		// Calculate Y rotation to face camera position (ignore Y difference)
		float deltaX = cameraPos.X - panelPos.X;
		float deltaZ = cameraPos.Z - panelPos.Z;
		float yRotation = Mathf.Atan2(deltaX, deltaZ);

		_menuPanel.Rotation = new Vector3(0, yRotation, 0);
	}

	private void OnBorderColorChanged(Color color)
	{
		if (_marginBackground != null)
			_marginBackground.Color = color;
	}

	public override void _Input(InputEvent @event)
	{
		// Forward input events to the active pointer provider
		_flatscreenPointerProvider?.HandleInput(@event);
		_vrPointerProvider?.HandleInput(@event);
	}

	public override void _Process(double delta)
	{
		// Update menu manager
		_menuManager?.Update((float)delta);

		// Quit: user confirmed quit dialog
		if (_menuManager?.PendingQuit == true)
		{
			GetTree().Quit();
			return;
		}
		// End game: user confirmed end-game dialog
		if (_menuManager?.PendingEndGame == true)
		{
			_menuManager.ClearPendingEndGame();
			PendingEndGame = true;
		}
		// ESC at root menu:
		//   - If a game is suspended → resume it
		//   - Otherwise → show quit confirmation dialog (original Wolf3D behavior)
		if (_menuManager?.CancelAtRootRequested == true)
		{
			_menuManager.ClearCancelAtRoot();
			if (HasSuspendedGame)
				PendingResumeGame = true;
			else
				_menuManager.ScriptContext.RequestQuit();
		}

		// Check if intermission screen was dismissed (Lua called ContinueToNextLevel)
		if (_menuManager?.ScriptContext?.ContinueToNextLevelRequested == true && LevelTransition != null)
		{
			PendingLevelTransition = LevelTransition;
		}

		// In VR mode, position the menu panel once after XR tracking is active
		// (Camera position is zero until tracking starts)
		if (_displayMode.IsVRActive && !_menuPanelPositioned && _displayMode.ViewerPosition != Vector3.Zero)
		{
			PositionMenuPanel();
			_menuPanelPositioned = true;
		}

		// Rotate panel to face camera every frame (true billboard)
		if (_displayMode.IsVRActive && _menuPanelPositioned)
		{
			UpdateMenuPanelRotation();
		}
	}
}
