// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace MapLoader
{

    public static class DoorTouch
    {
        static MelonLogger.Instance _log;
        static bool _installed;
        static int _reported;

        public static void Init(MelonLogger.Instance log) { _log = log; }

        public static void Install(HarmonyLib.Harmony h, MelonLogger.Instance log)
        {
            _log = log;
            if (_installed) return;
            _installed = true;

            Hook(h, "TryStartInteraction",    nameof(Post_Try));
            Hook(h, "StartHandleInteraction", nameof(Post_Handle));
            Hook(h, "StartCenterInteraction", nameof(Post_Center));
            Hook(h, "FinishStartInteraction", nameof(Post_Finish));
            Hook(h, "StopInteraction",        nameof(Post_Stop));
        }

        static void Hook(HarmonyLib.Harmony h, string method, string postfix)
        {
            try
            {
                var m = AccessTools.Method(typeof(Il2Cpp.DoorHandleV2), method);
                if (m == null)
                {
                    _log?.Warning("[doortouch] NOT HOOKED: DoorHandleV2." + method
                                + " does not exist on this build");
                    return;
                }
                h.Patch(m, postfix: new HarmonyMethod(typeof(DoorTouch), postfix));
                _log?.Msg("[doortouch] hooked DoorHandleV2." + method);
            }
            catch (Exception e)
            { _log?.Error("[doortouch] hooking " + method + " failed: " + e.Message); }
        }

        static void Post_Try(Il2Cpp.DoorHandleV2 __instance)    { Say(__instance, "REACHED FOR"); }
        static void Post_Handle(Il2Cpp.DoorHandleV2 __instance) { Say(__instance, "GRABBED BY THE HANDLE"); }
        static void Post_Center(Il2Cpp.DoorHandleV2 __instance) { Say(__instance, "GRABBED BY THE CENTRE"); }
        static void Post_Finish(Il2Cpp.DoorHandleV2 __instance) { Say(__instance, "GRAB ESTABLISHED"); }
        static void Post_Stop(Il2Cpp.DoorHandleV2 __instance)   { Say(__instance, "LET GO"); }

        static void Say(Il2Cpp.DoorHandleV2 h, string what)
        {
            try
            {
                if (h == null) { _log?.Msg("[doortouch] " + what + " <null handle>"); return; }

                var d = Get(() => h.doorV2);
                GameObject go = null;
                try { go = d != null ? d.gameObject : h.gameObject; } catch { }
                string name = go != null ? go.name : "?";
                bool ours = name.StartsWith("MapLoader_Door", StringComparison.Ordinal);

                _log?.Msg("[doortouch] " + what + "  " + (ours ? "OURS" : "shipped")
                        + " '" + name + "'"
                        + "  handleDist=" + Num(() => h.distanceHandleToPlayer)
                        + "  centerDist=" + Num(() => h.distanceCenterToPlayer)
                        + "  grabbedHandle=" + Num(() => h.GrabbedHandle)
                        + "  grabbedCentre=" + Num(() => h.GrabbedCenter)
                        + "  locked=" + Num(() => d.CurrentlyLocked)
                        + "  inUse=" + Num(() => d.inUse)

                        + (d != null && (what.StartsWith("GRABBED", StringComparison.Ordinal) || what == "LET GO")
                               ? "  | " + DoorCensus.Describe(d) : ""));

                if (_reported < 2 && go != null)
                {
                    _reported++;
                    DoorReport.Dump(go, "the door you just touched (" + what + ")");
                }
            }
            catch (Exception e) { _log?.Warning("[doortouch] " + e.Message); }
        }

        static T Get<T>(Func<T> f) where T : class
        { try { return f(); } catch { return null; } }

        static string Num<T>(Func<T> f)
        { try { var v = f(); return v == null ? "null" : v.ToString(); } catch { return "?"; } }

        public static void ResetForNewLevel() { _reported = 0; }
    }
}
