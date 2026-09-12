using System.Collections.Generic;
using BepInEx.Bootstrap;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace BoneAndEmber
{
    // Slices 2 to 6 of docs/design/001-always-on-hud.md: our own health bar
    // (with food segments, a low-health glow and vignette), stamina bar and
    // status chips, with the vanilla (and Minimal UI) versions faded out behind
    // them.
    //
    // This is a MonoBehaviour added to the vanilla Hud object in HudAwakePatch, so
    // it lives and dies with the game session and gets a per-frame LateUpdate for
    // free. Unity calls Awake the moment AddComponent runs.
    internal class AlwaysOnHud : MonoBehaviour
    {
        private const string MinimalUiGuid = "Azumatt.MinimalUI";

        // How long the stamina bar takes to fade in/out once it decides to change
        // state, so it's a fade, not a snap.
        private const float StaminaFadeDuration = 0.3f;

        // Low health state (slice 4). A small gap between enter and exit fractions
        // (hysteresis) so a health total hovering right at 30% - regen ticking up
        // against a slow bleed, say - doesn't flicker the critical state on and off
        // every frame.
        private const float CriticalEnterFraction = 0.30f;
        private const float CriticalExitFraction = 0.33f;
        private const float GlowFadeSpeed = 3f;      // alpha units/sec
        private const float VignetteFadeSpeed = 1.5f; // alpha units/sec
        private const float PulseSpeed = 1.5f;        // radians/sec, ~4.2s period

        // Minimal UI builds its clones in a Hud.Awake postfix marked HarmonyPriority(0),
        // which runs after ours, so they do not exist on our first frame. Look for a
        // while, then stop looking.
        private const int MuiSearchFrames = 600;

        internal static AlwaysOnHud Instance;

        private Hud _hud;
        private readonly ClonedBar _health = new ClonedBar();
        private readonly ClonedBar _stamina = new ClonedBar();
        private readonly FoodSegments _food = new FoodSegments();
        private readonly StatusChips _chips = new StatusChips();
        private readonly LoudMoments _loud = new LoudMoments();
        private readonly BarHeader _healthHeader = new BarHeader();
        private readonly BarHeader _staminaHeader = new BarHeader();
        private readonly PowerLine _power = new PowerLine();
        private int _appliedFontVersion = -1;

        // Font fallback for pieces that can't reach MessageHud (loud moments).
        internal TMP_Text HealthTextForFont => _health.Text;

        private CanvasGroup _staminaFadeGroup;
        private float _staminaHideTimer;
        private float _staminaFadeAlpha = 1f;

        private Image _healthGlow;
        private Image _vignette;
        private bool _critical;

        // We hide other people's bars by putting a CanvasGroup on them and setting
        // alpha to 0, never by touching activeSelf. Alpha on a parent hides the whole
        // subtree whatever its children think, and it is a flag neither vanilla nor
        // Minimal UI reads or writes, so two mods can disagree without a fight.
        private readonly List<CanvasGroup> _vanillaHealthGroups = new List<CanvasGroup>();
        private readonly List<CanvasGroup> _vanillaStaminaGroups = new List<CanvasGroup>();
        private readonly List<CanvasGroup> _vanillaFoodGroups = new List<CanvasGroup>();
        private readonly List<CanvasGroup> _vanillaStatusGroups = new List<CanvasGroup>();
        private readonly List<CanvasGroup> _vanillaPowerGroups = new List<CanvasGroup>();
        private bool _vanillaHealthResolved;
        private bool _vanillaStaminaResolved;
        private bool _vanillaFoodResolved;
        private bool _vanillaStatusResolved;
        private bool _vanillaPowerResolved;

        private readonly List<CanvasGroup> _muiHealthGroups = new List<CanvasGroup>();
        private readonly List<CanvasGroup> _muiStaminaGroups = new List<CanvasGroup>();
        private readonly List<CanvasGroup> _muiFoodGroups = new List<CanvasGroup>();
        private bool _muiResolved;
        private bool _muiPresent;
        private int _muiSearchedFrames;

        private bool _loggedFirstValue;
        private float _nextValueLog;

        // Cached values from the last time ApplyTransform actually wrote anything,
        // so a config value that hasn't changed doesn't cost a RectTransform write
        // every frame, and so nothing but a real config change ever moves the bars.
        private float _appliedScale = -1f;
        private Vector2 _appliedPosition = new Vector2(float.NaN, float.NaN);
        private float _appliedWidth = -1f;
        private float _appliedHealthHeight = -1f;
        private float _appliedStaminaHeight = -1f;
        private float _appliedEndAngle = float.NaN;
        private float _appliedHealthHeaderH = -1f;
        private float _appliedStaminaHeaderH = -1f;
        private float _appliedGapChips = -1f;
        private float _appliedGapHeader = -1f;
        private float _appliedGapStamina = -1f;
        private bool _appliedStaminaOn;
        private float _appliedPowerHeight = -1f;
        private float _appliedPowerHeaderH = -1f;
        private float _appliedGapPower = -1f;
        private bool _appliedPowerOn;

        private void Awake()
        {
            Instance = this;
            _hud = GetComponent<Hud>();
            if (_hud == null)
            {
                Plugin.Log.LogError("AlwaysOnHud was added to something that is not the Hud, giving up.");
                enabled = false;
                return;
            }

            _muiPresent = Chainloader.PluginInfos.ContainsKey(MinimalUiGuid);
            Plugin.Log.LogInfo(_muiPresent
                ? "Minimal UI detected, its health, stamina and food bars will be hidden too."
                : "Minimal UI not installed, hiding vanilla bars only.");

            BuildHealthBar();
            BuildStaminaBar();
            BuildPowerLine();
            BuildFoodSegments();
            BuildStatusChips();
            BuildVignette();

            // Parented to hudroot, which vanilla moves off screen whenever the HUD
            // is hidden, so the slab can never show on the main menu.
            _loud.Init(_hud.m_rootObject != null ? _hud.m_rootObject.transform : _hud.transform);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void BuildHealthBar()
        {
            RectTransform source = _hud.m_healthBarRoot;
            if (source == null)
            {
                Plugin.Log.LogError("Hud.m_healthBarRoot is null, cannot build the health bar.");
                return;
            }

            Transform parent = _hud.m_rootObject != null ? _hud.m_rootObject.transform : _hud.transform;
            _health.Build(source, parent, "health", "BoneAndEmber_HealthBar", includeText: true);
            _health.SetColor(Palette.EmberHealth, Palette.Bone);
            _health.ApplySize(Plugin.BarWidth.Value, Plugin.BarHeight.Value, Plugin.BarEndAngle.Value);

            // The cloned label is only a font source now; the header draws the numbers.
            if (_health.Text != null) _health.Text.gameObject.SetActive(false);
            _healthHeader.Build(_health.Root, _health.Text, "Health", "BoneAndEmber_HealthHeader");

            // Damage trail (slice 4): fast snaps to the real value immediately, slow
            // holds the lost chunk for ~0.45s before draining. Set explicitly rather
            // than trust whatever the source prefab had serialized, the same
            // "stop inheriting" call made for geometry in slice 2.
            if (_health.Fast != null)
            {
                _health.Fast.m_smoothDrain = false;
                _health.Fast.m_smoothFill = false;
            }

            if (_health.Slow != null)
            {
                _health.Slow.m_smoothDrain = true;
                _health.Slow.m_smoothFill = false;
                _health.Slow.m_changeDelay = 0.45f;
                _health.Slow.m_smoothSpeed = 3f;
            }

            _healthGlow = CreateGlow(_health.Root);
        }

        // Clones the same m_healthBarRoot prefab as health: it's the only plain
        // horizontal-bar shape vanilla ships, and Minimal UI's own stamina clone
        // reuses it the same way (confirmed in its decompile).
        private void BuildStaminaBar()
        {
            RectTransform source = _hud.m_healthBarRoot;
            if (source == null) return; // already logged by BuildHealthBar

            Transform parent = _hud.m_rootObject != null ? _hud.m_rootObject.transform : _hud.transform;
            _stamina.Build(source, parent, "stamina", "BoneAndEmber_StaminaBar", includeText: true);
            _stamina.SetColor(Palette.Gold, Palette.Gold);
            _stamina.ApplySize(Plugin.BarWidth.Value, Plugin.StaminaHeight.Value, Plugin.BarEndAngle.Value);

            // Numbers live in the header above the bar (slice 8), not to its right.
            // The header is a child of the stamina root, so it fades with the bar.
            if (_stamina.Text != null) _stamina.Text.gameObject.SetActive(false);
            _staminaHeader.Build(_stamina.Root, _stamina.Text, "Stamina", "BoneAndEmber_StaminaHeader");

            // Cloned from the health prefab, so without this it would inherit
            // health's own drain-delay settings, a "damage trail" doesn't make
            // sense on a resource bar that fades in and out on its own. Snap.
            if (_stamina.Fast != null) _stamina.Fast.m_smoothDrain = false;
            if (_stamina.Slow != null) _stamina.Slow.m_smoothDrain = false;

            _staminaFadeGroup = _stamina.Root.gameObject.AddComponent<CanvasGroup>();
        }

        // Fourth line of the cluster (005). Same prefab as the others; the
        // health bar's cloned label is the font source, like the headers.
        private void BuildPowerLine()
        {
            RectTransform source = _hud.m_healthBarRoot;
            if (source == null) return;

            Transform parent = _hud.m_rootObject != null ? _hud.m_rootObject.transform : _hud.transform;
            _power.Build(source, parent, _health.Text);
            _power.ApplySize(Plugin.BarWidth.Value, Plugin.PowerBarHeight.Value, Plugin.BarEndAngle.Value);
            _power.SetVisible(false);
        }

        // Clones vanilla's own damage-flash screen (Hud.m_damageScreen) as a
        // sibling, rather than building a vignette shape from scratch: it's
        // already a full-screen, edge-focused overlay with whatever sprite Iron
        // Gate authored for it, and cloning it as a sibling means it inherits the
        // same main-menu/loading/dead visibility gating vanilla's own flash has,
        // with no extra guard code on our part.
        private void BuildVignette()
        {
            if (_hud.m_damageScreen == null)
            {
                Plugin.Log.LogWarning("Hud.m_damageScreen is null, no vignette source to clone; the low health vignette is off for this session.");
                return;
            }

            GameObject go = Instantiate(_hud.m_damageScreen.gameObject, _hud.m_damageScreen.transform.parent);
            go.name = "BoneAndEmber_LowHealthVignette";
            go.SetActive(true);

            _vignette = go.GetComponent<Image>();
            Color c = Palette.EmberCritical;
            c.a = 0f;
            _vignette.color = c;

            Plugin.Log.LogInfo("Low health vignette cloned from " + HudPath.Of(_hud.m_damageScreen.transform) + ".");
        }

        private void BuildFoodSegments()
        {
            if (_health.Root == null) return;
            _food.Build(_health.Root, _health.Clip, _health.Text);
        }

        private void BuildStatusChips()
        {
            if (_health.Root == null) return;
            _chips.Build(_health.Root, _health.Text);
        }

        // Called from HealthBarPatch, the postfix on Hud.UpdateHealth, so we get the
        // same numbers at the same moment vanilla does.
        internal void SetHealth(Player player)
        {
            if (_health.Root == null) return;

            float max = player.GetMaxHealth();
            float health = player.GetHealth();

            _health.SetValue(health, max);
            UpdateCriticalState(max > 0f ? health / max : 1f);

            _healthHeader.SetNumbersVisible(Plugin.ShowNumbers.Value);
            _healthHeader.SetValues(Mathf.CeilToInt(health), Mathf.CeilToInt(max));

            LogValues(health, max);
        }

        // Hysteresis band (CriticalEnterFraction/CriticalExitFraction), not a
        // single 30% cutoff: recolors the fill only on an actual state change, so
        // health sitting right at the edge doesn't flip the color every frame.
        private void UpdateCriticalState(float fraction)
        {
            bool wasCritical = _critical;
            if (_critical)
            {
                if (fraction > CriticalExitFraction) _critical = false;
            }
            else if (fraction < CriticalEnterFraction)
            {
                _critical = true;
            }

            if (_critical == wasCritical) return;

            _health.SetColor(_critical ? Palette.EmberCritical : Palette.EmberHealth, Palette.Bone);
            _healthHeader.SetNumberColor(_critical ? Palette.EmberCritical : Palette.Bone);
            Plugin.Log.LogInfo(_critical ? "Health critical (below 30%)." : "Health no longer critical.");
        }

        // Called from StaminaBarPatch, the postfix on Hud.UpdateStamina.
        internal void SetStamina(Player player, float dt)
        {
            if (_stamina.Root == null) return;

            float stamina = player.GetStamina();
            float max = player.GetMaxStamina();
            _stamina.SetValue(stamina, max);

            _staminaHeader.SetNumbersVisible(Plugin.ShowNumbers.Value);
            _staminaHeader.SetValues(Mathf.CeilToInt(stamina), Mathf.CeilToInt(max));

            // Same 1-second threshold vanilla's own m_staminaHideTimer uses
            // (Hud.UpdateStamina), just applied to our own bar.
            if (stamina < max) _staminaHideTimer = 0f;
            else _staminaHideTimer += dt;

            float target = _staminaHideTimer < 1f ? 1f : 0f;
            _staminaFadeAlpha = Mathf.MoveTowards(_staminaFadeAlpha, target, dt / StaminaFadeDuration);
            if (_staminaFadeGroup != null) _staminaFadeGroup.alpha = _staminaFadeAlpha;
        }

        // Called from PowerLinePatch, the postfix on Hud.UpdateGuardianPower.
        internal void SetGuardianPower(Player player)
        {
            _power.Update(player, Time.deltaTime);
        }

        // Called from StatusChipsPatch, the postfix on Hud.UpdateStatusEffects.
        internal void SetStatusEffects(List<StatusEffect> effects)
        {
            bool on = Plugin.StatusChipsEnabled.Value;
            _chips.SetVisible(on);

            // The line draws your own power while it runs; don't chip it twice.
            bool powerOn = Plugin.PowerEnabled.Value && _power.Current != PowerLine.State.None;
            _chips.ExcludeHash = powerOn ? _power.PowerHash : 0;
            if (on) _chips.Update(effects, Time.deltaTime);

            _loud.Observe(effects);
        }

        // Called from FoodSegmentsPatch, the postfix on Hud.UpdateFood.
        internal void SetFood(Player player)
        {
            if (!Plugin.ShowFoodSegments.Value)
            {
                _food.SetVisible(false);
                return;
            }

            _food.Update(player, _critical);
        }

        private void LogValues(float health, float max)
        {
            if (!_loggedFirstValue)
            {
                _loggedFirstValue = true;
                Plugin.Log.LogInfo("Health bar is live: " + Mathf.CeilToInt(health) + " / " + Mathf.CeilToInt(max));
                return;
            }

            if (Time.unscaledTime < _nextValueLog) return;
            _nextValueLog = Time.unscaledTime + 5f;
            Plugin.Log.LogDebug("Health " + Mathf.CeilToInt(health) + " / " + Mathf.CeilToInt(max));
        }

        // LateUpdate rather than Awake, because Minimal UI's clones do not exist yet
        // when our Awake runs, and because reapplying every frame survives a config
        // toggle or another mod changing its mind.
        private void LateUpdate()
        {
            if (_hud == null) return;

            bool hudOn = Plugin.HudEnabled.Value;
            bool staminaOn = hudOn && Plugin.StaminaBarEnabled.Value;
            float dt = Time.deltaTime;

            if (_health.Root != null)
            {
                if (_health.Root.gameObject.activeSelf != hudOn) _health.Root.gameObject.SetActive(hudOn);
                if (hudOn)
                {
                    ApplyFontIfChanged();
                    ApplyTransform();
                    UpdateLowHealthEffects(dt);
                    UpdateEncumbered();
                    _loud.Update(dt);
                }
                else
                {
                    ForceLowHealthEffectsOff();
                    _loud.Hide();
                }
            }

            if (_stamina.Root != null && _stamina.Root.gameObject.activeSelf != staminaOn)
            {
                _stamina.Root.gameObject.SetActive(staminaOn);
            }

            // The line exists only once a power is chosen (005 state "None" is
            // an absent line, not an empty one).
            bool powerOn = hudOn && Plugin.PowerEnabled.Value && _power.Current != PowerLine.State.None;
            _power.SetVisible(powerOn);

            ApplyVisibility(hudOn, staminaOn);
        }

        // Only ever writes anything when a relevant config value actually changed
        // (or on the first call, via the sentinel "applied" defaults). Reported in
        // game: with an earlier version of this method writing an absolute value
        // every single frame unconditionally, the stamina bar still drifted toward
        // the bottom of the screen while draining and stuck there, which means
        // something else was fighting our per-frame write and winning on a later
        // pass the same frame - almost certainly the inherited Animator ClonedBar
        // now destroys outright rather than merely disabling. Writing only on an
        // actual change is a second, independent line of defense: if anything ever
        // does contest this property again, it will win for at most one frame
        // instead of every frame, and the log's "HUD transform applied" line will
        // only fire when we intended a change, making a rogue writer obvious.
        private void ApplyTransform()
        {
            float scale = Plugin.HudScale.Value;
            Vector2 position = Plugin.HudPosition.Value;
            float width = Plugin.BarWidth.Value;
            float healthHeight = Plugin.BarHeight.Value;
            float staminaHeight = Plugin.StaminaHeight.Value;
            float endAngle = Plugin.BarEndAngle.Value;
            float gapChips = Plugin.GapChipsToHeader.Value;
            float gapHeader = Plugin.GapHeaderToBar.Value;
            float gapStamina = Plugin.GapHealthToStamina.Value;

            float labelSize = Plugin.LabelSize.Value;
            float spacing = Plugin.LetterSpacing.Value;
            bool staminaOn = _stamina.Root != null && Plugin.StaminaBarEnabled.Value;
            float powerHeight = Plugin.PowerBarHeight.Value;
            float gapPower = Plugin.GapStaminaToPower.Value;
            bool powerOn = _power.Root != null && Plugin.PowerEnabled.Value && _power.Current != PowerLine.State.None;
            bool headersChanged =
                _healthHeader.ApplyStyle(labelSize, Plugin.HealthNumberScale.Value, spacing) |
                _staminaHeader.ApplyStyle(labelSize, Plugin.StaminaNumberScale.Value, spacing) |
                _power.ApplyStyle(labelSize, Plugin.StaminaNumberScale.Value, spacing);

            bool changed = headersChanged ||
                !Mathf.Approximately(scale, _appliedScale) ||
                position != _appliedPosition ||
                !Mathf.Approximately(width, _appliedWidth) ||
                !Mathf.Approximately(healthHeight, _appliedHealthHeight) ||
                !Mathf.Approximately(staminaHeight, _appliedStaminaHeight) ||
                !Mathf.Approximately(endAngle, _appliedEndAngle) ||
                !Mathf.Approximately(_healthHeader.Height, _appliedHealthHeaderH) ||
                !Mathf.Approximately(_staminaHeader.Height, _appliedStaminaHeaderH) ||
                !Mathf.Approximately(gapChips, _appliedGapChips) ||
                !Mathf.Approximately(gapHeader, _appliedGapHeader) ||
                !Mathf.Approximately(gapStamina, _appliedGapStamina) ||
                staminaOn != _appliedStaminaOn ||
                !Mathf.Approximately(powerHeight, _appliedPowerHeight) ||
                !Mathf.Approximately(_power.Header.Height, _appliedPowerHeaderH) ||
                !Mathf.Approximately(gapPower, _appliedGapPower) ||
                powerOn != _appliedPowerOn;
            if (!changed) return;

            _appliedScale = scale;
            _appliedPosition = position;
            _appliedWidth = width;
            _appliedHealthHeight = healthHeight;
            _appliedStaminaHeight = staminaHeight;
            _appliedEndAngle = endAngle;
            _appliedHealthHeaderH = _healthHeader.Height;
            _appliedStaminaHeaderH = _staminaHeader.Height;
            _appliedGapChips = gapChips;
            _appliedGapHeader = gapHeader;
            _appliedGapStamina = gapStamina;
            _appliedStaminaOn = staminaOn;
            _appliedPowerHeight = powerHeight;
            _appliedPowerHeaderH = _power.Header.Height;
            _appliedGapPower = gapPower;
            _appliedPowerOn = powerOn;

            // One left-aligned column, top to bottom: chips, health header, health
            // bar, stamina header, stamina bar. HudPosition is the health bar's
            // bottom-left; everything else is placed from it. Headers and the chip
            // row are children of their bar's root, so only the two roots need
            // absolute positions.
            Vector3 scaleVec = new Vector3(scale, scale, 1f);
            _health.Root.localScale = scaleVec;
            _health.Root.anchoredPosition = position;
            _health.ApplySize(width, healthHeight, endAngle);
            if (_healthHeader.Root != null) _healthHeader.Root.anchoredPosition = new Vector2(0f, gapHeader);

            float chipsOffset = gapHeader + _healthHeader.Height + gapChips;
            _chips.SetLayout(chipsOffset, width, _healthHeader.Height, labelSize);

            if (_stamina.Root != null)
            {
                float staminaY = position.y - gapStamina - _staminaHeader.Height - gapHeader - staminaHeight;
                _stamina.Root.localScale = scaleVec;
                _stamina.Root.anchoredPosition = new Vector2(position.x, staminaY);
                _stamina.ApplySize(width, staminaHeight, endAngle);
                if (_staminaHeader.Root != null) _staminaHeader.Root.anchoredPosition = new Vector2(0f, gapHeader);
            }

            // Column ends at the stamina bar's bottom edge when it's on, else at the
            // health bar's. The power line (005) hangs under whichever that is,
            // and the caption one header gap below the column's end.
            float columnBottom = staminaOn
                ? gapStamina + _staminaHeader.Height + gapHeader + staminaHeight
                : 0f;

            if (_power.Root != null)
            {
                float powerBlock = gapPower + _power.Header.Height + gapHeader + powerHeight;
                float powerY = position.y - columnBottom - powerBlock;
                _power.Root.localScale = scaleVec;
                _power.Root.anchoredPosition = new Vector2(position.x, powerY);
                _power.ApplySize(width, powerHeight, endAngle);
                if (_power.Header.Root != null) _power.Header.Root.anchoredPosition = new Vector2(0f, gapHeader);
                if (powerOn) columnBottom += powerBlock;
            }

            _food.SetCaptionOffset(columnBottom + gapHeader + 2f);

            if (_healthGlow != null)
            {
                const float bleed = 4f;
                _healthGlow.sprite = EndCapSprite.Get(Mathf.CeilToInt(width + bleed * 2f), Mathf.CeilToInt(healthHeight + bleed * 2f), endAngle);
                _healthGlow.type = Image.Type.Simple;
            }

            Plugin.Log.LogInfo(
                "HUD transform applied: scale=" + scale + " position=" + position +
                " barSize=" + width + "x" + healthHeight + " staminaHeight=" + staminaHeight +
                " endAngle=" + endAngle + " headerH=" + _healthHeader.Height + "/" + _staminaHeader.Height +
                " powerHeight=" + powerHeight + " powerOn=" + powerOn);
        }

        // FontName is live: when it changes, HudFont re-resolves and bumps its
        // version, and every text we own gets the new asset (or vanilla's back).
        // Fires once on the crossing, not every frame you stay heavy. Driven
        // from here rather than from the weight bar because you usually cross
        // the limit while looting with the inventory shut, and the bar only
        // updates while it's open. IsEncumbered is vanilla's own answer, so
        // this can't drift from the stamina drain it warns about.
        private bool _wasEncumbered;

        private void UpdateEncumbered()
        {
            Player player = Player.m_localPlayer;
            if (player == null) return;

            bool encumbered = player.IsEncumbered();
            if (encumbered && !_wasEncumbered) _loud.Trigger(LoudMoments.Encumbered);
            _wasEncumbered = encumbered;
        }

        private void ApplyFontIfChanged()
        {
            HudFont.Refresh();
            if (HudFont.Version == _appliedFontVersion) return;
            _appliedFontVersion = HudFont.Version;

            HudFont.ApplyAll(_health.Root);
            HudFont.ApplyAll(_stamina.Root);
            HudFont.ApplyAll(_power.Root);
            _loud.ReapplyFont();
            Plugin.Log.LogInfo("font: applied to all HUD text (version " + HudFont.Version + ").");
        }

        // Slow, continuous pulse while critical; fades to fully off otherwise. One
        // MoveTowards target handles both the fade-in/out at the 30% threshold and
        // the ongoing pulse, so there's no separate "is transitioning" state to
        // track.
        private void UpdateLowHealthEffects(float dt)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * PulseSpeed);

            if (_healthGlow != null)
            {
                float target = _critical ? Mathf.Lerp(0.10f, 0.30f, pulse) : 0f;
                Color c = _healthGlow.color;
                c.a = Mathf.MoveTowards(c.a, target, dt * GlowFadeSpeed);
                _healthGlow.color = c;
            }

            if (_vignette != null)
            {
                bool wanted = _critical && Plugin.LowHealthVignette.Value;
                float target = wanted ? Mathf.Lerp(0.10f, 0.28f, pulse) : 0f;
                Color c = _vignette.color;
                c.a = Mathf.MoveTowards(c.a, target, dt * VignetteFadeSpeed);
                _vignette.color = c;
            }
        }

        private void ForceLowHealthEffectsOff()
        {
            if (_healthGlow != null)
            {
                Color c = _healthGlow.color;
                c.a = 0f;
                _healthGlow.color = c;
            }

            if (_vignette != null)
            {
                Color c = _vignette.color;
                c.a = 0f;
                _vignette.color = c;
            }
        }

        // A faint halo bleeding a few pixels past the bar's own frame: sibling
        // index 0 puts it behind bkg/border/fill, so only the outer bleed reads,
        // giving a soft-edged glow rather than a hard-edged outline. There's no
        // gradient sprite or shader in this pipeline to soften it further; if it
        // reads too hard-edged in game, that needs a real glow sprite, not more
        // code.
        private static Image CreateGlow(RectTransform parent)
        {
            GameObject go = new GameObject("BoneAndEmber_HealthGlow", typeof(RectTransform), typeof(Image));
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.SetSiblingIndex(0);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            const float bleed = 4f;
            rt.offsetMin = new Vector2(-bleed, -bleed);
            rt.offsetMax = new Vector2(bleed, bleed);

            Image img = go.GetComponent<Image>();
            img.raycastTarget = false;
            Color c = Palette.EmberCritical;
            c.a = 0f;
            img.color = c;

            return img;
        }

        private void ApplyVisibility(bool hudOn, bool staminaOn)
        {
            bool foodOn = hudOn && Plugin.ShowFoodSegments.Value;

            ResolveVanillaHealthTargets();
            SetAlpha(_vanillaHealthGroups, hudOn ? 0f : 1f);

            ResolveVanillaStaminaTargets();
            SetAlpha(_vanillaStaminaGroups, staminaOn ? 0f : 1f);

            ResolveVanillaFoodTargets();
            SetAlpha(_vanillaFoodGroups, foodOn ? 0f : 1f);

            bool chipsOn = hudOn && Plugin.StatusChipsEnabled.Value;
            ResolveVanillaStatusTargets();
            SetAlpha(_vanillaStatusGroups, chipsOn ? 0f : 1f);

            // Hidden whenever the line is on, not only while a power is chosen:
            // vanilla's widget is inactive with no power anyway, and gating on
            // the toggle alone means the A/B is one config flip.
            bool powerOn = hudOn && Plugin.PowerEnabled.Value;
            ResolveVanillaPowerTargets();
            SetAlpha(_vanillaPowerGroups, powerOn ? 0f : 1f);

            if (!_muiPresent) return;

            ResolveMuiTargets();
            bool hideMui = Plugin.HideOtherBars.Value;
            SetAlpha(_muiHealthGroups, hudOn && hideMui ? 0f : 1f);
            SetAlpha(_muiStaminaGroups, staminaOn && hideMui ? 0f : 1f);
            SetAlpha(_muiFoodGroups, foodOn && hideMui ? 0f : 1f);
        }

        // Was two Find("Health")/Find("healthicon") calls by guessed name, the same
        // shallow-lookup mistake that missed Minimal UI's bars in slice 2. It also
        // missed two background plates ("darken", "bkg" or similar) that aren't
        // named "Health" or "healthicon" at all, which is why they kept showing
        // through behind our bar. Hide every direct child of healthpanel instead,
        // known or not, except the ones food owns (those get their own toggle in
        // ResolveVanillaFoodTargets, checked by reference against Hud's own
        // fields, not by guessing more names). Never touch healthpanel itself:
        // Minimal UI re-enables it whenever it finds it off, so that flag is
        // contested ground.
        private void ResolveVanillaHealthTargets()
        {
            if (_vanillaHealthResolved) return;
            _vanillaHealthResolved = true;

            if (_hud.m_healthPanel == null)
            {
                Plugin.Log.LogWarning("Hud.m_healthPanel is null, cannot hide vanilla's health panel.");
                return;
            }

            int childCount = _hud.m_healthPanel.childCount;
            for (int i = 0; i < childCount; i++)
            {
                Transform child = _hud.m_healthPanel.GetChild(i);
                if (IsFoodChild(child)) continue;

                AddGroup(_vanillaHealthGroups, child);
                Plugin.Log.LogInfo("Hiding vanilla healthpanel child: " + HudPath.Of(child));
            }
        }

        private bool IsFoodChild(Transform child)
        {
            if (_hud.m_foodBarRoot != null && child == (Transform)_hud.m_foodBarRoot) return true;
            if (_hud.m_foodIcon != null && child == _hud.m_foodIcon.transform) return true;
            if (ArrayContainsTransform(_hud.m_foodBars, child)) return true;
            if (ArrayContainsTransform(_hud.m_foodIcons, child)) return true;
            if (ArrayContainsTransform(_hud.m_foodTime, child)) return true;
            return false;
        }

        private static bool ArrayContainsTransform<T>(T[] components, Transform t) where T : Component
        {
            if (components == null) return false;
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] != null && components[i].transform == t) return true;
            }
            return false;
        }

        private void ResolveVanillaStaminaTargets()
        {
            if (_vanillaStaminaResolved) return;
            _vanillaStaminaResolved = true;

            // staminapanel is a Find on a direct child by name, same shallow lookup
            // that missed Minimal UI's bars in slice 2. If it also misses here, the
            // fallback to m_staminaBar2Root only hides the bar's fill, not any
            // background plate around it.
            Transform staminaPanel = _hud.m_rootObject != null
                ? _hud.m_rootObject.transform.Find("staminapanel")
                : null;
            Transform stamina = staminaPanel != null ? staminaPanel : _hud.m_staminaBar2Root;
            AddGroup(_vanillaStaminaGroups, stamina);

            Plugin.Log.LogInfo(
                "Hiding vanilla stamina: " + HudPath.Of(stamina) +
                (staminaPanel == null ? " (staminapanel not found by name, fell back to m_staminaBar2Root)" : ""));
        }

        private void ResolveVanillaFoodTargets()
        {
            if (_vanillaFoodResolved) return;
            _vanillaFoodResolved = true;

            // Prefer Hud's own serialized array fields over guessed child names
            // (food0/food1/food2): a Find("name") lookup already missed once in
            // slice 2 (Minimal UI's bars), and these arrays are exactly the
            // objects Hud.UpdateFood itself writes to, so they can't be stale.
            AddGroup(_vanillaFoodGroups, _hud.m_foodBarRoot);
            if (_hud.m_foodIcon != null) AddGroup(_vanillaFoodGroups, _hud.m_foodIcon.transform);
            AddGroupsFromArray(_vanillaFoodGroups, _hud.m_foodBars);
            AddGroupsFromArray(_vanillaFoodGroups, _hud.m_foodIcons);
            AddGroupsFromArray(_vanillaFoodGroups, _hud.m_foodTime);

            Plugin.Log.LogInfo("Hiding " + _vanillaFoodGroups.Count + " vanilla food object(s).");
        }

        // The whole vanilla status effect list, one CanvasGroup on its root.
        // GuardianPower lives in its own m_gpRoot, handled below.
        private void ResolveVanillaStatusTargets()
        {
            if (_vanillaStatusResolved) return;
            _vanillaStatusResolved = true;

            AddGroup(_vanillaStatusGroups, _hud.m_statusEffectListRoot);
            Plugin.Log.LogInfo("Hiding vanilla status effect list: " + HudPath.Of(_hud.m_statusEffectListRoot));
        }

        // Vanilla's guardian power corner widget (005). Alpha only: Hud.Awake
        // deactivates it and UpdateGuardianPower reactivates it once a power is
        // chosen, so activeSelf is vanilla's, and Minimal UI's clone (when it is
        // ever re-enabled) is its own object and untouched here.
        private void ResolveVanillaPowerTargets()
        {
            if (_vanillaPowerResolved) return;
            _vanillaPowerResolved = true;

            if (_hud.m_gpRoot == null)
            {
                Plugin.Log.LogWarning("Hud.m_gpRoot is null, cannot hide vanilla's guardian power widget.");
                return;
            }

            AddGroup(_vanillaPowerGroups, _hud.m_gpRoot);
            Plugin.Log.LogInfo("Hiding vanilla guardian power widget: " + HudPath.Of(_hud.m_gpRoot));
        }

        private static void AddGroupsFromArray<T>(List<CanvasGroup> into, T[] components) where T : Component
        {
            if (components == null) return;
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] != null) AddGroup(into, components[i].transform);
            }
        }

        // Transform.Find("MUI_HPBar") only checks direct children (or a literal "/"
        // path), so if Minimal UI parents its clones anywhere but directly under
        // m_rootObject.transform, a shallow lookup misses them silently. Search the
        // whole Hud hierarchy instead, including inactive objects, and log whatever
        // we actually find so a wrong guess about the hierarchy shows up in the log
        // instead of as a bar we failed to hide.
        private void ResolveMuiTargets()
        {
            if (_muiResolved) return;

            Transform health = FindDescendant(_hud.transform, "MUI_HPBar");
            Transform stamina = FindDescendant(_hud.transform, "MUI_StaminaBar");
            Transform food = FindDescendant(_hud.transform, "MUI_FoodBar");

            if (health != null || stamina != null || food != null)
            {
                _muiResolved = true;
                AddGroup(_muiHealthGroups, health);
                AddGroup(_muiStaminaGroups, stamina);
                AddGroup(_muiFoodGroups, food);
                if (health != null) Plugin.Log.LogInfo("Found Minimal UI health bar at " + HudPath.Of(health));
                if (stamina != null) Plugin.Log.LogInfo("Found Minimal UI stamina bar at " + HudPath.Of(stamina));
                if (food != null) Plugin.Log.LogInfo("Found Minimal UI food bar at " + HudPath.Of(food));
                return;
            }

            if (++_muiSearchedFrames < MuiSearchFrames) return;

            _muiResolved = true;
            Plugin.Log.LogWarning(
                "Minimal UI is loaded but MUI_HPBar/MUI_StaminaBar/MUI_FoodBar were never found anywhere under Hud, " +
                "after searching the full hierarchy for " + MuiSearchFrames + " frames. " +
                "Its custom bars may be switched off in its own config, or it renamed them in an update.");
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root == null) return null;

            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == name) return all[i];
            }
            return null;
        }

        private static void AddGroup(List<CanvasGroup> into, Transform target)
        {
            if (target == null) return;

            CanvasGroup group = target.GetComponent<CanvasGroup>();
            if (group == null) group = target.gameObject.AddComponent<CanvasGroup>();
            into.Add(group);
        }

        private static void SetAlpha(List<CanvasGroup> groups, float alpha)
        {
            for (int i = 0; i < groups.Count; i++)
            {
                CanvasGroup group = groups[i];
                if (group == null) continue;
                if (Mathf.Approximately(group.alpha, alpha)) continue;

                group.alpha = alpha;
                group.blocksRaycasts = alpha > 0f;
                group.interactable = alpha > 0f;
            }
        }
    }
}
