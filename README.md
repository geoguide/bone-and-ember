# Bone & Ember

A client-only UI overhaul for [Valheim](https://www.valheimgame.com/), built on BepInEx and Harmony.

Vanilla Valheim hides a lot of what you need to know. Max health comes from food, but the bar never says so. Repair is free and most players never find it. Items on the ground are a hover prompt you have to walk your crosshair onto, one at a time. Bone & Ember surfaces that information where you already are, in a palette that tries to look like it shipped with the game.

**Client-only.** It changes what you see and nothing else. Install it in your own game and join any server: the server does not need it, and nobody else has to install anything.

> Status: early. Version 0.1.0, developed and tested on macOS. See [Known limitations](#known-limitations).

## What it does

| Area | What changes |
|---|---|
| **HUD** | Always-on health and stamina bars with numbers. Health is segmented by the food that grants it, so you can see where your max came from. Status effects become labelled chips instead of unexplained icons. Low health gets its own state. |
| **Sense** | Stand still for a moment and nearby items and plants mark themselves, near to far. Dropped loot reads loud and names itself; plants stay quiet. Never shows anything through a wall. |
| **Compass** | A strip across the top with your death markers on it, so you can walk back to a corpse without opening the map. Recovered graves stop being tracked and dim on the minimap. |
| **Inventory** | Restyled slots and panel, a detail card with stat deltas against what you have equipped, a filter row, and a carry-weight bar. |
| **Repair** | A repair line that is always visible while the inventory is open, says what is worn and where to fix it, and repairs everything in one press. Vanilla only draws its repair button when you are already standing at a station, which is why most players never find it. |
| **Forsaken power** | The guardian power moved out of its corner widget and into the HUD. |

Everything is behind a config toggle, so you can turn any of it off and A/B it live in game.

## Install

1. Install [BepInEx for Valheim](https://valheim.thunderstore.io/package/denikson/BepInExPack_Valheim/) if you do not already have it.
2. Download the latest `BoneAndEmber-<version>.zip` from [Releases](../../releases).
3. Drop `BoneAndEmber.dll` into `BepInEx/plugins/`.
4. Launch the game.

Full step-by-step for Windows and macOS, from scratch or into an existing BepInEx install, is in [docs/INSTALL.md](docs/INSTALL.md).

To uninstall, delete the DLL. Nothing it does is written to your world or character.

## Configuration

Every behaviour has a toggle. Two ways to reach them:

- **In game:** [Configuration Manager](https://valheim.thunderstore.io/package/Azumatt/Official_BepInEx_ConfigurationManager/) with **F1**.
- **In a text editor:** `BepInEx/config/com.geo.boneandember.cfg`. The mod watches that file and applies a save immediately, no restart. Turn that off with `General.LiveConfigReload`.

Sections: `Layout`, `Font`, `Motion`, `Compass`, `Inventory`, `Radial`, `Sense`, `Power`, `Dev`.

Configuration Manager draws with Unity's old IMGUI at a fixed pixel size and has no scale setting, so on a high-DPI display it is close to unreadable. Editing the file is usually nicer.

## Known limitations

- Developed and tested on **macOS** (Apple Silicon, via Rosetta) against a single-player world. It should work on Windows, but it has not been tested there.
- Tested alongside [Minimal UI](https://valheim.thunderstore.io/package/Azumatt/Minimal_UI/) early on, but not recently. There is coexistence code for it that is currently unverified.
- Version 0.1.0. Expect rough edges, and please open an issue if you find one.

## Building from source

You need the [.NET SDK](https://dotnet.microsoft.com/download) and a local Valheim install. The project compiles against the game's own assemblies, which are **not** redistributed here.

```bash
git clone https://github.com/geoguide/bone-and-ember.git
cd bone-and-ember
tools/setup-dev.sh    # writes local.props with your Valheim paths
dotnet build          # compiles and copies the DLL into BepInEx/plugins/
```

`tools/setup-dev.sh` is macOS-oriented. On Windows, create `local.props` by hand:

```xml
<Project>
  <PropertyGroup>
    <ValheimDir>C:\Program Files (x86)\Steam\steamapps\common\Valheim</ValheimDir>
    <ManagedDir>$(ValheimDir)\valheim_Data\Managed</ManagedDir>
  </PropertyGroup>
</Project>
```

| Command | What it does |
|---|---|
| `dotnet build` | Compile and copy the mod into the game |
| `tools/package.sh` | Build Release and zip a GitHub release artifact into `dist/` |
| `tools/package-thunderstore.sh` | Build Release and zip a Thunderstore package into `dist/` |
| `tools/log.sh [pattern]` | Search the BepInEx log |
| `tools/decompile.sh` | Decompile the game locally for reference |

### Releasing

The two package scripts produce different shapes on purpose. `package.sh` makes
a plain zip holding the DLL and the install guide, for attaching to a GitHub
release. `package-thunderstore.sh` makes the layout Thunderstore requires:
`manifest.json`, `icon.png` and `README.md` at the root, with the DLL under
`plugins/`.

Bump `ModVersion` in `src/Plugin.cs` and `version_number` in
`thunderstore/manifest.json` together. The Thunderstore script refuses to build
if they disagree, because a version number there is permanent and cannot be
reused even after a deletion.

### A note on the decompiled game code

`tools/decompile.sh` writes Iron Gate's own code into `reference/decompiled/` so it can be read while working. That directory is gitignored and must never be committed, redistributed, or pasted into issues or docs. It exists locally, for reading, and it stays there.

## How this is built

Design decisions are written down before they are coded. Each feature has a spec in [`docs/design/`](docs/design/) covering the moment in play that is broken, what it should feel like, the states it has to handle, and what was learned once it was tested in game. If you want to understand why something works the way it does, start there.

## Contributing

Issues and pull requests are welcome. Useful things to include in a bug report:

- Your OS, Valheim version, and Bone & Ember version
- The relevant part of `BepInEx/LogOutput.log`
- Other mods you have installed

Please do not include decompiled game code in issues or pull requests.

## License

[MIT](LICENSE). Bundle it in a modpack if you like.

Not affiliated with Iron Gate or Coffee Stain. Valheim is their trademark.
