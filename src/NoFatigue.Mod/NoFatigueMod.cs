// ODG Operations - NoFatigue
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using MelonLoader;

[assembly: MelonInfo(typeof(NoFatigue.Main), "ODG No Fatigue", "1.0.0", "ArchD")]
[assembly: MelonGame(null, null)]

namespace NoFatigue
{

    public class Main : MelonMod
    {
        internal static MelonLogger.Instance L;

        internal static MelonPreferences_Category Cat;
        internal static MelonPreferences_Entry<bool> ArmFatigue;
        internal static MelonPreferences_Entry<bool> BreathingWobble;
        internal static MelonPreferences_Entry<bool> WeaponSway;
        internal static MelonPreferences_Entry<bool> WeightPenalty;
        internal static MelonPreferences_Entry<bool> SprintStamina;
        internal static MelonPreferences_Entry<bool> BreathingSfx;
        internal static MelonPreferences_Entry<bool> Verbose;

        public override void OnInitializeMelon()
        {
            L = LoggerInstance;

            Cat = MelonPreferences.CreateCategory("ODG_NoFatigue");
            ArmFatigue = Cat.CreateEntry("RemoveArmFatigue", true,
                description: "arms never get tired: no drain, no tremble as the gun is held up");
            BreathingWobble = Cat.CreateEntry("RemoveBreathingWobble", true,
                description: "also flattens the breathing wobble to zero. If the weapon ever "
                           + "looks frozen at a slight offset, this is the one to turn off -- "
                           + "RemoveArmFatigue alone still stops the gun getting worse over time");
            WeaponSway = Cat.CreateEntry("RemoveWeaponSway", true,
                description: "the gun stops lagging behind the camera when you turn. This is "
                           + "feel, not fatigue -- turn it off to keep the stock aim sway");
            WeightPenalty = Cat.CreateEntry("RemoveWeightPenalty", true,
                description: "carried weight no longer slows you down, blocks jog/sprint, or "
                           + "scales fall damage");
            SprintStamina = Cat.CreateEntry("RemoveSprintStamina", true,
                description: "infinite sprint: no depletion, no lockout, no out-of-breath slowdown");
            BreathingSfx = Cat.CreateEntry("RemoveBreathingSFX", true,
                description: "silences the panting and the out-of-breath gasp");
            Verbose = Cat.CreateEntry("Verbose", false,
                description: "logs a live state line every two seconds so the values can be read "
                           + "back in game instead of taken on trust");
            Cat.SaveToFile(false);

            Patches.Install(HarmonyInstance);
            L.Msg(Patches.Summary());
            L.Msg("arms " + On(ArmFatigue) + ", breath wobble " + On(BreathingWobble)
                + ", sway " + On(WeaponSway) + ", weight " + On(WeightPenalty)
                + ", sprint stamina " + On(SprintStamina) + ", panting " + On(BreathingSfx));
        }

        static string On(MelonPreferences_Entry<bool> e) { return e.Value ? "OFF" : "stock"; }

        public override void OnSceneWasLoaded(int index, string name)
        {
            Patches.ForgetCaches();
        }

        float _next;
        public override void OnUpdate()
        {
            if (Verbose == null || !Verbose.Value) return;
            if (UnityEngine.Time.unscaledTime < _next) return;
            _next = UnityEngine.Time.unscaledTime + 2f;
            Patches.LogState();
        }
    }
}
