// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(MapLoader.Main), "Map Loader", "2.0.0", "ArchD")]
[assembly: MelonGame(null, null)]

namespace MapLoader
{

    public class Main : MelonMod
    {

        public static MelonPreferences_Entry<float> HvtShare, HvtRadius;
        MelonPreferences_Entry<string> _mapsRoot;
        MelonPreferences_Entry<string> _mapNames;
        MelonPreferences_Entry<bool> _startAtNight;
        MelonPreferences_Entry<bool> _autoDoors;
        MelonPreferences_Entry<string> _doorMode;
        MelonPreferences_Entry<bool> _autoNav;
        MelonPreferences_Entry<int> _botIdle;
        MelonPreferences_Entry<bool> _coop;

        bool _doorsLocked = true;
        bool _navQueued;
        bool _aiPending;

        float _censusAt;
        bool _censusDone;
        int _censusRun;

        float _aiHoldUntil;

        static string DefaultMapsRoot()
        {
            try
            {
                var root = MelonLoader.Utils.MelonEnvironment.GameRootDirectory;
                if (!string.IsNullOrEmpty(root))
                {
                    var shipped = System.IO.Path.Combine(root, "Mods", "MapLoader_Assets");
                    if (System.IO.Directory.Exists(shipped)) return shipped;
                }
            }
            catch { }
            return @"D:\OperatorHSR\maps";
        }

        internal static bool SystemEnabled;

        internal static Main Instance;

        public static bool OursRunning()
        {
            try { return Instance != null && Instance.HostIsRunningOurMap(); }
            catch { return false; }
        }

        public override void OnInitializeMelon()
        {
            Instance = this;
            var cfg = MelonPreferences.CreateCategory("MapLoader");

            var enabled = cfg.CreateEntry("MapSystemEnabled", true);

            HvtShare  = cfg.CreateEntry("HvtShare", 0.6f, description: "fraction of the OPFOR wave placed on the spawn markers nearest the HVT (0 = spread over the whole map)");
            HvtRadius = cfg.CreateEntry("HvtRadius", 60f, description: "metres from the marked HVT spot within which a spawn marker counts as the compound");

            var reEnabled = cfg.CreateEntry("MapSystemOnV2", false);
            if (!reEnabled.Value)
            {
                if (!enabled.Value)
                {
                    enabled.Value = true;
                    LoggerInstance.Msg("[mapid] MapSystemEnabled forced true (was false). "
                                     + "One-time: the kill switch shipped false and a saved "
                                     + "value outlives a changed default. Set it false again "
                                     + "afterwards and it stays false.");
                }
                reEnabled.Value = true;
                try { cfg.SaveToFile(false); } catch { }
            }

            SystemEnabled = enabled.Value;
            if (!SystemEnabled)
            {
                LoggerInstance.Msg("map system OFF (MapSystemEnabled=false). No board rows, "
                                 + "no runtime map build, no host vegetation or lighting "
                                 + "changes -- the game's own missions are untouched. "
                                 + "Set MapSystemEnabled=true in MelonPreferences to restore.");
                return;
            }

            _mapsRoot = cfg.CreateEntry("MapsRoot", DefaultMapsRoot());

            _mapNames = cfg.CreateEntry("MapNames", "Farmstead");

            MergeMapName(cfg, "MapsMergedLrmV1", "LittleRocketMan");
            MergeMapName(cfg, "MapsMergedTrenchFootV1", "Trenches");
            MergeMapName(cfg, "MapsMergedTeamBuildV1", "Killhouse");
            MergeMapName(cfg, "MapsMergedHoaV1", "VirginiaBeach");
            MergeMapName(cfg, "MapsMergedFarmsteadBackV1", "Farmstead");

            ResetMapNames(cfg, "MapsResetFiveV1",
                "Farmstead,LittleRocketMan,Trenches,Killhouse,Rig");

            _devKeys = cfg.CreateEntry("EnableDevKeys", false);
            _startAtNight = cfg.CreateEntry("StartAtNightV2", true);

            DoorSentry.Trace   = cfg.CreateEntry("DoorSentryTrace",   false).Value;

            DoorSync.Enabled   = cfg.CreateEntry("DoorSyncEnabled",   true).Value;

            HostVeg.DropEnabled = cfg.CreateEntry("HostTreeDrop", true).Value;
            DoorListener.Trace = cfg.CreateEntry("DoorListenerTrace", false).Value;

            _autoDoors = cfg.CreateEntry("AutoPlaceDoors", true);

            _doorMode = cfg.CreateEntry("DoorMode", "relocate");
            _autoNav = cfg.CreateEntry("AutoBuildNav", true);

            _botIdle = cfg.CreateEntry("BotIdleState", 1);
            MapAI.IdleMode = _botIdle.Value;

            _coop = cfg.CreateEntry("BuildMapOnEveryMissionV2", false);

            var coopReset = cfg.CreateEntry("CoopOverrideResetAfterV12", false);
            if (!coopReset.Value)
            {
                if (_coop.Value)
                    LoggerInstance.Warning(
                        "clearing BuildMapOnEveryMissionV2, which v12 saved as true. "
                        + "That saved value is why the compound was being built into "
                        + "every shipped mission. Clients no longer need it: they "
                        + "detect our mission from the replicated operation name.");
                _coop.Value = false;
                coopReset.Value = true;
                try { cfg.SaveToFile(); } catch { }
            }

            SceneBundle.Init(LoggerInstance);
            SceneProbe.Init(LoggerInstance);
            SceneLighting.Init(LoggerInstance);
            MapScene.Init(LoggerInstance);
            MapDoors.Init(LoggerInstance);
            MapExfil.Init(LoggerInstance);
            MapBarriers.Init(LoggerInstance);
            DoorSpawnRegistry.Init(LoggerInstance);
            DoorCensus.Init(LoggerInstance);
            DoorSync.Init(LoggerInstance);
            MapHVT.Init(LoggerInstance);
            MapWater.Init(LoggerInstance);
            MapGlass.Init(LoggerInstance);
            MapSkeleton.Init(LoggerInstance);
            MapBody.Init(LoggerInstance);

            CustomMaps.Init(LoggerInstance);
            MapIdentity.Init(LoggerInstance, _mapNames != null ? _mapNames.Value : "Farmstead");
            MapAI.Init(LoggerInstance);

            PlayerBody.Init(LoggerInstance);
            SpawnTruth.Init(LoggerInstance);
            MapPatrols.Init(LoggerInstance);
            MapRuntime.Init(LoggerInstance);
            HostFoliage.Init(LoggerInstance);
            HostVeg.Init(LoggerInstance);
            GroundSkirt.Init(LoggerInstance);
            StairStep.Init(LoggerInstance);
            StairFill.Init(LoggerInstance);
            PlateFill.Init(LoggerInstance);
            BlackHawk.Init(LoggerInstance);
            MapUnstick.Init(LoggerInstance);
            MapDump.Init(LoggerInstance);
            SpawnGuard.Init(LoggerInstance);
            ShellSwap.Install(HarmonyInstance, LoggerInstance);

            SpawnGuard.Install(HarmonyInstance, LoggerInstance);

            CustomOps.Init(LoggerInstance);
            MapPreview.Init(LoggerInstance);
            MapInfils.Init(LoggerInstance);
            DoorListener.Init(LoggerInstance);
            DoorWiring.Init(LoggerInstance);
            DoorReport.Init(LoggerInstance);
            LookProbe.Init(LoggerInstance);
            FakeBounce.Init(LoggerInstance);
            MapLights.Init(LoggerInstance);
            MapAmbience.Init(LoggerInstance);
            NightArt.Init(LoggerInstance);
            DayArt.Init(LoggerInstance);
            LoadVeil.Init(LoggerInstance);
            DoorSentry.Init(LoggerInstance);
            DoorTouch.Init(LoggerInstance);
            BoardState.Init(LoggerInstance);
            ShotMatrix.Init(LoggerInstance);

            MapScene.Install(HarmonyInstance, LoggerInstance);
            MapAI.Install(HarmonyInstance, LoggerInstance);
            CustomOps.Install(HarmonyInstance, LoggerInstance);
            DoorTouch.Install(HarmonyInstance, LoggerInstance);
#if !ODG_PUBLIC

            OdgConsole.Install(HarmonyInstance, LoggerInstance);
#endif
            DoorSentry.Install(HarmonyInstance, LoggerInstance);
            FreeCamProbe.Install(HarmonyInstance, LoggerInstance);
            FallGuard.Install(HarmonyInstance, LoggerInstance);
            ZoneGuard.Install(HarmonyInstance, LoggerInstance);
            SpawnShield.Install(HarmonyInstance, LoggerInstance);
            SpawnSlots.Install(HarmonyInstance, LoggerInstance);
            BulletWatch.Install(HarmonyInstance, LoggerInstance);
            BlackHawk.Install(HarmonyInstance, LoggerInstance);
            RimWatch.Init(LoggerInstance);

            StaleFiles.Report(LoggerInstance);
            LaptopTrace.Init(LoggerInstance);
            LaptopTrace.Install(HarmonyInstance, LoggerInstance);

            MapDoors.Mode = (_doorMode.Value ?? "relocate").Trim().ToLowerInvariant();
            LoggerInstance.Msg("  doors: mode=" + MapDoors.Mode
                             + "  (relocate = the proven route; clone = 100 doors, under investigation)");

            SceneBundle.ScanAndPreload(_mapsRoot.Value);

            MapScene.CustomSceneLoaded += OnCustomMap;

            MapScene.TransitionStarting += OnTransitionStart;

            try
            {

                _activeSceneChangedHook =
                    (UnityEngine.Events.UnityAction<UnityEngine.SceneManagement.Scene,
                                                    UnityEngine.SceneManagement.Scene>)
                    ((UnityEngine.SceneManagement.Scene from,
                      UnityEngine.SceneManagement.Scene to)
                        => OnActiveSceneChanged(from, to));
                UnityEngine.SceneManagement.SceneManager.activeSceneChanged
                    += _activeSceneChangedHook;
                LoggerInstance.Msg("[latch] activeSceneChanged trigger registered");
            }
            catch (Exception e)
            {
                LoggerInstance.Warning("[latch] activeSceneChanged subscription failed ("
                    + e.Message + "); the latch still arms from the ServerChangeScene "
                    + "prefix and the scene-unload callback, which cover the crashing path.");
            }

            try
            {
                var gameRoot = System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath);
                var vf = System.IO.Path.Combine(gameRoot, "Mods", ".installed_release");
                LoggerInstance.Msg("pack version: " + (System.IO.File.Exists(vf)
                    ? System.IO.File.ReadAllText(vf).Trim()
                    : "<no .installed_release; launcher has not stamped this install>"));
            }
            catch { }

            try
            {
                var logs = System.IO.Path.Combine(
                    MelonLoader.Utils.MelonEnvironment.GameRootDirectory,
                    "MelonLoader", "Logs");
                string newest = null;
                var newestAt = DateTime.MinValue;
                if (System.IO.Directory.Exists(logs))
                    foreach (var f in System.IO.Directory.GetFiles(logs, "*.log"))
                    {
                        var at = System.IO.File.GetLastWriteTime(f);
                        if (at > newestAt) { newestAt = at; newest = f; }
                    }
                if (newest != null)
                    LoggerInstance.Msg("[tick] previous session log: "
                        + System.IO.Path.GetFileName(newest) + " (last write "
                        + newestAt.ToString("yyyy-MM-dd HH:mm:ss") + ")");
            }
            catch { }

            LoggerInstance.Msg("ready. Map data: " + MapRuntime.BundleDir);
            if (!System.IO.Directory.Exists(MapRuntime.BundleDir))
                LoggerInstance.Warning("MAP DATA NOT FOUND at " + MapRuntime.BundleDir
                    + " -- the ODG OPS tab will work, but the map cannot be built. "
                    + "Install the map data pack, or set MapLoader/BundleDir.");
            LoggerInstance.Msg("  F9        build the map        Shift+F9  tear it down");
            LoggerInstance.Msg("  F7        build ODG OPS tab    Shift+F7  dump laptop UI");
            LoggerInstance.Msg("  Shift+F6  laptop tracer on/off  (bare F6 is the Fireteam Roles panel)");
            LoggerInstance.Msg("  BACKSPACE door readout on/off   Shift+BACKSPACE full dump of the nearest door");
            LoggerInstance.Msg("            (touching any door is logged automatically -- no key needed)");
            LoggerInstance.Msg("  =         day / night     [ ] night exposure   , / moon lux   - moon angle");
            LoggerInstance.Msg("  DELETE    place doors          Shift+DEL cycle door yaw");
            LoggerInstance.Msg("  F5        what am I looking at   Shift+F5  scan for flat-colour surfaces");
            LoggerInstance.Msg("  ARROWS    ground brightness: UP/DOWN coarse, LEFT/RIGHT fine");
            LoggerInstance.Msg("  F4        flip door hinge side (180)");
            LoggerInstance.Msg("  F2        bounce on/off   Shift+F2 less bounce   F3  more bounce");
            LoggerInstance.Msg("  Shift+F4  cinematic grade on/off");
            LoggerInstance.Msg("  Shift+=   kill EVERY light this mod owns (are the spokes ours?)");
            LoggerInstance.Msg("  Shift+[ ] foliage draw distance (frame rate vs how far the greenery goes)");
            LoggerInstance.Msg("  ,         MARK: stamp this moment into the diagnostics record");
            LoggerInstance.Msg("  Ctrl+, / . moon dimmer/brighter   +Shift  fog thinner/thicker");
            LoggerInstance.Msg("  F10       rebuild navigation   Shift+F10  does the navmesh run through our walls?");
            LoggerInstance.Msg("  F11       spawn the AI wave into the map");
            LoggerInstance.Msg("  F8        report state         Shift+F8  spawns + lighting");
            LoggerInstance.Msg("  Ctrl+\\    dump the bundle registry");
            LoggerInstance.Msg("  CO-OP: BuildMapOnEveryMission = " + (_coop.Value ? "ON" : "off")
                             + "  (everyone in a shared session needs it ON, plus the map data)");
            LoggerInstance.Msg("  (F-keys are exclusive to this mod. AirborneInfil still holds");
            LoggerInstance.Msg("   Insert, Home and the keypad; HSR holds End. Avoid those three.)");
        }

        void OnCustomMap(UnityEngine.SceneManagement.Scene scene)
        {
            LoggerInstance.Msg("=== custom map '" + scene.name + "' is live ===");

            bool wantNight = SceneLighting.WantDay ? false : _startAtNight.Value;
            if (wantNight && MapIdentity.Current != null && !MapIdentity.Current.NightSafe)
            {
                wantNight = false;
                LoggerInstance.Msg("[light] '" + MapIdentity.Current.Name + "' starts in DAY: "
                                 + "it has interiors and our night rig is a moon with no local "
                                 + "lights, so night here means pitch black indoors. Press = "
                                 + "for night anyway.");
            }
            SafeRun("lighting", () => SceneLighting.Apply(wantNight));
            SafeRun("ai", () => MapAI.Apply("map load"));

            if (_autoDoors.Value || _autoNav.Value) _navQueued = true;
        }

        static bool _transitionCleanupDone;

        static string _lastInitScene;

        static UnityEngine.Events.UnityAction<UnityEngine.SceneManagement.Scene,
            UnityEngine.SceneManagement.Scene> _activeSceneChangedHook;

        float _latchSince, _latchNote;

        bool _pendingSceneChange;

        void OnTransitionStart()
        {
            _pendingSceneChange = true;
            TransitionCleanup();
        }

        void TransitionCleanup()
        {
            if (_transitionCleanupDone) { Crumb("transition cleanup already done"); return; }
            _transitionCleanupDone = true;
            Crumb("transition cleanup at scene-change START (scene still alive)");

            string active = null;
            try { active = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; }
            catch { }

            bool sceneIsBeingReplaced = _pendingSceneChange;
            if (!string.IsNullOrEmpty(MapRuntime.HostSceneName)
                && MapRuntime.OwnsHiddenState(active))
            {
                if (sceneIsBeingReplaced)
                {
                    Crumb("  restore SKIPPED: '" + (active ?? "?") + "' is being replaced, so "
                        + "its objects die with it. ~80k host-renderer writes declined.");

                    SafeRun("transition restore (survives scene)",
                            () => HostVeg.Restore(LoggerInstance));
                }
                else
                {
                    SafeRun("transition restore", () => MapDoors.RestoreRelocated());
                    SafeRun("transition restore exfil", () => MapExfil.Restore());
                    SafeRun("transition restore barriers", () => MapBarriers.Restore());
                    SafeRun("transition restore", () => DoorWiring.RestoreEnabled());
                    SafeRun("transition restore", () => ShellSwap.RestoreShell(LoggerInstance));
                }
            }

            SafeRun("transition probe reset", SceneProbe.Reset);

            SafeRun("transition forget", () => ShellSwap.ForgetHostLights(LoggerInstance));
            SafeRun("transition forget", ShellSwap.ForgetHidden);
            SafeRun("transition forget", DoorSentry.Reset);
            SafeRun("transition forget", NavForge.Abort);
            SafeRun("transition forget", LoadVeil.Forget);
            SafeRun("transition forget", FakeBounce.Forget);
            SafeRun("transition forget", SkyRig.Forget);
            SafeRun("transition forget", NightArt.Forget);
            SafeRun("transition forget", MapLights.Forget);
            SafeRun("transition forget", MapAmbience.Forget);
            SafeRun("transition forget", ApvGate.Forget);
            SafeRun("transition forget", ShotMatrix.Forget);
            SafeRun("transition forget", MeshFactory.ForgetGroundMats);

            SafeRun("transition forget", MeshFactory.ForgetDonor);
            SafeRun("transition forget", HostFoliage.Reset);
            SafeRun("transition forget", HostVeg.Reset);
            SafeRun("transition forget", GroundSkirt.Reset);

            SafeRun("stair step restore", StairStep.Restore);
            SafeRun("unstick forget", MapUnstick.Forget);
            SafeRun("transition forget", SpawnTruth.Forget);
            SafeRun("transition forget", DoorWiring.ResetForNewLevel);
            SafeRun("transition forget", DoorSpawnRegistry.ForgetStray);
            SafeRun("transition forget", DoorTouch.ResetForNewLevel);
            SafeRun("transition forget", () => DoorSync.ResetForNewLevel("scene change"));
            SafeRun("transition forget", () => BlackHawk.ResetForNewLevel("scene change"));
            SafeRun("transition forget", MapDoors.ResetForNewLevel);
            SafeRun("transition forget", MapPreview.OnSceneGone);

            SafeRun("transition forget", CustomOps.OnSceneGone);

            SafeRun("transition forget", MapRuntime.ResetForNewLevel);
        }

        void OnActiveSceneChanged(UnityEngine.SceneManagement.Scene from,
                                  UnityEngine.SceneManagement.Scene to)
        {
            try
            {
                string toName = null;
                try { toName = to.name; } catch { }

                if (!string.IsNullOrEmpty(toName)
                    && string.Equals(toName, _lastInitScene, StringComparison.Ordinal))
                {
                    Crumb("active scene -> '" + toName + "' (already initialized; not latching)");
                    return;
                }

                if (!string.IsNullOrEmpty(toName)
                    && string.Equals(toName, MapRuntime.SceneName, StringComparison.Ordinal))
                {
                    Crumb("active scene -> '" + toName + "' (our own map scene; not latching, "
                        + "because nothing would ever initialise it to clear the latch)");
                    return;
                }

                MapScene.LatchUp("active scene -> '" + (toName ?? "?") + "'");
            }
            catch (Exception e) { LoggerInstance.Error("active scene changed: " + e.Message); }
        }

        public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
        {
            if (!SystemEnabled) return;
            Crumb("scene UNLOADED '" + sceneName + "'");

            try
            {
                if (string.Equals(UnityEngine.SceneManagement.SceneManager
                        .GetActiveScene().name, sceneName, StringComparison.Ordinal))
                    MapScene.LatchUp("active scene '" + sceneName + "' unloaded");
            }
            catch { }

            if (_transitionCleanupDone)
            {
                SafeRun("scene unloaded", CustomOps.OnSceneGone);
                Crumb("  unload-time cleanup skipped: it ran at transition start");
                return;
            }

            SafeRun("forget host lights", () => ShellSwap.ForgetHostLights(LoggerInstance));

            SafeRun("scene unloaded", () => CustomOps.OnSceneGone());

            SafeRun("host foliage reset", HostFoliage.Reset);
            SafeRun("host vegetation reset", HostVeg.Reset);
            SafeRun("ground skirt reset", GroundSkirt.Reset);
            SafeRun("stair step forget", StairStep.Forget);
            SafeRun("unstick forget", MapUnstick.Forget);

            SafeRun("spawn truth reset", SpawnTruth.Forget);

            if (!string.IsNullOrEmpty(MapRuntime.HostSceneName)
                && MapRuntime.OwnsHiddenState(sceneName))
            {
                SafeRun("scene unloaded", () => MapDoors.RestoreRelocated());
                SafeRun("scene unloaded exfil", () => MapExfil.Restore());
                SafeRun("scene unloaded barriers", () => MapBarriers.Restore());

                if (!string.IsNullOrEmpty(MapRuntime.HostSceneName))
                    SafeRun("scene unloaded", () => DoorWiring.RestoreEnabled());
                SafeRun("scene unloaded", () => ShellSwap.RestoreShell(LoggerInstance));
            }

            SafeRun("scene unloaded", () => ShellSwap.ForgetHidden());
            SafeRun("scene unloaded", () => DoorWiring.ResetForNewLevel());
            SafeRun("scene unloaded", () => DoorSpawnRegistry.ForgetStray());
            SafeRun("scene unloaded", () => DoorTouch.ResetForNewLevel());
            SafeRun("scene unloaded", () => MapPreview.OnSceneGone());

            if (MapScene.TransitionLatch)
            {
                SafeRun("scene unloaded", DoorSentry.Reset);
                SafeRun("scene unloaded", NavForge.Abort);
                SafeRun("scene unloaded", LoadVeil.Forget);
                SafeRun("scene unloaded", MapDoors.ResetForNewLevel);
                SafeRun("scene unloaded", MapRuntime.ResetForNewLevel);
            }
        }

        static void Crumb(string what)
        {
            try { Melon<Main>.Logger.Msg("[crumb] " + what); } catch { }
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            if (!SystemEnabled) return;
            _pendingSceneChange = false;
            Crumb("scene INITIALIZED '" + sceneName + "' (idx " + buildIndex + ")");

            _lastInitScene = sceneName;
            _transitionCleanupDone = false;
            MapScene.LatchDown("scene '" + sceneName + "' initialized");

            SafeRun("shot matrix", () => ShotMatrix.OnSceneInitialized(sceneName));

            try
            {
                float t0;
                if (_loadedAt.TryGetValue(sceneName, out t0))
                {
                    _loadedAt.Remove(sceneName);
                    float gapMs = (Time.realtimeSinceStartup - t0) * 1000f;
                    Crumb("[gap] '" + sceneName + "' Loaded -> Initialized = "
                        + gapMs.ToString("F0") + " ms. Anything built inside that window "
                        + "runs during the level's own initialisation.");
                }
                else
                {
                    Crumb("[gap] '" + sceneName + "' initialised with no Loaded stamp, so "
                        + "Initialized really did come first for this one.");
                }
            }
            catch { }

        }

        static bool IsOurAdditiveWorld(string sceneName)
        {
            try
            {
                var want = MapIdentity.AdditiveSceneName;
                return !string.IsNullOrEmpty(want)
                    && string.Equals(sceneName, want, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {

            if (!IsOurAdditiveWorld(sceneName) && !IsMenuScene(sceneName))
            {
                try
                {
                    var rv = UnityEngine.Rendering.ProbeReferenceVolume.instance;
                    var cb = rv != null ? rv.currentBakingSet : null;

                    if (cb != null && cb.name != null
                        && MapIdentity.IsAnyAdditiveSceneSet(cb.name))
                    {
                        var sc = UnityEngine.SceneManagement.SceneManager
                                     .GetSceneByName(sceneName);
                        if (sc.IsValid() && sc.isLoaded)
                        {
                            LoggerInstance.Msg("[map] probe baking set was still '"
                                + cb.name + "' from the additive mission; swapping to '"
                                + sceneName + "' so this level's probes load.");
                            MapRuntime.SwapProbeSetTo(sc, "host level took over", true);
                        }
                    }
                }
                catch { }
            }

            if (IsOurAdditiveWorld(sceneName))
            {

                try
                {
                    var sc = UnityEngine.SceneManagement.SceneManager
                                 .GetSceneByName(sceneName);
                    if (sc.IsValid())
                        MapRuntime.NoteAdditiveSceneLoaded(sc.handle);
                }
                catch { }
                LoggerInstance.Msg("[map] '" + sceneName + "' is OUR additive world, "
                    + "not a new host level -- no rebuild, no reset.");
                return;
            }
            if (!SystemEnabled) return;
            Crumb("scene loaded '" + sceneName + "' (idx " + buildIndex + ") -- if the "
                + "log stops here, the fault is in this method, not in the map build");

            _loadedAt[sceneName] = Time.realtimeSinceStartup;

            if (MapScene.TransitionLatch)
            {
                LoggerInstance.Warning("[latch] still up at scene LOADED -- "
                    + "OnSceneWasInitialized did not run for '" + sceneName
                    + "'. Dropping it here so the mod is not muted.");
                _transitionCleanupDone = false;
                MapScene.LatchDown("scene loaded (late)");
            }
            SafeRun("scene loaded", () => MapScene.NotifySceneLoaded(sceneName));

            SafeRun("door registry", () => DoorSpawnRegistry.EnsureForSceneDoors(
                "scene '" + sceneName + "' loaded"));
            Crumb("  MapScene notified");

            SafeRun("spawn guard", () => SpawnGuard.OnSceneLoaded(sceneName));
            Crumb("  spawn guard armed");

            if (IsMenuScene(sceneName))
            {
                SafeRun("scene loaded", () => CustomOps.OnLobbyReturn());

                SafeRun("stamp clear", CustomOps.ClearStampAtMissionEnd);
            }

            if (sceneName != MapRuntime.SceneName)
            {
                Crumb("  new host level: resetting per-level state");
                MapRuntime.ResetForNewLevel();
                SceneLighting.WantDay = CustomOps.LaunchWantsDay;
                CustomOps.LaunchWantsDay = false;
                MapAI.ResetForNewLevel();
                OdgConsole.ResetForNewLevel();
                MapDoors.ResetForNewLevel();
                MapHVT.ResetForNewLevel();
                MapSkeleton.ResetForNewLevel();
                MapBody.ResetForNewLevel();
                _voidFired = false;
                _gateStuckSince = 0f; _gateForced = false;

                SafeRun("scene loaded", () => CustomOps.LaunchConsumed());
                _navQueued = false;
                _aiPending = false;
                Crumb("  per-level state reset");
            }

            bool coop = _coop != null && _coop.Value;
            bool ours = false;
            try
            {
                ours = (Il2CppMirror.NetworkServer.active
                        || Il2CppMirror.NetworkClient.active)
                    && HostIsRunningOurMap();
                if (ours)
                {

                    var rep = ReplicatedOpName();
                    SceneLighting.WantDay = rep != null
                        && rep.IndexOf("@day", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch { }
            if ((coop || ours) && sceneName != MapRuntime.SceneName && !IsMenuScene(sceneName))
            {
                if (!MapRuntime.QueueOnNextScene)
                    LoggerInstance.Msg(ours
                        ? "mission stamp '" + ReplicatedOpName()
                        + "' matches; building the map on this machine"
                        : "co-op override is ON, so the map is being built into this "
                        + "mission whatever it is. If this is a shipped mission, that "
                        + "is the override doing it: set BuildMapOnEveryMissionV2 = "
                        + "false in UserData\\MelonPreferences.cfg.");
                MapRuntime.QueueOnNextScene = true;
            }

            _coopWatchUntil = 0f;
            try
            {
                if (!ours && !coop
                    && Il2CppMirror.NetworkClient.active
                    && !Il2CppMirror.NetworkServer.active
                    && sceneName != MapRuntime.SceneName
                    && !IsMenuScene(sceneName))
                {
                    _coopWatchUntil = Time.realtimeSinceStartup + 90f;
                    _coopWatchNext = 0f;
                    LoggerInstance.Msg("[coop] client: no mission stamp at scene "
                        + "load (replicated name '" + ReplicatedOpName() + "'); "
                        + "watching 90 s in case it arrives late");
                }
            }
            catch { }

            if (MapRuntime.QueueOnNextScene && sceneName != MapRuntime.SceneName)
            {
                MapRuntime.QueueOnNextScene = false;

                LoggerInstance.Msg("host level '" + sceneName + "' loaded; building the map");
                SafeRun("map begin", () => MapRuntime.Begin());
            }
        }

        static MelonPreferences_Entry<bool> _allowDayToggle;
        static bool AllowDayToggle()
        {
            try
            {
                if (_allowDayToggle == null)
                {
                    var cfg = MelonPreferences.GetCategory("MapLoader")
                           ?? MelonPreferences.CreateCategory("MapLoader");
                    _allowDayToggle = cfg.GetEntry<bool>("AllowDayToggle")
                        ?? cfg.CreateEntry("AllowDayToggle", false,
                             description: "Let '=' switch a night-only map to the host level's "
                                        + "daylight. Off because that daylight was never tuned "
                                        + "for these maps and reads as a blown-out sun.");
                }
                return _allowDayToggle.Value;
            }
            catch { return false; }
        }

        static readonly System.Collections.Generic.Dictionary<string, float> _loadedAt
            = new System.Collections.Generic.Dictionary<string, float>();

        string ReplicatedOpName()
        {
            try
            {
                var gmn = Il2Cpp.GameManagerNetwork.instance;
                if (gmn == null) return "<no GameManagerNetwork>";

                string a = null, b = null;
                try { a = gmn.selectedOperation; } catch { }
                try { b = gmn.UserOperationName; } catch { }
                if (!string.IsNullOrEmpty(b)) return b;
                if (!string.IsNullOrEmpty(a)) return a;
                return "<empty>";
            }
            catch { return "<unreadable>"; }
        }

        bool HostIsRunningOurMap()
        {
            try
            {
                var gmn = Il2Cpp.GameManagerNetwork.instance;
                if (gmn == null) return false;

                string a = null, b = null;
                try { a = gmn.selectedOperation; } catch { }
                try { b = gmn.UserOperationName; } catch { }
                if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b)) return false;

                string names = _mapNames != null ? _mapNames.Value : null;
                if (string.IsNullOrEmpty(names)) return false;

                foreach (var raw in names.Split(','))
                {
                    var n = raw.Trim();
                    if (n.Length == 0) continue;

                    if (!string.IsNullOrEmpty(a) &&
                        a.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)
                        return MapIdentity.Select(n);
                    if (!string.IsNullOrEmpty(b) &&
                        b.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)
                        return MapIdentity.Select(n);
                }
            }
            catch { }
            return false;
        }

        static float _gateStuckSince;
        static bool _gateForced;
        static float _gateNext;
        static float _gateWaitLogAt;

        public static void ResetGateRescue()
        {
            _gateStuckSince = 0f;
            _gateForced = false;
        }

        void RestartWatchdog()
        {
            float armed = CustomOps.RestartArmedAt;
            if (armed < 0f) return;
            float now = Time.realtimeSinceStartup;
            if (now - armed < 4f) return;
            CustomOps.RestartArmedAt = -1f;
            try
            {
                if (MapRuntime.Done) return;
                if (!HostIsRunningOurMap()) return;
                if (IsMenuScene(UnityEngine.SceneManagement.SceneManager
                                .GetActiveScene().name)) return;
                LoggerInstance.Warning("[restart] four seconds after RESTART MISSION the map "
                    + "is still not built, so the restart did not reload the scene. Building "
                    + "it in place now. The veil is already up and lifts when the player is "
                    + "standing on our floor, exactly as it does on a fresh launch.");
                MapRuntime.Init(LoggerInstance);
                MapRuntime.Begin();
            }
            catch (Exception e) { LoggerInstance.Warning("[restart] watchdog: " + e.Message); }
        }

        void GateRescueTick()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _gateNext) return;
            _gateNext = now + 1f;
            try
            {
                if (!Il2CppMirror.NetworkServer.active) { _gateStuckSince = 0f; return; }
                if (!MapRuntime.Done) { _gateStuckSince = 0f; return; }
                if (!HostIsRunningOurMap()) { _gateStuckSince = 0f; return; }

                var gmn = Il2Cpp.GameManagerNetwork.instance;
                if (gmn == null) { _gateStuckSince = 0f; return; }

                bool open = false;
                try { open = gmn.AllPlayersLoaded; } catch { return; }
                if (open) { _gateStuckSince = 0f; _gateForced = false; return; }

                if (_gateStuckSince == 0f) { _gateStuckSince = now; return; }
                if (_gateForced || now - _gateStuckSince < 10f) return;

                int ready = -1; try { ready = gmn.ReadyPlayers; } catch { }
                int conns = -1; try { conns = Il2CppMirror.NetworkServer.connections.Count; } catch { }
                if (ready >= 0 && conns > 0 && ready < conns)
                {
                    _gateStuckSince = now;
                    if (now >= _gateWaitLogAt)
                    {
                        _gateWaitLogAt = now + 30f;
                        LoggerInstance.Msg("[gate] the ready gate is WAITING, not hung: " + ready + " of "
                            + conns + " player(s) have reported loaded. Not running "
                            + "Server_AllPlayersLoaded while someone is still loading; it would "
                            + "respawn everyone already standing without their weapons.");
                    }
                    return;
                }

                int total, waiting;
                bool everyone = SpawnShield.EveryoneIsDeployed(out total, out waiting);
                int deployed = total - waiting;
                if (everyone || deployed > 0)
                {
                    _gateForced = true;
                    LoggerInstance.Msg("[gate] the ready gate reads unsatisfied, but " + deployed + " of "
                        + total + " player(s) already have a body out in the level, so the "
                        + "deploy has happened and there is nothing to rescue. NOT running "
                        + "Server_AllPlayersLoaded: it respawns everyone and a respawned body "
                        + "arrives with no weapons.");
                    return;
                }

                _gateForced = true;
                LoggerInstance.Warning("[gate] the ready gate sat unsatisfied for 10 s on our "
                    + "mission with the map fully built (ReadyPlayers=" + ready + "). This is "
                    + "the restart hang; running the game's own all-players-loaded step.");

                bool ran = false;
                try
                {
                    var mode = Il2Cpp.GameMode.singleton;
                    if (mode != null) { mode.Server_AllPlayersLoaded(); ran = true; }
                }
                catch (Exception e) { LoggerInstance.Warning("[gate] Server_AllPlayersLoaded: " + e.Message); }

                if (ran) LoggerInstance.Msg("[gate] ran Server_AllPlayersLoaded: players respawned "
                                          + "and moved to their insertion point.");
                else
                {

                    LoggerInstance.Warning("[gate] no GameMode.singleton; opening the gate alone "
                                         + "so nobody stays stuck on the loading screen.");
                    try { gmn.NetworkAllPlayersLoaded = true; } catch { }
                }
            }
            catch { }
        }

        static float _coopWatchUntil, _coopWatchNext;

        void CoopWatchTick()
        {
            if (_coopWatchUntil <= 0f) return;
            float now = Time.realtimeSinceStartup;
            if (now < _coopWatchNext) return;
            _coopWatchNext = now + 1f;

            if (MapRuntime.Done || MapRuntime.Building) { _coopWatchUntil = 0f; return; }

            bool client = false;
            try { client = Il2CppMirror.NetworkClient.active && !Il2CppMirror.NetworkServer.active; }
            catch { }
            if (!client) { _coopWatchUntil = 0f; return; }

            if (now >= _coopWatchUntil)
            {
                _coopWatchUntil = 0f;
                LoggerInstance.Msg("[coop] client: watch window closed with no "
                    + "mission stamp; last replicated name '" + ReplicatedOpName()
                    + "'. If the host IS in the compound, that value is the clue.");
                return;
            }

            if (!HostIsRunningOurMap()) return;

            _coopWatchUntil = 0f;
            LoggerInstance.Msg("[coop] client: mission stamp '" + ReplicatedOpName()
                + "' arrived after the scene; building the map now");
            SafeRun("map begin", () => MapRuntime.Begin());
        }

        static float _voidNext;
        static bool _voidFired;

        void VoidWatchTick()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _voidNext) return;
            _voidNext = now + 2f;

            if (_voidFired || MapRuntime.Done || MapRuntime.Building) return;
            try
            {
                if (!Il2CppMirror.NetworkServer.active
                    && !Il2CppMirror.NetworkClient.active) return;
            }
            catch { return; }

            string scene = null;
            try { scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; }
            catch { }
            if (scene != MapRuntime.SceneName) return;

            _voidFired = true;
            LoggerInstance.Warning("[void] the active scene is '" + scene + "' with "
                + "nothing built in it; building the map now so the floor exists");
            SafeRun("map begin", () => MapRuntime.Begin());
        }

        float _beat;

        public override void OnLateUpdate()
        {
            SafeRun("black hawk late", BlackHawk.LateTick);
        }

        public override void OnGUI()
        {
            if (!SystemEnabled) return;

            if (MapScene.TransitionLatch) return;

            try { LoadVeil.DrawGUI(); } catch { }
            try { OdgConsole.DrawMissingMapWarning(); } catch { }
            try { OdgConsole.DrawVramWarning(); } catch { }
            try { OdgConsole.Draw(); } catch { }
        }

        public override void OnUpdate()
        {
            if (!SystemEnabled) return;

            if (MapScene.TransitionLatch)
            {
                float lnow = Time.realtimeSinceStartup;
                if (_latchSince == 0f) { _latchSince = lnow; _latchNote = lnow + 5f; }
                if (lnow >= _latchNote)
                {
                    _latchNote = lnow + 5f;
                    LoggerInstance.Msg("[latch] holding (" + (MapScene.LatchReason ?? "?")
                        + ", " + (lnow - _latchSince).ToString("F0") + " s)");
                }
                if (lnow - _latchSince > 120f)
                {
                    LoggerInstance.Warning("[latch] up for 120 s with no scene "
                        + "initialize; forcing it down so the mod is not muted for the "
                        + "rest of the session. Reason was: "
                        + (MapScene.LatchReason ?? "?"));
                    _transitionCleanupDone = false;
                    MapScene.LatchDown("watchdog failsafe");
                }
                return;
            }
            _latchSince = 0f;

            if (Time.realtimeSinceStartup - _beat > 10f)
            {
                _beat = Time.realtimeSinceStartup;
                LoggerInstance.Msg("[alive] scene='"
                    + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
                    + "' laptops=" + CustomOps.AllLaptops().Count
                    + " mapBuilt=" + MapRuntime.Done
                    + "  (F9 build map, F7 laptop tab)");
            }

            if (MapRuntime.Building) SafeRun("map step", MapRuntime.Step);

            SafeRun("spawn shield", SpawnShield.Tick);

            SafeRun("spawn slots", SpawnSlots.Tick);

            SafeRun("bullet watch", BulletWatch.Tick);

            SafeRun("door sync", DoorSync.Tick);

            SafeRun("black hawk", BlackHawk.Tick);
            if (!BlackHawk.Riding) SafeRun("map teleport", MapRuntime.TeleportTick);

            if (!BlackHawk.Riding) SafeRun("void watch", MapRuntime.VoidWatchTick);

            SafeRun("hvt", MapHVT.SpawnIfMarked);
            SafeRun("hvt watch", MapHVT.Tick);
            SafeRun("body upkeep", MapBody.Tick);

            SafeRun("skel", MapSkeleton.Tick);
            SafeRun("bounce", FakeBounce.Tick);
            SafeRun("art", NightArt.Tick);
            SafeRun("dayart", DayArt.Tick);

            SafeRun("apv gate", ApvGate.Tick);

            SafeRun("lights roll", MapLights.Tick);

            SafeRun("ambience", MapAmbience.Tick);
            SafeRun("veil", LoadVeil.Tick);

            SafeRun("stamp watch", CustomOps.StampWatchTick);
            SafeRun("restart", RestartWatchdog);
            SafeRun("cull", MapRuntime.CullingTick);

            SafeRun("additive", MapRuntime.AdditiveTick);

            SafeRun("host foliage", () => HostFoliage.Tick(MapRuntime.Done));

            SafeRun("host vegetation", () => HostVeg.Tick(MapRuntime.Done));

            SafeRun("host sweep", MapRuntime.HostSweepTick);

            SafeRun("ambient guard", MapRuntime.AmbientGuardTick);

            SafeRun("ground skirt", () => GroundSkirt.Tick(MapRuntime.Done));

            SafeRun("stair step", StairStep.Tick);
            if (!BlackHawk.Riding) SafeRun("unstick", MapUnstick.Tick);
            SafeRun("spawn guard tick", SpawnGuard.Tick);

            TickPerf.MaybeReport(LoggerInstance);
            SafeRun("sentry", DoorSentry.Tick);
            SafeRun("bundle tick", SceneBundle.Tick);
            SafeRun("probe tick", SceneProbe.Tick);
            SafeRun("scene sync", MapScene.SyncFromBundles);
            SafeRun("op sync", SyncOps);
            SafeRun("coop watch", CoopWatchTick);
            if (!BlackHawk.Riding) SafeRun("void watch", VoidWatchTick);
            SafeRun("gate rescue", GateRescueTick);
            SafeRun("stamp", CustomOps.StampRetry);

            SafeRun("shot matrix", ShotMatrix.Tick);
            if (NavForge.Filling) SafeRun("nav step", () => NavForge.StepTiles(LoggerInstance));

            if (_navQueued && Il2CppMirror.NetworkServer.active
                && MapRuntime.AdditiveReady)
            {

                _navQueued = false;

                SafeRun("black hawk arm", BlackHawk.OnMapBuilt);

                if (_autoNav.Value) { SafeRun("nav", BuildNav); _aiPending = true; }
                else SafeRun("ai spawn", () => MapAI.SpawnNow());
                if (_autoDoors.Value) SafeRun("doors", () => MapDoors.Place());
                SafeRun("door census", () => DoorCensus.Arm());

                SafeRun("exfil zones", () => MapExfil.Place(MapRuntime.SceneName));
            }
            else if (_navQueued && !Il2CppMirror.NetworkServer.active
                     && Il2CppMirror.NetworkClient.active)
            {

                _navQueued = false;
                SafeRun("nav clean", () => NavForge.DropStaleGraphsOnClient(LoggerInstance));
                if (_autoDoors.Value) SafeRun("doors", () => MapDoors.Place());
                SafeRun("door census", () => DoorCensus.Arm());

                SafeRun("exfil zones", () => MapExfil.Place(MapRuntime.SceneName));
                SafeRun("black hawk arm", BlackHawk.OnMapBuilt);
            }

            SafeRun("floater watch", MapAI.FloaterTick);

            if (MapRuntime.Done && !_censusDone)
            {
                if (_censusAt == 0f)
                {
                    _censusAt = Time.realtimeSinceStartup + 45f;
                    MapAI.MarkLevelReady();
                }
                else if (Time.realtimeSinceStartup > _censusAt)
                {

                    _censusRun++;
                    MapAI.DumpBotCensus(_censusRun == 1
                        ? "automatic, 45 s after build"
                        : "automatic, " + (_censusRun == 2 ? "4 min" : "9 min") + " in");
                    if (_censusRun >= 3) _censusDone = true;
                    else _censusAt = Time.realtimeSinceStartup
                                   + (_censusRun == 1 ? 240f : 300f);
                }
            }
            else if (!MapRuntime.Done && (_censusDone || _censusAt != 0f))
            {
                _censusDone = false; _censusAt = 0f; _censusRun = 0;
                _aiHoldUntil = 0f;
                MapAI.ResetFloaterWatch();
            }

            if (_aiPending && !NavForge.Filling && _aiHoldUntil == 0f)
            {
                int others = 0;
                try { others = Il2CppMirror.NetworkServer.connections.Count - 1; } catch { }
                if (others > 0)
                {
                    float grace = Mathf.Clamp(MapRuntime.BuildSeconds * 0.5f, 10f, 45f);
                    _aiHoldUntil = Time.realtimeSinceStartup + grace;
                    LoggerInstance.Msg("[mapai] holding the wave " + grace.ToString("F0")
                        + " s for " + others + " other machine(s) still building their copy. "
                        + "Our own build took " + MapRuntime.BuildSeconds.ToString("F0")
                        + " s, and a bot spawned before they have a floor arrives with "
                        + "nothing under it.");
                }
                else _aiHoldUntil = -1f;
            }

            bool holdingWave = _aiPending
                            && _aiHoldUntil > 0f
                            && Time.realtimeSinceStartup < _aiHoldUntil;

            SafeRun("exfil watch", () => OdgConsole.Tick());

            SafeRun("gunner purge", () => MapAI.PurgeGunnersTick());

            SafeRun("sky verify", () => SkyRig.Tick());

            SafeRun("shade receipt", () => MapRuntime.ShadeReportTick());

            if (_aiPending && !NavForge.Filling && !holdingWave)
            {
                _aiPending = false;
                SafeRun("ai spawn", () => MapAI.SpawnNow());
            }

            if (DevDown(KeyCode.F8)) SafeRun("map scene", () =>
            {
                if (Shift) { MapScene.DumpState(); MapAI.DumpState(); SceneLighting.DumpState(); HostFoliage.Report(); HostVeg.Report(); GroundSkirt.Report(); SpawnGuard.Report(); return; }
                string first = null;
                foreach (var s in MapScene.Known) { first = s; break; }
                if (first == null)
                {
                    LoggerInstance.Warning("no bundle scenes registered. Check a .mapbundle "
                        + "exists under " + _mapsRoot.Value + " and finished loading (Ctrl+\\).");
                    return;
                }
                LoggerInstance.Msg("switching mission to '" + first + "'");
                MapScene.Go(first);
            });

            if (DevDown(KeyCode.F9)) SafeRun("map", () =>
            {
                LoggerInstance.Msg("*** F9 pressed ***");
                if (Shift) { MapRuntime.Teardown(); return; }
                if (MapRuntime.Done) { MapRuntime.DumpState(); return; }
                MapRuntime.Init(LoggerInstance);
                MapRuntime.Begin();
            });

            if (DevDown(KeyCode.LeftBracket) && Shift)
                SafeRun("tune", () => MapRuntime.ScaleFoliageCull(0.8f));

            else if (DevDown(KeyCode.LeftBracket))  SafeRun("tune", () => {
                if (SceneLighting.IsNight) MapRuntime.AdjustNightEV(-0.5f);
                else MapRuntime.AdjustDayEV(-0.5f); });
            if (DevDown(KeyCode.RightBracket) && Shift)
                SafeRun("tune", () => MapRuntime.ScaleFoliageCull(1.25f));
            else if (DevDown(KeyCode.RightBracket)) SafeRun("tune", () => {
                if (SceneLighting.IsNight) MapRuntime.AdjustNightEV(+0.5f);
                else MapRuntime.AdjustDayEV(+0.5f); });
            if (DevDown(KeyCode.F2))
                SafeRun("tune", () => { if (Shift) FakeBounce.ScaleStrength(0.7f); else FakeBounce.Toggle(); });
            if (DevDown(KeyCode.F3) && !Shift) SafeRun("tune", () => FakeBounce.ScaleStrength(1.4f));

            if (Ctrl && DevDown(KeyCode.Comma))
                SafeRun("tune", () => { if (Shift) MapRuntime.ScaleFog(0.75f); else MapRuntime.ScaleMoonLux(0.7f); });

            if (Ctrl && (DevDown(KeyCode.Period) || DevDown(KeyCode.Slash)))
                SafeRun("tune", () => { if (Shift) MapRuntime.ScaleFog(1.35f); else MapRuntime.ScaleMoonLux(1.4f); });
            if (DevDown(KeyCode.Minus))  SafeRun("tune", () => MapRuntime.StepMoonElevation(4f));

            if (Ctrl && DevDown(KeyCode.UpArrow))
                SafeRun("tune", () => MeshFactory.SetGroundBrightness(
                    MeshFactory.GroundAlbedoScale + 0.05f, LoggerInstance));
            if (Ctrl && DevDown(KeyCode.DownArrow))
                SafeRun("tune", () => MeshFactory.SetGroundBrightness(
                    MeshFactory.GroundAlbedoScale - 0.05f, LoggerInstance));
            if (Ctrl && DevDown(KeyCode.RightArrow))
                SafeRun("tune", () => MeshFactory.SetGroundBrightness(
                    MeshFactory.GroundAlbedoScale + 0.01f, LoggerInstance));
            if (Ctrl && DevDown(KeyCode.LeftArrow))
                SafeRun("tune", () => MeshFactory.SetGroundBrightness(
                    MeshFactory.GroundAlbedoScale - 0.01f, LoggerInstance));

            if (DevDown(KeyCode.F3) && Shift && MapRuntime.Done)
                SafeRun("doors", () => { _doorsLocked = !_doorsLocked;
                                         MapDoors.SetAllLocked(_doorsLocked); });

            if (DevDown(KeyCode.F4))
                SafeRun("art", () => { if (Shift) NightArt.StepLevel(); else MapDoors.FlipHinges(); });

            if (DevDown(KeyCode.F5)) SafeRun("look", () =>
            {
                if (Shift) LookProbe.ScanSuspicious(); else LookProbe.Fire();
            });

            if (DevDown(KeyCode.Equals) && Shift)
                SafeRun("tune", () => MapRuntime.ToggleOurLights());
            else if (DevDown(KeyCode.Equals)) SafeRun("day/night", () => {
                LoggerInstance.Msg("*** = pressed ***");

                bool hasRealDay = false;
                try { hasRealDay = MapIdentity.OwnSky; } catch { }
                if (!hasRealDay)
                {
                    LoggerInstance.Msg("[light] day refused: this map ships a night rig only. "
                        + "Its 'day' would be the host level's own sun, which this map was "
                        + "never lit for. Set MapLoader/AllowDayToggle true to re-enable.");
                    if (!AllowDayToggle()) return;
                }
                SceneLighting.Toggle(); });

            if (DevDown(KeyCode.Delete)) SafeRun("doors", () =>
            {
                LoggerInstance.Msg("*** DELETE pressed ***");
                if (Shift)
                {

                    MapDoors.RotateAllDoors(90f);
                    return;
                }
                MapDoors.Place();
            });

            if (DevDown(KeyCode.F11)) SafeRun("ai spawn", () => {
                LoggerInstance.Msg("*** F11 pressed ***");

                if (Shift) { MapAI.DeepDump(); return; }
                MapAI.DumpState();
                MapAI.SpawnNow();
            });

            if (DevDown(KeyCode.F10)) SafeRun("nav", () => {
                LoggerInstance.Msg("*** F10 pressed ***");

                if (!Il2CppMirror.NetworkServer.active && !Shift)
                {
                    LoggerInstance.Warning("[nav] not the host, so there is nothing here to "
                        + "build a graph for. The host owns navigation; this machine only "
                        + "draws the bots it is sent. Shift+F10 still reports what is here.");
                    return;
                }

                if (Shift) { NavForge.Diagnose(LoggerInstance); return; }
                BuildNav(); });

            if (DevDown(KeyCode.F7)) SafeRun("ops tab", () =>
            {
                LoggerInstance.Msg("*** F7 pressed ***");
                if (CustomOps.AllLaptops().Count == 0)
                    LoggerInstance.Warning("no MissionLaptop here. The tab can only be "
                        + "built where a laptop exists (the ready room), not in a menu.");
                if (Shift) CustomOps.DumpUi();
                else CustomOps.Build();
            });

            SafeRun("trace tick", LaptopTrace.Tick);

            if (DevDown(KeyCode.F6)
                && (DevHeld(KeyCode.LeftShift) || DevHeld(KeyCode.RightShift)))
                SafeRun("trace", () => {
                    LoggerInstance.Msg("*** Shift+F6 pressed ***"); LaptopTrace.Toggle(); });

            if (Dev) SafeRun("door keys", () => DoorListener.HandleInput());
            DoorListener.Tick();

            DoorWiring.Tick();

            SafeRun("stray doors", DoorSpawnRegistry.TickStray);

            if (DevDown(KeyCode.Backslash)) SafeRun("registry", () =>
            {
                if (Ctrl) SceneBundle.DumpState();
                else MapScene.DumpState();
            });
        }

        void SyncOps()
        {
            bool added = false;

            foreach (var scene in MapIdentity.Names())
            {
                var name = scene.Trim();
                if (name.Length == 0 || _knownOps.Contains(name)) continue;
                _knownOps.Add(name);
                added = true;

                if (!string.Equals(name, "Farmstead", StringComparison.OrdinalIgnoreCase))
                {
                    CustomOps.Register(BuildMission(name));
                    continue;
                }

                CustomOps.Register(new CustomOp
                {
                    Id = name,
                    SceneName = name,
                    Title = "DUKE'S OPS",
                    Duration = "30-45 MIN",
                    ThreatLevel = "HIGH",
                    Description =
                        "ODG Commanding Officer: \"Listing promised a rustic farm stay "
                      + "with quiet neighbours. They're quiet because an arms broker "
                      + "runs the place as a transfer point, and the host doesn't take "
                      + "cancellations.\"\n\nLocation: Abbottabad, "
                      + "Pakistan\n\nFarmhouse, barn cluster and outbuildings at the "
                      + "centre, crop fields all round with long sightlines and thin "
                      + "cover. They hold the buildings in depth and they watch the "
                      + "approaches.\n\nHostile Presence: armed caretakers, numbers set "
                      + "by user\nCivilian Presence: none. Booking was "
                      + "cancelled\nInfiltration: three insertion points, all "
                      + "ground\nExtraction: same as infiltration\nObjective: clear "
                      + "every structure and leave a one-star review.",
                    AreaOfOperation = name.ToUpperInvariant(),
                    MinAI = 1,
                    MaxAI = 15,
                    PrepSeconds = 120,

                    Infils = new[] { "ALPHA 1", "BRAVO 1", "CHARLIE 1" },
                });
            }

            if (added) _tabDirty = true;

            if (_tabDirty && Time.realtimeSinceStartup - _lastTabTry > 2f)
            {
                _lastTabTry = Time.realtimeSinceStartup;
                if (CustomOps.Build()) _tabDirty = false;
            }
        }

        void ResetMapNames(MelonPreferences_Category cfg, string marker, string mapNames)
        {
            var done = cfg.CreateEntry(marker, false);
            if (done.Value) return;

            var before = _mapNames.Value;
            if (!string.Equals(before, mapNames, StringComparison.Ordinal))
            {
                _mapNames.Value = mapNames;
                LoggerInstance.Msg("[mapid] MapNames reset to '" + mapNames + "' (was '"
                                 + before + "'). One-time, because this release ships "
                                 + "only these maps and a saved list outlives a changed "
                                 + "default. Edit MapNames afterwards and it stays edited.");
            }
            done.Value = true;
            try { cfg.SaveToFile(false); } catch { }
        }

        void MergeMapName(MelonPreferences_Category cfg, string marker, string mapName)
        {
            var merged = cfg.CreateEntry(marker, false);
            if (merged.Value) return;

            var have = new System.Collections.Generic.List<string>();
            foreach (var raw in (_mapNames.Value ?? "").Split(','))
            {
                var n = raw.Trim();
                if (n.Length > 0) have.Add(n);
            }
            if (have.Count == 0) have.Add("Farmstead");

            bool present = false;
            foreach (var n in have)
                if (string.Equals(n, mapName, StringComparison.OrdinalIgnoreCase))
                    present = true;

            if (!present)
            {
                have.Add(mapName);
                _mapNames.Value = string.Join(",", have.ToArray());
                LoggerInstance.Msg("[mapid] added '" + mapName + "' to MapNames (now '"
                                 + _mapNames.Value + "'). One-time merge, because a "
                                 + "saved value outlives a raised default -- this is the "
                                 + "only way an install that has run before ever sees a "
                                 + "new map.");
            }
            merged.Value = true;
            try { cfg.SaveToFile(false); } catch { }
        }

        static CustomOp BuildMission(string name)
        {

            if (CustomMaps.IsCustom(name))
            {
                CustomMaps.Meta meta;
                var plain = PlainMission(name);
                return CustomMaps.TryLoadMeta(name, out meta) ? CustomMaps.Apply(plain, meta) : plain;
            }
            return ShippedMission(name);
        }

        static CustomOp PlainMission(string name)
        {
            return new CustomOp
            {
                Id = name,
                SceneName = name,
                Title = "DUKE'S OPS",
                PackageName = name.ToUpperInvariant(),
                Duration = "30-45 MIN",
                ThreatLevel = "HIGH",
                Description = "No briefing has been written for '" + name + "'. Put a map.json "
                            + "next to its manifest.bin with a title and a briefing.",
                AreaOfOperation = name.ToUpperInvariant(),
                MinAI = 1,
                MaxAI = 15,
                PrepSeconds = 120,
                Infils = new[] { "ALPHA 1", "BRAVO 1", "CHARLIE 1" },
            };
        }

        static CustomOp ShippedMission(string name)
        {
            if (string.Equals(name, "PowderPalace", StringComparison.OrdinalIgnoreCase))
            {
                return new CustomOp
                {
                    Id = name,
                    SceneName = name,
                    Title = "DUKE'S OPS",

                    PackageName = "SHOCK & AWE STRIP MALL",
                    Duration = "30-45 MIN",
                    ThreatLevel = "HIGH",
                    OpType = "DIRECT ACTION",
                    Description =
                        "ODG Commanding Officer: \"Shopfronts, a gym nobody uses, and a "
                      + "bank that hasn't taken a deposit in nine years. Underneath it "
                      + "is a metro station the city closed and never filled in, and "
                      + "that's what they actually bought.\"\n\nLocation: Sofia, "
                      + "Bulgaria\n\nHalf a kilometre of retail with offices stacked "
                      + "above and tunnels below. They hold the interiors and the "
                      + "platform, so the street is where they'll let you "
                      + "walk.\n\nHostile Presence: armed, numbers set by "
                      + "user\nCivilian Presence: none\nInfiltration: four insertion "
                      + "points, one of them through the tunnel\nExtraction: same as "
                      + "infiltration\nObjective: clear the block from the platform up.",

                    AreaOfOperation = "SHOCK & AWE STRIP MALL",

                    MinAI = 1,
                    MaxAI = 32,
                    PrepSeconds = 120,

                    Infils = new[] { "ALPHA 1", "BRAVO 1", "DELTA 1", "ALPHA 2" },
                };
            }

            if (string.Equals(name, "Rig", StringComparison.OrdinalIgnoreCase))
            {
                return new CustomOp
                {
                    Id = name,
                    SceneName = name,
                    Title = "DUKE'S OPS",
                    PackageName = "THE OFFSHORE ACCOUNT",
                    Duration = "30-45 MIN",
                    ThreatLevel = "HIGH",
                    OpType = "DIRECT ACTION",
                    Description =
                        "ODG Commanding Officer: \"The platform stopped producing "
                      + "eleven months ago and the operator kept paying the crew "
                      + "manifest anyway. Nobody on that manifest is a "
                      + "welder.\"\n\nLocation: North Sea, 180 km east of "
                      + "Aberdeen\n\nFour decks on pontoons, fifty metres of stairwell "
                      + "from waterline to helipad. The catwalks are open grating, so "
                      + "the deck above you can see your feet and shoot through "
                      + "them.\n\nHostile Presence: armed crew, numbers set by "
                      + "user\nCivilian Presence: none. The real crew went home in "
                      + "October\nInfiltration: eight insertion points, all "
                      + "ground\nExtraction: same as infiltration\nObjective: clear all "
                      + "four decks and shut the platform down.",
                    AreaOfOperation = "THE OFFSHORE ACCOUNT",

                    MinAI = 1,
                    MaxAI = 28,
                    PrepSeconds = 120,

                    Infils = new[] { "ALPHA 1", "BRAVO 1", "DELTA 1", "ALPHA 2",
                                     "BRAVO 2", "DELTA 2", "ALPHA 3", "BRAVO 3" },
                };
            }

            if (string.Equals(name, "Creek", StringComparison.OrdinalIgnoreCase))
            {
                return new CustomOp
                {
                    Id = name,
                    SceneName = name,
                    Title = "DUKE'S OPS",
                    PackageName = "DEAD MAN'S CREEK",
                    Duration = "30-45 MIN",
                    ThreatLevel = "MODERATE",
                    OpType = "DIRECT ACTION",
                    Description =
                        "ODG Commanding Officer: \"A played-out mine at the top of the "
                      + "valley, a house at the bottom that stopped being a family "
                      + "home, and one road between them. You can't get close without "
                      + "walking three hundred metres of timber.\"\n\nLocation: Coeur "
                      + "d'Alene, Idaho\n\nWooded from the creek to the ridges with "
                      + "thirty metres of fall either side. They're spread across four "
                      + "positions rather than sat in one, so no single room ends "
                      + "this.\n\nHostile Presence: armed, numbers set by "
                      + "user\nCivilian Presence: none\nInfiltration: three insertion "
                      + "points, all ground\nExtraction: same as "
                      + "infiltration\nObjective: clear the valley end to end.",
                    AreaOfOperation = "DEAD MAN'S CREEK",

                    MinAI = 1,
                    MaxAI = 24,
                    PrepSeconds = 120,

                    Infils = new[] { "ALPHA 1", "BRAVO 1", "DELTA 1" },
                };
            }

            if (string.Equals(name, "SmallTown", StringComparison.OrdinalIgnoreCase))
            {
                return new CustomOp
                {
                    Id = name,
                    SceneName = name,
                    Title = "DUKE'S OPS",
                    PackageName = "NO PLACE LIKE HOME",
                    Duration = "30-45 MIN",
                    ThreatLevel = "HIGH",
                    OpType = "DIRECT ACTION",
                    Description =
                        "ODG Commanding Officer: \"Someone built a whole town to "
                      + "practise fighting in. Someone else moved into it and started "
                      + "practising for real.\"\n\nLocation: Copehill Down, Salisbury "
                      + "Plain\n\nA hundred and eighty metres of breeze block sat in a "
                      + "wooded bowl. Most buildings have an upper floor and a cellar "
                      + "that opens onto the street, so you're clearing in three "
                      + "dimensions the whole way.\n\nHostile Presence: entrenched, "
                      + "numbers set by user\nCivilian Presence: none. The town was "
                      + "never real\nInfiltration: three insertion points, all "
                      + "ground\nExtraction: same as infiltration\nObjective: clear the "
                      + "town building by building.",
                    AreaOfOperation = "NO PLACE LIKE HOME",

                    MinAI = 1,
                    MaxAI = 20,
                    PrepSeconds = 120,

                    Infils = new[] { "ALPHA 1", "BRAVO 1", "DELTA 1" },
                };
            }

            if (string.Equals(name, "Backroad", StringComparison.OrdinalIgnoreCase))
            {
                return new CustomOp
                {
                    Id = name,
                    SceneName = name,
                    Title = "DUKE'S OPS",
                    PackageName = "THE SCENIC ROUTE",
                    Duration = "30-45 MIN",
                    ThreatLevel = "MODERATE",
                    OpType = "DIRECT ACTION",
                    Description =
                        "ODG Commanding Officer: \"Trucks have been running up a "
                      + "logging road that isn't on any permit. There's a yard at the "
                      + "top with more wear on the turning circle than a legal cut "
                      + "would leave.\"\n\nLocation: Olympic Peninsula, "
                      + "Washington\n\nThree hundred metres of forest with forty metres "
                      + "of climb through the switchbacks. The road is the only open "
                      + "sky here, and they hold the treeline above it.\n\nHostile "
                      + "Presence: armed, numbers set by user\nCivilian Presence: none. "
                      + "Loggers don't work at gunpoint\nInfiltration: three insertion "
                      + "points, all ground\nExtraction: same as "
                      + "infiltration\nObjective: take the yard and close the route.",
                    AreaOfOperation = "THE SCENIC ROUTE",

                    MinAI = 1,
                    MaxAI = 20,
                    PrepSeconds = 120,

                    Infils = new[] { "ALPHA 1", "BRAVO 1", "DELTA 1" },
                };
            }

            if (string.Equals(name, "Trenches", StringComparison.OrdinalIgnoreCase))
            {
                return new CustomOp
                {
                    Id = name,
                    SceneName = name,
                    Title = "DUKE'S OPS",
                    PackageName = "TRENCH FOOT",
                    Duration = "25-40 MIN",
                    ThreatLevel = "HIGH",
                    OpType = "DIRECT ACTION",
                    Description =
                        "ODG Commanding Officer: \"Somebody cut a full trench network "
                      + "out here. Firing steps, dugouts, revetted corners and "
                      + "communication runs back to a reserve line. They're still in "
                      + "it.\"\n\nLocation: Kharkiv Oblast, Ukraine\n\nSightlines "
                      + "run the length of a cut and stop dead at every traverse, and "
                      + "grenades come round bends you can't see. Ninety metres east "
                      + "through the timber there's a walled compound giving the "
                      + "orders.\n\nHostile Presence: dug in, numbers set by "
                      + "user\nCivilian Presence: none\nInfiltration: two insertion "
                      + "points, both back in the treeline\nExtraction: same as "
                      + "infiltration\nObjective: clear the trench system end to end, "
                      + "then take the compound.",
                    AreaOfOperation = "TRENCH FOOT",

                    MinAI = 1,
                    MaxAI = 30,
                    PrepSeconds = 120,
                    Infils = new[] { "ALPHA 1", "BRAVO 1" },
                };
            }

            if (string.Equals(name, "Killhouse", StringComparison.OrdinalIgnoreCase))
            {
                return new CustomOp
                {
                    Id = name,
                    SceneName = name,
                    Title = "DUKE'S OPS",
                    PackageName = "TEAM BUILDING EXERCISE",
                    Duration = "15-30 MIN",
                    ThreatLevel = "HIGH",
                    OpType = "DIRECT ACTION",
                    Description =
                        "ODG Commanding Officer: \"Purpose-built shoothouse under a "
                      + "steel roof. Doorways that funnel you, corners you can't clear "
                      + "from the threshold, catwalks over the top. The occupants kept "
                      + "the layout and changed the curriculum.\"\n\nLocation: Moyock, "
                      + "North Carolina\n\nClose work in a box. Every room connects to "
                      + "two others, no light gets in except through the doors you "
                      + "open, and everyone inside stays inside.\n\nHostile Presence: "
                      + "entrenched, numbers set by user\nCivilian Presence: "
                      + "none\nInfiltration: two insertion points, opposite "
                      + "approaches\nExtraction: same as infiltration\nObjective: clear "
                      + "the shoothouse room by room. Attendance is mandatory.",
                    AreaOfOperation = "TEAM BUILDING EXERCISE",
                    MinAI = 1,
                    MaxAI = 18,
                    PrepSeconds = 120,
                    Infils = new[] { "ALPHA 1", "BRAVO 1" },
                };
            }

            if (string.Equals(name, "VirginiaBeach", StringComparison.OrdinalIgnoreCase))
            {
                return new CustomOp
                {
                    Id = name,
                    SceneName = name,
                    Title = "DUKE'S OPS",
                    PackageName = "HOA MEETING",
                    Duration = "20-35 MIN",
                    ThreatLevel = "MODERATE",
                    OpType = "DIRECT ACTION",
                    Description =
                        "ODG Commanding Officer: \"Fenced lot, one structure, and a "
                      + "property line somebody cares about a great deal. The dispute "
                      + "has moved past strongly worded letters.\"\n\nLocation: "
                      + "Virginia Beach, Virginia\n\nThe fence lines channel you "
                      + "whether you want them to or not, and the structure in the "
                      + "middle is the only thing on this lot that stops a bullet. The "
                      + "screening just hides you.\n\nHostile Presence: dispersed, "
                      + "numbers set by user\nCivilian Presence: none\nInfiltration: "
                      + "one insertion point, ground\nExtraction: same as "
                      + "infiltration\nObjective: clear the lot and the structure. "
                      + "Bring the minutes.",
                    AreaOfOperation = "HOA MEETING",
                    MinAI = 1,
                    MaxAI = 20,
                    PrepSeconds = 120,

                    Infils = new[] { "ALPHA 1" },
                };
            }

            if (string.Equals(name, "LittleRocketMan", StringComparison.OrdinalIgnoreCase))
            {
                return new CustomOp
                {
                    Id = name,
                    SceneName = name,
                    Title = "DUKE'S OPS",
                    PackageName = "LITTLE ROCKET MAN",
                    Duration = "20-35 MIN",
                    ThreatLevel = "MODERATE",
                    OpType = "DIRECT ACTION",
                    Description =
                        "ODG Commanding Officer: \"You know this place. Two houses "
                      + "facing each other across a yard, a fence line, and open ground "
                      + "between the front doors. The government built it to be "
                      + "flattened by a bomb, and the new tenant fancies himself a "
                      + "rocket man.\"\n\nLocation: Nevada Test Site, Nevada\n\nPorch "
                      + "to porch and window to window. Every approach crosses "
                      + "somebody's field of fire, the yard containers stop rifle "
                      + "rounds and little else, and whoever holds an upper floor holds "
                      + "the ground between.\n\nHostile Presence: entrenched, numbers "
                      + "set by user\nCivilian Presence: none. The mannequins were "
                      + "removed\nInfiltration: two insertion points, the north and "
                      + "south ends of the lot\nExtraction: same as "
                      + "infiltration\nObjective: clear both houses and ground the "
                      + "rocket man for good.",
                    AreaOfOperation = "LITTLE ROCKET MAN",

                    MinAI = 1,
                    MaxAI = 20,
                    PrepSeconds = 120,

                    Infils = new[] { "ALPHA 1", "BRAVO 1" },
                };
            }

            if (string.Equals(name, "Villa", StringComparison.OrdinalIgnoreCase))
            {
                return new CustomOp
                {
                    Id = name,
                    SceneName = name,
                    Title = "DUKE'S OPS",
                    PackageName = "OPEN HOUSE",
                    Duration = "20-35 MIN",
                    ThreatLevel = "MODERATE",
                    OpType = "DIRECT ACTION",
                    Description =
                        "ODG Commanding Officer: \"Hillside villa, infinity pool, "
                      + "floor-to-ceiling glass, and not one euro of it earned legally. "
                      + "He launders arms money through holiday lets and he's hosting "
                      + "the buyers this week.\"\n\nLocation: Costa del Sol, "
                      + "Spain\n\nThe lot terraces up from a walled forecourt to a "
                      + "glass living level with bedrooms above it. The glass works "
                      + "both ways and almost none of it stops a round. They hold the "
                      + "inside, so the grounds are yours.\n\nHostile Presence: armed "
                      + "security, numbers set by user\nCivilian Presence: none. The "
                      + "viewing was invitation-only\nInfiltration: one insertion "
                      + "point, the driveway\nExtraction: same as "
                      + "infiltration\nObjective: clear the property, top floor "
                      + "included, and close the listing.",
                    AreaOfOperation = "OPEN HOUSE",

                    MinAI = 1,
                    MaxAI = 14,
                    PrepSeconds = 120,

                    Infils = new[] { "ALPHA 1" },
                };
            }

            return new CustomOp
            {
                Id = name,
                SceneName = name,
                Title = "DUKE'S OPS",
                PackageName = name.ToUpperInvariant(),
                Duration = "30-45 MIN",
                ThreatLevel = "HIGH",
                Description = "No briefing has been written for '" + name + "'. The map is "
                            + "registered and will launch; add its text to BuildMission in "
                            + "MapLoaderMod.cs.",
                AreaOfOperation = name.ToUpperInvariant(),
                MinAI = 1,
                MaxAI = 15,
                PrepSeconds = 120,
                Infils = new[] { "ALPHA 1", "BRAVO 1", "CHARLIE 1" },
            };
        }

        readonly System.Collections.Generic.HashSet<string> _knownOps =
            new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool _tabDirty;
        float _lastTabTry;

        static bool IsMenuScene(string n)
        {
            if (string.IsNullOrEmpty(n)) return true;
            string l = n.ToLowerInvariant();
            return l.Contains("menu") || l.Contains("lobby") || l.Contains("splash")
                || l.Contains("operation room") || l.Contains("loading")
                || l.Contains("mainmenu") || l.Contains("ready");
        }

        static MelonPreferences_Entry<bool> _devKeys;
#if ODG_PUBLIC

        internal static bool Dev { get { return false; } }
#else
        internal static bool Dev { get { return _devKeys != null && _devKeys.Value; } }
#endif
        static bool DevDown(KeyCode k) { return Dev && Input.GetKeyDown(k); }
        static bool DevHeld(KeyCode k) { return Dev && Input.GetKey(k); }

        static bool Shift => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        static bool Ctrl => Input.GetKey(KeyCode.LeftControl);

        void BuildNav()
        {
            var markers = MapAI.FindGroup(MapAI.SpawnGroup);
            Transform anchor = markers.Count > 0 ? markers[0] : null;
            if (anchor == null)
            {
                var cam = Camera.main;
                anchor = cam != null ? cam.transform : null;
            }
            if (anchor == null)
            {
                LoggerInstance.Warning("nav: nothing in the scene to anchor the graph to");
                return;
            }

            var b = SceneBounds();

            if (b.size.x > 2000f || b.size.z > 2000f)
            {
                var mb = MarkerBounds();
                if (mb.HasValue)
                {
                    var m = mb.Value;
                    m.Expand(new Vector3(60f, 30f, 60f));
                    LoggerInstance.Msg("nav: renderer union is " + b.size.x.ToString("F0")
                        + " x " + b.size.z.ToString("F0") + " m (horizon scenery); "
                        + "rebuilt from " + "the gameplay markers instead");
                    b = m;
                }
            }

            LoggerInstance.Msg("nav: building over " + b.size.x.ToString("F0") + " x "
                             + b.size.z.ToString("F0") + " m");
            NavForge.BuildFull(b, anchor, LoggerInstance);
        }

        static Bounds? MarkerBounds()
        {
            Bounds b = default; bool any = false;
            try
            {
                string[] groups = { "GBAISpawnPoint", "GBAIGuardPoint",
                                    "GBPlayerStart", "GBInsertionPoint" };
                for (int g = 0; g < groups.Length; g++)
                {
                    var list = MapAI.FindGroup(groups[g]);
                    for (int i = 0; i < (list != null ? list.Count : 0); i++)
                    {
                        if (list[i] == null) continue;
                        var p = list[i].position;
                        if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                        else b.Encapsulate(p);
                    }
                }
            }
            catch { }
            return any ? b : (Bounds?)null;
        }

        static Bounds SceneBounds()
        {
            var b = new Bounds(Vector3.zero, new Vector3(50f, 20f, 50f));
            bool any = false;

            try
            {
                var mapRoot = MapRuntime.Root;
                if (mapRoot != null)
                {
                    var mr = mapRoot.GetComponentsInChildren<Renderer>(false);
                    for (int k = 0; k < mr.Length; k++)
                    {
                        if (mr[k] == null) continue;
                        if (!any) { b = mr[k].bounds; any = true; }
                        else b.Encapsulate(mr[k].bounds);
                    }
                    if (any) return b;
                }
            }
            catch { }

            try
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                var roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Count; i++)
                {
                    var r = roots[i];
                    if (r == null || !r.name.StartsWith("MAP_", StringComparison.Ordinal)) continue;
                    var rends = r.GetComponentsInChildren<Renderer>(false);
                    for (int k = 0; k < rends.Length; k++)
                    {
                        if (rends[k] == null) continue;
                        if (!any) { b = rends[k].bounds; any = true; }
                        else b.Encapsulate(rends[k].bounds);
                    }
                }
            }
            catch { }
            if (any) b.Expand(4f);
            return b;
        }

        public static string ActiveTick = "<none yet>";
        public static bool ActiveTickReturned = true;

        void SafeRun(string what, Action a)
        {
            ActiveTick = what;
            ActiveTickReturned = false;
            long _t = TickPerf.Begin();
            try { a(); }
            catch (Exception e) { LoggerInstance.Error(what + ": " + e); }
            finally { TickPerf.End(what, _t); }
            ActiveTickReturned = true;
        }

        public override void OnApplicationQuit()
        {
            if (!SystemEnabled) return;

            try
            {
                LoggerInstance.Msg("[tick] last SafeRun label at quit: '" + ActiveTick
                    + "' (returned=" + ActiveTickReturned + ")"
                    + (MapScene.TransitionLatch
                        ? "; latch was UP (" + (MapScene.LatchReason ?? "?") + ")"
                        : ""));
            }
            catch { }
        }
    }
}
