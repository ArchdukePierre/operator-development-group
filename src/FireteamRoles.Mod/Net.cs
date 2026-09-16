// ODG Operations - FireteamRoles
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;

namespace FireteamRoles
{

    internal static class Net
    {
        internal const string K_VER = "odgr_ver";
        internal const string K_ROLE = "odgr_role";
        internal const string K_GIFT = "odgr_gift";
        internal const string K_GIFT_ACK = "odgr_gift_ack";
        internal const string K_STATE = "odgr_state";
        internal const string K_VITALS = "odgr_vitals";
        internal const string K_DRAG = "odgr_drag";
        internal const string K_MED = "odgr_med";
        internal const string K_MED_ACK = "odgr_med_ack";
        internal const string K_REVIVE = "odgr_revive";
        internal const string K_BREACH = "odgr_breach";
        internal const string K_LADDER = "odgr_ladder";
        internal const string K_DRONE = "odgr_drone";
        internal const string K_STRIKES = "odgr_strikes";

        internal const string SCHEMA = "1";

        static readonly string[] AllKeys =
        {
            K_VER, K_ROLE, K_GIFT, K_GIFT_ACK, K_STATE, K_VITALS, K_DRAG, K_MED, K_MED_ACK, K_REVIVE, K_BREACH, K_LADDER
        };

        const ulong SoloId = 1UL;

        static ulong _lobby;
        static ulong _self;
        static readonly Dictionary<string, string> _local = new Dictionary<string, string>();
        static readonly Dictionary<string, string> _pushed = new Dictionary<string, string>();
        static bool _warnedNoSteam;

        internal static ulong Lobby => _lobby;
        internal static ulong Self => _self;

        internal static bool Online => _lobby != 0UL && _self != 0UL && _self != SoloId;

        internal static bool Solo => !Online;

        internal static void Reset()
        {
            _lobby = 0UL;
            _local.Clear();
            _pushed.Clear();
        }

        static readonly string[] Volatile = { K_STATE, K_VITALS, K_DRAG, K_MED, K_MED_ACK, K_GIFT, K_GIFT_ACK, K_REVIVE, K_BREACH, K_LADDER, K_DRONE, K_STRIKES };

        internal static void ClearVolatileKeys()
        {
            for (int i = 0; i < Volatile.Length; i++) SetSelf(Volatile[i], "");
        }

        internal static ulong LobbyOwner()
        {
            if (!Online) return _self;
            try
            {
                var id = Il2CppSteamworks.SteamMatchmaking.GetLobbyOwner(new Il2CppSteamworks.CSteamID(_lobby));
                return id.m_SteamID;
            }
            catch { return 0UL; }
        }

        internal static void RefreshIdentity()
        {

            if (_self == 0UL || _self == SoloId)
            {
                ulong id = 0UL;
                try { id = Il2CppSteamworks.SteamUser.GetSteamID().m_SteamID; }
                catch (Exception e)
                {
                    if (!_warnedNoSteam) { _warnedNoSteam = true; Log.Warn("steam user unavailable: " + e.Message); }
                }

                if (id == 0UL)
                {
                    try
                    {
                        var pm = Il2Cpp.PlayerMaster.MyPlayerMaster;
                        if (pm != null) id = pm.NetworkthisPlayerSteam64;
                    }
                    catch { }
                }

                if (id == 0UL) id = SoloId;
                if (id != _self)
                {
                    _self = id;
                    Log.Msg("identity: " + (_self == SoloId ? "solo (no steam id)" : _self.ToString()));
                }
            }

            ulong lobby = 0UL;
            try
            {
                var sl = Il2CppVectorGames.SteamLobby.singleton;
                if (sl != null) lobby = sl.lobbyID.m_SteamID;
            }
            catch { lobby = 0UL; }

            if (lobby != _lobby)
            {
                _lobby = lobby;
                _pushed.Clear();
                Log.Msg(_lobby == 0UL ? "lobby: none (solo)" : "lobby: " + _lobby);
                PublishIdentity();
                foreach (var kv in _local) Push(kv.Key, kv.Value);
            }
        }

        internal static void PublishIdentity()
        {
            SetSelf(K_VER, SCHEMA);
            SetSelf(K_ROLE, ((int)State.MyRole).ToString());
        }

        internal static void SetSelf(string key, string value)
        {
            if (value == null) value = "";
            _local[key] = value;
            Push(key, value);
        }

        static void Push(string key, string value)
        {
            if (!Online) return;
            string prev;
            if (_pushed.TryGetValue(key, out prev) && prev == value) return;
            try
            {
                Il2CppSteamworks.SteamMatchmaking.SetLobbyMemberData(new Il2CppSteamworks.CSteamID(_lobby), key, value);
                _pushed[key] = value;
            }
            catch (Exception e) { Log.Detail("SetLobbyMemberData(" + key + ") failed: " + e.Message); }
        }

        internal static string Get(ulong member, string key)
        {

            if (member == _self)
            {
                string v;
                return _local.TryGetValue(key, out v) ? v : "";
            }
            if (!Online || member == 0UL) return "";
            try
            {
                var s = Il2CppSteamworks.SteamMatchmaking.GetLobbyMemberData(
                    new Il2CppSteamworks.CSteamID(_lobby), new Il2CppSteamworks.CSteamID(member), key);
                return s ?? "";
            }
            catch { return ""; }
        }

        internal static string PersonaName(ulong id)
        {
            if (id == 0UL) return "";
            try
            {
                if (id == _self) return Il2CppSteamworks.SteamFriends.GetPersonaName() ?? "";
                return Il2CppSteamworks.SteamFriends.GetFriendPersonaName(new Il2CppSteamworks.CSteamID(id)) ?? "";
            }
            catch { return ""; }
        }

        internal static ulong HostId
        {
            get
            {
                try
                {
                    if (_lobby == 0UL) return 0UL;
                    return Il2CppSteamworks.SteamMatchmaking.GetLobbyOwner(new Il2CppSteamworks.CSteamID(_lobby)).m_SteamID;
                }
                catch { return 0UL; }
            }
        }

        internal static List<ulong> Members()
        {
            var outp = new List<ulong>();
            if (!Online) { if (_self != 0UL) outp.Add(_self); return outp; }
            try
            {
                var lob = new Il2CppSteamworks.CSteamID(_lobby);
                int n = Il2CppSteamworks.SteamMatchmaking.GetNumLobbyMembers(lob);
                for (int i = 0; i < n; i++)
                {
                    var id = Il2CppSteamworks.SteamMatchmaking.GetLobbyMemberByIndex(lob, i).m_SteamID;
                    if (id != 0UL) outp.Add(id);
                }
            }
            catch (Exception e) { Log.Detail("Members() failed: " + e.Message); }
            if (outp.Count == 0 && _self != 0UL) outp.Add(_self);
            return outp;
        }

        internal static void Poll(Action<ulong, string, string> onValue)
        {
            var members = Members();
            for (int i = 0; i < members.Count; i++)
            {
                ulong id = members[i];
                for (int k = 0; k < AllKeys.Length; k++)
                    onValue(id, AllKeys[k], Get(id, AllKeys[k]));
            }
        }

        internal static string Join(params object[] parts)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) sb.Append('|');
                sb.Append(parts[i]);
            }
            return sb.ToString();
        }

        internal static string[] Split(string s)
        {
            if (string.IsNullOrEmpty(s)) return new string[0];
            return s.Split('|');
        }

        internal static ulong ULong(string s)
        {
            ulong v;
            return ulong.TryParse(s, out v) ? v : 0UL;
        }

        internal static int Int(string s, int fallback = 0)
        {
            int v;
            return int.TryParse(s, out v) ? v : fallback;
        }

        internal static float Float(string s, float fallback = 0f)
        {
            float v;
            return float.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        internal class SeqGate
        {
            readonly Dictionary<ulong, int> _seen = new Dictionary<ulong, int>();

            internal bool IsNew(ulong from, int seq)
            {
                if (seq <= 0) return false;
                int last;
                if (_seen.TryGetValue(from, out last) && seq <= last) return false;
                _seen[from] = seq;
                return true;
            }

            internal void Clear() { _seen.Clear(); }
        }

        internal static string F(float v)
        {
            return v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
