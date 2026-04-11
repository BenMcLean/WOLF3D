using BenMcLean.Wolf3D.Shared.Automap;
using BenMcLean.Wolf3D.Shared.StatusBar;
using Godot;

namespace BenMcLean.Wolf3D.VR.ActionStage;

/// <summary>
/// A 320×200 (mode 13h 4:3) wristwatch-style HUD attached to the left grip controller in VR.
/// Composites the automap (top 320×160) and status bar (bottom 320×40) into a single
/// SubViewport whose texture is applied to a world-space QuadMesh.
/// Fades in when the wrist is raised toward the head (palm-facing detection via dot product).
/// </summary>
public partial class WristwatchDisplay : Node3D
{
	// Physical screen size in Godot metres — true 4:3 aspect ratio.
	// 320×200 VGA pixels were non-square on original CRT hardware (pixel aspect ratio 5:6),
	// so the quad must be 4:3, not the 16:10 ratio of the raw pixel dimensions.
	// Tune if the display feels too large or too small in headset.
	private const float ScreenWidth = 0.1f,
		ScreenHeight = ScreenWidth * 0.75f,
		// Dot-product thresholds for the wrist-raise fade.
		// Below ShowDot: fully transparent. Above FullDot: fully opaque.
		// The screen faces the palm, so dot = 1 when the palm points directly at the camera.
		WristRaiseShowDot = 0.3f,
		WristRaiseFullDot = 0.65f;

	private readonly AutomapController _automapController;
	private readonly StatusBarRenderer _statusBarRenderer;
	private readonly Camera3D _camera;

	private SubViewport _compositorViewport;
	private MeshInstance3D _screenMesh;
	private StandardMaterial3D _screenMaterial;

	/// <param name="automapController">
	/// Owns the 320×160 automap SubViewport. WristwatchDisplay adds it to the scene tree.
	/// </param>
	/// <param name="statusBarRenderer">
	/// Owns the 320×40 status bar SubViewport. WristwatchDisplay adds it to the scene tree.
	/// </param>
	/// <param name="camera">Head camera used for wrist-raise dot-product detection.</param>
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
		// --- Inner SubViewports must be in the scene tree to render ---
		// They are owned by AutomapController / StatusBarRenderer respectively;
		// adding them here keeps them alive for the lifetime of this node.
		AddChild(_automapController.Viewport);
		AddChild(_statusBarRenderer.Viewport);

		// --- Compositor SubViewport: 320×200 (mode 13h) ---
		// Samples the two inner viewports via TextureRect and composites them.
		_compositorViewport = new SubViewport
		{
			Name = "WristwatchCompositorViewport",
			Size = new Vector2I(320, 200),
			Disable3D = true,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
		};
		AddChild(_compositorViewport);

		// Automap occupies the top 320×160
		_compositorViewport.AddChild(new TextureRect
		{
			Name = "AutomapRect",
			Texture = _automapController.ViewportTexture,
			Position = Vector2.Zero,
			Size = new Vector2(AutomapRenderer.ViewWidth, AutomapRenderer.ViewHeight),
			StretchMode = TextureRect.StretchModeEnum.Scale,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
		});

		// Status bar occupies the bottom 320×40
		_compositorViewport.AddChild(new TextureRect
		{
			Name = "StatusBarRect",
			Texture = _statusBarRenderer.ViewportTexture,
			Position = new Vector2(0f, AutomapRenderer.ViewHeight),
			Size = new Vector2(320f, 40f),
			StretchMode = TextureRect.StretchModeEnum.Scale,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
		});

		// --- World-space screen quad ---
		_screenMaterial = new StandardMaterial3D
		{
			AlbedoTexture = _compositorViewport.GetTexture(),
			// Unshaded so the retro pixel art is not affected by scene lighting
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			// DEBUG: always opaque to verify geometry/material; restore fade-in before shipping
			AlbedoColor = new Color(1f, 1f, 1f, 1f),
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
		// DEBUG: wrist-raise opacity disabled — always opaque for diagnostics.
		// Restore before shipping.
		return;

		// Wrist-raise detection: the quad faces its own world-space +Z axis.
		// When the palm is raised toward the head that axis points toward the camera.
		Vector3 screenNormal = _screenMesh.GlobalBasis.Z;
		Vector3 toCamera = (_camera.GlobalPosition - _screenMesh.GlobalPosition).Normalized();
		float dot = screenNormal.Dot(toCamera);

		float alpha = Mathf.Clamp(
			Mathf.InverseLerp(WristRaiseShowDot, WristRaiseFullDot, dot),
			0f, 1f);

		_screenMaterial.AlbedoColor = new Color(1f, 1f, 1f, alpha);
	}
}
