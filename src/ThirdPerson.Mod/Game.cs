// ODG Operations - ThirdPerson
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

namespace ThirdPerson
{

    internal static class Game
    {
        internal static bool Alive(UnityEngine.Object o)
        {
            try { return o != null; } catch { return false; }
        }

        internal static Il2Cpp.PlayerNetworking LocalPlayer()
        {
            try
            {
                var pm = Il2Cpp.PlayerMaster.MyPlayerMaster;
                if (pm == null) return null;
                var pn = pm.PlayerSpawnedObject;
                if (pn == null) return null;
                return pn;
            }
            catch { return null; }
        }

        internal static Il2Cpp.TpToggle FindToggle(Il2Cpp.PlayerNetworking pn)
        {
            try { return pn.GetComponentInChildren<Il2Cpp.TpToggle>(true); } catch { return null; }
        }

        internal static Transform EyeTransform(Il2Cpp.PlayerNetworking pn)
        {
            try { var c = pn.Camera; if (c != null) return c.transform; } catch { }
            try { return pn.transform; } catch { return null; }
        }

        internal static Transform PlayerRoot(Il2Cpp.PlayerNetworking pn)
        {
            try { return pn.transform; } catch { return null; }
        }

        internal static Camera TpCamera(Il2Cpp.TpToggle tp)
        {
            try { return tp.TPCamera; } catch { return null; }
        }

        internal static bool GameInThirdPerson(Il2Cpp.TpToggle tp)
        {
            try { return tp.inThirdPerson; } catch { return false; }
        }

        internal static bool Toggle(Il2Cpp.TpToggle tp, MelonLogger.Instance log)
        {
            try { tp.ToggleThirdPerson(); return true; }
            catch (Exception e) { log.Warning("ToggleThirdPerson threw: " + e.Message); return false; }
        }

        internal static void ShowHead(Il2Cpp.PlayerNetworking pn, bool show)
        {
            try { if (show) pn.ReverseShadowCharacter(); else pn.SetShadowCharacter(); } catch { }
        }

        internal static bool? HeadHidden(Il2Cpp.PlayerNetworking pn)
        {
            try
            {
                var arr = pn.AuthShadowedParents;
                if (arr == null || arr.Length == 0) return null;
                for (int i = 0; i < arr.Length; i++)
                {
                    var go = arr[i];
                    if (go == null) continue;
                    var r = go.GetComponentInChildren<Renderer>(true);
                    if (r == null) continue;
                    return r.shadowCastingMode == ShadowCastingMode.ShadowsOnly;
                }
            }
            catch { }
            return null;
        }

        internal static bool IsAds(Il2Cpp.PlayerNetworking pn)
        {
            try { return pn.isADS; } catch { return false; }
        }

        internal static bool InVehicle(Il2Cpp.PlayerNetworking pn)
        {
            try { return pn.helicopter != null; } catch { return false; }
        }

        internal static bool FreeCamOn(Il2Cpp.PlayerNetworking pn)
        {
            try { var f = pn.freeCamera; return f != null && f.ToggledCam; } catch { return false; }
        }

        internal static bool ConsoleOpen()
        {
            try { var q = Il2CppQFSW.QC.QuantumConsole.Instance; return q != null && q.IsActive; } catch { return false; }
        }

        internal static bool InputDisabled()
        {
            try { return Il2Cpp.InputLayer.InputDisabled; } catch { return false; }
        }

        internal static float GameFov()
        {
            try { var gm = Il2Cpp.GameManager.instance; if (gm != null) return gm.CameraFOV; } catch { }
            return 0f;
        }

        internal static void SetGameFov(float fov)
        {
            try
            {
                var gm = Il2Cpp.GameManager.instance;
                if (gm != null) { gm.CameraFOV = fov; gm.TargetCameraFOV = fov; }
            }
            catch { }
        }

        internal static float MainCameraFov()
        {
            try { var c = Il2Cpp.MainCameraSingleton.MainCam; if (c != null) return c.fieldOfView; } catch { }
            return 0f;
        }

        internal static Unity.Cinemachine.CinemachineCamera CinemachineOn(Camera cam)
        {
            try { return cam.GetComponent<Unity.Cinemachine.CinemachineCamera>(); } catch { return null; }
        }

        internal static void SetCameraFov(Camera cam, Unity.Cinemachine.CinemachineCamera cm, float fov)
        {
            try { cam.fieldOfView = fov; } catch { }
            if (cm == null) return;
            try
            {
                var lens = cm.Lens;
                lens.FieldOfView = fov;
                cm.Lens = lens;
            }
            catch { }
        }

        internal static bool ReadClipPlanes(Transform eye, out float near, out float far)
        {
            near = 0f; far = 0f;
            try
            {
                var cm = eye.GetComponent<Unity.Cinemachine.CinemachineCamera>();
                if (cm == null) return false;
                var lens = cm.Lens;
                near = lens.NearClipPlane; far = lens.FarClipPlane;
                return near > 0f && far > near;
            }
            catch { return false; }
        }

        internal static bool GetClipPlanes(Unity.Cinemachine.CinemachineCamera cm, out float near, out float far)
        {
            near = 0f; far = 0f;
            try { var lens = cm.Lens; near = lens.NearClipPlane; far = lens.FarClipPlane; return true; } catch { return false; }
        }

        internal static void SetClipPlanes(Unity.Cinemachine.CinemachineCamera cm, float near, float far)
        {
            try
            {
                var lens = cm.Lens;
                lens.NearClipPlane = near;
                lens.FarClipPlane = far;
                cm.Lens = lens;
            }
            catch { }
        }

        internal static int MipLimit
        {
            get { try { return QualitySettings.globalTextureMipmapLimit; } catch { return 0; } }
            set { try { QualitySettings.globalTextureMipmapLimit = value; } catch { } }
        }

        internal static string Path(Transform t)
        {
            try
            {
                if (t == null) return "<null>";
                string s = t.name;
                var p = t.parent;
                int guard = 0;
                while (p != null && guard++ < 32) { s = p.name + "/" + s; p = p.parent; }
                return s;
            }
            catch { return "<unreadable>"; }
        }

        internal static string Components(GameObject go)
        {
            try
            {
                if (go == null) return "<null>";
                var comps = go.GetComponents<Component>();
                if (comps == null) return "";
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < comps.Length; i++)
                {
                    var c = comps[i];
                    if (c == null) continue;
                    string n = "?";
                    try { n = c.GetIl2CppType().Name; } catch { }
                    string en = "";
                    try { var b = c.TryCast<Behaviour>(); if (b != null) en = b.enabled ? "(on)" : "(off)"; } catch { }
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(n).Append(en);
                }
                return sb.ToString();
            }
            catch { return "<unreadable>"; }
        }
    }
}
