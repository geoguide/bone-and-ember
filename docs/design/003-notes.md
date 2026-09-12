# 003 notes: slots, tooltips, the grid cursor, food math, radial

Research for slice 1 of `003-inventory.md`. Read from
`reference/decompiled/assembly_valheim/`, `assembly_valheim/Valheim.UI/`,
`assembly_guiutils/`, `assembly_utils/`, and `minimalui/`. Method names are
stable enough to grep for, exact line numbers are not, so re-run
`tools/decompile.sh` and re-grep after a Valheim update.

Nothing here is Iron Gate's code. It is a description of it.

## Three findings that changed the spec

**Vanilla already ships a radial menu with a consumables page.** It is bound to
R3 on gamepad and G on keyboard, it lists your food with icons and counts, and
its interact calls the same local-only use path we wanted. Slice 8 shrinks from
"build a radial" to "open the food page in one press and restyle it".

**Slices 2 and 3 are mostly restyling things that already exist.** The slot
prefab already carries an equipped tag, a food dot tinted by dominant macro, a
durability bar, and hotbar numbers on the top row. Almost nothing here is new UI.

**No controller button is free.** The default gamepad layout is fully assigned,
including the d-pad up the spec wanted for quick-use. Reuse the vanilla radial
binding or use an alt-key combo.

## How slots get built and updated

`InventoryGrid.UpdateGui(Player, ItemData dragItem)` is private and runs **every
frame**. Call chain: `InventoryGui.Update` → `UpdateInventory(player)` →
`m_playerGrid.UpdateInventory(inv, player, dragItem)` → `UpdateGamepad()` then
`UpdateGui()`. Same for the container grid via `UpdateContainer`.

Element creation is lazy and wholesale. `UpdateGui` compares `m_width`/`m_height`
against `m_inventory.GetWidth()/GetHeight()`; if they differ it **destroys every
element and rebuilds the list** from `m_elementPrefab`. Layout is
`anchoredPosition = (x * m_elementSpace, -y * m_elementSpace)`, index into
`m_elements` is `y * width + x`.

Per-frame work in `UpdateGui`:

1. Reset `m_used` and `m_canBeDroppedOn` on all elements.
2. Loop `m_inventory.GetAllItems()`, fill in icon, durability, equipped, queued,
   noteleport, food, quality, amount, and call `CreateItemTooltip` for the
   hovered or gamepad-selected one.
3. Second loop over all elements: set `m_selected` active for the cursor
   position, and for anything with `m_used == false`, disable every child and
   blank the tooltip strings.

`InventoryElement`, the component on the slot prefab, already carries:

| Field | What it is | Spec item it covers |
| --- | --- | --- |
| `m_icon` (Image) | item sprite | slice 2 icons |
| `m_amount` (TMP_Text) | `"{stack}/{maxStack}"`, only when `maxStackSize > 1` | slice 2 counts |
| `m_quality` (TMP_Text) | upgrade level, only when `maxQuality > 1` | |
| `m_durability` (GuiBar) | **only active when `useDurability && durability < max`** | slice 2 durability bars |
| `m_equiped` (Image) | already drawn when `item.m_equipped`, player grid only | **slice 3 equipped tag already exists** |
| `m_queued` (Image) | equip action pending | |
| `m_noteleport` (Image) | non-teleportable marker | |
| `m_food` (Image) | **already tinted** by dominant macro: blue eitr, red health, yellow stamina, white balanced | food at a glance |
| `m_selected` (GameObject) | the gamepad cursor highlight | controller restyle |
| `m_button` (Button) | its `colors.highlightedColor` is the mouse hover highlight | controller restyle |
| `m_tooltip` (UITooltip) | per-slot tooltip data | slice 4 |
| child `"binding"` (TMP_Text) | **already prints 1 to 8 on row 0 when `player != null`** | **slice 3 hotbar numbers already exist** |

So slice 3 is almost entirely a restyle of `m_equiped` and the `binding` text,
not new UI. The container grid passes `player = null`, which is exactly what
turns off the binding numbers and the equipped and queued flags there.

Two hazards for our patch. Postfix `UpdateGui` by name, it is private.

- The rebuild path destroys elements, so any child object we add dies with them.
  Container grids resize per chest, so this fires often. Attach a marker
  component to each element and rebuild our extras when the marker is missing,
  rather than assuming one-time setup.
- The second loop unconditionally disables children on empty slots, and the
  first loop re-sets icon color, durability active state and text every frame.
  Anything we set must be re-set in our postfix every frame or it gets stomped.

## How the tooltip is assembled

Two layers, cleanly separable.

**The widget.** `UITooltip` in `assembly_guiutils`. There is exactly one live
tooltip GameObject at a time, a static `m_tooltip` instantiated from
`m_tooltipPrefab` under the canvas. `UpdateTextElements` finds children literally
named `"Text"` and `"Topic"`, both `TMP_Text`, and pushes
`Localization.instance.Localize(...)` into them. Mouse shows it after a 0.5s
hover timer, gamepad and touch show it immediately. `HideTooltip()` destroys it.
`Set(topic, text, anchor)` early-returns if both strings are unchanged, so it is
cheap to call per frame.

`InventoryGrid.CreateItemTooltip` is one line:
`tooltip.Set(item.m_shared.m_name, item.GetTooltip(), m_tooltipAnchor)`.

**The body string.** `ItemDrop.ItemData.GetTooltip()` calls static
`GetTooltip(item, qualityLevel, crafting, worldLevel, stackOverride, appending)`.
It is one big `StringBuilder`: description, then a run of
`"\n$loc_key: <color=orange>{0}</color>"` lines, then a `switch` on
`m_shared.m_itemType` for the type-specific block, then status effects, set
effects and equipment modifiers.

**Do not parse this string for the detail panel.** Re-derive from the same
sources it uses:

- weapons: `item.GetDamage(quality, worldLevel)` returns `HitData.DamageTypes`;
  `m_shared.m_attack.m_attackStamina` and `m_attackEitr`;
  `m_shared.m_attackForce` for knockback; `m_shared.m_backstabBonus`
- armor: `item.GetArmor(quality, worldLevel)`
- blocking: `item.GetBaseBlockPower(q)`, `item.GetDeflectionForce(q)`,
  `m_shared.m_timedBlockBonus`
- durability: `item.m_durability`, `item.GetMaxDurability(q)`,
  `item.GetDurabilityPercentage()`
- weight: `item.GetWeight()`, `item.GetNonStackedWeight()`
- value: `m_shared.m_value`, `item.GetValue()`

Gotcha: `GetTooltip` dereferences `Player.m_localPlayer.GetSkillLevel(...)` with
no null guard. Calling it on the main menu throws.

## How the controller grid cursor works

`InventoryGrid.UpdateGamepad()`, run first thing in `UpdateInventory`, every
frame.

Gate: `m_uiGroup.IsActive && !Console.IsVisible() && !ZInput.IsTouchActive() &&
ZInput.IsExclusiveGamepadActive()`.

`m_selected` is a `Vector2i` moved by `JoyDPad*` or `JoyLStick*`. On change it
calls `GetGamepadSelectedElement().GetComponent<Selectable>().Select()` and, if
present, `m_ensureVisible.CenterOnItem(...)` to scroll it into view.

The highlight itself is drawn in `UpdateGui`'s second loop:
`element.m_selected.SetActive((gamepadActive || touchSelected) && element.Position == m_selected)`.
Mouse hover is separate: `InventoryElement.UpdateHighlightColor()` swaps the
Button's `ColorBlock.highlightedColor`. Restyling the cursor means both.

Buttons while the grid has focus: **A** select or pick up, with LT for split and
RT for drop. **X** use or equip, the same path as right-click. **LT+X**
quick-move to the other inventory. **LB/RB** (`JoyTabLeft`/`JoyTabRight`) cycle
UI groups.

Grid to grid handoff: pressing up on row 0 fires `OnMoveToUpperInventoryGrid`,
down on the last row fires `OnMoveToLowerInventoryGrid`. `InventoryGui.Awake`
wires those to `MoveToUpperInventoryGrid` and `MoveToLowerInventoryGrid`, which
map the x position across the two grid widths and call `SetActiveGroup`. A
`jumpToNextContainer` latch forces you to release the stick before crossing, so
the first press at an edge does nothing.

## The container view shares InventoryGrid

Fully. `InventoryGui.Awake`:

```
m_playerGrid    = m_player.GetComponentInChildren<InventoryGrid>();
m_containerGrid = m_container.GetComponentInChildren<InventoryGrid>();
```

Both get the same `m_onSelected`, `m_onReleased`, `m_onRightClick`, `m_onEnter`
and `CanDropDragOntoItem` handlers. `UpdateContainer` calls
`m_containerGrid.UpdateInventory(m_currentContainer.GetInventory(), null, m_dragItem)`.
The `null` player is the only difference, and it is what disables hotbar bindings
and the equipped flag on chest slots.

So a single postfix on `InventoryGrid.UpdateGui` covers both grids and **slice 2
is covered for free**. To tell them apart use the public
`InventoryGui.instance.ContainerGrid` property, or compare against
`m_playerGrid`.

Two container-specific things to respect: `ResetView()` re-pivots `m_gridRoot`
when the chest is taller than the viewport, and there is a `m_scrollbar` and
`m_ensureVisible` pair, so big chests scroll. Our added visuals must be children
of the element, not absolutely positioned over the grid, or they desync on
scroll.

## Food effects are computable without eating

Everything is on `m_shared` and the math is in `Player`.

Static, per item:

- `m_shared.m_food`, max health added
- `m_shared.m_foodStamina`, max stamina added
- `m_shared.m_foodEitr`, max eitr added
- `m_shared.m_foodBurnTime`, seconds
- `m_shared.m_foodRegen`, hp per regen tick, and the tick is every 10s
- `m_shared.m_consumeStatusEffect`, the effect eating applies, if any

Decay and totals, from `Player.UpdateFood`, which ticks once a second scaled by
`Game.m_foodRate`:

```
f       = pow(clamp01(food.m_time / burnTime), 0.3)
health  = m_shared.m_food * f          // same shape for stamina and eitr
maxHP   = m_baseHP (25) + sum(food.m_health)
maxStam = m_baseStamina + sum(food.m_stamina)
maxEitr = 0 + sum(food.m_eitr)
```

So the predicted new max after eating is the current max plus the item's full
`m_food`, minus whatever the replaced slot was contributing. `Player.GetFoods()`
is public, so we can read the live three slots and name the meal that would be
pushed out.

Slot rules, from `Player.CanEat` and `EatFood`: three slots max. The same food
refreshes in place if `CanEatAgain()`, meaning `m_time < burnTime / 2`.
Otherwise it fills a free slot, and if all three are full it replaces
`GetMostDepletedFood()`.

`Player.CanEat(item, showMessages: false)` is public and side-effect free with
the flag off, so it is safe to call for the spec's "whether you can eat it now".
`Player.CanConsumeItem` sits above it and also checks world level.

Formatting: `ItemDrop.ItemData.GetDurationString(seconds)` gives `"25m 0s"`. The
spec's "+30 max health for 25 min" is a straight reformat of `m_food` and that
helper.

One vanilla bug to know about: in `EatFood`'s replace-the-most-depleted-slot
branch it assigns `m_health` and `m_stamina` but not `m_eitr`. If we predict
eitr numbers we would be more correct than the game for one tick, until
`UpdateFood` recomputes.

## What Minimal UI touches here

Caveat: `reference/decompiled/minimalui` is an older build. It references
`Element` (now `InventoryElement`), `m_splitPanel`, and a public `m_inventory`
field. Read it as intent, not as current API.

- **`InventoryGrid.UpdateGui` postfix** (`InventoryGridUpdateGuiPatch`) is a
  direct collision with our slot restyle. It scales every item icon to 0.75,
  rewrites `m_quality` as stars or `xN`, destroys the quality `Outline`
  component, and moves the quality rect to `(-4, -6)`. It guards the icon scale
  with `if (localScale == Vector3.one)`, which is fragile. Harmony priority
  decides who runs last.
- **`InventoryGui.Awake`, patched twice**, recolors and reskins every descendant
  named `Bkg`, `border (1)` or `repairsimple`, force-enables `blur` objects,
  rescales the info panel `Bkg` and `Darken`, shifts `m_player`'s `Bkg` and
  `PlayerGrid` left by 10px, and reparents the info panel's Texts, Skills,
  Trophies and PVP buttons into a new `ButtonGroup` plus adds Crafting and
  Inventory toggle buttons.
- **`InventoryGui.Update`, patched twice**, is the important one. It re-applies
  `anchorMin`, `anchorMax` and `localScale` to `m_player`, `m_container`,
  `Crafting` and `m_infoPanel` every frame from its own config. Anything we
  position by re-anchoring those four gets overwritten instantly. Our UI must be
  a child of them or on our own object.
- **`InventoryGui.Show` and `Hide`**: with its `AlwaysOpen` off, it deactivates
  `m_player` and `m_crafting` entirely, so the player grid only appears when a
  container is open. Don't assume `m_player.gameObject.activeSelf`.
- **`ShowSplitDialog`, `OnShowVariantSelection`, `StoreGui.Show`,
  `CraftingStationInteract`, `SkillsDialog.Awake`, `TextsDialog.Awake`** are
  cosmetic reskins only.

Not touched by Minimal UI at all: the tooltip, the gamepad cursor and
`m_selected`, food, and item drag. Those are ours uncontested.

## Restyling slots does not break drag or the split dialog

The drag ghost is a separate object. `InventoryGui.SetupDragItem` instantiates
`m_dragItemPrefab` under the InventoryGui transform, not under the grid. The
grid's only involvement is greying the source icon:
`element.m_icon.color = (item == dragItem) ? Color.grey : Color.white`. So if we
set `m_icon.color` unconditionally we would erase the drag feedback. That is the
one real interaction.

The split dialog is `InventoryGui.m_splitDialog`, its own object, driven by
`ShowSplitDialog` and `OnSplitOk`. Element styling cannot reach it.

Three caveats:

1. **Hit-testing.** `GetHoveredElement()` uses
   `elementRectTransform.rect.Contains(...)` and `GetItem()` uses
   `RectTransformUtility.RectangleContainsScreenPoint`. Don't resize the
   element's own RectTransform, and don't put raycast-target graphics on top of
   it.
2. **Handler lookup.** `UIInputHandler` and `UIDragHandler` are found with
   `GetComponentInChildren` at build time. Don't reparent the prefab's children.
3. **The rebuild path** described above destroys anything we added.

## Quick-use radial: vanilla already has one

`Valheim.UI.RadialBase`, held as `Hud.m_radialMenu`, opened by the `"OpenRadial"`
binding (default G) or `"JoyRadial"` (R3 in the default gamepad layout), routed
through `OpenRadialConfig`. `Hud.InRadial()` is public.

The main page, `ValheimRadialConfig`, already builds these groups: hotbar,
**consumables**, weaponstools, armor_utility, emotes, allitems, hammer if you
have one, and last-used.

`ItemGroupConfig.InitRadialConfig` fills a page from
`inventory.GetAllItemsOfType(ItemTypes, sortByGridOrder: true)`. Each
`ItemElement` shows the icon, stack amount and a durability bar, and its
`Interact` is:

```
Player.m_localPlayer.UseItem(null, item, fromInventoryGui: false)
```

That is exactly the local-only use path the spec asked for. Secondary interact
drops one.

`RadialData.SO` already exposes `EnableReleaseToUseMode`, `HoldCloseDelay`,
radial size, cursor speed, hover-select speed, and element counts, where
`MaxElementsRange` defaults to 8 to 12, matching the spec's "up to 8 items".

So slice 8 becomes: open the consumables page directly on one press instead of
two, and restyle the elements to the 001 palette.

## No controller button is free

The default gamepad layout is fully assigned:

A jump, use, build. B jump, dodge. X sit. Y inventory. LB run. RB secondary
attack. LT block. RT attack. L3 crouch. **R3 radial.** D-pad up hotbar use.
D-pad down guardian power. D-pad left and right cycle hotbar. Start menu. Select
map.

The spec's proposed "d-pad up held" is `JoyHotbarUse`. Recommendation: don't add
a button. Either reuse `OpenRadial` and `JoyRadial` and change what they open,
or use `ZInput`'s altKey concept the way `JoyCamZoomIn` does, which is d-pad up
plus alt.

## Two spec errors, now fixed

- The grid is 8 wide by 4 tall. `GetWidth()` returns 8, `GetHeight()` returns 4.
  The spec said "4x8", which reads the other way round.
- Eaten food is not in the grid. It is `Player.m_foods`, a three-entry list on
  the player, rendered on the HUD. The grid holds the uneaten item. The "four
  jobs" line and slice 4 have been reworded.

## Radial (slice 8)

`Valheim.UI.RadialBase` is the whole menu. `Hud.m_radialMenu` holds it, and
`OpenRadialConfig` (an `IRadialConfig` ScriptableObject) decides which group
opens. We do not touch input: the bindings are vanilla's `OpenRadial` (G by
default) and `JoyRadial` (R3 in the default gamepad layout, LT in the alt
layouts), read by `RadialConfigHelper`, and none of that is patched.

**Wedges are drawn by a shader, not by sprites.** This is the finding that
shapes everything else. `RadialMenuElement` owns a per-instance material
(`BackgroundMaterial`, cloned from `Background.material` on first access) and
every visual state is a material property:

| Property | Type | What it does |
| --- | --- | --- |
| `_UnselectedColor` | Color | the resting wedge plate |
| `_ActivatedColor` | Color | equipped/active state |
| `_QueuedColor` | Color | queued equip |
| `_Selected` | int | 0/1 |
| `_Activated` | float | 0..1 |
| `_Queued` | int | 0/1 |
| `_Hovering` | float | 0..1, tweened by `RadialMenuAnimationManager` |
| `_Segments` | int | how many wedges in the ring |
| `_Offset` | float | this wedge's position in the ring |

The C# property setters clamp alpha to 0.8, but `BackgroundMaterial.SetColor`
bypasses that, so a fully opaque plate is reachable.

Consequence: there is no wedge GameObject to recolor and no arc sprite to
outline. Anything that has to follow the wedge arc (separators, an outline
that overshoots the arc edge) has to be a shader property that already exists
or it cannot be done without authoring a shader. Our patch probes the
material with `Material.HasProperty` and logs which color properties are
really there, rather than assuming names.

**Elements are rebuilt on every open.** `RadialBase.ConstructRadial` calls
`ClearElements`, which `Destroy`s every child of the element container, and
each group config (`ItemGroupConfig.InitRadialConfig` and friends)
`Instantiate`s fresh elements from `RadialData.SO`. So the marker-component
pattern from the slots does not apply here: there is nothing to mark, because
nothing survives. The right hook is a postfix on `ConstructRadial`, which runs
once per open with the finished element list.

**How a group is chosen on open.** `OpenRadialConfig.InitRadialConfig` picks
one: emotes if `OpenEmote` is held, then a hover menu if the thing you are
looking at has one, otherwise `RadialData.SO.MainGroupConfig`. The main page
builds group buttons for hotbar, consumables, weaponstools, armor_utility,
emotes and allitems. Opening a different group is just
`radial.Open(config, backConfig)`, so starting on consumables needs no new
group and no change to which groups exist.

**Selection and the center.** `RadialBase.Selected` is a property; changing it
runs `OnSelectedUpdate`, which calls `ElementInfo.Set(element, animator)`.
`ElementInfo` is the center: `m_title` and `m_subTitle` (both
`TextMeshProUGUI`), `m_icon`, `m_durabilityBar`, and a `RadialInventoryInfo`
panel. Worth knowing: for an `ItemElement` the center **blanks the title**
(`m_title.text = flag ? "" : element.Name`) and shows the name through the
inventory-info panel instead, so writing the item name into `m_title`
ourselves is an addition, not an override of something vanilla draws there.

**Mouse works already, no keyboard/mouse path needed later.** `RadialBase`
takes two direction sources and `RadialConfigHelper.SetXYControls` wires both:
`GetControllerDirection` reads the `RadialStick` axis, and `GetMouseDirection`
takes `ZInput.pointerPosition` minus `radial.InfoPosition` (the center) and
normalizes it, so the pointer's angle from the center picks the wedge exactly
like the stick does. Interaction is dual-bound too: `GetConfirm` accepts
`JoyRadialInteract` **or** left mouse up, `GetBack` accepts `JoyRadialBack`,
`JoyRadialClose` or right mouse up, and `GetClose` accepts Escape and the
backquote key alongside the pad buttons. So mouse and keyboard are first-class
in the radial already; nothing needs adding for them.

**Wedge width is already constant; do not pad short groups.** The count that
drives wedge width is `RadialBase.MaxElementsPerLayer`, not the number of
entries in the group. `SetElementsPerLayer` seeds it from
`RadialData.SO.MaxElementsRange[0]`, which is **8**, and only raises it to 12
once a group holds more than 8. `SetRadialLayout` then calls
`element.SetSegment(MaxElementsPerLayer)` on every element, so `_Segments` is
8 for any group of 8 or fewer and the arc is `360 / 8 = 45` degrees whether the
group holds three entries or eight. A three-item group already draws three
45-degree wedges with a gap, not three 120-degree ones.

So there is nothing to fix, and padding a short group to 6 would make it
worse: 6 segments is a 60-degree arc, wider than every other group's 45.
`MaxElementsPerLayer` is `{ get; private set; }` in any case. Left alone.
