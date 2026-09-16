// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using MelonLoader;
using UnityEngine;

namespace MapLoader
{

    public static class PlayerBody
    {
        static MelonLogger.Instance _log;
        public static void Init(MelonLogger.Instance log) { _log = log; }

        public struct Rig
        {
            public Il2Cpp.PlayerNetworking Pn;
            public Il2Cpp.MasterController Mc;
            public Il2CppCMF.AdvancedWalkerController Walker;
            public Il2CppCMF.Mover Mover;
            public Rigidbody Rb;
            public Transform Tr;
            public string Via;

            public bool Valid { get { return Tr != null && Rb != null; } }
        }

        static bool _warnedFallback;
        static string _lastVia;

        public static bool TryGet(out Rig rig)
        {
            rig = default(Rig);

            if (Fill(ref rig, Get1(), "PlayerMaster.PlayerSpawnedObject")) return true;

            if (Fill(ref rig, Get2(), "GameManager.myPlayerNetworking")) return true;

            if (Fill(ref rig, Get3(), "PlayerNetworking.isLocalPlayer scan")) return true;

            if (rig.Tr != null) return true;

            try
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    rig.Tr = cam.transform.root;
                    rig.Via = "Camera.main root (FALLBACK)";
                    if (!_warnedFallback)
                    {
                        _warnedFallback = true;
                        _log?.Warning("[body] the local avatar could not be resolved through "
                                    + "PlayerNetworking; falling back to the camera root. Position "
                                    + "reads are still correct, but a teleport through this path is "
                                    + "REFUSED rather than silently applied to the wrong object.");
                    }
                    return rig.Tr != null;
                }
            }
            catch { }

            return false;
        }

        static Il2Cpp.PlayerNetworking Get1()
        {
            try { var pm = Il2Cpp.PlayerMaster.MyPlayerMaster; return pm != null ? pm.PlayerSpawnedObject : null; }
            catch { return null; }
        }

        static Il2Cpp.PlayerNetworking Get2()
        {
            try { return Il2Cpp.GameManager.myPlayerNetworking; }
            catch { return null; }
        }

        static Il2Cpp.PlayerNetworking Get3()
        {
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<Il2Cpp.PlayerNetworking>();
                if (all != null)
                    for (int i = 0; i < all.Length; i++)
                    {
                        var p = all[i];
                        if (p == null) continue;
                        bool mine = false;
                        try { mine = p.isLocalPlayer; } catch { }
                        if (mine) return p;
                    }
            }
            catch { }
            return null;
        }

        static bool Fill(ref Rig rig, Il2Cpp.PlayerNetworking pn, string via)
        {
            if (pn == null) return false;
            Rig r = default(Rig);
            r.Pn = pn;
            r.Via = via;
            try { r.Mc = pn.masterController; } catch { }
            if (r.Mc != null)
            {
                try { r.Tr = r.Mc.transform; } catch { }
                try { r.Rb = r.Mc.rigidBody; } catch { }
                try { r.Walker = r.Mc.walkerController; } catch { }
                try { r.Mover = r.Mc.mover; } catch { }
            }
            if (r.Tr == null) { try { r.Tr = pn.transform; } catch { } }
            if (r.Tr != null && rig.Tr == null) rig = r;
            if (!r.Valid) return false;
            rig = r;
            if (_lastVia != via)
            {
                _lastVia = via;
                _log?.Msg("[body] the local avatar resolves through '" + via + "'.");
            }
            return true;
        }

        public static bool HasMovableRig
        {
            get
            {
                Rig r;
                try { return TryGet(out r) && r.Valid; }
                catch { return false; }
            }
        }

        public static string LastVia
        {
            get
            {
                Rig r;
                try { return TryGet(out r) ? (r.Via ?? "?") : "unresolved"; }
                catch { return "unresolved"; }
            }
        }

        public static bool TryPosition(out Vector3 p)
        {
            p = Vector3.zero;
            Rig r;
            if (!TryGet(out r) || r.Tr == null) return false;

            try { if (r.Rb != null) { p = r.Rb.position; return true; } } catch { }
            try { p = r.Tr.position; return true; } catch { return false; }
        }

        public static bool MoveTo(Vector3 target, string why)
        {
            Rig r;
            if (!TryGet(out r))
            {
                _log?.Warning("[body] MoveTo(" + why + "): no player rig to move. NOT MOVED.");
                return false;
            }
            if (!r.Valid || r.Rb == null)
            {
                _log?.Warning("[body] MoveTo(" + why + "): resolved the player through '"
                            + r.Via + "' but there is no rigidbody on it, so this is not the "
                            + "movable rig. REFUSING to write a position onto the wrong object "
                            + "-- that is the failure this guard exists for.");
                return false;
            }

            Vector3 before = r.Rb.position;
            try
            {

                try { r.Rb.linearVelocity = Vector3.zero; } catch { }
                r.Rb.position = target;
                try { if (r.Tr != null) r.Tr.position = target; } catch { }
                try { if (r.Walker != null) r.Walker.SetMomentum(Vector3.zero); } catch { }
                try { if (r.Mover != null) r.Mover.SetVelocity(Vector3.zero); } catch { }

                try
                {
                    var ss = r.Pn != null
                           ? r.Pn.GetComponentInChildren<Il2CppSmooth.SmoothSyncMirror>(true) : null;
                    if (ss != null) ss.teleportOwnedObjectFromOwner();
                }
                catch (Exception e) { _log?.Msg("[body] SmoothSync teleport skipped: " + e.Message); }

                try
                {
                    var fc = r.Pn != null
                           ? r.Pn.GetComponentInChildren<Il2Cpp.Character_FollowCamera>(true) : null;
                    if (fc != null) fc.SnapToTarget();
                }
                catch { }
            }
            catch (Exception e)
            {
                _log?.Error("[body] MoveTo(" + why + ") threw: " + e.Message + " -- NOT MOVED.");
                return false;
            }

            LastMoveAt = Time.realtimeSinceStartup;
            _log?.Msg("[body] moved the player via '" + r.Via + "' for " + why
                    + ": " + Fmt(before) + " -> " + Fmt(target)
                    + "  (" + Vector3.Distance(before, target).ToString("F1") + " m). "
                    + "This is a REQUEST; the CONFIRM line below is the result.");

            try { MelonCoroutines.Start(HoldAndConfirm(target, why)); }
            catch (Exception e) { _log?.Warning("[body] confirm coroutine failed to start: " + e.Message); }
            return true;
        }

        public static bool Confirmed { get; private set; }

        static bool _confirmPending;
        static float _confirmStartedAt;
        public static bool ConfirmPending
        {
            get { return _confirmPending && Time.realtimeSinceStartup - _confirmStartedAt < 4f; }
        }

        public static float LastMoveAt;
        public static Vector3 LastRequested { get; private set; }
        public static float LastError { get; private set; }

        static int _moveGen;

        static System.Collections.IEnumerator HoldAndConfirm(Vector3 target, string why)
        {
            int gen = ++_moveGen;
            Confirmed = false;
            _confirmPending = true;
            _confirmStartedAt = Time.realtimeSinceStartup;
            LastRequested = target;
            LastError = float.NaN;

            float until = Time.realtimeSinceStartup + 1f;
            int reasserts = 0;
            while (Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.1f);
                if (gen != _moveGen) yield break;
                Rig r;
                if (!TryGet(out r) || r.Rb == null) continue;
                Vector3 p;
                try { p = r.Rb.position; } catch { continue; }

                float dxz = new Vector2(p.x - target.x, p.z - target.z).magnitude;
                if (dxz <= 2f) continue;
                try
                {
                    r.Rb.linearVelocity = Vector3.zero;
                    r.Rb.position = target;
                    if (r.Tr != null) r.Tr.position = target;
                    reasserts++;
                }
                catch { }
            }

            yield return new WaitForSeconds(1.5f);
            if (gen != _moveGen) yield break;

            Vector3 now;
            if (!TryPosition(out now))
            {
                _confirmPending = false;
                _log?.Warning("[body] CONFIRM " + why + ": the player could not be read back at all. "
                            + "Treat the placement as FAILED.");
                yield break;
            }
            float err = new Vector2(now.x - target.x, now.z - target.z).magnitude;
            LastError = err;
            Confirmed = err <= 5f;
            _confirmPending = false;

            if (Confirmed)
            {
                _log?.Msg("[body] CONFIRM " + why + ": asked for " + Fmt(target)
                        + ", player is at " + Fmt(now) + " (" + err.ToString("F1")
                        + " m horizontal" + (reasserts > 0 ? ", " + reasserts + " re-assert(s)" : "")
                        + "). PLACEMENT HELD.");
            }
            else
            {
                _log?.Error("[body] CONFIRM " + why + ": asked for " + Fmt(target)
                          + " but the player is at " + Fmt(now) + " -- " + err.ToString("F0")
                          + " m away after " + reasserts + " re-assert(s). THE PLACEMENT DID NOT "
                          + "TAKE. Something else owns this body: a seat, a ride, or another mod's "
                          + "spawn. The out-of-map watch will say what it does next.");
            }
        }

        static string Fmt(Vector3 v)
        {
            return "(" + v.x.ToString("F1") + ", " + v.y.ToString("F1") + ", " + v.z.ToString("F1") + ")";
        }
    }
}
