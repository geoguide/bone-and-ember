using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoneAndEmber
{
    // Slice 7 of docs/design/003-inventory.md: weight as a thin bone bar under
    // the grid rather than a number you only notice once you're already too
    // heavy, with the number beside it and the whole thing going ember over
    // the limit. Replaces vanilla's readout, which DetailCard hides via
    // HideVanillaWeight.
    //
    // Built once against the player grid and driven every frame from
    // InventorySlotStylePatch, which already runs there. Positions come from
    // the grid's own elements, same pivot-agnostic approach as the filter row.
    internal static class WeightBar
    {
        private const float BarHeight = 10f;
        private const float RowPadding = 10f;   // gap between the grid and the row
        private const float PanelPadding = 12f; // plate wraps the row by this much
        private const float RowHeight = 24f;
        private const float CapWidth = 66f;     // "WEIGHT"
        private const float NumberWidth = 110f;
        private const float ColumnGap = 10f;
        private const float TrackAlpha = 0.25f;
        private const float OutlineAlpha = 0.25f;
        private const float FontSize = 20f;
        private const float CapFontSize = 13f;

        private static GameObject _root;
        private static RectTransform _fill;
        private static Image _fillImage;
        private static Image _outlineImage;
        private static TMP_Text _label;
        private static float _trackWidth;

        internal static void ApplyVisibility()
        {
            if (_root != null) _root.SetActive(Plugin.WeightBar.Value);
        }

        internal static void Build(InventoryGrid grid, RectTransform firstColumn, RectTransform lastColumn, RectTransform lastRow)
        {
            if (_root != null) return;
            if (grid == null || grid.m_gridRoot == null || firstColumn == null || lastColumn == null || lastRow == null) return;

            float elemW = firstColumn.rect.width;
            float elemH = firstColumn.rect.height;
            if (elemW <= 0f || elemH <= 0f) return;

            float leftEdge = firstColumn.anchoredPosition.x - elemW * firstColumn.pivot.x;
            float rightEdge = lastColumn.anchoredPosition.x + elemW * (1f - firstColumn.pivot.x);
            float span = rightEdge - leftEdge;
            // +Y is up, so the bottom of the last row is below its anchor point.
            float gridBottom = lastRow.anchoredPosition.y - elemH * lastRow.pivot.y;

            GameObject rowGo = new GameObject("BoneAndEmber_WeightBar", typeof(RectTransform));
            RectTransform rowRect = (RectTransform)rowGo.transform;
            rowRect.SetParent(grid.m_gridRoot, false);
            rowRect.localRotation = Quaternion.identity;
            rowRect.localScale = Vector3.one;
            rowRect.anchorMin = firstColumn.anchorMin;
            rowRect.anchorMax = firstColumn.anchorMax;
            rowRect.pivot = new Vector2(0f, 1f); // top-left, so it grows down and right
            rowRect.anchoredPosition = new Vector2(leftEdge, gridBottom - RowPadding);
            rowRect.sizeDelta = new Vector2(span, RowHeight);
            _root = rowGo;

            _trackWidth = Mathf.Max(0f, span - CapWidth - NumberWidth - ColumnGap * 2f);

            // Small caps label on the left, same voice as the HUD's HEALTH and
            // STAMINA headers.
            TMP_Text cap = MakeText(rowRect, "Caption", 0f, CapWidth, CapFontSize, TextAlignmentOptions.MidlineLeft);
            cap.name = "Caption";
            cap.text = "WEIGHT";
            cap.characterSpacing = 4f;
            cap.color = new Color(Palette.Bone.r, Palette.Bone.g, Palette.Bone.b, 0.7f);

            GameObject trackGo = new GameObject("Track", typeof(RectTransform), typeof(Image));
            RectTransform trackRect = (RectTransform)trackGo.transform;
            trackRect.SetParent(rowRect, false);
            trackRect.localScale = Vector3.one;
            trackRect.anchorMin = new Vector2(0f, 1f);
            trackRect.anchorMax = new Vector2(0f, 1f);
            trackRect.pivot = new Vector2(0f, 1f);
            // Vertically centred in the row so the bar sits on the same line
            // as the label and the number rather than hanging off the top.
            trackRect.anchoredPosition = new Vector2(CapWidth + ColumnGap, -(RowHeight - BarHeight) * 0.5f);
            trackRect.sizeDelta = new Vector2(_trackWidth, BarHeight);

            Image track = trackGo.GetComponent<Image>();
            track.sprite = null;
            track.type = Image.Type.Simple;
            track.raycastTarget = false;
            track.color = new Color(Palette.Bone.r, Palette.Bone.g, Palette.Bone.b, TrackAlpha);

            GameObject fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            _fill = (RectTransform)fillGo.transform;
            _fill.SetParent(trackRect, false);
            _fill.localScale = Vector3.one;
            // Single left anchor with the width driven by sizeDelta, the same
            // scheme GuiBar uses for every vanilla bar.
            _fill.anchorMin = new Vector2(0f, 0f);
            _fill.anchorMax = new Vector2(0f, 1f);
            _fill.pivot = new Vector2(0f, 0.5f);
            _fill.offsetMin = Vector2.zero;
            _fill.offsetMax = Vector2.zero;
            _fill.sizeDelta = new Vector2(0f, 0f);

            _fillImage = fillGo.GetComponent<Image>();
            _fillImage.sprite = null;
            _fillImage.type = Image.Type.Simple;
            _fillImage.raycastTarget = false;
            _fillImage.color = Palette.Bone;

            // 1px bone outline around the bar, the same generated frame the
            // slots use. Goes ember with the fill when over the limit.
            GameObject outlineGo = new GameObject("Outline", typeof(RectTransform), typeof(Image));
            RectTransform outlineRect = (RectTransform)outlineGo.transform;
            outlineRect.SetParent(trackRect, false);
            outlineRect.localScale = Vector3.one;
            outlineRect.anchorMin = Vector2.zero;
            outlineRect.anchorMax = Vector2.one;
            outlineRect.offsetMin = Vector2.zero;
            outlineRect.offsetMax = Vector2.zero;

            _outlineImage = outlineGo.GetComponent<Image>();
            _outlineImage.sprite = SlotBorderSprite.Get();
            _outlineImage.type = Image.Type.Sliced;
            _outlineImage.raycastTarget = false;
            _outlineImage.color = new Color(Palette.Bone.r, Palette.Bone.g, Palette.Bone.b, OutlineAlpha);

            // Anchored off the bar's right end rather than the panel edge, so
            // the number travels with the bar instead of drifting to the far
            // side of the row.
            float barRight = CapWidth + ColumnGap + _trackWidth;
            _label = MakeText(rowRect, "Number", barRight + ColumnGap,
                NumberWidth, FontSize, TextAlignmentOptions.MidlineLeft);
            _label.color = Palette.Bone;
            _label.fontStyle = FontStyles.Bold;

            ApplyVisibility();
            Plugin.Log.LogInfo("weight bar: built under " + HudPath.Of(rowRect) +
                ", rect=" + rowRect.rect.size.ToString("F1") +
                " anchoredPos=" + rowRect.anchoredPosition.ToString("F1") +
                ", bar=" + _trackWidth.ToString("F1") + "x" + BarHeight +
                ", barRight=" + barRight.ToString("F1"));

            // Grow our own panel plate down over the row so it reads against
            // the panel instead of against the game world behind it.
            Patches.InventoryPanelStylePatch.WrapBelowPlayerPanel(rowRect, PanelPadding);
        }

        private static TMP_Text MakeText(RectTransform row, string name, float x, float width, float size, TextAlignmentOptions align)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(row, false);
            rt.localScale = Vector3.one;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, 0f);
            rt.sizeDelta = new Vector2(width, RowHeight);

            TMP_Text text = go.AddComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.enableAutoSizing = false;
            text.alignment = align;
            text.raycastTarget = false;
            text.color = Palette.Bone;
            HudFont.Apply(text);
            return text;
        }

        internal static void Refresh(Player player, Inventory inventory)
        {
            ApplyVisibility();
            if (!Plugin.WeightBar.Value || _root == null || player == null || inventory == null) return;

            float total = inventory.GetTotalWeight();
            float max = player.GetMaxCarryWeight();
            bool over = total > max;
            float fraction = max > 0f ? Mathf.Clamp01(total / max) : 0f;

            if (_fill != null) _fill.sizeDelta = new Vector2(_trackWidth * fraction, 0f);
            if (_fillImage != null) _fillImage.color = over ? Palette.EmberCritical : Palette.Bone;
            if (_outlineImage != null)
            {
                _outlineImage.color = over
                    ? Palette.EmberCritical
                    : new Color(Palette.Bone.r, Palette.Bone.g, Palette.Bone.b, OutlineAlpha);
            }
            if (_label != null)
            {
                _label.color = over ? Palette.EmberCritical : Palette.Bone;
                _label.text = Mathf.CeilToInt(total) + " / " + Mathf.CeilToInt(max);
            }
        }
    }
}
