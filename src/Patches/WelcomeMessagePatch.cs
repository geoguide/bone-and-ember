using HarmonyLib;

namespace BoneAndEmber.Patches
{
    // Hello world: shows a center-screen message when your character spawns.
    // Delete this once the mod does something real.
    [HarmonyPatch(typeof(Player), "OnSpawned")]
    internal static class WelcomeMessagePatch
    {
        private static void Postfix(Player __instance)
        {
            if (!Plugin.ShowWelcome.Value) return;

            __instance.Message(
                MessageHud.MessageType.Center,
                $"{Plugin.ModName} {Plugin.ModVersion} is running");

            Plugin.Log.LogInfo("Player spawned, welcome message shown");
        }
    }
}
