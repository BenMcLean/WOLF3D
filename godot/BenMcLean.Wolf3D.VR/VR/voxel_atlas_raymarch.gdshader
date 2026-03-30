shader_type spatial;
render_mode unshaded, depth_draw_always, cull_back;

// VoxelAtlas.Texture: R8 3D texture storing raw palette indices.
// 0 = transparent, 1-255 = palette color index.
// Texture axes: U=MagicaVoxel X=Godot X, V=MagicaVoxel Y=Godot Z, W/slices=MagicaVoxel Z=Godot Y(up)
uniform sampler3D voxel_atlas : filter_nearest, repeat_disable;
// VoxelAtlas.PaletteTexture: 256x1 RGBA8 LUT. Index n is at texel (n, 0).
uniform sampler2D palette : filter_nearest, repeat_disable, source_color;

// From Model.XYZ[0..2]: atlas origin in MagicaVoxel coordinates (X, Y, Z).
uniform ivec3 model_offset = ivec3(0, 0, 0);
// From Model.XYZ[3..5]: model size in MagicaVoxel coordinates (X, Y, Z).
uniform ivec3 model_size = ivec3(1, 1, 1);

uniform float max_distance = 200.0;
uniform int max_steps = 512;

// Sample voxel at Godot local-space integer position (Y-up).
// Returns palette index; 0 = transparent/empty.
int fetch_voxel(ivec3 local_pos) {
	// Bounds in Godot local space:
	//   x in [0, model_size.x)  — MagicaVoxel X
	//   y in [0, model_size.z)  — Godot Y(up) = MagicaVoxel Z
	//   z in [0, model_size.y)  — Godot Z(depth) = MagicaVoxel Y
	if (any(lessThan(local_pos, ivec3(0)))) return 0;
	if (local_pos.x >= model_size.x) return 0;
	if (local_pos.y >= model_size.z) return 0;
	if (local_pos.z >= model_size.y) return 0;
	// Convert Godot local space -> atlas texture coordinates:
	//   atlas.x = offset.x + local.x   (MagicaVoxel X = Godot X, no swap)
	//   atlas.y = offset.y + local.z   (MagicaVoxel Y = Godot Z)
	//   atlas.z = offset.z + local.y   (MagicaVoxel Z = Godot Y/up)
	ivec3 atlas_coord = ivec3(
		model_offset.x + local_pos.x,
		model_offset.y + local_pos.z,
		model_offset.z + local_pos.y
	);
	return int(round(texelFetch(voxel_atlas, atlas_coord, 0).r * 255.0));
}

// DDA raymarcher operating in voxel index space [0, model_size).
vec4 raymarch(vec3 ray_origin, vec3 ray_dir) {
	ivec3 voxel_pos = ivec3(floor(ray_origin));
	vec3 delta_dist = abs(vec3(1.0) / ray_dir);
	ivec3 ray_step = ivec3(sign(ray_dir));
	vec3 side_dist = (sign(ray_dir) * (vec3(voxel_pos) - ray_origin) + sign(ray_dir) * 0.5 + 0.5) * delta_dist;

	float distance = 0.0;

	for (int step = 0; step < max_steps && distance < max_distance; step++) {
		// Exit when out of model bounds
		if (voxel_pos.x < 0 || voxel_pos.x >= model_size.x) break;
		if (voxel_pos.y < 0 || voxel_pos.y >= model_size.z) break;
		if (voxel_pos.z < 0 || voxel_pos.z >= model_size.y) break;

		int palette_idx = fetch_voxel(voxel_pos);
		if (palette_idx > 0) {
			vec4 color = texelFetch(palette, ivec2(palette_idx, 0), 0);
			return vec4(color.rgb, 1.0);
		}

		if (side_dist.x < side_dist.y) {
			if (side_dist.x < side_dist.z) {
				side_dist.x += delta_dist.x;
				voxel_pos.x += ray_step.x;
				distance = side_dist.x;
			} else {
				side_dist.z += delta_dist.z;
				voxel_pos.z += ray_step.z;
				distance = side_dist.z;
			}
		} else {
			if (side_dist.y < side_dist.z) {
				side_dist.y += delta_dist.y;
				voxel_pos.y += ray_step.y;
				distance = side_dist.y;
			} else {
				side_dist.z += delta_dist.z;
				voxel_pos.z += ray_step.z;
				distance = side_dist.z;
			}
		}
	}

	return vec4(0.0);
}

// HMD centre position in world space, updated each frame by VoxelWeapon._Process.
// Eye-independent so this varying is consistent across both VR eyes.
uniform vec3 camera_world_center = vec3(0.0, 1.7, 0.0);
// Half the interpupillary distance in metres. Used to compute the per-eye parallax margin
// so the billboard covers the model from both eyes. 0.040 = 80 mm max IPD / 2, which
// covers the full human IPD range conservatively.
uniform float ipd_half = 0.040;
// Inverse of MODEL_MATRIX, updated each frame by VoxelWeapon._Process.
// Precomputed on the CPU to avoid a per-fragment inverse() call in the shader.
uniform mat4 inv_model_matrix;

// Billboarded world-space quad position — eye-independent, interpolates correctly in VR multiview.
varying vec3 vertex_world;

void vertex() {
	// Billboard: build a camera-facing quad from translation + uniform scale only.
	// MODEL_MATRIX rotation is deliberately ignored here so the quad always faces the
	// camera. inv_model_matrix (the precomputed inverse) is used in the fragment shader
	// to transform rays into the voxel coordinate space for AABB intersection.
	vec3 world_origin = MODEL_MATRIX[3].xyz;
	vec3 to_cam = normalize(camera_world_center - world_origin);
	// Build right/up axes perpendicular to to_cam.
	// Fall back to Z as reference when to_cam is near-vertical to avoid a degenerate cross product.
	vec3 ref_up = abs(to_cam.y) < 0.999 ? vec3(0.0, 1.0, 0.0) : vec3(0.0, 0.0, 1.0);
	vec3 right = normalize(cross(ref_up, to_cam));
	vec3 up    = normalize(cross(to_cam, right));
	// VoxelWeaponScale is uniform, so all three MODEL_MATRIX column lengths are equal.
	float s = length(MODEL_MATRIX[0].xyz);
	// Per-eye parallax margin: voxels at depth ±d from the billboard plane appear laterally
	// shifted in each eye's view by (ipd_half × d) / dist. The worst case is d = half the
	// model's longest dimension. This margin is added symmetrically so both eyes are covered.
	float dist = length(camera_world_center - world_origin);
	float model_half_depth = float(max(model_size.x, max(model_size.y, model_size.z))) * 0.5 * s;
	float parallax_margin = (dist > 0.001) ? (ipd_half * model_half_depth / dist) : 0.0;
	// VERTEX.xy are the quad local half-extents; s converts voxels to metres.
	vertex_world = world_origin
		+ right * (VERTEX.x * s + sign(VERTEX.x) * parallax_margin)
		+ up    * (VERTEX.y * s + sign(VERTEX.y) * parallax_margin);
	// Override clip-space position with the billboarded world position.
	// PROJECTION_MATRIX and VIEW_MATRIX are per-eye in VR multiview vertex shader.
	POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(vertex_world, 1.0);
}

void fragment() {
	// Per-eye camera position in world space (INV_VIEW_MATRIX is per-eye in fragment shader).
	vec3 camera_world = (INV_VIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;

	// Per-eye ray direction: camera → surface point in world space.
	// vertex_world is world-space geometry (same for both eyes); camera_world differs per eye.
	vec3 ray_world_dir = normalize(vertex_world - camera_world);

	// Transform camera position and ray direction to object local space.
	vec3 camera_local = (inv_model_matrix * vec4(camera_world, 1.0)).xyz;
	vec3 ray_dir = normalize((inv_model_matrix * vec4(ray_world_dir, 0.0)).xyz);
	// Apply Ry(+π/2) swizzle to orient voxel space to the controller's forward direction,
	// equivalent to Basis(Vector3.Up, -π/2) on the mesh but baked here so MODEL_MATRIX stays identity.
	// Ry(+π/2): (x, y, z) → (z, y, -x)
	camera_local = vec3(camera_local.z, camera_local.y, -camera_local.x);
	ray_dir = vec3(ray_dir.z, ray_dir.y, -ray_dir.x);

	// Shift from BoxMesh-centered local space [-size/2, +size/2] to voxel [0, model_size) space.
	// model_size is MagicaVoxel (X, Y, Z); BoxMesh.Size is Godot (X=MvX, Y=MvZ, Z=MvY),
	// so the Y/Z swizzle is needed for the per-axis half-extents.
	vec3 half_size = vec3(float(model_size.x), float(model_size.z), float(model_size.y)) * 0.5;
	vec3 camera_voxel = camera_local + half_size;

	// Ray-AABB intersection with voxel volume [0, 2*half_size).
	// Gives the exact entry point regardless of which BoxMesh face this fragment came from.
	vec3 box_max = 2.0 * half_size;
	vec3 inv_dir = 1.0 / ray_dir;
	vec3 t0 = (vec3(0.0) - camera_voxel) * inv_dir;
	vec3 t1 = (box_max - camera_voxel) * inv_dir;
	vec3 tmin_v = min(t0, t1);
	vec3 tmax_v = max(t0, t1);
	float t_enter = max(max(tmin_v.x, tmin_v.y), tmin_v.z);
	float t_exit  = min(min(tmax_v.x, tmax_v.y), tmax_v.z);

	if (t_enter >= t_exit || t_exit < 0.0) discard;
	// Clamp to strictly inside [0, box_max) rather than nudging along the ray direction.
	// An epsilon push in t-space is directionally biased and can land boundary-entry rays
	// in the wrong voxel on edge faces. Axis-aligned clamping handles the float-precision
	// boundary case (floor(model_size.x) == model_size.x) without any directional skew.
	vec3 ray_origin = clamp(
		camera_voxel + ray_dir * max(t_enter, 0.0),
		vec3(0.0), box_max - vec3(1e-4));

	vec4 color = raymarch(ray_origin, ray_dir);
	if (color.a < 0.5) discard;
	ALBEDO = color.rgb;
}
