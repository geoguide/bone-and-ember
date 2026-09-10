using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace ValheimUI
{
    // Client-only: nothing here talks to the server, so it works on any server
    // without the server (or other players) installing it.
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string ModGuid = "com.geo.valheimui";
        public const string ModName = "ValheimUI";
        public const string ModVersion = "0.1.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> ShowWelcome;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;

            ShowWelcome = Config.Bind(
                "General",
                "ShowWelcomeMessage",
                true,
                "Show a message when you spawn, to confirm the mod is loaded.");

            _harmony = new Harmony(ModGuid);
            _harmony.PatchAll(typeof(Plugin).Assembly);

            Log.LogInfo($"{ModName} {ModVersion} loaded");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
