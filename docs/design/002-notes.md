# 002 notes: death pins, tombstone recovery, map selection, facing

Research for slice 1 of `002-waypoint-strip.md`. Read from
`reference/decompiled/assembly_valheim/`, `assembly_utils/`, and `minimalui/`.
Line numbers are from that decompile and will drift after a Valheim update, so
re-run `tools/decompile.sh` and re-grep the method name rather than trusting a
number.

Nothing here is Iron Gate's code. It is a description of it.

## Two findings that changed the spec

**Vanilla has no concept of a selected pin.** Double-click on the map is already
bound to "create a pin here". There is nothing to hook for the tracked pin, so
slice 5 is cut from 002 and deferred to the map screen pass. Details below.

**Deleting a death pin edits the character save.** Pins live in the `.fch`
player profile, not in a mod file. So recovered pins now dim by default and
`RemoveRecoveredPins` is an opt-in config, off by default.

## Death pins

Created in exactly one place, `Player.OnDeath()` at `Player.cs:3452`:

    Minimap.instance.AddPin(transform.position, Minimap.PinType.Death,
        $"$hud_mapday {EnvMan.instance.GetDay(...)}", save: true,
        isChecked: false, 0L);

- Position is the player's `transform.position` at the instant of death.
- The name is a localization token plus the day number, e.g. `"$hud_mapday 42"`.
  There is no timestamp on a pin. Position plus day is the whole identity we get,
  which is enough: two deaths in the same spot on the same day are the same
  recovery trip anyway.
- `PinType.Death` is enum value 4 (`Minimap.cs:24`).

`Minimap.PinData` (`Minimap.cs:46`) carries `m_name`, `m_type`, `m_pos`,
`m_save`, `m_checked`, `m_ownerID`, plus the live UI refs `m_uiElement` and
`m_iconElement`. Dimming a pin on the map means writing `m_iconElement.color`,
nothing more.

Pins live in `private List<PinData> m_pins` (`Minimap.cs:312`). We publicize the
game assemblies, so we can enumerate it directly with no allocation and no
reflection.

Existing APIs worth reusing, all on `Minimap`:

- `AddPin(...)` at 2326
- `RemovePin(PinData)` at 2268, `RemovePin(Vector3 pos, float radius)` at 2221
- `GetClosestPin(Vector3 pos, float radius, bool mustBeVisible)` at 2249
- `GetSprite(PinType)` at 2364, which hands back the vanilla skull sprite. That
  is the one to clone for the strip marker, per the "clone vanilla elements" rule.

### Where pins are stored

`Minimap.GetMapData()` at `Minimap.cs:2128` walks `m_pins`, writes every pin with
`m_save` into a `ZPackage` (name, pos, type, checked, ownerID, author), and
`SaveMapData()` hands that to `Game.instance.GetPlayerProfile().SetMapData(...)`.

That means pins are **per character, not per world**, and they are written to the
character's `.fch` file. Anything we delete is deleted for good the next time the
profile saves. That is why dimming is now the default.

## Tombstone recovery

`TombStone.cs`. `Awake` wires the container callback:

    m_container.m_onTakeAllSuccess += OnTakeAllSuccess;

**`TombStone.OnTakeAllSuccess()` is the hook.** It is private, runs only on the
client that emptied the grave, and is already where vanilla shows
`$piece_tombstone_recovered`. A Harmony postfix there gives us:

- `__instance.transform.position`, the grave's current position.
- `__instance.m_nview.GetZDO().GetVec3(ZDOVars.s_spawnPoint)`, the position it
  was created at before any drift. Closer to the death pin, so prefer it.
- `__instance.IsOwner()`, which compares the grave's `s_owner` ZDO long against
  `Game.instance.GetPlayerProfile().GetPlayerID()`. Gate on this so looting a
  friend's grave never marks one of our deaths recovered.

### Two gaps in that hook

**Drag-out looting.** `OnTakeAllSuccess` fires on take-all, including the
auto-loot-all path inside `Interact` when the contents fit in your inventory. It
does not fire if you drag items out one at a time. Backstop:
`TombStone.UpdateDespawn()` runs on a 2 second `InvokeRepeating` and destroys the
grave once it is empty and not in use. A postfix there, gated on `IsOwner()` and
an empty inventory, catches that case.

**Deaths with no grave.** `Player.CreateTombStone()` at `Player.cs:3289` returns
early when the inventory is empty or the `DeathKeepInventory` global key is set.
Those deaths still get a map pin but never produce a tombstone, so no hook can
ever fire for them. They can only be cleared by the proximity fallback the spec
already calls for.

### Matching a grave to a pin

The grave spawns at `GetCenterPoint()`, roughly the death position but about a
metre up. `PositionCheck()` lets it drift up to 4 m from its spawn point and it
can float in water. So match on XZ distance with a generous radius. 10 m, the
same number the spec uses for the proximity fallback, covers it.

`Utils.DistanceXZ(a, b)` is the helper `Minimap` itself uses everywhere.

## Map pin selection: not a thing

This is the part of the spec that does not survive contact with the code.

- `Minimap.OnMapLeftUp` at 2404 detects a double-click and calls `OnMapDblClick()`
  at 2429. That either sends a ping or opens the **new pin name input**. It never
  selects anything.
- `Minimap.OnMapLeftClick()` at 2449 finds the closest pin to the cursor and
  toggles `m_checked` (or clears `m_ownerID` on a shared pin). That checkmark is
  the only per-pin state vanilla has, and it means "dealt with", not "tracked".
- `m_selectedType` at 278 is the currently selected pin **type** in the icon
  toolbar. Not a selected pin. The name is misleading.
- Controller, `Minimap.cs:998` to `1020`: `JoyButtonA` creates a pin,
  `JoyTabRight` deletes the pin under screen center, `JoyTabLeft` toggles its
  checkmark, `JoyDPadUp`/`Down` cycle pin type, `JoyDPadRight` toggles the type
  filter. Every button on the map screen is already spoken for.

So there is no existing gesture to read and no free controller button. Building a
tracked pin means inventing a gesture (shift-click, say) and finding a controller
binding for it. Deferred to the map screen pass, where that decision belongs.

## Facing direction

Vanilla's own player marker reads the **camera**, not the player:

    // Minimap.cs:1178
    UpdatePlayerMarker(player, Utils.GetMainCamera().transform.rotation);

and `UpdatePlayerMarker` at 1713 uses `-eulerAngles.y` for the heading. Camera is
the right source for us too: the strip should agree with what is on screen, not
with which way the body happens to be facing.

`Utils.GetMainCamera()` (`assembly_utils/Utils.cs:288`) caches `Camera.main` per
frame, so calling it every `LateUpdate` costs nothing.

Axes: `WorldToMapPoint` at 1799 maps world `+Z` to map up and `+X` to map right,
so **+Z is north and +X is east**, plain Unity. Bearing to a target:

    Vector3 d = target - playerPos;
    float bearing = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;   // 0 = N, 90 = E
    float rel = Mathf.DeltaAngle(cameraYaw, bearing);        // -180..180

## HUD plumbing

`Hud.m_rootObject` is moved off screen by `Hud.SetVisible` at `Hud.cs:436`, but
that only ever fires for cutscenes and the Ctrl+F3 user hide (`Hud.cs:524`).
Parenting under it, the way `AlwaysOnHud` does, gets those two states free.

Map, inventory, menu and death are **not** covered, so the strip checks them
itself:

- `Minimap.IsOpen()` at 482
- `InventoryGui.IsVisible()` at 1015
- `Menu.IsVisible()` at 291
- `Player.m_localPlayer` null, or `IsDead()`

Minimal UI touches the minimap (`Mappatcher`, `RotationPatch`, `MUIMap`) but only
to reskin it, move it and optionally stop it rotating. It never reads or writes
pin data, and it puts nothing at top center. No conflict with 002.

Reuse from 001: `Palette` (Bone for the line and ticks, EmberHealth for the skull),
`HudFont.Apply`, `HudPath.Of` for log lines, and the "clone a vanilla `TMP_Text`
as a font source" pattern from `BarHeader.MakeText` and `LoudMoments`. Font source
is `MessageHud.instance.m_messageCenterText`, falling back to
`AlwaysOnHud.Instance.HealthTextForFont` when MessageHud has not woken yet.

For the recovery state file: both `UnityEngine.JSONSerializeModule.dll` and
`Newtonsoft.Json.dll` ship in the game's Managed folder. `JsonUtility` is the
lighter option and needs one more `<Reference>` line in `BoneAndEmber.csproj`. Key
the file by world and character, since pins are per profile:
`ZNet.instance.GetWorldUID()` (`ZNet.cs:2081`) and
`Game.instance.GetPlayerProfile().GetPlayerID()` (`PlayerProfile.cs:741`).

## Proposal for slice 2: the compass line

Smallest thing testable in game. A strip with N, E, S and W that stays correct as
you turn. No markers, no pins, no recovery.

**Files**

- `src/Hud/CompassStrip.cs`, builds and drives the strip.
- `src/Patches/CompassStripPatch.cs`, a `Hud.Awake` postfix that does
  `AddComponent<CompassStrip>()`. Separate from `HudAwakePatch` so 002 can be
  toggled and debugged without touching 001. Two postfixes on the same method is
  fine.

**Hierarchy**, under `Hud.m_rootObject`, anchored top center, pivot (0.5, 1):

    BoneAndEmber_CompassStrip
      Line            Image, ~2 px, Bone at ~35% alpha, width = StripWidth
      Cardinal_N/E/S/W  a CanvasGroup holding a Tick Image and a Label

Four ticks, built once in `Awake`, repositioned in `LateUpdate`. Nothing is
instantiated per frame.

**Per frame**

    float yaw = Utils.GetMainCamera().transform.eulerAngles.y;
    float rel = Mathf.DeltaAngle(yaw, cardinalBearing);
    float x   = rel / (StripFovDegrees * 0.5f) * (StripWidth * 0.5f);

`StripFovDegrees` is a config value, default 120, and deliberately **not** the
camera's real field of view. The strip is about a third of the screen wide, so a
marker could never sit over the actual thing on screen no matter what we pick.
A fixed angular span reads like a game compass and is cheap to A/B live. This is
a feel decision, not a correctness one, so it wants testing in game.

Ticks past `±StripFovDegrees / 2` hide. Alpha ramps down over the outer ~15% of
the width so labels fade out rather than pop.

**Config**: `WaypointStripEnabled`, `StripWidth` (default about 0.33 of screen
width), `StripOffsetY`, `StripFovDegrees`, `AlwaysShowCompass`.

**Visibility gate** in `LateUpdate`: hidden when the config is off, when the map,
inventory or menu is open, or when there is no live player. Everything
null-guarded.

**Logging**: one line on build with `HudPath.Of(root)`, plus a throttled heading
line behind the existing `DevCommands` flag so `tools/log.sh` can confirm the yaw
is sane without spamming.

**Open, needs UnityExplorer (Fn+F7)**: where vanilla's center messages actually
sit, which sets the default `StripOffsetY`.

## Tested in game

Not yet. Slice 1 is documents only.
