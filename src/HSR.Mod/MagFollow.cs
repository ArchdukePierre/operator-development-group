// ODG Operations - HSR
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using MelonLoader;
using UnityEngine;

namespace HSR
{

    internal static class MagFollow
    {

        const int SettleFrames = 10;

        const float StillEpsilon = 0.001f;

        const float SeatRadius = 0.40f;

        sealed class Link
        {
            internal Transform Ours;
            internal Transform Donor;
            internal Vector3 RestPos;
            internal Quaternion RestRot;

            internal Il2Cpp.WeaponV3 Weapon;

            internal Renderer[] DonorRends;
        }

        static readonly System.Collections.Generic.List<Link> _links =
            new System.Collections.Generic.List<Link>();

        static Vector3 _offsetPos;
        static Quaternion _offsetRot;
        static bool _haveOffset;

        static Vector3 _lastProbe;
        static int _stillFrames;
        static Link _prober;
        static MelonLogger.Instance _log;

        static Vector3 _bodyPos;
        static Quaternion _bodyRot;
        const float BodyMoveEpsilon = 0.0015f;

        internal static string StateForHealth()
        {
            try
            {
                Prune();
                if (_links.Count == 0) return "none";
                if (!_haveOffset) return "probing";
                for (int i = 0; i < _links.Count; i++)
                    if (_links[i].Donor == null) return "donor-gone";
                return _links.Count > 1 ? "locked x" + _links.Count : "locked";
            }
            catch { return "err"; }
        }

        internal static void Reset()
        {
            _links.Clear();
            _prober = null;
            _haveOffset = false; _stillFrames = 0;
        }

        static void Prune()
        {
            for (int i = _links.Count - 1; i >= 0; i--)
            {
                bool dead;
                try { dead = _links[i].Ours == null; } catch { dead = true; }
                if (!dead) continue;
                if (_prober == _links[i]) _prober = null;
                _links.RemoveAt(i);
            }
        }

        internal static void Bind(Transform ours, Transform donor, MelonLogger.Instance log)
        {
            try
            {
                _log = log;
                if (ours == null || donor == null) return;
                Prune();

                Link link = null;
                for (int i = 0; i < _links.Count; i++)
                    if (_links[i].Ours == ours) { link = _links[i]; break; }

                if (link == null)
                {

                    link = new Link();
                    link.Ours = ours;
                    link.RestPos = ours.localPosition;
                    link.RestRot = ours.localRotation;
                    _links.Add(link);
                }

                if (link.Donor != donor)
                {
                    link.Donor = donor;
                    try { link.DonorRends = donor.GetComponentsInChildren<Renderer>(true); }
                    catch { link.DonorRends = null; }

                    if (!_haveOffset && _prober == null)
                    {
                        _prober = link;
                        _stillFrames = 0;
                    }
                }
            }
            catch { }
        }

        internal static void Tick()
        {
            try
            {
                if (_links.Count == 0) return;
                Prune();

                for (int i = 0; i < _links.Count; i++)
                {
                    var link = _links[i];
                    try
                    {

                        var rr = link.DonorRends;
                        if (rr != null)
                        {
                            int held = 0;
                            for (int r = 0; r < rr.Length; r++)
                            {
                                var rend = rr[r];
                                if (rend == null) continue;
                                if (rend.enabled) { rend.enabled = false; held++; }
                            }
                            if (held > 0) { try { MagProbe.Wrote(link.Donor, "MagFollow.hold", held); } catch { } }
                        }

                        bool seatedOk;
                        try { seatedOk = link.Donor != null && HSRMod.IsMagazineSocketed(link.Donor); }
                        catch { seatedOk = false; }
                        if (!seatedOk)
                        {
                            var now = FindFittedMag(link.Ours);
                            if (now != null && now != link.Donor)
                            {
                                link.Donor = now;
                                try { link.DonorRends = now.GetComponentsInChildren<Renderer>(true); }
                                catch { link.DonorRends = null; }
                                if (_log != null && _rebinds++ < 6)
                                    _log.Msg("mag: now following the seated magazine '" + now.gameObject.name + "'.");
                            }
                        }

                        if (link.Donor == null)
                        {

                            var fresh = FindFittedMag(link.Ours);
                            if (fresh != null)
                            {
                                link.Donor = fresh;
                                try { link.DonorRends = fresh.GetComponentsInChildren<Renderer>(true); }
                                catch { link.DonorRends = null; }
                                if (_log != null && _rebinds++ < 4)
                                    _log.Msg("mag: the fitted magazine was replaced (reload) — rebound to "
                                           + "'" + fresh.gameObject.name + "' and re-skinning it.");
                            }
                            else
                            {

                                HoldRest(link);
                                continue;
                            }
                        }

                        if (!_haveOffset)
                        {
                            HoldRest(link);
                            if (_prober == null) { _prober = link; _stillFrames = 0; }
                            if (_prober == link) TryCapture(link);
                            continue;
                        }

                        var body = link.Ours.parent;
                        if (body != null && BodyMoved(body))
                        {
                            if (_log != null)
                                _log.Msg("mag: body alignment changed - re-deriving the magazine offset");
                            _haveOffset = false;
                            _prober = link;
                            _stillFrames = 0;
                            HoldRest(link);
                            continue;
                        }

                        link.Ours.position = link.Donor.position + link.Donor.rotation * _offsetPos;
                        link.Ours.rotation = link.Donor.rotation * _offsetRot;

                        if (MagSkin.Apply(link.Ours, link.Donor, _log))
                        {
                            try
                            {
                                var orr = link.Ours.GetComponent<Renderer>();
                                if (orr != null && orr.enabled) orr.enabled = false;
                            }
                            catch { }
                        }

                        if (MagSkin.Ready) SkinAllMagazines(link);
                    }
                    catch { }
                }
            }
            catch { }
        }

        static void TryCapture(Link link)
        {
            var parent = link.Ours.parent;
            if (parent == null) return;

            var probe = parent.InverseTransformPoint(link.Donor.position);
            if ((probe - _lastProbe).sqrMagnitude > StillEpsilon * StillEpsilon)
            {
                _lastProbe = probe;
                _stillFrames = 0;
                return;
            }
            if (++_stillFrames < SettleFrames) return;

            float d = Vector3.Distance(link.Donor.position, link.Ours.position);
            if (d > SeatRadius)
            {

                if (_stillFrames == SettleFrames && _log != null)
                    _log.Msg(string.Format("mag: donor settled {0:F3}m from ours, outside the well "
                                         + "(need <{1:F2}) - holding at rest, still probing",
                                           d, SeatRadius));
                return;
            }

            var inv = Quaternion.Inverse(link.Donor.rotation);
            _offsetPos = inv * (link.Ours.position - link.Donor.position);
            _offsetRot = inv * link.Ours.rotation;
            _haveOffset = true;
            _prober = null;
            _bodyPos = parent.localPosition;
            _bodyRot = parent.localRotation;
            if (_log != null)
                _log.Msg(string.Format("mag: locked to '{0}' at a fixed offset, {1:F3}m, captured "
                                     + "after {2} still frames", link.Donor.gameObject.name, d,
                                       SettleFrames));
        }

        static bool BodyMoved(Transform body)
        {
            try
            {
                if ((body.localPosition - _bodyPos).sqrMagnitude
                    > BodyMoveEpsilon * BodyMoveEpsilon) return true;
                return Quaternion.Angle(body.localRotation, _bodyRot) > 0.25f;
            }
            catch { return false; }
        }

        static void SweepWeapon(Link link)
        {
            var w = link.Weapon;
            bool dead;
            try { dead = w == null || w.transform == null; } catch { dead = true; }
            if (dead)
            {
                link.Weapon = FindWeapon(link.Ours);
                w = link.Weapon;
                if (w == null) return;
            }

            Il2Cpp.Magazine[] mags = null;
            try { mags = w.transform.GetComponentsInChildren<Il2Cpp.Magazine>(true); } catch { }
            if (mags == null) return;

            int setHash = 17;
            for (int i = 0; i < mags.Length; i++)
            { try { if (mags[i] != null) setHash = setHash * 31 + mags[i].GetInstanceID(); } catch { } }
            if (setHash != _lastMagSetHash)
            {
                _lastMagSetHash = setHash;
                if (_censusReports++ < 10) ReportMags(mags);
            }

            Transform seated = null;
            for (int i = 0; i < mags.Length; i++)
            {
                var m = mags[i];
                if (m == null) continue;
                Transform t;
                try { t = m.transform; } catch { continue; }
                if (t == null) continue;

                MagSkin.Apply(link.Ours, t, _log);

                if (seated == null && HSRMod.IsMagazineSocketed(t)) seated = t;
            }

            if (seated != null) link.Donor = seated;

            HoldRest(link);
        }

        static int _lastMagSetHash;
        static int _censusReports;

        static void ReportMags(Il2Cpp.Magazine[] mags)
        {
            if (_log == null) return;
            try
            {
                _log.Msg("MAGSWEEP: magazine set changed — {0} object(s) on the weapon:", mags.Length);
                for (int i = 0; i < mags.Length; i++)
                {
                    var m = mags[i];
                    if (m == null) continue;
                    Transform t;
                    try { t = m.transform; } catch { continue; }
                    if (t == null) continue;

                    int on = 0, forced = 0, total = 0;
                    try
                    {
                        var rs = t.GetComponentsInChildren<Renderer>(true);
                        for (int r = 0; r < rs.Length; r++)
                        {
                            var rr = rs[r]; if (rr == null) continue;
                            total++;
                            if (rr.enabled) on++;
                            if (rr.forceRenderingOff) forced++;
                        }
                    }
                    catch { }

                    bool skin = false; try { skin = t.Find("M200_MagSkin") != null; } catch { }
                    bool seated = false; try { seated = HSRMod.IsMagazineSocketed(t); } catch { }
                    bool active = false; try { active = t.gameObject.activeInHierarchy; } catch { }

                    string path = "?";
                    try
                    {
                        var sb = new System.Text.StringBuilder(t.gameObject.name);
                        var pnt = t.parent; int g = 0;
                        while (pnt != null && g++ < 4)
                        { sb.Insert(0, pnt.gameObject.name + "/"); pnt = pnt.parent; }
                        path = sb.ToString();
                    }
                    catch { }

                    _log.Msg("   '{0}' seated={1} skin={2} active={3} rends={4} on={5} forcedOff={6}  local={7}  {8}",
                        t.gameObject.name, seated, skin, active, total, on, forced,
                        t.localPosition.ToString("F3"), path);
                }
            }
            catch { }
        }

        static Il2Cpp.WeaponV3 FindWeapon(Transform ours)
        {
            try
            {
                Transform t = ours;
                int g = 0;
                while (t != null && g++ < 12)
                {
                    var wv = t.GetComponent<Il2Cpp.WeaponV3>();
                    if (wv != null) return wv;
                    t = t.parent;
                }
            }
            catch { }
            return null;
        }

        static int _skinSweeps;

        static void SkinAllMagazines(Link link)
        {
            try
            {
                bool dead;
                try { dead = link.Weapon == null || link.Weapon.transform == null; } catch { dead = true; }
                if (dead) { link.Weapon = FindWeapon(link.Ours); }
                var w = link.Weapon;
                if (w == null) return;

                Il2Cpp.Magazine[] mags = null;
                try { mags = w.transform.GetComponentsInChildren<Il2Cpp.Magazine>(true); } catch { }
                if (mags == null) return;

                for (int i = 0; i < mags.Length; i++)
                {
                    var m = mags[i];
                    if (m == null) continue;
                    Transform t;
                    try { t = m.transform; } catch { continue; }
                    if (t == null || t == link.Donor) continue;

                    bool already;
                    try { already = t.Find("M200_MagSkin") != null; } catch { continue; }
                    if (already) continue;

                    if (MagSkin.Apply(link.Ours, t, _log) && _log != null && _skinSweeps++ < 6)
                        _log.Msg("mag: skinned an extra magazine on the rifle ('" + t.gameObject.name
                               + "') — a reload leaves more than one.");
                }
            }
            catch { }
        }

        static int _rebinds;

        static Transform FindFittedMag(Transform ours)
        {
            try
            {

                Transform w = ours;
                int g = 0;
                Il2Cpp.WeaponV3 wv = null;
                while (w != null && g++ < 12)
                {
                    wv = w.GetComponent<Il2Cpp.WeaponV3>();
                    if (wv != null) break;
                    w = w.parent;
                }
                if (wv == null || wv.transform == null) return null;

                var mags = wv.transform.GetComponentsInChildren<Il2Cpp.Magazine>(true);
                if (mags == null) return null;
                for (int i = 0; i < mags.Length; i++)
                {
                    var m = mags[i];
                    if (m == null || m.transform == null) continue;
                    if (HSRMod.IsMagazineSocketed(m.transform)) return m.transform;
                }
            }
            catch { }
            return null;
        }

        static void HoldRest(Link link)
        {
            if (link == null || link.Ours == null) return;
            link.Ours.localPosition = link.RestPos;
            link.Ours.localRotation = link.RestRot;
        }
    }
}
