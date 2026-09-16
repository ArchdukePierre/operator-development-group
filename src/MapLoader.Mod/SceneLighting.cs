// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MapLoader
{

    public static class SceneLighting
    {
        public const string Root  = "LIGHTING";
        public const string Day   = "DAY";
        public const string Night = "NIGHT";

        static MelonLogger.Instance _log;
        static bool _isNight;

        public static bool IsNight => _isNight;

        public static void Init(MelonLogger.Instance log) { _log = log; }

        public static bool WantDay;

        public static bool Apply(bool night)
        {
            var root = FindRoot();
            if (root == null)
            {
                _log?.Warning("[light] no '" + Root + "' root in the active scene. "
                            + "Either this is not a bundle map, or it was built before "
                            + "lighting was authored in.");
                return false;
            }

            var day = root.Find(Day);
            var nite = root.Find(Night);
            if (day == null || nite == null)
            {
                _log?.Warning("[light] '" + Root + "' is missing DAY or NIGHT");
                return false;
            }

            day.gameObject.SetActive(!night);
            nite.gameObject.SetActive(night);
            _isNight = night;

            try
            {

                if (MapRuntime.UseHostLighting && MapRuntime.DayIsHostLit)
                    ShellSwap.SetHostLights(!night, _log);
            }
            catch (Exception e) { _log?.Warning("[light] host lights: " + e.Message); }

            _log?.Msg("[light] " + (night ? "NIGHT" : "DAY")
                    + "  (day rig " + (night ? "off" : "on")
                    + ", night rig " + (night ? "on" : "off") + ")");
            return true;
        }

        public static void Toggle() { Apply(!_isNight); }

        static Transform FindRoot()
        {
            try
            {

                var direct = MapRuntime.LightingRoot;
                if (direct != null) return direct;

                var scene = SceneManager.GetActiveScene();
                if (!scene.IsValid()) return null;
                var roots = scene.GetRootGameObjects();
                if (roots == null) return null;
                for (int i = 0; i < roots.Count; i++)
                {
                    var r = roots[i];
                    if (r == null) continue;
                    if (r.name == Root) return r.transform;
                    var child = r.transform.Find(Root);
                    if (child != null) return child;
                }
            }
            catch (Exception e) { _log?.Warning("[light] FindRoot: " + e.Message); }
            return null;
        }

        public static void DumpState()
        {
            var root = FindRoot();
            if (root == null) { _log?.Msg("[light] no LIGHTING root"); return; }
            for (int i = 0; i < root.childCount; i++)
            {
                var g = root.GetChild(i);
                _log?.Msg("[light] group '" + g.name + "' active=" + g.gameObject.activeSelf
                        + " lights=" + g.childCount);
                for (int c = 0; c < g.childCount; c++)
                {
                    var l = g.GetChild(c).GetComponent<Light>();
                    if (l == null) continue;
                    _log?.Msg("           " + g.GetChild(c).name + "  type=" + l.type
                            + " shadows=" + l.shadows + " K=" + l.colorTemperature);
                }
            }
        }
    }
}
