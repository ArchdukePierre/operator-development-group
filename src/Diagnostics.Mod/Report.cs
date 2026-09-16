// ODG Operations - Diagnostics
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Text;
using MelonLoader;
using UnityEngine;

namespace ODGDiag
{

    internal static class Report
    {
        public static void Build(string previous)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("ODG OPERATIONS - DIAGNOSTIC REPORT");
                sb.AppendLine("Written " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine();

                var v = WinEvents.Last;
                if (v != null)
                {
                    sb.AppendLine("================================================================");
                    sb.AppendLine("  SHORT VERSION");
                    sb.AppendLine("================================================================");
                    sb.AppendLine("   " + v.OneLine());
                    sb.AppendLine();
                }

                sb.AppendLine("WHAT TO DO");
                sb.AppendLine("   Send this file to Archduke. If a crash dump folder is named");
                sb.AppendLine("   below, zip that folder and send it too. That is everything.");
                sb.AppendLine();
                sb.AppendLine("   If the game JUST crashed and you have not started it again");
                sb.AppendLine("   yet: start it once more first. This file is rebuilt at");
                sb.AppendLine("   startup, and that pass is what reads the crash.");
                sb.AppendLine();
                sb.AppendLine("   While playing: press the COMMA key ( , ) whenever something");
                sb.AppendLine("   looks wrong. It stamps that moment, and your position, into");
                sb.AppendLine("   the record.");
                sb.AppendLine();
                sb.AppendLine("================================================================");
                sb.AppendLine(previous ?? "");
                sb.AppendLine("================================================================");
                sb.AppendLine();
                sb.AppendLine("THIS MACHINE");
                sb.AppendLine("   gpu        " + Safe(() => SystemInfo.graphicsDeviceName));
                sb.AppendLine("   gpu driver " + Safe(() => SystemInfo.graphicsDeviceVersion));
                sb.AppendLine("   vram       " + Safe(() => SystemInfo.graphicsMemorySize + " MB"));
                sb.AppendLine("   cpu        " + Safe(() => SystemInfo.processorType)
                                              + " x" + Safe(() => SystemInfo.processorCount.ToString()));
                sb.AppendLine("   ram        " + Safe(() => SystemInfo.systemMemorySize + " MB"));
                sb.AppendLine("   os         " + Safe(() => SystemInfo.operatingSystem));
                sb.AppendLine("   unity      " + Safe(() => Application.unityVersion));
                sb.AppendLine("   pack       " + InstalledRelease());
                sb.AppendLine("   mods       " + LoadedMods());
                sb.AppendLine();

                sb.AppendLine("THIS MACHINE, IN MORE DETAIL");
                sb.AppendLine(Machine.Facts());
                sb.AppendLine();
                sb.AppendLine("IS THIS THE SAME PACK EVERYONE ELSE IS RUNNING?");
                sb.AppendLine(PackFingerprint.Describe());
                sb.AppendLine();
                sb.AppendLine("NEXT-CRASH DUMP");
                sb.AppendLine("   " + DumpArming.Status);
                sb.AppendLine();
                sb.AppendLine("WHERE THE FULL RECORDS ARE");
                sb.AppendLine("   " + Folders.Sessions);
                sb.AppendLine("   One file per session, named by date; the name says whether it");
                sb.AppendLine("   ended normally or crashed.");
                File.WriteAllText(Folders.Report, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        public static string InstalledRelease()
        {
            try
            {
                var f = Path.Combine(Folders.GameRoot(), "Mods", ".installed_release");
                return File.Exists(f) ? File.ReadAllText(f).Trim()
                                      : "<not stamped by the launcher>";
            }
            catch { return "<unreadable>"; }
        }

        public static string LoadedMods()
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var m in MelonMod.RegisteredMelons)
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(m.Info.Name).Append(' ').Append(m.Info.Version);
                }
                return sb.Length > 0 ? sb.ToString() : "<none>";
            }
            catch { return "<unreadable>"; }
        }

        public static string Safe(Func<string> f) { try { return f() ?? "?"; } catch { return "?"; } }

        public static string MapLoaderPrefs()
        {
            try
            {
                var f = Path.Combine(Folders.GameRoot(), "UserData", "MelonPreferences.cfg");
                if (!File.Exists(f)) return "<no MelonPreferences.cfg>";
                bool inSection = false;
                var sb = new StringBuilder();
                foreach (var raw in File.ReadAllLines(f))
                {
                    var line = raw.Trim();
                    if (line.StartsWith("["))
                    {
                        inSection = line.Equals("[MapLoader]", StringComparison.OrdinalIgnoreCase);
                        continue;
                    }
                    if (!inSection || line.Length == 0) continue;
                    if (sb.Length > 0) sb.Append("  ");
                    sb.Append(line.Replace(" = ", "="));
                }
                return sb.Length > 0 ? sb.ToString() : "<[MapLoader] section empty>";
            }
            catch { return "<unreadable>"; }
        }
    }

    internal static class CrashDumps
    {
        public static string Describe(DateTime crashedAround)
        {
            try
            {
                string company = "VectorGames", product = "OPERATOR";
                try { if (!string.IsNullOrEmpty(Application.companyName)) company = Application.companyName; } catch { }
                try { if (!string.IsNullOrEmpty(Application.productName)) product = Application.productName; } catch { }

                string baseDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Temp", company, product, "Crashes");

                if (!Directory.Exists(baseDir))
                    return "Unity crash dump: none found (" + baseDir + ")";

                var dirs = Directory.GetDirectories(baseDir);
                if (dirs.Length == 0)
                    return "Unity crash dump: folder exists but is empty (" + baseDir + ")";

                string newest = null;
                DateTime newestAt = DateTime.MinValue;
                foreach (var d in dirs)
                {
                    var t = Directory.GetLastWriteTime(d);
                    if (t > newestAt) { newestAt = t; newest = d; }
                }

                var age = crashedAround - newestAt;
                bool matches = Math.Abs(age.TotalMinutes) < 10;

                var sb = new StringBuilder();
                if (matches)
                {
                    sb.AppendLine("UNITY CRASH DUMP FOUND, AND IT MATCHES THIS CRASH.");
                    sb.AppendLine("   " + newest);
                    sb.AppendLine("   Zip that whole folder and send it. The error.log inside");
                    sb.AppendLine("   names the module that actually faulted.");
                }
                else
                {
                    sb.AppendLine("Unity crash dump: newest is from "
                        + newestAt.ToString("yyyy-MM-dd HH:mm")
                        + ", which does NOT match this crash. The game died without");
                    sb.AppendLine("   writing one, which itself points at a hard kill: GPU driver");
                    sb.AppendLine("   reset, out of memory, or the process being terminated.");
                    sb.AppendLine("   (" + newest + ")");
                }
                return sb.ToString().TrimEnd();
            }
            catch (Exception e) { return "Unity crash dump: lookup failed (" + e.Message + ")"; }
        }
    }
}
