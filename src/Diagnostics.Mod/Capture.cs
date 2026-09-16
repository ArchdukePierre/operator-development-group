// ODG Operations - Diagnostics
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace ODGDiag
{

    internal static class Capture
    {

        const float WindowSeconds = 2f;

        const int MaxPerWindow = 400;

        sealed class Gate
        {
            public int Written;
            public int Dropped;
            public float WindowEnd;

            public bool Pass(string tag)
            {

                float now = (float)(System.Diagnostics.Stopwatch.GetTimestamp()
                                    / (double)System.Diagnostics.Stopwatch.Frequency);
                lock (this)
                {
                    if (now >= WindowEnd)
                    {
                        if (Dropped > 0)
                            Journal.Write(tag + " [" + Dropped + " line(s) rate-limited]");
                        Written = 0; Dropped = 0; WindowEnd = now + WindowSeconds;
                    }
                    if (Written >= MaxPerWindow) { Dropped++; return false; }
                    Written++;
                    return true;
                }
            }
        }

        static readonly Gate _mlGate = new Gate();
        static readonly Gate _unityGate = new Gate();

        static readonly Gate _errGate = new Gate();

        static readonly Dictionary<string, int> _unitySeen = new Dictionary<string, int>();
        const int UnitySeenCap = 256;

        static Application.LogCallback _unityHook;

        public static string Install()
        {
            string melon = "off", unity = "off";

            try
            {

                MelonLogger.MsgDrawingCallbackHandler += OnMelonMsg;
                MelonLogger.WarningCallbackHandler    += OnMelonWarning;
                MelonLogger.ErrorCallbackHandler      += OnMelonError;
                melon = "on";
            }
            catch (Exception e) { Journal.Write("capture: melonlog tap failed: " + e.Message); }

            try
            {

                _unityHook = (Application.LogCallback)OnUnityLog;
                Application.add_logMessageReceivedThreaded(_unityHook);
                unity = "on";
            }
            catch (Exception e) { Journal.Write("capture: unity tap failed: " + e.Message); }

            return "capture: melonlog=" + melon + " unitylog=" + unity;
        }

        static void OnMelonMsg(MelonLoader.Logging.ColorARGB c1, MelonLoader.Logging.ColorARGB c2, string section, string text)
        {
            if (IsOurs(section)) return;
            if (!_mlGate.Pass("ML")) return;
            Journal.Write("ML  [" + section + "] " + text);
        }

        static void OnMelonWarning(string section, string text)
        {
            if (!_errGate.Pass("MLW")) return;
            Journal.Write("MLW [" + section + "] " + text);
        }

        static void OnMelonError(string section, string text)
        {
            if (!_errGate.Pass("MLE")) return;
            Journal.Write("MLE [" + section + "] " + text);
        }

        static bool IsOurs(string section)
            => section != null && section.IndexOf("Diagnostics", StringComparison.OrdinalIgnoreCase) >= 0;

        static void OnUnityLog(string condition, string stackTrace, LogType type)
        {
            try
            {

                if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
                    return;

                string key = condition ?? "<null>";
                if (key.Length > 120) key = key.Substring(0, 120);

                int seen;
                lock (_unitySeen)
                {

                    if (!_unitySeen.ContainsKey(key) && _unitySeen.Count >= UnitySeenCap)
                    {
                        _unitySeen.Clear();
                        Journal.Write("UNITY [error-dedupe table reset: "
                                    + UnitySeenCap + " distinct messages seen]");
                    }
                    _unitySeen.TryGetValue(key, out seen);
                    _unitySeen[key] = ++seen;
                }

                bool full = seen <= 3;
                bool tick = seen % 100 == 0;
                if (!full && !tick) return;
                if (!_unityGate.Pass("UNITY")) return;

                if (full)
                {
                    string line = "UNITY " + type + ": " + key;

                    if (type == LogType.Exception && !string.IsNullOrEmpty(stackTrace))
                    {
                        int nl = stackTrace.IndexOf('\n');
                        line += "  @ " + (nl > 0 ? stackTrace.Substring(0, nl) : stackTrace).Trim();
                    }
                    Journal.Write(line);
                }
                else
                {
                    Journal.Write("UNITY " + type + " (x" + seen + "): " + key);
                }
            }
            catch { }
        }
    }
}
