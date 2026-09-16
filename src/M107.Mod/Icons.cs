// ODG Operations - M107
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace M107
{

    internal static class Icons
    {

        const int Super = 2;

        static readonly Vector3 LightDir = new Vector3(-0.35f, 0.55f, -0.75f);

        struct Tri { internal Vector3 A, B, C, N; }

        internal static Sprite Render(string[] meshKeys, int size, Vector3 viewEuler, Color tint, string name)
        {
            try
            {
                var tris = Collect(meshKeys, viewEuler, out int meshes, out int unreadable);
                if (unreadable > 0)
                    M107Mod.Log("icon: '" + name + "' - " + unreadable + " mesh(es) are not readable, skipped");
                if (tris.Count == 0)
                {
                    M107Mod.Log("icon: '" + name + "' produced no geometry (" + meshes + " mesh(es) matched), keeping the donor icon");
                    return null;
                }

                int n = size * Super;
                var col = new Color32[n * n];
                var depth = new float[n * n];
                for (int i = 0; i < depth.Length; i++) depth[i] = float.MaxValue;

                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                for (int i = 0; i < tris.Count; i++)
                {
                    var t = tris[i];
                    minX = Mathf.Min(minX, Mathf.Min(t.A.x, Mathf.Min(t.B.x, t.C.x)));
                    maxX = Mathf.Max(maxX, Mathf.Max(t.A.x, Mathf.Max(t.B.x, t.C.x)));
                    minY = Mathf.Min(minY, Mathf.Min(t.A.y, Mathf.Min(t.B.y, t.C.y)));
                    maxY = Mathf.Max(maxY, Mathf.Max(t.A.y, Mathf.Max(t.B.y, t.C.y)));
                }
                float w = Mathf.Max(1e-6f, maxX - minX), h = Mathf.Max(1e-6f, maxY - minY);
                float pad = n * 0.06f;
                float scale = Mathf.Min((n - 2f * pad) / w, (n - 2f * pad) / h);
                float ox = n * 0.5f - (minX + maxX) * 0.5f * scale;
                float oy = n * 0.5f - (minY + maxY) * 0.5f * scale;

                var light = LightDir.normalized;
                for (int i = 0; i < tris.Count; i++)
                    Raster(tris[i], col, depth, n, scale, ox, oy, light, tint);

                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var outPix = new Il2CppStructArray<Color32>(size * size);
                Downsample(col, n, outPix, size);
                tex.SetPixels32(outPix);
                tex.Apply(false, false);
                tex.name = name + "_tex";
                tex.wrapMode = TextureWrapMode.Clamp;

                tex.hideFlags = HideFlags.DontUnloadUnusedAsset;

                var spr = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
                spr.name = name;
                spr.hideFlags = HideFlags.DontUnloadUnusedAsset;

                M107Mod.Log("icon: '" + name + "' rendered " + size + "x" + size + " from " +
                            meshes + " mesh(es), " + tris.Count + " triangle(s)");
                return spr;
            }
            catch (Exception e) { M107Mod.Log("icon: '" + name + "' render err: " + e.Message); return null; }
        }

        static List<Tri> Collect(string[] keys, Vector3 viewEuler, out int meshes, out int unreadable)
        {
            var tris = new List<Tri>();
            meshes = 0; unreadable = 0;
            var master = Master();
            if (master == null) return tris;

            var view = Matrix4x4.Rotate(Quaternion.Euler(viewEuler));
            var root = master.transform;

            var mfs = master.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < mfs.Length; i++)
            {
                var mf = mfs[i]; if (mf == null) continue;
                Mesh mesh = null; try { mesh = mf.sharedMesh; } catch { }
                if (mesh == null) continue;
                string nm; try { nm = mf.name; } catch { continue; }
                if (nm == null) continue;

                bool want = false;
                for (int k = 0; k < keys.Length; k++)
                    if (nm.IndexOf(keys[k], StringComparison.OrdinalIgnoreCase) >= 0) { want = true; break; }
                if (!want) continue;
                try { if (!mf.gameObject.activeSelf) continue; } catch { }

                Matrix4x4 m;
                try { m = view * root.worldToLocalMatrix * mf.transform.localToWorldMatrix; } catch { continue; }

                Vector3[] vs = null;
                int[] idx = null;

                bool readable = false; try { readable = mesh.isReadable; } catch { }
                if (readable)
                {
                    try
                    {
                        var cpuVerts = mesh.vertices; var cpuTris = mesh.triangles;
                        if (cpuVerts != null && cpuTris != null && cpuVerts.Length > 0 && cpuTris.Length >= 3)
                        {
                            vs = new Vector3[cpuVerts.Length];
                            for (int v = 0; v < cpuVerts.Length; v++) vs[v] = m.MultiplyPoint3x4(cpuVerts[v]);
                            idx = new int[cpuTris.Length];
                            for (int v = 0; v < cpuTris.Length; v++) idx[v] = cpuTris[v];
                        }
                    }
                    catch { }
                }
                else if (FromGpu(mesh, m, out vs, out idx)) { }
                else { unreadable++; continue; }

                if (vs == null || idx == null || vs.Length == 0 || idx.Length < 3) { unreadable++; continue; }

                meshes++;
                for (int t = 0; t + 2 < idx.Length; t += 3)
                {
                    int i0 = idx[t], i1 = idx[t + 1], i2 = idx[t + 2];
                    if (i0 < 0 || i1 < 0 || i2 < 0 || i0 >= vs.Length || i1 >= vs.Length || i2 >= vs.Length) continue;
                    var a = vs[i0]; var b = vs[i1]; var c = vs[i2];

                    var nrm = Vector3.Cross(b - a, c - a);
                    float len = nrm.magnitude;
                    if (len < 1e-12f) continue;
                    nrm /= len;
                    tris.Add(new Tri { A = a, B = b, C = c, N = nrm });
                }
            }
            return tris;
        }

        static bool FromGpu(Mesh mesh, Matrix4x4 m, out Vector3[] verts, out int[] idx)
        {
            verts = null; idx = null;
            GraphicsBuffer vb = null, ib = null;
            try
            {
                int vcount = 0; try { vcount = mesh.vertexCount; } catch { }
                if (vcount <= 0) return false;

                int stride = 0; try { stride = mesh.GetVertexBufferStride(0); } catch { }
                if (stride < 12) return false;

                try { vb = mesh.GetVertexBuffer(0); } catch { }
                try { ib = mesh.GetIndexBuffer(); } catch { }
                if (vb == null || ib == null) return false;
                try { if (!vb.IsValid() || !ib.IsValid()) return false; } catch { }

                var vraw = new Il2CppStructArray<byte>(vcount * stride);
                vb.GetData(vraw.Cast<Il2CppSystem.Array>());
                var managed = new byte[vcount * stride];
                for (int i = 0; i < managed.Length; i++) managed[i] = vraw[i];

                verts = new Vector3[vcount];
                for (int i = 0; i < vcount; i++)
                {
                    int o = i * stride;
                    var p = new Vector3(
                        BitConverter.ToSingle(managed, o),
                        BitConverter.ToSingle(managed, o + 4),
                        BitConverter.ToSingle(managed, o + 8));
                    verts[i] = m.MultiplyPoint3x4(p);
                }

                int icount = 0, istride = 0;
                try { icount = ib.count; istride = ib.stride; } catch { }
                if (icount <= 0 || istride <= 0) { verts = null; return false; }

                var iraw = new Il2CppStructArray<byte>(icount * istride);
                ib.GetData(iraw.Cast<Il2CppSystem.Array>());
                var imanaged = new byte[icount * istride];
                for (int i = 0; i < imanaged.Length; i++) imanaged[i] = iraw[i];

                bool sixteen = istride == 2;
                idx = new int[icount];
                for (int i = 0; i < icount; i++)
                    idx[i] = sixteen ? BitConverter.ToUInt16(imanaged, i * 2)
                                     : (int)BitConverter.ToUInt32(imanaged, i * 4);
                return true;
            }
            catch (Exception e)
            {
                M107Mod.Log("icon: GPU readback failed: " + e.Message);
                verts = null; idx = null;
                return false;
            }
            finally
            {
                try { if (vb != null) vb.Release(); } catch { }
                try { if (ib != null) ib.Release(); } catch { }
            }
        }

        static void Raster(Tri t, Color32[] col, float[] depth, int n, float scale, float ox, float oy,
                           Vector3 light, Color tint)
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
            float shade = 0.30f + 0.70f * lam;
            var c32 = new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(tint.r * shade * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(tint.g * shade * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(tint.b * shade * 255f), 0, 255),
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

        static GameObject _probe;
        static GameObject Master()
        {
            try
            {
                if (_probe != null) return _probe;
                if (!BarrettArt.Ready) return null;
                var holder = new GameObject("M107_IconSource");
                UnityEngine.Object.DontDestroyOnLoad(holder);
                holder.SetActive(false);
                _probe = BarrettArt.Attach(holder.transform, "iconsrc");
                return _probe;
            }
            catch { return null; }
        }
    }
}
