using HarmonyLib;

namespace BoneAndEmber.Patches
{
    // Feeds the food segment dividers, pulse and caption from vanilla's own food
    // update, same reasoning as HealthBarPatch.
    [HarmonyPatch(typeof(Hud), "UpdateFood")]
    internal static class FoodSegmentsPatch
    {
        private static bool Prepare()
        {
            if (AccessTools.Method(typeof(Hud), "UpdateFood") != null) return true;

            Plugin.Log.LogError(
                "Hud.UpdateFood not found, food segments will not update. " +
                "Re-run tools/decompile.sh and check what it is called now.");
            return false;
        }

        private static void Postfix(Player player)
        {
            if (player == null || !Plugin.HudEnabled.Value) return;

            AlwaysOnHud hud = AlwaysOnHud.Instance;
            if (hud == null) return;

            hud.SetFood(player);
        }
    }
}
