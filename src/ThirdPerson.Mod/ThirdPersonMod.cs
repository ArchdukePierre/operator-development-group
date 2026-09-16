// ODG Operations - ThirdPerson
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Diagnostics;
using System.Reflection;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(ThirdPerson.ThirdPersonMod), "ODG Third Person", "1.0.0", "ArchdukePierre")]
[assembly: MelonGame(null, null)]

namespace ThirdPerson
{

    public class ThirdPersonMod : MelonMod
    {
        private const string Version = "1.0.0";

        private MelonPreferences_Category _cat;
        private MelonPreferences_Entry<bool> _enabled, _startInTp, _adsToFp, _fpInVehicles, _collision, _squeezeToEye, _perfLog, _matchClip;
        private MelonPreferences_Entry<int> _mipBoost;
        private MelonPreferences_Entry<string> _mipModeCfg;
        private MelonPreferences_Entry<string> _toggleKeyCfg, _swapKeyCfg, _cycleKeyCfg, _panelKeyCfg, _dumpKeyCfg, _current, _nvgLookCfg;
        private readonly MelonPreferences_Entry<string>[] _presetCfg = new MelonPreferences_Entry<string>[Preset.Count];
        private MelonPreferences_Entry<int> _activePreset, _revision;
        private MelonPreferences_Entry<float> _posSmooth, _rotSmooth, _swapSeconds, _blendSeconds, _collisionRadius, _minDistance, _mouseSens, _panelScale;

        private KeyCode _toggleKey = KeyCode.F5, _swapKey = KeyCode.X, _cycleKey = KeyCode.P, _panelKey = KeyCode.F9, _dumpKey = KeyCode.F10;

        private Il2Cpp.PlayerNetworking _pn;
        private Il2Cpp.TpToggle _tp;
        private Camera _cam;
        private Unity.Cinemachine.CinemachineCamera _cm;
        private Transform _eye;
        private Transform _root;
        private float _nextAcquire;
        private bool _warnedNoToggle;

        private readonly Rig _rig = new Rig();
        private readonly Nvg _nvg = new Nvg();
        private Preset[] _presets;
        private int _slot = 1;
        private bool _wantTp;
        private bool _gameTp;
        private bool _headShown;
        private float _nativeFov = 60f;
        private bool _fovOverridden;
        private float _nextHeadCheck;
        private int _lastDriveFrame = -1;
        private bool _beforeRenderHooked;
        private int _beforeRenderCalls;
        private int _lateUpdateWrites;

        private bool _validNow;
        private int _lastPrefixFrame = -100;
        private float _lastFovWritten = -1f;
        private float _nextFovReassert;
        private float _nextTogglePoll;
        private bool _dirty;
        private float _saveAt;
        private float _headChangedAt;
        private bool _clipMatched;
        private float _tpNearOrig, _tpFarOrig;
        private bool _mipApplied;
        private int _mipBase;
        private bool _mipAlways;
        private int _mipGameValue;
        private int _mipLastWritten = -1;
        private float _mipNextPoll;

        private long _perfUpdateTicks, _perfDriveTicks;
        private int _perfDriveCalls, _perfFrames;
        private float _perfNextLog;
        private int _perfGetKeyAtLog;
        private const int PerfN = 1024;
        private readonly float[] _perfT = new float[PerfN];
        private readonly float[] _perfDt = new float[PerfN];
        private int _perfHead;
        private float _perfSwitchAt = -1f;
        private string _perfBefore;

        private static ThirdPersonMod _instance;
        private static UnityEngine.Events.UnityAction _beforeRenderAction;

        private Panel _panel;
        private bool _panelOpen;
        private int _row;
        private float _holdTimer;
        private int _holdDir;

        private const int RowDistance = 0, RowSide = 1, RowHeight = 2, RowTiltPitch = 3, RowTiltYaw = 4, RowFov = 5,
                          RowConverge = 6, RowShoulder = 7, RowSmoothing = 8, RowSwap = 9, RowAds = 10, RowVehicle = 11,
                          RowCollision = 12, RowNvg = 13, RowSlot = 14, RowSave = 15, RowReset = 16, RowN = 17;

        private static readonly string[] RowLabels =
        {
            "Distance", "Shoulder offset", "Height", "Tilt pitch", "Tilt yaw", "Field of view",
            "Aim convergence", "Shoulder", "Smoothing", "Swap time", "ADS to first person", "First person in vehicles",
            "Wall collision", "Night vision look", "Preset slot", "Save to slot", "Reset to defaults"
        };

        public override void OnInitializeMelon()
        {
            _instance = this;
            _cat = MelonPreferences.CreateCategory("ThirdPerson");

            _enabled = _cat.CreateEntry("Enabled", true, description: "Master switch for the mod.");
            _revision = _cat.CreateEntry("Revision", 0, description: "Internal. Config migration marker.");
            _startInTp = _cat.CreateEntry("StartInThirdPerson", false, description: "Begin every mission in third person.");

            _toggleKeyCfg = _cat.CreateEntry("ToggleKey", "F5", description: "Switch between first and third person.");
            _swapKeyCfg = _cat.CreateEntry("SwapShoulderKey", "X", description: "Orbit the camera to the other shoulder.");
            _cycleKeyCfg = _cat.CreateEntry("CyclePresetKey", "P", description: "Load the next saved camera position.");
            _panelKeyCfg = _cat.CreateEntry("PanelKey", "F9", description: "Open the adjustment panel.");
            _dumpKeyCfg = _cat.CreateEntry("DumpKey", "F10", description: "Log every camera fact to the console. Diagnostic only; None disables.");
            _nvgLookCfg = _cat.CreateEntry("ThirdPersonNvg", "CameraGoggles", description: "Night vision behind the character. CameraGoggles: the game's camera-mounted PVS-31 pair (its helmet-cam look) is switched on while the camera is behind the character and handed back at the eye. Off: leave the game's goggle circles where they fall. FullScreen, the old default, did not take in play and now means CameraGoggles.");

            _current = _cat.CreateEntry("Current", "", description: "The live camera position (d|side|h|tiltP|tiltY|fov|converge|right). Managed by the panel.");
            _activePreset = _cat.CreateEntry("ActivePreset", 1, description: "Preset slot selected in the panel, 1-4.");
            var defaults = Preset.Defaults();
            for (int i = 0; i < Preset.Count; i++)
                _presetCfg[i] = _cat.CreateEntry("Preset" + (i + 1), defaults[i].Serialize(), description: "Saved position " + (i + 1) + ": name|d|side|h|tiltP|tiltY|fov|converge|right.");

            _adsToFp = _cat.CreateEntry("AdsToFirstPerson", true, description: "Aiming down sights returns to first person until you stop aiming.");
            _fpInVehicles = _cat.CreateEntry("FirstPersonInVehicles", true, description: "Seats in the helicopter use first person.");
            _collision = _cat.CreateEntry("WallCollision", true, description: "Pull the camera in when a wall is between it and your head.");
            _squeezeToEye = _cat.CreateEntry("SqueezeToEye", true, description: "When a wall pulls the camera to within half a metre of your head, ease the view to the eye instead of showing the inside of the helmet.");
            _collisionRadius = _cat.CreateEntry("CollisionRadius", 0.2f, description: "Metres of clearance the camera keeps from walls.");
            _minDistance = _cat.CreateEntry("CollisionMinDistance", 0.3f, description: "Closest the camera may be pulled in, metres.");
            _posSmooth = _cat.CreateEntry("PositionSmoothing", 12f, description: "Camera position follow rate per second. 0 is rigid.");
            _rotSmooth = _cat.CreateEntry("RotationSmoothing", 16f, description: "Camera rotation follow rate per second. 0 is rigid.");
            _swapSeconds = _cat.CreateEntry("SwapSeconds", 0.35f, description: "How long the orbit to the other shoulder takes.");
            _blendSeconds = _cat.CreateEntry("BlendSeconds", 0.25f, description: "How long the move between first and third person takes.");
            _mouseSens = _cat.CreateEntry("PanelMouseSensitivity", 0.02f, description: "Metres the camera moves per unit of mouse travel while the panel is open.");
            _panelScale = _cat.CreateEntry("PanelScale", 1f, description: "Size multiplier for the panel and messages.");
            _perfLog = _cat.CreateEntry("PerfLog", true, description: "Log the mod's own per-frame cost every 30 seconds, and the fps for five seconds either side of each view switch.");
            _matchClip = _cat.CreateEntry("MatchFirstPersonClipPlanes", true, description: "Give the third-person camera the first-person camera's near and far clip planes, so the gun and arms are not sliced away when the camera is pulled in close.");
            _mipBoost = _cat.CreateEntry("ThirdPersonMipBoost", 0, description: "Off at 0. Raise the global texture mipmap limit by this many steps (1 halves texture memory). For cards that page textures once the character streams in at full detail.");
            _mipModeCfg = _cat.CreateEntry("MipBoostMode", "Always", description: "Always: apply the boost once at startup and hold it, so textures load at the reduced level and nothing is ever re-uploaded (no stalls; first person is one step softer too). Switch: apply on entering third person and restore on leaving, which re-uploads every texture at each switch and every aim, a multi-second stall.");

            _toggleKey = ParseKey(_toggleKeyCfg.Value, KeyCode.F5);
            _swapKey = ParseKey(_swapKeyCfg.Value, KeyCode.X);
            _cycleKey = ParseKey(_cycleKeyCfg.Value, KeyCode.P);
            _panelKey = ParseKey(_panelKeyCfg.Value, KeyCode.F9);
            _dumpKey = ParseKey(_dumpKeyCfg.Value, KeyCode.None);

            _presets = defaults;
            for (int i = 0; i < Preset.Count; i++) _presets[i] = Preset.Parse(_presetCfg[i].Value, _presets[i]);
            _slot = Mathf.Clamp(_activePreset.Value, 1, Preset.Count);

            CamParams cur;
            if (CamParams.TryParse(_current.Value, out cur)) _rig.Target = cur;
            else _rig.Target = _presets[_slot - 1].Params;
            _rig.SnapParams();
            _rig.SetShoulder(_rig.Target.Right, false);
            ApplyRigSettings();

            _wantTp = _startInTp.Value;

            Patches.ApplyCore(HarmonyInstance, LoggerInstance);
            HookBeforeRender();
            _perfNextLog = Time.unscaledTime + 30f;

            _mipAlways = !string.Equals((_mipModeCfg.Value ?? "").Trim(), "Switch", StringComparison.OrdinalIgnoreCase);
            _mipGameValue = Game.MipLimit;
            MipTick(true);

            LoggerInstance.Msg("Third person v" + Version + " ready. " + _toggleKey + " = toggle view, " + _swapKey + " = swap shoulder, "
                             + _cycleKey + " = next preset, " + _panelKey + " = adjust panel"
                             + (_dumpKey != KeyCode.None ? ", " + _dumpKey + " = diagnostic dump" : "")
                             + ". Night vision: " + NvgLookLabel(_nvg.Look)
                             + ". Core patches bound: " + Patches.Bound + "/" + Patches.CoreWanted + " (panel input hooks install when the panel first opens)."
                             + " Before-render hook: " + (_beforeRenderHooked ? "on" : "off"));
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {

            Drop();
            if (_mipAlways) MipTick(true);
        }

        public override void OnUpdate()
        {
            long t0 = Stopwatch.GetTimestamp();
            try
            {
                float dt = Time.unscaledDeltaTime;
                PerfSample(dt);
                Acquire();
                HandleKeys();
                if (_panelOpen) PanelInput(dt);
                else if (Patches.InputHooksInstalled) Patches.ClearAll();
                Sync(dt);
                _nvg.Tick(_gameTp && _validNow, _rig.EffectiveBlend, _eye, LoggerInstance);
                if (_panel != null) _panel.Tick();
                if (_dirty && Time.unscaledTime >= _saveAt) SaveNow();
                if (_mipAlways && Time.unscaledTime >= _mipNextPoll) MipTick(false);
            }
            catch (Exception e)
            {
                LoggerInstance.Warning("update: " + e.Message);
            }
            _perfUpdateTicks += Stopwatch.GetTimestamp() - t0;
            _perfFrames++;
            if (Time.unscaledTime >= _perfNextLog) PerfLog();
        }

        public override void OnApplicationQuit()
        {
            if (_dirty) SaveNow();
        }

        private bool PrefixLive()
        {
            return Time.frameCount - _lastPrefixFrame <= 1;
        }

        public override void OnLateUpdate()
        {
            if (!_gameTp || PrefixLive()) return;
            Drive(false);
        }

        private static void OnBeforeRender()
        {
            var m = _instance;
            if (m == null || !m._gameTp) return;
            m._beforeRenderCalls++;
            if (m.PrefixLive()) return;
            m.Drive(true);
        }

        internal static void DriveBeforeCinemachine()
        {
            var m = _instance;
            if (m == null) return;
            m._lastPrefixFrame = Time.frameCount;
            if (!m._gameTp) return;
            m.Drive(false);
        }

        private bool Valid()
        {
            return _pn != null && Game.Alive(_pn) && Game.Alive(_tp) && Game.Alive(_cam) && Game.Alive(_eye);
        }

        private void Acquire()
        {
            if (_pn != null && !Valid())
            {

                Drop();
            }
            _validNow = _pn != null;
            if (_pn != null) return;
            if (Time.unscaledTime < _nextAcquire) return;
            _nextAcquire = Time.unscaledTime + 0.5f;

            var pn = Game.LocalPlayer();
            if (pn == null) { _warnedNoToggle = false; return; }

            var tp = Game.FindToggle(pn);
            if (tp == null)
            {
                if (!_warnedNoToggle) { _warnedNoToggle = true; LoggerInstance.Warning("No TpToggle under the local player; third person is unavailable on this character."); }
                return;
            }
            var cam = Game.TpCamera(tp);
            if (cam == null)
            {
                if (!_warnedNoToggle) { _warnedNoToggle = true; LoggerInstance.Warning("TpToggle found but its TPCamera is null; third person is unavailable."); }
                return;
            }

            _pn = pn;
            _tp = tp;
            _cam = cam;
            _cm = Game.CinemachineOn(cam);
            _eye = Game.EyeTransform(pn);
            _root = Game.PlayerRoot(pn);
            if (_panel != null) _panel.TryBorrowFont(_pn);

            _gameTp = Game.GameInThirdPerson(tp);
            var hidden = Game.HeadHidden(pn);
            _headShown = hidden.HasValue ? !hidden.Value : _gameTp;
            _rig.Blend = _gameTp ? 1f : 0f;
            _rig.ResetSmoothing();
            _lastDriveFrame = -1;
            _fovOverridden = false;
            _lastFovWritten = -1f;
            _validNow = true;

            LoggerInstance.Msg("Bound to " + SafeName(_pn) + ": toggle on " + Game.Path(SafeTransform(_tp)) + ", third-person camera " + Game.Path(SafeTransform(_cam))
                             + (_cm != null ? " (with Cinemachine lens)" : "") + ", eye " + Game.Path(_eye) + ", game in third person: " + _gameTp
                             + (_wantTp ? " - entering third person." : "."));
        }

        private void Drop()
        {
            if (_fovOverridden) { Game.SetGameFov(_nativeFov); _fovOverridden = false; }
            if (_dirty) SaveNow();
            if (!_mipAlways) ApplyMipBoost(false);
            _clipMatched = false;
            _validNow = false;
            _nvg.Disengage(null);
            _nvg.Forget();
            _pn = null; _tp = null; _cam = null; _cm = null; _eye = null; _root = null;
            _gameTp = false;
            _headShown = false;
            _rig.Blend = 0f;
            _rig.ResetSmoothing();
            _lastDriveFrame = -1;
            if (_panelOpen) ClosePanel(false);
            Patches.ClearAll();
        }

        private void Sync(float dt)
        {
            if (!_validNow)
            {
                _rig.Tick(dt, false);
                return;
            }

            if (!_wantTp && !_gameTp && _rig.Blend <= 0f)
            {
                _rig.Tick(dt, false);
                if (Time.unscaledTime >= _nextTogglePoll)
                {
                    _nextTogglePoll = Time.unscaledTime + 0.5f;
                    if (Game.GameInThirdPerson(_tp)) AdoptGameToggle(true, dt);
                }
                return;
            }

            bool suppress = (_adsToFp.Value && Game.IsAds(_pn))
                         || (_fpInVehicles.Value && Game.InVehicle(_pn))
                         || Game.FreeCamOn(_pn);
            bool effective = _enabled.Value && _wantTp && !suppress;

            bool gameNow = _gameTp;
            if (Time.unscaledTime >= _nextTogglePoll)
            {
                _nextTogglePoll = Time.unscaledTime + 0.5f;
                gameNow = Game.GameInThirdPerson(_tp);
            }
            if (gameNow != _gameTp)
            {
                AdoptGameToggle(gameNow, dt);
                effective = _enabled.Value && _wantTp && !suppress;
            }

            _rig.Tick(dt, effective);

            bool gameShouldBeTp = effective || _rig.Blend > 0.001f;
            if (gameShouldBeTp != _gameTp)
            {
                if (gameShouldBeTp) EnterTp(); else ExitTp();
            }

            if (_gameTp)
            {

                bool show = _rig.Blend > 0.5f && !_rig.HeadTooClose;
                if (show != _headShown && Time.unscaledTime - _headChangedAt >= 0.25f)
                {
                    Game.ShowHead(_pn, show);
                    _headShown = show;
                    _headChangedAt = Time.unscaledTime;
                }
                else if (show && Time.unscaledTime > _nextHeadCheck)
                {

                    _nextHeadCheck = Time.unscaledTime + 0.5f;
                    if (Game.HeadHidden(_pn) == true) Game.ShowHead(_pn, true);
                }
            }
        }

        private void EnterTp()
        {
            _nativeFov = ReadNativeFov();

            try { _cam.transform.SetPositionAndRotation(_eye.position, _eye.rotation); } catch { }
            Game.SetCameraFov(_cam, _cm, _nativeFov);

            if (!Game.Toggle(_tp, LoggerInstance))
            {

                _gameTp = Game.GameInThirdPerson(_tp);
                _wantTp = _gameTp;
                _rig.Blend = _gameTp ? 1f : 0f;
                Toast("THIRD PERSON UNAVAILABLE", "see the console for the reason", 3f);
                return;
            }
            _gameTp = true;
            _rig.ResetSmoothing();
            _lastDriveFrame = -1;
            _lastFovWritten = -1f;
            EnsurePanel();
            PerfMark("third person");
            ApplyTpLens();
            if (!_mipAlways) ApplyMipBoost(true);

            Game.ShowHead(_pn, false);
            _headShown = false;
            _nextHeadCheck = Time.unscaledTime + 0.5f;
        }

        private void ExitTp()
        {
            _nvg.Disengage(LoggerInstance);
            RestoreTpLens();
            if (!_mipAlways) ApplyMipBoost(false);
            PerfMark("first person");
            if (Game.Toggle(_tp, LoggerInstance)) _gameTp = false;
            else
            {
                _gameTp = Game.GameInThirdPerson(_tp);
                if (_gameTp) { _wantTp = true; _rig.Blend = 1f; }
            }
            if (_fovOverridden) { Game.SetGameFov(_nativeFov); _fovOverridden = false; }
            _headShown = false;
            _lastDriveFrame = -1;
        }

        private void ApplyTpLens()
        {
            if (!_matchClip.Value || _cm == null || _eye == null) return;
            float near, far;
            if (!Game.ReadClipPlanes(_eye, out near, out far)) return;
            if (!_clipMatched)
            {
                if (Game.GetClipPlanes(_cm, out _tpNearOrig, out _tpFarOrig)) _clipMatched = true;
            }
            Game.SetClipPlanes(_cm, near, far);
        }

        private void RestoreTpLens()
        {
            if (!_clipMatched) return;
            _clipMatched = false;
            if (_cm != null && Game.Alive(_cm)) Game.SetClipPlanes(_cm, _tpNearOrig, _tpFarOrig);
        }

        private void MipTick(bool force)
        {
            _mipNextPoll = Time.unscaledTime + 1f;
            int boost = Mathf.Clamp(_mipBoost.Value, 0, 3);
            if (boost <= 0) return;
            int cur = Game.MipLimit;
            if (cur != _mipLastWritten) _mipGameValue = cur;
            int want = _mipGameValue + boost;
            if (cur == want && !force) return;
            if (cur == want) return;
            Game.MipLimit = want;
            _mipLastWritten = want;
            LoggerInstance.Msg("Texture mip limit " + cur + " -> " + want + " (held for the session).");
        }

        private void ApplyMipBoost(bool on)
        {
            int boost = Mathf.Clamp(_mipBoost.Value, 0, 3);
            if (on)
            {
                if (boost <= 0 || _mipApplied) return;
                _mipBase = Game.MipLimit;
                Game.MipLimit = _mipBase + boost;
                _mipApplied = true;
                LoggerInstance.Msg("Texture mip limit " + _mipBase + " -> " + (_mipBase + boost) + " for third person.");
            }
            else if (_mipApplied)
            {
                _mipApplied = false;
                Game.MipLimit = _mipBase;
                LoggerInstance.Msg("Texture mip limit restored to " + _mipBase + ".");
            }
        }

        private float ReadNativeFov()
        {
            float f = Game.GameFov();
            if (f < 10f) f = Game.MainCameraFov();
            if (f < 10f) f = 60f;
            return f;
        }

        private void Drive(bool fromBeforeRender)
        {
            if (!_gameTp) return;
            long t0 = Stopwatch.GetTimestamp();
            _perfDriveCalls++;
            try
            {
                if (!Valid()) return;

                int frame = Time.frameCount;
                bool first = frame != _lastDriveFrame;
                _lastDriveFrame = frame;
                if (!fromBeforeRender) _lateUpdateWrites++;

                Vector3 eyePos = _eye.position;
                Quaternion eyeRot = _eye.rotation;

                Vector3 pos; Quaternion rot;
                _rig.Solve(eyePos, eyeRot, _root, Time.unscaledDeltaTime, first, out pos, out rot);
                _cam.transform.SetPositionAndRotation(pos, rot);

                float b = _rig.Blend;
                float e = b * b * (3f - 2f * b);
                float fov = _rig.Applied.Fov > 0f ? Mathf.Lerp(_nativeFov, _rig.Applied.Fov, e) : _nativeFov;

                if (Mathf.Abs(fov - _lastFovWritten) > 0.01f || Time.unscaledTime >= _nextFovReassert)
                {
                    Game.SetCameraFov(_cam, _cm, fov);
                    _lastFovWritten = fov;
                    _nextFovReassert = Time.unscaledTime + 0.5f;
                }

                if (_rig.Applied.Fov > 0f)
                {

                    Game.SetGameFov(fov);
                    _fovOverridden = true;
                }
                else if (_fovOverridden)
                {
                    Game.SetGameFov(_nativeFov);
                    _fovOverridden = false;
                }
            }
            catch (Exception ex)
            {
                if (_lateUpdateWrites % 300 == 1) LoggerInstance.Warning("drive: " + ex.Message);
            }
            _perfDriveTicks += Stopwatch.GetTimestamp() - t0;
        }

        private void AdoptGameToggle(bool gameNow, float dt)
        {
            LoggerInstance.Msg("Game switched to " + (gameNow ? "third" : "first") + " person on its own; following.");
            _gameTp = gameNow;
            _wantTp = gameNow;
            _rig.Blend = gameNow ? 1f : 0f;
            _rig.ResetSmoothing();
            _headShown = gameNow;
            _lastFovWritten = -1f;
            if (gameNow) { _nativeFov = ReadNativeFov(); PerfMark("third person"); }
        }

        private void HandleKeys()
        {

            try { if (!Input.anyKeyDown) return; } catch { }

            bool console = Game.ConsoleOpen();
            bool typing = console || Cursor.visible;

            if (!console && Input.GetKeyDown(_toggleKey)) ToggleWant();
            if (!typing && _swapKey != KeyCode.None && Input.GetKeyDown(_swapKey)) SwapShoulder();
            if (!typing && _cycleKey != KeyCode.None && Input.GetKeyDown(_cycleKey)) LoadPreset(_slot % Preset.Count + 1, true);
            if (!console && _panelKey != KeyCode.None && Input.GetKeyDown(_panelKey)) TogglePanel();
            if (!console && _dumpKey != KeyCode.None && Input.GetKeyDown(_dumpKey)) Dump();
        }

        private void ToggleWant()
        {
            _wantTp = !_wantTp;
            if (_wantTp)
            {
                if (!_validNow) Toast("THIRD PERSON", "waiting for your character", 2f);
                else Toast("THIRD PERSON", _swapKey + "  shoulder     " + _cycleKey + "  preset     " + _panelKey + "  adjust", 3f);
            }
            else Toast("FIRST PERSON", null, 1.2f);
            LoggerInstance.Msg(_wantTp ? "Third person on." : "Third person off.");
        }

        private void SwapShoulder()
        {
            _rig.Target.Right = !_rig.Target.Right;
            _rig.SetShoulder(_rig.Target.Right, _gameTp);
            Persist();
            Toast(_rig.Target.Right ? "RIGHT SHOULDER" : "LEFT SHOULDER", null, 1.2f);
            if (_panelOpen) RefreshPanel();
        }

        private void LoadPreset(int slot, bool announce)
        {
            slot = Mathf.Clamp(slot, 1, Preset.Count);
            _slot = slot;
            var p = _presets[slot - 1];
            _rig.Target = p.Params.Clamped();
            _rig.SetShoulder(_rig.Target.Right, _gameTp);
            Persist();
            if (announce) Toast("PRESET " + slot, p.Name.ToUpperInvariant(), 1.6f);
            LoggerInstance.Msg("Loaded preset " + slot + " (" + p.Name + "): " + p.Params.Serialize());
            if (_panelOpen) RefreshPanel();
        }

        private void SavePreset(int slot)
        {
            slot = Mathf.Clamp(slot, 1, Preset.Count);
            _presets[slot - 1].Params = _rig.Target.Clamped();
            _presetCfg[slot - 1].Value = _presets[slot - 1].Serialize();
            _slot = slot;
            Persist();
            Toast("SAVED TO SLOT " + slot, _presets[slot - 1].Name.ToUpperInvariant(), 1.8f);
            LoggerInstance.Msg("Saved preset " + slot + " (" + _presets[slot - 1].Name + "): " + _presets[slot - 1].Params.Serialize());
            if (_panelOpen) RefreshPanel();
        }

        private void Persist()
        {
            try
            {
                _current.Value = _rig.Target.Serialize();
                _activePreset.Value = _slot;
            }
            catch (Exception e) { LoggerInstance.Warning("save: " + e.Message); }
            _dirty = true;
            _saveAt = Time.unscaledTime + 2f;
        }

        private void SaveNow()
        {
            _dirty = false;
            try { MelonPreferences.Save(); }
            catch (Exception e) { LoggerInstance.Warning("save: " + e.Message); }
        }

        private void ApplyRigSettings()
        {
            _nvg.Look = ParseNvgLook(_nvgLookCfg.Value);
            _rig.SqueezeIn = _squeezeToEye.Value ? 0.45f : 0f;
            _rig.PositionSmoothing = Mathf.Clamp(_posSmooth.Value, 0f, 60f);
            _rig.RotationSmoothing = Mathf.Clamp(_rotSmooth.Value, 0f, 60f);
            _rig.SwapSeconds = Mathf.Clamp(_swapSeconds.Value, 0.05f, 2f);
            _rig.BlendSeconds = Mathf.Clamp(_blendSeconds.Value, 0.02f, 2f);
            _rig.Collision = _collision.Value;
            _rig.CollisionRadius = Mathf.Clamp(_collisionRadius.Value, 0.02f, 0.6f);
            _rig.MinDistance = Mathf.Clamp(_minDistance.Value, 0.1f, 2f);
        }

        private void TogglePanel()
        {
            if (_panelOpen) ClosePanel(true); else OpenPanel();
        }

        private void OpenPanel()
        {
            EnsurePanel();
            int hooks = Patches.EnsureInputHooks(HarmonyInstance, LoggerInstance);
            if (hooks < Patches.InputWanted)
                Toast("PANEL INPUT NOT FULLY FROZEN", "look, move or fire may leak through; see the console", 3f);
            _panelOpen = true;
            _row = 0;
            _holdTimer = 0f;
            _holdDir = 0;
            _panel.SetVisible(true);
            RefreshPanel();
            if (!_wantTp) Toast("PANEL OPEN", "press " + _toggleKey + " for third person to see the camera move", 3f);
        }

        private void ClosePanel(bool persist)
        {
            _panelOpen = false;
            if (_panel != null) _panel.SetVisible(false);
            Patches.ClearAll();
            if (persist) { Persist(); SaveNow(); }
        }

        private void EnsurePanel()
        {
            if (_panel != null) return;
            _panel = new Panel(RowLabels);
            _panel.SetScale(_panelScale.Value);
            if (_pn != null && Game.Alive(_pn)) _panel.TryBorrowFont(_pn);
        }

        private void Toast(string main, string sub, float seconds)
        {
            EnsurePanel();
            _panel.Toast(main, sub, seconds);
        }

        private void PanelInput(float dt)
        {

            Patches.FreezeLook = true;
            Patches.FreezeMove = true;
            Patches.BlockFire = true;

            bool changed = false;

            float mx = 0f, my = 0f, wheel = 0f;
            try { mx = Input.GetAxis("Mouse X"); my = Input.GetAxis("Mouse Y"); wheel = Input.GetAxis("Mouse ScrollWheel"); } catch { }

            if (mx != 0f || my != 0f)
            {
                float sens = Mathf.Clamp(_mouseSens.Value, 0.001f, 0.2f);
                float signed = (_rig.Target.Right ? 1f : -1f) * _rig.Target.Side + mx * sens;
                bool right = signed >= 0f;
                _rig.Target.Side = Mathf.Clamp(Mathf.Abs(signed), CamParams.SideMin, CamParams.SideMax);
                if (right != _rig.Target.Right)
                {

                    _rig.Target.Right = right;
                    _rig.SetShoulder(right, false);
                }
                _rig.Target.Height = Mathf.Clamp(_rig.Target.Height + my * sens, CamParams.HeightMin, CamParams.HeightMax);
                changed = true;
            }
            if (wheel != 0f)
            {
                _rig.Target.Distance = Mathf.Clamp(_rig.Target.Distance - wheel * 0.6f, CamParams.DistanceMin, CamParams.DistanceMax);
                changed = true;
            }

            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.Keypad8)) { _row = (_row - 1 + RowN) % RowN; changed = true; }
            if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.Keypad2)) { _row = (_row + 1) % RowN; changed = true; }

            int tap = 0;
            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.Keypad4)) tap = -1;
            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.Keypad6)) tap = 1;
            if (tap != 0)
            {
                Adjust(_row, tap, false, 0f);
                _holdTimer = 0f;
                _holdDir = tap;
                changed = true;
            }
            else
            {
                int held = 0;
                if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.Keypad4)) held = -1;
                if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.Keypad6)) held = 1;
                if (held != 0 && held == _holdDir)
                {
                    _holdTimer += dt;
                    if (_holdTimer > 0.35f) { Adjust(_row, held, true, dt); changed = true; }
                }
                else { _holdTimer = 0f; _holdDir = 0; }
            }

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) { Activate(_row); changed = true; }
            if (Input.GetKeyDown(KeyCode.Home))
            {
                _rig.Target = CamParams.Default();
                _rig.SetShoulder(_rig.Target.Right, _gameTp);
                Toast("CAMERA RESET", "defaults restored", 1.5f);
                changed = true;
            }

            if (changed) RefreshPanel();
        }

        private void Adjust(int row, int dir, bool held, float dt)
        {
            float f = held ? dt : 1f;
            switch (row)
            {
                case RowDistance:  _rig.Target.Distance = Mathf.Clamp(_rig.Target.Distance + dir * (held ? 1.2f : 0.05f) * f, CamParams.DistanceMin, CamParams.DistanceMax); break;
                case RowSide:      _rig.Target.Side = Mathf.Clamp(_rig.Target.Side + dir * (held ? 0.6f : 0.02f) * f, CamParams.SideMin, CamParams.SideMax); break;
                case RowHeight:    _rig.Target.Height = Mathf.Clamp(_rig.Target.Height + dir * (held ? 0.6f : 0.02f) * f, CamParams.HeightMin, CamParams.HeightMax); break;
                case RowTiltPitch: _rig.Target.TiltPitch = Mathf.Clamp(_rig.Target.TiltPitch + dir * (held ? 25f : 0.5f) * f, CamParams.TiltMin, CamParams.TiltMax); break;
                case RowTiltYaw:   _rig.Target.TiltYaw = Mathf.Clamp(_rig.Target.TiltYaw + dir * (held ? 25f : 0.5f) * f, CamParams.TiltMin, CamParams.TiltMax); break;
                case RowFov:
                {
                    float v = _rig.Target.Fov;
                    if (v <= 0f) { v = dir > 0 ? CamParams.FovMin : 0f; }
                    else
                    {
                        v += dir * (held ? 20f : 1f) * f;
                        if (v < CamParams.FovMin) v = 0f;
                        if (v > CamParams.FovMax) v = CamParams.FovMax;
                    }
                    _rig.Target.Fov = v;
                    break;
                }
                case RowConverge:  if (!held) _rig.Target.Converge = !_rig.Target.Converge; break;
                case RowShoulder:  if (!held) SwapShoulder(); break;
                case RowSmoothing:
                {
                    float v = Mathf.Clamp(_posSmooth.Value + dir * (held ? 12f : 1f) * f, 0f, 40f);
                    _posSmooth.Value = v;
                    _rotSmooth.Value = Mathf.Clamp(v * 1.3f, 0f, 60f);
                    ApplyRigSettings();
                    break;
                }
                case RowSwap:      _swapSeconds.Value = Mathf.Clamp(_swapSeconds.Value + dir * (held ? 0.5f : 0.05f) * f, 0.05f, 2f); ApplyRigSettings(); break;
                case RowAds:       if (!held) _adsToFp.Value = !_adsToFp.Value; break;
                case RowVehicle:   if (!held) _fpInVehicles.Value = !_fpInVehicles.Value; break;
                case RowCollision: if (!held) { _collision.Value = !_collision.Value; ApplyRigSettings(); } break;
                case RowNvg:
                    if (!held)
                    {
                        int n = (int)ParseNvgLook(_nvgLookCfg.Value) + dir;
                        if (n < 0) n = 1;
                        if (n > 1) n = 0;
                        _nvgLookCfg.Value = ((NvgLook)n).ToString();
                        ApplyRigSettings();
                    }
                    break;
                case RowSlot:
                case RowSave:      if (!held) _slot = ((_slot - 1 + dir + Preset.Count) % Preset.Count) + 1; break;
                case RowReset:     break;
            }
        }

        private void Activate(int row)
        {
            switch (row)
            {
                case RowSlot:  LoadPreset(_slot, true); break;
                case RowSave:  SavePreset(_slot); break;
                case RowReset:
                    _rig.Target = CamParams.Default();
                    _rig.SetShoulder(_rig.Target.Right, _gameTp);
                    Toast("CAMERA RESET", "defaults restored", 1.5f);
                    break;
                case RowConverge: case RowShoulder: case RowAds: case RowVehicle: case RowCollision: case RowNvg:
                    Adjust(row, 1, false, 0f);
                    break;
            }
        }

        private void RefreshPanel()
        {
            if (_panel == null) return;
            var t = _rig.Target;
            _panel.SetValue(RowDistance, t.Distance.ToString("0.00") + " m");
            _panel.SetValue(RowSide, t.Side.ToString("0.00") + " m");
            _panel.SetValue(RowHeight, (t.Height >= 0f ? "+" : "") + t.Height.ToString("0.00") + " m");
            _panel.SetValue(RowTiltPitch, t.TiltPitch.ToString("0.0") + " deg");
            _panel.SetValue(RowTiltYaw, t.TiltYaw.ToString("0.0") + " deg");
            _panel.SetValue(RowFov, t.Fov <= 0f ? "game (" + _nativeFov.ToString("0") + ")" : t.Fov.ToString("0"));
            _panel.SetValue(RowConverge, t.Converge ? "on" : "off");
            _panel.SetValue(RowShoulder, t.Right ? "right" : "left");
            _panel.SetValue(RowSmoothing, _posSmooth.Value <= 0f ? "rigid" : _posSmooth.Value.ToString("0"));
            _panel.SetValue(RowSwap, _swapSeconds.Value.ToString("0.00") + " s");
            _panel.SetValue(RowAds, _adsToFp.Value ? "on" : "off");
            _panel.SetValue(RowVehicle, _fpInVehicles.Value ? "on" : "off");
            _panel.SetValue(RowCollision, _collision.Value ? "on" : "off");
            _panel.SetValue(RowNvg, NvgLookLabel(ParseNvgLook(_nvgLookCfg.Value)));
            _panel.SetValue(RowSlot, "<  " + _slot + " : " + _presets[_slot - 1].Name + "  >   Enter = load");
            _panel.SetValue(RowSave, "slot " + _slot + "   Enter = save");
            _panel.SetValue(RowReset, "Enter");
            _panel.SetSelected(_row);
            _panel.SetHint("mouse moves the camera   wheel sets distance   arrows select and adjust   Enter applies   " + _panelKey + " closes");

            string state = !_validNow ? "No character bound yet."
                         : !_wantTp ? "Third person is off (" + _toggleKey + ")."
                         : _gameTp ? "Third person live." + (_rig.Collided ? " Camera pulled in by a wall." : "")
                         : "Third person suppressed (aiming, vehicle or free camera).";
            _panel.SetFooter(state + "  Home resets. " + _swapKey + " swaps shoulder, " + _cycleKey + " cycles presets. Settings save when the panel closes.");
        }

        private void HookBeforeRender()
        {
            _beforeRenderHooked = false;
            try
            {
                var mi = typeof(Application).GetMethod("add_onBeforeRender", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (mi == null) { LoggerInstance.Warning("Application.add_onBeforeRender not found; using LateUpdate only."); return; }
                _beforeRenderAction = new Action(OnBeforeRender);
                mi.Invoke(null, new object[] { _beforeRenderAction });
                _beforeRenderHooked = true;
            }
            catch (Exception e)
            {
                LoggerInstance.Warning("before-render hook failed (" + e.Message + "); using LateUpdate only.");
            }
        }

        private void PerfSample(float dt)
        {
            _perfT[_perfHead] = Time.unscaledTime;
            _perfDt[_perfHead] = dt;
            _perfHead = (_perfHead + 1) % PerfN;

            if (_perfSwitchAt >= 0f && Time.unscaledTime >= _perfSwitchAt + 5f)
            {
                if (_perfLog.Value) LoggerInstance.Msg("[perf] " + _perfBefore + " | after: " + PerfWindow(_perfSwitchAt, _perfSwitchAt + 5f));
                _perfSwitchAt = -1f;
            }
        }

        private void PerfMark(string to)
        {
            float now = Time.unscaledTime;
            _perfBefore = "switch to " + to + ": 5 s before: " + PerfWindow(now - 5f, now);
            _perfSwitchAt = now;
        }

        private string PerfWindow(float from, float to)
        {
            int n = 0; float sum = 0f, worst = 0f; int over33 = 0, over100 = 0;
            for (int i = 0; i < PerfN; i++)
            {
                float t = _perfT[i];
                if (t <= 0f || t < from || t > to) continue;
                float dt = _perfDt[i];
                n++; sum += dt; if (dt > worst) worst = dt; if (dt > 0.0333f) over33++; if (dt > 0.1f) over100++;
            }
            if (n == 0 || sum <= 0f) return "no samples";
            return (n / sum).ToString("0") + " fps avg, worst " + (worst * 1000f).ToString("0") + " ms, " + over33 + " frames >33 ms, " + over100 + " >100 ms";
        }

        private void PerfLog()
        {
            _perfNextLog = Time.unscaledTime + 30f;
            if (_perfLog.Value && _perfFrames > 0)
            {
                double usPerTick = 1000000.0 / Stopwatch.Frequency;
                double upd = _perfUpdateTicks * usPerTick / _perfFrames;
                double drv = _perfDriveTicks * usPerTick / _perfFrames;
                int getKey = Patches.GetKeyCalls - _perfGetKeyAtLog;
                LoggerInstance.Msg("[perf] 30 s: update " + upd.ToString("0.0") + " us/frame, camera " + drv.ToString("0.0") + " us/frame ("
                    + (_perfDriveCalls / (float)_perfFrames).ToString("0.00") + " writes/frame), third person " + (_gameTp ? "on" : "off")
                    + ", panel hooks " + (Patches.InputHooksInstalled ? "installed, " + (getKey / (float)_perfFrames).ToString("0") + " GetKey calls/frame" : "not installed"));
            }
            _perfGetKeyAtLog = Patches.GetKeyCalls;
            _perfUpdateTicks = 0; _perfDriveTicks = 0; _perfDriveCalls = 0; _perfFrames = 0;
        }

        private void Dump()
        {
            var L = LoggerInstance;
            try
            {
                L.Msg("[dump] enabled=" + _enabled.Value + " wantTp=" + _wantTp + " gameTp=" + _gameTp + " blend=" + _rig.Blend.ToString("0.00")
                    + " shoulder=" + _rig.ShoulderBlend.ToString("0.00") + " slot=" + _slot + " target=" + _rig.Target.Serialize()
                    + " applied=" + _rig.Applied.Serialize() + " focal=" + _rig.Focal.ToString("0.0") + " collided=" + _rig.Collided
                    + " panel=" + _panelOpen + " patches=" + Patches.Bound + " (core " + Patches.CoreWanted + ", input " + (Patches.InputHooksInstalled ? Patches.InputHooksBound + "/" + Patches.InputWanted : "not installed") + ")"
                    + " beforeRender=" + (_beforeRenderHooked ? "hooked" : "off") + " beforeRenderCalls=" + _beforeRenderCalls
                    + " cinemachinePrefixCalls=" + Patches.CinemachinePrefixCalls + " prefixLive=" + PrefixLive() + " lateUpdateWrites=" + _lateUpdateWrites);
                L.Msg("[dump] last 5 s: " + PerfWindow(Time.unscaledTime - 5f, Time.unscaledTime));

                var pn = Game.LocalPlayer();
                if (pn == null) { L.Msg("[dump] no local spawned player (PlayerMaster.MyPlayerMaster.PlayerSpawnedObject is null)"); return; }
                L.Msg("[dump] player " + SafeName(pn) + " root=" + Game.Path(Game.PlayerRoot(pn)) + " isOwned=" + Safe(() => pn.isOwned.ToString())
                    + " inThirdPerson=" + Safe(() => pn.inThirdPerson.ToString()) + " isADS=" + Safe(() => pn.isADS.ToString())
                    + " helicopter=" + Safe(() => (pn.helicopter != null).ToString()) + " freecam=" + Game.FreeCamOn(pn)
                    + " inputDisabled=" + Game.InputDisabled() + " cursorVisible=" + Cursor.visible + " lock=" + Cursor.lockState);

                var eye = Game.EyeTransform(pn);
                L.Msg("[dump] eye " + Game.Path(eye) + " pos=" + Safe(() => eye.position.ToString("0.00")) + " euler=" + Safe(() => eye.eulerAngles.ToString("0.0"))
                    + " components=[" + Safe(() => Game.Components(eye.gameObject)) + "]");
                try
                {
                    var fc = pn.characterCameraFollower;
                    if (fc != null)
                        L.Msg("[dump] follow-camera on " + Game.Path(fc.transform) + " target=" + Game.Path(fc.Target) + " targetNoAuth=" + Game.Path(fc.TargetNoAuth)
                            + " inVehicle=" + fc.InVehicle + " offset=" + fc.positionOffset.ToString("0.00"));
                    else L.Msg("[dump] follow-camera: null");
                }
                catch (Exception e) { L.Msg("[dump] follow-camera unreadable: " + e.Message); }

                var tp = Game.FindToggle(pn);
                if (tp == null) { L.Msg("[dump] TpToggle: NOT FOUND under the player"); }
                else
                {
                    L.Msg("[dump] TpToggle on " + Game.Path(SafeTransform(tp)) + " inThirdPerson=" + Game.GameInThirdPerson(tp));
                    try
                    {
                        var fps = tp.FPSCamera;
                        L.Msg("[dump]   FPSCamera=" + (fps == null ? "null" : Game.Path(fps.transform) + " active=" + fps.activeInHierarchy + " [" + Game.Components(fps) + "]"));
                    }
                    catch (Exception e) { L.Msg("[dump]   FPSCamera unreadable: " + e.Message); }
                    try
                    {
                        var c = tp.TPCamera;
                        if (c == null) L.Msg("[dump]   TPCamera=null");
                        else
                        {
                            L.Msg("[dump]   TPCamera=" + Game.Path(c.transform) + " enabled=" + c.enabled + " activeInHierarchy=" + c.gameObject.activeInHierarchy
                                + " fov=" + c.fieldOfView.ToString("0.0") + " near=" + c.nearClipPlane + " localPos=" + c.transform.localPosition.ToString("0.00")
                                + " tag=" + c.tag + " [" + Game.Components(c.gameObject) + "]");
                            var parent = c.transform.parent;
                            L.Msg("[dump]   TPCamera parent=" + Game.Path(parent) + (parent == null ? "" : " activeSelf=" + parent.gameObject.activeSelf + " [" + Game.Components(parent.gameObject) + "]"));
                        }
                    }
                    catch (Exception e) { L.Msg("[dump]   TPCamera unreadable: " + e.Message); }
                    L.Msg("[dump]   TPRoot=" + Safe(() => NameActive(tp.TPRoot)) + " FPRoot=" + Safe(() => NameActive(tp.FPRoot))
                        + " Legs=" + Safe(() => NameActive(tp.Legs)) + " Feet=" + Safe(() => NameActive(tp.Feet)));
                }

                try
                {
                    var mc = Il2Cpp.MainCameraSingleton.MainCam;
                    L.Msg("[dump] MainCameraSingleton.MainCam=" + (mc == null ? "null" : Game.Path(mc.transform) + " enabled=" + mc.enabled + " fov=" + mc.fieldOfView.ToString("0.0")));
                }
                catch (Exception e) { L.Msg("[dump] MainCam unreadable: " + e.Message); }
                try
                {
                    var main = Camera.main;
                    L.Msg("[dump] Camera.main=" + (main == null ? "null" : Game.Path(main.transform) + " fov=" + main.fieldOfView.ToString("0.0")));
                }
                catch (Exception e) { L.Msg("[dump] Camera.main unreadable: " + e.Message); }

                try
                {
                    var brains = UnityEngine.Object.FindObjectsOfType<Unity.Cinemachine.CinemachineBrain>();
                    L.Msg("[dump] cinemachine brains: " + (brains == null ? 0 : brains.Length));
                    if (brains != null)
                        for (int i = 0; i < brains.Length; i++)
                        {
                            var b = brains[i];
                            if (b == null) continue;
                            string live = "none";
                            try { var v = b.ActiveVirtualCamera; if (v != null) { var vb = v.TryCast<Unity.Cinemachine.CinemachineVirtualCameraBase>(); live = vb != null ? Game.Path(vb.transform) : "(non-vcam)"; } } catch { }
                            L.Msg("[dump]   brain " + Game.Path(b.transform) + " enabled=" + Safe(() => b.enabled.ToString()) + " output=" + Safe(() => Game.Path(b.OutputCamera.transform))
                                + " live=" + live + " blending=" + Safe(() => b.IsBlending.ToString()) + " update=" + Safe(() => b.UpdateMethod.ToString()));
                        }
                }
                catch (Exception e) { L.Msg("[dump] brains unreadable: " + e.Message); }

                try
                {
                    var cams = pn.cinemachineCameras;
                    L.Msg("[dump] player cinemachine cameras: " + (cams == null ? 0 : cams.Length));
                    if (cams != null)
                        for (int i = 0; i < cams.Length; i++)
                        {
                            var c = cams[i];
                            if (c == null) { L.Msg("[dump]   #" + i + " null"); continue; }
                            L.Msg("[dump]   #" + i + " " + Game.Path(c.transform) + " enabled=" + c.enabled + " active=" + c.gameObject.activeInHierarchy + " fov=" + c.Lens.FieldOfView.ToString("0.0"));
                        }
                }
                catch (Exception e) { L.Msg("[dump] player cinemachine cameras unreadable: " + e.Message); }

                try
                {
                    var arr = pn.AuthShadowedParents;
                    L.Msg("[dump] AuthShadowedParents: " + (arr == null ? 0 : arr.Length));
                    if (arr != null)
                        for (int i = 0; i < arr.Length; i++)
                        {
                            var go = arr[i];
                            if (go == null) { L.Msg("[dump]   #" + i + " null"); continue; }
                            var r = go.GetComponentInChildren<Renderer>(true);
                            L.Msg("[dump]   #" + i + " " + go.name + " active=" + go.activeInHierarchy + " renderer=" + (r == null ? "none" : r.name + " mode=" + r.shadowCastingMode));
                        }
                }
                catch (Exception e) { L.Msg("[dump] AuthShadowedParents unreadable: " + e.Message); }

                L.Msg("[dump] GameManager CameraFOV=" + Game.GameFov().ToString("0.0") + " nativeFov(held)=" + _nativeFov.ToString("0.0") + " fovOverridden=" + _fovOverridden
                    + " squeeze=" + _rig.Squeeze.ToString("0.00") + " camDistance=" + _rig.CameraDistance.ToString("0.00") + " headTooClose=" + _rig.HeadTooClose
                    + " evade=" + _rig.Evade.ToString("0.00") + " clipMatched=" + _clipMatched + " mipMode=" + (_mipAlways ? "always" : "switch")
                    + " mipApplied=" + _mipApplied + " mipLimit=" + Game.MipLimit + " mipGameValue=" + _mipGameValue);
                L.Msg("[dump] nvg: gogglesDown=" + Nvg.GogglesDown(pn) + " " + _nvg.Describe());
            }
            catch (Exception e) { L.Warning("dump: " + e.Message); }
        }

        private static string NameActive(GameObject go)
        {
            return go == null ? "null" : go.name + "(" + (go.activeInHierarchy ? "active" : "inactive") + ")";
        }

        private static string Safe(Func<string> f)
        {
            try { return f() ?? "null"; } catch (Exception e) { return "<" + e.GetType().Name + ">"; }
        }

        private static string SafeName(UnityEngine.Object o)
        {
            try { return o == null ? "null" : o.name; } catch { return "?"; }
        }

        private static Transform SafeTransform(Component c)
        {
            try { return c == null ? null : c.transform; } catch { return null; }
        }

        private static NvgLook ParseNvgLook(string s)
        {
            if (string.IsNullOrEmpty(s)) return NvgLook.CameraGoggles;
            s = s.Trim();
            if (string.Equals(s, "Off", StringComparison.OrdinalIgnoreCase)) return NvgLook.Off;

            return NvgLook.CameraGoggles;
        }

        private static string NvgLookLabel(NvgLook look)
        {
            switch (look)
            {
                case NvgLook.Off: return "game default";
                case NvgLook.CameraGoggles: return "camera goggles";
                default: return "camera goggles";
            }
        }

        private static KeyCode ParseKey(string s, KeyCode fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            try { KeyCode k; if (Enum.TryParse<KeyCode>(s.Trim(), true, out k)) return k; } catch { }
            return fallback;
        }
    }
}
