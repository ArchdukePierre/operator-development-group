// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;

namespace MapLoader
{

    public static class BoardState
    {
        sealed class Saved
        {
            public Il2Cpp.OperationBoardUI Ui;

            public Il2Cpp.LocalizationTMPEvent SitrepLoc;
            public bool SitrepLocEnabled;
            public string SitrepText;
            public bool HasSitrep;
            public string WroteSitrep;

            public bool[] Sim, Hvt, Pvp, Ffa;
        }

        static readonly Dictionary<int, Saved> _saved = new Dictionary<int, Saved>();

        static readonly Dictionary<int, bool> _lastMode = new Dictionary<int, bool>();

        public static bool Reasserting;

        static MelonLogger.Instance _log;

        public static void Init(MelonLogger.Instance log) { _log = log; }

        public static void NoteMode(Il2Cpp.OperationBoardUI ui, bool simulation)
        {
            if (ui == null) return;
            try { _lastMode[ui.GetInstanceID()] = simulation; } catch { }
        }

        public static void Remember(Il2Cpp.OperationBoardUI ui)
        {
            if (ui == null) return;
            int id;
            try { id = ui.GetInstanceID(); } catch { return; }
            if (_saved.ContainsKey(id)) return;

            var s = new Saved { Ui = ui };
            try
            {
                s.SitrepLoc = ui.SituationReportText;
                if (s.SitrepLoc != null)
                {
                    s.SitrepLocEnabled = s.SitrepLoc.enabled;
                    var t = s.SitrepLoc.GetComponent<Il2CppTMPro.TextMeshProUGUI>();
                    if (t != null) { s.SitrepText = t.text; s.HasSitrep = true; }
                }
            }
            catch { }
            s.Sim = Snapshot(Get(() => ui.SimulationParameters));
            s.Hvt = Snapshot(Get(() => ui.HVTSimulationParameters));
            s.Pvp = Snapshot(Get(() => ui.PVPParameters));
            s.Ffa = Snapshot(Get(() => ui.FFAParameters));

            _saved[id] = s;
        }

        public static void NoteWrites(Il2Cpp.OperationBoardUI ui)
        {
            if (ui == null) return;
            int id;
            try { id = ui.GetInstanceID(); } catch { return; }
            Saved s;
            if (!_saved.TryGetValue(id, out s) || s == null) return;
            try
            {
                if (s.SitrepLoc != null)
                {
                    var t = s.SitrepLoc.GetComponent<Il2CppTMPro.TextMeshProUGUI>();
                    if (t != null) s.WroteSitrep = t.text;
                }
            }
            catch { }
        }

        public static void RestoreFor(Il2Cpp.OperationBoardUI ui, bool reassert)
        {
            if (ui == null) return;
            int id;
            try { id = ui.GetInstanceID(); } catch { return; }
            Saved s;
            if (!_saved.TryGetValue(id, out s) || s == null) return;
            _saved.Remove(id);

            Apply(Get(() => ui.SimulationParameters), s.Sim);
            Apply(Get(() => ui.HVTSimulationParameters), s.Hvt);
            Apply(Get(() => ui.PVPParameters), s.Pvp);
            Apply(Get(() => ui.FFAParameters), s.Ffa);

            RebuildMarkers(ui);

            try { if (s.SitrepLoc != null) s.SitrepLoc.enabled = true; } catch { }

            if (reassert)
            {

                try
                {
                    bool sim;
                    if (_lastMode.TryGetValue(id, out sim))
                    {
                        Reasserting = true;
                        if (sim) ui.SwitchToSimulationOperationUI();
                        else ui.SwitchToActiveOperationUI();
                    }
                }
                catch { }
                finally { Reasserting = false; }
            }
            _log?.Msg("[board] restored ui#" + id);
        }

        static void RebuildMarkers(Il2Cpp.OperationBoardUI ui)
        {
            try
            {
                var main = Collect(Get2(() => ui.MapParent));
                var full = Collect(Get2(() => ui.FullscreenMapParent));
                if (main != null) ui.MapInfilMarkers = main;
                if (full != null) ui.FullscreenMapInfilMarkers = full;
                if (main != null)
                    _log?.Msg("[board] marker arrays rebuilt from the live map ("
                            + main.Length + " main)");

            }
            catch (Exception e) { _log?.Warning("[board] RebuildMarkers: " + e.Message); }
        }

        static Il2CppReferenceArray<Il2Cpp.MapInfilMarker> Collect(Transform parent)
        {
            if (parent == null) return null;

            var markers = parent.GetComponentsInChildren<Il2Cpp.MapInfilMarker>(false);
            if (markers == null) return null;
            var live = new List<Il2Cpp.MapInfilMarker>();
            foreach (var m in markers)
            {
                if (m == null) continue;
                GameObject go = null;
                try { go = m.gameObject; } catch { }
                if (go == null) continue;
                if (go.name.StartsWith(MapInfils.ObjectName, StringComparison.Ordinal))
                    continue;
                live.Add(m);
            }
            if (live.Count == 0) return null;
            var arr = new Il2CppReferenceArray<Il2Cpp.MapInfilMarker>(live.Count);
            for (int i = 0; i < live.Count; i++) arr[i] = live[i];
            return arr;
        }

        static Il2CppReferenceArray<GameObject> Get(Func<Il2CppReferenceArray<GameObject>> f)
        {
            try { return f(); } catch { return null; }
        }

        static Transform Get2(Func<Transform> f)
        {
            try { return f(); } catch { return null; }
        }

        static bool[] Snapshot(Il2CppReferenceArray<GameObject> group)
        {
            try
            {
                if (group == null) return null;
                var b = new bool[group.Length];
                for (int i = 0; i < group.Length; i++)
                {
                    var go = group[i];
                    b[i] = go != null && go.activeSelf;
                }
                return b;
            }
            catch { return null; }
        }

        static void Apply(Il2CppReferenceArray<GameObject> group, bool[] want)
        {
            try
            {
                if (group == null || want == null) return;
                int n = Math.Min(group.Length, want.Length);
                for (int i = 0; i < n; i++)
                {
                    var go = group[i];
                    if (go != null && go.activeSelf != want[i]) go.SetActive(want[i]);
                }
            }
            catch { }
        }

        public static void OnSceneGone()
        {
            _saved.Clear();
            _lastMode.Clear();
        }
    }
}
