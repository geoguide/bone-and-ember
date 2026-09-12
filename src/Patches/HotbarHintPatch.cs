using HarmonyLib;
using TMPro;
using UnityEngine;

namespace BoneAndEmber.Patches
{
    // docs/design/003-inventory.md, hotbar button hint: on a gamepad, show
    // which button acts on the highlighted hotbar slot.
    //
    // This is the bar at the bottom of the screen during play, not the
    // inventory grid: HotkeyBar owns its own gamepad handling, separate from
    // InventoryGrid's. JoyHotbarLeft and JoyHotbarRight move the highlight and
    // JoyHotbarUse acts on it (Player.UseHotbarItem), which is a real dedicated
    // binding, unlike the inventory grid where the d-pad is spent on navigation.
    //
    // The glyph goes in vanilla's own "binding" label rather than a new object.
    // That field already means "the input for this slot", it is the one place
    // in the element guaranteed to resolve TMP's gamepad sprite tags, and
    // vanilla itself blanks it when a pad is active (HotkeyBar.UpdateIcons),
    // so on a pad it is empty space we are filling rather than art we are
    // covering. Keyboard players keep their number untouched.
    [HarmonyPatch(typeof(HotkeyBar), "UpdateIcons")]
    internal static class HotbarHintPatch
    {
        private const string HintButton = "JoyHotbarUse";
        private static bool _logged;

        private static bool Prepare()
        {
            if (AccessTools.Method(typeof(HotkeyBar), "UpdateIcons") != null) return true;

            Plugin.Log.LogError(
                "HotkeyBar.UpdateIcons not found, the hotbar button hint is off. " +
                "Re-run tools/decompile.sh and check what it is called now.");
            return false;
        }

        private static void Postfix(HotkeyBar __instance, Player player)
        {
            if (__instance == null || player == null || player.GetInventory() == null) return;

            bool on = Plugin.HotbarHints.Value && ZInput.IsGamepadActive();
            string glyph = on ? Glyph() : null;

            for (int i = 0; i < __instance.transform.childCount; i++)
            {
                Transform element = __instance.transform.GetChild(i);

                Transform bindingT = element.Find("binding");
                if (bindingT == null) continue;
                TMP_Text binding = bindingT.GetComponent<TMP_Text>();
                if (binding == null) continue;

                // Vanilla's own "this one is highlighted" marker, so we never
                // have to guess at m_selected or keep our own copy of it.
                Transform selected = element.Find("selected");
                bool isSelected = selected != null && selected.gameObject.activeSelf;

                // Only when pressing the button would actually do something.
                ItemDrop.ItemData item = player.GetInventory().GetItemAt(i, 0);
                bool usable = item != null &&
                    (item.IsEquipable() || item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable);

                string want;
                if (on)
                {
                    // Blank on every other slot: with a pad there is no direct
                    // input for a slot you have not highlighted, which is why
                    // vanilla clears these too.
                    want = isSelected && usable ? glyph : "";
                }
                else
                {
                    want = (i + 1).ToString();
                }

                if (binding.text != want) binding.text = want;
                if (on && isSelected && binding.color != Palette.Bone) binding.color = Palette.Bone;
            }
        }

        // The real button on the real pad: GetBoundKeyString returns a TMP
        // sprite tag matched to the connected controller (PlayStation, Xbox or
        // Switch glyph sets), and follows a remap.
        private static string Glyph()
        {
            if (ZInput.instance == null) return "";
            string glyph = ZInput.instance.GetBoundKeyString(HintButton, emptyStringOnMissing: true);

            if (!_logged)
            {
                _logged = true;
                Plugin.Log.LogInfo("hotbar hint: gate is ZInput.IsGamepadActive() (HotkeyBar's own gate for its " +
                    "gamepad handling), glyph is ZInput.instance.GetBoundKeyString(\"" + HintButton + "\") = \"" +
                    glyph + "\". A box on screen instead of a button means TMP could not resolve that sprite tag.");
            }
            return glyph;
        }
    }
}
