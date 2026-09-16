// ODG Operations - FireteamRoles
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace FireteamRoles
{

    internal static class MeshCut
    {
        internal class Result
        {
            internal bool Ok;
            internal bool Erased;
            internal string Why = "";
            internal int TrianglesRemoved;
            internal int TrianglesAdded;
            internal int CollarQuads;
            internal string Channels = "";
            internal bool Recovered;
        }

        sealed class Owned { internal Mesh Work, Original; internal MeshFilter Mf; internal MeshCollider Mc; }
        static readonly Dictionary<int, Owned> _owned = new Dictionary<int, Owned>();

        internal static void Forget()
        {
            MeshRead.Forget();
            int n = 0;
            foreach (var kv in _owned)
            {
                var o = kv.Value; if (o == null) continue;
                try { if (o.Mf != null && o.Original != null) o.Mf.sharedMesh = o.Original; } catch { }
                try { if (o.Mc != null && o.Original != null) { o.Mc.sharedMesh = null; o.Mc.sharedMesh = o.Original; } } catch { }
                n++;
            }
            _owned.Clear();
            if (n > 0) Log.Detail("put back " + n + " breached mesh(es)");
        }

        internal static bool IsCut(MeshFilter mf)
        {
            try { return mf != null && _owned.ContainsKey(mf.GetInstanceID()); } catch { return false; }
        }

        class Verts
        {
            internal readonly List<Vector3> P = new List<Vector3>();
            internal List<Vector3> N;
            internal List<Vector4> T;
            internal List<Vector2> U0, U1, U2, U3;
            internal List<Color> C;

            internal int Count { get { return P.Count; } }

            internal void Init(Vector3[] p, Vector3[] n, Vector4[] t,
                               Vector2[] u0, Vector2[] u1, Vector2[] u2, Vector2[] u3, Color[] c)
            {
                int len = p.Length;
                n = Fit(n, len); t = Fit(t, len);
                u0 = Fit(u0, len); u1 = Fit(u1, len); u2 = Fit(u2, len); u3 = Fit(u3, len);
                c = Fit(c, len);

                P.Capacity = len + 256;
                P.AddRange(p);
                if (n != null) { N = new List<Vector3>(len + 256); N.AddRange(n); }
                if (t != null) { T = new List<Vector4>(len + 256); T.AddRange(t); }
                if (u0 != null) { U0 = new List<Vector2>(len + 256); U0.AddRange(u0); }
                if (u1 != null) { U1 = new List<Vector2>(len + 256); U1.AddRange(u1); }
                if (u2 != null) { U2 = new List<Vector2>(len + 256); U2.AddRange(u2); }
                if (u3 != null) { U3 = new List<Vector2>(len + 256); U3.AddRange(u3); }
                if (c != null) { C = new List<Color>(len + 256); C.AddRange(c); }
            }

            static T2[] Fit<T2>(T2[] a, int len) { return (a != null && a.Length == len) ? a : null; }

            internal string Summary()
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("pos");
                if (N != null) sb.Append(" nrm");
                if (T != null) sb.Append(" tan");
                if (U0 != null) sb.Append(" uv0");
                if (U1 != null) sb.Append(" uv2(lightmap)");
                if (U2 != null) sb.Append(" uv3");
                if (U3 != null) sb.Append(" uv4");
                if (C != null) sb.Append(" col");
                return sb.ToString();
            }

            internal int AddLerp(Vector3 pos, int ia, int ib, int ic, float wa, float wb, float wc)
            {
                P.Add(pos);
                if (N != null) N.Add((N[ia] * wa + N[ib] * wb + N[ic] * wc).normalized);
                if (T != null) T.Add(T[ia] * wa + T[ib] * wb + T[ic] * wc);
                if (U0 != null) U0.Add(U0[ia] * wa + U0[ib] * wb + U0[ic] * wc);
                if (U1 != null) U1.Add(U1[ia] * wa + U1[ib] * wb + U1[ic] * wc);
                if (U2 != null) U2.Add(U2[ia] * wa + U2[ib] * wb + U2[ic] * wc);
                if (U3 != null) U3.Add(U3[ia] * wa + U3[ib] * wb + U3[ic] * wc);
                if (C != null) C.Add(C[ia] * wa + C[ib] * wb + C[ic] * wc);
                return P.Count - 1;
            }

            internal int AddFrom(Vector3 pos, int src, Vector3 overrideNormal, bool useNormal)
            {
                P.Add(pos);
                if (N != null) N.Add(useNormal ? overrideNormal : N[src]);
                if (T != null) T.Add(T[src]);
                if (U0 != null) U0.Add(U0[src]);
                if (U1 != null) U1.Add(U1[src]);
                if (U2 != null) U2.Add(U2[src]);
                if (U3 != null) U3.Add(U3[src]);
                if (C != null) C.Add(C[src]);
                return P.Count - 1;
            }

            internal void Write(Mesh m)
            {
                m.SetVertices(FromManaged(P));
                if (N != null) m.SetNormals(FromManaged(N));
                if (T != null) m.SetTangents(FromManaged(T));
                if (U0 != null) m.SetUVs(0, FromManaged(U0));
                if (U1 != null) m.SetUVs(1, FromManaged(U1));
                if (U2 != null) m.SetUVs(2, FromManaged(U2));
                if (U3 != null) m.SetUVs(3, FromManaged(U3));
                if (C != null) m.SetColors(FromManaged(C));
            }
        }

        internal static Result Cut(MeshFilter mf, Hole h, float collar)
        {
            var r = new Result();
            if (mf == null) { r.Why = "no MeshFilter"; return r; }

            Mesh src = null;
            try { src = mf.sharedMesh; } catch { }
            if (src == null) { r.Why = "no mesh"; return r; }

            try
            {
                var rend = mf.GetComponent<MeshRenderer>();
                if (rend != null && rend.isPartOfStaticBatch) { r.Why = "static batch"; return r; }
            }
            catch { }

            int id = mf.GetInstanceID();
            Owned o;
            if (!_owned.TryGetValue(id, out o) || o == null || o.Work == null)
            {
                var work = Clone(src, r);
                if (work == null) return r;
                o = new Owned { Work = work, Original = src, Mf = mf };
                try
                {
                    var mc = mf.GetComponent<MeshCollider>();
                    if (mc != null)
                    {
                        var cm = mc.sharedMesh;
                        if (cm != null && cm.GetInstanceID() == src.GetInstanceID()) o.Mc = mc;
                    }
                }
                catch { }
                _owned[id] = o;
            }

            int outcome = CutCore(o.Work, mf.transform, h, collar, true, r);
            if (outcome == 0)
            {

                if (r.Why.StartsWith("rebuild")) { _owned.Remove(id); try { mf.sharedMesh = o.Original; } catch { } }
                return r;
            }
            if (outcome == 2) { r.Ok = true; r.Erased = true; return r; }

            try { mf.sharedMesh = o.Work; } catch (Exception e) { r.Why = "assign failed: " + e.Message; return r; }
            RebuildCollider(mf, o.Work);
            r.Ok = true;
            return r;
        }

        internal static Result CutCollider(MeshCollider mc, Hole h)
        {
            var r = new Result();
            if (mc == null) { r.Why = "no collider"; return r; }
            Mesh src = null;
            try { src = mc.sharedMesh; } catch { }
            if (src == null) { r.Why = "no collision mesh"; return r; }

            int id = mc.GetInstanceID();
            Owned o;
            if (!_owned.TryGetValue(id, out o) || o == null || o.Work == null)
            {
                var work = Clone(src, r);
                if (work == null) return r;
                o = new Owned { Work = work, Original = src, Mc = mc };
                _owned[id] = o;
            }

            int outcome = CutCore(o.Work, mc.transform, h, 0f, false, r);
            if (outcome == 0) { if (r.Why.StartsWith("rebuild")) _owned.Remove(id); return r; }
            if (outcome == 2) { r.Ok = true; r.Erased = true; return r; }
            try { mc.sharedMesh = null; mc.sharedMesh = o.Work; }
            catch (Exception e) { r.Why = "collider assign failed: " + e.Message; return r; }
            r.Ok = true;
            return r;
        }

        internal static Mesh FlippedCopy(Mesh src, out string why)
        {
            why = "";
            var r = new Result();
            var work = Clone(src, r);
            if (work == null) { why = r.Why; return null; }
            try
            {
                int subs = Mathf.Max(1, work.subMeshCount);
                int total = 0;
                for (int sm = 0; sm < subs; sm++)
                {
                    var tris = ToManaged(work.GetTriangles(sm));
                    if (tris == null) continue;
                    total += tris.Length / 3;
                    for (int i = 0; i + 2 < tris.Length; i += 3) { int t = tris[i + 1]; tris[i + 1] = tris[i + 2]; tris[i + 2] = t; }
                    work.SetTriangles(FromManaged(tris), sm);
                }
                var n = ToManaged(work.normals);
                if (n != null && n.Length > 0)
                {
                    for (int i = 0; i < n.Length; i++) n[i] = -n[i];
                    work.SetNormals(FromManaged(n));
                }
                work.name = src.name + "_inside";
                if (total < 1) { why = "no triangles"; UnityEngine.Object.Destroy(work); return null; }
                return work;
            }
            catch (Exception e)
            {
                why = e.Message;
                try { UnityEngine.Object.Destroy(work); } catch { }
                return null;
            }
        }

        static Mesh Clone(Mesh src, Result r)
        {
            bool readable = false;
            try { readable = src.isReadable; } catch { }
            Mesh source = src;
            if (!readable)
            {
                string rwhy;
                source = MeshRead.Recover(src, out rwhy);
                if (source == null) { r.Why = "not readable, and " + rwhy; return null; }
                r.Recovered = true;
            }
            try
            {
                var work = UnityEngine.Object.Instantiate(source);
                work.name = src.name + "_breached";
                return work;
            }
            catch (Exception e) { r.Why = "clone failed: " + e.Message; return null; }
        }

        static int CutCore(Mesh work, Transform t, Hole h, float collar, bool channels, Result r)
        {
            Vector3[] verts;
            int subCount;
            int[][] subTris;
            var vx = new Verts();
            try
            {
                verts = ToManaged(work.vertices);
                if (verts == null || verts.Length == 0) { r.Why = "empty mesh"; return 0; }

                if (channels)
                    vx.Init(verts, ToManaged(work.normals), ToManaged(work.tangents), ToManaged(work.uv),
                            ToManaged(work.uv2), ToManaged(work.uv3), ToManaged(work.uv4), ToManaged(work.colors));
                else
                    vx.Init(verts, null, null, null, null, null, null, null);

                subCount = Mathf.Max(1, work.subMeshCount);
                subTris = new int[subCount][];
                for (int sm = 0; sm < subCount; sm++) subTris[sm] = ToManaged(work.GetTriangles(sm));
            }
            catch (Exception e) { r.Why = "read failed: " + e.Message; return 0; }

            r.Channels = vx.Summary();

            int totalTris = 0;
            for (int sm = 0; sm < subCount; sm++)
                if (subTris[sm] != null) totalTris += subTris[sm].Length / 3;
            if (totalTris < 1) { r.Why = "empty mesh"; return 0; }

            int budget = Mathf.Max(3000, Cfg.BreachTriangleBudget.Value);
            if (totalTris > budget)
            { r.Why = "mesh too large (" + totalTris + " tris, budget " + budget + ")"; return 0; }

            Matrix4x4 l2w = t.localToWorldMatrix, w2l = t.worldToLocalMatrix;
            Bounds wb = h.WorldBounds(0.2f);
            Vector3 lmin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 lmax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = new Vector3((i & 1) == 0 ? wb.min.x : wb.max.x, (i & 2) == 0 ? wb.min.y : wb.max.y, (i & 4) == 0 ? wb.min.z : wb.max.z);
                Vector3 lp = w2l.MultiplyPoint3x4(c);
                lmin = Vector3.Min(lmin, lp); lmax = Vector3.Max(lmax, lp);
            }

            var outSubs = new List<int>[subCount];
            for (int sm = 0; sm < subCount; sm++) outSubs[sm] = new List<int>();
            int removed = 0, added = 0;

            float target = Mathf.Max(0.06f, Mathf.Min(h.HalfW, h.HalfH) / 3f);
            int maxDepth = Mathf.Clamp(Cfg.BreachSubdivision.Value, 0, 5);
            var work3 = _scratch;

            for (int sm = 0; sm < subCount; sm++)
            {
                var tris = subTris[sm];
                if (tris == null) continue;
                var outTris = outSubs[sm];

                for (int i = 0; i + 2 < tris.Length; i += 3)
                {
                    int a = tris[i], b = tris[i + 1], c2 = tris[i + 2];
                    if (a >= verts.Length || b >= verts.Length || c2 >= verts.Length) continue;

                    Vector3 va = vx.P[a], vb = vx.P[b], vc = vx.P[c2];

                    if (!TouchesBox(va, vb, vc, lmin, lmax))
                    {
                        outTris.Add(a); outTris.Add(b); outTris.Add(c2);
                        continue;
                    }

                    bool ia = h.Inside(l2w.MultiplyPoint3x4(va));
                    bool ib = h.Inside(l2w.MultiplyPoint3x4(vb));
                    bool ic = h.Inside(l2w.MultiplyPoint3x4(vc));
                    if (ia && ib && ic) { removed++; continue; }

                    if (!ia && !ib && !ic && !EdgeCrosses(va, vb, vc, l2w, h))
                    {
                        outTris.Add(a); outTris.Add(b); outTris.Add(c2);
                        continue;
                    }

                    int n = Subdivide(va, vb, vc, target, maxDepth, work3);
                    for (int k = 0; k < n; k++)
                    {
                        var f = work3[k];
                        Vector3 centroid = (f.A + f.B + f.C) * (1f / 3f);
                        if (h.Inside(l2w.MultiplyPoint3x4(centroid))) { removed++; continue; }

                        int i0 = Emit(vx, f.A, va, vb, vc, a, b, c2);
                        int i1 = Emit(vx, f.B, va, vb, vc, a, b, c2);
                        int i2 = Emit(vx, f.C, va, vb, vc, a, b, c2);
                        outTris.Add(i0); outTris.Add(i1); outTris.Add(i2);
                        added++;
                    }
                }
            }

            if (removed == 0) { r.Why = "the opening did not reach it"; return 0; }

            int kept = 0;
            for (int sm = 0; sm < subCount; sm++) kept += outSubs[sm].Count;
            if (kept < 3) return 2;

            if (collar > 0f) r.CollarQuads = BuildCollar(t, l2w, vx, outSubs, h, collar);

            try
            {
                work.Clear();
                if (vx.Count > 65000) work.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                vx.Write(work);
                work.subMeshCount = subCount;
                for (int sm = 0; sm < subCount; sm++)
                    work.SetTriangles(FromManaged(outSubs[sm]), sm);
                work.RecalculateBounds();
                if (channels && vx.N == null) work.RecalculateNormals();
                if (channels && vx.T == null && vx.U0 != null) work.RecalculateTangents();
            }
            catch (Exception e) { r.Why = "rebuild failed: " + e.Message; return 0; }

            r.TrianglesRemoved = removed;
            r.TrianglesAdded = added;
            return 1;
        }

        static void RebuildCollider(MeshFilter mf, Mesh m)
        {
            try
            {
                var mc = mf.GetComponent<MeshCollider>();
                if (mc != null)
                {
                    mc.sharedMesh = null;
                    mc.sharedMesh = m;
                    return;
                }

                var box = mf.GetComponent<BoxCollider>();
                if (box != null && box.enabled)
                {
                    box.enabled = false;
                    var added = mf.gameObject.AddComponent<MeshCollider>();
                    added.sharedMesh = m;
                    added.convex = false;
                    Breach.NoteReplacedBox(box, added);
                }
            }
            catch (Exception e) { Log.Detail("collider rebuild failed: " + e.Message); }
        }

        struct Tri { internal Vector3 A, B, C; }

        struct EdgeInfo { internal int Count; internal Vector3 N; internal Vector3 A, B; internal int IA, IB; }

        static readonly Dictionary<long, EdgeInfo> _edges = new Dictionary<long, EdgeInfo>();

        static int BuildCollar(Transform t, Matrix4x4 l2w, Verts vx, List<int>[] subs, Hole h, float thickness)
        {
            thickness = Mathf.Clamp(thickness, 0.01f, 2f);
            _edges.Clear();

            List<int> target = null;
            for (int sm = 0; sm < subs.Length; sm++)
            {
                var tris = subs[sm];
                if (tris == null || tris.Count < 3) continue;
                if (target == null) target = tris;

                for (int i = 0; i + 2 < tris.Count; i += 3)
                {
                    int i0 = tris[i], i1 = tris[i + 1], i2 = tris[i + 2];
                    if (i0 >= vx.Count || i1 >= vx.Count || i2 >= vx.Count) continue;

                    Vector3 p0 = vx.P[i0], p1 = vx.P[i1], p2 = vx.P[i2];

                    Vector3 mid = (p0 + p1 + p2) * (1f / 3f);
                    float d = h.RimDist(l2w.MultiplyPoint3x4(mid));
                    if (d < 0.5f || d > 2.0f) continue;

                    Vector3 fn = Vector3.Cross(p1 - p0, p2 - p0);
                    if (fn.sqrMagnitude < 1e-12f) continue;
                    fn = fn.normalized;

                    AddEdge(i0, i1, p0, p1, fn);
                    AddEdge(i1, i2, p1, p2, fn);
                    AddEdge(i2, i0, p2, p0, fn);
                }
            }
            if (target == null) { _edges.Clear(); return 0; }

            int made = 0;
            foreach (var kv in _edges)
            {
                var e = kv.Value;
                if (e.Count != 1) continue;

                Vector3 mid = (e.A + e.B) * 0.5f;
                Vector3 wmid = l2w.MultiplyPoint3x4(mid);
                float d = h.RimDist(wmid);
                if (d < 0.6f || d > 1.6f) continue;

                float w = Vector3.Dot(wmid - h.C, h.Fwd);
                if (w < h.DMin - 0.3f || w > h.DMax + 0.3f) continue;

                Vector3 n = e.N.sqrMagnitude < 1e-9f ? Vector3.up : e.N.normalized;
                Vector3 wn = t.TransformDirection(n).normalized;
                Vector3 a2 = t.InverseTransformPoint(t.TransformPoint(e.A) - wn * thickness);
                Vector3 b2 = t.InverseTransformPoint(t.TransformPoint(e.B) - wn * thickness);

                Quad(vx, target, e.A, e.B, b2, a2, e.IA, e.IB);
                made++;
                if (made > 2500) break;
            }

            _edges.Clear();
            return made;
        }

        static void AddEdge(int i0, int i1, Vector3 p0, Vector3 p1, Vector3 fn)
        {
            long k = EdgeKey(p0, p1);
            EdgeInfo e;
            if (_edges.TryGetValue(k, out e)) { e.Count++; e.N += fn; _edges[k] = e; return; }
            e = new EdgeInfo { Count = 1, N = fn, A = p0, B = p1, IA = i0, IB = i1 };
            _edges[k] = e;
        }

        static long EdgeKey(Vector3 a, Vector3 b)
        {
            long ha = PosKey(a), hb = PosKey(b);
            long lo = ha < hb ? ha : hb, hi = ha < hb ? hb : ha;
            return lo * 1000003L ^ hi;
        }

        static long PosKey(Vector3 p)
        {
            long x = (long)Mathf.Round(p.x * 10000f);
            long y = (long)Mathf.Round(p.y * 10000f);
            long z = (long)Mathf.Round(p.z * 10000f);
            return (x * 73856093L) ^ (y * 19349663L) ^ (z * 83492791L);
        }

        static void Quad(Verts vx, List<int> tris, Vector3 a, Vector3 b, Vector3 b2, Vector3 a2, int ia, int ib)
        {
            Vector3 fn = Vector3.Cross(b - a, a2 - a);
            if (fn.sqrMagnitude < 1e-12f) return;
            fn = fn.normalized;

            int baseI = vx.Count;
            vx.AddFrom(a, ia, fn, true); vx.AddFrom(b, ib, fn, true);
            vx.AddFrom(b2, ib, fn, true); vx.AddFrom(a2, ia, fn, true);
            tris.Add(baseI); tris.Add(baseI + 1); tris.Add(baseI + 2);
            tris.Add(baseI); tris.Add(baseI + 2); tris.Add(baseI + 3);

            int b2i = vx.Count;
            vx.AddFrom(a, ia, -fn, true); vx.AddFrom(b, ib, -fn, true);
            vx.AddFrom(b2, ib, -fn, true); vx.AddFrom(a2, ia, -fn, true);
            tris.Add(b2i); tris.Add(b2i + 2); tris.Add(b2i + 1);
            tris.Add(b2i); tris.Add(b2i + 3); tris.Add(b2i + 2);
        }

        internal static int SeedFrom(Vector3 worldCentre)
        {
            unchecked
            {
                int x = Mathf.RoundToInt(worldCentre.x * 100f);
                int y = Mathf.RoundToInt(worldCentre.y * 100f);
                int z = Mathf.RoundToInt(worldCentre.z * 100f);
                int h = 17;
                h = h * 31 + x; h = h * 31 + y; h = h * 31 + z;
                return h;
            }
        }

        static float Hash(int a, int b, int cc, int seed)
        {
            unchecked
            {
                uint h = (uint)seed;
                h ^= (uint)(a * 374761393); h = (h << 13) | (h >> 19); h *= 1274126177;
                h ^= (uint)(b * 668265263); h = (h << 17) | (h >> 15); h *= 2246822519;
                h ^= (uint)(cc * 2654435761); h = (h << 11) | (h >> 21); h *= 3266489917;
                h ^= h >> 15;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        internal static float RimScale(float u, float v, int seed, float amp)
        {
            if (amp <= 0f) return 1f;
            const int Cells = 11;
            float ang = Mathf.Atan2(v, u);
            float f = (ang + Mathf.PI) / (2f * Mathf.PI) * Cells;
            int i0 = Mathf.FloorToInt(f);
            float tt = f - i0;
            tt = tt * tt * (3f - 2f * tt);
            int c0 = ((i0 % Cells) + Cells) % Cells, c1 = (c0 + 1) % Cells;
            float a = Hash(c0, 7, 3, seed), b = Hash(c1, 7, 3, seed);
            return 1f + (Mathf.Lerp(a, b, tt) * 2f - 1f) * amp;
        }

        static readonly Tri[] _scratch = new Tri[1 + 4 * 4 * 4 * 4 * 4];

        static int Emit(Verts vx, Vector3 p, Vector3 va, Vector3 vb, Vector3 vc, int ia, int ib, int ic)
        {
            float u, v, w;
            Bary(p, va, vb, vc, out u, out v, out w);
            return vx.AddLerp(p, ia, ib, ic, u, v, w);
        }

        static void Bary(Vector3 p, Vector3 a, Vector3 b, Vector3 c, out float u, out float v, out float w)
        {
            Vector3 v0 = b - a, v1 = c - a, v2 = p - a;
            float d00 = Vector3.Dot(v0, v0), d01 = Vector3.Dot(v0, v1), d11 = Vector3.Dot(v1, v1);
            float d20 = Vector3.Dot(v2, v0), d21 = Vector3.Dot(v2, v1);
            float den = d00 * d11 - d01 * d01;
            if (Mathf.Abs(den) < 1e-12f) { u = 1f; v = 0f; w = 0f; return; }
            v = (d11 * d20 - d01 * d21) / den;
            w = (d00 * d21 - d01 * d20) / den;
            u = 1f - v - w;
        }

        static int Subdivide(Vector3 a, Vector3 b, Vector3 c, float target, int maxDepth, Tri[] outp)
        {
            int n = 0;
            Recurse(a, b, c, target, maxDepth, outp, ref n);
            return n;
        }

        static void Recurse(Vector3 a, Vector3 b, Vector3 c, float target, int depth, Tri[] outp, ref int n)
        {
            if (n >= outp.Length - 4) return;
            float longest = Mathf.Max((a - b).magnitude, Mathf.Max((b - c).magnitude, (c - a).magnitude));
            if (depth <= 0 || longest <= target)
            {
                outp[n++] = new Tri { A = a, B = b, C = c };
                return;
            }
            Vector3 ab = (a + b) * 0.5f, bc = (b + c) * 0.5f, ca = (c + a) * 0.5f;
            Recurse(a, ab, ca, target, depth - 1, outp, ref n);
            Recurse(ab, b, bc, target, depth - 1, outp, ref n);
            Recurse(ca, bc, c, target, depth - 1, outp, ref n);
            Recurse(ab, bc, ca, target, depth - 1, outp, ref n);
        }

        static bool TouchesBox(Vector3 a, Vector3 b, Vector3 c, Vector3 lo, Vector3 hi)
        {
            float minX = Mathf.Min(a.x, Mathf.Min(b.x, c.x)), maxX = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
            if (maxX < lo.x || minX > hi.x) return false;
            float minY = Mathf.Min(a.y, Mathf.Min(b.y, c.y)), maxY = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
            if (maxY < lo.y || minY > hi.y) return false;
            float minZ = Mathf.Min(a.z, Mathf.Min(b.z, c.z)), maxZ = Mathf.Max(a.z, Mathf.Max(b.z, c.z));
            if (maxZ < lo.z || minZ > hi.z) return false;
            return true;
        }

        static bool EdgeCrosses(Vector3 a, Vector3 b, Vector3 c, Matrix4x4 l2w, Hole h)
        {
            if (SegHits(a, b, l2w, h) || SegHits(b, c, l2w, h) || SegHits(c, a, l2w, h)) return true;
            if (h.Inside(l2w.MultiplyPoint3x4((a + b + c) * (1f / 3f)))) return true;

            for (int i = 1; i < 4; i++)
                for (int j = 1; i + j < 4; j++)
                {
                    float u = i / 4f, v = j / 4f;
                    if (h.Inside(l2w.MultiplyPoint3x4(a + (b - a) * u + (c - a) * v))) return true;
                }
            return false;
        }

        static bool SegHits(Vector3 p, Vector3 q, Matrix4x4 l2w, Hole h)
        {
            for (int i = 1; i < 10; i++)
                if (h.Inside(l2w.MultiplyPoint3x4(Vector3.Lerp(p, q, i / 10f)))) return true;
            return false;
        }

        static bool _bulkWarned;

        static IntPtr Data(Il2CppObjectBase arr) { return IntPtr.Add(arr.Pointer, 4 * IntPtr.Size); }

        internal static unsafe T[] ToManaged<T>(Il2CppStructArray<T> src) where T : unmanaged
        {
            if (src == null) return null;
            int n = src.Length;
            var outp = new T[n];
            if (n == 0) return outp;
            bool ok = false;
            try
            {
                long bytes = (long)n * sizeof(T);
                fixed (T* dst = outp) Buffer.MemoryCopy((void*)Data(src), dst, bytes, bytes);
                ok = outp[0].Equals(src[0]) && outp[n - 1].Equals(src[n - 1]);
            }
            catch { ok = false; }
            if (!ok)
            {
                if (!_bulkWarned) { _bulkWarned = true; Log.Warn("mesh arrays: the bulk copy disagreed with the indexer, using the slow path"); }
                for (int i = 0; i < n; i++) outp[i] = src[i];
            }
            return outp;
        }

        internal static unsafe Il2CppStructArray<T> FromManaged<T>(List<T> src) where T : unmanaged
        {
            int n = src.Count;
            var outp = new Il2CppStructArray<T>(n);
            if (n == 0) return outp;
            bool ok = false;
            try
            {
                long bytes = (long)n * sizeof(T);
                var span = CollectionsMarshal.AsSpan(src);
                fixed (T* p = span) Buffer.MemoryCopy(p, (void*)Data(outp), bytes, bytes);
                ok = outp[0].Equals(src[0]) && outp[n - 1].Equals(src[n - 1]);
            }
            catch { ok = false; }
            if (!ok) for (int i = 0; i < n; i++) outp[i] = src[i];
            return outp;
        }

        internal static unsafe Il2CppStructArray<T> FromManaged<T>(T[] src) where T : unmanaged
        {
            int n = src.Length;
            var outp = new Il2CppStructArray<T>(n);
            if (n == 0) return outp;
            bool ok = false;
            try
            {
                long bytes = (long)n * sizeof(T);
                fixed (T* p = src) Buffer.MemoryCopy(p, (void*)Data(outp), bytes, bytes);
                ok = outp[0].Equals(src[0]) && outp[n - 1].Equals(src[n - 1]);
            }
            catch { ok = false; }
            if (!ok) for (int i = 0; i < n; i++) outp[i] = src[i];
            return outp;
        }
    }
}
