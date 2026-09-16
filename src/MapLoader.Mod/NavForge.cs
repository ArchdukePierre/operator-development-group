// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using PF = Il2CppPathfinding;

namespace MapLoader
{

    public static class NavForge
    {
        public const float CellSize        = 0.30f;
        public const float CharacterRadius = 0.30f;

        static GameObject _go;
        static PF.RecastGraph _graph;
        static List<Bounds> _tiles;
        static int _tileIndex;

        public static PF.RecastGraph Graph => _graph;
        public static bool Filling => _tiles != null && _tileIndex < _tiles.Count;

        public static bool EnsureAstar(Transform sameSceneAs, MelonLogger.Instance log)
        {
            if (Il2Cpp.AstarPath.active != null)
            {

                string aScene = "?", gScene = "?";
                try { aScene = Il2Cpp.AstarPath.active.gameObject.scene.name; } catch { }
                try { if (sameSceneAs != null) gScene = sameSceneAs.gameObject.scene.name; } catch { }
                log.Msg("NAVFORGE: AstarPath.active already present -- reusing. "
                      + "astar is in scene '" + aScene + "', our geometry is in '"
                      + gScene + "'"
                      + (aScene == gScene ? "" : "  <-- DIFFERENT SCENES. Our map "
                        + "creates its own scene, so this is expected; it matters "
                        + "only if the scan comes back with too few nodes."));
                return true;
            }

            _go = new GameObject("MapLoader_Astar");
            _go.SetActive(false);
            if (sameSceneAs != null && sameSceneAs.parent != null)
                _go.transform.SetParent(sameSceneAs.parent, false);

            var astar = _go.AddComponent<Il2Cpp.AstarPath>();
            if (astar == null) { log.Error("NAVFORGE: AddComponent<AstarPath> returned null"); return false; }

            try { astar.scanOnStartup = false; } catch (Exception e) { log.Warning("scanOnStartup: " + e.Message); }
            try { astar.maxFrameTime = 3f; } catch { }
            try { astar.maxNearestNodeDistance = 20f; } catch { }
            try { astar.showGraphs = false; } catch { }

            _go.SetActive(true);

            bool ok = Il2Cpp.AstarPath.active != null;
            var data = ok ? Il2Cpp.AstarPath.active.data : null;
            int ng = -1;
            try { if (ok && Il2Cpp.AstarPath.active.graphs != null) ng = Il2Cpp.AstarPath.active.graphs.Length; }
            catch { }
            log.Msg($"NAVFORGE astar: active={(ok ? "OK" : "NULL")} " +
                    $"data={(data != null ? "OK" : "NULL")} graphs={ng} " +
                    $"scene={_go.scene.name}");

            if (!ok || data == null)
            {
                log.Error("NAVFORGE: AstarPath.OnEnable did not initialise -- ABORT (not forcing)");
                return false;
            }
            return true;
        }

        static void DropHostGraphs(Il2Cpp.AstarPath astar, PF.RecastGraph ours,
                                   MelonLogger.Instance log)
        {
            try
            {
                var graphs = astar.data.graphs;
                if (graphs == null) { log.Msg("NAVFORGE: no graph list to prune"); return; }

                int removed = 0, kept = 0;

                for (int i = graphs.Count - 1; i >= 0; i--)
                {
                    var gr = graphs[i];
                    if (gr == null) continue;
                    bool isOurs = false;
                    try { isOurs = ReferenceEquals(gr, ours) || gr.Pointer == ours.Pointer; } catch { }
                    if (isOurs) { kept++; continue; }
                    try
                    {
                        string nm = "?";
                        try { nm = gr.name; } catch { }
                        astar.data.RemoveGraph(gr);
                        removed++;
                        if (removed <= 6) log.Msg($"NAVFORGE: dropped host graph '{nm}'");
                    }
                    catch (Exception re)
                    { log.Warning("NAVFORGE: could not drop a host graph: " + re.Message); }
                }
                log.Msg($"NAVFORGE: removed {removed} host graph(s), kept {kept} of ours. "
                      + "Bots can now only path on the compound; graphs describing the "
                      + "hidden level are what put them in the treetops.");
            }
            catch (Exception e) { log.Warning("NAVFORGE DropHostGraphs: " + e.Message); }
        }

        public static int DropStaleGraphsOnClient(MelonLogger.Instance log)
        {
            try
            {
                var astar = Il2Cpp.AstarPath.active;
                if (astar == null || astar.data == null)
                {
                    log?.Msg("NAVFORGE client: no AstarPath here, so nothing can be "
                           + "clamping bots to a stale graph.");
                    return 0;
                }

                var graphs = astar.data.graphs;
                if (graphs == null) { log?.Msg("NAVFORGE client: no graph list"); return 0; }

                int before = graphs.Count, removed = 0;

                for (int i = graphs.Count - 1; i >= 0; i--)
                {
                    var gr = graphs[i];
                    if (gr == null) continue;
                    try
                    {
                        string nm = "?";
                        try { nm = gr.name; } catch { }
                        astar.data.RemoveGraph(gr);
                        removed++;
                        if (removed <= 4) log?.Msg("NAVFORGE client: dropped stale graph '" + nm + "'");
                    }
                    catch (Exception re)
                    { log?.Warning("NAVFORGE client: could not drop a graph: " + re.Message); }
                }

                log?.Msg("NAVFORGE client: removed " + removed + " of " + before
                       + " stale graph(s) left over from the host level. They described "
                       + "the hidden suburb, and an agent clamped to one of them is a bot "
                       + "standing somewhere nobody else sees it.");
                return removed;
            }
            catch (Exception e)
            {
                log?.Warning("NAVFORGE DropStaleGraphsOnClient: " + e.Message);
                return 0;
            }
        }

        public static PF.RecastGraph CreateGraph(Bounds bounds, MelonLogger.Instance log)
        {
            var astar = Il2Cpp.AstarPath.active;
            if (astar == null || astar.data == null) { log.Error("NAVFORGE: no AstarPath/data"); return null; }

            var s = bounds.size;
            if (!(s.x > 1f && s.y > 1f && s.z > 1f) ||
                float.IsNaN(s.x) || float.IsNaN(s.y) || float.IsNaN(s.z) ||
                float.IsInfinity(s.x) || s.x > 4000f || s.z > 4000f)
            {
                log.Error($"NAVFORGE: refusing degenerate bounds {bounds}");
                return null;
            }

            var g = new PF.RecastGraph();
            try { g.name = "MapLoaderRecast"; } catch { }
            astar.data.AddGraph(g);

            try
            {
                g.characterRadius = CharacterRadius;
                g.cellSize        = CellSize;
                g.walkableHeight  = 2f;
                g.walkableClimb   = 0.5f;
                g.maxSlope        = 30f;
                g.useTiles        = true;
                g.editorTileSize  = 128;
                g.forcedBoundsCenter = bounds.center;
                g.forcedBoundsSize   = bounds.size;
            }
            catch (Exception e) { log.Warning("NAVFORGE graph settings: " + e.Message); }

            try
            {

                g.rasterizeMeshes    = true;
                g.rasterizeColliders = true;

                g.rasterizeTerrain = MapLoader.MapIdentity.HostLevelIsMap;
                g.rasterizeTrees   = false;
            }
            catch (Exception e) { log.Warning("NAVFORGE rasterize flags: " + e.Message); }

            try
            {
                int foliage = MapRuntime.FoliageLayer;
                int trees = MapRuntime.TreeLayer;
                var lm = new LayerMask();
                int v = ~0;
                if (foliage >= 0) v &= ~(1 << foliage);

                if (trees >= 0) v &= ~(1 << trees);
                lm.value = v;
                g.mask = lm;
            }
            catch (Exception e) { log.Warning("NAVFORGE mask: " + e.Message); }

            LogGatherSettings(g, log);

            _graph = g;
            DropHostGraphs(astar, g, log);
            float cols = (s.x / CellSize) * (s.z / CellSize);
            log.Msg($"NAVFORGE graph: cell={CellSize} radius={CharacterRadius} " +
                    $"bounds c={bounds.center} s={bounds.size} => {cols / 1e6f:F2}M voxel columns");
            return g;
        }

        static void LogGatherSettings(PF.RecastGraph g, MelonLogger.Instance log)
        {
            string meshes = "?", colliders = "?", terrain = "?", trees = "?", mask = "?";
            try { meshes    = g.rasterizeMeshes.ToString(); }    catch { }
            try { colliders = g.rasterizeColliders.ToString(); } catch { }
            try { terrain   = g.rasterizeTerrain.ToString(); }   catch { }
            try { trees     = g.rasterizeTrees.ToString(); }     catch { }
            try { mask      = "0x" + g.mask.value.ToString("X8"); } catch { }
            log.Msg("NAVFORGE gather readback: meshes=" + meshes + " colliders=" + colliders
                  + " terrain=" + terrain + " trees=" + trees + " mask=" + mask
                  + "  (all four must read True/True/False/False or the scan is "
                  + "collecting something other than what was asked for)");
        }

        public static void Diagnose(MelonLogger.Instance log)
        {
            log.Msg("======== NAVFORGE DIAGNOSE ========");
            var astar = Il2Cpp.AstarPath.active;
            if (astar == null) { log.Error("no AstarPath.active -- nothing to test"); log.Msg("=================================="); return; }

            string aScene = "?"; try { aScene = astar.gameObject.scene.name; } catch { }
            var root = MapRuntime.Root;
            string gScene = "?"; try { if (root != null) gScene = root.scene.name; } catch { }
            log.Msg("astar scene='" + aScene + "'  map scene='" + gScene + "'");

            int graphs = -1; try { graphs = astar.graphs.Length; } catch { }
            int nodes = -1; try { if (_graph != null) nodes = _graph.CountNodes(); } catch { }
            log.Msg("graphs=" + graphs + "  our nodes=" + nodes);
            if (_graph != null) LogGatherSettings(_graph, log);

            if (root == null) { log.Warning("no map root -- build the map first (F9)"); log.Msg("=================================="); return; }

            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<MeshCollider> cols = null;
            try { cols = root.GetComponentsInChildren<MeshCollider>(false); } catch (Exception e)
            { log.Error("collider sweep: " + e.Message); log.Msg("=================================="); return; }
            if (cols == null) { log.Warning("no MeshColliders under the map root"); log.Msg("=================================="); return; }

            int wallsTested = 0, wallsPorous = 0, doorsTested = 0, doorsPorous = 0;
            var examples = new List<string>();

            for (int i = 0; i < cols.Length; i++)
            {
                var c = cols[i];
                if (c == null) continue;

                Bounds b;
                try { b = c.bounds; } catch { continue; }
                var sz = b.size;

                bool tall = sz.y > 1.5f;
                bool thin = Mathf.Min(sz.x, sz.z) < 1.2f;
                if (!tall || !thin) continue;

                string name = "?";
                try { name = c.gameObject.name; } catch { }
                bool isDoor = name.IndexOf("door", StringComparison.OrdinalIgnoreCase) >= 0;

                var p = new Vector3(b.center.x, b.min.y + 0.4f, b.center.z);
                bool on = false;
                try { on = astar.IsPointOnNavmesh(p); } catch { continue; }

                if (isDoor) { doorsTested++; if (on) doorsPorous++; }
                else
                {
                    wallsTested++;
                    if (on)
                    {
                        wallsPorous++;
                        if (examples.Count < 6)
                            examples.Add("  navmesh runs THROUGH '" + name + "' at " + p);
                    }
                }
            }

            log.Msg("walls tested=" + wallsTested + "  navmesh passes through=" + wallsPorous);
            for (int i = 0; i < examples.Count; i++) log.Msg(examples[i]);
            log.Msg("doors tested=" + doorsTested + "  navmesh passes through=" + doorsPorous);

            if (wallsTested == 0)
                log.Warning("VERDICT: found no wall-shaped colliders to test. The classifier "
                          + "(taller than 1.5 m, under 1.2 m thick) missed them, so this run "
                          + "proves nothing either way.");
            else if (wallsPorous * 4 > wallsTested)
                log.Warning("VERDICT: the navmesh runs through " + wallsPorous + " of "
                          + wallsTested + " walls. The scan is NOT rasterising our wall "
                          + "geometry, and that is why bots walk through walls. Check the "
                          + "gather readback above.");
            else if (wallsPorous > 0)
                log.Msg("VERDICT: mostly solid, " + wallsPorous + " leaks. Walls ARE being "
                      + "rasterised; the leaks are worth a look but are not the main cause.");
            else
                log.Msg("VERDICT: no wall is porous. Walls are in the navmesh, so bots crossing "
                      + "them are NOT doing it through the graph -- look at doors ("
                      + doorsPorous + " porous) or at spawn placement instead.");
            log.Msg("==================================");
        }

        public static void Probe(Transform sameSceneAs, MelonLogger.Instance log)
        {
            var cam = Camera.main;
            if (cam == null) { log.Error("NAVFORGE: no Camera.main"); return; }
            var p = cam.transform.position;

            log.Msg("======== NAVFORGE PROBE ========");
            if (!EnsureAstar(sameSceneAs, log)) { log.Msg("================================"); return; }
            var astar = Il2Cpp.AstarPath.active;

            var box = new Bounds(p, new Vector3(32f, 30f, 32f));
            var g = CreateGraph(box, log);
            if (g == null) { log.Msg("================================"); return; }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                astar.Scan(g);
            }
            catch (Exception e)
            {
                sw.Stop();
                log.Error("NAVFORGE: Scan threw: " + e.Message);
                log.Msg("================================");
                return;
            }
            sw.Stop();

            int nodes = -1;
            try { nodes = g.CountNodes(); } catch (Exception e) { log.Warning("CountNodes: " + e.Message); }
            bool onMesh = false;
            try { onMesh = astar.IsPointOnNavmesh(p); } catch { }
            bool scanned = false;
            try { scanned = g.isScanned; } catch { }

            float area = 32f * 32f;
            float perM2 = sw.ElapsedMilliseconds / area;
            log.Msg($"NAVFORGE PROBE: {sw.ElapsedMilliseconds} ms for {area:F0} m^2 " +
                    $"=> {perM2:F3} ms/m^2");
            log.Msg($"NAVFORGE PROBE: isScanned={scanned} nodes={nodes} playerOnNavmesh={onMesh}");
            log.Msg($"NAVFORGE PROBE: extrapolated full map (494x478 m) = " +
                    $"{perM2 * 494f * 478f / 1000f:F1} s of scan work");
            if (nodes == 0)
                log.Warning("NAVFORGE: 0 nodes. Recast ran but collected nothing -- check that " +
                            "AddColliders is on, our objects are layer 0, and the astar object " +
                            "is in the SAME scene as the map.");
            else if (nodes > 0)
                log.Msg("NAVFORGE: recast WORKS in this build. Shift+Home builds the whole map.");
            log.Msg("================================");
        }

        public static bool BuildFull(Bounds mapBounds, Transform sameSceneAs, MelonLogger.Instance log)
        {
            if (!EnsureAstar(sameSceneAs, log)) return false;
            var astar = Il2Cpp.AstarPath.active;

            if (_graph != null)
            {
                try { astar.data.RemoveGraph(_graph); } catch { }
                _graph = null;
            }

            var padded = new Bounds(mapBounds.center,
                                    mapBounds.size + new Vector3(8f, 20f, 8f));
            var g = CreateGraph(padded, log);
            if (g == null) return false;

            log.Msg("NAVFORGE: scanning the whole map in one pass. Expect a freeze of " +
                    "roughly 15 s -- the probe measured 0.054 ms/m^2.");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { astar.Scan(g); }
            catch (Exception e)
            {
                sw.Stop();
                log.Error("NAVFORGE: full Scan threw: " + e.Message);
                return false;
            }
            sw.Stop();

            int nodes = -1, tx = 0, tz = 0;
            try { nodes = g.CountNodes(); } catch { }
            try { tx = g.tileXCount; tz = g.tileZCount; } catch { }
            log.Msg($"NAVFORGE: full scan done in {sw.ElapsedMilliseconds / 1000f:F1} s, " +
                    $"nodes={nodes}, tiles={tx}x{tz}");
            if (nodes <= 0)
            {
                log.Error("NAVFORGE: scan produced 0 nodes. The graph exists but is empty -- " +
                          "check AddColliders is on and our geometry is on layer 0.");
                return false;
            }
            log.Msg("NAVFORGE: graph is up; AI spawn onto it automatically. "
                  + "(This used to say press [ -- that key now tunes exposure.)");
            _tiles = null; _tileIndex = 0;
            return true;
        }

        public static void StepTiles(MelonLogger.Instance log)
        {
            if (_tiles == null) return;
            if (_tileIndex >= _tiles.Count)
            {
                int nodes = -1;
                try { if (_graph != null) nodes = _graph.CountNodes(); } catch { }
                log.Msg($"NAVFORGE: tile fill complete ({_tiles.Count} tiles), nodes={nodes}");
                log.Msg("NAVFORGE: graph is up. Ctrl+Home spawns bots onto it.");
                _tiles = null;
                return;
            }
            var astar = Il2Cpp.AstarPath.active;
            if (astar == null) { _tiles = null; return; }
            try { if (astar.isScanning) return; } catch { }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { astar.UpdateGraphs(_tiles[_tileIndex]); }
            catch (Exception e)
            {
                log.Error($"NAVFORGE: UpdateGraphs threw on tile {_tileIndex}: {e.Message}");
                _tiles = null;
                return;
            }
            _tileIndex++;
            if (_tileIndex <= 5 || _tileIndex % 20 == 0 || _tileIndex == _tiles.Count)
            {
                int nodes = -1;
                try { if (_graph != null) nodes = _graph.CountNodes(); } catch { }
                log.Msg($"  NAVFORGE tile {_tileIndex}/{_tiles.Count} queued in " +
                        $"{sw.ElapsedMilliseconds} ms, nodes so far={nodes}");
            }
        }

        public static void Destroy(MelonLogger.Instance log)
        {
            _tiles = null; _tileIndex = 0;
            var astar = Il2Cpp.AstarPath.active;
            if (astar != null && _graph != null)
            {
                try { astar.data.RemoveGraph(_graph); } catch { }
            }
            _graph = null;
            if (_go != null)
            {
                try { UnityEngine.Object.Destroy(_go); } catch { }
                _go = null;
            }
            log.Msg("NAVFORGE torn down; AstarPath.active=" +
                    (Il2Cpp.AstarPath.active == null ? "null" : "present"));
        }

        public static void Abort()
        {
            _tiles = null; _tileIndex = 0;
            _graph = null;
            _go = null;
        }
    }
}
