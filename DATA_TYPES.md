# Wolfenstein 3D Data Type Mapping Guide

## Philosophy

This document defines the mapping between original Wolf3D C data types and C# types used in this project.

### General Principles

1. **C# Native Operations**: When iterating arrays/collections or performing C#-specific operations, use `int` (C#'s native indexing type)

2. **Wolf3D Value Semantics**: When representing values from Wolf3D (page indices, tile coordinates, etc.), use C# types matching the **bit width and signedness** of the original C types

3. **Intentional Extensions**: Where we've deliberately provided more bits than the original, document this decision

4. **Source Documentation**: Reference original Wolf3D source files and variable names in comments

### Key Intentional Extensions

#### Tile Coordinates

⚡ **All tile coordinates use `ushort` (16-bit) instead of Wolf3D's `byte` (8-bit)**

- **Rationale**: Enable maps larger than 64×64 for modding (up to 65535×65535)
- **Original Wolf3D**: `byte tilex, tiley` limited to MAPSIZE=64
- **This Project**: `ushort TileX, TileY` (Assets layer) supports arbitrary map sizes
- **Compatibility**: Original Wolf3D maps (≤64×64) fit perfectly within ushort range

#### Coordinate System Architecture

🎯 **Coordinate system separation between Assets and Presentation layers**

**Assets Layer (BenMcLean.Wolf3D.Assets)**: Uses original Wolf3D coordinate system
- **X, Y coordinates**: Matches original Wolf3D exactly (WL_DEF.H:tilex, tiley)
- **Rationale**: Faithful representation of Wolf3D data structures
- **Examples**: `TileX, TileY` for tile coordinates; `X, Y` for fixed-point coordinates

**VR/Godot Layer (BenMcLean.Wolf3D.VR)**: Translates to Godot's Y-up coordinate system
- **X, Y, Z coordinates**: Godot's 3D system (Y is vertical/up-down)
- **Translation**: Wolf3D X → Godot X; Wolf3D Y → Godot Z; Godot Y = height
- **Rationale**: Match Godot's coordinate conventions for rendering
- **Examples**: `TileX, TileZ` in VR layer; `CurrentX, CurrentZ` for door positions

#### Semantic Improvements

🎯 **Universal semantic improvements**: Some Wolf3D names are improved across ALL layers

**Door Orientation: `vertical` → `FacesEastWest`** (ALL layers)
- **Original Wolf3D**: `bool vertical` (WL_DEF.H:doorstruct:vertical)
- **Original meaning**: Doors that face north-south on the 2D map (vs east-west)
- **This Project**: `bool FacesEastWest` everywhere
- **Rationale**: "Vertical" was misleading even in 1991 - it didn't mean "up-down" even then! Likely a Commander Keen engine holdover. `FacesEastWest` is semantically correct.

**Documentation pattern**:
```csharp
// WL_DEF.H:doorstruct:vertical
// Renamed to FacesEastWest for semantic clarity
// Original "vertical" meant north-south orientation (poor naming even in original)
public bool FacesEastWest { get; set; }
```

**Coordinate Naming** (layer-specific)
- **Assets layer**: Use X, Y (matches Wolf3D coordinate system)
- **VR/Godot layer**: Use X, Z (Godot's Y-up system: Wolf3D Y → Godot Z)

**Documentation patterns**:
```csharp
// Assets layer - faithful to Wolf3D coordinates
public ushort TileX { get; set; }       // WL_DEF.H:doorstruct:tilex (original: byte)
public ushort TileY { get; set; }       // WL_DEF.H:doorstruct:tiley (original: byte)
public bool FacesEastWest { get; set; } // WL_DEF.H:doorstruct:vertical

// VR layer - translated to Godot coordinates
public ushort TileX { get; set; }       // From Wolf3D tilex
public ushort TileZ { get; set; }       // From Wolf3D tiley (Godot Z axis)
public bool FacesEastWest { get; set; } // WL_DEF.H:doorstruct:vertical
```

## DOS C Type System (16-bit)

The original Wolf3D was compiled for DOS using 16-bit integers:

| Original C Type | Bits | Signed | C# Equivalent | Notes |
|-----------------|------|--------|---------------|-------|
| `char` | 8 | Yes | `sbyte` | Rarely used |
| `unsigned char` | 8 | No | `byte` | Common for tile coords, flags |
| `byte` (typedef) | 8 | No | `byte` | Wolf3D's typedef for unsigned char |
| `int` | 16 | Yes | `short` | Used for shape numbers, angles |
| `unsigned` | 16 | No | `ushort` | Used for dimensions, counts |
| `unsigned int` | 16 | No | `ushort` | Same as unsigned |
| `word` (typedef) | 16 | No | `ushort` | Wolf3D's typedef |
| `long` | 32 | Yes | `int` | Used for fixed-point math |
| `unsigned long` | 32 | No | `uint` | Used for file offsets |
| `longword` (typedef) | 32 | No | `uint` | Wolf3D's typedef |
| `fixed` (typedef) | 32 | Yes | `int` | **16.16 fixed-point**, critical! |

## Common Wolf3D Data Structures

### Tile Coordinates

**Original Wolf3D**: `byte tilex, tiley` (WL_DEF.H:950, 966) in statstruct/doorstruct
**Also**: `unsigned tilex, tiley` (WL_DEF.H:997) in objstruct

**C# Mapping - INTENTIONAL EXTENSION**:
- **We use `ushort` for ALL tile coordinates** to support maps larger than 64×64
- Original Wolf3D: Limited to 64×64 tiles (MAPSIZE=64) using `byte` coordinates
- This project: Supports up to 65535×65535 tiles for modding flexibility
- For C# loop variables: Still use `int`

**Design Rationale**: Enabling larger custom maps for modders while maintaining compatibility with original Wolf3D data (which will fit within ushort range).

**Example (Assets layer)**:
```csharp
// Intentional extension: Using ushort instead of original byte
// Original: byte tilex, tiley; (from WL_DEF.H:950 statstruct)
// We use ushort to support maps > 64×64 for modding
public ushort TileX { get; set; }  // WL_DEF.H:tilex (original: byte)
public ushort TileY { get; set; }  // WL_DEF.H:tiley (original: byte)

// C# iteration (Assets layer uses Width, Height matching Wolf3D)
for (int i = 0; i < Width * Height; i++)  // Use int for C# loops
{
    ushort x = (ushort)(i % Width);   // Convert to ushort for Wolf3D semantics
    ushort y = (ushort)(i / Width);   // Wolf3D Y coordinate
}
```

### Map Dimensions

**Original Wolf3D**: `unsigned width, height` (ID_CA.H:21)
**C# Mapping**: `ushort`

**Example (Assets layer - faithful to Wolf3D)**:
```csharp
// Original: unsigned width, height; (from ID_CA.H:21)
public ushort Width { get; set; }   // WL_DEF.H: map width (X dimension)
public ushort Height { get; set; }  // WL_DEF.H: map height (Y dimension)
```

**Note**: VR/Godot layer may translate Height → Depth to match Y-up coordinate system.

### Map Data Arrays

**Original Wolf3D**: Map data stored as 16-bit values
**C# Mapping**: `ushort[]`

**Example**:
```csharp
// Original: Stored as unsigned words in MAPTEMP (see WL_MAIN.C)
public ushort[] MapData { get; set; }    // Walls layer
public ushort[] ObjectData { get; set; }  // Objects layer
public ushort[] OtherData { get; set; }   // Other layer
```

### Shape/Sprite Numbers

**Original Wolf3D**: `int shapenum` (WL_DEF.H:935, 952)
**C# Mapping**: `short` (but often `ushort` for page numbers since they're never negative)

**Example**:
```csharp
// Original: int shapenum; (from WL_DEF.H:935)
public short ShapeNum { get; set; }  // Can be -1 to indicate removed

// Page numbers are always >= 0
public ushort PageNumber { get; set; }
```

### VSWAP/VGAGRAPH Page Numbers

**Original Wolf3D**: Pages are indexed with unsigned values
**C# Mapping**: `ushort`

**Example**:
```csharp
// VSWAP page access
public byte[] GetPage(ushort pageNumber)  // Original uses unsigned
public ushort NumPages { get; set; }       // Original: unsigned
public ushort SpritePage { get; set; }     // Original: unsigned
```

### Fixed-Point Coordinates (16.16)

**Original Wolf3D**: `typedef long fixed` (WL_DEF.H:713), `fixed x, y` (WL_DEF.H:996)
**C# Mapping**: `int` (NOT `uint` - must preserve sign for negative coords)

**Critical**: These represent 16.16 fixed-point values (16 bits integer, 16 bits fractional)

**Example**:
```csharp
// Original: fixed x, y; (from WL_DEF.H:996)
// "fixed" is typedef for long (32-bit signed)
public int X { get; set; }  // 16.16 fixed-point
public int Y { get; set; }  // 16.16 fixed-point

// Convert tile to fixed-point: tile << 16
// Convert fixed-point to tile: fixed >> 16
// Convert fixed-point to float: fixed / 65536.0f

// For doors, we use uint because door positions don't go negative
public uint BaseGridX { get; set; }  // Intentional: using 32-bit for precision
public uint CurrentX { get; set; }
```

### File Offsets and Sizes

**Original Wolf3D**: `long planestart[3]` (ID_CA.H:19), `unsigned planelength[3]` (ID_CA.H:20)
**C# Mapping**:
- For large file offsets: `uint` or `long`
- For lengths that fit in 16-bit: `ushort`

**Example**:
```csharp
// Original: long planestart[3]; unsigned planelength[3]; (from ID_CA.H:19-20)
public uint PlaneStart { get; set; }     // DOS long = 32-bit
public ushort PlaneLength { get; set; }  // DOS unsigned = 16-bit
```

### Angles

**Original Wolf3D**: `int angle` (WL_DEF.H:1008), `int viewangle` (WL_DRAW.C)
**Constants**: `ANGLES 360`, `FINEANGLES 3600`
**C# Mapping**: `short` for Wolf3D angles, `int` for C# calculations

**Example**:
```csharp
// Original: int angle; (from WL_DEF.H:1008)
public short Angle { get; set; }  // 0-359 (ANGLES) or 0-3599 (FINEANGLES)
```

### Hit Points and Speeds

**Original Wolf3D**: `int hitpoints` (WL_DEF.H:1009), `long speed` (WL_DEF.H:1010)
**C# Mapping**:
- Hit points: `short` (matches int)
- Speed: `int` (matches long, often fixed-point)

**Example**:
```csharp
// Original: int hitpoints; long speed; (from WL_DEF.H:1009-1010)
public short HitPoints { get; set; }
public int Speed { get; set; }  // Often represents fixed-point velocity
```

### Door State

**Original Wolf3D**: `int ticcount` (WL_DEF.H:974), `byte lock` (WL_DEF.H:972)
**C# Mapping**:
- Tic counter: `short` (matches int)
- Lock: `byte`

**Example**:
```csharp
// Original: byte lock; int ticcount; (from WL_DEF.H:972, 974)
public byte Lock { get; set; }
public short TicCount { get; set; }
```

### Area Numbers and Flags

**Original Wolf3D**: `byte areanumber` (WL_DEF.H:998), `byte flags` (WL_DEF.H:991)
**C# Mapping**: `byte`

**Example**:
```csharp
// Original: byte flags; byte areanumber; (from WL_DEF.H:991, 998)
public byte Flags { get; set; }
public byte AreaNumber { get; set; }
```

## C# Specific Considerations

### Array Indexing

Always use `int` for C# array indexing and loop variables, as this is C#'s native type:

```csharp
// CORRECT: C# iteration with int
for (int i = 0; i < pages.Length; i++)
{
    ushort pageNum = (ushort)i;  // Convert to Wolf3D type when needed
}

// AVOID: Using ushort for C# loops (not idiomatic)
for (ushort i = 0; i < pages.Length; i++)  // Don't do this
```

### Intentional Extensions

When we intentionally use more bits than the original Wolf3D, document it:

```csharp
// Intentional extension: Using uint (32-bit) for VR-specific door animation
// Original Wolf3D used byte for door position (0-63)
// We use 16.16 fixed-point (uint) for smooth VR door sliding
public uint CurrentX { get; set; }  // 16.16 fixed-point, extends original byte
```

### Type Conversions

Be explicit about conversions between C# int and Wolf3D types:

```csharp
// Reading from C# collection
for (int i = 0; i < MapData.Length; i++)
{
    ushort mapValue = MapData[i];  // Wolf3D value
    ushort x = (ushort)(i % Width);      // Convert C# int to Wolf3D ushort
    ushort z = (ushort)(i / Width);      // Convert C# int to Wolf3D ushort
}
```

## Documentation Format

When declaring variables that correspond to Wolf3D source code, use this comment format:

```csharp
// WL_DEF.H:statstruct:shapenum
public short ShapeNum { get; set; }
```

For intentional extensions (like tile coordinates):

```csharp
// WL_DEF.H:statstruct:tilex (original: byte)
// Intentional extension: Using ushort to support maps > 64×64
public ushort TileX { get; set; }
```

For other intentional deviations:

```csharp
// Based on WL_DEF.H:doorstruct
// Intentional extension: Using uint for 16.16 fixed-point door positions
// Original used byte for position (0-63)
public uint CurrentX { get; set; }
```

## Quick Reference Table

| Wolf3D Context | Original C Type | C# Type | Range | Example |
|----------------|-----------------|---------|-------|---------|
| **Tile coordinates (ALL)** | `byte`/`unsigned` | **`ushort`** ⚡ | 0-65535 | `ushort TileX` |
| Map dimensions | `unsigned` | `ushort` | 0-65535 | `ushort Width` |
| Map data values | `unsigned` | `ushort` | 0-65535 | `ushort[]` |
| Page numbers | `unsigned` | `ushort` | 0-65535 | `ushort page` |
| Shape numbers | `int` | `short` | -32768-32767 | `short shape` |
| Fixed-point coords | `fixed` (long) | `int` | -2B-2B | `int x` |
| File offsets | `long` | `uint`/`long` | 0-4B | `uint offset` |
| Angles | `int` | `short` | 0-3599 | `short angle` |
| Hit points | `int` | `short` | -32768-32767 | `short hp` |
| Speed | `long` | `int` | -2B-2B | `int speed` |
| Flags | `byte` | `byte` | 0-255 | `byte flags` |
| Area number | `byte` | `byte` | 0-255 | `byte area` |
| Lock type | `byte` | `byte` | 0-255 | `byte lock` |
| Tic counter | `int` | `short` | -32768-32767 | `short tics` |
| **C# loops/indices** | N/A | `int` | N/A | `for (int i...)` |

**Legend**: ⚡ = Intentional extension from original Wolf3D for modding support

## Common Patterns

### Pattern 1: Map Iteration (Assets Layer)
```csharp
// Assets layer - uses X, Y matching Wolf3D
// C# loop variable
for (int i = 0; i < Width * Height; i++)
{
    // Extract Wolf3D tile coordinates
    ushort x = (ushort)(i % Width);    // WL_DEF.H:tilex
    ushort y = (ushort)(i / Width);    // WL_DEF.H:tiley
    ushort tile = MapData[i];          // Map value
}
```

### Pattern 1b: Map Iteration (VR Layer)
```csharp
// VR layer - translates to Godot's X, Z (Y is vertical)
for (int i = 0; i < Width * Depth; i++)  // Depth = Wolf3D Height
{
    ushort x = (ushort)(i % Width);    // From Wolf3D tilex
    ushort z = (ushort)(i / Width);    // From Wolf3D tiley → Godot Z
    ushort tile = MapData[i];
}
```

### Pattern 2: VSWAP Page Access
```csharp
// Wolf3D page number type
public byte[] GetSprite(ushort pageNumber)  // ID_CA.C:page parameter
{
    // C# array indexing
    for (int i = 0; i < 64 * 64; i++)
    {
        // ...
    }
}
```

### Pattern 3: Fixed-Point Math
```csharp
// WL_DEF.H:fixed (typedef long)
public int X { get; set; }  // 16.16 fixed-point

// Convert tile to fixed-point (WL_MAIN.C:#define TILEGLOBAL)
int fixedX = tileX << 16;  // Tile to fixed-point

// Convert fixed-point to tile
ushort tileX = (ushort)(fixedX >> 16);

// Convert to float for rendering
float floatX = fixedX / 65536.0f;
```

### Pattern 4a: Door Structure (Assets Layer)
```csharp
// Assets layer - faithful to Wolf3D coordinate system
public ushort TileX { get; set; }       // WL_DEF.H:doorstruct:tilex (original: byte)
public ushort TileY { get; set; }       // WL_DEF.H:doorstruct:tiley (original: byte)
public bool FacesEastWest { get; set; } // WL_DEF.H:doorstruct:vertical (renamed for clarity)
public byte Lock { get; set; }          // WL_DEF.H:doorstruct:lock
```

### Pattern 4b: Door Structure (VR Layer)
```csharp
// VR layer - translated to Godot Y-up coordinate system
public ushort TileX { get; set; }       // From Wolf3D tilex
public ushort TileZ { get; set; }       // From Wolf3D tiley (Godot Z axis)
public bool FacesEastWest { get; set; } // From Wolf3D vertical (renamed for 3D clarity)
public byte Lock { get; set; }

// Intentional extension for VR smooth animation
public uint CurrentX { get; set; }   // 16.16 fixed-point (original: byte position 0-63)
public uint CurrentZ { get; set; }   // 16.16 fixed-point (from Wolf3D Y)
```

## References

- `wolf3d/WL_DEF.H` - Main type definitions and structures
- `wolf3d/ID_HEAD.H` - Basic typedefs (byte, word, longword)
- `wolf3d/ID_CA.H` - Cache manager structures
- `wolf3d/WL_MAIN.C` - Main game logic
- `wolf3d/WL_PLAY.C` - Gameplay logic

## Notes

1. **Signedness Matters**: The original Wolf3D carefully chose signed vs unsigned types. Preserve this unless there's a good reason to change.

2. **Fixed-Point is Signed**: The `fixed` typedef (long/int) MUST be signed to handle negative coordinates properly.

3. **16-bit Mindset**: Remember that `int` and `unsigned` in DOS C were 16-bit, not 32-bit like modern C.

4. **Defensive Programming**: When converting from C# `int` to Wolf3D types, validate ranges in debug builds.

5. **Future Proofing**: If extending a type's range, document why and ensure compatibility with original data formats.
