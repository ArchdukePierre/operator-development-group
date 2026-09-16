// ODG Operations - FireteamRoles
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(FireteamRoles.Mod), "ODG Fireteam Roles", "1.0.0", "ArchdukePierre")]
[assembly: MelonGame(null, null)]

namespace FireteamRoles
{

    public class Mod : MelonMod
    {
        internal static bool KitDirty = true;
        internal static Il2Cpp.MedicalSystem KitCandidate;
        static Role _lastRoleApplied = (Role)(-1);
        static float _kitRetryAt;
        static int _kitAttempts;
        static float _nextWatchdog;
        static float _nextHealth;
        static int _lastFailingSites = -1;

        public override void OnInitializeMelon()
        {
            try { Directory.CreateDirectory(Path.Combine(Log.GameRoot, "Mods")); } catch { }
            try { Directory.CreateDirectory(Epi.ContentDir); } catch { }

            Log.Msg("==================== ODG Fireteam Roles 1.0.0 ====================");
            Cfg.Load();
            State.MyRole = RoleInfo.Clamp(Cfg.LastRole.Value);
            Log.Msg("role restored: " + RoleInfo.Name(State.MyRole));

            Hooks.Install(HarmonyInstance);
            Log.Msg(Hooks.Summary());
            if (!Hooks.AllBound)
                Log.Warn("some features will not work this session - see the MISSING/FAILED list above");
            Log.Msg("content dir: " + Epi.ContentDir);
            Log.Flush();
        }

        public override void OnUpdate()
        {
            State.SampleLoad();
            Err.Guard("net.poll", State.PollNetwork);
            Err.Guard("state.rescan", () => State.MaybeRescan(false));

            Err.Guard("downed.flag", Downed.RefreshAmDownedPublic);

            bool enabled = Err.Try("state.enabled", () => State.Enabled, false);
            if (!enabled) return;

            Err.Guard("input", Aim.Update);
            Err.Guard("cursor", Ui.CursorTick);
            Err.Guard("downed.local", Downed.LocalTick);
            Err.Guard("downed.host", Downed.HostTick);
            Err.Guard("drag.dragger", Drag.Tick);
            Err.Guard("drag.victim", Drag.VictimTick);
            Err.Guard("treat", Treat.Tick);
            Err.Guard("bar", Bar.Tick);
            Err.Guard("menu", Menu.Tick);
            Err.Guard("perks", Perks.Tick);
            Err.Guard("perks.ai", Perks.SniperAiTick);
            Err.Guard("pouch", Pouch.Tick);
            Err.Guard("breach.poll", Breach.Poll);
            Err.Guard("ladder", Ladder.Tick);
            Err.Guard("ladder.poll", Ladder.Poll);
            Err.Guard("jtac", Jtac.Tick);
            Err.Guard("radar", Radar.Tick);
            Err.Guard("drone", Drone.Tick);
            Err.Guard("drone.poll", Drone.Poll);
            Err.Guard("epi", Epi.Tick);
            Err.Guard("medical.counts", () => Medical.RefreshCounts(false));
            Err.Guard("icons.scan", () => MedIcons.Scan(Medical.Local()));
            Err.Guard("ui.prewarm", Ui.Prewarm);
            Err.Guard("pose", Pose.Tick);
            Err.Guard("kit", ApplyKit);

            if (Time.realtimeSinceStartup >= _nextWatchdog)
            {
                _nextWatchdog = Time.realtimeSinceStartup + 5f;
                Err.Guard("medical.watchdog", Medical.Watchdog);
            }

            if (Time.realtimeSinceStartup >= _nextHealth)
            {
                _nextHealth = Time.realtimeSinceStartup + 30f;
                int sites = Err.FailingSites;
                if (sites != _lastFailingSites)
                {
                    _lastFailingSites = sites;
                    if (sites > 0) Log.Warn("health: " + sites + " subsystem(s) reporting errors:" + Err.Report());
                    else Log.Msg("health: all subsystems clean");
                }
            }
        }

        void ApplyKit()
        {
            if (!KitDirty && _lastRoleApplied == State.MyRole) return;
            if (Time.realtimeSinceStartup < _kitRetryAt) return;
            _kitRetryAt = Time.realtimeSinceStartup + 0.5f;
            _kitAttempts++;

            var med = KitCandidate;
            bool candidateUsable = false;
            try { candidateUsable = med != null && med.gameObject != null && med.isOwned; }
            catch { med = null; }
            if (!candidateUsable) med = Medical.Local();

            if (med == null)
            {
                if (_kitAttempts % 20 == 0) Log.Msg("medic kit: still no MedicalSystem after " + _kitAttempts + " attempts");
                return;
            }

            if (!Medical.ApplyMedicKit(med)) return;
            _lastRoleApplied = State.MyRole;
            KitDirty = false;
            _kitAttempts = 0;
        }

        public override void OnFixedUpdate()
        {
            Err.Guard("pose.fixed", Pose.FixedTick);
            Err.Guard("drone.fixed", Drone.FixedTick);
        }

        public override void OnLateUpdate()
        {
            Err.Guard("cursor.late", Ui.HoldCursor);
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            Log.Msg("scene: " + sceneName);
            Err.Crumb("scene:" + sceneName);

            Err.ResetAll("scene change");
            State.OnSceneChanged();
            State.MaybeRescan(true);

            Treat.Close();
            Aim.SetPicker(false);
            Aim.CloseGive(false);
            Drag.Stop("scene change");
            Downed.HostClearAll("scene change");

            Breach.Reset("scene change", false);
            Drone.Reset("scene change");
            Jtac.Reset("scene change");
            Radar.Reset("scene change");
            Drone.Warm();
            Net.ClearVolatileKeys();
            Pose.Forget();
            MedIcons.Forget();
            Epi.Forget();
            Medical.Forget();
            KitDirty = true;
            KitCandidate = null;
            _lastRoleApplied = (Role)(-1);
            Log.Flush();
        }

        public override void OnGUI()
        {

            Err.Guard("ui.draw", Ui.Draw);
        }

        public override void OnApplicationQuit()
        {
            Log.Flush();
        }
    }
}
