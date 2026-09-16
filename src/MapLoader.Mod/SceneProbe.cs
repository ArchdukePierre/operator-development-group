// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MapLoader
{

    public static class SceneProbe
    {
        enum Step { Idle, WaitBundle, TryShort, TryPath, Report, Done }

        static Step _step = Step.Idle;
        static SceneBundle.Entry _entry;
        static string _bundlePath;
        static string _sceneName;
        static float _stepStarted;
        static AsyncOperation _op;
        static MelonLogger.Instance _log;
        static readonly List<string> _results = new List<string>();

        public static bool Running => _step != Step.Idle && _step != Step.Done;

        public static void Init(MelonLogger.Instance log) { _log = log; }

        static void Log(string s) { if (_log != null) _log.Msg("[P1] " + s); }
        static void Warn(string s) { if (_log != null) _log.Warning("[P1] " + s); }

        public static void Start(string bundlePath)
        {
            if (Running) { Warn("already running, ignoring"); return; }
            _bundlePath = bundlePath;
            _results.Clear();
            _entry = null; _op = null; _sceneName = null;

            Log("================ P1 scene-bundle probe ================");
            Log("bundle: " + bundlePath);
            if (!File.Exists(bundlePath))
            {
                Warn("file does not exist. Build one in Unity " + Application.unityVersion
                   + " and drop it there, or set MapLoader/SceneBundlePath.");
                _step = Step.Done;
                return;
            }
            Log("unity (this build): " + Application.unityVersion);
            _entry = SceneBundle.BeginLoad(bundlePath);
            _step = Step.WaitBundle;
            _stepStarted = Time.realtimeSinceStartup;
        }

        public static void Tick()
        {
            if (!Running) return;
            float elapsed = Time.realtimeSinceStartup - _stepStarted;

            switch (_step)
            {
                case Step.WaitBundle:
                    if (_entry == null) { Fail("no registry entry"); return; }
                    if (_entry.State == SceneBundle.State.Failed) { Fail("bundle load failed: " + _entry.Error); return; }
                    if (_entry.State != SceneBundle.State.Ready)
                    {
                        if (elapsed > 30f) Fail("bundle still loading after 30s");
                        return;
                    }

                    _results.Add("bundle loaded in " + _entry.LoadSeconds.ToString("F2") + "s");
                    _results.Add("isStreamedSceneAssetBundle = " + _entry.Bundle.isStreamedSceneAssetBundle);
                    _results.Add("GetAllScenePaths() returned " + _entry.ScenePaths.Count + " path(s)");

                    if (_entry.ScenePaths.Count == 0)
                    {

                        Warn("NO SCENES. Either the bundle was not built with a scene in it, or this "
                           + "build does not surface scene bundles. Before concluding P1 failed, "
                           + "rebuild as a scene-only bundle (one .unity, nothing else) and retry. "
                           + "If a scene-only bundle also reports zero, that is R1 and the fallback "
                           + "ladder applies.");
                        Finish(false);
                        return;
                    }

                    _sceneName = _entry.SceneNames[0];
                    Log("attempting additive load by short name: " + _sceneName);
                    _op = TryLoad(_sceneName);
                    _step = _op != null ? Step.Report : Step.TryPath;
                    _stepStarted = Time.realtimeSinceStartup;
                    return;

                case Step.TryPath:
                    string full = _entry.ScenePaths[0];
                    Log("short name refused; retrying by full path: " + full);
                    _op = TryLoad(full);
                    if (_op == null) { Fail("both short name and full path were refused by SceneManager"); return; }
                    _step = Step.Report;
                    _stepStarted = Time.realtimeSinceStartup;
                    return;

                case Step.Report:
                    if (_op != null && !_op.isDone)
                    {
                        if (elapsed > 60f) { Fail("scene load stuck at " + _op.progress.ToString("F2")); }
                        return;
                    }
                    Inspect();
                    Finish(true);
                    return;
            }
        }

        static AsyncOperation TryLoad(string key)
        {
            try
            {
                var op = SceneManager.LoadSceneAsync(key, LoadSceneMode.Additive);
                if (op == null) { Warn("LoadSceneAsync returned null for '" + key + "'"); return null; }
                return op;
            }
            catch (Exception e)
            {

                Warn("LoadSceneAsync threw for '" + key + "': " + e.Message);
                return null;
            }
        }

        static void Inspect()
        {
            Scene target = default(Scene);
            bool found = false;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.name == _sceneName || s.path == (_entry.ScenePaths.Count > 0 ? _entry.ScenePaths[0] : null))
                { target = s; found = true; break; }
            }

            if (!found)
            {
                _results.Add("scene NOT present in SceneManager after load completed");
                Warn("load reported done but the scene is not in SceneManager. "
                   + "That is the signature of a name that resolved to nothing.");
                return;
            }

            _results.Add("scene present: name='" + target.name + "' isLoaded=" + target.isLoaded
                       + " rootCount=" + target.rootCount);

            int renderers = 0, colliders = 0, lights = 0, objects = 0;
            try
            {
                var roots = target.GetRootGameObjects();
                foreach (var r in roots)
                {
                    objects++;
                    renderers += r.GetComponentsInChildren<Renderer>(true).Count;
                    colliders += r.GetComponentsInChildren<Collider>(true).Count;
                    lights += r.GetComponentsInChildren<Light>(true).Count;
                    Log("    root: " + r.name + "  children=" + r.transform.childCount);
                }
            }
            catch (Exception e) { Warn("hierarchy walk threw: " + e.Message); }

            _results.Add("roots=" + objects + " renderers=" + renderers
                       + " colliders=" + colliders + " lights=" + lights);

            try
            {
                var roots = target.GetRootGameObjects();
                int nullShader = 0, checkedMats = 0;
                var seen = new HashSet<string>();
                foreach (var r in roots)
                    foreach (var mr in r.GetComponentsInChildren<Renderer>(true))
                        foreach (var m in mr.sharedMaterials)
                        {
                            if (m == null) continue;
                            checkedMats++;
                            var sh = m.shader;
                            if (sh == null) { nullShader++; continue; }
                            if (seen.Add(sh.name)) Log("    shader seen: " + sh.name);
                        }
                _results.Add("materials=" + checkedMats + " nullShader=" + nullShader
                           + " distinctShaders=" + seen.Count);
            }
            catch (Exception e) { Warn("material walk threw: " + e.Message); }
        }

        static void Fail(string why)
        {
            Warn("FAILED: " + why);
            _results.Add("FAILED: " + why);
            Finish(false);
        }

        static void Finish(bool pass)
        {
            Log("---------------- P1 result ----------------");
            foreach (var r in _results) Log("  " + r);
            Log(pass ? "P1 PASS: a scene bundle registers and loads. Proceed to M2 (material whitelist)."
                     : "P1 NOT PASSED on this attempt. Try the variants before invoking R1.");
            Log("===========================================");
            _step = Step.Done;
        }

        public static void Reset()
        {
            try
            {
                if (!string.IsNullOrEmpty(_sceneName))
                {
                    for (int i = 0; i < SceneManager.sceneCount; i++)
                    {
                        var s = SceneManager.GetSceneAt(i);
                        if (s.name == _sceneName) { SceneManager.UnloadSceneAsync(s); break; }
                    }
                }
            }
            catch (Exception e) { Warn("reset unload threw: " + e.Message); }
            if (!string.IsNullOrEmpty(_bundlePath)) SceneBundle.Unload(_bundlePath, true);
            _step = Step.Idle;
            Log("probe reset.");
        }
    }
}
