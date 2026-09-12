# 002: Waypoint strip and corpse recovery

## The moment
Geo died, hiked back to the skull on the map, and found nothing. Either he had already collected it or he was in the wrong spot. Vanilla keeps every death marker forever and gives no way to tell a recovered corpse from a live one, and the only way to steer toward it is to keep opening the map.

## What it should feel like
Like an objective marker in a modern action game. You know where the body is and how far without breaking stride. When it's done, it's gone.

## Decision on scope (Geo deferred to a gamer's call)
- The strip shows **death markers** only. That's how most games do it: the world is quiet, you chase the one thing that matters. Everything else stays on the minimap.
- Death markers are always tracked until recovered.
- A user-tracked pin was the original plan and is **cut**. Research found vanilla has no concept of a selected pin: double-click on the map is already "create a pin here", single click toggles a pin's checkmark, and every controller button on the map screen is taken. Adding tracking means inventing a gesture and a binding, which belongs in the map screen pass, not here. See `002-notes.md`.

## Structure
- **Compass strip** along the top center: a thin bone line about a third of the screen wide, with N/E/S/W ticks in small caps. Markers slide along it as you turn. A marker off the strip's ends clamps to the edge with a small chevron.
- **Death marker**: skull glyph in ember with distance under it ("142 m"). Under 20 m the distance disappears and the skull grows slightly, so you look up from the HUD.
- Vertical offset from the top edge is configurable; default clears vanilla's center messages.

## Corpse recovery
- When the local player takes their tombstone (hook the client-side tombstone interaction), record that death as recovered in a small JSON file in BepInEx/config, keyed by world and character since map pins live in the character profile.
- Recovered deaths: strip marker disappears, and the minimap death pin is **dimmed** by default. Removing the pin outright is a config option (`RemoveRecoveredPins`), off by default, because pins are saved into the character's `.fch` profile and a deletion can't be undone.
- Deaths from before the mod was installed can't be classified. They show as live until visited. Visiting within 10 m with no tombstone present marks them recovered too.

## States
- No deaths: strip hidden entirely (config: AlwaysShowCompass to keep just the N/E/S/W line).
- Map open, inventory open, dead, main menu: strip hidden.
- Multiple deaths: all shown, nearest one brightest.
- Marker overlap: nearer marker draws on top, distances stagger.

## Config
WaypointStripEnabled, RemoveRecoveredPins (default off, dim instead), StripWidth, StripOffsetY, StripFovDegrees, AlwaysShowCompass.

## Build slices
1. Research: how death pins are created and stored (Minimap pins, PinType.Death), tombstone pickup hook, how the map's pin selection works. **Done, see `002-notes.md`.**
2. Compass line with N/E/S/W only, correct as you turn.
3. Death markers with distance.
4. Recovery tracking and pin dimming.
5. Polish, same palette and font as 001.

## Out of scope
Map screen changes, including any way to track an arbitrary pin. Geo wants to work on the map later, separately.

## Tested in game
2026-09-10 (during 003 slice 9): Geo found a grave by wandering that the strip no longer pointed at and the map showed dimmed. Two causes, both ours:
- The "visited, no grave present" fallback only looked for an owned grave within 10 m of the pin and could fire before the zone's objects were in. It now requires `ZNetScene.IsAreaReady` and accepts an owned grave anywhere within 40 m, and logs what it found before marking.
- Recovery state never persisted: `JsonUtility` wrote `{}` for the private nested classes, so every launch started with zero recovered deaths. Classes are public now and the save logs its entry count.
Vanilla itself never hides or removes a death pin; only the collected grave despawns.
