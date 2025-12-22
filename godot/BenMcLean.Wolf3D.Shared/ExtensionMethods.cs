using System;

namespace BenMcLean.Wolf3D.Shared;

public static class ExtensionMethods
{
	/// <summary>
	/// Draws a texture onto a different texture
	/// </summary>
	/// <param name="texture">raw rgba8888 pixel data to be modified</param>
	/// <param name="x">upper left corner of where to insert</param>
	/// <param name="y">upper left corner of where to insert</param>
	/// <param name="insert">raw rgba888 pixel data to insert</param>
	/// <param name="insertWidth">width of insert or 0 to assume square texture</param>
	/// <param name="width">width of texture or 0 to assume square texture</param>
	/// <returns>same texture with insert drawn</returns>
	public static byte[] DrawInsert(this byte[] texture, int x, int y, byte[] insert, ushort insertWidth = 0, ushort width = 0)
	{
		int insertX = 0, insertY = 0;
		if (x < 0)
		{
			insertX = -x;
			insertX <<= 2;
			x = 0;
		}
		if (y < 0)
		{
			insertY = -y;
			y = 0;
		}
		int xSide = (width < 1 ? (int)Math.Sqrt(texture.Length >> 2) : width) << 2;
		x <<= 2; // x *= 4;
		if (x > xSide) return texture;
		int insertXside = (insertWidth < 1 ? (int)Math.Sqrt(insert.Length >> 2) : insertWidth) << 2,
			actualInsertXside = (x + insertXside > xSide ? xSide - x : insertXside) - insertX,
			ySide = (width < 1 ? xSide : texture.Length / width) >> 2;
		if (y > ySide) return texture;
		if (xSide == insertXside && x == 0 && insertX == 0)
			Array.Copy(
				sourceArray: insert,
				sourceIndex: insertY * insertXside,
				destinationArray: texture,
				destinationIndex: y * xSide,
				length: Math.Min(insert.Length - insertY * insertXside + insertX, texture.Length - y * xSide));
		else
			for (int y1 = y * xSide + x, y2 = insertY * insertXside + insertX; y1 + actualInsertXside < texture.Length && y2 < insert.Length; y1 += xSide, y2 += insertXside)
				Array.Copy(
					sourceArray: insert,
					sourceIndex: y2,
					destinationArray: texture,
					destinationIndex: y1,
					length: actualInsertXside);
		return texture;
	}
	/// <summary>
	/// Compute power of two greater than or equal to `n`
	/// </summary>
	public static uint NextPowerOf2(this uint n)
	{
		n--; // decrement `n` (to handle the case when `n` itself is a power of 2)
			 // set all bits after the last set bit
		n |= n >> 1;
		n |= n >> 2;
		n |= n >> 4;
		n |= n >> 8;
		n |= n >> 16;
		return ++n; // increment `n` and return
	}
}
