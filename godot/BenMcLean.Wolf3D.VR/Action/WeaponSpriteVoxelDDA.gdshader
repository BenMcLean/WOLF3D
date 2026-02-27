shader_type spatial;
render_mode cull_disabled, unshaded, depth_draw_always;

uniform sampler2D sprite_texture;
uniform int resolution = 64;

void fragment() {

	// --- Ray in local space ---
	// Cache inv_model to avoid computing inverse(MODEL_MATRIX) twice
	mat4 inv_model = inverse(MODEL_MATRIX);
	vec3 ray_origin = (inv_model * vec4(CAMERA_POSITION_WORLD, 1.0)).xyz;
	// VERTEX in fragment() is in view space; convert view→world via INV_VIEW_MATRIX,
	// then world→local via inv_model, to match the space of ray_origin
	vec3 vertex_local = (inv_model * INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
	vec3 ray_dir = normalize(vertex_local - ray_origin);

	// --- Ray-AABB intersection ---
	vec3 box_min = vec3(-0.5);
	vec3 box_max = vec3(0.5);

	vec3 inv_dir = 1.0 / ray_dir;
	vec3 t0 = (box_min - ray_origin) * inv_dir;
	vec3 t1 = (box_max - ray_origin) * inv_dir;

	vec3 tmin = min(t0, t1);
	vec3 tmax = max(t0, t1);

	float t_entry = max(max(tmin.x, tmin.y), tmin.z);
	float t_exit  = min(min(tmax.x, tmax.y), tmax.z);

	if (t_exit < max(t_entry, 0.0))
		discard;

	// Nudge past the entry face to avoid FP boundary: ray_origin + ray_dir * t_entry
	// can land just outside the box, putting the initial cell at -1 (out of bounds).
	float t = max(t_entry, 0.0) + 1e-4;

	vec3 pos = ray_origin + ray_dir * t;
	vec3 p = pos + vec3(0.5);

	float grid = float(resolution);
	float voxel = 1.0 / grid;

	ivec2 cell = ivec2(floor(p.xz * grid));

	vec2 step_dir = sign(ray_dir.xz);
	vec2 delta = abs(vec2(voxel) / ray_dir.xz);

	vec2 next_boundary = (vec2(cell) + step(vec2(0.0), ray_dir.xz)) * voxel;
	// Initialize as absolute t values so steps use t = tmax2.x (assignment, not +=)
	vec2 tmax2 = t + (next_boundary - p.xz) / ray_dir.xz;

	vec4 tex = vec4(0.0);

	for (int i = 0; i < 128; i++) {

		if (cell.x < 0 || cell.y < 0 ||
			cell.x >= resolution || cell.y >= resolution)
			break;

		vec2 uv = (vec2(cell) + 0.5) / grid;
		// Force LOD 0: DDA alpha checks require exact per-texel alpha,
		// not blurred mipmap averages which corrupt the 0.5 threshold
		tex = textureLod(sprite_texture, uv, 0.0);

		if (tex.a > 0.5)
			break;

		if (tmax2.x < tmax2.y) {
			t = tmax2.x;
			tmax2.x += delta.x;
			cell.x += int(step_dir.x);
		} else {
			t = tmax2.y;
			tmax2.y += delta.y;
			cell.y += int(step_dir.y);
		}

		if (t > t_exit)
			discard;
	}

	if (tex.a <= 0.5)
		discard;

	ALBEDO = tex.rgb;

	// --- Correct depth ---
	vec3 hit_local = ray_origin + ray_dir * t;
	vec4 world_hit = MODEL_MATRIX * vec4(hit_local,1.0);
	vec4 clip_hit = PROJECTION_MATRIX * VIEW_MATRIX * world_hit;

	DEPTH = clip_hit.z / clip_hit.w;
}
