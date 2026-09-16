// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using UnityEngine;
using MelonLoader;
using HarmonyLib;

namespace MapLoader
{

    public static class MapHVT
    {

        public const string HvtName = "Osama bin Laden";

        static MelonLogger.Instance _log;
        static bool _spawnedThisLevel;
        static GameObject _point;

        static float _watchUntil;
        static float _holdUntil;
        static float _nextPoll;
        static Il2Cpp.BrainAI _him;

        public static void Init(MelonLogger.Instance log) { _log = log; }

        public static bool WillPlace
        {
            get
            {
                if (_point != null || _him != null) return true;

                try { Vector3 v; return MapRuntime.TryMarkedHvt(out v); } catch { return false; }
            }
        }

        public static void ResetForNewLevel()
        {
            _spawnedThisLevel = false;
            _point = null;
            _watchUntil = 0f;
            _holdUntil = 0f;
            _him = null;
            _clientDressed = false;
            _clientGiveUp = 0f;
            _clientArmAt = 0f; _clientNext = 0f;
        }

        static bool _clientDressed;
        static float _clientGiveUp;
        static float _clientArmAt;
        static float _clientNext;

        static void ClientTick(float now)
        {
            if (_clientDressed) return;
            if (!MapRuntime.Done) return;

            Vector3 marked;
            if (!MapRuntime.TryMarkedHvt(out marked)) { _clientDressed = true; return; }

            if (_clientArmAt == 0f) _clientArmAt = now + 30f;
            if (now < _clientArmAt) return;

            if (_clientGiveUp == 0f) _clientGiveUp = now + 360f;
            if (now < _clientNext) return;
            _clientNext = now + 2f;
            if (now > _clientGiveUp)
            {
                _clientDressed = true;
                _log?.Warning("[hvt] client: no bot ever appeared within reach of the marked "
                            + "spot " + marked + ", so the custom body was never fitted here. "
                            + "He will look like a standard bot on this machine.");
                return;
            }

            try
            {
                var bots = UnityEngine.Object.FindObjectsOfType<Il2Cpp.BrainAI>();
                if (bots == null || bots.Length == 0) return;

                Il2Cpp.BrainAI best = null;
                float bestSq = 16f;
                for (int i = 0; i < bots.Length; i++)
                {
                    var b = bots[i];
                    if (b == null) continue;
                    float d;
                    try { d = (b.transform.root.position - marked).sqrMagnitude; } catch { continue; }
                    if (d < bestSq) { bestSq = d; best = b; }
                }
                if (best == null) return;

                if (!MapBody.Wear(best))
                {
                    _log?.Msg("[hvt] client: that was not him (the body drew nowhere "
                            + "near the bot). Waiting and trying again.");
                    return;
                }
                _clientDressed = true;
                _log?.Msg("[hvt] client: fitted the custom body to the bot "
                        + Mathf.Sqrt(bestSq).ToString("F1") + " m from the marked spot. "
                        + "Identity cannot be replicated (isHVT and myName are not SyncVars), "
                        + "so position is what both machines agree on.");
            }
            catch (Exception e) { _log?.Warning("[hvt] client body: " + e.Message); }
        }

        public static void Tick()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _nextPoll) return;
            _nextPoll = now + 0.5f;

            if (!Il2CppMirror.NetworkServer.active)
            {
                if (Il2CppMirror.NetworkClient.active) ClientTick(now);
                return;
            }

            try
            {

                if (_him == null && _watchUntil > 0f)
                {
                    if (now > _watchUntil)
                    {
                        _watchUntil = 0f;
                        _log?.Warning("[hvt] the game's HVT spawner produced nobody. Placing him "
                                    + "directly instead.");
                        if (SpawnDirect()) _holdUntil = now + 15f;
                        return;
                    }
                    if (FindHim()) _holdUntil = now + 15f;
                    return;
                }

                if (_him != null && now < _holdUntil && _point != null)
                {
                    try
                    {
                        StandStill(_him);
                        var root = _him.transform.root;
                        if ((root.position - _point.transform.position).sqrMagnitude > 4f)
                        {
                            root.position = _point.transform.position;

                            MapAI.AnnounceTeleportPublic(root, _point.transform.position,
                                                         root.rotation);
                            _log?.Msg("[hvt] something moved " + HvtName + "; put him back.");
                        }
                    }
                    catch { _him = null; }
                }
            }
            catch (Exception e) { _log?.Warning("[hvt] tick: " + e.Message); }
        }

        static void StandStill(Il2Cpp.BrainAI b)
        {
            if (b == null) return;
            try { b.idleStates = Il2Cpp.BrainAI.IdleStates.Idle; } catch { }
            try { b.WanderDistance = 0; } catch { }
        }

        public static void SpawnIfMarked()
        {
            if (_spawnedThisLevel) return;
            try
            {
                if (!Il2CppMirror.NetworkServer.active) return;
                if (!MapRuntime.Done) return;

                Vector3 marked;
                if (!MapRuntime.TryMarkedHvt(out marked)) { _spawnedThisLevel = true; return; }

                var raid = Il2Cpp.RaidManager.singleton;
                if (raid == null) return;

                _spawnedThisLevel = true;

                var pos = MapRuntime.GroundedUnder(marked);

                _point = new GameObject("ODG_HVT_Spawn");
                if (MapRuntime.Root != null) _point.transform.SetParent(MapRuntime.Root.transform, false);
                _point.transform.position = pos;

                MapAI.EnsureDetails(_point);

                var prefabs = raid.hvtAI;
                bool borrowed = false;
                if (prefabs == null || prefabs.Length == 0)
                {
                    var std = raid.standardAI;
                    if (std == null || std.Length == 0)
                    {
                        _log?.Error("[hvt] neither hvtAI nor standardAI has a prefab; cannot place "
                                  + HvtName + ". The wave spawner would have failed too.");
                        return;
                    }
                    var one = new Il2CppInterop.Runtime.InteropTypes.Arrays
                                  .Il2CppReferenceArray<GameObject>(1L);
                    one[0] = std[0];
                    raid.hvtAI = one;
                    borrowed = true;
                }

                var pts = new Il2CppInterop.Runtime.InteropTypes.Arrays
                              .Il2CppReferenceArray<GameObject>(1L);
                pts[0] = _point;
                raid.hvtSpawnPoints = pts;
                if (borrowed)
                {

                    _log?.Msg("[hvt] placing " + HvtName + " at " + pos + " directly: this host "
                            + "level ships no HVT prefab, and a borrowed standard prefab through "
                            + "the game's spawner comes back unflagged and gets doubled.");
                    if (SpawnDirect()) _holdUntil = Time.realtimeSinceStartup + 15f;
                    return;
                }

                try
                {
                    var gm = Il2Cpp.GameManager.instance;
                    if (gm != null) gm.botHVTAmount = 1;
                }
                catch { }

                _log?.Msg("[hvt] placing " + HvtName + " at " + pos
                        + (borrowed ? "  (borrowed a standard bot prefab: this host level ships no HVT prefab)" : "")
                        + " and handing the spawn to the game's own ServerSpawnHVTs");

                try { raid.ServerSpawnHVTs(); }
                catch (Exception e) { _log?.Error("[hvt] ServerSpawnHVTs threw: " + e.Message); return; }

                _watchUntil = Time.realtimeSinceStartup + 20f;
            }
            catch (Exception e) { _log?.Error("[hvt] " + e.Message); }
        }

        static bool SpawnDirect()
        {
            try
            {
                var raid = Il2Cpp.RaidManager.singleton;
                if (raid == null || _point == null) return false;

                GameObject prefab = null;
                try { if (raid.hvtAI != null && raid.hvtAI.Length > 0) prefab = raid.hvtAI[0]; } catch { }
                if (prefab == null)
                { try { if (raid.standardAI != null && raid.standardAI.Length > 0) prefab = raid.standardAI[0]; } catch { } }
                if (prefab == null) { _log?.Error("[hvt] no prefab to place directly"); return false; }

                var go = UnityEngine.Object.Instantiate(prefab, _point.transform.position,
                                                        _point.transform.rotation);
                if (go == null) { _log?.Error("[hvt] Instantiate returned null"); return false; }
                try { go.SetActive(true); } catch { }
                try { Il2CppMirror.NetworkServer.Spawn(go); }
                catch (Exception se) { _log?.Error("[hvt] NetworkServer.Spawn: " + se.Message); return false; }

                var brain = go.GetComponentInChildren<Il2Cpp.BrainAI>();
                if (brain == null) { _log?.Error("[hvt] placed prefab has no BrainAI"); return false; }
                try { brain.isHVT = true; } catch { }
                try { brain.myName = HvtName; } catch { }
                try { if (brain.aiPatrol != null) brain.aiPatrol.patrolPoints = null; } catch { }
                _him = brain;

                _log?.Msg("[hvt] placed " + HvtName + " directly at " + _point.transform.position
                        + " and spawned him over the network.");
                try { MapBody.Wear(brain); } catch (Exception be) { _log?.Warning("[hvt] body: " + be.Message); }
                return true;
            }
            catch (Exception e) { _log?.Error("[hvt] direct place: " + e.Message); return false; }
        }

        static bool FindHim()
        {
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<Il2Cpp.BrainAI>();
                if (all == null) return false;
                int named = 0;
                for (int i = 0; i < all.Length; i++)
                {
                    var b = all[i];
                    if (b == null) continue;
                    bool hvt = false;
                    try { hvt = b.isHVT; } catch { continue; }
                    if (!hvt) continue;

                    try
                    {
                        if (_point != null &&
                            (b.transform.position - _point.transform.position).sqrMagnitude > 1600f) continue;
                    }
                    catch { }

                    try
                    {
                        var hr = b.transform.root;
                        hr.position = _point.transform.position;
                        MapAI.AnnounceTeleportPublic(hr, _point.transform.position, hr.rotation);
                    }
                    catch { }
                    try { b.myName = HvtName; named++; } catch { }
                    _him = b;

                    try { MapBody.Wear(b); } catch (Exception be) { _log?.Warning("[hvt] body: " + be.Message); }

                    try { if (b.aiPatrol != null) b.aiPatrol.patrolPoints = null; } catch { }

                    StandStill(b);
                }
                if (named > 0)
                {
                    _log?.Msg("[hvt] " + HvtName + " is in, at " + _point.transform.position
                            + ". Named " + named + " HVT(s); gore, animation and "
                            + "kill-confirmation are the game's own because he is a real AI "
                            + "rather than a prop.");
                    return true;
                }
                return false;
            }
            catch (Exception e) { _log?.Warning("[hvt] naming: " + e.Message); return false; }
        }
    }
}
