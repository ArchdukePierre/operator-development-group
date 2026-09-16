// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using MelonLoader;

namespace MapLoader
{

    internal static class TuneStore
    {
        static MelonPreferences_Category _cat;

        static MelonPreferences_Category Cat()
        {
            if (_cat != null) return _cat;
            _cat = MelonPreferences.GetCategory("MapLoaderTuning")
                ?? MelonPreferences.CreateCategory("MapLoaderTuning");
            return _cat;
        }

        static string Key(string dial)
        {
            string map = "Farmstead";
            try { if (!string.IsNullOrEmpty(MapRuntime.SceneName)) map = MapRuntime.SceneName; }
            catch { }
            return map + "_" + dial;
        }

        internal static void Save(string dial, float value)
        {
            try
            {
                var c = Cat();
                string k = Key(dial);
                var e = c.GetEntry<float>(k) ?? c.CreateEntry(k, value,
                    description: "Tuned in game. Delete this line to go back to the built-in default.");
                e.Value = value;
                MelonPreferences.Save();
            }
            catch (Exception ex)
            {
                try { Melon<Main>.Logger.Warning("[tune] could not save " + dial + ": " + ex.Message); }
                catch { }
            }
        }

        internal static float Load(string dial, float fallback)
        {
            try
            {
                var e = Cat().GetEntry<float>(Key(dial));
                return e != null ? e.Value : fallback;
            }
            catch { return fallback; }
        }

        internal static void ApplyAll(MelonLogger.Instance log)
        {
            try
            {
                float ground = Load("GroundAlbedo", MeshFactory.GroundAlbedoScale);
                float moon   = Load("MoonLux",      MapRuntime.MoonLux);
                float ev     = Load("NightEV",      MapRuntime.NightEV);
                float fog    = Load("FogMeanFreePath", MapRuntime.FogMeanFreePath);

                bool any = ground != MeshFactory.GroundAlbedoScale
                        || moon   != MapRuntime.MoonLux
                        || ev     != MapRuntime.NightEV
                        || fog    != MapRuntime.FogMeanFreePath;

                MeshFactory.GroundAlbedoScale = ground;
                MapRuntime.MoonLux            = moon;
                MapRuntime.NightEV            = ev;
                MapRuntime.FogMeanFreePath    = fog;

                float mip = Load("FoliageMipBias", -1f);
                MeshFactory.FoliageMipBiasOverride = mip;
                float cut = Load("FoliageCutoff", -1f);
                MeshFactory.FoliageCutoffOverride = cut;
                if (cut >= 0f)
                    log?.Msg("[tune] foliage alpha cutoff restored at " + cut.ToString("F2")
                           + " (dialled on this map; delete the line to go back to 0.33).");
                if (mip >= 0f)
                    log?.Msg("[tune] foliage mip bias restored at +" + mip.ToString("F2")
                           + " (dialled on this map; delete the line to go back to the built-in).");

                if (any)
                    log?.Msg("[tune] restored this map's saved dials: ground albedo "
                           + ground.ToString("F2") + ", moon " + moon.ToString("F2")
                           + " lux, night EV " + ev.ToString("F1") + ", fog "
                           + fog.ToString("F0") + " m. Tuned in game, kept here.");
                else
                    log?.Msg("[tune] no saved dials for this map; using the built-in values.");
            }
            catch (Exception ex) { log?.Warning("[tune] restore: " + ex.Message); }
        }
    }
}
