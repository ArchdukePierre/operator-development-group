// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace MapLoader
{

    static class FallGuard
    {
        static MelonLogger.Instance _log;
        static float _softMps = 7.5f;
        const float PlacementGraceS = 6f;

        public static void Install(HarmonyLib.Harmony h, MelonLogger.Instance log)
        {
            _log = log;
            try
            {
                var m = AccessTools.Method(typeof(Il2Cpp.MasterController), "ProcessFall");
                if (m == null) { _log?.Warning("[fall] MasterController.ProcessFall not found; fall damage untouched"); return; }
                h.Patch(m, prefix: new HarmonyMethod(typeof(FallGuard), nameof(Pre_ProcessFall)));
                _log?.Msg("[fall] watching MasterController.ProcessFall (placement grace " + PlacementGraceS
                        + " s, soft landing under " + _softMps.ToString("0.0") + " m/s on ODG maps)");
            }
            catch (Exception e) { _log?.Warning("[fall] patch: " + e.Message); }
        }

        static bool Pre_ProcessFall(Vector3 velocity)
        {
            try
            {
                if (!MapRuntime.Done) return true;
                float down = Mathf.Max(-velocity.y, 0f);
                string ride;
                if (BlackHawk.GuardsFall(out ride))
                {
                    _log?.Msg("[fall] landing at " + down.ToString("0.0") + " m/s " + ride + ": no damage.");
                    return false;
                }
                float since = PlayerBody.LastMoveAt > 0f ? Time.realtimeSinceStartup - PlayerBody.LastMoveAt : 1e9f;
                if (since < PlacementGraceS)
                {
                    _log?.Msg("[fall] landing at " + down.ToString("0.0") + " m/s " + since.ToString("0.0")
                            + " s after a placement: a placement is not a fall, no damage.");
                    return false;
                }
                if (down < _softMps)
                {
                    _log?.Msg("[fall] landing at " + down.ToString("0.0") + " m/s (under " + _softMps.ToString("0.0")
                            + "): no damage on this map.");
                    return false;
                }
                _log?.Msg("[fall] landing at " + down.ToString("0.0") + " m/s: the game's fall damage applies.");
            }
            catch { }
            return true;
        }

        public static string Console(string args)
        {
            float v;
            if (!string.IsNullOrEmpty(args)
                && float.TryParse(args, System.Globalization.NumberStyles.Float,
                                  System.Globalization.CultureInfo.InvariantCulture, out v))
            {
                _softMps = Mathf.Clamp(v, 0f, 30f);
                return "soft landing threshold on ODG maps: " + _softMps.ToString("0.0") + " m/s (this session)";
            }
            return "fall damage on ODG maps: landings under " + _softMps.ToString("0.0") + " m/s and any landing within "
                 + PlacementGraceS + " s of a placement cost nothing; 'odg fall <mps>' changes the threshold. "
                 + "Every landing is logged as [fall].";
        }
    }
}
