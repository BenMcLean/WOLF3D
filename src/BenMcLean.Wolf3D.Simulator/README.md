# BenMcLean.Wolf3D.Simulator

Discrete event simulator for Wolfenstein 3D game logic. This library provides a deterministic, serializable simulation of Wolf3D game mechanics running at the original 70Hz tic rate.

## Design Philosophy

- **Deterministic**: Given the same inputs, produces identical results
- **Tic-quantized**: All inputs and updates align to 70Hz tic boundaries (matching WL_DRAW.C:CalcTics)
- **Event-driven**: Outputs events describing what happened, allowing decoupled presentation
- **Serializable**: Only dynamic state is saved; static level data comes from MapAnalysis
- **Engine-agnostic**: No dependencies on Godot, Unity, or any specific game engine
- **Standards-compliant**: Follows DATA_TYPES.md for authentic Wolf3D data representation

## Architecture Integration

The simulator relies on `MapAnalyzer.MapAnalysis` (from BenMcLean.Wolf3D.Assets) for initial level data:

- **MapAnalysis.DoorSpawns** → Initialize doors with positions and orientations
- **Simulator state** → Only serialize changes (position, action, ticcount)
- **No duplication** → Static door data (X, Y, FacesEastWest, lock type) lives in MapAnalysis

This matches how the original Wolf3D worked: level data loaded once, only dynamic state saved in save games.

## Current Implementation Status

### ✅ Implemented: Doors

Doors are fully functional with authentic Wolf3D behavior based on WL_ACT1.C:

- **Four states**: Closed, Opening, Open, Closing (WL_DEF.H:doorstruct:action)
- **Automatic closing**: Doors auto-close after 300 tics ~4.3 seconds (WL_ACT1.C:OPENTICS)
- **Lock types**: Normal, Lock1-4 (gold/silver keys), Elevator (WL_DEF.H:door_t)
- **Position tracking**: 16-bit position from 0 (closed) to 0xFFFF (open) (WL_ACT1.C:doorposition)
- **Movement speed**: `position += tics << 10` per update (WL_ACT1.C:DoorOpening line 739)
- **Events**: DoorOpening, DoorOpened, DoorClosing, DoorClosed, DoorLocked

### ⚠️ Partially Implemented

Doors have TODOs for:
- Collision detection (WL_ACT1.C:CloseDoor lines 574-611 - checking if actors/player block door closing)
- Area connectivity updates (WL_ACT1.C:DoorOpening lines 727-729 - for sound/sight propagation)

### ❌ Not Yet Implemented

- **Pushwalls**: Moving wall segments (WL_ACT1.C:MovePWalls)
- **Actors**: Enemies, player physics
- **Area system**: Sound/sight connectivity graph (WL_ACT1.C:areaconnect)
- **Map data structures**: Tile maps, actor grids
- **Projectiles**: Bullets, rockets, flames

## Usage Example

```csharp
using BenMcLean.Wolf3D.Simulator;
using BenMcLean.Wolf3D.Assets;

// Load map data
var mapAnalyzer = new MapAnalyzer(xmlDocument);
var gameMap = /* load GameMap from MAPHEAD/GAMEMAPS */;
var mapAnalysis = mapAnalyzer.Analyze(gameMap);

// Create simulator and load doors from MapAnalysis
var simulator = new Simulator();

// Convert MapAnalysis.DoorSpawns to simulator doors
// You'll need to map door shapes to lock types based on your game XML
simulator.LoadDoorsFromMapAnalysis(
    mapAnalysis.Doors.Select(d => (d.Shape, d.X, d.Y, d.FacesEastWest)),
    shape => DetermineLockType(shape) // Your function to map shape → lock type
);

// Game loop (60fps rendering, 70Hz simulation)
double deltaTime = 1.0 / 60.0;
var events = simulator.Update(deltaTime);

// Process events
foreach (var evt in events)
{
    if (evt is DoorOpeningEvent opening)
    {
        // Play OPENDOORSND at (opening.TileX, opening.TileY)
    }
    else if (evt is DoorLockedEvent locked)
    {
        // Play NOWAYSND (player doesn't have required key)
    }
}

// Player presses "use" button near door index 0
simulator.QueueAction(new OperateDoorAction
{
    DoorIndex = 0,
    PlayerKeys = 0b0001 // Has key for Lock1 (gold key)
});
```

## Data Type Standards

All types follow DATA_TYPES.md conventions:

| Field | Wolf3D Type | C# Type | Reference |
|-------|-------------|---------|-----------|
| TileX, TileY | `byte` | `ushort` ⚡ | WL_DEF.H:doorstruct:tilex/tiley |
| FacesEastWest | `vertical` | `bool` | WL_DEF.H:doorstruct:vertical (renamed) |
| Lock | `byte` | `byte` | WL_DEF.H:doorstruct:lock |
| Action | `enum` | `DoorAction` (byte) | WL_DEF.H:doorstruct:action |
| Position | `unsigned` | `ushort` | WL_ACT1.C:doorposition |
| TicCount | `int` | `short` | WL_DEF.H:doorstruct:ticcount |

⚡ = Intentional extension to support maps > 64×64 for modding

## Time Handling

### Tic System (WL_DRAW.C:CalcTics)

- **TicRate**: 70 Hz (matching original Wolf3D exactly)
- **TicDuration**: 1/70 second ≈ 14.2857 milliseconds
- **Tic calculation**: `tics = TimeCount - lasttimecount`
- **Variable tics**: Original game used adaptive timing (more tics if frame took longer)
- **MaxTicsPerUpdate**: 10 (prevents "spiral of death" if frame rate drops)

### Determinism

All player actions are queued and processed at the **next tic boundary**, ensuring determinism regardless of when actions are submitted during a frame. This enables:
- Save/load games with exact state reproduction
- Network play with synchronized clients
- Demo recording and playback

## Event System

Events are emitted during `Update()` and describe **what happened** with precise timing. The presentation layer (Godot, Unity, etc.) consumes these events to:

- Play sounds at specific 3D positions (OPENDOORSND, CLOSEDOORSND, NOWAYSND)
- Trigger door animations (interpolate door.Position for smooth rendering)
- Update visual state
- Show UI feedback (locked door message, etc.)

Events include timestamps for synchronization with audio/visual systems.

## Serialization Strategy

### What to Serialize (Dynamic State)

For save games, serialize **only the changed state**:

1. **CurrentTic**: Simulation time
2. **AccumulatedTime**: Fractional tic remainder
3. **For each door**:
   - Position (ushort) - WL_ACT1.C:doorposition
   - Action (DoorAction byte) - WL_DEF.H:doorstruct:action
   - TicCount (short) - WL_DEF.H:doorstruct:ticcount

### What NOT to Serialize (Static Level Data)

These come from MapAnalysis and never change:
- Door positions (TileX, TileY)
- Door orientations (FacesEastWest)
- Lock types (Lock)
- Map geometry, spawn points, etc.

This matches original Wolf3D's save game format which only saved dynamic state.

## Wolf3D Source References

All code is documented with references to original Wolf3D source:

- **WL_ACT1.C**: Door/pushwall logic (lines 370-1082)
- **WL_DEF.H**: Data structure definitions (doorstruct lines 964-975)
- **WL_DRAW.C**: CalcTics timing (lines 1518-1563)
- **WL_PLAY.C**: Main game loop (PlayLoop)

Comments in code reference specific functions and line numbers for traceability.

## TODO: Missing Pieces for Full Door Simulation

### 1. Collision Detection (WL_ACT1.C:CloseDoor lines 574-611)

Doors need to check if they can close:

```csharp
bool CanDoorClose(int doorIndex)
{
    var door = doors[doorIndex];

    // WL_ACT1.C:574 - check actorat
    if (actorAt[door.TileX, door.TileY] != null)
        return false;

    // WL_ACT1.C:577-578 - check player position
    if (player.TileX == door.TileX && player.TileY == door.TileY)
        return false;

    // WL_ACT1.C:580-611 - check MINDIST for vertical/horizontal doors
    if (door.FacesEastWest)
    {
        // Check player within MINDIST (0x5800 in fixed-point)
        if (player.TileY == door.TileY)
        {
            if (((player.X + MINDIST) >> 16) == door.TileX) return false;
            if (((player.X - MINDIST) >> 16) == door.TileX) return false;
        }
        // Check actors at adjacent tiles
        // ...
    }
    else // North/South facing
    {
        // Similar checks for Y axis
        // ...
    }

    return true;
}
```

This requires:
- Actor position tracking (16.16 fixed-point coordinates)
- `MINDIST` constant (0x5800 = WL_DEF.H:MINDIST)
- `actorat[,]` grid

### 2. Area Connectivity (WL_ACT1.C:DoorOpening lines 710-733, DoorClosing lines 795-813)

When doors open/close, update area connections:

```csharp
void UpdateAreaConnections(int doorIndex, bool opening)
{
    var door = doors[doorIndex];

    // WL_ACT1.C:717-724 - get areas on each side
    int area1, area2;
    if (door.FacesEastWest)
    {
        area1 = map.GetArea(door.TileX + 1, door.TileY);
        area2 = map.GetArea(door.TileX - 1, door.TileY);
    }
    else
    {
        area1 = map.GetArea(door.TileX, door.TileY - 1);
        area2 = map.GetArea(door.TileX, door.TileY + 1);
    }

    // WL_ACT1.C:727-729 - adjust reference counts
    if (opening)
    {
        areaConnect[area1, area2]++;
        areaConnect[area2, area1]++;
    }
    else
    {
        areaConnect[area1, area2]--;
        areaConnect[area2, area1]--;
    }

    // WL_ACT1.C:729 - recalculate connectivity
    RecalculateAreasByPlayer();
}
```

This requires:
- Area map data (WL_ACT1.C:mapsegs)
- Area connection reference count matrix (WL_ACT1.C:areaconnect)
- Player area tracking (WL_ACT1.C:areabyplayer)

## Next Steps

1. **Add collision detection**: Implement `CanDoorClose()` with actor checking
2. **Add area system**: Implement area connectivity for sound/sight
3. **Add pushwalls**: Similar state machine to doors (WL_ACT1.C:MovePWalls)
4. **Add player physics**: Position, movement, collision
5. **Add actors**: Enemies with AI state machines

## Reference

For authentic Wolf3D behavior, see `/wolf3d/WL_ACT1.C` (doors/pushwalls) and `/wolf3d/WL_DEF.H` (data structures).
