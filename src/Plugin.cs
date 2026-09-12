using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace BoneAndEmber
{
    // Client-only: nothing here talks to the server, so it works on any server
    // without the server (or other players) installing it.
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string ModGuid = "com.geo.boneandember";
        public const string ModName = "Bone & Ember";
        public const string ModVersion = "0.1.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> ShowWelcome;

        // Always-on HUD (docs/design/001-always-on-hud.md)
        internal static ConfigEntry<bool> HudEnabled;
        internal static ConfigEntry<float> HudScale;
        internal static ConfigEntry<Vector2> HudPosition;
        internal static ConfigEntry<float> BarWidth;
        internal static ConfigEntry<float> BarHeight;
        internal static ConfigEntry<bool> ShowNumbers;
        internal static ConfigEntry<bool> HideOtherBars;
        internal static ConfigEntry<bool> ShowFoodSegments;
        internal static ConfigEntry<bool> StaminaBarEnabled;
        internal static ConfigEntry<float> StaminaHeight;
        internal static ConfigEntry<bool> LowHealthVignette;
        internal static ConfigEntry<bool> StatusChipsEnabled;
        internal static ConfigEntry<Color> FoodHealthColor;
        internal static ConfigEntry<float> NotchAngle;
        internal static ConfigEntry<bool> LoudMomentsEnabled;
        internal static ConfigEntry<float> LoudMomentOffsetY;

        // Forsaken Power line (docs/design/005-forsaken-power.md)
        internal static ConfigEntry<bool> PowerEnabled;
        internal static ConfigEntry<float> PowerBarHeight;
        internal static ConfigEntry<float> GapStaminaToPower;

        // Slice 8 layout and font. All live.
        internal static ConfigEntry<float> LabelSize;
        internal static ConfigEntry<float> HealthNumberScale;
        internal static ConfigEntry<float> StaminaNumberScale;
        internal static ConfigEntry<float> LetterSpacing;
        internal static ConfigEntry<float> GapChipsToHeader;
        internal static ConfigEntry<float> GapHeaderToBar;
        internal static ConfigEntry<float> GapHealthToStamina;
        internal static ConfigEntry<float> ChipGap;
        internal static ConfigEntry<float> BarEndAngle;
        internal static ConfigEntry<bool> NotchAngleOverride;
        internal static ConfigEntry<string> FontName;
        internal static ConfigEntry<bool> DevCommands;

        // Waypoint strip (docs/design/002-waypoint-strip.md)
        internal static ConfigEntry<bool> WaypointStripEnabled;
        internal static ConfigEntry<float> StripWidth;
        internal static ConfigEntry<float> StripOffsetY;
        internal static ConfigEntry<float> StripFovDegrees;
        internal static ConfigEntry<bool> AlwaysShowCompass;
        internal static ConfigEntry<bool> ShowDeathMarkers;
        internal static ConfigEntry<float> MarkerNearDistance;
        internal static ConfigEntry<int> MaxDeathMarkers;
        internal static ConfigEntry<bool> RemoveRecoveredPins;

        // Sense (docs/design/006-sense.md)
        internal static ConfigEntry<bool> SenseEnabled;
        internal static ConfigEntry<float> SenseDelay;
        internal static ConfigEntry<float> SenseRange;
        internal static ConfigEntry<bool> SenseShowNames;
        internal static ConfigEntry<int> SenseMaxMarkers;
        internal static ConfigEntry<float> SenseMoveGrace;
        internal static ConfigEntry<int> SenseMarkerSize;

        // Inventory (docs/design/003-inventory.md)
        internal static ConfigEntry<bool> InventoryEnabled;
        internal static ConfigEntry<float> SlotPlateAlpha;
        internal static ConfigEntry<bool> PanelRestyle;
        internal static ConfigEntry<bool> EquippedTags;
        internal static ConfigEntry<bool> DetailCard;
        internal static ConfigEntry<bool> ShowDeltas;
        internal static ConfigEntry<bool> HideVanillaWeight;
        internal static ConfigEntry<bool> DropTargetHighlight;
        internal static ConfigEntry<bool> FilterRow;
        internal static ConfigEntry<bool> HotbarHints;

        // Repair (docs/design/004-repair-and-buried-ux.md)
        internal static ConfigEntry<bool> RepairLine;
        internal static ConfigEntry<bool> RepairAll;
        internal static ConfigEntry<bool> WeightBar;
        internal static ConfigEntry<bool> RadialEnabled;
        internal static ConfigEntry<bool> RadialOpensFood;
        internal static ConfigEntry<float> RadialScale;
        internal static ConfigEntry<bool> RadialHideVanillaSidePanel;

        // Motion (slice 9, docs/design/003-inventory.md)
        internal static ConfigEntry<bool> MotionEnabled;

        internal static ConfigEntry<bool> LiveConfigReload;

        private Harmony _harmony;
        private FileSystemWatcher _configWatcher;
        private volatile bool _configDirty;
        private bool _reloadPending;
        private float _reloadAt;

        private void Awake()
        {
            Log = Logger;

            LiveConfigReload = Config.Bind(
                "General",
                "LiveConfigReload",
                true,
                "Apply edits to this file the moment you save it, without restarting. Lets you tune the mod in a text editor instead of the Configuration Manager panel, which on a high-DPI screen renders very small and cannot be scaled.");

            ShowWelcome = Config.Bind(
                "General",
                "ShowWelcomeMessage",
                true,
                "Show a message when you spawn, to confirm the mod is loaded.");

            HudEnabled = Config.Bind(
                "HUD",
                "Enabled",
                true,
                "Draw the new always-on HUD. Turn it off to get the vanilla bars (and Minimal UI's) back, live, with no reload.");

            HudScale = Config.Bind(
                "HUD",
                "Scale",
                1f,
                new ConfigDescription(
                    "Size of the HUD cluster.",
                    new AcceptableValueRange<float>(0.4f, 3f)));

            HudPosition = Config.Bind(
                "HUD",
                "Position",
                new Vector2(60f, 170f),
                "Where the cluster sits, in pixels from the bottom left corner.");

            BarWidth = Config.Bind(
                "HUD",
                "BarWidth",
                240f,
                new ConfigDescription(
                    "Fixed width of the health bar in pixels, before Scale is applied. Unlike vanilla, the bar does not grow with max health; food segments divide this width instead of stretching it. The stamina bar shares this width.",
                    new AcceptableValueRange<float>(120f, 600f)));

            BarHeight = Config.Bind(
                "HUD",
                "BarHeight",
                14f,
                new ConfigDescription(
                    "Fixed height of the health bar in pixels, before Scale is applied. The bar's frame (background, border, fill) is normalized to identity rotation and scale on build, so this is the real pixel height on screen, not something inherited from vanilla's prefab.",
                    new AcceptableValueRange<float>(4f, 80f)));

            ShowNumbers = Config.Bind(
                "HUD",
                "ShowNumbers",
                true,
                "Show current and max health on the bar.");

            ShowFoodSegments = Config.Bind(
                "HUD",
                "ShowFoodSegments",
                true,
                "Divide the health bar into base health and each active food's current share, pulse a food's segment once it can be eaten again, and caption whichever meal is closest to running out. Also hides vanilla's (and Minimal UI's) food icons and timers while on; turn this off to get those back instead.");

            StaminaBarEnabled = Config.Bind(
                "HUD",
                "StaminaBarEnabled",
                true,
                "Draw the stamina bar under health. Turn this off to get vanilla's (and Minimal UI's) stamina bar back instead, independent of the main HUD toggle.");

            StaminaHeight = Config.Bind(
                "HUD",
                "StaminaHeight",
                8f,
                new ConfigDescription(
                    "Fixed height of the stamina bar in pixels, before Scale is applied. Shares BarWidth with the health bar.",
                    new AcceptableValueRange<float>(2f, 60f)));

            HideOtherBars = Config.Bind(
                "HUD",
                "HideOtherHealthBars",
                true,
                "Also hide Minimal UI's health, stamina and food bars while ours are on. Turn this off if Minimal UI updates and something looks wrong.");

            LowHealthVignette = Config.Bind(
                "HUD",
                "LowHealthVignette",
                true,
                "Below 30% health, pulse a soft red vignette at the screen edges. The health bar itself always switches to its brighter critical color below 30%, regardless of this setting.");

            FoodHealthColor = Config.Bind(
                "HUD",
                "FoodHealthColor",
                Palette.FoodEmberDefault,
                "Color of the food-derived part of the health fill. Base health stays ember; everything above 25 is 'on loan' from meals and drawn in this lighter, warmer shade, with the notch as the seam.");

            NotchAngle = Config.Bind(
                "HUD",
                "NotchAngle",
                0f,
                new ConfigDescription(
                    "Lean of the meal notches on the health bar, in degrees. 0 is vertical.",
                    new AcceptableValueRange<float>(-45f, 45f)));

            LoudMomentsEnabled = Config.Bind(
                "HUD",
                "LoudMomentsEnabled",
                true,
                "Show a bold slab near the top of the screen for a couple of seconds when an effect that needs action starts (Freezing for now). Off leaves only the status chip.");

            StatusChipsEnabled = Config.Bind(
                "HUD",
                "StatusChipsEnabled",
                true,
                "Draw active status effects as a row of chips above the health bar (icon, name, time left), bad effects first with an ember outline. Also hides vanilla's status effect list while on; turn this off to get it back.");

            PowerEnabled = Config.Bind("Power", "Enabled", true,
                "Draw the Forsaken Power as a fourth line under stamina: sigil and boss name, the cooldown refilling as a thin bar, gold and breathing when ready, gold and draining while active. Also hides vanilla's corner widget and the power's own status chip while on; turn this off to get both back, live.");
            PowerBarHeight = Config.Bind("Power", "BarHeight", 4f,
                new ConfigDescription("Height of the power bar in pixels, before Scale. Thinner than stamina so the three bars step down in weight.", new AcceptableValueRange<float>(1f, 30f)));
            GapStaminaToPower = Config.Bind("Layout", "GapStaminaToPower", 8f,
                new ConfigDescription("Pixels between the bottom of the stamina bar and the power header.", new AcceptableValueRange<float>(0f, 60f)));

            LoudMomentOffsetY = Config.Bind("Layout", "LoudMomentOffsetY", 200f,
                new ConfigDescription("Pixels from the top of the screen down to the middle of the loud moment slab. Has to clear the compass strip and the death marker distances hanging under it, so it sits lower than it did before 002.", new AcceptableValueRange<float>(60f, 500f)));

            LabelSize = Config.Bind("Layout", "LabelSize", 12f,
                new ConfigDescription("Font size of the HEALTH / STAMINA labels, chip text and the food caption, before Scale.", new AcceptableValueRange<float>(8f, 24f)));
            HealthNumberScale = Config.Bind("Layout", "HealthNumberScale", 1.6f,
                new ConfigDescription("Health number size as a multiple of LabelSize.", new AcceptableValueRange<float>(1f, 3f)));
            StaminaNumberScale = Config.Bind("Layout", "StaminaNumberScale", 1.25f,
                new ConfigDescription("Stamina number size as a multiple of LabelSize.", new AcceptableValueRange<float>(1f, 3f)));
            LetterSpacing = Config.Bind("Layout", "LetterSpacing", 6f,
                new ConfigDescription("Letterspacing of the small caps labels, in TMP character spacing units.", new AcceptableValueRange<float>(0f, 20f)));
            GapChipsToHeader = Config.Bind("Layout", "GapChipsToHeader", 6f,
                new ConfigDescription("Pixels between the chip row and the health header.", new AcceptableValueRange<float>(0f, 40f)));
            GapHeaderToBar = Config.Bind("Layout", "GapHeaderToBar", 3f,
                new ConfigDescription("Pixels between a header line and its bar.", new AcceptableValueRange<float>(0f, 40f)));
            GapHealthToStamina = Config.Bind("Layout", "GapHealthToStamina", 8f,
                new ConfigDescription("Pixels between the bottom of the health bar and the stamina header.", new AcceptableValueRange<float>(0f, 60f)));
            ChipGap = Config.Bind("Layout", "ChipGap", 6f,
                new ConfigDescription("Pixels between status chips.", new AcceptableValueRange<float>(0f, 30f)));
            BarEndAngle = Config.Bind("Layout", "BarEndAngle", 12f,
                new ConfigDescription("Angle of the cut at the right end of both bars, in degrees. 0 is square. The meal notch follows this unless NotchAngleOverride is on.", new AcceptableValueRange<float>(-45f, 45f)));
            NotchAngleOverride = Config.Bind("Layout", "NotchAngleOverride", false,
                "When on, the meal notch uses NotchAngle instead of following BarEndAngle.");
            DevCommands = Config.Bind("Dev", "DevCommands", false,
                "Enable Bone & Ember's test console commands: bae_status <name> toggles a status effect on you, bae_food eats three test foods. Client-only. Off by default; the commands exist but refuse to run until this is on.");

            WaypointStripEnabled = Config.Bind("Compass", "WaypointStripEnabled", true,
                "Draw the compass strip across the top center. Slice 2 is the N/E/S/W line only; death markers arrive in slice 3.");
            StripWidth = Config.Bind("Compass", "StripWidth", 0.33f,
                new ConfigDescription("Width of the strip as a fraction of the screen width, so it holds up at any resolution.", new AcceptableValueRange<float>(0.15f, 0.9f)));
            StripOffsetY = Config.Bind("Compass", "StripOffsetY", 40f,
                new ConfigDescription("Pixels from the top edge of the screen down to the top of the strip. The block stacks labels, ticks, then the line, so the rule itself sits a little lower than this.", new AcceptableValueRange<float>(0f, 400f)));
            StripFovDegrees = Config.Bind("Compass", "StripFovDegrees", 120f,
                new ConfigDescription("How many degrees of the world the strip spans end to end. Deliberately not the camera's real field of view: the strip is only a third of the screen wide, so nothing on it could sit over the thing it points at anyway. A fixed span reads like a game compass. Tune it by feel.", new AcceptableValueRange<float>(40f, 360f)));
            AlwaysShowCompass = Config.Bind("Compass", "AlwaysShowCompass", true,
                "Keep the N/E/S/W line up even when there is nothing to chase. Off means the strip only appears once there is a death marker on it.");
            ShowDeathMarkers = Config.Bind("Compass", "ShowDeathMarkers", true,
                "Put your deaths on the strip as skulls with distance. Reads the death pins already on your map, so old corpses count. Off leaves just the compass line.");
            MarkerNearDistance = Config.Bind("Compass", "MarkerNearDistance", 20f,
                new ConfigDescription("Distance in metres at which a skull drops its number and grows a little, to get your eyes off the HUD and onto the ground.", new AcceptableValueRange<float>(0f, 200f)));
            MaxDeathMarkers = Config.Bind("Compass", "MaxDeathMarkers", 0,
                new ConfigDescription("Cap how many skulls the strip will draw, nearest first. 0 shows every death you have, which is the intended behaviour but gets busy on a long-lived character.", new AcceptableValueRange<int>(0, 50)));
            RemoveRecoveredPins = Config.Bind("Compass", "RemoveRecoveredPins", false,
                "Delete a death pin from the map once you have collected the grave, instead of greying it out. Off by default on purpose: map pins are saved into your character file, so a deletion cannot be undone and survives uninstalling this mod.");

            SenseEnabled = Config.Bind("Sense", "Enabled", true,
                "Stand still for a moment and nearby items and plants mark themselves, with a ring that sweeps out from your feet. Stillness is the only input: no key, no mode. Off removes the overlay entirely.");
            SenseDelay = Config.Bind("Sense", "Delay", 800f,
                new ConfigDescription("Milliseconds you have to stand still before sense comes up. Lower feels eager and fires while you are lining up a shot; higher means you have to really mean it.", new AcceptableValueRange<float>(100f, 4000f)));
            SenseRange = Config.Bind("Sense", "Range", 25f,
                new ConfigDescription("How far out sense reaches, in metres. Also how far the ground ring travels, so raising it slows the sweep down over the same 400ms.", new AcceptableValueRange<float>(5f, 60f)));
            SenseShowNames = Config.Bind("Sense", "ShowNames", true,
                "Name dropped items within 5 metres under their marker. Plants and rocks never take a name either way, because in a forest they are the majority of what is nearby and the names would be the whole screen.");
            SenseMoveGrace = Config.Bind("Sense", "MoveGrace", 2500f,
                new ConfigDescription("Milliseconds you can keep moving before what you sensed starts fading. What you found rides along with you until then, so taking a step does not punish you by snapping it off and training you to stand still again just to look. 0 goes back to losing it the moment you move.", new AcceptableValueRange<float>(0f, 15000f)));
            SenseMarkerSize = Config.Bind("Sense", "MarkerSize", 8,
                new ConfigDescription("Width of the marker spike in pixels; it is drawn about 1.7 times as tall. Spikes sit on a dark backing and point down at the thing they mark, which is what keeps them from reading as rain or embers. Make them bigger if they still get lost.", new AcceptableValueRange<int>(4, 24)));
            SenseMaxMarkers = Config.Bind("Sense", "MaxMarkers", 40,
                new ConfigDescription("Cap on markers drawn at once, nearest first. Density is the known risk in this feature: on a beach or in a forest almost everything nearby is a pickable, so turn this down if it reads as noise.", new AcceptableValueRange<int>(5, 120)));

            FontName = Config.Bind("Font", "FontName", "Chakra Petch",
                "Family name of a font installed on this machine to use for all HUD text, built into a TextMeshPro asset at runtime. Empty or not installed keeps vanilla's font. The log says which one won.");

            InventoryEnabled = Config.Bind("Inventory", "Enabled", true,
                "Restyle inventory and container slots: flat plate, bone border, larger icon, durability as a thin bar along the bottom edge. Turn off to get vanilla's own slots back, live, with no reload.");
            SlotPlateAlpha = Config.Bind("Inventory", "SlotPlateAlpha", 1f,
                new ConfigDescription("Opacity of the flat plate that replaces each slot's own background sprite (which is disabled outright, not just covered). 1 is fully opaque; lower it to let a little of the vanilla hover tint bleed through.", new AcceptableValueRange<float>(0f, 1f)));
            PanelRestyle = Config.Bind("Inventory", "PanelRestyle", true,
                "Replace the wooden background art on the player and container panels with a flat dark plate matching the slots, and hide the vanilla border art. Panel size and position are unchanged. Doesn't touch the crafting panel. Live: toggling this reapplies immediately, no reopen needed.");
            EquippedTags = Config.Bind("Inventory", "EquippedTags", true,
                "Restyle the equipped marker into an ember corner triangle. Off restores vanilla's own equipped marker, live.");
            DetailCard = Config.Bind("Inventory", "DetailCard", true,
                "Show the detail card to the right of the player grid, filled from whichever slot you're hovering or have selected, and hide vanilla's own tooltip while it's on. Independent of InventoryEnabled. Live: toggling this shows/hides the card and restores/hides vanilla's tooltip immediately.");
            ShowDeltas = Config.Bind("Inventory", "ShowDeltas", true,
                "On the detail card, compare each stat against the equipped item of the same type: green when better, ember when worse, dim when equal, 'new' when that slot is empty. For food, predict your max health and stamina after eating instead. Off shows the raw numbers only.");
            HideVanillaWeight = Config.Bind("Inventory", "HideVanillaWeight", true,
                "Hide vanilla's weight readout beside the grid. Temporary: slice 7's weight bar replaces it properly. The armor readout is left alone.");
            DropTargetHighlight = Config.Bind("Inventory", "DropTargetHighlight", true,
                "While dragging an item, outline the slot it would land in. Gold means it drops there, bone means it swaps with what is already there, ember means the move is refused.");
            FilterRow = Config.Bind("Inventory", "FilterRow", true,
                "Show the category chips above the hotbar row. Picking one dims every slot that does not match to 30%; nothing moves or resizes. No controller binding: LB and RB are already taken in the inventory.");
            RadialEnabled = Config.Bind("Radial", "RadialEnabled", true,
                "Restyle vanilla's radial menu: dark wedge plates, bone icons, and a center that names the item with its count and, for food, what eating it gives you. Off leaves the radial exactly as vanilla draws it. bae_radial toggles this live.");
            RadialScale = Config.Bind("Radial", "Scale", 1f,
                new ConfigDescription("Size of the whole radial. Scaling the root only changes distances, not angles, so the stick and the mouse still pick the same wedge they would at full size. Live, and held every frame: vanilla's own Big/Small radial size setting no longer fights it.", new AcceptableValueRange<float>(0.5f, 1.5f)));
            RadialOpensFood = Config.Bind("Radial", "RadialOpensFood", true,
                "Open the radial straight onto the consumables page when you have a meal worth topping up, instead of the main group. Back still returns to the main radial. Never changes which groups exist.");
            RadialHideVanillaSidePanel = Config.Bind("Radial", "HideVanillaSidePanel", true,
                "Hide vanilla's armor/weight/tooltip panel beside the radial while it's open. The center hub already carries an item's name, count, and food line.");
            HotbarHints = Config.Bind("Inventory", "HotbarHints", true,
                "With a gamepad, show the button glyph on the highlighted hotbar slot so you can see what acts on it without guessing. The glyph is read from your own bindings, so a remapped or non-Xbox pad shows its own button. Mouse and keyboard never see it.");
            RepairLine = Config.Bind("Inventory", "RepairLine", true,
                "Show a repair line under the grid, always, while the inventory is open. At a station it repairs everything worn in one press; away from one it says where to go. Vanilla only ever draws its repair button when you are already standing at a station, which is why most players never find it.");
            RepairAll = Config.Bind("Inventory", "RepairAll", true,
                "One press repairs every worn item instead of one per press, and prints a single summary instead of a message per item. Applies to vanilla's own repair button too, so it works on a gamepad. Off leaves vanilla's one-at-a-time behaviour.");
            WeightBar = Config.Bind("Inventory", "WeightBar", true,
                "Draw carry weight as a thin bone bar under the grid with the number beside it, going ember over the limit. Pairs with HideVanillaWeight, which hides the readout this replaces.");
            MotionEnabled = Config.Bind("Motion", "Enabled", true,
                "Animate hover, filter, and appear/disappear states across the radial, inventory, and HUD instead of snapping instantly. Off for an exact A/B against the instant versions.");
            Log.LogInfo("config: Inventory.Enabled=" + InventoryEnabled.Value +
                " Inventory.SlotPlateAlpha=" + SlotPlateAlpha.Value +
                " Inventory.PanelRestyle=" + PanelRestyle.Value +
                " Inventory.EquippedTags=" + EquippedTags.Value +
                " Inventory.DetailCard=" + DetailCard.Value +
                " Inventory.ShowDeltas=" + ShowDeltas.Value +
                " Inventory.HideVanillaWeight=" + HideVanillaWeight.Value +
                " Inventory.DropTargetHighlight=" + DropTargetHighlight.Value +
                " Inventory.FilterRow=" + FilterRow.Value +
                " Inventory.HotbarHints=" + HotbarHints.Value +
                " Inventory.RepairLine=" + RepairLine.Value +
                " Inventory.RepairAll=" + RepairAll.Value +
                " Inventory.WeightBar=" + WeightBar.Value +
                " Radial.RadialEnabled=" + RadialEnabled.Value +
                " Radial.RadialOpensFood=" + RadialOpensFood.Value +
                " Radial.Scale=" + RadialScale.Value +
                " Radial.HideVanillaSidePanel=" + RadialHideVanillaSidePanel.Value +
                " Motion.Enabled=" + MotionEnabled.Value +
                " Sense.Enabled=" + SenseEnabled.Value +
                " Sense.Delay=" + SenseDelay.Value +
                " Sense.Range=" + SenseRange.Value +
                " Sense.ShowNames=" + SenseShowNames.Value +
                " Sense.MaxMarkers=" + SenseMaxMarkers.Value +
                " Sense.MoveGrace=" + SenseMoveGrace.Value +
                " Sense.MarkerSize=" + SenseMarkerSize.Value);

            WatchConfigFile();

            _harmony = new Harmony(ModGuid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            LogPatchStatus(typeof(InventoryGrid), "UpdateGui");
            LogPatchStatus(typeof(InventoryGui), "Awake");
            LogPatchStatus(typeof(InventoryGrid), "CreateItemTooltip");
            LogPatchStatus(typeof(InventoryGui), "OnRepairPressed");
            LogPatchStatus(typeof(Valheim.UI.RadialBase), "ConstructRadial");
            LogPatchStatus(typeof(Valheim.UI.RadialBase), "Awake");
            LogPatchStatus(typeof(RadialOverlapPreventer), "PreventOverlap");
            LogPatchStatus(typeof(Valheim.UI.ElementInfo), "Set");
            LogPatchStatus(typeof(OpenRadialConfig), "InitRadialConfig");

            Log.LogInfo($"{ModName} {ModVersion} loaded");
        }

        // Confirms Harmony actually attached a postfix to the named method,
        // instead of inferring it from later behavior. Checked once at load so
        // "is the patch even applied" is answered in the log before anything
        // else runs, not guessed at from downstream symptoms.
        private static void LogPatchStatus(System.Type type, string methodName)
        {
            System.Reflection.MethodBase method = AccessTools.Method(type, methodName);
            if (method == null)
            {
                Log.LogWarning("patch check: " + type.Name + "." + methodName + " not found, cannot check.");
                return;
            }

            HarmonyLib.Patches info = Harmony.GetPatchInfo(method);
            int postfixCount = info != null ? info.Postfixes.Count : 0;
            int prefixCount = info != null ? info.Prefixes.Count : 0;
            if (postfixCount > 0 || prefixCount > 0)
            {
                Log.LogInfo("patch check: " + type.Name + "." + methodName + " has " + prefixCount + " prefix, " + postfixCount + " postfix patch(es) applied.");
            }
            else
            {
                Log.LogWarning("patch check: " + type.Name + "." + methodName + " has NO patches applied. Harmony did not attach.");
            }
        }

        // BepInEx reads the config file once at load and never looks at it
        // again, so editing it by hand normally means restarting the game. A
        // watcher plus Config.Reload() makes a text editor a live tuning tool,
        // which matters because Configuration Manager draws with Unity's old
        // IMGUI at a fixed pixel size and has no scale setting of its own: on a
        // high-DPI display it is close to unreadable and cannot be fixed from
        // our side.
        private void WatchConfigFile()
        {
            try
            {
                string path = Config.ConfigFilePath;
                string dir = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir)) return;

                _configWatcher = new FileSystemWatcher(dir, Path.GetFileName(path));
                _configWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size;
                _configWatcher.Changed += OnConfigFileChanged;
                _configWatcher.Created += OnConfigFileChanged;
                _configWatcher.EnableRaisingEvents = true;

                Log.LogInfo("config: watching " + path + " for live edits (General.LiveConfigReload).");
            }
            catch (Exception e)
            {
                // Not worth taking the mod down over; it only costs live editing.
                Log.LogWarning("config: could not watch the config file, edits will need a restart. " +
                               e.GetType().Name + ": " + e.Message);
            }
        }

        // Fires on a background thread, so it only raises a flag. Reload() runs
        // SettingChanged handlers, and ours touch Unity objects, which is only
        // legal on the main thread.
        private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
        {
            _configDirty = true;
        }

        private void Update()
        {
            if (_configDirty)
            {
                _configDirty = false;
                // Editors and BepInEx's own save both write in bursts; each
                // write pushes the deadline out so one save is one reload.
                _reloadPending = true;
                _reloadAt = Time.realtimeSinceStartup + 0.25f;
            }

            if (!_reloadPending || Time.realtimeSinceStartup < _reloadAt) return;
            _reloadPending = false;

            if (LiveConfigReload == null || !LiveConfigReload.Value) return;

            try
            {
                Config.Reload();
                Log.LogInfo("config: reloaded from disk.");
            }
            catch (Exception ex)
            {
                Log.LogWarning("config: reload failed, keeping the values already loaded. " +
                               ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void OnDestroy()
        {
            if (_configWatcher != null)
            {
                _configWatcher.EnableRaisingEvents = false;
                _configWatcher.Dispose();
                _configWatcher = null;
            }

            _harmony?.UnpatchSelf();
        }
    }
}
