using Godot;

namespace BenMcLean.Wolf3D.VR;

public static class Constants
{
	// Tom Hall's Doom Bible and also tweets from John Carmack state that the walls in Wolfenstein 3-D were always eight feet thick. The wall textures are 64x64 pixels, which means that the ratio is 8 pixels per foot.
	// However, VR uses the metric system, where 1 game unit is 1 meter in real space. One foot equals 0.3048 meters.
	public const float Foot = 0.3048f,
		Inch = Foot / 12f,
		// Now, unless I am a complete failure at basic math, (quite possible) this means that to scale Wolfenstein 3-D correctly in VR, one pixel must equal 0.0381 in meters, and a Wolfenstein 3-D wall must be 2.4384 meters thick.
		PixelWidth = 0.0381f,
		WallWidth = 2.4384f,
		HalfWallWidth = 1.2192f,
		// Wolfenstein 3-D ran in SVGA screen mode 13h, which has a 320x200 resolution in a 4:3 aspect ratio.
		// This means that the pixels are not square! They have a 1.2:1 aspect ratio.
		PixelHeight = 0.04572f,
		WallHeight = 2.92608f,
		HalfWallHeight = 1.46304f,
		// Wolfenstein 3-D counts time as "tics" which varies by framerate.
		// We don't want to vary, so 1 second = 70 tics, regardless of framerate.
		TicsPerSecond = 70f,
		Tic = 1f / TicsPerSecond,
		// I made up a new unit of measure called the Zenos for this project.
		// 1 Zenos is defined as 1 / 65536th of a WallWidth, used by Wolfenstein 3-D to measure how far an actor is off center from their square. This number comes from the size of a 16-bit integer.
		// 65536 Zenos per wall / 512 guard speed = 128 tics per wall
		// 128 tics per wall / 70 tics per second = 1.828571428571429 seconds per wall
		// 2.4384 meters per wall / 1.828571428571429 seconds per wall = 1.3335 meters per second
		// 70 tics per second * 2.4384 meters per wall / 65536 Zenos per wall = 0.0026044921875 (meters * tic) / (Zenos * second)
		// Check: 512 guard speed * 1 second delta * 0.0026044921875 ActorSpeedConversion = 1.3335 meters per second
		// 0.0026044921875
		ActorSpeedConversion = TicsPerSecond * WallWidth / 65536f,
		// Tests reveal that BJ's run speed is 11.2152 tiles/sec. http://diehardwolfers.areyep.com/viewtopic.php?p=82938#82938
		// 11.2152 tiles per second * 2.4384 meters per tile = 27.34714368 meters per second
		// Walking speed is half of running speed.
		RunSpeed = 27.34714368f,
		WalkSpeed = 13.67357184f,
		DeadZone = 0.5f,
		HalfPi = Mathf.Pi / 2f,
		QuarterPi = Mathf.Pi / 4f,
		/// <summary>
		/// This value is used to determine how big the player's head is for collision detection
		/// </summary>
		HeadXZ = PixelWidth * 3f;
	public static readonly float HeadDiagonal = Mathf.Sqrt(Mathf.Pow(HeadXZ, 2) * 2f), // Pythagorean theorem
		ShotRange = Mathf.Sqrt(Mathf.Pow(64 * WallWidth, 2) * 2f + Mathf.Pow(WallHeight, 2));
	/// <param name="x">A distance in meters</param>
	/// <returns>The corresponding map coordinate</returns>
	public static int IntCoordinate(float x) => Mathf.FloorToInt(x / WallWidth);
	/// <param name="x">A map coordinate</param>
	/// <returns>Center of the map square in meters</returns>
	public static float CenterSquare(int x) => FloatCoordinate(x) + HalfWallWidth;
	public static float CenterSquare(uint x) => CenterSquare((int)x);
	public static float CenterSquare(ushort x) => CenterSquare((int)x);
	public static float CenterSquare(short x) => CenterSquare((int)x);
	/// <param name="x">A map coordinate</param>
	/// <returns>North or east corner of map square in meters</returns>
	public static float FloatCoordinate(int x) => x * WallWidth;
	public static float FloatCoordinate(uint x) => FloatCoordinate((int)x);
	public static float FloatCoordinate(ushort x) => FloatCoordinate((int)x);
	public static float FloatCoordinate(short x) => FloatCoordinate((int)x);
	public static readonly Vector3 Scale = new(1f, 1.2f, 1f);
	//public static readonly Transform WallTransform = new Transform(Basis.Identity, new Vector3(HalfWallWidth, HalfWallHeight, 0));
	//public static readonly Transform WallTransformFlipped = new Transform(Basis.Identity.Rotated(Godot.Vector3.Up, Mathf.Pi), WallTransform.origin);
	public static float TicsToSeconds(int tics) => tics / TicsPerSecond;
	public static short SecondsToTics(float seconds) => (short)(seconds * TicsPerSecond);
	public static readonly QuadMesh WallMesh = new()
	{
		Size = new Vector2(WallWidth, WallHeight),
	};
	public static readonly BoxShape3D BoxShape = new()
	{
		Size = new Vector3(WallWidth, WallHeight, WallWidth),
	};
	public static readonly Vector3 Rotate90 = new(0, Godot.Mathf.Pi / 2f, 0);
	public static Vector2 Vector2(this Vector3 vector3) => new(vector3.X, vector3.Z);
	public static Vector3 Vector3(this Vector2 vector2) => new(vector2.X, 0f, vector2.Y);
	public static Vector3 Axis(Vector3.Axis axis) => axis switch
	{
		Godot.Vector3.Axis.X => Rotate90,
		Godot.Vector3.Axis.Y => Godot.Vector3.Up,
		_ => Godot.Vector3.Zero,
	};
	public static readonly Godot.Color White = Godot.Color.Color8(255, 255, 255, 255);
}
