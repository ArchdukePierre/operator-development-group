// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

namespace MapLoader
{

    public static class MeshFactory
    {
        static readonly Dictionary<string, Texture2D> _texCache =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        static readonly Dictionary<string, bool> _cutoutTex =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        public static float FoliageMipBiasOverride = -1f;

        static readonly List<Material> _cutoutMats = new List<Material>();

        public static float FoliageCutoffOverride = -1f;
        public static float FoliageCutoffNow
        {
            get { return FoliageCutoffOverride >= 0f ? FoliageCutoffOverride : 0.33f; }
        }
        public static int CutoutMaterialCount => _cutoutMats.Count;

        public static string SetFoliageCutoff(float c)
        {
            c = Mathf.Clamp(c, 0.05f, 0.95f);
            FoliageCutoffOverride = c;
            TuneStore.Save("FoliageCutoff", c);
            int n = 0;
            for (int i = 0; i < _cutoutMats.Count; i++)
            {
                var m = _cutoutMats[i];
                if (m == null) continue;
                try
                {
                    SetIf(m, "_AlphaCutoff", c);
                    SetIf(m, "_Cutoff", c);
                    SetIf(m, "_AlphaCutoffShadow", c);
                    n++;
                }
                catch { }
            }
            return "alpha cutoff " + c.ToString("F2") + " on " + n + " cutout material(s), live and "
                 + "kept for this map (shipped 0.33). Lower = thicker leaves and blades.";
        }

        public static string SetFoliageNormal(bool on)
        {
            int n = 0;
            for (int i = 0; i < _cutoutMats.Count; i++)
            {
                var m = _cutoutMats[i];
                if (m == null) continue;
                try
                {
                    if (!m.HasProperty("_NormalMap") || m.GetTexture("_NormalMap") == null) continue;
                    if (on) m.EnableKeyword("_NORMALMAP"); else m.DisableKeyword("_NORMALMAP");
                    n++;
                }
                catch { }
            }
            return "normal maps " + (on ? "ON" : "OFF") + " on " + n + " cutout material(s), live "
                 + "(not kept: this is a diagnostic). If the grain went with them, it is shading, "
                 + "not the alpha edge.";
        }
        public static float FoliageMipBiasNow
        {
            get { return FoliageMipBiasOverride >= 0f ? FoliageMipBiasOverride : MapIdentity.FoliageMipBias; }
        }

        public static string SetFoliageMipBias(float bias)
        {
            bias = Mathf.Clamp(bias, 0f, 4f);
            FoliageMipBiasOverride = bias;
            TuneStore.Save("FoliageMipBias", bias);
            int n = 0, dead = 0;
            foreach (var kv in _texCache)
            {
                if (kv.Key.EndsWith("|lin", StringComparison.Ordinal)) continue;
                bool cut;
                if (!_cutoutTex.TryGetValue(kv.Key, out cut) || !cut) continue;
                var t = kv.Value;
                if (t == null) { dead++; continue; }
                try { t.mipMapBias = bias; n++; } catch { dead++; }
            }
            return "cutout foliage mip bias +" + bias.ToString("F2") + " on " + n + " texture(s)"
                 + (dead > 0 ? " (" + dead + " unreachable)" : "")
                 + ", live and kept for this map. Higher steadies the edge crawl, lower "
                 + "keeps range crisp; Villa settled at 2.0, Trenches at 0.5.";
        }
        static Shader _shader;

        static readonly Dictionary<string, string> _blend =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static string _blendDir;
        static int _bound, _failed, _normals, _masks, _cutouts, _glass;

        static readonly System.Collections.Generic.List<string> _glassNames =
            new System.Collections.Generic.List<string>();
        public static string GlassList()
        {
            try
            {
                if (_glassNames.Count == 0) return "no material on this map was classified as glass.";
                return _glassNames.Count + " material(s) drawn as glass: " + string.Join(", ", _glassNames.ToArray());
            }
            catch { return "unreadable"; }
        }
        static bool _cutoutLogged;
        static bool _mipOptOutLogged;
        static bool _mipBiasLogged;
        static bool _noSpecLogged;

        static readonly string[] LeafyWords =
        { "leaf", "leaves", "needle", "branch", "pine_leaves", "grass", "fern",
          "clover", "bush", "shrub", "scrub", "undergrowth", "canopy", "frond" };

        public static bool IsLeafy(string n)
        {
            if (string.IsNullOrEmpty(n)) return false;
            var lo = n.ToLowerInvariant();
            for (int i = 0; i < LeafyWords.Length; i++)
                if (lo.Contains(LeafyWords[i])) return true;
            return false;
        }

        public static int TextureCount => _texCache.Count;
        public static int BoundCount => _bound;
        public static int FailedCount => _failed;
        public static int NormalCount => _normals;
        public static int MaskCount => _masks;

        public static long DecodeMs, CompressMs, MeshBuildMs, MaterialMs;

        public static long TexBytesCompressed, TexBytesRaw;
        public static int TexCountCompressed, TexCountRaw;

        static long TexBytes(int w, int h, float bpp)
        {
            return (long)(w * (double)h * bpp * 1.34);
        }

        public static void ResetCaches()
        {
            _texCache.Clear();

            _cutoutTex.Clear();
            _shader = null;
            _bound = 0;
            _failed = 0;
            _normals = 0;
            _masks = 0;
            _cutouts = 0;
            _glass = 0; _glassNames.Clear();
            _blend.Clear();
            _blendDir = null;
            _constants.Clear();
            _constantsDir = null;
            ConstantsApplied = 0;
            _emissive.Clear();
            _emissiveDir = null;
            EmissiveApplied = 0;
            _cutoutLogged = false;

            _cutoutRejected = 0;
            SkippedSubmeshes = 0;
            GroundDarkened = 0;
            DecodeMs = 0; CompressMs = 0; MeshBuildMs = 0; MaterialMs = 0;
            TexBytesCompressed = 0; TexBytesRaw = 0;
            TexCountCompressed = 0; TexCountRaw = 0;
        }

        public static void DestroyTextures()
        {
            try
            {
                foreach (var t in _texCache.Values)
                    if (t != null) UnityEngine.Object.Destroy(t);
            }
            catch { }
        }

        public static int CutoutCount => _cutouts;

        public static int CutoutRejected => _cutoutRejected;

        static readonly string[] GlassDeny =
        {
            "Fiberglass",
            "_OPAC",
        };
        static readonly string[] GlassAllow = { "Glass", "Window", "Windshield", "Mirror" };

        static readonly string[] SolidMeshNouns =
        {
            "Handle", "Frame", "Hinge", "Sill", "Shutter", "Latch",
            "Box", "Crate", "Locker", "Wall", "Floor", "Beam", "Pipe",
            "Rail", "Stair", "Deck", "Plate", "Support", "Bracket",
        };

        static bool MeshLooksStructural(string meshId)
        {
            if (string.IsNullOrEmpty(meshId)) return false;
            for (int i = 0; i < SolidMeshNouns.Length; i++)
                if (meshId.IndexOf(SolidMeshNouns[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {

                    if (meshId.IndexOf("Window", StringComparison.OrdinalIgnoreCase) >= 0
                        || meshId.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0
                        || meshId.IndexOf("Pane", StringComparison.OrdinalIgnoreCase) >= 0)
                        return false;
                    return true;
                }
            return false;
        }

        public static bool IsGlassMaterial(string matName)
        {
            if (string.IsNullOrEmpty(matName)) return false;
            for (int i = 0; i < GlassDeny.Length; i++)
                if (matName.IndexOf(GlassDeny[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
            for (int i = 0; i < GlassAllow.Length; i++)
                if (matName.IndexOf(GlassAllow[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        static readonly string[] WaterAllow = { "MI_Ocean", "M_Ocean", "_Ocean_" };

        public static bool IsWaterMaterial(string matName)
        {
            if (string.IsNullOrEmpty(matName)) return false;
            for (int i = 0; i < WaterAllow.Length; i++)
                if (matName.IndexOf(WaterAllow[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        static readonly string[] WaterMeshAllow =
        {
            "SM_CRK_River_",
            "SM_River_Plane",
            "S_CreekWaterPlane",

            "SM_ST_Vista_02",
            "SM_ST_Vista_03",
            "SM_ST_Vista_04",
            "SM_ST_Vista_05",
        };

        public static Color WaterTint = new Color(0.055f, 0.075f, 0.065f, 1f);

        public static bool IsWaterMesh(string meshId)
        {
            if (string.IsNullOrEmpty(meshId)) return false;
            for (int i = 0; i < WaterMeshAllow.Length; i++)
                if (meshId.IndexOf(WaterMeshAllow[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        public static bool GlassTransparent = true;

        public static float GlassAlpha = 0.55f;

        public static int GlassCount => _glass;

        public static void EnsureBlendTable(string bundleDir)
        {
            if (_blendDir == bundleDir) return;
            _blendDir = bundleDir;
            _blend.Clear();
            try
            {
                string p = Path.Combine(bundleDir, "material_blend.txt");
                if (!File.Exists(p)) return;
                foreach (var line in File.ReadAllLines(p))
                {
                    if (line.Length == 0 || line[0] == '#') continue;
                    int t = line.IndexOf('\t');
                    if (t <= 0) continue;
                    _blend[line.Substring(0, t)] = line.Substring(t + 1).Trim();
                }
                MelonLogger.Msg("[MapLoader] blend table: " + _blend.Count
                              + " authored material blend mode(s)");
            }
            catch { _blend.Clear(); }

            _decalsOff.Clear();
            try
            {
                string p2 = Path.Combine(bundleDir, "material_decals.txt");
                if (File.Exists(p2))
                {
                    foreach (var line in File.ReadAllLines(p2))
                    {
                        if (line.Length == 0 || line[0] == '#') continue;
                        int t2 = line.IndexOf('\t');
                        if (t2 <= 0) continue;
                        if (line.Substring(t2 + 1).Trim().Equals("OFF", StringComparison.OrdinalIgnoreCase))
                            _decalsOff.Add(line.Substring(0, t2));
                    }
                    if (_decalsOff.Count > 0)
                        MelonLogger.Msg("[MapLoader] decal table: " + _decalsOff.Count
                                      + " material(s) opt out of receiving decals");
                }
            }
            catch { _decalsOff.Clear(); }
        }

        static readonly System.Collections.Generic.HashSet<string> _decalsOff =
            new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static bool BlendAllowsCutout(string matName)
        {
            if (string.IsNullOrEmpty(matName) || _blend.Count == 0) return true;
            string b;
            if (!_blend.TryGetValue(matName, out b)) return true;
            return b != "BLEND_Opaque";
        }

        public static bool BlendForcesCutout(string matName)
        {
            if (string.IsNullOrEmpty(matName) || _blend.Count == 0) return false;
            string b;
            return _blend.TryGetValue(matName, out b) && b == "BLEND_Cutout";
        }

        static readonly Dictionary<string, float[]> _constants =
            new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
        static string _constantsDir;
        public static int ConstantsApplied;

        public static void EnsureConstantsTable(string bundleDir)
        {
            if (_constantsDir == bundleDir) return;
            _constantsDir = bundleDir;
            _constants.Clear();
            try
            {
                string p = Path.Combine(bundleDir, "material_constants.json");
                if (!File.Exists(p)) return;
                string body = File.ReadAllText(p);

                int i = 0, depth = 0;
                while (i < body.Length)
                {
                    char ch = body[i];
                    if (ch == '{') { depth++; i++; continue; }
                    if (ch == '}') { depth--; i++; continue; }
                    if (ch == '"' && depth == 1)
                    {
                        int end = body.IndexOf('"', i + 1);
                        if (end < 0) break;
                        string key = body.Substring(i + 1, end - i - 1);
                        int colon = body.IndexOf(':', end);
                        if (colon < 0) break;
                        int j = colon + 1;
                        while (j < body.Length && char.IsWhiteSpace(body[j])) j++;
                        if (j < body.Length && body[j] == '{')
                        {
                            int close = body.IndexOf('}', j);
                            if (close < 0) break;
                            if (key.Length > 0 && key[0] != '_')
                            {
                                var block = body.Substring(j, close - j + 1);
                                float r, g, b, met, sm;
                                if (ReadTriplet(block, "baseColor", out r, out g, out b)
                                    && ReadFloat(block, "metallic", out met)
                                    && ReadFloat(block, "smoothness", out sm))
                                    _constants[key] = new[] { r, g, b, met, sm };
                            }
                            i = close + 1;
                            continue;
                        }
                        i = end + 1;
                        continue;
                    }
                    i++;
                }
                if (_constants.Count > 0)
                    MelonLogger.Msg("[MapLoader] material constants: " + _constants.Count
                                  + " authored no-art material(s)");
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[MapLoader] material_constants.json: " + e.Message);
                _constants.Clear();
            }
        }

        static bool ReadFloat(string body, string key, out float v)
        {
            v = 0f;
            int i = body.IndexOf("\"" + key + "\"", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return false;
            int c = body.IndexOf(':', i);
            if (c < 0) return false;
            int e = c + 1;
            while (e < body.Length && body[e] != ',' && body[e] != '}' && body[e] != ']') e++;
            return float.TryParse(body.Substring(c + 1, e - c - 1).Trim(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out v);
        }

        static bool ReadTriplet(string body, string key, out float a, out float b, out float c)
        {
            a = b = c = 0f;
            int i = body.IndexOf("\"" + key + "\"", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return false;
            int open = body.IndexOf('[', i);
            int close = open >= 0 ? body.IndexOf(']', open) : -1;
            if (close < 0) return false;
            var parts = body.Substring(open + 1, close - open - 1).Split(',');
            if (parts.Length < 3) return false;
            return float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture, out a)
                && float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture, out b)
                && float.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture, out c);
        }

        static readonly Dictionary<string, Color> _emissive =
            new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        static string _emissiveDir;
        public static int EmissiveApplied;

        public static void EnsureEmissiveTable(string bundleDir)
        {
            if (_emissiveDir == bundleDir) return;
            _emissiveDir = bundleDir;
            _emissive.Clear();
            try
            {
                string p = Path.Combine(bundleDir, "emissive.txt");
                if (!File.Exists(p)) return;
                foreach (var line in File.ReadAllLines(p))
                {
                    if (line.Length == 0 || line[0] == '#') continue;
                    var cols = line.Split('\t');
                    if (cols.Length < 3) continue;
                    var rgb = cols[1].Split(',');
                    float r, g, b, nits;
                    if (rgb.Length < 3) continue;
                    if (!float.TryParse(rgb[0].Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out r)) continue;
                    if (!float.TryParse(rgb[1].Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out g)) continue;
                    if (!float.TryParse(rgb[2].Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out b)) continue;
                    if (!float.TryParse(cols[2].Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out nits)) continue;

                    _emissive[cols[0].Trim()] = new Color(
                        Mathf.GammaToLinearSpace(r) * nits,
                        Mathf.GammaToLinearSpace(g) * nits,
                        Mathf.GammaToLinearSpace(b) * nits, 1f);
                }
                if (_emissive.Count > 0)
                    MelonLogger.Msg("[MapLoader] emissive table: " + _emissive.Count
                                  + " fixture lens material(s)");
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[MapLoader] emissive.txt: " + e.Message);
                _emissive.Clear();
            }
        }

        public static float SmoothnessCeiling = 0.72f;

        const float FarmsteadSmoothnessCeiling = 0.60f;
        static bool CityMaterials { get { try { return MapIdentity.OwnSky; } catch { return true; } } }

        static int _cutoutRejected;
        static Texture2D _detailTex;

        public static float DetailTile = 12f;

        public static float TerrainDetailTile = 220f;
        public static bool DetailTileOverridden;

        public static int RetileDetail(float tile)
        {
            DetailTile = tile;
            DetailTileOverridden = true;
            int n = 0;
            try
            {
                var root = MapRuntime.Root;
                if (root == null) return 0;
                var rs = root.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rs.Length; i++)
                {
                    if (rs[i] == null) continue;
                    var mats = rs[i].sharedMaterials;
                    for (int k = 0; k < (mats != null ? mats.Length : 0); k++)
                    {
                        var m = mats[k];
                        if (m == null || !m.HasProperty("_DetailMap")) continue;
                        try
                        {
                            if (tile <= 0f) m.SetTexture("_DetailMap", null);
                            else m.SetTextureScale("_DetailMap", new Vector2(tile, tile));
                            n++;
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return n;
        }

        static float Hash2(float ix, float iy, int period)
        {
            ix = ix - Mathf.Floor(ix / period) * period;
            iy = iy - Mathf.Floor(iy / period) * period;
            float v = Mathf.Sin(ix * 127.1f + iy * 311.7f) * 43758.5453f;
            return v - Mathf.Floor(v);
        }

        static float VNoise(float x, float y, int freq)
        {
            float fx = x * freq, fy = y * freq;
            float ix = Mathf.Floor(fx), iy = Mathf.Floor(fy);
            float tx = fx - ix, ty = fy - iy;
            tx = tx * tx * (3f - 2f * tx); ty = ty * ty * (3f - 2f * ty);
            float a = Hash2(ix, iy, freq),     b = Hash2(ix + 1, iy, freq);
            float c = Hash2(ix, iy + 1, freq), d = Hash2(ix + 1, iy + 1, freq);
            return (a * (1 - tx) + b * tx) * (1 - ty) + (c * (1 - tx) + d * tx) * ty;
        }

        const int DetailSize = 512;

        static Texture2D DetailTex()
        {
            if (_detailTex != null) return _detailTex;
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var h = new float[DetailSize * DetailSize];
                float lo = float.MaxValue, hi = float.MinValue;
                for (int y = 0; y < DetailSize; y++)
                {
                    float v = (y + 0.5f) / DetailSize;
                    for (int x = 0; x < DetailSize; x++)
                    {
                        float u = (x + 0.5f) / DetailSize;
                        float t = 0f, amp = 0.5f; int f = 8;
                        for (int o = 0; o < 6; o++)
                        { t += amp * VNoise(u, v, f); amp *= 0.55f; f *= 2; }
                        h[y * DetailSize + x] = t;
                        if (t < lo) lo = t; if (t > hi) hi = t;
                    }
                }
                float span = Mathf.Max(hi - lo, 1e-5f);
                for (int i = 0; i < h.Length; i++)
                    h[i] = Mathf.Clamp01(((h[i] - lo) / span - 0.5f) * 1.55f + 0.5f);

                var px = new Color32[DetailSize * DetailSize];
                for (int y = 0; y < DetailSize; y++)
                    for (int x = 0; x < DetailSize; x++)
                    {
                        int i = y * DetailSize + x;
                        int xm = (x - 1 + DetailSize) % DetailSize, xp = (x + 1) % DetailSize;
                        int ym = (y - 1 + DetailSize) % DetailSize, yp = (y + 1) % DetailSize;
                        float gx = (h[y * DetailSize + xp] - h[y * DetailSize + xm]) * 0.5f;
                        float gy = (h[yp * DetailSize + x] - h[ym * DetailSize + x]) * 0.5f;
                        byte sx = (byte)(Mathf.Clamp01(0.5f - gx * 6f) * 255f);
                        byte sy = (byte)(Mathf.Clamp01(0.5f - gy * 6f) * 255f);
                        byte al = (byte)(Mathf.Clamp01(0.5f + (h[i] - 0.5f) * 0.30f) * 255f);
                        byte sm = (byte)(Mathf.Clamp01(0.5f + (h[i] - 0.5f) * 0.55f) * 255f);
                        px[i] = new Color32(al, sy, sm, sx);
                    }

                var t2 = new Texture2D(DetailSize, DetailSize, TextureFormat.RGBA32, true, true);
                Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Color32> arr = px;
                t2.SetPixels32(arr);
                t2.wrapMode = TextureWrapMode.Repeat;
                t2.filterMode = FilterMode.Trilinear;
                t2.anisoLevel = 4;
                t2.Apply(true, false);
                _detailTex = t2;
                MelonLogger.Msg("[MapLoader] built the shared surface-detail tile (" + DetailSize
                        + "px, " + sw.ElapsedMilliseconds + " ms). It goes on every opaque "
                        + "material, so the ~1,250 that arrive with no normal map stop being "
                        + "geometrically flat.");
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[MapLoader] detail tile: " + e.Message
                            + " -- surfaces keep whatever normals they came with.");
                _detailTex = null;
            }
            return _detailTex;
        }

        static Material _donor;
        static bool _donorMode;
        static string _donorWhy = "not looked for yet";

        public static string DonorNote { get { return _donorWhy; } }

        public static void ForgetDonor() { _donor = null; _donorWhy = "not looked for yet"; }

        static Material FindDonor()
        {
            if (_donor != null) return _donor;
            try
            {
                Transform ourRoot = null;
                try { ourRoot = MapRuntime.Root != null ? MapRuntime.Root.transform : null; } catch { }
                var rs = UnityEngine.Object.FindObjectsOfType<Renderer>();
                int looked = 0;
                for (int i = 0; i < (rs != null ? rs.Length : 0); i++)
                {
                    var r = rs[i];
                    if (r == null) continue;
                    try { if (ourRoot != null && r.transform.IsChildOf(ourRoot)) continue; } catch { }
                    Material m = null;
                    try { m = r.sharedMaterial; } catch { }
                    if (m == null || m.shader == null) continue;
                    looked++;
                    if (m.shader.name != "HDRP/Lit") continue;
                    string[] kw = null;
                    try { kw = m.shaderKeywords; } catch { }
                    if (kw == null) continue;
                    bool mask = false, norm = false, spec = false;
                    for (int k = 0; k < kw.Length; k++)
                    {
                        if (kw[k] == "_MASKMAP") mask = true;
                        else if (kw[k] == "_NORMALMAP") norm = true;
                        else if (kw[k] == "_MATERIAL_FEATURE_SPECULAR_COLOR") spec = true;
                    }
                    if (spec) continue;
                    if (!mask || !norm) continue;
                    _donor = m;
                    _donorWhy = "donor '" + m.name + "' from '" + r.gameObject.name
                              + "' (shader " + m.shader.name + ", queue " + m.renderQueue
                              + ", keywords " + string.Join(" ", kw) + ")";
                    return _donor;
                }
                _donorWhy = "no donor found: looked at " + looked + " shipped material(s) and none was "
                          + "HDRP/Lit with _MASKMAP and _NORMALMAP and no specular-colour feature";
            }
            catch (Exception e) { _donorWhy = "donor search failed: " + e.Message; }
            return null;
        }

        static void Key(Material m, string kw, bool on)
        {
            if (m == null) return;
            if (_donorMode) return;
            try { if (on) m.EnableKeyword(kw); else m.DisableKeyword(kw); } catch { }
        }

        public static Shader GetShader()
        {
            if (_shader != null) return _shader;

            foreach (var n in new[] { "HDRP/Lit", "HDRP/Unlit",
                                      "Universal Render Pipeline/Lit", "Standard" })
            {
                var s = Shader.Find(n);
                if (s != null) { _shader = s; break; }
            }

            MelonLogger.Msg("[MapLoader] shader resolved: " +
                            (_shader == null ? "NONE (materials will be broken)"
                                             : _shader.name));
            return _shader;
        }

        static readonly string[] SkipSubmeshNames = { "SewageWater" };

        public static int SkippedSubmeshes;

        public static bool SkipsSubmesh(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < SkipSubmeshNames.Length; i++)
                if (name.IndexOf(SkipSubmeshNames[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        public static Mesh Build(MapBundle.Geometry g, string name)
        {

            int vc = g.VertexCount;
            var vTmp = new Vector3[vc];
            var nTmp = new Vector3[vc];
            var uTmp = new Vector2[vc];
            for (int i = 0; i < vc; i++)
            {
                int p = i * 3, q = i * 2;
                vTmp[i] = new Vector3(g.Positions[p], g.Positions[p + 1], g.Positions[p + 2]);
                nTmp[i] = new Vector3(g.Normals[p], g.Normals[p + 1], g.Normals[p + 2]);
                uTmp[i] = new Vector2(g.UVs[q], g.UVs[q + 1]);
            }
            Il2CppStructArray<Vector3> verts = vTmp;
            Il2CppStructArray<Vector3> norms = nTmp;
            Il2CppStructArray<Vector2> uvs = uTmp;

            var mesh = new Mesh { name = name };

            if (vc > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.uv = uvs;

            int sub = g.SubMeshes.Count;
            if (sub <= 1)
            {

                Il2CppStructArray<int> tris = g.Indices;
                mesh.triangles = tris;
            }
            else
            {
                mesh.subMeshCount = sub;
                for (int s = 0; s < sub; s++)
                {
                    var sm = g.SubMeshes[s];

                    bool skip = SkipsSubmesh(sm.Name);
                    if (skip) SkippedSubmeshes++;
                    var slice = skip ? new int[0] : new int[sm.Count];
                    if (!skip) Array.Copy(g.Indices, sm.Start, slice, 0, sm.Count);
                    Il2CppStructArray<int> tris = slice;
                    mesh.SetTriangles(tris, s);
                }
            }

            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Texture2D LoadTexture(string path, bool linear = false)
        {
            if (string.IsNullOrEmpty(path)) return null;

            if (_texCache.TryGetValue(path + (linear ? "|lin" : ""), out var cached))
            {
                if (cached != null) return cached;
                _texCache.Remove(path + (linear ? "|lin" : ""));
            }
            Texture2D tex = null;
            try
            {
                if (File.Exists(path))
                {

                    var bytes = File.ReadAllBytes(path);
                    Il2CppStructArray<byte> arr = bytes;
                    var t = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear);
                    var swDec = System.Diagnostics.Stopwatch.StartNew();
                    bool decoded = ImageConversion.LoadImage(t, arr);
                    DecodeMs += swDec.ElapsedMilliseconds;
                    if (decoded)
                    {

                        if (MapIdentity.FullResTextures)
                        {
                            try
                            {
                                t.ignoreMipmapLimit = true;
                                if (!_mipOptOutLogged)
                                {
                                    _mipOptOutLogged = true;
                                    MelonLogger.Msg("[MapLoader] textures opt out of the "
                                        + "mipmap limit (readback ignoreMipmapLimit="
                                        + t.ignoreMipmapLimit + ", global limit "
                                        + QualitySettings.globalTextureMipmapLimit + ")");
                                }
                            }
                            catch (Exception e)
                            {
                                if (!_mipOptOutLogged)
                                {
                                    _mipOptOutLogged = true;
                                    MelonLogger.Warning("[MapLoader] ignoreMipmapLimit "
                                        + "not available on this build: " + e.Message
                                        + " -- textures follow the quality setting.");
                                }
                            }
                        }

                        if (!linear)
                        {
                            try
                            {

                                int mip = 0;
                                int w = t.width;
                                while (w > 256 && mip < t.mipmapCount - 1) { w >>= 1; mip++; }
                                var px = t.GetPixels32(mip);
                                if (px != null && px.Length > 0)
                                {

                                    int clear = 0, solid = 0;
                                    for (int i = 0; i < px.Length; i++)
                                    {
                                        byte al = px[i].a;
                                        if (al < 26) clear++;
                                        else if (al > 229) solid++;
                                    }
                                    float fClear = clear / (float)px.Length;
                                    float fSolid = solid / (float)px.Length;
                                    float fMid = 1f - fClear - fSolid;
                                    bool cut = fClear > 0.08f && fSolid > 0.01f && fMid < 0.25f;
                                    _cutoutTex[path] = cut;
                                    if (!cut && fClear > 0.08f)
                                    {
                                        _cutoutRejected++;
                                        if (_cutoutRejected <= 8)
                                            MelonLogger.Msg("[MapLoader] alpha on '"
                                                + System.IO.Path.GetFileName(path)
                                                + "' is " + (fSolid <= 0.01f ? "unused" : "packed data")
                                                + " (clear " + fClear.ToString("F2")
                                                + ", solid " + fSolid.ToString("F2")
                                                + ", mid " + fMid.ToString("F2")
                                                + ") -- NOT clipped. Clipping it would have made a "
                                                + "solid surface see-through.");
                                    }
                                }
                            }
                            catch { }
                        }

                        bool keepRaw = !linear && MapIdentity.UncompressedGround
                                       && IsGroundAlbedoPath(path);
                        if (keepRaw)
                        {
                            _rawGround++;
                            if (_rawGround <= 12)
                                MelonLogger.Msg("[MapLoader] '"
                                    + System.IO.Path.GetFileName(path)
                                    + "' stays uncompressed (" + t.width + "x" + t.height
                                    + ") -- DXT1 on near-flat ground is a 4-texel lattice");
                            try { TexBytesRaw += TexBytes(t.width, t.height, 4.0f);
                                  TexCountRaw++; } catch { }
                        }
                        else if (!linear)
                        {

                            var swCmp = System.Diagnostics.Stopwatch.StartNew();
                            try { t.Compress(false); } catch { }
                            CompressMs += swCmp.ElapsedMilliseconds;

                            try { TexBytesCompressed += TexBytes(t.width, t.height, 1.0f);
                                  TexCountCompressed++; } catch { }
                        }
                        else
                        {
                            try { TexBytesRaw += TexBytes(t.width, t.height, 4.0f);
                                  TexCountRaw++; } catch { }
                        }

                        try { t.filterMode = FilterMode.Trilinear; t.anisoLevel = 4; } catch { }

                        try { t.Apply(false, true); } catch { }
                        tex = t;
                    }
                }
            }
            catch { tex = null; }
            _texCache[path + (linear ? "|lin" : "")] = tex;
            return tex;
        }

        static void SetIf(Material m, string prop, float v)
        {
            if (m.HasProperty(prop)) m.SetFloat(prop, v);
        }

        static readonly string[] CutoutDeny =
            { "Bark", "Trunk", "Rock", "Rubble", "Brick" };
        static readonly string[] CutoutAllow =
            { "Leaf", "Leaves", "Frond", "Grass", "Wheat", "Brome", "Foliage",
              "Branch", "Plant", "Palm", "Wildgrass", "TwoSided" };

        public static bool NeedsCutout(string matName)
        {
            if (string.IsNullOrEmpty(matName)) return false;
            for (int i = 0; i < CutoutDeny.Length; i++)
                if (matName.IndexOf(CutoutDeny[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
            for (int i = 0; i < CutoutAllow.Length; i++)
                if (matName.IndexOf(CutoutAllow[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        public static float GroundAlbedoScale = 0.75f;

        static readonly System.Collections.Generic.List<Material> _groundMats
            = new System.Collections.Generic.List<Material>();
        static readonly System.Collections.Generic.List<Color> _groundBase
            = new System.Collections.Generic.List<Color>();

        public static void SetGroundBrightness(float scale, MelonLoader.MelonLogger.Instance log)
        {
            GroundAlbedoScale = Mathf.Clamp(scale, 0.02f, 1f);
            TuneStore.Save("GroundAlbedo", GroundAlbedoScale);
            int n = 0;
            for (int i = 0; i < _groundMats.Count && i < _groundBase.Count; i++)
            {
                var m = _groundMats[i];
                if (m == null) continue;
                var b = _groundBase[i];

                var c = new Color(b.r * GroundAlbedoScale,
                                  b.g * GroundAlbedoScale,
                                  b.b * GroundAlbedoScale, b.a);
                try
                {
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                    if (m.HasProperty("_Color")) m.SetColor("_Color", c);
                    n++;
                }
                catch { }
            }
            log?.Msg("[tune] ground albedo " + (GroundAlbedoScale * 100f).ToString("F0")
                   + "% of authored, across " + n + " material(s). Sky, moon and "
                   + "foliage are untouched.");
        }

        public static void ForgetGroundMats()
        {
            _cutoutMats.Clear();
            _groundMats.Clear(); _groundBase.Clear();
        }

        static int _rawGround;

        static readonly string[] RawAlbedoPrefixes =
        {
            "terrain_", "dirt_road_", "road_edge", "sloped_rock",
            "broken_rocks", "tall_cliff", "cobblestone", "stone_scatter",
        };

        static bool IsGroundAlbedoPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string f = System.IO.Path.GetFileName(path).ToLowerInvariant();
            if (!f.EndsWith("_albedo.png")) return false;

            for (int i = 0; i < RawAlbedoPrefixes.Length; i++)
                if (f.StartsWith(RawAlbedoPrefixes[i])) return true;
            return false;
        }
        public static int GroundDarkened;

        static readonly string[] GroundMats =
        {
            "terrain", "tilled_field", "compound_concrete", "compound_canal", "cracks",
        };

        static readonly string[] CityGroundMats =
        {
            "roadmain", "sidewalk", "groundbrick", "curbs_inst", "soil_inst",
            "mi_ground", "concrete_plates", "road_inst", "gravelpile",
            "basketball_court",
        };

        static readonly string[] GroundDeny =
        {
            "groundtrash", "groundleaves", "roadblock", "roadconstruction",
            "roaddecals", "broadleaf", "marker",
        };

        static bool IsGroundMaterial(string matName)
        {
            if (string.IsNullOrEmpty(matName)) return false;
            string l = matName.ToLowerInvariant();
            for (int i = 0; i < GroundDeny.Length; i++)
                if (l.Contains(GroundDeny[i])) return false;
            for (int i = 0; i < GroundMats.Length; i++)
                if (l.Contains(GroundMats[i])) return true;
            for (int i = 0; i < CityGroundMats.Length; i++)
                if (l.Contains(CityGroundMats[i])) return true;
            return false;
        }

        static bool IsMatteGround(string matName)
        {
            if (string.IsNullOrEmpty(matName)) return false;
            string l = matName.ToLowerInvariant();
            for (int i = 0; i < GroundDeny.Length; i++)
                if (l.Contains(GroundDeny[i])) return false;
            for (int i = 0; i < GroundMats.Length; i++)
                if (l.Contains(GroundMats[i])) return true;

            if (MapIdentity.MatteGroundExtra)
                for (int i = 0; i < SnowyGroundMats.Length; i++)
                    if (l.Contains(SnowyGroundMats[i])) return true;
            return false;
        }

        static readonly string[] SnowyGroundMats =
        {
            "dirt_road", "road_course", "road_bare", "road_trails",
            "rock", "gravel", "cliff", "bank", "verge",
        };

        public static Material BuildMaterial(MapBundle.MaterialDef def, string texDir,
                                            bool alphaClip = false, string meshId = null)
        {
            Material donor = null;
            _donorMode = false;
            try
            {
                if (MapIdentity.DonorMaterials)
                {
                    donor = FindDonor();
                    _donorMode = donor != null;
                }
            }
            catch { }
            var sh = GetShader();
            if (sh == null) return null;

            var mat = _donorMode ? new Material(donor) { name = def.Name ?? "map_mat" }
                                 : new Material(sh)    { name = def.Name ?? "map_mat" };

            mat.enableInstancing = true;

            Texture2D albedoTex = null;
            string albedoPath = null;
            if (!string.IsNullOrEmpty(def.Albedo))
            {
                albedoPath = Path.Combine(texDir, def.Albedo);
                albedoTex = LoadTexture(albedoPath);
            }

            var c = def.BaseColor;
            var col = c != null && c.Length == 4
                ? new Color(c[0], c[1], c[2], c[3]) : Color.white;

            if (IsGroundMaterial(def.Name))
            {
                col = new Color(col.r * GroundAlbedoScale,
                                col.g * GroundAlbedoScale,
                                col.b * GroundAlbedoScale,
                                col.a);
                GroundDarkened++;

                try
                {
                    _groundMats.Add(mat);
                    _groundBase.Add(c != null && c.Length == 4
                        ? new Color(c[0], c[1], c[2], c[3]) : Color.white);
                }
                catch { }
            }

            if (IsWaterMesh(meshId))
                col = new Color(WaterTint.r, WaterTint.g, WaterTint.b, col.a);

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", col);

            bool texSaysCutout = albedoPath != null
                && _cutoutTex.TryGetValue(albedoPath, out var cv) && cv;

            bool isGlass = IsGlassMaterial(def.Name)
                        && !(MapIdentity.StrictGlass && MeshLooksStructural(meshId))
                        && BlendAllowsCutout(def.Name);
            bool isWater = IsWaterMaterial(def.Name) || IsWaterMesh(meshId);

            bool isTerrainMat = IsGroundMaterial(def.Name);

            bool cutout = (texSaysCutout || BlendForcesCutout(def.Name))
                          && !isTerrainMat && !isGlass
                          && BlendAllowsCutout(def.Name);

            if (cutout)
            {

                SetIf(mat, "_AlphaCutoffEnable", 1f);
                float cutv = FoliageCutoffNow;
                SetIf(mat, "_AlphaCutoff", cutv);
                SetIf(mat, "_Cutoff", cutv);
                SetIf(mat, "_AlphaCutoffShadow", cutv);
                SetIf(mat, "_UseShadowThreshold", 0f);

                SetIf(mat, "_CullMode", 0f);
                SetIf(mat, "_CullModeForward", 0f);
                SetIf(mat, "_OpaqueCullMode", 2f);
                SetIf(mat, "_DoubleSidedEnable", 1f);

                bool flip = MapIdentity.FoliageBackfaceFlip;
                SetIf(mat, "_DoubleSidedNormalMode", flip ? 0f : 2f);
                if (mat.HasProperty("_DoubleSidedConstants"))
                    mat.SetVector("_DoubleSidedConstants",
                                  flip ? new Vector4(-1f, -1f, -1f, 0f)
                                       : new Vector4(1f, 1f, 1f, 0f));

                SetIf(mat, "_ZTestGBuffer", 3f);
                SetIf(mat, "_ZTestDepthEqualForOpaque", 3f);

                SetIf(mat, "_StencilWriteMask", 6f);
                SetIf(mat, "_StencilRefGBuffer", 10f);
                SetIf(mat, "_StencilWriteMaskGBuffer", 15f);
                SetIf(mat, "_StencilRefDepth", 8f);
                SetIf(mat, "_StencilWriteMaskDepth", 9f);
                SetIf(mat, "_StencilRefMV", 40f);
                SetIf(mat, "_StencilWriteMaskMV", 41f);

                if (MapIdentity.FoliageNoSpecular && IsLeafy(def.Name ?? meshId))
                {
                    try
                    {
                        SetIf(mat, "_MaterialID", 4f);
                        if (mat.HasProperty("_SpecularColor"))
                            mat.SetColor("_SpecularColor", Color.black);
                        SetIf(mat, "_EnergyConservingSpecularColor", 0f);
                        Key(mat, "_MATERIAL_FEATURE_SPECULAR_COLOR", true);
                        if (!_noSpecLogged)
                        {
                            _noSpecLogged = true;
                            var rb = mat.HasProperty("_SpecularColor")
                                   ? mat.GetColor("_SpecularColor").ToString() : "(absent)";
                            MelonLogger.Msg("[MapLoader] foliage specular removed on '"
                                + def.Name + "': _MaterialID readback "
                                + (mat.HasProperty("_MaterialID")
                                   ? mat.GetFloat("_MaterialID").ToString("F0") : "?")
                                + ", _SpecularColor " + rb
                                + ", keyword=" + mat.IsKeywordEnabled(
                                    "_MATERIAL_FEATURE_SPECULAR_COLOR"));
                        }
                    }
                    catch (Exception spe)
                    {
                        if (!_noSpecLogged)
                        {
                            _noSpecLogged = true;
                            MelonLogger.Warning("[MapLoader] foliage specular kill "
                                + "unavailable: " + spe.Message);
                        }
                    }
                }

                Key(mat, "_ALPHATEST_ON", true);
                Key(mat, "_DOUBLESIDED_ON", true);
                mat.renderQueue = 2450;

                _cutouts++;
                _cutoutMats.Add(mat);
                if (!_cutoutLogged)
                {
                    _cutoutLogged = true;
                    var kw = mat.shaderKeywords;
                    MelonLogger.Msg($"[MapLoader] cutout self-check on '{def.Name}': " +
                        $"_ALPHATEST_ON={mat.IsKeywordEnabled("_ALPHATEST_ON")} " +
                        $"queue={mat.renderQueue} " +
                        $"keywords=[{(kw == null ? "" : string.Join(",", kw))}] " +
                        $"mipLimit={QualitySettings.globalTextureMipmapLimit}");
                }
            }

            if (!isGlass && CityMaterials && MapIdentity.SurfaceDetail)
            {
                var dt = DetailTex();
                if (dt != null && mat.HasProperty("_DetailMap"))
                {
                    try
                    {
                        mat.SetTexture("_DetailMap", dt);

                        float dtile = DetailTileOverridden ? DetailTile : MapIdentity.DetailTile;
                        if (meshId != null && meshId.StartsWith("terrain_",
                                StringComparison.OrdinalIgnoreCase))
                            dtile = TerrainDetailTile;
                        mat.SetTextureScale("_DetailMap", new Vector2(dtile, dtile));
                        Key(mat, "_DETAIL_MAP", true);

                        bool isTerrainMesh = meshId != null
                            && meshId.StartsWith("terrain_", StringComparison.OrdinalIgnoreCase);
                        if (mat.HasProperty("_DetailAlbedoScale"))
                            mat.SetFloat("_DetailAlbedoScale", isTerrainMesh ? 1.0f : 0.55f);
                        float dn = MapIdentity.DetailNormal > 0f
                                 ? MapIdentity.DetailNormal : 0.85f;
                        if (mat.HasProperty("_DetailNormalScale"))
                            mat.SetFloat("_DetailNormalScale", isTerrainMesh ? 1.7f : dn);
                        if (mat.HasProperty("_DetailSmoothnessScale"))

                            mat.SetFloat("_DetailSmoothnessScale", 0.20f);
                        if (mat.HasProperty("_UVDetail")) mat.SetFloat("_UVDetail", 0f);
                    }
                    catch { }
                }
            }

            bool isGround = IsGroundMaterial(def.Name);
            bool isMatte = IsMatteGround(def.Name);

            float smooth = CityMaterials
                ? (isMatte ? 0.05f : isGlass ? 0.96f : isWater ? 0.94f : 0.34f)
                : (isMatte ? 0.05f : isGlass ? 0.90f : isWater ? 0.94f : 0.2f);

            if (IsWaterMaterial(def.Name))
            {
                var tile = new Vector2(2400f, 2400f);
                try
                {
                    if (mat.HasProperty("_BaseColorMap")) mat.SetTextureScale("_BaseColorMap", tile);
                    if (mat.HasProperty("_NormalMap")) mat.SetTextureScale("_NormalMap", tile);
                }
                catch { }
            }
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smooth);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smooth);
            if (mat.HasProperty("_SmoothnessRemapMax"))
                mat.SetFloat("_SmoothnessRemapMax", smooth);

            if (mat.HasProperty("_Metallic"))
                mat.SetFloat("_Metallic", string.IsNullOrEmpty(def.Mask) ? 0f : 1f);

            if (string.IsNullOrEmpty(def.Albedo) && !isGlass && !isWater)
            {
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0f);
                if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0f);
                if (mat.HasProperty("_SmoothnessRemapMax")) mat.SetFloat("_SmoothnessRemapMax", 0f);
            }

            float[] constants = null;
            if (albedoTex == null && _constants.Count > 0)
                _constants.TryGetValue(def.Name ?? "", out constants);
            if (constants != null && !isGlass)
            {
                var cc = new Color(constants[0], constants[1], constants[2], 1f);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", cc);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", cc);
                SetIf(mat, "_Metallic", constants[3]);
                SetIf(mat, "_Smoothness", constants[4]);
                SetIf(mat, "_Glossiness", constants[4]);
                SetIf(mat, "_SmoothnessRemapMax", constants[4]);
                ConstantsApplied++;
            }

            if (!string.IsNullOrEmpty(def.Albedo))
            {
                string full = albedoPath;
                var tex = albedoTex;
                if (tex != null)
                {

                    float mb = FoliageMipBiasNow;
                    if (cutout && mb > 0f)
                    {
                        try
                        {
                            tex.mipMapBias = mb;
                            if (!_mipBiasLogged)
                            {
                                _mipBiasLogged = true;
                                MelonLogger.Msg("[MapLoader] cutout foliage mip bias +"
                                    + mb.ToString("F2")
                                    + " (readback " + tex.mipMapBias.ToString("F2")
                                    + ") -- steadies leaf edges in motion.");
                            }
                        }
                        catch (Exception e)
                        {
                            if (!_mipBiasLogged)
                            {
                                _mipBiasLogged = true;
                                MelonLogger.Warning("[MapLoader] mipMapBias unavailable: "
                                    + e.Message);
                            }
                        }
                    }
                    bool bound = false;
                    if (mat.HasProperty("_BaseColorMap"))
                    { mat.SetTexture("_BaseColorMap", tex); bound = true; }
                    if (mat.HasProperty("_MainTex"))
                    { mat.SetTexture("_MainTex", tex); bound = true; }
                    if (mat.HasProperty("_BaseMap"))
                    { mat.SetTexture("_BaseMap", tex); bound = true; }

                    if (!bound)
                        MelonLogger.Warning($"[MapLoader] '{def.Name}': texture loaded " +
                            $"but shader '{sh.name}' has no albedo slot");
                    else _bound++;
                }
                else
                {
                    _failed++;
                    if (_failed <= 8)
                        MelonLogger.Warning($"[MapLoader] albedo FAILED to load for " +
                            $"'{def.Name}': {full}");
                }
            }

            if (!string.IsNullOrEmpty(def.Normal))
            {
                var n = LoadTexture(Path.Combine(texDir, def.Normal), linear: true);
                if (n != null && mat.HasProperty("_NormalMap"))
                {
                    mat.SetTexture("_NormalMap", n);

                    float nsOv = MapIdentity.NormalScaleOverride;
                    if (mat.HasProperty("_NormalScale"))
                        mat.SetFloat("_NormalScale",
                                     nsOv > 0f ? nsOv : (CityMaterials ? 1.30f : 1f));
                    Key(mat, "_NORMALMAP", true);
                    _normals++;
                }
            }
            if (!string.IsNullOrEmpty(def.Mask))
            {
                var mk = LoadTexture(Path.Combine(texDir, def.Mask), linear: true);
                if (mk != null && mat.HasProperty("_MaskMap"))
                {
                    mat.SetTexture("_MaskMap", mk);
                    Key(mat, "_MASKMAP", true);

                    if (mat.HasProperty("_SmoothnessRemapMin")) mat.SetFloat("_SmoothnessRemapMin", 0f);
                    if (mat.HasProperty("_SmoothnessRemapMax"))
                        mat.SetFloat("_SmoothnessRemapMax",
                                     isMatte ? 0.35f
                                   : MapIdentity.SmoothnessCeilingOverride > 0f
                                             ? MapIdentity.SmoothnessCeilingOverride
                                             : (CityMaterials ? SmoothnessCeiling
                                                              : FarmsteadSmoothnessCeiling));

                    if (mat.HasProperty("_AORemapMin")) mat.SetFloat("_AORemapMin", CityMaterials ? 0.10f : 0.30f);
                    if (mat.HasProperty("_AORemapMax")) mat.SetFloat("_AORemapMax", 1f);
                    _masks++;
                }
            }

            if (_decalsOff.Contains(def.Name))
            {
                SetIf(mat, "_SupportDecals", 0f);
                try { Key(mat, "_DISABLE_DECALS", true); } catch { }
            }

            if (isGlass)
            {

                var gcol = albedoTex != null
                    ? new Color(col.r, col.g, col.b, GlassAlpha)
                    : (constants != null
                        ? new Color(constants[0], constants[1], constants[2], GlassAlpha)
                        : new Color(0.24f, 0.27f, 0.26f, GlassAlpha));

                if (GlassTransparent)
                {

                    SetIf(mat, "_SurfaceType", 1f);
                    SetIf(mat, "_BlendMode", 0f);
                    SetIf(mat, "_SrcBlend", 1f);
                    SetIf(mat, "_DstBlend", 10f);
                    SetIf(mat, "_AlphaSrcBlend", 1f);
                    SetIf(mat, "_AlphaDstBlend", 10f);
                    SetIf(mat, "_ZWrite", 0f);
                    SetIf(mat, "_TransparentZWrite", 0f);
                    SetIf(mat, "_ZTestTransparent", 4f);
                    SetIf(mat, "_EnableFogOnTransparent", 1f);
                    SetIf(mat, "_EnableBlendModePreserveSpecularLighting", 1f);
                    SetIf(mat, "_TransparentDepthPrepassEnable", 0f);
                    SetIf(mat, "_TransparentDepthPostpassEnable", 0f);
                    SetIf(mat, "_TransparentBackfaceEnable", 0f);
                    SetIf(mat, "_TransparentSortPriority", 0f);
                    try { mat.SetOverrideTag("RenderType", "Transparent"); } catch { }

                    Key(mat, "_SURFACE_TYPE_TRANSPARENT", true);
                    Key(mat, "_ENABLE_FOG_ON_TRANSPARENT", true);
                    Key(mat, "_BLENDMODE_ALPHA", true);
                    mat.renderQueue = 3000;
                }
                else
                {

                    gcol = new Color(0.03f, 0.035f, 0.04f, 1f);
                }

                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", gcol);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", gcol);

                float gsm = constants != null ? constants[4] : 0.90f;
                SetIf(mat, "_Smoothness", gsm);
                SetIf(mat, "_Glossiness", gsm);
                SetIf(mat, "_SmoothnessRemapMax", gsm);
                SetIf(mat, "_Metallic", 0f);
                if (constants != null) ConstantsApplied++;
                _glass++;

                try { if (_glassNames.Count < 40) _glassNames.Add((def != null ? def.Name : null) ?? "?"); } catch { }
            }

            if (_emissive.Count > 0)
            {
                Color em;
                if (_emissive.TryGetValue(def.Name ?? "", out em)
                    && mat.HasProperty("_EmissiveColor"))
                {
                    mat.SetColor("_EmissiveColor", em);
                    SetIf(mat, "_UseEmissiveIntensity", 0f);
                    EmissiveApplied++;
                }
            }
            return mat;
        }
    }
}
