// ODG Operations - Diagnostics
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Diagnostics;
using System.Text;

namespace ODGDiag
{

    internal static class Shell
    {
        public static string Run(string exe, string args, int timeoutMs = 5000)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName               = exe,
                    Arguments              = args,
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true,
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) return null;
                    var sb = new StringBuilder();
                    p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                    p.ErrorDataReceived  += (s, e) => { };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return null; }
                    p.WaitForExit();
                    lock (sb) return sb.ToString();
                }
            }
            catch { return null; }
        }

        public static string RegQuery(string key, int timeoutMs = 4000)
        {
            return Run("reg", "query \"" + key + "\"", timeoutMs);
        }

        public static string RegValue(string regOutput, string name)
        {
            if (string.IsNullOrEmpty(regOutput)) return null;
            foreach (var raw in regOutput.Split('\n'))
            {
                var line = raw.Trim();
                if (!line.StartsWith(name, StringComparison.OrdinalIgnoreCase)) continue;

                int t = line.IndexOf("REG_", StringComparison.OrdinalIgnoreCase);
                if (t < 0) continue;
                int sp = line.IndexOf(' ', t);
                if (sp < 0) continue;
                var v = line.Substring(sp).Trim();
                return v.Length > 0 ? v : null;
            }
            return null;
        }
    }
}
