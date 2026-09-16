// ODG Operations - NewSlots
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using MelonLoader;

namespace NewSlots
{

    internal static class BuildPerf
    {
        static readonly Dictionary<string, int> _slot = new Dictionary<string, int>();
        static readonly List<string> _names = new List<string>();
        static readonly List<long> _ticks = new List<long>();
        static readonly List<long> _peak = new List<long>();
        static readonly List<int> _calls = new List<int>();
        static readonly double ToMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        internal static long Begin() { return System.Diagnostics.Stopwatch.GetTimestamp(); }

        internal static void End(string phase, long start)
        {
            if (start == 0L) return;
            long d = System.Diagnostics.Stopwatch.GetTimestamp() - start;
            int i;
            if (!_slot.TryGetValue(phase, out i))
            {
                i = _names.Count;
                _slot[phase] = i;
                _names.Add(phase); _ticks.Add(0L); _peak.Add(0L); _calls.Add(0);
            }
            _ticks[i] = _ticks[i] + d;
            if (d > _peak[i]) _peak[i] = d;
            _calls[i] = _calls[i] + 1;
        }

        internal static void Report(Action<string> log)
        {
            if (log == null || _names.Count == 0) return;
            double grand = 0.0;
            for (int i = 0; i < _names.Count; i++) grand += _ticks[i] * ToMs;

            log("[buildperf] item build finished. Total measured " + grand.ToString("F0") + " ms across "
              + _names.Count + " phase(s), heaviest first:");

            var order = new List<int>();
            for (int i = 0; i < _names.Count; i++) order.Add(i);
            order.Sort(delegate (int a, int b) { return _ticks[b].CompareTo(_ticks[a]); });

            for (int k = 0; k < order.Count; k++)
            {
                int i = order[k];
                double total = _ticks[i] * ToMs;
                if (total < 1.0) continue;
                log("  " + _names[i].PadRight(20)
                  + " calls=" + _calls[i].ToString().PadLeft(5)
                  + "  total=" + total.ToString("F0").PadLeft(7) + "ms"
                  + "  avg=" + (total / Math.Max(1, _calls[i])).ToString("F1").PadLeft(7) + "ms"
                  + "  peak=" + (_peak[i] * ToMs).ToString("F0").PadLeft(6) + "ms"
                  + "  = " + (100.0 * total / Math.Max(1.0, grand)).ToString("F1").PadLeft(5) + "% of build");
            }
        }
    }
}
