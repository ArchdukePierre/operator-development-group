// ODG Operations - FireteamRoles
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using UnityEngine;

namespace FireteamRoles
{

    internal sealed class Hole
    {
        internal Vector3 C;
        internal Vector3 Fwd;
        internal Vector3 Right;
        internal float HalfW;
        internal float Top, Bottom;
        internal float Front, Back;
        internal float Clear;
        internal float CornerR;
        internal float Rough;
        internal int Seed;
        internal bool Measured;

        internal float Thick { get { return Mathf.Max(0.02f, Back - Front); } }
        internal float DMin { get { return Front - 0.12f; } }
        internal float DMax { get { return Back + 0.12f; } }
        internal float HalfH { get { return (Top + Bottom) * 0.5f; } }
        internal float VMid { get { return (Top - Bottom) * 0.5f; } }
        internal float FloorY { get { return C.y - Bottom; } }
        internal float TopY { get { return C.y + Top; } }

        internal Quaternion Frame { get { return Quaternion.LookRotation(Fwd, Vector3.up); } }
        internal Vector3 Centre { get { return C + Fwd * ((DMin + DMax) * 0.5f) + Vector3.up * VMid; } }
        internal Vector3 HalfExtents { get { return new Vector3(HalfW, HalfH, (DMax - DMin) * 0.5f); } }

        internal Vector3 ClearCentre { get { return C + Fwd * (Back + Clear * 0.5f) + Vector3.up * VMid; } }
        internal Vector3 ClearHalfExtents { get { return new Vector3(HalfW + 0.25f, HalfH, Mathf.Max(0.01f, Clear * 0.5f)); } }

        internal Hole Deep()
        {
            var d = (Hole)MemberwiseClone();
            d.Back = Back + Clear;
            return d;
        }

        internal float Sd(Vector3 world, out float w)
        {
            Vector3 d = world - C;
            w = Vector3.Dot(d, Fwd);
            float u = Vector3.Dot(d, Right);
            float v = d.y - VMid;
            float k = Rough > 0f ? MeshCut.RimScale(u, v, Seed, Rough) : 1f;
            float bx = HalfW * k - CornerR, by = HalfH * k - CornerR;
            float qx = Mathf.Abs(u) - bx, qy = Mathf.Abs(v) - by;
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - CornerR;
        }

        internal bool Inside(Vector3 world)
        {

            if (world.y < FloorY || world.y > TopY) return false;
            float w;
            float sd = Sd(world, out w);
            if (w < DMin || w > DMax) return false;
            return sd <= 0f;
        }

        internal float RimDist(Vector3 world)
        {
            Vector3 d = world - C;
            float u = Vector3.Dot(d, Right), v = d.y - VMid;
            return Mathf.Max(Mathf.Abs(u) / Mathf.Max(0.05f, HalfW), Mathf.Abs(v) / Mathf.Max(0.05f, HalfH));
        }

        internal Vector3 RimPoint(float angle, float radial, float depth)
        {
            float cu = Mathf.Cos(angle), sv = Mathf.Sin(angle);
            float k = Rough > 0f ? MeshCut.RimScale(cu, sv, Seed, Rough) : 1f;
            float r = 1f / Mathf.Max(Mathf.Abs(cu) / Mathf.Max(0.05f, HalfW * k), Mathf.Abs(sv) / Mathf.Max(0.05f, HalfH * k));
            float u = cu * r * radial, v = sv * r * radial;
            v = Mathf.Clamp(v, -HalfH, HalfH);
            return C + Right * u + Vector3.up * (VMid + v) + Fwd * depth;
        }

        internal Bounds WorldBounds(float pad)
        {
            Vector3 he = HalfExtents + Vector3.one * pad;
            Quaternion q = Frame;
            Vector3 c = Centre;
            var b = new Bounds(c, Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                Vector3 s = new Vector3((i & 1) == 0 ? -he.x : he.x, (i & 2) == 0 ? -he.y : he.y, (i & 4) == 0 ? -he.z : he.z);
                b.Encapsulate(c + q * s);
            }
            return b;
        }

        internal string Describe()
        {
            return "w " + (HalfW * 2f).ToString("0.00") + " x h " + (Top + Bottom).ToString("0.00") +
                   " m, wall " + Thick.ToString("0.00") + " m " + (Measured ? "measured" : "assumed") +
                   ", clear " + Clear.ToString("0.0") + " m";
        }
    }
}
