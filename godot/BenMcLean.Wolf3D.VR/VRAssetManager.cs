using BenMcLean.Wolf3D.Assets.Graphics;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BenMcLean.Wolf3D.VR;

/// <summary>
/// Manages VR-specific 3D rendering assets.
/// Converts VSwap wall and sprite textures into Godot 3D materials optimized for VR.
/// Loads once at game start and persists until program termination.
/// </summary>
public static class VRAssetManager
{
	/// <summary>
	/// Scale factor for upscaling textures. Default is 8x (64x64 -> 512x512).
	/// 4x gives 256x256, 8x gives 512x512, 16x gives 1024x1024.
	/// </summary>
	public static byte ScaleFactor { get; private set; } = 8;

	/// <summary>
	/// Opaque materials (used for walls and normal door quads), indexed by VSwap page number.
	/// </summary>
	public static IReadOnlyDictionary<ushort, StandardMaterial3D> OpaqueMaterials { get; private set; }

	/// <summary>
	/// Sprite materials for billboarded objects, indexed by VSwap page number.
	/// </summary>
	public static IReadOnlyDictionary<ushort, StandardMaterial3D> SpriteMaterials { get; private set; }

	/// <summary>
	/// Cache of flipped opaque materials with horizontally mirrored UVs.
	/// Only contains materials for door textures. Used with negative scale for correct handle orientation.
	/// </summary>
	private static Dictionary<ushort, ShaderMaterial> _flippedOpaqueMaterials;

	/// <summary>
	/// Shared shader for flipped door materials with horizontal UV flipping.
	/// </summary>
	private static Shader _flippedDoorShader;

	/// <summary>
	/// Custom shader code for flipped door quads with horizontally flipped UVs.
	/// Uses back-face culling (same as normal quads) - the negative scale makes it face the opposite direction.
	/// UV flip compensates for the geometry flip to keep door handles on the correct side.
	/// </summary>
	private const string FlippedDoorShaderCode = @"
shader_type spatial;
render_mode unshaded, cull_back;

uniform sampler2D albedo_texture : source_color, filter_nearest, repeat_disable;

void fragment() {
	vec2 uv = vec2(1.0 - UV.x, UV.y);  // Flip UVs horizontally to compensate for geometry flip
	vec4 tex = texture(albedo_texture, uv);
	ALBEDO = tex.rgb;
	// Don't set ALPHA - doors are opaque and shouldn't use alpha blending
}
";

	/// <summary>
	/// Reference to the VSwap being used (from SharedAssetManager).
	/// </summary>
	private static VSwap _vswap;

	/// <summary>
	/// Initializes VR assets from the currently loaded game.
	/// Eagerly converts all wall and sprite textures to 3D materials.
	/// Should be called once after SharedAssetManager.LoadGame().
	/// </summary>
	/// <param name="scaleFactor">Upscaling factor (default 8x for 512x512 textures)</param>
	public static void Initialize(byte scaleFactor = 8)
	{
		// Get VSwap from SharedAssetManager
		_vswap = Shared.SharedAssetManager.CurrentGame?.VSwap
			?? throw new InvalidOperationException("SharedAssetManager.CurrentGame.VSwap is null. Load a game first.");

		ScaleFactor = scaleFactor;

		// Initialize flipped material shader
		_flippedDoorShader = new Shader { Code = FlippedDoorShaderCode };
		_flippedOpaqueMaterials = new Dictionary<ushort, ShaderMaterial>();

		// Eagerly convert all opaque materials (walls and doors) using parallelization
		// Only process pages that actually exist in the VSwap (skip null entries)
		Dictionary<ushort, StandardMaterial3D> opaqueMaterials = Enumerable.Range(0, _vswap.SpritePage)
			.Where(pageNumber => _vswap.Pages[pageNumber] != null)
			.Parallelize(pageNumber => new KeyValuePair<ushort, StandardMaterial3D>((ushort)pageNumber, CreateOpaqueMaterial((ushort)pageNumber)))
			.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
		OpaqueMaterials = opaqueMaterials;

		// Eagerly convert all sprite materials using parallelization
		// Only process sprite pages that actually exist in the VSwap (skip null entries)
		int spriteCount = _vswap.Pages.Length - _vswap.SpritePage;
		Dictionary<ushort, StandardMaterial3D> spriteMaterials = Enumerable.Range(_vswap.SpritePage, spriteCount)
			.Where(pageNumber => _vswap.Pages[pageNumber] != null)
			.Parallelize(pageNumber => new KeyValuePair<ushort, StandardMaterial3D>((ushort)pageNumber, CreateSpriteMaterial((ushort)pageNumber)))
			.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
		SpriteMaterials = spriteMaterials;
	}

	/// <summary>
	/// Eagerly creates flipped materials for the specified door texture indices.
	/// Returns a dictionary of the created materials indexed by texture page number.
	/// </summary>
	public static Dictionary<ushort, ShaderMaterial> CreateFlippedMaterialsForDoors(IEnumerable<ushort> doorTextureIndices)
	{
		Dictionary<ushort, ShaderMaterial> materials = [];

		foreach (ushort pageNumber in doorTextureIndices.Distinct())
		{
			ShaderMaterial material = CreateFlippedMaterial(pageNumber);
			materials[pageNumber] = material;
			_flippedOpaqueMaterials[pageNumber] = material; // Cache for future reference
		}

		return materials;
	}

	/// <summary>
	/// Creates a StandardMaterial3D for an opaque texture (walls and doors) with VR-optimized settings.
	/// Applies upscaling, generates mipmaps, and configures nearest-neighbor filtering for sharp pixels.
	/// </summary>
	private static StandardMaterial3D CreateOpaqueMaterial(ushort pageNumber)
	{
		// Get the original texture data from VSwap (will throw if page doesn't exist)
		byte[] originalData = _vswap.Pages[pageNumber];

		// Upscale the texture data
		byte[] scaledData = originalData.Upscale(ScaleFactor, ScaleFactor, _vswap.TileSqrt);
		int scaledSize = _vswap.TileSqrt * ScaleFactor;

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
	/// Creates a ShaderMaterial with horizontally flipped/mirrored UVs.
	/// Used for flipped door quads combined with negative scale geometry.
	/// UV flip compensates for the geometry flip to ensure door handles appear on the correct side.
	/// </summary>
	private static ShaderMaterial CreateFlippedMaterial(ushort pageNumber)
	{
		// Get the original texture data from VSwap (will throw if page doesn't exist)
		byte[] originalData = _vswap.Pages[pageNumber];

		// Upscale the texture data
		byte[] scaledData = originalData.Upscale(ScaleFactor, ScaleFactor, _vswap.TileSqrt);
		int scaledSize = _vswap.TileSqrt * ScaleFactor;

		// Create Godot Image
		Image image = Image.CreateFromData(
			width: scaledSize,
			height: scaledSize,
			useMipmaps: false,
			format: Image.Format.Rgba8,
			data: scaledData
		);

		if (image == null)
			return null;

		// Generate mipmaps
		image.GenerateMipmaps();

		// Create ImageTexture from the image
		ImageTexture texture = ImageTexture.CreateFromImage(image);

		if (texture == null)
			return null;

		// Create shader material with back-face culling and UV flipping
		ShaderMaterial material = new ShaderMaterial
		{
			Shader = _flippedDoorShader
		};

		// Set the texture uniform
		material.SetShaderParameter("albedo_texture", texture);

		return material;
	}

	/// <summary>
	/// Creates a StandardMaterial3D for a sprite texture with transparency support.
	/// Applies upscaling, generates mipmaps, and enables alpha blending for billboarded objects.
	/// </summary>
	private static StandardMaterial3D CreateSpriteMaterial(ushort pageNumber)
	{
		// Get the original texture data from VSwap (will throw if page doesn't exist)
		byte[] originalData = _vswap.Pages[pageNumber];

		// Upscale the texture data
		byte[] scaledData = originalData.Upscale(ScaleFactor, ScaleFactor, _vswap.TileSqrt);
		int scaledSize = _vswap.TileSqrt * ScaleFactor;

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

	/// <summary>
	/// Disposes all loaded VR assets.
	/// Currently not needed as VR assets persist until program termination,
	/// but included for completeness.
	/// </summary>
	public static void Cleanup()
	{
		// Dispose opaque materials
		if (OpaqueMaterials != null)
		{
			foreach (StandardMaterial3D material in OpaqueMaterials.Values)
				material?.Dispose();
		}

		// Dispose sprite materials
		if (SpriteMaterials != null)
		{
			foreach (StandardMaterial3D material in SpriteMaterials.Values)
				material?.Dispose();
		}

		// Dispose flipped materials
		if (_flippedOpaqueMaterials != null)
		{
			foreach (ShaderMaterial material in _flippedOpaqueMaterials.Values)
				material?.Dispose();
			_flippedOpaqueMaterials?.Clear();
		}

		_flippedDoorShader?.Dispose();
		_vswap = null;
	}
}
