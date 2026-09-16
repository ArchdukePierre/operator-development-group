// ODG Operations - M107
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(M107.M107Mod), "M107", "0.1.0", "ArchdukePierre")]
[assembly: MelonGame(null, null)]

namespace M107
{

    public class M107Mod : MelonMod
    {
        internal static M107Mod Instance;

        static string GameRoot
        {
            get { try { return MelonLoader.Utils.MelonEnvironment.GameRootDirectory; } catch { return AppContext.BaseDirectory; } }
        }
        internal static string LogPath => Path.Combine(GameRoot, "Mods", "M107.log");

        int _throttle;
        bool _done;
        bool _artOk, _tplOk, _regOk, _partsOk;

        internal static MelonPreferences_Entry<bool> PlacementKeys;

#if ODG_PUBLIC
        static bool DevKey(KeyCode k) { return false; }
#else
        static bool DevKey(KeyCode k) { return PlacementKeys != null && PlacementKeys.Value && Input.GetKeyDown(k); }
#endif

        public override void OnInitializeMelon()
        {
            Instance = this;

            try
            {
                var cfg = MelonPreferences.CreateCategory("M107");
                PlacementKeys = cfg.CreateEntry("EnablePlacementKeys", false);
            }
            catch (Exception e) { Log("prefs failed: " + e.Message); }
            try { File.AppendAllText(LogPath, "\n\n========== M107 v0.1  " + DateTime.Now + " ==========\n"); } catch { }
            Patch();
            Log("loaded. waiting for the armoury.");
        }

        void Patch()
        {
            int ok = 0, fail = 0;
            void P(MethodBase t, string post, string label)
            {
                try
                {
                    if (t == null) { Log("PATCH MISS: " + label); fail++; return; }
                    HarmonyInstance.Patch(t, postfix: new HarmonyMethod(
                        typeof(M107Mod).GetMethod(post, BindingFlags.Static | BindingFlags.NonPublic)));
                    ok++;
                }
                catch (Exception e) { Log("PATCH FAIL " + label + ": " + e.Message); fail++; }
            }

            P(AccessTools.Method(typeof(Il2Cpp.WeaponMods), "RegisterWeaponMod"), nameof(Post_RegisterMod), "WeaponMods.RegisterWeaponMod");

            HarmonyInstance.Patch(AccessTools.Method(typeof(Il2Cpp.WeaponMods), "UserCode_ForceRebuildWeapon"),
                prefix: new HarmonyMethod(typeof(M107Mod).GetMethod(nameof(Pre_Rebuild), BindingFlags.Static | BindingFlags.NonPublic)));

            try
            {
                HarmonyInstance.Patch(AccessTools.Method(typeof(Il2Cpp.WeaponMods), "ChangeAttachmentString"),
                    prefix: new HarmonyMethod(typeof(M107Mod).GetMethod(nameof(Pre_ChangeString), BindingFlags.Static | BindingFlags.NonPublic)));
                ok++;
            }
            catch (Exception e) { Log("PATCH FAIL ChangeAttachmentString: " + e.Message); fail++; }

            try
            {
                HarmonyInstance.Patch(AccessTools.Method(typeof(Il2Cpp.WeaponMods), "MenuSetBaseMods"),
                    prefix: new HarmonyMethod(typeof(M107Mod).GetMethod(nameof(Pre_MenuSetBaseMods), BindingFlags.Static | BindingFlags.NonPublic)));
                ok++;
            }
            catch (Exception e) { Log("PATCH FAIL MenuSetBaseMods: " + e.Message); fail++; }

            try
            {
                HarmonyInstance.Patch(AccessTools.Method(typeof(Il2Cpp.WeaponModding), "ChangeEyePositionSlider"),
                    prefix: new HarmonyMethod(typeof(M107Mod).GetMethod(nameof(Pre_EyeSliderTable), BindingFlags.Static | BindingFlags.NonPublic)));
                ok++;
            }
            catch (Exception e) { Log("PATCH FAIL ChangeEyePositionSlider: " + e.Message); fail++; }

            try
            {
                HarmonyInstance.Patch(AccessTools.Method(typeof(Il2Cpp.WeaponMods), "SetNewEyeOffset"),
                    prefix: new HarmonyMethod(typeof(M107Mod).GetMethod(nameof(Pre_EyeSliderDirect), BindingFlags.Static | BindingFlags.NonPublic)));
                ok++;
            }
            catch (Exception e) { Log("PATCH FAIL SetNewEyeOffset: " + e.Message); fail++; }
            try
            {
                HarmonyInstance.Patch(AccessTools.Method(typeof(Il2Cpp.WeaponV3), "WeaponPlayAnimation",
                        new Type[] { typeof(string), typeof(float) }),
                    postfix: new HarmonyMethod(typeof(M107Mod), nameof(Post_AnimProbe)));
            }
            catch (Exception e) { Log("PATCH FAIL WeaponPlayAnimation (probe): " + e.Message); fail++; }

            P(AccessTools.Method(typeof(Il2Cpp.WeaponMods), "SERVER_SetBaseMods"), nameof(Post_Rebuild), "WeaponMods.SERVER_SetBaseMods");
            P(AccessTools.Method(typeof(Il2Cpp.WeaponMods), "ForceRebuildWeapon"), nameof(Post_Rebuild), "WeaponMods.ForceRebuildWeapon");

            foreach (var m in new[] { "UserCode_ForceRebuildWeapon", "RebuildWeaponWithNetworkPreset", "RebuildModParentsOrdered" })
            {

                try
                {
                    var t2 = AccessTools.Method(typeof(Il2Cpp.WeaponMods), m);
                    if (t2 == null) { Log("PATCH MISS: WeaponMods." + m); fail++; continue; }
                    HarmonyInstance.Patch(t2, postfix: new HarmonyMethod(
                        typeof(M107Mod).GetMethod(nameof(Post_Rebuild), BindingFlags.Static | BindingFlags.NonPublic)));
                    ok++;
                }
                catch (Exception e) { Log("PATCH FAIL " + m + ": " + e.Message); fail++; }
            }
            P(AccessTools.Method(typeof(Il2Cpp.N_StaticWeaponSpawner), "SpawnWeapon"), nameof(Post_HookTaken), "N_StaticWeaponSpawner.SpawnWeapon");

            P(AccessTools.Method(typeof(Il2Cpp.BulletShell), "FireRound"), nameof(Post_FireRound), "BulletShell.FireRound");

            void PP(MethodBase t, string pre, string label)
            {
                try
                {
                    if (t == null) { Log("PATCH MISS: " + label); fail++; return; }
                    HarmonyInstance.Patch(t, prefix: new HarmonyMethod(
                        typeof(M107Mod).GetMethod(pre, BindingFlags.Static | BindingFlags.NonPublic)));
                    ok++;
                }
                catch (Exception e) { Log("PATCH FAIL " + label + ": " + e.Message); fail++; }
            }

            PP(AccessTools.Method(typeof(Il2Cpp.WeaponMod), "TryInitAssetRef"), nameof(Pre_TryInitAssetRef), "WeaponMod.TryInitAssetRef");

            PP(AccessTools.Method(typeof(Il2Cpp.LoadoutManager), "GetWeaponMod"), nameof(Pre_GetWeaponMod), "LoadoutManager.GetWeaponMod");
            PP(AccessTools.Method(typeof(Il2Cpp.LoadoutManager), "GetAttachment"), nameof(Pre_GetAttachment), "LoadoutManager.GetAttachment");
            PP(AccessTools.Method(typeof(Il2Cpp.LoadoutManager), "find_attachment"), nameof(Pre_FindAttachment), "LoadoutManager.find_attachment");

            PP(AccessTools.Method(typeof(Il2Cpp.WeaponMod), "WeaponModName"), nameof(Pre_WeaponModName), "WeaponMod.WeaponModName");

            try
            {
                HarmonyInstance.Patch(AccessTools.Method(typeof(Il2Cpp.PlayerNetworking), "SpawnWeapon",
                        new Type[] { typeof(string), typeof(string), typeof(Il2Cpp.PlayerNetworking.WeaponSlot) }),
                    prefix: new HarmonyMethod(typeof(M107Mod).GetMethod(nameof(Pre_SpawnWeaponNames), BindingFlags.Static | BindingFlags.NonPublic)));
                ok++;
            }
            catch (Exception e) { Log("PATCH FAIL SpawnWeapon(names): " + e.Message); fail++; }

            P(AccessTools.Method(typeof(Il2Cpp.ModUI), "AddMagazineToPouch"), nameof(Post_AddMagPouch), "ModUI.AddMagazineToPouch");

            PP(AccessTools.Method(typeof(Il2Cpp.MagManagerUI), "InitializeMagManager"), nameof(Pre_MagManagerOpen), "MagManagerUI.InitializeMagManager");

            PP(AccessTools.Method(typeof(Il2Cpp.MagManagerMagUI), "InitializeThisInstance"), nameof(Pre_PouchBox), "MagManagerMagUI.InitializeThisInstance");

            PP(AccessTools.Method(typeof(Il2Cpp.ModUI), "Init"), nameof(Pre_ModUIInit), "ModUI.Init");

            P(AccessTools.Method(typeof(Il2Cpp.ModUI), "Init"), nameof(Post_ModUIInit), "ModUI.Init (hide)");

            Log("patches: " + ok + " bound, " + fail + " failed.");
        }

        static GameObject HeldForProbe()
        {
            try
            {
                for (int i = 0; i < _live.Count; i++)
                {
                    var g = _live[i];
                    if (g == null) continue;
                    if (IsLocallyHeld(g)) return g;
                }
            }
            catch { }
            return null;
        }

        static bool IsLive(GameObject g)
        {
            try { for (int i = 0; i < _live.Count; i++) if (_live[i] == g) return true; } catch { }
            return false;
        }

        static void Post_AnimProbe(Il2Cpp.WeaponV3 __instance, string __0)
        {
            try
            {
                if (__instance == null) return;
                var g = __instance.gameObject;
                if (!IsLive(g) || !IsLocallyHeld(g)) return;
                AdsProbe.Event("anim:" + __0);
            }
            catch { }
        }

        public override void OnLateUpdate()
        {
            long t0 = Perf.Begin();
            try { if (_regOk) Alignment.LateTick(); } catch { }

            try { AdsProbe.Tick(HeldForProbe()); } catch { }

            try { if (_regOk) BenchList.HoldHidden(); } catch { }

            try { if (_regOk) { long s = Perf.Begin(); MagFollow.Tick(); Perf.End(Perf.MagFollowTick, s); } } catch { }
            Perf.End(Perf.LateUpdate, t0);
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {

            RackHook.Reset();
            Alignment.ResetForScene();
            AdsProbe.ResetForScene();
            Feel.Reset();
            Parts.ResetForScene();
            BenchList.ResetForScene();
            MagFix.ResetForScene();
            MagFollow.Reset();
            _healed.Clear();
            _retagged.Clear();
            _oursById.Clear();
            _oursLogged.Clear();
            _live.Clear();
            _dumped = false;
            _dumpAt = 0f;
            _done = false;
            _throttle = 0;
        }

        public override void OnUpdate()
        {
            long _t0 = Perf.Begin();
            try
            {

                if (_regOk) { Alignment.Tick(); Feel.Tick(); }
                if (_regOk && (Time.frameCount % 5) == 0) Alignment.ReportAds(LocalPlayer());
                if (_regOk && (Time.frameCount % 30) == 0)
                { long s = Perf.Begin(); SweepLive(); Perf.End(Perf.Sweep, s); }

                if (_regOk && Parts.Registered && Time.unscaledTime >= _discoverAt)
                {
                    _discoverAt = Time.unscaledTime + 2f;
                    long s = Perf.Begin(); DiscoverWeapons(); Perf.End(Perf.Discover, s);
                }

                if (_regOk && Parts.Registered && Time.unscaledTime >= _worldScanAt)
                {
                    long s = Perf.Begin();
                    int fresh = DiscoverWorldWeapons();
                    Perf.End(Perf.WorldScan, s);

                    _worldScanEvery = fresh > 0 ? 4f : Mathf.Min(_worldScanEvery * 1.6f, 30f);
                    _worldScanAt = Time.unscaledTime + _worldScanEvery;
                }

                if (_regOk && Parts.IconQueue.Count > 0 && (Time.frameCount % 3) == 0)
                    Parts.RenderOneQueuedIcon();

                Perf.MaybeReport();

                if (Time.unscaledTime >= _logFlushAt) { _logFlushAt = Time.unscaledTime + 0.5f; FlushLog(); }

                if (_regOk)
                {
                    try
                    {
                        if (DevKey(KeyCode.Slash)) DumpMagObjects("SLASH pressed");
                        else if (DevKey(KeyCode.Period))
                            foreach (var g in _live) if (g != null) { DumpVisible(g); break; }
                    }
                    catch { }
                }

                if (_done || ++_throttle < 30) return;
                _throttle = 0;

                if (!ArmouryLoaded())
                {
                    if (Time.timeSinceLevelLoad > 120f) { _done = true; Log("no armoury in this scene, standing down."); }
                    return;
                }

                if (Time.timeSinceLevelLoad > 180f) { _done = true; Log("gave up: setup did not complete in 180s."); return; }

                if (!_artOk) { _artOk = BarrettArt.Harvest(); if (!_artOk) return; }

                Il2Cpp.LoadoutManager lm = null;
                try { lm = Il2Cpp.LoadoutManager.singleton; } catch { }
                if (lm == null) return;

                if (!_tplOk) { _tplOk = WeaponTemplate.Build(lm); if (!_tplOk) return; }
                if (!_regOk)
                {
                    _regOk = WeaponTemplate.Register(lm);
                    if (_regOk && WeaponTemplate.RegisteredIndex != 53)
                        Log("NOTE: registered at index " + WeaponTemplate.RegisteredIndex +
                            ", expected 53. Every peer must load the same mod set in the same order.");
                    if (!_regOk) return;
                }

                if (!_partsOk)
                {
                    Parts.Probe(lm, WeaponTemplate.Template);
                    _partsOk = Parts.RegisterAll(lm);
                    if (_partsOk) WeaponTemplate.MakeItsOwn();
                    else Log("parts: registration did not complete â€” the rifle still works, the bench just shows donor parts.");
                }

                if (RackHook.TryBuild())
                {
                    _done = true;
                    Log("setup complete: '" + WeaponTemplate.DisplayName + "' registered at [" +
                        WeaponTemplate.RegisteredIndex + "] and hanging on the OPFOR rack.");
                }
            }
            catch (Exception e) { Log("update err: " + e.Message); }
            finally { Perf.End(Perf.Update, _t0); }
        }

        static Il2Cpp.PlayerNetworking _player;
        static Il2Cpp.PlayerNetworking LocalPlayer()
        {
            try { if (_player != null) return _player; } catch { _player = null; }
            try
            {
                var lp = Il2CppMirror.NetworkClient.localPlayer;
                if (lp != null) _player = lp.gameObject.GetComponent<Il2Cpp.PlayerNetworking>();
            }
            catch { }
            return _player;
        }

        static bool ArmouryLoaded()
        {
            try
            {
                for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                {
                    var s = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                    if (!s.isLoaded) continue;
                    var n = s.name;
                    if (!string.IsNullOrEmpty(n) && n.IndexOf("Operation Room", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
            }
            catch { }
            return false;
        }

        internal static bool IsLocallyHeld(GameObject weapon)
        {
            try
            {
                if (weapon == null) return false;
                var pn = weapon.GetComponentInParent<Il2Cpp.PlayerNetworking>();
                if (pn == null) return false;
                try { return pn.isOwned; } catch { return false; }
            }
            catch { return false; }
        }

        const string ArtRootName = "M107A1_Body";
        static readonly System.Collections.Generic.HashSet<int> _retagged = new System.Collections.Generic.HashSet<int>();

        static readonly System.Collections.Generic.List<GameObject> _live = new System.Collections.Generic.List<GameObject>();

        static Il2Cpp.PlayerNetworkingClientSide LocalClientSide()
        {
            try
            {
                var pn = LocalPlayer();
                if (pn == null) return null;
                var go = pn.gameObject;
                if (go == null) return null;
                var cs = go.GetComponent<Il2Cpp.PlayerNetworkingClientSide>();
                if (cs == null) cs = go.GetComponentInChildren<Il2Cpp.PlayerNetworkingClientSide>(true);
                return cs;
            }
            catch { return null; }
        }

        static readonly System.Collections.Generic.HashSet<int> _healed = new System.Collections.Generic.HashSet<int>();

        static void HealStoredStrings(GameObject go)
        {
            try
            {
                if (Parts.NameByPath.Count == 0) return;
                int iid; try { iid = go.GetInstanceID(); } catch { return; }
                if (_healed.Contains(iid)) return;

                var wm = go.GetComponent<Il2Cpp.WeaponMods>();
                if (wm == null) wm = go.GetComponentInChildren<Il2Cpp.WeaponMods>(true);
                if (wm == null) return;

                int total = 0;
                try
                {
                    var j = wm.JSON_MOD_STRING;
                    var f = LoadoutRepair.Fix(j, out int n1);
                    if (n1 > 0) { wm.JSON_MOD_STRING = f; total += n1; }
                }
                catch { }
                try
                {
                    var b = wm.BASE_JSON_MOD_STRING;
                    var f = LoadoutRepair.Fix(b, out int n2);
                    if (n2 > 0) { wm.BASE_JSON_MOD_STRING = f; total += n2; }
                }
                catch { }

                _healed.Add(iid);
                if (total > 0)
                    Log("loadout: healed " + total + " blank part name(s) carried by '" + go.name +
                        "' - blanks vs real names is what rebuilt the rifle in a loop and crashed the game");
            }
            catch { }
        }

        static int _stormCount;
        static float _stormWindow;
        static bool _stormTripped;

        static void StormCheck(Il2Cpp.WeaponMods wm)
        {
            try
            {
                float now = Time.time;
                if (now > _stormWindow) { _stormWindow = now + 5f; _stormCount = 0; _stormTripped = false; }
                if (++_stormCount < 250) return;
                if (!_stormTripped)
                {
                    _stormTripped = true;
                    string j = null; try { j = wm.JSON_MOD_STRING; } catch { }
                    Log("REBUILD STORM: " + _stormCount + " part attaches in 5s on '" + wm.gameObject.name +
                        "'. Not standing down - reporting only. carried string: " + (j ?? "<null>"));
                }
            }
            catch { }
        }

        static void SweepLive()
        {

            if (!_dumped && _dumpAt > 0f && Time.time >= _dumpAt)
            {
                _dumped = true;
                foreach (var g in _live) if (g != null) { DumpVisible(g); break; }
            }
            try
            {
                if (DevKey(KeyCode.Period))
                    foreach (var g in _live) if (g != null) { DumpVisible(g); break; }
            }
            catch { }

            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var g2 = _live[i];
                bool gone = false;
                try { gone = g2 == null; } catch { }
                if (gone) _live.RemoveAt(i);
            }
            if (_live.Count == 0) return;

            if (_sweepCursor >= _live.Count) _sweepCursor = 0;
            int only = _sweepCursor++;

            for (int i = only; i == only; i++)
            {
                var go = _live[i];
                bool dead = true;
                try { dead = go == null || !go.activeInHierarchy; } catch { }
                if (dead) continue;
                HideUnder(go);
                HealStoredStrings(go);

                Parts.FixMagazineSystem(go);

                try
                {
                    var wm = go.GetComponent<Il2Cpp.WeaponMods>();
                    if (wm != null)
                    {
                        WarnBig("live JSON_MOD_STRING on " + go.name, wm.JSON_MOD_STRING);
                        WarnBig("live BASE_JSON on " + go.name, wm.BASE_JSON_MOD_STRING);
                    }
                }
                catch { }

                if (Parts.FullSet)
                {
                    int id = 0;
                    try { id = go.GetInstanceID(); } catch { continue; }
                    if (_retagged.Add(id))
                    {
                        int n = Parts.RetagSockets(go);
                        Log("bench: claimed " + n + " socket(s) on the live " + go.name);
                    }

                    try { PinAllArt(go); } catch { }

                    Feel.SetupWeapon(go);

                    if (IsLocallyHeld(go))
                    {
                        Alignment.RegisterAdsAnchors(go);
                        Feel.DetectScope(go);
                    }

                    Parts.MakeTopRail(go);
                    Feel.FindBolt(go);
                    if ((Time.frameCount % 300) == 0) Feel.SwapBrass(go);
                }
            }
        }

        static float _discoverAt;

        static void DiscoverWeapons()
        {
            try
            {
                var pms = Il2Cpp.PlayerMaster.AllPlayerMasters;
                if (pms == null) return;

                for (int p = 0; p < pms.Count; p++)
                {
                    var pm = pms[p];
                    if (pm == null) continue;
                    Transform root; try { root = pm.transform; } catch { continue; }
                    if (root == null) continue;

                    var mods = root.GetComponentsInChildren<Il2Cpp.WeaponMods>(true);
                    if (mods == null) continue;

                    for (int i = 0; i < mods.Length; i++)
                    {
                        var wm = mods[i];

                        if (wm == null || !IsOurs(wm)) continue;
                        GameObject go; try { go = wm.gameObject; } catch { continue; }
                        if (go == null) continue;

                        bool known = false;
                        for (int k = 0; k < _live.Count; k++) if (_live[k] == go) { known = true; break; }
                        if (known) continue;

                        TrackLive(go);
                        Log("found a Barrett the patches never reported: '" + go.name + "' - assembling it");
                    }
                }
            }
            catch { }
        }

        static float _worldScanAt;
        static float _worldScanEvery = 4f;

        static int DiscoverWorldWeapons()
        {
            int fresh = 0;
            try
            {
                var found = UnityEngine.Object.FindObjectsByType(
                    Il2CppInterop.Runtime.Il2CppType.Of<Il2Cpp.WeaponMods>(),
                    UnityEngine.FindObjectsSortMode.None);
                if (found == null) return 0;

                for (int i = 0; i < found.Length; i++)
                {
                    var wm = found[i].TryCast<Il2Cpp.WeaponMods>();
                    if (wm == null || !IsOurs(wm)) continue;
                    GameObject go; try { go = wm.gameObject; } catch { continue; }
                    if (go == null) continue;

                    bool known = false;
                    for (int k = 0; k < _live.Count; k++) if (_live[k] == go) { known = true; break; }
                    if (known) continue;

                    TrackLive(go);
                    fresh++;
                    Log("world scan: found an unassembled Barrett '" + go.name + "' that belongs to no player");
                }
            }
            catch { }
            return fresh;
        }

        const int MaxLive = 24;

        static void TrackLive(GameObject go)
        {
            if (go == null) return;
            for (int i = 0; i < _live.Count; i++) if (_live[i] == go) return;

            if (_live.Count >= MaxLive)
            {
                for (int i = _live.Count - 1; i >= 0; i--)
                {
                    bool dead = false;
                    try { dead = _live[i] == null; } catch { }
                    if (dead) _live.RemoveAt(i);
                }
                if (_live.Count >= MaxLive) _live.RemoveAt(0);
            }

            _live.Add(go);
            if (!_dumped && _dumpAt <= 0f) _dumpAt = Time.time + 3f;
        }

        static bool _dumped;
        static int _sweepCursor;
        static float _dumpAt;

        static bool Pre_ModUIInit(Il2Cpp.WeaponModParent _parent, Il2Cpp.WeaponModding _modding)
        {
            try { BenchList.Fix(_parent); } catch { }
            return true;
        }

        static void Post_ModUIInit(Il2Cpp.ModUI __instance)
        {
            try { BenchList.NoteBox(__instance); } catch { }
        }

        static readonly System.Collections.Generic.HashSet<int> _nameAnswered =
            new System.Collections.Generic.HashSet<int>();

        static bool Pre_WeaponModName(Il2Cpp.WeaponMod __instance, ref string __result)
        {
            try
            {
                if (__instance == null) return true;
                int id; try { id = __instance.AssetReferenceIndex; } catch { return true; }
                if (!Parts.NameById.TryGetValue(id, out var nm)) return true;
                __result = nm;
                if (_nameAnswered.Add(id)) Log("name: answered WeaponModName() for id " + id + " -> '" + nm + "'");
                return false;
            }
            catch { return true; }
        }

        static bool Pre_SpawnWeaponNames(string weaponName, ref string mods)
        {
            try
            {
                if (string.IsNullOrEmpty(mods)) return true;
                if (!string.Equals(weaponName, WeaponTemplate.ObjectName, StringComparison.Ordinal)) return true;
                var fixedJson = LoadoutRepair.Fix(mods, out int filled);
                if (filled > 0) { mods = fixedJson; LoadoutRepair.Note(filled, "spawn"); }
            }
            catch { }
            return true;
        }

        static void Post_AddMagPouch(Il2Cpp.ModUI __instance)
        {
            try { MagFix.RepairPouch("just added"); } catch { }
        }

        static void DumpMagObjects(string why)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("\n########## MAGAZINE OBJECTS (").Append(why).Append(") ##########\n");

                int shown = 0;
                for (int w = 0; w < _live.Count && shown < 12; w++)
                {
                    var weapon = _live[w];
                    bool alive = false;
                    try { alive = weapon != null && weapon.activeInHierarchy; } catch { }
                    if (!alive) continue;

                    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<Il2Cpp.Magazine> mags = null;
                    try { mags = weapon.GetComponentsInChildren<Il2Cpp.Magazine>(true); } catch { }
                    if (mags == null) continue;

                    for (int i = 0; i < mags.Length && shown < 12; i++)
                    {
                        var m = mags[i]; if (m == null) continue;
                        GameObject go = null; try { go = m.gameObject; } catch { continue; }
                        if (go == null) continue;
                        shown++;

                        sb.Append("  '").Append(Safe(() => go.name)).Append("'  active=").Append(go.activeInHierarchy)
                          .Append("  worldPos=").Append(BarrettArt.F(go.transform.position)).Append('\n');
                        sb.Append("      path=").Append(BarrettArt.Path(go.transform)).Append('\n');
                        try
                        {
                            sb.Append("      ammo=").Append(m.ammoAmount).Append('/').Append(m.ammoAmountMax);
                            var cw = m.compatibleWeaponSystems;
                            sb.Append("  fits=[");
                            if (cw != null) for (int k = 0; k < cw.Length; k++) { if (k > 0) sb.Append(','); sb.Append(cw[k]); }
                            sb.Append("]\n");
                        }
                        catch { }

                        try
                        {
                            var all = go.GetComponentsInChildren<Transform>(true);
                            for (int k = 0; k < all.Length; k++)
                            {
                                var t = all[k]; if (t == null) continue;
                                string tn; try { tn = t.name; } catch { continue; }
                                if (tn == null || !tn.EndsWith("_art", StringComparison.Ordinal)) continue;
                                float gap = Vector3.Distance(t.position, go.transform.position);
                                sb.Append("      art '").Append(tn).Append("' worldPos=").Append(BarrettArt.F(t.position))
                                  .Append("  gap from its magazine = ").Append(gap.ToString("F3")).Append("m")
                                  .Append(gap > 0.15f ? "   <<< STRANDED" : "").Append('\n');
                            }
                        }
                        catch { }

                        try
                        {
                            int on = 0, off = 0;
                            var rs = go.GetComponentsInChildren<Renderer>(true);
                            for (int k = 0; k < rs.Length; k++)
                            {
                                var r = rs[k]; if (r == null) continue;
                                bool vis = false; try { vis = r.enabled && !r.forceRenderingOff && r.gameObject.activeInHierarchy; } catch { }
                                if (vis) on++; else off++;
                            }
                            sb.Append("      renderers visible=").Append(on).Append("  hidden=").Append(off).Append('\n');
                        }
                        catch { }
                    }
                }
                if (shown == 0) sb.Append("  none on any tracked rifle\n");
                sb.Append("########## END MAGAZINE OBJECTS ##########");
                Log(sb.ToString());
            }
            catch (Exception e) { Log("mag object dump err: " + e.Message); }
        }

        static string Safe(Func<string> f) { try { return f() ?? "null"; } catch { return "<err>"; } }

        static bool _pouchBoxLogged;

        static bool Pre_PouchBox(Il2Cpp.MagManagerUI ui, int saveIndex,
                                 Il2Cpp.WeaponModInfo magMod, ref Il2Cpp.WeaponModInfo ammoMod)
        {
            try
            {
                if (magMod == null) return true;
                string mn; try { mn = magMod.Name; } catch { return true; }
                if (string.IsNullOrEmpty(mn) || !mn.StartsWith(Parts.Prefix, StringComparison.Ordinal)) return true;

                var mine = MagFix.OurAmmoInfo();
                if (mine == null) return true;
                ammoMod = mine;
                if (!_pouchBoxLogged) { _pouchBoxLogged = true; Log("pouch: showing our 12.7mm round on M107 magazine boxes"); }
            }
            catch { }
            return true;
        }

        static bool Pre_MagManagerOpen(Il2Cpp.MagManagerUI __instance)
        {
            try { MagFix.RepairPouch("pouch opened"); } catch { }
            return true;
        }

        static float _bigWarnAt;

        static void WarnBig(string tag, string s)
        {
            try
            {
                if (s == null || s.Length < 100_000) return;
                if (Time.time < _bigWarnAt) return;
                _bigWarnAt = Time.time + 5f;
                Log("STRING BOMB: " + tag + " is " + s.Length + " chars. head: " +
                    s.Substring(0, Math.Min(200, s.Length)));
            }
            catch { }
        }

        static readonly System.Collections.Generic.HashSet<int> _oursById = new System.Collections.Generic.HashSet<int>();
        static readonly System.Collections.Generic.HashSet<int> _oursLogged = new System.Collections.Generic.HashSet<int>();

        static bool IsOurPart(Il2Cpp.WeaponMod mod)
        {
            try
            {
                if (mod == null) return false;
                string n = mod.gameObject.name;
                return n != null && n.StartsWith(Parts.Prefix, StringComparison.Ordinal);
            }
            catch { return false; }
        }

        static void NoteOurs(Il2Cpp.WeaponMods wm)
        {
            try
            {
                if (wm == null) return;
                var go = wm.gameObject; if (go == null) return;
                int id = go.GetInstanceID();
                if (_oursById.Count > 256) _oursById.Clear();
                _oursById.Add(id);
            }
            catch { }
        }

        internal static bool IsOurs(Il2Cpp.WeaponMods wm)
        {
            try
            {
                if (wm == null) return false;
                GameObject go; try { go = wm.gameObject; } catch { return false; }
                if (go == null) return false;

                string n = null; try { n = go.name; } catch { }
                if (n != null && n.StartsWith(WeaponTemplate.ObjectName, StringComparison.Ordinal)) return true;

                int id; try { id = go.GetInstanceID(); } catch { return false; }
                if (_oursById.Contains(id)) return true;

                string j = null;
                try { j = wm.JSON_MOD_STRING; } catch { }
                if (j == null || j.IndexOf(Parts.Prefix, StringComparison.Ordinal) < 0)
                    try { j = wm.BASE_JSON_MOD_STRING; } catch { j = null; }
                if (j == null || j.IndexOf(Parts.Prefix, StringComparison.Ordinal) < 0) return false;

                if (_oursById.Count > 256) _oursById.Clear();
                _oursById.Add(id);
                if (_oursLogged.Add(id))
                    Log("CLIENT-SIDE BARRETT: '" + n + "' is not our template - Mirror rebuilt it from the " +
                        "donor's assetId - but it carries our parts, so it is ours. Hiding the donor and " +
                        "assembling the art over it.");
                return true;
            }
            catch { return false; }
        }

        static bool UnderArt(Transform t)
        {
            int g = 0;
            while (t != null && g++ < 40)
            {
                string n; try { n = t.name; } catch { return false; }
                if (n != null && (n.EndsWith("_art", StringComparison.Ordinal) ||
                                  n.StartsWith(ArtRootName, StringComparison.Ordinal))) return true;
                t = t.parent;
            }
            return false;
        }

        static void Post_RegisterMod(Il2Cpp.WeaponMods __instance, Il2Cpp.WeaponMod _mod)
        {
            try
            {
                if (_mod == null) return;

                if (IsOurPart(_mod)) NoteOurs(__instance);
                if (!IsOurs(__instance)) return;
                StormCheck(__instance);

                TrackLive(__instance.gameObject);

                Parts.FixMagazineSystem(_mod.gameObject);
                int n = HideUnder(_mod.gameObject);
                if (n > 0) Log("hid " + n + " renderer(s) on donor part '" + _mod.gameObject.name + "'");

                PinArt(__instance.gameObject, _mod.gameObject);

                Alignment.InvalidateRailChain();
            }
            catch { }
        }

        static void Pre_EyeSliderTable(Il2Cpp.WeaponModding __instance)
        {
            try { if (__instance != null && IsOurs(__instance.Weapon)) Alignment.EyeSliderMoved(); }
            catch { }
        }

        static void Pre_EyeSliderDirect(Il2Cpp.WeaponMods __instance)
        {
            try { if (IsOurs(__instance)) Alignment.EyeSliderMoved(); }
            catch { }
        }

        static bool Pre_MenuSetBaseMods(Il2Cpp.WeaponMods __instance, bool grab, ref string modstring)
        {
            try
            {
                WarnBig("MenuSetBaseMods modstring", modstring);
                if (!IsOurs(__instance) || !Parts.FullSet || string.IsNullOrEmpty(modstring)) return true;
                var fixedJson = Parts.RemapIds(modstring, out int changed);
                if (changed > 0)
                {
                    modstring = fixedJson;
                    Log("loadout: translated " + changed + " donor part id(s) from the stored build");
                }

                var mended = LoadoutRepair.Fix(modstring, out int filled);
                if (filled > 0) { modstring = mended; LoadoutRepair.Note(filled, "MenuSetBaseMods"); }
            }
            catch { }
            return true;
        }

        static bool Pre_ChangeString(Il2Cpp.WeaponMods __instance, ref string newString)
        {
            try
            {
                WarnBig("ChangeAttachmentString newString", newString);
                if (!IsOurs(__instance) || !Parts.FullSet || string.IsNullOrEmpty(newString)) return true;
                var fixedJson = Parts.RemapIds(newString, out int changed);
                if (changed > 0)
                {
                    newString = fixedJson;
                    Log("bench: translated " + changed + " donor part id(s) coming in from the saved build");
                }
            }
            catch { }
            return true;
        }

        static bool Pre_Rebuild(Il2Cpp.WeaponMods __instance)
        {
            try
            {
                if (!IsOurs(__instance) || !Parts.FullSet) return true;
                string json = null;
                try { json = __instance.JSON_MOD_STRING; } catch { }
                WarnBig("Rebuild JSON_MOD_STRING", json);
                if (string.IsNullOrEmpty(json)) return true;
                var fixedJson = Parts.RemapIds(json, out int changed);
                if (changed > 0)
                {
                    try { __instance.JSON_MOD_STRING = fixedJson; } catch { }
                    Log("save: translated " + changed + " donor part id(s) in the stored build to M107 parts");
                }
                var mended = LoadoutRepair.Fix(changed > 0 ? fixedJson : json, out int filled);
                if (filled > 0)
                {
                    try { __instance.JSON_MOD_STRING = mended; } catch { }
                    LoadoutRepair.Note(filled, "Rebuild");
                }
            }
            catch { }
            return true;
        }

        static void Post_Rebuild(Il2Cpp.WeaponMods __instance)
        {
            try
            {
                if (!IsOurs(__instance)) return;
                TrackLive(__instance.gameObject);
                int n = HideUnder(__instance.gameObject);
                if (n > 0) Log("rebuild sweep hid " + n + " donor renderer(s) on " + __instance.gameObject.name);

                PinAllArt(__instance.gameObject);
                Feel.FindBolt(__instance.gameObject);
            }
            catch { }
        }

        static bool PinAllArt(GameObject weapon)
        {
            long _p = Perf.Begin();
            bool bound = false;
            try
            {
                if (weapon == null) { Perf.End(Perf.PinArt, _p); return false; }
                var all = weapon.GetComponentsInChildren<Transform>(true);

                Transform anchor = null;
                Transform magPart = null;
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] == null) continue;
                    string nm; try { nm = all[i].name; } catch { continue; }
                    if (nm == null) continue;
                    if (anchor == null && nm == "Weapon") anchor = all[i];

                    if (magPart == null)
                    {
                        try { if (all[i].GetComponent<Il2Cpp.Magazine>() != null) magPart = all[i]; } catch { }
                    }
                    if (anchor != null && magPart != null) break;
                }

                for (int i = 0; i < all.Length; i++)
                {

                    if (all[i] == null) continue;

                    string nm; try { nm = all[i].name; } catch { continue; }
                    if (nm == null) continue;

                    if (nm.StartsWith(ArtRootName, StringComparison.Ordinal))
                    { Alignment.Register(all[i], false); continue; }

                    if (anchor != null && nm.EndsWith("_art", StringComparison.Ordinal))
                    {
                        Alignment.RegisterPartArt(all[i], anchor);

                        if (magPart != null &&
                            (nm.StartsWith(Parts.Prefix + "10rnd Magazine", StringComparison.Ordinal) ||
                             nm.StartsWith(Parts.Prefix + "12.7x99mm", StringComparison.Ordinal)))
                        {
                            MagFollow.Bind(all[i], magPart);
                            bound = true;
                        }
                    }
                }
            }
            catch { }
            Perf.End(Perf.PinArt, _p);
            return bound;
        }

        static Transform FindAnchor(GameObject weapon)
        {
            try
            {
                var all = weapon.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    string n; try { n = all[i].name; } catch { continue; }
                    if (n == "Weapon") return all[i];
                }
            }
            catch { }
            return null;
        }

        static void PinArt(GameObject weapon, GameObject part)
        {
            try
            {
                var anchor = FindAnchor(weapon);
                if (anchor == null) return;

                Transform magPart = null;
                try { if (part.GetComponentInChildren<Il2Cpp.Magazine>(true) != null) magPart = part.transform; } catch { }

                var all = part.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] == null) continue;
                    string n; try { n = all[i].name; } catch { continue; }
                    if (n == null || !n.EndsWith("_art", StringComparison.Ordinal)) continue;
                    Alignment.RegisterPartArt(all[i], anchor);
                    if (magPart != null) MagFollow.Bind(all[i], magPart);
                }
            }
            catch { }
        }

        static int HideUnder(GameObject root)
        {
            int hidden = 0;
            long _p = Perf.Begin();
            try
            {
                var rs = root.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rs.Length; i++)
                {
                    var r = rs[i]; if (r == null) continue;

                    if (UnderArt(r.transform))
                    {
                        try { if (r.forceRenderingOff) r.forceRenderingOff = false; } catch { }
                        continue;
                    }
                    if (!IsDonorGeometry(r.transform)) continue;

                    bool wasOn = false;
                    try { wasOn = r.enabled && !r.forceRenderingOff; } catch { }
                    try { r.enabled = false; r.forceRenderingOff = true; } catch { }
                    if (wasOn) hidden++;
                }
            }
            catch { }
            Perf.End(Perf.Hide, _p);
            return hidden;
        }

        static bool IsDonorGeometry(Transform t)
        {
            int g = 0;
            while (t != null && g++ < 40)
            {
                bool isPart = false;
                try { isPart = t.gameObject.GetComponent<Il2Cpp.WeaponMod>() != null; } catch { }
                if (isPart)
                {
                    string n; try { n = t.name; } catch { return false; }
                    return n != null && n.StartsWith(Parts.Prefix, StringComparison.Ordinal);
                }
                t = t.parent;
            }
            return true;
        }

        static void DumpVisible(GameObject weapon)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("\n=== VISIBLE RENDERERS on ").Append(weapon.name).Append(" ===\n");
                var rs = weapon.GetComponentsInChildren<Renderer>(true);
                int on = 0;
                for (int i = 0; i < rs.Length; i++)
                {
                    var r = rs[i]; if (r == null) continue;
                    bool drawing;
                    try { drawing = r.enabled && !r.forceRenderingOff && r.gameObject.activeInHierarchy; } catch { continue; }
                    if (!drawing) continue;
                    on++;
                    string path = "?"; try { path = Dump(r.transform); } catch { }
                    string mat = "?";
                    try { var m = r.sharedMaterial; mat = m == null ? "null" : m.name; } catch { }
                    sb.Append("  ").Append(UnderArt(r.transform) ? "[ours] " : "[DONOR] ")
                      .Append(path).Append("   mat=").Append(mat).Append('\n');
                }
                sb.Append("total drawing: ").Append(on).Append(" of ").Append(rs.Length).Append('\n');

                sb.Append("--- not drawing ---\n");
                int hid = 0;
                for (int i = 0; i < rs.Length; i++)
                {
                    var r = rs[i]; if (r == null) continue;
                    bool en = false, forced = false, act = false;
                    try { en = r.enabled; forced = r.forceRenderingOff; act = r.gameObject.activeInHierarchy; } catch { continue; }
                    if (en && !forced && act) continue;
                    if (++hid > 40) { sb.Append("  ...more\n"); break; }
                    string path = "?"; try { path = Dump(r.transform); } catch { }
                    sb.Append("  ").Append(IsDonorGeometry(r.transform) ? "[donor] " : "[FITTED] ")
                      .Append("enabled=").Append(en).Append(" forced=").Append(forced).Append(" active=").Append(act)
                      .Append("  ").Append(path).Append('\n');
                }
                Log(sb.ToString());
            }
            catch (Exception e) { Log("visible dump err: " + e.Message); }
        }

        static string Dump(Transform t)
        {
            var parts = new System.Collections.Generic.List<string>();
            int g = 0;
            while (t != null && g++ < 12) { parts.Add(t.name); t = t.parent; }
            parts.Reverse();
            return string.Join("/", parts);
        }

        static void Post_FireRound(Il2Cpp.BulletShell __instance)
        {
            try
            {
                if (__instance == null) return;
                var t = __instance.transform;
                int g = 0;
                while (t != null && g++ < 24)
                {
                    string n; try { n = t.name; } catch { break; }
                    bool mine = n != null && (n.StartsWith(WeaponTemplate.ObjectName, StringComparison.Ordinal) ||
                                              n.StartsWith(Parts.Prefix, StringComparison.Ordinal));
                    if (!mine)
                        try { var wm0 = t.gameObject.GetComponent<Il2Cpp.WeaponMods>(); if (wm0 != null) mine = IsOurs(wm0); }
                        catch { }
                    if (mine)
                    {

                        Parts.EnsureRoundVisuals(__instance);
                        try
                        {
                            if (Parts.ShellMesh != null) __instance.Mesh_Shell = Parts.ShellMesh;
                            if (Parts.BulletMesh != null) __instance.Mesh_Bullet = Parts.BulletMesh;
                        }
                        catch { }
                        Feel.OnFired(t.gameObject);
                        return;
                    }
                    t = t.parent;
                }
            }
            catch { }
        }

        static void Post_HookTaken(Il2Cpp.N_StaticWeaponSpawner __instance, Il2Cpp.PlayerNetworking playerNetworking)
        {
            try
            {
                if (__instance == null) return;
                if (!__instance.gameObject.name.StartsWith(RackHook.HookName, StringComparison.Ordinal)) return;
                Log("our hook was used. WeaponRef=" +
                    (__instance.WeaponReference == null ? "null" : __instance.WeaponReference.name) +
                    "  registeredIndex=" + WeaponTemplate.RegisteredIndex);
            }
            catch { }
        }

        static int _blocked;
        static bool Pre_TryInitAssetRef(Il2Cpp.WeaponMod __instance)
        {
            try
            {
                if (__instance == null) return true;
                string n; try { n = __instance.gameObject.name; } catch { return true; }
                if (n == null || !n.StartsWith(Parts.Prefix, StringComparison.Ordinal)) return true;
                if (++_blocked <= 12) Log("blocked donor asset load on '" + n + "'");
                return false;
            }
            catch { }
            return true;
        }

        static bool Pre_GetWeaponMod(int index, ref GameObject __result)
        {
            try
            {
                if (Parts.TryGetPrefab(index, out var go) && go != null) { __result = go; return false; }
            }
            catch { }
            return true;
        }

        static bool Pre_GetAttachment(string attachment, ref GameObject __result)
        {
            try
            {
                if (!string.IsNullOrEmpty(attachment) && Parts.TryGetId(attachment, out int id)
                    && Parts.TryGetPrefab(id, out var go) && go != null)
                { __result = go; return false; }
            }
            catch { }
            return true;
        }

        static bool Pre_FindAttachment(string attachment, ref int __result)
        {
            try
            {
                if (!string.IsNullOrEmpty(attachment) && Parts.TryGetId(attachment, out int id))
                { __result = id; return false; }
            }
            catch { }
            return true;
        }

        static readonly StringBuilder _logBuf = new StringBuilder(8192);
        static float _logFlushAt;

        internal static void Log(string s)
        {
            try
            {
                lock (_logBuf)
                {
                    _logBuf.Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append("  ").Append(s).Append('\n');
                    if (_logBuf.Length > 16384) FlushLogLocked();
                }
            }
            catch { }
            try { MelonLogger.Msg("[M107] " + s); } catch { }
        }

        static void FlushLogLocked()
        {
            try
            {
                if (_logBuf.Length == 0) return;
                File.AppendAllText(LogPath, _logBuf.ToString());
                _logBuf.Length = 0;
            }
            catch { _logBuf.Length = 0; }
        }

        internal static void FlushLog()
        {
            try { lock (_logBuf) FlushLogLocked(); } catch { }
        }
    }
}
