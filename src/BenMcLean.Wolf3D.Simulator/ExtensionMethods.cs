namespace BenMcLean.Wolf3D.Simulator;

/// <summary>
/// Extension methods for Wolf3D coordinate conversions.
/// Wolf3D uses two coordinate systems:
/// 1. Tiles: Integer grid coordinates (0, 1, 2, ...)
/// 2. Fixed-Point: 16.16 format where upper 16 bits = tile, lower 16 bits = fractional position (0-65535)
/// </summary>
public static class ExtensionMethods
{
	/// <summary>
	/// Converts a tile coordinate with fractional offset to 16.16 fixed-point format.
	/// </summary>
	/// <param name="tile">Tile coordinate (0, 1, 2, ...)</param>
	/// <param name="fractional">Fractional position within tile (0-65535, where 0 = corner, 32768 = center). Defaults to 0 (north/west corner).</param>
	/// <returns>16.16 fixed-point coordinate</returns>
	public static int ToFixedPoint(this short tile, ushort fractional = 0) => (tile << 16) | fractional;
	/// <summary>
	/// Converts a tile coordinate to 16.16 fixed-point format, centered in the tile.
	/// </summary>
	/// <param name="tile">Tile coordinate (0, 1, 2, ...)</param>
	/// <returns>16.16 fixed-point coordinate at tile center (fractional = 0x8000)</returns>
	public static int ToFixedPointCenter(this short tile) => tile.ToFixedPoint(0x8000);
	/// <summary>
	/// Converts a tile coordinate with fractional offset to 16.16 fixed-point format.
	/// </summary>
	/// <param name="tile">Tile coordinate (0, 1, 2, ...)</param>
	/// <param name="fractional">Fractional position within tile (0-65535, where 0 = corner, 32768 = center). Defaults to 0 (north/west corner).</param>
	/// <returns>16.16 fixed-point coordinate</returns>
	public static int ToFixedPoint(this ushort tile, ushort fractional = 0) => (tile << 16) | fractional;
	/// <summary>
	/// Converts a tile coordinate to 16.16 fixed-point format, centered in the tile.
	/// </summary>
	/// <param name="tile">Tile coordinate (0, 1, 2, ...)</param>
	/// <returns>16.16 fixed-point coordinate at tile center (fractional = 0x8000)</returns>
	public static int ToFixedPointCenter(this ushort tile) => tile.ToFixedPoint(0x8000);
	/// <summary>
	/// Extracts the tile coordinate (integer part) from a Wolf3D 16.16 fixed-point coordinate.
	/// </summary>
	/// <param name="fixedPoint">16.16 fixed-point coordinate</param>
	/// <returns>Tile coordinate (upper 16 bits)</returns>
	public static short ToTile(this int fixedPoint) => (short)(fixedPoint >> 16);
	/// <summary>
	/// Extracts the fractional position within a tile from a Wolf3D 16.16 fixed-point coordinate.
	/// </summary>
	/// <param name="fixedPoint">16.16 fixed-point coordinate</param>
	/// <returns>Fractional position (0-65535, where 0 = tile corner, 32768 = center, 65535 = next corner)</returns>
	public static ushort ToFractional(this int fixedPoint) => (ushort)(fixedPoint & 0xFFFF);
	public static Direction ToSimulatorDirection(this Assets.Direction cardinalDir) => cardinalDir switch
	{
		Assets.Direction.N => Direction.N,
		Assets.Direction.E => Direction.E,
		Assets.Direction.S => Direction.S,
		Assets.Direction.W => Direction.W,
		_ => Direction.E
	};
}
