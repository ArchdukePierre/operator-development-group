// ODG Operations - VKS
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace VKS
{

    internal static class Art
    {

        static readonly HashSet<string> KeepRunning = new HashSet<string>
        {
            "WeaponMods",
            "WeaponMod",
            "WeaponModParent",
            "ModMaterialLink",
            "Magazine",
        };

        internal static GameObject Build(GameObject template, Transform parent)
        {
            if (template == null) return null;
            GameObject go = null;
            try
            {
                go = UnityEngine.Object.Instantiate(template, parent).TryCast<GameObject>();
                if (go == null) { VKSMod.Log("art: Instantiate returned null"); return null; }
                go.name = "VKS (display)";
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;

                try
                {
                    var ni = go.GetComponent<Il2CppMirror.NetworkIdentity>();
                    if (ni != null)
                    {
                        ulong had = 0; try { had = ni.sceneId; } catch { }
                        ni.sceneId = 0;
                        ni.hasSpawned = false;
                        VKSMod.Log("art: identity detached from the scene (template sceneId was " + had + ")");
                    }
                }
                catch (Exception e) { VKSMod.Log("art: identity reset failed: " + e.Message); }

                go.SetActive(true);

                string json = null;
                Il2Cpp.WeaponMods wm = null;
                try
                {
                    wm = go.GetComponent<Il2Cpp.WeaponMods>();
                    if (wm != null) json = wm.BASE_JSON_MOD_STRING;
                }
                catch (Exception e) { VKSMod.Log("art: WeaponMods read failed: " + e.Message); }

                if (wm != null && !string.IsNullOrEmpty(json))
                {
                    var build = BaseBuild.Corrected(json);
                    if (build != null) PatchTemplate(template, build);

                    try
                    {
                        wm.MenuSetBaseMods(false, build ?? json);
                        VKSMod.Log("art: MenuSetBaseMods called with the " + (build != null ? "corrected" : "shipped") +
                                   " base build (" + (build ?? json).Length + " chars)");
                    }
                    catch (Exception e) { VKSMod.Log("art: MenuSetBaseMods threw: " + e.Message + " - showing the bare rifle"); }
                }
                else VKSMod.Log("art: no WeaponMods or no base string on the clone - showing the bare rifle");

                Neutralise(go, false);
                return go;
            }
            catch (Exception e)
            {
                VKSMod.Log("art build failed: " + e);
                try { if (go != null) UnityEngine.Object.Destroy(go); } catch { }
                return null;
            }
        }

        static bool _patchLogged;
        static void PatchTemplate(GameObject template, string build)
        {
            try
            {
                var twm = template.GetComponent<Il2Cpp.WeaponMods>();
                if (twm == null) return;
                var old = twm.BASE_JSON_MOD_STRING;
                if (old == build) return;
                twm.BASE_JSON_MOD_STRING = build;
                if (!_patchLogged)
                {
                    _patchLogged = true;
                    VKSMod.Log("template: base build corrected so a picked-up VKS carries its optic.\n    was: " + old + "\n    now: " + build);
                }
                else VKSMod.Log("template: base build corrected again for this scene");
            }
            catch (Exception e) { VKSMod.Log("template: base build patch failed: " + e.Message); }
        }

        internal static void Neutralise(GameObject display, bool freezeAnimators)
        {
            var off = new Dictionary<string, int>();
            int kept = 0;
            try
            {
                var behaviours = display.GetComponentsInChildren<Behaviour>(true);
                for (int i = 0; i < behaviours.Length; i++)
                {
                    var b = behaviours[i];
                    if (b == null) continue;
                    string tn;
                    try { tn = b.GetIl2CppType().Name; } catch { tn = "?"; }

                    if (KeepRunning.Contains(tn)) { kept++; continue; }
                    if (tn == "Animator" && !freezeAnimators) { kept++; continue; }
                    if (tn == "NetworkIdentity") { kept++; continue; }

                    try
                    {
                        if (!b.enabled) continue;
                        b.enabled = false;
                        off[tn] = off.TryGetValue(tn, out var n) ? n + 1 : 1;
                    }
                    catch { }
                }

                var bodies = display.GetComponentsInChildren<Rigidbody>(true);
                for (int i = 0; i < bodies.Length; i++)
                    if (bodies[i] != null) { try { bodies[i].isKinematic = true; bodies[i].detectCollisions = false; } catch { } }

                var cols = display.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < cols.Length; i++)
                    if (cols[i] != null) { try { cols[i].enabled = false; } catch { } }
            }
            catch (Exception e) { VKSMod.Log("art: neutralise err: " + e.Message); }

            if (off.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (var kv in off) { if (sb.Length > 0) sb.Append(", "); sb.Append(kv.Key).Append(" x").Append(kv.Value); }
                VKSMod.Log("art: switched off " + sb + "; left running " + kept);
            }
        }

        internal static Vector3 MeasureBarrel(GameObject display, out string source)
        {
            Transform muzzle = null, chamber = null;
            try
            {
                var all = display.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    string n; try { n = all[i].name; } catch { continue; }
                    if (muzzle == null && n == "MuzzleEnd") muzzle = all[i];
                    if (chamber == null && n == "Chamber") chamber = all[i];
                    if (muzzle != null && chamber != null) break;
                }
                var root = display.transform;
                if (muzzle != null && chamber != null)
                {
                    var v = root.InverseTransformPoint(muzzle.position) - root.InverseTransformPoint(chamber.position);
                    if (v.sqrMagnitude > 1e-6f) { source = "Chamber -> MuzzleEnd"; return v.normalized; }
                }
                if (muzzle != null)
                {
                    var v = root.InverseTransformDirection(muzzle.forward);
                    if (v.sqrMagnitude > 1e-6f) { source = "MuzzleEnd.forward"; return v.normalized; }
                }
                if (LocalBounds(root, out var lb))
                {
                    var s = lb.size;
                    source = "longest bounds axis (GUESSED)";
                    return (s.x >= s.y && s.x >= s.z) ? Vector3.right : (s.y >= s.z) ? Vector3.up : Vector3.forward;
                }
            }
            catch (Exception e) { VKSMod.Log("art: barrel measure failed: " + e.Message); }
            source = "default +Z";
            return Vector3.forward;
        }

        internal static string Parts(GameObject display)
        {
            var sb = new StringBuilder();
            try
            {
                var mods = display.GetComponentsInChildren<Il2Cpp.WeaponMod>(true);
                for (int i = 0; i < mods.Length; i++)
                {
                    if (mods[i] == null) continue;
                    if (sb.Length > 0) sb.Append(", ");
                    string n; try { n = mods[i].gameObject.name; } catch { n = "?"; }
                    int r = 0; try { r = mods[i].gameObject.GetComponentsInChildren<Renderer>(true).Length; } catch { }
                    sb.Append(n).Append(" (").Append(r).Append(" rend)");
                }
                if (sb.Length == 0) sb.Append("none");
                sb.Insert(0, mods.Length + " part(s): ");
            }
            catch (Exception e) { sb.Append("part list failed: " + e.Message); }
            return sb.ToString();
        }

        internal static int CountRenderers(GameObject go)
        {
            try { return go.GetComponentsInChildren<Renderer>(true).Length; } catch { return 0; }
        }

        internal static bool Bounds(GameObject go, out Bounds b) => Measure(go.transform, null, out b);

        internal static bool LocalBounds(Transform frame, out Bounds b) => Measure(frame, frame, out b);

        static bool Measure(Transform root, Transform frame, out Bounds b)
        {
            b = new Bounds();
            bool any = false;
            try
            {
                var filters = root.gameObject.GetComponentsInChildren<MeshFilter>(true);
                for (int i = 0; i < filters.Length; i++)
                {
                    var f = filters[i];
                    if (f == null) continue;
                    Mesh m = null;
                    try { m = f.sharedMesh; } catch { }
                    if (m == null) continue;
                    Accumulate(m.bounds, f.transform, frame, ref b, ref any);
                }

                var skinned = root.gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                for (int i = 0; i < skinned.Length; i++)
                {
                    var s = skinned[i];
                    if (s == null) continue;
                    Mesh m = null;
                    try { m = s.sharedMesh; } catch { }
                    if (m == null) continue;
                    Accumulate(m.bounds, s.transform, frame, ref b, ref any);
                }
            }
            catch { }
            return any;
        }

        internal static List<Vector3> Corners(GameObject go)
        {
            var pts = new List<Vector3>(256);
            try
            {
                var filters = go.GetComponentsInChildren<MeshFilter>(true);
                for (int i = 0; i < filters.Length; i++)
                {
                    var f = filters[i];
                    if (f == null) continue;
                    Mesh m = null;
                    try { m = f.sharedMesh; } catch { }
                    if (m == null) continue;
                    AddCorners(m.bounds, f.transform, pts);
                }
                var skinned = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                for (int i = 0; i < skinned.Length; i++)
                {
                    var s = skinned[i];
                    if (s == null) continue;
                    Mesh m = null;
                    try { m = s.sharedMesh; } catch { }
                    if (m == null) continue;
                    AddCorners(m.bounds, s.transform, pts);
                }
            }
            catch { }
            return pts;
        }

        static void AddCorners(Bounds local, Transform owner, List<Vector3> pts)
        {
            var c = local.center; var e = local.extents;
            for (int k = 0; k < 8; k++)
                pts.Add(owner.TransformPoint(new Vector3(
                    c.x + ((k & 1) == 0 ? -e.x : e.x),
                    c.y + ((k & 2) == 0 ? -e.y : e.y),
                    c.z + ((k & 4) == 0 ? -e.z : e.z))));
        }

        static void Accumulate(Bounds local, Transform owner, Transform frame, ref Bounds b, ref bool any)
        {
            var c = local.center; var e = local.extents;
            for (int k = 0; k < 8; k++)
            {
                var corner = new Vector3(
                    c.x + ((k & 1) == 0 ? -e.x : e.x),
                    c.y + ((k & 2) == 0 ? -e.y : e.y),
                    c.z + ((k & 4) == 0 ? -e.z : e.z));
                var p = owner.TransformPoint(corner);
                if (frame != null) p = frame.InverseTransformPoint(p);
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
            }
        }

        internal static string F(Vector3 v) => v.x.ToString("F4") + "," + v.y.ToString("F4") + "," + v.z.ToString("F4");

        internal static string Path(Transform t)
        {
            try
            {
                var s = t.name;
                var p = t.parent;
                while (p != null) { s = p.name + "/" + s; p = p.parent; }
                return s;
            }
            catch { return "?"; }
        }
    }
}
