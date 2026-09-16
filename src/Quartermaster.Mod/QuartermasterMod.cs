// ODG Operations - Quartermaster
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

[assembly: MelonInfo(typeof(Quartermaster.Main), "ODG Quartermaster", "1.0.0", "ArchdukePierre")]
[assembly: MelonGame(null, null)]

namespace Quartermaster
{

    public class Main : MelonMod
    {
        internal static MelonLogger.Instance L;
        internal static MelonPreferences_Category Cat;
        internal static MelonPreferences_Entry<bool> Enabled, StatCard, DeltaChips, RoundCards, HandlingOn, RoundsOn,
                                                     ApplyToBots, MobilityOn, ShowTableHash, Verbose, AutoPlace;
        internal static MelonPreferences_Entry<float> RecoilScale, MobilityFloor, MobilityFullAt, MobilityFreeUpTo,
                                                      CardX, CardY, UiScale, BandW;
        internal static MelonPreferences_Entry<string> DumpKey, BandScreen;

        internal static string DataDir
        {
            get { return Path.Combine(MelonEnvironment.GameRootDirectory, "Mods", "Quartermaster_Data"); }
        }
        internal static string UserDir
        {
            get { return Path.Combine(MelonEnvironment.UserDataDirectory, "Quartermaster"); }
        }

        public override void OnInitializeMelon()
        {
            L = LoggerInstance;
            Cat = MelonPreferences.CreateCategory("ODG_Quartermaster");
            Enabled      = Cat.CreateEntry("Enabled", true, description: "master switch. Off leaves only the read-only stat card.");
            StatCard     = Cat.CreateEntry("StatCard", true, description: "the stat card on the weapon bench");
            DeltaChips   = Cat.CreateEntry("DeltaChips", true, description: "green/red delta boxes while a dropdown option is under the cursor");
            RoundCards   = Cat.CreateEntry("RoundCards", true, description: "hover card and pinned card for every round");
            HandlingOn   = Cat.CreateEntry("Handling", true, description: "attachments change accuracy, ergonomics (ADS speed) and recoil");
            RecoilScale  = Cat.CreateEntry("RecoilScale", 1.0f, description: "scales every recoil delta. 0.5 halves them, 0 turns them off");
            RoundsOn     = Cat.CreateEntry("Rounds", true, description: "the round table: the v1 balance edits and the barrel velocity factor");
            ApplyToBots  = Cat.CreateEntry("ApplyToBots", true, description: "bots fire the same round table you do (fair). Off leaves bot rounds stock");
            MobilityOn   = Cat.CreateEntry("Mobility", true, description: "kit mass slows walk/jog/sprint together, inside a fixed band. Nothing else changes");
            MobilityFloor= Cat.CreateEntry("MobilityFloor", 0.88f, description: "speed factor for the heaviest kit. 0.88 = twelve percent slower at most");
            MobilityFullAt = Cat.CreateEntry("MobilityFullKg", 30f, description: "kit mass in kg at which the floor is reached: a belt gun with 300 rounds, plates and a sidearm");
            MobilityFreeUpTo = Cat.CreateEntry("MobilityFreeKg", 15f, description: "kit mass in kg with no penalty at all: a rifleman with plates, a sidearm and a dozen magazines");
            ShowTableHash= Cat.CreateEntry("ShowTableHash", true, description: "show the data-table md5 in the card footer so two players can compare");
            AutoPlace    = Cat.CreateEntry("AutoPlace", true, description: "centre the band at the bottom between the parts list and the preset slots, measured once per screen size and then kept. Dragging it turns this off");
            CardX        = Cat.CreateEntry("CardX", OdgUi.Chrome.UNSET, description: "band position, written when it is measured or dragged");
            CardY        = Cat.CreateEntry("CardY", OdgUi.Chrome.UNSET, description: "band position, written when it is measured or dragged");
            UiScale      = Cat.CreateEntry("UiScale", 0f, description: "band scale; 0 = auto-fit to the screen");
            BandW        = Cat.CreateEntry("BandW", 0f, description: "band width in design units, written with the position");
            BandScreen   = Cat.CreateEntry("BandScreen", "", description: "the screen the saved placement belongs to; a different screen measures again");
            Verbose      = Cat.CreateEntry("Verbose", false, description: "a state line every two seconds");
            DumpKey      = Cat.CreateEntry("DumpKey", "F8", description: "hotkey for the diagnostic dumps (UserData\\Quartermaster\\)");
            Cat.SaveToFile(false);

            try { Directory.CreateDirectory(UserDir); } catch { }
            Tables.Load(DataDir);
            L.Msg(Tables.Summary());

            Patches.Install(HarmonyInstance);
            L.Msg(Patches.Summary());
            L.Msg("enabled " + Enabled.Value + ", handling " + HandlingOn.Value + " (recoil x" + RecoilScale.Value.ToString("0.00")
                + "), rounds " + RoundsOn.Value + " (bots " + ApplyToBots.Value + "), mobility " + MobilityOn.Value
                + " (floor " + MobilityFloor.Value.ToString("0.00") + " at " + MobilityFullAt.Value.ToString("0") + " kg)");
        }

        public override void OnSceneWasLoaded(int index, string name)
        {
            try { Bench.OnScene(); } catch { }
            try { Mobility.Reset(); } catch { }
            try { Handling.Reset(); } catch { }
            try { Recoil.Reset(); } catch { }
            try { Rounds.Reset(); } catch { }
        }

        float _next;
        public override void OnUpdate()
        {
            try { Bench.Tick(); } catch (Exception e) { Once("bench tick: " + e.Message); }
            try { Mobility.Tick(); } catch (Exception e) { Once("mobility tick: " + e.Message); }
            try { Probe.Tick(); } catch (Exception e) { Once("probe tick: " + e.Message); }

            if (Verbose == null || !Verbose.Value) return;
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 2f;
            try { L.Msg(Mobility.StateLine() + "   " + Handling.StateLine()); } catch { }
        }

        static readonly System.Collections.Generic.HashSet<string> _once = new System.Collections.Generic.HashSet<string>();
        internal static void Once(string what)
        {
            if (_once.Contains(what)) return;
            _once.Add(what);
            L.Warning(what);
        }

        internal static void Log(string s) { L.Msg(s); }

        internal static void PersistCard(float x, float y, float scale, float w, string screen)
        {
            try
            {
                CardX.Value = x; CardY.Value = y; UiScale.Value = scale; BandW.Value = w; BandScreen.Value = screen ?? "";
                AutoPlace.Value = false;
                Cat.SaveToFile(false);
            }
            catch { }
        }

        internal static void PersistAuto(float x, float y, float scale, float w, string screen)
        {
            try
            {
                CardX.Value = x; CardY.Value = y; UiScale.Value = scale; BandW.Value = w; BandScreen.Value = screen ?? "";
                Cat.SaveToFile(false);
            }
            catch { }
        }
    }
}
