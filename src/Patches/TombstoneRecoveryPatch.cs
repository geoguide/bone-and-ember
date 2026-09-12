using HarmonyLib;

namespace BoneAndEmber.Patches
{
    // Slice 4 of docs/design/002-waypoint-strip.md: notice when the local player
    // collects their own grave, so the death stops being something to chase.
    //
    // Both hooks are client-side and read-only with respect to the game: nothing
    // is sent, no ZDO is written. They only tell our own state file what happened.
    [HarmonyPatch(typeof(TombStone))]
    internal static class TombstoneRecoveryPatch
    {
        // The main path. Vanilla wires this to Container.m_onTakeAllSuccess in
        // TombStone.Awake, and it runs only on the machine that emptied the grave,
        // which is exactly the event we want. Covers the "take all" button and the
        // auto-loot-all inside Interact when the contents fit.
        [HarmonyPatch("OnTakeAllSuccess")]
        [HarmonyPostfix]
        private static void OnTakeAllSuccessPostfix(TombStone __instance)
        {
            Record(__instance, "took all");
        }

        // Backstop for looting item by item, which never fires OnTakeAllSuccess.
        // UpdateDespawn runs on a 2 second repeat and destroys the grave once it is
        // empty, so by the time this passes the checks the grave is gone anyway.
        [HarmonyPatch("UpdateDespawn")]
        [HarmonyPostfix]
        private static void UpdateDespawnPostfix(TombStone __instance)
        {
            if (__instance == null || __instance.m_container == null) return;
            if (__instance.m_container.IsInUse()) return;
            if (__instance.m_container.GetInventory().NrOfItems() > 0) return;

            Record(__instance, "emptied");
        }

        private static void Record(TombStone stone, string reason)
        {
            if (stone == null || stone.m_nview == null || !stone.m_nview.IsValid()) return;

            // Looting a friend's grave must never clear one of our death markers.
            if (!stone.IsOwner()) return;

            // The spawn point is where the grave was created, before it drifted or
            // floated, so it lines up with the death pin better than the current
            // transform does. Falls back to wherever it is now.
            UnityEngine.Vector3 pos = stone.m_nview.GetZDO().GetVec3(ZDOVars.s_spawnPoint, stone.transform.position);
            RecoveredDeaths.MarkRecovered(pos, reason);
        }
    }
}
