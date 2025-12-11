using Godot;
using System;
using System.Linq;

namespace BenMcLean.Wolf3D.VR;

/// <summary>
/// Manages conversion of Wolfenstein 3D assets to Godot resources optimized for VR.
/// Eagerly converts and caches wall and door materials with configurable upscaling.
/// </summary>
public class GodotResources
{
	/// <summary>
	/// Scale factor for upscaling textures. Default is 8x (64x64 -> 512x512).
	/// 4x gives 256x256, 8x gives 512x512, 16x gives 1024x1024.
	/// </summary>
	public byte ScaleFactor { get; init; } = 8;

	/// <summary>
	/// Cached wall materials, indexed by VSwap page number.
	/// </summary>
	private readonly StandardMaterial3D[] _wallMaterials;

	/// <summary>
	/// Gets the array of all wall materials for batch operations.
	/// </summary>
	public StandardMaterial3D[] WallMaterials => _wallMaterials;

	/// <summary>
	/// Cached door materials, indexed by VSwap page number.
	/// </summary>
	private readonly ShaderMaterial[] _doorMaterials;

	/// <summary>
	/// Shared shader for all door materials with UV flipping for correct latch positioning.
	/// </summary>
	private readonly Shader _doorShader;

	/// <summary>
	/// Cached sprite materials for billboarded objects, indexed by VSwap page number.
	/// </summary>
	private readonly StandardMaterial3D[] _spriteMaterials;

	/// <summary>
	/// Gets the array of all sprite materials for batch operations.
	/// </summary>
	public StandardMaterial3D[] SpriteMaterials => _spriteMaterials;

	/// <summary>
	/// Reference to the source VSwap data.
	/// </summary>
	public Assets.VSwap VSwap { get; init; }

	/// <summary>
	/// Custom shader code for doors that flips UVs on the back face.
	/// This ensures the door latch appears on the same side from both viewing directions.
	/// </summary>
	private const string DoorShaderCode = @"
shader_type spatial;
render_mode unshaded, cull_disabled;

uniform sampler2D albedo_texture : source_color, filter_nearest, repeat_disable;

void fragment() {
	vec2 uv = FRONT_FACING ? UV : vec2(1.0 - UV.x, UV.y);
	vec4 tex = texture(albedo_texture, uv);
	ALBEDO = tex.rgb;
	ALPHA = tex.a;
}
";

	/// <summary>
	/// Creates a new GodotResources instance and eagerly converts all wall and door materials.
	/// Uses parallel processing for faster conversion.
	/// </summary>
	/// <param name="vswap">The VSwap data source</param>
	/// <param name="scaleFactor">Upscaling factor (default 8x for 512x512 textures)</param>
	public GodotResources(Assets.VSwap vswap, byte scaleFactor = 8)
	{
		VSwap = vswap ?? throw new ArgumentNullException(nameof(vswap));
		ScaleFactor = scaleFactor;

		// Initialize door shader
		_doorShader = new Shader { Code = DoorShaderCode };

		// Eagerly convert all wall materials using parallelization
		_wallMaterials = [.. Enumerable.Range(0, VSwap.SpritePage)
			.Parallelize(pageNumber => CreateWallMaterial((ushort)pageNumber))];

		// Eagerly convert all door materials using parallelization
		_doorMaterials = [.. Enumerable.Range(0, VSwap.SpritePage)
			.Parallelize(pageNumber => CreateDoorMaterial((ushort)pageNumber))];

		// Eagerly convert all sprite materials using parallelization
		int spriteCount = VSwap.Pages.Length - VSwap.SpritePage;
		_spriteMaterials = [.. Enumerable.Range(VSwap.SpritePage, spriteCount)
			.Parallelize(pageNumber => CreateSpriteMaterial((ushort)pageNumber))];

		GD.Print($"GodotResources: Converted {_wallMaterials.Length} wall materials, {_doorMaterials.Length} door materials, and {_spriteMaterials.Length} sprite materials at {ScaleFactor}x scale ({VSwap.TileSqrt * ScaleFactor}x{VSwap.TileSqrt * ScaleFactor})");
	}

	/// <summary>
	/// Gets a wall material by VSwap page number.
	/// Uses backface culling for standard wall rendering.
	/// </summary>
	/// <param name="pageNumber">VSwap page number (must be less than SpritePage)</param>
	/// <returns>Cached StandardMaterial3D ready for use in VR</returns>
	public StandardMaterial3D GetWallMaterial(ushort pageNumber)
	{
		if (pageNumber >= _wallMaterials.Length)
			throw new ArgumentOutOfRangeException(nameof(pageNumber),
				$"Page {pageNumber} is not a wall material (SpritePage = {VSwap.SpritePage})");

		return _wallMaterials[pageNumber];
	}

	/// <summary>
	/// Gets a door material by VSwap page number.
	/// Uses custom shader with double-sided rendering and UV flipping to ensure
	/// the door latch appears on the same side from both viewing directions.
	/// </summary>
	/// <param name="pageNumber">VSwap page number (must be less than SpritePage)</param>
	/// <returns>Cached ShaderMaterial with double-sided rendering</returns>
	public ShaderMaterial GetDoorMaterial(ushort pageNumber)
	{
		if (pageNumber >= _doorMaterials.Length)
			throw new ArgumentOutOfRangeException(nameof(pageNumber),
				$"Page {pageNumber} is not a door material (SpritePage = {VSwap.SpritePage})");

		return _doorMaterials[pageNumber];
	}

	/// <summary>
	/// Gets a sprite material by VSwap page number.
	/// Uses transparency and double-sided rendering for billboarded objects.
	/// </summary>
	/// <param name="pageNumber">VSwap page number (must be >= SpritePage)</param>
	/// <returns>Cached StandardMaterial3D with transparency support</returns>
	public StandardMaterial3D GetSpriteMaterial(ushort pageNumber)
	{
		if (pageNumber < VSwap.SpritePage)
			throw new ArgumentOutOfRangeException(nameof(pageNumber),
				$"Page {pageNumber} is not a sprite (SpritePage = {VSwap.SpritePage})");

		int index = pageNumber - VSwap.SpritePage;
		if (index >= _spriteMaterials.Length)
			throw new ArgumentOutOfRangeException(nameof(pageNumber),
				$"Page {pageNumber} is out of range (max = {VSwap.Pages.Length - 1})");

		return _spriteMaterials[index];
	}

	/// <summary>
	/// Creates a StandardMaterial3D for a wall texture with VR-optimized settings.
	/// Applies upscaling, generates mipmaps, and configures nearest-neighbor filtering for sharp pixels.
	/// </summary>
	private StandardMaterial3D CreateWallMaterial(ushort pageNumber)
	{
		// Get the original texture data from VSwap
		byte[] originalData = VSwap.Pages[pageNumber];

		if (originalData == null)
		{
			GD.PrintErr($"Warning: VSwap page {pageNumber} is null, creating default material");
			return new StandardMaterial3D { AlbedoColor = Colors.Magenta }; // Bright magenta for missing textures
		}

		// Upscale the texture data
		byte[] scaledData = originalData.Upscale(ScaleFactor, ScaleFactor, VSwap.TileSqrt);
		int scaledSize = VSwap.TileSqrt * ScaleFactor;

		// Debug: Check data validity
		int expectedSize = scaledSize * scaledSize * 4; // RGBA = 4 bytes per pixel
		if (scaledData.Length != expectedSize)
		{
			GD.PrintErr($"ERROR: Page {pageNumber} has wrong data size: {scaledData.Length} bytes, expected {expectedSize}");
			return new StandardMaterial3D { AlbedoColor = Colors.Red };
		}

		// Create Godot Image with mipmaps disabled initially
		Image image = Image.CreateFromData(
			width: scaledSize,
			height: scaledSize,
			useMipmaps: false, // Don't create mipmaps during construction
			format: Image.Format.Rgba8,
			data: scaledData
		);

		// Check if image was created successfully
		if (image == null)
		{
			GD.PrintErr($"ERROR: Image.CreateFromData returned null for page {pageNumber}");
			return new StandardMaterial3D { AlbedoColor = Colors.Yellow };
		}

		// Generate mipmaps after creation
		image.GenerateMipmaps();

		// Create ImageTexture from the image
		ImageTexture texture = ImageTexture.CreateFromImage(image);

		// Debug: Verify texture was created
		if (texture == null)
		{
			GD.PrintErr($"ERROR: ImageTexture.CreateFromImage returned null for page {pageNumber}, image size: {image.GetSize()}");
			return new StandardMaterial3D { AlbedoColor = Colors.Cyan };
		}

		// Create material with VR-optimized settings
		StandardMaterial3D material = new StandardMaterial3D
		{
			AlbedoTexture = texture,
			// Nearest filtering for sharp, retro pixel aesthetic
			TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
			// Disable shading for flat, Wolfenstein 3D-style walls
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			// Ensure proper backface culling
			CullMode = BaseMaterial3D.CullModeEnum.Back,
		};

		return material;
	}

	/// <summary>
	/// Creates a ShaderMaterial for a door texture with custom UV-flipping shader.
	/// Applies upscaling and generates mipmaps.
	/// The shader ensures the door appears correctly from both sides with the latch on the same side.
	/// </summary>
	private ShaderMaterial CreateDoorMaterial(ushort pageNumber)
	{
		// Get the original texture data from VSwap
		byte[] originalData = VSwap.Pages[pageNumber];

		if (originalData == null)
			return null;

		// Upscale the texture data
		byte[] scaledData = originalData.Upscale(ScaleFactor, ScaleFactor, VSwap.TileSqrt);
		int scaledSize = VSwap.TileSqrt * ScaleFactor;

		// Create Godot Image with mipmaps enabled
		Image image = Image.CreateFromData(
			width: scaledSize,
			height: scaledSize,
			useMipmaps: true,
			format: Image.Format.Rgba8,
			data: scaledData
		);

		// Generate mipmaps for proper distance rendering and performance
		image.GenerateMipmaps();

		// Create ImageTexture from the image
		ImageTexture texture = ImageTexture.CreateFromImage(image);

		// Create shader material with the custom door shader
		ShaderMaterial material = new ShaderMaterial
		{
			Shader = _doorShader
		};

		// Set the texture uniform
		material.SetShaderParameter("albedo_texture", texture);

		return material;
	}

	/// <summary>
	/// Creates a StandardMaterial3D for a sprite texture with transparency support.
	/// Applies upscaling, generates mipmaps, and enables alpha blending for billboarded objects.
	/// </summary>
	private StandardMaterial3D CreateSpriteMaterial(ushort pageNumber)
	{
		// Get the original texture data from VSwap
		byte[] originalData = VSwap.Pages[pageNumber];

		if (originalData == null)
		{
			GD.PrintErr($"Warning: VSwap sprite page {pageNumber} is null, creating default material");
			return new StandardMaterial3D { AlbedoColor = Colors.Magenta }; // Bright magenta for missing sprites
		}

		// Upscale the texture data
		byte[] scaledData = originalData.Upscale(ScaleFactor, ScaleFactor, VSwap.TileSqrt);
		int scaledSize = VSwap.TileSqrt * ScaleFactor;

		// Create Godot Image with mipmaps disabled initially
		Image image = Image.CreateFromData(
			width: scaledSize,
			height: scaledSize,
			useMipmaps: false,
			format: Image.Format.Rgba8,
			data: scaledData
		);

		if (image == null)
		{
			GD.PrintErr($"ERROR: Image.CreateFromData returned null for sprite page {pageNumber}");
			return new StandardMaterial3D { AlbedoColor = Colors.Yellow };
		}

		// Generate mipmaps after creation
		image.GenerateMipmaps();

		// Create ImageTexture from the image
		ImageTexture texture = ImageTexture.CreateFromImage(image);

		if (texture == null)
		{
			GD.PrintErr($"ERROR: ImageTexture.CreateFromImage returned null for sprite page {pageNumber}");
			return new StandardMaterial3D { AlbedoColor = Colors.Cyan };
		}

		// Create material with transparency and VR-optimized settings
		StandardMaterial3D material = new StandardMaterial3D
		{
			AlbedoTexture = texture,
			// Use alpha scissor (cutout) for binary transparency like original Wolf3D
			// This allows proper depth testing/writing unlike alpha blending
			Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
			AlphaScissorThreshold = 0.5f,
			// Nearest filtering for sharp, retro pixel aesthetic
			TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
			// Disable shading for flat, Wolfenstein 3D-style sprites
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			// Enable backface culling - billboards rotate to face camera, only front is visible
			CullMode = BaseMaterial3D.CullModeEnum.Back,
			// Alpha scissor allows normal depth testing for proper occlusion
			NoDepthTest = false,
			DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Always,
		};

		return material;
	}
}
