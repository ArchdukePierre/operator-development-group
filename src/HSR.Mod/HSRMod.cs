// ODG Operations - HSR
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Text;
using HarmonyLib;
using MelonLoader;
using Il2CppInterop.Runtime;
using UnityEngine;
using UObject = UnityEngine.Object;

[assembly: MelonInfo(typeof(HSR.HSRMod), "HSR Golden Experiment", "0.1.0", "ArchdukePierre")]
[assembly: MelonGame(null, null)]

namespace HSR
{

    internal static class HsrPaths
    {
        internal static string GameRoot
        {
            get
            {
                try { return MelonLoader.Utils.MelonEnvironment.GameRootDirectory; }
                catch { return AppContext.BaseDirectory; }
            }
        }
        internal static string AssetsDir => Path.Combine(GameRoot, "Mods", "HSR_Assets");
        internal static string DumpDir => Path.Combine(GameRoot, "UserData", "HSR_dumps");
    }

    public class HSRMod : MelonMod
    {
        static string DumpDir => HsrPaths.DumpDir;

        MelonPreferences_Entry<bool> _enableDump;
        MelonPreferences_Entry<bool> _enableClone;
        MelonPreferences_Entry<int> _rotPreset;
        MelonPreferences_Entry<float> _posX, _posY, _posZ, _fineYaw, _finePitch;
        MelonPreferences_Entry<float> _scopeRise;
        MelonPreferences_Entry<float> _scopeX, _scopeZ;
        MelonPreferences_Entry<float> _eyeX, _eyeRise, _eyeZ;
        Vector3 _eyeApplied;
        MelonPreferences_Entry<int> _alignFormat;
        MelonPreferences_Entry<bool> _tuningKeys;

#if ODG_PUBLIC
        const bool HsrDevKeys = false;
#else
        const bool HsrDevKeys = true;
#endif
        MelonPreferences_Entry<bool> _leakTracker;
        MelonPreferences_Entry<bool> _saveBuild;
        static MelonPreferences_Entry<bool> _disableWeight;

        AssetBundle _bundle;
        GameObject _barrettPrefab;
        int _dressScanFrames;
        int _patchThrottle;
        int _rackThrottle;
        bool _bundleTried;
        bool _weightZeroedThisScene;
        bool _r700Dumped;
        Il2Cpp.MasterController _master;

        internal const string WeaponName = "M200 Intervention";
        internal const string WeaponPrefix = "M200";

        static bool IsOurWeapon(GameObject go)
        {
            if (go == null) return false;
            var n = go.name;
            return n != null && n.StartsWith(WeaponPrefix, StringComparison.Ordinal);
        }

        static string BundlePath => Path.Combine(HsrPaths.AssetsDir, "hsr_weapon_parts");

        internal static HSRMod Instance;

        static void Pre_ModUIInit(Il2Cpp.ModUI __instance)
        {
            try { if (Instance != null) MagBox.FilterList(__instance, Instance.LoggerInstance); }
            catch { }
        }

        static void Post_ModUIInit(Il2Cpp.ModUI __instance)
        {
            try { if (Instance != null) BenchList.NoteBox(__instance, Instance.LoggerInstance); }
            catch { }

            try { if (Instance != null) MagBox.Note(__instance, Instance.LoggerInstance); }
            catch { }

            try { if (Instance != null) Instance.DressFromBench(__instance); } catch { }
        }

        static void Post_DischargeRound(Il2Cpp.WeaponV3 __instance)
        {
            try
            {
                if (Instance == null) return;
                if (__instance == null || !IsOurWeapon(__instance.gameObject)) return;

                var bolt = FindOurBolt(__instance);
                if (bolt == null) return;

                BoltCycle.OnShot(Instance.LoggerInstance);
                if (BoltCycle.CycleOnShot) BoltCycle.StartCycle(bolt);

                if (++_shotsSeen == 4 && _rackSeen.Count == 0)
                    Instance.LoggerInstance.Warning(
                        "bolt: four rounds fired and neither ManualChamber() nor RPC_ManualChamber() "
                      + "has been reached, so nothing is driving the bolt. Set BoltCycleOnShot=true "
                      + "under [HSR] to put the cycle back on the shot while this is sorted out.");
            }
            catch { }
        }

        static readonly System.Collections.Generic.HashSet<string> _rackSeen =
            new System.Collections.Generic.HashSet<string>();
        static int _shotsSeen;
        static int _reloadHookReports;
        static bool _fellBackOnState;

        static void RackEvent(Il2Cpp.WeaponV3 w, string via)
        {
            try
            {
                if (Instance == null) return;
                if (w == null || !IsOurWeapon(w.gameObject)) return;
                if (_rackSeen.Add(via))
                    Instance.LoggerInstance.Msg("bolt: manual rack reached us via {0}() — driving the cycle from it.", via);

                if (_rackClipLen <= 0f && !_fellBackOnState && !BoltCycle.AnyRiding)
                {
                    _fellBackOnState = true;
                    float s = CurrentStateSeconds(w);
                    if (s > 0f)
                        BoltCycle.SetCycleSeconds(s, "the animator state playing at the rack",
                                                  Instance.LoggerInstance);
                    else
                        Instance.LoggerInstance.Warning(
                            "bolt: could not read a rack duration from the animator. The cycle stays "
                          + "at its default; set BoltCycleSeconds under [HSR] to match the hand by eye.");
                }

                var boltForProbe = FindOurBolt(w);
                RackProbe.Arm(w.transform, boltForProbe, Instance.LoggerInstance);

                var bolt = boltForProbe;
                if (bolt != null) BoltCycle.StartCycle(bolt);
            }
            catch { }
        }

        static void Post_Reload(Il2Cpp.WeaponV3 __instance)
        {
            try { MagProbe.Event("RPC_Reload " + (__instance == null ? "?" : __instance.gameObject.name)); } catch { }
            try
            {
                if (Instance == null || __instance == null) return;
                if (!IsOurWeapon(__instance.gameObject)) return;
                if (_reloadHookReports++ < 4)
                    Instance.LoggerInstance.Msg("reload: UserCode_RPC_Reload fired on '{0}'.", __instance.gameObject.name);
                MelonCoroutines.Start(Instance.RehideSoon(__instance));
            }
            catch { }
        }

        static int _reloadStateReports;
        static void Post_ReloadState(Il2Cpp.WeaponV3 __instance, bool __0)
        {
            try { MagProbe.Event("SetReloadState " + __0); } catch { }
            try
            {
                if (Instance == null || __instance == null) return;
                if (!IsOurWeapon(__instance.gameObject)) return;
                if (_reloadStateReports++ < 8)
                    Instance.LoggerInstance.Msg("reload: SetReloadState({0}) on '{1}'.", __0, __instance.gameObject.name);
                Instance._wdFrame = 118;
                MelonCoroutines.Start(Instance.RehideSoon(__instance));
            }
            catch { }
        }

        static void Post_ManualChamber(Il2Cpp.WeaponV3 __instance) { RackEvent(__instance, "ManualChamber"); }
        static void Post_RpcManualChamber(Il2Cpp.WeaponV3 __instance) { RackEvent(__instance, "RPC_ManualChamber"); }

        static string _rackClip;
        static float _rackClipLen;
        readonly System.Collections.Generic.HashSet<string> _animCensus =
            new System.Collections.Generic.HashSet<string>();

        static void Post_WeaponPlayAnimation(Il2Cpp.WeaponV3 __instance, string __0)
        {
            try { MagProbe.Event("anim:" + __0); } catch { }
            try
            {
                if (Instance == null || __instance == null) return;
                if (string.IsNullOrEmpty(__0)) return;
                if (!IsOurWeapon(__instance.gameObject)) return;
                Instance.NoteWeaponAnim(__instance, __0);
            }
            catch { }
        }

        void NoteWeaponAnim(Il2Cpp.WeaponV3 w, string clip)
        {
            float len = ClipLength(w, clip);
            if (_animCensus.Add(clip))
                LoggerInstance.Msg("  weapon anim '{0}' length {1}", clip,
                    len > 0f ? len.ToString("F3") + "s" : "unresolved");
            if (len <= 0f) return;

            var k = BenchList.Norm(clip);
            if (k.IndexOf("BOLT", StringComparison.Ordinal) < 0 &&
                k.IndexOf("RACK", StringComparison.Ordinal) < 0 &&
                k.IndexOf("CHAMBER", StringComparison.Ordinal) < 0 &&
                k.IndexOf("CYCLE", StringComparison.Ordinal) < 0) return;

            _rackClip = clip;
            _rackClipLen = len;
            BoltCycle.SetCycleSeconds(len, "'" + clip + "'", LoggerInstance);
        }

        static float ClipLength(Il2Cpp.WeaponV3 w, string clip)
        {
            try
            {
                var anim = w.GetComponentInChildren<Animator>(true);
                if (anim == null) return 0f;
                var rac = anim.runtimeAnimatorController;
                if (rac == null) return 0f;
                var clips = rac.animationClips;
                if (clips == null) return 0f;
                for (int i = 0; i < clips.Length; i++)
                {
                    var c = clips[i];
                    if (c == null) continue;
                    if (!string.Equals(c.name, clip, StringComparison.OrdinalIgnoreCase)) continue;
                    float len = c.length;
                    float sp = 1f;
                    try { sp = anim.speed; } catch { }
                    if (sp > 0.01f) len /= sp;
                    return len;
                }
            }
            catch { }
            return 0f;
        }

        static float CurrentStateSeconds(Il2Cpp.WeaponV3 w)
        {
            try
            {
                var anim = w.GetComponentInChildren<Animator>(true);
                if (anim == null) return 0f;
                var st = anim.GetCurrentAnimatorStateInfo(0);
                float len = st.length;
                float sp = 1f;
                try { sp = anim.speed; } catch { }
                if (sp > 0.01f) len /= sp;
                return len;
            }
            catch { return 0f; }
        }

        static void Post_PouchBox(Il2Cpp.MagManagerMagUI __instance)
        {
            try { if (Instance != null) MagBox.PouchBox(__instance, Instance.LoggerInstance); }
            catch { }
        }

        static void Pre_CloneThisMag(Il2Cpp.MagManagerMagUI __instance)
        {
            try { if (Instance != null) PouchNames.BeforeClone(__instance, Instance.LoggerInstance); }
            catch { }
        }

        static void Pre_InitMagManager()
        {
            try { if (Instance != null) PouchNames.RepairPouch("pouch opened", Instance.LoggerInstance); }
            catch { }
        }

        public override void OnLateUpdate()
        {
            long _t = Perf.Begin();
            try { BenchList.HoldHidden(); } catch { }
            try { RackProbe.Tick(); } catch { }
            try { SettleSockets(); } catch { }

            try { ApplyScope(); } catch { }
            try { FastMagHold(); } catch { }
            try { MagWatchdog(); } catch { }
            try { MagBox.Tick(LoggerInstance); } catch { }

            try { MagFollow.Tick(); } catch { }

            try { MagProbe.Tick(ProbeRoots(), RootIsMine, IsHeldMagazine, LoggerInstance); } catch { }
            try { BoltCycle.Tick(LoggerInstance); } catch { }
            try { BoltCycle.TickProcedural(); } catch { }
            Perf.End(Perf.LateUpdate, _t);
        }

        static bool SpawnWeaponPrefix(Il2Cpp.N_StaticWeaponSpawner __instance, Il2Cpp.PlayerNetworking playerNetworking)
        {
            try
            {
                var wr = __instance.WeaponReference;
                bool isOurs = Instance != null && Instance._hsrClone != null && wr == Instance._hsrClone;
                if (!isOurs) return true;

                MelonLogger.Msg("[TAKE] HSR hook used — mod-handled spawn.");
                try { MagProbe.Event("TAKE"); } catch { }
                Instance.HandOutHSR(__instance, playerNetworking);
                return false;
            }
            catch (Exception e) { MelonLogger.Error("[TAKE] prefix failed: " + e); return true; }
        }

        static Il2Cpp.PlayerNetworking.WeaponSlot FreeWeaponSlot(Il2Cpp.PlayerNetworking p)
        {
            var Primary = Il2Cpp.PlayerNetworking.WeaponSlot.Primary;
            var SecondPrimary = Il2Cpp.PlayerNetworking.WeaponSlot.SecondPrimary;
            try
            {
                int prim = 0, second = 0;
                try { prim = p.NetworkPrimaryWeaponID; } catch { }
                try { second = p.NetworkSecondPrimaryWeaponID; } catch { }

                if (prim <= 0) return Primary;
                if (second <= 0) return SecondPrimary;

                var held = Il2Cpp.PlayerNetworking.WeaponSlot.None;
                try { held = p.NetworkCurrentWeaponSlot; } catch { }
                if (held == Primary || held == SecondPrimary) return held;
            }
            catch { }
            return Primary;
        }

        void HandOutHSR(Il2Cpp.N_StaticWeaponSpawner hook, Il2Cpp.PlayerNetworking player)
        {
            try
            {
                if (player == null) { LoggerInstance.Warning("[TAKE] no player."); return; }

                string modJson = "";
                try
                {
                    var customBuild = Path.Combine(HsrPaths.AssetsDir, "spawn_build.json");
                    if (File.Exists(customBuild)) modJson = File.ReadAllText(customBuild).Trim();
                }
                catch { }
                if (string.IsNullOrEmpty(modJson))
                    try
                    {
                        var wm = _hsrClone.GetComponent<Il2Cpp.WeaponMods>() ?? _hsrClone.GetComponentInChildren<Il2Cpp.WeaponMods>(true);
                        if (wm != null) modJson = wm.BASE_JSON_MOD_STRING ?? "";
                    }
                    catch { }

                var slot = FreeWeaponSlot(player);
                player.SpawnWeapon(WeaponName, modJson, slot);

                LoggerInstance.Msg("[TAKE] PlayerNetworking.SpawnWeapon(\"{0}\", <build:{1} chars>, {2}) called — the game handles the rest.",
                                   WeaponName, modJson.Length, slot);
                MelonCoroutines.Start(PostTakeCensus());

                MelonCoroutines.Start(HideFromPickup(player));
            }
            catch (Exception e) { LoggerInstance.Error("HandOutHSR: " + e); }
        }

        static void Post_WeaponHandedOver(Il2Cpp.PlayerNetworking __instance)
        {
            try
            {
                if (Instance == null || __instance == null) return;
                MelonCoroutines.Start(Instance.HideFromPickup(__instance));
            }
            catch { }
        }

        static void RebuildPostfix(Il2Cpp.WeaponMods __instance)
        {
            try
            {
                if (Instance == null || __instance == null) return;
                var w = __instance.Weapon;
                if (w == null || w.gameObject == null || !IsOurWeapon(w.gameObject)) return;
                MelonCoroutines.Start(Instance.RehideSoon(w));

                var json = CurrentBuildOf(__instance);
                if (!string.IsNullOrEmpty(json)) Instance.RememberBuild(json);
            }
            catch { }
        }

        static string CurrentBuildOf(Il2Cpp.WeaponMods wm)
        {
            string base_ = null;
            try { base_ = wm.BASE_JSON_MOD_STRING; } catch { }
            try
            {
                var props = wm.GetType().GetProperties(
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                for (int i = 0; i < props.Length; i++)
                {
                    if (props[i].PropertyType != typeof(string)) continue;
                    if (props[i].GetIndexParameters().Length != 0) continue;
                    string v = null;
                    try { v = props[i].GetValue(wm) as string; } catch { continue; }
                    if (string.IsNullOrEmpty(v) || v.Length < 32) continue;
                    if (v[0] != '{' && v[0] != '[') continue;
                    if (v.IndexOf("WeaponModName", StringComparison.Ordinal) < 0) continue;
                    if (base_ != null && v == base_) continue;
                    return v;
                }
            }
            catch { }
            return null;
        }

        string _lastBuildJson;

        void RememberBuild(string json)
        {
            try
            {
                if (_saveBuild == null || !_saveBuild.Value) return;
                if (string.IsNullOrEmpty(json) || json.Length < 32) return;
                if (json == _lastBuildJson) return;
                _lastBuildJson = json;
                var path = Path.Combine(HsrPaths.AssetsDir, "spawn_build.json");
                File.WriteAllText(path, json);
                LoggerInstance.Msg("BUILD: saved as the M200's default loadout ({0} chars). "
                                 + "The rack will hand this exact build out from now on.", json.Length);
            }
            catch (Exception e) { LoggerInstance.Warning("RememberBuild: " + e.Message); }
        }

        void DressFromBench(Il2Cpp.ModUI ui)
        {
            try
            {
                if (ui == null) return;
                Transform t = null;
                try { var p = ui.modParent; if (p != null) t = p.transform; } catch { }
                if (t == null) { try { var wm = ui.weaponModding; if (wm != null) t = wm.transform; } catch { } }
                if (t == null) return;

                Il2Cpp.WeaponV3 w = null;
                int g = 0;
                while (t != null && g++ < 40)
                {
                    var c = t.GetComponent<Il2Cpp.WeaponV3>();
                    if (c != null) { w = c; break; }
                    t = t.parent;
                }
                if (w == null || w.gameObject == null) return;
                if (!IsOurWeapon(w.gameObject)) return;
                if (w.gameObject == _hsrClone) return;

                HideDonorNow(w);

                int id = w.gameObject.GetInstanceID();
                Transform body;
                bool have = false;
                if (_bodyOf.TryGetValue(id, out body))
                { try { have = body != null && body.gameObject.activeInHierarchy; } catch { } }
                if (have) return;

                DressAsBarrett(w);
                _bodyOf[id] = FindOurBody(w);
            }
            catch { }
        }

        static readonly System.Collections.Generic.Dictionary<int, float> _hideDeadline
            = new System.Collections.Generic.Dictionary<int, float>();
        static readonly System.Collections.Generic.HashSet<int> _hideRunning
            = new System.Collections.Generic.HashSet<int>();

        System.Collections.IEnumerator HideFromPickup(Il2Cpp.PlayerNetworking player)
        {
            if (player == null) yield break;

            int key = 0;
            bool keyed = false;
            try { key = player.GetInstanceID(); keyed = true; } catch { }
            if (!keyed) yield break;

            _hideDeadline[key] = Time.time + 3.0f;
            if (_hideRunning.Contains(key))
            {

                yield break;
            }
            _hideRunning.Add(key);

            int frame = 0;
            try
            {
            while (Time.time < DeadlineFor(key))
            {
                if (player == null) yield break;
                Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<Il2Cpp.WeaponV3> ws = null;
                try { ws = player.transform.GetComponentsInChildren<Il2Cpp.WeaponV3>(true); }
                catch { }
                if (ws != null)
                    for (int i = 0; i < ws.Length; i++)
                    {
                        var w = ws[i];
                        if (w == null || w.gameObject == null) continue;
                        if (!IsOurWeapon(w.gameObject)) continue;
                        HideDonorNow(w);

                        if (frame % 10 == 0 && FindOurBody(w) == null)
                        {
                            DressAsBarrett(w);
                            try { _bodyOf[w.gameObject.GetInstanceID()] = FindOurBody(w); } catch { }
                        }
                    }
                frame++;
                yield return null;
            }
            }
            finally
            {

                _hideRunning.Remove(key);
                _hideDeadline.Remove(key);
            }
        }

        static float DeadlineFor(int key)
        {
            float d;
            return _hideDeadline.TryGetValue(key, out d) ? d : 0f;
        }

        System.Collections.IEnumerator RehideSoon(Il2Cpp.WeaponV3 w)
        {

            float until = Time.time + 2.0f;
            while (Time.time < until)
            {
                HideDonorNow(w);
                yield return null;
            }
            DressAsBarrett(w);
        }

        readonly System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<Renderer>> _hidByUs =
            new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<Renderer>>();
        int _restoreReports;

        void RestoreDonorNow(int id, string why)
        {
            System.Collections.Generic.List<Renderer> list;
            if (!_hidByUs.TryGetValue(id, out list) || list == null) return;
            int back = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var r = list[i];
                if (r == null) continue;
                try { if (!r.enabled) { r.enabled = true; back++; } } catch { }
            }
            list.Clear();
            if (back > 0 && _restoreReports++ < 6)
                LoggerInstance.Msg("BODY: put {0} donor renderer(s) back on a rifle with no body of ours ({1}). "
                                 + "A whole R700 beats a floating scope.", back, why);
        }

        void HideDonorNow(Il2Cpp.WeaponV3 w)
        {
            try { MagProbe.Event("HideDonorNow " + (w == null ? "?" : w.gameObject.name)); } catch { }
            long _t = Perf.Begin();
            try
            {
                if (w == null || w.gameObject == null) return;
                int wid = 0; try { wid = w.gameObject.GetInstanceID(); } catch { }
                System.Collections.Generic.List<Renderer> hid;
                if (!_hidByUs.TryGetValue(wid, out hid) || hid == null)
                { hid = new System.Collections.Generic.List<Renderer>(); _hidByUs[wid] = hid; }
                var rs = w.transform.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rs.Length; i++)
                {
                    var rend = rs[i];
                    if (rend == null || !rend.enabled) continue;
                    if (rend.TryCast<ParticleSystemRenderer>() != null) continue;
                    if (rend.TryCast<TrailRenderer>() != null) continue;
                    var t = rend.transform;
                    if (IsOurGroup(t)) continue;
                    if (OpticToken(rend.gameObject.name)) continue;
                    bool underKeep = false;
                    var p = t;
                    int g = 0;
                    while (p != null && g++ < 12)
                    {
                        if (OpticToken(p.gameObject.name)) { underKeep = true; break; }
                        p = p.parent;
                    }
                    if (underKeep) continue;
                    rend.enabled = false;
                    if (hid.Count < 256) hid.Add(rend);
                }
            }
            catch { }
            finally { Perf.End(Perf.Hide, _t); }
        }

        static Quaternion RotPreset(int i)
        {
            switch (((i % 6) + 6) % 6)
            {
                case 1: return Quaternion.Euler(0, 90, 0);
                case 2: return Quaternion.Euler(0, 180, 0);
                case 3: return Quaternion.Euler(0, 270, 0);
                case 4: return Quaternion.Euler(90, 0, 0);
                case 5: return Quaternion.Euler(-90, 0, 0);
                default: return Quaternion.identity;
            }
        }

        readonly System.Collections.Generic.HashSet<string> _visibleCensus =
            new System.Collections.Generic.HashSet<string>();

        static string PathUp(Transform t, int depth)
        {
            try
            {
                var sb = new System.Text.StringBuilder(t.gameObject.name);
                var p = t.parent; int g = 0;
                while (p != null && g++ < depth) { sb.Insert(0, p.gameObject.name + "/"); p = p.parent; }
                return sb.ToString();
            }
            catch { return "?"; }
        }

        bool _dumpedThisScene;
        bool _cloneRegistered;
        bool _menuPatchedThisScene;
        bool _rackPatchedThisScene;
        bool _inputBroken;
        string _sceneName = "";
        GameObject _hsrClone;
        GameObject _templateHolder;
        GameObject _lastSpawned;

        int _patchesOk, _patchesFailed;
        void One(string what, Action apply)
        {
            try { apply(); _patchesOk++; }
            catch (Exception e)
            {
                _patchesFailed++;
                LoggerInstance.Error("PATCH FAILED: " + what + " is now dead this run — " + e.Message);
            }
        }

        public override void OnInitializeMelon()
        {
            Instance = this;
            var cat = MelonPreferences.CreateCategory("HSR");
            _enableDump = cat.CreateEntry("EnableDump", false);
            _enableClone = cat.CreateEntry("EnableClone", true);

            _rotPreset = cat.CreateEntry("BodyRotPreset", 2);
            _posX = cat.CreateEntry("BodyPosX", -0.0025f);
            _posY = cat.CreateEntry("BodyPosY", 0.0115f);
            _posZ = cat.CreateEntry("BodyPosZ", 0.680f);
            _fineYaw = cat.CreateEntry("BodyFineYaw", 0f);
            _finePitch = cat.CreateEntry("BodyFinePitch", 0f);

            _scopeRise = cat.CreateEntry("ScopeSocketRise", 0.0345f);

            _scopeX = cat.CreateEntry("ScopeSocketX", 0f);
            _scopeZ = cat.CreateEntry("ScopeSocketZ", 0.020f);

            _eyeX = cat.CreateEntry("EyeOffsetX", 0f);
            _eyeRise = cat.CreateEntry("EyeOffsetRise", 0.035f);
            _eyeZ = cat.CreateEntry("EyeOffsetZ", 0.010f);

            _tuningKeys = cat.CreateEntry("EnableTuningKeysV2", false);
            _leakTracker = cat.CreateEntry("EnableLeakTracker", false);

            _saveBuild = cat.CreateEntry("SaveBenchBuildAsDefault", true);
            AutoFire.Init(cat);
            BoltCycle.Init(cat);
            RackProbe.Init(cat);
            ObjBuilder.Init(cat);
            WeaponAudio.Init(cat);
            MagSkin.Init(cat);
            MagLabel.Init(cat);

            _alignFormat = cat.CreateEntry("AlignFormat", 1);
            if (_alignFormat.Value < 5)
            {
                _rotPreset.Value = 2;
                _posX.Value = -0.0025f; _posY.Value = 0.0115f; _posZ.Value = 0.680f;
                _fineYaw.Value = 0f; _finePitch.Value = 0f;
                _scopeX.Value = 0f; _scopeRise.Value = 0.0345f; _scopeZ.Value = 0.020f;
                _eyeX.Value = 0f; _eyeRise.Value = 0.035f; _eyeZ.Value = 0.010f;
                _alignFormat.Value = 5;
                MelonPreferences.Save();
                RecordAlignment("migrated to format 5 (M200 ADS mark)");
                LoggerInstance.Msg("ALIGN: saved pose set to the M200 Intervention ADS mark.");
            }

            _disableWeight = cat.CreateEntry("DisableWeightSystem", true);
            Directory.CreateDirectory(DumpDir);
            try
            {

                One("rack spawn", () => HarmonyInstance.Patch(
                    AccessTools.Method(typeof(Il2Cpp.N_StaticWeaponSpawner), "SpawnWeapon"),
                    prefix: new HarmonyMethod(typeof(HSRMod), nameof(SpawnWeaponPrefix))));
                One("modding rebuild", () => HarmonyInstance.Patch(
                    AccessTools.Method(typeof(Il2Cpp.WeaponMods), "RebuildWeaponWithModString"),
                    postfix: new HarmonyMethod(typeof(HSRMod), nameof(RebuildPostfix))));

                One("network rebuild", () => HarmonyInstance.Patch(
                    AccessTools.Method(typeof(Il2Cpp.WeaponMods), "RebuildWeaponWithNetworkPreset"),
                    postfix: new HarmonyMethod(typeof(HSRMod), nameof(RebuildPostfix))));

                One("bench boxes", () => HarmonyInstance.Patch(
                    AccessTools.Method(typeof(Il2Cpp.ModUI), "Init"),
                    prefix: new HarmonyMethod(typeof(HSRMod), nameof(Pre_ModUIInit)),
                    postfix: new HarmonyMethod(typeof(HSRMod), nameof(Post_ModUIInit))));

                One("bolt cycle", () => HarmonyInstance.Patch(
                    AccessTools.Method(typeof(Il2Cpp.WeaponV3), "DischargeRound"),
                    postfix: new HarmonyMethod(typeof(HSRMod), nameof(Post_DischargeRound))));

                One("bolt rack", () => HarmonyInstance.Patch(
                    AccessTools.Method(typeof(Il2Cpp.WeaponV3), "ManualChamber"),
                    postfix: new HarmonyMethod(typeof(HSRMod), nameof(Post_ManualChamber))));
                One("bolt rack (rpc)", () => HarmonyInstance.Patch(
                    AccessTools.Method(typeof(Il2Cpp.WeaponV3), "UserCode_RPC_ManualChamber"),
                    postfix: new HarmonyMethod(typeof(HSRMod), nameof(Post_RpcManualChamber))));

                One("reload recover", () => HarmonyInstance.Patch(
                    AccessTools.Method(typeof(Il2Cpp.WeaponV3), "UserCode_RPC_Reload"),
                    postfix: new HarmonyMethod(typeof(HSRMod), nameof(Post_Reload))));
                One("reload state", () => HarmonyInstance.Patch(
                    AccessTools.Method(typeof(Il2Cpp.WeaponV3), "UserCode_RPC_SetReloadState__Boolean"),
                    postfix: new HarmonyMethod(typeof(HSRMod), nameof(Post_ReloadState))));
                One("anim timing", () => HarmonyInstance.Patch(
                    AccessTools.Method(typeof(Il2Cpp.WeaponV3), "WeaponPlayAnimation",
                        new Type[] { typeof(string), typeof(float) }),
                    postfix: new HarmonyMethod(typeof(HSRMod), nameof(Post_WeaponPlayAnimation))));

                One("mag pouch", () => HarmonyInstance.Patch(
                    AccessTools.Method(typeof(Il2Cpp.MagManagerMagUI), "InitializeThisInstance"),
                    postfix: new HarmonyMethod(typeof(HSRMod), nameof(Post_PouchBox))));

                One("pouch clone", () => HarmonyInstance.Patch(
                    AccessTools.Method(typeof(Il2Cpp.MagManagerMagUI), "CloneThisMag"),
                    prefix: new HarmonyMethod(typeof(HSRMod), nameof(Pre_CloneThisMag))));
                One("pouch repair", () => HarmonyInstance.Patch(
                    AccessTools.Method(typeof(Il2Cpp.MagManagerUI), "InitializeMagManager"),
                    prefix: new HarmonyMethod(typeof(HSRMod), nameof(Pre_InitMagManager))));

                One("client prefabs", () => HarmonyInstance.Patch(
                    AccessTools.Method(typeof(Il2Cpp.NetworkManagerVectorGames), "OnStartClient"),
                    postfix: new HarmonyMethod(typeof(HSRMod), nameof(Post_StartClient))));

                foreach (var sig in new[]
                {
                    new object[] { "SpawnWeapon",    new Type[] { typeof(string), typeof(string), typeof(Il2Cpp.PlayerNetworking.WeaponSlot) } },
                    new object[] { "SpawnWeapon",    new Type[] { typeof(UnityEngine.GameObject), typeof(string) } },
                    new object[] { "TryPickupWeapon", new Type[] { typeof(int), typeof(Il2Cpp.PlayerNetworking.WeaponSlot) } },
                })
                {
                    var nm = (string)sig[0];
                    try
                    {
                        var mi = AccessTools.Method(typeof(Il2Cpp.PlayerNetworking), nm, (Type[])sig[1]);
                        if (mi == null) { LoggerInstance.Warning("no PlayerNetworking." + nm + " with that signature"); continue; }
                        HarmonyInstance.Patch(mi, postfix: new HarmonyMethod(typeof(HSRMod), nameof(Post_WeaponHandedOver)));
                    }
                    catch (Exception e) { LoggerInstance.Warning("could not hook " + nm + ": " + e.Message); }
                }

                if (_patchesFailed == 0)
                    LoggerInstance.Msg("Patched: {0} hooks live (rack spawn, modding rebuild, network rebuild, "
                                     + "bench boxes, bolt cycle, mag pouch, pouch clone, pouch repair, client prefabs, pickup blanking).",
                                     _patchesOk);
                else
                    LoggerInstance.Error("Patched {0} hooks, {1} FAILED — the features named above "
                                       + "are not running this session.", _patchesOk, _patchesFailed);
            }
            catch (Exception pe) { LoggerInstance.Error("Harmony patch failed: " + pe.Message); }
            LoggerInstance.Msg("=== HSR mod loaded. Dump={0} Clone={1} (bundle loads at first scene) ===",
                _enableDump.Value, _enableClone.Value);
            LoggerInstance.Msg("Hotkeys: F9 dump · F11 spawn HSR at feet · F8 rebuild parts · F7 rack retry · F6 cycle body rotation");
        }

        public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
        {
            try { Leak.Suspend(); } catch { }
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            try { Leak.Suspend(); } catch { }
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            _sceneName = sceneName;
            _dumpedThisScene = false;
            _menuPatchedThisScene = false;
            _rackPatchedThisScene = false;
            _rackMounted = false;
            RackHook.Reset();
            BenchList.ResetForScene();
            MagBox.ResetForScene();
            MagFollow.Reset();
            MagSkin.ResetForScene();
            MagLabel.ResetForScene();
            PouchNames.ResetForScene();
            MagProbe.ResetForScene();
            _magDumped = false;
            _dressedIds.Clear();
            _bodyOf.Clear();
            _weightStep = 0;
            _fullScanInterval = 1;
            _fullScanCountdown = 1;
            _fullScanRetired = false;
            _uselessFullScans = 0;

            _socketHome.Clear();
            _pendPos.Clear();
            _pendStill.Clear();
            _pendWaited.Clear();
            _socketWritten.Clear();
            _socketFights.Clear();
            _socketConceded.Clear();

            _socketSettleReports = 0;
            _socketRebaseReports = 0;
            _socketConcedeReports = 0;
            _chainReports = 0;
            _sockets.Clear();
            _socketKeys.Clear();
            _socketIsEye.Clear();
            _rackAutoTried = false;
            _master = null;
            _weightZeroedThisScene = false;
            Leak.SceneChanged(sceneName);
            LoggerInstance.Msg("Scene loaded: [{0}] {1}", buildIndex, sceneName);
            if (!_bundleTried)
            {
                _bundleTried = true;
                LoadBarrettBundle();
                if (_barrettPrefab == null)
                {
                    try
                    {
                        if (_templateHolder == null)
                        {
                            _templateHolder = new GameObject("HSR_TemplateHolder");
                            _templateHolder.SetActive(false);
                            UObject.DontDestroyOnLoad(_templateHolder);
                        }
                        _barrettPrefab = ObjBuilder.Build(_templateHolder.transform, LoggerInstance);
                        if (_barrettPrefab != null) LoggerInstance.Msg("Barrett body READY via direct OBJ construction — no bundle needed.");
                    }
                    catch (Exception oe) { LoggerInstance.Error("OBJ build failed: " + oe); }
                }
            }
        }

        public override void OnUpdate()
        {
            long _t = Perf.Begin();
            try
            {
                Perf.MaybeReport(LoggerInstance);
                Health.MaybeReport(LoggerInstance);

                if (_leakTracker != null && _leakTracker.Value) Leak.Tick(LoggerInstance);

                if (_cloneRegistered && _hsrClone == null)
                {
                    LoggerInstance.Warning("HSR template was destroyed (scene change) — re-cloning.");
                    _cloneRegistered = false;
                }

                bool wantClone = _enableClone.Value && !_cloneRegistered;
                bool wantDump = _enableDump.Value && !_dumpedThisScene;

                bool wantR700 = !_r700Dumped;
                if (wantClone || wantDump || wantR700)
                {
                    var lm = Il2Cpp.LoadoutManager.singleton;
                    var weapons = lm == null ? null : lm.Game_Weapons;
                    if (weapons != null && weapons.Count > 0)
                    {
                        if (wantClone) TryCloneAndRegister(lm);
                        if (wantDump) { DumpAll(lm); DumpWall(); _dumpedThisScene = true; }
                        if (wantR700)
                        {
                            _r700Dumped = true;
                            try
                            {
                                DumpWeapon(lm, "r700", "r700_dump.txt");
                                LoggerInstance.Msg("R700 rig dumped -> {0}\\r700_dump.txt  "
                                                 + "(donor survey for the M200)", DumpDir);
                            }
                            catch (Exception e) { LoggerInstance.Error("r700 dump: " + e.Message); }
                        }
                    }
                }

                if (_cloneRegistered && !_rackMounted && Time.timeSinceLevelLoad > 3f)
                {
                    if (++_rackThrottle >= 60)
                    {
                        _rackThrottle = 0;
                        if (Time.timeSinceLevelLoad > 45f) _rackMounted = true;
                        else if (MountOnRack(null)) _rackMounted = true;
                    }
                }

                if (_cloneRegistered && !_menuPatchedThisScene)
                {
                    if (Time.timeSinceLevelLoad > 10f) _menuPatchedThisScene = true;
                    else if (++_patchThrottle >= 120) { _patchThrottle = 0; TryPatchMenu(); }
                }

                if (!_inputBroken)
                {

                    HandleNudgeKeys();
                }

                if (_disableWeight != null && _disableWeight.Value && _master != null)
                { try { _master.totalWeight = 0f; } catch { _master = null; } }

                if (++_dressScanFrames >= 120)
                {
                    _dressScanFrames = 0;
                    DressScan();
                }

                if (!_weightZeroedThisScene && Time.timeSinceLevelLoad > 5f)
                {
                    EnforceWeightOffSafe();
                    if (_weightStep >= 4) _weightZeroedThisScene = true;
                }
            }
            catch (Exception e)
            {
                if (e.Message != null && e.Message.Contains("Input"))
                { _inputBroken = true; LoggerInstance.Warning("Legacy Input unavailable; hotkeys disabled."); }
                else LoggerInstance.Error("OnUpdate: " + e);
            }
            Perf.End(Perf.Update, _t);
        }

        internal const uint OurAssetId = 0x4D323030u;
        static GameObject _donorPrefab;
        static uint _donorAssetId;

        static void GiveCloneItsOwnAssetId(GameObject clone, GameObject donor)
        {
            try
            {
                var ni = clone.GetComponent<Il2CppMirror.NetworkIdentity>();
                if (ni == null) { Instance.LoggerInstance.Warning("assetId: the clone has no NetworkIdentity; remote spawns will map to the R700."); return; }
                uint before = 0; try { before = ni.assetId; } catch { }
                _donorAssetId = before;
                uint want = OurAssetId;

                try
                {
                    var lm = Il2Cpp.LoadoutManager.singleton;
                    var ws = lm != null ? lm.Game_Weapons : null;
                    if (ws != null)
                        for (int i = 0; i < ws.Count; i++)
                        {
                            var g = ws[i]; if (g == null || g == clone) continue;
                            var oni = g.GetComponent<Il2CppMirror.NetworkIdentity>();
                            if (oni != null && oni.assetId == want) { want++; i = -1; }
                        }
                }
                catch { }
                try { ni.assetId = want; } catch (Exception se) { Instance.LoggerInstance.Warning("assetId: setter refused (" + se.Message + "), writing the field"); }
                uint after = 0; try { after = ni.assetId; } catch { }
                if (after != want) { try { ni._assetId = want; after = ni.assetId; } catch (Exception fe) { Instance.LoggerInstance.Warning("assetId: field write failed: " + fe.Message); } }
                Instance.LoggerInstance.Msg("assetId: clone {0:X8} -> {1:X8} (donor '{2}' keeps {3:X8}). Remote M200s now spawn as ours; real R700s stay R700s.",
                    before, after, donor != null ? donor.name : "?", before);
            }
            catch (Exception e) { Instance.LoggerInstance.Warning("assetId: " + e.Message); }
        }

        static void Post_StartClient()
        {
            try
            {
                if (Instance == null) return;
                if (_donorPrefab != null) { try { Il2CppMirror.NetworkClient.RegisterPrefab(_donorPrefab); } catch (Exception e) { Instance.LoggerInstance.Warning("prefabs: donor re-register: " + e.Message); } }
                var clone = Instance._hsrClone;
                if (clone != null) { try { Il2CppMirror.NetworkClient.RegisterPrefab(clone); } catch (Exception e) { Instance.LoggerInstance.Warning("prefabs: clone register: " + e.Message); } }
                GameObject a = null, b = null; bool okA = false, okB = false;
                try { okA = Il2CppMirror.NetworkClient.GetPrefab(_donorAssetId, out a); } catch { }
                uint ours = 0; try { var ni = clone != null ? clone.GetComponent<Il2CppMirror.NetworkIdentity>() : null; if (ni != null) ours = ni.assetId; } catch { }
                try { okB = Il2CppMirror.NetworkClient.GetPrefab(ours, out b); } catch { }
                Instance.LoggerInstance.Msg("prefabs: client table: R700 id {0:X8} -> '{1}'; M200 id {2:X8} -> '{3}'.",
                    _donorAssetId, okA && a != null ? a.name : "(none)", ours, okB && b != null ? b.name : "(none)");
            }
            catch (Exception e) { if (Instance != null) Instance.LoggerInstance.Warning("prefabs: " + e.Message); }
        }

        void TryCloneAndRegister(Il2Cpp.LoadoutManager lm)
        {
            try
            {
                GameObject sr25 = null;
                var weapons = lm.Game_Weapons;
                var names = new StringBuilder();
                for (int i = 0; i < weapons.Count; i++)
                {
                    var go = weapons[i];
                    if (go == null) continue;
                    var wname = WeaponNameOf(go) ?? go.name;
                    names.Append('[').Append(i).Append("] ").Append(go.name).Append(" / ").Append(wname).AppendLine();
                    var flat = wname.Replace("-", "").Replace(" ", "").ToLowerInvariant();
                    var flatGo = go.name.Replace("-", "").Replace(" ", "").ToLowerInvariant();
                    if (flat.Contains("r700") || flatGo.Contains("r700")) sr25 = go;
                }
                File.WriteAllText(Path.Combine(DumpDir, "game_weapons_list.txt"), names.ToString());

                if (sr25 == null)
                {
                    LoggerInstance.Warning("SR-25 not found in Game_Weapons yet ({0} entries) — will retry.", weapons.Count);
                    return;
                }

                LoggerInstance.Msg("R700 donor found: '{0}'. Cloning (prefab-pristine)...", sr25.name);

                if (_templateHolder == null)
                {
                    _templateHolder = new GameObject("HSR_TemplateHolder");
                    _templateHolder.SetActive(false);
                    UObject.DontDestroyOnLoad(_templateHolder);
                }
                var clone = UObject.Instantiate(sr25, _templateHolder.transform);
                clone.name = WeaponName;
                _donorPrefab = sr25;
                GiveCloneItsOwnAssetId(clone, sr25);

                var wv3 = clone.GetComponent<Il2Cpp.WeaponV3>() ?? clone.GetComponentInChildren<Il2Cpp.WeaponV3>(true);
                if (wv3 != null)
                {
                    try { wv3.displayName = WeaponName; } catch (Exception e) { LoggerInstance.Warning("displayName set failed: " + e.Message); }
                    LoggerInstance.Msg("WeaponV3: displayName='{0}' slotType={1} IsProduction={2}",
                        wv3.displayName, wv3.weaponSlotType, wv3.IsProduction);
                    var mods = clone.GetComponent<Il2Cpp.WeaponMods>() ?? clone.GetComponentInChildren<Il2Cpp.WeaponMods>(true);
                    if (mods != null)
                        LoggerInstance.Msg("WeaponMods: family='{0}' magCompat='{1}'", mods.WeaponFamily, mods.magazineCompatibility);
                }
                else LoggerInstance.Warning("Clone has no WeaponV3 component?!");

                int existing = -1;
                for (int i = 0; i < weapons.Count; i++)
                {
                    var g = weapons[i];
                    if (g != null && g.name == WeaponName) { existing = i; break; }
                    if (g == null && i == weapons.Count - 1) existing = i;
                }
                if (existing >= 0) weapons[existing] = clone;
                else weapons.Add(clone);
                _hsrClone = clone;
                _cloneRegistered = true;

                LoggerInstance.Msg("HSR appended to Game_Weapons (now {0} entries).", weapons.Count);

                int id = lm.GetWeaponID(WeaponName);
                LoggerInstance.Msg(id >= 0
                    ? "*** SEAM PROVEN: LoadoutManager.GetWeaponID(\"HSR\") = " + id + " ***"
                    : "GetWeaponID(\"HSR\") returned " + id + " — resolver may search differently.");
            }
            catch (Exception e) { LoggerInstance.Error("TryCloneAndRegister: " + e); }
        }

        void TryPatchMenu()
        {
            try
            {
                var arr = Resources.FindObjectsOfTypeAll(Il2CppType.Of<Il2Cpp.MenuController>());
                if (arr == null || arr.Length == 0) return;
                for (int i = 0; i < arr.Length; i++)
                {
                    var mc = arr[i].TryCast<Il2Cpp.MenuController>();
                    if (mc == null || mc.pWeaponNames == null) continue;

                    bool has = false;
                    for (int j = 0; j < mc.pWeaponNames.Count; j++)
                        if (mc.pWeaponNames[j] == WeaponName) { has = true; break; }
                    if (has) { _menuPatchedThisScene = true; continue; }

                    mc.pWeaponNames.Add(WeaponName);
                    LoggerInstance.Msg("Appended 'HSR' to MenuController.pWeaponNames (now {0}).", mc.pWeaponNames.Count);

                    try
                    {
                        var dd = mc.pWeaponDropdown;
                        if (dd != null)
                        {
                            dd.options.Add(new Il2CppTMPro.TMP_Dropdown.OptionData(WeaponName));
                            dd.RefreshShownValue();
                            LoggerInstance.Msg("Added 'HSR' to pWeaponDropdown UI ({0} options).", dd.options.Count);
                        }
                    }
                    catch (Exception de) { LoggerInstance.Warning("Dropdown UI append failed (list append still done): " + de.Message); }
                    _menuPatchedThisScene = true;
                }
            }
            catch (Exception e) { LoggerInstance.Error("TryPatchMenu: " + e); }
        }

        void TryPatchRack()
        {
            try
            {
                var arr = Resources.FindObjectsOfTypeAll(Il2CppType.Of<Il2Cpp.N_WeaponSpawner>());
                if (arr == null || arr.Length == 0) return;
                var sb = new StringBuilder();
                for (int i = 0; i < arr.Length; i++)
                {
                    var sp = arr[i].TryCast<Il2Cpp.N_WeaponSpawner>();
                    if (sp == null || sp.gameObject == null) continue;
                    if (!sp.gameObject.scene.IsValid()) continue;

                    var w = sp.weapons;
                    int spawnPts = 0; try { spawnPts = sp.weaponSpawns != null ? sp.weaponSpawns.Length : 0; } catch { }
                    sb.AppendLine($"=== N_WeaponSpawner '{sp.gameObject.name}' scene={_sceneName} ===");
                    sb.AppendLine($"  AllWeapons={sp.AllWeapons} FindSpawn={sp.FindSpawn} Respawn={sp.Respawn} SpawnAmount={sp.SpawnAmount} spawnPts={spawnPts}");
                    if (w != null)
                    {
                        sb.AppendLine($"  weapons list ({w.Count}):");
                        bool hasHsr = false;
                        for (int j = 0; j < w.Count; j++)
                        {
                            var g = w[j];
                            sb.AppendLine($"    [{j}] {(g != null ? g.name : "<null>")}");
                            if (g != null && g.name == WeaponName) hasHsr = true;
                        }
                        if (!hasHsr && _hsrClone != null)
                        {
                            w.Add(_hsrClone);
                            LoggerInstance.Msg("RACK: appended HSR to spawner '{0}' weapons list (now {1}).", sp.gameObject.name, w.Count);
                        }
                    }
                    _rackPatchedThisScene = true;
                }
                if (sb.Length > 0)
                {
                    File.WriteAllText(Path.Combine(DumpDir, "rack_dump.txt"), sb.ToString());
                    LoggerInstance.Msg("RACK: dumped spawner state → rack_dump.txt");
                }
            }
            catch (Exception e) { LoggerInstance.Error("TryPatchRack: " + e); }
        }

        void LoadBarrettBundle()
        {
            try
            {

                if (!File.Exists(BundlePath)) { LoggerInstance.Msg("No optional parts bundle (expected; the M14 is built from HSR.obj)."); return; }

                try
                {
                    if (RawLoadBundleAsync(BundlePath)) { LoggerInstance.Msg("Bundle loading via raw async il2cpp_runtime_invoke..."); return; }
                }
                catch (Exception e0) { LoggerInstance.Warning("Raw async invoke failed: " + e0.Message + " — trying wrapper paths."); }

                if (_bundle == null)
                try
                {
                    _bundle = AssetBundle.LoadFromFile_Internal(BundlePath, 0u, 0ul);
                    if (_bundle != null) LoggerInstance.Msg("Bundle via LoadFromFile_Internal.");
                }
                catch (Exception e1) { LoggerInstance.Warning("LoadFromFile_Internal failed: " + e1.GetType().Name + " — trying memory path."); }

                if (_bundle == null)
                {
                    try
                    {
                        var il2cppBytes = Il2CppSystem.IO.File.ReadAllBytes(BundlePath);
                        _bundle = AssetBundle.LoadFromMemory_Internal(il2cppBytes, 0u);
                        if (_bundle != null) LoggerInstance.Msg("Bundle via LoadFromMemory_Internal.");
                    }
                    catch (Exception e2) { LoggerInstance.Warning("LoadFromMemory_Internal failed: " + e2.GetType().Name + " — trying async path."); }
                }

                if (_bundle == null)
                {
                    try
                    {
                        var req = AssetBundle.LoadFromFileAsync_Internal(BundlePath, 0u, 0ul);
                        if (req != null)
                        {
                            MelonCoroutines.Start(FinishAsyncBundleLoad(req));
                            LoggerInstance.Msg("Bundle loading asynchronously...");
                            return;
                        }
                    }
                    catch (Exception e3) { LoggerInstance.Error("All bundle load paths failed. Last: " + e3.Message); }
                }
                if (_bundle == null) { LoggerInstance.Error("Bundle load returned null on all paths."); return; }
                FinishBundleSetup();
                return;
            }
            catch (Exception e) { LoggerInstance.Error("LoadBarrettBundle: " + e); }
        }

        System.Collections.IEnumerator PostTakeCensus()
        {
            yield return new WaitForSeconds(2.5f);
            try
            {
                var arr = Resources.FindObjectsOfTypeAll(Il2CppType.Of<Il2Cpp.WeaponV3>());
                int found = 0;
                if (arr != null)
                    for (int i = 0; i < arr.Length; i++)
                    {
                        var w = arr[i].TryCast<Il2Cpp.WeaponV3>();
                        if (w == null || w.gameObject == null || !w.gameObject.scene.IsValid()) continue;
                        if (!IsOurWeapon(w.gameObject)) continue;
                        if (_templateHolder != null && w.transform.IsChildOf(_templateHolder.transform)) continue;
                        found++;
                        var go = w.gameObject;
                        var path = go.name; var t = go.transform.parent;
                        int hops = 0;
                        while (t != null && hops++ < 6) { path = t.gameObject.name + "/" + path; t = t.parent; }
                        var rs = go.GetComponentsInChildren<Renderer>(true);
                        int en = 0; for (int r = 0; r < rs.Length; r++) if (rs[r].enabled) en++;
                        var p = go.transform.position;
                        LoggerInstance.Msg("[CENSUS] '{0}' active={1} pos=({2:F1},{3:F1},{4:F1}) renderers={5}({6} on) path=...{7}",
                            go.name, go.activeInHierarchy, p.x, p.y, p.z, rs.Length, en, path);
                        TuneAsFifty(w);
                    }
                LoggerInstance.Msg("[CENSUS] live HSR instances after take: {0}", found);
            }
            catch (Exception e) { LoggerInstance.Error("PostTakeCensus: " + e); }
        }

        void TuneAsFifty(Il2Cpp.WeaponV3 w)
        {
            try
            {
                if (w == null) return;
                var gs = w.gunStats;

                if (gs != null && gs.gunshotRange < 400f)
                {
                    w.displayName = WeaponName;

                    gs.MOA = 0.40f;

                    gs.Weight = 0f;
                    gs.Ergonomics = 25f;
                    gs.gunshotRange = 500f;

                    try { gs.weaponCaliber = Il2Cpp.GunStats.WeaponCaliber.Everything; } catch { }

                    LoggerInstance.Msg("TUNE: '{0}' → M200 Intervention (.408 CheyTac: 0.40 MOA, "
                                     + "donor fire rate kept, 500m report).", w.gameObject.name);
                }
            }
            catch (Exception e) { LoggerInstance.Warning("TuneAsFifty: " + e.Message); }

            TuneAmmoAsFifty(w);
        }

        void TuneAmmoAsFifty(Il2Cpp.WeaponV3 w)
        {
            try
            {
                if (w == null) return;
                var bullets = w.GetComponentsInChildren<Il2Cpp.Bullet>(true);
                int tuned = 0;
                for (int i = 0; i < bullets.Length; i++)
                {
                    var b = bullets[i];
                    if (b == null || b.damage >= 9000) continue;

                    b.damage = 9999;
                    b.Mass = 3.0f;
                    b.speed = 1035f;
                    b.dragAmount = 550f;
                    b.armourPenetration = 100f;
                    b.armorDamage = 999f;
                    b.penetrationAmount = 25f;
                    b.VehicleDamage = 100f;
                    b.doorMultiplier = 5f;
                    b.bluntDamageMultiplier = 5f;

                    b.GoreLevel = 100;
                    b.FractureChance = 100;
                    b.fragmentationChance = 1f;
                    b.maxFragmentationMultiplier = 10f;
                    b.HeavyBleedChance = 100;
                    b.bleedChance = 100;
                    b.bleedDamage = 50f;
                    b.FractureChance = 100;
                    b.ricochetChance = 0;
                    b.SuppressionAmount = 3f;
                    b.EnableTracer = true;
                    tuned++;
                }
                if (tuned > 0) LoggerInstance.Msg("AMMO: {0} round(s) on '{1}' → .50 AP-T INSANE (9999 dmg, AP 100, pen 25, GoreLevel 100, tracer).", tuned, w.gameObject.name);
            }
            catch (Exception e) { LoggerInstance.Warning("TuneAmmoAsFifty: " + e.Message); }
        }

        unsafe bool RawLoadBundleAsync(string path)
        {

            IntPtr methodPtr = IntPtr.Zero;
            foreach (var f in typeof(AssetBundle).GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public))
                if (f.Name.StartsWith("NativeMethodInfoPtr_LoadFromFileAsync_Internal"))
                { methodPtr = (IntPtr)f.GetValue(null); break; }
            if (methodPtr == IntPtr.Zero) { LoggerInstance.Warning("Raw: async MethodInfo ptr not found."); return false; }

            IntPtr il2cppPath = IL2CPP.ManagedStringToIl2Cpp(path);
            uint crc = 0; ulong offset = 0;
            void** args = stackalloc void*[3];
            args[0] = (void*)il2cppPath;
            args[1] = &crc;
            args[2] = &offset;
            IntPtr exc = IntPtr.Zero;
            IntPtr result = IL2CPP.il2cpp_runtime_invoke(methodPtr, IntPtr.Zero, args, ref exc);
            if (exc != IntPtr.Zero) { LoggerInstance.Warning("Raw: il2cpp exception during async invoke."); return false; }
            if (result == IntPtr.Zero) { LoggerInstance.Warning("Raw: async invoke returned null request."); return false; }
            MelonCoroutines.Start(FinishAsyncBundleLoad(new AssetBundleCreateRequest(result)));
            return true;
        }

        System.Collections.IEnumerator FinishAsyncBundleLoad(AssetBundleCreateRequest req)
        {
            float t = 0f;
            while (!req.isDone && t < 3f) { t += Time.deltaTime; yield return null; }
            if (!req.isDone) LoggerInstance.Msg("Async request not done after 3s — forcing sync completion.");
            _bundle = req.assetBundle;
            if (_bundle == null) { LoggerInstance.Error("Async bundle load returned null."); yield break; }
            LoggerInstance.Msg("Bundle via async path.");
            FinishBundleSetup();
        }

        void FinishBundleSetup()
        {
            try
            {

                GameObject prefab = null;
                try
                {
                    var asset = _bundle.LoadAsset("HSR_Parts", Il2CppType.Of<GameObject>());
                    prefab = asset != null ? asset.TryCast<GameObject>() : null;
                }
                catch (Exception le) { LoggerInstance.Warning("LoadAsset(name) failed (" + le.GetType().Name + ") — using LoadAllAssets."); }
                if (prefab == null)
                {
                    var all = _bundle.LoadAllAssets(Il2CppType.Of<GameObject>());
                    if (all != null)
                        for (int i = 0; i < all.Length; i++)
                        {
                            var go = all[i] != null ? all[i].TryCast<GameObject>() : null;
                            if (go != null && go.name == "HSR_Parts") { prefab = go; break; }
                            if (go != null && prefab == null) prefab = go;
                        }
                }
                _barrettPrefab = prefab;
                if (_barrettPrefab == null) { LoggerInstance.Error("HSR_Parts not found in bundle."); return; }
                _barrettPrefab.hideFlags |= HideFlags.DontUnloadUnusedAsset;

                int rebound = 0;
                var rends = _barrettPrefab.GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < rends.Length; r++)
                {
                    var mats = rends[r].sharedMaterials;
                    for (int m = 0; m < mats.Length; m++)
                    {
                        if (mats[m] == null) continue;
                        var live = Shader.Find(mats[m].shader.name);
                        if (live != null) { mats[m].shader = live; rebound++; }
                    }
                    rends[r].sharedMaterials = mats;
                }
                LoggerInstance.Msg("Barrett bundle READY: {0} renderers, {1} materials rebound to live shaders.", rends.Length, rebound);
            }
            catch (Exception e) { LoggerInstance.Error("FinishBundleSetup: " + e); }
        }

        readonly System.Collections.Generic.Dictionary<string, Vector3> _socketHome =
            new System.Collections.Generic.Dictionary<string, Vector3>();
        readonly System.Collections.Generic.List<Transform> _sockets =
            new System.Collections.Generic.List<Transform>();
        readonly System.Collections.Generic.List<string> _socketKeys =
            new System.Collections.Generic.List<string>();
        readonly System.Collections.Generic.List<bool> _socketIsEye =
            new System.Collections.Generic.List<bool>();

        static string SocketKey(Transform s)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                var t = s;
                int g = 0;
                int rootId = 0;

                while (t != null && g++ < 40)
                {
                    var n = t.gameObject.name;
                    int c = n.IndexOf("(Clone)", StringComparison.Ordinal);
                    if (c >= 0) n = n.Substring(0, c);

                    try { n = n + "#" + t.GetSiblingIndex(); } catch { }
                    if (sb.Length > 0) sb.Insert(0, "/");
                    sb.Insert(0, n);
                    if (t.GetComponent<Il2Cpp.WeaponV3>() != null)
                    { rootId = t.gameObject.GetInstanceID(); break; }
                    t = t.parent;
                }
                if (rootId != 0) sb.Insert(0, rootId + ":");
                return sb.ToString();
            }
            catch { return s == null ? "?" : s.gameObject.name; }
        }
        readonly System.Collections.Generic.HashSet<string> _partCensus =
            new System.Collections.Generic.HashSet<string>();
        readonly System.Collections.Generic.HashSet<string> _sweepCensus =
            new System.Collections.Generic.HashSet<string>();
        readonly System.Collections.Generic.List<Transform> _keepRoots =
            new System.Collections.Generic.List<Transform>();

        static Renderer FindDonorRenderer(Il2Cpp.WeaponV3 w, Transform ourBody)
        {
            try
            {
                var rs = w.transform.GetComponentsInChildren<Renderer>(true);
                if (rs == null) return null;
                Renderer fallback = null;
                for (int i = 0; i < rs.Length; i++)
                {
                    var r = rs[i];
                    if (r == null) continue;
                    Transform t; try { t = r.transform; } catch { continue; }
                    if (ourBody != null && (t == ourBody || t.IsChildOf(ourBody))) continue;
                    if (IsOurGroup(t)) continue;
                    string n; try { n = r.gameObject.name; } catch { continue; }
                    if (n == null) continue;
                    Material m = null; try { m = r.sharedMaterial; } catch { }
                    if (m == null) continue;
                    if (fallback == null) fallback = r;
                    string p = PathUp(t, 6);
                    if (p.IndexOf("MAGAZINE", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (p.IndexOf("MOD_SIGHT", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (n.IndexOf("Chassis", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Barrel", StringComparison.OrdinalIgnoreCase) >= 0
                        || n.IndexOf("Receiver", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Reciever", StringComparison.OrdinalIgnoreCase) >= 0)
                        return r;
                }
                return fallback;
            }
            catch { return null; }
        }

        int _rematReports;

        void MatchDonorRenderState(Il2Cpp.WeaponV3 w, Transform body)
        {
            var donor = FindDonorRenderer(w, body);
            if (donor == null) { LoggerInstance.Msg("BODY: no donor renderer to match against."); return; }
            var ours = body.GetComponentsInChildren<Renderer>(true);
            if (ours == null || ours.Length == 0) return;

            uint donorMask = 1; int donorLayer = 0;
            try { donorMask = donor.renderingLayerMask; } catch { }
            try { donorLayer = donor.gameObject.layer; } catch { }
            int ourLayer = -1; uint ourMask = 0;
            try { ourLayer = ours[0].gameObject.layer; ourMask = ours[0].renderingLayerMask; } catch { }

            int masks = 0, layers = 0;
            for (int i = 0; i < ours.Length; i++)
            {
                var r = ours[i]; if (r == null) continue;
                if (ObjBuilder.MatchDonorRenderingLayers) { try { if (r.renderingLayerMask != donorMask) { r.renderingLayerMask = donorMask; masks++; } } catch { } }
                if (ObjBuilder.MatchDonorLayer) { try { if (r.gameObject.layer != donorLayer) { r.gameObject.layer = donorLayer; layers++; } } catch { } }
            }
            LoggerInstance.Msg("BODY: donor '{0}' layer={1}({2}) renderingLayerMask=0x{3}; ours were layer={4}({5}) mask=0x{6}; set {7} mask(s), {8} layer(s).",
                donor.gameObject.name, donorLayer, LayerMask.LayerToName(donorLayer), donorMask.ToString("X"),
                ourLayer, ourLayer >= 0 ? LayerMask.LayerToName(ourLayer) : "?", ourMask.ToString("X"), masks, layers);

            if (ObjBuilder.UseDonorShader)
            {
                int swapped = 0;
                try { swapped = ObjBuilder.Rematerialise(body, w.transform, LoggerInstance); } catch (Exception re) { LoggerInstance.Msg("BODY: rematerialise err " + re.Message); }
                if (swapped > 0 && _rematReports++ < 3)
                    LoggerInstance.Msg("BODY: {0} renderer(s) now draw with the donor's shader and our maps.", swapped);
            }

            Renderer ourBodyR = null;
            for (int i = 0; i < ours.Length; i++)
            {
                var r = ours[i]; if (r == null) continue;
                string n; try { n = r.gameObject.name; } catch { continue; }
                if (n != null && n.IndexOf("body", StringComparison.OrdinalIgnoreCase) >= 0) { ourBodyR = r; break; }
            }
            if (ourBodyR == null) ourBodyR = ours[0];
            RenderDiff.Once(ourBodyR, donor, LoggerInstance);
        }

        void DressAsBarrett(Il2Cpp.WeaponV3 w)
        {
            long _t = Perf.Begin();
            try
            {
                if (w == null || w.gameObject == null) return;

                WeaponAudio.Apply(w, LoggerInstance);
                MagRegistry.Report(w, LoggerInstance);

                if (_barrettPrefab == null) return;
                HideDonorNow(w);
                var root = w.transform;
                var already = root.GetComponentsInChildren<Transform>(true);

                bool hasBody = false;
                for (int i = 0; i < already.Length; i++)
                    if (already[i].gameObject.name == "HSR_BarrettBody") { hasBody = true; break; }
                if (!hasBody)
                {
                    Transform anchor = null;
                    for (int i = 0; i < already.Length; i++)
                        if (already[i].gameObject.name == "Weapon") { anchor = already[i]; break; }
                    if (anchor == null) anchor = root;

                    var body = UObject.Instantiate(_barrettPrefab, anchor);
                    body.name = "HSR_BarrettBody";
                    body.SetActive(true);
                    body.transform.localPosition = PosOffset();
                    body.transform.localRotation = FullRot();

                    body.transform.localScale = new Vector3(-1f, 1f, 1f);
                    LoggerInstance.Msg("BODY: dressed '{0}' as M14 (handedness-corrected).", w.gameObject.name);

                    try { MatchDonorRenderState(w, body.transform); } catch (Exception me) { LoggerInstance.Msg("BODY: match err " + me.Message); }

                    ObjBuilder.AttachBolt(body.transform, root, LoggerInstance);

                }

                var wm = w.GetComponent<Il2Cpp.WeaponMods>() ?? w.GetComponentInChildren<Il2Cpp.WeaponMods>(true);
                if (wm != null && (wm.CurrentMods == null || wm.CurrentMods.Count == 0))
                {
                    try { wm.SERVER_SetBaseMods(); LoggerInstance.Msg("BODY: '{0}' had no parts — built defaults first.", w.gameObject.name); } catch { }
                }

                int hidden = 0, unhidden = 0;
                Transform fittedMag = null;
                _keepRoots.Clear();
                if (wm != null && wm.CurrentMods != null)
                {
                    for (int i = 0; i < wm.CurrentMods.Count; i++)
                    {
                        var mod = wm.CurrentMods[i];
                        if (mod == null || mod.gameObject == null) continue;
                        var name = mod.gameObject.name;

                        bool keep = OpticToken(name) || IsSightSocketed(mod.transform);
                        bool structural = !keep;

                        if (_partCensus.Add(name))
                            LoggerInstance.Msg("  donor part '{0}' -> {1}", name, keep ? "KEPT (optic)" : "hidden");
                        if (keep) _keepRoots.Add(mod.transform);

                        if (IsMagazineSocketed(mod.transform) ||
                            name.IndexOf("Magazine", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("PMAG", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            fittedMag = mod.transform;
                            if (_magPickCensus.Add(name))
                                LoggerInstance.Msg("  fitted magazine identified: '{0}' (socket '{1}')",
                                    name, mod.transform.parent != null ? mod.transform.parent.gameObject.name : "?");
                        }
                        var rs = mod.GetComponentsInChildren<Renderer>(true);
                        for (int r = 0; r < rs.Length; r++)
                        {
                            if (structural && rs[r].enabled) { rs[r].enabled = false; hidden++; }
                            else if (!structural && !rs[r].enabled) { rs[r].enabled = true; unhidden++; }
                        }
                    }
                }

                {
                    Transform ourBodyForMag = null;
                    var scan = w.transform.GetComponentsInChildren<Transform>(true);
                    for (int i = 0; i < scan.Length; i++)
                        if (scan[i] != null && scan[i].gameObject.name == "HSR_BarrettBody")
                        { ourBodyForMag = scan[i]; break; }
                    if (ourBodyForMag != null)
                        ObjBuilder.AttachMag(ourBodyForMag, fittedMag, LoggerInstance);
                    if (!_magDumped) { _magDumped = true; DumpMagCandidates(w.transform, fittedMag); }
                }

                MagWatch(w.transform);

                int swept = 0;
                Transform ourBody = null;
                var afterAll = w.transform.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < afterAll.Length; i++)
                    if (afterAll[i] != null && afterAll[i].gameObject.name == "HSR_BarrettBody")
                    { ourBody = afterAll[i]; break; }

                var everyR = w.transform.GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < everyR.Length; r++)
                {
                    var rend = everyR[r];
                    if (rend == null || !rend.enabled) continue;
                    if (rend.TryCast<ParticleSystemRenderer>() != null) continue;
                    if (rend.TryCast<TrailRenderer>() != null) continue;
                    var t = rend.transform;
                    if (ourBody != null && (t == ourBody || t.IsChildOf(ourBody))) continue;

                    if (IsOurGroup(t)) continue;
                    bool underKeep = false;
                    for (int k = 0; k < _keepRoots.Count; k++)
                    {
                        var kr = _keepRoots[k];
                        if (kr == null) continue;
                        if (t == kr || t.IsChildOf(kr)) { underKeep = true; break; }
                    }
                    if (underKeep) continue;
                    if (_sweepCensus.Add(rend.gameObject.name))
                        LoggerInstance.Msg("  swept non-mod part '{0}'", rend.gameObject.name);
                    rend.enabled = false;
                    swept++;
                }

                if (_visibleCensus.Count < 40)
                {
                    var still = w.transform.GetComponentsInChildren<Renderer>(true);
                    for (int r = 0; r < still.Length; r++)
                    {
                        var vr = still[r];
                        if (vr == null || !vr.enabled) continue;
                        try
                        {
                            if (vr.TryCast<ParticleSystemRenderer>() != null) continue;
                            if (vr.TryCast<TrailRenderer>() != null) continue;
                        }
                        catch { }
                        var vt = vr.transform;
                        if (ourBody != null && (vt == ourBody || vt.IsChildOf(ourBody))) continue;
                        if (IsOurGroup(vt)) continue;

                        string why = "NOT MATCHED BY ANY RULE";
                        for (int k = 0; k < _keepRoots.Count; k++)
                        {
                            var kr = _keepRoots[k];
                            if (kr == null) continue;
                            if (vt == kr || vt.IsChildOf(kr))
                            { why = "under kept optic '" + kr.gameObject.name + "'"; break; }
                        }
                        if (!_visibleCensus.Add(vr.gameObject.name)) continue;
                        LoggerInstance.Msg("  STILL DRAWING '{0}' — {1}   at {2}",
                            vr.gameObject.name, why, PathUp(vt, 4));
                    }
                }

                if (hidden > 0 || unhidden > 0 || swept > 0)
                    LoggerInstance.Msg("BODY: '{0}' — {1} donor renderers hidden, {2} accessory restored, {3} non-mod swept.", w.gameObject.name, hidden, unhidden, swept);

                Transform weaponNode = null;
                for (int i = 0; i < already.Length; i++)
                {
                    if (already[i] == null) continue;
                    if (string.Equals(already[i].gameObject.name, "Weapon", StringComparison.Ordinal))
                    { weaponNode = already[i]; break; }
                }
                int chainSkipped = 0;
                for (int i = 0; i < already.Length; i++)
                {
                    if (already[i] == null) continue;
                    var n = already[i].gameObject.name;

                    bool isSocket = string.Equals(n, "MOD_Sight", StringComparison.OrdinalIgnoreCase);
                    bool isAdsAnchor = (string.Equals(n, "ADS", StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(n, "ADSZ", StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(n, "real ads", StringComparison.OrdinalIgnoreCase))
                                     && already[i].GetComponent<Il2Cpp.WeaponModParent>() == null;
                    if (!isSocket && !isAdsAnchor) continue;
                    var s = already[i];

                    if (isAdsAnchor && weaponNode != null)
                    {
                        bool chain = false;
                        try { chain = weaponNode.IsChildOf(s); } catch { }
                        if (chain) { chainSkipped++; continue; }
                    }
                    string key = SocketKey(s);

                    bool tracked = false;
                    for (int k = 0; k < _sockets.Count; k++)
                        if (_sockets[k] == s) { tracked = true; break; }
                    if (!tracked)
                    {
                        _sockets.Add(s);
                        _socketKeys.Add(key);

                        _socketIsEye.Add(isAdsAnchor);
                        _scopeDirty = true;
                    }
                }
                if (chainSkipped > 0 && _chainReports++ < 4)
                    LoggerInstance.Msg("scope: {0} aim node(s) in the handling chain left to the game.", chainSkipped);

                ApplyScope();
            }
            catch (Exception e) { LoggerInstance.Error("DressAsBarrett: " + e); }
            finally { Perf.End(Perf.Dress, _t); }

            TuneAsFifty(w);
        }

        int _fullScanCountdown;
        int _fullScanInterval = 1;

        const int FullScanIntervalMax = 40;
        const int UselessFullScansBeforeRetiring = 6;

        readonly System.Collections.Generic.Dictionary<int, Transform> _bodyOf =
            new System.Collections.Generic.Dictionary<int, Transform>();
        bool _fullScanRetired;
        int _uselessFullScans;
        readonly System.Collections.Generic.HashSet<int> _dressedIds =
            new System.Collections.Generic.HashSet<int>();
        int _rearmReports;

        readonly System.Collections.Generic.List<Il2Cpp.WeaponV3> _liveWeapons =
            new System.Collections.Generic.List<Il2Cpp.WeaponV3>();

        void NoteLiveWeapon(Il2Cpp.WeaponV3 w)
        {
            try
            {
                for (int i = _liveWeapons.Count - 1; i >= 0; i--)
                {
                    bool dead; try { dead = _liveWeapons[i] == null || _liveWeapons[i].gameObject == null; } catch { dead = true; }
                    if (dead) { _liveWeapons.RemoveAt(i); continue; }
                    if (_liveWeapons[i] == w) return;
                }
                _liveWeapons.Add(w);
            }
            catch { }
        }

        System.Collections.Generic.List<Il2Cpp.WeaponV3> MyWeapons()
        {
            var outl = new System.Collections.Generic.List<Il2Cpp.WeaponV3>();
            try
            {
                var me = LocalPlayerRoot();
                if (me != null)
                {
                    var ws = me.GetComponentsInChildren<Il2Cpp.WeaponV3>(true);
                    if (ws != null)
                        for (int i = 0; i < ws.Length; i++)
                        {
                            var w = ws[i];
                            if (w == null || w.gameObject == null || !IsOurWeapon(w.gameObject)) continue;
                            outl.Add(w);
                        }
                }
                if (outl.Count == 0)
                {
                    int players = 0;
                    try { var pms = Il2Cpp.PlayerMaster.AllPlayerMasters; players = pms == null ? 0 : pms.Count; } catch { }
                    if (players <= 1)
                    {
                        for (int i = _liveWeapons.Count - 1; i >= 0; i--)
                        {
                            var w = _liveWeapons[i];
                            bool dead; try { dead = w == null || w.gameObject == null; } catch { dead = true; }
                            if (dead) { _liveWeapons.RemoveAt(i); continue; }
                            outl.Add(w);
                        }
                    }
                }
            }
            catch { }
            return outl;
        }
        int _periodicHide;
        string _lastFullOnlyName;

        void DressScan()
        {
            try
            {
                if (_barrettPrefab == null) return;

                _seenThisTick.Clear();
                long t0 = Perf.Begin();
                ScanPlayerWeapons();
                Perf.End(Perf.DressCheap, t0);

                if (--_fullScanCountdown > 0) return;

                long t1 = Perf.Begin();
                int fullOnly = ScanAllWeapons();
                Perf.End(Perf.DressFull, t1);

                if (fullOnly > 0)
                {

                    if (_rearmReports++ < 6)
                        LoggerInstance.Msg(
                            "DressScan: full scan re-armed to 1 because it dressed {0} rifle(s) the "
                          + "player walk did not reach (interval was {1}). Last was '{2}'.",
                            fullOnly, _fullScanInterval, _lastFullOnlyName ?? "?");
                    _fullScanInterval = 1;
                    _uselessFullScans = 0;
                }
                else
                {

                    if (_fullScanInterval < FullScanIntervalMax)
                    {
                        int next = _fullScanInterval * 2;
                        _fullScanInterval = next > FullScanIntervalMax ? FullScanIntervalMax : next;
                    }

                    _uselessFullScans++;
                }
                _fullScanCountdown = _fullScanInterval;
            }
            catch (Exception e) { LoggerInstance.Error("DressScan: " + e); }
        }

        int ScanPlayerWeapons()
        {
            int fresh = 0;
            try
            {
                var pms = Il2Cpp.PlayerMaster.AllPlayerMasters;
                if (pms == null) return 0;
                for (int p = 0; p < pms.Count; p++)
                {
                    var pm = pms[p];
                    if (pm == null || pm.transform == null) continue;
                    var ws = pm.transform.GetComponentsInChildren<Il2Cpp.WeaponV3>(true);
                    if (ws == null) continue;
                    for (int i = 0; i < ws.Length; i++) if (Consider(ws[i], true)) fresh++;
                }
            }
            catch { }
            return fresh;
        }

        int ScanAllWeapons()
        {
            int fresh = 0;
            try
            {
                var arr = Resources.FindObjectsOfTypeAll(Il2CppType.Of<Il2Cpp.WeaponV3>());
                if (arr == null) return 0;
                for (int i = 0; i < arr.Length; i++)
                {
                    var w = arr[i].TryCast<Il2Cpp.WeaponV3>();
                    if (w == null || w.gameObject == null || !w.gameObject.scene.IsValid()) continue;
                    if (Consider(w, false))
                    {
                        fresh++;
                        try { _lastFullOnlyName = w.gameObject.name; } catch { }
                    }
                }
            }
            catch (Exception e) { LoggerInstance.Error("ScanAllWeapons: " + e); }
            return fresh;
        }

        static Transform FindOurBody(Il2Cpp.WeaponV3 w)
        {
            try
            {
                var all = w.transform.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    var t = all[i];
                    if (t == null) continue;
                    if (t.gameObject.name == "HSR_BarrettBody") return t;
                }
            }
            catch { }
            return null;
        }

        static readonly System.Collections.Generic.HashSet<string> _magPickCensus =
            new System.Collections.Generic.HashSet<string>();

        static bool IsSightSocketed(Transform mod)
        {
            try
            {
                var t = mod;
                int g = 0;
                while (t != null && g++ < 8)
                {
                    if (string.Equals(t.gameObject.name, "MOD_SIGHT", StringComparison.OrdinalIgnoreCase))
                        return true;
                    t = t.parent;
                }
            }
            catch { }
            return false;
        }

        internal static bool IsMagazineSocketed(Transform mod)
        {
            try
            {
                var p = mod != null ? mod.parent : null;
                if (p == null) return false;
                var n = p.gameObject.name;
                if (string.IsNullOrEmpty(n)) return false;
                if (n.IndexOf("AMMO", StringComparison.OrdinalIgnoreCase) >= 0) return false;
                return n.IndexOf("MAGAZINE", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        static Transform FindOurBolt(Il2Cpp.WeaponV3 w)
        {
            try
            {
                var body = FindOurBody(w);
                if (body == null) return null;
                for (int i = 0; i < body.childCount; i++)
                {
                    var c = body.GetChild(i);
                    if (c == null) continue;
                    if (string.Equals(c.gameObject.name, "m200_bolt", StringComparison.OrdinalIgnoreCase))
                        return c;
                }
            }
            catch { }
            return null;
        }

        internal static Transform LocalPlayerRoot()
        {
            try
            {
                var me = Il2Cpp.PlayerMaster.MyPlayerMaster;
                return me == null ? null : me.transform;
            }
            catch { return null; }
        }

        internal int ApplyFireModeToOurs()
        {
            int n = 0;
            try
            {
                var mine = MyWeapons();
                for (int i = 0; i < mine.Count; i++) { AutoFire.Apply(mine[i]); n++; }
            }
            catch { }
            return n;
        }

        readonly System.Collections.Generic.HashSet<int> _seenThisTick =
            new System.Collections.Generic.HashSet<int>();

        bool Consider(Il2Cpp.WeaponV3 w, bool fromPlayers)
        {
            try
            {
                if (w == null || w.gameObject == null) return false;
                if (!IsOurWeapon(w.gameObject)) return false;
                if (w.gameObject == _hsrClone) return false;

                int id = w.gameObject.GetInstanceID();
                if (fromPlayers) _seenThisTick.Add(id);
                else if (_seenThisTick.Contains(id))
                {

                    return false;
                }

                bool isNew = _dressedIds.Add(id);
                NoteLiveWeapon(w);

                Transform body;
                bool haveBody = false;
                if (_bodyOf.TryGetValue(id, out body))
                {
                    try { haveBody = body != null && body.gameObject.activeInHierarchy; }
                    catch { haveBody = false; }
                }

                Health.Rifle(haveBody);
                if (!haveBody)
                {

                    RestoreDonorNow(id, "before redress");
                    Health.Redress();
                    DressAsBarrett(w);
                    var found = FindOurBody(w);
                    _bodyOf[id] = found;
                    try { haveBody = found != null && found.gameObject.activeInHierarchy; }
                    catch { haveBody = false; }
                }

                if (fromPlayers && haveBody) HideDonorNow(w);

                ForceMagAndCacheMaster(w);
                AutoFire.Apply(w);
                return !fromPlayers && isNew;
            }
            catch { return false; }
        }

        void ForceMagAndCacheMaster(Il2Cpp.WeaponV3 w)
        {
            try
            {
                var mags = w.GetComponentsInChildren<Il2Cpp.Magazine>(true);
                for (int m = 0; m < mags.Length; m++)
                {
                    var mag = mags[m]; if (mag == null) continue;
                    try { mag.BaseWeight = 0f; mag.BulletWeight = 0f; } catch { }
                }
                try { if (_master == null && w.fpsController != null) _master = w.fpsController; } catch { }
            }
            catch { }
        }

        int _weightStep;

        void EnforceWeightOffSafe()
        {
            if (_disableWeight == null || !_disableWeight.Value) { _weightStep = 4; return; }
            try
            {
                switch (_weightStep)
                {
                    case 0: ZeroWeights(Resources.FindObjectsOfTypeAll(Il2CppType.Of<Il2Cpp.GunStats>()), "GunStats"); break;
                    case 1: ZeroWeights(Resources.FindObjectsOfTypeAll(Il2CppType.Of<Il2Cpp.CharacterMod>()), "CharacterMod"); break;
                    case 2: ZeroWeights(Resources.FindObjectsOfTypeAll(Il2CppType.Of<Il2Cpp.InventoryItem>()), "InventoryItem"); break;
                    default: ZeroWeights(Resources.FindObjectsOfTypeAll(Il2CppType.Of<Il2Cpp.Magazine>()), "Magazine"); break;
                }
            }
            catch (Exception e) { LoggerInstance.Warning("EnforceWeightOffSafe: " + e.Message); }
            _weightStep++;
        }

        void ZeroWeights(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.Object> arr, string kind)
        {
            if (arr == null) return;
            int z = 0;
            for (int i = 0; i < arr.Length; i++)
            {
                var o = arr[i]; if (o == null) continue;
                try
                {
                    if (kind == "GunStats") { var c = o.TryCast<Il2Cpp.GunStats>(); if (c != null) { c.Weight = 0f; z++; } }
                    else if (kind == "CharacterMod") { var c = o.TryCast<Il2Cpp.CharacterMod>(); if (c != null) { c.Weight = 0f; z++; } }
                    else if (kind == "InventoryItem") { var c = o.TryCast<Il2Cpp.InventoryItem>(); if (c != null) { c.Weight = 0f; z++; } }
                    else if (kind == "Magazine") { var c = o.TryCast<Il2Cpp.Magazine>(); if (c != null) { c.BaseWeight = 0f; c.BulletWeight = 0f; z++; } }
                }
                catch { }
            }
            if (z > 0) LoggerInstance.Msg("WEIGHT: zeroed {0} {1}(s).", z, kind);
        }

        static readonly string[] ModeNames = { "body position", "body rotation", "scope position",
                                               "eye position", "bolt position", "bolt clock" };
        int _tuneMode;
        int _nudgeRefused;

        static string AlignHistoryPath =>
            Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory,
                         "HSR_alignment_history.txt");

        void RecordAlignment(string why)
        {
            try
            {
                var line = string.Format(
                    "{0:yyyy-MM-dd HH:mm:ss}  body=({1:F4},{2:F4},{3:F4}) yaw={4:F2} pitch={5:F2} "
                  + "preset={6} scope=({7:F4},{8:F4},{9:F4}) eye=({10:F4},{11:F4},{12:F4})  {13}\n",
                    DateTime.Now, _posX.Value, _posY.Value, _posZ.Value, _fineYaw.Value,
                    _finePitch.Value, _rotPreset.Value, _scopeX.Value, _scopeRise.Value,
                    _scopeZ.Value, _eyeX.Value, _eyeRise.Value, _eyeZ.Value, why);
                File.AppendAllText(AlignHistoryPath, line);
            }
            catch { }
        }

        void HandleNudgeKeys()
        {

            if (!Input.anyKeyDown) return;
            long _kt = Perf.Begin();
            try { HandleNudgeKeysInner(); } finally { Perf.End(Perf.Keys, _kt); }
        }

        void HandleNudgeKeysInner()
        {

            if (Input.GetKeyDown(KeyCode.B))
            {

                AutoFire.Toggle(this, LoggerInstance);
                return;
            }

            if (HsrDevKeys && Input.GetKeyDown(KeyCode.End))
            {
                LoggerInstance.Msg("ALIGN: body=({0:F3},{1:F3},{2:F3}) yaw={3:F1} pitch={4:F1} preset={5} | scope=({6:F3},{7:F3},{8:F3}) | eye=({9:F3},{10:F3},{11:F3})",
                    _posX.Value, _posY.Value, _posZ.Value, _fineYaw.Value, _finePitch.Value,
                    _rotPreset.Value, _scopeX.Value, _scopeRise.Value, _scopeZ.Value,
                    _eyeX.Value, _eyeRise.Value, _eyeZ.Value);
                LoggerInstance.Msg("  ; cycles: body position -> body rotation -> scope position -> eye position.");
                LoggerInstance.Msg("  Arrows = X/Y, PgUp/PgDn = Z.");
                LoggerInstance.Msg("  step: 5mm normally, ALT = 0.5mm fine, SHIFT = 25mm coarse.");
                LoggerInstance.Msg("  history: {0}", AlignHistoryPath);
                RecordAlignment("End pressed");
                return;
            }

            if (!HsrDevKeys || _tuningKeys == null || !_tuningKeys.Value) return;

            if (Input.GetKeyDown(KeyCode.Semicolon))
            {
                if (!HoldingOurs()) return;
                _tuneMode = (_tuneMode + 1) % ModeNames.Length;
                LoggerInstance.Msg("TUNE mode: {0}", ModeNames[_tuneMode]);
                return;
            }

            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) return;

            float dx = 0f, dy = 0f, dz = 0f;
            if (Input.GetKeyDown(KeyCode.RightArrow)) dx = 1f;
            else if (Input.GetKeyDown(KeyCode.LeftArrow)) dx = -1f;
            else if (Input.GetKeyDown(KeyCode.UpArrow)) dy = 1f;
            else if (Input.GetKeyDown(KeyCode.DownArrow)) dy = -1f;
            else if (Input.GetKeyDown(KeyCode.PageUp)) dz = 1f;
            else if (Input.GetKeyDown(KeyCode.PageDown)) dz = -1f;
            else return;

            if (!HoldingOurs())
            {

                if (++_nudgeRefused == 1 || _nudgeRefused % 30 == 0)
                    LoggerInstance.Msg("TUNE: ignored — the M200 is not in your hands ("
                        + _bodyScratch.Count + " body/bodies found). Pick it up first.");
                return;
            }
            _nudgeRefused = 0;

            bool fine = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            bool coarse = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            float mul = fine ? 0.1f : (coarse ? 5f : 1f);
            string fmt = fine ? "F4" : "F3";

            switch (_tuneMode)
            {
                case 0:
                    _posX.Value += dx * 0.005f * mul;
                    _posY.Value += dy * 0.005f * mul;
                    _posZ.Value += dz * 0.005f * mul;
                    ApplyAlignment();
                    LoggerInstance.Msg("TUNE body pos = (" + _posX.Value.ToString(fmt) + ", "
                        + _posY.Value.ToString(fmt) + ", " + _posZ.Value.ToString(fmt) + ")"
                        + (fine ? "  [fine]" : coarse ? "  [coarse]" : ""));
                    break;
                case 1:
                    _fineYaw.Value += dx * 1.0f * mul;
                    _finePitch.Value += dy * 1.0f * mul;
                    ApplyAlignment();
                    LoggerInstance.Msg("TUNE body rot: yaw=" + _fineYaw.Value.ToString(fine ? "F2" : "F1")
                        + " pitch=" + _finePitch.Value.ToString(fine ? "F2" : "F1")
                        + (fine ? "  [fine]" : coarse ? "  [coarse]" : ""));
                    break;
                case 2:
                    _scopeX.Value += dx * 0.005f * mul;
                    _scopeRise.Value += dy * 0.005f * mul;
                    _scopeZ.Value += dz * 0.005f * mul;
                    ApplyScope();
                    LoggerInstance.Msg("TUNE scope = (" + _scopeX.Value.ToString(fmt) + ", "
                        + _scopeRise.Value.ToString(fmt) + ", " + _scopeZ.Value.ToString(fmt) + ")"
                        + (fine ? "  [fine]" : coarse ? "  [coarse]" : ""));
                    break;
                case 3:
                    _eyeX.Value += dx * 0.005f * mul;
                    _eyeRise.Value += dy * 0.005f * mul;
                    _eyeZ.Value += dz * 0.005f * mul;
                    ApplyScope();
                    LoggerInstance.Msg("TUNE eye = (" + _eyeX.Value.ToString(fmt) + ", "
                        + _eyeRise.Value.ToString(fmt) + ", " + _eyeZ.Value.ToString(fmt) + ")"
                        + (fine ? "  [fine]" : coarse ? "  [coarse]" : ""));
                    break;
                case 4:
                {

                    var o = BoltCycle.NudgeOffset(dx * 0.002f * mul, dy * 0.002f * mul, dz * 0.002f * mul);
                    LoggerInstance.Msg("TUNE bolt pos = (" + o.x.ToString("F4") + ", "
                        + o.y.ToString("F4") + ", " + o.z.ToString("F4") + ")"
                        + (fine ? "  [fine]" : coarse ? "  [coarse]" : ""));
                    break;
                }
                default:
                {

                    float c = BoltCycle.NudgeClock(dx * 1.0f * mul);
                    LoggerInstance.Msg("TUNE bolt clock = " + c.ToString(fine ? "F2" : "F1") + " deg"
                        + (fine ? "  [fine]" : coarse ? "  [coarse]" : ""));
                    break;
                }
            }
            MelonPreferences.Save();
            RecordAlignment("tuned");
        }

        string _magState = "";
        bool _magDumped;

        void DumpMagCandidates(Transform weaponRoot, Transform fittedMag)
        {
            try
            {
                Transform ours = null;
                var all = weaponRoot.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                    if (all[i] != null && all[i].gameObject.name == "m200_mag") { ours = all[i]; break; }

                Vector3 root = weaponRoot.position;
                Vector3 oursPos = ours != null ? ours.position : root;
                LoggerInstance.Msg("MAGDUMP: weaponRoot={0}  ours={1}  transforms={2}",
                    Fmt(root), ours == null ? "MISSING" : Fmt(oursPos), all.Length);

                if (fittedMag != null)
                    LoggerInstance.Msg("MAGDUMP: fitted '{0}' pos={1} d(ours)={2:F3} children={3} rends={4} scale={5}",
                        fittedMag.gameObject.name, Fmt(fittedMag.position),
                        Vector3.Distance(fittedMag.position, oursPos),
                        fittedMag.childCount,
                        fittedMag.GetComponentsInChildren<Renderer>(true).Length,
                        Fmt(fittedMag.lossyScale));
                else
                    LoggerInstance.Msg("MAGDUMP: fitted = null (nothing in CurrentMods matched)");

                for (int i = 0; i < all.Length; i++)
                {
                    var t = all[i];
                    if (t == null) continue;
                    var n = t.gameObject.name;
                    if (n == null || n.StartsWith("m200_", StringComparison.OrdinalIgnoreCase)) continue;

                    bool named = n.IndexOf("mag", StringComparison.OrdinalIgnoreCase) >= 0
                              || n.IndexOf("pmag", StringComparison.OrdinalIgnoreCase) >= 0;
                    float d = Vector3.Distance(t.position, oursPos);
                    var r = t.GetComponent<Renderer>();
                    bool near = r != null && d < 0.25f;
                    if (!named && !near) continue;

                    LoggerInstance.Msg("MAGDUMP:   {0}'{1}' d={2:F3} kids={3} rend={4} path={5}",
                        near ? "* " : "  ", n, d, t.childCount, r != null, PathOf(t, 4));
                }
            }
            catch (Exception e) { LoggerInstance.Warning("MAGDUMP failed: " + e.Message); }
        }

        static string Fmt(Vector3 v) { return string.Format("({0:F3},{1:F3},{2:F3})", v.x, v.y, v.z); }

        static string PathOf(Transform t, int depth)
        {
            var s = t.gameObject.name;
            var p = t.parent;
            int g = 0;
            while (p != null && g++ < depth) { s = p.gameObject.name + "/" + s; p = p.parent; }
            return s;
        }

        void MagWatch(Transform weaponRoot)
        {
            try
            {
                Transform mag = null;
                var all = weaponRoot.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                    if (all[i] != null && all[i].gameObject.name == "m200_mag") { mag = all[i]; break; }
                if (mag == null) return;

                int off = 0;
                var rs = mag.GetComponentsInChildren<Renderer>(true);
                if (MagSkin.SkinnedUnder(weaponRoot))
                {
                    for (int i = 0; i < rs.Length; i++)
                        if (rs[i] != null && rs[i].enabled) { rs[i].enabled = false; off++; }
                }
                else
                {
                    for (int i = 0; i < rs.Length; i++)
                        if (rs[i] != null && !rs[i].enabled) { rs[i].enabled = true; off++; }
                }

                string deadParent = null;
                var t = mag.parent;
                int g = 0;
                while (t != null && g++ < 10)
                {
                    if (!t.gameObject.activeSelf) { deadParent = t.gameObject.name; break; }
                    t = t.parent;
                }

                string state = (deadParent != null ? "parent-inactive:" + deadParent
                                                   : "visible")
                             + " selfActive=" + mag.gameObject.activeSelf
                             + " renderers=" + rs.Length + (MagSkin.SkinnedUnder(weaponRoot) ? " skinned,ourOff=" : " reEnabled=") + off
                             + " parent=" + (mag.parent == null ? "none" : mag.parent.gameObject.name);
                if (state != _magState)
                {
                    _magState = state;
                    LoggerInstance.Msg("MAGWATCH: " + state);
                }
            }
            catch { }
        }

        static bool IsOurGroup(Transform t)
        {
            int g = 0;
            while (t != null && g++ < 8)
            {
                var n = t.gameObject.name;
                if (n != null && n.StartsWith("m200_", StringComparison.OrdinalIgnoreCase)) return true;
                t = t.parent;
            }
            return false;
        }

        static readonly string[] OpticTokens =
        {
            "scope", "sight", "optic", "reticle", "red dot", "reddot", "holo", "acog",
            "eotech", "aimpoint", "vortex", "leupold", "nightforce", "trijicon", "elcan",
            "magnifier", "mount", "ring", "riser", "rail", "picatinny", "adapter",
        };

        static bool OpticToken(string n)
        {
            if (string.IsNullOrEmpty(n)) return false;
            for (int i = 0; i < OpticTokens.Length; i++)
                if (n.IndexOf(OpticTokens[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        void CollectOurBodies(System.Collections.Generic.List<Transform> into)
        {
            into.Clear();

            foreach (var kv in _bodyOf)
            {
                var b = kv.Value;
                if (b == null) continue;
                try { if (b.gameObject != null) into.Add(b); } catch { }
            }
            if (into.Count > 0) return;

            try
            {
                var pms = Il2Cpp.PlayerMaster.AllPlayerMasters;
                if (pms == null) return;
                for (int p = 0; p < pms.Count; p++)
                {
                    var pm = pms[p];
                    if (pm == null || pm.transform == null) continue;
                    var ts = pm.transform.GetComponentsInChildren<Transform>(true);
                    if (ts == null) continue;
                    for (int i = 0; i < ts.Length; i++)
                    {
                        var t = ts[i];
                        if (t == null || t.gameObject == null) continue;
                        if (t.gameObject.name != "HSR_BarrettBody") continue;
                        into.Add(t);
                    }
                }
            }
            catch { }
        }

        readonly System.Collections.Generic.List<Transform> _bodyScratch =
            new System.Collections.Generic.List<Transform>();

        static int _holdFrame = -1;
        static bool _holdCached;
        internal static bool LocalHasOurs
        {
            get
            {
                try
                {
                    if (Instance == null) return false;
                    int f = Time.frameCount;
                    if (f == _holdFrame) return _holdCached;
                    _holdFrame = f;
                    _holdCached = Instance.HoldingOurs();
                    return _holdCached;
                }
                catch { return false; }
            }
        }

        int _wdFrame, _wdReports, _wdSummaries, _wdForeign, _wdLastRifles = -1;
        bool _wdPathReported;
        bool _belowCensusDone;

        int _fastFrame;
        readonly System.Collections.Generic.List<Transform> _magsToHold =
            new System.Collections.Generic.List<Transform>();

        internal void NoteMagazineToHold(Transform mag)
        {
            try
            {
                if (mag == null) return;
                for (int i = _magsToHold.Count - 1; i >= 0; i--)
                {
                    bool dead; try { dead = _magsToHold[i] == null; } catch { dead = true; }
                    if (dead) { _magsToHold.RemoveAt(i); continue; }
                    if (_magsToHold[i] == mag) return;
                }
                _magsToHold.Add(mag);
            }
            catch { }
        }

        readonly System.Collections.Generic.List<Transform> _probeRoots =
            new System.Collections.Generic.List<Transform>();

        System.Collections.Generic.List<Transform> ProbeRoots()
        {
            _probeRoots.Clear();
            try
            {
                for (int i = 0; i < _liveWeapons.Count; i++)
                {
                    var w = _liveWeapons[i];
                    if (w == null) continue;
                    try { if (w.gameObject == _hsrClone) continue; } catch { continue; }
                    Transform r = null;
                    try
                    {
                        var pn = w.GetComponentInParent<Il2Cpp.PlayerNetworking>();
                        if (pn != null) r = pn.transform;
                    }
                    catch { }
                    if (r == null) continue;
                    bool dup = false;
                    for (int k = 0; k < _probeRoots.Count; k++) if (_probeRoots[k] == r) { dup = true; break; }
                    if (!dup) _probeRoots.Add(r);
                }
            }
            catch { }
            return _probeRoots;
        }

        static bool RootIsMine(Transform root)
        {
            try
            {
                var pn = root.GetComponent<Il2Cpp.PlayerNetworking>();
                if (pn == null) pn = root.GetComponentInChildren<Il2Cpp.PlayerNetworking>(true);
                return pn != null && pn.isOwned;
            }
            catch { return false; }
        }

        bool IsHeldMagazine(Transform mag)
        {
            try { for (int i = 0; i < _magsToHold.Count; i++) if (_magsToHold[i] == mag) return true; } catch { }
            return false;
        }

        int _fastReports;
        int _shadowReports;
        int _fastSweep, _sweepReports;

        void SweepNewMagazines()
        {
            if (!MagSkin.Ready) return;
            var roots = ProbeRoots();
            if (roots.Count == 0) return;
            var ourMag = FindOurMagGroup();
            if (ourMag == null) return;
            int done = 0;
            for (int ri = 0; ri < roots.Count; ri++)
            {
            Il2Cpp.Magazine[] mags = null;
            try { mags = roots[ri].GetComponentsInChildren<Il2Cpp.Magazine>(true); } catch { }
            if (mags == null) continue;
            for (int i = 0; i < mags.Length; i++)
            {
                var m = mags[i];
                if (m == null) continue;
                Transform t; try { t = m.transform; } catch { continue; }
                if (t == null) continue;
                string n; try { n = t.gameObject.name; } catch { continue; }
                if (n == null || n.IndexOf(MagBox.DonorMagName, StringComparison.Ordinal) < 0) continue;
                bool has; try { has = t.Find("M200_MagSkin") != null; } catch { continue; }
                if (has) continue;
                bool ok = false;
                try { ok = MagSkin.Apply(ourMag, t, LoggerInstance); } catch { }
                if (ok) done++;
            }
            }
            if (done > 0 && _sweepReports++ < 6)
                LoggerInstance.Msg("mag: fast sweep skinned {0} new M200 magazine(s).", done);
        }

        Transform FindOurMagGroup()
        {
            try
            {
                for (int wi = 0; wi < _liveWeapons.Count; wi++)
                {
                    var w = _liveWeapons[wi];
                    if (w == null) continue;
                    if (w.gameObject == _hsrClone) continue;
                    var ourBody = FindOurBody(w);
                    if (ourBody == null) continue;
                    for (int c = 0; c < ourBody.childCount; c++)
                    {
                        var ch = ourBody.GetChild(c);
                        if (ch != null && string.Equals(ch.gameObject.name, ObjBuilder.MagGroup, StringComparison.OrdinalIgnoreCase))
                            return ch;
                    }
                }
            }
            catch { }
            return null;
        }

        void FastMagHold()
        {
            if (++_fastFrame < 8) return;
            _fastFrame = 0;
            if (++_fastSweep >= 3) { _fastSweep = 0; try { SweepNewMagazines(); } catch { } }
            try
            {
                for (int i = _magsToHold.Count - 1; i >= 0; i--)
                {
                    var mag = _magsToHold[i];
                    bool dead; try { dead = mag == null; } catch { dead = true; }
                    if (dead) { _magsToHold.RemoveAt(i); continue; }

                    Transform skin = null;
                    try { skin = mag.Find("M200_MagSkin"); } catch { }

                    var rs = mag.GetComponentsInChildren<Renderer>(true);
                    if (rs == null) continue;
                    int hit = 0;
                    for (int r = 0; r < rs.Length; r++)
                    {
                        var rend = rs[r];
                        if (rend == null) continue;
                        var t = rend.transform;
                        if (skin != null && (t == skin || t.IsChildOf(skin))) continue;
                        try { if (!rend.forceRenderingOff) { rend.forceRenderingOff = true; hit++; } }
                        catch { }
                    }
                    if (hit > 0) { try { MagProbe.Wrote(mag, "FastMagHold", hit); } catch { } }
                    if (skin != null) { try { MagSkin.HoldSkin(mag, skin, rs); } catch { } }

                    if (_shadowReports < 6)
                    {
                        for (int r = 0; r < rs.Length; r++)
                        {
                            var rr2 = rs[r];
                            if (rr2 == null) continue;
                            try
                            {
                                if (rr2.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off) continue;
                                if (!rr2.enabled || rr2.forceRenderingOff) continue;
                                _shadowReports++;
                                LoggerInstance.Msg("SHADOW: '{0}' still casts a shadow  at {1}",
                                    rr2.gameObject.name, PathUp(rr2.transform, 4));
                                rr2.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                            }
                            catch { }
                        }
                    }

                    if (hit > 0 && _fastReports++ < 8)
                        LoggerInstance.Msg("mag: '{0}' grew {1} fresh renderer(s) after a reload - out again within an eighth of a second.",
                            mag.gameObject.name, hit);
                }
            }
            catch { }
        }

        void MagWatchdog()
        {

            if (!PouchNames.RepairedThisScene) { try { PouchNames.RepairPouch("scene", LoggerInstance); } catch { } }
            if (++_wdFrame < 120) return;
            _wdFrame = 0;
            try
            {
                int rifles = 0, examined = 0, forced = 0, strays = 0;
                Transform ourMag = null;

                for (int wi = _liveWeapons.Count - 1; wi >= 0; wi--)
                {
                    var w = _liveWeapons[wi];
                    bool dead; try { dead = w == null || w.gameObject == null || w.transform == null; } catch { dead = true; }
                    if (dead) { _liveWeapons.RemoveAt(wi); continue; }
                    if (w.gameObject == _hsrClone) continue;
                    rifles++;

                    if (!_wdPathReported)
                    {
                        _wdPathReported = true;
                        bool underMaster = false;
                        try { var me = LocalPlayerRoot(); underMaster = me != null && w.transform.IsChildOf(me); } catch { }
                        LoggerInstance.Msg("WATCHDOG: rifle lives at {0}  underMyPlayerMaster={1}  root='{2}'",
                            PathUp(w.transform, 8), underMaster, w.transform.root != null ? w.transform.root.gameObject.name : "?");
                    }

                    var ourBody = FindOurBody(w);
                    if (ourMag == null && ourBody != null)
                    {
                        for (int c = 0; c < ourBody.childCount; c++)
                        {
                            var ch = ourBody.GetChild(c);
                            if (ch != null && string.Equals(ch.gameObject.name, ObjBuilder.MagGroup, StringComparison.OrdinalIgnoreCase))
                            { ourMag = ch; break; }
                        }
                    }

                    if (!_belowCensusDone)
                    {
                        _belowCensusDone = true;
                        var below = w.transform.GetComponentsInChildren<Renderer>(true);
                        for (int r = 0; r < below.Length; r++)
                        {
                            var rd = below[r];
                            if (rd == null) continue;
                            try
                            {
                                if (!rd.enabled || rd.forceRenderingOff) continue;
                                if (rd.TryCast<ParticleSystemRenderer>() != null) continue;
                                var lt = w.transform.InverseTransformPoint(rd.bounds.center);
                                if (lt.y > -0.02f) continue;

                                var rt = rd.transform;
                                bool isOurs = ourBody != null && (rt == ourBody || rt.IsChildOf(ourBody));
                                bool isGroup = IsOurGroup(rt);
                                bool isOptic = false;
                                var pp = rt; int gg = 0;
                                while (pp != null && gg++ < 12)
                                {
                                    try { if (OpticToken(pp.gameObject.name) || IsSightSocketed(pp)) { isOptic = true; break; } }
                                    catch { }
                                    pp = pp.parent;
                                }
                                string mat = "?";
                                try { var mm = rd.sharedMaterial; mat = mm == null ? "NULL" : mm.name; } catch { }

                                LoggerInstance.Msg("BELOW: '{0}' localY={1:F3} ours={2} ourGroup={3} optic={4} mat='{5}'  at {6}",
                                    rd.gameObject.name, lt.y, isOurs, isGroup, isOptic, mat, PathUp(rt, 5));
                            }
                            catch { }
                        }
                    }

                    var rs = w.transform.GetComponentsInChildren<Renderer>(true);
                    for (int r = 0; r < rs.Length; r++)
                    {
                        var rend = rs[r];
                        if (rend == null) continue;
                        bool on; try { on = rend.enabled && !rend.forceRenderingOff; } catch { continue; }
                        if (!on) continue;
                        try
                        {
                            if (rend.TryCast<ParticleSystemRenderer>() != null) continue;
                            if (rend.TryCast<TrailRenderer>() != null) continue;
                        }
                        catch { }
                        var t = rend.transform;
                        if (ourBody != null && (t == ourBody || t.IsChildOf(ourBody))) continue;
                        if (IsOurGroup(t)) continue;
                        examined++;

                        bool optic = false, underMag = false;
                        var p = t; int g = 0;
                        while (p != null && g++ < 12)
                        {
                            try
                            {
                                if (OpticToken(p.gameObject.name) || IsSightSocketed(p)) { optic = true; break; }
                                if (!underMag && p.GetComponent<Il2Cpp.Magazine>() != null) underMag = true;
                            }
                            catch { }
                            p = p.parent;
                        }
                        if (optic) continue;

                        if (_wdReports++ < 16)
                            LoggerInstance.Msg("WATCHDOG: donor renderer still drawing '{0}' underMagazine={1}  at {2}",
                                rend.gameObject.name, underMag, PathUp(t, 5));
                        try { rend.forceRenderingOff = true; forced++; } catch { }

                        if (underMag)
                        {
                            var mp = t; int mg = 0;
                            while (mp != null && mg++ < 12)
                            {
                                bool isMag = false;
                                try { isMag = mp.GetComponent<Il2Cpp.Magazine>() != null; } catch { }
                                if (isMag) { NoteMagazineToHold(mp); try { MagProbe.Wrote(mp, "Watchdog", 1); } catch { } break; }
                                mp = mp.parent;
                            }
                        }
                    }

                    Transform root = null;
                    try { root = w.transform.root; } catch { }
                    if (root == null) continue;
                    var mags = root.GetComponentsInChildren<Il2Cpp.Magazine>(true);
                    if (mags == null) continue;
                    for (int i = 0; i < mags.Length; i++)
                    {
                        var m = mags[i];
                        if (m == null) continue;
                        Transform t; try { t = m.transform; } catch { continue; }
                        if (t == null) continue;

                        Transform owner = null;
                        var p = t; int g = 0;
                        while (p != null && g++ < 24)
                        {
                            try { if (p.GetComponent<Il2Cpp.WeaponV3>() != null) { owner = p; break; } } catch { }
                            p = p.parent;
                        }
                        if (owner != null) continue;

                        string mn = null;
                        try { mn = t.gameObject.name; } catch { }
                        if (mn == null || mn.IndexOf(MagBox.DonorMagName, StringComparison.Ordinal) < 0)
                        {
                            if (_wdForeign++ < 4)
                                LoggerInstance.Msg("WATCHDOG: left '{0}' alone, it is not an M200 magazine.",
                                    mn == null ? "?" : mn);
                            continue;
                        }
                        strays++;

                        bool skinned = false;
                        try { skinned = t.Find("M200_MagSkin") != null; } catch { }
                        if (_wdReports++ < 16)
                            LoggerInstance.Msg("WATCHDOG: M200 magazine '{0}' is parented OUTSIDE any weapon (skinned={1})  at {2}",
                                t.gameObject.name, skinned, PathUp(t, 6));

                        if (MagSkin.Ready && ourMag != null)
                        { try { MagSkin.Apply(ourMag, t, LoggerInstance); } catch { } }
                    }
                }

                if ((forced > 0 || strays > 0 || rifles != _wdLastRifles) && _wdSummaries++ < 30)
                    LoggerInstance.Msg("WATCHDOG: {0} live M200(s), {1} donor renderer(s) examined, {2} forced off, {3} stray magazine(s) outside any weapon.",
                        rifles, examined, forced, strays);
                _wdLastRifles = rifles;
            }
            catch (Exception e) { if (_wdSummaries++ < 3) LoggerInstance.Warning("WATCHDOG: " + e.Message); }
        }

        bool HoldingOurs()
        {
            try
            {
                var mine = MyWeapons();
                for (int i = 0; i < mine.Count; i++)
                {
                    var b = FindOurBody(mine[i]);
                    if (b != null && b.gameObject.activeInHierarchy) return true;
                }
            }
            catch { }
            return false;
        }

        bool _scopeDirty;
        Vector3 _scopeApplied = new Vector3(float.NaN, 0f, 0f);

        void ApplyScope()
        {
            var scopeOff = new Vector3(_scopeX.Value, _scopeRise.Value, _scopeZ.Value);
            var eyeOff = new Vector3(_eyeX.Value, _eyeRise.Value, _eyeZ.Value);
            if (!_scopeDirty && scopeOff == _scopeApplied && eyeOff == _eyeApplied) return;
            _scopeDirty = false;
            _scopeApplied = scopeOff;
            _eyeApplied = eyeOff;
            for (int i = _sockets.Count - 1; i >= 0; i--)
            {
                var s = _sockets[i];
                if (s == null)
                {

                    _sockets.RemoveAt(i);
                    if (i < _socketKeys.Count) _socketKeys.RemoveAt(i);
                    if (i < _socketIsEye.Count) _socketIsEye.RemoveAt(i);
                    continue;
                }
                Vector3 home;
                try
                {
                    if (s.gameObject == null) continue;
                    if (i >= _socketKeys.Count) continue;
                    if (_socketConceded.Contains(_socketKeys[i])) continue;
                    if (!_socketHome.TryGetValue(_socketKeys[i], out home)) continue;
                    bool isEye = i < _socketIsEye.Count && _socketIsEye[i];
                    var placed = home + (isEye ? eyeOff : scopeOff);
                    s.localPosition = placed;
                    _socketWritten[_socketKeys[i]] = placed;
                }
                catch { }
            }
        }

        readonly System.Collections.Generic.Dictionary<string, Vector3> _pendPos =
            new System.Collections.Generic.Dictionary<string, Vector3>();
        readonly System.Collections.Generic.Dictionary<string, int> _pendStill =
            new System.Collections.Generic.Dictionary<string, int>();

        readonly System.Collections.Generic.Dictionary<string, Vector3> _socketWritten =
            new System.Collections.Generic.Dictionary<string, Vector3>();

        readonly System.Collections.Generic.Dictionary<string, int> _socketFights =
            new System.Collections.Generic.Dictionary<string, int>();
        readonly System.Collections.Generic.HashSet<string> _socketConceded =
            new System.Collections.Generic.HashSet<string>();

        const int SocketFightLimit = 8;
        int _socketConcedeReports;
        int _chainReports;

        const float SocketRebaseEpsilon = 0.0015f;
        int _socketRebaseReports;

        const int SocketSettleFrames = 6;
        const float SocketStillEpsilon = 0.0002f;

        const int SocketSettleDeadline = 120;
        readonly System.Collections.Generic.Dictionary<string, int> _pendWaited =
            new System.Collections.Generic.Dictionary<string, int>();
        int _socketSettleReports;

        void SettleSockets()
        {
            try
            {
                for (int i = 0; i < _sockets.Count && i < _socketKeys.Count; i++)
                {
                    var s = _sockets[i];
                    if (s == null) continue;
                    var key = _socketKeys[i];
                    if (key == null || _socketHome.ContainsKey(key)) continue;

                    Vector3 now;
                    try { if (s.gameObject == null) continue; now = s.localPosition; }
                    catch { continue; }

                    Vector3 was;
                    int still;
                    if (!_pendPos.TryGetValue(key, out was)) { _pendPos[key] = now; _pendStill[key] = 0; continue; }
                    if (!_pendStill.TryGetValue(key, out still)) still = 0;

                    int waited;
                    if (!_pendWaited.TryGetValue(key, out waited)) waited = 0;
                    _pendWaited[key] = ++waited;
                    bool timedOut = waited >= SocketSettleDeadline;

                    if (!timedOut && (now - was).sqrMagnitude > SocketStillEpsilon * SocketStillEpsilon)
                    {

                        _pendPos[key] = now;
                        _pendStill[key] = 0;
                        continue;
                    }

                    still++;
                    _pendStill[key] = still;
                    if (!timedOut && still < SocketSettleFrames) continue;
                    if (timedOut && _socketSettleReports < 8)
                        LoggerInstance.Msg("scope: '{0}' never held still - taking its baseline anyway at {1} "
                                         + "after {2} frames, rather than leaving the offset unapplied.",
                            s.gameObject.name, now.ToString("F4"), waited);

                    _socketHome[key] = now;
                    _scopeDirty = true;
                    if (_socketSettleReports++ < 8)
                        LoggerInstance.Msg("scope: baseline settled for '{0}' at {1} after {2} still frames  [{3}]",
                            s.gameObject.name, now.ToString("F4"), still, key);
                }

                for (int i = 0; i < _sockets.Count && i < _socketKeys.Count && i < _socketIsEye.Count; i++)
                {
                    var s2 = _sockets[i];
                    if (s2 == null) continue;
                    var key2 = _socketKeys[i];
                    if (key2 == null) continue;

                    if (_socketConceded.Contains(key2)) continue;

                    Vector3 wrote;
                    if (!_socketWritten.TryGetValue(key2, out wrote)) continue;

                    Vector3 cur;
                    try { if (s2.gameObject == null) continue; cur = s2.localPosition; }
                    catch { continue; }

                    if ((cur - wrote).sqrMagnitude <= SocketRebaseEpsilon * SocketRebaseEpsilon)
                    {
                        _socketFights[key2] = 0;
                        continue;
                    }

                    int fights;
                    if (!_socketFights.TryGetValue(key2, out fights)) fights = 0;
                    _socketFights[key2] = ++fights;
                    if (fights >= SocketFightLimit)
                    {
                        _socketConceded.Add(key2);
                        if (_socketConcedeReports++ < 6)
                            LoggerInstance.Msg("scope: '{0}' is written by the game every frame - "
                                             + "leaving it alone, it is not ours to place.",
                                s2.gameObject.name);
                        continue;
                    }

                    var off = _socketIsEye[i]
                        ? new Vector3(_eyeX.Value, _eyeRise.Value, _eyeZ.Value)
                        : new Vector3(_scopeX.Value, _scopeRise.Value, _scopeZ.Value);

                    Vector3 home2;
                    if (!_socketHome.TryGetValue(key2, out home2)) continue;
                    var next = home2 + off;
                    try { s2.localPosition = next; } catch { continue; }
                    _socketWritten[key2] = next;

                    if (_socketRebaseReports++ < 10)
                        LoggerInstance.Msg("scope: '{0}' was found at {1} after we set it to {2} - "
                                         + "re-asserted the settled placement {3}.",
                            s2.gameObject.name, cur.ToString("F4"), wrote.ToString("F4"), next.ToString("F4"));
                }
            }
            catch { }
        }

        Quaternion FullRot() => RotPreset(_rotPreset.Value) * Quaternion.Euler(_finePitch.Value, _fineYaw.Value, 0f);
        Vector3 PosOffset() => new Vector3(_posX.Value, _posY.Value, _posZ.Value);

        void ApplyAlignment()
        {
            try
            {
                CollectOurBodies(_bodyScratch);
                var pos = PosOffset();
                var rot = FullRot();
                for (int i = 0; i < _bodyScratch.Count; i++)
                {
                    var t = _bodyScratch[i];
                    if (t == null) continue;
                    t.localPosition = pos;
                    t.localRotation = rot;
                }
            }
            catch (Exception e) { LoggerInstance.Warning("ApplyAlignment: " + e.Message); }
        }

        void DumpWeaponCompat(Il2Cpp.LoadoutManager lm)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== weapon magazineCompatibility / WeaponFamily / caliber ===");
                var weapons = lm.Game_Weapons;
                for (int i = 0; i < weapons.Count; i++)
                {
                    var go = weapons[i];
                    if (go == null) continue;
                    var wm = go.GetComponent<Il2Cpp.WeaponMods>() ?? go.GetComponentInChildren<Il2Cpp.WeaponMods>(true);
                    var wv = go.GetComponent<Il2Cpp.WeaponV3>() ?? go.GetComponentInChildren<Il2Cpp.WeaponV3>(true);
                    string cal = "?"; try { if (wv != null && wv.gunStats != null) cal = wv.gunStats.weaponCaliber.ToString(); } catch { }
                    string mc = "?", fam = "?"; try { if (wm != null) { mc = wm.magazineCompatibility; fam = wm.WeaponFamily; } } catch { }
                    sb.AppendLine($"[{i,2}] {go.name,-22} magCompat='{mc}' family='{fam}' caliber={cal}");
                }
                File.WriteAllText(Path.Combine(DumpDir, "weapon_compat.txt"), sb.ToString());
                LoggerInstance.Msg("COMPAT: dumped all weapon mag-compat/family → weapon_compat.txt");
            }
            catch (Exception e) { LoggerInstance.Error("DumpWeaponCompat: " + e); }
        }

        void DumpRig()
        {
            try
            {
                var arr = Resources.FindObjectsOfTypeAll(Il2CppType.Of<Il2Cpp.WeaponV3>());
                if (arr == null) return;
                for (int i = 0; i < arr.Length; i++)
                {
                    var w = arr[i].TryCast<Il2Cpp.WeaponV3>();
                    if (w == null || w.gameObject == null || !w.gameObject.scene.IsValid()) continue;
                    if (!IsOurWeapon(w.gameObject)) continue;
                    if (_templateHolder != null && w.transform.IsChildOf(_templateHolder.transform)) continue;

                    var sb = new StringBuilder();
                    sb.AppendLine("=== RIG DUMP: " + w.gameObject.name + " ===");
                    int lines = 0;
                    WalkRig(w.transform, 0, sb, ref lines);

                    sb.AppendLine();
                    sb.AppendLine("=== component field dumps (il2cpp reflection) ===");
                    var comps = w.GetComponentsInChildren<Component>(true);
                    for (int c = 0; c < comps.Length; c++)
                    {
                        if (comps[c] == null) continue;
                        var tn = comps[c].GetIl2CppType().FullName;
                        if (!(tn.Contains("RecoilAnimation") || tn.Contains("IKsetup") || tn.Contains("FPSAnimationHelper") || tn.Contains("WeaponSwayV2") || tn.Contains("ADSFixed"))) continue;
                        sb.AppendLine("-- " + tn + " @ " + comps[c].gameObject.name + " --");
                        try
                        {
                            var it = comps[c].GetIl2CppType();
                            var fields = it.GetFields();
                            for (int p = 0; p < fields.Length && p < 120; p++)
                            {
                                try
                                {
                                    var pv = fields[p].GetValue(comps[c]);
                                    var vs = pv != null ? pv.ToString() : "null";
                                    if (vs.Length > 90) vs = vs.Substring(0, 90);
                                    sb.AppendLine("  [f] " + fields[p].Name + " = " + vs);
                                }
                                catch { }
                            }
                            var props = it.GetProperties();
                            for (int p = 0; p < props.Length && p < 60; p++)
                            {
                                try
                                {
                                    if (props[p].Name == "destroyCancellationToken" || props[p].Name == "useGUILayout" || props[p].Name == "hideFlags" || props[p].Name == "tag") continue;
                                    var pv = props[p].GetValue(comps[c], null);
                                    var vs = pv != null ? pv.ToString() : "null";
                                    if (vs.Length > 90) vs = vs.Substring(0, 90);
                                    sb.AppendLine("  [p] " + props[p].Name + " = " + vs);
                                }
                                catch { }
                            }
                        }
                        catch (Exception re) { sb.AppendLine("  <reflection failed: " + re.Message + ">"); }
                    }
                    File.WriteAllText(Path.Combine(DumpDir, "hsr_rig_dump.txt"), sb.ToString());
                    LoggerInstance.Msg("RIG: dumped '{0}' hierarchy + anim components → hsr_rig_dump.txt", w.gameObject.name);

                    try
                    {
                        var wm2 = w.GetComponent<Il2Cpp.WeaponMods>() ?? w.GetComponentInChildren<Il2Cpp.WeaponMods>(true);
                        if (wm2 != null)
                        {
                            var live = wm2.GetJsonModStringFromCurrentMods();
                            File.WriteAllText(Path.Combine(DumpDir, "hsr_build_json.txt"),
                                "LIVE BUILD:\n" + (live ?? "<null>") + "\n\nSYNCED JSON_MOD_STRING:\n" + (wm2.JSON_MOD_STRING ?? "<null>"));
                            LoggerInstance.Msg("BUILD: captured current attachment recipe → hsr_build_json.txt");
                        }
                    }
                    catch (Exception je) { LoggerInstance.Warning("BUILD capture failed: " + je.Message); }
                    return;
                }
                LoggerInstance.Msg("RIG: no live HSR to dump — grab one from the rack first, then press F9.");
            }
            catch (Exception e) { LoggerInstance.Error("DumpRig: " + e); }
        }

        void WalkRig(Transform t, int depth, StringBuilder sb, ref int lines)
        {
            if (lines > 1500 || depth > 30) return;
            var n = t.gameObject.name;
            var lp = t.localPosition;
            bool interesting = n.IndexOf("IK", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("hand", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("grip", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("aim", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("bolt", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("charging", StringComparison.OrdinalIgnoreCase) >= 0;
            sb.Append(new string(' ', depth * 2)).Append(interesting ? ">> " : "").Append(n)
              .Append($"  ({lp.x:F3},{lp.y:F3},{lp.z:F3})").AppendLine();
            lines++;
            for (int i = 0; i < t.childCount; i++) WalkRig(t.GetChild(i), depth + 1, sb, ref lines);
        }

        void DumpSceneTree()
        {
            try
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                var sb = new StringBuilder();
                sb.AppendLine($"=== scene tree (2 levels): {scene.name} ===");
                var roots = scene.GetRootGameObjects();
                for (int r = 0; r < roots.Length; r++)
                {
                    var root = roots[r];
                    var p = root.transform.position;
                    sb.AppendLine($"{root.name}  children={root.transform.childCount} pos=({p.x:F1},{p.y:F1},{p.z:F1}) active={root.activeInHierarchy}");
                    int n1 = root.transform.childCount;
                    for (int a = 0; a < n1; a++)
                    {
                        var c1 = root.transform.GetChild(a);
                        var p1 = c1.position;
                        sb.AppendLine($"  {c1.gameObject.name}  children={c1.childCount} pos=({p1.x:F1},{p1.y:F1},{p1.z:F1})");
                        if (n1 <= 30)
                            for (int b = 0; b < c1.childCount && b < 40; b++)
                            {
                                var c2 = c1.GetChild(b);
                                sb.AppendLine($"    {c2.gameObject.name}  children={c2.childCount}");
                            }
                    }
                }
                File.WriteAllText(Path.Combine(DumpDir, "scene_tree.txt"), sb.ToString());
                LoggerInstance.Msg("TREE: scene hierarchy → scene_tree.txt");
            }
            catch (Exception e) { LoggerInstance.Error("DumpSceneTree: " + e); }
        }

        bool _rackMounted;
        bool _rackAutoTried;

        bool MountOnRack(Il2Cpp.LoadoutManager lm)
        {
            if (_hsrClone == null) return false;
            return RackHook.TryBuild(_hsrClone, _barrettPrefab, LoggerInstance);
        }

        void DressRackDisplay(Transform hook)
        {
            try
            {

                for (int k = 0; k < hook.childCount; k++)
                {
                    var rs = hook.GetChild(k).GetComponentsInChildren<Renderer>(true);
                    for (int r = 0; r < rs.Length; r++) rs[r].enabled = false;
                }
                var body = UObject.Instantiate(_barrettPrefab, hook);
                body.name = "HSR_BarrettBody(display)";
                body.transform.localPosition = Vector3.zero;
                body.transform.localRotation = RotPreset(_rotPreset.Value);
                LoggerInstance.Msg("RACK: display dressed as Barrett (rot preset {0}).", _rotPreset.Value);
            }
            catch (Exception e) { LoggerInstance.Error("DressRackDisplay: " + e); }
        }

        System.Collections.IEnumerator DressRackWeapon(Transform hook)
        {
            for (int wait = 0; wait < 10; wait++)
            {
                yield return new WaitForSeconds(0.5f);
                var arr = Resources.FindObjectsOfTypeAll(Il2CppType.Of<Il2Cpp.WeaponV3>());
                if (arr == null) continue;
                for (int i = 0; i < arr.Length; i++)
                {
                    var w = arr[i].TryCast<Il2Cpp.WeaponV3>();
                    if (w == null || w.gameObject == null || !w.gameObject.scene.IsValid()) continue;
                    if (!IsOurWeapon(w.gameObject)) continue;
                    if (Vector3.Distance(w.transform.position, hook.position) > 2.5f) continue;
                    LoggerInstance.Msg("RACK: game spawned '{0}' at the hook — dressing it.", w.gameObject.name);
                    var wm = w.GetComponent<Il2Cpp.WeaponMods>() ?? w.GetComponentInChildren<Il2Cpp.WeaponMods>(true);
                    if (wm != null)
                    {
                        try { wm.SERVER_SetBaseMods(); LoggerInstance.Msg("RACK: dressed. Go grab it."); }
                        catch (Exception be) { LoggerInstance.Warning("RACK dress failed: " + be.Message); }
                    }
                    yield break;
                }
            }
            LoggerInstance.Warning("RACK: no HSR appeared near the hook within 5s — SpawnWeapon may need a different path.");
        }

        void SpawnPhysicalHSR()
        {
            try
            {
                if (_hsrClone == null) { LoggerInstance.Warning("F11: no HSR clone yet."); return; }
                var cam = Camera.main;
                if (cam == null) { LoggerInstance.Warning("F11: no main camera."); return; }

                var inst = UObject.Instantiate(_hsrClone);
                inst.name = WeaponName;
                inst.transform.position = cam.transform.position + cam.transform.forward * 1.6f;
                inst.transform.rotation = Quaternion.LookRotation(cam.transform.right, Vector3.up);
                inst.SetActive(true);

                var wv3 = inst.GetComponent<Il2Cpp.WeaponV3>() ?? inst.GetComponentInChildren<Il2Cpp.WeaponV3>(true);
                if (wv3 != null)
                {
                    try { wv3.NetworkisDropped = true; } catch (Exception se) { LoggerInstance.Warning("isDropped set failed: " + se.Message); }
                    try { wv3.NetworkisKinematic = false; } catch { }
                }
                var rb = inst.GetComponent<Rigidbody>() ?? inst.GetComponentInChildren<Rigidbody>(true);
                if (rb != null) { rb.isKinematic = false; rb.useGravity = true; }

                try
                {
                    if (Il2CppMirror.NetworkServer.active)
                    {
                        Il2CppMirror.NetworkServer.Spawn(inst, (Il2CppMirror.NetworkConnection)null);
                        LoggerInstance.Msg("F11: NetworkServer.Spawn OK (host active).");
                    }
                    else LoggerInstance.Warning("F11: NetworkServer not active — spawn is local-only.");
                }
                catch (Exception ne) { LoggerInstance.Warning("F11: NetworkServer.Spawn failed: " + ne.Message); }

                _lastSpawned = inst;

                var wm = inst.GetComponent<Il2Cpp.WeaponMods>() ?? inst.GetComponentInChildren<Il2Cpp.WeaponMods>(true);
                if (wm != null)
                {
                    try { wm.SERVER_SetBaseMods(); LoggerInstance.Msg("F11: SERVER_SetBaseMods called — building default parts."); }
                    catch (Exception be) { LoggerInstance.Warning("F11: SERVER_SetBaseMods failed: " + be.Message); }
                    try { LoggerInstance.Msg("F11: CurrentMods now = {0} (F8 forces a rebuild if it stayed 0)", wm.CurrentMods != null ? wm.CurrentMods.Count : -1); } catch { }
                }
                else LoggerInstance.Warning("F11: no WeaponMods on spawned instance.");

                LoggerInstance.Msg("F11: physical HSR spawned at {0} — walk up and grab it.", inst.transform.position);
            }
            catch (Exception e) { LoggerInstance.Error("SpawnPhysicalHSR: " + e); }
        }

        void RebuildSpawned()
        {
            try
            {
                var wm = _lastSpawned.GetComponent<Il2Cpp.WeaponMods>() ?? _lastSpawned.GetComponentInChildren<Il2Cpp.WeaponMods>(true);
                if (wm == null) { LoggerInstance.Warning("F8: no WeaponMods."); return; }
                try { wm.ChangeAttachmentString(wm.BASE_JSON_MOD_STRING); LoggerInstance.Msg("F8: attachment string set to base JSON."); }
                catch (Exception ce) { LoggerInstance.Warning("F8: ChangeAttachmentString failed: " + ce.Message); }
                try { wm.RebuildWeaponWithModString(); LoggerInstance.Msg("F8: RebuildWeaponWithModString called."); }
                catch (Exception re) { LoggerInstance.Warning("F8: Rebuild failed: " + re.Message); }
                try { LoggerInstance.Msg("F8: CurrentMods now = {0}", wm.CurrentMods != null ? wm.CurrentMods.Count : -1); } catch { }
            }
            catch (Exception e) { LoggerInstance.Error("RebuildSpawned: " + e); }
        }

        void DumpWall()
        {
            try
            {
                var arr = Resources.FindObjectsOfTypeAll(Il2CppType.Of<Il2Cpp.WeaponV3>());
                if (arr == null) return;
                var sb = new StringBuilder();
                int live = 0;
                for (int i = 0; i < arr.Length; i++)
                {
                    var w = arr[i].TryCast<Il2Cpp.WeaponV3>();
                    if (w == null || w.gameObject == null) continue;
                    var go = w.gameObject;
                    if (!go.scene.IsValid()) continue;
                    live++;
                    var path = go.name; var t = go.transform.parent;
                    while (t != null) { path = t.gameObject.name + "/" + path; t = t.parent; }
                    var p = go.transform.position;
                    sb.AppendLine($"{go.name,-24} active={go.activeInHierarchy} pos=({p.x:F2},{p.y:F2},{p.z:F2}) path={path}");
                }
                sb.Insert(0, $"=== live WeaponV3 instances: {live} (scene {_sceneName}) ===\n");

                for (int i = 0; i < arr.Length; i++)
                {
                    var w = arr[i].TryCast<Il2Cpp.WeaponV3>();
                    if (w == null || w.gameObject == null || !w.gameObject.scene.IsValid()) continue;
                    sb.AppendLine("--- root component census of '" + w.gameObject.name + "' ---");
                    var comps = w.gameObject.GetComponents<Component>();
                    for (int c = 0; c < comps.Length; c++)
                        if (comps[c] != null) sb.AppendLine("  " + comps[c].GetIl2CppType().FullName);
                    break;
                }

                sb.AppendLine();
                sb.AppendLine("=== rack-decor candidates (scenery named like weapons, not WeaponV3) ===");
                string[] needles = { "sr-25", "sr25", "r700", "m4a1", "hk416", "mcx", "scar", "mk17", "mk16", "svd", "vks", "spr300", "mp5", "mp7", "mpx", "m870", "benelli", "glock", "rack", "wall", "armory" };
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                var roots = scene.GetRootGameObjects();
                int found = 0;
                for (int r = 0; r < roots.Length; r++)
                {
                    var all = roots[r].GetComponentsInChildren<Transform>(true);
                    for (int t = 0; t < all.Length; t++)
                    {
                        var go2 = all[t].gameObject;
                        var nm = go2.name.ToLowerInvariant();
                        bool hit = false;
                        for (int n = 0; n < needles.Length; n++) if (nm.Contains(needles[n])) { hit = true; break; }
                        if (!hit) continue;
                        if (go2.GetComponentInParent<Il2Cpp.WeaponV3>(true) != null) continue;
                        var pp = go2.transform.position; var rr = go2.transform.eulerAngles;
                        var path2 = go2.name; var tp = go2.transform.parent;
                        while (tp != null) { path2 = tp.gameObject.name + "/" + path2; tp = tp.parent; }
                        sb.AppendLine($"{go2.name,-32} pos=({pp.x:F2},{pp.y:F2},{pp.z:F2}) rot=({rr.x:F0},{rr.y:F0},{rr.z:F0}) path={path2}");
                        if (++found > 120) { sb.AppendLine("  ... (truncated)"); break; }
                    }
                    if (found > 120) break;
                }

                File.WriteAllText(Path.Combine(DumpDir, "wall_dump.txt"), sb.ToString());
                LoggerInstance.Msg("WALL: dumped {0} live WeaponV3 + {1} decor candidates → wall_dump.txt", live, found);
            }
            catch (Exception e) { LoggerInstance.Error("DumpWall: " + e); }
        }

        void DumpAll(Il2Cpp.LoadoutManager lm)
        {
            LoggerInstance.Msg("DumpAll → {0} (scene {1})", DumpDir, _sceneName);
            try { DumpWeapon(lm, "sr25", "sr25_dump.txt"); } catch (Exception e) { LoggerInstance.Error("sr25 dump: " + e.Message); }
            try { DumpWeapon(lm, "vks", "vks_dump.txt"); } catch (Exception e) { LoggerInstance.Error("vks dump: " + e.Message); }

            try { DumpWeapon(lm, "r700", "r700_dump.txt"); } catch (Exception e) { LoggerInstance.Error("r700 dump: " + e.Message); }
            try { DumpMenuNames(); } catch (Exception e) { LoggerInstance.Error("menu dump: " + e.Message); }
            try { DumpAmmo(); } catch (Exception e) { LoggerInstance.Error("ammo dump: " + e.Message); }
        }

        static string WeaponNameOf(GameObject go)
        {
            var wv3 = go.GetComponent<Il2Cpp.WeaponV3>() ?? go.GetComponentInChildren<Il2Cpp.WeaponV3>(true);
            if (wv3 != null) { try { return wv3.displayName; } catch { } }
            return null;
        }

        void DumpWeapon(Il2Cpp.LoadoutManager lm, string match, string file)
        {
            var weapons = lm.Game_Weapons;
            for (int i = 0; i < weapons.Count; i++)
            {
                var go = weapons[i];
                if (go == null) continue;
                var flat = ((WeaponNameOf(go) ?? "") + "|" + go.name).Replace("-", "").Replace(" ", "").ToLowerInvariant();
                if (!flat.Contains(match)) continue;

                var sb = new StringBuilder();
                sb.AppendLine("=== " + go.name + " (Game_Weapons[" + i + "]) scene=" + _sceneName + " ===");
                DumpComponentTree(go, sb);
                File.WriteAllText(Path.Combine(DumpDir, file), sb.ToString());
                LoggerInstance.Msg("Dumped {0} → {1}", go.name, file);
                return;
            }
            LoggerInstance.Warning("No weapon matching '{0}' in Game_Weapons.", match);
        }

        void DumpComponentTree(GameObject go, StringBuilder sb)
        {
            var comps = go.GetComponentsInChildren<Component>(true);
            sb.AppendLine("-- component inventory (" + comps.Length + ") --");
            for (int i = 0; i < comps.Length; i++)
            {
                var c = comps[i];
                if (c == null) continue;
                sb.AppendLine(c.GetIl2CppType().FullName + "  @ " + c.gameObject.name);
            }
            var wv3 = go.GetComponentInChildren<Il2Cpp.WeaponV3>(true);
            DumpObject("WeaponV3", wv3, sb);
            if (wv3 != null)
            {
                DumpObject("GunStats", SafeGet(() => wv3.gunStats), sb);
                DumpObject("GunFunction", SafeGet(() => wv3.gunFunction), sb);
                DumpObject("WeaponRecoil", SafeGet(() => wv3.weaponRecoil), sb);
                DumpObject("CameraRecoil", SafeGet(() => wv3.cameraRecoil), sb);
                DumpObject("WeaponAnimation", SafeGet(() => wv3.weaponAnimation), sb);
                DumpObject("aiParameters", SafeGet(() => wv3.aiParameters), sb);
            }
            var wm = go.GetComponentInChildren<Il2Cpp.WeaponMods>(true);
            DumpObject("WeaponMods", wm, sb);
            if (wm != null)
            {
                try { sb.AppendLine("-- BASE_JSON_MOD_STRING --"); sb.AppendLine(wm.BASE_JSON_MOD_STRING); } catch { }
                try
                {
                    var slots = wm.BaseModSlots;
                    sb.AppendLine("-- BaseModSlots (" + (slots != null ? slots.Length : 0) + ") --");
                    if (slots != null)
                        for (int i = 0; i < slots.Length; i++)
                            if (slots[i] != null) sb.AppendLine("  " + slots[i].gameObject.name);
                }
                catch { }
            }
        }

        static T SafeGet<T>(Func<T> f) where T : class { try { return f(); } catch { return null; } }

        void DumpObject(string title, object o, StringBuilder sb)
        {
            if (o == null) { sb.AppendLine("-- " + title + ": (absent) --"); return; }
            sb.AppendLine("-- " + title + " (" + o.GetType().FullName + ") --");
            foreach (var p in o.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (!p.CanRead || p.GetIndexParameters().Length != 0) continue;
                var pt = p.PropertyType;
                if (!(pt.IsPrimitive || pt.IsEnum || pt == typeof(string) || pt == typeof(Vector3) || pt == typeof(Vector2))) continue;
                try { sb.AppendLine("  " + p.Name + " = " + p.GetValue(o)); }
                catch { sb.AppendLine("  " + p.Name + " = <threw>"); }
            }
        }

        void DumpMenuNames()
        {
            var arr = Resources.FindObjectsOfTypeAll(Il2CppType.Of<Il2Cpp.MenuController>());
            if (arr == null || arr.Length == 0) return;
            var sb = new StringBuilder();
            for (int i = 0; i < arr.Length; i++)
            {
                var mc = arr[i].TryCast<Il2Cpp.MenuController>();
                if (mc == null) continue;
                sb.AppendLine("=== MenuController '" + mc.gameObject.name + "' scene=" + _sceneName + " ===");
                var p = mc.pWeaponNames; var s = mc.sWeaponNames;
                if (p != null) { sb.AppendLine("pWeaponNames (" + p.Count + "):"); for (int j = 0; j < p.Count; j++) sb.AppendLine("  [" + j + "] " + p[j]); }
                if (s != null) { sb.AppendLine("sWeaponNames (" + s.Count + "):"); for (int j = 0; j < s.Count; j++) sb.AppendLine("  [" + j + "] " + s[j]); }
            }
            File.WriteAllText(Path.Combine(DumpDir, "menunames_dump.txt"), sb.ToString());
            LoggerInstance.Msg("Dumped menu weapon name lists.");
        }

        void DumpAmmo()
        {
            var arr = Resources.FindObjectsOfTypeAll(Il2CppType.Of<Il2Cpp.Bullet>());
            if (arr == null) return;
            var sb = new StringBuilder();
            int hits = 0;
            for (int i = 0; i < arr.Length; i++)
            {
                var b = arr[i].TryCast<Il2Cpp.Bullet>();
                if (b == null) continue;
                var n = b.gameObject != null ? b.gameObject.name : b.name;
                sb.AppendLine("=== Bullet '" + n + "' ===");
                foreach (var p in typeof(Il2Cpp.Bullet).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                {
                    if (!p.CanRead || p.GetIndexParameters().Length != 0) continue;
                    var pt = p.PropertyType;
                    if (!(pt.IsPrimitive || pt.IsEnum || pt == typeof(string))) continue;
                    try { sb.AppendLine("  " + p.Name + " = " + p.GetValue(b)); } catch { }
                }
                hits++;
            }
            File.WriteAllText(Path.Combine(DumpDir, "ammo_dump.txt"), sb.ToString());
            LoggerInstance.Msg("Dumped {0} Bullet assets.", hits);
        }
    }
}
