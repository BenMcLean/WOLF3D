using Godot;

namespace BenMcLean.Wolf3D.VR;

/// <summary>
/// Desktop-only spectator compositor for VR gameplay capture.
/// Renders a separate first-person camera into the desktop window while the HMD
/// continues using Godot's normal XR rendering path.
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

	private readonly SubViewport _viewport = new()
	{
		Name = "SpectatorViewport",
		TransparentBg = false,
		HandleInputLocally = false,
		RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
		Disable3D = false,
		Size = new Vector2I(1280, 720),
	};

	private readonly Camera3D _camera = new()
	{
		Name = "SpectatorCamera",
		Current = true,
		Near = 0.05f,
		Fov = DefaultFov,
	};

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

	private readonly TextureRect _output = new()
	{
		Name = "SpectatorOutput",
		AnchorLeft = 0f,
		AnchorTop = 0f,
		AnchorRight = 1f,
		AnchorBottom = 1f,
		ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
		StretchMode = TextureRect.StretchModeEnum.Scale,
		TextureFilter = Control.TextureFilterEnum.Linear,
	};

	private Camera3D _trackedCamera;
	private SpectatorMode _mode;
	private bool _active;
	private bool _hasPose;
	private Vector3 _smoothedPosition = Vector3.Zero;
	private Vector3 _smoothedForward = Vector3.Forward;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;

		AddChild(_viewport);
		_viewport.AddChild(_camera);

		AddChild(_canvasLayer);
		_canvasLayer.AddChild(_background);
		_canvasLayer.AddChild(_output);
		_output.Texture = _viewport.GetTexture();

		SetActive(false);
		SyncViewportSize();
	}

	public override void _Process(double delta)
	{
		SyncViewportSize();

		if (!_active || _mode != SpectatorMode.WorldCamera || _trackedCamera is null || !GodotObject.IsInstanceValid(_trackedCamera))
			return;

		UpdateCameraPose((float)delta);
	}

	/// <summary>
	/// Shares the source room's 3D world with the spectator viewport and starts
	/// following the tracked VR camera.
	/// </summary>
	public void AttachTo(Node3D roomRoot, Camera3D trackedCamera)
	{
		_mode = SpectatorMode.WorldCamera;
		_trackedCamera = trackedCamera;
		_hasPose = false;

		Viewport sourceViewport = roomRoot?.GetViewport();
		_viewport.World3D = sourceViewport?.World3D;
		_output.Texture = _viewport.GetTexture();

		SetActive(_viewport.World3D is not null && trackedCamera is not null);
	}

	/// <summary>
	/// Shows a pre-rendered 2D texture directly in the spectator window.
	/// Used for menu capture so recordings see the menu artwork itself.
	/// </summary>
	public void AttachTexture(Texture2D texture)
	{
		_mode = SpectatorMode.DirectTexture;
		_trackedCamera = null;
		_viewport.World3D = null;
		_hasPose = false;
		_output.Texture = texture;
		SetActive(texture is not null);
	}

	/// <summary>
	/// Stops rendering the spectator feed and reveals the normal desktop output.
	/// </summary>
	public void Detach()
	{
		_mode = SpectatorMode.Inactive;
		_trackedCamera = null;
		_viewport.World3D = null;
		_hasPose = false;
		_output.Texture = _viewport.GetTexture();
		SetActive(false);
	}

	private void UpdateCameraPose(float delta)
	{
		Vector3 trackedPosition = _trackedCamera.GlobalPosition;
		Vector3 hmdForward = (-_trackedCamera.GlobalBasis.Z).Normalized();
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

	private void SetActive(bool active)
	{
		_active = active;
		_canvasLayer.Visible = active;
		_output.Visible = active;
		_background.Visible = active;
	}

	private void SyncViewportSize()
	{
		Vector2I windowSize = DisplayServer.WindowGetSize();
		if (windowSize.X <= 0 || windowSize.Y <= 0)
			return;

		if (_viewport.Size != windowSize)
			_viewport.Size = windowSize;
	}

	private static float ExponentialBlend(float sharpness, float delta) =>
		1f - Mathf.Exp(-sharpness * delta);
}
