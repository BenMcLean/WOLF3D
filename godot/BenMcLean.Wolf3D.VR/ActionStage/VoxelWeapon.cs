using System;
using BenMcLean.Wolf3D.Simulator;
using BenMcLean.Wolf3D.VR.VR;
using Godot;

namespace BenMcLean.Wolf3D.VR.ActionStage;

/// <summary>
/// A QuadMesh rendered with the voxel_atlas_raymarch shader, intended to be attached
/// to a VR controller "hand". Subscribes to weapon events for one slot and swaps
/// to the matching voxel model whenever the weapon sprite changes.
/// Each instance owns its own ShaderMaterial so model swaps are isolated from
/// other hands or overlapping subscribers.
/// A QuadMesh is used instead of BoxMesh so that all rasterized fragments lie on one
/// flat plane, giving consistent vertex_world-based ray directions with no face-seam
/// distortion. The quad is sized to the bounding sphere diameter so it covers the
/// weapon silhouette from any viewing angle without needing per-frame updates.
/// </summary>
/// <param name="voxelAtlas">Voxel atlas from VRAssetManager.VoxelAtlas.</param>
/// <param name="slotIndex">Weapon slot index this hand displays (0 = left/primary, 1 = right).</param>
public partial class VoxelWeapon(VoxelAtlas voxelAtlas, int slotIndex) : Node3D
{
	private readonly VoxelAtlas _voxelAtlas = voxelAtlas ?? throw new ArgumentNullException(nameof(voxelAtlas));
	private ShaderMaterial _material;
	private QuadMesh _quadMesh;
	private MeshInstance3D _meshInstance;
	private Simulator.Simulator _simulator;

	public override void _Ready()
	{
		Shader voxelShader = new() { Code = FileAccess.GetFileAsString("res://VR/voxel_atlas_raymarch.gdshader") };
		if (voxelShader is null)
		{
			GD.PrintErr("Warning: voxel_atlas_raymarch.gdshader not found");
			return;
		}
		_material = new ShaderMaterial { Shader = voxelShader };
		_material.SetShaderParameter("voxel_atlas", _voxelAtlas.Texture);
		_material.SetShaderParameter("palette", _voxelAtlas.PaletteTexture);
		_quadMesh = new QuadMesh { Size = Vector2.One };
		_meshInstance = new MeshInstance3D
		{
			Name = "Mesh",
			Mesh = _quadMesh,
			MaterialOverride = _material,
		};
		_meshInstance.Scale = Constants.VoxelWeaponScale;
		AddChild(_meshInstance);
		Visible = false;  // Hidden until a weapon is equipped in this slot
	}

	/// <summary>
	/// Subscribe to simulator weapon events for this hand's slot.
	/// Call this after the simulator is initialized, then call
	/// <see cref="Simulator.Simulator.EmitWeaponState"/> to set the initial model.
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
		UpdateModel(evt.Shape);
	}

	private void OnWeaponSpriteChanged(WeaponSpriteChangedEvent evt)
	{
		if (evt.SlotIndex != slotIndex)
			return;
		UpdateModel(evt.Shape);
	}

	private void UpdateModel(ushort shape)
	{
		if (_material is null || _quadMesh is null || _meshInstance is null)
			return;
		if (!_voxelAtlas.Models.TryGetValue(shape, out int[] xyz))
		{
			Visible = false;
			return;
		}
		// xyz[0..2] = atlas origin (MagicaVoxel X, Y, Z); xyz[3..5] = model size; xyz[6..8] = grip origin (MagicaVoxel X, Y, Z)
		_material.SetShaderParameter("model_offset", new Vector3I(xyz[0], xyz[1], xyz[2]));
		_material.SetShaderParameter("model_size", new Vector3I(xyz[3], xyz[4], xyz[5]));
		// QuadMesh size: 2× bounding sphere diameter in voxel units.
		// 2× because the grip origin can be at the edge of the model, so the billboard
		// must extend a full diagonal in every direction from the grip point to guarantee
		// the entire weapon silhouette is covered. Scale converts voxels → metres.
		float diagonal = Mathf.Sqrt(xyz[3] * xyz[3] + xyz[4] * xyz[4] + xyz[5] * xyz[5]);
		_quadMesh.Size = new Vector2(2f * diagonal, 2f * diagonal);
		// Position mesh so the grip origin voxel (xyz[6..8], MagicaVoxel coords) sits at the controller's grip point (local origin).
		// The Ry(+π/2) swizzle in the shader maps: controller X ↔ -MvY, controller Y ↔ MvZ, controller Z ↔ MvX.
		// Grip voxel center offset from mesh centre, converted to parent (controller) space:
		//   controller X = +(gripMvY + 0.5 - sizeY / 2) × scale   [positive factor due to sign flip]
		//   controller Y = -(gripMvZ + 0.5 - sizeZ / 2) × scale
		//   controller Z = -(gripMvX + 0.5 - sizeX / 2) × scale
		if (xyz.Length >= 9)
		{
			Vector3 scale = _meshInstance.Scale;
			_meshInstance.Position = new Vector3(
				scale.X * (xyz[7] + 0.5f - xyz[4] * 0.5f),
				-scale.Y * (xyz[8] + 0.5f - xyz[5] * 0.5f),
				-scale.Z * (xyz[6] + 0.5f - xyz[3] * 0.5f));
		}
		Visible = true;
	}

	public override void _Process(double delta)
	{
		if (_material is null) return;
		Camera3D camera = GetViewport().GetCamera3D();
		if (camera is not null)
			_material.SetShaderParameter("camera_world_center", camera.GlobalPosition);
	}

	public override void _ExitTree()
	{
		Unsubscribe();
		base._ExitTree();
	}
}
