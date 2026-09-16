// ODG Operations - FireteamRoles
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using UnityEngine;
using Il2CppFIMSpace.FProceduralAnimation;

namespace FireteamRoles
{

    internal static class Pose
    {
        class Body
        {
            internal RagdollAnimator2 Ra2;
            internal RagdollHandler Handler;
            internal bool Fallen;
            internal bool DragTone;
            internal Rigidbody Chest;
            internal Rigidbody Shoulder;
            internal Rigidbody Hips;
        }

        static readonly Dictionary<ulong, Body> _bodies = new Dictionary<ulong, Body>();

        static IRagdollAnimator2HandlerOwner Owner(Body b)
        {
            try { return b.Ra2.Cast<IRagdollAnimator2HandlerOwner>(); }
            catch { return null; }
        }

        internal static void Forget()
        {
            _bodies.Clear();
        }

        internal static void Tick()
        {

            if (!Cfg.DownedRagdoll.Value)
            {
                if (_bodies.Count > 0) { Forget(); Log.Detail("pose: ragdoll disabled, prone crawl in use"); }
                return;
            }

            foreach (var kv in State.Peers)
            {
                var p = kv.Value;
                if (p == null) continue;

                Body b;
                _bodies.TryGetValue(p.Id, out b);
                bool isDown = b != null && b.Fallen && Alive(b);
                bool wantDown = p.Downed && p.Live;

                bool dead = false;
                try { dead = p.Health == null || p.Health.isDead; }
                catch { dead = true; }

                if (wantDown && !isDown && !dead) Fall(p);
                else if (!wantDown && isDown)
                {
                    if (dead) { _bodies.Remove(p.Id); Log.Detail("pose: " + p.Name + " died, ragdoll handed to the game"); }
                    else Rise(p, b);
                }
                else if (isDown && !dead) Tone(p, b);
            }

            List<ulong> drop = null;
            foreach (var kv in _bodies)
                if (!State.Peers.ContainsKey(kv.Key) || !Alive(kv.Value))
                    (drop ?? (drop = new List<ulong>())).Add(kv.Key);
            if (drop != null) for (int i = 0; i < drop.Count; i++) _bodies.Remove(drop[i]);
        }

        internal static void FixedTick()
        {
            foreach (var kv in _bodies)
            {
                var b = kv.Value;
                if (!b.Fallen || !b.DragTone || !Alive(b)) continue;
                var p = State.Find(kv.Key);
                if (p == null || p.Dragger == 0UL) continue;
                var dragger = State.Find(p.Dragger);
                if (dragger == null || !dragger.Live) continue;
                DragPose(p, dragger, b);
            }
        }

        static bool Alive(Body b)
        {
            try { return b.Ra2 != null && b.Ra2.gameObject != null; }
            catch { return false; }
        }

        static void Fall(Peer p)
        {
            RagdollAnimator2 ra2 = null;
            try { if (p.Root != null) ra2 = p.Root.GetComponentInChildren<RagdollAnimator2>(true); }
            catch { }
            if (ra2 == null) { Log.Detail("pose: no RagdollAnimator2 on " + p.Name); return; }

            var b = new Body { Ra2 = ra2 };
            try { b.Handler = ra2.Handler; } catch { }

            try
            {

                try
                {
                    var walker = Downed.Walker(p.Pn);
                    if (walker != null && b.Handler != null) b.Handler.User_ProvideAnchorVelocity(walker.GetVelocity());
                }
                catch { }

                Err.Risky("pose.switchtofall", () => ra2.RA2Event_SwitchToFall());
                LimpMuscles(b);
                CacheBones(b);
                b.Fallen = true;
                _bodies[p.Id] = b;
                Log.Fired("pose-fall");
                Log.Msg("pose: " + p.Name + " is on the ground");
            }
            catch (Exception e) { Log.Detail("pose fall failed for " + p.Name + ": " + e.Message); }
        }

        static void Rise(Peer p, Body b)
        {
            try
            {
                SetChainMuscles(b, 1f, 1f, 1f);
                var o = Owner(b);
                if (o != null) RagdollHandlerUtilities.User_FadeMusclesPower(o, 1f, 0.3f, 0f, false);
                b.Ra2.RA2Event_TransitionStand(0.7f);
                Log.Fired("pose-rise");
                Log.Msg("pose: " + p.Name + " is getting up");
            }
            catch (Exception e) { Log.Detail("pose rise failed for " + p.Name + ": " + e.Message); }
            _bodies.Remove(p.Id);
        }

        static void Tone(Peer p, Body b)
        {
            bool dragged = p.Dragger != 0UL;
            if (dragged == b.DragTone) return;
            b.DragTone = dragged;
            if (dragged) SeatedMuscles(b);
            else LimpMuscles(b);
        }

        static void LimpMuscles(Body b)
        {
            SetChainMuscles(b, core: 0.12f, legs: 0f, arms: 0.05f);
        }

        static void SeatedMuscles(Body b)
        {
            SetChainMuscles(b, core: 0.8f, legs: 0f, arms: 0.1f);
        }

        static void SetChainMuscles(Body b, float core, float legs, float arms)
        {
            if (b.Handler == null) return;
            try
            {
                SetChain(b.Handler, Il2Cpp.ERagdollChainType.Core, core);
                SetChain(b.Handler, Il2Cpp.ERagdollChainType.LeftLeg, legs);
                SetChain(b.Handler, Il2Cpp.ERagdollChainType.RightLeg, legs);
                SetChain(b.Handler, Il2Cpp.ERagdollChainType.LeftArm, arms);
                SetChain(b.Handler, Il2Cpp.ERagdollChainType.RightArm, arms);
                b.Handler.User_UpdateJointsPlayParameters(false);
            }
            catch (Exception e) { Log.Detail("pose muscle split failed: " + e.Message); }
        }

        static void SetChain(RagdollHandler h, Il2Cpp.ERagdollChainType t, float force)
        {
            try
            {
                var chain = h.GetChain(t);
                if (chain != null) chain.MusclesForce = force;
            }
            catch { }
        }

        static void CacheBones(Body b)
        {
            var o = Owner(b);
            if (o == null) return;
            b.Chest = BoneRb(o, Il2Cpp.ERagdollBoneID.Chest);
            if (b.Chest == null) b.Chest = BoneRb(o, Il2Cpp.ERagdollBoneID.UpperChest);
            if (b.Chest == null) b.Chest = BoneRb(o, Il2Cpp.ERagdollBoneID.Spine);
            b.Shoulder = BoneRb(o, Il2Cpp.ERagdollBoneID.RightUpperArm);
            if (b.Shoulder == null) b.Shoulder = BoneRb(o, Il2Cpp.ERagdollBoneID.LeftUpperArm);
            b.Hips = BoneRb(o, Il2Cpp.ERagdollBoneID.Hips);
        }

        static Rigidbody BoneRb(IRagdollAnimator2HandlerOwner o, Il2Cpp.ERagdollBoneID id)
        {
            try
            {
                var bone = RagdollHandlerUtilities.User_GetBoneSetupByBoneID(o, id);
                return bone != null ? bone.GameRigidbody : null;
            }
            catch { return null; }
        }

        static void DragPose(Peer victim, Peer dragger, Body b)
        {
            try
            {
                Vector3 vPos = victim.Position;
                Vector3 dPos = dragger.Position;
                Vector3 toDragger = dPos - vPos;
                toDragger.y = 0f;
                if (toDragger.sqrMagnitude < 0.01f) return;
                Vector3 dragDir = toDragger.normalized;

                Vector3 hand = dPos - dragDir * 0.35f + Vector3.up * 0.75f;

                if (b.Shoulder != null)
                    RagdollHandlerUtilities.DragRigidbodyTowards(b.Shoulder, hand, 6f);

                if (b.Chest != null && b.Hips != null)
                {
                    Vector3 slant = Vector3.Slerp(Vector3.up, dragDir, 0.42f).normalized;
                    Vector3 chestTarget = b.Hips.worldCenterOfMass + slant * 0.55f;
                    RagdollHandlerUtilities.DragRigidbodyTowards(b.Chest, chestTarget, 4f);
                }
            }
            catch { }
        }
    }
}
