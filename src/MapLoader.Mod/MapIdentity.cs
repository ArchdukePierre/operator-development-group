// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;

namespace MapLoader
{

    internal static class MapIdentity
    {
        internal sealed class Entry
        {
            public string Name;
            public string SceneName;
            public string RootName;
            public string BundleDir;
            public bool Installed;

            public bool NightSafe;

            public bool OwnSky;

            public bool FullDoors;

            public bool SkipInteriorDoors;

            public bool OutdoorDoorsAreGates;

            public bool PoolWater;

            public bool DoorSteelTint;

            public bool WantsGroundSkirt;

            public float MinStepOffset;

            public float LightIntensityScale;

            public bool StrictGlass;

            public bool DonorMaterials;

            public float DetailTile;

            public int MaxLights;

            public bool TreeTrunkColliders;

            public float FoliageCullM;

            public float CanopyFactor;

            public bool FoliageShadows;

            public float GroundAlbedo;

            public float DetailNormal;

            public bool VolumetricRays;

            public bool SurfaceDetail;

            public bool FoliageBackfaceFlip;

            public float DaySunElevationDeg;

            public bool FullResTextures;

            public bool HostLevelIsMap;

            public bool HostGrassOff;

            public string[] DropMeshWords;

            public string AdditiveSceneName;

            public string[] AdditiveKillNames;
            public float NormalScaleOverride;
            public bool MatteGroundExtra;
            public bool UncompressedGround;

            public float FoliageMipBias;

            public float GroundAlbedoScaleOverride;

            public float ShadowDistanceM;

            public bool FoliageNoSpecular;

            public float SmoothnessCeilingOverride;

            public bool DefaultHostLighting;

            public float DoorYawOffset;

            public float WanderDistance;
            public bool DayAutoExposure;

            public bool CinematicDay;

            public float DaySunKelvin;

            public float HostShadowDimmerOverride;

            public int FoliageMaxPerMesh;

            public bool OpenTreadStairs;

            public bool PlateSlabs;

            public bool HeliInfil;

            public bool OceanWater;

            public float SeaLevelY;

            public bool SeaSky;

            public int SkyCubeSize;

            public int SunShadowResolution;

            public float SunAngularDiameterDeg;
        }

        static readonly Dictionary<string, Entry> _byName =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        static readonly List<Entry> _order = new List<Entry>();
        static MelonLogger.Instance _log;

        internal static Entry Current { get; private set; }

        internal static void Init(MelonLogger.Instance log, string mapNames)
        {
            _log = log;
            _byName.Clear();
            _order.Clear();

            foreach (var raw in (mapNames ?? "Farmstead").Split(','))
            {
                var n = (raw ?? "").Trim();
                if (n.Length == 0) continue;
                if (_byName.ContainsKey(n)) continue;
                var e = Build(n);
                _byName[n] = e;
                _order.Add(e);
            }

            foreach (var n in CustomMaps.Scan())
            {
                if (_byName.ContainsKey(n)) continue;
                var e = Build(n);
                _byName[n] = e;
                _order.Add(e);
            }
            if (_order.Count == 0)
            {
                var e = Build("Farmstead");
                _byName[e.Name] = e; _order.Add(e);
            }
            Current = _order[0];

            foreach (var e in _order)
                _log?.Msg("[mapid] " + e.Name + ": scene='" + e.SceneName + "' root='"
                        + e.RootName + "' data=" + (e.Installed ? "present" : "MISSING")
                        + " at " + e.BundleDir);
            if (_order.Count > 1)
                _log?.Msg("[mapid] " + _order.Count + " map(s) registered. They are SIBLING "
                        + "missions inside the one DUKE'S OPS row, in this order, with "
                        + "'" + _order[0].Name + "' at index 0.");
        }

        static Entry Build(string name)
        {

            string dir = null;
            try
            {
                var root = MelonLoader.Utils.MelonEnvironment.GameRootDirectory;
                if (!string.IsNullOrEmpty(root))
                {
                    var shipped = Path.Combine(root, "Mods", "MapLoader_Assets", name);
                    if (Directory.Exists(shipped)) dir = shipped;

                    if (dir == null)
                    {
                        var custom = Path.Combine(root, "Mods", "MapLoader_Assets",
                                                  CustomMaps.FolderName, name);
                        if (Directory.Exists(custom)) dir = custom;
                    }
                }
            }
            catch { }
            if (dir == null) dir = @"D:\GB_work\bundle\" + name;

            bool ok = false;
            try { ok = File.Exists(Path.Combine(dir, "manifest.bin")); } catch { }

            return new Entry
            {
                Name = name,
                SceneName = name,
                RootName = "MAP_" + name,
                BundleDir = dir,
                Installed = ok,

                NightSafe = string.Equals(name, "Farmstead", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(name, "Rig", StringComparison.OrdinalIgnoreCase),

                OwnSky = !(string.Equals(name, "Farmstead", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(name, "LittleRocketMan", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(name, "Trenches", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(name, "Killhouse", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(name, "VirginiaBeach", StringComparison.OrdinalIgnoreCase)),

                FullDoors = string.Equals(name, "Rig", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(name, "Villa", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(name, "LittleRocketMan", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(name, "Farmstead", StringComparison.OrdinalIgnoreCase),
                SkipInteriorDoors = string.Equals(name, "Farmstead", StringComparison.OrdinalIgnoreCase),
                OutdoorDoorsAreGates = string.Equals(name, "Farmstead", StringComparison.OrdinalIgnoreCase),

                DoorSteelTint = string.Equals(name, "Rig", StringComparison.OrdinalIgnoreCase),

                PoolWater = string.Equals(name, "Villa", StringComparison.OrdinalIgnoreCase),

                WantsGroundSkirt = !(string.Equals(name, "Rig", StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(name, "Backroad", StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(name, "Villa", StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(name, "LittleRocketMan", StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(name, "Trenches", StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(name, "Killhouse", StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(name, "VirginiaBeach", StringComparison.OrdinalIgnoreCase)),

                MinStepOffset = string.Equals(name, "Rig", StringComparison.OrdinalIgnoreCase)
                                ? 0.36f
                              : string.Equals(name, "SmallTown", StringComparison.OrdinalIgnoreCase)
                                ? 0.35f
                              : string.Equals(name, "Villa", StringComparison.OrdinalIgnoreCase)
                                ? 0.45f : 0f,

                LightIntensityScale = string.Equals(name, "Rig", StringComparison.OrdinalIgnoreCase)
                                      ? 6f : 1f,
                StrictGlass = string.Equals(name, "Rig", StringComparison.OrdinalIgnoreCase),

                DonorMaterials = string.Equals(name, "Rig", StringComparison.OrdinalIgnoreCase),

                OpenTreadStairs = string.Equals(name, "Rig", StringComparison.OrdinalIgnoreCase),

                PlateSlabs = string.Equals(name, "Rig", StringComparison.OrdinalIgnoreCase),
                HeliInfil = string.Equals(name, "Rig", StringComparison.OrdinalIgnoreCase),
                OceanWater = string.Equals(name, "Rig", StringComparison.OrdinalIgnoreCase),
                SeaLevelY = string.Equals(name, "Rig", StringComparison.OrdinalIgnoreCase) ? 3.7f : 0f,
                SeaSky = string.Equals(name, "Rig", StringComparison.OrdinalIgnoreCase),
                SkyCubeSize = string.Equals(name, "Rig", StringComparison.OrdinalIgnoreCase) ? 1024 : 0,
                SunShadowResolution = string.Equals(name, "Rig", StringComparison.OrdinalIgnoreCase) ? 4096 : 0,
                SunAngularDiameterDeg = string.Equals(name, "Rig", StringComparison.OrdinalIgnoreCase) ? 1.2f : 0f,

                MaxLights = string.Equals(name, "Rig", StringComparison.OrdinalIgnoreCase)
                            ? 340 : 240,
                DetailTile = string.Equals(name, "Rig", StringComparison.OrdinalIgnoreCase)
                             ? 3f : 12f,

                TreeTrunkColliders = string.Equals(name, "Creek", StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(name, "SmallTown", StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(name, "VirginiaBeach", StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(name, "Killhouse", StringComparison.OrdinalIgnoreCase),

                FoliageShadows = string.Equals(name, "Creek", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(name, "Trenches", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(name, "SmallTown", StringComparison.OrdinalIgnoreCase),

                GroundAlbedo = string.Equals(name, "Backroad", StringComparison.OrdinalIgnoreCase)
                               ? 0.10f
                             : string.Equals(name, "Villa", StringComparison.OrdinalIgnoreCase)
                               ? 0.08f

                             : string.Equals(name, "SmallTown", StringComparison.OrdinalIgnoreCase)
                               ? 0.15f
                             : 0f,

                DetailNormal = 0f,

                VolumetricRays = string.Equals(name, "Backroad", StringComparison.OrdinalIgnoreCase),

                SurfaceDetail = !(string.Equals(name, "SmallTown", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(name, "Backroad", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(name, "Villa", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(name, "LittleRocketMan", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(name, "Trenches", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(name, "Killhouse", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(name, "VirginiaBeach", StringComparison.OrdinalIgnoreCase)),

                FoliageCullM = string.Equals(name, "Backroad", StringComparison.OrdinalIgnoreCase)
                               ? 400f
                             : string.Equals(name, "Villa", StringComparison.OrdinalIgnoreCase)
                               ? 200f
                             : string.Equals(name, "Creek", StringComparison.OrdinalIgnoreCase)
                               ? 90f
                             : string.Equals(name, "SmallTown", StringComparison.OrdinalIgnoreCase)

                               ? 180f : 0f,

                CanopyFactor = string.Equals(name, "Creek", StringComparison.OrdinalIgnoreCase)
                               ? 0.18f
                             : string.Equals(name, "Backroad", StringComparison.OrdinalIgnoreCase)
                               ? 0.80f : 1f,
                FoliageMaxPerMesh = string.Equals(name, "Creek", StringComparison.OrdinalIgnoreCase)
                                    ? 50000 : 0,

                FoliageBackfaceFlip = string.Equals(name, "Backroad", StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(name, "Trenches", StringComparison.OrdinalIgnoreCase),

                HostGrassOff = string.Equals(name, "Farmstead", StringComparison.OrdinalIgnoreCase),

                DropMeshWords = string.Equals(name, "Farmstead", StringComparison.OrdinalIgnoreCase)
                                ? new string[] { "_Tree_", "SM_tree", "Small_tree",
                                                 "Poplar", "ArecaPalm", "_Frond",

                                                 "Hickory" }

                                : string.Equals(name, "SmallTown", StringComparison.OrdinalIgnoreCase)
                                ? new string[] { "SM_Training_Door_01$",
                                                 "SM_Training_Door_01_Handle_01$" }

                                : string.Equals(name, "Rig", StringComparison.OrdinalIgnoreCase)
                                ? new string[] { "SM_Decal_Replacer_Plane" }
                                : null,

                DaySunElevationDeg = string.Equals(name, "Backroad", StringComparison.OrdinalIgnoreCase)
                                     ? 45f
                                   : string.Equals(name, "Villa", StringComparison.OrdinalIgnoreCase)
                                     ? 55f

                                   : string.Equals(name, "SmallTown", StringComparison.OrdinalIgnoreCase)
                                     ? 38f : 0f,
                FullResTextures = string.Equals(name, "Backroad", StringComparison.OrdinalIgnoreCase),

                FoliageMipBias = string.Equals(name, "Backroad", StringComparison.OrdinalIgnoreCase)
                                 ? 2.0f
                               : string.Equals(name, "Villa", StringComparison.OrdinalIgnoreCase)
                                 ? 2.0f

                               : string.Equals(name, "Trenches", StringComparison.OrdinalIgnoreCase)
                                 ? 0.5f

                               : string.Equals(name, "SmallTown", StringComparison.OrdinalIgnoreCase)
                                 ? 1.0f : 0f,

                GroundAlbedoScaleOverride = string.Equals(name, "Backroad", StringComparison.OrdinalIgnoreCase)
                                            ? 1.0f

                                          : string.Equals(name, "SmallTown", StringComparison.OrdinalIgnoreCase)
                                            ? 1.0f : 0f,

                ShadowDistanceM = string.Equals(name, "Backroad", StringComparison.OrdinalIgnoreCase)
                                  ? 160f : 0f,

                FoliageNoSpecular = string.Equals(name, "Backroad", StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(name, "Villa", StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(name, "SmallTown", StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(name, "Trenches", StringComparison.OrdinalIgnoreCase),

                SmoothnessCeilingOverride = string.Equals(name, "Trenches", StringComparison.OrdinalIgnoreCase)
                                            ? 0.34f : 0f,

                DefaultHostLighting = string.Equals(name, "Trenches", StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(name, "SmallTown", StringComparison.OrdinalIgnoreCase),

                DoorYawOffset = string.Equals(name, "LittleRocketMan", StringComparison.OrdinalIgnoreCase)
                                ? 0f : 270f,

                WanderDistance = string.Equals(name, "Killhouse", StringComparison.OrdinalIgnoreCase) ? 3f
                               : string.Equals(name, "VirginiaBeach", StringComparison.OrdinalIgnoreCase) ? 5f
                               : string.Equals(name, "LittleRocketMan", StringComparison.OrdinalIgnoreCase) ? 6f
                               : 0f,

                DayAutoExposure = string.Equals(name, "Villa", StringComparison.OrdinalIgnoreCase),
                CinematicDay = string.Equals(name, "SmallTown", StringComparison.OrdinalIgnoreCase),

                DaySunKelvin = string.Equals(name, "SmallTown", StringComparison.OrdinalIgnoreCase)
                               ? 5400f : 0f,

                HostShadowDimmerOverride = string.Equals(name, "SmallTown", StringComparison.OrdinalIgnoreCase)
                                           ? 0.88f : 0f,

                NormalScaleOverride = string.Equals(name, "Backroad", StringComparison.OrdinalIgnoreCase)
                                      ? 1.0f

                                    : string.Equals(name, "SmallTown", StringComparison.OrdinalIgnoreCase)
                                      ? 1.0f : 0f,

                MatteGroundExtra = string.Equals(name, "Backroad", StringComparison.OrdinalIgnoreCase),

                UncompressedGround = string.Equals(name, "Backroad", StringComparison.OrdinalIgnoreCase),

                HostLevelIsMap = string.Equals(name, "LittleRocketMan", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(name, "Trenches", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(name, "Killhouse", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(name, "VirginiaBeach", StringComparison.OrdinalIgnoreCase),
                AdditiveSceneName = string.Equals(name, "LittleRocketMan", StringComparison.OrdinalIgnoreCase)
                                    ? "Beach_Compound"
                                  : string.Equals(name, "Trenches", StringComparison.OrdinalIgnoreCase)
                                    ? "TrenchMap"
                                  : string.Equals(name, "VirginiaBeach", StringComparison.OrdinalIgnoreCase)
                                    ? "PVP map"
                                  : string.Equals(name, "Killhouse", StringComparison.OrdinalIgnoreCase)
                                    ? "KillhousePVP Day" : null,

                AdditiveKillNames = string.Equals(name, "VirginiaBeach", StringComparison.OrdinalIgnoreCase)
                                    ? new[] { "Fence_straight", "Fence_corner",
                                              "BarrierWallBig", "BarrierWallBigCorner",
                                              "BarrierWallBig_Woodland",
                                              "BarrierWallBigCorner_Woodland",
                                              "BarGate" }
                                    : null,
            };
        }

        internal static Entry Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Entry e;
            return _byName.TryGetValue(name.Trim(), out e) ? e : null;
        }

        internal static Entry Resolve(string stamped)
        {
            if (string.IsNullOrEmpty(stamped)) return null;
            var s = stamped.Trim();
            int at = s.IndexOf('@');
            if (at > 0) s = s.Substring(0, at);
            var hit = Get(s);
            if (hit != null) return hit;

            foreach (var e in _order)
                if (s.IndexOf(e.Name, StringComparison.OrdinalIgnoreCase) >= 0) return e;
            return null;
        }

        internal static bool Select(string name)
        {
            var e = Resolve(name);
            if (e == null)
            {
                _log?.Warning("[mapid] no map registered as '" + name + "'. Known: "
                            + string.Join(", ", Names()) + ". Add it to MapNames.");
                return false;
            }
            if (!e.Installed)
            {
                _log?.Error("[mapid] '" + e.Name + "' has no manifest.bin at " + e.BundleDir
                          + ". The map data was never installed on this machine, so there "
                          + "is nothing to build. In co-op this machine will be the only "
                          + "one without the map.");

                try { OdgConsole.MissingMap = e.Name; } catch { }
                return false;
            }
            if (Current != null && !string.Equals(Current.Name, e.Name, StringComparison.OrdinalIgnoreCase))
                _log?.Msg("[mapid] switching from '" + Current.Name + "' to '" + e.Name + "'");
            Current = e;
            return true;
        }

        internal static string InFolder(string sceneName, string file)
        {
            try
            {
                var e = Get(sceneName);
                if (e == null || string.IsNullOrEmpty(e.BundleDir)) return null;
                var p = Path.Combine(e.BundleDir, file);
                return File.Exists(p) ? p : null;
            }
            catch { return null; }
        }

        internal static string[] Names()
        {
            var a = new string[_order.Count];
            for (int i = 0; i < _order.Count; i++) a[i] = _order[i].Name;
            return a;
        }

        internal static IReadOnlyList<Entry> All { get { return _order; } }

        internal static string SceneName { get { return Current != null ? Current.SceneName : "Farmstead"; } }
        internal static string RootName  { get { return Current != null ? Current.RootName  : "MAP_Farmstead"; } }
        internal static string BundleDir { get { return Current != null ? Current.BundleDir : @"D:\GB_work\bundle\Farmstead"; } }
        internal static bool OwnSky { get { return Current != null && Current.OwnSky; } }
        internal static bool HostGrassOff { get { return Current != null && Current.HostGrassOff; } }
        internal static string[] DropMeshWords { get { return Current != null ? Current.DropMeshWords : null; } }
        internal static bool FullDoors { get { return Current != null && Current.FullDoors; } }
        internal static bool SkipInteriorDoors { get { return Current != null && Current.SkipInteriorDoors; } }
        internal static bool OutdoorDoorsAreGates { get { return Current != null && Current.OutdoorDoorsAreGates; } }
        internal static bool DoorSteelTint { get { return Current != null && Current.DoorSteelTint; } }
        internal static bool PoolWater { get { return Current != null && Current.PoolWater; } }
        internal static bool OpenTreadStairs { get { return Current != null && Current.OpenTreadStairs; } }
        internal static bool PlateSlabs { get { return Current != null && Current.PlateSlabs; } }
        internal static bool HeliInfil { get { return Current != null && Current.HeliInfil; } }
        internal static bool OceanWater { get { return Current != null && Current.OceanWater; } }
        internal static float SeaLevelY { get { return Current != null ? Current.SeaLevelY : 0f; } }
        internal static bool SeaSky { get { return Current != null && Current.SeaSky; } }
        internal static int SkyCubeSize { get { return Current != null ? Current.SkyCubeSize : 0; } }
        internal static int SunShadowResolution { get { return Current != null ? Current.SunShadowResolution : 0; } }
        internal static float SunAngularDiameterDeg { get { return Current != null ? Current.SunAngularDiameterDeg : 0f; } }

        internal static bool WantsGroundSkirt
        {
            get { return Current == null || Current.WantsGroundSkirt; }
        }
        internal static float MinStepOffset
        {
            get { return Current != null ? Current.MinStepOffset : 0f; }
        }
        internal static bool StrictGlass { get { return Current != null && Current.StrictGlass; } }
        internal static bool DonorMaterials { get { return Current != null && Current.DonorMaterials; } }
        internal static bool TreeTrunkColliders
        {
            get { return Current != null && Current.TreeTrunkColliders; }
        }
        internal static bool FoliageShadows
        {
            get { return Current != null && Current.FoliageShadows; }
        }

        internal static float GroundAlbedo
        {
            get { return Current != null ? Current.GroundAlbedo : 0f; }
        }

        internal static float DetailNormal
        {
            get { return Current != null ? Current.DetailNormal : 0f; }
        }
        internal static bool VolumetricRays
        {
            get { return Current != null && Current.VolumetricRays; }
        }

        internal static bool SurfaceDetail
        {
            get { return Current == null || Current.SurfaceDetail; }
        }
        internal static float FoliageCullM
        {
            get { return Current != null ? Current.FoliageCullM : 0f; }
        }
        internal static float CanopyFactor
        {
            get
            {
                return Current != null && Current.CanopyFactor > 0f
                     ? Current.CanopyFactor : 1f;
            }
        }
        internal static int FoliageMaxPerMesh
        {
            get { return Current != null ? Current.FoliageMaxPerMesh : 0; }
        }
        internal static float DetailTile
        {
            get { return Current != null && Current.DetailTile > 0f ? Current.DetailTile : 12f; }
        }
        internal static bool FoliageBackfaceFlip
        {
            get { return Current != null && Current.FoliageBackfaceFlip; }
        }
        internal static float DaySunElevationDeg
        {
            get { return Current != null ? Current.DaySunElevationDeg : 0f; }
        }
        internal static bool FullResTextures
        {
            get { return Current != null && Current.FullResTextures; }
        }
        internal static float NormalScaleOverride
        {
            get { return Current != null ? Current.NormalScaleOverride : 0f; }
        }
        internal static bool MatteGroundExtra
        {
            get { return Current != null && Current.MatteGroundExtra; }
        }
        internal static bool UncompressedGround
        {
            get { return Current != null && Current.UncompressedGround; }
        }
        internal static bool CinematicDay
        {
            get { return Current != null && Current.CinematicDay; }
        }
        internal static float DaySunKelvin
        {
            get { return Current != null ? Current.DaySunKelvin : 0f; }
        }
        internal static float HostShadowDimmerOverride
        {
            get { return Current != null ? Current.HostShadowDimmerOverride : 0f; }
        }
        internal static bool HostLevelIsMap
        {
            get { return Current != null && Current.HostLevelIsMap; }
        }
        internal static string AdditiveSceneName
        {
            get { return Current != null ? Current.AdditiveSceneName : null; }
        }
        internal static string[] AdditiveKillNames
        {
            get { return Current != null ? Current.AdditiveKillNames : null; }
        }

        static readonly string[] AdditiveScenes =
            { "Beach_Compound", "TrenchMap", "PVP map", "KillhousePVP Day" };

        internal static bool IsAnyAdditiveSceneSet(string bakingSetName)
        {
            if (string.IsNullOrEmpty(bakingSetName)) return false;
            for (int i = 0; i < AdditiveScenes.Length; i++)
                if (bakingSetName.IndexOf(AdditiveScenes[i],
                        StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }
        internal static float FoliageMipBias
        {
            get { return Current != null ? Current.FoliageMipBias : 0f; }
        }
        internal static float GroundAlbedoScaleOverride
        {
            get { return Current != null ? Current.GroundAlbedoScaleOverride : 0f; }
        }
        internal static float ShadowDistanceM
        {
            get { return Current != null ? Current.ShadowDistanceM : 0f; }
        }
        internal static bool DayAutoExposure
        {
            get { return Current != null && Current.DayAutoExposure; }
        }

        internal static float SmoothnessCeilingOverride
        {
            get { return Current != null ? Current.SmoothnessCeilingOverride : 0f; }
        }

        internal static bool DefaultHostLighting
        {
            get { return Current != null && Current.DefaultHostLighting; }
        }

        internal static float DoorYawOffset
        {
            get { return Current != null ? Current.DoorYawOffset : 270f; }
        }

        internal static float WanderDistance
        {
            get { return Current != null ? Current.WanderDistance : 0f; }
        }

        internal static bool FoliageNoSpecular
        {
            get { return Current != null && Current.FoliageNoSpecular; }
        }
        internal static int MaxLights
        {
            get { return Current != null && Current.MaxLights > 0 ? Current.MaxLights : 240; }
        }
        internal static float LightIntensityScale
        {
            get
            {
                return Current != null && Current.LightIntensityScale > 0f
                     ? Current.LightIntensityScale : 1f;
            }
        }

    }
}
