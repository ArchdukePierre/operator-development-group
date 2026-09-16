// ODG Operations - HSR
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using MelonLoader;
using UnityEngine;

namespace HSR
{

    internal static class MagRegistry
    {
        static bool _done;

        internal static void ResetForScene() { _done = false; }

        internal static void Report(Il2Cpp.WeaponV3 w, MelonLogger.Instance log)
        {
            if (_done || w == null || log == null) return;
            _done = true;
            try
            {
                log.Msg("MAGREG: what a registered M200 magazine would have to match --");

                try
                {
                    var wm = w.GetComponentInChildren<Il2Cpp.WeaponMods>(true);
                    if (wm != null)
                    {
                        string fam = "?", mc = "?";
                        try { fam = wm.WeaponFamily ?? "null"; } catch { }
                        try { mc = wm.magazineCompatibility ?? "null"; } catch { }
                        log.Msg("   weapon '{0}': WeaponFamily='{1}'  magazineCompatibility='{2}'",
                                w.gameObject.name, fam, mc);
                    }
                    else log.Msg("   no WeaponMods on the weapon");
                }
                catch (Exception e) { log.Msg("   weapon read failed: " + e.Message); }

                try
                {
                    var mags = w.transform.GetComponentsInChildren<Il2Cpp.Magazine>(true);
                    int n = mags != null ? mags.Length : 0;
                    log.Msg("   {0} Magazine component(s) on the weapon:", n);
                    for (int i = 0; i < n && i < 6; i++)
                    {
                        var m = mags[i];
                        if (m == null) continue;
                        string systems = "none";
                        try
                        {
                            var cur = m.compatibleWeaponSystems;
                            if (cur != null && cur.Length > 0)
                            {
                                systems = "";
                                for (int k = 0; k < cur.Length; k++)
                                    systems += (systems.Length > 0 ? "," : "") + cur[k];
                            }
                        }
                        catch { systems = "unreadable"; }
                        log.Msg("      '{0}' compatibleWeaponSystems=[{1}]", m.gameObject.name, systems);
                    }
                }
                catch (Exception e) { log.Msg("   magazine read failed: " + e.Message); }

                try
                {
                    var lm = Il2Cpp.LoadoutManager.singleton;
                    var infos = lm == null ? null : lm.WeaponModInfo;
                    var cache = lm == null ? null : lm.WeaponModCompatibilityCache;
                    var stripped = lm == null ? null : lm.AllStrippedWeaponMods;
                    log.Msg("   registry: WeaponModInfo={0}  CompatibilityCache={1}  AllStrippedWeaponMods={2}",
                        infos == null ? -1 : infos.Length,
                        cache == null ? -1 : cache.Length,
                        stripped == null ? -1 : stripped.Length);

                    if (infos != null)
                    {
                        for (int i = 0; i < infos.Length; i++)
                        {
                            var inf = infos[i];
                            if (inf == null) continue;
                            string nm = null;
                            try { nm = inf.Name; } catch { continue; }
                            if (string.IsNullOrEmpty(nm)) continue;
                            if (nm.IndexOf("R700_Mag", StringComparison.OrdinalIgnoreCase) < 0) continue;

                            int compat = -1; string disp = "?", cs = "?";
                            try { compat = inf.Compatibility; } catch { }
                            try { disp = inf.DisplayName ?? "null"; } catch { }
                            try { if (cache != null && compat >= 0 && compat < cache.Length) cs = cache[compat]; }
                            catch { }
                            log.Msg("      [{0}] Name='{1}' Display='{2}' Compatibility={3} ('{4}')",
                                    i, nm, disp, compat, cs);
                        }
                    }
                }
                catch (Exception e) { log.Msg("   registry read failed: " + e.Message); }
            }
            catch { }
        }
    }
}
