// ODG Operations - HSR
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using MelonLoader;
using UnityEngine;

namespace HSR
{

    internal static class PouchNames
    {
        static int _restoreReports, _repairReports, _foreignReports;
        static bool _repairedThisScene;

        internal static bool RepairedThisScene { get { return _repairedThisScene; } }
        internal static void ResetForScene() { _repairedThisScene = false; }

        static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2Cpp.WeaponModInfo> Registry()
        {
            try
            {
                var lm = Il2Cpp.LoadoutManager.singleton;
                return lm == null ? null : lm.WeaponModInfo;
            }
            catch { return null; }
        }

        internal static bool IsRegistryName(string n)
        {
            if (string.IsNullOrEmpty(n)) return false;
            var infos = Registry();
            if (infos == null) return false;
            for (int i = 0; i < infos.Length; i++)
            {
                var w = infos[i];
                if (w == null) continue;
                string nm;
                try { nm = w.Name; } catch { continue; }
                if (string.Equals(nm, n, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        internal static string RealNameFor(string label)
        {
            if (string.IsNullOrEmpty(label)) return null;
            var infos = Registry();
            if (infos == null) return null;
            for (int i = 0; i < infos.Length; i++)
            {
                var w = infos[i];
                if (w == null) continue;
                string nm;
                try { nm = w.Name; } catch { continue; }
                if (string.Equals(nm, label, StringComparison.OrdinalIgnoreCase)) return nm;
            }
            for (int i = 0; i < infos.Length; i++)
            {
                var w = infos[i];
                if (w == null) continue;
                string nm;
                try { nm = w.Name; } catch { continue; }
                if (string.IsNullOrEmpty(nm)) continue;
                string re = MagLabel.Rewrite(nm);
                if (!ReferenceEquals(re, nm) && string.Equals(re, label, StringComparison.OrdinalIgnoreCase)) return nm;
            }
            return null;
        }

        internal static void BeforeClone(Il2Cpp.MagManagerMagUI card, MelonLogger.Instance log)
        {
            if (card == null) return;
            string savedMag = null, savedAmmo = null;
            try
            {
                int idx = card.MagSaveIndex;
                var pm = Il2Cpp.PlayerMaster.MyPlayerMaster;
                var list = pm == null ? null : pm.Magazines;
                var pres = list == null ? null : list.presets;
                if (pres != null && idx >= 0 && idx < pres.Count)
                {
                    var e = pres[idx];
                    if (e != null) { savedMag = e.magazineModName; savedAmmo = e.ammoModName; }
                }
            }
            catch { }

            Il2CppTMPro.TextMeshProUGUI magText = null, ammoText = null;
            try { magText = card.MagNameText; } catch { }
            try { ammoText = card.AmmoNameText; } catch { }
            Restore(magText, savedMag, "magazine", log);
            Restore(ammoText, savedAmmo, "round", log);
        }

        static void Restore(Il2CppTMPro.TextMeshProUGUI t, string saved, string what, MelonLogger.Instance log)
        {
            if (t == null) return;
            string cur;
            try { cur = t.text; } catch { return; }
            string real = IsRegistryName(saved) ? saved : RealNameFor(cur);
            if (real == null || string.Equals(real, cur, StringComparison.Ordinal)) return;
            try { t.text = real; } catch { return; }
            if (log != null && _restoreReports++ < 6)
                log.Msg("pouch: duplicate reads the {0} label, so it was set back to '{1}' (was '{2}') for the save.",
                        what, real, cur);
        }

        internal static void RepairPouch(string when, MelonLogger.Instance log)
        {
            try
            {
                var pm = Il2Cpp.PlayerMaster.MyPlayerMaster;
                if (pm == null) return;
                _repairedThisScene = true;
                var list = pm.Magazines;
                if (list == null) return;
                var pres = list.presets;
                if (pres == null) return;

                int fixedUp = 0;
                for (int i = 0; i < pres.Count; i++)
                {
                    var e = pres[i];
                    if (e == null) continue;
                    string mag, ammo;
                    try { mag = e.magazineModName; ammo = e.ammoModName; } catch { continue; }

                    if (!IsRegistryName(mag))
                    {
                        var real = RealNameFor(mag);
                        if (real != null)
                        {
                            try { e.magazineModName = real; fixedUp++; } catch { }
                            if (log != null && _repairReports++ < 8)
                                log.Msg("pouch: saved entry {0} named its magazine '{1}', a display label; now '{2}'.", i, mag, real);
                        }
                        else if (log != null && _foreignReports++ < 4)
                            log.Warning("pouch: saved entry {0} names a magazine the registry does not have, '{1}'. "
                                      + "Not ours, left alone; the game's manager will stop at it.", i, mag ?? "(null)");
                    }

                    if (!IsRegistryName(ammo))
                    {
                        var real = RealNameFor(ammo);
                        if (real != null)
                        {
                            try { e.ammoModName = real; fixedUp++; } catch { }
                            if (log != null && _repairReports++ < 8)
                                log.Msg("pouch: saved entry {0} named its round '{1}', a display label; now '{2}'.", i, ammo, real);
                        }
                        else if (log != null && _foreignReports++ < 4)
                            log.Warning("pouch: saved entry {0} names a round the registry does not have, '{1}'. "
                                      + "Not ours, left alone; the game's manager will stop at it.", i, ammo ?? "(null)");
                    }
                }

                if (fixedUp > 0)
                {
                    try { pm.SaveMagsList(); } catch { }
                    if (log != null)
                        log.Msg("pouch: {0} saved name(s) put back to registry names and saved ({1}).", fixedUp, when);
                }
            }
            catch (Exception ex)
            {
                if (log != null) log.Warning("pouch repair: " + ex.Message);
            }
        }
    }
}
