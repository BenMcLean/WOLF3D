using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace BenMcLean.Wolf3D.VR;

public static class ExtensionMethods
{
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
		if (scaleX < 1 || scaleY < 1 || scaleX < 2 && scaleY < 2) return (byte[])texture.Clone();
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
	public static Simulator.Direction ConvertCardinalToSimulatorDirection(this Assets.Direction cardinalDir) => cardinalDir switch
	{
		Assets.Direction.N => Simulator.Direction.N,
		Assets.Direction.E => Simulator.Direction.E,
		Assets.Direction.S => Simulator.Direction.S,
		Assets.Direction.W => Simulator.Direction.W,
		_ => Simulator.Direction.E
	};
	#region Coordinates
	/// <summary>
	/// Converts a tile coordinate to meters (at tile corner).
	/// </summary>
	/// <param name="tile">Tile coordinate (0, 1, 2, ...)</param>
	/// <returns>Position in meters (north/east corner of tile)</returns>
	public static float ToMeters(this short tile) => tile * Constants.WallWidth;
	public static float ToMeters(this ushort tile) => tile * Constants.WallWidth;
	/// <summary>
	/// Converts a tile coordinate to meters (at tile center).
	/// </summary>
	/// <param name="tile">Tile coordinate (0, 1, 2, ...)</param>
	/// <returns>Position in meters (center of tile)</returns>
	public static float ToMetersCentered(this short tile) => tile.ToMeters() + Constants.HalfWallWidth;
	public static float ToMetersCentered(this ushort tile) => tile.ToMeters() + Constants.HalfWallWidth;
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
	public static int ToTile(this float meters) => Mathf.FloorToInt(meters / Constants.WallWidth);
	/// <summary>
	/// Converts a meter position to a 16.16 fixed-point coordinate.
	/// </summary>
	/// <param name="meters">Position in meters</param>
	/// <returns>16.16 fixed-point coordinate</returns>
	public static int ToFixedPoint(this float meters) => (int)(meters / Constants.FixedPointToMeters);
	#endregion Coordinates
	public static float TicsToSeconds(this int tics) => tics / Constants.TicsPerSecond;
	public static short SecondsToTics(this float seconds) => (short)(seconds * Constants.TicsPerSecond);
	public static Vector2 Vector2(this Vector3 vector3) => new(vector3.X, vector3.Z);
	public static Vector3 Vector3(this Vector2 vector2) => new(vector2.X, 0f, vector2.Y);
	public static Vector3 Axis(this Vector3.Axis axis) => axis switch
	{
		Godot.Vector3.Axis.X => Constants.Rotate90,
		Godot.Vector3.Axis.Y => Godot.Vector3.Up,
		_ => Godot.Vector3.Zero,
	};
}
