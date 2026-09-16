// ODG Operations - ThirdPerson
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace ThirdPerson
{

    internal sealed class Rig
    {
        internal CamParams Target = CamParams.Default();
        internal CamParams Applied = CamParams.Default();

        internal float Blend;
        internal float ShoulderBlend = 1f;

        internal float PositionSmoothing = 12f;
        internal float RotationSmoothing = 16f;
        internal float SwapSeconds = 0.35f;
        internal float BlendSeconds = 0.25f;
        internal bool Collision = true;
        internal float CollisionRadius = 0.2f;
        internal float MinDistance = 0.3f;
        internal int CollisionMask = ~(1 << 2);

        internal bool Collided;
        internal float Focal = 20f;
        internal float EffectiveBlend;

        internal float SqueezeIn = 0.45f;
        internal float SqueezeOut = 0.70f;
        internal float SqueezeSeconds = 0.15f;
        internal float HeadHideBelow = 0.50f;
        internal float HeadShowAbove = 0.62f;
        internal float Squeeze;
        internal float CameraDistance;
        internal bool HeadTooClose;
        private float _squeezeTarget;

        internal float EvadeSeconds = 0.35f;
        internal float Evade = 1f;
        private float _evadeTarget = 1f;
        private static readonly float[] EvadeSteps = { 0.5f, 0f, -0.5f, -1f };

        private float _shoulderFrom = 1f, _shoulderTo = 1f, _shoulderT = 1f;

        private Vector3 _smoothPos;
        private Quaternion _smoothRot = Quaternion.identity;
        private bool _smoothInit;

        private Vector3 _lagPos;
        private Quaternion _lagRot = Quaternion.identity;

        private readonly Il2CppStructArray<RaycastHit> _hits = new Il2CppStructArray<RaycastHit>(24L);

        internal bool SwapInProgress { get { return _shoulderT < 1f; } }

        internal void ResetSmoothing()
        {
            _smoothInit = false;
            Squeeze = 0f;
            _squeezeTarget = 0f;
            HeadTooClose = false;
            Evade = 1f;
            _evadeTarget = 1f;
        }

        internal void SnapParams()
        {
            Applied = Target;
        }

        internal void SetShoulder(bool right, bool animate)
        {
            float to = right ? 1f : -1f;
            if (!animate)
            {
                ShoulderBlend = to; _shoulderFrom = to; _shoulderTo = to; _shoulderT = 1f;
                return;
            }
            if (Mathf.Approximately(_shoulderTo, to) && _shoulderT >= 1f && Mathf.Approximately(ShoulderBlend, to)) return;
            _shoulderFrom = ShoulderBlend;
            _shoulderTo = to;
            _shoulderT = 0f;
        }

        internal void Tick(float dt, bool wantThirdPerson)
        {
            if (dt < 0f) dt = 0f;
            if (dt > 0.1f) dt = 0.1f;

            float rate = BlendSeconds <= 0.01f ? 1000f : 1f / BlendSeconds;
            Blend = Mathf.MoveTowards(Blend, wantThirdPerson ? 1f : 0f, rate * dt);

            if (_shoulderT < 1f)
            {
                _shoulderT = Mathf.Min(1f, _shoulderT + dt / Mathf.Max(0.05f, SwapSeconds));
                float e = _shoulderT * _shoulderT * (3f - 2f * _shoulderT);
                ShoulderBlend = Mathf.Lerp(_shoulderFrom, _shoulderTo, e);
            }

            float k = 1f - Mathf.Exp(-10f * dt);
            Applied.Distance = Mathf.Lerp(Applied.Distance, Target.Distance, k);
            Applied.Side = Mathf.Lerp(Applied.Side, Target.Side, k);
            Applied.Height = Mathf.Lerp(Applied.Height, Target.Height, k);
            Applied.TiltPitch = Mathf.Lerp(Applied.TiltPitch, Target.TiltPitch, k);
            Applied.TiltYaw = Mathf.Lerp(Applied.TiltYaw, Target.TiltYaw, k);
            Applied.Fov = (Target.Fov <= 0f || Applied.Fov <= 0f) ? Target.Fov : Mathf.Lerp(Applied.Fov, Target.Fov, k);
            Applied.Converge = Target.Converge;
            Applied.Right = Target.Right;
        }

        internal void Solve(Vector3 eyePos, Quaternion eyeRot, Transform ignoreRoot, float dt, bool first,
                            out Vector3 pos, out Quaternion rot)
        {
            Vector3 fwd = eyeRot * Vector3.forward;
            Vector3 up = eyeRot * Vector3.up;

            float d = Applied.Distance, s = Applied.Side, h = Applied.Height;
            float r = Mathf.Sqrt(d * d + s * s);
            float a = Mathf.Atan2(s, d) * ShoulderBlend;
            Vector3 desired = eyePos + eyeRot * LocalAt(a, h, r);

            Collided = false;
            if (Collision)
            {
                if (first)
                {

                    float plen;
                    float primary = Clearance(eyePos, desired, ignoreRoot, out plen);
                    float want = 1f;
                    if (primary >= 0f && primary < plen * 0.75f)
                    {
                        float bestAvail = primary;
                        for (int i = 0; i < EvadeSteps.Length; i++)
                        {
                            float f = EvadeSteps[i];
                            float clen;
                            float cl = Clearance(eyePos, eyePos + eyeRot * LocalAt(a * f, h, r), ignoreRoot, out clen);
                            float avail = cl < 0f ? clen : cl;
                            if (cl < 0f || cl >= clen * 0.9f) { want = f; break; }
                            if (avail > bestAvail + 0.15f) { want = f; bestAvail = avail; }
                        }
                    }
                    _evadeTarget = want;
                    Evade = Mathf.MoveTowards(Evade, _evadeTarget, dt / Mathf.Max(0.05f, EvadeSeconds));
                }

                if (Evade != 1f) desired = eyePos + eyeRot * LocalAt(a * Evade, h, r);

                float len;
                float clear = Clearance(eyePos, desired, ignoreRoot, out len);
                if (clear >= 0f && clear < len && len > 0.001f)
                {
                    desired = eyePos + (desired - eyePos) * (clear / len);
                    Collided = true;
                }
            }

            if (first)
            {
                float pulled = (desired - eyePos).magnitude;
                if (SqueezeIn > 0f && Collided && pulled < SqueezeIn) _squeezeTarget = 1f;
                else if (!Collided || pulled > SqueezeOut || SqueezeIn <= 0f) _squeezeTarget = 0f;
                Squeeze = Mathf.MoveTowards(Squeeze, _squeezeTarget, dt / Mathf.Max(0.02f, SqueezeSeconds));
            }

            Quaternion look;
            if (Applied.Converge)
            {
                if (first)
                {
                    float target = AimDistance(eyePos, fwd, ignoreRoot);
                    Focal = Mathf.Lerp(Focal, target, 1f - Mathf.Exp(-8f * dt));
                }
                Vector3 aim = eyePos + fwd * Focal;
                Vector3 to = aim - desired;
                look = to.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(to, up) : eyeRot;
            }
            else
            {
                look = eyeRot;
            }
            look = look * Quaternion.Euler(Applied.TiltPitch, Applied.TiltYaw, 0f);

            float e = Blend * Blend * (3f - 2f * Blend);
            float sq = Squeeze * Squeeze * (3f - 2f * Squeeze);
            e *= 1f - sq;
            EffectiveBlend = e;
            Vector3 p = Vector3.Lerp(eyePos, desired, e);
            Quaternion q = Quaternion.Slerp(eyeRot, look, e);

            bool rigid = !_smoothInit || Blend < 0.999f || SwapInProgress || Squeeze != _squeezeTarget || Evade != _evadeTarget;

            if (first)
            {
                if (rigid || PositionSmoothing <= 0f) _smoothPos = p;
                else _smoothPos = Vector3.Lerp(_smoothPos, p, 1f - Mathf.Exp(-PositionSmoothing * dt));

                if (rigid || RotationSmoothing <= 0f) _smoothRot = q;
                else _smoothRot = Quaternion.Slerp(_smoothRot, q, 1f - Mathf.Exp(-RotationSmoothing * dt));

                _smoothInit = true;
                _lagPos = _smoothPos - p;
                _lagRot = Quaternion.Inverse(q) * _smoothRot;
            }
            else
            {
                _smoothPos = p + _lagPos;
                _smoothRot = q * _lagRot;
            }

            pos = _smoothPos;
            rot = _smoothRot;

            CameraDistance = (pos - eyePos).magnitude;
            if (HeadTooClose) { if (CameraDistance > HeadShowAbove) HeadTooClose = false; }
            else if (CameraDistance < HeadHideBelow) HeadTooClose = true;
        }

        private static Vector3 LocalAt(float azimuth, float height, float radius)
        {
            return new Vector3(radius * Mathf.Sin(azimuth), height, -radius * Mathf.Cos(azimuth));
        }

        private float Clearance(Vector3 from, Vector3 to, Transform ignoreRoot, out float len)
        {
            Vector3 delta = to - from;
            len = delta.magnitude;
            if (len <= 0.02f) return -1f;
            Vector3 dir = delta / len;
            float best = -1f;
            try
            {
                int n = Physics.SphereCastNonAlloc(from, CollisionRadius, dir, _hits, len, CollisionMask, QueryTriggerInteraction.Ignore);
                if (n > _hits.Length) n = _hits.Length;
                for (int i = 0; i < n; i++)
                {
                    var hit = _hits[i];
                    float hd = hit.distance;
                    if (hd <= 0.001f) continue;
                    if (ignoreRoot != null)
                    {
                        var col = hit.collider;
                        if (col != null && col.transform.IsChildOf(ignoreRoot)) continue;
                    }
                    if (best < 0f || hd < best) best = hd;
                }
            }
            catch { best = -1f; }
            return best;
        }

        private float AimDistance(Vector3 origin, Vector3 dir, Transform ignoreRoot)
        {
            const float Max = 80f, Min = 2.5f;
            float best = Max;
            try
            {
                int n = Physics.RaycastNonAlloc(origin, dir, _hits, Max, CollisionMask, QueryTriggerInteraction.Ignore);
                if (n > _hits.Length) n = _hits.Length;
                for (int i = 0; i < n; i++)
                {
                    var hit = _hits[i];
                    float hd = hit.distance;
                    if (hd <= 0.05f) continue;
                    if (ignoreRoot != null)
                    {
                        var col = hit.collider;
                        if (col != null && col.transform.IsChildOf(ignoreRoot)) continue;
                    }
                    if (hd < best) best = hd;
                }
            }
            catch { }
            return Mathf.Max(Min, best);
        }
    }
}
