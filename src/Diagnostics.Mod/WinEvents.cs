// ODG Operations - Diagnostics
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Text;

namespace ODGDiag
{

    internal sealed class WinVerdict
    {
        public string FaultingModule;
        public string ExceptionCode;
        public bool   Tdr;
        public bool   Whea;
        public bool   MachineDied;
        public string Text = "";

        public string OneLine()
        {
            if (Whea)                        return "HARDWARE FAULT: Windows logged a machine-check error around the crash. That is the CPU or the RAM, not the game.";
            if (MachineDied)                 return "THE WHOLE PC WENT DOWN, not just the game. Power, heat, or hardware.";
            if (Tdr)                         return "THE GRAPHICS DRIVER RESET around the crash. That is driver-side.";
            if (FaultingModule != null)      return "Windows blamed: " + FaultingModule
                                                  + (ExceptionCode != null ? "  (" + ExceptionCode + ")" : "");
            return "Windows recorded nothing around the crash, so the process was killed without unwinding.";
        }
    }

    internal static class WinEvents
    {

        public static WinVerdict Last { get; private set; }

        public static WinVerdict Describe(DateTime diedAround)
        {
            var v = new WinVerdict();
            var sb = new StringBuilder();

            string app = Wevt("qe Application \"/q:*[System[(EventID=1000)]]\" /c:8 /rd:true /f:text");
            sb.AppendLine(SummariseCrashEvents(app, diedAround, v));

            string tdr = Wevt("qe System \"/q:*[System[(EventID=4101)]]\" /c:4 /rd:true /f:text");
            var tdrText = SummariseTdr(tdr, diedAround, v);
            if (tdrText.Length > 0) { sb.AppendLine(); sb.Append(tdrText); }

            string whea = Wevt("qe System \"/q:*[System[Provider[@Name='Microsoft-Windows-WHEA-Logger']]]\" /c:5 /rd:true /f:text");
            var wheaText = SummariseWhea(whea, diedAround, v);
            if (wheaText.Length > 0) { sb.AppendLine(); sb.AppendLine(); sb.Append(wheaText); }

            string kp = Wevt("qe System \"/q:*[System[(EventID=41 or EventID=1001)]]\" /c:5 /rd:true /f:text");
            var kpText = SummariseMachineDeath(kp, diedAround, v);
            if (kpText.Length > 0) { sb.AppendLine(); sb.AppendLine(); sb.Append(kpText); }

            v.Text = sb.ToString().TrimEnd();
            Last = v;
            return v;
        }

        static string Wevt(string args) { return Shell.Run("wevtutil", args, 5000); }

        static string SummariseCrashEvents(string text, DateTime diedAround, WinVerdict v)
        {

            if (text == null)
                return "Windows crash events: could not be read (wevtutil unavailable).";
            try
            {
                var blocks = text.Split(new[] { "Event[" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var b in blocks)
                {
                    if (b.IndexOf("OPERATOR.exe", StringComparison.OrdinalIgnoreCase) < 0) continue;

                    DateTime when;
                    bool timed = TryDate(b, out when);
                    if (timed && Math.Abs((when - diedAround).TotalMinutes) > 30) continue;

                    string module = LineAfter(b, "Faulting module name:");
                    string offset = LineAfter(b, "Fault offset:");
                    string code   = LineAfter(b, "Exception code:");

                    v.FaultingModule = module;
                    v.ExceptionCode  = code;

                    var sb = new StringBuilder();
                    sb.AppendLine("WINDOWS RECORDED THIS CRASH (Application Event 1000"
                        + (timed ? ", " + when.ToString("HH:mm:ss") : "") + "):");
                    sb.AppendLine("   faulting module: " + (module ?? "<not stated>"));
                    if (code   != null) sb.AppendLine("   exception code:  " + code);
                    if (offset != null) sb.AppendLine("   fault offset:    " + offset);
                    sb.AppendLine("   This names where it died even when no dump was written.");
                    sb.Append(Interpret(module));
                    return sb.ToString().TrimEnd();
                }
                return "Windows crash events: none for OPERATOR.exe near the death. Taken together "
                     + "with no dump, that points at a hard kill: GPU driver reset, out of memory, "
                     + "or the process being terminated.";
            }
            catch (Exception e) { return "Windows crash events: parse failed (" + e.Message + ")"; }
        }

        static string Interpret(string module)
        {
            if (string.IsNullOrEmpty(module)) return "";
            string m = module.ToLowerInvariant();
            if (m.StartsWith("nvwgf2") || m.StartsWith("nvd3d") || m.StartsWith("nvoglv") || m.StartsWith("nvldd"))
                return "   -> that is the NVIDIA display driver. Clean-install it (DDU) or roll back\n"
                     + "      to the version that was on the machine before this started.";
            if (m.StartsWith("amdxx") || m.StartsWith("atidx") || m.StartsWith("amdkm"))
                return "   -> that is the AMD display driver. Clean-install it or roll back.";
            if (m.StartsWith("gameassembly"))
                return "   -> that is the game's own compiled code. A mod can push it there, so the\n"
                     + "      next test is a run with the Mods folder renamed.";
            if (m.StartsWith("coreclr") || m.StartsWith("clrjit") || m.StartsWith("system.private"))
                return "   -> that is the mod runtime (.NET under MelonLoader). Suspect a mod first.";
            if (m.StartsWith("unityplayer"))
                return "   -> that is the engine itself.";
            if (m.StartsWith("ntdll") || m.StartsWith("kernelbase"))
                return "   -> that usually means memory corruption or an overlay injecting into the\n"
                     + "      game. Close RivaTuner/Afterburner/overlays and try once without them.";
            return "";
        }

        static string SummariseTdr(string text, DateTime diedAround, WinVerdict v)
        {
            if (string.IsNullOrEmpty(text)) return "";
            try
            {
                foreach (var b in text.Split(new[] { "Event[" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (b.IndexOf("Display", StringComparison.OrdinalIgnoreCase) < 0
                        && b.IndexOf("driver", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    DateTime when;
                    bool timed = TryDate(b, out when);
                    if (timed && Math.Abs((when - diedAround).TotalMinutes) > 30) continue;
                    v.Tdr = true;
                    return "GPU DRIVER RESET RECORDED (System Event 4101"
                         + (timed ? ", " + when.ToString("HH:mm:ss") : "")
                         + "): the display driver stopped responding and was reset. If this "
                         + "lines up with the death, the crash is driver-side, and the usual "
                         + "moves are a clean driver install and lower graphics load.";
                }
                return "";
            }
            catch { return ""; }
        }

        static string SummariseWhea(string text, DateTime diedAround, WinVerdict v)
        {
            if (string.IsNullOrEmpty(text)) return "";
            try
            {
                foreach (var b in text.Split(new[] { "Event[" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    DateTime when;
                    bool timed = TryDate(b, out when);
                    if (timed && Math.Abs((when - diedAround).TotalMinutes) > 30) continue;
                    v.Whea = true;
                    return "HARDWARE ERROR RECORDED (WHEA-Logger"
                         + (timed ? ", " + when.ToString("HH:mm:ss") : "")
                         + "): Windows logged a machine-check error, which means the CPU or the "
                         + "memory returned a wrong result. This is not a game problem and no "
                         + "reinstall will touch it. Update the BIOS, put the memory back to its "
                         + "default speed (XMP/EXPO off), and if it persists the part is failing.";
                }
                return "";
            }
            catch { return ""; }
        }

        static string SummariseMachineDeath(string text, DateTime diedAround, WinVerdict v)
        {
            if (string.IsNullOrEmpty(text)) return "";
            try
            {
                foreach (var b in text.Split(new[] { "Event[" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    DateTime when;
                    bool timed = TryDate(b, out when);
                    if (timed && Math.Abs((when - diedAround).TotalMinutes) > 30) continue;
                    v.MachineDied = true;
                    return "THE MACHINE ITSELF WENT DOWN (Kernel-Power 41 / bugcheck"
                         + (timed ? ", " + when.ToString("HH:mm:ss") : "")
                         + "): Windows did not shut down cleanly. The game did not crash so much "
                         + "as the PC stopped underneath it. Look at power, temperatures, and the "
                         + "memory profile before looking at anything in the game.";
                }
                return "";
            }
            catch { return ""; }
        }

        static bool TryDate(string block, out DateTime when)
        {
            when = default;
            var v = LineAfter(block, "Date:");
            return v != null && DateTime.TryParse(v.Trim().TrimEnd('Z'), out when);
        }

        static string LineAfter(string text, string label)
        {
            int i = text.IndexOf(label, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            i += label.Length;
            int end = text.IndexOfAny(new[] { '\r', '\n', ',' }, i);
            if (end < 0) end = text.Length;
            var v = text.Substring(i, end - i).Trim();
            return v.Length > 0 ? v : null;
        }
    }
}
