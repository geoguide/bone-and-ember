# Installing Bone & Ember

A UI overhaul for Valheim. **Client-only**: it changes what you see, nothing else. You can join any server with it, the server does not need it, and nobody else has to install anything.

You need two things: **BepInEx** (the mod loader Valheim mods run on) and **BoneAndEmber.dll** (this mod).

---

## If you already mod Valheim

You have BepInEx. Drop `BoneAndEmber.dll` into:

```
<Valheim folder>/BepInEx/plugins/
```

Start the game. Done.

Using r2modman or Thunderstore Mod Manager? Use **Settings > Import local mod**, or drop the DLL into that profile's `BepInEx/plugins` folder.

---

## Windows, from scratch

1. **Install BepInEx.** Get the *BepInEx pack for Valheim* from Thunderstore (thunderstore.io, search "BepInExPack Valheim"). Download it and unzip.
2. Inside the unzipped download there is a folder whose contents are `BepInEx`, `doorstop_config.ini`, `winhttp.dll` and so on. Copy **those contents** into your Valheim folder, next to `valheim.exe`.
   - To find that folder: Steam > right-click Valheim > Manage > Browse local files.
3. **Run Valheim once** and quit. This makes BepInEx generate its folders.
4. Put `BoneAndEmber.dll` into `Valheim/BepInEx/plugins/`.
5. Start Valheim. You should see the new HUD as soon as you load a world.

## Mac, from scratch

Valheim on Mac is an Apple Silicon build, and BepInEx only runs under Rosetta, so there is one extra step Windows does not have.

1. Install the BepInEx pack for Valheim the same way as above, into your Valheim folder. On Mac that folder is usually:
   ```
   ~/Library/Application Support/Steam/steamapps/common/Valheim
   ```
   Steam > right-click Valheim > Manage > Browse local files gets you there.
2. **Set the launch options.** Steam > right-click Valheim > Properties > General > Launch Options, and paste exactly:
   ```
   /usr/bin/arch -x86_64 /bin/bash ./start_game_bepinex.sh %command%
   ```
   Without this, the game launches normally and no mods load at all. If the mod "does nothing", check this first.
3. Run Valheim once and quit, so BepInEx creates its folders.
4. Put `BoneAndEmber.dll` into `Valheim/BepInEx/plugins/`.
5. Start Valheim from Steam.

---

## Did it work?

Load a single-player world. The health and stamina bars should be in the bottom left in a new style, with a compass strip across the top.

If nothing changed, check the log:

- Windows: `Valheim/BepInEx/LogOutput.log`
- Mac: same path inside the Valheim folder

Search it for `BoneAndEmber`. A line like `BoneAndEmber 0.1.0 loaded` means the mod is in and running. No such line means BepInEx is not loading it (on Mac, that is almost always the launch options in step 2).

## The font

The HUD is designed in **Chakra Petch**. If that font is not installed on your machine, the mod quietly uses Valheim's own font instead and everything still works, it just looks a bit different from the screenshots.

To get the intended look: download Chakra Petch (free, fonts.google.com), install it, and restart the game. On Windows, right-click the `.ttf` files and choose Install. You can point the mod at any installed font instead by changing `Font.FontName` in the config.

The log says which one won: search `LogOutput.log` for `font:`.

## Changing settings

Everything is configurable and most of it can be toggled while the game is running.

Install **Configuration Manager** (Thunderstore, "BepInEx ConfigurationManager") and press **F1** in game to get a settings panel. On a Mac laptop that is **Fn+F1**.

Settings are also a plain text file, created after the first run:

```
Valheim/BepInEx/config/com.geo.boneandember.cfg
```

Notable ones:

- `Motion.Enabled` — turns all animation off, if you prefer everything instant.
- `HUD.Enabled` — turns the custom health/stamina HUD off and gives vanilla's back.
- `Inventory.Enabled` — same for the inventory restyle.
- `Compass.WaypointStripEnabled` — the compass strip and death markers.

## Uninstalling

Delete `BoneAndEmber.dll` from `BepInEx/plugins/`. Nothing else is touched. The mod never changes your world or character save.

One exception worth knowing: if you turn on `Compass.RemoveRecoveredPins` (off by default), it deletes death pins from your map as you collect graves, and map pins **are** saved in your character file. That one is not undoable. Everything else is display only.

## Known rough edges

- Built and tested on macOS. It should be identical on Windows, but Windows has had less testing.
- Tested against Valheim as of September 2026. A game update can break a mod; if something looks wrong after Valheim patches, that is the likely cause.
