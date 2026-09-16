// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace MapLoader
{

    public static class MapWater
    {
        static MelonLogger.Instance _log;
        public static void Init(MelonLogger.Instance log) { _log = log; }

        static WaterSurface _ocean;
        static MeshRenderer _oceanSlab;

        static void PlaceOcean()
        {
            try
            {
                var root = MapRuntime.Root;
                if (root == null) return;
                float sea = MapIdentity.SeaLevelY;

                MeshRenderer slab = null; string slabName = "(none)";
                var t = root.transform;
                for (int i = 0; i < t.childCount; i++)
                {
                    var c = t.GetChild(i);
                    string n = null;
                    try { n = c.name; } catch { }
                    if (n == null || n.IndexOf("_Ocean_", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var mr = c.GetComponent<MeshRenderer>();
                    if (mr != null) { slab = mr; slabName = n; break; }
                }

                bool supportKnown = false, support = false;
                try
                {
                    var asset = HDRenderPipeline.currentAsset;
                    if (asset != null)
                    {
                        support = asset.currentPlatformRenderPipelineSettings.supportWater;
                        supportKnown = true;
                    }
                }
                catch { }
                if (supportKnown && !support)
                {
                    _log?.Warning("[water] the HDRP asset says supportWater=FALSE; the sea keeps "
                                + "the static quad");
                    return;
                }

                var go = new GameObject("ODG_Ocean");
                go.transform.SetParent(root.transform, false);
                go.transform.position = new Vector3(0f, sea, 0f);
                var ws = go.AddComponent<WaterSurface>();
                if (ws == null)
                {
                    _log?.Warning("[water] AddComponent<WaterSurface> returned null; the sea keeps "
                                + "the static quad");
                    UnityEngine.Object.Destroy(go);
                    return;
                }
                ws.surfaceType = WaterSurfaceType.OceanSeaLake;
                ws.geometryType = WaterGeometryType.Infinite;
                ws.timeMultiplier = 1f;

                try { ws.largeWindSpeed = OceanWindKmh; } catch { }
                try { ws.largeChaos = 0.8f; } catch { }
                try { ws.repetitionSize = 500f; } catch { }
                try { ws.largeBand0Multiplier = 1f; ws.largeBand1Multiplier = 1f; } catch { }
                try { ws.ripples = true; ws.ripplesWindSpeed = 8f; ws.ripplesChaos = 0.8f; } catch { }

                try { ws.scatteringColor = OceanScatter; } catch { }
                try { ws.refractionColor = new Color(0.00f, 0.40f, 0.72f, 1f); } catch { }
                try { ws.absorptionDistance = OceanAbsorbM; } catch { }
                try { ws.startSmoothness = 0.97f; ws.endSmoothness = 0.88f; } catch { }
                try { ws.foam = true; ws.simulationFoamAmount = 0.25f; } catch { }
                try { ws.foamPersistenceMultiplier = 0.5f; } catch { }
                try { ws.caustics = false; } catch { }
                try { ws.underWater = false; } catch { }

                try
                {
                    var vgo = new GameObject("ODG_OceanVolume");
                    vgo.transform.SetParent(root.transform, false);
                    var vol = vgo.AddComponent<UnityEngine.Rendering.Volume>();
                    vol.isGlobal = true;
                    vol.priority = MapRuntime.OwnVolumePriority();
                    vol.weight = 1f;
                    var profile = ScriptableObject.CreateInstance(
                        Il2CppInterop.Runtime.Il2CppType.Of<UnityEngine.Rendering.VolumeProfile>())
                        .Cast<UnityEngine.Rendering.VolumeProfile>();
                    vol.sharedProfile = profile;
                    var added = profile.Add(Il2CppInterop.Runtime.Il2CppType.Of<WaterRendering>(), false);
                    var wr = added != null ? added.TryCast<WaterRendering>() : null;
                    if (wr != null)
                    {
                        wr.active = true;
                        wr.enable.overrideState = true;
                        wr.enable.value = true;
                    }
                    else _log?.Warning("[water] no WaterRendering override on this build");
                }
                catch (Exception e) { _log?.Warning("[water] WaterRendering volume: " + e.Message); }

                if (slab != null) slab.enabled = false;
                _ocean = ws; _oceanSlab = slab;

                int count = -1;
                try { count = WaterSurface.instanceCount; } catch { }
                _log?.Msg("[water] HDRP OCEAN surface standing at y=" + sea.ToString("F2")
                        + " (infinite, wind " + OceanWindKmh.ToString("F0") + " km/h); the static "
                        + "quad " + slabName + " is " + (slab != null ? "hidden, collider kept"
                        : "not under the root, nothing hidden") + "; registered surfaces=" + count
                        + (supportKnown ? ", supportWater=true" : ", support unreadable")
                        + ". odg water wind <kmh> / odg water off|on retune it live.");
            }
            catch (Exception e) { _log?.Warning("[water] PlaceOcean: " + e.Message); }
        }

        internal static float OceanWindKmh = 22f;
        internal static Color OceanScatter = new Color(0.00f, 0.22f, 0.52f, 1f);
        internal static float OceanAbsorbM = 12f;

        internal static string Console(string arg)
        {
            arg = (arg ?? "").Trim().ToLowerInvariant();
            if (_ocean == null) return "no ocean surface is standing on this map";
            try
            {
                if (arg == "off")
                {
                    _ocean.gameObject.SetActive(false);
                    if (_oceanSlab != null) _oceanSlab.enabled = true;
                    return "ocean surface OFF; the static quad is back";
                }
                if (arg == "on")
                {
                    _ocean.gameObject.SetActive(true);
                    if (_oceanSlab != null) _oceanSlab.enabled = false;
                    return "ocean surface ON";
                }
                if (arg.StartsWith("scatter"))
                {
                    var p = arg.Substring(7).Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    float r, g, b;
                    if (p.Length == 3 && float.TryParse(p[0], out r) && float.TryParse(p[1], out g)
                        && float.TryParse(p[2], out b))
                    {
                        OceanScatter = new Color(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b), 1f);
                        _ocean.scatteringColor = OceanScatter;
                        return "ocean scattering = " + r.ToString("F2") + " " + g.ToString("F2") + " "
                             + b.ToString("F2") + " (deep-water colour)";
                    }
                    return "odg water scatter <r> <g> <b>   0..1 each (now "
                         + OceanScatter.r.ToString("F2") + " " + OceanScatter.g.ToString("F2") + " "
                         + OceanScatter.b.ToString("F2") + "). Try 0 0.27 0.23 (HDRP default).";
                }
                if (arg.StartsWith("abs"))
                {
                    float m;
                    if (float.TryParse(arg.Substring(3).Trim(), out m))
                    {
                        OceanAbsorbM = Mathf.Clamp(m, 0.1f, 100f);
                        _ocean.absorptionDistance = OceanAbsorbM;
                        return "ocean absorption distance = " + OceanAbsorbM.ToString("F1") + " m";
                    }
                    return "odg water abs <metres>   (now " + OceanAbsorbM.ToString("F1") + ")";
                }
                if (arg.StartsWith("wind"))
                {
                    float k;
                    if (float.TryParse(arg.Substring(4).Trim(), out k))
                    {
                        OceanWindKmh = Mathf.Clamp(k, 0f, 200f);
                        _ocean.largeWindSpeed = OceanWindKmh;
                        return "ocean wind = " + OceanWindKmh.ToString("F0") + " km/h";
                    }
                    return "odg water wind <km/h>   (now " + OceanWindKmh.ToString("F0") + ")";
                }
            }
            catch (Exception e) { return "water: " + e.Message; }
            return "odg water on|off | wind <km/h> | scatter <r> <g> <b> | abs <m>";
        }

        public static void Place()
        {
            try
            {
                _ocean = null; _oceanSlab = null;
                if (MapIdentity.OceanWater) PlaceOcean();
                if (!MapIdentity.PoolWater) return;
                var root = MapRuntime.Root;
                if (root == null) return;

                Transform slab = null;
                var t = root.transform;
                for (int i = 0; i < t.childCount; i++)
                {
                    var c = t.GetChild(i);
                    string n = null;
                    try { n = c.name; } catch { }
                    if (n != null && n.IndexOf("_pool", StringComparison.OrdinalIgnoreCase) >= 0)
                    { slab = c; break; }
                }
                if (slab == null)
                {
                    _log?.Msg("[water] no pool slab under the map root; nothing to dress");
                    return;
                }
                var mr = slab.GetComponent<MeshRenderer>() ?? slab.GetComponentInChildren<MeshRenderer>();
                if (mr == null)
                {
                    _log?.Warning("[water] pool slab has no MeshRenderer; leaving it as built");
                    return;
                }
                var b = mr.bounds;

                bool supportKnown = false, support = false;
                try
                {
                    var asset = HDRenderPipeline.currentAsset;
                    if (asset != null)
                    {
                        support = asset.currentPlatformRenderPipelineSettings.supportWater;
                        supportKnown = true;
                    }
                }
                catch { }
                if (supportKnown && !support)
                {
                    _log?.Warning("[water] the HDRP asset says supportWater=FALSE; the game's "
                                + "water system cannot render here, keeping the static slab");
                    return;
                }

                var go = new GameObject("ODG_PoolWater");
                go.transform.SetParent(root.transform, false);
                go.transform.position = new Vector3(b.center.x, b.max.y - 0.03f, b.center.z);

                go.transform.localScale = new Vector3(b.size.x + 0.25f, 1f, b.size.z + 0.25f);

                var ws = go.AddComponent<WaterSurface>();
                if (ws == null)
                {
                    _log?.Warning("[water] AddComponent<WaterSurface> returned null; keeping the slab");
                    UnityEngine.Object.Destroy(go);
                    return;
                }
                ws.surfaceType = WaterSurfaceType.Pool;
                ws.geometryType = WaterGeometryType.Quad;
                ws.timeMultiplier = 0.9f;

                ws.scatteringColor = new Color(0.06f, 0.32f, 0.42f, 1f);
                try { ws.refractionColor = new Color(0.12f, 0.48f, 0.56f, 1f); } catch { }
                try { ws.absorptionDistance = 2.5f; } catch { }
                try { ws.caustics = true; ws.causticsIntensity = 0.35f; } catch { }

                mr.enabled = false;

                int count = -1;
                try { count = WaterSurface.instanceCount; } catch { }
                _log?.Msg("[water] HDRP POOL surface placed at ("
                        + b.center.x.ToString("F1") + ", " + (b.max.y - 0.03f).ToString("F2")
                        + ", " + b.center.z.ToString("F1") + "), "
                        + (b.size.x + 0.25f).ToString("F1") + " x " + (b.size.z + 0.25f).ToString("F1")
                        + " m; registered surfaces=" + count
                        + (supportKnown ? ", supportWater=true"
                                        : ", support unreadable (if the pool is EMPTY in play, "
                                        + "this is why; the slab renderer was disabled on '"
                                        + slab.name + "')"));
            }
            catch (Exception e) { _log?.Warning("[water] Place: " + e.Message); }
        }
    }
}
