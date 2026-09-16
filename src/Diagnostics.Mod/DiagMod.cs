// ODG Operations - Diagnostics
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Text;
using System.Threading;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;

[assembly: MelonInfo(typeof(ODGDiag.Diag), "ODG Diagnostics", "3.4.0", "ArchD")]
[assembly: MelonGame(null, null)]

[assembly: MelonPriority(-10000)]

namespace ODGDiag
{

    public sealed class Diag : MelonMod
    {
        const int KeepSessions = 14;

        static MelonLogger.Instance _log;
        static string _liveFile;
        static int _quitClean;
        static string _previous = "";
        static bool _reportRewritten;

        static float _nextBeat, _nextGate;
        static int _frames;
        static float _frameAccum, _worstFrame;

        static string _lastGateLine;
        static string _pendingNote;
        static int _ourPid;
        static bool _stampedMain;

        static KeyCode _markKey = KeyCode.Comma;

        public override void OnInitializeMelon()
        {
            _log = LoggerInstance;
            try
            {
                try { _ourPid = System.Diagnostics.Process.GetCurrentProcess().Id; } catch { }

                bool armDumps = true;
                try
                {
                    var cfg = MelonPreferences.CreateCategory("ODGDiagnostics");
                    var entry = cfg.CreateEntry("MarkKey", "Comma",
                        description: "Key that stamps a MARK into the record. Any UnityEngine.KeyCode name.");
                    KeyCode parsed;
                    if (Enum.TryParse<KeyCode>(entry.Value, true, out parsed)) _markKey = parsed;
                    else LoggerInstance.Warning("MarkKey '" + entry.Value
                        + "' is not a key name; falling back to Comma.");

                    armDumps = cfg.CreateEntry("WindowsCrashDumps", true,
                        description: "Let Windows write a small crash dump for OPERATOR so the next "
                                   + "crash names the module that faulted. Own user account only; "
                                   + "set false to remove it.").Value;
                }
                catch { }
                Folders.Prepare();

                DumpArming.Apply(armDumps);

                _previous = Harvest();

                _liveFile = Folders.LiveFileForThisProcess();
                Journal.Start(Folders.RingFor(_liveFile), _liveFile);
                Journal.Write("session start (pid " + _ourPid + ")");
                Journal.Write(Journal.EpochLine());
                WriteMachineBlock();

                Journal.Write(Capture.Install());

                Report.Build(_previous);

                AppDomain.CurrentDomain.UnhandledException += OnUnhandled;

                AppDomain.CurrentDomain.ProcessExit += (s, e) => CleanShutdown("process exit");

                Folders.Prune(KeepSessions);

                _log.Msg("recording to UserData\\" + Folders.RootName + "\\Sessions");
                if (_previous.Contains("DID NOT EXIT CLEANLY"))
                    _log.Warning("the previous session crashed. The report is ready: "
                               + Folders.Report);
            }
            catch (Exception e)
            {

                try { _log?.Error("diagnostics failed to start: " + e.Message); } catch { }
            }
        }

        static string Harvest()
        {
            try
            {
                var stale = new System.Collections.Generic.List<string>();
                foreach (var f in Directory.GetFiles(Folders.Sessions, "_live *.log"))
                {

                    int pid = 0;
                    var name = Path.GetFileNameWithoutExtension(f);
                    int.TryParse(name.Replace("_live", "").Trim(), out pid);

                    if (pid > 0 && pid != _ourPid && ProcessAlive(pid)) continue;
                    stale.Add(f);
                }

                foreach (var r in Directory.GetFiles(Folders.Sessions, "_live *.ring"))
                    if (!File.Exists(Path.ChangeExtension(r, ".log")))
                        try { File.Delete(r); } catch { }

                if (stale.Count == 0)
                {

                    string sum = LastCleanSummary();
                    return "Previous session: exited cleanly (or this is the first run)."
                         + (sum != null ? "\n" + sum : "");
                }

                string newest = null; DateTime newestAt = DateTime.MinValue;
                foreach (var f in stale)
                {

                    var died = File.GetLastWriteTime(f);

                    int recovered = Journal.RecoverInto(Folders.RingFor(f), f);

                    string tail = TailOf(f, 40);
                    var kept = Folders.Retire(f, crashed: true, when: died);

                    if (kept != null) { try { File.Delete(Folders.RingFor(f)); } catch { } }

                    if (kept != null && died > newestAt)
                    {
                        newestAt = died;
                        newest = kept + "\n" + tail;

                        _pendingNote = recovered > 0
                            ? recovered + " line(s) were recovered from the crash ring "
                              + "(the mirror never wrote them)."
                            : null;
                    }
                }

                if (newest == null)
                    return "Previous session: crashed, but its record could not be kept.";

                int nl = newest.IndexOf('\n');
                string keptPath = newest.Substring(0, nl);
                string lastLines = newest.Substring(nl + 1);

                var sb = new StringBuilder();
                sb.AppendLine("Previous session: DID NOT EXIT CLEANLY.");
                sb.AppendLine("It ended around " + newestAt.ToString("yyyy-MM-dd HH:mm") + ".");
                sb.AppendLine("That is a crash, a hard lockup, or the process being killed.");
                sb.AppendLine("Full record: " + keptPath);
                sb.AppendLine();
                sb.AppendLine("THE LAST 40 THINGS THAT HAPPENED BEFORE IT DIED");
                sb.AppendLine("(the final line is the closest we can get to the cause)");
                if (_pendingNote != null) sb.AppendLine(_pendingNote);
                sb.AppendLine();
                sb.Append(lastLines);
                sb.AppendLine();
                sb.AppendLine(CrashDumps.Describe(newestAt));

                var winDump = DumpArming.Describe(newestAt);
                if (winDump != null) { sb.AppendLine(); sb.AppendLine(winDump); }
                sb.AppendLine();

                sb.AppendLine(WinEvents.Describe(newestAt).Text);
                return sb.ToString();
            }
            catch (Exception e) { return "Previous session: check failed (" + e.Message + ")"; }
        }

        static string LastCleanSummary()
        {
            try
            {
                string newest = null; DateTime at = DateTime.MinValue;
                foreach (var f in Directory.GetFiles(Folders.Sessions, "* - ok.log"))
                {
                    var t = File.GetLastWriteTime(f);
                    if (t > at) { at = t; newest = f; }
                }
                if (newest == null) return null;
                var lines = File.ReadAllLines(newest);
                for (int i = lines.Length - 1; i >= 0 && i >= lines.Length - 6; i--)
                    if (lines[i].Contains("session summary:"))
                        return lines[i].Substring(lines[i].IndexOf("session summary:"));
            }
            catch { }
            return null;
        }

        static bool ProcessAlive(int pid)
        {
            try
            {
                var p = System.Diagnostics.Process.GetProcessById(pid);
                try { return p.ProcessName == System.Diagnostics.Process.GetCurrentProcess().ProcessName; }
                catch { return true; }
            }
            catch { return false; }
        }

        static string TailOf(string file, int lines)
        {
            try
            {
                var all = File.ReadAllLines(file);
                var sb = new StringBuilder();
                for (int i = Math.Max(0, all.Length - lines); i < all.Length; i++)
                    sb.AppendLine("  " + all[i]);
                return sb.ToString();
            }
            catch (Exception e) { return "  (could not read the record: " + e.Message + ")\n"; }
        }

        static void WriteMachineBlock()
        {
            Journal.Write("---- machine ----");
            Journal.Write("  gpu        " + Report.Safe(() => SystemInfo.graphicsDeviceName));
            Journal.Write("  gpu driver " + Report.Safe(() => SystemInfo.graphicsDeviceVersion));
            Journal.Write("  vram       " + Report.Safe(() => SystemInfo.graphicsMemorySize + " MB"));
            Journal.Write("  cpu        " + Report.Safe(() => SystemInfo.processorType)
                                          + " x" + Report.Safe(() => SystemInfo.processorCount.ToString()));
            Journal.Write("  ram        " + Report.Safe(() => SystemInfo.systemMemorySize + " MB"));
            Journal.Write("  os         " + Report.Safe(() => SystemInfo.operatingSystem));
            Journal.Write("  gfx api    " + Report.Safe(() => SystemInfo.graphicsDeviceType.ToString()));
            Journal.Write("  pack       " + Report.InstalledRelease());

            Journal.Write("  maploader  " + Report.MapLoaderPrefs());
            Journal.Write("-----------------");
        }

        static void OnUnhandled(object sender, UnhandledExceptionEventArgs e)
        {

            try
            {
                Journal.WriteNow("!!! UNHANDLED MANAGED EXCEPTION terminating=" + e.IsTerminating);
                Journal.WriteNow("!!! " + (e.ExceptionObject != null ? e.ExceptionObject.ToString() : "<null>"));
            }
            catch { }
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            Journal.Write("scene LOADED '" + sceneName + "'  (" + LoadedScenes() + ")");
            _worstFrame = 0f;
            _lastGateLine = null;

            if (!_reportRewritten)
            {
                _reportRewritten = true;
                Journal.Write("mods loaded: " + Report.LoadedMods());
                Report.Build(_previous);
            }
        }

        public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
        {
            Journal.Write("scene unloaded '" + sceneName + "'");
        }

        static string LoadedScenes()
        {
            try
            {
                var sb = new StringBuilder(64);
                sb.Append("active=").Append(SceneManager.GetActiveScene().name);
                sb.Append(" loaded=");
                int n = SceneManager.sceneCount;
                for (int i = 0; i < n; i++)
                {
                    if (i > 0) sb.Append('+');
                    sb.Append(SceneManager.GetSceneAt(i).name);
                }
                return sb.ToString();
            }
            catch { return "<scene read failed>"; }
        }

        public override void OnUpdate()
        {

            if (!_stampedMain) { _stampedMain = true; try { Journal.NoteMainThread(); } catch { } }

            try
            {
                if (Input.GetKeyDown(_markKey) || Input.GetKeyDown(KeyCode.ScrollLock))
                    Journal.Write("MARK pressed by the player" + Position()
                        + "  (" + LoadedScenes() + ")");
            }
            catch { }

            float dt;
            try
            {

                dt = Time.unscaledDeltaTime;
            }
            catch { return; }

            _frames++;
            _frameAccum += dt;
            if (dt > _worstFrame) _worstFrame = dt;

            if      (dt > 0.250f) _over250++;
            else if (dt > 0.100f) _over100++;
            else if (dt > 0.033f) _over33++;

            _tFrames++;
            _tSeconds += dt;
            if (dt > 0.100f) _tOver100++;
            if (dt > 1.0f) _tStalls++;
            if (dt > _tWorst) _tWorst = dt;

            float now = _frameAccum + _nextBeatBase;

            if (dt > 1.0f)
                Journal.Write("STALL one frame took " + dt.ToString("F2") + " s"
                    + Position() + "  (" + LoadedScenes() + ")");

            {
                long wsNow = 0, heapNow = 0;
                int c0 = 0, c1 = 0, c2 = 0;
                try
                {
                    wsNow   = Environment.WorkingSet / (1024 * 1024);
                    heapNow = GC.GetTotalMemory(false) / (1024 * 1024);
                    c0 = GC.CollectionCount(0); c1 = GC.CollectionCount(1); c2 = GC.CollectionCount(2);
                }
                catch { }

                if (_probeArmed && dt > ProbeThresholdSec && wsNow > 0)
                {
                    long dws = wsNow - _wsPrev;
                    long dheap = heapNow - _heapPrev;
                    int g0d = c0 - _gcPrev0, g1d = c1 - _gcPrev1, g2d = c2 - _gcPrev2;

                    string verdict;
                    if (g2d > 0)                    verdict = "gen2 GC";
                    else if (g0d > 0 || g1d > 0)    verdict = "gen0/1 GC";
                    else if (dws <= -64)            verdict = "NATIVE FREE (no GC) -- asset unload shape";
                    else if (dws >= 64)             verdict = "native ALLOC (no GC) -- load/stream shape";
                    else                            verdict = "no memory movement -- not memory";

                    Journal.Write("[stallprobe] " + (dt * 1000f).ToString("F0") + "ms"
                        + "  workingSet " + _wsPrev + " -> " + wsNow + " MB (" + (dws >= 0 ? "+" : "") + dws + ")"
                        + "  heap " + (dheap >= 0 ? "+" : "") + dheap + " MB"
                        + "  gc " + g0d + "/" + g1d + "/" + g2d
                        + "  " + verdict
                        + "  (" + LoadedScenes() + ")");
                }

                _wsPrev = wsNow; _heapPrev = heapNow;
                _gcPrev0 = c0; _gcPrev1 = c1; _gcPrev2 = c2;
                _probeArmed = true;
            }

            if (now >= _nextBeat)
            {
                _nextBeat = now + 5f;
                float fps = _frameAccum > 0f ? _frames / _frameAccum : 0f;
                long mem = GC.GetTotalMemory(false) / (1024 * 1024);

                long ws = 0;
                try { ws = Environment.WorkingSet / (1024 * 1024); } catch { }

                int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
                string gc = (g0 - _gc0) + "/" + (g1 - _gc1) + "/" + (g2 - _gc2);
                _gc0 = g0; _gc1 = g1; _gc2 = g2;

                string gfx = "";
                if (_gfxMemWorks != 0)
                {
                    try
                    {
                        long g = UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver();
                        if (_gfxMemWorks < 0) _gfxMemWorks = g > 0 ? 1 : 0;
                        if (_gfxMemWorks == 1) gfx = " gfxMB=" + (g / (1024 * 1024));
                    }
                    catch { _gfxMemWorks = 0; }
                }

                Journal.Write("beat fps=" + fps.ToString("F0")
                    + " worst=" + (_worstFrame * 1000f).ToString("F0") + "ms"
                    + " >33ms=" + _over33 + " >100ms=" + _over100 + " >250ms=" + _over250
                    + " gc=" + gc
                    + " managedMB=" + mem + " processMB=" + ws + gfx
                    + Position()
                    + "  " + LoadedScenes());
                _frames = 0;
                _nextBeatBase += _frameAccum;
                _frameAccum = 0f;
                _worstFrame = 0f;
                _over33 = 0; _over100 = 0; _over250 = 0;

                string role = "OFFLINE";
                int conns = -1;
                try
                {
                    bool sv = Il2CppMirror.NetworkServer.active;
                    bool cl = Il2CppMirror.NetworkClient.active;
                    role = sv ? (cl ? "HOST" : "SERVER") : (cl ? "CLIENT" : "OFFLINE");
                    if (sv) conns = Il2CppMirror.NetworkServer.connections.Count;
                }
                catch { }
                if (role != _lastRole || conns != _lastConns)
                {
                    _lastRole = role; _lastConns = conns;
                    Journal.Write("network role: " + role
                        + (conns >= 0 ? "  connections=" + conns : ""));
                }
            }

            if (now >= _nextGate)
            {
                _nextGate = now + 5f;
                ReadyGateProbe();
            }

            if (now >= _nextCost)
            {
                _nextCost = now + 60f;
                Journal.Write("diagnostics self-cost: " + Journal.SelfCost());
            }
        }
        static float _nextBeatBase;
        static int _gc0, _gc1, _gc2;

        static long _wsPrev;
        static long _heapPrev;
        static int _gcPrev0, _gcPrev1, _gcPrev2;
        static bool _probeArmed;

        const float ProbeThresholdSec = 0.250f;
        static int _gfxMemWorks = -1;

        static string Position()
        {
            try
            {
                var cam = Camera.main;
                if (cam == null) return "";
                var p = cam.transform.position;
                return " pos=(" + p.x.ToString("F0") + "," + p.y.ToString("F0")
                     + "," + p.z.ToString("F0") + ")";
            }
            catch { return ""; }
        }

        static int _tFrames, _tStalls, _tOver100;
        static float _tSeconds, _tWorst;
        static float _nextCost;
        static int _over33, _over100, _over250;
        static string _lastRole;
        static int _lastConns = -2;

        static void ReadyGateProbe()
        {
            try
            {
                var gmn = Il2Cpp.GameManagerNetwork.instance;
                if (gmn == null) return;

                bool all;
                try { all = gmn.AllPlayersLoaded; } catch { return; }
                if (all)
                {
                    if (_lastGateLine != null)
                    {
                        Journal.Write("ready gate: satisfied (AllPlayersLoaded=true)");
                        _lastGateLine = null;
                    }
                    return;
                }

                string scene;
                try { scene = SceneManager.GetActiveScene().name; } catch { return; }
                if (IsMenuish(scene)) return;

                int ready = -1;
                try { ready = gmn.ReadyPlayers; } catch { }
                int conns = -1;
                try { if (Il2CppMirror.NetworkServer.active) conns = Il2CppMirror.NetworkServer.connections.Count; }
                catch { }

                string line = "READY GATE PENDING ready=" + ready
                    + (conns >= 0 ? " serverConnections=" + conns : " (client)")
                    + "  " + LoadedScenes();
                if (line == _lastGateLine) return;
                _lastGateLine = line;
                Journal.Write(line + "  <-- if active= is the custom map rather than the "
                    + "level the game loaded, that is the suspect");
            }
            catch { }
        }

        static bool IsMenuish(string n)
        {
            if (string.IsNullOrEmpty(n)) return true;
            string l = n.ToLowerInvariant();
            return l.Contains("menu") || l.Contains("lobby") || l.Contains("splash")
                || l.Contains("operation room") || l.Contains("loading") || l.Contains("ready");
        }

        public override void OnApplicationQuit() { CleanShutdown("application quit"); }

        static void CleanShutdown(string how)
        {

            if (Interlocked.Exchange(ref _quitClean, 1) != 0) return;
            try
            {

                float avg = _tSeconds > 0f ? _tFrames / _tSeconds : 0f;
                Journal.Write("session summary: " + (int)(_tSeconds / 60f) + " min, avg fps "
                    + avg.ToString("F0") + ", worst frame " + (_tWorst * 1000f).ToString("F0")
                    + " ms, frames over 100 ms: " + _tOver100 + ", stalls over 1 s: " + _tStalls);
                Journal.WriteNow("clean shutdown (" + how + ")");
                Journal.Stop();
                Folders.Retire(_liveFile, crashed: false, when: DateTime.Now);
            }
            catch { }
        }
    }
}
