# OnStartup Implementation Plan

## Goal

Replace `Menus/@Start` with a mandatory `<OnStartup>` Lua element on `<Game>`.
`<OnStartup>` determines what happens immediately after a game's assets finish loading:
either navigate to a named menu screen or jump directly to a level by flat map index.

## Design Decisions (settled)

- `<OnStartup>` is **mandatory** on `<Game>` — there are no situations in which it is not needed.
- `Menus/@Start` attribute is **eliminated** — fully replaced by `<OnStartup>`.
- `StartLevel(n)` uses the **flat `MapAnalyses` index** (0-based integer). Episode and level
  numbers are cosmetic display values only; they never appear in internal code paths.
- **Hard crash with error** if Lua calls something that doesn't exist (null VgaGraph, missing
  menu name, out-of-range level index, etc.). No silent no-ops anywhere.
- Execution uses the **existing `MenuScriptContext` + `LuaScriptEngine` machinery** — the same
  path as menu scripts that run while menus are showing but before an action stage starts.
  `LuaScriptEngine.DoString(code, context)` is the call site.
- The existing `LuaScriptEngine.ExecuteActionFunction` TODO (currently silently skips missing
  functions) must be **resolved to throw** as part of this work.

## Lua API available inside `<OnStartup>`

```lua
LoadMenu("Main")    -- navigate to a named menu; throws if VgaGraph is null or menu not found
StartLevel(0)       -- start level at flat MapAnalyses index; throws if Maps is empty or out of range
```

Both are the only two top-level functions. All other `MenuScriptContext` methods are also
available (inherited from the same engine), but `LoadMenu` and `StartLevel` are the two that
make sense for OnStartup use.

## Example XML

```xml
<!-- Wolf3D: show main menu first -->
<Game Name="Wolfenstein 3-D Shareware" ...>
  <OnStartup>LoadMenu("Main")</OnStartup>
  ...
</Game>

<!-- KOD: jump straight to level 0 -->
<Game Name="Kitchens of Doom" ...>
  <OnStartup>StartLevel(0)</OnStartup>
  ...
</Game>
```

---

## Files to Change

### 1. `games/WOLF3D.xsd`

- Add `<xs:element name="OnStartup" type="xs:string"/>` (no `minOccurs="0"` — it is required)
  to the `<xs:all>` block inside the `Game` complex type.
- Remove the `Start` attribute definition from the `Menus` element
  (`<xs:attribute name="Start" type="xs:string">`).

### 2. `games/WL1.xml` and `games/N3D.xml`

- Add `<OnStartup>LoadMenu("Main")</OnStartup>` as a child of the root `<Game>` element.
- Remove the `Start="..."` attribute from each `<Menus>` element.

### 3. `godot/BenMcLean.Wolf3D.Shared/Menu/MenuScriptContext.cs`

Add two members inside the `#region Navigation` block:

```csharp
/// <summary>
/// Delegate for starting a level by flat map index.
/// Set by Root after context creation.
/// </summary>
public Action<int> StartLevelAction { get; set; }

/// <summary>
/// Navigate to a named menu screen.
/// Alias for NavigateToMenu — preferred name in OnStartup scripts.
/// Throws InvalidOperationException if VgaGraph is null or menu name not found.
/// Exposed to Lua.
/// </summary>
public void LoadMenu(string menuName) => NavigateToMenuAction?.Invoke(menuName);

/// <summary>
/// Start a level by its flat MapAnalyses index (0-based).
/// Throws InvalidOperationException if Maps is empty or index is out of range.
/// Exposed to Lua.
/// </summary>
public void StartLevel(int mapIndex) => StartLevelAction?.Invoke(mapIndex);
```

### 4. `src/BenMcLean.Wolf3D.Assets/Gameplay/MenuData.cs`

- Remove `StartMenu` property from `MenuCollection`.
- Remove `StartMenu = menusElement.Attribute("Start")?.Value` from `MenuCollection.Load`.
- Remove the XML doc comment that references `Start` attribute.

### 5. `godot/BenMcLean.Wolf3D.Shared/Menu/MenuManager.cs`

- Remove all reads of `MenuCollection.StartMenu`.
- The initial menu to navigate to on first show is now determined solely by `Root` via OnStartup.
  `MenuManager` no longer needs to know which menu is "first" — it just waits to be told.

### 6. `godot/BenMcLean.Wolf3D.VR/MenuStage/MenuRoom.cs`

- `StartMenuOverride` remains (still used for intermission, game-over, death, etc.).
- The initial `NavigateToMenu` call that currently falls back to `MenuCollection.StartMenu`
  is removed — the caller (Root via OnStartup) is always responsible for the initial navigation.

### 7. `godot/BenMcLean.Wolf3D.VR/Root.cs`

Replace the block after `setupRoom.IsComplete && !setupRoom.IsInitialLoad` (currently ends with
`TransitionTo(new MenuRoom(DisplayMode))`) with OnStartup execution:

```csharp
// Read OnStartup Lua code (mandatory — throws if missing)
string onStartupCode = SharedAssetManager.CurrentGame.XML
    .Element("OnStartup")?.Value
    ?? throw new InvalidOperationException(
        "Missing required <OnStartup> element in game XML.");

// Build a minimal script context wired for startup transitions
MenuScriptContext startupCtx = new(
    SharedAssetManager.CurrentGame.MenuCollection,   // may be empty MenuCollection
    SharedAssetManager.Config)
{
    NavigateToMenuAction = menuName =>
    {
        // Throws if VgaGraph is null
        if (SharedAssetManager.CurrentGame.VgaGraph == null)
            throw new InvalidOperationException(
                $"LoadMenu(\"{menuName}\") called but game has no VgaGraph.");
        // Throws if menu not found
        if (SharedAssetManager.CurrentGame.MenuCollection?.GetMenu(menuName) == null)
            throw new InvalidOperationException(
                $"LoadMenu(\"{menuName}\"): menu \"{menuName}\" not found.");
        TransitionTo(new MenuRoom(DisplayMode) { StartMenuOverride = menuName });
    },
    StartLevelAction = mapIndex =>
    {
        // Throws if Maps is empty or index is out of range
        MapAnalyzer.MapAnalysis[] analyses = SharedAssetManager.CurrentGame.MapAnalyses;
        if (analyses == null || analyses.Length == 0)
            throw new InvalidOperationException(
                $"StartLevel({mapIndex}) called but game has no maps.");
        if (mapIndex < 0 || mapIndex >= analyses.Length)
            throw new InvalidOperationException(
                $"StartLevel({mapIndex}): index out of range (0–{analyses.Length - 1}).");
        _suspendedGame = null;
        TransitionTo(new ActionRoom(DisplayMode, levelIndex: mapIndex));
    },
};

// Execute OnStartup — throws on any error (hard crash policy)
LuaScriptEngine startupEngine = new([typeof(MenuScriptContext)]);
startupEngine.DoString(onStartupCode, startupCtx);
```

Note: `_suspendedGame = null` before `StartLevel` matches existing new-game logic.

### 8. `src/BenMcLean.Wolf3D.Simulator/Lua/LuaScriptEngine.cs`

Fix the TODO in `ExecuteActionFunction` (lines ~438–444): replace the silent-skip with a hard
throw:

```csharp
if (!compiledActionFunctions.TryGetValue(functionName, out DynValue compiled))
    throw new InvalidOperationException(
        $"Action function '{functionName}' not compiled. " +
        "Ensure CompileAllActionFunctions was called and the function is defined in the XML.");
```

---

## MenuCollection.GetMenu helper (may already exist — check first)

`Root`'s `NavigateToMenuAction` guard calls `MenuCollection.GetMenu(menuName)`. If this method
does not already exist on `MenuCollection`, add it:

```csharp
/// <summary>Returns the named menu, or null if not found.</summary>
public MenuScreen GetMenu(string name) =>
    Menus.TryGetValue(name, out MenuScreen m) ? m : null;
```

---

## What does NOT change

- `StartMenuOverride` on `MenuRoom` — still used for intermission, game-over, death screens, etc.
- `PauseMenu` on `MenuCollection` / `Menus/@Pause` in XSD — still valid, unrelated to OnStartup.
- All `ActionRoom` construction paths — OnStartup creates `ActionRoom` the same way as the
  existing `menuRoom.ShouldStartGame` path.
- `CurrentLevelIndex` export on `Root` — can be removed as dead code after this change since
  level index is now always supplied by the script, but that cleanup is optional/separate.

---

## Reading order for a new context

1. Read `Root.cs` (full file) to understand current boot flow.
2. Read `MenuScriptContext.cs` (already read) to know what's there.
3. Read `MenuManager.cs` (focus on constructor and `CompileMenuFunctions`).
4. Read `MenuData.cs` around `MenuCollection` and `StartMenu`.
5. Read `WL1.xml` lines 1–10 and the `<Menus>` element to find `Start=` attribute.
6. Apply changes in the order listed above.
7. Build with `dotnet build` from `BenMcLean.Wolf3D/` and verify zero errors.
