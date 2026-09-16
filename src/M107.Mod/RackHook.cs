// ODG Operations - M107
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace M107
{

    internal static class RackHook
    {
        internal const string HookName = "WeaponSpawn M107A1";
        const string RackPath = "OPFOR";
        const string SvdHookName = "WeaponSpawn SVD";

        static readonly Vector3 HookPos = new Vector3(-13.0111f, 0.7948f, -15.6253f);
        static readonly Vector3 HookEuler = new Vector3(0.4504f, 270f, 0f);

        internal static bool Built { get; private set; }
        static GameObject _hook;

        internal static void Reset() { Built = false; _hook = null; }

        internal static bool TryBuild()
        {
            if (Built && _hook != null) return true;
            if (WeaponTemplate.Template == null || !BarrettArt.Ready) return false;

            var rack = FindRack();
            if (rack == null) return false;

            try
            {

                var existing = rack.Find(HookName);
                if (existing != null) { _hook = existing.gameObject; Built = true; return true; }

                var svdHook = rack.Find(SvdHookName);

                var go = new GameObject(HookName);
                go.transform.SetParent(rack, false);
                go.transform.position = HookPos;
                go.transform.rotation = Quaternion.Euler(HookEuler);
                go.layer = 9;

                var box = go.AddComponent(Il2CppType.Of<BoxCollider>()).TryCast<BoxCollider>();
                if (box != null)
                {
                    box.isTrigger = true;
                    box.center = new Vector3(0.104f, 0.007f, -0.760f);
                    box.size = new Vector3(0.116f, 0.284f, 1.050f);
                    if (svdHook != null)
                    {
                        var sb = svdHook.gameObject.GetComponent<BoxCollider>();
                        if (sb != null)
                        {
                            box.center = sb.center;
                            var s = sb.size; s.z *= 1.15f;
                            box.size = s;
                        }
                    }
                }

                var ni = go.AddComponent(Il2CppType.Of<Il2CppMirror.NetworkIdentity>())
                           .TryCast<Il2CppMirror.NetworkIdentity>();
                if (ni == null) M107Mod.Log("hook: NetworkIdentity add returned null (continuing)");

                var spawner = go.AddComponent(Il2CppType.Of<Il2Cpp.N_StaticWeaponSpawner>())
                                .TryCast<Il2Cpp.N_StaticWeaponSpawner>();
                if (spawner == null) { M107Mod.Log("hook: AddComponent<N_StaticWeaponSpawner> returned null"); return false; }
                spawner.WeaponReference = WeaponTemplate.Template;
                spawner.DisableWhenPickedUp = false;

                var display = BarrettArt.Attach(go.transform, "M107A1 (display)");
                if (display != null)
                {
                    PoseDisplay(go.transform, display, svdHook);
                    Alignment.SeedDisplay(display.transform);
                    Alignment.Register(display.transform, true);
                }

                _hook = go;
                Built = true;

                M107Mod.Log("HOOK BUILT '" + HookName + "' at " + BarrettArt.F(go.transform.position) +
                            " euler " + BarrettArt.F(go.transform.eulerAngles) +
                            "  parent=" + BarrettArt.Path(rack) +
                            "  WeaponRef=" + (spawner.WeaponReference == null ? "null" : spawner.WeaponReference.name) +
                            "  layer=" + go.layer);
                var idn = go.GetComponent<Il2CppMirror.NetworkIdentity>();
                M107Mod.Log("hook identity: " + (idn == null ? "none" : "netId=" + idn.netId + " sceneId=" + idn.sceneId + " assetId=" + idn.assetId));
                return true;
            }
            catch (Exception e) { M107Mod.Log("hook build failed: " + e); return false; }
        }

        static void PoseDisplay(Transform hook, GameObject art, Transform svdHook)
        {
            try
            {

                art.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                art.transform.localScale = Vector3.one;
                art.transform.localPosition = Vector3.zero;

                Vector3 desiredLocal = new Vector3(0f, 0.02f, -0.58f);
                if (svdHook != null)
                {
                    Bounds svdB = new Bounds(); bool any = false;
                    var rs = svdHook.gameObject.GetComponentsInChildren<Renderer>(true);
                    for (int i = 0; i < rs.Length; i++)
                    {
                        if (rs[i] == null) continue;
                        if (!any) { svdB = rs[i].bounds; any = true; } else svdB.Encapsulate(rs[i].bounds);
                    }
                    if (any)
                    {
                        desiredLocal = svdHook.InverseTransformPoint(svdB.center);
                        M107Mod.Log("display: SVD hangs its gun at hook-local " + BarrettArt.F(desiredLocal));
                    }
                }

                if (BarrettArt.Bounds(art, out var ab))
                {
                    Vector3 centreInHook = hook.InverseTransformPoint(art.transform.TransformPoint(ab.center));
                    art.transform.localPosition += (desiredLocal - centreInHook);
                }
                M107Mod.Log("display posed: local " + BarrettArt.F(art.transform.localPosition) +
                            " rot " + BarrettArt.F(art.transform.localEulerAngles));
            }
            catch (Exception e) { M107Mod.Log("display pose err: " + e.Message); }
        }

        static Transform FindRack()
        {
            try
            {
                for (int s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++)
                {
                    var sc = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
                    if (!sc.isLoaded) continue;
                    var roots = sc.GetRootGameObjects();
                    for (int r = 0; r < roots.Length; r++)
                    {
                        if (roots[r].name != "Weapons") continue;
                        var t = roots[r].transform.Find(RackPath);
                        if (t != null) return t;
                    }
                }
            }
            catch { }
            return null;
        }
    }
}
