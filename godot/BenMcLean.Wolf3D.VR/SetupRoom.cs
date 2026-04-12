using System;
using BenMcLean.Wolf3D.Shared;
using BenMcLean.Wolf3D.Shared.Setup;
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
/// Also serves as the error display surface: Root calls ShowError() to display unhandled
/// exceptions, which prevents further loading and keeps the screen visible.
///
/// Display:
///   VR mode      — DosScreen quad attached to the camera (head-locked), 3 m forward
///   Flatscreen   — DosScreen in a CanvasLayer scaled to the largest 4:3 area in the window
/// </summary>
public partial class SetupRoom : Node3D, IRoom
{
	public bool SkipFade => true;

	private enum Phase { NotStarted, ShowingMessage, Loading, Done }

	private readonly IDisplayMode _displayMode;
	private readonly string _xmlPath;
	private DosScreen _dosScreen;
	private Phase _phase = Phase.NotStarted;
	private bool _hasExternalError;

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
		_displayMode.LocomotionEnabled = false;

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
	/// Displays an unhandled exception on the DosScreen and halts the loading sequence.
	/// Called by Root when an exception occurs outside the normal loading catch block.
	/// Safe to call before _Ready() — _hasExternalError prevents loading; display is best-effort.
	/// Layout is budgeted to the 80x25 screen: _Ready() uses 3 lines, leaving 22 for the error.
	/// Vital info (type + message) is written first so stack frames cannot push it off screen.
	/// Full exception detail is always in the ADB log.
	/// </summary>
	public void ShowError(Exception ex)
	{
		if (_hasExternalError)
			return; // already displaying an error — ignore subsequent calls
		_hasExternalError = true;
		if (_dosScreen is null)
			return;
		// Line budget: 3 header lines already on screen, 22 remaining.
		// 1  ERROR: {type}
		// 4  message (truncated)        +1 if truncated
		// 1  Stack:
		// 14 stack frames               +1 if truncated
		// = 21-22 lines max
		_dosScreen.WriteLine(Trunc($"ERROR: {ex.GetType().FullName}", 80));
		WriteLines(ex.Message, maxLines: 4);
		_dosScreen.WriteLine("Stack:");
		string[] frames = (ex.StackTrace ?? string.Empty)
			.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
		int show = Math.Min(frames.Length, 14);
		for (int i = 0; i < show; i++)
			_dosScreen.WriteLine(Trunc(frames[i].Trim(), 80));
		if (frames.Length > show)
			_dosScreen.WriteLine($"...{frames.Length - show} more frames");
	}

	private void WriteLines(string text, int maxLines)
	{
		string[] lines = text.Split('\n');
		int count = Math.Min(lines.Length, maxLines);
		for (int i = 0; i < count; i++)
			_dosScreen.WriteLine(Trunc(lines[i].TrimEnd(), 80));
		if (lines.Length > maxLines)
			_dosScreen.WriteLine($"...({lines.Length - maxLines} more lines)");
	}

	private static string Trunc(string s, int max) =>
		s.Length <= max ? s : s[..max];

	/// <summary>
	/// Attaches the DosScreen as a quad to the camera so it is always visible (head-locked).
	/// Size matches an 8-foot-wide screen at 3 metres distance.
	/// </summary>
	private void SetupVRDosScreen()
	{
		if (_displayMode.Camera is null)
			return;

		AddChild(new WorldEnvironment()
		{
			Environment = new Godot.Environment
			{
				BackgroundMode = Godot.Environment.BGMode.Color,
				BackgroundColor = Colors.Black,
				AmbientLightSource = Godot.Environment.AmbientSource.Color,
				AmbientLightColor = Colors.White,
				AmbientLightEnergy = 1.0f,
			}
		});

		_displayMode.Camera.AddChild(new MeshInstance3D()
		{
			Mesh = new QuadMesh() { Size = new Vector2(2.4384f, 1.8288f) }, // 8 ft × 6 ft in metres
			MaterialOverride = new StandardMaterial3D()
			{
				AlbedoTexture = _dosScreen.GetSubViewport().GetTexture(),
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				DisableReceiveShadows = true,
				DisableAmbientLight = true,
				CullMode = BaseMaterial3D.CullModeEnum.Back,
				Transparency = BaseMaterial3D.TransparencyEnum.Disabled,
			},
			Position = Vector3.Forward * 2.5f,
		});
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

		canvasLayer.AddChild(new ColorRect
		{
			Color = Colors.Black,
			Size = windowSize,
		});

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

		canvasLayer.AddChild(new TextureRect()
		{
			Texture = _dosScreen.GetSubViewport().GetTexture(),
			Size = dosSize,
			Position = dosPosition,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.Scale,
			TextureFilter = Control.TextureFilterEnum.Nearest,
		});
	}

	public override void _Process(double delta)
	{
		// If an external error was shown, stop — don't start or continue loading
		if (_hasExternalError)
			return;

		// DisplayMode.Update is called from Root._Process (ProcessMode.Always).
		// Calling it here too would apply turning and position validation twice per frame.

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
					// On Android, MANAGE_EXTERNAL_STORAGE must be granted before file access.
					// Checked here (after DosScreen is visible) so the error displays to the user.
					if (!AndroidPermissions.HasAllFilesAccess())
					{
						AndroidPermissions.OpenAllFilesAccessSettings();
						throw new UnauthorizedAccessException(
							"All Files Access permission is required to read game data from /sdcard/WOLF3D/.\n\n" +
							"The Android All Files Access settings page has been opened.\n" +
							"Grant 'All Files Access' to this app, then relaunch.");
					}

					// For non-initial loads, release the current game's VR 3D materials
					// before SharedAssetManager.LoadGame() disposes the atlas textures
					if (!IsInitialLoad)
						VRAssetManager.Cleanup();
					else
						SharedAssetManager.ExtractSharewareIfNeeded(
							System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_xmlPath)));

					SharedAssetManager.LoadGame(_xmlPath);

					_dosScreen.WriteLine("Done.");
					_phase = Phase.Done;
					// Root polls IsComplete and performs the scene transition
				}
				catch (Exception ex)
				{
					GD.PrintErr($"ERROR: {ex}");
					string msg = ex.Message;
					if (msg.Length > 2000)
						msg = msg[..2000];
					_dosScreen.WriteLine($"ERROR: {msg}");
					// Stay in Loading — Root will not transition and the error stays visible
				}
				break;
		}
	}
}
