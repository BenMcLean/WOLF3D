using System;
using System.Collections.Generic;
using System.Linq;

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
}
