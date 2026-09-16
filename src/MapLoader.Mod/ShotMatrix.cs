// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using MelonLoader;
using UnityEngine;
using HD = UnityEngine.Rendering.HighDefinition;

namespace MapLoader
{

    public static class ShotMatrix
    {
        static MelonLogger.Instance _log;

        static MelonPreferences_Entry<bool> _prefOn;
        static MelonPreferences_Entry<string> _prefVariants;
        static MelonPreferences_Entry<int> _prefQuitSecs;
        static MelonPreferences_Entry<string> _prefOutDir;
        static MelonPreferences_Category _cat;

        public static void Init(MelonLogger.Instance log)
        {

            try { Application.runInBackground = true; } catch { }

            _log = log;
            try
            {
                _cat = MelonPreferences.GetCategory("MapLoader")
                    ?? MelonPreferences.CreateCategory("MapLoader");
                _prefOn = _cat.GetEntry<bool>("ShotMatrix")
                       ?? _cat.CreateEntry("ShotMatrix", false,
                            description: "Run the unattended lighting screenshot matrix on the next "
                                       + "boot's menu, then quit. The harness flips this back to "
                                       + "false the moment it starts, so every run has to be armed "
                                       + "on purpose (the driver script does it).");
                _prefVariants = _cat.GetEntry<string>("ShotVariants")
                             ?? _cat.CreateEntry("ShotVariants", "A,B,C,D,E,F,G,H,I",
                                  description: "Which lighting variants the matrix shoots, in order. "
                                             + "A host-lit control, B +apv-off, C +no fill, D own rig, "
                                             + "E own rig apv-off, F +ssgi, G E without ambient "
                                             + "shaping, H E with probes off, I E with the PBS sky.");
                _prefQuitSecs = _cat.GetEntry<int>("ShotQuitSecs")
                             ?? _cat.CreateEntry("ShotQuitSecs", 420,
                                  description: "Watchdog for the harness run: however wedged it gets, "
                                             + "the game quits this many seconds after the run "
                                             + "starts, with a hard process kill five seconds later.");
                _prefOutDir = _cat.GetEntry<string>("ShotOutDir")
                           ?? _cat.CreateEntry("ShotOutDir", @"D:\OperatorHSR\tools\shots\out",
                                description: "Where the harness writes its run folders "
                                           + "(<stamp>\\<variant>_<view>.png plus manifest.json "
                                           + "and done.json).");
            }
            catch (Exception e) { _log?.Warning("[shot] prefs: " + e.Message); }
        }

        static bool _wantRun;

        static bool _started;
        static bool _running;

        public static bool Running => _running;

        public static void OnSceneInitialized(string sceneName)
        {
            if (_started || _running) return;
            if (_prefOn == null || !_prefOn.Value) return;
            if (!LooksLikeBootMenu(sceneName)) return;
            _started = true;
            _log?.Msg("[shot] ShotMatrix pref is set and the menu is up -- starting the "
                    + "screenshot matrix. This session belongs to the harness now and "
                    + "ends in a quit.");
            try { MelonCoroutines.Start(Run(null)); }
            catch (Exception e) { _log?.Error("[shot] start: " + e.Message); }
        }

        public static void Tick()
        {

            if (_wantRun || _started)
            { try { Application.runInBackground = true; } catch { } }

            string req = MapRuntime.HarnessRunRequest;
            if (req == null) return;
            MapRuntime.HarnessRunRequest = null;

            if (_running) { _log?.Warning("[shot] a matrix run is already in flight; ignoring the new request."); return; }
            string scene = null;
            try { scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; }
            catch { }
            if (!LooksLikeBootMenu(scene))
            {
                _log?.Warning("[shot] 'odg shot run' only works from the main menu -- the "
                            + "harness builds the city into the menu scene and quits when "
                            + "it is done, neither of which belongs in a mission. Scene "
                            + "here is '" + (scene ?? "?") + "'.");
                return;
            }
            _started = true;
            _log?.Msg("[shot] console request accepted (" + req + "). The run ends in a "
                    + "quit -- that is the harness contract, not an accident.");
            try { MelonCoroutines.Start(Run(req == "*" ? null : req)); }
            catch (Exception e) { _log?.Error("[shot] start: " + e.Message); }
        }

        static bool LooksLikeBootMenu(string n)
        {
            if (string.IsNullOrEmpty(n)) return false;
            return n.ToLowerInvariant().Contains("menu");
        }

        public static void Forget()
        {
            _shotGo = null;
            _shotCam = null;
            _shotHd = null;
            _sunGo = null;
            _canvases.Clear();
            _cameras.Clear();
        }

        static GameObject _shotGo;
        static Camera _shotCam;
        static HD.HDAdditionalCameraData _shotHd;
        static GameObject _sunGo;

        static string _runDir;
        static float _quitAt;
        static readonly List<string> _errors = new List<string>();
        static readonly List<string> _variantJson = new List<string>();
        static readonly List<string> _shotJson = new List<string>();
        static int _shotCount;

        static int _captureMode;

        static readonly List<Canvas> _canvases = new List<Canvas>();
        static readonly List<Camera> _cameras = new List<Camera>();
        static float _rescanAt;

        static bool Expired() { return Time.realtimeSinceStartup > _quitAt; }

        static void Err(string what)
        {
            _errors.Add(what);
            _log?.Error("[shot] " + what);
        }

        static IEnumerator Run(string variantCsv)
        {
            _running = true;
            float startedAt = Time.realtimeSinceStartup;
            bool ok = false;

            try
            {

                try
                {
                    if (_prefOn != null && _prefOn.Value)
                    {
                        _wantRun = true;
                        _prefOn.Value = false;
                        try { _cat?.SaveToFile(false); } catch { try { MelonPreferences.Save(); } catch { } }
                        _log?.Msg("[shot] ShotMatrix pref flipped back to false and saved "
                                + "(crash-loop guard; the driver re-arms it per run).");
                    }
                }
                catch (Exception e) { _log?.Warning("[shot] pref flip: " + e.Message); }

                _errors.Clear();
                _variantJson.Clear();
                _shotJson.Clear();
                _shotCount = 0;
                _captureMode = 0;

                int quitSecs = 420;
                try { quitSecs = Mathf.Clamp(_prefQuitSecs != null ? _prefQuitSecs.Value : 420, 60, 3600); } catch { }
                _quitAt = startedAt + quitSecs;

                string outRoot = @"D:\OperatorHSR\tools\shots\out";
                try { if (_prefOutDir != null && !string.IsNullOrEmpty(_prefOutDir.Value)) outRoot = _prefOutDir.Value; } catch { }
                _runDir = Path.Combine(outRoot, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                try { Directory.CreateDirectory(_runDir); }
                catch (Exception e)
                {
                    Err("cannot create output dir " + _runDir + ": " + e.Message);
                    _runDir = null;
                    yield break;
                }
                _log?.Msg("[shot] output: " + _runDir + "  watchdog: " + quitSecs + " s");

                float settleUntil = Time.realtimeSinceStartup + 5f;
                while (Time.realtimeSinceStartup < settleUntil && !Expired()) yield return null;

                if (!MapIdentity.Select("PowderPalace"))
                {
                    Err("MapIdentity.Select(PowderPalace) refused -- map data missing?");
                    yield break;
                }

                List<View> cityViews = LoadViews(Path.Combine(MapRuntime.BundleDir, "shots.json"));
                List<View> farmViews = LoadViews(Path.Combine(MapRuntime.BundleDir, "shots_farmstead.json"));
                if (cityViews.Count == 0)
                {
                    Err("shots.json missing or parsed to zero views at " + MapRuntime.BundleDir);
                    yield break;
                }
                _log?.Msg("[shot] " + cityViews.Count + " city viewpoint(s), "
                        + farmViews.Count + " Farmstead control viewpoint(s)");

                MapRuntime.LightingParams baseline = MapRuntime.LightingParams.Defaults();
                baseline.hostLit = true;
                baseline.ssgi = false;
                baseline.apvOff = false;
                baseline.probeUsage = 1;

                MapRuntime.Init(_log);
                MapRuntime.HarnessBuild = true;
                bool begun = false;
                try { begun = MapRuntime.Begin(); }
                catch (Exception e) { Err("Begin threw: " + e.Message); }
                if (!begun) { Err("MapRuntime.Begin() returned false"); yield break; }

                float buildDeadline = Time.realtimeSinceStartup + 120f;
                while (!MapRuntime.Done && Time.realtimeSinceStartup < buildDeadline && !Expired())
                    yield return null;
                if (!MapRuntime.Done)
                {
                    Err("the city build did not finish inside 120 s (building="
                        + MapRuntime.Building + ")");
                    yield break;
                }
                _log?.Msg("[shot] city built in " + MapRuntime.BuildSeconds.ToString("F0") + " s");

                try { LoadVeil.Hide("harness run; there is no player to wait for"); } catch { }

                MakeHarnessSun();

                MakeShotCam();
                yield return null;

                string csv = variantCsv;
                if (string.IsNullOrEmpty(csv))
                {
                    try { csv = _prefVariants != null ? _prefVariants.Value : null; } catch { }
                    if (string.IsNullOrEmpty(csv)) csv = "A,B,C,D,E,F,G,H,I";
                }

                foreach (var rawV in csv.Split(','))
                {
                    string v = rawV.Trim().ToUpperInvariant();
                    if (v.Length == 0) continue;
                    if (Expired()) { Err("watchdog expired before variant " + v); break; }

                    MapRuntime.LightingParams p;
                    bool shapingOff = false;
                    if (!BuildVariantParams(v, baseline, out p, out shapingOff))
                    {
                        Err("unknown variant '" + v + "' skipped");
                        continue;
                    }

                    _log?.Msg("[shot] ==== variant " + v + " ====");
                    try { MapRuntime.RebuildLighting(p); }
                    catch (Exception e) { Err("variant " + v + " rebuild: " + e.Message); }

                    if (shapingOff)
                    { try { MapRuntime.SetAmbientShapingLive(false); } catch (Exception e) { Err("shaping off: " + e.Message); } }

                    bool invalid = false;
                    if (p.apvOff)
                    {
                        yield return null;
                        bool took = false;
                        try { took = ApvGate.Apply(_shotHd); }
                        catch (Exception e) { Err("variant " + v + " apv apply: " + e.Message); }
                        if (!took)
                        {
                            invalid = true;
                            Err("variant " + v + ": the APV-off write did not take; marking "
                              + "INVALID so a dark frame cannot be misattributed to lighting");
                        }
                    }

                    if (p.ssgi) TryForceFrameBit((uint)HD.FrameSettingsField.SSGI, true);

                    for (int f = 0; f < 120 && !Expired(); f++) { Suppress(); yield return null; }

                    foreach (var view in cityViews)
                    {
                        if (Expired()) break;
                        PlaceCam(view);
                        for (int f = 0; f < 40 && !Expired(); f++) { Suppress(); yield return null; }
                        string file = Path.Combine(_runDir, v + "_" + view.Name + ".png");
                        var cap = Capture(file);
                        while (cap.MoveNext()) yield return cap.Current;
                        RecordShot(v, view.Name, file);
                    }

                    RecordVariant(v, p, invalid, shapingOff);
                    WriteManifest(false);

                    if (shapingOff)
                    { try { MapRuntime.SetAmbientShapingLive(true); } catch { } }
                }

                if (farmViews.Count > 0 && !Expired())
                {

                    try { ShellSwap.SetHostLights(true, _log); } catch { }
                    try { if (_sunGo != null) UnityEngine.Object.Destroy(_sunGo); } catch { }
                    _sunGo = null;
                    try { if (_shotGo != null) UnityEngine.Object.Destroy(_shotGo); } catch { }
                    _shotGo = null; _shotCam = null; _shotHd = null;

                    try { MapRuntime.Teardown(); }
                    catch (Exception e) { Err("city teardown: " + e.Message); }

                    for (int f = 0; f < 300 && !Expired(); f++) yield return null;

                    if (!MapIdentity.Select("Farmstead"))
                    {
                        Err("Farmstead not installed on this machine; control cycle skipped");
                    }
                    else
                    {
                        MapRuntime.Init(_log);
                        MapRuntime.HarnessBuild = true;
                        bool fBegun = false;
                        try { fBegun = MapRuntime.Begin(); }
                        catch (Exception e) { Err("Farmstead Begin threw: " + e.Message); }
                        float fDeadline = Time.realtimeSinceStartup + 120f;
                        while (fBegun && !MapRuntime.Done
                               && Time.realtimeSinceStartup < fDeadline && !Expired())
                            yield return null;
                        if (!fBegun || !MapRuntime.Done)
                        {
                            Err("the Farmstead control build did not finish");
                        }
                        else
                        {
                            try { LoadVeil.Hide("harness control cycle"); } catch { }

                            try { SceneLighting.Apply(true); } catch (Exception e) { Err("farm night: " + e.Message); }

                            string farmDir = Path.Combine(_runDir, "farmstead");
                            try { Directory.CreateDirectory(farmDir); } catch { }
                            MakeShotCam();
                            yield return null;
                            for (int f = 0; f < 120 && !Expired(); f++) { Suppress(); yield return null; }

                            foreach (var view in farmViews)
                            {
                                if (Expired()) break;
                                PlaceCam(view);
                                for (int f = 0; f < 40 && !Expired(); f++) { Suppress(); yield return null; }
                                string file = Path.Combine(farmDir, "FARM_" + view.Name + ".png");
                                var cap = Capture(file);
                                while (cap.MoveNext()) yield return cap.Current;
                                RecordShot("FARM", view.Name, file);
                            }
                            RecordVariant("FARM", default(MapRuntime.LightingParams), false, false);
                        }
                    }
                }

                ok = _errors.Count == 0;
            }
            finally
            {

                MapRuntime.HarnessBuild = false;
                _running = false;
                try { WriteManifest(true); } catch { }
                try { WriteDone(ok, Time.realtimeSinceStartup - startedAt); } catch { }
                _log?.Msg("[shot] run over: " + _shotCount + " shot(s), " + _errors.Count
                        + " error(s), ok=" + ok + ". Quitting; the driver takes it from here.");
                try { MelonCoroutines.Start(QuitRoutine()); } catch { try { Application.Quit(); } catch { } }
            }
        }

        static IEnumerator QuitRoutine()
        {
            try { Application.Quit(); } catch { }

            float until = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < until) yield return null;
            try { System.Diagnostics.Process.GetCurrentProcess().Kill(); } catch { }
        }

        static bool BuildVariantParams(string v, MapRuntime.LightingParams baseline,
                                       out MapRuntime.LightingParams p, out bool shapingOff)
        {
            shapingOff = false;
            p = baseline;
            switch (v)
            {
                case "A":
                    return true;
                case "B":
                    p.apvOff = true;
                    return true;
                case "C":

                    p.apvOff = true;
                    p.fillLux = 0f;
                    return true;
                case "D":
                    p = MapRuntime.LightingParams.OwnRig(false);
                    return true;
                case "E":
                    p = MapRuntime.LightingParams.OwnRig(true);
                    return true;
                case "F":
                    p = MapRuntime.LightingParams.OwnRig(true);
                    p.ssgi = true;
                    return true;
                case "G":
                    p = MapRuntime.LightingParams.OwnRig(true);
                    shapingOff = true;
                    return true;
                case "H":

                    p = MapRuntime.LightingParams.OwnRig(true);
                    p.probeUsage = 0;
                    return true;
                case "I":
                    p = MapRuntime.LightingParams.OwnRig(true);
                    p.skyMode = 2;
                    return true;
            }
            return false;
        }

        static void MakeHarnessSun()
        {
            try
            {
                var go = new GameObject("HARNESS_SUN");
                go.SetActive(false);
                go.transform.rotation = Quaternion.Euler(50f, 135f, 0f);
                var l = go.AddComponent<Light>();
                l.type = LightType.Directional;
                l.shadows = LightShadows.Soft;
                l.color = Color.white;
                l.useColorTemperature = true;
                l.colorTemperature = 6100f;
                l.cullingMask = ~0;
                var hd = go.AddComponent<HD.HDAdditionalLightData>();
                if (hd != null)
                {
                    try { hd.SetIntensity(52000f, UnityEngine.Rendering.LightUnit.Lux); } catch { }
                    try { hd.EnableColorTemperature(true); } catch { }
                    try { hd.SetColor(Color.white, 6100f); } catch { }
                    try { hd.EnableShadows(true); } catch { }
                    try { hd.angularDiameter = 0.5f; } catch { }
                    try { hd.interactsWithSky = true; } catch { }
                }
                go.SetActive(true);
                _sunGo = go;
                _log?.Msg("[shot] HARNESS_SUN up: 52,000 lux @ elev 50 az 135, 6100 K, "
                        + "shadows on. Free-standing on purpose, so the host-sun passes "
                        + "adopt it as the recorded host sun.");
            }
            catch (Exception e) { Err("harness sun: " + e.Message); }
        }

        static void MakeShotCam()
        {
            try
            {
                var go = new GameObject("SHOT_CAM");
                var cam = go.AddComponent<Camera>();
                cam.fieldOfView = 60f;
                cam.depth = 100f;
                cam.nearClipPlane = 0.1f;

                cam.farClipPlane = 4000f;
                cam.cullingMask = ~0;
                try { go.tag = "MainCamera"; } catch { }
                var hd = go.AddComponent<HD.HDAdditionalCameraData>();
                if (hd != null)
                {
                    try { hd.clearColorMode = HD.HDAdditionalCameraData.ClearColorMode.Sky; } catch { }
                }

                _shotGo = go;
                _shotCam = cam;
                _shotHd = hd;
                _log?.Msg("[shot] SHOT_CAM up (fov 60, depth 100, sky clear, tagged "
                        + "MainCamera so the gate tick and the light budget follow it).");
            }
            catch (Exception e) { Err("shot cam: " + e.Message); }
        }

        static void PlaceCam(View v)
        {
            try
            {
                if (_shotGo == null) return;
                var t = _shotGo.transform;
                t.position = new Vector3(v.X, v.Y, v.Z);

                t.rotation = Quaternion.Euler(v.Pitch, v.Yaw, 0f);
            }
            catch (Exception e) { Err("place cam " + v.Name + ": " + e.Message); }
        }

        static void TryForceFrameBit(uint bit, bool on)
        {
            try
            {
                if (_shotHd == null) return;
                var fs = _shotHd.renderingPathCustomFrameSettings;
                var mask = _shotHd.renderingPathCustomFrameSettingsOverrideMask;
                fs.bitDatas[bit] = on;
                mask.mask[bit] = true;
                _shotHd.customRenderingSettings = true;
                _shotHd.renderingPathCustomFrameSettings = fs;
                _shotHd.renderingPathCustomFrameSettingsOverrideMask = mask;
                bool now = false;
                try { now = _shotHd.renderingPathCustomFrameSettings.IsEnabled((HD.FrameSettingsField)bit); } catch { }
                _log?.Msg("[shot] frame bit " + bit + " forced " + (on ? "ON" : "OFF")
                        + ", readback=" + now
                        + (now == on ? "" : "  (the write did NOT take; Sanitize or the seam refused it)"));
            }
            catch (Exception e) { _log?.Warning("[shot] frame bit " + bit + ": " + e.Message); }
        }

        static void Suppress()
        {
            float now = Time.realtimeSinceStartup;
            if (now >= _rescanAt)
            {
                _rescanAt = now + 1f;
                _canvases.Clear();
                _cameras.Clear();
                try
                {
                    var cs = UnityEngine.Object.FindObjectsOfType<Canvas>();
                    for (int i = 0; i < (cs != null ? cs.Length : 0); i++)
                        if (cs[i] != null) _canvases.Add(cs[i]);
                }
                catch { }
                try
                {
                    var cams = UnityEngine.Object.FindObjectsOfType<Camera>();
                    for (int i = 0; i < (cams != null ? cams.Length : 0); i++)
                        if (cams[i] != null) _cameras.Add(cams[i]);
                }
                catch { }
            }
            for (int i = 0; i < _canvases.Count; i++)
                try { if (_canvases[i] != null && _canvases[i].enabled) _canvases[i].enabled = false; } catch { }
            for (int i = 0; i < _cameras.Count; i++)
            {
                try
                {
                    var c = _cameras[i];
                    if (c == null || c == _shotCam) continue;
                    if (c.enabled) c.enabled = false;
                }
                catch { }
            }
        }

        static IEnumerator Capture(string path)
        {
            if (_captureMode != 2)
            {
                bool asked = false;
                try
                {
                    ScreenCapture.CaptureScreenshot(path, 1);
                    asked = true;
                }
                catch (Exception e)
                {
                    _log?.Warning("[shot] ScreenCapture threw (" + e.Message
                                + "); switching to the ReadPixels path for the whole run.");
                    _captureMode = 2;
                }
                if (asked)
                {

                    long lastSize = -1;
                    float deadline = Time.realtimeSinceStartup + 5f;
                    while (Time.realtimeSinceStartup < deadline)
                    {

                        for (int f = 0; f < 12; f++) yield return null;
                        long size = -1;
                        try { if (File.Exists(path)) size = new FileInfo(path).Length; } catch { }
                        if (size > 0 && size == lastSize) { _captureMode = 1; yield break; }
                        lastSize = size;
                    }
                    if (_captureMode == 1)
                    {

                        Err("capture never landed: " + Path.GetFileName(path));
                        yield break;
                    }
                    _log?.Warning("[shot] ScreenCapture produced no file in 5 s -- a "
                                + "silently stripped icall looks exactly like this. "
                                + "Switching to the ReadPixels path for the whole run.");
                    _captureMode = 2;
                }
            }

            yield return new WaitForEndOfFrame();
            try
            {
                int w = Screen.width, h = Screen.height;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                var png = ImageConversion.EncodeToPNG(tex);
                if (png != null && png.Length > 0)
                {
                    var managed = new byte[png.Length];
                    for (int i = 0; i < png.Length; i++) managed[i] = png[i];
                    File.WriteAllBytes(path, managed);
                }
                else Err("EncodeToPNG returned nothing for " + Path.GetFileName(path));
                UnityEngine.Object.Destroy(tex);
            }
            catch (Exception e) { Err("readpixels capture " + Path.GetFileName(path) + ": " + e.Message); }
        }

        static void RecordShot(string variant, string view, string file)
        {
            bool exists = false;
            long size = 0;
            try { exists = File.Exists(file); if (exists) size = new FileInfo(file).Length; } catch { }
            if (exists && size > 0) _shotCount++;
            _shotJson.Add("{\"variant\":" + JsonStr(variant) + ",\"view\":" + JsonStr(view)
                        + ",\"file\":" + JsonStr(Path.GetFileName(file))
                        + ",\"ok\":" + ((exists && size > 0) ? "true" : "false")
                        + ",\"bytes\":" + size + "}");
        }

        static void RecordVariant(string v, MapRuntime.LightingParams p, bool invalid, bool shapingOff)
        {
            var sb = new StringBuilder(512);
            sb.Append("{\"name\":").Append(JsonStr(v));
            sb.Append(",\"invalid\":").Append(invalid ? "true" : "false");
            sb.Append(",\"shapingForcedFlat\":").Append(shapingOff ? "true" : "false");
            sb.Append(",\"hostLit\":").Append(p.hostLit ? "true" : "false");
            sb.Append(",\"apvOff\":").Append(p.apvOff ? "true" : "false");
            sb.Append(",\"ssgi\":").Append(p.ssgi ? "true" : "false");
            sb.Append(",\"skyMode\":").Append(p.skyMode);
            sb.Append(",\"probeUsage\":").Append(p.probeUsage);
            sb.Append(",\"sunLux\":").Append(Num(p.sunLux));
            sb.Append(",\"fillLux\":").Append(Num(p.fillLux));
            sb.Append(",\"dayEv\":").Append(Num(p.dayEv));
            sb.Append(",\"indirectMult\":").Append(Num(p.indirectMult));
            try { sb.Append(",\"frameBits\":").Append(JsonStr(ApvGate.ReadBits())); } catch { }
            try { sb.Append(",\"sky\":").Append(JsonStr(SkyRig.Describe())); } catch { }
            try
            {
                var sh = RenderSettings.ambientProbe;
                float r = sh[0, 0], g = sh[1, 0], b = sh[2, 0];
                float lum = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                sb.Append(",\"probeDC\":[").Append(Num(r)).Append(',').Append(Num(g))
                  .Append(',').Append(Num(b)).Append("],\"probeLum\":").Append(Num(lum));
            }
            catch { sb.Append(",\"probeLum\":-1"); }
            try { sb.Append(",\"buildSeconds\":").Append(Num(MapRuntime.BuildSeconds)); } catch { }
            try { sb.Append(",\"patchQuads\":").Append(VoidPatches.QuadCount); } catch { }
            try { sb.Append(",\"constantsApplied\":").Append(MeshFactory.ConstantsApplied); } catch { }
            try { sb.Append(",\"emissiveApplied\":").Append(MeshFactory.EmissiveApplied); } catch { }
            try
            {
                var hist = MapRuntime.ShapingHistogram;
                if (hist != null)
                {
                    sb.Append(",\"shapingHistogram\":[");
                    for (int i = 0; i < hist.Length; i++)
                    { if (i > 0) sb.Append(','); sb.Append(hist[i]); }
                    sb.Append(']');
                }
            }
            catch { }
            sb.Append(",\"screen\":[").Append(Screen.width).Append(',').Append(Screen.height).Append("]");
            sb.Append('}');
            _variantJson.Add(sb.ToString());
        }

        static void WriteManifest(bool final)
        {
            if (_runDir == null) return;
            var sb = new StringBuilder(4096);
            sb.Append("{\n \"map\": \"PowderPalace\",\n \"final\": ").Append(final ? "true" : "false");
            sb.Append(",\n \"captureMode\": ").Append(JsonStr(
                _captureMode == 2 ? "readpixels" : _captureMode == 1 ? "screencapture" : "unknown"));
            sb.Append(",\n \"variants\": [\n  ");
            sb.Append(string.Join(",\n  ", _variantJson.ToArray()));
            sb.Append("\n ],\n \"shots\": [\n  ");
            sb.Append(string.Join(",\n  ", _shotJson.ToArray()));
            sb.Append("\n ],\n \"errors\": [");
            for (int i = 0; i < _errors.Count; i++)
            { if (i > 0) sb.Append(','); sb.Append(JsonStr(_errors[i])); }
            sb.Append("]\n}\n");
            try { File.WriteAllText(Path.Combine(_runDir, "manifest.json"), sb.ToString()); }
            catch (Exception e) { _log?.Warning("[shot] manifest write: " + e.Message); }
        }

        static void WriteDone(bool ok, float elapsed)
        {
            if (_runDir == null) return;
            var sb = new StringBuilder(256);
            sb.Append("{\"ok\":").Append(ok ? "true" : "false");
            sb.Append(",\"shots\":").Append(_shotCount);
            sb.Append(",\"elapsedSec\":").Append(Num(elapsed));
            sb.Append(",\"errors\":[");
            for (int i = 0; i < _errors.Count; i++)
            { if (i > 0) sb.Append(','); sb.Append(JsonStr(_errors[i])); }
            sb.Append("]}\n");
            try { File.WriteAllText(Path.Combine(_runDir, "done.json"), sb.ToString()); }
            catch (Exception e) { _log?.Warning("[shot] done write: " + e.Message); }
        }

        struct View
        {
            public string Name;
            public float X, Y, Z, Yaw, Pitch;
        }

        static List<View> LoadViews(string path)
        {
            var list = new List<View>(8);
            string body = null;
            try { if (File.Exists(path)) body = File.ReadAllText(path); } catch { }
            if (string.IsNullOrEmpty(body)) return list;
            int i = 0;
            while (true)
            {
                int open = body.IndexOf('{', i);
                if (open < 0) break;
                int close = body.IndexOf('}', open + 1);
                if (close < 0) break;
                int inner = body.IndexOf('{', open + 1);
                if (inner >= 0 && inner < close) { i = inner; continue; }

                string block = body.Substring(open, close - open + 1);
                if (block.IndexOf("\"pos\"", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var v = new View();
                    bool ok = ReadStr(block, "name", out v.Name)
                           && ReadVec3(block, "pos", out v.X, out v.Y, out v.Z);
                    ReadNum(block, "yaw", out v.Yaw);
                    ReadNum(block, "pitch", out v.Pitch);
                    if (ok && !string.IsNullOrEmpty(v.Name)) list.Add(v);
                }
                i = close + 1;
            }
            return list;
        }

        static bool ReadStr(string body, string key, out string v)
        {
            v = null;
            int i = body.IndexOf("\"" + key + "\"", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return false;
            int c = body.IndexOf(':', i);
            if (c < 0) return false;
            int q1 = body.IndexOf('"', c + 1);
            if (q1 < 0) return false;
            int q2 = body.IndexOf('"', q1 + 1);
            if (q2 < 0) return false;
            v = body.Substring(q1 + 1, q2 - q1 - 1);
            return true;
        }

        static bool ReadNum(string body, string key, out float v)
        {
            v = 0f;
            int i = body.IndexOf("\"" + key + "\"", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return false;
            int c = body.IndexOf(':', i);
            if (c < 0) return false;
            int e = c + 1;
            while (e < body.Length && body[e] != ',' && body[e] != '}' && body[e] != ']') e++;
            return float.TryParse(body.Substring(c + 1, e - c - 1).Trim(),
                NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        static bool ReadVec3(string body, string key, out float x, out float y, out float z)
        {
            x = y = z = 0f;
            int i = body.IndexOf("\"" + key + "\"", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return false;
            int b1 = body.IndexOf('[', i);
            if (b1 < 0) return false;
            int b2 = body.IndexOf(']', b1 + 1);
            if (b2 < 0) return false;
            var parts = body.Substring(b1 + 1, b2 - b1 - 1).Split(',');
            if (parts.Length < 3) return false;
            return float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out x)
                && float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out y)
                && float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out z);
        }

        static string Num(float f)
        {
            if (float.IsNaN(f) || float.IsInfinity(f)) return "0";
            return f.ToString("R", CultureInfo.InvariantCulture);
        }

        static string JsonStr(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 8);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
