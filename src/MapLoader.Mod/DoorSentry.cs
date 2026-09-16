// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace MapLoader
{

    public static class DoorSentry
    {
        static MelonLogger.Instance _log;
        public static void Init(MelonLogger.Instance log) { _log = log; }

        public static void Install(HarmonyLib.Harmony h, MelonLogger.Instance log)
        {
            _log = log;
            int ok = 0;

            ok += Patch(h, "AddForce", nameof(Post_AddForce));
            ok += Patch(h, "AddForceAndUnlatch", nameof(Post_AddForceAndUnlatch));
            ok += Patch(h, "AddDirectRotation", nameof(Post_AddDirectRotation));
            ok += Patch(h, "HitDoor", nameof(Post_HitDoor));
            ok += Patch(h, "UserCode_CMD_AddForce__Single__NetworkIdentity", nameof(Post_CmdForce));
            ok += Patch(h, "UserCode_CMD_AddRotation__Single__NetworkIdentity", nameof(Post_CmdRotation));
            _log?.Msg("[sentry] watching " + ok + " door-rotation writer(s)");
        }

        static int Patch(HarmonyLib.Harmony h, string method, string postfix)
        {
            try
            {
                var m = AccessTools.Method(typeof(Il2Cpp.DoorV2), method);
                if (m == null) { _log?.Warning("[sentry] no method " + method); return 0; }
                h.Patch(m, postfix: new HarmonyMethod(typeof(DoorSentry), postfix));
                return 1;
            }
            catch (Exception e)
            {
                _log?.Warning("[sentry] patch " + method + ": " + e.Message);
                return 0;
            }
        }

        static string Who(Il2Cpp.PlayerNetworking p)
        {

            if (p == null) return "NO PLAYER (AI or scripted)";
            try { return "player '" + p.gameObject.name + "'"; } catch { }
            return "player <unreadable>";
        }

        internal static bool Trace;

        static void Say(Il2Cpp.DoorV2 d, string what)
        {
            if (!Trace) return;
            try
            {
                _log?.Msg("[sentry] '" + (d != null ? d.gameObject.name : "?") + "' " + what
                        + (d != null ? "  rot=" + d.currentRotation.ToString("F1")
                                     + " locked=" + d.CurrentlyLocked
                                     + " latched=" + d.IsLatched : ""));
            }
            catch { }
        }

        public static void Post_AddForce(Il2Cpp.DoorV2 __instance, float force, Il2Cpp.PlayerNetworking player)
            => Say(__instance, "AddForce " + force.ToString("F2") + " by " + Who(player));

        public static void Post_AddForceAndUnlatch(Il2Cpp.DoorV2 __instance, float force, Il2Cpp.PlayerNetworking player)
            => Say(__instance, "AddForceAndUnlatch " + force.ToString("F2") + " by " + Who(player));

        public static void Post_AddDirectRotation(Il2Cpp.DoorV2 __instance, float rotation, Il2Cpp.PlayerNetworking player)
            => Say(__instance, "AddDirectRotation " + rotation.ToString("F2") + " by " + Who(player));

        public static void Post_HitDoor(Il2Cpp.DoorV2 __instance, Vector3 _HitPos, float force, float damage, Il2Cpp.PlayerNetworking netID)
            => Say(__instance, "HIT for " + damage.ToString("F0") + " dmg, force " + force.ToString("F1")
                             + " by " + Who(netID));

        public static void Post_CmdForce(Il2Cpp.DoorV2 __instance, float force)
            => Say(__instance, "CMD_AddForce " + force.ToString("F2") + " (network)");

        public static void Post_CmdRotation(Il2Cpp.DoorV2 __instance, float rotation)
            => Say(__instance, "CMD_AddRotation " + rotation.ToString("F2") + " (network)");

        sealed class Watch { public Il2Cpp.DoorV2 D; public bool WasOpen; }
        static readonly List<Watch> _watch = new List<Watch>();
        static float _next;

        public static void Track(Il2Cpp.DoorV2 d)
        {
            if (d == null) return;
            _watch.Add(new Watch { D = d, WasOpen = false });
        }

        public static void Reset() { _watch.Clear(); }

        public static void Tick()
        {
            if (_watch.Count == 0) return;
            float now = Time.unscaledTime;
            if (now < _next) return;
            _next = now + 0.5f;

            for (int i = _watch.Count - 1; i >= 0; i--)
            {
                var w = _watch[i];
                if (w.D == null) { _watch.RemoveAt(i); continue; }
                float rot = 0f;
                bool inUse = false;
                try { rot = Mathf.Abs(w.D.currentRotation); inUse = w.D.inUse; }
                catch { _watch.RemoveAt(i); continue; }

                bool open = rot > 5f;
                if (open && !w.WasOpen)
                {

                    Say(w.D, "LEFT THE CLOSED POSE" + (inUse ? " while held" : " with nobody holding it"));
                }
                w.WasOpen = open;
            }
        }
    }
}
