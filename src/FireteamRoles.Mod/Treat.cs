// ODG Operations - FireteamRoles
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using UnityEngine;

namespace FireteamRoles
{

    internal static class Treat
    {
        internal static ulong TargetId;
        internal static bool Open { get { return TargetId != 0UL; } }
        internal static Peer Target { get { return State.Find(TargetId); } }

        static int _seq;
        static int _pendingSeq;
        static ulong _pendingVictim;
        static Il2Cpp.InventoryItem.ItemType _pendingType;
        static float _pendingUntil;
        internal static string LastResult = "";

        static readonly Net.SeqGate _orders = new Net.SeqGate();

        internal static float ReviveHold;
        static int _reviveSeq;
        static readonly Net.SeqGate _revives = new Net.SeqGate();

        internal static bool Busy { get { return _pendingSeq != 0; } }

        internal static void OpenOn(Peer p)
        {
            if (p == null) return;
            TargetId = p.Id;
            ReviveHold = 0f;
            LastResult = "";

            Ui.ItemIndex = 0;
            Ui.LimbIndex = 0;
            Err.Crumb("treat-open:" + p.Name);
            Log.Msg("treating " + p.Name + ": " + Wounds.OpenSummary(p.Health));
        }

        internal static void Close()
        {
            TargetId = 0UL;
            ReviveHold = 0f;
        }

        internal static void Tick()
        {

            if (_pendingSeq != 0 && Time.realtimeSinceStartup > _pendingUntil) TimeOutPending();

            if (!Open) return;
            var t = Target;
            var me = State.Me;

            if (t == null || !t.Live || !t.Downed) { Close(); return; }
            if (me == null || !me.Live || me.Downed) { Close(); return; }
            if (Vector3.Distance(me.Position, t.Position) > Cfg.TreatRangeMeters.Value * 1.6f) { Close(); return; }
        }

        static void TimeOutPending()
        {
            if (_pendingIsEpi) Epi.Refund();
            else Medical.Refund(Medical.Local(), _pendingType);
            LastResult = "no response, item returned";
            Log.Detail("treat order " + _pendingSeq + " timed out, refunded");
            _pendingSeq = 0;
            _pendingVictim = 0UL;
            _pendingIsEpi = false;
        }

        internal const int EpiIndex = 8;
        internal const int EpiWireType = -1;

        internal static bool CanApply(int itemIndex)
        {
            if (!Open || Busy) return false;
            if (itemIndex == EpiIndex) return Medical.EpiCount > 0;
            return Medical.CachedUses(itemIndex) > 0;
        }

        internal static void ApplyIndex(int itemIndex, Il2Cpp.HealthBodyPart limb)
        {
            if (itemIndex == EpiIndex) ApplyEpi();
            else Apply(Medical.AllTypes[Mathf.Clamp(itemIndex, 0, Medical.AllTypes.Length - 1)], limb);
        }

        static void ApplyEpi()
        {
            var t = Target;
            if (t == null || Busy) return;
            if (!Epi.Spend()) { LastResult = "no vials left"; return; }

            _seq++;
            _pendingSeq = _seq;
            _pendingVictim = t.Id;
            _pendingType = Il2Cpp.InventoryItem.ItemType.Empty;
            _pendingIsEpi = true;
            _pendingUntil = Time.realtimeSinceStartup + 5f;
            LastResult = "injecting epinephrine...";

            Net.SetSelf(Net.K_MED, Net.Join(_seq, t.Id, EpiWireType, (int)limbForEpi));
        }

        const Il2Cpp.HealthBodyPart limbForEpi = Il2Cpp.HealthBodyPart.Chest;
        static bool _pendingIsEpi;

        internal static void Apply(Il2Cpp.InventoryItem.ItemType type, Il2Cpp.HealthBodyPart limb)
        {
            var t = Target;
            if (t == null || Busy) return;

            var med = Medical.Local();
            if (!Medical.Spend(med, type)) { LastResult = "none left"; return; }

            _seq++;
            _pendingSeq = _seq;
            _pendingVictim = t.Id;
            _pendingType = type;
            _pendingUntil = Time.realtimeSinceStartup + 5f;
            LastResult = "applying " + Medical.Name(type) + "...";

            Net.SetSelf(Net.K_MED, Net.Join(_seq, t.Id, (int)type, (int)limb));
        }

        internal static void PumpNetwork()
        {
            ReceiveOrder();
            ReceiveAck();
            ReceiveRevive();
        }

        static void ReceiveOrder()
        {
            var members = Net.Members();
            for (int i = 0; i < members.Count; i++)
            {
                ulong from = members[i];
                if (from == Net.Self) continue;
                var f = Net.Split(Net.Get(from, Net.K_MED));
                if (f.Length < 4) continue;

                int seq = Net.Int(f[0]);
                ulong victim = Net.ULong(f[1]);
                if (victim != Net.Self || seq == 0) continue;
                if (!_orders.IsNew(from, seq)) continue;

                int typeInt = Net.Int(f[2], -99);
                var limb = (Il2Cpp.HealthBodyPart)Net.Int(f[3]);

                int result = 0;
                try
                {
                    var med = Medical.Local();
                    if (med == null) { }
                    else if (typeInt == EpiWireType)
                    {

                        Epi.Apply(med);
                        Downed.EpiReceived++;
                        result = 1;
                        Log.Fired("epi-received");
                    }
                    else if (Medical.ApplyToSelf(med, (Il2Cpp.InventoryItem.ItemType)typeInt, limb)) result = 1;
                }
                catch (Exception e) { Log.Detail("treat apply failed: " + e.Message); }

                PublishAck(from, seq, result);
                if (result == 1)
                {
                    Ui.Toast(typeInt == EpiWireType
                        ? State.NameOf(from) + " hit you with epinephrine"
                        : State.NameOf(from) + " treated your " + Wounds.Label(limb).ToLowerInvariant());
                    Log.Fired("treat-receive");
                }
            }
        }

        static readonly Dictionary<ulong, string> _acks = new Dictionary<ulong, string>();

        static void PublishAck(ulong requester, int seq, int result)
        {
            _acks[requester] = requester + ":" + seq + ":" + result;
            var sb = new System.Text.StringBuilder();
            foreach (var kv in _acks)
            {
                if (sb.Length > 0) sb.Append(';');
                sb.Append(kv.Value);
            }
            Net.SetSelf(Net.K_MED_ACK, sb.ToString());
        }

        static void ReceiveAck()
        {
            if (_pendingSeq == 0) return;
            var raw = Net.Get(_pendingVictim, Net.K_MED_ACK);
            if (string.IsNullOrEmpty(raw)) return;

            var rows = raw.Split(';');
            for (int i = 0; i < rows.Length; i++)
            {
                var f = rows[i].Split(':');
                if (f.Length < 3) continue;
                if (Net.ULong(f[0]) != Net.Self) continue;
                if (Net.Int(f[1]) != _pendingSeq) continue;

                int result = Net.Int(f[2]);
                if (result == 1) LastResult = (_pendingIsEpi ? Epi.ItemName : Medical.Name(_pendingType)) + " applied";
                else
                {
                    if (_pendingIsEpi) Epi.Refund();
                    else Medical.Refund(Medical.Local(), _pendingType);
                    LastResult = "it did not take, item returned";
                }
                _pendingSeq = 0;
                _pendingIsEpi = false;
                return;
            }
        }

        internal static bool NeedsEpi(Peer t)
        {
            if (t == null) return false;
            float threshold = Cfg.EpiRequiredBelowHeartRate.Value;
            if (threshold <= 0f) return false;
            if (t.Hr <= 0f) return false;
            return t.Hr < threshold && t.EpiReceived <= 0;
        }

        internal static bool ReviveAllowed(Peer t)
        {
            if (t == null || !t.Downed) return false;
            if (NeedsEpi(t)) return false;
            if (!Cfg.ReviveRequiresStabilization.Value) return true;
            return Wounds.IsClean(t.Health);
        }

        internal static void HoldRevive(float dt)
        {
            var t = Target;
            if (t == null) { ReviveHold = 0f; return; }

            if (!ReviveAllowed(t))
            {
                ReviveHold = 0f;
                LastResult = NeedsEpi(t)
                    ? "CANNOT REVIVE - pulse " + t.Hr.ToString("0") + ", they need epinephrine"
                    : "CANNOT REVIVE - " + Wounds.OpenSummary(t.Health);
                return;
            }

            ReviveHold += dt;
            if (ReviveHold < Mathf.Max(0.5f, Cfg.ReviveHoldSeconds.Value)) return;

            ReviveHold = 0f;
            _reviveSeq++;
            Net.SetSelf(Net.K_REVIVE, Net.Join(_reviveSeq, t.Id));
            LastResult = "reviving...";
            Log.Msg("revive requested for " + t.Name);
            Close();
        }

        internal static void ReleaseRevive()
        {
            ReviveHold = 0f;
        }

        static void ReceiveRevive()
        {
            if (!State.IsHost) return;
            var members = Net.Members();
            for (int i = 0; i < members.Count; i++)
            {
                ulong from = members[i];
                var f = Net.Split(Net.Get(from, Net.K_REVIVE));
                if (f.Length < 2) continue;
                int seq = Net.Int(f[0]);
                ulong victim = Net.ULong(f[1]);
                if (seq == 0 || victim == 0UL) continue;

                if (!_revives.IsNew(from, seq)) continue;

                Downed.HostRevive(victim, from);
                Log.Fired("revive-receive");
            }
        }
    }
}
