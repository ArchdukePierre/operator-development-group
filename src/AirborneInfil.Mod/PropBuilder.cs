// ODG Operations - AirborneInfil
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MelonLoader;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace AirborneInfil
{

    internal sealed class PropMaterial
    {
        public string BaseMap;
        public string NormalMap;
        public bool DoubleSided;
        public Color Fallback = new Color(0.34f, 0.36f, 0.32f);
        public float Metallic = 0.0f;
        public float Smoothness = 0.25f;
    }

    internal static class PropBuilder
    {

        internal static GameObject Build(string name, Transform holder, string objPath, string texDir,
                                         PropMaterial pm, float scale, bool mirrorX, bool doubleSidedMesh, MelonLogger.Instance log)
        {
            if (!File.Exists(objPath)) { log.Warning("[PropBuilder] OBJ missing: " + objPath); return null; }

            var v = new List<Vector3>(); var vt = new List<Vector2>(); var vn = new List<Vector3>();
            var faces = new List<int[]>();

            foreach (var raw in File.ReadLines(objPath))
            {
                var line = raw.Trim();
                if (line.Length < 2) continue;
                var tok = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                switch (tok[0])
                {
                    case "v": if (tok.Length >= 4) v.Add(new Vector3(P(tok[1]), P(tok[2]), P(tok[3]))); break;
                    case "vt": if (tok.Length >= 3) vt.Add(new Vector2(P(tok[1]), P(tok[2]))); break;
                    case "vn": if (tok.Length >= 4) vn.Add(new Vector3(P(tok[1]), P(tok[2]), P(tok[3]))); break;
                    case "f":
                        if (tok.Length < 4) break;
                        var corners = new int[tok.Length - 1][];
                        for (int i = 1; i < tok.Length; i++)
                        {
                            var idx = tok[i].Split('/');
                            corners[i - 1] = new int[3]
                            {
                                Fix(idx[0], v.Count),
                                idx.Length > 1 && idx[1] != "" ? Fix(idx[1], vt.Count) : -1,
                                idx.Length > 2 && idx[2] != "" ? Fix(idx[2], vn.Count) : -1
                            };
                        }
                        for (int i = 2; i < corners.Length; i++)
                            faces.Add(new int[9] { corners[0][0], corners[0][1], corners[0][2],
                                                   corners[i-1][0], corners[i-1][1], corners[i-1][2],
                                                   corners[i][0], corners[i][1], corners[i][2] });
                        break;
                }
            }

            if (faces.Count == 0) { log.Warning("[PropBuilder] no faces in " + objPath); return null; }

            var map = new Dictionary<(int, int, int), int>(faces.Count * 2);
            var lv = new List<Vector3>(); var lu = new List<Vector2>(); var ln = new List<Vector3>();
            var tris = new List<int>(faces.Count * 3);
            foreach (var f in faces)
                for (int c = 0; c < 3; c++)
                {
                    var key = (f[c * 3], f[c * 3 + 1], f[c * 3 + 2]);
                    if (key.Item1 < 0 || key.Item1 >= v.Count) continue;
                    if (!map.TryGetValue(key, out int li))
                    {
                        li = lv.Count; map[key] = li;
                        lv.Add(v[key.Item1] * scale);
                        lu.Add(key.Item2 >= 0 && key.Item2 < vt.Count ? vt[key.Item2] : Vector2.zero);
                        ln.Add(key.Item3 >= 0 && key.Item3 < vn.Count ? vn[key.Item3] : Vector3.up);
                    }
                    tris.Add(li);
                }

            if (doubleSidedMesh)
            {
                int baseN = lv.Count;
                for (int i = 0; i < baseN; i++) { lv.Add(lv[i]); lu.Add(lu[i]); ln.Add(-ln[i]); }
                int triN = tris.Count;
                for (int i = 0; i < triN; i += 3) { tris.Add(tris[i] + baseN); tris.Add(tris[i + 2] + baseN); tris.Add(tris[i + 1] + baseN); }
            }

            var mesh = new Mesh { name = name + "_mesh" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = ToV3(lv);
            mesh.uv = ToV2(lu);
            mesh.normals = ToV3(ln);
            mesh.triangles = ToI(tris);
            mesh.RecalculateBounds();

            var root = new GameObject(name);
            root.transform.SetParent(holder, false);
            var go = new GameObject(name + "_mesh");
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = MakeMaterial(name, texDir, pm, log);

            if (mirrorX) root.transform.localScale = new Vector3(-1f, 1f, 1f);

            log.Msg("[PropBuilder] {0}: {1} verts, {2} tris, scale {3}", name, lv.Count, tris.Count / 3, scale);
            return root;
        }

        internal static GameObject BuildFromManifest(string name, Transform holder, string partsDir, MelonLogger.Instance log)
        {
            var mf = Path.Combine(partsDir, "manifest.txt");
            if (!File.Exists(mf)) return null;
            var root = new GameObject(name);
            root.transform.SetParent(holder, false);
            int built = 0;
            foreach (var raw in File.ReadAllLines(mf))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                var bar = line.IndexOf('|');
                string objf = bar >= 0 ? line.Substring(0, bar) : line;
                string texf = bar >= 0 ? line.Substring(bar + 1) : "";
                var pm = new PropMaterial
                {
                    BaseMap = texf.Length > 0 ? texf : null,
                    DoubleSided = true,
                    Fallback = new Color(0.42f, 0.44f, 0.42f),
                    Smoothness = 0.3f
                };
                var part = Build(name + "_" + Path.GetFileNameWithoutExtension(objf), root.transform,
                                 Path.Combine(partsDir, objf), partsDir, pm, 1f, false, false, log);
                if (part != null) built++;
            }
            if (built == 0) { UnityEngine.Object.Destroy(root); return null; }
            log.Msg("[PropBuilder] {0}: {1} manifest parts assembled.", name, built);
            return root;
        }

        static Material MakeMaterial(string name, string texDir, PropMaterial pm, MelonLogger.Instance log)
        {
            var shader = Shader.Find("HDRP/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var m = new Material(shader) { name = name + "_mat" };
            bool gotBase = false;
            try
            {
                if (pm.BaseMap != null)
                {
                    var t = LoadTex(Path.Combine(texDir, pm.BaseMap), log);
                    if (t != null) { m.SetTexture("_BaseColorMap", t); m.SetTexture("_MainTex", t); gotBase = true; }
                }
                if (pm.NormalMap != null)
                {
                    var n = LoadTex(Path.Combine(texDir, pm.NormalMap), log);
                    if (n != null) { m.SetTexture("_NormalMap", n); m.EnableKeyword("_NORMALMAP"); }
                }
            }
            catch (Exception e) { log.Warning("[PropBuilder] tex load failed: " + e.Message); }

            if (!gotBase)
            {
                try { m.SetColor("_BaseColor", pm.Fallback); m.SetColor("_Color", pm.Fallback); } catch { }
            }
            try { m.SetFloat("_Metallic", pm.Metallic); m.SetFloat("_Smoothness", pm.Smoothness); } catch { }

            if (pm.DoubleSided)
            {
                try { m.SetFloat("_DoubleSidedEnable", 1f); m.EnableKeyword("_DOUBLESIDED_ON"); } catch { }
                try { m.SetFloat("_CullMode", 0f); m.SetFloat("_CullModeForward", 0f); } catch { }
                try { m.SetFloat("_DoubleSidedNormalMode", 1f); } catch { }
            }
            return m;
        }

        static Texture2D LoadTex(string path, MelonLogger.Instance log)
        {
            if (!File.Exists(path)) { log.Warning("[PropBuilder] tex missing: " + Path.GetFileName(path)); return null; }
            var bytes = File.ReadAllBytes(path);
            var buf = new Il2CppStructArray<byte>(bytes.Length);
            for (int i = 0; i < bytes.Length; i++) buf[i] = bytes[i];
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            if (!ImageConversion.LoadImage(tex, buf)) { log.Warning("[PropBuilder] LoadImage failed: " + Path.GetFileName(path)); return null; }
            return tex;
        }

        static float P(string s) => float.Parse(s, CultureInfo.InvariantCulture);
        static int Fix(string s, int count) { int i = int.Parse(s, CultureInfo.InvariantCulture); return i > 0 ? i - 1 : count + i; }
        static Il2CppStructArray<Vector3> ToV3(List<Vector3> s) { var a = new Il2CppStructArray<Vector3>(s.Count); for (int i = 0; i < s.Count; i++) a[i] = s[i]; return a; }
        static Il2CppStructArray<Vector2> ToV2(List<Vector2> s) { var a = new Il2CppStructArray<Vector2>(s.Count); for (int i = 0; i < s.Count; i++) a[i] = s[i]; return a; }
        static Il2CppStructArray<int> ToI(List<int> s) { var a = new Il2CppStructArray<int>(s.Count); for (int i = 0; i < s.Count; i++) a[i] = s[i]; return a; }
    }
}
