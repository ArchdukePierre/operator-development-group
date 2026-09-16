// ODG Operations - ThirdPerson
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace ThirdPerson
{

    internal static class Patches
    {
        internal static bool FreezeLook;
        internal static bool FreezeMove;
        internal static bool BlockFire;

        internal static int Bound;
        internal const int CoreWanted = 1;
        internal const int InputWanted = 4;
        internal static bool InputHooksInstalled;
        internal static int InputHooksBound;

        internal static int CinemachinePrefixCalls;
        internal static int LookCalls;
        internal static int MoveCalls;
        internal static int GetKeyCalls;

        internal static void ApplyCore(HarmonyLib.Harmony harmony, MelonLogger.Instance log)
        {
            Bind(harmony, log, typeof(Il2Cpp.CinemachineUpdater), "LateUpdate", nameof(Pre_CinemachineUpdate), true);
        }

        internal static int EnsureInputHooks(HarmonyLib.Harmony harmony, MelonLogger.Instance log)
        {
            if (InputHooksInstalled) return InputHooksBound;
            InputHooksInstalled = true;
            int before = Bound;
            Bind(harmony, log, typeof(Il2CppCMF.CameraMouseInput), "GetHorizontalCameraInput", nameof(Post_Look), false);
            Bind(harmony, log, typeof(Il2CppCMF.CameraMouseInput), "GetVerticalCameraInput", nameof(Post_Look), false);
            Bind(harmony, log, typeof(Il2CppCMF.AdvancedWalkerController), "CalculateMovementVelocity", nameof(Post_Move), false);
            Bind(harmony, log, typeof(Il2Cpp.InputLayer), "GetKey", nameof(Post_GetKey), false);
            InputHooksBound = Bound - before;
            log.Msg("Panel input hooks installed: " + InputHooksBound + "/" + InputWanted + ".");
            return InputHooksBound;
        }

        private static void Bind(HarmonyLib.Harmony harmony, MelonLogger.Instance log, Type type, string method, string handler, bool prefix)
        {
            try
            {
                var mi = AccessTools.Method(type, method);
                if (mi == null) { log.Warning("patch miss: " + type.Name + "." + method); return; }
                var hm = new HarmonyMethod(typeof(Patches), handler);
                if (prefix) harmony.Patch(mi, prefix: hm);
                else harmony.Patch(mi, postfix: hm);
                Bound++;
            }
            catch (Exception e)
            {
                log.Warning("patch failed " + type.Name + "." + method + ": " + e.Message);
            }
        }

        private static void Pre_CinemachineUpdate()
        {
            CinemachinePrefixCalls++;
            try { ThirdPersonMod.DriveBeforeCinemachine(); } catch { }
        }

        private static void Post_Look(ref float __result)
        {
            LookCalls++;
            if (FreezeLook) __result = 0f;
        }

        private static void Post_Move(ref Vector3 __result)
        {
            MoveCalls++;
            if (FreezeMove) __result = Vector3.zero;
        }

        private static void Post_GetKey(Il2Cpp.InputKey key, ref bool __result)
        {
            GetKeyCalls++;
            if (!BlockFire) return;
            if (key == Il2Cpp.InputKey.Aim || key == Il2Cpp.InputKey.Shoot) __result = false;
        }

        internal static void ClearAll()
        {
            FreezeLook = false;
            FreezeMove = false;
            BlockFire = false;
        }
    }
}
