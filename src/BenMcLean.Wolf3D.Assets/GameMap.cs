using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace BenMcLean.Wolf3D.Assets;

public sealed class GameMap
{
	#region Data
	public string Name { get; private set; }
	public override string ToString() => Name;
	public ushort Number { get; private init; }
	public ushort Width { get; private init; }
	public const ushort Height = 0; // Vertical
	public ushort Depth { get; private init; }
	public ushort[] MapData { get; private set; }
	public ushort[] ObjectData { get; private set; }
	public ushort[] OtherData { get; private set; }
	public ushort X(int i) => (ushort)(i % Width);
	public ushort X(uint i) => (ushort)(i % Width);
	public ushort X(ushort i) => (ushort)(i % Width);
	public const ushort Y = 0; // Vertical
	public ushort Z(int i) => (ushort)(i / Depth);
	public ushort Z(uint i) => (ushort)(i / Depth);
	public ushort Z(ushort i) => (ushort)(i / Depth);
	public ushort GetIndex(uint x, uint z) => GetIndex((ushort)x, (ushort)z);
	public ushort GetIndex(ushort x, ushort z) => (ushort)((z * Depth) + x);
	public ushort GetMapData(ushort x, ushort z) => MapData[GetIndex(x, z)];
	public ushort GetMapData(uint x, uint z) => GetMapData((ushort)x, (ushort)z);
	public ushort GetObjectData(uint x, uint z) => GetObjectData((ushort)x, (ushort)z);
	public ushort GetObjectData(ushort x, ushort z) => ObjectData[GetIndex(x, z)];
	public ushort GetOtherData(uint x, uint z) => GetOtherData((ushort)x, (ushort)z);
	public ushort GetOtherData(ushort x, ushort z) => OtherData[GetIndex(x, z)];
	public bool IsWithinMap(int x, int z) => x >= 0 && z >= 0 && x < Width && z < Depth;
	#endregion Data
	#region Loading
	public static GameMap[] Load(XElement xml, string folder = "") => Load(
		mapHead: Path.Combine(folder, xml.Element("Maps").Attribute("MapHead").Value),
		gameMaps: Path.Combine(folder, xml.Element("Maps").Attribute("GameMaps").Value));
	public static GameMap[] Load(string mapHead, string gameMaps)
	{
		using FileStream mapHeadStream = new(mapHead, FileMode.Open);
		using FileStream gameMapsStream = new(gameMaps, FileMode.Open);
		return Load(mapHeadStream, gameMapsStream);
	}
	public static long[] ParseMapHead(Stream stream)
	{
		List<long> offsets = [];
		using (BinaryReader mapHeadReader = new(stream))
		{
			if (mapHeadReader.ReadUInt16() != 0xABCD)
				throw new InvalidDataException("File \"" + stream + "\" has invalid signature code!");
			uint offset;
			while (stream.CanRead && (offset = mapHeadReader.ReadUInt32()) != 0)
				offsets.Add(offset);
		}
		return [.. offsets];
	}
	public static GameMap[] Load(Stream mapHead, Stream gameMaps) =>
		Load(ParseMapHead(mapHead), gameMaps);
	public static GameMap[] Load(long[] offsets, Stream gameMaps)
	{
		GameMap[] maps = new GameMap[offsets.Length];
		using (BinaryReader gameMapsReader = new(gameMaps))
			for (ushort mapNumber = 0; mapNumber < offsets.Length; mapNumber++)
			{
				long offset = offsets[mapNumber];
				gameMaps.Seek(offset, 0);
				uint mapOffset = gameMapsReader.ReadUInt32(),
					objectOffset = gameMapsReader.ReadUInt32(),
					otherOffset = gameMapsReader.ReadUInt32();
				ushort mapByteSize = gameMapsReader.ReadUInt16(),
					objectByteSize = gameMapsReader.ReadUInt16(),
					otherByteSize = gameMapsReader.ReadUInt16();
				GameMap map = new()
				{
					Number = mapNumber,
					Width = gameMapsReader.ReadUInt16(),
					Depth = gameMapsReader.ReadUInt16(),
				};
				char[] name = new char[16];
				gameMapsReader.Read(name, 0, name.Length);
				map.Name = new string(name).Replace("\0", string.Empty).Trim();
				char[] carmackized = new char[4];
				gameMapsReader.Read(carmackized, 0, carmackized.Length);
				bool isCarmackized = new string(carmackized).Equals("!ID!");
				// "Note that for Wolfenstein 3-D, a 4-byte signature string ("!ID!") will normally be present directly after the level name. The signature does not appear to be used anywhere, but is useful for distinguishing between v1.0 files (the signature string is missing), and files for v1.1 and later (includes the signature string)."
				// "Note that for Wolfenstein 3-D v1.0, map files are not carmackized, only RLEW compression is applied."
				// http://www.shikadi.net/moddingwiki/GameMaps_Format#Map_data_.28GAMEMAPS.29
				// Carmackized game maps files are external GAMEMAPS.xxx files and the map header is stored internally in the executable. The map header must be extracted and the game maps decompressed before TED5 can access them. TED5 itself can produce carmackized files and external MAPHEAD.xxx files. Carmackization does not replace the RLEW compression used in uncompressed data, but compresses this data, that is, the data is doubly compressed.
				ushort[] mapData;
				gameMaps.Seek(mapOffset, 0);
				if (isCarmackized)
					mapData = CarmackExpand(gameMapsReader);
				else
				{
					mapData = new ushort[mapByteSize / 2];
					for (uint i = 0; i < mapData.Length; i++)
						mapData[i] = gameMapsReader.ReadUInt16();
				}
				map.MapData = RlewExpand(mapData, (ushort)(map.Depth * map.Width));
				ushort[] objectData;
				gameMaps.Seek(objectOffset, 0);
				if (isCarmackized)
					objectData = CarmackExpand(gameMapsReader);
				else
				{
					objectData = new ushort[objectByteSize / 2];
					for (uint i = 0; i < objectData.Length; i++)
						objectData[i] = gameMapsReader.ReadUInt16();
				}
				map.ObjectData = RlewExpand(objectData, (ushort)(map.Depth * map.Width));
				ushort[] otherData;
				gameMaps.Seek(otherOffset, 0);
				if (isCarmackized)
					otherData = CarmackExpand(gameMapsReader);
				else
				{
					otherData = new ushort[otherByteSize / 2];
					for (uint i = 0; i < otherData.Length; i++)
						otherData[i] = gameMapsReader.ReadUInt16();
				}
				map.OtherData = RlewExpand(otherData, (ushort)(map.Depth * map.Width));
				maps[mapNumber] = map;
			}
		return maps;
	}
	#endregion Loading
	#region Decompression algorithms
	public const byte CARMACK_NEAR = 0xA7,
		CARMACK_FAR = 0xA8;
	public static ushort[] RlewExpand(ushort[] carmackExpanded, ushort length, ushort tag = 0xABCD)
	{
		ushort[] rawMapData = new ushort[length];
		int src_index = 1, dest_index = 0;
		do
		{
			ushort value = carmackExpanded[src_index++]; // WORDS!!
			if (value != tag) // uncompressed
				rawMapData[dest_index++] = value;
			else
			{ // compressed string
				ushort count = carmackExpanded[src_index++];
				value = carmackExpanded[src_index++];
				for (ushort i = 1; i <= count; i++)
					rawMapData[dest_index++] = value;
			}
		} while (dest_index < length);
		return rawMapData;
	}
	public static ushort[] CarmackExpand(BinaryReader binaryReader)
	{
		ushort index = 0,
			length = (ushort)(binaryReader.ReadUInt16() >> 1);
		ushort[] expandedWords = new ushort[length];
		while (length > 0)
		{
			ushort ch = binaryReader.ReadUInt16(),
				chHigh = (ushort)(ch >> 8);
			if (chHigh == CARMACK_NEAR)
			{
				ushort count = (ushort)(ch & 0xFF);
				if (count == 0)
				{
					ch |= binaryReader.ReadByte();
					expandedWords[index++] = ch;
					length--;
				}
				else
				{
					ushort offset = binaryReader.ReadByte();
					length -= count;
					if (length < 0)
						return expandedWords;
					while (count-- > 0)
						expandedWords[index] = expandedWords[index++ - offset];
				}
			}
			else if (chHigh == CARMACK_FAR)
			{
				ushort count = (ushort)(ch & 0xFF);
				if (count == 0)
				{
					ch |= binaryReader.ReadByte();
					expandedWords[index++] = ch;
					length--;
				}
				else
				{
					ushort offset = binaryReader.ReadUInt16();
					length -= count;
					if (length < 0)
						return expandedWords;
					while (count-- > 0)
						expandedWords[index++] = expandedWords[offset++];
				}
			}
			else
			{
				expandedWords[index++] = ch;
				length--;
			}
		}
		return expandedWords;
	}
	#endregion Decompression algorithms
}
