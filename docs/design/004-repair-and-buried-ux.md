# 004: Repair, and the rest of what's buried

## The moment
A friend tried the mod and said: "it took me 10 years to find the repair button. I actually don't know how to repair."

He is not wrong, and he is not new. Here is what vanilla actually asks of you, read out of `InventoryGui`:

1. Be standing at a crafting station. If you are not, the repair panel and its button are `SetActive(false)` outright. There is nothing on screen to find, so you cannot learn it exists by looking at your inventory.
2. Open the inventory, then look at the crafting panel, which is a different tab from the grid you are actually looking at. The button is a small anvil icon there.
3. Press it once per item. `RepairOneItem()` repairs the first worn item and returns. Six worn pieces is six presses, each one printing its own center message.
4. Know which station. `CanRepair` wants the item's recipe to name this exact station (or its repair station), and the station's level to be at least the recipe's `m_minStationLevel`. When that fails, the button just sits there un-pressable with no reason given.

The kicker: repair is **free**. No materials, no cost. It sets durability to max and grants a little Crafting skill. A player who does not know about it is losing gear for no reason at all, which is exactly what happened to him.

## What it should feel like
You should never have to go looking. If something you are carrying can be fixed where you are standing, the game should say so, in the place you already are, and fixing all of it should be one action.

And when it can't be fixed, it should say why in words, not by hiding.

## Before / after

**Before:** inventory → crafting tab → find anvil icon → click, click, click, click.

**After (proposed):** a repair line sits with the weight bar under the grid, always in view while the inventory is open.
- At a station with worn gear: `REPAIR ALL (4)` in gold. One press fixes everything repairable, then the line goes quiet: `NOTHING TO REPAIR`.
- At a station that can't do this gear: `NEEDS A FORGE` or `NEEDS WORKBENCH LVL 2`, in dim bone, reading the reason off the same `CanRepair` checks vanilla uses.
- Away from any station: `REPAIR AT A WORKBENCH` in dim bone. This is the important one, because it is the state the friend was in for ten years, and vanilla shows nothing at all here.
- Worn items in the grid get a small ember pip; at a station that can fix them, the pip goes gold. The durability bar already shows how worn a thing is, so the pip only ever means "this is fixable, here, now".

Repair-all is a loop over vanilla's own repair, once per repairable item, i.e. exactly what the player would do by hand. Durability lives in your own inventory, not in a ZDO, so this stays client-only and does nothing another player or the server can see.

## States
- **Empty:** nothing worn, at a station → `NOTHING TO REPAIR`, dim, no button.
- **Full:** many worn items → count in the label, one press clears them all; suppress vanilla's per-item center message flood and print one line instead.
- **Overflowing:** more worn items than one press should silently churn through? No, repair is free and instant, so all of them is fine. One press, one summary.
- **Mouse vs controller:** the line is a real button for the mouse. On a pad it needs a binding; simplest is to make it a focusable element in the inventory's own UI group so the existing d-pad navigation reaches it, with the button glyph shown the way the hotbar hint does it.
- **With and without Minimal UI:** Minimal UI is parked, so untested against it. The line lives under our own grid furniture, next to the weight bar, so it does not fight vanilla's crafting panel at all. Vanilla's own repair button is left exactly where it is.
- **Main menu, loading, in a dialog:** the whole thing rides `InventoryGrid.UpdateGui`, which only runs while the inventory screen is open, so there is nothing to guard beyond the usual null checks.

## Config
- `Inventory.RepairLine` (default true) — the line under the grid.
- `Inventory.RepairAll` (default true) — one press repairs everything. Off makes the button repair one item, like vanilla.

## Built in slice 1 (2026-09-10)
The line and repair-all, not the per-slot pip. The pip wants a corner and all four are already spoken for on a slot (binding number, equipped triangle, stack count, durability strip), so it waits until the line has been seen in game and we know whether it is even needed.

Two decisions worth keeping:
- **The rules are borrowed, not copied.** `CanRepair`, `HaveRepairableItems` and `RepairOneItem` are private on `InventoryGui`, and their logic is fiddly (recipe must name this station or its repair station, station level must clear `m_minStationLevel`, world level can override all of it). `RepairGate` calls vanilla's own methods by reflection instead of reimplementing them, so the line can never claim a repair vanilla would refuse. If any of the three ever goes missing the line turns itself off and says so in the log.
- **Vanilla's button gets repair-all too**, not just our line. It is already reachable with a gamepad through the crafting tab's UI group, so the pad gets one-press repair without us injecting a new focusable element into vanilla's navigation graph. Gamepad players use the button they already know; the line is the discoverability fix for everyone else.

Message flood is handled by counting suppression around the loop, so six repairs print one summary instead of six `$msg_repaired` lines.

## Out of scope
Changing where vanilla's own repair button lives, or hiding it. Anything that costs or refunds materials. Auto-repair on arriving at a station, which takes the decision away from the player and would fire constantly.

## The rest of what's buried

Ranked by how much pain it causes against how cleanly we can fix it client-side. Nothing here is decided, it's for you to pick from.

1. **Station level and what it unlocks.** Standing at a workbench, there is no way to see "this is level 2, level 3 needs a tanning rack" without opening the build menu and hunting. High pain, and it's all readable off `CraftingStation.GetLevel()` and the piece requirements.
2. **Can I craft this, and what am I missing.** The recipe list greys out what you can't build but makes you click each one to find out which ingredient is short. A missing-ingredient summary, or a "craftable now" filter reusing our filter-row pattern, would be cheap.
3. **Guardian power.** `Player.GetGuardianPowerHUD` gives the active power and its cooldown. Vanilla puts it in a corner icon that most players forget exists. It could sit with the status chips, which is where "things that are true about me right now" already lives.
4. **Teleport-restricted items.** Vanilla marks ore with a small icon on the slot. It only bites when you are already standing at a portal with a full pack. Could read much louder in the detail card, and the weight bar could say "3 items can't teleport".
5. **Skills.** A whole screen behind a key most people press once. The interesting part, which skill is close to levelling, could surface as a single line rather than a screen.
6. **Quick-stack into a container.** The biggest QoL ask in every Valheim mod list, and the one that breaks our rules: moving items into a chest changes a networked container, which is exactly what "client-only, nothing the server sees" forbids. I'd want your explicit call before going near it, because it is a real exception, not a technicality.

## Tested in game
2026-09-11, first pass. Deltas, drop-target highlight, equipped triangle and the encumbered slab all confirmed working. Three things came back:

- **"Repair at a workbench" left the obvious question unanswered: repair what?** The line now leads with what is worn (`CLUB WORN  ·  REPAIR AT A WORKBENCH`, or `3 ITEMS WORN  ·  …`), so it names the problem before the destination.
- **It read as a button in its idle state and clicking did nothing.** It never took clicks when idle, but nothing said so. Leading with the worn summary and the `·` separator makes it read as a status line; the gold plate still only appears when it is really pressable.
- **Geo's instinct: "if it doesn't work it probably shouldn't show up."** Half right, and the half that matters cuts the other way: hiding it when unusable is exactly what vanilla does and exactly why nobody finds repair. The rule now is that the line appears whenever anything is worn, anywhere, and disappears only when there is genuinely nothing to fix. Nothing worn, no line.

**The button says `FREE`.** Repair costs nothing in Valheim, ever, but every other game charges for it, so the safe assumption is that it costs materials and should be saved for when you really need it. That assumption is free to hold and expensive to act on, and it is the likeliest reason the player this doc opens with said "I don't know how to repair" in the present tense after ten years. One dimmed word on the button kills it. Dimmed rather than bold because it is a footnote to the action, not the action.

**A station you are standing at can still refuse.** A workbench wants a roof over it that is not too exposed; a forge wants fire. `CheckUsable` only returns false, so the line was falling through to a useless "cannot repair here". It now re-runs the same two tests to say which, reusing vanilla's own strings (`$msg_stationneedroof`, `$msg_stationtooexposed`, `$msg_needfire`) so the line matches what the game says when you try to use the station directly. This is the thesis of this doc in miniature: the roof requirement is invisible until you walk into it, and then nothing tells you what happened.

Vanilla's own "You are carrying too much" was also still printing under the encumbered slab. Encumbered is a real status effect with its own start message, but it is triggered from the weight threshold rather than from LoudMoments' table, so the suppression list never covered it. It does now.
