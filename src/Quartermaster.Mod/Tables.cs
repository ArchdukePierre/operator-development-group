// ODG Operations - Quartermaster
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Quartermaster
{
    internal sealed class PartDef
    {
        public string Name = "", Type = "", Cls = "", Note = "";
        public float Weight, V = 1f, H = 1f, Moa = 1f, Ergo, Vel = 1f, Sup = -1f;
        public bool Cosmetic;
        public int Ammo = -1;
        public bool IsAmmo { get { return Ammo >= 0 || Type == "Ammunition"; } }
        public bool Suppressed { get { return Sup >= 0f; } }
    }

    internal sealed class RoundDef
    {
        public string Shell = "", Cal = "", Template = "", Summary = "", Body = "", Best = "";
        public int Idx = -1;
        public string[] Parts = new string[0], Bullets = new string[0], Tags = new string[0];
        public Dictionary<string, float> Stock = new Dictionary<string, float>();
        public Dictionary<string, float> Over = new Dictionary<string, float>();
        public Dictionary<string, string> Matrix = new Dictionary<string, string>();
        public float MoaFactor = 1f, Misfire;
        public bool Subsonic;

        public float Eff(string key)
        {
            float v;
            if (Over.TryGetValue(key, out v)) return v;
            return Stock.TryGetValue(key, out v) ? v : 0f;
        }
        public bool Has(string tag) { return Array.IndexOf(Tags, tag) >= 0; }
    }

    internal sealed class WeaponDef
    {
        public string Name = "", Display = "", Cls = "", Profile = "", Modes = "";
        public float Weight, Moa, Ergo, Range;
        public int Rpm, RpmSup, Cal;
        public bool Manual, SupRpm;
    }

    internal sealed class MagDef { public string Name = ""; public float Base, Round; public int Cap; }
    internal sealed class GarmentDef { public string Name = "", Slot = "", Rating = ""; public float Weight, Dur; public bool Armour; }

    internal static class Tables
    {
        internal static readonly Dictionary<string, PartDef> Parts = new Dictionary<string, PartDef>(StringComparer.OrdinalIgnoreCase);
        internal static readonly List<RoundDef> Rounds = new List<RoundDef>();
        internal static readonly Dictionary<string, RoundDef> RoundByShell = new Dictionary<string, RoundDef>(StringComparer.OrdinalIgnoreCase);
        internal static readonly Dictionary<string, RoundDef> RoundByPart = new Dictionary<string, RoundDef>(StringComparer.OrdinalIgnoreCase);
        internal static readonly Dictionary<string, RoundDef> RoundByBullet = new Dictionary<string, RoundDef>(StringComparer.OrdinalIgnoreCase);
        internal static readonly Dictionary<int, RoundDef> RoundByIndex = new Dictionary<int, RoundDef>();
        internal static readonly Dictionary<string, WeaponDef> Weapons = new Dictionary<string, WeaponDef>(StringComparer.OrdinalIgnoreCase);
        internal static readonly Dictionary<string, MagDef> Mags = new Dictionary<string, MagDef>(StringComparer.OrdinalIgnoreCase);
        internal static readonly Dictionary<string, GarmentDef> Garments = new Dictionary<string, GarmentDef>(StringComparer.OrdinalIgnoreCase);
        internal static string Hash = "------";
        internal static bool Loaded;
        internal static string Error = "";

        internal static void Load(string dir)
        {
            Loaded = false; Error = "";
            Parts.Clear(); Rounds.Clear(); RoundByShell.Clear(); RoundByPart.Clear(); RoundByBullet.Clear();
            RoundByIndex.Clear(); Weapons.Clear(); Mags.Clear(); Garments.Clear();
            try
            {
                if (!Directory.Exists(dir)) { Error = "data folder missing: " + dir; return; }
                LoadParts(Path.Combine(dir, "parts.json"));
                LoadRounds(Path.Combine(dir, "rounds.json"));
                LoadWeapons(Path.Combine(dir, "weapons.json"));
                LoadMags(Path.Combine(dir, "magazines.json"));
                LoadGarments(Path.Combine(dir, "garments.json"));
                Hash = Md5(dir);
                Loaded = Parts.Count > 0 && Rounds.Count > 0 && Weapons.Count > 0;
                if (!Loaded) Error = "tables loaded empty";
            }
            catch (Exception e) { Error = e.GetType().Name + ": " + e.Message; Loaded = false; }
        }

        internal static string Summary()
        {
            if (!Loaded) return "TABLES NOT LOADED (" + Error + ") - the mod is display-only and shows nothing";
            return "tables: " + Parts.Count + " parts, " + Rounds.Count + " rounds, " + Weapons.Count + " weapons, "
                 + Mags.Count + " magazines, " + Garments.Count + " garments   md5 " + Hash;
        }

        static float F(JsonElement e, string k, float d)
        {
            JsonElement v;
            if (!e.TryGetProperty(k, out v)) return d;
            if (v.ValueKind == JsonValueKind.Number) return (float)v.GetDouble();
            if (v.ValueKind == JsonValueKind.True) return 1f;
            if (v.ValueKind == JsonValueKind.False) return 0f;
            return d;
        }
        static int I(JsonElement e, string k, int d) { return (int)Math.Round(F(e, k, d)); }
        static bool B(JsonElement e, string k, bool d)
        {
            JsonElement v;
            if (!e.TryGetProperty(k, out v)) return d;
            if (v.ValueKind == JsonValueKind.True) return true;
            if (v.ValueKind == JsonValueKind.False) return false;
            if (v.ValueKind == JsonValueKind.Number) return v.GetDouble() != 0;
            return d;
        }
        static string S(JsonElement e, string k, string d)
        {
            JsonElement v;
            if (!e.TryGetProperty(k, out v)) return d;
            return v.ValueKind == JsonValueKind.String ? (v.GetString() ?? d) : d;
        }
        static string[] SA(JsonElement e, string k)
        {
            JsonElement v;
            if (!e.TryGetProperty(k, out v) || v.ValueKind != JsonValueKind.Array) return new string[0];
            var l = new List<string>();
            foreach (var x in v.EnumerateArray()) if (x.ValueKind == JsonValueKind.String) l.Add(x.GetString());
            return l.ToArray();
        }

        static void LoadParts(string path)
        {
            using (var doc = JsonDocument.Parse(File.ReadAllText(path)))
            {
                foreach (var e in doc.RootElement.GetProperty("parts").EnumerateArray())
                {
                    var p = new PartDef
                    {
                        Name = S(e, "n", ""), Type = S(e, "t", ""), Cls = S(e, "cls", ""), Note = S(e, "note", ""),
                        Weight = F(e, "w", 0f), V = F(e, "v", 1f), H = F(e, "h", 1f), Moa = F(e, "m", 1f),
                        Ergo = F(e, "e", 0f), Vel = F(e, "s", 1f), Sup = F(e, "sup", -1f), Cosmetic = B(e, "c", false),
                        Ammo = I(e, "ammo", -1),
                    };
                    if (p.Name.Length > 0 && !Parts.ContainsKey(p.Name)) Parts.Add(p.Name, p);
                }
            }
        }

        static void LoadRounds(string path)
        {
            using (var doc = JsonDocument.Parse(File.ReadAllText(path)))
            {
                foreach (var e in doc.RootElement.GetProperty("rounds").EnumerateArray())
                {
                    var r = new RoundDef
                    {
                        Shell = S(e, "shell", ""), Cal = S(e, "cal", ""), Template = S(e, "template", ""),
                        Summary = S(e, "summary", ""), Body = S(e, "body", ""), Best = S(e, "best", ""),
                        Idx = I(e, "idx", -1), Parts = SA(e, "parts"), Bullets = SA(e, "bullets"), Tags = SA(e, "tags"),
                        MoaFactor = F(e, "moaFactor", 1f), Misfire = F(e, "misfire", 0f), Subsonic = B(e, "subsonic", false),
                    };
                    JsonElement o;
                    if (e.TryGetProperty("stock", out o) && o.ValueKind == JsonValueKind.Object)
                        foreach (var kv in o.EnumerateObject()) r.Stock[kv.Name] = F(o, kv.Name, 0f);
                    if (e.TryGetProperty("over", out o) && o.ValueKind == JsonValueKind.Object)
                        foreach (var kv in o.EnumerateObject()) r.Over[kv.Name] = F(o, kv.Name, 0f);
                    if (e.TryGetProperty("matrix", out o) && o.ValueKind == JsonValueKind.Object)
                        foreach (var kv in o.EnumerateObject()) r.Matrix[kv.Name] = S(o, kv.Name, "");
                    Rounds.Add(r);
                    if (r.Shell.Length > 0) RoundByShell[r.Shell] = r;
                    foreach (var p in r.Parts) if (!string.IsNullOrEmpty(p)) RoundByPart[p] = r;
                    foreach (var b in r.Bullets) if (!string.IsNullOrEmpty(b)) RoundByBullet[b] = r;
                    if (r.Idx >= 0) RoundByIndex[r.Idx] = r;
                }
            }
        }

        static void LoadWeapons(string path)
        {
            using (var doc = JsonDocument.Parse(File.ReadAllText(path)))
            {
                foreach (var e in doc.RootElement.GetProperty("weapons").EnumerateArray())
                {
                    var w = new WeaponDef
                    {
                        Name = S(e, "n", ""), Display = S(e, "d", ""), Cls = S(e, "cls", ""), Profile = S(e, "profile", ""),
                        Modes = S(e, "modes", ""), Weight = F(e, "w", 0f), Moa = F(e, "moa", 0f), Ergo = F(e, "ergo", 0f),
                        Range = F(e, "range", 0f), Rpm = I(e, "rpm", 0), RpmSup = I(e, "rpmSup", 0), Cal = I(e, "cal", 0),
                        Manual = B(e, "manual", false), SupRpm = B(e, "supRpm", false),
                    };
                    if (w.Name.Length == 0) continue;
                    string k = Norm(w.Name);
                    if (!Weapons.ContainsKey(k)) Weapons.Add(k, w);
                    string kd = Norm(w.Display);
                    if (kd.Length > 0 && !Weapons.ContainsKey(kd)) Weapons.Add(kd, w);
                }
            }
        }

        static void LoadMags(string path)
        {
            using (var doc = JsonDocument.Parse(File.ReadAllText(path)))
                foreach (var e in doc.RootElement.GetProperty("mags").EnumerateArray())
                {
                    var m = new MagDef { Name = S(e, "n", ""), Base = F(e, "base", 0f), Round = F(e, "round", 0f), Cap = I(e, "cap", 0) };
                    if (m.Name.Length > 0 && !Mags.ContainsKey(m.Name)) Mags.Add(m.Name, m);
                }
        }

        static void LoadGarments(string path)
        {
            using (var doc = JsonDocument.Parse(File.ReadAllText(path)))
                foreach (var e in doc.RootElement.GetProperty("garments").EnumerateArray())
                {
                    var g = new GarmentDef { Name = S(e, "n", ""), Slot = S(e, "slot", ""), Rating = S(e, "rating", ""), Weight = F(e, "w", 0f), Dur = F(e, "dur", 0f), Armour = B(e, "armour", false) };
                    if (g.Name.Length > 0 && !Garments.ContainsKey(g.Name)) Garments.Add(g.Name, g);
                }
        }

        static string Md5(string dir)
        {
            try
            {
                using (var md5 = MD5.Create())
                {
                    var all = new List<byte>();
                    foreach (var f in new[] { "parts.json", "rounds.json", "weapons.json", "magazines.json", "garments.json", "overrides.json" })
                    {
                        var p = Path.Combine(dir, f);
                        if (File.Exists(p)) all.AddRange(File.ReadAllBytes(p));
                    }
                    var h = md5.ComputeHash(all.ToArray());
                    var sb = new StringBuilder();
                    for (int i = 0; i < 6; i++) sb.Append(h[i].ToString("x2"));
                    return sb.ToString();
                }
            }
            catch { return "------"; }
        }

        internal static string Clean(string n)
        {
            if (string.IsNullOrEmpty(n)) return "";
            n = n.Trim();
            while (n.EndsWith("(Clone)")) n = n.Substring(0, n.Length - 7).Trim();
            return n;
        }

        internal static string Norm(string n)
        {
            if (string.IsNullOrEmpty(n)) return "";
            var sb = new StringBuilder(n.Length);
            foreach (var c in n) if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        internal static PartDef Part(string name)
        {
            PartDef p;
            return Parts.TryGetValue(Clean(name), out p) ? p : null;
        }
        internal static WeaponDef Weapon(string goName)
        {
            WeaponDef w;
            return Weapons.TryGetValue(Norm(Clean(goName)), out w) ? w : null;
        }
        internal static MagDef Mag(string name)
        {
            MagDef m;
            return Mags.TryGetValue(Clean(name), out m) ? m : null;
        }
        internal static GarmentDef Garment(string name)
        {
            GarmentDef g;
            return Garments.TryGetValue(Clean(name), out g) ? g : null;
        }
        internal static RoundDef RoundOfShell(string shellGoName)
        {
            RoundDef r;
            return RoundByShell.TryGetValue(Clean(shellGoName), out r) ? r : null;
        }
        internal static RoundDef RoundOfPart(string partName)
        {
            RoundDef r;
            return RoundByPart.TryGetValue(Clean(partName), out r) ? r : null;
        }
        internal static RoundDef RoundOfBullet(string bulletName)
        {
            RoundDef r;
            return RoundByBullet.TryGetValue(Clean(bulletName), out r) ? r : null;
        }
        internal static RoundDef RoundOfIndex(int i)
        {
            RoundDef r;
            return RoundByIndex.TryGetValue(i, out r) ? r : null;
        }
    }
}
