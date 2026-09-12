using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoneAndEmber
{
    // Slice 4 of docs/design/003-inventory.md: the Persona-style detail card.
    // A MonoBehaviour attached to InventoryGui's own GameObject in
    // DetailCardPatch, mirroring how AlwaysOnHud attaches to Hud - it lives
    // for the whole game session, InventoryGui is built once, not per open.
    //
    // Facts here come straight off ItemData/HitData, the same sources
    // ItemDrop.ItemData.GetTooltip() itself reads (003-notes.md, "How the
    // tooltip is assembled"), never by parsing GetTooltip()'s own string.
    // No deltas against equipped gear yet, that's slice 5: every stat here is
    // just this item's own numbers.
    internal class DetailCard : MonoBehaviour
    {
        private const float CardWidth = 260f;
        private const float CardGap = 16f;
        private const float Padding = 14f;       // horizontal
        private const float VerticalPadding = 12f; // 12 top + 12 bottom = the 24 the card adds
        private const float MinCardHeight = 120f;
        private const float Spacing = 8f;
        private const float WidgetClearMargin = 12f;

        // Slice 9: fade instead of SetActive-snap, and a quick crossfade when
        // the hovered item changes while the card is already up.
        private const float FadeMs = 80f;
        private const float CardPlateAlpha = 0.97f;
        private const float CrossfadeFloor = 0.4f;

        internal static DetailCard Instance;

        private InventoryGui _gui;
        private RectTransform _root;
        private RectTransform _content;
        private CanvasGroup _group;
        private TMP_Text _kicker;
        private TMP_Text _name;
        private TMP_Text _statBlock;

        private Tween _alpha;
        private bool _alphaInit;
        private bool _visible;

        private void Awake()
        {
            Instance = this;
            _gui = GetComponent<InventoryGui>();
            Build();
        }

        // The one session-long per-frame driver for inventory motion: this
        // component already lives for the whole game session on InventoryGui
        // (like AlwaysOnHud does on Hud), so it also ticks FilterRow's chip
        // underline slide rather than spinning up a second MonoBehaviour for
        // two floats.
        private void Update()
        {
            if (_root == null) return;

            float dt = Time.deltaTime;
            bool motion = Plugin.MotionEnabled.Value;

            if (!_alphaInit)
            {
                _alphaInit = true;
                _alpha.Snap(_visible ? 1f : 0f);
            }
            else if (motion)
            {
                _alpha.Retarget(_visible ? 1f : 0f, FadeMs);
            }
            else
            {
                _alpha.Snap(_visible ? 1f : 0f);
            }

            float value = _alpha.Update(dt);
            if (_group != null) _group.alpha = value;
            if (!_visible && _alpha.Done && value <= 0f) _root.gameObject.SetActive(false);

            FilterRow.Tick(dt);
        }

        private void Build()
        {
            RectTransform playerPanel = _gui != null ? _gui.m_player : null;
            if (playerPanel == null)
            {
                Plugin.Log.LogWarning("detail card: InventoryGui.m_player is null, card not built.");
                return;
            }

            GameObject go = new GameObject("BoneAndEmber_DetailCard", typeof(RectTransform), typeof(Image));
            _root = (RectTransform)go.transform;
            _root.SetParent(playerPanel, false);
            _root.localRotation = Quaternion.identity;
            _root.localScale = Vector3.one;
            // Anchored to the player panel's own right edge and stretched to
            // its full height, so the card moves and resizes with m_player
            // automatically if that panel ever does, without needing to know
            // m_player's own anchor scheme relative to the rest of the screen.
            // Pinned to the panel's top right corner and grown downward, so
            // the card's top edge never moves as its content changes height.
            _root.anchorMin = new Vector2(1f, 1f);
            _root.anchorMax = new Vector2(1f, 1f);
            _root.pivot = new Vector2(0f, 1f);
            _root.anchoredPosition = new Vector2(ComputeGap(playerPanel), 0f);
            _root.sizeDelta = new Vector2(CardWidth, MinCardHeight);

            Image bg = go.GetComponent<Image>();
            bg.sprite = ChipSprite.Get(); // clipped top-right corner, already built for slice 001 status chips
            bg.type = Image.Type.Sliced;
            // Nearly opaque, unlike the status chips that share Palette.Plate:
            // the card carries a block of small stat text over whatever the
            // world is doing behind the panel, and at the chips' 0.88 the
            // numbers picked up the scenery.
            bg.color = new Color(Palette.Plate.r, Palette.Plate.g, Palette.Plate.b, CardPlateAlpha);
            bg.raycastTarget = false;

            _group = go.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;

            GameObject contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup));
            _content = (RectTransform)contentGo.transform;
            _content.SetParent(_root, false);
            _content.localRotation = Quaternion.identity;
            _content.localScale = Vector3.one;
            // Top-anchored with a size fitter rather than stretched, so its
            // height reports the content's real height instead of being driven
            // by the card. The card then sizes itself from that.
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.offsetMin = new Vector2(Padding, 0f);
            _content.offsetMax = new Vector2(-Padding, 0f);
            _content.anchoredPosition = new Vector2(0f, -VerticalPadding);

            ContentSizeFitter contentFitter = contentGo.AddComponent<ContentSizeFitter>();
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            VerticalLayoutGroup vlg = contentGo.GetComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = Spacing;

            _kicker = CreateText("Kicker", 13f);
            _kicker.characterSpacing = 4f;
            _name = CreateText("Name", 22f);
            _statBlock = CreateText("StatBlock", 14f);
            _statBlock.color = new Color(Palette.Bone.r, Palette.Bone.g, Palette.Bone.b, 0.9f);

            _name.text = "Hover an item";

            ApplyWeightVisibility();
            Plugin.HideVanillaWeight.SettingChanged += (s, e) => ApplyWeightVisibility();

            Plugin.Log.LogInfo("detail card: built under " + HudPath.Of(_root) +
                " (child of m_player, right edge, gap=" + _root.anchoredPosition.x.ToString("F1") + ", width=" + CardWidth + ")");
            LogOverlapCheck();
        }

        // The card sits past m_player's right edge, but vanilla's armor and
        // weight readouts live out there too and were showing through it. Push
        // the card clear of whichever of them reaches furthest right, measured
        // live rather than guessed: their world-space right edge converted back
        // into m_player's own local units, which is what anchoredPosition is in.
        private float ComputeGap(RectTransform playerPanel)
        {
            float gap = CardGap;
            gap = ClearWidget(gap, playerPanel, _gui.m_armor != null ? _gui.m_armor.rectTransform : null, "m_armor");
            gap = ClearWidget(gap, playerPanel, _gui.m_weight != null ? _gui.m_weight.rectTransform : null, "m_weight");
            return gap;
        }

        private static float ClearWidget(float gap, RectTransform playerPanel, RectTransform widget, string label)
        {
            if (widget == null) return gap;

            Vector3[] widgetCorners = new Vector3[4];
            widget.GetWorldCorners(widgetCorners);
            Vector3[] panelCorners = new Vector3[4];
            playerPanel.GetWorldCorners(panelCorners);

            float widgetRight = Mathf.Max(widgetCorners[2].x, widgetCorners[3].x);
            float panelRight = Mathf.Max(panelCorners[2].x, panelCorners[3].x);
            float extraWorld = widgetRight - panelRight;
            if (extraWorld <= 0f) return gap;

            float scale = playerPanel.lossyScale.x;
            if (Mathf.Approximately(scale, 0f)) scale = 1f;
            float needed = extraWorld / scale + WidgetClearMargin;

            if (needed > gap)
            {
                Plugin.Log.LogInfo("detail card: " + label + " reaches " + (extraWorld / scale).ToString("F1") +
                    "px past the panel edge, pushing the card gap to " + needed.ToString("F1") + "px to clear it.");
                return needed;
            }
            return gap;
        }

        // Hides the whole weight widget, wooden tab and all, not just the
        // number: the readout sits inside a "Weight" container alongside its
        // own bkg sprite (Minimal UI reaches for "root/Container/Weight/bkg"
        // the same way), so hiding only the text left the tab floating empty.
        // Falls back to the text component alone if the parent isn't the
        // wrapper we expect. The armor readout is deliberately left alone.
        private void ApplyWeightVisibility()
        {
            if (_gui == null || _gui.m_weight == null) return;
            bool hide = Plugin.HideVanillaWeight.Value;

            Transform widget = _gui.m_weight.transform.parent;
            bool parentIsWrapper = widget != null &&
                widget.name.ToLowerInvariant().Contains("weight") &&
                widget != _gui.m_player;

            if (parentIsWrapper)
            {
                widget.gameObject.SetActive(!hide);
                if (!_loggedWeightHide)
                {
                    _loggedWeightHide = true;
                    Plugin.Log.LogInfo("detail card: vanilla weight widget is " + HudPath.Of(widget) +
                        ", hiding the whole container (tab included).");
                }
            }
            else
            {
                _gui.m_weight.enabled = !hide;
                if (!_loggedWeightHide)
                {
                    _loggedWeightHide = true;
                    Plugin.Log.LogWarning("detail card: m_weight's parent (" +
                        (widget != null ? widget.name : "(none)") +
                        ") does not look like a weight container, hiding just the number. The wooden tab may remain.");
                }
            }
        }

        private bool _loggedWeightHide;

        private TMP_Text CreateText(string name, float fontSize)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_content, false);
            go.transform.localScale = Vector3.one;

            TMP_Text text = go.AddComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.enableAutoSizing = false;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.color = Palette.Bone;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.text = "";

            ContentSizeFitter fitter = go.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            HudFont.Apply(text);
            return text;
        }

        // Purely diagnostic: doesn't change anything, just tells us in the log
        // whether the guess about m_player's right edge lands clear of the
        // crafting panel or not (003-inventory.md's "Still open" item).
        private void LogOverlapCheck()
        {
            if (_gui == null || _gui.m_crafting == null || _root == null) return;

            Vector3[] cardCorners = new Vector3[4];
            _root.GetWorldCorners(cardCorners);
            Vector3[] craftCorners = new Vector3[4];
            _gui.m_crafting.GetWorldCorners(craftCorners);

            Rect cardRect = new Rect(cardCorners[0].x, cardCorners[0].y, cardCorners[2].x - cardCorners[0].x, cardCorners[2].y - cardCorners[0].y);
            Rect craftRect = new Rect(craftCorners[0].x, craftCorners[0].y, craftCorners[2].x - craftCorners[0].x, craftCorners[2].y - craftCorners[0].y);
            bool overlaps = cardRect.Overlaps(craftRect);

            Plugin.Log.LogInfo("detail card: world rect " + cardRect + ", crafting panel world rect " + craftRect + ", overlaps=" + overlaps);
            if (overlaps)
            {
                Plugin.Log.LogWarning("detail card: OVERLAPS the crafting panel. Needs a smaller CardWidth or a different anchor.");
            }

            ReportOverlap(cardRect, _gui.m_armor != null ? _gui.m_armor.rectTransform : null, "armor readout");
            ReportOverlap(cardRect, _gui.m_weight != null ? _gui.m_weight.rectTransform : null, "weight readout");
        }

        private static void ReportOverlap(Rect cardRect, RectTransform widget, string label)
        {
            if (widget == null) return;

            Vector3[] corners = new Vector3[4];
            widget.GetWorldCorners(corners);
            Rect widgetRect = new Rect(corners[0].x, corners[0].y, corners[2].x - corners[0].x, corners[2].y - corners[0].y);
            bool overlaps = cardRect.Overlaps(widgetRect);

            Plugin.Log.LogInfo("detail card: " + label + " world rect " + widgetRect + ", overlaps=" + overlaps);
            if (overlaps)
            {
                Plugin.Log.LogWarning("detail card: still OVERLAPS the " + label + " after the gap adjustment.");
            }
        }

        // Retargets the fade to 0 rather than hiding immediately; Update()
        // finishes the job with SetActive(false) once the alpha reaches 0, so
        // the card fades out instead of popping off.
        internal void Hide()
        {
            _visible = false;
        }

        // Called by DetailCardFeedPatch, up to twice a frame (player grid,
        // then container grid if one's open). item is null when that grid has
        // nothing hovered or selected this frame; we ignore null rather than
        // clear the card, so it stays on the last real item instead of
        // blanking every time the mouse crosses a gap between slots.
        internal void SetItem(ItemDrop.ItemData item)
        {
            if (_root == null) return;
            bool wasVisible = _visible;
            _visible = true;
            _root.gameObject.SetActive(true);
            if (item == null) return;

            string typeName = TypeName(item.m_shared.m_itemType);
            string kickerText = typeName.ToUpperInvariant();
            if (item.m_equipped)
            {
                kickerText += "   <color=#" + ColorUtility.ToHtmlStringRGB(Palette.EmberHealth) + ">EQUIPPED</color>";
            }
            string kicker = kickerText;
            string name = Localization.instance.Localize(item.m_shared.m_name);
            string statBlock = BuildStatBlock(item, Player.m_localPlayer);

            // Content changed while already visible: crossfade rather than
            // pop by dipping the alpha and letting Update() bring it back up,
            // instead of restarting from fully hidden.
            bool contentChanged = wasVisible && (_name.text != name || _kicker.text != kicker || _statBlock.text != statBlock);

            _kicker.text = kicker;
            _name.text = name;
            _statBlock.text = statBlock;

            if (contentChanged && Plugin.MotionEnabled.Value)
            {
                _alpha.Retarget(Mathf.Min(_alpha.Value, CrossfadeFloor), 0f);
                _alphaInit = true;
            }

            ResizeToContent();
        }

        // Height follows the content, with a floor so the card doesn't bob up
        // and down between a one-line material and a full weapon stat block.
        private void ResizeToContent()
        {
            if (_content == null || _root == null) return;

            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
            float height = Mathf.Max(MinCardHeight, _content.rect.height + VerticalPadding * 2f);
            _root.sizeDelta = new Vector2(CardWidth, height);
        }

        // "Damage by type, block, knockback, armor, durability as a percent,
        // weight", plus the food block for consumables. Every value comes from
        // the same ItemData/HitData methods GetTooltip() itself calls
        // (003-notes.md), never from parsing its string.
        //
        // Which stats appear is decided by m_itemType, not by whether the
        // number happens to be nonzero. Those fields carry defaults on every
        // item's shared data whether or not they mean anything: Feathers came
        // out claiming Block 10, Knockback 50 and Armor 10 when this went by
        // "> 0" alone. The type sets here mirror GetTooltip's own switch
        // exactly, which is the authority on when each stat is meaningful.
        private static string BuildStatBlock(ItemDrop.ItemData item, Player player)
        {
            StringBuilder sb = new StringBuilder();
            ItemDrop.ItemData.ItemType type = item.m_shared.m_itemType;
            int quality = item.m_quality;
            float worldLevel = item.m_worldLevel;

            // Each stat is measured against whichever displaced item actually
            // provides it: a two-hander compares its damage to the sword it
            // would take off and its block to the shield, since equipping it
            // costs you both. Armor keeps comparing by slot.
            Comparison against = BuildComparison(item, player);
            bool compare = Plugin.ShowDeltas.Value;

            ItemDrop.ItemData attackAgainst = against.Attack;
            ItemDrop.ItemData blockAgainst = against.Block;
            ItemDrop.ItemData armorAgainst = against.Armor;
            ItemDrop.ItemData weightAgainst = against.Weight;

            if (HasAttack(type))
            {
                bool haveEquipped = compare && attackAgainst != null;
                HitData.DamageTypes dmg = item.GetDamage(quality, worldLevel);
                HitData.DamageTypes eqDmg = haveEquipped
                    ? attackAgainst.GetDamage(attackAgainst.m_quality, attackAgainst.m_worldLevel)
                    : default(HitData.DamageTypes);

                AppendDamage(sb, "Damage", dmg.m_damage, eqDmg.m_damage, compare, haveEquipped);
                AppendDamage(sb, "Blunt", dmg.m_blunt, eqDmg.m_blunt, compare, haveEquipped);
                AppendDamage(sb, "Slash", dmg.m_slash, eqDmg.m_slash, compare, haveEquipped);
                AppendDamage(sb, "Pierce", dmg.m_pierce, eqDmg.m_pierce, compare, haveEquipped);
                AppendDamage(sb, "Chop", dmg.m_chop, eqDmg.m_chop, compare, haveEquipped);
                AppendDamage(sb, "Pickaxe", dmg.m_pickaxe, eqDmg.m_pickaxe, compare, haveEquipped);
                AppendDamage(sb, "Fire", dmg.m_fire, eqDmg.m_fire, compare, haveEquipped);
                AppendDamage(sb, "Frost", dmg.m_frost, eqDmg.m_frost, compare, haveEquipped);
                AppendDamage(sb, "Lightning", dmg.m_lightning, eqDmg.m_lightning, compare, haveEquipped);
                AppendDamage(sb, "Poison", dmg.m_poison, eqDmg.m_poison, compare, haveEquipped);
                AppendDamage(sb, "Spirit", dmg.m_spirit, eqDmg.m_spirit, compare, haveEquipped);

                AppendStat(sb, "Knockback", item.m_shared.m_attackForce,
                    haveEquipped ? attackAgainst.m_shared.m_attackForce : 0f, compare, haveEquipped, true, "0");
            }

            if (CanBlock(type))
            {
                bool haveBlock = compare && blockAgainst != null;
                AppendStat(sb, "Block", item.GetBaseBlockPower(quality),
                    haveBlock ? blockAgainst.GetBaseBlockPower(blockAgainst.m_quality) : 0f, compare, haveBlock, true, "0");
            }

            if (IsArmorType(type))
            {
                bool haveArmor = compare && armorAgainst != null;
                AppendStat(sb, "Armor", item.GetArmor(quality, worldLevel),
                    haveArmor ? armorAgainst.GetArmor(armorAgainst.m_quality, armorAgainst.m_worldLevel) : 0f,
                    compare, haveArmor, true, "0");
            }

            // No delta: durability is this item's own wear, so comparing it
            // against the equipped item's wear says nothing about which item
            // is better.
            if (item.m_shared.m_useDurability)
            {
                AppendPlain(sb, "Durability", Mathf.RoundToInt(item.GetDurabilityPercentage() * 100f) + "%");
            }

            // Lighter is better, so this one compares the other way round.
            bool haveWeight = compare && weightAgainst != null;
            AppendStat(sb, "Weight", item.GetWeight(),
                haveWeight ? weightAgainst.GetWeight() : 0f, compare, haveWeight, false, "0.0");

            if (type == ItemDrop.ItemData.ItemType.Consumable)
            {
                AppendFoodLines(sb, item, player);
            }

            return sb.ToString();
        }

        // Where each stat's "before" number comes from. Equipping one item can
        // cost you two (a two-hander takes off both the sword and the shield),
        // so a single comparison target would have to throw one of them away.
        // Instead each stat is paired with the displaced item that actually
        // provides it.
        private struct Comparison
        {
            internal ItemDrop.ItemData Attack;
            internal ItemDrop.ItemData Block;
            internal ItemDrop.ItemData Armor;
            internal ItemDrop.ItemData Weight;
        }

        // Armor compares by slot, which for Helmet/Chest/Legs/Shoulder is the
        // same thing as comparing by type. Hand items compare against whatever
        // you would actually have to put down to wield this, per
        // 003-inventory.md's decisions.
        private static Comparison BuildComparison(ItemDrop.ItemData item, Player player)
        {
            Comparison c = default(Comparison);
            if (player == null) return c;

            Inventory inventory = player.GetInventory();
            if (inventory == null) return c;

            ItemDrop.ItemData.ItemType type = item.m_shared.m_itemType;

            if (!IsHandItem(type))
            {
                ItemDrop.ItemData bySlot = FirstOfType(inventory.GetEquippedItems(), type);
                if (bySlot == item) return c;
                c.Armor = bySlot;
                c.Weight = bySlot;
                return c;
            }

            List<ItemDrop.ItemData> displaced = DisplacedByEquipping(item, player);
            if (displaced.Count == 0) return c;

            for (int i = 0; i < displaced.Count; i++)
            {
                ItemDrop.ItemData d = displaced[i];
                ItemDrop.ItemData.ItemType dType = d.m_shared.m_itemType;

                if (c.Attack == null && HasAttack(dType)) c.Attack = d;

                // A shield is the truest block comparison; a parrying weapon
                // only stands in if nothing better comes off.
                if (dType == ItemDrop.ItemData.ItemType.Shield) c.Block = d;
                else if (c.Block == null && CanBlock(dType)) c.Block = d;
            }

            // Weight is an item-to-item question, so it follows whichever
            // displaced item is the closest counterpart to this one.
            c.Weight = c.Attack ?? c.Block ?? displaced[0];
            return c;
        }

        // Which equipped items Humanoid.EquipItem would take off to make room
        // for this one, mirroring its own per-type branches. The special cases
        // are real: a torch slots into a free left hand beside a one-hander
        // and costs you nothing, a one-hander leaves a shield or torch alone,
        // and a shield leaves a one-hander or torch alone.
        private static List<ItemDrop.ItemData> DisplacedByEquipping(ItemDrop.ItemData item, Player player)
        {
            List<ItemDrop.ItemData> displaced = new List<ItemDrop.ItemData>();
            ItemDrop.ItemData right = player.RightItem;
            ItemDrop.ItemData left = player.LeftItem;

            switch (item.m_shared.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.Tool:
                case ItemDrop.ItemData.ItemType.Bow:
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                    AddIfReal(displaced, right, item);
                    AddIfReal(displaced, left, item);
                    break;

                case ItemDrop.ItemData.ItemType.Torch:
                    if (right != null && left == null &&
                        right.m_shared.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon)
                    {
                        break; // goes in the free left hand, nothing comes off
                    }
                    AddIfReal(displaced, right, item);
                    if (left != null && left.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Shield)
                    {
                        AddIfReal(displaced, left, item);
                    }
                    break;

                case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                    AddIfReal(displaced, right, item);
                    if (left != null &&
                        left.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Shield &&
                        left.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Torch)
                    {
                        AddIfReal(displaced, left, item);
                    }
                    break;

                case ItemDrop.ItemData.ItemType.Shield:
                    AddIfReal(displaced, left, item);
                    if (right != null &&
                        right.m_shared.m_itemType != ItemDrop.ItemData.ItemType.OneHandedWeapon &&
                        right.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Torch)
                    {
                        AddIfReal(displaced, right, item);
                    }
                    break;
            }

            return displaced;
        }

        private static void AddIfReal(List<ItemDrop.ItemData> list, ItemDrop.ItemData candidate, ItemDrop.ItemData hovered)
        {
            if (candidate == null || candidate == hovered) return;
            list.Add(candidate);
        }

        private static ItemDrop.ItemData FirstOfType(List<ItemDrop.ItemData> items, ItemDrop.ItemData.ItemType type)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].m_shared.m_itemType == type) return items[i];
            }
            return null;
        }

        private static bool IsHandItem(ItemDrop.ItemData.ItemType t)
        {
            switch (t)
            {
                case ItemDrop.ItemData.ItemType.Tool:
                case ItemDrop.ItemData.ItemType.Torch:
                case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                case ItemDrop.ItemData.ItemType.Shield:
                case ItemDrop.ItemData.ItemType.Bow:
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                    return true;
                default:
                    return false;
            }
        }

        // A damage type only earns a line if this item or the equipped one
        // actually deals it, so a slashing sword compared against a blunt club
        // still shows both rows rather than silently dropping the one where
        // this item happens to be zero.
        private static void AppendDamage(StringBuilder sb, string label, float value, float equippedValue, bool compare, bool haveEquipped)
        {
            if (value <= 0f && (!haveEquipped || equippedValue <= 0f)) return;
            AppendStat(sb, label, value, equippedValue, compare, haveEquipped, true, "0");
        }

        // "+N max health, +N stamina for M min", whether Player.CanEat(item,
        // false) says you can eat it now, and which of your three meals it
        // would replace (003-notes.md, "Food effects are computable without
        // eating"). Slot rules mirror Player.CanEat/EatFood exactly: same
        // name and still refreshable means it tops itself up, a free slot
        // means nothing gets displaced, otherwise it's whichever active meal
        // has the least time left among the ones that can still be
        // refreshed (Player's own private GetMostDepletedFood, reimplemented
        // here from the same public Food fields since that method isn't
        // exposed).
        private static void AppendFoodLines(StringBuilder sb, ItemDrop.ItemData item, Player player)
        {
            string effect = "";
            if (item.m_shared.m_food > 0f)
            {
                effect += "+" + item.m_shared.m_food.ToString("0") + " max health";
            }
            if (item.m_shared.m_foodStamina > 0f)
            {
                effect += (effect.Length > 0 ? ", " : "") + "+" + item.m_shared.m_foodStamina.ToString("0") + " stamina";
            }
            if (item.m_shared.m_foodEitr > 0f)
            {
                effect += (effect.Length > 0 ? ", " : "") + "+" + item.m_shared.m_foodEitr.ToString("0") + " eitr";
            }
            if (effect.Length > 0)
            {
                int minutes = Mathf.CeilToInt(item.m_shared.m_foodBurnTime / 60f);
                effect += " for " + minutes + " min";
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(effect);
            }

            if (player == null) return;

            bool canEat = player.CanEat(item, showMessages: false);
            AppendPlain(sb, "Can eat now", canEat ? "Yes" : "No");

            // Which meal this displaces, mirroring Player.EatFood's own order:
            // a serving of the same food that's still refreshable tops itself
            // up, otherwise a free slot takes it, otherwise it pushes out
            // whichever meal has the least time left among the ones that can
            // still be refreshed (Player.GetMostDepletedFood, reimplemented
            // here from the same public Food fields since it isn't exposed).
            List<Player.Food> foods = player.GetFoods();
            Player.Food sameName = null;
            for (int i = 0; i < foods.Count; i++)
            {
                if (foods[i].m_item != null && foods[i].m_item.m_shared.m_name == item.m_shared.m_name)
                {
                    sameName = foods[i];
                    break;
                }
            }

            Player.Food displaced = null;
            if (sameName != null && sameName.CanEatAgain())
            {
                displaced = sameName;
                AppendPlain(sb, "Replaces", "your current serving (tops it up)");
            }
            else if (foods.Count >= 3)
            {
                for (int i = 0; i < foods.Count; i++)
                {
                    if (foods[i].CanEatAgain() && (displaced == null || foods[i].m_time < displaced.m_time))
                    {
                        displaced = foods[i];
                    }
                }
                if (displaced != null && displaced.m_item != null)
                {
                    AppendPlain(sb, "Replaces", Localization.instance.Localize(displaced.m_item.m_shared.m_name));
                }
            }
            // else: a free slot, nothing gets displaced, displaced stays null.

            if (!Plugin.ShowDeltas.Value || !canEat) return;

            // What your maximums actually become. The displaced meal's current
            // (already decayed) contribution comes off, this food's full value
            // goes on: maxHP = m_baseHP + sum of each active meal's m_health,
            // per Player.UpdateFood, so swapping one entry moves the total by
            // exactly that difference.
            AppendPrediction(sb, "Max health", player.GetMaxHealth(),
                displaced != null ? displaced.m_health : 0f, item.m_shared.m_food);
            AppendPrediction(sb, "Max stamina", player.GetMaxStamina(),
                displaced != null ? displaced.m_stamina : 0f, item.m_shared.m_foodStamina);
            if (item.m_shared.m_foodEitr > 0f || player.GetMaxEitr() > 0f)
            {
                AppendPrediction(sb, "Max eitr", player.GetMaxEitr(),
                    displaced != null ? displaced.m_eitr : 0f, item.m_shared.m_foodEitr);
            }
        }

        private static void AppendPrediction(StringBuilder sb, string label, float current, float displacedContribution, float gained)
        {
            float predicted = current - displacedContribution + gained;
            if (Mathf.Approximately(predicted, current)) return;

            if (sb.Length > 0) sb.Append('\n');
            sb.Append(label).Append(ValueColumn).Append(current.ToString("0")).Append(" → ");
            sb.Append(Colored(predicted.ToString("0"), predicted >= current ? Palette.GoodGreen : Palette.EmberHealth));
        }

        // TMP's <pos> tag gives us real columns inside one text object: label
        // on the left, value at a fixed offset, delta right of that. Without
        // it the deltas wander with the label length and stop reading as a
        // column you can scan down.
        private const string ValueColumn = "<pos=58%>";
        private const string DeltaColumn = "<pos=82%>";

        private static void AppendPlain(StringBuilder sb, string label, string value)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(label).Append(ValueColumn).Append(value);
        }

        // One stat row, with the delta against the equipped item of the same
        // type: green when this item wins, ember when it loses, dim when they
        // match, "new" when that slot is empty. higherIsBetter flips for
        // weight, where a smaller number is the better one.
        private static void AppendStat(StringBuilder sb, string label, float value, float equippedValue,
            bool compare, bool haveEquipped, bool higherIsBetter, string format)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(label).Append(ValueColumn).Append(value.ToString(format));

            if (!compare) return;

            sb.Append(DeltaColumn);
            if (!haveEquipped)
            {
                sb.Append(Dim("new"));
                return;
            }

            float diff = value - equippedValue;
            if (Mathf.Approximately(diff, 0f))
            {
                sb.Append(Dim("="));
                return;
            }

            bool better = higherIsBetter ? diff > 0f : diff < 0f;
            string sign = diff > 0f ? "+" : "";
            sb.Append(Colored(sign + diff.ToString(format), better ? Palette.GoodGreen : Palette.EmberHealth));
        }

        private static string Colored(string text, Color color)
        {
            return "<color=#" + ColorUtility.ToHtmlStringRGB(color) + ">" + text + "</color>";
        }

        private static string Dim(string text)
        {
            return "<alpha=#80>" + text + "<alpha=#FF>";
        }

        private static bool IsWeaponType(ItemDrop.ItemData.ItemType t)
        {
            switch (t)
            {
                case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                case ItemDrop.ItemData.ItemType.Bow:
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.Torch:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                    return true;
                default:
                    return false;
            }
        }

        // Damage and knockback: the same set GetTooltip shows them for.
        private static bool HasAttack(ItemDrop.ItemData.ItemType t)
        {
            if (IsWeaponType(t)) return true;
            return t == ItemDrop.ItemData.ItemType.Ammo || t == ItemDrop.ItemData.ItemType.AmmoNonEquipable;
        }

        // Block: weapons (which can parry) and shields. Not ammo.
        private static bool CanBlock(ItemDrop.ItemData.ItemType t)
        {
            return IsWeaponType(t) || t == ItemDrop.ItemData.ItemType.Shield;
        }

        private static bool IsArmorType(ItemDrop.ItemData.ItemType t)
        {
            switch (t)
            {
                case ItemDrop.ItemData.ItemType.Helmet:
                case ItemDrop.ItemData.ItemType.Chest:
                case ItemDrop.ItemData.ItemType.Legs:
                case ItemDrop.ItemData.ItemType.Shoulder:
                    return true;
                default:
                    return false;
            }
        }

        // Presentational only, entirely under our control (not read off any
        // vanilla localization key, there isn't a generic one for this).
        private static string TypeName(ItemDrop.ItemData.ItemType type)
        {
            switch (type)
            {
                case ItemDrop.ItemData.ItemType.Material: return "Material";
                case ItemDrop.ItemData.ItemType.Consumable: return "Food";
                case ItemDrop.ItemData.ItemType.OneHandedWeapon: return "One-Handed Weapon";
                case ItemDrop.ItemData.ItemType.Bow: return "Bow";
                case ItemDrop.ItemData.ItemType.Shield: return "Shield";
                case ItemDrop.ItemData.ItemType.Helmet: return "Helmet";
                case ItemDrop.ItemData.ItemType.Chest: return "Chest Armor";
                case ItemDrop.ItemData.ItemType.Ammo: return "Ammo";
                case ItemDrop.ItemData.ItemType.Customization: return "Customization";
                case ItemDrop.ItemData.ItemType.Legs: return "Leg Armor";
                case ItemDrop.ItemData.ItemType.Hands: return "Hand Armor";
                case ItemDrop.ItemData.ItemType.Trophy: return "Trophy";
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon: return "Two-Handed Weapon";
                case ItemDrop.ItemData.ItemType.Torch: return "Torch";
                case ItemDrop.ItemData.ItemType.Misc: return "Misc";
                case ItemDrop.ItemData.ItemType.Shoulder: return "Shoulder Armor";
                case ItemDrop.ItemData.ItemType.Utility: return "Utility";
                case ItemDrop.ItemData.ItemType.Tool: return "Tool";
                case ItemDrop.ItemData.ItemType.Attach_Atgeir: return "Atgeir Attachment";
                case ItemDrop.ItemData.ItemType.Fish: return "Fish";
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft: return "Two-Handed Weapon";
                case ItemDrop.ItemData.ItemType.AmmoNonEquipable: return "Ammo";
                case ItemDrop.ItemData.ItemType.Trinket: return "Trinket";
                default: return "Item";
            }
        }
    }
}
