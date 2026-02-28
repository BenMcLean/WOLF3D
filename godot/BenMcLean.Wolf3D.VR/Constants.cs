using Godot;

namespace BenMcLean.Wolf3D.VR;

public static class Constants
{
	/// <summary>
	/// Tom Hall's Doom Bible and also tweets from John Carmack state that the walls in Wolfenstein 3-D were always eight feet thick. The wall textures are 64x64 pixels, which means that the ratio is 8 pixels per foot.
	/// However, VR uses the metric system, where 1 game unit is 1 meter in real space. One foot equals 0.3048 meters.
	/// </summary>
	public const float Foot = 0.3048f,
		Inch = Foot / 12f,
		/// <summary>
		/// To scale Wolfenstein 3-D correctly in VR, one pixel must equal 0.0381 in meters, and a Wolfenstein 3-D wall must be 2.4384 meters thick.
		/// </summary>
		PixelWidth = Foot / 8f,
		TileWidth = Foot * 8f,
		HalfTileWidth = Foot * 4f,
		/// <summary>
		/// Wolf3D uses 16.16 fixed-point coordinates where 65536 units = 1 tile.
		/// </summary>
		FixedPointToMeters = TileWidth / 65536f,
		/// <summary>
		/// Wolfenstein 3-D ran in SVGA screen mode 13h, which has a 320x200 resolution in a 4:3 aspect ratio.
		/// This means that the pixels are not square! They have a 1.2:1 aspect ratio.
		/// <summary>
		PixelHeight = Foot * 0.15f,
		TileHeight = Foot * 9.6f,
		HalfTileHeight = Foot * 4.8f,
		DeadZone = 0.5f,
		HalfPi = Mathf.Pi / 2f,
		QuarterPi = Mathf.Pi / 4f,
		EighthPi = Mathf.Pi / 8f,
		/// <summary>
		/// The weapon sprites are not drawn to world scale so they need their own inferred scale.
		/// The pistol in the PC release is the Walther P38.
		/// The pistol is 9 pixels across.
		/// Wikipedia reports that the width of the Walther P38 is 36 mm. (1.4 in)
		/// So 1 pixel = 4 millimeters.
		/// </summary>
		WeaponMetersPerPixel = 0.004f,
		WeaponWidth = WeaponMetersPerPixel * 64f,
		WeaponHeight = WeaponWidth * 1.2f,
		/// <summary>
		/// This value is used to determine how big the player's head is for collision detection
		/// </summary>
		HeadXZ = PixelWidth * 3f;
	public static readonly float HeadDiagonal = Mathf.Sqrt(Mathf.Pow(HeadXZ, 2) * 2f), // Pythagorean theorem
		ShotRange = Mathf.Sqrt(Mathf.Pow(64f * TileWidth, 2) * 2f + Mathf.Pow(TileHeight, 2));
	public static readonly QuadMesh WallMesh = new()
	{
		Size = new Vector2(TileWidth, TileHeight),
	};
	public static readonly BoxShape3D BoxMesh = new()
	{
		Size = new Vector3(TileWidth, TileHeight, TileWidth),
	};
	public static readonly Vector3 Scale = new(1f, 1.2f, 1f),
		WeaponScale = new(WeaponWidth, WeaponMetersPerPixel, WeaponHeight),
		Rotate90 = new(0, HalfPi, 0);
	public static readonly Color White = Godot.Color.Color8(255, 255, 255, 255);
}
