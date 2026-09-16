// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace MapLoader
{

    public static class ShellSwap
    {

        static readonly List<Collider> _hiddenColliders = new List<Collider>();

        static readonly List<Terrain> _hiddenTerrains = new List<Terrain>();

        static readonly List<Behaviour> _hiddenBrg = new List<Behaviour>();

        static readonly List<GameObject> _deactivatedRoots = new List<GameObject>();

        static readonly List<Light> _hostLights = new List<Light>();
        static readonly List<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>
            _hostSkyLights = new List<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>();

        static readonly List<Renderer> _forcedOffRenderers = new List<Renderer>();
        static readonly List<bool> _forcedOffWasEnabled = new List<bool>();

        static readonly List<GameObject> _layerMoved = new List<GameObject>();
        static readonly List<int> _layerWas = new List<int>();
        public static bool ExcludeHostFromNav = true;

        public static int KeepSceneHandle = 0;

        public static bool IsAdditiveWorld(UnityEngine.SceneManagement.Scene sc)
        {
            try
            {
                if (!sc.IsValid()) return false;
                if (KeepSceneHandle != 0 && sc.handle == KeepSceneHandle) return true;
                var nm = MapIdentity.AdditiveSceneName;
                return !string.IsNullOrEmpty(nm)
                    && string.Equals(sc.name, nm, StringComparison.Ordinal);
            }
            catch { return false; }
        }

        public static int RestoreAdditiveTerrain(MelonLogger.Instance log)
        {
            int n = 0;
            try
            {
                for (int i = _hiddenTerrains.Count - 1; i >= 0; i--)
                {
                    var td = _hiddenTerrains[i];
                    if (td == null) continue;
                    if (!IsAdditiveWorld(td.gameObject.scene)) continue;
                    try { td.enabled = true; } catch { }
                    try
                    {
                        var tc = td.GetComponent<TerrainCollider>();
                        if (tc != null) { tc.enabled = true; _hiddenColliders.Remove(tc); }
                    }
                    catch { }
                    _hiddenTerrains.RemoveAt(i);
                    n++;
                }
                if (n > 0)
                    log?.Msg("[shell] " + n + " additive-world terrain(s) had been switched off by "
                           + "the shell hide and are back on, colliders included. That was the "
                           + "ground the trees and the spawn were missing.");
            }
            catch (Exception e) { log?.Warning("[shell] restore additive terrain: " + e.Message); }
            return n;
        }

        static MelonLogger.Instance _log;

        public static void Install(HarmonyLib.Harmony harmony, MelonLogger.Instance log)
        {
            _log = log;
            try
            {
                var mi = AccessTools.Method(typeof(Il2Cpp.OperationsManager),
                                            "StartOperation");
                if (mi == null)
                {
                    log.Warning("hook miss: OperationsManager.StartOperation");
                    return;
                }
                harmony.Patch(mi, prefix: new HarmonyMethod(
                    typeof(ShellSwap), nameof(Pre_StartOperation)));
                log.Msg("hooked OperationsManager.StartOperation");
            }
            catch (Exception e) { log.Error("StartOperation hook: " + e.Message); }

            try
            {
                var mi = AccessTools.Method(typeof(Il2Cpp.MissionLaptop),
                                            "SpawnOperations");
                if (mi == null)
                {
                    log.Warning("hook miss: MissionLaptop.SpawnOperations");
                    return;
                }
                harmony.Patch(mi, postfix: new HarmonyMethod(
                    typeof(ShellSwap), nameof(Post_SpawnOperations)));
                log.Msg("hooked MissionLaptop.SpawnOperations");
            }
            catch (Exception e) { log.Error("SpawnOperations hook: " + e.Message); }
        }

        static void Post_SpawnOperations()
        {
        }

        static void Pre_StartOperation(string sceneName, int prepTime,
                                       string displayName, string infilTime,
                                       string _activeAchievement, float targetTime)
        {
            try
            {

                MelonLogger.Msg($"[Map_Loader] StartOperation scene='{sceneName}' " +
                                $"display='{displayName}' infilTime='{infilTime}' " +
                                $"prep={prepTime}  (CustomOps decides whether this one " +
                                $"is ours; see its own [ops] LAUNCHING line)");
            }
            catch { }
        }

        static readonly string[] ShellRoots =
        {
            "STATIC SCENE", "DYNAMIC SCENE", "IMPOSTORS", "Junkyard Source",
        };

        static readonly string[] ShellRootContains =
        {
            "BackgroundHouses", "Background Buildings", "MediumTruck",
            "road grass", "Road Tess", "Road_", "Ground",
            "Decals", "Point Lights", "WallVariation", "PowerBox",
            "Kitcabinet", "MeshBaker", "Light Blockers",
        };

        static readonly string[] GameplayRoots = { "InfiltrationManager", "playerborder" };

        static bool IsGameplayRoot(string n)
        {
            if (string.IsNullOrEmpty(n)) return false;
            for (int i = 0; i < GameplayRoots.Length; i++)
                if (string.Equals(n, GameplayRoots[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        static bool NameSaysScenery(string n)
        {
            if (string.IsNullOrEmpty(n)) return false;
            for (int i = 0; i < ShellRootContains.Length; i++)
                if (n.IndexOf(ShellRootContains[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        static readonly string[] DeactivateRoots =
        {
            "STATIC SCENE", "IMPOSTORS", "Junkyard Source",
        };

        public static int HeavyRootRenderers = 300;

        static Bounds _mapBounds;
        static bool _boundsValid;

        public static void SetMapBounds(Bounds b, bool valid)
        {
            _mapBounds = b;
            _boundsValid = valid;
        }

        static bool OverlapsXZ(Bounds a, Bounds b)
        {
            return a.min.x <= b.max.x && a.max.x >= b.min.x
                && a.min.z <= b.max.z && a.max.z >= b.min.z;
        }

        static bool TryRootBounds(GameObject go, Il2CppInterop.Runtime.InteropTypes.Arrays
                                  .Il2CppArrayBase<Renderer> rends, out Bounds b)
        {
            b = new Bounds();
            bool init = false;
            for (int i = 0; i < rends.Length; i++)
            {
                var rd = rends[i];
                if (rd == null || rd.gameObject == null) continue;
                Bounds rb;
                if (rd.gameObject.activeInHierarchy) rb = rd.bounds;
                else rb = new Bounds(rd.transform.position, Vector3.one * 0.5f);
                if (!init) { b = rb; init = true; }
                else b.Encapsulate(rb);
            }
            return init;
        }

        public static bool MapUsesDoors = true;

        static void DecideDoorPolicy(MelonLogger.Instance log)
        {
            try
            {
                var markers = MapDoors.FindDoorMarkers();
                int n = markers != null ? markers.Count : 0;
                MapUsesDoors = n > 0;
                log?.Msg("  door policy: this map ships " + n + " door marker(s), so host "
                       + "doors are " + (MapUsesDoors
                            ? "SPARED (they get relocated into our doorways)"
                            : "HIDDEN with the rest of the shell. Sparing them on a map with "
                            + "no doorways leaves the suburb's doors hanging in mid-air, "
                            + "solid, with nothing around them."));
            }
            catch (Exception e)
            {
                MapUsesDoors = true;
                log?.Warning("  door policy: could not count markers (" + e.Message
                           + "); sparing host doors, which is the survivable answer");
            }
        }

        static bool IsDoorPart(Transform t)
        {
            if (!MapUsesDoors) return false;
            try
            {
                int hops = 0;
                while (t != null && hops < 8)
                {
                    if (t.GetComponent<Il2Cpp.DoorV2>() != null) return true;
                    if (t.GetComponent<Il2Cpp.DoorHandleV2>() != null) return true;
                    t = t.parent;
                    hops++;
                }
            }
            catch { }
            return false;
        }

        static bool HoldsSomethingAlive(GameObject go)
        {
            try
            {
                var cam = Camera.main;
                if (cam != null && cam.transform.IsChildOf(go.transform)) return true;

                if (go.GetComponentInChildren<CharacterController>(true) != null) return true;
                if (go.GetComponentInChildren<AudioListener>(true) != null) return true;
            }
            catch { return true; }

            try
            {
                if (go.GetComponentInChildren<Il2Cpp.AgentController>(true) != null) return true;
            }
            catch (Exception e)
            {
                if (!_agentLookupWarned)
                {
                    _agentLookupWarned = true;
                    _log?.Warning("[shell] AgentController lookup threw (" + e.Message
                            + "). Treating roots as scenery from here. If bots vanish, "
                            + "this is why.");
                }
            }
            return false;
        }

        static bool _agentLookupWarned;

        public static int SweepHostRenderers(Transform ourRoot, MelonLogger.Instance log,
                                             bool verbose)
        {
            if (!MapIdentity.HostLevelIsMap) return 0;
            int hid = 0, keptPlayer = 0, keptDoor = 0, cols = 0;
            try
            {
                var ourScene = ourRoot != null ? ourRoot.gameObject.scene : default;
                Transform camRoot = null;
                try
                {
                    var cam = Camera.main;
                    if (cam != null) camRoot = cam.transform.root;
                }
                catch { }

                var rends = UnityEngine.Object.FindObjectsOfType<Renderer>(true);
                for (int i = 0; i < (rends != null ? rends.Length : 0); i++)
                {
                    var rd = rends[i];
                    if (rd == null) continue;
                    try
                    {
                        var sc = rd.gameObject.scene;
                        if (!sc.IsValid()) continue;
                        if (ourScene.IsValid() && sc.handle == ourScene.handle) continue;

                        if (IsAdditiveWorld(sc)) continue;

                        if (string.Equals(sc.name, "DontDestroyOnLoad", StringComparison.Ordinal))
                        { keptPlayer++; continue; }
                        if (rd.forceRenderingOff) continue;
                        if (ourRoot != null && rd.transform.IsChildOf(ourRoot)) continue;

                        if (rd is SkinnedMeshRenderer) { keptPlayer++; continue; }

                        try
                        {
                            if (rd.GetComponentInParent<Il2CppMirror.NetworkIdentity>() != null)
                            { keptPlayer++; continue; }
                        }
                        catch { }

                        if (camRoot != null && rd.transform.IsChildOf(camRoot))
                        { keptPlayer++; continue; }
                        if (IsDoorPart(rd.transform)) { keptDoor++; continue; }

                        bool wasEnabled = rd.enabled;
                        rd.enabled = false;
                        rd.forceRenderingOff = true;
                        _forcedOffRenderers.Add(rd);
                        _forcedOffWasEnabled.Add(wasEnabled);
                        hid++;
                    }
                    catch { }
                }
            }
            catch (Exception e) { log?.Warning("[shell] SweepHostRenderers: " + e.Message); }

            try
            {
                var ourScene2 = ourRoot != null ? ourRoot.gameObject.scene : default;
                Transform camRoot2 = null;
                try
                {
                    var cam = Camera.main;
                    if (cam != null) camRoot2 = cam.transform.root;
                }
                catch { }
                var all = UnityEngine.Object.FindObjectsOfType<Collider>(true);
                for (int i = 0; i < (all != null ? all.Length : 0); i++)
                {
                    var cd = all[i];
                    if (cd == null || !cd.enabled) continue;
                    try
                    {
                        var sc = cd.gameObject.scene;
                        if (!sc.IsValid()) continue;
                        if (ourScene2.IsValid() && sc.handle == ourScene2.handle) continue;

                        if (IsAdditiveWorld(sc)) continue;
                        if (string.Equals(sc.name, "DontDestroyOnLoad", StringComparison.Ordinal))
                            continue;
                        if (ourRoot != null && cd.transform.IsChildOf(ourRoot)) continue;
                        if (camRoot2 != null && cd.transform.IsChildOf(camRoot2)) continue;

                        if (cd.GetComponentInParent<Il2CppMirror.NetworkIdentity>() != null)
                            continue;
                        if (cd.GetComponentInChildren<SkinnedMeshRenderer>(true) != null) continue;
                        if (IsDoorPart(cd.transform)) { keptDoor++; continue; }
                        cd.enabled = false;
                        _hiddenColliders.Add(cd);
                        cols++;
                    }
                    catch { }
                }
            }
            catch (Exception e) { log?.Warning("[shell] host collider sweep: " + e.Message); }

            if (hid > 0 || cols > 0 || verbose)
                log?.Msg("[shell] host sweep: disabled " + cols + " host COLLIDER(s) -- "
                       + "invisible-but-solid suburb geometry standing inside the map. "
                       + "hid " + hid + " host renderer(s) (kept "
                       + keptPlayer + " alive: DDOL, skinned, networked or player-rig; "
                       + keptDoor + " door part(s)). With the host's lights off, anything "
                       + "swept was rendering BLACK inside the additive world.");
            return hid;
        }

        public static int HideShell(Transform ourRoot, MelonLogger.Instance log)
        {
            RestoreShell(log);
            DecideDoorPolicy(log);
            int r = 0, c = 0, skipped = 0, deact = 0, deactRends = 0, doorSkip = 0;
            int navMoved = 0;
            try
            {

                var roots = new System.Collections.Generic.List<GameObject>();
                var ourScene = ourRoot != null ? ourRoot.gameObject.scene : default;
                int n = UnityEngine.SceneManagement.SceneManager.sceneCount;
                for (int s = 0; s < n; s++)
                {
                    var sc = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
                    if (!sc.IsValid() || !sc.isLoaded) continue;
                    if (ourScene.IsValid() && sc.handle == ourScene.handle) continue;

                    if (IsAdditiveWorld(sc)) continue;
                    var sr = sc.GetRootGameObjects();
                    if (sr == null) continue;
                    for (int k = 0; k < sr.Count; k++)
                        if (sr[k] != null) roots.Add(sr[k]);
                }
                if (roots.Count == 0) { log.Warning("no host scene roots"); return 0; }
                log.Msg($"  scanning {roots.Count} root(s) across the loaded scenes "
                      + $"(ours: '{(ourScene.IsValid() ? ourScene.name : "<none>")}')");

                for (int i = 0; i < roots.Count; i++)
                {
                    var go = roots[i];
                    if (go == null) continue;
                    if (ourRoot != null && go.transform == ourRoot) continue;

                    bool isShell = false, canDeactivate = false;
                    for (int s = 0; s < ShellRoots.Length; s++)
                        if (go.name == ShellRoots[s]) { isShell = true; break; }

                    if (!isShell && NameSaysScenery(go.name))
                    {
                        isShell = true;
                        log.Msg($"  root '{go.name}' is known host scenery by name -- hiding");
                    }
                    for (int s = 0; s < DeactivateRoots.Length; s++)
                        if (go.name == DeactivateRoots[s]) { canDeactivate = true; break; }

                    var rends = go.GetComponentsInChildren<Renderer>(true);

                    if (!isShell && rends != null && rends.Length >= HeavyRootRenderers)
                    {
                        bool holdsCamera = false;
                        var cam = Camera.main;
                        if (cam != null && cam.transform.IsChildOf(go.transform))
                            holdsCamera = true;
                        if (!holdsCamera)
                        {
                            isShell = true;
                            log.Msg($"  root '{go.name}' has {rends.Length} renderers " +
                                    "-- treating as level geometry (name not in the list)");
                        }
                    }

                    if (!isShell && _boundsValid && rends != null && rends.Length > 0
                        && !IsGameplayRoot(go.name)
                        && !HoldsSomethingAlive(go))
                    {
                        Bounds rb;
                        if (TryRootBounds(go, rends, out rb) && OverlapsXZ(rb, _mapBounds))
                        {
                            isShell = true;
                            log.Msg($"  root '{go.name}' ({rends.Length} renderers) overlaps " +
                                    $"the ported map in XZ -- hiding. centre={rb.center} size={rb.size}");
                        }
                    }

                    if (!isShell)
                    {
                        skipped++;

                        if (rends != null && rends.Length > 0)
                        {
                            Bounds sb;
                            bool haveB = TryRootBounds(go, rends, out sb);
                            string where = haveB ? $" centre={sb.center} size={sb.size}" : "";

                            string why;
                            if (!_boundsValid) why = "map bounds not valid at hide time";
                            else if (!haveB) why = "could not compute its bounds";

                            else if (IsGameplayRoot(go.name))
                                why = "gameplay root by name (infil/exfil machinery lives here; hiding it breaks insertions)";
                            else if (HoldsSomethingAlive(go)) why = "HoldsSomethingAlive said it is alive";
                            else if (!OverlapsXZ(sb, _mapBounds))
                                why = $"does not overlap our map in XZ (ours centre={_mapBounds.center} size={_mapBounds.size})";
                            else why = "OVERLAPS AND IS NOT ALIVE -- this should have been hidden";

                            log.Msg($"  left alone: '{go.name}' ({rends.Length} renderers){where}  <- {why}");
                        }
                        continue;
                    }

                    if (canDeactivate)
                    {
                        try
                        {
                            if (go.GetComponentInChildren<Il2Cpp.DoorV2>(true) != null)
                                canDeactivate = false;
                        }
                        catch { }
                    }

                    if (canDeactivate && go.activeSelf)
                    {
                        go.SetActive(false);
                        _deactivatedRoots.Add(go);
                        deact++;
                        deactRends += (rends == null ? 0 : rends.Length);
                        log.Msg($"  deactivated root '{go.name}' " +
                                $"({(rends == null ? 0 : rends.Length)} renderers, " +
                                "immune to re-enable)");
                        continue;
                    }

                    log.Msg($"  hiding root '{go.name}'");
                    if (rends != null)
                        for (int k = 0; k < rends.Length; k++)
                        {
                            var rd = rends[k];
                            if (rd == null || rd.gameObject == null) continue;
                            if (ourRoot != null && rd.transform.IsChildOf(ourRoot)) continue;

                            if (IsDoorPart(rd.transform)) { doorSkip++; continue; }
                            bool wasEnabled = rd.enabled;
                            rd.enabled = false;
                            rd.forceRenderingOff = true;
                            _forcedOffRenderers.Add(rd);
                            _forcedOffWasEnabled.Add(wasEnabled);
                            if (wasEnabled) r++;

                            if (ExcludeHostFromNav && MapRuntime.FoliageLayer >= 0)
                            {
                                try
                                {
                                    var g2 = rd.gameObject;
                                    if (g2.layer != MapRuntime.FoliageLayer)
                                    {
                                        _layerMoved.Add(g2);
                                        _layerWas.Add(g2.layer);
                                        g2.layer = MapRuntime.FoliageLayer;
                                        navMoved++;
                                    }
                                }
                                catch { }
                            }
                        }

                    var cols = go.GetComponentsInChildren<Collider>(true);
                    if (cols != null)
                        for (int k = 0; k < cols.Length; k++)
                        {
                            var cd = cols[k];
                            if (cd == null || cd.gameObject == null) continue;
                            if (ourRoot != null && cd.transform.IsChildOf(ourRoot)) continue;
                            if (!cd.enabled) continue;

                            if (IsDoorPart(cd.transform)) { doorSkip++; continue; }
                            cd.enabled = false;
                            _hiddenColliders.Add(cd);
                            c++;
                        }
                }

                int dir = 0;
                bool keepHostLights = false;
                try { keepHostLights = MapRuntime.UseHostLighting; } catch { }
                if (keepHostLights)
                    log.Msg("  host lights LEFT ON: this map borrows the shipped level's "
                          + "daylight. Its renderers and terrain are still hidden, so none of "
                          + "the host MAP is visible -- only its light and its sky.");
                for (int i = 0; keepHostLights == false && i < roots.Count; i++)
                {
                    var go = roots[i];
                    if (go == null) continue;
                    if (ourRoot != null && go.transform == ourRoot) continue;
                    var lights = go.GetComponentsInChildren<Light>(true);
                    if (lights == null) continue;
                    for (int k = 0; k < lights.Length; k++)
                    {
                        var li = lights[k];
                        if (li == null || !li.enabled) continue;
                        if (ourRoot != null && li.transform.IsChildOf(ourRoot)) continue;
                        try { if (li.type == LightType.Directional) dir++; } catch { }
                        li.enabled = false;
                        _hostLights.Add(li);

                        try
                        {
                            var hda = li.GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>();
                            if (hda != null && hda.interactsWithSky)
                            {
                                hda.interactsWithSky = false;
                                _hostSkyLights.Add(hda);
                            }
                        }
                        catch { }
                    }
                }
                if (_hostLights.Count > 0)
                    log.Msg($"  switched off {_hostLights.Count} host light(s) ({dir} directional, "
                          + $"{_hostLights.Count - dir} local) so only our rig lights the map");
            }
            catch (Exception e) { log.Error("HideShell: " + e.Message); }

            int pb = 0;
            int pbLayer = LayerMask.NameToLayer("PlayerBlock");
            if (pbLayer >= 0)
            {
                try
                {
                    var all = Resources.FindObjectsOfTypeAll<Collider>();
                    if (all != null)
                        for (int k = 0; k < all.Length; k++)
                        {
                            var cd = all[k];
                            if (cd == null || cd.gameObject == null) continue;
                            if (cd.gameObject.layer != pbLayer) continue;
                            if (!cd.gameObject.scene.IsValid()) continue;
                            if (ourRoot != null && cd.transform.IsChildOf(ourRoot)) continue;
                            if (!cd.enabled) continue;
                            cd.enabled = false;
                            _hiddenColliders.Add(cd);
                            pb++;
                        }
                }
                catch (Exception e) { log.Warning("PlayerBlock: " + e.Message); }
                log.Msg($"  removed {pb} PlayerBlock boundary colliders " +
                        "(host map's out-of-bounds walls)");
            }
            else log.Warning("  no 'PlayerBlock' layer in this build");
            c += pb;

            int t = 0;
            try
            {
                var terrains = Resources.FindObjectsOfTypeAll<Terrain>();
                if (terrains != null)
                    for (int k = 0; k < terrains.Length; k++)
                    {
                        var td = terrains[k];
                        if (td == null || td.gameObject == null) continue;
                        if (!td.gameObject.scene.IsValid()) continue;
                        if (ourRoot != null && td.transform.IsChildOf(ourRoot)) continue;

                        if (IsAdditiveWorld(td.gameObject.scene)) continue;
                        if (!td.enabled) continue;
                        td.enabled = false;
                        _hiddenTerrains.Add(td);
                        t++;
                        var tc = td.GetComponent<TerrainCollider>();
                        if (tc != null && tc.enabled)
                        { tc.enabled = false; _hiddenColliders.Add(tc); c++; }
                    }
            }
            catch (Exception e) { log.Warning("terrain hide: " + e.Message); }

            int brg = 0;
            bool stripHostPlants = false;
            try { stripHostPlants = MapIdentity.OwnSky; } catch { }
            try
            {
                if (!stripHostPlants)
                {

                    int back = 0;
                    for (int k = 0; k < _hiddenBrg.Count; k++)
                    {
                        try { if (_hiddenBrg[k] != null) { _hiddenBrg[k].enabled = true; back++; } }
                        catch { }
                    }
                    _hiddenBrg.Clear();
                    log.Msg("  host vegetation KEPT: this map borrows the shipped level's "
                          + "world, so its trees and grass are part of the look. Only a map "
                          + "with a sky of its own withdraws them."
                          + (back > 0 ? "  Re-enabled " + back + " registerer(s) a previous "
                                      + "mission had switched off." : ""));
                }
                var regs = stripHostPlants
                    ? Resources.FindObjectsOfTypeAll<Il2CppBRGInstancedRenderer.BRGRegisterer>()
                    : null;
                if (regs != null)
                    for (int k = 0; k < regs.Length; k++)
                    {
                        var rg = regs[k];
                        if (rg == null || rg.gameObject == null) continue;
                        if (!rg.gameObject.scene.IsValid()) continue;
                        if (ourRoot != null && rg.transform.IsChildOf(ourRoot)) continue;
                        if (!rg.enabled) continue;
                        rg.enabled = false;
                        _hiddenBrg.Add(rg);
                        brg++;
                    }
            }
            catch (Exception e) { log.Warning("BRG vegetation hide: " + e.Message); }
            if (brg > 0)
                log.Msg("  withdrew " + brg + " BRG instanced-vegetation registerer(s) -- the "
                      + "host's GPU-scattered trees and grass, which have no renderers to hide");

            HostVeg.Suppress(ourRoot, log);

            HostVeg.KillHostFlares(ourRoot, log);

            if (doorSkip > 0)
                log.Msg($"  kept {doorSkip} DOOR renderers+colliders alive -- the 42 scene doors " +
                        "are the only fully-wired doors in the game, and they must stay both " +
                        "VISIBLE and interactive");
            log.Msg($"shell hidden: {deact} roots deactivated ({deactRends} renderers), " +
                    $"{r} renderers disabled, {c} colliders, {t} terrains " +
                    $"({skipped} other roots left alone)");
                if (ExcludeHostFromNav)
                    log.Msg($"  {navMoved} hidden host object(s) moved onto the foliage layer so " +
                            "the nav scan cannot see them. Hiding a floor does not remove it from " +
                            "the navmesh, and bots were standing on the suburb's upper floor at " +
                            "y=3.8 with nothing under them. Restored exactly on teardown.");
            if (r == 0 && deact == 0)
                log.Warning("nothing hidden -- this scene has no root named " +
                            string.Join("/", ShellRoots) + ". Run F7 and add its " +
                            "geometry root to ShellRoots.");
            log.Msg("F9 or Shift+F2 restores the shell exactly.");
            return r;
        }

        public static void DumpRoots(Transform ourRoot, MelonLogger.Instance log)
        {
            log.Msg("======== scene root inventory ========");
            try
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                var roots = scene.GetRootGameObjects();
                if (roots == null) { log.Warning("no scene roots"); return; }

                var hidden = new HashSet<int>();
                for (int i = 0; i < _forcedOffRenderers.Count; i++)
                {
                    var rd = _forcedOffRenderers[i];
                    if (rd != null) hidden.Add(rd.GetInstanceID());
                }

                log.Msg($"{"root",-30} {"active",6} {"rend",6} {"ON",6} {"hid",5}  centre / size");
                int leftover = 0;
                for (int i = 0; i < roots.Count; i++)
                {
                    var go = roots[i];
                    if (go == null) continue;
                    bool ours = ourRoot != null && go.transform == ourRoot;

                    var all = go.GetComponentsInChildren<Renderer>(true);
                    if (all == null || all.Length == 0) continue;

                    int on = 0, hid = 0;
                    bool bi = false;
                    Bounds b = new Bounds();
                    for (int k = 0; k < all.Length; k++)
                    {
                        var rd = all[k];
                        if (rd == null || rd.gameObject == null) continue;
                        if (hidden.Contains(rd.GetInstanceID())) hid++;

                        if (rd.enabled && !rd.forceRenderingOff
                            && rd.gameObject.activeInHierarchy)
                        {
                            on++;
                            if (!bi) { b = rd.bounds; bi = true; }
                            else b.Encapsulate(rd.bounds);
                        }
                    }

                    string tag = "";
                    if (ours) tag = "  <== OURS";
                    else if (on > 0) { leftover += on; tag = "  <== STILL DRAWING"; }

                    string nm = go.name ?? "?";
                    if (nm.Length > 30) nm = nm.Substring(0, 30);
                    log.Msg($"{nm,-30} {go.activeSelf,6} {all.Length,6} {on,6} {hid,5}  " +
                            (bi ? $"{b.center} / {b.size}" : "-") + tag);
                }
                log.Msg($"host renderers still drawing: {leftover}");
                if (leftover > 0)
                    log.Warning("=> a STILL DRAWING root is host geometry the hide " +
                                "missed. If stray props float where a hidden building " +
                                "was, that root is where they come from.");
                else
                    log.Msg("=> nothing of the host is drawing; strays are OURS.");
            }
            catch (Exception e) { log.Error("DumpRoots: " + e.Message); }
            log.Msg("======== end root inventory ========");
        }

        public static void SetHostLights(bool on, MelonLogger.Instance log)
        {
            int n = 0;
            if (on)
            {
                for (int i = 0; i < _hostLights.Count; i++)
                    try { if (_hostLights[i] != null) { _hostLights[i].enabled = true; n++; } } catch { }
                for (int i = 0; i < _hostSkyLights.Count; i++)
                    try { if (_hostSkyLights[i] != null) _hostSkyLights[i].interactsWithSky = true; } catch { }
                _hostLights.Clear(); _hostSkyLights.Clear();
                log?.Msg("[shell] host lights ON (" + n + ") -- the shipped level's own daylight "
                       + "is lighting our map again. Its GEOMETRY stays hidden.");
                return;
            }

            try
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                var all = UnityEngine.Object.FindObjectsOfType<Light>(true);
                Transform our = null;
                try { our = MapRuntime.Root != null ? MapRuntime.Root.transform : null; } catch { }
                for (int i = 0; i < (all != null ? all.Length : 0); i++)
                {
                    var li = all[i];
                    if (li == null || !li.enabled) continue;
                    if (OursLight(li)) continue;

                    try
                    {
                        if (KeepSceneHandle != 0
                            && li.gameObject.scene.handle == KeepSceneHandle) continue;
                    }
                    catch { }

                    try
                    {
                        if (string.Equals(li.gameObject.scene.name, "DontDestroyOnLoad",
                                          StringComparison.Ordinal)) continue;
                    }
                    catch { }
                    li.enabled = false;
                    _hostLights.Add(li);
                    try
                    {
                        var hda = li.GetComponent<UnityEngine.Rendering.HighDefinition
                                       .HDAdditionalLightData>();
                        if (hda != null && hda.interactsWithSky)
                        { hda.interactsWithSky = false; _hostSkyLights.Add(hda); }
                    }
                    catch { }
                    n++;
                }
            }
            catch (Exception e) { log?.Warning("[shell] host lights off: " + e.Message); }
            log?.Msg("[shell] host lights OFF (" + n + ") -- night is ours alone. "
                   + "Recorded, so '=' back to day switches every one of them on again.");
        }

        static readonly List<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>
            _dimmedSuns = new List<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>();
        static readonly List<float> _dimmedWas = new List<float>();

        static bool OursLight(Light li)
        {
            if (li == null) return true;
            try
            {
                var r = MapRuntime.Root;
                if (r != null && li.transform.IsChildOf(r.transform)) return true;
            }
            catch { }
            try
            {
                var lr = MapRuntime.LightingRoot;
                if (lr != null && li.transform.IsChildOf(lr)) return true;
            }
            catch { }
            return false;
        }

        public static string SoftenHostShadows(float dimmer, MelonLogger.Instance log)
        {
            dimmer = Mathf.Clamp01(dimmer);
            int n = 0;
            try
            {
                Transform our = null;
                try { our = MapRuntime.Root != null ? MapRuntime.Root.transform : null; } catch { }
                var all = UnityEngine.Object.FindObjectsOfType<Light>(true);
                for (int i = 0; i < (all != null ? all.Length : 0); i++)
                {
                    var li = all[i];
                    if (li == null) continue;
                    try { if (li.type != LightType.Directional) continue; } catch { continue; }
                    if (OursLight(li)) continue;
                    var hda = li.GetComponent<UnityEngine.Rendering.HighDefinition
                                   .HDAdditionalLightData>();
                    if (hda == null) continue;
                    if (!_dimmedSuns.Contains(hda))
                    { _dimmedSuns.Add(hda); try { _dimmedWas.Add(hda.shadowDimmer); } catch { _dimmedWas.Add(1f); } }
                    try { hda.shadowDimmer = dimmer; n++; } catch { }
                    try { hda.volumetricShadowDimmer = dimmer; } catch { }
                }
            }
            catch (Exception e) { log?.Warning("[shell] shadow dimmer: " + e.Message); }
            return "host sun shadows at " + dimmer.ToString("F2") + " on " + n
                 + " directional light(s). 1.00 is a pitch-black shadow, 0.00 is no shadow "
                 + "at all; the map ships at 0.45.";
        }

        static void RestoreSunDimmers()
        {
            for (int i = 0; i < _dimmedSuns.Count; i++)
                try { if (_dimmedSuns[i] != null) _dimmedSuns[i].shadowDimmer = _dimmedWas[i]; }
                catch { }
            _dimmedSuns.Clear(); _dimmedWas.Clear();
        }

        static readonly List<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData> _shadowSuns =
            new List<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>();
        static readonly List<bool> _shadowWasOverride = new List<bool>();
        static readonly List<int> _shadowWasRes = new List<int>();
        static readonly List<float> _shadowWasAngular = new List<float>();

        public static string TuneHostSunShadows(int res, float angularDeg, MelonLogger.Instance log)
        {
            int n = 0;
            string quality = "unreadable", maxRes = "unreadable";
            int cap = 0;
            try
            {
                var asset = UnityEngine.Rendering.HighDefinition.HDRenderPipeline.currentAsset;
                if (asset != null)
                {
                    var sp = asset.currentPlatformRenderPipelineSettings.hdShadowInitParams;
                    try { cap = sp.maxDirectionalShadowMapResolution; maxRes = cap.ToString(); } catch { }
                    try { quality = sp.directionalShadowFilteringQuality.ToString(); } catch { }
                }
            }
            catch (Exception e) { log?.Warning("[shell] shadow init params: " + e.Message); }
            if (cap > 0) res = Mathf.Min(res, cap);
            res = Mathf.Clamp(res, 256, 8192);
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<Light>(true);
                for (int i = 0; i < (all != null ? all.Length : 0); i++)
                {
                    var li = all[i];
                    if (li == null || !li.enabled) continue;
                    try { if (!li.gameObject.activeInHierarchy) continue; } catch { continue; }
                    try { if (li.type != LightType.Directional) continue; } catch { continue; }
                    if (OursLight(li)) continue;
                    try { if (li.shadows == LightShadows.None) continue; } catch { continue; }
                    var hda = li.GetComponent<UnityEngine.Rendering.HighDefinition
                                   .HDAdditionalLightData>();
                    if (hda == null) continue;
                    if (!_shadowSuns.Contains(hda))
                    {
                        _shadowSuns.Add(hda);
                        bool wasOv = false; int wasRes = 0; float wasAng = 0.5f;
                        try { wasOv = hda.shadowResolution.useOverride; } catch { }
                        try { wasRes = hda.shadowResolution.@override; } catch { }
                        try { wasAng = hda.angularDiameter; } catch { }
                        _shadowWasOverride.Add(wasOv); _shadowWasRes.Add(wasRes);
                        _shadowWasAngular.Add(wasAng);
                    }
                    try { hda.SetShadowResolution(res); } catch (Exception e)
                    { log?.Warning("[shell] SetShadowResolution: " + e.Message); }
                    if (angularDeg > 0f)
                    {
                        try { hda.angularDiameter = angularDeg; } catch { }

                        try { hda.filterSampleCount = 32; } catch { }
                        try { hda.blockerSampleCount = 24; } catch { }
                    }
                    n++;
                }
            }
            catch (Exception e) { log?.Warning("[shell] sun shadows: " + e.Message); }
            string readback = "-";
            try
            {
                if (_shadowSuns.Count > 0)
                    readback = _shadowSuns[0].shadowResolution.@override + " (override="
                             + _shadowSuns[0].shadowResolution.useOverride + ")";
            }
            catch { }
            return "host sun shadows on " + n + " directional light(s): resolution " + res
                 + " asked (asset ceiling " + maxRes + "), reads back " + readback
                 + "; directional filtering quality " + quality
                 + (string.Equals(quality, "High", StringComparison.OrdinalIgnoreCase)
                    ? ", so PCSS is compiled and angularDiameter " + angularDeg.ToString("F2")
                      + " deg sets the penumbra ('odg shadow soft <deg>' moves it live)"
                    : ", so this build has NO PCSS: the penumbra is fixed PCF and angularDiameter "
                      + "is inert here; resolution and cascades are the only shadow levers.");
        }

        static void RestoreSunShadows()
        {
            for (int i = 0; i < _shadowSuns.Count; i++)
            {
                var hda = _shadowSuns[i];
                if (hda == null) continue;
                try
                {
                    if (_shadowWasOverride[i]) hda.SetShadowResolution(_shadowWasRes[i]);
                    else hda.SetShadowResolutionOverride(false);
                }
                catch { }
                try { hda.angularDiameter = _shadowWasAngular[i]; } catch { }
            }
            _shadowSuns.Clear(); _shadowWasOverride.Clear(); _shadowWasRes.Clear();
            _shadowWasAngular.Clear();
        }

        static readonly List<Light> _tunedSuns = new List<Light>();
        static readonly List<Quaternion> _tunedRot = new List<Quaternion>();
        static readonly List<float> _tunedK = new List<float>();
        static readonly List<bool> _tunedUseK = new List<bool>();

        public static string RetuneHostSun(float elevation, float azimuth, float kelvin,
                                           MelonLogger.Instance log)
        {
            int n = 0;
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<Light>(true);
                for (int i = 0; i < (all != null ? all.Length : 0); i++)
                {
                    var li = all[i];
                    if (li == null || !li.enabled) continue;

                    try { if (!li.gameObject.activeInHierarchy) continue; } catch { continue; }
                    try { if (li.type != LightType.Directional) continue; } catch { continue; }
                    if (OursLight(li)) continue;
                    if (!_tunedSuns.Contains(li))
                    {
                        _tunedSuns.Add(li);
                        _tunedRot.Add(li.transform.rotation);
                        try { _tunedK.Add(li.colorTemperature); } catch { _tunedK.Add(6500f); }
                        try { _tunedUseK.Add(li.useColorTemperature); } catch { _tunedUseK.Add(false); }
                    }
                    try { li.transform.rotation = Quaternion.Euler(elevation, azimuth, 0f); } catch { }
                    try { li.useColorTemperature = true; li.colorTemperature = kelvin; } catch { }
                    try
                    {
                        var hda = li.GetComponent<UnityEngine.Rendering.HighDefinition
                                       .HDAdditionalLightData>();
                        if (hda != null)
                        {
                            try { hda.EnableColorTemperature(true); } catch { }
                            try { hda.SetColor(Color.white, kelvin); } catch { }

                            if (MapLoader.MapIdentity.VolumetricRays)
                            {
                                try { hda.affectsVolumetric = true; } catch { }
                                try { hda.volumetricDimmer = 1f; } catch { }
                                try { hda.volumetricShadowDimmer = 1f; } catch { }
                            }
                        }
                    }
                    catch { }
                    n++;
                }
            }
            catch (Exception e) { log?.Warning("[shell] host sun retune: " + e.Message); }
            return "host sun re-aimed on " + n + " directional light(s): elevation "
                 + elevation.ToString("F0") + ", azimuth " + azimuth.ToString("F0")
                 + ", " + kelvin.ToString("F0") + " K. The host keeps its exposure, its "
                 + "volumes and its sky; only the key light's angle and colour moved.";
        }

        public static void ForgetHostLights(MelonLogger.Instance log)
        {
            int n = _hostLights.Count + _dimmedSuns.Count + _tunedSuns.Count + _shadowSuns.Count;
            _hostLights.Clear(); _hostSkyLights.Clear();
            _dimmedSuns.Clear(); _dimmedWas.Clear();
            _tunedSuns.Clear(); _tunedRot.Clear(); _tunedK.Clear(); _tunedUseK.Clear();
            _shadowSuns.Clear(); _shadowWasOverride.Clear(); _shadowWasRes.Clear();
            _shadowWasAngular.Clear();
            if (n > 0)
                log?.Msg("[shell] forgot " + n + " recorded host light(s): their level is being "
                       + "unloaded, so restoring them would write through wrappers whose native "
                       + "objects are already gone.");
        }

        public static float HostSunLux()
        {
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<Light>(true);
                for (int i = 0; i < (all != null ? all.Length : 0); i++)
                {
                    var li = all[i];
                    if (li == null || !li.enabled) continue;
                    try { if (!li.gameObject.activeInHierarchy) continue; } catch { continue; }
                    try { if (li.type != LightType.Directional) continue; } catch { continue; }
                    if (OursLight(li)) continue;
                    var hda = li.GetComponent<UnityEngine.Rendering.HighDefinition
                                   .HDAdditionalLightData>();
                    if (hda == null) continue;
                    try { float v = hda.intensity; if (v > 1f) return v; } catch { }
                }
            }
            catch { }
            return 0f;
        }

        static void RestoreHostSuns()
        {
            for (int i = 0; i < _tunedSuns.Count; i++)
            {
                var li = _tunedSuns[i];
                if (li == null) continue;
                try { li.transform.rotation = _tunedRot[i]; } catch { }
                try { li.useColorTemperature = _tunedUseK[i]; li.colorTemperature = _tunedK[i]; } catch { }
            }
            _tunedSuns.Clear(); _tunedRot.Clear(); _tunedK.Clear(); _tunedUseK.Clear();
        }

        public static void RestoreShell(MelonLogger.Instance log)
        {
            RestoreSunDimmers();
            RestoreHostSuns();
            RestoreSunShadows();

            HostVeg.Restore(log);

            int c = 0;
            for (int i = 0; i < _hiddenColliders.Count; i++)
            {
                try { if (_hiddenColliders[i] != null) { _hiddenColliders[i].enabled = true; c++; } }
                catch { }
            }
            int t = 0;
            for (int i = 0; i < _hiddenTerrains.Count; i++)
            {
                try { if (_hiddenTerrains[i] != null) { _hiddenTerrains[i].enabled = true; t++; } }
                catch { }
            }
            int brg = 0;
            for (int i = 0; i < _hiddenBrg.Count; i++)
            {
                try { if (_hiddenBrg[i] != null) { _hiddenBrg[i].enabled = true; brg++; } }
                catch { }
            }
            if (brg > 0) log?.Msg($"  {brg} BRG vegetation registerer(s) re-enabled.");
            int L = 0;
            for (int i = 0; i < _hostLights.Count; i++)
            {
                try { if (_hostLights[i] != null) { _hostLights[i].enabled = true; L++; } }
                catch { }
            }
            _hostLights.Clear();
            for (int i = 0; i < _hostSkyLights.Count; i++)
            {
                try { if (_hostSkyLights[i] != null) _hostSkyLights[i].interactsWithSky = true; }
                catch { }
            }
            _hostSkyLights.Clear();

            int d = 0;
            for (int i = 0; i < _deactivatedRoots.Count; i++)
            {
                try { if (_deactivatedRoots[i] != null) { _deactivatedRoots[i].SetActive(true); d++; } }
                catch { }
            }

            int f = 0;
            for (int i = 0; i < _forcedOffRenderers.Count; i++)
            {
                try
                {
                    var rd = _forcedOffRenderers[i];
                    if (rd == null) continue;
                    rd.forceRenderingOff = false;
                    rd.enabled = i < _forcedOffWasEnabled.Count && _forcedOffWasEnabled[i];
                    f++;
                }
                catch { }
            }

            int lm = 0;
            for (int i = 0; i < _layerMoved.Count; i++)
            {
                try
                {
                    var g2 = _layerMoved[i];
                    if (g2 == null) continue;
                    g2.layer = i < _layerWas.Count ? _layerWas[i] : g2.layer;
                    lm++;
                }
                catch { }
            }
            if (lm > 0) log?.Msg($"  {lm} host object(s) put back on their original layer.");

            _hiddenColliders.Clear();
            _hiddenTerrains.Clear();
            _hiddenBrg.Clear();
            _deactivatedRoots.Clear();
            _forcedOffRenderers.Clear();
            _forcedOffWasEnabled.Clear();
            _layerMoved.Clear();
            _layerWas.Clear();

            if (c + t + d + f + L > 0)
                log.Msg($"shell restored: {d} roots reactivated, {f} force-off cleared, " +
                        $"{c} colliders, {t} terrains, {L} lights");
        }

        public static void SetOcclusionCulling(bool on, MelonLogger.Instance log)
        {
            try
            {
                var gm = Il2Cpp.GameManager.instance;
                if (gm == null) { log.Warning("GameManager.instance null"); return; }
                gm.SetCameraOcclusionCulling(on);
                log.Msg(on
                    ? "camera occlusion culling re-enabled"
                    : "camera occlusion culling DISABLED (stale bake was culling " +
                      "the ported map behind hidden occluders)");
            }
            catch (Exception e) { log.Warning("SetCameraOcclusionCulling: " + e.Message); }
        }

        public static int DisableDistanceCullers(MelonLogger.Instance log)
        {
            int n = 0;
            try
            {
                var g = Resources.FindObjectsOfTypeAll<Il2Cpp.GroupDistanceCuller>();
                if (g != null)
                    for (int i = 0; i < g.Length; i++)
                    {
                        if (g[i] == null || g[i].gameObject == null) continue;
                        if (!g[i].gameObject.scene.IsValid()) continue;
                        if (!g[i].enabled) continue;
                        g[i].enabled = false;
                        n++;
                    }
                var o = Resources.FindObjectsOfTypeAll<Il2Cpp.GameobjectOptimize>();
                if (o != null)
                    for (int i = 0; i < o.Length; i++)
                    {
                        if (o[i] == null || o[i].gameObject == null) continue;
                        if (!o[i].gameObject.scene.IsValid()) continue;
                        if (!o[i].enabled) continue;
                        o[i].enabled = false;
                        n++;
                    }
            }
            catch (Exception e) { log.Warning("distance cullers: " + e.Message); }
            if (n > 0) log.Msg($"disabled {n} host distance-cullers");
            return n;
        }

        static readonly List<Il2CppBRGInstancedRenderer.BRGRegisterer> _disabledBrg =
            new List<Il2CppBRGInstancedRenderer.BRGRegisterer>();

        public static int SuppressInstancedVegetation(MelonLogger.Instance log)
        {
            int n = 0;

            bool stripHere = false;
            try { stripHere = MapIdentity.OwnSky; } catch { }
            if (!stripHere)
            {
                log.Msg("host vegetation left rendering: this map borrows the shipped "
                      + "level's world and its trees are part of that look.");
                return 0;
            }
            try
            {
                var regs = Resources.FindObjectsOfTypeAll<Il2CppBRGInstancedRenderer.BRGRegisterer>();
                if (regs != null)
                    for (int i = 0; i < regs.Length; i++)
                    {
                        var r = regs[i];
                        if (r == null || r.gameObject == null) continue;
                        if (!r.gameObject.scene.IsValid()) continue;
                        try { r.SetRenderEnabled(false); _disabledBrg.Add(r); n++; }
                        catch (Exception e)
                        {
                            if (n == 0) log.Warning("SetRenderEnabled: " + e.Message);
                        }
                    }
            }
            catch (Exception e) { log.Warning("BRG registerers: " + e.Message); }
            log.Msg(n > 0
                ? $"suppressed {n} BRG registerer(s) -- host vegetation and the " +
                  "shadows it cast with no visible caster"
                : "no BRGRegisterer found");
            return n;
        }

        public static void RestoreInstancedVegetation()
        {
            for (int i = 0; i < _disabledBrg.Count; i++)
            {
                try { if (_disabledBrg[i] != null) _disabledBrg[i].SetRenderEnabled(true); }
                catch { }
            }
            _disabledBrg.Clear();
        }

        public static int RefreshCachedShadows(MelonLogger.Instance log)
        {
            int n = 0;
            try
            {
                var ups = Resources.FindObjectsOfTypeAll<Il2Cpp.OnDemandShadowMapUpdate>();
                if (ups != null)
                    for (int i = 0; i < ups.Length; i++)
                    {
                        var u = ups[i];
                        if (u == null || u.gameObject == null) continue;
                        if (!u.gameObject.scene.IsValid()) continue;
                        try { u.UpdateShadowMap(); n++; }
                        catch (Exception e)
                        {
                            if (n == 0) log.Warning("UpdateShadowMap: " + e.Message);
                        }
                    }
            }
            catch (Exception e) { log.Warning("shadow refresh: " + e.Message); }
            log.Msg(n > 0
                ? $"refreshed {n} cached shadow map(s) -- clears hidden host " +
                  "buildings burned into the atlas"
                : "no OnDemandShadowMapUpdate found (shadows may not be cached here)");
            return n;
        }

        public static void ForceOperationRoomOutdoor(MelonLogger.Instance log)
        {
            try
            {
                var loaders = Resources.FindObjectsOfTypeAll<Il2Cpp.OperationRoomLoader>();
                if (loaders == null || loaders.Length == 0)
                {
                    log.Warning("ForceOperationRoomOutdoor: no OperationRoomLoader in scene "
                              + "-- is the shell actually 'Operation Room'?");
                    return;
                }

                foreach (var l in loaders)
                {
                    if (l == null || l.gameObject == null || !l.gameObject.scene.IsValid())
                        continue;

                    bool indoorOn = false, outdoorOn = false;
                    try
                    {
                        var ia = l.IndoorsArea; var oa = l.OutdoorsArea;
                        if (ia != null) foreach (var go in ia) if (go != null && go.activeSelf) { indoorOn = true; break; }
                        if (oa != null) foreach (var go in oa) if (go != null && go.activeSelf) { outdoorOn = true; break; }
                    }
                    catch (Exception e) { log.Warning("reading Indoors/OutdoorsArea: " + e.Message); }

                    log.Msg($"OperationRoomLoader found: indoor active={indoorOn} outdoor active={outdoorOn}");

                    if (outdoorOn && !indoorOn)
                    {
                        log.Msg("already outdoor, leaving it alone");
                        continue;
                    }

                    try
                    {
                        l.OperationTrainingAreaSwitch();
                        log.Msg("called OperationTrainingAreaSwitch() to force outdoor");
                    }
                    catch (Exception e)
                    {
                        log.Warning("OperationTrainingAreaSwitch threw: " + e.Message);
                        continue;
                    }

                    bool indoorAfter = false, outdoorAfter = false;
                    try
                    {
                        var ia = l.IndoorsArea; var oa = l.OutdoorsArea;
                        if (ia != null) foreach (var go in ia) if (go != null && go.activeSelf) { indoorAfter = true; break; }
                        if (oa != null) foreach (var go in oa) if (go != null && go.activeSelf) { outdoorAfter = true; break; }
                    }
                    catch { }
                    if (outdoorAfter && !indoorAfter)
                        log.Msg("confirmed: outdoor is now active, indoor is not");
                    else
                        log.Warning($"UNEXPECTED after switch: indoor active={indoorAfter} "
                                  + $"outdoor active={outdoorAfter}. OperationTrainingAreaSwitch "
                                  + "may not be the indoor/outdoor toggle -- try Switch() instead, "
                                  + "or this scene's default was already outdoor and the call "
                                  + "just walked it back to indoor.");
                }
            }
            catch (Exception e) { log.Error("ForceOperationRoomOutdoor: " + e.Message); }
        }

        public static void ForgetHidden()
        {
            _hostSkyLights.Clear();

            _hostLights.Clear();
            _hiddenColliders.Clear();
            _hiddenTerrains.Clear();

            _deactivatedRoots.Clear();
            _forcedOffRenderers.Clear();
            _forcedOffWasEnabled.Clear();

            _layerMoved.Clear();
            _layerWas.Clear();
            _hiddenBrg.Clear();
            _boundsValid = false;
        }

        public static bool AnythingHidden =>
            _hiddenColliders.Count > 0
            || _forcedOffRenderers.Count > 0 || _deactivatedRoots.Count > 0;
    }
}
