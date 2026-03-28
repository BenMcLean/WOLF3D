using System.IO;
using static BenMcLean.Wolf3D.Assets.Gameplay.MapAnalyzer;

namespace BenMcLean.Wolf3D.Assets.Gameplay;

/// <summary>
/// Reads WALLSPAWNS binary files containing pre-baked wall spawn data.
/// <para>
/// Wolf3D's GAMEMAPS wall plane stores one tile value per cell, which the standard
/// MapAnalysis wall scan converts to VSWAP page numbers using a light/dark pairing
/// formula (WL_MAIN.C SetupWalls). This gives every face of a tile the same texture
/// (or a light/dark variant), which is correct for Wolf3D but wrong for KOD.
/// </para>
/// <para>
/// KOD's I3D engine assigns a distinct texture to each cardinal face of each block type
/// (n_wall, e_wall, s_wall, w_wall in the .BLK file). The WALLSPAWNS file carries that
/// per-face data alongside the GAMEMAPS file. When a game's &lt;Maps&gt; XML element
/// has a WallSpawns attribute, MapAnalysis uses these pre-baked spawns instead of
/// running EastWest/NorthSouth.
/// </para>
/// <para>
/// File format: [ushort levelCount][uint[] offsets][level data...]
/// Per level at its offset: [ushort count][count * (ushort X, ushort Y, ushort Shape, byte flags)]
/// flags: bit 0 = FacesEastWest, bit 1 = Flip
/// </para>
/// </summary>
public static class WallSpawns
{
	/// <summary>
	/// Loads all levels' wall spawns from a WALLSPAWNS file.
	/// Returns an array indexed by level number (matching GameMap.Number).
	/// </summary>
	public static MapAnalysis.WallSpawn[][] LoadAll(string path)
	{
		using FileStream stream = new(path, FileMode.Open, FileAccess.Read);
		return LoadAll(stream);
	}

	/// <summary>
	/// Loads all levels' wall spawns from a stream.
	/// </summary>
	public static MapAnalysis.WallSpawn[][] LoadAll(Stream stream)
	{
		using BinaryReader reader = new(stream, System.Text.Encoding.ASCII, leaveOpen: true);
		ushort levelCount = reader.ReadUInt16();
		uint[] offsets = new uint[levelCount];
		for (int i = 0; i < levelCount; i++)
			offsets[i] = reader.ReadUInt32();
		MapAnalysis.WallSpawn[][] result = new MapAnalysis.WallSpawn[levelCount][];
		for (int i = 0; i < levelCount; i++)
		{
			stream.Seek(offsets[i], SeekOrigin.Begin);
			ushort count = reader.ReadUInt16();
			MapAnalysis.WallSpawn[] spawns = new MapAnalysis.WallSpawn[count];
			for (int j = 0; j < count; j++)
			{
				ushort x = reader.ReadUInt16();
				ushort y = reader.ReadUInt16();
				ushort shape = reader.ReadUInt16();
				byte flags = reader.ReadByte();
				bool facesEastWest = (flags & 1) != 0;
				bool flip = (flags & 2) != 0;
				spawns[j] = new MapAnalysis.WallSpawn(shape, facesEastWest, x, y, flip);
			}
			result[i] = spawns;
		}
		return result;
	}
}
