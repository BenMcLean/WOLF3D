using Godot;

namespace BenMcLean.Wolf3D.VR;

/// <summary>
/// Debug visualization for pixel-perfect aiming.
/// Displays a red sphere at the point where the camera's ray hits something.
/// This is a temporary debug tool and will not be in the final game.
/// </summary>
public partial class AimIndicator : Node3D
{
	private readonly PixelPerfectAiming _aiming;
	private readonly Camera3D _camera;
	private MeshInstance3D _aimPoint;
	/// <summary>
	/// The most recent raycast result. Updated every frame.
	/// </summary>
	public PixelPerfectAiming.AimHitResult CurrentHit { get; private set; }
	/// <summary>
	/// Creates the aim indicator for camera-based debugging.
	/// </summary>
	/// <param name="aiming">Pixel-perfect aiming system to use for raycasting</param>
	/// <param name="camera">Camera to track</param>
	public AimIndicator(PixelPerfectAiming aiming, Camera3D camera)
	{
		_aiming = aiming;
		_camera = camera;
		CreateAimPoint();
		// Initialize with no hit
		CurrentHit = new PixelPerfectAiming.AimHitResult { IsHit = false };
	}
	/// <summary>
	/// Creates the red sphere that marks the aim point.
	/// </summary>
	private void CreateAimPoint()
	{
		SphereMesh sphereMesh = new()
		{
			Radius = 0.05f, // 5cm sphere
			Height = 0.1f,
			RadialSegments = 8,
			Rings = 4,
		};
		StandardMaterial3D material = new()
		{
			AlbedoColor = Colors.Red,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		};
		_aimPoint = new MeshInstance3D
		{
			Mesh = sphereMesh,
			MaterialOverride = material,
			Name = "AimPoint",
			Visible = false, // Hidden until we have a hit
		};
		AddChild(_aimPoint);
	}
	/// <summary>
	/// Updates the aim point position each frame by raycasting from the camera.
	/// </summary>
	public override void _Process(double delta)
	{
		// Get camera ray
		Vector3 rayOrigin = _camera.GlobalPosition,
			rayDirection = -_camera.GlobalTransform.Basis.Z,
			cameraForward = rayDirection;
		// Perform raycast
		CurrentHit = _aiming.Raycast(rayOrigin, rayDirection, cameraForward);
		// Update visual indicator
		if (CurrentHit.IsHit)
		{
			_aimPoint.Position = CurrentHit.Position;
			_aimPoint.Visible = true;
		}
		else
			_aimPoint.Visible = false;
	}
}
