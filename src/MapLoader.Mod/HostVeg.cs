// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace MapLoader
{

    public static class HostVeg
    {

        sealed class TerrainState
        {
            public Terrain T;
            public bool  Enabled;
            public bool  DrawTrees;
            public bool  DrawHeightmap;
            public bool  CollectDetail;
            public float TreeDistance;
            public float TreeBillboardDistance;
            public int   TreeMaxFullLod;
            public float DetailDistance;
            public float DetailDensity;

            public Behaviour Brg;
            public bool BrgWas;

            public Il2CppInterop.Runtime.InteropTypes.Arrays
                   .Il2CppStructArray<TreeInstance> TreesWas;
        }

        static readonly List<TerrainState> _terrains = new List<TerrainState>();
        static readonly List<GameObject>   _proxyParents = new List<GameObject>();
        static readonly List<Behaviour>    _flares = new List<Behaviour>();
        static readonly List<Behaviour>    _fog    = new List<Behaviour>();
        static readonly List<Behaviour>    _decals = new List<Behaviour>();

        static bool  _brgDistancesSaved;
        static float _brgTreeDistanceWas, _brgDetailDistanceWas;

        static float _pristineTree, _pristineDetail;

        static void NotePristine()
        {
            try
            {
                float t = Il2CppBRGInstancedRenderer.TerrainBRGRegisterer.TreeRenderDistance;
                float d = Il2CppBRGInstancedRenderer.TerrainBRGRegisterer.DetailRenderDistance;
                if (t > 0f) _pristineTree   = t;
                if (d > 0f) _pristineDetail = d;
            }
            catch { }
        }

        static float SaneBrg(float live, float pristine)
        {
            if (live > 0f) return live;
            if (pristine > 0f) return pristine;
            return float.MaxValue;
        }

        static int ReleaseProcessWide()
        {
            if (_brgDistancesSaved)
            {
                try
                {
                    Il2CppBRGInstancedRenderer.TerrainBRGRegisterer.TreeRenderDistance =
                        SaneBrg(_brgTreeDistanceWas, _pristineTree);
                    Il2CppBRGInstancedRenderer.TerrainBRGRegisterer.DetailRenderDistance =
                        SaneBrg(_brgDetailDistanceWas, _pristineDetail);
                }
                catch { }
                _brgDistancesSaved = false;
            }
            int p = 0;
            for (int i = 0; i < _proxyParents.Count; i++)
            {
                try { if (_proxyParents[i] != null) { _proxyParents[i].SetActive(true); p++; } }
                catch { }
            }
            _proxyParents.Clear();
            return p;
        }

        static void AssertCleanBaseline(MelonLogger.Instance log)
        {
            try
            {
                float t = Il2CppBRGInstancedRenderer.TerrainBRGRegisterer.TreeRenderDistance;
                float d = Il2CppBRGInstancedRenderer.TerrainBRGRegisterer.DetailRenderDistance;
                if (t > 0f && d > 0f) { NotePristine(); return; }
                float wantT = SaneBrg(t, _pristineTree);
                float wantD = SaneBrg(d, _pristineDetail);
                if (t <= 0f) Il2CppBRGInstancedRenderer.TerrainBRGRegisterer.TreeRenderDistance   = wantT;
                if (d <= 0f) Il2CppBRGInstancedRenderer.TerrainBRGRegisterer.DetailRenderDistance = wantD;
                log?.Warning("[hostveg] a previous mission left a BRG streaming distance at 0 "
                           + "(tree=" + t.ToString("F0") + " detail=" + d.ToString("F0")
                           + "). Put back to tree=" + wantT.ToString("F0")
                           + " detail=" + wantD.ToString("F0") + " before this map decides "
                           + "anything. A zero here is never authored: it is a teardown that "
                           + "did not run, and on a map that borrows its host's treeline it "
                           + "removes every tree.");
            }
            catch { }
        }

        static MelonLogger.Instance _log;
        static bool  _armed;
        static bool  _grassOnly;
        static float _nextWatch;
        static int   _reasserts;

        static MelonPreferences_Entry<bool> _purge, _killFlares, _killFog, _killDecals;

        public static void Init(MelonLogger.Instance log)
        {
            _log = log;
            try
            {
                var cfg = MelonPreferences.GetCategory("MapLoader")
                       ?? MelonPreferences.CreateCategory("MapLoader");

                _purge = cfg.GetEntry<bool>("HostVegPurge")
                      ?? cfg.CreateEntry("HostVegPurge", true,
                           description: "Remove the host level's terrain trees and grass while our map "
                                      + "stands. On by default: it is a no-op on any host that has no "
                                      + "Terrain, which includes Farmstead's. Everything is recorded "
                                      + "and put back on restore.");

                _killFlares = cfg.GetEntry<bool>("HostVegKillLensFlares")
                           ?? cfg.CreateEntry("HostVegKillLensFlares", false,
                                description: "Also switch off the host's LensFlareComponentSRP. HDRP draws "
                                           + "these from its own flare list, not from a Renderer, so the "
                                           + "shell hide cannot reach them. OFF by default because "
                                           + "Farmstead's host has 23 of them and this would change it.");

                _killFog = cfg.GetEntry<bool>("HostVegKillLocalFog")
                        ?? cfg.CreateEntry("HostVegKillLocalFog", false,
                             description: "Also switch off the host's LocalVolumetricFog volumes. Not "
                                        + "Renderers either. OFF by default for the same reason.");

                _killDecals = cfg.GetEntry<bool>("HostVegKillDecals")
                           ?? cfg.CreateEntry("HostVegKillDecals", false,
                                description: "Also switch off the host's HDRP DecalProjectors, which "
                                           + "project the host's grime onto our geometry. OFF by default: "
                                           + "Farmstead's host has 61 of them.");
            }
            catch { }
        }

        public static void Reset()
        {

            ReleaseProcessWide();
            _terrains.Clear();
            _flares.Clear();
            _fog.Clear();
            _decals.Clear();
            _armed = false;
            _grassOnly = false;
            _reasserts = 0;
            _nextWatch = 0f;
        }

        public static int Suppress(Transform ourRoot, MelonLogger.Instance log)
        {

            AssertCleanBaseline(log);
            if (_purge != null && !_purge.Value) return 0;

            if (!MapIdentity.OwnSky && !MapIdentity.HostLevelIsMap)
            {

                if (_armed)
                {
                    log?.Msg("  [hostveg] a previous mission left this host's vegetation "
                           + "switched off. Putting it back before this map decides what "
                           + "it wants to keep.");
                    Restore(log);
                }

                return GrassOnly(ourRoot, log);
            }

            if (_grassOnly)
            {
                log?.Msg("  [hostveg] a previous mission had this host's GRASS off with its "
                       + "trees kept. This map wants both gone: undoing the half pass first.");
                Restore(log);
            }

            int n = 0, proxies = 0;

            try
            {
                var ts = Resources.FindObjectsOfTypeAll<Terrain>();
                if (ts != null)
                    for (int i = 0; i < ts.Length; i++)
                    {
                        var t = ts[i];
                        if (t == null || t.gameObject == null) continue;
                        if (!t.gameObject.scene.IsValid()) continue;
                        if (ourRoot != null && t.transform.IsChildOf(ourRoot)) continue;

                        if (ShellSwap.KeepSceneHandle != 0
                            && t.gameObject.scene.handle == ShellSwap.KeepSceneHandle)
                        {
                            try
                            {
                                var brg = BrgOf(t);
                                var td = t.terrainData;
                                log?.Msg("  [hostveg] SPARED the additive world's terrain '"
                                       + Name(t) + "' (scene=" + t.gameObject.scene.name
                                       + "): enabled=" + t.enabled
                                       + " drawTrees=" + t.drawTreesAndFoliage
                                       + " collectDetail=" + t.collectDetailPatches
                                       + " detailDist=" + t.detailObjectDistance.ToString("F0")
                                       + " detailDensity=" + t.detailObjectDensity.ToString("F2")
                                       + " treeDist=" + t.treeDistance.ToString("F0")
                                       + " brg=" + (brg == null ? "none"
                                                    : brg.enabled ? "enabled" : "DISABLED")
                                       + " detailPrototypes="
                                       + (td == null ? -1 : td.detailPrototypes.Length)
                                       + " treeInstances="
                                       + (td == null ? -1 : td.treeInstanceCount)
                                       + " layers="
                                       + (td == null ? -1 : td.terrainLayers.Length));
                            }
                            catch { }
                            continue;
                        }
                        if (Recorded(t)) continue;

                        var st = new TerrainState { T = t };
                        try { st.Enabled               = t.enabled; }                catch { }
                        try { st.DrawTrees             = t.drawTreesAndFoliage; }     catch { }
                        try { st.DrawHeightmap         = t.drawHeightmap; }           catch { }
                        try { st.CollectDetail         = t.collectDetailPatches; }    catch { }
                        try { st.TreeDistance          = t.treeDistance; }            catch { }
                        try { st.TreeBillboardDistance = t.treeBillboardDistance; }   catch { }
                        try { st.TreeMaxFullLod        = t.treeMaximumFullLODCount; } catch { }
                        try { st.DetailDistance        = t.detailObjectDistance; }    catch { }
                        try { st.DetailDensity         = t.detailObjectDensity; }     catch { }
                        try
                        {
                            st.Brg = BrgOf(t);
                            if (st.Brg != null) st.BrgWas = st.Brg.enabled;
                        }
                        catch { }
                        _terrains.Add(st);

                        Disarm(t);
                        n++;

                        log?.Msg("  [hostveg] disarmed host terrain '" + Name(t) + "' at "
                               + Pos(t) + ": drawTreesAndFoliage " + st.DrawTrees + "->False, "
                               + "collectDetailPatches " + st.CollectDetail + "->False, treeDistance "
                               + st.TreeDistance.ToString("F0") + "->0, detailObjectDistance "
                               + st.DetailDistance.ToString("F0") + "->0. "
                               + TreeCount(t));
                    }
            }
            catch (Exception e) { log?.Warning("[hostveg] terrain pass: " + e.Message); }

            try
            {
                NotePristine();
                if (!_brgDistancesSaved)
                {
                    _brgTreeDistanceWas   = SaneBrg(
                        Il2CppBRGInstancedRenderer.TerrainBRGRegisterer.TreeRenderDistance, _pristineTree);
                    _brgDetailDistanceWas = SaneBrg(
                        Il2CppBRGInstancedRenderer.TerrainBRGRegisterer.DetailRenderDistance, _pristineDetail);
                    _brgDistancesSaved = true;
                }
                Il2CppBRGInstancedRenderer.TerrainBRGRegisterer.TreeRenderDistance   = 0f;

                if (!MapIdentity.HostLevelIsMap)
                    Il2CppBRGInstancedRenderer.TerrainBRGRegisterer.DetailRenderDistance = 0f;
                if (n > 0)
                    log?.Msg("  [hostveg] BRG terrain streaming distances 0 (were tree "
                           + _brgTreeDistanceWas.ToString("F0") + " m, detail "
                           + _brgDetailDistanceWas.ToString("F0") + " m)");
            }
            catch (Exception e) { log?.Warning("[hostveg] BRG distances: " + e.Message); }

            proxies += TakeProxyParent(SafeProxy(() =>
                Il2Cpp.SpeedTreeProxyManager.treeProxyParent), "Speed Tree Proxy Manager", log);
            proxies += TakeProxyParent(SafeProxy(() =>
                Il2Cpp.InstancedTreeProxyManager.treeProxyParent), "Instanced Tree Proxy Manager", log);

            bool farFloor = MapIdentity.OwnSky && MapRuntime.MapFloorFarFromHost;
            int fl = 0, fg = 0, dc = 0;
            if ((_killFlares != null && _killFlares.Value) || farFloor)
                fl = TakeBehaviours<UnityEngine.Rendering.LensFlareComponentSRP>(ourRoot, _flares);
            if ((_killFog != null && _killFog.Value) || farFloor)
                fg = TakeBehaviours<UnityEngine.Rendering.HighDefinition.LocalVolumetricFog>(ourRoot, _fog);
            if (_killDecals != null && _killDecals.Value)
                dc = TakeBehaviours<UnityEngine.Rendering.HighDefinition.DecalProjector>(ourRoot, _decals);
            if (fl + fg + dc > 0)
                log?.Msg("  [hostveg] also switched off " + fl + " host lens flare(s), "
                       + fg + " local fog volume(s), " + dc + " decal projector(s)");

            if (n > 0 || proxies > 0)
            {
                _armed = true;
                log?.Msg("  [hostveg] " + n + " host terrain(s) disarmed, " + proxies
                       + " tree proxy parent(s) deactivated. Re-checked every 2 s, because "
                       + "TerrainBRGRegisterer.OnDisable sets drawTreesAndFoliage back to TRUE "
                       + "on its way out and something else may re-enable the terrain.");
            }
            return n;
        }

        static int GrassOnly(Transform ourRoot, MelonLogger.Instance log)
        {
            if (!MapIdentity.HostGrassOff) return 0;

            int n = 0;
            try
            {
                var ts = Resources.FindObjectsOfTypeAll<Terrain>();
                if (ts != null)
                    for (int i = 0; i < ts.Length; i++)
                    {
                        var t = ts[i];
                        if (t == null || t.gameObject == null) continue;
                        if (!t.gameObject.scene.IsValid()) continue;
                        if (ourRoot != null && t.transform.IsChildOf(ourRoot)) continue;
                        if (ShellSwap.KeepSceneHandle != 0
                            && t.gameObject.scene.handle == ShellSwap.KeepSceneHandle) continue;
                        if (Recorded(t)) continue;

                        var st = new TerrainState { T = t };
                        try { st.Enabled               = t.enabled; }                catch { }
                        try { st.DrawTrees             = t.drawTreesAndFoliage; }     catch { }
                        try { st.DrawHeightmap         = t.drawHeightmap; }           catch { }
                        try { st.CollectDetail         = t.collectDetailPatches; }    catch { }
                        try { st.TreeDistance          = t.treeDistance; }            catch { }
                        try { st.TreeBillboardDistance = t.treeBillboardDistance; }   catch { }
                        try { st.TreeMaxFullLod        = t.treeMaximumFullLODCount; } catch { }
                        try { st.DetailDistance        = t.detailObjectDistance; }    catch { }
                        try { st.DetailDensity         = t.detailObjectDensity; }     catch { }
                        try
                        {
                            st.Brg = BrgOf(t);
                            if (st.Brg != null) st.BrgWas = st.Brg.enabled;
                        }
                        catch { }
                        _terrains.Add(st);

                        DisarmGrass(t);
                        DropTrees(st, ourRoot, log);
                        n++;

                        log?.Msg("  [hostveg] host terrain '" + Name(t) + "' at " + Pos(t)
                               + ": GRASS off, TREES KEPT. collectDetailPatches "
                               + st.CollectDetail + "->False, detailObjectDistance "
                               + st.DetailDistance.ToString("F0") + "->0, detailObjectDensity "
                               + st.DetailDensity.ToString("F2") + "->0; drawTreesAndFoliage "
                               + "and treeDistance NOT touched. " + TreeCount(t));
                    }
            }
            catch (Exception e) { log?.Warning("[hostveg] grass-only terrain pass: " + e.Message); }

            if (n == 0) return 0;

            try
            {
                NotePristine();
                if (!_brgDistancesSaved)
                {
                    _brgTreeDistanceWas   = SaneBrg(
                        Il2CppBRGInstancedRenderer.TerrainBRGRegisterer.TreeRenderDistance, _pristineTree);
                    _brgDetailDistanceWas = SaneBrg(
                        Il2CppBRGInstancedRenderer.TerrainBRGRegisterer.DetailRenderDistance, _pristineDetail);
                    _brgDistancesSaved = true;
                }

                float wantTree = SaneBrg(_brgTreeDistanceWas, _pristineTree);
                Il2CppBRGInstancedRenderer.TerrainBRGRegisterer.TreeRenderDistance   = wantTree;
                Il2CppBRGInstancedRenderer.TerrainBRGRegisterer.DetailRenderDistance = 0f;
                log?.Msg("  [hostveg] BRG detail streaming distance 0 (was "
                       + _brgDetailDistanceWas.ToString("F0") + " m). Tree distance ASSERTED at "
                       + wantTree.ToString("F0") + " m -- one registerer draws both, and these "
                       + "two statics are the only thing that separates them.");
            }
            catch (Exception e) { log?.Warning("[hostveg] grass-only BRG distance: " + e.Message); }

            _armed = true;
            _grassOnly = true;
            log?.Msg("  [hostveg] " + n + " host terrain(s) had their GRASS switched off. The "
                   + "level's trees are untouched and are still this map's horizon; this map "
                   + "covers its own ground. Re-checked every 2 s.");
            return n;
        }

        static void DisarmGrass(Terrain t)
        {
            try { t.collectDetailPatches = false; } catch { }
            try { t.detailObjectDistance = 0f;    } catch { }
            try { t.detailObjectDensity  = 0f;    } catch { }
        }

        public static void Tick(bool mapBuilt)
        {
            if (!mapBuilt || !_armed) return;
            if (_purge != null && !_purge.Value) return;

            if (!MapIdentity.OwnSky && !MapIdentity.HostLevelIsMap && !_grassOnly)
                return;
            if (Time.time < _nextWatch) return;
            _nextWatch = Time.time + 2f;

            int drift = 0;
            for (int i = 0; i < _terrains.Count; i++)
            {
                var st = _terrains[i];
                if (st == null || st.T == null) continue;
                bool bad = false;
                try
                {
                    if (_grassOnly)
                    {

                        bad = st.T.collectDetailPatches
                           || st.T.detailObjectDistance > 0f
                           || st.T.detailObjectDensity  > 0f;
                    }
                    else
                    {
                        bad = st.T.enabled || st.T.drawTreesAndFoliage || st.T.collectDetailPatches;

                        if (!bad && st.Brg != null && st.Brg.enabled) bad = true;
                    }
                }
                catch { continue; }
                if (!bad) continue;
                if (_grassOnly) DisarmGrass(st.T); else Disarm(st.T);
                drift++;
            }

            if (_grassOnly && _brgDistancesSaved)
            {
                try
                {
                    if (Il2CppBRGInstancedRenderer.TerrainBRGRegisterer.DetailRenderDistance > 0f)
                    {
                        Il2CppBRGInstancedRenderer.TerrainBRGRegisterer.DetailRenderDistance = 0f;
                        drift++;
                    }
                    if (Il2CppBRGInstancedRenderer.TerrainBRGRegisterer.TreeRenderDistance <= 0f)
                    {
                        Il2CppBRGInstancedRenderer.TerrainBRGRegisterer.TreeRenderDistance =
                            SaneBrg(_brgTreeDistanceWas, _pristineTree);
                        drift++;
                    }
                }
                catch { }
            }

            if (drift == 0) return;

            _reasserts++;
            if (_reasserts == 1 || _reasserts % 30 == 0)
                _log?.Warning("[hostveg] " + drift + " host terrain(s) came back armed and were "
                            + "disarmed again (" + _reasserts + " time(s) so far). Something outside "
                            + "this class is re-enabling them.");
        }

        static Behaviour BrgOf(Terrain t)
        {
            try
            {
                var c = t.gameObject.GetComponent<
                    Il2CppBRGInstancedRenderer.TerrainBRGRegisterer>();
                return c as Behaviour;
            }
            catch { return null; }
        }

        static void Disarm(Terrain t)
        {

            try
            {
                var brg = BrgOf(t);
                if (brg != null && brg.enabled) brg.enabled = false;
            }
            catch { }
            try { t.drawTreesAndFoliage     = false; } catch { }
            try { t.collectDetailPatches    = false; } catch { }
            try { t.treeDistance            = 0f;    } catch { }
            try { t.treeBillboardDistance   = 0f;    } catch { }
            try { t.treeMaximumFullLODCount = 0;     } catch { }
            try { t.detailObjectDistance    = 0f;    } catch { }
            try { t.detailObjectDensity     = 0f;    } catch { }

            try { t.enabled = false; } catch { }
        }

        public static bool Armed { get { return _armed; } }

        public static void RestoreIfArmed(MelonLogger.Instance log)
        {
            if (!_armed) return;
            log?.Warning("[hostveg] still armed with no map standing -- restoring. "
                       + "A teardown path missed HostVeg.Restore; the proxy manager "
                       + "and the BRG distances are process-global, so leaving them "
                       + "purged would strip every stock map's foliage.");
            Restore(log);
        }

        public static void Restore(MelonLogger.Instance log)
        {
            int n = 0;
            for (int i = 0; i < _terrains.Count; i++)
            {
                var st = _terrains[i];
                if (st == null || st.T == null) continue;

                try
                {
                    if (st.TreesWas != null && st.T.terrainData != null)
                    {
                        st.T.terrainData.treeInstances = st.TreesWas;
                        st.TreesWas = null;
                    }
                }
                catch (Exception te) { log?.Warning("[hostveg] tree restore: " + te.Message); }
                try { if (st.Brg != null) st.Brg.enabled = st.BrgWas; }          catch { }
                try { st.T.drawTreesAndFoliage     = st.DrawTrees; }             catch { }
                try { st.T.drawHeightmap           = st.DrawHeightmap; }         catch { }
                try { st.T.collectDetailPatches    = st.CollectDetail; }         catch { }
                try { st.T.treeDistance            = st.TreeDistance; }          catch { }
                try { st.T.treeBillboardDistance   = st.TreeBillboardDistance; } catch { }
                try { st.T.treeMaximumFullLODCount = st.TreeMaxFullLod; }        catch { }
                try { st.T.detailObjectDistance    = st.DetailDistance; }        catch { }
                try { st.T.detailObjectDensity     = st.DetailDensity; }         catch { }
                try { st.T.enabled                 = st.Enabled; }               catch { }
                n++;
            }
            _terrains.Clear();

            int p = ReleaseProcessWide();

            int b = PutBack(_flares) + PutBack(_fog) + PutBack(_decals);

            _armed = false;
            _grassOnly = false;
            _reasserts = 0;
            if (n + p + b > 0)
                log?.Msg("  [hostveg] restored " + n + " host terrain(s), " + p
                       + " proxy parent(s), " + b + " other host component(s).");
        }

        public static void Report()
        {
            var log = _log;
            if (log == null) return;
            log.Msg("[hostveg] ---- host vegetation inventory ----");
            log.Msg("[hostveg] armed=" + _armed + "  grassOnly=" + _grassOnly
                  + "  terrains recorded=" + _terrains.Count
                  + "  proxy parents=" + _proxyParents.Count + "  re-asserts=" + _reasserts);
            try
            {
                var ts = Resources.FindObjectsOfTypeAll<Terrain>();
                log.Msg("[hostveg] " + (ts == null ? 0 : ts.Length) + " Terrain component(s) in memory");
                for (int i = 0; ts != null && i < ts.Length && i < 8; i++)
                {
                    var t = ts[i];
                    if (t == null) continue;
                    string sc = "?";
                    try { sc = t.gameObject.scene.IsValid() ? t.gameObject.scene.name : "<prefab>"; } catch { }
                    log.Msg("[hostveg]   '" + Name(t) + "' scene=" + sc + " pos=" + Pos(t)
                          + " enabled=" + Safe(() => t.enabled.ToString())
                          + " drawTrees=" + Safe(() => t.drawTreesAndFoliage.ToString())
                          + " collectDetail=" + Safe(() => t.collectDetailPatches.ToString())
                          + " treeDist=" + Safe(() => t.treeDistance.ToString("F0"))
                          + " detailDist=" + Safe(() => t.detailObjectDistance.ToString("F0"))
                          + " detailDensity=" + Safe(() => t.detailObjectDensity.ToString("F2")));
                    log.Msg("[hostveg]      " + TreeCount(t));
                }
            }
            catch (Exception e) { log.Warning("[hostveg] terrain inventory: " + e.Message); }

            try
            {
                var regs = Resources.FindObjectsOfTypeAll<Il2CppBRGInstancedRenderer.BRGRegisterer>();
                int on = 0;
                var kinds = new Dictionary<string, int>();
                for (int i = 0; regs != null && i < regs.Length; i++)
                {
                    var r = regs[i];
                    if (r == null || r.gameObject == null) continue;
                    if (!r.gameObject.scene.IsValid()) continue;
                    string k = "?";
                    try { k = r.GetIl2CppType().Name; } catch { }
                    int had; kinds.TryGetValue(k, out had); kinds[k] = had + 1;
                    try { if (r.enabled) on++; } catch { }
                }
                var sb = new System.Text.StringBuilder();
                foreach (var kv in kinds) sb.Append(' ').Append(kv.Key).Append('=').Append(kv.Value);
                log.Msg("[hostveg] BRG registerers in scenes:" + (sb.Length == 0 ? " none" : sb.ToString())
                      + "   still enabled=" + on);
                log.Msg("[hostveg] BRG baseline: pristine tree=" + _pristineTree.ToString("F0")
                      + " detail=" + _pristineDetail.ToString("F0") + "   saved=" + _brgDistancesSaved
                      + " tree=" + _brgTreeDistanceWas.ToString("F0")
                      + " detail=" + _brgDetailDistanceWas.ToString("F0"));
                log.Msg("[hostveg] BRG streaming distances: tree="
                      + Safe(() => Il2CppBRGInstancedRenderer.TerrainBRGRegisterer.TreeRenderDistance.ToString("F0"))
                      + " detail="
                      + Safe(() => Il2CppBRGInstancedRenderer.TerrainBRGRegisterer.DetailRenderDistance.ToString("F0")));
            }
            catch (Exception e) { log.Warning("[hostveg] BRG inventory: " + e.Message); }

            log.Msg("[hostveg]   SpeedTreeProxyManager.treeProxyParent = "
                  + Describe(SafeProxy(() => Il2Cpp.SpeedTreeProxyManager.treeProxyParent)));
            log.Msg("[hostveg]   InstancedTreeProxyManager.treeProxyParent = "
                  + Describe(SafeProxy(() => Il2Cpp.InstancedTreeProxyManager.treeProxyParent)));

            log.Msg("[hostveg]   lens flares=" + CountOf<UnityEngine.Rendering.LensFlareComponentSRP>()
                  + "  local fog=" + CountOf<UnityEngine.Rendering.HighDefinition.LocalVolumetricFog>()
                  + "  decal projectors=" + CountOf<UnityEngine.Rendering.HighDefinition.DecalProjector>()
                  + "   (each behind its own preference, all off by default)");
            log.Msg("[hostveg] ---- end inventory ----");
        }

        static bool Recorded(Terrain t)
        {
            for (int i = 0; i < _terrains.Count; i++)
                if (_terrains[i] != null && _terrains[i].T == t) return true;
            return false;
        }

        static int TakeProxyParent(GameObject go, string what, MelonLogger.Instance log)
        {
            if (go == null) return 0;
            try
            {
                if (!go.activeSelf) return 0;
                go.SetActive(false);
                _proxyParents.Add(go);
                int r = 0;
                try { var rr = go.GetComponentsInChildren<Renderer>(true); r = rr == null ? 0 : rr.Length; }
                catch { }
                log?.Msg("  [hostveg] deactivated '" + what + "' (" + r + " pooled renderer(s)). It lives "
                       + "in DontDestroyOnLoad, which a scene walk never visits, so the shell hide "
                       + "could not have reached it.");
                return 1;
            }
            catch { return 0; }
        }

        internal static int KillHostFlares(Transform ourRoot, MelonLogger.Instance log)
        {
            try
            {
                if (_flaresOff == null)
                {
                    var cfg = MelonPreferences.GetCategory("MapLoader")
                           ?? MelonPreferences.CreateCategory("MapLoader");
                    _flaresOff = cfg.GetEntry<bool>("HostFlaresOff")
                        ?? cfg.CreateEntry("HostFlaresOff", true,
                             description: "Switch off the shipped level's lens flares while our "
                                        + "map is standing. They are screen-space, they are drawn "
                                        + "by their own component rather than by a light, so "
                                        + "hiding the level does not hide them, and at a tuned "
                                        + "night exposure they read as rings and rays with no "
                                        + "source. Restored when the map is torn down.");
                }
                if (!_flaresOff.Value) return 0;

                int n = TakeBehaviours<UnityEngine.Rendering.LensFlareComponentSRP>(ourRoot, _flares);
                if (n > 0)
                {
                    _armed = true;
                    log?.Msg("  [hostveg] switched off " + n + " host lens flare(s). These are the "
                           + "rings and radial rays: screen-space, drawn by their own component, "
                           + "so switching off the host's lights never touched them.");
                }
                else log?.Msg("  [hostveg] no host lens flares on this level.");
                return n;
            }
            catch (Exception e) { log?.Warning("[hostveg] lens flares: " + e.Message); return 0; }
        }

        static MelonPreferences_Entry<bool> _flaresOff;

        static int TakeBehaviours<T>(Transform ourRoot, List<Behaviour> into)
            where T : Behaviour
        {
            int n = 0;
            try
            {
                var all = Resources.FindObjectsOfTypeAll<T>();
                if (all == null) return 0;
                for (int i = 0; i < all.Length; i++)
                {
                    var b = all[i];
                    if (b == null || b.gameObject == null) continue;
                    if (!b.gameObject.scene.IsValid()) continue;
                    if (ourRoot != null && b.transform.IsChildOf(ourRoot)) continue;
                    if (!b.enabled) continue;
                    b.enabled = false;
                    into.Add(b);
                    n++;
                }
            }
            catch { }
            return n;
        }

        static int PutBack(List<Behaviour> list)
        {
            int n = 0;
            for (int i = 0; i < list.Count; i++)
            {
                try { if (list[i] != null) { list[i].enabled = true; n++; } }
                catch { }
            }
            list.Clear();
            return n;
        }

        static int CountOf<T>() where T : UnityEngine.Object
        {
            try
            {
                var a = Resources.FindObjectsOfTypeAll<T>();
                return a == null ? 0 : a.Length;
            }
            catch { return -1; }
        }

        static GameObject SafeProxy(Func<GameObject> f)
        {
            try { return f(); } catch { return null; }
        }

        static string Describe(GameObject go)
        {
            if (go == null) return "<null>";
            try
            {
                var rr = go.GetComponentsInChildren<Renderer>(true);
                return "'" + go.name + "' active=" + go.activeSelf
                     + " renderers=" + (rr == null ? 0 : rr.Length);
            }
            catch { return "<unreadable>"; }
        }

        public static bool DropEnabled = true;

        static int DropTrees(TerrainState st, Transform ourRoot, MelonLogger.Instance log)
        {
            if (!DropEnabled || st == null || st.T == null) return 0;
            try
            {
                var td = st.T.terrainData;
                if (td == null) return 0;

                Bounds ours;
                bool haveSkirt = false;
                try { haveSkirt = GroundSkirt.Exists; } catch { }
                if (haveSkirt) { ours = GroundSkirt.OuterEdge; }
                else
                {
                    ours = MapRuntime.Bounds();
                    ours.Expand(new Vector3(60f, 0f, 60f));
                }
                if (ours.size.x < 1f || ours.size.z < 1f) return 0;

                var arr = td.treeInstances;
                if (arr == null || arr.Length == 0) return 0;
                st.TreesWas = arr;

                Vector3 tp = st.T.transform.position;
                Vector3 size = td.size;
                if (size.y <= 0.01f) return 0;

                var edit = td.treeInstances;

                float top = tp.y + size.y + 50f;
                var hits = new Il2CppInterop.Runtime.InteropTypes.Arrays
                               .Il2CppStructArray<RaycastHit>(16L);

                int considered = 0, moved = 0, onOurs = 0, onSkirtY = 0;
                float worst = 0f, drop = 0f;
                float skirtY = haveSkirt ? ours.center.y : 0f;

                for (int i = 0; i < edit.Length; i++)
                {
                    var inst = edit[i];
                    Vector3 nrm = inst.position;
                    Vector3 w = new Vector3(tp.x + nrm.x * size.x,
                                            tp.y + nrm.y * size.y,
                                            tp.z + nrm.z * size.z);
                    if (w.x < ours.min.x || w.x > ours.max.x) continue;
                    if (w.z < ours.min.z || w.z > ours.max.z) continue;
                    considered++;

                    float floor = float.NegativeInfinity;
                    try
                    {
                        int n = Physics.RaycastNonAlloc(new Vector3(w.x, top, w.z), Vector3.down,
                                                        hits, size.y + 200f, ~0,
                                                        QueryTriggerInteraction.Ignore);
                        for (int k = 0; k < n && k < hits.Length; k++)
                        {
                            var c = hits[k].collider;
                            if (c == null) continue;
                            if (!Ours(c, ourRoot)) continue;
                            float y = hits[k].point.y;
                            if (y > floor) floor = y;
                        }
                    }
                    catch { }

                    if (float.IsNegativeInfinity(floor))
                    {

                        if (!haveSkirt) continue;
                        floor = skirtY;
                        onSkirtY++;
                    }
                    else onOurs++;

                    float want = (floor - tp.y) / size.y;
                    if (want < 0f) want = 0f; else if (want > 1f) want = 1f;
                    float delta = Mathf.Abs((want - nrm.y) * size.y);
                    if (delta < 0.25f) continue;

                    inst.position = new Vector3(nrm.x, want, nrm.z);
                    edit[i] = inst;
                    moved++;
                    drop += delta;
                    if (delta > worst) worst = delta;
                }

                if (moved == 0)
                {
                    st.TreesWas = null;
                    log?.Msg("  [hostveg] host trees: " + considered + " of " + edit.Length
                           + " stand over our ground and every one of them was already within "
                           + "0.25 m of our floor. Nothing moved.");
                    return 0;
                }

                td.treeInstances = edit;
                log?.Msg("  [hostveg] host trees: brought " + moved + " of " + considered
                       + " down onto our floor (" + onOurs + " onto a surface of ours, "
                       + onSkirtY + " onto the skirt plane), average " + (drop / moved).ToString("F1")
                       + " m, worst " + worst.ToString("F1") + " m. The other "
                       + (edit.Length - considered) + " are past the skirt and are the horizon, "
                       + "so they keep the height the level gave them. Snapshot held for restore.");
                return moved;
            }
            catch (Exception e)
            {
                log?.Warning("[hostveg] host tree drop: " + e.Message);
                return 0;
            }
        }

        static bool Ours(Collider c, Transform ourRoot)
        {
            try
            {
                if (GroundSkirt.IsSkirt(c)) return true;
                if (ourRoot == null) return false;
                return c.transform.IsChildOf(ourRoot);
            }
            catch { return false; }
        }

        static string TreeCount(Terrain t)
        {
            try
            {
                var td = t.terrainData;
                if (td == null) return "terrainData is null";
                int trees = 0, dw = 0, dh = 0, dp = 0;
                try { trees = td.treeInstanceCount; } catch { }
                try { dw = td.detailWidth; dh = td.detailHeight; } catch { }
                try { var a = td.detailPrototypes; dp = a == null ? 0 : a.Length; } catch { }
                return "terrainData '" + td.name + "': " + trees + " tree instance(s), "
                     + dp + " detail prototype(s) at " + dw + "x" + dh
                     + ", size " + td.size.ToString();
            }
            catch { return "terrainData unreadable"; }
        }

        static string Name(Terrain t) { try { return t.gameObject.name; } catch { return "?"; } }
        static string Pos(Terrain t)  { try { return t.transform.position.ToString(); } catch { return "?"; } }
        static string Safe(Func<string> f) { try { return f() ?? "?"; } catch { return "?"; } }
    }
}
