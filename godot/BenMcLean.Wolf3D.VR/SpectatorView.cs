using Godot;

namespace BenMcLean.Wolf3D.VR;

/// <summary>
/// Desktop-only spectator compositor for VR gameplay capture.
/// Renders a separate first-person camera into the root viewport (desktop window)
/// while the HMD continues using Godot's normal XR rendering path.
/// The spectator Camera3D is a direct scene-tree node so MovieWriter captures it.
/// </summary>
public partial class SpectatorView : Node
{
	private enum SpectatorMode
	{
		Inactive,
		WorldCamera,
		DirectTexture,
	}

	private const float DefaultFov = 75f;
	private const float PositionSharpness = 10f;
	private const float DirectionSharpness = 12f;

	// Placed directly in the scene tree so it renders to the root viewport.
	// In Godot 4 XR mode, a regular Camera3D with Current=true renders to the
	// desktop window independently of the XRCamera3D's HMD path, which is what
	// lets MovieWriter (which attaches to the root viewport) capture spectator output.
	private readonly Camera3D _camera = new()
	{
		Name = "SpectatorCamera",
		Current = false,
		Near = 0.05f,
		Fov = DefaultFov,
	};

	// Used only in DirectTexture mode (menus) to overlay a 2D texture.
	private readonly CanvasLayer _canvasLayer = new()
	{
		Name = "SpectatorCanvas",
		Layer = 0,
	};

	private readonly ColorRect _background = new()
	{
		Name = "SpectatorBackground",
		Color = Colors.Black,
		AnchorLeft = 0f,
		AnchorTop = 0f,
		AnchorRight = 1f,
		AnchorBottom = 1f,
	};

	// Wolf3D menus are 320×200 but designed for a 4:3 CRT (non-square pixels).
	// AspectRatioContainer enforces 4:3 display ratio, pillarboxing into the 16:9 viewport.
	private readonly AspectRatioContainer _menuContainer = new()
	{
		Name = "MenuContainer",
		Ratio = 4f / 3f,
		StretchMode = AspectRatioContainer.StretchModeEnum.Fit,
		AlignmentHorizontal = AspectRatioContainer.AlignmentMode.Center,
		AlignmentVertical = AspectRatioContainer.AlignmentMode.Center,
		AnchorLeft = 0f,
		AnchorTop = 0f,
		AnchorRight = 1f,
		AnchorBottom = 1f,
	};

	private readonly TextureRect _output = new()
	{
		Name = "SpectatorOutput",
		AnchorLeft = 0f,
		AnchorTop = 0f,
		AnchorRight = 1f,
		AnchorBottom = 1f,
		ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
		StretchMode = TextureRect.StretchModeEnum.Scale,
		TextureFilter = Control.TextureFilterEnum.Nearest,
	};

	private Camera3D _trackedCamera;
	private Node3D _vrOrigin;
	private SpectatorMode _mode;
	private bool _hasPose;
	private Vector3 _smoothedPosition = Vector3.Zero;
	private Vector3 _smoothedForward = Vector3.Forward;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;

		AddChild(_camera);

		AddChild(_canvasLayer);
		_canvasLayer.AddChild(_background);
		_canvasLayer.AddChild(_menuContainer);
		_menuContainer.AddChild(_output);

		// Render the root viewport at a fixed 1920×1080 regardless of OS window size.
		// ContentScaleModeEnum.Viewport renders the scene at ContentScaleSize and scales to the
		// window for display. MovieWriter reads the render target (always 1920×1080), not the OS
		// window, so recording resolution is stable even if the XR runtime resizes the window.
		Window root = GetTree().Root;
		root.ContentScaleMode = Window.ContentScaleModeEnum.Viewport;
		root.ContentScaleSize = RuntimeOptions.SpectatorResolution;

		SetMode(SpectatorMode.Inactive);
	}

	public override void _Process(double delta)
	{
		if (_mode != SpectatorMode.WorldCamera || _trackedCamera is null || !GodotObject.IsInstanceValid(_trackedCamera))
			return;

		UpdateCameraPose((float)delta);
	}

	/// <summary>
	/// Starts following the tracked VR camera with the spectator camera.
	/// The spectator camera renders directly to the root viewport so MovieWriter captures it.
	/// vrOrigin is the XROrigin3D; its GlobalBasis is composed with the camera's local Basis
	/// to correctly compute yaw even when the XR rig lives inside a SubViewport.
	/// </summary>
	public void AttachTo(Node3D vrOrigin, Camera3D trackedCamera)
	{
		_vrOrigin = vrOrigin;
		_trackedCamera = trackedCamera;
		_hasPose = false;
		SetMode(trackedCamera is not null ? SpectatorMode.WorldCamera : SpectatorMode.Inactive);
	}

	/// <summary>
	/// Shows a pre-rendered 2D texture directly in the spectator window.
	/// Used for menu capture so recordings see the menu artwork itself.
	/// </summary>
	public void AttachTexture(Texture2D texture)
	{
		_trackedCamera = null;
		_hasPose = false;
		_output.Texture = texture;
		SetMode(texture is not null ? SpectatorMode.DirectTexture : SpectatorMode.Inactive);
	}

	/// <summary>
	/// Stops rendering the spectator feed.
	/// </summary>
	public void Detach()
	{
		_vrOrigin = null;
		_trackedCamera = null;
		_hasPose = false;
		_output.Texture = null;
		SetMode(SpectatorMode.Inactive);
	}

	private void UpdateCameraPose(float delta)
	{
		// XRCamera3D lives inside a SubViewport; GlobalBasis doesn't always propagate yaw
		// across the viewport boundary. Explicitly compose XROrigin's world-space basis with
		// the camera's local basis to get the true world-space orientation (pitch + yaw, no roll).
		bool hasOrigin = _vrOrigin is not null && GodotObject.IsInstanceValid(_vrOrigin);
		Basis worldBasis = hasOrigin
			? _vrOrigin.GlobalBasis * _trackedCamera.Basis
			: _trackedCamera.GlobalBasis;
		Vector3 trackedPosition = hasOrigin
			? _vrOrigin.GlobalPosition + _vrOrigin.GlobalBasis * _trackedCamera.Position
			: _trackedCamera.GlobalPosition;
		Vector3 hmdForward = (-worldBasis.Z).Normalized();
		Vector3 desiredForward = ComputeStableForward(hmdForward);

		if (!_hasPose)
		{
			_smoothedPosition = trackedPosition;
			_smoothedForward = desiredForward;
			_hasPose = true;
		}
		else
		{
			_smoothedPosition = _smoothedPosition.Lerp(
				trackedPosition,
				ExponentialBlend(PositionSharpness, delta));
			_smoothedForward = _smoothedForward.Slerp(
				desiredForward,
				ExponentialBlend(DirectionSharpness, delta)).Normalized();
		}

		_camera.GlobalPosition = _smoothedPosition;
		_camera.GlobalBasis = Basis.LookingAt(_smoothedForward, Vector3.Up);
		_camera.Fov = _trackedCamera.Fov > 1f ? _trackedCamera.Fov : DefaultFov;
	}

	private static Vector3 ComputeStableForward(Vector3 hmdForward)
	{
		Vector3 flatForward = new Vector3(hmdForward.X, 0f, hmdForward.Z);
		if (flatForward.LengthSquared() < 0.0001f)
			return Vector3.Forward;

		flatForward = flatForward.Normalized();

		float clampedPitch = Mathf.Clamp(hmdForward.Y, -0.95f, 0.95f);
		float horizontalScale = Mathf.Sqrt(Mathf.Max(0.0f, 1.0f - clampedPitch * clampedPitch));

		return new Vector3(
			flatForward.X * horizontalScale,
			clampedPitch,
			flatForward.Z * horizontalScale).Normalized();
	}

	private void SetMode(SpectatorMode mode)
	{
		_mode = mode;
		switch (mode)
		{
			case SpectatorMode.WorldCamera:
				_camera.Current = true;
				_canvasLayer.Visible = false;
				_menuContainer.Visible = false;
				break;
			case SpectatorMode.DirectTexture:
				_camera.Current = false;
				_canvasLayer.Visible = true;
				_background.Visible = true;
				_menuContainer.Visible = _output.Texture is not null;
				break;
			default:
				_camera.Current = false;
				_canvasLayer.Visible = false;
				_menuContainer.Visible = false;
				break;
		}
	}

	private static float ExponentialBlend(float sharpness, float delta) =>
		1f - Mathf.Exp(-sharpness * delta);
}
