// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Text;
using MelonLoader;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MapLoader
{

    public sealed class CustomOp
    {
        public string Id;
        public string SceneName;
        public string Title;
        public string Description;
        public string AreaOfOperation;

        public string PackageName = "THE WORLD'S WORST AIR BNB";
        public string OpType = "DIRECT ACTION";
        public string HostilePresence = "SET BY USER";
        public string Duration = "30-45 MIN";
        public string ThreatLevel = "HIGH";
        public int MinAI = 10;
        public int MaxAI = 15;
        public int PrepSeconds = 120;
        public int DefaultInfilTime;
        public string[] Infils;
        public GameObject MapPrefab;
    }

    public static class CustomOps
    {
        public const string TabName   = "ODG OPS";
        public const string OpMarker  = "ODG_OP_";
        public const string TabObject = "ODG_OPS_TAB";
        public const string ListObject = "ODG_OPS_LIST";

        static readonly List<CustomOp> _ops = new List<CustomOp>();
        static readonly Dictionary<int, CustomOp> _byBoard = new Dictionary<int, CustomOp>();

        static readonly List<Il2Cpp.CerebusOpboard> _boardByIndex =
            new List<Il2Cpp.CerebusOpboard>();
        static int _ourPkgIndex = -1;

        const int OperationRowCount = 1;

        static MelonLogger.Instance _log;
        static bool _installed;

        static Il2Cpp.MissionLaptop _laptop;

        const string ActiveButtonName = "ACTIVE OPERATIONS BUTTON";
        const string SimButtonName    = "OPERATION SIMULATION BUTTON";

        public static void Init(MelonLogger.Instance log) { _log = log; }
        public static int Count => _ops.Count;

        public static CustomOp CurrentOp()
        {
            try
            {
                var uis = UnityEngine.Object.FindObjectsOfType<Il2Cpp.OperationBoardUI>(true);
                for (int i = 0; i < (uis != null ? uis.Length : 0); i++)
                {
                    var b = uis[i] != null ? Safe(() => uis[i].CerebusOpboard) : null;
                    CustomOp op;
                    if (b != null && _byBoard.TryGetValue(b.GetInstanceID(), out op) && op != null)
                        return op;
                }
            }
            catch { }
            try
            {
                var want = MapIdentity.SceneName;
                for (int i = 0; i < _ops.Count; i++)
                    if (string.Equals(_ops[i].SceneName, want, StringComparison.OrdinalIgnoreCase))
                        return _ops[i];
            }
            catch { }
            return _ops.Count > 0 ? _ops[0] : null;
        }

        public static void Register(CustomOp op)
        {
            if (op == null || string.IsNullOrEmpty(op.SceneName)) return;
            _ops.RemoveAll(o => o.Id == op.Id);
            _ops.Add(op);
            _log?.Msg("[ops] registered '" + op.Title + "' -> scene '" + op.SceneName + "'");
        }

        public static void Install(HarmonyLib.Harmony h, MelonLogger.Instance log)
        {
            _log = log;
            if (_installed) return;
            _installed = true;

            Patch(h, typeof(Il2Cpp.CerebusOpboard), "Start_Operation",
                  nameof(Pre_StartOperation), nameof(Post_StartOperation));

            Patch(h, typeof(Il2Cpp.GameManagerNetwork), "RestartOperation",
                  nameof(Pre_Restart), null);

            Patch(h, typeof(Il2Cpp.CerebusOpboard), "SetAI_COUNT",
                  null, nameof(Post_SetAiCount));

            Patch(h, typeof(Il2Cpp.OperationBoardUI), "Setup",
                  nameof(Pre_Setup), nameof(Post_Setup));

            Patch(h, typeof(Il2Cpp.OperationBoardUI), "SwitchToActiveOperationUI",
                  null, nameof(Post_ActiveTab));
            Patch(h, typeof(Il2Cpp.OperationBoardUI), "SwitchToSimulationOperationUI",
                  null, nameof(Post_SimTab));

            Patch(h, typeof(Il2Cpp.MissionLaptopDisplayer), "SetCurrentPage",
                  null, nameof(Post_SetCurrentPage));
            Patch(h, typeof(Il2Cpp.MissionLaptop), "SpawnOperations",
                  null, nameof(Post_SpawnOperations));

            Patch(h, typeof(Il2Cpp.MissionLaptop), "SpawnTargetPackages",
                  nameof(Pre_SpawnTargetPackages), nameof(Post_SpawnTargetPackages));

            Patch(h, typeof(Il2Cpp.MissionLaptop), "SpawnTargetPackagePlanning",
                  nameof(Pre_SpawnPlanning), null);

            Patch(h, typeof(Il2Cpp.MapInfilMarker), "OnButtonPressed",
                  nameof(Pre_MarkerPressed), null);

            Patch(h, typeof(Il2Cpp.OperationBoardUI), "ExecuteOperationButtonPressed",
                  nameof(Pre_ExecutePressed), nameof(Post_ExecutePressed));
            Patch(h, typeof(Il2Cpp.OperationBoardUI), "EnableConfirmationMenu",
                  null, nameof(Post_ConfirmMenu));

            Patch(h, typeof(Il2Cpp.OperationBoardUI), "CancelOperationButton",
                  null, nameof(Post_CancelPressed));
            Patch(h, typeof(Il2Cpp.OperationBoardUI), "StartOperationButton",
                  null, nameof(Post_StartButton));
        }

        static void Patch(HarmonyLib.Harmony h, Type type, string method,
                          string prefix, string postfix)
        {
            try
            {
                var m = HarmonyLib.AccessTools.Method(type, method);
                if (m == null) { _log?.Warning("[ops] no " + type.Name + "." + method); return; }
                h.Patch(m,
                    prefix: prefix == null ? null
                        : new HarmonyLib.HarmonyMethod(typeof(CustomOps), prefix),
                    postfix: postfix == null ? null
                        : new HarmonyLib.HarmonyMethod(typeof(CustomOps), postfix));
                _log?.Msg("[ops] hooked " + type.Name + "." + method);
            }
            catch (Exception e) { _log?.Error("[ops] patch " + method + ": " + e.Message); }
        }

        static void Pre_StartOperation(Il2Cpp.CerebusOpboard __instance)
        {
            try
            {
                if (__instance == null) return;
                CustomOp op;
                if (!_byBoard.TryGetValue(__instance.GetInstanceID(), out op))
                {

                    MapRuntime.QueueOnNextScene = false;
                    if (!string.IsNullOrEmpty(MapScene.PendingScene))
                    {
                        _log?.Msg("[ops] vanilla launch: dropping a stale pending scene '"
                                + MapScene.PendingScene + "' that would have redirected "
                                + "this mission to our map");
                        MapScene.PendingScene = null;
                    }
                    _ourLaunch = false;
                    return;
                }

                int prep = -1; try { prep = __instance.PrepTimeInSeconds; } catch { }
                _log?.Msg("[ops] LAUNCHING '" + op.Title + "' / '" + op.PackageName
                        + "'  prep=" + prep + "s -> the map will be built at runtime "
                        + "once the host level loads");

                try
                {

                    if (_tpOverridden.Contains(__instance.GetInstanceID()))
                    {
                        int def = DefaultSlotFor(op);

                        LaunchWantsDay = __instance.SelectedInfiltrationTime == DaySlot;
                        if (__instance.SelectedInfiltrationTime != def)
                        {
                            _log?.Msg("[ops] TIME OF DAY = " + (LaunchWantsDay ? "day" : "night")
                                    + " picked: the map's own host slot (" + def + ") still "
                                    + "loads, and the pick selects which light rig comes up.");
                            __instance.SelectedInfiltrationTime = def;
                        }

                        try
                        {
                            var tp = __instance.TARGETPACKAGE;
                            if (tp != null && def >= 0 && def < tp.Length && tp[def] != null)
                                _log?.Msg("[ops] host scene for this launch: '"
                                        + tp[def].OPERATION_SCENE + "'");
                        }
                        catch { }
                    }
                    else
                    {
                        LaunchWantsDay = __instance.SelectedInfiltrationTime != op.DefaultInfilTime;
                        if (LaunchWantsDay)
                        {
                            _log?.Msg("[ops] TIME OF DAY = day slot picked: the default host "
                                    + "slot still loads (no host override on this board), and "
                                    + "the map will light its own DAY rig instead of the moon.");
                            __instance.SelectedInfiltrationTime = op.DefaultInfilTime;
                        }

                        _hostSwapSlot = -1; _hostSwapWas = null; _hostSwapBoard = null;
                        try
                        {

                            string want = DefaultSlotFor(op) == DaySlot
                                        ? HostSceneDayFor(op) : NightHostScene;
                            var tp = __instance.TARGETPACKAGE;
                            int sel = __instance.SelectedInfiltrationTime;
                            if (tp != null && sel >= 0 && sel < tp.Length && tp[sel] != null
                                && !string.Equals(tp[sel].OPERATION_SCENE, want,
                                                  StringComparison.OrdinalIgnoreCase))
                            {
                                _hostSwapBoard = __instance;
                                _hostSwapSlot  = sel;
                                _hostSwapWas   = tp[sel].OPERATION_SCENE;
                                tp[sel].OPERATION_SCENE = want;
                                _log?.Msg("[ops] host scene substituted for this launch: '"
                                        + _hostSwapWas + "' -> '" + want + "'. This board is the "
                                        + "template and cannot be rewritten, so the swap lasts "
                                        + "exactly one call and is undone immediately after.");
                            }
                        }
                        catch (Exception e)
                        {
                            _log?.Error("[ops] host substitution: " + e.Message
                                      + " (the launch continues on the inherited host)");
                        }
                    }
                }
                catch { }

                if (op != null && !string.IsNullOrEmpty(op.SceneName))
                    MapIdentity.Select(op.SceneName);

                _launchedBoard = __instance;

                MapRuntime.QueueOnNextScene = true;
                _ourLaunch = true;

                int pick;
                string from;
                if (_aiChoice.TryGetValue(__instance.GetInstanceID(), out pick) && pick > 0)
                {
                    LaunchAiCount = pick; from = "your pick";
                }
                else if (LaunchSliderValue >= 0.5f)
                {

                    LaunchAiCount = Math.Max(1, (int)Math.Round((double)LaunchSliderValue));
                    from = "the slider's position";
                }
                else
                {
                    LaunchAiCount = -1; from = "nowhere -- slider unreadable, the board default stands";
                }
                _log?.Msg("[ops] OPERATION PARAMETERS: TIME OF DAY slot " + LaunchTimeSlot
                        + " (" + (LaunchWantsDay ? "day" : "night") + " rig), PREPARATION TIME "
                        + LaunchPrepSeconds + " s, OPFOR COUNT " + LaunchAiCount + " (from " + from
                        + "; this is the TOTAL, HVT included, reinforcements off), OPFOR DIFFICULTY "
                        + LaunchDifficultyName);
            }
            catch (Exception e) { _log?.Error("[ops] Start_Operation: " + e.Message); }
        }

        static string _stampWanted;

        public static bool LaunchWantsDay;

        static Il2Cpp.CerebusOpboard _hostSwapBoard;
        static int _hostSwapSlot = -1;
        static string _hostSwapWas;

        static void RestoreSubstitutedHost()
        {
            if (_hostSwapBoard == null || _hostSwapSlot < 0 || _hostSwapWas == null) return;
            try
            {
                var tp = _hostSwapBoard.TARGETPACKAGE;
                if (tp != null && _hostSwapSlot < tp.Length && tp[_hostSwapSlot] != null)
                {
                    tp[_hostSwapSlot].OPERATION_SCENE = _hostSwapWas;
                    _log?.Msg("[ops] template board restored to '" + _hostSwapWas
                            + "'; the stock mission behind it is untouched.");
                }
            }
            catch (Exception e) { _log?.Error("[ops] host restore: " + e.Message); }
            finally { _hostSwapBoard = null; _hostSwapSlot = -1; _hostSwapWas = null; }
        }

        static void Post_StartOperation(Il2Cpp.CerebusOpboard __instance)
        {
            RestoreSubstitutedHost();
            try
            {
                if (__instance == null || !Il2CppMirror.NetworkServer.active) return;
                CustomOp op;
                if (_byBoard.TryGetValue(__instance.GetInstanceID(), out op))
                {

                    _stampWanted = op.SceneName + (LaunchWantsDay ? "@day" : "");
                }
                else
                {

                    string cur = ReadStamp();
                    if (string.IsNullOrEmpty(cur) || !IsOurSceneName(cur)) return;
                    _stampWanted = "";
                }
                StampRetry();
            }
            catch (Exception e) { _log?.Warning("[ops] stamp: " + e.Message); }
        }

        public static void StampRetry()
        {
            if (_stampWanted == null) return;
            try
            {
                var gmn = Il2Cpp.GameManagerNetwork.instance;
                if (gmn == null) return;

                gmn.NetworkUserOperationName = _stampWanted;
                _log?.Msg(_stampWanted.Length == 0
                    ? "[ops] cleared the mission stamp (vanilla launch)"
                    : "[ops] stamped UserOperationName='" + _stampWanted
                      + "' -- clients detect our mission from this");
                _stampWanted = null;
            }
            catch (Exception e)
            {

                _log?.Warning("[ops] stamp write: " + e.Message);
                _stampWanted = null;
            }
        }

        public static void ClearStampAtMissionEnd()
        {
            try
            {
                if (!Il2CppMirror.NetworkServer.active) return;
                string cur = ReadStamp();
                if (string.IsNullOrEmpty(cur) || !IsOurSceneName(cur)) return;
                _stampWanted = "";
                StampRetry();
            }
            catch { }
        }

        static string ReadStamp()
        {
            try
            {
                var gmn = Il2Cpp.GameManagerNetwork.instance;
                return gmn != null ? gmn.UserOperationName : null;
            }
            catch { return null; }
        }

        static bool IsOurSceneName(string v)
        {
            if (string.IsNullOrEmpty(v)) return false;
            var e = MapIdentity.Resolve(v);
            for (int i = 0; i < _ops.Count; i++)
            {
                var o = _ops[i];
                if (o == null || string.IsNullOrEmpty(o.SceneName)) continue;
                if (string.Equals(o.SceneName, v, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (e != null && string.Equals(o.SceneName, e.Name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        static bool _setupInFlight;

        static readonly Dictionary<int, int> _aiChoice = new Dictionary<int, int>();

        public static int LaunchAiCount = -1;

        public static float LaunchSliderValue = -1f;
        public static int LaunchPrepSeconds = -1;
        public static int LaunchDifficulty = -1;
        public static string LaunchDifficultyName = "?";
        public static int LaunchTimeSlot = -1;
        static bool _prepObserved = true;

        public static void ObservePrepTimer()
        {
            if (_prepObserved) return;
            try
            {
                var om = Il2Cpp.OperationsManager.singleton;
                if (om == null) return;
                float t = om.PrepTimer;
                if (t <= 0.01f) return;
                _prepObserved = true;
                int started = (int)Math.Round(t);
                if (LaunchPrepSeconds > 0 && Math.Abs(started - LaunchPrepSeconds) > 1)
                    _log?.Warning("[ops] PREPARATION TIME: the countdown started at " + started
                                + " s but the panel asked for " + LaunchPrepSeconds
                                + " s. The board's value was reset between the press and the launch.");
                else
                    _log?.Msg("[ops] PREPARATION TIME: the countdown started at " + started
                            + " s, which is what the panel asked for.");
            }
            catch { }
        }

        public static float RestartArmedAt = -1f;

        static void Pre_Restart()
        {
            try
            {

                if (!Main.OursRunning()) return;

                MelonLoader.MelonLogger.Msg("[ops] RESTART MISSION on our map: covering the "
                    + "screen, tearing the map down and arming a rebuild. Restart reloads the "
                    + "host level WITHOUT running Start_Operation, which is the only thing "
                    + "that normally arms one, so left alone the level comes back with no map "
                    + "on it and the ready gate waits forever.");

                try { LoadVeil.Show("mission restart"); } catch { }
                try { MapRuntime.Teardown(); } catch { }
                try { MapRuntime.QueueOnNextScene = true; } catch { }
                RestartArmedAt = UnityEngine.Time.realtimeSinceStartup;
                Main.ResetGateRescue();
            }
            catch (Exception e) { _log?.Warning("[ops] restart: " + e.Message); }
        }

        static void Post_SetAiCount(Il2Cpp.CerebusOpboard __instance, float __0)
        {
            try
            {
                if (__instance == null) return;

                if (_syncingBoards) return;
                int n = (int)Math.Round((double)__0);
                if (n < 1) n = 1;
                _aiChoice[__instance.GetInstanceID()] = n;
            }
            catch { }
        }

        public static void ForgetMapPrefab(string sceneName)
        {
            for (int i = 0; i < _ops.Count; i++)
            {
                var o = _ops[i];
                if (o == null) continue;
                if (string.Equals(o.SceneName, sceneName, StringComparison.OrdinalIgnoreCase))
                {
                    try { if (o.MapPrefab != null) UnityEngine.Object.Destroy(o.MapPrefab); } catch { }
                    o.MapPrefab = null;
                }
            }
        }

        public static void ApplyChosenAiCount(MelonLogger.Instance log)
        {
            try
            {
                var gm = Il2Cpp.GameManager.instance;
                if (gm == null) return;
                int was = 0;
                try { was = gm.botAmount; } catch { }
                if (LaunchAiCount < 1)
                {
                    log?.Msg("[ops] enemy count: the slider was never moved, so "
                           + "botAmount stays at " + was + " (the value the board "
                           + "we cloned brought with it).");
                    return;
                }
                if (was == LaunchAiCount)
                {
                    log?.Msg("[ops] enemy count: botAmount already reads "
                           + LaunchAiCount + ", which is what the slider asked for.");
                    return;
                }
                gm.botAmount = LaunchAiCount;
                log?.Msg("[ops] enemy count: the slider asked for " + LaunchAiCount
                       + " and GameManager.botAmount held " + was + " -- the number "
                       + "the mission we cloned wanted. Set to " + LaunchAiCount
                       + ". ServerSpawnAI reads botAmount and nothing else, so this "
                       + "is the hop that was missing.");
            }
            catch (Exception e) { log?.Warning("[ops] enemy count: " + e.Message); }
        }

        static bool _ourLaunch;

        static readonly HashSet<int> _templateBoardIds = new HashSet<int>();

        static Il2Cpp.CerebusOpboard _launchedBoard;

        public static void LaunchConsumed()
        {
            if (!_ourLaunch) return;
            _ourLaunch = false;
            _log?.Msg("[ops] launch consumed: board redirection off");
        }

        static void Pre_Setup(Il2Cpp.OperationBoardUI __instance,
                              ref Il2Cpp.CerebusOpboard __0)
        {
            _setupInFlight = true;
            try
            {
                if (__instance == null || __0 == null) return;

                var restoreTo = _launchedBoard != null ? _launchedBoard : _ourBoard;
                if (_ourLaunch && restoreTo != null
                    && _templateBoardIds.Contains(__0.GetInstanceID()))
                {
                    _log?.Msg("[ops] post-launch re-bind of the template board "
                            + "intercepted: keeping '" + _ourBoard.name + "' on ui#"
                            + __instance.GetInstanceID());
                    __0 = restoreTo;
                }

                bool ours = _byBoard.ContainsKey(__0.GetInstanceID());
                _log?.Msg("[ops] Setup ui#" + __instance.GetInstanceID() + " board='"
                        + __0.name + "'" + (ours ? " (ours)" : ""));
                if (ours)
                {

                    BoardState.Remember(__instance);
                }
                else
                {

                    RestoreFor(__instance, false);
                }
            }
            catch (Exception e) { _log?.Error("[ops] Pre_Setup: " + e.Message); }
        }

        static void Post_Setup(Il2Cpp.OperationBoardUI __instance,
                               Il2Cpp.CerebusOpboard __0)
        {
            _setupInFlight = false;
            try
            {
                if (__instance == null || __0 == null) return;
                CustomOp op;
                if (!_byBoard.TryGetValue(__0.GetInstanceID(), out op))
                {

                    return;
                }

                _launchIssued = false;
                _modalRetried = false;

                try
                {
                    if (_tpOverridden.Contains(__0.GetInstanceID()))
                    {
                        int slot = DefaultSlotFor(op);
                        if (__0.SelectedInfiltrationTime != slot)
                        {
                            __0.SelectedInfiltrationTime = slot;
                            _log?.Msg("[ops] TIME OF DAY seated on slot " + slot + " ("
                                    + (slot == DaySlot ? "day" : "night") + "), this map's own "
                                    + "default. Moving it is a real choice by the player now.");
                        }
                    }
                }
                catch { }

                _log?.Msg("[ops] board UI bound to '" + op.Title + "'  AI 1-"
                        + Math.Max(60, op.MaxAI) + "  prep " + op.PrepSeconds + "s");

                ApplyBoardValues(__0, op);

                int wired = MapInfils.WireFromInstances(__instance, op.SceneName);
                if (wired == 0)
                {
                    _log?.Msg("[ops] native map path not present; using the overlay");
                    ShowMapPreview(__instance, op);
                    MapInfils.Apply(__instance, op.SceneName);
                }
                ShowOneParameterGroup(__instance);
                ShowSitrep(__instance, op);

                BoardState.NoteWrites(__instance);
            }
            catch (Exception e) { _log?.Error("[ops] Post_Setup: " + e.Message); }
        }

        static void Post_ActiveTab(Il2Cpp.OperationBoardUI __instance)
        { NativeTabShown(__instance, false); }

        static void Post_SimTab(Il2Cpp.OperationBoardUI __instance)
        { NativeTabShown(__instance, true); }

        static void NativeTabShown(Il2Cpp.OperationBoardUI ui, bool simulation)
        {

            if (BoardState.Reasserting) return;
            BoardState.NoteMode(ui, simulation);
            if (_setupInFlight) return;

            var shownOn = LaptopOf(ui);
            HideOurPanel(shownOn);

            try { if (shownOn != null) _ourTabSelected.Remove(shownOn.GetInstanceID()); } catch { }

            try
            {
                var b = ui != null ? Safe(() => ui.CerebusOpboard) : null;
                if (b == null || !_byBoard.ContainsKey(b.GetInstanceID()))
                    RestoreFor(ui, true);
            }
            catch { }
        }

        static void RestoreFor(Il2Cpp.OperationBoardUI ui, bool reassert)
        {
            if (ui == null) return;
            MapPreview.RestoreFor(Safe(() => ui.MapParent), Safe(() => ui.FullscreenMapParent));
            MapInfils.DestroyInstancesUnder(Safe(() => ui.MapParent));
            MapInfils.DestroyInstancesUnder(Safe(() => ui.FullscreenMapParent));
            MapInfils.Clear();
            BoardState.RestoreFor(ui, reassert);
            RestoreNativeTabs();
        }

        static readonly List<GameObject> _nativeHidden = new List<GameObject>();
        static readonly List<CanvasGroup> _dimmed = new List<CanvasGroup>();
        static readonly List<float> _dimmedAlpha = new List<float>();

        static void RecordAlpha(CanvasGroup g)
        {
            if (g == null) return;
            try
            {
                if (g.alpha > 0f) { _dimmed.Add(g); _dimmedAlpha.Add(g.alpha); }
                g.alpha = 0f;
            }
            catch { }
        }

        static void RestoreNativeTabs()
        {
            try
            {
                for (int i = 0; i < _nativeHidden.Count; i++)
                {
                    var go = _nativeHidden[i];
                    if (go != null) go.SetActive(true);
                }
                for (int i = 0; i < _dimmed.Count; i++)
                {
                    var g = _dimmed[i];
                    if (g != null) g.alpha = i < _dimmedAlpha.Count ? _dimmedAlpha[i] : 1f;
                }
            }
            catch { }
            _nativeHidden.Clear();
            _dimmed.Clear();
            _dimmedAlpha.Clear();
        }

        static void HideOurPanel(Il2Cpp.MissionLaptop lap)
        {
            if (lap == null) { HideAll(); return; }
            try
            {
                Tab tab;
                if (_tabs.TryGetValue(lap.GetInstanceID(), out tab)
                    && tab != null && tab.Panel != null)
                    tab.Panel.SetActive(false);
            }
            catch { }
        }

        static bool _building;
        static bool _loggedRowStack;

        static int _rowRetries;

        static void Post_SpawnOperations(Il2Cpp.MissionLaptop __instance)
        {
            if (_building) return;
            try
            {
                if (_ops.Count == 0) return;
                _laptop = __instance;
                Build();
            }
            catch (Exception e) { _log?.Error("[ops] Post_SpawnOperations: " + e.Message); }
        }

        static void Pre_SpawnTargetPackages(Il2Cpp.MissionLaptop __instance,
                                            ref Il2Cpp.CerebusOperation __0)
        {
            try
            {

                bool flagFresh = _ourRowClicked && FrameFresh(_ourRowFrame);
                _ourRowClicked = false;
                if (!flagFresh && !OurTabShowingOn(__instance)) return;
                if (_ourOperation == null) return;
                _log?.Msg("[ops] our row was clicked: redirecting SpawnTargetPackages from '"
                        + (__0 != null ? __0.name : "<null>") + "' to '"
                        + _ourOperation.name + "'");
                __0 = _ourOperation;
            }
            catch (Exception e) { _log?.Error("[ops] Pre_SpawnTargetPackages: " + e.Message); }
        }

        static void Pre_SpawnPlanning(Il2Cpp.MissionLaptop __instance,
                                      ref Il2Cpp.CerebusOpboard __0)
        {
            try
            {

                bool pkgFresh = _ourPkgClicked && FrameFresh(_ourPkgFrame);
                _ourPkgClicked = false;
                if (!pkgFresh && !OurTabShowingOn(__instance)) return;

                Il2Cpp.CerebusOpboard want = null;
                if (pkgFresh && _ourPkgIndex >= 0 && _ourPkgIndex < _boardByIndex.Count)
                    want = _boardByIndex[_ourPkgIndex];
                else if (_boardByIndex.Count == 1)
                    want = _boardByIndex[0];

                if (want == null)
                {
                    _log?.Msg("[ops] planning board asked for but the clicked mission is "
                            + "unknown (index " + _ourPkgIndex + " of " + _boardByIndex.Count
                            + ", fresh=" + pkgFresh + "). Leaving the game's own board in "
                            + "place rather than guessing which map to open.");
                    return;
                }

                try { if (__instance != null) _launchLaptopId = __instance.GetInstanceID(); }
                catch { }
                CustomOp clickedOp = null;
                try { _byBoard.TryGetValue(want.GetInstanceID(), out clickedOp); } catch { }
                _log?.Msg("[ops] mission " + _ourPkgIndex + " ('"
                        + (clickedOp != null ? clickedOp.PackageName : "?")
                        + "' -> map '" + (clickedOp != null ? clickedOp.SceneName : "?")
                        + "') was clicked: redirecting the planning board from '"
                        + (__0 != null ? __0.name : "<null>") + "' to '" + want.name + "'");
                __0 = want;
            }
            catch (Exception e) { _log?.Error("[ops] Pre_SpawnPlanning: " + e.Message); }
        }

        static void Post_SpawnTargetPackages(Il2Cpp.MissionLaptop __instance,
                                             Il2Cpp.CerebusOperation __0)
        {
            try
            {
                if (__instance == null || __0 == null) return;

                if (__0.name == null || !__0.name.StartsWith(OpMarker, StringComparison.Ordinal))
                {

                    if (OurTabShowingOn(__instance))
                        _log?.Warning("[ops] a vanilla operation ('" + __0.name + "') was "
                                    + "dispatched while our tab was showing; the clicked "
                                    + "row was not bound to our CerebusOperation");
                    return;
                }

                int npk = 0;
                try { var a = __0.TargetPackages; npk = a != null ? a.Length : -1; } catch { }
                _log?.Msg("[ops] SpawnTargetPackages fired for operation '" + __0.name
                        + "' which holds " + npk + " package(s)");

                CustomOp op = null;
                foreach (var o in _ops)
                    if (__0.name == OpMarker + o.Id) { op = o; break; }
                if (op == null) return;

                var parent = SafeT(() => __instance.TargetPackageParent);
                if (parent == null) { _log?.Warning("[ops] no TargetPackageParent"); return; }

                for (int i = 0; i < parent.childCount; i++)
                {
                    var prow = parent.GetChild(i);
                    var rowOp = i < _ops.Count ? _ops[i] : op;
                    LabelPackageRow(prow, rowOp);
                    MarkPackageRow(prow, i);
                }
                _log?.Msg("[ops] relabelled " + parent.childCount + " package row(s): "
                        + string.Join(", ", _ops.ConvertAll(o => o.PackageName).ToArray()));
            }
            catch (Exception e) { _log?.Error("[ops] Post_SpawnTargetPackages: " + e.Message); }
        }

        static readonly HashSet<int> _tagged = new HashSet<int>();

        static bool NativeTabShowingOn(Il2Cpp.MissionLaptop lap)
        {
            if (lap == null) return false;
            try
            {
                Tab t;
                if (_tabs.TryGetValue(lap.GetInstanceID(), out t)
                    && t != null && t.Panel != null && t.Panel.activeInHierarchy)
                    return false;
                var at = Safe(() => lap.ActiveOperationsTab);
                var st = Safe(() => lap.SimulationOperationsTab);
                return (at != null && at.activeInHierarchy)
                    || (st != null && st.activeInHierarchy);
            }
            catch { return false; }
        }

        static bool OurTabShowingOn(Il2Cpp.MissionLaptop lap)
        {
            try
            {
                if (lap != null)
                {
                    Tab t;
                    if (_tabs.TryGetValue(lap.GetInstanceID(), out t))
                        return t != null && t.Panel != null && t.Panel.activeInHierarchy;
                    return false;
                }
                foreach (var kv in _tabs)
                {
                    var t2 = kv.Value;
                    if (t2 != null && t2.Panel != null && t2.Panel.activeInHierarchy)
                        return true;
                }
            }
            catch { }
            return false;
        }

        static void MarkPackageRow(Transform row, int index)
        {
            if (row == null) return;
            try
            {
                if (!_tagged.Add(row.gameObject.GetInstanceID())) return;
                var trig = row.gameObject.GetComponent<EventTrigger>();
                if (trig == null) trig = row.gameObject.AddComponent<EventTrigger>();

                if (trig.triggers == null)
                    trig.triggers =
                        new Il2CppSystem.Collections.Generic.List<EventTrigger.Entry>();
                var e = new EventTrigger.Entry();
                e.eventID = EventTriggerType.PointerDown;
                e.callback = new EventTrigger.TriggerEvent();

                int captured = index;
                e.callback.AddListener((UnityEngine.Events.UnityAction<BaseEventData>)
                    ((BaseEventData _) =>
                    {
                        _ourPkgClicked = true;
                        _ourPkgIndex = captured;
                        _ourPkgFrame = UnityEngine.Time.frameCount;
                    }));
                trig.triggers.Add(e);
            }
            catch (Exception ex) { _log?.Warning("[ops] MarkPackageRow: " + ex.Message); }
        }

        static bool _dumpedPkgRow;

        static Il2Cpp.CerebusOperation _ourOperation;
        static bool _ourRowClicked;
        static int _ourRowFrame;

        static Il2Cpp.CerebusOpboard _ourBoard;
        static bool _ourPkgClicked;
        static int _ourPkgFrame;

        static bool FrameFresh(int frame)
        {
            try { return UnityEngine.Time.frameCount - frame <= 90; }
            catch { return false; }
        }

        static void LabelPackageRow(Transform row, CustomOp op)
        {
            if (row == null) return;
            try
            {
                var tmps = row.GetComponentsInChildren<Il2CppTMPro.TextMeshProUGUI>(true);
                if (tmps == null) return;

                if (!_dumpedPkgRow)
                {
                    _dumpedPkgRow = true;
                    _log?.Msg("[ops] PACKAGE ROW FIELDS (" + tmps.Length + "):");
                    for (int i = 0; i < tmps.Length; i++)
                        if (tmps[i] != null)
                            _log?.Msg("        [" + i + "] " + tmps[i].gameObject.name
                                    + " = " + tmps[i].text);
                }

                for (int i = 0; i < tmps.Length; i++)
                {
                    var t = tmps[i];
                    if (t == null) continue;
                    var loc = t.GetComponent<Il2Cpp.LocalizationTMPEvent>();
                    if (loc != null) loc.enabled = false;

                    string cur = (t.text ?? "").Trim();
                    if (cur.Length == 0) continue;
                    string u = cur.ToUpperInvariant();

                    if (u.Contains("DIRECT ACTION") || u.Contains("HVT ") || u.Contains("INTEL")
                     || u.Contains("AREA DEFENCE") || u.Contains("AREA DEFENSE"))
                        t.text = op.OpType;
                    else if (u.Contains("SET BY USER") || u.Contains("HOSTILE"))
                        t.text = op.HostilePresence;
                    else
                        t.text = op.PackageName;
                }
            }
            catch (Exception e) { _log?.Warning("[ops] LabelPackageRow: " + e.Message); }
        }

        static bool _launchIssued;

        static void Post_ExecutePressed(Il2Cpp.OperationBoardUI __instance)
        {
            try
            {
                var b = Safe(() => __instance.CerebusOpboard);
                bool ours = b != null && _byBoard.ContainsKey(b.GetInstanceID());
                var lap = LaptopOf(__instance);
                _log?.Msg("[ops] EXECUTE pressed. board='" + (b != null ? b.name : "<null>")
                        + "'  ours=" + ours + "  laptop='"
                        + (lap != null ? lap.name : "?") + "'");

                var ni = Safe(() => __instance.NotEnoughInfilsButton);
                var ne = Safe(() => __instance.NotEnoughExfilsButton);
                if (ni != null && ni.activeInHierarchy)
                {
                    _log?.Warning("[ops] blocked: NOT ENOUGH INFILS selected. Left-click an "
                                + "insertion point on the map first.");
                    return;
                }
                if (ne != null && ne.activeInHierarchy)
                {
                    _log?.Warning("[ops] blocked: NOT ENOUGH EXFILS selected.");
                    return;
                }

                var w = Safe(() => __instance.ConfirmationWindow);
                DumpModal(w, "ConfirmationWindow");

                if (w != null && !_confirmOpenedOn.Contains(__instance.GetInstanceID()))
                {
                    try
                    {
                        var a = w.mwAnimator;
                        if (a != null)
                        {
                            if (!a.gameObject.activeSelf) a.gameObject.SetActive(true);
                            if (!a.enabled) a.enabled = true;
                        }
                    }
                    catch { }
                    try
                    {
                        ShutModal(w, "repopulating before OpenWindow");

                        bool populated = false;
                        try { __instance.EnableConfirmationMenu(); populated = true; }
                        catch (Exception ecm)
                        { _log?.Warning("[ops] native EnableConfirmationMenu: " + ecm.Message); }
                        if (!populated) w.OpenWindow();
                        _confirmOpenedOn.Add(__instance.GetInstanceID());
                        _log?.Msg("[ops] no confirm menu opened for ui#"
                                + __instance.GetInstanceID() + " on this press; opened it "
                                + (populated ? "through the native populate path."
                                             : "directly (native populate threw)."));
                    }
                    catch (Exception oe)
                    { _log?.Warning("[ops] forced OpenWindow: " + oe.Message); }
                }

                if (!ours || w == null) return;

                if (_launchIssued) return;

                if (_launchLaptopId != 0)
                {
                    var thisLap = LaptopOf(__instance);
                    if (thisLap == null || thisLap.GetInstanceID() != _launchLaptopId)
                        return;
                }

                bool presentable = w.gameObject.activeInHierarchy;

                if (!presentable && !_modalRetried)
                {
                    _modalRetried = true;
                    ShutModal(w, "retry before OpenWindow");
                    try
                    {
                        var a = w.mwAnimator;
                        if (a != null)
                        {
                            if (!a.gameObject.activeSelf) a.gameObject.SetActive(true);
                            if (!a.enabled) a.enabled = true;
                        }
                    }
                    catch { }
                    try { w.OpenWindow(); } catch (Exception oe)
                    { _log?.Warning("[ops] OpenWindow retry: " + oe.Message); }
                    presentable = w.gameObject.activeInHierarchy;
                    _log?.Msg("[ops] modal retry -> presentable=" + presentable);
                }

                if (!presentable)
                {
                    _launchIssued = true;
                    _log?.Msg("[ops] confirmation window cannot be shown: "
                            + "launching directly via StartOperationButton");
                    __instance.StartOperationButton();
                    try { w.CloseWindow(); } catch { }

                    ShutModal(w, "retry before OpenWindow");
                }
            }
            catch (Exception e) { _log?.Error("[ops] Post_ExecutePressed: " + e.Message); }

            try { MelonLoader.MelonCoroutines.Start(SweepBlindModals()); } catch { }
        }

        static int _syncFrame = -1;

        static Il2Cpp.CerebusOpboard SyncSiblingBoards(Il2Cpp.OperationBoardUI pressed)
        {
            int frame = 0;
            try { frame = UnityEngine.Time.frameCount; } catch { }
            if (frame == _syncFrame) return null;
            _syncFrame = frame;
            try
            {

                Il2Cpp.CerebusOpboard ours = null;
                var uis = UnityEngine.Object.FindObjectsOfType<Il2Cpp.OperationBoardUI>(true);
                if (uis == null) return null;
                if (pressed != null)
                {
                    var pb = Safe(() => pressed.CerebusOpboard);
                    if (pb != null && _byBoard.ContainsKey(pb.GetInstanceID())) ours = pb;
                }
                if (ours == null)
                {

                    for (int i = 0; i < uis.Length; i++)
                    {
                        var b = Safe(() => uis[i].CerebusOpboard);
                        if (b != null && _byBoard.ContainsKey(b.GetInstanceID())) { ours = b; break; }
                    }
                    if (ours != null && pressed != null)
                        _log?.Msg("[ops] the pressed laptop holds a shipped board; "
                                + "falling back to the first ODG board found ('"
                                + ours.name + "')");
                }
                if (ours == null) return null;
                SyncAllTo(ours, uis);
                return ours;
            }
            catch (Exception e) { _log?.Warning("[ops] SyncSiblingBoards: " + e.Message); return null; }
        }

        static void SyncAllTo(Il2Cpp.CerebusOpboard board,
                              Il2Cpp.OperationBoardUI[] uis = null)
        {
            if (board == null) return;
            if (uis == null)
                uis = UnityEngine.Object.FindObjectsOfType<Il2Cpp.OperationBoardUI>(true);
            if (uis == null) return;

            int prepWas = 0, infilWas = 0;
            bool held = false;
            try
            {
                prepWas = board.PrepTimeInSeconds;
                infilWas = board.SelectedInfiltrationTime;
                held = true;
            }
            catch { }
            for (int i = 0; i < uis.Length; i++)
            {
                var ui = uis[i];
                if (ui == null) continue;
                var b = Safe(() => ui.CerebusOpboard);
                if (b != null && b.GetInstanceID() == board.GetInstanceID()) continue;
                try
                {
                    var lap = LaptopOf(ui);
                    if (lap == null)
                    {
                        _log?.Msg("[ops] sibling ui#" + ui.GetInstanceID()
                                + " has no MissionLaptop parent; left alone");
                        continue;
                    }
                    ui.Setup(board, lap);
                    _log?.Msg("[ops] sibling ui#" + ui.GetInstanceID()
                            + " was on '" + (b != null ? b.name : "<null>")
                            + "'; rebound to '" + board.name + "' so its confirm "
                            + "menu cannot show another mission");
                }
                catch (Exception se)
                { _log?.Warning("[ops] sibling rebind ui#" + ui.GetInstanceID() + ": " + se.Message); }
            }

            if (held)
            {
                try
                {
                    int now = board.PrepTimeInSeconds;
                    if (now != prepWas)
                    {
                        board.PrepTimeInSeconds = prepWas;
                        _log?.Msg("[ops] prep timer: the rebind reset the board to "
                                + now + " s; the chosen " + prepWas + " s is back "
                                + "(Setup writes 10 onto any board it is handed)");
                    }
                    if (board.SelectedInfiltrationTime != infilWas)
                        board.SelectedInfiltrationTime = infilWas;
                }
                catch (Exception pe) { _log?.Warning("[ops] prep restore: " + pe.Message); }
            }
        }

        static string _lastSeenStamp;
        static float _nextStampWatch;
        public static void StampWatchTick()
        {
            float now = 0f;
            try { now = UnityEngine.Time.realtimeSinceStartup; } catch { }
            if (now < _nextStampWatch) return;
            _nextStampWatch = now + 0.25f;
            try
            {
                var s = ReadStamp();
                if (string.Equals(s, _lastSeenStamp, StringComparison.Ordinal)) return;
                _lastSeenStamp = s;
                if (string.IsNullOrEmpty(s)) return;

                var bare = s; int at = bare.IndexOf('@');
                if (at > 0) bare = bare.Substring(0, at);
                Il2Cpp.CerebusOpboard board = null;
                for (int i = 0; i < _boardByIndex.Count; i++)
                {
                    var b = _boardByIndex[i];
                    if (b == null) continue;
                    CustomOp op;
                    if (!_byBoard.TryGetValue(b.GetInstanceID(), out op) || op == null) continue;
                    if (string.Equals(op.SceneName, bare, StringComparison.OrdinalIgnoreCase))
                    { board = b; break; }
                }
                if (board == null) return;

                var uis = UnityEngine.Object.FindObjectsOfType<Il2Cpp.OperationBoardUI>(true);
                if (uis == null) return;
                bool anyOurs = false, anyOther = false;
                for (int i = 0; i < uis.Length; i++)
                {
                    var b = Safe(() => uis[i].CerebusOpboard);
                    if (b == null) continue;
                    if (b.GetInstanceID() == board.GetInstanceID()) anyOurs = true;
                    else anyOther = true;
                }
                if (!anyOther) return;
                _log?.Msg("[ops] the stamp says '" + s + "' and " + (anyOurs ? "some" : "none")
                        + " of this machine's laptops show it; rebinding them all "
                        + "so nobody confirms against the wrong mission");
                SyncAllTo(board, uis);

                for (int i = 0; i < uis.Length; i++)
                {
                    var ui = uis[i];
                    if (ui == null) continue;
                    try
                    {
                        var w = ui.ConfirmationWindow;
                        if (w == null || !w.isOn) continue;
                        ShutModal(w, "repainting the open confirm menu");
                        ui.EnableConfirmationMenu();
                        _log?.Msg("[ops] repainted the open confirm menu on ui#"
                                + ui.GetInstanceID() + " with '" + board.name + "'");
                    }
                    catch (Exception re)
                    { _log?.Warning("[ops] confirm repaint: " + re.Message); }
                }
            }
            catch (Exception e) { _log?.Warning("[ops] StampWatchTick: " + e.Message); }
        }

        static void Post_CancelPressed(Il2Cpp.OperationBoardUI __instance)
        {
            try
            {
                int shut = 0;
                var uis = UnityEngine.Object.FindObjectsOfType<Il2Cpp.OperationBoardUI>(true);
                for (int i = 0; i < (uis != null ? uis.Length : 0); i++)
                {
                    var ui = uis[i];
                    if (ui == null) continue;
                    try
                    {
                        var w = ui.ConfirmationWindow;
                        if (w == null) continue;
                        bool on = false;
                        try { on = w.isOn; } catch { }
                        bool act = false;
                        try { act = w.gameObject.activeSelf; } catch { }
                        if (!on && !act) continue;
                        ShutModal(w, "cancelled: one press opens a confirm on every "
                                   + "laptop and the game's cancel closes only one");
                        shut++;
                    }
                    catch { }
                }

                _confirmOpenedOn.Clear();
                _modalRetried = false;
                _launchIssued = false;
                _log?.Msg("[ops] CANCEL: shut " + shut + " confirm window(s) across the room "
                        + "and cleared the latch, so EXECUTE works again.");
            }
            catch (Exception e) { _log?.Warning("[ops] Post_CancelPressed: " + e.Message); }
        }

        static bool _syncingBoards;

        static void Pre_ExecutePressed(Il2Cpp.OperationBoardUI __instance)
        {
            try
            {
                if (__instance == null) return;

                float sliderNow = -1f;
                try
                {
                    var sl0 = __instance.OpforCountSlider;
                    sliderNow = sl0 != null ? sl0.value : -1f;
                }
                catch { }

                _syncingBoards = true;
                Il2Cpp.CerebusOpboard pressed;
                try { pressed = SyncSiblingBoards(__instance); }
                finally { _syncingBoards = false; }

                if (pressed != null && sliderNow >= 0.5f)
                {
                    int n0 = Math.Max(1, (int)Math.Round((double)sliderNow));
                    _aiChoice[pressed.GetInstanceID()] = n0;
                    try { pressed.SetAI_COUNT((float)n0); } catch { }
                    try
                    {
                        var sl1 = __instance.OpforCountSlider;
                        if (sl1 != null && Math.Abs(sl1.value - n0) > 0.01f) sl1.value = n0;
                    }
                    catch { }
                }

                LaunchSliderValue = sliderNow;
                if (pressed != null)
                {
                    try { LaunchPrepSeconds = pressed.PrepTimeInSeconds; } catch { }
                    try { LaunchTimeSlot = pressed.SelectedInfiltrationTime; } catch { }
                    try
                    {
                        LaunchDifficulty = (int)pressed.Difficulty;
                        LaunchDifficultyName = pressed.Difficulty.ToString();
                    }
                    catch { }
                    _prepObserved = false;
                }

                if (pressed != null)
                {
                    bool server = false;
                    try { server = Il2CppMirror.NetworkServer.active; } catch { }
                    CustomOp pop;
                    if (server && _byBoard.TryGetValue(pressed.GetInstanceID(), out pop) && pop != null)
                    {

                        _stampWanted = pop.SceneName;
                        StampRetry();
                        _log?.Msg("[ops] stamped '" + _stampWanted + "' at the EXECUTE press, "
                                + "so every machine's confirm shows this mission");
                    }
                }

                var w = Safe(() => __instance.ConfirmationWindow);
                if (w == null) return;

                _confirmOpenedOn.Remove(__instance.GetInstanceID());

                bool isOn = false;
                try { isOn = w.isOn; } catch { return; }
                if (!isOn) return;

                ShutModal(w, "clearing the latch for this press");
                _log?.Msg("[ops] cleared the confirm-window latch on ui#"
                        + __instance.GetInstanceID() + " so this press can open the "
                        + "menu (isOn was set, which makes the game skip it).");
            }
            catch (Exception e) { _log?.Warning("[ops] Pre_ExecutePressed: " + e.Message); }
        }

        static readonly System.Collections.Generic.HashSet<int> _confirmOpenedOn
            = new System.Collections.Generic.HashSet<int>();

        public static void OnLobbyReturn()
        {
            if (!_launchIssued && !_modalRetried && _launchLaptopId == 0) return;
            _launchIssued = false;
            _modalRetried = false;
            _launchLaptopId = 0;
            _log?.Msg("[ops] back at the laptop: launch-repair state reset");
        }

        static int _launchLaptopId;
        static bool _modalRetried;

        static bool Pre_MarkerPressed(Il2Cpp.MapInfilMarker __instance)
        {
            try { if (MapInfils.HandleMarkerClick(__instance)) return false; }
            catch (Exception e) { _log?.Warning("[ops] marker click: " + e.Message); }
            return true;
        }

        static Il2Cpp.MissionLaptop LaptopOf(Il2Cpp.OperationBoardUI ui)
        {
            try
            {
                if (ui == null) return null;
                return ui.GetComponentInParent<Il2Cpp.MissionLaptop>();
            }
            catch { return null; }
        }

        static bool ModalVisible(Il2CppMichsky.DreamOS.ModalWindowManager w)
        {
            try
            {
                if (w == null) return false;
                if (!w.gameObject.activeInHierarchy) return false;
                var t = w.transform;
                int guard = 0;
                while (t != null && guard++ < 14)
                {
                    if (!t.gameObject.activeSelf) return false;
                    var g = t.GetComponent<CanvasGroup>();
                    if (g != null && g.alpha < 0.05f) return false;
                    t = t.parent;
                }
                return true;
            }
            catch { return false; }
        }

        static System.Collections.IEnumerator SweepBlindModals()
        {
            yield return new UnityEngine.WaitForSeconds(1.0f);
            int shut = 0, kept = 0;
            Il2Cpp.OperationBoardUI[] uis = null;
            try { uis = UnityEngine.Object.FindObjectsOfType<Il2Cpp.OperationBoardUI>(true); }
            catch { }
            for (int i = 0; i < (uis != null ? uis.Length : 0); i++)
            {
                var ui = uis[i];
                if (ui == null) continue;
                try
                {
                    var w = ui.ConfirmationWindow;
                    if (w == null || !w.isOn) continue;
                    if (ModalVisible(w)) { kept++; continue; }
                    ShutModal(w, "latched but not on screen: a blind confirm window "
                                 + "on another laptop, whose Input Blocker was over the board");
                    shut++;
                }
                catch { }
            }
            if (shut > 0)
                _log?.Msg("[ops] confirm sweep: took down " + shut + " latched-but-invisible "
                        + "confirm window(s), kept " + kept + " on screen. Those blind ones "
                        + "are what made CANCEL look dead and swallowed the next EXECUTE.");
        }

        static void ShutModal(Il2CppMichsky.DreamOS.ModalWindowManager w, string why)
        {
            if (w == null) return;
            bool wasActive = false;
            try { wasActive = w.gameObject.activeSelf; } catch { }
            try { if (w.isOn) w.CloseWindow(); } catch { }
            try { w.isOn = false; } catch { }
            try { if (w.gameObject.activeSelf) w.gameObject.SetActive(false); } catch { }
            if (wasActive)
                _log?.Msg("[ops] confirm window taken down (" + why + "); its full-panel "
                        + "Input Blocker is no longer over the board");
        }

        static void DumpModal(Il2CppMichsky.DreamOS.ModalWindowManager w, string label)
        {
            try
            {
                if (w == null) { _log?.Msg("[ops]   " + label + " = null"); return; }
                bool isOn = false; try { isOn = w.isOn; } catch { }
                string anim = "?";
                try
                {
                    var a = w.mwAnimator;
                    anim = a == null ? "null"
                         : (a.isActiveAndEnabled ? "enabled" : "DISABLED")
                           + " on '" + a.gameObject.name + "'";
                }
                catch { }
                _log?.Msg("[ops]   " + label + ": isOn=" + isOn + "  animator=" + anim);

                var t = w.transform;
                int guard = 0;
                while (t != null && guard++ < 14)
                {
                    var g = t.GetComponent<CanvasGroup>();
                    string extra = g != null ? "  alpha=" + g.alpha.ToString("0.00") : "";
                    _log?.Msg("[ops]     " + (t.gameObject.activeSelf ? "on " : "OFF")
                            + " " + t.name + extra);
                    t = t.parent;
                }
            }
            catch (Exception e) { _log?.Warning("[ops] DumpModal: " + e.Message); }
        }

        static void Post_ConfirmMenu(Il2Cpp.OperationBoardUI __instance)
        {

            try
            {
                var lap = LaptopOf(__instance);
                var b = __instance != null ? Safe(() => __instance.CerebusOpboard) : null;
                if (__instance != null) _confirmOpenedOn.Add(__instance.GetInstanceID());
                _log?.Msg("[ops] confirmation menu opened on ui#"
                        + (__instance != null ? __instance.GetInstanceID() : 0)
                        + " laptop='" + (lap != null ? lap.name : "?")
                        + "' board='" + (b != null ? b.name : "?") + "'");
                if (__instance != null)
                    DumpModal(Safe(() => __instance.ConfirmationWindow), "ConfirmationWindow");
            }
            catch { }
        }

        static void Post_StartButton(Il2Cpp.OperationBoardUI __instance)
        {
            try
            {
                var b = Safe(() => __instance.CerebusOpboard);
                _log?.Msg("[ops] START pressed -> Start_Operation on '"
                        + (b != null ? b.name : "<null>") + "'");
            }
            catch { }
        }

        public static bool Build()
        {
            if (_ops.Count == 0) { _log?.Warning("[ops] nothing registered to show"); return false; }

            var laptops = AllLaptops();
            if (laptops.Count == 0) return false;

            if (_building) return true;
            _building = true;
            try
            {
                bool any = false;
                foreach (var l in laptops) any |= BuildFor(l);
                return any;
            }
            finally { _building = false; }
        }

        public static System.Collections.Generic.List<Il2Cpp.MissionLaptop> AllLaptops()
        {
            var found = new System.Collections.Generic.List<Il2Cpp.MissionLaptop>();
            try
            {
                var laps = Resources.FindObjectsOfTypeAll<Il2Cpp.MissionLaptop>();
                if (laps == null) return found;
                for (int i = 0; i < laps.Length; i++)
                {
                    var l = laps[i];
                    if (l == null || l.gameObject == null) continue;
                    if (!l.gameObject.scene.IsValid()) continue;
                    found.Add(l);
                }
            }
            catch (Exception e) { _log?.Warning("[ops] AllLaptops: " + e.Message); }
            return found;
        }

        sealed class Tab
        {
            public Il2Cpp.MissionLaptop Laptop;
            public GameObject Button;
            public GameObject Panel;
            public Transform List;
        }

        static readonly Dictionary<int, Tab> _tabs = new Dictionary<int, Tab>();

        static bool BuildFor(Il2Cpp.MissionLaptop lap)
        {
            if (lap == null) return false;
            _laptop = lap;

            var simTab = Safe(() => lap.SimulationOperationsTab);
            var simList = SafeT(() => lap.SimulationOperationList);
            if (simTab == null || simList == null) return false;

            int key = lap.GetInstanceID();
            Tab tab;
            if (!_tabs.TryGetValue(key, out tab) || tab.Button == null || tab.List == null)
            {
                tab = CreateTab(lap, simTab, simList);
                if (tab == null) return false;
                _tabs[key] = tab;
            }
            else
            {

                try
                {
                    if (tab.Button != null && tab.Button.transform.parent != null)
                        WireShippedTabs(tab.Button.transform.parent);
                }
                catch { }
            }

            if (tab.List != null && tab.List.childCount == OperationRowCount) return true;

            int rows = Populate(lap, tab);
            _log?.Msg("[ops] tab on '" + lap.gameObject.name + "': " + rows + " row(s)");
            if (rows > 0 && _rowRetries > 0)
            {

                _log?.Msg("[ops] the row built on retry; the earlier first-pass "
                        + "failure was expected and is resolved");
                _rowRetries = 0;
            }
            return rows > 0;
        }

        static Tab CreateTab(Il2Cpp.MissionLaptop lap, GameObject simTab, Transform simList)
        {
            try
            {
                var parent = simTab.transform.parent;
                var simBtn = FindSibling(parent, SimButtonName);
                var actBtn = FindSibling(parent, ActiveButtonName);
                if (simBtn == null)
                {
                    _log?.Error("[ops] no sibling '" + SimButtonName + "' under '"
                              + (parent != null ? parent.name : "?") + "'");
                    DumpSiblings(parent);
                    return null;
                }

                var tab = new Tab { Laptop = lap };

                tab.Panel = UnityEngine.Object.Instantiate(simTab, parent);
                tab.Panel.name = ListObject;
                tab.List = MatchInClone(simTab.transform, tab.Panel.transform, simList);
                if (tab.List == null)
                {
                    _log?.Warning("[ops] could not locate the list inside the panel clone; "
                                + "falling back to a search by name");
                    tab.List = FindDeep(tab.Panel.transform, simList.name);
                }
                if (tab.List == null)
                {
                    UnityEngine.Object.Destroy(tab.Panel);
                    _log?.Error("[ops] panel clone has no content transform");
                    return null;
                }
                _log?.Msg("[ops] panel clone contains " + tab.List.childCount
                        + " row(s) before clearing; list path = " + PathOf(tab.List));
                for (int i = tab.List.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.DestroyImmediate(tab.List.GetChild(i).gameObject);

                int strays = 0;
                foreach (var t in tab.Panel.GetComponentsInChildren<Transform>(true))
                {
                    if (t == null || t == tab.List) continue;
                    if (!t.name.Contains("Prefab Base")) continue;
                    UnityEngine.Object.DestroyImmediate(t.gameObject);
                    strays++;
                }
                if (strays > 0)
                    _log?.Msg("[ops] removed " + strays + " stray shipped row(s) from the clone");

                tab.Panel.SetActive(false);

                tab.Button = UnityEngine.Object.Instantiate(simBtn.gameObject, parent);
                tab.Button.name = TabObject;
                tab.Button.transform.SetSiblingIndex(simBtn.GetSiblingIndex() + 1);
                tab.Button.SetActive(true);
                PlaceByDelta(tab.Button, simBtn, actBtn);
                SetLabel(tab.Button, TabName);
                WireClick(tab);
                WireShippedTabs(parent);

                _log?.Msg("[ops] created tab on '" + lap.gameObject.name
                        + "': panel from '" + simTab.name + "', button from '" + simBtn.name
                        + "', list '" + tab.List.name + "'");
                return tab;
            }
            catch (Exception e)
            {
                _log?.Error("[ops] CreateTab: " + e.Message);
                return null;
            }
        }

        static string PathOf(Transform t)
        {
            if (t == null) return "<null>";
            var sb = new StringBuilder(t.name);
            var p = t.parent;
            int guard = 0;
            while (p != null && guard++ < 8) { sb.Insert(0, p.name + "/"); p = p.parent; }
            return sb.ToString();
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var r = FindDeep(root.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        static void PlaceByDelta(GameObject clone, Transform second, Transform first)
        {
            try
            {
                var rt = clone.GetComponent<RectTransform>();
                var b = second?.TryCast<RectTransform>();
                var a = first?.TryCast<RectTransform>();
                if (rt == null || b == null) return;

                var group = b.parent != null
                    ? b.parent.GetComponent<UnityEngine.UI.LayoutGroup>() : null;
                if (group != null)
                {
                    _log?.Msg("[ops] parent has " + group.GetIl2CppType().Name
                            + "; leaving placement to it");
                    return;
                }

                float ourW = Mathf.Max(90f, b.rect.width * 0.45f);
                rt.sizeDelta = new Vector2(ourW, b.sizeDelta.y);

                float dx = b.rect.width * 0.5f + ourW * 0.5f + 6f;
                if (a != null)
                {
                    float shipped = b.anchoredPosition.x - a.anchoredPosition.x;
                    if (Mathf.Abs(shipped) > 1f) dx = Mathf.Min(dx, Mathf.Abs(shipped));
                }

                if (Mathf.Abs(dx) < 1f)
                {
                    float w = b.rect.width;
                    if (w < 1f) w = 180f;
                    dx = w + 8f;
                    _log?.Msg("[ops] tab delta was ~0 (tabs are not offset by "
                            + "anchoredPosition); using width " + w + " instead");
                }
                rt.anchoredPosition = b.anchoredPosition + new Vector2(dx, 0f);

                rt.anchorMin = b.anchorMin;
                rt.anchorMax = b.anchorMax;
                rt.pivot = b.pivot;
                rt.localScale = b.localScale;
                rt.localRotation = b.localRotation;

                _log?.Msg("[ops] placed '" + clone.name + "' at " + rt.anchoredPosition
                        + "  (ref " + b.name + " at " + b.anchoredPosition
                        + (a != null ? ", prev " + a.name + " at " + a.anchoredPosition : "")
                        + ")  size=" + rt.sizeDelta);
            }
            catch (Exception e) { _log?.Warning("[ops] PlaceByDelta: " + e.Message); }
        }

        public static void ReportRect(string label, Transform t)
        {
            if (t == null) { _log?.Msg("[ops] rect " + label + ": null"); return; }
            try
            {
                var rt = t?.TryCast<RectTransform>();
                if (rt == null) { _log?.Msg("[ops] rect " + label + ": not a RectTransform"); return; }
                var c = new Il2CppInterop.Runtime.InteropTypes.Arrays
                    .Il2CppStructArray<Vector3>(4);
                rt.GetWorldCorners(c);
                string chain = "";
                var p = t;
                while (p != null && chain.Length < 160)
                {
                    if (!p.gameObject.activeSelf) chain += " OFF:" + p.name;
                    p = p.parent;
                }
                _log?.Msg("[ops] rect " + label
                        + "  anchoredPos=" + rt.anchoredPosition
                        + " size=" + rt.sizeDelta
                        + " scale=" + rt.localScale
                        + " activeInHierarchy=" + t.gameObject.activeInHierarchy
                        + (chain.Length > 0 ? "  inactive parents:" + chain : ""));
                _log?.Msg("        worldCorners BL=" + c[0] + " TR=" + c[2]);
            }
            catch (Exception e) { _log?.Warning("[ops] ReportRect " + label + ": " + e.Message); }
        }

        static Transform FindSibling(Transform parent, string name)
        {
            if (parent == null) return null;
            for (int i = 0; i < parent.childCount; i++)
            {
                var c = parent.GetChild(i);
                if (c != null && string.Equals(c.name, name, StringComparison.OrdinalIgnoreCase))
                    return c;
            }
            return null;
        }

        static void DumpSiblings(Transform parent)
        {
            if (parent == null) return;
            var sb = new StringBuilder();
            for (int i = 0; i < parent.childCount; i++)
            {
                if (sb.Length > 0) sb.Append(" | ");
                sb.Append(parent.GetChild(i).name);
            }
            _log?.Msg("[ops] siblings under '" + parent.name + "': " + sb);
        }

        static void WireClick(Tab tab)
        {
            try
            {
                foreach (var c in tab.Button.GetComponents<Component>())
                {
                    if (c == null) continue;
                    var n = c.GetIl2CppType().Name;
                    if (n == "PanelButton" || n == "Button")
                    {
                        var beh = c.TryCast<Behaviour>();
                        if (beh != null) { beh.enabled = false; _log?.Msg("[ops] disabled inherited " + n); }
                    }
                }

                var trig = tab.Button.GetComponent<EventTrigger>();
                if (trig == null) trig = tab.Button.AddComponent<EventTrigger>();
                trig.triggers = new Il2CppSystem.Collections.Generic.List<EventTrigger.Entry>();

                var entry = new EventTrigger.Entry();
                entry.eventID = EventTriggerType.PointerClick;
                entry.callback = new EventTrigger.TriggerEvent();
                int key = tab.Laptop != null ? tab.Laptop.GetInstanceID() : 0;
                entry.callback.AddListener((UnityEngine.Events.UnityAction<BaseEventData>)
                    ((BaseEventData _) => OnTabClicked(key)));
                trig.triggers.Add(entry);

                _log?.Msg("[ops] click wired via EventTrigger on '" + tab.Button.name + "'");
            }
            catch (Exception e) { _log?.Error("[ops] WireClick: " + e.Message); }
        }

        static void WireShippedTabs(Transform parent)
        {
            foreach (var nm in new[] { ActiveButtonName, SimButtonName })
            {
                var t = FindSibling(parent, nm);
                if (t == null) continue;

                if (!_tagged.Add(t.gameObject.GetInstanceID())) continue;
                try
                {
                    var trig = t.gameObject.GetComponent<EventTrigger>();
                    if (trig == null) trig = t.gameObject.AddComponent<EventTrigger>();
                    if (trig.triggers == null)
                        trig.triggers =
                            new Il2CppSystem.Collections.Generic.List<EventTrigger.Entry>();
                    var e = new EventTrigger.Entry();
                    e.eventID = EventTriggerType.PointerClick;
                    e.callback = new EventTrigger.TriggerEvent();
                    e.callback.AddListener((UnityEngine.Events.UnityAction<BaseEventData>)
                        ((BaseEventData _) =>
                        {

                            _ourTabSelected.Clear();
                            HideAll(); RestoreNativeTabs();
                        }));
                    trig.triggers.Add(e);
                    _log?.Msg("[ops] watching shipped tab '" + nm + "' to hide ours");
                }
                catch (Exception ex) { _log?.Warning("[ops] WireShippedTabs: " + ex.Message); }
            }
        }

        static readonly HashSet<int> _ourTabSelected = new HashSet<int>();

        static bool _backRelisting;

        static void Post_SetCurrentPage(Il2Cpp.MissionLaptopDisplayer __instance)
        {
            try
            {
                if (__instance == null) return;
                var st = Il2Cpp.MissionLaptopNetworkState.instance;
                int page = st != null ? st.CurrentLaptopPage : 0;
                if (page != 0 && page != 1) return;
                var lap = __instance.MissionLaptop;
                if (lap == null) return;
                int key = lap.GetInstanceID();

                if (NativeTabShowingOn(lap))
                {
                    if (_ourTabSelected.Remove(key))
                        _log?.Msg("[ops] the player is on one of the game's own tabs; "
                                + "ODG OPS is no longer the tab to come back to on '"
                                + lap.gameObject.name + "'");
                    return;
                }

                bool oursHere = _ourTabSelected.Contains(key);
                if (!oursHere)
                {
                    try
                    {
                        var ui = lap.GetComponentInChildren<Il2Cpp.OperationBoardUI>(true);
                        var b = ui != null ? Safe(() => ui.CerebusOpboard) : null;
                        if (b != null && _byBoard.ContainsKey(b.GetInstanceID())) oursHere = true;
                    }
                    catch { }
                }
                if (!oursHere) return;

                if (page == 1)
                {
                    if (_ourOperation == null || _backRelisting) return;
                    _backRelisting = true;
                    try
                    {
                        lap.SpawnTargetPackages(_ourOperation);
                        _log?.Msg("[ops] back to the mission list: rebuilt it from '"
                                + _ourOperation.name + "' so it shows DUKE'S OPS "
                                + "rather than the game's own operations");
                    }
                    catch (Exception se)
                    { _log?.Warning("[ops] back-list rebuild: " + se.Message); }
                    finally { _backRelisting = false; }
                    return;
                }

                OnTabClicked(key);

                try { MelonLoader.MelonCoroutines.Start(HoldOurTab(key)); } catch { }
                _log?.Msg("[ops] back to the front page: this laptop was on an ODG mission, "
                        + "so ODG OPS is showing again rather than the simulation list");
            }
            catch (Exception e) { _log?.Warning("[ops] Post_SetCurrentPage: " + e.Message); }
        }

        static System.Collections.IEnumerator HoldOurTab(int laptopKey)
        {
            for (int i = 0; i < 6; i++)
            {
                yield return new UnityEngine.WaitForSeconds(0.08f);
                Tab t;
                if (!_tabs.TryGetValue(laptopKey, out t) || t == null || t.Panel == null) yield break;
                if (!t.Panel.activeInHierarchy)
                {
                    try { OnTabClicked(laptopKey); } catch { }
                }
            }
        }

        static void OnTabClicked(int laptopKey)
        {
            try
            {
                Tab tab;
                if (!_tabs.TryGetValue(laptopKey, out tab) || tab == null) return;
                _ourTabSelected.Add(laptopKey);
                var lap = tab.Laptop;

                if (lap != null)
                {
                    var at = Safe(() => lap.ActiveOperationsTab);
                    var st = Safe(() => lap.SimulationOperationsTab);

                    if (at != null && at.activeSelf) { at.SetActive(false); _nativeHidden.Add(at); }
                    if (st != null && st.activeSelf) { st.SetActive(false); _nativeHidden.Add(st); }
                    RecordAlpha(Safe(() => lap.ActiveOperationButtonSelected));
                    RecordAlpha(Safe(() => lap.SimulationOperationButtonSelected));
                }
                if (tab.Panel != null) tab.Panel.SetActive(true);
                _log?.Msg("[ops] ODG OPS selected on '"
                        + (lap != null ? lap.gameObject.name : "?") + "'");
            }
            catch (Exception e) { _log?.Error("[ops] tab click: " + e.Message); }
        }

        static void HideAll()
        {
            foreach (var kv in _tabs)
                if (kv.Value != null && kv.Value.Panel != null)
                    kv.Value.Panel.SetActive(false);

        }

        static void SetAlpha(CanvasGroup g, float a)
        { if (g != null) { try { g.alpha = a; } catch { } } }

        public static void OnSceneGone()
        {

            try
            {
                foreach (var kv in _tabs)
                {
                    var t = kv.Value;
                    if (t != null && t.Panel != null && t.Panel.scene.isLoaded)
                        return;
                }
            }
            catch { }

            _tabs.Clear();
            _tagged.Clear();

            RestoreNativeTabs();

            _launchIssued = false;
            _modalRetried = false;
            _launchLaptopId = 0;
            _setupInFlight = false;
            _ourRowClicked = false;
            _ourPkgClicked = false;
            _ourPkgIndex = -1;
            _ourLaunch = false;
            BoardState.OnSceneGone();
            MapInfils.OnSceneGone();
        }

        static int Populate(Il2Cpp.MissionLaptop lap, Tab tab)
        {
            var list = tab != null ? tab.List : null;
            if (list == null) return 0;
            for (int i = list.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(list.GetChild(i).gameObject);

            var template = FindTemplateOperation();
            if (template == null) { _log?.Error("[ops] no CerebusOperation to clone"); return 0; }

            int n = 0;
            if (_ops.Count > 0)
            {
                var op = _ops[0];
                try
                {
                    var clone = UnityEngine.Object.Instantiate(template);
                    if (clone == null) return 0;
                    clone.name = OpMarker + op.Id;
                    clone.hideFlags = HideFlags.DontSave;
                    try { clone.isSimulation = true; } catch { }
                    try { clone.isLocked = false; } catch { }

                    ConfigurePackages(clone, _ops);

                    int pk = 0;
                    try { var a = clone.TargetPackages; pk = a != null ? a.Length : -1; } catch { }
                    _log?.Msg("[ops] CreateInstance row for operation '" + clone.name
                            + "' holding " + pk + " package(s) on laptop '"
                            + lap.gameObject.name + "'");

                    int before = list.childCount;
                    clone.CreateInstance(list, lap);
                    if (list.childCount > before)
                    {
                        var row = list.GetChild(list.childCount - 1);
                        LabelRow(row, op, template);
                        MarkRow(row, clone);
                    }
                    n++;
                }
                catch (Exception e)
                {

                    _rowRetries++;
                    if (!_loggedRowStack)
                    {
                        _loggedRowStack = true;
                        _log?.Msg("[ops] row '" + op.Title + "' not ready on the first pass "
                                + "(the laptop is still building its own UI); will retry. "
                                + "Detail, once:\n" + e);
                    }
                }
            }
            return n;
        }

        static void MarkRow(Transform row, Il2Cpp.CerebusOperation clone)
        {
            if (row == null) return;
            try
            {
                _ourOperation = clone;
                if (!_tagged.Add(row.gameObject.GetInstanceID())) return;
                var trig = row.gameObject.GetComponent<EventTrigger>();
                if (trig == null) trig = row.gameObject.AddComponent<EventTrigger>();

                if (trig.triggers == null)
                    trig.triggers =
                        new Il2CppSystem.Collections.Generic.List<EventTrigger.Entry>();
                var e = new EventTrigger.Entry();
                e.eventID = EventTriggerType.PointerDown;
                e.callback = new EventTrigger.TriggerEvent();
                e.callback.AddListener((UnityEngine.Events.UnityAction<BaseEventData>)
                    ((BaseEventData _) => { _ourRowClicked = true; _ourRowFrame = UnityEngine.Time.frameCount; }));
                trig.triggers.Add(e);
            }
            catch (Exception ex) { _log?.Warning("[ops] MarkRow: " + ex.Message); }
        }

        static bool _dumpedRow;

        static void LabelRow(Transform row, CustomOp op, Il2Cpp.CerebusOperation template)
        {
            if (row == null) return;
            try
            {
                row.gameObject.name = OpMarker + op.Id + "_ROW";
                var tmps = row.GetComponentsInChildren<Il2CppTMPro.TextMeshProUGUI>(true);
                if (tmps == null || tmps.Length == 0)
                {
                    _log?.Warning("[ops] row for '" + op.Title + "' has no text to label");
                    return;
                }

                if (!_dumpedRow)
                {
                    _dumpedRow = true;
                    _log?.Msg("[ops] ROW FIELDS (" + tmps.Length + "):");
                    for (int i = 0; i < tmps.Length; i++)
                        if (tmps[i] != null)
                            _log?.Msg("        [" + i + "] '" + tmps[i].gameObject.name
                                    + "' = \"" + tmps[i].text + "\"  active="
                                    + tmps[i].gameObject.activeInHierarchy);
                }

                int replaced = 0;
                for (int i = 0; i < tmps.Length; i++)
                {
                    var t = tmps[i];
                    if (t == null) continue;

                    var loc = t.GetComponent<Il2Cpp.LocalizationTMPEvent>();
                    if (loc != null) loc.enabled = false;

                    string cur = t.text ?? "";
                    string flat = cur.Trim();
                    if (flat.Length == 0) continue;

                    string upper = flat.ToUpperInvariant();
                    if (upper.Contains("MIN") || upper.Contains("HOUR"))
                        t.text = op.Duration;
                    else if (upper.Contains("HIGH") || upper.Contains("LOW")
                          || upper.Contains("VARIABLE") || upper.Contains("UNKNOWN")
                          || upper.Contains("SEVERE") || upper.Contains("MODERATE"))
                        t.text = op.ThreatLevel;
                    else
                        t.text = op.Title;
                    replaced++;
                }
                _log?.Msg("[ops] row '" + op.Title + "': rewrote " + replaced
                        + " of " + tmps.Length + " text field(s)");
            }
            catch (Exception e) { _log?.Warning("[ops] LabelRow: " + e.Message); }
        }

        static void ConfigurePackages(Il2Cpp.CerebusOperation clone, List<CustomOp> ops)
        {
            var packages = Safe(() => clone.TargetPackages);
            if (packages == null || packages.Length == 0)
            {
                _log?.Warning("[ops] no TargetPackages to model on");
                return;
            }

            var src = PickWalkInPackage(packages);
            if (src == null) { _log?.Warning("[ops] no package to model on"); return; }

            _templateBoardIds.Clear();
            for (int ti = 0; ti < packages.Length; ti++)
            {
                var tb = packages[ti] != null ? Safe(() => packages[ti].TargetPackage) : null;
                if (tb == null) continue;
                try { _templateBoardIds.Add(tb.GetInstanceID()); } catch { }
            }
            var srcBoard = Safe(() => src.TargetPackage);

            _boardByIndex.Clear();

            var arr = new Il2CppInterop.Runtime.InteropTypes.Arrays
                .Il2CppReferenceArray<Il2Cpp.CerebusTargetPackage>(ops.Count);
            int built = 0;

            for (int i = 0; i < ops.Count; i++)
            {
                var op = ops[i];

                var pkg = UnityEngine.Object.Instantiate(src);
                if (pkg == null) { _log?.Error("[ops] package clone failed for " + op.Id); continue; }
                pkg.name = OpMarker + op.Id + "_PKG";
                pkg.hideFlags = HideFlags.DontSave;

                try { pkg.OPERATION_AREA_OF_OPERATION = op.PackageName; } catch { }
                try { pkg.isLocked = false; } catch { }
                try { pkg.isSimulation = true; } catch { }

                try { pkg.MissionRequiredForUnlock = null; } catch { }
                try { pkg.progressionUnlockCode = ""; } catch { }

                try { pkg.TargetPackageIndex = i; } catch { }

                var board = srcBoard;
                if (board != null)
                {
                    var boardClone = UnityEngine.Object.Instantiate(board);
                    if (boardClone != null)
                    {
                        boardClone.name = OpMarker + op.Id + "_BOARD";
                        boardClone.hideFlags = HideFlags.DontSave;
                        try { pkg.TargetPackage = boardClone; } catch { }
                        ApplyBoardValues(boardClone, op);
                        _byBoard[boardClone.GetInstanceID()] = op;
                        board = boardClone;
                    }
                }
                _boardByIndex.Add(board);
                if (i == 0) _ourBoard = board;

                arr[i] = pkg;
                built++;
            }

            try
            {
                clone.TargetPackages = arr;
                var names = new List<string>();
                for (int i = 0; i < ops.Count; i++) names.Add(i + ":" + ops[i].PackageName);
                _log?.Msg("[ops] DUKE'S OPS holds " + built + " mission(s), siblings in one "
                        + "list: " + string.Join(", ", names.ToArray())
                        + "  (template had " + packages.Length + ")");
            }
            catch (Exception e) { _log?.Error("[ops] TargetPackages assign: " + e.Message); }
        }

        internal const int DaySlot = 0;
        internal const int NightSlot = 1;
        const string NightHostScene = "Suburb Night";

        static readonly HashSet<int> _tpOverridden = new HashSet<int>();

        static string _hostSceneDayCached;
        static string HostSceneDay()
        {
            if (_hostSceneDayCached != null) return _hostSceneDayCached;
            try
            {
                var cfg = MelonPreferences.GetCategory("MapLoader")
                       ?? MelonPreferences.CreateCategory("MapLoader");
                var e = cfg.GetEntry<string>("HostSceneDay")
                     ?? cfg.CreateEntry("HostSceneDay", "Mexico Depot Day",
                          description: "Which stock level hosts a DAY launch of our maps. The "
                                     + "board's day slot loads this scene; candidates need a "
                                     + "RaidManager with bots and exfil zones ('Afghan_Compounds_Day' "
                                     + "is the surveyed backup). Changed without a rebuild; takes "
                                     + "effect next game start.");
                _hostSceneDayCached = e.Value;
            }
            catch { _hostSceneDayCached = "Mexico Depot Day"; }
            if (string.IsNullOrEmpty(_hostSceneDayCached))
                _hostSceneDayCached = "Mexico Depot Day";
            return _hostSceneDayCached;
        }

        static readonly Dictionary<string, string> _hostDayCached
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        static string HostSceneDayFor(CustomOp op)
        {
            string map = op != null ? op.SceneName : null;
            if (string.IsNullOrEmpty(map)) return HostSceneDay();
            string got;
            if (_hostDayCached.TryGetValue(map, out got)) return got;

            string def = string.Equals(map, "Creek", StringComparison.OrdinalIgnoreCase)
                       ? "Suburb Night"
                       : string.Equals(map, "SmallTown", StringComparison.OrdinalIgnoreCase)
                       ? "Armenia Day"
                       : string.Equals(map, "Backroad", StringComparison.OrdinalIgnoreCase)
                       ? "Suburb Night"
                       : string.Equals(map, "Villa", StringComparison.OrdinalIgnoreCase)
                       ? "Suburb Night"

                       : string.Equals(map, "LittleRocketMan", StringComparison.OrdinalIgnoreCase)
                       ? "Suburb Night"

                       : string.Equals(map, "Trenches", StringComparison.OrdinalIgnoreCase)
                       ? "Suburb Night"
                       : string.Equals(map, "VirginiaBeach", StringComparison.OrdinalIgnoreCase)
                       ? "Suburb Night"
                       : string.Equals(map, "Killhouse", StringComparison.OrdinalIgnoreCase)
                       ? "Suburb Night" : null;
            string val = null;
            if (def != null)
            {
                try
                {
                    var cfg = MelonPreferences.GetCategory("MapLoader")
                           ?? MelonPreferences.CreateCategory("MapLoader");
                    var e = cfg.GetEntry<string>("HostSceneDay" + map)
                         ?? cfg.CreateEntry("HostSceneDay" + map, def,
                              description: "Which stock level hosts a DAY launch of " + map
                                         + ". Needs RaidManager + InfiltrationManager; "
                                         + "'Mexico Depot Day' is the proven fallback.");
                    val = e.Value;
                }
                catch { val = def; }
                if (string.IsNullOrEmpty(val)) val = def;
            }
            else
            {
                val = HostSceneDay();
            }
            _hostDayCached[map] = val;
            return val;
        }

        static int DefaultSlotFor(CustomOp op)
        {
            var e = op != null ? MapIdentity.Get(op.SceneName) : null;
            bool nightHost = e != null && e.NightSafe && !e.OwnSky;
            return nightHost ? NightSlot : DaySlot;
        }

        static void ApplyTargetPackage(Il2Cpp.CerebusOpboard board, CustomOp op)
        {
            try
            {
                int id = board.GetInstanceID();
                if (_tpOverridden.Contains(id)) return;

                if (_templateBoardIds.Contains(id))
                {
                    _log?.Error("[ops] TARGETPACKAGE override asked for a TEMPLATE board; "
                              + "refused. This is a bug in the caller.");
                    return;
                }

                string display = !string.IsNullOrEmpty(op.AreaOfOperation)
                    ? op.AreaOfOperation : op.PackageName;

                var day = new Il2Cpp.TARGETPACKAGE_DETAILS();
                day.OPERATION_SCENE = HostSceneDayFor(op);
                day.DISPLAY_NAME = display;
                day.INFILTRATION_TIME = "0800";

                var night = new Il2Cpp.TARGETPACKAGE_DETAILS();
                night.OPERATION_SCENE = NightHostScene;
                night.DISPLAY_NAME = display;
                night.INFILTRATION_TIME = "2300";

                var arr = new Il2CppInterop.Runtime.InteropTypes.Arrays
                    .Il2CppReferenceArray<Il2Cpp.TARGETPACKAGE_DETAILS>(2);
                arr[DaySlot] = day;
                arr[NightSlot] = night;
                board.TARGETPACKAGE = arr;
                _tpOverridden.Add(id);
                _log?.Msg("[ops] '" + op.PackageName + "' hosts: day='" + day.OPERATION_SCENE
                        + "' (0800) night='" + NightHostScene + "' (2300), default slot "
                        + DefaultSlotFor(op) + ". The host level is a choice now, not "
                        + "whatever board the template scorer happened to pick.");
            }
            catch (Exception e)
            {

                _log?.Error("[ops] TARGETPACKAGE override failed: " + e.Message
                          + " (the mission keeps the template's host scene)");
            }
        }

        static void ApplyBoardValues(Il2Cpp.CerebusOpboard board, CustomOp op)
        {
            if (board == null || op == null) return;

            ApplyTargetPackage(board, op);

            try { board.MinAI = 1; } catch { }
            try { board.MaxAI = Math.Max(60, op.MaxAI); } catch { }

            bool firstTime = false;
            try { firstTime = _defaultsApplied.Add(board.GetInstanceID()); } catch { }
            if (firstTime)
            {
                try { board.PrepTimeInSeconds = op.PrepSeconds; } catch { }

                try
                {
                    board.SelectedInfiltrationTime =
                        _tpOverridden.Contains(board.GetInstanceID())
                            ? DefaultSlotFor(op) : op.DefaultInfilTime;
                }
                catch { }
            }

            try
            {

                var pts = MapInfils.Load(op.SceneName);
                string[] names = null;
                if (pts != null && pts.Count > 0)
                {
                    names = new string[pts.Count];
                    for (int i = 0; i < pts.Count; i++) names[i] = pts[i].Name;
                }
                else names = op.Infils;

                if (names != null && names.Length > 0)
                {
                    var arr = new Il2CppInterop.Runtime.InteropTypes.Arrays
                        .Il2CppStringArray(names.Length);
                    for (int i = 0; i < names.Length; i++) arr[i] = names[i];
                    board.AvailableInfils = arr;
                }
            }
            catch (Exception e) { _log?.Warning("[ops] infils: " + e.Message); }

            try { board.isLocked = false; } catch { }
            try { board.isSimulation = true; } catch { }
            try { board.isCompleted = false; } catch { }
            try { board.requireEnoughInfils = false; } catch { }
            try { board.requireEnoughExfils = false; } catch { }

            try
            {
                var prefab = op.MapPrefab != null
                    ? op.MapPrefab
                    : MapInfils.BuildBoardPrefab(op.SceneName);

                if (prefab == null)
                    prefab = MapInfils.BuildFallbackPrefab(op.SceneName);
                if (prefab != null) board.MapPrefab = prefab;
                else _log?.Error("[ops] '" + op.PackageName + "' is showing the TEMPLATE "
                               + "board's map because both the real prefab and the "
                               + "placeholder failed to build.");
            }
            catch (Exception e) { _log?.Warning("[ops] MapPrefab: " + e.Message); }

            if (!_loggedBoardReadback)
            {
                _loggedBoardReadback = true;
                try
                {
                    _log?.Msg("[ops] board readback: isSimulation=" + board.isSimulation
                            + " requireInfils=" + board.requireEnoughInfils
                            + " requireExfils=" + board.requireEnoughExfils
                            + " isLocked=" + board.isLocked
                            + " mapPrefab=" + (board.MapPrefab != null
                                ? board.MapPrefab.name : "<null>"));
                }
                catch (Exception e) { _log?.Warning("[ops] readback failed: " + e.Message); }
            }
        }

        static bool _loggedBoardReadback;

        static readonly HashSet<int> _defaultsApplied = new HashSet<int>();

        static void ShowMapPreview(Il2Cpp.OperationBoardUI ui, CustomOp op)
        {
            bool shown = false;
            try
            {
                shown |= MapPreview.Show(Safe(() => ui.MapParent), op.SceneName);
                shown |= MapPreview.Show(Safe(() => ui.FullscreenMapParent), op.SceneName);
            }
            catch (Exception e) { _log?.Warning("[ops] map preview: " + e.Message); }

            if (!shown)
                _log?.Warning("[ops] '" + op.Title + "' is showing the map it was cloned "
                            + "from. Expected a plan view at "
                            + MapPreview.PreviewDir + "\\" + op.SceneName + ".png");
        }

        static void ShowOneParameterGroup(Il2Cpp.OperationBoardUI ui)
        {
            if (ui == null) return;
            try
            {
                SetGroup(Safe(() => ui.SimulationParameters), true);
                SetGroup(Safe(() => ui.HVTSimulationParameters), false);
                SetGroup(Safe(() => ui.PVPParameters), false);
                SetGroup(Safe(() => ui.FFAParameters), false);
            }
            catch (Exception e) { _log?.Warning("[ops] parameters: " + e.Message); }
        }

        static void SetGroup(
            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<GameObject> group,
            bool on)
        {
            try
            {
                if (group == null) return;
                for (int i = 0; i < group.Length; i++)
                {
                    var go = group[i];
                    if (go != null && go.activeSelf != on) go.SetActive(on);
                }
            }
            catch { }
        }

        static void ShowSitrep(Il2Cpp.OperationBoardUI ui, CustomOp op)
        {
            if (ui == null || op == null || string.IsNullOrEmpty(op.Description)) return;
            try
            {

                var loc = Safe(() => ui.SituationReportText);
                if (loc == null) return;
                loc.enabled = false;
                var t = loc.GetComponent<Il2CppTMPro.TextMeshProUGUI>();
                if (t == null) return;
                t.text = op.Description;
            }
            catch (Exception e) { _log?.Warning("[ops] sitrep: " + e.Message); }
        }

        static Transform MatchInClone(Transform origRoot, Transform cloneRoot, Transform target)
        {
            if (origRoot == null || cloneRoot == null || target == null) return null;
            var path = new List<int>();
            var t = target;
            while (t != null && t != origRoot)
            {
                path.Add(t.GetSiblingIndex());
                t = t.parent;
            }
            if (t != origRoot) return null;
            path.Reverse();
            var c = cloneRoot;
            foreach (var idx in path)
            {
                if (idx < 0 || idx >= c.childCount) return null;
                c = c.GetChild(idx);
            }
            return c;
        }

        static readonly string[] GroundCallsigns =
        {
            "ALPHA", "BRAVO", "DELTA",
        };

        static bool IsGroundCallsign(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < GroundCallsigns.Length; i++)
                if (s.IndexOf(GroundCallsigns[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        static Il2Cpp.CerebusTargetPackage PickWalkInPackage(
            Il2CppInterop.Runtime.InteropTypes.Arrays
                .Il2CppReferenceArray<Il2Cpp.CerebusTargetPackage> packages)
        {
            Il2Cpp.CerebusTargetPackage best = null;
            int bestScore = int.MinValue;
            for (int i = 0; i < packages.Length; i++)
            {
                var pk = packages[i];
                if (pk == null) continue;
                int score = ScoreOne(pk, "template", i, true);
                if (score > bestScore) { bestScore = score; best = pk; }
            }
            if (best != null)
            {
                string bn = "";
                try { var bd = Safe(() => best.TargetPackage); if (bd != null) bn = bd.name ?? ""; } catch { }
                _log?.Msg("[ops] cloning template board '" + bn + "' (best walk-in score "
                        + bestScore + "). Package 0 is the boat mission and is only used "
                        + "if nothing scores higher.");
                if (bestScore <= 0)
                    _log?.Warning("[ops] no walk-in board in the template: every package is "
                                + "helicopter or has no infils, so the confirm-infil page may "
                                + "still offer air insertions. Nothing in the mod can change "
                                + "the METHOD once the board is cloned.");
            }
            return best;
        }

        static Il2Cpp.CerebusOperation FindTemplateOperation()
        {
            var mgrs = Resources.FindObjectsOfTypeAll<Il2Cpp.OperationsManager>();
            if (mgrs == null) return null;

            Il2Cpp.CerebusOperation bestOp = null, anyOp = null;
            int bestScore = int.MinValue;

            for (int m = 0; m < mgrs.Length; m++)
            {
                if (mgrs[m] == null) continue;
                foreach (var arr in new[] { Safe(() => mgrs[m].SimulationOperations),
                                            Safe(() => mgrs[m].ActiveOperations) })
                {
                    if (arr == null) continue;
                    for (int i = 0; i < arr.Length; i++)
                    {
                        var op = arr[i];
                        if (op == null) continue;
                        if (anyOp == null) anyOp = op;
                        var pks = Safe(() => op.TargetPackages);
                        if (pks == null) continue;
                        int score = ScorePackages(pks, op.name);
                        if (score > bestScore) { bestScore = score; bestOp = op; }
                    }
                }
            }

            if (bestOp != null)
                _log?.Msg("[ops] template operation '" + bestOp.name + "' wins with walk-in "
                        + "score " + bestScore + ". Chosen by scoring every operation's "
                        + "packages, because taking the first one always landed on the boat.");
            return bestOp ?? anyOp;
        }

        static int ScorePackages(
            Il2CppInterop.Runtime.InteropTypes.Arrays
                .Il2CppReferenceArray<Il2Cpp.CerebusTargetPackage> packages, string opName)
        {
            int best = int.MinValue;
            for (int i = 0; i < packages.Length; i++)
            {
                var pk = packages[i];
                if (pk == null) continue;
                int sc = ScoreOne(pk, opName, i, false);
                if (sc > best) best = sc;
            }
            return best;
        }

        static int ScoreOne(Il2Cpp.CerebusTargetPackage pk, string opName, int index, bool log)
        {
            var bd = Safe(() => pk.TargetPackage);
            string bn = "";
            try { if (bd != null) bn = bd.name ?? ""; } catch { }
            int ground = 0, heli = 0;
            try
            {
                var infils = bd != null ? bd.AvailableInfils : null;
                for (int k = 0; k < (infils != null ? infils.Length : 0); k++)
                {
                    string nm = infils[k];
                    if (string.IsNullOrEmpty(nm)) continue;
                    if (IsGroundCallsign(nm)) ground++; else heli++;
                }
            }
            catch { }

            int total = ground + heli;
            int score = ground * 10 - heli * 10 + (total >= 3 ? 5 : 0) - (total == 1 ? 20 : 0);
            if (log)
                _log?.Msg("[ops]   " + opName + " pkg " + index + " board '" + bn + "': "
                        + ground + " ground, " + heli + " heli, " + total + " total -> "
                        + score);
            return score;
        }

        static Il2Cpp.CerebusOperation First(
            Il2CppInterop.Runtime.InteropTypes.Arrays
                .Il2CppReferenceArray<Il2Cpp.CerebusOperation> a)
        {
            if (a == null) return null;
            for (int i = 0; i < a.Length; i++) if (a[i] != null) return a[i];
            return null;
        }

        public static Il2Cpp.MissionLaptop FindLaptop()
        {
            var laps = Resources.FindObjectsOfTypeAll<Il2Cpp.MissionLaptop>();
            if (laps == null) return null;
            Il2Cpp.MissionLaptop fallback = null;
            for (int i = 0; i < laps.Length; i++)
            {
                var l = laps[i];
                if (l == null || l.gameObject == null) continue;
                if (!l.gameObject.scene.IsValid()) continue;
                if (l.gameObject.activeInHierarchy) return l;
                if (fallback == null) fallback = l;
            }
            return fallback;
        }

        static T Safe<T>(Func<T> f) where T : class
        { try { return f(); } catch { return null; } }

        static Transform SafeT(Func<Transform> f)
        { try { return f(); } catch { return null; } }

        static void SetLabel(GameObject go, string text)
        {
            try
            {
                var tmps = go.GetComponentsInChildren<Il2CppTMPro.TextMeshProUGUI>(true);
                if (tmps == null || tmps.Length == 0)
                { _log?.Warning("[ops] no TextMeshProUGUI under the tab to label"); return; }
                for (int i = 0; i < tmps.Length; i++)
                {
                    if (tmps[i] == null) continue;

                    var loc = tmps[i].GetComponent<Il2Cpp.LocalizationTMPEvent>();
                    if (loc != null) loc.enabled = false;
                    tmps[i].text = text;
                }
            }
            catch (Exception e) { _log?.Warning("[ops] SetLabel: " + e.Message); }
        }

        public static void DumpUi()
        {
            var lap = FindLaptop();
            if (lap == null) { _log?.Error("[ops] no MissionLaptop"); return; }
            _log?.Msg("======== laptop UI ========");
            _log?.Msg("laptop '" + lap.gameObject.name + "' active=" + lap.gameObject.activeInHierarchy);
            DumpGo("ActiveOperationsTab", Safe(() => lap.ActiveOperationsTab));
            DumpGo("SimulationOperationsTab", Safe(() => lap.SimulationOperationsTab));
            DumpTr("ActiveOperationsList", SafeT(() => lap.ActiveOperationsList));
            DumpTr("SimulationOperationList", SafeT(() => lap.SimulationOperationList));
            DumpTr("opBoardParent", SafeT(() => lap.opBoardParent));
            _log?.Msg("  tabs built: " + _tabs.Count);
            foreach (var kv in _tabs)
                _log?.Msg("      on '" + (kv.Value.Laptop != null ? kv.Value.Laptop.gameObject.name : "?")
                        + "'  button=" + (kv.Value.Button != null ? kv.Value.Button.name : "<none>")
                        + "  rows=" + (kv.Value.List != null ? kv.Value.List.childCount : -1)
                        + "  panelActive=" + (kv.Value.Panel != null && kv.Value.Panel.activeSelf));

            var uis = Resources.FindObjectsOfTypeAll<Il2Cpp.OperationBoardUI>();
            _log?.Msg("  OperationBoardUI instances: " + (uis != null ? uis.Length : 0));

            _log?.Msg("registered ops: " + _ops.Count);
            foreach (var o in _ops)
                _log?.Msg("   '" + o.Title + "' scene=" + o.SceneName + " AI=" + o.MinAI + "-"
                        + o.MaxAI + " prep=" + o.PrepSeconds + "s infils="
                        + (o.Infils != null ? o.Infils.Length : 0)
                        + " map=" + (o.MapPrefab != null ? o.MapPrefab.name : "<none>"));
            _log?.Msg("======== end laptop UI ========");
        }

        static void DumpGo(string label, GameObject go)
        {
            if (go == null) { _log?.Msg("  " + label + ": null"); return; }
            _log?.Msg("  " + label + ": '" + go.name + "' active=" + go.activeInHierarchy
                    + " sibling=" + go.transform.GetSiblingIndex());
            DumpComponents(go, "      ");
            var p = go.transform.parent;
            if (p != null)
            {
                var sb = new StringBuilder();
                for (int i = 0; i < p.childCount && i < 12; i++)
                {
                    if (sb.Length > 0) sb.Append(" | ");
                    sb.Append(p.GetChild(i).name);
                }
                _log?.Msg("      siblings: " + sb);
            }
        }

        static void DumpTr(string label, Transform t)
        {
            if (t == null) { _log?.Msg("  " + label + ": null"); return; }
            _log?.Msg("  " + label + ": '" + t.name + "' children=" + t.childCount
                    + " active=" + t.gameObject.activeInHierarchy);
        }

        static void DumpComponents(GameObject go, string indent)
        {
            try
            {
                var comps = go.GetComponents<Component>();
                var sb = new StringBuilder();
                for (int i = 0; i < comps.Length; i++)
                {
                    if (comps[i] == null) continue;
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(comps[i].GetIl2CppType().Name);
                }
                _log?.Msg(indent + "components: " + sb);
            }
            catch (Exception e) { _log?.Warning(indent + "components: " + e.Message); }
        }
    }
}
