using System.Collections.Generic;
using HarmonyLib;
using Valheim.UI;

namespace BoneAndEmber.Patches
{
    // Restyles the radial's wedges once per open. ConstructRadial is the right
    // hook because it runs exactly once per open with the finished element
    // list, right after ClearElements has destroyed the previous set
    // (003-notes.md, "Radial"). Postfix: vanilla builds, we repaint.
    [HarmonyPatch(typeof(RadialBase), "ConstructRadial")]
    internal static class RadialStylePatch
    {
        private static bool Prepare()
        {
            if (AccessTools.Method(typeof(RadialBase), "ConstructRadial") != null) return true;

            Plugin.Log.LogError(
                "RadialBase.ConstructRadial not found, the radial restyle is off. " +
                "Re-run tools/decompile.sh and check what it is called now.");
            return false;
        }

        private static void Postfix(RadialBase __instance, List<RadialMenuElement> elements)
        {
            if (__instance == null) return;
            RadialStyle.StyleRadial(__instance, elements);
        }
    }
}
