using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoneAndEmber
{
    // Slice 6 of docs/design/001-always-on-hud.md: active status effects as a row
    // of chips above the health bar. Each chip: dark plate with a clipped
    // top-right corner, the effect's own icon, its name, and the countdown when
    // it has one. Bad effects first with an ember outline, the rest dimmer.
    //
    // Chips are keyed by StatusEffect.NameHash(), not list index, so one can fade
    // in when its effect appears and fade out when it ends without the whole row
    // rebuilding (vanilla's list is index-keyed and rebuilds on any count change).
    internal class StatusChips
    {
        private const float Padding = 6f;
        private const float InnerGap = 5f;
        private const float FadeDuration = 0.08f; // slice 9: was 0.2
        private const float DimAlpha = 0.65f;
        private const float TimeAlpha = 0.6f;

        // Live from config / the health header: chip height matches the header
        // line, text is LabelSize, the icon is 1em square.
        private float _height = 22f;
        private float _fontSize = 12f;
        private float _rowWidth = 240f;

        private class Chip
        {
            public int Hash;
            public string AssetName;
            public bool Bad;
            public bool Present;
            public float Alpha;
            public float Width;
            public RectTransform Root;
            public CanvasGroup Group;
            public Image Outline;
            public Image Plate;
            public Image Icon;
            public TMP_Text Name;
            public TMP_Text Time;
        }

        // Vanilla has no "harmful" flag on StatusEffect (StatusAttribute is only
        // ColdResistance/DoubleImpactDamage/SailingPower/TamingBoost), so this is
        // ours: the class is the strongest signal, then the asset name, then the
        // stat modifiers for SE_Stats-based effects like Tared or Encumbered, and
        // last m_flashIcon, which vanilla sets on effects it wants to flash red.
        private static readonly HashSet<string> BadNames = new HashSet<string>
        {
            "Wet", "Cold", "Freezing", "Poison", "Burning", "Smoked", "Tared",
            "Encumbered", "Harpooned", "Puke",
        };

        private readonly Dictionary<int, Chip> _chips = new Dictionary<int, Chip>();
        private readonly List<Chip> _ordered = new List<Chip>();
        private readonly List<int> _toRemove = new List<int>();

        private RectTransform _row;
        private TMP_Text _fontSource;
        private bool _built;

        // Power line (005): the local player's own Forsaken Power effect is drawn
        // by the power line while it runs, so its chip is skipped here to avoid
        // showing it twice. 0 means nothing is excluded. Set every frame by
        // AlwaysOnHud, so turning the line off brings the chip back.
        internal int ExcludeHash;

        internal void Build(RectTransform barRoot, TMP_Text fontSource)
        {
            _fontSource = fontSource;

            GameObject go = new GameObject("BoneAndEmber_StatusChips", typeof(RectTransform));
            _row = (RectTransform)go.transform;
            _row.SetParent(barRoot, false);
            _row.anchorMin = new Vector2(0f, 1f);
            _row.anchorMax = new Vector2(0f, 1f);
            _row.pivot = new Vector2(0f, 0f);
            _row.sizeDelta = new Vector2(0f, _height);
            _row.anchoredPosition = Vector2.zero;

            if (_fontSource == null)
            {
                Plugin.Log.LogWarning("chips: no font source available, chips will have icons only.");
            }

            _built = true;
            Plugin.Log.LogInfo("chips: row created under " + HudPath.Of(barRoot));
        }

        // Row bottom sits `offset` above the bar's top; chips wrap upward within
        // `rowWidth`, never pushing the bars down. Height and font follow the
        // health header so the row and the header read as one grid.
        internal void SetLayout(float offset, float rowWidth, float height, float fontSize)
        {
            if (_row == null) return;
            _row.anchoredPosition = new Vector2(0f, offset);
            _rowWidth = rowWidth;

            if (!Mathf.Approximately(height, _height) || !Mathf.Approximately(fontSize, _fontSize))
            {
                _height = height;
                _fontSize = fontSize;
                _row.sizeDelta = new Vector2(0f, _height);
                foreach (Chip chip in _chips.Values) ApplyChipStyle(chip);
            }
        }

        private void ApplyChipStyle(Chip chip)
        {
            chip.Root.sizeDelta = new Vector2(chip.Width, _height);
            chip.Icon.rectTransform.sizeDelta = new Vector2(_fontSize, _fontSize);
            if (chip.Name != null) chip.Name.fontSize = _fontSize;
            if (chip.Time != null) chip.Time.fontSize = _fontSize;
        }

        internal void SetVisible(bool visible)
        {
            if (_row != null && _row.gameObject.activeSelf != visible) _row.gameObject.SetActive(visible);
        }

        internal void Update(List<StatusEffect> effects, float dt)
        {
            if (!_built) return;

            foreach (Chip chip in _chips.Values) chip.Present = false;

            for (int i = 0; i < effects.Count; i++)
            {
                StatusEffect se = effects[i];
                if (se == null) continue;

                int hash = se.NameHash();
                if (ExcludeHash != 0 && hash == ExcludeHash) continue;

                Chip chip;
                if (!_chips.TryGetValue(hash, out chip))
                {
                    chip = CreateChip(se, hash);
                    _chips.Add(hash, chip);
                }

                Refresh(chip, se);
                chip.Present = true;
            }

            // Slice 9: a chip that just left `effects` still occupies its slot
            // until its fade-out finishes (Alpha reaches 0 below), so neighbors
            // don't snap sideways the instant the effect ends. Present chips
            // keep vanilla's own effect order; fading ones are appended after
            // their badness group since where exactly they land no longer
            // matters once they're on their way out.
            _ordered.Clear();
            for (int pass = 0; pass < 2; pass++)
            {
                bool wantBad = pass == 0;
                for (int i = 0; i < effects.Count; i++)
                {
                    StatusEffect se = effects[i];
                    if (se == null) continue;
                    Chip chip;
                    if (!_chips.TryGetValue(se.NameHash(), out chip)) continue;
                    if (chip.Bad != wantBad) continue;
                    _ordered.Add(chip);
                }

                foreach (Chip chip in _chips.Values)
                {
                    if (chip.Bad != wantBad || chip.Present) continue;
                    if (chip.Alpha <= 0f) continue;
                    _ordered.Add(chip);
                }
            }

            float gap = Plugin.ChipGap.Value;
            float x = 0f;
            float y = 0f;
            for (int i = 0; i < _ordered.Count; i++)
            {
                Chip chip = _ordered[i];
                if (x > 0f && x + chip.Width > _rowWidth)
                {
                    x = 0f;
                    y += _height + gap; // wrap upward: the row grows away from the bars
                }
                chip.Root.anchoredPosition = new Vector2(x, y);
                x += chip.Width + gap;
            }

            _toRemove.Clear();
            foreach (Chip chip in _chips.Values)
            {
                float target = chip.Present ? (chip.Bad ? 1f : DimAlpha) : 0f;
                chip.Alpha = Mathf.MoveTowards(chip.Alpha, target, dt / FadeDuration);
                chip.Group.alpha = chip.Alpha;

                if (!chip.Present && chip.Alpha <= 0f) _toRemove.Add(chip.Hash);
            }

            for (int i = 0; i < _toRemove.Count; i++)
            {
                Chip chip = _chips[_toRemove[i]];
                Plugin.Log.LogInfo("chips: " + chip.AssetName + " ended, chip removed.");
                Object.Destroy(chip.Root.gameObject);
                _chips.Remove(_toRemove[i]);
            }
        }

        private static bool IsBad(StatusEffect se)
        {
            if (se is SE_Wet || se is SE_Frost || se is SE_Poison || se is SE_Burning ||
                se is SE_Smoke || se is SE_Puke || se is SE_Harpooned)
            {
                return true;
            }

            if (BadNames.Contains(se.name)) return true;

            SE_Stats stats = se as SE_Stats;
            if (stats != null)
            {
                if (stats.m_speedModifier < 0f) return true;
                if (stats.m_healthRegenMultiplier < 1f) return true;
                if (stats.m_staminaRegenMultiplier < 1f) return true;
                if (stats.m_eitrRegenMultiplier < 1f) return true;
                if (stats.m_staminaDrainPerSec > 0f) return true;
                if (stats.m_healthPerTick < 0f) return true;
                if (stats.m_damageModifier < 1f) return true;
            }

            return se.m_flashIcon;
        }

        private Chip CreateChip(StatusEffect se, int hash)
        {
            Chip chip = new Chip { Hash = hash, AssetName = se.name, Bad = IsBad(se) };

            GameObject go = new GameObject("BoneAndEmber_Chip_" + se.name, typeof(RectTransform), typeof(CanvasGroup));
            chip.Root = (RectTransform)go.transform;
            chip.Root.SetParent(_row, false);
            chip.Root.anchorMin = Vector2.zero;
            chip.Root.anchorMax = Vector2.zero;
            chip.Root.pivot = Vector2.zero;
            chip.Root.sizeDelta = new Vector2(60f, _height);
            chip.Group = go.GetComponent<CanvasGroup>();
            chip.Group.alpha = 0f;
            chip.Group.blocksRaycasts = false;
            chip.Group.interactable = false;

            chip.Outline = MakeImage("Outline", chip.Root, ChipSprite.Get(), Palette.EmberBad, Image.Type.Sliced);
            Stretch(chip.Outline.rectTransform, -1f);

            chip.Plate = MakeImage("Plate", chip.Root, ChipSprite.Get(), Palette.Plate, Image.Type.Sliced);
            Stretch(chip.Plate.rectTransform, 0f);

            chip.Icon = MakeImage("Icon", chip.Root, se.m_icon, Color.white, Image.Type.Simple);
            RectTransform icon = chip.Icon.rectTransform;
            icon.anchorMin = new Vector2(0f, 0.5f);
            icon.anchorMax = new Vector2(0f, 0.5f);
            icon.pivot = new Vector2(0f, 0.5f);
            icon.sizeDelta = new Vector2(_fontSize, _fontSize);
            icon.anchoredPosition = new Vector2(Padding, 0f);
            chip.Icon.preserveAspect = true;

            if (_fontSource != null)
            {
                chip.Name = MakeText("Name", chip.Root, Palette.Bone);
                Color timeColor = Palette.Bone;
                timeColor.a = TimeAlpha;
                chip.Time = MakeText("Time", chip.Root, timeColor);
            }

            Plugin.Log.LogInfo(
                "chips: effect \"" + Localization.instance.Localize(se.m_name) + "\" asset=" + se.name +
                " hash=" + hash + " type=" + se.GetType().Name + " bad=" + chip.Bad +
                " flashIcon=" + se.m_flashIcon + " iconText=\"" + se.GetIconText() + "\"" +
                " -> chip under " + HudPath.Of(_row));

            return chip;
        }

        private void Refresh(Chip chip, StatusEffect se)
        {
            chip.Outline.gameObject.SetActive(chip.Bad);
            if (chip.Icon.sprite != se.m_icon) chip.Icon.sprite = se.m_icon;

            float x = Padding + _fontSize;

            if (chip.Name != null)
            {
                string name = Localization.instance.Localize(se.m_name);
                if (chip.Name.text != name) chip.Name.text = name;
                float w = chip.Name.GetPreferredValues(name).x;
                chip.Name.rectTransform.anchoredPosition = new Vector2(x + InnerGap, 0f);
                chip.Name.rectTransform.sizeDelta = new Vector2(w, _height);
                x += InnerGap + w;
            }

            if (chip.Time != null)
            {
                string time = se.GetIconText();
                bool hasTime = !string.IsNullOrEmpty(time);
                if (chip.Time.gameObject.activeSelf != hasTime) chip.Time.gameObject.SetActive(hasTime);
                if (hasTime)
                {
                    if (chip.Time.text != time) chip.Time.text = time;
                    float w = chip.Time.GetPreferredValues(time).x;
                    chip.Time.rectTransform.anchoredPosition = new Vector2(x + InnerGap, 0f);
                    chip.Time.rectTransform.sizeDelta = new Vector2(w, _height);
                    x += InnerGap + w;
                }
            }

            chip.Width = x + Padding + 4f; // extra room so the clipped corner doesn't eat the last glyph
            chip.Root.sizeDelta = new Vector2(chip.Width, _height);
        }

        private static Image MakeImage(string name, Transform parent, Sprite sprite, Color color, Image.Type type)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Image img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.type = type;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private TMP_Text MakeText(string name, Transform parent, Color color)
        {
            TMP_Text text = Object.Instantiate(_fontSource, parent);
            text.gameObject.name = name;
            text.gameObject.SetActive(true);
            text.fontSize = _fontSize;
            text.color = color;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.characterSpacing = 0f;
            text.fontStyle = FontStyles.Normal;
            HudFont.Apply(text);

            RectTransform rt = text.rectTransform;
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(40f, _height);
            return text;
        }

        private static void Stretch(RectTransform rt, float inset)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }
    }
}
