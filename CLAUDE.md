# ValheimUI

A client-only Valheim mod that overhauls the game's UI. Built by Geo (product designer + engineer, new to C# and Valheim modding, fluent in TypeScript/React). Explain C#, Unity, and Harmony concepts briefly inline the first time they come up.

## Stack

- **BepInEx 5** loads the mod. **Harmony** (bundled with BepInEx) patches game methods at runtime.
- **C# targeting net462**, built with the .NET SDK on macOS. Game code is referenced from the install and publicized (private members made accessible at compile time).
- **Unity uGUI + TextMeshPro** is how Valheim draws its UI. We modify and extend it, we don't replace the renderer.
- No Jotunn for now. Add it only if we need its GUIManager helpers, and understand it then becomes a dependency every player must install.

## Machine

- macOS on Apple Silicon. Valheim is the native Mac build from Steam.
- BepInEx only runs under Rosetta. Steam launch options must be: `/usr/bin/arch -x86_64 /bin/bash ./start_game_bepinex.sh %command%`. If the mod "does nothing", check this first.
- Game folder: `~/Library/Application Support/Steam/steamapps/common/Valheim` (real paths are in `local.props`, written by `tools/setup-dev.sh`).
- Game code lives in `valheim.app/Contents/Resources/Data/Managed/`, not `valheim_Data/Managed/` like Windows guides say.
- Don't use `[BepInProcess("valheim.exe")]`. The process isn't called that on Mac and the plugin would silently not load.

## Loop

1. `dotnet build` compiles and copies `ValheimUI.dll` into `BepInEx/plugins/ValheimUI/` automatically.
2. Ask Geo before launching the game. `tools/launch.sh` starts it via Steam and follows the log.
3. `tools/log.sh [pattern]` greps `BepInEx/LogOutput.log`. Every change should log something you can check.
4. Geo tests in a **single-player world**. The real target is a friend's dedicated server he can't access, so nothing may depend on server-side install.

## Reading the game's code

- `tools/decompile.sh` decompiles the game into `reference/decompiled/`. Re-run after every Valheim update.
- **Always read the vanilla class before patching it.** Grep `reference/decompiled/assembly_valheim/` for the method, check its signature and when it's called. Don't guess at Valheim APIs from memory.
- Core UI classes to start with: `Hud`, `InventoryGui`, `InventoryGrid`, `Minimap`, `MessageHud`, `Chat`, `StoreGui`, `Menu`, `EnemyHud`, `KeyHints`.
- `reference/decompiled/` is Iron Gate's code. Never commit it, paste large chunks of it into docs, or ship it.
- For live inspection, UnityExplorer (F7 in game) shows the actual UI hierarchy: object names, RectTransforms, components. Ask Geo to check paths there when the decompiled code isn't enough.

## Rules

- **Client-only.** No RPCs, no ZDO writes, no changes to game data that other players or the server would see. Stay purely presentational plus local input.
- **Coexist with Minimal UI** (Azumatt), which Geo has installed. It reskins and moves panels. Before touching a panel, check whether Minimal UI also modifies it, and don't fight it over the same RectTransforms.
- **Postfix over prefix.** Use a prefix that returns false (skips the original method) only when there's no other way, and say why in a comment.
- **Null-guard everything UI.** `Hud.instance`, `InventoryGui.instance`, `Player.m_localPlayer` are null on the main menu and during loading.
- **Clone vanilla elements** (fonts, sprites, button prefabs) instead of building from scratch, so new UI looks native.
- **Every behavior gets a config toggle** via `Config.Bind`, so it shows up in Configuration Manager (F1) and Geo can A/B it live.
- One patch class per file in `src/Patches/`, named after what it changes (e.g. `InventorySortButtonPatch.cs`).
- Writing style for docs, comments, commit messages, and in-game text: plain and direct, no em dashes.

## Design before code

Geo is a designer and wants to see the design before code gets written. For any new UI change:

1. Write a short spec in `docs/design/NNN-name.md` from `docs/design/_template.md`: the moment in play that's broken, what it should feel like, before/after, states (empty, full, loading, controller vs mouse).
2. Get Geo's go-ahead, then implement in the smallest slice that can be tested in game.
3. After he's tested it in game, note what changed in the spec.

## Current state

- v0.1.0: hello world. Shows "ValheimUI 0.1.0 is running" when you spawn. It has **not been compiled yet**: the first job is `dotnet build` and fixing whatever breaks (reference names, publicizer package version, the `Player.OnSpawned` / `Player.Message` signatures against the decompiled code).
