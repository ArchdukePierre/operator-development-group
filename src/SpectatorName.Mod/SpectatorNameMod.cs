// ODG Operations - SpectatorName
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(SpectatorName.SpectatorNameMod), "ODG Spectator Name", "1.0.0", "ArchdukePierre")]
[assembly: MelonGame(null, null)]

namespace SpectatorName
{

    public class SpectatorNameMod : MelonMod
    {
        private MelonPreferences_Category _cat;
        private MelonPreferences_Entry<bool> _enabled;
        private MelonPreferences_Entry<float> _bottomOffset;
        private MelonPreferences_Entry<float> _scale;
        private MelonPreferences_Entry<bool> _showLabel;
        private MelonPreferences_Entry<string> _dumpKeyCfg;

        private KeyCode _dumpKey = KeyCode.None;

        private Nameplate _plate;

        private const float ResolveSeconds = 0.1f;
        private float _nextResolve;
        private string _shown;

        public override void OnInitializeMelon()
        {
            _cat = MelonPreferences.CreateCategory("SpectatorName");

            _enabled = _cat.CreateEntry("Enabled", true,
                description: "Show the name of the player you are spectating.");
            _bottomOffset = _cat.CreateEntry("BottomOffset", 150f,
                description: "Pixels up from the bottom edge (1080p reference).");
            _scale = _cat.CreateEntry("Scale", 1f,
                description: "Overall size multiplier.");
            _showLabel = _cat.CreateEntry("ShowLabel", true,
                description: "Draw the small SPECTATING caption above the name.");
            _dumpKeyCfg = _cat.CreateEntry("DumpKey", "J",
                description: "Logs every player and their helmet cam state to the console. "
                           + "Diagnostic only. Set to None to disable.");

            _dumpKey = ParseKey(_dumpKeyCfg.Value, KeyCode.None);

            LoggerInstance.Msg("Spectator name ready. Press F while dead to cycle; the name "
                             + "appears at the bottom of the screen.");
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {

            _shown = null;
            _nextResolve = 0f;
            if (_plate != null) _plate.SetVisible(false);
        }

        public override void OnUpdate()
        {
            try { Tick(); }
            catch (Exception e) { LoggerInstance.Warning("spectator name tick: " + e.Message); }
        }

        private void Tick()
        {
            if (_dumpKey != KeyCode.None && Input.GetKeyDown(_dumpKey)) Dump();

            if (!_enabled.Value) { Hide(); return; }

            var me = Roster.Local();
            if (me == null) { Hide(); return; }

            if (Roster.SpawnedObject(me) != null) { Hide(); return; }

            if (Time.unscaledTime >= _nextResolve)
            {
                _nextResolve = Time.unscaledTime + ResolveSeconds;

                var found = Roster.SpectatedName();
                if (found != _shown)
                {
                    _shown = found;
                    if (!string.IsNullOrEmpty(found)) LoggerInstance.Msg("Spectating " + found + ".");
                }
            }

            if (string.IsNullOrEmpty(_shown))
            {
                if (_plate != null) _plate.SetVisible(false);
                return;
            }

            if (_plate == null) _plate = new Nameplate();
            _plate.Apply(_bottomOffset.Value, _scale.Value, _showLabel.Value);
            _plate.SetName(_shown);
            _plate.SetVisible(true);
        }

        private void Hide()
        {
            _shown = null;
            if (_plate != null) _plate.SetVisible(false);
        }

        private void Dump()
        {
            try
            {
                var me = Roster.Local();
                int idx = -1;
                try { var gm = Il2Cpp.GameManager.instance; if (gm != null) idx = gm.helmetCamIndex; } catch { }

                LoggerInstance.Msg("[dump] localPlayerMaster={0} localSpawnedObject={1} helmetCamIndex={2}",
                    me == null ? "null" : "ok",
                    (me != null && Roster.SpawnedObject(me) != null) ? "alive" : "none (dead)",
                    idx);

                var list = Roster.All();
                if (list == null) { LoggerInstance.Msg("[dump] roster: null"); return; }

                LoggerInstance.Msg("[dump] roster: {0} entries", list.Count);
                for (int i = 0; i < list.Count; i++)
                {
                    var pm = list[i];
                    if (pm == null) { LoggerInstance.Msg("[dump]   #{0} <null>", i); continue; }

                    string nm = "?";    try { nm = pm.thisPlayerName; } catch { }
                    string loc = "?";   try { loc = pm.isLocalPlayer.ToString(); } catch { }
                    string alive = "?"; try { alive = pm.currentlySpawnedAndAlive.ToString(); } catch { }

                    var pn = Roster.SpawnedObject(pm);
                    string cam = "no spawned object";
                    if (pn != null)
                    {
                        GameObject hc = null; try { hc = pn.HelmetCam; } catch { }
                        if (hc == null) cam = "HelmetCam null";
                        else
                        {
                            string self = "?", inh = "?";
                            try { self = hc.activeSelf.ToString(); } catch { }
                            try { inh = hc.activeInHierarchy.ToString(); } catch { }
                            cam = "HelmetCam " + hc.name + " activeSelf=" + self + " activeInHierarchy=" + inh;
                        }
                    }

                    LoggerInstance.Msg("[dump]   #{0} name={1} local={2} alive={3} {4}", i, nm, loc, alive, cam);
                }
            }
            catch (Exception e) { LoggerInstance.Warning("dump: " + e.Message); }
        }

        private static KeyCode ParseKey(string s, KeyCode fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            try { KeyCode k; if (Enum.TryParse<KeyCode>(s.Trim(), true, out k)) return k; } catch { }
            return fallback;
        }
    }
}
