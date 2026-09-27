# GroundBlastFx

Ground effects for rocket engines in KSP 1.12. Dust, launch pad steam, water spray, ejecta on airless moons, scorch marks and flame light. It all depends on the engine thrust, the height, the ground type and the atmosphere.

Download on SpaceDock or in the releases here. Copy `GameData/GroundBlastFx` into your KSP GameData folder.

Needs KSP 1.12.x on Windows (DirectX 11, the default) and a GPU that supports compute shaders. No other mod needed.

License: MIT. Player info is in [GameData/GroundBlastFx/README.md](GameData/GroundBlastFx/README.md).

## Screenshots

![](docs/images/01_pad_steam_day.webp)
![](docs/images/02_pad_steam_night.webp)
![](docs/images/03_pad_flame_light.webp)
![](docs/images/04_dust_hover_side.webp)
![](docs/images/05_dust_hover_top.webp)
![](docs/images/06_dust_wind_drift.webp)
![](docs/images/07_dust_liftoff_near_pad.webp)
![](docs/images/08_water_spray_top.webp)
![](docs/images/09_water_spray_side.webp)
![](docs/images/10_mun_ejecta_sheet.webp)
![](docs/images/11_mun_blast_mark.webp)

## How it works

The plugin looks at every running engine, finds where the plume hits the ground with raycasts, and groups engines that are close together. Each group gets a 3D grid anchored to the ground, simulated on the GPU (`VolumeField.compute`), then drawn by a raymarching shader (`GroundVolume.shader`). On airless bodies there is no cloud, just an ejecta sheet and particles. Scorch marks are saved in your save file.

## Folders

- `Source/GroundBlastFx`: the C# plugin
- `Tests/GroundBlastFx.Tests`: unit tests (no KSP needed)
- `UnityProject`: shaders and the Unity project that builds them (Unity 2019.4.18f1)
- `GameData/GroundBlastFx`: the mod folder, with configs, translations and the compiled shaders
- `Tools`: build and packaging scripts

## Building

1. Install the .NET SDK. Set `KSP_ROOT` to your KSP 1.12 folder (or pass `-KspRoot`).
2. Build the plugin and run the tests: `powershell -ExecutionPolicy Bypass -File Tools/build_dll.ps1 -Configuration Release -RunTests`
3. The compiled shaders are included. If you change them, open the project in Unity 2019.4.18f1 and run `Tools/build_bundles.ps1`.
4. Make the zip: `Tools/package.ps1`
