// ODG Operations - FireteamRoles
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using UnityEngine;

namespace FireteamRoles
{

    internal static class Jtac
    {
        static readonly Dictionary<ulong, int> _left = new Dictionary<ulong, int>();
        static int _stock = -1;
        static ulong _requester;
        static float _requestAt;
        static ulong _firing;
        static string _published = "";
        static float _nextPublish;

        internal static int MyStrikes = -1;

        internal static void Reset(string why)
        {
            _left.Clear();
            _stock = -1;
            _requester = 0UL; _firing = 0UL;
            MyStrikes = -1;
            _published = "";
            try { if (State.IsHost) Net.SetSelf(Net.K_STRIKES, ""); } catch { }
            Log.Detail("jtac budgets cleared (" + why + ")");
        }

        internal static Il2Cpp.ISR ActiveIsr()
        {
            try
            {
                var gm = Il2Cpp.GameManager.instance;
                return gm != null ? gm.activeISR : null;
            }
            catch { return null; }
        }

        internal static void OnFireRequest(Il2Cpp.PlayerNetworking pn)
        {
            if (!State.IsHost || pn == null) return;
            ulong who = 0UL;
            try
            {
                foreach (var kv in State.Peers)
                {
                    var p = kv.Value;
                    if (p != null && p.Pn != null && p.Pn.Pointer == pn.Pointer) { who = p.Id; break; }
                }
            }
            catch { }
            _requester = who;
            _requestAt = Time.realtimeSinceStartup;
            Log.Detail("fire button from " + (who == 0UL ? "unknown player" : State.NameOf(who)));
            Log.Fired("fire-request");
        }

        static bool IsJtac(ulong id)
        {
            if (id == Net.Self) return State.MyRole == Role.Jtac;
            var p = State.Find(id);
            return p != null && p.Role == Role.Jtac;
        }

        static int Budget(ulong id)
        {
            return IsJtac(id) ? Mathf.Max(1, Cfg.JtacMissiles.Value) : Mathf.Max(0, _stock);
        }

        internal static void BeforeFire(Il2Cpp.ISR isr)
        {
            if (!State.IsHost || isr == null || !State.Enabled) return;
            int cur = 0;
            try { cur = isr.MissileCount; } catch { return; }
            if (_stock < 0)
            {
                _stock = Mathf.Max(0, cur);
                Log.Msg("ISR shipped with " + _stock + " Hellfire(s); a JTAC gets " + Cfg.JtacMissiles.Value);
            }

            bool recent = Time.realtimeSinceStartup - _requestAt < 2f && _requester != 0UL;
            ulong who = recent ? _requester : Net.Self;
            int left;
            if (!_left.TryGetValue(who, out left)) { left = Budget(who); _left[who] = left; }
            try { isr.MissileCount = left; } catch { }
            _firing = who;
            Log.Detail("strike attempt by " + State.NameOf(who) + " with " + left + " left" + (recent ? "" : " (no recent request, assumed the host)"));
        }

        internal static void AfterFire(Il2Cpp.ISR isr)
        {
            if (!State.IsHost || isr == null || _firing == 0UL) return;
            int after = 0;
            try { after = Mathf.Max(0, isr.MissileCount); } catch { }
            int before; _left.TryGetValue(_firing, out before);
            _left[_firing] = after;
            if (after < before) { Log.Msg("HELLFIRE by " + State.NameOf(_firing) + ": " + after + " left"); Log.Fired("strike"); }
            else Log.Detail("strike by " + State.NameOf(_firing) + " did not fire (cooldown or empty), " + after + " left");
            _firing = 0UL;
            _nextPublish = 0f;
        }

        internal static void Tick()
        {
            if (!State.Enabled) return;
            float now = Time.realtimeSinceStartup;
            if (State.IsHost)
            {
                if (now < _nextPublish) return;
                _nextPublish = now + 1.5f;
                var sb = new System.Text.StringBuilder();
                foreach (var kv in _left)
                {
                    if (sb.Length > 0) sb.Append(';');
                    sb.Append(kv.Key).Append(':').Append(kv.Value);
                }

                if (State.MyRole == Role.Jtac && !_left.ContainsKey(Net.Self))
                { if (sb.Length > 0) sb.Append(';'); sb.Append(Net.Self).Append(':').Append(Cfg.JtacMissiles.Value); }
                string s = sb.ToString();
                if (s != _published) { _published = s; Net.SetSelf(Net.K_STRIKES, s); }
                int mine; MyStrikes = _left.TryGetValue(Net.Self, out mine) ? mine : (State.MyRole == Role.Jtac ? Cfg.JtacMissiles.Value : -1);
                return;
            }

            if (now < _nextPublish) return;
            _nextPublish = now + 1f;
            try
            {
                ulong host = Net.HostId;
                if (host == 0UL) return;
                string raw = Net.Get(host, Net.K_STRIKES);
                if (string.IsNullOrEmpty(raw)) { if (State.MyRole == Role.Jtac && MyStrikes < 0) MyStrikes = Cfg.JtacMissiles.Value; return; }
                var rows = raw.Split(';');
                for (int i = 0; i < rows.Length; i++)
                {
                    var f = rows[i].Split(':');
                    if (f.Length < 2) continue;
                    if (Net.ULong(f[0]) != Net.Self) continue;
                    MyStrikes = Net.Int(f[1]);
                    return;
                }
                if (State.MyRole == Role.Jtac && MyStrikes < 0) MyStrikes = Cfg.JtacMissiles.Value;
            }
            catch { }
        }
    }
}
