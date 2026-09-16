// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Text;
using MelonLoader;
using UnityEngine;

namespace MapLoader
{

    public static class LookProbe
    {
        static MelonLogger.Instance _log;
        public static void Init(MelonLogger.Instance log) { _log = log; }

        static readonly Dictionary<int, bool> _uvFlat = new Dictionary<int, bool>();

        static bool UvIsOnePoint(Mesh msh)
        {
            if (msh == null) return false;
            int id;
            try { id = msh.GetInstanceID(); } catch { return false; }
            bool v;
            if (_uvFlat.TryGetValue(id, out v)) return v;
            v = false;
            try
            {

                if (msh.isReadable && msh.vertexCount > 2)
                {
                    var uvs = msh.uv;
                    if (uvs != null && uvs.Length > 2)
                    {
                        var u0 = uvs[0];
                        var u1 = uvs[uvs.Length / 2];
                        var u2 = uvs[uvs.Length - 1];

                        v = u0.x == u1.x && u0.y == u1.y
                         && u1.x == u2.x && u1.y == u2.y;
                    }
                }
            }
            catch { v = false; }
            _uvFlat[id] = v;
            return v;
        }

        public static void ScanSuspicious()
        {
            try
            {

                var rends = UnityEngine.Object.FindObjectsOfType<Renderer>();

                var groups = new Dictionary<string, int>(StringComparer.Ordinal);
                var example = new Dictionary<string, string>(StringComparer.Ordinal);
                int hitCount = 0;
                _uvFlat.Clear();

                for (int i = 0; i < rends.Length; i++)
                {
                    var r = rends[i];
                    if (r == null) continue;

                    try
                    {
                        if (!r.enabled || r.forceRenderingOff) continue;
                        if (!r.gameObject.activeInHierarchy) continue;
                    }
                    catch { continue; }

                    var b = r.bounds;

                    bool flatUv = false;
                    try
                    {
                        var mf = r.gameObject.GetComponent<MeshFilter>();
                        flatUv = UvIsOnePoint(mf != null ? mf.sharedMesh : null);
                    }
                    catch { }

                    var mats = r.sharedMaterials;
                    if (mats == null) continue;
                    for (int k = 0; k < mats.Length; k++)
                    {
                        var m = mats[k];
                        if (m == null) continue;

                        string why = null;
                        string shader = "?";
                        try { shader = m.shader != null ? m.shader.name : "<NO SHADER>"; } catch { }

                        if (shader.IndexOf("Unlit", StringComparison.OrdinalIgnoreCase) >= 0)
                            why = "UNLIT SHADER";

                        if (why == null && flatUv)
                            why = "DEGENERATE UVs (one texel over the whole mesh)";

                        if (why == null)
                        {
                            try
                            {
                                if (m.HasProperty("_EmissiveColor"))
                                {
                                    var e = m.GetColor("_EmissiveColor");
                                    if (e.r + e.g + e.b > 0.05f)
                                        why = "EMISSIVE " + e.ToString("F2");
                                }
                            }
                            catch { }
                        }

                        Color bc = Color.white; bool haveCol = false;
                        try
                        {
                            if (m.HasProperty("_BaseColor")) { bc = m.GetColor("_BaseColor"); haveCol = true; }
                            else if (m.HasProperty("_UnlitColor")) { bc = m.GetColor("_UnlitColor"); haveCol = true; }
                            else if (m.HasProperty("_Color")) { bc = m.GetColor("_Color"); haveCol = true; }
                        }
                        catch { }
                        if (why == null && haveCol
                            && bc.r < 0.35f && bc.g > 0.25f && bc.b > 0.25f
                            && Math.Abs(bc.g - bc.b) < 0.30f)
                            why = "TEAL BASE COLOUR " + bc.ToString("F2");

                        if (why == null)
                        {
                            bool hasBase = false;
                            try
                            {
                                hasBase = (m.HasProperty("_BaseColorMap") && m.GetTexture("_BaseColorMap") != null)
                                       || (m.HasProperty("_UnlitColorMap") && m.GetTexture("_UnlitColorMap") != null)
                                       || (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null);
                            }
                            catch { }
                            if (!hasBase) why = "NO BASE TEXTURE";
                        }

                        if (why == null) continue;

                        hitCount++;
                        bool ours = false;
                        try { ours = MapRuntime.Root != null && r.transform.IsChildOf(MapRuntime.Root.transform); }
                        catch { }

                        string key = why + "  |  '" + r.gameObject.name + "'  mat='" + m.name
                                   + "'  " + (ours ? "OURS" : "HOST");
                        int c; groups.TryGetValue(key, out c);

                        if (c == 0 && groups.Count >= 2000) continue;
                        groups[key] = c + 1;
                        if (!example.ContainsKey(key))
                            example[key] = "shader=" + shader + "  size=" + b.size.ToString("F2")
                                         + "  at " + b.center.ToString("F1")
                                         + "  colour=" + (haveCol ? bc.ToString("F2") : "-")
                                         + "  path=" + PathOf(r.transform);
                    }
                }

                if (hitCount == 0)
                {
                    _log?.Msg("[look] scan: nothing on screen is unlit, emissive, teal, "
                            + "untextured or on a single UV");
                    return;
                }

                var sb = new StringBuilder();
                sb.Append('\n').Append("===== SURFACES THAT IGNORE THE LIGHTING (")
                  .Append(hitCount).Append(" renderer-materials) =====").Append('\n');
                var keys = new List<string>(groups.Keys);
                keys.Sort((a, b2) => groups[b2].CompareTo(groups[a]));
                for (int i = 0; i < keys.Count && i < 15; i++)
                    sb.Append("   x").Append(groups[keys[i]]).Append("  ").Append(keys[i])
                      .Append('\n').Append("        ").Append(example[keys[i]]).Append('\n');
                sb.Append("=================================================");
                _log?.Msg(sb.ToString());
            }
            catch (Exception e) { _log?.Warning("[look] scan: " + e.Message); }
        }

        public static void Fire()
        {
            try
            {
                var cam = Camera.main;
                if (cam == null) { _log?.Msg("[look] no camera"); return; }

                Vector3 eye = cam.transform.position;
                Ray ray = new Ray(eye, cam.transform.forward);

                var rends = UnityEngine.Object.FindObjectsOfType<Renderer>();
                var hits = new List<KeyValuePair<float, Renderer>>();
                for (int i = 0; i < rends.Length; i++)
                {
                    var r = rends[i];
                    if (r == null) continue;
                    float dist;
                    if (!r.bounds.IntersectRay(ray, out dist)) continue;
                    if (dist > 60f) continue;
                    hits.Add(new KeyValuePair<float, Renderer>(dist, r));
                }
                hits.Sort((a, b) => a.Key.CompareTo(b.Key));

                if (hits.Count == 0)
                {
                    _log?.Msg("[look] nothing of ours under the crosshair within 60 m");
                    return;
                }

                var sb = new StringBuilder();
                sb.Append("\n===== LOOKING AT (nearest ").Append(Math.Min(6, hits.Count))
                  .Append(" of ").Append(hits.Count).Append(") =====\n");

                for (int i = 0; i < hits.Count && i < 6; i++)
                {
                    var r = hits[i].Value;
                    var go = r.gameObject;
                    sb.Append('[').Append(i).Append("] ").Append(hits[i].Key.ToString("F2"))
                      .Append(" m   name='").Append(go.name).Append("'\n");

                    string scene = "?";
                    try { scene = go.scene.IsValid() ? go.scene.name : "<prefab>"; } catch { }
                    bool ours = false;
                    try { ours = MapRuntime.Root != null && go.transform.IsChildOf(MapRuntime.Root.transform); } catch { }
                    sb.Append("      scene='").Append(scene).Append("'  ")
                      .Append(ours ? "OURS" : "HOST / not ours")
                      .Append("  type=").Append(r.GetType().Name)
                      .Append("  enabled=").Append(r.enabled)
                      .Append("  forcedOff=").Append(SafeForced(r)).Append("\n");
                    sb.Append("      path=").Append(PathOf(go.transform)).Append("\n");

                    var b = r.bounds;
                    sb.Append("      size=").Append(b.size)
                      .Append(" centre=").Append(b.center)
                      .Append(" flat=").Append(b.size.y < 0.06f ? "YES" : "no").Append('\n');

                    try
                    {
                        var mf = go.GetComponent<MeshFilter>();
                        var mesh = mf != null ? mf.sharedMesh : null;
                        sb.Append("      mesh='").Append(mesh != null ? mesh.name : "<none>")
                          .Append("' verts=").Append(mesh != null ? mesh.vertexCount : 0)
                          .Append(" collider=")
                          .Append(go.GetComponent<Collider>() != null ? "yes" : "NO").Append('\n');
                    }
                    catch { }

                    try
                    {
                        var mats = r.sharedMaterials;
                        sb.Append("      materials=").Append(mats == null ? 0 : mats.Length).Append('\n');
                        if (mats != null)
                            for (int k = 0; k < mats.Length && k < 3; k++)
                            {
                                var m = mats[k];
                                if (m == null) { sb.Append("        [").Append(k).Append("] NULL MATERIAL\n"); continue; }
                                sb.Append("        [").Append(k).Append("] '").Append(m.name)
                                  .Append("' shader='")
                                  .Append(m.shader != null ? m.shader.name : "<NO SHADER>").Append("'\n");
                                sb.Append("            colour=").Append(SafeColour(m))
                                  .Append(" smooth=").Append(SafeFloat(m, "_Smoothness"))
                                  .Append(" metal=").Append(SafeFloat(m, "_Metallic")).Append('\n');
                                sb.Append("            baseMap=").Append(TexName(m, "_BaseColorMap"))
                                  .Append(" mainTex=").Append(TexName(m, "_MainTex")).Append('\n');
                                sb.Append("            normal=").Append(TexName(m, "_NormalMap"))
                                  .Append(" mask=").Append(TexName(m, "_MaskMap")).Append('\n');
                            }
                    }
                    catch (Exception me) { sb.Append("      materials threw: ").Append(me.Message).Append('\n'); }
                }
                sb.Append("=================================");
                _log?.Msg(sb.ToString());
            }
            catch (Exception e) { _log?.Error("[look] " + e.Message); }
        }

        static string SafeForced(Renderer r)
        { try { return r.forceRenderingOff.ToString(); } catch { return "?"; } }

        static string PathOf(Transform t)
        {
            try
            {
                var parts = new List<string>();
                var cur = t; int guard = 0;
                while (cur != null && guard++ < 12) { parts.Add(cur.name); cur = cur.parent; }
                parts.Reverse();
                return string.Join("/", parts.ToArray());
            }
            catch { return "?"; }
        }

        static string SafeColour(Material m)
        {
            try
            {
                if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor").ToString();
                if (m.HasProperty("_Color")) return m.GetColor("_Color").ToString();
            }
            catch { }
            return "?";
        }

        static string SafeFloat(Material m, string prop)
        {
            try { if (m.HasProperty(prop)) return m.GetFloat(prop).ToString("F2"); }
            catch { }
            return "-";
        }

        static string TexName(Material m, string prop)
        {
            try
            {
                if (!m.HasProperty(prop)) return "n/a";
                var t = m.GetTexture(prop);
                return t == null ? "<EMPTY>" : t.name;
            }
            catch { return "?"; }
        }
    }
}
