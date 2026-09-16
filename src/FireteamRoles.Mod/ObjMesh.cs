// ODG Operations - FireteamRoles
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace FireteamRoles
{

    internal static class ObjMesh
    {
        internal static Mesh Load(string path, float scale, out string why)
        {
            why = "";
            try
            {
                if (!File.Exists(path)) { why = "not found: " + path; return null; }

                var pos = new List<Vector3>();
                var uv = new List<Vector2>();
                var nrm = new List<Vector3>();

                var outPos = new List<Vector3>();
                var outUv = new List<Vector2>();
                var outNrm = new List<Vector3>();
                var outTri = new List<int>();

                var map = new Dictionary<long, int>();
                var face = new List<long>(16);
                var inv = CultureInfo.InvariantCulture;

                foreach (var raw in File.ReadAllLines(path))
                {
                    if (raw.Length < 2) continue;
                    char c0 = raw[0];
                    if (c0 == '#') continue;

                    var t = raw.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (t.Length == 0) continue;

                    if (t[0] == "v" && t.Length >= 4)
                    {
                        pos.Add(new Vector3(
                            float.Parse(t[1], inv) * scale,
                            float.Parse(t[2], inv) * scale,
                            float.Parse(t[3], inv) * scale));
                    }
                    else if (t[0] == "vt" && t.Length >= 3)
                    {
                        uv.Add(new Vector2(float.Parse(t[1], inv), float.Parse(t[2], inv)));
                    }
                    else if (t[0] == "vn" && t.Length >= 4)
                    {
                        nrm.Add(new Vector3(
                            float.Parse(t[1], inv), float.Parse(t[2], inv), float.Parse(t[3], inv)));
                    }
                    else if (t[0] == "f" && t.Length >= 4)
                    {
                        face.Clear();
                        for (int i = 1; i < t.Length; i++)
                        {
                            int vi, ti, ni;
                            if (!Corner(t[i], pos.Count, uv.Count, nrm.Count, out vi, out ti, out ni)) continue;
                            face.Add(((long)(vi + 1) << 42) | ((long)(ti + 1) << 21) | (long)(ni + 1));
                        }
                        if (face.Count < 3) continue;

                        for (int i = 1; i + 1 < face.Count; i++)
                        {
                            int a = Corner(face[0], pos, uv, nrm, outPos, outUv, outNrm, map);
                            int b = Corner(face[i], pos, uv, nrm, outPos, outUv, outNrm, map);
                            int c = Corner(face[i + 1], pos, uv, nrm, outPos, outUv, outNrm, map);
                            if (a < 0 || b < 0 || c < 0) continue;
                            outTri.Add(a); outTri.Add(b); outTri.Add(c);

                            Vector3 fn = Vector3.Cross(outPos[b] - outPos[a], outPos[c] - outPos[a]);
                            if (fn.sqrMagnitude > 1e-12f)
                            {
                                fn = fn.normalized;
                                if (outNrm[a].sqrMagnitude < 1e-6f) outNrm[a] = fn;
                                if (outNrm[b].sqrMagnitude < 1e-6f) outNrm[b] = fn;
                                if (outNrm[c].sqrMagnitude < 1e-6f) outNrm[c] = fn;
                            }
                        }
                    }
                }

                if (outPos.Count < 3 || outTri.Count < 3) { why = "no geometry parsed"; return null; }

                var m = new Mesh();
                m.name = Path.GetFileNameWithoutExtension(path);
                if (outPos.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

                m.hideFlags = HideFlags.DontUnloadUnusedAsset;

                var vv = MeshCut.FromManaged(outPos);
                var nn2 = MeshCut.FromManaged(outNrm);
                var uu = MeshCut.FromManaged(outUv);
                var tt = MeshCut.FromManaged(outTri);

                m.SetVertices(vv);
                m.SetNormals(nn2);
                m.SetUVs(0, uu);
                m.SetTriangles(tt, 0);
                m.RecalculateBounds();
                m.RecalculateTangents();

                Log.Msg("obj '" + m.name + "': " + (outTri.Count / 3) + " tris, " + outPos.Count +
                        " verts, bounds " + m.bounds.size.ToString("0.00"));
                return m;
            }
            catch (Exception e) { why = e.Message; return null; }
        }

        static bool Corner(string tok, int nv, int nt, int nn, out int vi, out int ti, out int ni)
        {
            vi = ti = ni = -1;
            var p = tok.Split('/');
            if (p.Length == 0 || p[0].Length == 0) return false;

            int v;
            if (!int.TryParse(p[0], out v)) return false;
            vi = v > 0 ? v - 1 : nv + v;
            if (vi < 0 || vi >= nv) return false;

            if (p.Length > 1 && p[1].Length > 0)
            {
                int t;
                if (int.TryParse(p[1], out t))
                {
                    int k = t > 0 ? t - 1 : nt + t;
                    if (k >= 0 && k < nt) ti = k;
                }
            }
            if (p.Length > 2 && p[2].Length > 0)
            {
                int n;
                if (int.TryParse(p[2], out n))
                {
                    int k = n > 0 ? n - 1 : nn + n;
                    if (k >= 0 && k < nn) ni = k;
                }
            }
            return true;
        }

        static int Corner(long key, List<Vector3> pos, List<Vector2> uv, List<Vector3> nrm,
                          List<Vector3> outPos, List<Vector2> outUv, List<Vector3> outNrm,
                          Dictionary<long, int> map)
        {
            int existing;
            if (map.TryGetValue(key, out existing)) return existing;

            int vi = (int)((key >> 42) & 0x1FFFFF) - 1;
            int ti = (int)((key >> 21) & 0x1FFFFF) - 1;
            int ni = (int)(key & 0x1FFFFF) - 1;
            if (vi < 0 || vi >= pos.Count) return -1;

            outPos.Add(pos[vi]);
            outUv.Add(ti >= 0 && ti < uv.Count ? uv[ti] : Vector2.zero);
            outNrm.Add(ni >= 0 && ni < nrm.Count ? nrm[ni] : Vector3.zero);

            int idx = outPos.Count - 1;
            map[key] = idx;
            return idx;
        }
    }
}
