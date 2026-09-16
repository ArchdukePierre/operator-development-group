// ODG Operations - Quartermaster
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using UnityEngine;
using Il2CppKinemation.FPSFramework.Runtime.Recoil;

namespace Quartermaster
{

    internal static class Recoil
    {
        static IntPtr _weaponPtr = IntPtr.Zero, _cameraPtr = IntPtr.Zero;
        static float _v = 1f, _h = 1f;
        static bool _ctdSeen, _fallbackLogged;
        static int _plays;
        internal static string Mode { get { return _ctdSeen ? "targets" : (_plays > 3 ? "multiplier (fallback)" : "waiting"); } }
        internal static float V { get { return _v; } }
        internal static float H { get { return _h; } }

        internal static void Reset() { _weaponPtr = IntPtr.Zero; _cameraPtr = IntPtr.Zero; _v = _h = 1f; }

        internal static void Register(Il2Cpp.WeaponV3 w, Factors f)
        {
            try
            {
                if (w == null || !w.isOwned) return;
                var wr = w.weaponRecoil;
                var ra = wr != null ? wr.Recoily : null;
                _weaponPtr = ra != null ? ra.Pointer : IntPtr.Zero;
                _v = f.V; _h = f.H;
                var mc = w.fpsController;
                if (mc == null) { var pn = w.playerNetworking; if (pn != null) mc = pn.masterController; }
                var cam = mc != null ? mc.CameraRecoil : null;
                _cameraPtr = cam != null ? cam.Pointer : IntPtr.Zero;
            }
            catch { }
        }

        static bool Mine(RecoilAnimation ra)
        {
            if (ra == null) return false;
            var p = ra.Pointer;
            return p != IntPtr.Zero && (p == _weaponPtr || p == _cameraPtr);
        }

        static bool Active()
        {
            return Main.Enabled.Value && Main.HandlingOn.Value && Tables.Loaded && (Math.Abs(_v - 1f) > 0.001f || Math.Abs(_h - 1f) > 0.001f);
        }

        internal static void Post_CalculateTargetData(RecoilAnimation __instance)
        {
            Patches.Fired("RecoilAnimation.CalculateTargetData");
            _ctdSeen = true;
            if (!Active() || !Mine(__instance)) return;
            try
            {
                var r = __instance._targetRot;
                r.x *= _v; r.y *= _h; r.z *= _h;
                __instance._targetRot = r;
                var l = __instance._targetLoc;
                __instance._targetLoc = l * _v;
                Patches.Fired("recoil-targets-scaled");
            }
            catch (Exception e) { Main.Once("recoil targets: " + e.Message); }
        }

        internal static void Pre_Play(RecoilAnimation __instance)
        {
            Patches.Fired("RecoilAnimation.Play");
            _plays++;
            if (_ctdSeen || _plays <= 3) return;
            if (!Active() || !Mine(__instance)) return;
            try
            {
                __instance.RecoilMultiplier = __instance.RecoilMultiplier * ((_v + _h) * 0.5f);
                if (!_fallbackLogged)
                {
                    _fallbackLogged = true;
                    Main.Log("recoil: CalculateTargetData never fired, scaling RecoilMultiplier in Play instead (no vertical/horizontal split)");
                }
                Patches.Fired("recoil-multiplier-scaled");
            }
            catch (Exception e) { Main.Once("recoil multiplier: " + e.Message); }
        }
    }
}
