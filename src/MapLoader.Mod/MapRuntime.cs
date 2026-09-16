// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MapLoader
{

    public static class MapRuntime
    {

        public static string SceneName { get { return MapIdentity.SceneName; } }
        public static string RootName  { get { return MapIdentity.RootName;  } }

        public static string BundleDir { get { return MapIdentity.BundleDir; } }

        public static string ResolveBundleDir()
        {
            try
            {
                var root = MelonLoader.Utils.MelonEnvironment.GameRootDirectory;
                if (!string.IsNullOrEmpty(root))
                {
                    var shipped = Path.Combine(root, "Mods", "MapLoader_Assets", "Farmstead");
                    if (Directory.Exists(shipped)) return shipped;
                }
            }
            catch { }
            return @"D:\GB_work\bundle\Farmstead";
        }

        public static int BudgetMs = 45;
        public static int FoliageMax = 6000;

        static MelonLogger.Instance _log;
        static Scene _scene;

        static Scene _prevActive;
        static GameObject _root;
        static MapBundle.Manifest _manifest;
        static int _meshIndex, _instanceIndex, _placed;

        static int _rendOptDone, _rendOptFail;

        static float _buildStartedAt;
        public static float BuildSeconds { get; private set; }
        static bool _building, _done;
        static readonly Dictionary<string, Material[]> _mats =
            new Dictionary<string, Material[]>(StringComparer.Ordinal);

        static readonly float[] AoBucketMult = { 1.00f, 0.90f, 0.75f, 0.55f };
        static byte[] _aoBuckets;
        static int[] _aoBase;
        static readonly Dictionary<string, Material[]> _bucketMats =
            new Dictionary<string, Material[]>(StringComparer.Ordinal);

        static readonly List<Material> _bucketCreated = new List<Material>();
        struct ShapedRec { public MeshRenderer R; public Material[] Base; public Material[] Shaped; }
        static readonly List<ShapedRec> _shapedRenderers = new List<ShapedRec>();

        struct TunedRec { public Material M; public float BaseAo; public int Bucket; }
        static readonly List<TunedRec> _bucketTuned = new List<TunedRec>();

        static float _shadeReportAt = -1f;
        static Texture2D _whiteMask;
        static readonly int[] _shapeHist = new int[4];
        static bool _maskKeywordChecked;

        public static int[] ShapingHistogram { get { return _shapeHist; } }

        static bool AmbientShapingOn
        {
            get
            {

                var p = PrefBool("AmbientShapingOptIn", false,
                    "OPT-IN. Dim the sky-ambient term per instance by how much sky each "
                  + "instance can actually see (from an offline instance_ao.bin), writing "
                  + "a white mask onto maskless materials to do it. Rejected in play on "
                  + "2026-09-03; off unless you turn it on.");
                return p != null && p.Value;
            }
        }

        static MelonLoader.MelonPreferences_Entry<float> _shadeStrength;

        static float ShadeStrength
        {
            get
            {
                if (_shadeStrength == null)
                    _shadeStrength = PrefFloat("ShadeStrength", 1.0f,
                        "Scales the ambient-shaping bucket table. 0 = buckets off, "
                      + "1 = the shipped table (covered x0.9, interior x0.75, deep x0.55), "
                      + "up to 2 to deepen. Live via 'odg shade <x>', no rebuild.");
                float v = _shadeStrength != null ? _shadeStrength.Value : 1f;
                if (v < 0f) v = 0f;
                if (v > 2f) v = 2f;
                return v;
            }
        }

        static float EffectiveAoMult(int bucket)
        {
            float m = 1f - (1f - AoBucketMult[bucket]) * ShadeStrength;
            return m < 0f ? 0f : m;
        }

        static void LoadInstanceAo()
        {
            _aoBuckets = null;
            _aoBase = null;
            if (!MapIdentity.OwnSky || _manifest == null) return;
            string path = Path.Combine(BundleDir, "instance_ao.bin");
            if (!AmbientShapingOn)
            {

                try
                {
                    if (File.Exists(path))
                        _log?.Msg("[shade] instance_ao.bin is present in this map's data but ambient "
                                + "shaping is opt-in and OFF (AmbientShapingOptIn=false): ignored. "
                                + "The file is a leftover from a pack that no longer ships it.");
                }
                catch { }
                return;
            }
            try
            {
                if (!File.Exists(path)) return;
                byte[] want;
                using (var sha = System.Security.Cryptography.SHA1.Create())
                    want = sha.ComputeHash(File.ReadAllBytes(Path.Combine(BundleDir, "manifest.json")));

                using (var fs = File.OpenRead(path))
                using (var r = new BinaryReader(fs))
                {
                    if (new string(r.ReadChars(4)) != "IAO1")
                    { _log?.Warning("[shade] instance_ao.bin: bad magic; shaping OFF"); return; }
                    int count = r.ReadInt32();
                    var stamp = r.ReadBytes(8);
                    bool match = stamp != null && stamp.Length == 8;
                    for (int i = 0; match && i < 8; i++) match = stamp[i] == want[i];
                    if (!match)
                    {
                        _log?.Warning("[shade] instance_ao.bin was baked against a DIFFERENT "
                                    + "manifest.json (hash mismatch). Shaping is OFF for this "
                                    + "build -- rebake the bin, do not trust stale buckets.");
                        return;
                    }
                    if (count != _manifest.InstanceCount)
                    {
                        _log?.Warning("[shade] instance_ao.bin carries " + count
                                    + " buckets but the manifest holds " + _manifest.InstanceCount
                                    + " instances. Shaping is OFF for this build.");
                        return;
                    }
                    _aoBuckets = r.ReadBytes(count);
                    if (_aoBuckets == null || _aoBuckets.Length != count)
                    { _aoBuckets = null; _log?.Warning("[shade] instance_ao.bin: short read; shaping OFF"); return; }
                }

                _aoBase = new int[_manifest.Meshes.Count];
                int run = 0;
                for (int i = 0; i < _manifest.Meshes.Count; i++)
                {
                    _aoBase[i] = run;
                    run += _manifest.Meshes[i].Instances.Count;
                }
                if (run != _aoBuckets.Length)
                {
                    _log?.Warning("[shade] manifest instance sum " + run + " != bucket count "
                                + _aoBuckets.Length + "; shaping OFF");
                    _aoBuckets = null; _aoBase = null;
                    return;
                }
                for (int i = 0; i < _shapeHist.Length; i++) _shapeHist[i] = 0;
                _log?.Msg("[shade] instance_ao.bin verified against the loaded manifest: "
                        + _aoBuckets.Length + " bucket(s) armed.");
            }
            catch (Exception e)
            {
                _log?.Warning("[shade] instance_ao.bin: " + e.Message + "; shaping OFF");
                _aoBuckets = null; _aoBase = null;
            }
        }

        static Texture2D WhiteMask()
        {
            if (_whiteMask != null) return _whiteMask;
            try
            {
                var t = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
                t.SetPixel(0, 0, Color.white);
                t.Apply(false, true);
                _whiteMask = t;
            }
            catch (Exception e) { _log?.Warning("[shade] white mask: " + e.Message); }
            return _whiteMask;
        }

        static Material[] ShapedMats(string meshId, Material[] baseMats, int bucket)
        {
            string key = meshId + "#" + bucket;
            Material[] arr;
            if (_bucketMats.TryGetValue(key, out arr)) return arr;

            float mult = EffectiveAoMult(bucket);
            arr = new Material[baseMats.Length];
            for (int i = 0; i < baseMats.Length; i++)
            {
                var src = baseMats[i];
                if (src == null) { arr[i] = null; continue; }

                if (MeshFactory.IsGlassMaterial(src.name)) { arr[i] = src; continue; }
                try
                {
                    var m = new Material(src);
                    _bucketCreated.Add(m);
                    if (m.IsKeywordEnabled("_MASKMAP"))
                    {
                        float cur = 1f;
                        try { if (m.HasProperty("_AORemapMax")) cur = m.GetFloat("_AORemapMax"); }
                        catch { }
                        if (m.HasProperty("_AORemapMax"))
                        {
                            m.SetFloat("_AORemapMax", cur * mult);

                            _bucketTuned.Add(new TunedRec { M = m, BaseAo = cur, Bucket = bucket });
                        }
                    }
                    else
                    {
                        var wm = WhiteMask();
                        if (wm != null && m.HasProperty("_MaskMap"))
                        {
                            m.SetTexture("_MaskMap", wm);
                            if (m.HasProperty("_AORemapMin")) m.SetFloat("_AORemapMin", 0f);
                            if (m.HasProperty("_AORemapMax")) m.SetFloat("_AORemapMax", mult);
                            _bucketTuned.Add(new TunedRec { M = m, BaseAo = 1f, Bucket = bucket });
                            m.EnableKeyword("_MASKMAP");

                            if (!_maskKeywordChecked)
                            {
                                _maskKeywordChecked = true;
                                bool took = false;
                                try { took = m.IsKeywordEnabled("_MASKMAP"); } catch { }
                                _log?.Msg("[shade] _MASKMAP keyword write on a maskless "
                                        + "material: " + (took ? "TOOK" : "DID NOT TAKE -- "
                                        + "shaping is limited to materials that already "
                                        + "carry a mask map"));
                            }
                        }
                    }
                    arr[i] = m;
                }
                catch { arr[i] = src; }
            }
            _bucketMats[key] = arr;
            return arr;
        }

        public static void SetAmbientShapingLive(bool on)
        {
            int n = 0;
            try
            {
                for (int i = 0; i < _shapedRenderers.Count; i++)
                {
                    var rec = _shapedRenderers[i];
                    if (rec.R == null) continue;
                    var mats = on ? rec.Shaped : rec.Base;
                    try
                    {
                        if (mats.Length == 1) rec.R.sharedMaterial = mats[0];
                        else rec.R.sharedMaterials = mats;
                        n++;
                    }
                    catch { }
                }
            }
            catch (Exception e) { _log?.Warning("[shade] live toggle: " + e.Message); }
            _log?.Msg("[shade] ambient shaping " + (on ? "ON" : "OFF (flat buckets)")
                    + " across " + n + " renderer(s)");
        }

        public static string SetShadeStrengthLive(float s)
        {
            if (s < 0f) s = 0f;
            if (s > 2f) s = 2f;
            try
            {
                var p = _shadeStrength ?? PrefFloat("ShadeStrength", 1.0f, null);
                if (p != null) { _shadeStrength = p; p.Value = s; MelonLoader.MelonPreferences.Save(); }
            }
            catch { }
            int n = 0;
            try
            {
                for (int i = 0; i < _bucketTuned.Count; i++)
                {
                    var rec = _bucketTuned[i];
                    if (rec.M == null) continue;
                    try { rec.M.SetFloat("_AORemapMax", rec.BaseAo * EffectiveAoMult(rec.Bucket)); n++; }
                    catch { }
                }
            }
            catch (Exception e) { _log?.Warning("[shade] strength: " + e.Message); }
            string line = "shade strength " + s.ToString("F2") + " -> effective open x"
                 + EffectiveAoMult(0).ToString("F2") + ", covered x" + EffectiveAoMult(1).ToString("F2")
                 + ", interior x" + EffectiveAoMult(2).ToString("F2") + ", deep x"
                 + EffectiveAoMult(3).ToString("F2") + "  (" + n + " material variant(s) retuned; saved)";
            _log?.Msg("[shade] " + line);
            return line;
        }

        public static string ShadeConsole(string arg)
        {
            if (arg == "on") { SetAmbientShapingLive(true); return "ambient shaping ON (bucket variants worn)"; }
            if (arg == "off") { SetAmbientShapingLive(false); return "ambient shaping OFF (base materials worn; 'odg shade on' restores)"; }
            if (arg.Length > 0)
            {
                float s;
                if (float.TryParse(arg, System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out s))
                    return SetShadeStrengthLive(s);
                return "odg shade [on|off|<0..2>]   0 = buckets off, 1 = shipped table, 2 = doubled depth";
            }
            return "shade: strength " + ShadeStrength.ToString("F2")
                 + ", buckets " + (_aoBuckets != null ? "armed" : "absent")
                 + ", histogram open " + _shapeHist[0] + " / covered " + _shapeHist[1]
                 + " / interior " + _shapeHist[2] + " / deep " + _shapeHist[3]
                 + ", effective open x" + EffectiveAoMult(0).ToString("F2")
                 + " covered x" + EffectiveAoMult(1).ToString("F2")
                 + " interior x" + EffectiveAoMult(2).ToString("F2")
                 + " deep x" + EffectiveAoMult(3).ToString("F2");
        }

        public static void ShadeReportTick()
        {
            if (_shadeReportAt < 0f || Time.realtimeSinceStartup < _shadeReportAt) return;
            _shadeReportAt = -1f;
            try
            {
                var sh = RenderSettings.ambientProbe;
                float r = sh[0, 0], g = sh[1, 0], b = sh[2, 0];
                float lum = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                var sb = new System.Text.StringBuilder("[shade] brightness receipt: ambient probe DC = ")
                    .Append(r.ToString("F1")).Append(", ").Append(g.ToString("F1")).Append(", ")
                    .Append(b.ToString("F1")).Append(" (luminance ").Append(lum.ToString("F1"))
                    .Append("), strength ").Append(ShadeStrength.ToString("F2"))
                    .Append("; effective indirect per bucket:");
                string[] names = { "open", "covered", "interior", "deep" };
                for (int i = 0; i < 4; i++)
                {
                    float m = EffectiveAoMult(i);
                    sb.Append(' ').Append(names[i]).Append(" x").Append(m.ToString("F2"))
                      .Append(" = ").Append((lum * m).ToString("F1"));
                }
                sb.Append(".  A bucket's figure is the sky fill its shadowed surfaces "
                        + "actually receive; if a room reads as a cave, this line says "
                        + "whether the probe or the multiplier is the half to move.");
                _log?.Msg(sb.ToString());
            }
            catch (Exception e)
            { _log?.Msg("[shade] brightness receipt unavailable (" + e.Message + "); judge on screen"); }
        }

        static Transform _rootT;

        static readonly List<Transform> _plants = new List<Transform>();

        static readonly List<Transform> _trunks = new List<Transform>();

        static readonly List<Transform> _waterT = new List<Transform>();

        static readonly HashSet<int> _groundIds = new HashSet<int>();
        static readonly HashSet<int> _shadowProxyIds = new HashSet<int>();

        struct MeshFlags
        {
            public bool ShadowProxy, LightPlane, Plant, Ground, Collide;
            public bool CrevicePlug;

            public bool SmallProp;

            public bool Vehicle;

            public bool OverDetailed;
        }
        static readonly Dictionary<string, MeshFlags> _flags =
            new Dictionary<string, MeshFlags>(StringComparer.Ordinal);

        static readonly string[] VehicleNames =
        {
            "Car_hybrid_Body", "car_small_body", "BoxTruck", "SemiTruck",
            "SWAT_Truck", "RefrigeratorTruck", "DumpTruck", "EuroVan_Body",
            "OldPickup_Body", "FireTruck", "SchoolBus", "Trailer_01",
            "FlatbedTrailer", "Subway_train",
        };

        static bool IsVehicle(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            for (int i = 0; i < VehicleNames.Length; i++)
                if (id.IndexOf(VehicleNames[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        static int TriCount(Mesh m)
        {
            int n = 0;
            try
            {
                for (int s = 0; s < m.subMeshCount; s++)
                    n += (int)(m.GetIndexCount(s) / 3);
            }
            catch { }
            return n;
        }

        static MeshFlags FlagsFor(MapBundle.MeshDef md, Mesh mesh)
        {
            MeshFlags f;
            if (_flags.TryGetValue(md.Id, out f)) return f;
            f = new MeshFlags
            {

                SmallProp = mesh != null
                            && mesh.bounds.size.x < 2f
                            && mesh.bounds.size.y < 2f
                            && mesh.bounds.size.z < 2f,

                OverDetailed = mesh != null
                            && TriCount(mesh) >= 8000
                            && mesh.bounds.size.x < 3f
                            && mesh.bounds.size.y < 3f
                            && mesh.bounds.size.z < 3f,
                Vehicle     = IsVehicle(md.Id),
                ShadowProxy = md.Id.IndexOf("ShadowProxy", StringComparison.OrdinalIgnoreCase) >= 0,
                LightPlane  = md.Id.IndexOf("Light_Plane", StringComparison.OrdinalIgnoreCase) >= 0,

                CrevicePlug = md.Id.IndexOf("Basement_Floor_Dirt",
                                            StringComparison.OrdinalIgnoreCase) >= 0,
                Plant       = IsPlant(md.Id),
                Ground      = IsGroundName(md.Id),
            };
            f.Collide = ShouldCollide(md.Id, mesh);
            _flags[md.Id] = f;
            return f;
        }

        public static bool QueueOnNextScene;

        public static bool Building => _building;
        public static bool Done => _done;

        public static string HostSceneName;

        static bool _addRequested;
        static int _addWaitFrames;
        static bool _addSwept;
        static int _addSceneHandle;

        public static bool AdditiveReady
        {
            get
            {
                if (!MapIdentity.HostLevelIsMap) return true;
                var nm = MapIdentity.AdditiveSceneName;
                if (string.IsNullOrEmpty(nm)) return true;
                return _addSwept;
            }
        }

        static readonly string[] AdditiveKillRoots =
        {
            "PVP Game Mode", "FFA GAME MODE", "PVP Borders", "FFA Borders",
            "CinemachineCamera", "ISR CAM", "EditorHacks",
            "DamageZone", "Debug Cam", "Teleporter", "Border",
        };

        static bool _censusDone;

        static void LightmapAudit(int keep)
        {
            try
            {
                int lmCount = -1;
                try
                {
                    var prop = typeof(UnityEngine.LightmapSettings).GetProperty("lightmaps");
                    var arr = prop != null ? prop.GetValue(null, null) : null;
                    if (arr != null)
                    {
                        var lp = arr.GetType().GetProperty("Length")
                              ?? arr.GetType().GetProperty("Count");
                        if (lp != null) lmCount = Convert.ToInt32(lp.GetValue(arr, null));
                    }
                }
                catch (Exception le)
                {
                    _log?.Warning("[census] lightmaps array unreadable via reflection too: "
                                + le.Message);
                }

                int worst = -1, best = int.MaxValue, oor = 0, sampled = 0;
                var rr = UnityEngine.Object.FindObjectsOfType<Renderer>(true);
                for (int i = 0; i < (rr != null ? rr.Length : 0); i++)
                {
                    var r = rr[i];
                    if (r == null) continue;
                    try
                    {
                        if (r.gameObject.scene.handle != keep) continue;
                        int li = r.lightmapIndex;
                        if (li < 0 || li >= 65534) continue;
                        sampled++;
                        if (li > worst) worst = li;
                        if (li < best) best = li;
                        if (lmCount >= 0 && li >= lmCount) oor++;
                    }
                    catch { }
                }

                int hMin = int.MaxValue, hMax = -1, hN = 0;
                try
                {
                    var rr2 = UnityEngine.Object.FindObjectsOfType<Renderer>(true);
                    for (int i = 0; i < (rr2 != null ? rr2.Length : 0); i++)
                    {
                        var r = rr2[i];
                        if (r == null) continue;
                        try
                        {
                            var sc = r.gameObject.scene;
                            if (!sc.IsValid()) continue;
                            if (sc.handle == keep) continue;
                            if (sc.name == SceneName) continue;
                            int li = r.lightmapIndex;
                            if (li < 0 || li >= 65534) continue;
                            hN++;
                            if (li < hMin) hMin = li;
                            if (li > hMax) hMax = li;
                        }
                        catch { }
                    }
                }
                catch { }

                bool overlap = hMax >= 0 && worst >= 0 && hMin <= worst && best <= hMax;
                _log?.Msg("[census] lightmap INDEX RANGES -- host: " + hN + " renderer(s) using "
                        + (hMax < 0 ? "none" : hMin + ".." + hMax)
                        + " | additive: " + sampled + " renderer(s) using "
                        + (worst < 0 ? "none" : best + ".." + worst)
                        + (overlap ? "   *** RANGES OVERLAP: the additive world is addressing the "
                                   + "HOST's lightmaps (a night city). That is black in lightmap "
                                   + "UV space on lightmapped surfaces only ***"
                                   : "   (disjoint -- the additive load offset them correctly)"));
                _log?.Msg("[census] lightmaps: LightmapSettings.lightmaps=" + lmCount
                        + ", " + sampled + " lightmapped renderer(s) in the additive scene, "
                        + "highest lightmapIndex=" + worst + ", OUT OF RANGE="
                        + (lmCount < 0 ? "NOT CHECKED" : oor.ToString())
                        + (lmCount < 0
                           ? "  <<< the lightmap array read FAILED (count=-1), so the range check "
                           + "never ran. This used to print 'all indices resolve' here, which was "
                           + "a verdict from a test that did not execute."
                           : oor > 0 ? "  <<< THESE SAMPLE GARBAGE AND RENDER BLACK"
                                     : "  (all indices resolve)"));
            }
            catch (Exception e) { _log?.Warning("[census] LightmapAudit: " + e.Message); }
        }

        static void RenderSettingsAudit()
        {
            try
            {
                var act = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                _log?.Msg("[census] active scene='" + act.name + "' (RenderSettings come from "
                        + "THIS scene)  ambientMode=" + RenderSettings.ambientMode
                        + " ambientIntensity=" + RenderSettings.ambientIntensity.ToString("F2")
                        + " ambientLight=" + RenderSettings.ambientLight
                        + " subtractiveShadowColor=" + RenderSettings.subtractiveShadowColor);
            }
            catch (Exception e) { _log?.Warning("[census] RenderSettingsAudit: " + e.Message); }
        }

        static void LightmapModeAudit()
        {
            try
            {
                _log?.Msg("[census] lightmapsMode=" + UnityEngine.LightmapSettings.lightmapsMode
                        + "  (Subtractive vs Shadowmask mismatch between the active scene and "
                        + "how the additive scene was baked is a known cause of black bakes)");
            }
            catch (Exception e) { _log?.Warning("[census] LightmapModeAudit: " + e.Message); }
        }

        static void CensusAdditiveLighting()
        {
            if (_censusDone) return;
            _censusDone = true;
            try
            {
                int keep = ShellSwap.KeepSceneHandle;
                if (keep == 0) return;
                var rends = UnityEngine.Object.FindObjectsOfType<Renderer>(true);
                int total = 0, lightmapped = 0, probeLit = 0, offLod = 0;
                for (int i = 0; i < (rends != null ? rends.Length : 0); i++)
                {
                    var r = rends[i];
                    if (r == null) continue;
                    try
                    {
                        if (r.gameObject.scene.handle != keep) continue;
                        total++;
                        if (r.lightmapIndex >= 0 && r.lightmapIndex < 65534) lightmapped++;
                        else probeLit++;
                    }
                    catch { }
                }
                var groups = UnityEngine.Object.FindObjectsOfType<LODGroup>(true);
                int lodGroups = 0, lodRenderers = 0, maxLods = 0;
                for (int i = 0; i < (groups != null ? groups.Length : 0); i++)
                {
                    var g = groups[i];
                    if (g == null) continue;
                    try
                    {
                        if (g.gameObject.scene.handle != keep) continue;
                        lodGroups++;
                        int n = g.lodCount;
                        if (n > maxLods) maxLods = n;
                        if (n > 1) offLod++;
                        lodRenderers += n;
                    }
                    catch { }
                }
                _log?.Msg("[census] additive world '" + MapIdentity.AdditiveSceneName + "': "
                        + total + " renderer(s), " + lightmapped + " lightmapped, "
                        + probeLit + " PROBE-LIT ("
                        + (total > 0 ? (probeLit * 100 / total) : 0) + "%). "
                        + lodGroups + " LODGroup(s), " + offLod + " with more than one LOD, "
                        + "deepest " + maxLods + ".");
                _log?.Msg("[census] Unity lightmaps LOD0 only, so the probe-lit count IS the "
                        + "population that goes black if probe lighting does not reach it, and "
                        + "the >1-LOD count is how much dither stipple that can produce. A map "
                        + "with few of either cannot show the artifact whatever else is wrong.");
                try
                {
                    var rv = UnityEngine.Rendering.ProbeReferenceVolume.instance;
                    if (rv == null) { _log?.Warning("[census] no ProbeReferenceVolume instance"); }
                    else
                    {
                        _log?.Msg("[census] probe volume: initialised=" + rv.isInitialized
                                + " perSceneData=" + (rv.perSceneDataList != null
                                                      ? rv.perSceneDataList.Count : -1)
                                + " set='" + (rv.currentBakingSet != null
                                              ? rv.currentBakingSet.name : "<none>") + "'");
                    }
                }
                catch (Exception e) { _log?.Warning("[census] probe volume read: " + e.Message); }

                try
                {
                    long desired = (long)UnityEngine.Texture.desiredTextureMemory;
                    long target  = (long)UnityEngine.Texture.targetTextureMemory;
                    long current = (long)UnityEngine.Texture.currentTextureMemory;
                    long totalTex = (long)UnityEngine.Texture.totalTextureMemory;
                    bool on      = UnityEngine.QualitySettings.streamingMipmapsActive;
                    int budget   = (int)UnityEngine.QualitySettings.streamingMipmapsMemoryBudget;
                    double MB = 1024.0 * 1024.0;
                    int vramMB = 0;
                    try { vramMB = SystemInfo.graphicsMemorySize; } catch { }
                    _log?.Msg("[census] VRAM=" + vramMB + " MB  <-- resident texture memory must "
                            + "stay well under this or the driver evicts and repages");
                    _log?.Msg("[census] texture streaming: active=" + on
                            + " budget=" + budget + " MB"
                            + "  desired=" + (desired / MB).ToString("F0") + " MB"
                            + "  target=" + (target / MB).ToString("F0") + " MB"
                            + "  current=" + (current / MB).ToString("F0") + " MB"
                            + "  total=" + (totalTex / MB).ToString("F0") + " MB"
                            + "  pendingLoads=" + UnityEngine.Texture.streamingTexturePendingLoadCount
                            + "  streamedRenderers=" + UnityEngine.Texture.streamingRendererCount);
                    if (on && desired > target && target > 0)
                        _log?.Warning("[census] THE STREAMER IS STARVED: desired ("
                                    + (desired / MB).ToString("F0") + " MB) exceeds target ("
                                    + (target / MB).ToString("F0") + " MB) by "
                                    + ((desired - target) / MB).ToString("F0")
                                    + " MB. Mips are being dropped across the board, and that "
                                    + "includes lightmaps -- which is blocky black on surfaces "
                                    + "whose direct lighting is still correct.");
                }
                catch (Exception e) { _log?.Warning("[census] texture streaming read: " + e.Message); }

                try
                {
                    var stack = UnityEngine.Rendering.VolumeManager.instance.stack;
                    var ex = stack != null ? stack.GetComponent<
                        UnityEngine.Rendering.HighDefinition.Exposure>() : null;
                    if (ex != null)
                        _log?.Msg("[census] exposure: mode=" + ex.mode.value
                                + " fixedExposure=" + ex.fixedExposure.value.ToString("F2")
                                + " compensation=" + ex.compensation.value.ToString("F2")
                                + "  (Villa owns Fixed EV 15.42; an additive map owns nothing)");
                }
                catch (Exception e) { _log?.Warning("[census] exposure read: " + e.Message); }
                try { _log?.Msg("[census] frame bits: " + ApvGate.Status()); }
                catch (Exception e) { _log?.Warning("[census] frame bits: " + e.Message); }
            }
            catch (Exception e) { _log?.Warning("[census] CensusAdditiveLighting: " + e.Message); }
        }

        public static void NoteAdditiveSceneLoaded(int handle)
        {
            if (!MapIdentity.HostLevelIsMap || handle == 0) return;
            _addSceneHandle = handle;
            ShellSwap.KeepSceneHandle = handle;

            try
            {
                var sc = UnityEngine.SceneManagement.SceneManager
                             .GetSceneByName(MapIdentity.AdditiveSceneName);
                if (sc.IsValid() && sc.isLoaded)
                {
                    UnityEngine.SceneManagement.SceneManager.SetActiveScene(sc);
                    _log?.Msg("[map] active scene -> '" + sc.name
                            + "' at load time (ambient + lightmap settings)");

                    SwapProbeSetTo(sc, "additive world loaded", true);
                }
            }
            catch (Exception e)
            { _log?.Warning("[map] early SetActiveScene: " + e.Message); }
            _log?.Msg("[map] additive world scene registered (handle " + handle
                    + ") -- protected from the shell hide from this instant");
        }

        static bool _probeSwapDone;
        static int _probeSwapTries;

        public static bool SwapProbeSetTo(UnityEngine.SceneManagement.Scene sc,
                                          string why, bool verbose)
        {
            try
            {
                var rv = UnityEngine.Rendering.ProbeReferenceVolume.instance;
                if (rv == null)
                {
                    if (verbose) _log?.Warning("[apv] ProbeReferenceVolume.instance is null");
                    return false;
                }
                string before = "null";
                try { var cb = rv.currentBakingSet; if (cb != null) before = cb.name; }
                catch (Exception e) { if (verbose) _log?.Warning("[apv] currentBakingSet: " + e.Message); }

                UnityEngine.Rendering.ProbeVolumeBakingSet want = null;
                object mine = null;
                int n = 0;
                try
                {
                    var list = rv.perSceneDataList;
                    n = list != null ? list.Count : -1;
                    for (int i = 0; i < n; i++)
                    {
                        var d = list[i];
                        if (d == null) continue;
                        string sn = "?", bn = "?";
                        try { sn = d.gameObject.scene.name; } catch { }
                        try { var b = d.bakingSet; if (b != null) bn = b.name; } catch { }
                        if (verbose)
                            _log?.Msg("[apv]   perSceneData[" + i + "] scene='" + sn
                                    + "' set='" + bn + "'");
                        try
                        {
                            if (d.gameObject.scene.handle == sc.handle)
                            { mine = d; want = d.bakingSet; }
                        }
                        catch { }
                    }
                }
                catch (Exception e)
                { if (verbose) _log?.Warning("[apv] perSceneDataList: " + e.Message); }

                if (want == null)
                {
                    if (verbose)
                        _log?.Warning("[apv] no ProbeVolumePerSceneData registered for '"
                                    + sc.name + "' yet (" + n + " registered in total) -- "
                                    + "will retry; probe-lit objects stay black until it lands");
                    return false;
                }
                if (before == want.name)
                {
                    if (verbose) _log?.Msg("[apv] baking set already '" + before + "'");
                    return true;
                }
                rv.SetActiveBakingSet(want);
                try { ((UnityEngine.Rendering.ProbeVolumePerSceneData)mine).QueueSceneLoading(); }
                catch (Exception e)
                { if (verbose) _log?.Warning("[apv] QueueSceneLoading: " + e.Message); }

                string after = "null";
                try { var cb = rv.currentBakingSet; if (cb != null) after = cb.name; }
                catch { }
                _log?.Msg("[map] probe baking set: '" + before + "' -> '" + after
                        + "' (" + why + "). Anything but a CHANGE here means the "
                        + "world's probe-lit objects -- furniture, interiors, every "
                        + "LOD below the nearest -- render black.");
                return after == want.name;
            }
            catch (Exception e)
            {
                if (verbose) _log?.Warning("[apv] swap failed: " + e.Message);
                return false;
            }
        }

        static void ProbeSwapTick()
        {
            if (_probeSwapDone || _probeSwapTries > 240) return;
            _probeSwapTries++;
            if (_probeSwapTries % 10 != 0) return;
            try
            {
                var sc = UnityEngine.SceneManagement.SceneManager
                             .GetSceneByName(MapIdentity.AdditiveSceneName);
                if (!sc.IsValid() || !sc.isLoaded) return;
                bool verbose = (_probeSwapTries == 10) || (_probeSwapTries == 240);
                if (SwapProbeSetTo(sc, "retry #" + (_probeSwapTries / 10), verbose))
                {
                    _probeSwapDone = true;
                    _log?.Msg("[apv] probe set settled after " + (_probeSwapTries / 10)
                            + " attempt(s)");
                }
            }
            catch { }
        }

        public static void AdditiveTick()
        {
            if (!MapIdentity.HostLevelIsMap || !_addRequested) return;
            ProbeSwapTick();
            if (_addSwept) return;
            try
            {
                var sc = UnityEngine.SceneManagement.SceneManager
                             .GetSceneByName(MapIdentity.AdditiveSceneName);
                if (!sc.IsValid() || !sc.isLoaded)
                {

                    if (++_addWaitFrames > 2700)
                    {
                        _addSwept = true;
                        _log?.Error("[map] additive world '"
                                + MapIdentity.AdditiveSceneName + "' NEVER LOADED "
                                + "(waited ~45 s). Proceeding without it: nav and AI "
                                + "run on what exists, which is likely nothing but "
                                + "the overlay. The mission will look broken -- "
                                + "this line is the reason.");
                    }
                    return;
                }
                _addSceneHandle = sc.handle;
                ShellSwap.KeepSceneHandle = sc.handle;
                int killed = 0, comps = 0;
                var killedNames = new System.Collections.Generic.List<string>();
                var roots = sc.GetRootGameObjects();
                for (int i = 0; i < roots.Count; i++)
                {
                    var go = roots[i];
                    if (go == null) continue;
                    for (int k = 0; k < AdditiveKillRoots.Length; k++)
                        if (go.name == AdditiveKillRoots[k])
                        {
                            go.SetActive(false);
                            killed++;
                            killedNames.Add(go.name);
                            break;
                        }
                }

                int zonesOff = ZoneGuard.SweepScene(sc);

                for (int i = 0; i < roots.Count; i++)
                {
                    var go = roots[i];
                    if (go == null || !go.activeInHierarchy) continue;
                    var cams = go.GetComponentsInChildren<Camera>(true);
                    for (int k = 0; k < cams.Length; k++)
                        if (cams[k] != null && cams[k].enabled)
                        { cams[k].enabled = false; comps++; }
                    var lis = go.GetComponentsInChildren<AudioListener>(true);
                    for (int k = 0; k < lis.Length; k++)
                        if (lis[k] != null && lis[k].enabled)
                        { lis[k].enabled = false; comps++; }
                    var cvs = go.GetComponentsInChildren<Canvas>(true);
                    for (int k = 0; k < cvs.Length; k++)
                        if (cvs[k] != null && cvs[k].enabled)
                        { cvs[k].enabled = false; comps++; }
                }

                try
                {
                    UnityEngine.SceneManagement.SceneManager.SetActiveScene(sc);
                    _log?.Msg("[map] active scene -> '" + sc.name + "': its ambient, "
                            + "lightmaps and StaticLightingSky are the map's, and "
                            + "an empty runtime scene has none of those to give");
                }
                catch (Exception e)
                { _log?.Warning("[map] SetActiveScene(" + sc.name + "): " + e.Message); }

                var killNames = MapIdentity.AdditiveKillNames;
                int named = 0;
                if (killNames != null && killNames.Length > 0)
                {
                    for (int i = 0; i < roots.Count; i++)
                    {
                        var go = roots[i];
                        if (go == null || !go.activeInHierarchy) continue;
                        var all = go.GetComponentsInChildren<Transform>(true);
                        for (int t = 0; t < all.Length; t++)
                        {
                            var tf = all[t];
                            if (tf == null) continue;
                            string nm;
                            try { nm = tf.gameObject.name; } catch { continue; }
                            if (string.IsNullOrEmpty(nm)) continue;
                            for (int k = 0; k < killNames.Length; k++)
                            {
                                if (!nm.StartsWith(killNames[k], StringComparison.OrdinalIgnoreCase))
                                    continue;
                                try { tf.gameObject.SetActive(false); named++; } catch { }
                                break;
                            }
                        }
                    }
                    _log?.Msg("[map] additive world: " + named + " object(s) disabled by "
                            + "name (" + string.Join(", ", killNames) + ")");
                }
                try { Physics.SyncTransforms(); } catch { }
                _addSwept = true;

                _teleported = false;
                _teleportTries = 0; _moveIssued = false;
                _log?.Msg("[map] additive world '" + sc.name + "' ready: "
                        + killed + " PvP root(s) disabled (" + string.Join(", ", killedNames) + "), "
                        + zonesOff + " damage zone(s) disabled by type, " + comps
                        + " stray camera/listener/canvas component(s) off. "
                        + "Nav, AI and the teleport now proceed.");

                try { ShellSwap.SweepHostRenderers(_root != null ? _root.transform : null, _log, true); } catch { }

                try { ShellSwap.RestoreAdditiveTerrain(_log); } catch { }

                try { HideListedSceneObjects(); }
                catch (Exception e) { _log?.Warning("[map] hide_scene: " + e.Message); }

                try { MapBarriers.Sweep("additive-ready"); }
                catch (Exception e) { _log?.Warning("[map] barriers: " + e.Message); }

                try
                {
                    _log?.Msg("[map] re-grounding plants now the additive world is up "
                            + "(the build-time pass had nothing to raycast against)");
                    GroundPlants();

                    GroundFoliage();
                }
                catch (Exception e) { _log?.Warning("[map] re-ground: " + e.Message); }
                try { CensusAdditiveLighting(); } catch (Exception e) { _log?.Warning("[census] main: " + e.Message); }
                try { RenderSettingsAudit(); }    catch (Exception e) { _log?.Warning("[census] rs: " + e.Message); }
                try { LightmapModeAudit(); }      catch (Exception e) { _log?.Warning("[census] lmmode: " + e.Message); }
                try { LightmapAudit(ShellSwap.KeepSceneHandle); } catch (Exception e) { _log?.Warning("[census] lm: " + e.Message); }
                try { RelieveTextureStreaming(); } catch (Exception e) { _log?.Warning("[stream] relief: " + e.Message); }

                try
                {
                    const double MBb = 1024.0 * 1024.0;
                    double before = UnityEngine.Texture.currentTextureMemory / MBb;
                    Resources.UnloadUnusedAssets();
                    double after = UnityEngine.Texture.currentTextureMemory / MBb;
                    _log?.Msg("[vram] unloaded orphaned assets after the scene swap: "
                            + before.ToString("F0") + " MB -> " + after.ToString("F0")
                            + " MB resident textures (freed " + (before - after).ToString("F0")
                            + " MB). Anything still resident is REFERENCED -- the suburb "
                            + "host is invisible here but its materials still hold its "
                            + "textures, and that is the next lever if this number is small.");
                }
                catch (Exception e) { _log?.Warning("[vram] unload: " + e.Message); }
            }
            catch (Exception e)
            {
                _log?.Warning("[map] AdditiveTick: " + e.Message);
                _addSwept = true;
            }
        }

        static readonly System.Collections.Generic.List<UnityEngine.Rendering.Volume>
            _hostVolumesOff = new System.Collections.Generic.List<UnityEngine.Rendering.Volume>();

        static void DisableHostVolumes()
        {

            try { _log?.Msg("[vol] BEFORE teardown -- " + VolumeConsole("live")); }
            catch { }
            try
            {
                var vols = UnityEngine.Object.FindObjectsOfType<UnityEngine.Rendering.Volume>(true);
                var byScene = new Dictionary<string, int>();
                int n = 0, keptDdol = 0;
                for (int i = 0; i < (vols != null ? vols.Length : 0); i++)
                {
                    var v = vols[i];
                    if (v == null || !v.enabled) continue;
                    var sc = v.gameObject.scene;
                    if (!sc.IsValid()) continue;

                    if (string.Equals(sc.name, "DontDestroyOnLoad", StringComparison.Ordinal))
                    { keptDdol++; continue; }

                    if (string.Equals(sc.name, SceneName, StringComparison.Ordinal))
                        continue;
                    if (ShellSwap.KeepSceneHandle != 0
                        && sc.handle == ShellSwap.KeepSceneHandle) continue;
                    try { if (_root != null && v.transform.IsChildOf(_root.transform)) continue; }
                    catch { }
                    v.enabled = false;
                    _hostVolumesOff.Add(v);
                    n++;
                    try
                    {
                        string k = sc.name + (v.isGlobal ? " [global]" : " [local]");
                        int c; byScene.TryGetValue(k, out c); byScene[k] = c + 1;
                    }
                    catch { }
                }

                var sb = new System.Text.StringBuilder();
                foreach (var kv in byScene) sb.Append("  ").Append(kv.Key).Append(" x").Append(kv.Value);
                _log?.Msg("[map] host volumes OFF (" + n + " outside our scene and "
                        + "the additive world) -- the additive world's own "
                        + "sky/fog/tonemap stand.  BY SCENE:" + sb.ToString()
                        + "  |  SPARED " + keptDdol + " DontDestroyOnLoad volume(s) -- the "
                        + "game's global stack, which a stock match keeps alive.");
                try { _log?.Msg("[vol] AFTER teardown -- " + VolumeConsole("live")); }
                catch { }
            }
            catch (Exception e) { _log?.Warning("[map] DisableHostVolumes: " + e.Message); }
        }

        static void RestoreHostVolumes()
        {
            for (int i = 0; i < _hostVolumesOff.Count; i++)
                try { if (_hostVolumesOff[i] != null) _hostVolumesOff[i].enabled = true; }
                catch { }
            _hostVolumesOff.Clear();
        }

        public static bool OwnsHiddenState(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return false;
            if (string.Equals(sceneName, SceneName, StringComparison.Ordinal)) return true;

            if (string.IsNullOrEmpty(HostSceneName)) return true;
            return string.Equals(sceneName, HostSceneName, StringComparison.Ordinal);
        }
        public static int Placed => _placed;
        public static GameObject Root => _root;

        public static Transform MarkersRoot
            => _markers != null ? _markers.transform : null;

        public static Transform LightingRoot
            => _lighting != null ? _lighting.transform : null;

        public static void Init(MelonLogger.Instance log)
        {
            _log = log;

            try { ApvGate.Init(log); } catch { }
            try { VoidPatches.Init(log); } catch { }

            try
            {
                var cfg = MelonLoader.MelonPreferences.GetCategory("MapLoader")
                       ?? MelonLoader.MelonPreferences.CreateCategory("MapLoader");
                if (cfg.GetEntry<int>("LightingSchemaV") == null)
                    cfg.CreateEntry("LightingSchemaV", 1,
                        description: "Version stamp for the lighting defaults. Future builds "
                                   + "migrate saved HostLighting/CityApvOff values when this "
                                   + "is behind theirs; never edit by hand.");
            }
            catch { }
        }

        static MelonLoader.MelonPreferences_Entry<bool> PrefBool(string name, bool def, string desc)
        {
            try
            {
                var cfg = MelonLoader.MelonPreferences.GetCategory("MapLoader")
                       ?? MelonLoader.MelonPreferences.CreateCategory("MapLoader");
                return cfg.GetEntry<bool>(name) ?? cfg.CreateEntry(name, def, description: desc);
            }
            catch { return null; }
        }

        static MelonLoader.MelonPreferences_Entry<float> PrefFloat(string name, float def, string desc)
        {
            try
            {
                var cfg = MelonLoader.MelonPreferences.GetCategory("MapLoader")
                       ?? MelonLoader.MelonPreferences.CreateCategory("MapLoader");
                return cfg.GetEntry<float>(name) ?? cfg.CreateEntry(name, def, description: desc);
            }
            catch { return null; }
        }

        public static void ResetForNewLevel()
        {

            try { ShellSwap.ForgetHostLights(_log); } catch { }
            try { RestoreHostVolumes(); } catch { }
            _addRequested = false;
            _addSwept = false;
            _addWaitFrames = 0;
            _probeSwapDone = false;
            _probeSwapTries = 0;
            _addSceneHandle = 0;
            ShellSwap.KeepSceneHandle = 0;

            if (!_building && !_done) return;
            _log?.Msg("[map] new host level: clearing previous map state");
            _root = null;
            _markers = null;

            _boundsCached = false;

            _lighting = null;

            _nightExposure = null;

            _dayExposure = null;

            _ownPriority = 0f;
            _moonHd = null;
            _fillHd = null;
            _moonGo = null;

            SkyRig.Forget();
            MapLights.Forget();
            MapAmbience.Forget();
            VoidPatches.Forget();

            FakeBounce.Forget();
            NightArt.Forget(); DayArt.Forget();

            MeshFactory.ForgetGroundMats();
            NightFog = null;

            _dayProfile = null;
            _ssgi = null;
            _hostLitOverride = null;
            ApvGate.Demand = false;
            _rootT = null;
            _plants.Clear();
            _trunks.Clear();
            _waterT.Clear();
            _flags.Clear();
            _groundIds.Clear();
            _shadowProxyIds.Clear();
            _scene = default(Scene);
            _prevActive = default(Scene);
            _manifest = null;
            _meshCache.Clear();
            _meshHasCollider.Clear();
            _meshIsFoliage.Clear();
            _censusDone = false;

            _addShadowGo = null;
            _fillGo = null;
            _ambStage = 0;
            _ambNextAt = 0f;
            RestoreTextureStreaming();
            _mats.Clear();

            _bucketMats.Clear();
            _bucketCreated.Clear();
            _shapedRenderers.Clear();
            _bucketTuned.Clear();
            _shadeReportAt = -1f;
            _aoBuckets = null;
            _aoBase = null;
            _whiteMask = null;
            _building = false;
            _rendOptDone = 0; _rendOptFail = 0;
            ProbeHostRenderMask();
            _done = false;
            _placed = 0;
            _meshIndex = 0;
            _instanceIndex = 0;
            _teleportTries = 0; _moveIssued = false;
            _teleported = false;
            _spawnRead = false; _spawnHave = false;
            _hvtRead = false; _hvtHave = false;
        }

        internal static void Crumb(string what)
        {
            try { _log?.Msg("[crumb] build: " + what); } catch { }
        }

        public static bool Begin()
        {
            Crumb("Begin() entered");
            if (_building) { _log?.Msg("[map] already building"); return true; }

            if (_done && _root != null)
            {
                _log?.Msg("[map] already built; press Shift+F9 to tear down first");
                return true;
            }
            if (_done && _root == null)
            {
                _log?.Warning("[map] previous build is marked done but its root died "
                            + "with the scene (mission restart / re-entry). Clearing "
                            + "the stale state and building again.");
                ResetForNewLevel();
            }

            if (!Directory.Exists(BundleDir))
            {
                _log?.Error("[map] no bundle dir: " + BundleDir);
                return false;
            }

            try { HostSceneName = SceneManager.GetActiveScene().name; } catch { HostSceneName = null; }
            Crumb("host scene recorded as '" + (HostSceneName ?? "?") + "'");

            FoliageCullDistance = MapIdentity.FoliageCullM > 0f
                                ? MapIdentity.FoliageCullM : 70f;
            _cullingApplied = false;

            var sw = Stopwatch.StartNew();
            Crumb("reading manifest from " + BundleDir);
            try { _manifest = MapBundle.ReadManifest(BundleDir); }
            catch (Exception e) { _log?.Error("[map] manifest: " + e.Message); return false; }

            TuneStore.ApplyAll(_log);

            _log?.Msg("[map] manifest: " + _manifest.Meshes.Count + " meshes, "
                    + _manifest.InstanceCount + " instances  (" + sw.ElapsedMilliseconds + " ms)");

            _scene = SceneFactory.TryCreate(SceneName, _log);
            if (_scene.IsValid())
            {
                _prevActive = SceneManager.GetActiveScene();
                SceneManager.SetActiveScene(_scene);
                _log?.Msg("[map] created scene '" + SceneName + "', set active");
            }
            else
            {
                _log?.Msg("[map] no separate scene available on this build; "
                        + "building into the host level and tearing down by root");
            }

            _root = new GameObject(RootName);
            if (_scene.IsValid()) SceneManager.MoveGameObjectToScene(_root, _scene);
            _rootT = _root.transform;

            try { _rootT.hierarchyCapacity = _manifest.InstanceCount + 64; } catch { }

            MeshFactory.ResetCaches();
            _mats.Clear();
            _bucketMats.Clear();
            _bucketCreated.Clear();
            _shapedRenderers.Clear();
            _bucketTuned.Clear();
            _shadeReportAt = -1f;
            _whiteMask = null;
            _maskKeywordChecked = false;
            _probeUsageApplied = 1;
            _plants.Clear();
            _trunks.Clear();
            _waterT.Clear();
            _flags.Clear();
            _groundIds.Clear();
            _shadowProxyIds.Clear();
            _meshIndex = 0; _instanceIndex = 0; _placed = 0;
            _boundsCached = false;
            _fastPose = true;

            LoadInstanceAo();
            FindFoliageLayer();

            SafeRunLocal("place markers early", () => PlaceMarkers());

            _doorPanelsSkipped = 0;
            _meshesDropped = 0; _instancesDropped = 0; _droppedIds.Clear();
            _doorPanelsKept = 0;
            _vehicles = 0;
            _overDetailed = 0;
            _shadowProxies = 0;
            _foliageNoShadow = 0;
            _crevicePlugs = 0;
            _foliageShadowCasters = 0;
            _smallProps = 0;
            _fakeLightCards = 0;
            _cappedInstances = 0;
            _meshesFailed = 0;

            float gscale = MapIdentity.GroundAlbedoScaleOverride;
            if (gscale > 0f && Math.Abs(MeshFactory.GroundAlbedoScale - gscale) > 0.001f)
            {
                _log?.Msg("[map] ground albedo scale " + MeshFactory.GroundAlbedoScale
                        + " -> " + gscale + " for this map (the 0.75 default was "
                        + "tuned against night desert dirt; this map's ground is snow)");
                MeshFactory.GroundAlbedoScale = gscale;
            }

            LoadVeil.Show("map build starting");
            _building = true; _done = false;
            _buildStartedAt = Time.realtimeSinceStartup;

            MapScene.Register(SceneName);
            return true;
        }

        public static void Step()
        {
            if (!_building || _manifest == null) return;
            var sw = Stopwatch.StartNew();

            while (_meshIndex < _manifest.Meshes.Count)
            {
                var md = _manifest.Meshes[_meshIndex];

                if (DropsThisMesh(md.Id))
                {
                    _meshesDropped++;
                    _instancesDropped += md.Instances.Count;

                    if (_droppedIds.Count < 40) _droppedIds.Add(md.Id + " x" + md.Instances.Count);
                    _meshIndex++; _instanceIndex = 0;
                    continue;
                }

                if (IsStaticDoorPanel(md.Id))
                {
                    var probe = GetMesh(md);
                    if (probe == null || IsDoorLeafSized(probe, md.Id))
                    {

                        if (!KeepDoorPanels)
                        {
                            _doorPanelsSkipped += md.Instances.Count;
                            _meshIndex++; _instanceIndex = 0;
                            continue;
                        }
                        _doorPanelsAsScenery += md.Instances.Count;
                        _panelMeshIds.Add(md.Id);
                    }
                    else _doorPanelsKept += md.Instances.Count;
                }

                if (_instanceIndex == 0 && !_mats.ContainsKey(md.Id))
                {
                    var swMat = Stopwatch.StartNew();
                    try { _mats[md.Id] = BuildMaterials(md); }
                    catch (Exception e)
                    {
                        _log?.Warning("[map] materials " + md.Id + ": " + e.Message);
                        _mats[md.Id] = null;
                    }

                    MeshFactory.MaterialMs += swMat.ElapsedMilliseconds;
                }

                Mesh mesh = GetMesh(md);
                if (mesh == null) { _meshesFailed++; _meshIndex++; _instanceIndex = 0; continue; }

                int cap = InstanceCap(md);
                if (_instanceIndex == 0 && cap < md.Instances.Count)
                    _cappedInstances += md.Instances.Count - cap;
                var mats = _mats.TryGetValue(md.Id, out var mm) ? mm : null;
                var flags = FlagsFor(md, mesh);

                while (_instanceIndex < md.Instances.Count && _instanceIndex < cap)
                {
                    Place(md, mesh, mats, md.Instances[_instanceIndex], flags);
                    _instanceIndex++;
                    _placed++;
                    if (sw.ElapsedMilliseconds >= BudgetMs) return;
                }

                _meshIndex++;
                _instanceIndex = 0;

                try
                {
                    int total = _manifest != null && _manifest.Meshes != null
                              ? _manifest.Meshes.Count : 0;
                    if (total > 0)
                        LoadVeil.Progress(0.90f * _meshIndex / total, "Deploying to Mission");
                }
                catch { }
                if (sw.ElapsedMilliseconds >= BudgetMs) return;
            }

            Finish();
        }

        static int InstanceCap(MapBundle.MeshDef md)
        {

            int cap = MapIdentity.FoliageMaxPerMesh > 0
                    ? MapIdentity.FoliageMaxPerMesh : FoliageMax;
            if (cap <= 0) return md.Instances.Count;
            string id = md.Id.ToLowerInvariant();
            bool foliage = id.StartsWith("fol_") || id.Contains("grass") || id.Contains("wheat")
                        || id.Contains("bromeplant") || id.Contains("cabbage");
            return foliage ? Math.Min(md.Instances.Count, cap) : md.Instances.Count;
        }

        static readonly System.Collections.Generic.HashSet<string> _meshHasCollider =
            new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

        static readonly System.Collections.Generic.HashSet<string> _meshIsFoliage =
            new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

        static readonly Dictionary<string, Mesh> _meshCache =
            new Dictionary<string, Mesh>(StringComparer.Ordinal);

        static Mesh GetMesh(MapBundle.MeshDef md)
        {
            Mesh m;
            if (_meshCache.TryGetValue(md.Id, out m)) return m;
            var sw = Stopwatch.StartNew();
            try
            {
                var g = MapBundle.ReadGeometry(BundleDir, md.Id);

                if (MapIdentity.OpenTreadStairs) StairFill.Analyse(md.Id, g);

                if (MapIdentity.PlateSlabs) PlateFill.Analyse(md.Id, g);
                m = MeshFactory.Build(g, md.Id);
            }
            catch (Exception e)
            {
                _log?.Warning("[map] mesh " + md.Id + ": " + e.Message);
                m = null;
            }

            MeshFactory.MeshBuildMs += sw.ElapsedMilliseconds;
            _meshCache[md.Id] = m;
            return m;
        }

        static Material[] BuildMaterials(MapBundle.MeshDef md)
        {
            string texDir = Path.Combine(BundleDir, "textures");

            MeshFactory.EnsureBlendTable(BundleDir);
            MeshFactory.EnsureConstantsTable(BundleDir);
            MeshFactory.EnsureEmissiveTable(BundleDir);
            var arr = new Material[Math.Max(1, md.Materials.Count)];
            for (int i = 0; i < arr.Length; i++)
            {
                var def = i < md.Materials.Count ? md.Materials[i] : null;
                if (def == null) { arr[i] = null; continue; }

                bool clip = MeshFactory.NeedsCutout(def.Name ?? md.Id);
                arr[i] = MeshFactory.BuildMaterial(def, texDir, clip, md.Id);
            }
            return arr;
        }

        internal static Material FindBuiltMaterialForMesh(string meshIdContains)
        {
            if (string.IsNullOrEmpty(meshIdContains)) return null;
            try
            {
                foreach (var kv in _mats)
                {
                    string id = kv.Key;
                    var arr = kv.Value;
                    if (arr == null || string.IsNullOrEmpty(id)) continue;
                    if (id.IndexOf(meshIdContains, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Material first = null;
                    for (int i = 0; i < arr.Length; i++)
                    {
                        var m = arr[i];
                        if (m == null) continue;
                        if (first == null) first = m;
                        string n = null;
                        try { n = m.name; } catch { continue; }
                        if (!string.IsNullOrEmpty(n)
                            && n.IndexOf("decal", StringComparison.OrdinalIgnoreCase) < 0)
                            return m;
                    }
                    if (first != null) return first;

                }
            }
            catch { }
            return null;
        }

        internal static Material FindBuiltMaterial(string nameContains)
        {
            if (string.IsNullOrEmpty(nameContains)) return null;
            try
            {
                foreach (var kv in _mats)
                {
                    var arr = kv.Value;
                    if (arr == null) continue;
                    for (int i = 0; i < arr.Length; i++)
                    {
                        var m = arr[i];
                        if (m == null) continue;
                        string n = null;
                        try { n = m.name; } catch { continue; }
                        if (!string.IsNullOrEmpty(n) && WordMatch(n, nameContains))
                            return m;
                    }
                }
            }
            catch { }
            return null;
        }

        static bool WordMatch(string haystack, string needle)
        {
            int at = 0;
            while (true)
            {
                int i = haystack.IndexOf(needle, at, StringComparison.OrdinalIgnoreCase);
                if (i < 0) return false;
                bool startOk = i == 0
                    || !char.IsLetterOrDigit(haystack[i - 1])
                    || (char.IsLower(haystack[i - 1]) && char.IsUpper(haystack[i]));
                int e = i + needle.Length;
                bool endOk = e >= haystack.Length
                    || !char.IsLetterOrDigit(haystack[e])
                    || (char.IsLower(haystack[e - 1]) && char.IsUpper(haystack[e]));
                if (startOk && endOk) return true;
                at = i + 1;
            }
        }

        static int _doorPanelsAsScenery;
        static readonly System.Collections.Generic.HashSet<string> _panelMeshIds =
            new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

        static bool _keepPanelsRead;
        static bool _keepPanels = true;
        public static bool KeepDoorPanels
        {
            get
            {
                if (!_keepPanelsRead)
                {
                    _keepPanelsRead = true;
                    try
                    {
                        var cfg = MelonLoader.MelonPreferences.GetCategory("MapLoader")
                               ?? MelonLoader.MelonPreferences.CreateCategory("MapLoader");
                        var e = cfg.GetEntry<bool>("KeepStaticDoorPanels")
                             ?? cfg.CreateEntry("KeepStaticDoorPanels", true,
                                  description: "Build the map's static door leaves as scenery "
                                             + "instead of deleting them. The city has ~740 "
                                             + "doorways and only 11 can be made to open, so "
                                             + "deleting them left the streets doorless. Panels "
                                             + "that clash with a placed working door are culled "
                                             + "afterwards by proximity.");
                        _keepPanels = e.Value;
                    }
                    catch { _keepPanels = true; }
                }

                return _keepPanels && MapIdentity.OwnSky;
            }
        }

        public static int ClearPanelsNear(Vector3[] doorPositions, float radius)
        {
            int culled = 0;
            try
            {
                if (_rootT == null || doorPositions == null || doorPositions.Length == 0)
                    return 0;
                float r2 = radius * radius;
                for (int i = _rootT.childCount - 1; i >= 0; i--)
                {
                    var c = _rootT.GetChild(i);
                    if (c == null) continue;
                    string n = null;
                    try { n = c.name; } catch { continue; }
                    if (n == null || !_panelMeshIds.Contains(n)) continue;

                    try { if (!c.gameObject.activeSelf) continue; } catch { }
                    var p = c.position;
                    for (int d = 0; d < doorPositions.Length; d++)
                    {
                        if ((p - doorPositions[d]).sqrMagnitude > r2) continue;
                        try { c.gameObject.SetActive(false); culled++; } catch { }
                        break;
                    }
                }
                if (culled > 0)
                    _log?.Msg("[map] " + culled + " static door leaf/leaves switched off: a "
                            + "working door was placed in the same doorway. The rest stay as "
                            + "scenery so the city is not full of empty holes.");
            }
            catch (Exception e) { _log?.Warning("[map] ClearPanelsNear: " + e.Message); }
            return culled;
        }

        static bool IsStaticDoorPanel(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            string l = id.ToLowerInvariant();
            if (l.IndexOf("door", StringComparison.Ordinal) < 0) return false;

            if (l.Contains("wall")) return false;
            if (l.Contains("frame")) return false;
            if (l.Contains("light")) return false;
            if (l.Contains("cupboard")) return false;
            if (l.Contains("nightstand")) return false;

            if (IsVehiclePart(l)) return false;
            return true;
        }

        static readonly string[] VehicleWords =
            { "_car_", "car_hybrid", "truck", "_van_", "taxi", "_bus", "bus_",
              "vehicle", "sedan", "pickup", "_suv", "police", "ambul", "firetruck",
              "jimmy", "trailer", "wagon", "motorcycle", "scooter" };

        static bool IsVehiclePart(string lower)
        {
            if (string.IsNullOrEmpty(lower)) return false;
            for (int i = 0; i < VehicleWords.Length; i++)
                if (lower.IndexOf(VehicleWords[i], StringComparison.Ordinal) >= 0) return true;

            if (lower.StartsWith("car_", StringComparison.Ordinal)) return true;
            return false;
        }

        const float DoorLeafMaxWide = 3.0f;
        const float DoorLeafMaxTall = 3.5f;
        static bool IsDoorLeafSized(Mesh m, string id)
        {
            try
            {
                var s = m.bounds.size;
                float wide = Mathf.Max(s.x, s.z);
                if (wide <= DoorLeafMaxWide && s.y <= DoorLeafMaxTall) return true;
                _log?.Msg("[map] KEPT '" + id + "': the name says door but it is "
                        + s.x.ToString("F1") + " x " + s.y.ToString("F1") + " x "
                        + s.z.ToString("F1") + " m, which is building, not leaf");
                return false;
            }
            catch (Exception e)
            {

                _log?.Warning("[map] door size probe '" + id + "': " + e.Message);
                return true;
            }
        }

        static void Place(MapBundle.MeshDef md, Mesh mesh, Material[] mats,
                          MapBundle.InstanceDef inst, MeshFlags f)
        {

            var go = new GameObject(md.Id);
            var t = go.transform;
            t.SetParent(_rootT, false);

            var lp = new Vector3(inst.px, inst.py, inst.pz);
            var lr = new Quaternion(inst.rx, inst.ry, inst.rz, inst.rw);
            if (_fastPose)
            {
                try { t.SetLocalPositionAndRotation(lp, lr); }
                catch (Exception pe)
                {
                    _fastPose = false;
                    _log?.Warning("[map] SetLocalPositionAndRotation is unusable on this "
                                + "runtime (" + pe.Message + "); falling back to separate "
                                + "position and rotation writes for the rest of the build");
                }
            }
            if (!_fastPose) { t.localPosition = lp; t.localRotation = lr; }
            t.localScale = new Vector3(inst.sx, inst.sy, inst.sz);

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();

            var useMats = mats;
            if (_aoBuckets != null && mats != null)
            {
                int gi = _aoBase[_meshIndex] + _instanceIndex;
                int bucket = (uint)gi < (uint)_aoBuckets.Length ? _aoBuckets[gi] : 0;
                if (bucket > 0 && bucket < AoBucketMult.Length)
                {
                    var shaped = ShapedMats(md.Id, mats, bucket);
                    if (shaped != null)
                    {
                        useMats = shaped;
                        _shapeHist[bucket]++;
                        _shapedRenderers.Add(new ShapedRec { R = r, Base = mats, Shaped = shaped });
                    }
                }
                else _shapeHist[0]++;
            }

            if (useMats != null)
            {
                if (useMats.Length == 1) r.sharedMaterial = useMats[0];
                else r.sharedMaterials = useMats;
            }

            bool wantsAmbient = MapIdentity.OwnSky || MapIdentity.HostLevelIsMap;
            try
            {
                r.lightProbeUsage = wantsAmbient
                    ? UnityEngine.Rendering.LightProbeUsage.BlendProbes
                    : UnityEngine.Rendering.LightProbeUsage.Off;
            }
            catch { _rendOptFail |= 1; }

            try
            {
                r.reflectionProbeUsage = wantsAmbient
                    ? UnityEngine.Rendering.ReflectionProbeUsage.BlendProbes
                    : UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }
            catch { _rendOptFail |= 2; }
            try { r.motionVectorGenerationMode =
                      UnityEngine.MotionVectorGenerationMode.ForceNoMotion; }
            catch { _rendOptFail |= 4; }
            try { r.allowOcclusionWhenDynamic = false; }
            catch { _rendOptFail |= 8; }

            try { r.staticShadowCaster = true; }
            catch { _rendOptFail |= 16; }

            try { if (_hostRenderMask != 0) r.renderingLayerMask = _hostRenderMask; }
            catch { _rendOptFail |= 32; }
            _rendOptDone++;

            if (f.CrevicePlug)
            {
                r.forceRenderingOff = true;
                _crevicePlugs++;
            }
            else if (f.ShadowProxy)
            {
                r.forceRenderingOff = true;
                _shadowProxies++;

                try { _shadowProxyIds.Add(go.GetInstanceID()); } catch { }
            }
            else if (f.Ground && f.Collide)
            {

                try { _groundIds.Add(go.GetInstanceID()); } catch { }
            }

            else if (f.SmallProp && !f.Ground)
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (PropLayer >= 0) go.layer = PropLayer;
                _smallProps++;
            }

            if (IsShadowDuplicate(md.Id))
            {
                try
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    _overlayNoShadow++;
                }
                catch { }
            }
            else if (MapIdentity.HostLevelIsMap && IsDecal(md.Id))
            {
                try
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    _overlayNoShadow++;
                }
                catch { }
            }

            else if ((f.Vehicle || f.OverDetailed) && !f.Ground)
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (f.Vehicle) _vehicles++; else _overDetailed++;
            }

            if (MeshFactory.IsWaterMesh(md.Id))
                _waterT.Add(t);

            if (f.Plant)
            {
                _plants.Add(t);

                if (MapIdentity.TreeTrunkColliders && IsTrunkTree(md.Id)
                    && !md.Id.StartsWith("FOL_veg_", StringComparison.OrdinalIgnoreCase))
                    _trunks.Add(t);

                bool castLeaves = MapIdentity.FoliageShadows
                               && (IsTrunkTree(md.Id) || IsCanopyTree(md.Id));
                if (castLeaves)
                {
                    _foliageShadowCasters++;
                }
                else
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    _foliageNoShadow++;
                }

                bool farTree = MapIdentity.TreeTrunkColliders && TreeLayer >= 0
                               && IsTrunkTree(md.Id);
                if (farTree) go.layer = TreeLayer;
                else if (FoliageLayer >= 0) go.layer = FoliageLayer;
                _meshIsFoliage.Add(md.Id);
            }

            if (f.LightPlane)
            {
                r.enabled = false;
                _fakeLightCards++;
            }

            if (f.Collide)
            {
                try
                {
                    go.AddComponent<MeshCollider>().sharedMesh = mesh;

                    _meshHasCollider.Add(md.Id);
                }
                catch { }

                if (MapIdentity.OpenTreadStairs) StairFill.Attach(go, md.Id, inst.sy);
                if (MapIdentity.PlateSlabs) PlateFill.Attach(go, md.Id, inst.sy);
            }
        }

        static uint _hostRenderMask;
        static uint _ourDefaultMask;
        static void ProbeHostRenderMask()
        {
            _hostRenderMask = 0; _ourDefaultMask = 0;
            try
            {
                Transform ours = Root != null ? Root.transform : null;
                var rs = UnityEngine.Object.FindObjectsOfType<MeshRenderer>();
                for (int i = 0; i < (rs != null ? rs.Length : 0); i++)
                {
                    var r = rs[i];
                    if (r == null) continue;
                    try { if (ours != null && r.transform.IsChildOf(ours)) continue; } catch { }
                    try { if (!r.gameObject.scene.IsValid()) continue; } catch { continue; }
                    _hostRenderMask = r.renderingLayerMask;
                    break;
                }
                var probe = new GameObject("ODG_maskprobe"); var pr = probe.AddComponent<MeshRenderer>();
                _ourDefaultMask = pr.renderingLayerMask; UnityEngine.Object.Destroy(probe);
                _log?.Msg("[map] light layers: a shipped renderer wears renderingLayerMask 0x"
                        + _hostRenderMask.ToString("X") + ", a new renderer wears 0x" + _ourDefaultMask.ToString("X")
                        + (_hostRenderMask == 0 ? " -- no shipped renderer found, ours keep the default"
                           : _hostRenderMask == _ourDefaultMask ? " -- same, so this is not why the flashlight does nothing here"
                           : " -- DIFFERENT: ours now wear the shipped mask so the game's lights, the flashlight included, reach them"));
            }
            catch (Exception e) { _log?.Warning("[map] light layer probe: " + e.Message); }
        }

        static bool _fastPose = true;
        static int _doorPanelsSkipped;
        static int _meshesDropped, _instancesDropped;
        static readonly List<string> _droppedIds = new List<string>();

        static bool DropsThisMesh(string id)
        {
            var words = MapIdentity.DropMeshWords;
            if (words == null || words.Length == 0 || string.IsNullOrEmpty(id)) return false;
            for (int i = 0; i < words.Length; i++)
            {
                var w = words[i];
                if (string.IsNullOrEmpty(w)) continue;

                if (w[w.Length - 1] == '$')
                {
                    if (id.EndsWith(w.Substring(0, w.Length - 1), StringComparison.OrdinalIgnoreCase))
                        return true;
                    continue;
                }
                if (id.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }
        static int _doorPanelsKept;
        static int _vehicles;
        static int _overDetailed;
        static int _crevicePlugs;
        static int _shadowProxies;
        static int _foliageNoShadow;
        static int _foliageShadowCasters;
        static int _smallProps;
        static int _overlayNoShadow;
        static int _fakeLightCards;

        static int _cappedInstances;
        static int _meshesFailed;

        static readonly string[] GroundNames =
        {
            "terrain_", "SM_Field_Merged", "SM_Tilled_Field",
            "SM_Canal_Merged", "SM_Compound_Walkway", "SM_Ground",

            "SM_Road_", "Road_Large", "Road_Crosswalk", "SplineRoad",

            "_floor", "Floor2", "FloorHeight", "DemoFloor", "Sidewalk",
            "SplineWalkway", "Path_A", "Office_Floor",
        };

        static readonly string[] NotGroundWords =
        {
            "template", "sign",

            "vista",
        };

        static bool IsGroundName(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            for (int i = 0; i < NotGroundWords.Length; i++)
                if (id.IndexOf(NotGroundWords[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
            for (int i = 0; i < GroundNames.Length; i++)
                if (id.IndexOf(GroundNames[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        const float SnapDeadband = 0.05f;

        const float SnapMaxDrop = 12f;

        const float SupportTolerance = 0.75f;

        internal static bool SurfaceIsOurs(Collider c)
        {
            if (c == null) return false;
            try
            {
                if (_root != null && c.transform.IsChildOf(_root.transform)) return true;
                if (MapIdentity.HostLevelIsMap)
                {
                    var sc = c.gameObject.scene;

                    if (ShellSwap.IsAdditiveWorld(sc)) return true;

                    try { if (c.TryCast<TerrainCollider>() != null) return true; }
                    catch { }
                }
            }
            catch { }
            return false;
        }

        static bool IsRealSurface(Collider c)
        {
            if (c == null) return false;
            try
            {
                var go = c.gameObject;
                if (go == null) return false;
                int id = go.GetInstanceID();
                if (_shadowProxyIds.Contains(id)) return false;
                if (_groundIds.Contains(id)) return true;

                if (MapIdentity.HostLevelIsMap) return SurfaceIsOurs(c);

                return _rootT != null && c.transform.IsChildOf(_rootT);
            }
            catch { return false; }
        }

        static bool IsGroundHit(Collider c)
        {
            if (c == null) return false;
            try { return _groundIds.Contains(c.gameObject.GetInstanceID()); }
            catch { return false; }
        }

        internal static bool IsOurSurface(Collider c) { return IsRealSurface(c); }
        internal static bool IsOurGround(Collider c)  { return IsGroundHit(c); }
        internal static Transform OurRoot            { get { return _rootT; } }

        static readonly Dictionary<string, int> _gfBuried    = new Dictionary<string, int>(StringComparer.Ordinal);
        static readonly Dictionary<string, int> _gfSupported = new Dictionary<string, int>(StringComparer.Ordinal);
        static readonly Dictionary<string, int> _gfUnclear   = new Dictionary<string, int>(StringComparer.Ordinal);
        static readonly Dictionary<string, int> _gfNoSurface = new Dictionary<string, int>(StringComparer.Ordinal);
        static readonly Dictionary<string, int> _gfTooFar    = new Dictionary<string, int>(StringComparer.Ordinal);

        static void GfNote(Dictionary<string, int> d, string key)
        {
            if (d.Count >= 300 && !d.ContainsKey(key)) return;
            int v; d.TryGetValue(key, out v); d[key] = v + 1;
        }

        static void GfDump(string label, Dictionary<string, int> d, int max)
        {
            if (d.Count == 0) return;
            var keys = new List<string>(d.Keys);
            keys.Sort((a, b) => d[b].CompareTo(d[a]));
            _log?.Msg("[map] grounding: " + label + " (" + keys.Count + " distinct)");
            for (int i = 0; i < keys.Count && i < max; i++)
                _log?.Msg("[map]      x" + d[keys[i]] + "  " + keys[i]);
            if (keys.Count > max)
                _log?.Msg("[map]      ... " + (keys.Count - max) + " more distinct");
        }

        public static int GroundFoliage()
        {
            if (_root == null) return 0;
            _gfBuried.Clear(); _gfSupported.Clear(); _gfUnclear.Clear();
            _gfNoSurface.Clear(); _gfTooFar.Clear();
            int moved = 0, looked = 0, seated = 0, buried = 0, supported = 0;
            int unclear = 0, noSurface = 0, tooFar = 0, bufferFull = 0;
            float biggest = 0f;
            var sw = Stopwatch.StartNew();

            bool prevAuto = false, haveAuto = false;
            try { prevAuto = Physics.autoSyncTransforms; haveAuto = true; } catch { }

            try
            {

                var hits = new Il2CppStructArray<RaycastHit>(32L);
                int cap = hits.Length;

                if (haveAuto) { try { Physics.autoSyncTransforms = false; } catch { haveAuto = false; } }
                try { Physics.SyncTransforms(); } catch { }

                for (int i = 0; i < _plants.Count; i++)
                {
                    var c = _plants[i];
                    if (c == null) continue;
                    looked++;

                    Vector3 p = c.position;

                    var origin = new Vector3(p.x, p.y + 8f, p.z);
                    int n = Physics.RaycastNonAlloc(origin, Vector3.down, hits, 8f + SnapMaxDrop,
                                                    ~0, QueryTriggerInteraction.Ignore);
                    if (n >= cap) bufferFull++;
                    if (n > cap) n = cap;

                    float best = float.NegativeInfinity;
                    Collider bestCol = null;
                    Collider aboveGround = null;
                    for (int k = 0; k < n; k++)
                    {
                        var h = hits[k];
                        var col = h.collider;
                        if (!IsRealSurface(col)) continue;
                        float y = h.point.y;
                        if (y <= p.y + 0.01f)
                        {
                            if (y > best) { best = y; bestCol = col; }
                        }
                        else if (aboveGround == null && IsGroundHit(col))
                        {
                            aboveGround = col;
                        }
                    }

                    if (aboveGround != null)
                    {
                        buried++;
                        GfNote(_gfBuried, c.name + "  under " + aboveGround.transform.name);
                        continue;
                    }

                    if (bestCol == null)
                    {
                        noSurface++; GfNote(_gfNoSurface, c.name);

                        if (_gfWhyShown < 3)
                        {
                            _gfWhyShown++;
                            try
                            {
                                _log?.Msg("[map] grounding: '" + c.name + "' at ("
                                        + p.x.ToString("0.0") + ", " + p.y.ToString("0.0") + ", "
                                        + p.z.ToString("0.0") + ") found no ground. " + n
                                        + " hit(s); KeepSceneHandle=" + ShellSwap.KeepSceneHandle
                                        + ", additive scene '" + (MapIdentity.AdditiveSceneName ?? "-")
                                        + "', HostLevelIsMap=" + MapIdentity.HostLevelIsMap);
                                for (int k = 0; k < n && k < 8; k++)
                                {
                                    var hc = hits[k].collider;
                                    string cn = "?", scn = "?";
                                    try { cn = hc.gameObject.name; } catch { }
                                    try { scn = hc.gameObject.scene.name + "#"
                                              + hc.gameObject.scene.handle; } catch { }
                                    _log?.Msg("[map]    hit y=" + hits[k].point.y.ToString("0.00")
                                            + "  '" + cn + "'  scene=" + scn
                                            + "  accepted=" + IsRealSurface(hc));
                                }
                            }
                            catch { }
                        }
                        continue;
                    }

                    float drop = p.y - best;
                    if (drop <= SnapDeadband) { seated++; continue; }

                    if (!IsGroundHit(bestCol))
                    {
                        string on = "?";
                        try { on = bestCol.transform.name; } catch { }
                        if (drop <= SupportTolerance)
                        {

                            supported++;
                            GfNote(_gfSupported, c.name + "  on " + on);
                            continue;
                        }

                        unclear++;
                        GfNote(_gfUnclear, c.name + "  " + drop.ToString("F1") + " m over " + on);
                        continue;
                    }

                    if (drop > SnapMaxDrop)
                    { tooFar++; GfNote(_gfTooFar, c.name); continue; }

                    c.position = new Vector3(p.x, best, p.z);
                    if (drop > biggest) biggest = drop;
                    moved++;
                }

                _log?.Msg("[map] grounding: " + moved + " of " + looked
                        + " plant instance(s) lowered, largest drop "
                        + biggest.ToString("F2") + " m  (" + sw.ElapsedMilliseconds
                        + " ms, autoSyncTransforms was "
                        + (haveAuto ? prevAuto.ToString() : "unreadable, left alone") + ")");
                _log?.Msg("[map] grounding: left alone -- " + seated + " already seated, "
                        + buried + " at or under the ground, " + supported
                        + " standing on something, " + unclear + " unclear, "
                        + noSurface + " with no surface of ours beneath, "
                        + tooFar + " beyond the " + SnapMaxDrop + " m cap");

                int accounted = moved + seated + buried + supported + unclear + noSurface + tooFar;
                if (accounted != looked)
                    _log?.Warning("[map] grounding: " + (looked - accounted) + " plant(s) fell "
                        + "through every branch, so these buckets do not add up to " + looked
                        + " and this report cannot be trusted");
                if (bufferFull > 0)
                    _log?.Warning("[map] grounding: " + bufferFull + " ray(s) filled the "
                        + cap + "-hit buffer. RaycastNonAlloc truncates unsorted, so a "
                        + "ground hit may have been dropped. Raise the buffer.");
                if (moved == 0 && noSurface == looked && looked > 0)
                    _log?.Warning("[map] grounding: EVERY one of " + looked + " plants reported "
                        + "no surface of ours beneath. That is the hit buffer not reaching "
                        + "PhysX -- check it is an Il2CppStructArray and not a managed "
                        + "RaycastHit[].");

                GfDump("standing on a surface that is not ground", _gfSupported, 20);
                GfDump("UNCLEAR, high above a non-ground surface", _gfUnclear, 20);
                GfDump("at or under the ground, left by the one-direction rule", _gfBuried, 10);
                GfDump("no surface of ours beneath", _gfNoSurface, 10);
                GfDump("beyond the drop cap", _gfTooFar, 10);
            }
            catch (Exception e) { _log?.Warning("[map] GroundFoliage: " + e.Message); }
            finally
            {
                if (haveAuto) { try { Physics.autoSyncTransforms = prevAuto; } catch { } }
                try { Physics.SyncTransforms(); } catch { }
            }
            return moved;
        }

        static readonly string[] PlantWords =
        {
            "grass","wheat","bromeplant","cabbage","tree","bush","plant",
            "poplar","hickory","shagbark","sapling","leaf","weed","shrub",
            "palm",

            "leaves","litterpile",
        };

        static void SafeRunLocal(string what, Action a)
        {
            try { a(); } catch (Exception e) { _log?.Warning("[map] " + what + ": " + e.Message); }
        }

        static readonly string[] TrunkWords = { "beech_tree", "black_cherry",

            "europeanhornbeam_field", "europeanhornbeam_forest",

            "pine_00" };

        static readonly string[] CanopyWords = { "pine_var", "poplartree" };

        static bool IsCanopyTree(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            var lo = id.ToLowerInvariant();
            for (int i = 0; i < CanopyWords.Length; i++)
                if (lo.Contains(CanopyWords[i])) return true;
            return false;
        }

        static bool IsTrunkTree(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            var lo = id.ToLowerInvariant();
            for (int i = 0; i < TrunkWords.Length; i++)
                if (lo.Contains(TrunkWords[i])) return true;
            return false;
        }

        public static string SetFoliageShadowsLive(bool cast)
        {
            if (_root == null) return "no map built yet";
            int n = 0;
            try
            {
                var rs = _root.GetComponentsInChildren<MeshRenderer>(true);
                for (int i = 0; i < (rs != null ? rs.Length : 0); i++)
                {
                    var r = rs[i];
                    if (r == null) continue;
                    string nm = null;
                    try { nm = r.gameObject.name; } catch { continue; }
                    if (!(IsTrunkTree(nm) || IsCanopyTree(nm))) continue;
                    try
                    {
                        r.shadowCastingMode = cast
                            ? UnityEngine.Rendering.ShadowCastingMode.On
                            : UnityEngine.Rendering.ShadowCastingMode.Off;
                        n++;
                    }
                    catch { }
                }
            }
            catch (Exception e) { return "foliage shadows: " + e.Message; }
            return "mature trees " + (cast ? "CAST" : "cast NO") + " shadows on " + n
                 + " renderer(s), live. Dappled light costs frame rate; this is the "
                 + "switch to judge it by. The build's own choice is MapIdentity.FoliageShadows.";
        }

        public static string SetFoliageReceiveLive(bool recv)
        {
            if (_root == null) return "no map built yet";
            int n = 0;
            try
            {
                var rs = _root.GetComponentsInChildren<MeshRenderer>(true);
                for (int i = 0; i < (rs != null ? rs.Length : 0); i++)
                {
                    var r = rs[i];
                    if (r == null) continue;
                    string nm = null;
                    try { nm = r.gameObject.name; } catch { continue; }
                    if (nm == null || !_meshIsFoliage.Contains(nm)) continue;
                    try { r.receiveShadows = recv; n++; } catch { }
                }
            }
            catch (Exception e) { return "foliage receive: " + e.Message; }
            return "foliage " + (recv ? "RECEIVES" : "does NOT receive") + " shadows on " + n
                 + " renderer(s), live. If the grain went with it, the shadow map is the grain.";
        }

        static readonly List<Renderer> _hiddenSceneRenderers = new List<Renderer>();
        static readonly List<Collider> _hiddenSceneColliders = new List<Collider>();

        static bool NameListed(string name, List<string> prefixes)
        {
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < prefixes.Count; i++)
                if (name.StartsWith(prefixes[i], StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static void HideListedSceneObjects()
        {
            string p = Path.Combine(BundleDir, "hide_scene.txt");
            if (!File.Exists(p)) return;
            var prefixes = new List<string>();
            foreach (var raw in File.ReadAllLines(p))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                prefixes.Add(line);
            }
            if (prefixes.Count == 0) return;
            int nr = 0, nc = 0;
            var rs = Resources.FindObjectsOfTypeAll<Renderer>();
            for (int i = 0; i < rs.Length; i++)
            {
                var r = rs[i];
                try
                {
                    if (r == null || r.gameObject == null) continue;
                    if (!ShellSwap.IsAdditiveWorld(r.gameObject.scene)) continue;
                    if (!NameListed(r.gameObject.name, prefixes)) continue;
                    if (!r.enabled) continue;
                    r.enabled = false;
                    _hiddenSceneRenderers.Add(r);
                    nr++;
                }
                catch { }
            }
            var cs = Resources.FindObjectsOfTypeAll<Collider>();
            for (int i = 0; i < cs.Length; i++)
            {
                var c = cs[i];
                try
                {
                    if (c == null || c.gameObject == null) continue;
                    if (!ShellSwap.IsAdditiveWorld(c.gameObject.scene)) continue;
                    if (!NameListed(c.gameObject.name, prefixes)) continue;
                    if (!c.enabled) continue;
                    c.enabled = false;
                    _hiddenSceneColliders.Add(c);
                    nc++;
                }
                catch { }
            }
            _log?.Msg("[map] hide_scene.txt: " + nr + " renderer(s) and " + nc
                    + " collider(s) of the additive world switched off ("
                    + string.Join(", ", prefixes.ToArray()) + ")");
        }

        static void RestoreHiddenScene()
        {
            int n = 0;
            for (int i = 0; i < _hiddenSceneRenderers.Count; i++)
            {
                try
                {
                    var r = _hiddenSceneRenderers[i];
                    if (r != null) { r.enabled = true; n++; }
                }
                catch { }
            }
            for (int i = 0; i < _hiddenSceneColliders.Count; i++)
            {
                try
                {
                    var c = _hiddenSceneColliders[i];
                    if (c != null) { c.enabled = true; n++; }
                }
                catch { }
            }
            _hiddenSceneRenderers.Clear();
            _hiddenSceneColliders.Clear();
            if (n > 0) _log?.Msg("[map] hide_scene.txt: " + n + " object(s) put back");
        }

        static int _gfWhyShown;

        static int TrunkCapsules;

        static void TreeTrunkCapsules()
        {
            if (_trunks.Count == 0) return;
            int added = 0;
            for (int i = 0; i < _trunks.Count; i++)
            {
                var t = _trunks[i];
                if (t == null) continue;
                try
                {

                    var cgo = new GameObject("trunk");
                    var ct = cgo.transform;
                    ct.SetParent(t, false);
                    var cap = cgo.AddComponent<CapsuleCollider>();

                    cap.direction = 1;
                    cap.radius = 0.28f;
                    cap.height = 7f;
                    cap.center = new Vector3(0f, 3.5f, 0f);
                    added++;
                }
                catch { }
            }
            TrunkCapsules = added;
            _log?.Msg("[map] trunk capsules: " + added + " of " + _trunks.Count
                    + " tree instance(s), post-grounding, pre-nav");
        }

        static void GroundPlants()
        {
            _gfWhyShown = 0;
            if (_root == null || _plants.Count == 0) return;
            var hits = new Il2CppInterop.Runtime.InteropTypes.Arrays
                           .Il2CppStructArray<RaycastHit>(16L);
            var centre = Bounds().center;
            int snapped = 0, borrowed = 0, checkedN = 0;

            float FloorUnder(Vector3 at)
            {
                int n = Physics.RaycastNonAlloc(new Vector3(at.x, at.y + 4f, at.z),
                                                Vector3.down, hits, 90f, ~0,
                                                QueryTriggerInteraction.Ignore);
                float best = float.NegativeInfinity;
                for (int h = 0; h < n && h < hits.Length; h++)
                {
                    var c = hits[h].collider;
                    if (c == null) continue;
                    if (!SurfaceIsOurs(c)) continue;

                    if (hits[h].point.y > at.y + 3.5f) continue;
                    if (hits[h].point.y > best) best = hits[h].point.y;
                }
                return best;
            }

            for (int i = 0; i < _plants.Count; i++)
            {
                var t = _plants[i];
                if (t == null) continue;
                checkedN++;
                var p = t.position;
                float floor = FloorUnder(p);
                if (floor == float.NegativeInfinity)
                {

                    var dir = centre - p; dir.y = 0f;
                    if (dir.sqrMagnitude > 1f)
                    {
                        var inward = p + dir.normalized * 6f;
                        floor = FloorUnder(inward);
                        if (floor != float.NegativeInfinity) borrowed++;
                    }
                }
                if (floor == float.NegativeInfinity) continue;

                if (p.y - floor > 0.25f)
                {
                    t.position = new Vector3(p.x, floor + 0.02f, p.z);
                    snapped++;
                }
            }
            _log?.Msg("[map] grounded " + snapped + " floating plant(s) of " + checkedN
                    + " checked (" + borrowed + " at the rim borrowed inland ground). "
                    + "The ring of hovering trees at the map edge was this.");
        }

        static readonly string[] StructureWords =
        {
            "rungladder", "palette", "pallet", "ladder",
        };

        static bool IsStructure(string l)
        {
            for (int i = 0; i < StructureWords.Length; i++)
                if (l.Contains(StructureWords[i])) return true;
            return false;
        }

        static bool IsPlant(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            string l = id.ToLowerInvariant();
            if (IsStructure(l)) return false;
            if (l.StartsWith("fol_", StringComparison.Ordinal)) return true;
            for (int i = 0; i < PlantWords.Length; i++)
                if (l.Contains(PlantWords[i])) return true;
            return false;
        }

        static bool IsShadowDuplicate(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            return id.StartsWith("DQ_bf_", StringComparison.Ordinal)
                || string.Equals(id, "DQ_doormullion", StringComparison.Ordinal)
                || string.Equals(id, "DQ_stairwelledge", StringComparison.Ordinal);
        }

        static bool IsDecal(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            return id.IndexOf("_DQ_", StringComparison.OrdinalIgnoreCase) >= 0
                || id.StartsWith("DQ_", StringComparison.OrdinalIgnoreCase);
        }

        static bool ShouldCollide(string id, Mesh mesh)
        {
            if (IsPlant(id)) return false;
            if (IsDecal(id)) return false;

            if (MeshFactory.IsWaterMesh(id)) return false;
            var s = mesh.bounds.size;
            return Mathf.Max(s.x, Mathf.Max(s.y, s.z)) > 0.5f;
        }

        static GameObject _lighting;

        static UnityEngine.Rendering.HighDefinition.Exposure _nightExposure;

        static UnityEngine.Rendering.HighDefinition.Exposure _dayExposure;
        static UnityEngine.Rendering.HighDefinition.HDAdditionalLightData _moonHd;
        static UnityEngine.Rendering.HighDefinition.HDAdditionalLightData _fillHd;
        static GameObject _moonGo;

        public static float MoonLux = 1.4f;

        public static float NightEV = 3.2f;

        public static float DayEV = 15.5f;

        public static void AdjustDayEV(float delta)
        {
            if (_dayExposure == null) { _log?.Msg("[tune] no day exposure yet"); return; }
            DayEV += delta;
            try { _dayExposure.fixedExposure.value = DayEV; } catch { }
            _log?.Msg("[tune] day exposure EV100 = " + DayEV.ToString("F1")
                    + "  (higher = darker; 14.5 was the desert value)");
        }

        public static float SunElevation = 64f;
        public static float SunAzimuth = 135f;

        public static float SunKelvin = 6100f;

        public static float HostShadowDimmer = 0.85f;

        static bool _hostLightRead;
        static bool _hostLight = true;

        static bool? _hostLitOverride;

        public static bool DayIsHostLit = true;

        public static bool UseHostLighting
        {
            get
            {
                if (_hostLitOverride.HasValue)
                    return _hostLitOverride.Value && MapIdentity.OwnSky;
                if (!_hostLightRead)
                {
                    _hostLightRead = true;
                    try
                    {
                        var cfg = MelonLoader.MelonPreferences.GetCategory("MapLoader")
                               ?? MelonLoader.MelonPreferences.CreateCategory("MapLoader");
                        var e = cfg.GetEntry<bool>("HostLighting")
                             ?? cfg.CreateEntry("HostLighting", true,
                                  description: "Light our map with the HOST level's own sun, sky and "
                                             + "volumes instead of building a rig. On by default: the "
                                             + "shipped level's lighting is authored and baked, ours is "
                                             + "not, and ours rendered black. Off rebuilds our own rig.");
                        _hostLight = e.Value;
                    }
                    catch { _hostLight = true; }
                }

                return _hostLight && MapIdentity.OwnSky;
            }
        }

        public struct LightingParams
        {
            public bool hostLit;
            public float sunLux;
            public float sunKelvin;
            public float sunElev;
            public float sunAz;
            public float fillLux;
            public float dayEv;
            public bool apvOff;
            public bool ssgi;
            public int skyMode;
            public float indirectMult;
            public int probeUsage;

            public static LightingParams Defaults()
            {
                var p = new LightingParams
                {
                    hostLit = UseHostLighting,
                    sunLux = SkyRig.SunLux,
                    sunKelvin = SkyRig.SunKelvin,
                    sunElev = SkyRig.SunElevation,
                    sunAz = SkyRig.SunAzimuth,
                    fillLux = SkyRig.FillLux,
                    dayEv = SkyRig.CityDayEV,
                    apvOff = ApvGate.PrefOn,
                    ssgi = SsgiPrefOn,
                    skyMode = SkyRig.DaySkyMode,
                    indirectMult = SkyRig.SkyFill,
                    probeUsage = 1,
                };

                if (p.hostLit)
                {
                    float active = 0f;
                    try { active = ShellSwap.HostSunLux(); } catch { }
                    if (active < 5000f)
                    {
                        var own = OwnRig(p.apvOff);
                        own.ssgi = p.ssgi;
                        own.skyMode = p.skyMode;
                        _log?.Msg("[map] LIGHTING: host-lit day requested, but the host's "
                                + "active sun measures " + active.ToString("F0")
                                + " lux -- a night scene. Building our own day rig ("
                                + own.sunLux.ToString("F0") + " lux at "
                                + own.sunElev.ToString("F0") + " deg, EV "
                                + own.dayEv.ToString("F2") + ") instead of standing in "
                                + "moonlight at noon exposure.");
                        return own;
                    }
                }
                return p;
            }

            public static LightingParams OwnRig(bool apvOff)
            {

                const float lux = 52241.375f, elev = 64f;

                float canopy = MapIdentity.CanopyFactor;
                float e = lux * Mathf.Sin(elev * Mathf.Deg2Rad) * canopy + 0.20f * lux;
                float ev = Mathf.Clamp(Mathf.Log(8f * CityAlbedo * e / Mathf.PI, 2f), 6f, 17f);
                return new LightingParams
                {
                    hostLit = false,
                    sunLux = lux,
                    sunKelvin = 6727f,
                    sunElev = elev,
                    sunAz = 135f,

                    fillLux = 6000f,
                    dayEv = ev,
                    apvOff = apvOff,
                    ssgi = false,
                    skyMode = 0,

                    indirectMult = SkyRig.SkyFill,
                    probeUsage = 1,
                };
            }
        }

        static LightingParams _lightParams;
        public static LightingParams ActiveLightingParams { get { return _lightParams; } }

        public static string HarnessRunRequest;

        public static bool HarnessBuild;

        public static void AddLighting() { AddLighting(LightingParams.Defaults()); }

        public static void AddLighting(LightingParams p)
        {
            if (MapIdentity.HostLevelIsMap)
            {

                _log?.Msg("[map] host level IS the map: its own lighting stands, ours is skipped");

                if (MapIdentity.DefaultHostLighting)
                    _log?.Msg("[map] ...and its SHADOWS too: no SHADOWS_ADDITIVE volume "
                            + "on this map, the host's shadow settings stand as shipped");
                else
                    AdditiveShadowSettings();
                return;
            }
            if (_lighting != null) return;

            _lightParams = p;
            if (MapIdentity.OwnSky)
            {
                SkyRig.SunLux = p.sunLux;
                SkyRig.SunKelvin = p.sunKelvin;
                SkyRig.SunElevation = p.sunElev;
                SkyRig.SunAzimuth = p.sunAz;
                SkyRig.FillLux = p.fillLux;
                SkyRig.CityDayEV = p.dayEv;
                SkyRig.SkyFill = p.indirectMult;
                SkyRig.DaySkyMode = p.skyMode;
            }

            bool hostLit = p.hostLit;

            DayIsHostLit = hostLit;
            if (hostLit)
                _log?.Msg("[map] LIGHTING: the DAY rig is EMPTY on purpose. This map stands in "
                        + "daylight under the host level's own sun, sky, exposure and volumes, "
                        + "exactly as the game ships them. NIGHT is still ours, and '=' switches "
                        + "between them -- going to night switches the host's lights OFF, coming "
                        + "back switches them on. MelonPreferences [MapLoader] HostLighting=false "
                        + "builds our own day rig instead.");
            try
            {
                SkyRig.Init(_log);
                _lighting = new GameObject(SceneLighting.Root);

                if (_scene.IsValid()) SceneManager.MoveGameObjectToScene(_lighting, _scene);
                else if (_root != null) _lighting.transform.SetParent(_root.transform, true);

                var day = new GameObject(SceneLighting.Day);
                day.transform.SetParent(_lighting.transform, false);

                if (!hostLit)
                MakeSun(day.transform, "SUN_Day", SkyRig.DaySunLux, SkyRig.DaySunKelvin,
                        SkyRig.DaySunEuler, true, SkyRole.Sun);

                for (int q = 0; q < 4; q++)
                    MakeSun(day.transform, "SUN_DayFill" + q,
                            SkyRig.FillLux, 6200f,

                            new Vector3(20f, q * 90f + 45f, 0f), false, SkyRole.None);

                DayArt.Build(day.transform);

                var night = new GameObject(SceneLighting.Night);
                night.transform.SetParent(_lighting.transform, false);

                MakeSun(night.transform, "SUN_NightMoon", MoonLux, 6900f,
                        new Vector3(52.0f, 205.0f, 0.0f), true, SkyRole.Moon);

                MakeSun(night.transform, "SUN_NightFill", MoonLux / 14f, 6700f,
                        new Vector3(30.0f, 25.0f, 0.0f), false);

                FakeBounce.Build(night.transform);
                NightArt.Build(night.transform);

                FakeBounce.SetMoonDirection(Quaternion.Euler(52.0f, 205.0f, 0.0f) * Vector3.forward);

                night.SetActive(false);

                DayEV = SkyRig.DayEV;

                if (!hostLit) AddExposureVolume(day, DayEV, false);
                else
                {

                    try
                    {
                        var sgo = new GameObject("SHADOWS_HOSTLIT");
                        sgo.transform.SetParent(day.transform, false);
                        var sv = sgo.AddComponent<UnityEngine.Rendering.Volume>();
                        sv.isGlobal = true;
                        sv.priority = OwnVolumePriority();
                        sv.weight = 1f;
                        var sp = ScriptableObject.CreateInstance(
                            Il2CppInterop.Runtime.Il2CppType.Of<UnityEngine.Rendering.VolumeProfile>())
                            .Cast<UnityEngine.Rendering.VolumeProfile>();
                        sv.sharedProfile = sp;
                        AddShadowSettings(sp, "DAY(host-lit)");
                        AddGrade(sp);

                        SkyRig.ApplySkyLook();
                        float camEv = AddCityExposure(sp);

                        SkyRig.SunElevation = SunElevation;
                        SkyRig.SunAzimuth = SunAzimuth;
                        SkyRig.DomeExposure = camEv - 1.9f;
                        SkyRig.AddHostLitSky(sp, "DAY(host-lit)");

                        SkyRig.AddIndirect(sp, "DAY(host-lit)");

                        AddMicroShadows(sp, "DAY(host-lit)");

                        if (MapIdentity.SeaSky) AddSeaFog(sp, "DAY(host-lit)");

                        _dayProfile = sp;
                        if (SsgiPrefOn) AddSsgi(sp, "DAY(host-lit)");
                    }
                    catch (Exception se) { _log?.Warning("[map] host-lit shadow volume: " + se.Message); }
                }
                AddExposureVolume(night, NightEV, true);

                _log?.Msg("[map] lighting: day + night rigs created, exposure owned "
                        + "(night EV " + NightEV + ", day EV " + DayEV + ")");
            }
            catch (Exception e) { _log?.Warning("[map] AddLighting: " + e.Message); }
        }

        static float _ownPriority;

        static GameObject _addShadowGo;

        static string ShadowSettingsNow()
        {
            try
            {
                var vols = UnityEngine.Object.FindObjectsOfType<UnityEngine.Rendering.Volume>(true);
                for (int i = 0; i < (vols != null ? vols.Length : 0); i++)
                {
                    var v = vols[i];
                    if (v == null || !v.enabled || !v.gameObject.activeInHierarchy) continue;
                    var prof = v.sharedProfile;
                    if (prof == null) continue;
                    var comps = prof.components;
                    for (int c = 0; c < (comps != null ? comps.Count : 0); c++)
                    {
                        var sh = comps[c] != null
                            ? comps[c].TryCast<UnityEngine.Rendering.HighDefinition.HDShadowSettings>()
                            : null;
                        if (sh == null) continue;
                        return "'" + v.gameObject.name + "' distance="
                             + sh.maxShadowDistance.value.ToString("F0") + " m cascades="
                             + sh.cascadeShadowSplitCount.value;
                    }
                }
                return "NOTHING -- no live volume carries HDShadowSettings, so HDRP is on "
                     + "the pipeline asset's defaults";
            }
            catch (Exception e) { return "unreadable: " + e.Message; }
        }

        static float _streamBudgetWas = -1f;

        static float StreamBudgetCapMB()
        {
            try
            {
                var cfg = MelonLoader.MelonPreferences.GetCategory("MapLoader")
                       ?? MelonLoader.MelonPreferences.CreateCategory("MapLoader");
                var e = cfg.GetEntry<float>("AdditiveTextureBudgetCapMB")
                     ?? cfg.CreateEntry("AdditiveTextureBudgetCapMB", 6144f,
                          description: "Ceiling (MB) for the texture streaming budget while an "
                                     + "additive-world mission is up. These missions hold TWO "
                                     + "stock scenes resident at once, which starves the shipped "
                                     + "budget: the streamer sheds mips, mask maps read AO=0 and "
                                     + "surfaces render black in texture space. The budget is "
                                     + "raised only as far as Texture.desiredTextureMemory asks "
                                     + "for, and never past this cap. Lower it if VRAM is tight.");
                return e != null ? e.Value : 6144f;
            }
            catch { return 6144f; }
        }

        static bool _streamActiveWas;
        static bool _streamTouched;

        const float VramTextureShare = 0.55f;

        static float StreamBudgetMB()
        {
            try
            {
                var cfg = MelonLoader.MelonPreferences.GetCategory("MapLoader")
                       ?? MelonLoader.MelonPreferences.CreateCategory("MapLoader");
                var e = cfg.GetEntry<float>("AdditiveTextureBudgetMB")
                     ?? cfg.CreateEntry("AdditiveTextureBudgetMB", 0f,
                          description: "Texture streaming budget (MB) while an additive-world "
                                     + "mission is up. 0 = automatic, which is 55% of the card's "
                                     + "VRAM. These missions hold TWO stock scenes resident at "
                                     + "once and measured 9222 MB of textures on a 7956 MB card; "
                                     + "over VRAM the driver evicts and repages, which is the "
                                     + "black patches, the moving quality bands and the freezes. "
                                     + "Set a number here to override.");
                float v = e != null ? e.Value : 0f;
                if (v > 0f) return v;
            }
            catch { }
            try
            {
                int vram = SystemInfo.graphicsMemorySize;
                if (vram > 512) return Mathf.Round(vram * VramTextureShare);
            }
            catch { }
            return 3072f;
        }

        static void RelieveTextureStreaming()
        {
            try
            {
                const double MB = 1024.0 * 1024.0;
                int vram = 0;
                try { vram = SystemInfo.graphicsMemorySize; } catch { }
                double current = UnityEngine.Texture.currentTextureMemory / MB;
                bool active = QualitySettings.streamingMipmapsActive;
                float budget = QualitySettings.streamingMipmapsMemoryBudget;

                _log?.Msg("[stream] VRAM=" + vram + " MB, resident texture memory="
                        + current.ToString("F0") + " MB, streaming active=" + active
                        + ", budget=" + budget.ToString("F0") + " MB");

                if (vram > 0 && current <= vram * VramTextureShare)
                {
                    _log?.Msg("[stream] textures fit inside " + (VramTextureShare * 100f).ToString("F0")
                            + "% of VRAM; leaving streaming as the game shipped it.");
                    return;
                }

                float want = StreamBudgetMB();
                if (!_streamTouched)
                {
                    _streamActiveWas = active;
                    _streamBudgetWas = budget;
                    _streamTouched = true;
                }

                _log?.Warning("[stream] OVER VRAM: " + current.ToString("F0") + " MB of textures on a "
                            + vram + " MB card (streaming " + (active ? "ON" : "OFF")
                            + ", would want ~" + want.ToString("F0") + " MB). Reported only: this "
                            + "costs frame time and stalls, but Villa runs HIGHER than Nuketown on "
                            + "the same card and is clean, so it is not the black.");
            }
            catch (Exception e) { _log?.Warning("[stream] RelieveTextureStreaming: " + e.Message); }
        }

        static readonly System.Collections.Generic.List<Behaviour> _decalsOff =
            new System.Collections.Generic.List<Behaviour>();

        internal static string DecalConsole(string arg)
        {
            try
            {
                arg = (arg ?? "").Trim().ToLowerInvariant();
                var all = UnityEngine.Object.FindObjectsOfType<
                    UnityEngine.Rendering.HighDefinition.DecalProjector>(true);
                int total = all != null ? all.Length : 0;
                if (arg == "off")
                {
                    int n = 0;
                    for (int i = 0; i < total; i++)
                    {
                        var d = all[i];
                        if (d == null || !d.enabled) continue;
                        d.enabled = false;
                        _decalsOff.Add(d);
                        n++;
                    }
                    return "disabled " + n + " DecalProjector(s) of " + total
                         + ". If the black smears vanish NOW, the decal path is the "
                         + "cause. 'odg decals on' restores.";
                }
                if (arg == "on")
                {
                    int n = 0;
                    for (int i = 0; i < _decalsOff.Count; i++)
                    {
                        var d = _decalsOff[i];
                        if (d == null) continue;
                        try { d.enabled = true; n++; } catch { }
                    }
                    _decalsOff.Clear();
                    return "re-enabled " + n + " DecalProjector(s)";
                }
                int on = 0, inAdd = 0;
                for (int i = 0; i < total; i++)
                {
                    var d = all[i];
                    if (d == null) continue;
                    if (d.enabled && d.gameObject.activeInHierarchy) on++;
                    try
                    {
                        if (ShellSwap.KeepSceneHandle != 0
                            && d.gameObject.scene.handle == ShellSwap.KeepSceneHandle) inAdd++;
                    }
                    catch { }
                }
                return "decal projectors: " + total + " total, " + on + " live, "
                     + inAdd + " in the additive scene  |  odg decals off | on";
            }
            catch (Exception e) { return "odg decals failed: " + e.Message; }
        }

        static int _ambStage;
        static float _ambNextAt;

        static float AmbientDc()
        {
            try
            {
                var p = RenderSettings.ambientProbe;
                return Mathf.Abs(p[0, 0]) + Mathf.Abs(p[1, 0]) + Mathf.Abs(p[2, 0]);
            }
            catch { return -1f; }
        }

        static GameObject _fillGo;

        static float _fillLux = 1800f;

        static void BuildAmbientFill()
        {
            if (_fillGo != null) return;
            var go = new GameObject("AMBIENT_FILL");
            if (_root != null)
            {
                try { go.transform.SetParent(_root.transform, false); } catch { }
            }
            _fillGo = go;
            MakeSun(go.transform, "FILL_N", _fillLux, 6500f, new Vector3(40f,   0f, 0f), false);
            MakeSun(go.transform, "FILL_E", _fillLux, 6500f, new Vector3(40f,  90f, 0f), false);
            MakeSun(go.transform, "FILL_S", _fillLux, 6500f, new Vector3(40f, 180f, 0f), false);
            MakeSun(go.transform, "FILL_W", _fillLux, 6500f, new Vector3(40f, 270f, 0f), false);
            _log?.Msg("[amb] AMBIENT FILL: 4 shadowless directional light(s) at "
                    + _fillLux.ToString("F0") + " lux. The ambient probe is black on this "
                    + "map and the scene's sky feeds it nothing, so the shade is lifted by "
                    + "light instead -- WITHOUT binding a sky, which is what replaced the "
                    + "real one last build. 'odg amb lux <n>' tunes it live, 'odg amb "
                    + "restore' removes it.");
        }

        static void SetFillLux(float lux)
        {
            _fillLux = Mathf.Clamp(lux, 0f, 40000f);
            if (_fillGo == null) return;
            var ls = _fillGo.GetComponentsInChildren<
                UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>(true);
            for (int i = 0; i < (ls != null ? ls.Length : 0); i++)
            {
                try { ls[i].SetIntensity(_fillLux, UnityEngine.Rendering.LightUnit.Lux); }
                catch { }
            }
        }

        public static void AmbientGuardTick()
        {
            if (!Done || !MapIdentity.HostLevelIsMap || _ambStage >= 4) return;
            if (!_addSwept) return;
            if (Time.realtimeSinceStartup < _ambNextAt) return;
            _ambNextAt = Time.realtimeSinceStartup + 3f;
            float dc = AmbientDc();
            if (_ambStage == 0)
            {
                _log?.Msg("[amb] ambient probe DC=" + dc.ToString("F4")
                        + (dc >= 0.01f
                           ? "  (healthy; indirect is reaching the map, leaving it alone)"
                           : "  *** BLACK: no indirect light at all. Every surface the sun "
                           + "misses renders at zero, which is the black. Lifting it with "
                           + "shadowless fill light -- NOT by binding a sky. ***"));
                if (dc >= 0.01f) { _ambStage = 4; return; }
                BuildAmbientFill();
                _ambStage = 3;
                return;
            }
            if (_ambStage == 3)
            {
                _log?.Msg("[amb] fill is up at " + _fillLux.ToString("F0")
                        + " lux; the ambient probe itself stays black by design (HDRP "
                        + "owns it) -- the shade is now lit by real lights instead. "
                        + "'odg amb lux <n>' if it is too dark or too flat.");
                _ambStage = 4;
            }
        }

        internal static string AmbConsole(string arg)
        {
            try
            {
                arg = (arg ?? "").Trim().ToLowerInvariant();
                if (arg == "fix")
                {
                    BuildAmbientFill();
                    _ambStage = 3;
                    _ambNextAt = 0f;
                    return "fill lights applied by hand at " + _fillLux.ToString("F0") + " lux.";
                }
                if (arg.StartsWith("lux"))
                {
                    var rest = arg.Length > 3 ? arg.Substring(3).Trim() : "";
                    float v;
                    if (float.TryParse(rest, out v))
                    {
                        BuildAmbientFill();
                        SetFillLux(v);
                        return "fill = " + _fillLux.ToString("F0") + " lux x4, live. "
                             + "Higher lifts the shade further and flattens it; 0 is off. "
                             + "The sun is ~52,000 lux for scale.";
                    }
                    return "odg amb lux <n>   (now " + _fillLux.ToString("F0") + ")";
                }
                if (arg == "restore")
                {
                    if (_fillGo != null) { UnityEngine.Object.Destroy(_fillGo); _fillGo = null; }
                    _ambStage = 0;
                    _ambNextAt = 0f;
                    return "fill removed and the sky left exactly as the scene authored it; "
                         + "the guard will re-measure.";
                }
                return "ambient probe DC=" + AmbientDc().ToString("F4")
                     + " fill=" + (_fillGo != null ? _fillLux.ToString("F0") + " lux" : "none")
                     + " stage=" + _ambStage
                     + "  |  odg amb fix | lux <n> | restore";
            }
            catch (Exception e) { return "odg amb failed: " + e.Message; }
        }

        static UnityEngine.Rendering.HighDefinition.HDAdditionalLightData FindSunHd()
        {
            try
            {
                Light best = null; float bright = -1f;
                var ls = UnityEngine.Object.FindObjectsOfType<Light>();
                for (int i = 0; i < (ls != null ? ls.Length : 0); i++)
                {
                    var L = ls[i];
                    if (L == null || L.type != LightType.Directional) continue;
                    if (!L.enabled || !L.gameObject.activeInHierarchy) continue;
                    if (L.shadows == LightShadows.None) continue;
                    if (L.intensity > bright) { bright = L.intensity; best = L; }
                }
                return best != null
                    ? best.GetComponent<UnityEngine.Rendering.HighDefinition
                        .HDAdditionalLightData>() : null;
            }
            catch { return null; }
        }

        internal static string ShadowConsole(string arg)
        {
            try
            {
                arg = (arg ?? "").Trim().ToLowerInvariant();
                var hd = FindSunHd();
                if (hd == null) return "no shadow-casting directional light found";
                string who = hd.gameObject.name;

                if (arg.StartsWith("normal") || arg.StartsWith("slope"))
                {
                    bool normal = arg.StartsWith("normal");
                    var rest = arg.Substring(normal ? 6 : 5).Trim();
                    float v;
                    if (float.TryParse(rest, out v))
                    {
                        v = Mathf.Clamp(v, 0f, 5f);
                        try
                        {
                            if (normal) hd.normalBias = v; else hd.slopeBias = v;
                        }
                        catch (Exception e) { return "could not set it: " + e.Message; }
                        return (normal ? "normalBias" : "slopeBias") + " = " + v.ToString("F2")
                             + " on '" + who + "', live. Raise until the acne bars vanish; too "
                             + "far and shadows detach from their casters.";
                    }
                    return "odg shadow " + (normal ? "normal" : "slope") + " <0..5>";
                }
                if (arg.StartsWith("soft"))
                {
                    var rest = arg.Substring(4).Trim();
                    float deg;
                    if (float.TryParse(rest, out deg))
                    {
                        deg = Mathf.Clamp(deg, 0.1f, 10f);
                        try { hd.angularDiameter = deg; }
                        catch (Exception e) { return "could not set it: " + e.Message; }
                        return "sun angular diameter = " + deg.ToString("F2") + " deg on '" + who
                             + "'. Wider = softer penumbra -- but ONLY if this build compiled "
                             + "PCSS (see the [shell] sun shadows line for the filtering quality).";
                    }
                    return "odg shadow soft <0.1..10>  (sun angular diameter, degrees)";
                }
                if (arg.StartsWith("res"))
                {
                    var rest = arg.Substring(3).Trim();
                    int r;
                    if (int.TryParse(rest, out r))
                    {
                        try { hd.SetShadowResolution(Mathf.Clamp(r, 256, 8192)); }
                        catch (Exception e) { return "could not set it: " + e.Message; }
                        return "shadow resolution = " + r + " on '" + who + "'. More texels is "
                             + "the other cure for acne, at real cost.";
                    }
                    return "odg shadow res <256..8192>";
                }
                string bias = "?";
                try { bias = "normalBias=" + hd.normalBias.ToString("F2")
                           + " slopeBias=" + hd.slopeBias.ToString("F2"); }
                catch { }
                return "shadow sun '" + who + "' intensity=" + hd.gameObject
                        .GetComponent<Light>().intensity.ToString("F0")
                     + "  " + bias
                     + "  |  odg shadow normal <n> | slope <n> | res <n>";
            }
            catch (Exception e) { return "odg shadow failed: " + e.Message; }
        }

        const string NL = "\n";

        internal static string MarkHere(string label)
        {
            try
            {
                Vector3 eye;
                if (!PlayerBody.TryPosition(out eye))
                    return "could not read the player position";
                if (!PlayerBody.HasMovableRig)
                    return "the player rig is not resolved yet -- wait for the map to finish "
                         + "loading and try again";

                Vector3 feet = eye;
                try
                {
                    var hits = new Il2CppInterop.Runtime.InteropTypes.Arrays
                                   .Il2CppStructArray<RaycastHit>(8L);
                    int n = Physics.RaycastNonAlloc(new Vector3(eye.x, eye.y + 0.5f, eye.z),
                                                    Vector3.down, hits, 8f, ~0,
                                                    QueryTriggerInteraction.Ignore);
                    float best = float.NegativeInfinity;
                    for (int i = 0; i < n && i < hits.Length; i++)
                    {
                        var h = hits[i];
                        if (h.collider == null) continue;
                        if (h.point.y > best) best = h.point.y;
                    }
                    if (best > float.NegativeInfinity) feet = new Vector3(eye.x, best, eye.z);
                }
                catch { }

                string map = SceneName;
                string lab = string.IsNullOrEmpty(label) ? "mark" : label;
                string line = map + "\t" + lab
                            + "\tfeet\t" + feet.x.ToString("F2") + "\t"
                            + feet.y.ToString("F2") + "\t" + feet.z.ToString("F2")
                            + "\teye\t" + eye.x.ToString("F2") + "\t"
                            + eye.y.ToString("F2") + "\t" + eye.z.ToString("F2");
                string path = "?";
                try
                {
                    path = System.IO.Path.Combine(
                        UnityEngine.Application.dataPath, "..", "UserData", "odg_marks.txt");
                    System.IO.File.AppendAllText(path, line + System.Environment.NewLine);
                }
                catch (Exception e) { return "recorded nothing: " + e.Message; }

                _log?.Msg("[mark] " + line);
                return "MARKED '" + lab + "' on " + map
                     + "   feet (" + feet.x.ToString("F1") + ", " + feet.y.ToString("F1")
                     + ", " + feet.z.ToString("F1") + ")"
                     + "   -> UserData\\odg_marks.txt. Walk to the next spot and run "
                     + "'odg here <name>' again; every mark is appended.";
            }
            catch (Exception e) { return "odg here failed: " + e.Message; }
        }

        static readonly System.Collections.Generic.List<Renderer> _lmR =
            new System.Collections.Generic.List<Renderer>();
        static readonly System.Collections.Generic.List<int> _lmWas =
            new System.Collections.Generic.List<int>();

        internal static string LightmapConsole(string arg)
        {
            try
            {
                arg = (arg ?? "").Trim().ToLowerInvariant();
                int keep = ShellSwap.KeepSceneHandle;
                if (keep == 0) return "no additive world on this map";

                if (arg == "off")
                {
                    if (_lmR.Count > 0) return "already off; 'odg lm on' restores first";
                    int n = 0;
                    var rr = UnityEngine.Object.FindObjectsOfType<Renderer>(true);
                    for (int i = 0; i < (rr != null ? rr.Length : 0); i++)
                    {
                        var r = rr[i];
                        if (r == null) continue;
                        try
                        {
                            if (r.gameObject.scene.handle != keep) continue;
                            int li = r.lightmapIndex;
                            if (li < 0 || li >= 65534) continue;
                            _lmR.Add(r); _lmWas.Add(li);
                            r.lightmapIndex = -1;
                            n++;
                        }
                        catch { }
                    }
                    return "dropped " + n + " additive renderer(s) off the lightmap path. "
                         + "If the black patches on walls and floors VANISH now, they were "
                         + "sampling the HOST's night bake. 'odg lm on' restores.";
                }
                if (arg == "on")
                {
                    int n = 0;
                    for (int i = 0; i < _lmR.Count; i++)
                    {
                        var r = _lmR[i];
                        if (r == null) continue;
                        try { r.lightmapIndex = _lmWas[i]; n++; } catch { }
                    }
                    _lmR.Clear(); _lmWas.Clear();
                    return "restored " + n + " lightmap index/indices";
                }

                int lit = 0, mn = int.MaxValue, mx = -1;
                var all = UnityEngine.Object.FindObjectsOfType<Renderer>(true);
                for (int i = 0; i < (all != null ? all.Length : 0); i++)
                {
                    var r = all[i];
                    if (r == null) continue;
                    try
                    {
                        if (r.gameObject.scene.handle != keep) continue;
                        int li = r.lightmapIndex;
                        if (li < 0 || li >= 65534) continue;
                        lit++; if (li < mn) mn = li; if (li > mx) mx = li;
                    }
                    catch { }
                }
                return "additive lightmapped renderers: " + lit
                     + (mx >= 0 ? ("  index range " + mn + ".." + mx) : "  (none)")
                     + ".  Beach_Compound ships 4 lightmaps, Suburb Night ships 13, so a "
                     + "correct merge puts the additive scene's at 13..16. Anything inside "
                     + "0..12 here is the HOST's bake."
                     + (_lmR.Count > 0 ? "  [currently OFF]" : "")
                     + "  |  odg lm off | on";
            }
            catch (Exception e) { return "odg lm failed: " + e.Message; }
        }

        internal static string VolumeConsole(string arg)
        {
            try
            {
                arg = (arg ?? "").Trim().ToLowerInvariant();
                var vols = UnityEngine.Object.FindObjectsOfType<UnityEngine.Rendering.Volume>(true);
                var sb = new System.Text.StringBuilder();
                int live = 0, off = 0;
                var rows = new System.Collections.Generic.List<string>();
                for (int i = 0; i < (vols != null ? vols.Length : 0); i++)
                {
                    var v = vols[i];
                    if (v == null) continue;
                    bool on = false;
                    try { on = v.enabled && v.gameObject.activeInHierarchy; } catch { }
                    if (on) live++; else off++;
                    if (arg == "live" && !on) continue;
                    string scene = "?";
                    try { scene = v.gameObject.scene.IsValid() ? v.gameObject.scene.name : "<none>"; }
                    catch { }
                    string has = "";
                    try
                    {
                        var prof = v.sharedProfile;
                        var comps = prof != null ? prof.components : null;
                        for (int c = 0; c < (comps != null ? comps.Count : 0); c++)
                        {
                            var cc = comps[c];
                            if (cc == null) continue;
                            string tn = "";
                            try { tn = cc.GetIl2CppType().Name; } catch { }
                            if (tn.Contains("VisualEnvironment") || tn.Contains("Sky")
                                || tn.Contains("Exposure") || tn.Contains("IndirectLighting")
                                || tn.Contains("Fog"))
                                has += (has.Length > 0 ? "," : "") + tn;
                        }
                    }
                    catch { }
                    if (has.Length == 0 && arg != "all") continue;
                    string prio = "?";
                    try { prio = v.priority.ToString("F0"); } catch { }
                    rows.Add(NL + "  " + (on ? "ON " : "off")
                           + "  [" + scene + "]  '" + v.gameObject.name + "'"
                           + "  prio=" + prio + "  " + has);
                }
                sb.Append("volumes: ").Append(live).Append(" live, ").Append(off)
                  .Append(" disabled. Lighting-relevant:");
                for (int i = 0; i < rows.Count && i < 40; i++) sb.Append(rows[i]);
                if (rows.Count == 0)
                    sb.Append(NL + "  NONE LIVE PROVIDES A SKY, EXPOSURE OR FOG. That is exactly "
                            + "why the ambient probe reads zero: nothing is left to feed it.");
                sb.Append(NL + "  |  odg volumes | live | all");
                return sb.ToString();
            }
            catch (Exception e) { return "odg volumes failed: " + e.Message; }
        }

        internal static string WhatConsole()
        {
            try
            {
                var cam = Camera.main;
                if (cam == null) return "no camera";
                var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                var hits = new Il2CppInterop.Runtime.InteropTypes.Arrays
                               .Il2CppStructArray<RaycastHit>(16L);
                int n = Physics.RaycastNonAlloc(ray, hits, 200f, ~0,
                                                QueryTriggerInteraction.Ignore);
                var sb = new System.Text.StringBuilder();
                sb.Append("looking from ").Append(cam.transform.position.ToString("F1"))
                  .Append(" -- ").Append(n).Append(" hit(s):");

                var idx = new System.Collections.Generic.List<int>();
                for (int i = 0; i < n && i < hits.Length; i++) idx.Add(i);
                idx.Sort((a, b) => hits[a].distance.CompareTo(hits[b].distance));

                float prev = -1f;
                for (int k = 0; k < idx.Count && k < 8; k++)
                {
                    var h = hits[idx[k]];
                    var c = h.collider;
                    if (c == null) continue;
                    var go = c.gameObject;
                    string scene = "?";
                    try { scene = go.scene.IsValid() ? go.scene.name : "<none>"; } catch { }
                    string owner = scene == SceneName ? "OURS"
                                 : (ShellSwap.KeepSceneHandle != 0
                                    && go.scene.handle == ShellSwap.KeepSceneHandle)
                                   ? "ADDITIVE" : "HOST";
                    var r = go.GetComponent<Renderer>();
                    string mat = "no renderer", shader = "-", lm = "-", drawing = "-";
                    string keywords = "";
                    if (r != null)
                    {
                        try { mat = r.sharedMaterial != null ? r.sharedMaterial.name : "<null mat>"; } catch { }
                        try { shader = r.sharedMaterial != null && r.sharedMaterial.shader != null
                                       ? r.sharedMaterial.shader.name : "-"; } catch { }
                        try { lm = r.lightmapIndex.ToString(); } catch { }
                        try { drawing = (!r.forceRenderingOff && r.enabled) ? "DRAWING" : "hidden"; } catch { }

                        try
                        {
                            var kw = r.sharedMaterial != null ? r.sharedMaterial.shaderKeywords : null;
                            if (kw != null && kw.Length > 0)
                                keywords = "  kw=[" + string.Join(",", kw) + "]";
                        }
                        catch { }
                    }

                    string zf = (prev >= 0f && (h.distance - prev) < 0.03f)
                              ? "  <<< COPLANAR WITH THE ONE ABOVE (Z-FIGHTING)" : "";
                    prev = h.distance;
                    sb.Append("\n  ").Append(h.distance.ToString("F2")).Append(" m  [")
                      .Append(owner).Append("/").Append(scene).Append("]  '")
                      .Append(go.name).Append("'  layer=").Append(go.layer)
                      .Append("  ").Append(drawing).Append("  lm=").Append(lm)
                      .Append("  mat='").Append(mat).Append("'  shader='").Append(shader)
                      .Append("'").Append(keywords).Append(zf);
                }

                int hostDrawing = 0, hostTotal = 0;
                try
                {
                    var rs = UnityEngine.Object.FindObjectsOfType<Renderer>(true);
                    for (int i = 0; i < (rs != null ? rs.Length : 0); i++)
                    {
                        var r = rs[i];
                        if (r == null) continue;
                        var sc = r.gameObject.scene;
                        if (!sc.IsValid()) continue;
                        if (sc.name == SceneName) continue;
                        if (ShellSwap.KeepSceneHandle != 0
                            && sc.handle == ShellSwap.KeepSceneHandle) continue;
                        hostTotal++;
                        if (!r.forceRenderingOff && r.enabled) hostDrawing++;
                    }
                }
                catch { }
                sb.Append("\n  host renderers: ").Append(hostDrawing).Append(" STILL DRAWING of ")
                  .Append(hostTotal)
                  .Append(hostDrawing > 0 ? "  <<< host geometry is visible inside the world, and "
                                          + "the host's lights are off, so it renders BLACK"
                                          : "  (host fully hidden)");
                return sb.ToString();
            }
            catch (Exception e) { return "odg what failed: " + e.Message; }
        }

        static float _nextHostSweep;

        public static void HostSweepTick()
        {
            if (!Done || !MapIdentity.HostLevelIsMap) return;
            if (Time.realtimeSinceStartup < _nextHostSweep) return;
            _nextHostSweep = Time.realtimeSinceStartup + 5f;
            try
            {
                int hid = ShellSwap.SweepHostRenderers(
                    _root != null ? _root.transform : null, _log, false);
                if (hid > 0)
                    _log?.Warning("[shell] host sweep re-assert: " + hid + " host renderer(s) "
                                + "came back and were hidden again. Something re-activates "
                                + "host objects mid-mission; the tick holds the line.");
            }
            catch { }
        }

        internal static string StreamConsole(string arg)
        {
            try
            {
                const double MB = 1024.0 * 1024.0;
                arg = (arg ?? "").Trim().ToLowerInvariant();

                if (arg == "off" || arg == "on")
                {
                    bool on = arg == "on";
                    QualitySettings.streamingMipmapsActive = on;
                    return "texture streaming " + (on ? "ON" : "OFF")
                         + (on ? "" : " -- every texture now loads at full resolution. If the "
                                    + "black patches and the moving quality bands VANISH, "
                                    + "streaming starvation is confirmed. Expect a hitch and "
                                    + "higher VRAM while it loads.");
                }
                if (arg == "all")
                {
                    UnityEngine.Texture.streamingTextureForceLoadAll = true;
                    return "streamingTextureForceLoadAll = true: streaming stays on but every "
                         + "mip is held resident. Same visual outcome as 'off' with a gentler "
                         + "load pattern.";
                }
                if (arg.StartsWith("budget"))
                {
                    var rest = arg.Length > 6 ? arg.Substring(6).Trim() : "";
                    float mb;
                    if (float.TryParse(rest, out mb) && mb > 0f)
                    {
                        float was = QualitySettings.streamingMipmapsMemoryBudget;
                        QualitySettings.streamingMipmapsMemoryBudget = mb;
                        return "streaming budget " + was.ToString("F0") + " -> "
                             + mb.ToString("F0") + " MB";
                    }
                    return "odg stream budget <MB>   (now "
                         + QualitySettings.streamingMipmapsMemoryBudget.ToString("F0") + " MB)";
                }

                double desired = UnityEngine.Texture.desiredTextureMemory / MB;
                double target = UnityEngine.Texture.targetTextureMemory / MB;
                bool starved = QualitySettings.streamingMipmapsActive && desired > target;
                return "streaming active=" + QualitySettings.streamingMipmapsActive
                     + " budget=" + QualitySettings.streamingMipmapsMemoryBudget.ToString("F0") + " MB"
                     + " desired=" + desired.ToString("F0") + " MB"
                     + " target=" + target.ToString("F0") + " MB"
                     + " pending=" + UnityEngine.Texture.streamingTexturePendingLoadCount
                     + (starved ? "   *** STARVED by " + (desired - target).ToString("F0")
                                + " MB -- this is the suspected cause ***" : "   (not starved)")
                     + "  |  odg stream off | on | all | budget <MB>";
            }
            catch (Exception e) { return "odg stream failed: " + e.Message; }
        }

        internal static void RestoreTextureStreaming()
        {
            try
            {
                if (!_streamTouched) return;
                QualitySettings.streamingMipmapsMemoryBudget = _streamBudgetWas;
                QualitySettings.streamingMipmapsActive = _streamActiveWas;
                _log?.Msg("[stream] restored: streaming active=" + _streamActiveWas
                        + ", budget=" + _streamBudgetWas.ToString("F0")
                        + " MB. The game is back to how it shipped.");
                _streamTouched = false;
                _streamBudgetWas = -1f;
            }
            catch { }
        }

        static void AdditiveShadowSettings()
        {
            try
            {
                if (_addShadowGo != null) return;
                _log?.Msg("[map] shadows BEFORE ours on this additive map: " + ShadowSettingsNow());
                var go = new GameObject("SHADOWS_ADDITIVE");
                if (_root != null)
                {
                    try { go.transform.SetParent(_root.transform, false); }
                    catch { }
                }
                _addShadowGo = go;
                var v = go.AddComponent<UnityEngine.Rendering.Volume>();
                v.isGlobal = true;
                v.priority = OwnVolumePriority();
                v.weight = 1f;
                var prof = ScriptableObject.CreateInstance(
                    Il2CppInterop.Runtime.Il2CppType.Of<UnityEngine.Rendering.VolumeProfile>())
                    .Cast<UnityEngine.Rendering.VolumeProfile>();
                v.sharedProfile = prof;

                AddShadowSettings(prof, "ADDITIVE(" + MapIdentity.SceneName + ")", true);
            }
            catch (Exception e)
            {
                _log?.Warning("[map] AdditiveShadowSettings: " + e.Message);
            }
        }

        internal static float OwnVolumePriority()
        {
            if (_ownPriority > 0f) return _ownPriority;
            float max = 0f; string top = "none";
            try
            {
                var vols = UnityEngine.Object.FindObjectsOfType<UnityEngine.Rendering.Volume>(true);
                for (int i = 0; i < (vols != null ? vols.Length : 0); i++)
                {
                    var hv = vols[i];
                    if (hv == null) continue;

                    try { if (_lighting != null && hv.transform.IsChildOf(_lighting.transform)) continue; }
                    catch { }
                    if (hv.priority > max) { max = hv.priority; top = hv.gameObject.name; }
                }
            }
            catch (Exception e) { _log?.Warning("[map] volume priority scan: " + e.Message); }
            _ownPriority = Mathf.Max(1000f, max + 100f);
            _log?.Msg("[map] host's highest volume priority is " + max + " ('" + top
                    + "'); ours run at " + _ownPriority);
            return _ownPriority;
        }

        static void AddExposureVolume(GameObject rig, float ev, bool isNight)
        {
            try
            {
                var go = new GameObject("EXPOSURE_" + rig.name);
                go.transform.SetParent(rig.transform, false);
                var v = go.AddComponent<UnityEngine.Rendering.Volume>();
                v.isGlobal = true;

                v.priority = OwnVolumePriority();
                v.weight = 1f;
                var profile = ScriptableObject.CreateInstance(
                    Il2CppInterop.Runtime.Il2CppType.Of<UnityEngine.Rendering.VolumeProfile>())
                    .Cast<UnityEngine.Rendering.VolumeProfile>();
                v.sharedProfile = profile;

                var comp = profile.Add(
                    Il2CppInterop.Runtime.Il2CppType.Of<UnityEngine.Rendering.HighDefinition.Exposure>(),
                    true);
                var ex = comp.TryCast<UnityEngine.Rendering.HighDefinition.Exposure>();
                if (ex == null) { _log?.Warning("[map] exposure add failed on " + rig.name); return; }
                ex.active = true;
                ex.mode.overrideState = true;
                ex.mode.value = UnityEngine.Rendering.HighDefinition.ExposureMode.Fixed;
                ex.fixedExposure.overrideState = true;
                ex.fixedExposure.value = ev;

                if (!isNight && MapIdentity.DayAutoExposure)
                {
                    try
                    {
                        ex.mode.value = UnityEngine.Rendering.HighDefinition
                                        .ExposureMode.AutomaticHistogram;
                        ex.meteringMode.overrideState = true;
                        ex.meteringMode.value = UnityEngine.Rendering.HighDefinition
                                                .MeteringMode.CenterWeighted;
                        ex.limitMin.overrideState = true;
                        ex.limitMin.value = ev - 2.5f;
                        ex.limitMax.overrideState = true;
                        ex.limitMax.value = ev + 1.5f;
                        ex.adaptationSpeedDarkToLight.overrideState = true;
                        ex.adaptationSpeedDarkToLight.value = 3f;
                        ex.adaptationSpeedLightToDark.overrideState = true;
                        ex.adaptationSpeedLightToDark.value = 1f;

                        _log?.Msg("[map] day exposure ADAPTS (the game's own "
                                + "volumes do and ours did not): mode="
                                + ex.mode.value + " range EV "
                                + ex.limitMin.value.ToString("F1") + ".."
                                + ex.limitMax.value.ToString("F1")
                                + " around the computed " + ev.ToString("F2")
                                + ". A fixed EV metered for the lawn is the "
                                + "wrong exposure indoors. 'odg dayev <x>' "
                                + "still moves the centre.");
                    }
                    catch (Exception ae)
                    {
                        _log?.Warning("[map] auto exposure not available, "
                                    + "staying Fixed: " + ae.Message);
                        ex.mode.value = UnityEngine.Rendering.HighDefinition
                                        .ExposureMode.Fixed;
                    }
                }
                if (isNight) { _nightExposure = ex; AddNightFog(profile, go.name); }
                else _dayExposure = ex;
                AddShadowSettings(profile, rig.name);

                SkyRig.Add(profile, isNight, rig.name);
                if (!isNight)
                {

                    _dayProfile = profile;
                    if (_lightParams.ssgi) AddSsgi(profile, go.name);
                }

                try
                {
                    _log?.Msg("[map] " + go.name + ": mode=" + ex.mode.value
                            + " fixedEV=" + ex.fixedExposure.value
                            + " override=" + ex.fixedExposure.overrideState
                            + " (asked for Fixed " + ev + ")");
                }
                catch { _log?.Msg("[map] " + go.name + ": Fixed exposure EV100 " + ev); }
            }
            catch (Exception e) { _log?.Warning("[map] AddExposureVolume: " + e.Message); }
        }

        static UnityEngine.Rendering.HighDefinition.GlobalIllumination _ssgi;
        static UnityEngine.Rendering.VolumeProfile _dayProfile;

        static bool SsgiPrefOn
        {

            get { var p = PrefBool("CitySsgiV2", true,
                      "Add a ScreenSpaceGlobalIllumination override to the map's day volume. "
                    + "SSGI replaces probe indirect where the screen has data; the screenshot "
                    + "harness judges whether that helps here. 'odg ssgi on|off' moves it live.");
                  return p != null && p.Value; }
        }

        static void AddMicroShadows(UnityEngine.Rendering.VolumeProfile profile, string owner)
        {
            try
            {
                var added = profile.Add(Il2CppInterop.Runtime.Il2CppType
                    .Of<UnityEngine.Rendering.HighDefinition.MicroShadowing>(), false);
                var ms = added != null
                    ? added.TryCast<UnityEngine.Rendering.HighDefinition.MicroShadowing>() : null;
                if (ms == null) { _log?.Warning("[map] no MicroShadowing on this build"); return; }
                ms.active = true;
                Try(() => { ms.enable.overrideState = true; ms.enable.value = true; }, "micro.enable");
                Try(() => { ms.opacity.overrideState = true; ms.opacity.value = 0.6f; }, "micro.opacity");
                _log?.Msg("[map] micro-shadows on " + owner + " at 0.6: sunlit surfaces "
                        + "self-shade from their own AO maps instead of reading as prints.");
            }
            catch (Exception e) { _log?.Warning("[map] AddMicroShadows: " + e.Message); }
        }

        static void AddSsgi(UnityEngine.Rendering.VolumeProfile profile, string owner)
        {
            try
            {

                var added = profile.Add(Il2CppInterop.Runtime.Il2CppType
                    .Of<UnityEngine.Rendering.HighDefinition.GlobalIllumination>(), false);
                var gi = added != null
                    ? added.TryCast<UnityEngine.Rendering.HighDefinition.GlobalIllumination>() : null;
                if (gi == null) { _log?.Warning("[map] no GlobalIllumination on this build"); return; }
                gi.active = true;
                _ssgi = gi;
                Try(() => { gi.enable.overrideState = true; gi.enable.value = true; }, "ssgi.enable");
                Try(() => { gi.quality.overrideState = true; gi.quality.value = 3; }, "ssgi.quality(custom)");

                Try(() => { gi.fullResolutionSS.overrideState = true; gi.fullResolutionSS.value = false; }, "ssgi.fullResolutionSS");
                Try(() => { gi.maxRaySteps = 48; }, "ssgi.maxRaySteps");
                Try(() => { gi.denoise = true; }, "ssgi.denoise");
                _log?.Msg("[map] SSGI override on " + owner + ": enable=true, half-res, "
                        + "48 ray steps, denoised. The camera frame setting for SSGI ships "
                        + "default-ON, so the volume override is the whole switch.");
            }
            catch (Exception e) { _log?.Warning("[map] AddSsgi: " + e.Message); }
        }

        public static string SetSsgi(bool on)
        {
            try
            {
                var p = PrefBool("CitySsgi", false, null);
                if (p != null) { p.Value = on; MelonLoader.MelonPreferences.Save(); }
            }
            catch { }
            if (on)
            {
                if (_ssgi != null)
                {
                    try { _ssgi.active = true; _ssgi.enable.overrideState = true; _ssgi.enable.value = true; }
                    catch (Exception e) { return "could not re-enable it: " + e.Message; }
                    return "ssgi ON (saved), live on the standing day volume.";
                }
                if (_dayProfile != null)
                {
                    AddSsgi(_dayProfile, "DAY(live)");
                    return _ssgi != null ? "ssgi ON (saved), added to the standing day volume."
                                         : "ssgi saved ON but the override could not be added; see the log.";
                }
                return "ssgi ON (saved; no day volume standing, applies on the next build).";
            }
            if (_ssgi != null)
            {
                try { _ssgi.enable.overrideState = true; _ssgi.enable.value = false; _ssgi.active = false; }
                catch (Exception e) { return "could not disable it: " + e.Message; }
                return "ssgi OFF (saved), live.";
            }
            return "ssgi OFF (saved; it was not on).";
        }

        public static void RebuildLighting(LightingParams p)
        {
            if (!MapIdentity.OwnSky)
            {
                _log?.Msg("[map] RebuildLighting refused: this map keeps its tuned rig.");
                return;
            }
            if (_root == null && !_done)
            {
                _log?.Msg("[map] RebuildLighting refused: no map standing.");
                return;
            }
            Crumb("rebuild lighting (hostLit=" + p.hostLit + " apvOff=" + p.apvOff
                + " ssgi=" + p.ssgi + " sky=" + p.skyMode + " probes=" + p.probeUsage + ")");
            try
            {

                SkyRig.Forget();
                NightArt.Forget(); DayArt.Forget();
                FakeBounce.Forget();
                _nightExposure = null;
                _dayExposure = null;
                _grade = null;
                _wb = null;
                _cityExp = null;
                _ssgi = null;
                _dayProfile = null;
                NightFog = null;

                _hostLitOverride = p.hostLit;
                ApvGate.Demand = p.apvOff;

                if (_lighting != null)
                {
                    try { UnityEngine.Object.Destroy(_lighting); } catch { }
                    _lighting = null;
                }

                try { ShellSwap.SetHostLights(p.hostLit, _log); } catch { }

                AddLighting(p);

                if (p.hostLit)
                {

                    try { _log?.Msg("[map] " + ShellSwap.SoftenHostShadows(HostShadowDimmer, _log)); } catch { }
                    try { _log?.Msg("[map] " + ShellSwap.RetuneHostSun(SunElevation, SunAzimuth, SunKelvin, _log)); } catch { }
                }

                SceneLighting.Apply(false);
                ApplyProbeUsage(p.probeUsage);
            }
            catch (Exception e) { _log?.Warning("[map] RebuildLighting: " + e.Message); }
        }

        static int _probeUsageApplied = 1;
        public static void ApplyProbeUsage(int usage)
        {
            if (!MapIdentity.OwnSky || _root == null) return;
            if (usage == _probeUsageApplied) return;
            int n = 0;
            try
            {
                var want = usage == 0
                    ? UnityEngine.Rendering.LightProbeUsage.Off
                    : UnityEngine.Rendering.LightProbeUsage.BlendProbes;
                var rs = _root.GetComponentsInChildren<Renderer>(false);
                for (int i = 0; i < rs.Length; i++)
                {
                    var r = rs[i];
                    if (r == null) continue;
                    try { r.lightProbeUsage = want; n++; } catch { }
                }
                _probeUsageApplied = usage;
                _log?.Msg("[map] lightProbeUsage=" + want + " on " + n + " renderer(s)");
            }
            catch (Exception e) { _log?.Warning("[map] ApplyProbeUsage: " + e.Message); }
        }

        public static float Saturation = 16f;
        public static float Contrast = 8f;
        static UnityEngine.Rendering.HighDefinition.ColorAdjustments _grade;

        public static float CityAlbedo = 0.30f;
        static UnityEngine.Rendering.HighDefinition.Exposure _cityExp;

        static float AddCityExposure(UnityEngine.Rendering.VolumeProfile p)
        {
            float evOut = 14.7f;
            try
            {
                float lux = 0f;
                var sun = ShellSwap.HostSunLux();
                if (sun > 0f) lux = sun;
                if (lux <= 0f)
                {
                    _log?.Warning("[map] could not read the host sun's intensity; leaving its "
                                + "exposure alone and trusting the postExposure trim.");
                    return evOut;
                }

                float canopyH = MapIdentity.CanopyFactor;
                float albedoH = MapIdentity.GroundAlbedo > 0f
                              ? MapIdentity.GroundAlbedo : CityAlbedo;
                float e = lux * Mathf.Sin(SunElevation * Mathf.Deg2Rad) * canopyH
                        + lux * 0.20f;
                float lum = albedoH * e / Mathf.PI;
                float ev = Mathf.Log(8f * lum, 2f);
                ev = Mathf.Clamp(ev, 6f, 17f);

                var added = p.Add(Il2CppInterop.Runtime.Il2CppType
                    .Of<UnityEngine.Rendering.HighDefinition.Exposure>(), true);
                var x = added != null
                    ? added.TryCast<UnityEngine.Rendering.HighDefinition.Exposure>() : null;
                if (x == null) { _log?.Warning("[map] no Exposure component"); return evOut; }
                x.active = true; _cityExp = x;
                x.mode.overrideState = true;
                x.mode.value = UnityEngine.Rendering.HighDefinition.ExposureMode.Fixed;
                x.fixedExposure.overrideState = true;
                x.fixedExposure.value = ev;
                evOut = ev;
                _log?.Msg("[map] city exposure COMPUTED, not borrowed: the host sun measures "
                        + lux.ToString("F0") + " lux, so ground at " + albedoH.ToString("F2")
                        + " albedo under it sits at " + lum.ToString("F0")
                        + " cd/m2, and EV " + ev.ToString("F2") + " puts that at mid-grey. "
                        + "The host's own exposure is tuned for sand and clips a pale city to "
                        + "white, which is why the colour dials could not fix it.");
            }
            catch (Exception e) { _log?.Warning("[map] AddCityExposure: " + e.Message); }
            return evOut;
        }

        public static string SetCityAlbedo(float a)
        {
            CityAlbedo = Mathf.Clamp(a, 0.05f, 0.9f);
            return "city albedo = " + CityAlbedo.ToString("F2")
                 + " (takes effect on the next map build; lower = brighter frame)";
        }

        static void AddGrade(UnityEngine.Rendering.VolumeProfile p)
        {
            try
            {
                var added = p.Add(Il2CppInterop.Runtime.Il2CppType
                    .Of<UnityEngine.Rendering.HighDefinition.ColorAdjustments>(), true);
                var c = added != null
                    ? added.TryCast<UnityEngine.Rendering.HighDefinition.ColorAdjustments>() : null;
                if (c == null) { _log?.Warning("[map] no ColorAdjustments on this build"); return; }
                c.active = true;
                _grade = c;
                Try(() => { c.saturation.overrideState = true; c.saturation.value = Saturation; }, "saturation");
                Try(() => { c.contrast.overrideState = true; c.contrast.value = Contrast; }, "contrast");

                Try(() => { c.colorFilter.overrideState = true;
                            c.colorFilter.value = Color.white; },
                    "colorFilter");

                Try(() => { c.postExposure.overrideState = true;
                            c.postExposure.value = PostExposure; }, "postExposure");
                AddWhiteBalance(p);
                _log?.Msg("[map] colour grade on our volume only: saturation +" + Saturation
                        + ", contrast +" + Contrast + ". The host's grade is tuned for dust; a "
                        + "city of glass and paint under it reads muted. 'odg look <sat> [contrast]' "
                        + "moves it live.");
            }
            catch (Exception e) { _log?.Warning("[map] AddGrade: " + e.Message); }
        }

        public static float PostExposure = 0f;

        public static float CoolTemp = -12f;
        static UnityEngine.Rendering.HighDefinition.WhiteBalance _wb;

        static void AddWhiteBalance(UnityEngine.Rendering.VolumeProfile p)
        {
            try
            {
                var added = p.Add(Il2CppInterop.Runtime.Il2CppType
                    .Of<UnityEngine.Rendering.HighDefinition.WhiteBalance>(), true);
                var w = added != null
                    ? added.TryCast<UnityEngine.Rendering.HighDefinition.WhiteBalance>() : null;
                if (w == null) { _log?.Warning("[map] no WhiteBalance on this build"); return; }
                w.active = true; _wb = w;
                Try(() => { w.temperature.overrideState = true; w.temperature.value = CoolTemp; },
                    "whitebalance.temperature");
                _log?.Msg("[map] white balance " + CoolTemp + " (negative is cooler). This is what "
                        + "takes the desert amber off the city; 'odg look cool <t>' moves it live, "
                        + "0 leaves the host's own balance alone.");
            }
            catch (Exception e) { _log?.Warning("[map] AddWhiteBalance: " + e.Message); }
        }

        public static string SetExposure(float ev)
        {
            PostExposure = Mathf.Clamp(ev, -5f, 5f);
            if (_grade == null)
                return "exposure trim = " + PostExposure.ToString("F1") + " EV (stored; next build)";
            try { _grade.postExposure.overrideState = true; _grade.postExposure.value = PostExposure; }
            catch (Exception e) { return "could not set it: " + e.Message; }
            return "exposure trim = " + PostExposure.ToString("F1") + " EV, live. Negative is "
                 + "darker. This rides ON TOP of the host's exposure rather than replacing it.";
        }

        public static string SetCool(float t)
        {
            CoolTemp = Mathf.Clamp(t, -100f, 100f);
            if (_wb == null)
                return "cool = " + CoolTemp.ToString("F0") + " (stored; next build)";
            try { _wb.temperature.overrideState = true; _wb.temperature.value = CoolTemp; }
            catch (Exception e) { return "could not set it: " + e.Message; }
            return "white balance = " + CoolTemp.ToString("F0")
                 + ", live. More negative is cooler; 0 is the host's own balance.";
        }

        public static string SetLook(float sat, float con)
        {
            Saturation = Mathf.Clamp(sat, -100f, 100f);
            Contrast = Mathf.Clamp(con, -100f, 100f);
            if (_grade == null)
                return "look = sat " + Saturation.ToString("F0") + ", contrast "
                     + Contrast.ToString("F0") + " (stored; takes effect on the next build)";
            try
            {
                _grade.saturation.overrideState = true; _grade.saturation.value = Saturation;
                _grade.contrast.overrideState = true; _grade.contrast.value = Contrast;
            }
            catch (Exception e) { return "could not set it: " + e.Message; }
            return "look = saturation +" + Saturation.ToString("F0") + ", contrast +"
                 + Contrast.ToString("F0") + ", live.";
        }

        static void Try(Action a, string what)
        {
            try { a(); }
            catch (Exception e) { _log?.Warning("[map] shadow " + what + ": " + e.Message); }
        }

        static void AddShadowSettings(UnityEngine.Rendering.VolumeProfile p, string owner)
        {
            AddShadowSettings(p, owner, MapIdentity.OwnSky);
        }

        static void AddShadowSettings(UnityEngine.Rendering.VolumeProfile p, string owner,
                                      bool detailed)
        {
            try
            {
                var added = p.Add(
                    Il2CppInterop.Runtime.Il2CppType.Of<UnityEngine.Rendering.HighDefinition.HDShadowSettings>(),
                    false);
                var sh = added != null
                    ? added.TryCast<UnityEngine.Rendering.HighDefinition.HDShadowSettings>() : null;
                if (sh == null) { _log?.Warning("[map] no HDShadowSettings on " + owner); return; }
                sh.active = true;

                bool city = detailed;
                sh.maxShadowDistance.overrideState = true;

                float shadowM = MapIdentity.ShadowDistanceM;
                sh.maxShadowDistance.value = shadowM > 0f ? shadowM
                                           : (city ? 130f : 90f);
                sh.cascadeShadowSplitCount.overrideState = true;
                sh.cascadeShadowSplitCount.value = city ? 4 : 2;
                if (city)
                {

                    Try(() => { sh.cascadeShadowSplit0.overrideState = true;
                                sh.cascadeShadowSplit0.value = 0.046f; }, "split0");
                    Try(() => { sh.cascadeShadowSplit1.overrideState = true;
                                sh.cascadeShadowSplit1.value = 0.155f; }, "split1");
                    Try(() => { sh.cascadeShadowSplit2.overrideState = true;
                                sh.cascadeShadowSplit2.value = 0.40f; }, "split2");

                    Try(() => { sh.cascadeShadowBorder0.overrideState = true;
                                sh.cascadeShadowBorder0.value = 0.15f; }, "border0");
                    Try(() => { sh.cascadeShadowBorder1.overrideState = true;
                                sh.cascadeShadowBorder1.value = 0.15f; }, "border1");
                    Try(() => { sh.cascadeShadowBorder2.overrideState = true;
                                sh.cascadeShadowBorder2.value = 0.15f; }, "border2");
                    Try(() => { sh.cascadeShadowBorder3.overrideState = true;
                                sh.cascadeShadowBorder3.value = 0.20f; }, "border3");
                }

                string bordersNow = "-";
                try
                {
                    bordersNow = sh.cascadeShadowBorder0.value.ToString("F2") + "/"
                               + sh.cascadeShadowBorder1.value.ToString("F2") + "/"
                               + sh.cascadeShadowBorder2.value.ToString("F2") + "/"
                               + sh.cascadeShadowBorder3.value.ToString("F2")
                               + " override=" + sh.cascadeShadowBorder0.overrideState;
                }
                catch { }
                string splitsNow = "-";
                try
                {
                    splitsNow = sh.cascadeShadowSplit0.value.ToString("F3") + "/"
                              + sh.cascadeShadowSplit1.value.ToString("F3") + "/"
                              + sh.cascadeShadowSplit2.value.ToString("F3");
                }
                catch { }
                _log?.Msg("[map] shadows on " + owner + ": distance "
                        + sh.maxShadowDistance.value + " m, "
                        + sh.cascadeShadowSplitCount.value + " cascade(s)"
                        + (city ? ", splits " + splitsNow + ", borders " + bordersNow
                                : ". Each cascade is a full redraw of everything inside "
                                + "it, so this is geometry cost, not shadow quality."));
            }
            catch (Exception e) { _log?.Warning("[map] AddShadowSettings: " + e.Message); }
        }

        public static UnityEngine.Rendering.HighDefinition.Fog NightFog;

        public static float FogMeanFreePath = 120f;

        public static string SetRays(bool on)
        {
            if (NightFog == null) return "no fog volume yet; launch the map first";
            try
            {
                NightFog.enableVolumetricFog.overrideState = true;
                NightFog.enableVolumetricFog.value = on;
                NightFog.depthExtent.overrideState = true;
                NightFog.depthExtent.value = on ? 55f : 160f;
            }
            catch (Exception e) { return "fog write failed: " + e.Message; }
            int n = 0;
            try
            {
                foreach (var hd in UnityEngine.Object.FindObjectsOfType<
                             UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>())
                {
                    if (hd == null) continue;
                    var li = hd.GetComponent<Light>();
                    if (li == null || li.type != LightType.Directional) continue;
                    try { hd.affectsVolumetric = on; n++; } catch { }
                }
            }
            catch { }
            return "sun shafts " + (on ? "ON" : "OFF") + " on " + n
                 + " directional light(s), fog depth "
                 + (on ? "55" : "160") + " m. Off is the cheaper frame.";
        }

        const float FogAnisotropy = 0.75f;

        static void AddSeaFog(UnityEngine.Rendering.VolumeProfile profile, string owner)
        {
            try
            {
                var added = profile.Add(
                    Il2CppInterop.Runtime.Il2CppType.Of<UnityEngine.Rendering.HighDefinition.Fog>(),
                    false);
                var fog = added != null
                    ? added.TryCast<UnityEngine.Rendering.HighDefinition.Fog>() : null;
                if (fog == null) { _log?.Warning("[map] sea fog add failed on " + owner); return; }
                fog.active = true;
                float sea = MapIdentity.SeaLevelY;
                Try(() => { fog.enabled.overrideState = true; fog.enabled.value = true; }, "seafog.enabled");
                Try(() => { fog.colorMode.overrideState = true;
                            fog.colorMode.value = UnityEngine.Rendering.HighDefinition.FogColorMode.SkyColor; },
                    "seafog.colorMode");
                Try(() => { fog.tint.overrideState = true; fog.tint.value = Color.white; }, "seafog.tint");
                Try(() => { fog.meanFreePath.overrideState = true; fog.meanFreePath.value = 1400f; },
                    "seafog.mfp");
                Try(() => { fog.baseHeight.overrideState = true; fog.baseHeight.value = sea; },
                    "seafog.base");
                Try(() => { fog.maximumHeight.overrideState = true; fog.maximumHeight.value = sea + 300f; },
                    "seafog.max");
                Try(() => { fog.maxFogDistance.overrideState = true; fog.maxFogDistance.value = 6000f; },
                    "seafog.maxDist");
                Try(() => { fog.enableVolumetricFog.overrideState = true;
                            fog.enableVolumetricFog.value = false; }, "seafog.volumetric");
                _log?.Msg("[map] sea fog on " + owner + ": colour from the SKY (not the host's "
                        + "authored constant), mean free path 1400 m from y=" + sea.ToString("F1")
                        + ", no volumetrics.");
            }
            catch (Exception e) { _log?.Warning("[map] AddSeaFog: " + e.Message); }
        }

        static void AddNightFog(UnityEngine.Rendering.VolumeProfile profile, string owner)
        {
            try
            {

                var added = profile.Add(
                    Il2CppInterop.Runtime.Il2CppType.Of<UnityEngine.Rendering.HighDefinition.Fog>(),
                    false);
                var fog = added.TryCast<UnityEngine.Rendering.HighDefinition.Fog>();
                if (fog == null) { _log?.Warning("[map] fog add failed on " + owner); return; }

                fog.active = true;
                fog.enabled.overrideState = true;
                fog.enabled.value = true;

                fog.enableVolumetricFog.overrideState = true;

                fog.enableVolumetricFog.value = MapIdentity.VolumetricRays;
                if (MapIdentity.VolumetricRays)
                    _log?.Msg("[map] volumetric fog ON for this map: the sun scatters in it, "
                            + "so light falls through the canopy as shafts. If the sky bands "
                            + "into radial spokes this is the cause and it is one flag.");

                fog.meanFreePath.overrideState = true;
                fog.meanFreePath.value = FogMeanFreePath;

                fog.baseHeight.overrideState = true;
                fog.baseHeight.value = 0f;
                fog.maximumHeight.overrideState = true;
                fog.maximumHeight.value = 45f;

                fog.anisotropy.overrideState = true;
                fog.anisotropy.value = FogAnisotropy;

                fog.depthExtent.overrideState = true;

                fog.depthExtent.value = MapIdentity.VolumetricRays ? 55f : 160f;

                NightFog = fog;

                AnchorFogToGround();

                bool volOn = true;
                try { volOn = fog.enableVolumetricFog.value; } catch { }
                bool volOver = false;
                try { volOver = fog.enableVolumetricFog.overrideState; } catch { }
                _log?.Msg("[map] night fog: volumetric=" + volOn
                        + " (override=" + volOver + ")"
                        + " meanFreePath " + fog.meanFreePath.value + " m"
                        + ", anisotropy " + fog.anisotropy.value
                        + ", depth " + fog.depthExtent.value + " m."
                        + "  volumetric=False means NO light scatters through the fog, so"
                        + " no shafts and no froxel spokes can come from THIS component."
                        + " anisotropy and depth are inert while it reads False.");
            }
            catch (Exception e) { _log?.Warning("[map] AddNightFog: " + e.Message); }
        }

        public static void AnchorFogToGround()
        {
            if (MapIdentity.HostLevelIsMap) return;
            if (NightFog == null) return;
            try
            {

                float ground;
                var mt = MarkersRoot;
                var ys = new List<float>(200);
                if (mt != null)
                    for (int g2 = 0; g2 < mt.childCount; g2++)
                    {
                        var grp = mt.GetChild(g2);
                        for (int i = 0; i < grp.childCount; i++)
                            ys.Add(grp.GetChild(i).position.y);
                    }
                if (ys.Count >= 5)
                {
                    ys.Sort();
                    ground = ys[ys.Count / 2] - 2f;
                }
                else
                {
                    var b = Bounds();
                    if (b.size.y < 1f) { _log?.Msg("[map] fog left at default height, map bounds not ready"); return; }
                    ground = b.min.y;
                }
                DayArt.Anchor(ground);
                NightFog.baseHeight.value = ground;
                NightFog.maximumHeight.value = ground + 45f;
                _log?.Msg("[map] fog anchored to the ground at y=" + ground.ToString("F1")
                        + ", thinning out by y=" + (ground + 45f).ToString("F1")
                        + (ys.Count >= 5 ? "  (from the marker median, not the bounds)" : ""));
            }
            catch (Exception e) { _log?.Warning("[map] AnchorFogToGround: " + e.Message); }
        }

        public static void ScaleFog(float factor)
        {
            if (NightFog == null) { _log?.Msg("[tune] no night fog yet"); return; }
            try
            {

                FogMeanFreePath = Mathf.Clamp(FogMeanFreePath / factor, 15f, 2000f);
                TuneStore.Save("FogMeanFreePath", FogMeanFreePath);
                NightFog.meanFreePath.value = FogMeanFreePath;
                _log?.Msg("[tune] fog meanFreePath " + FogMeanFreePath.ToString("F0")
                        + " m (lower is thicker)");
            }
            catch (Exception e) { _log?.Warning("[tune] fog: " + e.Message); }
        }

        public static int FoliageLayer = -1;
        public static int PropLayer = -1;

        public static int TreeLayer = -1;
        public static float TreeCullDistance = 400f;

        public static float PropCullDistance = 110f;

        static void FindFoliageLayer()
        {
            if (FoliageLayer >= 0) return;
            try
            {

                for (int i = 31; i >= 8; i--)
                {
                    string n = null;
                    try { n = LayerMask.LayerToName(i); } catch { }
                    if (string.IsNullOrEmpty(n))
                    {
                        if (FoliageLayer < 0)
                        {
                            FoliageLayer = i;
                            _log?.Msg("[map] foliage will use layer " + i
                                    + " (unnamed, so nothing else in the game is using it)");
                            continue;
                        }
                        if (PropLayer < 0)
                        {
                            PropLayer = i;
                            _log?.Msg("[map] small props will use layer " + i);
                            continue;
                        }

                        TreeLayer = i;
                        _log?.Msg("[map] trees will use layer " + i
                                + " (drawn to " + TreeCullDistance.ToString("F0")
                                + " m; grass and brush keep the near cull)");
                        return;
                    }
                }

                if (FoliageLayer < 0)
                    _log?.Warning("[map] no free layer for foliage culling; every layer 8-31 "
                                + "is named. Distance culling is OFF and the frame rate will "
                                + "stay geometry-bound.");
                else if (TreeLayer < 0)
                    _log?.Warning("[map] only " + (PropLayer >= 0 ? "two free layers" : "one free layer")
                                + " (8-31 are otherwise all named), so there is no separate tree "
                                + "tier. Trees share the foliage layer, and ApplyFoliageCulling "
                                + "therefore draws that layer to the TREE distance so the "
                                + "silhouette survives. Grass draws that far too; Shift+[ ] "
                                + "tunes it.");
            }
            catch (Exception e) { _log?.Warning("[map] FindFoliageLayer: " + e.Message); }
        }

        public static float FoliageCullDistance = 70f;

        public static float EffectiveFoliageCull
        {
            get
            {
                return TreeLayer < 0
                    ? Mathf.Max(FoliageCullDistance, TreeCullDistance)
                    : FoliageCullDistance;
            }
        }

        static bool _cullingApplied;

        sealed class TouchedCam
        {
            public Camera Cam;
            public float[] Dist;
            public int AddedBits;
            public bool Spherical;
            public float Far;
            public bool FarRaised;
        }
        static readonly System.Collections.Generic.List<TouchedCam> _cullTouched
            = new System.Collections.Generic.List<TouchedCam>();

        public static void RestoreFoliageCulling()
        {
            int restored = 0;
            foreach (var t in _cullTouched)
            {
                try
                {
                    if (t.Cam == null) continue;
                    var d = new Il2CppInterop.Runtime.InteropTypes.Arrays
                                .Il2CppStructArray<float>(32L);
                    for (int i = 0; i < 32; i++) d[i] = t.Dist[i];
                    t.Cam.layerCullDistances = d;
                    t.Cam.layerCullSpherical = t.Spherical;
                    if (t.AddedBits != 0)
                        t.Cam.cullingMask = t.Cam.cullingMask & ~t.AddedBits;
                    if (t.FarRaised && t.Far > 0f) { try { t.Cam.farClipPlane = t.Far; } catch { } }
                    restored++;
                }
                catch { }
            }
            if (restored > 0)
                _log?.Msg("[map] foliage culling RESTORED on " + restored
                        + " camera(s): our map is down, their layers, masks and "
                        + "cull distances are theirs again.");
            _cullTouched.Clear();
            _cullingApplied = false;
        }

        public static void ApplyFoliageCulling()
        {

            if (FoliageLayer < 0) return;

            if (_root == null) return;

            var cams = AllWorldCameras();

            try
            {
                var sb = new System.Text.StringBuilder();
                for (int ci = 0; ci < cams.Count; ci++)
                {
                    var c = cams[ci];
                    if (c == null) continue;
                    sb.Append(ci == 0 ? "" : ", ").Append("[").Append(c.name).Append("]")
                      .Append(c.isActiveAndEnabled ? "" : "(off)")
                      .Append(" 0x").Append(c.cullingMask.ToString("X8"));
                }
                _log?.Msg("[map] world camera(s) drawing the map: " + cams.Count
                        + " -- " + sb.ToString() + ". Any of these that lacks our "
                        + "foliage bits renders the map as bare terrain, which is "
                        + "what aiming down sights looked like.");
            }
            catch { }
            for (int ci = 0; ci < cams.Count; ci++)
                ApplyFoliageCullingTo(cams[ci]);
        }

        static System.Collections.Generic.List<Camera> AllWorldCameras()
        {
            var outp = new System.Collections.Generic.List<Camera>();
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<Camera>(true);
                for (int i = 0; i < (all != null ? all.Length : 0); i++)
                {
                    var c = all[i];
                    if (c == null) continue;
                    try
                    {
                        if ((c.cullingMask & 1) == 0) continue;
                        outp.Add(c);
                    }
                    catch { }
                }
            }
            catch { }
            if (outp.Count == 0)
            {
                try { if (Camera.main != null) outp.Add(Camera.main); } catch { }
            }
            return outp;
        }

        static void ApplyFoliageCullingTo(Camera cam)
        {
            try
            {
                if (cam == null) return;

                bool known = false;
                foreach (var t in _cullTouched)
                    if (ReferenceEquals(t.Cam, cam)) { known = true; break; }
                if (!known)
                {
                    var snap = new TouchedCam { Cam = cam, Dist = new float[32] };
                    try
                    {
                        var cd = cam.layerCullDistances;
                        for (int i = 0; i < 32 && i < cd.Length; i++) snap.Dist[i] = cd[i];
                    }
                    catch { }
                    try { snap.Spherical = cam.layerCullSpherical; } catch { }
                    try { snap.Far = cam.farClipPlane; } catch { }
                    _cullTouched.Add(snap);
                }

                var d = new Il2CppInterop.Runtime.InteropTypes.Arrays
                            .Il2CppStructArray<float>(32L);
                for (int i = 0; i < 32; i++) d[i] = 0f;

                d[FoliageLayer] = EffectiveFoliageCull;
                if (PropLayer >= 0) d[PropLayer] = PropCullDistance;
                if (TreeLayer >= 0) d[TreeLayer] = TreeCullDistance;
                cam.layerCullDistances = d;

                cam.layerCullSpherical = true;

                float wantFar = MapIdentity.OceanWater ? 12000f : 0f;
                if (wantFar > 0f)
                {
                    try
                    {
                        float was = cam.farClipPlane;
                        if (was < wantFar)
                        {
                            cam.farClipPlane = wantFar;
                            foreach (var t in _cullTouched)
                                if (ReferenceEquals(t.Cam, cam)) { t.FarRaised = true; break; }
                            _log?.Msg("[map] far plane " + was.ToString("F0") + " -> "
                                    + wantFar.ToString("F0") + " m on camera '" + cam.name
                                    + "' so the sea reaches the horizon");
                        }
                    }
                    catch (Exception fe) { _log?.Warning("[map] far plane: " + fe.Message); }
                }

                int want = (1 << FoliageLayer)
                         | (PropLayer >= 0 ? (1 << PropLayer) : 0)
                         | (TreeLayer >= 0 ? (1 << TreeLayer) : 0);
                int mask = cam.cullingMask;
                if ((mask & want) != want)
                {
                    foreach (var t in _cullTouched)
                        if (ReferenceEquals(t.Cam, cam)) { t.AddedBits |= want & ~mask; break; }
                    cam.cullingMask = mask | want;
                    _log?.Warning("[map] the camera was NOT DRAWING layer(s) "
                        + Convert.ToString(want & ~mask, 2)
                        + " -- our foliage layer is unnamed but was excluded from "
                        + "'" + cam.name + "' cullingMask (was 0x" + mask.ToString("X8")
                        + ", now 0x" + cam.cullingMask.ToString("X8") + "). Every plant "
                        + "was built, grounded and shadow-configured, and none of it "
                        + "could ever appear. Unnamed does not mean rendered.");
                }
                else
                {
                    _log?.Msg("[map] camera '" + cam.name + "' draws the foliage layer(s)"
                            + " (cullingMask 0x" + mask.ToString("X8") + ") -- so if "
                            + "plants are missing it is not the layer.");
                }
                _cullingApplied = true;
                _log?.Msg("[map] culling: foliage beyond " + FoliageCullDistance
                        + " m, small props beyond " + PropCullDistance
                        + " m. Buildings, walls and terrain still draw to the far "
                        + "plane, so the map does not visibly shrink.");
            }
            catch (Exception e) { _log?.Warning("[map] ApplyFoliageCulling: " + e.Message); }
        }

        static float _nextCullTry;
        public static void CullingTick()
        {
            if (FoliageLayer < 0) return;

            if (_root == null)
            {
                if (_cullTouched.Count > 0) RestoreFoliageCulling();

                try { HostVeg.RestoreIfArmed(_log); } catch { }
                return;
            }

            int wantNow = (1 << FoliageLayer)
                        | (PropLayer >= 0 ? (1 << PropLayer) : 0)
                        | (TreeLayer >= 0 ? (1 << TreeLayer) : 0);
            try
            {
                var live = AllWorldCameras();
                for (int i = 0; i < live.Count; i++)
                {
                    var c = live[i];
                    if (c == null) continue;
                    bool lost = false;
                    try { lost = (c.cullingMask & wantNow) != wantNow; } catch { }
                    if (lost) ApplyFoliageCullingTo(c);
                }
            }
            catch { }

            if (Time.realtimeSinceStartup < _nextCullTry) return;
            _nextCullTry = Time.realtimeSinceStartup + (_cullingApplied ? 5f : 1f);
            try
            {
                var cam = Camera.main;
                if (cam == null) return;
                float cur = 0f;
                try { cur = cam.layerCullDistances[FoliageLayer]; } catch { }

                int want = (1 << FoliageLayer)
                         | (PropLayer >= 0 ? (1 << PropLayer) : 0)
                         | (TreeLayer >= 0 ? (1 << TreeLayer) : 0);
                bool maskLost = false;
                try { maskLost = (cam.cullingMask & want) != want; } catch { }
                if (maskLost || Mathf.Abs(cur - EffectiveFoliageCull) > 0.5f)
                    ApplyFoliageCulling();
            }
            catch { }
        }

        public static void ScaleFoliageCull(float factor)
        {
            FoliageCullDistance = Mathf.Clamp(FoliageCullDistance * factor, 20f, 600f);

            if (TreeLayer < 0)
                TreeCullDistance = Mathf.Clamp(TreeCullDistance * factor, 120f, 600f);

            _cullingApplied = false;
            ApplyFoliageCulling();
        }

        public static void AdjustNightEV(float delta)
        {
            if (_nightExposure == null) { _log?.Msg("[tune] no night exposure yet"); return; }
            NightEV += delta;
            TuneStore.Save("NightEV", NightEV);
            try { _nightExposure.fixedExposure.value = NightEV; } catch { }
            _log?.Msg("[tune] night exposure EV100 = " + NightEV.ToString("F1"));
        }

        public static void ScaleMoonLux(float factor)
        {
            if (_moonHd == null) { _log?.Msg("[tune] no moon yet"); return; }
            MoonLux = Mathf.Clamp(MoonLux * factor, 0.2f, 200f);
            TuneStore.Save("MoonLux", MoonLux);
            try { _moonHd.SetIntensity(MoonLux, UnityEngine.Rendering.LightUnit.Lux); } catch { }
            try { _fillHd?.SetIntensity(MoonLux / 14f, UnityEngine.Rendering.LightUnit.Lux); } catch { }
            _log?.Msg("[tune] moon " + MoonLux.ToString("F1") + " lux, fill "
                    + (MoonLux / 14f).ToString("F2") + " lux");
        }

        public static bool OurLightsOn = true;

        public static void ToggleOurLights()
        {
            OurLightsOn = !OurLightsOn;
            int n = 0;
            try
            {
                if (_moonGo != null) { _moonGo.SetActive(OurLightsOn); n++; }
            }
            catch { }
            try
            {
                if (_lighting != null)
                {

                    var lights = _lighting.GetComponentsInChildren<Light>(true);
                    for (int i = 0; i < lights.Length; i++)
                    {
                        var l = lights[i];
                        if (l == null) continue;
                        try { l.enabled = OurLightsOn; n++; } catch { }
                    }
                }
            }
            catch (Exception e) { _log?.Warning("[tune] our lights: " + e.Message); }

            try { FakeBounce.Enabled = OurLightsOn; } catch { }

            _log?.Msg("[tune] EVERY light this mod owns is now "
                    + (OurLightsOn ? "ON" : "OFF") + " (" + n + " light(s)). "
                    + "If the spokes are still on screen with these OFF, they are not "
                    + "ours -- not the moon, not the fill, not the bounce probes -- and "
                    + "the search moves to the game's own sky and camera passes.");
        }

        public static void StepMoonElevation(float degrees)
        {
            if (_moonGo == null) { _log?.Msg("[tune] no moon yet"); return; }
            var e = _moonGo.transform.rotation.eulerAngles;
            float nx = Mathf.Repeat(e.x + degrees - 8f, 46f) + 8f;
            _moonGo.transform.rotation = Quaternion.Euler(nx, e.y, e.z);

            FakeBounce.SetMoonDirection(_moonGo.transform.forward);
            _log?.Msg("[tune] moon elevation = " + nx.ToString("F0") + " degrees");
        }

        public enum SkyRole { None, Sun, Moon }

        static void MakeSun(Transform parent, string name, float lux, float kelvin,
                            Vector3 euler, bool shadows, SkyRole role = SkyRole.None)
        {
            try
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);

                go.SetActive(false);
                go.transform.rotation = Quaternion.Euler(euler);

                var l = go.AddComponent<Light>();
                l.type = LightType.Directional;
                l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
                l.color = Color.white;
                l.colorTemperature = kelvin;
                l.useColorTemperature = true;
                l.cullingMask = ~0;

                var hd = go.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>();
                if (hd != null)
                {
                    try { hd.SetIntensity(lux, UnityEngine.Rendering.LightUnit.Lux); } catch { }
                    try { hd.EnableColorTemperature(true); } catch { }
                    try { hd.SetColor(Color.white, kelvin); } catch { }
                    try { hd.EnableShadows(shadows); } catch { }

                    if (role == SkyRole.None)
                    { try { hd.affectsVolumetric = false; } catch { } }
                    try { hd.shadowDimmer = shadows ? 1f : 0f; } catch { }

                    try { hd.interactsWithSky = role != SkyRole.None; } catch (Exception e)
                    { _log?.Warning("[map] " + name + " interactsWithSky: " + e.Message); }

                    try { hd.angularDiameter = 0.5f; } catch { }
                    try { hd.distance = role == SkyRole.Sun ? 150000000f : 384400000f; } catch { }

                    if (role == SkyRole.Sun) SkyRig.RegisterSun(hd);

                    if (role == SkyRole.Moon)
                    {
                        _moonHd = hd; _moonGo = go;

                        try { hd.shadowDimmer = 0.88f; } catch { }

                        try { hd.shadowTint = new Color(0.16f, 0.18f, 0.24f); } catch { }

                        try { hd.penumbraTint = false; } catch { }

                        try { hd.affectsVolumetric = true; } catch { }

                        try { hd.volumetricDimmer = 3f; } catch { }

                        try { hd.volumetricShadowDimmer = 0f; } catch { }

                        try
                        {
                            hd.useContactShadow.useOverride = true;
                            hd.useContactShadow.@override = true;
                            _log?.Msg("[map] moon contact shadows: opted in "
                                    + "(useOverride=" + hd.useContactShadow.useOverride + ")");
                        }
                        catch (Exception ce)
                        { _log?.Warning("[map] moon contact shadows: " + ce.Message); }

                        try
                        {
                            _log?.Msg("[map] moon volumetrics: affects=" + hd.affectsVolumetric
                                    + " dimmer=" + hd.volumetricDimmer.ToString("F2")
                                    + " shadowDimmer=" + hd.volumetricShadowDimmer.ToString("F2")
                                    + "  (affects=True lights the fog; shadowDimmer=0 is what "
                                    + "stops it drawing rays)");
                        }
                        catch { }
                        try
                        {
                            hd.celestialBodyShadingSource = UnityEngine.Rendering.HighDefinition
                                .HDAdditionalLightData.CelestialBodyShadingSource.Manual;
                            hd.sunColor = new Color(1f, 0.96f, 0.90f);
                            hd.sunIntensity = 130000f;
                            hd.moonPhase = 0f;
                            hd.moonPhaseRotation = 0f;
                            hd.earthshine = 0.10f;
                            hd.surfaceTint = Color.white;

                            hd.flareMultiplier = 0f;
                            hd.flareSize = 2f;
                        }
                        catch (Exception e)
                        { _log?.Warning("[map] " + name + " moon shading: " + e.Message); }
                    }
                }
                if (name == "SUN_NightFill") _fillHd = hd;
                if (name != null && name.StartsWith("SUN_DayFill", StringComparison.Ordinal))
                    SkyRig.RegisterFill(hd);
                go.SetActive(true);

                try
                {
                    _log?.Msg("[map] " + name + ": hdIntensity=" + hd.intensity
                            + " unit=" + hd.lightUnit
                            + " K=" + l.colorTemperature
                            + " shadows=" + l.shadows
                            + " dimmer=" + hd.shadowDimmer.ToString("F2")
                            + " sky=" + hd.interactsWithSky
                            + " shading=" + hd.celestialBodyShadingSource);
                }
                catch { }
            }
            catch (Exception e) { _log?.Warning("[map] MakeSun " + name + ": " + e.Message); }
        }

        static GameObject _markers;

        public static bool MapFloorFarFromHost;

        public static int PlaceMarkers()
        {
            string path = Path.Combine(BundleDir, "gameplay.bin");
            if (!File.Exists(path)) { _log?.Warning("[map] no gameplay.bin"); return 0; }
            if (_markers != null) return 0;

            var markerYs = new List<float>();
            int n = 0;
            try
            {
                _markers = new GameObject("MARKERS");

                if (_scene.IsValid()) SceneManager.MoveGameObjectToScene(_markers, _scene);
                else if (_root != null) _markers.transform.SetParent(_root.transform, true);

                using (var fs = File.OpenRead(path))
                using (var r = new System.IO.BinaryReader(fs))
                {
                    if (new string(r.ReadChars(4)) != "MGP1")
                    { _log?.Warning("[map] gameplay.bin: bad magic"); return 0; }
                    int groups = r.ReadInt32();
                    for (int g = 0; g < groups; g++)
                    {
                        string cls = ReadStr(r);
                        int count = r.ReadInt32();
                        var groupGo = new GameObject(cls ?? "group");
                        groupGo.transform.SetParent(_markers.transform, false);
                        for (int i = 0; i < count; i++)
                        {
                            string nm = ReadStr(r);
                            ReadStr(r);
                            float px = r.ReadSingle(), py = r.ReadSingle(), pz = r.ReadSingle();
                            float rx = r.ReadSingle(), ry = r.ReadSingle(),
                                  rz = r.ReadSingle(), rw = r.ReadSingle();
                            r.ReadSingle(); r.ReadSingle(); r.ReadSingle();
                            var go = new GameObject(nm ?? (cls + "_" + i));
                            go.transform.SetParent(groupGo.transform, false);
                            go.transform.localPosition = new Vector3(px, py, pz);
                            go.transform.localRotation = new Quaternion(rx, ry, rz, rw);
                            markerYs.Add(py);
                            n++;
                        }
                        _log?.Msg("[map] markers: " + cls + " x" + count);
                    }
                }
            }
            catch (Exception e) { _log?.Warning("[map] markers stopped: " + e.Message); }
            _log?.Msg("[map] placed " + n + " marker(s)");
            try
            {
                if (markerYs.Count > 0)
                {
                    markerYs.Sort();
                    float ourY = markerYs[markerYs.Count / 2];
                    float hostY = 0f;
                    var cam = Camera.main;
                    if (cam != null) hostY = cam.transform.position.y;
                    MapFloorFarFromHost = Mathf.Abs(hostY - ourY) > 20f;
                    _log?.Msg("[map] floor delta vs host: our median marker y="
                            + ourY.ToString("F1") + ", host (camera) y="
                            + hostY.ToString("F1") + " -> "
                            + (MapFloorFarFromHost
                               ? "FAR (host flares/fog would hang in our sky; they go)"
                               : "near (host flares/fog sit at plausible heights; prefs decide)"));
                }
            }
            catch { }
            return n;
        }

        static string ReadStr(System.IO.BinaryReader r)
        {
            int len = r.ReadUInt16();
            if (len == 0) return null;
            return System.Text.Encoding.UTF8.GetString(r.ReadBytes(len));
        }

        public static float? AuthoredGroundY()
        {
            try
            {
                if (_markers == null) return null;
                var ys = new List<float>();
                var mt = _markers.transform;
                for (int g = 0; g < mt.childCount; g++)
                {
                    var grp = mt.GetChild(g);
                    string cls = grp.name ?? "";

                    if (cls.IndexOf("SpawnPoint", StringComparison.OrdinalIgnoreCase) < 0
                        && cls.IndexOf("GuardPoint", StringComparison.OrdinalIgnoreCase) < 0
                        && cls.IndexOf("PlayerStart", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    for (int i = 0; i < grp.childCount; i++)
                        ys.Add(grp.GetChild(i).position.y);
                }
                if (ys.Count < 8) return null;
                ys.Sort();

                return ys[ys.Count / 2] - 1.0f;
            }
            catch { return null; }
        }

        static int _teleportTries;

        static bool _moveIssued;
        static bool _teleported;

        public static void SkipMarkedSpawn(string why)
        {
            if (_teleported) return;
            _teleported = true;
            try { SpawnTruth.ReportArrival(); } catch { }
            _log?.Msg("[map] marked-spawn teleport not needed: " + why);
        }

        public static void TeleportTick()
        {
            if (!_done || _teleported) return;

            if (!PlayerBody.HasMovableRig) { _teleportTries = 0; return; }

            Vector3 hold;
            if (PlayerBody.TryPosition(out hold) && AtHoldingPoint(hold))
            {
                _teleportTries = 0;
                if (Time.realtimeSinceStartup >= _holdLogAt)
                {
                    _holdLogAt = Time.realtimeSinceStartup + 10f;
                    _log?.Msg("[map] the player is at the game's holding point (" + hold.x.ToString("0.0") + ", "
                            + hold.y.ToString("0.0") + ", " + hold.z.ToString("0.0")
                            + "); waiting for the game's own deploy before placing them.");
                }
                return;
            }
            if (_teleportTries++ > 600) return;
            if (_teleportTries % 30 != 0) return;

            Vector3 bodyPos = Vector3.zero;
            bool havePos = false;
            bool inLevel = PlayerIsOnOurFloor();
            if (inLevel && PlayerBody.TryPosition(out bodyPos))
            {
                havePos = true;
                string offWhy;
                if (SpawnTruth.IsOutOfMap(bodyPos, out offWhy)) inLevel = false;
            }

            if (inLevel && havePos && !_moveIssued && SpawnSlots.AtOurSpawn(bodyPos))
            {
                _teleported = true;
                SpawnTruth.ReportArrival();
                SpawnSlots.NoteLanded(bodyPos);
                return;
            }

            if (inLevel && _moveIssued)
            {
                if (PlayerBody.ConfirmPending) return;
                if (!PlayerBody.Confirmed && !float.IsNaN(PlayerBody.LastError)
                    && PlayerBody.LastError > 25f)
                {
                    inLevel = false;
                    _log?.Warning("[map] the placement did not take (" + PlayerBody.LastError.ToString("F0")
                                + " m off); moving the player to the insertion again.");
                }
            }

            Vector3 markedProbe;
            if (inLevel && !_moveIssued && TryMarkedSpawn(out markedProbe)) inLevel = false;
            if (inLevel) { _teleported = true; SpawnTruth.ReportArrival(); return; }
            TeleportPlayerIn();

            try { MelonLoader.MelonCoroutines.Start(HoldPlayerOnOurFloor()); } catch { }
        }

        public static bool PlayerIsInMap()
        {
            try { return PlayerInMap(); } catch { return false; }
        }

        static bool PlayerInMap()
        {
            try
            {
                if (_root == null) return false;
                GameObject player = null;
                {

                    PlayerBody.Rig rig;
                    if (PlayerBody.TryGet(out rig) && rig.Tr != null)
                        player = rig.Tr.gameObject;
                }
                if (player == null) return false;

                var b = Bounds();
                var p = player.transform.position;

                return p.x >= b.min.x && p.x <= b.max.x
                    && p.z >= b.min.z && p.z <= b.max.z;
            }
            catch { return false; }
        }

        static bool _spawnRead;
        static bool _spawnHave;
        static Vector3 _spawnMarked;

        public static bool TryMarkedSpawn(out Vector3 v)
        {
            v = _spawnMarked;
            if (_spawnRead) return _spawnHave;
            _spawnRead = true;
            try
            {
                var f = PlacementsFile();
                if (f == null) { _log?.Msg("[map] no placements file for " + SceneName); return false; }

                var txt = File.ReadAllText(f);
                float x, y, z;
                if (!ReadVec(txt, "playerSpawn", out x, out y, out z)) return false;
                _spawnMarked = new Vector3(x, y, z);
                v = _spawnMarked;
                _spawnHave = true;
                _log?.Msg("[map] marked spawn for " + SceneName + ": " + _spawnMarked
                        + " (height will come from our floor, not from this file)");
                return true;
            }
            catch (Exception e) { _log?.Warning("[map] marked spawn: " + e.Message); return false; }
        }

        public static bool ReadVec(string json, string key, out float x, out float y, out float z)
        {
            x = y = z = 0f;
            try
            {
                int i = json.IndexOf("\"" + key + "\"", StringComparison.OrdinalIgnoreCase);
                if (i < 0) return false;
                int open = json.IndexOf('{', i); if (open < 0) return false;
                int close = json.IndexOf('}', open); if (close < 0) return false;
                var body = json.Substring(open + 1, close - open - 1);
                return Num(body, "x", out x) && Num(body, "y", out y) && Num(body, "z", out z);
            }
            catch { return false; }
        }

        static bool Num(string body, string k, out float v)
        {
            v = 0f;
            int i = body.IndexOf("\"" + k + "\"", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return false;
            int c = body.IndexOf(':', i); if (c < 0) return false;
            int e = c + 1;
            while (e < body.Length && body[e] != ',' && body[e] != '}') e++;
            return float.TryParse(body.Substring(c + 1, e - c - 1).Trim(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out v);
        }

        public static void ForgetMarks()
        {
            _hvtRead = false; _hvtHave = false;
            _spawnRead = false; _spawnHave = false;
        }

        static string PlacementsFile()
        {
            try
            {

                var own = MapIdentity.InFolder(SceneName, "placements.json");
                if (own != null) return own;
                var f = Path.Combine(Path.GetDirectoryName(BundleDir.TrimEnd('\\')),
                                     "placements", SceneName + ".json");
                if (File.Exists(f)) return f;
                string root = null;
                try { root = Path.GetDirectoryName(UnityEngine.Application.dataPath); } catch { }
                if (!string.IsNullOrEmpty(root))
                {
                    f = Path.Combine(root, "Mods", "MapLoader_Assets",
                                     "placements", SceneName + ".json");
                    if (File.Exists(f)) return f;
                }
            }
            catch { }
            return null;
        }

        static bool _hvtRead, _hvtHave;
        static Vector3 _hvtMarked;

        public static bool TryMarkedHvt(out Vector3 v)
        {
            v = _hvtMarked;
            if (_hvtRead) return _hvtHave;
            _hvtRead = true;
            try
            {
                var f = PlacementsFile();
                if (f == null) return false;
                float x, y, z;
                if (!ReadVec(File.ReadAllText(f), "hvt", out x, out y, out z)) return false;
                _hvtMarked = new Vector3(x, y, z);
                v = _hvtMarked;
                _hvtHave = true;
                _log?.Msg("[map] marked HVT spot for " + SceneName + ": " + _hvtMarked);
                return true;
            }
            catch (Exception e) { _log?.Warning("[map] marked hvt: " + e.Message); return false; }
        }

        public static Vector3 GroundedUnder(Vector3 p, float lookDown = 6f)
        {
            try
            {
                if (_root == null) return p;
                float startY = p.y + 0.5f;
                var hits = new Il2CppInterop.Runtime.InteropTypes.Arrays
                               .Il2CppStructArray<RaycastHit>(24L);
                int n = Physics.RaycastNonAlloc(new Vector3(p.x, startY, p.z), Vector3.down,
                                                hits, lookDown, ~0, QueryTriggerInteraction.Ignore);
                float best = float.NegativeInfinity;
                for (int i = 0; i < n && i < hits.Length; i++)
                {
                    var h = hits[i];
                    var c = h.collider;
                    if (c == null) continue;
                    if (!SurfaceIsOurs(c)) continue;
                    if (h.point.y > startY) continue;
                    if (h.point.y > best) best = h.point.y;
                }
                if (best > float.NegativeInfinity)
                {
                    _log?.Msg("[map] grounded under mark: y " + p.y.ToString("F2")
                            + " -> floor " + best.ToString("F2"));
                    return new Vector3(p.x, best + 0.05f, p.z);
                }
                _log?.Warning("[map] no floor of ours within " + lookDown
                            + " m below the mark; using it unchanged");
            }
            catch (Exception e) { _log?.Warning("[map] GroundedUnder: " + e.Message); }
            return p;
        }

        public static Vector3 GroundedSpawn(Vector3 p)
        {
            bool found;
            return GroundedSpawn(p, out found);
        }

        public static Vector3 GroundedSpawn(Vector3 p, out bool found)
        {
            found = false;
            try
            {
                if (_root == null) return p + new Vector3(0f, 1.2f, 0f);
                float startY = p.y + 2f;
                var hits = new Il2CppInterop.Runtime.InteropTypes.Arrays
                               .Il2CppStructArray<RaycastHit>(32L);
                int n = Physics.RaycastNonAlloc(new Vector3(p.x, startY, p.z), Vector3.down,
                                                hits, 60f, ~0,
                                                QueryTriggerInteraction.Ignore);
                float best = float.NegativeInfinity;
                for (int i = 0; i < n && i < hits.Length; i++)
                {
                    var h = hits[i];
                    var c = h.collider;
                    if (c == null) continue;
                    if (!SurfaceIsOurs(c)) continue;
                    if (h.point.y > startY) continue;
                    if (h.point.y > best) best = h.point.y;
                }

                float maxBelow = 2.5f;
                if (best > float.NegativeInfinity && best < p.y - maxBelow)
                {
                    _log?.Warning("[map] spawn: the ray says the floor is y="
                            + best.ToString("F2") + " but the AUTHORED marker is y="
                            + p.y.ToString("F2") + " -- " + (p.y - best).ToString("F2")
                            + " m below a marker the builder verified against the "
                            + "terrain. Refusing it: that is an underside or a "
                            + "collider with no top, and standing the player on it "
                            + "is how a spawn turns into a two-second fall.");
                    best = float.NegativeInfinity;
                }
                if (best > float.NegativeInfinity)
                {
                    found = true;
                    _log?.Msg("[map] spawn grounded: marker y=" + p.y.ToString("F2")
                            + " -> our floor y=" + best.ToString("F2")
                            + " (difference " + (best - p.y).ToString("F2")
                            + " m; that difference is what used to snap the player up "
                            + "after walking)");
                    return new Vector3(p.x, best + 1.1f, p.z);
                }
                _log?.Warning("[map] spawn: no floor of ours under the insertion point; "
                            + "using the marker height unchanged");
            }
            catch (Exception e) { _log?.Warning("[map] GroundedSpawn: " + e.Message); }
            return p + new Vector3(0f, 1.2f, 0f);
        }

        public static string FloorBlockerReport()
        {
            try
            {
                PlayerBody.Rig rig;
                if (!PlayerBody.TryGet(out rig) || rig.Tr == null)
                    return "player rig unresolved";
                var p = rig.Tr.position;
                var hits = new Il2CppInterop.Runtime.InteropTypes.Arrays
                               .Il2CppStructArray<RaycastHit>(24L);
                int n = Physics.RaycastNonAlloc(p + Vector3.up * 1.0f, Vector3.down,
                                                hits, 4f, ~0,
                                                QueryTriggerInteraction.Ignore);
                float best = float.NegativeInfinity;
                Collider bc = null;
                var player = rig.Tr;
                for (int i = 0; i < n && i < hits.Length; i++)
                {
                    var h = hits[i];
                    var c = h.collider;
                    if (c == null) continue;
                    try { if (c.transform.IsChildOf(player)) continue; } catch { }
                    if (h.point.y <= best) continue;
                    best = h.point.y;
                    bc = c;
                }
                if (bc == null)
                    return "nothing within 4 m under the player at ("
                         + p.x.ToString("F1") + ", " + p.y.ToString("F1") + ", "
                         + p.z.ToString("F1") + ")";
                bool ours = false;
                string rootName = "?";
                try { ours = _root != null && bc.transform.IsChildOf(_root.transform); } catch { }
                try { rootName = bc.transform.root.name; } catch { }
                return "top surface under the player is '" + bc.name + "' (root '"
                     + rootName + "', y=" + best.ToString("F2") + ", ours=" + ours + ")";
            }
            catch (Exception e) { return "blocker report failed: " + e.Message; }
        }

        public static bool PlayerIsOnOurFloor()
        {
            try
            {
                if (_root == null) return false;
                GameObject player = null;
                {

                    PlayerBody.Rig rig;
                    if (PlayerBody.TryGet(out rig) && rig.Tr != null)
                        player = rig.Tr.gameObject;
                }
                if (player == null) return false;

                var p = player.transform.position;
                var hits = new Il2CppInterop.Runtime.InteropTypes.Arrays
                               .Il2CppStructArray<RaycastHit>(24L);

                int n = Physics.RaycastNonAlloc(p + Vector3.up * 1.0f, Vector3.down,
                                                hits, 4f, ~0,
                                                QueryTriggerInteraction.Ignore);
                float best = float.NegativeInfinity;
                bool bestIsOurs = false;
                for (int i = 0; i < n && i < hits.Length; i++)
                {
                    var h = hits[i];
                    var c = h.collider;
                    if (c == null) continue;

                    try { if (c.transform.IsChildOf(player.transform)) continue; } catch { }
                    if (h.point.y <= best) continue;
                    best = h.point.y;

                    if (MapIdentity.HostLevelIsMap) bestIsOurs = true;
                    else
                    {
                        try { bestIsOurs = c.transform.IsChildOf(_root.transform); }
                        catch { bestIsOurs = false; }
                    }
                }
                return bestIsOurs;
            }
            catch { return false; }
        }

        public static System.Collections.IEnumerator HoldPlayerOnOurFloor()
        {

            if (MapIdentity.HostLevelIsMap)
            {

                _log?.Msg("[map] host level IS the map: the under-the-floor lift "
                        + "does not apply and is skipped");
                yield break;
            }
            float deadline = Time.realtimeSinceStartup + 20f;
            int corrections = 0;
            float lastLift = 0f;

            while (Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForSeconds(0.1f);
                GameObject player = null;
                try
                {
                    PlayerBody.Rig rig;
                    if (PlayerBody.TryGet(out rig) && rig.Tr != null)
                        player = rig.Tr.gameObject;
                }
                catch { }
                if (BlackHawk.Riding) continue;
                if (player == null) continue;

                float lift = 0f;
                try
                {
                    var p = player.transform.position;

                    var hits = new Il2CppInterop.Runtime.InteropTypes.Arrays
                                   .Il2CppStructArray<RaycastHit>(24L);
                    int n = Physics.RaycastNonAlloc(p + Vector3.up * 8f, Vector3.down,
                                                    hits, 12f, ~0,
                                                    QueryTriggerInteraction.Ignore);
                    float ours = float.NegativeInfinity;
                    for (int i = 0; i < n && i < hits.Length; i++)
                    {
                        var h = hits[i];
                        var c = h.collider;
                        if (c == null) continue;

                        try { if (!c.transform.IsChildOf(_root.transform)) continue; } catch { continue; }
                        if (h.point.y > ours) ours = h.point.y;
                    }

                    if (ours > float.NegativeInfinity && p.y < ours - 0.4f)
                        lift = ours + 0.1f;
                }
                catch { }

                if (lift <= 0f) continue;

                try
                {
                    var p = player.transform.position;
                    if (PlayerBody.MoveTo(new Vector3(p.x, lift, p.z), "hold on our floor"))
                    {
                        corrections++;
                        lastLift = lift - p.y;
                    }
                }
                catch { }
            }

            if (corrections > 0)
                _log?.Msg("[map] player was below our floor and was lifted "
                        + corrections + " time(s), last by "
                        + lastLift.ToString("F2") + " m. More than one correction means "
                        + "the GAME is respawning the player after we place them, not "
                        + "that our placement was wrong.");
            else
                _log?.Msg("[map] player stayed on our floor with no corrections needed.");
        }

        public static void VoidWatchTick()
        {
            SpawnTruth.Tick();
        }

        static float _notReadyLogAt, _holdLogAt;

        static bool AtHoldingPoint(Vector3 p)
        {
            return Mathf.Abs(p.x) < 1.5f && Mathf.Abs(p.z) < 1.5f && p.y < -20f;
        }

        public static bool TeleportPlayerIn()
        {
            try
            {

                if (MapIdentity.HostLevelIsMap && !AdditiveReady)
                {
                    if (Time.realtimeSinceStartup >= _notReadyLogAt)
                    {
                        _notReadyLogAt = Time.realtimeSinceStartup + 5f;
                        _log?.Msg("[map] the additive world is not up yet; the player's placement waits for it.");
                    }
                    return false;
                }
                Transform target = null;
                foreach (var grp in new[] { "GBInsertionPoint", "GBPlayerStart" })
                {
                    var list = MapAI.FindGroup(grp);
                    if (list.Count > 0) { target = list[0]; break; }
                }
                if (target == null)
                {
                    _log?.Warning("[map] no insertion point or player start to move to");
                    return false;
                }

                PlayerBody.Rig rig;
                if (!PlayerBody.TryGet(out rig) || rig.Tr == null)
                { _log?.Warning("[map] no player to move"); return false; }

                Vector3 marked;
                if (TryMarkedSpawn(out marked))
                {

                    bool onFloor;
                    var mpos = GroundedSpawn(marked, out onFloor);
                    if (onFloor)
                    {
                        if (!PlayerBody.MoveTo(mpos, "marked spawn for " + SceneName))
                        {
                            _log?.Error("[map] the MARKED spawn at " + mpos + " could not be "
                                      + "applied to the player body. The player is NOT in the map.");
                            return false;
                        }
                        _log?.Msg("[map] requested the MARKED spawn at " + mpos
                                + " (from placements/" + SceneName + ".json). The [body] CONFIRM "
                                + "line a moment from now is what says whether it took.");
                        _moveIssued = true;
                        return true;
                    }
                    _log?.Warning("[map] the MARKED spawn at " + marked + " has NO floor of "
                                + "ours beneath it -- refusing it and falling back to the "
                                + "authored player starts.");
                }

                bool tFound;
                var pos = GroundedSpawn(target.position, out tFound);
                if (!tFound)
                {
                    var starts = MapAI.FindGroup("GBPlayerStart");
                    for (int i = 0; i < starts.Count; i++)
                    {
                        if (starts[i] == null) continue;
                        bool f2;
                        var p2 = GroundedSpawn(starts[i].position, out f2);
                        if (f2)
                        {
                            _log?.Warning("[map] '" + target.name + "' had no floor of ours; "
                                        + "spawning at '" + starts[i].name + "' instead.");
                            target = starts[i];
                            pos = p2;
                            tFound = true;
                            break;
                        }
                    }
                }
                if (!tFound)
                    _log?.Error("[map] NO floor of ours under '" + target.name + "' or any "
                              + "authored player start. The map likely failed to build its "
                              + "ground; spawning at the marker height anyway.");

                if (!PlayerBody.MoveTo(pos, "insertion marker " + target.name))
                {
                    _log?.Error("[map] '" + target.name + "' at " + pos + " could not be applied "
                              + "to the player body. The player is NOT in the map.");
                    return false;
                }
                _log?.Msg("[map] requested '" + target.name + "' at " + pos
                        + "; the [body] CONFIRM line is the result.");

                _moveIssued = true;
                        return true;
            }
            catch (Exception e) { _log?.Error("[map] TeleportPlayerIn: " + e.Message); return false; }
        }

        static void FreeMeshCpuCopies()
        {
            int freed = 0, kept = 0;
            try
            {
                foreach (var kv in _meshCache)
                {
                    var m = kv.Value;
                    if (m == null) continue;

                    if (!_meshIsFoliage.Contains(kv.Key)) { kept++; continue; }
                    if (_meshHasCollider.Contains(kv.Key)) { kept++; continue; }
                    try { m.UploadMeshData(true); freed++; }
                    catch { kept++; }
                }
                _log?.Msg("[perf] freed the CPU-side copy of " + freed + " foliage mesh(es), "
                        + "kept " + kept + ". The GPU copy is untouched so nothing looks "
                        + "different; only foliage is eligible because the navmesh scan "
                        + "reads vertices off every other mesh AFTER this runs.");
            }
            catch (Exception e) { _log?.Warning("[perf] FreeMeshCpuCopies: " + e.Message); }
        }

        static void Finish()
        {
            _building = false;
            try
            {
                if (_buildStartedAt > 0f)
                    BuildSeconds = Time.realtimeSinceStartup - _buildStartedAt;
            }
            catch { }
            SafeRunLocal("free mesh cpu copies", FreeMeshCpuCopies);

            SafeRunLocal("ground plants", GroundPlants);

            SafeRunLocal("tree trunks", TreeTrunkCapsules);

            SafeRunLocal("map lights", MapLights.Build);

            SafeRunLocal("map ambience", () => { MapAmbience.OfferPlants(_plants); MapAmbience.OfferWater(_waterT); MapAmbience.Build(); });

            _done = true;

            LoadVeil.BuildFinished();
            if (_doorPanelsAsScenery > 0 || _doorPanelsSkipped > 0)
                _log?.Msg("[map] door leaves: " + _doorPanelsAsScenery + " built as scenery, "
                        + _doorPanelsSkipped + " skipped. The city has far more doorways than "
                        + "doors that can be made to open, so the leaves stay and only the "
                        + "few that clash with a placed working door are culled afterwards.");
            if (_doorPanelsKept > 0)
                _log?.Msg("[map] kept " + _doorPanelsKept + " instance(s) whose name says "
                        + "door but whose size says building. Before the size check "
                        + "these were deleted, which on the city took a third of the "
                        + "ground-floor tier with them");
            if (_vehicles > 0 || _overDetailed > 0)
                _log?.Msg("[map] " + _vehicles + " vehicle instance(s) and " + _overDetailed
                        + " over-detailed prop(s) taken out of the shadow pass. Between them "
                        + "they were 41% of the map's shadow triangles while casting "
                        + "silhouettes nothing could read: the trains are underground where "
                        + "no light reaches, and a 179k-triangle sofa does not have a "
                        + "179k-triangle outline. Expect ~199 vehicles on the city; 434 "
                        + "would mean the branch order changed and wheels are falling "
                        + "through to here instead of into SmallProp.");
            if (_smallProps > 0)
                _log?.Msg("[map] " + _smallProps + " small prop(s) taken out of the shadow "
                        + "pass and culled at distance (each one is trivial; there are "
                        + "thousands of them)");
            if (_crevicePlugs > 0)
                _log?.Msg("[map] " + _crevicePlugs + " basement crevice plug(s) hidden but "
                        + "still solid. They are floor meshes scaled up to 28x vertically to "
                        + "fill the basement shafts, never meant to be seen, and one was "
                        + "standing in a doorway. Colliders kept: they cover all 176 "
                        + "landscape holes.");
            if (_foliageShadowCasters > 0)
                _log?.Msg("[map] " + _foliageShadowCasters + " mature tree(s) KEPT in the "
                        + "shadow pass -- the dappled canopy shadow on the ground and the "
                        + "walls is the whole reason a wooded map reads as lit. Grass and "
                        + "scrub stay out; cost is bounded by the shadow distance.");
            if (_foliageNoShadow > 0)
                _log?.Msg("[map] " + _foliageNoShadow + " foliage renderer(s) removed from "
                        + "the shadow pass (they were 61% of the map's 52M triangles and were "
                        + "being redrawn once per shadow cascade)");
            if (_shadowProxies > 0)
                _log?.Msg("[map] " + _shadowProxies + " shadow-proxy sheet(s) hidden and stopped "
                        + "casting (the teal tiles in the fields: coarse copies of the "
                        + "landscape buried 5 cm under it on Unreal's no-material-assigned "
                        + "material, every vertex on one UV so the whole sheet drew a single "
                        + "cyan texel. The real terrain casts its own shadow.)");
            if (_fakeLightCards > 0)
                _log?.Msg("[map] hid " + _fakeLightCards + " Unreal fake-light card(s) "
                        + "that would have hung on the walls as grey squares");
            if (MeshFactory.SkippedSubmeshes > 0)
                _log?.Msg("[map] skipped " + MeshFactory.SkippedSubmeshes
                        + " untextured water submesh(es) (the canal surface that "
                        + "showed as blue panels in the fields)");
            if (MeshFactory.GroundDarkened > 0)
                _log?.Msg("[map] darkened " + MeshFactory.GroundDarkened + " ground material(s) to "
                        + (MeshFactory.GroundAlbedoScale * 100f).ToString("F0")
                        + "% of authored albedo (arrows retune this live)");
            if (_cappedInstances > 0)
                _log?.Msg("[map] FoliageMax dropped " + _cappedInstances + " foliage instance(s) "
                        + "that are in the bundle and are not on screen");
            if (_meshesDropped > 0)
            {
                _log?.Msg("[map] refused " + _meshesDropped + " mesh(es) / " + _instancesDropped
                        + " instance(s) by name (MapIdentity.DropMeshWords). Their meshes were "
                        + "never read and their textures never decoded, so this is VRAM and "
                        + "build time as well as geometry.");
                for (int i = 0; i < _droppedIds.Count; i++)
                    _log?.Msg("[map]      " + _droppedIds[i]);
            }
            StairFill.LogSummary(_log);
            if (MapIdentity.PlateSlabs) PlateFill.LogSummary(_log);

            try { if (MeshFactory.GlassCount > 0) _log?.Msg("[map] " + MeshFactory.GlassList()); } catch { }
            try
            {
                if (MapIdentity.DonorMaterials)
                    _log?.Msg("[map] materials copied from a shipped one rather than built from a "
                            + "bare shader: " + MeshFactory.DonorNote + ". Keywords are the game's, "
                            + "we set values only. This is the cure for see-through and chrome "
                            + "surfaces: our own keyword set resolves to a variant with no depth pass.");
            }
            catch { }
            _log?.Msg("[map] BUILT " + _placed + " instance(s), "
                    + (_meshCache.Count - _meshesFailed) + " mesh(es)"
                    + (_meshesFailed > 0 ? " (" + _meshesFailed + " FAILED)" : "")
                    + ", " + MeshFactory.BoundCount + " texture(s) bound"
                    + (MeshFactory.FailedCount > 0
                        ? ", " + MeshFactory.FailedCount + " FAILED" : "")
                    + "  [" + MeshFactory.NormalCount + " normal, "
                    + MeshFactory.MaskCount + " mask, "
                    + MeshFactory.CutoutCount + " cutout, "
                    + MeshFactory.GlassCount + " glass"
                    + (MeshFactory.CutoutRejected > 0
                        ? ", " + MeshFactory.CutoutRejected + " clear-alpha REFUSED clipping"
                        : "")
                    + "]");

            try
            {
                long cmp = MeshFactory.TexBytesCompressed, raw = MeshFactory.TexBytesRaw;
                _log?.Msg("[perf] texture memory ~" + ((cmp + raw) / 1048576) + " MB  ("
                        + MeshFactory.TexCountCompressed + " compressed = " + (cmp / 1048576)
                        + " MB, " + MeshFactory.TexCountRaw + " uncompressed = " + (raw / 1048576)
                        + " MB). Normal and mask maps are deliberately RGBA32, so they cost "
                        + "four times an albedo of the same size. Compare this against the "
                        + "card's VRAM in the header: over it, the driver pages and the game "
                        + "stalls for seconds at a time.");
                _log?.Msg("[perf] per-renderer: " + _rendOptDone + " renderer(s) told to skip "
                        + "light probes, reflection probes, motion vectors and dynamic "
                        + "occlusion" + (_rendOptFail == 0 ? "" : "  (FAILED mask 0x"
                        + _rendOptFail.ToString("X") + " -- 1=probes 2=refl 4=motion 8=occl 16=staticshadow)")
                        + ". None of those have inputs on a runtime-built map, so this is "
                        + "work declined rather than quality dropped.");
            }
            catch { }

            try
            {
                int vramMB2 = 0;
                try { vramMB2 = SystemInfo.graphicsMemorySize; } catch { }
                double cur = UnityEngine.Texture.currentTextureMemory / (1024.0 * 1024.0);
                _log?.Msg("[vram] '" + SceneName + "': resident textures "
                        + cur.ToString("F0") + " MB of " + vramMB2 + " MB VRAM ("
                        + (vramMB2 > 0 ? (cur * 100.0 / vramMB2).ToString("F0") : "?")
                        + "%)" + (vramMB2 > 0 && cur > vramMB2 * 0.85
                                  ? "   *** OVER 85% -- the driver evicts and repages, which is "
                                  + "black patches that follow the camera and multi-second "
                                  + "stalls ***" : ""));

                if (vramMB2 > 0 && cur > vramMB2)
                {
                    _log?.Warning("[vram] OVER THE CARD by "
                        + (cur - vramMB2).ToString("F0") + " MB. Every frame the driver "
                        + "now evicts and re-uploads textures across PCIe. This is the state "
                        + "that hung the whole desktop on 2026-08-28: frame times climb past "
                        + "200 ms, then the main thread stops and the Windows shell goes with "
                        + "it. Nothing here fixes it automatically -- every lever costs image "
                        + "quality. Alt+Tab out at the first multi-second freeze.");
                    try { OdgConsole.WarnVram(cur, vramMB2); } catch { }
                }
            }
            catch (Exception e) { _log?.Warning("[vram] read: " + e.Message); }
            _log?.Msg("[map] build phases: decode " + MeshFactory.DecodeMs + " ms, compress "
                    + MeshFactory.CompressMs + " ms, mesh build " + MeshFactory.MeshBuildMs
                    + " ms, material build " + MeshFactory.MaterialMs + " ms");

            LoadVeil.Progress(0.91f, "measuring the map");
            var b = Bounds();
            ShellSwap.SetMapBounds(b, true);

            AnchorFogToGround();
            _log?.Msg("[map] bounds " + b.size.x.ToString("F0") + " x "
                    + b.size.z.ToString("F0") + " m centred " + b.center);

            LoadVeil.Progress(0.92f, "placing markers");
            PlaceMarkers();

            if (MapIdentity.OwnSky)
                SafeRunLocal("void patches", () => VoidPatches.Build(BundleDir, _rootT));

            if (_aoBuckets != null)
            {
                _log?.Msg("[shade] buckets applied: open " + _shapeHist[0]
                        + ", covered " + _shapeHist[1] + ", interior " + _shapeHist[2]
                        + ", deep " + _shapeHist[3] + " at strength "
                        + ShadeStrength.ToString("F2") + " -> x" + EffectiveAoMult(0).ToString("F2")
                        + " / x" + EffectiveAoMult(1).ToString("F2")
                        + " / x" + EffectiveAoMult(2).ToString("F2")
                        + " / x" + EffectiveAoMult(3).ToString("F2")
                        + " (indirect-only; 'odg shade' dials it live, 'variant G' "
                        + "flips it to prove its contribution)");

                _shadeReportAt = Time.realtimeSinceStartup + 8f;
            }
            LoadVeil.Progress(0.93f, "lighting the night");
            AddLighting();

            LoadVeil.Progress(0.94f, "clearing the host level");
            if (MapIdentity.HostLevelIsMap
                && !string.IsNullOrEmpty(MapIdentity.AdditiveSceneName))
            {

                try
                {
                    var already = UnityEngine.SceneManagement.SceneManager
                                      .GetSceneByName(MapIdentity.AdditiveSceneName);
                    if (already.IsValid() && already.isLoaded)
                        _log?.Msg("[map] additive world already loaded");
                    else
                        UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(
                            MapIdentity.AdditiveSceneName,
                            UnityEngine.SceneManagement.LoadSceneMode.Additive);
                    _addRequested = true;
                    _log?.Msg("[map] loading the real '" + MapIdentity.AdditiveSceneName
                            + "' additively -- it IS this map's world");
                }
                catch (Exception e)
                { _log?.Warning("[map] additive load request failed: " + e.Message); }
            }
            {
                int hidden = ShellSwap.HideShell(_root.transform, _log);
                _log?.Msg("[map] hid " + hidden + " host renderer(s)");

                try { ShellSwap.SweepHostRenderers(_root.transform, _log, true); } catch { }
            }
            if (MapIdentity.HostLevelIsMap)
            {

                try { ShellSwap.SetHostLights(false, _log); } catch { }
                DisableHostVolumes();
            }

            MapWater.Place();

            MapGlass.Place();

            if (DayIsHostLit && !MapIdentity.HostLevelIsMap)
            {
                _log?.Msg("[map] " + ShellSwap.SoftenHostShadows(HostShadowDimmer, _log));
                _log?.Msg("[map] " + ShellSwap.RetuneHostSun(SunElevation, SunAzimuth,
                                                             SunKelvin, _log));

                if (MapIdentity.SunShadowResolution > 0)
                    _log?.Msg("[map] " + ShellSwap.TuneHostSunShadows(
                        MapIdentity.SunShadowResolution, MapIdentity.SunAngularDiameterDeg, _log));
            }

            try { Physics.SyncTransforms(); } catch { }
            ApplyFoliageCulling();
            LoadVeil.Progress(0.96f, "settling the foliage");
            GroundFoliage();

            if (HarnessBuild)
            {
                _teleported = true;
                _log?.Msg("[map] harness build complete: teleport, scene "
                        + "notification and the gameplay layers were skipped "
                        + "on purpose.");
                return;
            }

            TeleportPlayerIn();

            MapScene.NotifySceneLoaded(SceneName);
        }

        static Bounds _bounds;
        static bool _boundsCached;

        public static void InvalidateBounds() { _boundsCached = false; }

        public static float GroundY
        {
            get { try { return Bounds().min.y; } catch { return 0f; } }
        }

        public static Bounds WalkableFootprint(int minObjects, out bool fromGround,
                                               out int groundObjects, out int markers)
        {
            fromGround = false;
            groundObjects = 0;
            markers = 0;
            var all = Bounds();
            try
            {
                if (_root == null) return all;

                var b = new Bounds();
                bool any = false;
                var rs = _root.GetComponentsInChildren<Renderer>(false);
                for (int i = 0; i < rs.Length; i++)
                {
                    var r = rs[i];
                    if (r == null) continue;
                    int id;
                    try { id = r.gameObject.GetInstanceID(); } catch { continue; }
                    if (!_groundIds.Contains(id)) continue;
                    groundObjects++;
                    if (!any) { b = r.bounds; any = true; }
                    else b.Encapsulate(r.bounds);
                }
                if (!any || groundObjects < minObjects) return all;

                var mt = MarkersRoot;
                if (mt != null)
                {
                    for (int g = 0; g < mt.childCount; g++)
                    {
                        var grp = mt.GetChild(g);
                        string cls = grp.name ?? "";
                        if (cls.IndexOf("SpawnPoint", StringComparison.OrdinalIgnoreCase) < 0
                            && cls.IndexOf("GuardPoint", StringComparison.OrdinalIgnoreCase) < 0
                            && cls.IndexOf("PlayerStart", StringComparison.OrdinalIgnoreCase) < 0
                            && cls.IndexOf("InsertionPoint", StringComparison.OrdinalIgnoreCase) < 0)
                            continue;
                        for (int i = 0; i < grp.childCount; i++)
                        {
                            var p = grp.GetChild(i).position;
                            b.Encapsulate(new Vector3(p.x, b.center.y, p.z));
                            markers++;
                        }
                    }
                }

                b.Encapsulate(new Vector3(b.center.x, all.min.y, b.center.z));
                b.Encapsulate(new Vector3(b.center.x, all.max.y, b.center.z));
                fromGround = true;
                return b;
            }
            catch { fromGround = false; return all; }
        }

        public static Bounds Bounds()
        {
            if (_boundsCached) return _bounds;
            var b = new Bounds(Vector3.zero, new Vector3(10f, 10f, 10f));
            bool any = false;
            if (_root == null) return b;
            var rs = _root.GetComponentsInChildren<Renderer>(false);
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i] == null) continue;
                if (!any) { b = rs[i].bounds; any = true; }
                else b.Encapsulate(rs[i].bounds);
            }

            if (any) { _bounds = b; _boundsCached = true; }
            return b;
        }

        public static void Teardown()
        {
            _boundsCached = false;
            LoadVeil.Hide("map torn down");
            try
            {
                ShellSwap.RestoreShell(_log);
                MapDoors.RestoreRelocated();
                MapExfil.Restore();
                DoorWiring.RestoreEnabled();
                RestoreHiddenScene();

                try
                {
                    if (_prevActive.IsValid() && _prevActive.isLoaded)
                        SceneManager.SetActiveScene(_prevActive);
                }
                catch { }
                if (_root != null) UnityEngine.Object.Destroy(_root);
                _root = null;

                _markers = null;
                _lighting = null;

                _nightExposure = null;
                _dayExposure = null;
                _ownPriority = 0f;
                _moonHd = null;
                _fillHd = null;
                _moonGo = null;
                SkyRig.Forget();
                MapLights.Forget();
                MapAmbience.Forget();
                VoidPatches.Forget();
                FakeBounce.Forget();
            NightArt.Forget(); DayArt.Forget();

            MeshFactory.ForgetGroundMats();
            NightFog = null;
            _dayProfile = null;
            _ssgi = null;
            _hostLitOverride = null;
            ApvGate.Demand = false;
                _rootT = null;
                _plants.Clear();
                _flags.Clear();
                _groundIds.Clear();
                _shadowProxyIds.Clear();
                ZoneGuard.ResetForNewLevel();
                SpawnShield.ResetForNewLevel("map torn down");
                SpawnSlots.ResetForNewLevel("map torn down");
                BulletWatch.ResetForNewLevel("map torn down");
                DoorSync.ResetForNewLevel("map torn down");
                _teleported = false;
                _teleportTries = 0; _moveIssued = false;
                if (_scene.IsValid() && _scene.isLoaded)
                    SceneManager.UnloadSceneAsync(_scene);
                _scene = default(Scene);
            }
            catch (Exception e) { _log?.Warning("[map] teardown: " + e.Message); }

            try
            {
                foreach (var m in _meshCache.Values)
                    if (m != null) UnityEngine.Object.Destroy(m);
            }
            catch { }
            try
            {
                foreach (var arr in _mats.Values)
                {
                    if (arr == null) continue;
                    for (int i = 0; i < arr.Length; i++)
                        if (arr[i] != null) UnityEngine.Object.Destroy(arr[i]);
                }
            }
            catch { }

            try
            {
                for (int i = 0; i < _bucketCreated.Count; i++)
                    if (_bucketCreated[i] != null) UnityEngine.Object.Destroy(_bucketCreated[i]);
            }
            catch { }
            try { if (_whiteMask != null) UnityEngine.Object.Destroy(_whiteMask); } catch { }
            _bucketMats.Clear();
            _bucketCreated.Clear();
            _shapedRenderers.Clear();
            _bucketTuned.Clear();
            _shadeReportAt = -1f;
            _aoBuckets = null;
            _aoBase = null;
            _whiteMask = null;
            _meshCache.Clear();
            _mats.Clear();
            MeshFactory.DestroyTextures();
            MeshFactory.ResetCaches();
            _building = false; _done = false; _placed = 0;
            _meshIndex = 0; _instanceIndex = 0;
            _meshesFailed = 0; _cappedInstances = 0;
            _log?.Msg("[map] torn down");
        }

        public static void DumpState()
        {
            _log?.Msg("[map] building=" + _building + " done=" + _done
                    + " placed=" + _placed + " meshes=" + _meshCache.Count
                    + " scene=" + (_scene.IsValid() ? _scene.name : "<none>")
                    + " active=" + SceneManager.GetActiveScene().name);
        }
    }
}
