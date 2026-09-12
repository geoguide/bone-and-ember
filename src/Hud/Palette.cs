using UnityEngine;

namespace BoneAndEmber
{
    // Round 2 mockup palette, approved in docs/design/001-always-on-hud.md.
    // Health and stamina (slices 3, 5) consume Bone/EmberHealth/Gold now.
    // EmberCritical (low health, slice 4) and EmberBad (status chip outlines
    // and the loud slab, slices 6-7) are declared here too so later slices
    // read from the same approved values instead of re-deriving hex.
    internal static class Palette
    {
        internal static readonly Color Bone = Hex("#EDE3D1");
        internal static readonly Color EmberHealth = Hex("#D9573B");
        internal static readonly Color EmberCritical = Hex("#FF5A36");
        internal static readonly Color Gold = Hex("#E9B949");
        internal static readonly Color EmberBad = Hex("#E0793A");

        // "Better" side of a detail card delta (003 slice 5). Muted and warm
        // rather than a pure UI green, so it sits in the same family as bone,
        // ember and gold instead of reading as a system color.
        internal static readonly Color GoodGreen = Hex("#8CB369");

        // Food segment dividers: dark enough to read as a notch cut into the
        // bright fill, and to disappear against the (also dark) empty track.
        internal static readonly Color DividerNotch = Hex("#14100c");

        // Empty bar track, same dark as the divider notch so a notch over empty
        // track disappears on its own.
        internal static readonly Color Track = new Color(0.078f, 0.063f, 0.047f, 0.75f);

        // Default for the food-derived share of the health fill; the live value
        // is Plugin.FoodHealthColor so it can be tuned in game.
        internal static readonly Color FoodEmberDefault = Hex("#E8785A");

        // Status chip plate: same dark as the divider notch, mostly opaque.
        internal static readonly Color Plate = new Color(0.078f, 0.063f, 0.047f, 0.88f);

        // Inventory surfaces (003). The panel is the dark ground and the slots
        // sit one step up from it, so a slot reads as a raised tile rather than
        // a hole cut in the panel. Both fully opaque: these are real surfaces,
        // not overlays, and anything translucent picks up the wood art behind.
        internal static readonly Color PanelPlate = Hex("#14100C");
        internal static readonly Color SlotPlate = Hex("#221B14");

        private static Color Hex(string html)
        {
            if (ColorUtility.TryParseHtmlString(html, out Color c)) return c;

            Plugin.Log.LogError("Bad color literal in Palette: " + html);
            return Color.magenta;
        }
    }
}
