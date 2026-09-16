// ODG Operations - Dlss5
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace Dlss5
{

    internal static class NeuralRendering
    {
        internal const string ReShadeProxy = "dxgi.dll";
        internal const string Addon = "renodx-dlss5.addon64";
        internal const string NrDll = "nvngx_dlssnr.dll";
        internal const string Bridge = "dlss5-bridge.addon64";
        internal const string BridgeCfg = "dlss5-bridge.cfg";
        internal const string BridgeLog = "dlss5-bridge.log";
        internal const string ReShadeIni = "ReShade.ini";
        internal const string ReShadeLog = "ReShade.log";
        internal const string AddonSection = "RenoDX.DLSS5";

        internal static string GameDir = "";

        static float _nextPresenceCheck = -1f;
        static bool _present;
        static string _missing = "";

        static bool? _bridgeWritten;
        static float _nextBridgeVerify;
        static bool _armed;
        static string _bootNeuralUplift;
        static int _hotkeyPresses;
        static float _nextHotkey;
        static long _rsLogLen = -1;
        static bool? _rsLogToggle;

        internal static void Init()
        {
            try { GameDir = MelonLoader.Utils.MelonEnvironment.GameRootDirectory; }
            catch { GameDir = AppContext.BaseDirectory; }
            CheckPresence(true);
        }

        internal static bool StackPresent
        {
            get
            {
                if (Time.unscaledTime >= _nextPresenceCheck) CheckPresence(false);
                return _present;
            }
        }

        internal static string MissingSummary() { return _missing; }
        internal static string BootNeuralUplift { get { return _bootNeuralUplift; } }
        internal static bool? LastBridgeWritten { get { return _bridgeWritten; } }
        internal static int HotkeyPresses { get { return _hotkeyPresses; } }

        internal static bool IsD3D12
        {
            get
            {
                try { return SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Direct3D12; }
                catch { return false; }
            }
        }

        static void CheckPresence(bool log)
        {
            _nextPresenceCheck = Time.unscaledTime + 10f;
            var missing = new List<string>();
            if (!IsReShade(Path.Combine(GameDir, ReShadeProxy))) missing.Add(ReShadeProxy + " (ReShade)");
            if (!File.Exists(Path.Combine(GameDir, Addon))) missing.Add(Addon);
            if (!File.Exists(Path.Combine(GameDir, NrDll))) missing.Add(NrDll);
            if (!IsD3D12 && !File.Exists(Path.Combine(GameDir, Bridge))) missing.Add(Bridge + " (needed on Direct3D 11)");
            _present = missing.Count == 0;
            _missing = string.Join(", ", missing);
            if (log)
                Dlss5Mod.L.Msg(_present ? "DLSS 5 stack present in " + GameDir
                                        : "DLSS 5 stack incomplete in " + GameDir + ": missing " + _missing);
        }

        internal static bool IsReShade(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                var v = FileVersionInfo.GetVersionInfo(path);
                return (v.ProductName ?? "").IndexOf("ReShade", StringComparison.OrdinalIgnoreCase) >= 0
                    || (v.FileDescription ?? "").IndexOf("ReShade", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return File.Exists(path); }
        }

        internal static void Apply(bool on, string why)
        {
            if (!_present) return;
            ArmAddonOnce();
            bool bridge = File.Exists(Path.Combine(GameDir, Bridge));
            if (bridge && Dlss5Mod.P.ManageBridge.Value) WriteBridge(on, why);
            if (!Dlss5Mod.P.AssertAddonHotkey.Value) return;
            if (bridge) { if (on) AssertAddon(true); }
            else AssertAddon(on);
        }

        internal static void OnQuit()
        {

        }

        static void WriteBridge(bool on, string why)
        {
            if (!File.Exists(Path.Combine(GameDir, Bridge))) return;
            float now = Time.unscaledTime;

            if (_bridgeWritten.HasValue && _bridgeWritten.Value == on && now < _nextBridgeVerify) return;
            _nextBridgeVerify = now + 15f;

            string path = Path.Combine(GameDir, BridgeCfg);
            string existing = null;
            try { if (File.Exists(path)) existing = File.ReadAllText(path); } catch { }

            string text = Rewrite(existing, on);
            if (existing != null && existing == text)
            {
                _bridgeWritten = on;
                return;
            }
            try
            {
                File.WriteAllText(path, text);
                _bridgeWritten = on;
                Dlss5Mod.L.Msg("[bridge] " + (on ? "ON  (stage=3, source=auto)" : "OFF (stage=0, source=off)") + "  " + why);
            }
            catch (Exception e)
            {
                Dlss5Mod.L.Warning("[bridge] could not write " + BridgeCfg + ": " + e.Message);
            }
        }

        internal static string Rewrite(string existing, bool on)
        {
            var outp = new List<string>();
            bool sawStage = false, sawSource = false;
            if (!string.IsNullOrEmpty(existing))
            {
                foreach (var raw in existing.Replace("\r\n", "\n").Split('\n'))
                {
                    string t = raw.TrimStart();
                    if (t.StartsWith("stage=", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!sawStage) { outp.Add("stage=" + (on ? 3 : 0)); sawStage = true; }
                        continue;
                    }
                    if (t.StartsWith("source=", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!sawSource) { outp.Add("source=" + (on ? "auto" : "off")); sawSource = true; }
                        continue;
                    }
                    outp.Add(raw);
                }
                while (outp.Count > 0 && outp[outp.Count - 1].Length == 0) outp.RemoveAt(outp.Count - 1);
            }
            if (!sawStage) outp.Add("stage=" + (on ? 3 : 0));
            if (!sawSource) outp.Add("source=" + (on ? "auto" : "off"));
            return string.Join("\n", outp) + "\n";
        }

        internal static string ReadBridgeCfg()
        {
            try
            {
                string p = Path.Combine(GameDir, BridgeCfg);
                return File.Exists(p) ? File.ReadAllText(p) : null;
            }
            catch { return null; }
        }

        static void ArmAddonOnce()
        {
            if (_armed) return;
            _armed = true;
            if (!Dlss5Mod.P.WriteReShadeConfig.Value) return;

            string vk = Dlss5Mod.P.AddonToggleVk.Value + ",0,0,0";
            if (ReShadeApi.TryBind())
            {
                _bootNeuralUplift = ReShadeApi.Get(AddonSection, "NeuralUplift");
                ReShadeApi.Set(AddonSection, "NeuralUplift", "1");
                ReShadeApi.Set(AddonSection, "EnableHooks", "2");
                ReShadeApi.Set(AddonSection, "NREnableUpscaling", "0");
                ReShadeApi.Set(AddonSection, "NRToggleKey", vk);
                Dlss5Mod.L.Msg("[addon] armed through ReShade " + ReShadeApi.Version + " (" + ReShadeApi.ModuleName
                    + "): NeuralUplift was " + (_bootNeuralUplift ?? "(unset)") + " at load, now 1");
                if (_bootNeuralUplift != "1")
                    Dlss5Mod.L.Warning("[addon] it loaded with NeuralUplift=" + (_bootNeuralUplift ?? "(unset)")
                        + "; neural rendering may stay off until the hotkey assert flips it or the next launch");
                ReShadeApi.Log("ODG DLSS 5 mod: add-on armed (NeuralUplift=1, EnableHooks=2, NRToggleKey=" + vk + ")");
            }
            else
            {

                string ini = Path.Combine(GameDir, ReShadeIni);
                try
                {
                    var doc = Ini.Load(ini);
                    _bootNeuralUplift = doc.Get(AddonSection, "NeuralUplift");
                    doc.Set(AddonSection, "NeuralUplift", "1");
                    doc.Set(AddonSection, "EnableHooks", "2");
                    doc.Set(AddonSection, "NREnableUpscaling", "0");
                    doc.Set(AddonSection, "NRToggleKey", vk);
                    doc.Save(ini);
                    Dlss5Mod.L.Msg("[addon] ReShade is not loaded in this process; seeded " + ReShadeIni + " for the next launch");
                }
                catch (Exception e) { Dlss5Mod.L.Warning("[addon] could not seed " + ReShadeIni + ": " + e.Message); }
            }
        }

        static bool? _assertWant;

        static void AssertAddon(bool want)
        {
            if (!ReShadeApi.Bound) return;
            if (!_assertWant.HasValue || _assertWant.Value != want) { _assertWant = want; _hotkeyPresses = 0; }
            float now = Time.unscaledTime;
            if (now < _nextHotkey) return;
            _nextHotkey = now + 3f;

            bool? logged = LastToggleInLog();
            bool addonOn = logged.HasValue ? logged.Value : (_bootNeuralUplift == "1");
            if (addonOn == want) return;

            if (_hotkeyPresses >= 3)
            {
                if (_hotkeyPresses == 3)
                {
                    _hotkeyPresses++;
                    Dlss5Mod.L.Warning("[addon] still reports " + (addonOn ? "ON" : "OFF") + " after 3 hotkey presses; giving up. "
                        + "Open the ReShade overlay (Home) > Add-ons and set the DLSS 5 Neural Rendering box by hand.");
                }
                return;
            }
            _hotkeyPresses++;
            ushort vk = (ushort)Dlss5Mod.P.AddonToggleVk.Value;
            Native.SendKey(vk);
            Dlss5Mod.L.Msg("[addon] pressed VK " + vk + " to turn neural rendering " + (want ? "on" : "off") + " (" + _hotkeyPresses + "/3; "
                + (logged.HasValue ? "log said " + (logged.Value ? "ON" : "OFF") : "loaded with NeuralUplift=" + (_bootNeuralUplift ?? "(unset)")) + ")");
        }

        internal static bool? LastToggleInLog()
        {
            string p = Path.Combine(GameDir, ReShadeLog);
            try
            {
                if (!File.Exists(p)) return null;
                long len = new FileInfo(p).Length;
                if (len == _rsLogLen) return _rsLogToggle;
                _rsLogLen = len;
                _rsLogToggle = null;
                var lines = Tail(p, 600);
                for (int i = lines.Length - 1; i >= 0; i--)
                {
                    if (lines[i].IndexOf("NR toggled ON", StringComparison.OrdinalIgnoreCase) >= 0) { _rsLogToggle = true; break; }
                    if (lines[i].IndexOf("NR toggled OFF", StringComparison.OrdinalIgnoreCase) >= 0) { _rsLogToggle = false; break; }
                }
                return _rsLogToggle;
            }
            catch { return null; }
        }

        internal static string[] Tail(string path, int lines, int maxBytes = 262144)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long take = Math.Min(fs.Length, maxBytes);
                    fs.Seek(fs.Length - take, SeekOrigin.Begin);
                    var buf = new byte[take];
                    int got = 0;
                    while (got < take) { int n = fs.Read(buf, got, (int)(take - got)); if (n <= 0) break; got += n; }
                    var all = Encoding.UTF8.GetString(buf, 0, got).Replace("\r\n", "\n").Split('\n');
                    int start = Math.Max(0, all.Length - lines);
                    var outp = new string[all.Length - start];
                    Array.Copy(all, start, outp, 0, outp.Length);
                    return outp;
                }
            }
            catch { return new string[0]; }
        }

        internal static void Popup(string text, bool red)
        {
            try
            {
                var hm = Il2Cpp.HintManager.singleton;
                if (hm == null) return;
                hm.SpawnPopup(text, red ? Il2Cpp.Popup.PopupType.RedWarning : Il2Cpp.Popup.PopupType.YellowWarning, true, false);
            }
            catch (Exception e) { Dlss5Mod.L.Warning("popup: " + e.Message); }
        }
    }

    internal static class ReShadeApi
    {
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr GetModuleHandleW(string name);
        [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern uint GetModuleFileNameW(IntPtr module, StringBuilder name, uint size);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate byte GetConfigValueFn(IntPtr module, IntPtr runtime, [MarshalAs(UnmanagedType.LPStr)] string section,
                                       [MarshalAs(UnmanagedType.LPStr)] string key, IntPtr value, ref UIntPtr size);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void SetConfigValueFn(IntPtr module, IntPtr runtime, [MarshalAs(UnmanagedType.LPStr)] string section,
                                       [MarshalAs(UnmanagedType.LPStr)] string key, [MarshalAs(UnmanagedType.LPStr)] string value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void LogMessageFn(IntPtr module, int level, [MarshalAs(UnmanagedType.LPStr)] string message);

        static readonly string[] Candidates = { "dxgi.dll", "d3d11.dll", "d3d12.dll", "ReShade64.dll", "d3d9.dll", "opengl32.dll", "d3d10.dll" };

        static IntPtr _module;
        static GetConfigValueFn _get;
        static SetConfigValueFn _set;
        static LogMessageFn _log;
        static bool _tried;

        internal static bool Bound { get { return _module != IntPtr.Zero; } }
        internal static string ModuleName = "";
        internal static string ModulePath = "";
        internal static string Version = "";

        internal static bool TryBind()
        {
            if (Bound) return true;
            if (_tried) return false;
            _tried = true;
            try
            {
                foreach (var name in Candidates)
                {
                    IntPtr h = GetModuleHandleW(name);
                    if (h == IntPtr.Zero) continue;
                    if (GetProcAddress(h, "ReShadeRegisterAddon") == IntPtr.Zero) continue;
                    IntPtr g = GetProcAddress(h, "ReShadeGetConfigValue");
                    IntPtr s = GetProcAddress(h, "ReShadeSetConfigValue");
                    IntPtr l = GetProcAddress(h, "ReShadeLogMessage");
                    if (g == IntPtr.Zero || s == IntPtr.Zero) continue;
                    _get = Marshal.GetDelegateForFunctionPointer<GetConfigValueFn>(g);
                    _set = Marshal.GetDelegateForFunctionPointer<SetConfigValueFn>(s);
                    _log = l != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer<LogMessageFn>(l) : null;
                    _module = h;
                    ModuleName = name;
                    var sb = new StringBuilder(1024);
                    if (GetModuleFileNameW(h, sb, 1024) > 0) ModulePath = sb.ToString();
                    try { Version = FileVersionInfo.GetVersionInfo(ModulePath).FileVersion ?? ""; } catch { }
                    return true;
                }
            }
            catch (Exception e) { Dlss5Mod.L.Warning("ReShade bind: " + e.Message); }
            return false;
        }

        static IntPtr Self { get { return GetModuleHandleW(null); } }

        internal static string Get(string section, string key)
        {
            if (!Bound) return null;
            IntPtr buf = Marshal.AllocHGlobal(1024);
            try
            {
                UIntPtr size = (UIntPtr)1024;
                Marshal.WriteByte(buf, 0);
                byte ok = _get(Self, IntPtr.Zero, section, key, buf, ref size);
                if (ok == 0) return null;
                return Marshal.PtrToStringAnsi(buf);
            }
            catch (Exception e) { Dlss5Mod.L.Warning("ReShadeGetConfigValue " + section + "/" + key + ": " + e.Message); return null; }
            finally { Marshal.FreeHGlobal(buf); }
        }

        internal static void Set(string section, string key, string value)
        {
            if (!Bound) return;
            try { _set(Self, IntPtr.Zero, section, key, value); }
            catch (Exception e) { Dlss5Mod.L.Warning("ReShadeSetConfigValue " + section + "/" + key + ": " + e.Message); }
        }

        internal static void Log(string text)
        {
            if (!Bound || _log == null) return;
            try { _log(Self, 3, text); } catch { }
        }
    }

    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Explicit)]
        struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public uint type; public INPUTUNION u; }

        [DllImport("user32", SetLastError = true)]
        static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [DllImport("user32")]
        static extern uint MapVirtualKeyW(uint code, uint mapType);

        internal static void SendKey(ushort vk)
        {
            try
            {
                ushort scan = (ushort)MapVirtualKeyW(vk, 0);
                var down = new INPUT { type = 1 };
                down.u.ki = new KEYBDINPUT { wVk = vk, wScan = scan };
                var up = new INPUT { type = 1 };
                up.u.ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = 2 };
                SendInput(2, new[] { down, up }, Marshal.SizeOf(typeof(INPUT)));
            }
            catch (Exception e) { Dlss5Mod.L.Warning("SendInput: " + e.Message); }
        }
    }

    internal sealed class Ini
    {
        readonly List<string> _lines = new List<string>();

        internal static Ini Load(string path)
        {
            var ini = new Ini();
            if (File.Exists(path))
                ini._lines.AddRange(File.ReadAllText(path).Replace("\r\n", "\n").Split('\n'));
            while (ini._lines.Count > 0 && ini._lines[ini._lines.Count - 1].Length == 0) ini._lines.RemoveAt(ini._lines.Count - 1);
            return ini;
        }

        internal string Get(string section, string key)
        {
            int s = SectionStart(section);
            if (s < 0) return null;
            for (int i = s + 1; i < _lines.Count && !_lines[i].TrimStart().StartsWith("["); i++)
            {
                int eq = _lines[i].IndexOf('=');
                if (eq > 0 && _lines[i].Substring(0, eq).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                    return _lines[i].Substring(eq + 1).Trim();
            }
            return null;
        }

        internal void Set(string section, string key, string value)
        {
            int s = SectionStart(section);
            if (s < 0)
            {
                if (_lines.Count > 0) _lines.Add("");
                _lines.Add("[" + section + "]");
                _lines.Add(key + "=" + value);
                return;
            }
            int end = s + 1;
            for (int i = s + 1; i < _lines.Count && !_lines[i].TrimStart().StartsWith("["); i++)
            {
                end = i + 1;
                int eq = _lines[i].IndexOf('=');
                if (eq > 0 && _lines[i].Substring(0, eq).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    _lines[i] = key + "=" + value;
                    return;
                }
            }

            while (end > s + 1 && _lines[end - 1].Length == 0) end--;
            _lines.Insert(end, key + "=" + value);
        }

        internal void Save(string path)
        {
            File.WriteAllText(path, string.Join("\n", _lines) + "\n");
        }

        int SectionStart(string section)
        {
            string want = "[" + section + "]";
            for (int i = 0; i < _lines.Count; i++)
                if (_lines[i].Trim().Equals(want, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }
    }
}
