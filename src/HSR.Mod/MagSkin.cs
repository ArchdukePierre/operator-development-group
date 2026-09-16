// ODG Operations - HSR
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using MelonLoader;
using UnityEngine;

namespace HSR
{

    internal static class MagSkin
    {
        const string SkinName = "M200_MagSkin";

        static MelonPreferences_Entry<bool> _enabled;
        internal static void Init(MelonPreferences_Category cat)
        {

            _enabled = cat.CreateEntry("ReskinMagazine", true);
        }

        internal static bool Enabled
        {
            get { try { return _enabled != null && _enabled.Value; } catch { return false; } }
        }

        static Mesh _baked;
        static Material _mat;
        static int _reported;
        static int _renewReports;

        internal static bool Skinned { get; private set; }

        internal static bool SkinnedUnder(Transform weaponRoot)
        {
            try
            {
                if (weaponRoot == null) return false;
                var all = weaponRoot.GetComponentsInChildren<Transform>(true);
                if (all == null) return false;

                for (int i = 0; i < all.Length; i++)
                {
                    var t = all[i];
                    if (t == null) continue;
                    if (!string.Equals(t.gameObject.name, SkinName, StringComparison.Ordinal)) continue;
                    var mag = t.parent;
                    if (mag == null) continue;
                    if (HSRMod.IsMagazineSocketed(mag)) return true;
                }
            }
            catch { }
            return false;
        }

        internal static bool Ready { get { return _baked != null; } }

        internal static bool HoldSkin(Transform donor, Transform skin, Renderer[] rr)
        {
            if (donor == null || skin == null || rr == null) return false;
            bool gameShows = false;
            for (int i = 0; i < rr.Length; i++)
            {
                var r = rr[i];
                if (r == null) continue;
                Transform t; try { t = r.transform; } catch { continue; }
                if (t == skin || t.IsChildOf(skin)) continue;
                try { if (r.enabled) { gameShows = true; break; } } catch { }
            }
            bool changed = false;
            try
            {
                var sr = skin.GetComponent<Renderer>();
                if (sr != null)
                {
                    if (sr.forceRenderingOff) { sr.forceRenderingOff = false; changed = true; }
                    if (sr.enabled != gameShows) { sr.enabled = gameShows; changed = true; }
                }
            }
            catch { }
            if (changed) { try { MagProbe.Wrote(donor, "MagSkin.hold", 1); } catch { } }
            return changed;
        }

        internal static void ResetForScene()
        {
            _baked = null; _mat = null; _reported = 0; Skinned = false;
        }

        internal static bool Apply(Transform ours, Transform donor, MelonLogger.Instance log)
        {
            if (!Enabled || ours == null || donor == null) return false;

            try
            {
                var dn = donor.gameObject.name;
                if (dn == null || dn.IndexOf(MagBox.DonorMagName, StringComparison.Ordinal) < 0) return false;
            }
            catch { return false; }

            try
            {

                var existing = donor.Find(SkinName);
                if (existing != null)
                {

                    Skinned = true;
                    try
                    {
                        var rr = donor.GetComponentsInChildren<Renderer>(true);
                        int renewed = 0;
                        for (int i = 0; i < rr.Length; i++)
                        {
                            var r = rr[i];
                            if (r == null) continue;
                            if (r.transform == existing || r.transform.IsChildOf(existing)) continue;
                            if (!r.forceRenderingOff) { r.forceRenderingOff = true; renewed++; }
                        }
                        if (renewed > 0) { try { MagProbe.Wrote(donor, "MagSkin.renew", renewed); } catch { } }
                        HoldSkin(donor, existing, rr);
                        if (renewed > 0 && log != null && _renewReports++ < 6)
                            log.Msg("mag: '{0}' grew {1} fresh renderer(s) after a reload — forced off again.",
                                    donor.gameObject.name, renewed);
                    }
                    catch { }
                    return true;
                }

                if (_baked == null && !Bake(ours, donor, log)) return false;

                var go = new GameObject(SkinName);
                go.transform.SetParent(donor, false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                go.AddComponent<MeshFilter>().sharedMesh = _baked;
                var mr = go.AddComponent<MeshRenderer>();
                if (_mat != null) mr.sharedMaterial = _mat;

                try { mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; } catch { }
                try { mr.receiveShadows = true; } catch { }

                int off = 0;
                var rends = donor.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rends.Length; i++)
                {
                    var r = rends[i];
                    if (r == null || r.gameObject == go) continue;
                    try { r.forceRenderingOff = true; off++; } catch { }
                }

                if (_reported++ < 3 && log != null)
                    log.Msg("mag: '{0}' now wears the M200 magazine ({1} donor renderer(s) forced off). "
                          + "It is a child of the clone, so the reload carries it.",
                            donor.gameObject.name, off);
                Skinned = true;
                try { MagProbe.Wrote(donor, "MagSkin.apply", off); } catch { }
                try { if (HSRMod.Instance != null) HSRMod.Instance.NoteMagazineToHold(donor); } catch { }
                return true;
            }
            catch (Exception e)
            {
                if (log != null) log.Warning("mag skin: " + e.Message);
                return false;
            }
        }

        static bool Bake(Transform ours, Transform donor, MelonLogger.Instance log)
        {
            var mf = ours.GetComponent<MeshFilter>();
            var src = mf != null ? mf.sharedMesh : null;
            if (src == null) { if (log != null) log.Warning("mag skin: our magazine has no mesh"); return false; }

            var mr = ours.GetComponent<MeshRenderer>();
            if (mr != null) _mat = mr.sharedMaterial;

            Matrix4x4 m = donor.worldToLocalMatrix * ours.localToWorldMatrix;

            var vin = src.vertices;
            var nin = src.normals;
            var uin = src.uv;
            var tin = src.triangles;
            int vn = vin != null ? vin.Length : 0;
            if (vn == 0) { if (log != null) log.Warning("mag skin: our magazine mesh is empty"); return false; }

            var mesh = new Mesh();
            mesh.name = SkinName;
            if (vn > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            var vout = new Vector3[vn];
            for (int i = 0; i < vn; i++) vout[i] = m.MultiplyPoint3x4(vin[i]);

            int nn = nin != null ? nin.Length : 0;
            var nout = new Vector3[nn];
            for (int i = 0; i < nn; i++) nout[i] = m.MultiplyVector(nin[i]).normalized;

            bool mirrored = m.determinant < 0f;
            int tn = tin != null ? tin.Length : 0;
            var tout = new int[tn];
            if (mirrored)
                for (int i = 0; i + 2 < tn; i += 3)
                { tout[i] = tin[i]; tout[i + 1] = tin[i + 2]; tout[i + 2] = tin[i + 1]; }
            else
                for (int i = 0; i < tn; i++) tout[i] = tin[i];

            mesh.vertices = ObjBuilder.ToIl2Cpp(vout);
            if (nn == vn) mesh.normals = ObjBuilder.ToIl2Cpp(nout);
            if (uin != null && uin.Length == vn) mesh.uv = uin;
            mesh.triangles = ObjBuilder.ToIl2CppInt(tout);
            if (nn != vn) mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            _baked = mesh;
            if (log != null)
                log.Msg("mag: baked the M200 magazine into '{0}' local space ({1} verts{2}).",
                        donor.gameObject.name, vn, mirrored ? ", winding flipped for the X mirror" : "");
            return true;
        }
    }
}
