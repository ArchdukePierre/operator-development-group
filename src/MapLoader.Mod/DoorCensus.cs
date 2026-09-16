// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using NetworkIdentity = Il2CppMirror.NetworkIdentity;

namespace MapLoader
{

    public static class DoorCensus
    {
        static MelonLogger.Instance _log;
        static object _running;

        public static void Init(MelonLogger.Instance log) { _log = log; }

        public static void Arm()
        {
            try { _running = MelonCoroutines.Start(Run()); }
            catch (Exception e) { _log?.Warning("[doors] census: " + e.Message); }
        }

        static IEnumerator Run()
        {
            yield return new WaitForSeconds(5f);
            Take("5 s after placement");
            yield return new WaitForSeconds(25f);
            Take("30 s after placement");
            State("30 s after placement");
            for (int m = 2; m <= 8; m += 2)
            {
                yield return new WaitForSeconds(120f);
                State(m + " min in");
            }
        }

        static bool _saidComponents;

        public static string Describe(Il2Cpp.DoorV2 d)
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                uint netId = 0; bool owned = false, srv = false, cli = false, clientOwned = false;
                try
                {
                    var ni = d.GetComponent<NetworkIdentity>();
                    if (ni != null)
                    {
                        netId = ni.netId; owned = ni.isOwned; srv = ni.isServer; cli = ni.isClient;
                        try { clientOwned = ni.connectionToClient != null; } catch { }
                    }
                }
                catch { }
                float rot = 0f; try { rot = d.currentRotation; } catch { }

                float pred = 0f; try { pred = d.PredictedRotation; } catch { }
                bool latched = false, inUse = false, locked = false;
                try { latched = d.IsLatched; } catch { }
                try { inUse = d.inUse; } catch { }
                try { locked = d.CurrentlyLocked; } catch { }

                string sync = "none";
                try
                {
                    var ms = d.GetComponentInChildren<Il2Cpp.MilkRigidbodySync>(true);
                    if (ms != null)
                    {
                        string owner = "";
                        try { owner = ms.HasOwner ? (ms.IsCurrentOwner ? " ownedByUs" : " ownedByThem") : " unowned"; } catch { }
                        sync = "milk" + owner;
                    }
                }
                catch { }
                try
                {
                    if (sync == "none")
                    {
                        var ss = d.GetComponentInChildren<Il2CppSmooth.SmoothSyncMirror>(true);
                        if (ss != null)
                        {
                            string child = "";
                            try { child = ss.childObjectToSync != null ? " child=" + ss.childObjectToSync.name : ""; } catch { }
                            sync = "smooth" + child;
                        }
                    }
                }
                catch { }

                if (!_saidComponents)
                {
                    try
                    {
                        var nbs = d.GetComponentsInChildren<Il2CppMirror.NetworkBehaviour>(true);
                        var names = new System.Text.StringBuilder();
                        for (int i = 0; i < (nbs != null ? nbs.Length : 0); i++)
                        {
                            if (nbs[i] == null) continue;
                            if (names.Length > 0) names.Append(", ");
                            names.Append(nbs[i].GetIl2CppType().Name).Append('@').Append(nbs[i].gameObject.name);
                        }
                        _saidComponents = true;
                        _log?.Msg("[doors] a door's network components: " + names);
                    }
                    catch { }
                }
                sb.Append("netId=").Append(netId).Append(" rot=").Append(rot.ToString("0"))
                  .Append(Math.Abs(pred) > 1f ? " leaf=" + (rot + pred).ToString("0") + " (predicted here, not from the host)" : "")
                  .Append(" latched=").Append(latched).Append(" inUse=").Append(inUse).Append(" locked=").Append(locked)
                  .Append(" owned=").Append(owned).Append(srv ? " server" : "").Append(cli ? " client" : "")
                  .Append(clientOwned ? " ownedByAClient" : "").Append(" sync=").Append(sync);
            }
            catch (Exception e) { sb.Append("(unreadable: ").Append(e.Message).Append(")"); }
            return sb.ToString();
        }

        public static string State(string why)
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                bool server = false;
                try { server = Il2CppMirror.NetworkServer.active; } catch { }
                var all = Resources.FindObjectsOfTypeAll<Il2Cpp.DoorV2>();
                int total = 0, open = 0, busy = 0, unlatched = 0;
                var lines = new List<string>();
                for (int i = 0; i < (all != null ? all.Length : 0); i++)
                {
                    var d = all[i];
                    try
                    {
                        if (d == null || d.gameObject == null) continue;
                        if (!d.gameObject.scene.IsValid()) continue;
                        if (MapDoors.IsTemplate(d.gameObject)) continue;
                        if (!d.gameObject.activeInHierarchy) continue;
                        total++;
                        float rot = 0f; try { rot = d.currentRotation; } catch { }
                        bool latched = true, inUse = false;
                        try { latched = d.IsLatched; } catch { }
                        try { inUse = d.inUse; } catch { }
                        bool isOpen = Math.Abs(rot) > 5f;
                        if (isOpen) open++;
                        if (inUse) busy++;
                        if (!latched) unlatched++;
                        if ((isOpen || inUse || !latched) && lines.Count < 40)
                        {
                            var p = d.transform.position;
                            lines.Add("   '" + d.gameObject.name + "' " + Describe(d) + " at ("
                                    + p.x.ToString("0.0") + ", " + p.y.ToString("0.0") + ", " + p.z.ToString("0.0") + ")");
                        }
                    }
                    catch { }
                }
                sb.Append("[doors] state (" + why + ", " + (server ? "HOST" : "CLIENT") + "): " + total
                        + " door(s), " + open + " open (more than 5 deg), " + busy + " in use, " + unlatched
                        + " unlatched. Same netId at the same minute on the other machine: a different angle is the desync.");
                _log?.Msg(sb.ToString());
                for (int i = 0; i < lines.Count; i++) _log?.Msg("[doors]" + lines[i]);
            }
            catch (Exception e) { _log?.Warning("[doors] state: " + e.Message); }
            return sb.ToString();
        }

        public static string Take(string why)
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                bool server = false;
                try { server = Il2CppMirror.NetworkServer.active; } catch { }
                var markers = new List<Vector3>();
                try
                {
                    var ms = MapDoors.FindDoorMarkers();
                    for (int i = 0; i < (ms != null ? ms.Count : 0); i++)
                        if (ms[i] != null) markers.Add(ms[i].position);
                }
                catch { }
                var all = Resources.FindObjectsOfTypeAll<Il2Cpp.DoorV2>();
                int total = 0, atMarker = 0, away = 0, unspawned = 0, hidden = 0;
                var lines = new List<string>();
                for (int i = 0; i < (all != null ? all.Length : 0); i++)
                {
                    var d = all[i];
                    try
                    {
                        if (d == null || d.gameObject == null) continue;
                        if (!d.gameObject.scene.IsValid()) continue;
                        var go = d.gameObject;
                        if (MapDoors.IsTemplate(go)) continue;
                        total++;
                        var p = go.transform.position;
                        float best = float.MaxValue;
                        for (int k = 0; k < markers.Count; k++)
                        {
                            float dd = Vector3.Distance(p, markers[k]);
                            if (dd < best) best = dd;
                        }
                        uint netId = 0; bool isClient = false;
                        try { var ni = go.GetComponent<NetworkIdentity>(); if (ni != null) { netId = ni.netId; isClient = ni.isClient; } } catch { }
                        bool active = go.activeInHierarchy;
                        bool drawn = false;
                        try
                        {
                            var rs = go.GetComponentsInChildren<Renderer>(false);
                            for (int r = 0; r < rs.Length; r++)
                                if (rs[r] != null && rs[r].enabled && !rs[r].forceRenderingOff) { drawn = true; break; }
                        }
                        catch { }
                        bool near = markers.Count > 0 && best < 1.0f;
                        if (near) atMarker++; else away++;
                        if (netId == 0) unspawned++;
                        if (!active || !drawn) hidden++;
                        if (!near || netId == 0 || !active || !drawn)
                            lines.Add("   '" + go.name + "' netId=" + netId + " isClient=" + isClient
                                    + " active=" + active + " drawn=" + drawn
                                    + " at (" + p.x.ToString("0.0") + ", " + p.y.ToString("0.0") + ", " + p.z.ToString("0.0") + ")"
                                    + (markers.Count > 0 ? " nearest marker " + best.ToString("0.0") + " m" : " (no markers)"));
                    }
                    catch { }
                }
                sb.Append("[doors] census (" + why + ", " + (server ? "HOST" : "CLIENT") + "): " + total
                        + " door(s) in scene, " + atMarker + " within 1 m of a marker, " + away + " away from every marker, "
                        + unspawned + " with netId 0 (never spawned to this machine), " + hidden + " not active or not drawn."
                        + (lines.Count == 0 ? " Every door is where the markers say and visible." : ""));
                _log?.Msg(sb.ToString());
                for (int i = 0; i < lines.Count && i < 20; i++) _log?.Msg("[doors]" + lines[i]);
            }
            catch (Exception e) { _log?.Warning("[doors] census: " + e.Message); }
            return sb.ToString();
        }
    }
}
