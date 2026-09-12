using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoneAndEmber
{
    // Slice 1 of docs/design/004-repair-and-buried-ux.md: a repair line under
    // the grid, in view the whole time the inventory is open.
    //
    // Vanilla SetActive(false)s its entire repair panel whenever you are not
    // standing at a station, so the feature is invisible exactly when a player
    // would be learning it exists. This line is always there and always says
    // something: what it will fix, or where to go to fix it.
    //
    // Built and driven the same way the weight bar is, from the patch that
    // already runs per frame on the player grid.
    internal static class RepairLine
    {
        private const float RowHeight = 22f;
        private const float RowGap = 6f;        // below the weight bar row
        private const float WeightRowHeight = 24f;
        private const float WeightRowPadding = 10f;
        private const float PanelPadding = 12f;
        private const float FontSize = 14f;
        private const float LetterSpacing = 4f;
        private const float PadX = 10f;
        private const float PlateAlpha = 0.92f;
        private const float IdleAlpha = 0.55f;
        private const float RescanInterval = 0.25f;
        private const float FadeMs = 80f;
        private const float HoverLighten = 0.12f;

        private static GameObject _root;
        private static RectTransform _plateRect;
        private static Image _plate;
        private static TMP_Text _label;
        private static CanvasGroup _group;

        private static float _nextRescan;
        private static int _repairable;
        private static int _worn;
        private static string _text = "";
        private static bool _actionable;
        private static Tween _plateAlpha;
        private static bool _tweenInit;
        private static bool _hovered;

        internal static bool Actionable => _actionable;

        internal static void ApplyVisibility()
        {
            // Nothing worn means nothing to say: no line at all, rather than a
            // dead "nothing to repair" that reads like a button you can press.
            // The line still appears the moment anything is worn, anywhere,
            // which is the whole point of it over vanilla's station-only panel.
            if (_root != null) _root.SetActive(Plugin.RepairLine.Value && _worn > 0);
        }

        internal static void Build(InventoryGrid grid, RectTransform firstColumn, RectTransform lastColumn, RectTransform lastRow)
        {
            if (_root != null) return;
            if (grid == null || grid.m_gridRoot == null || firstColumn == null || lastColumn == null || lastRow == null) return;
            if (!RepairGate.Resolve()) return;

            float elemW = firstColumn.rect.width;
            float elemH = firstColumn.rect.height;
            if (elemW <= 0f || elemH <= 0f) return;

            float leftEdge = firstColumn.anchoredPosition.x - elemW * firstColumn.pivot.x;
            float rightEdge = lastColumn.anchoredPosition.x + elemW * (1f - firstColumn.pivot.x);
            float span = rightEdge - leftEdge;
            float gridBottom = lastRow.anchoredPosition.y - elemH * lastRow.pivot.y;

            // Directly under the weight bar's own row, which sits one
            // WeightRowPadding below the grid and is WeightRowHeight tall.
            float y = gridBottom - WeightRowPadding - WeightRowHeight - RowGap;

            GameObject rowGo = new GameObject("BoneAndEmber_RepairLine", typeof(RectTransform), typeof(CanvasGroup));
            RectTransform rowRect = (RectTransform)rowGo.transform;
            rowRect.SetParent(grid.m_gridRoot, false);
            rowRect.localRotation = Quaternion.identity;
            rowRect.localScale = Vector3.one;
            rowRect.anchorMin = firstColumn.anchorMin;
            rowRect.anchorMax = firstColumn.anchorMax;
            rowRect.pivot = new Vector2(0f, 1f);
            rowRect.anchoredPosition = new Vector2(leftEdge, y);
            rowRect.sizeDelta = new Vector2(span, RowHeight);
            _root = rowGo;
            _group = rowGo.GetComponent<CanvasGroup>();

            // The plate is the button. It only takes raycasts while there is
            // something to repair, so a dim "go find a workbench" line cannot
            // eat a click meant for the grid behind it.
            GameObject plateGo = new GameObject("Plate", typeof(RectTransform), typeof(Image));
            _plateRect = (RectTransform)plateGo.transform;
            _plateRect.SetParent(rowRect, false);
            _plateRect.localScale = Vector3.one;
            _plateRect.anchorMin = new Vector2(0f, 1f);
            _plateRect.anchorMax = new Vector2(0f, 1f);
            _plateRect.pivot = new Vector2(0f, 1f);
            _plateRect.anchoredPosition = Vector2.zero;
            _plateRect.sizeDelta = new Vector2(160f, RowHeight);

            _plate = plateGo.GetComponent<Image>();
            _plate.sprite = ChipSprite.Get();
            _plate.type = Image.Type.Sliced;
            _plate.color = Palette.Gold;
            _plate.raycastTarget = false;

            plateGo.AddComponent<RepairButton>();

            GameObject labelGo = new GameObject("Label", typeof(RectTransform));
            RectTransform labelRect = (RectTransform)labelGo.transform;
            labelRect.SetParent(_plateRect, false);
            labelRect.localScale = Vector3.one;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(PadX, 0f);
            labelRect.offsetMax = new Vector2(-PadX, 0f);

            _label = labelGo.AddComponent<TextMeshProUGUI>();
            _label.fontSize = FontSize;
            _label.enableAutoSizing = false;
            _label.alignment = TextAlignmentOptions.MidlineLeft;
            _label.characterSpacing = LetterSpacing;
            _label.raycastTarget = false;
            _label.color = Palette.Bone;
            HudFont.Apply(_label);

            ApplyVisibility();
            Plugin.Log.LogInfo("repair line: built under " + HudPath.Of(rowRect) +
                ", rect=" + rowRect.rect.size.ToString("F1") + " anchoredPos=" + rowRect.anchoredPosition.ToString("F1"));

            // Grow our own panel plate down over this row too, so it reads
            // against the panel rather than the world. The weight bar already
            // did this for its own row; ours sits lower, so it wins.
            Patches.InventoryPanelStylePatch.WrapBelowPlayerPanel(rowRect, PanelPadding);
        }

        internal static void Refresh(Player player)
        {
            if (!Plugin.RepairLine.Value || _root == null || player == null)
            {
                ApplyVisibility();
                return;
            }

            // Rescan before deciding visibility: the line shows or hides on
            // whether anything is worn, and that answer comes from the scan.
            if (Time.time >= _nextRescan)
            {
                _nextRescan = Time.time + RescanInterval;
                RepairGate.Rescan(out _repairable, out _worn);

                bool actionable = _repairable > 0;

                // "Free" is the fact most players never learn. Vanilla charges
                // nothing to repair, ever, but every other game charges for it,
                // so gear gets hoarded and lost to an assumption. Dimmed, so it
                // reads as a footnote to the action rather than competing with
                // it; the alpha tag runs to the end of the string, which is why
                // it has no closing tag.
                string action = Plugin.RepairAll.Value
                    ? "REPAIR ALL (" + _repairable + ")"
                    : "REPAIR (" + _repairable + ")";
                string text = actionable
                    ? action + "  <alpha=#8C>·  FREE"
                    : RepairGate.BlockedReason(_worn);

                if (actionable != _actionable || text != _text)
                {
                    _actionable = actionable;
                    _text = text;
                    Apply();

                    // Logged on change, not per frame: "why is the line not
                    // gold" is otherwise unanswerable without a debugger, and
                    // the answer is always one of these four numbers.
                    CraftingStation station = player.GetCurrentCraftingStation();
                    Plugin.Log.LogInfo("repair line: worn=" + _worn + " repairable=" + _repairable +
                        " station=" + (station != null ? station.m_name + " lvl" + station.GetLevel() : "(none)") +
                        " usable=" + (station != null ? station.CheckUsable(player, showMessage: false).ToString() : "n/a") +
                        " nocost=" + player.NoCostCheat() +
                        " -> \"" + (_text ?? "(hidden)") + "\"");
                }
            }

            ApplyVisibility();
            if (_worn <= 0) return;

            float target = _actionable ? 1f : 0f;
            if (!_tweenInit)
            {
                _tweenInit = true;
                _plateAlpha.Snap(target);
            }
            else if (Plugin.MotionEnabled.Value)
            {
                _plateAlpha.Retarget(target, FadeMs);
            }
            else
            {
                _plateAlpha.Snap(target);
            }

            float plateAlpha = _plateAlpha.Update(Time.deltaTime);
            if (_plate != null)
            {
                Color c = _actionable && _hovered ? Lighten(Palette.Gold, HoverLighten) : Palette.Gold;
                _plate.color = new Color(c.r, c.g, c.b, plateAlpha * PlateAlpha);
            }
        }

        internal static void SetHovered(bool hovered)
        {
            _hovered = hovered;
        }

        private static Color Lighten(Color c, float amount)
        {
            return new Color(
                Mathf.Clamp01(c.r + amount),
                Mathf.Clamp01(c.g + amount),
                Mathf.Clamp01(c.b + amount),
                c.a);
        }

        // Actionable reads as a real button: gold plate, dark bold text, takes
        // clicks. Idle is just a dim line of text with no plate behind it, the
        // same way an unselected filter chip carries no plate.
        private static void Apply()
        {
            if (_label == null || _plate == null) return;

            _label.text = _text ?? "";
            _label.fontStyle = _actionable ? FontStyles.Bold : FontStyles.Normal;
            _label.color = _actionable
                ? Palette.PanelPlate
                : new Color(Palette.Bone.r, Palette.Bone.g, Palette.Bone.b, IdleAlpha);

            _plate.raycastTarget = _actionable;

            float width = _label.GetPreferredValues(_label.text).x + PadX * 2f;
            _plateRect.sizeDelta = new Vector2(width, RowHeight);
        }

        internal static void Press()
        {
            if (!_actionable) return;

            int done = Plugin.RepairAll.Value ? RepairGate.RepairEverything() : RepairGate.RepairUpTo(1);
            RepairGate.AnnounceRepaired(done);

            _nextRescan = 0f; // reflect the new state on the next frame, not in a quarter second
        }
    }
}
