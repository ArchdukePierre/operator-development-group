// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace MapLoader
{

    public static class SpawnTruth
    {
        static MelonLogger.Instance _log;

        static MelonPreferences_Entry<bool>  _enabled;
        static MelonPreferences_Entry<float> _markerWarnM;
        static MelonPreferences_Entry<float> _edgeSlackM;
        static MelonPreferences_Entry<int>   _maxRescues;

        public static void Init(MelonLogger.Instance log)
        {
            _log = log;
            try
            {
                var cfg = MelonPreferences.GetCategory("MapLoader")
                       ?? MelonPreferences.CreateCategory("MapLoader");
                _enabled = cfg.GetEntry<bool>("SpawnRescueEnabled")
                        ?? cfg.CreateEntry("SpawnRescueEnabled", true,
                             description: "Put a player who is outside the playable map back at the "
                                        + "insertion point, and say so. Turn off only to reproduce a "
                                        + "spawn bug.");
                _edgeSlackM = cfg.GetEntry<float>("SpawnRescueEdgeSlackM")
                           ?? cfg.CreateEntry("SpawnRescueEdgeSlackM", 1f,
                             description: "How far outside the map's own geometry a player has to be "
                                        + "before they count as outside the level. There is nothing "
                                        + "of ours to stand on out there, so this is small on "
                                        + "purpose; the failing session was 73 m out.");
                _markerWarnM = cfg.GetEntry<float>("SpawnMarkerWarnM")
                            ?? cfg.CreateEntry("SpawnMarkerWarnM", 175f,
                             description: "Distance from every gameplay marker at which the log says "
                                        + "the player is somewhere odd. REPORTING ONLY -- it never "
                                        + "moves anybody. Measured on this city: the worst reachable "
                                        + "ground is 169.1 m from a marker and the failing session "
                                        + "was 202.7 m, so this cannot be made to act safely.");
                _maxRescues = cfg.GetEntry<int>("SpawnRescueMaxAttempts")
                           ?? cfg.CreateEntry("SpawnRescueMaxAttempts", 4,
                             description: "Consecutive rescues that fail to stick before the guard "
                                        + "stands down and just reports. It must not fight the game "
                                        + "in a loop.");
            }
            catch (Exception e) { _log?.Warning("[spawntruth] prefs: " + e.Message); }
        }

        static bool _haveFootprint;
        static float _fx0, _fx1, _fz0, _fz1, _fy0, _fy1;
        static string _footprintFor;
        static Transform _skirt;
        static bool _skirtLooked;

        static float _skirtNextLook;

        static Transform Skirt()
        {
            if (_skirt != null) return _skirt;

            float now = Time.realtimeSinceStartup;
            if (_skirtLooked && now < _skirtNextLook) return null;
            _skirtNextLook = now + 2f;
            _skirtLooked = true;
            try
            {
                var root = MapRuntime.OurRoot;

                if (root != null) _skirt = root.Find("ODG Ground Skirt");
            }
            catch { _skirt = null; }
            return _skirt;
        }

        static bool Footprint()
        {
            if (_haveFootprint && _footprintFor == MapRuntime.SceneName) return true;
            if (!MapRuntime.Done) return false;
            Transform root = null;
            try { root = MapRuntime.OurRoot; } catch { }
            if (root == null) return false;

            var skirt = Skirt();
            float x0 = float.MaxValue, x1 = float.MinValue;
            float z0 = float.MaxValue, z1 = float.MinValue;
            float y0 = float.MaxValue, y1 = float.MinValue;
            int used = 0, skipped = 0;
            try
            {
                var rs = root.GetComponentsInChildren<Renderer>(false);
                for (int i = 0; i < rs.Length; i++)
                {
                    var r = rs[i];
                    if (r == null) continue;
                    if (skirt != null)
                    {
                        bool isSkirt = false;
                        try { isSkirt = r.transform.IsChildOf(skirt); } catch { }
                        if (isSkirt) { skipped++; continue; }
                    }
                    var b = r.bounds;
                    if (b.min.x < x0) x0 = b.min.x; if (b.max.x > x1) x1 = b.max.x;
                    if (b.min.z < z0) z0 = b.min.z; if (b.max.z > z1) z1 = b.max.z;
                    if (b.min.y < y0) y0 = b.min.y; if (b.max.y > y1) y1 = b.max.y;
                    used++;
                }
            }
            catch (Exception e) { _log?.Warning("[spawntruth] footprint: " + e.Message); return false; }

            if (used == 0 || x1 <= x0 || z1 <= z0) return false;

            _fx0 = x0; _fx1 = x1; _fz0 = z0; _fz1 = z1; _fy0 = y0; _fy1 = y1;
            _haveFootprint = true;
            _footprintFor = MapRuntime.SceneName;

            _log?.Msg("[spawntruth] the map's own footprint, skirt excluded, from " + used
                    + " renderer(s)" + (skipped > 0 ? " (" + skipped + " skirt renderer(s) skipped)"
                                                    : " (no ground skirt found)")
                    + ": x " + x0.ToString("F1") + ".." + x1.ToString("F1")
                    + "  y " + y0.ToString("F1") + ".." + y1.ToString("F1")
                    + "  z " + z0.ToString("F1") + ".." + z1.ToString("F1")
                    + ". Anything outside that in X or Z has nothing of ours under it.");
            if (skirt == null)
                _log?.Warning("[spawntruth] no 'ODG Ground Skirt' under the map root. The "
                            + "standing-on-scenery test is inert this session; the footprint "
                            + "test still covers the same ground.");
            return true;
        }

        static readonly List<Vector3> _markers = new List<Vector3>();
        static bool _markersRead;
        static string _markersFor;

        static readonly string[] MarkerGroups =
        {
            "GBInsertionPoint", "GBPlayerStart", "GBAISpawnPoint",
            "GBAIGuardPoint", "GBAIPatrolRoute", "BP_Generic_Door_C",

            "BP_Generic_Door_Industrial_01_C", "BP_Generic_Door_Industrial_02_C",
            "BP_Generic_Door_Quarters_01_C", "BP_Generic_Door_Bulkhead_01_C",
            "BP_Door_Rollup_Oilrig_01_C",
        };

        public static void Forget()
        {
            _markersRead = false; _markers.Clear();
            _haveFootprint = false; _footprintFor = null;
            _skirt = null; _skirtLooked = false;
            _rescues = 0; _consecFailed = 0; _stoodDown = false; _awaitingResult = false;
            _passWritten = false; _failWritten = false; _lastWhy = null; _arrivalReported = false;
        }

        static void ReadMarkers()
        {
            if (_markersRead && _markersFor == MapRuntime.SceneName) return;
            _markers.Clear();
            _markersFor = MapRuntime.SceneName;
            _markersRead = true;
            try
            {
                for (int g = 0; g < MarkerGroups.Length; g++)
                {
                    var list = MapAI.FindGroup(MarkerGroups[g]);
                    for (int i = 0; i < list.Count; i++)
                        if (list[i] != null) _markers.Add(list[i].position);
                }
            }
            catch (Exception e) { _log?.Warning("[spawntruth] marker read: " + e.Message); }
        }

        static float NearestMarker(Vector3 p)
        {
            ReadMarkers();
            float best = float.MaxValue;
            for (int i = 0; i < _markers.Count; i++)
            {
                float dx = _markers[i].x - p.x, dz = _markers[i].z - p.z;
                float d = dx * dx + dz * dz;
                if (d < best) best = d;
            }
            return best == float.MaxValue ? float.NaN : Mathf.Sqrt(best);
        }

        public static bool IsOutOfMap(Vector3 p, out string why)
        {
            why = null;
            if (!Footprint()) return false;

            if (p.y < _fy0 - 3f)
            {
                why = "below the map's lowest geometry (y=" + p.y.ToString("F1")
                    + " against a floor of " + _fy0.ToString("F1") + ")";
                return true;
            }

            float slack = _edgeSlackM != null ? Mathf.Max(0f, _edgeSlackM.Value) : 1f;

            float outX = Mathf.Max(_fx0 - p.x, p.x - _fx1);
            float outZ = Mathf.Max(_fz0 - p.z, p.z - _fz1);
            float outside = Mathf.Max(outX, outZ);
            if (outside > slack && p.y <= _fy1)
            {
                why = "outside the map's own footprint by " + outside.ToString("F1")
                    + " m (x " + _fx0.ToString("F0") + ".." + _fx1.ToString("F0")
                    + ", z " + _fz0.ToString("F0") + ".." + _fz1.ToString("F0")
                    + ") -- there is no geometry of ours out here";
                return true;
            }

            if (StandingOnSkirt(p))
            {
                why = "standing on the ground skirt, which is scenery -- there is open air "
                    + "between it and the last walkable ground";
                return true;
            }

            return false;
        }

        static bool StandingOnSkirt(Vector3 p)
        {
            var skirt = Skirt();
            if (skirt == null) return false;
            try
            {
                var hits = new Il2CppInterop.Runtime.InteropTypes.Arrays
                               .Il2CppStructArray<RaycastHit>(24L);

                int n = Physics.RaycastNonAlloc(p + Vector3.up * 1f, Vector3.down, hits, 14f, ~0,
                                                QueryTriggerInteraction.Ignore);
                float best = float.NegativeInfinity;
                bool bestIsSkirt = false;
                for (int i = 0; i < n && i < hits.Length; i++)
                {
                    var c = hits[i].collider;
                    if (c == null) continue;
                    if (hits[i].point.y <= best) continue;
                    best = hits[i].point.y;
                    try { bestIsSkirt = c.transform.IsChildOf(skirt); }
                    catch { bestIsSkirt = false; }
                }
                return bestIsSkirt;
            }
            catch { return false; }
        }

        static float _next, _lastRescue, _nextHeartbeat, _doneAt, _lastWarn;
        static int _rescues, _consecFailed;
        static bool _stoodDown, _passWritten, _failWritten, _arrivalReported, _awaitingResult;
        static string _lastWhy;

        public static void Tick()
        {
            if (_enabled != null && !_enabled.Value) return;
            if (!MapRuntime.Done) { _doneAt = 0f; return; }

            float now = Time.realtimeSinceStartup;
            if (_doneAt <= 0f) { _doneAt = now; _next = now; }
            if (now < _next) return;

            _next = now + (now - _doneAt < 30f ? 0.5f : 2f);

            Vector3 p;
            if (!PlayerBody.TryPosition(out p)) return;

            if (Mathf.Abs(p.x) < 1.5f && Mathf.Abs(p.z) < 1.5f && p.y < -20f) return;
            if (MapIdentity.HostLevelIsMap && !MapRuntime.AdditiveReady) return;

            string why;
            bool bad = IsOutOfMap(p, out why);

            if (!bad)
            {
                if (_lastWhy != null)
                {
                    _log?.Msg("[spawntruth] the player is back inside the level at ("
                            + p.x.ToString("F1") + ", " + p.y.ToString("F1") + ", "
                            + p.z.ToString("F1") + "). " + _rescues + " rescue(s) this mission.");
                    _lastWhy = null;
                }

                _consecFailed = 0;
                _awaitingResult = false;
                if (!_arrivalReported && !PlayerBody.ConfirmPending) ReportArrival();
                return;
            }

            if (_lastWhy == null || now - _lastWarn > 10f)
            {
                _lastWarn = now;
                _lastWhy = why;
                _log?.Warning("[spawntruth] THE PLAYER IS NOT IN THE MAP: at ("
                            + p.x.ToString("F1") + ", " + p.y.ToString("F1") + ", "
                            + p.z.ToString("F1") + "), " + why + ". Nearest gameplay marker "
                            + Fmt(NearestMarker(p)) + ".");
            }

            if (_stoodDown)
            {
                if (now >= _nextHeartbeat)
                {
                    _nextHeartbeat = now + 30f;
                    _log?.Error("[spawntruth] still outside the level at (" + p.x.ToString("F1")
                              + ", " + p.y.ToString("F1") + ", " + p.z.ToString("F1")
                              + ") and this guard has stood down. Use the console: odg infil <n>.");
                }
                return;
            }

            if (PlayerBody.ConfirmPending) return;
            if (now - _lastRescue < 6f) return;

            if (_awaitingResult)
            {

                _awaitingResult = false;
                _consecFailed++;
                int cap = _maxRescues != null ? Mathf.Max(1, _maxRescues.Value) : 4;
                if (_consecFailed >= cap)
                {
                    _stoodDown = true;
                    _nextHeartbeat = now;
                    Verdict("FAIL -- " + _rescues + " attempt(s) to put the player into '"
                          + MapRuntime.SceneName + "' and the body is still outside it ("
                          + why + "). Last read "
                          + (float.IsNaN(PlayerBody.LastError) ? "not taken"
                             : PlayerBody.LastError.ToString("F0") + " m from the requested spot")
                          + ". Something else owns this body -- an insertion vehicle seat, or "
                          + "another mod's spawn hook. STANDING DOWN so the player is not yanked "
                          + "every few seconds; they can place themselves with 'odg infil <n>'. "
                          + "Do not ship this map until this line is absent from a played session.");
                    return;
                }
            }

            _lastRescue = now;
            _rescues++;
            _awaitingResult = true;
            _log?.Warning("[spawntruth] rescue #" + _rescues + ": putting the player back through "
                        + "the map's own insertion.");
            if (!MapRuntime.TeleportPlayerIn())
            {
                _log?.Error("[spawntruth] rescue #" + _rescues + " could not even be attempted. "
                          + "The player is outside the level and the mod cannot move them.");
            }
        }

        public static void ReportArrival()
        {
            _arrivalReported = true;
            Vector3 p;
            if (!PlayerBody.TryPosition(out p))
            {
                Verdict("FAIL -- the player body could not be read at all after the map came up.");
                return;
            }
            string why;
            if (IsOutOfMap(p, out why))
            {
                Verdict("FAIL -- the map is up and the player is at (" + p.x.ToString("F1") + ", "
                      + p.y.ToString("F1") + ", " + p.z.ToString("F1") + "): " + why + ".");
                return;
            }
            float d = NearestMarker(p);
            float warn = _markerWarnM != null ? _markerWarnM.Value : 175f;
            Verdict("PASS -- the player body is at (" + p.x.ToString("F1") + ", "
                  + p.y.ToString("F1") + ", " + p.z.ToString("F1") + "), inside the map's "
                  + "footprint, " + Fmt(d) + " from the nearest gameplay marker. Read off the "
                  + "AVATAR, not off the PlayerMaster connection object that used to answer "
                  + "for it."
                  + (!float.IsNaN(d) && d > warn
                     ? "  NOTE: that is further from the level than any reachable ground measured "
                       + "on this map (169 m). Worth a look, but not worth a teleport." : ""));
        }

        static void Verdict(string s)
        {

            bool pass = s.StartsWith("PASS");
            if (pass) { if (_passWritten) return; _passWritten = true; _log?.Msg("[VERDICT] spawn: " + s); }
            else { if (_failWritten) return; _failWritten = true; _log?.Error("[VERDICT] spawn: " + s); }
        }

        static string Fmt(float d)
        {
            return float.IsNaN(d) ? "(no markers to measure against)" : d.ToString("F0") + " m";
        }
    }
}
