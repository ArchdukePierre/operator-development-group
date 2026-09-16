// ODG Operations - HSR
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;

namespace HSR
{

    internal static class MagIcon
    {
        const int Super = 2;
        const int Size = 128;

        static readonly Vector3 View = new Vector3(12f, -28f, 0f);

        static readonly Color Tint = new Color(0.34f, 0.34f, 0.36f, 1f);
        static readonly Vector3 LightDir = new Vector3(-0.35f, 0.55f, -0.75f);

        struct Tri { internal Vector3 A, B, C, N; }

        static Sprite _sprite;
        static bool _tried;

        internal static Sprite Sprite => _sprite;

        internal static bool Ready
        {
            get { try { return _sprite != null; } catch { return false; } }
        }

        internal static Sprite Build(Transform magGroup, MelonLogger.Instance log)
        {
            try
            {
                if (Ready) return _sprite;
                if (magGroup == null) return null;

                var tris = Collect(magGroup, out int meshes, out int unreadable);
                if (tris.Count == 0)
                {
                    if (!_tried)
                    {
                        _tried = true;
                        log.Msg("magicon: no geometry from m14_mag (" + meshes + " mesh(es), "
                              + unreadable + " unreadable) — keeping the donor emblem");
                    }
                    return null;
                }

                int n = Size * Super;
                var col = new Color32[n * n];
                var depth = new float[n * n];
                for (int i = 0; i < depth.Length; i++) depth[i] = float.MaxValue;

                float minX = float.MaxValue, minY = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue;
                for (int i = 0; i < tris.Count; i++)
                {
                    var t = tris[i];
                    minX = Mathf.Min(minX, Mathf.Min(t.A.x, Mathf.Min(t.B.x, t.C.x)));
                    maxX = Mathf.Max(maxX, Mathf.Max(t.A.x, Mathf.Max(t.B.x, t.C.x)));
                    minY = Mathf.Min(minY, Mathf.Min(t.A.y, Mathf.Min(t.B.y, t.C.y)));
                    maxY = Mathf.Max(maxY, Mathf.Max(t.A.y, Mathf.Max(t.B.y, t.C.y)));
                }
                float w = Mathf.Max(1e-6f, maxX - minX), h = Mathf.Max(1e-6f, maxY - minY);
                float pad = n * 0.07f;
                float scale = Mathf.Min((n - 2f * pad) / w, (n - 2f * pad) / h);
                float ox = n * 0.5f - (minX + maxX) * 0.5f * scale;
                float oy = n * 0.5f - (minY + maxY) * 0.5f * scale;

                var light = LightDir.normalized;
                for (int i = 0; i < tris.Count; i++)
                    Raster(tris[i], col, depth, n, scale, ox, oy, light);

                var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                var outPix = new Il2CppStructArray<Color32>(Size * Size);
                Downsample(col, n, outPix, Size);
                tex.SetPixels32(outPix);
                tex.Apply(false, false);
                tex.name = "M14_MagIcon_tex";
                tex.wrapMode = TextureWrapMode.Clamp;

                tex.hideFlags = HideFlags.DontUnloadUnusedAsset;

                var spr = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
                spr.name = "M14_MagIcon";
                spr.hideFlags = HideFlags.DontUnloadUnusedAsset;
                _sprite = spr;

                log.Msg("magicon: rendered " + Size + "x" + Size + " from " + meshes
                      + " mesh(es), " + tris.Count + " triangle(s)");
                return _sprite;
            }
            catch (Exception e) { log.Warning("magicon: render failed: " + e.Message); return null; }
        }

        static List<Tri> Collect(Transform root, out int meshes, out int unreadable)
        {
            var tris = new List<Tri>();
            meshes = 0; unreadable = 0;

            var view = Matrix4x4.Rotate(Quaternion.Euler(View));
            var mfs = root.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < mfs.Length; i++)
            {
                var mf = mfs[i]; if (mf == null) continue;
                Mesh mesh = null; try { mesh = mf.sharedMesh; } catch { }
                if (mesh == null) continue;

                bool readable = false; try { readable = mesh.isReadable; } catch { }
                if (!readable) { unreadable++; continue; }

                Matrix4x4 m;
                try { m = view * root.worldToLocalMatrix * mf.transform.localToWorldMatrix; }
                catch { continue; }

                Il2CppStructArray<Vector3> vs = null;
                Il2CppStructArray<int> idx = null;
                try { vs = mesh.vertices; idx = mesh.triangles; } catch { }
                if (vs == null || idx == null || vs.Length == 0 || idx.Length < 3)
                { unreadable++; continue; }

                var pts = new Vector3[vs.Length];
                for (int v = 0; v < vs.Length; v++) pts[v] = m.MultiplyPoint3x4(vs[v]);

                meshes++;
                for (int t = 0; t + 2 < idx.Length; t += 3)
                {
                    int i0 = idx[t], i1 = idx[t + 1], i2 = idx[t + 2];
                    if (i0 < 0 || i1 < 0 || i2 < 0) continue;
                    if (i0 >= pts.Length || i1 >= pts.Length || i2 >= pts.Length) continue;
                    var a = pts[i0]; var b = pts[i1]; var c = pts[i2];
                    var nrm = Vector3.Cross(b - a, c - a);
                    float len = nrm.magnitude;
                    if (len < 1e-12f) continue;
                    tris.Add(new Tri { A = a, B = b, C = c, N = nrm / len });
                }
            }
            return tris;
        }

        static void Raster(Tri t, Color32[] col, float[] depth, int n, float scale,
                           float ox, float oy, Vector3 light)
        {
            float ax = t.A.x * scale + ox, ay = t.A.y * scale + oy;
            float bx = t.B.x * scale + ox, by = t.B.y * scale + oy;
            float cx = t.C.x * scale + ox, cy = t.C.y * scale + oy;

            float area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
            if (Mathf.Abs(area) < 1e-9f) return;
            float inv = 1f / area;

            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ax, Mathf.Min(bx, cx))));
            int x1 = Mathf.Min(n - 1, Mathf.CeilToInt(Mathf.Max(ax, Mathf.Max(bx, cx))));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ay, Mathf.Min(by, cy))));
            int y1 = Mathf.Min(n - 1, Mathf.CeilToInt(Mathf.Max(ay, Mathf.Max(by, cy))));
            if (x1 < x0 || y1 < y0) return;

            float lam = Mathf.Abs(t.N.x * light.x + t.N.y * light.y + t.N.z * light.z);
            float shade = 0.32f + 0.68f * lam;
            var c32 = new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(Tint.r * shade * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(Tint.g * shade * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(Tint.b * shade * 255f), 0, 255),
                255);

            for (int y = y0; y <= y1; y++)
            {
                float py = y + 0.5f;
                int row = y * n;
                for (int x = x0; x <= x1; x++)
                {
                    float px = x + 0.5f;
                    float w0 = ((bx - ax) * (py - ay) - (by - ay) * (px - ax)) * inv;
                    float w1 = ((px - ax) * (cy - ay) - (py - ay) * (cx - ax)) * inv;
                    float w2 = 1f - w0 - w1;
                    if (w0 < 0f || w1 < 0f || w2 < 0f) continue;

                    float z = t.A.z * w2 + t.B.z * w1 + t.C.z * w0;
                    int o = row + x;
                    if (z >= depth[o]) continue;
                    depth[o] = z;
                    col[o] = c32;
                }
            }
        }

        static void Downsample(Color32[] src, int n, Il2CppStructArray<Color32> dst, int size)
        {
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int r = 0, g = 0, b = 0, a = 0;
                    for (int sy = 0; sy < Super; sy++)
                    {
                        int row = (y * Super + sy) * n + x * Super;
                        for (int sx = 0; sx < Super; sx++)
                        {
                            var p = src[row + sx];
                            r += p.r; g += p.g; b += p.b; a += p.a;
                        }
                    }
                    int cnt = Super * Super;

                    int cov = a / 255;
                    if (cov > 0) { r /= cov; g /= cov; b /= cov; }
                    dst[y * size + x] = new Color32(
                        (byte)Mathf.Clamp(r, 0, 255), (byte)Mathf.Clamp(g, 0, 255),
                        (byte)Mathf.Clamp(b, 0, 255), (byte)Mathf.Clamp(a / cnt, 0, 255));
                }
        }
    }
}
