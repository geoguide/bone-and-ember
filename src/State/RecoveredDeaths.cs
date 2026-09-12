using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;

namespace BoneAndEmber
{
    // Slice 4 of docs/design/002-waypoint-strip.md: which of your deaths you have
    // already collected.
    //
    // Vanilla has nowhere to put this. A death pin carries a position, a day number
    // and a checkmark, and the checkmark already means "dealt with" to the player,
    // so we keep our own file rather than overloading it. Map pins live in the
    // character profile, so entries are keyed by world and character too.
    //
    // Recovery is recorded by position, matched with a radius: a tombstone can
    // drift several metres from where you died and can float, so exact coordinates
    // would never line up.
    internal static class RecoveredDeaths
    {
        // Generous on purpose. The grave spawns at the player's center point, can
        // drift 4 m from there (TombStone.PositionCheck) and can float away on
        // water, so a tight radius would miss its own pin.
        internal const float MatchRadius = 10f;

        private const string FileName = "BoneAndEmber.recovered-deaths.json";

        // Public, not private: JsonUtility silently serialized the private
        // nested versions as "{}", so nothing ever reached disk and every
        // launch started with zero recovered deaths (002, found in slice 9).
        [Serializable]
        public class Entry
        {
            public long world;
            public long player;
            public float x;
            public float z;
        }

        [Serializable]
        public class FileData
        {
            public List<Entry> deaths = new List<Entry>();
        }

        private static FileData _data;
        private static long _world;
        private static long _player;
        private static bool _sessionResolved;

        private static string Path => System.IO.Path.Combine(Paths.ConfigPath, FileName);

        internal static bool IsRecovered(Vector3 pos)
        {
            if (!EnsureLoaded()) return false;

            for (int i = 0; i < _data.deaths.Count; i++)
            {
                Entry e = _data.deaths[i];
                if (e.world != _world || e.player != _player) continue;

                float dx = e.x - pos.x;
                float dz = e.z - pos.z;
                if (dx * dx + dz * dz <= MatchRadius * MatchRadius) return true;
            }
            return false;
        }

        // Records the death pin nearest to pos as collected, and applies whatever
        // the config says should happen to the map pin. Safe to call repeatedly:
        // a position already recorded is ignored.
        internal static void MarkRecovered(Vector3 pos, string reason)
        {
            if (!EnsureLoaded()) return;

            Minimap.PinData pin = ClosestDeathPin(pos, MatchRadius);
            if (pin == null)
            {
                Plugin.Log.LogInfo("recovery: nothing to mark near " + Format(pos) + " (" + reason + "), no death pin within " +
                                   MatchRadius + " m");
                return;
            }

            if (IsRecovered(pin.m_pos)) return;

            _data.deaths.Add(new Entry { world = _world, player = _player, x = pin.m_pos.x, z = pin.m_pos.z });
            Save();

            Plugin.Log.LogInfo("recovery: death at " + Format(pin.m_pos) + " marked recovered (" + reason + ")");

            Minimap map = Minimap.instance;
            if (map == null) return;

            if (Plugin.RemoveRecoveredPins.Value)
            {
                map.RemovePin(pin);
                Plugin.Log.LogInfo("recovery: pin removed from the map (RemoveRecoveredPins is on, this edits the character save)");
            }
            else
            {
                // Nudge vanilla into redrawing so MinimapPinDimPatch can grey it on
                // the next pass instead of at the next map open.
                map.m_pinUpdateRequired = true;
            }
        }

        internal static Minimap.PinData ClosestDeathPin(Vector3 pos, float radius)
        {
            Minimap map = Minimap.instance;
            if (map == null || map.m_pins == null) return null;

            Minimap.PinData best = null;
            float bestDist = radius;

            for (int i = 0; i < map.m_pins.Count; i++)
            {
                Minimap.PinData pin = map.m_pins[i];
                if (pin == null || pin.m_type != Minimap.PinType.Death) continue;

                float d = Utils.DistanceXZ(pos, pin.m_pos);
                if (d <= bestDist)
                {
                    best = pin;
                    bestDist = d;
                }
            }
            return best;
        }

        // World and character are only known once ZNet and the profile are up, and
        // they change when you load a different world, so re-resolve rather than
        // caching once for the process.
        private static bool EnsureLoaded()
        {
            ZNet net = ZNet.instance;
            Game game = Game.instance;
            if (net == null || game == null) return false;

            long world = net.GetWorldUID();
            long player = game.GetPlayerProfile() != null ? game.GetPlayerProfile().GetPlayerID() : 0L;
            if (world == 0L || player == 0L) return false;

            if (_sessionResolved && world == _world && player == _player && _data != null) return true;

            _world = world;
            _player = player;
            _sessionResolved = true;
            Load();
            return _data != null;
        }

        private static void Load()
        {
            try
            {
                if (!File.Exists(Path))
                {
                    _data = new FileData();
                    Plugin.Log.LogInfo("recovery: no state file yet, starting empty (" + Path + ")");
                    return;
                }

                string json = File.ReadAllText(Path);
                _data = JsonUtility.FromJson<FileData>(json) ?? new FileData();
                if (_data.deaths == null) _data.deaths = new List<Entry>();

                int mine = 0;
                for (int i = 0; i < _data.deaths.Count; i++)
                {
                    if (_data.deaths[i].world == _world && _data.deaths[i].player == _player) mine++;
                }
                Plugin.Log.LogInfo("recovery: loaded " + _data.deaths.Count + " entries, " + mine +
                                   " for this world and character");
            }
            catch (Exception e)
            {
                // A corrupt state file must never cost the player their session; the
                // worst case is every death shows as live again.
                _data = new FileData();
                Plugin.Log.LogWarning("recovery: could not read " + Path + ", starting empty. " +
                                      e.GetType().Name + ": " + e.Message);
            }
        }

        private static void Save()
        {
            try
            {
                string json = JsonUtility.ToJson(_data, true);
                File.WriteAllText(Path, json);
                Plugin.Log.LogInfo("recovery: saved " + _data.deaths.Count + " entries (" + json.Length + " chars) to " + Path);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("recovery: could not write " + Path + ". " +
                                      e.GetType().Name + ": " + e.Message);
            }
        }

        private static string Format(Vector3 pos)
        {
            return "(" + pos.x.ToString("0") + ", " + pos.z.ToString("0") + ")";
        }
    }
}
