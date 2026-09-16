// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;

namespace MapLoader
{

    public static class DoorWiring
    {
        static MelonLogger.Instance _log;
        static float _next;
        static int _wired;
        static int _enabled;
        static int _lastPlayerId;

        static readonly List<Il2Cpp.DoorV2> _enabledByUs = new List<Il2Cpp.DoorV2>();

        public static void Init(MelonLogger.Instance log) { _log = log; }

        public static void ResetForNewLevel()
        {
            _wired = 0;
            _lastPlayerId = 0;
            _next = 0f;

            _enabledByUs.Clear();
        }

        public static int RestoreEnabled()
        {
            int n = 0;
            for (int i = _enabledByUs.Count - 1; i >= 0; i--)
            {
                var dv = _enabledByUs[i];
                if (dv == null) continue;
                try { dv.enabled = false; n++; } catch { }
            }
            _enabledByUs.Clear();
            if (n > 0)
                _log?.Msg("[doorwire] switched " + n + " borrowed DoorV2 script(s) back off");
            return n;
        }

        public static Il2Cpp.PlayerNetworking LocalPlayer()
        {

            try
            {
                var p = Il2Cpp.GameManager.myPlayerNetworking;
                if (p != null) return p;
            }
            catch { }
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<Il2Cpp.PlayerNetworking>();
                if (all != null)
                    for (int i = 0; i < all.Length; i++)
                    {
                        var p = all[i];
                        if (p == null) continue;
                        bool mine = false;
                        try { mine = p.isLocalPlayer; } catch { }
                        if (mine) return p;
                    }
            }
            catch { }
            return null;
        }

        public static void Tick()
        {
            if (Time.realtimeSinceStartup < _next) return;
            _next = Time.realtimeSinceStartup + 0.5f;

            try
            {
                var player = LocalPlayer();
                if (player == null) return;

                int pid = 0;
                try { pid = player.GetInstanceID(); } catch { }
                bool playerChanged = pid != _lastPlayerId;
                if (playerChanged)
                {
                    _lastPlayerId = pid;
                    _wired = 0;
                    _log?.Msg("[doorwire] local player appeared or changed; "
                            + "re-pushing it into our door handles");
                }

                int before = _enabled;
                int done = Wire(player, playerChanged);
                if (done > 0)
                    _log?.Msg("[doorwire] pushed the local player into " + done
                            + " door handle(s)");
                if (_enabled > before)
                    _log?.Msg("[doorwire] switched ON " + (_enabled - before)
                            + " DoorV2 script(s) that shipped disabled");
            }
            catch (Exception e) { _log?.Warning("[doorwire] " + e.Message); }
        }

        static int Wire(Il2Cpp.PlayerNetworking player, bool force)
        {
            int n = 0;
            try
            {
                var handles = Resources.FindObjectsOfTypeAll<Il2Cpp.DoorHandleV2>();
                if (handles == null) return 0;

                for (int i = 0; i < handles.Length; i++)
                {
                    var h = handles[i];
                    if (h == null) continue;
                    GameObject go = null;
                    try { go = h.gameObject; } catch { }
                    if (go == null) continue;
                    if (MapDoors.IsTemplate(go)) continue;
                    if (!go.scene.IsValid()) continue;
                    if (!go.activeInHierarchy) continue;

                    try
                    {
                        var dv = h.doorV2;
                        if (dv != null && !dv.enabled)
                        {
                            dv.enabled = true;
                            _enabled++;

                            _enabledByUs.Add(dv);
                        }
                    }
                    catch { }

                    if (!force)
                    {

                        try { if (h.MyLocalPlayer != null) continue; } catch { }
                    }

                    try { h.SetPlayerObject(player); n++; }
                    catch (Exception e)
                    {
                        if (n < 3) _log?.Warning("[doorwire] SetPlayerObject: " + e.Message);
                    }
                }
            }
            catch (Exception e) { _log?.Warning("[doorwire] Wire: " + e.Message); }
            _wired += n;
            return n;
        }

        public static int WireNow()
        {
            var p = LocalPlayer();
            if (p == null)
            {
                _log?.Msg("[doorwire] no local player yet; the periodic pass will "
                        + "push it in as soon as there is one");
                return 0;
            }
            int before = _enabled;
            int n = Wire(p, true);
            _log?.Msg("[doorwire] pushed the local player into " + n + " door handle(s)");
            if (_enabled > before)
                _log?.Msg("[doorwire] switched ON " + (_enabled - before)
                        + " DoorV2 script(s) that shipped disabled");
            return n;
        }
    }
}
