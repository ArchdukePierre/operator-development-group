// ODG Operations - HostPin
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using MelonLoader;

[assembly: MelonInfo(typeof(HostPin.Main), "ODG Host Pin", "1.0.0", "ArchD")]
[assembly: MelonGame(null, null)]

namespace HostPin
{

    public class Main : MelonMod
    {
        const string WantScene = "Suburb Night";
        const string BoardMark = "ODG_OP_";
        const string MapMark = "Farmstead";

        public override void OnInitializeMelon()
        {
            try
            {
                var m = HarmonyLib.AccessTools.Method(
                    typeof(Il2Cpp.CerebusOpboard), "Start_Operation");
                if (m == null)
                {
                    LoggerInstance.Error("no CerebusOpboard.Start_Operation on this "
                        + "build; the pin is inert and Farmstead will load whatever "
                        + "host its board inherited.");
                    return;
                }
                HarmonyInstance.Patch(m, prefix: new HarmonyLib.HarmonyMethod(
                    typeof(Main), nameof(PinHost)));
                LoggerInstance.Msg("hooked Start_Operation: Farmstead's board is pinned "
                    + "to '" + WantScene + "' at launch.");
            }
            catch (Exception e)
            {
                LoggerInstance.Error("hook failed: " + e.Message);
            }
        }

        static void PinHost(Il2Cpp.CerebusOpboard __instance)
        {
            try
            {
                if (__instance == null) return;
                string name = null;
                try { name = __instance.name; } catch { }
                if (string.IsNullOrEmpty(name)) return;

                if (name.IndexOf(BoardMark, StringComparison.Ordinal) < 0) return;
                if (name.IndexOf(MapMark, StringComparison.OrdinalIgnoreCase) < 0) return;

                var tp = __instance.TARGETPACKAGE;
                if (tp == null) return;
                for (int i = 0; i < tp.Length; i++)
                {
                    var slot = tp[i];
                    if (slot == null) continue;
                    string was = null;
                    try { was = slot.OPERATION_SCENE; } catch { }
                    if (string.Equals(was, WantScene, StringComparison.OrdinalIgnoreCase))
                        continue;
                    slot.OPERATION_SCENE = WantScene;
                    Melon<Main>.Logger.Msg("slot " + i + ": '" + (was ?? "?") + "' -> '"
                        + WantScene + "' (the game update moved this board's host; "
                        + "Farmstead stays in the level it was tuned in)");
                }
            }
            catch (Exception e)
            {
                Melon<Main>.Logger.Error("pin failed, launch continues unpinned: " + e.Message);
            }
        }
    }
}
