using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoneAndEmber
{
    // Slice 3 of docs/design/002-waypoint-strip.md: your deaths on the compass
    // strip. Skull in ember with the distance under it, nearest one brightest,
    // clamped to a chevron at the ends when it's behind you.
    //
    // Death pins are read straight out of Minimap.m_pins, so every skull already
    // on your map counts. No new deaths needed and nothing of ours is persisted
    // yet. Recovery (knowing which of these you've already collected) is slice 4.
    //
    // Markers are pooled by index, not keyed by pin: there is no per-marker
    // animation to preserve across frames, so the pool just gets re-pointed at
    // whatever the current sorted list is.
    internal class DeathMarkers
    {
        // Pins change rarely (a death, a map load), so don't refilter every frame.
        private const float RescanInterval = 0.5f;

        private const float IconSize = 18f;
        private const float PlatePad = 4f;
        private const float NearIconScale = 1.3f;
        private const float TopGap = 5f;      // line to top of skull
        private const float TextGap = 1f;     // skull to distance text
        private const float ChevronGap = 3f;  // skull to chevron
        private const int ChevronWidth = 5;
        private const int ChevronHeight = 9;

        // Two markers closer together than this stagger their distance labels so
        // the numbers don't overlap.
        private const float OverlapPx = 34f;

        // Slice 4 fallback: standing this close to a death with no grave of ours in
        // sight means the grave is already gone, so the death counts as collected.
        // Throttled because it sweeps the scene for tombstones.
        private const float VisitCheckInterval = 1f;

        // How far from the pin an owned grave still counts as "this death's
        // grave" for that fallback. Wider than RecoveredDeaths.MatchRadius on
        // purpose: in testing a grave stood well outside 10 m of its pin and
        // got the death marked collected while it still held loot.
        private const float VisitGraveRadius = 40f;

        private const float FarAlpha = 0.6f;

        private class Marker
        {
            public RectTransform Root;
            public CanvasGroup Group;
            public Image Plate;
            public Image Icon;
            public Image Chevron;
            public TMP_Text Distance;
        }

        private class Death
        {
            public Vector3 Pos;
            public float Distance;
            public float Offset;   // -1..1 across the strip, already clamped
            public bool Clamped;
        }

        private RectTransform _parent;
        private TMP_Text _fontSource;
        private bool _built;

        private readonly List<Marker> _pool = new List<Marker>();
        private readonly List<Death> _deaths = new List<Death>();
        private readonly List<Vector3> _pinPositions = new List<Vector3>();
        private float _nextRescan;
        private int _lastPinCount = -1;
        private float _nextVisitCheck;

        private float _appliedFontSize = -1f;

        internal int Count { get; private set; }

        internal void Build(RectTransform stripRoot, TMP_Text fontSource)
        {
            _parent = stripRoot;
            _fontSource = fontSource;
            _built = fontSource != null && stripRoot != null;
            if (!_built)
            {
                Plugin.Log.LogWarning("compass: no font source or strip root, death markers are off for this session.");
            }
        }

        internal void Hide()
        {
            Count = 0;
            for (int i = 0; i < _pool.Count; i++)
            {
                if (_pool[i].Root.gameObject.activeSelf) _pool[i].Root.gameObject.SetActive(false);
            }
        }

        // halfWidth and halfFov come from the strip so both agree on the mapping.
        internal void Update(float cameraYaw, float halfWidth, float halfFov, float fontSize)
        {
            if (!_built) { Count = 0; return; }

            if (!Plugin.ShowDeathMarkers.Value)
            {
                Hide();
                return;
            }

            Player player = Player.m_localPlayer;
            if (player == null) { Hide(); return; }

            RescanIfDue();
            BuildDeathList(player.transform.position, cameraYaw, halfFov);
            CheckVisited(player.transform.position);

            Count = _deaths.Count;
            if (Count == 0) { Hide(); return; }

            float near = Plugin.MarkerNearDistance.Value;
            float lastX = float.NegativeInfinity;
            int row = 0;

            for (int i = 0; i < _deaths.Count; i++)
            {
                Death d = _deaths[i];
                Marker m = GetMarker(i, fontSize);
                if (!m.Root.gameObject.activeSelf) m.Root.gameObject.SetActive(true);

                float x = d.Offset * halfWidth;
                m.Root.anchoredPosition = new Vector2(x, -TopGap);

                // Nearest is brightest. Everything else steps back so the one you
                // are actually walking to is obvious at a glance.
                m.Group.alpha = i == 0 ? 1f : FarAlpha;

                bool isNear = d.Distance <= near;
                float scale = isNear ? NearIconScale : 1f;
                float icon = IconSize * scale;
                m.Icon.rectTransform.sizeDelta = new Vector2(icon, icon);
                m.Plate.rectTransform.sizeDelta = new Vector2(icon + PlatePad * 2f, icon + PlatePad * 2f);

                // Under the near threshold the number stops being useful and starts
                // being clutter: drop it and grow the skull so you look up.
                bool showText = !isNear;
                if (m.Distance.gameObject.activeSelf != showText) m.Distance.gameObject.SetActive(showText);
                if (showText)
                {
                    // Stagger only against the marker drawn just before this one in
                    // x order; that is enough to unpick a pair sitting on top of
                    // each other without shuffling the whole row.
                    row = Mathf.Abs(x - lastX) < OverlapPx ? 1 - row : 0;
                    lastX = x;

                    m.Distance.text = Mathf.RoundToInt(d.Distance) + " m";
                    float lineHeight = fontSize * 1.15f;
                    m.Distance.rectTransform.anchoredPosition =
                        new Vector2(0f, -(IconSize * scale + TextGap + row * lineHeight));
                }

                bool showChevron = d.Clamped;
                if (m.Chevron.gameObject.activeSelf != showChevron) m.Chevron.gameObject.SetActive(showChevron);
                if (showChevron)
                {
                    float dir = d.Offset >= 0f ? 1f : -1f;
                    RectTransform crt = m.Chevron.rectTransform;
                    crt.anchoredPosition = new Vector2(dir * (IconSize * scale * 0.5f + ChevronGap),
                                                       -IconSize * scale * 0.5f);
                    crt.localScale = new Vector3(dir, 1f, 1f);
                }
            }

            for (int i = _deaths.Count; i < _pool.Count; i++)
            {
                if (_pool[i].Root.gameObject.activeSelf) _pool[i].Root.gameObject.SetActive(false);
            }

            // Nearer draws on top. Walk from the back so the nearest ends up last
            // in the sibling order, which is what uGUI draws over everything else.
            for (int i = _deaths.Count - 1; i >= 0; i--) _pool[i].Root.SetAsLastSibling();
        }

        // Pull death pin positions out of the minimap. Only the positions are kept,
        // so nothing here holds a reference into vanilla's pin list.
        private void RescanIfDue()
        {
            Minimap map = Minimap.instance;
            if (map == null || map.m_pins == null) return;

            bool due = Time.time >= _nextRescan || map.m_pins.Count != _lastPinCount;
            if (!due) return;

            _nextRescan = Time.time + RescanInterval;
            _lastPinCount = map.m_pins.Count;

            _pinPositions.Clear();
            for (int i = 0; i < map.m_pins.Count; i++)
            {
                Minimap.PinData pin = map.m_pins[i];
                if (pin != null && pin.m_type == Minimap.PinType.Death) _pinPositions.Add(pin.m_pos);
            }
        }

        private void BuildDeathList(Vector3 playerPos, float cameraYaw, float halfFov)
        {
            _deaths.Clear();

            for (int i = 0; i < _pinPositions.Count; i++)
            {
                Vector3 pos = _pinPositions[i];
                if (RecoveredDeaths.IsRecovered(pos)) continue;

                Vector3 d = pos - playerPos;
                float bearing = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                float t = Mathf.DeltaAngle(cameraYaw, bearing) / halfFov;

                _deaths.Add(new Death
                {
                    Pos = pos,
                    Distance = Utils.DistanceXZ(playerPos, pos),
                    Offset = Mathf.Clamp(t, -1f, 1f),
                    Clamped = Mathf.Abs(t) > 1f,
                });
            }

            _deaths.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            int max = Plugin.MaxDeathMarkers.Value;
            if (max > 0 && _deaths.Count > max) _deaths.RemoveRange(max, _deaths.Count - max);
        }

        // Deaths from before the mod was installed, and deaths that never produced
        // a grave at all (empty inventory, or the DeathKeepInventory global key),
        // can never fire the tombstone hooks. Walking up to one and finding no
        // grave of ours is the only evidence available, so take it.
        private void CheckVisited(Vector3 playerPos)
        {
            if (_deaths.Count == 0) return;
            if (Time.time < _nextVisitCheck) return;

            // _deaths is sorted, so the first one is the only candidate.
            Death nearest = _deaths[0];
            if (nearest.Distance > RecoveredDeaths.MatchRadius) return;

            // The grave is a networked object; until its zone is fully in,
            // "no grave here" is not evidence of anything.
            if (ZNetScene.instance == null || !ZNetScene.instance.IsAreaReady(nearest.Pos)) return;

            _nextVisitCheck = Time.time + VisitCheckInterval;

            int owned;
            float nearestGrave;
            if (OwnedGraveNear(nearest.Pos, VisitGraveRadius, out owned, out nearestGrave)) return;

            Plugin.Log.LogInfo("recovery: visit check at " + nearest.Distance.ToString("0") + " m from the pin found " +
                owned + " owned grave(s) loaded, nearest " +
                (owned > 0 ? nearestGrave.ToString("0") + " m" : "none") + " away, none within " + VisitGraveRadius + " m.");
            RecoveredDeaths.MarkRecovered(nearest.Pos, "visited, no grave present");
            _nextRescan = 0f; // drop the marker on the next frame rather than in half a second
        }

        private static bool OwnedGraveNear(Vector3 pos, float radius, out int owned, out float nearest)
        {
            owned = 0;
            nearest = float.MaxValue;
            TombStone[] stones = Object.FindObjectsByType<TombStone>(FindObjectsSortMode.None);
            for (int i = 0; i < stones.Length; i++)
            {
                TombStone stone = stones[i];
                if (stone == null || stone.m_nview == null || !stone.m_nview.IsValid()) continue;
                if (!stone.IsOwner()) continue;
                owned++;
                float d = Utils.DistanceXZ(pos, stone.transform.position);
                if (d < nearest) nearest = d;
                if (d <= radius) return true;
            }
            return false;
        }

        private Marker GetMarker(int index, float fontSize)
        {
            while (_pool.Count <= index) _pool.Add(BuildMarker(_pool.Count));

            Marker m = _pool[index];

            // Every marker gets the size on the way in. Applying it only when the
            // config value changed left markers built later carrying whatever size
            // they inherited from the cloned vanilla text, which is why the second
            // skull's distance came out huge.
            if (!Mathf.Approximately(fontSize, m.Distance.fontSize)) m.Distance.fontSize = fontSize;

            if (!Mathf.Approximately(fontSize, _appliedFontSize))
            {
                for (int i = 0; i < _pool.Count; i++) _pool[i].Distance.fontSize = fontSize;
                _appliedFontSize = fontSize;
            }
            return m;
        }

        private Marker BuildMarker(int index)
        {
            GameObject go = new GameObject("DeathMarker_" + index, typeof(RectTransform), typeof(CanvasGroup));
            RectTransform root = (RectTransform)go.transform;
            root.SetParent(_parent, false);
            // Hangs off the bottom of the strip, which is where the line is, so the
            // cardinals sit above the rule and the markers below it.
            root.anchorMin = new Vector2(0.5f, 0f);
            root.anchorMax = new Vector2(0.5f, 0f);
            root.pivot = new Vector2(0.5f, 1f);
            root.sizeDelta = Vector2.zero;

            CanvasGroup group = go.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            // First child, so it draws behind the skull. Same clipped-corner plate
            // the status chips use in 001, so the two features look related.
            GameObject plateGo = new GameObject("Plate", typeof(RectTransform), typeof(Image));
            plateGo.transform.SetParent(root, false);
            Image plate = plateGo.GetComponent<Image>();
            plate.raycastTarget = false;
            plate.type = Image.Type.Sliced;
            plate.sprite = ChipSprite.Get();
            plate.color = Palette.Plate;
            RectTransform prt = plate.rectTransform;
            prt.anchorMin = new Vector2(0.5f, 1f);
            prt.anchorMax = new Vector2(0.5f, 1f);
            prt.pivot = new Vector2(0.5f, 1f);
            prt.anchoredPosition = new Vector2(0f, PlatePad);
            prt.sizeDelta = new Vector2(IconSize + PlatePad * 2f, IconSize + PlatePad * 2f);

            GameObject iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(root, false);
            Image icon = iconGo.GetComponent<Image>();
            icon.raycastTarget = false;
            icon.preserveAspect = true;
            icon.color = Palette.EmberHealth;
            icon.sprite = DeathSprite();
            RectTransform irt = icon.rectTransform;
            irt.anchorMin = new Vector2(0.5f, 1f);
            irt.anchorMax = new Vector2(0.5f, 1f);
            irt.pivot = new Vector2(0.5f, 1f);
            irt.anchoredPosition = Vector2.zero;
            irt.sizeDelta = new Vector2(IconSize, IconSize);

            GameObject chevGo = new GameObject("Chevron", typeof(RectTransform), typeof(Image));
            chevGo.transform.SetParent(root, false);
            Image chevron = chevGo.GetComponent<Image>();
            chevron.raycastTarget = false;
            chevron.color = Palette.EmberHealth;
            chevron.sprite = ChevronSprite.Get(ChevronWidth, ChevronHeight);
            RectTransform crt = chevron.rectTransform;
            crt.anchorMin = new Vector2(0.5f, 1f);
            crt.anchorMax = new Vector2(0.5f, 1f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(ChevronWidth, ChevronHeight);
            chevGo.SetActive(false);

            TMP_Text distance = Object.Instantiate(_fontSource, root);
            distance.gameObject.name = "Distance";
            distance.gameObject.SetActive(true);
            distance.text = "";
            distance.color = Palette.Bone;
            distance.alignment = TextAlignmentOptions.Top;
            distance.textWrappingMode = TextWrappingModes.NoWrap;
            distance.overflowMode = TextOverflowModes.Overflow;
            distance.enableAutoSizing = false;
            distance.fontStyle = FontStyles.Normal;
            distance.characterSpacing = 0f;
            distance.fontSize = _appliedFontSize > 0f ? _appliedFontSize : Plugin.LabelSize.Value;
            RectTransform drt = distance.rectTransform;
            drt.anchorMin = new Vector2(0.5f, 1f);
            drt.anchorMax = new Vector2(0.5f, 1f);
            drt.pivot = new Vector2(0.5f, 1f);
            drt.localRotation = Quaternion.identity;
            drt.localScale = Vector3.one;
            drt.sizeDelta = new Vector2(80f, 18f);

            foreach (Animator a in distance.GetComponentsInChildren<Animator>(true)) Object.Destroy(a);
            HudFont.Apply(distance);

            root.gameObject.SetActive(false);
            if (index == 0) Plugin.Log.LogInfo("compass: death marker pool started under " + HudPath.Of(_parent));

            return new Marker { Root = root, Group = group, Plate = plate, Icon = icon, Chevron = chevron, Distance = distance };
        }

        // Vanilla's own skull, so the strip and the map agree. GetSprite is the
        // same lookup AddPin uses to stamp PinData.m_icon; falling back to a live
        // pin's icon covers the case where the icon list is not ready yet.
        private static Sprite DeathSprite()
        {
            Minimap map = Minimap.instance;
            if (map == null) return null;

            Sprite sprite = map.GetSprite(Minimap.PinType.Death);
            if (sprite != null) return sprite;

            if (map.m_pins == null) return null;
            for (int i = 0; i < map.m_pins.Count; i++)
            {
                Minimap.PinData pin = map.m_pins[i];
                if (pin != null && pin.m_type == Minimap.PinType.Death && pin.m_icon != null) return pin.m_icon;
            }
            return null;
        }
    }
}
