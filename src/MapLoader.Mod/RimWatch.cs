// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using MelonLoader;
using UnityEngine;

namespace MapLoader
{

    static class RimWatch
    {
        static MelonLogger.Instance _log;
        static float _next, _lastSaid;
        static Collider[] _rim;
        static Transform _rimRoot;
        static int _hits;

        public static void Init(MelonLogger.Instance log) { _log = log; }
        public static void ResetForNewLevel() { _rim = null; _rimRoot = null; _hits = 0; _lastSaid = 0f; }

        static Collider[] Rim()
        {
            var root = MapRuntime.Root;
            if (root == null) { _rim = null; _rimRoot = null; return null; }
            if (_rim != null && _rimRoot == root.transform) return _rim;
            var list = new System.Collections.Generic.List<Collider>();
            try
            {
                var cols = root.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < (cols != null ? cols.Length : 0); i++)
                {
                    var c = cols[i];
                    if (c == null) continue;
                    string n = null; try { n = c.gameObject.name; } catch { }
                    if (n != null && n.StartsWith("barrier", StringComparison.OrdinalIgnoreCase)) list.Add(c);
                }
            }
            catch { }
            _rim = list.ToArray();
            _rimRoot = root.transform;
            _log?.Msg("[barriers] rim watch: " + _rim.Length + " barrier collider(s) under the map root"
                    + (_rim.Length == 0 ? " (this map has no rim of ours)" : ""));
            return _rim;
        }

        public static void Tick()
        {
            try
            {
                if (!MapRuntime.Done) return;
                float now = Time.realtimeSinceStartup;
                if (now < _next) return;
                _next = now + 2f;
                var rim = Rim();
                if (rim == null || rim.Length == 0) return;
                Vector3 p;
                if (!PlayerBody.TryPosition(out p)) return;
                var near = Physics.OverlapSphere(p + Vector3.up * 1.0f, 1.3f, ~0, QueryTriggerInteraction.Ignore);
                bool touching = false;
                for (int i = 0; i < (near != null ? near.Length : 0) && !touching; i++)
                    for (int k = 0; k < rim.Length; k++)
                        if (near[i] == rim[k]) { touching = true; break; }
                if (!touching) return;
                _hits++;
                if (now - _lastSaid < 60f) return;
                _lastSaid = now;
                var b = MapRuntime.Bounds();
                var c = b.center;
                float dx = p.x - c.x, dz = p.z - c.z;
                _log?.Msg("[barriers] you are against the map's RIM BARRIER at (" + p.x.ToString("0.0") + ", "
                        + p.y.ToString("0.0") + ", " + p.z.ToString("0.0") + "), " + Math.Sqrt(dx * dx + dz * dz).ToString("0")
                        + " m from the map centre. This is the invisible edge of the playable area, ours by design; "
                        + _hits + " contact(s) so far this mission. If this is where the 'invisible barriers' are, "
                        + "the rim is too tight for how far the fight goes.");
            }
            catch { }
        }
    }
}
