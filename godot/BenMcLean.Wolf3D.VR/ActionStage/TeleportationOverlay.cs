using System;
using System.Collections.Generic;
using Godot;

namespace BenMcLean.Wolf3D.VR.ActionStage;

/// <summary>
/// VR teleportation visual overlay.
/// Draws a parabolic arc from the right controller to the targeted destination tile,
/// and a wireframe circle on that tile.
/// Green = navigable destination. Red = blocked or out-of-bounds destination.
///
/// Arc origin: right controller world position.
/// Arc direction: right controller forward direction.
/// Accessibility check: destination tile's runtime navigability (closed doors, pushwalls, actors, etc.)
/// </summary>
public partial class TeleportationOverlay : Node3D
{
	private readonly MeshInstance3D _arcMesh = new() { Name = "TeleportArc" };
	private readonly MeshInstance3D _circleMesh = new() { Name = "TeleportCircle" };

	// Parabolic arc simulation parameters.
	// Half-gravity + higher launch speed gives a longer, more graceful arc suited to VR
	// teleportation, where the controller is typically ~1 m above the floor and aimed
	// roughly forward. Adjust these two constants to tune range and arc curvature.
	private const float ArcLaunchSpeed = 20f;   // m/s tangential launch speed
	private const float ArcGravity = -4.9f;     // m/s² downward acceleration (half of realistic)
	private const float ArcStepDt = 0.05f;      // seconds per simulation step
	private const int ArcMaxSteps = 120;         // max steps (120 × 0.05s = 6s of flight)
	private const float FloorY = 0f;             // Wolf3D floor is always at Y=0

	private const float CircleRadius = Constants.HalfTileWidth * 0.4f;
	private const int CircleSegments = 16;

	private static readonly Color GreenColor = new(0f, 1f, 0f, 1f);
	private static readonly Color RedColor = new(1f, 0f, 0f, 1f);

	/// <summary>
	/// The destination tile computed in the last UpdateOverlay call.
	/// Null if the arc did not land on the floor or landed out of bounds.
	/// X = Godot X = Wolf3D X; Z = Godot Z = Wolf3D Y.
	/// </summary>
	public (ushort X, ushort Z)? DestinationTile { get; private set; }

	/// <summary>
	/// True if the destination tile from the last UpdateOverlay call is navigable.
	/// </summary>
	public bool IsDestinationNavigable { get; private set; }

	public override void _Ready()
	{
		AddChild(_arcMesh);
		AddChild(_circleMesh);
		Visible = false;
	}

	/// <summary>
	/// Recalculates and redraws the teleportation arc and destination circle.
	/// Call this every frame while teleportation mode is active.
	/// </summary>
	/// <param name="controllerPos">Right controller world position (arc launch point)</param>
	/// <param name="controllerForward">Right controller forward direction (arc launch direction)</param>
	/// <param name="isTileNavigable">
	/// Callback returning true if the given tile (X, Z) is currently navigable.
	/// Should already incorporate map bounds checking.
	/// </param>
	public void UpdateOverlay(
		Vector3 controllerPos,
		Vector3 controllerForward,
		Func<ushort, ushort, bool> isTileNavigable)
	{
		List<Vector3> arcPoints = SimulateArc(controllerPos, controllerForward);

		// Need at least two points to draw a line, and the arc must have landed
		if (arcPoints.Count < 2 || arcPoints[^1].Y > FloorY)
		{
			HideOverlay();
			return;
		}

		Vector3 landing = arcPoints[^1];
		int tileXInt = landing.X.ToTile();
		int tileZInt = landing.Z.ToTile();

		if (tileXInt < 0 || tileZInt < 0)
		{
			HideOverlay();
			return;
		}

		ushort destTileX = (ushort)tileXInt;
		ushort destTileZ = (ushort)tileZInt;
		DestinationTile = (destTileX, destTileZ);

		bool navigable = isTileNavigable(destTileX, destTileZ);
		IsDestinationNavigable = navigable;

		Color color = navigable ? GreenColor : RedColor;

		// Snap arc endpoint to the tile center for a clean visual landing
		Vector3 tileCenter = new(destTileX.ToMetersCentered(), FloorY, destTileZ.ToMetersCentered());
		arcPoints[^1] = tileCenter;

		DrawArc(arcPoints, color);
		DrawCircle(tileCenter, color);

		Visible = true;
	}

	/// <summary>
	/// Hides the overlay and clears destination state.
	/// </summary>
	public void HideOverlay()
	{
		Visible = false;
		DestinationTile = null;
		IsDestinationNavigable = false;
	}

	/// <summary>
	/// Simulates a parabolic arc using Euler integration.
	/// Launches a virtual projectile from startPos in the forward direction,
	/// applying gravity each step, until it reaches FloorY or exhausts ArcMaxSteps.
	/// Returns the sampled world positions. The last point is at or below FloorY if the arc landed.
	/// </summary>
	private static List<Vector3> SimulateArc(Vector3 startPos, Vector3 forward)
	{
		List<Vector3> points = [startPos];

		Vector3 vel = forward.Normalized() * ArcLaunchSpeed;
		Vector3 pos = startPos;

		for (int i = 0; i < ArcMaxSteps; i++)
		{
			vel.Y += ArcGravity * ArcStepDt;
			pos += vel * ArcStepDt;

			if (pos.Y <= FloorY)
			{
				// Linearly interpolate to the exact floor crossing
				Vector3 prev = points[^1];
				float t = (FloorY - prev.Y) / (pos.Y - prev.Y);
				Vector3 floorCrossing = prev + (pos - prev) * t;
				floorCrossing.Y = FloorY;
				points.Add(floorCrossing);
				break;
			}

			points.Add(pos);
		}

		return points;
	}

	private void DrawArc(List<Vector3> arcPoints, Color color)
	{
		ImmediateMesh mesh = new();
		StandardMaterial3D material = new()
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = color,
			Transparency = BaseMaterial3D.TransparencyEnum.Disabled,
		};
		mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, material);
		for (int i = 0; i < arcPoints.Count - 1; i++)
		{
			mesh.SurfaceAddVertex(arcPoints[i]);
			mesh.SurfaceAddVertex(arcPoints[i + 1]);
		}
		mesh.SurfaceEnd();
		_arcMesh.Mesh = mesh;
	}

	private void DrawCircle(Vector3 center, Color color)
	{
		ImmediateMesh mesh = new();
		StandardMaterial3D material = new()
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = color,
			Transparency = BaseMaterial3D.TransparencyEnum.Disabled,
		};
		mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, material);
		for (int i = 0; i < CircleSegments; i++)
		{
			float angle0 = i * Mathf.Tau / CircleSegments;
			float angle1 = (i + 1) * Mathf.Tau / CircleSegments;
			Vector3 p0 = center + new Vector3(Mathf.Cos(angle0) * CircleRadius, 0f, Mathf.Sin(angle0) * CircleRadius);
			Vector3 p1 = center + new Vector3(Mathf.Cos(angle1) * CircleRadius, 0f, Mathf.Sin(angle1) * CircleRadius);
			mesh.SurfaceAddVertex(p0);
			mesh.SurfaceAddVertex(p1);
		}
		mesh.SurfaceEnd();
		_circleMesh.Mesh = mesh;
	}
}
