using BenMcLean.Wolf3D.Shared.Menu.Input;
using BenMcLean.Wolf3D.VR.VR;
using Godot;

namespace BenMcLean.Wolf3D.VR.Menu;

/// <summary>
/// Pointer provider for VR mode.
/// Tracks VR controller ray intersections with the menu panel.
/// </summary>
public class VRMenuPointerProvider : IMenuPointerProvider
{
	private readonly IDisplayMode _displayMode;
	private PointerState _primaryPointer;
	private PointerState _secondaryPointer;
	private MeshInstance3D _menuPanel;
	private float _panelWidth;
	private float _panelHeight;
	private bool _wasPrimaryTriggerPressed;
	private bool _wasPrimaryGripPressed;
	private bool _wasSecondaryTriggerPressed;
	private bool _wasSecondaryGripPressed;

	/// <inheritdoc/>
	public PointerState PrimaryPointer => _primaryPointer;

	/// <inheritdoc/>
	public PointerState SecondaryPointer => _secondaryPointer;

	/// <summary>
	/// Creates a new VR menu pointer provider.
	/// </summary>
	/// <param name="displayMode">The VR display mode for accessing controller data.</param>
	public VRMenuPointerProvider(IDisplayMode displayMode)
	{
		_displayMode = displayMode;
	}

	/// <summary>
	/// Sets the menu panel to ray cast against.
	/// </summary>
	/// <param name="panel">The menu panel MeshInstance3D.</param>
	/// <param name="width">Panel width in meters.</param>
	/// <param name="height">Panel height in meters.</param>
	public void SetMenuPanel(MeshInstance3D panel, float width, float height)
	{
		_menuPanel = panel;
		_panelWidth = width;
		_panelHeight = height;
	}

	/// <inheritdoc/>
	public void Update(float delta)
	{
		if (_menuPanel == null || !_displayMode.IsVRActive)
		{
			_primaryPointer = new PointerState { IsActive = false };
			_secondaryPointer = new PointerState { IsActive = false };
			return;
		}

		// Check button states (detect "just pressed" by comparing to previous frame)
		bool primaryTrigger = _displayMode.IsPrimaryHandTriggerPressed();
		bool primaryGrip = _displayMode.IsPrimaryHandGripPressed();
		bool secondaryTrigger = _displayMode.IsSecondaryHandTriggerPressed();
		bool secondaryGrip = _displayMode.IsSecondaryHandGripPressed();

		bool primarySelectPressed = primaryTrigger && !_wasPrimaryTriggerPressed;
		bool primaryCancelPressed = primaryGrip && !_wasPrimaryGripPressed;
		bool secondarySelectPressed = secondaryTrigger && !_wasSecondaryTriggerPressed;
		bool secondaryCancelPressed = secondaryGrip && !_wasSecondaryGripPressed;

		_wasPrimaryTriggerPressed = primaryTrigger;
		_wasPrimaryGripPressed = primaryGrip;
		_wasSecondaryTriggerPressed = secondaryTrigger;
		_wasSecondaryGripPressed = secondaryGrip;

		// Cast ray from primary (right) controller
		_primaryPointer = CastControllerRay(
			_displayMode.PrimaryHandPosition,
			_displayMode.PrimaryHandForward,
			primarySelectPressed,
			primaryCancelPressed);

		// Cast ray from secondary (left) controller
		_secondaryPointer = CastControllerRay(
			_displayMode.SecondaryHandPosition,
			_displayMode.SecondaryHandForward,
			secondarySelectPressed,
			secondaryCancelPressed);
	}

	/// <summary>
	/// Casts a ray from a controller and checks for intersection with the menu panel.
	/// </summary>
	/// <param name="origin">Ray origin (controller position).</param>
	/// <param name="direction">Ray direction (controller forward).</param>
	/// <param name="selectPressed">True if select button just pressed this frame.</param>
	/// <param name="cancelPressed">True if cancel button just pressed this frame.</param>
	/// <returns>Pointer state with intersection result.</returns>
	private PointerState CastControllerRay(Vector3 origin, Vector3 direction, bool selectPressed, bool cancelPressed)
	{
		// Get panel transform
		Transform3D panelTransform = _menuPanel.GlobalTransform;
		Vector3 panelPosition = panelTransform.Origin;
		Vector3 panelNormal = panelTransform.Basis.Z; // Panel's front face (+Z) points toward camera

		// Ray-plane intersection
		// t = dot(panelPosition - origin, normal) / dot(direction, normal)
		float denominator = direction.Dot(panelNormal);

		// Check if ray is parallel to panel (or pointing away)
		if (Mathf.Abs(denominator) < 0.0001f)
			return new PointerState { IsActive = false, CancelPressed = cancelPressed };

		float t = (panelPosition - origin).Dot(panelNormal) / denominator;

		// Check if intersection is behind the controller
		if (t < 0)
			return new PointerState { IsActive = false, CancelPressed = cancelPressed };

		// Calculate intersection point in world space
		Vector3 intersection = origin + direction * t;

		// Convert to panel-local coordinates
		// The panel is centered at panelPosition, with X going right and Y going up
		Vector3 localPoint = panelTransform.AffineInverse() * intersection;

		// Convert from local 3D coords to 2D panel coords
		// Panel mesh is centered at origin, so coordinates range from -width/2 to width/2
		float u = (localPoint.X + _panelWidth / 2f) / _panelWidth;
		float v = (-localPoint.Y + _panelHeight / 2f) / _panelHeight; // Flip Y for screen coords

		// Check if within panel bounds
		if (u < 0 || u > 1 || v < 0 || v > 1)
			return new PointerState { IsActive = false, CancelPressed = cancelPressed };

		// Convert to menu viewport coordinates (0-320 x 0-200)
		return new PointerState
		{
			IsActive = true,
			Position = new Vector2(u * 320f, v * 200f),
			SelectPressed = selectPressed,
			CancelPressed = cancelPressed
		};
	}
}
