using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoneAndEmber
{
    // Slices 2 to 4 of docs/design/002-waypoint-strip.md: a thin bone rule across
    // the top center with N/E/S/W ticks that slide as you turn. The death markers
    // hanging under it are DeathMarkers, and whether one still counts as live is
    // RecoveredDeaths.
    //
    // A MonoBehaviour on the vanilla Hud object (added by CompassStripPatch), so it
    // lives and dies with the game session and gets a free per-frame LateUpdate.
    //
    // Heading comes from the camera, not the player, because that's what vanilla's
    // own map marker uses (Minimap.cs:1178) and because the strip should agree with
    // what's on screen rather than with which way the body happens to face. World
    // +Z is north and +X is east (Minimap.WorldToMapPoint).
    internal class CompassStrip : MonoBehaviour
    {
        // MessageHud can wake after Hud, so the font source is resolved lazily.
        // Look for a while, then give up rather than checking forever.
        private const int FontSearchFrames = 600;

        private const float LineHeight = 2f;
        private const float TickWidth = 2f;
        private const float TickHeight = 6f;
        private const float LabelGap = 3f;

        // Where the edge fade starts, as a fraction of half the strip width. Ticks
        // ramp to nothing over the last stretch so labels fade out instead of
        // popping off the end.
        private const float FadeStart = 0.80f;

        private const float LineAlpha = 0.35f;
        private const float TickAlpha = 0.55f;
        private const float LabelAlpha = 0.9f;

        private const float HeadingLogInterval = 2f;

        // 0 is north because bearings are measured off world +Z.
        private static readonly float[] CardinalBearings = { 0f, 90f, 180f, 270f };
        private static readonly string[] CardinalNames = { "N", "E", "S", "W" };

        private class Cardinal
        {
            public RectTransform Root;
            public CanvasGroup Group;
            public TMP_Text Label;
        }

        internal static CompassStrip Instance;

        private Hud _hud;
        private Transform _parent;
        private RectTransform _root;
        private RectTransform _canvasRect;
        private Image _line;
        private Cardinal[] _cardinals;
        private readonly DeathMarkers _markers = new DeathMarkers();

        private bool _built;
        private bool _gaveUp;
        private int _searchedFrames;
        private int _appliedFontVersion = -1;
        private float _nextHeadingLog;

        // Cached so a config value that hasn't moved doesn't cost a RectTransform
        // write every frame. Same approach as AlwaysOnHud.ApplyTransform.
        private float _appliedWidth = -1f;
        private float _appliedOffsetY = float.NaN;
        private float _appliedLabelSize = -1f;

        private void Awake()
        {
            Instance = this;
            _hud = GetComponent<Hud>();
            if (_hud == null)
            {
                Plugin.Log.LogError("CompassStrip was added to something that is not the Hud, giving up.");
                enabled = false;
                return;
            }

            // hudroot, which vanilla slides off screen for cutscenes and the
            // Ctrl+F3 hide (Hud.SetVisible). Map, inventory, menu and death are not
            // covered by that, so ShouldHide handles those itself.
            _parent = _hud.m_rootObject != null ? _hud.m_rootObject.transform : _hud.transform;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void LateUpdate()
        {
            if (_hud == null || _gaveUp) return;

            bool on = Plugin.WaypointStripEnabled.Value;
            if (!on)
            {
                if (_root != null) _root.gameObject.SetActive(false);
                return;
            }

            if (!_built && !Build()) return;

            if (ShouldHide())
            {
                if (_root.gameObject.activeSelf) _root.gameObject.SetActive(false);
                _markers.Hide();
                return;
            }

            Camera cam = Utils.GetMainCamera();
            if (cam == null) return;

            ApplyFontIfChanged();
            ApplyLayoutIfChanged();

            float yaw = cam.transform.eulerAngles.y;
            float halfWidth = Plugin.StripWidth.Value * CanvasWidth() * 0.5f;
            float halfFov = Mathf.Max(1f, Plugin.StripFovDegrees.Value * 0.5f);

            // Markers update before the visibility decision because their count is
            // what decides it: with AlwaysShowCompass off, the strip only exists
            // while there is something on it.
            _markers.Update(yaw, halfWidth, halfFov, Plugin.LabelSize.Value);

            bool visible = Plugin.AlwaysShowCompass.Value || _markers.Count > 0;
            if (_root.gameObject.activeSelf != visible) _root.gameObject.SetActive(visible);
            if (!visible) return;

            UpdateCardinals(yaw, halfWidth, halfFov);
        }

        private static bool ShouldHide()
        {
            Player player = Player.m_localPlayer;
            if (player == null || player.IsDead()) return true;
            if (Minimap.IsOpen()) return true;
            if (InventoryGui.IsVisible()) return true;
            if (Menu.IsVisible()) return true;
            return false;
        }

        // Slides every cardinal to where it belongs for the camera's current yaw and
        // fades the ones nearing the ends. Four objects, built once, never
        // reallocated.
        private void UpdateCardinals(float yaw, float halfWidth, float halfFov)
        {
            for (int i = 0; i < _cardinals.Length; i++)
            {
                Cardinal c = _cardinals[i];
                float rel = Mathf.DeltaAngle(yaw, CardinalBearings[i]); // -180..180
                float t = rel / halfFov;                                // -1..1 on strip

                bool onStrip = Mathf.Abs(t) <= 1f;
                if (c.Root.gameObject.activeSelf != onStrip) c.Root.gameObject.SetActive(onStrip);
                if (!onStrip) continue;

                c.Root.anchoredPosition = new Vector2(t * halfWidth, 0f);
                c.Group.alpha = EdgeFade(Mathf.Abs(t));
            }

            if (Plugin.DevCommands.Value && Time.time >= _nextHeadingLog)
            {
                _nextHeadingLog = Time.time + HeadingLogInterval;
                Plugin.Log.LogInfo("compass: camera yaw " + yaw.ToString("0.0") +
                                   " deg, strip half-width " + halfWidth.ToString("0") +
                                   " px over +/-" + halfFov.ToString("0") + " deg, " +
                                   _markers.Count + " death marker(s)");
            }
        }

        private static float EdgeFade(float t)
        {
            if (t <= FadeStart) return 1f;
            return Mathf.Clamp01(1f - (t - FadeStart) / (1f - FadeStart));
        }

        // The canvas the HUD is drawn on, not the raw screen: the canvas scaler may
        // be working in reference pixels, and every RectTransform here is in canvas
        // units.
        private float CanvasWidth()
        {
            if (_canvasRect == null) return Screen.width;

            float width = _canvasRect.rect.width;
            return width > 1f ? width : Screen.width;
        }

        private void ApplyLayoutIfChanged()
        {
            float width = Plugin.StripWidth.Value * CanvasWidth();
            float offsetY = Plugin.StripOffsetY.Value;
            float labelSize = Plugin.LabelSize.Value;

            bool changed = !Mathf.Approximately(width, _appliedWidth) ||
                           !Mathf.Approximately(offsetY, _appliedOffsetY) ||
                           !Mathf.Approximately(labelSize, _appliedLabelSize);
            if (!changed) return;

            _appliedWidth = width;
            _appliedOffsetY = offsetY;
            _appliedLabelSize = labelSize;

            float labelHeight = Mathf.Ceil(labelSize * 1.2f);
            float height = LineHeight + TickHeight + LabelGap + labelHeight;

            _root.sizeDelta = new Vector2(width, height);
            _root.anchoredPosition = new Vector2(0f, -offsetY);

            for (int i = 0; i < _cardinals.Length; i++)
            {
                TMP_Text label = _cardinals[i].Label;
                label.fontSize = labelSize;
                RectTransform lrt = label.rectTransform;
                lrt.sizeDelta = new Vector2(Mathf.Max(24f, labelSize * 3f), labelHeight);
                lrt.anchoredPosition = new Vector2(0f, TickHeight + LabelGap);
            }

            Plugin.Log.LogInfo("compass: layout width " + width.ToString("0") +
                               " px, offsetY " + offsetY.ToString("0") +
                               ", label " + labelSize.ToString("0.#"));
        }

        private void ApplyFontIfChanged()
        {
            if (_appliedFontVersion == HudFont.Version) return;
            HudFont.Refresh();
            _appliedFontVersion = HudFont.Version;
            HudFont.ApplyAll(_root);
        }

        private bool Build()
        {
            if (_parent == null)
            {
                Plugin.Log.LogWarning("compass: no HUD parent, strip is off for this session.");
                _gaveUp = true;
                return false;
            }

            TMP_Text fontSource = MessageHud.instance != null ? MessageHud.instance.m_messageCenterText : null;
            string fontFrom = "MessageHud.m_messageCenterText";
            if (fontSource == null)
            {
                AlwaysOnHud hud = AlwaysOnHud.Instance;
                fontSource = hud != null ? hud.HealthTextForFont : null;
                fontFrom = "health number (MessageHud not available)";
            }

            if (fontSource == null)
            {
                if (++_searchedFrames < FontSearchFrames) return false;
                Plugin.Log.LogWarning("compass: no font source after " + FontSearchFrames +
                                      " frames, strip is off for this session.");
                _gaveUp = true;
                return false;
            }

            GameObject go = new GameObject("BoneAndEmber_CompassStrip", typeof(RectTransform));
            _root = (RectTransform)go.transform;
            _root.SetParent(_parent, false);
            _root.anchorMin = new Vector2(0.5f, 1f);
            _root.anchorMax = new Vector2(0.5f, 1f);
            _root.pivot = new Vector2(0.5f, 1f);

            // The rule, pinned to the bottom of the root and stretched to its width,
            // so changing StripWidth moves the line for free.
            GameObject lineGo = new GameObject("Line", typeof(RectTransform), typeof(Image));
            lineGo.transform.SetParent(_root, false);
            _line = lineGo.GetComponent<Image>();
            _line.raycastTarget = false;
            _line.color = WithAlpha(Palette.Bone, LineAlpha);
            RectTransform lrt = _line.rectTransform;
            lrt.anchorMin = new Vector2(0f, 0f);
            lrt.anchorMax = new Vector2(1f, 0f);
            lrt.pivot = new Vector2(0.5f, 0f);
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = new Vector2(0f, LineHeight);

            _cardinals = new Cardinal[CardinalBearings.Length];
            for (int i = 0; i < _cardinals.Length; i++)
            {
                _cardinals[i] = BuildCardinal(CardinalNames[i], fontSource);
            }

            _markers.Build(_root, fontSource);

            // Resolved once, while the strip is still active in the hierarchy:
            // GetComponentInParent skips inactive objects, and the strip spends
            // plenty of frames switched off.
            Canvas canvas = _root.GetComponentInParent<Canvas>();
            _canvasRect = canvas != null ? canvas.rootCanvas.transform as RectTransform : null;
            if (_canvasRect == null)
            {
                Plugin.Log.LogWarning("compass: no parent Canvas found, falling back to Screen.width for StripWidth.");
            }

            _built = true;
            Plugin.Log.LogInfo("compass: strip created under " + HudPath.Of(_parent) +
                               ", font from " + fontFrom);
            return true;
        }

        private Cardinal BuildCardinal(string name, TMP_Text fontSource)
        {
            GameObject go = new GameObject("Cardinal_" + name, typeof(RectTransform), typeof(CanvasGroup));
            RectTransform root = (RectTransform)go.transform;
            root.SetParent(_root, false);
            // Anchored to the bottom center of the strip: x is driven per frame,
            // y stays on the line.
            root.anchorMin = new Vector2(0.5f, 0f);
            root.anchorMax = new Vector2(0.5f, 0f);
            root.pivot = new Vector2(0.5f, 0f);
            root.sizeDelta = Vector2.zero;

            CanvasGroup group = go.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            GameObject tickGo = new GameObject("Tick", typeof(RectTransform), typeof(Image));
            tickGo.transform.SetParent(root, false);
            Image tick = tickGo.GetComponent<Image>();
            tick.raycastTarget = false;
            tick.color = WithAlpha(Palette.Bone, TickAlpha);
            RectTransform trt = tick.rectTransform;
            trt.anchorMin = new Vector2(0.5f, 0f);
            trt.anchorMax = new Vector2(0.5f, 0f);
            trt.pivot = new Vector2(0.5f, 0f);
            trt.anchoredPosition = Vector2.zero;
            trt.sizeDelta = new Vector2(TickWidth, TickHeight);

            TMP_Text label = Instantiate(fontSource, root);
            label.gameObject.name = "Label";
            label.gameObject.SetActive(true);
            label.text = name;
            label.color = WithAlpha(Palette.Bone, LabelAlpha);
            label.alignment = TextAlignmentOptions.Bottom;
            // No letterspacing: on a one-character label TMP only adds trailing
            // space, which shifts a centered glyph off its tick.
            label.characterSpacing = 0f;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.enableAutoSizing = false;
            label.fontStyle = FontStyles.Normal;
            RectTransform lrt = label.rectTransform;
            lrt.anchorMin = new Vector2(0.5f, 0f);
            lrt.anchorMax = new Vector2(0.5f, 0f);
            lrt.pivot = new Vector2(0.5f, 0f);
            lrt.localRotation = Quaternion.identity;
            lrt.localScale = Vector3.one;

            // Vanilla text objects carry animators and other drivers; none of them
            // should be moving ours.
            foreach (Animator a in label.GetComponentsInChildren<Animator>(true)) Destroy(a);
            HudFont.Apply(label);

            return new Cardinal { Root = root, Group = group, Label = label };
        }

        private static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }
    }
}
