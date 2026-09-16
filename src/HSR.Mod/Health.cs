// ODG Operations - HSR
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using MelonLoader;
using UnityEngine;

namespace HSR
{

    internal static class Health
    {
        static int _rifles, _withBody, _redress, _reports;
        static float _next;

        internal static void Rifle(bool hasBody) { _rifles++; if (hasBody) _withBody++; }
        internal static void Redress() { _redress++; }

        internal static void MaybeReport(MelonLogger.Instance log)
        {
            try
            {
                float t = Time.realtimeSinceStartup;
                if (_next == 0f) { _next = t + 30f; return; }
                if (t < _next) return;
                _next = t + 30f;

                if (_rifles == 0) { _redress = 0; return; }

                string mag = MagFollow.StateForHealth();
                string fire = AutoFire.Enabled ? "AUTO" : "SEMI";
                bool ok = _withBody == _rifles && _redress == 0;

                log.Msg(string.Format(
                    "HEALTH [{0}]  rifle-ticks={1}  with-body={2}  redress={3}  mag={4}  fire={5}",
                    ok ? "OK" : "CHECK", _rifles, _withBody, _redress, mag, fire));

                if (_withBody < _rifles)
                    log.Warning("HEALTH: a rifle was seen without our body on it. That is the "
                              + "invisible-M14 state; it should self-heal on the next tick.");

                _rifles = 0; _withBody = 0; _redress = 0; _reports++;
            }
            catch { }
        }
    }
}
