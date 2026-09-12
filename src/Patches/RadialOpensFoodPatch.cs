using HarmonyLib;
using UnityEngine;
using Valheim.UI;

namespace BoneAndEmber.Patches
{
    // Opens the radial straight onto the consumables page instead of the main
    // group, when eating would actually do something.
    //
    // Postfix on OpenRadialConfig.InitRadialConfig, which is where vanilla
    // picks the group: emotes if OpenEmote is held, a hover menu if you are
    // looking at something that has one, otherwise the main group. We only
    // step in on that last case, and we do it by calling vanilla's own
    // radial.Open with the main group pushed as the back config, so B still
    // walks back out to the normal radial.
    //
    // No new group is created and no group is removed: "consumables" is the
    // same ItemGroupConfig name ValheimRadialConfig itself builds a button for.
    // Input is untouched.
    [HarmonyPatch(typeof(OpenRadialConfig), "InitRadialConfig")]
    internal static class RadialOpensFoodPatch
    {
        private static bool Prepare()
        {
            if (AccessTools.Method(typeof(OpenRadialConfig), "InitRadialConfig") != null) return true;

            Plugin.Log.LogError(
                "OpenRadialConfig.InitRadialConfig not found, the food-first radial is off. " +
                "Re-run tools/decompile.sh and check what it is called now.");
            return false;
        }

        private static void Postfix(RadialBase radial)
        {
            if (!Plugin.RadialEnabled.Value || !Plugin.RadialOpensFood.Value) return;
            if (radial == null || RadialData.SO == null) return;

            // Only override vanilla's default landing page. If it routed to
            // emotes or to a hover menu, that was a deliberate choice and we
            // leave it alone.
            if (!(radial.CurrentConfig is ValheimRadialConfig)) return;

            Player player = Player.m_localPlayer;
            if (player == null || player.IsDead()) return;
            if (!HasEdibleFood(player)) return;

            ItemGroupConfig food = Object.Instantiate(RadialData.SO.ItemGroupConfig);
            food.GroupName = "consumables";
            radial.Open(food, RadialData.SO.MainGroupConfig);
            Plugin.Log.LogInfo("radial: opened on the consumables group (food is worth eating right now).");
        }

        // Food in the pack that you could eat this second. Player.CanEat is
        // vanilla's own answer and already covers both cases that matter: a
        // free stomach slot, or an existing meal refreshable enough to top up.
        // An empty stomach is the main case for landing here, and CanEat says
        // yes to everything then, which an earlier "has a refreshable meal"
        // test got backwards.
        private static bool HasEdibleFood(Player player)
        {
            Inventory inventory = player.GetInventory();
            if (inventory == null) return false;

            System.Collections.Generic.List<ItemDrop.ItemData> items = inventory.GetAllItems();
            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData item = items[i];
                if (item == null || item.m_shared == null) continue;
                if (item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Consumable) continue;
                if (item.m_shared.m_food <= 0f && item.m_shared.m_foodStamina <= 0f && item.m_shared.m_foodEitr <= 0f) continue;

                if (player.CanEat(item, showMessages: false)) return true;
            }
            return false;
        }
    }
}
