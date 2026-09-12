using HarmonyLib;
using UnityEngine;

namespace BoneAndEmber.Patches
{
    // Feeds the detail card from whichever slot vanilla itself would be
    // tooltipping this frame, on both grids (the same InventoryGrid.UpdateGui
    // postfix point InventorySlotStylePatch uses, 003-notes.md "the container
    // view shares InventoryGrid"), independent of InventoryEnabled: this is
    // its own toggle (DetailCard) and should work even with the slot restyle
    // off, so it's a separate patch rather than folded into that one.
    //
    // InventoryGrid.GetHoveredElement() and the private m_selected/GetElement
    // that back GetGamepadSelectedItem's touch path are private-or-internal,
    // so rather than lean on the AssemblyPublicizer to have made them
    // reachable, this reimplements the hover check from confirmed-public
    // pieces only (GetElementRectTransform, ZInput.pointerPosition), the
    // same technique UITooltip.LateUpdate already uses on itself.
    [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
    internal static class DetailCardFeedPatch
    {
        private static bool Prepare()
        {
            if (AccessTools.Method(typeof(InventoryGrid), "UpdateGui") != null) return true;

            Plugin.Log.LogError(
                "InventoryGrid.UpdateGui not found, the detail card will not update. " +
                "Re-run tools/decompile.sh and check what it is called now.");
            return false;
        }

        private static void Postfix(InventoryGrid __instance)
        {
            if (!Plugin.DetailCard.Value)
            {
                DetailCard.Instance?.Hide();
                return;
            }

            if (__instance == null || __instance.m_gridRoot == null) return;
            Inventory inventory = __instance.GetInventory();
            if (inventory == null) return;

            InventoryElement[] elements = __instance.m_gridRoot.GetComponentsInChildren<InventoryElement>(true);
            ItemDrop.ItemData item = FindTooltipItem(__instance, elements, inventory);

            // Called for both grids; a null result (nothing hovered/selected
            // on this grid this frame) is ignored by SetItem, so the card
            // stays on whichever grid last had a real hover instead of
            // blanking every time the mouse crosses a gap between slots.
            DetailCard.Instance?.SetItem(item);
        }

        private static ItemDrop.ItemData FindTooltipItem(InventoryGrid grid, InventoryElement[] elements, Inventory inventory)
        {
            InventoryElement active = GridHover.FindActiveElement(grid, elements);
            if (active == null) return null;
            return inventory.GetItemAt(active.Position.x, active.Position.y);
        }
    }
}
