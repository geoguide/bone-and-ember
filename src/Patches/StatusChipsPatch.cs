using System.Collections.Generic;
using HarmonyLib;

namespace BoneAndEmber.Patches
{
    // Feeds the status chips from vanilla's own status effect update. The list it
    // gets is already filtered to effects with an icon that aren't hidden
    // (SEMan.GetHUDStatusEffects), same set vanilla draws.
    [HarmonyPatch(typeof(Hud), "UpdateStatusEffects")]
    internal static class StatusChipsPatch
    {
        private static bool Prepare()
        {
            if (AccessTools.Method(typeof(Hud), "UpdateStatusEffects") != null) return true;

            Plugin.Log.LogError(
                "Hud.UpdateStatusEffects not found, status chips will not update. " +
                "Re-run tools/decompile.sh and check what it is called now.");
            return false;
        }

        private static void Postfix(List<StatusEffect> statusEffects)
        {
            if (statusEffects == null || !Plugin.HudEnabled.Value) return;

            AlwaysOnHud hud = AlwaysOnHud.Instance;
            if (hud == null) return;

            hud.SetStatusEffects(statusEffects);
        }
    }
}
