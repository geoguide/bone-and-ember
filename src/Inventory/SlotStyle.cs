using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoneAndEmber
{
    // Slice 2 of docs/design/003-inventory.md: restyle InventoryGrid's own slot
    // prefab in place rather than building new UI. One of these gets attached
    // to an InventoryElement's GameObject the first time
    // InventorySlotStylePatch sees it; its presence is the "already built"
    // marker. It has to be, since the element's whole GameObject (and
    // everything on it, us included) is destroyed and recreated whenever
    // InventoryGrid.UpdateGui rebuilds the grid on a resize, which happens
    // every time a different-sized chest is opened. See 003-notes.md, "How
    // slots get built and updated".
    //
    // Geometry changes here only ever touch child RectTransforms, never the
    // element's own transform: InventoryGrid's hit-testing (GetHoveredElement,
    // GetItem) reads that rect directly, and resizing it would break clicking
    // and dragging.
    //
    // Vanilla's own UpdateGui writes m_icon.color (the drag-tint), the
    // durability bar's active state, value and color, m_amount's text and
    // enabled state, and the tooltip strings every frame, always before our
    // postfix runs in the same frame. We overwrite m_amount's text ourselves
    // (stack count only, no "/max") and fight the durability color every
    // frame in Refresh(); everything else Build() sets is geometry vanilla
    // never touches, so it stays put once set.
    internal class SlotStyle : MonoBehaviour
    {
        private const float DurabilityBarHeight = 3f;
        private const float DurabilityInset = 3f;
        private const float IconScale = 1.15f;
        private const float AmountInset = 3f;
        private const float HoverLighten = 0.12f;
        private const float PressLighten = 0.2f;
        private const float BindingInset = 3f;
        private const float BindingFontScale = 0.8f;
        private const float BindingAlpha = 0.7f;
        private const float EquippedTagFraction = 0.32f; // fraction of the slot each side
        private const float BorderAlpha = 0.25f;
        private const float BorderMatchAlpha = 0.8f;
        private const float DimAlpha = 0.2f;
        private const int BorderThickness = 1;
        private const int BorderMatchThickness = 2;

        // Slice 9: filter dim/match fade to 120ms instead of snapping, and a
        // press state on top of whatever the border is already showing. Bone
        // at 60% has no existing Palette token (nothing else uses it yet), so
        // it's a local constant rather than a promoted one.
        private const float FilterTweenMs = 120f;
        private const float PressTweenMs = 80f;
        private const float PressBorderAlpha = 0.6f;

        private const int BorderHoverThickness = 2;
        private const float HoverPlateLift = 0.16f;   // how much lighter the plate goes

        // Drop feedback on the one destination slot. Stronger alpha than the
        // old spray-everything version could use, since only one slot is lit
        // at a time and it needs to read at a glance mid-drag.
        private static readonly Color DropTargetColor = new Color(Palette.Gold.r, Palette.Gold.g, Palette.Gold.b, 0.45f);
        private static readonly Color DropSwapColor = new Color(Palette.Bone.r, Palette.Bone.g, Palette.Bone.b, 0.38f);
        private static readonly Color DropBlockedColor = new Color(Palette.EmberCritical.r, Palette.EmberCritical.g, Palette.EmberCritical.b, 0.38f);

        private InventoryElement _element;
        private Image _plate;
        private GuiBar _durability;
        private TMP_Text _amount;
        private CanvasGroup _canvasGroup;
        private Image _border;

        private Tween _dimAlpha;
        private Tween _matchAlpha;
        private Tween _pressAlpha;
        private bool _filterTweenInit;


        private Image _equippedTag;
        private RectTransform _equippedRect;
        private Sprite _equippedOriginalSprite;
        private Color _equippedOriginalColor;
        private Vector2 _equippedOriginalAnchorMin;
        private Vector2 _equippedOriginalAnchorMax;
        private Vector2 _equippedOriginalOffsetMin;
        private Vector2 _equippedOriginalOffsetMax;
        private Vector2 _equippedOriginalPivot;

        internal void Build(InventoryElement element)
        {
            RectTransform elementRect = element.transform as RectTransform;
            if (elementRect == null) return;

            _element = element;

            Image plate = BuildPlate(elementRect);
            _plate = plate;
            _border = BuildBorder(elementRect);
            RetargetButton(element, plate);
            ResizeIcon(element);
            RepositionAmount(element);
            RepositionDurability(element);
            RestyleBinding(element);
            CacheEquippedTag(element);

            _canvasGroup = element.GetComponent<CanvasGroup>();
            if (_canvasGroup == null) _canvasGroup = element.gameObject.AddComponent<CanvasGroup>();

            SlotHover hover = element.GetComponent<SlotHover>();
            if (hover == null) hover = element.gameObject.AddComponent<SlotHover>();
            hover.Element = element;
        }

        // The real slot surface: opaque flat plate in the 001 palette, first
        // sibling so the border and everything vanilla already had (icon,
        // amount, durability) draw on top of it. Raycastable, since
        // RetargetButton makes this the thing clicks and drags actually land
        // on once vanilla's own background stops being it.
        private static Image BuildPlate(RectTransform elementRect)
        {
            GameObject go = new GameObject("BoneAndEmber_Plate", typeof(RectTransform), typeof(Image));
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(elementRect, false);
            Normalize(rt);
            Stretch(rt);
            rt.SetSiblingIndex(0);

            Image img = go.GetComponent<Image>();
            img.sprite = null;
            img.type = Image.Type.Simple;
            img.raycastTarget = true;
            img.color = PlateColor();
            return img;
        }

        private static Image BuildBorder(RectTransform elementRect)
        {
            GameObject go = new GameObject("BoneAndEmber_Border", typeof(RectTransform), typeof(Image));
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(elementRect, false);
            Normalize(rt);
            Stretch(rt);
            rt.SetSiblingIndex(1);

            Image img = go.GetComponent<Image>();
            img.sprite = SlotBorderSprite.Get();
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;
            img.sprite = SlotBorderSprite.Get(BorderThickness);
            img.color = BorderColor(BorderAlpha);
            return img;
        }

        private static Color BorderColor(float alpha)
        {
            return new Color(Palette.Bone.r, Palette.Bone.g, Palette.Bone.b, alpha);
        }

        // Turns out sibling order alone wasn't the fix: vanilla's own
        // background (item_background, opaque white-tinted) sits on the
        // element's own root, and our plate at 88% alpha over an opaque
        // sprite just read as a slightly darker version of vanilla instead of
        // replacing it. So replace it for real: disable vanilla's own image
        // and hand the Button its own targetGraphic instead, with a
        // ColorBlock built around our plate's own color so hover/press still
        // visibly does something. Unity's event system resolves a click on a
        // child (the plate) up to the nearest ancestor Selectable/handler
        // (ExecuteEvents.GetEventHandler walks up), the same way clicking an
        // icon or label inside any ordinary Button already works, so making a
        // child the raycast target here is not a new risk.
        private static void RetargetButton(InventoryElement element, Image plate)
        {
            Button button = element.m_button;
            if (button == null || plate == null) return;

            Image vanillaBg = button.image;
            if (vanillaBg != null && vanillaBg != plate)
            {
                vanillaBg.enabled = false;
            }

            button.targetGraphic = plate;

            // Every state is the resting colour: the hover and press look are
            // driven in RefreshFilterDim instead, where the border is reachable
            // too. Leaving the ColorBlock live would just fight those writes.
            Color baseColor = PlateColor();
            ColorBlock colors = button.colors;
            colors.normalColor = baseColor;
            colors.highlightedColor = baseColor;
            colors.pressedColor = baseColor;
            colors.selectedColor = baseColor;
            button.colors = colors;
        }

        private static Color Lighten(Color c, float amount)
        {
            return new Color(
                Mathf.Clamp01(c.r + amount),
                Mathf.Clamp01(c.g + amount),
                Mathf.Clamp01(c.b + amount),
                c.a);
        }

        // A uniform localScale grows the icon from its own pivot without
        // touching anchors, offsets, or position, so it doesn't matter what
        // vanilla's original icon rect was or where its pivot sits (as long
        // as that pivot is centered, which an icon meant to sit in the middle
        // of a slot should already be). The previous approach stretched the
        // icon to a fixed inset of the slot instead, which on this prefab's
        // real proportions came out smaller than vanilla's own icon, not
        // bigger, hence "the icons aren't visibly bigger."
        private static void ResizeIcon(InventoryElement element)
        {
            if (element.m_icon == null) return;
            RectTransform rt = element.m_icon.rectTransform;
            rt.localScale = new Vector3(IconScale, IconScale, 1f);
        }

        // Bottom-right, stack count only, no "/max" (matches the round 1
        // mockup; max belongs in the slice 4 detail panel instead). Vanilla
        // sets m_amount.text itself every frame as "{stack}/{maxStack}"
        // whenever maxStackSize > 1, so the count-only format has to be
        // reapplied every frame too, in Refresh(), not just here at Build().
        private void RepositionAmount(InventoryElement element)
        {
            _amount = element.m_amount;
            if (element.m_amount == null) return;
            RectTransform rt = element.m_amount.rectTransform;
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-AmountInset, AmountInset);
            element.m_amount.alignment = TextAlignmentOptions.BottomRight;
            HudFont.Apply(element.m_amount);
        }

        // Forces the durability GuiBar's fill into the single-left-anchor,
        // sizeDelta-driven scheme GuiBar.SetWidth/SetBar assumes, the same
        // pattern ClonedBar.StretchFillHeight uses for the HUD bars, then tells
        // GuiBar the new full width directly with SetWidth instead of trusting
        // whatever it cached from the prefab's original (unknown, probably
        // much smaller) size.
        private void RepositionDurability(InventoryElement element)
        {
            _durability = element.m_durability;
            if (_durability == null) return;

            RectTransform container = _durability.transform as RectTransform;
            if (container == null) return;

            container.anchorMin = new Vector2(0f, 0f);
            container.anchorMax = new Vector2(1f, 0f);
            container.pivot = new Vector2(0.5f, 0f);
            container.sizeDelta = new Vector2(-DurabilityInset * 2f, DurabilityBarHeight);
            container.anchoredPosition = new Vector2(0f, DurabilityInset);

            RectTransform fill = _durability.m_bar;
            if (fill != null)
            {
                fill.anchorMin = new Vector2(0f, 0f);
                fill.anchorMax = new Vector2(0f, 1f);
                fill.pivot = new Vector2(0f, 0.5f);
                fill.offsetMin = Vector2.zero;
                fill.offsetMax = Vector2.zero;
            }

            _durability.SetWidth(container.rect.width);
        }

        // Top-left, small and quiet. "binding" isn't an InventoryElement
        // field (vanilla itself finds it the same way, by name, in
        // InventoryGrid.UpdateGui's own element-creation loop), and its text
        // and enabled state are only ever set once, at that same creation
        // time, not every frame, so a one-time restyle here is safe.
        private static void RestyleBinding(InventoryElement element)
        {
            Transform bindingT = element.transform.Find("binding");
            if (bindingT == null) return;
            TMP_Text binding = bindingT.GetComponent<TMP_Text>();
            if (binding == null) return;

            RectTransform rt = binding.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(BindingInset, -BindingInset);

            binding.alignment = TextAlignmentOptions.TopLeft;
            binding.enableAutoSizing = false;
            binding.fontSize *= BindingFontScale;
            binding.color = new Color(Palette.Bone.r, Palette.Bone.g, Palette.Bone.b, BindingAlpha);
            HudFont.Apply(binding);
        }

        // Caches m_equiped's original look once (sprite, color, and the
        // geometry, since a corner tag needs different anchors than whatever
        // vanilla's own equipped marker used) so EquippedTags can toggle
        // between our ember corner triangle and vanilla's original live,
        // every frame, in Refresh(), the same way HudEnabled and friends
        // toggle live elsewhere in this mod. Vanilla still owns m_equiped's
        // .enabled state every frame (on only when the item is actually
        // equipped) and we never touch that, only what it looks like when on.
        private void CacheEquippedTag(InventoryElement element)
        {
            _equippedTag = element.m_equiped;
            if (_equippedTag == null) return;

            _equippedRect = _equippedTag.rectTransform;
            _equippedOriginalSprite = _equippedTag.sprite;
            _equippedOriginalColor = _equippedTag.color;
            _equippedOriginalAnchorMin = _equippedRect.anchorMin;
            _equippedOriginalAnchorMax = _equippedRect.anchorMax;
            _equippedOriginalOffsetMin = _equippedRect.offsetMin;
            _equippedOriginalOffsetMax = _equippedRect.offsetMax;
            _equippedOriginalPivot = _equippedRect.pivot;

            ApplyEquippedStyle();
        }

        private void ApplyEquippedStyle()
        {
            if (_equippedTag == null || _equippedRect == null) return;

            if (Plugin.EquippedTags.Value)
            {
                _equippedRect.anchorMin = new Vector2(1f - EquippedTagFraction, 1f - EquippedTagFraction);
                _equippedRect.anchorMax = Vector2.one;
                _equippedRect.offsetMin = Vector2.zero;
                _equippedRect.offsetMax = Vector2.zero;
                _equippedTag.sprite = EquippedTagSprite.Get();
                _equippedTag.type = Image.Type.Simple;
                _equippedTag.color = Palette.EmberHealth;
            }
            else
            {
                _equippedRect.anchorMin = _equippedOriginalAnchorMin;
                _equippedRect.anchorMax = _equippedOriginalAnchorMax;
                _equippedRect.offsetMin = _equippedOriginalOffsetMin;
                _equippedRect.offsetMax = _equippedOriginalOffsetMax;
                _equippedRect.pivot = _equippedOriginalPivot;
                _equippedTag.sprite = _equippedOriginalSprite;
                _equippedTag.color = _equippedOriginalColor;
            }
        }

        // Per-frame, called for every element every frame regardless of
        // whether we just built it: vanilla's own UpdateGui runs first and
        // resets both the durability color (ResetColor(), or the
        // durability-at-zero blink, which we leave alone, it's a real "about
        // to break" signal) and the amount text (back to "{stack}/{maxStack}")
        // before our postfix gets a turn, so both have to be reapplied here,
        // every frame, not just once at Build(). ApplyEquippedStyle is cheap
        // (a handful of property sets) and re-checking EquippedTags.Value
        // every frame, rather than only at Build(), is what makes that toggle
        // live instead of needing the inventory reopened.
        internal void Refresh(InventoryElement element, ItemDrop.ItemData item, ItemDrop.ItemData dragItem,
            bool isDropTarget, bool dragBlocked, bool isHovered)
        {
            RefreshDurabilityColor(item);
            RefreshAmountText(item);
            ApplyEquippedStyle();
            RefreshDropTarget(element, item, dragItem, isDropTarget, dragBlocked);
            RefreshFilterDim(item, isHovered);
        }

        // Filtered-out slots fade rather than move: InventoryGrid hit-tests
        // against the element's own RectTransform, so its size and position
        // have to stay exactly where vanilla put them. A CanvasGroup dims the
        // whole slot in one go and is a flag neither vanilla nor Minimal UI
        // reads or writes, so nothing fights us over it (the same reasoning
        // AlwaysOnHud uses to hide vanilla's bars).
        private void RefreshFilterDim(ItemDrop.ItemData item, bool isHovered)
        {
            bool filtering = Plugin.FilterRow.Value && FilterRow.Active != ItemFilter.All;
            bool matches = filtering && item != null && FilterRow.Matches(item, FilterRow.Active);
            bool dimmed = filtering && item != null && !matches;
            bool pressed = _element != null && SlotHover.Pressed == _element;
            bool motion = Plugin.MotionEnabled.Value;

            float dimTarget = dimmed ? DimAlpha : 1f;
            float matchTarget = matches ? 1f : 0f;
            float pressTarget = pressed ? 1f : 0f;

            if (!_filterTweenInit)
            {
                _filterTweenInit = true;
                _dimAlpha.Snap(dimTarget);
                _matchAlpha.Snap(matchTarget);
                _pressAlpha.Snap(pressTarget);
            }
            else if (motion)
            {
                _dimAlpha.Retarget(dimTarget, FilterTweenMs);
                _matchAlpha.Retarget(matchTarget, FilterTweenMs);
                _pressAlpha.Retarget(pressTarget, PressTweenMs);
            }
            else
            {
                _dimAlpha.Snap(dimTarget);
                _matchAlpha.Snap(matchTarget);
                _pressAlpha.Snap(pressTarget);
            }

            float dt = Time.deltaTime;
            if (_canvasGroup != null) _canvasGroup.alpha = _dimAlpha.Update(dt);

            float matchAmount = _matchAlpha.Update(dt);
            float pressAmount = _pressAlpha.Update(dt);

            // Hover wins over the filter's own edge: it's the thing you're
            // pointing at right now. Sprite/thickness swap stays instant -
            // crossfading a 1px-vs-2px sprite doesn't read as motion, only the
            // color does.
            if (_border != null)
            {
                Color restingColor;
                int thickness;
                if (isHovered)
                {
                    restingColor = Palette.Bone;
                    thickness = BorderHoverThickness;
                }
                else
                {
                    Color goldColor = new Color(Palette.Gold.r, Palette.Gold.g, Palette.Gold.b, BorderMatchAlpha);
                    restingColor = Color.Lerp(BorderColor(BorderAlpha), goldColor, matchAmount);
                    thickness = matches ? BorderMatchThickness : BorderThickness;
                }

                _border.sprite = SlotBorderSprite.Get(thickness);
                Color pressColor = new Color(Palette.Bone.r, Palette.Bone.g, Palette.Bone.b, PressBorderAlpha);
                _border.color = Color.Lerp(restingColor, pressColor, pressAmount);
            }

            if (_plate != null)
            {
                Color plate = PlateColor();
                _plate.color = isHovered ? Lighten(plate, HoverPlateLift) : plate;
            }
        }

        // Only the slot the item would actually land in, not every slot that
        // would accept one: lighting up all of them looked nice and told you
        // nothing you didn't already know.
        //
        // InventoryGrid.DropItem takes the destination straight from the
        // hovered slot's grid position, so the target square is simply the one
        // under the cursor (or under the gamepad cursor). What happens there
        // has three outcomes, and the color says which:
        //   gold  - lands here, either an empty slot or stacking onto the same item
        //   bone  - swaps, the two items trade places
        //   ember - refused, so releasing here does nothing
        // The dragged item's own slot stays unlit; releasing there is a no-op
        // and vanilla returns early on it.
        private void RefreshDropTarget(InventoryElement element, ItemDrop.ItemData item, ItemDrop.ItemData dragItem,
            bool isDropTarget, bool dragBlocked)
        {
            if (element == null || element.m_dropFocus == null) return;

            if (!Plugin.DropTargetHighlight.Value || dragItem == null || !isDropTarget || item == dragItem)
            {
                element.m_dropFocus.color = Color.clear;
                return;
            }

            Color color;
            if (dragBlocked)
            {
                color = DropBlockedColor;
            }
            else if (item != null && WouldSwap(item, dragItem))
            {
                color = DropSwapColor;
            }
            else
            {
                color = DropTargetColor;
            }

            element.m_dropFocus.color = color;
        }

        // The swap branch of InventoryGrid.DropItem: an occupied slot holding
        // something that can't merge with what's on the cursor. Anything else
        // that lands (empty slot, or a matching stack with room) is a place,
        // not a swap. The stack-size check vanilla makes here compares the
        // whole dragged stack, which is what a plain drag always is; a split
        // stack drops through to the merge path instead.
        private static bool WouldSwap(ItemDrop.ItemData slotItem, ItemDrop.ItemData dragItem)
        {
            if (slotItem.m_shared.m_name != dragItem.m_shared.m_name) return true;
            if (dragItem.m_shared.m_maxQuality > 1 && slotItem.m_quality != dragItem.m_quality) return true;
            return slotItem.m_shared.m_maxStackSize == 1;
        }

        private void RefreshDurabilityColor(ItemDrop.ItemData item)
        {
            if (_durability == null) return;
            if (item == null || !item.m_shared.m_useDurability) return;
            if (item.m_durability <= 0f) return;

            float pct = item.GetDurabilityPercentage();
            _durability.SetColor(pct < 0.2f ? Palette.EmberCritical : Palette.Gold);
        }

        private void RefreshAmountText(ItemDrop.ItemData item)
        {
            if (_amount == null) return;
            // Vanilla already disabled m_amount when maxStackSize <= 1; leave
            // that alone rather than fight it for a slot with no count to show.
            if (item == null || item.m_shared.m_maxStackSize <= 1) return;

            _amount.text = item.m_stack.ToString();
        }

        // A fresh GameObject is already identity, but ClonedBar's own comment
        // ("The source prefab carries baked rotation/scale that's Unity scene
        // data, invisible in the decompiled C#") is exactly the kind of thing
        // that bit slice 3 of the HUD once already, and normalizing here costs
        // nothing. Doesn't rule out an ancestor's scale, only our own object's.
        private static void Normalize(RectTransform rt)
        {
            rt.localRotation = Quaternion.identity;
            rt.localScale = Vector3.one;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        // The slot surface: one step lighter than the panel it sits on, so a
        // slot reads as a raised tile rather than a hole. Opaque by default;
        // SlotPlateAlpha only exists to let the vanilla hover tint bleed
        // through if Geo wants it.
        internal static Color PlateColor()
        {
            Color c = Palette.SlotPlate;
            float alpha = Plugin.SlotPlateAlpha != null ? Plugin.SlotPlateAlpha.Value : 1f;
            return new Color(c.r, c.g, c.b, alpha);
        }
    }
}
