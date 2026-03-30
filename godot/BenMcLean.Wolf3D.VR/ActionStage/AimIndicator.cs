using BenMcLean.Wolf3D.VR.VR;
using Godot;

namespace BenMcLean.Wolf3D.VR.ActionStage;

/// <summary>
/// Crosshair aim indicator for pixel-perfect aiming.
/// In VR: displays a crosshair at each hand's aim point (one per controller).
/// In flatscreen: displays a single crosshair at the camera's aim point.
/// The crosshair lies parallel to the aimed surface, floats one pixel above it,
/// and rotates around the surface normal to match the controller's orientation.
/// Hides when the ray misses (e.g., pointing outside the level).
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
	/// Creates a crosshair quad that marks an aim point on surfaces.
	/// Size matches the 13x11 pixel crosshair with mode-13h non-square pixels.
	/// </summary>
	private static MeshInstance3D CreateAimPoint(string name)
	{
		QuadMesh crosshairMesh = new()
		{
			Size = new Vector2(6.5f * Constants.PixelWidth, 5.5f * Constants.PixelHeight),
		};
		MeshInstance3D aimPoint = new()
		{
			Mesh = crosshairMesh,
			MaterialOverride = VRAssetManager.CrosshairMaterial,
			Name = name,
			Visible = false,
		};
		return aimPoint;
	}

	public override void _Ready()
	{
		AddChild(_aimPoint0);
		if (_aimPoint1 is not null)
			AddChild(_aimPoint1);
	}

	/// <summary>
	/// Updates the aim point positions and orientations each frame by raycasting from each hand.
	/// </summary>
	public override void _Process(double delta)
	{
		Vector3 cameraForward = -_displayMode.Camera.GlobalTransform.Basis.Z;

		// Hand 0 (right in VR, camera in flatscreen)
		Vector3 origin0 = _displayMode.IsVRActive
			? _displayMode.GetHandPosition(0)
			: _displayMode.Camera.GlobalPosition;
		Hit0 = _aiming.Raycast(origin0, _displayMode.GetHandForward(0), cameraForward);
		UpdateAimPoint(_aimPoint0, Hit0, 0);

		// Hand 1 (left in VR only)
		if (_aimPoint1 != null)
		{
			Hit1 = _aiming.Raycast(_displayMode.GetHandPosition(1), _displayMode.GetHandForward(1), cameraForward);
			UpdateAimPoint(_aimPoint1, Hit1, 1);
		}
	}

	/// <summary>
	/// Updates a crosshair quad's visibility, position, and orientation for a raycast hit.
	/// The crosshair is placed parallel to the hit surface, offset one pixel above it,
	/// and rotated around the surface normal to match the controller's roll.
	/// </summary>
	private void UpdateAimPoint(MeshInstance3D aimPoint, PixelPerfectAiming.AimHitResult hit, int handIndex)
	{
		if (!hit.IsHit)
		{
			aimPoint.Visible = false;
			return;
		}
		Vector3 normal = hit.Normal;
		// Offset crosshair one pixel above the surface
		Vector3 position = hit.Position + normal * Constants.PixelWidth;
		// Orient crosshair parallel to the surface, with Z-rotation matching the controller.
		// Project the controller's local up onto the surface plane to get crosshair up direction.
		Node3D handNode = _displayMode.GetHandNode(handIndex);
		Vector3 handUp = handNode.GlobalTransform.Basis.Y;
		Vector3 crosshairUp = handUp - handUp.Dot(normal) * normal;
		if (crosshairUp.LengthSquared() < 0.001f)
		{
			// Controller up is parallel to normal (e.g., pointing straight at floor).
			// Fall back to world up projected onto the surface plane.
			crosshairUp = Vector3.Up - Vector3.Up.Dot(normal) * normal;
			if (crosshairUp.LengthSquared() < 0.001f)
			{
				// Normal is world up/down (floor/ceiling). Use world forward as fallback.
				crosshairUp = Vector3.Forward - Vector3.Forward.Dot(normal) * normal;
			}
		}
		crosshairUp = crosshairUp.Normalized();
		// Build basis: X=right, Y=up, Z=face normal (quad faces in +Z local direction)
		Vector3 crosshairRight = crosshairUp.Cross(normal);
		aimPoint.GlobalTransform = new Transform3D(
			new Basis(crosshairRight, crosshairUp, normal),
			position
		);
		aimPoint.Visible = true;
	}
}
