// ODG Operations - M107
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace M107
{

    internal static class Perf
    {
        const int Slots = 9;
        internal const int Update = 0, LateUpdate = 1, Sweep = 2, Discover = 3,
                           Hide = 4, Rails = 5, MagFollowTick = 6, PinArt = 7, WorldScan = 8;

        static readonly string[] Names =
        {
            "OnUpdate", "OnLateUpdate", "SweepLive", "DiscoverWeapons",
            "HideUnder", "PlaceRails", "MagFollow.Tick", "PinAllArt", "WorldScan",
        };

        static readonly long[] _ticks = new long[Slots];
        static readonly long[] _peak = new long[Slots];
        static readonly int[] _calls = new int[Slots];
        static long _lastReport;
        static readonly double TicksToMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        internal static long Begin() => System.Diagnostics.Stopwatch.GetTimestamp();

        internal static void End(int slot, long start)
        {
            long d = System.Diagnostics.Stopwatch.GetTimestamp() - start;
            _ticks[slot] += d;
            if (d > _peak[slot]) _peak[slot] = d;
            _calls[slot]++;
        }

        internal static void MaybeReport()
        {
            try
            {
                long now = System.Diagnostics.Stopwatch.GetTimestamp();
                if (_lastReport == 0) { _lastReport = now; return; }
                double elapsedMs = (now - _lastReport) * TicksToMs;
                if (elapsedMs < 30000.0) return;
                _lastReport = now;

                var sb = new System.Text.StringBuilder();
                sb.Append("PERF over ").Append((elapsedMs / 1000.0).ToString("F1")).Append("s:");
                bool any = false;
                for (int i = 0; i < Slots; i++)
                {
                    if (_calls[i] == 0) continue;
                    any = true;
                    double total = _ticks[i] * TicksToMs;
                    double peak = _peak[i] * TicksToMs;
                    sb.Append("\n  ").Append(Names[i].PadRight(16))
                      .Append(" calls=").Append(_calls[i].ToString().PadLeft(6))
                      .Append("  total=").Append(total.ToString("F1").PadLeft(8)).Append("ms")
                      .Append("  avg=").Append((total / _calls[i]).ToString("F3")).Append("ms")
                      .Append("  peak=").Append(peak.ToString("F2")).Append("ms")
                      .Append("  = ").Append((total / elapsedMs * 100.0).ToString("F2")).Append("%")
                      .Append(peak > 8.0 ? "   <<< STUTTER, over half a 60fps frame" : "");
                    _ticks[i] = 0; _peak[i] = 0; _calls[i] = 0;
                }
                if (any) M107Mod.Log(sb.ToString());
            }
            catch { }
        }
    }
}
