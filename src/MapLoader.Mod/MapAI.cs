// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;
using Il2CppSystem.Collections.Generic;
using L = Il2CppSystem.Collections.Generic;

namespace MapLoader
{

    public static class MapAI
    {

        public const string SpawnGroup  = "GBAISpawnPoint";
        public const string GuardGroup  = "GBAIGuardPoint";
        public const string PatrolGroup = "GBAIPatrolRoute";
        public const string PlayerGroup = "GBPlayerStart";
        public const string InsertGroup = "GBInsertionPoint";

        static MelonLogger.Instance _log;
        static bool _installed;
        static int _lastApplied;

        public static void Init(MelonLogger.Instance log) { _log = log; }

        public static void ResetForNewLevel()
        { _lastApplied = 0; _waveTrimmed = -1; ResetSpawnSeam(); MapPatrols.ResetForNewLevel();
          _bornHere.Clear(); }

        static readonly System.Collections.Generic.HashSet<int> _bornHere =
            new System.Collections.Generic.HashSet<int>();

        public static void Install(HarmonyLib.Harmony h, MelonLogger.Instance log)
        {
            _log = log;
            if (_installed) return;
            _installed = true;

            try
            {
                var m = AccessTools.Method(typeof(Il2Cpp.RaidManager), "ServerSpawnAI",
                                           new[] { typeof(bool) });
                if (m != null)
                {
                    h.Patch(m, prefix: new HarmonyMethod(typeof(MapAI), nameof(Pre_ServerSpawnAI)));

                    try
                    {
                        var cs = AccessTools.Method(typeof(Il2Cpp.RaidManager), "CanSpawn");
                        if (cs != null)
                            h.Patch(cs, prefix: new HarmonyMethod(typeof(MapAI), nameof(Pre_CanSpawn)));
                        else _log?.Warning("[mapai] RaidManager.CanSpawn not found; the game's "
                                         + "player-distance spawn guard stays in force");
                    }
                    catch (Exception ce) { _log?.Warning("[mapai] CanSpawn patch: " + ce.Message); }
                    log.Msg("[mapai] hooked RaidManager.ServerSpawnAI");
                }
                else log.Warning("[mapai] no RaidManager.ServerSpawnAI(bool)");

                var ib = AccessTools.Method(typeof(Il2Cpp.RaidManager), "InstantiateBot");
                if (ib != null)
                {
                    h.Patch(ib, prefix: new HarmonyMethod(typeof(MapAI), nameof(Pre_InstantiateBot)));
                    log.Msg("[mapai] hooked RaidManager.InstantiateBot (spawn placement seam)");
                }
                else log.Warning("[mapai] no RaidManager.InstantiateBot -- bots will spawn at "
                               + "the game's own points and only the host will see them right");

                int sp = 0;
                foreach (var mi in AccessTools.GetDeclaredMethods(typeof(Il2CppMirror.NetworkServer)))
                {
                    if (mi.Name != "Spawn" || !mi.IsStatic) continue;
                    var ps = mi.GetParameters();
                    if (ps.Length == 0 || ps[0].ParameterType != typeof(GameObject)) continue;
                    try
                    {
                        h.Patch(mi, prefix: new HarmonyMethod(typeof(MapAI), nameof(Pre_NetworkSpawn)));
                        sp++;
                    }
                    catch (Exception pe)
                    { log.Warning("[mapai] Spawn overload: " + pe.Message); }
                }
                _seamInstalled = sp;
                if (sp > 0)
                    log.Msg("[mapai] hooked NetworkServer.Spawn x" + sp
                          + " (placement seam v2: the bot is moved BEFORE the spawn "
                          + "payload is written, so every client is born knowing the "
                          + "right place)");
                else
                    log.Warning("[mapai] NO NetworkServer.Spawn overload patched -- the "
                              + "placement seam is not installed and co-op will desync");
            }
            catch (Exception e) { log.Error("[mapai] patch: " + e.Message); }

            MapScene.CustomSceneLoaded += OnCustomScene;
        }

        static void OnCustomScene(Scene scene)
        {

            _lastApplied = 0;
            Apply("scene load");
        }

        static bool Pre_ServerSpawnAI() { Apply("ServerSpawnAI"); return MatchWaveToSlider(); }

        static int _guardAnswered, _guardRefused;
        static int _spawnLowRoof;

        static bool Pre_CanSpawn(Vector3 __0, ref bool __result)
        {
            try
            {
                if (CustomOps.LaunchAiCount < 1) return true;
                if (!Il2CppMirror.NetworkServer.active) return true;
                var players = UnityEngine.Object.FindObjectsOfType<Il2Cpp.MasterController>();
                for (int i = 0; i < (players != null ? players.Length : 0); i++)
                {
                    var pl = players[i];
                    if (pl == null) continue;
                    var d = pl.transform.position - __0; d.y = 0f;
                    if (d.sqrMagnitude < 9f) { __result = false; _guardRefused++; return false; }
                }
                __result = true;
                _guardAnswered++;
                return false;
            }
            catch { return true; }
        }

        static int AliveWave()
        {
            int alive = 0;
            try
            {

                bool haveBox = false;
                Vector3 lo = Vector3.zero, hi = Vector3.zero;
                try
                {
                    var pts = FindGroup(SpawnGroup);
                    for (int i = 0; i < (pts != null ? pts.Count : 0); i++)
                    {
                        if (pts[i] == null) continue;
                        var p = pts[i].position;
                        if (!haveBox) { lo = p; hi = p; haveBox = true; }
                        else { lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
                    }
                }
                catch { }
                if (haveBox)
                {
                    lo -= new Vector3(30f, 6f, 30f);
                    hi += new Vector3(30f, 6f, 30f);
                }
                var bots = UnityEngine.Object.FindObjectsOfType<Il2Cpp.BrainAI>();
                for (int i = 0; i < (bots != null ? bots.Length : 0); i++)
                {
                    var b = bots[i];
                    if (b == null) continue;
                    bool hvt = false; try { hvt = b.isHVT; } catch { }
                    if (hvt) continue;
                    var root = b.transform.root;

                    if (_bornHere.Count > 0 && !_bornHere.Contains(root.GetInstanceID())) continue;
                    if (haveBox)
                    {
                        var p = root.position;
                        if (p.x < lo.x || p.x > hi.x || p.y < lo.y || p.y > hi.y
                         || p.z < lo.z || p.z > hi.z) continue;
                    }
                    Il2Cpp.Health hp = null;
                    try
                    {
                        hp = b.GetComponentInParent<Il2Cpp.Health>();
                        if (hp == null) hp = b.GetComponentInChildren<Il2Cpp.Health>(true);
                    }
                    catch { }
                    bool dead = false; try { if (hp != null) dead = hp.isDead; } catch { }
                    if (!dead) alive++;
                }
            }
            catch { }
            return alive;
        }

        static int _waveTrimmed = -1;

        public static bool MatchWaveToSlider()
        {
            try
            {
                int want = CustomOps.LaunchAiCount;
                if (want < 1) return true;

                int hvt = MapHVT.WillPlace ? 1 : 0;
                int total = want;
                want = Math.Max(0, want - hvt);
                var raid = Il2Cpp.RaidManager.singleton;
                if (raid == null) return true;

                int alive = AliveWave();
                int missing = Math.Max(0, want - alive);
                if (missing == 0)
                {
                    _log?.Msg("[mapai] OPFOR COUNT " + total + ": the wave already stands at "
                            + alive + " hostile(s) + HVT " + hvt + ", so this ServerSpawnAI call is "
                            + "SKIPPED rather than adding another wave (two calls per mission is "
                            + "how 6 became 12).");
                    return false;
                }
                int fullWave = want;
                want = missing;

                var list = raid.botSpawnPoints;
                int had = list != null ? list.Count : 0;
                if (had > want)
                {
                    if (want == 0)
                    {
                        raid.botSpawnPoints = new Il2CppSystem.Collections.Generic.List<GameObject>();
                    }
                    else
                    {

                    var keep = new Il2CppSystem.Collections.Generic.List<GameObject>();
                    var taken = new System.Collections.Generic.HashSet<int>();
                    int nearTook = 0, nearHave = 0;
                    float share = 0f, radius = 0f;
                    try
                    {
                        share = Mathf.Clamp01(Main.HvtShare != null ? Main.HvtShare.Value : 0f);
                        radius = Main.HvtRadius != null ? Main.HvtRadius.Value : 0f;
                    }
                    catch { }
                    Vector3 hvtSpot;
                    if (share > 0f && radius > 0f && MapRuntime.TryMarkedHvt(out hvtSpot))
                    {
                        var near = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<float, int>>();
                        for (int i = 0; i < had; i++)
                        {
                            var go = list[i]; if (go == null) continue;
                            Vector3 d; try { d = go.transform.position - hvtSpot; } catch { continue; }
                            d.y = 0f;
                            float dist = d.magnitude;
                            if (dist <= radius) near.Add(new System.Collections.Generic.KeyValuePair<float, int>(dist, i));
                        }
                        near.Sort((a, b) => a.Key.CompareTo(b.Key));
                        nearHave = near.Count;
                        int nearWant = Mathf.Min(near.Count, Mathf.RoundToInt(want * share));
                        for (int i = 0; i < nearWant; i++) { keep.Add(list[near[i].Value]); taken.Add(near[i].Value); nearTook++; }
                    }

                    int rest = want - nearTook;
                    if (rest > 0)
                    {
                        var pool = new System.Collections.Generic.List<int>();
                        for (int i = 0; i < had; i++) if (!taken.Contains(i) && list[i] != null) pool.Add(i);
                        double step = pool.Count / (double)rest;
                        for (int i = 0; i < rest && pool.Count > 0; i++)
                        {
                            int k = (int)(i * step);
                            if (k >= pool.Count) k = pool.Count - 1;
                            keep.Add(list[pool[k]]);
                        }
                    }
                    raid.botSpawnPoints = keep;
                    if (share > 0f)
                        _log?.Msg("[mapai] compound share " + Mathf.RoundToInt(share * 100f) + "%: " + nearHave
                                + " marker(s) within " + radius.ToString("0") + " m of the HVT, took the " + nearTook
                                + " nearest; " + rest + " spread over the other " + (had - nearTook) + ".");
                    }
                }
                float chanceWas = 0f;
                try { chanceWas = raid.spawnChancePerPoint; } catch { }
                try { raid.spawnChancePerPoint = 1f; } catch { }

                int backupWas = 0; bool reinfWas = false, timedWas = false;
                try
                {
                    backupWas = raid.minAIBackupAmount;
                    reinfWas = raid.hasReinforcements;
                    timedWas = raid.timedBackup;
                    raid.minAIBackupAmount = 0;
                    raid.hasReinforcements = false;
                    raid.timedBackup = false;
                }
                catch { }

                string diff = "unknown";
                try
                {
                    var gm = Il2Cpp.GameManager.instance;
                    if (gm != null)
                    {
                        int now = (int)gm.AI_Difficulty;
                        if (CustomOps.LaunchDifficulty >= 0 && now != CustomOps.LaunchDifficulty)
                        {
                            gm.AI_Difficulty = (Il2Cpp.GameManager.AIDifficulty)CustomOps.LaunchDifficulty;
                            diff = CustomOps.LaunchDifficultyName + " (GameManager held "
                                 + ((Il2Cpp.GameManager.AIDifficulty)now).ToString() + "; set)";
                        }
                        else diff = gm.AI_Difficulty.ToString() + " (GameManager agrees)";
                    }
                }
                catch (Exception de) { diff = "unreadable: " + de.Message; }
                {
                    _waveTrimmed = total;
                    _log?.Msg("[mapai] OPFOR COUNT " + total + " = wave " + fullWave + " + HVT " + hvt
                            + (alive > 0 ? " (" + alive + " already alive, spawning " + missing + ")" : "")
                            + ". botSpawnPoints " + had + " -> "
                            + (raid.botSpawnPoints != null ? raid.botSpawnPoints.Count : 0)
                            + ", spawnChancePerPoint " + chanceWas.ToString("0.00")
                            + " -> 1.00; reinforcements OFF (were hasReinforcements=" + reinfWas
                            + ", timedBackup=" + timedWas + ", minAIBackupAmount=" + backupWas
                            + "); OPFOR DIFFICULTY " + diff + ". Spawn guard: ours on this map "
                            + "(a point is refused only with a player within 3 m).");
                }
            }
            catch (Exception e) { _log?.Warning("[mapai] MatchWaveToSlider: " + e.Message); }
            return true;
        }

        static int _seamInstalled;
        static int _spawnDealt;
        static int _spawnRewrites, _spawnLeft;
        static System.Collections.Generic.List<string> _spawnExamples =
            new System.Collections.Generic.List<string>();

        static readonly System.Collections.Generic.Dictionary<int, int> _markerClaim =
            new System.Collections.Generic.Dictionary<int, int>();
        static int _spawnCrowded;
        static int _spawnOversubscribed;
        static int _spawnBeside;
        static int _spawnBesideForced;
        static readonly System.Collections.Generic.List<Vector3> _placedThisWave =
            new System.Collections.Generic.List<Vector3>();

        public static void ResetSpawnSeam()
        {
            _spawnDealt = 0; _spawnRewrites = 0; _spawnLeft = 0;
            _spawnCrowded = 0; _spawnOversubscribed = 0;
            _spawnBeside = 0; _spawnBesideForced = 0;
            _placedThisWave.Clear();
            _markerClaim.Clear();
            _idleReported = false;
            _spawnExamples.Clear();
        }

        static bool DealToMarker(Transform root, string seam)
        {
            var points = FindGroup(SpawnGroup);
            if (points.Count == 0) return false;

            var pos = root.position;

            Vector3 hvtSpot;
            if (MapRuntime.TryMarkedHvt(out hvtSpot))
            {
                var dh = pos - hvtSpot; dh.y = 0f;
                if (dh.sqrMagnitude < 16f) { _spawnLeft++; return false; }
            }

            int me = root.GetInstanceID();

            int nearest = -1; float nearestSq = 4f;
            for (int i = 0; i < points.Count; i++)
            {
                var pt = points[i];
                if (pt == null) continue;
                var d = pos - pt.position; d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq < nearestSq) { nearestSq = sq; nearest = i; }
            }
            if (nearest >= 0)
            {
                int owner;
                if (!_markerClaim.TryGetValue(nearest, out owner) || owner == me)
                {
                    _markerClaim[nearest] = me;
                    _spawnLeft++;
                    return false;
                }

                if (RoomAround(pos, root) >= 1.2f)
                {
                    _spawnLeft++;
                    return false;
                }
                _spawnCrowded++;
            }

            Transform target = null;
            int chosen = -1;
            for (int step = 0; step < points.Count; step++)
            {
                int i = (_spawnDealt + step) % points.Count;
                var cand = points[i];
                if (cand == null) continue;
                if (_markerClaim.ContainsKey(i)) continue;
                target = cand; chosen = i; break;
            }
            bool oversubscribed = false;
            if (target == null)
            {

                chosen = _spawnDealt % points.Count;
                target = points[chosen];
                _spawnOversubscribed++;
                oversubscribed = true;
            }
            if (target == null) return false;
            _markerClaim[chosen] = me;
            _spawnDealt++;

            var np = oversubscribed ? Beside(target.position, root) : Grounded(target.position);
            if (!oversubscribed) _placedThisWave.Add(np);
            if (_spawnExamples.Count < 3)
                _spawnExamples.Add("(" + pos.x.ToString("F0") + ","
                    + pos.y.ToString("F0") + "," + pos.z.ToString("F0")
                    + ")->(" + np.x.ToString("F0") + "," + np.y.ToString("F0")
                    + "," + np.z.ToString("F0") + ")");
            root.position = np;
            root.rotation = target.rotation;
            _spawnRewrites++;
            if (_spawnRewrites == 1 || _spawnRewrites % 25 == 0)
                _log?.Msg("[mapai] spawn seam(" + seam + "): rewrite #" + _spawnRewrites
                        + " " + _spawnExamples[0]);
            return true;
        }

        static void Pre_NetworkSpawn(GameObject __0)
        {
            try
            {
                if (__0 != null && (__0.GetComponentInChildren<Il2Cpp.BrainAI>(true) != null
                                    || __0.GetComponent<Il2Cpp.AgentController>() != null))
                    _bornHere.Add(__0.transform.root.GetInstanceID());
            }
            catch { }
            try
            {
                if (__0 == null) return;

                if (!MapRuntime.Building && !MapRuntime.Done) return;
                if (!Il2CppMirror.NetworkServer.active) return;
                Il2Cpp.BrainAI brain = null;
                try { brain = __0.GetComponentInChildren<Il2Cpp.BrainAI>(true); } catch { }
                if (brain == null) return;
                DealToMarker(__0.transform, "v2");
            }
            catch { }
        }

        static void Pre_InstantiateBot(ref Vector3 position, ref Quaternion rotation)
        {
            try
            {
                if (!Il2CppMirror.NetworkServer.active) return;
                if (!MapRuntime.Done) return;
                var points = FindGroup(SpawnGroup);
                if (points.Count == 0) return;

                for (int i = 0; i < points.Count; i++)
                {
                    var pt = points[i];
                    if (pt == null) continue;
                    var d = position - pt.position; d.y = 0f;
                    if (d.sqrMagnitude < 4f) { _spawnLeft++; return; }
                }
                Vector3 hvtSpot;
                if (MapRuntime.TryMarkedHvt(out hvtSpot))
                {
                    var dh = position - hvtSpot; dh.y = 0f;
                    if (dh.sqrMagnitude < 16f) { _spawnLeft++; return; }
                }

                Transform target = null;
                for (int tries = 0; tries < points.Count; tries++)
                {
                    var cand = points[(_spawnDealt + tries) % points.Count];
                    if (cand == null) continue;
                    if (!Stranded(cand.position)) { target = cand; _spawnDealt += tries; break; }
                    _spawnSkippedHigh++;
                }
                if (target == null) return;
                bool secondLap = _spawnDealt >= points.Count;
                _spawnDealt++;
                Vector3 np;
                if (secondLap)
                {

                    np = Beside(target.position, null);
                }
                else
                {
                    np = Grounded(target.position);
                    _placedThisWave.Add(np);
                }
                if (_spawnExamples.Count < 3)
                    _spawnExamples.Add("(" + position.x.ToString("F0") + ","
                        + position.y.ToString("F0") + "," + position.z.ToString("F0")
                        + ")->(" + np.x.ToString("F0") + "," + np.y.ToString("F0")
                        + "," + np.z.ToString("F0") + ")");
                position = np;
                rotation = target.rotation;
                _spawnRewrites++;
                if (_spawnRewrites == 1 || _spawnRewrites % 25 == 0)
                    _log?.Msg("[mapai] spawn seam: rewrite #" + _spawnRewrites
                            + (_spawnSkippedHigh > 0 ? "  (" + _spawnSkippedHigh
                               + " marker(s) skipped: an upper storey nobody can climb to, "
                               + "or nothing of ours to stand on -- a mounted gunner dealt "
                               + "there hangs in the air)" : "")
                            + " " + _spawnExamples[0]
                            + " (bots are BORN at our markers now, so the spawn "
                            + "payload carries the right place to every machine)");
            }
            catch (Exception e)
            {

                if (_spawnRewrites == 0 && _spawnLeft == 0)
                    _log?.Warning("[mapai] spawn seam: " + e.Message);
            }
        }

        static string _markersGroundedScene = "";

        static void GroundMarkersOnce(System.Collections.Generic.List<Transform> markers)
        {
            try
            {
                if (!MapRuntime.Done) return;
                string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                if (scene == _markersGroundedScene) return;
                _markersGroundedScene = scene;

                var hits = new Il2CppInterop.Runtime.InteropTypes.Arrays
                               .Il2CppStructArray<RaycastHit>(16L);
                int moved = 0, noFloor = 0;
                float worst = 0f;

                for (int i = 0; i < markers.Count; i++)
                {
                    var t = markers[i];
                    if (t == null) continue;
                    var p = t.position;
                    float best = float.NegativeInfinity;
                    try
                    {
                        int n = Physics.RaycastNonAlloc(p + Vector3.up * 2.5f, Vector3.down,
                                                        hits, 42f, ~0,
                                                        QueryTriggerInteraction.Ignore);
                        for (int k = 0; k < n && k < hits.Length; k++)
                        {
                            var c = hits[k].collider;
                            if (c == null) continue;

                            try { if (c.GetComponentInParent<Il2Cpp.Health>() != null) continue; } catch { }
                            try { if (c.transform.IsChildOf(t)) continue; } catch { }
                            float y = hits[k].point.y;
                            if (y > p.y + 0.1f) continue;
                            if (y > best) best = y;
                        }
                    }
                    catch { }

                    if (float.IsNegativeInfinity(best)) { noFloor++; continue; }
                    float drop = p.y - best;

                    if (drop < 2.0f) continue;
                    try { t.position = new Vector3(p.x, best, p.z); } catch { continue; }
                    moved++;
                    if (drop > worst) worst = drop;
                }

                if (moved > 0 || noFloor > 0)
                    _log?.Msg("[mapai] grounded " + moved + " of " + markers.Count
                            + " spawn marker(s) onto our floor, worst drop " + worst.ToString("F1")
                            + " m; " + noFloor + " had no floor of ours within 42 m and were left "
                            + "alone. A marker over the deck is a bot hanging in the air on every "
                            + "screen, and the placement pass never corrects one that has ground "
                            + "somewhere below it.");
            }
            catch (Exception e) { _log?.Warning("[mapai] ground markers: " + e.Message); }
        }

        public static int Apply(string why)
        {
            try
            {
                var raid = Il2Cpp.RaidManager.singleton;
                if (raid == null) return 0;

                var markers = FindGroup(SpawnGroup);
                if (markers.Count == 0)
                {

                    return 0;
                }
                GroundMarkersOnce(markers);

                var list = raid.botSpawnPoints;
                if (list == null)
                {
                    list = new L.List<GameObject>();
                    raid.botSpawnPoints = list;
                }

                if (list.Count >= markers.Count && _lastApplied == markers.Count)
                    return list.Count;

                list.Clear();
                int added = 0;
                foreach (var t in markers)
                {
                    if (t == null) continue;
                    EnsureDetails(t.gameObject);
                    list.Add(t.gameObject);
                    added++;
                }

                _lastApplied = added;

                int extra = 0;
                try
                {
                    int ask = CustomOps.LaunchAiCount;
                    if (ask > added && added > 0)
                    {
                        int k = 0;
                        while (list.Count < ask && extra < added * 6)
                        {
                            var t = markers[k % markers.Count];
                            k++;
                            if (t == null) continue;
                            list.Add(t.gameObject);
                            extra++;
                        }
                    }
                }
                catch { }
                _guardAnswered = 0; _guardRefused = 0;
                _log?.Msg("[mapai] " + why + ": botSpawnPoints <- " + added
                        + " marker(s) from " + SpawnGroup
                        + (extra > 0 ? " + " + extra + " repeated so the list holds the "
                           + "asked-for " + CustomOps.LaunchAiCount + " (the game spawns one bot "
                           + "per point and stops when the list is empty)" : ""));

                MapPatrols.AssignToSpawnPoints();
                return added;
            }
            catch (Exception e)
            {
                _log?.Error("[mapai] Apply: " + e.Message);
                return 0;
            }
        }

        public static void EnsureDetails(GameObject go)
        {
            try
            {
                var d = go.GetComponent<Il2Cpp.BotSpawnDetails>();
                if (d != null) return;
                d = go.AddComponent<Il2Cpp.BotSpawnDetails>();
                if (d == null) return;

                d.DetectionRange = 80f;
                d.HearingRange = 40f;
                d.FOV = 110f;
                d.maxEffectiveRange = 150f;
                d.DetectionTimeMultiplier = 1f;
                d.useComms = true;

                float wd = MapIdentity.WanderDistance > 0f ? MapIdentity.WanderDistance : 12f;
                d.WanderDistance = (int)Math.Round(wd);
                d.PatrolSpeed = 1.2f;
                d.patrolWaitTime = 2f;

                d.idleState = IdleWanted;
                ReportIdle(d);
            }
            catch (Exception e) { _log?.Warning("[mapai] EnsureDetails: " + e.Message); }
        }

        public static int IdleMode = 1;

        static Il2Cpp.BrainAI.IdleStates IdleWanted
        {
            get
            {
                switch (IdleMode)
                {
                    case 0:  return Il2Cpp.BrainAI.IdleStates.Idle;
                    case 2:  return Il2Cpp.BrainAI.IdleStates.Patrol;
                    case 3:  return Il2Cpp.BrainAI.IdleStates.Random;
                    default: return Il2Cpp.BrainAI.IdleStates.Wander;
                }
            }
        }

        static bool _idleReported;

        static void ReportIdle(Il2Cpp.BotSpawnDetails d)
        {
            if (_idleReported || d == null) return;
            _idleReported = true;
            try
            {
                var got = d.idleState;
                _log?.Msg("[mapai] idle behaviour: asked for " + IdleWanted + ", the component "
                        + "reads back " + got + (got == IdleWanted ? "" : "  <- IT DID NOT STICK")
                        + ". WanderDistance=" + d.WanderDistance + " m, PatrolSpeed="
                        + d.PatrolSpeed.ToString("F1") + ". Idle means they stand where they "
                        + "spawn; Wander means they walk around near it.");
            }
            catch (Exception e) { _log?.Warning("[mapai] idle read-back: " + e.Message); }
        }

        public static System.Collections.Generic.List<Transform> FindGroup(string group)
        {
            var found = new System.Collections.Generic.List<Transform>();
            try
            {

                var direct = MapRuntime.MarkersRoot;
                if (direct != null)
                {
                    var dg = direct.Find(group);
                    if (dg != null)
                    {
                        for (int c = 0; c < dg.childCount; c++) found.Add(dg.GetChild(c));
                        return found;
                    }
                }

                var scene = SceneManager.GetActiveScene();
                if (!scene.IsValid()) return found;

                var roots = scene.GetRootGameObjects();
                if (roots == null) return found;

                for (int i = 0; i < roots.Count; i++)
                {
                    var r = roots[i];
                    if (r == null) continue;

                    var m = r.name == "MARKERS" ? r.transform : r.transform.Find("MARKERS");
                    if (m == null) continue;
                    var g = m.Find(group);
                    if (g == null) continue;
                    for (int c = 0; c < g.childCount; c++) found.Add(g.GetChild(c));
                }
            }
            catch (Exception e) { _log?.Warning("[mapai] FindGroup " + group + ": " + e.Message); }
            return found;
        }

        public static bool SpawnNow()
        {
            try
            {
                if (!Il2CppMirror.NetworkServer.active)
                {

                    _log?.Msg("[mapai] client: the host owns the AI, nothing to do here");
                    return false;
                }

                var raid = Il2Cpp.RaidManager.singleton;
                if (raid == null) { _log?.Warning("[mapai] no RaidManager yet"); return false; }

                int pts = Apply("SpawnNow");
                if (pts == 0)
                {
                    _log?.Warning("[mapai] no " + SpawnGroup + " markers, so nothing to spawn from. "
                                + "Is the map built?");
                    return false;
                }

                int prefabs = 0;
                try { prefabs = raid.standardAI != null ? raid.standardAI.Length : 0; } catch { }
                if (prefabs == 0)
                {
                    _log?.Error("[mapai] RaidManager.standardAI is EMPTY -- there is no bot prefab "
                              + "to instantiate. Spawn points alone produce nothing. Launch from a "
                              + "PVE operation rather than an empty training level.");
                    return false;
                }

                int removed = 0;
                try
                {
                    var agents = UnityEngine.Object.FindObjectsOfType<Il2Cpp.AgentController>();
                    if (agents != null)
                        for (int i = 0; i < agents.Length; i++)
                        {
                            var a = agents[i];
                            if (a == null || a.gameObject == null) continue;
                            if (!a.gameObject.scene.IsValid()) continue;
                            var ni = a.gameObject.GetComponent<Il2CppMirror.NetworkIdentity>();
                            if (ni == null) continue;
                            Il2CppMirror.NetworkServer.Destroy(a.gameObject);
                            removed++;
                        }
                }
                catch (Exception e) { _log?.Warning("[mapai] clearing old bots: " + e.Message); }
                if (removed > 0) _log?.Msg("[mapai] removed " + removed + " bot(s) from the host level");

                try { CustomOps.ApplyChosenAiCount(_log); } catch { }
                _log?.Msg("[mapai] spawning AI: " + pts + " point(s), " + prefabs + " prefab(s)");
                _purgeGunnersAt = Time.time + 6f;
                try { raid.ServerSpawnAI(false); }
                catch (Exception e)
                {
                    _log?.Warning("[mapai] ServerSpawnAI: " + e.Message + "; trying RespawnAI");
                    try { raid.RespawnAI(); }
                    catch (Exception e2) { _log?.Error("[mapai] RespawnAI: " + e2.Message); return false; }
                }

                MapPatrols.SweepBrains();

                try { raid.UpdateAICount(); } catch { }
                _log?.Msg("[mapai] AI spawn requested");

                MelonLoader.MelonCoroutines.Start(RelocateUntilSettled());
                return true;
            }
            catch (Exception e) { _log?.Error("[mapai] SpawnNow: " + e.Message); return false; }
        }

        static readonly System.Collections.Generic.HashSet<int> _placed
            = new System.Collections.Generic.HashSet<int>();
        static int _dealt;

        public static void ResetPlacement()
        {
            _placed.Clear(); _dealt = 0;
            _foundFar = 0; _leftAlone = 0; _farExamples.Clear();
        }

        static float _purgeGunnersAt = -1f;
        static readonly string[] GunnerWords =
            { "mounted", "turret", "staticgun", "static_gun", "dshk", "browning",
              "m2hb", "technical", "emplacement", "gunner" };

        static bool LooksLikeMountedCrew(string n)
        {
            if (string.IsNullOrEmpty(n)) return false;
            var low = n.ToLowerInvariant();
            for (int i = 0; i < GunnerWords.Length; i++)
                if (low.Contains(GunnerWords[i])) return true;

            if (low.Contains(".50") || low.Contains("50cal") || low.Contains("cal50")) return true;
            return false;
        }

        public static void PurgeGunnersTick()
        {
            if (_purgeGunnersAt < 0f || Time.time < _purgeGunnersAt) return;
            _purgeGunnersAt = -1f;
            try
            {
                if (!Il2CppMirror.NetworkServer.active) return;
                var agents = UnityEngine.Object.FindObjectsOfType<Il2Cpp.AgentController>();
                int killed = 0;
                var names = new System.Collections.Generic.List<string>();
                for (int i = 0; i < (agents != null ? agents.Length : 0); i++)
                {
                    var a = agents[i];
                    if (a == null || a.gameObject == null) continue;
                    if (!a.gameObject.scene.IsValid()) continue;
                    string nm = "";
                    try { nm = a.gameObject.name ?? ""; } catch { }
                    if (!LooksLikeMountedCrew(nm)) continue;
                    if (a.gameObject.GetComponent<Il2CppMirror.NetworkIdentity>() == null) continue;
                    try { Il2CppMirror.NetworkServer.Destroy(a.gameObject); killed++;
                          if (names.Count < 6) names.Add(nm); }
                    catch { }
                }
                if (killed > 0)
                    _log?.Msg("[mapai] removed " + killed + " mounted-gun crew member(s) on load: "
                            + string.Join(", ", names.ToArray())
                            + ". They cannot hold this map's floor, so they fall, get rescued and "
                            + "fall again, which every other machine sees as an invisible enemy.");
                else
                    _log?.Msg("[mapai] no mounted-gun crew matched by name. If the .50 gunners are "
                            + "still there, the retire-after-4 rule will still take them, and the "
                            + "names to add are in the next [float] lines.");
            }
            catch (Exception e) { _log?.Warning("[mapai] gunner purge: " + e.Message); }
        }

        static int _spawnSkippedHigh;

        static readonly System.Collections.Generic.Dictionary<uint, int> _rescuedTotal =
            new System.Collections.Generic.Dictionary<uint, int>();

        const int MaxLifetimeRescues = 2;

        static bool Stranded(Vector3 p)
        {
            try
            {
                if (MapRuntime.Root == null) return false;
                var hits = new Il2CppInterop.Runtime.InteropTypes.Arrays
                               .Il2CppStructArray<RaycastHit>(48L);
                int n = Physics.RaycastNonAlloc(p + Vector3.up * 0.5f, Vector3.down,
                                                hits, 200f, ~0,
                                                QueryTriggerInteraction.Ignore);
                int floors = 0; float lowest = p.y;
                for (int i = 0; i < n && i < hits.Length; i++)
                {
                    var h = hits[i];

                    var t = h.collider != null ? h.collider.transform : null;
                    if (t == null || !t.IsChildOf(MapRuntime.Root.transform)) continue;
                    if (h.point.y > p.y - 0.6f) continue;
                    floors++;
                    if (h.point.y < lowest) lowest = h.point.y;
                }
                if (floors >= 2 && (p.y - lowest) > 9f) return true;

                int m = Physics.RaycastNonAlloc(p + Vector3.up * 0.5f, Vector3.down,
                                                hits, 12f, ~0,
                                                QueryTriggerInteraction.Ignore);
                for (int i = 0; i < m && i < hits.Length; i++)
                {
                    var t2 = hits[i].collider != null ? hits[i].collider.transform : null;
                    if (t2 != null && t2.IsChildOf(MapRuntime.Root.transform)) return false;
                }
                return true;
            }
            catch { return false; }
        }

        static bool RealSurface(Collider c)
        {
            if (c == null) return false;
            try
            {
                var root = MapRuntime.Root;
                if (root != null && c.transform.IsChildOf(root.transform)) return true;
                if (MapIdentity.HostLevelIsMap)
                {
                    if (ShellSwap.IsAdditiveWorld(c.gameObject.scene)) return true;
                    try { if (c.TryCast<TerrainCollider>() != null) return true; } catch { }
                }
            }
            catch { }
            return false;
        }

        static bool TryGround(Vector3 p, out Vector3 g)
        {
            g = p;
            try
            {
                var hits = new Il2CppInterop.Runtime.InteropTypes.Arrays
                               .Il2CppStructArray<RaycastHit>(24L);
                var from = new Vector3(p.x, p.y + 0.6f, p.z);
                int n = Physics.RaycastNonAlloc(from, Vector3.down, hits, 2.2f, ~0,
                                                QueryTriggerInteraction.Ignore);
                float best = float.NegativeInfinity;
                for (int i = 0; i < n && i < hits.Length; i++)
                {
                    var h = hits[i];
                    if (!RealSurface(h.collider)) continue;
                    if (h.point.y > p.y + 0.5f || h.point.y < p.y - 1.5f) continue;
                    if (h.point.y > best) best = h.point.y;
                }
                if (best > float.NegativeInfinity) { g = new Vector3(p.x, best + 0.05f, p.z); return true; }
            }
            catch { }
            return false;
        }

        static bool Headroom(Vector3 g)
        {
            try
            {
                RaycastHit hit;
                if (Physics.Raycast(g + Vector3.up * 0.3f, Vector3.up, out hit, 1.7f, ~0,
                                    QueryTriggerInteraction.Ignore))
                    return false;
            }
            catch { }
            return true;
        }

        static bool ClearAround(Vector3 g)
        {
            try
            {
                var cols = Physics.OverlapSphere(g + Vector3.up * 1.0f, 0.35f, ~0,
                                                 QueryTriggerInteraction.Ignore);
                for (int i = 0; i < (cols != null ? cols.Length : 0); i++)
                    if (cols[i] != null) return false;
            }
            catch { }
            return true;
        }

        static float RoomAround(Vector3 g, Transform self)
        {
            float best = float.MaxValue;
            for (int i = 0; i < _placedThisWave.Count; i++)
            {
                var d = g - _placedThisWave[i]; d.y = 0f;
                if (d.magnitude < best) best = d.magnitude;
            }
            try
            {
                var bots = UnityEngine.Object.FindObjectsOfType<Il2Cpp.BrainAI>();
                for (int i = 0; i < (bots != null ? bots.Length : 0); i++)
                {
                    var b = bots[i];
                    if (b == null) continue;
                    var r = b.transform.root;
                    if (self != null && r == self) continue;
                    var d = g - r.position; d.y = 0f;
                    if (d.magnitude < best) best = d.magnitude;
                }
            }
            catch { }
            return best;
        }

        static Vector3 Beside(Vector3 marker, Transform self)
        {
            Vector3 fallback = marker; float fallbackRoom = -1f; bool any = false;
            int seed = _spawnDealt * 7 + _placedThisWave.Count * 3;
            for (int k = 0; k < 12; k++)
            {
                float ang = (seed + k) * 2.399963f;
                float rad = 1.6f + 0.25f * (k % 5);
                var cand = marker + new Vector3((float)Math.Cos(ang) * rad, 0f, (float)Math.Sin(ang) * rad);
                Vector3 g;
                if (!TryGround(cand, out g)) continue;
                if (!Headroom(g)) { _spawnLowRoof++; continue; }
                float room = RoomAround(g, self);
                if (room > fallbackRoom) { fallbackRoom = room; fallback = g; any = true; }
                if (room < 1.3f) continue;
                if (!ClearAround(g)) continue;
                _spawnBeside++;
                _placedThisWave.Add(g);
                return g;
            }
            _spawnBesideForced++;
            if (!any)
            {

                fallback = marker + new Vector3(1.6f, 0f, 0f);
            }
            _placedThisWave.Add(fallback);
            return fallback;
        }

        static float ClosestPair()
        {
            float best = float.MaxValue;
            try
            {
                var bots = UnityEngine.Object.FindObjectsOfType<Il2Cpp.BrainAI>();
                var ps = new System.Collections.Generic.List<Vector3>();
                for (int i = 0; i < (bots != null ? bots.Length : 0); i++)
                    if (bots[i] != null) ps.Add(bots[i].transform.root.position);
                for (int i = 0; i < ps.Count; i++)
                    for (int j = i + 1; j < ps.Count; j++)
                    {
                        var d = ps[i] - ps[j]; d.y = 0f;
                        if (d.magnitude < best) best = d.magnitude;
                    }
            }
            catch { }
            return best == float.MaxValue ? 0f : best;
        }

        static Vector3 Grounded(Vector3 p)
        {
            try
            {
                var root = MapRuntime.Root;
                if (root == null) return p + Vector3.up * 0.1f;

                float startY = p.y + 0.5f;
                var hits = new Il2CppInterop.Runtime.InteropTypes.Arrays
                               .Il2CppStructArray<RaycastHit>(32L);
                int n = Physics.RaycastNonAlloc(new Vector3(p.x, startY, p.z), Vector3.down,
                                                hits, 12f, ~0,
                                                QueryTriggerInteraction.Ignore);
                float best = float.NegativeInfinity;
                for (int i = 0; i < n && i < hits.Length; i++)
                {
                    var h = hits[i];
                    var c = h.collider;
                    if (c == null) continue;

                    try { if (!c.transform.IsChildOf(root.transform)) continue; } catch { continue; }
                    if (h.point.y > startY) continue;
                    if (h.point.y > best) best = h.point.y;
                }
                if (best > float.NegativeInfinity)
                    return new Vector3(p.x, best + 0.1f, p.z);
            }
            catch { }

            return p + Vector3.up * 0.1f;
        }

        static int _netTold, _netMissing;
        static int _netComponents;

        static int _foundFar;
        static int _leftAlone;
        static readonly System.Collections.Generic.List<string> _farExamples =
            new System.Collections.Generic.List<string>();

        public static void AnnounceTeleportPublic(Transform root, Vector3 pos, Quaternion rot)
        {
            AnnounceTeleport(root, pos, rot);
        }

        static bool AnnounceTeleport(Transform root, Vector3 pos, Quaternion rot)
        {
            try
            {

                var all = root.GetComponentsInChildren<Il2CppSmooth.SmoothSyncMirror>(true);
                if (all == null || all.Length == 0) { _netMissing++; return false; }
                int told = 0;
                for (int i = 0; i < all.Length; i++)
                {
                    var ss = all[i];
                    if (ss == null) continue;

                    try { ss.setPosition(pos, true); } catch { }
                    try { ss.setRotation(rot, true); } catch { }
                    try { ss.teleportAnyObjectFromServer(pos, rot, root.localScale); } catch { }
                    try { ss.forceStateSendNextFixedUpdate(); } catch { }
                    told++;
                }
                if (told == 0) { _netMissing++; return false; }
                _netTold++;
                _netComponents += told;
                return true;
            }
            catch (Exception e)
            {
                _netMissing++;
                if (_netMissing <= 3) _log?.Warning("[mapai] teleport announce: " + e.Message);
                return false;
            }
        }

        public static int RelocateExistingAI()
        {
            int moved = 0;
            try
            {
                if (!Il2CppMirror.NetworkServer.active) return 0;

                var points = FindGroup(SpawnGroup);
                if (points.Count == 0) return 0;

                var bots = UnityEngine.Object.FindObjectsOfType<Il2Cpp.BrainAI>();
                if (bots == null || bots.Length == 0) return 0;

                for (int i = 0; i < bots.Length; i++)
                {
                    var b = bots[i];
                    if (b == null) continue;
                    Transform root = null;
                    try { root = b.transform.root; } catch { }
                    if (root == null) continue;

                    bool isHvt = false;
                    try { isHvt = b.isHVT; } catch { }
                    if (isHvt) continue;

                    if (!_placed.Add(root.GetInstanceID())) continue;

                    bool lost = false;
                    try
                    {
                        var here = root.position;
                        var probe = new Il2CppInterop.Runtime.InteropTypes.Arrays
                                        .Il2CppStructArray<RaycastHit>(8L);
                        int hn = Physics.RaycastNonAlloc(here + Vector3.up * 1f, Vector3.down,
                                                         probe, 80f, ~0,
                                                         QueryTriggerInteraction.Ignore);
                        bool ground = false;
                        for (int k = 0; k < hn && k < probe.Length; k++)
                        {
                            var cc = probe[k].collider;
                            if (cc == null) continue;
                            try { if (cc.transform.root == root) continue; } catch { continue; }
                            ground = true; break;
                        }
                        if (!ground) lost = true;

                        if (!lost)
                        {
                            var mb = MapRuntime.Bounds();
                            var flat = new Vector3(here.x - mb.center.x, 0f, here.z - mb.center.z);
                            if (Mathf.Abs(flat.x) > mb.extents.x + 10f ||
                                Mathf.Abs(flat.z) > mb.extents.z + 10f) lost = true;
                        }
                    }
                    catch { }
                    if (!lost) { _leftAlone++; continue; }

                    var p = points[_dealt % points.Count];
                    if (p == null) continue;
                    try
                    {
                        var was = root.position;
                        var np = Grounded(p.position);
                        if ((was - np).sqrMagnitude > 4f)
                        {
                            _foundFar++;
                            if (_farExamples.Count < 4)
                                _farExamples.Add("(" + was.x.ToString("F0") + ","
                                    + was.y.ToString("F0") + "," + was.z.ToString("F0")
                                    + ")->(" + np.x.ToString("F0") + ","
                                    + np.y.ToString("F0") + "," + np.z.ToString("F0") + ")");
                        }
                        root.position = np;
                        root.rotation = p.rotation;

                        AnnounceTeleport(root, np, p.rotation);
                        _dealt++;
                        moved++;
                    }
                    catch { }
                }
            }
            catch (Exception e) { _log?.Warning("[mapai] RelocateExistingAI: " + e.Message); }
            return moved;
        }

        public static System.Collections.IEnumerator RelocateUntilSettled()
        {
            ResetPlacement();
            int total = 0, quiet = 0, sweeps = 0;
            float deadline = Time.realtimeSinceStartup + 15f;

            while (Time.realtimeSinceStartup < deadline && quiet < 2)
            {
                int n = RelocateExistingAI();
                total += n;
                sweeps++;
                quiet = n == 0 ? quiet + 1 : 0;
                yield return new WaitForSeconds(0.25f);
            }

            var points = FindGroup(SpawnGroup);
            _log?.Msg("[mapai] relocated " + total + " bot(s) across " + points.Count
                    + " spawn point(s) over " + sweeps + " sweep(s). Sweeping rather "
                    + "than placing once is what catches the bots the game had not "
                    + "created yet when the map finished building.");

            _log?.Msg("[mapai] network: told " + _netTold + " teleport(s) across "
                    + _netComponents + " SmoothSync component(s), " + _netMissing
                    + " bot(s) had none to tell. A bot's position is NOT a SyncVar, "
                    + "so an untold move is a bot only the host can see.");
            _log?.Msg("[mapai] left alone: " + _leftAlone + " bot(s) had ground under "
                    + "them and were NOT moved, wherever they had walked to. Only bots "
                    + "with nothing beneath them, or outside the map, are rescued now: "
                    + "one round showed every bot this sweep moved was also a bot the two "
                    + "machines disagreed about, and every bot it left alone agreed.");
            _log?.Msg("[mapai] spawn origin: " + _foundFar + " of " + total
                    + " bot(s) were over 2 m from their marker when first found"
                    + (_farExamples.Count > 0
                        ? ". Examples: " + string.Join("  ", _farExamples.ToArray())
                        : "")
                    + ". Near zero means the game spawned them at OUR points and only "
                    + "the shuffle diverges; near all means they spawned in the HIDDEN "
                    + "level, and a client that misses the correction sees exactly that.");
            _log?.Msg("[mapai] spawn seam: " + _spawnRewrites + " spawn(s) rewritten to "
                    + "our markers at birth, " + _spawnLeft + " already aimed right"
                    + (_spawnExamples.Count > 0
                        ? ". Examples: " + string.Join("  ", _spawnExamples.ToArray()) : "")
                    + ". If the spawn-origin count above is near zero, the seam is doing "
                    + "the placing and the sweep has nothing left to fix.");

            _log?.Msg("[mapai] spread: " + _markerClaim.Count + " of " + points.Count
                    + " marker(s) ended up holding a bot. " + _spawnCrowded
                    + " spawn(s) were aimed at a marker another bot already had and were "
                    + "sent to a free one instead; " + _spawnOversubscribed
                    + " found every marker taken and stood BESIDE one on checked ground ("
                    + _spawnBeside + " passed every test, " + _spawnBesideForced
                    + " took the roomiest spot on offer; " + _spawnLowRoof
            + " spot(s) refused for no headroom). Closest two bots on the map: "
                    + ClosestPair().ToString("0.0") + " m apart.");

            int movedCount = total;
            int seenCount = movedCount + _leftAlone;
            if (seenCount == 0)
                _log?.Msg("[VERDICT] enemy placement: no bots to judge this wave.");
            else if (movedCount == 0)
                _log?.Msg("[VERDICT] enemy placement: PASS -- all " + seenCount + " bot(s) "
                        + "were already standing on a marker and none had to be moved, so "
                        + "every machine agrees on where they are.");
            else if (movedCount * 5 < seenCount)
                _log?.Msg("[VERDICT] enemy placement: GOOD -- " + movedCount + " of "
                        + seenCount + " bot(s) needed moving. Those few will look misplaced "
                        + "to everyone but the host; the rest agree.");
            else
                _log?.Error("[VERDICT] enemy placement: POOR -- " + movedCount + " of "
                          + seenCount + " bot(s) had fallen out of the world and were put "
                          + "back. Each rescue is a bot other players may see elsewhere, "
                          + "but the alternative is a bot nobody can shoot. A high count "
                          + "here means bots are falling through the floor, which is a "
                          + "different bug from where they spawn. Send this log.");

            if (_seamInstalled <= 0 && movedCount > 0)
                _log?.Warning("[mapai] the NetworkServer.Spawn seam is not installed, so "
                            + "nothing caught those " + movedCount + " bot(s) at birth.");

            _netTold = 0; _netMissing = 0; _netComponents = 0;
        }

        const float FloatClearance = 4f;
        static readonly System.Collections.Generic.Dictionary<uint, int> _noFloorStrikes =
            new System.Collections.Generic.Dictionary<uint, int>();
        const float FloatProbe = 60f;
        const float FloatDwell = 3f;

        static float _nextFloatScan;
        static readonly System.Collections.Generic.Dictionary<uint, float> _airborneSince =
            new System.Collections.Generic.Dictionary<uint, float>();
        static int _rescued, _rescueDealt;
        public static int RescuedCount { get { return _rescued; } }

        static readonly System.Collections.Generic.HashSet<uint> _floatReported =
            new System.Collections.Generic.HashSet<uint>();

        const int MaxRescueTries = 3;
        static readonly System.Collections.Generic.Dictionary<uint, int> _rescueTries =
            new System.Collections.Generic.Dictionary<uint, int>();
        static readonly System.Collections.Generic.HashSet<uint> _rescueGaveUp =
            new System.Collections.Generic.HashSet<uint>();
        public static int RescueGaveUpCount { get { return _rescueGaveUp.Count; } }

        static float _levelReadyAt;
        public static void MarkLevelReady() { _levelReadyAt = Time.realtimeSinceStartup; }

        public static void ResetFloaterWatch()
        {
            _nextFloatScan = 0f; _airborneSince.Clear(); _floatReported.Clear();
            _rescued = 0; _rescueDealt = 0;
            _rescueTries.Clear(); _rescuedTotal.Clear(); _rescueGaveUp.Clear();
        }

        static string LifeState(Il2Cpp.BrainAI b, Transform root)
        {
            try
            {
                var h = root.GetComponentInChildren<Il2Cpp.Health>(true);
                if (h == null) return "life=?";
                bool dead = false;
                try { dead = h.isDead; } catch { }
                string extra = "";
                if (dead)
                {
                    try { extra = " ragdollHP=" + h.RagdollHealth.ToString("F0"); } catch { }
                }
                return (dead ? "DEAD" : "alive") + extra;
            }
            catch { return "life=?"; }
        }

        static bool RenderCentre(Transform root, out Vector3 centre, out int rends)
        {
            centre = Vector3.zero; rends = 0;
            try
            {

                var skins = root.GetComponentsInChildren<SkinnedMeshRenderer>(false);
                if (skins != null)
                {
                    for (int i = 0; i < skins.Length; i++)
                    {
                        var sk = skins[i];
                        if (sk == null || !sk.enabled) continue;
                        var rb = sk.rootBone;
                        if (rb == null) continue;
                        centre = rb.position;
                        rends = skins.Length;
                        return true;
                    }
                }

                var all = root.GetComponentsInChildren<Renderer>(false);
                if (all == null || all.Length == 0) return false;
                bool any = false;
                Bounds b = new Bounds();
                for (int i = 0; i < all.Length; i++)
                {
                    var r = all[i];
                    if (r == null) continue;
                    if (!r.enabled) continue;
                    if (!any) { b = r.bounds; any = true; }
                    else b.Encapsulate(r.bounds);
                    rends++;
                }
                if (!any) return false;
                centre = b.center;
                return true;
            }
            catch { return false; }
        }

        public static void FloaterTick()
        {
            try
            {
                if (!MapRuntime.Done) return;
                float now = Time.realtimeSinceStartup;
                if (now < _nextFloatScan) return;
                _nextFloatScan = now + 2f;

                var bots = UnityEngine.Object.FindObjectsOfType<Il2Cpp.BrainAI>();
                if (bots == null || bots.Length == 0) return;

                var hits = new Il2CppInterop.Runtime.InteropTypes.Arrays
                               .Il2CppStructArray<RaycastHit>(8L);

                for (int i = 0; i < bots.Length; i++)
                {
                    var b = bots[i];
                    if (b == null) continue;
                    Transform root = null;
                    try { root = b.transform.root; } catch { }
                    if (root == null) continue;

                    uint id = 0;
                    try
                    {
                        var ni = root.GetComponent<Il2CppMirror.NetworkIdentity>();
                        if (ni != null) id = ni.netId;
                    }
                    catch { }
                    if (id == 0) continue;

                    var p = root.position;
                    float best = float.PositiveInfinity;
                    bool oursBelow = false;
                    try
                    {
                        int n = Physics.RaycastNonAlloc(p + Vector3.up * 0.5f, Vector3.down,
                                                        hits, FloatProbe,
                                                        ~0, QueryTriggerInteraction.Ignore);
                        for (int k = 0; k < n && k < hits.Length; k++)
                        {
                            var c = hits[k].collider;
                            if (c == null) continue;

                            try { if (c.transform.root == root) continue; } catch { continue; }
                            float d = hits[k].distance;
                            if (d < best) best = d;
                            if (!oursBelow)
                            {
                                try
                                {
                                    string cn = c.gameObject.name ?? "";
                                    bool water = cn.StartsWith("ODG_Ocean") || cn.StartsWith("ODG_PoolWater");
                                    if (!water && MapRuntime.SurfaceIsOurs(c)) oursBelow = true;
                                }
                                catch { }
                            }
                        }
                    }
                    catch { continue; }

                    if (Il2CppMirror.NetworkServer.active && !oursBelow && !MapIdentity.HostLevelIsMap)
                    {
                        bool hostHvt = false; try { hostHvt = b.isHVT; } catch { }
                        int strikes; _noFloorStrikes.TryGetValue(id, out strikes); strikes++;
                        _noFloorStrikes[id] = strikes;
                        if (!hostHvt && strikes >= 2)
                        {
                            string nm = ""; try { nm = root.name ?? ""; } catch { }
                            try { Il2CppMirror.NetworkServer.Destroy(root.gameObject); } catch (Exception de) { _log?.Warning("[float] remove host AI netId=" + id + ": " + de.Message); }
                            _noFloorStrikes.Remove(id); _rescueTries.Remove(id); _airborneSince.Remove(id); _rescuedTotal.Remove(id);
                            _log?.Msg("[float] netId=" + id + " '" + nm + "' at " + p.ToString("F1") + " stood on nothing of ours for two scans ("
                                    + (best < FloatProbe ? "the host level's ground " + best.ToString("F1") + " m below it" : "nothing within " + FloatProbe.ToString("0") + " m")
                                    + "): the host level's own AI, removed from every machine.");
                            continue;
                        }
                    }
                    else _noFloorStrikes.Remove(id);

                    bool inAir = best > FloatClearance;
                    if (!inAir) { _airborneSince.Remove(id); continue; }

                    if (_rescueGaveUp.Contains(id)) continue;

                    float since;
                    if (!_airborneSince.TryGetValue(id, out since))
                    { _airborneSince[id] = now; continue; }
                    if (now - since < FloatDwell) continue;
                    bool firstReport = _floatReported.Add(id);

                    if (Il2CppMirror.NetworkServer.active && MapRuntime.Done
                        && float.IsPositiveInfinity(best))
                    {
                        bool removed = false;
                        try
                        {
                            Il2CppMirror.NetworkServer.Destroy(root.gameObject);
                            removed = true;
                        }
                        catch (Exception de)
                        { _log?.Warning("[float] remove netId=" + id + ": " + de.Message); }
                        _rescueTries.Remove(id);
                        _airborneSince.Remove(id);
                        _rescuedTotal.Remove(id);
                        if (firstReport)
                            _log?.Warning("[float] netId=" + id + " had NO floor of ours anywhere "
                                        + "beneath it at " + p.ToString("F1") + ", so it is the host "
                                        + "level's own AI standing on the storey we hid, not one of "
                                        + "ours that fell. Rescuing it never held in any recorded "
                                        + "session and every attempt is a teleport the clients are "
                                        + "not told about, which is a body left hanging in the air "
                                        + "on their screens. "
                                        + (removed ? "Removed from every machine via Mirror."
                                                   : "Mirror refused the removal; it is still there."));
                        continue;
                    }

                    if (Il2CppMirror.NetworkServer.active && MapRuntime.Done)
                    {
                        var pts = FindGroup(SpawnGroup);
                        if (pts.Count > 0)
                        {

                            Transform tgt = null;
                            Vector3 np = Vector3.zero;
                            for (int attempt = 0; attempt < 8 && tgt == null; attempt++)
                            {
                                var cand = pts[_rescueDealt % pts.Count];
                                _rescueDealt++;
                                if (cand == null) continue;
                                var cp = Grounded(cand.position);
                                bool solid = false;
                                try
                                {
                                    var pr = new Il2CppInterop.Runtime.InteropTypes.Arrays
                                                 .Il2CppStructArray<RaycastHit>(4L);
                                    int pn = Physics.RaycastNonAlloc(cp + Vector3.up * 1f,
                                                                     Vector3.down, pr, 12f, ~0,
                                                                     QueryTriggerInteraction.Ignore);
                                    for (int k = 0; k < pn && k < pr.Length; k++)
                                    {
                                        var cc = pr[k].collider;
                                        if (cc == null) continue;
                                        try { if (cc.transform.root == root) continue; } catch { continue; }
                                        solid = true; break;
                                    }
                                }
                                catch { }
                                if (solid) { tgt = cand; np = cp; }
                            }
                            if (tgt == null)
                            {
                                if (firstReport)
                                    _log?.Warning("[float] netId=" + id + " could not be rescued: "
                                                + "eight markers in a row had no ground under "
                                                + "them either. The map's floor is missing where "
                                                + "those markers are, which is a bigger problem "
                                                + "than this one bot.");
                            }
                            else
                            {
                                try
                                {
                                    int tries;
                                    _rescueTries.TryGetValue(id, out tries);
                                    _rescueTries[id] = ++tries;

                                    root.position = np;
                                    root.rotation = tgt.rotation;
                                    AnnounceTeleport(root, np, tgt.rotation);

                                    Vector3 after = root.position;
                                    bool landed = (after - np).sqrMagnitude < 0.25f;

                                    if (landed)
                                    {
                                        _rescued++;
                                        _rescueTries.Remove(id);
                                        _airborneSince.Remove(id);

                                        int tot;
                                        _rescuedTotal.TryGetValue(id, out tot);
                                        tot++;
                                        _rescuedTotal[id] = tot;
                                        if (tot >= MaxLifetimeRescues)
                                        {
                                            _rescuedTotal.Remove(id);
                                            bool gone = false;
                                            try
                                            {
                                                Il2CppMirror.NetworkServer.Destroy(root.gameObject);
                                                gone = true;
                                            }
                                            catch (Exception de)
                                            { _log?.Warning("[float] retire netId=" + id + ": " + de.Message); }
                                            _log?.Warning("[float] netId=" + id + " has been RETIRED after "
                                                        + tot + " rescues. It could not hold our floor, so it "
                                                        + "was falling, being teleported back and falling "
                                                        + "again -- which every other machine sees as an "
                                                        + "invisible enemy standing somewhere it is not. "
                                                        + (gone ? "Removed from every client via Mirror."
                                                                : "Mirror refused the removal; it is still there."));
                                            continue;
                                        }
                                        _log?.Msg("[float] netId=" + id + " was falling out of "
                                                + "the world and has been put back on a marker "
                                                + "at " + np + ", verified to have ground under "
                                                + "it and verified to have moved. Other players "
                                                + "may see it somewhere else: a rescue cannot be "
                                                + "replicated, and a lost bot is worse than a "
                                                + "misplaced one.");
                                    }
                                    else if (tries >= MaxRescueTries)
                                    {

                                        _rescueGaveUp.Add(id);
                                        _log?.Warning("[float] netId=" + id + " CANNOT BE MOVED. "
                                                + tries + " attempts to place it at " + np
                                                + " all read back " + after + " unchanged, so "
                                                + "something else owns this transform "
                                                + "(SmoothSyncMirror is the likely owner). "
                                                + "Giving up on this bot rather than retrying "
                                                + "every 6 s for the rest of the mission. It "
                                                + "will stay where it is and stay alive, which "
                                                + "means extraction will wait on it.");
                                    }
                                    else
                                    {
                                        _log?.Msg("[float] netId=" + id + " rescue attempt "
                                                + tries + " of " + MaxRescueTries
                                                + " did not take: asked for " + np
                                                + ", transform still reads " + after);
                                    }
                                }
                                catch { }
                            }
                        }
                    }

                    if (!firstReport) continue;

                    bool hvt = false; try { hvt = b.isHVT; } catch { }
                    Vector3 rc; int rn;
                    string drawInfo = "";
                    if (RenderCentre(root, out rc, out rn))
                    {
                        float gap = (rc - p).magnitude;
                        drawInfo = "  draws at (" + rc.x.ToString("F1") + ", "
                                 + rc.y.ToString("F1") + ", " + rc.z.ToString("F1") + ")"
                                 + (gap > 3f
                                    ? "  <-- MESH IS " + gap.ToString("F1") + " m FROM THE "
                                      + "TRANSFORM, so this is a skinning fault and not a "
                                      + "position fault"
                                    : "");
                    }
                    string life = LifeState(b, root);
                    _log?.Warning("[float] netId=" + id + " (" + life + ") has been in the air for "
                                + (now - since).ToString("F0") + " s at ("
                                + p.x.ToString("F1") + ", " + p.y.ToString("F1") + ", "
                                + p.z.ToString("F1") + ") with "
                                + (best > 1e8f ? "NOTHING" : best.ToString("F1") + " m")
                                + " below it" + (hvt ? "  [HVT]" : "")
                                + drawInfo
                                + ". Compare this netId against the other machine's log: "
                                + "if it is grounded there, only this machine sees it flying.");
                }
            }
            catch (Exception e) { _log?.Warning("[float] " + e.Message); }
        }

        static readonly System.Collections.Generic.Dictionary<int, bool> _personCache =
            new System.Collections.Generic.Dictionary<int, bool>();

        static bool IsPerson(Transform root)
        {
            if (root == null) return false;
            int id;
            try { id = root.GetInstanceID(); } catch { return false; }
            bool known;
            if (_personCache.TryGetValue(id, out known)) return known;
            bool person = false;
            try
            {
                person = root.GetComponentInChildren<Il2Cpp.BrainAI>(true) != null
                      || root.GetComponentInChildren<Il2Cpp.MasterController>(true) != null;
            }
            catch { }
            _personCache[id] = person;
            return person;
        }

        static void LookProbe()
        {
            _personCache.Clear();
            try
            {
                var cam = Camera.main;
                if (cam == null) { _log?.Msg("[look] no camera"); return; }
                var eye = cam.transform.position;
                var dir = cam.transform.forward;

                Transform ours = null;
                try { var rg = MapRuntime.Root; if (rg != null) ours = rg.transform; } catch { }

                _log?.Msg("[look] from (" + eye.x.ToString("F1") + ", " + eye.y.ToString("F1")
                        + ", " + eye.z.ToString("F1") + ") looking at "
                        + dir.x.ToString("F2") + ", " + dir.y.ToString("F2") + ", "
                        + dir.z.ToString("F2"));

                var hits = new Il2CppInterop.Runtime.InteropTypes.Arrays
                               .Il2CppStructArray<RaycastHit>(32L);
                int n = Physics.RaycastNonAlloc(eye, dir, hits, 300f, ~0,
                                                QueryTriggerInteraction.Ignore);
                _log?.Msg("[look] " + n + " collider(s) along your view:");
                for (int i = 0; i < n && i < hits.Length && i < 8; i++)
                {
                    var c = hits[i].collider;
                    if (c == null) continue;
                    string nm = "?", rt = "?"; bool mine = false;
                    try { nm = c.gameObject.name; } catch { }
                    try { rt = c.transform.root.name; } catch { }
                    try { if (ours != null) mine = c.transform.IsChildOf(ours); } catch { }
                    _log?.Msg("[look]   " + hits[i].distance.ToString("F1") + " m  '" + nm
                            + "'  root='" + rt + "'  " + (mine ? "OURS" : "NOT OURS"));
                }

                var all = UnityEngine.Object.FindObjectsOfType<Renderer>();
                var ray = new Ray(eye, dir);
                string bestName = null, bestRoot = null;
                bool bestMine = false;
                float bestD = float.MaxValue;
                Vector3 bestCentre = Vector3.zero;

                float bestBaseY = 0f;
                int looked = 0;
                for (int i = 0; i < (all != null ? all.Length : 0); i++)
                {
                    var rd = all[i];
                    if (rd == null) continue;
                    bool drawing;
                    try { drawing = rd.enabled && !rd.forceRenderingOff; } catch { continue; }
                    if (!drawing) continue;
                    Bounds b;
                    try { b = rd.bounds; } catch { continue; }

                    float d = Vector3.Distance(b.center, eye);
                    if (d < 2f) continue;
                    if (d > 120f || d > bestD) continue;
                    float hitD;
                    if (!b.IntersectRay(ray, out hitD)) continue;
                    looked++;

                    Transform rr = null;
                    try { rr = rd.transform.root; } catch { continue; }
                    if (rr == null) continue;
                    if (IsPerson(rr)) continue;
                    bestD = d;
                    bestCentre = b.center;
                    bestBaseY = b.min.y;
                    try { bestName = rd.gameObject.name; } catch { bestName = "?"; }
                    try { bestRoot = rd.transform.root.name; } catch { bestRoot = "?"; }
                    try { bestMine = ours != null && rd.transform.IsChildOf(ours); } catch { }
                }

                if (bestName == null)
                {
                    _log?.Msg("[look] nothing drawing is in your crosshair within 120 m.");
                    return;
                }

                _log?.Msg("[look] nearest thing DRAWING in your crosshair: '" + bestName
                        + "'  root='" + bestRoot + "'  " + (bestMine ? "OURS" : "NOT OURS -- host")
                        + "  " + bestD.ToString("F1") + " m away, centre y="
                        + bestCentre.y.ToString("F1"));

                int m = Physics.RaycastNonAlloc(bestCentre, Vector3.down, hits, 200f, ~0,
                                                QueryTriggerInteraction.Ignore);
                float floor = float.NegativeInfinity; string on = "?"; bool onOurs = false;
                for (int i = 0; i < m && i < hits.Length; i++)
                {
                    var c = hits[i].collider;
                    if (c == null) continue;
                    if (hits[i].point.y > floor)
                    {
                        floor = hits[i].point.y;
                        try { on = c.gameObject.name; } catch { }
                        try { onOurs = ours != null && c.transform.IsChildOf(ours); } catch { }
                    }
                }
                if (floor == float.NegativeInfinity)
                    _log?.Msg("[look]   NOTHING beneath it for 200 m. It is hanging in space.");
                else
                {

                    float gapFromBase = bestBaseY - floor;
                    _log?.Msg("[look]   ground beneath it at y=" + floor.ToString("F1")
                            + " ('" + on + "', " + (onOurs ? "ours" : "host") + ")");
                    _log?.Msg("[look]   its BASE is at y=" + bestBaseY.ToString("F1")
                            + ", so the gap under it is " + gapFromBase.ToString("F2") + " m"
                            + (gapFromBase > 0.5f
                                ? "  <-- genuinely floating"
                                : (gapFromBase < -0.5f ? "  (sunk into the ground)" : "  (planted)")));
                    _log?.Msg("[look]   (centre y=" + bestCentre.y.ToString("F1")
                            + " is " + (bestCentre.y - floor).ToString("F1")
                            + " m up, but that is mostly the object's own height -- do not read it as a gap)");
                }
            }
            catch (Exception e) { _log?.Warning("[look] probe: " + e.Message); }
        }

        static void ForeignRenderers()
        {
            try
            {
                _personCache.Clear();
                var cam = Camera.main;
                Vector3 eye = cam != null ? cam.transform.position : Vector3.zero;

                Transform ours = null;
                try { var rg = MapRuntime.Root; if (rg != null) ours = rg.transform; } catch { }

                var all = UnityEngine.Object.FindObjectsOfType<Renderer>();
                int total = all != null ? all.Length : 0;
                int mine = 0, off = 0, far = 0, chars = 0;

                var byRoot = new System.Collections.Generic.Dictionary<string, int>(StringComparer.Ordinal);
                var nearest = new System.Collections.Generic.Dictionary<string, float>(StringComparer.Ordinal);
                var sample = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);

                for (int i = 0; i < total; i++)
                {
                    var rd = all[i];
                    if (rd == null) continue;

                    bool drawing;
                    try { drawing = rd.enabled && !rd.forceRenderingOff; } catch { continue; }
                    if (!drawing) { off++; continue; }

                    Transform t = null;
                    try { t = rd.transform; } catch { continue; }
                    if (t == null) continue;
                    if (ours != null)
                    {
                        bool isOurs = false;
                        try { isOurs = t.IsChildOf(ours); } catch { }
                        if (isOurs) { mine++; continue; }
                    }

                    Transform tr = null;
                    try { tr = t.root; } catch { continue; }
                    if (tr == null) continue;

                    if (IsPerson(tr)) { chars++; continue; }

                    float d;
                    try { d = Vector3.Distance(t.position, eye); } catch { continue; }

                    if (d > 400f) { far++; continue; }

                    string rootName = "?";
                    try { rootName = tr.name; } catch { }

                    int had; byRoot.TryGetValue(rootName, out had);
                    byRoot[rootName] = had + 1;
                    float best;
                    if (!nearest.TryGetValue(rootName, out best) || d < best)
                    {
                        nearest[rootName] = d;
                        try
                        {
                            sample[rootName] = t.name + " at y=" + t.position.y.ToString("F1");
                        }
                        catch { }
                    }
                }

                _log?.Msg("[veg] " + total + " renderer(s) in the scene: " + mine
                        + " ours, " + off + " switched off or force-hidden, " + chars
                        + " on people (bots and players, excluded), " + far
                        + " beyond 400 m. That leaves " + byRoot.Count + " STATIC foreign "
                        + "root(s) still drawing within 400 m of you, listed below. If a "
                        + "floating tree is in front of you and this list is empty, the tree "
                        + "is ours and the grounding pass is misclassifying it.");

                var keys = new System.Collections.Generic.List<string>(byRoot.Keys);
                keys.Sort((a, b) => nearest[a].CompareTo(nearest[b]));
                for (int i = 0; i < keys.Count && i < 25; i++)
                {
                    string k = keys[i];
                    string smp; sample.TryGetValue(k, out smp);
                    _log?.Msg("[veg]   " + nearest[k].ToString("F0").PadLeft(4) + " m  '" + k
                            + "'  x" + byRoot[k]
                            + (string.IsNullOrEmpty(smp) ? "" : "  e.g. " + smp));
                }
                if (keys.Count > 25)
                    _log?.Msg("[veg]   ... " + (keys.Count - 25) + " more root(s), all further away");
                if (keys.Count == 0)
                    _log?.Msg("[veg]   nothing foreign is drawing near you. If you can SEE a "
                            + "floating tree from where you pressed this, then it IS ours, "
                            + "and the grounding pass is misclassifying it rather than "
                            + "missing it -- stand under one and press this again.");
            }
            catch (Exception e) { _log?.Warning("[veg] inventory: " + e.Message); }
        }

        public static void DeepDump()
        {
            try
            {
                _log?.Msg("======== ODG DEEP DUMP ========");
                LookProbe();
                ForeignRenderers();

                bool srv = false, cli = false;
                int conns = 0;
                try { srv = Il2CppMirror.NetworkServer.active; } catch { }
                try { cli = Il2CppMirror.NetworkClient.active; } catch { }
                try { conns = Il2CppMirror.NetworkServer.connections.Count; } catch { }
                _log?.Msg("[dump] role: " + (srv && cli ? "HOST" : srv ? "SERVER" : cli ? "CLIENT" : "OFFLINE")
                        + "  connections=" + conns
                        + "  scene='" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + "'");

                _log?.Msg("[dump] timing: build took " + MapRuntime.BuildSeconds.ToString("F0")
                        + " s; " + (MapRuntime.Done
                            ? (Time.realtimeSinceStartup - _levelReadyAt).ToString("F0")
                              + " s since it finished"
                            : "still building"));
                _log?.Msg("[dump] map: built=" + MapRuntime.Done
                        + " placed=" + MapRuntime.Placed
                        + " bundle='" + MapRuntime.BundleDir + "'");
                _log?.Msg("[dump] build phases: decode " + MeshFactory.DecodeMs
                        + " ms, compress " + MeshFactory.CompressMs
                        + " ms, mesh " + MeshFactory.MeshBuildMs
                        + " ms, material " + MeshFactory.MaterialMs + " ms");
                long tc = MeshFactory.TexBytesCompressed, tr = MeshFactory.TexBytesRaw;
                _log?.Msg("[dump] textures: " + MeshFactory.TexCountCompressed + " compressed ("
                        + (tc / 1048576) + " MB) + " + MeshFactory.TexCountRaw + " raw ("
                        + (tr / 1048576) + " MB) = " + ((tc + tr) / 1048576) + " MB on the card. "
                        + "bound=" + MeshFactory.BoundCount + " failed=" + MeshFactory.FailedCount);

                _log?.Msg("[dump] frame: " + (1f / Mathf.Max(Time.smoothDeltaTime, 1e-5f)).ToString("F0")
                        + " fps  (" + (Time.smoothDeltaTime * 1000f).ToString("F1") + " ms)"
                        + "  mono=" + (GC.GetTotalMemory(false) / 1048576) + " MB");

                try
                {
                    var astar = Il2Cpp.AstarPath.active;
                    int g = -1;
                    try { if (astar != null) g = astar.graphs.Length; } catch { }
                    _log?.Msg("[dump] nav: AstarPath=" + (astar != null ? "present" : "NONE")
                            + " graphs=" + g
                            + (srv ? "" : "  (a client should own none; the host does the pathing)"));
                }
                catch { }

                try
                {
                    var doors = UnityEngine.Object.FindObjectsOfType<Il2Cpp.DoorV2>();
                    int total = doors != null ? doors.Length : 0, vis = 0, near = 0;
                    var me = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
                    for (int i = 0; i < total; i++)
                    {
                        var d = doors[i];
                        if (d == null) continue;
                        if (MapDoors.IsTemplate(d.gameObject)) continue;
                        try
                        {
                            var rs = d.GetComponentsInChildren<Renderer>(false);
                            bool anyOn = false;
                            for (int k = 0; k < rs.Length; k++)
                                if (rs[k] != null && rs[k].enabled) { anyOn = true; break; }
                            if (anyOn) vis++;
                            if ((d.transform.position - me).sqrMagnitude < 900f) near++;
                        }
                        catch { }
                    }
                    _log?.Msg("[dump] doors: " + total + " in scene, " + vis
                            + " with a visible renderer, " + near + " within 30 m of the camera. "
                            + "A door that exists but draws nothing is a hidden renderer, not a "
                            + "placement failure.");
                }
                catch { }

                _log?.Msg("[dump] " + MapBody.Describe());

                _log?.Msg("[dump] rescues: " + _rescued + " bot(s) put back after falling out "
                        + "of the world this level. Each one is a bot other machines may "
                        + "place differently, because a teleport does not replicate.");

                try
                {
                    var cat = MelonLoader.MelonPreferences.GetCategory("MapLoader");
                    if (cat != null)
                    {
                        var sb = new System.Text.StringBuilder();
                        foreach (var e in cat.Entries)
                        {
                            if (sb.Length > 0) sb.Append("  ");
                            sb.Append(e.Identifier).Append('=')
                              .Append(e.BoxedEditedValue ?? e.BoxedValue);
                        }
                        _log?.Msg("[dump] settings: " + sb);
                    }
                }
                catch (Exception ce) { _log?.Msg("[dump] settings unreadable: " + ce.Message); }

                try
                {
                    var cam = Camera.main;
                    if (cam != null)
                    {
                        var eye = cam.transform.position;
                        var bots2 = UnityEngine.Object.FindObjectsOfType<Il2Cpp.BrainAI>();
                        int high = 0;
                        var lines = new System.Collections.Generic.List<string>();
                        for (int i = 0; i < (bots2 != null ? bots2.Length : 0); i++)
                        {
                            var b2 = bots2[i];
                            if (b2 == null) continue;
                            Transform r2 = null;
                            try { r2 = b2.transform.root; } catch { }
                            if (r2 == null) continue;
                            var d = r2.position - eye;
                            float dist = d.magnitude;
                            if (dist < 0.5f) continue;
                            float elev = Mathf.Asin(Mathf.Clamp(d.y / dist, -1f, 1f))
                                       * Mathf.Rad2Deg;
                            if (elev < 15f) continue;
                            high++;
                            uint id2 = 0;
                            try
                            {
                                var ni2 = r2.GetComponent<Il2CppMirror.NetworkIdentity>();
                                if (ni2 != null) id2 = ni2.netId;
                            }
                            catch { }
                            if (lines.Count < 12)
                                lines.Add("netId=" + id2 + " elev=" + elev.ToString("F0")
                                        + " deg dist=" + dist.ToString("F0") + " m at ("
                                        + r2.position.x.ToString("F1") + ", "
                                        + r2.position.y.ToString("F1") + ", "
                                        + r2.position.z.ToString("F1") + ")");
                        }
                        _log?.Msg("[dump] in the sky: " + high + " bot(s) more than 15 degrees "
                                + "above your eye line. This is the thing people actually "
                                + "report, measured from the camera rather than inferred from "
                                + "a coordinate.");
                        for (int i = 0; i < lines.Count; i++) _log?.Msg("[dump]   " + lines[i]);
                    }
                }
                catch (Exception se) { _log?.Msg("[dump] sky check: " + se.Message); }

                try
                {
                    var pms = UnityEngine.Object.FindObjectsOfType<Il2Cpp.PlayerMaster>();
                    _log?.Msg("[dump] players: " + (pms != null ? pms.Length : 0));
                    for (int i = 0; i < (pms != null ? pms.Length : 0) && i < 8; i++)
                    {
                        var pm = pms[i];
                        if (pm == null) continue;
                        string nm = "?"; Vector3 pp = Vector3.zero;
                        try { nm = pm.gameObject.name; } catch { }
                        try { pp = pm.transform.position; } catch { }
                        _log?.Msg("[dump]   '" + nm + "' at (" + pp.x.ToString("F1") + ", "
                                + pp.y.ToString("F1") + ", " + pp.z.ToString("F1") + ")");
                    }
                }
                catch { }

                DumpBotCensus("deep dump");
                _log?.Msg("======== END DEEP DUMP ========");
            }
            catch (Exception e) { _log?.Warning("[dump] " + e.Message); }
        }

        public static void DumpBotCensus(string why)
        {
            try
            {
                var bots = UnityEngine.Object.FindObjectsOfType<Il2Cpp.BrainAI>();
                if (bots == null || bots.Length == 0)
                { _log?.Msg("[census] no bots (" + why + ")"); return; }

                var rows = new System.Collections.Generic.List<
                    System.Collections.Generic.KeyValuePair<uint, string>>();
                for (int i = 0; i < bots.Length; i++)
                {
                    var b = bots[i];
                    if (b == null) continue;
                    Transform broot = null;
                    try { broot = b.transform.root; } catch { }
                    if (broot == null) continue;
                    uint id = 0;
                    try
                    {
                        var ni = broot.GetComponent<Il2CppMirror.NetworkIdentity>();
                        if (ni != null) id = ni.netId;
                    }
                    catch { }
                    var pos = broot.position;
                    bool hvt = false; try { hvt = b.isHVT; } catch { }

                    Vector3 rc; int rn;
                    string extra = "";
                    if (RenderCentre(broot, out rc, out rn))
                    {
                        float gap = (rc - pos).magnitude;
                        extra += " rend=" + rn + " draws=" + gap.ToString("F1") + "m";
                        if (gap > 3f)
                        {
                            extra += "!MESH-ADRIFT bone=(" + rc.x.ToString("0.0") + ", " + rc.y.ToString("0.0")
                                   + ", " + rc.z.ToString("0.0") + ")";

                            try
                            {
                                var sk0 = broot.GetComponentInChildren<SkinnedMeshRenderer>(false);
                                var rb0 = sk0 != null ? sk0.rootBone : null;
                                if (rb0 != null)
                                {
                                    extra += " boneUnderBot=" + rb0.IsChildOf(broot);
                                    Transform tp = rb0; int hops = 0;
                                    while (tp.parent != null && hops++ < 20) tp = tp.parent;
                                    if (tp != broot) extra += " boneTop='" + tp.name + "'";
                                }
                            }
                            catch { }
                            try
                            {
                                RaycastHit bh;
                                if (Physics.Raycast(rc + Vector3.up * 0.5f, Vector3.down, out bh, 60f, ~0,
                                                    QueryTriggerInteraction.Ignore))
                                {
                                    var bc = bh.collider;
                                    extra += " boneOver='" + bc.gameObject.name + "' " + bh.distance.ToString("0.0")
                                           + "m layer=" + bc.gameObject.layer + "(" + LayerMask.LayerToName(bc.gameObject.layer) + ")"
                                           + (MapRuntime.Root != null && bc.transform.IsChildOf(MapRuntime.Root.transform) ? " OURS" : "");
                                }
                                else extra += " boneOver=nothing";
                            }
                            catch { }
                        }
                    }
                    else extra += " rend=0";
                    extra += " " + LifeState(b, broot);

                    try
                    {
                        var rb = broot.GetComponentInChildren<Rigidbody>(true);
                        if (rb != null)
                        {
                            var v = rb.velocity;
                            if (v.sqrMagnitude > 0.25f)
                                extra += " vel=" + v.magnitude.ToString("F1")
                                       + (v.y < -1f ? " FALLING" : "");
                        }
                    }
                    catch { }
                    try
                    {

                        var hits2 = new Il2CppInterop.Runtime.InteropTypes.Arrays
                                        .Il2CppStructArray<RaycastHit>(32L);
                        int nn = Physics.RaycastNonAlloc(pos + Vector3.up * 0.5f, Vector3.down,
                                                         hits2, 60f, ~0,
                                                         QueryTriggerInteraction.Ignore);
                        if (nn >= hits2.Length) extra += " RAYFULL";
                        float bestd = float.PositiveInfinity;
                        for (int k = 0; k < nn && k < hits2.Length; k++)
                        {
                            var cc = hits2[k].collider;
                            if (cc == null) continue;
                            try { if (cc.transform.root == broot) continue; } catch { continue; }
                            if (hits2[k].distance < bestd) bestd = hits2[k].distance;
                        }
                        extra += bestd > 1e8f ? " ground=NONE"
                               : " ground=" + bestd.ToString("F1") + "m";
                    }
                    catch { }

                    rows.Add(new System.Collections.Generic.KeyValuePair<uint, string>(id,
                        "netId=" + id
                        + " pos=(" + pos.x.ToString("F1") + ", " + pos.y.ToString("F1")
                        + ", " + pos.z.ToString("F1") + ")"
                        + (hvt ? " HVT" : "") + extra));
                }
                rows.Sort(delegate (
                    System.Collections.Generic.KeyValuePair<uint, string> x,
                    System.Collections.Generic.KeyValuePair<uint, string> y)
                    { return x.Key.CompareTo(y.Key); });
                _log?.Msg("[census] " + rows.Count + " bot(s), " + why
                        + ". Diff these against the other machine's census: same "
                        + "netId same position is in sync; same netId different "
                        + "position IS the desync, measured.");
                int cap = rows.Count < 120 ? rows.Count : 120;
                for (int i = 0; i < cap; i++) _log?.Msg("[census]   " + rows[i].Value);
                if (rows.Count > cap)
                    _log?.Msg("[census]   ... " + (rows.Count - cap) + " more");
            }
            catch (Exception e) { _log?.Warning("[census] " + e.Message); }
        }

        public static void DumpState()
        {
            foreach (var g in new[] { SpawnGroup, GuardGroup, PatrolGroup, PlayerGroup, InsertGroup })
                _log?.Msg("[mapai] " + g.PadRight(18) + " markers: " + FindGroup(g).Count);

            var raid = Il2Cpp.RaidManager.singleton;
            if (raid == null) { _log?.Msg("[mapai] no RaidManager"); return; }

            int pts = 0;
            try { pts = raid.botSpawnPoints != null ? raid.botSpawnPoints.Count : -1; } catch { }
            int ai = 0;
            try { ai = raid.standardAI != null ? raid.standardAI.Length : -1; } catch { }
            _log?.Msg("[mapai] RaidManager.botSpawnPoints=" + pts + "  standardAI prefabs=" + ai);
            if (ai == 0)
                _log?.Warning("[mapai] standardAI is EMPTY -- no bot prefab to spawn. "
                            + "Spawn points alone will produce nothing.");
        }
    }
}
