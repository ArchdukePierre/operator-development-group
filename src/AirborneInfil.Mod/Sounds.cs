// ODG Operations - AirborneInfil
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace AirborneInfil
{

    internal static class Sounds
    {
        const int SR = 22050;
        static AudioClip _c130;
        static AudioClip _open, _fly, _land;

        static string AssetsDir => Path.Combine(AiPaths.GameRoot, "Mods", "AirborneInfil_Assets");

        static AudioClip LoadWav(string name)
        {
            try
            {
                var path = Path.Combine(AssetsDir, name);
                if (!File.Exists(path)) { AirborneMod.Log("sound missing: " + name); return null; }
                var b = File.ReadAllBytes(path);

                int channels = 1, sampleRate = SR, bits = 16, dataOff = -1, dataLen = 0;
                int i = 12;
                while (i + 8 <= b.Length)
                {
                    string id = System.Text.Encoding.ASCII.GetString(b, i, 4);
                    int sz = BitConverter.ToInt32(b, i + 4);
                    if (sz < 0) break;
                    if (id == "fmt " && i + 24 <= b.Length)
                    {
                        channels = BitConverter.ToInt16(b, i + 10);
                        sampleRate = BitConverter.ToInt32(b, i + 12);
                        bits = BitConverter.ToInt16(b, i + 22);
                    }
                    else if (id == "data") { dataOff = i + 8; dataLen = sz; break; }
                    long next = (long)i + 8 + sz + (sz & 1);
                    if (next <= i || next > b.Length) break;
                    i = (int)next;
                }
                if (dataOff < 0 || bits != 16) { AirborneMod.Log("sound bad WAV: " + name); return null; }
                dataLen = Math.Min(dataLen, b.Length - dataOff);
                if (dataLen < 2) { AirborneMod.Log("sound empty WAV: " + name); return null; }
                int n = dataLen / 2;
                var data = new float[n];
                for (int s = 0; s < n; s++)
                    data[s] = BitConverter.ToInt16(b, dataOff + s * 2) / 32768f;
                int frames = n / Math.Max(1, channels);
                var arr = new Il2CppStructArray<float>(n);
                for (int s = 0; s < n; s++) arr[s] = data[s];
                var clip = AudioClip.Create(name, frames, channels, sampleRate, false);
                if (clip == null || !clip.SetData(arr, 0)) { AirborneMod.Log("sound SetData failed: " + name); return null; }
                return clip;
            }
            catch (Exception e) { AirborneMod.Log("LoadWav err " + name + ": " + e.Message); return null; }
        }

        internal static void AttachPlaneAudio(GameObject plane, float volume = 1f)
        {
            if (plane == null) return;
            try
            {
                if (_c130 == null) _c130 = MakeC130();
                if (_c130 == null) return;
                var src = plane.AddComponent<AudioSource>();
                src.clip = _c130; src.loop = true;
                src.spatialBlend = 1f;
                src.dopplerLevel = 1.4f;
                src.rolloffMode = AudioRolloffMode.Logarithmic;
                src.minDistance = 500f;
                src.maxDistance = 3000f;
                src.volume = volume;
                src.Play();
            }
            catch (Exception e) { AirborneMod.Log("plane audio err: " + e.Message); }
        }

        internal static AudioSource AttachFlutter(GameObject host)
        {
            if (host == null) return null;
            try
            {
                if (_fly == null) _fly = LoadWav("parachute_fly.wav");
                if (_fly == null) return null;
                var src = host.AddComponent<AudioSource>();
                src.clip = _fly; src.loop = true;
                src.spatialBlend = 0f;
                src.volume = 0.55f;
                src.Play();
                return src;
            }
            catch { return null; }
        }

        internal static void PlayCollapse()
        {
            try
            {
                if (_land == null) _land = LoadWav("parachute_land.wav");
                if (_land == null) return;
                var go = new GameObject("AirborneLand");
                var src = go.AddComponent<AudioSource>();
                src.clip = _land; src.spatialBlend = 0f; src.volume = 0.9f;
                src.Play();
                UnityEngine.Object.Destroy(go, 2.5f);
            }
            catch { }
        }

        internal static void PlayWhump()
        {
            try
            {
                if (_open == null) _open = LoadWav("parachute_open.wav");
                if (_open != null)
                {
                    var g = new GameObject("AirborneOpen");
                    var s = g.AddComponent<AudioSource>();
                    s.clip = _open; s.spatialBlend = 0f; s.volume = 0.9f;
                    s.Play();
                    UnityEngine.Object.Destroy(g, 6f);
                    return;
                }

                var _whump = MakeWhump();
                if (_whump == null) return;
                var go = new GameObject("AirborneWhump");
                var src = go.AddComponent<AudioSource>();
                src.clip = _whump; src.spatialBlend = 0f; src.volume = 0.85f;
                src.Play();
                UnityEngine.Object.Destroy(go, 1.2f);
            }
            catch { }
        }

        static AudioClip MakeC130()
        {
            try
            {
                const int n = SR * 6;
                var d = new float[n];

                float f0 = 68f;
                float[] det = { 0f, 1f / 3f, -1f / 6f, 0.5f };
                float[] harm = { 1f, 0.55f, 0.30f, 0.18f, 0.08f };
                var rng = new System.Random(77);
                float hp = 0f;
                for (int i = 0; i < n; i++)
                {
                    double t = (double)i / SR;
                    float s = 0f;
                    for (int e = 0; e < det.Length; e++)
                    {
                        float fe = f0 + det[e];
                        for (int h = 0; h < harm.Length; h++)
                            s += harm[h] * (float)Math.Sin(2.0 * Math.PI * fe * (h + 1) * t + e * 1.3 + h * 0.7);
                    }
                    s *= 0.085f;

                    float w = (float)(rng.NextDouble() - 0.5);
                    hp += 0.25f * (w - hp);
                    s += (w - hp) * 0.05f;
                    d[i] = Mathf.Clamp(s, -0.95f, 0.95f);
                }
                return ToClip("AirborneC130Drone", d);
            }
            catch (Exception e) { AirborneMod.Log("C130 synth err: " + e.Message); return null; }
        }

        static AudioClip MakeFlutter()
        {
            try
            {
                const int n = SR * 4;
                var d = new float[n];
                var rng = new System.Random(42);
                float lp = 0f;
                for (int i = 0; i < n; i++)
                {
                    double t = (double)i / SR;
                    float w = (float)(rng.NextDouble() - 0.5);
                    lp += 0.30f * (w - lp);

                    float am = 0.5f + 0.32f * (float)Math.Sin(2.0 * Math.PI * 11.0 * t)
                                    + 0.18f * (float)Math.Sin(2.0 * Math.PI * 3.0 * t);
                    d[i] = Mathf.Clamp(lp * am * 2.2f, -0.9f, 0.9f);
                }
                return ToClip("AirborneFlutter", d);
            }
            catch { return null; }
        }

        static AudioClip MakeWhump()
        {
            try
            {
                int n = (int)(SR * 0.7f);
                var d = new float[n];
                var rng = new System.Random(9);
                float lp = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = (float)i / n;
                    float w = (float)(rng.NextDouble() - 0.5);
                    float coef = Mathf.Lerp(0.45f, 0.05f, t);
                    lp += coef * (w - lp);
                    float env = Mathf.Pow(1f - t, 2.2f);
                    float thump = 0.6f * (float)Math.Sin(2.0 * Math.PI * 55.0 * t * 0.7) * Mathf.Exp(-7f * t);
                    d[i] = Mathf.Clamp(lp * env * 3.2f + thump, -0.95f, 0.95f);
                }
                return ToClip("AirborneWhump", d);
            }
            catch { return null; }
        }

        static AudioClip MakeCollapse()
        {

            try
            {
                int n = (int)(SR * 1.4f);
                var d = new float[n];
                var rng = new System.Random(5);
                float lp = 0f, hp = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = (float)i / n;
                    double ts = (double)i / SR;
                    float w = (float)(rng.NextDouble() - 0.5);

                    float flapHz = Mathf.Lerp(13f, 3f, t);
                    float am = 0.45f + 0.45f * (float)Math.Sin(2.0 * Math.PI * flapHz * ts);
                    float coef = Mathf.Lerp(0.35f, 0.06f, t);
                    lp += coef * (w - lp);
                    float body = lp * am * Mathf.Pow(1f - t, 1.4f) * 3.0f;

                    hp += 0.5f * (w - hp);
                    float zip = (w - hp) * Mathf.Exp(-14f * t) * 0.9f;
                    d[i] = Mathf.Clamp(body + zip, -0.95f, 0.95f);
                }
                return ToClip("AirborneCollapse", d);
            }
            catch { return null; }
        }

        static AudioClip ToClip(string name, float[] data)
        {
            var arr = new Il2CppStructArray<float>(data.Length);
            for (int i = 0; i < data.Length; i++) arr[i] = data[i];
            var clip = AudioClip.Create(name, data.Length, 1, SR, false);
            if (clip == null || !clip.SetData(arr, 0)) return null;
            return clip;
        }
    }
}
