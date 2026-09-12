using UnityEngine;

namespace BoneAndEmber
{
    // Which slot the player is actually pointing at, shared by the detail card
    // (what to describe) and the drop highlight (where a dragged item lands).
    //
    // InventoryGrid works this out internally too, in UpdateGui, but its
    // GetHoveredElement is private and the m_selected/GetElement pair behind
    // the gamepad path is private as well, so rather than depend on the
    // publicizer having exposed them this rebuilds the same answer from
    // confirmed-public pieces: GetElementRectTransform, ZInput.pointerPosition
    // and GetGamepadSelectedElement. The mouse test is the same one
    // UITooltip.LateUpdate already uses on itself.
    internal static class GridHover
    {
        // Mirrors vanilla's own choice in UpdateGui: gamepad and touch follow
        // the grid cursor, everything else follows the mouse.
        // Dev only: bae_pin locks the highlight on one slot so hover-dependent
        // states can actually be screenshotted. Reaching for the screenshot key
        // clears a live hover, which makes reviewing them close to impossible.
        internal static InventoryElement Pinned;

        internal static InventoryElement FindActiveElement(InventoryGrid grid, InventoryElement[] elements)
        {
            if (grid == null || elements == null) return null;

            if (Pinned != null)
            {
                for (int i = 0; i < elements.Length; i++)
                {
                    if (elements[i] == Pinned) return Pinned;
                }
                return null;
            }

            // A connected pad wins outright. IsExclusiveGamepadActive alone was
            // too strict: with a pad in hand and the mouse parked over the grid
            // it stayed false, so the d-pad moved vanilla's cursor while our
            // highlight followed the stationary pointer, and the two disagreed.
            bool gamepadOrTouch = grid.m_uiGroup != null && grid.m_uiGroup.IsActive &&
                (ZInput.IsExclusiveGamepadActive() || ZInput.IsGamepadActive() || ZInput.IsTouchActive());
            if (gamepadOrTouch)
            {
                RectTransform selected = grid.GetGamepadSelectedElement();
                if (selected == null) return null;
                for (int i = 0; i < elements.Length; i++)
                {
                    if (elements[i] != null && (RectTransform)elements[i].transform == selected)
                    {
                        return elements[i];
                    }
                }
                return null;
            }

            return FindHoveredElement(elements);
        }

        // Pins whatever was last hovered, or the gamepad cursor's slot if the
        // pointer has not been used. Returns a line for the console.
        internal static string TogglePin()
        {
            if (Pinned != null)
            {
                Pinned = null;
                return "bae_pin: unpinned, the highlight follows the cursor again.";
            }

            InventoryElement target = SlotHover.Last;
            if (target == null && InventoryGui.instance != null && InventoryGui.instance.m_playerGrid != null)
            {
                RectTransform selected = InventoryGui.instance.m_playerGrid.GetGamepadSelectedElement();
                if (selected != null) target = selected.GetComponent<InventoryElement>();
            }

            if (target == null)
            {
                return "bae_pin: nothing to pin. Hover a slot (or move the d-pad onto one) first.";
            }

            Pinned = target;
            return "bae_pin: pinned slot " + target.Position.x + "," + target.Position.y +
                ". Run bae_pin again to release it.";
        }

        // Straight from the event system via SlotHover, scoped to this grid so
        // the player grid and an open container never both claim a hover.
        internal static InventoryElement FindHoveredElement(InventoryElement[] elements)
        {
            InventoryElement hovered = SlotHover.Current;
            if (hovered == null || elements == null) return null;

            for (int i = 0; i < elements.Length; i++)
            {
                if (elements[i] == hovered) return hovered;
            }
            return null;
        }
    }
}
