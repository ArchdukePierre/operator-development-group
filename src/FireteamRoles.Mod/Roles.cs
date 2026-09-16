// ODG Operations - FireteamRoles
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using UnityEngine;

namespace FireteamRoles
{
    internal enum Role
    {
        None = 0,
        Assaulter = 1,
        Breacher = 2,
        Sniper = 3,
        Medic = 4,
        Jtac = 5
    }

    internal static class RoleInfo
    {
        internal const int Count = 6;

        internal static string Name(Role r)
        {
            switch (r)
            {
                case Role.Assaulter: return "ASSAULTER";
                case Role.Breacher: return "BREACHER";
                case Role.Sniper: return "SNIPER";
                case Role.Medic: return "MEDIC";
                case Role.Jtac: return "JTAC OPERATOR";
                default: return "-";
            }
        }

        internal static string Short(Role r)
        {
            switch (r)
            {
                case Role.Assaulter: return "ASL";
                case Role.Breacher: return "BRC";
                case Role.Sniper: return "SNP";
                case Role.Medic: return "MED";
                case Role.Jtac: return "JTC";
                default: return "--";
            }
        }

        internal static string Perk(Role r)
        {
            switch (r)
            {
                case Role.Breacher:
                    return Cfg.BreacherExplosives.Value + " explosives, and the only one who can breach a wall";
                case Role.Medic:
                    return Cfg.MedicItemCount.Value + " of every med, epinephrine, treats and revives";
                case Role.Assaulter:
                    return "sprint " + Mathf.RoundToInt(Cfg.AssaulterSprintBonus.Value * 100f) +
                           "% faster and endless, deploys a ladder [" +
                           Cfg.LadderKey.Value.ToUpperInvariant() + "]";
                case Role.Sniper:
                    return "arms never shake, AI " +
                           Mathf.RoundToInt(Cfg.SniperHitChanceCut.Value * 100f) + "% less likely to hit you";
                case Role.Jtac:
                    return Cfg.JtacMissiles.Value + " Hellfires on the ATAK, an enemy radar, and a thermal FPV drone [" +
                           Cfg.DroneKey.Value.ToUpperInvariant() + "]";
                default:
                    return "no role, no perks";
            }
        }

        internal static Color Colour(Role r)
        {
            switch (r)
            {
                case Role.Assaulter: return new Color(0.82f, 0.82f, 0.80f, 1f);
                case Role.Breacher: return new Color(0.90f, 0.58f, 0.20f, 1f);
                case Role.Sniper: return new Color(0.55f, 0.75f, 0.45f, 1f);
                case Role.Medic: return new Color(0.85f, 0.30f, 0.30f, 1f);
                case Role.Jtac: return new Color(0.40f, 0.70f, 0.98f, 1f);
                default: return new Color(0.55f, 0.55f, 0.55f, 1f);
            }
        }

        internal static Role Clamp(int v)
        {
            if (v < 0 || v >= Count) return Role.None;
            return (Role)v;
        }
    }
}
