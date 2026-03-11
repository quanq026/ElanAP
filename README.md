# ElanAP

Turns a Windows Precision Touchpad into an absolute-positioning input device — like a graphics tablet, but built into your laptop. Each finger position maps 1:1 to a defined area on screen, with sub-millisecond latency and no cursor drift.

## Who is this for?

- **osu! / osu!mania players** — use your touchpad the way a tablet player would, or map touchpad zones to keys for mania
- **Rhythm game players** — any game that benefits from absolute tap input (Cytus, Lanota, Malody, etc.)
- **Developers / power users** — anyone who wants to remap raw touchpad input to keyboard keys or cursor regions

## Features

### Absolute Cursor Mode
The touchpad behaves like a drawing tablet. Your finger's physical position on the pad maps directly to a region on screen — no relative dragging, no drift. Lift and retouch anywhere instantly.

- Configurable screen and touchpad bounds
- Drag-to-select area on an interactive visual map
- Preset profiles: Full Area, Center 75%, Center 50%, Half regions
- Lock aspect ratio to match screen proportions
- Standard screen resolution presets (FHD, QHD, 4K, ...)

### Mania Mode (Key Mapping)
Divides the touchpad into vertical zones, each mapped to a keyboard key. Supports 2–8 zones.

- Each contact fires its zone's key instantly on touch-down, releases on lift
- Multi-touch: simultaneous contacts on different zones press independent keys
- Sliding between zones transitions the key press correctly
- Useful for: osu!mania, DJMAX, and any game or app where tapping regions should trigger keys

### Input Quality
- Raw HID input via Windows Raw Input API — bypasses the OS gesture stack entirely
- Direct byte parsing on hot path — zero managed allocations per frame, no GC spikes
- Dedicated input thread at `ThreadPriority.Highest` with 1ms timer resolution
- Typical end-to-end latency: **~100–200μs** average, sub-1ms at normal play rates
- Touchpad gesture suppression via hooks — no persistent system changes, crash-safe

### Why is it this fast?

The main bottleneck in naïve implementations is the HID parsing library — HidSharp allocates managed objects on every report, triggering .NET GC pauses up to ~10ms. ElanAP eliminates this by using HidSharp only once at startup to discover the bit layout of the touchpad's HID descriptor, then discards it in favor of a hand-rolled bit reader that operates directly on the raw bytes. The hot path — from `WM_INPUT` arriving to `SendInput` firing — does zero heap allocations. Combined with a dedicated message-only window on a `ThreadPriority.Highest` thread, `timeBeginPeriod(1)` for scheduler resolution, and `GC.TryStartNoGCRegion` during active use, GC pauses are structurally prevented rather than just minimized.

### General
- Global hotkey **F6** to toggle on/off from any application
- System tray support
- Per-config XML save/load, auto-saves to `default.cfg`
- Single instance enforcement

## Requirements

- Windows 10/11
- .NET Framework 4.6.1+ (pre-installed on most Windows systems)
- A **Windows Precision Touchpad**  
  *(Verify: Settings → Bluetooth & devices → Touchpad → "Your PC has a precision touchpad")*

## Download

Grab the latest from [Releases](https://github.com/quanq026/ElanAP/releases). Place these two files in the same folder:
- `ElanAP.exe`
- `HidSharp.dll`

## Building

NuGet packages are pre-included in `packages/` — no restore needed.

**Visual Studio 2017+:** Open `ElanAP\ElanAP.csproj`, press `Ctrl+Shift+B`.

**Command line:**
```
# With MSBuild in PATH (VS Developer Prompt)
msbuild ElanAP\ElanAP.csproj /p:Configuration=Release

# Without MSBuild in PATH
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe ElanAP\ElanAP.csproj /p:Configuration=Release
```

Output: `ElanAP\bin\Release\ElanAP.exe` (keep `HidSharp.dll` alongside it).

> MSBuild may show ToolsVersion or architecture mismatch warnings — these are harmless.

## Credits

- Inspired by [SynAP](https://github.com/InfinityGhost/SynAP) by InfinityGhost
- Uses [HidSharp](https://www.zer7.com/software/hidsharp) for HID device enumeration and descriptor parsing
