using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoneAndEmber
{
    // Slice 6 of docs/design/003-inventory.md: a row of category chips above
    // the hotbar row. Picking one dims every slot that doesn't match to 30%
    // alpha. Nothing moves and nothing resizes: the element RectTransform is
    // what InventoryGrid hit-tests against, so its size and position stay
    // exactly as vanilla left them (003-inventory.md's decisions, and the
    // hit-testing note in 003-notes.md).
    //
    // No controller cycling. The spec said to use LB/RB only if they were free
    // in the inventory, and they aren't: JoyTabLeft/JoyTabRight are bound to
    // BumperL/BumperR and InventoryGui.UpdateGamepad already consumes both to
    // move between UI groups whenever the inventory has focus, with BumperL
    // additionally serving as the quick-move modifier in
    // InventoryGrid.OnLeftDown. Taking them would break moving between the
    // player grid, a container and the crafting tabs.
    internal enum ItemFilter
    {
        All,
        Weapons,
        Armor,
        Food,
        Tools,
        Materials,
    }

    internal static class FilterRow
    {
        private const float ChipHeight = 26f;
        private const float ChipGap = 4f;
        private const float RowGap = 8f;
        private const float PanelPadding = 12f;
        private const float DimAlpha = 0.2f;
        private const float ChipFontSize = 14f;
        private const float ChipLetterSpacing = 6f;  // TMP units are 1/100 em, so 0.06em
        private const float UnderlineHeight = 2f;
        private const float InactiveTextAlpha = 0.7f;
        private const float HoverTextAlpha = 0.9f;
        private const float UnderlineSlideMs = 100f; // slice 9

        private static readonly ItemFilter[] Order =
        {
            ItemFilter.All, ItemFilter.Weapons, ItemFilter.Armor,
            ItemFilter.Food, ItemFilter.Tools, ItemFilter.Materials,
        };

        private static readonly List<Image> ChipBackgrounds = new List<Image>();
        private static readonly List<TMP_Text> ChipLabels = new List<TMP_Text>();
        private static readonly List<ItemFilter> ChipFilters = new List<ItemFilter>();
        private static ItemFilter? _hovered;

        private static GameObject _root;

        // Slice 9: one shared underline that slides to the active chip
        // instead of N static underlines toggled by SetActive. Every chip is
        // the same width (see Build()), so only X needs to move.
        private static Image _underline;
        private static Tween _underlineX;
        private static bool _underlineInit;

        internal static ItemFilter Active { get; private set; } = ItemFilter.All;

        internal static float AlphaFor(ItemDrop.ItemData item)
        {
            if (!Plugin.FilterRow.Value) return 1f;
            if (Active == ItemFilter.All) return 1f;
            // An empty slot has nothing to filter out, and dimming every empty
            // square just makes the grid look switched off.
            if (item == null) return 1f;
            return Matches(item, Active) ? 1f : DimAlpha;
        }

        // Materials is the catch-all, so every item is findable under exactly
        // one of the five specific filters and nothing falls through.
        internal static bool Matches(ItemDrop.ItemData item, ItemFilter filter)
        {
            ItemDrop.ItemData.ItemType t = item.m_shared.m_itemType;
            switch (filter)
            {
                case ItemFilter.All:
                    return true;
                case ItemFilter.Weapons:
                    return IsWeapon(t);
                case ItemFilter.Armor:
                    return IsArmor(t);
                case ItemFilter.Food:
                    return t == ItemDrop.ItemData.ItemType.Consumable;
                case ItemFilter.Tools:
                    return IsTool(t);
                case ItemFilter.Materials:
                    return !IsWeapon(t) && !IsArmor(t) && !IsTool(t) &&
                           t != ItemDrop.ItemData.ItemType.Consumable;
                default:
                    return true;
            }
        }

        private static bool IsWeapon(ItemDrop.ItemData.ItemType t)
        {
            switch (t)
            {
                case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                case ItemDrop.ItemData.ItemType.Bow:
                case ItemDrop.ItemData.ItemType.Ammo:
                case ItemDrop.ItemData.ItemType.AmmoNonEquipable:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsArmor(ItemDrop.ItemData.ItemType t)
        {
            switch (t)
            {
                case ItemDrop.ItemData.ItemType.Helmet:
                case ItemDrop.ItemData.ItemType.Chest:
                case ItemDrop.ItemData.ItemType.Legs:
                case ItemDrop.ItemData.ItemType.Shoulder:
                case ItemDrop.ItemData.ItemType.Shield:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsTool(ItemDrop.ItemData.ItemType t)
        {
            return t == ItemDrop.ItemData.ItemType.Tool || t == ItemDrop.ItemData.ItemType.Torch;
        }

        internal static void SetActive(ItemFilter filter)
        {
            Active = filter;
            RestyleChips();
            Plugin.Log.LogInfo("filter row: active filter is now " + filter);
        }

        // The chip row draws its own selected state from Active and nothing
        // else, so a chip cannot look selected unless the filter really
        // changed. That was the tell for the click never landing: the chip
        // styled itself while Active stayed All.
        internal static ItemFilter ActiveForDisplay => Active;

        internal static void ApplyVisibility()
        {
            if (_root != null) _root.SetActive(Plugin.FilterRow.Value);
        }

        // Sits above row 0, aligned to the grid's own columns. Parented to the
        // grid root so it travels with the grid. Positions are derived from
        // the elements' own anchors and pivot rather than assumed, so the
        // maths holds whatever convention the prefab uses.
        internal static void Build(InventoryGrid grid, RectTransform firstColumn, RectTransform lastColumn)
        {
            if (_root != null) return;
            if (grid == null || grid.m_gridRoot == null || firstColumn == null || lastColumn == null) return;

            float elemW = firstColumn.rect.width;
            float elemH = firstColumn.rect.height;
            if (elemW <= 0f || elemH <= 0f) return;

            float leftEdge = firstColumn.anchoredPosition.x - elemW * firstColumn.pivot.x;
            float rightEdge = lastColumn.anchoredPosition.x + elemW * (1f - firstColumn.pivot.x);
            float span = rightEdge - leftEdge;
            // +Y is up in this space: row 1 sits at -m_elementSpace.
            float rowTop = firstColumn.anchoredPosition.y + elemH * (1f - firstColumn.pivot.y);

            GameObject rowGo = new GameObject("BoneAndEmber_FilterRow", typeof(RectTransform));
            RectTransform rowRect = (RectTransform)rowGo.transform;
            rowRect.SetParent(grid.m_gridRoot, false);
            rowRect.localRotation = Quaternion.identity;
            rowRect.localScale = Vector3.one;
            rowRect.anchorMin = firstColumn.anchorMin;
            rowRect.anchorMax = firstColumn.anchorMax;
            rowRect.pivot = new Vector2(0f, 0f); // bottom-left, so it grows up and right
            rowRect.anchoredPosition = new Vector2(leftEdge, rowTop + RowGap);
            rowRect.sizeDelta = new Vector2(span, ChipHeight);
            _root = rowGo;

            float chipWidth = (span - ChipGap * (Order.Length - 1)) / Order.Length;
            for (int i = 0; i < Order.Length; i++)
            {
                BuildChip(rowRect, Order[i], i * (chipWidth + ChipGap), chipWidth);
            }

            BuildUnderline(rowRect, chipWidth);
            RestyleChips();
            ApplyVisibility();
            Plugin.Log.LogInfo("filter row: built under " + HudPath.Of(rowRect) +
                ", rect=" + rowRect.rect.size.ToString("F1") +
                " anchoredPos=" + rowRect.anchoredPosition.ToString("F1") +
                ", " + Order.Length + " chips, chipWidth=" + chipWidth.ToString("F1") + "px");

            // Grow our own panel plate up over the row so the chips sit on the
            // panel rather than on the game world.
            Patches.InventoryPanelStylePatch.WrapAbovePlayerPanel(rowRect, PanelPadding);
        }

        private static void BuildChip(RectTransform row, ItemFilter filter, float x, float width)
        {
            GameObject chipGo = new GameObject("BoneAndEmber_FilterChip_" + filter, typeof(RectTransform), typeof(Image));
            RectTransform chipRect = (RectTransform)chipGo.transform;
            chipRect.SetParent(row, false);
            chipRect.localRotation = Quaternion.identity;
            chipRect.localScale = Vector3.one;
            chipRect.anchorMin = new Vector2(0f, 0f);
            chipRect.anchorMax = new Vector2(0f, 0f);
            chipRect.pivot = new Vector2(0f, 0f);
            chipRect.anchoredPosition = new Vector2(x, 0f);
            chipRect.sizeDelta = new Vector2(width, ChipHeight);

            Image bg = chipGo.GetComponent<Image>();
            bg.sprite = ChipSprite.Get(); // clipped top-right corner, shared with the 001 status chips
            bg.type = Image.Type.Sliced;
            // Stays a raycast target even when fully transparent: Unity's Image
            // hit test ignores alpha, so an inactive chip with no plate still
            // takes clicks and hovers.
            bg.raycastTarget = true;

            GameObject labelGo = new GameObject("Label", typeof(RectTransform));
            RectTransform labelRect = (RectTransform)labelGo.transform;
            labelRect.SetParent(chipRect, false);
            labelRect.localRotation = Quaternion.identity;
            labelRect.localScale = Vector3.one;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            TMP_Text label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text = filter.ToString().ToUpperInvariant();
            label.fontSize = ChipFontSize;
            label.enableAutoSizing = false;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            label.characterSpacing = ChipLetterSpacing;
            HudFont.Apply(label);

            // Our own click handler, not a Button: see FilterChip for why.
            FilterChip chip = chipGo.AddComponent<FilterChip>();
            chip.Filter = filter;

            ChipBackgrounds.Add(bg);
            ChipLabels.Add(label);
            ChipFilters.Add(filter);
        }

        // One underline, parented to the row itself (same origin as every
        // chip: anchorMin/Max (0,0), pivot (0,0)) rather than to a chip, so it
        // can slide across chip boundaries instead of popping between N
        // static ones.
        private static void BuildUnderline(RectTransform row, float width)
        {
            GameObject go = new GameObject("BoneAndEmber_FilterUnderline", typeof(RectTransform), typeof(Image));
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(row, false);
            rt.localScale = Vector3.one;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.sizeDelta = new Vector2(width, UnderlineHeight);

            _underline = go.GetComponent<Image>();
            _underline.sprite = null;
            _underline.type = Image.Type.Simple;
            _underline.raycastTarget = false;
            _underline.color = Palette.EmberHealth;
        }

        // Driven from DetailCard.Update() - the one session-long MonoBehaviour
        // already ticking inventory motion - rather than a new component for
        // one float.
        internal static void Tick(float dt)
        {
            if (_underline == null) return;
            _underlineX.Update(dt);
            ApplyUnderlinePosition();
        }

        private static void ApplyUnderlinePosition()
        {
            if (_underline == null) return;
            Vector2 pos = _underline.rectTransform.anchoredPosition;
            pos.x = _underlineX.Value;
            _underline.rectTransform.anchoredPosition = pos;
        }

        // Active chip is filled bone with dark text; the rest are dark plates
        // with bone text.
        internal static void SetHovered(ItemFilter filter, bool hovering)
        {
            if (hovering) _hovered = filter;
            else if (_hovered.HasValue && _hovered.Value == filter) _hovered = null;
            RestyleChips();
        }

        private static void RestyleChips()
        {
            int activeIndex = -1;
            for (int i = 0; i < ChipFilters.Count; i++)
            {
                bool active = ChipFilters[i] == Active;
                if (active) activeIndex = i;
                bool hovered = _hovered.HasValue && _hovered.Value == ChipFilters[i];

                // Inactive chips carry no plate at all, so the row reads as
                // text on the panel until something is chosen.
                if (ChipBackgrounds[i] != null)
                {
                    ChipBackgrounds[i].color = active ? Palette.Bone : Color.clear;
                }

                if (ChipLabels[i] != null)
                {
                    // The active label carries the weight, not just a colour
                    // swap: bold and fully opaque against the bone plate. Colour
                    // alone was washing out at this size.
                    if (active)
                    {
                        ChipLabels[i].color = Palette.PanelPlate;
                        ChipLabels[i].fontStyle = FontStyles.Bold;
                    }
                    else
                    {
                        ChipLabels[i].color = new Color(Palette.Bone.r, Palette.Bone.g, Palette.Bone.b,
                            hovered ? HoverTextAlpha : InactiveTextAlpha);
                        ChipLabels[i].fontStyle = FontStyles.Normal;
                    }
                }
            }

            if (_underline != null && activeIndex >= 0)
            {
                float targetX = ChipBackgrounds[activeIndex].rectTransform.anchoredPosition.x;
                if (!_underlineInit)
                {
                    _underlineInit = true;
                    _underlineX.Snap(targetX);
                }
                else if (Plugin.MotionEnabled.Value)
                {
                    _underlineX.Retarget(targetX, UnderlineSlideMs);
                }
                else
                {
                    _underlineX.Snap(targetX);
                }
                ApplyUnderlinePosition();
            }
        }
    }
}
