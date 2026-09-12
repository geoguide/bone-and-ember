using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoneAndEmber
{
    // Slice 7 of docs/design/001-always-on-hud.md: the loud moment. A bold
    // skewed slab near the top of the screen for a couple of seconds when
    // something happens that the player must act on, then only the chip remains.
    //
    // To add another loud moment, add one line to Table (keyed by the status
    // effect's asset name), or call Trigger(...) directly for a non-status
    // trigger like "about to die".
    internal class LoudMoments
    {
        internal class Moment
        {
            public readonly string Title;
            public readonly string Subtitle;
            public Moment(string title, string subtitle) { Title = title; Subtitle = subtitle; }
        }

        // Not in Table: this one isn't a status effect we observe, it's a
        // threshold the inventory crosses, so AlwaysOnHud fires it directly.
        internal static readonly Moment Encumbered = new Moment("ENCUMBERED", "Drop something to run again");

        private static readonly Dictionary<string, Moment> Table = new Dictionary<string, Moment>
        {
            { "Freezing", new Moment("FREEZING", "Find a fire or warmer clothes") },
        };

        // Effects whose vanilla start message the slab covers, but which are not
        // triggered from Table. Encumbered is fired by AlwaysOnHud watching the
        // weight threshold, yet it is still a real status effect with its own
        // "You are carrying too much" start message, so without this the slab
        // and vanilla's line both show.
        private static readonly HashSet<string> AlsoCovered = new HashSet<string> { "Encumbered" };

        private const float HoldSeconds = 2.5f;
        private const float InSeconds = 0.12f;
        private const float OutSeconds = 0.2f;
        private const float SkewDeg = 12f;
        private const float SlideDistance = 12f; // slice 9: slide in from this far above resting
        private const float PadX = 36f;
        private const float PadY = 14f;
        private const float TitleSize = 44f;
        private const float SubtitleSize = 18f;

        private static readonly Color TextColor = new Color(0.102f, 0.059f, 0.031f, 1f); // #1A0F08

        private enum Phase { Hidden, In, Hold, Out }

        private Transform _parent;
        private RectTransform _root;
        private CanvasGroup _group;
        private Image _plate;
        private TMP_Text _title;
        private TMP_Text _subtitle;
        private bool _built;

        private Phase _phase = Phase.Hidden;
        private float _phaseTime;
        private Tween _alpha;
        private Tween _slideOffset;

        private readonly HashSet<int> _active = new HashSet<int>();
        private readonly HashSet<int> _seen = new HashSet<int>();
        private bool _seeded;

        internal bool IsShowing => _phase != Phase.Hidden;

        // Start messages ("$se_freezing_start" and its localized form) of every
        // effect in Table, so StartMessageSuppressPatch can swallow vanilla's
        // "You are freezing!" when the slab is about to say the same thing.
        private static HashSet<string> _suppressed;

        internal static bool IsCoveredStartMessage(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            if (_suppressed == null)
            {
                ObjectDB db = ObjectDB.instance;
                if (db == null) return false; // try again next call
                _suppressed = new HashSet<string>();
                for (int i = 0; i < db.m_StatusEffects.Count; i++)
                {
                    StatusEffect se = db.m_StatusEffects[i];
                    if (se == null || string.IsNullOrEmpty(se.m_startMessage)) continue;
                    if (!Table.ContainsKey(se.name) && !AlsoCovered.Contains(se.name)) continue;
                    _suppressed.Add(se.m_startMessage);
                    _suppressed.Add(Localization.instance.Localize(se.m_startMessage));
                    Plugin.Log.LogInfo("loud: will suppress vanilla start message for " + se.name + ": \"" + se.m_startMessage + "\"");
                }
            }

            return _suppressed.Contains(text) || _suppressed.Contains(Localization.instance.Localize(text));
        }

        internal void Init(Transform parent)
        {
            _parent = parent;
        }

        // Fires on the frame an effect in Table first appears. The first call only
        // seeds what's already active (loading in while freezing shouldn't shout).
        internal void Observe(List<StatusEffect> effects)
        {
            _seen.Clear();
            for (int i = 0; i < effects.Count; i++)
            {
                StatusEffect se = effects[i];
                if (se == null) continue;
                int hash = se.NameHash();
                _seen.Add(hash);

                if (_seeded && !_active.Contains(hash))
                {
                    Moment moment;
                    if (Table.TryGetValue(se.name, out moment))
                    {
                        Plugin.Log.LogInfo("loud: " + se.name + " started");
                        Trigger(moment);
                    }
                }
            }

            _active.Clear();
            _active.UnionWith(_seen);
            _seeded = true;
        }

        internal void Trigger(Moment moment)
        {
            if (!Plugin.LoudMomentsEnabled.Value) return;
            if (IsShowing) return;

            Player player = Player.m_localPlayer;
            if (player == null || player.IsDead()) return;

            if (!_built && !Build()) return;

            _title.text = moment.Title;
            _subtitle.text = moment.Subtitle;
            Layout();

            _phaseTime = 0f;
            if (Plugin.MotionEnabled.Value)
            {
                _alpha.Start(0f, 1f, InSeconds * 1000f);
                _slideOffset.Start(-SlideDistance, 0f, InSeconds * 1000f);
                _phase = Phase.In;
            }
            else
            {
                _alpha.Snap(1f);
                _slideOffset.Snap(0f);
                _phase = Phase.Hold;
            }

            _root.gameObject.SetActive(true);
            Plugin.Log.LogInfo("loud: showing \"" + moment.Title + "\"");
        }

        internal void Update(float dt)
        {
            if (_phase == Phase.Hidden) return;

            Player player = Player.m_localPlayer;
            if (player == null || player.IsDead())
            {
                Hide();
                return;
            }

            // Reapplied while showing rather than only at build, so dragging the
            // slider in Configuration Manager moves the slab under you. The
            // slide-in offset rides on top as a separate component, not a
            // second writer of the same field.
            float restingY = -Plugin.LoudMomentOffsetY.Value;
            _root.anchoredPosition = new Vector2(0f, restingY + _slideOffset.Update(dt));

            switch (_phase)
            {
                case Phase.In:
                    _group.alpha = _alpha.Update(dt);
                    if (_alpha.Done) { _phase = Phase.Hold; _phaseTime = 0f; }
                    break;
                case Phase.Hold:
                    _phaseTime += dt;
                    if (_phaseTime >= HoldSeconds - InSeconds)
                    {
                        if (Plugin.MotionEnabled.Value)
                        {
                            _alpha.Start(_group.alpha, 0f, OutSeconds * 1000f);
                            _phase = Phase.Out;
                        }
                        else
                        {
                            _alpha.Snap(0f);
                            _group.alpha = 0f;
                            Hide();
                        }
                    }
                    break;
                case Phase.Out:
                    _group.alpha = _alpha.Update(dt);
                    if (_alpha.Done) Hide();
                    break;
            }
        }

        internal void ReapplyFont()
        {
            if (_root != null) HudFont.ApplyAll(_root);
        }

        internal void Hide()
        {
            _phase = Phase.Hidden;
            if (_root != null) _root.gameObject.SetActive(false);
        }

        // Font comes from vanilla's center message (MessageHud.m_messageCenterText),
        // resolved lazily because MessageHud may wake after Hud. Falls back to the
        // health number's font.
        private bool Build()
        {
            TMP_Text fontSource = MessageHud.instance != null ? MessageHud.instance.m_messageCenterText : null;
            string fontFrom = "MessageHud.m_messageCenterText";
            if (fontSource == null)
            {
                AlwaysOnHud hud = AlwaysOnHud.Instance;
                fontSource = hud != null ? hud.HealthTextForFont : null;
                fontFrom = "health number (MessageHud not available)";
            }
            if (fontSource == null || _parent == null)
            {
                Plugin.Log.LogWarning("loud: no font source or parent available, loud moments are off for this session.");
                return false;
            }

            GameObject go = new GameObject("BoneAndEmber_LoudMoment", typeof(RectTransform), typeof(CanvasGroup));
            _root = (RectTransform)go.transform;
            _root.SetParent(_parent, false);
            _root.anchorMin = new Vector2(0.5f, 1f);
            _root.anchorMax = new Vector2(0.5f, 1f);
            _root.pivot = new Vector2(0.5f, 0.5f);
            _root.anchoredPosition = new Vector2(0f, -Plugin.LoudMomentOffsetY.Value);
            _group = go.GetComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;

            GameObject plateGo = new GameObject("Plate", typeof(RectTransform), typeof(Image));
            plateGo.transform.SetParent(_root, false);
            _plate = plateGo.GetComponent<Image>();
            _plate.type = Image.Type.Simple;
            _plate.color = Palette.EmberBad;
            _plate.raycastTarget = false;
            RectTransform prt = _plate.rectTransform;
            prt.anchorMin = Vector2.zero;
            prt.anchorMax = Vector2.one;
            prt.offsetMin = Vector2.zero;
            prt.offsetMax = Vector2.zero;

            _title = MakeText("Title", fontSource, TitleSize, FontStyles.Bold);
            _subtitle = MakeText("Subtitle", fontSource, SubtitleSize, FontStyles.Normal);

            _root.gameObject.SetActive(false);
            _built = true;
            Plugin.Log.LogInfo("loud: slab created under " + HudPath.Of(_parent) + ", font from " + fontFrom);
            return true;
        }

        private TMP_Text MakeText(string name, TMP_Text fontSource, float size, FontStyles style)
        {
            TMP_Text text = Object.Instantiate(fontSource, _root);
            text.gameObject.name = name;
            text.gameObject.SetActive(true);
            text.text = "";
            text.fontSize = size;
            text.fontStyle = style;
            text.color = TextColor;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.enableAutoSizing = false;

            RectTransform rt = text.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localRotation = Quaternion.identity;
            rt.localScale = Vector3.one;

            // Some vanilla text objects carry extra components (Localize, outlines,
            // animators); none should drive ours.
            foreach (Animator a in text.GetComponentsInChildren<Animator>(true)) Object.Destroy(a);
            HudFont.Apply(text);

            Plugin.Log.LogInfo("loud: created text " + name + " under " + HudPath.Of(rt.parent));
            return text;
        }

        // Size the slab from the text, then draw the skewed plate at exactly that
        // size so the lean is really 12 degrees.
        private void Layout()
        {
            Vector2 titleSize = _title.GetPreferredValues(_title.text);
            Vector2 subSize = _subtitle.GetPreferredValues(_subtitle.text);

            float width = Mathf.Max(titleSize.x, subSize.x) + PadX * 2f;
            float height = titleSize.y + subSize.y + PadY * 2f + 2f;
            width += Mathf.Tan(SkewDeg * Mathf.Deg2Rad) * height; // room for the lean

            _root.sizeDelta = new Vector2(width, height);

            int w = Mathf.CeilToInt(width);
            int h = Mathf.CeilToInt(height);
            _plate.sprite = SkewSprite.Get(w, h, SkewDeg);

            _title.rectTransform.sizeDelta = new Vector2(titleSize.x + 4f, titleSize.y);
            _title.rectTransform.anchoredPosition = new Vector2(0f, (subSize.y + 2f) * 0.5f);
            _subtitle.rectTransform.sizeDelta = new Vector2(subSize.x + 4f, subSize.y);
            _subtitle.rectTransform.anchoredPosition = new Vector2(0f, -(titleSize.y + 2f) * 0.5f);
        }
    }
}
