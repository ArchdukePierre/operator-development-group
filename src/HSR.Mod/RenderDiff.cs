// ODG Operations - HSR
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Text;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

namespace HSR
{

    internal static class RenderDiff
    {
        static bool _done;

        static readonly string[] Passes =
        {
            "GBuffer", "DepthOnly", "DepthForwardOnly", "Forward", "ForwardOnly", "MotionVectors",
            "TransparentDepthPrepass", "TransparentDepthPostpass", "TransparentBackface", "ShadowCaster",
            "META", "DistortionVectors", "RayTracingPrepass", "FullScreenDebug", "SceneSelectionPass",
        };

        internal static void ResetForScene() { }

        internal static void Once(Renderer ours, Renderer donor, MelonLogger.Instance log)
        {
            if (_done || ours == null || donor == null || log == null) return;
            _done = true;
            try
            {
                var sb = new StringBuilder(4096);
                sb.Append("RENDERDIFF: ours '").Append(ours.gameObject.name).Append("' vs donor '").Append(donor.gameObject.name).Append("'\n");
                sb.Append("   renderer  ").Append(RendLine(ours)).Append('\n');
                sb.Append("   renderer  ").Append(RendLine(donor)).Append('\n');

                Material a = null, b = null;
                try { a = ours.sharedMaterial; } catch { }
                try { b = donor.sharedMaterial; } catch { }
                if (a == null || b == null) { sb.Append("   a material is null (ours=").Append(a != null).Append(" donor=").Append(b != null).Append(")"); log.Msg(sb.ToString()); return; }

                sb.Append("   material  ours  shader='").Append(a.shader != null ? a.shader.name : "?").Append("' queue=").Append(a.renderQueue)
                  .Append(" keywords=[").Append(Keywords(a)).Append("]\n");
                sb.Append("   material  donor shader='").Append(b.shader != null ? b.shader.name : "?").Append("' queue=").Append(b.renderQueue)
                  .Append(" keywords=[").Append(Keywords(b)).Append("]\n");

                sb.Append("   passes    ");
                for (int i = 0; i < Passes.Length; i++)
                {
                    bool pa = true, pb = true; bool ok = true;
                    try { pa = a.GetShaderPassEnabled(Passes[i]); pb = b.GetShaderPassEnabled(Passes[i]); } catch { ok = false; }
                    if (!ok) continue;
                    if (pa != pb) sb.Append(Passes[i]).Append(" ours=").Append(pa ? "on" : "off").Append(" donor=").Append(pb ? "on" : "off").Append("  ");
                }
                sb.Append("(only passes that differ are listed)\n");

                int diffs = 0;
                var seen = new HashSet<string>();
                if (a.shader != null)
                {
                    int n = 0; try { n = a.shader.GetPropertyCount(); } catch { }
                    for (int i = 0; i < n; i++)
                    {
                        string name; ShaderPropertyType type;
                        try { name = a.shader.GetPropertyName(i); type = a.shader.GetPropertyType(i); } catch { continue; }
                        if (string.IsNullOrEmpty(name)) continue;
                        seen.Add(name);
                        if (!b.HasProperty(name)) { sb.Append("   only-ours ").Append(name).Append('\n'); continue; }
                        try
                        {
                            switch (type)
                            {
                                case ShaderPropertyType.Float:
                                case ShaderPropertyType.Range:
                                {
                                    float va = a.GetFloat(name), vb = b.GetFloat(name);
                                    if (Mathf.Abs(va - vb) > 1e-5f) { sb.Append("   diff ").Append(name).Append(" ours=").Append(va.ToString("G6")).Append(" donor=").Append(vb.ToString("G6")).Append('\n'); diffs++; }
                                    break;
                                }
                                case ShaderPropertyType.Int:
                                {
                                    int va = a.GetInt(name), vb = b.GetInt(name);
                                    if (va != vb) { sb.Append("   diff ").Append(name).Append(" ours=").Append(va).Append(" donor=").Append(vb).Append('\n'); diffs++; }
                                    break;
                                }
                                case ShaderPropertyType.Color:
                                {
                                    Color va = a.GetColor(name), vb = b.GetColor(name);
                                    if (Mathf.Abs(va.a - vb.a) > 1e-3f) { sb.Append("   diff ").Append(name).Append(".a ours=").Append(va.a.ToString("F3")).Append(" donor=").Append(vb.a.ToString("F3")).Append('\n'); diffs++; }
                                    break;
                                }
                                case ShaderPropertyType.Texture:
                                {
                                    bool ta = a.GetTexture(name) != null, tb = b.GetTexture(name) != null;
                                    if (ta != tb) { sb.Append("   tex  ").Append(name).Append(" ours=").Append(ta ? "set" : "none").Append(" donor=").Append(tb ? "set" : "none").Append('\n'); }
                                    break;
                                }
                            }
                        }
                        catch { }
                    }
                }
                if (b.shader != null && (a.shader == null || b.shader.name != a.shader.name))
                {
                    int n = 0; try { n = b.shader.GetPropertyCount(); } catch { }
                    for (int i = 0; i < n; i++)
                    {
                        string name; try { name = b.shader.GetPropertyName(i); } catch { continue; }
                        if (string.IsNullOrEmpty(name) || seen.Contains(name)) continue;
                        sb.Append("   only-donor ").Append(name).Append('\n');
                    }
                }
                sb.Append("   ").Append(diffs).Append(" numeric difference(s)");
                log.Msg(sb.ToString());
            }
            catch (Exception e) { log.Msg("RENDERDIFF: failed: " + e.Message); }
        }

        static string RendLine(Renderer r)
        {
            var sb = new StringBuilder();
            try { sb.Append("layer=").Append(r.gameObject.layer).Append('(').Append(LayerMask.LayerToName(r.gameObject.layer)).Append(')'); } catch { }
            try { sb.Append(" renderingLayerMask=0x").Append(r.renderingLayerMask.ToString("X")); } catch { }
            try { sb.Append(" priority=").Append(r.rendererPriority); } catch { }
            try { sb.Append(" sortingOrder=").Append(r.sortingOrder); } catch { }
            try { sb.Append(" shadows=").Append(r.shadowCastingMode); } catch { }
            try { sb.Append(" receiveShadows=").Append(r.receiveShadows); } catch { }
            try { sb.Append(" motionVectors=").Append(r.motionVectorGenerationMode); } catch { }
            try { sb.Append(" probes=").Append(r.lightProbeUsage).Append('/').Append(r.reflectionProbeUsage); } catch { }
            try { sb.Append(" enabled=").Append(r.enabled).Append(" fro=").Append(r.forceRenderingOff); } catch { }
            try { sb.Append(" type=").Append(r.GetIl2CppType().Name); } catch { }
            return sb.ToString();
        }

        static string Keywords(Material m)
        {
            try
            {
                var k = m.shaderKeywords;
                if (k == null || k.Length == 0) return "";
                return string.Join(" ", k);
            }
            catch { return "?"; }
        }
    }
}
