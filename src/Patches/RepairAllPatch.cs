using HarmonyLib;

namespace BoneAndEmber.Patches
{
    // Slice 1 of docs/design/004-repair-and-buried-ux.md: one press repairs
    // everything, on vanilla's own button as well as on our line.
    //
    // Vanilla's OnRepairPressed fixes exactly one item and returns, so six worn
    // pieces is six presses and six centre messages. Patching vanilla's button
    // rather than only our own line is deliberate: vanilla's button is already
    // reachable with a gamepad through the crafting tab's UI group, so the pad
    // gets repair-all for free without us injecting a new focusable element
    // into vanilla's navigation graph.
    //
    // The prefix does not skip the original (it returns void): it only raises
    // the message-suppression flag so vanilla's own "$msg_repaired" line for
    // the first item is swallowed along with the rest, leaving one summary.
    [HarmonyPatch(typeof(InventoryGui), "OnRepairPressed")]
    internal static class RepairAllPatch
    {
        private static bool Prepare()
        {
            if (AccessTools.Method(typeof(InventoryGui), "OnRepairPressed") != null) return true;

            Plugin.Log.LogError(
                "InventoryGui.OnRepairPressed not found, vanilla's repair button keeps its one-item behaviour. " +
                "Re-run tools/decompile.sh and check what it is called now.");
            return false;
        }

        private static void Prefix(out bool __state)
        {
            __state = Plugin.RepairAll.Value && RepairGate.Resolve();
            if (__state) RepairGate.BeginSuppressing();
        }

        private static void Postfix(bool __state)
        {
            if (!__state) return;

            // Vanilla already repaired one item before this ran.
            int done = 1 + RepairGate.RepairEverything();
            RepairGate.EndSuppressing();
            RepairGate.AnnounceRepaired(done);
        }
    }
}
