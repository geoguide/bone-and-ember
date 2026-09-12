using HarmonyLib;

namespace BoneAndEmber.Patches
{
    // Attaches the detail card to InventoryGui, same pattern as HudAwakePatch
    // attaching AlwaysOnHud to Hud: InventoryGui is built once for the whole
    // game session, so this runs once, not per inventory open.
    [HarmonyPatch(typeof(InventoryGui), "Awake")]
    internal static class DetailCardPatch
    {
        private static void Postfix(InventoryGui __instance)
        {
            if (__instance == null || __instance.GetComponent<DetailCard>() != null) return;

            __instance.gameObject.AddComponent<DetailCard>();
            Plugin.Log.LogInfo("Detail card attached");
        }
    }
}
