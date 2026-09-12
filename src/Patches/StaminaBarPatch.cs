using HarmonyLib;

namespace BoneAndEmber.Patches
{
    // Feeds our stamina bar from vanilla's own stamina update, same reasoning as
    // HealthBarPatch: same numbers at the same moment, and vanilla keeps updating
    // its own (hidden) bar underneath, so turning our HUD off restores a live one.
    [HarmonyPatch(typeof(Hud), "UpdateStamina")]
    internal static class StaminaBarPatch
    {
        private static bool Prepare()
        {
            if (AccessTools.Method(typeof(Hud), "UpdateStamina") != null) return true;

            Plugin.Log.LogError(
                "Hud.UpdateStamina not found, the stamina bar will not update. " +
                "Re-run tools/decompile.sh and check what it is called now.");
            return false;
        }

        private static void Postfix(Player player, float dt)
        {
            if (player == null || !Plugin.HudEnabled.Value) return;

            AlwaysOnHud hud = AlwaysOnHud.Instance;
            if (hud == null) return;

            hud.SetStamina(player, dt);
        }
    }
}
