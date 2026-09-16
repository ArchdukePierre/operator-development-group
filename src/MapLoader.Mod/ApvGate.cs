// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using MelonLoader;
using UnityEngine;
using HD = UnityEngine.Rendering.HighDefinition;

namespace MapLoader
{

    public static class ApvGate
    {
        static MelonLogger.Instance _log;
        public static void Init(MelonLogger.Instance log) { _log = log; }

        const uint ApvBit = 127u;
        static readonly HD.FrameSettingsField ApvField = (HD.FrameSettingsField)127;

        static readonly uint[] QualityBits = { 21u, 23u, 24u, 95u };
        static readonly string[] QualityNames = { "ContactShadows", "SSR", "SSAO", "SSGI" };

        public static bool Demand;

        static HD.HDAdditionalCameraData _cam;
        static bool _applied;
        static float _nextTick;

        static bool _loggedQuality;
        static int _settledTicks;
        static int _reasserts;
        static bool _loggedLosers;

        static readonly bool[] _postWrite = new bool[QualityBits.Length];
        static readonly string[] _writeErr = new string[QualityBits.Length];
        static bool _postWriteValid;
        static int _applyCount;

        static bool _prefRead;
        static MelonPreferences_Entry<bool> _pref;

        static MelonPreferences_Entry<bool> Pref()
        {
            if (!_prefRead)
            {
                _prefRead = true;
                try
                {
                    var cfg = MelonPreferences.GetCategory("MapLoader")
                           ?? MelonPreferences.CreateCategory("MapLoader");

                    _pref = cfg.GetEntry<bool>("CityApvOffV2")
                         ?? cfg.CreateEntry("CityApvOffV2", true,
                              description: "Force the AdaptiveProbeVolume frame setting OFF on the "
                                         + "rendering camera while a city map is up. The shipped "
                                         + "probe system is APV and the city sits outside every "
                                         + "baked cell, so with this ON the deferred indirect "
                                         + "falls back to the ambient probe our sky feeds. "
                                         + "Default false until the screenshot harness verdict; "
                                         + "'odg apv on|off' moves it live.");
                }
                catch { _pref = null; }
            }
            return _pref;
        }

        public static bool PrefOn
        {
            get { var p = Pref(); return p != null && p.Value; }
        }

        static MelonPreferences_Entry<bool> _addPref;

        static MelonPreferences_Entry<bool> AddPref()
        {
            try
            {
                if (_addPref != null) return _addPref;
                var cfg = MelonPreferences.GetCategory("MapLoader")
                       ?? MelonPreferences.CreateCategory("MapLoader");
                _addPref = cfg.GetEntry<bool>("AdditiveApvOff")
                        ?? cfg.CreateEntry("AdditiveApvOff", true,
                             description: "On a host-level-as-map (Nuketown, Killhouse, "
                                        + "Trenches, VirginiaBeach), force the Adaptive Probe "
                                        + "Volume frame bit OFF so HDRP falls back to the "
                                        + "ambient probe. On by default: with it on the APV "
                                        + "lookup returned nothing for these worlds and every "
                                        + "surface not in direct sun rendered pure black. "
                                        + "'odg apv off' turns it off live.");
                return _addPref;
            }
            catch { return null; }
        }

        public static bool AddPrefOn
        {
            get { var p = AddPref(); return p != null && p.Value; }
        }

        public static bool AdditiveWanted
        {
            get { return MapRuntime.Done && MapIdentity.HostLevelIsMap && AddPrefOn; }
        }

        public static bool Wanted
        {

            get
            {
                if (MapRuntime.Done && MapIdentity.OwnSky && (PrefOn || Demand)) return true;

                return AdditiveWanted;
            }
        }

        public static bool Apply(HD.HDAdditionalCameraData hdCam)
        {
            if (hdCam == null) return false;
            try
            {

                bool quality = MapIdentity.OwnSky;
                var fs = hdCam.renderingPathCustomFrameSettings;
                var mask = hdCam.renderingPathCustomFrameSettingsOverrideMask;
                fs.bitDatas[ApvBit] = false;
                mask.mask[ApvBit] = true;

                for (int q = 0; quality && q < QualityBits.Length; q++)
                {
                    try
                    {
                        fs.bitDatas[QualityBits[q]] = true;
                        mask.mask[QualityBits[q]] = true;
                        _writeErr[q] = null;
                    }
                    catch (Exception qe) { _writeErr[q] = qe.Message; }
                }
                hdCam.customRenderingSettings = true;
                hdCam.renderingPathCustomFrameSettings = fs;
                hdCam.renderingPathCustomFrameSettingsOverrideMask = mask;

                bool bit = true;
                try { bit = hdCam.renderingPathCustomFrameSettings.IsEnabled(ApvField); }
                catch { }

                for (int q = 0; q < QualityBits.Length; q++)
                {
                    bool on = false;
                    try { on = hdCam.renderingPathCustomFrameSettings
                                 .IsEnabled((HD.FrameSettingsField)QualityBits[q]); }
                    catch { }
                    _postWrite[q] = on;
                }
                _postWriteValid = true;

                _applyCount++;
                if (_applyCount <= 3 || (_applyCount % 10) == 0)
                {
                    string name = "?";
                    try { name = hdCam.gameObject.name; } catch { }
                    _log?.Msg("[apv] cam=" + name + " apply#" + _applyCount + " bit127=" + bit
                            + (bit ? "  (the OFF write did NOT take)" : "  (APV is off; indirect "
                            + "now falls back to the ambient probe)"));
                }
                _cam = hdCam;
                _applied = !bit;
                return !bit;
            }
            catch (Exception e)
            {
                _log?.Warning("[apv] apply: " + e.Message);
                return false;
            }
        }

        static void Restore(HD.HDAdditionalCameraData hdCam)
        {
            if (hdCam == null) return;
            try
            {
                var mask = hdCam.renderingPathCustomFrameSettingsOverrideMask;
                mask.mask[ApvBit] = false;
                hdCam.renderingPathCustomFrameSettingsOverrideMask = mask;
                bool bit = true;
                try { bit = hdCam.renderingPathCustomFrameSettings.IsEnabled(ApvField); }
                catch { }
                _log?.Msg("[apv] override withdrawn, bit127=" + bit
                        + " (the pipeline default owns it again)");
            }
            catch (Exception e) { _log?.Warning("[apv] restore: " + e.Message); }
            _applied = false;
        }

        public static void Tick()
        {
            if (Time.realtimeSinceStartup < _nextTick) return;
            _nextTick = Time.realtimeSinceStartup + 1f;
            try
            {
                if (!Wanted)
                {
                    if (_applied && _cam != null) Restore(_cam);
                    if (!_applied) _cam = null;
                    return;
                }

                var cam = Camera.main;
                if (cam == null) return;
                var hdCam = cam.GetComponent<HD.HDAdditionalCameraData>();
                if (hdCam == null) return;

                if (_cam != null && _cam != hdCam)
                { _cam = null; _applied = false; _settledTicks = 0; _postWriteValid = false; }

                var comp = hdCam.renderingPathCustomFrameSettings;
                var compMask = hdCam.renderingPathCustomFrameSettingsOverrideMask;
                bool bit = true;
                try { bit = comp.IsEnabled(ApvField); } catch { }
                bool maskOn = false;
                try { maskOn = compMask.mask[ApvBit]; } catch { }

                bool qualityDrift = false;
                var qNow = new bool[QualityBits.Length];
                for (int q = 0; q < QualityBits.Length; q++)
                {
                    bool on = false, qm = false;
                    try { on = comp.IsEnabled((HD.FrameSettingsField)QualityBits[q]); } catch { }
                    try { qm = compMask.mask[QualityBits[q]]; } catch { }
                    qNow[q] = on;
                    if (!on || !qm) qualityDrift = true;
                }

                if (bit || !maskOn || qualityDrift)
                {
                    _settledTicks = 0;
                    _reasserts++;

                    if (qualityDrift && _reasserts >= 10 && !_loggedLosers && _postWriteValid)
                    {
                        _loggedLosers = true;
                        var sb = new System.Text.StringBuilder("[apv] after ")
                            .Append(_reasserts)
                            .Append(" re-asserts these bits still read wrong on the live camera:");
                        for (int q = 0; q < QualityBits.Length; q++)
                        {
                            if (qNow[q]) continue;
                            sb.Append("  ").Append(QualityNames[q])
                              .Append("[writeThrew=").Append(_writeErr[q] ?? "no")
                              .Append(" afterApply=").Append(_postWrite[q])
                              .Append(" oneTickLater=").Append(qNow[q]).Append(']');
                        }
                        sb.Append("  afterApply=False or a throw means the WRITE loses; "
                                + "afterApply=True means the SANITIZER/SEAM loses it after "
                                + "landing. The gate keeps re-asserting either way.");
                        _log?.Warning(sb.ToString());
                    }
                    Apply(hdCam);
                    return;
                }

                _settledTicks++;
                if (_settledTicks >= 2 && !_loggedQuality)
                {
                    _loggedQuality = true;
                    var sb = new System.Text.StringBuilder(
                        "[apv] settled after " + _reasserts + " re-assert(s): APV=off");
                    for (int q = 0; q < QualityBits.Length; q++)
                        sb.Append(' ').Append(QualityNames[q]).Append("=True");
                    sb.Append(" (held two consecutive checks). These are contact darkening "
                            + "and reflections; with them held on, the shade buckets can "
                            + "afford to be gentler than they were.");
                    _log?.Msg(sb.ToString());
                }
            }
            catch (Exception e) { _log?.Warning("[apv] tick: " + e.Message); }
        }

        public static void Forget()
        {
            _cam = null;
            _applied = false;

            _settledTicks = 0;
            _reasserts = 0;
            _loggedQuality = false;
            _loggedLosers = false;
            _postWriteValid = false;
            _applyCount = 0;
        }

        public static string Console(string arg)
        {
            if (arg == "on" || arg == "off")
            {
                bool on = arg == "on";
                bool additive = MapIdentity.HostLevelIsMap;
                try
                {
                    var p = additive ? AddPref() : Pref();
                    if (p != null) { p.Value = on; MelonPreferences.Save(); }
                }
                catch { }

                try
                {
                    var cam = Camera.main;
                    var hdCam = cam != null ? cam.GetComponent<HD.HDAdditionalCameraData>() : null;
                    if (hdCam != null)
                    {
                        if (on && (MapIdentity.OwnSky || additive)) Apply(hdCam);
                        else if (!on) Restore(hdCam);
                    }
                }
                catch { }
                return "apv gate " + (on ? "ON (bit 127 forced off; saved)" : "OFF (camera back "
                     + "to the pipeline default; saved)")
                     + (additive ? "  [AdditiveApvOff -- this map borrows a host level; "
                                 + "ON means indirect comes from the ambient probe instead "
                                 + "of APV, which is what stops black-in-shadow]"
                       : MapIdentity.OwnSky ? "" : "  -- inert on this map (no own sky)");
            }
            return Status();
        }

        public static string Status()
        {
            string cam = "no camera";
            string bits = "";
            try
            {
                var c = Camera.main;
                var hdCam = c != null ? c.GetComponent<HD.HDAdditionalCameraData>() : null;
                if (hdCam != null)
                {
                    cam = c.name;
                    var fs = hdCam.renderingPathCustomFrameSettings;
                    bits = "  APV=" + fs.IsEnabled(ApvField)
                         + " SSGI=" + fs.IsEnabled(HD.FrameSettingsField.SSGI)
                         + " SSAO=" + fs.IsEnabled(HD.FrameSettingsField.SSAO)
                         + " ContactShadows=" + fs.IsEnabled((HD.FrameSettingsField)21)
                         + " SSR=" + fs.IsEnabled((HD.FrameSettingsField)23)
                         + " custom=" + hdCam.customRenderingSettings
                         + " reasserts=" + _reasserts;
                }
            }
            catch (Exception e) { bits = "  (readback failed: " + e.Message + ")"; }
            return "apv gate: pref=" + (PrefOn ? "on" : "off")
                 + " additivePref=" + (AddPrefOn ? "on" : "off")
                 + " demand=" + (Demand ? "on" : "off")
                 + " ownSky=" + MapIdentity.OwnSky
                 + " hostLevelIsMap=" + MapIdentity.HostLevelIsMap
                 + " applied=" + _applied
                 + "  cam=" + cam + bits;
        }

        public static string ReadBits()
        {
            try
            {
                var c = Camera.main;
                var hdCam = c != null ? c.GetComponent<HD.HDAdditionalCameraData>() : null;
                if (hdCam == null) return "no-camera";
                var fs = hdCam.renderingPathCustomFrameSettings;
                return "apv=" + fs.IsEnabled(ApvField)
                     + " ssgi=" + fs.IsEnabled(HD.FrameSettingsField.SSGI)
                     + " ssao=" + fs.IsEnabled(HD.FrameSettingsField.SSAO);
            }
            catch (Exception e) { return "unreadable: " + e.Message; }
        }
    }
}
