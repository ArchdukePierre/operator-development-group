// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace MapLoader
{

    public static class HostFoliage
    {
        const string ManagerName = "Speed Tree Proxy Manager";

        const float MaxDrop = 40f;
        const float Settle  = 0.10f;
        const float Clear   = 2f;

        static MelonLogger.Instance _log;
        static Transform _manager;
        static float _nextScan;
        static bool  _rootMode;
        static float _rootShift;
        static int   _snapped, _reverted, _passes, _failedSamples;

        static readonly Dictionary<int, float> _placed = new Dictionary<int, float>();

        static MelonPreferences_Entry<bool> _enabled;

        public static void Init(MelonLogger.Instance log)
        {
            _log = log;
            try
            {
                var cfg = MelonPreferences.GetCategory("MapLoader")
                       ?? MelonPreferences.CreateCategory("MapLoader");
                _enabled = cfg.GetEntry<bool>("HostFoliageGrounding")
                        ?? cfg.CreateEntry("HostFoliageGrounding", false,
                             description: "Try to lower the host's SpeedTree proxies onto our mesh. "
                                        + "Off: they are pooled proxies the game repositions every "
                                        + "frame, so moving them does nothing useful. Use the ground "
                                        + "skirt instead.");
            }
            catch { }
        }

        public static void Reset()
        {

            for (int i = 0; i < _hidden.Count; i++)
            {
                try { if (_hidden[i] != null) _hidden[i].forceRenderingOff = false; } catch { }
            }
            _hidden.Clear();
            _hiddenReported = 0;
            _manager = null; _rootMode = false; _rootShift = 0f;
            _snapped = _reverted = _passes = _failedSamples = 0;
            _placed.Clear();
            _nextScan = 0f;
        }

        public static void Tick(bool mapBuilt)
        {
            if (!mapBuilt) return;
            if (_enabled == null || !_enabled.Value) return;
            if (Time.time < _nextScan) return;
            _nextScan = Time.time + 2f;
            try { Pass(); }
            catch (Exception e) { _log?.Warning("[hostveg] pass: " + e.Message); }
        }

        static bool FindManager()
        {
            if (_manager != null) return true;
            try
            {
                var go = GameObject.Find(ManagerName);
                if (go == null) return false;
                _manager = go.transform;
                _log?.Msg("[hostveg] found '" + ManagerName + "' with "
                        + _manager.childCount + " child root(s). Grounding it onto our mesh.");
                return true;
            }
            catch { return false; }
        }

        static void Pass()
        {
            if (!FindManager()) return;
            if (MapRuntime.OurRoot == null) return;

            var rends = _manager.GetComponentsInChildren<Renderer>(false);
            if (rends == null || rends.Length == 0) return;

            if (_rootMode) { HoldRootOffset(rends); return; }

            var hits = new Il2CppStructArray<RaycastHit>(32L);
            int moved = 0, reverted = 0, noGround = 0, seated = 0;
            float sumDrop = 0f;

            for (int i = 0; i < rends.Length; i++)
            {
                var r = rends[i];
                if (r == null) continue;
                var t = r.transform;
                if (t == null) continue;

                float drop; bool groundless;
                if (!DropFor(r, hits, out drop, out groundless))
                {
                    if (groundless) { HideGroundless(r); noGround++; }
                    continue;
                }

                int id = t.GetInstanceID();
                float lastDrop;
                if (_placed.TryGetValue(id, out lastDrop))
                {

                    if (drop > lastDrop + 0.5f) reverted++;
                    else if (drop <= Settle) { seated++; continue; }
                }

                if (drop <= Settle) continue;

                var p = t.position;
                p.y -= drop;
                t.position = p;
                _placed[id] = 0f;
                sumDrop += drop;
                moved++;
            }

            ReportHidden();
            _passes++;
            _snapped  += moved;
            _reverted += reverted;

            if (moved > 0)
                _log?.Msg("[hostveg] lowered " + moved + " host prop(s), average "
                        + (sumDrop / moved).ToString("F1") + " m"
                        + (noGround > 0 ? "; " + noGround + " with no surface of ours beneath" : "")
                        + (seated   > 0 ? "; " + seated + " already seated" : ""));

            if (reverted > 0 && reverted >= moved && _passes >= 2)
                EnterRootMode(rends, hits);
        }

        static bool DropFor(Renderer r, Il2CppStructArray<RaycastHit> hits, out float drop)
        {
            bool groundless;
            return DropFor(r, hits, out drop, out groundless);
        }

        static bool DropFor(Renderer r, Il2CppStructArray<RaycastHit> hits, out float drop,
                            out bool groundless)
        {
            drop = 0f; groundless = false;
            Bounds b;
            try { b = r.bounds; } catch { return false; }
            if (b.size.y <= 0f && b.size.x <= 0f) return false;

            float ground;
            if (!GroundUnder(b, hits, out ground)) { groundless = true; return false; }
            drop = b.min.y - ground;
            if (drop <= 0f) return false;
            if (drop > MaxDrop) { groundless = true; return false; }
            return true;
        }

        static readonly List<Renderer> _hidden = new List<Renderer>();
        static int _hiddenReported;

        static void HideGroundless(Renderer r)
        {
            try
            {
                if (r == null || r.forceRenderingOff) return;
                r.forceRenderingOff = true;
                _hidden.Add(r);
            }
            catch { }
        }

        static void ReportHidden()
        {
            if (_hidden.Count == _hiddenReported) return;
            _hiddenReported = _hidden.Count;
            _log?.Msg("[hostveg] " + _hidden.Count + " host prop(s) switched off: nothing of "
                    + "ours beneath them (past our terrain, or more than " + MaxDrop
                    + " m up). Seating them was impossible and leaving them was the "
                    + "floating trees at the edge of the map. Put back on teardown.");
        }

        static bool GroundUnder(Bounds b, Il2CppStructArray<RaycastHit> hits, out float y)
        {
            y = 0f;
            var origin = new Vector3(b.center.x, b.max.y + Clear, b.center.z);
            float len = (b.max.y - b.min.y) + Clear + MaxDrop;

            int n = Physics.RaycastNonAlloc(origin, Vector3.down, hits, len,
                                            ~0, QueryTriggerInteraction.Ignore);
            if (n > hits.Length) n = hits.Length;

            float best = float.NegativeInfinity;
            bool found = false;
            for (int k = 0; k < n; k++)
            {
                var h = hits[k];

                if (!MapRuntime.IsOurSurface(h.collider)) continue;
                float hy = h.point.y;
                if (hy <= b.min.y + 0.01f && hy > best) { best = hy; found = true; }
            }
            if (!found) return false;
            y = best;
            return true;
        }

        static void EnterRootMode(Renderer[] rends, Il2CppStructArray<RaycastHit> hits)
        {
            float shift;
            if (!MedianDrop(rends, hits, out shift))
            {

                _failedSamples++;
                if (_failedSamples == 1 || _failedSamples % 30 == 0)
                    _log?.Warning("[hostveg] per-instance placement is being reverted, and no "
                                + "usable ground sample was found this pass (" + _failedSamples
                                + " so far). Retrying.");
                return;
            }

            Apply(shift);
            _rootMode = true;
            _log?.Msg("[hostveg] the manager keeps rewriting its own children, so the root moved "
                    + "instead: down " + shift.ToString("F1") + " m. This survives proxy rebuilds.");
        }

        static bool MedianDrop(Renderer[] rends, Il2CppStructArray<RaycastHit> hits, out float median)
        {
            median = 0f;
            var drops = new List<float>();
            for (int i = 0; i < rends.Length; i++)
            {
                var r = rends[i];
                if (r == null) continue;
                float d;
                if (DropFor(r, hits, out d) && d > Settle) drops.Add(d);
            }
            if (drops.Count == 0) return false;
            drops.Sort();
            median = drops[drops.Count / 2];
            return median > Settle;
        }

        static void Apply(float shift)
        {
            var p = _manager.position;
            p.y -= shift;
            _manager.position = p;
            _rootShift += shift;
        }

        static void HoldRootOffset(Renderer[] rends)
        {
            var hits = new Il2CppStructArray<RaycastHit>(32L);

            for (int i = 0; i < rends.Length; i++)
            {
                float d0; bool gl;
                if (rends[i] != null && !DropFor(rends[i], hits, out d0, out gl) && gl)
                    HideGroundless(rends[i]);
            }
            ReportHidden();
            float d;
            if (!MedianDrop(rends, hits, out d)) return;
            if (d <= 1f) return;

            Apply(d);
            _log?.Msg("[hostveg] foliage drifted back up; re-lowered " + d.ToString("F1")
                    + " m (total " + _rootShift.ToString("F1") + " m)");
        }

        static string Safe(Func<string> f) { try { return f() ?? "?"; } catch { return "?"; } }

        public static void Report()
        {
            if (_manager == null)
            {
                _log?.Msg("[hostveg] '" + ManagerName + "' not found in this scene.");
                return;
            }

            _log?.Msg("[hostveg] mode=" + (_rootMode ? "root offset" : "per-instance")
                    + "  totalShift=" + _rootShift.ToString("F1") + " m"
                    + "  lowered=" + _snapped + "  reverted=" + _reverted
                    + "  passes=" + _passes + "  failedSamples=" + _failedSamples);

            try
            {
                var rends = _manager.GetComponentsInChildren<Renderer>(false);
                var hits = new Il2CppStructArray<RaycastHit>(32L);
                _log?.Msg("[hostveg] ---- inventory of " + rends.Length + " renderer(s) ----");

                int shown = 0;
                for (int i = 0; i < rends.Length && shown < 24; i++)
                {
                    var r = rends[i];
                    if (r == null) { _log?.Msg("[hostveg]   [" + i + "] <null>"); continue; }
                    shown++;

                    string nm = "?", tp = "?";
                    Vector3 tpos = Vector3.zero;
                    Bounds b = new Bounds();
                    bool okB = false;
                    try { nm = r.gameObject.name; } catch { }
                    try { tp = r.GetIl2CppType().Name; } catch { }
                    try { tpos = r.transform.position; } catch { }
                    try { b = r.bounds; okB = true; } catch { }

                    _log?.Msg("[hostveg]   [" + i + "] '" + nm + "' " + tp
                            + "  enabled=" + Safe(() => r.enabled.ToString())
                            + "  tPos=(" + tpos.x.ToString("F1") + "," + tpos.y.ToString("F1")
                            + "," + tpos.z.ToString("F1") + ")");
                    if (!okB) { _log?.Msg("[hostveg]        bounds unreadable"); continue; }

                    _log?.Msg("[hostveg]        bounds c=(" + b.center.x.ToString("F1") + ","
                            + b.center.y.ToString("F1") + "," + b.center.z.ToString("F1")
                            + ") size=(" + b.size.x.ToString("F0") + "," + b.size.y.ToString("F0")
                            + "," + b.size.z.ToString("F0") + ")"
                            + " minY=" + b.min.y.ToString("F1") + " maxY=" + b.max.y.ToString("F1"));

                    var origin = new Vector3(b.center.x, b.max.y + Clear, b.center.z);
                    float len = (b.max.y - b.min.y) + Clear + MaxDrop;
                    int n = Physics.RaycastNonAlloc(origin, Vector3.down, hits, len,
                                                    ~0, QueryTriggerInteraction.Ignore);
                    if (n > hits.Length) n = hits.Length;
                    if (n == 0) { _log?.Msg("[hostveg]        ray down from centre: NOTHING hit in " + len.ToString("F0") + " m"); continue; }

                    var sb = new System.Text.StringBuilder();
                    for (int k = 0; k < n && k < 6; k++)
                    {
                        var h = hits[k];
                        string cn = "?";
                        try { cn = h.collider.gameObject.name; } catch { }
                        bool ours = false;
                        try { ours = MapRuntime.IsOurSurface(h.collider); } catch { }
                        sb.Append("  y=").Append(h.point.y.ToString("F1"))
                          .Append(ours ? " OURS '" : " host '").Append(cn).Append('\'');
                    }
                    _log?.Msg("[hostveg]        ray hit " + n + ":" + sb.ToString());
                }
                _log?.Msg("[hostveg] ---- end inventory ----");
            }
            catch (Exception e) { _log?.Warning("[hostveg] inventory: " + e.Message); }
        }
    }
}
