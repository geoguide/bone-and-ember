using TMPro;
using UnityEngine;

namespace BoneAndEmber
{
    // Slice 8: the line above each bar. Label on the left (small caps,
    // letterspaced, bone at 70%), numbers on the right, right-aligned to the
    // bar's right edge. Current value big, max smaller and dimmer after a slash,
    // digits forced to a fixed advance with TMP's <mspace> tag so they don't
    // jitter as they change. Parented to the bar's root, so it inherits the
    // bar's position, scale and (for stamina) fade.
    internal class BarHeader
    {
        private const string DigitAdvance = "0.6em";

        internal RectTransform Root { get; private set; }
        internal TMP_Text Label { get; private set; }
        internal TMP_Text Number { get; private set; }
        internal float Height { get; private set; }

        private string _lastNumber;
        private float _appliedLabelSize = -1f;
        private float _appliedScale = -1f;
        private float _appliedSpacing = float.NaN;

        internal void Build(RectTransform barRoot, TMP_Text fontSource, string label, string objectName)
        {
            GameObject go = new GameObject(objectName, typeof(RectTransform));
            Root = (RectTransform)go.transform;
            Root.SetParent(barRoot, false);
            Root.anchorMin = new Vector2(0f, 1f);
            Root.anchorMax = new Vector2(1f, 1f);
            Root.pivot = new Vector2(0f, 0f);
            Root.offsetMin = new Vector2(0f, 0f);
            Root.offsetMax = new Vector2(0f, 20f);

            if (fontSource == null)
            {
                Plugin.Log.LogWarning(objectName + ": no font source, header is empty for this session.");
                return;
            }

            Label = MakeText(fontSource, "Label");
            Label.text = label.ToUpperInvariant();
            Label.alignment = TextAlignmentOptions.BottomLeft;
            Color labelColor = Palette.Bone;
            labelColor.a = 0.7f;
            Label.color = labelColor;
            RectTransform lrt = Label.rectTransform;
            lrt.anchorMin = new Vector2(0f, 0f);
            lrt.anchorMax = new Vector2(0f, 1f);
            lrt.pivot = new Vector2(0f, 0f);
            lrt.anchoredPosition = Vector2.zero;
            lrt.sizeDelta = new Vector2(240f, 0f);

            Number = MakeText(fontSource, "Number");
            Number.alignment = TextAlignmentOptions.BottomRight;
            Number.richText = true;
            Number.color = Palette.Bone;
            RectTransform nrt = Number.rectTransform;
            nrt.anchorMin = new Vector2(1f, 0f);
            nrt.anchorMax = new Vector2(1f, 1f);
            nrt.pivot = new Vector2(1f, 0f);
            nrt.anchoredPosition = Vector2.zero;
            nrt.sizeDelta = new Vector2(320f, 0f);

            Plugin.Log.LogInfo(objectName + ": created Label and Number under " + HudPath.Of(Root) + " (fixed anchors on the bar root)");
        }

        // Live: label size, number scale and letterspacing all come from config.
        // Returns true when the header's height changed, so layout can re-run.
        internal bool ApplyStyle(float labelSize, float numberScale, float letterSpacing)
        {
            if (Label == null) return false;

            bool changed = !Mathf.Approximately(labelSize, _appliedLabelSize) ||
                           !Mathf.Approximately(numberScale, _appliedScale) ||
                           !Mathf.Approximately(letterSpacing, _appliedSpacing);
            if (!changed) return false;

            _appliedLabelSize = labelSize;
            _appliedScale = numberScale;
            _appliedSpacing = letterSpacing;

            Label.fontSize = labelSize;
            Label.characterSpacing = letterSpacing;
            Number.fontSize = labelSize * numberScale;

            Height = Mathf.Ceil(Number.fontSize * 1.2f);
            Root.offsetMax = new Vector2(0f, Height);
            return true;
        }

        internal void SetValues(int current, int max)
        {
            if (Number == null) return;
            string s = "<mspace=" + DigitAdvance + ">" + current + "</mspace>" +
                       "<size=62%><alpha=#99> / <mspace=" + DigitAdvance + ">" + max + "</mspace><alpha=#FF></size>";
            if (s == _lastNumber) return;
            _lastNumber = s;
            Number.text = s;
        }

        internal void SetNumberColor(Color color)
        {
            if (Number != null && Number.color != color) Number.color = color;
        }

        // Power line (005): the value is not always a current/max pair. Rich
        // text goes through as given; the caller formats it.
        internal void SetText(string richText)
        {
            if (Number == null || richText == _lastNumber) return;
            _lastNumber = richText;
            Number.text = richText;
        }

        // Power line (005): the label is the boss name, which only exists once a
        // power is chosen, so it's set live rather than at build time.
        internal void SetLabel(string label)
        {
            if (Label == null) return;
            string s = label.ToUpperInvariant();
            if (Label.text != s) Label.text = s;
        }

        internal void SetLabelColor(Color color)
        {
            if (Label != null && Label.color != color) Label.color = color;
        }

        // Shift the label right to make room for something before it (the
        // power line's sigil). The number is anchored to the right edge and
        // does not move.
        internal void SetLabelOffset(float x)
        {
            if (Label == null) return;
            Label.rectTransform.anchoredPosition = new Vector2(x, 0f);
        }

        internal void SetNumbersVisible(bool visible)
        {
            if (Number != null && Number.gameObject.activeSelf != visible) Number.gameObject.SetActive(visible);
        }

        private TMP_Text MakeText(TMP_Text fontSource, string name)
        {
            TMP_Text text = Object.Instantiate(fontSource, Root);
            text.gameObject.name = name;
            text.gameObject.SetActive(true);
            text.text = "";
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.enableAutoSizing = false;
            text.fontStyle = FontStyles.Normal;
            text.characterSpacing = 0f;
            text.rectTransform.localRotation = Quaternion.identity;
            text.rectTransform.localScale = Vector3.one;
            foreach (Animator a in text.GetComponentsInChildren<Animator>(true)) Object.Destroy(a);
            HudFont.Apply(text);
            return text;
        }
    }
}
