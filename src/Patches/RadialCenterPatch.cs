using HarmonyLib;
using TMPro;
using UnityEngine.UI;
using Valheim.UI;

namespace BoneAndEmber.Patches
{
    // The radial's center panel, restyled whenever the selection changes.
    // RadialBase.OnSelectedUpdate calls ElementInfo.Set(element, animator), so
    // this postfix lands once per wedge you move onto.
    //
    // m_title, m_subTitle, m_icon and m_inventoryInfo are protected/private
    // fields on ElementInfo, reached here through Harmony's ___name field
    // injection rather than reflection of our own, so a rename shows up as a
    // patch failure at load instead of a silent no-op in game.
    [HarmonyPatch(typeof(ElementInfo), "Set", new[] { typeof(RadialMenuElement), typeof(RadialMenuAnimationManager) })]
    internal static class RadialCenterPatch
    {
        private static bool Prepare()
        {
            if (AccessTools.Method(typeof(ElementInfo), "Set",
                new[] { typeof(RadialMenuElement), typeof(RadialMenuAnimationManager) }) != null) return true;

            Plugin.Log.LogError(
                "ElementInfo.Set(RadialMenuElement, ...) not found, the radial center restyle is off. " +
                "Re-run tools/decompile.sh and check what it is called now.");
            return false;
        }

        private static void Postfix(ElementInfo __instance, RadialMenuElement element,
            TextMeshProUGUI ___m_title, TextMeshProUGUI ___m_subTitle, Image ___m_icon,
            RadialInventoryInfo ___m_inventoryInfo)
        {
            if (__instance == null) return;
            RadialStyle.StyleCenter(__instance, element, ___m_title, ___m_subTitle, ___m_icon);
            RadialStyle.StyleSidePanel(___m_inventoryInfo);
        }
    }
}
