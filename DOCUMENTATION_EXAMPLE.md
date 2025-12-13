# Documentation Example

This file demonstrates how to apply the data type philosophy and source code documentation to existing code.

## Important: Project Design Decisions

### Tile Coordinate Extension

**All tile coordinates in this project use `ushort` (16-bit unsigned) instead of the original Wolf3D's `byte` (8-bit).**

- **Original Wolf3D**: Limited to 64×64 maps (MAPSIZE=64) using `byte tilex, tiley`
- **This Project**: Supports up to 65535×65535 maps using `ushort` for modding flexibility
- This is an **intentional extension** documented in DATA_TYPES.md

When documenting tile coordinates, always note this extension:
```csharp
// WL_DEF.H:doorstruct:tilex (original: byte)
// Intentional extension: Using ushort to support maps > 64×64
public ushort TileX { get; set; }
```

### Coordinate System Architecture

**Assets layer vs VR layer use different coordinate naming**

**Assets Layer (BenMcLean.Wolf3D.Assets)**:
- Uses **X, Y** matching original Wolf3D coordinate system
- **Rationale**: Faithful representation of Wolf3D data structures
- Examples: `TileX, TileY`, `X, Y` for fixed-point coordinates

**VR/Godot Layer (BenMcLean.Wolf3D.VR)**:
- Uses **X, Y, Z** with Godot's Y-up convention
- **Translation**: Wolf3D X → Godot X; Wolf3D Y → Godot Z; Godot Y = vertical
- Examples: `TileX, TileZ`, `CurrentX, CurrentZ`

### Semantic Improvements (ALL Layers)

**`FacesEastWest` replaces Wolf3D's `vertical` everywhere**
- **Original Wolf3D**: Used `vertical` flag (misleading even in 1991!)
- **This Project**: Uses `FacesEastWest` in both Assets and VR layers
- **Rationale**: "Vertical" didn't mean up-down even in original Wolf3D - it meant north-south door orientation. Likely a Commander Keen engine holdover. `FacesEastWest` is semantically correct.

**Documentation patterns**:
```csharp
// Assets layer - faithful to Wolf3D coordinates, improved semantics
public ushort TileX { get; set; }       // WL_DEF.H:doorstruct:tilex (original: byte)
public ushort TileY { get; set; }       // WL_DEF.H:doorstruct:tiley (original: byte)
public bool FacesEastWest { get; set; } // WL_DEF.H:doorstruct:vertical (renamed)

// VR layer - translated to Godot coordinates, same semantics
public ushort TileX { get; set; }       // From Wolf3D tilex
public ushort TileZ { get; set; }       // From Wolf3D tiley (Godot Z axis)
public bool FacesEastWest { get; set; } // WL_DEF.H:doorstruct:vertical (renamed)
```

## Example 1a: Door Structure (Assets Layer)

### Before (undocumented)
```csharp
public class Door
{
    public ushort TileX { get; set; }
    public ushort TileY { get; set; }
    public bool FacesEastWest { get; set; }
    public byte Lock { get; set; }
    public short TicCount { get; set; }
}
```

### After (documented with Wolf3D sources)
```csharp
/// <summary>
/// Represents a door actor in the game world.
/// Based on doorstruct from WL_DEF.H:964-975
/// Assets layer - faithful to Wolf3D coordinate system
/// </summary>
public class Door
{
    // WL_DEF.H:doorstruct:tilex (original: byte)
    // Intentional extension: Using ushort to support maps > 64×64
    public ushort TileX { get; set; }

    // WL_DEF.H:doorstruct:tiley (original: byte)
    // Intentional extension: Using ushort to support maps > 64×64
    public ushort TileY { get; set; }

    // WL_DEF.H:doorstruct:vertical
    // Renamed to FacesEastWest for semantic clarity
    // Original "vertical" meant north-south orientation (poor naming even in original)
    public bool FacesEastWest { get; set; }

    // WL_DEF.H:doorstruct:lock
    public byte Lock { get; set; }

    // WL_DEF.H:doorstruct:ticcount
    public short TicCount { get; set; }
}
```

## Example 1b: Door Structure (VR Layer)

### Before (undocumented)
```csharp
public class Door
{
    public ushort TileX { get; set; }
    public ushort TileZ { get; set; }
    public bool FacesEastWest { get; set; }
    public byte Lock { get; set; }
    public short TicCount { get; set; }
    public uint CurrentX { get; set; }
    public uint CurrentZ { get; set; }
}
```

### After (documented with Wolf3D sources)
```csharp
/// <summary>
/// Represents a door actor for VR rendering.
/// Based on doorstruct from WL_DEF.H:964-975
/// VR layer - translated to Godot Y-up coordinate system
/// </summary>
public class Door
{
    // From Wolf3D tilex
    public ushort TileX { get; set; }

    // From Wolf3D tiley (translated to Godot Z axis)
    public ushort TileZ { get; set; }

    // WL_DEF.H:doorstruct:vertical
    // Renamed to FacesEastWest for semantic clarity
    // Original "vertical" meant north-south orientation (poor naming even in original)
    public bool FacesEastWest { get; set; }

    // WL_DEF.H:doorstruct:lock
    public byte Lock { get; set; }

    // WL_DEF.H:doorstruct:ticcount
    public short TicCount { get; set; }

    // Intentional extension: Using uint for 16.16 fixed-point door positions
    // Original Wolf3D doorstruct didn't store sub-tile positions
    // We use 16.16 fixed-point (uint) for smooth VR door sliding
    public uint CurrentX { get; set; }

    // Intentional extension: From Wolf3D Y → Godot Z
    public uint CurrentZ { get; set; }
}
```

## Example 2: Static Object Structure (Assets Layer)

### Before (undocumented)
```csharp
public class StaticObject
{
    public ushort TileX { get; set; }
    public ushort TileY { get; set; }
    public short ShapeNum { get; set; }
    public byte Flags { get; set; }
    public byte ItemNumber { get; set; }
}
```

### After (documented with Wolf3D sources)
```csharp
/// <summary>
/// Represents a static (non-thinking) actor like lamps, treasure, etc.
/// Based on statstruct from WL_DEF.H:948-955
/// Assets layer - faithful to Wolf3D coordinate system
/// </summary>
public class StaticObject
{
    // WL_DEF.H:statstruct:tilex (original: byte)
    // Intentional extension: Using ushort to support maps > 64×64
    public ushort TileX { get; set; }

    // WL_DEF.H:statstruct:tiley (original: byte)
    // Intentional extension: Using ushort to support maps > 64×64
    public ushort TileY { get; set; }

    // WL_DEF.H:statstruct:shapenum
    // NOTE: Can be -1 to indicate object has been removed
    public short ShapeNum { get; set; }

    // WL_DEF.H:statstruct:flags
    public byte Flags { get; set; }

    // WL_DEF.H:statstruct:itemnumber
    public byte ItemNumber { get; set; }
}
```

## Example 3: Actor/Object Structure (Complex)

### Before (undocumented)
```csharp
public class Actor
{
    public int X { get; set; }
    public int Y { get; set; }
    public ushort TileX { get; set; }
    public ushort TileY { get; set; }
    public byte AreaNumber { get; set; }
    public short Angle { get; set; }
    public short HitPoints { get; set; }
    public int Speed { get; set; }
    public byte Flags { get; set; }
}
```

### After (documented with Wolf3D sources)
```csharp
/// <summary>
/// Represents a thinking actor (enemy, player, etc.)
/// Based on objstruct from WL_DEF.H:984-1019
/// </summary>
public class Actor
{
    // WL_DEF.H:objstruct:x
    // "fixed" typedef (long in DOS C) = 16.16 fixed-point coordinate
    public int X { get; set; }

    // WL_DEF.H:objstruct:y
    // "fixed" typedef (long in DOS C) = 16.16 fixed-point coordinate
    public int Y { get; set; }

    // WL_DEF.H:objstruct:tilex
    // NOTE: objstruct uses "unsigned" (16-bit), not "byte" like statstruct
    public ushort TileX { get; set; }

    // WL_DEF.H:objstruct:tiley
    // NOTE: objstruct uses "unsigned" (16-bit), not "byte" like statstruct
    public ushort TileY { get; set; }

    // WL_DEF.H:objstruct:areanumber
    public byte AreaNumber { get; set; }

    // WL_DEF.H:objstruct:angle
    // Range: 0-359 (ANGLES constant) or 0-3599 (FINEANGLES)
    public short Angle { get; set; }

    // WL_DEF.H:objstruct:hitpoints
    public short HitPoints { get; set; }

    // WL_DEF.H:objstruct:speed
    // DOS long (32-bit signed) - often represents velocity in fixed-point
    public int Speed { get; set; }

    // WL_DEF.H:objstruct:flags
    // FL_SHOOTABLE, FL_BONUS, FL_VISABLE, etc. (WL_DEF.H:169-177)
    public byte Flags { get; set; }
}
```

## Example 4: Map Structure

### Before (undocumented)
```csharp
public class GameMap
{
    public ushort Width { get; set; }
    public ushort Depth { get; set; }
    public ushort[] MapData { get; set; }
    public ushort[] ObjectData { get; set; }
    public ushort[] OtherData { get; set; }

    public ushort X(int i) => (ushort)(i % Width);
    public ushort Z(int i) => (ushort)(i / Depth);
}
```

### After (documented with Wolf3D sources)
```csharp
/// <summary>
/// Represents a game map/level.
/// Based on maptype from ID_CA.H:17-23
/// </summary>
public class GameMap
{
    // ID_CA.H:maptype:width
    // DOS "unsigned" (16-bit)
    public ushort Width { get; set; }

    // ID_CA.H:maptype:height (but we call it Depth for 3D clarity)
    // DOS "unsigned" (16-bit)
    public ushort Depth { get; set; }

    // Walls layer - tile numbers for walls
    // Decompressed from planestart[0], planelength[0]
    // See WL_MAIN.C:SetupWalls for how wall numbers map to VSWAP pages
    public ushort[] MapData { get; set; }

    // Objects layer - object types, enemy spawns, items
    // Decompressed from planestart[1], planelength[1]
    public ushort[] ObjectData { get; set; }

    // Other/Info layer - floor codes, area numbers, etc.
    // Decompressed from planestart[2], planelength[2]
    public ushort[] OtherData { get; set; }

    /// <summary>
    /// Extract X tile coordinate from linear array index.
    /// Uses C# int for array indexing, returns Wolf3D ushort tile coordinate.
    /// </summary>
    public ushort X(int i) => (ushort)(i % Width);

    /// <summary>
    /// Extract Z tile coordinate from linear array index.
    /// Uses C# int for array indexing, returns Wolf3D ushort tile coordinate.
    /// </summary>
    public ushort Z(int i) => (ushort)(i / Depth);
}
```

## Example 5: VSWAP Page Access

### Before (undocumented)
```csharp
public class VSwap
{
    public ushort NumPages { get; set; }
    public ushort SpritePage { get; set; }

    public byte[] GetPage(ushort pageNumber)
    {
        // ...
    }

    public byte GetR(ushort page, ushort x, ushort y)
    {
        // ...
    }
}
```

### After (documented with Wolf3D sources)
```csharp
/// <summary>
/// VSWAP file reader for sprite/wall graphics.
/// Format documentation: http://www.shikadi.net/moddingwiki/VSWAP_Format
/// </summary>
public class VSwap
{
    // VSWAP header field
    // DOS "unsigned" (16-bit) - total number of pages in VSWAP
    public ushort NumPages { get; set; }

    // VSWAP header field
    // DOS "unsigned" (16-bit) - first sprite page number
    // Pages before this are walls, pages after are sprites
    public ushort SpritePage { get; set; }

    /// <summary>
    /// Get a page by number.
    /// Wolf3D page numbers are ushort (DOS unsigned, 16-bit).
    /// Each page is 64x64 pixels = 4096 bytes.
    /// </summary>
    /// <param name="pageNumber">WL_DEF.H concept - page/chunk index</param>
    public byte[] GetPage(ushort pageNumber)
    {
        // C# iteration uses int
        for (int i = 0; i < 4096; i++)
        {
            // ...
        }
    }

    /// <summary>
    /// Get red component of pixel at (x,y) on given page.
    /// </summary>
    /// <param name="page">Page number (ushort, Wolf3D type)</param>
    /// <param name="x">X coordinate 0-63 (ushort for Wolf3D consistency)</param>
    /// <param name="y">Y coordinate 0-63 (ushort for Wolf3D consistency)</param>
    public byte GetR(ushort page, ushort x, ushort y)
    {
        // ...
    }
}
```

## Example 6: Iteration Pattern

### Correct C# iteration with Wolf3D type conversions
```csharp
public void ProcessMap(GameMap map)
{
    // Use C# int for iteration (native C# type)
    for (int i = 0; i < map.Width * map.Depth; i++)
    {
        // Convert to Wolf3D types when extracting tile coordinates
        ushort x = (ushort)(i % map.Width);   // WL_DEF.H concept: tilex
        ushort z = (ushort)(i / map.Width);   // WL_DEF.H concept: tiley

        // Access map data with C# index
        ushort wallTile = map.MapData[i];

        // Do something with Wolf3D-typed values
        ProcessTile(x, z, wallTile);
    }
}

private void ProcessTile(ushort x, ushort z, ushort tile)
{
    // Parameters use Wolf3D types
    // ...
}
```

## Example 7: Fixed-Point Math

### Before (unclear)
```csharp
public class Actor
{
    public int X { get; set; }
    public int Y { get; set; }
}

public void MoveActor(Actor actor, int tileX, int tileY)
{
    actor.X = tileX << 16;
    actor.Y = tileY << 16;
}
```

### After (documented)
```csharp
public class Actor
{
    // WL_DEF.H:objstruct:x
    // "fixed" typedef (DOS long, 32-bit signed)
    // 16.16 fixed-point: upper 16 bits = tile, lower 16 bits = sub-tile
    // Example: 0x00010000 = tile 1, 0x00018000 = tile 1 + 0.5
    public int X { get; set; }

    // WL_DEF.H:objstruct:y
    // "fixed" typedef (DOS long, 32-bit signed)
    // 16.16 fixed-point format
    public int Y { get; set; }
}

/// <summary>
/// Position actor at tile coordinates.
/// WL_DEF.H:#define TILEGLOBAL (1L<<16) - converts tile to fixed-point
/// </summary>
public void MoveActor(Actor actor, ushort tileX, ushort tileZ)
{
    // Convert tile coordinates to 16.16 fixed-point
    // This matches WL_MAIN.C pattern: position = tile << TILESHIFT
    actor.X = tileX << 16;  // TILESHIFT = 16 (WL_DEF.H:121)
    actor.Y = tileZ << 16;
}

/// <summary>
/// Convert fixed-point coordinate to tile coordinate.
/// </summary>
public ushort GetTileX(Actor actor)
{
    // WL_DEF.H:TILESHIFT - extract upper 16 bits
    return (ushort)(actor.X >> 16);
}

/// <summary>
/// Convert fixed-point to float for rendering.
/// </summary>
public float GetFloatX(Actor actor)
{
    // Divide by 2^16 to get fractional tile position
    return actor.X / 65536.0f;
}
```

## Summary

The key principles demonstrated:

1. **Reference Wolf3D sources** in comments: `WL_DEF.H:objstruct:x`

2. **Use correct C# ↔ Wolf3D type mapping** from DATA_TYPES.md

3. **Document fixed-point format** explicitly when using 16.16

4. **Use int for C# loops**, convert to Wolf3D types when storing

5. **Explain intentional deviations** from original Wolf3D

6. **Add context** about ranges, special values (-1 = removed), and flags

7. **Reference algorithms** from Wolf3D source (e.g., SetupWalls)

This makes the codebase:
- **Maintainable**: Clear relationship to original Wolf3D
- **Understandable**: Type choices are justified and documented
- **Correct**: Preserves original semantics (signed/unsigned, bit width)
- **Traceable**: Can verify against original Wolf3D source code
