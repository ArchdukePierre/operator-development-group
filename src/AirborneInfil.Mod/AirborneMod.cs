// ODG Operations - AirborneInfil
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UObject = UnityEngine.Object;

[assembly: MelonInfo(typeof(AirborneInfil.AirborneMod), "Airborne Infil", "2.0.0", "ArchdukePierre")]
[assembly: MelonGame(null, null)]

namespace AirborneInfil
{

    public class AirborneMod : MelonMod
    {
        internal static AirborneMod Instance;

        const int ST_IDLE = 0, ST_RIDE = 1, ST_DEPLOY = 2, ST_CANOPY = 3;

        MelonPreferences_Entry<bool> _enabled, _hideHeli, _showChute, _showPlane, _chuteMirror, _planeMirror;
        MelonPreferences_Entry<bool> _muteHeli, _windOn, _legsDangle, _teamChutes, _planeFollowHeading, _rideReskin;
        static MelonPreferences_Entry<bool> _opBoardMarker;
        internal static bool MarkersOn => _opBoardMarker == null || _opBoardMarker.Value;
        MelonPreferences_Entry<int> _anchorMode;
        MelonPreferences_Entry<float> _chuteHeight, _chuteScale, _chuteYaw, _deployDelay, _windVol, _swayAmt;
        MelonPreferences_Entry<float> _planeAlt, _planeSide, _planeSpeed, _planeScale, _planeYaw, _planeBack;
        MelonPreferences_Entry<string> _poseClip;

        int _state = ST_IDLE;
        bool _infilActive;
        float _infilStart, _deployAt, _deployT0;
        Il2Cpp.HelicopterV3 _heli;
        Transform _anchorTf;
        Il2Cpp.InteractableChair _localChair;
        Animator _chairAnim;
        string _sitAnim = "";
        readonly List<string> _clipNames = new List<string>();
        int _clipIdx = -1;
        readonly Transform[] _legBones = new Transform[6];
        GameObject _holder, _chuteTpl, _planeTpl, _chute, _plane, _windGO;
        AudioClip _windClip;
        readonly List<Renderer> _hidden = new List<Renderer>();
        readonly List<AudioSource> _mutedSrc = new List<AudioSource>();
        readonly List<float> _mutedVol = new List<float>();
        bool _needPlane;
        float _planeT0;
        Vector3 _planeOrigin, _planeDir = new Vector3(1f, 0f, 0.35f);
        Vector3 _prevAnchorPos, _smoothVel;
        bool _havePrev;

        static string AssetsDir => Path.Combine(AiPaths.GameRoot, "Mods", "AirborneInfil_Assets");

        public override void OnInitializeMelon()
        {
            Instance = this;
            var c = MelonPreferences.CreateCategory("AirborneInfil");
            _enabled     = c.CreateEntry("Enabled", true);
            _enabled.Value = true;
            _hideHeli    = c.CreateEntry("HideHelicopter", true);
            _showChute   = c.CreateEntry("ShowParachute", true);
            _showPlane   = c.CreateEntry("ShowC130Flyby", true);
            _muteHeli    = c.CreateEntry("MuteHeliRotor", true);
            _windOn      = c.CreateEntry("WindSound", true);
            _legsDangle  = c.CreateEntry("LegsDangle", true);
            _teamChutes  = c.CreateEntry("TeamChutes", true);
            _planeFollowHeading = c.CreateEntry("PlaneFollowsHeliHeading", true);
            _rideReskin  = c.CreateEntry("RideReskinHeli", false);
            _opBoardMarker = c.CreateEntry("OpBoardMarker", true);
            _anchorMode  = c.CreateEntry("ChuteAnchor_0seat_1camera", 0);
            _chuteHeight = c.CreateEntry("ChuteHeight", 1.5f);
            _chuteScale  = c.CreateEntry("ChuteScale", 0.33f);
            _chuteYaw    = c.CreateEntry("ChuteYaw", 0f);
            _deployDelay = c.CreateEntry("DeployDelaySeconds", 6f);
            _windVol     = c.CreateEntry("WindVolume", 0.45f);
            _swayAmt     = c.CreateEntry("SwayAmount", 1f);
            _planeAlt    = c.CreateEntry("PlaneAltitude", 200f);
            _planeSide   = c.CreateEntry("PlaneSideOffset", 40f);
            _planeBack   = c.CreateEntry("PlaneStartBack", 600f);
            _planeSpeed  = c.CreateEntry("PlaneSpeed", 95f);
            _planeScale  = c.CreateEntry("PlaneScale", 1.0f);
            _planeYaw    = c.CreateEntry("PlaneYaw", 0f);
            _planeMirror = c.CreateEntry("PlaneMirrorX", true);
            _chuteMirror = c.CreateEntry("ChuteMirrorX", true);
            _poseClip    = c.CreateEntry("PoseClipName", "");
            _planeDir = new Vector3(1f, 0f, 0.35f).normalized;

            try { File.AppendAllText(AiPaths.LogPath, "\n\n===== Airborne Infil v2  " + DateTime.Now + " =====\n"); } catch { }

            Log("loaded. Paradrop reskin v2. Toggle via MelonPreferences [AirborneInfil] "
              + "Enabled (currently " + _enabled.Value + "). No hotkey.");
            Log("numpad: 8/2 chute height, 6/4 scale, 9/7 yaw, 3/1 plane alt, +/- plane yaw, 5 values, 0 debug.");
            Log("pose keys: KeypadEnter cycle animation, KeypadPeriod back to sit, KeypadMultiply save as auto-pose, KeypadDivide legs dangle on/off.");

            Hook(typeof(Il2Cpp.InfilVehicleAnimation), "SetupVehicle", nameof(Post_SetupVehicle));
            Hook(typeof(Il2Cpp.HelicopterV3), "SitInAssignedSeat", nameof(Post_SitInSeat));
            Hook(typeof(Il2Cpp.HelicopterV3), "AutoExitHelicopter", nameof(Post_AutoExit));

            SteerableDrop.Init(this);
        }

        internal static bool MasterEnabled => Instance != null && Instance._enabled.Value;

        internal GameObject GetChuteClone() { EnsureChuteTemplate(); return Spawn(_chuteTpl); }

        internal GameObject MakeWindPlayer()
        {
            try
            {
                if (_windClip == null) _windClip = MakeWindClip();
                if (_windClip == null) return null;
                var go = new GameObject("AirborneDropWind");
                var src = go.AddComponent<AudioSource>();
                src.clip = _windClip; src.loop = true; src.spatialBlend = 0f;
                src.volume = _windVol.Value; src.Play();
                return go;
            }
            catch { return null; }
        }

        internal void PlaceDropChute(GameObject chute, Vector3 playerPos, Quaternion rot, float scaleMul = 1f)
        { Place(chute, playerPos + Vector3.up * _chuteHeight.Value, rot, _chuteScale.Value * scaleMul, _chuteMirror.Value); }

        internal void AttachChuteToPlayer(GameObject chute, Transform player)
        {
            if (chute == null || player == null) return;
            try
            {
                var t = chute.transform;
                t.SetParent(player, false);
                t.localPosition = Vector3.up * _chuteHeight.Value;
                t.localRotation = Quaternion.identity;
                float s = _chuteScale.Value;
                t.localScale = new Vector3(_chuteMirror.Value ? -s : s, s, s);
            }
            catch { }
        }

        internal void OrientChute(GameObject chute, float pop, float camYaw)
        {
            if (chute == null) return;
            try
            {
                var tr = chute.transform;
                tr.rotation = Quaternion.Euler(0f, camYaw + _chuteYaw.Value, 0f);
                float s = _chuteScale.Value * pop;
                tr.localScale = new Vector3(_chuteMirror.Value ? -s : s, s, s);
            }
            catch { }
        }

        Vector3 _chuteVel; float _chuteYawCur, _chuteYawVel; bool _chuteAnchorInit;
        internal void ResetChuteAnchor() { _chuteAnchorInit = false; _chuteVel = Vector3.zero; _chuteYawVel = 0f; }

        bool _camFarLogged;
        internal void PlaceChuteAnchored(GameObject chute, float pop, Vector3? body = null)
        {
            if (chute == null) return;
            try
            {
                var cam = Camera.main; if (cam == null) return;
                var ct = cam.transform;
                var tr = chute.transform;
                if (tr.parent != null) tr.SetParent(null, true);

                Vector3 anchor = ct.position;
                if (body.HasValue && (ct.position - body.Value).sqrMagnitude > 36f)
                {
                    anchor = body.Value + Vector3.up * 1.6f;
                    if (!_camFarLogged)
                    {
                        _camFarLogged = true;
                        Log("[chute] Camera.main is " + (ct.position - body.Value).magnitude.ToString("F0") +
                            " m from the body - anchoring the canopy to the body instead");
                    }
                }
                else _camFarLogged = false;

                Vector3 target = anchor + Vector3.up * _chuteHeight.Value;
                float camYaw = ct.eulerAngles.y + _chuteYaw.Value;
                if (!_chuteAnchorInit)
                { tr.position = target; _chuteYawCur = camYaw; _chuteVel = Vector3.zero; _chuteYawVel = 0f; _chuteAnchorInit = true; }
                float dt = Time.deltaTime;
                tr.position = Vector3.SmoothDamp(tr.position, target, ref _chuteVel, 0.05f, Mathf.Infinity, dt);
                _chuteYawCur = Mathf.SmoothDampAngle(_chuteYawCur, camYaw, ref _chuteYawVel, 0.09f, Mathf.Infinity, dt);
                tr.rotation = Quaternion.Euler(0f, _chuteYawCur, 0f);
                float s = _chuteScale.Value * pop;
                tr.localScale = new Vector3(_chuteMirror.Value ? -s : s, s, s);
            }
            catch { }
        }

        internal GameObject GetPlaneClone() { EnsurePlaneTemplate(); return Spawn(_planeTpl); }

        internal void PlaceDropPlane(GameObject plane, Vector3 pos, Vector3 dir)
        {
            Place(plane, pos,
                Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0f, _planeYaw.Value, 0f),
                _planeScale.Value, _planeMirror.Value);
        }

        public override void OnGUI() => SteerableDrop.DrawHUD();

        void Hook(Type t, string method, string handler)
        {
            try
            {
                var mi = AccessTools.Method(t, method);
                if (mi == null) { Log("HOOK MISS: " + t.Name + "." + method); return; }
                HarmonyInstance.Patch(mi, postfix: new HarmonyMethod(typeof(AirborneMod), handler));
                Log("hooked " + t.Name + "." + method);
            }
            catch (Exception e) { Log("HOOK FAIL " + t.Name + "." + method + ": " + e.Message); }
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            Cleanup("scene load");

            RemoteChutes.Clear();
            SteerableDrop.Abort("scene load");
            AirborneMarker.OnSceneLoad();
        }

        static void Post_SetupVehicle(Il2Cpp.InfilVehicleAnimation __instance)
        {
            try
            {
                if (Instance == null || !Instance._enabled.Value || __instance == null) return;
                bool exfil = false; try { exfil = __instance.NetworkisExfil; } catch { }
                if (exfil) return;
                string anim = ""; try { anim = __instance.activeAnimation ?? ""; } catch { }
                if (anim.Length > 0 && anim.IndexOf("Infil", StringComparison.OrdinalIgnoreCase) < 0) return;
                Il2Cpp.HelicopterV3 heli = null; try { heli = __instance.helicopter; } catch { }
                if (heli == null) return;
                if (!Instance._rideReskin.Value) return;
                Instance.BeginReskin(heli, "SetupVehicle anim='" + anim + "'");
            }
            catch (Exception e) { Log("SetupVehicle hook err: " + e.Message); }
        }

        static void Post_SitInSeat(Il2Cpp.HelicopterV3 __instance, Il2Cpp.PlayerMaster master, int seatID)
        {
            try
            {
                if (Instance == null || !Instance._enabled.Value || __instance == null) return;
                if (!Instance._rideReskin.Value) return;

                try { var iva = __instance.infilVehicleAnimation; if (iva != null && iva.NetworkisExfil) return; } catch { }
                if (!Instance._infilActive) Instance.BeginReskin(__instance, "SitInSeat(" + seatID + ")");
                else if (Instance._heli == null) Instance._heli = __instance;

                bool isLocal = false;
                try
                {
                    var local = Il2Cpp.PlayerMaster.MyPlayerMaster;
                    isLocal = master != null && local != null && master.Pointer == local.Pointer;
                }
                catch { }

                if (isLocal) Instance.CaptureSeatAnchor(__instance, seatID);
            }
            catch (Exception e) { Log("SitInSeat hook err: " + e.Message); }
        }

        static void Post_AutoExit(Il2Cpp.HelicopterV3 __instance)
        {
            try { if (Instance != null) Instance.Cleanup("AutoExitHelicopter"); }
            catch (Exception e) { Log("AutoExit hook err: " + e.Message); }
        }

        void BeginReskin(Il2Cpp.HelicopterV3 heli, string why)
        {
            if (!_enabled.Value) return;
            Cleanup("re-begin");
            _heli = heli;
            _infilActive = true;
            _state = ST_RIDE;
            _infilStart = Time.time;
            _deployAt = 0f;
            _havePrev = false; _smoothVel = Vector3.zero;
            EnsureTemplates();
            if (_hideHeli.Value) HideHeli();
            if (_muteHeli.Value) MuteHeliAudio();
            if (_showPlane.Value) { _needPlane = true; _planeT0 = 0f; }
            Log("reskin BEGIN [" + why + "] heli=" + (heli != null ? heli.name : "?") +
                " hidden=" + _hidden.Count + " muted=" + _mutedSrc.Count);
        }

        void Deploy()
        {
            _state = ST_DEPLOY;
            _deployT0 = Time.time;

            if (_showChute.Value) _chute = Spawn(_chuteTpl);
            if (_windOn.Value) StartWind();

            var pose = _poseClip.Value;
            if (!string.IsNullOrEmpty(pose) && _chairAnim != null)
            {
                try { _chairAnim.CrossFade(pose, 0.25f); Log("auto-pose -> '" + pose + "'"); }
                catch (Exception e) { Log("auto-pose failed: " + e.Message); }
            }
            Log("DEPLOY: canopy pop (local; " + RemoteChutes.Count + " remote tracked)");
        }

        void Cleanup(string why)
        {
            bool had = _infilActive || _chute != null || _plane != null || _hidden.Count > 0;
            RestoreHeli();
            RestoreHeliAudio();
            if (_chute != null) { try { UObject.Destroy(_chute); } catch { } _chute = null; }
            if (_plane != null) { try { UObject.Destroy(_plane); } catch { } _plane = null; }

            if (_windGO != null) { try { UObject.Destroy(_windGO); } catch { } _windGO = null; }
            _infilActive = false; _needPlane = false; _heli = null; _anchorTf = null;
            _localChair = null; _chairAnim = null; _sitAnim = ""; _clipNames.Clear(); _clipIdx = -1;
            for (int i = 0; i < _legBones.Length; i++) _legBones[i] = null;
            _state = ST_IDLE; _deployAt = 0f; _havePrev = false;
            if (had) Log("reskin CLEANUP [" + why + "]");
        }

        void CaptureSeatAnchor(Il2Cpp.HelicopterV3 heli, int seatID)
        {
            try
            {
                var seats = heli.helicopterSeats;
                if (seats == null || seatID < 0 || seatID >= seats.Count) return;
                var seat = seats[seatID];
                var chair = seat != null ? seat.interactableChair : null;
                if (chair == null) return;
                _localChair = chair;
                var pp = chair.playerPosition;
                if (pp == null) pp = chair.rootAnimatorObject;
                if (pp != null) { _anchorTf = pp; Log("chute anchored to local seat " + seatID + " (" + pp.name + ")"); }
                if (_deployAt <= 0f) _deployAt = Time.time + Mathf.Max(1f, _deployDelay.Value);

                try { _chairAnim = chair.animator; } catch { }
                try { _sitAnim = chair.SitAnimation ?? ""; } catch { }
                EnumerateClips();
                CacheLegBones(chair);
            }
            catch (Exception e) { Log("CaptureSeatAnchor err: " + e.Message); }
        }

        void EnumerateClips()
        {
            _clipNames.Clear(); _clipIdx = -1;
            try
            {
                if (_chairAnim == null) { Log("no chair animator - clip hunt unavailable"); return; }
                var rac = _chairAnim.runtimeAnimatorController;
                if (rac == null) { Log("chair animator has no controller - clip hunt unavailable"); return; }
                var clips = rac.animationClips;
                if (clips == null) return;
                var seen = new HashSet<string>();
                for (int i = 0; i < clips.Length; i++)
                {
                    var cl = clips[i]; if (cl == null) continue;
                    var n = cl.name; if (string.IsNullOrEmpty(n) || !seen.Add(n)) continue;
                    _clipNames.Add(n);
                }
                Log("animator clips available: " + _clipNames.Count + " (KeypadEnter cycles them live)");
                var sb = new StringBuilder();
                for (int i = 0; i < _clipNames.Count; i++)
                {
                    sb.Append('[').Append(i).Append("] ").Append(_clipNames[i]).Append("   ");
                    if ((i + 1) % 6 == 0) { Log("  " + sb.ToString()); sb.Clear(); }
                }
                if (sb.Length > 0) Log("  " + sb.ToString());
            }
            catch (Exception e) { Log("EnumerateClips err: " + e.Message); }
        }

        void CacheLegBones(Il2Cpp.InteractableChair chair)
        {
            for (int i = 0; i < _legBones.Length; i++) _legBones[i] = null;
            try
            {
                Transform root = null;
                try { root = chair.rootAnimatorObject; } catch { }
                if (root == null && _chairAnim != null) root = _chairAnim.transform;
                if (root == null) { Log("no rig root - legs dangle unavailable"); return; }
                string[] want = { "LeftUpLeg", "RightUpLeg", "LeftLeg", "RightLeg", "LeftFoot", "RightFoot" };
                int found = 0, visited = 0;
                var stack = new Stack<Transform>();
                stack.Push(root);
                while (stack.Count > 0 && visited < 600 && found < want.Length)
                {
                    var t = stack.Pop(); visited++;
                    if (t == null) continue;
                    var n = t.name;
                    for (int w = 0; w < want.Length; w++)
                        if (_legBones[w] == null && n.EndsWith(want[w], StringComparison.OrdinalIgnoreCase))
                        { _legBones[w] = t; found++; break; }
                    int ch = t.childCount;
                    for (int i = 0; i < ch; i++) { try { stack.Push(t.GetChild(i)); } catch { } }
                }
                Log("leg bones cached: " + found + "/6 (visited " + visited + " nodes)");
            }
            catch (Exception e) { Log("CacheLegBones err: " + e.Message); }
        }

        void HideHeli()
        {
            if (_heli == null) return;
            try
            {
                var rs = _heli.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rs.Length; i++)
                {
                    var r = rs[i];
                    if (r != null && r.enabled) { r.enabled = false; _hidden.Add(r); }
                }
            }
            catch (Exception e) { Log("HideHeli err: " + e.Message); }
        }

        void RestoreHeli()
        {
            for (int i = 0; i < _hidden.Count; i++)
            { try { var r = _hidden[i]; if (r != null) r.enabled = true; } catch { } }
            _hidden.Clear();
        }

        void MuteHeliAudio()
        {
            if (_heli == null) return;
            try
            {
                var srcs = _heli.GetComponentsInChildren<AudioSource>(true);
                for (int i = 0; i < srcs.Length; i++)
                {
                    var s = srcs[i]; if (s == null) continue;
                    string n = "";
                    try { n = s.gameObject.name ?? ""; } catch { }

                    bool machine = n.IndexOf("Engine", StringComparison.OrdinalIgnoreCase) >= 0
                                || n.IndexOf("Rotor", StringComparison.OrdinalIgnoreCase) >= 0
                                || n.IndexOf("Wind", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!machine) continue;
                    try { _mutedSrc.Add(s); _mutedVol.Add(s.volume); s.volume = 0f; } catch { }
                }
                Log("muted " + _mutedSrc.Count + " heli audio sources (rotor/engine/wind)");
            }
            catch (Exception e) { Log("MuteHeliAudio err: " + e.Message); }
        }

        void RestoreHeliAudio()
        {
            for (int i = 0; i < _mutedSrc.Count && i < _mutedVol.Count; i++)
            { try { var s = _mutedSrc[i]; if (s != null) s.volume = _mutedVol[i]; } catch { } }
            _mutedSrc.Clear(); _mutedVol.Clear();
        }

        void StartWind()
        {
            try
            {
                if (_windClip == null) _windClip = MakeWindClip();
                if (_windClip == null) return;
                _windGO = new GameObject("AirborneWind");
                var src = _windGO.AddComponent<AudioSource>();
                src.clip = _windClip;
                src.loop = true;
                src.spatialBlend = 0f;
                src.volume = 0f;
                src.Play();
            }
            catch (Exception e) { Log("StartWind err: " + e.Message); }
        }

        AudioClip MakeWindClip()
        {
            try
            {
                const int sr = 22050;
                const int n = sr * 4;
                var data = new float[n];
                var rng = new System.Random(1234);
                float v = 0f, lp = 0f;
                for (int i = 0; i < n; i++)
                {
                    v += (float)(rng.NextDouble() - 0.5) * 0.25f;
                    v *= 0.985f;
                    lp += 0.12f * (v - lp);
                    float s = lp * 3f;

                    s *= 0.75f + 0.25f * (float)Math.Sin(2.0 * Math.PI * i / n);
                    data[i] = Mathf.Clamp(s, -0.95f, 0.95f);
                }

                int fade = sr / 3;
                for (int i = 0; i < fade; i++)
                {
                    float w = (float)i / fade;
                    data[n - fade + i] = data[n - fade + i] * (1f - w) + data[i] * w;
                }
                var arr = new Il2CppStructArray<float>(n);
                for (int i = 0; i < n; i++) arr[i] = data[i];
                var clip = AudioClip.Create("AirborneWindLoop", n, 1, sr, false);
                if (clip == null || !clip.SetData(arr, 0)) { Log("wind clip SetData failed"); return null; }
                Log("wind loop synthesized (4s, 22kHz)");
                return clip;
            }
            catch (Exception e) { Log("MakeWindClip err: " + e.Message); return null; }
        }

        public override void OnUpdate()
        {
            try
            {
                Hotkeys();
                SteerableDrop.Update();

                if (_enabled.Value && _teamChutes.Value && _showChute.Value)
                {

                    bool sceneLoading = false;
                    try
                    {
                        int nsc = UnityEngine.SceneManagement.SceneManager.sceneCount;
                        for (int si = 0; si < nsc; si++)
                            if (!UnityEngine.SceneManagement.SceneManager.GetSceneAt(si).isLoaded)
                            { sceneLoading = true; break; }
                    }
                    catch { }

                    if (sceneLoading)
                    {

                        RemoteChutes.Clear();
                    }
                    else
                    {
                        EnsureChuteTemplate();
                        RemoteChutes.Tick(_chuteTpl, Spawn, PlaceChute,
                                          SteerableDrop.AirborneIndex, Log);
                    }
                }

                if (!_infilActive) return;
                if (Time.time - _infilStart > 600f) { Cleanup("safety timeout"); return; }

                if (_state == ST_RIDE && _deployAt > 0f && Time.time >= _deployAt && !SteerableDrop.TakesOver) Deploy();
                float pop = 1f;
                if (_state == ST_DEPLOY)
                {
                    float t = (Time.time - _deployT0) / 0.9f;
                    if (t >= 1f) { _state = ST_CANOPY; pop = 1f; }
                    else pop = EaseOutBack(Mathf.Max(0.02f, t));
                }

                if (_state >= ST_DEPLOY) UpdateChutes(pop);

                if (_windGO != null)
                {
                    try
                    {
                        var src = _windGO.GetComponent<AudioSource>();
                        if (src != null) src.volume = Mathf.MoveTowards(src.volume, _windVol.Value, Time.deltaTime * 0.35f);
                    }
                    catch { }
                }

                if (_needPlane && _heli != null)
                {
                    var hp = _heli.transform.position;
                    if (hp.sqrMagnitude > 1f)
                    {
                        if (_planeFollowHeading.Value)
                        {
                            var f = _heli.transform.forward; f.y = 0f;
                            if (f.sqrMagnitude > 0.04f) _planeDir = f.normalized;
                        }
                        float back = _deployAt > 0f
                            ? _planeSpeed.Value * Mathf.Max(2f, _deployAt - Time.time)
                            : _planeBack.Value;
                        _plane = Spawn(_planeTpl);
                        Sounds.AttachPlaneAudio(_plane);
                        _planeOrigin = hp + Vector3.up * _planeAlt.Value
                                       + new Vector3(-_planeDir.z, 0f, _planeDir.x) * _planeSide.Value
                                       - _planeDir * back;
                        _planeT0 = Time.time;
                        _needPlane = false;
                        Log("C130 spawned " + back.ToString("F0") + "m out, overhead at deploy, heading " + F(_planeDir));
                    }
                }
                if (_plane != null)
                {
                    Vector3 pos = _planeOrigin + _planeDir * (_planeSpeed.Value * (Time.time - _planeT0));
                    Quaternion rot = Quaternion.LookRotation(_planeDir, Vector3.up) * Quaternion.Euler(0f, _planeYaw.Value, 0f);
                    Place(_plane, pos, rot, _planeScale.Value, _planeMirror.Value);
                }
            }
            catch { }
        }

        void UpdateChutes(float pop)
        {
            Transform a = (_anchorMode.Value == 1 && Camera.main != null) ? Camera.main.transform
                         : (_anchorTf != null ? _anchorTf : (_heli != null ? _heli.transform : null));
            if (a == null) return;

            Vector3 pos;
            try { pos = a.position; } catch { return; }

            if (_havePrev && Time.deltaTime > 0.0001f)
            {
                var vel = (pos - _prevAnchorPos) / Time.deltaTime;
                vel.y = 0f;
                _smoothVel = Vector3.Lerp(_smoothVel, vel, 0.08f);
            }
            _prevAnchorPos = pos; _havePrev = true;

            float speed = _smoothVel.magnitude;
            float baseYaw = speed > 1.5f ? Quaternion.LookRotation(_smoothVel.normalized, Vector3.up).eulerAngles.y
                                         : a.eulerAngles.y;
            float lean = Mathf.Min(12f, speed * 0.8f);

            if (_chute != null)
                PlaceChute(_chute, pos, baseYaw, lean, pop, 0f);

        }

        void PlaceChute(GameObject go, Vector3 anchorPos, float baseYaw, float lean, float pop, float phase)
        {
            float t = Time.time, sway = _swayAmt.Value;
            float roll  = 5.0f * Mathf.Sin(t * 0.70f + phase) * sway;
            float pitch = 3.5f * Mathf.Sin(t * 0.53f + phase * 1.3f) * sway;
            float drift = 6.0f * Mathf.Sin(t * 0.21f + phase) * sway;
            var rot = Quaternion.Euler(lean + pitch, baseYaw + _chuteYaw.Value + drift, roll);
            Place(go, anchorPos + Vector3.up * _chuteHeight.Value, rot, _chuteScale.Value * pop, _chuteMirror.Value);
        }

        static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            t = Mathf.Clamp01(t);
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        public override void OnLateUpdate()
        {
            SteerableDrop.LateTick();
            if (!_infilActive || _state < ST_DEPLOY || !_legsDangle.Value) return;
            try
            {
                float t = Time.time;
                var lu = _legBones[0]; var ru = _legBones[1];
                var ll = _legBones[2]; var rl = _legBones[3];
                var lf = _legBones[4]; var rf = _legBones[5];
                if (lu != null) lu.localRotation = Quaternion.Euler(6f * Mathf.Sin(t * 1.15f), 0f, 4f);
                if (ru != null) ru.localRotation = Quaternion.Euler(6f * Mathf.Sin(t * 1.15f + 1.7f), 0f, -4f);
                if (ll != null) ll.localRotation = Quaternion.Euler(12f + 5f * Mathf.Sin(t * 1.3f + 0.9f), 0f, 0f);
                if (rl != null) rl.localRotation = Quaternion.Euler(12f + 5f * Mathf.Sin(t * 1.3f + 2.4f), 0f, 0f);
                if (lf != null) lf.localRotation = Quaternion.Euler(15f, 0f, 0f);
                if (rf != null) rf.localRotation = Quaternion.Euler(15f, 0f, 0f);
            }
            catch { }
        }

        static void Place(GameObject go, Vector3 pos, Quaternion rot, float scale, bool mirror)
        {
            if (go == null) return;
            var t = go.transform;
            t.position = pos;
            t.rotation = rot;
            t.localScale = new Vector3(mirror ? -scale : scale, scale, scale);
        }

        void EnsureTemplates()
        {
            EnsureChuteTemplate();
            EnsurePlaneTemplate();
        }

        void EnsureHolder()
        {
            if (_holder == null)
            {
                _holder = new GameObject("AirborneInfil_Templates");
                _holder.SetActive(false);
                UObject.DontDestroyOnLoad(_holder);
            }
        }

        void EnsureChuteTemplate()
        {
            EnsureHolder();
            if (_chuteTpl == null)
            {

                var pm = new PropMaterial { BaseMap = "Parachute_base.png", NormalMap = "Parachute_norm.png", DoubleSided = false, Smoothness = 0.2f };
                _chuteTpl = PropBuilder.Build("AirborneChute", _holder.transform, Path.Combine(AssetsDir, "parachute.obj"), AssetsDir, pm, 1f, false, true, LoggerInstance);
            }
        }

        void EnsurePlaneTemplate()
        {
            EnsureHolder();
            if (_planeTpl == null)
            {

                _planeTpl = PropBuilder.BuildFromManifest("AirborneC130", _holder.transform, Path.Combine(AssetsDir, "c130_parts"), LoggerInstance);
                if (_planeTpl == null)
                {
                    var pm = new PropMaterial { BaseMap = null, DoubleSided = true, Fallback = new Color(0.30f, 0.33f, 0.30f), Smoothness = 0.15f };
                    _planeTpl = PropBuilder.Build("AirborneC130", _holder.transform, Path.Combine(AssetsDir, "c130.obj"), AssetsDir, pm, 1f, false, false, LoggerInstance);
                }
            }
        }

        GameObject Spawn(GameObject tpl)
        {
            if (tpl == null) return null;
            try
            {
                var clone = UObject.Instantiate(tpl).TryCast<GameObject>();
                if (clone == null) return null;
                clone.transform.SetParent(null, true);
                clone.SetActive(true);
                return clone;
            }
            catch (Exception e) { Log("Spawn err: " + e.Message); return null; }
        }

        void DumpScenes()
        {
            try
            {
                int n = UnityEngine.SceneManagement.SceneManager.sceneCountInBuildSettings;
                Log("[scenes] ===== " + n + " scene(s) in build settings =====");
                for (int i = 0; i < n; i++)
                {
                    string path = "?";
                    try { path = UnityEngine.SceneManagement.SceneUtility.GetScenePathByBuildIndex(i); } catch { }
                    Log("[scenes] [" + i + "] " + path);
                }
                int loaded = UnityEngine.SceneManagement.SceneManager.sceneCount;
                Log("[scenes] ----- " + loaded + " loaded -----");
                for (int i = 0; i < loaded; i++)
                {
                    try
                    {
                        var s = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                        Log("[scenes] loaded: '" + s.name + "' path=" + s.path + " rootCount=" + s.rootCount +
                            " valid=" + s.IsValid() + " loaded=" + s.isLoaded);
                    }
                    catch { }
                }
                try
                {
                    var om = UObject.FindObjectOfType<Il2Cpp.OperationsManager>();
                    Log("[scenes] OperationsManager.currentScene = " + (om != null ? om.currentScene : "(none)"));
                }
                catch (Exception e) { Log("[scenes] currentScene unavailable: " + e.Message); }
                Log("[scenes] ===== end =====");
            }
            catch (Exception e) { Log("[scenes] err: " + e.Message); }
        }

        void Hotkeys()
        {

            if (Input.GetKeyDown(KeyCode.Keypad8)) Nudge(_chuteHeight, 0.5f, "chuteHeight");
            if (Input.GetKeyDown(KeyCode.Keypad2)) Nudge(_chuteHeight, -0.5f, "chuteHeight");
            if (Input.GetKeyDown(KeyCode.Keypad6)) Nudge(_chuteScale, 0.03f, "chuteScale");
            if (Input.GetKeyDown(KeyCode.Keypad4)) Nudge(_chuteScale, -0.03f, "chuteScale");
            if (Input.GetKeyDown(KeyCode.Keypad9)) Nudge(_chuteYaw, 15f, "chuteYaw");
            if (Input.GetKeyDown(KeyCode.Keypad7)) Nudge(_chuteYaw, -15f, "chuteYaw");
            if (Input.GetKeyDown(KeyCode.Keypad3)) Nudge(_planeAlt, 20f, "planeAlt");
            if (Input.GetKeyDown(KeyCode.Keypad1)) Nudge(_planeAlt, -20f, "planeAlt");
            if (Input.GetKeyDown(KeyCode.KeypadPlus)) Nudge(_planeYaw, 15f, "planeYaw");
            if (Input.GetKeyDown(KeyCode.KeypadMinus)) Nudge(_planeYaw, -15f, "planeYaw");
            if (Input.GetKeyDown(KeyCode.Keypad5)) PrintValues();
            if (Input.GetKeyDown(KeyCode.Keypad0)) DumpDebug();

            if (Input.GetKeyDown(KeyCode.KeypadEnter) && _chairAnim != null && _clipNames.Count > 0)
            {
                _clipIdx = (_clipIdx + 1) % _clipNames.Count;
                var name = _clipNames[_clipIdx];
                try { _chairAnim.CrossFade(name, 0.25f); Log("POSE TRY [" + _clipIdx + "/" + (_clipNames.Count - 1) + "]: " + name); }
                catch (Exception e)
                {
                    try { _chairAnim.Play(name); Log("POSE TRY (hard) [" + _clipIdx + "]: " + name); }
                    catch { Log("pose '" + name + "' not playable: " + e.Message); }
                }
            }
            if (Input.GetKeyDown(KeyCode.KeypadPeriod) && _chairAnim != null && _sitAnim.Length > 0)
            {
                try { _chairAnim.CrossFade(_sitAnim, 0.25f); Log("pose reverted to sit ('" + _sitAnim + "')"); } catch { }
            }
            if (Input.GetKeyDown(KeyCode.KeypadMultiply) && _clipIdx >= 0 && _clipIdx < _clipNames.Count)
            {
                _poseClip.Value = _clipNames[_clipIdx];
                Log("SAVED auto-pose = '" + _poseClip.Value + "' (plays at every deploy; clear via PoseClipName pref)");
            }
            if (Input.GetKeyDown(KeyCode.KeypadDivide))
            {
                _legsDangle.Value = !_legsDangle.Value;
                Log("legs dangle " + (_legsDangle.Value ? "ON" : "OFF"));
            }
        }

        void Nudge(MelonPreferences_Entry<float> e, float d, string name)
        { e.Value += d; Log(name + " = " + e.Value.ToString("F2")); }

        void PrintValues()
        {
            Log("VALUES  chuteHeight=" + _chuteHeight.Value.ToString("F2") + " chuteScale=" + _chuteScale.Value.ToString("F3") +
                " chuteYaw=" + _chuteYaw.Value.ToString("F0") + " deployDelay=" + _deployDelay.Value.ToString("F1") +
                " windVol=" + _windVol.Value.ToString("F2") + " sway=" + _swayAmt.Value.ToString("F2") +
                " | planeAlt=" + _planeAlt.Value.ToString("F0") + " planeYaw=" + _planeYaw.Value.ToString("F0") +
                " planeScale=" + _planeScale.Value.ToString("F2") +
                " | pose='" + _poseClip.Value + "' dangle=" + _legsDangle.Value);
        }

        void DumpDebug()
        {
            try
            {
                Log("== DEBUG DUMP ==");
                Log("  state=" + _state + " infilActive=" + _infilActive +
                    " deployIn=" + (_deployAt > 0 ? (_deployAt - Time.time).ToString("F1") + "s" : "unset") +
                    " heli=" + (_heli != null ? _heli.name : "null"));
                Log("  chute=" + (_chute != null) + " teamChutes=" + RemoteChutes.Count + " plane=" + (_plane != null) +
                    " hidden=" + _hidden.Count + " muted=" + _mutedSrc.Count + " wind=" + (_windGO != null));
                Log("  anchor=" + (_anchorTf != null ? _anchorTf.name : "null") +
                    " chairAnim=" + (_chairAnim != null) + " clips=" + _clipNames.Count +
                    " legBones=" + CountBones() + " sit='" + _sitAnim + "'");
                if (_heli != null) Log("  heliPos=" + F(_heli.transform.position));
                if (Camera.main != null) Log("  cameraPos=" + F(Camera.main.transform.position));
            }
            catch (Exception e) { Log("dump err: " + e.Message); }
        }

        int CountBones() { int n = 0; for (int i = 0; i < _legBones.Length; i++) if (_legBones[i] != null) n++; return n; }

        static string F(Vector3 v) => v.x.ToString("F1") + "," + v.y.ToString("F1") + "," + v.z.ToString("F1");

        internal static void Log(string s)
        {
            try { File.AppendAllText(AiPaths.LogPath, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s + "\n"); } catch { }
            try { Instance?.LoggerInstance.Msg(s); } catch { }
        }
    }

    internal static class AiPaths
    {
        internal static string GameRoot
        {
            get { try { return MelonLoader.Utils.MelonEnvironment.GameRootDirectory; } catch { return AppContext.BaseDirectory; } }
        }
        internal static string LogPath => Path.Combine(GameRoot, "Mods", "AirborneInfil.log");
    }
}
