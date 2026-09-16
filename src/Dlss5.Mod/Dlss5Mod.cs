// ODG Operations - Dlss5
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using MelonLoader;
using UnityEngine;
using Il2CppTND.Upscaling.Framework;

[assembly: MelonInfo(typeof(Dlss5.Dlss5Mod), "ODG DLSS 5", "1.0.0", "Archduke")]
[assembly: MelonGame(null, null)]

namespace Dlss5
{

    public class Dlss5Mod : MelonMod
    {
        internal static MelonLogger.Instance L;
        internal static Prefs P;

        static bool _dirty = true;
        static float _nextTick;
        static int _pendingRelabel = -1;
        static bool _lastWant;
        static string _lastWhy = "";

        public override void OnInitializeMelon()
        {
            L = LoggerInstance;
            P = new Prefs();
            NeuralRendering.Init();
            Patches.Install(HarmonyInstance);
            L.Msg("DLSS 5 option ready. Remembered selection: " + (P.Enabled.Value ? "DLSS 5" : "not DLSS 5")
                + ". Stack: " + (NeuralRendering.StackPresent ? "present" : "missing " + NeuralRendering.MissingSummary())
                + ". " + P.DiagKey.Value + " writes a diagnostics dump.");
        }

        internal static void MarkDirty() { _dirty = true; }
        internal static void RelabelLater(int index) { _pendingRelabel = index; }

        public override void OnLateUpdate()
        {
            try
            {
                if (P.DiagKeyCode != KeyCode.None && Input.GetKeyDown(P.DiagKeyCode)) Diag.Dump("hotkey");

                if (_pendingRelabel >= 0)
                {
                    int i = _pendingRelabel;
                    _pendingRelabel = -1;
                    Menu.Relabel(i);
                }

                float now = Time.unscaledTime;
                if (!_dirty && now < _nextTick) return;
                _dirty = false;
                _nextTick = now + 1f;
                Reconcile();
            }
            catch (Exception e)
            {
                L.Error("OnLateUpdate: " + e);
            }
        }

        static void Reconcile()
        {
            string why;
            bool dlss = GameState.DlssRunning(out why);
            bool present = NeuralRendering.StackPresent;
            bool want = P.Enabled.Value && dlss && present;
            string reason = !P.Enabled.Value ? "DLSS 5 is not the selected upscaler"
                          : !present ? "stack missing: " + NeuralRendering.MissingSummary()
                          : why;
            if (want != _lastWant || (P.Verbose.Value && reason != _lastWhy))
            {
                L.Msg("neural rendering wanted=" + want + "  (" + reason + ")");
                _lastWant = want;
                _lastWhy = reason;
            }
            NeuralRendering.Apply(want, reason);
        }

        public override void OnApplicationQuit()
        {
            try { NeuralRendering.OnQuit(); } catch { }
        }
    }

    internal static class GameState
    {
        internal static bool DlssRunning(out string why)
        {
            why = "no live camera";
            Camera[] cams;
            try { cams = Camera.allCameras; }
            catch (Exception e) { why = "Camera.allCameras threw: " + e.Message; return false; }
            if (cams == null) return false;

            bool sawSettings = false;
            foreach (var cam in cams)
            {
                if (cam == null) continue;
                Il2Cpp.CameraSettings cs = null;
                try { cs = cam.GetComponent<Il2Cpp.CameraSettings>(); } catch { }
                if (cs == null) continue;
                sawSettings = true;
                try
                {
                    if (cs.isScopeCamera) continue;
                    var up = cs.TND_upscaler;
                    if (up == null) { why = cam.name + ": no TNDUpscaler"; continue; }
                    UpscalerName name = up.GetActiveUpscaler();
                    UpscalerQuality q = up.GetQuality();
                    why = cam.name + ": " + name + " / " + q;
                    if ((name == UpscalerName.DLSS4 || name == UpscalerName.DLSS3) && q != UpscalerQuality.Off)
                        return true;
                }
                catch (Exception e) { why = cam.name + ": " + e.Message; }
            }
            if (!sawSettings) why = "no CameraSettings on any live camera (menu?)";
            return false;
        }
    }

    internal sealed class Prefs
    {

        const int Rev = 1;

        readonly MelonPreferences_Category _cat;
        internal readonly MelonPreferences_Entry<int> Revision;
        internal readonly MelonPreferences_Entry<bool> Enabled;
        internal readonly MelonPreferences_Entry<string> MenuLabel;
        internal readonly MelonPreferences_Entry<string> DiagKey;
        internal readonly MelonPreferences_Entry<bool> ManageBridge;
        internal readonly MelonPreferences_Entry<bool> WriteReShadeConfig;
        internal readonly MelonPreferences_Entry<bool> AssertAddonHotkey;
        internal readonly MelonPreferences_Entry<int> AddonToggleVk;
        internal readonly MelonPreferences_Entry<bool> Verbose;
        internal KeyCode DiagKeyCode = KeyCode.F11;

        internal Prefs()
        {
            _cat = MelonPreferences.CreateCategory("Dlss5");
            Revision = _cat.CreateEntry("Revision", 0,
                description: "config revision, used for one-time migrations. Do not edit");
            Enabled = _cat.CreateEntry("Enabled", false,
                description: "True when DLSS 5 is the upscaler picked in Settings > Graphics. The menu writes this; "
                           + "editing it by hand is fine too.");
            MenuLabel = _cat.CreateEntry("MenuLabel", "DLSS 5",
                description: "Text of the entry appended to the upscaler dropdown.");
            DiagKey = _cat.CreateEntry("DiagKey", "F11",
                description: "Key that writes a full DLSS 5 diagnostics dump to the log and Mods\\Dlss5_diag.txt. "
                           + "Unity KeyCode name; None disables it.");
            ManageBridge = _cat.CreateEntry("ManageBridge", true,
                description: "Drive dlss5-bridge.cfg (stage/source) from the menu choice. Off leaves the bridge "
                           + "to its own file.");
            WriteReShadeConfig = _cat.CreateEntry("WriteReShadeConfig", true,
                description: "Keep [RenoDX.DLSS5] NeuralUplift=1 / EnableHooks=2 / NREnableUpscaling=0 and the "
                           + "toggle hotkey in ReShade.ini so the add-on starts armed.");
            AssertAddonHotkey = _cat.CreateEntry("AssertAddonHotkey", true,
                description: "If ReShade.log shows the add-on toggled OFF while DLSS 5 is selected, press its "
                           + "toggle key (AddonToggleVk) to turn it back on, at most three times per session.");
            AddonToggleVk = _cat.CreateEntry("AddonToggleVk", 124,
                description: "Windows virtual-key code written to [RenoDX.DLSS5] NRToggleKey and pressed by "
                           + "AssertAddonHotkey. 124 = F13, a key no keyboard or game bind uses.");
            Verbose = _cat.CreateEntry("Verbose", false,
                description: "Log every reconcile decision, not only the transitions.");

            if (Revision.Value < Rev) Revision.Value = Rev;

            DiagKeyCode = ParseKey(DiagKey.Value, KeyCode.F11);
        }

        internal void Save()
        {
            try { MelonPreferences.Save(); } catch { }
        }

        static KeyCode ParseKey(string s, KeyCode fallback)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            try { return (KeyCode)Enum.Parse(typeof(KeyCode), s.Trim(), true); }
            catch { return fallback; }
        }
    }
}
