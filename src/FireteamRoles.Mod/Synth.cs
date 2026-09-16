// ODG Operations - FireteamRoles
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace FireteamRoles
{

    internal static class Synth
    {
        static AudioClip _rotor, _boom;
        const int Rate = 44100;

        internal static AudioClip Rotor()
        {
            if (_rotor != null) return _rotor;
            try
            {
                const float len = 2f;
                int n = (int)(Rate * len);
                var arr = new Il2CppStructArray<float>(n);
                float[] motors = { 171f, 178.5f, 184f, 191.5f };
                float[] phase = { 0.1f, 1.3f, 2.6f, 4.1f };
                float lp = 0f;
                var rng = new System.Random(1337);
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate;
                    float s = 0f;
                    for (int m = 0; m < motors.Length; m++)
                    {
                        float f = motors[m];
                        for (int h = 1; h <= 7; h++)
                        {
                            float amp = 1f / Mathf.Pow(h, 1.25f) * ((h & 1) == 1 ? 1f : 0.7f);
                            s += Mathf.Sin(2f * Mathf.PI * f * h * t + phase[m] * h) * amp;
                        }
                    }
                    s *= 0.055f;

                    s *= 1f + 0.08f * Mathf.Sin(2f * Mathf.PI * 6.5f * t);
                    float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                    lp += (white - lp) * 0.08f;
                    s += lp * 0.10f;
                    arr[i] = Mathf.Clamp(s, -1f, 1f) * 0.9f;
                }
                _rotor = AudioClip.Create("odg_drone_rotor", n, 1, Rate, false);
                if (_rotor == null || !_rotor.SetData(arr, 0)) { Log.Warn("drone: rotor clip failed"); _rotor = null; }
                else { try { _rotor.hideFlags = HideFlags.DontUnloadUnusedAsset; } catch { } }
            }
            catch (Exception e) { Err.Note("synth.rotor", e); _rotor = null; }
            return _rotor;
        }

        internal static AudioClip Boom()
        {
            if (_boom != null) return _boom;
            try
            {
                const float len = 1.1f;
                int n = (int)(Rate * len);
                var arr = new Il2CppStructArray<float>(n);
                var rng = new System.Random(4242);
                float lp = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate;
                    float env = Mathf.Exp(-t * 4.5f);
                    float thump = Mathf.Sin(2f * Mathf.PI * (55f + 90f * Mathf.Exp(-t * 12f)) * t) * Mathf.Exp(-t * 7f);
                    float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                    lp += (white - lp) * Mathf.Lerp(0.6f, 0.08f, Mathf.Clamp01(t * 2f));
                    arr[i] = Mathf.Clamp(thump * 0.9f + lp * env * 0.8f, -1f, 1f);
                }
                _boom = AudioClip.Create("odg_drone_boom", n, 1, Rate, false);
                if (_boom == null || !_boom.SetData(arr, 0)) { _boom = null; }
                else { try { _boom.hideFlags = HideFlags.DontUnloadUnusedAsset; } catch { } }
            }
            catch (Exception e) { Err.Note("synth.boom", e); _boom = null; }
            return _boom;
        }
    }
}
