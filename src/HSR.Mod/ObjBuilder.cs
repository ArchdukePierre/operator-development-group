// ODG Operations - HSR
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MelonLoader;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace HSR
{

    internal static class ObjBuilder
    {
        static string ObjPath => Path.Combine(HsrPaths.AssetsDir, "HSR.obj");
        static string TexDir => HsrPaths.AssetsDir;

        const float Scale = 1.0f;

        static readonly HashSet<string> SkipParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        const string BoltGroup = "m200_bolt";

        internal const string MagGroup = "m200_mag";

        static MelonPreferences_Entry<float> _smoothMax;
        static MelonPreferences_Entry<float> _tint;
        internal static void Init(MelonPreferences_Category cat)
        {
            _smoothMax = cat.CreateEntry("BodySmoothnessMax", 1.0f);

            _tint = cat.CreateEntry("BodyTint", 1.0f);
            _shadows = cat.CreateEntry("BodyCastShadows", false);

            _decals = cat.CreateEntry("BodyReceiveDecals", false);

            _matchLayers = cat.CreateEntry("BodyMatchDonorRenderingLayers", true);

            _matchLayer = cat.CreateEntry("BodyMatchDonorLayer", false);

            _donorShader = cat.CreateEntry("BodyUseDonorShader", true);
        }

        static MelonPreferences_Entry<bool> _donorShader;
        internal static bool UseDonorShader { get { try { return _donorShader == null || _donorShader.Value; } catch { return true; } } }

        static MelonPreferences_Entry<bool> _decals, _matchLayers, _matchLayer;
        internal static bool ReceiveDecals { get { try { return _decals != null && _decals.Value; } catch { return false; } } }
        internal static bool MatchDonorRenderingLayers { get { try { return _matchLayers == null || _matchLayers.Value; } catch { return true; } } }
        internal static bool MatchDonorLayer { get { try { return _matchLayer != null && _matchLayer.Value; } catch { return false; } } }

        static MelonPreferences_Entry<bool> _shadows;
        static bool CastShadows
        {
            get { try { return _shadows != null && _shadows.Value; } catch { return false; } }
        }

        static float Tint
        {
            get
            {
                try
                {
                    if (_tint == null) return 1.0f;
                    float v = _tint.Value;
                    return v < 0.05f ? 0.05f : (v > 1f ? 1f : v);
                }
                catch { return 1.0f; }
            }
        }

        static float SmoothnessMax
        {
            get
            {
                try
                {
                    if (_smoothMax == null) return 1.0f;
                    float v = _smoothMax.Value;
                    return v < 0f ? 0f : (v > 1f ? 1f : v);
                }
                catch { return 1.0f; }
            }
        }

        const string BoltBoneName = "Action";

        const string ModsBranch = "MODS";

        internal static GameObject Build(Transform holder, MelonLogger.Instance log)
        {
            if (!File.Exists(ObjPath)) { log.Warning("OBJ not found: " + ObjPath); return null; }

            var v = new List<Vector3>(); var vt = new List<Vector2>(); var vn = new List<Vector3>();
            var parts = new List<(string name, List<int[]> faces)>();
            List<int[]> cur = null;

            foreach (var raw in File.ReadLines(ObjPath))
            {
                var line = raw.Trim();
                if (line.Length < 2) continue;
                var tok = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                switch (tok[0])
                {
                    case "v": v.Add(new Vector3(P(tok[1]), P(tok[2]), P(tok[3]))); break;
                    case "vt": vt.Add(new Vector2(P(tok[1]), P(tok[2]))); break;
                    case "vn": vn.Add(new Vector3(P(tok[1]), P(tok[2]), P(tok[3]))); break;
                    case "g":
                        var gname = tok.Length > 1 ? tok[1] : "part" + parts.Count;
                        cur = new List<int[]>();
                        parts.Add((gname, cur));
                        break;
                    case "f":
                        if (cur == null) { cur = new List<int[]>(); parts.Add(("root", cur)); }

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
                            cur.Add(new int[9] { corners[0][0], corners[0][1], corners[0][2],
                                                 corners[i-1][0], corners[i-1][1], corners[i-1][2],
                                                 corners[i][0], corners[i][1], corners[i][2] });
                        break;
                }
            }

            var root = new GameObject("HSR_BarrettBody");
            root.transform.SetParent(holder, false);
            var mat = MakeMaterial(log);
            int built = 0, totalVerts = 0;

            foreach (var (name, faces) in parts)
            {
                var baseName = name.Split('.')[0].Replace("-", "_");
                if (SkipParts.Contains(baseName) || faces.Count == 0) continue;

                var map = new Dictionary<(int, int, int), int>();
                var lv = new List<Vector3>(); var lu = new List<Vector2>(); var ln = new List<Vector3>();
                var tris = new List<int>(faces.Count * 3);
                foreach (var f in faces)
                    for (int c = 0; c < 3; c++)
                    {
                        var key = (f[c * 3], f[c * 3 + 1], f[c * 3 + 2]);
                        if (!map.TryGetValue(key, out int li))
                        {
                            li = lv.Count; map[key] = li;
                            lv.Add(v[key.Item1] * Scale);
                            lu.Add(key.Item2 >= 0 ? vt[key.Item2] : Vector2.zero);
                            ln.Add(key.Item3 >= 0 ? vn[key.Item3] : Vector3.up);
                        }
                        tris.Add(li);
                    }

                var mesh = new Mesh();
                mesh.name = name;

                if (lv.Count > 65000)
                    mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

                mesh.vertices = ToIl2Cpp(lv);
                mesh.uv = ToIl2CppV2(lu);
                mesh.normals = ToIl2Cpp(ln);
                mesh.triangles = ToIl2CppInt(tris);
                mesh.RecalculateBounds();

                var go = new GameObject(name);
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mrend = go.AddComponent<MeshRenderer>();
                mrend.sharedMaterial = mat;

                try
                {
                    mrend.shadowCastingMode = CastShadows
                        ? UnityEngine.Rendering.ShadowCastingMode.On
                        : UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                catch { }
                built++; totalVerts += lv.Count;
            }

            log.Msg("OBJ build: {0} parts, {1} verts, scale {2:F5}, shadows {3} — body constructed in-engine.",
                    built, totalVerts, Scale, CastShadows ? "ON" : "OFF");
            return built > 0 ? root : null;
        }

        internal static void AttachBolt(Transform body, Transform weaponRoot, MelonLogger.Instance log)
        {
            if (body == null || weaponRoot == null) return;

            Transform group = null;
            for (int i = 0; i < body.childCount; i++)
                if (string.Equals(body.GetChild(i).gameObject.name, BoltGroup, StringComparison.OrdinalIgnoreCase))
                { group = body.GetChild(i); break; }

            if (group == null) return;

            BoltCycle.BindOurs(group, log);

            var rideRot = FindNamed(weaponRoot, "BoltRotate", body);
            var ridePos = rideRot != null ? rideRot.parent : FindNamed(weaponRoot, "Bolt", body);
            if (rideRot != null)
            {
                BoltCycle.BindRide(group, rideRot, ridePos, log);
                return;
            }

            if (BoltCycle.ProceduralEnabled)
            {
                log.Msg("bolt: '" + BoltGroup + "' stays on our body — no donor 'BoltRotate' found, "
                      + "so the cycle is driven on our own clock.");
                return;
            }

            Transform bone = FindBone(weaponRoot, body);
            if (bone == null)
            {
                log.Warning("no bolt transform on the donor rig (looked for an exact '"
                          + BoltBoneName + "' outside our own body and never under " + ModsBranch
                          + "). Falling back to the procedural cycle; set ProceduralBolt=true "
                          + "under [HSR] to enable it.");
                return;
            }

            group.SetParent(bone, true);
            BoltCycle.NoteDonorBolt(bone, log);
            log.Msg("bolt: '" + BoltGroup + "' -> donor '" + bone.gameObject.name
                  + "'. BOLTPROBE will report whether that transform actually moves on firing.");
        }

        internal static void AttachMag(Transform body, Transform host, MelonLogger.Instance log)
        {
            if (body == null) return;

            Transform group = null;
            for (int i = 0; i < body.childCount; i++)
                if (string.Equals(body.GetChild(i).gameObject.name, MagGroup, StringComparison.OrdinalIgnoreCase))
                { group = body.GetChild(i); break; }
            if (group == null) return;

            if (host == null) return;

            MagFollow.Bind(group, host, log);
        }

        static Transform FindNamed(Transform t, string name, Transform exclude)
        {
            if (t == null) return null;
            if (exclude != null && (t == exclude || t.IsChildOf(exclude))) return null;
            var n = t.gameObject.name;
            if (string.Equals(n, ModsBranch, StringComparison.Ordinal)) return null;
            if (string.Equals(n, name, StringComparison.Ordinal)) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var hit = FindNamed(t.GetChild(i), name, exclude);
                if (hit != null) return hit;
            }
            return null;
        }

        static Transform FindBone(Transform t, Transform exclude)
        {
            if (exclude != null && (t == exclude || t.IsChildOf(exclude))) return null;
            var n = t.gameObject.name;
            if (string.Equals(n, ModsBranch, StringComparison.Ordinal)) return null;
            if (string.Equals(n, BoltBoneName, StringComparison.Ordinal)) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var hit = FindBone(t.GetChild(i), exclude);
                if (hit != null) return hit;
            }
            return null;
        }

        static float P(string s) => float.Parse(s, CultureInfo.InvariantCulture);
        static int Fix(string s, int count) { int i = int.Parse(s, CultureInfo.InvariantCulture); return i > 0 ? i - 1 : count + i; }

        static Il2CppStructArray<Vector3> ToIl2Cpp(List<Vector3> src)
        { var a = new Il2CppStructArray<Vector3>(src.Count); for (int i = 0; i < src.Count; i++) a[i] = src[i]; return a; }
        static Il2CppStructArray<Vector2> ToIl2CppV2(List<Vector2> src)
        { var a = new Il2CppStructArray<Vector2>(src.Count); for (int i = 0; i < src.Count; i++) a[i] = src[i]; return a; }
        static Il2CppStructArray<int> ToIl2CppInt(List<int> src)
        { var a = new Il2CppStructArray<int>(src.Count); for (int i = 0; i < src.Count; i++) a[i] = src[i]; return a; }

        internal static Il2CppStructArray<Vector3> ToIl2Cpp(Vector3[] src)
        { var a = new Il2CppStructArray<Vector3>(src.Length); for (int i = 0; i < src.Length; i++) a[i] = src[i]; return a; }
        internal static Il2CppStructArray<int> ToIl2CppInt(int[] src)
        { var a = new Il2CppStructArray<int>(src.Length); for (int i = 0; i < src.Length; i++) a[i] = src[i]; return a; }

        static string Prop(Material m, string n)
        {
            try { return m.HasProperty(n) ? m.GetFloat(n).ToString("0.##") : "-"; } catch { return "?"; }
        }

        static string KeywordList(Material m)
        {
            try
            {
                var ks = m.shaderKeywords;
                if (ks == null || ks.Length == 0) return "(none)";
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < ks.Length && i < 24; i++)
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(ks[i]);
                }
                return sb.ToString();
            }
            catch { return "?"; }
        }

        static Material _donorClone;
        static int _donorMatReports;

        internal static Material MaterialFromDonor(Transform weaponRoot, Transform ourBody,
                                                   MelonLogger.Instance log)
        {
            if (_donorClone != null) return _donorClone;
            try
            {
                if (weaponRoot == null) return null;

                Material src = null;
                string from = null;
                var rends = weaponRoot.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rends.Length; i++)
                {
                    var r = rends[i];
                    if (r == null) continue;
                    try
                    {
                        if (r.TryCast<ParticleSystemRenderer>() != null) continue;
                        if (r.TryCast<TrailRenderer>() != null) continue;
                        var t = r.transform;
                        if (ourBody != null && (t == ourBody || t.IsChildOf(ourBody))) continue;

                        var m = r.sharedMaterial;
                        if (m == null || m.shader == null) continue;

                        var sn = m.shader.name;
                        if (sn == null || sn.IndexOf("Lit", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        if (sn.IndexOf("Decal", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                        bool isHdrpLit = string.Equals(sn, "HDRP/Lit", StringComparison.Ordinal);
                        if (isHdrpLit) { src = m; from = r.gameObject.name; break; }
                        if (src == null) { src = m; from = r.gameObject.name; }
                    }
                    catch { }
                }

                if (src == null)
                {
                    if (log != null) log.Warning("material: no donor material to copy — keeping the hand-built one.");
                    return null;
                }

                var mat = new Material(src) { name = "HSR_Body_FromDonor" };

                var baseTex = LoadTex(Path.Combine(TexDir, "Body_BaseMap.png"), log);
                if (baseTex != null)
                {
                    if (mat.HasProperty("_BaseColorMap")) mat.SetTexture("_BaseColorMap", baseTex);
                    if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", baseTex);
                }
                var mask = LoadTex(Path.Combine(TexDir, "Body_MaskMap.png"), log, linear: true);

                if (mask != null && mat.HasProperty("_MaskMap"))
                    mat.SetTexture("_MaskMap", mask);
                var nrm = LoadTex(Path.Combine(TexDir, "Body_Normal.png"), log, linear: true);
                if (nrm != null && mat.HasProperty("_NormalMap"))
                {

                    bool unreal = false;
                    try { unreal = mat.IsKeywordEnabled("_USE_UNREAL_NORMAL_MAP"); } catch { }
                    mat.SetTexture("_NormalMap", unreal ? FlipGreen(nrm, log) : nrm);
                }

                try
                {
                    float t = Tint;
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(t, t, t, 1f));
                    if (mat.HasProperty("_SmoothnessRemapMax")) mat.SetFloat("_SmoothnessRemapMax", SmoothnessMax);
                    if (mat.HasProperty("_SmoothnessRemapMin")) mat.SetFloat("_SmoothnessRemapMin", 0f);
                }
                catch { }

                try
                {

                    if (mat.HasProperty("_EmissiveColor")) mat.SetColor("_EmissiveColor", Color.black);

                    bool specWorkflow = false;
                    try { specWorkflow = mat.IsKeywordEnabled("_MATERIAL_FEATURE_SPECULAR_COLOR"); } catch { }
                    if (specWorkflow)
                    {
                        if (mat.HasProperty("_SpecularColor")) mat.SetColor("_SpecularColor", new Color(0.04f, 0.04f, 0.04f, 1f));
                        if (mat.HasProperty("_SpecularColorMap")) mat.SetTexture("_SpecularColorMap", null);
                        if (mat.HasProperty("_EnergyConservingSpecularColor")) mat.SetFloat("_EnergyConservingSpecularColor", 1f);
                        if (log != null) log.Msg("material: donor copy is a specular-colour workflow (the brass round's); specular set to dielectric 0.04, brass map dropped.");
                    }
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(Tint, Tint, Tint, 1f));
                    var sh = mat.shader;
                    var sb = new System.Text.StringBuilder();
                    int n = sh != null ? sh.GetPropertyCount() : 0;
                    for (int i = 0; i < n; i++)
                    {
                        string pn = sh.GetPropertyName(i);
                        if (pn == null) continue;
                        string up = pn.ToUpperInvariant();
                        if (up.Contains("HSV") || up.Contains("HUE") || up.Contains("SATUR") || up.Contains("TINT") || up.Contains("SHIFT"))
                        {
                            var pt = sh.GetPropertyType(i);
                            if (pt == UnityEngine.Rendering.ShaderPropertyType.Float || pt == UnityEngine.Rendering.ShaderPropertyType.Range)
                                sb.Append(pn).Append('=').Append(mat.GetFloat(pn).ToString("G4")).Append(' ');
                            else if (pt == UnityEngine.Rendering.ShaderPropertyType.Color)
                                sb.Append(pn).Append('=').Append(mat.GetColor(pn).ToString()).Append(' ');
                            else if (pt == UnityEngine.Rendering.ShaderPropertyType.Vector)
                                sb.Append(pn).Append('=').Append(mat.GetVector(pn).ToString()).Append(' ');
                        }
                    }
                    if (log != null) log.Msg("material: donor recolour knobs (keywords left as the donor's): " + (sb.Length > 0 ? sb.ToString() : "none"));
                    if (log != null)
                    {
                        string ka = "?", kb = "?";
                        try { ka = string.Join(" ", mat.shaderKeywords); } catch { }
                        try { kb = string.Join(" ", src.shaderKeywords); } catch { }
                        log.Msg("material: copy keywords [{0}]{1}", ka, ka == kb ? " = donor's" : " DIFFER from donor's [" + kb + "]");
                    }
                }
                catch (Exception ke) { if (log != null) log.Msg("material: recolour scan err " + ke.Message); }

                _donorClone = mat;
                if (_donorMatReports++ < 3 && log != null)
                    log.Msg("material: copied the donor's own material from '{0}' (shader '{1}', queue {2}) "
                          + "and swapped in our three maps. Every render setting is the game's.",
                            from, src.shader.name, mat.renderQueue);
                return mat;
            }
            catch (Exception e)
            {
                if (log != null) log.Warning("material from donor: " + e.Message);
                return null;
            }
        }

        internal static int Rematerialise(Transform ourBody, Transform weaponRoot, MelonLogger.Instance log)
        {
            try
            {
                if (ourBody == null) return 0;
                var mat = MaterialFromDonor(weaponRoot, ourBody, log);
                if (mat == null) return 0;

                int n = 0;
                var rs = ourBody.GetComponentsInChildren<MeshRenderer>(true);
                for (int i = 0; i < rs.Length; i++)
                {
                    var r = rs[i];
                    if (r == null) continue;
                    try { if (r.sharedMaterial != mat) { r.sharedMaterial = mat; n++; } } catch { }
                }
                return n;
            }
            catch { return 0; }
        }

        static Material MakeMaterial(MelonLogger.Instance log)
        {
            var shader = Shader.Find("HDRP/Lit");
            if (shader == null) { log.Warning("HDRP/Lit not found — using fallback."); shader = Shader.Find("Standard"); }
            var m = new Material(shader) { name = "HSR_Body_Runtime" };

            try
            {
                if (m.HasProperty("_SurfaceType")) m.SetFloat("_SurfaceType", 0f);
                if (m.HasProperty("_BlendMode")) m.SetFloat("_BlendMode", 0f);
                if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 1f);
                if (m.HasProperty("_ZTestDepthEqualForOpaque")) m.SetFloat("_ZTestDepthEqualForOpaque", 3f);
                if (m.HasProperty("_AlphaCutoffEnable")) m.SetFloat("_AlphaCutoffEnable", 0f);
                if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", 1f);
                if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", 0f);
                m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.DisableKeyword("_ALPHATEST_ON");
                m.DisableKeyword("_BLENDMODE_ALPHA");
                m.renderQueue = 2000;
            }
            catch (Exception se) { log.Warning("opaque setup: " + se.Message); }

            try
            {
                if (m.HasProperty("_MaterialID")) m.SetFloat("_MaterialID", 1f);
                if (m.HasProperty("_TransmissionEnable")) m.SetFloat("_TransmissionEnable", 0f);
                if (m.HasProperty("_ThicknessMultiplier")) m.SetFloat("_ThicknessMultiplier", 0f);
                if (m.HasProperty("_DiffusionProfileHash")) m.SetFloat("_DiffusionProfileHash", 0f);

                m.DisableKeyword("_MATERIAL_FEATURE_SUBSURFACE_SCATTERING");
                m.DisableKeyword("_MATERIAL_FEATURE_TRANSMISSION");
                m.DisableKeyword("_MATERIAL_FEATURE_ANISOTROPY");
                m.DisableKeyword("_MATERIAL_FEATURE_IRIDESCENCE");
                m.DisableKeyword("_MATERIAL_FEATURE_CLEAR_COAT");
                m.DisableKeyword("_MATERIAL_FEATURE_SPECULAR_COLOR");

                if (m.HasProperty("_DoubleSidedEnable")) m.SetFloat("_DoubleSidedEnable", 0f);
                if (m.HasProperty("_CullMode")) m.SetFloat("_CullMode", 2f);
                if (m.HasProperty("_CullModeForward")) m.SetFloat("_CullModeForward", 2f);
                m.DisableKeyword("_DOUBLESIDED_ON");

                if (m.HasProperty("_RequireSplitLighting")) m.SetFloat("_RequireSplitLighting", 0f);
                if (m.HasProperty("_ReceivesSSR")) m.SetFloat("_ReceivesSSR", 1f);

                if (!ReceiveDecals)
                {
                    if (m.HasProperty("_SupportDecals")) m.SetFloat("_SupportDecals", 0f);
                    m.EnableKeyword("_DISABLE_DECALS");
                }
                if (m.HasProperty("_RenderQueueType")) m.SetFloat("_RenderQueueType", 1f);
                if (m.HasProperty("_StencilRefGBuffer")) m.SetFloat("_StencilRefGBuffer", 2f);
                if (m.HasProperty("_StencilWriteMaskGBuffer")) m.SetFloat("_StencilWriteMaskGBuffer", 3f);
                if (m.HasProperty("_StencilRef")) m.SetFloat("_StencilRef", 0f);
                if (m.HasProperty("_StencilWriteMask")) m.SetFloat("_StencilWriteMask", 6f);

            }
            catch (Exception fe) { log.Warning("lighting model setup: " + fe.Message); }

            try
            {
                var baseTex = LoadTex(Path.Combine(TexDir, "Body_BaseMap.png"), log);
                if (baseTex != null) { m.SetTexture("_BaseColorMap", baseTex); m.SetTexture("_MainTex", baseTex); }

                try
                {
                    float t = Tint;
                    m.SetColor("_BaseColor", new Color(t, t, t, 1f));
                    m.SetColor("_Color", new Color(t, t, t, 1f));
                }
                catch { }
                var mask = LoadTex(Path.Combine(TexDir, "Body_MaskMap.png"), log, linear: true);
                if (mask != null)
                {
                    m.SetTexture("_MaskMap", mask);
                    m.EnableKeyword("_MASKMAP");

                    try
                    {
                        m.SetFloat("_SmoothnessRemapMin", 0f);
                        m.SetFloat("_SmoothnessRemapMax", SmoothnessMax);
                        m.SetFloat("_Smoothness", SmoothnessMax * 0.62f);

                        m.SetFloat("_MetallicRemapMin", 0f);
                        m.SetFloat("_MetallicRemapMax", 1f);
                    }
                    catch (Exception re) { log.Warning("gloss cap: " + re.Message); }
                }
                var nrm = LoadTex(Path.Combine(TexDir, "Body_Normal.png"), log, linear: true);
                if (nrm != null) { m.SetTexture("_NormalMap", nrm); m.EnableKeyword("_NORMALMAP"); }
            }
            catch (Exception e)
            {
                log.Warning("Texture load failed (" + e.GetType().Name + ") — flat dark metal instead.");
                m.SetColor("_BaseColor", new Color(0.16f, 0.16f, 0.17f));
                try { m.SetFloat("_Metallic", 0.6f); m.SetFloat("_Smoothness", 0.45f); } catch { }
            }

            log.Msg("material: shader='{0}' queue={1} MaterialID={2} surface={3} cull={4} "
                  + "blend={5} stencilGB={6}",
                m.shader.name, m.renderQueue, Prop(m, "_MaterialID"), Prop(m, "_SurfaceType"),
                Prop(m, "_CullMode"), Prop(m, "_BlendMode"), Prop(m, "_StencilRefGBuffer"));
            log.Msg("material: keywords = {0}", KeywordList(m));
            return m;
        }

        static Texture2D _flippedNormal;

        static Texture2D FlipGreen(Texture2D src, MelonLogger.Instance log)
        {
            if (_flippedNormal != null) return _flippedNormal;
            try
            {
                var px = src.GetPixels32();
                for (int i = 0; i < px.Length; i++) { var c = px[i]; c.g = (byte)(255 - c.g); px[i] = c; }
                var dst = new Texture2D(src.width, src.height, TextureFormat.RGBA32, true, true);
                dst.name = src.name + "_gflip";
                dst.SetPixels32(px);
                dst.Apply(true, false);
                dst.wrapMode = src.wrapMode; dst.filterMode = src.filterMode; dst.anisoLevel = src.anisoLevel;
                _flippedNormal = dst;
                if (log != null) log.Msg("material: normal map green channel flipped for the donor's Unreal-style decode ({0}x{1}).", src.width, src.height);
                return dst;
            }
            catch (Exception e)
            {
                if (log != null) log.Msg("material: green flip failed, using the map as is: " + e.Message);
                return src;
            }
        }

        static Texture2D LoadTex(string path, MelonLogger.Instance log, bool linear = false)
        {
            if (!File.Exists(path)) return null;
            var bytes = File.ReadAllBytes(path);
            var il2cppBytes = new Il2CppStructArray<byte>(bytes.Length);
            for (int i = 0; i < bytes.Length; i++) il2cppBytes[i] = bytes[i];
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear);
            if (!ImageConversion.LoadImage(tex, il2cppBytes)) { log.Warning("LoadImage failed: " + Path.GetFileName(path)); return null; }

            try
            {
                tex.Compress(false);
                tex.Apply(false, true);
            }
            catch (Exception e) { log.Warning("compress failed for " + Path.GetFileName(path) + ": " + e.Message); }

            return tex;
        }
    }
}
