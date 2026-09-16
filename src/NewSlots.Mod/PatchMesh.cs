// ODG Operations - NewSlots
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace NewSlots
{

    internal static class PatchMesh
    {
        const int GRID_W = 44;
        const float STRIP_FRACTION = 1f / 16f;

        internal static bool[,] SampleFor(Texture2D art)
        {
            float artFrac = 1f - STRIP_FRACTION;
            float aspect = (art.height * artFrac) / Mathf.Max(1f, art.width);
            int gh = Mathf.Clamp(Mathf.RoundToInt(GRID_W * aspect), 6, 72);
            return Sample(art, GRID_W, gh, artFrac);
        }

        internal static Mesh BuildCard(float width, float height, float thickness, string name, Mesh reuse)
        {
            try
            {
                float hw = width / 2f, hh = height / 2f, hz = thickness / 2f;
                var verts = new List<Vector3>();
                var norms = new List<Vector3>();
                var uvs = new List<Vector2>();
                var tris = new List<int>();

                void Face(float z, Vector3 n, bool flip)
                {
                    int i = verts.Count;
                    verts.Add(new Vector3(-hw, -hh, z)); verts.Add(new Vector3(hw, -hh, z));
                    verts.Add(new Vector3(hw, hh, z)); verts.Add(new Vector3(-hw, hh, z));
                    for (int k = 0; k < 4; k++) norms.Add(n);

                    float v0 = STRIP_FRACTION, v1 = 1f;

                    if (!flip)
                    {

                        uvs.Add(new Vector2(0f, v0)); uvs.Add(new Vector2(1f, v0));
                        uvs.Add(new Vector2(1f, v1)); uvs.Add(new Vector2(0f, v1));
                        tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
                        tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
                    }
                    else
                    {

                        uvs.Add(new Vector2(1f, v0)); uvs.Add(new Vector2(0f, v0));
                        uvs.Add(new Vector2(0f, v1)); uvs.Add(new Vector2(1f, v1));
                        tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
                        tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
                    }
                }

                Face(hz, Vector3.forward, false);
                Face(-hz, Vector3.back, true);

                if (thickness < 0.0008f) hz = 0.0004f;

                var mesh = reuse != null ? reuse : new Mesh();
                if (reuse != null) mesh.Clear();
                mesh.name = "ODG_PatchCard_" + name;

                var v = new Il2CppStructArray<Vector3>(verts.Count);
                for (int i = 0; i < verts.Count; i++) v[i] = verts[i];
                mesh.vertices = v;
                var n2 = new Il2CppStructArray<Vector3>(norms.Count);
                for (int i = 0; i < norms.Count; i++) n2[i] = norms[i];
                mesh.normals = n2;
                var u2 = new Il2CppStructArray<Vector2>(uvs.Count);
                for (int i = 0; i < uvs.Count; i++) u2[i] = uvs[i];
                mesh.uv = u2;
                var t2 = new Il2CppStructArray<int>(tris.Count);
                for (int i = 0; i < tris.Count; i++) t2[i] = tris[i];
                mesh.triangles = t2;

                mesh.RecalculateBounds();
                Art.Pin(mesh);
                return mesh;
            }
            catch (Exception e)
            {
                NewSlotsMod.Detail("PatchMesh card " + name + " threw: " + e.Message);
                return null;
            }
        }

        internal static Mesh Build(bool[,] fill, float width, float height, float thickness, string name, Mesh reuse)
        {
            try
            {
                if (fill == null) return null;
                int gw = fill.GetLength(0);
                int gh = fill.GetLength(1);

                float pw = width, ph = width * (gh / (float)gw) * 1f;
                float artAspect = gh / (float)gw;
                if (width * artAspect <= height) { pw = width; ph = width * artAspect; }
                else { ph = height; pw = height / artAspect; }

                float cx = pw / gw;
                float cy = ph / gh;
                float x0 = -pw / 2f, y0 = -ph / 2f;
                float zf = thickness / 2f, zb = -thickness / 2f;

                var verts = new List<Vector3>();
                var norms = new List<Vector3>();
                var uvs = new List<Vector2>();
                var tris = new List<int>();

                float StripV() { return STRIP_FRACTION * 0.5f; }
                Vector2 ArtUV(float gx, float gy)
                {

                    float u = gx / gw;
                    float v = STRIP_FRACTION + (1f - STRIP_FRACTION) * (1f - gy / gh);
                    return new Vector2(u, v);
                }

                void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n,
                          Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
                {
                    int i = verts.Count;
                    verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                    norms.Add(n); norms.Add(n); norms.Add(n); norms.Add(n);
                    uvs.Add(ua); uvs.Add(ub); uvs.Add(uc); uvs.Add(ud);
                    tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
                    tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
                }

                for (int y = 0; y < gh; y++)
                {
                    int x = 0;
                    while (x < gw)
                    {
                        if (!fill[x, y]) { x++; continue; }
                        int x1 = x;
                        while (x1 < gw && fill[x1, y]) x1++;

                        float ax = x0 + x * cx, bx = x0 + x1 * cx;
                        float ay = y0 + (gh - 1 - y) * cy, by = ay + cy;

                        var uvA = ArtUV(x, y + 1); var uvB = ArtUV(x1, y + 1);
                        var uvC = ArtUV(x1, y); var uvD = ArtUV(x, y);

                        Quad(new Vector3(ax, ay, zf), new Vector3(bx, ay, zf),
                             new Vector3(bx, by, zf), new Vector3(ax, by, zf),
                             Vector3.forward, uvA, uvB, uvC, uvD);

                        Quad(new Vector3(bx, ay, zb), new Vector3(ax, ay, zb),
                             new Vector3(ax, by, zb), new Vector3(bx, by, zb),
                             Vector3.back, uvB, uvA, uvD, uvC);
                        x = x1;
                    }
                }

                var sv = new Vector2(0.5f, StripV());
                float bev = Mathf.Min(thickness * 1.2f, Mathf.Min(cx, cy) * 0.9f);
                float zt = zf + thickness * 0.15f;
                for (int y = 0; y < gh; y++)
                {
                    for (int x = 0; x < gw; x++)
                    {
                        if (!fill[x, y]) continue;
                        float ax = x0 + x * cx, bx = ax + cx;
                        float ay = y0 + (gh - 1 - y) * cy, by = ay + cy;

                        if (y == 0 || !fill[x, y - 1])
                            Quad(new Vector3(ax, by - bev, zt), new Vector3(bx, by - bev, zt),
                                 new Vector3(bx, by, zb), new Vector3(ax, by, zb),
                                 (Vector3.up + Vector3.forward).normalized, sv, sv, sv, sv);
                        if (y == gh - 1 || !fill[x, y + 1])
                            Quad(new Vector3(bx, ay + bev, zt), new Vector3(ax, ay + bev, zt),
                                 new Vector3(ax, ay, zb), new Vector3(bx, ay, zb),
                                 (Vector3.down + Vector3.forward).normalized, sv, sv, sv, sv);
                        if (x == 0 || !fill[x - 1, y])
                            Quad(new Vector3(ax + bev, ay, zt), new Vector3(ax + bev, by, zt),
                                 new Vector3(ax, by, zb), new Vector3(ax, ay, zb),
                                 (Vector3.left + Vector3.forward).normalized, sv, sv, sv, sv);
                        if (x == gw - 1 || !fill[x + 1, y])
                            Quad(new Vector3(bx - bev, by, zt), new Vector3(bx - bev, ay, zt),
                                 new Vector3(bx, ay, zb), new Vector3(bx, by, zb),
                                 (Vector3.right + Vector3.forward).normalized, sv, sv, sv, sv);
                    }
                }

                var mesh = reuse != null ? reuse : new Mesh();
                if (reuse != null) mesh.Clear();
                mesh.name = "ODG_PatchMesh_" + name;

                var v = new Il2CppStructArray<Vector3>(verts.Count);
                for (int i = 0; i < verts.Count; i++) v[i] = verts[i];
                mesh.vertices = v;

                var n2 = new Il2CppStructArray<Vector3>(norms.Count);
                for (int i = 0; i < norms.Count; i++) n2[i] = norms[i];
                mesh.normals = n2;

                var u2 = new Il2CppStructArray<Vector2>(uvs.Count);
                for (int i = 0; i < uvs.Count; i++) u2[i] = uvs[i];
                mesh.uv = u2;

                var t2 = new Il2CppStructArray<int>(tris.Count);
                for (int i = 0; i < tris.Count; i++) t2[i] = tris[i];
                mesh.triangles = t2;

                mesh.RecalculateBounds();
                Art.Pin(mesh);
                return mesh;
            }
            catch (Exception e)
            {
                NewSlotsMod.Log("PatchMesh " + name + " threw: " + e.Message);
                return null;
            }
        }

        internal static void Remap(Mesh mesh, int wAxis, int hAxis, int tAxis, Vector3 centre)
        {
            try
            {
                if (wAxis == 0 && hAxis == 1 && tAxis == 2 && centre == Vector3.zero) return;

                var v = mesh.vertices;
                var n = mesh.normals;
                var nv = new Il2CppStructArray<Vector3>(v.Length);
                var nn = new Il2CppStructArray<Vector3>(n.Length);
                for (int i = 0; i < v.Length; i++)
                {
                    var p = v[i];
                    var q = Vector3.zero;
                    q[wAxis] = p.x; q[hAxis] = p.y; q[tAxis] = p.z;
                    nv[i] = q + centre;

                    var m = n[i];
                    var r = Vector3.zero;
                    r[wAxis] = m.x; r[hAxis] = m.y; r[tAxis] = m.z;
                    nn[i] = r;
                }
                mesh.vertices = nv;
                mesh.normals = nn;

                bool even = (wAxis == 0 && hAxis == 1 && tAxis == 2) ||
                            (wAxis == 1 && hAxis == 2 && tAxis == 0) ||
                            (wAxis == 2 && hAxis == 0 && tAxis == 1);
                if (!even)
                {
                    var t = mesh.triangles;
                    var nt = new Il2CppStructArray<int>(t.Length);
                    for (int i = 0; i < t.Length; i += 3)
                    { nt[i] = t[i]; nt[i + 1] = t[i + 2]; nt[i + 2] = t[i + 1]; }
                    mesh.triangles = nt;
                }
                mesh.RecalculateBounds();
            }
            catch (Exception e) { NewSlotsMod.Log("PatchMesh remap threw: " + e.Message); }
        }

        static bool[,] Sample(Texture2D art, int gw, int gh, float artFrac)
        {
            try
            {
                var px = art.GetPixels32();
                int w = art.width, h = art.height;
                int artTop = h;
                int artRows = Mathf.RoundToInt(h * artFrac);
                int rowBase = h - artRows;

                var fill = new bool[gw, gh];
                for (int gy = 0; gy < gh; gy++)
                {
                    for (int gx = 0; gx < gw; gx++)
                    {

                        int sx0 = gx * w / gw, sx1 = Math.Max(sx0 + 1, (gx + 1) * w / gw);
                        int iy0 = gy * artRows / gh, iy1 = Math.Max(iy0 + 1, (gy + 1) * artRows / gh);
                        int hit = 0, total = 0;
                        for (int sy = iy0; sy < iy1; sy += Math.Max(1, (iy1 - iy0) / 4))
                        {
                            int py = rowBase + (artRows - 1 - sy);
                            for (int sx = sx0; sx < sx1; sx += Math.Max(1, (sx1 - sx0) / 4))
                            {
                                total++;
                                if (px[py * w + sx].a > 96) hit++;
                            }
                        }
                        fill[gx, gy] = total > 0 && hit * 2 >= total;
                    }
                }

                var clean = new bool[gw, gh];
                for (int y = 0; y < gh; y++)
                    for (int x = 0; x < gw; x++)
                    {
                        if (!fill[x, y]) continue;
                        int nb = 0;
                        if (x > 0 && fill[x - 1, y]) nb++;
                        if (x < gw - 1 && fill[x + 1, y]) nb++;
                        if (y > 0 && fill[x, y - 1]) nb++;
                        if (y < gh - 1 && fill[x, y + 1]) nb++;
                        clean[x, y] = nb >= 1;
                    }
                return clean;
            }
            catch (Exception e)
            {
                NewSlotsMod.Log("PatchMesh sample threw: " + e.Message);
                return null;
            }
        }
    }
}
