// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using UnityEngine;
using HD = UnityEngine.Rendering.HighDefinition;

namespace MapLoader
{

    public static class AaDials
    {
        static List<HD.HDAdditionalCameraData> Live(out List<string> names)
        {
            var list = new List<HD.HDAdditionalCameraData>();
            names = new List<string>();
            try
            {
                var cams = Camera.allCameras;
                for (int i = 0; i < (cams != null ? cams.Length : 0); i++)
                {
                    var c = cams[i];
                    if (c == null) continue;
                    try { if (!c.enabled || !c.gameObject.activeInHierarchy) continue; } catch { continue; }
                    var hd = c.GetComponent<HD.HDAdditionalCameraData>();
                    if (hd == null) continue;
                    list.Add(hd);
                    string n = "?"; try { n = c.name; } catch { }
                    names.Add(n);
                }
            }
            catch { }
            return list;
        }

        public static string Describe()
        {
            List<string> names;
            var hds = Live(out names);
            if (hds.Count == 0) return "no live camera with HDRP data";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < hds.Count; i++)
            {
                var hd = hds[i];
                string mode = "?", q = "?", sharp = "?", flick = "?", mvrej = "?", dyn = "?";
                try { mode = hd.antialiasing.ToString(); } catch { }
                try { q = hd.TAAQuality.ToString(); } catch { }
                try { sharp = hd.taaSharpenStrength.ToString("F2"); } catch { }
                try { flick = hd.taaAntiFlicker.ToString("F2"); } catch { }
                try { mvrej = hd.taaMotionVectorRejection.ToString("F2"); } catch { }
                try { dyn = hd.allowDynamicResolution.ToString(); } catch { }
                string dlss = "?", fsr2 = "?";
                try { dlss = hd.allowDeepLearningSuperSampling.ToString(); } catch { }
                try { fsr2 = hd.allowFidelityFX2SuperResolution.ToString(); } catch { }
                sb.Append(i > 0 ? "\n" : "").Append("[").Append(names[i]).Append("] aa=").Append(mode)
                  .Append(" taaQuality=").Append(q).Append(" sharpen=").Append(sharp)
                  .Append(" antiFlicker=").Append(flick).Append(" mvReject=").Append(mvrej)
                  .Append(" dynRes=").Append(dyn).Append(" dlss=").Append(dlss).Append(" fsr2=").Append(fsr2);
            }

            try
            {
                var drs = UnityEngine.Rendering.DynamicResolutionHandler.instance;
                if (drs != null)
                {
                    string scale = "?", filt = "?";
                    try { scale = drs.GetCurrentScale().ToString("F2"); } catch { }
                    try { filt = drs.filter.ToString(); } catch { }
                    sb.Append("\n[dynamic resolution] scale=").Append(scale).Append(" filter=").Append(filt);
                }
            }
            catch { }
            return sb.ToString();
        }

        public static bool AssertTaa(bool on, int quality, float sharpen, float antiFlicker,
                                     out string readback)
        {
            bool changed = false;
            readback = "";
            List<string> names; var hds = Live(out names);
            for (int i = 0; i < hds.Count; i++)
            {
                var hd = hds[i];
                try
                {
                    int want = on ? 2 : 0;
                    if ((int)hd.antialiasing != want)
                    {
                        hd.antialiasing = (HD.HDAdditionalCameraData.AntialiasingMode)want;
                        changed = true;
                    }
                    if (on)
                    {
                        if ((int)hd.TAAQuality != quality)
                        { hd.TAAQuality = (HD.HDAdditionalCameraData.TAAQualityLevel)quality; changed = true; }
                        if (Mathf.Abs(hd.taaSharpenStrength - sharpen) > 0.01f)
                        { hd.taaSharpenStrength = sharpen; changed = true; }
                        if (Mathf.Abs(hd.taaAntiFlicker - antiFlicker) > 0.01f)
                        { hd.taaAntiFlicker = antiFlicker; changed = true; }
                    }
                    if (i == 0)
                        readback = "[" + names[i] + "] aa=" + hd.antialiasing + " quality=" + hd.TAAQuality
                                 + " sharpen=" + hd.taaSharpenStrength.ToString("F2")
                                 + " antiFlicker=" + hd.taaAntiFlicker.ToString("F2");
                }
                catch (Exception e) { readback = "write failed: " + e.Message; }
            }
            return changed;
        }

        public static string SetSharpen(float v)
        {
            v = Mathf.Clamp(v, 0f, 2f);
            List<string> names; var hds = Live(out names);
            int n = 0; string rb = "?";
            for (int i = 0; i < hds.Count; i++)
            {
                try { hds[i].taaSharpenStrength = v; rb = hds[i].taaSharpenStrength.ToString("F2"); n++; }
                catch { }
            }
            return "TAA sharpen = " + v.ToString("F2") + " on " + n + " camera(s) (readback " + rb
                 + "). If the game's settings put it back, it will read differently next time.";
        }

        public static string SetAntiFlicker(float v)
        {
            v = Mathf.Clamp01(v);
            List<string> names; var hds = Live(out names);
            int n = 0; string rb = "?";
            for (int i = 0; i < hds.Count; i++)
            {
                try { hds[i].taaAntiFlicker = v; rb = hds[i].taaAntiFlicker.ToString("F2"); n++; }
                catch { }
            }
            return "TAA anti-flicker = " + v.ToString("F2") + " on " + n + " camera(s) (readback " + rb + ")";
        }

        public static string SetMode(string m)
        {
            int mode;
            switch ((m ?? "").ToLowerInvariant())
            {
                case "none": case "off": mode = 0; break;
                case "fxaa": mode = 1; break;
                case "taa": mode = 2; break;
                case "smaa": mode = 3; break;
                default: return "odg aa mode taa|smaa|fxaa|none";
            }
            List<string> names; var hds = Live(out names);
            int n = 0; string rb = "?";
            for (int i = 0; i < hds.Count; i++)
            {
                try
                {
                    hds[i].antialiasing = (HD.HDAdditionalCameraData.AntialiasingMode)mode;
                    rb = hds[i].antialiasing.ToString(); n++;
                }
                catch { }
            }
            return "anti-aliasing = " + m + " on " + n + " camera(s) (readback " + rb
                 + "). SMAA has no history, so if the grain is temporal it goes now and the "
                 + "edges get crunchier instead.";
        }

        public static string SetQuality(string q)
        {
            int lvl;
            switch ((q ?? "").ToLowerInvariant())
            {
                case "low": lvl = 0; break;
                case "med": case "medium": lvl = 1; break;
                case "high": lvl = 2; break;
                default: return "odg aa quality low|medium|high";
            }
            List<string> names; var hds = Live(out names);
            int n = 0; string rb = "?";
            for (int i = 0; i < hds.Count; i++)
            {
                try
                {
                    hds[i].TAAQuality = (HD.HDAdditionalCameraData.TAAQualityLevel)lvl;
                    rb = hds[i].TAAQuality.ToString(); n++;
                }
                catch { }
            }
            return "TAA quality = " + q + " on " + n + " camera(s) (readback " + rb + ")";
        }
    }
}
