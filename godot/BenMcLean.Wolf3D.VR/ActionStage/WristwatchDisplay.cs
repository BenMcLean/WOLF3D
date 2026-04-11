using BenMcLean.Wolf3D.Shared.Automap;
using BenMcLean.Wolf3D.Shared.StatusBar;
using Godot;

namespace BenMcLean.Wolf3D.VR.ActionStage;

/// <summary>
/// A 320×200 (mode 13h 4:3) wristwatch-style HUD attached to the left grip controller in VR.
/// Renders the automap (top 320×160) and status bar (bottom 320×40) in a single SubViewport
/// and applies the result to a world-space QuadMesh that billboards to face the camera.
/// </summary>
public partial class WristwatchDisplay : Node3D
{
	// Physical screen size in Godot metres — true 4:3 aspect ratio.
	// 320×200 VGA pixels were non-square on original CRT hardware (pixel aspect ratio 5:6),
	// so the quad must be 4:3, not the 16:10 ratio of the raw pixel dimensions.
	// Tune if the display feels too large or too small in headset.
	private const float ScreenWidth = 0.1f,
		ScreenHeight = ScreenWidth * 0.75f, // 4:3 — pixel aspect ratio correction for mode 13h
		// Gaze opacity: dot product of camera forward vs. direction-from-camera-to-screen
		// (1 = dead-centre, 0 = 90° to the side). Fully transparent below Hide, fully opaque above Show.
		GazeHideDot = 0.70f,  // ~46° off-centre — screen fades to invisible beyond this
		GazeShowDot = 0.80f;  // ~37° off-centre — screen is fully opaque within this

	private readonly AutomapController _automapController;
	private readonly StatusBarRenderer _statusBarRenderer;
	private readonly Camera3D _camera;

	private MeshInstance3D _screenMesh;
	private StandardMaterial3D _screenMaterial;

	/// <param name="automapController">Provides AutomapRenderer. WristwatchDisplay owns the SubViewport.</param>
	/// <param name="statusBarRenderer">Provides the status bar canvas. WristwatchDisplay owns the SubViewport.</param>
	/// <param name="camera">Head camera used for billboarding and wrist-raise detection.</param>
	public WristwatchDisplay(
		AutomapController automapController,
		StatusBarRenderer statusBarRenderer,
		Camera3D camera)
	{
		Name = "WristwatchDisplay";
		_automapController = automapController;
		_statusBarRenderer = statusBarRenderer;
		_camera = camera;
	}

	public override void _Ready()
	{
		// --- Single 320×200 SubViewport: automap (top) + status bar (bottom) in one pass ---
		SubViewport hudViewport = new()
		{
			Name = "WristwatchHudViewport",
			Size = new Vector2I(320, 200),
			Disable3D = true,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
		};
		AddChild(hudViewport);

		// Automap renderer placed at the top of the viewport
		hudViewport.AddChild(_automapController.Renderer);

		// Status bar canvas placed immediately below the automap
		_statusBarRenderer.Canvas.Position = new Vector2(0f, AutomapRenderer.ViewHeight);
		hudViewport.AddChild(_statusBarRenderer.Canvas);

		// --- World-space screen quad ---
		_screenMaterial = new StandardMaterial3D
		{
			AlbedoTexture = hudViewport.GetTexture(),
			// Unshaded so the retro pixel art is not affected by scene lighting
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			AlbedoColor = new Color(1f, 1f, 1f, 0f), // start invisible; gaze logic fades it in
		};

		_screenMesh = new MeshInstance3D
		{
			Name = "WristwatchScreen",
			Mesh = new QuadMesh { Size = new Vector2(ScreenWidth, ScreenHeight) },
			MaterialOverride = _screenMaterial,
		};
		AddChild(_screenMesh);

		// --- Position on the inner wrist (palm side) of the left grip controller ---
		// Grip pose convention for this setup: +Y toward palm, -Z toward fingers, +Z toward wrist.
		// Z offset moves the display toward the wrist end (away from fingertips).
		Position = new Vector3(0f, 0.02f, 0.125f);
		Rotation = new Vector3(Mathf.DegToRad(-45f), Mathf.DegToRad(180f), Mathf.DegToRad(-90f));
	}

	public override void _Process(double delta)
	{
		UpdateScreenBillboard();
		UpdateScreenOpacity();
	}

	/// <summary>
	/// Fades the screen in when the camera is looking toward it, out when looking away.
	/// Since the screen billboards to always face the camera, gaze is measured as the dot
	/// product of the camera's forward direction with the direction from camera to screen:
	/// 1 = screen dead-centre in view, approaching 0 = far off to the side.
	/// </summary>
	private void UpdateScreenOpacity()
	{
		if (_screenMaterial is null || _screenMesh is null || _camera is null
			|| _screenMaterial.IsQueuedForDeletion())
			return;

		// Camera looks toward its own -Z in Godot's coordinate system
		Vector3 cameraForward = -_camera.GlobalBasis.Z;
		Vector3 toScreen = (_screenMesh.GlobalPosition - _camera.GlobalPosition).Normalized();
		float dot = cameraForward.Dot(toScreen);

		float alpha = Mathf.Clamp(
			Mathf.InverseLerp(GazeHideDot, GazeShowDot, dot),
			0f, 1f);

		_screenMaterial.AlbedoColor = new Color(1f, 1f, 1f, alpha);
	}

	/// <summary>
	/// Billboards the screen mesh to face the camera every frame.
	/// The screen's world position (driven by WristwatchDisplay's attachment to the
	/// left grip controller) is unchanged; only the mesh's orientation is overridden.
	/// World Y is used as the up reference so the content stays right-side up regardless
	/// of which way the wrist is held or the gun is pointed.
	/// </summary>
	private void UpdateScreenBillboard()
	{
		if (_screenMesh is null || _camera is null)
			return;

		Vector3 toCamera = (_camera.GlobalPosition - _screenMesh.GlobalPosition).Normalized();

		// Degenerate: screen directly above or below camera — fall back to world forward as up
		Vector3 worldUp = Mathf.Abs(toCamera.Dot(Vector3.Up)) > 0.999f
			? -Vector3.Forward
			: Vector3.Up;

		Vector3 right = worldUp.Cross(toCamera).Normalized();
		Vector3 up = toCamera.Cross(right).Normalized();
		_screenMesh.GlobalBasis = new Basis(right, up, toCamera);
	}
}
