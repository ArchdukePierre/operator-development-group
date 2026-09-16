// ODG Operations - Dlss5
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppMichsky.MUIP;

namespace Dlss5
{

    internal static class Patches
    {
        static readonly HashSet<string> _fired = new HashSet<string>();
        static int _attempted, _bound;

        [ThreadStatic] static bool _suppress;
        internal static bool Suppressed { get { return _suppress; } }

        internal static void RunSuppressed(Action a)
        {
            bool prev = _suppress;
            _suppress = true;
            try { a(); }
            finally { _suppress = prev; }
        }

        internal static void Install(HarmonyLib.Harmony h)
        {
            Patch(h, typeof(Il2Cpp.PlayMenu), "SetSavedValues", null, nameof(Pre_SavedValues), nameof(Post_SavedValues));
            Patch(h, typeof(Il2Cpp.PlayMenu), "SetUpscaler", new[] { typeof(int) }, nameof(Pre_SetUpscaler), null);
            Patch(h, typeof(Il2Cpp.PlayMenu), "SetUpscalerInteractable", new[] { typeof(int) }, null, nameof(Post_Interactable));
            Patch(h, typeof(Il2Cpp.GameManager), "ApplyUpscalerSettings", null, null, nameof(Post_Apply));
            Patch(h, typeof(Il2Cpp.GameManager), "DisableUpscalers", null, null, nameof(Post_Disable));
            Patch(h, typeof(Il2Cpp.CameraSettings), "SetUpscalerSettings", null, null, nameof(Post_CamUpscaler));
            Dlss5Mod.L.Msg("patches: " + _bound + "/" + _attempted + " bound");
        }

        static void Pre_SavedValues()
        {
            Fired("PlayMenu.SetSavedValues");
            _suppress = true;
        }

        static void Post_SavedValues(Il2Cpp.PlayMenu __instance)
        {
            try { Menu.AfterSavedValues(__instance); }
            catch (Exception e) { Dlss5Mod.L.Error("AfterSavedValues: " + e); }
            finally { _suppress = false; }
            Dlss5Mod.MarkDirty();
        }

        static void Pre_SetUpscaler(Il2Cpp.PlayMenu __instance, ref int selection)
        {
            Fired("PlayMenu.SetUpscaler");
            try { Menu.OnSetUpscaler(__instance, ref selection); }
            catch (Exception e) { Dlss5Mod.L.Error("OnSetUpscaler: " + e); }
        }

        static void Post_Interactable(Il2Cpp.PlayMenu __instance, int upscalerSetting)
        {
            Fired("PlayMenu.SetUpscalerInteractable");
            Dlss5Mod.MarkDirty();
        }

        static void Post_Apply()
        {
            Fired("GameManager.ApplyUpscalerSettings");
            Dlss5Mod.MarkDirty();
        }

        static void Post_Disable()
        {
            Fired("GameManager.DisableUpscalers");
            if (Dlss5Mod.P.Enabled.Value)
            {
                Dlss5Mod.L.Msg("the game disabled upscalers (no DLSS on this machine); DLSS 5 selection cleared");
                Dlss5Mod.P.Enabled.Value = false;
                Dlss5Mod.P.Save();
            }
            Dlss5Mod.MarkDirty();
        }

        static void Post_CamUpscaler()
        {
            Fired("CameraSettings.SetUpscalerSettings");
            Dlss5Mod.MarkDirty();
        }

        static void Fired(string what)
        {
            if (_fired.Contains(what)) return;
            _fired.Add(what);
            Dlss5Mod.L.Msg("FIRED  " + what);
        }

        static void Patch(HarmonyLib.Harmony h, Type type, string method, Type[] args, string prefix, string postfix)
        {
            try
            {
                _attempted++;
                var mi = args == null ? AccessTools.Method(type, method) : AccessTools.Method(type, method, args);
                if (mi == null)
                {
                    Dlss5Mod.L.Warning("HOOK MISS  " + type.Name + "." + method);
                    return;
                }
                h.Patch(mi,
                    prefix == null ? null : new HarmonyMethod(typeof(Patches), prefix),
                    postfix == null ? null : new HarmonyMethod(typeof(Patches), postfix));
                _bound++;
                Dlss5Mod.L.Msg("hooked " + type.Name + "." + method);
            }
            catch (Exception e)
            {
                Dlss5Mod.L.Error("HOOK FAIL  " + type.Name + "." + method + ": " + e.Message);
            }
        }
    }

    internal static class Menu
    {
        internal const int Dlss4Index = 1;

        internal static CustomDropdown Dropdown()
        {
            try
            {
                var pm = Il2Cpp.PlayMenu.singleton;
                return pm != null ? pm.UpscalerSettings : null;
            }
            catch { return null; }
        }

        internal static int IndexOf(CustomDropdown dd)
        {
            if (dd == null) return -1;
            var items = dd.items;
            if (items == null) return -1;
            string label = Dlss5Mod.P.MenuLabel.Value;
            int n = items.Count;
            for (int i = 0; i < n; i++)
            {
                var it = items[i];
                if (it != null && string.Equals(it.itemName, label, StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        internal static void AfterSavedValues(Il2Cpp.PlayMenu pm)
        {
            var dd = pm != null ? pm.UpscalerSettings : null;
            if (dd == null)
            {
                Dlss5Mod.L.Warning("SetSavedValues ran but PlayMenu.UpscalerSettings is null");
                return;
            }

            int idx = IndexOf(dd);
            if (idx < 0)
            {
                int before = dd.items != null ? dd.items.Count : -1;

                dd.CreateNewItem(Dlss5Mod.P.MenuLabel.Value, true);
                idx = IndexOf(dd);
                Dlss5Mod.L.Msg("added \"" + Dlss5Mod.P.MenuLabel.Value + "\" to the upscaler dropdown at index "
                    + idx + " (it had " + before + " items)");
            }

            int sel = dd.selectedItemIndex;
            if (!Dlss5Mod.P.Enabled.Value) return;

            if (sel == Dlss4Index && idx >= 0)
            {

                dd.selectedItemIndex = idx;
                dd.SetupDropdown();
            }
            else if (sel != idx)
            {
                Dlss5Mod.L.Msg("DLSS 5 was remembered but the game's upscaler is index " + sel + "; forgetting it");
                Dlss5Mod.P.Enabled.Value = false;
                Dlss5Mod.P.Save();
            }
        }

        internal static void OnSetUpscaler(Il2Cpp.PlayMenu pm, ref int selection)
        {
            var dd = pm != null ? pm.UpscalerSettings : null;
            int idx = dd != null ? IndexOf(dd) : -1;
            bool suppressed = Patches.Suppressed;

            if (idx >= 0 && selection == idx)
            {
                if (!NeuralRendering.StackPresent)
                {
                    Dlss5Mod.L.Warning("DLSS 5 chosen but the stack is missing: " + NeuralRendering.MissingSummary());
                    NeuralRendering.Popup("DLSS 5 is not installed on this machine. See Mods\\DLSS_5.md for the one-step installer, then relaunch. Using DLSS 4 for now.", true);
                    if (!suppressed) { Dlss5Mod.P.Enabled.Value = false; Dlss5Mod.P.Save(); }
                    selection = Dlss4Index;
                    Dlss5Mod.RelabelLater(Dlss4Index);
                }
                else
                {
                    if (!suppressed && !Dlss5Mod.P.Enabled.Value) Dlss5Mod.L.Msg("DLSS 5 selected");
                    if (!suppressed) { Dlss5Mod.P.Enabled.Value = true; Dlss5Mod.P.Save(); }
                    selection = Dlss4Index;
                }
            }
            else if (!suppressed && Dlss5Mod.P.Enabled.Value)
            {
                Dlss5Mod.L.Msg("DLSS 5 deselected (upscaler index " + selection + " chosen)");
                Dlss5Mod.P.Enabled.Value = false;
                Dlss5Mod.P.Save();
            }
            Dlss5Mod.MarkDirty();
        }

        internal static void Relabel(int index)
        {
            var dd = Dropdown();
            if (dd == null) return;
            try
            {
                Patches.RunSuppressed(() =>
                {
                    dd.selectedItemIndex = index;
                    dd.SetupDropdown();
                });
            }
            catch (Exception e) { Dlss5Mod.L.Warning("relabel: " + e.Message); }
        }
    }
}
