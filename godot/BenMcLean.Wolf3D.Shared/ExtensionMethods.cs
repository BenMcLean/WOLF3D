using System;
using System.Buffers.Binary;

namespace BenMcLean.Wolf3D.Shared;

public static class ExtensionMethods
{
	#region Time
	/// <summary>
	/// Converts Wolf3D tics to seconds (Godot delta time)
	/// </summary>
	public static double ToSeconds(this int tics) => tics * Constants.SecondsPerTic;
	/// <summary>
	/// Converts Wolf3D tics to seconds (Godot delta time)
	/// </summary>
	public static double ToSeconds(this long tics) => tics * Constants.SecondsPerTic;
	/// <summary>
	/// Converts Wolf3D tics to TimeSpan
	/// </summary>
	public static TimeSpan ToTimeSpan(this int tics) =>
		TimeSpan.FromTicks(tics * Constants.TimeSpanTicksPerTic);
	/// <summary>
	/// Converts Wolf3D tics to TimeSpan
	/// </summary>
	public static TimeSpan ToTimeSpan(this long tics) =>
		TimeSpan.FromTicks(tics * Constants.TimeSpanTicksPerTic);
	/// <summary>
	/// Converts seconds (Godot delta time) to Wolf3D tics
	/// </summary>
	public static int ToTics(this double seconds) =>
		(int)Math.Round(seconds * Constants.TicsPerSecond);
	/// <summary>
	/// Converts seconds (Godot delta time) to Wolf3D tics as long
	/// </summary>
	public static long ToTicsLong(this double seconds) =>
		(long)Math.Round(seconds * Constants.TicsPerSecond);
	/// <summary>
	/// Converts seconds (Godot delta time) to TimeSpan
	/// </summary>
	public static TimeSpan ToTimeSpan(this double seconds) => TimeSpan.FromSeconds(seconds);
	/// <summary>
	/// Converts TimeSpan to Wolf3D tics
	/// </summary>
	public static int ToTics(this TimeSpan timeSpan) =>
		(int)Math.Round(timeSpan.Ticks * Constants.TicsPerTimeSpanTick);
	/// <summary>
	/// Converts TimeSpan to Wolf3D tics as long
	/// </summary>
	public static long ToTicsLong(this TimeSpan timeSpan) =>
		(long)Math.Round(timeSpan.Ticks * Constants.TicsPerTimeSpanTick);
	#endregion Time
	#region Drawing
	/// <summary>
	/// Draws one pixel of the specified color
	/// </summary>
	/// <param name="texture">raw rgba8888 pixel data</param>
	/// <param name="color">rgba color to draw</param>
	/// <param name="width">width of texture or 0 to assume square texture</param>
	/// <returns>same texture with pixel drawn</returns>
	public static byte[] DrawPixel(this byte[] texture, ushort x, ushort y, uint color = Constants.White, ushort width = 0)
	{
		ushort xSide = (ushort)((width < 1 ? (ushort)Math.Sqrt(texture.Length >> 2) : width) << 2),
			ySide = (ushort)((width < 1 ? xSide : texture.Length / width) >> 2);
		x <<= 2;//x *= 4;
		if (x >= xSide || y >= ySide) return texture;
		BinaryPrimitives.WriteUInt32BigEndian(
			destination: texture.AsSpan(
				start: y * xSide + x,
				length: 4),
			value: color);
		return texture;
	}
	/// <summary>
	/// Draws a rectangle of the specified color
	/// </summary>
	/// <param name="texture">raw rgba8888 pixel data to be modified</param>
	/// <param name="color">rgba color to draw</param>
	/// <param name="x">upper left corner of rectangle</param>
	/// <param name="y">upper left corner of rectangle</param>
	/// <param name="width">width of texture or 0 to assume square texture</param>
	/// <returns>same texture with rectangle drawn</returns>
	public static byte[] DrawRectangle(this byte[] texture, int x, int y, uint color = Constants.White, int rectWidth = 1, int rectHeight = 1, ushort width = 0)
	{
		if (rectWidth == 1 && rectHeight == 1)
			return texture.DrawPixel((ushort)x, (ushort)y, color, width);
		if (rectHeight < 1) rectHeight = rectWidth;
		if (x < 0)
		{
			rectWidth += x;
			x = 0;
		}
		if (y < 0)
		{
			rectHeight += y;
			y = 0;
		}
		if (width < 1) width = (ushort)Math.Sqrt(texture.Length >> 2);
		int height = texture.Length / width >> 2;
		if (rectWidth < 1 || rectHeight < 1 || x >= width || y >= height) return texture;
		rectWidth = Math.Min(rectWidth, width - x);
		rectHeight = Math.Min(rectHeight, height - y);
		int xSide = width << 2,
			x4 = x << 2,
			offset = y * xSide + x4,
			rectWidth4 = rectWidth << 2,
			yStop = offset + xSide * rectHeight;
		for (int x2 = offset; x2 < offset + rectWidth4; x2 += 4)
			BinaryPrimitives.WriteUInt32BigEndian(
				destination: texture.AsSpan(
					start: x2,
					length: 4),
				value: color);
		for (int y2 = offset + xSide; y2 < yStop; y2 += xSide)
			Array.Copy(
				sourceArray: texture,
				sourceIndex: offset,
				destinationArray: texture,
				destinationIndex: y2,
				length: rectWidth4);
		return texture;
	}
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
	#endregion Drawing
	#region Utilities
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
	#endregion Utilities
}
