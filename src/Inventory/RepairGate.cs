using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BoneAndEmber
{
    // Slice 1 of docs/design/004-repair-and-buried-ux.md: the repair rules,
    // borrowed rather than copied.
    //
    // CanRepair, HaveRepairableItems and RepairOneItem are private on
    // InventoryGui, and their rules are fiddly (the item's recipe has to name
    // this station or its repair station, the station has to be at least the
    // recipe's minimum level, world level can override the whole thing). A
    // reimplementation would drift the first time Iron Gate touches any of
    // that, and a wrong "you can fix this here" is worse than no line at all.
    // So this calls vanilla's own methods and only owns the presentation.
    //
    // Repairing is free in vanilla: it sets durability to max and grants a
    // little Crafting skill, no materials. Durability lives in the player's
    // own inventory, not in a ZDO, so repeating vanilla's own repair is
    // exactly what the player pressing the button N times would do, and stays
    // client-only.
    internal static class RepairGate
    {
        // Safety net, not a real limit: the loop stops on HaveRepairableItems
        // going false. This only matters if a repair ever silently no-ops.
        private const int MaxPerPress = 64;

        // While true, StartMessageSuppressPatch swallows centre messages, so a
        // repair-all prints one summary instead of one line per item. Counted
        // rather than a plain flag: RepairEverything raises it too, and it can
        // be called while OnRepairPressed already holds it.
        internal static bool Suppressing => _suppressDepth > 0;
        private static int _suppressDepth;

        internal static void BeginSuppressing()
        {
            _suppressDepth++;
        }

        internal static void EndSuppressing()
        {
            if (_suppressDepth > 0) _suppressDepth--;
        }

        private static MethodInfo _canRepair;
        private static MethodInfo _haveRepairable;
        private static MethodInfo _repairOne;
        private static bool _resolved;
        private static bool _usable;

        private static readonly List<ItemDrop.ItemData> Worn = new List<ItemDrop.ItemData>();
        private static readonly HashSet<ItemDrop.ItemData> Repairable = new HashSet<ItemDrop.ItemData>();
        private static readonly object[] NoArgs = new object[0];
        private static readonly object[] OneArg = new object[1];

        internal static bool Resolve()
        {
            if (_resolved) return _usable;
            _resolved = true;

            _canRepair = AccessTools.Method(typeof(InventoryGui), "CanRepair", new[] { typeof(ItemDrop.ItemData) });
            _haveRepairable = AccessTools.Method(typeof(InventoryGui), "HaveRepairableItems");
            _repairOne = AccessTools.Method(typeof(InventoryGui), "RepairOneItem");
            _usable = _canRepair != null && _haveRepairable != null && _repairOne != null;

            if (!_usable)
            {
                Plugin.Log.LogError(
                    "repair: InventoryGui is missing one of CanRepair / HaveRepairableItems / RepairOneItem (" +
                    (_canRepair == null ? "CanRepair " : "") + (_haveRepairable == null ? "HaveRepairableItems " : "") +
                    (_repairOne == null ? "RepairOneItem" : "") +
                    "). The repair line is off for this session. Re-run tools/decompile.sh and check the names.");
            }
            return _usable;
        }

        internal static bool CanRepair(ItemDrop.ItemData item)
        {
            if (!Resolve() || item == null || InventoryGui.instance == null) return false;
            OneArg[0] = item;
            return (bool)_canRepair.Invoke(InventoryGui.instance, OneArg);
        }

        internal static bool HaveRepairableItems()
        {
            if (!Resolve() || InventoryGui.instance == null) return false;
            return (bool)_haveRepairable.Invoke(InventoryGui.instance, NoArgs);
        }

        // The set of worn items this station can actually fix right now, and
        // the worn count regardless. Refreshed on the line's own throttle, not
        // every frame: CanRepair reaches ObjectDB for a recipe per item.
        internal static void Rescan(out int repairable, out int worn)
        {
            Repairable.Clear();
            repairable = 0;
            worn = 0;

            Player player = Player.m_localPlayer;
            if (player == null || player.GetInventory() == null) return;

            Worn.Clear();
            player.GetInventory().GetWornItems(Worn);
            worn = Worn.Count;

            for (int i = 0; i < Worn.Count; i++)
            {
                if (!CanRepair(Worn[i])) continue;
                Repairable.Add(Worn[i]);
                repairable++;
            }
        }

        internal static bool IsRepairableHere(ItemDrop.ItemData item)
        {
            return item != null && Repairable.Contains(item);
        }

        // Mirrors CraftingStation.CheckUsable's own order (roof, then cover,
        // then fire) and reuses its localization keys, so the line says exactly
        // what the game says when you try to use the station itself.
        private static string UnusableReason(CraftingStation station, Player player)
        {
            if (station.m_craftRequireRoof && !player.NoCostCheat() && station.m_roofCheckPoint != null)
            {
                float cover;
                bool underRoof;
                Cover.GetCoverForPoint(station.m_roofCheckPoint.position, out cover, out underRoof);

                if (!underRoof) return Localization.instance.Localize("$msg_stationneedroof").ToUpperInvariant();
                if (cover < 0.7f) return Localization.instance.Localize("$msg_stationtooexposed").ToUpperInvariant();
            }

            if (station.m_craftRequireFire && !player.NoCostCheat())
            {
                return Localization.instance.Localize("$msg_needfire").ToUpperInvariant();
            }

            return null;
        }

        // "CLUB WORN" reads better than "1 ITEM WORN" when there is only one,
        // and answers "repair what?" outright.
        internal static string WornSummary(int wornCount)
        {
            if (wornCount <= 0) return "";
            if (wornCount == 1 && Worn.Count > 0 && Worn[0] != null && Worn[0].m_shared != null)
            {
                return Localization.instance.Localize(Worn[0].m_shared.m_name).ToUpperInvariant() + " WORN";
            }
            return wornCount + " ITEMS WORN";
        }

        // Vanilla's own repair, run until nothing is left (or until max items,
        // which is how the one-at-a-time config path reuses this). Returns how
        // many were fixed so the caller can print one line for all of them.
        internal static int RepairEverything()
        {
            return RepairUpTo(MaxPerPress);
        }

        internal static int RepairUpTo(int max)
        {
            if (!Resolve() || InventoryGui.instance == null) return 0;
            if (max > MaxPerPress) max = MaxPerPress;

            int done = 0;
            BeginSuppressing();
            try
            {
                while (done < max && HaveRepairableItems())
                {
                    _repairOne.Invoke(InventoryGui.instance, NoArgs);
                    done++;
                }
            }
            finally
            {
                EndSuppressing();
            }
            return done;
        }

        internal static void AnnounceRepaired(int count)
        {
            Player player = Player.m_localPlayer;
            if (player == null || count <= 0) return;

            player.Message(MessageHud.MessageType.Center,
                count == 1 ? "Repaired 1 item" : "Repaired " + count + " items");
            Plugin.Log.LogInfo("repair: repaired " + count + " item(s) in one press.");
        }

        // Why the player cannot repair here, in words, for the line to show.
        // Reads the same pieces CanRepair does so the reason matches the rule
        // that actually refused.
        internal static string BlockedReason(int wornCount)
        {
            Player player = Player.m_localPlayer;
            if (player == null) return null;

            // The caller hides the line entirely when nothing is worn, so every
            // reason below is prefixed with what there is to fix. "Repair at a
            // workbench" on its own left the obvious question unanswered: repair
            // what?
            string worn = WornSummary(wornCount);
            if (wornCount == 0) return null;

            CraftingStation station = player.GetCurrentCraftingStation();
            if (station == null && !player.NoCostCheat()) return worn + "  ·  REPAIR AT A WORKBENCH";
            if (station != null && !station.m_canRepair) return worn + "  ·  THIS STATION CANNOT REPAIR";

            // A station you are standing at can still refuse: vanilla wants a
            // workbench under a roof that is not too exposed, and a forge next
            // to fire. CheckUsable only says no, so re-run the same two tests
            // to say which, in vanilla's own words.
            if (station != null && !station.CheckUsable(player, showMessage: false))
            {
                string why = UnusableReason(station, player);
                if (why != null) return worn + "  ·  " + why;
            }

            // Something is worn, the station repairs, but nothing qualified.
            // The first worn item's recipe explains it well enough to act on.
            if (station != null && ObjectDB.instance != null)
            {
                for (int i = 0; i < Worn.Count; i++)
                {
                    Recipe recipe = ObjectDB.instance.GetRecipe(Worn[i]);
                    if (recipe == null) continue;

                    CraftingStation needed = recipe.m_repairStation != null ? recipe.m_repairStation : recipe.m_craftingStation;
                    if (needed == null) continue;

                    if (needed.m_name != station.m_name)
                    {
                        return worn + "  ·  NEEDS A " + Localization.instance.Localize(needed.m_name).ToUpperInvariant();
                    }
                    if (Mathf.Min(station.GetLevel(), 4) < recipe.m_minStationLevel)
                    {
                        return worn + "  ·  NEEDS " + Localization.instance.Localize(needed.m_name).ToUpperInvariant() +
                               " LVL " + recipe.m_minStationLevel;
                    }
                }
            }

            return worn + "  ·  CANNOT REPAIR HERE";
        }
    }
}
