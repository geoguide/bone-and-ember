# 001: Always-on HUD

## The moment
The health display and the icons around it are confusing. It's hard to tell how much life you have, what the small icons mean, and whether a status like Wet matters right now. Part of the cause: max health comes from food, and vanilla only shows that as small icons around the bar.

## What it should feel like
Modern and easy to read at a glance, in the spirit of Returnal and Stellar Blade, with Persona-style boldness saved for the moments that need to shout. Calm until you need to act.

## Direction (decided)
"B's colors with A's type, somewhere in between." Reference mockup: https://claude.ai/code/artifact/a436291e-a0d5-4bfe-9dd9-b66cd10cf208

- **Palette (from B):** bone `#EDE3D1` for text and trails, ember red `#D9573B` health, brighter `#FF5A36` when critical, gold `#E9B949` stamina, ember `#E0793A` outline for bad effects.
- **Type (from A):** squared, technical face (the mockup used Chakra Petch). In game this means a TextMeshPro font asset, see open questions.
- **Edges:** in between. Clean clipped corners from A, with a subtle angle on segment dividers from B. Mockup the in-between version before building.

## Structure
- **Health:** thin horizontal bar, bottom-left. Faint dividers mark base health and each active food's share of max. A food about to expire pulses its segment, with a small caption ("Queens jam wears off 0:03"). When it expires, the bar shrinks visibly.
- **Damage trail:** on a hit the fill drops instantly, and a light trail holds the lost chunk for about 0.45s, then drains.
- **Stamina:** thinner bar directly under health. Hidden when full, fades in while spending, fades out about 1s after refilling.
- **Status chips:** sit just above the bars. Icon, one word, timer. Bad effects first with an ember outline, good ones quieter.
- **Low health:** bar goes brighter red, soft red vignette at the screen edges.
- **Loud moments:** Freezing (and later: about to die, encumbered?) gets a bold angled slab near the top for about 2.5s, then only the chip remains.

## Scale
The mockup is oversized so it reads on a phone. In game the cluster should be roughly 20 to 25% of screen width at 1440p, sized from the vanilla HUD's own scale. Tune in game with a config value.

## States
- Main menu, loading, dead: HUD hidden (null-guard Hud / Player).
- No food eaten: only the base segment.
- Eitr (magic): later slice, same pattern as stamina.
- Controller vs mouse: no interaction needed, display only.
- With Minimal UI installed: check it doesn't also restyle the health/food area.

## Config (Configuration Manager, F1)
- Enable new HUD (fall back to vanilla when off)
- HUD scale
- Show numbers on bars
- Show food segments
- Low health vignette on/off

## Build slices
1. Read vanilla: find how `Hud` builds and updates the health, stamina, food, and status effect UI (decompiled code + UnityExplorer).
2. Hide vanilla health/stamina and draw a plain new health bar with numbers. Prove it updates.
3. Food segments + shrink on expiry.
4. Damage trail, low health state.
5. Stamina bar with auto-hide.
6. Status chips (replace or restyle vanilla status effect icons).
7. Loud moment for Freezing.
8. Fonts and final polish.

## Open questions
- Custom font: bundling a TMP font asset needs a Unity AssetBundle (so the Unity Editor). Until then, use the closest font Valheim already ships.
- Where vanilla keeps food timers and the status effect list (likely on `Player` and `SEMan`), confirm in decompiled code.

## Tested in game
Not yet.
