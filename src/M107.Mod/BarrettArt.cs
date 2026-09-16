// ODG Operations - M107
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using UnityEngine;

namespace M107
{

    internal static class BarrettArt
    {
        internal const string PropPrefix = "barrett_m107a1";

        const string StscSuppressor = "Supressor_STSC";
        const string MuzzleBrake = "barrett_m107a1_muzle_brake";
        static readonly string[] OwnSilencer =
        { "barrett_m107a1_silencer_body", "barrett_m107a1_silencer_attachment", "barrett_m107a1_silencer_detail" };

        static GameObject _master;
        internal static bool Ready => _master != null;

        internal static bool Harvest()
        {
            if (_master != null) return true;

            var prop = FindProp();
            if (prop == null) return false;

            try
            {
                var holder = new GameObject("M107_ArtHolder");
                UnityEngine.Object.DontDestroyOnLoad(holder);
                holder.SetActive(false);

                var copy = UnityEngine.Object.Instantiate(prop.gameObject, holder.transform).TryCast<GameObject>();
                if (copy == null) { M107Mod.Log("art: Instantiate returned null"); return false; }
                copy.name = "M107A1_Art";
                copy.transform.localPosition = Vector3.zero;
                copy.transform.localRotation = Quaternion.identity;
                copy.transform.localScale = Vector3.one;

                ConfigureMuzzle(copy);
                Persist(copy);

                _master = copy;
                if (Bounds(copy, out var b))
                    M107Mod.Log("art: harvested from " + Path(prop) + "  parts=" + copy.GetComponentsInChildren<MeshFilter>(true).Length +
                                "  localBounds size=" + F(b.size));
                else
                    M107Mod.Log("art: harvested, bounds unavailable");
                return true;
            }
            catch (Exception e) { M107Mod.Log("art harvest failed: " + e); return false; }
        }

        static void ConfigureMuzzle(GameObject art)
        {
            int on = 0, off = 0;
            try
            {
                var all = art.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    var t = all[i]; string n;
                    try { n = t.name; } catch { continue; }

                    if (n.IndexOf(StscSuppressor, StringComparison.OrdinalIgnoreCase) >= 0)
                    { t.gameObject.SetActive(false); off++; M107Mod.Log("art: dropped borrowed can '" + n + "'"); continue; }

                    if (n.Equals(MuzzleBrake, StringComparison.OrdinalIgnoreCase))
                    { t.gameObject.SetActive(true); on++; continue; }

                    foreach (var s in OwnSilencer)
                        if (n.Equals(s, StringComparison.OrdinalIgnoreCase)) { t.gameObject.SetActive(false); off++; break; }
                }
            }
            catch (Exception e) { M107Mod.Log("muzzle config err: " + e.Message); }
            M107Mod.Log("art: muzzle configured — brake enabled(" + on + "), suppressors disabled(" + off + ")");
        }

        static readonly string[] TexProps =
        { "_BaseColorMap", "_MaskMap", "_NormalMap", "_MainTex", "_BumpMap", "_EmissiveColorMap" };

        static void Persist(GameObject art)
        {
            int meshes = 0, mats = 0, texes = 0;
            try
            {
                var mfs = art.GetComponentsInChildren<MeshFilter>(true);
                for (int i = 0; i < mfs.Length; i++)
                {
                    var mf = mfs[i]; if (mf == null) continue;
                    try
                    {
                        var m = mf.sharedMesh;
                        if (m != null && m.hideFlags != HideFlags.DontUnloadUnusedAsset)
                        { m.hideFlags = HideFlags.DontUnloadUnusedAsset; meshes++; }
                    }
                    catch { }
                }

                var rends = art.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rends.Length; i++)
                {
                    var r = rends[i]; if (r == null) continue;
                    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Material> ms = null;
                    try { ms = r.sharedMaterials; } catch { }
                    if (ms == null) continue;
                    for (int k = 0; k < ms.Length; k++)
                    {
                        var mat = ms[k]; if (mat == null) continue;
                        try
                        {
                            if (mat.hideFlags != HideFlags.DontUnloadUnusedAsset)
                            { mat.hideFlags = HideFlags.DontUnloadUnusedAsset; mats++; }
                        }
                        catch { }
                        foreach (var p in TexProps)
                        {
                            try
                            {
                                if (!mat.HasProperty(p)) continue;
                                var tex = mat.GetTexture(p);
                                if (tex == null || tex.hideFlags == HideFlags.DontUnloadUnusedAsset) continue;
                                tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                                texes++;
                            }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception e) { M107Mod.Log("art: persist err: " + e.Message); }
            M107Mod.Log("art: pinned against scene unload - " + meshes + " mesh(es), " + mats +
                        " material(s), " + texes + " texture(s)");
        }

        internal static Transform FindProp()
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
                        var hit = Search(roots[r].transform, 0);
                        if (hit != null) return hit;
                    }
                }
            }
            catch { }
            return null;
        }

        static int _visited;
        static Transform Search(Transform t, int depth)
        {
            if (t == null || depth > 14) return null;
            if (depth == 0) _visited = 0;
            if (_visited++ > 250000) return null;
            string n; try { n = t.name; } catch { return null; }
            if (n != null && n.StartsWith(PropPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var top = t; int g = 0;
                while (top.parent != null && g++ < 12)
                {
                    string pn; try { pn = top.parent.name; } catch { break; }
                    if (pn != null && pn.StartsWith(PropPrefix, StringComparison.OrdinalIgnoreCase)) top = top.parent;
                    else break;
                }
                return top;
            }
            for (int i = 0; i < t.childCount; i++)
            {
                var hit = Search(t.GetChild(i), depth + 1);
                if (hit != null) return hit;
            }
            return null;
        }

        internal static GameObject Attach(Transform parent, string name)
        {
            if (_master == null || parent == null) return null;
            try
            {
                var inst = UnityEngine.Object.Instantiate(_master, parent).TryCast<GameObject>();
                if (inst == null) return null;
                inst.name = name;
                inst.transform.localPosition = Vector3.zero;
                inst.transform.localRotation = Quaternion.identity;
                inst.transform.localScale = Vector3.one;
                inst.SetActive(true);
                return inst;
            }
            catch (Exception e) { M107Mod.Log("art attach failed: " + e.Message); return null; }
        }

        internal static void AlignMuzzle(GameObject art, Transform muzzleEnd)
        {
            if (art == null || muzzleEnd == null || art.transform.parent == null) return;
            try
            {
                if (!MuzzleTipLocal(art, out var tipLocal))
                { M107Mod.Log("align: no muzzle part found, leaving art at origin"); return; }

                var parent = art.transform.parent;
                Vector3 targetInParent = parent.InverseTransformPoint(muzzleEnd.position);

                art.transform.localPosition = targetInParent - tipLocal;
                M107Mod.Log("align: tip(local " + F(tipLocal) + ") -> MuzzleEnd(parent " + F(targetInParent) +
                            ")  =>  art localPosition " + F(art.transform.localPosition));
            }
            catch (Exception e) { M107Mod.Log("align err: " + e.Message); }
        }

        static bool MuzzleTipLocal(GameObject art, out Vector3 tip)
        {
            tip = Vector3.zero;
            try
            {
                var root = art.transform;
                var mfs = art.GetComponentsInChildren<MeshFilter>(true);
                float best = float.MinValue; bool found = false;
                for (int i = 0; i < mfs.Length; i++)
                {
                    var mf = mfs[i]; if (mf == null || mf.sharedMesh == null) continue;
                    string n; try { n = mf.name; } catch { continue; }
                    bool isMuzzle = n.IndexOf("muzle_brake", StringComparison.OrdinalIgnoreCase) >= 0
                                 || n.IndexOf("muzzle_brake", StringComparison.OrdinalIgnoreCase) >= 0
                                 || n.IndexOf("barrel_body", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!isMuzzle) continue;
                    if (!mf.gameObject.activeSelf) continue;

                    var mb = mf.sharedMesh.bounds;

                    for (int c = 0; c < 8; c++)
                    {
                        var corner = new Vector3(
                            mb.center.x + ((c & 1) == 0 ? -mb.extents.x : mb.extents.x),
                            mb.center.y + ((c & 2) == 0 ? -mb.extents.y : mb.extents.y),
                            mb.center.z + ((c & 4) == 0 ? -mb.extents.z : mb.extents.z));
                        var inRoot = root.InverseTransformPoint(mf.transform.TransformPoint(corner));
                        if (inRoot.z > best) { best = inRoot.z; tip = inRoot; found = true; }
                    }
                }
                if (found) tip = new Vector3(0f, tip.y, tip.z);
                return found;
            }
            catch { return false; }
        }

        internal static bool Bounds(GameObject go, out Bounds b)
        {
            b = new Bounds(); bool any = false;
            try
            {
                var root = go.transform;
                var mfs = go.GetComponentsInChildren<MeshFilter>(true);
                for (int i = 0; i < mfs.Length; i++)
                {
                    var mf = mfs[i]; if (mf == null || mf.sharedMesh == null) continue;
                    if (!mf.gameObject.activeSelf) continue;
                    var mb = mf.sharedMesh.bounds;
                    for (int c = 0; c < 8; c++)
                    {
                        var corner = new Vector3(
                            mb.center.x + ((c & 1) == 0 ? -mb.extents.x : mb.extents.x),
                            mb.center.y + ((c & 2) == 0 ? -mb.extents.y : mb.extents.y),
                            mb.center.z + ((c & 4) == 0 ? -mb.extents.z : mb.extents.z));
                        var p = root.InverseTransformPoint(mf.transform.TransformPoint(corner));
                        if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                    }
                }
            }
            catch { }
            return any;
        }

        internal static bool FindMesh(string key, out Mesh mesh, out Material mat)
        {
            mesh = null; mat = null;
            if (_master == null) return false;
            try
            {
                var mfs = _master.GetComponentsInChildren<MeshFilter>(true);
                for (int i = 0; i < mfs.Length; i++)
                {
                    var mf = mfs[i]; if (mf == null || mf.sharedMesh == null) continue;
                    string n; try { n = mf.name; } catch { continue; }
                    if (n == null || n.IndexOf(key, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    mesh = mf.sharedMesh;
                    try { var r = mf.gameObject.GetComponent<Renderer>(); if (r != null) mat = r.sharedMaterial; } catch { }
                    return true;
                }
            }
            catch { }
            return false;
        }

        internal static string F(Vector3 v) => "(" + v.x.ToString("F3") + ", " + v.y.ToString("F3") + ", " + v.z.ToString("F3") + ")";

        internal static string Path(Transform t)
        {
            if (t == null) return "(null)";
            var parts = new List<string>(); int g = 0;
            while (t != null && g++ < 40) { parts.Add(t.name); t = t.parent; }
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
