using Godot;

public partial class Root : Node3D
{
	private Camera3D _camera;
	private Sky _sky;
	private readonly ShaderMaterial _skyMaterial = new()
	{
		Shader = new() { Code = """
shader_type sky;

uniform vec4 ground_color;
uniform vec4 sky_color;

void sky() {
	if (EYEDIR.y < 0.0) {
		COLOR = ground_color.rgb;
	} else {
		COLOR = sky_color.rgb;
	}
}
""", }
	};
	public WorldEnvironment WorldEnvironment;
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
		_skyMaterial.SetShaderParameter("ground_color", new Color(0f, 1f, 0f, 1f));
		_skyMaterial.SetShaderParameter("sky_color", new Color(0f, 0f, 1f, 1f));
		_camera = new Camera3D
		{
			Position = new Vector3(0, 1.6f, 0),
			RotationDegrees = new Vector3(0, 0, 0),
		};
		AddChild(_camera);
	}
	public override void _Process(double delta)
	{
	}
}
