using System;
using System.Collections.Generic;
using System.Linq;
using BenMcLean.Wolf3D.Assets;
using Godot;

namespace BenMcLean.Wolf3D.VR;

public static class ExtensionMethods
{
	#region Coordinates
	/// <summary>
	/// Converts a tile coordinate to meters (at tile corner).
	/// </summary>
	/// <param name="tile">Tile coordinate (0, 1, 2, ...)</param>
	/// <returns>Position in meters (north/east corner of tile)</returns>
	public static float ToMeters(this short tile) => tile * Constants.TileWidth;
	public static float ToMeters(this ushort tile) => tile * Constants.TileWidth;
	/// <summary>
	/// Converts a tile coordinate to meters (at tile center).
	/// </summary>
	/// <param name="tile">Tile coordinate (0, 1, 2, ...)</param>
	/// <returns>Position in meters (center of tile)</returns>
	public static float ToMetersCentered(this short tile) => tile.ToMeters() + Constants.HalfTileWidth;
	public static float ToMetersCentered(this ushort tile) => tile.ToMeters() + Constants.HalfTileWidth;
	/// <summary>
	/// Converts a 16.16 fixed-point coordinate to meters.
	/// </summary>
	/// <param name="fixedPoint">16.16 fixed-point coordinate</param>
	/// <returns>Position in meters</returns>
	public static float ToMeters(this int fixedPoint) => fixedPoint * Constants.FixedPointToMeters;
	public static float ToMeters(this uint fixedPoint) => fixedPoint * Constants.FixedPointToMeters;
	/// <summary>
	/// Converts a meter position to a tile coordinate (floor).
	/// </summary>
	/// <param name="meters">Position in meters</param>
	/// <returns>Tile coordinate</returns>
	public static short ToTile(this float meters) => (short)Mathf.FloorToInt(meters / Constants.TileWidth);
	/// <summary>
	/// Converts a meter position to a 16.16 fixed-point coordinate.
	/// </summary>
	/// <param name="meters">Position in meters</param>
	/// <returns>16.16 fixed-point coordinate</returns>
	public static int ToFixedPoint(this float meters) => (int)(meters / Constants.FixedPointToMeters);
	#endregion Coordinates
	#region Angles
	/// <summary>
	/// Converts Godot camera Y rotation to a cardinal Direction (for pushwall movement).
	/// Snaps to the nearest cardinal direction: N, E, S, or W.
	/// Godot Y rotation: 0=North(-Z), π/2=West(-X), π=South(+Z), 3π/2=East(+X)
	/// Note: Due to the negative X in projection math, East/West are opposite of typical Godot convention
	/// </summary>
	/// <param name="rotationY">Camera Y rotation in radians</param>
	/// <returns>Cardinal direction (N, E, S, or W)</returns>
	public static Direction ToCardinalDirection(this float rotationY)
	{
		// Normalize rotation to [0, 2π)
		float normalized = rotationY % Mathf.Tau;
		if (normalized < 0)
			normalized += Mathf.Tau;
		// Divide circle into 4 quadrants centered on cardinal directions
		// Each quadrant is π/2 (90°) wide, with boundaries at π/4 (45°) intervals
		// North: [0°, 45°) and [315°, 360°)
		// West: [45°, 135°)
		// South: [135°, 225°)
		// East: [225°, 315°)
		if (normalized < Constants.QuarterPi)
			return Direction.N;
		else if (normalized < 3f * Constants.QuarterPi)
			return Direction.W;
		else if (normalized < 5f * Constants.QuarterPi)
			return Direction.S;
		else if (normalized < 7f * Constants.QuarterPi)
			return Direction.E;
		else
			return Direction.N; // [7π/4, 2π) wraps to North
	}
	/// <summary>
	/// Converts Godot camera Y rotation to an 8-way Direction.
	/// Snaps to the nearest of 8 directions: N, NE, E, SE, S, SW, W, NW.
	/// Godot Y rotation: 0=North(-Z), π/2=West(-X), π=South(+Z), 3π/2=East(+X)
	/// </summary>
	/// <param name="rotationY">Camera Y rotation in radians</param>
	/// <returns>8-way direction</returns>
	public static Direction ToDirection(this float rotationY)
	{
		// Normalize rotation to [0, 2π)
		float normalized = rotationY % Mathf.Tau;
		if (normalized < 0)
			normalized += Mathf.Tau;
		// Divide circle into 8 octants centered on 8-way directions
		// Each octant is π/4 (45°) wide, with boundaries at π/8 (22.5°) intervals
		// North: [0°, 22.5°) and [337.5°, 360°)
		// Northwest: [22.5°, 67.5°)
		// West: [67.5°, 112.5°)
		// Southwest: [112.5°, 157.5°)
		// South: [157.5°, 202.5°)
		// Southeast: [202.5°, 247.5°)
		// East: [247.5°, 292.5°)
		// Northeast: [292.5°, 337.5°)
		if (normalized < Constants.EighthPi)
			return Direction.N;
		else if (normalized < 3f * Constants.EighthPi)
			return Direction.NW;
		else if (normalized < 5f * Constants.EighthPi)
			return Direction.W;
		else if (normalized < 7f * Constants.EighthPi)
			return Direction.SW;
		else if (normalized < 9f * Constants.EighthPi)
			return Direction.S;
		else if (normalized < 11f * Constants.EighthPi)
			return Direction.SE;
		else if (normalized < 13f * Constants.EighthPi)
			return Direction.E;
		else if (normalized < 15f * Constants.EighthPi)
			return Direction.NE;
		else
			return Direction.N; // [337.5°, 360°) wraps to North
	}
	/// <summary>
	/// Converts a Direction enum to a Godot Y rotation angle in radians.
	/// Wolf3D +Y (south/down map) → Godot -Z requires negating the angle to flip the Z axis.
	/// Standard atan2 convention: 0=East(+X), π/2=South(+Z), π=West(-X), 3π/2=North(-Z)
	/// Direction enum: E=0, NE=1, N=2, NW=3, W=4, SW=5, S=6, SE=7
	/// </summary>
	/// <param name="direction">Direction to convert</param>
	/// <returns>Godot Y rotation in radians</returns>
	public static float ToAngle(this Direction direction) =>
		-(byte)direction * Constants.QuarterPi;
	public static Vector2 Vector2(this Vector3 vector3) => new(vector3.X, vector3.Z);
	public static Vector3 Vector3(this Vector2 vector2) => new(vector2.X, 0f, vector2.Y);
	public static Vector3 Axis(this Vector3.Axis axis) => axis switch
	{
		Godot.Vector3.Axis.X => Constants.Rotate90,
		Godot.Vector3.Axis.Y => Godot.Vector3.Up,
		_ => Godot.Vector3.Zero,
	};
	#endregion Angles
	#region Utilities
	/// <summary>
	/// Parallelizes the execution of a Select query while preserving the order of the source sequence.
	/// </summary>
	public static List<TResult> Parallelize<TSource, TResult>(
		this IEnumerable<TSource> source,
		Func<TSource, TResult> selector) => [.. source
			.Select((element, index) => (element, index))
			.AsParallel()
			.Select(sourceTuple => (result: selector(sourceTuple.element), sourceTuple.index))
			.OrderBy(resultTuple => resultTuple.index)
			.AsEnumerable()
			.Select(resultTuple => resultTuple.result)];
	/// <summary>
	/// Simple nearest-neighbor upscaling by integer multipliers
	/// </summary>
	/// <param name="texture">raw rgba8888 pixel data of source image</param>
	/// <param name="scaleX">horizontal scaling factor</param>
	/// <param name="scaleY">vertical scaling factor</param>
	/// <param name="width">width of texture or 0 to assume square texture</param>
	/// <returns>new raw rgba8888 pixel data of newWidth = width * scaleX</returns>
	public static byte[] Upscale(this byte[] texture, byte scaleX = 1, byte scaleY = 1, ushort width = 0)
	{
		if (scaleX < 1 || scaleY < 1 || scaleX < 2 && scaleY < 2)
			return (byte[])texture.Clone();
		int xSide = (width < 1 ? (int)Math.Sqrt(texture.Length >> 2) : width) << 2,
			newXside = xSide * scaleX,
			newXsidefactorY = newXside * scaleY;
		byte[] scaled = new byte[texture.Length * scaleY * scaleX];
		if (scaleX < 2)
			for (int y1 = 0, y2 = 0; y1 < texture.Length; y1 += xSide, y2 += newXsidefactorY)
				for (int z = y2; z < y2 + newXsidefactorY; z += newXside)
					Array.Copy(
						sourceArray: texture,
						sourceIndex: y1,
						destinationArray: scaled,
						destinationIndex: z,
						length: xSide);
		else
		{
			int factorX4 = scaleX << 2;
			for (int y1 = 0, y2 = 0; y1 < texture.Length; y1 += xSide, y2 += newXsidefactorY)
			{
				for (int x1 = y1, x2 = y2; x1 < y1 + xSide; x1 += 4, x2 += factorX4)
					for (int z = 0; z < factorX4; z += 4)
						Array.Copy(
							sourceArray: texture,
							sourceIndex: x1,
							destinationArray: scaled,
							destinationIndex: x2 + z,
							length: 4);
				for (int z = y2 + newXside; z < y2 + newXsidefactorY; z += newXside)
					Array.Copy(
						sourceArray: scaled,
						sourceIndex: y2,
						destinationArray: scaled,
						destinationIndex: z,
						length: newXside);
			}
		}
		return scaled;
	}
	#endregion Utilities
}
