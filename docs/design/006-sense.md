# 006: Sense

## The moment
You kill a boar, it drops hide, and the hide lands in a fern. You know it is there. You walk in circles with the crosshair pointed at the ground looking for a hover prompt to light up. Same thing after a fight in a forest with four spent arrows on the ground. Same thing when you drop your pack at a crafting station to make room and then cannot find the axe you set down ten seconds ago.

Valheim gives you no way to look at the ground. Every object on it is a hover prompt you have to walk your crosshair onto, one at a time, at melee range. So players sweep the world with the camera like a metal detector, or they just lose things.

## What it should feel like
Like the character noticing, not like a UI mode. You stop moving, there is a beat, and then the ground quietly reports. Nothing to press, nothing to dismiss, nothing to remember. Start walking and it forgets.

Stillness is the whole input. That is the design: the game already knows when you have stopped, and stopping is already the moment you look around.

## Activation
Horizontal speed under 0.1 m/s continuously for `Sense.Delay` (800ms) turns sense on. Speed comes from a rolling 0.2 second window of horizontal position change, not a single frame, so one frame of jitter can neither trip it nor hold it open.

Camera movement does not cancel. Looking around while standing still is exactly when you want this.

Moving does not cancel it either, not right away. What you sensed freezes and rides along with you, correctly placed over the world, for `Sense.MoveGrace` (2.5s) of continuous movement. Then it fades over a slow second and a half. Stop again at any point and it snaps back to full and starts scanning again.

That grace is the difference between a tool and a tic. Killing it on the first step teaches you to stand still again just to look, which is a worse loop than the one this was meant to fix.

Occlusion gets no grace. It keeps running while the set is frozen, so walking behind a rock still hides what is behind it.

## The wave
A marker at distance d appears when the reveal wave passes d, 0 to `Sense.Range` over 400ms, so what you see is something leaving your feet rather than forty things switching on at once.

The wave is timing, not a drawing. The first build drew it as a ring on the ground and it read as a stray white stroke across the screen. A constant-width bright line projected at a grazing angle always will: most of the circle lands far away and the near arc sweeps the whole frame. Bumpy terrain made the kinks visible but was never the root cause. The reveal order carries the same feeling on its own, so the ring is cut.

Drawing it properly, Returnal style, means a shader washing over world geometry off the depth buffer. That needs an AssetBundle built in a matching Unity Editor and shipped with the mod, which is a toolchain this project does not have and would not add for one effect.

## Markers
A narrow downward spike, about 8px wide and 14 tall, whose point lands on the item's world position offset up 0.3m. The body stands above the thing rather than covering it, and the point says which thing. Every marker sits on a dark grown copy of the same shape.

The shape matters more than the colour here. Two earlier builds used a diamond and it kept losing to the weather, because Valheim's own rain, mist and embers are themselves small bright diamonds and squares. A marker cannot win a fight it entered on the particle system's terms. Tall, sharp and pointing down is an instruction, not a mote, and nothing in the game emits it.

- **Dropped items** are a solid **gold** spike at full alpha. Within 5m they take the item's localized name above them, 12px bone at 70%. Above, because below would put the name over the item the spike is pointing at.
- **Plants and rocks** are a **hollow bone** spike at 55% of that alpha, and never take a name. In a forest or on a beach they are the majority of what is nearby, and they are texture, not information. Your loot has to read first. Hollow versus solid means the two weights differ in shape and not only in brightness, which survives a dark night and a bright snowfield alike. The outline is deliberately one pixel: the spike narrows toward its point, and a thicker stroke closes the interior up until the hollow version stops reading as different from the solid one.
- **Ember** overrides the colour either way if the material is not in your known materials list, so something you have never picked up before reads differently from the thousandth raspberry.
- Alpha ramps from 1.0 at 0 to 5m down to 0.35 at `Sense.Range`, and a marker you have walked well past drops out on its own.
- Never through walls. A marker whose item is not visible from the camera is not drawn at all.
- Cap 40, nearest first.

## Suppressed while
Any GUI is open, riding, sailing, swimming, dead, in a cutscene, teleporting, or in combat. If a suppress condition starts while sense is up, it fades out immediately instead of waiting for you to move.

**In combat** means both of these at once:
1. A weapon is drawn, and
2. an enemy within 15m is targeting you.

Both halves get logged separately, because the second one is the fragile one. Vanilla only reports an AI's target reliably on the client that owns that AI, so the check reads the networked "have target" flag as well as the local one.

## States
- **Nothing nearby:** the ring still plays, no markers. The pulse is the answer.
- **Overflowing:** the 40 nearest, everything past that silently dropped.
- **Mouse vs controller:** identical. Stillness is the only input, so there is nothing to bind and nothing for the pad to reach.
- **Motion off:** no wave, no fades. Markers snap on at activation and off at deactivation, for an exact A/B.
- **Main menu, loading, dead:** the root lives under `hudroot`, which vanilla already hides for cutscenes and the Ctrl+F3 hide. Death and the GUI cases are checked explicitly.
- **With Minimal UI:** nothing here touches a vanilla RectTransform. It is a new overlay on the HUD canvas, so there is nothing to fight over.

## Config
`Sense.Enabled`, `Sense.Delay`, `Sense.Range`, `Sense.ShowNames`, `Sense.MaxMarkers`, `Sense.MoveGrace`, `Sense.MarkerSize`.

## Build slices
1. Stillness, suppression, scan, occlusion, pooled two-weight markers, names, ember for unknown materials, the ground ring, `bae_sense`, logging. **Built and tested.**
2. From that test: ring cut, movement grace and slow decay, spike markers that read against weather. **Done.**
3. Possible, if it earns it: ore veins and item stands.

## Out of scope
Ore veins and `MineRock`, which are a different component family from `Pickable`. Item stands (`PickableItem`). Anything that changes pickup itself. Any persistence: sense remembers nothing between activations.

## Tested in game
2026-09-11, slice 1. It works: markers appear, the two weights read, occlusion holds. Three things came back.

- **The ground ring looked bad and is cut.** See "The wave" above. Geo read it as a terrain smoothness problem; it was really that a thin projected line at a grazing angle crosses the whole frame and reads as a stray stroke no matter what the ground does.
- **Losing the markers the moment you moved made him want to stop again just to see them.** That is the feature training a bad habit. Hence `Sense.MoveGrace`: the set rides along while you walk and decays only after a couple of seconds of it. Started at 3.5s, dropped to 2.5s on feel: long enough that a step does not punish you, short enough that it does not linger into the next thing you are doing.
- **Bone diamonds blended into snow and rain.** Both are small bright specks, which is exactly what the markers were. First fix was a dark backing, gold for drops, and hollow versus solid.

2026-09-12, slice 2 part way. The backing and the two weights landed, but the diamond still read as weather: the game emits small blue diamonds and squares of its own, so shape was the axis that mattered and the marker was on the wrong side of it. Markers are now downward spikes that point at the thing they mark. Tested and kept: the spike reads as an instruction rather than a mote, and the point resolves which item in a cluster it means.
