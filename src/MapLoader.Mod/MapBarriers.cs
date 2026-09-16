// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Text;
using MelonLoader;
using UnityEngine;

namespace MapLoader
{

    public static class MapBarriers
    {
        static MelonLogger.Instance _log;
        static readonly List<Collider> _disarmed = new List<Collider>();

        public static void Init(MelonLogger.Instance log) { _log = log; }

        public static string Sweep(string why)
        {
            var sb = new StringBuilder();
            int pbLayer = -1;
            try { pbLayer = LayerMask.NameToLayer("PlayerBlock"); } catch { }
            Transform ourRoot = null;
            try { ourRoot = MapRuntime.Root != null ? MapRuntime.Root.transform : null; } catch { }
            Bounds map;
            try { map = MapRuntime.Bounds(); } catch { map = new Bounds(Vector3.zero, Vector3.one * 400f); }

            int disarmed = 0, census = 0, pbSeen = 0, pbOn = 0;
            var lines = new List<string>();
            try
            {
                var all = Resources.FindObjectsOfTypeAll<Collider>();
                for (int i = 0; i < (all != null ? all.Length : 0); i++)
                {
                    var c = all[i];
                    try
                    {
                        if (c == null || c.gameObject == null) continue;
                        var go = c.gameObject;
                        var sc = go.scene;
                        if (!sc.IsValid()) continue;
                        if (string.Equals(sc.name, "DontDestroyOnLoad", StringComparison.Ordinal)) continue;
                        if (ourRoot != null && c.transform.IsChildOf(ourRoot)) continue;
                        bool trig = false; try { trig = c.isTrigger; } catch { }
                        if (trig) continue;

                        if (pbLayer >= 0 && go.layer == pbLayer)
                        {
                            pbSeen++;
                            bool en = false; try { en = c.enabled && go.activeInHierarchy; } catch { }
                            if (en)
                            {
                                pbOn++;
                                c.enabled = false;
                                _disarmed.Add(c);
                                disarmed++;
                                Bounds b = c.bounds;
                                lines.Add("   PlayerBlock '" + go.name + "' in " + sc.name
                                        + " was ON: x " + b.min.x.ToString("0") + ".." + b.max.x.ToString("0")
                                        + " y " + b.min.y.ToString("0") + ".." + b.max.y.ToString("0")
                                        + " z " + b.min.z.ToString("0") + ".." + b.max.z.ToString("0")
                                        + " -> disarmed");
                            }
                            continue;
                        }

                        bool on = false; try { on = c.enabled && go.activeInHierarchy; } catch { }
                        if (!on) continue;
                        try { if (c.TryCast<TerrainCollider>() != null) continue; } catch { }
                        Bounds cb = c.bounds;
                        if (cb.max.x < map.min.x || cb.min.x > map.max.x
                         || cb.max.z < map.min.z || cb.min.z > map.max.z) continue;
                        if (HasVisibleRenderer(go)) continue;
                        census++;
                        if (lines.Count < 40)
                            lines.Add("   '" + go.name + "' in " + sc.name + " layer " + go.layer
                                    + " (" + LayerMask.LayerToName(go.layer) + ") "
                                    + c.GetIl2CppType().Name
                                    + " x " + cb.min.x.ToString("0") + ".." + cb.max.x.ToString("0")
                                    + " y " + cb.min.y.ToString("0.0") + ".." + cb.max.y.ToString("0.0")
                                    + " z " + cb.min.z.ToString("0") + ".." + cb.max.z.ToString("0")
                                    + "  parent '" + (go.transform.parent != null ? go.transform.parent.name : "-") + "'");
                    }
                    catch { }
                }
            }
            catch (Exception e) { _log?.Warning("[barriers] sweep: " + e.Message); }

            sb.Append("[barriers] (" + why + ") PlayerBlock colliders: " + pbSeen + " seen, " + pbOn
                    + " were live and are now off. Solid colliders inside the map with nothing drawing them: "
                    + census + (census == 0 ? " -- nothing invisible is solid here." : ""));
            _log?.Msg(sb.ToString());
            for (int i = 0; i < lines.Count; i++) _log?.Msg("[barriers]" + lines[i]);
            if (census > lines.Count) _log?.Msg("[barriers]   ... and " + (census - lines.Count) + " more");
            return sb.ToString() + (lines.Count > 0 ? "\n" + string.Join("\n", lines.ToArray()) : "");
        }

        static bool HasVisibleRenderer(GameObject go)
        {
            try
            {
                var own = go.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < own.Length; i++)
                    if (own[i] != null && own[i].enabled && own[i].gameObject.activeInHierarchy) return true;
                var t = go.transform.parent;
                int hops = 0;
                while (t != null && hops < 6)
                {
                    var rs = t.GetComponentsInChildren<Renderer>(true);
                    for (int i = 0; i < rs.Length; i++)
                        if (rs[i] != null && rs[i].enabled && rs[i].gameObject.activeInHierarchy) return true;
                    t = t.parent; hops++;
                }
            }
            catch { return true; }
            return false;
        }

        public static void Restore()
        {
            int n = 0;
            for (int i = 0; i < _disarmed.Count; i++)
            {
                try { if (_disarmed[i] != null) { _disarmed[i].enabled = true; n++; } } catch { }
            }
            _disarmed.Clear();
            if (n > 0) _log?.Msg("[barriers] " + n + " PlayerBlock collider(s) re-armed for the host level");
        }
    }
}
