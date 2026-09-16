// ODG Operations - ThirdPerson
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Globalization;
using UnityEngine;

namespace ThirdPerson
{

    internal struct CamParams
    {
        public float Distance;
        public float Side;
        public float Height;
        public float TiltPitch;
        public float TiltYaw;
        public float Fov;
        public bool Converge;
        public bool Right;

        public const float DistanceMin = 0.4f, DistanceMax = 6f;
        public const float SideMin = 0f, SideMax = 1.5f;
        public const float HeightMin = -1f, HeightMax = 1.5f;
        public const float TiltMin = -45f, TiltMax = 45f;
        public const float FovMin = 40f, FovMax = 110f;

        public static CamParams Default()
        {
            return new CamParams
            {
                Distance = 1.7f, Side = 0.55f, Height = 0.25f,
                TiltPitch = 0f, TiltYaw = 0f, Fov = 0f,
                Converge = true, Right = true
            };
        }

        public CamParams Clamped()
        {
            var p = this;
            p.Distance = Mathf.Clamp(p.Distance, DistanceMin, DistanceMax);
            p.Side = Mathf.Clamp(p.Side, SideMin, SideMax);
            p.Height = Mathf.Clamp(p.Height, HeightMin, HeightMax);
            p.TiltPitch = Mathf.Clamp(p.TiltPitch, TiltMin, TiltMax);
            p.TiltYaw = Mathf.Clamp(p.TiltYaw, TiltMin, TiltMax);
            if (p.Fov != 0f) p.Fov = Mathf.Clamp(p.Fov, FovMin, FovMax);
            if (float.IsNaN(p.Distance) || float.IsNaN(p.Side) || float.IsNaN(p.Height)
                || float.IsNaN(p.TiltPitch) || float.IsNaN(p.TiltYaw) || float.IsNaN(p.Fov))
                return Default();
            return p;
        }

        public string Serialize()
        {
            var ci = CultureInfo.InvariantCulture;
            return string.Join("|",
                Distance.ToString("0.###", ci), Side.ToString("0.###", ci), Height.ToString("0.###", ci),
                TiltPitch.ToString("0.##", ci), TiltYaw.ToString("0.##", ci), Fov.ToString("0.#", ci),
                Converge ? "1" : "0", Right ? "1" : "0");
        }

        public static bool TryParse(string s, out CamParams p)
        {
            p = Default();
            if (string.IsNullOrEmpty(s)) return false;
            var a = s.Split('|');
            if (a.Length < 8) return false;
            var ci = CultureInfo.InvariantCulture;
            try
            {
                var q = new CamParams
                {
                    Distance = float.Parse(a[0].Trim(), ci),
                    Side = float.Parse(a[1].Trim(), ci),
                    Height = float.Parse(a[2].Trim(), ci),
                    TiltPitch = float.Parse(a[3].Trim(), ci),
                    TiltYaw = float.Parse(a[4].Trim(), ci),
                    Fov = float.Parse(a[5].Trim(), ci),
                    Converge = a[6].Trim() == "1",
                    Right = a[7].Trim() == "1"
                };
                p = q.Clamped();
                return true;
            }
            catch { p = Default(); return false; }
        }
    }

    internal sealed class Preset
    {
        public string Name;
        public CamParams Params;

        public const int Count = 4;

        public static Preset[] Defaults()
        {
            return new[]
            {
                new Preset { Name = "Close",     Params = new CamParams { Distance = 1.4f, Side = 0.50f, Height = 0.20f, Fov = 0f,  Converge = true, Right = true } },
                new Preset { Name = "Standard",  Params = new CamParams { Distance = 1.9f, Side = 0.60f, Height = 0.30f, Fov = 0f,  Converge = true, Right = true } },
                new Preset { Name = "Wide",      Params = new CamParams { Distance = 3.0f, Side = 0.55f, Height = 0.60f, Fov = 70f, Converge = true, Right = true } },
                new Preset { Name = "Low tight", Params = new CamParams { Distance = 1.2f, Side = 0.70f, Height = -0.05f, Fov = 0f, Converge = true, Right = true } },
            };
        }

        public string Serialize()
        {
            return (Name ?? "Preset").Replace("|", "/") + "|" + Params.Serialize();
        }

        public static Preset Parse(string s, Preset fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            int cut = s.IndexOf('|');
            if (cut <= 0) return fallback;
            CamParams p;
            if (!CamParams.TryParse(s.Substring(cut + 1), out p)) return fallback;
            string name = s.Substring(0, cut).Trim();
            return new Preset { Name = string.IsNullOrEmpty(name) ? fallback.Name : name, Params = p };
        }
    }
}
