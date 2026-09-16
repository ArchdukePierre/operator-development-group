// ODG Operations - HSR
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;

namespace HSR
{

    internal static class Leak
    {
        const float SweepSeconds = 60f;
        const float ReportSeconds = 30f;

        static readonly string[] Names =
            { "GameObject", "Material", "Texture2D", "Mesh", "AudioClip", "Renderer" };

        static Il2CppSystem.Type[] _types;
        static readonly int[] _now = new int[6];

        class SceneStat
        {
            internal readonly int[] First = new int[6];
            internal readonly int[] Prev = new int[6];
            internal bool HaveFirst;
            internal int Visits;
        }
        static readonly System.Collections.Generic.Dictionary<string, SceneStat> _stats =
            new System.Collections.Generic.Dictionary<string, SceneStat>(StringComparer.Ordinal);

        static SceneStat Stat(string scene)
        {
            SceneStat st;
            if (!_stats.TryGetValue(scene, out st)) { st = new SceneStat(); _stats[scene] = st; }
            return st;
        }

        static int _step = -1;
        static float _nextSweep, _nextReport;
        static string _scene = "?";

        static MelonPreferences_Entry<bool> _on;
        static bool _read;

        static bool Enabled()
        {
            if (!_read)
            {
                _read = true;
                try
                {
                    var cfg = MelonPreferences.GetCategory("HSR")
                           ?? MelonPreferences.CreateCategory("HSR");
                    _on = cfg.GetEntry<bool>("LeakTracker")
                       ?? cfg.CreateEntry("LeakTracker", false,
                            description: "Per-type object-count sweeps for leak hunting. OFF by "
                                       + "default: it calls Resources.FindObjectsOfTypeAll, which "
                                       + "enumerates objects that are being destroyed, and it "
                                       + "cannot see a scene change coming. Costs a 30-50 ms "
                                       + "hitch per type. Turn on only while chasing a leak.");
                }
                catch { }
            }
            return _on != null && _on.Value;
        }

        internal static void SceneChanged(string scene)
        {
            _scene = scene ?? "?";
            Stat(_scene).Visits++;
            _step = -1;
            _nextSweep = Time.realtimeSinceStartup + 10f;
        }

        internal static void Suspend()
        {
            _step = -1;
            _nextSweep = Time.realtimeSinceStartup + 15f;
        }

        static void EnsureTypes()
        {
            if (_types != null) return;
            _types = new Il2CppSystem.Type[6];
            _types[0] = Il2CppType.Of<GameObject>();
            _types[1] = Il2CppType.Of<Material>();
            _types[2] = Il2CppType.Of<Texture2D>();
            _types[3] = Il2CppType.Of<Mesh>();
            _types[4] = Il2CppType.Of<AudioClip>();
            _types[5] = Il2CppType.Of<Renderer>();
        }

        internal static void Tick(MelonLogger.Instance log)
        {
            if (!Enabled()) return;
            try
            {
                float t = Time.realtimeSinceStartup;

                if (_nextReport == 0f) { _nextReport = t + ReportSeconds; _nextSweep = t + 15f; }

                if (t >= _nextReport)
                {
                    _nextReport = t + ReportSeconds;
                    long total = 0, mono = 0;
                    try { total = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong(); } catch { }
                    try { mono = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong(); } catch { }
                    log.Msg(string.Format("LEAK mem: native={0,7:F1}MB  mono={1,6:F1}MB  gc0={2}  scene='{3}'",
                        total / 1048576.0, mono / 1048576.0, GC.CollectionCount(0), _scene));
                }

                if (_step < 0)
                {
                    if (t < _nextSweep) return;
                    EnsureTypes();
                    _step = 0;
                    return;
                }

                if (_step < Names.Length)
                {
                    try
                    {
                        var arr = Resources.FindObjectsOfTypeAll(_types[_step]);
                        _now[_step] = arr == null ? -1 : arr.Length;
                    }
                    catch { _now[_step] = -1; }
                    _step++;
                    return;
                }

                _step = -1;
                _nextSweep = Time.realtimeSinceStartup + SweepSeconds;

                var st = Stat(_scene);
                var sb = new System.Text.StringBuilder();
                sb.Append("LEAK objects in '").Append(_scene).Append("' (visit ")
                  .Append(st.Visits < 1 ? 1 : st.Visits).Append("):");
                for (int i = 0; i < Names.Length; i++)
                {
                    sb.Append("\n  ").Append(Names[i].PadRight(11))
                      .Append(_now[i].ToString().PadLeft(8));
                    if (st.HaveFirst)
                    {
                        int dPrev = _now[i] - st.Prev[i];
                        int dFirst = _now[i] - st.First[i];
                        sb.Append("   since last ").Append((dPrev >= 0 ? "+" : "") + dPrev)
                          .Append("   since first visit ").Append((dFirst >= 0 ? "+" : "") + dFirst);

                        if (dPrev > 100) sb.Append("   <-- STILL GROWING");
                    }
                    else sb.Append("   (first visit to this scene)");
                }
                log.Msg(sb.ToString());

                for (int i = 0; i < Names.Length; i++)
                {
                    if (!st.HaveFirst) st.First[i] = _now[i];
                    st.Prev[i] = _now[i];
                }
                st.HaveFirst = true;
            }
            catch (Exception e) { log.Warning("LEAK: " + e.Message); }
        }
    }
}
