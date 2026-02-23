using System;
using BenMcLean.Wolf3D.Shared;
using BenMcLean.Wolf3D.VR.VR;
using Godot;

namespace BenMcLean.Wolf3D.VR;

/// <summary>
/// Loading room that shows a DosScreen progress log while synchronously loading game assets.
/// Used for the initial shareware load (isInitialLoad=true) and for loading any selected game
/// (isInitialLoad=false). Root polls IsComplete and transitions accordingly.
///
/// Two-phase approach: Phase 1 writes the "Loading..." message and renders one frame so the
/// user sees feedback before Phase 2 performs the blocking SharedAssetManager.LoadGame() call.
///
/// Display:
///   VR mode      — DosScreen quad attached to the camera (head-locked), 1.5 m forward
///   Flatscreen   — DosScreen in a CanvasLayer scaled to the largest 4:3 area in the window
/// </summary>
public partial class SetupRoom : Node3D
{
	private enum Phase { NotStarted, ShowingMessage, Loading, Done }

	private readonly IDisplayMode _displayMode;
	private readonly string _xmlPath;
	private DosScreen _dosScreen;
	private Phase _phase = Phase.NotStarted;

	/// <summary>
	/// True when this is the first load (WL1 shareware). After completion Root shows the
	/// game selection menu. False for subsequent loads; after completion Root starts the game.
	/// </summary>
	public bool IsInitialLoad { get; }

	/// <summary>
	/// Polled by Root._Process(). True once the asset load completed successfully.
	/// </summary>
	public bool IsComplete => _phase == Phase.Done;

	/// <param name="displayMode">Display mode initialized by Root (VR or flatscreen).</param>
	/// <param name="xmlPath">Path to the game XML definition file to load.</param>
	/// <param name="isInitialLoad">
	/// True for the initial WL1 shareware load; false for the user-selected game load.
	/// </param>
	public SetupRoom(IDisplayMode displayMode, string xmlPath, bool isInitialLoad)
	{
		_displayMode = displayMode;
		_xmlPath = xmlPath;
		IsInitialLoad = isInitialLoad;
		Name = "SetupRoom";
		// Always process so the loading sequence runs even during fade transitions
		ProcessMode = ProcessModeEnum.Always;
	}

	public override void _Ready()
	{
		_displayMode.Initialize(this);

		_dosScreen = new DosScreen();
		AddChild(_dosScreen);

		if (_displayMode.IsVRActive)
			SetupVRDosScreen();
		else
			SetupFlatscreenDosScreen();

		_dosScreen.WriteLine("Wolfenstein 3-D VR Engine");
		_dosScreen.WriteLine("=========================");
		_dosScreen.WriteLine("");
	}

	/// <summary>
	/// Attaches the DosScreen as a quad to the camera so it is always visible (head-locked).
	/// Size matches an 8-foot-wide screen at 5 feet distance (typical VR desktop experience).
	/// </summary>
	private void SetupVRDosScreen()
	{
		if (_displayMode.Camera is null)
			return;

		MeshInstance3D quad = new MeshInstance3D()
		{
			Mesh = new QuadMesh()
			{
				Size = new Vector2(2.4384f, 1.8288f), // 8ft × 6ft in metres
			},
			MaterialOverride = new StandardMaterial3D()
			{
				AlbedoTexture = _dosScreen.GetViewport().GetTexture(),
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				DisableReceiveShadows = true,
				DisableAmbientLight = true,
				CullMode = BaseMaterial3D.CullModeEnum.Back,
				Transparency = BaseMaterial3D.TransparencyEnum.Disabled,
			},
			Position = Vector3.Forward * 1.5f,
		};
		_displayMode.Camera.AddChild(quad);
	}

	/// <summary>
	/// Displays the DosScreen as a 2D overlay scaled to the largest 4:3 area in the window,
	/// with a black letterbox/pillarbox background.
	/// </summary>
	private void SetupFlatscreenDosScreen()
	{
		CanvasLayer canvasLayer = new() { Layer = 1 };
		AddChild(canvasLayer);

		Vector2I windowSize = DisplayServer.WindowGetSize();

		ColorRect background = new()
		{
			Color = Colors.Black,
			Size = windowSize,
		};
		canvasLayer.AddChild(background);

		// Scale to the largest 4:3 rectangle that fits in the window (720:400 aspect)
		const float dosAspect = 720f / 400f;
		float windowAspect = (float)windowSize.X / windowSize.Y;
		Vector2 dosSize, dosPosition;

		if (windowAspect > dosAspect)
		{
			// Widescreen window — pillarbox
			dosSize = new Vector2(windowSize.Y * dosAspect, windowSize.Y);
			dosPosition = new Vector2((windowSize.X - dosSize.X) / 2f, 0f);
		}
		else
		{
			// Taller window — letterbox
			dosSize = new Vector2(windowSize.X, windowSize.X / dosAspect);
			dosPosition = new Vector2(0f, (windowSize.Y - dosSize.Y) / 2f);
		}

		TextureRect textureRect = new()
		{
			Texture = _dosScreen.GetViewport().GetTexture(),
			Size = dosSize,
			Position = dosPosition,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.Scale,
			TextureFilter = Control.TextureFilterEnum.Nearest,
		};
		canvasLayer.AddChild(textureRect);
	}

	public override void _Process(double delta)
	{
		_displayMode.Update(delta);

		switch (_phase)
		{
			case Phase.NotStarted:
				// Write the "Loading..." message and let one frame render before blocking
				_dosScreen.WriteLine($"Loading: {System.IO.Path.GetFileNameWithoutExtension(_xmlPath)}...");
				_phase = Phase.ShowingMessage;
				break;

			case Phase.ShowingMessage:
				_phase = Phase.Loading;
				try
				{
					// For non-initial loads, release the current game's VR 3D materials
					// before SharedAssetManager.LoadGame() disposes the atlas textures
					if (!IsInitialLoad)
						VRAssetManager.Cleanup();

					SharedAssetManager.LoadGame(_xmlPath);

					_dosScreen.WriteLine("Done.");
					_phase = Phase.Done;
					// Root polls IsComplete and performs the scene transition
				}
				catch (Exception ex)
				{
					_dosScreen.WriteLine($"ERROR: {ex.Message}");
					// Stay in Loading — Root will not transition and the error stays visible
				}
				break;
		}
	}
}
