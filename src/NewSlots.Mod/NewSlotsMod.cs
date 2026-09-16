// ODG Operations - NewSlots
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(NewSlots.NewSlotsMod), "NewSlots", "1.1.1", "ArchdukePierre")]
[assembly: MelonGame(null, null)]

namespace NewSlots
{

    public class NewSlotsMod : MelonMod
    {
        internal static HarmonyLib.Harmony Harm;
        internal static bool QuietName;

        internal static float PatchScale = 1.30f;
        internal static bool PatchDepthFlip = false;
        static MelonPreferences_Category _cfg;

        internal static float BuildBudgetMs = 5f;
        internal static float SealBudgetMs = 3f;

        static string GameRoot
        {
            get { try { return MelonLoader.Utils.MelonEnvironment.GameRootDirectory; } catch { return AppContext.BaseDirectory; } }
        }
        static string LogPath => Path.Combine(GameRoot, "Mods", "NewSlots.log");

        int _throttle;
        int _verifyThrottle;
        int _upgradeThrottle;
        int _upgradeTries;

        public override void OnInitializeMelon()
        {
            Harm = HarmonyInstance;
            try { Directory.CreateDirectory(Path.GetDirectoryName(LogPath)); } catch { }
            try { Directory.CreateDirectory(Art.ContentDir); } catch { }

            try
            {
                File.AppendAllText(LogPath,
                    "\n\n==================== NewSlots v1.0  " + DateTime.Now + " ====================\n" +
                    "adds new catalog entries; overwrites no stock bundle.\n");
            }
            catch { }

            try
            {
                _cfg = MelonPreferences.CreateCategory("NewSlots");
                var ps = _cfg.CreateEntry("PatchScale", 1.30f, description: "size multiplier for generated silhouette patches");
                var pf = _cfg.CreateEntry("PatchDepthFlip", false, description: "flip which donor face generated patches sit on");
                PatchScale = Mathf.Clamp(ps.Value, 0.5f, 2.5f);
                PatchDepthFlip = pf.Value;
                var bb = _cfg.CreateEntry("BuildBudgetMs", 5.0f, description: "per-frame ms spent building the pack at menu");
                var sb = _cfg.CreateEntry("SealBudgetMs", 3.0f, description: "per-frame ms spent compressing textures");
                BuildBudgetMs = Mathf.Clamp(bb.Value, 1f, 33f);

                PrewarmDuringLoad = _cfg.CreateEntry("PrewarmDuringLoad", true,
                    description: "decode textures and meshes while waiting for LoadoutManager, "
                               + "so the catalog build finds them cached").Value;
                SealBudgetMs = Mathf.Clamp(sb.Value, 1f, 33f);

                _nPanelX = _cfg.CreateEntry("NativePanelX", -99999f, description: "patch adjuster x, canvas units from the left (-99999 = place it automatically)");
                _nPanelY = _cfg.CreateEntry("NativePanelY", -99999f, description: "patch adjuster y, canvas units from the middle (-99999 = place it automatically)");
                _nScale  = _cfg.CreateEntry("NativeUiScale", 0f, description: "patch adjuster size; 0 = auto-fit to the screen");

                if (System.Math.Abs(_nPanelX.Value - 40f) < 0.01f && System.Math.Abs(_nPanelY.Value) < 0.01f)
                {
                    _nPanelX.Value = -99999f; _nPanelY.Value = -99999f;
                    Log("[ui] adjuster position was the old (40, 0) default, which sits underneath "
                      + "the Loadout Presets panel. Reset so it places itself upper-right.");
                }
                _nativeUi = _cfg.CreateEntry("NativePanel", true, description: "draw the adjuster in the game's UI style; false = the old IMGUI box");

                _nMin = _cfg.CreateEntry("NativePanelMinimised", false, description: "patch adjuster starts rolled up");

                _panelX = _cfg.CreateEntry("PanelX", -1f, description: "patch adjuster x (-1 = default position)");
                _panelY = _cfg.CreateEntry("PanelY", -1f, description: "patch adjuster y");
                _panelMin = _cfg.CreateEntry("PanelMinimised", false, description: "patch adjuster starts rolled up");
                var share = _cfg.CreateEntry("ShareSeating", true, description: "send your patch seating to the people you play with, and use theirs");
                TuningNet.Enabled = share.Value;
                if (_panelX.Value >= 0f && _panelY.Value >= 0f)
                    Ui.LoadPlacement(_panelX.Value, _panelY.Value, _panelMin.Value);
                else
                    Ui.LoadPlacement(float.NaN, float.NaN, _panelMin.Value);
            }
            catch { }

            Log("content dir: " + Art.ContentDir + "   PatchScale=" + PatchScale + " DepthFlip=" + PatchDepthFlip);
            Defs.Load(Art.ContentDir);
            Hooks.Install();
            Log("waiting for LoadoutManager.");
        }

        internal static bool PrewarmDuringLoad = true;
        static int _warmCursor;
        static bool _warmDone;

        static void Prewarm(float budgetMs)
        {
            if (_warmDone) return;
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (_warmCursor < Defs.All.Count)
                {
                    var d = Defs.All[_warmCursor];
                    _warmCursor++;

                    if (!string.IsNullOrEmpty(d.Art))        Art.Optional(d.Art,        d.ModName,        false);
                    if (!string.IsNullOrEmpty(d.ArtNormal))  Art.Optional(d.ArtNormal,  d.ModName + "_n", true);
                    if (!string.IsNullOrEmpty(d.ArtMask))    Art.Optional(d.ArtMask,    d.ModName + "_m", true);
                    if (!string.IsNullOrEmpty(d.ArtOpacity)) Art.Optional(d.ArtOpacity, d.ModName + "_o", true);
                    if (!string.IsNullOrEmpty(d.MeshFileName)) MeshFile.Load(d.MeshFileName, d.ModName);

                    if (sw.Elapsed.TotalMilliseconds >= budgetMs) return;
                }
                _warmDone = true;
                Log("prewarm: caches warmed for " + Defs.All.Count + " item(s) before LoadoutManager appeared");
            }
            catch (Exception e)
            {
                _warmDone = true;
                Log("prewarm: stopped after " + _warmCursor + " item(s): " + e.GetType().Name + ": " + e.Message);
            }
        }

        public override void OnUpdate()
        {

            if (!Catalog.Registered)
            {

                if (PrewarmDuringLoad && !Catalog.Started) Prewarm(BuildBudgetMs);

                Catalog.Tick(BuildBudgetMs);
                return;
            }

            Art.PumpSeal(SealBudgetMs);

            if (++_upgradeThrottle >= 30)
            {
                _upgradeThrottle = 0;
                if (_upgradeTries <= 40)
                {
                    _upgradeTries++;
                    try { ItemFactory.UpgradeMaterials(Il2Cpp.LoadoutManager.singleton, _upgradeTries >= 40); } catch { }
                }
            }

            if (++_verifyThrottle >= 600)
            {
                _verifyThrottle = 0;
                Catalog.Verify();
            }

            if (WantNativeUi && !NativeUi.GaveUp) { try { NativeUi.Tick(); } catch { } }
            else if (NativeUi.Active) { try { NativeUi.Teardown(); } catch { } }

            try { CornerGrab.Tick(); } catch { }
        }

        int _flushThrottle;

        public override void OnLateUpdate()
        {
            try { Seat.Tick(); } catch { }

            if (++_flushThrottle >= 600) { _flushThrottle = 0; try { FlushLog(); } catch { } }
        }

        static MelonPreferences_Entry<float> _panelX, _panelY;
        static MelonPreferences_Entry<bool> _panelMin;
        static MelonPreferences_Entry<float> _nPanelX, _nPanelY, _nScale;
        static MelonPreferences_Entry<bool> _nativeUi, _nMin;

        internal static bool NativeMinimised
        {
            get { try { return _nMin != null && _nMin.Value; } catch { return false; } }
            set { try { if (_nMin != null) _nMin.Value = value; } catch { } }
        }

        internal static bool WantNativeUi { get { try { return _nativeUi == null || _nativeUi.Value; } catch { return true; } } }
        internal static float PanelPosX   { get { try { return _nPanelX.Value; } catch { return 40f; } } }
        internal static float PanelPosY   { get { try { return _nPanelY.Value; } catch { return 0f;  } } }
        internal static float PanelUiScale{ get { try { return _nScale.Value;  } catch { return 0f;  } } }

        internal static void SaveNativePlacement(float x, float y, float scale)
        {
            try
            {
                if (_nPanelX == null) return;
                _nPanelX.Value = x; _nPanelY.Value = y; _nScale.Value = scale;
                MelonPreferences.Save();
            }
            catch { }
        }

        internal static void SavePanelPlacement(float x, float y, bool minimised)
        {
            try
            {
                if (_panelX == null) return;
                _panelX.Value = x; _panelY.Value = y; _panelMin.Value = minimised;
                MelonPreferences.Save();
            }
            catch { }
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {

            try { NativeUi.Teardown(); } catch { }
            try { Ui.Close(); } catch { }
            try { CornerGrab.Cancel(); } catch { }

            try
            {
                bool connected = false;
                try { connected = Il2CppMirror.NetworkClient.active || Il2CppMirror.NetworkServer.active; } catch { }
                if (!connected) TuningNet.Solo();
            }
            catch { }

            _throttle = 0;
            if (Catalog.Registered) Catalog.Verify();
        }

        public override void OnGUI()
        {

            try { CornerGrab.Draw(); } catch { }

            if (WantNativeUi && NativeUi.Suppress) return;
            try { Ui.Draw(); } catch { }
        }

        static readonly object _lock = new object();
        static readonly System.Text.StringBuilder _buf = new System.Text.StringBuilder();

        internal static void Log(string msg)
        {
            try { MelonLogger.Msg(msg); } catch { }
            Detail(msg);
        }

        internal static void Detail(string msg)
        {
            try
            {
                lock (_lock)
                {
                    _buf.Append(msg).Append('\n');
                    if (_buf.Length > 16384) FlushLocked();
                }
            }
            catch { }
        }

        internal static void FlushLog()
        {
            try { lock (_lock) { FlushLocked(); } } catch { }
        }

        static void FlushLocked()
        {
            if (_buf.Length == 0) return;
            try { File.AppendAllText(LogPath, _buf.ToString()); } catch { }
            _buf.Length = 0;
        }
    }
}
