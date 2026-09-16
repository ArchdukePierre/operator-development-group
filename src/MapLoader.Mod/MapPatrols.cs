// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using UnityEngine;
using L = Il2CppSystem.Collections.Generic;

namespace MapLoader
{
    public static class MapPatrols
    {
        public const char WaypointSeparator = '#';

        public static float AdoptRadius = 22f;

        public static float PatrolShare = 0.6f;

        static MelonLoader.MelonLogger.Instance _log;
        public static void Init(MelonLoader.MelonLogger.Instance log) { _log = log; }

        public sealed class Route
        {
            public string Name;
            public List<Transform> Points = new List<Transform>();

            public List<Transform> Walk;
            public bool Mirrored;
            public Vector3 Centre;
        }

        static List<Route> _routes;

        public static bool MirrorBlockedLoops = true;

        public const int OfflineExpectedMirrors = 29;
        static int _mirrored = -1;

        public static List<Route> Routes()
        {
            if (_routes != null) return _routes;
            _routes = new List<Route>();
            var flat = MapAI.FindGroup(MapAI.PatrolGroup);
            if (flat == null || flat.Count == 0)
            {
                _log?.Msg("[patrol] no " + MapAI.PatrolGroup + " markers");
                return _routes;
            }

            var byName = new Dictionary<string, List<KeyValuePair<int, Transform>>>();
            int loose = 0;
            foreach (var t in flat)
            {
                if (t == null) continue;
                string n = t.name ?? "";
                int hash = n.LastIndexOf(WaypointSeparator);
                string key;
                int idx;
                if (hash <= 0 || !int.TryParse(n.Substring(hash + 1), out idx))
                {

                    key = n; idx = 0; loose++;
                }
                else key = n.Substring(0, hash);

                List<KeyValuePair<int, Transform>> lst;
                if (!byName.TryGetValue(key, out lst))
                {
                    lst = new List<KeyValuePair<int, Transform>>();
                    byName[key] = lst;
                }
                lst.Add(new KeyValuePair<int, Transform>(idx, t));
            }

            foreach (var kv in byName)
            {
                var lst = kv.Value;
                lst.Sort(delegate (KeyValuePair<int, Transform> a,
                                   KeyValuePair<int, Transform> b)
                         { return a.Key.CompareTo(b.Key); });
                if (lst.Count < 2) continue;
                var r = new Route { Name = kv.Key };
                var sum = Vector3.zero;
                foreach (var p in lst) { r.Points.Add(p.Value); sum += p.Value.position; }
                r.Centre = sum / r.Points.Count;
                _routes.Add(r);
            }

            _log?.Msg("[patrol] " + flat.Count + " marker(s) -> " + _routes.Count
                    + " route(s)" + (loose > 0
                        ? ", " + loose + " unnumbered marker(s) ignored (old bundle)"
                        : ""));
            if (_routes.Count > 0)
            {
                int wp = 0;
                foreach (var r in _routes) wp += r.Points.Count;
                _log?.Msg("[patrol] " + wp + " waypoint(s), longest route "
                        + LongestName() + ".");
            }
            return _routes;
        }

        static string LongestName()
        {
            Route best = null;
            foreach (var r in _routes)
                if (best == null || r.Points.Count > best.Points.Count) best = r;
            return best == null ? "-" : best.Name + " with " + best.Points.Count;
        }

        public static List<Transform> WalkPoints(Route r)
        {
            if (r.Walk != null) return r.Walk;

            if (!MirrorBlockedLoops || r.Points.Count <= 2)
            {
                r.Walk = r.Points;
                return r.Walk;
            }

            var a = r.Points[r.Points.Count - 1];
            var b = r.Points[0];
            bool blocked = false;
            if (a != null && b != null)
            {
                try
                {

                    blocked = Physics.Linecast(a.position, b.position, ~0,
                                               QueryTriggerInteraction.Ignore);
                }
                catch (Exception e)
                {

                    _log?.Warning("[patrol] closure probe '" + r.Name + "': " + e.Message);
                    blocked = false;
                }
            }

            if (!blocked) { r.Walk = r.Points; return r.Walk; }

            var w = new List<Transform>(r.Points.Count * 2 - 2);
            for (int i = 0; i < r.Points.Count; i++) w.Add(r.Points[i]);
            for (int i = r.Points.Count - 2; i >= 1; i--) w.Add(r.Points[i]);
            r.Walk = w;
            r.Mirrored = true;
            return r.Walk;
        }

        public static void ReportClosures()
        {
            if (_routes == null || _mirrored >= 0) return;
            _mirrored = 0;
            foreach (var r in _routes) { WalkPoints(r); if (r.Mirrored) _mirrored++; }
            _log?.Msg("[patrol] loop closure: " + _mirrored + " of " + _routes.Count
                    + " route(s) mirrored (offline expected " + OfflineExpectedMirrors
                    + "). Looping is left true; only the point list changes, so no "
                    + "edge is walked that was not authored.");
        }

        public static void ResetForNewLevel() { _routes = null; _assigned = 0; _mirrored = -1; }

        public static Route Nearest(Vector3 p, out float dist)
        {
            dist = float.MaxValue;
            Route best = null;
            foreach (var r in Routes())
            {
                for (int i = 0; i < r.Points.Count; i++)
                {
                    var t = r.Points[i];
                    if (t == null) continue;
                    float d = Vector3.Distance(p, t.position);
                    if (d < dist) { dist = d; best = r; }
                }
            }
            return best;
        }

        static int _assigned;

        public static int AssignToSpawnPoints()
        {
            int n = 0, near = 0;
            try
            {
                var routes = Routes();
                if (routes.Count == 0) return 0;
                var spawns = MapAI.FindGroup(MapAI.SpawnGroup);
                int i = 0;
                foreach (var t in spawns)
                {
                    if (t == null) continue;
                    i++;
                    float d;
                    var r = Nearest(t.position, out d);
                    if (r == null || d > AdoptRadius) continue;
                    near++;

                    if ((i * 2654435761u % 1000u) / 1000f > PatrolShare) continue;
                    if (Give(t.gameObject, r)) n++;
                }
            }
            catch (Exception e) { _log?.Warning("[patrol] assign: " + e.Message); }
            _assigned = n;
            _log?.Msg("[patrol] " + near + " spawn point(s) within " + AdoptRadius
                    + " m of a route, " + n + " put on Patrol, the rest stay on "
                    + "Wander.");
            return n;
        }

        static bool _reported;

        static bool Give(GameObject go, Route r)
        {
            try
            {
                MapAI.EnsureDetails(go);
                var d = go.GetComponent<Il2Cpp.BotSpawnDetails>();
                if (d == null) return false;

                var list = new L.List<Transform>();
                ReportClosures();
                var pts = WalkPoints(r);
                foreach (var t in pts) if (t != null) list.Add(t);
                d.patrolPoints = list;
                d.idleState = Il2Cpp.BrainAI.IdleStates.Patrol;
                d.patrolWaitTime = 2f;

                if (!_reported)
                {
                    _reported = true;
                    int back = -1;
                    try { back = d.patrolPoints != null ? d.patrolPoints.Count : -1; }
                    catch { }
                    _log?.Msg("[patrol] read-back on a real component: wrote "
                            + pts.Count + " point(s), the component reads "
                            + back + (back == pts.Count ? "" : "  <- IT DID NOT STICK")
                            + "; idleState reads " + d.idleState + ".");
                }
                return true;
            }
            catch (Exception e)
            {
                if (!_reported)
                {
                    _reported = true;
                    _log?.Warning("[patrol] BotSpawnDetails has no usable patrol "
                                + "list on this build (" + e.Message + "). Falling "
                                + "back to the post-spawn brain sweep.");
                }
                return false;
            }
        }

        public static int SweepBrains()
        {
            int fixedUp = 0;
            try
            {
                if (!Il2CppMirror.NetworkServer.active) return 0;
                if (Routes().Count == 0) return 0;
                var brains = UnityEngine.Object.FindObjectsOfType<Il2Cpp.BrainAI>();
                if (brains == null) return 0;
                for (int i = 0; i < brains.Length; i++)
                {
                    var b = brains[i];
                    if (b == null) continue;
                    if (b.isHVT) continue;
                    var ap = b.aiPatrol;
                    if (ap == null) continue;
                    int have = 0;
                    try { have = ap.patrolPoints != null ? ap.patrolPoints.Count : 0; }
                    catch { }
                    if (have > 0) continue;
                    float d;
                    var r = Nearest(b.transform.position, out d);
                    if (r == null || d > AdoptRadius) continue;
                    var list = new L.List<Transform>();
                    foreach (var t in WalkPoints(r)) if (t != null) list.Add(t);
                    ap.patrolPoints = list;
                    ap.Looping = true;
                    ap.patrolWaitTime = 2f;
                    b.idleStates = Il2Cpp.BrainAI.IdleStates.Patrol;
                    fixedUp++;
                }
            }
            catch (Exception e) { _log?.Warning("[patrol] sweep: " + e.Message); }
            if (fixedUp > 0)
                _log?.Msg("[patrol] post-spawn sweep put " + fixedUp
                        + " brain(s) on a route directly.");
            return fixedUp;
        }
    }
}
