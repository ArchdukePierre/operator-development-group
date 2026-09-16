// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using MelonLoader;
using UnityEngine;

namespace MapLoader
{

    public static class MapUnstick
    {
        static MelonLogger.Instance _log;
        static bool _enabled = true;

        static float _next;
        static float _doneAt = -1f;
        static int _streak;
        static Vector3 _lastPos;
        static bool _havePos;

        static float _coolUntil;
        static int _fails;
        static bool _stoodDown;
        static Vector3 _wedge;
        static float _judgeAt = -1f;

        const int NeedStreak = 6;
        const float PushMin = 0.5f;
        const float MovedMax = 0.05f;
        const float FrozenMax = 0.08f;
        const int MaxFails = 4;

        public static void Init(MelonLogger.Instance log)
        {
            _log = log;
            try
            {
                var cfg = MelonPreferences.GetCategory("MapLoader")
                       ?? MelonPreferences.CreateCategory("MapLoader");
                var e = cfg.GetEntry<bool>("UnstickEnabled")
                     ?? cfg.CreateEntry("UnstickEnabled", true,
                          description: "Free a player who is wedged against map geometry: "
                                     + "pushing on the stick, body not moving, for 3 "
                                     + "seconds straight. Moves them a metre or three to "
                                     + "the nearest open floor. 'odg unstick' does the "
                                     + "same by hand and ignores this switch.");
                _enabled = e == null || e.Value;
            }
            catch (Exception ex) { _log?.Warning("[unstick] prefs: " + ex.Message); }
        }

        public static void Forget()
        {
            _streak = 0;
            _havePos = false;
            _fails = 0;
            _stoodDown = false;
            _doneAt = -1f;
            _judgeAt = -1f;
        }

        static Il2CppCMF.AdvancedWalkerController FindWalker()
        {
            try
            {
                var p = DoorWiring.LocalPlayer();
                if (p != null)
                {
                    var w = p.GetComponentInChildren<Il2CppCMF.AdvancedWalkerController>(true);
                    if (w != null) return w;
                }
            }
            catch { }
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<Il2CppCMF.AdvancedWalkerController>();
                if (all != null && all.Length == 1) return all[0];
            }
            catch { }
            return null;
        }

        public static void Tick()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _next) return;
            _next = now + 0.5f;

            if (!MapRuntime.Done) { _doneAt = -1f; return; }
            if (_doneAt < 0f) _doneAt = now;
            if (now - _doneAt < 20f) return;

            if (_judgeAt > 0f && now >= _judgeAt)
            {
                _judgeAt = -1f;
                Vector3 p2;
                bool moved = PlayerBody.TryPosition(out p2)
                          && (p2 - _wedge).magnitude > 0.5f;
                if (moved) { _fails = 0; }
                else
                {
                    _fails++;
                    _log?.Warning("[unstick] the move did not take (" + _fails + "/"
                                + MaxFails + ").");
                    if (_fails >= MaxFails && !_stoodDown)
                    {
                        _stoodDown = true;
                        _log?.Warning("[unstick] standing down after " + MaxFails
                                    + " failed attempts; 'odg unstick' still works "
                                    + "by hand.");
                    }
                }
            }

            if (!_enabled || _stoodDown) return;
            if (now < _coolUntil) return;
            if (PlayerBody.ConfirmPending) return;

            var walker = FindWalker();
            if (walker == null) return;

            Vector3 pos;
            if (!PlayerBody.TryPosition(out pos)) return;

            float wish = 0f, got = 0f;
            try
            {
                wish = walker.GetMovementVelocity().magnitude;
                got = walker.measuredXY_Velocity;
            }
            catch { return; }

            bool frozen = _havePos && (pos - _lastPos).magnitude < FrozenMax;
            _lastPos = pos;
            _havePos = true;

            if (wish > PushMin && got < MovedMax && frozen) _streak++;
            else _streak = 0;

            if (_streak < NeedStreak) return;
            _streak = 0;

            _log?.Warning("[unstick] pushing for " + (NeedStreak * 0.5f).ToString("F1")
                        + " s with the body held at (" + pos.x.ToString("F1") + ", "
                        + pos.y.ToString("F1") + ", " + pos.z.ToString("F1")
                        + "); looking for open floor.");
            Attempt(pos);
        }

        static string Attempt(Vector3 pos)
        {
            _coolUntil = Time.realtimeSinceStartup + 6f;
            float[] radii = { 1.0f, 1.6f, 2.4f, 3.2f };
            for (int ri = 0; ri < radii.Length; ri++)
            {
                for (int hi = 0; hi < 8; hi++)
                {
                    float a = hi * Mathf.PI / 4f;
                    var probe = pos + new Vector3(Mathf.Cos(a) * radii[ri], 0.5f,
                                                  Mathf.Sin(a) * radii[ri]);
                    bool found;
                    var g = MapRuntime.GroundedSpawn(probe, out found);
                    if (!found) continue;
                    if (Mathf.Abs((g.y - 1.1f) - pos.y) > 2.0f) continue;
                    _wedge = pos;
                    _judgeAt = Time.realtimeSinceStartup + 3f;
                    PlayerBody.MoveTo(g, "unstick");
                    _log?.Msg("[unstick] moving " + radii[ri].ToString("F1")
                            + " m to (" + g.x.ToString("F1") + ", " + g.y.ToString("F1")
                            + ", " + g.z.ToString("F1") + ").");
                    return "moved you " + radii[ri].ToString("F1")
                         + " m to the nearest open floor.";
                }
            }
            _log?.Warning("[unstick] no open floor of ours within 3.2 m; not moving.");
            return "no open floor found within 3.2 m; try 'odg spawn <n>' instead.";
        }

        public static string Rescue()
        {
            if (!MapRuntime.Done) return "no map of ours is standing.";
            if (Time.realtimeSinceStartup < _coolUntil)
                return "just moved you; give it a few seconds.";
            Vector3 pos;
            if (!PlayerBody.TryPosition(out pos)) return "cannot find your body.";
            return Attempt(pos);
        }
    }
}
