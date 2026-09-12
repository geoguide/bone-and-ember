# 003: Inventory overhaul (plan)

Status: plan plus round 1 mockup: https://claude.ai/code/artifact/fde9a76a-73e8-4c04-8ee0-60806bae4024 Build in its own chat after 002 ships. Slice 1 (research) is done, findings in `003-notes.md`. Everything below slice 1 is still unverified in game.

## The moment
Placeholder. Geo is new to Valheim and hasn't hit his defining inventory frustration yet. Fill this in with the first one that lands: a specific time you opened the inventory, reached for something, and it went wrong.

## What vanilla is doing wrong (from a newcomer's seat)
- One 8x4 grid does three jobs: storage, worn equipment, and the hotbar (top row). Nothing on screen says which is which. Food is a fourth job the grid only half does: the uneaten item sits in a slot like any other material, and what it did to you once you ate it lives somewhere else entirely, on the HUD.
- Item facts are a wall of orange numbers in a tooltip. Comparing a new axe to your equipped one means remembering numbers.
- Durability is a sliver under the icon. Weight is one number in a corner that you only notice when you're already too heavy.
- You pick food in the inventory but only learn what it did on the HUD, a screen away and a second later. Nothing tells you "this is +30 max health" while you're deciding, or that eating it would push out the meal you're still running on.
- Controller navigation is a cursor on a grid. Quick-using food or a potion means opening the inventory mid-fight.

## Hard constraint
The grid is game data. The server owns your 8x4 layout, stack sizes, and item positions, and other players' clients read them. We can change how slots are drawn, grouped, filtered, and navigated, and what the detail panel says. We cannot add slots, move equipment out of the grid for real, or change stacking. Everything below is presentation and local input.

## References, by job
- **Structure: Zelda (BotW / TotK).** Category tabs across the top, big tiles, counts in the corner, one line of text. Same survival-crafting problem space, readable at a glance.
- **Item detail: Persona 5.** Bold name, one clear stat block, deltas against what you're wearing in color. This is where Persona's boldness earns its place.
- **Controller quick-use: Monster Hunter.** Radial menu for consumables and food, no inventory screen needed.
- **Compare: Diablo IV.** Hover an item, see it beside the equipped one, arrows for better and worse.
- **Avoid:** Persona's list layout for the grid itself. Lists don't fit stacks and drag-to-slot.

## What it should feel like
Same family as the HUD: bone, ember, gold, squared type, thin edges, no wooden frames. The inventory should read as the same product as the bars.

## Decisions from the round 1 mockup
- Detail card sits to the right of the player grid, in the open middle of the screen. It does not overlap the crafting panel on the far right. When a container is open the card stays put; the container grid is below the player grid, not beside it.
- Filtered-out slots dim to about 30% alpha. They do not shrink: the element RectTransform is what hit-tests, so its size stays.
- Slot counts show the stack number only, no "/max".
- No "Pack" row label: there are only 6px between rows. A 1px bone rule at 30% alpha in that gap separates the hotbar row from the rest, and the hotbar numbers do the naming.
- Weapon deltas compare against whatever you would have to put down to wield the hovered item, not by item type. Armor compares by slot.

## Structure
1. **Grid stays a grid.** Same 32 slots, same positions, so muscle memory and the hotbar row survive. Slots get flat plates in the HUD palette, larger icons, counts bottom-right in tabular digits, durability as a thin bar along the bottom edge in gold that goes ember under 20%.
2. **Rows get meaning.** The top row is visibly the hotbar: a subtle label and slot numbers that match the HUD hotbar. Equipped items get an ember corner tag wherever they sit in the grid, since we can't move them.
3. **Filters instead of tabs.** Because slots can't move, tabs that hide items would break drag-to-slot. Instead: a filter row (All, Weapons, Armor, Food, Materials, Tools) that dims everything not in the chosen category. Items stay put, the eye finds them.
4. **Detail panel (the Persona part).** Selecting or hovering an item fills a fixed panel to the right of the grid: big name, item type, one stat block. For weapons and armor, deltas against the equipped item in green and ember. For food this panel is the only place the answer can live, since the effect isn't in the grid at all: what it will do, in the same language as the HUD ("+30 max health for 25 min"), whether you can eat it now, and which of your three meals it would replace if you're full.
5. **Weight as a bar,** not a number. Bone bar along the bottom of the grid that goes ember near the limit, with the number beside it.
6. **Crafting panel** left alone in the first pass. It's a second product and a second spec.

## Controller
- The grid cursor stays vanilla. We restyle its highlight to match.
- Quick-use food and consumables without opening the inventory. Vanilla already has this: the radial on R3 (G on keyboard) has a consumables page showing icons and counts, and using an item there is already the local-only path we wanted. Our job is to land on that page in one press instead of two, and restyle it. No new button, because none is free.
- Every hint we add names the controller button when a controller is active.

## States
- Empty inventory, full inventory, overweight.
- Item hovered vs selected vs dragging.
- Container open beside inventory (chest view): our styling must apply to both grids or neither.
- Crafting panel open: our detail panel must not overlap it.
- Dead, main menu, loading: nothing of ours renders.
- With Minimal UI installed: it reskins these same panels. Coexist or hide its version behind a toggle, same approach as the HUD.

## Config
InventoryEnabled, FilterRow, DetailPanel, WeightBar, DurabilityBars, EquippedTags, QuickUseRadial, QuickUseButton.

## Build slices
1. Research: InventoryGui, InventoryGrid, ItemDrop.ItemData, how tooltips are built, how the grid cursor works on controller, what Minimal UI touches here.
2. Slot restyle: plates, icons, counts, durability bars. Prove it survives a container open.
3. Hotbar row label and equipped tags.
4. Detail panel with static facts, no deltas. Read the facts off ItemData, don't parse the vanilla tooltip string.
5. Deltas for weapons and armor. Food predictions in HUD language, computed from m_shared and the player's live three food slots.
6. Filter row.
7. Weight bar.
8. Quick-use: point the vanilla radial's consumables page at one press and restyle it. Not a new radial, see `003-notes.md`.
9. Polish and motion: fonts/palette from 001 shipped earlier under this slice number without being marked off. This slot now covers real motion instead - a shared tween helper (`src/Hud/Tween.cs`), radial hub layout and hover fade, inventory detail card/filter/slot motion, and HUD loud moment/status chip timing. See `001-always-on-hud.md`'s loud moment and status chip sections for the HUD half. Config: `Motion.Enabled` turns all of it off for an exact A/B against the instant versions.

## Decisions: hotbar button hint (sweep, 2026-09-10)
With a gamepad, the highlighted hotbar slot carries a small glyph plate showing the button that acts on it, read live from the player's own bindings (`ZInput.GetBoundKeyString`), so a DualSense shows its own glyph and a remap shows the remapped button.

There is only one such button. Vanilla has no separate equip and unequip action: `InventoryGrid.UpdateGamepad` sends `JoyButtonX` to `Player.UseItem`, which toggles, and d-pad up/down are already spent on moving the highlight between rows. So the glyph says "this is the button" and nothing else; whether pressing it puts the item on or takes it off is carried by the equipped corner triangle. One meaning per element.

**It belongs on the bar at the bottom of the screen, not the inventory grid.** The first build put it on the inventory grid's row 0, which was wrong twice over: that grid's d-pad is spent on navigation, and it is not the thing a player means by "the hotbar". `HotkeyBar` has its own gamepad handling, with `JoyHotbarLeft`/`JoyHotbarRight` moving the highlight and a real dedicated `JoyHotbarUse` acting on it. So the hint lives there, on whichever element vanilla has marked selected.

The glyph goes into vanilla's own `binding` label rather than a new object. That field already means "the input for this slot", vanilla itself blanks it when a pad is active, and it is the one text in the element guaranteed to resolve TMP's gamepad sprite tags. The first attempt built a fresh TMP and the PlayStation glyph came out as an empty box. Keyboard players keep their number. Config `Inventory.HotbarHints`.

## Slice 9 follow-ups from testing (2026-09-10)
- Gamepad: vanilla's UIGroupHandler shows a gold "this panel has focus" object whenever a pad is the active input. It used to sit under the wooden panel art and read as a rim; with the art hidden it covered the whole grid in gold on the first d-pad press. It is now held at alpha 0 while PanelRestyle is on, and the focus signal moves to our own panel border (gold edge on the focused panel, bone otherwise).
- Radial size: vanilla's RadialOverlapPreventer rescales the radial on its first Start() after our postfix, so the first open came out at vanilla's size and every later one at ours. Vanilla's scale is now reset to 1 whenever it writes, Radial.Scale is applied every frame (live slider), and its default moved from 0.8 to 1.0 since the bigger first-open look was preferred. Range is 0.5 to 1.5.
- The big vanilla ornament over the selected wedge (each wedge's own Highlighter image) is untouched so far.

## Open questions, answered in slice 1

Full detail in `003-notes.md`.

- **Does the container view share InventoryGrid code so slice 2 covers both?** Yes, completely. Same class, same element prefab, same `UpdateGui`. One postfix covers both grids. Tell them apart with `InventoryGui.instance.ContainerGrid`, and respect the container's scrolling and per-chest resize.
- **Can food effects be computed for display without eating, from ItemData.m_shared?** Yes. `m_food`, `m_foodStamina`, `m_foodEitr`, `m_foodBurnTime`, `m_foodRegen`, plus `Player.GetFoods()` for the live three slots and `Player.CanEat(item, false)` for eligibility. The decay curve is `pow(timeLeft / burnTime, 0.3)`.
- **Which controller button is actually free while the inventory is closed?** None. The default layout is fully assigned, and the d-pad up this spec wanted is `JoyHotbarUse`. Reuse the vanilla radial binding or use an alt-key combo.
- **Does restyling slots break the drag ghost or split-stack dialog?** No, both are separate objects under InventoryGui. The one overlap is the grey tint the grid puts on a dragged item's icon, so don't set `m_icon.color` unconditionally. The real risks are hit-testing, reparenting the prefab's children, and the rebuild-on-resize path that destroys anything we added.

## Still open
- Where exactly the detail panel sits without fighting the crafting panel. Needs UnityExplorer.
- Whether we fight Minimal UI over `InventoryGrid.UpdateGui`, which it also postfixes, or just require it off.
