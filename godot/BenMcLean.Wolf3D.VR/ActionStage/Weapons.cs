using Godot;
using System;
using System.Collections.Generic;
using BenMcLean.Wolf3D.Simulator;
using BenMcLean.Wolf3D.Shared;

namespace BenMcLean.Wolf3D.VR.ActionStage;

/// <summary>
/// Manages player weapon presentation state and weapon fire audio.
/// In flatscreen mode, the weapon is rendered as a HUD overlay.
/// In VR, controller-attached weapon visuals are handled elsewhere.
/// </summary>
/// <remarks>
/// Creates weapon rendering system.
/// </remarks>
/// <param name="spriteTextures">Dictionary of sprite textures from VRAssetManager.SpriteTextures.</param>
public partial class Weapons(IReadOnlyDictionary<ushort, Texture2D> spriteTextures) : Node3D
{
	private static readonly Vector2 HudWeaponBoxSize = new(192f, 120f),
		HudWeaponBoxPosition = new(-96f, -240f);
	// Sprite textures used by the flatscreen HUD representation.
	private readonly IReadOnlyDictionary<ushort, Texture2D> spriteTextures = spriteTextures ?? throw new ArgumentNullException(nameof(spriteTextures));
	private TextureRect hudWeaponSprite;
	private Control hudCanvas;
	private ushort? currentShape;
	// Current weapon slot being displayed (0 = primary/left, 1 = right)
	private int currentSlot = 0;
	// Simulator reference for event subscription
	private Simulator.Simulator simulator;
	/// <summary>
	/// Attaches the flatscreen weapon display to the HUD canvas.
	/// Safe to call after weapon events have already started arriving.
	/// </summary>
	public void AttachHud(Control canvas)
	{
		hudCanvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
		if (hudWeaponSprite is null)
		{
			hudWeaponSprite = new TextureRect
			{
				Name = "HudWeaponSprite",
				AnchorLeft = 0.5f,
				AnchorRight = 0.5f,
				AnchorTop = 1.0f,
				AnchorBottom = 1.0f,
				Position = HudWeaponBoxPosition,
				Size = HudWeaponBoxSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
				Visible = false,
				ZIndex = 20,
			};
			hudCanvas.AddChild(hudWeaponSprite);
		}
		if (currentShape.HasValue)
			UpdateWeaponSprite(currentShape.Value);
	}
	/// <summary>
	/// Subscribe to simulator weapon events.
	/// Call this after simulator is initialized.
	/// </summary>
	/// <param name="sim">Simulator instance</param>
	public void Subscribe(Simulator.Simulator sim)
	{
		simulator = sim ?? throw new ArgumentNullException(nameof(sim));
		// Subscribe to weapon events
		simulator.WeaponEquipped += OnWeaponEquipped;
		simulator.WeaponSpriteChanged += OnWeaponSpriteChanged;
		simulator.WeaponFired += OnWeaponFired;
	}
	/// <summary>
	/// Unsubscribe from simulator events.
	/// </summary>
	public void Unsubscribe()
	{
		if (simulator is not null)
		{
			simulator.WeaponEquipped -= OnWeaponEquipped;
			simulator.WeaponSpriteChanged -= OnWeaponSpriteChanged;
			simulator.WeaponFired -= OnWeaponFired;
		}
	}
	/// <summary>
	/// Handle weapon equipped event - show initial weapon sprite.
	/// </summary>
	private void OnWeaponEquipped(WeaponEquippedEvent evt)
	{
		// Only display primary weapon slot for now
		if (evt.SlotIndex != currentSlot)
			return;
		// Update weapon sprite
		UpdateWeaponSprite(evt.Shape);
	}
	/// <summary>
	/// Handle weapon sprite change event - update displayed sprite.
	/// </summary>
	private void OnWeaponSpriteChanged(WeaponSpriteChangedEvent evt)
	{
		// Only display primary weapon slot for now
		if (evt.SlotIndex != currentSlot)
			return;
		// Update weapon sprite
		UpdateWeaponSprite(evt.Shape);
	}
	/// <summary>
	/// Handle weapon fired event - plays weapon sound and could trigger muzzle flash.
	/// </summary>
	private void OnWeaponFired(WeaponFiredEvent evt)
	{
		// Play weapon fire sound for any slot
		if (!string.IsNullOrEmpty(evt.SoundName))
			EventBus.Emit(GameEvent.PlaySound, evt.SoundName);
	}
	/// <summary>
	/// Update the displayed weapon sprite material.
	/// Uses MaterialOverride just like actors do.
	/// </summary>
	/// <param name="shape">Sprite page number</param>
	private void UpdateWeaponSprite(ushort shape)
	{
		currentShape = shape;
		if (hudWeaponSprite is null)
			return;
		if (spriteTextures.TryGetValue(shape, out Texture2D texture))
		{
			hudWeaponSprite.Texture = texture;
			hudWeaponSprite.Visible = true;
		}
		else
			hudWeaponSprite.Visible = false;
	}
	public override void _ExitTree()
	{
		Unsubscribe();
		base._ExitTree();
	}
}
