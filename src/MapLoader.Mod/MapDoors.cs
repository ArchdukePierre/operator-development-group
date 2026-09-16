// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

using Mirror = Il2CppMirror;
using NetworkServer = Il2CppMirror.NetworkServer;
using NetworkIdentity = Il2CppMirror.NetworkIdentity;

namespace MapLoader
{

    public static class MapDoors
    {
        public const string DoorGroup = "BP_Generic_Door_C";

        static readonly string[] DoorGroups =
        {
            DoorGroup,
            "BP_Generic_Door_Industrial_01_C",
            "BP_Generic_Door_Industrial_02_C",
            "BP_Generic_Door_Quarters_01_C",
            "BP_Generic_Door_Bulkhead_01_C",

            "BP_ST_WoodDoor_C",
        };

        public static System.Collections.Generic.List<Transform> FindDoorMarkers()
        {
            var all = new System.Collections.Generic.List<Transform>();
            int skipped = 0;
            for (int i = 0; i < DoorGroups.Length; i++)
            {
                var g = MapAI.FindGroup(DoorGroups[i]);
                if (g == null) continue;
                for (int k = 0; k < g.Count; k++)
                {
                    var t = g[k];
                    if (t == null) continue;

                    if (MapIdentity.SkipInteriorDoors && MarkerKind(t.name) == 0) { skipped++; continue; }
                    all.Add(t);
                }
            }
            if (skipped > 0 && _skipReported != skipped)
            {
                _skipReported = skipped;
                _log?.Msg("[doors] " + skipped + " house-interior doorway(s) left open on purpose: their "
                        + "frames are taller than the borrowed leaf. " + all.Count + " doorway(s) get doors.");
            }
            return all;
        }

        static int _skipReported;

        static readonly Vector3 HitBoxLocalPos   = new Vector3(0.50283f, 1.1149505f, 0f);
        static readonly Vector3 HitBoxLocalScale = new Vector3(0.97553676f, 2.2299006f, 0.08750002f);
        const string HitBoxChildName = "PLACEHOLDER DOOR MODEL";

        public static float YawOffset = 270f;

        public static void AdoptMapYaw()
        {
            float w = MapIdentity.DoorYawOffset;
            if (w != YawOffset)
            {
                MelonLogger.Msg("[doors] yaw offset for this map is " + w.ToString("F0")
                              + " (was " + YawOffset.ToString("F0") + "). 270 undoes the "
                              + "UE-to-Unity quarter turn; a map read from a Unity scene "
                              + "directly needs 0.");
                YawOffset = w;
            }
        }

        public static float HingeFlip = 1f;

        public static void FlipHinges() { RotateAllDoors(180f); }

        public static void DumpPlacements(string why)
        {
            try
            {
                var all = new List<GameObject>(_relocated);
                all.AddRange(_spawned);
                if (all.Count == 0) return;

                var sb = new System.Text.StringBuilder();
                sb.Append("{\n");
                sb.Append("  \"why\": \"").Append(why).Append("\",\n");
                sb.Append("  \"yawOffset\": ").Append(YawOffset.ToString("F1")).Append(",\n");
                sb.Append("  \"hingeFlip\": ").Append(HingeFlip.ToString("F0")).Append(",\n");
                sb.Append("  \"mode\": \"").Append(Mode).Append("\",\n");
                sb.Append("  \"doors\": [\n");
                int n = 0;
                foreach (var g in all)
                {
                    if (g == null) continue;
                    var t = g.transform;
                    var p = t.position;
                    var e = t.rotation.eulerAngles;
                    float rot = 0f, maxRot = 0f; bool inv = false, locked = false, latched = false;
                    Vector3 hinge = p;
                    try
                    {
                        var dv = g.GetComponent<Il2Cpp.DoorV2>();
                        if (dv != null)
                        {
                            rot = dv.currentRotation; maxRot = dv.maxRotationY;
                            inv = dv.Invert; locked = dv.CurrentlyLocked; latched = dv.IsLatched;
                            var pv = PanelOf(dv, g);
                            if (pv != null) hinge = pv.position;
                        }
                    }
                    catch { }

                    if (n > 0) sb.Append(",\n");
                    sb.Append("    {");
                    sb.Append("\"name\": \"").Append(g.name).Append("\", ");
                    sb.Append("\"pos\": [").Append(p.x.ToString("F4")).Append(", ")
                      .Append(p.y.ToString("F4")).Append(", ").Append(p.z.ToString("F4")).Append("], ");
                    sb.Append("\"euler\": [").Append(e.x.ToString("F3")).Append(", ")
                      .Append(e.y.ToString("F3")).Append(", ").Append(e.z.ToString("F3")).Append("], ");

                    sb.Append("\"hinge\": [").Append(hinge.x.ToString("F4")).Append(", ")
                      .Append(hinge.y.ToString("F4")).Append(", ").Append(hinge.z.ToString("F4")).Append("], ");
                    sb.Append("\"currentRotation\": ").Append(rot.ToString("F2")).Append(", ");
                    sb.Append("\"maxRotationY\": ").Append(maxRot.ToString("F0")).Append(", ");
                    sb.Append("\"invert\": ").Append(inv ? "true" : "false").Append(", ");
                    sb.Append("\"locked\": ").Append(locked ? "true" : "false").Append(", ");
                    sb.Append("\"latched\": ").Append(latched ? "true" : "false");
                    sb.Append("}");
                    n++;
                }
                sb.Append("\n  ]\n}\n");

                string dir = MapRuntime.BundleDir;
                if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir)) return;
                string path = System.IO.Path.Combine(dir, "door_placements.json");
                System.IO.File.WriteAllText(path, sb.ToString());
                _log?.Msg("[doors] wrote the exact pose of " + n + " door(s) to " + path
                        + " (yaw " + YawOffset.ToString("F0") + " deg, " + why + ")");
            }
            catch (Exception e) { _log?.Warning("[doors] DumpPlacements: " + e.Message); }
        }

        public static int SetAllLocked(bool locked)
        {
            int n = 0;
            var all = new List<GameObject>(_relocated);
            all.AddRange(_spawned);
            foreach (var g in all)
            {
                if (g == null) continue;
                try
                {
                    var dv = g.GetComponent<Il2Cpp.DoorV2>();
                    if (dv == null) continue;
                    if (NetworkServer.active) dv.Server_SetDoorLocked(locked);
                    n++;
                }
                catch { }
            }
            _log?.Msg("[doors] " + (locked ? "locked " : "UNLOCKED ") + n
                    + " door(s) for testing. This is a debug toggle; placement "
                    + "still authors locks exactly as before.");
            return n;
        }

        static void TurnAboutHinge(GameObject g, float degrees)
        {
            Vector3 hinge = g.transform.position;
            try
            {
                var dv = g.GetComponent<Il2Cpp.DoorV2>();
                var pivot = PanelOf(dv, g);
                if (pivot != null) hinge = pivot.position;
            }
            catch { }
            g.transform.RotateAround(hinge, Vector3.up, degrees);
        }

        public static void RotateAllDoors(float degrees)
        {
            YawOffset = Mathf.Repeat(YawOffset + degrees, 360f);
            if (Mathf.Abs(degrees - 180f) < 0.01f) HingeFlip = -HingeFlip;
            int n = 0;
            var all = new List<GameObject>(_relocated);
            all.AddRange(_spawned);
            foreach (var g in all)
            {
                if (g == null) continue;
                try
                {
                    TurnAboutHinge(g, degrees);
                    var dv = g.GetComponent<Il2Cpp.DoorV2>();
                    if (dv != null)
                    {

                        CloseAndLatchLocal(dv);
                        AnnounceClosed(dv);
                    }
                    n++;
                }
                catch { }
            }
            _log?.Msg("[doors] turned " + n + " door(s) " + degrees.ToString("F0")
                    + " degrees on the spot; yaw offset is now "
                    + YawOffset.ToString("F0") + " deg. If they are square to "
                    + "their frames, that is the number to keep.");
            DumpPlacements("after a manual turn to " + YawOffset.ToString("F0") + " deg");
        }

        static void CloseAndLatchLocal(Il2Cpp.DoorV2 d)
        {
            if (d == null) return;
            try { if (!d.canLatch) d.canLatch = true; } catch { }
            try { d.SetDoorClosedLocal(true); } catch { }
            try { d.currentRotation = 0f; d.Velocity = 0f; d.PredictedRotation = 0f; } catch { }

            try { d.SetRotationHotReload(0f); } catch { }

            try
            {
                var pivot = d.PivotTransform;
                if (pivot != null && pivot != d.transform)
                    pivot.localRotation = Quaternion.identity;
            }
            catch { }
        }

        static void AnnounceClosed(Il2Cpp.DoorV2 d)
        {
            if (d == null) return;
            try
            {
                if (!NetworkServer.active) return;
                var ni = d.GetComponent<NetworkIdentity>();
                if (ni == null || ni.netId == 0) return;
                d.RPC_SetDoorClosed(true);
            }
            catch { }
        }

        public static string Mode = "relocate";

        static MelonLogger.Instance _log;
        static readonly List<GameObject> _spawned = new List<GameObject>();

        public static void Init(MelonLogger.Instance log) { _log = log; }

        public static int Count => _spawned.Count;

        public static void ResetForNewLevel()
        {
            _relocatedPose.Clear(); _holdRuns++;
            _spawned.Clear();
            _tinted.Clear();
            _claimed.Clear();

            _movedFrom.Clear();
            _relocated.Clear();
            DoorSentry.Reset();
        }

        static int Score(Il2Cpp.DoorV2 d, out string detail)
        {
            int n = 0;
            var missing = new List<string>();
            Action<bool, string> check = (ok, name) =>
            { if (ok) n++; else missing.Add(name); };

            try { check(d.PivotTransform != null, "PivotTransform"); } catch { }
            try { check(d.HandleFront != null, "HandleFront"); } catch { }
            try { check(d.HandleBack != null, "HandleBack"); } catch { }
            try { check(d.rb != null, "rb"); } catch { }
            try { check(d.DoorPhysicsSync != null, "DoorPhysicsSync"); } catch { }
            try { check(d.DoorHitBox != null, "DoorHitBox"); } catch { }
            try { check(d.DoorModelParent != null, "DoorModelParent"); } catch { }
            try { check(d.latchCollider != null, "latchCollider"); } catch { }
            try { check(d.HingeTopCollider != null, "HingeTopCollider"); } catch { }
            try { check(d.HingeBottomCollider != null, "HingeBottomCollider"); } catch { }
            try { check(d.audioSource != null, "audioSource"); } catch { }
            try { check(d.GetComponent<NetworkIdentity>() != null, "NetworkIdentity"); } catch { }

            detail = missing.Count == 0 ? "complete" : "missing " + string.Join(",", missing.ToArray());
            return n;
        }

        public static Il2Cpp.DoorV2 BestPrefab(bool verbose)
        {
            Il2Cpp.DoorV2 best = null;
            int bestScore = -1;
            var all = Resources.FindObjectsOfTypeAll<Il2Cpp.DoorV2>();
            if (all == null) return null;

            for (int i = 0; i < all.Length; i++)
            {
                var d = all[i];
                if (d == null || d.gameObject == null) continue;

                if (d.gameObject.scene.IsValid()) continue;

                string detail;
                int s = Score(d, out detail);
                if (verbose)
                    _log?.Msg("  prefab '" + d.gameObject.name + "' score " + s + "/12  " + detail);
                if (s > bestScore) { bestScore = s; best = d; }
            }
            if (best != null && verbose)
                _log?.Msg("  -> chose '" + best.gameObject.name + "' (" + bestScore + "/12)");
            return best;
        }

        static ulong StableId(Il2Cpp.DoorV2 d)
        {
            try
            {
                var ni = d.GetComponent<NetworkIdentity>();
                if (ni != null && ni.sceneId != 0) return ni.sceneId;
            }
            catch { }
            return ulong.MaxValue;
        }

        public static bool IsTemplate(GameObject go)
        {
            try
            {
                if (go == null) return false;

                if (go.name.StartsWith("ODG_DoorTemplate", StringComparison.Ordinal)
                    && !go.name.EndsWith("(Clone)", StringComparison.Ordinal)) return true;
                if (string.Equals(go.scene.name, "DontDestroyOnLoad", StringComparison.Ordinal)) return true;
                var t = go.transform; int hops = 0;
                while (t != null && hops < 12)
                {
                    if (t.name == "ODG_DoorTemplates") return true;
                    t = t.parent; hops++;
                }
            }
            catch { }
            return false;
        }

        public static List<Il2Cpp.DoorV2> SceneDoors(bool verbose)
        {
            var found = new List<Il2Cpp.DoorV2>();
            try
            {
                var all = Resources.FindObjectsOfTypeAll<Il2Cpp.DoorV2>();
                if (all == null) return found;
                var ourRoot = MapRuntime.Root != null ? MapRuntime.Root.transform : null;

                for (int i = 0; i < all.Length; i++)
                {
                    var d = all[i];
                    if (d == null || d.gameObject == null) continue;
                    if (!d.gameObject.scene.IsValid()) continue;

                    if (ourRoot != null && d.transform.IsChildOf(ourRoot)) continue;
                    if (d.gameObject.name.StartsWith("MapLoader_Door",
                                                     StringComparison.Ordinal)) continue;
                    if (IsTemplate(d.gameObject)) continue;
                    found.Add(d);
                }

                found.Sort((a, b) =>
                {
                    string _;
                    int byScore = Score(b, out _).CompareTo(Score(a, out _));
                    if (byScore != 0) return byScore;
                    ulong ia = StableId(a), ib = StableId(b);
                    if (ia != ib) return ia.CompareTo(ib);
                    return string.CompareOrdinal(a.gameObject.name, b.gameObject.name);
                });

                if (verbose)
                {
                    _log?.Msg("[doors] " + found.Count + " shipped scene door(s) available");
                    int show = Math.Min(3, found.Count);
                    for (int i = 0; i < show; i++)
                    {
                        string detail;
                        int s = Score(found[i], out detail);
                        _log?.Msg("  scene door '" + found[i].gameObject.name + "' score "
                                + s + "/12  " + detail + "  scene='"
                                + found[i].gameObject.scene.name + "'");
                    }
                }
            }
            catch (Exception e) { _log?.Warning("[doors] SceneDoors: " + e.Message); }
            return found;
        }

        public static Il2Cpp.DoorV2 BestSource(Il2Cpp.DoorV2 prefab)
        {
            Il2Cpp.DoorV2 best = prefab;
            int bestScore = -1;
            if (prefab != null) { string d0; bestScore = Score(prefab, out d0); }

            foreach (var sd in SceneDoors(true))
            {
                if (sd == null) continue;
                string d1;
                int s1 = Score(sd, out d1);
                if (s1 > bestScore) { bestScore = s1; best = sd; }
            }
            return best;
        }

        public static int Place()
        {
            if (!NetworkServer.active)
            {

                if (Mode != "relocate")
                {
                    _log?.Error("[doors] not the server, and mode '" + Mode + "' clones "
                              + "and spawns, which only the host may do. Doors will be "
                              + "missing on this machine; set DoorMode = relocate.");
                    return 0;
                }
                _log?.Msg("[doors] client: relocating the level's own doors locally "
                        + "to match the host's placement");
            }

            AdoptMapYaw();

            try { DoorSpawnRegistry.EnsureForSceneDoors("door placement"); } catch { }
            var markers = FindDoorMarkers();
            if (markers != null && markers.Count > 0)
            {
                ShellSwap.MapUsesDoors = true;

                if (MapIdentity.FullDoors)
                    try { MelonLoader.MelonCoroutines.Start(FinishClonesLate()); } catch { }
            }
            if (markers.Count == 0)
            {
                _log?.Warning("[doors] no door markers in the active scene (looked for "
                                + string.Join(", ", DoorGroups) + ")");

                ShellSwap.MapUsesDoors = false;
                return 0;
            }

            if (Mode == "relocate" || Mode == "both")
            {
                int rel = RelocateSceneDoors(markers);
                _log?.Msg("[doors] mode=" + Mode + ": relocated " + rel + " shipped door(s)");
                if (Mode == "relocate")
                {

                    if (MapIdentity.FullDoors && NetworkServer.active
                        && markers.Count > rel)
                    {
                        _log?.Msg("[doors] FullDoors map: " + markers.Count
                                + " doorway(s), " + rel + " shipped door(s) "
                                + "relocated; cloning the remaining "
                                + (markers.Count - rel));
                    }
                    else
                    {

                        DoorWiring.WireNow();
                        return rel;
                    }
                }
            }

            var prefab = BestPrefab(true);
            var source = BestSource(prefab);
            if (source == null)
            {
                _log?.Error("[doors] no DoorV2 prefab and no shipped scene door to copy.");
                return 0;
            }

            uint assetId = 0;
            try
            {
                if (prefab != null)
                {
                    var pid = prefab.GetComponent<NetworkIdentity>();
                    if (pid != null) assetId = pid.assetId;
                }
            }
            catch { }

            bool sourceIsScene = false;
            try { sourceIsScene = source.gameObject.scene.IsValid(); } catch { }
            string sScore; int sN = Score(source, out sScore);
            DoorReport.Dump(source.gameObject, "THE SOURCE we are about to clone");
            _log?.Msg("[doors] placing " + markers.Count + " door(s) by cloning "
                    + (sourceIsScene ? "the SHIPPED scene door '" : "the prefab '")
                    + source.gameObject.name + "' (" + sN + "/12), assetId=" + assetId);

            int moved = 0;

            int ok = 0, failed = 0, repaired = 0;
            for (int mi = 0; mi < markers.Count; mi++)
            {
                var m = markers[mi];
                if (m == null) continue;
                if (_claimed.Contains(mi)) continue;
                GameObject go = null;
                try
                {

                    var rot = m.rotation;

                    var src = SourceFor(m, source);
                    bool wasActive = true;
                    try { wasActive = src.gameObject.activeSelf; } catch { }
                    try { src.gameObject.SetActive(false); } catch { }
                    try
                    {
                        go = UnityEngine.Object.Instantiate(src.gameObject, m.position, rot);
                    }
                    finally
                    {
                        try { src.gameObject.SetActive(wasActive); } catch { }
                    }
                    if (go == null) { failed++; continue; }
                    go.name = "MapLoader_Door_" + ok;

                    try
                    {
                        var ni = go.GetComponent<NetworkIdentity>();
                        if (ni != null)
                        {

                            ni.hasSpawned = false;

                            ni.sceneId = 0;

                            uint own = ni.assetId;
                            if (own == 0 && assetId != 0) ni.assetId = assetId;
                            if (ok == 0)
                                _log?.Msg("[doors] clone defused: hasSpawned=false, sceneId=0, "
                                        + "assetId=" + ni.assetId
                                        + (own == 0 ? " (borrowed)" : " (its own)"));
                        }
                    }
                    catch (Exception ae)
                    {
                        if (ok == 0) _log?.Warning("[doors] defusing the clone: " + ae.Message);
                    }

                    try { go.SetActive(true); } catch { }

                    try { if (Mathf.Abs(YawOffset) > 0.01f) TurnAboutHinge(go, YawOffset); } catch { }

                    var d = go.GetComponent<Il2Cpp.DoorV2>();
                    if (d != null)
                    {

                        CloseAndLatchLocal(d);
                        if (Repair(d, go)) repaired++;
                    }

                    NetworkServer.Spawn(go);

                    if (d != null) AnnounceClosed(d);
                    _spawned.Add(go);
                    DoorSentry.Track(d);
                    ok++;
                }
                catch (Exception e)
                {
                    failed++;
                    if (failed <= 3) _log?.Warning("[doors] place: " + e.Message);
                    if (go != null) { try { UnityEngine.Object.Destroy(go); } catch { } }
                }
            }

            _log?.Msg("[doors] spawned " + ok + ", repaired " + repaired + ", failed " + failed);
            if (_spawned.Count > 0) DoorReport.Dump(_spawned[0], "THE FIRST CLONE we placed");

            if (_spawned.Count > 0)
                MelonLoader.MelonCoroutines.Start(SurvivalCheck());

            int culled = 0;
            for (int i = _spawned.Count - 1; i >= 0; i--)
            {
                var g = _spawned[i];
                if (g == null) { _spawned.RemoveAt(i); continue; }
                var dv = g.GetComponent<Il2Cpp.DoorV2>();
                bool usable = false;
                try
                {
                    usable = dv != null && dv.enabled
                          && (dv.HandleFront != null || dv.HandleBack != null)
                          && dv.DoorHitBox != null;
                }
                catch { }
                if (usable) continue;
                try
                {
                    if (NetworkServer.active) NetworkServer.Destroy(g);
                    else UnityEngine.Object.Destroy(g);
                }
                catch { try { UnityEngine.Object.Destroy(g); } catch { } }
                _spawned.RemoveAt(i);
                culled++;
                ok--;
            }
            if (culled > 0)
                _log?.Msg("[doors] removed " + culled + " door(s) that could not be made "
                        + "usable, so they cannot block a doorway");

            DoorWiring.WireNow();
            if (ok > 0) Verify(_spawned[0]);

            try
            {
                var pos = new List<UnityEngine.Vector3>();
                for (int i = 0; i < _spawned.Count; i++)
                    if (_spawned[i] != null) pos.Add(_spawned[i].transform.position);
                for (int i = 0; i < _relocated.Count; i++)
                    if (_relocated[i] != null) pos.Add(_relocated[i].transform.position);
                if (pos.Count > 0)
                    MapRuntime.ClearPanelsNear(pos.ToArray(), 2.0f);
            }
            catch (Exception ce) { _log?.Warning("[doors] panel cull: " + ce.Message); }

            if (MapIdentity.DoorSteelTint)
            {
                int tinted = 0;
                for (int i = 0; i < _spawned.Count; i++)
                    if (_spawned[i] != null) tinted += TintOnce(_spawned[i]);
                if (tinted > 0)
                    _log?.Msg("[doors] tinted " + tinted + " renderer(s) on the cloned "
                            + "doors to painted steel (FullDoors map)");
            }
            return ok;
        }

        static int SteelTint(GameObject door)
        {
            int n = 0;
            try
            {
                var rs = door.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rs.Length; i++)
                {
                    var r = rs[i];
                    if (r == null) continue;
                    Material m = null;
                    try { m = r.material; } catch { }
                    if (m == null) continue;
                    var steel = new Color(0.30f, 0.32f, 0.33f, 1f);
                    try
                    {
                        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", steel);
                        if (m.HasProperty("_Color")) m.SetColor("_Color", steel);
                        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.75f);
                        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.40f);
                        if (m.HasProperty("_SmoothnessRemapMax")) m.SetFloat("_SmoothnessRemapMax", 0.40f);
                        n++;
                    }
                    catch { }
                }
            }
            catch { }
            return n;
        }

        static System.Collections.IEnumerator FinishClonesLate()
        {
            float start = Time.realtimeSinceStartup;
            float earliest = start + 30f;
            float until = start + 150f;
            int known = -1, quiet = 0, painted = 0, rounds = 0;

            while (Time.realtimeSinceStartup < until)
            {
                yield return new UnityEngine.WaitForSeconds(2f);
                int seen = 0, now = 0;
                try
                {
                    var doors = UnityEngine.Object.FindObjectsOfType<Il2Cpp.DoorV2>();
                    var pos = new List<UnityEngine.Vector3>();
                    for (int i = 0; i < (doors != null ? doors.Length : 0); i++)
                    {
                        var d = doors[i];
                        if (d == null) continue;
                        var go = d.gameObject;
                        if (go == null || IsTemplate(go)) continue;
                        seen++;

                        try { pos.Add(go.transform.position); } catch { }
                        if (MapIdentity.DoorSteelTint && IsOurClone(go)) now += TintOnce(go);
                    }
                    if (pos.Count > 0) MapRuntime.ClearPanelsNear(pos.ToArray(), 2.0f);
                }
                catch (Exception e) { _log?.Warning("[doors] late clone finish: " + e.Message); }

                painted += now;
                rounds++;
                if (seen == known && now == 0) quiet++; else quiet = 0;
                known = seen;
                if (quiet >= 4 && Time.realtimeSinceStartup > earliest) break;
            }

            if (painted > 0)
                _log?.Msg("[doors] repainted " + painted + " renderer(s) on the door(s) that "
                        + "arrived from the host, and culled the map's own leaves around them. "
                        + "This is the pass a client never used to run: without it the borrowed "
                        + "doors stay depot wood here while the host sees painted steel.");
        }

        static bool IsOurClone(GameObject go)
        {
            try
            {
                var n = go.name;
                return n != null
                    && n.StartsWith("ODG_DoorTemplate", StringComparison.Ordinal)
                    && n.EndsWith("(Clone)", StringComparison.Ordinal);
            }
            catch { return false; }
        }

        static readonly HashSet<int> _tinted = new HashSet<int>();

        static int TintOnce(GameObject door)
        {
            if (door == null) return 0;
            int key;
            try { key = door.GetInstanceID(); } catch { return 0; }
            if (!_tinted.Add(key)) return 0;
            return SteelTint(door);
        }

        static readonly HashSet<int> _claimed = new HashSet<int>();

        static int MarkerKind(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            var n = name.ToLowerInvariant();
            if (n.Contains("gate") || n.Contains("fence")) return 2;
            if (n.Contains("compound") || n.Contains("barn") || n.Contains("exterior")
                || n.Contains("shed") || n.Contains("garage") || n.Contains("prayer")) return 1;
            if (n.Contains("osama") || n.Contains("house") || n.Contains("interior")
                || n.Contains("room") || n.Contains("door")) return 0;
            return -1;
        }

        static int DoorKind(Il2Cpp.DoorV2 d)
        {
            try
            {
                var n = d.gameObject.name.ToLowerInvariant();
                if (n.Contains("fence") || n.Contains("gate")) return 2;
                if (n.Contains("exterior")) return 1;
                if (n.Contains("interior")) return 0;
            }
            catch { }
            return -1;
        }

        static string KindName(int k) { return k == 2 ? "gate" : k == 1 ? "exterior" : k == 0 ? "interior" : "unknown"; }

        static int[] PairByKind(List<Il2Cpp.DoorV2> doors, List<Transform> markers)
        {
            var pick = new int[doors.Count];
            var taken = new HashSet<int>();
            for (int i = 0; i < doors.Count; i++) pick[i] = -1;
            int byKind = 0, byOrder = 0;

            var order = new List<int>();
            for (int k = 2; k >= 0; k--)
                for (int i = 0; i < doors.Count; i++)
                    if (doors[i] != null && DoorKind(doors[i]) == k) order.Add(i);
            for (int i = 0; i < doors.Count; i++)
                if (doors[i] != null && DoorKind(doors[i]) == -1) order.Add(i);
            foreach (int i in order)
            {
                int dk = DoorKind(doors[i]);
                int chosen = -1;
                if (dk >= 0)
                    for (int mi = 0; mi < markers.Count; mi++)
                    {
                        if (taken.Contains(mi) || markers[mi] == null) continue;
                        if (MarkerKind(markers[mi].name) == dk) { chosen = mi; byKind++; break; }
                    }

                if (chosen < 0 && dk != 2)
                    for (int mi = 0; mi < markers.Count; mi++)
                    {
                        if (taken.Contains(mi) || markers[mi] == null) continue;
                        chosen = mi; byOrder++; break;
                    }
                if (chosen < 0) continue;
                taken.Add(chosen);
                pick[i] = chosen;
            }
            _log?.Msg("[doors] pairing: " + byKind + " door(s) took a doorway of their own kind "
                    + "(gate/exterior/interior by marker name), " + byOrder
                    + " took the next free doorway in list order.");
            return pick;
        }

        static Il2Cpp.DoorV2 SourceFor(Transform marker, Il2Cpp.DoorV2 fallback)
        {
            try
            {
                int mk = marker != null ? MarkerKind(marker.name) : -1;
                if (mk < 0) return fallback;

                if (mk == 1 && MapIdentity.OutdoorDoorsAreGates) mk = 2;
                var doors = SceneDoors(false);
                Il2Cpp.DoorV2 best = null; int bestScore = -1;
                Il2Cpp.DoorV2 bestNonGate = null; int bestNonGateScore = -1;
                for (int i = 0; i < doors.Count; i++)
                {
                    if (doors[i] == null) continue;
                    int dk = DoorKind(doors[i]);
                    string dd; int sc = Score(doors[i], out dd);
                    if (dk == mk && sc > bestScore) { bestScore = sc; best = doors[i]; }
                    if (dk != 2 && sc > bestNonGateScore) { bestNonGateScore = sc; bestNonGate = doors[i]; }
                }
                if (best != null) return best;

                if (fallback != null && DoorKind(fallback) == 2 && bestNonGate != null) return bestNonGate;
                return fallback;
            }
            catch { return fallback; }
        }

        static int RelocateSceneDoors(List<Transform> markers)
        {
            _claimed.Clear();
            int moved = 0;
            try
            {
                var doors = SceneDoors(true);
                if (doors.Count == 0)
                {
                    _log?.Msg("[doors] no shipped scene doors to relocate; "
                            + "falling back to prefab clones for all doorways");
                    return 0;
                }

                var pick = PairByKind(doors, markers);
                for (int i = 0; i < doors.Count; i++)
                {
                    var d = doors[i];
                    int mi = pick[i];
                    if (d == null || mi < 0) continue;
                    var m = markers[mi];
                    if (m == null) continue;
                    try
                    {
                        var go = d.gameObject;

                        _movedFrom.Add(new Moved
                        {
                            Go = go,
                            Parent = go.transform.parent,
                            LocalPos = go.transform.localPosition,
                            LocalRot = go.transform.localRotation,
                            ActiveSelf = go.activeSelf,
                            CanLatch = SafeCanLatch(d, out bool haveCanLatch),
                            HadCanLatch = true,
                        });
                        _movedFrom[_movedFrom.Count - 1].HadCanLatch = haveCanLatch;

                        Transform panel = PanelOf(d, go);
                        if (moved < 3) LogDoorState(d, panel, "before move");

                        try
                        {
                            var sync = d.DoorPhysicsSync;
                            if (sync != null)
                            {
                                sync.savedLinearVelocity = Vector3.zero;
                                sync.savedAngularVelocity = Vector3.zero;
                            }
                        }
                        catch { }

                        go.transform.SetParent(null, true);

                        go.transform.rotation = m.rotation;

                        go.transform.position = m.position;
                        if (!go.activeSelf) go.SetActive(true);

                        if (Mathf.Abs(YawOffset) > 0.01f) TurnAboutHinge(go, YawOffset);

                        _relocatedPose[go] = new KeyValuePair<Vector3, Quaternion>(go.transform.position, go.transform.rotation);

                        try { Physics.SyncTransforms(); } catch { }

                        try { d.ResetDoor(); }
                        catch (Exception re) { if (moved < 3) _log?.Warning("[doors] ResetDoor: " + re.Message); }

                        CloseAndLatchLocal(d);
                        AnnounceClosed(d);
                        if (moved < 3) LogDoorState(d, panel, "after close");

                        _relocated.Add(go);
                        DoorSentry.Track(d);
                        _claimed.Add(mi);
                        moved++;
                    }
                    catch (Exception e)
                    {
                        if (moved < 3) _log?.Warning("[doors] relocate: " + e.Message);
                    }
                }

                if (moved > 0)
                    MelonLoader.MelonCoroutines.Start(SurvivalCheck());
                _log?.Msg("[doors] relocated " + moved + " shipped door(s) into doorways "
                        + "(these carry the editor wiring; the rest fall back to clones)");

                if (moved > 0)
                {
                    try { MelonLoader.MelonCoroutines.Start(HoldRelocated()); }
                    catch (Exception he) { _log?.Warning("[doors] hold could not start: " + he.Message); }
                }
                if (moved > 0) Verify(_relocated[0]);
                if (moved > 0) DumpPlacements("after placement");

                try
                {
                    var pos = new System.Collections.Generic.List<UnityEngine.Vector3>();
                    for (int i = 0; i < _relocated.Count; i++)
                    {
                        var d = _relocated[i];
                        if (d == null) continue;
                        try { pos.Add(d.transform.position); } catch { }
                    }
                    if (pos.Count > 0)
                        MapRuntime.ClearPanelsNear(pos.ToArray(), 2.0f);
                }
                catch (Exception ce) { _log?.Warning("[doors] panel cull: " + ce.Message); }
            }
            catch (Exception e) { _log?.Error("[doors] RelocateSceneDoors: " + e.Message); }
            return moved;
        }

        sealed class Moved
        {
            public GameObject Go;
            public Transform Parent;
            public Vector3 LocalPos;
            public Quaternion LocalRot;
            public bool ActiveSelf;

            public bool CanLatch;
            public bool HadCanLatch;
        }
        static readonly List<Moved> _movedFrom = new List<Moved>();

        static readonly List<GameObject> _relocated = new List<GameObject>();

        static readonly Dictionary<GameObject, KeyValuePair<Vector3, Quaternion>> _relocatedPose =
            new Dictionary<GameObject, KeyValuePair<Vector3, Quaternion>>();
        static int _holdRuns;

        static System.Collections.IEnumerator HoldRelocated()
        {
            int gen = ++_holdRuns;
            int passes = 0, restoredTotal = 0;
            bool said = false;
            while (passes++ < 240 && gen == _holdRuns)
            {
                yield return new UnityEngine.WaitForSeconds(0.5f);
                int restored = 0;
                try
                {
                    foreach (var kv in _relocatedPose)
                    {
                        var go = kv.Key;
                        if (go == null) continue;
                        var want = kv.Value;
                        if (Vector3.Distance(go.transform.position, want.Key) <= 0.3f) continue;

                        go.transform.position = want.Key;
                        go.transform.rotation = want.Value;
                        restored++;
                    }
                    if (restored > 0)
                    {
                        try { Physics.SyncTransforms(); } catch { }
                        restoredTotal += restored;
                        if (!said)
                        {
                            said = true;
                            _log?.Warning("[doors] " + restored + " relocated door(s) had been put back at the host level's "
                                        + "original spots (the network spawn payload landed after our placement) -- "
                                        + "re-placed on their markers. This is why a door the host walked through "
                                        + "stood in mid-air for everyone else.");
                        }
                    }
                }
                catch (Exception e) { _log?.Warning("[doors] hold: " + e.Message); }
            }
            if (restoredTotal > 0)
                _log?.Msg("[doors] hold ended: " + restoredTotal + " re-placement(s) over " + passes + " pass(es).");
        }

        static bool SafeCanLatch(Il2Cpp.DoorV2 d, out bool read)
        {
            try { bool v = d.canLatch; read = true; return v; }
            catch { read = false; return false; }
        }

        public static int RestoreRelocated()
        {
            int n = 0;
            int latchRestored = 0;
            for (int i = _movedFrom.Count - 1; i >= 0; i--)
            {
                var m = _movedFrom[i];
                if (m == null || m.Go == null) continue;
                try
                {
                    m.Go.transform.SetParent(m.Parent, false);
                    m.Go.transform.localPosition = m.LocalPos;
                    m.Go.transform.localRotation = m.LocalRot;
                    if (m.Go.activeSelf != m.ActiveSelf) m.Go.SetActive(m.ActiveSelf);

                    if (m.HadCanLatch)
                    {
                        var dv = m.Go.GetComponent<Il2Cpp.DoorV2>();
                        if (dv != null && dv.canLatch != m.CanLatch)
                        { dv.canLatch = m.CanLatch; latchRestored++; }
                    }
                    n++;
                }
                catch { }
            }
            if (latchRestored > 0)
                _log?.Msg("[doors] put canLatch back on " + latchRestored
                        + " borrowed door(s) that were authored not to latch");
            _movedFrom.Clear();
            _relocated.Clear();
            if (n > 0) _log?.Msg("[doors] returned " + n + " borrowed door(s) to the host level");
            return n;
        }

        static System.Collections.IEnumerator SurvivalCheck()
        {
            yield return null;
            yield return null;

            int reclosed = 0;
            var all = new List<GameObject>(_relocated);
            all.AddRange(_spawned);
            foreach (var g in all)
            {
                if (g == null) continue;
                var dv = g.GetComponent<Il2Cpp.DoorV2>();
                if (dv == null) continue;

                bool shut = true;
                float cr = 0f, pr = 0f, vel = 0f;
                try
                {
                    cr = dv.currentRotation; pr = dv.PredictedRotation; vel = dv.Velocity;
                    shut = Mathf.Abs(cr) < 0.05f && Mathf.Abs(pr) < 0.05f && Mathf.Abs(vel) < 0.05f;
                }
                catch { }
                if (shut) continue;
                try
                {
                    _log?.Warning("[doors] '" + g.name + "' off-pose after placement: currentRotation="
                                + cr.ToString("F2") + " predicted=" + pr.ToString("F2")
                                + " velocity=" + vel.ToString("F2")
                                + " latched=" + dv.IsLatched + " canLatch=" + dv.canLatch);
                    CloseAndLatchLocal(dv);
                    AnnounceClosed(dv);
                    reclosed++;
                }
                catch { }
            }
            if (reclosed > 0)
                _log?.Msg("[doors] re-closed " + reclosed + " door(s) that drifted open "
                        + "after placement (momentum carried through the teleport)");

            int alive = 0, dead = 0;

            var placed = new List<GameObject>(_relocated);
            placed.AddRange(_spawned);
            for (int i = 0; i < placed.Count; i++)
            {
                if (placed[i] == null) dead++; else alive++;
            }

            if (dead == 0)
                _log?.Msg("[doors] SURVIVAL CHECK: all " + alive
                        + " door(s) still alive a frame later ("
                        + _relocated.Count + " relocated, " + _spawned.Count + " cloned)");
            else
                _log?.Error("[doors] SURVIVAL CHECK: " + dead + " of "
                          + (alive + dead) + " door(s) were DESTROYED within a frame of "
                          + "being created. That is Mirror rejecting a clone of a scene "
                          + "NetworkIdentity, not a placement problem.");
        }

        static Transform PanelOf(Il2Cpp.DoorV2 d, GameObject go)
        {
            try { if (d != null && d.PivotTransform != null) return d.PivotTransform; } catch { }
            return go != null ? go.transform : null;
        }

        static void LogDoorState(Il2Cpp.DoorV2 d, Transform panel, string tag)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("[doors] ").Append(tag).Append(" '").Append(d.gameObject.name).Append("'");
                try { sb.Append("  currentRotation=").Append(d.currentRotation.ToString("F1")); } catch { }
                try { sb.Append(" predicted=").Append(d.PredictedRotation.ToString("F1")); } catch { }
                try { sb.Append(" velocity=").Append(d.Velocity.ToString("F2")); } catch { }
                try { sb.Append(" maxRotationY=").Append(d.maxRotationY.ToString("F0")); } catch { }
                try { sb.Append(" invert=").Append(d.Invert); } catch { }
                try { sb.Append(" latched=").Append(d.IsLatched); } catch { }
                try { sb.Append(" locked=").Append(d.CurrentlyLocked); } catch { }
                try { sb.Append(" dead=").Append(d.isDead); } catch { }
                try { sb.Append(" kinematic=").Append(d.rb != null ? d.rb.isKinematic.ToString() : "<no rb>"); } catch { }
                if (panel != null)
                    sb.Append(" pivotLocalEuler=").Append(panel.localEulerAngles.ToString("F1"));
                try
                {
                    var sync = d.DoorPhysicsSync;
                    if (sync != null)
                    {
                        sb.Append("\n         sync UseLocalSpace=").Append(sync.UseLocalSpace);
                        try { sb.Append(" targetPos=").Append(sync.targetPosition.ToString("F1")); } catch { }
                        try { sb.Append(" targetRot=").Append(sync.targetRotation.eulerAngles.ToString("F1")); } catch { }
                    }
                    else sb.Append("  sync=<none>");
                }
                catch { }
                _log?.Msg(sb.ToString());
            }
            catch (Exception e) { _log?.Warning("[doors] anatomy: " + e.Message); }
        }

        static bool Repair(Il2Cpp.DoorV2 d, GameObject go)
        {
            bool changed = false;

            try
            {
                if (!d.enabled) { d.enabled = true; changed = true; }
            }
            catch { }

            try
            {
                var handles = go.GetComponentsInChildren<Il2Cpp.DoorHandleV2>(true);
                if (handles != null && handles.Length > 0)
                {
                    if (d.HandleFront == null)
                    {
                        d.HandleFront = handles[0];
                        changed = true;
                    }
                    if (d.HandleBack == null)
                    {
                        d.HandleBack = handles.Length > 1 ? handles[1] : handles[0];
                        changed = true;
                    }

                    for (int i = 0; i < handles.Length; i++)
                    {
                        if (handles[i] == null) continue;
                        if (handles[i].doorV2 == null) { handles[i].doorV2 = d; changed = true; }
                    }
                }
                else _log?.Warning("[doors] prefab has NO DoorHandleV2 in its hierarchy -- "
                                 + "this door can never be grabbed");
            }
            catch (Exception e) { _log?.Warning("[doors] handles: " + e.Message); }

            try
            {
                if (d.DoorHitBox == null)
                {
                    var child = go.transform.Find(HitBoxChildName);
                    GameObject hb;
                    if (child != null) hb = child.gameObject;
                    else
                    {
                        hb = new GameObject(HitBoxChildName);
                        hb.transform.SetParent(go.transform, false);
                        hb.transform.localPosition = HitBoxLocalPos;
                        hb.transform.localRotation = Quaternion.identity;
                        hb.transform.localScale = HitBoxLocalScale;
                    }
                    var bc = hb.GetComponent<BoxCollider>();
                    if (bc == null) bc = hb.AddComponent<BoxCollider>();
                    bc.size = Vector3.one;
                    bc.center = Vector3.zero;
                    bc.isTrigger = false;
                    d.DoorHitBox = bc;
                    changed = true;
                }
            }
            catch (Exception e) { _log?.Warning("[doors] hitbox: " + e.Message); }

            try
            {
                if (d.PivotTransform == null)
                {
                    Transform pivot = null;
                    try { pivot = go.transform.Find("Door Pivot and rigidbody"); } catch { }
                    if (pivot == null && d.rb != null)
                    {

                        try { pivot = d.rb.transform; } catch { }
                    }
                    if (pivot != null && pivot != go.transform)
                    {
                        d.PivotTransform = pivot;
                        changed = true;
                    }
                    else
                    {
                        d.PivotTransform = go.transform;
                        changed = true;
                        _log?.Warning("[doors] '" + go.name + "' has no pivot child; falling back to "
                                    + "the ROOT. Update will overwrite this door's placement "
                                    + "rotation with Euler(0,angle,0) every frame.");
                    }
                }
            }
            catch { }

            return changed;
        }

        public static void Verify(GameObject door)
        {
            if (door == null) { _log?.Msg("[doors] nothing to verify"); return; }
            try
            {
                var d = door.GetComponent<Il2Cpp.DoorV2>();
                if (d == null) { _log?.Warning("[doors] verify: no DoorV2"); return; }

                string detail;
                int s = Score(d, out detail);
                var ni = door.GetComponent<NetworkIdentity>();
                _log?.Msg("[doors] verify '" + door.name + "': score " + s + "/12  " + detail);
                _log?.Msg("        netId=" + (ni != null ? ni.netId.ToString() : "<none>")
                        + "  isServer=" + (ni != null && ni.isServer)
                        + "  isClient=" + (ni != null && ni.isClient));

                var h = d.HandleFront;
                if (h == null) { _log?.Warning("        HandleFront NULL -- will never tick"); return; }
                _log?.Msg("        handle dist=" + h.distanceHandleToPlayer
                        + " center=" + h.distanceCenterToPlayer
                        + "  (0 forever == the old failure; walk up to a door and re-check)");
            }
            catch (Exception e) { _log?.Warning("[doors] verify: " + e.Message); }
        }

        public static void Clear()
        {
            int n = 0;
            foreach (var go in _spawned)
            {
                if (go == null) continue;
                try
                {
                    if (NetworkServer.active) NetworkServer.Destroy(go);
                    else UnityEngine.Object.Destroy(go);
                    n++;
                }
                catch { }
            }
            _spawned.Clear();
            _log?.Msg("[doors] removed " + n);
        }
    }
}
