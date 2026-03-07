using System;
using Godot;

namespace BenMcLean.Wolf3D.VR;

/// <summary>
/// Full-screen black overlay for scene transition fading.
/// VW_FadeOut/VW_FadeIn from WL_DRAW.C - 30 steps at 70Hz (~0.4286 seconds).
/// Uses CanvasLayer 101 to render above ScreenFlashOverlay (layer 100).
/// ProcessMode is Always so it continues animating while the scene tree is paused.
/// </summary>
public partial class ScreenFadeOverlay : Node
{
	private CanvasLayer canvasLayer;
	private ColorRect colorRect;

	private float alpha;
	private float targetAlpha;
	private float rate; // Alpha units per second

	// VR fade: veil quad parented to the XRCamera3D so it is always inside the view frustum.
	// Vertex shader outputs POSITION in NDC via UV, bypassing projection and near/far clipping.
	// CanvasLayer handles flatscreen and the VR mirror window.
	private Camera3D _vrCamera;
	private MeshInstance3D _vrFadeMesh;
	private ShaderMaterial _vrFadeMaterial;

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
	/// Attaches a veil quad to the given VR camera so fades appear in the headset.
	/// The quad is a child of the camera (not of this node) so it always stays inside
	/// the view frustum — parenting to a world-space node causes frustum culling.
	/// The shader material is created once and reused across camera changes.
	/// Call after each room transition; pass null to detach.
	/// </summary>
	public void SetVRCamera(Camera3D camera)
	{
		_vrCamera = camera;
		_vrFadeMesh = null; // Old mesh was freed with old camera

		if (camera == null)
			return;

		// Create shader material once; it persists across camera changes because
		// ScreenFadeOverlay holds the reference.
		_vrFadeMaterial ??= new ShaderMaterial
		{
			Shader = new Shader
			{
				// Adapted from Godot 3 FadeCamera.cs:
				// skip_vertex_transform lets the vertex shader set POSITION directly in NDC.
				// UV-based position (2*UV-1) maps the (1,1) quad to full clip space [-1,1].
				// Parenting to the camera keeps the mesh inside the view frustum.
				Code = """
shader_type spatial;
render_mode blend_mix, skip_vertex_transform, cull_disabled, unshaded, depth_draw_never, depth_test_disabled;

uniform vec4 color : source_color;

void vertex() {
	POSITION = vec4(2.0 * UV - 1.0, 0.0, 1.0);
}

void fragment() {
	ALBEDO = color.rgb;
	ALPHA = color.a;
}
""",
			},
		};

		_vrFadeMesh = new MeshInstance3D
		{
			Name = "VRFadeQuad",
			Mesh = new QuadMesh { Size = new Vector2(1f, 1f) },
			MaterialOverride = _vrFadeMaterial,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Visible = alpha > 0f,
		};
		camera.AddChild(_vrFadeMesh);

		// Sync current fade state immediately in case a fade is mid-animation
		_vrFadeMaterial.SetShaderParameter("color", new Color(0f, 0f, 0f, alpha));
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

		// VR: veil quad parented to XRCamera3D, visible in both headset eyes
		if (_vrFadeMesh != null && GodotObject.IsInstanceValid(_vrFadeMesh))
		{
			_vrFadeMesh.Visible = alpha > 0f;
			_vrFadeMaterial.SetShaderParameter("color", new Color(0f, 0f, 0f, alpha));
		}
		else if (_vrFadeMesh != null)
		{
			_vrFadeMesh = null; // Freed with old camera
		}
	}
}
