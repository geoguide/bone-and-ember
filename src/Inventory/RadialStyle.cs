using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valheim.UI;

namespace BoneAndEmber
{
    // Slice 8 of docs/design/003-inventory.md: restyle the vanilla radial, do
    // not rebuild it. See 003-notes.md "Radial" for the research this rests on.
    //
    // Two things shape the whole approach:
    //
    // 1. Wedges are drawn by a shader, not by sprites. RadialMenuElement owns a
    //    per-instance material and every state (_UnselectedColor, _Hovering,
    //    _Segments, _Offset, ...) is a material property. There is no wedge
    //    GameObject to recolor and no arc sprite to outline, so anything that
    //    has to follow the arc can only be done through a property the shader
    //    already has. We probe with Material.HasProperty and log what is really
    //    there rather than guessing names.
    // 2. Elements are destroyed and rebuilt on every open (ConstructRadial
    //    calls ClearElements), so the marker-component pattern from the slots
    //    does not apply: there is nothing to mark. We restyle from a postfix on
    //    ConstructRadial, which runs once per open with the finished list.
    internal static class RadialStyle
    {
        // Candidate names for the resting plate and the hovered state. The
        // shader is Iron Gate's, so we ask the material which of these it
        // actually has instead of assuming.
        private static readonly string[] PlateProps = { "_UnselectedColor" };
        private static readonly string[] HoverProps = { "_HoveredColor", "_HoverColor", "_SelectedColor", "_HighlightColor" };

        private const float CenterNameSize = 22f;
        private const float CenterHintSize = 16f;
        private const float CenterHintAlpha = 0.6f;
        private const float CenterDiscScale = 0.6f;
        private const float CenterDiscAlpha = 0.85f;
        private const float CenterCountSize = 14f;
        private const float CenterFoodSize = 14f;
        private const float SeparatorAlpha = 0.25f;
        private const float IconTintAlpha = 1f;

        // Slice 9: the hub stack. Vanilla never positions m_icon/m_title/
        // m_subTitle relative to each other, it only lays out one line worth
        // of subtitle - once a food line gets appended as a second line, that
        // baked layout is what overlaps. We take full control of the stack
        // instead of only recoloring text.
        private const float HubIconSize = 64f;
        private const float HubIconGap = 8f;
        private const float HubTextGap = 2f;

        private static bool _loggedMaterial;
        private static bool _loggedRoot;
        private static string _resolvedHoverProp;
        private static bool _hoverPropResolved;

        internal static void StyleRadial(RadialBase radial, List<RadialMenuElement> elements)
        {
            if (!Plugin.RadialEnabled.Value || radial == null || elements == null) return;

            ApplyScale(radial);

            int styled = 0;
            for (int i = 0; i < elements.Count; i++)
            {
                RadialMenuElement element = elements[i];
                if (element == null) continue;
                if (StyleElement(element, i == 0)) styled++;
            }

            string group = "(none)";
            if (radial.CurrentConfig != null)
            {
                group = radial.CurrentConfig.LocalizedName ?? radial.CurrentConfig.GetType().Name;
            }
            Plugin.Log.LogInfo("radial: styled " + styled + " wedges, group=" + group);
        }

        // Scaling the root scales distances, not angles. Wedge choice comes
        // from the angle between the center and either the stick vector or
        // (pointerPosition - InfoPosition), and a uniform scale about the
        // center leaves every one of those angles unchanged, so selection
        // tracks identically at 0.8. Individual elements are not scaled:
        // SetRadialLayout resets element localScale to one on every build.
        internal static void ApplyScale(RadialBase radial)
        {
            float scale = Plugin.RadialScale != null ? Plugin.RadialScale.Value : 1f;
            RectTransform root = radial.transform as RectTransform;
            if (root == null) return;

            if (!Mathf.Approximately(root.localScale.x, scale)) root.localScale = Vector3.one * scale;

            if (!_loggedRoot)
            {
                _loggedRoot = true;
                Plugin.Log.LogInfo("radial: root " + HudPath.Of(root) +
                    " scale=" + scale.ToString("F2") +
                    " rect=" + root.rect.size.ToString("F1") +
                    " pivot=" + root.pivot.ToString("F2") +
                    " anchoredPos=" + root.anchoredPosition.ToString("F1"));
            }
        }

        private static bool StyleElement(RadialMenuElement element, bool logThisOne)
        {
            Material mat = element.BackgroundMaterial;
            if (mat == null) return false;

            if (logThisOne) LogMaterialOnce(element, mat);

            // The C# property setters clamp alpha to 0.8; SetColor does not, so
            // the plate can actually be opaque.
            for (int i = 0; i < PlateProps.Length; i++)
            {
                if (mat.HasProperty(PlateProps[i])) mat.SetColor(PlateProps[i], Palette.PanelPlate);
            }

            string hoverProp = ResolveHoverProp(mat);
            if (hoverProp != null) mat.SetColor(hoverProp, Palette.EmberHealth);

            // Bone icons so they read against both the dark plate and the
            // ember hover. Never touch the sprite, only the tint.
            if (element.Icon != null)
            {
                element.Icon.color = new Color(Palette.Bone.r, Palette.Bone.g, Palette.Bone.b, IconTintAlpha);
            }

            return true;
        }

        private static string ResolveHoverProp(Material mat)
        {
            if (_hoverPropResolved) return _resolvedHoverProp;
            _hoverPropResolved = true;

            for (int i = 0; i < HoverProps.Length; i++)
            {
                if (mat.HasProperty(HoverProps[i]))
                {
                    _resolvedHoverProp = HoverProps[i];
                    Plugin.Log.LogInfo("radial: hover color property is \"" + HoverProps[i] + "\", tinting it ember.");
                    return _resolvedHoverProp;
                }
            }

            Plugin.Log.LogWarning("radial: the wedge shader has no hover color property among " +
                string.Join(", ", HoverProps) + ". Hover stays vanilla's own look; " +
                "an ember hover fill and an outline that overshoots the arc would need a shader of our own. " +
                "See the property dump above for what this shader does expose.");
            return null;
        }

        // One dump, first open: the shader's name and every property on it, so
        // the separator and outline questions get answered from fact rather
        // than from guesses about property names.
        private static void LogMaterialOnce(RadialMenuElement element, Material mat)
        {
            if (_loggedMaterial) return;
            _loggedMaterial = true;

            Plugin.Log.LogInfo("radial: first element at " + HudPath.Of(element.transform) +
                ", shader=" + (mat.shader != null ? mat.shader.name : "(none)"));

            if (mat.shader == null) return;
            int count = mat.shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                string name = mat.shader.GetPropertyName(i);
                Plugin.Log.LogInfo("radial:   property " + name + " (" + mat.shader.GetPropertyType(i) + ")");
            }

            DumpHierarchy(element.transform, 0);
        }

        private static void DumpHierarchy(Transform t, int depth)
        {
            string indent = new string(' ', depth * 2);
            Component[] comps = t.GetComponents<Component>();
            string[] names = new string[comps.Length];
            for (int i = 0; i < comps.Length; i++)
            {
                names[i] = comps[i] != null ? comps[i].GetType().Name : "(missing)";
            }
            Plugin.Log.LogInfo("radial:   " + indent + t.name + " [" + string.Join(", ", names) + "]");
            for (int i = 0; i < t.childCount; i++) DumpHierarchy(t.GetChild(i), depth + 1);
        }

        // The center panel. For an ItemElement vanilla deliberately blanks the
        // title and shows the name through its inventory-info panel instead
        // (003-notes.md), so writing the name here adds a line rather than
        // fighting one vanilla draws.
        internal static void StyleCenter(ElementInfo info, RadialMenuElement element, TMP_Text title, TMP_Text subTitle, Image icon)
        {
            if (!Plugin.RadialEnabled.Value || info == null) return;

            StyleCenterDisc(info);

            // An item name is the thing you are choosing, so it gets the full
            // weight. Group names and "Back" are navigation hints and step
            // back to match.
            bool isItem = element is ItemElement || element is ThrowElement;
            if (title != null)
            {
                if (isItem)
                {
                    title.fontSize = CenterNameSize;
                    title.fontStyle = FontStyles.Bold;
                    title.color = Palette.Bone;
                    if (element != null && string.IsNullOrEmpty(title.text)) title.text = element.Name;
                }
                else
                {
                    title.fontSize = CenterHintSize;
                    title.fontStyle = FontStyles.Normal;
                    title.color = new Color(Palette.Bone.r, Palette.Bone.g, Palette.Bone.b, CenterHintAlpha);
                }
                HudFont.Apply(title);
            }

            if (subTitle != null)
            {
                subTitle.fontSize = CenterCountSize;
                subTitle.color = new Color(Palette.Bone.r, Palette.Bone.g, Palette.Bone.b, 0.7f);
                HudFont.Apply(subTitle);

                string food = FoodLine(element);
                if (food != null)
                {
                    // One extra line under the count, in the same ember the HUD
                    // uses for the food-derived share of health.
                    string tint = ColorUtility.ToHtmlStringRGB(Plugin.FoodHealthColor != null
                        ? Plugin.FoodHealthColor.Value
                        : Palette.FoodEmberDefault);
                    subTitle.text = string.IsNullOrEmpty(subTitle.text)
                        ? "<color=#" + tint + "><size=" + CenterFoodSize + ">" + food + "</size></color>"
                        : subTitle.text + "\n<color=#" + tint + "><size=" + CenterFoodSize + ">" + food + "</size></color>";
                }
            }

            if (icon != null) icon.rectTransform.sizeDelta = new Vector2(HubIconSize, HubIconSize);
            LayoutHub(icon, title, subTitle, isItem && icon != null && icon.gameObject.activeSelf);
        }

        // Stacks icon (if shown), title, then subtitle top to bottom, centered
        // as one block on the hub's own center. Heights come from measured
        // text, the same way LoudMoments.Layout() sizes its slab, since
        // subtitle can be one line (count) or two (count + food).
        private static void LayoutHub(Image icon, TMP_Text title, TMP_Text subTitle, bool showIcon)
        {
            if (title == null) return;

            RectTransform iconRect = icon != null ? icon.rectTransform : null;
            RectTransform titleRect = title.rectTransform;
            RectTransform subRect = subTitle != null ? subTitle.rectTransform : null;

            if (showIcon && iconRect != null) CenterTop(iconRect);
            CenterTop(titleRect);
            if (subRect != null) CenterTop(subRect);

            float iconBlock = showIcon ? HubIconSize + HubIconGap : 0f;
            Vector2 titleSize = title.GetPreferredValues(string.IsNullOrEmpty(title.text) ? " " : title.text);
            float titleHeight = Mathf.Max(titleSize.y, 1f);

            bool hasSub = subRect != null && !string.IsNullOrEmpty(subTitle.text);
            float subHeight = 0f;
            if (hasSub)
            {
                Vector2 subSize = subTitle.GetPreferredValues(subTitle.text);
                subHeight = subSize.y;
            }

            float totalHeight = iconBlock + titleHeight + (hasSub ? HubTextGap + subHeight : 0f);
            float cursorY = totalHeight * 0.5f;

            if (showIcon && iconRect != null)
            {
                iconRect.anchoredPosition = new Vector2(0f, cursorY);
                cursorY -= iconBlock;
            }

            titleRect.anchoredPosition = new Vector2(0f, cursorY);
            cursorY -= titleHeight;

            if (hasSub)
            {
                cursorY -= HubTextGap;
                subRect.anchoredPosition = new Vector2(0f, cursorY);
            }
        }

        // Centered on X, top-pivoted on Y, so anchoredPosition.y is the
        // block's own top edge and stacking is a simple running subtraction.
        private static void CenterTop(RectTransform rt)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 1f);
        }

        // Vanilla's own armor/weight/tooltip panel beside the hub. The hub
        // already carries the name, count and food line, so this is
        // redundant once it's on - hidden with alpha rather than SetActive
        // so we never fight vanilla's own activeSelf toggling of it.
        internal static void StyleSidePanel(RadialInventoryInfo inventoryInfo)
        {
            if (inventoryInfo == null) return;

            CanvasGroup group = inventoryInfo.GetComponent<CanvasGroup>();
            if (group == null) group = inventoryInfo.gameObject.AddComponent<CanvasGroup>();

            bool hide = Plugin.RadialEnabled.Value && Plugin.RadialHideVanillaSidePanel.Value;
            float alpha = hide ? 0f : 1f;
            if (Mathf.Approximately(group.alpha, alpha)) return;

            group.alpha = alpha;
            group.blocksRaycasts = alpha > 0f;
            group.interactable = alpha > 0f;
        }

        // A smaller, more transparent center disc so the world reads through
        // it. Only the background is scaled, not the whole ElementInfo, so the
        // text keeps the sizes set above.
        private static void StyleCenterDisc(ElementInfo info)
        {
            Image bg = info.BackgroundImage;
            if (bg == null) return;

            RectTransform rt = bg.rectTransform;
            if (rt != null) rt.localScale = Vector3.one * CenterDiscScale;

            Color c = Palette.PanelPlate;
            bg.color = new Color(c.r, c.g, c.b, CenterDiscAlpha);
        }

        // "+7 HP  +20 STAM", only for food, straight off m_shared like the
        // detail card does.
        private static string FoodLine(RadialMenuElement element)
        {
            ItemElement item = element as ItemElement;
            if (item == null || item.m_data == null) return null;

            ItemDrop.ItemData data = item.m_data;
            if (data.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Consumable) return null;

            string line = "";
            if (data.m_shared.m_food > 0f) line += "+" + data.m_shared.m_food.ToString("0") + " HP";
            if (data.m_shared.m_foodStamina > 0f)
            {
                line += (line.Length > 0 ? "  " : "") + "+" + data.m_shared.m_foodStamina.ToString("0") + " STAM";
            }
            if (data.m_shared.m_foodEitr > 0f)
            {
                line += (line.Length > 0 ? "  " : "") + "+" + data.m_shared.m_foodEitr.ToString("0") + " EITR";
            }
            return line.Length > 0 ? line : null;
        }
    }
}
