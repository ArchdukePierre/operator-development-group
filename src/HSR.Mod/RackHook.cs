// ODG Operations - HSR
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;

namespace HSR
{

    internal static class RackHook
    {
        internal const string HookName = "WeaponSpawn M200";
        const string RackPath = "OPFOR";
        const string BarrettHookName = "WeaponSpawn M107A1";
        const string SvdHookName = "WeaponSpawn SVD";

        internal static Vector3 HookPos = new Vector3(-13.0111f, 0.2888f, -15.6253f);
        static readonly Vector3 HookEuler = new Vector3(0.4504f, 270f, 0f);

        internal static bool Built { get; private set; }
        static GameObject _hook;

        internal static void Reset() { Built = false; _hook = null; }

        internal static bool TryBuild(GameObject template, GameObject bodyPrefab,
                                      MelonLogger.Instance log)
        {
            if (Built && _hook != null) return true;
            if (template == null) return false;

            var rack = FindRack();
            if (rack == null) return false;

            try
            {
                var existing = rack.Find(HookName);
                if (existing != null) { _hook = existing.gameObject; Built = true; return true; }

                var neighbour = rack.Find(BarrettHookName) ?? rack.Find(SvdHookName);

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
                    if (neighbour != null)
                    {
                        var nb = neighbour.gameObject.GetComponent<BoxCollider>();
                        if (nb != null)
                        {
                            box.center = nb.center;

                            var s = nb.size; s.z *= 0.82f;
                            box.size = s;
                        }
                    }
                }

                var ni = go.AddComponent(Il2CppType.Of<Il2CppMirror.NetworkIdentity>())
                           .TryCast<Il2CppMirror.NetworkIdentity>();
                if (ni == null) log.Warning("rack: NetworkIdentity add returned null (continuing)");

                var spawner = go.AddComponent(Il2CppType.Of<Il2Cpp.N_StaticWeaponSpawner>())
                                .TryCast<Il2Cpp.N_StaticWeaponSpawner>();
                if (spawner == null) { log.Error("rack: AddComponent<N_StaticWeaponSpawner> was null"); return false; }
                spawner.WeaponReference = template;
                spawner.DisableWhenPickedUp = false;

                try
                {
                    var ic = go.AddComponent(Il2CppType.Of<Il2Cpp.InteractableComponent>())
                               .TryCast<Il2Cpp.InteractableComponent>();
                    if (ic != null) ic.InteractableName = "M200 Intervention";
                }
                catch (Exception ie) { log.Warning("rack: InteractableComponent add failed: " + ie.Message); }

                if (bodyPrefab != null)
                {
                    var display = UnityEngine.Object.Instantiate(bodyPrefab, go.transform);
                    display.name = "M14 (display)";
                    display.SetActive(true);
                    PoseDisplay(go.transform, display.transform, neighbour, log);
                }

                _hook = go;
                Built = true;
                log.Msg("RACK: built '{0}' at {1} on {2} (MK16 left alone) — WeaponRef={3}",
                    HookName, go.transform.position.ToString("F4"), RackPath,
                    spawner.WeaponReference == null ? "null" : spawner.WeaponReference.name);
                return true;
            }
            catch (Exception e) { log.Error("rack build failed: " + e); return false; }
        }

        static void PoseDisplay(Transform hook, Transform art, Transform neighbour,
                                MelonLogger.Instance log)
        {
            try
            {
                art.localRotation = Quaternion.Euler(0f, 180f, 0f);
                art.localScale = new Vector3(-1f, 1f, 1f);
                art.localPosition = Vector3.zero;

                Vector3 desiredLocal = new Vector3(0f, 0.02f, -0.58f);
                if (neighbour != null)
                {
                    Bounds b = new Bounds(); bool any = false;
                    var rs = neighbour.gameObject.GetComponentsInChildren<Renderer>(true);
                    for (int i = 0; i < rs.Length; i++)
                    {
                        if (rs[i] == null) continue;
                        if (!any) { b = rs[i].bounds; any = true; } else b.Encapsulate(rs[i].bounds);
                    }
                    if (any) desiredLocal = neighbour.InverseTransformPoint(b.center);
                }

                Bounds ours = new Bounds(); bool ok = false;
                var ar = art.gameObject.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < ar.Length; i++)
                {
                    if (ar[i] == null) continue;
                    if (!ok) { ours = ar[i].bounds; ok = true; } else ours.Encapsulate(ar[i].bounds);
                }
                if (ok)
                {
                    var haveLocal = hook.InverseTransformPoint(ours.center);
                    art.localPosition += (desiredLocal - haveLocal);
                }
                log.Msg("RACK: display posed at hook-local {0}", art.localPosition.ToString("F3"));
            }
            catch (Exception e) { log.Warning("rack pose failed: " + e.Message); }
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
