// ODG Operations - ThirdPerson
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace ThirdPerson
{
    internal enum NvgLook { Off, CameraGoggles }

    internal sealed class Nvg
    {
        internal NvgLook Look = NvgLook.CameraGoggles;

        private readonly List<Il2Cpp.NvgLensFollower> _eyeDisabled = new List<Il2Cpp.NvgLensFollower>(4);
        private GameObject _cameraGoggles;
        private bool _activatedByUs;
        private bool _on;
        private bool _engaged;
        private bool _warnedMissing;
        private float _diagUntil, _diagNext;

        internal bool Engaged { get { return _engaged; } }

        internal static bool GoggleEffectOn()
        {
            try { var gm = Il2Cpp.GameManager.instance; return gm != null && gm.nvgOn; } catch { return false; }
        }

        internal static bool GogglesDown(Il2Cpp.PlayerNetworking pn)
        {
            try { return pn != null && pn.nvgsOn; } catch { return false; }
        }

        internal void Tick(bool driving, float effectiveBlend, Transform eye, MelonLogger.Instance log)
        {
            bool on = Look != NvgLook.Off && driving && effectiveBlend > 0.001f && GoggleEffectOn();
            if (!on)
            {
                if (_engaged) Disengage(log);
                return;
            }
            _engaged = true;

            bool want = effectiveBlend > 0.05f;
            if (want != _on)
            {
                if (want) Engage(eye, log);
                else Release(log);
            }

            if (_on && Time.unscaledTime < _diagUntil && Time.unscaledTime >= _diagNext)
            {
                _diagNext = Time.unscaledTime + 1f;
                log.Msg("[nvg] " + State());
            }
        }

        private void Engage(Transform eye, MelonLogger.Instance log)
        {
            GameObject nvg = null;
            try { var mcs = Il2Cpp.MainCameraSingleton.instance; if (mcs != null) nvg = mcs.NVG; } catch { }
            if (nvg == null || !Game.Alive(nvg))
            {
                if (!_warnedMissing)
                {
                    _warnedMissing = true;
                    log.Warning("[nvg] MainCameraSingleton.NVG is missing in this scene; night vision behind the character stays the game's own.");
                }
                return;
            }

            _eyeDisabled.Clear();
            try
            {
                if (eye != null)
                {
                    var followers = eye.GetComponentsInChildren<Il2Cpp.NvgLensFollower>(false);
                    for (int i = 0; followers != null && i < followers.Length; i++)
                    {
                        var f = followers[i];
                        if (f == null || !f.enabled) continue;
                        try { if (f.TryCast<Il2Cpp.NvgScopeFollower>() != null) continue; } catch { }
                        f.enabled = false;
                        _eyeDisabled.Add(f);
                    }
                }
            }
            catch (Exception ex) { log.Warning("[nvg] eye followers: " + ex.Message); }

            _cameraGoggles = nvg;
            _activatedByUs = false;
            try
            {
                if (!nvg.activeSelf) { nvg.SetActive(true); _activatedByUs = true; }
            }
            catch (Exception ex) { log.Warning("[nvg] camera goggles: " + ex.Message); }
            _on = true;
            _diagUntil = Time.unscaledTime + 6f;
            _diagNext = Time.unscaledTime + 1f;
            log.Msg("[nvg] camera goggles on: " + Game.Path(nvg.transform) + (_activatedByUs ? " (activated)" : " (was already active)")
                  + ", " + _eyeDisabled.Count + " eye follower(s) paused");
            log.Msg("[nvg] " + Tree(nvg));
        }

        private void Release(MelonLogger.Instance log)
        {
            if (_on)
            {
                try { if (_activatedByUs && Game.Alive(_cameraGoggles)) _cameraGoggles.SetActive(false); } catch { }
                for (int i = 0; i < _eyeDisabled.Count; i++)
                {
                    try { if (Game.Alive(_eyeDisabled[i])) _eyeDisabled[i].enabled = true; } catch { }
                }
                if (log != null) log.Msg("[nvg] camera goggles off, " + _eyeDisabled.Count + " eye follower(s) resumed");
            }
            _eyeDisabled.Clear();
            _cameraGoggles = null;
            _activatedByUs = false;
            _on = false;
        }

        internal void Disengage(MelonLogger.Instance log)
        {
            Release(log);
            _engaged = false;
        }

        internal void Forget()
        {
            _eyeDisabled.Clear();
            _cameraGoggles = null;
            _activatedByUs = false;
            _on = false;
            _engaged = false;
        }

        internal string Describe()
        {
            return "look=" + Look + " engaged=" + _engaged + " cameraGoggles=" + _on + " " + State();
        }

        private static string State()
        {
            try
            {
                string driver = "none";
                var d = Il2Cpp.NvgLensFollower.s_driver;
                if (d != null && Game.Alive(d))
                {
                    try { driver = Game.Path(d.transform) + " r=" + d.LensRadius.ToString("0.#") + "mm n=" + d.NumLenses; } catch { driver = "?"; }
                }
                int active = -1, helmet = -1;
                try { active = Il2Cpp.NvgLensFollower.s_activeCount; } catch { }
                try { helmet = Il2Cpp.NvgLensFollower.s_helmetCount; } catch { }
                string nvgObj = "?", pass = "?";
                try
                {
                    var mcs = Il2Cpp.MainCameraSingleton.instance;
                    var o = mcs != null ? mcs.NVG : null;
                    nvgObj = o == null ? "null" : (o.activeInHierarchy ? "on" : "off");
                }
                catch { }
                try
                {
                    var gm = Il2Cpp.GameManager.instance;
                    var p = gm != null ? gm.NVGCustomPass : null;
                    pass = p == null ? "null" : (p.activeInHierarchy ? "on" : "off");
                }
                catch { }
                return "nvgOn=" + GoggleEffectOn() + " nvgObject=" + nvgObj + " customPass=" + pass
                     + " followers active=" + active + " helmet=" + helmet
                     + " lenses=" + Il2Cpp.NvgLensFollower.HasActiveLenses + "/" + Il2Cpp.NvgLensFollower.LensCount
                     + " radiusM=" + Il2Cpp.NvgLensFollower.LensRadiusMeters.ToString("0.###")
                     + " driver=" + driver;
            }
            catch (Exception e) { return "nvg unreadable: " + e.Message; }
        }

        private static string Tree(GameObject root)
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                sb.Append(root.name).Append(" [").Append(Game.Components(root)).Append("]");
                var t = root.transform;
                for (int i = 0; i < t.childCount && i < 8; i++)
                {
                    var c = t.GetChild(i);
                    sb.Append("; ").Append(c.name).Append(c.gameObject.activeSelf ? "" : "(inactive)")
                      .Append(" [").Append(Game.Components(c.gameObject)).Append("]");
                    for (int j = 0; j < c.childCount && j < 8; j++)
                    {
                        var g = c.GetChild(j);
                        sb.Append("; ").Append(c.name).Append("/").Append(g.name).Append(g.gameObject.activeSelf ? "" : "(inactive)")
                          .Append(" [").Append(Game.Components(g.gameObject)).Append("]");
                    }
                }
            }
            catch (Exception e) { sb.Append(" <tree unreadable: ").Append(e.Message).Append(">"); }
            return sb.ToString();
        }
    }
}
