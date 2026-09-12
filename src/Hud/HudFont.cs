using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace BoneAndEmber
{
    // Slice 8 font attempt: build a TextMeshPro font asset at runtime from a font
    // installed on this machine (FontName in config), and fall back to vanilla's
    // font when that isn't possible. A bundled TTF needs a Unity AssetBundle and
    // is a later project. Resolution is lazy and re-runs when FontName changes.
    internal static class HudFont
    {
        internal static int Version { get; private set; }

        private static TMP_FontAsset _asset;
        private static string _triedName;
        private static bool _tried;

        internal static TMP_FontAsset Current
        {
            get
            {
                Refresh();
                return _asset;
            }
        }

        internal static void Apply(TMP_Text text)
        {
            if (text == null) return;
            TMP_FontAsset asset = Current;
            if (asset != null && text.font != asset) text.font = asset;
        }

        internal static void ApplyAll(Transform root)
        {
            if (root == null) return;
            TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++) Apply(texts[i]);
        }

        // Looks for <Family>-Regular.ttf (spaces stripped) or <Family>.ttf in the
        // usual font folders: where Font Book installs on a Mac, and both the
        // system and per-user folders on Windows. Windows matters because
        // double-clicking a font and choosing Install, without admin rights,
        // puts it in the per-user one and nowhere else. Missing directories are
        // skipped, so the whole list is safe to try on either platform.
        private static string FindFontFile(string family)
        {
            string compact = family.Replace(" ", "");
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] dirs =
            {
                Path.Combine(home, "Library/Fonts"),
                "/Library/Fonts",
                "/System/Library/Fonts",
                Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
                string.IsNullOrEmpty(localAppData) ? "" : Path.Combine(localAppData, "Microsoft/Windows/Fonts"),
            };
            string[] names = { compact + "-Regular.ttf", compact + ".ttf", family + "-Regular.ttf", family + ".ttf", compact + "-Regular.otf", compact + ".otf" };
            for (int d = 0; d < dirs.Length; d++)
            {
                if (string.IsNullOrEmpty(dirs[d]) || !Directory.Exists(dirs[d])) continue;
                for (int n = 0; n < names.Length; n++)
                {
                    string path = Path.Combine(dirs[d], names[n]);
                    if (File.Exists(path)) return path;
                }
            }
            return null;
        }

        // Re-resolves only when the configured name changed since last time.
        internal static void Refresh()
        {
            string name = Plugin.FontName != null ? Plugin.FontName.Value : null;
            name = name != null ? name.Trim() : "";
            if (_tried && _triedName == name) return;

            _tried = true;
            _triedName = name;
            _asset = null;
            Version++;

            if (name.Length == 0)
            {
                Plugin.Log.LogInfo("font: FontName is empty, keeping vanilla's font.");
                return;
            }

            try
            {
                string[] installed = Font.GetOSInstalledFontNames();
                string match = null;
                for (int i = 0; i < installed.Length; i++)
                {
                    if (string.Equals(installed[i], name, StringComparison.OrdinalIgnoreCase))
                    {
                        match = installed[i];
                        break;
                    }
                }

                if (match == null)
                {
                    Plugin.Log.LogWarning(
                        "font: \"" + name + "\" is not installed on this machine (" + installed.Length +
                        " OS fonts listed), keeping vanilla's font. Install the TTF system-wide and set FontName to its exact family name.");
                    return;
                }

                // Not the Font overload: TMP_FontAsset.CreateFontAsset(Font) needs
                // embedded font data ("Include Font Data"), which an OS-loaded Font
                // never has, so it returns null (seen in game). The family/style
                // overload is TMP's own system-font route: it resolves the font's
                // file on disk and builds a DynamicOS atlas from it.
                TMP_FontAsset asset = null;
                string how = null;
                string[] styles = { "Regular", "Normal", "Medium", "" };
                for (int i = 0; i < styles.Length && asset == null; i++)
                {
                    asset = TMP_FontAsset.CreateFontAsset(match, styles[i], 90);
                    if (asset != null) how = "system font reference, style \"" + styles[i] + "\"";
                }

                // Fallback: find the TTF ourselves in the usual font folders and
                // load it by path. Same dynamic atlas, just without relying on the
                // OS reporting a style name TMP recognises.
                if (asset == null)
                {
                    string file = FindFontFile(match);
                    if (file != null)
                    {
                        asset = TMP_FontAsset.CreateFontAsset(file, 0, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024);
                        if (asset != null) how = "file " + file;
                    }
                    else
                    {
                        Plugin.Log.LogInfo("font: no TTF for \"" + match + "\" found in the user or system font folders either.");
                    }
                }

                if (asset == null)
                {
                    Plugin.Log.LogWarning("font: TMP could not build a font asset for \"" + match + "\" by family/style or by file, keeping vanilla's font.");
                    return;
                }

                asset.name = "BoneAndEmber_" + match.Replace(' ', '_');
                _asset = asset;
                Plugin.Log.LogInfo(
                    "font: using \"" + match + "\" via " + how + " (" + asset.name +
                    ", atlas " + asset.atlasWidth + "x" + asset.atlasHeight + ", " + asset.atlasPopulationMode + ").");
            }
            catch (Exception e)
            {
                _asset = null;
                Plugin.Log.LogWarning("font: runtime font creation threw, keeping vanilla's font. " + e.GetType().Name + ": " + e.Message);
            }
        }
    }
}
