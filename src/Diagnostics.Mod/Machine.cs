// ODG Operations - Diagnostics
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace ODGDiag
{

    internal static class Machine
    {
        const int RefreshDays = 14;
        static string _cached;

        static string CachePath { get { return Path.Combine(Folders.Root, "machine.txt"); } }

        public static string Facts()
        {
            try
            {
                if (_cached != null) return _cached;
                string path = CachePath;
                bool stale = true;
                if (File.Exists(path))
                {
                    _cached = File.ReadAllText(path);
                    stale = (DateTime.Now - File.GetLastWriteTime(path)).TotalDays > RefreshDays;
                }
                if (stale) RefreshInBackground();
                return _cached ?? "   (not probed yet -- it is collected in the background and "
                                + "will be in this report next time the game starts)";
            }
            catch { return "   (machine probe unavailable)"; }
        }

        static int _refreshing;
        public static void RefreshInBackground()
        {

            if (Interlocked.Exchange(ref _refreshing, 1) == 1) return;
            try
            {
                var t = new Thread(() =>
                {
                    try
                    {
                        var text = Probe();
                        try { File.WriteAllText(CachePath, text, new UTF8Encoding(false)); } catch { }
                        _cached = text;
                    }
                    catch { }
                    finally { Interlocked.Exchange(ref _refreshing, 0); }
                });
                t.IsBackground = true;
                t.Name = "ODGDiag machine probe";
                t.Start();
            }
            catch { Interlocked.Exchange(ref _refreshing, 0); }
        }

        static string Probe()
        {
            var sb = new StringBuilder();
            sb.AppendLine("   probed     " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));

            const string cls = @"HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
            for (int i = 0; i <= 1; i++)
            {
                var k = Shell.RegQuery(cls + "\\" + i.ToString("0000"));
                if (k == null) continue;
                var desc = Shell.RegValue(k, "DriverDesc");
                if (desc == null) continue;
                var ver  = Shell.RegValue(k, "DriverVersion");
                var date = Shell.RegValue(k, "DriverDate");
                sb.AppendLine("   gpu        " + desc);
                sb.AppendLine("   driver     " + (ver ?? "?") + NvidiaSuffix(desc, ver)
                            + "   dated " + (DriverDate(date) ?? "?"));
            }

            var bios = Shell.RegQuery(@"HKLM\HARDWARE\DESCRIPTION\System\BIOS");
            if (bios != null)
            {
                sb.AppendLine("   board      " + (Shell.RegValue(bios, "BaseBoardManufacturer") ?? "?")
                            + " " + (Shell.RegValue(bios, "BaseBoardProduct") ?? "?"));
                sb.AppendLine("   bios       " + (Shell.RegValue(bios, "BIOSVersion") ?? "?")
                            + "   released " + (Shell.RegValue(bios, "BIOSReleaseDate") ?? "?"));
            }

            var cpu = Shell.RegQuery(@"HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            if (cpu != null)
            {
                sb.AppendLine("   cpu id     " + (Shell.RegValue(cpu, "Identifier") ?? "?"));
                var cur  = Microcode(Shell.RegValue(cpu, "Update Revision"));
                var prev = Microcode(Shell.RegValue(cpu, "Previous Update Revision"));
                if (cur  != null) sb.AppendLine("   microcode  " + cur);
                if (prev != null) sb.AppendLine("   was        " + prev);
                var name = Shell.RegValue(cpu, "ProcessorNameString");
                if (IsRaptorLake(name))
                {
                    sb.AppendLine("   NOTE: this is a 13th/14th gen Intel part. Those degrade over time");
                    sb.AppendLine("         (Vmin shift) unless the BIOS carries microcode 0x12B or later.");
                    sb.AppendLine("         If the revision above is lower, the BIOS is out of date.");
                }
            }

            var gd = Shell.RegQuery(@"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
            if (gd != null)
            {
                var hw = Shell.RegValue(gd, "HwSchMode");
                var tl = Shell.RegValue(gd, "TdrLevel");
                var td = Shell.RegValue(gd, "TdrDelay");
                sb.AppendLine("   gpu sched  HwSchMode=" + (hw ?? "default")
                            + "  TdrLevel=" + (tl ?? "default")
                            + "  TdrDelay=" + (td ?? "default"));
            }

            const string ps =
                "Get-CimInstance Win32_PhysicalMemory | ForEach-Object { 'DIMM|' + $_.DeviceLocator + '|' + "
              + "[math]::Round($_.Capacity/1MB) + '|' + $_.Speed + '|' + $_.ConfiguredClockSpeed + '|' + $_.PartNumber }; "
              + "Get-CimInstance Win32_PageFileUsage | ForEach-Object { 'PAGE|' + $_.Name + '|' + $_.AllocatedBaseSize }";
            var wmi = Shell.Run("powershell",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + ps + "\"", 20000);
            bool anyDimm = false, xmp = false, anyPage = false;
            if (wmi != null)
            {
                foreach (var raw in wmi.Split('\n'))
                {
                    var line = raw.Trim();
                    var f = line.Split('|');
                    if (f.Length >= 6 && f[0] == "DIMM")
                    {
                        anyDimm = true;
                        sb.AppendLine("   memory     " + f[1] + "  " + f[2] + " MB  " + f[3]
                                    + " MHz (configured " + f[4] + ")  " + f[5].Trim());
                        int sp;
                        if (int.TryParse(f[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out sp) && sp > 4800)
                            xmp = true;
                    }
                    else if (f.Length >= 3 && f[0] == "PAGE")
                    {
                        anyPage = true;
                        sb.AppendLine("   page file  " + f[1] + "  " + f[2] + " MB");
                    }
                }
            }
            if (anyDimm && xmp)
                sb.AppendLine("   NOTE: memory is running above JEDEC, so an XMP/EXPO profile is on. "
                            + "Turning it off is the cheapest stability test there is.");
            if (wmi != null && !anyPage)
                sb.AppendLine("   NOTE: no page file is configured. Unity games crash without one.");

            return sb.ToString().TrimEnd();
        }

        static bool IsRaptorLake(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (name.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) < 0) return false;
            if (name.IndexOf("13th Gen", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (name.IndexOf("14th Gen", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return name.IndexOf("-13", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("-14", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static string Microcode(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return null;
            hex = hex.Trim();
            if (hex.Length < 8) return null;
            try
            {
                var b = new byte[hex.Length / 2];
                for (int i = 0; i < b.Length; i++)
                    b[i] = byte.Parse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                uint lo = BitConverter.ToUInt32(b, 0);
                string s = "0x" + lo.ToString("X");
                if (b.Length >= 8)
                {
                    uint hi = BitConverter.ToUInt32(b, 4);
                    s = "0x" + hi.ToString("X") + " (Intel) / 0x" + lo.ToString("X") + " (AMD)";
                }
                return s + "   [raw " + hex + "]";
            }
            catch { return null; }
        }

        static string DriverDate(string v)
        {
            if (string.IsNullOrEmpty(v)) return null;
            v = v.Trim();
            if (v.Length != 16) return v;
            try
            {
                var b = new byte[8];
                for (int i = 0; i < 8; i++)
                    b[i] = byte.Parse(v.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                long ft = BitConverter.ToInt64(b, 0);
                if (ft <= 0) return v;

                return DateTime.FromFileTime(ft).ToString("yyyy-MM-dd");
            }
            catch { return v; }
        }

        static string NvidiaSuffix(string desc, string ver)
        {
            try
            {
                if (desc == null || ver == null) return "";
                if (desc.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) < 0) return "";
                var p = ver.Split('.');
                if (p.Length != 4) return "";
                var n = p[2] + p[3];
                if (n.Length < 5) return "";
                n = n.Substring(n.Length - 5);
                return "  (NVIDIA " + n.Insert(n.Length - 2, ".") + ")";
            }
            catch { return ""; }
        }
    }
}
