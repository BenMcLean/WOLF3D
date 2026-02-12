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
	public void FadeToBlack(float duration)
	{
		targetAlpha = 1f;
		rate = (duration > 0f) ? (1f / duration) : 1f;
		UpdateVisuals();
	}

	/// <summary>
	/// Starts a fade from black over the specified duration.
	/// Assumes screen is already fully black (alpha = 1).
	/// </summary>
	public void FadeFromBlack(float duration)
	{
		alpha = 1f;
		targetAlpha = 0f;
		rate = (duration > 0f) ? (1f / duration) : 1f;
		UpdateVisuals();
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
		if (alpha <= 0f)
		{
			colorRect.Visible = false;
			return;
		}

		colorRect.Visible = true;
		colorRect.Color = new Color(0f, 0f, 0f, alpha);
	}
}
