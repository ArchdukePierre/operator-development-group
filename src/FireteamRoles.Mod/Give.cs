// ODG Operations - FireteamRoles
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using UnityEngine;

namespace FireteamRoles
{

    internal static class Give
    {
        static int _seq;
        static int _pendingSeq;
        static ulong _pendingTo;
        static Il2Cpp.InventoryItem.ItemType _pendingType;
        static bool _pendingIsEpi;
        static float _pendingUntil;

        static readonly Net.SeqGate _gifts = new Net.SeqGate();
        static readonly System.Collections.Generic.Dictionary<ulong, string> _acks =
            new System.Collections.Generic.Dictionary<ulong, string>();

        internal static bool Busy { get { return _pendingSeq != 0; } }

        internal static bool CanGive(Peer target)
        {
            if (State.MyRole != Role.Medic || !State.Enabled) return false;
            if (target == null || target.Id == Net.Self) return false;
            if (!target.HasMod || !target.Live) return false;
            if (Busy) return false;
            return true;
        }

        internal static void Send(Peer target, Il2Cpp.InventoryItem.ItemType type, bool epi)
        {
            if (!CanGive(target)) return;

            var med = Medical.Local();
            bool spent = epi ? Epi.Spend() : Medical.Spend(med, type);
            if (!spent) { Ui.Toast("none left to give"); return; }

            _seq++;
            _pendingSeq = _seq;
            _pendingTo = target.Id;
            _pendingType = type;
            _pendingIsEpi = epi;
            _pendingUntil = Time.realtimeSinceStartup + 5f;

            Net.SetSelf(Net.K_GIFT, Net.Join(_seq, target.Id, epi ? -1 : (int)type, 1));
            Ui.Toast("gave " + (epi ? Epi.ItemName : Medical.Name(type)) + " to " + target.Name);
        }

        internal static void Pump()
        {
            ReceiveGift();
            ReceiveAck();

            if (_pendingSeq != 0 && Time.realtimeSinceStartup > _pendingUntil)
            {
                Log.Msg("gift " + _pendingSeq + " timed out, refunding");
                Refund();
            }
        }

        static void Refund()
        {
            if (_pendingIsEpi) {  }
            else Medical.Refund(Medical.Local(), _pendingType);
            Ui.Toast("hand-off failed, item returned");
            Clear();
        }

        static void Clear()
        {
            _pendingSeq = 0; _pendingTo = 0UL; _pendingUntil = 0f; _pendingIsEpi = false;
        }

        static void ReceiveGift()
        {
            var members = Net.Members();
            for (int i = 0; i < members.Count; i++)
            {
                ulong from = members[i];
                if (from == Net.Self) continue;
                var f = Net.Split(Net.Get(from, Net.K_GIFT));
                if (f.Length < 4) continue;

                int seq = Net.Int(f[0]);
                ulong to = Net.ULong(f[1]);
                int typeInt = Net.Int(f[2], -99);
                if (to != Net.Self || seq == 0) continue;
                if (!_gifts.IsNew(from, seq)) continue;

                int applied = 0;
                var med = Medical.Local();
                try
                {
                    if (typeInt == -1)
                    {

                        Epi.Sync(med);
                        applied = 1;
                    }
                    else
                    {

                        var type = (Il2Cpp.InventoryItem.ItemType)typeInt;
                        if (Medical.Grant(med, type, 1)) applied = 1;
                    }
                }
                catch (Exception e) { Log.Detail("gift apply failed: " + e.Message); }

                PublishAck(from, seq, applied);
                if (applied > 0)
                    Ui.Toast(State.NameOf(from) + " gave you " +
                             (typeInt == -1 ? Epi.ItemName : Medical.Name((Il2Cpp.InventoryItem.ItemType)typeInt)));
                Log.Fired("gift-receive");
            }
        }

        static void PublishAck(ulong requester, int seq, int applied)
        {
            _acks[requester] = requester + ":" + seq + ":" + applied;
            var sb = new System.Text.StringBuilder();
            foreach (var kv in _acks)
            {
                if (sb.Length > 0) sb.Append(';');
                sb.Append(kv.Value);
            }
            Net.SetSelf(Net.K_GIFT_ACK, sb.ToString());
        }

        static void ReceiveAck()
        {
            if (_pendingSeq == 0) return;
            var raw = Net.Get(_pendingTo, Net.K_GIFT_ACK);
            if (string.IsNullOrEmpty(raw)) return;

            var rows = raw.Split(';');
            for (int i = 0; i < rows.Length; i++)
            {
                var f = rows[i].Split(':');
                if (f.Length < 3) continue;
                if (Net.ULong(f[0]) != Net.Self) continue;
                if (Net.Int(f[1]) != _pendingSeq) continue;

                int applied = Net.Int(f[2]);
                if (applied <= 0) { Ui.Toast("they had no room for it"); Refund(); return; }
                Clear();
                return;
            }
        }
    }
}
