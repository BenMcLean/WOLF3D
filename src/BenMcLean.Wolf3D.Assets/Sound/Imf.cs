using System.Collections.Generic;
using System.IO;

namespace BenMcLean.Wolf3D.Assets.Sound;

/// <summary>
/// Parses and stores IMF format music data. http://www.shikadi.net/moddingwiki/IMF_Format
/// </summary>
public struct Imf(BinaryReader binaryReader)
{
	/// <summary>
	/// These songs play back at 700 Hz.
	/// </summary>
	public const float Hz = 1f / 700f;
	#region Data
	/// <summary>
	/// Sent to register port.
	/// </summary>
	public readonly byte Register = binaryReader.ReadByte();
	/// <summary>
	/// Sent to data port.
	/// </summary>
	public readonly byte Data = binaryReader.ReadByte();
	/// <summary>
	/// How much to wait.
	/// </summary>
	public readonly ushort Delay = binaryReader.ReadUInt16();
	public readonly float DelayFloat => Delay * Hz;
	#endregion Data
	/// <summary>
	/// Parsing IMF files based on http://www.shikadi.net/moddingwiki/IMF_Format
	/// </summary>
	public static Imf[] ReadImf(Stream stream)
	{
		Imf[] imf;
		using (BinaryReader binaryReader = new(stream))
		{
			ushort length = (ushort)(binaryReader.ReadUInt16() >> 2); // Length is provided in number of bytes. Divide by 4 to get the number of 4 byte packets.
			if (length == 0)
			{
				#region Type-0 format
				stream.Seek(0, 0);
				List<Imf> list = [];
				while (stream.Position < stream.Length)
					list.Add(new Imf(binaryReader));
				imf = [.. list];
				#endregion Type-0 format
			}
			else
			{
				#region Type-1 format
				imf = new Imf[length];
				for (int i = 0; i < imf.Length; i++)
					imf[i] = new Imf(binaryReader);
				#endregion Type-1 format
			}
		}
		return imf;
	}
}
