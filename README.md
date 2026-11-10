# Crafting Speed Mod

Limit the crafting speed of individual assemblers, from 0.1% to 100%. Handy for building-supermarket
lines where some buildings should be produced slowly, so every product line can run at the same time.

* Open an assembler: a "Speed limit" row sits above your inventory.
  * Drag the slider, use the `-` / `+` buttons or the mouse wheel over the row: steps of 1%, hold Shift for 10%, Ctrl for 0.1%.
  * `Reset` goes back to 100%.
  * Or type a **target output per minute** and the limit is worked out for you (0.1% precision).
  * The window's own "per minute" readout shows the limited rate (orange when limited), and the status line says "limited to …".
* It never goes above 100%; there are other mods for that.
* The limit scales both normal crafting progress and the proliferator "extra products" progress.
* The row's position can be nudged in `BepInEx/config/com.github.jiesiluo.assemblerspeed.cfg` (`RowNudgeX`, `RowNudgeY`).
* **Blueprints keep the limits.** Copy machines into a blueprint and the pasted machines get the same limits,
  including machines that are still ghosts waiting for drones.
* The game's own copy / paste building settings also carries the limit.
* Limits are stored in the save through DSPModSave. Saves from 1.0.x load as before.

## Compatibility

* Requires [BlueprintTweaks](https://thunderstore.io/c/dyson-sphere-program/p/kremnev8/BlueprintTweaks/) for
  storing limits in blueprints. The data is appended after the game's own blueprint data, so these blueprints
  still paste in an unmodded game (at full speed), and older blueprints paste exactly as before.
* Only assemblers (recipe type "Assemble") show the slider.

## Building

The project references the game and the r2modman profile directly. Defaults are in `Directory.Build.props`;
override them if your paths differ:

```
dotnet build CraftingSpeed/CraftingSpeed.csproj -c Release -p:DSPGameDir="..." -p:BepInExDir="..."
dotnet build CraftingSpeedPreloader/CraftingSpeedPreloader.csproj -c Release
```

Package layout: `plugins/CraftingSpeed.dll`, `patchers/CraftingSpeedPreloader.dll`, plus `manifest.json`,
`README.md`, `CHANGELOG.md` and `icon.png`.
