# GroundBlastFx 1.0.1

Volumetric ground effects for rocket engines in Kerbal Space Program 1.12: dust, launch pad steam, water spray, vacuum
ejecta, scorch marks and flame light, all driven by the real thrust, height, surface, atmosphere and propellant of each
engine.

## Requirements

- Kerbal Space Program 1.12.x
- **Windows with Direct3D 11** (the default renderer). OpenGL, Metal, macOS and Linux are not supported: on those, the
  mod stays inactive.
- A GPU with compute shader support. No other mod is required.

## Installation

Copy the `GroundBlastFx` folder into `Kerbal Space Program/GameData`. To update, delete the old `GroundBlastFx` folder
first (your settings live in `PluginData/Settings.cfg`; keep that file if you want them).

## In game

- Click the **GroundBlastFx** button in the flight toolbar to open the settings window.
- **Effect density**: amount of dust and steam (it does not change render detail).
- **Visible range**: how far away clouds are drawn.
- KSP's game settings (Difficulty options) have a **GroundBlastFx** tab with the same two settings, plus each effect on
  or off, keeping marks in the save, and hiding stock ground dust.
- The window follows the game language: English, French, German, Spanish, Italian, Portuguese, Russian, Simplified
  Chinese, Japanese.
- Stock ground dust is hidden while the mod runs, so the two do not overlap.
- Scorch marks are saved with your game and are still there when you come back to a site.

## Features

- Simulated 3D clouds (on the GPU) anchored to the ground: they stay where they were made, drift with the wind, follow
  the terrain and dissipate over time. Several engines close together make one cloud.
- Launch pads: steam jets out of the real flame-trench outlets of the pad, grows with thrust, and billows into big
  clouds.
- Dust takes the color of the ground actually on screen (works with or without Parallax), with palettes per body and
  biome for stock planets. Snow and ice blow white; thin air (Duna) gives a low cloud and a fast sheet of grains.
- Water: spray cloud and droplets. Airless bodies: a thin, streaked ejecta sheet that stops as soon as the engine cuts,
  and a blast mark left on the ground.
- Scorch marks depend on the ground (scorched grass, darkened soil or concrete) and on the propellant (kerolox soot,
  cleaner methalox and hydrolox).
- Cloud shadows on the ground and on the vessel; flame light on the clouds by day and by night.

## Compatibility

Used during development alongside Waterfall, Scatterer, EVE, Parallax Continued, Kopernicus, TUFX, Deferred, SmokeScreen,
RealPlume and Firefly. Other mods that draw their own engine dust on the ground (for example KerbalFX ImpactPuffs or
PlumeDynamics) will overlap with this one; disable one of them. Kerbal Konstructs launch pads with declared smoke outlets
are supported but have been tested less. RSS/RP-1 has not been tested yet.

## Performance

Clouds are rendered at full detail on every machine. On a recent GPU the cost is a few milliseconds per frame near a
large launch; older or entry-level GPUs will notice it.

## Problems

Please attach `KSP.log`, `GameData/GroundBlastFx/PluginData/GroundBlastFx.log` and a screenshot.

## License

MIT (see `LICENSE`), by Warnix. Developed with the help of AI coding assistants; see `CREDITS.md` for the published techniques it is
based on.

---

**Français** : effets au sol volumétriques des moteurs-fusées pour KSP 1.12 (Windows, Direct3D 11). Copier le dossier
`GroundBlastFx` dans `GameData`. En vol, le bouton GroundBlastFx ouvre la fenêtre (densité des effets, portée visible) ; les mêmes réglages sont dans les paramètres de la partie de KSP, onglet GroundBlastFx ;
la langue suit celle du jeu. Journal : `PluginData/GroundBlastFx.log`. Licence MIT.
