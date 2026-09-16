// ODG Operations - Quartermaster
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;

namespace Quartermaster
{

    internal struct Factors
    {
        public float V, H, Moa, Vel, Ergo, Weight;
        public int Parts, Unknown;
        public static Factors Identity { get { var f = new Factors(); f.V = f.H = f.Moa = f.Vel = 1f; return f; } }
    }

    internal static class Model
    {
        internal const float VMin = 0.65f, VMax = 1.25f;
        internal const float MoaMin = 0.80f, MoaMax = 1.20f;
        internal const float VelMin = 0.92f, VelMax = 1.06f;
        internal const float ErgoMin = 20f, ErgoMax = 100f;

        internal static float Clamp(float v, float lo, float hi) { return v < lo ? lo : (v > hi ? hi : v); }
        internal static float Lerp(float a, float b, float t) { return a + (b - a) * Clamp(t, 0f, 1f); }

        internal static Factors Combine(IList<PartDef> parts, float recoilScale)
        {
            var f = Factors.Identity;
            if (parts == null) return f;
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                if (p == null) { f.Unknown++; continue; }
                f.Parts++;
                f.V *= 1f + (p.V - 1f) * recoilScale;
                f.H *= 1f + (p.H - 1f) * recoilScale;
                f.Moa *= p.Moa;
                f.Vel *= p.Vel;
                f.Ergo += p.Ergo;
                f.Weight += p.Weight;
            }
            f.V = Clamp(f.V, VMin, VMax);
            f.H = Clamp(f.H, VMin, VMax);
            f.Moa = Clamp(f.Moa, MoaMin, MoaMax);
            f.Vel = Clamp(f.Vel, VelMin, VelMax);
            return f;
        }

        internal static float ErgoTotal(float baseErgo, float delta) { return Clamp(baseErgo + delta, ErgoMin, ErgoMax); }

        internal static float AdsFactor(float ergo) { return Lerp(0.80f, 1.15f, (ergo - ErgoMin) / (ErgoMax - ErgoMin)); }

        internal static float StanceFactor(float ergo) { return Lerp(0.90f, 1.08f, (ergo - ErgoMin) / (ErgoMax - ErgoMin)); }

        internal static float Mobility(float massKg, float freeUpTo, float fullAt, float floor)
        {
            floor = Clamp(floor, 0.80f, 1.00f);
            if (fullAt <= freeUpTo + 0.5f) fullAt = freeUpTo + 0.5f;
            float s = Clamp((massKg - freeUpTo) / (fullAt - freeUpTo), 0f, 1f);
            float k = s * s * (3f - 2f * s);
            return 1f - (1f - floor) * k;
        }

        internal static string PenClass(float ap)
        {
            if (ap >= 4.5f) return "V";
            if (ap >= 4.0f) return "IV";
            if (ap >= 3.5f) return "III+";
            if (ap >= 3.0f) return "III";
            if (ap >= 2.5f) return "IIIA";
            if (ap >= 2.0f) return "IIA";
            if (ap >= 1.5f) return "II";
            return "I";
        }

        internal static string Pct(float factor)
        {
            int p = (int)Math.Round((factor - 1f) * 100f);
            return (p > 0 ? "+" : "") + p + " %";
        }
    }
}
