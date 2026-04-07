using System;
using System.IO;
using System.Xml.Linq;
using BenMcLean.Wolf3D.Assets.Graphics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

if (args.Length < 2)
{
	Console.WriteLine("Usage: TileDebugger <path-to-xml> <path-to-game-folder>");
	Console.WriteLine("Example: TileDebugger games/N3D.xml C:/Games/N3D");
	return 1;
}

string xmlPath = args[0];
string gameFolder = args[1];

if (!File.Exists(xmlPath))
{
	Console.Error.WriteLine($"ERROR: XML file not found: {xmlPath}");
	return 1;
}
if (!Directory.Exists(gameFolder))
{
	Console.Error.WriteLine($"ERROR: Game folder not found: {gameFolder}");
	return 1;
}

XElement xml = XElement.Load(xmlPath);
Console.WriteLine($"Loading VgaGraph from {xmlPath} with folder {gameFolder}...");
VgaGraph vgaGraph = VgaGraph.Load(xml, gameFolder);
if (vgaGraph == null)
{
	Console.Error.WriteLine("ERROR: VgaGraph element missing or incomplete in XML.");
	return 1;
}

byte[][] tiles = vgaGraph.Tiles;
Console.WriteLine($"Loaded {tiles.Length} tiles.");
if (tiles.Length == 0)
{
	Console.Error.WriteLine("ERROR: No tiles found. Check that <Tiles Start=\"...\" Count=\"...\"/> is in the XML.");
	return 1;
}

// Arrange tiles in a grid: up to 16 per row
const int tileSize = 8;
const int tilesPerRow = 16;
int rows = (tiles.Length + tilesPerRow - 1) / tilesPerRow;
int imgWidth = tilesPerRow * tileSize;
int imgHeight = rows * tileSize;

using Image<Rgba32> img = new(imgWidth, imgHeight, new Rgba32(0, 0, 0, 255));
for (int t = 0; t < tiles.Length; t++)
{
	byte[] rgba = tiles[t];
	if (rgba == null || rgba.Length < tileSize * tileSize * 4)
	{
		Console.Error.WriteLine($"Warning: tile {t} has unexpected data length {rgba?.Length ?? 0}, skipping.");
		continue;
	}
	int tileCol = t % tilesPerRow;
	int tileRow = t / tilesPerRow;
	int destX = tileCol * tileSize;
	int destY = tileRow * tileSize;
	for (int py = 0; py < tileSize; py++)
		for (int px = 0; px < tileSize; px++)
		{
			int srcOffset = (py * tileSize + px) * 4;
			img[destX + px, destY + py] = new Rgba32(
				r: rgba[srcOffset],
				g: rgba[srcOffset + 1],
				b: rgba[srcOffset + 2],
				a: rgba[srcOffset + 3]);
		}
}

string outPath = Path.Combine(Directory.GetCurrentDirectory(), "tiles_debug.png");
img.SaveAsPng(outPath);
Console.WriteLine($"Saved tile sheet to: {outPath}");
return 0;
