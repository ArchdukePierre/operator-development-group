// ODG Operations - NewSlots
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace NewSlots
{

    internal static class Art
    {
        internal static string ContentDir
        {
            get
            {
                string root;
                try { root = MelonLoader.Utils.MelonEnvironment.GameRootDirectory; }
                catch { root = AppContext.BaseDirectory; }
                return Path.Combine(root, "Mods", "NewSlots_Content");
            }
        }

        internal static void Pin(UnityEngine.Object o)
        {
            if (o == null) return;
            try { o.hideFlags = HideFlags.DontUnloadUnusedAsset; } catch { }
        }

        internal static bool CompressTextures = false;

        internal static void Seal(Texture2D t)
        {
            if (t == null) return;
            try
            {
                if (CompressTextures && (t.width & 3) == 0 && (t.height & 3) == 0) t.Compress(false);
                t.Apply(false, true);
            }
            catch (Exception e) { NewSlotsMod.Detail("art: seal failed for " + t.name + ": " + e.Message); }
        }

        static readonly System.Collections.Generic.Queue<Texture2D> _sealQ = new System.Collections.Generic.Queue<Texture2D>();
        static int _sealed;

        internal static void QueueSeal(Texture2D t)
        {
            if (t == null) return;
            try { _sealQ.Enqueue(t); } catch { }
        }

        internal static void PumpSeal(float budgetMs)
        {
            if (_sealQ.Count == 0) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (_sealQ.Count > 0)
            {
                Seal(_sealQ.Dequeue());
                _sealed++;
                if (sw.Elapsed.TotalMilliseconds >= budgetMs) break;
            }
            if (_sealQ.Count == 0)
            {
                NewSlotsMod.Log("textures sealed: " + _sealed + " (compressed and CPU copy released)");
                NewSlotsMod.FlushLog();
            }
        }

        internal static Texture2D Texture(Def d)
        {
            var t = Optional(d.Art, d.ModName, false);
            if (t != null) return t;
            return Placeholder(d.ModName);
        }

        static readonly System.Collections.Generic.Dictionary<string, Texture2D> _texCache
            = new System.Collections.Generic.Dictionary<string, Texture2D>();

        internal static Texture2D Optional(string file, string name, bool linear)
        {
            if (string.IsNullOrEmpty(file)) return null;

            string key = file + (linear ? "|lin" : "|srgb");
            Texture2D hit;
            if (_texCache.TryGetValue(key, out hit) && hit != null)
            {
                NewSlotsMod.Detail("art: " + file + " -> reused cached decode");
                return hit;
            }

            var bytes = Pack.ReadBytes(file);
            if (bytes == null) { NewSlotsMod.Detail("art: no sealed entry '" + file + "'"); return null; }

            var t = LoadPng(bytes, name + (linear ? "_n" : ""), linear);
            if (t != null) _texCache[key] = t;
            if (t == null) { NewSlotsMod.Log("art: " + file + " failed to decode"); return null; }
            NewSlotsMod.Detail("art: loaded " + file + "  " + t.width + "x" + t.height + (linear ? "  (linear)" : ""));
            return t;
        }

        static Texture2D LoadPng(byte[] bytes, string name, bool linear)
        {
            try
            {

                var raw = new Il2CppStructArray<byte>(bytes);

                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear);
                tex.name = "ODG_" + name;
                if (!ImageConversion.LoadImage(tex, raw)) return null;
                tex.wrapMode = TextureWrapMode.Repeat;
                tex.Apply(true, false);
                Pin(tex);
                return tex;
            }
            catch (Exception e) { NewSlotsMod.Log("art: LoadPng threw: " + e.Message); return null; }
        }

        internal static Texture2D Placeholder(string name)
        {
            const int N = 256;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true);
            tex.name = "ODG_gen_" + name;
            try
            {
                var px = new Il2CppStructArray<Color32>(N * N);
                var field = new Color32(22, 24, 26, 255);
                var amber = new Color32(217, 158, 41, 255);
                var pale = new Color32(232, 232, 228, 255);

                for (int y = 0; y < N; y++)
                {
                    for (int x = 0; x < N; x++)
                    {
                        Color32 c = field;
                        int edge = 18;
                        bool border = x < edge || y < edge || x >= N - edge || y >= N - edge;
                        int band = (x + y) % 96;
                        if (border) c = amber;
                        else if (band < 22) c = amber;
                        else if (x > N / 2 - 34 && x < N / 2 + 34 && y > N / 2 - 34 && y < N / 2 + 34) c = pale;
                        px[y * N + x] = c;
                    }
                }
                tex.SetPixels32(px);
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.Apply(true, false);
            }
            catch (Exception e) { NewSlotsMod.Log("art: placeholder threw: " + e.Message); }
            Pin(tex);
            return tex;
        }

        internal static Sprite Icon(Texture2D tex, string name)
        {
            if (tex == null) return null;
            try
            {
                var s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                s.name = "ODG_icon_" + name;
                Pin(s);
                return s;
            }
            catch (Exception e) { NewSlotsMod.Log("art: sprite threw: " + e.Message); return null; }
        }

        internal static Material Material(Material donor, Texture2D baseColour, string name)
        {
            Material m = null;
            try
            {
                if (donor != null && donor.shader != null)
                {

                    m = new Material(donor);
                    m.shader = Shader.Find(donor.shader.name) ?? donor.shader;
                }
            }
            catch (Exception e) { NewSlotsMod.Log("art: donor material copy failed: " + e.Message); m = null; }

            if (m == null)
            {
                var sh = Shader.Find("HDRP/Lit");
                if (sh == null) { NewSlotsMod.Log("art: HDRP/Lit not found, patch will render with whatever the clone had"); return null; }
                m = new Material(sh);
            }

            m.name = "ODG_" + name;
            SetIfPresent(m, "_BaseColorMap", baseColour);
            SetIfPresent(m, "_MainTex", baseColour);
            SetIfPresent(m, "_BaseMap", baseColour);

            Pin(m);
            return m;
        }

        internal static bool IsPlaceholder(Material m)
        {
            if (m == null) return true;
            try
            {
                var n = m.name;
                return n == null || n.IndexOf("placeholder", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return true; }
        }

        static void SetIfPresent(Material m, string prop, Texture t)
        {
            try { if (m.HasProperty(prop)) m.SetTexture(prop, t); } catch { }
        }
    }
}
