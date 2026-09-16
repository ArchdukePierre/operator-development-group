// ODG Operations - FireteamRoles
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using UnityEngine;
using Il2CppRootMotion.FinalIK;

namespace FireteamRoles
{

    internal static class Drag
    {
        static int _seq;
        internal static ulong Victim;
        static float _holdAt;
        static bool _held;
        static FullBodyBipedEffector _effector = FullBodyBipedEffector.RightHand;

        static readonly Dictionary<ulong, ulong> _cosmetic = new Dictionary<ulong, ulong>();

        static readonly Dictionary<ulong, FullBodyBipedEffector> _holdHand =
            new Dictionary<ulong, FullBodyBipedEffector>();

        internal static bool Dragging { get { return Victim != 0UL; } }

        internal static void Toggle(Peer target)
        {
            if (Dragging) { Stop("released"); return; }

            if (target == null) { Log.Detail("grab: nothing in range"); return; }
            if (!target.Downed) { Log.Detail("grab: " + target.Name + " is not down"); return; }
            if (!target.Live) { Log.Detail("grab: " + target.Name + " has no live avatar resolved"); return; }
            if (target.Dragger != 0UL && target.Dragger != Net.Self)
            {
                Log.Detail("grab: " + target.Name + " already held by " + State.NameOf(target.Dragger));
                Ui.Toast("someone already has them");
                return;
            }

            var me = State.Me;
            if (me == null) { Log.Detail("grab: local player unresolved"); return; }
            if (!me.Live) { Log.Detail("grab: local avatar unresolved"); return; }
            if (me.Downed) { Log.Detail("grab: you are down yourself"); return; }

            Victim = target.Id;
            _seq++;
            _held = false;
            _holdAt = Time.realtimeSinceStartup + 0.35f;

            try
            {
                bool left = SideIsLeft(me, target);
                _effector = left ? FullBodyBipedEffector.LeftHand : FullBodyBipedEffector.RightHand;
                if (left) me.Pn.CMD_ShoulderTapL(target.Pn, 0);
                else me.Pn.CMD_ShoulderTapR(target.Pn, 0);
                Log.Fired("shoulder-tap-send");
            }
            catch (Exception e) { Log.Detail("shoulder tap send failed: " + e.Message); }

            BeginHold(me, target, _effector);
            PublishHeld(target.Id);
            Ui.Toast("dragging " + target.Name);
            Log.Msg("grab: " + target.Name + " (published claim, waiting for the host to confirm)");
            Log.Fired("drag-start-send");
        }

        internal static void Stop(string why)
        {
            if (!Dragging) return;
            var me = State.Me;
            var target = State.Find(Victim);
            EndHold(me);
            try
            {
                var walker = Downed.Walker(target != null ? target.Pn : null);
                if (walker != null) walker.SetMomentum(Vector3.zero);
            }
            catch { }

            PublishHeld(0UL);
            Log.Msg("release (" + why + ")");
            Victim = 0UL;
            _held = false;
        }

        static bool SideIsLeft(Peer me, Peer target)
        {
            try
            {
                var toTarget = target.Position - me.Position;
                var right = me.Root.right;
                return Vector3.Dot(toTarget, right) < 0f;
            }
            catch { return false; }
        }

        static InteractionSystem SystemOf(Peer p)
        {
            if (p == null || p.Root == null) return null;
            try { return p.Root.GetComponentInChildren<InteractionSystem>(true); }
            catch { return null; }
        }

        static InteractionObject ShoulderOf(Peer p, bool left)
        {
            if (p == null || p.Pn == null) return null;
            try { return left ? p.Pn.leftShoulder : p.Pn.rightShoulder; }
            catch { return null; }
        }

        static void BeginHold(Peer holder, Peer target, FullBodyBipedEffector eff)
        {
            var sys = SystemOf(holder);
            var shoulder = ShoulderOf(target, eff == FullBodyBipedEffector.LeftHand);
            if (sys == null || shoulder == null) { Log.Detail("hold: no interaction system or shoulder object"); return; }
            try
            {
                sys.StartInteraction(eff, shoulder, true);
                if (holder != null) _holdHand[holder.Id] = eff;
                Log.Fired("finalik-start");
            }
            catch (Exception e) { Log.Detail("StartInteraction failed: " + e.Message); }
        }

        static void EndHold(Peer holder)
        {
            if (holder == null) return;
            var sys = SystemOf(holder);

            FullBodyBipedEffector eff;
            if (!_holdHand.TryGetValue(holder.Id, out eff)) eff = FullBodyBipedEffector.RightHand;
            _holdHand.Remove(holder.Id);

            if (sys == null) return;
            try { sys.ResumeInteraction(eff); } catch { }
            try { sys.StopInteraction(eff); } catch { }
        }

        internal static void Tick()
        {
            if (!Dragging) return;

            var me = State.Me;
            var target = State.Find(Victim);

            if (me == null || !me.Live || me.Downed) { Stop("dragger down"); return; }
            if (target == null || !target.Live || !target.Downed) { Stop("casualty no longer down"); return; }

            float dist = Vector3.Distance(me.Position, target.Position);
            if (dist > Mathf.Max(2f, Cfg.DragBreakDistance.Value)) { Stop("too far"); return; }

            if (!_held && Time.realtimeSinceStartup >= _holdAt)
            {
                _held = true;
                var sys = SystemOf(me);
                FullBodyBipedEffector eff;
                if (!_holdHand.TryGetValue(me.Id, out eff)) eff = _effector;
                if (sys != null) { try { sys.PauseInteraction(eff); Log.Fired("finalik-pause"); } catch { } }
            }

            try
            {
                var walker = Downed.Walker(me.Pn);
                if (walker != null) walker.movementSpeed = Mathf.Max(0.2f, Cfg.DragSpeed.Value);
            }
            catch { }
        }

        internal static void VictimTick()
        {
            var me = State.Me;
            if (me == null || !me.Downed || me.Dragger == 0UL) { _wasDragged = false; return; }

            var dragger = State.Find(me.Dragger);
            if (dragger == null || !dragger.Live) return;

            if (!_wasDragged)
            {
                _wasDragged = true;
                Log.Msg("being dragged by " + dragger.Name);
                Ui.Toast(dragger.Name + " is pulling you");
            }

            try
            {
                var root = me.Root;
                if (root == null) return;

                Vector3 mePos = root.position;
                Vector3 dPos = dragger.Position;
                Vector3 d = mePos - dPos;
                d.y = 0f;
                float dist = d.magnitude;

                const float leash = 0.85f;
                if (dist <= leash) return;

                Vector3 dir = d.normalized;
                Vector3 target = dPos + dir * leash;
                Vector3 delta = target - mePos;
                delta.y = 0f;

                float step = Mathf.Min(delta.magnitude,
                                       Mathf.Max(0.2f, Cfg.DragSpeed.Value * 2f) * Time.deltaTime);
                if (step <= 0.0005f) return;
                Vector3 move = delta.normalized;

                if (!Blocked(root, dragger, mePos, move, step))
                {
                    root.position = mePos + move * step;
                    if (!_moveLogged) { _moveLogged = true; Log.Fired("drag-victim-move"); }
                }
            }
            catch (Exception e) { Log.Detail("drag victim move failed: " + e.Message); }
        }

        static bool _wasDragged;
        static bool _moveLogged;

        static bool Blocked(Transform root, Peer dragger, Vector3 from, Vector3 dir, float step)
        {
            try
            {
                var origin = from + Vector3.up * 0.45f;
                var hits = Physics.SphereCastAll(origin, 0.28f, dir, step + 0.12f, ~0,
                                                 QueryTriggerInteraction.Ignore);
                if (hits == null) return false;
                for (int i = 0; i < hits.Length; i++)
                {
                    var t = hits[i].transform;
                    if (t == null) continue;
                    var r = t.root;
                    if (r == null) continue;
                    if (r.Pointer == root.Pointer) continue;
                    if (dragger != null && dragger.Root != null && r.Pointer == dragger.Root.Pointer) continue;
                    return true;
                }
            }
            catch { }
            return false;
        }

        static void PublishHeld(ulong victim)
        {
            _seq++;
            Net.SetSelf(Net.K_DRAG, Net.Join(_seq, "held", victim));
        }

        internal static void ConsumeNetwork()
        {
            var members = Net.Members();

            var claims = new Dictionary<ulong, ulong>();
            for (int i = 0; i < members.Count; i++)
            {
                ulong from = members[i];
                var f = Net.Split(Net.Get(from, Net.K_DRAG));
                if (f.Length < 3) continue;
                ulong victim = Net.ULong(f[2]);
                if (victim == 0UL) continue;
                if (!claims.ContainsKey(victim)) claims[victim] = from;
            }

            if (State.IsHost)
            {
                foreach (var kv in State.Peers)
                {
                    var p = kv.Value;
                    if (p == null || !p.Downed) continue;
                    ulong holder;
                    Downed.HostSetDragger(p.Id, claims.TryGetValue(p.Id, out holder) ? holder : 0UL);
                }
            }

            for (int i = 0; i < members.Count; i++)
            {
                ulong from = members[i];
                if (from == Net.Self) continue;

                ulong nowHolding = 0UL;
                foreach (var kv in claims) if (kv.Value == from) { nowHolding = kv.Key; break; }

                ulong was;
                bool have = _cosmetic.TryGetValue(from, out was);
                if (nowHolding != 0UL && (!have || was != nowHolding))
                {
                    if (have) EndHold(State.Find(from));
                    _cosmetic[from] = nowHolding;
                    var holder = State.Find(from);
                    var tgt = State.Find(nowHolding);
                    if (holder != null && tgt != null) BeginHold(holder, tgt, FullBodyBipedEffector.RightHand);
                }
                else if (nowHolding == 0UL && have)
                {
                    _cosmetic.Remove(from);
                    EndHold(State.Find(from));
                }
            }
        }
    }
}
