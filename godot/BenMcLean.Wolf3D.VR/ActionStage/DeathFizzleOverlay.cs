using Godot;
using System;
using BenMcLean.Wolf3D.Simulator;

namespace BenMcLean.Wolf3D.VR.ActionStage;

/// <summary>
/// Full-screen death effect: pseudo-random pixel-by-pixel transition to solid color on player death.
/// ID_VH.C:FizzleFade — uses a 17-bit Galois LFSR to reveal pixels in pseudo-random order.
/// WL_GAME.C:Died: VW_Bar fills view with VGA color 4 (red), then FizzleFade reveals it over 70 tics.
///
/// VR: veil quad parented to XRCamera3D. Uses skip_vertex_transform + NDC POSITION trick
/// (same as ScreenFlashOverlay) so the quad covers the full clip-space rectangle for each eye
/// independently in stereoscopic rendering. The mesh is ALWAYS VISIBLE — transparency is
/// controlled solely by the progress shader parameter (progress=0 → all pixels ALPHA=0 →
/// fully transparent) to avoid the multi-frame OpenXR compositor latency that occurs when
/// toggling Visible on/off (see ScreenFadeOverlay for documentation of that issue).
/// Render order vs other camera-child overlays is controlled by SortingOffset + RenderPriority:
///   ScreenFlashOverlay quad  SortingOffset=-2, RenderPriority=0 → behind (first)
///   DeathFizzleOverlay quad  SortingOffset=-1, RenderPriority=1 → middle
///   ScreenFadeOverlay sphere SortingOffset= 0, RenderPriority=2 → in front (last)
/// Godot 4: HIGHER SortingOffset/RenderPriority = rendered LAST = in front.
/// Both mechanisms enforce the same order for belt-and-suspenders reliability.
/// This ensures the black fade-to-black correctly covers the red fizzle screen.
///
/// Flatscreen: CanvasLayer ColorRect with canvas_item shader — same UV-based fizzle texture lookup.
/// Hidden when inactive (no compositor latency concern for 2D elements; saves a draw call).
/// Stays fully red after animation completes so ScreenFadeOverlay can fade it to black.
/// </summary>
public partial class DeathFizzleOverlay : Node
{
	// FizzleFade duration: 70 frames at 70 tics/sec = 1 second
	// WL_GAME.C:FizzleFade(bufferofs, displayofs+screenofs, viewwidth, viewheight, 70, false)
	private const double FizzleDuration = 1.0;

	// Fizzle texture resolution matches original Wolf3D viewport size
	// ID_VH.C:FizzleFade width=320, height=200
	private const int FizzleWidth = 320;
	private const int FizzleHeight = 200;

	// Shared fizzle order texture generated once per application lifetime
	private static ImageTexture _sharedFizzleTexture;

	private ShaderMaterial _flatscreenMaterial;
	private CanvasLayer _canvasLayer;
	private ColorRect _colorRect;

	// VR fizzle: veil quad parented to the XRCamera3D, always visible.
	// Transparency controlled by the progress shader parameter (0.0 = fully transparent).
	// Never toggle Visible — see class summary for the OpenXR compositor latency explanation.
	private MeshInstance3D _vrFizzleMesh;
	private ShaderMaterial _vrFizzleMaterial;

	private bool _active;
	private double _progress; // 0.0 = fully transparent, 1.0 = fully covered
	private Color _fizzleColor;

	/// <summary>
	/// Fired when the fizzle animation completes (screen fully covered with color).
	/// ActionRoom listens to this to set PendingDeathFadeOut.
	/// </summary>
	public event Action FizzleComplete;

	public override void _Ready()
	{
		ImageTexture tex = GetOrCreateFizzleTexture();

		_flatscreenMaterial = new ShaderMaterial
		{
			Shader = new Shader
			{
				// canvas_item shader: per-pixel reveal based on precomputed fizzle order.
				// Pixels with order < progress become opaque; others are transparent.
				// UV goes (0,0)→(1,1) across the full-screen ColorRect, stretching the
				// 320×200 fizzle texture to screen with filter_nearest for blocky pixels.
				Code = """
shader_type canvas_item;

uniform sampler2D fizzle_texture : filter_nearest;
uniform float progress : hint_range(0.0, 1.0) = 0.0;
uniform vec4 fizzle_color : source_color = vec4(1.0, 0.0, 0.0, 1.0);

void fragment() {
	float order = texture(fizzle_texture, UV).r;
	COLOR = order < progress ? fizzle_color : vec4(0.0, 0.0, 0.0, 0.0);
}
""",
			},
		};
		_flatscreenMaterial.SetShaderParameter("fizzle_texture", tex);
		_flatscreenMaterial.SetShaderParameter("fizzle_color", new Color(1f, 0f, 0f, 1f));
		_flatscreenMaterial.SetShaderParameter("progress", 0f);

		_canvasLayer = new CanvasLayer
		{
			Name = "DeathFizzleCanvas",
			Layer = 100,
		};
		AddChild(_canvasLayer);

		// Flatscreen: hidden when inactive (toggling is safe for 2D; saves a draw call).
		_colorRect = new ColorRect
		{
			Name = "DeathFizzleRect",
			AnchorLeft = 0f,
			AnchorTop = 0f,
			AnchorRight = 1f,
			AnchorBottom = 1f,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Material = _flatscreenMaterial,
			Visible = false,
		};
		_canvasLayer.AddChild(_colorRect);
	}

	/// <summary>
	/// Attaches a veil quad to the given VR camera so the fizzle effect appears in the headset.
	///
	/// The quad is created as a CHILD OF THE CAMERA so it is always inside the view frustum.
	/// Parenting to a world-space node causes frustum culling.
	///
	/// The vertex shader uses skip_vertex_transform + NDC POSITION trick:
	///   POSITION = vec4(2.0 * UV - 1.0, 0.0, 1.0)
	/// This maps the (1×1) QuadMesh UV range to clip-space [-1,1]×[-1,1], bypassing the
	/// model-view-projection transform. In stereo XR each eye renders its own pass with
	/// its own clip space, so the quad correctly covers the full viewport for each eye.
	/// depth_test_disabled + depth_draw_never ensure it renders over the entire scene.
	///
	/// The mesh is ALWAYS VISIBLE after creation. progress=0.0 makes all pixels
	/// transparent (order ∈ [0,1) so order &lt; 0.0 is always false), avoiding the
	/// multi-frame OpenXR compositor latency that happens when toggling Visible.
	///
	/// Call after each room transition; pass null to detach.
	/// </summary>
	public void SetVRCamera(Camera3D camera)
	{
		_vrFizzleMesh = null; // Old mesh was freed with old camera

		if (camera is null)
			return;

		_vrFizzleMaterial ??= new ShaderMaterial
		{
			// RenderPriority 1: renders in front of ScreenFlashOverlay (0) but behind ScreenFadeOverlay (2).
			// Belt-and-suspenders with SortingOffset. See class summary for full overlay render order.
			RenderPriority = 1,
			Shader = new Shader
			{
				// spatial shader: same NDC full-screen quad trick as ScreenFlashOverlay.
				// blend_mix with per-pixel ALPHA handles transparent (scene shows through)
				// and opaque (solid fizzle color) pixels in the same draw call.
				// depth_draw_never + depth_test_disabled: renders over the entire scene
				// without writing to or testing the depth buffer.
				// cull_disabled: quad has no concept of front/back face.
				// unshaded: fizzle color is not affected by scene lighting.
				Code = """
shader_type spatial;
render_mode blend_mix, skip_vertex_transform, cull_disabled, unshaded, depth_draw_never, depth_test_disabled;

uniform sampler2D fizzle_texture : filter_nearest;
uniform float progress : hint_range(-1.0, 1.0) = 0.0;
uniform vec4 fizzle_color : source_color = vec4(1.0, 0.0, 0.0, 1.0);

void vertex() {
	// Map QuadMesh UV (0,0)→(1,1) to clip space (-1,-1)→(1,1).
	// Bypasses model-view-projection; covers the full viewport for each stereo eye.
	// w=1.0 means no perspective division; z=0.0 places at near clip plane.
	POSITION = vec4(2.0 * UV - 1.0, 0.0, 1.0);
}

void fragment() {
	float order = texture(fizzle_texture, UV).r;
	if (order < progress) {
		ALBEDO = fizzle_color.rgb;
		ALPHA = fizzle_color.a;
	} else {
		// Transparent: scene shows through via blend_mix (ALPHA=0 → blend factor 0).
		ALBEDO = vec3(0.0);
		ALPHA = 0.0;
	}
}
""",
			},
		};

		_vrFizzleMaterial.SetShaderParameter("fizzle_texture", GetOrCreateFizzleTexture());
		_vrFizzleMaterial.SetShaderParameter("fizzle_color", new Color(1f, 0f, 0f, 1f));
		// progress=0.0: order ∈ [0,1) so order < 0.0 is always false → fully transparent.
		_vrFizzleMaterial.SetShaderParameter("progress", 0f);

		_vrFizzleMesh = new MeshInstance3D
		{
			Name = "VRFizzleQuad",
			Mesh = new QuadMesh { Size = new Vector2(1f, 1f) },
			MaterialOverride = _vrFizzleMaterial,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			// Always visible — progress=0 makes all pixels transparent.
			// Toggling Visible on/off has multi-frame latency in the OpenXR compositor.
			// See ScreenFadeOverlay.SetVRCamera for documentation of this constraint.
			Visible = true,
			// SortingOffset -1: must render in front of ScreenFlashOverlay (-2) but behind
			// ScreenFadeOverlay (0). See class summary for full overlay render order.
			SortingOffset = -1f,
		};
		camera.AddChild(_vrFizzleMesh);

		// Sync current fizzle state immediately in case TriggerFizzle was called before
		// SetVRCamera (unlikely in practice but consistent with ScreenFlashOverlay pattern).
		_vrFizzleMaterial.SetShaderParameter("fizzle_color", _fizzleColor.A > 0f ? _fizzleColor : new Color(1f, 0f, 0f, 1f));
		_vrFizzleMaterial.SetShaderParameter("progress", (float)_progress);
	}

	/// <summary>
	/// Starts the fizzle-to-color animation.
	/// WL_GAME.C:Died — triggered by PlayerDied event with the configured VGA palette color.
	/// </summary>
	/// <param name="color">Fill color. Wolf3D uses VGA palette color 4 (red).</param>
	public void TriggerFizzle(Color color)
	{
		_fizzleColor = color;
		_progress = 0.0;
		_active = true;

		_flatscreenMaterial.SetShaderParameter("fizzle_color", color);
		_flatscreenMaterial.SetShaderParameter("progress", 0f);
		_colorRect.Visible = true;

		if (_vrFizzleMesh is not null && GodotObject.IsInstanceValid(_vrFizzleMesh))
		{
			// No Visible toggle — mesh is always visible. Just update shader parameters.
			_vrFizzleMaterial.SetShaderParameter("fizzle_color", color);
			_vrFizzleMaterial.SetShaderParameter("progress", 0f);
		}
	}

	public override void _ExitTree()
	{
		// _vrFizzleMesh is parented to the camera, not to this node, so it is not freed
		// automatically when this overlay is removed. Free it explicitly to avoid an orphan.
		if (_vrFizzleMesh is not null && GodotObject.IsInstanceValid(_vrFizzleMesh))
			_vrFizzleMesh.QueueFree();
		_vrFizzleMesh = null;
	}

	public override void _Process(double delta)
	{
		if (!_active)
			return;

		_progress += delta / FizzleDuration;
		if (_progress >= 1.0)
		{
			_progress = 1.0;
			_active = false;
			UpdateShaderProgress();
			FizzleComplete?.Invoke();
			return;
		}

		UpdateShaderProgress();
	}

	private void UpdateShaderProgress()
	{
		_flatscreenMaterial.SetShaderParameter("progress", (float)_progress);

		if (_vrFizzleMesh is not null && GodotObject.IsInstanceValid(_vrFizzleMesh))
			_vrFizzleMaterial.SetShaderParameter("progress", (float)_progress);
		else if (_vrFizzleMesh is not null)
			_vrFizzleMesh = null; // Freed with old camera
	}

	/// <summary>
	/// Generates the fizzle order texture using the LFSR from ID_VH.C:FizzleFade.
	/// Each pixel stores a normalized float in [0, 1) indicating when it becomes visible
	/// as progress advances from 0 to 1. Generated once per application lifetime.
	///
	/// Algorithm: 17-bit Galois right-shift LFSR, period 2^17−1 = 131071.
	/// Feedback polynomial taps at bits 13 and 16 (XOR mask 0x00012000).
	/// Coordinates extracted before advancing: y = (bits 0-7) − 1, x = bits 8-16.
	/// Of the 131071 states, exactly 320×200 = 64000 map to valid screen pixels.
	/// Each valid pixel is visited exactly once per cycle.
	/// </summary>
	private static ImageTexture GetOrCreateFizzleTexture()
	{
		if (_sharedFizzleTexture is not null)
			return _sharedFizzleTexture;

		int[] order = new int[FizzleWidth * FizzleHeight];
		Array.Fill(order, -1);

		// ID_VH.C:FizzleFade LFSR loop
		uint rndval = 1;
		int seqIdx = 0;
		do
		{
			// Extract x,y from current state BEFORE advancing (matches original assembly order)
			// ID_VH.C: y = low byte - 1, x = bits 8-16 (9 bits)
			int y = (int)(rndval & 0xFFu) - 1;
			int x = (int)((rndval >> 8) & 0x1FFu);

			// Advance: right-shift, XOR with 0x00012000 if carry bit was set
			bool carry = (rndval & 1u) != 0u;
			rndval >>= 1;
			if (carry)
				rndval ^= 0x00012000u;

			if (x < FizzleWidth && y >= 0 && y < FizzleHeight)
				order[y * FizzleWidth + x] = seqIdx++;
		}
		while (rndval != 1);

		// Normalize to [0, 1): divide by seqIdx (= 64000) so:
		//   first pixel  → 0/64000 = 0.0
		//   last pixel   → 63999/64000 ≈ 0.99998 < 1.0
		// Guarantees all pixels are revealed when progress reaches exactly 1.0.
		Image image = Image.CreateEmpty(FizzleWidth, FizzleHeight, false, Image.Format.Rf);
		for (int i = 0; i < FizzleWidth * FizzleHeight; i++)
		{
			float value = order[i] >= 0 ? (float)order[i] / seqIdx : 0f;
			image.SetPixel(i % FizzleWidth, i / FizzleWidth, new Color(value, 0f, 0f, 1f));
		}

		_sharedFizzleTexture = ImageTexture.CreateFromImage(image);
		return _sharedFizzleTexture;
	}
}
