using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoneAndEmber
{
    // Slice 3 of docs/design/001-always-on-hud.md: dividers marking base health
    // and each active food's current share of max, a pulsing highlight over a
    // food's own segment once it can be eaten again (CanEatAgain, half burn
    // time), and a caption for whichever meal is closest to running out.
    //
    // Not a MonoBehaviour: AlwaysOnHud owns one instance, parents its pieces
    // under the health bar's Root, and drives it from Hud.UpdateFood's postfix.
    internal class FoodSegments
    {
        private const int MaxFoods = 3;
        private const float DividerWidth = 2f;
        private const float CaptionThresholdSeconds = 120f;
        private const float CaptionGap = 6f;

        private Image _foodFill;
        private float _appliedAngle = float.NaN;
        private readonly RectTransform[] _dividers = new RectTransform[MaxFoods];
        private readonly Image[] _highlights = new Image[MaxFoods];
        private TMP_Text _caption;
        private bool _built;

        // fontSource can be null (the health bar failed to find a TMP source);
        // dividers and highlights don't need a font, only the caption does.
        // clipRoot: the bar's angled-end mask container. Food fill, notches and
        // highlights go there so the end cap cuts them too; the caption stays on
        // the bar root, under the bar.
        internal void Build(RectTransform barRoot, RectTransform clipRoot, TMP_Text fontSource)
        {
            RectTransform inside = clipRoot != null ? clipRoot : barRoot;

            // Created first so it renders under the dividers and highlights.
            _foodFill = CreateFoodFill(inside);

            for (int i = 0; i < MaxFoods; i++)
            {
                _dividers[i] = CreateDivider(inside, i);
                _highlights[i] = CreateHighlight(inside, i);
            }

            if (fontSource != null)
            {
                _caption = CreateCaption(barRoot, fontSource);
            }
            else
            {
                Plugin.Log.LogWarning("FoodSegments: no font source available, the caption is off for this session.");
            }

            _built = true;
        }

        // Caption sits at the bottom of the whole cluster, under the stamina bar
        // when there is one, so it never lands on the stamina header. Driven from
        // AlwaysOnHud's layout pass since only it knows where the column ends.
        internal void SetCaptionOffset(float yBelowHealthBar)
        {
            if (_caption != null) _caption.rectTransform.anchoredPosition = new Vector2(0f, -yBelowHealthBar);
        }

        internal void SetVisible(bool visible)
        {
            if (!_built || visible) return;

            for (int i = 0; i < MaxFoods; i++)
            {
                _dividers[i].gameObject.SetActive(false);
                _highlights[i].gameObject.SetActive(false);
            }

            if (_caption != null) _caption.gameObject.SetActive(false);
            if (_foodFill != null) _foodFill.gameObject.SetActive(false);
        }

        // Called from FoodSegmentsPatch, the postfix on Hud.UpdateFood, so we
        // read the same Player.Food values at the same moment vanilla does.
        internal void Update(Player player, bool critical)
        {
            if (!_built) return;

            var foods = player.GetFoods();
            float baseHp = player.GetBaseFoodHP();
            float maxHp = player.GetMaxHealth();
            if (maxHp <= 0f) maxHp = baseHp;

            UpdateFoodFill(player.GetHealth(), baseHp, maxHp, critical);

            float cumulative = baseHp;
            string urgentCaption = null;
            float urgentSeconds = float.MaxValue;

            for (int i = 0; i < MaxFoods; i++)
            {
                if (i >= foods.Count)
                {
                    _dividers[i].gameObject.SetActive(false);
                    _highlights[i].gameObject.SetActive(false);
                    continue;
                }

                Player.Food food = foods[i];

                // Divider marks the START of this food's segment, i.e. the boundary
                // with whatever came before it (base, or the previous food). The
                // end of the LAST food's segment is just the bar's own right edge,
                // so it never gets a divider of its own.
                float startFrac = Mathf.Clamp01(cumulative / maxHp);
                cumulative += food.m_health;
                float endFrac = Mathf.Clamp01(cumulative / maxHp);

                SetDivider(_dividers[i], startFrac);
                SetHighlight(_highlights[i], startFrac, endFrac, food.CanEatAgain());

                // food.m_time is in food-seconds; vanilla divides by Game.m_foodRate
                // before display (Hud.UpdateFood), match that here.
                float secondsLeft = food.m_time / Game.m_foodRate;
                if (secondsLeft < CaptionThresholdSeconds && secondsLeft < urgentSeconds)
                {
                    urgentSeconds = secondsLeft;
                    string name = Localization.instance.Localize(food.m_item.m_shared.m_name);
                    urgentCaption = name + " gone in " + FormatMinSec(secondsLeft);
                }
            }

            if (_caption == null) return;

            float labelSize = Plugin.LabelSize.Value;
            if (!Mathf.Approximately(_caption.fontSize, labelSize)) _caption.fontSize = labelSize;

            if (urgentCaption != null)
            {
                _caption.gameObject.SetActive(true);
                _caption.text = urgentCaption;
            }
            else
            {
                _caption.gameObject.SetActive(false);
            }
        }

        // The part of the current fill that is above base health, "on loan" from
        // meals, in the lighter shade. Spans [base, health] as fractions of max, so
        // it sits exactly on top of the fast fill's own extent past the base notch
        // and shrinks with it. When the whole bar is critical it takes the critical
        // color too, so the bar reads as one bright warning rather than two tones.
        private void UpdateFoodFill(float health, float baseHp, float maxHp, bool critical)
        {
            if (_foodFill == null) return;

            float baseFrac = Mathf.Clamp01(baseHp / maxHp);
            float healthFrac = Mathf.Clamp01(health / maxHp);
            bool show = healthFrac > baseFrac + 0.001f;

            if (_foodFill.gameObject.activeSelf != show) _foodFill.gameObject.SetActive(show);
            if (!show) return;

            RectTransform rt = _foodFill.rectTransform;
            rt.anchorMin = new Vector2(baseFrac, 0f);
            rt.anchorMax = new Vector2(healthFrac, 1f);

            Color c = critical ? Palette.EmberCritical : Plugin.FoodHealthColor.Value;
            c.a = 1f;
            if (_foodFill.color != c) _foodFill.color = c;
        }

        private static Image CreateFoodFill(RectTransform parent)
        {
            GameObject go = new GameObject("BoneAndEmber_FoodFill", typeof(RectTransform), typeof(Image));
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Image img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.color = Palette.FoodEmberDefault;

            go.SetActive(false);
            return img;
        }

        private void SetDivider(RectTransform divider, float frac)
        {
            divider.gameObject.SetActive(true);
            divider.anchorMin = new Vector2(frac, 0f);
            divider.anchorMax = new Vector2(frac, 1f);

            // Lean follows the bar's end cap angle unless NotchAngleOverride is on,
            // so the seam and the end agree. Live, so applied here rather than once
            // at creation. Compared against our own cached value, not
            // localEulerAngles.z, which Unity wraps to 0..360 and would never match
            // a negative setting.
            float angle = Plugin.NotchAngleOverride.Value ? Plugin.NotchAngle.Value : Plugin.BarEndAngle.Value;
            if (!Mathf.Approximately(_appliedAngle, angle))
            {
                _appliedAngle = angle;
                for (int i = 0; i < MaxFoods; i++)
                {
                    _dividers[i].localEulerAngles = new Vector3(0f, 0f, angle);
                }
            }
        }

        private static void SetHighlight(Image highlight, float startFrac, float endFrac, bool canEatAgain)
        {
            RectTransform rt = highlight.rectTransform;
            rt.anchorMin = new Vector2(startFrac, 0f);
            rt.anchorMax = new Vector2(endFrac, 1f);

            if (!canEatAgain)
            {
                highlight.gameObject.SetActive(false);
                return;
            }

            highlight.gameObject.SetActive(true);
            float alpha = 0.15f + Mathf.Abs(Mathf.Sin(Time.time * 6f)) * 0.35f;
            Color c = Palette.Bone;
            c.a = alpha;
            highlight.color = c;
        }

        private static string FormatMinSec(float seconds)
        {
            int total = Mathf.CeilToInt(Mathf.Max(0f, seconds));
            int m = total / 60;
            int s = total % 60;
            return m + ":" + s.ToString("D2");
        }

        // A thin, dark mark at a fixed fraction of the bar's
        // width. Dark rather than bone: it reads as a notch cut into the bright
        // fill, and disappears on its own against the (also dark) empty track,
        // no separate visibility logic needed. Anchored as a Y-stretch / X-point
        // (frac is the anchor, sizeDelta.x the pixel width around it), so it
        // tracks BarWidth changes automatically without us needing to recompute
        // pixels.
        private static RectTransform CreateDivider(RectTransform parent, int index)
        {
            GameObject go = new GameObject("BoneAndEmber_FoodDivider" + index, typeof(RectTransform), typeof(Image));
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(DividerWidth, 0f);

            Image img = go.GetComponent<Image>();
            Color c = Palette.DividerNotch;
            c.a = 0.75f;
            img.color = c;

            go.SetActive(false);
            return rt;
        }

        // Spans a food's segment as a fractional horizontal stretch, so it always
        // matches wherever that segment's boundaries land, at any BarWidth.
        private static Image CreateHighlight(RectTransform parent, int index)
        {
            GameObject go = new GameObject("BoneAndEmber_FoodHighlight" + index, typeof(RectTransform), typeof(Image));
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Image img = go.GetComponent<Image>();
            img.raycastTarget = false;

            go.SetActive(false);
            return img;
        }

        private static TMP_Text CreateCaption(RectTransform parent, TMP_Text fontSource)
        {
            TMP_Text caption = Object.Instantiate(fontSource, parent);
            caption.gameObject.name = "BoneAndEmber_FoodCaption";
            caption.fontSize = Plugin.LabelSize.Value;
            Color captionColor = Palette.Bone;
            captionColor.a = 0.85f;
            caption.color = captionColor;
            caption.alignment = TextAlignmentOptions.TopLeft;
            caption.textWrappingMode = TextWrappingModes.NoWrap;
            caption.characterSpacing = 0f;
            HudFont.Apply(caption);

            RectTransform rt = caption.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(0f, -CaptionGap);
            rt.sizeDelta = new Vector2(400f, 24f);

            caption.gameObject.SetActive(false);
            Plugin.Log.LogInfo("food: created caption " + caption.gameObject.name + " under " + HudPath.Of(parent) + " (fixed anchor, bottom-left of the bar)");
            return caption;
        }
    }
}
