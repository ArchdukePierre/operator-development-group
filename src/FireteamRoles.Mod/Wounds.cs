// ODG Operations - FireteamRoles
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;

namespace FireteamRoles
{

    internal static class Wounds
    {
        internal struct Entry
        {
            internal Il2Cpp.HealthBodyPart Part;
            internal bool Bleeding;
            internal float Rate;
            internal bool Fractured;
            internal float HP;
        }

        internal static readonly Il2Cpp.HealthBodyPart[] Parts =
        {
            Il2Cpp.HealthBodyPart.Head,
            Il2Cpp.HealthBodyPart.Chest,
            Il2Cpp.HealthBodyPart.Stomach,
            Il2Cpp.HealthBodyPart.LArm,
            Il2Cpp.HealthBodyPart.RArm,
            Il2Cpp.HealthBodyPart.LLeg,
            Il2Cpp.HealthBodyPart.RLeg
        };

        internal static string Label(Il2Cpp.HealthBodyPart p)
        {
            switch (p)
            {
                case Il2Cpp.HealthBodyPart.Head: return "HEAD";
                case Il2Cpp.HealthBodyPart.Chest: return "CHEST";
                case Il2Cpp.HealthBodyPart.Stomach: return "STOMACH";
                case Il2Cpp.HealthBodyPart.LArm: return "LEFT ARM";
                case Il2Cpp.HealthBodyPart.RArm: return "RIGHT ARM";
                case Il2Cpp.HealthBodyPart.LLeg: return "LEFT LEG";
                case Il2Cpp.HealthBodyPart.RLeg: return "RIGHT LEG";
                default: return p.ToString();
            }
        }

        internal static float Bleed(Il2Cpp.Health h, Il2Cpp.HealthBodyPart p)
        {
            if (h == null) return 0f;
            try
            {
                switch (p)
                {
                    case Il2Cpp.HealthBodyPart.Head: return h.BleedingDamage_Head;
                    case Il2Cpp.HealthBodyPart.Chest: return h.BleedingDamage_Chest;
                    case Il2Cpp.HealthBodyPart.Stomach: return h.BleedingDamage_Stomach;
                    case Il2Cpp.HealthBodyPart.LArm: return h.BleedingDamage_LArm;
                    case Il2Cpp.HealthBodyPart.RArm: return h.BleedingDamage_RArm;
                    case Il2Cpp.HealthBodyPart.LLeg: return h.BleedingDamage_LLeg;
                    case Il2Cpp.HealthBodyPart.RLeg: return h.BleedingDamage_RLeg;
                }
            }
            catch { }
            return 0f;
        }

        internal static bool Fractured(Il2Cpp.Health h, Il2Cpp.HealthBodyPart p)
        {
            if (h == null) return false;
            try
            {
                switch (p)
                {
                    case Il2Cpp.HealthBodyPart.LArm: return h.Fractured_LArm;
                    case Il2Cpp.HealthBodyPart.RArm: return h.Fractured_RArm;
                    case Il2Cpp.HealthBodyPart.LLeg: return h.Fractured_LLeg;
                    case Il2Cpp.HealthBodyPart.RLeg: return h.Fractured_RLeg;
                }
            }
            catch { }
            return false;
        }

        internal static void SetBleed(Il2Cpp.Health h, Il2Cpp.HealthBodyPart p, float rate)
        {
            if (h == null) return;
            try
            {
                switch (p)
                {
                    case Il2Cpp.HealthBodyPart.Head: h.NetworkBleedingDamage_Head = rate; break;
                    case Il2Cpp.HealthBodyPart.Chest: h.NetworkBleedingDamage_Chest = rate; break;
                    case Il2Cpp.HealthBodyPart.Stomach: h.NetworkBleedingDamage_Stomach = rate; break;
                    case Il2Cpp.HealthBodyPart.LArm: h.NetworkBleedingDamage_LArm = rate; break;
                    case Il2Cpp.HealthBodyPart.RArm: h.NetworkBleedingDamage_RArm = rate; break;
                    case Il2Cpp.HealthBodyPart.LLeg: h.NetworkBleedingDamage_LLeg = rate; break;
                    case Il2Cpp.HealthBodyPart.RLeg: h.NetworkBleedingDamage_RLeg = rate; break;
                }
            }
            catch { }
        }

        internal static int OpenCount(Il2Cpp.Health h)
        {
            int n = 0;
            for (int i = 0; i < Parts.Length; i++)
            {
                if (Bleed(h, Parts[i]) > 0.001f) n++;
                if (Fractured(h, Parts[i])) n++;
            }
            return n;
        }

        internal static float HP(Il2Cpp.Health h, Il2Cpp.HealthBodyPart p)
        {
            if (h == null) return 0f;
            try
            {
                switch (p)
                {
                    case Il2Cpp.HealthBodyPart.Head: return h.HeadHP;
                    case Il2Cpp.HealthBodyPart.Chest: return h.ChestHP;
                    case Il2Cpp.HealthBodyPart.Stomach: return h.StomachHP;
                    case Il2Cpp.HealthBodyPart.LArm: return h.LArmHP;
                    case Il2Cpp.HealthBodyPart.RArm: return h.RArmHP;
                    case Il2Cpp.HealthBodyPart.LLeg: return h.LLegHP;
                    case Il2Cpp.HealthBodyPart.RLeg: return h.RLegHP;
                }
            }
            catch { }
            return 0f;
        }

        internal static List<Entry> Read(Il2Cpp.Health h)
        {
            var outp = new List<Entry>(Parts.Length);
            for (int i = 0; i < Parts.Length; i++)
            {
                var p = Parts[i];
                float rate = Bleed(h, p);
                outp.Add(new Entry
                {
                    Part = p,
                    Rate = rate,
                    Bleeding = rate > 0.001f,
                    Fractured = Fractured(h, p),
                    HP = HP(h, p)
                });
            }
            return outp;
        }

        internal static bool IsClean(Il2Cpp.Health h)
        {
            if (h == null) return false;
            for (int i = 0; i < Parts.Length; i++)
            {
                if (Bleed(h, Parts[i]) > 0.001f) return false;
                if (Fractured(h, Parts[i])) return false;
            }
            return true;
        }

        internal static List<string> OpenList(Il2Cpp.Health h)
        {
            var outp = new List<string>();
            if (h == null) return outp;
            for (int i = 0; i < Parts.Length; i++)
            {
                var p = Parts[i];
                if (Bleed(h, p) > 0.001f) outp.Add(Label(p).ToLowerInvariant() + " bleeding");
                if (Fractured(h, p)) outp.Add(Label(p).ToLowerInvariant() + " fractured");
            }
            return outp;
        }

        internal static string OpenSummary(Il2Cpp.Health h)
        {
            var l = OpenList(h);
            if (l.Count == 0) return "stabilised";
            return string.Join(", ", l.ToArray());
        }

        internal static string Suggest(Entry e)
        {
            if (e.Fractured) return "splint";
            if (!e.Bleeding) return "";
            if (e.Rate >= 8f) return "tourniquet or hemostatic";
            if (e.Rate >= 3f) return "hemostatic or pressure dressing";
            return "bandage";
        }
    }
}
