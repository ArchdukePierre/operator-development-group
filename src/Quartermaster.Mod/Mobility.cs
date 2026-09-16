// ODG Operations - Quartermaster
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Quartermaster
{

    internal static class Mobility
    {
        internal static bool Dirty = true;
        internal static float Mass, Mult = 1f;
        internal static readonly List<KeyValuePair<string, float>> Items = new List<KeyValuePair<string, float>>();
        internal static string Note = "";

        static float _next;
        static Il2Cpp.PlayerNetworking _pn;
        static IntPtr _mcPtr = IntPtr.Zero;
        static bool _loggedOnce;

        internal static void Reset() { _pn = null; _mcPtr = IntPtr.Zero; Mult = 1f; Mass = 0f; Dirty = true; Items.Clear(); }

        internal static void Post_KitChanged(Il2Cpp.PlayerNetworking __instance)
        {
            Patches.Fired("PlayerNetworking.SpawnPresetMags/RefillAllMags");
            Dirty = true;
        }

        internal static void Post_MagMoved(Il2Cpp.Magazine __instance)
        {
            Patches.Fired("Magazine.PickupMag/DropMag");
            Dirty = true;
        }

        internal static void Post_MasterUpdate(Il2Cpp.MasterController __instance)
        {
            Patches.Fired("MasterController.Update");
            if (_mcPtr == IntPtr.Zero || __instance == null || __instance.Pointer != _mcPtr) return;
            if (!Main.Enabled.Value || !Main.MobilityOn.Value || Mult >= 0.999f) return;
            try
            {
                var wc = __instance.walkerController;
                if (wc == null) return;
                wc.movementSpeed = wc.movementSpeed * Mult;
                Patches.Fired("mobility-applied");
            }
            catch { }
        }

        internal static void Tick()
        {
            if (!Dirty && Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 4f;
            Dirty = false;
            Resolve();
            Recompute();
        }

        static void Resolve()
        {
            try
            {
                var pm = Il2Cpp.PlayerMaster.MyPlayerMaster;
                var pn = pm != null ? pm.PlayerSpawnedObject : null;
                if (pn == null) { _pn = null; _mcPtr = IntPtr.Zero; return; }
                _pn = pn;
                var mc = pn.masterController;
                _mcPtr = mc != null ? mc.Pointer : IntPtr.Zero;
            }
            catch { _pn = null; _mcPtr = IntPtr.Zero; }
        }

        internal static float Recompute()
        {
            Items.Clear();
            float total = 0f;
            var pn = _pn;
            if (pn == null || !Tables.Loaded) { Mass = 0f; Mult = 1f; return 0f; }
            var seated = new HashSet<IntPtr>();
            try
            {
                total += WeaponMass(SafeWeapon(() => pn.primaryWeapon), seated);
                total += WeaponMass(SafeWeapon(() => pn.secondPrimaryWeapon), seated);
                total += WeaponMass(SafeWeapon(() => pn.secondaryWeapon), seated);
                total += WeaponMass(SafeWeapon(() => pn.specialPurposeWeapon), seated);
            }
            catch { }
            try
            {
                var pouch = pn.MagPouch;
                if (pouch != null)
                {
                    var mags = pouch.GetComponentsInChildren<Il2Cpp.Magazine>(true);
                    float pouchKg = 0f; int n = 0;
                    for (int i = 0; mags != null && i < mags.Length; i++)
                    {
                        var m = mags[i]; if (m == null || seated.Contains(m.Pointer)) continue;
                        pouchKg += MagMass(m); n++;
                    }
                    if (n > 0) { total += pouchKg; Items.Add(new KeyValuePair<string, float>(n + " spare mags", pouchKg)); }
                }
            }
            catch { }
            try
            {
                var cust = pn.GetComponentInChildren<Il2Cpp.CharacterCustomisation>(true);
                if (cust != null) total += GarmentMass(cust);
            }
            catch { }
            try
            {
                var gl = pn.GrenadesList;
                int g = gl != null ? gl.Count : 0;
                if (g > 0) { total += 0.4f * g; Items.Add(new KeyValuePair<string, float>(g + " grenades", 0.4f * g)); }
            }
            catch { }

            Mass = total;
            Mult = Model.Mobility(total, Main.MobilityFreeUpTo.Value, Main.MobilityFullAt.Value, Main.MobilityFloor.Value);
            Items.Sort((a, b) => b.Value.CompareTo(a.Value));
            if (!_loggedOnce && total > 0f)
            {
                _loggedOnce = true;
                Main.Log("kit " + total.ToString("0.0") + " kg -> speed x" + Mult.ToString("0.000") + "   " + Heavy(4));
            }
            return total;
        }

        static Il2Cpp.WeaponV3 SafeWeapon(Func<Il2Cpp.WeaponV3> f) { try { return f(); } catch { return null; } }

        static float WeaponMass(Il2Cpp.WeaponV3 w, HashSet<IntPtr> seated)
        {
            if (w == null) return 0f;
            float kg = 0f;
            string name = Handling.WeaponName(w);
            try
            {
                var wd = Tables.Weapon(name);
                float rec = wd != null ? wd.Weight : 0f;
                if (rec <= 0f) { try { rec = w.gunStats.Weight; } catch { } }
                kg += rec;
                var parts = Handling.PartsOf(w, null, null);
                for (int i = 0; i < parts.Count; i++) kg += parts[i].Weight;
                try
                {
                    var gf = w.gunFunction;
                    var mag = gf != null ? gf.currentMagazine : null;
                    if (mag != null) { kg += MagMass(mag); seated.Add(mag.Pointer); }
                }
                catch { }
            }
            catch { }
            if (kg > 0f) Items.Add(new KeyValuePair<string, float>(name, kg));
            return kg;
        }

        internal static float MagMass(Il2Cpp.Magazine m)
        {
            if (m == null) return 0f;
            try
            {
                var md = Tables.Mag(m.gameObject.name);
                float b = md != null ? md.Base : 0f, r = md != null ? md.Round : 0f;
                if (md == null) { try { b = m.BaseWeight; r = m.BulletWeight; } catch { } }
                int count = 0; try { count = m.ammoAmount; } catch { }
                if (count < 0) count = 0;
                return b + r * count;
            }
            catch { return 0f; }
        }

        static float GarmentMass(Il2Cpp.CharacterCustomisation c)
        {
            float kg = 0f;
            var seen = new HashSet<IntPtr>();
            for (int i = 0; i < 14; i++)
            {
                Il2Cpp.CharacterMod cm = null;
                try
                {
                    switch (i)
                    {
                        case 0: cm = c.HeadGear; break;  case 1: cm = c.EyeWear; break;  case 2: cm = c.EarPro; break;
                        case 3: cm = c.Face; break;      case 4: cm = c.Voice; break;    case 5: cm = c.Shirt; break;
                        case 6: cm = c.Gloves; break;    case 7: cm = c.Wrist; break;    case 8: cm = c.Pants; break;
                        case 9: cm = c.Shoes; break;     case 10: cm = c.Vest; break;    case 11: cm = c.Belt; break;
                        case 12: cm = c.Backpack; break; case 13: cm = c.Tattoo; break;
                    }
                }
                catch { cm = null; }
                kg += OneGarment(cm, seen);
            }
            try
            {
                var ps = c.modParents;
                int n = ps != null ? ps.Count : 0;
                for (int k = 0; k < n; k++)
                {
                    var p = ps[k]; if (p == null) continue;
                    Il2Cpp.CharacterMod cm = null;
                    try { cm = p.currentMod; } catch { }
                    kg += OneGarment(cm, seen);
                }
            }
            catch { }
            return kg;
        }

        static float OneGarment(Il2Cpp.CharacterMod cm, HashSet<IntPtr> seen)
        {
            if (cm == null) return 0f;
            try
            {
                if (!seen.Add(cm.Pointer)) return 0f;
                string name = Tables.Clean(cm.gameObject.name);
                var gd = Tables.Garment(name);
                float w = gd != null ? gd.Weight : 0f;
                if (gd == null) { try { w = cm.Weight; } catch { } }
                if (w > 0.05f) Items.Add(new KeyValuePair<string, float>(name, w));
                return w;
            }
            catch { return 0f; }
        }

        internal static string Heavy(int n)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < Items.Count && i < n; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(Items[i].Key).Append(' ').Append(Items[i].Value.ToString("0.0")).Append(" kg");
            }
            return sb.ToString();
        }

        internal static string StateLine()
        {
            return "kit " + Mass.ToString("0.0") + " kg x" + Mult.ToString("0.000") + (_mcPtr == IntPtr.Zero ? " (no local controller)" : "");
        }
    }
}
