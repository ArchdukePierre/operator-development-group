// ODG Operations - HSR
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using MelonLoader;
using UnityEngine;

namespace HSR
{

    internal static class RackProbe
    {

        const int MaxTracked = 400;

        const float Window = 2.5f;

        const float MoveEpsilon = 0.0005f;
        const float TurnEpsilon = 0.35f;

        const int MaxRuns = 3;

        static MelonPreferences_Entry<bool> _enabled;
        internal static void Init(MelonPreferences_Category cat)
        {

            _enabled = cat.CreateEntry("RackProbe", true);

            _autoFit = cat.CreateEntry("AutoFitBolt", false);
        }

        static bool Enabled
        {
            get { try { return _enabled != null && _enabled.Value; } catch { return false; } }
        }

        static MelonPreferences_Entry<bool> _autoFit;
        static bool AutoFit { get { try { return _autoFit != null && _autoFit.Value; } catch { return false; } } }

        static Vector3 GripPoint()
        {
            Vector3 sum = Vector3.zero; int n = 0;
            for (int i = 0; i < _grip.Length; i++)
            {
                var t = _grip[i];
                if (t == null) continue;
                try { sum += t.position; n++; } catch { }
            }
            if (n > 0) return sum / n;
            try { return _hand != null ? _hand.position : Vector3.zero; } catch { return Vector3.zero; }
        }

        static Transform[] _t;
        static Vector3[] _p0;
        static Quaternion[] _r0;
        static float[] _maxD, _maxA, _firstMove, _lastMove;
        static int _n;

        static float _t0, _until;

        static Transform _ourBolt, _hand;

        static readonly string[] GripBones = { "index_02_r", "middle_02_r", "ring_02_r", "pinky_02_r" };
        static readonly Transform[] _grip = new Transform[4];
        static Vector3 _handleAtGrab, _handAtGrab;
        static bool _grabbed;
        static Transform _donorBolt;
        static int _runs;
        static bool _running;
        static MelonLogger.Instance _log;
        static bool _dumpedAnimator;

        internal static void Arm(Transform weaponRoot, MelonLogger.Instance log)
        {
            Arm(weaponRoot, null, log);
        }

        internal static void Arm(Transform weaponRoot, Transform ourBolt, MelonLogger.Instance log)
        {
            try
            {
                if (!Enabled || weaponRoot == null) return;
                if (_running || _runs >= MaxRuns) return;
                _log = log;

                if (!_dumpedAnimator) { _dumpedAnimator = true; DumpAnimator(weaponRoot, log); }

                var all = weaponRoot.GetComponentsInChildren<Transform>(true);
                if (all == null) return;

                if (_t == null || _t.Length < MaxTracked)
                {
                    _t = new Transform[MaxTracked];
                    _p0 = new Vector3[MaxTracked];
                    _r0 = new Quaternion[MaxTracked];
                    _maxD = new float[MaxTracked];
                    _maxA = new float[MaxTracked];
                    _firstMove = new float[MaxTracked];
                    _lastMove = new float[MaxTracked];
                }

                _n = 0;
                for (int i = 0; i < all.Length && _n < MaxTracked; i++)
                {
                    var t = all[i];
                    if (t == null) continue;

                    if (IsOurs(t)) continue;

                    _t[_n] = t;
                    _p0[_n] = t.localPosition;
                    _r0[_n] = t.localRotation;
                    _maxD[_n] = 0f;
                    _maxA[_n] = 0f;
                    _firstMove[_n] = -1f;
                    _lastMove[_n] = -1f;
                    _n++;
                }

                _ourBolt = ourBolt;
                _grabbed = false;
                _hand = null;
                _donorBolt = null;
                for (int g = 0; g < _grip.Length; g++) _grip[g] = null;

                for (int i = 0; i < _n; i++)
                {
                    var nm = _t[i].gameObject.name;
                    if (_hand == null && string.Equals(nm, "hand_r", StringComparison.Ordinal)) _hand = _t[i];
                    for (int g = 0; g < GripBones.Length; g++)
                        if (_grip[g] == null && string.Equals(nm, GripBones[g], StringComparison.Ordinal)) _grip[g] = _t[i];
                    if (_donorBolt == null && string.Equals(nm, "Bolt", StringComparison.Ordinal)
                        && _t[i].childCount > 0) _donorBolt = _t[i];
                }

                _t0 = Time.time;
                _until = _t0 + Window;
                _running = true;
            }
            catch { _running = false; }
        }

        static bool IsOurs(Transform t)
        {
            int g = 0;
            while (t != null && g++ < 8)
            {
                var n = t.gameObject.name;
                if (n != null && (n.StartsWith("m200_", StringComparison.OrdinalIgnoreCase) ||
                                  n.StartsWith("HSR_", StringComparison.OrdinalIgnoreCase))) return true;
                t = t.parent;
            }
            return false;
        }

        internal static void Tick()
        {
            if (!_running) return;
            try
            {
                float now = Time.time - _t0;
                for (int i = 0; i < _n; i++)
                {
                    var t = _t[i];
                    if (t == null) continue;
                    float d = Vector3.Distance(t.localPosition, _p0[i]);
                    float a = Quaternion.Angle(t.localRotation, _r0[i]);
                    if (d > _maxD[i]) _maxD[i] = d;
                    if (a > _maxA[i]) _maxA[i] = a;
                    if (d > MoveEpsilon || a > TurnEpsilon)
                    {
                        if (_firstMove[i] < 0f) _firstMove[i] = now;
                        _lastMove[i] = now;
                    }
                }

                if (!_grabbed && _donorBolt != null && _hand != null && _ourBolt != null)
                {
                    for (int i = 0; i < _n; i++)
                    {
                        if (_t[i] != _donorBolt) continue;
                        if (_maxD[i] <= MoveEpsilon) break;
                        _grabbed = true;
                        _handAtGrab = GripPoint();
                        _handleAtGrab = HandleWorld(_ourBolt);
                        break;
                    }
                }

                if (Time.time < _until) return;
                _running = false;
                _runs++;
                Report();
            }
            catch { _running = false; }
        }

        static void Report()
        {
            if (_log == null) return;

            float first = float.MaxValue, last = -1f;
            int movers = 0;
            for (int i = 0; i < _n; i++)
            {
                if (_firstMove[i] < 0f) continue;
                movers++;
                if (_firstMove[i] < first) first = _firstMove[i];
                if (_lastMove[i] > last) last = _lastMove[i];
            }

            if (movers == 0)
            {
                _log.Msg(string.Format(
                    "RACKPROBE {0}/{1}: watched {2} donor transforms for {3:F1}s and NONE moved. "
                  + "The rack is animating the character, not the weapon rig, so there is nothing "
                  + "here to ride or to time against. Pin BoltCycleSeconds by eye.",
                    _runs, MaxRuns, _n, Window));
                return;
            }

            _log.Msg(string.Format(
                "RACKPROBE {0}/{1}: {2} of {3} donor transforms moved. MOTION SPANS {4:F3}s "
              + "(first at {5:F3}s, last at {6:F3}s after the rack began). That span is the "
              + "duration to match; set BoltCycleSeconds to it.",
                _runs, MaxRuns, movers, _n, last - first, first, last));

            if (_grabbed && _ourBolt != null)
            {

                var parent = _ourBolt.parent;
                Vector3 d = _handAtGrab - _handleAtGrab;
                Vector3 local = parent != null ? parent.InverseTransformVector(d) : d;
                _log.Msg(string.Format(
                    "   GRIP: at the grab the fingers close {0:F4}m from our handle. Offset to close "
                  + "it: BoltOffsetX {1:F4}  BoltOffsetY {2:F4}  BoltOffsetZ {3:F4}{4}",
                    d.magnitude, local.x, local.y, local.z,
                    AutoFit ? "  -- APPLIED" : "  (set AutoFitBolt=true to apply it)"));

                if (AutoFit)
                {

                    var next = BoltCycle.Offset + local;
                    BoltCycle.SetOffset(next);
                    try { MelonPreferences.Save(); } catch { }
                    _log.Msg(string.Format(
                        "   GRIP: applied -> BoltOffsetX {0:F4}  BoltOffsetY {1:F4}  BoltOffsetZ {2:F4}",
                        next.x, next.y, next.z));
                }
            }

            for (int i = 0; i < _n; i++)
            {
                var t = _t[i];
                if (t == null) continue;
                var nm = t.gameObject.name;
                if (nm == null || nm.IndexOf("Bolt", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (_firstMove[i] < 0f)
                {
                    _log.Msg(string.Format("   BOLT {0,-22} DID NOT MOVE   {1}", nm, PathOf(t, 3)));
                    continue;
                }
                _log.Msg(string.Format("   BOLT {0,-22} {1:F4}m {2,6:F1}deg  from {3:F3}s to {4:F3}s   {5}",
                    nm, _maxD[i], _maxA[i], _firstMove[i], _lastMove[i], PathOf(t, 3)));
            }

            for (int rank = 0; rank < 8; rank++)
            {
                int best = -1;
                float bestScore = 0f;
                for (int i = 0; i < _n; i++)
                {
                    if (_firstMove[i] < 0f) continue;

                    float score = _maxD[i] * 100f + _maxA[i];
                    if (score <= bestScore) continue;
                    bestScore = score; best = i;
                }
                if (best < 0) break;
                var t = _t[best];
                _log.Msg(string.Format(
                    "   moved {0,-28} {1:F4}m {2,6:F1}deg  from {3:F3}s to {4:F3}s   {5}",
                    t != null ? t.gameObject.name : "?",
                    _maxD[best], _maxA[best], _firstMove[best], _lastMove[best],
                    PathOf(t, 3)));
                _firstMove[best] = -1f;
            }
        }

        static void DumpAnimator(Transform weaponRoot, MelonLogger.Instance log)
        {
            try
            {
                var anims = weaponRoot.GetComponentsInChildren<Animator>(true);
                int count = anims != null ? anims.Length : 0;
                log.Msg("RACKPROBE animator census: {0} Animator(s) under the weapon.", count);
                if (count == 0) return;

                for (int a = 0; a < count && a < 3; a++)
                {
                    var an = anims[a];
                    if (an == null) continue;
                    string ctrl = "none";
                    int nclips = 0;
                    try
                    {
                        var rac = an.runtimeAnimatorController;
                        if (rac != null)
                        {
                            ctrl = rac.name;
                            var cl = rac.animationClips;
                            nclips = cl != null ? cl.Length : 0;
                            log.Msg("   animator '{0}' speed={1:F2} controller '{2}' with {3} clip(s):",
                                an.gameObject.name, an.speed, ctrl, nclips);
                            for (int i = 0; i < nclips && i < 24; i++)
                            {
                                var c = cl[i];
                                if (c == null) continue;
                                log.Msg("      clip '{0}' {1:F3}s", c.name, c.length);
                            }
                            continue;
                        }
                    }
                    catch { }
                    log.Msg("   animator '{0}' controller {1} clips {2}", an.gameObject.name, ctrl, nclips);
                }
            }
            catch (Exception e) { log.Warning("RACKPROBE animator census failed: " + e.Message); }
        }

        static Vector3 HandleWorld(Transform bolt)
        {
            try
            {
                var mf = bolt.GetComponent<MeshFilter>();
                var mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null) return bolt.position;
                var verts = mesh.vertices;
                int n = verts != null ? verts.Length : 0;
                if (n == 0) return bolt.TransformPoint(mesh.bounds.center);

                float lo = float.MaxValue, hi = float.MinValue;
                for (int i = 0; i < n; i++) { float x = verts[i].x; if (x < lo) lo = x; if (x > hi) hi = x; }
                float cut = hi - (hi - lo) * 0.1f;

                Vector3 sum = Vector3.zero; int c = 0;
                for (int i = 0; i < n; i++) { var v = verts[i]; if (v.x >= cut) { sum += v; c++; } }
                if (c == 0) return bolt.TransformPoint(mesh.bounds.center);
                return bolt.TransformPoint(sum / c);
            }
            catch { return bolt.position; }
        }

        static string PathOf(Transform t, int depth)
        {
            try
            {
                if (t == null) return "?";
                var s = t.gameObject.name;
                var p = t.parent;
                int g = 0;
                while (p != null && g++ < depth) { s = p.gameObject.name + "/" + s; p = p.parent; }
                return s;
            }
            catch { return "?"; }
        }
    }
}
