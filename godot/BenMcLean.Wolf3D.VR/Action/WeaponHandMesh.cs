using System;
using System.Collections.Generic;
using BenMcLean.Wolf3D.Simulator;
using Godot;

namespace BenMcLean.Wolf3D.VR;

/// <summary>
/// A BoxMesh rendered with the WeaponSpriteVoxelDDA shader, intended to be attached
/// to a VR controller "hand". Subscribes to weapon events for one slot and swaps
/// the shader texture whenever the weapon sprite changes.
/// Each instance owns its own ShaderMaterial so the texture swap is isolated from
/// other hands or overlapping subscribers.
/// </summary>
public partial class WeaponHandMesh : Node3D
{
	private readonly IReadOnlyDictionary<ushort, Texture2D> _spriteTextures;
	private readonly int _slotIndex;
	private ShaderMaterial _material;
	private Simulator.Simulator _simulator;

	/// <param name="spriteTextures">Sprite textures from VRAssetManager.SpriteTextures.</param>
	/// <param name="slotIndex">Weapon slot index this hand displays (0 = left/primary, 1 = right).</param>
	public WeaponHandMesh(IReadOnlyDictionary<ushort, Texture2D> spriteTextures, int slotIndex)
	{
		_spriteTextures = spriteTextures ?? throw new ArgumentNullException(nameof(spriteTextures));
		_slotIndex = slotIndex;
	}

	public override void _Ready()
	{
		Shader voxelShader = new Shader { Code = FileAccess.GetFileAsString("res://Action/WeaponSpriteVoxelDDA.gdshader") };
		if (voxelShader is null)
		{
			GD.PrintErr("Warning: WeaponSpriteVoxelDDA.gdshader not found");
			return;
		}
		_material = new ShaderMaterial { Shader = voxelShader };
		_material.SetShaderParameter("resolution", 64);
		MeshInstance3D mesh = new()
		{
			Name = "Mesh",
			Mesh = new BoxMesh { Size = Vector3.One },
			MaterialOverride = _material,
			RotationDegrees = new Vector3(90f, 0f, 0f),
			Scale = Constants.WeaponScale,
		};
		AddChild(mesh);
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

	/// <summary>Unsubscribe from simulator weapon events.</summary>
	public void Unsubscribe()
	{
		if (_simulator != null)
		{
			_simulator.WeaponEquipped -= OnWeaponEquipped;
			_simulator.WeaponSpriteChanged -= OnWeaponSpriteChanged;
		}
	}

	private void OnWeaponEquipped(WeaponEquippedEvent evt)
	{
		if (evt.SlotIndex != _slotIndex)
			return;
		UpdateTexture(evt.Shape);
	}

	private void OnWeaponSpriteChanged(WeaponSpriteChangedEvent evt)
	{
		if (evt.SlotIndex != _slotIndex)
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
