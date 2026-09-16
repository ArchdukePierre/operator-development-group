// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace MapLoader
{

    public static class SpawnGuard
    {
        static MelonLogger.Instance _log;
        static MelonPreferences_Entry<bool>  _enabled;
        static MelonPreferences_Entry<float> _grace;
        static MelonPreferences_Entry<float> _hardCap;

        static bool  _armed;
        static float _openedAt;
        static float _allLoadedAt = -1f;
        static int   _blocked;
        static int   _identified;
        static bool  _boundMain, _boundFromPos;

        static Il2Cpp.GameManagerNetwork _gmn;
        static float _nextLookup;

        public static void Init(MelonLogger.Instance log)
        {
            _log = log;
            try
            {
                var cfg = MelonPreferences.GetCategory("MapLoader")
                       ?? MelonPreferences.CreateCategory("MapLoader");
                _enabled = cfg.GetEntry<bool>("SpawnProtection")
                        ?? cfg.CreateEntry("SpawnProtection", true,
                             description: "Stop players being killed while still loading into a mission. "
                                        + "Host decides; protects everyone in the lobby.");
                _grace = cfg.GetEntry<float>("SpawnProtectionGraceSec")
                      ?? cfg.CreateEntry("SpawnProtectionGraceSec", 3f,
                             description: "Extra seconds of protection after everyone has finished loading.");
                _hardCap = cfg.GetEntry<float>("SpawnProtectionMaxSec")
                        ?? cfg.CreateEntry("SpawnProtectionMaxSec", 45f,
                             description: "Absolute ceiling on the protection window. Closes even if "
                                        + "somebody never finishes loading. Keeps it un-abusable.");
            }
            catch (Exception e) { _log?.Warning("[guard] prefs: " + e.Message); }
        }

        public static void Install(HarmonyLib.Harmony h, MelonLogger.Instance log)
        {
            _log = log;
            _boundMain    = TryPatch(h, "UserCode_CMDTakeDamage__Single__HealthBodyPart__HealthBodyPart__Int32",
                                     new[] { typeof(float), typeof(Il2Cpp.HealthBodyPart),
                                             typeof(Il2Cpp.HealthBodyPart), typeof(int) },
                                     nameof(Pre_TakeDamage));
            _boundFromPos = TryPatch(h, "UserCode_CMDTakeDamageFromPosition__Single__HealthBodyPart__HealthBodyPart__Int32__Vector3",
                                     new[] { typeof(float), typeof(Il2Cpp.HealthBodyPart),
                                             typeof(Il2Cpp.HealthBodyPart), typeof(int), typeof(Vector3) },
                                     nameof(Pre_TakeDamageFromPosition));

            if (_boundMain && _boundFromPos)
                _log?.Msg("[guard] spawn protection armed on both damage chokepoints.");
            else
                _log?.Error("[guard] spawn protection is INCOMPLETE (main=" + _boundMain
                          + " fromPosition=" + _boundFromPos + "). Damage will still get through "
                          + "the unbound path; treat protection as off.");
        }

        static bool TryPatch(HarmonyLib.Harmony h, string method, Type[] args, string prefix)
        {
            try
            {
                var m = AccessTools.Method(typeof(Il2Cpp.Health), method, args);
                if (m == null) { _log?.Warning("[guard] no Health." + method); return false; }
                h.Patch(m, prefix: new HarmonyMethod(typeof(SpawnGuard), prefix));
                return true;
            }
            catch (Exception e) { _log?.Error("[guard] patching " + method + ": " + e.Message); return false; }
        }

        static bool IsMissionScene(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            if (s == "3d Menu" || s == "Operation Room" || s == "ApplicationSplashScreen") return false;
            return true;
        }

        public static void OnSceneLoaded(string sceneName)
        {
            try
            {
                if (_enabled != null && !_enabled.Value) return;
                if (!IsMissionScene(sceneName)) { _armed = false; return; }

                _armed = true;
                _openedAt = Time.time;
                _allLoadedAt = -1f;
                _blocked = 0;
                _identified = 0;
                _gmn = null;
                _log?.Msg("[guard] spawn protection OPEN for '" + sceneName + "' -- players cannot be "
                        + "damaged until everyone has loaded (+" + Grace().ToString("F0") + " s), or "
                        + Cap().ToString("F0") + " s, whichever comes first.");
            }
            catch { }
        }

        static float Grace() { return _grace   != null ? Mathf.Max(0f, _grace.Value)   : 3f; }
        static float Cap()   { return _hardCap != null ? Mathf.Max(1f, _hardCap.Value) : 45f; }

        public static void Tick()
        {
            if (!_armed) return;
            try
            {

                if (Time.time - _openedAt >= Cap()) { Close("the " + Cap().ToString("F0") + " s ceiling"); return; }

                if (_allLoadedAt < 0f && AllLoaded()) _allLoadedAt = Time.time;
                if (_allLoadedAt >= 0f && Time.time - _allLoadedAt >= Grace())
                    Close("everyone finished loading");
            }
            catch { }
        }

        static void Close(string why)
        {
            _armed = false;
            _log?.Msg("[guard] spawn protection CLOSED (" + why + "). Blocked " + _blocked
                    + " incoming hit(s) on " + _identified + " player(s) while it was open.");
        }

        static bool AllLoaded()
        {
            try
            {
                if (_gmn == null && Time.time >= _nextLookup)
                {
                    _nextLookup = Time.time + 1f;
                    _gmn = UnityEngine.Object.FindObjectOfType<Il2Cpp.GameManagerNetwork>();
                }
                if (_gmn == null) return false;
                return _gmn.AllPlayersLoaded;
            }
            catch { return false; }
        }

        static bool Pre_TakeDamage(Il2Cpp.Health __instance, float _damage,
                                   Il2Cpp.HealthBodyPart _bodyPart,
                                   Il2Cpp.HealthBodyPart crossedDamage, int shooterID)
        {
            return !ShouldBlock(__instance);
        }

        static bool Pre_TakeDamageFromPosition(Il2Cpp.Health __instance, float _damage,
                                               Il2Cpp.HealthBodyPart _bodyPart,
                                               Il2Cpp.HealthBodyPart crossedDamage, int shooterID,
                                               Vector3 position)
        {
            return !ShouldBlock(__instance);
        }

        static bool ShouldBlock(Il2Cpp.Health victim)
        {
            try
            {
                if (!_armed) return false;
                if (victim == null) return false;

                if (!Il2CppMirror.NetworkServer.active) return false;

                if (IsBot(victim)) return false;

                _blocked++;

                if (_blocked <= 5 || _blocked % 50 == 0)
                {
                    string who = "?";
                    try { who = victim.gameObject.name; } catch { }
                    _log?.Msg("[guard] blocked incoming damage on '" + who + "' (" + _blocked
                            + " so far) -- still inside the spawn window.");
                }
                return true;
            }
            catch { return false; }
        }

        static bool IsBot(Il2Cpp.Health h)
        {
            try
            {
                var go = h.gameObject;
                if (go == null) return true;
                if (go.GetComponentInParent<Il2Cpp.BrainAI>() != null) return true;
                if (_identified < 8)
                {
                    _identified++;
                    _log?.Msg("[guard] treating '" + go.name + "' as a player for protection.");
                }
                return false;
            }
            catch { return true; }
        }

        public static void Report()
        {
            _log?.Msg("[guard] enabled=" + (_enabled == null || _enabled.Value)
                    + "  bound(main=" + _boundMain + ", fromPos=" + _boundFromPos + ")"
                    + "  armed=" + _armed
                    + (_armed ? "  openFor=" + (Time.time - _openedAt).ToString("F1") + " s" : "")
                    + "  grace=" + Grace().ToString("F0") + " s  cap=" + Cap().ToString("F0") + " s"
                    + "  blocked=" + _blocked);
        }
    }
}
