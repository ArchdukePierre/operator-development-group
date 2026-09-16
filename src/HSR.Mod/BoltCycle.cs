// ODG Operations - HSR
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using MelonLoader;
using UnityEngine;

namespace HSR
{

    internal static class BoltCycle
    {

        const float UnlockTime = 0.12f;
        const float BackTime = 0.18f;
        const float DwellTime = 0.06f;
        const float ForwardTime = 0.18f;
        const float LockTime = 0.12f;

        const float DefaultTotal = UnlockTime + BackTime + DwellTime + ForwardTime + LockTime;
        static readonly float FUnlock = UnlockTime / DefaultTotal;
        static readonly float FBack = (UnlockTime + BackTime) / DefaultTotal;
        static readonly float FDwell = (UnlockTime + BackTime + DwellTime) / DefaultTotal;
        static readonly float FForward = (UnlockTime + BackTime + DwellTime + ForwardTime) / DefaultTotal;

        static float _total = DefaultTotal;
        static int _rejects;

        internal static void SetCycleSeconds(float seconds, string why, MelonLogger.Instance log)
        {
            if (Pinned) return;

            if (seconds < 0.08f || seconds > 4f)
            {
                if (log != null && _rejects++ < 4)
                    log.Warning(string.Format(
                        "bolt: ignoring a {0:F3}s reading from {1} — outside 0.08s..4.0s, so it is "
                      + "not a bolt cycle. Cycle stays at {2:F3}s.", seconds, why, _total));
                return;
            }
            if (Mathf.Abs(seconds - _total) < 0.005f) return;
            float was = _total;
            _total = seconds;
            if (log != null)
                log.Msg(string.Format("bolt: cycle retimed {0:F3}s -> {1:F3}s to match {2}",
                                      was, seconds, why));
        }

        internal static float CycleSeconds { get { return _total; } }

        internal static bool AnyRiding { get { return _rides > 0; } }

        const float UnlockDegrees = 65f;

        const float TravelMetres = 0.135f;

        static MelonPreferences_Entry<bool> _procedural;
        static MelonPreferences_Entry<bool> _probe;
        static MelonPreferences_Entry<bool> _onShot;

        internal static void Init(MelonPreferences_Category cat)
        {

            _procedural = cat.CreateEntry("ProceduralBolt", false);

            _probe = cat.CreateEntry("BoltProbe", true);

            _onShot = cat.CreateEntry("BoltCycleOnShot", false);

            _seconds = cat.CreateEntry("BoltCycleSeconds", 0f);
            try { if (_seconds.Value > 0f) _total = _seconds.Value; } catch { }

            _offX = cat.CreateEntry("BoltOffsetX", 0f);
            _offY = cat.CreateEntry("BoltOffsetY", 0f);
            _offZ = cat.CreateEntry("BoltOffsetZ", 0f);
            _clock = cat.CreateEntry("BoltClockDegrees", 0f);
        }

        static MelonPreferences_Entry<float> _offX, _offY, _offZ, _clock;

        internal static Vector3 Offset
        {
            get
            {
                try { return new Vector3(_offX.Value, _offY.Value, _offZ.Value); }
                catch { return Vector3.zero; }
            }
        }

        internal static float ClockDegrees
        {
            get { try { return _clock == null ? 0f : _clock.Value; } catch { return 0f; } }
        }

        internal static Vector3 NudgeOffset(float dx, float dy, float dz)
        {
            try
            {
                _offX.Value += dx; _offY.Value += dy; _offZ.Value += dz;
                return Offset;
            }
            catch { return Vector3.zero; }
        }

        internal static void SetOffset(Vector3 v)
        {
            try { _offX.Value = v.x; _offY.Value = v.y; _offZ.Value = v.z; } catch { }
        }

        internal static float NudgeClock(float d)
        {
            try { _clock.Value += d; return _clock.Value; } catch { return 0f; }
        }

        static MelonPreferences_Entry<float> _seconds;

        static bool Pinned
        {
            get { try { return _seconds != null && _seconds.Value > 0f; } catch { return false; } }
        }

        internal static bool ProceduralEnabled
        {
            get { try { return _procedural != null && _procedural.Value; } catch { return false; } }
        }

        internal static bool CycleOnShot
        {
            get { try { return _onShot != null && _onShot.Value; } catch { return false; } }
        }

        static MelonLogger.Instance _log;
        static Transform _donorBolt;
        static Vector3 _restPos;
        static Quaternion _restRot;
        static bool _haveRest;
        static float _until;
        static float _maxTravel, _maxDegrees;
        static int _shotsProbed;
        const int ProbeShots = 3;

        internal static void NoteDonorBolt(Transform action, MelonLogger.Instance log)
        {
            if (action == null) return;
            if (_donorBolt == action) return;
            _donorBolt = action;
            _haveRest = false;
            log.Msg("bolt: donor animated transform is '" + action.gameObject.name
                  + "' at " + PathOf(action, 4));
        }

        internal static void OnShot(MelonLogger.Instance log)
        {
            try
            {
                if (_probe == null || !_probe.Value) return;
                if (_donorBolt == null || _shotsProbed >= ProbeShots) return;
                if (!_haveRest)
                {
                    _restPos = _donorBolt.localPosition;
                    _restRot = _donorBolt.localRotation;
                    _haveRest = true;
                }
                _until = Time.time + 1.0f;
                _maxTravel = 0f;
                _maxDegrees = 0f;
            }
            catch { }
        }

        internal static void Tick(MelonLogger.Instance log)
        {
            try
            {
                if (_until <= 0f || _donorBolt == null) return;

                float d = Vector3.Distance(_donorBolt.localPosition, _restPos);
                if (d > _maxTravel) _maxTravel = d;
                float a = Quaternion.Angle(_donorBolt.localRotation, _restRot);
                if (a > _maxDegrees) _maxDegrees = a;

                if (Time.time < _until) return;
                _until = 0f;
                _shotsProbed++;

                bool moved = _maxTravel > 0.002f || _maxDegrees > 1.0f;
                log.Msg(string.Format(
                    "BOLTPROBE shot {0}/{1}: donor '{2}' moved {3:F4} m and {4:F1} deg  -> {5}",
                    _shotsProbed, ProbeShots, _donorBolt.gameObject.name,
                    _maxTravel, _maxDegrees,
                    moved ? "ANIMATED, riding it is correct"
                          : "STATIC — set ProceduralBolt=true under [HSR] to drive it ourselves"));
            }
            catch { }
        }

        sealed class Bolt
        {
            internal Transform T;
            internal Vector3 RestPos;
            internal Quaternion RestRot;
            internal bool HaveRest;
            internal float Clock = -1f;

            internal Vector3 Pivot;
            internal bool HavePivot;

            internal Transform RideRot;
            internal Transform RidePos;
            internal Quaternion RideRotRest;
            internal Vector3 RidePosRest;
            internal bool HaveRideRest;
        }

        static readonly System.Collections.Generic.List<Bolt> _bolts =
            new System.Collections.Generic.List<Bolt>();

        static int _running;

        static int _rides;

        static Bolt Entry(Transform bolt)
        {
            if (bolt == null) return null;
            Bolt found = null;
            for (int i = _bolts.Count - 1; i >= 0; i--)
            {
                var b = _bolts[i];
                bool dead;
                try { dead = b.T == null; } catch { dead = true; }
                if (dead)
                {
                    if (b.Clock >= 0f && _running > 0) _running--;
                    _bolts.RemoveAt(i);
                    continue;
                }
                if (b.T == bolt) found = b;
            }
            if (found != null) return found;
            var n = new Bolt();
            n.T = bolt;
            n.Pivot = PivotOf(bolt, out n.HavePivot);
            _bolts.Add(n);
            if (_log != null)
                _log.Msg(string.Format("bolt: '{0}' turns about local ({1:F4}, {2:F4}) {3}",
                    bolt.gameObject.name, n.Pivot.x, n.Pivot.y,
                    n.HavePivot ? "(from the mesh)"
                                : "(NO MESH FOUND - falling back to the body origin, which is the "
                                + "old wrong behaviour; the bolt will swing wide)"));
            return n;
        }

        static Vector3 PivotOf(Transform bolt, out bool ok)
        {
            ok = false;
            try
            {
                var mf = bolt.GetComponent<MeshFilter>();
                var mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null) return Vector3.zero;

                var verts = mesh.vertices;
                int n = verts != null ? verts.Length : 0;
                if (n == 0) { ok = true; return mesh.bounds.center; }

                var xs = new float[n];
                var ys = new float[n];
                for (int i = 0; i < n; i++) { var p = verts[i]; xs[i] = p.x; ys[i] = p.y; }
                Array.Sort(xs);
                Array.Sort(ys);
                ok = true;

                return new Vector3(xs[n / 2], ys[n / 2], mesh.bounds.center.z);
            }
            catch { return Vector3.zero; }
        }

        internal static void BindOurs(Transform bolt, MelonLogger.Instance log = null)
        {
            if (log != null) _log = log;
            try { Entry(bolt); } catch { }
        }

        internal static void BindRide(Transform ourBolt, Transform rideRot, Transform ridePos,
                                      MelonLogger.Instance log)
        {
            try
            {
                if (log != null) _log = log;
                if (ourBolt == null || rideRot == null) return;
                var b = Entry(ourBolt);
                if (b == null) return;
                if (b.RideRot == rideRot) return;

                if (b.RideRot == null) _rides++;
                b.RideRot = rideRot;
                b.RidePos = ridePos;
                b.HaveRideRest = false;
                if (log != null)
                    log.Msg("bolt: riding the donor's '" + rideRot.gameObject.name + "'"
                          + (ridePos != null ? " (+ '" + ridePos.gameObject.name + "' for travel)" : "")
                          + " — timing now comes from the game, not from our clock.");
            }
            catch { }
        }

        static bool TickRide(Bolt b)
        {
            if (b.RideRot == null) return false;
            try
            {
                if (!b.HaveRideRest)
                {
                    b.RideRotRest = b.RideRot.localRotation;
                    if (b.RidePos != null) b.RidePosRest = b.RidePos.localPosition;
                    b.HaveRideRest = true;
                    return true;
                }
                if (!b.HaveRest)
                {
                    b.RestPos = b.T.localPosition;
                    b.RestRot = b.T.localRotation;
                    b.HaveRest = true;
                }

                float deg = Quaternion.Angle(b.RideRot.localRotation, b.RideRotRest);
                float back = 0f;
                if (b.RidePos != null)
                    back = Vector3.Distance(b.RidePos.localPosition, b.RidePosRest);

                if (b.RidePos == null && deg > 1f)
                {
                    float t = deg / UnlockDegrees;
                    if (t > 1f) t = 1f;
                    back = TravelMetres * t;
                }

                Place(b, deg, back);
                return true;
            }
            catch { return false; }
        }

        const float RestDriftEpsilon = 0.002f;
        static int _driftReports;

        const float RetriggerLockout = 0.05f;
        static float _lastStart = -1f;

        internal static void StartCycle(Transform bolt)
        {
            if (!ProceduralEnabled) return;
            var b = Entry(bolt);
            if (b == null) return;

            float now = Time.time;
            if (b.Clock >= 0f && now - _lastStart < RetriggerLockout) return;
            _lastStart = now;

            try
            {

                if (b.HaveRest && b.Clock < 0f)
                {
                    float d = Vector3.Distance(b.T.localPosition, b.RestPos);
                    float a = Quaternion.Angle(b.T.localRotation, b.RestRot);
                    if (d > RestDriftEpsilon || a > 1.0f)
                    {
                        if (_driftReports++ < 8 && _log != null)
                            _log.Msg(string.Format(
                                "bolt: rest pose drifted {0:F4}m / {1:F1}deg since capture on '{2}' "
                              + "- re-taking it. (local now {3}, captured {4})",
                                d, a, b.T.gameObject.name,
                                b.T.localPosition.ToString("F4"), b.RestPos.ToString("F4")));
                        b.HaveRest = false;
                    }
                }

                if (!b.HaveRest)
                {
                    b.RestPos = b.T.localPosition;
                    b.RestRot = b.T.localRotation;
                    b.HaveRest = true;
                }

                if (b.Clock < 0f) _running++;
                b.Clock = 0f;
            }
            catch { }
        }

        internal static void TickProcedural()
        {

            if (_rides > 0)
            {
                for (int i = _bolts.Count - 1; i >= 0; i--)
                {
                    var rb = _bolts[i];
                    try
                    {
                        if (rb.T == null || rb.RideRot == null) continue;
                        TickRide(rb);
                    }
                    catch { }
                }
            }

            if (_running <= 0) return;
            float dt = Time.deltaTime;
            float total = _total;
            if (total <= 0f) total = DefaultTotal;

            for (int i = _bolts.Count - 1; i >= 0; i--)
            {
                var b = _bolts[i];
                try
                {
                    if (b.T == null)
                    {
                        if (b.Clock >= 0f && _running > 0) _running--;
                        _bolts.RemoveAt(i);
                        continue;
                    }
                    if (b.RideRot != null) { b.Clock = -1f; if (_running > 0) _running--; continue; }
                    if (b.Clock < 0f || !b.HaveRest) continue;

                    b.Clock += dt;
                    if (b.Clock >= total)
                    {
                        b.T.localPosition = b.RestPos;
                        b.T.localRotation = b.RestRot;
                        b.Clock = -1f;
                        if (_running > 0) _running--;
                        continue;
                    }

                    float u = b.Clock / total;
                    float rot, back;
                    if (u < FUnlock)
                    {
                        rot = Ease(u / FUnlock); back = 0f;
                    }
                    else if (u < FBack)
                    {
                        rot = 1f; back = Ease((u - FUnlock) / (FBack - FUnlock));
                    }
                    else if (u < FDwell)
                    {
                        rot = 1f; back = 1f;
                    }
                    else if (u < FForward)
                    {
                        rot = 1f; back = 1f - Ease((u - FDwell) / (FForward - FDwell));
                    }
                    else
                    {
                        rot = 1f - Ease((u - FForward) / (1f - FForward));
                        back = 0f;
                    }

                    Place(b, UnlockDegrees * rot, TravelMetres * back);
                }
                catch
                {

                    try { if (b.Clock >= 0f && _running > 0) _running--; b.Clock = -1f; } catch { }
                }
            }
        }

        static void Place(Bolt b, float degrees, float back)
        {
            var q = Quaternion.AngleAxis(degrees + ClockDegrees, Vector3.forward);
            b.T.localRotation = q * b.RestRot;
            b.T.localPosition = b.Pivot + q * (b.RestPos - b.Pivot)
                              + new Vector3(0f, 0f, back)
                              + Offset;
        }

        static float Ease(float x)
        {
            if (x <= 0f) return 0f;
            if (x >= 1f) return 1f;
            return x * x * (3f - 2f * x);
        }

        static string PathOf(Transform t, int depth)
        {
            var s = t.gameObject.name;
            var p = t.parent;
            int g = 0;
            while (p != null && g++ < depth) { s = p.gameObject.name + "/" + s; p = p.parent; }
            return s;
        }
    }
}
