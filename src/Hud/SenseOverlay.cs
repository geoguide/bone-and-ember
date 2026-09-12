using TMPro;
using UnityEngine;

namespace BoneAndEmber
{
    // docs/design/006-sense.md. Stand still and the ground reports what is near
    // you: a wave travels out from your feet and every dropped item and pickable
    // it passes takes a marker.
    //
    // Stillness is the entire input. There is no key, no mode and nothing to
    // dismiss, which is the point: stopping is already the moment you look
    // around, so the game may as well answer.
    //
    // The wave is timing, not a drawing. Slice 1 drew it as a ring on the ground
    // and it read as a stray white stroke across the screen: a constant-width
    // line projected at a grazing angle always will, and bumpy terrain only made
    // the kinks visible. The reveal order carries the same feeling on its own.
    //
    // Moving does not kill it immediately. The set you sensed freezes and rides
    // along with you, then fades once you have been moving for Sense.MoveGrace,
    // because snapping it off on the first step trains you to stop again just to
    // see it, which is a worse loop than letting it decay.
    internal class SenseOverlay : MonoBehaviour
    {
        internal static SenseOverlay Instance;

        private const int FontSearchFrames = 600;

        // How long the reveal wave takes to reach Sense.Range.
        private const float SweepMs = 400f;

        // The slow decay once you have been moving past the grace period. Long on
        // purpose: this is a thing dimming, not a thing being switched off.
        private const float ExpireMs = 1500f;

        // A windowed speed rather than a per-frame one, so a frame of physics
        // jitter can neither trip it nor hold it open.
        private const float SpeedWindow = 0.2f;
        private const float StillSpeed = 0.1f;          // m/s

        private const float EnemyRange = 15f;
        private const float DevLogInterval = 2f;

        private Hud _hud;
        private Transform _parent;
        private RectTransform _root;
        private RectTransform _canvasRect;
        private Camera _canvasCamera;

        private readonly SenseMarkers _markers = new SenseMarkers();

        private bool _built;
        private bool _gaveUp;
        private int _searchedFrames;
        private int _appliedFontVersion = -1;

        // Stillness
        private Vector3 _samplePos;
        private float _sampleTime = -1f;
        private float _speed;
        private float _stillTime;

        // State
        private bool _up;         // markers are on screen or on their way out
        private bool _frozen;     // moving: keep what we found, stop scanning
        private bool _expiring;   // moving long enough: slow fade to nothing
        private float _movingTime;
        private float _expireTime;
        private Tween _sweep;

        private float _nextDevLog;

        private void Awake()
        {
            Instance = this;
            _hud = GetComponent<Hud>();
            if (_hud == null)
            {
                Plugin.Log.LogError("SenseOverlay was added to something that is not the Hud, giving up.");
                enabled = false;
                return;
            }

            // hudroot, so vanilla's cutscene and Ctrl+F3 hiding covers us for free.
            // Map, inventory, menu and death are not covered by that, which is what
            // ShouldSuppress is for.
            _parent = _hud.m_rootObject != null ? _hud.m_rootObject.transform : _hud.transform;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void LateUpdate()
        {
            if (_hud == null || _gaveUp) return;

            if (!Plugin.SenseEnabled.Value)
            {
                if (_up) Extinguish("config off");
                if (_root != null && _root.gameObject.activeSelf) _root.gameObject.SetActive(false);
                return;
            }

            if (!_built && !Build()) return;

            Player player = Player.m_localPlayer;
            Camera cam = Utils.GetMainCamera();

            string reason;
            if (ShouldSuppress(player, out reason))
            {
                // A suppress condition gets no grace period. You are in a menu or
                // a fight, and a slow decay over either reads as a bug.
                if (_up) Extinguish(reason);
                if (_root.gameObject.activeSelf) _root.gameObject.SetActive(false);
                _stillTime = 0f;
                return;
            }

            if (cam == null) return;

            ApplyFontIfChanged();
            UpdateSpeed(player);

            float dt = Time.deltaTime;
            bool still = _speed < StillSpeed;

            if (!_up)
            {
                if (still) _stillTime += dt;
                else _stillTime = 0f;

                if (_stillTime * 1000f >= Plugin.SenseDelay.Value) Activate(player, cam);

                if (!_up)
                {
                    if (_root.gameObject.activeSelf) _root.gameObject.SetActive(false);
                    return;
                }
            }
            else if (still)
            {
                // Stopped again. Everything comes straight back to full and
                // scanning resumes, so stopping mid-decay is rewarded rather than
                // making you wait out the fade first.
                if (_expiring)
                {
                    _expiring = false;
                    Plugin.Log.LogInfo("sense: held (stopped again)");
                }
                _movingTime = 0f;
                _frozen = false;
            }
            else
            {
                // Moving. What you sensed freezes and rides along rather than
                // snapping off under you. Occlusion keeps running inside
                // SenseMarkers, so this never shows anything through a wall.
                _movingTime += dt;
                _frozen = true;

                if (!_expiring && _movingTime * 1000f >= Plugin.SenseMoveGrace.Value)
                {
                    _expiring = true;
                    _expireTime = 0f;
                    Plugin.Log.LogInfo("sense: expiring (moving " + _movingTime.ToString("0.0") + "s)");
                }
            }

            if (!_root.gameObject.activeSelf) _root.gameObject.SetActive(true);

            // With motion off there is no wave at all: the gate opens wide and
            // every marker snaps on together.
            float sweep = Plugin.MotionEnabled.Value ? _sweep.Update(dt) : float.MaxValue;

            _markers.Update(player, cam, sweep, _frozen, _expiring, ExpireMs, dt);

            if (_expiring)
            {
                _expireTime += dt;
                if (_expireTime * 1000f >= ExpireMs) Extinguish("moved");
            }

            DevLog(player);
        }

        private void Activate(Player player, Camera cam)
        {
            _up = true;
            _frozen = false;
            _expiring = false;
            _movingTime = 0f;
            _stillTime = 0f;
            _sweep.Start(0f, Plugin.SenseRange.Value, SweepMs);
            _markers.ForceRescan();

            // Scan once immediately so the count in the log is the real one rather
            // than zero on the frame sense came up.
            _markers.Update(player, cam, 0f, false, false, ExpireMs, 0f);
            Plugin.Log.LogInfo("sense: activated, " + _markers.LastScanSummary);
        }

        private void Extinguish(string reason)
        {
            if (!_up) return;

            _up = false;
            _frozen = false;
            _expiring = false;
            _movingTime = 0f;
            _stillTime = 0f;
            _markers.Clear();
            if (_root != null && _root.gameObject.activeSelf) _root.gameObject.SetActive(false);

            Plugin.Log.LogInfo("sense: deactivated (" + reason + ")");
        }

        private void UpdateSpeed(Player player)
        {
            Vector3 p = player.transform.position;
            if (_sampleTime < 0f)
            {
                _samplePos = p;
                _sampleTime = Time.time;
                return;
            }

            float elapsed = Time.time - _sampleTime;
            if (elapsed < SpeedWindow) return;

            Vector3 d = p - _samplePos;
            d.y = 0f;
            _speed = d.magnitude / elapsed;
            _samplePos = p;
            _sampleTime = Time.time;
        }

        private bool ShouldSuppress(Player player, out string reason)
        {
            reason = null;
            if (player == null) { reason = "no player"; return true; }
            if (player.IsDead()) { reason = "dead"; return true; }
            if (player.InCutscene()) { reason = "cutscene"; return true; }
            if (player.IsTeleporting()) { reason = "teleporting"; return true; }

            // The set vanilla itself uses in Player.TakeInput. That method is
            // protected so it cannot be called from here, only copied.
            if (Minimap.IsOpen() ||
                InventoryGui.IsVisible() ||
                Menu.IsVisible() ||
                StoreGui.IsVisible() ||
                Console.IsVisible() ||
                TextInput.IsVisible() ||
                (TextViewer.instance != null && TextViewer.instance.IsVisible()) ||
                (Chat.instance != null && Chat.instance.HasFocus()) ||
                Hud.IsPieceSelectionVisible() ||
                Hud.InRadial())
            {
                reason = "gui";
                return true;
            }

            if (player.IsRiding()) { reason = "riding"; return true; }
            if (player.GetControlledShip() != null || player.IsAttachedToShip()) { reason = "sailing"; return true; }
            if (player.IsSwimming()) { reason = "swimming"; return true; }

            bool weapon;
            int enemies;
            bool targeting;
            if (InCombat(player, out weapon, out enemies, out targeting)) { reason = "combat"; return true; }

            return false;
        }

        // Both halves have to be true: a weapon is out AND something within
        // EnemyRange is coming for you. Each half is reported separately in the
        // dev log because the second one is the fragile half.
        private static bool InCombat(Player player, out bool weapon, out int enemies, out bool targeting)
        {
            weapon = false;
            enemies = 0;
            targeting = false;

            // GetCurrentWeapon is no use here: it falls back to m_unarmedWeapon and
            // never returns null for a player, so it cannot tell drawn from empty
            // handed. RightItem and LeftItem go null when the hands are sheathed.
            ItemDrop.ItemData right = player.RightItem;
            ItemDrop.ItemData left = player.LeftItem;
            if (right != null && right.IsWeapon()) weapon = true;
            else if (left != null && left.IsWeapon()) weapon = true;

            Vector3 pos = player.transform.position;
            System.Collections.Generic.List<BaseAI> ais = BaseAI.BaseAIInstances;
            for (int i = 0; i < ais.Count; i++)
            {
                BaseAI ai = ais[i];
                if (ai == null || ai.m_character == null) continue;
                if (!BaseAI.IsEnemy(player, ai.m_character)) continue;
                if (Vector3.Distance(ai.transform.position, pos) > EnemyRange) continue;

                enemies++;

                // GetTargetCreature only answers on the client that owns this AI.
                // Where it does answer, trust it exactly. Where it does not, fall
                // back to the ZDO-backed flag, which is true on every client.
                Character target = ai.GetTargetCreature();
                if (target == player) targeting = true;
                else if (target == null && ai.HaveTarget() && ai.IsAlerted()) targeting = true;
            }

            return weapon && targeting;
        }

        private void DevLog(Player player)
        {
            if (!Plugin.DevCommands.Value || Time.time < _nextDevLog) return;
            _nextDevLog = Time.time + DevLogInterval;

            bool weapon;
            int enemies;
            bool targeting;
            bool combat = InCombat(player, out weapon, out enemies, out targeting);
            Plugin.Log.LogInfo("sense: scan " + _markers.LastScanSummary +
                               ", drawing " + _markers.Count +
                               "; combat check: weapon=" + weapon +
                               " enemies=" + enemies +
                               " targeting=" + targeting +
                               " -> " + combat);
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
                Plugin.Log.LogWarning("sense: no HUD parent, the overlay is off for this session.");
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
                Plugin.Log.LogWarning("sense: no font source after " + FontSearchFrames +
                                      " frames, the overlay is off for this session.");
                _gaveUp = true;
                return false;
            }

            GameObject go = new GameObject("BoneAndEmber_Sense", typeof(RectTransform), typeof(CanvasGroup));
            _root = (RectTransform)go.transform;
            _root.SetParent(_parent, false);
            // Stretched over the whole canvas: everything inside is positioned from
            // a projected screen point, so the root is just a coordinate space.
            _root.anchorMin = Vector2.zero;
            _root.anchorMax = Vector2.one;
            _root.offsetMin = Vector2.zero;
            _root.offsetMax = Vector2.zero;

            CanvasGroup group = go.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            // Resolved while the root is still active: GetComponentInParent skips
            // inactive objects, and this one spends most of its life switched off.
            Canvas canvas = _root.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                Canvas rootCanvas = canvas.rootCanvas;
                _canvasRect = rootCanvas.transform as RectTransform;
                // A Screen Space Overlay canvas wants a null camera for the
                // screen-to-local conversion; anything else wants its own.
                _canvasCamera = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                    ? null
                    : rootCanvas.worldCamera;
            }

            if (_canvasRect == null)
            {
                Plugin.Log.LogWarning("sense: no parent Canvas found, the overlay is off for this session.");
                _gaveUp = true;
                return false;
            }

            _markers.Build(_root, _canvasRect, _canvasCamera, fontSource);

            _root.gameObject.SetActive(false);
            _built = true;
            Plugin.Log.LogInfo("sense: overlay created under " + HudPath.Of(_parent) +
                               ", font from " + fontFrom +
                               ", canvas " + HudPath.Of(_canvasRect));
            return true;
        }

        // Screen pixels to a position inside the HUD canvas.
        internal static bool ToCanvas(RectTransform canvasRect, Camera canvasCamera, Vector3 screenPos, out Vector2 local)
        {
            local = Vector2.zero;
            if (canvasRect == null) return false;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, new Vector2(screenPos.x, screenPos.y), canvasCamera, out local);
        }
    }
}
