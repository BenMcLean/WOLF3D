using BenMcLean.Wolf3D.Assets.Gameplay;
using BenMcLean.Wolf3D.Assets.Graphics;
using Godot;
using System;
using System.Collections.Generic;

namespace BenMcLean.Wolf3D.VR;

/// <summary>
/// Pixel-perfect aiming system that casts rays from the camera and performs
/// pixel-level transparency checks on billboards.
/// Can optionally show a red sphere at the intersection point.
/// Supports multiple instances (e.g., one per VR controller).
/// </summary>
public partial class PixelPerfectAiming : Node3D
{
	private Camera3D _camera;
	private MeshInstance3D _aimPoint;
	private bool _showAimIndicator;
	private MapAnalyzer.MapAnalysis _mapAnalysis;
	private Walls _walls;
	private Doors _doors;
	private SimulatorController _simulatorController;
	private Actors _actors;
	private Fixtures _fixtures;
	private Bonuses _bonuses;
	private IReadOnlyDictionary<ushort, StandardMaterial3D> _spriteMaterials;
	private Dictionary<StandardMaterial3D, ushort> _materialToPage;
	private VSwap _vswap;
	private float _maxRayDistance = 64f * Constants.TileWidth; // Maximum ray distance (64 tiles)

	/// <summary>
	/// The most recent raycast result. Updated every frame.
	/// </summary>
	public AimHitResult CurrentHit { get; private set; }

	/// <summary>
	/// Result of a raycast operation.
	/// </summary>
	private struct RayHit
	{
		public Vector3 Position;
		public float Distance;
		public bool IsHit;
		public HitType Type;
		public int ActorIndex; // Only valid if Type == HitType.Actor
	}

	/// <summary>
	/// Public result of an aiming raycast, including what type of object was hit.
	/// </summary>
	public struct AimHitResult
	{
		/// <summary>True if the ray hit something</summary>
		public bool IsHit { get; init; }
		/// <summary>World position of the hit point</summary>
		public Vector3 Position { get; init; }
		/// <summary>Distance to the hit point</summary>
		public float Distance { get; init; }
		/// <summary>Type of object that was hit</summary>
		public HitType Type { get; init; }
		/// <summary>Actor index (only valid if Type == HitType.Actor)</summary>
		public int ActorIndex { get; init; }
	}

	/// <summary>
	/// Type of object hit by the raycast.
	/// </summary>
	public enum HitType
	{
		None,
		Wall,
		Door,
		PushWall,
		Actor,     // Opaque pixel on actor sprite
		Fixture,   // Opaque pixel on fixture sprite
		Bonus      // Opaque pixel on bonus sprite
	}

	/// <summary>
	/// Creates the pixel-perfect aiming system.
	/// </summary>
	/// <param name="camera">Camera to cast rays from</param>
	/// <param name="mapAnalysis">Map data for wall/door collision</param>
	/// <param name="walls">Walls instance for collision checks</param>
	/// <param name="doors">Doors instance for collision checks</param>
	/// <param name="simulatorController">Simulator controller for door/pushwall state</param>
	/// <param name="actors">Actors instance for billboard collision</param>
	/// <param name="fixtures">Fixtures instance for billboard collision</param>
	/// <param name="bonuses">Bonuses instance for billboard collision</param>
	/// <param name="spriteMaterials">Sprite materials for texture access</param>
	/// <param name="showAimIndicator">If true, shows a red sphere at the hit point</param>
	public PixelPerfectAiming(
		Camera3D camera,
		MapAnalyzer.MapAnalysis mapAnalysis,
		Walls walls,
		Doors doors,
		SimulatorController simulatorController,
		Actors actors,
		Fixtures fixtures,
		Bonuses bonuses,
		IReadOnlyDictionary<ushort, StandardMaterial3D> spriteMaterials,
		bool showAimIndicator = true)
	{
		_camera = camera ?? throw new ArgumentNullException(nameof(camera));
		_mapAnalysis = mapAnalysis ?? throw new ArgumentNullException(nameof(mapAnalysis));
		_walls = walls;
		_doors = doors;
		_simulatorController = simulatorController ?? throw new ArgumentNullException(nameof(simulatorController));
		_actors = actors ?? throw new ArgumentNullException(nameof(actors));
		_fixtures = fixtures ?? throw new ArgumentNullException(nameof(fixtures));
		_bonuses = bonuses ?? throw new ArgumentNullException(nameof(bonuses));
		_spriteMaterials = spriteMaterials ?? throw new ArgumentNullException(nameof(spriteMaterials));
		_vswap = Shared.SharedAssetManager.CurrentGame?.VSwap ?? throw new InvalidOperationException("VSwap not loaded");
		_showAimIndicator = showAimIndicator;

		// Create reverse lookup from material to page number for pixel transparency checks
		_materialToPage = new Dictionary<StandardMaterial3D, ushort>();
		foreach (KeyValuePair<ushort, StandardMaterial3D> kvp in _spriteMaterials)
			_materialToPage[kvp.Value] = kvp.Key;

		// Create aim indicator if requested
		if (_showAimIndicator)
			CreateAimPoint();

		// Initialize with no hit
		CurrentHit = new AimHitResult { IsHit = false };
	}

	/// <summary>
	/// Creates the red sphere that marks the aim point.
	/// </summary>
	private void CreateAimPoint()
	{
		SphereMesh sphereMesh = new()
		{
			Radius = 0.05f, // 5cm sphere
			Height = 0.1f,
			RadialSegments = 8,
			Rings = 4,
		};

		StandardMaterial3D material = new()
		{
			AlbedoColor = Colors.Red,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		};

		_aimPoint = new MeshInstance3D
		{
			Mesh = sphereMesh,
			MaterialOverride = material,
			Name = "AimPoint",
			Visible = false, // Hidden until we have a hit
		};

		AddChild(_aimPoint);
	}

	/// <summary>
	/// Updates the aim point position each frame by raycasting from the camera.
	/// </summary>
	public override void _Process(double delta)
	{
		RayHit hit = PerformRaycast();

		// Update public hit result
		CurrentHit = new AimHitResult
		{
			IsHit = hit.IsHit,
			Position = hit.Position,
			Distance = hit.Distance,
			Type = hit.Type,
			ActorIndex = hit.ActorIndex
		};

		// Update visual indicator if enabled
		if (_showAimIndicator && _aimPoint != null)
		{
			if (hit.IsHit)
			{
				_aimPoint.Position = hit.Position;
				_aimPoint.Visible = true;
			}
			else
			{
				_aimPoint.Visible = false;
			}
		}
	}

	/// <summary>
	/// Performs a raycast from the camera forward, checking walls, doors, and billboards
	/// with pixel-perfect transparency detection.
	/// </summary>
	private RayHit PerformRaycast()
	{
		Vector3 rayOrigin = _camera.GlobalPosition;
		Vector3 rayDirection = -_camera.GlobalTransform.Basis.Z.Normalized(); // Forward direction (normalized)

		// Check solid objects first (walls, closed doors, pushwalls)
		RayHit wallHit = RaycastWalls(rayOrigin, rayDirection);
		RayHit doorHit = RaycastDoors(rayOrigin, rayDirection);
		RayHit pushWallHit = RaycastPushWalls(rayOrigin, rayDirection);

		// Find closest solid hit
		RayHit closestHit = new() { IsHit = false, Distance = float.MaxValue };

		if (wallHit.IsHit && wallHit.Distance < closestHit.Distance)
			closestHit = wallHit;

		if (doorHit.IsHit && doorHit.Distance < closestHit.Distance)
			closestHit = doorHit;

		if (pushWallHit.IsHit && pushWallHit.Distance < closestHit.Distance)
			closestHit = pushWallHit;

		// Check billboards with pixel-perfect detection
		// Only check billboards closer than the wall/door hit
		float maxBillboardDistance = closestHit.IsHit ? closestHit.Distance : _maxRayDistance;
		RayHit billboardHit = RaycastBillboards(rayOrigin, rayDirection, maxBillboardDistance);

		if (billboardHit.IsHit && billboardHit.Distance < closestHit.Distance)
			closestHit = billboardHit;

		return closestHit;
	}

	/// <summary>
	/// Raycasts against walls using a DDA-style grid traversal algorithm.
	/// Based on the original Wolfenstein 3D raycasting algorithm.
	/// </summary>
	private RayHit RaycastWalls(Vector3 rayOrigin, Vector3 rayDirection)
	{
		// Convert ray origin to tile coordinates
		float currentX = rayOrigin.X / Constants.TileWidth;
		float currentZ = rayOrigin.Z / Constants.TileWidth;

		// DDA algorithm setup
		int mapX = (int)currentX;
		int mapZ = (int)currentZ;

		// Ray direction in tile space
		float deltaDistX = rayDirection.X == 0 ? float.MaxValue : Mathf.Abs(1f / rayDirection.X);
		float deltaDistZ = rayDirection.Z == 0 ? float.MaxValue : Mathf.Abs(1f / rayDirection.Z);

		int stepX, stepZ;
		float sideDistX, sideDistZ;

		// Calculate step direction and initial side distances
		if (rayDirection.X < 0)
		{
			stepX = -1;
			sideDistX = (currentX - mapX) * deltaDistX;
		}
		else
		{
			stepX = 1;
			sideDistX = (mapX + 1f - currentX) * deltaDistX;
		}

		if (rayDirection.Z < 0)
		{
			stepZ = -1;
			sideDistZ = (currentZ - mapZ) * deltaDistZ;
		}
		else
		{
			stepZ = 1;
			sideDistZ = (mapZ + 1f - currentZ) * deltaDistZ;
		}

		// Perform DDA
		bool hit = false;
		bool hitVertical = false; // true if we hit a vertical wall (X-aligned)
		int maxSteps = 100; // Prevent infinite loops
		int steps = 0;

		while (!hit && steps < maxSteps)
		{
			// Jump to next grid square
			if (sideDistX < sideDistZ)
			{
				sideDistX += deltaDistX;
				mapX += stepX;
				hitVertical = true;
			}
			else
			{
				sideDistZ += deltaDistZ;
				mapZ += stepZ;
				hitVertical = false;
			}

			steps++;

			// Early exit: Check if ray has left vertical bounds
			// Calculate approximate distance traveled and resulting Y position
			float traveledDist = hitVertical ? sideDistX : sideDistZ;
			float rayY = rayOrigin.Y + rayDirection.Y * traveledDist;
			if (rayY < 0 || rayY > Constants.TileHeight)
				break; // Ray is outside playable vertical space, can't hit anything

			// Check if we're out of bounds
			if (mapX < 0 || mapZ < 0 || mapX >= _mapAnalysis.Width || mapZ >= _mapAnalysis.Depth)
				break;

			// Check if this tile blocks vision (is not transparent)
			// In Wolf3D, non-transparent tiles are walls or solid obstacles
			// This excludes doors which have their own raycasting check
			if (!_mapAnalysis.IsTransparent(mapX, mapZ))
			{
				hit = true;
			}
		}

		if (!hit)
			return new RayHit { IsHit = false };

		// Calculate hit position with proper 3D ray intersection
		float perpWallDist;
		float hitX, hitZ;

		if (hitVertical)
		{
			// Hit a vertical wall (perpendicular to X axis)
			perpWallDist = (sideDistX - deltaDistX);
			hitX = mapX * Constants.TileWidth + (stepX > 0 ? 0 : Constants.TileWidth);
			// Calculate Z based on how far along X we traveled
			hitZ = rayOrigin.Z + (hitX - rayOrigin.X) * rayDirection.Z / rayDirection.X;
		}
		else
		{
			// Hit a horizontal wall (perpendicular to Z axis)
			perpWallDist = (sideDistZ - deltaDistZ);
			hitZ = mapZ * Constants.TileWidth + (stepZ > 0 ? 0 : Constants.TileWidth);
			// Calculate X based on how far along Z we traveled
			hitX = rayOrigin.X + (hitZ - rayOrigin.Z) * rayDirection.X / rayDirection.Z;
		}

		// Calculate the distance parameter t along the ray
		// Use the axis that changed to calculate t
		float t;
		if (hitVertical && Mathf.Abs(rayDirection.X) > 0.0001f)
			t = (hitX - rayOrigin.X) / rayDirection.X;
		else if (Mathf.Abs(rayDirection.Z) > 0.0001f)
			t = (hitZ - rayOrigin.Z) / rayDirection.Z;
		else
			return new RayHit { IsHit = false }; // Shouldn't happen

		// Calculate Y using the ray parameter
		float hitY = rayOrigin.Y + rayDirection.Y * t;

		// Check if hit is within wall vertical bounds
		if (hitY < 0 || hitY > Constants.TileHeight)
			return new RayHit { IsHit = false }; // Hit above ceiling or below floor

		Vector3 hitPosition = new Vector3(hitX, hitY, hitZ);
		float distance = t; // Since rayDirection is normalized, t is the distance

		return new RayHit
		{
			IsHit = true,
			Position = hitPosition,
			Distance = distance,
			Type = HitType.Wall,
			ActorIndex = -1
		};
	}

	/// <summary>
	/// Raycasts against doors by checking each door's position.
	/// Doors are vertical or horizontal planes at specific tile positions.
	/// Partially open doors only block the portion still in the doorway.
	/// </summary>
	private RayHit RaycastDoors(Vector3 rayOrigin, Vector3 rayDirection)
	{
		if (_doors == null || _simulatorController?.Doors == null)
			return new RayHit { IsHit = false };

		// Early exit: If ray is already out of vertical bounds at origin, it can't hit doors
		if (rayOrigin.Y < 0 || rayOrigin.Y > Constants.TileHeight)
		{
			// Check if ray will ever re-enter bounds
			if (rayOrigin.Y < 0 && rayDirection.Y <= 0)
				return new RayHit { IsHit = false }; // Below floor, pointing down/horizontal
			if (rayOrigin.Y > Constants.TileHeight && rayDirection.Y >= 0)
				return new RayHit { IsHit = false }; // Above ceiling, pointing up/horizontal
		}

		RayHit closestHit = new() { IsHit = false, Distance = float.MaxValue };

		// Check each door in the simulator (not MapAnalysis - we need the live state)
		for (int i = 0; i < _simulatorController.Doors.Count; i++)
		{
			Simulator.Entities.Door door = _simulatorController.Doors[i];

			// Calculate how open the door is (0.0 = closed, 1.0 = fully open)
			// Door.Position: 0 = closed, 0xFFFF = fully open
			float openFraction = door.Position / 65535.0f;

			// Skip fully open doors - they're completely inside the wall
			if (openFraction >= 0.99f)
				continue;
			// Door position in world coordinates (center of tile)
			float doorWorldX = door.TileX.ToMetersCentered();
			float doorWorldZ = door.TileY.ToMetersCentered();

			// Check intersection with door plane
			Vector3 hitPosition;
			float t; // Ray parameter

			if (door.FacesEastWest)
			{
				// Door runs East-West (vertical plane in X), slides along +Z when opening
				if (Mathf.Abs(rayDirection.X) < 0.0001f)
					continue; // Ray parallel to door

				t = (doorWorldX - rayOrigin.X) / rayDirection.X;
				if (t < 0)
					continue; // Behind ray

				hitPosition = rayOrigin + rayDirection * t;

				// Calculate door extent based on how open it is
				// As door opens, it slides toward +Z direction into the wall
				// The door moves, so we need to track which part is still in the doorway
				// When closed (openFraction = 0): spans from doorWorldZ - halfWidth to doorWorldZ + halfWidth
				// When half open (openFraction = 0.5): spans from doorWorldZ to doorWorldZ + halfWidth
				// When fully open (openFraction = 1): spans from doorWorldZ + halfWidth to doorWorldZ + halfWidth (zero width)
				float halfWidth = Constants.HalfTileWidth;
				float tileWidth = Constants.TileWidth;

				// Door slides by offset = openFraction * tileWidth in +Z direction
				// The blocking portion is what's still in the original doorway
				float doorMinZ = doorWorldZ - halfWidth + openFraction * tileWidth;
				float doorMaxZ = doorWorldZ + halfWidth; // Right edge stays constant

				// Check if ray hits the portion of the door still in the doorway
				if (hitPosition.Z < doorMinZ || hitPosition.Z > doorMaxZ)
					continue; // Ray passes through the open part of the doorway
			}
			else
			{
				// Door runs North-South (vertical plane in Z), slides along +X when opening
				if (Mathf.Abs(rayDirection.Z) < 0.0001f)
					continue; // Ray parallel to door

				t = (doorWorldZ - rayOrigin.Z) / rayDirection.Z;
				if (t < 0)
					continue; // Behind ray

				hitPosition = rayOrigin + rayDirection * t;

				// Calculate door extent based on how open it is
				// As door opens, it slides toward +X direction into the wall
				float halfWidth = Constants.HalfTileWidth;
				float tileWidth = Constants.TileWidth;

				// Door slides by offset = openFraction * tileWidth in +X direction
				float doorMinX = doorWorldX - halfWidth + openFraction * tileWidth;
				float doorMaxX = doorWorldX + halfWidth; // Right edge stays constant

				// Check if ray hits the portion of the door still in the doorway
				if (hitPosition.X < doorMinX || hitPosition.X > doorMaxX)
					continue; // Ray passes through the open part of the doorway
			}

			// Check if hit is within vertical bounds
			if (hitPosition.Y < 0 || hitPosition.Y > Constants.TileHeight)
				continue;

			float distance = (hitPosition - rayOrigin).Length();

			if (distance < closestHit.Distance)
			{
				closestHit = new RayHit
				{
					IsHit = true,
					Position = hitPosition,
					Distance = distance,
					Type = HitType.Door,
					ActorIndex = -1
				};
			}
		}

		return closestHit;
	}

	/// <summary>
	/// Raycasts against pushwalls by checking each pushwall's current position.
	/// Pushwalls are 4-sided cubes that can move, so we check all 4 faces.
	/// </summary>
	private RayHit RaycastPushWalls(Vector3 rayOrigin, Vector3 rayDirection)
	{
		if (_simulatorController?.PushWalls == null)
			return new RayHit { IsHit = false };

		// Early exit: If ray is already out of vertical bounds at origin, it can't hit pushwalls
		if (rayOrigin.Y < 0 || rayOrigin.Y > Constants.TileHeight)
		{
			// Check if ray will ever re-enter bounds
			if (rayOrigin.Y < 0 && rayDirection.Y <= 0)
				return new RayHit { IsHit = false }; // Below floor, pointing down/horizontal
			if (rayOrigin.Y > Constants.TileHeight && rayDirection.Y >= 0)
				return new RayHit { IsHit = false }; // Above ceiling, pointing up/horizontal
		}

		RayHit closestHit = new() { IsHit = false, Distance = float.MaxValue };

		// Check each pushwall in the simulator
		for (int i = 0; i < _simulatorController.PushWalls.Count; i++)
		{
			Simulator.Entities.PushWall pushWall = _simulatorController.PushWalls[i];

			// Get pushwall's current position in world coordinates (from 16.16 fixed-point)
			float pushWallX = pushWall.X * Constants.FixedPointToMeters;
			float pushWallZ = pushWall.Y * Constants.FixedPointToMeters;

			// Pushwall is a cube - check all 4 vertical faces
			// Each face is Constants.TileWidth wide and Constants.TileHeight tall

			float halfWidth = Constants.HalfTileWidth;
			float height = Constants.TileHeight;

			// Check all 4 faces and find closest hit
			RayHit[] faceHits = new RayHit[4];

			// East face (positive X)
			faceHits[0] = RaycastPushWallFace(rayOrigin, rayDirection, pushWallX + halfWidth, pushWallZ, true, height);

			// West face (negative X)
			faceHits[1] = RaycastPushWallFace(rayOrigin, rayDirection, pushWallX - halfWidth, pushWallZ, true, height);

			// South face (positive Z)
			faceHits[2] = RaycastPushWallFace(rayOrigin, rayDirection, pushWallX, pushWallZ + halfWidth, false, height);

			// North face (negative Z)
			faceHits[3] = RaycastPushWallFace(rayOrigin, rayDirection, pushWallX, pushWallZ - halfWidth, false, height);

			// Find closest face hit
			foreach (RayHit hit in faceHits)
			{
				if (hit.IsHit && hit.Distance < closestHit.Distance)
					closestHit = hit;
			}
		}

		return closestHit;
	}

	/// <summary>
	/// Raycasts against a single face of a pushwall cube.
	/// </summary>
	/// <param name="rayOrigin">Ray origin</param>
	/// <param name="rayDirection">Ray direction (normalized)</param>
	/// <param name="faceX">X coordinate of face center</param>
	/// <param name="faceZ">Z coordinate of face center</param>
	/// <param name="isVerticalInX">True if face is perpendicular to X axis, false if perpendicular to Z</param>
	/// <param name="height">Height of the face</param>
	private RayHit RaycastPushWallFace(Vector3 rayOrigin, Vector3 rayDirection, float faceX, float faceZ, bool isVerticalInX, float height)
	{
		Vector3 hitPosition;
		float t;

		if (isVerticalInX)
		{
			// Face perpendicular to X axis
			if (Mathf.Abs(rayDirection.X) < 0.0001f)
				return new RayHit { IsHit = false }; // Ray parallel to face

			t = (faceX - rayOrigin.X) / rayDirection.X;
			if (t < 0)
				return new RayHit { IsHit = false }; // Behind ray

			hitPosition = rayOrigin + rayDirection * t;

			// Check if hit is within face bounds (Z direction)
			if (Mathf.Abs(hitPosition.Z - faceZ) > Constants.HalfTileWidth)
				return new RayHit { IsHit = false }; // Outside face bounds in Z
		}
		else
		{
			// Face perpendicular to Z axis
			if (Mathf.Abs(rayDirection.Z) < 0.0001f)
				return new RayHit { IsHit = false }; // Ray parallel to face

			t = (faceZ - rayOrigin.Z) / rayDirection.Z;
			if (t < 0)
				return new RayHit { IsHit = false }; // Behind ray

			hitPosition = rayOrigin + rayDirection * t;

			// Check if hit is within face bounds (X direction)
			if (Mathf.Abs(hitPosition.X - faceX) > Constants.HalfTileWidth)
				return new RayHit { IsHit = false }; // Outside face bounds in X
		}

		// Check if hit is within vertical bounds
		if (hitPosition.Y < 0 || hitPosition.Y > height)
			return new RayHit { IsHit = false }; // Outside face bounds in Y

		float distance = t; // rayDirection is normalized

		return new RayHit
		{
			IsHit = true,
			Position = hitPosition,
			Distance = distance,
			Type = HitType.PushWall,
			ActorIndex = -1
		};
	}

	/// <summary>
	/// Raycasts against billboards (actors, fixtures, bonuses) with pixel-perfect transparency detection.
	/// Billboards always face the camera, so we need to account for their rotation.
	/// </summary>
	private RayHit RaycastBillboards(Vector3 rayOrigin, Vector3 rayDirection, float maxDistance)
	{
		// Early exit: If ray is already out of vertical bounds at origin, it can't hit billboards
		if (rayOrigin.Y < 0 || rayOrigin.Y > Constants.TileHeight)
		{
			// Check if ray will ever re-enter bounds
			if (rayOrigin.Y < 0 && rayDirection.Y <= 0)
				return new RayHit { IsHit = false }; // Below floor, pointing down/horizontal
			if (rayOrigin.Y > Constants.TileHeight && rayDirection.Y >= 0)
				return new RayHit { IsHit = false }; // Above ceiling, pointing up/horizontal
		}

		// Early exit: If ray at maxDistance is still out of bounds (and won't re-enter)
		float rayYAtMaxDist = rayOrigin.Y + rayDirection.Y * maxDistance;
		if ((rayOrigin.Y < 0 && rayYAtMaxDist < 0) || (rayOrigin.Y > Constants.TileHeight && rayYAtMaxDist > Constants.TileHeight))
			return new RayHit { IsHit = false }; // Ray never enters vertical bounds

		RayHit closestHit = new() { IsHit = false, Distance = maxDistance };

		// Check actors
		closestHit = CheckBillboardCollection(_actors, rayOrigin, rayDirection, closestHit, HitType.Actor);

		// Check fixtures
		closestHit = CheckBillboardCollection(_fixtures, rayOrigin, rayDirection, closestHit, HitType.Fixture);

		// Check bonuses
		closestHit = CheckBillboardCollection(_bonuses, rayOrigin, rayDirection, closestHit, HitType.Bonus);

		return closestHit;
	}

	/// <summary>
	/// Checks a collection of billboards for ray intersections with pixel-perfect transparency.
	/// </summary>
	private RayHit CheckBillboardCollection(Node3D billboardParent, Vector3 rayOrigin, Vector3 rayDirection, RayHit currentClosest, HitType hitType)
	{
		if (billboardParent == null)
			return currentClosest;

		// Billboard normal is the camera's forward direction projected onto the XZ plane
		// Billboards are always vertical and face the camera (rotating around Y axis only)
		// We project the camera's forward direction onto the horizontal plane to get the normal
		Vector3 cameraForward = -_camera.GlobalTransform.Basis.Z;
		Vector3 billboardNormal = new Vector3(cameraForward.X, 0, cameraForward.Z).Normalized();

		// If the camera is looking straight up or down, use a default horizontal normal
		if (billboardNormal.Length() < 0.0001f)
			billboardNormal = new Vector3(0, 0, 1);

		// Iterate through all billboard instances
		// For actors (MeshInstance3D), we track actor index for hit detection
		int actorIndex = 0;
		foreach (Node child in billboardParent.GetChildren())
		{
			if (child is MeshInstance3D billboard)
			{
				RayHit hit = CheckBillboard(billboard, rayOrigin, rayDirection, billboardNormal, currentClosest.Distance, hitType, actorIndex);
				if (hit.IsHit && hit.Distance < currentClosest.Distance)
					currentClosest = hit;
				actorIndex++; // Increment for next actor
			}
			else if (child is MultiMeshInstance3D multiMesh)
			{
				// Check each instance in the MultiMesh
				for (int i = 0; i < multiMesh.Multimesh.InstanceCount; i++)
				{
					Transform3D instanceTransform = multiMesh.Multimesh.GetInstanceTransform(i);
					Vector3 billboardPosition = instanceTransform.Origin;

					// Get material for this multimesh to extract sprite page
					StandardMaterial3D material = multiMesh.MaterialOverride as StandardMaterial3D;
					if (material == null)
						continue;

					RayHit hit = CheckBillboardAtPosition(
						billboardPosition,
						material,
						rayOrigin,
						rayDirection,
						billboardNormal,
						currentClosest.Distance,
						hitType,
						-1); // MultiMesh instances (fixtures/bonuses) don't have individual indices

					if (hit.IsHit && hit.Distance < currentClosest.Distance)
						currentClosest = hit;
				}
			}
		}

		return currentClosest;
	}

	/// <summary>
	/// Checks a single billboard (MeshInstance3D) for ray intersection with pixel-perfect detection.
	/// </summary>
	private RayHit CheckBillboard(MeshInstance3D billboard, Vector3 rayOrigin, Vector3 rayDirection, Vector3 billboardNormal, float maxDistance, HitType hitType, int actorIndex)
	{
		Vector3 billboardPosition = billboard.GlobalPosition;
		StandardMaterial3D material = billboard.MaterialOverride as StandardMaterial3D;

		if (material == null)
			return new RayHit { IsHit = false };

		return CheckBillboardAtPosition(billboardPosition, material, rayOrigin, rayDirection, billboardNormal, maxDistance, hitType, actorIndex);
	}

	/// <summary>
	/// Checks billboard at a specific position with pixel-perfect transparency detection.
	/// </summary>
	private RayHit CheckBillboardAtPosition(
		Vector3 billboardPosition,
		StandardMaterial3D material,
		Vector3 rayOrigin,
		Vector3 rayDirection,
		Vector3 billboardNormal,
		float maxDistance,
		HitType hitType,
		int actorIndex)
	{
		// Check intersection with billboard plane
		float denom = rayDirection.Dot(billboardNormal);
		if (Mathf.Abs(denom) < 0.0001f)
			return new RayHit { IsHit = false }; // Ray parallel to billboard

		float t = (billboardPosition - rayOrigin).Dot(billboardNormal) / denom;
		// Since rayDirection is normalized, t represents the actual distance along the ray
		if (t < 0 || t > maxDistance)
			return new RayHit { IsHit = false }; // Behind ray or too far

		Vector3 hitPosition = rayOrigin + rayDirection * t;
		float distance = t; // rayDirection is normalized, so t equals the distance

		// Convert hit position to billboard local coordinates
		// Billboard is aligned with camera, so we need to find the offset from center
		Vector3 offset = hitPosition - billboardPosition;

		// Calculate right vector (perpendicular to normal, in XZ plane)
		Vector3 billboardRight = new Vector3(-billboardNormal.Z, 0, billboardNormal.X).Normalized();
		Vector3 billboardUp = Vector3.Up;

		// Project offset onto billboard coordinate system
		float localX = offset.Dot(billboardRight);
		float localY = offset.Dot(billboardUp);

		// Check if hit is within billboard bounds
		if (Mathf.Abs(localX) > Constants.HalfTileWidth || Mathf.Abs(localY) > Constants.HalfTileHeight)
			return new RayHit { IsHit = false };

		// Convert to texture coordinates (0-1 range)
		// Billboard UVs go from 0 to 1, with (0,0) at bottom-left
		float u = (localX + Constants.HalfTileWidth) / Constants.TileWidth;
		float v = 1f - (localY + Constants.HalfTileHeight) / Constants.TileHeight; // Flip V

		// Get page number for this material
		if (!_materialToPage.TryGetValue(material, out ushort pageNumber))
			return new RayHit { IsHit = false }; // Material not found, treat as miss

		// Convert UV to pixel coordinates in original sprite space (before upscaling)
		// Sprites are TileSqrt x TileSqrt (usually 64x64)
		int pixelX = Mathf.Clamp((int)(u * _vswap.TileSqrt), 0, _vswap.TileSqrt - 1);
		int pixelY = Mathf.Clamp((int)(v * _vswap.TileSqrt), 0, _vswap.TileSqrt - 1);

		// Check pixel transparency using VSwap's pre-computed masks
		// Note: VSwap.IsTransparent returns true for OPAQUE pixels (alpha > 128)
		// despite the confusing method name - likely due to Wolf3D's alpha channel convention
		if (!_vswap.IsTransparent(pageNumber, (ushort)pixelX, (ushort)pixelY))
			return new RayHit { IsHit = false }; // Transparent pixel (IsTransparent=false), ray continues

		// Opaque pixel (IsTransparent=true), we have a hit
		return new RayHit
		{
			IsHit = true,
			Position = hitPosition,
			Distance = distance,
			Type = hitType,
			ActorIndex = actorIndex
		};
	}
}
