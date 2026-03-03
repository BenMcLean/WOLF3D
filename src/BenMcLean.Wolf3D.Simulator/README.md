# BenMcLean.Wolf3D.Simulator

Discrete event simulator for Wolfenstein 3D game logic. This library provides a deterministic, serializable simulation of Wolf3D game mechanics running at the original 70Hz tic rate.

## Design Philosophy

- **Deterministic**: Given the same inputs, produces identical results
- **Tic-quantized**: All inputs and updates align to 70Hz tic boundaries (matching WL_DRAW.C:CalcTics)
- **Event-driven**: Outputs events describing what happened, allowing decoupled presentation
- **Serializable**: Only dynamic state is saved; static level data comes from MapAnalysis
- **Engine-agnostic**: No dependencies on Godot, Unity, or any specific game engine

## Architecture Integration

The simulator relies on `MapAnalyzer.MapAnalysis` (from BenMcLean.Wolf3D.Assets) for initial level data:

- **MapAnalysis.DoorSpawns** → Initialize doors with positions and orientations
- **Simulator state** → Only serialize changes (position, action, ticcount)
- **No duplication** → Static door data (X, Y, FacesEastWest, lock type) lives in MapAnalysis

This matches how the original Wolf3D worked: level data loaded once, only dynamic state saved in save games.

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

- **WL_ACT1.C**: Door/pushwall logic
- **WL_DEF.H**: Data structure definitions
- **WL_DRAW.C**: CalcTics timing
- **WL_PLAY.C**: Main game loop (PlayLoop)

Comments in code reference specific functions for traceability.

## Reference

For authentic Wolf3D behavior, see `/wolf3d/WL_ACT1.C` (doors/pushwalls) and `/wolf3d/WL_DEF.H` (data structures).
