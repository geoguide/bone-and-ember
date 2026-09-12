using HarmonyLib;

namespace BoneAndEmber.Patches
{
    // Hides vanilla's own tooltip on inventory slots while the detail card is
    // enabled, per docs/design/003-inventory.md slice 4.
    //
    // Two halves, because skipping the fill alone wasn't enough. Not filling
    // the tooltip left whatever the prefab shipped with in m_text/m_topic, and
    // UITooltip.OnHoverStart shows the window whenever either of those is
    // non-empty, so hovering a slot produced an empty box reading "Tooltip".
    // So: blank the strings, and suppress the show itself.
    //
    // Prefixes, and this is the "no other way" case CLAUDE.md asks to call
    // out. UITooltip.Set can synchronously trigger OnHoverStart and push text
    // into the already-visible label the moment it's called, so clearing the
    // strings afterward in a postfix can't reliably un-show what already
    // rendered. CreateItemTooltip is one line (tooltip.Set(...)) with no other
    // side effect, so skipping it is safe.
    internal static class SuppressVanillaTooltipPatch
    {
        private static bool _loggedSuppression;

        // Half one: never fill an inventory slot's tooltip, and blank whatever
        // the prefab left in it so there's nothing for OnHoverStart to show.
        [HarmonyPatch(typeof(InventoryGrid), "CreateItemTooltip")]
        internal static class Fill
        {
            private static bool Prepare()
            {
                if (AccessTools.Method(typeof(InventoryGrid), "CreateItemTooltip") != null) return true;

                Plugin.Log.LogError(
                    "InventoryGrid.CreateItemTooltip not found, vanilla's tooltip will keep showing alongside the detail card. " +
                    "Re-run tools/decompile.sh and check what it is called now.");
                return false;
            }

            private static bool Prefix(UITooltip tooltip)
            {
                if (!Plugin.DetailCard.Value) return true;

                if (tooltip != null)
                {
                    tooltip.m_text = "";
                    tooltip.m_topic = "";
                }
                return false;
            }
        }

        // Half two: don't let a slot's tooltip open at all. Scoped to slots by
        // checking for an InventoryElement on the same object (the hierarchy
        // dump in slice 2 confirmed UITooltip sits on the element root), so
        // every other tooltip in the game still works normally.
        [HarmonyPatch(typeof(UITooltip), "OnHoverStart")]
        internal static class Show
        {
            private static bool Prepare()
            {
                if (AccessTools.Method(typeof(UITooltip), "OnHoverStart") != null) return true;

                Plugin.Log.LogError(
                    "UITooltip.OnHoverStart not found, vanilla's slot tooltip will still pop up. " +
                    "Re-run tools/decompile.sh and check what it is called now.");
                return false;
            }

            private static bool Prefix(UITooltip __instance)
            {
                if (!Plugin.DetailCard.Value) return true;
                if (__instance == null || __instance.GetComponent<InventoryElement>() == null) return true;

                if (!_loggedSuppression)
                {
                    _loggedSuppression = true;
                    Plugin.Log.LogInfo("detail card: suppressed vanilla's slot tooltip on " + HudPath.Of(__instance.transform) +
                        " (first time only; other tooltips in the game are untouched).");
                }
                return false;
            }
        }
    }
}
