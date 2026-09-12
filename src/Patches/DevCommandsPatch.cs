using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace BoneAndEmber.Patches
{
    // Dev-only console commands for testing the HUD without hunting for rain,
    // mountains or ingredients. Registered once after vanilla builds its own
    // command table, gated at run time by the DevCommands config toggle (off by
    // default) so they can be flipped live without a restart. Client-only:
    // everything touches the local player's own state, the same as eating or
    // getting rained on would.
    //
    //   bae_status <name>   toggle a status effect (add if missing, remove if present)
    //   bae_food            eat one of each of three test foods from the ObjectDB
    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    internal static class DevCommandsPatch
    {
        private static bool _registered;

        // Preferred test foods, in order; anything with m_food > 0 fills the gaps.
        private static readonly string[] PreferredFoods = { "Raspberry", "Mushroom", "CookedMeat", "Honey", "Blueberries", "NeckTail" };

        private static void Postfix()
        {
            if (_registered) return;
            _registered = true;

            new Terminal.ConsoleCommand(
                "bae_status",
                "[name] BoneAndEmber dev: toggle a status effect on yourself (add if missing, remove if present)",
                ToggleStatus,
                isCheat: false, isNetwork: false, onlyServer: false, isSecret: false,
                allowInDevBuild: true, hideBehindDevCommands: false,
                optionsFetcher: StatusNames);

            new Terminal.ConsoleCommand(
                "bae_food",
                "BoneAndEmber dev: eat one of each of three test foods so the segments show",
                EatTestFoods,
                isCheat: false, isNetwork: false, onlyServer: false, isSecret: false,
                allowInDevBuild: true, hideBehindDevCommands: false);

            new Terminal.ConsoleCommand(
                "bae_radial",
                "BoneAndEmber dev: toggle the radial restyle on and off for an A/B look",
                ToggleRadial,
                isCheat: false, isNetwork: false, onlyServer: false, isSecret: false,
                allowInDevBuild: true, hideBehindDevCommands: false);

            new Terminal.ConsoleCommand(
                "bae_sense",
                "BoneAndEmber dev: toggle the sense overlay on and off without opening Configuration Manager",
                ToggleSense,
                isCheat: false, isNetwork: false, onlyServer: false, isSecret: false,
                allowInDevBuild: true, hideBehindDevCommands: false);

            new Terminal.ConsoleCommand(
                "bae_items",
                "[text] BoneAndEmber dev: list item and buildable-piece prefab names matching text, with the name the game shows for each, because spawn wants the prefab name and the UI shows the other one",
                ListItems,
                isCheat: false, isNetwork: false, onlyServer: false, isSecret: false,
                allowInDevBuild: true, hideBehindDevCommands: false);

            new Terminal.ConsoleCommand(
                "bae_wear",
                "[percent] BoneAndEmber dev: wear every damageable item you carry down to this percent durability (default 30) so repair has something to fix",
                WearItems,
                isCheat: false, isNetwork: false, onlyServer: false, isSecret: false,
                allowInDevBuild: true, hideBehindDevCommands: false);

            new Terminal.ConsoleCommand(
                "bae_pin",
                "BoneAndEmber dev: lock the inventory highlight on the last hovered slot so it can be screenshotted",
                TogglePin,
                isCheat: false, isNetwork: false, onlyServer: false, isSecret: false,
                allowInDevBuild: true, hideBehindDevCommands: false);

            Plugin.Log.LogInfo("dev: registered console commands bae_status, bae_food, bae_radial, bae_sense, bae_items, bae_wear and bae_pin (gated by DevCommands config).");
        }

        private static bool Gate(Terminal.ConsoleEventArgs args, out Player player)
        {
            player = Player.m_localPlayer;
            if (!Plugin.DevCommands.Value)
            {
                Say(args, "BoneAndEmber dev commands are off. Turn on DevCommands in Configuration Manager (Fn+F1).");
                return false;
            }
            if (player == null)
            {
                Say(args, "No local player.");
                return false;
            }
            return true;
        }

        private static void ToggleStatus(Terminal.ConsoleEventArgs args)
        {
            Player player;
            if (!Gate(args, out player)) return;

            if (args.Length < 2)
            {
                Say(args, "Usage: bae_status <name>   (tab completes; e.g. Wet, Cold, Freezing, Rested)");
                return;
            }

            string name = args[1];
            int hash = name.GetStableHashCode();
            SEMan seman = player.GetSEMan();

            if (seman.HaveStatusEffect(hash))
            {
                bool removed = seman.RemoveStatusEffect(hash);
                Say(args, (removed ? "Removed " : "Could not remove ") + name);
                Plugin.Log.LogInfo("dev: bae_status " + name + " -> " + (removed ? "removed" : "remove failed"));
                return;
            }

            StatusEffect added = seman.AddStatusEffect(hash, resetTime: true);
            if (added == null)
            {
                Say(args, "Unknown status effect \"" + name + "\". Names are case-sensitive asset names, tab to list them.");
                Plugin.Log.LogWarning("dev: bae_status " + name + " -> not found in ObjectDB");
                return;
            }

            Say(args, "Added " + name + (added.m_ttl > 0f ? " (" + Mathf.RoundToInt(added.m_ttl) + "s)" : " (no timer)"));
            Plugin.Log.LogInfo("dev: bae_status " + name + " -> added, hash=" + hash + " ttl=" + added.m_ttl + " icon=" + (added.m_icon != null));
        }

        private static void EatTestFoods(Terminal.ConsoleEventArgs args)
        {
            Player player;
            if (!Gate(args, out player)) return;

            ObjectDB db = ObjectDB.instance;
            if (db == null)
            {
                Say(args, "ObjectDB not ready.");
                return;
            }

            // Index every edible item by prefab name.
            Dictionary<string, ItemDrop> foods = new Dictionary<string, ItemDrop>();
            for (int i = 0; i < db.m_items.Count; i++)
            {
                GameObject go = db.m_items[i];
                if (go == null) continue;
                ItemDrop drop = go.GetComponent<ItemDrop>();
                if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null) continue;
                if (drop.m_itemData.m_shared.m_food <= 0f) continue;
                foods[go.name] = drop;
            }

            List<ItemDrop> picks = new List<ItemDrop>();
            for (int i = 0; i < PreferredFoods.Length && picks.Count < 3; i++)
            {
                ItemDrop drop;
                if (foods.TryGetValue(PreferredFoods[i], out drop)) picks.Add(drop);
            }
            foreach (KeyValuePair<string, ItemDrop> kv in foods)
            {
                if (picks.Count >= 3) break;
                if (!picks.Contains(kv.Value)) picks.Add(kv.Value);
            }

            if (picks.Count == 0)
            {
                Say(args, "No edible items found in ObjectDB.");
                Plugin.Log.LogWarning("dev: bae_food -> no foods in ObjectDB (" + db.m_items.Count + " items scanned)");
                return;
            }

            int eaten = 0;
            for (int i = 0; i < picks.Count; i++)
            {
                ItemDrop drop = picks[i];
                // EatFood reads item.m_dropPrefab.name; on a raw prefab that field
                // may be unset, so eat a clone with it filled in, not the prefab's data.
                ItemDrop.ItemData data = drop.m_itemData.Clone();
                data.m_dropPrefab = drop.gameObject;
                data.m_stack = 1;

                string shown = Localization.instance.Localize(data.m_shared.m_name);
                bool ok = player.EatFood(data);
                if (ok) eaten++;

                string line = (ok ? "Ate " : "Skipped ") + shown + " (" + drop.gameObject.name + ", +" + data.m_shared.m_food +
                              " hp, " + Mathf.RoundToInt(data.m_shared.m_foodBurnTime) + "s)" +
                              (ok ? "" : ": already eaten and not yet half-burned, or stomach full");
                Say(args, line);
                Plugin.Log.LogInfo("dev: bae_food -> " + line);
            }

            Say(args, eaten + " of " + picks.Count + " eaten. Use 'puke' (devcommands) or wait to clear.");
        }

        // spawn takes a prefab name ("AxeStone"), the inventory shows a
        // localized display name ("Stone axe"), and nothing in game maps one to
        // the other. Searching both and printing the pair is the whole point.
        private static void ListItems(Terminal.ConsoleEventArgs args)
        {
            Player player;
            if (!Gate(args, out player)) return;

            ObjectDB db = ObjectDB.instance;
            if (db == null)
            {
                Say(args, "ObjectDB not ready.");
                return;
            }

            string filter = args.Length >= 2 ? args[1] : "";
            string needle = filter.ToLowerInvariant();
            int shown = 0;
            const int Max = 40;

            for (int i = 0; i < db.m_items.Count; i++)
            {
                GameObject go = db.m_items[i];
                if (go == null) continue;
                ItemDrop drop = go.GetComponent<ItemDrop>();
                if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null) continue;

                string prefab = go.name;
                string display = Localization.instance.Localize(drop.m_itemData.m_shared.m_name);

                if (needle.Length > 0 &&
                    !prefab.ToLowerInvariant().Contains(needle) &&
                    !display.ToLowerInvariant().Contains(needle))
                {
                    continue;
                }

                if (shown >= Max)
                {
                    Say(args, "... more matches; narrow the search.");
                    break;
                }

                Say(args, "spawn " + prefab + "   (" + display + ")");
                shown++;
            }

            // Pieces (workbench, forge, chest, portal) are not ItemDrops, so
            // they are nowhere in ObjectDB. They live in ZNetScene's prefab
            // list, and spawn takes them just the same.
            if (needle.Length > 0 && ZNetScene.instance != null)
            {
                int pieces = 0;
                for (int i = 0; i < ZNetScene.instance.m_prefabs.Count && shown < Max; i++)
                {
                    GameObject go = ZNetScene.instance.m_prefabs[i];
                    if (go == null || go.GetComponent<ItemDrop>() != null) continue;

                    Piece piece = go.GetComponent<Piece>();
                    if (piece == null) continue;

                    string display = piece.m_name != null ? Localization.instance.Localize(piece.m_name) : "";
                    if (!go.name.ToLowerInvariant().Contains(needle) &&
                        !display.ToLowerInvariant().Contains(needle))
                    {
                        continue;
                    }

                    if (pieces == 0) Say(args, "-- buildable pieces --");
                    Say(args, "spawn " + go.name + "   (" + display + ")");
                    pieces++;
                    shown++;
                }
            }

            if (shown == 0) Say(args, "No items or pieces match \"" + filter + "\".");
        }

        // Vanilla has no way to damage your own gear from the console, and
        // wearing an axe down by hand takes a few hundred swings, so repair had
        // nothing testable. Durability is the local player's own inventory
        // data, same as eating, so this stays client-only.
        private static void WearItems(Terminal.ConsoleEventArgs args)
        {
            Player player;
            if (!Gate(args, out player)) return;

            float percent = 30f;
            if (args.Length >= 2)
            {
                float parsed;
                if (float.TryParse(args[1], out parsed)) percent = Mathf.Clamp(parsed, 0f, 100f);
            }

            List<ItemDrop.ItemData> items = player.GetInventory().GetAllItems();
            int worn = 0;
            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData item = items[i];
                if (item == null || item.m_shared == null || !item.m_shared.m_useDurability) continue;

                item.m_durability = item.GetMaxDurability() * (percent / 100f);
                worn++;
            }

            Say(args, worn == 0
                ? "Nothing you are carrying uses durability."
                : "Worn " + worn + " item(s) down to " + Mathf.RoundToInt(percent) + "%. Open the inventory to see the repair line.");
            Plugin.Log.LogInfo("dev: bae_wear " + percent + " -> " + worn + " item(s) damaged.");
        }

        private static List<string> StatusNames()
        {
            List<string> names = new List<string>();
            ObjectDB db = ObjectDB.instance;
            if (db == null) return names;
            for (int i = 0; i < db.m_StatusEffects.Count; i++)
            {
                if (db.m_StatusEffects[i] != null) names.Add(db.m_StatusEffects[i].name);
            }
            return names;
        }

        private static void Say(Terminal.ConsoleEventArgs args, string text)
        {
            if (args != null && args.Context != null) args.Context.AddString(text);
        }
        private static void TogglePin(Terminal.ConsoleEventArgs args)
        {
            if (!Plugin.DevCommands.Value)
            {
                args.Context.AddString("bae_pin: DevCommands is off. Turn it on in Configuration Manager first.");
                return;
            }

            string result = GridHover.TogglePin();
            args.Context.AddString(result);
            Plugin.Log.LogInfo("dev: " + result);
        }

        // Sense has no build step to redo: SenseOverlay reads the config every
        // frame and switches itself off, so this takes effect immediately.
        private static void ToggleSense(Terminal.ConsoleEventArgs args)
        {
            if (!Plugin.DevCommands.Value)
            {
                args.Context.AddString("bae_sense: DevCommands is off. Turn it on in Configuration Manager first.");
                return;
            }

            Plugin.SenseEnabled.Value = !Plugin.SenseEnabled.Value;
            string state = Plugin.SenseEnabled.Value ? "on" : "off";
            args.Context.AddString("bae_sense: sense is now " + state + ". Stand still for " +
                                   Plugin.SenseDelay.Value.ToString("0") + " ms to bring it up.");
            Plugin.Log.LogInfo("dev: bae_sense -> SenseEnabled=" + Plugin.SenseEnabled.Value);
        }

        // The radial rebuilds its elements on every open, so flipping this and
        // reopening is enough to see the difference; nothing needs reloading.
        private static void ToggleRadial(Terminal.ConsoleEventArgs args)
        {
            if (!Plugin.DevCommands.Value)
            {
                args.Context.AddString("bae_radial: DevCommands is off. Turn it on in Configuration Manager first.");
                return;
            }

            Plugin.RadialEnabled.Value = !Plugin.RadialEnabled.Value;
            string state = Plugin.RadialEnabled.Value ? "on" : "off";
            args.Context.AddString("bae_radial: radial restyle is now " + state + ". Reopen the radial to see it.");
            Plugin.Log.LogInfo("dev: bae_radial -> RadialEnabled=" + Plugin.RadialEnabled.Value);
        }

    }
}
