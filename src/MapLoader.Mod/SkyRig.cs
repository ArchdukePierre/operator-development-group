// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace MapLoader
{

    internal static class SkyRig
    {

        internal static float SunLux = 30000f;
        internal static float SunKelvin = 5400f;

        internal static float SunElevation = 90f;
        internal static float SunAzimuth = 125f;
        internal static float CityDayEV = 14.7f;

        internal static float DomeExposure = 12.8f;
        internal static float DomeTrimEV = 0f;

        internal static bool UseHdriDay = true;

        internal static float SkyFill = 3.0f;

        internal static float FillLux = 2200f;

        static bool _cineWas;

        internal static void ApplySkyLook()
        {
            string map = null;
            try { map = MapIdentity.SceneName; } catch { }

            bool st = string.Equals(map, "SmallTown", System.StringComparison.OrdinalIgnoreCase)
                   || string.Equals(map, "Backroad", System.StringComparison.OrdinalIgnoreCase)

                   || string.Equals(map, "Villa", System.StringComparison.OrdinalIgnoreCase);

            float elev = MapIdentity.DaySunElevationDeg;
            MapRuntime.SunElevation = elev > 0f ? elev : 64f;

            bool sea = false;
            try { sea = MapIdentity.SeaSky; } catch { }
            SunGlow      = st ? 1.9f : sea ? 1.4f : 1f;
            SunGlowWidth = st ? 1.25f : sea ? 1.15f : 1f;
            BloomIntensity = st ? 0.22f : 0.08f;

            SkyFill = st ? 4.2f : SkyFill;

            DomeTrimEV = st ? 1.3f : sea ? 0.7f : 0f;

            bool cine = MapIdentity.CinematicDay;
            float kelvin = MapIdentity.DaySunKelvin;
            MapRuntime.SunKelvin = kelvin > 0f ? kelvin : 6100f;
            float dim = MapIdentity.HostShadowDimmerOverride;
            MapRuntime.HostShadowDimmer = dim > 0f ? dim : 0.85f;
            if (cine)
            {
                FillLux = 1300f;
                SkyFill = 3.4f;
                BloomIntensity = 0.16f;
                MapRuntime.Saturation = 10f;

                MapRuntime.Contrast = 6f;
                MapRuntime.CoolTemp = 4f;
                _log?.Msg("[sky] cinematic day preset: sun " + MapRuntime.SunElevation.ToString("F0")
                        + " deg / " + MapRuntime.SunKelvin.ToString("F0") + " K, shadows "
                        + MapRuntime.HostShadowDimmer.ToString("F2") + ", day fill 1300 lux, sky fill "
                        + "3.4x, bloom 0.16, look sat 10 / con 6 / cool +4. 'odg day' lists every "
                        + "dial; 'odg day preset' re-applies these.");
            }
            else if (_cineWas)
            {
                FillLux = 2200f;
                SkyFill = 3.0f;
                BloomIntensity = 0.08f;
                MapRuntime.Saturation = 16f;
                MapRuntime.Contrast = 8f;
                MapRuntime.CoolTemp = -12f;
            }
            _cineWas = cine;
        }

        internal static bool PaintSunCore = true;

        internal static float SunGlow = 1f;
        internal static float SunGlowWidth = 1f;
        internal static float BloomIntensity = 0.08f;

        internal static bool Clouds = true;

        internal static bool NightSky = false;

        internal static int DaySkyMode = 0;

        static MelonLogger.Instance _log;
        static HDAdditionalLightData _sunHd;
        static HDRISky _dayHdri;
        static GradientSky _dayGrad;
        static VisualEnvironment _dayEnv;
        static Cubemap _skyCube;
        static IndirectLightingController _indirect;
        static float _probeCheckAt = -1f;
        static int _probeChecks;
        static VolumetricClouds _dayClouds;
        static GradientSky _nightSky;
        static PhysicallyBasedSky _dayPbs;
        static string _dayDome = "not built";

        internal static void Init(MelonLogger.Instance log) { _log = log; }

        internal static void RegisterSun(HDAdditionalLightData hd) { _sunHd = hd; }

        internal static void Forget()
        {
            _sunHd = null; _dayHdri = null; _dayGrad = null; _dayClouds = null;
            _nightSky = null; _dayBloom = null; _dayDome = "not built";
            _dayEnv = null; _indirect = null; _skyCube = null; _fills.Clear();
            _dayPbs = null; _hdActive = false;
            _probeCheckAt = -1f; _probeChecks = 0;

        }

        internal static float DaySunLux
        { get { return MapIdentity.OwnSky ? SunLux : 52241.375f; } }
        internal static float DaySunKelvin
        { get { return MapIdentity.OwnSky ? SunKelvin : 6727f; } }
        internal static Vector3 DaySunEuler
        {
            get
            {
                return MapIdentity.OwnSky
                    ? new Vector3(SunElevation, SunAzimuth, 0f)

                    : new Vector3(71.70f, 167.48f, 141.42f);
            }
        }
        internal static float DayEV
        { get { return MapIdentity.OwnSky ? CityDayEV : 15.5f; } }

        internal static void Add(VolumeProfile profile, bool isNight, string rigName)
        {
            ApplySkyLook();
            PaintSunCore = true;
            if (!MapIdentity.OwnSky) return;
            if (isNight && !NightSky) return;
            if (profile == null) return;
            try
            {
                if (isNight)
                {
                    if (!AddVisualEnvironment(profile, SkyType.Gradient, rigName, true)) return;
                    AddGradientSky(profile, rigName);
                }
                else if (DaySkyMode == 2)
                {

                    if (!AddVisualEnvironment(profile, SkyType.PhysicallyBased, rigName))
                        return;
                    AddDayGradient(profile, rigName);
                    AddPbsSky(profile, rigName);
                    AddBloom(profile, rigName);
                    AddIndirect(profile, rigName);
                    _probeCheckAt = Time.time + 6f;
                    _probeChecks = 0;
                }
                else
                {

                    var cube = UseHdSky ? LoadHdSkyCubemap(rigName) : null;
                    _hdActive = cube != null;
                    if (cube == null) cube = BuildSkyCubemap(rigName);
                    bool hdri = UseHdriDay && cube != null;

                    if (!AddVisualEnvironment(profile, hdri ? SkyType.HDRI
                                                            : SkyType.Gradient, rigName))
                        return;

                    AddDayGradient(profile, rigName);
                    if (cube != null) AddHdriSky(profile, rigName, cube);

                    if (Clouds && !_hdActive) AddClouds(profile, rigName);
                    AddBloom(profile, rigName);
                    AddIndirect(profile, rigName);
                    _probeCheckAt = Time.time + 6f;
                    _probeChecks = 0;
                }
            }
            catch (Exception e) { _log?.Warning("[sky] " + rigName + ": " + e.Message); }
        }

        static T AddOverride<T>(VolumeProfile profile, string what, string rigName)
            where T : VolumeComponent
        {
            var comp = profile.Add(Il2CppInterop.Runtime.Il2CppType.Of<T>(), true);
            var c = comp != null ? comp.TryCast<T>() : null;
            if (c == null)
            {
                _log?.Warning("[sky] " + rigName + ": could not add " + what
                            + " -- the rig keeps the host sky");
                return null;
            }
            c.active = true;
            return c;
        }

        static bool AddVisualEnvironment(VolumeProfile profile, SkyType skyType, string rigName,
                                         bool isNightEnv = false)
        {
            var ve = AddOverride<VisualEnvironment>(profile, "VisualEnvironment", rigName);
            if (ve == null) return false;
            ve.skyType.overrideState = true;
            ve.skyType.value = (int)skyType;

            try
            {
                ve.skyAmbientMode.overrideState = true;
                ve.skyAmbientMode.value = SkyAmbientMode.Dynamic;
            }
            catch (Exception e) { _log?.Warning("[sky] skyAmbientMode: " + e.Message); }

            if (!isNightEnv) _dayEnv = ve;
            _log?.Msg("[sky] " + rigName + ": skyType=" + ve.skyType.value
                    + " (" + skyType + ") override=" + ve.skyType.overrideState
                    + " ambient=" + ve.skyAmbientMode.value);
            return ve.skyType.value == (int)skyType;
        }

        static void SkyPix(double dx, double dy, double dz, double sx, double sy, double sz,
                           bool sea, bool paintCore, double glowK, double glowW,
                           out double r, out double g, out double b)
        {
            double il = 1.0 / Math.Sqrt(dx * dx + dy * dy + dz * dz);
            dx *= il; dy *= il; dz *= il;
            double up = dy < 0 ? 0 : (dy > 1 ? 1 : dy);
            double t = Math.Pow(up, 0.42);
            r = 0.72 + (0.16 - 0.72) * t;
            g = 0.80 + (0.34 - 0.80) * t;
            b = 0.92 + (0.78 - 0.92) * t;
            double below = -dy * 2.2; below = below < 0 ? 0 : (below > 1 ? 1 : below);
            below = below * below * (3.0 - 2.0 * below);

            double br = sea ? 0.100 : 0.115, bg = sea ? 0.135 : 0.110, bb = sea ? 0.170 : 0.100;
            r += (br - r) * below; g += (bg - g) * below; b += (bb - b) * below;
            double cd = dx * sx + dy * sy + dz * sz;
            if (cd > 1) cd = 1; if (cd < -1) cd = -1;
            double ang = Math.Acos(cd) * (180.0 / Math.PI);
            double c1 = 1.0 - ang / 1.6; if (c1 < 0) c1 = 0; if (c1 > 1) c1 = 1;
            double coreV = Math.Sqrt(c1);
            double g1 = 1.0 - ang / (26.0 * glowW); if (g1 < 0) g1 = 0; if (g1 > 1) g1 = 1;
            double glow = Math.Pow(g1, 3.2 / Math.Max(glowW, 0.01));
            double add = (paintCore ? coreV * 55.0 : 0.0) + glow * 0.55 * glowK;
            r += add; g += 0.94 * add; b += 0.80 * add;

            double hp = sea ? dy + 0.30 : (dy > 0.16 ? dy : 0.16);
            double fb = FbmD(dx / hp * 1.15, dz / hp * 1.15);
            double cov = (fb - 0.02) * 2.3; if (cov < 0) cov = 0; if (cov > 1) cov = 1;
            double fade = sea ? (dy - 0.14) * 4.5 : (dy - 0.06) * 3.4;
            if (fade < 0) fade = 0; if (fade > 1) fade = 1;
            double cover = cov * fade * 0.92;
            if (cover > 0)
            {
                double lit = cd * 0.5 + 0.62; if (lit < 0) lit = 0; if (lit > 1.4) lit = 1.4;
                r += (0.95 * lit - r) * cover;
                g += (0.95 * lit - g) * cover;
                b += (0.97 * lit - b) * cover;
            }
        }

        static double FbmD(double x, double z)
        {
            double total = 0, amp = 0.5, freq = 1.6;
            for (int i = 0; i < 4; i++)
            {
                total += amp * Math.Sin(x * freq * 1.7 + Math.Cos(z * freq * 1.3) * 2.1)
                             * Math.Cos(z * freq * 1.9 + Math.Sin(x * freq * 1.1) * 1.7);
                amp *= 0.5; freq *= 2.17;
            }
            return total;
        }

        static Vector3 FaceDir(int face, float u, float v)
        {
            switch (face)
            {
                case 0: return new Vector3(1f, v, -u);
                case 1: return new Vector3(-1f, v, u);
                case 2: return new Vector3(u, 1f, -v);
                case 3: return new Vector3(u, -1f, v);
                case 4: return new Vector3(u, v, 1f);
                default: return new Vector3(-u, v, -1f);
            }
        }

        static float Fbm(float x, float z)
        {
            float total = 0f, amp = 0.5f, freq = 1.6f;
            for (int i = 0; i < 4; i++)
            {
                total += amp * Mathf.Sin(x * freq * 1.7f + Mathf.Cos(z * freq * 1.3f) * 2.1f)
                             * Mathf.Cos(z * freq * 1.9f + Mathf.Sin(x * freq * 1.1f) * 1.7f);
                amp *= 0.5f; freq *= 2.17f;
            }
            return total;
        }

        static Color SkyColor(Vector3 d, Vector3 sun)
        {
            d = d.normalized;
            float up = Mathf.Clamp01(d.y);
            float t = Mathf.Pow(up, 0.42f);

            float r = Mathf.Lerp(0.72f, 0.16f, t);
            float g = Mathf.Lerp(0.80f, 0.34f, t);
            float b = Mathf.Lerp(0.92f, 0.78f, t);

            float below = Mathf.Clamp01(-d.y * 2.2f);
            below = below * below * (3f - 2f * below);

            r = Mathf.Lerp(r, 0.115f, below);
            g = Mathf.Lerp(g, 0.110f, below);
            b = Mathf.Lerp(b, 0.100f, below);

            float cd = Vector3.Dot(d, sun);
            float ang = Mathf.Acos(Mathf.Clamp(cd, -1f, 1f)) * Mathf.Rad2Deg;
            float core = Mathf.Sqrt(Mathf.Clamp01(1f - ang / 1.6f));

            float glow = Mathf.Pow(Mathf.Clamp01(1f - ang / (26f * SunGlowWidth)),
                                   3.2f / Mathf.Max(SunGlowWidth, 0.01f));
            float add = (PaintSunCore ? core * 55f : 0f) + glow * 0.55f * SunGlow;
            r += 1.00f * add; g += 0.94f * add; b += 0.80f * add;

            float hp = Mathf.Max(d.y, 0.16f);
            float cover = Mathf.Clamp01((Fbm(d.x / hp * 1.15f, d.z / hp * 1.15f) - 0.02f) * 2.3f)
                        * Mathf.Clamp01((d.y - 0.06f) * 3.4f) * 0.92f;
            if (cover > 0f)
            {
                float lit = Mathf.Clamp(cd * 0.5f + 0.62f, 0f, 1.4f);
                r = Mathf.Lerp(r, 0.95f * lit, cover);
                g = Mathf.Lerp(g, 0.95f * lit, cover);
                b = Mathf.Lerp(b, 0.97f * lit, cover);
            }
            return new Color(r, g, b, 1f);
        }

        static int CubeSize
        {
            get
            {
                int n = 0;
                try { n = MapIdentity.SkyCubeSize; } catch { }
                return n >= 128 ? Mathf.Min(n, 2048) : 256;
            }
        }

        internal static bool FlipV = true;

        static Cubemap BuildSkyCubemap(string rigName)
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();

                Vector3 sun = -(Quaternion.Euler(SunElevation, SunAzimuth, 0f) * Vector3.forward);

                int N = CubeSize;
                var cube = new Cubemap(N, TextureFormat.RGBAHalf, false);

                bool sea = false;
                try { sea = MapIdentity.SeaSky; } catch { }
                bool flip = FlipV, core = PaintSunCore;
                double glowK = SunGlow, glowW = SunGlowWidth;
                double sx = sun.x, sy = sun.y, sz = sun.z;
                var px = new float[N * N * 4];
                long paintMs = 0, uploadMs = 0; bool layoutOk = true;
                for (int f = 0; f < 6; f++)
                {
                    var t0 = System.Diagnostics.Stopwatch.StartNew();
                    int face = f;
                    System.Threading.Tasks.Parallel.For(0, N, y =>
                    {
                        double t = ((y + 0.5) / N) * 2.0 - 1.0;
                        double v = flip ? -t : t;
                        int row = y * N;
                        for (int x = 0; x < N; x++)
                        {
                            double u = ((x + 0.5) / N) * 2.0 - 1.0;
                            double dx, dy, dz;
                            switch (face)
                            {
                                case 0: dx = 1; dy = v; dz = -u; break;
                                case 1: dx = -1; dy = v; dz = u; break;
                                case 2: dx = u; dy = 1; dz = -v; break;
                                case 3: dx = u; dy = -1; dz = v; break;
                                case 4: dx = u; dy = v; dz = 1; break;
                                default: dx = -u; dy = v; dz = -1; break;
                            }
                            double r, g, b;
                            SkyPix(dx, dy, dz, sx, sy, sz, sea, core, glowK, glowW, out r, out g, out b);
                            int o = (row + x) * 4;
                            px[o] = (float)r; px[o + 1] = (float)g; px[o + 2] = (float)b; px[o + 3] = 1f;
                        }
                    });
                    paintMs += t0.ElapsedMilliseconds;
                    var t1 = System.Diagnostics.Stopwatch.StartNew();
                    var arr = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Color>((long)(N * N));
                    bool ok = false;
                    try
                    {
                        var dst = IntPtr.Add(arr.Pointer, 4 * IntPtr.Size);
                        System.Runtime.InteropServices.Marshal.Copy(px, 0, dst, px.Length);
                        Color c0 = arr[0], cl = arr[N * N - 1];
                        int ol = (N * N - 1) * 4;
                        ok = Mathf.Abs(c0.r - px[0]) < 1e-6f && Mathf.Abs(c0.b - px[2]) < 1e-6f
                          && Mathf.Abs(cl.g - px[ol + 1]) < 1e-6f && Mathf.Abs(cl.a - 1f) < 1e-6f;
                    }
                    catch { ok = false; }
                    if (!ok)
                    {
                        layoutOk = false;
                        for (int i = 0; i < N * N; i++)
                        {
                            int o = i * 4;
                            arr[i] = new Color(px[o], px[o + 1], px[o + 2], 1f);
                        }
                    }
                    cube.SetPixels(arr, (CubemapFace)f, 0);
                    uploadMs += t1.ElapsedMilliseconds;
                }
                cube.Apply(false, false);
                _skyCube = cube;
                _log?.Msg("[sky] " + rigName + ": built our own " + N + "px sky cubemap in "
                        + sw.ElapsedMilliseconds + " ms (paint " + paintMs + " ms on worker threads, "
                        + "upload " + uploadMs + " ms" + (layoutOk ? ", block copy" : ", element-wise: "
                        + "the il2cpp array header was not 4 pointers here") + "), rows "
                        + (FlipV ? "top-down" : "bottom-up") + " -- blue gradient, a sun disc at "
                        + SunElevation.ToString("F0") + " deg / az " + SunAzimuth.ToString("F0")
                        + " (the same direction the shadows fall), and broken cloud. "
                        + "Nothing on this host had an HDRI to borrow, so there is nothing "
                        + "here that depends on the host level at all.");
                return cube;
            }
            catch (Exception e)
            {
                _log?.Warning("[sky] " + rigName + ": could not build the sky cubemap ("
                            + e.Message + ") -- day falls back to the gradient, which is "
                            + "plainer but is PROVEN to render on this build");
                return null;
            }
        }

        internal static bool UseHdSky = true;

        const string HdSkyFile = "sky.bin";

        static float[][] _hdFaces;
        static int _hdSize;
        static string _hdDir;
        static bool _hdActive;

        static float HalfToFloat(ushort h)
        {
            int sign = (h >> 15) & 1;
            int exp = (h >> 10) & 0x1F;
            int man = h & 0x3FF;
            float f;
            if (exp == 0) f = man * (1f / 16777216f);
            else if (exp == 31) f = man == 0 ? float.PositiveInfinity : float.NaN;
            else
            {

                f = (1f + man * (1f / 1024f));
                int e = exp - 15;
                while (e > 0) { f *= 2f; e--; }
                while (e < 0) { f *= 0.5f; e++; }
            }
            return sign == 1 ? -f : f;
        }

        static bool EnsureHdFaces(string rigName)
        {
            string dir = MapRuntime.BundleDir;
            if (_hdFaces != null && string.Equals(_hdDir, dir, StringComparison.OrdinalIgnoreCase))
                return true;
            _hdFaces = null;
            _hdDir = dir;
            string path;
            try
            {
                path = System.IO.Path.Combine(dir ?? "", HdSkyFile);
                if (!System.IO.File.Exists(path)) return false;
            }
            catch { return false; }

            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                using (var fs = System.IO.File.OpenRead(path))
                using (var r = new System.IO.BinaryReader(fs))
                {
                    if (new string(r.ReadChars(4)) != "MSK1")
                    { _log?.Warning("[sky] " + HdSkyFile + ": bad magic"); return false; }
                    uint version = r.ReadUInt32();
                    int size = (int)r.ReadUInt32();
                    uint channels = r.ReadUInt32();
                    r.ReadSingle();

                    if (version != 1 || channels != 3 || size < 16 || size > 4096)
                    {
                        _log?.Warning("[sky] " + HdSkyFile + ": unsupported layout (v"
                                    + version + ", " + size + "px, " + channels + "ch)");
                        return false;
                    }

                    var faces = new float[6][];
                    int floatsPerFace = size * size * 3;
                    var half = new byte[floatsPerFace * 2];
                    for (int f = 0; f < 6; f++)
                    {
                        int clen = (int)r.ReadUInt32();
                        if (clen <= 0 || clen > 64 * 1024 * 1024)
                        { _log?.Warning("[sky] " + HdSkyFile + ": absurd face length " + clen); return false; }
                        var comp = r.ReadBytes(clen);

                        using (var ms = new System.IO.MemoryStream(comp))
                        using (var ds = new System.IO.Compression.DeflateStream(
                                   ms, System.IO.Compression.CompressionMode.Decompress))
                        {
                            int got = 0;
                            while (got < half.Length)
                            {
                                int n = ds.Read(half, got, half.Length - got);
                                if (n <= 0) break;
                                got += n;
                            }
                            if (got != half.Length)
                            { _log?.Warning("[sky] " + HdSkyFile + ": face " + f + " short ("
                                          + got + " of " + half.Length + " bytes)"); return false; }
                        }
                        var fd = new float[floatsPerFace];
                        for (int i = 0; i < floatsPerFace; i++)
                            fd[i] = HalfToFloat((ushort)(half[i * 2] | (half[i * 2 + 1] << 8)));
                        faces[f] = fd;
                    }
                    _hdFaces = faces;
                    _hdSize = size;
                    _log?.Msg("[sky] " + rigName + ": decoded " + HdSkyFile + " ("
                            + size + "px x 6 faces, " + sw.ElapsedMilliseconds + " ms). The "
                            + "decoded floats stay cached, so the next mission skips this.");
                }
                return true;
            }
            catch (Exception e)
            {
                _log?.Warning("[sky] " + HdSkyFile + ": " + e.Message
                            + " -- falling back to the generated sky");
                _hdFaces = null;
                return false;
            }
        }

        static Cubemap LoadHdSkyCubemap(string rigName)
        {
            try
            {
                if (!EnsureHdFaces(rigName)) return null;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                int N = _hdSize;

                Vector3 sun = -(Quaternion.Euler(SunElevation, SunAzimuth, 0f) * Vector3.forward);

                var cube = new Cubemap(N, TextureFormat.RGBAHalf, false);
                var px = new Color[N * N];
                for (int f = 0; f < 6; f++)
                {
                    var fd = _hdFaces[f];
                    for (int y = 0; y < N; y++)
                    {

                        float t = ((y + 0.5f) / N) * 2f - 1f;
                        float v = -t;
                        int row = y * N;
                        for (int x = 0; x < N; x++)
                        {
                            float u = ((x + 0.5f) / N) * 2f - 1f;
                            int o = (row + x) * 3;
                            float r = fd[o], g = fd[o + 1], b = fd[o + 2];

                            Vector3 d = FaceDir(f, u, v).normalized;
                            float cd = Vector3.Dot(d, sun);

                            float cosLimit = Mathf.Cos(Mathf.Deg2Rad
                                           * Mathf.Min(89f, 30f * SunGlowWidth));
                            if (cd > cosLimit)
                            {
                                float ang = Mathf.Acos(Mathf.Clamp(cd, -1f, 1f)) * Mathf.Rad2Deg;
                                float core = Mathf.Sqrt(Mathf.Clamp01(1f - ang / 1.6f));
                                float glow = Mathf.Pow(
                                    Mathf.Clamp01(1f - ang / (26f * SunGlowWidth)),
                                    3.2f / Mathf.Max(SunGlowWidth, 0.01f));
                                float add = (PaintSunCore ? core * 55f : 0f)
                                          + glow * 0.55f * SunGlow;
                                r += 1.00f * add; g += 0.94f * add; b += 0.80f * add;
                            }
                            px[row + x] = new Color(r, g, b, 1f);
                        }
                    }
                    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Color> arr = px;
                    cube.SetPixels(arr, (CubemapFace)f, 0);
                }
                cube.Apply(false, false);
                _skyCube = cube;
                _log?.Msg("[sky] " + rigName + ": photographic sky bound (" + N + "px, sun "
                        + "disc painted at " + SunElevation.ToString("F0") + " deg / az "
                        + SunAzimuth.ToString("F0") + ", " + sw.ElapsedMilliseconds + " ms). "
                        + "'odg sky hd off' compares the generated dome live.");
                return cube;
            }
            catch (Exception e)
            {
                _log?.Warning("[sky] photographic sky: " + e.Message
                            + " -- the generated dome takes over");
                return null;
            }
        }

        internal static string SetHdSky(bool on)
        {
            UseHdSky = on;
            if (_dayHdri == null)
                return "sky source = " + (on ? "sky.bin" : "generated")
                     + " (stored; there is no live HDRI sky to rebind)";
            Cubemap cube = null;
            if (on) cube = LoadHdSkyCubemap("HD-AB");
            _hdActive = on && cube != null;
            if (cube == null)
            {
                if (on) UseHdSky = false;
                cube = BuildSkyCubemap("HD-AB");
            }
            if (cube == null) return "no cubemap could be built; the old sky is still up.";
            try { _dayHdri.hdriSky.overrideState = true; _dayHdri.hdriSky.value = cube; }
            catch (Exception e) { return "rebuilt but could not bind it: " + e.Message; }
            return "day sky = " + (_hdActive ? "sky.bin photographic (1024px)"
                                            : "generated dome (256px)") + ", live.";
        }

        static void AddPbsSky(VolumeProfile profile, string rigName)
        {
            var p = AddOverride<PhysicallyBasedSky>(profile, "PhysicallyBasedSky", rigName);
            if (p == null) return;
            _dayPbs = p;
            Set(() => { p.exposure.overrideState = true; p.exposure.value = 0f; }, "pbs.exposure");
            Set(() => { p.aerosolDensity.overrideState = true; p.aerosolDensity.value = 0.125f; }, "pbs.aerosolDensity");
            Set(() => { p.aerosolAnisotropy.overrideState = true; p.aerosolAnisotropy.value = 0.65f; }, "pbs.aerosolAnisotropy");
            Set(() => { p.colorSaturation.overrideState = true; p.colorSaturation.value = 0.684f; }, "pbs.colorSaturation");
            Set(() => { p.alphaSaturation.overrideState = true; p.alphaSaturation.value = 0.825f; }, "pbs.alphaSaturation");
            Set(() => { p.alphaMultiplier.overrideState = true; p.alphaMultiplier.value = 1f; }, "pbs.alphaMultiplier");
            Set(() => { p.groundTint.overrideState = true; p.groundTint.value = Color.black; }, "pbs.groundTint");

            Set(() =>
            {
                if (_dayEnv != null)
                {
                    _dayEnv.renderingSpace.overrideState = true;
                    _dayEnv.renderingSpace.value = (RenderingSpace)1;
                }
            }, "pbs.renderingSpace");
            _dayDome = "PBS (afghan config)";
            _log?.Msg("[sky] " + rigName + ": PhysicallyBasedSky configured from the decoded "
                    + "Afghan day stack (aerosol 0.125, saturation 0.684, camera-space). If "
                    + "it renders black the probe watchdog swaps to the gradient by itself.");
        }

        static void AddHdriSky(VolumeProfile profile, string rigName, Cubemap cube)
        {
            var h = AddOverride<HDRISky>(profile, "HDRISky", rigName);
            if (h == null) return;
            _dayHdri = h;

            Set(() => { h.hdriSky.overrideState = true; h.hdriSky.value = cube; }, "hdriSky");
            Set(() => { h.rotation.overrideState = true; h.rotation.value = 0f; }, "hdri.rotation");

            Set(() => { h.skyIntensityMode.overrideState = true;
                        h.skyIntensityMode.value = SkyIntensityMode.Exposure; }, "hdri.mode");
            Set(() => { h.exposure.overrideState = true;
                        h.exposure.value = DomeExposure + DomeTrimEV; }, "hdri.exposure");

            _dayDome = (_hdActive ? "HDRI (sky.bin 1024)" : "HDRI (generated 256)")
                     + " exp=" + (DomeExposure + DomeTrimEV).ToString("F2");
            _log?.Msg("[sky] " + rigName + ": day sky = " + _dayDome
                    + "  -- at this exposure the dome puts ~12,000 lux on an up-facing "
                    + "surface, which is the fill that stops shadows being black. "
                    + "If the frame is black, HDRISky is as dead as PBS was: type "
                    + "'odg sky mode gradient'.");
        }

        internal static void AddIndirect(VolumeProfile profile, string rigName)
        {
            var c = AddOverride<IndirectLightingController>(profile,
                        "IndirectLightingController", rigName);
            if (c == null) return;
            _indirect = c;
            Set(() => { c.indirectDiffuseLightingMultiplier.overrideState = true;
                        c.indirectDiffuseLightingMultiplier.value = SkyFill; }, "indirect.diffuse");
            Set(() => { c.reflectionLightingMultiplier.overrideState = true;
                        c.reflectionLightingMultiplier.value = 1f; }, "indirect.reflection");
            _log?.Msg("[sky] " + rigName + ": indirect diffuse owned at " + SkyFill
                    + "x (the host's volumes can no longer scale the sky fill). This is the "
                    + "dial for 'no light where the sun does not reach'; 'odg sky fill <x>' "
                    + "moves it live without touching the sky or the sunlit faces.");
        }

        internal static void Tick()
        {
            if (_probeCheckAt < 0f || Time.time < _probeCheckAt) return;
            _probeCheckAt = Time.time + 3f;
            _probeChecks++;
            try
            {
                var sh = RenderSettings.ambientProbe;
                float r = sh[0, 0], g = sh[1, 0], b = sh[2, 0];
                float lum = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                _log?.Msg("[sky] ambient probe DC = " + r.ToString("F3") + ", "
                        + g.ToString("F3") + ", " + b.ToString("F3")
                        + "  (luminance " + lum.ToString("F3") + "; this is the sky fill "
                        + "every shadowed surface is actually receiving)");

                try
                {
                    var vols = UnityEngine.Object.FindObjectsOfType<
                        UnityEngine.Rendering.Volume>();
                    var sb = new System.Text.StringBuilder("[sky] exposure contest:");
                    int found = 0;
                    for (int i = 0; i < (vols != null ? vols.Length : 0); i++)
                    {
                        var v = vols[i];
                        if (v == null || !v.enabled || v.profile == null) continue;
                        UnityEngine.Rendering.HighDefinition.Exposure ex = null;
                        try
                        {
                            if (!v.profile.TryGet(out ex) || ex == null || !ex.active)
                                continue;
                        }
                        catch { continue; }
                        found++;
                        string mode = "?", ev = "?";
                        try { mode = ex.mode.value.ToString(); } catch { }
                        try { ev = ex.fixedExposure.value.ToString("F2"); } catch { }
                        sb.Append("  '" + v.gameObject.name + "' prio=" + v.priority
                                + (v.isGlobal ? " global" : " LOCAL")
                                + " mode=" + mode + " fixedEV=" + ev + ";");
                    }
                    if (found > 0) _log?.Msg(sb.ToString());
                }
                catch { }
                if (lum > 0.0005f) { _probeCheckAt = -1f; return; }

                if (_probeChecks >= 3)
                {
                    _probeCheckAt = -1f;
                    if (_dayPbs != null && _dayEnv != null)
                    {

                        _log?.Warning("[sky] the ambient probe has read BLACK three times "
                                    + "under the PBS trial. Recording the answer and "
                                    + "switching day to the GradientSky.");
                        try
                        {
                            _dayEnv.skyType.overrideState = true;
                            _dayEnv.skyType.value = (int)SkyType.Gradient;
                        }
                        catch (Exception se) { _log?.Warning("[sky] pbs fallback: " + se.Message); }
                        _dayDome = "PBS FAILED -> gradient";
                    }
                    else if (_dayHdri != null && _dayEnv != null && UseHdriDay)
                    {
                        _log?.Warning("[sky] the ambient probe has read BLACK three times. "
                                    + "Either HDRISky does not render on this build or HDRP "
                                    + "is not publishing the probe. Switching day to the "
                                    + "GradientSky, which is proven to render -- plainer, "
                                    + "but never black. 'odg sky mode hdri' forces it back.");
                        SetSkyMode(false);
                    }
                    else
                    {
                        _log?.Warning("[sky] the ambient probe reads black and day is already "
                                    + "on the gradient. Shadow fill is coming from nothing but "
                                    + "the sun; raise 'odg sky dome' or lower 'odg sky ev'.");
                    }
                }
            }
            catch (Exception e)
            {
                _probeCheckAt = -1f;
                _log?.Msg("[sky] ambient probe is not readable on this build (" + e.Message
                        + "); the sky cannot be verified from in-process. Judge it on screen.");
            }
        }

        internal static void AddHostLitSky(VolumeProfile profile, string rigName)
        {
            ApplySkyLook();
            PaintSunCore = false;
            if (profile == null) return;
            try
            {

                var cube = UseHdSky ? LoadHdSkyCubemap(rigName) : null;
                _hdActive = cube != null;
                if (cube == null) cube = BuildSkyCubemap(rigName);
                if (cube == null)
                {
                    _log?.Warning("[sky] " + rigName + ": no cubemap, so the host's own sky "
                                + "stays up.");
                    return;
                }
                if (!AddVisualEnvironment(profile, SkyType.HDRI, rigName)) return;
                AddDayGradient(profile, rigName);
                AddHdriSky(profile, rigName, cube);

                bool seaSky = false;
                try { seaSky = MapIdentity.SeaSky; } catch { }
                if (Clouds && !_hdActive && !seaSky) AddClouds(profile, rigName);
                else if (seaSky) _log?.Msg("[sky] " + rigName + ": volumetric clouds skipped over "
                                         + "open water (streaking at the horizon); the painted "
                                         + "clouds in the cubemap are the clouds");
                _log?.Msg("[sky] " + rigName + ": OUR sky over the host's light. The host keeps "
                        + "its sun, its shadows and its ambient; the backdrop and its blue "
                        + "bounce are ours, and the painted disc sits exactly where the host "
                        + "sun was re-aimed to.");
            }
            catch (Exception e) { _log?.Warning("[sky] AddHostLitSky: " + e.Message); }
        }

        internal static string FlipSky()
        {
            FlipV = !FlipV;
            if (_dayHdri == null)
                return "sky rows = " + (FlipV ? "top-down" : "bottom-up")
                     + " (stored; there is no live HDRI sky to rebake)";
            var cube = BuildSkyCubemap("FLIP");
            if (cube == null) return "the rebake failed; the old sky is still up.";
            try { _dayHdri.hdriSky.overrideState = true; _dayHdri.hdriSky.value = cube; }
            catch (Exception e) { return "rebaked but could not bind it: " + e.Message; }
            return "sky rebaked with rows " + (FlipV ? "top-down" : "bottom-up")
                 + ". If the grey walls are gone, this is the right way up.";
        }

        static readonly System.Collections.Generic.List<HDAdditionalLightData> _fills =
            new System.Collections.Generic.List<HDAdditionalLightData>();
        internal static void RegisterFill(HDAdditionalLightData hd)
        { if (hd != null) _fills.Add(hd); }

        internal static string SetAmbientLights(float lux)
        {
            FillLux = Mathf.Clamp(lux, 0f, 40000f);
            int moved = 0;
            for (int i = 0; i < _fills.Count; i++)
            {
                var hd = _fills[i];
                if (hd == null) continue;
                try { hd.SetIntensity(FillLux, LightUnit.Lux); moved++; } catch { }
            }
            return "day fill = " + FillLux.ToString("F0") + " lux x " + moved
                 + " light(s), live. These are plain shadowless lights, so this "
                 + "brightens what the sun misses whether or not the sky's ambient "
                 + "is reaching anything. 0 switches them off.";
        }

        internal static string SetFill(float x)
        {
            SkyFill = Mathf.Clamp(x, 0f, 12f);
            if (_indirect == null)
                return "sky fill = " + SkyFill.ToString("F1") + "x (stored; no live volume)";
            try
            {
                _indirect.indirectDiffuseLightingMultiplier.overrideState = true;
                _indirect.indirectDiffuseLightingMultiplier.value = SkyFill;
            }
            catch (Exception e) { return "could not set it: " + e.Message; }
            return "sky fill = " + SkyFill.ToString("F1") + "x, live. This lifts ONLY the "
                 + "surfaces the sun misses; sunlit faces and the sky do not move.";
        }

        internal static string SetSkyMode(bool hdri)
        {
            UseHdriDay = hdri;
            if (_dayEnv == null)
                return "sky mode = " + (hdri ? "hdri" : "gradient")
                     + " (stored; takes effect on the next mission)";
            if (hdri && _dayHdri == null)
                return "there is no HDRI sky in this rig to switch to -- the cubemap "
                     + "failed to build. The gradient is what you have.";
            try
            {
                _dayEnv.skyType.overrideState = true;
                _dayEnv.skyType.value = (int)(hdri ? SkyType.HDRI : SkyType.Gradient);
            }
            catch (Exception e) { return "could not switch: " + e.Message; }
            _probeCheckAt = -1f;
            return "sky = " + (hdri ? "our cubemap (blue, sun, cloud)" : "flat gradient")
                 + ", live. Ambient follows it, so the shadows change with it.";
        }

        static void AddDayGradient(VolumeProfile profile, string rigName)
        {
            var g = AddOverride<GradientSky>(profile, "GradientSky", rigName);
            if (g == null) return;
            _dayGrad = g;
            Set(() => { g.top.overrideState = true; g.top.value = new Color(0.17f, 0.35f, 0.81f); }, "day.top");
            Set(() => { g.middle.overrideState = true; g.middle.value = new Color(0.55f, 0.68f, 0.88f); }, "day.middle");
            Set(() => { g.bottom.overrideState = true; g.bottom.value = new Color(0.28f, 0.27f, 0.25f); }, "day.bottom");
            Set(() => { g.gradientDiffusion.overrideState = true; g.gradientDiffusion.value = 1f; }, "day.diffusion");

            Set(() => { g.exposure.overrideState = true;
                        g.exposure.value = DomeExposure + 1.1f + DomeTrimEV; }, "day.exposure");
            _log?.Msg("[sky] " + rigName + ": gradient sky resident at exp="
                    + (DomeExposure + 1.1f + DomeTrimEV).ToString("F2")
                    + " (the live fallback; 'odg sky mode gradient' selects it)");
        }

        static void AddClouds(VolumeProfile profile, string rigName)
        {
            var c = AddOverride<VolumetricClouds>(profile, "VolumetricClouds", rigName);
            if (c == null) return;
            _dayClouds = c;
            Set(() => { c.enable.overrideState = true; c.enable.value = true; }, "clouds.enable");

            Set(() => { c.cloudControl.overrideState = true; c.cloudControl.value = VolumetricClouds.CloudControl.Simple; }, "cloudControl");

            Set(() => { c.m_CloudPreset.overrideState = true; }, "cloudPreset.override");
            Set(() => { c.cloudPreset = VolumetricClouds.CloudPresets.Sparse; }, "cloudPreset");
            Set(() => { c.ApplyCurrentCloudPreset(); }, "ApplyCurrentCloudPreset");
            Set(() => { c.cloudSimpleMode.overrideState = true; c.cloudSimpleMode.value = VolumetricClouds.CloudSimpleMode.Performance; }, "cloudSimpleMode");
            _log?.Msg("[sky] " + rigName + ": volumetric clouds enable=" + c.enable.value
                    + " control=" + c.cloudControl.value + " preset=" + c.cloudPreset
                    + " steps=" + c.cloudSimpleMode.value);
        }

        static Bloom _dayBloom;
        static void AddBloom(VolumeProfile profile, string rigName)
        {
            var b = AddOverride<Bloom>(profile, "Bloom", rigName);
            if (b == null) return;
            _dayBloom = b;
            Set(() => { b.intensity.overrideState = true; b.intensity.value = BloomIntensity; }, "bloom.intensity");
            _log?.Msg("[sky] " + rigName + ": bloom owned at " + BloomIntensity
                    + " (the host's own bloom no longer applies to our frame)");
        }

        static void AddGradientSky(VolumeProfile profile, string rigName)
        {
            var g = AddOverride<GradientSky>(profile, "GradientSky", rigName);
            if (g == null) return;
            _nightSky = g;

            Set(() => { g.top.overrideState = true; g.top.value = new Color(0.012f, 0.017f, 0.038f); }, "top");
            Set(() => { g.middle.overrideState = true; g.middle.value = new Color(0.020f, 0.026f, 0.050f); }, "middle");
            Set(() => { g.bottom.overrideState = true; g.bottom.value = new Color(0.028f, 0.031f, 0.044f); }, "bottom");
            Set(() => { g.gradientDiffusion.overrideState = true; g.gradientDiffusion.value = 1.6f; }, "gradientDiffusion");
            _log?.Msg("[sky] " + rigName + ": GradientSky (night) top=" + g.top.value);
        }

        static void Set(Action a, string what)
        {
            try { a(); }
            catch (Exception e) { _log?.Warning("[sky] " + what + ": " + e.Message); }
        }

        internal static string Describe()
        {
            return "sky=" + (MapIdentity.OwnSky ? "ours" : "host (Farmstead keeps the tuned rig)")
                 + " sun=" + SunLux.ToString("F0") + " lux @ " + SunElevation.ToString("F0")
                 + " deg az " + SunAzimuth.ToString("F0") + " / " + SunKelvin.ToString("F0") + " K"
                 + " dayEV=" + CityDayEV.ToString("F2")
                 + " bloom=" + BloomIntensity.ToString("F2")
                 + " clouds=" + (Clouds ? "on" : "off")
                 + " nightsky=" + (NightSky ? "ours" : "host")
                 + " dome=" + _dayDome
                 + (_sunHd == null ? " [no live sun]" : "");
        }

        internal static string SetSunLux(float lux)
        {
            SunLux = Mathf.Clamp(lux, 200f, 130000f);
            if (_sunHd != null)
                try { _sunHd.SetIntensity(SunLux, LightUnit.Lux); } catch { }
            return "sun = " + SunLux.ToString("F0") + " lux"
                 + (_sunHd == null ? " (stored; no live sun to move)" : " (live)");
        }

        internal static string SetSunAngle(float elevation, float azimuth)
        {

            SunElevation = Mathf.Clamp(elevation, 3f, 90f);
            SunAzimuth = Mathf.Repeat(azimuth, 360f);
            if (_sunHd != null)
                try
                {
                    _sunHd.transform.rotation =
                        Quaternion.Euler(SunElevation, SunAzimuth, 0f);
                }
                catch { }
            return "sun angle = " + SunElevation.ToString("F0") + " deg elevation, azimuth "
                 + SunAzimuth.ToString("F0") + (_sunHd == null ? " (stored)" : " (live)");
        }

        internal static string SetBloom(float v)
        {
            BloomIntensity = Mathf.Clamp(v, 0f, 1f);
            if (_dayBloom != null)
                try { _dayBloom.intensity.overrideState = true; _dayBloom.intensity.value = BloomIntensity; }
                catch { }
            return "bloom = " + BloomIntensity.ToString("F2")
                 + (_dayBloom == null ? " (stored; no live volume)" : " (live)");
        }

        internal static string SetDomeTrim(float ev)
        {
            float old = DomeTrimEV;
            DomeTrimEV = Mathf.Clamp(ev, -6f, 6f);
            float delta = DomeTrimEV - old;
            bool live = false;
            if (_dayHdri != null)
                try { _dayHdri.exposure.value = _dayHdri.exposure.value + delta; live = true; } catch { }
            if (_dayGrad != null)
                try { _dayGrad.exposure.value = _dayGrad.exposure.value + delta; live = true; } catch { }
            return "sky dome trim = " + DomeTrimEV.ToString("F2") + " EV"
                 + (live ? " (live)" : " (stored; takes effect on the next build)");
        }

        internal static string SetClouds(bool on)
        {
            Clouds = on;
            if (_dayClouds != null)
                try { _dayClouds.enable.overrideState = true; _dayClouds.enable.value = on; }
                catch { }
            return "clouds = " + (on ? "on" : "off")
                 + (_dayClouds == null ? " (stored; takes effect on the next build)" : " (live)");
        }

        internal static string SetNightSky(bool on)
        {
            NightSky = on;
            return "night sky = " + (on ? "ours" : "the host level's")
                 + ". The night rig's profile is built once, so this takes effect "
                 + "on the next mission start, not now.";
        }
    }
}
