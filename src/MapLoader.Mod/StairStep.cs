// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using MelonLoader;
using UnityEngine;

namespace MapLoader
{

    public static class StairStep
    {
        static MelonLogger.Instance _log;

        static CharacterController _cc;
        static float _origStep;
        static float _origSlope;

        static Il2CppCMF.Mover _mover;
        static float _origRatio;
        static float _height;

        static bool _held;
        static bool _reported;
        static bool _moaned;
        static float _next;

        public static void Init(MelonLogger.Instance log) { _log = log; }

        public static void Forget()
        {
            _cc = null;
            _mover = null;
            _held = false;
            _reported = false;
            _moaned = false;
        }

        static CharacterController FindController()
        {
            try
            {
                var mv = UnityEngine.Object.FindObjectsOfType<Il2Cpp.PlayerMovement>();
                for (int i = 0; i < (mv != null ? mv.Length : 0); i++)
                {
                    if (mv[i] == null) continue;
                    CharacterController c = null;
                    try { c = mv[i].controller; } catch { }
                    if (c == null)
                    {
                        try { c = mv[i].GetComponent<CharacterController>(); } catch { }
                    }
                    if (c != null) return c;
                }
            }
            catch { }
            try
            {
                var p = DoorWiring.LocalPlayer();
                if (p != null)
                {
                    var c = p.GetComponentInChildren<CharacterController>(true);
                    if (c != null) return c;
                }
            }
            catch { }
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<CharacterController>();
                if (all != null && all.Length == 1) return all[0];
            }
            catch { }
            return null;
        }

        static Il2CppCMF.Mover FindMover()
        {
            try
            {
                var p = DoorWiring.LocalPlayer();
                if (p != null)
                {
                    var m = p.GetComponentInChildren<Il2CppCMF.Mover>(true);
                    if (m != null) return m;
                }
            }
            catch { }
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<Il2CppCMF.Mover>();
                if (all != null && all.Length == 1) return all[0];
            }
            catch { }
            return null;
        }

        public static void Restore()
        {
            if (!_held) { Forget(); return; }
            try
            {
                if (_cc != null)
                {
                    _cc.stepOffset = _origStep;
                    _cc.slopeLimit = _origSlope;
                    _log?.Msg("[stairs] restored the player's own stepOffset "
                            + _origStep.ToString("F2") + " m / slopeLimit "
                            + _origSlope.ToString("F0") + " deg");
                }
            }
            catch (Exception e) { _log?.Warning("[stairs] restore: " + e.Message); }
            try
            {
                if (_mover != null)
                {
                    _mover.SetStepHeightRatio(_origRatio);
                    _log?.Msg("[stairs] restored the mover's own stepHeightRatio "
                            + _origRatio.ToString("F3"));
                }
            }
            catch (Exception e) { _log?.Warning("[stairs] restore mover: " + e.Message); }
            Forget();
        }

        public static void Tick()
        {
            if (Time.realtimeSinceStartup < _next) return;
            _next = Time.realtimeSinceStartup + 0.5f;

            float want = MapIdentity.MinStepOffset;
            if (want <= 0f) return;
            if (!MapRuntime.Done) return;

            try
            {
                if (_cc == null && _mover == null)
                {
                    _cc = FindController();
                    if (_cc == null) _mover = FindMover();
                    if (_cc == null && _mover == null)
                    {
                        if (!_moaned)
                        {
                            _moaned = true;
                            _log?.Warning("[stairs] neither a CharacterController nor a "
                                        + "CMF Mover found for the local player yet. Retrying.");
                        }
                        return;
                    }
                    _moaned = false;
                    if (_cc != null)
                    {
                        _origStep = _cc.stepOffset;
                        _origSlope = _cc.slopeLimit;
                    }
                    else
                    {
                        _origRatio = _mover.stepHeightRatio;
                        _height = _mover.colliderHeight;
                        if (_height < 0.5f || _height > 4f) _height = 1.8f;
                    }
                }

                if (_cc != null) TickController(want);
                else TickMover(want);
            }
            catch (Exception e)
            {
                _log?.Warning("[stairs] " + e.Message);
                _cc = null;
                _mover = null;
            }
        }

        static void TickController(float want)
        {
            if (!_reported)
            {
                _reported = true;
                _log?.Msg("[stairs] the player's CharacterController reads stepOffset "
                        + _origStep.ToString("F2") + " m, slopeLimit "
                        + _origSlope.ToString("F0") + " deg, radius "
                        + _cc.radius.ToString("F2") + " m, height "
                        + _cc.height.ToString("F2") + " m. This map's stairs need at "
                        + "least " + want.ToString("F2") + " m.");
            }

            if (_origStep >= want && _origSlope >= MinSlope)
            {
                if (!_held)
                {
                    _held = true;
                    _log?.Msg("[stairs] the game's own values already clear this map's "
                            + "stairs, so nothing is changed. If they still cannot be "
                            + "climbed the cause is not the step offset.");
                }
                return;
            }

            bool drift = Mathf.Abs(_cc.stepOffset - want) > 0.001f
                      || _cc.slopeLimit < MinSlope - 0.5f;
            if (!drift) return;

            _cc.stepOffset = Mathf.Max(_origStep, want);
            if (_cc.slopeLimit < MinSlope) _cc.slopeLimit = MinSlope;
            if (!_held)
            {
                _held = true;
                _log?.Msg("[stairs] raised the player's stepOffset "
                        + _origStep.ToString("F2") + " -> " + _cc.stepOffset.ToString("F2")
                        + " m and slopeLimit " + _origSlope.ToString("F0") + " -> "
                        + _cc.slopeLimit.ToString("F0") + " deg while this map stands. "
                        + "Industrial stairs are steeper than anything the game ships. "
                        + "Restored when the map goes.");
            }
        }

        static void TickMover(float want)
        {

            float ratio = Mathf.Clamp(want / _height, 0f, 0.5f);

            if (!_reported)
            {
                _reported = true;
                _log?.Msg("[stairs] the player's CMF Mover reads stepHeightRatio "
                        + _origRatio.ToString("F3") + " with colliderHeight "
                        + _height.ToString("F2") + " m (~"
                        + (_origRatio * _height).ToString("F2") + " m of step). This "
                        + "map asks for " + want.ToString("F2") + " m -> ratio "
                        + ratio.ToString("F3") + ".");
            }

            if (_origRatio >= ratio)
            {
                if (!_held)
                {
                    _held = true;
                    _log?.Msg("[stairs] the mover's own ratio already clears this map's "
                            + "stairs, so nothing is changed. If they still cannot be "
                            + "climbed the cause is not the step height.");
                }
                return;
            }

            float cur = _mover.stepHeightRatio;
            if (Mathf.Abs(cur - ratio) <= 0.002f) return;

            _mover.SetStepHeightRatio(ratio);
            if (!_held)
            {
                _held = true;
                _log?.Msg("[stairs] raised the mover's stepHeightRatio "
                        + _origRatio.ToString("F3") + " -> " + ratio.ToString("F3")
                        + " (~" + (ratio * _height).ToString("F2") + " m of step) "
                        + "while this map stands. Restored when the map goes.");
            }
        }

        const float MinSlope = 50f;
    }
}
