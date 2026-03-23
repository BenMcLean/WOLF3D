shader_type spatial;
render_mode unshaded, depth_draw_always, cull_disabled;

// VoxelAtlas.Texture: R8 3D texture storing raw palette indices.
// 0 = transparent, 1-255 = palette color index.
// Texture axes: U=MagicaVoxel X=Godot X, V=MagicaVoxel Y=Godot Z, W/slices=MagicaVoxel Z=Godot Y(up)
uniform sampler3D voxel_atlas : filter_nearest, repeat_disable;
// VoxelAtlas.PaletteTexture: 256x1 RGBA8 LUT. Index n is at texel (n, 0).
uniform sampler2D palette : filter_nearest, repeat_disable;

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

// DDA raymarcher operating in Godot local object space (Y-up).
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

varying vec3 local_vertex;
// Computed per-vertex so the per-eye INV_VIEW_MATRIX is used in both multipass and multiview VR.
varying vec3 camera_local;

void vertex() {
	local_vertex = VERTEX;
	vec3 camera_world = (INV_VIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	camera_local = (inverse(MODEL_MATRIX) * vec4(camera_world, 1.0)).xyz;
}

void fragment() {
	// BoxMesh local space is centered: X in [-sizeX/2, +sizeX/2], etc.
	// The DDA expects voxel-index space: X in [0, model_size.x), etc.
	// Shift ray origin by +half_size to convert between the two.
	// model_size is MagicaVoxel (X, Y, Z); BoxMesh.Size is Godot (X=MvX, Y=MvZ, Z=MvY),
	// so half_size must swizzle model_size accordingly.
	vec3 half_size = vec3(float(model_size.x), float(model_size.z), float(model_size.y)) * 0.5;
	vec3 ray_dir = normalize(local_vertex - camera_local);
	vec3 ray_origin = (FRONT_FACING ? local_vertex : camera_local) + half_size;
	// Push slightly inward so entry faces (where ray_origin lands exactly on an integer boundary)
	// don't immediately fail the voxel bounds check and produce transparent fragments.
	ray_origin += ray_dir * 0.0001;

	vec4 color = raymarch(ray_origin, ray_dir);
	if (color.a < 0.5) discard;
	ALBEDO = color.rgb;
}
