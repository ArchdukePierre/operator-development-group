// ODG Operations - NewSlots
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NewSlots
{

    internal sealed class Def
    {
        internal string ModName;
        internal string Display;
        internal string Art;
        internal string ArtNormal;
        internal string ArtMask;
        internal string ArtOpacity;
        internal string Icon;
        internal string MeshFileName;

        internal string Art2, ArtNormal2, ArtMask2;
        internal UnityEngine.Texture2D Tex2, TexNormal2, TexMask2;

        internal string MeshTarget;

        internal bool KeepHead;
        internal Kind Type = Kind.Patch;
        internal string Pack;

        internal bool AtlasPatch;

        internal bool ClipPatch;

        internal float PerScale = 1f;
        internal float PerOutMm = 0f;

        internal UnityEngine.Vector3 BoxSize, BoxCentre;
        internal int WA, HA, TA;
        internal bool TallDonor;
        internal UnityEngine.Mesh GenMesh;
        internal bool[,] GridFill;
        internal float ArtAspect;

        internal string DonorContains;

        internal int Index = -1;
        internal UnityEngine.Texture2D Tex;
        internal UnityEngine.Texture2D TexNormal;
        internal UnityEngine.Texture2D TexMask;
        internal UnityEngine.Texture2D TexOpacity;
        internal UnityEngine.Texture2D TexIcon;

        internal int DonorIndex = -1;
        internal UnityEngine.GameObject Clone;
        internal bool MaterialFinal;

        internal UnityEngine.Material OwnMat;
        internal UnityEngine.Material[] OwnMats;

        internal UnityEngine.Material[][] OwnSlots;
        internal bool ShaderWarned;

        internal string[] OwnShaderNames;
        internal bool ShaderDeadLogged;
    }

    internal enum Kind
    {

        Patch,

        Garment,

        Camo,
    }

    internal static class Defs
    {
        internal static readonly List<Def> All = new List<Def>();

        internal static readonly HashSet<string> Names =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal static void Load(string contentDir)
        {
            All.Clear();

            byte[] mbytes = Pack.ReadBytes("manifest.json");
            if (mbytes == null)
            {
                NewSlotsMod.Log("no sealed manifest in content.odgpak - nothing to register.");
                return;
            }

            string json;
            try { json = System.Text.Encoding.UTF8.GetString(mbytes); }
            catch (Exception e) { NewSlotsMod.Log("manifest unreadable: " + e.Message); return; }

            int ok = 0, bad = 0;
            foreach (var obj in Objects(json))
            {
                var d = new Def
                {
                    ModName = Field(obj, "name"),
                    Display = Field(obj, "display"),
                    Art = Field(obj, "base"),
                    ArtNormal = Field(obj, "normal"),
                    ArtMask = Field(obj, "mask"),
                    ArtOpacity = Field(obj, "opacity"),
                    Icon = Field(obj, "icon"),
                    MeshFileName = Field(obj, "mesh"),
                    Art2 = Field(obj, "base2"),
                    ArtNormal2 = Field(obj, "normal2"),
                    ArtMask2 = Field(obj, "mask2"),
                    MeshTarget = Field(obj, "meshTarget"),
                    KeepHead = Field(obj, "keepHead") == "1",
                    Pack = Field(obj, "pack"),
                    DonorContains = Field(obj, "donor"),
                };
                string kind = Field(obj, "kind");
                d.Type = kind == "Garment" ? Kind.Garment : kind == "Camo" ? Kind.Camo : Kind.Patch;
                d.AtlasPatch = Field(obj, "atlas") == "1";
                d.ClipPatch = Field(obj, "clip") == "1";

                if (string.IsNullOrEmpty(d.ModName) || string.IsNullOrEmpty(d.DonorContains)) { bad++; continue; }
                All.Add(d);
                ok++;
            }
            NewSlotsMod.Log("manifest: " + ok + " item(s) loaded" + (bad > 0 ? ", " + bad + " skipped as malformed" : ""));

            Names.Clear();
            foreach (var d in All)
                if (!string.IsNullOrEmpty(d.ModName)) Names.Add(d.ModName);

            Tuning.Load();
            foreach (var d in All)
            {
                if (d.Type != Kind.Patch) continue;
                var t = Tuning.Get(null, null, d.ModName);
                d.PerScale = t.Scale;
                d.PerOutMm = t.OutMm;
            }
        }

        static IEnumerable<string> Objects(string json)
        {
            int i = json.IndexOf('[');
            if (i < 0) yield break;
            int depth = 0, start = -1;
            bool inStr = false, esc = false;
            for (; i < json.Length; i++)
            {
                char c = json[i];
                if (inStr)
                {
                    if (esc) esc = false;
                    else if (c == '\\') esc = true;
                    else if (c == '"') inStr = false;
                    continue;
                }
                if (c == '"') { inStr = true; continue; }
                if (c == '{') { if (depth++ == 0) start = i; }
                else if (c == '}') { if (--depth == 0 && start >= 0) { yield return json.Substring(start, i - start + 1); start = -1; } }
            }
        }

        static string Field(string obj, string key)
        {
            string needle = "\"" + key + "\"";
            int k = obj.IndexOf(needle, StringComparison.Ordinal);
            if (k < 0) return null;
            int colon = obj.IndexOf(':', k + needle.Length);
            if (colon < 0) return null;
            int q = obj.IndexOf('"', colon + 1);
            if (q < 0) return null;

            var sb = new StringBuilder();
            for (int i = q + 1; i < obj.Length; i++)
            {
                char c = obj[i];
                if (c == '\\' && i + 1 < obj.Length)
                {
                    char n = obj[++i];
                    sb.Append(n == 'n' ? '\n' : n == 't' ? '\t' : n);
                    continue;
                }
                if (c == '"') break;
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
