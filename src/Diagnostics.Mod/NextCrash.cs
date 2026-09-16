// ODG Operations - Diagnostics
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace ODGDiag
{

    internal static class DumpArming
    {
        const string Key =
            @"HKCU\Software\Microsoft\Windows\Windows Error Reporting\LocalDumps\OPERATOR.exe";

        public static string Status { get; private set; } = "not set up";

        public static string DumpFolder
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CrashDumps");
            }
        }

        public static void Apply(bool enabled)
        {
            Status = "being set up";
            try
            {
                var t = new Thread(() => ApplyNow(enabled));
                t.IsBackground = true;
                t.Name = "ODGDiag dump arming";
                t.Start();
            }
            catch { ApplyNow(enabled); }
        }

        static void ApplyNow(bool enabled)
        {
            try
            {
                if (!enabled)
                {
                    Shell.Run("reg", "delete \"" + Key + "\" /f", 4000);
                    Status = "off (player opted out; no dumps will be written)";
                    return;
                }

                var folder = DumpFolder;
                try { Directory.CreateDirectory(folder); } catch { }

                Shell.Run("reg", "add \"" + Key + "\" /v DumpFolder /t REG_EXPAND_SZ /d \""
                                + folder + "\" /f", 4000);
                Shell.Run("reg", "add \"" + Key + "\" /v DumpCount /t REG_DWORD /d 3 /f", 4000);
                Shell.Run("reg", "add \"" + Key + "\" /v DumpType  /t REG_DWORD /d 1 /f", 4000);
                Status = "on -- the next crash writes a minidump to " + folder;
            }
            catch (Exception e) { Status = "could not be set up (" + e.Message + ")"; }
        }

        public static string Describe(DateTime crashedAround)
        {
            try
            {
                var dir = DumpFolder;
                if (!Directory.Exists(dir)) return null;

                string best = null; DateTime bestAt = DateTime.MinValue;
                foreach (var f in Directory.GetFiles(dir, "OPERATOR*.dmp"))
                {
                    var t = File.GetLastWriteTime(f);
                    if (t > bestAt) { bestAt = t; best = f; }
                }
                if (best == null) return null;
                if (Math.Abs((bestAt - crashedAround).TotalMinutes) > 10)
                    return "Windows minidump: newest is " + bestAt.ToString("yyyy-MM-dd HH:mm")
                         + ", which does not match this crash.";

                return "WINDOWS MINIDUMP FOR THIS CRASH:\n   " + best + "\n"
                     + "   ("+ (new FileInfo(best).Length / 1024) + " KB) Send this file too -- it names\n"
                     + "   the faulting module outright.";
            }
            catch { return null; }
        }
    }

    internal static class PackFingerprint
    {
        public static string Short { get; private set; } = "?";

        public static string Describe()
        {
            try
            {
                var dir = Path.Combine(Folders.GameRoot(), "Mods");
                if (!Directory.Exists(dir)) return "   Mods folder not found.";

                var files = new List<string>(Directory.GetFiles(dir, "*.dll"));
                files.Sort(StringComparer.OrdinalIgnoreCase);
                if (files.Count == 0) return "   no mod DLLs found.";

                var sb = new StringBuilder();
                var combined = new StringBuilder();
                foreach (var f in files)
                {
                    string h = HashOf(f);
                    combined.Append(Path.GetFileName(f)).Append(':').Append(h).Append('\n');
                    sb.AppendLine("   " + Path.GetFileName(f).PadRight(34) + " " + h.Substring(0, 12)
                                + "  " + (new FileInfo(f).Length / 1024) + " KB");
                }

                using (var sha = SHA256.Create())
                {
                    var all = sha.ComputeHash(Encoding.UTF8.GetBytes(combined.ToString()));
                    Short = Hex(all).Substring(0, 12);
                }
                sb.AppendLine();
                sb.AppendLine("   PACK FINGERPRINT: " + Short);
                sb.Append("   Two players with the same fingerprint are running identical mod code.");
                return sb.ToString();
            }
            catch (Exception e) { return "   fingerprint failed (" + e.Message + ")"; }
        }

        static string HashOf(string path)
        {
            try
            {
                using (var sha = SHA256.Create())
                using (var fs = File.OpenRead(path))
                    return Hex(sha.ComputeHash(fs));
            }
            catch { return "unreadable...."; }
        }

        static string Hex(byte[] b)
        {
            var sb = new StringBuilder(b.Length * 2);
            foreach (var x in b) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }
    }
}
