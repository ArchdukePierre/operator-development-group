// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MapLoader
{

    public static class MapScene
    {

        public static string PendingScene;

        static readonly HashSet<string> _ours =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static MelonLogger.Instance _log;
        static bool _installed;

        public static void Init(MelonLogger.Instance log) { _log = log; }

        public static void Register(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return;
            if (_ours.Add(sceneName))
                _log?.Msg("[mapscene] registered custom scene: " + sceneName);
        }

        public static bool IsOurs(string sceneName) =>
            !string.IsNullOrEmpty(sceneName) && _ours.Contains(sceneName);

        public static void SyncFromBundles()
        {
            foreach (var name in SceneBundle.RegisteredScenes)
                if (!_ours.Contains(name)) Register(name);
        }

        public static IEnumerable<string> Known => _ours;

        public static bool TransitionLatch { get; private set; }
        public static string LatchReason { get; private set; }

        public static event Action TransitionStarting;

        public static void LatchUp(string reason)
        {
            if (TransitionLatch)
            {

                LatchReason = reason;
                _log?.Msg("[crumb] [latch] re-assert (" + reason + ")");
                return;
            }
            TransitionLatch = true;
            LatchReason = reason;
            _log?.Msg("[crumb] [latch] up (" + reason + ")");
        }

        public static void LatchDown(string why)
        {
            if (!TransitionLatch) return;
            TransitionLatch = false;
            _log?.Msg("[crumb] [latch] down (" + why + ")");
            LatchReason = null;
        }

        public static void Install(HarmonyLib.Harmony h, MelonLogger.Instance log)
        {
            _log = log;
            if (_installed) return;
            _installed = true;

            TryPatch(h, "ServerChangeScene", nameof(Pre_ServerChangeScene));
            log.Msg("[mapscene] installed");
        }

        static void TryPatch(HarmonyLib.Harmony h, string method, string prefix)
        {
            try
            {
                var m = AccessTools.Method(typeof(Il2Cpp.NetworkManagerVectorGames), method,
                                           new[] { typeof(string) });
                if (m == null)
                {
                    _log?.Warning("[mapscene] no NetworkManagerVectorGames." + method);
                    return;
                }
                h.Patch(m, prefix: new HarmonyMethod(typeof(MapScene), prefix));
                _log?.Msg("[mapscene] hooked " + method);
            }
            catch (Exception e)
            {
                _log?.Error("[mapscene] patching " + method + ": " + e.Message);
            }
        }

        static void Pre_ServerChangeScene(ref string newSceneName)
        {

            try
            {
                LatchUp("ServerChangeScene -> '" + newSceneName + "'");
                var started = TransitionStarting;
                if (started != null) started();
            }
            catch (Exception e) { _log?.Error("[mapscene] transition start: " + e.Message); }

            try
            {
                if (string.IsNullOrEmpty(PendingScene)) return;

                string want = PendingScene;
                if (!IsOurs(want))
                {
                    _log?.Warning("[mapscene] pending scene '" + want + "' is not a "
                                + "registered bundle scene; letting the game proceed to '"
                                + newSceneName + "'");
                    PendingScene = null;
                    return;
                }

                _log?.Msg("[mapscene] ServerChangeScene '" + newSceneName + "' -> '" + want + "'");
                newSceneName = want;
                PendingScene = null;
            }
            catch (Exception e)
            {

                _log?.Error("[mapscene] prefix: " + e.Message);
            }
        }

        public static event Action<Scene> CustomSceneLoaded;

        public static void NotifySceneLoaded(string sceneName)
        {
            try
            {
                if (!IsOurs(sceneName)) return;
                var scene = SceneManager.GetActiveScene();
                _log?.Msg("[mapscene] custom scene loaded: " + sceneName
                        + "  roots=" + scene.rootCount);
                SceneBundle.Acquire(sceneName);
                var h = CustomSceneLoaded;
                if (h != null) h(scene);
            }
            catch (Exception e) { _log?.Error("[mapscene] sceneLoaded: " + e.Message); }
        }

        public static bool Go(string sceneName)
        {
            if (!IsOurs(sceneName))
            {
                _log?.Error("[mapscene] '" + sceneName + "' is not a registered bundle scene. "
                          + "Known: " + string.Join(", ", new List<string>(_ours).ToArray()));
                return false;
            }

            var nm = Il2Cpp.NetworkManagerVectorGames.instance;
            if (nm == null)
            {
                _log?.Error("[mapscene] no NetworkManagerVectorGames instance");
                return false;
            }
            if (!Il2CppMirror.NetworkServer.active)
            {
                _log?.Error("[mapscene] not the server; only the host can change scene");
                return false;
            }

            PendingScene = sceneName;
            try
            {

                nm.ServerChangeScene(sceneName);
                return true;
            }
            catch (Exception e)
            {
                PendingScene = null;
                _log?.Error("[mapscene] ServerChangeScene threw: " + e.Message);
                return false;
            }
        }

        public static void DumpState()
        {
            _log?.Msg("[mapscene] registered scenes: " + _ours.Count);
            foreach (var s in _ours) _log?.Msg("    " + s);
            _log?.Msg("[mapscene] pending: " + (PendingScene ?? "<none>"));
            var active = SceneManager.GetActiveScene();
            _log?.Msg("[mapscene] active scene: " + active.name + " (ours=" + IsOurs(active.name) + ")");
        }
    }
}
