using System;
using System.Collections.Generic;
using BenMcLean.Wolf3D.Simulator;
using Godot;

namespace BenMcLean.Wolf3D.VR.VR;

/// <summary>
/// A BoxMesh rendered with the WeaponSpriteVoxelDDA shader, intended to be attached
/// to a VR controller "hand". Subscribes to weapon events for one slot and swaps
/// the shader texture whenever the weapon sprite changes.
/// Each instance owns its own ShaderMaterial so the texture swap is isolated from
/// other hands or overlapping subscribers.
/// </summary>
/// <param name="spriteTextures">Sprite textures from VRAssetManager.SpriteTextures.</param>
/// <param name="slotIndex">Weapon slot index this hand displays (0 = left/primary, 1 = right).</param>
public partial class WeaponHandMesh(IReadOnlyDictionary<ushort, Texture2D> spriteTextures, int slotIndex) : Node3D
{
	private readonly IReadOnlyDictionary<ushort, Texture2D> _spriteTextures = spriteTextures ?? throw new ArgumentNullException(nameof(spriteTextures));
	private ShaderMaterial _material;
	private Simulator.Simulator _simulator;

	public override void _Ready()
	{
		Shader voxelShader = new() { Code = FileAccess.GetFileAsString("res://Resources/WeaponSpriteVoxelDDA.gdshader") };
		if (voxelShader is null)
		{
			GD.PrintErr("Warning: WeaponSpriteVoxelDDA.gdshader not found");
			return;
		}
		_material = new ShaderMaterial { Shader = voxelShader };
		_material.SetShaderParameter("resolution", 64);
		// Left hand (slot 1) mirrors the sprite horizontally: negate X scale.
		// Rotation cannot achieve a mirror (det=+1), but negative scale can (det=-1).
		// The shader's billboard uses length(MODEL_MATRIX[0]) so the billboard size is
		// unaffected by the sign, and cull_disabled means no backface issues.
		float xScale = slotIndex == 1 ? -Constants.WeaponWidth : Constants.WeaponWidth;
		MeshInstance3D mesh = new()
		{
			Name = "Mesh",
			Mesh = new QuadMesh { Size = Vector2.One },
			MaterialOverride = _material,
			RotationDegrees = new Vector3(90f, 0f, 0f),
			Scale = new Vector3(xScale, Constants.WeaponMetersPerPixel, Constants.WeaponHeight),
			// Shift up so the aim pose tracking point aligns with 75% down the sprite.
			// The sprite center is at the mesh origin; 75% down is 25% below center
			// (world -Y = model +Z after RotateX(90°)), so shifting up by 25% of
			// WeaponHeight moves that point to the origin.
			Position = new Vector3(0f, Constants.WeaponHeight * 0.25f, 0f),
		};
		AddChild(mesh);
		Visible = false;  // Hidden until a weapon is equipped in this slot
	}

	/// <summary>
	/// Subscribe to simulator weapon events for this hand's slot.
	/// Call this after the simulator is initialized, then call
	/// <see cref="Simulator.Simulator.EmitWeaponState"/> to set the initial sprite.
	/// </summary>
	public void Subscribe(Simulator.Simulator sim)
	{
		_simulator = sim ?? throw new ArgumentNullException(nameof(sim));
		sim.WeaponEquipped += OnWeaponEquipped;
		sim.WeaponSpriteChanged += OnWeaponSpriteChanged;
	}

	/// <summary>
	/// Display a fixed sprite texture without subscribing to a simulator.
	/// Use this for static contexts like the menu where no simulator is running.
	/// Must be called after the node has been added to the scene tree (_Ready has run).
	/// </summary>
	public void ShowTexture(ushort shape) => UpdateTexture(shape);

	/// <summary>Unsubscribe from simulator weapon events.</summary>
	public void Unsubscribe()
	{
		if (_simulator is not null)
		{
			_simulator.WeaponEquipped -= OnWeaponEquipped;
			_simulator.WeaponSpriteChanged -= OnWeaponSpriteChanged;
		}
	}

	private void OnWeaponEquipped(WeaponEquippedEvent evt)
	{
		if (evt.SlotIndex != slotIndex)
			return;
		UpdateTexture(evt.Shape);
	}

	private void OnWeaponSpriteChanged(WeaponSpriteChangedEvent evt)
	{
		if (evt.SlotIndex != slotIndex)
			return;
		UpdateTexture(evt.Shape);
	}

	private void UpdateTexture(ushort shape)
	{
		if (_material is null)
			return;
		if (_spriteTextures.TryGetValue(shape, out Texture2D texture))
		{
			_material.SetShaderParameter("sprite_texture", texture);
			Visible = true;
		}
		else
			Visible = false;
	}

	public override void _ExitTree()
	{
		Unsubscribe();
		base._ExitTree();
	}
}
