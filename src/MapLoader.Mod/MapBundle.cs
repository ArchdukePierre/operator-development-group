// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MapLoader
{

    public static class MapBundle
    {
        public sealed class MaterialDef
        {
            public string Name;
            public string Albedo;
            public string Normal;
            public string Mask;
            public float[] BaseColor;
        }

        public sealed class InstanceDef
        {
            public float px, py, pz;
            public float rx, ry, rz, rw;
            public float sx, sy, sz;
        }

        public sealed class MeshDef
        {
            public string Id;
            public List<MaterialDef> Materials = new List<MaterialDef>();
            public List<InstanceDef> Instances = new List<InstanceDef>();
        }

        public sealed class Manifest
        {
            public string Root;
            public List<MeshDef> Meshes = new List<MeshDef>();
            public int InstanceCount;
        }

        static string ReadStr(BinaryReader r)
        {
            int n = r.ReadUInt16();
            if (n == 0) return null;
            return Encoding.UTF8.GetString(r.ReadBytes(n));
        }

        public static Manifest ReadManifest(string bundleDir)
        {
            string path = Path.Combine(bundleDir, "manifest.bin");
            if (!File.Exists(path))
                throw new FileNotFoundException("manifest.bin missing", path);

            var man = new Manifest { Root = bundleDir };
            using (var fs = File.OpenRead(path))
            using (var r = new BinaryReader(fs))
            {

                string magic = new string(r.ReadChars(4));
                bool v2 = magic == "MMF2";
                if (!v2 && magic != "MMF1")
                    throw new InvalidDataException("bad manifest magic: " + magic);
                int meshCount = r.ReadInt32();
                for (int i = 0; i < meshCount; i++)
                {
                    var md = new MeshDef { Id = ReadStr(r) };
                    int matCount = r.ReadUInt16();
                    for (int m = 0; m < matCount; m++)
                    {
                        var mat = new MaterialDef { Name = ReadStr(r), Albedo = ReadStr(r) };
                        if (v2)
                        {
                            mat.Normal = ReadStr(r);
                            mat.Mask = ReadStr(r);
                        }
                        mat.BaseColor = new[] { r.ReadSingle(), r.ReadSingle(),
                                                r.ReadSingle(), r.ReadSingle() };
                        md.Materials.Add(mat);
                    }
                    int instCount = r.ReadInt32();
                    for (int k = 0; k < instCount; k++)
                    {
                        md.Instances.Add(new InstanceDef
                        {
                            px = r.ReadSingle(), py = r.ReadSingle(), pz = r.ReadSingle(),
                            rx = r.ReadSingle(), ry = r.ReadSingle(),
                            rz = r.ReadSingle(), rw = r.ReadSingle(),
                            sx = r.ReadSingle(), sy = r.ReadSingle(), sz = r.ReadSingle(),
                        });
                    }
                    man.InstanceCount += md.Instances.Count;
                    man.Meshes.Add(md);
                }
            }
            return man;
        }

        public sealed class SubMesh
        {
            public int Start;
            public int Count;
            public string Name;
        }

        public sealed class Geometry
        {
            public float[] Positions;
            public float[] Normals;
            public float[] UVs;
            public int[] Indices;
            public List<SubMesh> SubMeshes = new List<SubMesh>();
            public int VertexCount;
        }

        public static Geometry ReadGeometry(string bundleDir, string meshId)
        {
            string path = Path.Combine(bundleDir, "meshes", meshId + ".mesh");
            using (var fs = File.OpenRead(path))
            using (var r = new BinaryReader(fs))
            {
                if (new string(r.ReadChars(4)) != "MMB1")
                    throw new InvalidDataException("bad mesh magic: " + meshId);
                int vc = r.ReadInt32();
                int ic = r.ReadInt32();
                int sc = r.ReadInt32();

                var g = new Geometry
                {
                    VertexCount = vc,
                    Positions = new float[vc * 3],
                    Normals = new float[vc * 3],
                    UVs = new float[vc * 2],
                    Indices = new int[ic],
                };

                var buf = r.ReadBytes(vc * 3 * 4);
                Buffer.BlockCopy(buf, 0, g.Positions, 0, buf.Length);
                buf = r.ReadBytes(vc * 3 * 4);
                Buffer.BlockCopy(buf, 0, g.Normals, 0, buf.Length);
                buf = r.ReadBytes(vc * 2 * 4);
                Buffer.BlockCopy(buf, 0, g.UVs, 0, buf.Length);
                buf = r.ReadBytes(ic * 4);
                Buffer.BlockCopy(buf, 0, g.Indices, 0, buf.Length);

                for (int i = 0; i < sc; i++)
                {
                    var sm = new SubMesh { Start = r.ReadInt32(), Count = r.ReadInt32() };
                    int n = r.ReadUInt16();
                    sm.Name = n > 0 ? Encoding.UTF8.GetString(r.ReadBytes(n)) : "mat";
                    g.SubMeshes.Add(sm);
                }
                return g;
            }
        }
    }
}
