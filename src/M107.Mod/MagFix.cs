// ODG Operations - M107
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using UnityEngine;
using Il2CppInterop.Runtime;

namespace M107
{

    internal static class MagFix
    {
        static Il2Cpp.PlayerMaster _master;
        static int _repaired;

        static bool OursMag(string name) =>
            !string.IsNullOrEmpty(name) && name.StartsWith(Parts.Prefix, StringComparison.Ordinal);

        internal static Il2Cpp.WeaponModInfo OurAmmoInfo()
        {
            try
            {
                if (Parts.AmmoId < 0) return null;
                var lm = Il2Cpp.LoadoutManager.singleton;
                var infos = lm == null ? null : lm.WeaponModInfo;
                if (infos == null || Parts.AmmoId >= infos.Length) return null;
                return infos[Parts.AmmoId];
            }
            catch { return null; }
        }

        static Il2Cpp.PlayerMaster Master()
        {
            try { if (_master != null) return _master; } catch { _master = null; }
            try
            {
                var pms = Il2Cpp.PlayerMaster.AllPlayerMasters;
                if (pms == null) return null;
                for (int i = 0; i < pms.Count; i++)
                {
                    var pm = pms[i]; if (pm == null) continue;
                    bool local = false; try { local = pm.isLocalPlayer; } catch { }
                    if (!local && pms.Count > 1) continue;
                    _master = pm; return pm;
                }
            }
            catch { }
            return null;
        }

        internal static void ResetForScene() { _master = null; }

        internal static void RepairPouch(string when)
        {
            try
            {

                string want = Parts.AmmoDonorName;
                if (string.IsNullOrEmpty(want)) return;

                var pm = Master(); if (pm == null) return;
                var list = pm.Magazines; if (list == null) return;
                var pres = list.presets; if (pres == null) return;

                int fixedUp = 0;
                for (int i = 0; i < pres.Count; i++)
                {
                    var e = pres[i]; if (e == null) continue;
                    string mag, ammo;
                    try { mag = e.magazineModName; ammo = e.ammoModName; } catch { continue; }
                    if (!OursMag(mag)) continue;
                    if (string.Equals(ammo, want, StringComparison.Ordinal)) continue;
                    try { e.ammoModName = want; fixedUp++; } catch { }
                }

                if (fixedUp > 0)
                {
                    try { pm.SaveMagsList(); } catch { }
                    _repaired += fixedUp;
                    M107Mod.Log("pouch: put the round back to '" + want + "' on " + fixedUp +
                                " magazine entr(y/ies) so they reload again (" + when + ") - the pouch still SHOWS 12.7mm");
                }
            }
            catch (Exception e) { M107Mod.Log("pouch: repair err: " + e.Message); }
        }
    }
}
