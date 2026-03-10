# ElanAP — Touchpad Absolute Positioning for osu!

## Requirements

- Windows 10/11
- .NET Framework 4.6.1+ (included in Windows)
- Windows Precision Touchpad  
  *(Check: Settings → Bluetooth & devices → Touchpad → "Your PC has a precision touchpad")*

## Download

Grab the latest from [Releases](https://github.com/quanq026/ElanAP/releases). You need:
- `ElanAP.exe`
- `HidSharp.dll` (same folder)

## Features

- **Absolute positioning** — touchpad = tablet, finger position maps directly to screen
- **Full deadzone blocking** — touches outside configured area produce zero cursor movement
- **Drag-to-select area** — draw your area directly on the visual map
- **Preset profiles** — Full Area, Center 75%, Center 50%, Top/Bottom/Left/Right Half
- **Screen resolution presets** — FHD, QHD, 4K, and more common resolutions
- **Global hotkey F6** — Toggle on/off from any app
- **Lock aspect ratio** — Touchpad area follows screen aspect ratio
- **System tray** — Minimize to tray
- **Save/Load configs** — XML-based configuration files
- **Optimized** — Zero-allocation hot path, cached parser indices, minimal latency

## Usage

1. Run `ElanAP.exe`
2. Set **Screen Bounds** — where on your display the cursor should move
3. Set **Touchpad Bounds** — which part of the touchpad maps to the screen area
4. Press **Start** or **F6**
5. Press **F6** to stop (mouse is blocked while active, so use keyboard)

### Tips
- Use **profiles** (ComboBox) for quick area setup
- **Right-click** the map for centering options
- **Lock aspect ratio** prevents distortion
- Config auto-saves to `default.cfg`

## Building

### Option 1: Visual Studio (recommended)

1. Open `ElanAP\ElanAP.csproj` in **Visual Studio 2017+**
2. Build → Build Solution (`Ctrl+Shift+B`)

### Option 2: Command line

NuGet packages (`HidSharp`, `Microsoft.Net.Compilers`) are already included in the `packages/` folder — no `nuget restore` needed.

**If MSBuild is in PATH** (Visual Studio Developer Command Prompt):
```
msbuild ElanAP\ElanAP.csproj /p:Configuration=Release
```

**If MSBuild is NOT in PATH** (plain PowerShell/cmd):
```
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe ElanAP\ElanAP.csproj /p:Configuration=Release
```

> **Note:** The .NET Framework 4.0 MSBuild will show a ToolsVersion warning and some assembly mismatch warnings — these are safe to ignore, the build still succeeds.

### Output

- **Debug:** `ElanAP\bin\Debug\ElanAP.exe`
- **Release:** `ElanAP\bin\Release\ElanAP.exe`

To run, keep `HidSharp.dll` in the same folder as `ElanAP.exe`.

## Credits

- Inspired by [SynAP](https://github.com/InfinityGhost/SynAP) by InfinityGhost
- Uses [HidSharp](https://www.zer7.com/software/hidsharp) for HID descriptor parsing
- Built for the osu! community
