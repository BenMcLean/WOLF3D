shader_type spatial;
render_mode unshaded, depth_draw_always, cull_back;

// =============================================================================
// OVERVIEW
// =============================================================================
// Billboard quad + DDA raymarcher for voxel weapon models.
//
// WHY A QUAD (not a box mesh):
//   All rasterised fragments lie on one plane, giving a consistent ray direction
//   per pixel with no face-normal discontinuities. A box mesh produces six faces
//   each with a different normal, creating seam artefacts where faces meet because
//   the ray origin jumps discontinuously between adjacent faces.
//
// HOW IT WORKS:
//   1. Vertex shader: positions the quad as a camera-facing billboard sized to the
//      model's bounding-sphere diameter.  MODEL_MATRIX rotation is intentionally
//      ignored so the quad always faces the camera; the controller orientation is
//      recovered in the fragment shader via inv_model_matrix.
//   2. Fragment shader: for each pixel, casts a ray from the VR eye through the
//      billboard surface into the voxel volume.  A DDA (Digital Differential
//      Analyser) traverses the 3D voxel grid and returns the first filled voxel's
//      palette colour.
//
// COORDINATE SYSTEM CHAIN:
//   World space       — Godot Y-up right-hand system, metres.
//   Mesh local space  — inv_model_matrix applied; same handedness and Y-up as
//                       world, scaled by 1/VoxelWeaponScale (250 vox/m).
//                       Origin = _meshInstance.GlobalPosition (quad centre).
//   DDA voxel space   — after Ry(+π/2) swizzle + half-size shift; integer grid
//                       [0, model_size), origin at the corner of the bounding box.
//                       Axis mapping: x = MvX, y = MvZ (up), z = MvY (depth).
//
//   MagicaVoxel (Mv) axes:  X = model right,  Y = model depth/forward,  Z = model up
//   Godot world axes:        X = world right,  Y = world up,             Z = world back
//
// GRIP ALIGNMENT:
//   VoxelWeapon.cs sets _meshInstance.Position so the grip voxel of the model
//   coincides with VoxelWeapon's node origin (the controller grip pose).
//   This offset is encoded in MODEL_MATRIX / inv_model_matrix, so the ray origin
//   and direction automatically account for it.
//
// VR MULTIVIEW:
//   VIEW_MATRIX / INV_VIEW_MATRIX are the MIDPOINT (cyclopean) view matrices in
//   Godot multiview — identical for both eyes.  CAMERA_POSITION_WORLD is likewise
//   the midpoint.  Per-eye separation is provided via EYE_OFFSET (vec3, in view
//   space, e.g. ±ipd/2 along X).
//   Per-eye ray origin:     (INV_VIEW_MATRIX * vec4(EYE_OFFSET, 1.0)).xyz
//   Per-eye ray direction:  normalize(view_frag.xyz − EYE_OFFSET) → world via
//                           INV_VIEW_MATRIX with w=0.
//   camera_world_center is the eye-independent HMD midpoint used ONLY for
//   billboard orientation so the quad faces the same way for both eyes.
// =============================================================================

// =============================================================================
// UNIFORMS
// =============================================================================

// R8 3D texture: each texel stores a raw palette index in [0, 255] encoded as a
// normalised float in [0.0, 1.0].  0 = transparent / empty; 1–255 = palette entry.
// filter_nearest prevents blending between indices, which would produce bogus values.
//
// Texture axis layout (established by VoxelAtlas.BuildTexture3D):
//   U axis (Godot width)         = MagicaVoxel X  = Godot world X
//   V axis (Godot height)        = MagicaVoxel Y  = Godot world Z  (horizontal depth)
//   W axis (Godot depth / slice) = MagicaVoxel Z  = Godot world Y  (up)
uniform sampler3D voxel_atlas : filter_nearest, repeat_disable;

// 256×1 RGBA8 palette LUT.  Texel at column n is palette entry n.
// Palette entry 0 is transparent (handled in the DDA by checking index > 0).
// filter_nearest and texelFetch ensure exact integer index lookups without bleeding.
uniform sampler2D palette : filter_nearest, repeat_disable, source_color;

// Atlas origin of this model in the 3D texture, in MagicaVoxel (X, Y, Z) voxel units.
// Set from Model.XYZ[0..2] by VoxelWeapon.UpdateModel.
uniform ivec3 model_offset = ivec3(0, 0, 0);

// Bounding-box size of this model in MagicaVoxel (X, Y, Z) voxel units.
// Set from Model.XYZ[3..5] by VoxelWeapon.UpdateModel.
// Note: in DDA voxel space the axes are reordered to (MvX, MvZ, MvY), so
// lookups that depend on model_size must account for the y/z swap (see fetch_voxel).
uniform ivec3 model_size = ivec3(1, 1, 1);

// DDA safety limits.  For weapon-sized models (≤ ~40 voxels per axis), the DDA
// always exits via the bounds check well before these are reached; they exist
// only to prevent a GPU hang if malformed data is ever supplied.
uniform float max_distance = 200.0;
uniform int max_steps = 512;

// HMD centre position in world space (metres), updated every frame by
// VoxelWeapon._Process.  Using the midpoint rather than a per-eye position keeps
// billboard orientation identical for both eyes, so vertex_world (a varying) is
// consistent and interpolates correctly in VR multiview.
uniform vec3 camera_world_center = vec3(0.0, 1.7, 0.0);

// Half of the maximum interpupillary distance (IPD), in metres.
// 0.040 m = 80 mm / 2, conservatively covering the full human IPD range.
// Used in the vertex shader to widen the billboard so that voxels at depth ±d
// from the billboard plane are still covered by fragments in both eyes.
uniform float ipd_half = 0.040;

// Inverse of _meshInstance.GlobalTransform, precomputed by
// VoxelWeapon.UpdateInvModelMatrix every frame (and immediately on model swap).
// Precomputing on the CPU avoids an expensive per-fragment inverse() in the shader.
// MODEL_MATRIX (the forward transform) is Godot-provided; its inverse is not
// automatically available in the fragment stage.
uniform mat4 inv_model_matrix;

// =============================================================================
// VARYINGS
// =============================================================================

// World-space position of this quad vertex after billboard transformation.
// Carried as a varying (not reconstructed from POSITION / gl_FragCoord) so that
// it interpolates correctly in VR multiview, where PROJECTION_MATRIX is per-eye
// but vertex_world represents pre-projection world geometry shared by both eyes.
varying vec3 vertex_world;

// =============================================================================
// VOXEL FETCH
// =============================================================================

// Returns the palette index [0, 255] for the voxel at `pos` in DDA voxel space,
// or 0 (transparent) if pos is out of bounds or the voxel is empty.
//
// DDA voxel space axes (input):  x = MvX,  y = MvZ (up),  z = MvY (depth)
// Atlas texture axes (output):   U = MvX,  V = MvY,        W = MvZ
//
// The y↔z swap in atlas_coord converts DDA space to atlas UV layout:
//   atlas.x  (U = MvX) ← pos.x  (DDA x = MvX)  — no swap needed
//   atlas.y  (V = MvY) ← pos.z  (DDA z = MvY)  — swap: DDA z holds MvY
//   atlas.z  (W = MvZ) ← pos.y  (DDA y = MvZ)  — swap: DDA y holds MvZ
//
// The bounds check mirrors the DDA loop's own check (which runs before calling
// this function), so it is redundant in normal operation.  It is retained as a
// safety net in case this function is ever called from another context.
int fetch_voxel(ivec3 pos) {
	if (any(lessThan(pos, ivec3(0)))) return 0;
	if (pos.x >= model_size.x) return 0;  // MvX bound
	if (pos.y >= model_size.z) return 0;  // MvZ bound (DDA y axis = MvZ)
	if (pos.z >= model_size.y) return 0;  // MvY bound (DDA z axis = MvY)
	ivec3 atlas_coord = ivec3(
		model_offset.x + pos.x,  // U = MvX  (DDA x → atlas x, no swap)
		model_offset.y + pos.z,  // V = MvY  (DDA z → atlas y)
		model_offset.z + pos.y   // W = MvZ  (DDA y → atlas z)
	);
	// texelFetch returns a normalised float in [0, 1] for an R8 texture.
	// Multiply by 255 and round to recover the integer palette index stored
	// by the baker (direct cast would truncate 0.9999... to 0).
	return int(round(texelFetch(voxel_atlas, atlas_coord, 0).r * 255.0));
}

// =============================================================================
// DDA RAYMARCHER
// =============================================================================

// Traverses the voxel grid in DDA voxel space from ray_origin along ray_dir,
// returning the RGBA colour of the first non-empty voxel hit, or vec4(0.0) if
// the ray exits the model volume without hitting anything.
//
// DDA ALGORITHM OVERVIEW:
//   For each axis, maintain the ray-parameter t at which the ray next crosses a
//   grid plane (side_dist).  At each step, advance along whichever axis has the
//   nearest next crossing (smallest side_dist), increment that axis's voxel index,
//   and add delta_dist to side_dist for that axis to get the following crossing.
//   Check the newly entered voxel after each step.
//
//   delta_dist[i] = |1 / ray_dir[i]| — the t-interval between consecutive
//   parallel grid planes along axis i.  Axes with ray_dir[i] = 0 get delta_dist
//   = infinity, so they are never stepped (correctly, since the ray never crosses
//   planes in that direction).
//
// All inputs are in DDA voxel space [0, model_size).
// hit_pos_dda is set to the centre of the first filled voxel hit (voxel_pos + 0.5),
// still in DDA voxel space.  The caller uses this to compute the correct clip-space
// depth of the voxel surface for the depth buffer.
vec4 raymarch(vec3 ray_origin, vec3 ray_dir, out vec3 hit_pos_dda) {
	hit_pos_dda = vec3(0.0);
	// ---- Starting voxel ----
	// Nudge ray_origin slightly in the ray travel direction before flooring.
	// Without this, floating-point imprecision at the AABB entry point can place
	// ray_origin just below a voxel boundary (e.g. 2.9999997 instead of 3.0),
	// causing floor() to start the DDA one voxel short and miss the first filled
	// voxel, producing a visible gap when viewed at certain angles.
	// The per-axis sign(ray_dir) nudge is directionally correct independently for
	// each axis: positive travel nudges positive, negative travel nudges negative,
	// and zero travel (sign = 0) leaves that axis unaffected.
	ivec3 voxel_pos = ivec3(floor(ray_origin + 1e-4 * sign(ray_dir)));

	// t-interval between consecutive grid planes for each axis.
	// Computed as abs(1 / ray_dir[i]) so it is always positive regardless of
	// direction; sign is handled separately by ray_step.
	// For ray_dir[i] = 0: delta_dist[i] = infinity (axis never stepped).
	vec3 delta_dist = abs(vec3(1.0) / ray_dir);

	// Per-axis step direction: +1 or -1.
	// For ray_dir[i] = 0: ray_step[i] = 0 (axis never stepped; side_dist[i] = inf).
	ivec3 ray_step = ivec3(sign(ray_dir));

	// t-distance from ray_origin to the first grid-plane crossing in each axis.
	//
	// DERIVATION (positive ray_dir.x as example):
	//   The ray is currently in voxel vp.x, which spans [vp.x, vp.x + 1).
	//   The next +X plane is at vp.x + 1.
	//   side_dist.x = (vp.x + 1 - ro.x) / ray_dir.x
	//               = (vp.x + 1 - ro.x) * delta_dist.x      (since delta = 1/ray_dir)
	//
	// For negative ray_dir.x:
	//   The next -X plane is at vp.x.
	//   side_dist.x = (ro.x - vp.x) / |ray_dir.x|
	//               = (ro.x - vp.x) * delta_dist.x
	//
	// Unified formula using ray_sign = sign(ray_dir):
	//   positive (ray_sign = +1): (vp - ro + 0.5 + 0.5) * Δ = (vp - ro + 1) * Δ  ✓
	//   negative (ray_sign = -1): (-(vp - ro + 0.5) + 0.5) * Δ = (ro - vp) * Δ   ✓
	//   zero     (ray_sign =  0): 0.5 * Δ = 0.5 * infinity = infinity              ✓
	//
	// vec3(ray_step) reuses the already-computed sign(ray_dir), avoiding a
	// redundant sign() call.
	vec3 ray_sign = vec3(ray_step);
	vec3 side_dist = (ray_sign * (vec3(voxel_pos) - ray_origin + 0.5) + 0.5) * delta_dist;

	// Accumulated DDA t-value, used only as a safety exit via max_distance.
	// For all valid weapon-sized models the bounds check below is the real exit.
	float distance = 0.0;

	for (int step = 0; step < max_steps && distance < max_distance; step++) {
		// Primary exit: ray has left the model bounding box.
		// Axes in DDA space: x = MvX, y = MvZ, z = MvY — hence comparing x against
		// model_size.x, y against model_size.z, and z against model_size.y.
		if (voxel_pos.x < 0 || voxel_pos.x >= model_size.x) break;
		if (voxel_pos.y < 0 || voxel_pos.y >= model_size.z) break;
		if (voxel_pos.z < 0 || voxel_pos.z >= model_size.y) break;

		int palette_idx = fetch_voxel(voxel_pos);
		if (palette_idx > 0) {
			// First non-empty voxel hit: look up its colour in the palette LUT.
			// Record the voxel centre so the caller can write the correct depth.
			hit_pos_dda = vec3(voxel_pos) + vec3(0.5);
			vec4 color = texelFetch(palette, ivec2(palette_idx, 0), 0);
			return vec4(color.rgb, 1.0);
		}

		// Advance to the next voxel: step along the axis whose next plane crossing
		// is nearest (smallest side_dist).  After stepping, advance that axis's
		// side_dist by delta_dist so it now points to the crossing after next.
		// Ties (equal side_dist values) resolve deterministically via strict <,
		// preferring x over y over z; this is fine because ties only occur at
		// exact voxel corners, which are infinitesimally rare at non-integer angles.
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

	return vec4(0.0);  // Ray exited the model volume without hitting any voxel.
}

// =============================================================================
// VERTEX SHADER
// =============================================================================

void vertex() {
	// ---- Billboard origin ----
	// MODEL_MATRIX[3].xyz is the world-space translation of the MeshInstance
	// (i.e. _meshInstance.GlobalPosition, which is the quad centre).
	// Rotation and scale from MODEL_MATRIX are intentionally ignored for the
	// billboard geometry so the quad always faces the camera.  The controller
	// orientation is recovered in the fragment shader through inv_model_matrix,
	// which is the full inverse of MODEL_MATRIX including rotation and scale.
	vec3 world_origin = MODEL_MATRIX[3].xyz;

	// Direction from the quad centre to the HMD midpoint.
	// Using camera_world_center (the eye-independent midpoint) rather than a
	// per-eye position ensures the billboard faces the same way for both eyes.
	vec3 to_cam = normalize(camera_world_center - world_origin);

	// Build an orthonormal right/up basis for the billboard plane.
	// cross(world_up, to_cam) gives the right vector perpendicular to both.
	// cross(to_cam, right) gives the up vector perpendicular to both.
	// When to_cam is nearly vertical (|y| ≥ 0.999), cross(world_up, to_cam) would
	// be near-zero (degenerate), so fall back to world-Z as the reference axis.
	vec3 ref_up = abs(to_cam.y) < 0.999 ? vec3(0.0, 1.0, 0.0) : vec3(0.0, 0.0, 1.0);
	vec3 right = normalize(cross(ref_up, to_cam));
	vec3 up    = normalize(cross(to_cam, right));

	// Extract the uniform scale factor (metres per mesh-local unit) from MODEL_MATRIX.
	// VoxelWeaponScale is guaranteed uniform, so all three column lengths are equal;
	// reading column 0 (the X column) is sufficient.
	float s = length(MODEL_MATRIX[0].xyz);

	// ---- Stereo parallax margin ----
	// In VR the two eyes are separated by the IPD.  A voxel at depth offset d from
	// the billboard plane appears laterally shifted by approximately
	//   lateral_shift = ipd_half * d / dist
	// in each eye's view.  If the billboard only covers the bounding-sphere
	// projection for the HMD centre ray, voxels near the model edges can fall
	// outside the billboard in one eye and receive no fragment, causing half the
	// model to be invisible in that eye.
	//
	// The worst-case depth offset is half the model's longest dimension (the
	// bounding-sphere radius), giving a required margin of:
	//   parallax_margin = ipd_half * (max_dim / 2 * scale) / dist
	//
	// Adding this margin symmetrically to each side of the quad ensures all voxels
	// are covered from both eyes for any viewing angle.
	float dist = length(camera_world_center - world_origin);
	float model_half_depth = float(max(model_size.x, max(model_size.y, model_size.z))) * 0.5 * s;
	float parallax_margin = (dist > 0.001) ? (ipd_half * model_half_depth / dist) : 0.0;

	// ---- Billboarded vertex position ----
	// VERTEX.xy is the quad vertex in mesh-local space before scale is applied.
	// Multiplying by s converts to metres.  The parallax margin is added outward
	// on each side via sign(VERTEX.xy) so the quad is enlarged symmetrically.
	vertex_world = world_origin
		+ right * (VERTEX.x * s + sign(VERTEX.x) * parallax_margin)
		+ up    * (VERTEX.y * s + sign(VERTEX.y) * parallax_margin);

	// Override the clip-space output with the billboarded world position.
	// In VR multiview, PROJECTION_MATRIX and VIEW_MATRIX are per-eye, so each eye
	// gets the correct perspective projection of the same world-space billboard.
	POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(vertex_world, 1.0);
}

// =============================================================================
// FRAGMENT SHADER
// =============================================================================

void fragment() {
	// ---- Per-eye camera position ----
	// In Godot VR multiview, CAMERA_POSITION_WORLD is documented as the MIDPOINT
	// between the two eyes, and VIEW_MATRIX is a midpoint/cyclopean view matrix.
	// EYE_OFFSET is the per-eye position offset IN VIEW SPACE (e.g. ±ipd/2 on X).
	// To get the per-eye world-space eye position, treat EYE_OFFSET as a point in
	// the midpoint view space and transform it to world space via INV_VIEW_MATRIX:
	//   (INV_VIEW_MATRIX * vec4(EYE_OFFSET, 1.0)).xyz
	// Using vec4(0,0,0,1) instead would give CAMERA_POSITION_WORLD (midpoint),
	// which is IDENTICAL for both eyes and produces no stereo parallax.
	vec3 camera_world = (INV_VIEW_MATRIX * vec4(EYE_OFFSET, 1.0)).xyz;

	// ---- Per-eye ray direction ----
	// Unproject the fragment's SCREEN_UV through the (midpoint) projection frustum to
	// get the fragment position in view space, then cast the ray FROM the per-eye
	// position (EYE_OFFSET in view space) THROUGH that fragment point.
	// This ensures the ray direction also accounts for the IPD offset, not just the
	// ray origin — without this, the direction would be from the midpoint through the
	// fragment, which is nearly correct but geometrically inconsistent with the origin.
	// INV_VIEW_MATRIX with w=0 applies only rotation (no translation), transforming
	// the view-space direction to world space.
	vec4 view_frag = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, 0.0, 1.0);
	view_frag /= view_frag.w;
	vec3 ray_dir_view = normalize(view_frag.xyz - EYE_OFFSET);
	vec3 ray_world_dir = normalize((INV_VIEW_MATRIX * vec4(ray_dir_view, 0.0)).xyz);

	// ---- World space → mesh local space ----
	// inv_model_matrix is the inverse of _meshInstance.GlobalTransform.
	// Applied as a point transform (w=1) it gives the eye position in mesh local
	// space (in voxel units, since MODEL_MATRIX encodes VoxelWeaponScale = 0.004 m/vox).
	// Applied as a direction (w=0) it rotates and scales the ray direction into mesh
	// local space.  Re-normalisation is required because the inverse of a scaled
	// matrix changes the vector length.
	vec3 camera_local = (inv_model_matrix * vec4(camera_world, 1.0)).xyz;
	vec3 ray_dir = normalize((inv_model_matrix * vec4(ray_world_dir, 0.0)).xyz);

	// ---- Mesh local space → DDA voxel space: Ry(+π/2) swizzle ----
	// The MeshInstance carries no stored rotation (MODEL_MATRIX rotation is bypassed
	// by the billboard vertex shader), so mesh local axes are aligned with world axes.
	// To orient the voxel model so the gun barrel (MvX axis) points along the
	// controller aim direction (Godot +Z), we apply a virtual Ry(+π/2) rotation here
	// in the fragment shader rather than storing it in MODEL_MATRIX.
	//
	// Ry(+π/2) rotation matrix (rotation about world Y by +90°):
	//   | 0   0   1 |       i.e.  new_x =  old_z
	//   | 0   1   0 |            new_y =  old_y
	//   |-1   0   0 |            new_z = -old_x
	// In swizzle notation:  (x, y, z) → (z, y, -x)
	//
	// After this swizzle the DDA voxel axes relate to controller (mesh local) axes:
	//   DDA x  (= MvX, barrel)  ↔  controller +Z  (aim / forward)
	//   DDA y  (= MvZ, up)      ↔  controller +Y  (up)
	//   DDA z  (= MvY, depth)   ↔  controller −X  (left)
	//
	// This means models authored with the barrel along the MvX axis in MagicaVoxel
	// will aim along the controller's forward direction in Godot.
	camera_local = vec3(camera_local.z, camera_local.y, -camera_local.x);
	ray_dir      = vec3(ray_dir.z,      ray_dir.y,      -ray_dir.x);

	// ---- Mesh local space → DDA voxel space: half-size shift ----
	// After the swizzle the origin is still at the MeshInstance centre (the quad
	// centre, which is offset from the grip voxel by _meshInstance.Position).
	// Shift the origin to the corner of the bounding box so the DDA can work in
	// the [0, model_size) integer voxel index space.
	//
	// half_size must account for the DDA axis reordering:
	//   DDA x = MvX  →  half_size.x = model_size.x / 2
	//   DDA y = MvZ  →  half_size.y = model_size.z / 2  (y↔z swap: DDA y = MvZ)
	//   DDA z = MvY  →  half_size.z = model_size.y / 2  (y↔z swap: DDA z = MvY)
	vec3 half_size = vec3(float(model_size.x), float(model_size.z), float(model_size.y)) * 0.5;
	vec3 camera_voxel = camera_local + half_size;

	// ---- Ray–AABB intersection ----
	// Find the t-parameter range [t_enter, t_exit] over which the ray
	//   P(t) = camera_voxel + t * ray_dir
	// is inside the voxel volume AABB [0, box_max).
	//
	// For each axis i, the ray intersects the pair of planes at 0 and box_max[i]:
	//   t0[i] = (0          - camera_voxel[i]) / ray_dir[i]
	//   t1[i] = (box_max[i] - camera_voxel[i]) / ray_dir[i]
	//
	// tmin_v[i] = min(t0[i], t1[i])  — entry t for axis i (may be negative if
	//                                   camera is already past that face)
	// tmax_v[i] = max(t0[i], t1[i])  — exit  t for axis i
	//
	// The overall entry is the latest per-axis entry (all three slabs must be entered):
	//   t_enter = max(tmin_v.x, tmin_v.y, tmin_v.z)
	// The overall exit is the earliest per-axis exit (any slab exit ends the overlap):
	//   t_exit  = min(tmax_v.x, tmax_v.y, tmax_v.z)
	//
	// Ray misses the box if t_enter >= t_exit (slabs don't overlap).
	// Box is behind camera if t_exit < 0 (entire intersection is behind origin).
	//
	// Special case — axis-parallel rays (ray_dir[i] = 0):
	//   inv_dir[i] = ±infinity; t0[i] and t1[i] become ±infinity.
	//   min/max then leave that axis's contribution as (−∞, +∞), correctly imposing
	//   no t constraint for a ray that never crosses planes in that direction.
	//   Exception: if camera_voxel[i] is exactly 0 or box_max[i] and ray_dir[i] = 0,
	//   the product 0 * ∞ = NaN can occur; the clamp below recovers from this.
	vec3 box_max = 2.0 * half_size;
	vec3 inv_dir = 1.0 / ray_dir;
	vec3 t0 = (vec3(0.0) - camera_voxel) * inv_dir;
	vec3 t1 = (box_max  - camera_voxel) * inv_dir;
	vec3 tmin_v = min(t0, t1);
	vec3 tmax_v = max(t0, t1);
	float t_enter = max(max(tmin_v.x, tmin_v.y), tmin_v.z);
	float t_exit  = min(min(tmax_v.x, tmax_v.y), tmax_v.z);

	if (t_enter >= t_exit || t_exit < 0.0) discard;

	// ---- Ray entry point ----
	// Advance the camera position to the AABB entry face.
	// max(t_enter, 0.0): if t_enter < 0 the camera is already inside the volume,
	// so start from the camera position itself (t = 0) rather than going backwards.
	//
	// Axis-aligned clamp to [0, box_max − 1e-4]:
	//   Lower bound (0): recovers from NaN produced by the 0 * ∞ AABB edge case,
	//   ensuring the DDA always starts inside a valid voxel row.
	//   Upper bound (box_max − 1e-4): prevents floor(ray_origin) from reaching
	//   model_size (e.g. floor(32.0) = 32), which is out of bounds.  The 1e-4
	//   margin keeps floor in the last valid voxel [model_size − 1].
	//   Axis-aligned clamping is used rather than a uniform t-space nudge because
	//   a directional nudge (ray_dir * epsilon) can shift coordinates in all three
	//   axes simultaneously, potentially landing on the wrong side of an interior
	//   voxel boundary in a non-primary axis.
	vec3 ray_origin = clamp(
		camera_voxel + ray_dir * max(t_enter, 0.0),
		vec3(0.0), box_max - vec3(1e-4));

	vec3 hit_pos_dda;
	vec4 color = raymarch(ray_origin, ray_dir, hit_pos_dda);
	if (color.a < 0.5) discard;
	ALBEDO = color.rgb;

	// ---- Write correct depth for the hit voxel surface ----
	// Without this, the depth buffer stores the billboard quad's flat depth.
	// On the Meta Quest 3 (and any OpenXR compositor using depth for ATW/ASW
	// reprojection), wrong depth causes the compositor to treat the weapon as a
	// flat plane and warp it as such, cancelling the per-eye stereo parallax that
	// the DDA correctly computed.
	//
	// Reverse transformation chain:  DDA voxel space → mesh local → world → clip
	//   1. Undo half_size shift: back to swizzled mesh-local space centred at origin.
	//   2. Undo Ry(+π/2) swizzle (forward: (x,y,z)→(z,y,−x); inverse: (x,y,z)→(−z,y,x)).
	//   3. MODEL_MATRIX → world space.
	//   4. PROJECTION_MATRIX * VIEW_MATRIX → clip space (per-eye in VR multiview).
	vec3 hit_shifted = hit_pos_dda - half_size;
	vec3 hit_local = vec3(-hit_shifted.z, hit_shifted.y, hit_shifted.x);
	vec4 hit_clip = PROJECTION_MATRIX * VIEW_MATRIX * MODEL_MATRIX * vec4(hit_local, 1.0);
	DEPTH = hit_clip.z / hit_clip.w;
}
