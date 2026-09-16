// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;

namespace MapLoader
{

    public static class MapAmbience
    {
        static MelonLogger.Instance _log;

        const string FileName = "ambience.bin";
        const string Magic = "AMB1";
        const string WavDir = "ambience";

        public static int MaxEmitters = 12;

        public static float EmitterSpacing = 25f;

        public static float ChirpGapMin = 20f;
        public static float ChirpGapMax = 60f;

        public static float FadeSeconds = 3f;

        public static int BedCount { get; private set; }
        public static int EmitterCount { get; private set; }

        static MelonPreferences_Entry<bool> _enabled;
        static MelonPreferences_Entry<float> _volume;

        static GameObject _root;
        static bool _built;

        struct Bed { public AudioSource Src; public float BaseVol; }
        static readonly List<Bed> _beds = new List<Bed>(2);
        static bool _arrived;
        static float _fade;

        struct Chirp { public AudioClip Clip; public float BaseVol; }
        static readonly List<Chirp> _chirps = new List<Chirp>(8);
        static readonly List<AudioSource> _sites = new List<AudioSource>(16);
        static float[] _nextAt;

        struct WaterLoop { public AudioClip Clip; public float BaseVol, Min, Max; }
        static readonly List<WaterLoop> _waterLoops = new List<WaterLoop>(2);
        static readonly List<AudioSource> _waterSites = new List<AudioSource>(12);
        public static int WaterSiteCount { get; private set; }

        sealed class SiteGroup
        {
            public string Display;
            public byte Kind;
            public AudioClip Clip;
            public float BaseVol, Min, Max, GapMin, GapMax, YOffset, Spacing;
            public int MaxSites;
            public string[] Words;
            public readonly List<AudioSource> Srcs = new List<AudioSource>(8);
            public float[] NextAt;
        }
        static readonly List<SiteGroup> _groups = new List<SiteGroup>(8);
        public static int SiteLoopCount { get; private set; }
        public static int SiteShotCount { get; private set; }

        static List<Transform> _offered;
        static List<Transform> _offeredWater;

        static readonly System.Random _rng = new System.Random();

        public static void Init(MelonLogger.Instance log)
        {
            _log = log;
            try
            {
                var cfg = MelonPreferences.GetCategory("MapLoader")
                       ?? MelonPreferences.CreateCategory("MapLoader");
                _enabled = cfg.GetEntry<bool>("AmbienceEnabled")
                        ?? cfg.CreateEntry("AmbienceEnabled", true,
                             description: "Birdsong on maps that ship an ambience layer. "
                                        + "Purely local: every player hears their own copy.");
                _volume = cfg.GetEntry<float>("AmbienceVolume")
                       ?? cfg.CreateEntry("AmbienceVolume", 0.35f,
                             description: "Ambience loudness, 0..1. Multiplied into the "
                                        + "per-sound volumes authored in ambience.bin.");
            }
            catch (Exception e) { _log?.Warning("[amb] prefs: " + e.Message); }
        }

        public static void OfferPlants(List<Transform> plants) { _offered = plants; }
        public static void OfferWater(List<Transform> water) { _offeredWater = water; }

        public static void Build()
        {
            if (_built) return;
            try
            {
                if (!MapIdentity.OwnSky)
                {

                    _built = true;
                    return;
                }

                string path = Path.Combine(MapRuntime.BundleDir, FileName);
                if (!File.Exists(path))
                {

                    _log?.Msg("[amb] no " + FileName + " in bundle, skipping");
                    _built = true;
                    return;
                }

                var recs = ReadAll(path);
                if (recs == null || recs.Count == 0) { _built = true; return; }

                var parent = MapRuntime.Root != null ? MapRuntime.Root.transform : null;
                if (parent == null)
                {
                    _log?.Warning("[amb] no map root to hang the ambience under");
                    return;
                }

                _root = new GameObject("MAPAMBIENCE");
                _root.transform.SetParent(parent, true);

                ProbeMixer();

                var sw = System.Diagnostics.Stopwatch.StartNew();
                string wavRoot = Path.Combine(MapRuntime.BundleDir, WavDir);
                int loaded = 0;
                float emMin = 8f, emMax = 40f;
                for (int i = 0; i < recs.Count; i++)
                {
                    var rec = recs[i];
                    var clip = LoadWav(Path.Combine(wavRoot, rec.File), rec.Name);
                    if (clip == null) continue;
                    loaded++;
                    if (rec.Kind == 0)
                    {

                        var src = _root.AddComponent<AudioSource>();
                        src.clip = clip;
                        src.loop = true;
                        src.playOnAwake = false;
                        src.spatialBlend = 0f;
                        src.volume = 0f;
                        _beds.Add(new Bed { Src = src, BaseVol = rec.Volume });
                    }
                    else if (rec.Kind == 1)
                    {
                        _chirps.Add(new Chirp { Clip = clip, BaseVol = rec.Volume });

                        if (rec.MinDist > 0f) emMin = rec.MinDist;
                        if (rec.MaxDist > 0f) emMax = rec.MaxDist;
                    }
                    else if (rec.Kind == 2)
                    {
                        _waterLoops.Add(new WaterLoop
                        {
                            Clip = clip,
                            BaseVol = rec.Volume,
                            Min = rec.MinDist > 0f ? rec.MinDist : 6f,
                            Max = rec.MaxDist > 0f ? rec.MaxDist : 42f,
                        });
                    }
                    else if (rec.Kind == 3 || rec.Kind == 4 || rec.Kind == 5)
                    {
                        var g = ParseGroup(rec, clip);
                        if (g != null) _groups.Add(g);
                    }
                    else
                    {

                        _log?.Msg("[amb] " + rec.Name + ": unknown kind " + rec.Kind + ", skipped");
                    }
                }
                sw.Stop();

                if (_chirps.Count > 0) BuildEmitters(emMin, emMax);
                if (_waterLoops.Count > 0) BuildWaterSites();
                if (_groups.Count > 0) BuildGroups();

                BedCount = _beds.Count;
                EmitterCount = _sites.Count;
                _built = true;
                _log?.Msg("[amb] " + loaded + " clip(s) decoded in " + sw.ElapsedMilliseconds
                        + " ms: " + _beds.Count + " bed(s) armed (starting once the veil "
                        + "is down), " + _chirps.Count + " chirp sound(s) across "
                        + _sites.Count + " tree point(s).");
            }
            catch (Exception e)
            {
                _log?.Warning("[amb] build stopped: " + e.Message);
                _built = true;
            }
        }

        struct Rec
        {
            public string Name;
            public byte Kind;
            public string File;
            public float Volume, MinDist, MaxDist;
        }

        static List<Rec> ReadAll(string path)
        {
            var list = new List<Rec>(16);
            using (var fs = File.OpenRead(path))
            using (var r = new BinaryReader(fs))
            {
                if (new string(r.ReadChars(4)) != Magic)
                {
                    _log?.Warning("[amb] " + FileName + ": bad magic");
                    return null;
                }
                int n = r.ReadInt32();
                if (n < 0 || n > 1000)
                {
                    _log?.Warning("[amb] " + FileName + ": absurd count " + n);
                    return null;
                }
                for (int i = 0; i < n; i++)
                {
                    var rec = new Rec();
                    int len = r.ReadUInt16();
                    rec.Name = len > 0
                        ? System.Text.Encoding.UTF8.GetString(r.ReadBytes(len))
                        : ("amb_" + i);
                    rec.Kind = r.ReadByte();
                    len = r.ReadUInt16();
                    rec.File = len > 0
                        ? System.Text.Encoding.UTF8.GetString(r.ReadBytes(len))
                        : "";
                    rec.Volume = r.ReadSingle();
                    rec.MinDist = r.ReadSingle();
                    rec.MaxDist = r.ReadSingle();
                    r.ReadByte();
                    if (rec.File.Length == 0)
                    {
                        _log?.Warning("[amb] " + rec.Name + ": no wav filename, skipped");
                        continue;
                    }
                    list.Add(rec);
                }
            }
            return list;
        }

        static SiteGroup ParseGroup(Rec rec, AudioClip clip)
        {
            var g = new SiteGroup { Kind = rec.Kind, Clip = clip, BaseVol = rec.Volume };
            g.Min = rec.MinDist > 0f ? rec.MinDist : 4f;
            g.Max = rec.MaxDist > 0f ? rec.MaxDist : 30f;
            g.GapMin = 30f; g.GapMax = 120f; g.MaxSites = 8; g.Spacing = 12f;
            var parts = (rec.Name ?? "").Split('|');
            g.Display = parts.Length > 0 ? parts[0].Trim() : rec.Name;
            for (int i = 1; i < parts.Length; i++)
            {
                var kv = parts[i].Split(new[] { '=' }, 2);
                if (kv.Length != 2) continue;
                string k = kv[0].Trim().ToLowerInvariant(), v = kv[1].Trim();
                float f;
                if (k == "mesh") g.Words = v.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                else if (k == "max") { int n; if (int.TryParse(v, out n)) g.MaxSites = Mathf.Clamp(n, 1, 32); }
                else if (k == "spacing") { if (float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f)) g.Spacing = f; }
                else if (k == "y") { if (float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f)) g.YOffset = f; }
                else if (k == "gap")
                {
                    var ab = v.Split('-');
                    float a, b;
                    if (ab.Length == 2
                        && float.TryParse(ab[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out a)
                        && float.TryParse(ab[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out b))
                    { g.GapMin = Mathf.Max(1f, a); g.GapMax = Mathf.Max(g.GapMin, b); }
                }
            }
            if (g.Kind != 5 && (g.Words == null || g.Words.Length == 0))
            {
                _log?.Warning("[amb] " + g.Display + ": kind " + g.Kind + " needs mesh=; skipped");
                return null;
            }
            return g;
        }

        static List<Vector3> FindSites(SiteGroup g)
        {
            var pos = new List<Vector3>(64);
            var root = MapRuntime.Root != null ? MapRuntime.Root.transform : null;
            if (root == null) return pos;
            int n = root.childCount;
            for (int i = 0; i < n; i++)
            {
                var c = root.GetChild(i);
                string nm = null;
                try { nm = c.name; } catch { continue; }
                if (nm == null) continue;
                bool hit = false;
                for (int w = 0; w < g.Words.Length && !hit; w++)
                    hit = nm.IndexOf(g.Words[w], StringComparison.OrdinalIgnoreCase) >= 0;
                if (!hit) continue;
                Vector3 p;
                try
                {
                    var r = c.GetComponent<Renderer>();
                    p = r != null ? r.bounds.center : c.position;
                }
                catch { continue; }
                p.y += g.YOffset;
                pos.Add(p);
            }
            if (pos.Count == 0) return pos;
            var order = Rank(pos);
            float sp2 = g.Spacing * g.Spacing;
            var picked = new List<Vector3>(g.MaxSites);
            for (int k = 0; k < order.Count && picked.Count < g.MaxSites; k++)
            {
                var p = pos[order[k]];
                bool crowded = false;
                for (int j = 0; j < picked.Count; j++)
                    if ((picked[j] - p).sqrMagnitude < sp2) { crowded = true; break; }
                if (!crowded) picked.Add(p);
            }
            return picked;
        }

        static void BuildGroups()
        {
            int loops = 0, shots = 0;
            for (int gi = 0; gi < _groups.Count; gi++)
            {
                var g = _groups[gi];
                if (g.Kind == 5)
                {
                    var src = _root.AddComponent<AudioSource>();
                    src.clip = g.Clip; src.loop = false; src.playOnAwake = false;
                    src.spatialBlend = 0f; src.volume = 0f;
                    g.Srcs.Add(src);
                    g.NextAt = new float[] { Time.time + g.GapMin * (0.3f + 0.7f * (float)_rng.NextDouble()) };
                    shots++;
                    _log?.Msg("[amb] " + g.Display + ": 2D one-shot every " + g.GapMin.ToString("F0")
                            + "-" + g.GapMax.ToString("F0") + " s");
                    continue;
                }
                var sites = FindSites(g);
                if (sites.Count == 0)
                {
                    _log?.Msg("[amb] " + g.Display + ": no instance named like '"
                            + string.Join(",", g.Words) + "' under the map root; skipped");
                    continue;
                }
                for (int k = 0; k < sites.Count; k++)
                {
                    var go = new GameObject((g.Kind == 3 ? "loop_" : "shot_") + gi + "_" + k);
                    go.transform.SetParent(_root.transform, true);
                    go.transform.position = sites[k];
                    var src = go.AddComponent<AudioSource>();
                    src.clip = g.Clip;
                    src.playOnAwake = false;
                    src.loop = g.Kind == 3;
                    src.spatialBlend = 1f;
                    src.rolloffMode = AudioRolloffMode.Logarithmic;
                    src.minDistance = g.Min;
                    src.maxDistance = g.Max;
                    src.dopplerLevel = 0f;
                    src.volume = 0f;
                    g.Srcs.Add(src);
                }
                if (g.Kind == 3) loops += sites.Count; else shots += sites.Count;
                g.NextAt = new float[g.Srcs.Count];
                for (int k = 0; k < g.NextAt.Length; k++)
                    g.NextAt[k] = Time.time + 8f + (float)_rng.NextDouble() * g.GapMax;
                _log?.Msg("[amb] " + g.Display + ": " + sites.Count + " site(s) at '"
                        + string.Join(",", g.Words) + "', rolloff " + g.Min.ToString("F0") + "-"
                        + g.Max.ToString("F0") + " m" + (g.Kind == 4 ? ", one-shot every "
                        + g.GapMin.ToString("F0") + "-" + g.GapMax.ToString("F0") + " s" : ", looping"));
            }
            SiteLoopCount = loops; SiteShotCount = shots;
        }

        static AudioClip LoadWav(string path, string name)
        {
            try
            {
                if (!File.Exists(path))
                {
                    _log?.Warning("[amb] wav missing: " + Path.GetFileName(path));
                    return null;
                }
                var b = File.ReadAllBytes(path);
                int channels = 1, sampleRate = 44100, bits = 16, dataOff = -1, dataLen = 0;
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
                if (dataOff < 0 || bits != 16 || channels < 1 || channels > 2)
                {
                    _log?.Warning("[amb] bad WAV (want 16-bit PCM mono/stereo): "
                                + Path.GetFileName(path));
                    return null;
                }
                dataLen = Math.Min(dataLen, b.Length - dataOff);
                if (dataLen < 2) { _log?.Warning("[amb] empty WAV: " + Path.GetFileName(path)); return null; }

                int nsamp = dataLen / 2;
                var arr = new Il2CppStructArray<float>(nsamp);
                for (int s = 0; s < nsamp; s++)
                    arr[s] = BitConverter.ToInt16(b, dataOff + s * 2) / 32768f;
                int frames = nsamp / channels;
                var clip = AudioClip.Create(name, frames, channels, sampleRate, false);
                if (clip == null || !clip.SetData(arr, 0))
                {
                    _log?.Warning("[amb] SetData failed: " + Path.GetFileName(path));
                    return null;
                }
                return clip;
            }
            catch (Exception e)
            {
                _log?.Warning("[amb] LoadWav " + name + ": " + e.Message);
                return null;
            }
        }

        static void BuildEmitters(float minDist, float maxDist)
        {
            var plants = _offered;
            if (plants == null || plants.Count == 0)
            {
                _log?.Msg("[amb] no plant list offered; the bed plays alone");
                return;
            }

            var pos = new List<Vector3>(64);
            for (int i = 0; i < plants.Count; i++)
            {
                var t = plants[i];
                if (t == null) continue;
                try
                {
                    var rend = t.GetComponentInChildren<Renderer>();
                    if (rend == null) continue;
                    var bb = rend.bounds;
                    if (bb.size.y < 4f) continue;

                    var p = t.position;
                    p.y += Mathf.Min(bb.size.y * 0.66f, 8f);
                    pos.Add(p);
                }
                catch { }
            }
            if (pos.Count == 0)
            {
                _log?.Msg("[amb] no plant stands over 4 m; the bed plays alone");
                return;
            }

            var order = Rank(pos);
            float spacingSq = EmitterSpacing * EmitterSpacing;
            var picked = new List<Vector3>(MaxEmitters);
            for (int k = 0; k < order.Count && picked.Count < MaxEmitters; k++)
            {
                var p = pos[order[k]];
                bool crowded = false;
                for (int j = 0; j < picked.Count; j++)
                    if ((picked[j] - p).sqrMagnitude < spacingSq) { crowded = true; break; }
                if (!crowded) picked.Add(p);
            }

            for (int k = 0; k < picked.Count; k++)
            {
                var go = new GameObject("chirp_" + k);
                go.transform.SetParent(_root.transform, true);
                go.transform.position = picked[k];
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = false;
                src.spatialBlend = 1f;
                src.rolloffMode = AudioRolloffMode.Logarithmic;
                src.minDistance = minDist;
                src.maxDistance = maxDist;

                src.dopplerLevel = 0f;
                _sites.Add(src);
            }

            _nextAt = new float[_sites.Count];
            for (int k = 0; k < _sites.Count; k++)
                _nextAt[k] = Time.time + 5f + (float)_rng.NextDouble() * (ChirpGapMax - 5f);
        }

        static void BuildWaterSites()
        {
            var water = _offeredWater;
            if (water == null || water.Count == 0)
            {
                _log?.Msg("[amb] water loop authored but the map offered no "
                        + "water meshes; skipped");
                return;
            }

            var pos = new List<Vector3>(water.Count);
            for (int i = 0; i < water.Count; i++)
            {
                var t = water[i];
                if (t == null) continue;
                try
                {

                    var rend = t.GetComponentInChildren<Renderer>();
                    pos.Add(rend != null ? rend.bounds.center : t.position);
                }
                catch { }
            }
            if (pos.Count == 0) return;

            const float spacing = 35f;
            float spacingSq = spacing * spacing;
            var picked = new List<Vector3>(_waterSites.Capacity);
            for (int i = 0; i < pos.Count && picked.Count < 12; i++)
            {
                bool crowded = false;
                for (int j = 0; j < picked.Count; j++)
                    if ((picked[j] - pos[i]).sqrMagnitude < spacingSq) { crowded = true; break; }
                if (!crowded) picked.Add(pos[i]);
            }

            var wl = _waterLoops[0];
            for (int k = 0; k < picked.Count; k++)
            {
                var go = new GameObject("water_" + k);
                go.transform.SetParent(_root.transform, true);
                go.transform.position = picked[k];
                var src = go.AddComponent<AudioSource>();
                src.clip = wl.Clip;
                src.playOnAwake = false;
                src.loop = true;
                src.spatialBlend = 1f;
                src.rolloffMode = AudioRolloffMode.Logarithmic;
                src.minDistance = wl.Min;
                src.maxDistance = wl.Max;
                src.dopplerLevel = 0f;
                src.volume = 0f;
                _waterSites.Add(src);
            }
            WaterSiteCount = _waterSites.Count;
            _log?.Msg("[amb] water: " + _waterSites.Count + " loop site(s) along "
                    + pos.Count + " water segment(s), rolloff " + wl.Min.ToString("F0")
                    + "-" + wl.Max.ToString("F0") + " m");
        }

        static List<int> Rank(List<Vector3> pts)
        {
            var order = new List<int>(pts.Count);
            for (int i = 0; i < pts.Count; i++) order.Add(i);

            var anchors = new List<Vector3>(300);
            try
            {
                var mt = MapRuntime.MarkersRoot;
                if (mt != null)
                    for (int g = 0; g < mt.childCount; g++)
                    {
                        var grp = mt.GetChild(g);
                        for (int i = 0; i < grp.childCount; i++)
                            anchors.Add(grp.GetChild(i).position);
                    }
            }
            catch { }
            if (anchors.Count < 8) return order;

            var score = new float[pts.Count];
            for (int i = 0; i < pts.Count; i++)
            {
                float best = float.MaxValue;
                for (int a = 0; a < anchors.Count; a++)
                {
                    float d = (anchors[a] - pts[i]).sqrMagnitude;
                    if (d < best) best = d;
                }
                score[i] = best;
            }
            order.Sort((a, b) => score[a].CompareTo(score[b]));
            return order;
        }

        static void ProbeMixer()
        {
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<AudioSource>();
                for (int i = 0; all != null && i < all.Length; i++)
                {
                    var s = all[i];
                    if (s == null) continue;
                    try { if (_root != null && s.transform.IsChildOf(_root.transform)) continue; }
                    catch { continue; }
                    var g = s.outputAudioMixerGroup;
                    _log?.Msg("[amb] mixer probe: scene source '" + s.gameObject.name
                            + "' routes to " + (g == null
                                ? "NO mixer group -- bare sources sit outside the game's "
                                + "volume sliders, so the ambience answers only to its own pref"
                                : "mixer group '" + g.name + "' -- a later pass can adopt it "
                                + "so the game's sliders govern the birds"));
                    return;
                }
                _log?.Msg("[amb] mixer probe: no live scene AudioSource to read");
            }
            catch (Exception e) { _log?.Warning("[amb] mixer probe: " + e.Message); }
        }

        public static void Tick()
        {
            if (_root == null) return;
            try
            {

                if (!_arrived && MapRuntime.Done && !LoadVeil.Showing)
                    _arrived = true;
                if (!_arrived) return;

                bool on = _enabled == null || _enabled.Value;
                float pref = _volume != null ? Mathf.Clamp01(_volume.Value) : 0.35f;

                _fade = Mathf.MoveTowards(_fade, on ? 1f : 0f,
                                          Time.deltaTime / Mathf.Max(0.1f, FadeSeconds));
                for (int i = 0; i < _beds.Count; i++)
                {
                    var b = _beds[i];
                    if (b.Src == null) continue;
                    if (!b.Src.isPlaying && on) b.Src.Play();
                    b.Src.volume = _fade * pref * b.BaseVol;
                }

                if (_waterLoops.Count > 0)
                {
                    float wv = _fade * pref * _waterLoops[0].BaseVol;
                    for (int i = 0; i < _waterSites.Count; i++)
                    {
                        var s = _waterSites[i];
                        if (s == null) continue;
                        if (!s.isPlaying && on && s.clip != null)
                        {
                            try { s.time = (float)_rng.NextDouble() * s.clip.length; } catch { }
                            s.Play();
                        }
                        s.volume = wv;
                    }
                }

                bool fired = false;
                float tnow = Time.time;
                for (int gi = 0; gi < _groups.Count; gi++)
                {
                    var g = _groups[gi];
                    if (g.Kind == 3)
                    {
                        float gv = _fade * pref * g.BaseVol;
                        for (int k = 0; k < g.Srcs.Count; k++)
                        {
                            var s = g.Srcs[k];
                            if (s == null) continue;
                            if (!s.isPlaying && on && s.clip != null)
                            {
                                try { s.time = (float)_rng.NextDouble() * s.clip.length; } catch { }
                                s.Play();
                            }
                            s.volume = gv;
                        }
                        continue;
                    }
                    if (!on || fired || g.NextAt == null) continue;
                    for (int k = 0; k < g.Srcs.Count; k++)
                    {
                        if (tnow < g.NextAt[k]) continue;
                        g.NextAt[k] = tnow + g.GapMin + (float)_rng.NextDouble() * (g.GapMax - g.GapMin);
                        var s = g.Srcs[k];
                        if (s == null || s.clip == null) continue;
                        s.pitch = 0.94f + 0.12f * (float)_rng.NextDouble();
                        s.volume = pref * g.BaseVol * _fade;
                        s.Play();
                        fired = true;
                        break;
                    }
                }

                if (!on || _chirps.Count == 0 || _nextAt == null) return;
                float now = Time.time;
                for (int k = 0; k < _sites.Count; k++)
                {
                    if (now < _nextAt[k]) continue;
                    _nextAt[k] = now + ChirpGapMin
                               + (float)_rng.NextDouble() * (ChirpGapMax - ChirpGapMin);
                    var src = _sites[k];
                    if (src == null) continue;
                    var c = _chirps[_rng.Next(_chirps.Count)];
                    if (c.Clip == null) continue;
                    src.clip = c.Clip;
                    src.pitch = 0.95f + 0.10f * (float)_rng.NextDouble();
                    src.volume = pref * c.BaseVol;
                    src.Play();
                    break;
                }
            }
            catch (Exception e) { _log?.Warning("[amb] tick: " + e.Message); }
        }

        public static void SetEnabled(bool on)
        {
            try { if (_enabled != null) _enabled.Value = on; } catch { }
        }

        public static void SetVolume(float v)
        {
            try { if (_volume != null) _volume.Value = Mathf.Clamp01(v); } catch { }
        }

        public static string Describe()
        {
            bool on = _enabled == null || _enabled.Value;
            float pref = _volume != null ? _volume.Value : 0.35f;
            if (_root == null)
                return "ambience: nothing built on this map ("
                     + (on ? "enabled" : "disabled") + ", volume "
                     + pref.ToString("F2") + ")";
            return "ambience: " + _beds.Count + " bed(s) "
                 + (_arrived ? (on ? "playing" : "faded out (off)") : "waiting for the veil")
                 + ", " + _sites.Count + " chirp point(s), volume "
                 + pref.ToString("F2") + (on ? "" : ", DISABLED");
        }

        public static void Forget()
        {
            _root = null;
            _beds.Clear();
            _chirps.Clear();
            _sites.Clear();
            _waterLoops.Clear();
            _waterSites.Clear();
            _groups.Clear();
            SiteLoopCount = 0; SiteShotCount = 0;
            _nextAt = null;
            _offered = null;
            _offeredWater = null;
            _arrived = false;
            _fade = 0f;
            _built = false;
            BedCount = 0;
            EmitterCount = 0;
            WaterSiteCount = 0;
        }
    }
}
