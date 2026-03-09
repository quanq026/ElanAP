# SynAP / ElanAP — Touchpad Absolute Positioning for osu!

Turns your touchpad into a drawing tablet. Maps touchpad coordinates directly to screen position — no more relative cursor movement.

| Project | Touchpad Support | Technology |
|---------|-----------------|------------|
| **SynAP** | Synaptics (COM SDK) | Synaptics Pointing Device Drivers |
| **ElanAP** | Any Windows Precision Touchpad (Elan, Synaptics PTP, ALPS, etc.) | Win32 Raw Input + HidSharp |

> **ElanAP** works on any laptop with a Windows Precision Touchpad (most laptops from 2018+).
> Check: Settings → Bluetooth & devices → Touchpad → "Your PC has a precision touchpad"

## Requirements

- .NET Framework 4.6.1 or newer
- **SynAP**: Synaptics Touchpad + Synaptics Pointing Device Drivers
- **ElanAP**: Any Windows Precision Touchpad (Windows 10/11)

## Features (ElanAP)

- **Absolute positioning** — touchpad maps directly to screen area like a drawing tablet
- **Deadzone blocking** — touches outside configured area are completely ignored
- **Drag-to-select** — draw area directly on the visual map
- **Preset profiles** — Full Area, Center 75%, Center 50%, Top/Bottom/Left/Right Half
- **Screen resolution presets** — FHD, QHD, 4K, and more
- **Global hotkey F6** — Toggle on/off from any app
- **Lock aspect ratio** — Touchpad area follows screen aspect ratio
- **System tray** — Minimize to tray
- **Save/Load configs** — XML-based configuration files
- **Zero-allocation hot path** — Optimized for minimal input latency

## Usage

#### Bounds
* **Screen bounds** set the area in which you want your cursor to be limited to on your display
* **Touchpad bounds** set the area where you want the finger position to be translated to on the screen area.

#### Settings
* The **Lock Aspect Ratio** checkbox (if enabled) forces the area of the touchpad bounds to follow the same aspect ratio of the screen area.

#### ElanAP Tips
1. Run `ElanAP.exe` (alongside `HidSharp.dll`)
2. Set Screen Bounds and Touchpad Bounds (or use presets)
3. Press **Start** or **F6**
4. Press **F6** again to stop

## Building

```
nuget restore
msbuild SynAP\SynAP.csproj /p:Configuration=Release
msbuild ElanAP\ElanAP.csproj /p:Configuration=Release
```

## Screenshots

**SynAP** *(Pre-Release v4.0.1)*

![Main Window v4.0.1](https://i.imgur.com/xjCS4gl.png)

## Credits

- Original SynAP by [InfinityGhost](https://github.com/InfinityGhost)
- ElanAP uses [HidSharp](https://www.zer7.com/software/hidsharp) for HID descriptor parsing
- Built for the osu! community