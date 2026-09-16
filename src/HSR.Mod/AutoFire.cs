// ODG Operations - HSR
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using MelonLoader;
using UnityEngine;

namespace HSR
{

    internal static class AutoFire
    {
        static MelonPreferences_Entry<bool> _auto;

        internal static void Init(MelonPreferences_Category cat)
        {
            _auto = cat.CreateEntry("FullAuto", false);
        }

        internal static bool Enabled
        {
            get { try { return _auto != null && _auto.Value; } catch { return false; } }
        }

        internal static void Toggle(HSRMod mod, MelonLogger.Instance log)
        {
            try
            {
                if (_auto == null) return;
                _auto.Value = !_auto.Value;
                MelonPreferences.Save();
                int n = mod.ApplyFireModeToOurs();
                log.Msg("FIRE MODE: " + (_auto.Value ? "FULL AUTO" : "SEMI")
                      + "  (" + n + " rifle(s) updated"
                      + (n == 0 ? ", none in hand — applies when you pick the M14 up" : "")
                      + ")");
            }
            catch (Exception e) { log.Warning("AutoFire.Toggle: " + e.Message); }
        }

        internal static void Apply(Il2Cpp.WeaponV3 w)
        {
            try
            {
                if (w == null || w.gameObject == null) return;
                var stats = w.GetComponentsInChildren<Il2Cpp.GunStats>(true);
                if (stats == null) return;
                bool auto = Enabled;
                for (int i = 0; i < stats.Length; i++)
                {
                    var gs = stats[i]; if (gs == null) continue;
                    try { gs.fullAuto = auto; } catch { }
                    try { gs.semiAutomatic = !auto; } catch { }
                    try { gs.burst = false; } catch { }
                    SetEnum(gs, auto);
                }
            }
            catch { }
        }

        static void SetEnum(Il2Cpp.GunStats gs, bool auto)
        {
            try
            {
                var p = gs.GetType().GetProperty("fireMode");
                if (p == null || !p.CanWrite) return;
                var t = p.PropertyType;
                if (!t.IsEnum) return;
                var name = auto ? "Auto" : "Semi";
                if (!Enum.IsDefined(t, name)) return;
                p.SetValue(gs, Enum.Parse(t, name));
            }
            catch { }
        }
    }
}
