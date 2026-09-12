using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace BoneAndEmber.Patches
{
    // Slice 3 of docs/design/003-inventory.md: replace the wooden panel art on
    // the player and container panels (not crafting yet) with a flat dark
    // plate matching the slots, plus a thin bone border. Awake, not per-frame:
    // InventoryGui and its m_player/m_container panels are built once for the
    // whole game session, unlike the InventoryElement grids (003-notes.md).
    //
    // First attempt cleared vanilla's Bkg sprite and recolored that same Image
    // in place, which came out as a torn-edged black shape in game: the wood
    // sprite was being tinted, not replaced. Whatever puts it back (this game's
    // UI does carry Animators that rewrite their properties every frame even
    // sitting in a default state, which ClonedBar's own comment already flagged
    // for the HUD bars), editing vanilla's Image in place is not reliable. So
    // now vanilla's art is switched off outright and our own generated plate
    // and border go in front of it, exactly the way the slots work: cover it,
    // don't edit it.
    [HarmonyPatch(typeof(InventoryGui), "Awake")]
    internal static class InventoryPanelStylePatch
    {
        private class VanillaArt
        {
            internal GameObject GameObject;
            internal bool OriginalActive;
        }

        private class Overlay
        {
            internal GameObject Plate;
            internal GameObject Border;
            internal Image PlateImage;
        }

        private const float BorderAlpha = 0.25f;
        private const float FocusedBorderAlpha = 0.8f;

        private static readonly List<VanillaArt> Originals = new List<VanillaArt>();
        private static readonly List<Overlay> Overlays = new List<Overlay>();
        private static bool _subscribed;
        private static bool _loggedHighlight;

        // Our own borders per panel, so the gamepad "this panel has focus"
        // signal can live on them (see RefreshGamepadHighlight).
        private static Image _playerBorderImage;
        private static Image _containerBorderImage;

        // The player panel's own plate and border, kept so WeightBar can grow
        // them down over its row instead of leaving it sitting on the world.
        private static RectTransform _playerPanel;
        private static RectTransform _playerPlate;
        private static RectTransform _playerBorder;

        private static bool Prepare()
        {
            if (AccessTools.Method(typeof(InventoryGui), "Awake") != null) return true;

            Plugin.Log.LogError(
                "InventoryGui.Awake not found, the panel background restyle is off. " +
                "Re-run tools/decompile.sh and check what it is called now.");
            return false;
        }

        private static void Postfix(InventoryGui __instance)
        {
            Originals.Clear();
            Overlays.Clear();

            _playerPanel = __instance.m_player;
            CollectAndBuild(__instance.m_player, "player");
            CollectAndBuild(__instance.m_container, "container");

            if (!_subscribed)
            {
                _subscribed = true;
                Plugin.PanelRestyle.SettingChanged += (s, e) => Apply();
                Plugin.SlotPlateAlpha.SettingChanged += (s, e) => Apply();
            }

            Apply();
        }

        // Direct children only, deliberately: PlayerGrid/ContainerGrid (the slot
        // grids SlotStyle already owns) sit nested one level inside each panel,
        // and a recursive name search would also catch our own
        // "BoneAndEmber_Border" objects in there (the substring "border" is right
        // in the name) and fight slice 2.
        private static void CollectAndBuild(RectTransform panel, string label)
        {
            if (panel == null) return;
            bool foundAny = false;

            for (int i = 0; i < panel.childCount; i++)
            {
                Transform child = panel.GetChild(i);
                if (child.name.StartsWith("BoneAndEmber_")) continue;

                Image img = child.GetComponent<Image>();
                if (img == null) continue;

                string lower = child.name.ToLowerInvariant();
                if (lower.Contains("border") || lower.Contains("bkg") || lower.Contains("background"))
                {
                    Originals.Add(new VanillaArt { GameObject = child.gameObject, OriginalActive = child.gameObject.activeSelf });
                    Plugin.Log.LogInfo("panel style: " + label + " will hide vanilla art " + HudPath.Of(child) +
                        " (sprite " + (img.sprite != null ? img.sprite.name : "(none)") + ")");
                    foundAny = true;
                }
            }

            if (!foundAny)
            {
                Plugin.Log.LogWarning("panel style: no bkg/border image found as a direct child of the " + label +
                    " panel (" + HudPath.Of(panel) + "). Nothing to hide there; the overlay still goes in.");
            }

            Overlay overlay = BuildOverlay(panel, label);
            Overlays.Add(overlay);

            Image borderImage = overlay.Border != null ? overlay.Border.GetComponent<Image>() : null;
            if (panel == _playerPanel)
            {
                _playerPlate = overlay.Plate != null ? overlay.Plate.transform as RectTransform : null;
                _playerBorder = overlay.Border != null ? overlay.Border.transform as RectTransform : null;
                _playerBorderImage = borderImage;
            }
            else
            {
                _containerBorderImage = borderImage;
            }
        }

        // Vanilla's UIGroupHandler switches m_enableWhenActiveAndGamepad on
        // whenever a pad is the active input and this panel's group has focus
        // (UIGroupHandler.Update). In vanilla that object sits under the opaque
        // wooden art and reads as a gold rim; with the art hidden it shows as a
        // solid gold slab over our plate, which is what "everything goes yellow
        // on the d-pad" was. Alpha it out through a CanvasGroup (SetActive is
        // vanilla's and rewritten every frame) and carry the same focus signal
        // on our own border as a gold edge instead. Called every frame from
        // InventorySlotStylePatch, once per grid.
        internal static void RefreshGamepadHighlight(InventoryGrid grid, bool isPlayerGrid)
        {
            if (grid == null || grid.m_uiGroup == null) return;
            bool on = Plugin.PanelRestyle.Value;

            GameObject highlight = grid.m_uiGroup.m_enableWhenActiveAndGamepad;
            if (highlight != null)
            {
                CanvasGroup group = highlight.GetComponent<CanvasGroup>();
                if (group == null) group = highlight.AddComponent<CanvasGroup>();
                float alpha = on ? 0f : 1f;
                if (!Mathf.Approximately(group.alpha, alpha)) group.alpha = alpha;

                if (!_loggedHighlight)
                {
                    _loggedHighlight = true;
                    Plugin.Log.LogInfo("panel style: gamepad group highlight is " + HudPath.Of(highlight.transform) +
                        ", kept at alpha 0 while PanelRestyle is on; focus shows on our border instead.");
                }
            }

            Image border = isPlayerGrid ? _playerBorderImage : _containerBorderImage;
            if (border == null || !on) return;

            bool focused = grid.m_uiGroup.IsActive &&
                (ZInput.IsExclusiveGamepadActive() || grid.m_uiGroup.m_setDefaultOnKBM);
            Color color = focused
                ? new Color(Palette.Gold.r, Palette.Gold.g, Palette.Gold.b, FocusedBorderAlpha)
                : new Color(Palette.Bone.r, Palette.Bone.g, Palette.Bone.b, BorderAlpha);
            if (border.color != color) border.color = color;
        }

        // Grows the player panel's plate and border downward so they wrap
        // whatever hangs below the panel's own rect, with padding. Only our
        // own generated rects move; the vanilla panel and the grid inside it
        // are left exactly where they are.
        internal static void WrapBelowPlayerPanel(RectTransform content, float padding)
        {
            if (_playerPanel == null || content == null || _playerPlate == null) return;

            Vector3[] contentCorners = new Vector3[4];
            content.GetWorldCorners(contentCorners);
            Vector3[] panelCorners = new Vector3[4];
            _playerPanel.GetWorldCorners(panelCorners);

            float contentBottom = Mathf.Min(contentCorners[0].y, contentCorners[3].y);
            float panelBottom = Mathf.Min(panelCorners[0].y, panelCorners[3].y);

            float scale = _playerPanel.lossyScale.y;
            if (Mathf.Approximately(scale, 0f)) scale = 1f;

            float overshoot = (panelBottom - contentBottom) / scale;
            float extend = Mathf.Max(0f, overshoot + padding);

            Extend(_playerPlate, extend);
            Extend(_playerBorder, extend);

            Plugin.Log.LogInfo("panel style: extended the player plate " + extend.ToString("F1") +
                "px below the panel to wrap the weight row (padding " + padding + "px); plate height is now " +
                _playerPlate.rect.height.ToString("F1") + "px.");
        }

        // Mirror of WrapBelowPlayerPanel for content that sits above the
        // panel's own rect, like the filter chips.
        internal static void WrapAbovePlayerPanel(RectTransform content, float padding)
        {
            if (_playerPanel == null || content == null || _playerPlate == null) return;

            Vector3[] contentCorners = new Vector3[4];
            content.GetWorldCorners(contentCorners);
            Vector3[] panelCorners = new Vector3[4];
            _playerPanel.GetWorldCorners(panelCorners);

            float contentTop = Mathf.Max(contentCorners[1].y, contentCorners[2].y);
            float panelTop = Mathf.Max(panelCorners[1].y, panelCorners[2].y);

            float scale = _playerPanel.lossyScale.y;
            if (Mathf.Approximately(scale, 0f)) scale = 1f;

            float overshoot = (contentTop - panelTop) / scale;
            float extend = Mathf.Max(0f, overshoot + padding);

            ExtendUp(_playerPlate, extend);
            ExtendUp(_playerBorder, extend);

            Plugin.Log.LogInfo("panel style: extended the player plate " + extend.ToString("F1") +
                "px above the panel to wrap the filter row (padding " + padding + "px); plate height is now " +
                _playerPlate.rect.height.ToString("F1") + "px.");
        }

        private static void ExtendUp(RectTransform rt, float extend)
        {
            if (rt == null) return;
            rt.offsetMax = new Vector2(rt.offsetMax.x, extend);
        }

        private static void Extend(RectTransform rt, float extend)
        {
            if (rt == null) return;
            rt.offsetMin = new Vector2(rt.offsetMin.x, -extend);
        }

        private static Overlay BuildOverlay(RectTransform panel, string label)
        {
            GameObject plateGo = new GameObject("BoneAndEmber_PanelPlate", typeof(RectTransform), typeof(Image));
            RectTransform plateRect = (RectTransform)plateGo.transform;
            plateRect.SetParent(panel, false);
            plateRect.localRotation = Quaternion.identity;
            plateRect.localScale = Vector3.one;
            Stretch(plateRect);
            plateRect.SetSiblingIndex(0);

            Image plateImg = plateGo.GetComponent<Image>();
            plateImg.sprite = null; // flat generated rectangle: Unity's own white pixel, tinted
            plateImg.type = Image.Type.Simple;
            plateImg.raycastTarget = false;
            plateImg.color = Palette.PanelPlate;

            GameObject borderGo = new GameObject("BoneAndEmber_PanelBorder", typeof(RectTransform), typeof(Image));
            RectTransform borderRect = (RectTransform)borderGo.transform;
            borderRect.SetParent(panel, false);
            borderRect.localRotation = Quaternion.identity;
            borderRect.localScale = Vector3.one;
            Stretch(borderRect);
            borderRect.SetSiblingIndex(1);

            Image borderImg = borderGo.GetComponent<Image>();
            borderImg.sprite = SlotBorderSprite.Get(); // same thin bone frame the slots use
            borderImg.type = Image.Type.Sliced;
            borderImg.raycastTarget = false;
            borderImg.color = new Color(Palette.Bone.r, Palette.Bone.g, Palette.Bone.b, BorderAlpha);

            Plugin.Log.LogInfo("panel style: " + label + " overlay built under " + HudPath.Of(panel) +
                " (plate at sibling 0, border at 1, panel size and position untouched)");
            return new Overlay { Plate = plateGo, Border = borderGo, PlateImage = plateImg };
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void Apply()
        {
            bool on = Plugin.PanelRestyle.Value;

            for (int i = 0; i < Originals.Count; i++)
            {
                VanillaArt art = Originals[i];
                if (art.GameObject == null) continue;
                art.GameObject.SetActive(on ? false : art.OriginalActive);
            }

            Color plateColor = Palette.PanelPlate;
            for (int i = 0; i < Overlays.Count; i++)
            {
                Overlay o = Overlays[i];
                if (o.Plate != null) o.Plate.SetActive(on);
                if (o.Border != null) o.Border.SetActive(on);
                if (o.PlateImage != null) o.PlateImage.color = plateColor;
            }
        }
    }
}
