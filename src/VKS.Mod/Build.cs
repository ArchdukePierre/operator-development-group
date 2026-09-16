// ODG Operations - VKS
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace VKS
{

    internal static class BaseBuild
    {
        const string MagSocket = "MOD_MAGAZINE";
        const string SightSocket = "MOD_SIGHT";

        internal static string Corrected(string shipped)
        {
            Il2Cpp.LoadoutManager lm = null;
            try { lm = Il2Cpp.LoadoutManager.singleton; } catch { }
            if (lm == null) return null;

            string mag = Pick(lm, "magazine", "VKS_Mag");
            string ammo = Pick(lm, "round", "338 Lupua AP");
            string mount = Pick(lm, "mount", "Geissele 34mm Mount");
            string cap = Pick(lm, "ring cap", "Geissele 34mm Cap");
            string scope = Pick(lm, "scope", "S&B PMII 5-25x56 BLK", "S&B PMII 5-25x56");

            if (mag == null || ammo == null || mount == null || cap == null)
            {
                VKSMod.Log("build: a required part is missing from the registry, keeping the shipped base build");
                return null;
            }

            var names = new List<string>();
            var paths = new List<string[]>();
            names.Add(mag); paths.Add(new[] { MagSocket });
            names.Add(ammo); paths.Add(new[] { MagSocket, "MOD_Ammo" });
            names.Add(mount); paths.Add(new[] { SightSocket });
            if (scope != null) { names.Add(scope); paths.Add(new[] { SightSocket, "MOD_Scope" }); }
            else VKSMod.Log("build: no PMII in the registry under either name, the mount goes on empty");
            names.Add(cap); paths.Add(new[] { SightSocket, "Rear Ring" });
            names.Add(cap); paths.Add(new[] { SightSocket, "Front Ring" });

            return Serialise(names, paths);
        }

        static string Pick(Il2Cpp.LoadoutManager lm, string role, params string[] candidates)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                int idx = -1;
                try { idx = lm.find_attachment(candidates[i]); } catch { }
                if (idx >= 0)
                {
                    VKSMod.Log("build: " + role + " = '" + candidates[i] + "' [" + idx + "]");
                    return candidates[i];
                }
            }
            VKSMod.Log("build: " + role + " - none of [" + string.Join(", ", candidates) + "] is in the registry");
            return null;
        }

        static string Serialise(List<string> names, List<string[]> paths)
        {
            var sb = new StringBuilder(512);
            sb.Append("{\"WeaponModName\":[");
            for (int i = 0; i < names.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(names[i]).Append('"');
            }
            sb.Append("],\"ModParentDirectory\":[");
            for (int i = 0; i < paths.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"path\":[");
                for (int k = 0; k < paths[i].Length; k++)
                {
                    if (k > 0) sb.Append(',');
                    sb.Append('"').Append(paths[i][k]).Append('"');
                }
                sb.Append("]}");
            }
            sb.Append("],\"WeaponModPosition\":[");
            for (int i = 0; i < names.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"x\":0.0,\"y\":0.0,\"z\":0.0}");
            }
            sb.Append("]}");
            return sb.ToString();
        }
    }
}
