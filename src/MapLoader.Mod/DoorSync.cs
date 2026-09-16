// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using NetworkIdentity = Il2CppMirror.NetworkIdentity;

namespace MapLoader
{

    static class DoorSync
    {
        static MelonLogger.Instance _log;
        public static bool Enabled = true;

        const float SettleSeconds = 0.8f;

        const float SendGap = 0.2f;

        const float AfterMoveCooldown = 10f;
        const float OpenRecheckFirst = 15f;
        const float OpenRecheckMax = 60f;

        const float AnswerWindow = 0.75f;

        const float OpenDegrees = 5f;

        const float ScanSeconds = 10f;

        sealed class Watch
        {
            public Il2Cpp.DoorV2 D;
            public string Name;
            public uint NetId;
            public float LastRot;
            public float StillSince;
            public float NextAsk;
            public bool Primed;
            public bool Moved;
            public bool Pending;
            public float AskedAt;
            public float AskedRot;
            public float OpenBeat = OpenRecheckFirst;
        }

        static readonly List<Watch> _doors = new List<Watch>();
        static float _nextScan;
        static float _nextSend;
        static int _cursor;

        static int _asked, _agreed, _corrected;
        static float _worst;
        static string _worstName = "";
        static float _nextSummary;

        public static void Init(MelonLogger.Instance log) { _log = log; }

        public static void ResetForNewLevel(string why)
        {
            _doors.Clear();
            _nextScan = 0f; _nextSend = 0f; _cursor = 0;
            _asked = 0; _agreed = 0; _corrected = 0;
            _worst = 0f; _worstName = ""; _nextSummary = 0f;
        }

        public static void Tick()
        {
            if (!Enabled) return;

            bool server = false, client = false;
            try { server = Il2CppMirror.NetworkServer.active; } catch { }
            if (server) return;
            try { client = Il2CppMirror.NetworkClient.active && Il2CppMirror.NetworkClient.ready; } catch { }
            if (!client) { if (_doors.Count > 0) _doors.Clear(); return; }

            float now = Time.time;
            if (now >= _nextScan) { _nextScan = now + ScanSeconds; Rescan(now); }
            if (_doors.Count == 0) return;

            int n = _doors.Count;
            for (int step = 0; step < n; step++)
            {
                _cursor++;
                if (_cursor >= n) _cursor = 0;
                var w = _doors[_cursor];
                if (w == null || w.D == null) continue;

                float rot;
                try { rot = Leaf(w.D); } catch { continue; }

                if (w.Pending)
                {
                    if (now - w.AskedAt < AnswerWindow) { w.LastRot = rot; continue; }
                    Verdict(w, rot);
                    continue;
                }

                if (Mathf.Abs(rot - w.LastRot) > 0.25f)
                {
                    w.LastRot = rot;
                    w.StillSince = now;
                    w.Moved = true;
                    continue;
                }

                bool open = Mathf.Abs(rot) > OpenDegrees;

                if (w.Primed && !w.Moved && !open) continue;
                if (now - w.StillSince < SettleSeconds) continue;
                if (now < w.NextAsk) continue;
                if (now < _nextSend) break;

                Ask(w, now, rot, open);
                break;
            }

            if (_nextSummary == 0f) _nextSummary = now + 60f;
            else if (now >= _nextSummary)
            {
                _nextSummary = now + 60f;
                if (_asked > 0)
                {
                    string worst = _corrected > 0
                        ? "; worst gap " + _worst.ToString("0") + " deg on '" + _worstName + "'" : "";
                    _log?.Msg("[doorsync] 60 s: asked the host about " + _asked + " door(s), "
                            + _agreed + " already matched, " + _corrected + " were wrong here and got the host's angle"
                            + worst);
                    _asked = 0; _agreed = 0; _corrected = 0; _worst = 0f; _worstName = "";
                }
            }
        }

        static void Ask(Watch w, float now, float rot, bool open)
        {
            _nextSend = now + SendGap;
            w.Pending = true;
            w.AskedAt = now;
            w.AskedRot = rot;
            w.Moved = false;
            w.NextAsk = now + (open ? w.OpenBeat : AfterMoveCooldown);
            _asked++;
            try { w.D.CMD_RequestCurrentDoorState(null); }
            catch (Exception e)
            {
                w.Pending = false;
                _log?.Warning("[doorsync] '" + w.Name + "': " + e.Message);
                w.NextAsk = now + 60f;
            }
        }

        static void Verdict(Watch w, float rot)
        {
            w.Pending = false;
            w.Primed = true;

            try { w.D.PredictedRotation = 0f; } catch { }
            try { w.D.Velocity = 0f; } catch { }
            try { rot = w.D.currentRotation; } catch { }
            w.LastRot = rot;
            w.StillSince = Time.time;
            float gap = Mathf.Abs(rot - w.AskedRot);
            if (gap > OpenDegrees)
            {
                _corrected++;
                if (gap > _worst) { _worst = gap; _worstName = w.Name; }
                w.OpenBeat = OpenRecheckFirst;
                _log?.Msg("[doorsync] '" + w.Name + "' netId=" + w.NetId + " stood at "
                        + w.AskedRot.ToString("0") + " deg here and the host answered "
                        + rot.ToString("0") + " deg. The host's angle wins. This door's motion was lost on the wire"
                        + " (DoorV2 sends rotation unreliably and only resends while the host's own angle is moving),"
                        + " so it would have stayed wrong for the rest of the mission.");
            }
            else
            {
                _agreed++;
                w.OpenBeat = Mathf.Min(w.OpenBeat * 2f, OpenRecheckMax);
            }
        }

        static void Rescan(float now)
        {
            var seen = new Dictionary<int, Watch>();
            for (int i = 0; i < _doors.Count; i++)
            {
                var w = _doors[i];
                if (w == null || w.D == null) continue;
                try { seen[w.D.GetInstanceID()] = w; } catch { }
            }

            _doors.Clear();
            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<Il2Cpp.DoorV2> all = null;
            try { all = UnityEngine.Object.FindObjectsOfType<Il2Cpp.DoorV2>(); } catch { }
            for (int i = 0; i < (all != null ? all.Length : 0); i++)
            {
                var d = all[i];
                try
                {
                    if (d == null || d.gameObject == null) continue;
                    if (!d.gameObject.activeInHierarchy) continue;
                    if (MapDoors.IsTemplate(d.gameObject)) continue;
                    uint netId = 0;
                    var ni = d.gameObject.GetComponent<NetworkIdentity>();
                    if (ni == null) continue;
                    netId = ni.netId;
                    if (netId == 0) continue;
                    Watch w;
                    if (!seen.TryGetValue(d.GetInstanceID(), out w) || w == null)
                    {
                        w = new Watch
                        {
                            D = d,
                            Name = d.gameObject.name,
                            NetId = netId,
                            LastRot = SafeRot(d),
                            StillSince = now,
                            NextAsk = 0f,
                        };
                    }
                    else { w.D = d; w.NetId = netId; }
                    _doors.Add(w);
                }
                catch { }
            }
        }

        static float Leaf(Il2Cpp.DoorV2 d)
        {
            float rot = 0f, pred = 0f;
            try { rot = d.currentRotation; } catch { }
            try { pred = d.PredictedRotation; } catch { }
            return rot + pred;
        }

        static float SafeRot(Il2Cpp.DoorV2 d)
        {
            try { return Leaf(d); } catch { return 0f; }
        }

        public static string Console(string arg)
        {
            arg = (arg ?? "").Trim().ToLowerInvariant();
            if (arg == "off") { Enabled = false; return "door sync off. A door opened here that the host never heard about will stay open here for the rest of the mission."; }
            if (arg == "on") { Enabled = true; _nextScan = 0f; return "door sync on."; }

            bool server = false;
            try { server = Il2CppMirror.NetworkServer.active; } catch { }
            if (server) return "door sync: this machine is the host, so there is nobody to ask. Clients do the asking.";
            int open = 0, pending = 0;
            for (int i = 0; i < _doors.Count; i++)
            {
                var w = _doors[i];
                if (w == null || w.D == null) continue;
                if (Mathf.Abs(SafeRot(w.D)) > OpenDegrees) open++;
                if (w.Pending) pending++;
            }
            return "door sync " + (Enabled ? "on" : "off") + ": watching " + _doors.Count + " door(s), "
                 + open + " open, " + pending + " question(s) out. This minute: asked " + _asked
                 + ", matched " + _agreed + ", corrected " + _corrected + ".";
        }
    }
}
