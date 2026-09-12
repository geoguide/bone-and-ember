# Bone & Ember

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
- Function keys on a Mac are media keys by default: in game it's **Fn+F1** for Configuration Manager and **Fn+F7** for UnityExplorer. Say "Fn+" when giving Geo hotkeys.
- UnityExplorer opens on startup and grabs the keyboard (text fields stop working) until it's hidden with Fn+F7.
- Don't use `[BepInProcess("valheim.exe")]`. The process isn't called that on Mac and the plugin would silently not load.

## Loop

1. `dotnet build` compiles and copies `BoneAndEmber.dll` into `BepInEx/plugins/BoneAndEmber/` automatically.
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

## Current design

Specs live in `docs/design/`. Read the one you're working on before touching code, and `001-notes-vanilla-hud.md` for how vanilla's HUD works.

- `001-always-on-hud.md`: **shipped through slice 8** (health with food segments, stamina, status chips, low health state, loud moments, polish pass). Follow-ups may still be open in chat.
- `002-waypoint-strip.md`: compass strip with death markers and corpse recovery tracking. In progress.
- `003-inventory.md`: inventory overhaul plan. Research only so far.

Design decisions are Geo's. Claude Code implements the spec as written and raises questions before deviating. Minimal UI is currently parked in `BepInEx/disabled/`; the coexist code stays but don't test against it.

## Dev loop extras

- `-console` is on the Steam launch options. Fn+F5 opens the console, `devcommands` unlocks vanilla cheats (`spawn`, `tod`, `env`, `god`, `heal`, `puke`, `clearstatus`, `addstatus`).
- Our own dev commands (behind the DevCommands config toggle): `bae_status <name>` toggles a status effect, `bae_food` eats three test foods.
- DevCommands also turns on extra logging, not just commands. The compass strip logs camera yaw every 2 seconds while it's on screen (`tools/log.sh compass`), which is how you tell "the math is wrong" from "nothing is being drawn".
- One Claude Code session builds at a time. Research-only sessions writing separate docs can run in parallel.
- 002 writes state to `BepInEx/config/BoneAndEmber.recovered-deaths.json` (which deaths you have collected, keyed by world and character). Delete it to make every death show as live again, which is how you re-test recovery without dying.

## Current state

- v0.1.0 hello world verified Sept 2026. HUD (001) built and tested in game through slice 8. Whole loop proven: build, deploy, launch, log.
