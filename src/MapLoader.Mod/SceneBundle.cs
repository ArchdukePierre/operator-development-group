// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MapLoader
{

    public static unsafe class SceneBundle
    {
        public enum State { Idle, Loading, Ready, Failed }

        public sealed class Entry
        {
            public string Path;
            public string Sha256;
            public AssetBundleCreateRequest Request;
            public AssetBundle Bundle;
            public State State = State.Idle;
            public string Error;
            public readonly List<string> ScenePaths = new List<string>();
            public readonly List<string> SceneNames = new List<string>();
            public int RefCount;
            public float StartedAt;
            public float ReadyAt;

            public float LoadSeconds => ReadyAt > 0f ? ReadyAt - StartedAt : Time.realtimeSinceStartup - StartedAt;
        }

        static readonly Dictionary<string, Entry> _byPath =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        static readonly Dictionary<string, Entry> _byScene =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        static MelonLogger.Instance _log;
        static IntPtr _method;
        static bool _resolved;

        public static void Init(MelonLogger.Instance log) { _log = log; }

        static void Log(string s) { if (_log != null) _log.Msg("[bundle] " + s); }
        static void Warn(string s) { if (_log != null) _log.Warning("[bundle] " + s); }

        public static IEnumerable<Entry> Entries => _byPath.Values;
        public static int Count => _byPath.Count;

        public static bool TryGetByScene(string sceneName, out Entry e) =>
            _byScene.TryGetValue(sceneName ?? "", out e);

        public static bool IsRegisteredScene(string sceneName) =>
            !string.IsNullOrEmpty(sceneName) && _byScene.ContainsKey(sceneName);

        public static IEnumerable<string> RegisteredScenes => _byScene.Keys;

        static bool Resolve()
        {
            if (_resolved) return _method != IntPtr.Zero;
            _resolved = true;
            try
            {
                var t = typeof(AssetBundle);
                FieldInfo best = null;
                foreach (var f in t.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    if (f.FieldType != typeof(IntPtr)) continue;
                    if (!f.Name.StartsWith("NativeMethodInfoPtr_LoadFromFileAsync_Internal",
                                           StringComparison.Ordinal)) continue;
                    best = f;
                    break;
                }
                if (best == null)
                {
                    Warn("no NativeMethodInfoPtr_LoadFromFileAsync_Internal* field on AssetBundle. "
                       + "Interop generation changed; dump AssetBundle's static fields and re-match.");
                    return false;
                }
                _method = (IntPtr)best.GetValue(null);
                Log("resolved " + best.Name + " -> 0x" + _method.ToString("X"));
                return _method != IntPtr.Zero;
            }
            catch (Exception e)
            {
                Warn("resolve failed: " + e.Message);
                return false;
            }
        }

        public static Entry BeginLoad(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string full;
            try { full = Path.GetFullPath(path); } catch { full = path; }

            Entry existing;
            if (_byPath.TryGetValue(full, out existing))
            {
                if (existing.State == State.Failed)
                {
                    Log("retrying previously failed " + Path.GetFileName(full));
                    _byPath.Remove(full);
                }
                else
                {
                    Log("already " + existing.State.ToString().ToLowerInvariant() + ", reusing: "
                      + Path.GetFileName(full) + "  refs=" + existing.RefCount);
                    return existing;
                }
            }

            if (!File.Exists(full))
            {
                Warn("no such file: " + full);
                return new Entry { Path = full, State = State.Failed, Error = "file not found" };
            }

            const long Limit = 2147483648L;
            long bytes = new FileInfo(full).Length;
            if (bytes >= Limit)
            {
                string msg = "bundle is " + (bytes / 1048576) + " MiB, over the 2048 MiB "
                           + "AssetBundle limit -- Unity will refuse it. Rebuild with a "
                           + "smaller texture size or split it.";
                Warn(Path.GetFileName(full) + ": " + msg);
                return new Entry { Path = full, State = State.Failed, Error = msg };
            }
            if (!Resolve())
                return new Entry { Path = full, State = State.Failed, Error = "native method unresolved" };

            var e = new Entry { Path = full, State = State.Loading, StartedAt = Time.realtimeSinceStartup };
            try { e.Sha256 = HashFile(full); } catch { }
            _byPath[full] = e;

            try
            {
                IntPtr strPtr = IL2CPP.ManagedStringToIl2Cpp(full);
                uint crc = 0u;
                ulong offset = 0uL;

                void** args = stackalloc void*[3];
                args[0] = (void*)strPtr;
                args[1] = &crc;
                args[2] = &offset;

                IntPtr exc = IntPtr.Zero;
                IntPtr ret = IL2CPP.il2cpp_runtime_invoke(_method, IntPtr.Zero, args, ref exc);

                if (exc != IntPtr.Zero)
                {
                    e.State = State.Failed;
                    e.Error = "il2cpp exception from LoadFromFileAsync_Internal";
                    Warn(e.Error + ": " + Path.GetFileName(full));
                    return e;
                }
                if (ret == IntPtr.Zero)
                {
                    e.State = State.Failed;
                    e.Error = "null request pointer";
                    Warn(e.Error + ": " + Path.GetFileName(full));
                    return e;
                }

                e.Request = new AssetBundleCreateRequest(ret);
                Log("load started: " + Path.GetFileName(full)
                  + "  sha=" + Short(e.Sha256) + "  " + (new FileInfo(full).Length / 1024) + " KiB");
            }
            catch (Exception ex)
            {
                e.State = State.Failed;
                e.Error = ex.Message;
                Warn("invoke threw: " + ex.Message);
            }
            return e;
        }

        public static void Tick()
        {
            List<Entry> done = null;
            foreach (var e in _byPath.Values)
            {
                if (e.State != State.Loading || e.Request == null) continue;
                bool isDone;
                try { isDone = e.Request.isDone; }
                catch (Exception ex)
                {
                    e.State = State.Failed; e.Error = "isDone threw: " + ex.Message;
                    (done ?? (done = new List<Entry>())).Add(e);
                    continue;
                }
                if (!isDone) continue;
                (done ?? (done = new List<Entry>())).Add(e);
            }
            if (done == null) return;

            foreach (var e in done)
            {
                if (e.State == State.Failed) { Warn(Path.GetFileName(e.Path) + ": " + e.Error); continue; }
                Finish(e);
            }
        }

        static void Finish(Entry e)
        {
            try
            {
                e.Bundle = e.Request.assetBundle;
            }
            catch (Exception ex)
            {
                e.State = State.Failed; e.Error = "assetBundle threw: " + ex.Message;
                Warn(e.Error); return;
            }

            if (e.Bundle == null)
            {

                e.Bundle = NativeBundle.LoadSync(e.Path, out string how);
                if (e.Bundle != null)
                {
                    Log(Path.GetFileName(e.Path) + ": " + how);
                }
                else
                {
                    e.State = State.Failed;
                    e.Error = "bundle null after load, and the injected icall also "
                            + "declined (" + how + ")";
                    Warn(Path.GetFileName(e.Path) + ": " + e.Error);
                    return;
                }
            }

            e.ReadyAt = Time.realtimeSinceStartup;
            e.State = State.Ready;

            try
            {
                var paths = e.Bundle.GetAllScenePaths();
                if (paths != null)
                {
                    foreach (var p in paths)
                    {
                        if (string.IsNullOrEmpty(p)) continue;
                        e.ScenePaths.Add(p);
                        string name = Path.GetFileNameWithoutExtension(p);
                        e.SceneNames.Add(name);
                        _byScene[name] = e;
                    }
                }
            }
            catch (Exception ex) { Warn("GetAllScenePaths threw: " + ex.Message); }

            Log("READY " + Path.GetFileName(e.Path) + " in " + e.LoadSeconds.ToString("F2") + "s"
              + "  scenes=" + e.ScenePaths.Count
              + (e.Bundle.isStreamedSceneAssetBundle ? " (streamed scene bundle)" : " (asset bundle)"));
            foreach (var p in e.ScenePaths) Log("    scene: " + p);
            if (e.ScenePaths.Count == 0)
            {
                try
                {
                    var names = e.Bundle.GetAllAssetNames();
                    Log("    no scenes; " + (names == null ? 0 : names.Count) + " assets");
                }
                catch { }
            }
        }

        public static void Acquire(string sceneName)
        {
            Entry e;
            if (!_byScene.TryGetValue(sceneName ?? "", out e)) return;
            e.RefCount++;
            Log("acquire " + sceneName + "  refs " + (e.RefCount - 1) + " -> " + e.RefCount);
        }

        public static void Release(string sceneName)
        {
            Entry e;
            if (!_byScene.TryGetValue(sceneName ?? "", out e)) return;
            int before = e.RefCount;
            e.RefCount = Math.Max(0, e.RefCount - 1);
            Log("release " + sceneName + "  refs " + before + " -> " + e.RefCount);
        }

        public static bool Unload(string path, bool force = false)
        {
            string full;
            try { full = Path.GetFullPath(path); } catch { full = path; }
            Entry e;
            if (!_byPath.TryGetValue(full, out e)) { Warn("unload: not registered: " + full); return false; }
            if (e.RefCount > 0 && !force)
            {
                Warn("unload refused, refcount " + e.RefCount + ": " + Path.GetFileName(full)
                   + " (pass force to override)");
                return false;
            }
            try { if (e.Bundle != null) e.Bundle.Unload(false); }
            catch (Exception ex) { Warn("Unload(false) threw: " + ex.Message); }

            foreach (var n in e.SceneNames) _byScene.Remove(n);
            _byPath.Remove(full);
            Log("unloaded " + Path.GetFileName(full) + "  (" + e.SceneNames.Count + " scene names released)");
            return true;
        }

        public static void UnloadAll(bool force = true)
        {
            var paths = new List<string>(_byPath.Keys);
            foreach (var p in paths) Unload(p, force);
        }

        public static int ScanAndPreload(string mapsRoot)
        {
            if (!Directory.Exists(mapsRoot)) { Log("no maps root: " + mapsRoot); return 0; }
            int n = 0;
            foreach (var f in Directory.GetFiles(mapsRoot, "*.mapbundle", SearchOption.AllDirectories))
            { BeginLoad(f); n++; }
            foreach (var f in Directory.GetFiles(mapsRoot, "*.bundle", SearchOption.AllDirectories))
            { BeginLoad(f); n++; }
            Log("registry scan: " + n + " bundle(s) under " + mapsRoot);
            return n;
        }

        public static void DumpState()
        {
            Log("=== registry: " + _byPath.Count + " bundle(s), " + _byScene.Count + " scene name(s)");
            foreach (var e in _byPath.Values)
            {
                Log("  " + e.State.ToString().ToUpperInvariant().PadRight(7)
                  + " refs=" + e.RefCount
                  + " " + Path.GetFileName(e.Path)
                  + " sha=" + Short(e.Sha256)
                  + " " + e.LoadSeconds.ToString("F2") + "s"
                  + (e.Error != null ? "  ERR: " + e.Error : ""));
                foreach (var n in e.SceneNames) Log("      scene: " + n);
            }
            var live = SceneManager.sceneCount;
            Log("  live scenes (" + live + "):");
            for (int i = 0; i < live; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                Log("      " + s.name + "  loaded=" + s.isLoaded + " roots=" + s.rootCount);
            }
        }

        static string Short(string sha) => string.IsNullOrEmpty(sha) ? "?" : sha.Substring(0, 12);

        static string HashFile(string path)
        {
            using (var fs = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                var h = sha.ComputeHash(fs);
                var sb = new System.Text.StringBuilder(h.Length * 2);
                foreach (var b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
