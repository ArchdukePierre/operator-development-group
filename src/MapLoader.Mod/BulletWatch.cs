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

    static class BulletWatch
    {
        static MelonLogger.Instance _log;
        static bool _installed;

        class Tally
        {
            public string Key, Line;
            public int Count;
            public Vector3 First, Last;
            public float FirstDist, LastDist;
        }

        static readonly Dictionary<string, Tally> _tally = new Dictionary<string, Tally>();
        static readonly List<Tally> _order = new List<Tally>();
        static int _hits, _drawn, _suspect;
        static float _lastLogAt; static int _burst;
        static float _nextSummary;
        static string _scene = "";
        static bool _lod0;
        static bool _trunksOff;

        public static void Install(HarmonyLib.Harmony h, MelonLogger.Instance log)
        {
            _log = log;

            int bound = 0;
            try
            {
                var mi = AccessTools.Method(typeof(Il2Cpp.BulletInstance), "ProcessBullet");
                if (mi == null) _log?.Warning("[hits] BulletInstance.ProcessBullet not found -- the managed bullet path is NOT watched, which is the one that fires");
                else { h.Patch(mi, postfix: new HarmonyMethod(typeof(BulletWatch), nameof(Post_Managed))); bound++; }
            }
            catch (Exception e) { _log?.Warning("[hits] ProcessBullet hook: " + e.Message); }

            try
            {
                var m = AccessTools.Method(typeof(Il2Cpp.BulletRB), "OnCollisionEnter");
                if (m != null) { h.Patch(m, postfix: new HarmonyMethod(typeof(BulletWatch), nameof(Post_Collision))); bound++; }
            }
            catch (Exception e) { _log?.Warning("[hits] BulletRB hook: " + e.Message); }

            if (bound == 0) { _log?.Warning("[hits] no bullet path could be watched"); return; }
            _installed = true;
            _log?.Msg("[hits] watching " + bound + " bullet path(s), BulletInstance.ProcessBullet first: "
                    + "every bullet stop on an ODG map is classified (drawn / not drawn / ours); "
                    + "`odg hits`, `odg lod0`, `odg trunks`");
        }

        public static void ResetForNewLevel(string why)
        {
            if (_hits > 0) _log?.Msg("[hits] cleared (" + why + "): " + _hits + " hit(s), " + _suspect + " on things not drawn");
            _tally.Clear(); _order.Clear();
            _hits = _drawn = _suspect = 0; _burst = 0;
            _nextSummary = 0f;
            _lod0 = false; _trunksOff = false;
        }

        static bool OurMap()
        {
            try { return MapRuntime.Done; } catch { return false; }
        }

        public static void Post_Managed(Il2Cpp.BulletInstance __instance)
        {
            try
            {
                if (__instance == null) return;
                var h = __instance.hit;
                var c = h.collider;
                if (c == null) return;
                Record(c, h.point);
            }
            catch { }
        }

        static void Post_Collision(Il2Cpp.BulletRB __instance, Collision hit)
        {
            try
            {
                if (hit == null) return;
                var c = hit.collider;
                if (c == null) return;
                Vector3 p = Vector3.zero;
                try { p = hit.GetContact(0).point; } catch { try { p = __instance.transform.position; } catch { } }
                Record(c, p);
            }
            catch { }
        }

        static void Record(Collider c, Vector3 p)
        {
            try
            {
                if (!_installed || !OurMap()) return;
                string scene = "";
                try { scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; } catch { }
                if (scene != _scene) { ResetForNewLevel("scene changed to '" + scene + "'"); _scene = scene; }
                var go = c.gameObject;
                if (go == null) return;
                _hits++;

                string where, what;
                bool drawn = Classify(go, c, out where, out what);
                if (drawn) { _drawn++; return; }
                _suspect++;

                string key = where + "|" + go.name + "|" + what;
                float dist = float.NaN;
                Vector3 me;
                if (PlayerBody.TryPosition(out me)) dist = Vector3.Distance(me, p);
                Tally t;
                if (!_tally.TryGetValue(key, out t))
                {
                    t = new Tally { Key = key, First = p, FirstDist = dist, Line = go.name + " [" + where + "] " + what + " (layer " + LayerMask.LayerToName(go.layer) + ", " + Chain(go.transform) + ")" };
                    _tally[key] = t; _order.Add(t);
                    float now = Time.realtimeSinceStartup;
                    if (now - _lastLogAt > 1f) _burst = 0;
                    _lastLogAt = now;
                    if (++_burst <= 6 && _order.Count <= 80)
                        _log?.Msg("[hits] bullet stopped by " + t.Line + " at (" + p.x.ToString("0.0") + ", " + p.y.ToString("0.0") + ", " + p.z.ToString("0.0") + ")"
                                + (float.IsNaN(dist) ? "" : ", " + dist.ToString("0") + " m from you"));
                }
                t.Count++; t.Last = p; t.LastDist = dist;
            }
            catch { }
        }

        public static void Tick()
        {
            if (!_installed || _suspect == 0) return;
            float now = Time.realtimeSinceStartup;
            if (_nextSummary == 0f) { _nextSummary = now + 60f; return; }
            if (now < _nextSummary) return;
            _nextSummary = now + 60f;
            _log?.Msg("[hits] last minute: " + _hits + " bullet stop(s) so far, " + _drawn + " on drawn surfaces, " + _suspect + " on things NOT drawn -- " + Top(5));
        }

        static bool IsABody(GameObject go, Collider c)
        {
            try
            {
                if (LayerMask.LayerToName(go.layer) == "Hitbox") return true;
                if (go.GetComponentInParent<Il2Cpp.Health>() != null) return true;
                if (go.GetComponentInParent<Il2Cpp.BrainAI>() != null) return true;
                if (go.GetComponentInParent<Il2Cpp.PlayerNetworking>() != null) return true;
            }
            catch { }
            return false;
        }

        static bool Classify(GameObject go, Collider c, out string where, out string what)
        {
            where = "?"; what = "?";
            try
            {

                if (IsABody(go, c)) { where = "a body"; what = "hit a character"; return true; }

                var root = MapRuntime.Root;
                bool ours = root != null && go.transform.IsChildOf(root.transform);
                bool additive = false;
                try { additive = ShellSwap.IsAdditiveWorld(go.scene); } catch { }
                where = ours ? "ours" : additive ? "the map's own world" : "host level '" + go.scene.name + "'";
                if (c.isTrigger) { what = "a TRIGGER (bullets should not stop here)"; return false; }

                if (ours)
                {
                    string n = go.name.ToLowerInvariant();
                    if (n.Contains("trunk")) { what = "our trunk collision (invisible by design; `odg trunks off` to test)"; return false; }
                    if (n.Contains("barrier")) { what = "our rim barrier (invisible by design)"; return false; }

                    if (n == "platefill" || n == "stairfill") { what = "our floor thickening (invisible by design, under the walkable top)"; return true; }
                }

                var rs = go.GetComponents<Renderer>();
                if (rs == null || rs.Length == 0)
                {

                    var lg = go.GetComponentInParent<LODGroup>();
                    if (lg != null)
                    {
                        int total = 0, vis = 0, lods = 0;
                        try
                        {
                            var arr = lg.GetLODs();
                            lods = arr != null ? arr.Length : 0;
                            for (int i = 0; i < lods; i++)
                            {
                                var lr = arr[i].renderers;
                                for (int k = 0; k < (lr != null ? lr.Length : 0); k++)
                                {
                                    if (lr[k] == null) continue;
                                    total++;
                                    if (lr[k].enabled && lr[k].isVisible) vis++;
                                }
                            }
                        }
                        catch { }
                        var mc = c.TryCast<MeshCollider>();
                        string mesh = "";
                        try { if (mc != null && mc.sharedMesh != null) mesh = " mesh '" + mc.sharedMesh.name + "'"; } catch { }
                        what = "collision proxy" + mesh + " under LOD group '" + lg.name + "' (" + lods + " LOD(s), " + vis + " of " + total + " renderer(s) drawn now" + (_lod0 ? ", LOD0 forced" : "") + ")";

                        return false;
                    }
                    var pr = go.GetComponentInParent<Renderer>();
                    if (pr != null) { what = "collider-only object; nearest drawing parent '" + pr.gameObject.name + "' " + (pr.enabled && pr.isVisible ? "drawn" : "NOT drawn"); return pr.enabled && pr.isVisible; }
                    what = "collider-only object, nothing draws it";
                    return false;
                }

                bool anyEnabled = false, anyVisible = false, clearMat = false;
                for (int i = 0; i < rs.Length; i++)
                {
                    var r = rs[i];
                    if (r == null) continue;
                    if (r.enabled) anyEnabled = true;
                    bool reallyOff = false;
                    try { reallyOff = r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly; } catch { }
                    try { if (r.forceRenderingOff) reallyOff = true; } catch { }
                    if (r.enabled && !reallyOff) anyVisible = true;
                    try
                    {
                        var m = r.sharedMaterial;
                        if (m != null && m.name != null && m.name.Contains("Barrier_Void")) clearMat = true;
                    }
                    catch { }
                }
                if (clearMat) { what = "invisible material Barrier_Void"; return false; }
                if (!anyEnabled) { what = "renderer DISABLED"; return false; }
                if (!anyVisible) { what = "renderer enabled but NOT drawn this frame (culled by distance or occlusion)"; return false; }
                what = "drawn";
                return true;
            }
            catch { what = "classify failed"; return true; }
        }

        static string Chain(Transform t)
        {
            try
            {
                string s = t.name; int hops = 0;
                while (t.parent != null && hops++ < 3) { t = t.parent; s = t.name + "/" + s; }
                return s;
            }
            catch { return "?"; }
        }

        static string Top(int n)
        {
            var list = new List<Tally>(_order);
            list.Sort((a, b) => b.Count.CompareTo(a.Count));
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < list.Count && i < n; i++)
            {
                if (i > 0) sb.Append("; ");
                sb.Append(list[i].Count).Append("x ").Append(list[i].Line);
            }
            return sb.ToString();
        }

        static int ForceLod(int level)
        {
            int n = 0;
            try
            {
                var all = Resources.FindObjectsOfTypeAll<LODGroup>();
                for (int i = 0; i < (all != null ? all.Length : 0); i++)
                {
                    var lg = all[i];
                    if (lg == null || lg.gameObject == null) continue;
                    bool inWorld = false;
                    try { inWorld = ShellSwap.IsAdditiveWorld(lg.gameObject.scene) || (MapRuntime.Root != null && lg.transform.IsChildOf(MapRuntime.Root.transform)); } catch { }
                    if (!inWorld) continue;
                    try { lg.ForceLOD(level); n++; } catch { }
                }
            }
            catch (Exception e) { _log?.Warning("[hits] force LOD: " + e.Message); }
            return n;
        }

        static int Trunks(bool enable)
        {
            int n = 0;
            try
            {
                var root = MapRuntime.Root;
                if (root == null) return 0;
                var cols = root.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < (cols != null ? cols.Length : 0); i++)
                {
                    var c = cols[i];
                    if (c == null) continue;
                    if (!c.gameObject.name.ToLowerInvariant().Contains("trunk")) continue;
                    c.enabled = enable; n++;
                }
            }
            catch (Exception e) { _log?.Warning("[hits] trunks: " + e.Message); }
            return n;
        }

        public static string Console(string cmd, string arg)
        {
            arg = (arg ?? "").Trim().ToLowerInvariant();
            if (cmd == "lod0")
            {
                if (arg == "off") { int n = ForceLod(-1); _lod0 = false; return "LOD groups released: " + n + " back to distance-based LODs"; }
                int k = ForceLod(0); _lod0 = true;
                return "LOD0 forced on " + k + " LOD group(s) in the world: collision and visual now agree. `odg lod0 off` releases them. If the invisible hits stop, this is the cause.";
            }
            if (cmd == "trunks")
            {
                if (arg == "on") { int n = Trunks(true); _trunksOff = false; return "trunk collision ON again (" + n + " collider(s))"; }
                int m = Trunks(false); _trunksOff = true;
                return "trunk collision OFF (" + m + " collider(s)): bullets and bodies pass through our tree trunks until `odg trunks on`. If the trees stop stopping bullets, the cylinders are the cause.";
            }
            if (arg == "reset") { ResetForNewLevel("console"); return "hit tally cleared"; }
            if (!_installed) return "bullet watch is not installed (BulletRB.OnCollisionEnter was not found)";
            var sb = new System.Text.StringBuilder();
            sb.Append("bullet stops on this map: ").Append(_hits).Append(", ").Append(_drawn).Append(" on drawn surfaces, ").Append(_suspect).Append(" on things not drawn")
              .Append(_lod0 ? "; LOD0 forced" : "").Append(_trunksOff ? "; trunk collision off" : "");
            var list = new List<Tally>(_order);
            list.Sort((a, b) => b.Count.CompareTo(a.Count));
            for (int i = 0; i < list.Count && i < 25; i++)
            {
                var t = list[i];
                sb.Append("\n  ").Append(t.Count).Append("x ").Append(t.Line)
                  .Append("  first at (").Append(t.First.x.ToString("0")).Append(", ").Append(t.First.y.ToString("0.0")).Append(", ").Append(t.First.z.ToString("0")).Append(")")
                  .Append(float.IsNaN(t.FirstDist) ? "" : " " + t.FirstDist.ToString("0") + " m");
            }
            if (list.Count == 0) sb.Append("\n  nothing undrawn has stopped a bullet yet");
            return sb.ToString();
        }
    }
}
