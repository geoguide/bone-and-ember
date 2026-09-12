# 001: Always-on HUD

## The moment
The health display and the icons around it are confusing. It's hard to tell how much life you have, what the small icons mean, and whether a status like Wet matters right now. Part of the cause: max health comes from food, and vanilla only shows that as small icons around the bar.

Geo is new to Valheim, so this is a newcomer's confusion, and that's the bar to design for: someone in their first hours should be able to read the HUD without a wiki.

What vanilla (with Minimal UI) actually shows, from his first session: health is a small dark vertical bar in the bottom-left corner with a red number box (31), food sits beside it as an icon with a timer ("11m") and a fork glyph, stamina is a separate yellow bar at bottom center (52), and status effects are labeled icons in the top-right corner next to the minimap (Shelter, Cold, No skill drain 9:48). Health, food, and status are in three different corners.

## What it should feel like
Modern and easy to read at a glance, in the spirit of Returnal and Stellar Blade, with Persona-style boldness saved for the moments that need to shout. Calm until you need to act.

## Direction (decided)
"B's colors with A's type, somewhere in between." Reference mockup: https://claude.ai/code/artifact/a436291e-a0d5-4bfe-9dd9-b66cd10cf208

- **Palette (from B):** bone `#EDE3D1` for text and trails, ember red `#D9573B` health, brighter `#FF5A36` when critical, gold `#E9B949` stamina, ember `#E0793A` outline for bad effects.
- **Type (from A):** squared, technical face (the mockup used Chakra Petch). In game this means a TextMeshPro font asset, see open questions.
- **Edges:** in between. Clean clipped corners from A, with a subtle angle on segment dividers from B. Mockup the in-between version before building.

## Round 2 mockup (approved)
Direction C on the mockup page is the target for slices 3 to 8. Decisions:
- Palette: bone `#EDE3D1` text and damage trail, ember `#D9573B` health (brighter `#FF5A36` when critical), gold `#E9B949` stamina, ember `#E0793A` for bad-effect chip outlines and the loud slab.
- Type: squared, technical (Chakra Petch in the mockup). Right ends of bars clipped at an angle, segment dividers leaned about 12 degrees.
- Big health number sits above the bar, left-aligned with a small HEALTH label; max is smaller and dimmer beside it.
- Status chips in one row above the bars: icon, word, timer. Bad effects first with ember outline, good ones dimmer. If the row ever crowds, the fallback is to move passive effects (Rested, Shelter, No skill drain) back to vanilla's top-right spot and keep only actionable ones on the bars.
- Food segments drain continuously and pulse once a meal is past half life; caption reads "<food> gone in m:ss" when under two minutes.
- Bar defaults: 240 by 14 for health, 240 by 8 for stamina, tune in game via config.

## Addendum: base vs food health (from in-game testing)
Tested at 30/39 with one mushroom. The notch at 25 read as a ruler mark, not a boundary, because vanilla's baked tick marks look the same and the fill is one color on both sides. Geo's confusion: "I'm past it but still see it."
Decision:
- Strip vanilla's baked tick marks from the fill and track sprites. The only marks on the bar are meal boundaries.
- The base 25 stays ember `#D9573B`. Everything food-derived is a lighter, warmer ember (start around `#E8785A`, tune in game), one shade for all meals. The notch stays as the seam between meals. Reads as one bar with an extension bolted on, which is what it is.
- Numbers stay current / max. Max includes food. The bar carries the "on loan" information, not the number.
- Stamina number rides the fill in the cloned prefab. All text elements must be parented to the bar root with a fixed anchor.

## Slice 8 spec: polish pass
Everything here is presentation. No new data, no new patches beyond what exists.

**Layout, one grid.** The cluster is one left-aligned column: chips row, health header, health bar, stamina header, stamina bar. Left edges all align to the bar's left edge. Vertical gaps: 6px between chips and health header, 3px between a header and its bar, 8px between health bar and stamina header.

**Headers.** Each bar gets a header line above it. Left: label in small caps, letterspaced, bone at 70% alpha ("HEALTH", "STAMINA"). Right: the numbers, right-aligned to the bar's right edge, tabular digits so they don't jitter. Health number big (about 1.6x the label), max smaller and dimmer after a slash. Stamina header uses the same pattern at a smaller size and fades with its bar. The stamina number no longer sits to the right of the bar.

**Bar ends.** Clip the right end of both bars at an angle (BarEndAngle, default 12, live). The notch angle follows BarEndAngle unless NotchAngle is set explicitly, so the seam and the end cap agree. If the angled version still reads wrong in game, set both to 0 and move on.

**Critical state.** When health is critical, the health number goes critical ember too. Label stays bone.

**Chips.** Same height as the health header line. Icon 1em square, name, then time in a dimmer weight. Gap between chips 6px. Row is left-aligned with the bars; it never pushes the bars down, it grows upward.

**Font.** Target is a squared technical face like Chakra Petch. Approach, in order: (1) try building a TextMeshPro font asset at runtime from a system font via Font.CreateDynamicFontFromOSFont and TMP_FontAsset.CreateFontAsset, with the font name in config (FontName), and log whether it worked; (2) if that fails, keep the vanilla font. A bundled custom TTF needs a Unity AssetBundle and is a later project. Numbers must stay legible at 14px whichever font wins.

**Food caption.** Same small size as the labels, bone at 85%, sits under the health bar, only when a meal is under two minutes.

**Out of scope.** Eitr bar, minimap, hotbar, any vanilla message text.

## Structure
- **Health:** thin horizontal bar, bottom-left. Faint dividers mark base health and each active food's share of max. A food's segment shrinks continuously over its life, so the pulse and caption fire at the "you can eat this again" moment instead of at expiry.
- **Damage trail:** on a hit the fill drops instantly, and a light trail holds the lost chunk for about 0.45s, then drains.
- **Stamina:** thinner bar directly under health. Hidden when full, fades in while spending, fades out about 1s after refilling.
- **Status chips:** sit just above the bars. Icon, one word, timer. Bad effects first with an ember outline, good ones quieter. Slice 9 (`003-inventory.md`) dropped the appear/disappear fade to 80ms and fixed a layout jump: a chip leaving still reserves its slot until its fade-out finishes, instead of its neighbors snapping over immediately.
- **Low health:** bar goes brighter red, soft red vignette at the screen edges.
- **Loud moments:** Freezing (and later: about to die, encumbered?) gets a bold angled slab near the top for about 2.5s, then only the chip remains. Slice 9 (`003-inventory.md`) replaced the original scale/ease-back-in motion with a 12px slide-in-from-above plus alpha over 120ms, then alpha-out over 200ms; encumbered shares the same slab and got the same treatment. `Motion.Enabled` off restores an instant snap.

## How food actually works (from slice 1)
Max health is 25 plus each active food's current contribution, and that contribution decays as `pow(timeLeft / burnTime, 0.3)`. The 0.3 exponent means it drops quickly at first and then very slowly, so a food is still worth roughly two thirds of its value at half time.

So a segment shrinks the whole time it is alive. There is no flat period and no cliff at the end, and "the bar shrinks visibly when it expires" was the wrong mental model: by expiry there is barely anything left to lose.

The moment worth designing for is `CanEatAgain()`, which flips at half burn time. That is when the game lets you top the food back up, so it is the only point where the display is telling you to do something. Vanilla already pulses the food icon there. Our pulse, caption and any loud treatment go there.

## Geometry (from slice 2, in game)
With Minimal UI parked, our cloned bar still rendered vertical, about 330px tall with tick marks. That ruled out Minimal UI. The cause: vanilla's own bar prefab carries a baked rotation and/or scale that the decompiled code can't show, because rotation and scale are Unity scene data, not C# logic. Cloning it inherited that geometry.

We now log the source's and the clone's RectTransform state (rotation, scale, anchors, pivot, sizeDelta) at creation, then strip every transform in the clone back to identity rotation and unit scale, anchor the root to its parent's bottom-left corner, and size it ourselves. `GuiBar`'s fill logic (`SetWidth`) is hardcoded in vanilla's own code to resize along the RectTransform's local X axis, that part is genuinely fixed and not scene data, so once rotation is zeroed, X is really width on screen and the vanilla fill and damage trail keep working. No need to abandon the clone for hand-drawn Images.

## Bar width (from slice 2, in game)
Vanilla grows the health bar with max health: `(maxHealth / 25) * 32` pixels, so eating stretches it. Cloning that behavior directly gave a 40px stub at 33 HP before any food was eaten, which is unreadable and shifts anything anchored near it.

Slice 2 switched to a fixed width instead (`BarWidth` in Configuration Manager, default 320px, independent of the `Scale` multiplier). The bar's frame stays put; only the fill inside it moves with health, the way GuiBar already animates. Slice 3's food segments divide this fixed width proportionally rather than stretching the bar, which also reads better: a segment losing width inside a constant frame is a clearer signal than the whole bar changing size.

## Scale
The mockup is oversized so it reads on a phone. In game the cluster should be roughly 20 to 25% of screen width at 1440p, sized from the vanilla HUD's own scale. Tune in game with a config value.

## States
- Main menu, loading, dead: HUD hidden (null-guard Hud / Player).
- No food eaten: only the base segment.
- Eitr (magic): later slice, same pattern as stamina.
- Controller vs mouse: Geo plays on a controller. The HUD itself is display only, but any hint text we add must name controller buttons when a controller is active, the way vanilla's key hints do. Also: on controller the d-pad only highlights hotbar slots, you have to press the slot's button to equip, and nothing on screen says so. Candidate for a later spec.
- With Minimal UI installed: it replaces health, stamina and food with its own rotated clones (`MUI_HPBar`, `MUI_StaminaBar`, `MUI_FoodBar`), on by default. Our HUD hides those too, so BoneAndEmber looks right out of the box and the enable toggle is a real A/B. Details in `001-notes-vanilla-hud.md`.

## Config (Configuration Manager, F1)
- Enable new HUD (fall back to vanilla when off)
- HUD scale
- Bar width (px, fixed, does not grow with max health, see below)
- HUD position
- Show numbers on bars
- Show food segments
- Low health vignette on/off
- Hide other health/stamina bars (Minimal UI compatibility)

## Build slices
1. ~~Read vanilla~~ Done, written up in `001-notes-vanilla-hud.md`.
2. Hide vanilla health/stamina and draw a plain new health bar with numbers. Prove it updates.
3. Food segments that shrink continuously, with the pulse and caption at the eat-again moment.
4. Damage trail, low health state.
5. Stamina bar with auto-hide.
6. Status chips (replace or restyle vanilla status effect icons).
7. Loud moment for Freezing.
8. Fonts and final polish.

## Open questions
- Custom font: bundling a TMP font asset needs a Unity AssetBundle (so the Unity Editor). Until then, use the closest font Valheim already ships.
- ~~Where vanilla keeps food timers and the status effect list~~ Answered in slice 1. Food timers are `Player.Food.m_time` on `Player.m_foods`, read with `Player.GetFoods()`. Status effects come from `Player.GetSEMan().GetHUDStatusEffects()`, with the countdown on the effect itself. See `001-notes-vanilla-hud.md`.

## Tested in game
Not yet.
