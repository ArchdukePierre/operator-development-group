// ODG Operations - LoadoutPresets
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace LoadoutPresets
{

    internal static class Instr
    {
        static int _seq;
        static Dictionary<string, string> _snap;

        internal static void Install(HarmonyLib.Harmony h)
        {
            var C  = typeof(Il2Cpp.CharacterCustomisationController);
            var CC = typeof(Il2Cpp.CharacterCustomisation);
            var CM = typeof(Il2Cpp.CharacterMod);
            var UI = typeof(Il2Cpp.CharacterModUI);

            PrePost(h, C, "OnSelectedMod",           nameof(Pre_OnSelectedMod),   nameof(Post_OnSelectedMod));
            PrePost(h, C, "OnSelected",              nameof(Pre_OnSelected),      nameof(Post_OnSelected));
            PrePost(h, C, "Start_Selection",         nameof(Pre_StartSel),        nameof(Post_Bracket));
            PrePost(h, C, "Start_Selection_Modding", nameof(Pre_StartSelModding), nameof(Post_Bracket));
            PrePost(h, C, "StartItemModding",        nameof(Pre_StartItemModding),nameof(Post_Bracket));
            PrePost(h, C, "CancelSelection",         nameof(Pre_Cancel),          nameof(Post_Cancel));

            Post(h, C,  "RemoveMod",           nameof(Post_CtrlRemoveMod));
            Post(h, CC, "SpawnMod",            nameof(Post_SpawnMod));
            Post(h, CC, "RemoveMod",           nameof(Post_CC_RemoveMod));
            Post(h, CC, "GetRig",              nameof(Post_GetRig));
            Post(h, CC, "AssignParents",       nameof(Post_AssignParents));
            Post(h, CC, "ChangeSkin",          nameof(Post_ChangeSkin));
            Post(h, CM, "Initialize",          nameof(Post_Initialize));
            Post(h, CM, "ActuallySetMaterial", nameof(Post_ActuallySetMaterial));
            Post(h, CM, "ApplyColorModifier",  nameof(Post_ApplyColorModifier));
            Post(h, UI, "OnSelected",          nameof(Post_UI_OnSelected));
            Post(h, UI, "OnSelectedModParent", nameof(Post_UI_OnSelectedModParent));

            LoadoutPresetsMod.Log("[instr] equip-flow instrumentation installed");
        }

        static void PrePost(HarmonyLib.Harmony h, Type t, string method, string pre, string post)
        {
            try
            {
                var mi = AccessTools.Method(t, method);
                if (mi == null) { LoadoutPresetsMod.Log("[instr] MISS " + t.Name + "." + method); return; }
                h.Patch(mi,
                    prefix:  pre  != null ? new HarmonyMethod(typeof(Instr), pre)  : null,
                    postfix: post != null ? new HarmonyMethod(typeof(Instr), post) : null);
            }
            catch (Exception e) { LoadoutPresetsMod.Log("[instr] FAIL " + t.Name + "." + method + ": " + e.Message); }
        }
        static void Post(HarmonyLib.Harmony h, Type t, string method, string post) => PrePost(h, t, method, null, post);

        static string Ctx(Il2Cpp.CharacterCustomisationController c)
        {
            try
            {
                string slot = "?"; try { slot = c.currentModdingSlot.ToString(); } catch { }
                var mp = c.currentModdingParent;
                string pn = "null"; string cm = "null";
                if (mp != null)
                {
                    try { pn = mp.ModParentName ?? "?"; } catch { }
                    try { var cur = mp.currentMod; if (cur != null) { var mi = cur.ModInfo; cm = mi != null ? mi.ModName : "?"; } } catch { cm = "(AV)"; }
                }
                int sc = -1; try { var s = c.selectionChildren; sc = s != null ? s.Count : -1; } catch { }
                return "slot=" + slot + " parent=" + pn + " curMod=" + cm + " selKids=" + sc;
            }
            catch (Exception e) { return "ctx-err:" + e.Message; }
        }
        static void Enter(string m, string extra) { LoadoutPresetsMod.Log("#" + (++_seq) + " >> " + m + "   " + extra); }
        static void Exit(string m) { LoadoutPresetsMod.Log("#" + _seq + " << " + m); }
        static void Leaf(string m) { LoadoutPresetsMod.Log("#" + _seq + "      * " + m); }

        static void Pre_OnSelectedMod(Il2Cpp.CharacterCustomisationController __instance, string __0)
        { try { Enter("Ctrl.OnSelectedMod(" + __0 + ")", Ctx(__instance)); } catch { } }
        static void Post_OnSelectedMod(Il2Cpp.CharacterCustomisationController __instance, string __0)
        { try { Exit("Ctrl.OnSelectedMod"); LoadoutPresetsMod.Log("      after: " + Ctx(__instance)); Diff("OnSelectedMod"); } catch { } }

        static void Pre_OnSelected(Il2Cpp.CharacterCustomisationController __instance, string __0)
        { try { Enter("Ctrl.OnSelected(" + __0 + ")", Ctx(__instance)); } catch { } }
        static void Post_OnSelected(Il2Cpp.CharacterCustomisationController __instance, string __0)
        { try { Exit("Ctrl.OnSelected"); LoadoutPresetsMod.Log("      after: " + Ctx(__instance)); Diff("OnSelected"); } catch { } }

        static void Pre_StartSel(Il2Cpp.CharacterCustomisationController __instance, string __0)
        { try { Enter("Ctrl.Start_Selection(\"" + __0 + "\")", Ctx(__instance)); } catch { } }
        static void Pre_StartSelModding(Il2Cpp.CharacterCustomisationController __instance, Il2Cpp.CharacterModParent __0)
        { try { string n = "null"; try { n = __0 != null ? __0.ModParentName : "null"; } catch { } Enter("Ctrl.Start_Selection_Modding(parent=" + n + ")", Ctx(__instance)); } catch { } }
        static void Pre_StartItemModding(Il2Cpp.CharacterCustomisationController __instance, string __0)
        { try { Enter("Ctrl.StartItemModding(\"" + __0 + "\")", Ctx(__instance)); } catch { } }
        static void Pre_Cancel(Il2Cpp.CharacterCustomisationController __instance)
        { try { Enter("Ctrl.CancelSelection", Ctx(__instance)); } catch { } }
        static void Post_Cancel(Il2Cpp.CharacterCustomisationController __instance)
        { try { Exit("Ctrl.CancelSelection"); Diff("CancelSelection"); } catch { } }
        static void Post_Bracket(Il2Cpp.CharacterCustomisationController __instance)
        { try { Exit("(selection-open)"); LoadoutPresetsMod.Log("      after: " + Ctx(__instance)); } catch { } }

        static void Post_CtrlRemoveMod(Il2Cpp.CharacterCustomisationController __instance)
        { try { Leaf("Ctrl.RemoveMod()  " + Ctx(__instance)); Diff("Ctrl.RemoveMod"); } catch { } }

        static void Post_SpawnMod(Il2Cpp.CharacterCustomisation __instance, Il2Cpp.CharacterMod __0, Il2Cpp.CharacterModParent __1)
        {
            try
            {
                string nm = "?"; bool skinned = false;
                try { var mi = __0 != null ? __0.ModInfo : null; nm = mi != null ? mi.ModName : "?"; } catch { nm = "(AV)"; }
                try { skinned = __0 != null && __0.isSkinned; } catch { }
                string pn = "null"; try { pn = __1 != null ? __1.ModParentName : "null"; } catch { }
                string smr = "n/a";
                try
                {
                    var neu = __1 != null ? __1.currentMod : null;
                    if (neu != null) { var m = neu.skinnedMesh; if (m != null) smr = "SMR sharedMesh=" + (m.sharedMesh != null) + " rootBone=" + (m.rootBone != null) + " bones=" + (m.bones != null ? m.bones.Length : 0); }
                }
                catch (Exception e) { smr = "smr-err:" + e.Message; }
                Leaf("CC.SpawnMod(" + nm + " skinned=" + skinned + ") parent=" + pn + "  " + smr);
            }
            catch { }
        }
        static void Post_CC_RemoveMod(Il2Cpp.CharacterCustomisation __instance, Il2Cpp.CharacterCustomisation.ModdingSlot __0)
        { try { Leaf("CC.RemoveMod(slot=" + __0 + ")"); } catch { } }
        static void Post_GetRig(Il2Cpp.CharacterCustomisation __instance, UnityEngine.SkinnedMeshRenderer __0)
        { try { Leaf("CC.GetRig(smr=" + (__0 != null ? __0.name : "null") + ")   <-- SKINNED BIND"); } catch { } }
        static void Post_AssignParents(Il2Cpp.CharacterCustomisation __instance, Il2Cpp.CharacterMod __0, Il2Cpp.CharacterModParent __1)
        { try { string pn = "null"; try { pn = __1 != null ? __1.ModParentName : "null"; } catch { } Leaf("CC.AssignParents(parent=" + pn + ")"); } catch { } }
        static void Post_ChangeSkin(Il2Cpp.CharacterCustomisation __instance, UnityEngine.GameObject __0, UnityEngine.Material __1)
        { try { Leaf("CC.ChangeSkin(go=" + (__0 != null ? __0.name : "null") + " mat=" + (__1 != null ? __1.name : "null") + ")"); } catch { } }
        static void Post_Initialize(Il2Cpp.CharacterMod __instance, Il2Cpp.CharacterModParent __0)
        { try { string pn = "null"; try { pn = __0 != null ? __0.ModParentName : "null"; } catch { } Leaf("CharacterMod.Initialize(parent=" + pn + ")"); } catch { } }
        static void Post_ActuallySetMaterial(Il2Cpp.CharacterMod __instance)
        { try { int mi = -1; try { mi = __instance.materialIndex; } catch { } Leaf("CharacterMod.ActuallySetMaterial(matIndex=" + mi + ")"); } catch { } }
        static void Post_ApplyColorModifier(Il2Cpp.CharacterMod __instance)
        { try { Leaf("CharacterMod.ApplyColorModifier()"); } catch { } }
        static void Post_UI_OnSelected(Il2Cpp.CharacterModUI __instance)
        { try { Leaf("CharacterModUI.OnSelected()  [BUTTON]"); } catch { } }
        static void Post_UI_OnSelectedModParent(Il2Cpp.CharacterModUI __instance)
        { try { Leaf("CharacterModUI.OnSelectedModParent()  [BUTTON]"); } catch { } }

        static void Walk(Il2CppSystem.Collections.Generic.List<Il2Cpp.CharacterModParent> ps, string pre, Dictionary<string, string> outd)
        {
            try
            {
                int n = ps.Count;
                for (int k = 0; k < n; k++)
                {
                    var p = ps[k]; if (p == null) continue;
                    string name = "#" + k; try { if (!string.IsNullOrEmpty(p.ModParentName)) name = p.ModParentName; } catch { }
                    string key = pre + name, val = "-";
                    var cur = p.currentMod;
                    if (cur != null)
                    {
                        string mn = ""; try { var mi = cur.ModInfo; mn = mi != null ? mi.ModName : ""; } catch { }
                        int mat = -1; try { mat = cur.materialIndex; } catch { }
                        val = mn + "#" + mat;
                        Il2CppSystem.Collections.Generic.List<Il2Cpp.CharacterModParent> kids = null;
                        try { kids = cur.childModParents; } catch { }
                        if (kids != null) Walk(kids, key + ">", outd);
                    }
                    outd[key] = val;
                }
            }
            catch { }
        }
        static void Diff(string tag)
        {
            try
            {
                var cust = LoadoutPresetsMod._cust; if (cust == null) return;
                var ps = cust.modParents; if (ps == null) return;
                var cur = new Dictionary<string, string>();
                Walk(ps, "", cur);
                if (_snap != null)
                    foreach (var kv in cur)
                    {
                        string o; _snap.TryGetValue(kv.Key, out o);
                        if (o != kv.Value) LoadoutPresetsMod.Log("      DELTA[" + tag + "] " + kv.Key + ": " + (o ?? "-") + " -> " + kv.Value);
                    }
                _snap = cur;
            }
            catch (Exception e) { LoadoutPresetsMod.Log("[instr] Diff err: " + e.Message); }
        }
    }
}
