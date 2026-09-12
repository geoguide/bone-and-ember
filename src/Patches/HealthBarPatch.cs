using HarmonyLib;

namespace BoneAndEmber.Patches
{
    // Feeds our bar from vanilla's own health update, so we read the same numbers at
    // the same moment. Postfix, not a prefix: vanilla keeps updating its bar behind
    // our faded-out CanvasGroup, which costs almost nothing and means turning our HUD
    // off in Configuration Manager brings back a live vanilla bar with no reload.
    [HarmonyPatch(typeof(Hud), "UpdateHealth")]
    internal static class HealthBarPatch
    {
        // UpdateHealth is private, so it is matched by name. If a Valheim update ever
        // renames it, skip this patch with a clear log line instead of taking the
        // whole mod down at PatchAll.
        private static bool Prepare()
        {
            if (AccessTools.Method(typeof(Hud), "UpdateHealth") != null) return true;

            Plugin.Log.LogError(
                "Hud.UpdateHealth not found, the health bar will not update. " +
                "Re-run tools/decompile.sh and check what it is called now.");
            return false;
        }

        private static void Postfix(Player player)
        {
            if (player == null || !Plugin.HudEnabled.Value) return;

            AlwaysOnHud hud = AlwaysOnHud.Instance;
            if (hud == null) return;

            hud.SetHealth(player);
        }
    }
}
