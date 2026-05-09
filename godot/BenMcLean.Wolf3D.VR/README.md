# BenMcLean.Wolf3D.VR

This project supports a small set of runtime command line options for choosing the display mode and enabling the spectator capture view.
Project-specific options can be passed either directly or after Godot's `--` user-argument separator.

## Command Line Options

| Option | Meaning |
| --- | --- |
| `--flatscreen` | Force flatscreen mode and skip OpenXR initialization. |
| `--no-vr` | Alias for `--flatscreen`. |
| `--5dof` | Force VR mode and use 5DOF play. |
| `--roomscale` | Force VR mode and use roomscale play. |
| `--spectator` | Enable the spectator view compositor for desktop capture. |
| `--no-spectator` | Disable spectator view, even if it was enabled through the environment. |

## Defaults

- If no display mode option is provided, the game tries to initialize OpenXR.
- If OpenXR initializes successfully, the game runs in VR.
- If OpenXR is unavailable or fails to initialize, the game falls back to flatscreen mode.
- The default VR play mode is `Roomscale`.
- Spectator view is disabled by default because it adds an extra 3D render pass.

## Option Precedence

- `--flatscreen` and `--no-vr` take priority over VR mode options.
- `--roomscale` takes priority over `--5dof` when both are present.
- `--spectator` overrides the spectator environment variable and forces spectator view on.
- `--no-spectator` overrides the spectator environment variable and forces spectator view off.

## Environment Variables

These are not command line options, but they affect the same runtime behavior:

- `WOLF3D_VR_PLAY_MODE=5dof`
- `WOLF3D_VR_PLAY_MODE=roomscale`
- `WOLF3D_VR_SPECTATOR=1`
- `WOLF3D_VR_SPECTATOR=true`
- `WOLF3D_VR_SPECTATOR=yes`
- `WOLF3D_VR_SPECTATOR=on`

Command line options override these environment-based settings where applicable.

## Examples

Run in flatscreen mode:

```text
BenMcLean.Wolf3D.VR.exe --flatscreen
```

Run in VR with roomscale locomotion:

```text
BenMcLean.Wolf3D.VR.exe --roomscale
```

Run in VR with spectator capture enabled:

```text
BenMcLean.Wolf3D.VR.exe --spectator
```

Run in flatscreen mode and explicitly disable spectator view:

```text
BenMcLean.Wolf3D.VR.exe --flatscreen --no-spectator
```

Run with Godot Movie Writer and pass VR project options after `--`:

```text
godot --path godot/BenMcLean.Wolf3D.VR --write-movie capture.avi -- --spectator --roomscale
```

## Notes

- There is currently no built-in `--help` option.
- When spectator view is enabled, the desktop window is locked to `1920x1080` for stable capture output.
- Godot engine options such as `--write-movie` are handled by the engine. VR project options may be passed after `--` and are still recognized by this project.
