using Godot;
using System;
using BenMcLean.Wolf3D.Simulator;

namespace BenMcLean.Wolf3D.VR;

/// <summary>
/// Full-screen color overlay for gameplay flash effects (damage, bonus pickups).
/// WL_PLAY.C:InitRedShifts - bonus flash shifts palette toward (R=64,G=62,B=0) in VGA 6-bit,
/// which is yellow (0xFFF800 in 8-bit RGB), not white despite the "whiteshifts" variable name.
/// Uses a CanvasLayer with ColorRect to guarantee the overlay covers everything on screen,
/// regardless of 3D geometry depth.
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
		if (flashAlpha <= 0f)
		{
			colorRect.Visible = false;
			return;
		}

		colorRect.Visible = true;
		colorRect.Color = new Color(flashColor.R, flashColor.G, flashColor.B, flashAlpha);
	}
}
