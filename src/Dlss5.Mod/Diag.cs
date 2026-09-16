// ODG Operations - Dlss5
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using Il2CppMichsky.MUIP;

namespace Dlss5
{

    internal static class Diag
    {
        internal static void Dump(string trigger)
        {
            var sb = new StringBuilder();
            void W(string s) { sb.Append(s).Append('\n'); }

            W("===== DLSS 5 diagnostics (" + trigger + ") " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " =====");
            try
            {
                var p = Dlss5Mod.P;
                W("prefs: Enabled=" + p.Enabled.Value + " ManageBridge=" + p.ManageBridge.Value + " WriteReShadeConfig=" + p.WriteReShadeConfig.Value
                  + " AssertAddonHotkey=" + p.AssertAddonHotkey.Value + " AddonToggleVk=" + p.AddonToggleVk.Value + " label=\"" + p.MenuLabel.Value + "\"");
                W("game dir: " + NeuralRendering.GameDir);
                W("graphics: " + SystemInfo.graphicsDeviceType + " | " + SystemInfo.graphicsDeviceName + " | " + SystemInfo.graphicsDeviceVersion
                  + " | vram " + SystemInfo.graphicsMemorySize + " MB | " + Screen.width + "x" + Screen.height);
                W("nvidia driver (registry): " + NvidiaDriver());
            }
            catch (Exception e) { W("header: " + e.Message); }

            try
            {
                string d = NeuralRendering.GameDir;
                W("-- files");
                foreach (var f in new[] { "dxgi.dll", "renodx-dlss5.addon64", "nvngx_dlssnr.dll", "dlss5-bridge.addon64", "dlss5-bridge.cfg",
                                          "ReShade.ini", "ReShade.log", "dlss5-bridge.log", "nvngx_dlss.dll", "version.dll",
                                          @"OPERATOR_Data\Plugins\x86_64\nvngx_dlss.dll", @"OPERATOR_Data\Plugins\x86_64\DLSSUnityPlugin.dll", "NVUnityPlugin.dll" })
                    W("  " + Describe(Path.Combine(d, f), f));
                W("  stack present: " + NeuralRendering.StackPresent + (NeuralRendering.StackPresent ? "" : "  missing: " + NeuralRendering.MissingSummary()));
            }
            catch (Exception e) { W("files: " + e.Message); }

            try
            {
                W("-- loaded modules of interest");
                var keys = new[] { "dxgi", "renodx", "dlss5", "nvngx", "_nvngx", "sl.interposer", "reshade", "version.dll", "nvunityplugin", "dlssunityplugin", "d3d11.dll", "d3d12.dll", "d3d12core" };
                foreach (ProcessModule m in Process.GetCurrentProcess().Modules)
                {
                    string n = m.ModuleName.ToLowerInvariant();
                    bool hit = false;
                    foreach (var k in keys) if (n.Contains(k)) { hit = true; break; }
                    if (!hit) continue;
                    string ver = "";
                    try { ver = m.FileVersionInfo.FileVersion ?? ""; } catch { }
                    W("  " + m.ModuleName + "  " + ver + "  " + m.FileName);
                }
                W("  ReShade API bound: " + ReShadeApi.Bound + (ReShadeApi.Bound ? " via " + ReShadeApi.ModuleName + " " + ReShadeApi.Version : ""));
            }
            catch (Exception e) { W("modules: " + e.Message); }

            try
            {
                W("-- game settings");
                W("  HDRP DLSSDetected=" + SafeStr(() => UnityEngine.Rendering.HighDefinition.HDDynamicResolutionPlatformCapabilities.DLSSDetected.ToString()));
                W("  saved CurrentUpscaler_2026=" + SafeStr(() => Il2Cpp.SaveManager.LoadSteamStorage<int>("CurrentUpscaler_2026", Il2Cpp.SaveManager.SaveFileType.Other, -99).ToString())
                  + "  upscalerQuality=" + SafeStr(() => Il2Cpp.SaveManager.LoadSteamStorage<int>("upscalerQuality", Il2Cpp.SaveManager.SaveFileType.PlayerSettings, -99).ToString())
                  + "  dlssPreset=" + SafeStr(() => Il2Cpp.SaveManager.LoadSteamStorage<int>("dlssPreset", Il2Cpp.SaveManager.SaveFileType.Other, -99).ToString())
                  + "  aaMode=" + SafeStr(() => Il2Cpp.SaveManager.LoadSteamStorage<int>("aaMode", Il2Cpp.SaveManager.SaveFileType.PlayerSettings, -99).ToString()));
                var dd = Menu.Dropdown();
                if (dd == null) W("  upscaler dropdown: PlayMenu.singleton or UpscalerSettings is null");
                else
                {
                    var items = dd.items;
                    var names = new List<string>();
                    if (items != null) for (int i = 0; i < items.Count; i++) names.Add(i + ":" + (items[i] != null ? items[i].itemName : "null"));
                    string shown = "";
                    try { shown = dd.selectedText != null ? dd.selectedText.text : ""; } catch { }
                    W("  upscaler dropdown: selected=" + dd.selectedItemIndex + " shown=\"" + shown + "\" ours=" + Menu.IndexOf(dd) + " items=[" + string.Join(", ", names) + "]");
                }
                W("  ScalableBufferManager scale: " + SafeStr(() => ScalableBufferManager.widthScaleFactor.ToString("0.###") + " x " + ScalableBufferManager.heightScaleFactor.ToString("0.###")));
            }
            catch (Exception e) { W("settings: " + e.Message); }

            try
            {
                W("-- cameras");
                var cams = Camera.allCameras;
                if (cams == null || cams.Length == 0) W("  none");
                else foreach (var cam in cams)
                {
                    if (cam == null) continue;
                    var cs = cam.GetComponent<Il2Cpp.CameraSettings>();
                    if (cs == null) { W("  " + cam.name + ": no CameraSettings"); continue; }
                    string line = "  " + cam.name + ": scope=" + SafeStr(() => cs.isScopeCamera.ToString()) + " dynResActive=" + SafeStr(() => cs._dynResActive.ToString());
                    var hd = cs.cameraData;
                    if (hd != null) line += " aa=" + SafeStr(() => hd.antialiasing.ToString()) + " allowDynRes=" + SafeStr(() => hd.allowDynamicResolution.ToString());
                    var up = cs.TND_upscaler;
                    if (up == null) line += " TND=none";
                    else line += " TND selected=" + SafeStr(() => up.GetSelectedUpscaler().ToString()) + " active=" + SafeStr(() => up.GetActiveUpscaler().ToString())
                              + " quality=" + SafeStr(() => up.GetQuality().ToString()) + " scaling=" + SafeStr(() => up.GetScaling().ToString("0.##"));
                    W(line);
                }
                string why;
                W("  DlssRunning=" + GameState.DlssRunning(out why) + " (" + why + ")");
            }
            catch (Exception e) { W("cameras: " + e.Message); }

            try
            {
                W("-- add-on / bridge");
                W("  add-on NeuralUplift at load: " + (NeuralRendering.BootNeuralUplift ?? "(unset/unknown)") + "  hotkey presses: " + NeuralRendering.HotkeyPresses);
                if (ReShadeApi.Bound)
                    foreach (var k in new[] { "NeuralUplift", "EnableHooks", "NREnableUpscaling", "NRToggleKey", "NRStyle", "NRGlobalTone", "DLSSNR.Enabled" })
                        W("  [RenoDX.DLSS5] " + k + "=" + (ReShadeApi.Get(NeuralRendering.AddonSection, k) ?? "(unset)"));
                var lt = NeuralRendering.LastToggleInLog();
                W("  ReShade.log last NR toggle: " + (lt.HasValue ? (lt.Value ? "ON" : "OFF") : "(none logged)"));
                W("  bridge last written: " + (NeuralRendering.LastBridgeWritten.HasValue ? (NeuralRendering.LastBridgeWritten.Value ? "ON" : "OFF") : "(not yet)"));
                string cfg = NeuralRendering.ReadBridgeCfg();
                W("  dlss5-bridge.cfg: " + (cfg == null ? "(absent)" : cfg.Replace("\r", "").Replace("\n", " | ")));
            }
            catch (Exception e) { W("addon: " + e.Message); }

            try
            {
                foreach (var lf in new[] { "ReShade.log", "dlss5-bridge.log" })
                {
                    string p = Path.Combine(NeuralRendering.GameDir, lf);
                    W("-- " + lf + (File.Exists(p) ? " (last 25 lines)" : " (absent)"));
                    if (!File.Exists(p)) continue;
                    foreach (var l in NeuralRendering.Tail(p, 25)) if (l.Length > 0) W("  " + Trunc(l, 220));
                }
                string rs = Path.Combine(NeuralRendering.GameDir, "ReShade.log");
                if (File.Exists(rs))
                {
                    W("-- ReShade.log signal lines");
                    foreach (var l in NeuralRendering.Tail(rs, 4000, 1 << 20))
                        if (l.IndexOf("Registered add-on", StringComparison.OrdinalIgnoreCase) >= 0
                         || l.IndexOf("Failed to load add-on", StringComparison.OrdinalIgnoreCase) >= 0
                         || l.IndexOf("feature 18", StringComparison.OrdinalIgnoreCase) >= 0
                         || l.IndexOf("NR toggled", StringComparison.OrdinalIgnoreCase) >= 0
                         || l.IndexOf("0xBAD", StringComparison.OrdinalIgnoreCase) >= 0
                         || l.IndexOf("nvngx_dlssnr", StringComparison.OrdinalIgnoreCase) >= 0)
                            W("  " + Trunc(l, 220));
                }
            }
            catch (Exception e) { W("logs: " + e.Message); }

            W("===== end =====");

            string text = sb.ToString();
            foreach (var line in text.Split('\n')) if (line.Length > 0) Dlss5Mod.L.Msg(line);
            try
            {
                string outp = Path.Combine(NeuralRendering.GameDir, "Mods", "Dlss5_diag.txt");
                File.WriteAllText(outp, text);
                Dlss5Mod.L.Msg("diagnostics written to " + outp);
            }
            catch (Exception e) { Dlss5Mod.L.Warning("could not write Dlss5_diag.txt: " + e.Message); }
        }

        static string Describe(string path, string label)
        {
            try
            {
                if (!File.Exists(path)) return label + ": ABSENT";
                var fi = new FileInfo(path);
                string ver = "";
                try { var v = FileVersionInfo.GetVersionInfo(path); ver = (v.FileVersion ?? "") + (string.IsNullOrEmpty(v.ProductName) ? "" : " " + v.ProductName); } catch { }
                return label + ": " + fi.Length + " B  " + fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm") + "  " + ver;
            }
            catch (Exception e) { return label + ": " + e.Message; }
        }

        static string NvidiaDriver()
        {
            try
            {
                using (var cls = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                {
                    if (cls == null) return "(no display class key)";
                    foreach (var sub in cls.GetSubKeyNames())
                    {
                        using (var k = cls.OpenSubKey(sub))
                        {
                            if (k == null) continue;
                            string prov = k.GetValue("ProviderName") as string ?? "";
                            string ver = k.GetValue("DriverVersion") as string ?? "";
                            if (prov.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) < 0 || ver.Length == 0) continue;
                            var parts = ver.Split('.');
                            string digits = parts.Length == 4 ? parts[2] + parts[3] : ver.Replace(".", "");
                            string tail = digits.Length >= 5 ? digits.Substring(digits.Length - 5) : digits;
                            string pretty = tail.Length == 5 ? tail.Substring(0, 3) + "." + tail.Substring(3) : tail;
                            return pretty + " (" + ver + "; DLSS 5 needs 616.56 or newer)";
                        }
                    }
                }
                return "(no NVIDIA adapter key)";
            }
            catch (Exception e) { return "(" + e.Message + ")"; }
        }

        static string SafeStr(Func<string> f)
        {
            try { return f(); } catch (Exception e) { return "err:" + e.GetType().Name; }
        }

        static string Trunc(string s, int n) { return s.Length <= n ? s : s.Substring(0, n) + "..."; }
    }
}
