using System;
using BenMcLean.Wolf3D.Simulator;
using Godot;

namespace BenMcLean.Wolf3D.VR.VR;

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
/// <param name="gripNode">
/// Optional grip-pose controller node (from IDisplayMode.GetGripHandNode).
/// When supplied, VoxelWeapon's local position is updated each frame so its origin
/// (where the grip voxel sits) tracks the grip pose origin in world space, while the
/// weapon's orientation is inherited from its parent (aim-pose controller).
/// </param>
public partial class VoxelWeapon(VoxelAtlas voxelAtlas, int slotIndex, Node3D gripNode = null) : Node3D
{
	private readonly VoxelAtlas _voxelAtlas = voxelAtlas ?? throw new ArgumentNullException(nameof(voxelAtlas));
	private readonly Node3D _gripNode = gripNode;
	private ShaderMaterial _material;
	private QuadMesh _quadMesh;
	private MeshInstance3D _meshInstance;
	private Simulator.Simulator _simulator;

	public override void _Ready()
	{
		// Must always process so _Process() keeps shader uniforms current even when
		// the parent scene (e.g. MenuRoom) is paused during fade transitions.
		ProcessMode = ProcessModeEnum.Always;
		Shader voxelShader = new() { Code = FileAccess.GetFileAsString("res://Resources/voxel_atlas_raymarch.gdshader") };
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
			Scale = Constants.VoxelWeaponScale
		};
		AddChild(_meshInstance);
		// Initialise to identity so the uniform is never the zero matrix before _Process runs.
		_material.SetShaderParameter("inv_model_matrix", Transform3D.Identity);
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

	/// <summary>
	/// Display a fixed voxel model without subscribing to a simulator.
	/// Use this for static contexts like the menu where no simulator is running.
	/// Must be called after the node has been added to the scene tree (_Ready has run).
	/// </summary>
	public void ShowModel(ushort shape) => UpdateModel(shape);

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
		// QuadMesh size: bounding sphere diameter in voxel units, plus a 1-voxel margin.
		// The quad is centered at the model's geometric centre (not the grip), so a half-extent
		// equal to the bounding sphere radius (diagonal/2) is theoretically sufficient, but the
		// fit is very tight for elongated models (e.g. a long gun barrel where diagonal ≈ sizeX).
		// One extra voxel prevents floating-point boundary precision from clipping the far edge.
		// Scale converts voxels → metres.
		float diagonal = Mathf.Sqrt(xyz[3] * xyz[3] + xyz[4] * xyz[4] + xyz[5] * xyz[5]);
		_quadMesh.Size = new Vector2(diagonal + 1f, diagonal + 1f);
		// Position mesh so the grip origin voxel (xyz[6..8], MagicaVoxel coords) sits at this node's local origin.
		// VoxelWeapon is a child of the aim controller (no rotation offset), so _meshInstance.Position
		// is expressed in aim-controller space. _Process moves VoxelWeapon itself to the grip pose
		// origin each frame, so the grip voxel ends up at the physical hand position in world space.
		// The Ry(+π/2) swizzle in the shader maps: controller X ↔ –MvY, controller Y ↔ MvZ, controller Z ↔ MvX.
		// Grip voxel center offset from mesh centre, converted to parent (aim-controller) space:
		//   controller X = +(gripMvY + 0.5 – sizeY / 2) × scale   [positive factor due to sign flip]
		//   controller Y = –(gripMvZ + 0.5 – sizeZ / 2) × scale
		//   controller Z = –(gripMvX + 0.5 – sizeX / 2) × scale
		if (xyz.Length >= 9)
		{
			Vector3 scale = _meshInstance.Scale;
			_meshInstance.Position = new Vector3(
				scale.X * (xyz[7] + 0.5f - xyz[4] * 0.5f),
				-scale.Y * (xyz[8] + 0.5f - xyz[5] * 0.5f),
				-scale.Z * (xyz[6] + 0.5f - xyz[3] * 0.5f));
			UpdateInvModelMatrix();
		}
		Visible = true;
	}

	private void UpdateInvModelMatrix()
	{
		if (_meshInstance is not null)
			_material.SetShaderParameter("inv_model_matrix", _meshInstance.GlobalTransform.AffineInverse());
	}

	public override void _Process(double delta)
	{
		if (_material is null) return;
		// Move this node's origin to the grip pose position each frame so the grip voxel
		// aligns with the physical hand grip, while orientation is inherited from the aim
		// controller (parent). This decouples position (grip pose) from orientation (aim pose).
		if (_gripNode is not null && GetParent() is Node3D aimParent)
			Position = aimParent.GlobalTransform.AffineInverse() * _gripNode.GlobalPosition;
		Camera3D camera = GetViewport().GetCamera3D();
		if (camera is not null)
			_material.SetShaderParameter("camera_world_center", camera.GlobalPosition);
		UpdateInvModelMatrix();
	}

	public override void _ExitTree()
	{
		Unsubscribe();
		base._ExitTree();
	}
}
