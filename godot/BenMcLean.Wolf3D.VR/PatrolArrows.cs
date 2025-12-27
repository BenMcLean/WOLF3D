using BenMcLean.Wolf3D.Assets;
using Godot;
using System.Collections.Generic;
using static BenMcLean.Wolf3D.Assets.MapAnalyzer;
using static BenMcLean.Wolf3D.Assets.MapAnalyzer.MapAnalysis;

namespace BenMcLean.Wolf3D.VR;

/// <summary>
/// Debug visualization for patrol points in a Wolfenstein 3D map.
/// Renders wireframe directional arrows at each patrol tile to show turn directions.
/// This is a debugging tool - should not be used in production.
/// </summary>
public partial class PatrolArrows : Node3D
{
	private readonly List<MeshInstance3D> arrowNodes = [];
	private static readonly Color ArrowColor = new(1f, 1f, 0f, 1f); // Yellow wireframe

	/// <summary>
	/// Creates wireframe arrows for all patrol points in the map.
	/// </summary>
	/// <param name="mapAnalysis">Map analysis containing patrol point data</param>
	public PatrolArrows(MapAnalysis mapAnalysis)
	{
		if (mapAnalysis?.PatrolPoints is null)
			return;

		foreach (PatrolPoint point in mapAnalysis.PatrolPoints)
		{
			MeshInstance3D arrowNode = CreateArrowMesh(point);
			AddChild(arrowNode);
			arrowNodes.Add(arrowNode);
		}
	}

	/// <summary>
	/// Creates a wireframe arrow mesh for a patrol point.
	/// The arrow points in the direction of the turn.
	/// </summary>
	private static MeshInstance3D CreateArrowMesh(PatrolPoint point)
	{
		ImmediateMesh mesh = new();
		StandardMaterial3D material = new()
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = ArrowColor,
			Transparency = BaseMaterial3D.TransparencyEnum.Disabled,
		};

		// Calculate arrow position (centered on tile, just above floor for visibility)
		Vector3 position = new(
			point.X.ToMetersCentered(),
			Constants.PixelHeight, // Just above floor
			point.Y.ToMetersCentered()
		);

		// Calculate arrow rotation based on turn direction
		// Direction enum in Godot VR coordinates:
		// E (Wolf3D +X) → Godot +X, N (Wolf3D +Y) → Godot +Z
		// W (Wolf3D -X) → Godot -X, S (Wolf3D -Y) → Godot -Z
		// Arrow mesh points in +X by default (East), rotations are counter-clockwise from there
		float rotationY = point.Turn switch
		{
			Direction.E => 0f,                          // East: 0° (points +X in Godot)
			Direction.NE => Constants.QuarterPi,              // Northeast: 45°
			Direction.N => Constants.HalfPi,               // North: 90° (points +Z in Godot)
			Direction.NW => 3f * Constants.QuarterPi,         // Northwest: 135°
			Direction.W => Mathf.Pi,                    // West: 180° (points -X in Godot)
			Direction.SW => -3f * Constants.QuarterPi,        // Southwest: -135° (or 225°)
			Direction.S => -Constants.HalfPi,              // South: -90° (points -Z in Godot)
			Direction.SE => -Constants.QuarterPi,             // Southeast: -45° (or 315°)
			_ => 0f
		};

		// Build arrow geometry using ImmediateMesh
		mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, material);

		// Arrow dimensions (in meters)
		const float arrowLength = Constants.TileWidth * 0.6f,  // 60% of tile width
			arrowHeadLength = Constants.TileWidth * 0.2f,
			arrowHeadWidth = Constants.TileWidth * 0.15f;

		// Main arrow shaft (line from back to front)
		Vector3 backPoint = new(-arrowLength / 2f, 0f, 0f);
		Vector3 frontPoint = new(arrowLength / 2f, 0f, 0f);
		mesh.SurfaceAddVertex(backPoint);
		mesh.SurfaceAddVertex(frontPoint);

		// Arrow head (two lines forming a >)
		Vector3 headLeft = frontPoint - new Vector3(arrowHeadLength, 0f, arrowHeadWidth);
		Vector3 headRight = frontPoint - new Vector3(arrowHeadLength, 0f, -arrowHeadWidth);

		// Left arrowhead line
		mesh.SurfaceAddVertex(frontPoint);
		mesh.SurfaceAddVertex(headLeft);

		// Right arrowhead line
		mesh.SurfaceAddVertex(frontPoint);
		mesh.SurfaceAddVertex(headRight);

		mesh.SurfaceEnd();

		// Create MeshInstance3D with the arrow
		MeshInstance3D arrowNode = new()
		{
			Mesh = mesh,
			Position = position,
			Rotation = new Vector3(0f, rotationY, 0f),
			Name = $"PatrolArrow_{point.X}_{point.Y}_{point.Turn}"
		};

		return arrowNode;
	}

	/// <summary>
	/// Shows all patrol arrows (enables visibility).
	/// </summary>
	public void Show()
	{
		Visible = true;
	}

	/// <summary>
	/// Hides all patrol arrows (disables visibility).
	/// </summary>
	public void Hide()
	{
		Visible = false;
	}
}
