using HarmonyLib;

namespace BoneAndEmber.Patches
{
    // Feeds the power line (docs/design/005-forsaken-power.md) from vanilla's own
    // guardian power update, same shape as HealthBarPatch: postfix, so vanilla
    // keeps driving its corner widget behind our faded-out CanvasGroup and
    // turning the line off in Configuration Manager brings that widget back live.
    [HarmonyPatch(typeof(Hud), "UpdateGuardianPower")]
    internal static class PowerLinePatch
    {
        private static bool Prepare()
        {
            if (AccessTools.Method(typeof(Hud), "UpdateGuardianPower") != null) return true;

            Plugin.Log.LogError(
                "Hud.UpdateGuardianPower not found, the power line will not update. " +
                "Re-run tools/decompile.sh and check what it is called now.");
            return false;
        }

        private static void Postfix(Player player)
        {
            if (player == null || !Plugin.HudEnabled.Value || !Plugin.PowerEnabled.Value) return;

            AlwaysOnHud hud = AlwaysOnHud.Instance;
            if (hud == null) return;

            hud.SetGuardianPower(player);
        }
    }
}
