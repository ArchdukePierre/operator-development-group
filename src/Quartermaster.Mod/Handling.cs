// ODG Operations - Quartermaster
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Quartermaster
{

    internal static class Handling
    {
        internal sealed class Base
        {
            public float Moa, Tbr, Ergo, Ads, Stance, Weight;
            public string Name = "";
            public bool Known;
        }

        static readonly Dictionary<int, Base> _base = new Dictionary<int, Base>();
        static readonly Dictionary<int, Factors> _factors = new Dictionary<int, Factors>();
        static readonly HashSet<string> _logged = new HashSet<string>();
        internal static string LastApplied = "";

        internal static void Reset() { _base.Clear(); _factors.Clear(); }

        internal static void Post_Rebuild(Il2Cpp.WeaponMods __instance)
        {
            Patches.Fired("WeaponMods.RebuildWeaponWithNetworkPreset");
            Apply(WeaponOf(__instance), "rebuild");
        }

        internal static void Post_AddRemove(Il2Cpp.WeaponMods __instance)
        {
            Patches.Fired("WeaponMods.Add/RemoveAttachment");
            Apply(WeaponOf(__instance), "edit");
            Bench.Dirty = true;
            Mobility.Dirty = true;
        }

        internal static void Post_OnStartAuthority(Il2Cpp.WeaponV3 __instance)
        {
            Patches.Fired("WeaponV3.OnStartAuthority");
            Apply(__instance, "authority");
            Mobility.Dirty = true;
        }

        internal static void Post_FinishEquip(Il2Cpp.PlayerNetworking __instance)
        {
            Patches.Fired("PlayerNetworking.FinishEquip");
            try
            {
                if (__instance == null || !__instance.isOwned) return;
                Apply(__instance.c_activeWeapon, "equip");
            }
            catch { }
            Mobility.Dirty = true;
        }

        static Il2Cpp.WeaponV3 WeaponOf(Il2Cpp.WeaponMods m)
        {
            try { return m != null ? m.Weapon : null; } catch { return null; }
        }

        internal static string PartName(Il2Cpp.WeaponMod m)
        {
            string n = "";
            try { n = Tables.Clean(m.gameObject.name); } catch { }
            if (n.Length == 0) { try { n = Tables.Clean(m.WeaponModName()); } catch { } }
            return n;
        }

        internal static string WeaponName(Il2Cpp.WeaponV3 w)
        {
            try { return Tables.Clean(w.gameObject.name); } catch { return ""; }
        }

        internal static List<PartDef> PartsOf(Il2Cpp.WeaponV3 w, List<string> names, List<string> unknown)
        {
            var list = new List<PartDef>();
            if (w == null) return list;
            try
            {
                var mods = w.mods; if (mods == null) return list;
                var cur = mods.CurrentMods; if (cur == null) return list;
                int n = cur.Count;
                for (int i = 0; i < n; i++)
                {
                    var m = cur[i]; if (m == null) continue;
                    string nm = PartName(m);
                    var p = Tables.Part(nm);
                    if (p == null)
                    {
                        if (unknown != null) unknown.Add(nm);
                        float lw = 0f; try { lw = m.Weight; } catch { }
                        p = new PartDef { Name = nm, Type = "?", Cls = "unknown", Cosmetic = true, Weight = lw };
                    }
                    list.Add(p);
                    if (names != null) names.Add(nm);
                }
            }
            catch { }
            return list;
        }

        internal static Factors FactorsFor(Il2Cpp.WeaponV3 w)
        {
            if (w == null) return Factors.Identity;
            return Model.Combine(PartsOf(w, null, null), Mathf.Clamp(Main.RecoilScale.Value, 0f, 2f));
        }

        internal static RoundDef SeatedRound(Il2Cpp.WeaponV3 w)
        {
            if (w == null) return null;
            try
            {
                var mods = w.mods;
                var cur = mods != null ? mods.CurrentMods : null;
                if (cur != null)
                {
                    int n = cur.Count;
                    for (int i = 0; i < n; i++)
                    {
                        var m = cur[i]; if (m == null) continue;
                        if (m.attachmentType != Il2Cpp.WeaponMod.AttachmentType.Ammunition) continue;
                        RoundDef r = null;
                        try { r = Tables.RoundOfIndex(m.AmmoReferenceIndex); } catch { }
                        if (r == null) r = Tables.RoundOfPart(PartName(m));
                        if (r != null) return r;
                    }
                }
            }
            catch { }
            try
            {
                var gf = w.gunFunction;
                var shell = gf != null ? gf.currentRoundInChamber : null;
                if (shell != null)
                {
                    var r = Tables.RoundOfShell(shell.gameObject.name);
                    if (r != null) return r;
                }
            }
            catch { }
            return null;
        }

        internal static Base BaseOf(Il2Cpp.WeaponV3 w)
        {
            if (w == null) return null;
            int id; try { id = w.GetInstanceID(); } catch { return null; }
            Base b;
            return _base.TryGetValue(id, out b) ? b : null;
        }

        internal static float SeatedMagMass(Il2Cpp.WeaponV3 w)
        {
            try
            {
                var gf = w.gunFunction;
                var mag = gf != null ? gf.currentMagazine : null;
                return mag != null ? Mobility.MagMass(mag) : 0f;
            }
            catch { return 0f; }
        }

        internal static bool Apply(Il2Cpp.WeaponV3 w, string why)
        {
            if (w == null) return false;
            bool owned = false; try { owned = w.isOwned; } catch { }
            int id; try { id = w.GetInstanceID(); } catch { return false; }
            string goName = WeaponName(w);
            var wd = Tables.Weapon(goName);

            Base b;
            if (!_base.TryGetValue(id, out b))
            {
                b = new Base { Name = goName, Known = wd != null };
                try { var gs = w.gunStats; if (gs != null) { b.Moa = gs.MOA; b.Tbr = gs.TimeBetweenRounds; b.Ergo = gs.Ergonomics; b.Weight = gs.Weight; } } catch { }
                try { var a = w.ADS_Fixed; if (a != null) b.Ads = a.adsSpeedModifer; } catch { }
                try { var s = w.weaponStance; if (s != null) b.Stance = s.AnimatorSpeed; } catch { }
                if (b.Ads <= 0f) b.Ads = 1f;
                if (b.Stance <= 0f) b.Stance = 1f;
                if (wd != null)
                {
                    if (b.Moa <= 0f) b.Moa = wd.Moa;
                    if (b.Ergo <= 0f) b.Ergo = wd.Ergo;
                    if (b.Weight <= 0f) b.Weight = wd.Weight;
                }
                _base[id] = b;
            }

            var f = FactorsFor(w);
            var round = SeatedRound(w);
            float moaF = f.Moa * (round != null ? round.MoaFactor : 1f);
            float ergo = Model.ErgoTotal(b.Ergo, f.Ergo);
            _factors[id] = f;

            bool wrote = false;
            if (owned && b.Known && Main.Enabled.Value && Main.HandlingOn.Value && Tables.Loaded)
            {
                try { var gs = w.gunStats; if (gs != null && b.Moa > 0f) { gs.MOA = b.Moa * moaF; wrote = true; } } catch { }
                try { var a = w.ADS_Fixed; if (a != null) a.adsSpeedModifer = b.Ads * Model.AdsFactor(ergo); } catch { }
                try { var s = w.weaponStance; if (s != null) s.AnimatorSpeed = b.Stance * Model.StanceFactor(ergo); } catch { }
            }
            if (owned) Recoil.Register(w, f);
            Bench.Dirty = true;

            if (owned)
            {
                LastApplied = goName + " V" + f.V.ToString("0.00") + " H" + f.H.ToString("0.00") + " MOA x" + moaF.ToString("0.00")
                            + " ergo " + ergo.ToString("0") + " (" + f.Parts + " parts" + (f.Unknown > 0 ? ", " + f.Unknown + " unknown" : "") + ")";
                if (_logged.Add(goName + "|" + why))
                    Main.Log("handling [" + why + "] " + LastApplied + (wrote ? "" : b.Known ? "  (display only)" : "  (not in weapons.json: display only)"));
            }
            return wrote;
        }

        internal static string StateLine() { return "handling: " + (LastApplied.Length > 0 ? LastApplied : "nothing applied yet"); }
    }
}
