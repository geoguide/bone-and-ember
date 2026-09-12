using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoneAndEmber
{
    // A vanilla bar (health, stamina, ...) cloned from Hud.m_healthBarRoot, the
    // only plain horizontal-bar prefab vanilla ships (Minimal UI reuses it for
    // its own stamina clone too, confirmed in its decompile). Strips whatever
    // rotation/scale the source prefab carries, see the "Geometry" note in
    // docs/design/001-always-on-hud.md, and sizes/colors itself instead of
    // trusting what the prefab handed us.
    //
    // Not a MonoBehaviour: a plain holder for the pieces AlwaysOnHud drives
    // every frame. Two live at once, one for health, one for stamina.
    internal class ClonedBar
    {
        internal RectTransform Root { get; private set; }
        internal GuiBar Fast { get; private set; }
        internal GuiBar Slow { get; private set; }
        internal TMP_Text Text { get; private set; }

        // Everything that must be cut by the angled right end lives under Clip:
        // the track, both fills, and (parented later by FoodSegments) the food
        // fill, notches and highlights. Headers, glow and captions stay on Root.
        internal RectTransform Clip { get; private set; }

        private string _label;
        private Image _mask;
        private int _maskW = -1;
        private int _maskH = -1;
        private float _maskAngle = float.NaN;

        internal void Build(RectTransform source, Transform parent, string label, string objectName, bool includeText)
        {
            _label = label;

            LogRect(_label + " source root", source);
            LogRect(_label + " source fill (fast/bar)", FindChildRect(source, "fast/bar"));

            Root = Object.Instantiate(source, parent);
            Root.gameObject.name = objectName;
            // The source can be inactive (Minimal UI disables that subtree), and a
            // clone of an inactive object is born inactive.
            Root.gameObject.SetActive(true);

            LogRect(_label + " clone root, as inherited", Root);

            Fast = FindBar(Root, "fast");
            Slow = FindBar(Root, "slow");
            if (Fast == null || Slow == null)
            {
                Plugin.Log.LogWarning(_label + ": cloned bar is missing its fast/slow GuiBar children, the fill will not animate.");
            }
            else
            {
                LogRect(_label + " clone fill (fast/bar), as inherited", Fast.m_bar);
            }

            NormalizeGeometry();
            SilenceAnimators();
            FlattenSprites();
            SetUpText(includeText);
            BuildClip();

            LogRect(_label + " clone root, after normalize", Root);
            if (Fast != null) LogRect(_label + " clone fill (fast/bar), after normalize", Fast.m_bar);

            Plugin.Log.LogInfo(_label + " bar cloned from " + HudPath.Of(source) + " to " + HudPath.Of(Root));
        }

        // The source prefab carries baked rotation/scale that's Unity scene data,
        // invisible in the decompiled C#, which is how slice 2 shipped a vertical
        // bar. Strip every transform in the clone back to identity, then
        // re-establish the geometry we actually want: root anchored to its
        // parent's bottom-left corner, decorative frame pieces (bkg, border)
        // stretched to whatever size we give the root, and the fill images
        // stretched to the root's full height. GuiBar.SetWidth only ever touches
        // the fill's X size (RectTransform.Axis.Horizontal is hardcoded in
        // GuiBar.cs), never Y, so without this the fill keeps an inherited height
        // regardless of what we set on the root.
        private void NormalizeGeometry()
        {
            RectTransform[] all = Root.GetComponentsInChildren<RectTransform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                all[i].localRotation = Quaternion.identity;
                all[i].localScale = Vector3.one;
            }

            Root.anchorMin = Vector2.zero;
            Root.anchorMax = Vector2.zero;
            Root.pivot = Vector2.zero;

            StretchToParent(FindChildRect(Root, "bkg"));
            StretchToParent(FindChildRect(Root, "border"));

            if (Fast != null) StretchFillHeight(Fast.m_bar);
            if (Slow != null) StretchFillHeight(Slow.m_bar);
        }

        // An Animator writes its animated properties every frame, even sitting in
        // its default state, which would fight anything else that sets the same
        // RectTransform properties (this is the leading suspect behind the stamina
        // bar drifting toward the bottom of the screen while draining, reported in
        // game: a "Visible" bool defaulting to false could drive it straight into a
        // hidden-state pose the first time it got a chance to evaluate). Disabling
        // alone stops it from evaluating, but destroying it outright removes any
        // chance of it being re-enabled or evaluated later by anything we didn't
        // anticipate. We do not drive vanilla's flash triggers; slice 4 does its own.
        private void SilenceAnimators()
        {
            Animator[] animators = Root.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; i++)
            {
                Animator anim = animators[i];
                string controllerName = anim.runtimeAnimatorController != null
                    ? anim.runtimeAnimatorController.name
                    : "(none)";
                Plugin.Log.LogInfo(
                    _label + ": found inherited Animator on " + HudPath.Of(anim.transform) +
                    " (controller: " + controllerName + "), disabling and removing it.");
                anim.enabled = false;
                Object.Destroy(anim);
            }

            if (animators.Length == 0)
            {
                Plugin.Log.LogInfo(_label + ": no inherited Animator found on the clone.");
            }
        }

        // Vanilla parents its number inside the fill (fast/bar), the very object
        // whose width changes, so the label rides the fill edge. Worse, on a clone
        // nothing drives it, so it just shows whatever number it had at clone time
        // (the stamina bar saying "45" forever, reported in game). Every inherited
        // label gets switched off, whether or not we want a number, and if we do
        // want one it's a fresh copy parented to Root with a fixed anchor.
        private void SetUpText(bool includeText)
        {
            TMP_Text[] inherited = Root.GetComponentsInChildren<TMP_Text>(true);
            TMP_Text source = inherited.Length > 0 ? inherited[0] : null;

            if (includeText)
            {
                if (source == null)
                {
                    Plugin.Log.LogWarning(_label + ": no TextMeshPro text found in the cloned bar, numbers are off for this session.");
                }
                else
                {
                    Text = Object.Instantiate(source, Root);
                    Text.gameObject.name = "BoneAndEmber_" + _label + "Text";

                    RectTransform rt = Text.rectTransform;
                    rt.anchorMin = new Vector2(0f, 0.5f);
                    rt.anchorMax = new Vector2(0f, 0.5f);
                    rt.pivot = new Vector2(0f, 0f);
                    rt.anchoredPosition = new Vector2(2f, 10f);
                    Text.alignment = TextAlignmentOptions.BottomLeft;
                    Text.gameObject.SetActive(true);

                    Plugin.Log.LogInfo(_label + ": created text " + Text.gameObject.name + " under " + HudPath.Of(Text.transform.parent));
                }
            }

            for (int i = 0; i < inherited.Length; i++)
            {
                Plugin.Log.LogInfo(_label + ": disabling inherited text at " + HudPath.Of(inherited[i].transform) + " (was \"" + inherited[i].text + "\")");
                inherited[i].gameObject.SetActive(false);
            }
        }

        // Slice 8 bar ends: a Mask on a stretched child cuts the right end at an
        // angle. uGUI's RectMask2D is rectangular only, so it's a real Mask driven
        // by an Image whose sprite is a rectangle with one leaned edge, generated
        // at the bar's exact pixel size (EndCapSprite). Track and fills move under
        // it; their anchors are relative to a rect identical to Root's, so nothing
        // shifts.
        private void BuildClip()
        {
            GameObject go = new GameObject("Clip", typeof(RectTransform), typeof(Image), typeof(Mask));
            Clip = (RectTransform)go.transform;
            Clip.SetParent(Root, false);
            Clip.anchorMin = Vector2.zero;
            Clip.anchorMax = Vector2.one;
            Clip.offsetMin = Vector2.zero;
            Clip.offsetMax = Vector2.zero;
            Clip.SetAsFirstSibling();

            _mask = go.GetComponent<Image>();
            _mask.raycastTarget = false;
            Mask mask = go.GetComponent<Mask>();
            mask.showMaskGraphic = false;

            RectTransform bkg = FindChildRect(Root, "bkg");
            Transform[] move = { bkg, Fast != null ? Fast.transform : null, Slow != null ? Slow.transform : null };
            for (int i = 0; i < move.Length; i++)
            {
                if (move[i] == null) continue;
                move[i].SetParent(Clip, false);
                Plugin.Log.LogInfo(_label + ": moved " + move[i].name + " under " + HudPath.Of(Clip));
            }
        }

        // Fixed size, not vanilla's (value / 25) * 32 that grows the bar with max
        // health. Only Root's sizeDelta needs setting for both dimensions: the
        // clip, track and fills are stretch-anchored to it, and the fill's height
        // is too, so only its width needs driving, which GuiBar.SetWidth already
        // does. The mask sprite is regenerated when size or angle change.
        internal void ApplySize(float width, float height, float endAngle)
        {
            if (Root == null) return;
            Root.sizeDelta = new Vector2(width, height);
            if (Fast != null) Fast.SetWidth(width);
            if (Slow != null) Slow.SetWidth(width);

            int w = Mathf.CeilToInt(width);
            int h = Mathf.CeilToInt(height);
            if (_mask != null && (w != _maskW || h != _maskH || !Mathf.Approximately(endAngle, _maskAngle)))
            {
                _maskW = w;
                _maskH = h;
                _maskAngle = endAngle;
                _mask.sprite = EndCapSprite.Get(w, h, endAngle);
                _mask.type = Image.Type.Simple;
            }
        }

        internal void SetValue(float value, float max)
        {
            if (Fast != null)
            {
                Fast.SetMaxValue(max);
                Fast.SetValue(value);
            }

            if (Slow != null)
            {
                Slow.SetMaxValue(max);
                Slow.SetValue(value);
            }
        }

        // GuiBar already caches its own fill Image (m_barImage, set in its own
        // Awake) and exposes SetColor for it, so use that instead of reaching into
        // m_bar ourselves.
        internal void SetColor(Color fastColor, Color slowColor)
        {
            if (Fast != null) Fast.SetColor(fastColor);
            if (Slow != null) Slow.SetColor(slowColor);
        }

        // Vanilla's bar art carries two things we don't want. The fill and track
        // sprites are not flat white: they have color and tick marks baked in, and
        // Image.color multiplies against the texture, so our ember tint on top of
        // vanilla's own red came out dark maroon, and the baked ticks read as ruler
        // marks next to our meal notches ("I'm past it but still see it", from the
        // spec addendum). Clear the fill and track sprites to Unity's flat white
        // default so Image.color is the real color, hide the border (a flat
        // rectangle the size of the bar would just cover everything), and hide any
        // other inherited image we didn't expect. The only marks left on the bar
        // are the ones we draw.
        private void FlattenSprites()
        {
            RectTransform fastFill = Fast != null ? Fast.m_bar : null;
            RectTransform slowFill = Slow != null ? Slow.m_bar : null;
            RectTransform bkg = FindChildRect(Root, "bkg");
            RectTransform border = FindChildRect(Root, "border");

            Image[] images = Root.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                Image img = images[i];
                string spriteName = img.sprite != null ? img.sprite.name : "(none)";
                RectTransform rt = img.rectTransform;

                if (rt == fastFill || rt == slowFill)
                {
                    img.sprite = null;
                    img.type = Image.Type.Simple;
                    Plugin.Log.LogInfo(_label + ": flattened fill " + HudPath.Of(rt) + " (was sprite " + spriteName + ")");
                }
                else if (rt == bkg)
                {
                    img.sprite = null;
                    img.type = Image.Type.Simple;
                    img.color = Palette.Track;
                    Plugin.Log.LogInfo(_label + ": flattened track " + HudPath.Of(rt) + " (was sprite " + spriteName + ")");
                }
                else if (rt == border)
                {
                    img.gameObject.SetActive(false);
                    Plugin.Log.LogInfo(_label + ": hid border " + HudPath.Of(rt) + " (was sprite " + spriteName + ")");
                }
                else
                {
                    img.gameObject.SetActive(false);
                    Plugin.Log.LogInfo(_label + ": hid unexpected inherited image " + HudPath.Of(rt) + " (was sprite " + spriteName + ")");
                }
            }
        }

        // Decorative frame pieces are not managed by GuiBar. Rather than guess
        // their pixel size from a prefab we now distrust, make them fill whatever
        // size we give the root, so they always match it.
        private static void StretchToParent(RectTransform rt)
        {
            if (rt == null) return;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        // Stretches a fill image to the root's full height while leaving its X
        // anchoring alone, so GuiBar's own width management (X only) keeps
        // working unmodified and the fill's height just follows whatever we give
        // the root automatically, instead of hardcoding a second number that has
        // to be kept in sync.
        private static void StretchFillHeight(RectTransform bar)
        {
            Vector2 anchorMin = bar.anchorMin;
            Vector2 anchorMax = bar.anchorMax;
            bar.anchorMin = new Vector2(anchorMin.x, 0f);
            bar.anchorMax = new Vector2(anchorMax.x, 1f);
            bar.offsetMin = new Vector2(bar.offsetMin.x, 0f);
            bar.offsetMax = new Vector2(bar.offsetMax.x, 0f);
        }

        private static GuiBar FindBar(Transform root, string child)
        {
            Transform t = root.Find(child);
            return t != null ? t.GetComponent<GuiBar>() : null;
        }

        private static RectTransform FindChildRect(Transform root, string path)
        {
            if (root == null) return null;
            Transform t = root.Find(path);
            return t != null ? t.GetComponent<RectTransform>() : null;
        }

        private static void LogRect(string label, RectTransform rt)
        {
            if (rt == null)
            {
                Plugin.Log.LogInfo("[rect] " + label + ": not found");
                return;
            }

            Plugin.Log.LogInfo(
                "[rect] " + label + " (" + HudPath.Of(rt) + "): " +
                "rotation=" + rt.localEulerAngles.ToString("F1") +
                " scale=" + rt.localScale.ToString("F2") +
                " anchorMin=" + rt.anchorMin.ToString("F2") +
                " anchorMax=" + rt.anchorMax.ToString("F2") +
                " pivot=" + rt.pivot.ToString("F2") +
                " sizeDelta=" + rt.sizeDelta.ToString("F1") +
                " rect=" + rt.rect.size.ToString("F1"));
        }
    }
}
