# Changelog

All notable changes to Bone & Ember are recorded here.
This project uses [semantic versioning](https://semver.org/).

## 0.1.1

### Changed

- Sense markers now fade in over 260ms instead of 120ms. The old value was
  about seven frames, and the easing curve is weighted toward the start, so
  markers reached most of their brightness almost immediately and read as
  popping in rather than fading.

### Added

- Thunderstore packaging: `thunderstore/manifest.json`, an icon, and
  `tools/package-thunderstore.sh`. The script refuses to build when the code
  version and the manifest version disagree, because a Thunderstore version
  number is permanent and cannot be reused even after deleting the package.
- `CHANGELOG.md`.

## 0.1.0

First public release.

### Added

- **HUD.** Always-on health and stamina bars with numbers. Health is segmented
  by the food granting it, so you can see where your maximum came from. Status
  effects become labelled chips instead of unexplained icons, plus a low-health
  state.
- **Sense.** Stand still for a moment and nearby items and plants mark
  themselves, near to far. Dropped loot reads at full strength and names itself
  up close; plants and rocks stay quiet. Occlusion-checked, so nothing shows
  through a wall.
- **Compass.** Death markers with distance along the top of the screen, plus
  corpse recovery tracking, so a grave you have already collected stops being
  tracked and dims on the minimap.
- **Inventory.** Restyled slots and panel, a detail card with stat deltas
  against what you have equipped, a filter row, a carry-weight bar, hotbar
  hints, and a restyled radial menu.
- **Repair.** A repair line that stays visible while the inventory is open,
  names what is worn, says where it can be fixed, and repairs everything in one
  press. Vanilla only draws its repair button when you are already standing at
  a station, which is why most players never find it.
- **Forsaken power.** Moved out of its corner widget and into the HUD.
