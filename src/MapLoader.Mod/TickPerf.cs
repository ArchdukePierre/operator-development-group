// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using MelonLoader;

namespace MapLoader
{

    internal static class TickPerf
    {
        static readonly Dictionary<string, int> _slot = new Dictionary<string, int>();
        static readonly List<string> _names = new List<string>();
        static readonly List<long> _ticks = new List<long>();
        static readonly List<long> _peak = new List<long>();
        static readonly List<int> _calls = new List<int>();
        static long _lastReport;
        static readonly double ToMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        internal static bool Enabled = true;

        internal static long Begin()
        {
            return Enabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
        }

        internal static void End(string name, long start)
        {
            if (!Enabled || start == 0L) return;
            long d = System.Diagnostics.Stopwatch.GetTimestamp() - start;
            int i;
            if (!_slot.TryGetValue(name, out i))
            {
                i = _names.Count;
                _slot[name] = i;
                _names.Add(name); _ticks.Add(0L); _peak.Add(0L); _calls.Add(0);
            }
            _ticks[i] = _ticks[i] + d;
            if (d > _peak[i]) _peak[i] = d;
            _calls[i] = _calls[i] + 1;
        }

        internal static void MaybeReport(MelonLogger.Instance log)
        {
            if (!Enabled || log == null) return;
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (_lastReport == 0L) { _lastReport = now; return; }
            double elapsedMs = (now - _lastReport) * ToMs;
            if (elapsedMs < 30000.0) return;
            _lastReport = now;

            var rows = new List<string>();
            double grand = 0.0;
            for (int i = 0; i < _names.Count; i++)
            {
                double total = _ticks[i] * ToMs;
                grand += total;
                double peak = _peak[i] * ToMs;

                if (total >= 5.0 || peak >= 8.0)
                    rows.Add(string.Format("  {0,-22} calls={1,6}  total={2,9:F1}ms  avg={3,7:F3}ms  peak={4,7:F2}ms  = {5,5:F2}% of wall",
                        _names[i], _calls[i], total, total / Math.Max(1, _calls[i]), peak, 100.0 * total / elapsedMs));
                _ticks[i] = 0L; _peak[i] = 0L; _calls[i] = 0;
            }

            if (rows.Count == 0)
            {
                log.Msg(string.Format("[tickperf] over {0:F1}s: nothing above 5ms total or 8ms peak "
                                    + "({1:F1}ms across all ticks = {2:F2}% of wall)",
                                    elapsedMs / 1000.0, grand, 100.0 * grand / elapsedMs));
                return;
            }
            log.Msg(string.Format("[tickperf] over {0:F1}s  (all ticks {1:F1}ms = {2:F2}% of wall):",
                                  elapsedMs / 1000.0, grand, 100.0 * grand / elapsedMs));
            for (int i = 0; i < rows.Count; i++) log.Msg(rows[i]);
        }
    }
}
