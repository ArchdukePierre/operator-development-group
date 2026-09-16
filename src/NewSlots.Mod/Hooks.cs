// ODG Operations - NewSlots
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NewSlots
{
    internal static class Hooks
    {
        static MethodInfo _addCompatible;
        internal static int Injected;
        static readonly System.Collections.Generic.HashSet<string> _seenCompat = new System.Collections.Generic.HashSet<string>();

        internal static void Install()
        {
            int ok = 0, fail = 0;

            void Bind(MethodBase target, string pre, string post, string label)
            {
                try
                {
                    if (target == null) { NewSlotsMod.Log("PATCH MISS (no such method): " + label); fail++; return; }
                    var preM = pre == null ? null : new HarmonyMethod(typeof(Hooks).GetMethod(pre, BindingFlags.Static | BindingFlags.NonPublic));
                    var postM = post == null ? null : new HarmonyMethod(typeof(Hooks).GetMethod(post, BindingFlags.Static | BindingFlags.NonPublic));
                    NewSlotsMod.Harm.Patch(target, prefix: preM, postfix: postM);
                    ok++;
                }
                catch (Exception e) { NewSlotsMod.Log("PATCH FAIL " + label + ": " + e.Message); fail++; }
            }

            Bind(AccessTools.Method(typeof(Il2Cpp.LoadoutManager), "GetCharacterMod", new Type[] { typeof(int) }),
                 nameof(Pre_GetById), null, "LoadoutManager.GetCharacterMod(int)");

            Bind(AccessTools.Method(typeof(Il2Cpp.LoadoutManager), "GetCharacterMod", new Type[] { typeof(string) }),
                 nameof(Pre_GetByName), null, "LoadoutManager.GetCharacterMod(string)");

            Bind(AccessTools.Method(typeof(Il2Cpp.LoadoutManager), "find_characterMod", new Type[] { typeof(string) }),
                 nameof(Pre_FindCharacterMod), null, "LoadoutManager.find_characterMod (boot-race guard)");

            Bind(AccessTools.Method(typeof(Il2Cpp.CharacterMod), "TryInitAssetRef"),
                 nameof(Pre_TryInitAssetRef), null, "CharacterMod.TryInitAssetRef (== Awake, folded)");

            Bind(AccessTools.Method(typeof(Il2Cpp.LoadoutManager), "RegisterCharacterModInstance"),
                 nameof(Pre_SkipIfOurs), null, "LoadoutManager.RegisterCharacterModInstance");
            Bind(AccessTools.Method(typeof(Il2Cpp.LoadoutManager), "CharacterModDestroyed"),
                 nameof(Pre_SkipIfOurs), null, "LoadoutManager.CharacterModDestroyed");

            Bind(AccessTools.Method(typeof(Il2Cpp.CharacterMod), "DestroyAssetRef"),
                 nameof(Pre_DestroyAssetRef), null, "CharacterMod.DestroyAssetRef");

            Bind(AccessTools.Method(typeof(Il2Cpp.CharacterCustomisationController), "Start_Selection_Modding"),
                 null, nameof(Post_StartSelectionModding), "Controller.Start_Selection_Modding");

            Bind(AccessTools.Method(typeof(Il2Cpp.CharacterMod), "Initialize"),
                 null, nameof(Post_ModInitialize), "CharacterMod.Initialize");

            Bind(AccessTools.Method(typeof(Il2Cpp.CharacterCustomisationController), "StartCustomisation"),
                 null, nameof(Post_CabinetOpen), "Controller.StartCustomisation");
            Bind(AccessTools.Method(typeof(Il2Cpp.CharacterCustomisationController), "button_StopCustomisation"),
                 null, nameof(Post_CabinetClose), "Controller.button_StopCustomisation");
            Bind(AccessTools.Method(typeof(Il2Cpp.CharacterCustomisationController), "OnSelectedMod"),
                 null, nameof(Post_OnSelectedMod), "Controller.OnSelectedMod");

            Bind(AccessTools.Method(typeof(Il2Cpp.CharacterSettings), "Client_SetCharacter"),
                 nameof(Pre_EnsureRegistered), null, "CharacterSettings.Client_SetCharacter");
            Bind(AccessTools.Method(typeof(Il2Cpp.CharacterSettings), "syncCharacterMods"),
                 nameof(Pre_EnsureRegistered), null, "CharacterSettings.syncCharacterMods");
            Bind(AccessTools.Method(typeof(Il2Cpp.CharacterModSync), "ApplyCurrentModPreset"),
                 nameof(Pre_EnsureRegistered), null, "CharacterModSync.ApplyCurrentModPreset");
            Bind(AccessTools.Method(typeof(Il2Cpp.CharacterModSync), "ApplyNewPresetToClient"),
                 nameof(Pre_EnsureRegistered), null, "CharacterModSync.ApplyNewPresetToClient");

            Bind(AccessTools.Method(typeof(Il2Cpp.CharacterModSync), "CMD_SetCharacter", new Type[] { typeof(string), typeof(string) }),
                 nameof(Pre_SendCharacter), null, "CharacterModSync.CMD_SetCharacter (attach seating)");
            Bind(AccessTools.Method(typeof(Il2Cpp.CharacterModSync), "OnCharacterModsJSONChanged", new Type[] { typeof(string), typeof(string) }),
                 nameof(Pre_ModsJsonChanged), null, "CharacterModSync.OnCharacterModsJSONChanged (detach seating)");

            try { _addCompatible = AccessTools.Method(typeof(Il2Cpp.CharacterCustomisationController), "AddSingleCompatibleMod"); }
            catch { }
            if (_addCompatible == null) NewSlotsMod.Log("AddSingleCompatibleMod not found - our items will not appear in socket pickers.");

            NewSlotsMod.Log("hooks installed: " + ok + " ok, " + fail + " failed.");
        }

        static bool Pre_GetById(Il2Cpp.LoadoutManager __instance, int __0, ref Il2Cpp.CharacterMod __result)
        {
            try
            {
                Il2Cpp.CharacterMod mine;
                if (!Catalog.ById.TryGetValue(__0, out mine) || mine == null) return true;

                try { mine.SetAssetReferenceIndex(__0); } catch { }
                try { mine.ModInfoIndex = __0; } catch { }

                __result = mine;
                return false;
            }
            catch (Exception e) { NewSlotsMod.Log("GetCharacterMod(int) prefix threw: " + e.Message); return true; }
        }

        static void EnsureIfOurs(string name)
        {
            try
            {
                if (Catalog.Registered || string.IsNullOrEmpty(name)) return;
                if (!Defs.Names.Contains(name)) return;
                NewSlotsMod.Log("boot race: " + ItemFactory.Fmt(name) + " was asked for before the build finished - completing it now");
                NewSlotsMod.FlushLog();
                Catalog.EnsureRegistered();
            }
            catch (Exception e) { NewSlotsMod.Log("EnsureIfOurs threw: " + e.Message); }
        }

        static void Pre_FindCharacterMod(string __0) { EnsureIfOurs(__0); }

        static bool Pre_GetByName(Il2Cpp.LoadoutManager __instance, string __0, ref Il2Cpp.CharacterMod __result)
        {
            try
            {
                if (string.IsNullOrEmpty(__0)) return true;
                EnsureIfOurs(__0);
                int id;
                if (!Catalog.IdByName.TryGetValue(__0, out id)) return true;

                Il2Cpp.CharacterMod mine;
                if (!Catalog.ById.TryGetValue(id, out mine) || mine == null) return true;

                try { mine.SetAssetReferenceIndex(id); } catch { }
                try { mine.ModInfoIndex = id; } catch { }

                __result = mine;
                if (!NewSlotsMod.QuietName) { NewSlotsMod.Log("served by name: " + ItemFactory.Fmt(__0) + " -> " + id); NewSlotsMod.QuietName = true; }
                return false;
            }
            catch (Exception e) { NewSlotsMod.Log("GetCharacterMod(string) prefix threw: " + e.Message); return true; }
        }

        static bool Pre_TryInitAssetRef(Il2Cpp.CharacterMod __instance)
        {
            try
            {
                if (!Catalog.Registered || Catalog.StockCount < 0) return true;
                if (__instance == null) return true;

                try { if (Catalog.StreamAllowFor != 0 && __instance.GetInstanceID() == Catalog.StreamAllowFor) return true; }
                catch { }

                int ari;
                try { ari = __instance.AssetReferenceIndex; } catch { return true; }
                if (ari < Catalog.StockCount) return true;

                try { __instance._assetRefInit = true; } catch { }
                return false;
            }
            catch { return true; }
        }

        static bool Pre_SkipIfOurs(int __0)
        {
            try { return !Catalog.IsOurs(__0); }
            catch { return true; }
        }

        static bool Pre_DestroyAssetRef(Il2Cpp.CharacterMod __instance)
        {
            try
            {
                if (!Catalog.Registered || Catalog.StockCount < 0 || __instance == null) return true;
                int ari;
                try { ari = __instance.AssetReferenceIndex; } catch { return true; }
                return ari < Catalog.StockCount;
            }
            catch { return true; }
        }

        static void Post_StartSelectionModding(Il2Cpp.CharacterCustomisationController __instance, Il2Cpp.CharacterModParent __0)
        {
            try
            {
                Ui.CurrentSocket = __0;
                if (!Catalog.Registered || _addCompatible == null) return;
                if (__0 == null) return;

                var cur = __instance.currentModdingParent;
                if (cur == null) return;
                if (cur.Pointer != __0.Pointer) return;
                Il2Cpp.CharacterModUI ui = null;
                try { ui = cur.modUI; } catch { }
                if (ui == null) return;

                string socketCompat = null;
                try { socketCompat = __0.Mod_Compatibility; } catch { }
                if (string.IsNullOrEmpty(socketCompat)) return;

                var lm = Il2Cpp.LoadoutManager.singleton;
                if (lm == null) return;
                var infos = lm.CharacterModInfo;
                if (infos == null) return;

                int added = 0;
                foreach (var d in Catalog.Live)
                {
                    if (d.Index < 0 || d.Index >= infos.Length) continue;
                    var row = infos[d.Index];
                    if (row == null) continue;

                    string ourCompat = null;
                    try { ourCompat = row.GetCompatibilityString(); } catch { }
                    if (!string.Equals(ourCompat, socketCompat, StringComparison.Ordinal)) continue;

                    try
                    {
                        _addCompatible.Invoke(__instance, new object[] { d.Index });
                        added++;
                    }
                    catch (Exception e) { NewSlotsMod.Log("inject " + ItemFactory.Fmt(d.ModName) + " failed: " + e.Message); }
                }

                if (added > 0)
                {
                    Injected += added;
                    NewSlotsMod.Log("picker " + ItemFactory.Fmt(SafeName(__0)) + " compat=" + ItemFactory.Fmt(socketCompat) +
                                    ": injected " + added + " item(s)");
                }
                else if (_seenCompat.Add(socketCompat))
                {

                    NewSlotsMod.Log("picker " + ItemFactory.Fmt(SafeName(__0)) + " compat=" + ItemFactory.Fmt(socketCompat) +
                                    ": nothing of ours matches this family");
                }
            }
            catch (Exception e) { NewSlotsMod.Log("socket injection threw: " + e.Message); }
        }

        static string SafeName(Il2Cpp.CharacterModParent p)
        {
            try { return p.ModParentName; } catch { return "(unreadable)"; }
        }

        static void Pre_EnsureRegistered()
        {
            try { Catalog.EnsureRegistered(); } catch { }
        }

        internal static void SocketIdentity(Il2Cpp.CharacterModParent p, out string garment, out string socket)
        {
            garment = null; socket = null;
            if (p == null) return;
            try { socket = p.ModParentName; } catch { }
            try
            {
                var owner = p.parentMod;
                if (owner != null)
                {
                    var info = owner.ModInfo;
                    if (info != null) garment = info.ModName;
                    if (string.IsNullOrEmpty(garment)) garment = owner.gameObject.name;
                }
            }
            catch { }
        }

        static void Post_ModInitialize(Il2Cpp.CharacterMod __instance, Il2Cpp.CharacterModParent __0)
        {
            try
            {

                if (!Catalog.Registered || __instance == null) return;
                int ari;
                try { ari = __instance.AssetReferenceIndex; } catch { return; }
                if (ari < Catalog.StockCount) return;

                Def d = null;
                foreach (var x in Catalog.Live)
                    if (x.Index == ari) { d = x; break; }

                if (d == null || (d.Type != Kind.Patch && d.Type != Kind.Garment)) return;

                string garment, socket;
                SocketIdentity(__0, out garment, out socket);

                Seat.Register(d, __instance.gameObject, garment, socket);
            }
            catch { }
        }

        static void Post_CabinetOpen(Il2Cpp.CharacterCustomisationController __instance)
        {
            Ui.CabinetOpen = true;
            Ui.Controller = __instance;
        }

        static void Post_CabinetClose() { Ui.Close(); }

        static void Pre_SendCharacter(ref string __1)
        {
            try { __1 = TuningNet.Attach(__1); } catch { }
        }

        static void Pre_ModsJsonChanged(Il2Cpp.CharacterModSync __instance, ref string __1)
        {
            try
            {
                uint peer = 0; bool local = false;
                try { local = __instance.isLocalPlayer; } catch { }
                try { peer = __instance.netId; } catch { }
                string cleaned = TuningNet.Detach(__1, peer, local);
                if (!ReferenceEquals(cleaned, __1))
                {
                    __1 = cleaned;
                    try { __instance.characterModsJSON = cleaned; } catch { }
                }
            }
            catch { }
        }

        static void Post_OnSelectedMod(string __0)
        {
            try
            {
                int id;
                if (__0 != null && Catalog.IdByName.TryGetValue(__0, out id))
                    foreach (var d in Catalog.Live)
                        if (d.Index == id) { Ui.LastSelected = d; break; }
            }
            catch { }
        }
    }
}
