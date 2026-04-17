using Godot;
using System;
using BenMcLean.Wolf3D.Simulator;

namespace BenMcLean.Wolf3D.VR.ActionStage;

/// <summary>
/// Full-screen color overlay for gameplay flash effects (damage, bonus pickups).
/// WL_PLAY.C:InitRedShifts - bonus flash shifts palette toward (R=64,G=62,B=0) in VGA 6-bit,
/// which is yellow (0xFFF800 in 8-bit RGB), not white despite the "whiteshifts" variable name.
/// Uses a CanvasLayer with ColorRect for flatscreen, and a veil quad parented to the XRCamera3D
/// for VR (same pattern as ScreenFadeOverlay) so the effect appears in the headset.
/// Scene transition fading is handled separately by ScreenFadeOverlay (CanvasLayer 101).
/// </summary>
public partial class ScreenFlashOverlay : Node
{
	// WL_PLAY.C:InitRedShifts - max shift is NUMWHITESHIFTS/WHITESTEPS = 3/20 = 15%
	// This is the blend factor toward the target color at the strongest flash level
	private const float FlashStartAlpha = 0.15f;

	// Wolf3D tic rate for converting duration in tics to seconds
	private const double TicRate = 70.0;

	private CanvasLayer canvasLayer;
	private ColorRect colorRect;

	// Flash state
	private float flashAlpha;
	private float flashDecayRate; // Alpha units per second
	private Color flashColor;

	// VR flash: veil quad parented to the XRCamera3D so it is always inside the view frustum.
	// Vertex shader outputs POSITION in NDC via UV, bypassing projection and near/far clipping.
	// Parenting to the camera keeps the mesh inside the view frustum.
	private MeshInstance3D _vrFlashMesh;
	private ShaderMaterial _vrFlashMaterial;

	public override void _Ready()
	{
		// CanvasLayer on a high layer renders on top of everything (3D scene, other UI)
		canvasLayer = new CanvasLayer
		{
			Name = "ScreenFlashCanvas",
			Layer = 100, // Above all other layers
		};
		AddChild(canvasLayer);

		// ColorRect covers entire viewport
		colorRect = new ColorRect
		{
			Name = "ScreenFlashRect",
			Color = new Color(0f, 0f, 0f, 0f),
			// Anchor to fill entire screen
			AnchorLeft = 0f,
			AnchorTop = 0f,
			AnchorRight = 1f,
			AnchorBottom = 1f,
			// No mouse interaction - let clicks pass through
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = false,
		};
		canvasLayer.AddChild(colorRect);
	}

	/// <summary>
	/// Attaches a veil quad to the given VR camera so flash effects appear in the headset.
	/// The quad is a child of the camera (not of this node) so it always stays inside
	/// the view frustum — parenting to a world-space node causes frustum culling.
	/// Call after each room transition; pass null to detach.
	/// </summary>
	public void SetVRCamera(Camera3D camera)
	{
		_vrFlashMesh = null; // Old mesh was freed with old camera

		if (camera is null)
			return;

		// Create shader material once; it persists across camera changes because
		// ScreenFlashOverlay holds the reference.
		_vrFlashMaterial ??= new ShaderMaterial
		{
			Shader = new Shader
			{
				// Same pattern as ScreenFadeOverlay:
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

		_vrFlashMesh = new MeshInstance3D
		{
			Name = "VRFlashQuad",
			Mesh = new QuadMesh { Size = new Vector2(1f, 1f) },
			MaterialOverride = _vrFlashMaterial,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Visible = false,
			// SortingOffset -2: renders behind DeathFizzleOverlay (-1) and ScreenFadeOverlay (0).
			// Godot 4: HIGHER SortingOffset = rendered LAST = in front. See DeathFizzleOverlay.
			SortingOffset = -2f,
		};
		camera.AddChild(_vrFlashMesh);

		// Sync current flash state immediately in case a flash is mid-animation
		_vrFlashMaterial.SetShaderParameter("color", new Color(flashColor.R, flashColor.G, flashColor.B, flashAlpha));
		_vrFlashMesh.Visible = flashAlpha > 0f;
	}

	/// <summary>
	/// Subscribe to screen flash events from the SimulatorController.
	/// </summary>
	public void Subscribe(SimulatorController controller)
	{
		if (controller is null)
			throw new ArgumentNullException(nameof(controller));
		controller.ScreenFlash += OnScreenFlash;
	}

	private void OnScreenFlash(ScreenFlashEvent e)
	{
		// Extract RGB from 24-bit color
		float r = ((e.Color >> 16) & 0xFF) / 255f;
		float g = ((e.Color >> 8) & 0xFF) / 255f;
		float b = (e.Color & 0xFF) / 255f;

		flashColor = new Color(r, g, b);
		flashAlpha = FlashStartAlpha;

		// Calculate decay rate: alpha should reach 0 over the duration in tics
		double durationSeconds = e.Duration / TicRate;
		flashDecayRate = (durationSeconds > 0) ? (float)(FlashStartAlpha / durationSeconds) : FlashStartAlpha;

		UpdateOverlay();
	}

	public override void _Process(double delta)
	{
		if (flashAlpha <= 0f)
			return;

		flashAlpha -= flashDecayRate * (float)delta;
		if (flashAlpha < 0f)
			flashAlpha = 0f;

		UpdateOverlay();
	}

	private void UpdateOverlay()
	{
		// Flatscreen: CanvasLayer ColorRect (also covers the VR mirror window)
		if (flashAlpha <= 0f)
		{
			colorRect.Visible = false;
		}
		else
		{
			colorRect.Visible = true;
			colorRect.Color = new Color(flashColor.R, flashColor.G, flashColor.B, flashAlpha);
		}

		// VR: veil quad parented to XRCamera3D, visible in both headset eyes
		if (_vrFlashMesh is not null && GodotObject.IsInstanceValid(_vrFlashMesh))
		{
			_vrFlashMesh.Visible = flashAlpha > 0f;
			_vrFlashMaterial.SetShaderParameter("color", new Color(flashColor.R, flashColor.G, flashColor.B, flashAlpha));
		}
		else if (_vrFlashMesh is not null)
		{
			_vrFlashMesh = null; // Freed with old camera
		}
	}
}
