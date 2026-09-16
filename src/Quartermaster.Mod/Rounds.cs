// ODG Operations - Quartermaster
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Quartermaster
{

    internal static class Rounds
    {
        static Il2Cpp.WeaponV3 _ctx;
        static readonly HashSet<string> _logged = new HashSet<string>();
        internal static string LastRound = "";

        internal static void Reset() { _ctx = null; }

        internal static void Pre_SpawnProjectile(Il2Cpp.WeaponV3 __instance) { _ctx = __instance; }
        internal static void Post_SpawnProjectile() { _ctx = null; }

        internal static void Post_DischargeRound(Il2Cpp.WeaponV3 __instance)
        {
            Patches.Fired("WeaponV3.DischargeRound");
            if (!Main.Verbose.Value) return;
            try
            {
                if (!__instance.isOwned) return;
                var gs = __instance.gunStats;
                Main.Log("shot: " + Handling.WeaponName(__instance) + " MOA " + (gs != null ? gs.MOA.ToString("0.00") : "?")
                       + " _MOA " + __instance._MOA.ToString("0.0000") + " recoil mode " + Recoil.Mode);
            }
            catch { }
        }

        internal static void Post_SpawnBullet(Il2Cpp.Bullet bulletPrefab, Il2Cpp.BulletInstance __result)
        {
            Patches.Fired("Bullet.SpawnBullet");
            if (__result == null || !Main.Enabled.Value || !Main.RoundsOn.Value || !Tables.Loaded) return;
            try
            {
                RoundDef rd = null;
                float vel = 1f;
                var w = _ctx;
                if (w != null)
                {
                    try
                    {
                        var gf = w.gunFunction;
                        var shell = gf != null ? gf.currentRoundInChamber : null;
                        if (shell != null) rd = Tables.RoundOfShell(shell.gameObject.name);
                    }
                    catch { }
                    try { vel = Handling.FactorsFor(w).Vel; } catch { vel = 1f; }
                }
                string bname = "";
                try { bname = Tables.Clean(bulletPrefab != null ? bulletPrefab.name : ""); } catch { }
                if (rd == null) rd = Tables.RoundOfBullet(bname);
                if (rd == null) return;

                bool isPlayer = false; try { isPlayer = __result.isPlayer; } catch { }
                if (!isPlayer && !Main.ApplyToBots.Value) return;

                foreach (var kv in rd.Over) Write(__result, kv.Key, kv.Value);

                if (Math.Abs(vel - 1f) > 0.001f)
                {
                    __result.speed = __result.speed * vel;
                    try
                    {
                        var bv = __result.bulletVelocity;
                        if (bv.sqrMagnitude > 0.01f) __result.bulletVelocity = bv * vel;
                    }
                    catch { }
                }

                LastRound = rd.Shell + (rd.Over.Count > 0 ? " (v1 edits)" : "") + (Math.Abs(vel - 1f) > 0.001f ? " vel x" + vel.ToString("0.00") : "");
                if (_logged.Add(rd.Shell))
                    Main.Log("round: " + rd.Shell + " <- " + bname + (isPlayer ? "" : " [bot]") + ", " + rd.Over.Count + " override(s)"
                           + (Math.Abs(vel - 1f) > 0.001f ? ", barrel velocity x" + vel.ToString("0.00") : ""));
                Patches.Fired("round-applied");
            }
            catch (Exception e) { Main.Once("round apply: " + e.Message); }
        }

        static void Write(Il2Cpp.BulletInstance b, string key, float v)
        {
            try
            {
                switch (key)
                {
                    case "damage":      b.damage = (int)Math.Round(v); break;
                    case "speed":       b.speed = v; break;
                    case "ap":          b.armourPenetration = v; break;
                    case "armorDamage": b.armorDamage = v; break;
                    case "pen":         b.penetrationAmount = v; break;
                    case "bleed":       b.bleedChance = (int)Math.Round(v); break;
                    case "heavy":       b.HeavyBleedChance = (int)Math.Round(v); break;
                    case "bleedDmg":    b.bleedDamage = v; break;
                    case "frac":        b.FractureChance = (int)Math.Round(v); break;
                    case "frag":        b.fragmentationChance = v; break;
                    case "fragMult":    b.maxFragmentationMultiplier = v; break;
                    case "tracer":      b.EnableTracer = v > 0.5f; break;
                    case "selfMOA":     b.selfMOA = v; break;
                    case "supp":        b.SuppressionAmount = v; break;
                }
            }
            catch (Exception e) { Main.Once("round field " + key + ": " + e.Message); }
        }
    }
}
