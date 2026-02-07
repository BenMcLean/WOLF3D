using Godot;
using System;
using BenMcLean.Wolf3D.Simulator;

namespace BenMcLean.Wolf3D.VR;

/// <summary>
/// Full-screen color overlay for flash and fade effects.
/// WL_PLAY.C:InitRedShifts - bonus flash shifts palette toward (R=64,G=62,B=0) in VGA 6-bit,
/// which is yellow (0xFFF800 in 8-bit RGB), not white despite the "whiteshifts" variable name.
/// Uses a CanvasLayer with ColorRect to guarantee the overlay covers everything on screen,
/// regardless of 3D geometry depth.
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

	// Fade state (for scene transitions, not driven by simulator events)
	private float fadeAlpha;
	private float fadeTargetAlpha;
	private float fadeRate; // Alpha units per second
	private Color fadeColor;

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
	/// Subscribe to screen flash events from the SimulatorController.
	/// </summary>
	public void Subscribe(SimulatorController controller)
	{
		if (controller == null)
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

	/// <summary>
	/// Initiates a fade-to-black effect over the specified duration.
	/// Used for scene transitions (elevator activation, death, etc.).
	/// </summary>
	/// <param name="durationSeconds">Duration of the fade in seconds</param>
	public void FadeToBlack(float durationSeconds)
	{
		fadeColor = new Color(0f, 0f, 0f);
		fadeTargetAlpha = 1f;
		fadeRate = (durationSeconds > 0) ? (1f / durationSeconds) : 1f;
		UpdateOverlay();
	}

	/// <summary>
	/// Initiates a fade-from-black effect over the specified duration.
	/// Used after scene transitions to reveal the new scene.
	/// </summary>
	/// <param name="durationSeconds">Duration of the fade in seconds</param>
	public void FadeFromBlack(float durationSeconds)
	{
		fadeColor = new Color(0f, 0f, 0f);
		fadeAlpha = 1f;
		fadeTargetAlpha = 0f;
		fadeRate = (durationSeconds > 0) ? (1f / durationSeconds) : 1f;
		UpdateOverlay();
	}

	public override void _Process(double delta)
	{
		bool needsUpdate = false;
		float dt = (float)delta;

		// Decay flash alpha
		if (flashAlpha > 0f)
		{
			flashAlpha -= flashDecayRate * dt;
			if (flashAlpha < 0f)
				flashAlpha = 0f;
			needsUpdate = true;
		}

		// Animate fade alpha toward target
		if (fadeAlpha != fadeTargetAlpha)
		{
			float step = fadeRate * dt;
			if (fadeTargetAlpha > fadeAlpha)
			{
				fadeAlpha += step;
				if (fadeAlpha >= fadeTargetAlpha)
					fadeAlpha = fadeTargetAlpha;
			}
			else
			{
				fadeAlpha -= step;
				if (fadeAlpha <= fadeTargetAlpha)
					fadeAlpha = fadeTargetAlpha;
			}
			needsUpdate = true;
		}

		if (needsUpdate)
			UpdateOverlay();
	}

	private void UpdateOverlay()
	{
		// Combine flash and fade: use whichever alpha is higher
		float combinedAlpha = Mathf.Max(flashAlpha, fadeAlpha);

		if (combinedAlpha <= 0f)
		{
			colorRect.Visible = false;
			return;
		}

		colorRect.Visible = true;
		// Use flash color for flash effects, fade color for fade effects
		Color displayColor = (flashAlpha >= fadeAlpha)
			? new Color(flashColor.R, flashColor.G, flashColor.B, combinedAlpha)
			: new Color(fadeColor.R, fadeColor.G, fadeColor.B, combinedAlpha);

		colorRect.Color = displayColor;
	}
}
