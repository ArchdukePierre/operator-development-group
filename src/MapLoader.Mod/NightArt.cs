// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using MelonLoader;
using UnityEngine;
using HD = UnityEngine.Rendering.HighDefinition;

namespace MapLoader
{

    public static class NightArt
    {
        static MelonLogger.Instance _log;
        public static void Init(MelonLogger.Instance log) { _log = log; }

        static GameObject _volGo;
        static GameObject _fogGo;
        static HD.LocalVolumetricFog _localFog;
        static HD.Tonemapping _tone;
        static HD.Bloom _bloom;
        static HD.Vignette _vig;
        static HD.ColorAdjustments _grade;
        static HD.ScreenSpaceAmbientOcclusion _ao;
        static HD.ContactShadows _contact;
        static HD.MicroShadowing _micro;
        static HD.SplitToning _split;

        public const int MaxLevel = 5;

        public static int Level = 5;
        static Texture3D _noise;

        public static bool Enabled = true;

        public static void Build(Transform parent)
        {
            if (_volGo != null) return;
            try
            {
                _volGo = new GameObject("NIGHT_ART");
                _volGo.transform.SetParent(parent, false);
                var v = _volGo.AddComponent<UnityEngine.Rendering.Volume>();
                v.isGlobal = true;

                bool ownSky = false;
                try { ownSky = MapIdentity.OwnSky; } catch { }
                v.priority = ownSky ? MapRuntime.OwnVolumePriority() : 1000f;
                _log?.Msg("[art] night grade priority " + v.priority.ToString("G")
                        + (ownSky ? " (out-ranks the host, which is the point on a map "
                                  + "with its own sky)"
                                  : " (the shipped value: on a borrowed host this grade sits "
                                  + "under the level's own stack, which is how this map was "
                                  + "tuned)"));
                v.weight = 1f;

                var profile = ScriptableObject.CreateInstance(
                    Il2CppInterop.Runtime.Il2CppType.Of<UnityEngine.Rendering.VolumeProfile>())
                    .Cast<UnityEngine.Rendering.VolumeProfile>();
                v.sharedProfile = profile;

                AddTonemapping(profile);
                AddDetail(profile);
                AddBloom(profile);
                AddVignette(profile);
                AddGrade(profile);
                AddSplitToning(profile);
                ApplyLevel();

                _log?.Msg("[art] post stack BUILT (tonemap, bloom, vignette, grade). "
                        + "Which of them are ACTIVE is the level line above, not this one.");
            }
            catch (Exception e) { _log?.Warning("[art] Build: " + e.Message); }
        }

        static T Add<T>(UnityEngine.Rendering.VolumeProfile p) where T : UnityEngine.Rendering.VolumeComponent
        {

            var added = p.Add(Il2CppInterop.Runtime.Il2CppType.Of<T>(), false);
            var c = added != null ? added.TryCast<T>() : null;
            if (c != null) c.active = true;
            return c;
        }

        static void AddTonemapping(UnityEngine.Rendering.VolumeProfile p)
        {
            try
            {
                var t = Add<HD.Tonemapping>(p);
                _tone = t;
                if (t == null) { _log?.Warning("[art] no Tonemapping"); return; }
                t.mode.overrideState = true;

                t.mode.value = HD.TonemappingMode.Neutral;
            }
            catch (Exception e) { _log?.Warning("[art] tonemap: " + e.Message); }
        }

        static void AddDetail(UnityEngine.Rendering.VolumeProfile p)
        {
            try
            {
                var ao = Add<HD.ScreenSpaceAmbientOcclusion>(p);
                _ao = ao;
                if (ao != null)
                {
                    ao.intensity.overrideState = true;
                    ao.intensity.value = 1.35f;
                    ao.radius.overrideState = true;
                    ao.radius.value = 1.8f;

                    ao.directLightingStrength.overrideState = true;
                    ao.directLightingStrength.value = 0.35f;
                }

                var cs = Add<HD.ContactShadows>(p);
                _contact = cs;
                if (cs != null)
                {
                    cs.enable.overrideState = true;
                    cs.enable.value = true;
                    cs.length.overrideState = true;
                    cs.length.value = 0.8f;
                    cs.opacity.overrideState = true;
                    cs.opacity.value = 0.75f;
                }

                var ms = Add<HD.MicroShadowing>(p);
                _micro = ms;
                if (ms != null)
                {
                    ms.opacity.overrideState = true;
                    ms.opacity.value = 0.85f;
                }

                _log?.Msg("[art] detail overrides CREATED (not necessarily active): AO "
                        + (_ao != null ? "yes" : "MISSING") + ", contact shadows "
                        + (_contact != null ? "yes" : "MISSING") + ", micro shadows "
                        + (_micro != null ? "yes" : "MISSING")
                        + ". See the frame settings line for what actually renders.");
            }
            catch (Exception e) { _log?.Warning("[art] detail: " + e.Message); }
        }

        static void AddSplitToning(UnityEngine.Rendering.VolumeProfile p)
        {
            try
            {
                var st = Add<HD.SplitToning>(p);
                _split = st;
                if (st == null) return;
                st.shadows.overrideState = true;

                st.shadows.value = new Color(0.52f, 0.55f, 0.62f);
                st.highlights.overrideState = true;
                st.highlights.value = new Color(0.62f, 0.55f, 0.45f);
                st.balance.overrideState = true;
                st.balance.value = -8f;
            }
            catch (Exception e) { _log?.Warning("[art] split toning: " + e.Message); }
        }

        static void AddBloom(UnityEngine.Rendering.VolumeProfile p)
        {
            try
            {
                var b = Add<HD.Bloom>(p);
                _bloom = b;
                if (b == null) return;
                b.intensity.overrideState = true;

                b.intensity.value = 0.45f;
                b.scatter.overrideState = true;
                b.scatter.value = 0.75f;
                b.tint.overrideState = true;
                b.tint.value = new Color(0.93f, 0.95f, 1f);
            }
            catch (Exception e) { _log?.Warning("[art] bloom: " + e.Message); }
        }

        static void AddVignette(UnityEngine.Rendering.VolumeProfile p)
        {
            try
            {
                var vg = Add<HD.Vignette>(p);
                _vig = vg;
                if (vg == null) return;
                vg.intensity.overrideState = true;
                vg.intensity.value = 0.38f;
                vg.smoothness.overrideState = true;
                vg.smoothness.value = 0.6f;

                vg.color.overrideState = true;
                vg.color.value = new Color(0.02f, 0.03f, 0.06f);
            }
            catch (Exception e) { _log?.Warning("[art] vignette: " + e.Message); }
        }

        static void AddGrade(UnityEngine.Rendering.VolumeProfile p)
        {
            try
            {
                var c = Add<HD.ColorAdjustments>(p);
                _grade = c;
                if (c == null) return;

                c.contrast.overrideState = true;
                c.contrast.value = 8f;
                c.saturation.overrideState = true;
                c.saturation.value = -14f;

            }
            catch (Exception e) { _log?.Warning("[art] grade: " + e.Message); }
        }

        public static void BuildLocalFog(Transform parent)
        {
            if (_fogGo != null) return;
            try
            {
                _fogGo = new GameObject("NIGHT_FOG_LOCAL");
                _fogGo.transform.SetParent(parent, false);
                _fogGo.SetActive(false);

                var lf = _fogGo.AddComponent<HD.LocalVolumetricFog>();
                if (lf == null) { _log?.Warning("[art] no LocalVolumetricFog"); return; }

                _noise = BuildNoise();

                var pr = lf.parameters;
                if (pr != null)
                {
                    try { pr.size = new Vector3(60f, 24f, 60f); } catch { }

                    try { pr.meanFreePath = 70f; } catch { }
                    try { pr.albedo = new Color(0.90f, 0.92f, 0.96f); } catch { }

                    try { pr.blendingMode = HD.LocalVolumetricFogBlendingMode.Additive; } catch { }
                    try { pr.distanceFadeStart = 25f; pr.distanceFadeEnd = 60f; } catch { }
                    if (_noise != null)
                    {
                        try { pr.volumeMask = _noise; } catch { }
                        try { pr.textureTiling = new Vector3(2f, 1f, 2f); } catch { }

                        try { pr.textureScrollingSpeed = new Vector3(0.007f, 0.001f, 0.005f); } catch { }
                    }
                    try { lf.parameters = pr; } catch { }
                }

                _fogGo.SetActive(true);
                _log?.Msg("[art] local fog built: 60 m box riding the camera, "
                        + (_noise != null ? "scrolling 3D noise mask" : "NO mask (noise build failed)")
                        + " -- this is the fog that moves as you walk");
            }
            catch (Exception e) { _log?.Warning("[art] BuildLocalFog: " + e.Message); }
        }

        static Texture3D BuildNoise()
        {
            try
            {

                const int N = 64;
                var tex = new Texture3D(N, N, N, TextureFormat.RGBA32, false);
                tex.wrapMode = TextureWrapMode.Repeat;
                tex.filterMode = FilterMode.Bilinear;

                var data = new Il2CppInterop.Runtime.InteropTypes.Arrays
                               .Il2CppStructArray<byte>((long)(N * N * N * 4));
                for (int z = 0; z < N; z++)
                    for (int y = 0; y < N; y++)
                        for (int x = 0; x < N; x++)
                        {

                            float v = Fbm(x, y, z, N);

                            v = 0.30f + 0.55f * Mathf.SmoothStep(0f, 1f, v);

                            float h = (float)y / N;
                            float layer = Mathf.SmoothStep(0f, 0.28f, h)
                                        * (1f - Mathf.SmoothStep(0.45f, 0.98f, h));
                            v *= layer;
                            int o = (x + N * (y + N * z)) * 4;

                            data[o + 0] = 255;
                            data[o + 1] = 255;
                            data[o + 2] = 255;
                            data[o + 3] = (byte)(v * 255f);
                        }

                tex.SetPixelData<byte>(data, 0, 0);
                tex.Apply(false, false);
                return tex;
            }
            catch (Exception e) { _log?.Warning("[art] noise: " + e.Message); return null; }
        }

        static float Hash(int x, int y, int z)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + z * 1013904223);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        static float ValueNoise(float x, float y, float z, int period)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y), z0 = Mathf.FloorToInt(z);
            float fx = x - x0, fy = y - y0, fz = z - z0;

            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            fz = fz * fz * (3f - 2f * fz);
            int x1 = x0 + 1, y1 = y0 + 1, z1 = z0 + 1;
            int px0 = ((x0 % period) + period) % period, px1 = ((x1 % period) + period) % period;
            int py0 = ((y0 % period) + period) % period, py1 = ((y1 % period) + period) % period;
            int pz0 = ((z0 % period) + period) % period, pz1 = ((z1 % period) + period) % period;

            float c000 = Hash(px0, py0, pz0), c100 = Hash(px1, py0, pz0);
            float c010 = Hash(px0, py1, pz0), c110 = Hash(px1, py1, pz0);
            float c001 = Hash(px0, py0, pz1), c101 = Hash(px1, py0, pz1);
            float c011 = Hash(px0, py1, pz1), c111 = Hash(px1, py1, pz1);

            float e = Mathf.Lerp(Mathf.Lerp(c000, c100, fx), Mathf.Lerp(c010, c110, fx), fy);
            float f = Mathf.Lerp(Mathf.Lerp(c001, c101, fx), Mathf.Lerp(c011, c111, fx), fy);
            return Mathf.Lerp(e, f, fz);
        }

        static float Fbm(int x, int y, int z, int n)
        {
            float v = 0f, amp = 0.5f, freq = 8f / n;
            int period = 8;
            for (int o = 0; o < 4; o++)
            {
                v += amp * ValueNoise(x * freq, y * freq, z * freq, period);
                amp *= 0.5f; freq *= 2f; period *= 2;
            }
            return v / 0.9375f;
        }

        public static void ForceFrameSettings()
        {
            try
            {
                var cam = Camera.main;
                if (cam == null) { _log?.Warning("[art] frame settings: no camera"); return; }

                bool ownSky = false;
                try { ownSky = MapIdentity.OwnSky; } catch { }
                if (!ownSky)
                {
                    _log?.Msg("[art] camera frame settings LEFT ALONE: this map borrows the "
                            + "host level's camera setup, exactly as the shipped build did. "
                            + "Forcing volumetrics on here is what produced the radial spokes.");
                    return;
                }

                var hdCam = cam.GetComponent<HD.HDAdditionalCameraData>();
                if (hdCam == null)
                {
                    _log?.Warning("[art] frame settings: camera has no HDAdditionalCameraData; "
                                + "AO, contact shadows and volumetrics cannot be forced on");
                    return;
                }

                var fields = new HD.FrameSettingsField[]
                {
                    HD.FrameSettingsField.SSAO,
                    HD.FrameSettingsField.ContactShadows,
                    HD.FrameSettingsField.Volumetrics,
                    HD.FrameSettingsField.ReprojectionForVolumetrics,
                    HD.FrameSettingsField.AtmosphericScattering,
                    HD.FrameSettingsField.Shadowmask,
                    HD.FrameSettingsField.ScreenSpaceShadows,
                };

                hdCam.customRenderingSettings = true;

                var fs = hdCam.renderingPathCustomFrameSettings;
                var mask = hdCam.renderingPathCustomFrameSettingsOverrideMask;

                int done = 0;
                var sb = new System.Text.StringBuilder();
                foreach (var f in fields)
                {
                    try
                    {

                        fs.bitDatas[(uint)f] = true;
                        mask.mask[(uint)f] = true;
                        done++;
                        sb.Append(f.ToString()).Append(' ');
                    }
                    catch (Exception fe)
                    { sb.Append("!").Append(f.ToString()).Append("(").Append(fe.Message).Append(") "); }
                }

                hdCam.renderingPathCustomFrameSettings = fs;
                hdCam.renderingPathCustomFrameSettingsOverrideMask = mask;

                _log?.Msg("[art] frame settings forced on the camera (" + done + "): " + sb);

                try
                {
                    var chk = hdCam.renderingPathCustomFrameSettings;
                    bool ssao = chk.IsEnabled(HD.FrameSettingsField.SSAO);
                    bool contact = chk.IsEnabled(HD.FrameSettingsField.ContactShadows);
                    _log?.Msg("[art] frame settings readback: custom=" + hdCam.customRenderingSettings
                            + " SSAO=" + ssao
                            + " ContactShadows=" + contact
                            + " Volumetrics=" + chk.IsEnabled(HD.FrameSettingsField.Volumetrics)
                            + "  (all True means the effects can finally reach the screen)");
                    if (!ssao || !contact)
                        _log?.Warning("[art] SSAO and/or contact shadows are OFF at the "
                                    + "camera and could not be forced on. The volume "
                                    + "overrides for them are therefore doing nothing, "
                                    + "which is why stages 1-3 looked identical.");
                }
                catch (Exception re) { _log?.Warning("[art] frame settings readback: " + re.Message); }
            }
            catch (Exception e) { _log?.Warning("[art] ForceFrameSettings: " + e.Message); }
        }

        static bool _framesDone;
        static float _nextFrameTry;

        public static void Tick()
        {

            if (MapRuntime.Done
                && !_framesDone && Time.realtimeSinceStartup > _nextFrameTry)
            {
                _nextFrameTry = Time.realtimeSinceStartup + 1f;
                if (Camera.main != null) { ForceFrameSettings(); _framesDone = true; }
            }

            if (_fogGo == null || !Enabled) return;
            try
            {
                var cam = Camera.main;
                if (cam == null) return;

                var p = cam.transform.position;
                _fogGo.transform.position = new Vector3(p.x, MapRuntime.GroundY + 6f, p.z);
            }
            catch { }
        }

        public static void StepLevel()
        {
            Level = Level > 0 ? 0 : MaxLevel;
            ApplyLevel();
        }

        static readonly string[] LevelName =
        {
            "0 raw scene, no post at all",
            "1 tonemap + detail (AO, contact shadows, micro shadows)",
            "2 + bloom",
            "3 + vignette",
            "4 + grade and split toning (cool shadows, warm highlights)",
            "5 (local fog removed -- it was the source of the sky spokes)",
        };

        public static void ApplyLevel()
        {
            try
            {
                if (_tone  != null) _tone.active  = Level >= 1;
                if (_ao      != null) _ao.active      = Level >= 1;
                if (_contact != null) _contact.active = Level >= 1;
                if (_micro   != null) _micro.active   = Level >= 1;
                if (_split   != null) _split.active   = Level >= 4;
                if (_bloom != null) _bloom.active = Level >= 2;
                if (_vig   != null) _vig.active   = Level >= 3;
                if (_grade != null) _grade.active = Level >= 4;
                if (_volGo != null) _volGo.SetActive(Level >= 1);
                if (_fogGo != null) _fogGo.SetActive(Level >= 5);
                _log?.Msg("[art] cinematic grade " + (Level > 0 ? "ON" : "OFF")
                        + "  (" + LevelName[Mathf.Clamp(Level, 0, MaxLevel)] + ")");
            }
            catch (Exception e) { _log?.Warning("[art] ApplyLevel: " + e.Message); }
        }

        public static void Toggle() { StepLevel(); }

        public static void Forget()
        {
            _volGo = null; _fogGo = null; _localFog = null; _noise = null;
            _tone = null; _bloom = null; _vig = null; _grade = null;
            _ao = null; _contact = null; _micro = null; _split = null;
            _framesDone = false; _nextFrameTry = 0f;
        }
    }
}
