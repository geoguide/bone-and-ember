using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoneAndEmber
{
    // docs/design/005-forsaken-power.md, slice 1: the Forsaken Power as a fourth
    // line of the cluster under stamina. Same header grammar as the bars (sigil
    // and boss name left, value right, thin bar under), but the bar fills
    // instead of draining: it is a resource recharging.
    //
    // Four states, in display precedence:
    //   None      no power chosen, the line is absent
    //   Active    the power's effect is on you: gold, draining over m_ttl
    //   Cooldown  bone at the dim weights, refilling over m_cooldown
    //   Ready     full gold, breathing slowly, with the bound key shown
    //
    // "Active" is decided by whether the effect is in the local player's status
    // effect list, not by whether you pressed the button, so a friend firing the
    // same power near you reads as active too.
    internal class PowerLine
    {
        internal enum State { None, Ready, Active, Cooldown }

        // Ready breathe: slow and shallow, about half the food pulse.
        private const float BreatheSpeed = 2f;      // radians/sec, ~3.1s period
        private const float BreatheMinAlpha = 0.78f;
        private const float DimAlpha = 0.7f;
        private const float TimeAlpha = 0.6f;
        private const float IconGap = 5f;

        internal RectTransform Root => _bar.Root;
        internal BarHeader Header => _header;
        internal State Current { get; private set; } = State.None;
        internal int PowerHash { get; private set; }

        private readonly ClonedBar _bar = new ClonedBar();
        private readonly BarHeader _header = new BarHeader();
        private Image _icon;
        private bool _built;

        private string _lastName;
        private State _appliedState = (State)(-1);
        private float _appliedIconSize = -1f;
        private bool _loggedKey;
        private string _keyText;

        internal void Build(RectTransform source, Transform parent, TMP_Text fontSource)
        {
            _bar.Build(source, parent, "power", "BoneAndEmber_PowerBar", includeText: false);
            _bar.SetColor(Palette.Gold, Palette.Gold);

            // Cloned from the health prefab: no damage trail on a bar that only
            // moves by the clock. Snap both fills.
            if (_bar.Fast != null) { _bar.Fast.m_smoothDrain = false; _bar.Fast.m_smoothFill = false; }
            if (_bar.Slow != null) { _bar.Slow.m_smoothDrain = false; _bar.Slow.m_smoothFill = false; }

            _header.Build(_bar.Root, fontSource, "", "BoneAndEmber_PowerHeader");

            if (_header.Root != null)
            {
                GameObject go = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                RectTransform rt = (RectTransform)go.transform;
                rt.SetParent(_header.Root, false);
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(0f, 0f);
                rt.pivot = new Vector2(0f, 0f);
                rt.anchoredPosition = Vector2.zero;
                _icon = go.GetComponent<Image>();
                _icon.raycastTarget = false;
                _icon.preserveAspect = true;
                _icon.color = Palette.Bone;
            }

            _built = true;
            Plugin.Log.LogInfo("power: line built under " + HudPath.Of(_bar.Root) + ", hidden until a power is chosen.");
        }

        internal bool ApplyStyle(float labelSize, float numberScale, float letterSpacing)
        {
            bool changed = _header.ApplyStyle(labelSize, numberScale, letterSpacing);

            // Sigil is 1em square, sitting on the label's baseline box, and the
            // label starts after it. Only the label moves; the number stays
            // right-aligned to the bar's right edge like the other headers.
            if (_icon != null && !Mathf.Approximately(labelSize, _appliedIconSize))
            {
                _appliedIconSize = labelSize;
                _icon.rectTransform.sizeDelta = new Vector2(labelSize, labelSize);
                _icon.rectTransform.anchoredPosition = new Vector2(0f, Mathf.Round(labelSize * 0.15f));
                _header.SetLabelOffset(labelSize + IconGap);
                changed = true;
            }

            return changed;
        }

        internal void ApplySize(float width, float height, float endAngle)
        {
            _bar.ApplySize(width, height, endAngle);
        }

        internal void SetVisible(bool visible)
        {
            if (Root != null && Root.gameObject.activeSelf != visible) Root.gameObject.SetActive(visible);
        }

        // Called from PowerLinePatch, the postfix on Hud.UpdateGuardianPower, so
        // we read the same power and cooldown vanilla just drew.
        internal void Update(Player player, float dt)
        {
            if (!_built || player == null) return;

            player.GetGuardianPowerHUD(out StatusEffect se, out float cooldown);
            if (se == null)
            {
                Transition(State.None, null, 0f, 0f);
                return;
            }

            PowerHash = se.NameHash();

            string name = Localization.instance.Localize(se.m_name);
            if (name != _lastName)
            {
                _lastName = name;
                _header.SetLabel(name);
                if (_icon != null) _icon.sprite = se.m_icon;
                Plugin.Log.LogInfo("power: chosen power is " + se.name + " (" + name + "), cooldown length " + se.m_cooldown + "s, active length " + se.m_ttl + "s.");
            }

            SEMan seman = player.GetSEMan();
            StatusEffect running = seman != null ? seman.GetStatusEffect(PowerHash) : null;

            if (running != null && running.m_ttl > 0f)
            {
                float remaining = Mathf.Max(0f, running.GetRemaningTime());
                Transition(State.Active, se, remaining, running.m_ttl);
                _bar.SetValue(remaining, running.m_ttl);
                _header.SetText(TimeText(remaining));
            }
            else if (cooldown > 0f)
            {
                float length = se.m_cooldown > 0f ? se.m_cooldown : cooldown;
                Transition(State.Cooldown, se, cooldown, length);
                _bar.SetValue(Mathf.Clamp(length - cooldown, 0f, length), length);
                _header.SetText(TimeText(cooldown));
            }
            else
            {
                Transition(State.Ready, se, 0f, 0f);
                _bar.SetValue(1f, 1f);
                _header.SetText(ReadyText());

                // Breathe: gold alpha rising and falling on both fills.
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * BreatheSpeed);
                Color c = Palette.Gold;
                c.a = Plugin.MotionEnabled.Value ? Mathf.Lerp(BreatheMinAlpha, 1f, pulse) : 1f;
                _bar.SetColor(c, c);
            }
        }

        private void Transition(State next, StatusEffect se, float remaining, float length)
        {
            if (next == Current) return;
            State prev = Current;
            Current = next;

            string what = se != null ? se.name : "(none)";
            Plugin.Log.LogInfo("power: " + prev + " -> " + next + " " + what +
                (length > 0f ? " (" + Mathf.CeilToInt(remaining) + "s of " + Mathf.CeilToInt(length) + "s)" : ""));

            ApplyStateStyle(next);
        }

        // Per-state colors, written on the transition only. Ready's breathe is
        // the one thing that keeps writing every frame, in Update.
        private void ApplyStateStyle(State state)
        {
            if (_appliedState == state) return;
            _appliedState = state;

            Color label = Palette.Bone;
            Color number = Palette.Bone;
            Color fill = Palette.Gold;
            Color icon = Color.white;

            switch (state)
            {
                case State.Active:
                    number = Palette.Gold;
                    break;
                case State.Cooldown:
                    label.a = DimAlpha;
                    number.a = TimeAlpha;
                    fill = Palette.Bone;
                    fill.a = DimAlpha;
                    icon.a = DimAlpha;
                    break;
                case State.Ready:
                    number = Palette.Gold;
                    break;
            }

            _header.SetLabelColor(label);
            _header.SetNumberColor(number);
            _bar.SetColor(fill, fill);
            if (_icon != null) _icon.color = icon;
        }

        private static string TimeText(float seconds)
        {
            return "<mspace=0.6em>" + StatusEffect.GetTimeString(seconds) + "</mspace>";
        }

        // The bound key for the power, resolved through vanilla's own $KEY_ token
        // so a gamepad gets its glyph instead of "F". Resolved once per state
        // entry rather than every frame; rebinding mid-session is rare enough.
        private string ReadyText()
        {
            if (_keyText == null)
            {
                string key = Localization.instance.Localize("$KEY_GP");
                bool usable = !string.IsNullOrEmpty(key) && !key.StartsWith("[");
                _keyText = usable ? key.ToUpperInvariant() : "";
                if (!_loggedKey)
                {
                    _loggedKey = true;
                    Plugin.Log.LogInfo("power: $KEY_GP resolved to '" + key + "'" + (usable ? "" : ", showing READY without a key"));
                }
            }

            return _keyText.Length > 0
                ? "<size=80%>" + _keyText + "</size>  READY"
                : "READY";
        }

        // Rebinding or switching to a pad changes the glyph; forget the cached
        // string when the state is re-entered so the next Ready re-resolves.
        internal void ForgetKey()
        {
            _keyText = null;
        }
    }
}
