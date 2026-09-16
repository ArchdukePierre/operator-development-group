// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace MapLoader
{

    static class SpawnSlots
    {
        static MelonLogger.Instance _log;
        static bool _installed;
        static int _hooks;

        static readonly List<Transform> _slots = new List<Transform>();
        static GameObject _holder;
        static int _answered;
        static string _scene = "";
        static bool _saidLanded;

        const float NearMarkedM = 7f;
        const float AboveMarkedMaxM = 0.6f;
        static readonly float[] Rings = { 2.4f, 3.6f, 5.0f };

        public static void Install(HarmonyLib.Harmony h, MelonLogger.Instance log)
        {
            _log = log;
            _hooks = 0;
            Hook(h, typeof(Il2Cpp.GameManager), "nextSpawnPosition",
                 new Type[] { typeof(bool), typeof(int), typeof(int) }, nameof(Pre_Next), "spawn query");
            Hook(h, typeof(Il2Cpp.GameManager), "nextSpawnPositionRandom", null, nameof(Pre_NextRandom), "random spawn query");
            _installed = _hooks >= 1;
            _log?.Msg("[spawn] slots: " + _hooks + "/2 hooks; on our maps the host answers the game's spawn query with a clear slot "
                    + "round the marked spawn; the game's own RPC places everyone, on the game's own timing");
        }

        static void Hook(HarmonyLib.Harmony h, Type t, string name, Type[] args, string prefix, string what)
        {
            try
            {
                var m = args == null ? AccessTools.Method(t, name) : AccessTools.Method(t, name, args);
                if (m == null) { _log?.Warning("[spawn] " + t.Name + "." + name + " not found; the " + what + " is not covered"); return; }
                h.Patch(m, prefix: new HarmonyMethod(typeof(SpawnSlots), prefix));
                _hooks++;
            }
            catch (Exception e) { _log?.Warning("[spawn] " + what + " hook: " + e.Message); }
        }

        public static void ResetForNewLevel(string why)
        {
            if (_slots.Count > 0)
                _log?.Msg("[spawn] slots cleared (" + why + "): " + _answered + " quer" + (_answered == 1 ? "y" : "ies") + " answered");
            _slots.Clear();
            try { if (_holder != null) UnityEngine.Object.Destroy(_holder); } catch { }
            _holder = null;
            _answered = 0;
            _saidLanded = false;
        }

        static bool MapUp()
        {
            try
            {
                if (!MapRuntime.Done) return false;
                if (MapIdentity.HostLevelIsMap && !MapRuntime.AdditiveReady) return false;
                return true;
            }
            catch { return false; }
        }

        static bool OurMission()
        {
            try { return MapRuntime.Done || MapRuntime.Building; } catch { return false; }
        }

        public static float DistanceToMarked(Vector3 p)
        {
            Vector3 m;
            if (!MapRuntime.TryMarkedSpawn(out m)) return float.PositiveInfinity;
            float dx = p.x - m.x, dz = p.z - m.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        public static bool AtOurSpawn(Vector3 p)
        {
            return DistanceToMarked(p) <= NearMarkedM;
        }

        public static void NoteLanded(Vector3 p)
        {
            if (_saidLanded) return;
            _saidLanded = true;
            _log?.Msg("[spawn] the game's own deploy landed the player " + DistanceToMarked(p).ToString("0.0")
                    + " m from the marked spawn: placed by the game's path, weapons and all; no teleport.");
        }

        public static void Tick()
        {
            if (!_installed) return;
            try
            {
                string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                if (scene != _scene) { ResetForNewLevel("scene changed to '" + scene + "'"); _scene = scene; }
            }
            catch { }
        }

        static bool Pre_Next(Il2Cpp.GameManager __instance, ref Transform __result, bool useTeams, int team, int iteration)
        {
            return Answer(ref __result, "nextSpawnPosition(useTeams=" + useTeams + ", team=" + team + ", iteration=" + iteration + ")");
        }

        static bool Pre_NextRandom(Il2Cpp.GameManager __instance, ref Transform __result)
        {
            return Answer(ref __result, "nextSpawnPositionRandom()");
        }

        static bool Answer(ref Transform result, string query)
        {
            try
            {
                if (!Il2CppMirror.NetworkServer.active) return true;
                if (!MapUp()) return true;
                Vector3 marked;
                if (!MapRuntime.TryMarkedSpawn(out marked)) return true;
                string how;
                var t = NextSlot(marked, out how);
                if (t == null)
                {
                    _log?.Warning("[spawn] no clear slot round the marked spawn (" + how + "); the game's own point stands and the tick places the player later");
                    return true;
                }
                result = t;
                _answered++;
                var p = t.position;
                _log?.Msg("[spawn] answered the game's " + query + " with slot " + _answered + " at (" + p.x.ToString("0.0") + ", "
                        + p.y.ToString("0.0") + ", " + p.z.ToString("0.0") + ") " + how + "; the game's own RPC puts this player there on every machine");
                return false;
            }
            catch (Exception e) { _log?.Warning("[spawn] answer: " + e.Message); return true; }
        }

        static Transform NextSlot(Vector3 marked, out string how)
        {
            how = "";
            EnsureHolder();
            var refused = new System.Text.StringBuilder();
            int tried = 0;

            for (int ring = -1; ring < Rings.Length; ring++)
            {
                int count = ring < 0 ? 1 : 8;
                for (int a = 0; a < count; a++)
                {
                    Vector3 c = marked;
                    if (ring >= 0)
                    {
                        float ang = (a * 45f + ring * 22.5f) * Mathf.Deg2Rad;
                        c = marked + new Vector3(Mathf.Sin(ang) * Rings[ring], 0f, Mathf.Cos(ang) * Rings[ring]);
                    }
                    tried++;
                    string why;
                    Vector3 pos;
                    if (!Clear(c, marked, out pos, out why))
                    {
                        if (refused.Length < 300) refused.Append(ring < 0 ? "marked: " : "r" + Rings[ring].ToString("0.0") + "/" + a + ": ").Append(why).Append("; ");
                        continue;
                    }
                    if (Taken(pos)) continue;
                    var go = new GameObject("ODG_SpawnSlot_" + (_slots.Count + 1));
                    go.transform.SetParent(_holder.transform, true);
                    go.transform.position = pos;
                    go.transform.rotation = Facing(pos, marked);
                    _slots.Add(go.transform);
                    how = (ring < 0 ? "(the marked spawn itself" : "(ring " + Rings[ring].ToString("0.0") + " m, bearing " + (a * 45 + ring * 22.5f).ToString("0") + " deg")
                        + (refused.Length > 0 ? "; refused " + refused.ToString().TrimEnd(' ', ';') : "") + ")";
                    return go.transform;
                }
            }
            how = tried + " candidate(s) refused: " + refused.ToString().TrimEnd(' ', ';');
            return null;
        }

        static void EnsureHolder()
        {
            if (_holder != null) return;
            _holder = new GameObject("ODG_SpawnSlots");
            _slots.Clear();
        }

        static bool Taken(Vector3 pos)
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                var s = _slots[i];
                if (s == null) continue;
                var q = s.position;
                float dx = q.x - pos.x, dz = q.z - pos.z;
                if (dx * dx + dz * dz < 1.2f * 1.2f) return true;
            }
            return false;
        }

        static bool Clear(Vector3 c, Vector3 marked, out Vector3 pos, out string why)
        {
            pos = c; why = "";
            bool found;

            var g = MapRuntime.GroundedSpawn(new Vector3(c.x, marked.y, c.z), out found);
            if (!found) { why = "no floor of ours"; return false; }
            float floorY = g.y - 1.1f;
            if (floorY - marked.y > AboveMarkedMaxM)
            {
                why = "floor " + (floorY - marked.y).ToString("0.0") + " m above the marked height (standing on something)";
                return false;
            }
            var feet = new Vector3(c.x, floorY, c.z);
            try
            {
                var hits = Physics.OverlapCapsule(feet + Vector3.up * 0.5f, feet + Vector3.up * 1.6f, 0.3f, ~0, QueryTriggerInteraction.Ignore);
                if (hits != null && hits.Length > 0)
                {
                    string n = "?";
                    try { n = hits[0] != null ? hits[0].name : "?"; } catch { }
                    why = "blocked by '" + n + "'" + (hits.Length > 1 ? " +" + (hits.Length - 1) : "");
                    return false;
                }
            }
            catch (Exception e) { why = "overlap test failed: " + e.Message; return false; }
            pos = feet + Vector3.up * 0.15f;
            return true;
        }

        static Quaternion Facing(Vector3 pos, Vector3 marked)
        {
            try
            {
                var centre = MapRuntime.Bounds().center;
                var d = centre - marked; d.y = 0f;
                if (d.sqrMagnitude < 1f) return Quaternion.identity;
                return Quaternion.LookRotation(d.normalized, Vector3.up);
            }
            catch { return Quaternion.identity; }
        }

        public static string Console()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("spawn slots: ").Append(_hooks).Append("/2 hooks; ").Append(_answered).Append(" quer").Append(_answered == 1 ? "y" : "ies")
              .Append(" answered this level");
            Vector3 m;
            if (MapRuntime.TryMarkedSpawn(out m)) sb.Append("; marked spawn (").Append(m.x.ToString("0.0")).Append(", ").Append(m.z.ToString("0.0")).Append(")");
            for (int i = 0; i < _slots.Count; i++)
            {
                var s = _slots[i];
                if (s == null) continue;
                var p = s.position;
                sb.Append("\n  slot ").Append(i + 1).Append(": (").Append(p.x.ToString("0.0")).Append(", ").Append(p.y.ToString("0.0")).Append(", ").Append(p.z.ToString("0.0")).Append(")");
            }
            return sb.ToString();
        }
    }
}
