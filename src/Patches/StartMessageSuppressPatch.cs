using HarmonyLib;

namespace BoneAndEmber.Patches
{
    // Slice 7 follow-up: when the loud slab fires for an effect, vanilla's own
    // center message for the same moment ("You are freezing!") is redundant.
    //
    // This is a prefix that returns false, which the project rules reserve for
    // when there's no other way. There isn't one here: by the time our code hears
    // about the effect, StatusEffect.Setup has already handed the message to
    // MessageHud, so the only place to stop it is before ShowMessage runs. It
    // skips only Center messages whose text is the start message of an effect
    // in LoudMoments' table, and only while LoudMomentsEnabled is on. Everything
    // else, including every other vanilla message, passes through untouched.
    [HarmonyPatch(typeof(MessageHud), "ShowMessage")]
    internal static class StartMessageSuppressPatch
    {
        private static bool Prepare()
        {
            if (AccessTools.Method(typeof(MessageHud), "ShowMessage") != null) return true;
            Plugin.Log.LogError("MessageHud.ShowMessage not found, vanilla start messages will show alongside the slab.");
            return false;
        }

        private static bool Prefix(MessageHud.MessageType type, string text)
        {
            if (type != MessageHud.MessageType.Center) return true;

            // A repair-all is mid-flight: vanilla prints one line per item and
            // the caller replaces all of them with a single summary.
            if (RepairGate.Suppressing) return false;

            if (!Plugin.HudEnabled.Value || !Plugin.LoudMomentsEnabled.Value) return true;
            if (!LoudMoments.IsCoveredStartMessage(text)) return true;

            Plugin.Log.LogInfo("loud: suppressed vanilla center message \"" + text + "\" (slab covers it).");
            return false;
        }
    }
}
