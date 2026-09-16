// ODG Operations - NewSlots
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace NewSlots
{

    internal static class MeshFile
    {

        static readonly System.Collections.Generic.Dictionary<string, Mesh> _cache
            = new System.Collections.Generic.Dictionary<string, Mesh>();

        internal static Mesh Load(string fileName, string name)
        {
            Mesh hit;
            if (fileName != null && _cache.TryGetValue(fileName, out hit) && hit != null)
            {
                NewSlotsMod.Log("mesh: " + fileName + " -> reused cached build (saves ~1.0 s)");
                return hit;
            }

            byte[] b = Pack.ReadBytes(fileName);
            if (b == null) { NewSlotsMod.Log("mesh: no sealed entry " + fileName); return null; }

            try
            {
                int p = 0;

                if (b.Length < 24 || b[0] != (byte)'O' || b[1] != (byte)'D' || b[2] != (byte)'G' || b[3] != (byte)'M')
                { NewSlotsMod.Log("mesh: bad magic in " + fileName); return null; }
                p = 4;
                int ver = ReadI(b, ref p);
                int vc = ReadI(b, ref p);
                int tc = ReadI(b, ref p);
                int bc = ReadI(b, ref p);
                int flags = ReadI(b, ref p);
                if (ver != 2 || vc <= 0) { NewSlotsMod.Log("mesh: unsupported " + fileName + " v" + ver); return null; }

                var verts = new Il2CppStructArray<Vector3>(vc);
                for (int i = 0; i < vc; i++)
                    verts[i] = new Vector3(ReadF(b, ref p), ReadF(b, ref p), ReadF(b, ref p));

                Il2CppStructArray<Vector3> norms = null;
                if ((flags & 1) != 0)
                {
                    norms = new Il2CppStructArray<Vector3>(vc);
                    for (int i = 0; i < vc; i++)
                        norms[i] = new Vector3(ReadF(b, ref p), ReadF(b, ref p), ReadF(b, ref p));
                }

                Il2CppStructArray<Vector2> uvs = null;
                if ((flags & 2) != 0)
                {
                    uvs = new Il2CppStructArray<Vector2>(vc);
                    for (int i = 0; i < vc; i++)
                        uvs[i] = new Vector2(ReadF(b, ref p), ReadF(b, ref p));
                }

                var tris = new Il2CppStructArray<int>(tc);
                for (int i = 0; i < tc; i++) tris[i] = ReadI(b, ref p);

                Il2CppStructArray<BoneWeight> weights = null;
                if ((flags & 4) != 0)
                {
                    weights = new Il2CppStructArray<BoneWeight>(vc);
                    for (int i = 0; i < vc; i++)
                    {
                        int i0 = ReadI(b, ref p), i1 = ReadI(b, ref p), i2 = ReadI(b, ref p), i3 = ReadI(b, ref p);
                        float w0 = ReadF(b, ref p), w1 = ReadF(b, ref p), w2 = ReadF(b, ref p), w3 = ReadF(b, ref p);
                        var bw = new BoneWeight();
                        bw.boneIndex0 = i0; bw.boneIndex1 = i1; bw.boneIndex2 = i2; bw.boneIndex3 = i3;
                        bw.weight0 = w0; bw.weight1 = w1; bw.weight2 = w2; bw.weight3 = w3;
                        weights[i] = bw;
                    }
                }

                Il2CppStructArray<Matrix4x4> binds = null;
                if (bc > 0)
                {
                    binds = new Il2CppStructArray<Matrix4x4>(bc);
                    for (int i = 0; i < bc; i++)
                    {
                        var m = new Matrix4x4();

                        m.m00 = ReadF(b, ref p); m.m01 = ReadF(b, ref p); m.m02 = ReadF(b, ref p); m.m03 = ReadF(b, ref p);
                        m.m10 = ReadF(b, ref p); m.m11 = ReadF(b, ref p); m.m12 = ReadF(b, ref p); m.m13 = ReadF(b, ref p);
                        m.m20 = ReadF(b, ref p); m.m21 = ReadF(b, ref p); m.m22 = ReadF(b, ref p); m.m23 = ReadF(b, ref p);
                        m.m30 = ReadF(b, ref p); m.m31 = ReadF(b, ref p); m.m32 = ReadF(b, ref p); m.m33 = ReadF(b, ref p);
                        binds[i] = m;
                    }
                }

                var mesh = new Mesh();
                mesh.name = "ODG_Mesh_" + name;

                try { mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; } catch { }
                mesh.vertices = verts;
                if (norms != null) mesh.normals = norms;
                if (uvs != null) mesh.uv = uvs;
                mesh.triangles = tris;
                if (weights != null) mesh.boneWeights = weights;
                if (binds != null) mesh.bindposes = binds;
                mesh.RecalculateBounds();
                if (norms == null) mesh.RecalculateNormals();

                try { mesh.RecalculateTangents(); }
                catch (Exception e) { NewSlotsMod.Log("mesh: tangents failed on " + fileName + ": " + e.Message); }
                Art.Pin(mesh);

                NewSlotsMod.Log("mesh: " + fileName + " -> " + vc + " verts, " + (tc / 3) + " tris, " +
                                bc + " bindposes, skin=" + ((flags & 4) != 0));

                if (fileName != null) _cache[fileName] = mesh;
                return mesh;
            }
            catch (Exception e)
            {
                NewSlotsMod.Log("mesh: " + fileName + " failed: " + e.GetType().Name + ": " + e.Message);
                return null;
            }
        }

        internal sealed class Data
        {
            internal Vector3[] Verts, Norms;
            internal Vector2[] UVs;
            internal int[] Tris;
        }
        static readonly System.Collections.Generic.Dictionary<string, Data> _data =
            new System.Collections.Generic.Dictionary<string, Data>();

        internal static Data LoadData(string fileName)
        {
            Data hit;
            if (fileName != null && _data.TryGetValue(fileName, out hit) && hit != null) return hit;
            byte[] b = Pack.ReadBytes(fileName);
            if (b == null) { NewSlotsMod.Log("mesh: no sealed entry " + fileName); return null; }
            try
            {
                int p = 4;
                if (b.Length < 24 || b[0] != (byte)'O' || b[1] != (byte)'D' || b[2] != (byte)'G' || b[3] != (byte)'M')
                { NewSlotsMod.Log("mesh: bad magic in " + fileName); return null; }
                int ver = ReadI(b, ref p), vc = ReadI(b, ref p), tc = ReadI(b, ref p), bc = ReadI(b, ref p), flags = ReadI(b, ref p);
                if (ver != 2 || vc <= 0) { NewSlotsMod.Log("mesh: unsupported " + fileName + " v" + ver); return null; }
                var d = new Data { Verts = new Vector3[vc], Tris = new int[tc] };
                for (int i = 0; i < vc; i++) d.Verts[i] = new Vector3(ReadF(b, ref p), ReadF(b, ref p), ReadF(b, ref p));
                if ((flags & 1) != 0)
                {
                    d.Norms = new Vector3[vc];
                    for (int i = 0; i < vc; i++) d.Norms[i] = new Vector3(ReadF(b, ref p), ReadF(b, ref p), ReadF(b, ref p));
                }
                if ((flags & 2) != 0)
                {
                    d.UVs = new Vector2[vc];
                    for (int i = 0; i < vc; i++) d.UVs[i] = new Vector2(ReadF(b, ref p), ReadF(b, ref p));
                }
                for (int i = 0; i < tc; i++) d.Tris[i] = ReadI(b, ref p);
                NewSlotsMod.Log("mesh: " + fileName + " -> " + vc + " verts, " + (tc / 3) + " tris (data, deformable)");
                if (fileName != null) _data[fileName] = d;
                return d;
            }
            catch (Exception e)
            {
                NewSlotsMod.Log("mesh: " + fileName + " data failed: " + e.GetType().Name + ": " + e.Message);
                return null;
            }
        }

        internal static Mesh Build(Data d, string name)
        {
            try
            {
                int vc = d.Verts.Length;
                var verts = new Il2CppStructArray<Vector3>(vc);
                for (int i = 0; i < vc; i++) verts[i] = d.Verts[i];
                var tris = new Il2CppStructArray<int>(d.Tris.Length);
                for (int i = 0; i < d.Tris.Length; i++) tris[i] = d.Tris[i];
                var mesh = new Mesh();
                mesh.name = "ODG_Patch_" + name;
                try { mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; } catch { }
                try { mesh.MarkDynamic(); } catch { }
                mesh.vertices = verts;
                if (d.Norms != null)
                {
                    var n = new Il2CppStructArray<Vector3>(vc);
                    for (int i = 0; i < vc; i++) n[i] = d.Norms[i];
                    mesh.normals = n;
                }
                if (d.UVs != null)
                {
                    var uv = new Il2CppStructArray<Vector2>(vc);
                    for (int i = 0; i < vc; i++) uv[i] = d.UVs[i];
                    mesh.uv = uv;
                }
                mesh.triangles = tris;
                mesh.RecalculateBounds();
                if (d.Norms == null) mesh.RecalculateNormals();
                try { mesh.RecalculateTangents(); } catch { }
                return mesh;
            }
            catch (Exception e)
            {
                NewSlotsMod.Log("mesh: build for " + name + " failed: " + e.GetType().Name + ": " + e.Message);
                return null;
            }
        }

        static int ReadI(byte[] b, ref int p)
        {
            int v = b[p] | (b[p + 1] << 8) | (b[p + 2] << 16) | (b[p + 3] << 24);
            p += 4;
            return v;
        }

        static float ReadF(byte[] b, ref int p)
        {
            float v = BitConverter.ToSingle(b, p);
            p += 4;
            return v;
        }
    }
}
