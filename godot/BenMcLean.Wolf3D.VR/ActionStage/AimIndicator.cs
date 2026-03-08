using BenMcLean.Wolf3D.VR.VR;
using Godot;

namespace BenMcLean.Wolf3D.VR.ActionStage;

/// <summary>
/// Debug visualization for pixel-perfect aiming.
/// In VR: displays a red sphere at each hand's aim point (one per controller).
/// In flatscreen: displays a single red sphere at the camera's aim point.
/// This is a temporary debug tool and will not be in the final game.
/// </summary>
public partial class AimIndicator : Node3D
{
	private readonly PixelPerfectAiming _aiming;
	private readonly IDisplayMode _displayMode;
	private MeshInstance3D _aimPoint0;
	private MeshInstance3D _aimPoint1;

	/// <summary>
	/// The most recent raycast result for hand 0 (right/primary). Updated every frame.
	/// </summary>
	public PixelPerfectAiming.AimHitResult Hit0 { get; private set; }

	/// <summary>
	/// The most recent raycast result for hand 1 (left/secondary, VR only). Updated every frame.
	/// </summary>
	public PixelPerfectAiming.AimHitResult Hit1 { get; private set; }

	/// <summary>
	/// Creates the aim indicator.
	/// </summary>
	/// <param name="aiming">Pixel-perfect aiming system to use for raycasting</param>
	/// <param name="displayMode">Display mode to get hand positions and directions from</param>
	public AimIndicator(PixelPerfectAiming aiming, IDisplayMode displayMode)
	{
		_aiming = aiming;
		_displayMode = displayMode;
		_aimPoint0 = CreateAimPoint("AimPoint0");
		if (displayMode.IsVRActive)
			_aimPoint1 = CreateAimPoint("AimPoint1");
		Hit0 = new PixelPerfectAiming.AimHitResult { IsHit = false };
		Hit1 = new PixelPerfectAiming.AimHitResult { IsHit = false };
	}

	/// <summary>
	/// Creates a red sphere that marks an aim point.
	/// </summary>
	private MeshInstance3D CreateAimPoint(string name)
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
		MeshInstance3D aimPoint = new()
		{
			Mesh = sphereMesh,
			MaterialOverride = material,
			Name = name,
			Visible = false,
		};
		AddChild(aimPoint);
		return aimPoint;
	}

	/// <summary>
	/// Updates the aim point positions each frame by raycasting from each hand.
	/// </summary>
	public override void _Process(double delta)
	{
		Vector3 cameraForward = -_displayMode.Camera.GlobalTransform.Basis.Z;

		// Hand 0 (right in VR, camera in flatscreen)
		Vector3 origin0 = _displayMode.IsVRActive
			? _displayMode.GetHandPosition(0)
			: _displayMode.Camera.GlobalPosition;
		Hit0 = _aiming.Raycast(origin0, _displayMode.GetHandForward(0), cameraForward);
		UpdateAimPoint(_aimPoint0, Hit0);

		// Hand 1 (left in VR only)
		if (_aimPoint1 != null)
		{
			Hit1 = _aiming.Raycast(_displayMode.GetHandPosition(1), _displayMode.GetHandForward(1), cameraForward);
			UpdateAimPoint(_aimPoint1, Hit1);
		}
	}

	private static void UpdateAimPoint(MeshInstance3D aimPoint, PixelPerfectAiming.AimHitResult hit)
	{
		if (hit.IsHit)
		{
			aimPoint.Position = hit.Position;
			aimPoint.Visible = true;
		}
		else
			aimPoint.Visible = false;
	}
}
