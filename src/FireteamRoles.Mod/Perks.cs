// ODG Operations - FireteamRoles
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using UnityEngine;

namespace FireteamRoles
{

    internal static class Perks
    {
        const int StockGrenadeCap = 4;

        internal static int LoadoutCap
        {
            get { return StockGrenadeCap; }
        }

        internal static int MyGrenadeCap
        {
            get
            {
                if (State.Enabled && State.MyRole == Role.Breacher)
                    return Mathf.Max(StockGrenadeCap, Cfg.BreacherExplosives.Value);
                return Mathf.Max(1, Cfg.DefaultExplosives.Value);
            }
        }

        internal static int CapFor(Il2Cpp.PlayerNetworking pn)
        {
            try
            {
                foreach (var kv in State.Peers)
                {
                    var p = kv.Value;
                    if (p.Pn == null || pn == null) continue;
                    if (p.Pn.Pointer != pn.Pointer) continue;
                    if (p.HasMod && p.Role == Role.Breacher)
                        return Mathf.Max(StockGrenadeCap, Cfg.BreacherExplosives.Value);
                    return Mathf.Max(1, Cfg.DefaultExplosives.Value);
                }
            }
            catch { }
            return StockGrenadeCap;
        }

        static void Popup(int cap)
        {
            try
            {
                var hm = Il2Cpp.HintManager.singleton;
                if (hm == null) return;

                string msg;
                if (State.Enabled && State.MyRole == Role.Breacher &&
                    Cfg.BreacherExplosives.Value > cap && Cfg.BreacherTopUp.Value)
                {
                    msg = "Pack " + cap + " here. The other " + (Cfg.BreacherExplosives.Value - cap) +
                          " are issued when you insert";
                }
                else msg = "You cannot have more than " + cap + " Grenades";

                hm.SpawnPopup(msg, Il2Cpp.Popup.PopupType.YellowWarning, true, false);
            }
            catch { }
        }

        internal static bool AddGrenadeToPouch(string nadeName)
        {
            try
            {
                var pm = Il2Cpp.PlayerMaster.MyPlayerMaster;
                if (pm == null || pm.Grenades == null) return true;
                int cap = LoadoutCap;
                if (pm.Grenades.Count >= cap) { Popup(cap); return false; }

                int before = pm.Grenades.Count;
                pm.TryAddGrenadeToPreset(nadeName);
                if (pm.Grenades.Count == before) return false;

                var pn = LocalPn();
                if (pn != null) pn.SpawnWeapon(nadeName, "", Il2Cpp.PlayerNetworking.WeaponSlot.Grenade);
                Log.Fired("nade-add");
                return false;
            }
            catch (Exception e) { Log.Detail("AddGrenadeToPouch reimpl failed, stock takes it: " + e.Message); return true; }
        }

        internal static bool TryAddGrenadeToPreset(Il2Cpp.PlayerMaster pm, string grenadeName)
        {
            try
            {
                if (pm == null || pm.Grenades == null) return true;
                try
                {
                    var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                    if (scene != "Operation Room") return false;
                }
                catch { }

                if (!string.IsNullOrEmpty(grenadeName) && grenadeName.EndsWith("(Clone)"))
                    grenadeName = grenadeName.Substring(0, grenadeName.Length - 7);

                int cap = LoadoutCap;
                if (pm.Grenades.Count >= cap) { Popup(cap); return false; }

                pm.Grenades.Add(grenadeName);
                pm.SaveNadesList();
                Log.Fired("nade-persist");
                return false;
            }
            catch (Exception e) { Log.Detail("TryAddGrenadeToPreset reimpl failed, stock takes it: " + e.Message); return true; }
        }

        internal static bool InInit;

        internal static void PreInit()
        {
            InInit = true;
            try
            {
                var pm = Il2Cpp.PlayerMaster.MyPlayerMaster;
                if (pm == null || pm.Grenades == null) return;
                int cap = LoadoutCap;
                var vals = pm.Grenades.values;
                if (vals == null || vals.Count <= cap) return;
                int removed = 0;
                while (vals.Count > cap) { vals.RemoveAt(vals.Count - 1); removed++; }
                pm.SaveNadesList();
                Log.Msg("pouch trimmed to " + cap + " (" + removed + " removed, role is " + RoleInfo.Name(State.MyRole) + ")");
            }
            catch (Exception e) { Log.Detail("pouch trim failed: " + e.Message); }
        }

        internal static void PostInit() { InInit = false; }

        internal static bool AllowClearAllGrenades()
        {
            if (!InInit) return true;
            try
            {
                var pm = Il2Cpp.PlayerMaster.MyPlayerMaster;
                if (pm == null || pm.Grenades == null) return true;
                if (pm.Grenades.Count <= LoadoutCap)
                {
                    Log.Fired("nade-wipe-blocked");
                    return false;
                }
            }
            catch { }
            return true;
        }

        internal static bool ClientPickup(Il2Cpp.GrenadeV2 g, Il2CppMirror.NetworkIdentity netid,
                                          Il2Cpp.PlayerNetworking pn, bool spawnEquip)
        {
            try
            {
                if (pn == null || !pn.isOwned) return true;
                var list = pn.GrenadesList;
                if (list == null) return true;
                int cap = MyGrenadeCap;
                if (list.Count >= StockGrenadeCap && list.Count < cap)
                {
                    g.CMD_PickupGrenade(netid, pn, spawnEquip);
                    Log.Fired("nade-pickup-extend");
                    return false;
                }
            }
            catch { }
            return true;
        }

        internal static bool ServerPickup(Il2Cpp.GrenadeV2 g, Il2CppMirror.NetworkIdentity netid,
                                          Il2Cpp.PlayerNetworking pn, bool spawnEquip)
        {
            try
            {
                if (!State.IsHost) return true;
                if (pn == null) return true;
                var list = pn.GrenadesList;
                if (list == null) return true;
                if (list.Count < StockGrenadeCap) return true;

                int cap = CapFor(pn);
                if (list.Count >= cap) return false;

                var conn = pn.connectionToClient;
                if (netid != null && conn != null) netid.AssignClientAuthority(conn);

                var ident = g.ResolveSpawnedGrenadeIdentity();
                if (ident == null) g.ServerSpawnProjectile(conn);
                else g.ServerReassignSpawnedGrenadeAuthority(conn);

                g.NetworkisDropped = false;

                var ws = g.weaponSpawner;
                if (ws != null)
                {
                    try { ws.CMD_SpawnFirearms(); } catch { }
                    g.weaponSpawner = null;
                }

                list.Add((int)g.netId);
                Log.Fired("nade-server-extend");
                return false;
            }
            catch (Exception e)
            {
                Log.Err("server pickup extension failed, grenade refused: " + e.Message);
                return false;
            }
        }

        static float _next;
        static float _savedDrainRate = -1f;
        static bool _sniperApplied;

        internal static void Tick()
        {
            if (Time.realtimeSinceStartup < _next) return;
            _next = Time.realtimeSinceStartup + 2f;
            if (!State.Enabled) return;

            var me = State.Me;
            if (me == null || !me.Live) return;

            SniperArms(me, State.MyRole == Role.Sniper);
            AssaulterSprint(me, State.MyRole == Role.Assaulter);
        }

        static bool _sniperStockKnown;
        static float _stockDrainRate, _stockBreathMin, _stockBreathMax;

        static void SniperArms(Peer me, bool on)
        {
            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<Il2Cpp.ArmStamina> arms = null;
            try { arms = me.Root.GetComponentsInChildren<Il2Cpp.ArmStamina>(true); }
            catch (Exception e) { Log.Detail("sniper arms scan failed: " + e.Message); return; }
            if (arms == null || arms.Length == 0) return;

            if (!_sniperStockKnown)
            {
                try
                {
                    var a0 = arms[0];
                    if (a0 != null)
                    {
                        _stockDrainRate = a0.drainRate;
                        _stockBreathMin = a0.breathMin;
                        _stockBreathMax = a0.breathMax;
                        _sniperStockKnown = true;
                    }
                }
                catch { }
            }

            if (!on)
            {
                if (!_sniperApplied) return;
                _sniperApplied = false;
                if (!_sniperStockKnown) return;
                for (int i = 0; i < arms.Length; i++)
                {
                    var a = arms[i];
                    if (a == null) continue;
                    try
                    {
                        a.drain = true;
                        a.drainRate = _stockDrainRate;
                        a.breathMin = _stockBreathMin;
                        a.breathMax = _stockBreathMax;
                    }
                    catch { }
                }
                Log.Msg("sniper: steady arms released");
                return;
            }

            int touched = 0;
            for (int i = 0; i < arms.Length; i++)
            {
                var a = arms[i];
                if (a == null) continue;
                try
                {
                    a.drain = false;
                    a.drainRate = 0f;
                    a.armStamina = Mathf.Max(a.armStamina, 100f);
                    a.breathMin = 0f;
                    a.breathMax = 0f;
                    a.Breath = 0f;
                    touched++;
                }
                catch { }
            }
            if (touched > 0 && !_sniperApplied)
            {
                _sniperApplied = true;
                Log.Msg("sniper: steady arms on " + touched + " weapon(s)");
                Log.Fired("sniper-arms");
            }
        }

        static float _stockSprint = -1f, _stockSprintHR = -1f;
        static bool _sprintSpeedLogged;

        static void AssaulterSprint(Peer me, bool on)
        {
            try
            {
                var mc = me.Pn != null ? me.Pn.masterController : null;
                if (mc == null) return;

                if (_stockSprint < 0f)
                {
                    try { _stockSprint = mc.SprintSpeed; _stockSprintHR = mc.SprintSpeedHighReady; }
                    catch { }
                }

                if (on)
                {
                    if (_savedDrainRate < 0f)
                    {
                        _savedDrainRate = mc.staminaDrainRate;
                        Log.Msg("assaulter: infinite sprint (stock drain was " + _savedDrainRate + ")");
                        Log.Fired("assaulter-sprint");
                    }
                    mc.staminaDrainRate = 0f;
                    mc.sprintStaminaDepleted = false;
                    mc.sprintStamina = Mathf.Max(mc.sprintStamina, 80f);

                    float k = 1f + Mathf.Clamp(Cfg.AssaulterSprintBonus.Value, 0f, 2f);
                    if (_stockSprint > 0f)
                    {
                        float want = _stockSprint * k;
                        float wantHR = _stockSprintHR * k;

                        if (!Mathf.Approximately(mc.SprintSpeed, want)) mc.SprintSpeed = want;
                        if (_stockSprintHR > 0f && !Mathf.Approximately(mc.SprintSpeedHighReady, wantHR))
                            mc.SprintSpeedHighReady = wantHR;
                        if (!_sprintSpeedLogged)
                        {
                            _sprintSpeedLogged = true;
                            Log.Msg("assaulter: sprint " + _stockSprint.ToString("0.##") + " -> " +
                                    want.ToString("0.##") + " (+" +
                                    (Cfg.AssaulterSprintBonus.Value * 100f).ToString("0") + "%)");
                            Log.Fired("assaulter-sprint-speed");
                        }
                    }
                }
                else
                {
                    if (_savedDrainRate >= 0f)
                    {

                        mc.staminaDrainRate = _savedDrainRate;
                        _savedDrainRate = -1f;
                        Log.Msg("assaulter: sprint drain restored");
                    }
                    if (_sprintSpeedLogged && _stockSprint > 0f)
                    {
                        mc.SprintSpeed = _stockSprint;
                        if (_stockSprintHR > 0f) mc.SprintSpeedHighReady = _stockSprintHR;
                        _sprintSpeedLogged = false;
                        Log.Msg("assaulter: sprint speed restored");
                    }
                }
            }
            catch (Exception e) { Log.Detail("assaulter sprint failed: " + e.Message); }
        }

        static readonly Dictionary<IntPtr, float> _botStock = new Dictionary<IntPtr, float>();
        static float _nextAi;
        static bool _aiLogged;

        internal static void SniperAiTick()
        {
            if (!State.IsHost || !State.Enabled) return;
            if (Time.realtimeSinceStartup < _nextAi) return;
            _nextAi = Time.realtimeSinceStartup + 0.4f;

            float cut = Mathf.Clamp(Cfg.SniperHitChanceCut.Value, 0f, 0.95f);
            if (cut <= 0f) return;

            try
            {
                var teams = Il2Cpp.TeamManager.OperatorTeams;
                if (teams == null) return;

                for (int teamId = 0; teamId <= 8; teamId++)
                {
                    Il2Cpp.TeamRuntimeData trd = null;
                    try { if (!teams.TryGetValue(teamId, out trd)) continue; }
                    catch { continue; }
                    if (trd == null) continue;

                    Il2CppSystem.Collections.Generic.List<Il2Cpp.BrainAI> bots = null;
                    try { bots = trd.TeamBots; } catch { }
                    if (bots == null) continue;

                    for (int i = 0; i < bots.Count; i++)
                    {
                        var b = bots[i];
                        if (b == null) continue;
                        ApplyToBot(b, cut);
                    }
                }
            }
            catch (Exception e) { Log.Detail("sniper ai tick failed: " + e.Message); }
        }

        static void ApplyToBot(Il2Cpp.BrainAI b, float cut)
        {
            try
            {
                IntPtr key = b.Pointer;
                float stock;
                if (!_botStock.TryGetValue(key, out stock))
                {

                    if (_botStock.Count > 512) ForgetBots("cache full");

                    stock = b.baseHitChance;
                    if (stock <= 0f) return;
                    _botStock[key] = stock;
                }

                bool onSniper = AimingAtSniper(b);
                float want = onSniper ? stock * (1f - cut) : stock;
                if (Mathf.Approximately(b.baseHitChance, want)) return;
                b.baseHitChance = want;

                if (onSniper && !_aiLogged)
                {
                    _aiLogged = true;
                    Log.Msg("sniper: bot hit chance " + stock.ToString("0.###") + " -> " +
                            want.ToString("0.###") + " while aiming at a sniper");
                    Log.Fired("sniper-evasion");
                }
            }
            catch { }
        }

        static bool AimingAtSniper(Il2Cpp.BrainAI b)
        {
            try
            {
                var t = b.AimingTargetTransform;
                if (t == null) return false;
                var root = t.root;
                if (root == null) return false;

                foreach (var kv in State.Peers)
                {
                    var p = kv.Value;
                    if (p == null || !p.HasMod || p.Role != Role.Sniper) continue;
                    if (p.Root == null) continue;
                    if (p.Root.Pointer == root.Pointer) return true;
                }
            }
            catch { }
            return false;
        }

        internal static void ForgetBots(string why)
        {
            _botStock.Clear();
            _aiLogged = false;
            Log.Detail("bot hit-chance cache cleared (" + why + ")");
        }

        internal static void Reset(string why)
        {
            _sniperApplied = false;

            RestoreSprint();
            _sprintSpeedLogged = false;
            _stockSprint = -1f;
            _stockSprintHR = -1f;
            ForgetBots(why);
        }

        static void RestoreSprint()
        {
            if (!_sprintSpeedLogged || _stockSprint <= 0f) return;
            try
            {
                var me = State.Me;
                var mc = (me != null && me.Pn != null) ? me.Pn.masterController : null;
                if (mc == null) return;
                mc.SprintSpeed = _stockSprint;
                if (_stockSprintHR > 0f) mc.SprintSpeedHighReady = _stockSprintHR;
                Log.Detail("sprint speed put back to " + _stockSprint.ToString("0.##") + " before reset");
            }
            catch { }
        }

        static Il2Cpp.PlayerNetworking LocalPn()
        {
            var me = State.Me;
            if (me != null && me.Pn != null) return me.Pn;
            try { return Il2Cpp.GameManager.myPlayerNetworking; }
            catch { return null; }
        }
    }
}
