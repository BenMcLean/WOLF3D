using BenMcLean.Wolf3D.Shared.Menu.Input;
using BenMcLean.Wolf3D.VR.VR;
using Godot;

namespace BenMcLean.Wolf3D.VR.MenuStage;

/// <summary>
/// Pointer provider for VR mode.
/// Tracks VR controller ray intersections with the menu panel.
/// Uses event-driven input from XRController3D button signals.
/// Hand 0 = right controller, hand 1 = left controller.
/// </summary>
public class VRMenuPointerProvider : IMenuPointerProvider
{
	private readonly IDisplayMode _displayMode;
	private PointerState _primaryPointer;
	private PointerState _secondaryPointer;
	private MeshInstance3D _menuPanel;
	private float _panelWidth;
	private float _panelHeight;

	// Event-driven button states, indexed by hand (0 = right, 1 = left)
	private bool _selectPressed0;
	private bool _cancelPressed0;
	private bool _selectPressed1;
	private bool _cancelPressed1;

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

		// Subscribe to controller button events
		_displayMode.HandButtonPressed += OnHandButtonPressed;
	}

	private void OnHandButtonPressed(int handIndex, string buttonName)
	{
		if (handIndex == 0)
		{
			if (buttonName == "trigger_click") _selectPressed0 = true;
			else if (buttonName == "grip_click") _cancelPressed0 = true;
		}
		else if (handIndex == 1)
		{
			if (buttonName == "trigger_click") _selectPressed1 = true;
			else if (buttonName == "grip_click") _cancelPressed1 = true;
		}
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
	/// <remarks>
	/// VR controller input comes through IDisplayMode events, not InputEvent.
	/// This method is a no-op for VR.
	/// </remarks>
	public void HandleInput(InputEvent @event)
	{
		// VR input comes through IDisplayMode button events
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

		// Capture and clear button states (they were set by event handlers)
		bool select0 = _selectPressed0, cancel0 = _cancelPressed0;
		bool select1 = _selectPressed1, cancel1 = _cancelPressed1;
		_selectPressed0 = false;
		_cancelPressed0 = false;
		_selectPressed1 = false;
		_cancelPressed1 = false;

		// Cast ray from hand 0 (right controller)
		_primaryPointer = CastControllerRay(
			_displayMode.GetHandPosition(0),
			_displayMode.GetHandForward(0),
			select0,
			cancel0);

		// Cast ray from hand 1 (left controller)
		_secondaryPointer = CastControllerRay(
			_displayMode.GetHandPosition(1),
			_displayMode.GetHandForward(1),
			select1,
			cancel1);
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
