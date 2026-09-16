// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;

namespace MapLoader
{

    public static class DoorListener
    {
        static MelonLogger.Instance _log;
        static bool _on;
        static float _next;
        static string _last = "";

        public static void Init(MelonLogger.Instance log) { _log = log; }

        public static void Toggle()
        {
            _on = !_on;
            _log?.Msg("[doorlisten] " + (_on ? "ON -- walk up to a door" : "off"));
            if (_on) { _next = 0f; _last = ""; }
        }

        public static void HandleInput()
        {
            try
            {
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (!Input.GetKeyDown(KeyCode.Backspace)) return;

                if (shift) DumpNearest();
                else Toggle();
            }
            catch (Exception e) { _log?.Warning("[doorlisten] input: " + e.Message); }
        }

        public static void DumpNearest()
        {
            var d = Nearest(out float dist);
            if (d == null)
            {
                _log?.Msg("[doorlisten] manual dump: no live door anywhere in the scene");
                return;
            }
            _log?.Msg("[doorlisten] manual dump of the nearest door, "
                    + dist.ToString("F1") + " m away");
            DoorReport.Dump(d.gameObject, "manual dump (Shift+Backspace)");
        }

        static Il2Cpp.DoorV2 Nearest(out float dist)
        {
            dist = float.MaxValue;
            Il2Cpp.DoorV2 near = null;
            try
            {
                var cam = Camera.main;
                if (cam == null) return null;
                Vector3 eye = cam.transform.position;

                var all = Resources.FindObjectsOfTypeAll<Il2Cpp.DoorV2>();
                if (all == null) return null;
                float bestSq = float.MaxValue;
                for (int i = 0; i < all.Length; i++)
                {
                    var d = all[i];
                    if (d == null || d.gameObject == null) continue;
                    if (!d.gameObject.scene.IsValid()) continue;
                    if (MapDoors.IsTemplate(d.gameObject)) continue;
                    float sq = (d.transform.position - eye).sqrMagnitude;
                    if (sq < bestSq) { bestSq = sq; near = d; }
                }
                if (near != null) dist = Mathf.Sqrt(bestSq);
            }
            catch { }
            return near;
        }

        public static void Tick()
        {
            if (!_on) return;
            if (Time.realtimeSinceStartup < _next) return;
            _next = Time.realtimeSinceStartup + 0.25f;

            try
            {
                var cam = Camera.main;
                if (cam == null) { Say("no camera"); return; }
                Vector3 eye = cam.transform.position;

                var all = Resources.FindObjectsOfTypeAll<Il2Cpp.DoorV2>();
                if (all == null || all.Length == 0) { Say("no DoorV2 in memory"); return; }

                Il2Cpp.DoorV2 near = null;
                float bestSq = float.MaxValue;
                for (int i = 0; i < all.Length; i++)
                {
                    var d = all[i];
                    if (d != null && d.gameObject != null && MapDoors.IsTemplate(d.gameObject)) continue;
                    if (d == null || d.gameObject == null) continue;
                    if (!d.gameObject.scene.IsValid()) continue;
                    if (!d.gameObject.activeInHierarchy) continue;
                    float sq = (d.transform.position - eye).sqrMagnitude;
                    if (sq < bestSq) { bestSq = sq; near = d; }
                }
                if (near == null) { Say("no live door"); return; }

                float dist = Mathf.Sqrt(bestSq);
                if (dist > 12f) { Say("nearest door " + dist.ToString("F1") + " m away"); return; }

                Say(Describe(near, dist));
            }
            catch (Exception e) { _log?.Warning("[doorlisten] " + e.Message); }
        }

        static string Describe(Il2Cpp.DoorV2 d, float dist)
        {
            string name = "?";
            try { name = d.gameObject.name; } catch { }
            bool ours = name.StartsWith("MapLoader_Door", StringComparison.Ordinal);

            string hd = "-", cd = "-", who = "-";
            try
            {
                var h = d.HandleFront != null ? d.HandleFront : d.HandleBack;
                if (h != null)
                {
                    try { hd = h.distanceHandleToPlayer.ToString("F2"); } catch { }
                    try { cd = h.distanceCenterToPlayer.ToString("F2"); } catch { }
                    try { who = h.MyLocalPlayer != null ? "SET" : "NULL"; } catch { }
                }
            }
            catch { }

            string hitbox = "?", handles = "?", locked = "?", inUse = "?", enabled = "?";
            try { hitbox = d.DoorHitBox != null ? "ok" : "NULL"; } catch { }
            try
            {
                bool f = d.HandleFront != null, b = d.HandleBack != null;
                handles = (f ? "F" : "-") + (b ? "B" : "-");
            }
            catch { }
            try { locked = d.CurrentlyLocked.ToString(); } catch { }
            try { inUse = d.inUse.ToString(); } catch { }
            try { enabled = d.enabled.ToString(); } catch { }

            string pose = "?";
            try
            {
                pose = d.currentRotation.ToString("F1") + "/" + d.PredictedRotation.ToString("F1")
                     + "/" + d.Velocity.ToString("F2")
                     + " latched=" + d.IsLatched + " canLatch=" + d.canLatch;
            }
            catch { }

            string net = "?";
            try
            {
                var ni = d.GetComponent<Il2CppMirror.NetworkIdentity>();
                net = ni != null ? ("netId=" + ni.netId) : "no NetworkIdentity";
            }
            catch { }

            string scene = "?";
            try { scene = d.gameObject.scene.name; } catch { }

            return (ours ? "OURS  " : "SHIPPED ") + "'" + name + "'"
                 + "  " + dist.ToString("F1") + "m"
                 + "  MyLocalPlayer=" + who
                 + "  handleDist=" + hd + " centerDist=" + cd
                 + "  hitbox=" + hitbox + " handles=" + handles
                 + "  enabled=" + enabled + " locked=" + locked + " inUse=" + inUse
                 + "  rot/pred/vel=" + pose
                 + "  " + net + "  scene='" + scene + "'";
        }

        internal static bool Trace;

        static void Say(string s)
        {
            if (s == _last) return;
            _last = s;
            if (!Trace) return;
            _log?.Msg("[doorlisten] " + s);
        }
    }
}
