# 005: Forsaken Power

## The moment
You killed Eikthyr, touched the stone, and now there is a power on you. In vanilla it lives in a small corner widget: a sigil, the name, and a countdown. With the always-on HUD (001) that widget now sits on top of the stamina bar, which is how Geo noticed it exists at all.

Three things are wrong with it beyond the collision. The cooldown is 20 minutes and the widget counts it down in seconds, which is noise you learn to ignore. When the power becomes ready, nothing happens, so most players forget they have it. And activation, the one moment in this loop with any drama, gets no acknowledgement on screen.

## What it should feel like
A charged thing you are carrying. Calm while it sits there, so it does not compete with health and stamina. Loud for the two seconds after you press the button, in the Persona sense: the boss name, big, gone again before it gets in the way.

## Direction (decided)
Direction A of three that were mocked in chat: a **power line** as the fourth row of the cluster, under stamina, plus the loud slab on activation. Rejected: a permanent first chip (a fixed thing in a row of transient things, and the row wraps around it) and a large emblem with a ring to the left of the bars (striking, but it breaks the one-grid rule and adds a new visual vocabulary).

## How vanilla does it
- The power is a `StatusEffect` asset (`GP_Eikthyr`, `GP_TheElder`, `GP_Bonemass`, `GP_Moder`, `GP_Yagluth`, `GP_Queen`, `GP_Ashlands`, `GP_DeepNorth`). The asset carries the sigil, the name, the effect text, the active duration (`m_ttl`) and the cooldown length (`m_cooldown`).
- `Player.GetGuardianPowerHUD` returns the chosen effect and the remaining cooldown. `Hud.UpdateGuardianPower` runs every frame and is our postfix target.
- Pressing the button (F, D-pad down on a pad) plays the raise-arms animation, and an animation event applies the effect to every player within 10 m and starts the cooldown.
- While the buff is running it is a normal status effect, so today it also shows up as a dim chip in our row. Vanilla shows nothing special at activation beyond the effect's own start message.
- All of this is readable client-side. Nothing here touches the network.

## The line
Same header pattern as the bars: label left, value right, thin bar underneath. Lives under the stamina bar. Stamina fades when full but keeps its space, so the line has a fixed home and never moves.

- Header left: the boss sigil at 1em (the same size as a chip icon), then the boss name in the small caps label style, letterspaced, like HEALTH and STAMINA.
- Header right: the value, right-aligned to the bar's right edge, tabular digits.
- Bar: 4px tall, thinner than stamina's 8, so the three bars step down in weight. Same clipped right end, same dark track.
- Gap from the stamina bar to the power header: 8px, the same as health to stamina.

## States
Precedence top to bottom.

1. **No power chosen.** The line is absent. The cluster ends at stamina. This is every new character's first hours, so the absence has to be clean, not an empty row.
2. **Active.** The power's effect is currently on you. Label bone at full alpha. Value is the remaining time, m:ss, in gold. Bar gold, draining from full over the active duration. "On you" is decided by whether the effect is in your status effect list, not by whether you pressed the button, so a friend firing the same power near you reads as active too.
3. **Cooling down.** Label bone at 70%. Value is the remaining cooldown, m:ss, in bone at 60%, the same dim weight chips use for their timers. Bar bone at 70%, refilling from empty to full over the cooldown length. It is a resource recharging, the opposite of stamina draining, and the bar says that without the number needing to be read.
4. **Ready.** Value is the button glyph and READY in gold ("F  READY", or the pad's glyph on a controller). Bar full gold with a slow, low amplitude breathe, about half the food pulse. Calm but visibly alive, so it does not read as a dead full bar.

Time format stays m:ss to match the chips. If twenty minutes of ticking seconds reads as noise in game, switch to whole minutes above one minute.

## The two beats
**Activation.** The moment the effect lands on you, the loud slab from 001 plays: the boss name in caps as the title, the first line of the power's effect text as the subtitle. Same skew, same slide in from above and 2.5s hold. The plate is gold instead of ember, because this is a good moment, not a warning. Vanilla's own center message for the effect is suppressed so the two do not stack.

**Ready.** When the cooldown reaches zero: no slab. The bar snaps to gold with a 300ms brightness flash and the value crossfades to READY. Quiet on purpose. If it turns out to be too quiet to notice in play, promote it to a small slab.

## Before / after
Before: vanilla's widget in the corner, currently drawn over our stamina bar. After: https://claude.ai/code/artifact/e399b4bb-63d7-41a4-8bf0-01443966b4f8

```
  Wet 2:00   No skill drain 9:49
 HEALTH                    25 / 25
 ████████████████████████████████
 STAMINA                   39 / 50
 ███████████████████████░░░░░░░░░
 ᛉ EIKTHYR               F  READY     ready: gold, breathing
 ▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬

 ᛉ EIKTHYR                  18:24     cooling down: dim, refilling
 ▬▬▬▬▬▬▬▬░░░░░░░░░░░░░░░░░░░░░░░░

 ᛉ EIKTHYR                   4:32     active: gold, draining
 ▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬░░░░░░░░░░
```

## What gets hidden
- Vanilla's guardian power widget, by alpha, never by deactivating it, so Minimal UI and vanilla keep their references. Only while this feature is on, so the toggle is a real A/B.
- The active buff's chip, when it is your own power. A buff from someone else's different power still chips as usual.

## Controller vs mouse
Display only, except the button glyph in the Ready value, which must name the pad button when a pad is active. Vanilla's key token for the power button should do that for free. Log what it resolved to.

## With Minimal UI
Minimal UI builds its own clone of the widget. We hide vanilla's original only; if Minimal UI is ever re-enabled, its clone is its business, same as the health bars in 001.

## Config (Configuration Manager, F1)
- Power.Enabled (on; off restores vanilla's widget and the buff chip)
- Power.BarHeight (4)
- Power.ActivationSlab (on)
- Power.ReadyFlash (on)
- Layout.GapStaminaToPower (8)

## Build slices
1. The line and its four states, hiding vanilla's widget, chip dedup, logging on every state change.
2. The two beats: gold slab on activation, flash on ready.
3. Dev command to force a state for screenshots.
4. In-game tuning.

## Out of scope
What the power does, the 10 m share radius, the boss stone interaction, showing other players' powers, the inventory "Active effects" text page.

## Tested in game
Not yet.
