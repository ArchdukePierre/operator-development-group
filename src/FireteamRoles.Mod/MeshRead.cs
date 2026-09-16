// ODG Operations - FireteamRoles
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace FireteamRoles
{

    internal static class MeshRead
    {

        static readonly Dictionary<int, Mesh> _recovered = new Dictionary<int, Mesh>();

        internal static void Forget()
        {
            int n = _recovered.Count;
            _recovered.Clear();
            if (n > 0) Log.Detail("released " + n + " recovered mesh(es)");
        }

        internal static Mesh Recover(Mesh src, out string why)
        {
            why = "";
            if (src == null) { why = "no mesh"; return null; }

            int id = src.GetInstanceID();
            Mesh cached;
            if (_recovered.TryGetValue(id, out cached) && cached != null) return cached;

            if (!Cfg.BreachGpuReadback.Value) { why = "gpu readback disabled"; return null; }

            try
            {
                int vcount = src.vertexCount;
                if (vcount <= 0) { why = "no vertices"; return null; }

                int budget = Mathf.Max(3000, Cfg.BreachTriangleBudget.Value);
                if (vcount > budget * 3) { why = "too large to recover (" + vcount + " verts)"; return null; }

                var attrs = new Il2CppSystem.Collections.Generic.List<VertexAttributeDescriptor>();
                int nattr = src.GetVertexAttributesList(attrs);
                if (nattr <= 0) { why = "no vertex attributes"; return null; }

                int streams = Mathf.Max(1, src.vertexBufferCount);
                var raw = new byte[streams][];
                var stride = new int[streams];

                for (int s = 0; s < streams; s++)
                {
                    stride[s] = src.GetVertexBufferStride(s);
                    if (stride[s] <= 0) { why = "stream " + s + " has no stride"; return null; }
                    raw[s] = ReadBuffer(src.GetVertexBufferImpl(s), vcount * stride[s], out why);
                    if (raw[s] == null) return null;
                }

                bool i16 = src.indexFormat == IndexFormat.UInt16;
                int isize = i16 ? 2 : 4;
                int subs = Mathf.Max(1, src.subMeshCount);

                int total = 0;
                for (int sm = 0; sm < subs; sm++) total += (int)src.GetIndexCountImpl(sm, 0);
                if (total < 3) { why = "no indices"; return null; }

                byte[] ib = ReadBuffer(src.GetIndexBufferImpl(), total * isize, out why);
                if (ib == null)
                {

                    ib = ReadBuffer(src.GetIndexBufferImpl(), 0, out why);
                    if (ib == null) return null;
                }

                var m = new Mesh();
                m.name = src.name + "_recovered";
                if (vcount > 65000) m.indexFormat = IndexFormat.UInt32;

                var offset = new int[streams];
                var pos = new Vector3[vcount];
                Vector3[] nrm = null; Vector4[] tan = null; Color[] col = null;
                var uvs = new Vector2[4][];
                bool gotPos = false;

                for (int a = 0; a < nattr; a++)
                {
                    var d = attrs[a];
                    int st = d.stream;
                    if (st < 0 || st >= streams) continue;
                    int off = offset[st];
                    int sz = FormatSize(d.format) * d.dimension;
                    offset[st] += sz;

                    switch (d.attribute)
                    {
                        case VertexAttribute.Position:
                            if (!Decode3(raw[st], stride[st], off, d, vcount, pos)) { why = "position format " + d.format; return null; }
                            gotPos = true;
                            break;
                        case VertexAttribute.Normal:
                            nrm = new Vector3[vcount];
                            if (!Decode3(raw[st], stride[st], off, d, vcount, nrm)) nrm = null;
                            break;
                        case VertexAttribute.Tangent:
                            tan = new Vector4[vcount];
                            if (!Decode4(raw[st], stride[st], off, d, vcount, tan)) tan = null;
                            break;
                        case VertexAttribute.Color:
                            col = new Color[vcount];
                            if (!DecodeColor(raw[st], stride[st], off, d, vcount, col)) col = null;
                            break;
                        case VertexAttribute.TexCoord0:
                        case VertexAttribute.TexCoord1:
                        case VertexAttribute.TexCoord2:
                        case VertexAttribute.TexCoord3:
                            {
                                int ch = (int)d.attribute - (int)VertexAttribute.TexCoord0;
                                if (ch < 0 || ch > 3) break;
                                var u = new Vector2[vcount];
                                if (Decode2(raw[st], stride[st], off, d, vcount, u)) uvs[ch] = u;
                                break;
                            }
                    }
                }

                if (!gotPos) { why = "no position attribute"; return null; }

                m.SetVertices(ToIl2(pos));
                if (nrm != null) m.SetNormals(ToIl2(nrm));
                if (tan != null) m.SetTangents(ToIl2V4(tan));
                if (col != null) m.SetColors(ToIl2C(col));
                for (int c = 0; c < 4; c++) if (uvs[c] != null) m.SetUVs(c, ToIl2V2(uvs[c]));

                m.subMeshCount = subs;
                for (int sm = 0; sm < subs; sm++)
                {
                    int start = (int)src.GetIndexStartImpl(sm, 0);
                    int cnt = (int)src.GetIndexCountImpl(sm, 0);
                    int baseV = (int)src.GetBaseVertexImpl(sm);
                    if (cnt < 3) continue;

                    var tri = new Il2CppStructArray<int>(cnt);
                    for (int k = 0; k < cnt; k++)
                    {
                        int b = (start + k) * isize;
                        int v = i16 ? (ib[b] | (ib[b + 1] << 8))
                                    : (ib[b] | (ib[b + 1] << 8) | (ib[b + 2] << 16) | (ib[b + 3] << 24));
                        v += baseV;
                        tri[k] = (v >= 0 && v < vcount) ? v : 0;
                    }
                    m.SetTriangles(tri, sm);
                }

                m.RecalculateBounds();
                _recovered[id] = m;
                Log.Msg("recovered '" + src.name + "' off the GPU: " + vcount + " verts, " +
                        (total / 3) + " tris, " + streams + " stream(s), " + nattr + " attribute(s)");
                Log.Fired("gpu-readback");
                return m;
            }
            catch (Exception e) { why = "readback failed: " + e.Message; return null; }
        }

        static byte[] ReadBuffer(GraphicsBuffer gb, int bytes, out string why)
        {
            why = "";
            if (gb == null) { why = "no buffer"; return null; }
            try
            {
                int total = bytes > 0 ? bytes : gb.count * gb.stride;
                if (total <= 0 || total > 64 * 1024 * 1024) { why = "buffer size " + total; return null; }

                var arr = new Il2CppStructArray<byte>(total);
                bool ok = false;

                var asArray = arr.Cast<Il2CppSystem.Array>();
                try { gb.InternalGetData(asArray, 0, 0, total, 1); ok = true; }
                catch { }
                if (!ok) { try { gb.GetData(asArray); ok = true; } catch (Exception e2) { why = e2.Message; } }
                if (!ok) return null;

                return MeshCut.ToManaged(arr);
            }
            catch (Exception e) { why = e.Message; return null; }
            finally { try { gb.Dispose(); } catch { } }
        }

        static int FormatSize(VertexAttributeFormat f)
        {
            switch (f)
            {
                case VertexAttributeFormat.Float32:
                case VertexAttributeFormat.UInt32:
                case VertexAttributeFormat.SInt32: return 4;
                case VertexAttributeFormat.Float16:
                case VertexAttributeFormat.UNorm16:
                case VertexAttributeFormat.SNorm16:
                case VertexAttributeFormat.UInt16:
                case VertexAttributeFormat.SInt16: return 2;
                default: return 1;
            }
        }

        static float Comp(byte[] b, int at, VertexAttributeFormat f)
        {
            switch (f)
            {
                case VertexAttributeFormat.Float32: return BitConverter.ToSingle(b, at);
                case VertexAttributeFormat.Float16: return Half(BitConverter.ToUInt16(b, at));
                case VertexAttributeFormat.UNorm16: return BitConverter.ToUInt16(b, at) / 65535f;
                case VertexAttributeFormat.SNorm16: return Mathf.Max(BitConverter.ToInt16(b, at) / 32767f, -1f);
                case VertexAttributeFormat.UNorm8: return b[at] / 255f;
                case VertexAttributeFormat.SNorm8: return Mathf.Max((sbyte)b[at] / 127f, -1f);
                default: return 0f;
            }
        }

        static bool Supported(VertexAttributeFormat f)
        {
            return f == VertexAttributeFormat.Float32 || f == VertexAttributeFormat.Float16
                || f == VertexAttributeFormat.UNorm16 || f == VertexAttributeFormat.SNorm16
                || f == VertexAttributeFormat.UNorm8 || f == VertexAttributeFormat.SNorm8;
        }

        static float Half(ushort h)
        {
            int sign = (h >> 15) & 1, exp = (h >> 10) & 0x1F, man = h & 0x3FF;
            if (exp == 0) return (sign == 1 ? -1f : 1f) * man * 5.9604645e-8f;
            if (exp == 31) return man == 0 ? (sign == 1 ? float.NegativeInfinity : float.PositiveInfinity) : float.NaN;
            return (sign == 1 ? -1f : 1f) * Mathf.Pow(2f, exp - 15) * (1f + man / 1024f);
        }

        static bool Decode3(byte[] b, int stride, int off, VertexAttributeDescriptor d, int n, Vector3[] outp)
        {
            if (!Supported(d.format) || d.dimension < 3) return false;
            int cs = FormatSize(d.format);
            for (int i = 0; i < n; i++)
            {
                int at = i * stride + off;
                if (at + cs * 3 > b.Length) return false;
                outp[i] = new Vector3(Comp(b, at, d.format), Comp(b, at + cs, d.format), Comp(b, at + cs * 2, d.format));
            }
            return true;
        }

        static bool Decode4(byte[] b, int stride, int off, VertexAttributeDescriptor d, int n, Vector4[] outp)
        {
            if (!Supported(d.format) || d.dimension < 4) return false;
            int cs = FormatSize(d.format);
            for (int i = 0; i < n; i++)
            {
                int at = i * stride + off;
                if (at + cs * 4 > b.Length) return false;
                outp[i] = new Vector4(Comp(b, at, d.format), Comp(b, at + cs, d.format),
                                      Comp(b, at + cs * 2, d.format), Comp(b, at + cs * 3, d.format));
            }
            return true;
        }

        static bool Decode2(byte[] b, int stride, int off, VertexAttributeDescriptor d, int n, Vector2[] outp)
        {
            if (!Supported(d.format) || d.dimension < 2) return false;
            int cs = FormatSize(d.format);
            for (int i = 0; i < n; i++)
            {
                int at = i * stride + off;
                if (at + cs * 2 > b.Length) return false;
                outp[i] = new Vector2(Comp(b, at, d.format), Comp(b, at + cs, d.format));
            }
            return true;
        }

        static bool DecodeColor(byte[] b, int stride, int off, VertexAttributeDescriptor d, int n, Color[] outp)
        {
            if (!Supported(d.format) || d.dimension < 4) return false;
            int cs = FormatSize(d.format);
            for (int i = 0; i < n; i++)
            {
                int at = i * stride + off;
                if (at + cs * 4 > b.Length) return false;
                outp[i] = new Color(Comp(b, at, d.format), Comp(b, at + cs, d.format),
                                    Comp(b, at + cs * 2, d.format), Comp(b, at + cs * 3, d.format));
            }
            return true;
        }

        static Il2CppStructArray<Vector3> ToIl2(Vector3[] v)
        { var a = new Il2CppStructArray<Vector3>(v.Length); for (int i = 0; i < v.Length; i++) a[i] = v[i]; return a; }
        static Il2CppStructArray<Vector4> ToIl2V4(Vector4[] v)
        { var a = new Il2CppStructArray<Vector4>(v.Length); for (int i = 0; i < v.Length; i++) a[i] = v[i]; return a; }
        static Il2CppStructArray<Vector2> ToIl2V2(Vector2[] v)
        { var a = new Il2CppStructArray<Vector2>(v.Length); for (int i = 0; i < v.Length; i++) a[i] = v[i]; return a; }
        static Il2CppStructArray<Color> ToIl2C(Color[] v)
        { var a = new Il2CppStructArray<Color>(v.Length); for (int i = 0; i < v.Length; i++) a[i] = v[i]; return a; }
    }
}
