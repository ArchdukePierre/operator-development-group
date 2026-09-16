// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MelonLoader;
using UnityEngine;

namespace MapLoader
{

    public static class VoidPatches
    {
        static MelonLogger.Instance _log;
        public static void Init(MelonLogger.Instance log) { _log = log; }

        const string FileName = "patches.json";

        public static int QuadCount { get; private set; }

        static GameObject _root;
        static readonly List<Mesh> _meshes = new List<Mesh>(32);
        static Material _fallbackMat;

        struct Quad { public float x, z, y, width, depth; }

        public static void Build(string mapDir, Transform mapRoot)
        {
            QuadCount = 0;
            if (mapRoot == null) return;
            string path = null;
            try
            {
                path = Path.Combine(mapDir ?? "", FileName);
                if (!File.Exists(path)) return;
            }
            catch { return; }

            try
            {
                var quads = Parse(File.ReadAllText(path));
                if (quads.Count == 0)
                {
                    _log?.Warning("[voids] " + FileName + " parsed to zero quads -- the "
                                + "holes are still open. The file is present, so this is "
                                + "a parse failure, not missing data.");
                    return;
                }

                Material mat = null;
                string matFrom = null;
                string[] wantMesh = { "SM_MERGED_Road_Large", "Road_Large", "Road_Small",
                                      "SM_MERGED_Sidewalk", "Sidewalk_Large", "Sidewalk_Small",
                                      "TemplateFloor" };
                for (int i = 0; i < wantMesh.Length && mat == null; i++)
                {
                    mat = MapRuntime.FindBuiltMaterialForMesh(wantMesh[i]);
                    if (mat != null) matFrom = "mesh '" + wantMesh[i] + "'";
                }

                string[] want = { "asphalt", "road", "street", "tarmac", "sidewalk",
                                  "pavement", "concrete" };
                for (int i = 0; i < want.Length && mat == null; i++)
                {
                    mat = MapRuntime.FindBuiltMaterial(want[i]);
                    if (mat != null) matFrom = "name '" + want[i] + "'";
                }
                if (mat != null)
                    _log?.Msg("[voids] patches borrow the bundle's street material via "
                            + matFrom + " (" + mat.name + "), so a repair looks like the "
                            + "street it repairs rather than a blank slab.");
                if (mat == null)
                {

                    var sh = MeshFactory.GetShader();
                    if (sh == null)
                    {
                        _log?.Warning("[voids] no shader; the patches cannot be built");
                        return;
                    }
                    _fallbackMat = new Material(sh) { name = "voidpatch_fallback" };
                    try
                    {
                        if (_fallbackMat.HasProperty("_BaseColor"))
                            _fallbackMat.SetColor("_BaseColor", new Color(0.085f, 0.085f, 0.09f, 1f));
                        if (_fallbackMat.HasProperty("_Color"))

                            _fallbackMat.SetColor("_Color", new Color(0.085f, 0.085f, 0.09f, 1f));
                        if (_fallbackMat.HasProperty("_Smoothness"))
                            _fallbackMat.SetFloat("_Smoothness", 0.12f);
                        if (_fallbackMat.HasProperty("_SmoothnessRemapMax"))
                            _fallbackMat.SetFloat("_SmoothnessRemapMax", 0.12f);
                    }
                    catch { }
                    mat = _fallbackMat;
                    _log?.Warning("[voids] no street-floor mesh and no word-boundary "
                                + "name match in the built set; the patches wear dark "
                                + "asphalt-grey instead of the bundle's own pavement");
                }

                _root = new GameObject("VOIDPATCHES");

                _root.transform.SetParent(mapRoot, false);

                int built = 0;
                for (int i = 0; i < quads.Count; i++)
                {
                    if (BuildQuad(quads[i], mat, i)) built++;
                }
                QuadCount = built;
                _log?.Msg("[voids] " + built + " floor patch(es) laid over the scanned "
                        + "street voids (" + quads.Count + " authored). Each is a real "
                        + "collider two centimetres under the lowest real floor it "
                        + "touches and tucked a metre under the surrounding ground, "
                        + "so the streets stop having places to fall out of the world.");
            }
            catch (Exception e) { _log?.Warning("[voids] build: " + e.Message); }
        }

        static bool BuildQuad(Quad q, Material mat, int index)
        {
            try
            {
                var go = new GameObject("PATCH_" + index);
                var t = go.transform;
                t.SetParent(_root.transform, false);
                t.localPosition = new Vector3(q.x, q.y, q.z);

                var mesh = SizedQuad(q.width, q.depth);
                if (mesh == null) return false;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;

                try
                {
                    r.lightProbeUsage = MapIdentity.OwnSky
                        ? UnityEngine.Rendering.LightProbeUsage.BlendProbes
                        : UnityEngine.Rendering.LightProbeUsage.Off;
                }
                catch { }
                try { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; } catch { }
                try { r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion; } catch { }
                try { r.staticShadowCaster = true; } catch { }

                try { go.AddComponent<MeshCollider>().sharedMesh = mesh; } catch { }

                return true;
            }
            catch (Exception e)
            {
                _log?.Warning("[voids] patch " + index + ": " + e.Message);
                return false;
            }
        }

        static Mesh SizedQuad(float width, float depth)
        {
            try
            {
                float hw = width * 0.5f, hd = depth * 0.5f;
                float uw = width * 0.5f, ud = depth * 0.5f;
                var m = new Mesh { name = "voidpatch_quad" };
                var v = new Vector3[]
                {
                    new Vector3(-hw, 0f, -hd),
                    new Vector3( hw, 0f, -hd),
                    new Vector3( hw, 0f,  hd),
                    new Vector3(-hw, 0f,  hd),
                };
                var n = new Vector3[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
                var uv = new Vector2[]
                {
                    new Vector2(0f, 0f), new Vector2(uw, 0f),
                    new Vector2(uw, ud), new Vector2(0f, ud),
                };

                var tris = new int[] { 0, 3, 2, 0, 2, 1 };
                Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3> vv = v;
                Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3> nn = n;
                Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector2> uu = uv;
                Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> tt = tris;
                m.vertices = vv;
                m.normals = nn;
                m.uv = uu;
                m.triangles = tt;
                m.RecalculateTangents();
                m.RecalculateBounds();
                _meshes.Add(m);
                return m;
            }
            catch (Exception e)
            {
                _log?.Warning("[voids] quad mesh: " + e.Message);
                return null;
            }
        }

        static List<Quad> Parse(string body)
        {
            var list = new List<Quad>(32);
            if (string.IsNullOrEmpty(body)) return list;
            int i = 0;
            while (true)
            {
                int open = body.IndexOf('{', i);
                if (open < 0) break;
                int close = body.IndexOf('}', open + 1);
                if (close < 0) break;

                int inner = body.IndexOf('{', open + 1);
                if (inner >= 0 && inner < close) { i = inner; continue; }

                string block = body.Substring(open, close - open + 1);
                if (block.IndexOf("\"width\"", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var q = new Quad();
                    bool ok = ReadNum(block, "x", out q.x)
                           && ReadNum(block, "z", out q.z)
                           && ReadNum(block, "y", out q.y)
                           && ReadNum(block, "width", out q.width)
                           && ReadNum(block, "depth", out q.depth);
                    if (ok && q.width > 0.01f && q.depth > 0.01f) list.Add(q);
                }
                i = close + 1;
            }
            return list;
        }

        static bool ReadNum(string body, string key, out float v)
        {
            v = 0f;
            int i = body.IndexOf("\"" + key + "\"", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return false;
            int c = body.IndexOf(':', i);
            if (c < 0) return false;
            int e = c + 1;
            while (e < body.Length && body[e] != ',' && body[e] != '}') e++;
            return float.TryParse(body.Substring(c + 1, e - c - 1).Trim(),
                NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        public static void Forget()
        {
            try
            {
                for (int i = 0; i < _meshes.Count; i++)
                    if (_meshes[i] != null) UnityEngine.Object.Destroy(_meshes[i]);
            }
            catch { }
            _meshes.Clear();
            try { if (_fallbackMat != null) UnityEngine.Object.Destroy(_fallbackMat); } catch { }
            _fallbackMat = null;
            _root = null;
            QuadCount = 0;
        }
    }
}
