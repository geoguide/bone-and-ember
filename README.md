# ValheimUI

A client-only Valheim UI overhaul, built with BepInEx + Harmony. Developed on macOS (Apple Silicon).

## First-time setup (Mac)

1. Quit Valheim.
2. `tools/setup.sh` installs the mod loader and test mods into the game (BepInEx, Configuration Manager, Minimal UI, UnityExplorer), installs the dev tools, decompiles the game for reference, and does a test build.
3. Paste the launch line it copies to your clipboard into Steam: Valheim > Properties > General > Launch Options.
4. Launch Valheim from Steam and load a single-player world.

## Day to day

| Command | What it does |
|---|---|
| `dotnet build` | Compile and copy the mod into the game |
| `tools/launch.sh` | Start Valheim via Steam and follow the mod log |
| `tools/log.sh [pattern]` | Search the BepInEx log |
| `tools/decompile.sh` | Refresh the decompiled game code after a Valheim update |
| `tools/install-game-mods.sh` | Update BepInEx and the helper mods |

In game: **F1** Configuration Manager, **F7** UnityExplorer.

## Playing on a server

The mod is client-only: install it in your own game and join any server. The server and other players don't need it.
