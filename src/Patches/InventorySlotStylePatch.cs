using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace BoneAndEmber.Patches
{
    // Slice 2 of docs/design/003-inventory.md: restyle both the player grid and
    // the container grid from a single postfix, since InventoryGui.Awake wires
    // the exact same InventoryGrid class to m_playerGrid and m_containerGrid
    // (003-notes.md, "the container view shares InventoryGrid"). Postfix, not a
    // prefix: we read the same items vanilla just laid out this frame, and we
    // only fight vanilla for the one field it stomps that we care about (the
    // durability color and amount text, see SlotStyle.Refresh).
    //
    // UpdateGui only runs while the inventory screen is actually open
    // (InventoryGui.Update gates the whole update chain on the "visible"
    // animator bool), and the container grid specifically only gets it once a
    // chest is open (m_currentContainer != null), so first-sight logging and
    // styling naturally happen the first time Geo opens each screen in a test
    // session, not at plugin load.
    [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
    internal static class InventorySlotStylePatch
    {
        private static bool _loggedHierarchy;
        private static int _postfixCalls;
        private static bool _hotbarRuleDecided;
        private static string _lastFilterLine;
        private static readonly Dictionary<InventoryGrid, int> LastSeenCount = new Dictionary<InventoryGrid, int>();

        // UpdateGui is private, so it's matched by name. If a Valheim update
        // ever renames it, skip this patch with a clear log line instead of
        // taking the whole mod down at PatchAll.
        private static bool Prepare()
        {
            if (AccessTools.Method(typeof(InventoryGrid), "UpdateGui") != null) return true;

            Plugin.Log.LogError(
                "InventoryGrid.UpdateGui not found, the inventory slot restyle is off. " +
                "Re-run tools/decompile.sh and check what it is called now.");
            return false;
        }

        // player and dragItem are UpdateGui's own arguments, handed over by
        // Harmony. player is null for the container grid and non-null for the
        // player grid, which is the same signal vanilla uses to decide whether
        // to draw hotbar binding numbers, so it's a more reliable "is this the
        // player grid" test than comparing against InventoryGui.m_playerGrid.
        // dragItem is what's currently on the cursor, or null; it drives the
        // drop-target highlight.
        private static void Postfix(InventoryGrid __instance, Player player, ItemDrop.ItemData dragItem)
        {
            _postfixCalls++;
            // Proves the postfix keeps firing frame after frame, not just once
            // on the frame the inventory opened, without spamming a line every
            // frame: one line the first time, one line roughly three seconds
            // in (assuming ~60fps) as a "still running" checkpoint.
            if (_postfixCalls == 1 || _postfixCalls == 180)
            {
                Plugin.Log.LogInfo("slot style: postfix call #" + _postfixCalls +
                    ", InventoryEnabled=" + Plugin.InventoryEnabled.Value +
                    ", grid=" + (__instance != null ? __instance.name : "(null)"));
            }

            InventoryPanelStylePatch.RefreshGamepadHighlight(__instance, player != null);

            if (!Plugin.InventoryEnabled.Value) return;
            if (__instance == null || __instance.m_gridRoot == null) return;

            Inventory inventory = __instance.GetInventory();
            if (inventory == null) return;

            InventoryElement[] elements = __instance.m_gridRoot.GetComponentsInChildren<InventoryElement>(true);

            int lastCount;
            if (!LastSeenCount.TryGetValue(__instance, out lastCount) || lastCount != elements.Length)
            {
                LastSeenCount[__instance] = elements.Length;
                Plugin.Log.LogInfo("slot style: grid " + HudPath.Of(__instance.m_gridRoot) + " now has " + elements.Length + " element(s).");
            }

            if (!_hotbarRuleDecided && player != null)
            {
                _hotbarRuleDecided = true;
                AddHotbarRule(__instance, elements);
                BuildGridFurniture(__instance, elements);
            }

            // Weight is the player's, so it only tracks the player grid.
            if (player != null)
            {
                WeightBar.Refresh(player, inventory);
                RepairLine.Refresh(player);
            }

            // The one slot a dragged item would land in: whatever the cursor
            // (or the gamepad cursor) is over on this grid. Null on whichever
            // grid the pointer isn't over, so at most one slot in the whole
            // screen lights up.
            // Worked out every frame now, not only mid-drag: it drives the
            // hover highlight as well as the drop target.
            InventoryElement active = GridHover.FindActiveElement(__instance, elements);
            InventoryElement dropTarget = dragItem != null ? active : null;

            // InventoryGui.OnSelectedItem refuses the move outright before it
            // ever reaches DropItem when a quest item would cross between two
            // inventories, so that destination is a dead end even though the
            // slot itself looks fine.
            bool dragBlocked = false;
            if (dragItem != null && dropTarget != null && InventoryGui.instance != null)
            {
                ItemDrop.ItemData targetItem = inventory.GetItemAt(dropTarget.Position.x, dropTarget.Position.y);
                bool questItemInvolved = dragItem.m_shared.m_questItem || (targetItem != null && targetItem.m_shared.m_questItem);
                bool crossingInventories = !inventory.ContainsItem(dragItem);
                dragBlocked = questItemInvolved && crossingInventories;
            }

            bool counting = Plugin.FilterRow.Value && FilterRow.Active != ItemFilter.All;
            int matchCount = 0;
            int dimCount = 0;
            int totalCount = 0;

            for (int i = 0; i < elements.Length; i++)
            {
                InventoryElement element = elements[i];
                SlotStyle style = element.GetComponent<SlotStyle>();
                if (style == null)
                {
                    RectTransform elementRect = element.transform as RectTransform;
                    // Not laid out yet (rect not yet computed this frame); try
                    // again next frame rather than build against a zero rect.
                    if (elementRect == null || elementRect.rect.width <= 0f || elementRect.rect.height <= 0f)
                    {
                        continue;
                    }

                    bool firstEver = !_loggedHierarchy;
                    LogHierarchyOnce(element.transform);

                    style = element.gameObject.AddComponent<SlotStyle>();
                    style.Build(element);

                    if (firstEver) LogAfterBuild(element);
                }

                ItemDrop.ItemData item = inventory.GetItemAt(element.Position.x, element.Position.y);
                style.Refresh(element, item, dragItem, element == dropTarget, dragBlocked, element == active);

                if (counting && item != null)
                {
                    if (FilterRow.Matches(item, FilterRow.Active)) matchCount++;
                    else dimCount++;
                }
                if (counting) totalCount++;
            }

            // Verifies the filter is actually reaching the grid before anyone
            // starts tuning alpha values: if dim is 0 with a filter active,
            // the problem is upstream of how it looks.
            if (counting)
            {
                string line = "filter=" + FilterRow.Active +
                    " match=" + matchCount + " dim=" + dimCount + " total=" + totalCount;
                if (line != _lastFilterLine)
                {
                    _lastFilterLine = line;
                    Plugin.Log.LogInfo(line);
                }
            }
            else
            {
                _lastFilterLine = null;
            }
        }

        // Dumps the live hierarchy of exactly one element, once per game
        // session, before we touch it. Requested so we can confirm real child
        // names against the guesses in 003-notes.md (the vanilla background
        // image, the durability bar's real anchor scheme) instead of assuming
        // them from the decompile alone.
        private static void LogHierarchyOnce(Transform root)
        {
            if (_loggedHierarchy) return;
            _loggedHierarchy = true;

            Plugin.Log.LogInfo("slot style: first element seen at " + HudPath.Of(root) + ", live hierarchy before restyle:");
            DumpRecursive(root, 0);
        }

        // The sibling-order question directly: where our new children actually
        // landed relative to what vanilla already had, and what vanilla's own
        // background graphic (via Button.image, per InventoryElement's Selectable)
        // actually is, so a guess about "our plate covers it" is checked, not
        // assumed.
        private static void LogAfterBuild(InventoryElement element)
        {
            RectTransform elementRect = element.transform as RectTransform;
            Plugin.Log.LogInfo("slot style: after Build(), same element (" + HudPath.Of(elementRect) + "), childCount=" + elementRect.childCount + ":");
            DumpRecursive(elementRect, 0);

            Transform plate = elementRect.Find("BoneAndEmber_Plate");
            Transform border = elementRect.Find("BoneAndEmber_Border");
            Plugin.Log.LogInfo("slot style: plate=" + (plate != null ? "found, siblingIndex=" + plate.GetSiblingIndex() + ", rect=" + RectOf(plate) : "MISSING"));
            Plugin.Log.LogInfo("slot style: border=" + (border != null ? "found, siblingIndex=" + border.GetSiblingIndex() : "MISSING"));

            Image bg = element.m_button != null ? element.m_button.image : null;
            if (bg != null)
            {
                Plugin.Log.LogInfo("slot style: vanilla background (m_button.image) at " + HudPath.Of(bg.transform) +
                    ", siblingIndex=" + bg.transform.GetSiblingIndex() +
                    ", sprite=" + (bg.sprite != null ? bg.sprite.name : "(none)") +
                    ", color=" + bg.color + ", enabled=" + bg.enabled);
            }
            else
            {
                Plugin.Log.LogInfo("slot style: element.m_button.image is null, vanilla background is not the button's target graphic.");
            }
        }

        // A 1px bone hairline in the gap between row 0 and row 1, separating
        // the hotbar row from the rest of the pack. This replaces the "Pack"
        // label from the spec: the measured gap is 6px (64px slots on 70px
        // centers), which no legible text fits in, so the hotbar numbers do
        // the naming and this just draws the seam. Decided in
        // 003-inventory.md's "Decisions from the round 1 mockup".
        //
        // Parented to the grid root rather than the panel so it scrolls and
        // hides with the grid. Player grid only: a chest has no hotbar row.
        private const float RuleThickness = 1f;
        private const float RuleAlpha = 0.3f;

        private static void AddHotbarRule(InventoryGrid grid, InventoryElement[] elements)
        {
            InventoryElement first = null;
            InventoryElement last = null;
            InventoryElement row1 = null;
            for (int i = 0; i < elements.Length; i++)
            {
                InventoryElement e = elements[i];
                if (e.Position.y == 0)
                {
                    if (first == null || e.Position.x < first.Position.x) first = e;
                    if (last == null || e.Position.x > last.Position.x) last = e;
                }
                else if (e.Position.y == 1 && row1 == null)
                {
                    row1 = e;
                }
            }

            if (first == null || last == null || row1 == null)
            {
                Plugin.Log.LogInfo("hotbar rule: skipped, grid has no row 0 and row 1 to sit between.");
                return;
            }

            RectTransform r0 = first.transform as RectTransform;
            RectTransform rLast = last.transform as RectTransform;
            RectTransform r1 = row1.transform as RectTransform;
            if (r0 == null || rLast == null || r1 == null || r0.rect.width <= 0f) return;

            // Everything is expressed in the elements' own anchor and pivot
            // convention, copied rather than assumed, so the maths holds
            // whatever that convention turns out to be. The row's visual span
            // runs from column 0's left edge to the last column's right edge.
            float elemWidth = r0.rect.width;
            float elemHeight = r0.rect.height;
            float leftEdge = r0.anchoredPosition.x - elemWidth * r0.pivot.x;
            float rightEdge = rLast.anchoredPosition.x + elemWidth * (1f - r0.pivot.x);
            float span = rightEdge - leftEdge;
            // +Y is up in this space (row 1 sits at -m_elementSpace), so the
            // gap runs from row 0's bottom edge down to row 1's top edge.
            // Averaging the raw anchoredPositions instead only finds the gap
            // when the pivot happens to be centred; with a top pivot it lands
            // in the middle of row 0, which is exactly what testing showed.
            float row0Bottom = r0.anchoredPosition.y - elemHeight * r0.pivot.y;
            float row1Top = r1.anchoredPosition.y + elemHeight * (1f - r1.pivot.y);
            float midY = (row0Bottom + row1Top) * 0.5f;

            GameObject go = new GameObject("BoneAndEmber_HotbarRule", typeof(RectTransform), typeof(Image));
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(grid.m_gridRoot, false);
            rt.localRotation = Quaternion.identity;
            rt.localScale = Vector3.one;
            rt.anchorMin = r0.anchorMin;
            rt.anchorMax = r0.anchorMax;
            rt.pivot = r0.pivot;
            rt.anchoredPosition = new Vector2(leftEdge + span * r0.pivot.x, midY);
            rt.sizeDelta = new Vector2(span, RuleThickness);

            Image img = go.GetComponent<Image>();
            img.sprite = null;
            img.type = Image.Type.Simple;
            img.raycastTarget = false;
            img.color = new Color(Palette.Bone.r, Palette.Bone.g, Palette.Bone.b, RuleAlpha);

            Plugin.Log.LogInfo("hotbar rule: drawn at " + HudPath.Of(rt) + ", span=" + span.ToString("F1") +
                "px, y=" + midY.ToString("F1") + ", thickness=" + RuleThickness + "px");
        }

        // The filter row above the grid and the weight bar below it, both
        // built from the same column and row references so they line up with
        // the grid's own geometry. Player grid only: a chest has no hotbar row
        // and carries no weight against your limit.
        private static void BuildGridFurniture(InventoryGrid grid, InventoryElement[] elements)
        {
            InventoryElement first = null;
            InventoryElement last = null;
            InventoryElement bottom = null;
            for (int i = 0; i < elements.Length; i++)
            {
                InventoryElement e = elements[i];
                if (e.Position.y == 0)
                {
                    if (first == null || e.Position.x < first.Position.x) first = e;
                    if (last == null || e.Position.x > last.Position.x) last = e;
                }
                if (bottom == null || e.Position.y > bottom.Position.y ||
                    (e.Position.y == bottom.Position.y && e.Position.x < bottom.Position.x))
                {
                    bottom = e;
                }
            }

            if (first == null || last == null || bottom == null)
            {
                Plugin.Log.LogWarning("grid furniture: could not find the grid's corner elements, filter row and weight bar skipped.");
                return;
            }

            FilterRow.Build(grid, first.transform as RectTransform, last.transform as RectTransform);
            WeightBar.Build(grid, first.transform as RectTransform, last.transform as RectTransform, bottom.transform as RectTransform);
            RepairLine.Build(grid, first.transform as RectTransform, last.transform as RectTransform, bottom.transform as RectTransform);
        }

        private static string RectOf(Transform t)
        {
            RectTransform rt = t as RectTransform;
            if (rt == null) return "(not a RectTransform)";
            return "size=" + rt.rect.size.ToString("F1") + " anchoredPos=" + rt.anchoredPosition.ToString("F1") + " localScale=" + rt.localScale.ToString("F2");
        }

        private static void DumpRecursive(Transform t, int depth)
        {
            string indent = new string(' ', depth * 2);
            Component[] comps = t.GetComponents<Component>();
            string[] names = new string[comps.Length];
            for (int i = 0; i < comps.Length; i++)
            {
                names[i] = comps[i] != null ? comps[i].GetType().Name : "(missing)";
            }

            Plugin.Log.LogInfo(
                "slot style: " + indent + t.name +
                " [" + string.Join(", ", names) + "]" +
                (t.gameObject.activeSelf ? "" : " (inactive)"));

            for (int i = 0; i < t.childCount; i++)
            {
                DumpRecursive(t.GetChild(i), depth + 1);
            }
        }
    }
}
