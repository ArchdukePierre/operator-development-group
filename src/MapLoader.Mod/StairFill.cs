// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace MapLoader
{

    internal static class StairFill
    {
        internal sealed class Step
        {
            public float Y;
            public float Lo, Hi;
            public float W0, W1;
        }
        internal sealed class Flight
        {
            public int Axis;
            public int Dir;
            public float Rise;
            public float Run;
            public float Depth;
            public readonly List<Step> Steps = new List<Step>();
        }

        sealed class Cluster
        {
            public float Area, SumY;
            public float MinX = float.MaxValue, MaxX = float.MinValue;
            public float MinZ = float.MaxValue, MaxZ = float.MinValue;
            public float Y { get { return Area > 0f ? SumY / Area : 0f; } }
            public float Cx { get { return 0.5f * (MinX + MaxX); } }
            public float Cz { get { return 0.5f * (MinZ + MaxZ); } }
            public float Lo(int axis) { return axis == 0 ? MinX : MinZ; }
            public float Hi(int axis) { return axis == 0 ? MaxX : MaxZ; }
            public float C(int axis) { return axis == 0 ? Cx : Cz; }
            public float Ext(int axis) { return Hi(axis) - Lo(axis); }
        }

        const float ClusterM = 0.03f;
        const float SameLevelM = 0.045f;
        const float MinArea = 0.03f;
        const float MinRise = 0.12f, MaxRise = 0.65f;
        const float MinRun = 0.14f, MaxRun = 0.75f;
        const float MaxTreadDepth = 0.8f;
        const float MinWidth = 0.4f;
        const float Thick = 0.08f;
        const float MidRiseM = 0.42f;

        static MelonLogger.Instance _log;
        static readonly Dictionary<string, List<Flight>> _byMesh =
            new Dictionary<string, List<Flight>>(StringComparer.Ordinal);
        static int _instances, _boxes, _midBoxes;
        static readonly HashSet<string> _meshesFilled = new HashSet<string>(StringComparer.Ordinal);

        internal static void Init(MelonLogger.Instance log) { _log = log; }

        internal static bool IsStair(string id)
        {
            return id != null && id.IndexOf("Stair", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static string Key(string meshId)
        {
            string dir = null;
            try { dir = MapIdentity.BundleDir; } catch { }
            return (dir ?? "") + "|" + meshId;
        }

        internal static void Analyse(string meshId, MapBundle.Geometry g)
        {
            if (!IsStair(meshId) || g == null) return;
            string key = Key(meshId);
            if (_byMesh.ContainsKey(key)) return;
            List<Flight> flights = null;
            try { flights = Find(g); }
            catch (Exception e)
            {
                _log?.Warning("[stairfill] " + meshId + ": " + e.Message);
            }
            flights = flights ?? new List<Flight>();
            _byMesh[key] = flights;
            if (flights.Count == 0) return;
            var sb = new System.Text.StringBuilder();
            sb.Append("[stairfill] ").Append(meshId).Append(": ").Append(flights.Count)
              .Append(" flight(s):");
            for (int i = 0; i < flights.Count; i++)
            {
                var f = flights[i];
                sb.Append(i == 0 ? " " : "; ").Append(f.Steps.Count).Append(" steps, rise ")
                  .Append(f.Rise.ToString("F2")).Append(" m, run ").Append(f.Run.ToString("F2"))
                  .Append(" m, treads ").Append(f.Depth.ToString("F2")).Append(" m deep -> ")
                  .Append(Mathf.Max(0f, f.Run - f.Depth).ToString("F2")).Append(" m open per step");
            }
            _log?.Msg(sb.ToString());
        }

        static List<Flight> Find(MapBundle.Geometry g)
        {
            var pos = g.Positions; var idx = g.Indices;
            var buckets = new Dictionary<int, Cluster>();
            for (int i = 0; i + 2 < idx.Length; i += 3)
            {
                int a = idx[i] * 3, b = idx[i + 1] * 3, c = idx[i + 2] * 3;
                float ax = pos[a], ay = pos[a + 1], az = pos[a + 2];
                float bx = pos[b], by = pos[b + 1], bz = pos[b + 2];
                float cx = pos[c], cy = pos[c + 1], cz = pos[c + 2];
                float ux = bx - ax, uy = by - ay, uz = bz - az;
                float vx = cx - ax, vy = cy - ay, vz = cz - az;
                float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                float len2 = nx * nx + ny * ny + nz * nz;
                if (len2 < 1e-12f) continue;
                float len = Mathf.Sqrt(len2);

                if (ny / len < 0.9f) continue;
                float area = 0.5f * len;
                float y = (ay + by + cy) / 3f;
                int k = Mathf.FloorToInt(y / ClusterM);
                Cluster cl;
                if (!buckets.TryGetValue(k, out cl)) { cl = new Cluster(); buckets[k] = cl; }
                cl.Area += area; cl.SumY += y * area;
                cl.MinX = Mathf.Min(cl.MinX, Mathf.Min(ax, Mathf.Min(bx, cx)));
                cl.MaxX = Mathf.Max(cl.MaxX, Mathf.Max(ax, Mathf.Max(bx, cx)));
                cl.MinZ = Mathf.Min(cl.MinZ, Mathf.Min(az, Mathf.Min(bz, cz)));
                cl.MaxZ = Mathf.Max(cl.MaxZ, Mathf.Max(az, Mathf.Max(bz, cz)));
            }
            var all = new List<Cluster>();
            foreach (var kv in buckets) if (kv.Value.Area >= MinArea) all.Add(kv.Value);
            all.Sort((p, q) => p.Y.CompareTo(q.Y));

            var groups = new List<List<Cluster>>();
            for (int i = 0; i < all.Count; i++)
            {
                if (groups.Count > 0)
                {
                    var last = groups[groups.Count - 1];
                    if (all[i].Y - last[last.Count - 1].Y < SameLevelM) { last.Add(all[i]); continue; }
                }
                groups.Add(new List<Cluster> { all[i] });
            }

            var flights = new List<Flight>();
            int gi = 0;
            while (gi < groups.Count)
            {
                Flight best = null;
                int bestEnd = gi;

                for (int s = 0; s < groups[gi].Count; s++)
                {
                    var start = groups[gi][s];
                    if (start.Ext(0) > MaxTreadDepth && start.Ext(2) > MaxTreadDepth) continue;
                    var f = new Flight();
                    var steps = new List<Cluster> { start };
                    int axis = -1, dir = 0; float rise0 = 0f;
                    int gj = gi + 1;
                    while (gj < groups.Count)
                    {
                        Cluster pick = null; float pickArea = 0f;
                        int pAxis = axis, pDir = dir;
                        foreach (var cand in groups[gj])
                        {
                            int cAxis = axis, cDir = dir;
                            if (!Valid(steps[steps.Count - 1], cand, ref cAxis, ref cDir, rise0)) continue;
                            if (cand.Area > pickArea) { pick = cand; pickArea = cand.Area; pAxis = cAxis; pDir = cDir; }
                        }
                        if (pick == null) break;
                        if (axis < 0) { axis = pAxis; dir = pDir; rise0 = pick.Y - start.Y; }
                        steps.Add(pick);
                        gj++;
                    }
                    if (steps.Count >= 3 && (best == null || steps.Count > best.Steps.Count))
                    {
                        f.Axis = axis; f.Dir = dir;
                        float rs = 0f, rn = 0f, dp = 0f;
                        for (int k = 0; k < steps.Count; k++)
                        {
                            var c = steps[k];
                            f.Steps.Add(new Step
                            {
                                Y = c.Y, Lo = c.Lo(axis), Hi = c.Hi(axis),
                                W0 = c.Lo(axis == 0 ? 2 : 0), W1 = c.Hi(axis == 0 ? 2 : 0)
                            });
                            dp += c.Ext(axis);
                            if (k > 0) { rs += c.Y - steps[k - 1].Y; rn += Mathf.Abs(c.C(axis) - steps[k - 1].C(axis)); }
                        }
                        f.Rise = rs / (steps.Count - 1); f.Run = rn / (steps.Count - 1);
                        f.Depth = dp / steps.Count;
                        best = f; bestEnd = gj;
                    }
                }
                if (best != null) { flights.Add(best); gi = bestEnd; }
                else gi++;
            }
            return flights;
        }

        static bool Valid(Cluster prev, Cluster cand, ref int axis, ref int dir, float rise0)
        {
            float rise = cand.Y - prev.Y;
            if (rise < MinRise || rise > MaxRise) return false;
            if (rise0 > 0f && Mathf.Abs(rise - rise0) > 0.35f * rise0) return false;
            int ax = axis;
            float dx = Mathf.Abs(cand.Cx - prev.Cx), dz = Mathf.Abs(cand.Cz - prev.Cz);
            if (ax < 0) ax = dx >= dz ? 0 : 2;
            float run = ax == 0 ? dx : dz;
            if (run < MinRun || run > MaxRun) return false;
            int d = (cand.C(ax) - prev.C(ax)) >= 0f ? 1 : -1;
            if (dir != 0 && d != dir) return false;
            if (cand.Ext(ax) > MaxTreadDepth) return false;
            int wa = ax == 0 ? 2 : 0;
            float overlap = Mathf.Min(cand.Hi(wa), prev.Hi(wa)) - Mathf.Max(cand.Lo(wa), prev.Lo(wa));
            if (overlap < MinWidth) return false;
            axis = ax; dir = d;
            return true;
        }

        internal static void Attach(GameObject go, string meshId, float scaleY)
        {
            if (go == null || !IsStair(meshId)) return;
            List<Flight> flights;
            if (!_byMesh.TryGetValue(Key(meshId), out flights) || flights.Count == 0) return;
            try
            {
                var child = new GameObject("STAIRFILL");
                var ct = child.transform;
                ct.SetParent(go.transform, false);
                ct.localPosition = Vector3.zero; ct.localRotation = Quaternion.identity;
                ct.localScale = Vector3.one;
                child.layer = go.layer;
                float sy = Mathf.Abs(scaleY);
                int boxes = 0, mids = 0;
                for (int fi = 0; fi < flights.Count; fi++)
                {
                    var f = flights[fi];
                    for (int k = 0; k < f.Steps.Count; k++)
                    {
                        var s = f.Steps[k];
                        var next = k + 1 < f.Steps.Count ? f.Steps[k + 1] : null;
                        float lo, hi;
                        if (next == null) { lo = s.Lo; hi = s.Hi; }
                        else if (f.Dir > 0) { lo = s.Lo; hi = next.Lo; }
                        else { lo = next.Hi; hi = s.Hi; }
                        if (hi - lo < 0.02f) { lo = s.Lo; hi = s.Hi; }
                        AddBox(child, f.Axis, lo, hi, s.Y - Thick * 0.5f, Thick, s.W0, s.W1);
                        boxes++;
                        if (next != null && (next.Y - s.Y) * sy > MidRiseM)
                        {
                            float mid = 0.5f * (lo + hi);
                            float mlo = f.Dir > 0 ? mid : lo, mhi = f.Dir > 0 ? hi : mid;
                            float top = s.Y + 0.5f * (next.Y - s.Y);
                            AddBox(child, f.Axis, mlo, mhi, top - Thick * 0.5f, Thick, s.W0, s.W1);
                            mids++;
                        }
                    }
                }
                _instances++; _boxes += boxes; _midBoxes += mids;
                _meshesFilled.Add(meshId);
            }
            catch (Exception e)
            {
                _log?.Warning("[stairfill] attach " + meshId + ": " + e.Message);
            }
        }

        static void AddBox(GameObject host, int axis, float lo, float hi, float cy, float thick,
                           float w0, float w1)
        {
            var bc = host.AddComponent<BoxCollider>();
            float cr = 0.5f * (lo + hi), lr = hi - lo;
            float cw = 0.5f * (w0 + w1), lw = w1 - w0;
            if (axis == 0)
            {
                bc.center = new Vector3(cr, cy, cw);
                bc.size = new Vector3(lr, thick, lw);
            }
            else
            {
                bc.center = new Vector3(cw, cy, cr);
                bc.size = new Vector3(lw, thick, lr);
            }
        }

        internal static void LogSummary(MelonLogger.Instance log)
        {
            if (_instances == 0) return;
            log?.Msg("[stairfill] closed the treads on " + _instances + " stair instance(s) across "
                   + _meshesFilled.Count + " mesh(es): " + _boxes + " tread box(es), " + _midBoxes
                   + " half-step box(es) where a rise exceeds " + MidRiseM.ToString("F2")
                   + " m. Invisible; the meshes are untouched. If a flight still stalls, "
                   + "odg dump section 8 walks it and names the rise.");
            _instances = 0; _boxes = 0; _midBoxes = 0; _meshesFilled.Clear();
        }
    }
}
