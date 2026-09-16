// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace MapLoader
{

    internal static class PlateFill
    {
        static MelonLogger.Instance _log;

        const float CellM      = 3.0f;
        const float ThickM     = 0.50f;
        const float GapM       = 0.02f;
        const float MinCover   = 0.40f;
        const float UpDot      = 0.85f;
        const float FlatDot    = 0.85f;
        const float BelowLo    = 0.15f;
        const float BelowHi    = 0.45f;
        const int   MaxSlabs   = 60;
        const float MinExtentM = 0.02f;

        static readonly string[] Deny = { "background", "vista", "ocean", "water", "foam", "decal", "sky", "cloud" };

        static bool Denied(string meshId)
        {
            var n = meshId.ToLowerInvariant();
            for (int i = 0; i < Deny.Length; i++) if (n.Contains(Deny[i])) return true;
            return false;
        }

        sealed class Slab { public float Y, X0, X1, Z0, Z1; }

        static readonly Dictionary<string, List<Slab>> _byMesh =
            new Dictionary<string, List<Slab>>(StringComparer.Ordinal);
        static readonly HashSet<string> _meshesFilled = new HashSet<string>(StringComparer.Ordinal);
        static int _instances, _boxes, _meshesWithPlates;

        internal static void Init(MelonLogger.Instance log) { _log = log; }

        internal static void Analyse(string meshId, MapBundle.Geometry g)
        {
            if (string.IsNullOrEmpty(meshId) || g == null || _byMesh.ContainsKey(meshId)) return;
            var slabs = new List<Slab>();
            try
            {
                var P = g.Positions; var N = g.Normals; var I = g.Indices;
                if (P == null || I == null || I.Length < 3 || Denied(meshId)) { _byMesh[meshId] = slabs; return; }
                float ymin = float.PositiveInfinity, ymax = float.NegativeInfinity;
                for (int v = 1; v + 1 < P.Length; v += 3) { if (P[v] < ymin) ymin = P[v]; if (P[v] > ymax) ymax = P[v]; }
                if (ymax - ymin < MinExtentM) { _byMesh[meshId] = slabs; return; }
                bool haveN = N != null && N.Length == P.Length;

                var cover = new Dictionary<long, float>();
                var yOf   = new Dictionary<long, float>();

                var allY  = new List<float>(I.Length / 3);
                var allX  = new List<float>(I.Length / 3);
                var allZ  = new List<float>(I.Length / 3);

                for (int t = 0; t + 2 < I.Length; t += 3)
                {
                    int a = I[t] * 3, b = I[t + 1] * 3, c = I[t + 2] * 3;
                    if (a < 0 || b < 0 || c < 0 || c + 2 >= P.Length || a + 2 >= P.Length || b + 2 >= P.Length) continue;
                    float ax = P[a], ay = P[a + 1], az = P[a + 2];
                    float bx = P[b], by = P[b + 1], bz = P[b + 2];
                    float cx = P[c], cy = P[c + 1], cz = P[c + 2];
                    float ux = bx - ax, uy = by - ay, uz = bz - az;
                    float vx = cx - ax, vy = cy - ay, vz = cz - az;
                    float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                    float len = Mathf.Sqrt(nx * nx + ny * ny + nz * nz);
                    if (len < 1e-6f) continue;
                    float area = 0.5f * len;
                    float mx = (ax + bx + cx) / 3f, my = (ay + by + cy) / 3f, mz = (az + bz + cz) / 3f;
                    allX.Add(mx); allY.Add(my); allZ.Add(mz);

                    if (Mathf.Abs(ny / len) < FlatDot) continue;
                    float upy = haveN ? (N[a + 1] + N[b + 1] + N[c + 1]) / 3f : (ny / len);
                    if (upy < UpDot) continue;
                    if (area < 0.01f) continue;

                    int yb = Mathf.RoundToInt(my / 0.05f);
                    int gx = Mathf.FloorToInt(mx / CellM), gz = Mathf.FloorToInt(mz / CellM);
                    long key = ((long)yb << 42) ^ ((long)(gx & 0x1FFFFF) << 21) ^ (long)(gz & 0x1FFFFF);
                    float acc; cover.TryGetValue(key, out acc); cover[key] = acc + area;
                    if (!yOf.ContainsKey(key)) yOf[key] = my;
                }

                float cellArea = CellM * CellM;
                var cand = new List<KeyValuePair<long, float>>(cover);
                cand.Sort((p, q) => q.Value.CompareTo(p.Value));
                for (int i = 0; i < cand.Count && slabs.Count < MaxSlabs; i++)
                {
                    if (cand[i].Value < MinCover * cellArea) break;
                    long key = cand[i].Key;
                    int yb = (int)(key >> 42);
                    int gx = (int)((key >> 21) & 0x1FFFFF); if (gx >= 0x100000) gx -= 0x200000;
                    int gz = (int)(key & 0x1FFFFF);         if (gz >= 0x100000) gz -= 0x200000;
                    float ys = yOf[key];
                    float x0 = gx * CellM, x1 = x0 + CellM, z0 = gz * CellM, z1 = z0 + CellM;

                    bool solid = false;
                    for (int k = 0; k < allY.Count; k++)
                    {
                        float dy = ys - allY[k];
                        if (dy < BelowLo || dy > BelowHi) continue;
                        if (allX[k] < x0 || allX[k] > x1 || allZ[k] < z0 || allZ[k] > z1) continue;
                        solid = true; break;
                    }
                    if (solid) continue;
                    slabs.Add(new Slab { Y = ys, X0 = x0, X1 = x1, Z0 = z0, Z1 = z1 });
                }
            }
            catch (Exception e) { _log?.Warning("[platefill] " + meshId + ": " + e.Message); }
            slabs = Merge(slabs);
            _byMesh[meshId] = slabs;
            if (slabs.Count > 0) _meshesWithPlates++;
        }

        static List<Slab> Merge(List<Slab> cells)
        {
            if (cells.Count < 2) return cells;
            var byY = new Dictionary<int, List<Slab>>();
            for (int i = 0; i < cells.Count; i++)
            {
                int yb = Mathf.RoundToInt(cells[i].Y / 0.05f);
                List<Slab> l; if (!byY.TryGetValue(yb, out l)) byY[yb] = l = new List<Slab>();
                l.Add(cells[i]);
            }
            var outp = new List<Slab>();
            foreach (var kv in byY)
            {
                var l = kv.Value;
                float x0 = float.PositiveInfinity, x1 = float.NegativeInfinity, z0 = x0, z1 = x1;
                for (int i = 0; i < l.Count; i++)
                {
                    if (l[i].X0 < x0) x0 = l[i].X0; if (l[i].X1 > x1) x1 = l[i].X1;
                    if (l[i].Z0 < z0) z0 = l[i].Z0; if (l[i].Z1 > z1) z1 = l[i].Z1;
                }
                int nx = Mathf.RoundToInt((x1 - x0) / CellM), nz = Mathf.RoundToInt((z1 - z0) / CellM);
                if (nx * nz == l.Count && l.Count > 1)
                    outp.Add(new Slab { Y = l[0].Y, X0 = x0, X1 = x1, Z0 = z0, Z1 = z1 });
                else outp.AddRange(l);
            }
            return outp;
        }

        internal static void Attach(GameObject go, string meshId, float scaleY)
        {
            if (go == null || string.IsNullOrEmpty(meshId)) return;
            List<Slab> slabs;
            if (!_byMesh.TryGetValue(meshId, out slabs) || slabs.Count == 0) return;
            try
            {
                float sy = Mathf.Max(0.01f, Mathf.Abs(scaleY));
                float thick = ThickM / sy, gap = GapM / sy;
                var child = new GameObject("PLATEFILL");
                var ct = child.transform;
                ct.SetParent(go.transform, false);
                ct.localPosition = Vector3.zero; ct.localRotation = Quaternion.identity;
                ct.localScale = Vector3.one;
                child.layer = go.layer;
                for (int i = 0; i < slabs.Count; i++)
                {
                    var s = slabs[i];
                    var bc = child.AddComponent<BoxCollider>();
                    bc.center = new Vector3(0.5f * (s.X0 + s.X1), s.Y - gap - 0.5f * thick, 0.5f * (s.Z0 + s.Z1));
                    bc.size   = new Vector3(s.X1 - s.X0, thick, s.Z1 - s.Z0);
                }
                _instances++; _boxes += slabs.Count;
                _meshesFilled.Add(meshId);
            }
            catch (Exception e) { _log?.Warning("[platefill] attach " + meshId + ": " + e.Message); }
        }

        internal static bool IsFill(GameObject go)
        {
            try { return go != null && go.name == "PLATEFILL"; } catch { return false; }
        }

        internal static void LogSummary(MelonLogger.Instance log)
        {
            if (_instances == 0 && _meshesWithPlates == 0)
            {
                log?.Msg("[platefill] no thin walkable plate found on any mesh with a collider. If bodies "
                       + "still sink into floors here, the floor they sink into is not one of ours.");
            }
            else
            {
                log?.Msg("[platefill] hung " + _boxes + " slab(s) " + ThickM.ToString("F2") + " m deep under "
                       + _instances + " instance(s) of " + _meshesFilled.Count + " mesh(es) whose walkable "
                       + "tops had nothing solid beneath them. Invisible; the meshes are untouched. A physical "
                       + "skeleton that ends up inside a plate is now pushed UP out of it, as it is on a "
                       + "shipped level's thick floor, instead of dropping a deck and wedging there.");
            }
            _instances = 0; _boxes = 0; _meshesWithPlates = 0; _meshesFilled.Clear(); _byMesh.Clear();
        }
    }
}
