using Godot;

namespace BenMcLean.Wolf3D.VR;

public partial class Root : Node3D
{
	private Camera3D _camera;
	private FreeLookCamera _freeLookCamera;
	private Sky _sky;
	private readonly ShaderMaterial _skyMaterial = new()
	{
		Shader = new() { Code = """
shader_type sky;

uniform vec4 floor_color;
uniform vec4 ceiling_color;

void sky() {
	COLOR = mix(floor_color.rgb, ceiling_color.rgb, step(0.0, EYEDIR.y));
}
""", }
	};
	public WorldEnvironment WorldEnvironment;
	public Assets.Assets Assets;
	public override void _Ready()
	{
		AddChild(WorldEnvironment = new()
		{
			Environment = new()
			{
				Sky = new()
				{
					SkyMaterial = _skyMaterial,
				},
				BackgroundMode = Environment.BGMode.Sky,
			}
		});
		_skyMaterial.SetShaderParameter("floor_color", new Color(0f, 1f, 0f, 1f));
		_skyMaterial.SetShaderParameter("ceiling_color", new Color(0f, 0f, 1f, 1f));
		_camera = new()
		{
			Position = new Vector3(0, Constants.HalfWallHeight, 0),
			RotationDegrees = new Vector3(0, 0, 0),
			Current = true,
		};
		AddChild(_camera);
		_freeLookCamera = new();
		_camera.AddChild(_freeLookCamera);
		_freeLookCamera.Enabled = true;
		Assets = BenMcLean.Wolf3D.Assets.Assets.Load(@"..\..\games\WL1.xml");
		GD.Print($"Game Maps: {Assets.Maps.Length}");
	}
	public override void _Process(double delta)
	{
	}
}
