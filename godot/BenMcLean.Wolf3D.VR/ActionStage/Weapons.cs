using Godot;
using System;
using System.Collections.Generic;
using BenMcLean.Wolf3D.Simulator;
using BenMcLean.Wolf3D.Shared;

namespace BenMcLean.Wolf3D.VR.ActionStage;

/// <summary>
/// Manages player weapon rendering at bottom of screen (like original Wolf3D HUD).
/// For initial implementation, shows weapon sprite as 2D overlay on camera.
/// Later can be enhanced for VR hand tracking.
/// </summary>
public partial class Weapons : Node3D
{
	// Weapon sprite display (attached to camera) - uses MeshInstance3D like actors
	private MeshInstance3D weaponSprite;
	// Sprite materials from VRAssetManager
	private readonly IReadOnlyDictionary<ushort, StandardMaterial3D> spriteMaterials;
	// Camera reference for positioning weapon sprite
	private readonly Node3D camera;
	// When true, skips creating the camera-attached sprite (VR uses WeaponHandMesh on controllers instead)
	private readonly bool disableVisual;
	// Current weapon slot being displayed (0 = primary/left, 1 = right)
	private int currentSlot = 0;
	// Simulator reference for event subscription
	private Simulator.Simulator simulator;

	/// <summary>
	/// Creates weapon rendering system.
	/// </summary>
	/// <param name="spriteMaterials">Dictionary of sprite materials from VRAssetManager.SpriteMaterials</param>
	/// <param name="camera">Camera node to attach weapon sprite to</param>
	/// <param name="disableVisual">
	/// When true, skips creating the camera-attached sprite.
	/// Use in VR mode where WeaponHandMesh on controllers provides the visual.
	/// Sound (WeaponFired) is still handled regardless.
	/// </param>
	public Weapons(
		IReadOnlyDictionary<ushort, StandardMaterial3D> spriteMaterials,
		Node3D camera,
		bool disableVisual = false)
	{
		this.spriteMaterials = spriteMaterials ?? throw new ArgumentNullException(nameof(spriteMaterials));
		this.camera = camera ?? throw new ArgumentNullException(nameof(camera));
		this.disableVisual = disableVisual;
	}

	public override void _Ready()
	{
		if (disableVisual)
			return;

		// Create weapon sprite node using MeshInstance3D (same as actors)
		// Position it at bottom-center of camera view (like original Wolf3D)
		weaponSprite = new MeshInstance3D
		{
			Name = "WeaponSprite",
			Mesh = Constants.WallMesh,  // Same quad mesh as actors use
			Visible = false,  // Start hidden until weapon equipped
		};

		// Attach to camera so it moves with view
		camera.AddChild(weaponSprite);

		// Position relative to camera - bottom center of view like original Wolf3D
		weaponSprite.Position = new Vector3(0f, 0f, -2f);

		// No rotation needed - quad faces correct direction by default
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
		if (simulator != null)
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

		// TODO: Show muzzle flash
	}

	/// <summary>
	/// Update the displayed weapon sprite material.
	/// Uses MaterialOverride just like actors do.
	/// </summary>
	/// <param name="shape">Sprite page number</param>
	private void UpdateWeaponSprite(ushort shape)
	{
		if (weaponSprite == null)
			return;
		// Get material for this sprite (same as actors do)
		if (spriteMaterials.TryGetValue(shape, out StandardMaterial3D material))
		{
			weaponSprite.MaterialOverride = material;
			weaponSprite.Visible = true;
		}
		else
			weaponSprite.Visible = false;
	}

	public override void _ExitTree()
	{
		Unsubscribe();
		base._ExitTree();
	}
}
