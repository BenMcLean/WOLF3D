using System;
using Godot;

namespace BenMcLean.Wolf3D.VR;

/// <summary>
/// Full-screen black overlay for scene transition fading.
/// VW_FadeOut/VW_FadeIn from WL_DRAW.C - 30 steps at 70Hz (~0.4286 seconds).
/// Flatscreen: CanvasLayer 101 (above ScreenFlashOverlay at 100) with a full-screen ColorRect.
/// VR: StandardMaterial3D sphere parented to XRCamera3D, seen from inside (CullMode.Front,
/// NoDepthTest) so it renders over the scene in both headset eyes via Godot's stereo pipeline.
/// ProcessMode is Always so it continues animating while the scene tree is paused.
/// </summary>
public partial class ScreenFadeOverlay : Node
{
	private CanvasLayer canvasLayer;
	private ColorRect colorRect;

	private float alpha;
	private float targetAlpha;
	private float rate; // Alpha units per second

	// VR fade: veil sphere parented to the XRCamera3D so it is always at the camera origin.
	// StandardMaterial3D with CullMode.Front renders the sphere's inside faces in both eyes.
	// NoDepthTest ensures it renders over the entire scene regardless of depth.
	// CanvasLayer handles flatscreen and the VR mirror window.
	private Camera3D _vrCamera;
	private MeshInstance3D _vrFadeMesh;
	private StandardMaterial3D _vrFadeMaterial;

	/// <summary>
	/// True while a fade animation is in progress.
	/// </summary>
	public bool IsFading => alpha != targetAlpha;

	/// <summary>
	/// Fired when fade-to-black reaches full black (alpha = 1).
	/// </summary>
	public event Action FadeOutComplete;

	/// <summary>
	/// Fired when fade-from-black reaches transparent (alpha = 0).
	/// </summary>
	public event Action FadeInComplete;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;

		canvasLayer = new CanvasLayer
		{
			Name = "ScreenFadeCanvas",
			Layer = 101, // Above ScreenFlashOverlay (100)
		};
		AddChild(canvasLayer);

		colorRect = new ColorRect
		{
			Name = "ScreenFadeRect",
			Color = new Color(0f, 0f, 0f, 0f),
			AnchorLeft = 0f,
			AnchorTop = 0f,
			AnchorRight = 1f,
			AnchorBottom = 1f,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = false,
		};
		canvasLayer.AddChild(colorRect);
	}

	/// <summary>
	/// Starts a fade to black over the specified duration.
	/// </summary>
	public void FadeToBlack(float duration = (float)Shared.Constants.FadeDuration)
	{
		targetAlpha = 1f;
		rate = (duration > 0f) ? (1f / duration) : 1f;
		UpdateVisuals();
	}

	/// <summary>
	/// Starts a fade from black over the specified duration.
	/// Assumes screen is already fully black (alpha = 1).
	/// </summary>
	public void FadeFromBlack(float duration = (float)Shared.Constants.FadeDuration)
	{
		alpha = 1f;
		targetAlpha = 0f;
		rate = (duration > 0f) ? (1f / duration) : 1f;
		UpdateVisuals();
	}

	/// <summary>
	/// Instantly clears the overlay to fully transparent without firing FadeInComplete.
	/// Used when transitioning into a scene that already has a black background (SkipFade),
	/// so the overlay does not permanently obscure the scene's content.
	/// </summary>
	public void SetTransparent()
	{
		alpha = 0f;
		targetAlpha = 0f;
		UpdateVisuals();
	}

	/// <summary>
	/// Attaches a veil sphere to the given VR camera so fades appear in the headset.
	/// The sphere is a child of the camera so it is always centered on the viewer.
	/// StandardMaterial3D with CullMode.Front and NoDepthTest is used so the inside
	/// faces render over the entire scene in both eyes using Godot's stereo pipeline.
	/// The material is created once and reused across camera changes.
	/// Call after each room transition; pass null to detach.
	/// </summary>
	public void SetVRCamera(Camera3D camera)
	{
		_vrCamera = camera;
		_vrFadeMesh = null; // Old mesh was freed with old camera

		if (camera is null)
			return;

		// Create material once; it persists across camera changes because
		// ScreenFadeOverlay holds the reference.
		// CullMode.Front renders back faces = inside of the sphere is visible.
		// NoDepthTest renders over the entire scene regardless of depth.
		// Unshaded so lighting does not affect the black color.
		_vrFadeMaterial ??= new StandardMaterial3D
		{
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			CullMode = BaseMaterial3D.CullModeEnum.Front,
			NoDepthTest = true,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = new Color(0f, 0f, 0f, 0f),
		};

		_vrFadeMesh = new MeshInstance3D
		{
			Name = "VRFadeSphere",
			// Radius 0.5m: large enough to be well past the near clip plane (~0.05m),
			// small enough to avoid precision issues. Size irrelevant with NoDepthTest.
			Mesh = new SphereMesh { Radius = 0.5f, Height = 1f },
			MaterialOverride = _vrFadeMaterial,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			// Always visible — material alpha=0 means transparent.
			// Toggling Visible on/off has multi-frame latency in the OpenXR compositor,
			// which would cause the level to show unobscured at the start of every fade-to-black.
			Visible = true,
		};
		camera.AddChild(_vrFadeMesh);

		// Sync current fade state immediately in case a fade is mid-animation
		_vrFadeMaterial.AlbedoColor = new Color(0f, 0f, 0f, alpha);
	}

	public override void _Process(double delta)
	{
		if (alpha == targetAlpha)
			return;

		float dt = (float)delta;
		float step = rate * dt;

		if (targetAlpha > alpha)
		{
			alpha += step;
			if (alpha >= targetAlpha)
			{
				alpha = targetAlpha;
				UpdateVisuals();
				FadeOutComplete?.Invoke();
				return;
			}
		}
		else
		{
			alpha -= step;
			if (alpha <= targetAlpha)
			{
				alpha = targetAlpha;
				UpdateVisuals();
				FadeInComplete?.Invoke();
				return;
			}
		}

		UpdateVisuals();
	}

	private void UpdateVisuals()
	{
		// Flatscreen: CanvasLayer ColorRect (also covers the VR mirror window)
		if (alpha <= 0f)
		{
			colorRect.Visible = false;
		}
		else
		{
			colorRect.Visible = true;
			colorRect.Color = new Color(0f, 0f, 0f, alpha);
		}

		// VR: veil sphere parented to XRCamera3D, visible in both headset eyes.
		// Mesh stays Visible=true always; material alpha controls opacity.
		if (_vrFadeMesh is not null && GodotObject.IsInstanceValid(_vrFadeMesh))
		{
			_vrFadeMaterial.AlbedoColor = new Color(0f, 0f, 0f, alpha);
		}
		else if (_vrFadeMesh is not null)
		{
			_vrFadeMesh = null; // Freed with old camera
		}
	}
}
