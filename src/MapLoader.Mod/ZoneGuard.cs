// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace MapLoader
{

    static class ZoneGuard
    {
        static MelonLogger.Instance _log;
        static readonly System.Collections.Generic.HashSet<int> _said =
            new System.Collections.Generic.HashSet<int>();
        static int _blocked;

        public static void Install(HarmonyLib.Harmony h, MelonLogger.Instance log)
        {
            _log = log;
            try
            {
                var m = AccessTools.Method(typeof(Il2Cpp.DamageZone), "Update");
                if (m == null) { _log?.Warning("[zone] DamageZone.Update not found; zones stay live"); return; }
                h.Patch(m, prefix: new HarmonyMethod(typeof(ZoneGuard), nameof(Pre_Update)));
                _log?.Msg("[zone] watching DamageZone.Update: no zone damage while an ODG map is up");
            }
            catch (Exception e) { _log?.Warning("[zone] patch: " + e.Message); }

            try
            {
                var td = AccessTools.Method(typeof(Il2Cpp.Health), "CMDTakeDamage");
                if (td == null) { _log?.Warning("[dmg] Health.CMDTakeDamage not found; heavy hits unlogged"); return; }
                h.Patch(td, prefix: new HarmonyMethod(typeof(ZoneGuard), nameof(Pre_TakeDamage)));
                _log?.Msg("[dmg] watching Health.CMDTakeDamage (server-part and heavy hits on ODG maps are logged)");
            }
            catch (Exception e) { _log?.Warning("[dmg] patch: " + e.Message); }
        }

        static float _lastDmgLog;
        static int _dmgLogged;

        static void Pre_TakeDamage(Il2Cpp.Health __instance, float _damage, Il2Cpp.HealthBodyPart _bodyPart,
                                   Il2Cpp.HealthBodyPart crossedDamage, int shooterID)
        {
            try
            {
                if (!MapRuntime.Done) return;
                bool serverPart = _bodyPart == Il2Cpp.HealthBodyPart.SERVER;
                if (!serverPart && _damage < 25f) return;
                float now = Time.realtimeSinceStartup;
                if (now - _lastDmgLog < 0.2f && _dmgLogged > 8) return;
                if (now - _lastDmgLog >= 0.2f) _dmgLogged = 0;
                _lastDmgLog = now; _dmgLogged++;
                string who = "?";
                try { who = __instance.gameObject.name; } catch { }
                Vector3 p = Vector3.zero;
                try { p = __instance.transform.position; } catch { }
                _log?.Msg("[dmg] '" + who + "' takes " + _damage.ToString("0") + " to " + _bodyPart + " (crossed " + crossedDamage
                        + ") from shooter " + shooterID + " at (" + p.x.ToString("0.0") + ", " + p.y.ToString("0.0") + ", "
                        + p.z.ToString("0.0") + ")" + (serverPart ? "  <-- SERVER-part damage: a zone or a script, not a shot and not a landing" : ""));
            }
            catch { }
        }

        public static void ResetForNewLevel() { _said.Clear(); _blocked = 0; }

        static string Chain(Transform t)
        {
            try
            {
                string s = t.name; int hops = 0;
                while (t.parent != null && hops++ < 8) { t = t.parent; s = t.name + "/" + s; }
                return s;
            }
            catch { return "?"; }
        }

        static void Say(Il2Cpp.DamageZone z, string how)
        {
            try
            {
                int id = z.GetInstanceID();
                if (!_said.Add(id)) return;
                float amt = 0f, itv = 0f;
                try { amt = z.DamageAmount; itv = z.DamageInterval; } catch { }
                var p = z.transform.position;
                _log?.Msg("[zone] DamageZone '" + Chain(z.transform) + "' in scene '" + z.gameObject.scene.name + "' at ("
                        + p.x.ToString("0") + ", " + p.y.ToString("0") + ", " + p.z.ToString("0") + "): "
                        + amt.ToString("0") + " damage every " + itv.ToString("0.0") + " s -- " + how);
            }
            catch { }
        }

        public static int SweepScene(UnityEngine.SceneManagement.Scene sc)
        {
            int off = 0;
            try
            {
                var all = Resources.FindObjectsOfTypeAll<Il2Cpp.DamageZone>();
                for (int i = 0; i < (all != null ? all.Length : 0); i++)
                {
                    var z = all[i];
                    if (z == null || z.gameObject == null) continue;
                    if (!z.gameObject.scene.IsValid() || z.gameObject.scene != sc) continue;
                    Say(z, "DISABLED at additive-ready");
                    try { z.enabled = false; off++; } catch { }
                    try
                    {

                        var cols = z.GetComponents<Collider>();
                        for (int k = 0; k < (cols != null ? cols.Length : 0); k++)
                            if (cols[k] != null && cols[k].isTrigger) cols[k].enabled = false;
                    }
                    catch { }
                }
            }
            catch (Exception e) { _log?.Warning("[zone] sweep: " + e.Message); }
            return off;
        }

        static bool Pre_Update(Il2Cpp.DamageZone __instance)
        {
            try
            {
                if (!MapRuntime.Done) return true;
                if (__instance != null)
                {
                    Say(__instance, "BLOCKED: its Update never runs while an ODG map is up");
                    _blocked++;
                }
                return false;
            }
            catch { return true; }
        }
    }
}
