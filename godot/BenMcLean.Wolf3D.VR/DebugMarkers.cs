using BenMcLean.Wolf3D.Shared;
using Godot;
using System.Collections.Generic;
using System.Linq;
using static BenMcLean.Wolf3D.Assets.MapAnalyzer;
using static BenMcLean.Wolf3D.Assets.MapAnalyzer.MapAnalysis;

namespace BenMcLean.Wolf3D.VR;

/// <summary>
/// Debug visualization for special tiles in a Wolfenstein 3D map.
/// Renders wireframe directional arrows at patrol points and X markers at deaf/ambush tiles.
/// This is a debugging tool - should not be used in production.
/// </summary>
public partial class DebugMarkers : Node3D
{
	private readonly List<MeshInstance3D> markerNodes = [];
	private static readonly Color ArrowColor = Colors.Yellow,
		AmbushColor = Colors.Yellow,
		AltElevatorColor = Colors.Yellow;
	/// <summary>
	/// Creates wireframe arrows for patrol points and X markers for deaf/ambush actors.
	/// </summary>
	/// <param name="mapAnalysis">Map analysis containing patrol point and actor spawn data</param>
	public DebugMarkers(MapAnalysis mapAnalysis)
	{
		if (mapAnalysis is null)
			return;
		// Create patrol arrows
		if (mapAnalysis.PatrolPoints is not null)
			foreach (PatrolPoint point in mapAnalysis.PatrolPoints)
			{
				MeshInstance3D arrowNode = CreateArrowMesh(point);
				AddChild(arrowNode);
				markerNodes.Add(arrowNode);
			}
		// Create ambush X markers for ambush tiles
		if (mapAnalysis.Ambushes is not null)
			foreach (uint encoded in mapAnalysis.Ambushes)
			{
				MeshInstance3D xMarkerNode = CreateXMarkerMesh(
					x: (ushort)(encoded & 0xFFFF),
					y: (ushort)(encoded >> 16));
				AddChild(xMarkerNode);
				markerNodes.Add(xMarkerNode);
			}
		// Create ambush X markers for actors with ambush flag
		if (mapAnalysis.ActorSpawns is not null)
			foreach (ActorSpawn actor in mapAnalysis.ActorSpawns
				.Where(actor => actor.Ambush))
			{
				MeshInstance3D xMarkerNode = CreateXMarkerMesh(
					x: actor.X,
					y: actor.Y,
					label: actor.ActorType);
				AddChild(xMarkerNode);
				markerNodes.Add(xMarkerNode);
			}
		// Create diamond markers for alternate elevators
		if (mapAnalysis.AltElevators is not null)
			foreach (uint encoded in mapAnalysis.AltElevators)
			{
				MeshInstance3D diamondNode = CreateDiamondMesh(
					x: (ushort)(encoded & 0xFFFF),
					y: (ushort)(encoded >> 16));
				AddChild(diamondNode);
				markerNodes.Add(diamondNode);
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
			x: point.X.ToMetersCentered(),
			y: Constants.PixelHeight,
			z: point.Y.ToMetersCentered());
		// Calculate arrow rotation based on turn direction
		// ToAngle() returns facing angles (for cameras/actors looking in a direction)
		// Arrows point in a direction, so we negate to convert facing → pointing
		float rotationY = -point.Turn.ToAngle();
		// Build arrow geometry using ImmediateMesh
		mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, material);
		// Arrow dimensions (in meters)
		const float arrowLength = Constants.TileWidth * 0.6f,  // 60% of tile width
			arrowHeadLength = Constants.TileWidth * 0.2f,
			arrowHeadWidth = Constants.TileWidth * 0.15f;
		// Main arrow shaft (line from back to front)
		Vector3 backPoint = new(-arrowLength / 2f, 0f, 0f),
			frontPoint = new(arrowLength / 2f, 0f, 0f);
		mesh.SurfaceAddVertex(backPoint);
		mesh.SurfaceAddVertex(frontPoint);
		// Arrow head (two lines forming a >)
		Vector3 headLeft = frontPoint - new Vector3(arrowHeadLength, 0f, arrowHeadWidth),
			headRight = frontPoint - new Vector3(arrowHeadLength, 0f, -arrowHeadWidth);
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
			Name = $"PatrolArrow_{point.X}_{point.Y}_{point.Turn}",
		};
		return arrowNode;
	}
	/// <summary>
	/// Creates a horizontal wireframe X marker for a deaf/ambush tile or actor.
	/// The X is positioned at Constants.PixelHeight above the floor.
	/// </summary>
	private static MeshInstance3D CreateXMarkerMesh(ushort x, ushort y, string label = "Tile")
	{
		ImmediateMesh mesh = new();
		StandardMaterial3D material = new()
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = AmbushColor,
			Transparency = BaseMaterial3D.TransparencyEnum.Disabled,
		};
		// Calculate X marker position (centered on tile, at PixelHeight)
		Vector3 position = new(
			x: x.ToMetersCentered(),
			y: Constants.PixelHeight,
			z: y.ToMetersCentered());
		// Build X geometry using ImmediateMesh
		mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, material);
		// X dimensions (in meters) - horizontal X
		const float halfSize = Constants.HalfTileWidth / 2f;
		// First diagonal (from top-left to bottom-right in XZ plane)
		Vector3 topLeft = new(-halfSize, 0f, -halfSize),
			bottomRight = new(halfSize, 0f, halfSize);
		mesh.SurfaceAddVertex(topLeft);
		mesh.SurfaceAddVertex(bottomRight);
		// Second diagonal (from top-right to bottom-left in XZ plane)
		Vector3 topRight = new(halfSize, 0f, -halfSize),
			bottomLeft = new(-halfSize, 0f, halfSize);
		mesh.SurfaceAddVertex(topRight);
		mesh.SurfaceAddVertex(bottomLeft);
		mesh.SurfaceEnd();
		// Create MeshInstance3D with the X marker
		MeshInstance3D xMarkerNode = new()
		{
			Mesh = mesh,
			Position = position,
			Name = $"AmbushMarker_{x}_{y}_{label}"
		};
		return xMarkerNode;
	}
	/// <summary>
	/// Creates a horizontal wireframe diamond (◊) marker for an alternate elevator tile.
	/// The diamond is positioned at Constants.PixelHeight above the floor.
	/// </summary>
	private static MeshInstance3D CreateDiamondMesh(ushort x, ushort y)
	{
		ImmediateMesh mesh = new();
		StandardMaterial3D material = new()
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = AltElevatorColor,
			Transparency = BaseMaterial3D.TransparencyEnum.Disabled,
		};
		// Calculate diamond position (centered on tile, at PixelHeight)
		Vector3 position = new(
			x: x.ToMetersCentered(),
			y: Constants.PixelHeight,
			z: y.ToMetersCentered());
		// Build diamond geometry using ImmediateMesh
		mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, material);
		// Diamond dimensions (in meters) - horizontal in XZ plane
		const float size = Constants.HalfTileWidth / 2f;
		// Diamond vertices (4 points: North, East, South, West)
		Vector3 north = new(0f, 0f, -size),
			east = new(size, 0f, 0f),
			south = new(0f, 0f, size),
			west = new(-size, 0f, 0f);
		// Draw diamond edges
		mesh.SurfaceAddVertex(north);
		mesh.SurfaceAddVertex(east);
		mesh.SurfaceAddVertex(east);
		mesh.SurfaceAddVertex(south);
		mesh.SurfaceAddVertex(south);
		mesh.SurfaceAddVertex(west);
		mesh.SurfaceAddVertex(west);
		mesh.SurfaceAddVertex(north);
		mesh.SurfaceEnd();
		// Create MeshInstance3D with the diamond marker
		MeshInstance3D diamondNode = new()
		{
			Mesh = mesh,
			Position = position,
			Name = $"AltElevatorMarker_{x}_{y}"
		};
		return diamondNode;
	}
}
