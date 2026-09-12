# Dev console cheatsheet

Launch options end with `-console`. Open with Fn+F5, type `devcommands` once per session. Tab completes names.

## World
- `tod 0.5` noon, `tod 0.9` night, `tod -1` normal clock
- `env Rain`, `env SnowStorm`, `env Clear`; `resetenv` stops the override
- `skiptime 600` jumps 10 game minutes
- `sleep` skips to morning

## You
- `god` invincible, again to turn off
- `heal` full health and stamina
- `puke` empties your stomach
- `clearstatus` removes every status effect
- `addstatus Wet` / `Cold` / `Freezing` / `Rested` / `Burning`
- `debugmode`, then Z to fly, K to kill nearby

## Items
- `spawn Raspberry 5 p` spawns and picks up
- `spawn Club 1 e` spawns and equips
- `removedrops` clears items on the ground

## Ours (DevCommands toggle in Configuration Manager, Fn+F1)
- `bae_status <name>` toggles an effect on the local player
- `bae_food` eats three test foods
- `bae_items <text>` searches item and buildable-piece prefab names together with their display names, and prints a ready-to-paste `spawn` line for each match. `spawn` wants the prefab name ("AxeStone"), the inventory shows the display name ("Stone axe"), and nothing in game maps one to the other. Pieces (workbench, forge, chest) are not items and live in a different list, which is why this searches both.
- `bae_wear [percent]` wears everything you carry down to that durability (default 30) so the repair line has something to fix
- `bae_pin` locks the inventory highlight on the last slot you hovered, so hover states can be screenshotted
- `bae_radial` toggles the radial restyle for an A/B look
- `bae_sense` toggles the sense overlay (006). Stand still for about a second to bring it up. With DevCommands on it also logs the scan tally and the combat check every 2 seconds: `tools/log.sh sense`

## Notes
- Cold and Freezing follow the weather. `addstatus` on them may not stick; use `env`.
- `tools/log.sh BoneAndEmber` prints the mod log, `tools/log.sh "chips: effect"` filters it.
