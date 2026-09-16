// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using MelonLoader;
using UnityEngine;
using HD = UnityEngine.Rendering.HighDefinition;

namespace MapLoader
{

    public static class DayArt
    {
        static MelonLogger.Instance _log;
        public static void Init(MelonLogger.Instance log) { _log = log; }

        static GameObject _go;
        static UnityEngine.Rendering.Volume _vol;
        static HD.Tonemapping _tone;
        static HD.ShadowsMidtonesHighlights _smh;
        static HD.Vignette _vig;
        static HD.MicroShadowing _micro;
        static HD.Fog _fog;
        static float _ground = float.NaN;

        public static bool  Enabled    = true;
        public static bool  UseAces    = true;
        public static float FogM       = 700f;
        public static float Vignette   = 0.16f;
        public static float Micro      = 0.55f;
        public static float ShadowCool = 0.06f;
        public static float HighWarm   = 0.05f;

        public static bool  Taa        = true;
        public static float TaaSharpen = 0.15f;
        public static float TaaFlicker = 0.85f;
        public static int   TaaQuality = 2;

        public static bool Built => _go != null;

        static void Restore()
        {
            Enabled    = TuneStore.Load("DayArtOn",      1f) >= 0.5f;
            UseAces    = TuneStore.Load("DayAces",       1f) >= 0.5f;
            FogM       = TuneStore.Load("DayFogM",       700f);
            Vignette   = TuneStore.Load("DayVignette",   0.16f);
            Micro      = TuneStore.Load("DayMicro",      0.55f);
            ShadowCool = TuneStore.Load("DayShadowCool", 0.06f);
            HighWarm   = TuneStore.Load("DayHighWarm",   0.05f);
            Taa        = TuneStore.Load("DayTaa",        1f) >= 0.5f;
            TaaSharpen = TuneStore.Load("DayTaaSharpen", 0.15f);
            TaaFlicker = TuneStore.Load("DayTaaFlicker", 0.85f);
        }

        static float _taaNextAt;
        static int _taaReasserts;
        static bool _taaLogged;
        static float _taaReportAt;

        public static void Tick()
        {
            if (_go == null || !Enabled || !MapRuntime.Done) return;
            if (Time.realtimeSinceStartup < _taaNextAt) return;
            _taaNextAt = Time.realtimeSinceStartup + 1f;
            try
            {
                string rb;
                bool changed = AaDials.AssertTaa(Taa, TaaQuality, TaaSharpen, TaaFlicker, out rb);
                if (changed) _taaReasserts++;
                if (changed && !_taaLogged)
                {
                    _taaLogged = true;
                    _log?.Msg("[dayart] camera anti-aliasing " + (Taa ? "forced to TAA" : "returned to None")
                            + ": " + rb + " -- the game ships its camera with NO anti-aliasing, and "
                            + "alpha-tested foliage under none of it is the salt and pepper. "
                            + "'odg day taa off' hands the camera back.");
                    _taaReportAt = Time.realtimeSinceStartup + 30f;
                }
                else if (_taaReasserts > 1 && Time.realtimeSinceStartup > _taaReportAt)
                {
                    _taaReportAt = Time.realtimeSinceStartup + 60f;
                    _log?.Msg("[dayart] the game put the camera's anti-aliasing back " + (_taaReasserts - 1)
                            + " time(s); re-asserted each time. Now: " + rb);
                }
            }
            catch (Exception e) { _log?.Warning("[dayart] taa assert: " + e.Message); }
        }

        public static string SetTaa(bool on)
        {
            Taa = on;
            TuneStore.Save("DayTaa", on ? 1f : 0f);
            _taaNextAt = 0f; _taaLogged = false;
            string rb;
            AaDials.AssertTaa(on, TaaQuality, TaaSharpen, TaaFlicker, out rb);
            return "camera anti-aliasing " + (on ? "TAA" : "None (the game's own)") + ": " + rb
                 + " -- kept for this map.";
        }

        public static string SetTaaSharpen(float v)
        {
            TaaSharpen = Mathf.Clamp(v, 0f, 2f);
            TuneStore.Save("DayTaaSharpen", TaaSharpen);
            _taaNextAt = 0f;
            string rb; AaDials.AssertTaa(Taa, TaaQuality, TaaSharpen, TaaFlicker, out rb);
            return "TAA sharpen " + TaaSharpen.ToString("F2") + ", kept for this map: " + rb;
        }

        public static string SetTaaFlicker(float v)
        {
            TaaFlicker = Mathf.Clamp01(v);
            TuneStore.Save("DayTaaFlicker", TaaFlicker);
            _taaNextAt = 0f;
            string rb; AaDials.AssertTaa(Taa, TaaQuality, TaaSharpen, TaaFlicker, out rb);
            return "TAA anti-flicker " + TaaFlicker.ToString("F2") + ", kept for this map: " + rb;
        }

        public static void Build(Transform parent)
        {
            if (_go != null) return;
            if (!MapIdentity.CinematicDay) return;
            try
            {
                Restore();
                _go = new GameObject("DAY_ART");
                _go.transform.SetParent(parent, false);
                var v = _go.AddComponent<UnityEngine.Rendering.Volume>();
                v.isGlobal = true;

                v.priority = MapRuntime.OwnVolumePriority() + 256f;
                v.weight = 1f;
                _vol = v;
                var profile = ScriptableObject.CreateInstance(
                    Il2CppInterop.Runtime.Il2CppType.Of<UnityEngine.Rendering.VolumeProfile>())
                    .Cast<UnityEngine.Rendering.VolumeProfile>();
                v.sharedProfile = profile;

                AddTone(profile);
                AddSmh(profile);
                AddVignette(profile);
                AddMicro(profile);
                AddFog(profile);
                _go.SetActive(Enabled);

                _log?.Msg("[dayart] cinematic day " + (Enabled ? "ON" : "OFF") + " at priority "
                        + v.priority.ToString("G") + ": tone " + (UseAces ? "ACES" : "Neutral")
                        + ", haze " + FogM.ToString("F0") + " m, vignette " + Vignette.ToString("F2")
                        + ", micro " + Micro.ToString("F2") + ", shadows cool " + ShadowCool.ToString("F2")
                        + ", highlights warm " + HighWarm.ToString("F2")
                        + ". 'odg day' lists the dials; every one is live and kept per map.");
                try { _log?.Msg("[dayart] camera AA now: " + AaDials.Describe().Replace("\n", " | ")); }
                catch { }
            }
            catch (Exception e) { _log?.Warning("[dayart] Build: " + e.Message); }
        }

        static T Add<T>(UnityEngine.Rendering.VolumeProfile p) where T : UnityEngine.Rendering.VolumeComponent
        {
            var added = p.Add(Il2CppInterop.Runtime.Il2CppType.Of<T>(), false);
            var c = added != null ? added.TryCast<T>() : null;
            if (c != null) c.active = true;
            return c;
        }

        static void Try(Action a, string what)
        {
            try { a(); }
            catch (Exception e) { _log?.Warning("[dayart] " + what + ": " + e.Message); }
        }

        static void AddTone(UnityEngine.Rendering.VolumeProfile p)
        {
            try
            {
                _tone = Add<HD.Tonemapping>(p);
                if (_tone == null) { _log?.Warning("[dayart] no Tonemapping"); return; }
                ApplyTone();
            }
            catch (Exception e) { _log?.Warning("[dayart] tone: " + e.Message); }
        }

        static void ApplyTone()
        {
            if (_tone == null) return;
            Try(() => { _tone.mode.overrideState = true;
                        _tone.mode.value = UseAces ? HD.TonemappingMode.ACES
                                                   : HD.TonemappingMode.Neutral; }, "tone.mode");
        }

        static void AddSmh(UnityEngine.Rendering.VolumeProfile p)
        {
            try
            {
                _smh = Add<HD.ShadowsMidtonesHighlights>(p);
                if (_smh == null) { _log?.Warning("[dayart] no ShadowsMidtonesHighlights"); return; }
                ApplySmh();
            }
            catch (Exception e) { _log?.Warning("[dayart] smh: " + e.Message); }
        }

        static void ApplySmh()
        {
            if (_smh == null) return;
            float c = Mathf.Clamp(ShadowCool, 0f, 0.4f);
            float w = Mathf.Clamp(HighWarm, 0f, 0.4f);
            Try(() => { _smh.shadows.overrideState = true;
                        _smh.shadows.value = new Vector4(1f - c, 1f - c * 0.35f, 1f + c, 0f); },
                "smh.shadows");
            Try(() => { _smh.highlights.overrideState = true;
                        _smh.highlights.value = new Vector4(1f + w, 1f + w * 0.3f, 1f - w, 0f); },
                "smh.highlights");
        }

        static void AddVignette(UnityEngine.Rendering.VolumeProfile p)
        {
            try
            {
                _vig = Add<HD.Vignette>(p);
                if (_vig == null) return;
                Try(() => { _vig.smoothness.overrideState = true; _vig.smoothness.value = 0.5f; }, "vig.smooth");
                Try(() => { _vig.color.overrideState = true;
                            _vig.color.value = new Color(0.03f, 0.03f, 0.04f); }, "vig.color");
                ApplyVignette();
            }
            catch (Exception e) { _log?.Warning("[dayart] vignette: " + e.Message); }
        }

        static void ApplyVignette()
        {
            if (_vig == null) return;
            Try(() => { _vig.intensity.overrideState = true;
                        _vig.intensity.value = Mathf.Clamp01(Vignette); }, "vig.intensity");
        }

        static void AddMicro(UnityEngine.Rendering.VolumeProfile p)
        {
            try
            {
                _micro = Add<HD.MicroShadowing>(p);
                if (_micro == null) return;
                ApplyMicro();
            }
            catch (Exception e) { _log?.Warning("[dayart] micro: " + e.Message); }
        }

        static void ApplyMicro()
        {
            if (_micro == null) return;
            Try(() => { _micro.opacity.overrideState = true;
                        _micro.opacity.value = Mathf.Clamp01(Micro); }, "micro.opacity");
        }

        static void AddFog(UnityEngine.Rendering.VolumeProfile p)
        {
            try
            {
                _fog = Add<HD.Fog>(p);
                if (_fog == null) { _log?.Warning("[dayart] no Fog"); return; }

                Try(() => { _fog.colorMode.overrideState = true;
                            _fog.colorMode.value = HD.FogColorMode.SkyColor; }, "fog.colorMode");
                Try(() => { _fog.tint.overrideState = true;
                            _fog.tint.value = new Color(0.86f, 0.90f, 1.0f); }, "fog.tint");
                Try(() => { _fog.maxFogDistance.overrideState = true;
                            _fog.maxFogDistance.value = 5000f; }, "fog.maxDist");

                Try(() => { _fog.enableVolumetricFog.overrideState = true;
                            _fog.enableVolumetricFog.value = false; }, "fog.volumetric");
                ApplyFog();
            }
            catch (Exception e) { _log?.Warning("[dayart] fog: " + e.Message); }
        }

        static void ApplyFog()
        {
            if (_fog == null) return;
            bool on = FogM > 0f;
            Try(() => { _fog.enabled.overrideState = true; _fog.enabled.value = on; }, "fog.enabled");
            Try(() => { _fog.meanFreePath.overrideState = true;
                        _fog.meanFreePath.value = on ? Mathf.Clamp(FogM, 50f, 20000f) : 20000f; },
                "fog.mfp");

            float g = float.IsNaN(_ground) ? 0f : _ground;
            Try(() => { _fog.baseHeight.overrideState = true; _fog.baseHeight.value = g + 60f; }, "fog.base");
            Try(() => { _fog.maximumHeight.overrideState = true; _fog.maximumHeight.value = g + 400f; }, "fog.max");
        }

        public static void Anchor(float ground)
        {
            _ground = ground;
            if (_fog != null) ApplyFog();
        }

        static string Live(string what) { return _go != null ? what + ", live." : what + " (stored; next build)."; }

        public static string SetEnabled(bool on)
        {
            Enabled = on;
            TuneStore.Save("DayArtOn", on ? 1f : 0f);
            if (_go != null) { try { _go.SetActive(on); } catch { } }
            return Live("cinematic day " + (on ? "ON" : "OFF"));
        }

        public static string SetTone(bool aces)
        {
            UseAces = aces;
            TuneStore.Save("DayAces", aces ? 1f : 0f);
            ApplyTone();
            return Live("tonemap = " + (aces ? "ACES" : "Neutral"));
        }

        public static string SetFog(float m)
        {
            FogM = Mathf.Clamp(m, 0f, 20000f);
            TuneStore.Save("DayFogM", FogM);
            ApplyFog();
            return Live("haze = " + (FogM > 0f ? FogM.ToString("F0") + " m mean free path (smaller is thicker)" : "off"));
        }

        public static string SetVignette(float v)
        {
            Vignette = Mathf.Clamp01(v);
            TuneStore.Save("DayVignette", Vignette);
            ApplyVignette();
            return Live("vignette = " + Vignette.ToString("F2"));
        }

        public static string SetMicro(float v)
        {
            Micro = Mathf.Clamp01(v);
            TuneStore.Save("DayMicro", Micro);
            ApplyMicro();
            return Live("micro shadows = " + Micro.ToString("F2"));
        }

        public static string SetShadowCool(float v)
        {
            ShadowCool = Mathf.Clamp(v, 0f, 0.4f);
            TuneStore.Save("DayShadowCool", ShadowCool);
            ApplySmh();
            return Live("shadow cool = " + ShadowCool.ToString("F2"));
        }

        public static string SetHighWarm(float v)
        {
            HighWarm = Mathf.Clamp(v, 0f, 0.4f);
            TuneStore.Save("DayHighWarm", HighWarm);
            ApplySmh();
            return Live("highlight warm = " + HighWarm.ToString("F2"));
        }

        public static string Describe()
        {
            return "cinematic day: " + (MapIdentity.CinematicDay ? (Built ? (Enabled ? "ON" : "OFF (built)") : "not built yet")
                                                                  : "not on this map")
                 + "\n  camera AA " + (Taa ? "TAA (ours; sharpen " + TaaSharpen.ToString("F2")
                                              + ", anti-flicker " + TaaFlicker.ToString("F2") + ")"
                                            : "the game's own (None)")
                 + "\n  tone " + (UseAces ? "ACES" : "Neutral")
                 + " | haze " + (FogM > 0f ? FogM.ToString("F0") + " m" : "off")
                 + " | vignette " + Vignette.ToString("F2")
                 + " | micro " + Micro.ToString("F2")
                 + " | shadows cool " + ShadowCool.ToString("F2")
                 + " | highlights warm " + HighWarm.ToString("F2")
                 + "\n  sun " + MapRuntime.SunElevation.ToString("F0") + " deg / az "
                 + MapRuntime.SunAzimuth.ToString("F0") + " / " + MapRuntime.SunKelvin.ToString("F0")
                 + " K | shadows " + MapRuntime.HostShadowDimmer.ToString("F2")
                 + " | sky fill " + SkyRig.SkyFill.ToString("F1") + "x | day fill "
                 + SkyRig.FillLux.ToString("F0") + " lux | bloom " + SkyRig.BloomIntensity.ToString("F2")
                 + "\n  look sat " + MapRuntime.Saturation.ToString("F0") + " / con "
                 + MapRuntime.Contrast.ToString("F0") + " / cool " + MapRuntime.CoolTemp.ToString("F0")
                 + " / ev " + MapRuntime.PostExposure.ToString("F1");
        }

        public static void Forget()
        {
            _go = null; _vol = null; _tone = null; _smh = null; _vig = null;
            _micro = null; _fog = null; _ground = float.NaN;
            _taaNextAt = 0f; _taaReasserts = 0; _taaLogged = false; _taaReportAt = 0f;
        }
    }
}
