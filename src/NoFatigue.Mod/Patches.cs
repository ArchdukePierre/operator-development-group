// ODG Operations - NoFatigue
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace NoFatigue
{

    internal static class Patches
    {
        static int _attempted, _bound;
        static readonly List<string> _missed = new List<string>();
        static readonly List<string> _failed = new List<string>();
        static readonly HashSet<string> _fired = new HashSet<string>();

        static Il2Cpp.MasterController _mc;
        static Il2Cpp.ArmStamina _arm;

        static float _restedArm = -1f;

        internal static void Install(HarmonyLib.Harmony h)
        {

            Patch(h, typeof(Il2Cpp.ArmStamina), "Start", null, null, nameof(Post_ArmStart));
            Patch(h, typeof(Il2Cpp.ArmStamina), "Update", null, nameof(Pre_Arm), nameof(Post_Arm));

            Patch(h, typeof(Il2Cpp.WeaponSwayV2), "Update", null, nameof(Pre_SwayV2), null);
            Patch(h, typeof(Il2Cpp.WeaponSway), "Update", null, nameof(Pre_SwayV1), null);

            Patch(h, typeof(Il2Cpp.MasterController), "CalculateTotalWeight", null, null, nameof(Post_Weight));

            Patch(h, typeof(Il2Cpp.MasterController), "GetWeightSpeedMultiplier", null, null, nameof(Post_WeightSpeed));
            Patch(h, typeof(Il2Cpp.MasterController), "GetWeightFallDamageMultiplier", null, null, nameof(Post_WeightFall));

            Patch(h, typeof(Il2Cpp.MasterController), "UpdateSprintStamina", null, nameof(Pre_Stamina), null);
            Patch(h, typeof(Il2Cpp.MasterController), "UpdateSprintBreathingSFX", null, nameof(Pre_BreathSfx), null);
        }

        internal static void ForgetCaches() { _mc = null; _arm = null; }

        static void Post_ArmStart(Il2Cpp.ArmStamina __instance)
        {
            Fired("ArmStamina.Start");
            try
            {
                if (__instance == null) return;
                float v = __instance.armStamina;
                if (v > _restedArm) _restedArm = v;
            }
            catch { }
        }

        static void Pre_Arm(Il2Cpp.ArmStamina __instance)
        {
            Fired("ArmStamina.Update");
            if (!Main.ArmFatigue.Value && !Main.BreathingWobble.Value) return;
            try
            {
                if (__instance == null) return;

                if (_arm != __instance && Owned(__instance.playerNetworking)) _arm = __instance;

                if (Main.ArmFatigue.Value)
                {

                    Il2Cpp.ArmStamina.StaticDrainRateMultiplier = 0f;

                    __instance.drain = false;
                    __instance.drainRate = 0f;

                    if (__instance.recoveryRate < 1000f) __instance.recoveryRate = 1000f;

                    float rested = _restedArm > 0f ? _restedArm : 100f;
                    if (__instance.armStamina < rested) __instance.armStamina = rested;
                }

                if (Main.BreathingWobble.Value)
                {
                    __instance.breathMin = 0f;
                    __instance.breathMax = 0f;
                    __instance.Breath = 0f;
                }
            }
            catch { }
        }

        static void Post_Arm(Il2Cpp.ArmStamina __instance)
        {
            try
            {
                if (__instance == null) return;

                float v = __instance.armStamina;
                if (v > _restedArm) _restedArm = v;

                if (Main.ArmFatigue.Value)
                {
                    float rested = _restedArm > 0f ? _restedArm : 100f;
                    if (__instance.armStamina < rested) __instance.armStamina = rested;
                }
                if (Main.BreathingWobble.Value) __instance.Breath = 0f;
            }
            catch { }
        }

        static void Pre_SwayV2(Il2Cpp.WeaponSwayV2 __instance)
        {
            Fired("WeaponSwayV2.Update");
            if (!Main.WeaponSway.Value) return;
            try
            {
                if (__instance == null) return;
                __instance.EnableSway = false;
                __instance.ResetValues = true;
            }
            catch { }
        }

        static void Pre_SwayV1(Il2Cpp.WeaponSway __instance)
        {
            Fired("WeaponSway.Update");
            if (!Main.WeaponSway.Value) return;
            try
            {
                if (__instance == null) return;
                __instance.allowPosition = false;
                __instance.allowRotation = false;
            }
            catch { }
        }

        static void Post_Weight(Il2Cpp.MasterController __instance)
        {
            Fired("MasterController.CalculateTotalWeight");
            if (!Main.WeightPenalty.Value) return;
            try
            {
                if (__instance == null) return;
                if (Owned(__instance)) _mc = __instance;
                PinWeightFields(__instance);
            }
            catch { }
        }

        static void PinWeightFields(Il2Cpp.MasterController mc)
        {
            mc.totalWeight = 0f;
            mc.maxWeightSpeedPenalty = 0f;
            mc.noJogWeight = 1e9f;
            mc.noSprintWeight = 1e9f;
            if (mc.maxWeight < 1e9f) mc.maxWeight = 1e9f;
            mc.weightDrainMultiplier = 0f;
        }

        static void Post_WeightSpeed(ref float __result)
        {
            Fired("MasterController.GetWeightSpeedMultiplier");
            if (!Main.WeightPenalty.Value) return;
            __result = 1f;
        }

        static void Post_WeightFall(ref float __result)
        {
            Fired("MasterController.GetWeightFallDamageMultiplier");
            if (!Main.WeightPenalty.Value) return;
            __result = 1f;
        }

        static bool Pre_Stamina(Il2Cpp.MasterController __instance)
        {
            Fired("MasterController.UpdateSprintStamina");
            if (!Main.SprintStamina.Value) return true;
            try
            {
                if (__instance == null) return true;
                if (Owned(__instance)) _mc = __instance;

                if (Main.WeightPenalty.Value) PinWeightFields(__instance);

                __instance.sprintStamina = 1f;
                __instance.sprintStaminaDepleted = false;
                __instance.staminaDepletedTimer = 0f;
                __instance.staminaDepletionCount = 0;
                __instance.currentStaminaDelay = 0f;
                __instance.staminaDrainRate = 0f;
                __instance.staminaDepletedDelay = 0f;
                __instance.maxStaminaDelay = 0f;
                __instance.sprintStaminaThreshold = 0f;
                return false;
            }
            catch { return true; }
        }

        static bool Pre_BreathSfx(Il2Cpp.MasterController __instance)
        {
            Fired("MasterController.UpdateSprintBreathingSFX");
            if (!Main.BreathingSfx.Value) return true;
            try
            {
                if (__instance == null) return true;

                var src = __instance.breathingAudioSource;
                if (src != null)
                {
                    if (src.isPlaying) src.Stop();
                    src.volume = 0f;
                }
                __instance.breathingMaxVolume = 0f;
                __instance.outOfBreathStamina = 0f;
                __instance.breathingFadeInStamina = 0f;
                __instance.outOfBreathPlayed = true;
                return false;
            }
            catch { return true; }
        }

        internal static void LogState()
        {
            try
            {
                var mc = _mc;
                if (mc != null)
                {
                    Main.L.Msg(string.Format(
                        "weight {0:0.0} (noJog {1:0} noSprint {2:0} pen {3:0.00})   "
                        + "sprintStam {4:0.000} depleted {5} run {6}",
                        mc.totalWeight, mc.noJogWeight, mc.noSprintWeight, mc.maxWeightSpeedPenalty,
                        mc.sprintStamina, mc.sprintStaminaDepleted, mc.runState));
                }
                var a = _arm;
                if (a != null)
                {
                    Main.L.Msg(string.Format(
                        "armStamina {0:0.0} (rested {1:0.0})   drain {2} rate {3:0.00} "
                        + "staticMul {4:0.00}   Breath {5:0.000} min {6:0.000} max {7:0.000}",
                        a.armStamina, _restedArm, a.drain, a.drainRate,
                        Il2Cpp.ArmStamina.StaticDrainRateMultiplier,
                        a.Breath, a.breathMin, a.breathMax));
                }
                if (mc == null && a == null)
                    Main.L.Msg("nothing patched has fired yet -- not in a mission, or the hooks missed.");
            }
            catch (Exception e) { Main.L.Warning("state dump: " + e.Message); }
        }

        internal static string Summary()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(_bound).Append('/').Append(_attempted).Append(" hooks bound");
            if (_missed.Count > 0) sb.Append("   MISSING: ").Append(string.Join(", ", _missed.ToArray()));
            if (_failed.Count > 0) sb.Append("   FAILED: ").Append(string.Join(", ", _failed.ToArray()));
            return sb.ToString();
        }

        static bool Owned(Il2CppMirror.NetworkBehaviour nb)
        {
            try { return nb != null && nb.isOwned; }
            catch { return false; }
        }

        static void Fired(string what)
        {
            if (_fired.Contains(what)) return;
            _fired.Add(what);
            Main.L.Msg("FIRED  " + what);
        }

        static void Patch(HarmonyLib.Harmony h, Type type, string method, Type[] args,
                          string prefix, string postfix)
        {
            try
            {
                _attempted++;
                var mi = args == null ? AccessTools.Method(type, method)
                                      : AccessTools.Method(type, method, args);
                if (mi == null)
                {
                    _missed.Add(type.Name + "." + method);
                    Main.L.Warning("HOOK MISS  " + type.Name + "." + method
                        + "  (whatever that one covered is still stock)");
                    return;
                }
                h.Patch(mi,
                    prefix == null ? null : new HarmonyMethod(typeof(Patches), prefix),
                    postfix == null ? null : new HarmonyMethod(typeof(Patches), postfix));
                _bound++;
                Main.L.Msg("hooked " + type.Name + "." + method);
            }
            catch (Exception e)
            {
                _failed.Add(type.Name + "." + method + ": " + e.Message);
                Main.L.Error("HOOK FAIL  " + type.Name + "." + method + ": " + e.Message);
            }
        }
    }
}
