// ODG Operations - AirborneInfil
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace AirborneInfil
{

    internal static class AirborneBoard
    {
        internal const string MarkerName = "ODGAIR_MARKER";

        static AirborneMod Mod;
        static MelonPreferences_Entry<string> _mode, _alias, _label, _excludeBoards, _groundCallsigns;
        static MelonPreferences_Entry<float> _offU, _offV, _offScale;
        static MelonPreferences_Entry<int> _minGround;

        static readonly List<GameObject> _touched = new List<GameObject>();

        static readonly HashSet<int> _excluded = new HashSet<int>();
        static readonly HashSet<string> _excludeLogged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<int> _rejectLogged = new HashSet<int>();
        static int _index = -1;
        static int _throttle;
        static bool _noAliasLogged;
        static int _lastCensusId;
        static int _lastVisualId;

        static Il2Cpp.MapInfilMarker _liveBoard, _liveDeploy;

        internal static bool AddMode => _mode != null
            && string.Equals(_mode.Value, "Add", StringComparison.OrdinalIgnoreCase);
        internal static bool HijackMode => _mode != null
            && string.Equals(_mode.Value, "Hijack", StringComparison.OrdinalIgnoreCase);
        internal static int MinGroundToHijack => _minGround != null ? _minGround.Value : 2;

        internal static void Init(AirborneMod mod)
        {
            Mod = mod;
            var c = MelonPreferences.CreateCategory("AirborneInfil");
            _mode      = c.CreateEntry("AirborneMode", "Add");
            _alias     = c.CreateEntry("AirborneAliasPoint", "");
            _label     = c.CreateEntry("AirborneMarkerLabel", "AIRBORNE 1");
            _offU      = c.CreateEntry("AirborneMarkerOffsetU", 0.035f);
            _offV      = c.CreateEntry("AirborneMarkerOffsetV", 0.035f);
            _offScale  = c.CreateEntry("AirborneMarkerOffsetScale", 400f);
            _minGround = c.CreateEntry("MinGroundPointsToHijack", 2);

            _excludeBoards   = c.CreateEntry("AirborneExcludeBoards", "Dark Waters");

            _groundCallsigns = c.CreateEntry("AirborneGroundCallsigns", "ALPHA,BRAVO,DELTA");

            Prefix(typeof(Il2Cpp.OperationBoardUI), "Setup", nameof(Pre_Setup));
            Postfix(typeof(Il2Cpp.OperationBoardUI), "Setup", nameof(Post_Setup));
            Prefix(typeof(Il2Cpp.InfilSelectorDisplayer), "SpawnMap", nameof(Pre_SpawnMap));
            Postfix(typeof(Il2Cpp.InfilSelectorDisplayer), "SpawnMap", nameof(Post_SpawnMap));

            AirborneMod.Log("[board] mode=" + _mode.Value + " alias='" + _alias.Value +
                            "' label='" + _label.Value + "'");
        }

        static void Prefix(Type t, string m, string handler)
        {
            try
            {
                var mi = AccessTools.Method(t, m);
                if (mi == null) { AirborneMod.Log("[board] HOOK MISS " + t.Name + "." + m); return; }
                Mod.HarmonyInstance.Patch(mi, prefix: new HarmonyMethod(typeof(AirborneBoard), handler));
                AirborneMod.Log("[board] prefixed " + t.Name + "." + m);
            }
            catch (Exception e) { AirborneMod.Log("[board] HOOK FAIL " + t.Name + "." + m + ": " + e.Message); }
        }

        static void Postfix(Type t, string m, string handler)
        {
            try
            {
                var mi = AccessTools.Method(t, m);
                if (mi == null) { AirborneMod.Log("[board] HOOK MISS " + t.Name + "." + m); return; }
                Mod.HarmonyInstance.Patch(mi, postfix: new HarmonyMethod(typeof(AirborneBoard), handler));
                AirborneMod.Log("[board] postfixed " + t.Name + "." + m);
            }
            catch (Exception e) { AirborneMod.Log("[board] HOOK FAIL " + t.Name + "." + m + ": " + e.Message); }
        }

        static void Pre_Setup(Il2Cpp.CerebusOpboard opboard)
        {
            if (!Ready()) return;
            GameObject prefab = null; string board = "?";
            try { prefab = opboard != null ? opboard.MapPrefab : null; } catch { }
            try { board = opboard != null ? (opboard.name ?? "?") : "?"; } catch { }
            if (prefab == null) return;

            if (IsExcludedBoard(board))
            {

                _excluded.Add(SafeId(prefab));
                if (_excludeLogged.Add(board))
                    AirborneMod.Log("[board] '" + board + "' is on AirborneExcludeBoards - no airborne point");
                return;
            }
            Inject(prefab, "board", board);
        }

        static bool IsOdgMap(GameObject mapPrefab)
        {
            try
            {
                var n = mapPrefab != null ? mapPrefab.name : null;
                return n != null && n.StartsWith("ODG_", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        static bool IsExcludedBoard(string board)
        {
            if (string.IsNullOrEmpty(board)) return false;
            if (board.StartsWith("ODG_", StringComparison.OrdinalIgnoreCase)) return true;
            string list = ""; try { list = _excludeBoards.Value ?? ""; } catch { }
            foreach (var raw in list.Split(','))
            {
                var s = raw.Trim();
                if (s.Length > 0 && board.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        static void Post_Setup(Il2Cpp.OperationBoardUI __instance)
        {
            if (!Ready()) return;
            try { ReadBack(__instance != null ? __instance.MapInfilMarkers : null, "board", true); } catch { }
        }

        static void Pre_SpawnMap(GameObject mapPrefab)
        {
            if (!Ready()) return;
            Inject(mapPrefab, "deploy", "?");
        }

        static void Post_SpawnMap(Il2Cpp.InfilSelectorDisplayer __instance)
        {
            if (!Ready()) return;
            try { ReadBack(__instance != null ? __instance.MapInfilMarkers : null, "deploy", true); } catch { }
        }

        static bool Ready() => AirborneMod.MasterEnabled && AirborneMod.MarkersOn && AddMode;

        internal static void Update()
        {
            if (!Ready()) return;

            try { if (_liveBoard != null) Relabel(_liveBoard); else _liveBoard = null; }
            catch { _liveBoard = null; }
            try { if (_liveDeploy != null) Relabel(_liveDeploy); else _liveDeploy = null; }
            catch { _liveDeploy = null; }

            if (++_throttle % 60 != 0) return;
            try
            {
                var d = Il2Cpp.InfilSelectorDisplayer.instance;
                if (d != null) ReadBack(d.MapInfilMarkers, "poll", false);
            }
            catch { }
        }

        internal static void OnSceneLoad()
        {
            _noAliasLogged = false;

        }

        static bool Inject(GameObject mapPrefab, string via, string board)
        {
            if (mapPrefab == null) return false;
            if (IsOdgMap(mapPrefab)) return false;
            if (_excluded.Contains(SafeId(mapPrefab))) return false;
            try
            {
                Il2CppArrayBase<Il2Cpp.MapInfilMarker> all = null;
                try { all = mapPrefab.GetComponentsInChildren<Il2Cpp.MapInfilMarker>(true); } catch { }
                if (all == null || all.Length == 0) return false;

                for (int i = 0; i < all.Length; i++)
                {
                    var m = all[i]; if (m == null) continue;
                    GameObject g = null; try { g = m.gameObject; } catch { }
                    if (g != null && g.name == MarkerName) return true;
                }

                var alias = PickAlias(all, SafeId(mapPrefab));
                if (alias == null)
                {
                    if (!_noAliasLogged)
                    {
                        _noAliasLogged = true;
                        AirborneMod.Log("[board] '" + mapPrefab.name + "' (board '" + board + "') has no " +
                                        "walk-in to alias (heli/vehicle-only map) - no airborne point added");
                    }
                    return false;
                }

                string aliasName = ""; try { aliasName = alias.InfilName ?? ""; } catch { }
                if (aliasName.Length == 0)
                {
                    AirborneMod.Log("[board] alias marker has no InfilName - refusing to add a point " +
                                    "the host cannot resolve");
                    return false;
                }

                var src = alias.gameObject;
                var want = src.transform.parent;
                GameObject go = UObject.Instantiate(src, want, false);
                go.name = MarkerName;
                try { go.hideFlags = src.hideFlags; } catch { }
                go.SetActive(true);

                try { if (go.transform.parent != want) go.transform.SetParent(want, false); } catch { }
                if (go.transform.parent != want)
                {
                    AirborneMod.Log("[board] clone would not parent under '" +
                                    (want != null ? want.name : "<null>") + "' - discarded, no point added");
                    try { UObject.Destroy(go); } catch { }
                    return false;
                }
                try { go.transform.SetAsLastSibling(); } catch { }

                OffsetRect(go, src, via);

                var mk = go.GetComponent<Il2Cpp.MapInfilMarker>();
                if (mk == null) { UObject.Destroy(go); return false; }

                try { mk.InfilName = aliasName; } catch { }
                try { mk.IsGroundInfil = true; } catch { }
                try { mk.IsHeliInfil = false; } catch { }
                try { mk.IsExfil = false; } catch { }
                try { mk.isSelectedLocal = false; } catch { }
                try { mk.MaxPlayers = alias.MaxPlayers > 0 ? alias.MaxPlayers : 8; } catch { }

                try { mk.MarkerIndex = -1; } catch { }

                try
                {
                    var t = mk.TextUI;
                    if (t != null)
                    {
                        t.text = _label.Value;

                        var loc = t.GetComponent<Il2Cpp.LocalizationTMPEvent>();
                        if (loc != null) loc.enabled = false;
                    }
                }
                catch { }

                if (!_touched.Contains(mapPrefab)) _touched.Add(mapPrefab);

                int after = -1;
                try { var a2 = mapPrefab.GetComponentsInChildren<Il2Cpp.MapInfilMarker>(true); after = a2 != null ? a2.Length : -1; } catch { }
                bool liveOK = false;
                try { liveOK = go.activeInHierarchy || go.activeSelf; } catch { }
                AirborneMod.Log("[board] added '" + _label.Value + "' to '" + mapPrefab.name +
                                "' #" + SafeId(mapPrefab) + " board='" + board + "' (" + via +
                                "), aliasing walk-in '" + aliasName + "' | markers " + all.Length + " -> " + after +
                                ", clone active=" + liveOK + " parent='" +
                                (go.transform.parent != null ? go.transform.parent.name : "<none>") + "'");
                return true;
            }
            catch (Exception e) { AirborneMod.Log("[board] inject err: " + e.Message); return false; }
        }

        static Il2Cpp.MapInfilMarker PickAlias(Il2CppArrayBase<Il2Cpp.MapInfilMarker> all, int prefabId)
        {
            string want = "";
            try { want = _alias.Value ?? ""; } catch { }

            Il2Cpp.MapInfilMarker first = null;
            for (int i = 0; i < all.Length; i++)
            {
                var m = all[i]; if (m == null) continue;
                GameObject g = null; try { g = m.gameObject; } catch { }
                if (g == null || g.name == MarkerName) continue;

                bool ground = false, heli = false; string nm = "";
                try { ground = m.IsGroundInfil; } catch { }
                try { heli = m.IsHeliInfil; } catch { }
                try { nm = m.InfilName ?? ""; } catch { }
                if (!ground || heli || nm.Length == 0) continue;

                if (!IsGroundCallsign(nm))
                {
                    if (_rejectLogged.Add(prefabId * 31 + i))
                        AirborneMod.Log("[board] '" + nm + "' reads IsGroundInfil but carries a ride " +
                                        "callsign - not a walk-in, not aliased (AirborneGroundCallsigns=" +
                                        _groundCallsigns.Value + ")");
                    continue;
                }

                if (want.Length > 0 && string.Equals(nm, want, StringComparison.OrdinalIgnoreCase))
                    return m;
                if (first == null) first = m;
            }
            return first;
        }

        static bool IsGroundCallsign(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string list = ""; try { list = _groundCallsigns.Value ?? ""; } catch { }
            foreach (var raw in list.Split(','))
            {
                var s = raw.Trim();
                if (s.Length > 0 && name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        static void OffsetRect(GameObject clone, GameObject src, string via)
        {
            try
            {
                var rt = clone.GetComponent<RectTransform>();
                var art = src.GetComponent<RectTransform>();
                if (rt == null || art == null) return;

                float du = _offU.Value, dv = _offV.Value;
                bool point = Mathf.Abs(art.anchorMax.x - art.anchorMin.x) < 0.001f
                          && Mathf.Abs(art.anchorMax.y - art.anchorMin.y) < 0.001f;
                if (point)
                {
                    var a = new Vector2(Mathf.Clamp(art.anchorMin.x + du, 0.02f, 0.98f),
                                        Mathf.Clamp(art.anchorMin.y + dv, 0.02f, 0.98f));
                    rt.anchorMin = a; rt.anchorMax = a;
                    rt.anchoredPosition = art.anchoredPosition;
                }
                else
                {
                    float s = _offScale.Value;
                    rt.anchoredPosition = art.anchoredPosition + new Vector2(du * s, dv * s);
                }
                AirborneMod.Log("[board] marker offset (" + via + "): " +
                                (point ? "anchor" : "anchoredPosition") +
                                " du=" + du.ToString("F3") + " dv=" + dv.ToString("F3"));
            }
            catch (Exception e) { AirborneMod.Log("[board] offset err: " + e.Message); }
        }

        static void ReadBack(Il2CppArrayBase<Il2Cpp.MapInfilMarker> arr, string via, bool verbose)
        {
            int n = arr != null ? arr.Length : -1;
            int at = -1, stamped = -1; string nm = "";
            Il2Cpp.MapInfilMarker ours = null, reference = null;

            for (int i = 0; i < n; i++)
            {
                var m = arr[i]; if (m == null) continue;
                GameObject g = null; try { g = m.gameObject; } catch { }
                if (g == null) continue;
                if (g.name.StartsWith(MarkerName, StringComparison.Ordinal))
                {
                    ours = m; at = i;
                    try { stamped = m.MarkerIndex; } catch { }
                    try { nm = m.InfilName ?? ""; } catch { }
                }
                else if (reference == null)
                {
                    bool ground = false; try { ground = m.IsGroundInfil; } catch { }
                    if (ground) reference = m;
                }
            }

            if (verbose)
                AirborneMod.Log("[board] readback (" + via + "): " + n + " marker(s), ours at " +
                                (at >= 0 ? at + " stamped " + stamped + " as '" + nm + "'" : "ABSENT"));

            if (at < 0)
            {

                if (n <= 0) return;
                if (_index >= 0)
                {
                    AirborneMod.Log("[board] our marker is NOT in the " + via + " array (" + n +
                                    " markers) - airborne DISARMED (was index " + _index + ")");
                    _index = -1;
                    SteerableDrop.SetAirborneIndex(-1);
                }
                Census(arr, via);
                return;
            }

            if (stamped != at)
            {

                AirborneMod.Log("[board] INDEX MISMATCH (" + via + "): array pos " + at +
                                " but MarkerIndex " + stamped + " - airborne disarmed");
                _index = -1;
                SteerableDrop.SetAirborneIndex(-1);
                return;
            }

            if (_index != stamped)
            {
                _index = stamped;
                SteerableDrop.SetAirborneIndex(stamped);
                AirborneMod.Log("[board] airborne point live (" + via + "): index " + stamped +
                                " of " + n + ", resolves as '" + nm + "'");
            }

            Relabel(ours);
            if (via == "deploy") _liveDeploy = ours; else _liveBoard = ours;

            if (verbose) VisualCompare(ours, reference, via);
        }

        static void Relabel(Il2Cpp.MapInfilMarker m)
        {
            if (m == null) return;
            try
            {
                var t = m.TextUI;
                if (t == null) return;
                string want = _label.Value;
                if (t.text != want) t.text = want;

                var loc = t.GetComponent<Il2Cpp.LocalizationTMPEvent>();
                if (loc != null && loc.enabled) loc.enabled = false;
            }
            catch { }
        }

        static void VisualCompare(Il2Cpp.MapInfilMarker ours, Il2Cpp.MapInfilMarker reference, string via)
        {
            if (ours == null) return;
            int id = SafeId(ours);
            if (id == _lastVisualId) return;
            _lastVisualId = id;
            AirborneMod.Log("[vis] ===== " + via + " =====");
            Describe(ours, "OURS");
            if (reference != null) Describe(reference, "REF ");
            else AirborneMod.Log("[vis] REF  <no stock ground marker in this array to compare against>");
        }

        static void Describe(Il2Cpp.MapInfilMarker m, string tag)
        {
            try
            {
                GameObject g = null; try { g = m.gameObject; } catch { }
                if (g == null) { AirborneMod.Log("[vis] " + tag + " <no gameObject>"); return; }

                string line = "[vis] " + tag + " '" + g.name + "'";
                try { line += " self=" + g.activeSelf + " inHier=" + g.activeInHierarchy; } catch { }
                try { line += " idx=" + m.MarkerIndex + " infil='" + m.InfilName + "'"; } catch { }
                try { line += " sel=" + m.isSelectedLocal; } catch { }
                try { line += " ground=" + m.IsGroundInfil + " heli=" + m.IsHeliInfil + " exfil=" + m.IsExfil; } catch { }
                try { var cg = m.canvasGroup; line += cg != null ? " cgAlpha=" + cg.alpha.ToString("F2") : " cg=<null>"; } catch { }
                try { line += " unselAlpha=" + m.unSelectedAlpha.ToString("F2"); } catch { }
                AirborneMod.Log(line);

                string rect = "[vis] " + tag + " rect";
                try
                {
                    var rt = g.GetComponent<RectTransform>();
                    if (rt != null)
                        rect += " aMin=" + rt.anchorMin + " aMax=" + rt.anchorMax +
                                " pos=" + rt.anchoredPosition + " size=" + rt.sizeDelta +
                                " scale=" + rt.localScale;
                    else rect += " <no RectTransform>";
                }
                catch { }
                AirborneMod.Log(rect);

                string kids = "[vis] " + tag + " kids";
                kids += Kid(m, "Selected", SafeGo(() => m.SelectedParent));
                kids += Kid(m, "Deselected", SafeGo(() => m.DeselectedParent));
                kids += Kid(m, "Ground", SafeGo(() => m.GroundInfilMarker));
                kids += Kid(m, "GroundDes", SafeGo(() => m.GroundInfilMarker_Deselected));
                kids += Kid(m, "Heli", SafeGo(() => m.HeliInfilMarker));
                kids += Kid(m, "Exfil", SafeGo(() => m.ExfilMarker));
                AirborneMod.Log(kids);

                try
                {
                    var t = m.TextUI;
                    AirborneMod.Log("[vis] " + tag + " text " + (t == null ? "<null>"
                        : "'" + t.text + "' enabled=" + t.enabled + " alpha=" + t.color.a.ToString("F2")));
                }
                catch { }

                AirborneMod.Log("[vis] " + tag + " path " + PathOf(g.transform));
            }
            catch (Exception e) { AirborneMod.Log("[vis] " + tag + " err: " + e.Message); }
        }

        static GameObject SafeGo(Func<GameObject> f) { try { return f(); } catch { return null; } }

        static string Kid(Il2Cpp.MapInfilMarker m, string name, GameObject go)
        {
            if (go == null) return " " + name + "=<null>";
            try { return " " + name + "=" + (go.activeSelf ? "on" : "OFF"); } catch { return " " + name + "=?"; }
        }

        static void Census(Il2CppArrayBase<Il2Cpp.MapInfilMarker> arr, string via)
        {
            int id = 0;
            try { var m0 = arr[0]; id = m0 != null ? m0.GetInstanceID() : 0; } catch { }
            if (id == 0 || id == _lastCensusId) return;
            _lastCensusId = id;

            AirborneMod.Log("[census] ===== " + via + ": " + arr.Length + " marker(s) in the live array =====");
            for (int i = 0; i < arr.Length; i++)
            {
                var m = arr[i];
                if (m == null) { AirborneMod.Log("[census] [" + i + "] <null>"); continue; }
                GameObject g = null; try { g = m.gameObject; } catch { }
                int idx = -1; string nm = "", label = "", oname = "?";
                bool ground = false, heli = false, exfil = false, act = false, self = false;
                try { idx = m.MarkerIndex; } catch { }
                try { nm = m.InfilName ?? ""; } catch { }
                try { var t = m.TextUI; if (t != null) label = t.text ?? ""; } catch { }
                try { ground = m.IsGroundInfil; } catch { }
                try { heli = m.IsHeliInfil; } catch { }
                try { exfil = m.IsExfil; } catch { }
                if (g != null)
                {
                    try { oname = g.name; } catch { }
                    try { act = g.activeInHierarchy; } catch { }
                    try { self = g.activeSelf; } catch { }
                }
                AirborneMod.Log("[census] [" + i + "] idx=" + idx + " obj='" + oname + "' infil='" + nm +
                                "' label='" + label + "'" + (ground ? " GROUND" : "") +
                                (heli ? " HELI" : "") + (exfil ? " EXFIL" : "") +
                                " self=" + self + " inHier=" + act +
                                " @ " + PathOf(g != null ? g.transform : null));
            }
            AirborneMod.Log("[census] ===== end =====");
        }

        static string PathOf(Transform t)
        {
            try
            {
                if (t == null) return "?";
                var parts = new List<string>();
                var cur = t; int guard = 0;
                while (cur != null && guard++ < 12) { parts.Add(cur.name); cur = cur.parent; }
                parts.Reverse();
                return string.Join("/", parts.ToArray());
            }
            catch { return "?"; }
        }

        static int SafeId(UnityEngine.Object o)
        {
            try { return o != null ? o.GetInstanceID() : 0; } catch { return 0; }
        }

        internal static void Remove(string why)
        {
            for (int i = 0; i < _touched.Count; i++)
            {
                var prefab = _touched[i];
                if (prefab == null) continue;
                try
                {
                    var all = prefab.GetComponentsInChildren<Il2Cpp.MapInfilMarker>(true);
                    if (all == null) continue;
                    for (int j = 0; j < all.Length; j++)
                    {
                        var m = all[j]; if (m == null) continue;
                        GameObject g = null; try { g = m.gameObject; } catch { }
                        if (g != null && g.name == MarkerName) UObject.Destroy(g);
                    }
                }
                catch { }
            }
            _touched.Clear();
            _index = -1;
            SteerableDrop.SetAirborneIndex(-1);
            AirborneMod.Log("[board] airborne point removed (" + why + ")");
        }
    }
}
