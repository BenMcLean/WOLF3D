using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BenMcLean.Wolf3D.Assets;

public static class ExtensionMethods
{
	public static bool IsTrue(this XElement xElement, string attribute) =>
		bool.TryParse(xElement?.Attribute(attribute)?.Value, out bool @bool) && @bool;
	public static bool IsFalse(this XElement xElement, string attribute) =>
		bool.TryParse(xElement?.Attribute(attribute)?.Value, out bool @bool) && !@bool;
	/// <summary>
	/// Returns first line in a string or entire string if no linebreaks are included
	/// </summary>
	/// <param name="str">String value</param>
	/// <returns>Returns first line in the string</returns>
	public static string FirstLine(this string @string) =>
		string.IsNullOrWhiteSpace(@string) ? null
		: @string.IndexOf(Environment.NewLine, StringComparison.CurrentCulture) is int index && index >= 0 ?
			@string.Substring(0, index)
			: @string;
	public static readonly Regex NewLineRegex = new(@"\r\n|\n|\r", RegexOptions.Singleline);
	public static string[] Lines(this string @string) => NewLineRegex.Split(@string);
	public static int CountLines(this string @string) => NewLineRegex.Matches(@string).Count + 1;
	public static IEnumerable<Tuple<int, int>> IntPairs(this XAttribute input) => IntPairs(input?.Value);
	public static IEnumerable<Tuple<int, int>> IntPairs(this string input)
	{
		if (string.IsNullOrWhiteSpace(input))
			throw new InvalidDataException("Can't get pairs from \"" + input + "\".");
		string[] inputs = input.Split(',');
		for (int i = 0; i < inputs.Length; i += 2)
			yield return new Tuple<int, int>(int.Parse(inputs[i]), int.Parse(inputs[i + 1]));
	}
	public static IEnumerable<Tuple<float, float>> FloatPairs(this XAttribute input) => FloatPairs(input?.Value);
	public static IEnumerable<Tuple<float, float>> FloatPairs(this string input)
	{
		if (string.IsNullOrWhiteSpace(input))
			throw new InvalidDataException("Can't get pairs from \"" + input + "\".");
		string[] inputs = input.Split(',');
		for (int i = 0; i < inputs.Length; i += 2)
			yield return new Tuple<float, float>(float.Parse(inputs[i]), float.Parse(inputs[i + 1]));
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
	#region Colors
	public static byte R(this uint color) => (byte)(color >> 24);
	public static byte G(this uint color) => (byte)(color >> 16);
	public static byte B(this uint color) => (byte)(color >> 8);
	public static byte A(this uint color) => (byte)color;
	/// <param name="index">Palette indexes (one byte per pixel)</param>
	/// <param name="palette">256 rgba8888 color values</param>
	/// <returns>rgba8888 texture (four bytes per pixel)</returns>
	public static byte[] Indices2ByteArray(this byte[] index, uint[] palette)
	{
		byte[] bytes = new byte[index.Length << 2];
		for (int i = 0, j = 0; i < index.Length; i++)
		{
			bytes[j++] = (byte)(palette[index[i]] >> 24);
			bytes[j++] = (byte)(palette[index[i]] >> 16);
			bytes[j++] = (byte)(palette[index[i]] >> 8);
			bytes[j++] = (byte)palette[index[i]];
		}
		return bytes;
	}
	/// <param name="indices">Palette indexes (one byte per pixel)</param>
	/// <param name="palette">256 rgba8888 color values</param>
	/// <returns>rgba8888 texture (one int per pixel)</returns>
	public static uint[] Indices2UIntArray(this byte[] indices, uint[] palette)
	{
		uint[] ints = new uint[indices.Length];
		for (int i = 0; i < indices.Length; i++)
			ints[i] = palette[indices[i]];
		return ints;
	}
	/// <param name="uints">rgba8888 color values (one int per pixel)</param>
	/// <returns>rgba8888 texture (four bytes per pixel)</returns>
	public static byte[] UInt2ByteArray(this uint[] uints)
	{
		byte[] bytes = new byte[uints.Length << 2];
		for (int i = 0, j = 0; i < uints.Length; i++)
		{
			bytes[j++] = (byte)(uints[i] >> 24);
			bytes[j++] = (byte)(uints[i] >> 16);
			bytes[j++] = (byte)(uints[i] >> 8);
			bytes[j++] = (byte)uints[i];
		}
		return bytes;
	}
	/// <param name="bytes">rgba8888 color values (four bytes per pixel)</param>
	/// <returns>rgba8888 texture (one uint per pixel)</returns>
	public static uint[] Byte2UIntArray(this byte[] bytes)
	{
		uint[] uints = new uint[bytes.Length >> 2];
		for (int i = 0, j = 0; i < bytes.Length; i += 4)
			uints[j++] = (uint)bytes[i] << 24
				| (uint)bytes[i + 1] << 16
				| (uint)bytes[i + 2] << 8
				| bytes[i + 3];
		return uints;
	}
	#endregion Colors
}
