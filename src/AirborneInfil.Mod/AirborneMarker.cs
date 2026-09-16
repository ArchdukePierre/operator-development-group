// ODG Operations - AirborneInfil
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace AirborneInfil
{

    internal static class AirborneMarker
    {
        const string LABEL = "AIRBORNE 1";
        static int _throttle;
        static int _airIdx = -1;
        static bool _autoDumped;
        static bool _tooFewLogged;

        internal static void Init(AirborneMod mod) { }

        internal static void OnSceneLoad()
        {

            _airIdx = -1;
            _autoDumped = false;
            _tooFewLogged = false;
        }

        internal static void Update()
        {
            if (!AirborneMod.MarkersOn) return;
            if (!AirborneBoard.HijackMode)
            {

                if (_airIdx >= 0) { RestoreAllLabels(); _airIdx = -1; }
                return;
            }
            bool inMission = false;
            try { inMission = Il2Cpp.InfiltrationManager.instance != null; } catch { }
            if (inMission) return;

            if (++_throttle % 60 != 0) return;
            TryHijack();
            if (!_autoDumped && _throttle > 600) { _autoDumped = true; DumpEverything("auto"); }
        }

        static void TryHijack()
        {
            try
            {
                var arr = Resources.FindObjectsOfTypeAll(Il2CppType.Of<Il2Cpp.MapInfilMarker>());
                if (arr == null || arr.Length == 0) return;

                Il2Cpp.MapInfilMarker pick = null; int pickRank = 99, idx = -1;

                var seen = new HashSet<int>();
                for (int i = 0; i < arr.Length; i++)
                {
                    var m = arr[i].TryCast<Il2Cpp.MapInfilMarker>(); if (m == null) continue;
                    GameObject go = null; try { go = m.gameObject; } catch { }
                    if (go == null || !go.activeInHierarchy) continue;

                    bool ground = false; string nm = ""; int mi = -1;
                    try { ground = m.IsGroundInfil; } catch { }
                    try { nm = m.InfilName ?? ""; } catch { }
                    try { mi = m.MarkerIndex; } catch { }
                    if (!ground || mi < 0) continue;
                    seen.Add(mi);

                    int rank = nm.IndexOf("ALPHA", StringComparison.OrdinalIgnoreCase) >= 0 ? 0
                             : nm.IndexOf("BRAVO", StringComparison.OrdinalIgnoreCase) >= 0 ? 1 : 2;

                    if (rank < pickRank || (rank == pickRank && mi > idx)) { pick = m; pickRank = rank; idx = mi; }
                }

                if (pick == null || idx < 0) return;

                if (seen.Count < AirborneBoard.MinGroundToHijack)
                {
                    if (!_tooFewLogged)
                    {
                        _tooFewLogged = true;
                        AirborneMod.Log("[marker] board has only " + seen.Count + " walk-in(s), need " +
                                        AirborneBoard.MinGroundToHijack + " to hijack one - left alone. " +
                                        "Set AirborneMode=Add to get an airborne point here.");
                    }
                    if (_airIdx >= 0) { RestoreAllLabels(); _airIdx = -1; SteerableDrop.SetAirborneIndex(-1); }
                    return;
                }

                if (idx != _airIdx)
                {
                    _airIdx = idx;
                    SteerableDrop.SetAirborneIndex(idx);
                    string picked = "?"; try { picked = pick.InfilName; } catch { }
                    AirborneMod.Log("[marker] hijacking walk-in '" + picked + "' (idx " + idx + ") -> " + LABEL);
                }

                for (int i = 0; i < arr.Length; i++)
                {
                    var m = arr[i].TryCast<Il2Cpp.MapInfilMarker>(); if (m == null) continue;
                    GameObject go = null; try { go = m.gameObject; } catch { }
                    if (go == null || !go.activeInHierarchy) continue;
                    int mi = -1; try { mi = m.MarkerIndex; } catch { }
                    if (mi == _airIdx) Relabel(m); else RestoreIfLabelled(m);
                }
            }
            catch (Exception e) { AirborneMod.Log("[marker] hijack err: " + e.Message); }
        }

        static void Relabel(Il2Cpp.MapInfilMarker m)
        {
            try { var t = m.TextUI; if (t != null && t.text != LABEL) t.text = LABEL; } catch { }
        }

        static void RestoreAllLabels()
        {
            try
            {
                var arr = Resources.FindObjectsOfTypeAll(Il2CppType.Of<Il2Cpp.MapInfilMarker>());
                if (arr == null) return;
                for (int i = 0; i < arr.Length; i++)
                {
                    var m = arr[i].TryCast<Il2Cpp.MapInfilMarker>();
                    if (m != null) RestoreIfLabelled(m);
                }
                AirborneMod.Log("[marker] labels restored");
            }
            catch (Exception e) { AirborneMod.Log("[marker] restore err: " + e.Message); }
        }

        static void RestoreIfLabelled(Il2Cpp.MapInfilMarker m)
        {
            try
            {
                var t = m.TextUI; if (t == null || t.text != LABEL) return;
                string nm = ""; try { nm = m.InfilName ?? ""; } catch { }
                if (nm.Length > 0) t.text = nm;
            }
            catch { }
        }

        static void DumpEverything(string via)
        {
            try
            {
                var arr = Resources.FindObjectsOfTypeAll(Il2CppType.Of<Il2Cpp.MapInfilMarker>());
                if (arr == null) { AirborneMod.Log("[dump] no markers"); return; }
                AirborneMod.Log("[dump] ===== " + via + ": " + arr.Length + " MapInfilMarker(s) =====");
                int n = 0;
                for (int i = 0; i < arr.Length && n < 400; i++)
                {
                    var m = arr[i].TryCast<Il2Cpp.MapInfilMarker>(); if (m == null) continue;
                    GameObject go = null; try { go = m.gameObject; } catch { }
                    if (go == null) continue;
                    int idx = -1; string nm = ""; bool ground = false, exfil = false, heli = false, sel = false, act = false;
                    try { idx = m.MarkerIndex; } catch { }
                    try { nm = m.InfilName ?? ""; } catch { }
                    try { ground = m.IsGroundInfil; } catch { }
                    try { exfil = m.IsExfil; } catch { }
                    try { heli = m.IsHeliInfil; } catch { }
                    try { sel = m.isSelectedLocal; } catch { }
                    try { act = go.activeInHierarchy; } catch { }
                    string label = ""; try { var t = m.TextUI; if (t != null) label = t.text ?? ""; } catch { }
                    AirborneMod.Log("[dump] [" + idx + "] '" + nm + "' label='" + label + "'" +
                                    (ground ? " GROUND" : "") + (heli ? " HELI" : "") + (exfil ? " EXFIL" : "") +
                                    (act ? " active" : " inactive") + (sel ? " SELECTED" : "") +
                                    "  @ " + PathOf(go.transform));
                    n++;
                }
                AirborneMod.Log("[dump] ===== end (" + n + " listed) =====");
            }
            catch (Exception e) { AirborneMod.Log("[dump] err: " + e.Message); }
        }

        static string PathOf(Transform t)
        {
            try
            {
                var parts = new List<string>();
                var cur = t; int guard = 0;
                while (cur != null && guard++ < 14) { parts.Add(cur.name); cur = cur.parent; }
                parts.Reverse();
                return string.Join("/", parts.ToArray());
            }
            catch { return "?"; }
        }
    }
}
