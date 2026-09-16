// ODG Operations - Quartermaster
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using HarmonyLib;

namespace Quartermaster
{

    internal static class Patches
    {
        static int _attempted, _bound;
        static readonly List<string> _missed = new List<string>();
        static readonly List<string> _failed = new List<string>();
        static readonly HashSet<string> _fired = new HashSet<string>();

        internal static void Install(HarmonyLib.Harmony h)
        {

            Patch(h, typeof(Il2Cpp.WeaponMods), "RebuildWeaponWithNetworkPreset", null, null, nameof(Handling.Post_Rebuild));
            Patch(h, typeof(Il2Cpp.WeaponMods), "AddNewAttachment", null, null, nameof(Handling.Post_AddRemove));
            Patch(h, typeof(Il2Cpp.WeaponMods), "RemoveAttachment", null, null, nameof(Handling.Post_AddRemove));
            Patch(h, typeof(Il2Cpp.WeaponV3), "OnStartAuthority", null, null, nameof(Handling.Post_OnStartAuthority));
            Patch(h, typeof(Il2Cpp.PlayerNetworking), "FinishEquip", null, null, nameof(Handling.Post_FinishEquip));

            Patch(h, typeof(Il2CppKinemation.FPSFramework.Runtime.Recoil.RecoilAnimation), "CalculateTargetData", null, null, nameof(Recoil.Post_CalculateTargetData));
            Patch(h, typeof(Il2CppKinemation.FPSFramework.Runtime.Recoil.RecoilAnimation), "Play", null, nameof(Recoil.Pre_Play), null);

            Patch(h, typeof(Il2Cpp.WeaponV3), "SpawnProjectile", null, nameof(Rounds.Pre_SpawnProjectile), nameof(Rounds.Post_SpawnProjectile));
            Patch(h, typeof(Il2Cpp.Bullet), "SpawnBullet", null, null, nameof(Rounds.Post_SpawnBullet));
            Patch(h, typeof(Il2Cpp.WeaponV3), "DischargeRound", null, null, nameof(Rounds.Post_DischargeRound));

            Patch(h, typeof(Il2Cpp.MasterController), "Update", null, null, nameof(Mobility.Post_MasterUpdate));
            Patch(h, typeof(Il2Cpp.PlayerNetworking), "SpawnPresetMags", null, null, nameof(Mobility.Post_KitChanged));
            Patch(h, typeof(Il2Cpp.PlayerNetworking), "RefillAllMags", null, null, nameof(Mobility.Post_KitChanged));
            Patch(h, typeof(Il2Cpp.Magazine), "PickupMag", null, null, nameof(Mobility.Post_MagMoved));
            Patch(h, typeof(Il2Cpp.Magazine), "DropMag", null, null, nameof(Mobility.Post_MagMoved));

            Patch(h, typeof(Il2Cpp.WeaponModding), "StartModding", null, null, nameof(Bench.Post_StartModding));
            Patch(h, typeof(Il2Cpp.WeaponModding), "StopModding", null, null, nameof(Bench.Post_StopModding));
            Patch(h, typeof(Il2Cpp.ModUI), "AddAttachment", null, null, nameof(Bench.Post_AddAttachment));
        }

        internal static string Summary()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(_bound).Append('/').Append(_attempted).Append(" hooks bound");
            if (_missed.Count > 0) sb.Append("   MISSING: ").Append(string.Join(", ", _missed.ToArray()));
            if (_failed.Count > 0) sb.Append("   FAILED: ").Append(string.Join(", ", _failed.ToArray()));
            return sb.ToString();
        }

        internal static bool HasFired(string what) { return _fired.Contains(what); }

        internal static void Fired(string what)
        {
            if (_fired.Contains(what)) return;
            _fired.Add(what);
            Main.L.Msg("FIRED  " + what);
        }

        static void Patch(HarmonyLib.Harmony h, Type type, string method, Type[] args, string prefix, string postfix)
        {
            try
            {
                _attempted++;
                var mi = args == null ? AccessTools.Method(type, method) : AccessTools.Method(type, method, args);
                if (mi == null)
                {
                    _missed.Add(type.Name + "." + method);
                    Main.L.Warning("HOOK MISS  " + type.Name + "." + method);
                    return;
                }
                Type owner = prefix != null ? OwnerOf(prefix) : OwnerOf(postfix);
                h.Patch(mi,
                    prefix == null ? null : new HarmonyMethod(OwnerOf(prefix), prefix),
                    postfix == null ? null : new HarmonyMethod(OwnerOf(postfix), postfix));
                _bound++;
                Main.L.Msg("hooked " + type.Name + "." + method);
            }
            catch (Exception e)
            {
                _failed.Add(type.Name + "." + method + ": " + e.Message);
                Main.L.Error("HOOK FAIL  " + type.Name + "." + method + ": " + e.Message);
            }
        }

        static Type OwnerOf(string method)
        {
            const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            foreach (var t in new[] { typeof(Handling), typeof(Recoil), typeof(Rounds), typeof(Mobility), typeof(Bench) })
                if (t.GetMethod(method, F) != null) return t;
            throw new Exception("no owner for patch method " + method);
        }
    }
}
