// ODG Operations - FireteamRoles
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using UnityEngine;

namespace FireteamRoles
{

    internal class Peer
    {
        internal ulong Id;
        internal string Name = "";
        internal Role Role = Role.None;
        internal bool HasMod;

        internal bool Downed;
        internal float Remaining;
        internal int Revives = 1;
        internal ulong Dragger;

        internal float Hr;
        internal float Pain;
        internal int TqMask;
        internal int SelfHeals;
        internal int EpiReceived;

        internal Il2Cpp.PlayerMaster Pm;
        internal Il2Cpp.PlayerNetworking Pn;
        internal Il2Cpp.Health Health;
        internal Il2Cpp.MedicalSystem Med;
        internal Transform Root;

        internal bool Live
        {
            get
            {
                try { return Pn != null && Root != null && Health != null; }
                catch { return false; }
            }
        }

        internal Vector3 Position
        {
            get { try { return Root != null ? Root.position : Vector3.zero; } catch { return Vector3.zero; } }
        }
    }

    internal static class State
    {
        internal static readonly Dictionary<ulong, Peer> Peers = new Dictionary<ulong, Peer>();

        static Role _myRole = Role.None;
        internal static bool RolesLocked;
        internal static bool IsPvp;
        internal static bool InMission;

        internal static Role MyRole
        {
            get { return _myRole; }
            set
            {
                if (_myRole == value) return;
                _myRole = value;
                try { Cfg.LastRole.Value = (int)value; MelonLoader.MelonPreferences.Save(); } catch { }
                Net.SetSelf(Net.K_ROLE, ((int)value).ToString());
                Log.Msg("role set to " + RoleInfo.Name(value));
            }
        }

        internal static bool Enabled
        {
            get { return !IsPvp || Cfg.AllowInPvp.Value; }
        }

        static void DecidePvp()
        {
            bool verdict = false;
            string why = "no game mode running";

            if (Cfg.AssumeCoop.Value)
            {
                verdict = false; why = "AssumeCoop is on";
                _pvpChecked = true;
            }
            else
            {

                bool haveSingleton = false;
                try
                {
                    var gm = Il2Cpp.GameMode.singleton;
                    if (gm != null)
                    {
                        haveSingleton = true;
                        bool pvp = gm.TryCast<Il2Cpp.PvpGameode>() != null;
                        bool ffa = gm.TryCast<Il2Cpp.FFA>() != null;
                        verdict = pvp || ffa;
                        why = "GameMode.singleton is " + gm.GetIl2CppType().Name + (verdict ? " (pvp)" : " (co-op)");
                    }
                }
                catch (Exception e) { why = "singleton check threw: " + e.Message; }

                if (!haveSingleton)
                {
                    bool settled = UnityEngine.Time.realtimeSinceStartup >= _pvpSettleUntil;
                    if (settled)
                    {
                        try
                        {
                            var p = UnityEngine.Object.FindObjectOfType<Il2Cpp.PvpGameode>();
                            var f = UnityEngine.Object.FindObjectOfType<Il2Cpp.FFA>();
                            bool pa = p != null && p.isActiveAndEnabled;
                            bool fa = f != null && f.isActiveAndEnabled;
                            verdict = pa || fa;
                            why = verdict
                                ? "active mode object after settle: " + (pa ? p.name : f.name)
                                : "no active pvp object after settle";
                        }
                        catch (Exception e) { why = "object check threw: " + e.Message; }
                    }
                    else why = "scene not settled, assuming co-op for now";
                }

                _pvpAsks++;
                bool settledNow = UnityEngine.Time.realtimeSinceStartup >= _pvpSettleUntil;
                if (haveSingleton || (settledNow && _pvpAsks >= 3)) _pvpChecked = true;
            }

            if (verdict != IsPvp || !_pvpLogged)
            {
                _pvpLogged = true;
                Log.Msg("pvp=" + verdict + "  (" + why + ")" + (_pvpChecked ? "  [settled]" : "  [re-checking]"));
                if (verdict) Log.Warn("PVP detected: downed, revive and roles are OFF for this session unless AllowInPvp is set");
            }
            IsPvp = verdict;
        }

        internal static bool IsHost
        {
            get
            {
                try { return Il2CppMirror.NetworkServer.active; }
                catch { return false; }
            }
        }

        internal static Peer Me
        {
            get { return Find(Net.Self); }
        }

        internal static Peer Find(ulong id)
        {
            Peer p;
            return Peers.TryGetValue(id, out p) ? p : null;
        }

        internal static Peer Ensure(ulong id)
        {
            Peer p;
            if (!Peers.TryGetValue(id, out p))
            {
                p = new Peer { Id = id };
                Peers[id] = p;
            }
            return p;
        }

        internal static Peer FindByHealth(Il2Cpp.Health h)
        {
            if (h == null) return null;
            foreach (var kv in Peers)
            {
                var p = kv.Value;
                try { if (p.Health != null && p.Health.Pointer == h.Pointer) return p; }
                catch { }
            }
            return null;
        }

        static float _nextScan;
        static float _nextDeepScan;
        static int _scanCount;
        static int _lastFound = -1;

        static bool _pvpChecked;
        static float _pvpSettleUntil;
        static int _pvpAsks;
        static bool _pvpLogged;
        static bool _pmWalkWarned;

        static int _deepScanMisses;

        internal static void OnSceneChanged()
        {
            _pvpChecked = false;
            _pvpSettleUntil = UnityEngine.Time.realtimeSinceStartup + 12f;
            _pvpAsks = 0;
            _pvpLogged = false;
            _deepScanMisses = 0;
            _nextDeepScan = 0f;
        }

        static bool Adopt(Il2Cpp.PlayerNetworking pn, bool assumeLocal)
        {
            if (pn == null) return false;

            GameObject go;
            try { go = pn.gameObject; if (go == null || !go.scene.IsValid()) return false; }
            catch { return false; }

            Il2Cpp.PlayerMaster pm = null;
            try { pm = pn.PlayerMaster; } catch { }
            if (pm == null) { try { pm = pn.playerMaster; } catch { } }

            ulong id = 0UL;
            string nm = "";
            if (pm != null)
            {
                try { id = pm.NetworkthisPlayerSteam64; } catch { }
                try { nm = pm.NetworkthisPlayerName ?? ""; } catch { }
            }

            if (id == 0UL)
            {
                bool owned = assumeLocal;
                if (!owned) { try { owned = pn.isOwned; } catch { } }
                if (!owned) return false;
                id = Net.Self;
                if (id == 0UL) return false;
            }

            var p = Ensure(id);
            p.Pn = pn;
            p.Pm = pm;
            if (!string.IsNullOrEmpty(nm)) p.Name = nm;
            try { p.Root = go.transform.root; } catch { p.Root = go.transform; }
            try { p.Health = pn.health; } catch { }
            if (p.Health == null)
            {
                try { p.Health = p.Root.GetComponentInChildren<Il2Cpp.Health>(true); } catch { }
            }
            try { p.Med = p.Root.GetComponentInChildren<Il2Cpp.MedicalSystem>(true); } catch { }
            return true;
        }

        static float _smoothFrame = 0.016f;

        internal static bool UnderLoad { get { return _smoothFrame > 0.08f; } }

        internal static void SampleLoad()
        {
            float dt;
            try { dt = Time.unscaledDeltaTime; } catch { return; }
            if (dt <= 0f || dt > 2f) return;
            _smoothFrame = _smoothFrame * 0.9f + dt * 0.1f;
        }

        internal static void MaybeRescan(bool force)
        {
            if (!force && Time.realtimeSinceStartup < _nextScan) return;

            if (!force && UnderLoad)
            {
                _nextScan = Time.realtimeSinceStartup + 2f;
                if (!_loadNoted) { _loadNoted = true; Log.Msg("standing down: game under heavy load, deferring scans"); }
                return;
            }
            if (_loadNoted && !UnderLoad) { _loadNoted = false; Log.Msg("load eased, scanning resumed"); }

            _nextScan = Time.realtimeSinceStartup + 3f;
            Rescan();
        }

        static bool _loadNoted;

        static void Rescan()
        {
            _scanCount++;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            foreach (var kv in Peers)
            {
                kv.Value.Pn = null; kv.Value.Pm = null; kv.Value.Health = null;
                kv.Value.Med = null; kv.Value.Root = null;
            }

            int found = 0;

            try
            {
                var masters = Il2Cpp.PlayerMaster.AllPlayerMasters;
                if (masters != null)
                {
                    for (int i = 0; i < masters.Count; i++)
                    {
                        var pm = masters[i];
                        if (pm == null) continue;
                        ulong id = 0UL;
                        try { id = pm.NetworkthisPlayerSteam64; } catch { }
                        if (id == 0UL) continue;
                        var p = Ensure(id);
                        p.Pm = pm;
                        try { var nm = pm.NetworkthisPlayerName; if (!string.IsNullOrEmpty(nm)) p.Name = nm; } catch { }
                    }
                }
            }

            catch (Exception e) { if (!_pmWalkWarned) { _pmWalkWarned = true; Log.Detail("AllPlayerMasters walk failed (expected pre-lobby): " + e.Message); } }

            try
            {
                var set = Il2Cpp.Health.AllAlivePlayers;
                if (set != null)
                {
                    var e = set.GetEnumerator();
                    while (e.MoveNext())
                    {
                        var hh = e.Current;
                        if (hh == null) continue;
                        try { if (hh.isBot) continue; } catch { continue; }

                        Il2Cpp.PlayerNetworking pn = null;
                        try { pn = hh.transform.root.GetComponentInChildren<Il2Cpp.PlayerNetworking>(true); }
                        catch { }
                        if (Adopt(pn, false)) found++;
                    }
                }
            }
            catch (Exception e) { Log.Detail("AllAlivePlayers walk failed: " + e.Message); }

            var mine = Find(Net.Self);
            bool localMissing = mine == null || mine.Pn == null;
            if (localMissing && !UnderLoad && Time.realtimeSinceStartup >= _nextDeepScan)
            {
                _deepScanMisses++;
                _nextDeepScan = Time.realtimeSinceStartup + Mathf.Min(20f, 4f + _deepScanMisses * 2f);
                try
                {
                    var all = Resources.FindObjectsOfTypeAll<Il2Cpp.PlayerNetworking>();
                    if (all != null)
                        for (int i = 0; i < all.Length; i++)
                            if (Adopt(all[i], false)) found++;

                    var after = Find(Net.Self);
                    bool localResolved = after != null && after.Pn != null;
                    if (localResolved) _deepScanMisses = 0;
                    Log.Detail("deep scan used, found " + found
                             + ", local " + (localResolved ? "resolved" : "still missing")
                             + " (next in " + Mathf.Min(20f, 4f + _deepScanMisses * 2f).ToString("0") + "s)");
                }
                catch (Exception e) { Log.Detail("deep scan failed: " + e.Message); }
            }

            bool needMedical = false;
            foreach (var kv in Peers)
                if (kv.Value.Health != null && kv.Value.Med == null) { needMedical = true; break; }

            try
            {
                var meds = needMedical ? Resources.FindObjectsOfTypeAll<Il2Cpp.MedicalSystem>() : null;
                if (meds != null)
                {
                    for (int i = 0; i < meds.Length; i++)
                    {
                        var m = meds[i];
                        if (m == null) continue;
                        try { if (!m.gameObject.scene.IsValid()) continue; } catch { continue; }

                        Il2Cpp.Health mh = null;
                        try { mh = m.health; } catch { }
                        if (mh == null) continue;

                        foreach (var kv in Peers)
                        {
                            var p = kv.Value;
                            if (p.Health == null) continue;
                            try { if (p.Health.Pointer == mh.Pointer) { p.Med = m; break; } }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception e) { Log.Detail("medical rescan failed: " + e.Message); }

            if (!_pvpChecked) DecidePvp();

            InMission = found > 0;

            if (_scanCount == 1 || found != _lastFound || sw.Elapsed.TotalMilliseconds > 20)
            {
                _lastFound = found;
                var me = Find(Net.Self);
                Log.Msg("rescan #" + _scanCount + ": " + found + " players in " +
                        sw.Elapsed.TotalMilliseconds.ToString("0.0") + "ms" + (IsPvp ? "  (pvp)" : "") +
                        "   me=" + (me == null ? "unresolved" :
                            ("pn=" + (me.Pn != null) + " health=" + (me.Health != null) + " med=" + (me.Med != null))));
            }
        }

        static float _nextPoll;

        internal static void PollNetwork()
        {
            if (Time.realtimeSinceStartup < _nextPoll) return;
            _nextPoll = Time.realtimeSinceStartup + 0.4f;

            Net.RefreshIdentity();

            var seen = new HashSet<ulong>();
            var members = Net.Members();
            for (int i = 0; i < members.Count; i++)
            {
                ulong id = members[i];
                seen.Add(id);
                var p = Ensure(id);

                p.HasMod = Net.Get(id, Net.K_VER) == Net.SCHEMA;
                if (string.IsNullOrEmpty(p.Name))
                {
                    var persona = Net.PersonaName(id);
                    if (!string.IsNullOrEmpty(persona)) p.Name = persona;
                }
                if (id == Net.Self) { p.Role = MyRole; p.HasMod = true; }
                else if (p.HasMod) p.Role = RoleInfo.Clamp(Net.Int(Net.Get(id, Net.K_ROLE), 0));
                else p.Role = Role.None;

                var vit = Net.Split(Net.Get(id, Net.K_VITALS));
                if (vit.Length >= 3)
                {
                    p.Hr = Net.Float(vit[0]);
                    p.Pain = Net.Float(vit[1]);
                    p.TqMask = Net.Int(vit[2]);
                    p.SelfHeals = vit.Length >= 4 ? Net.Int(vit[3]) : 0;
                    p.EpiReceived = vit.Length >= 5 ? Net.Int(vit[4]) : 0;
                }
            }

            var drop = new List<ulong>();
            foreach (var kv in Peers)
                if (!seen.Contains(kv.Key) && !kv.Value.Live) drop.Add(kv.Key);
            for (int i = 0; i < drop.Count; i++) Peers.Remove(drop[i]);

            Downed.ConsumeState();
            Give.Pump();
            Treat.PumpNetwork();
            Drag.ConsumeNetwork();
        }

        internal static string NameOf(ulong id)
        {
            var p = Find(id);
            if (p != null && !string.IsNullOrEmpty(p.Name)) return p.Name;
            return id == 0UL ? "?" : id.ToString();
        }
    }
}
