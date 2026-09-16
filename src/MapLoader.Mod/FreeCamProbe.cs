// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace MapLoader
{

    static class FreeCamProbe
    {
        static MelonLogger.Instance _log;
        static float _nextTick;
        static float _onSince = -1f;
        static Vector3 _lastPos;
        static int _stillTicks;

        public static void Install(HarmonyLib.Harmony h, MelonLogger.Instance log)
        {
            _log = log;
            int ok = 0;
            ok += Patch(h, "ToggleFreeCamera", nameof(Post_Toggle));
            ok += Patch(h, "ToggleOnCamera", nameof(Post_On));
            ok += Patch(h, "ToggleOffCamera", nameof(Post_Off));
            _log?.Msg("[freecam] watching " + ok + " free-camera method(s)");
        }

        static int Patch(HarmonyLib.Harmony h, string method, string postfix)
        {
            try
            {
                var m = AccessTools.Method(typeof(Il2Cpp.ScreenshotFreeCamera), method);
                if (m == null) { _log?.Warning("[freecam] no method " + method); return 0; }
                h.Patch(m, postfix: new HarmonyMethod(typeof(FreeCamProbe), postfix));
                return 1;
            }
            catch (Exception e)
            {
                _log?.Warning("[freecam] " + method + ": " + e.Message);
                return 0;
            }
        }

        static void Post_Toggle(Il2Cpp.ScreenshotFreeCamera __instance) { Report(__instance, "toggle pressed"); }
        static void Post_On(Il2Cpp.ScreenshotFreeCamera __instance) { Report(__instance, "ToggleOnCamera ran"); }
        static void Post_Off(Il2Cpp.ScreenshotFreeCamera __instance) { Report(__instance, "ToggleOffCamera ran"); }

        static string Path(Transform t)
        {
            try
            {
                string s = t.name; int hops = 0;
                while (t.parent != null && hops++ < 8) { t = t.parent; s = t.name + "/" + s; }
                return s;
            }
            catch { return "?"; }
        }

        static string Fmt(Vector3 v)
        {
            return "(" + v.x.ToString("0.0") + ", " + v.y.ToString("0.0") + ", " + v.z.ToString("0.0") + ")";
        }

        static string State(Il2Cpp.ScreenshotFreeCamera f, out Vector3 pos)
        {
            pos = Vector3.zero;
            var sb = new System.Text.StringBuilder();
            bool on = false; try { on = f.ToggledCam; } catch { }
            sb.Append("ToggledCam=").Append(on);
            Camera cam = null; try { cam = f.FreeCamera; } catch { }
            if (cam != null)
            {
                try
                {
                    pos = cam.transform.position;
                    sb.Append(", FreeCamera '").Append(Path(cam.transform)).Append("' active=")
                      .Append(cam.gameObject.activeInHierarchy).Append(" enabled=").Append(cam.enabled)
                      .Append(" depth=").Append(cam.depth.ToString("0")).Append(" at ").Append(Fmt(pos));
                }
                catch { sb.Append(", FreeCamera unreadable"); }
            }
            else sb.Append(", FreeCamera=null");
            try
            {
                var cm = f.FreeCameraCM;
                if (cm != null) sb.Append(", FreeCameraCM enabled=").Append(cm.enabled).Append(" priority=").Append(cm.Priority.ToString());
            }
            catch { }
            try
            {
                var pm = Il2Cpp.PlayerMaster.MyPlayerMaster;
                sb.Append(", player spawned-and-alive=").Append(pm != null ? pm.currentlySpawnedAndAlive.ToString() : "no PlayerMaster");
            }
            catch { sb.Append(", player state unreadable"); }
            try
            {
                var m = Camera.main;
                sb.Append(", Camera.main=").Append(m != null ? ("'" + Path(m.transform) + "'" + (m.enabled ? "" : " (disabled)")) : "none");
            }
            catch { }
            try
            {
                var rig = Il2Cpp.MainCameraSingleton.instance;
                if (rig != null)
                {
                    var gc = rig.GameCamera;
                    sb.Append(", rig GameCamera ").Append(gc != null ? (gc.enabled ? "on" : "off") : "null");
                }
                else sb.Append(", no MainCameraSingleton");
            }
            catch { }
            try
            {
                var tp = UnityEngine.Object.FindObjectOfType<Il2Cpp.TpToggle>();
                if (tp != null)
                {
                    sb.Append(", game third-person=").Append(tp.inThirdPerson);
                    var tc = tp.TPCamera;
                    if (tc != null) sb.Append(" (TPCamera ").Append(tc.enabled ? "on" : "off").Append(")");
                }
            }
            catch { }
            try { sb.Append(", input disabled=").Append(Il2Cpp.InputLayer.InputDisabled); } catch { }
            return sb.ToString();
        }

        static void Report(Il2Cpp.ScreenshotFreeCamera f, string why)
        {
            try
            {
                if (f == null) { _log?.Msg("[freecam] " + why + ": no ScreenshotFreeCamera"); return; }
                Vector3 pos;
                string st = State(f, out pos);
                _log?.Msg("[freecam] " + why + ": " + st
                        + ". (ToggleOn/Off only run while the player is spawned and alive; a camera that is "
                        + "on but never moves has something else writing its pose.)");
                bool on = false; try { on = f.ToggledCam; } catch { }
                _onSince = on ? Time.unscaledTime : -1f;
                _lastPos = pos; _stillTicks = 0;
                _nextTick = Time.unscaledTime + 1f;
            }
            catch (Exception e) { _log?.Msg("[freecam] " + why + ": " + e.Message); }
        }

        public static void Tick()
        {
            try
            {
                float now = Time.unscaledTime;
                if (now < _nextTick) return;
                _nextTick = now + 1f;
                Il2Cpp.ScreenshotFreeCamera f = null;
                try { var rig = Il2Cpp.MainCameraSingleton.instance; if (rig != null) f = rig.freeCamera; } catch { }
                if (f == null) return;
                bool on = false; try { on = f.ToggledCam; } catch { }
                if (!on) { if (_onSince >= 0f) { _log?.Msg("[freecam] off after " + (now - _onSince).ToString("0") + " s"); } _onSince = -1f; _stillTicks = 0; return; }
                if (_onSince < 0f) { _onSince = now; Report(f, "found on"); return; }
                Vector3 pos;
                string st = State(f, out pos);
                float moved = (pos - _lastPos).magnitude;
                _lastPos = pos;
                if (moved < 0.01f) _stillTicks++; else _stillTicks = 0;
                int secs = (int)(now - _onSince);

                if (secs % 5 != 0 && _stillTicks < 2) return;
                _log?.Msg("[freecam] on for " + secs + " s: moved " + moved.ToString("0.00") + " m in the last second"
                        + (_stillTicks >= 2 ? " (STILL for " + _stillTicks + " s)" : "") + "; " + st);
            }
            catch { }
        }
    }
}
