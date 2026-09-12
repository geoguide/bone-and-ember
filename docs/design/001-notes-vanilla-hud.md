# 001 notes: how vanilla draws the HUD

Research for slice 1 of `001-always-on-hud.md`. Read from
`reference/decompiled/assembly_valheim/`, `assembly_guiutils/`, and
`minimalui/`. Line numbers are from that decompile and will drift after a
Valheim update, so re-run `tools/decompile.sh` and re-grep the method name
rather than trusting a number.

Nothing here is Iron Gate's code. It is a description of it.

## Shape of the thing

There is one class, `Hud`, and one `Update()` at `Hud.cs:467` that drives every
piece of the player HUD. No events, no dirty flags, no callbacks. Each frame it
checks `Player.m_localPlayer` once, then calls a private method per HUD element:

    UpdateStatusEffects, UpdateGuardianPower, UpdateFood, UpdateHealth,
    UpdateStamina, UpdateAdrenaline, UpdateEitr, UpdateStealth,
    UpdateCrosshair, UpdateEvent, UpdateActionProgress, UpdateStagger,
    UpdateMount

Every one of them takes the `Player` directly. They only run when a local player
exists. That makes each a good postfix target: we get the same data at the same
moment vanilla does, without re-deriving anything or running our own timer.

`Hud.instance` is a static, set in `Awake` and nulled in `OnDestroy`. It exists
only inside a game session, so anything we cache off it has to be cleared the
same way.

## Health

Draw: `Hud.UpdateHealth(Player)`, `Hud.cs:1081`. Short method. It sizes the bar
from max health, pushes the value into two bars, writes the number.

Fields on Hud:

- `m_healthBarRoot` (RectTransform), `m_healthPanel` (RectTransform)
- `m_healthBarFast`, `m_healthBarSlow` (GuiBar)
- `m_healthText` (TMP_Text), `m_healthAnimator` (Animator)

Data:

- `Player.GetHealth()` and `Player.GetMaxHealth()`, both from `Character`
  (`Character.cs:2992` and `3059`).
- Health is stored in the ZDO, not a plain field. Read it through the getters.
  Never write it, that is a network write and breaks the client-only rule.

Sizing: vanilla's bar is not a fixed width. `SetHealthBarSize` (`Hud.cs:982`)
sets it to `maxHealth / 25 * 32` pixels, so the bar physically grows as food
raises your max. Same formula for stamina, eitr and the food bar.

Slice 2 deliberately does not copy this. Cloning the formula gave a ~40px stub
at starting health, and a bar that resizes as you eat shifts anything anchored
near it. Our bar uses a fixed width instead (`BarWidth` config, default 320px);
`GuiBar` already fills proportionally from `value / maxValue`, so the frame
stays put and only the fill moves. Food segments in slice 3 divide that fixed
width rather than stretching it. See the "Bar width" note in
`001-always-on-hud.md` for the reasoning.

The damage trail is already there. `GuiBar`
(`assembly_guiutils/GuiBar.cs`) has `m_smoothDrain`, `m_smoothFill`,
`m_smoothSpeed` and `m_changeDelay`, and animates its own width in `LateUpdate`.
Vanilla runs two of them over each other: `fast` snaps to the new value, `slow`
lags behind and drains. That is exactly the "light trail holds the lost chunk"
effect in the spec. Cloning the vanilla bar root gets it for free, so slice 4 is
mostly tuning `m_changeDelay` and recoloring the slow bar.

`Hud.FlashHealthBar()` is public and gets called from `Player.SetMaxHealth`
whenever food changes your maximum. Useful trigger for the slice 3 shrink moment.

## Stamina

Draw: `Hud.UpdateStamina(Player, float dt)`, `Hud.cs:1094`.

Fields: `m_staminaBar2Root`, `m_staminaBar2Fast`, `m_staminaBar2Slow`,
`m_staminaText`, `m_staminaAnimator`, `m_staminaHideTimer`.

Data: `Player.GetStamina()` and `Player.GetMaxStamina()` (`Player.cs:4425`),
plain fields, cheap.

The auto-hide the spec asks for already exists in vanilla form. `UpdateStamina`
keeps `m_staminaHideTimer`, resetting it to zero whenever stamina is below max
and counting up otherwise, then drives an Animator bool named `Visible` while the
timer is under one second. So "hidden when full, fades in while spending, fades
out about a second after" is vanilla behavior we can copy the timing of.

It also repositions itself: y=320 while the build menu or ship HUD is open, y=130
otherwise. Our bar needs the same dodge or it will sit under the build UI.

Eitr is the identical pattern (`UpdateEitr` at `Hud.cs:1163`, `m_eitrBarRoot`,
`Player.GetEitr()` / `GetMaxEitr()`), which confirms slice 5 can reuse whatever
we build for stamina.

## Food, and where max health actually comes from

This is the part the spec had as an open question, and the answer changes the
design a little.

`Player.Food` (`Player.cs:22`) is a small class:

- `m_item` (the ItemData, which carries the icon and `m_shared.m_foodBurnTime`)
- `m_time`, how much burn time is left
- `m_health`, `m_stamina`, `m_eitr`, this food's current contribution
- `CanEatAgain()`, true once `m_time` drops below half the burn time

You can have three at once, held in `Player.m_foods` (`Player.cs:338`), read via
`GetFoods()` (`Player.cs:2497`).

`Player.UpdateFood` (`Player.cs:2424`) ticks each food down, then recomputes its
contribution as the food's full value scaled by:

    pow(remaining / burnTime, 0.3)

Then `GetTotalFoodValue` (`Player.cs:2479`) adds it up as `m_baseHP` (25, at
`Player.cs:199`) plus each food's current `m_health`, and hands the total
straight to `SetMaxHealth(hp, flashBar: true)`.

Two consequences for the design:

1. A food's share of your max health decays continuously and non-linearly. The
   0.3 exponent means it falls off fast at first and then very slowly, so a
   segment spends most of its life shrinking gently and is still worth roughly
   two thirds of its value at half time. It does not sit flat and then fall off
   a cliff. So the segment should visibly shrink the whole time, and the
   dramatic moment is not expiry.
2. The moment worth calling out is `CanEatAgain()`, at half burn time. That is
   when the game will let you top the food back up, and it is the actionable
   cue. Vanilla already pulses the food icon for it (`Hud.cs:1028`). Our pulse
   and caption should fire there rather than in the last few seconds.

Display timing: `m_time` is in food-seconds. Vanilla divides by `Game.m_foodRate`
before showing it (`Hud.cs:1037`), shows whole minutes above 60 and pulsing
seconds below. Worth matching, and worth noting Minimal UI's food bar forgets the
`m_foodRate` divide.

Hud fields for food: `m_foodBars`, `m_foodIcons`, `m_foodTime` (arrays of three),
`m_foodBarRoot`, `m_foodBaseBar`, `m_foodIcon`, `m_foodText`. The segment
arithmetic we want for slice 3 is already written in `Hud.UpdateFood`
(`Hud.cs:1012`), it is just drawn as small bars instead of divisions of the
health bar.

## Status effects

Draw: `Hud.UpdateStatusEffects(List<StatusEffect>)`, `Hud.cs:1635`, fed each
frame from `Player.GetSEMan().GetHUDStatusEffects(list)` (`SEMan.cs:324`), which
returns every effect that has an icon and is not marked hidden.

Per effect (`StatusEffect.cs`):

- `m_name` (localization key), `m_icon` (Sprite), `m_category`, `m_tooltip`
- `m_flashIcon`, `m_cooldownIcon`, `m_hidden`, `m_isNew`
- `GetIconText()` at `:209`, the countdown already formatted
- `GetRemaningTime()` at `:204` (their spelling), which is `m_ttl - m_time`
- `NameHash()` at `:355`, a stable id
- `HaveAttribute()` for things like IsFood, plus the whole Modify* family

An effect with no ttl, like Wet from standing in rain, returns an empty countdown
string. Chips need to handle "no timer" as a normal state, not an error.

Rows are clones of `m_statusEffectTemplate` parented to `m_statusEffectListRoot`,
laid out `m_effectsPerRow` (7) across at `m_statusEffectSpacing` (55) apart. Child
objects are named `Icon`, `Cooldown`, `Name` and `TimeText`, plus an Animator that
takes a `flash` trigger when `m_isNew` is set.

Two quirks to design around in slice 6:

- The list is only rebuilt when the count changes. The rebuild path strips hidden
  effects out of the list it was handed, while the per-frame refresh path merely
  skips them, so the two can disagree about indices for a frame.
- Identity is the list index and nothing else, so an effect that ends can shift
  every chip after it. Key our chips off `NameHash()` if we want them to animate
  in and out independently.

## Object names in the prefab

Taken from Minimal UI's path lookups, which walk the same hierarchy:

    hudroot
      healthpanel
        Health          vanilla health bar group
        healthicon
        darken
        Food            m_foodBarRoot, with child baseBar
        food0, food1, food2
        foodicon, foodicon (1)
      staminapanel
      eitrpanel
      GuardianPower     with Name, Icon, Bkg, TimeText
      HotKeyBar

A bar root (health, stamina, eitr) has children `fast`, `slow`, `bkg`, `border`,
with `fast/bar` inside and `fast/bar/HealthText` inside that.

Confirmed in slice 2, in game: `Hud.m_healthBarRoot` clones from
`hudroot/healthpanel/Health` and the health value was live and correct
(33/33) immediately.

Correction, also from slice 2, with Minimal UI parked: the bar still rendered
vertical, about 330px tall with tick marks. That ruled out Minimal UI as the
cause. The decompiled `Hud.cs` and `GuiBar.cs` have no rotation or scale in
them, that's Unity scene/prefab data, invisible to a code decompile, so the
source bar itself carries a baked rotation (and/or scale) we never saw coming.
We now clone, log the RectTransform of both the source and the clone (rotation,
scale, anchors, pivot, sizeDelta), then strip every transform in the clone back
to identity rotation and unit scale and size it explicitly, rather than trust
what the prefab handed us. See the "Geometry" note in `001-always-on-hud.md`.

## Minimal UI is already sitting on all of this

`Azumatt.MinimalUI` 2.3.12, decompiled to `reference/decompiled/minimalui/`.

It does not restyle the vanilla bars. It clones them, hides the originals, and
drives the clones from postfixes. On default settings, which is what is installed:

- `Hud.Awake` postfix calls `CustomBars.Create()`, which builds `MUI_HPBar`,
  `MUI_StaminaBar`, `MUI_EitrBar`, `MUI_GuardianPowerBar` and `MUI_FoodBar` as
  clones parented to `hudroot`.
- Custom health, stamina and food bars all default to On. Health and stamina
  default to 90 degrees of rotation. That vertical bar with the red number box in
  the bottom left is Azumatt's clone, not vanilla.
- It hides vanilla by disabling `hudroot/staminapanel` outright, and by disabling
  the children `healthpanel/Health`, `healthicon`, `food0..2` and
  `foodicon (1)`.
- Its `ToggleFoodHealthPanel` re-enables `healthpanel` itself any time it finds it
  inactive. So `healthpanel`'s own active flag is contested ground.
- It postfixes `Hud.UpdateHealth`, `UpdateStamina`, `UpdateFood`,
  `UpdateStatusEffects`, `FlashHealthBar`, `StaminaBarEmptyFlash` and
  `Player.OnDamaged`, and moves `m_statusEffectListRoot` by (-60, +10) at 0.75
  scale.
- Everything it does is a postfix. Nothing blocks another mod postfixing the same
  method.

So the overlap is real but narrow. It is entirely about which GameObjects are
visible, never about the update methods.

### How we coexist

We do not touch `activeSelf` on anything Minimal UI writes. Instead we add a
`CanvasGroup` to each subtree we want gone and set `alpha = 0` with
`blocksRaycasts = false`.

Alpha 0 on a parent hides the whole subtree regardless of any child's active
flag, nothing in vanilla or Minimal UI reads or writes alpha on these objects,
and restoring is a single assignment. Both mods can hold an opinion about the
same object without overwriting each other.

Timing note: Minimal UI's `Hud.Awake` postfix carries `[HarmonyPriority(0)]`,
which means it runs last, so its clones do not exist yet when our own `Awake`
patch runs. Anything that needs to see `MUI_HPBar` has to happen a frame later,
which is why our visibility pass lives in a `LateUpdate` rather than in `Awake`.

Correction from slice 2, in game: `Transform.Find("MUI_HPBar")` on
`m_rootObject.transform` came back empty on Geo's install, even after waiting
several seconds for Minimal UI's postfix to run. `Find` with a bare name only
checks direct children, so if `CustomBars.Create()` parents its clones one level
deeper than `HudrootTransform` (the decompile shows a direct instantiate onto
`HudrootTransform`, but that is one version's behavior and Geo's installed
version, or its config, may differ), a shallow lookup misses it with no error.
We now search the whole Hud hierarchy with `GetComponentsInChildren<Transform>`
and log the full path of whatever we find, so a wrong guess about where Minimal
UI parents its clones shows up in the log instead of as an unhidden bar. The
one-time cost of a recursive search is fine for the few seconds it runs before
giving up.
