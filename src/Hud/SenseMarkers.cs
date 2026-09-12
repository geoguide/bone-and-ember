using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoneAndEmber
{
    // The markers for docs/design/006-sense.md: a diamond over every dropped item
    // and pickable near you, revealed near to far as the sweep passes.
    //
    // Two weights, which is the whole reason this reads as information rather
    // than noise. A dropped item (ItemDrop) is something you or a corpse left on
    // the ground: solid gold spike, and it names itself up close. A pickable (a
    // berry bush, a mushroom, beach flint) is a hollow bone spike at a fraction
    // of the brightness and never takes a name. In a forest pickables are the
    // overwhelming majority of what is nearby, and if they read as loud as your
    // loot then nothing reads at all.
    //
    // The marker is a downward spike whose point sits on the thing it marks, on
    // a dark backing. Earlier builds used a diamond and it lost to the weather:
    // Valheim's rain, mist and embers are themselves small bright diamonds and
    // squares, so the marker was competing on the one axis it could not win.
    //
    // Targets are found with Physics.OverlapSphere rather than an instance list
    // because vanilla does not expose one: ItemDrop.s_instances is private with
    // no accessor, and Pickable keeps no list at all. OverlapSphere is also what
    // vanilla itself uses for this (Player.AutoPickup, Piece.OnPlaced).
    internal class SenseMarkers
    {
        private const float RescanInterval = 0.25f;     // 4 Hz
        private const float OcclusionInterval = 0.25f;  // runs even while frozen
        private const int ColliderBuffer = 256;

        private const float MarkerUp = 0.3f;          // metres above the item
        private const float NameDistance = 5f;        // names appear inside this
        private const float NearAlphaDistance = 5f;   // full alpha out to here
        private const float FarAlpha = 0.35f;
        private const float PickableAlphaScale = 0.55f;
        private const float NameAlpha = 0.7f;

        // Past this multiple of the range a frozen marker is behind you and drops
        // out on its own, so walking away thins them out instead of dragging the
        // whole set along until the grace period runs out.
        private const float DropOffScale = 1.25f;

        private const float FadeInMs = 120f;

        // The backing is grown this many pixels past the icon on every side.
        private const int BackingPad = 2;

        // The spike is this much taller than it is wide. Tall and narrow is what
        // separates it from anything the weather throws.
        private const float AspectRatio = 1.7f;

        // The occlusion ray stops this far short of the item so the item's own
        // collider cannot occlude it. Pickables sit on the "piece" layer, which
        // has to be in the view-block mask for walls to work, so without this
        // every berry bush would hide itself.
        private const float OcclusionBackoff = 0.6f;

        private class Target
        {
            public Transform Tr;
            public bool IsDrop;
            public bool Unknown;      // material not in the player's known list
            public string Name;       // localized, drops only
            public float Distance;
            public bool Occluded;
            public Tween Alpha;
            public bool Revealed;     // the sweep has passed it at least once
        }

        private class Marker
        {
            public RectTransform Root;
            public CanvasGroup Group;
            public Image Backing;
            public Image Diamond;
            public TMP_Text Label;
        }

        private RectTransform _parent;
        private RectTransform _canvasRect;
        private Camera _canvasCamera;
        private TMP_Text _fontSource;
        private bool _built;

        private readonly List<Marker> _pool = new List<Marker>();
        private readonly List<Target> _targets = new List<Target>();
        private readonly List<Target> _scratch = new List<Target>();
        private readonly Dictionary<Transform, Target> _byTransform = new Dictionary<Transform, Target>();
        private readonly HashSet<Transform> _seen = new HashSet<Transform>();
        private readonly Collider[] _colliders = new Collider[ColliderBuffer];

        private int _scanMask;
        private int _viewBlockMask;
        private float _nextRescan;
        private float _nextOcclusion;
        private float _appliedFontSize = -1f;
        private int _appliedMarkerSize = -1;

        // Last scan's tallies, for the DevCommands log line.
        private int _lastColliders;
        private int _lastDrops;
        private int _lastPickables;
        private int _lastOccluded;

        internal int Count { get; private set; }
        internal string LastScanSummary
        {
            get
            {
                return _lastColliders + " colliders -> " + _lastDrops + " drops, " +
                       _lastPickables + " pickables, " + _lastOccluded + " occluded";
            }
        }

        internal void Build(RectTransform root, RectTransform canvasRect, Camera canvasCamera, TMP_Text fontSource)
        {
            _parent = root;
            _canvasRect = canvasRect;
            _canvasCamera = canvasCamera;
            _fontSource = fontSource;

            // Item drops are on "item". Pickables have no single layer: the layer
            // is set per prefab in the asset, and in practice they land on the
            // piece layers, so scan the superset and sort it out by component.
            _scanMask = LayerMask.GetMask("item", "piece", "piece_nonsolid");
            // Same set BaseAI uses to decide whether it can see you.
            _viewBlockMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "viewblock", "vehicle");

            _built = root != null && fontSource != null;
            if (!_built)
            {
                Plugin.Log.LogWarning("sense: no font source or root, markers are off for this session.");
            }
        }

        // Drops everything and snaps every marker off.
        internal void Clear()
        {
            Count = 0;
            _targets.Clear();
            _byTransform.Clear();
            _nextRescan = 0f;
            _nextOcclusion = 0f;
            for (int i = 0; i < _pool.Count; i++)
            {
                if (_pool[i].Root.gameObject.activeSelf) _pool[i].Root.gameObject.SetActive(false);
            }
        }

        internal void ForceRescan()
        {
            _nextRescan = 0f;
            _nextOcclusion = 0f;
        }

        // sweepRadius is how far the reveal wave has travelled, in metres, and is
        // what gates a marker appearing. frozen stops new scans but keeps
        // everything already found alive and correctly placed, which is what lets
        // the set ride along while you walk. fadingOut retargets everything to
        // zero over fadeOutMs.
        internal void Update(Player player, Camera cam, float sweepRadius, bool frozen, bool fadingOut, float fadeOutMs, float dt)
        {
            if (!_built || player == null || cam == null) { Count = 0; return; }

            if (!fadingOut && !frozen && Time.time >= _nextRescan)
            {
                _nextRescan = Time.time + RescanInterval;
                Rescan(player, cam);
            }

            // Occlusion keeps running even while the set is frozen, so walking
            // behind a rock still hides what is behind it. "Never through walls"
            // does not get a grace period.
            if (!fadingOut && Time.time >= _nextOcclusion)
            {
                _nextOcclusion = Time.time + OcclusionInterval;
                UpdateOcclusion(cam);
            }

            ApplySizesIfChanged();

            Vector3 origin = player.transform.position;
            float range = Mathf.Max(NearAlphaDistance + 1f, Plugin.SenseRange.Value);
            bool showNames = Plugin.SenseShowNames.Value;

            int visible = 0;
            for (int i = 0; i < _targets.Count; i++)
            {
                Target t = _targets[i];
                Marker m = GetMarker(i);

                // Picked up, despawned, or the zone unloaded underneath it.
                if (t.Tr == null)
                {
                    if (m.Root.gameObject.activeSelf) m.Root.gameObject.SetActive(false);
                    continue;
                }

                // Live distance, not the one from the last scan: while frozen you
                // are walking, so this is what fades things out behind you.
                t.Distance = Utils.DistanceXZ(origin, t.Tr.position);

                if (!t.Revealed && !fadingOut && sweepRadius >= t.Distance) t.Revealed = true;

                bool gone = t.Distance > range * DropOffScale;
                float target = (fadingOut || gone || !t.Revealed || t.Occluded) ? 0f : AlphaFor(t, range);
                if (Plugin.MotionEnabled.Value)
                {
                    t.Alpha.Retarget(target, target > t.Alpha.Value ? FadeInMs : fadeOutMs);
                    t.Alpha.Update(dt);
                }
                else
                {
                    t.Alpha.Snap(target);
                }

                if (t.Alpha.Value <= 0.001f)
                {
                    if (m.Root.gameObject.activeSelf) m.Root.gameObject.SetActive(false);
                    continue;
                }

                Vector3 screen = cam.WorldToScreenPointScaled(t.Tr.position + Vector3.up * MarkerUp);
                Vector2 local = Vector2.zero;
                bool onScreen = screen.z > 0f &&
                                screen.x >= 0f && screen.x <= Screen.width &&
                                screen.y >= 0f && screen.y <= Screen.height &&
                                SenseOverlay.ToCanvas(_canvasRect, _canvasCamera, screen, out local);
                if (!onScreen)
                {
                    if (m.Root.gameObject.activeSelf) m.Root.gameObject.SetActive(false);
                    continue;
                }

                if (!m.Root.gameObject.activeSelf) m.Root.gameObject.SetActive(true);
                m.Root.anchoredPosition = local;
                m.Group.alpha = t.Alpha.Value;

                Sprite want = SpriteFor(t.IsDrop);
                if (m.Diamond.sprite != want) m.Diamond.sprite = want;

                Color color = ColorFor(t);
                if (m.Diamond.color != color) m.Diamond.color = color;

                bool wantsName = t.IsDrop && showNames &&
                                 t.Distance <= NameDistance && !string.IsNullOrEmpty(t.Name);
                if (m.Label.gameObject.activeSelf != wantsName) m.Label.gameObject.SetActive(wantsName);
                if (wantsName && m.Label.text != t.Name) m.Label.text = t.Name;

                visible++;
            }

            // Deactivate the tail past the current target count.
            for (int i = _targets.Count; i < _pool.Count; i++)
            {
                if (_pool[i].Root.gameObject.activeSelf) _pool[i].Root.gameObject.SetActive(false);
            }

            Count = visible;
        }

        // Gold for something you dropped or something dropped for you, bone for
        // what grows there anyway, ember for a material you have never picked up.
        private static Color ColorFor(Target t)
        {
            if (t.Unknown) return Palette.EmberHealth;
            return t.IsDrop ? Palette.Gold : Palette.Bone;
        }

        private static Sprite SpriteFor(bool isDrop)
        {
            int w = MarkerWidth();
            int h = MarkerHeight(w);
            // Solid for something dropped, outlined for something growing there.
            // A thin stroke on purpose: the spike narrows toward the point, so a
            // thicker one closes the interior up and the hollow version stops
            // looking any different from the solid one.
            return isDrop
                ? MarkerSprite.Get(w, h, 0, 0)
                : MarkerSprite.Get(w, h, 0, Mathf.Max(1, Mathf.RoundToInt(w * 0.15f)));
        }

        private static int MarkerWidth()
        {
            return Mathf.Clamp(Plugin.SenseMarkerSize.Value, 4, 24);
        }

        private static int MarkerHeight(int width)
        {
            return Mathf.RoundToInt(width * AspectRatio);
        }

        // 1.0 out to NearAlphaDistance, down to FarAlpha at the configured range,
        // then a flat scale down for pickables.
        private static float AlphaFor(Target t, float range)
        {
            float k = Mathf.Clamp01((t.Distance - NearAlphaDistance) / (range - NearAlphaDistance));
            float a = Mathf.Lerp(1f, FarAlpha, k);
            if (!t.IsDrop) a *= PickableAlphaScale;
            return a;
        }

        private void UpdateOcclusion(Camera cam)
        {
            Vector3 eye = cam.transform.position;
            _lastOccluded = 0;
            for (int i = 0; i < _targets.Count; i++)
            {
                Target t = _targets[i];
                if (t.Tr == null) continue;

                Vector3 delta = (t.Tr.position + Vector3.up * MarkerUp) - eye;
                float dist = delta.magnitude - OcclusionBackoff;
                t.Occluded = dist > 0.01f && Physics.Raycast(eye, delta.normalized, dist, _viewBlockMask);
                if (t.Occluded) _lastOccluded++;
            }
        }

        private void Rescan(Player player, Camera cam)
        {
            Vector3 origin = player.transform.position;
            float range = Plugin.SenseRange.Value;
            int max = Plugin.SenseMaxMarkers.Value;

            int hits = Physics.OverlapSphereNonAlloc(origin, range, _colliders, _scanMask);
            _lastColliders = hits;
            _lastDrops = 0;
            _lastPickables = 0;

            _scratch.Clear();
            _byTransform.Clear();
            for (int i = 0; i < _targets.Count; i++)
            {
                if (_targets[i].Tr != null) _byTransform[_targets[i].Tr] = _targets[i];
            }

            // Dedupe on the transform we will actually read positions from: one
            // item can own several colliders.
            for (int i = 0; i < hits; i++)
            {
                Collider col = _colliders[i];
                if (col == null) continue;

                Transform tr;
                bool isDrop;
                string sharedName;
                if (!Classify(col, out tr, out isDrop, out sharedName)) continue;
                if (!_seen.Add(tr)) continue;

                if (isDrop) _lastDrops++; else _lastPickables++;

                Target existing;
                if (_byTransform.TryGetValue(tr, out existing))
                {
                    existing.Distance = Utils.DistanceXZ(origin, tr.position);
                    _scratch.Add(existing);
                    continue;
                }

                Target t = new Target
                {
                    Tr = tr,
                    IsDrop = isDrop,
                    Distance = Utils.DistanceXZ(origin, tr.position),
                    Unknown = IsUnknown(player, sharedName),
                    Name = isDrop && !string.IsNullOrEmpty(sharedName)
                        ? Localization.instance.Localize(sharedName)
                        : null,
                };
                t.Alpha.Snap(0f);
                _scratch.Add(t);
            }
            _seen.Clear();

            _scratch.Sort(CompareByDistance);
            if (max > 0 && _scratch.Count > max) _scratch.RemoveRange(max, _scratch.Count - max);

            _targets.Clear();
            _targets.AddRange(_scratch);

            // Occlusion last, so it only pays for the markers that survived the cap.
            UpdateOcclusion(cam);
            _nextOcclusion = Time.time + OcclusionInterval;
        }

        private static int CompareByDistance(Target a, Target b)
        {
            return a.Distance.CompareTo(b.Distance);
        }

        // Turns a collider into the thing we should mark, or rejects it.
        private static bool Classify(Collider col, out Transform tr, out bool isDrop, out string sharedName)
        {
            tr = null;
            isDrop = false;
            sharedName = null;

            // Item drops hang their collider off a child with a rigidbody, which is
            // how vanilla's auto-pickup finds them.
            Rigidbody rb = col.attachedRigidbody;
            ItemDrop drop = null;
            if (rb != null)
            {
                drop = rb.GetComponent<ItemDrop>();
                if (drop == null)
                {
                    // An item floating on water reparents its rigidbody to a dummy.
                    FloatingTerrainDummy dummy = rb.gameObject.GetComponent<FloatingTerrainDummy>();
                    if (dummy != null && dummy.m_parent != null)
                    {
                        drop = dummy.m_parent.gameObject.GetComponent<ItemDrop>();
                    }
                }
            }
            if (drop == null) drop = col.GetComponentInParent<ItemDrop>();

            if (drop != null)
            {
                // A drop that has been turned into a build piece is furniture now.
                if (drop.IsPiece()) return false;
                if (drop.m_nview == null || !drop.m_nview.IsValid()) return false;
                if (drop.m_itemData == null || drop.m_itemData.m_shared == null) return false;

                tr = drop.transform;
                isDrop = true;
                sharedName = drop.m_itemData.m_shared.m_name;
                return true;
            }

            Pickable pick = col.GetComponentInParent<Pickable>();
            if (pick != null)
            {
                // CanBePicked is vanilla's own filter, so a picked bush waiting on
                // its respawn timer does not get a marker.
                if (!pick.CanBePicked()) return false;

                tr = pick.transform;
                isDrop = false;
                sharedName = SharedNameOf(pick);
                return true;
            }

            return false;
        }

        private static string SharedNameOf(Pickable pick)
        {
            if (pick.m_itemPrefab == null) return null;
            ItemDrop proto = pick.m_itemPrefab.GetComponent<ItemDrop>();
            if (proto == null || proto.m_itemData == null || proto.m_itemData.m_shared == null) return null;
            return proto.m_itemData.m_shared.m_name;
        }

        // Known materials are keyed on the unlocalized token ("$item_flametal"),
        // not the prefab name and not the display text.
        private static bool IsUnknown(Player player, string sharedName)
        {
            if (player == null || string.IsNullOrEmpty(sharedName)) return false;
            return !player.IsMaterialKnown(sharedName);
        }

        private void ApplySizesIfChanged()
        {
            float fontSize = Plugin.LabelSize.Value;
            int markerSize = MarkerWidth();
            if (Mathf.Approximately(fontSize, _appliedFontSize) && markerSize == _appliedMarkerSize) return;
            _appliedFontSize = fontSize;
            _appliedMarkerSize = markerSize;

            for (int i = 0; i < _pool.Count; i++) LayoutMarker(_pool[i], fontSize, markerSize);
        }

        // Also called from BuildMarker: the pool grows during the update loop, so
        // a marker created after ApplySizesIfChanged has already run for this size
        // would otherwise keep the cloned source's rect and sit in the wrong place.
        // Both the icon and its backing are pivoted on the spike's apex, so the
        // point lands exactly on the projected world position and the body of the
        // marker stands above the thing rather than covering it.
        private static void LayoutMarker(Marker m, float fontSize, int width)
        {
            int height = MarkerHeight(width);
            m.Root.sizeDelta = new Vector2(width, height);

            RectTransform irt = m.Diamond.rectTransform;
            irt.pivot = new Vector2(0.5f, MarkerSprite.ApexPivot(height, 0));
            irt.sizeDelta = new Vector2(width, height);

            m.Backing.sprite = MarkerSprite.Get(width, height, BackingPad, 0);
            RectTransform brt = m.Backing.rectTransform;
            brt.pivot = new Vector2(0.5f, MarkerSprite.ApexPivot(height, BackingPad));
            brt.sizeDelta = new Vector2(MarkerSprite.Width(width, BackingPad), MarkerSprite.Height(height, BackingPad));

            // The name goes above the spike now. Below would put it over the item
            // the spike is pointing at.
            m.Label.fontSize = fontSize;
            RectTransform rt = m.Label.rectTransform;
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(Mathf.Max(80f, fontSize * 10f), Mathf.Ceil(fontSize * 1.2f));
            rt.anchoredPosition = new Vector2(0f, height + BackingPad + 3f);
        }

        private Marker GetMarker(int index)
        {
            while (_pool.Count <= index) _pool.Add(BuildMarker(_pool.Count));
            return _pool[index];
        }

        private Marker BuildMarker(int index)
        {
            int width = MarkerWidth();

            GameObject go = new GameObject("Marker_" + index, typeof(RectTransform), typeof(CanvasGroup));
            RectTransform root = (RectTransform)go.transform;
            root.SetParent(_parent, false);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);

            CanvasGroup group = go.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            // Backing first so it draws underneath. This is what separates a marker
            // from a snowflake.
            Image backing = MakeImage(root, "Backing", Palette.Plate);
            Image diamond = MakeImage(root, "Spike", Palette.Gold);
            diamond.sprite = SpriteFor(true);

            TMP_Text label = Object.Instantiate(_fontSource, root);
            label.gameObject.name = "Label";
            label.gameObject.SetActive(false);
            label.text = "";
            label.color = WithAlpha(Palette.Bone, NameAlpha);
            label.alignment = TextAlignmentOptions.Top;
            label.characterSpacing = 0f;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.enableAutoSizing = false;
            label.fontStyle = FontStyles.Normal;
            label.raycastTarget = false;
            RectTransform lrt = label.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
            lrt.pivot = new Vector2(0.5f, 0f);
            // Vanilla text prefabs carry baked rotation and scale, and animators
            // that would drive ours. ClonedBar documents what skipping this does.
            lrt.localRotation = Quaternion.identity;
            lrt.localScale = Vector3.one;
            foreach (Animator a in label.GetComponentsInChildren<Animator>(true)) Object.Destroy(a);
            HudFont.Apply(label);

            Marker m = new Marker { Root = root, Group = group, Backing = backing, Diamond = diamond, Label = label };
            LayoutMarker(m, Plugin.LabelSize.Value, width);
            go.SetActive(false);
            return m;
        }

        private static Image MakeImage(RectTransform parent, string name, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Image img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.color = color;
            RectTransform rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            return img;
        }

        private static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }
    }
}
