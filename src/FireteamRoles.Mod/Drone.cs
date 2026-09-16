// ODG Operations - FireteamRoles
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace FireteamRoles
{

    internal static class Drone
    {
        enum Mode { None, Thrown, Flying, Hover, Dead }

        static Mode _mode = Mode.None;
        static GameObject _root, _camGo;
        static Rigidbody _rb;
        static Collider _col;
        static Camera _cam;
        static Transform _camT;
        static AudioSource _rotor, _monitor;
        static GameObject _monitorGo;

        static bool _view;
        static float _armedAt, _battery, _launchedAt, _feedUntil, _deadAt, _nextPub, _colAt;
        static float _aimPitch;
        static float _yaw, _thrustCmd, _strafeCmd, _climbCmd, _throttle, _tilt;
        static float _leanP, _leanR, _lastYawRate, _yawRateSm;
        static bool _turbo;
        static Vector3 _lastVel; static bool _lastVelValid;
        static int _uses;
        static string _deathNote = "";
        static int _seq;

        static Camera _mainCam; static int _mainMask; static bool _mainMasked;

        static Il2Cpp.ISR _isr;
        static int _savedMode; static bool _savedB, _savedW, _thermalOn;
        static readonly List<UnityEngine.Rendering.Volume> _globalised = new List<UnityEngine.Rendering.Volume>();
        static bool _dumped;

        static bool _borrowed, _plainCam;
        static Il2Cpp.ISR _isrCam;
        static Transform _camHome; static Vector3 _camHomePos; static Quaternion _camHomeRot;
        static RenderTexture _camHomeRt; static float _camHomeFov, _camHomeNear, _camHomeFar, _camHomeDepth; static bool _camHomeEnabled, _camHomeOrtho;
        static RenderTexture _feed;
        static bool _camHomeActive, _camListenerOff, _forcePlain;
        static float _probeAt; static int _probes;
        static bool _firstFixed, _firstHud, _firstAudio, _firstPublish;

        internal static bool BorrowingIsr { get { return _borrowed; } }

        internal static void AfterIsrUpdate(Il2Cpp.ISR isr)
        {
            if (!_borrowed || _cam == null || _camT == null) return;
            try
            {
                if (_isrCam != null && isr != null && isr.Pointer != _isrCam.Pointer) return;
                var t = _cam.transform;
                if (t.parent != _camT) t.SetParent(_camT, false);
                t.localPosition = Vector3.zero; t.localRotation = Quaternion.identity;
                _cam.fieldOfView = VFov();
                if (_feed != null && _cam.targetTexture != _feed) _cam.targetTexture = _feed;
            }
            catch { }
        }

        static Mesh _m1, _m2; static Material _mat1, _mat2;

        sealed class Remote { internal GameObject Go; internal Vector3 Pos; internal Quaternion Rot; internal float Seen; internal int Seq; internal AudioSource Src; internal bool Boomed; }
        static readonly Dictionary<ulong, Remote> _remote = new Dictionary<ulong, Remote>();
        static float _nextPoll;

        static Texture2D _white, _scan, _vignette, _disc, _noise;
        static GUIStyle _osd, _osdSmall, _osdBig;

        internal static bool Piloting { get { return _view && _mode != Mode.None; } }
        internal static bool Exists { get { return _root != null && _mode != Mode.None; } }

        static string ContentDir
        {
            get { try { return Path.Combine(Epi.ContentDir, "drone"); } catch { return "Mods/FireteamRoles_Content/drone"; } }
        }

        internal static void Reset(string why)
        {
            Kill("reset: " + why, false);
            _uses = 0;
            foreach (var kv in _remote) { try { if (kv.Value.Go != null) UnityEngine.Object.Destroy(kv.Value.Go); } catch { } }
            _remote.Clear();
            try { Net.SetSelf(Net.K_DRONE, ""); } catch { }
        }

        internal static void Tick()
        {
            if (!Cfg.DroneEnabled.Value || !State.Enabled) { if (_mode != Mode.None) Kill("disabled", true); return; }
            if (_warmAt > 0f && Time.realtimeSinceStartup >= _warmAt) WarmNow();

            var me = State.Me;
            bool eligible = State.MyRole == Role.Jtac && me != null && me.Live && !Downed.AmDowned;

            if (Input.GetKeyDown(Cfg.Key(Cfg.DroneKey, KeyCode.U)))
            {
                if (_mode == Mode.None)
                {
                    if (!eligible) { }
                    else if (_uses >= Mathf.Max(1, Cfg.DroneCount.Value)) Ui.Toast("no drone left");
                    else Launch();
                }
                else if (_mode == Mode.Flying || _mode == Mode.Hover || _mode == Mode.Thrown)
                {
                    if (_view) LeaveView("drone holding - " + Cfg.DroneKey.Value.ToUpperInvariant() + " to fly it again");
                    else if (eligible) EnterView();
                }
            }

            if (_mode == Mode.None) { Remotes(); return; }

            if (_mode != Mode.Dead && !eligible) Kill("pilot down", true);

            if (_view)
            {
                if (Input.GetKeyDown(KeyCode.Backspace)) Kill("dropped", true);
                if (_mode == Mode.Flying && Cfg.DroneDetonate.Value && Input.GetMouseButtonDown(0)) Detonate();

                float sens = Mathf.Clamp(Cfg.DroneMouseSens.Value, 0.02f, 3f) * 1.8f;
                float mx = 0f, my = 0f;
                try { mx = Input.GetAxis("Mouse X"); my = Input.GetAxis("Mouse Y"); } catch { }
                _yaw += mx * sens;
                if (_yaw > 360f) _yaw -= 360f; else if (_yaw < 0f) _yaw += 360f;
                _lastYawRate = Time.deltaTime > 1e-4f ? mx * sens / Time.deltaTime : 0f;
                _aimPitch = Mathf.Clamp(_aimPitch - my * sens, -70f, 70f);
                if (Input.GetKeyDown(KeyCode.R)) _aimPitch = 0f;

                float ramp = Time.deltaTime * 5f;
                _thrustCmd = Mathf.MoveTowards(_thrustCmd, (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f), ramp);
                _strafeCmd = Mathf.MoveTowards(_strafeCmd, (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f), ramp);
                _climbCmd = Mathf.MoveTowards(_climbCmd, (Input.GetKey(KeyCode.Space) ? 1f : 0f) - ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C)) ? 1f : 0f), ramp);
                _turbo = Input.GetKey(KeyCode.LeftShift);
                Jello();
                if (_probeAt > 0f && Time.realtimeSinceStartup >= _probeAt) ProbeFeed();
            }
            else { _thrustCmd = 0f; _strafeCmd = 0f; _climbCmd = 0f; _turbo = false; _lastYawRate = 0f; }

            if (_mode == Mode.Dead)
            {
                if (_view && Time.realtimeSinceStartup >= _feedUntil) LeaveView(null);
                if (Time.realtimeSinceStartup - _deadAt > 20f) Despawn();
            }

            Audio();
            Publish();
            Remotes();
        }

        static void Jello()
        {
            if (_camT == null) return;
            float t = Time.realtimeSinceStartup;
            float amp = Mathf.Clamp(Cfg.DroneTurbulence.Value, 0f, 3f) * (0.05f + _throttle * 0.35f);
            float j1 = Mathf.Sin(t * 53f) * amp, j2 = Mathf.Sin(t * 61f + 1.3f) * amp * 0.7f, j3 = Mathf.Sin(t * 47f + 2.1f) * amp * 1.2f;
            try { _camT.localRotation = Quaternion.Euler(-Cfg.DroneCamTilt.Value + j1, j2, j3); } catch { }
        }

        internal static void FixedTick()
        {
            if (_rb == null || _mode == Mode.None || _mode == Mode.Dead) return;
            float now = Time.realtimeSinceStartup;
            float dt = Time.fixedDeltaTime;

            if (_mode == Mode.Thrown)
            {
                if (now < _armedAt) return;
                _mode = Mode.Flying;
                _lastVelValid = false;
                _colAt = now + 0.5f;
                Log.Fired("drone-armed");
            }
            if (!_firstFixed) { _firstFixed = true; Log.Fired("drone-fixed"); }

            try { if (_col != null && !_col.enabled && now >= _colAt) _col.enabled = true; } catch { }

            var me = State.Me;
            float range = 0f;
            try { if (me != null && me.Root != null) range = Vector3.Distance(me.Root.position, _root.transform.position); } catch { }
            _battery -= dt;
            if (_battery <= 0f) { Die("BATTERY"); return; }
            if (range > Mathf.Max(30f, Cfg.DroneRange.Value)) { Die("NO SIGNAL"); return; }

            float mass = _rb.mass;
            Vector3 vel = _rb.linearVelocity;
            bool flying = _view && _mode == Mode.Flying;
            bool turbo = _turbo && flying;
            float cruise = Mathf.Clamp(Cfg.DroneSpeed.Value, 3f, 40f) * (turbo ? 1.6f : 1f);
            float accel = Mathf.Clamp(Cfg.DroneAccel.Value, 3f, 40f) * (turbo ? 1.7f : 1f);
            float maxClimb = Mathf.Clamp(Cfg.DroneClimb.Value, 1f, 15f) * (turbo ? 1.5f : 1f);

            var aimRot = Quaternion.Euler(_aimPitch, _yaw, 0f);
            var headRot = Quaternion.Euler(0f, _yaw, 0f);
            Vector3 want = Vector3.zero;
            if (flying)
            {

                want = aimRot * Vector3.forward * (_thrustCmd * cruise)
                     + headRot * Vector3.right * (_strafeCmd * cruise * 0.7f)
                     + Vector3.up * (_climbCmd * maxClimb);
            }

            Vector3 dv = want - vel;
            Vector3 a = dv * 4f;
            Vector3 ah = new Vector3(a.x, 0f, a.z);
            if (ah.magnitude > accel) ah = ah.normalized * accel;
            float av = Mathf.Clamp(a.y, -15f, 15f);
            a = new Vector3(ah.x, av, ah.z);

            Vector3 force = mass * (a + Vector3.up * 9.81f);
            float maxThrust = mass * 9.81f * 3.5f;
            if (force.magnitude > maxThrust) force = force.normalized * maxThrust;
            _rb.AddForce(force, ForceMode.Force);
            _throttle = Mathf.Clamp01(force.magnitude / maxThrust);

            float leanMax = Mathf.Clamp(Cfg.DroneLeanMax.Value, 0f, 35f);
            Vector3 la = Quaternion.Inverse(headRot) * ah;
            float leanP = Mathf.Clamp(la.z * 1.1f, -leanMax, leanMax);
            float leanR = Mathf.Clamp(-la.x * 1.1f, -leanMax, leanMax);
            _yawRateSm = Mathf.Lerp(_yawRateSm, flying ? _lastYawRate : 0f, 0.2f);
            leanR += Mathf.Clamp(-_yawRateSm / 240f, -1f, 1f) * leanMax * 0.7f;
            _leanP = Mathf.Lerp(_leanP, leanP, 0.15f); _leanR = Mathf.Lerp(_leanR, leanR, 0.15f);

            float turb = Mathf.Clamp(Cfg.DroneTurbulence.Value, 0f, 3f) * (0.3f + _throttle) * 0.6f;
            float nP = (Mathf.Sin(now * 1.7f) * Mathf.Sin(now * 4.3f + 0.7f)) * turb;
            float nR = (Mathf.Sin(now * 2.9f + 1.9f) * Mathf.Sin(now * 3.7f)) * turb;

            var target = Quaternion.Euler(_aimPitch + _leanP + nP, _yaw, _leanR + nR);
            var cur = _rb.rotation;
            var next = Quaternion.Slerp(cur, target, 1f - Mathf.Exp(-dt * 14f));
            next = Quaternion.RotateTowards(cur, next, 720f * dt);
            _rb.MoveRotation(next);
            _rb.angularVelocity = Vector3.zero;
            _tilt = Vector3.Angle(next * Vector3.up, Vector3.up);

            if (_lastVelValid)
            {
                float jump = (vel - _lastVel).magnitude;
                if (jump > Mathf.Max(3f, Cfg.DroneCrashSpeed.Value)) { Die("CRASH"); return; }
            }
            _lastVel = _rb.linearVelocity; _lastVelValid = true;
        }

        static void Launch()
        {
            try
            {
                var me = State.Me;
                var pcam = Camera.main;
                if (pcam == null || me == null || me.Root == null) { Ui.Toast("drone: no camera"); return; }
                if (!EnsureAssets()) { Ui.Toast("drone asset missing"); return; }

                Vector3 f = pcam.transform.forward; f.y = 0f; f = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
                Vector3 pos = pcam.transform.position + f * 0.9f + Vector3.up * 0.15f;

                _root = new GameObject("odg_drone");
                _root.transform.position = pos;
                _yaw = pcam.transform.eulerAngles.y;
                _root.transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
                Model(_root.transform);

                _col = _root.AddComponent<SphereCollider>();
                ((SphereCollider)_col).radius = 0.2f;
                _col.enabled = false;
                _rb = _root.AddComponent<Rigidbody>();
                _rb.mass = 0.7f; _rb.linearDamping = 0.05f; _rb.angularDamping = 6f;
                _rb.useGravity = true;
                _rb.interpolation = RigidbodyInterpolation.Interpolate;
                _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

                try
                {
                    var mine = me.Root.GetComponentsInChildren<Collider>(true);
                    if (mine != null) for (int i = 0; i < mine.Length; i++) if (mine[i] != null) Physics.IgnoreCollision(_col, mine[i], true);
                }
                catch { }

                _rb.linearVelocity = f * 3.2f + Vector3.up * 3.6f;
                _rb.angularVelocity = new Vector3(UnityEngine.Random.Range(-2f, 2f), 0f, UnityEngine.Random.Range(-2f, 2f));
                _armedAt = Time.realtimeSinceStartup + 0.35f;
                _launchedAt = Time.realtimeSinceStartup;
                _battery = Mathf.Max(20f, Cfg.DroneBattery.Value);
                _aimPitch = 0f; _thrustCmd = 0f; _strafeCmd = 0f; _climbCmd = 0f; _leanP = 0f; _leanR = 0f; _yawRateSm = 0f;
                _throttle = 0.5f; _lastVelValid = false; _tilt = 0f;
                _mode = Mode.Thrown;
                _uses++;
                _firstFixed = _firstHud = _firstAudio = _firstPublish = false;
                _forcePlain = false; _probes = 0; _probeAt = Time.realtimeSinceStartup + 1.5f;

                MakeAudio();
                EnterView();

                Log.Msg("drone launched at " + pos.ToString("0.0") + " (use " + _uses + "/" + Cfg.DroneCount.Value + ")");
                Log.Fired("drone-launch");
                Ui.Toast("DRONE UP");
            }
            catch (Exception e) { Err.Note("drone.launch", e); Kill("launch failed", false); }
        }

        static void EnterView()
        {
            if (_view || _root == null) return;
            if (!AcquireCamera()) { Ui.Toast("drone: no camera to fly with"); return; }
            _view = true;

            if (_mode == Mode.Hover) _mode = Mode.Flying;
            Log.Detail("drone view: on the sticks (" + _mode + ")");
            if (Cfg.DroneMaskMainCamera.Value)
            {
                try
                {
                    _mainCam = Camera.main;
                    if (_mainCam != null && !_mainMasked) { _mainMask = _mainCam.cullingMask; _mainCam.cullingMask = 0; _mainMasked = true; }
                }
                catch { }
            }
            Thermal(true);
            if (_monitor != null) { try { _monitor.Play(); } catch { } }
            Log.Fired("drone-view");
        }

        static void LeaveView(string toast)
        {
            if (!_view) return;
            _view = false;
            if (_mode == Mode.Flying) _mode = Mode.Hover;
            try { if (_mainCam != null && _mainMasked) { _mainCam.cullingMask = _mainMask; _mainMasked = false; } } catch { }
            Thermal(false);
            ReleaseCamera();
            if (_monitor != null) { try { _monitor.Stop(); } catch { } }
            if (toast != null) Ui.Toast(toast);
        }

        static void Die(string why)
        {
            if (_mode == Mode.Dead) return;
            _mode = Mode.Dead;
            _deathNote = why;
            _deadAt = Time.realtimeSinceStartup;
            _feedUntil = _deadAt + 1.8f;
            try { if (_rotor != null) _rotor.Stop(); } catch { }
            if (why == "CRASH") BigBoom(_root != null ? _root.transform.position : Vector3.zero, 0.8f);
            Log.Msg("drone down: " + why);
            Log.Fired("drone-dead");
            if (!_view) Despawn();
        }

        static void Detonate()
        {
            if (_root == null) return;
            Vector3 at = _root.transform.position;
            int hit = 0;
            try
            {
                float radius = Mathf.Clamp(Cfg.DroneBlastRadius.Value, 1f, 12f);
                float dmg = Mathf.Max(1f, Cfg.DroneBlastDamage.Value);
                var cols = Physics.OverlapSphere(at, radius, ~0, QueryTriggerInteraction.Ignore);
                var seen = new HashSet<IntPtr>();
                if (cols != null)
                    for (int i = 0; i < cols.Length; i++)
                    {
                        var c = cols[i]; if (c == null) continue;
                        Il2Cpp.Health h = null;
                        try { h = c.GetComponentInParent<Il2Cpp.Health>(); } catch { }
                        if (h == null || !seen.Add(h.Pointer)) continue;
                        try { if (h.isDead) continue; } catch { }

                        var me = State.Me;
                        try { if (me != null && me.Health != null && me.Health.Pointer == h.Pointer) continue; } catch { }
                        float d = 1f;
                        try { d = Vector3.Distance(at, c.bounds.ClosestPoint(at)); } catch { }
                        float k = Mathf.Clamp01(1f - d / radius);
                        float amount = dmg * (0.35f + 0.65f * k);
                        try { h.CMDTakeDamage(amount, Il2Cpp.HealthBodyPart.Chest, Il2Cpp.HealthBodyPart.Chest, 0); hit++; }
                        catch (Exception e) { Log.Detail("drone blast damage failed: " + e.Message); }
                    }
            }
            catch (Exception e) { Err.Note("drone.detonate", e); }
            BigBoom(at, 1f);
            Log.Msg("drone DETONATED at " + at.ToString("0.0") + ", " + hit + " hit");
            Log.Fired("drone-detonate");
            Die("DETONATED");
            try { if (_root != null) { foreach (var r in _root.GetComponentsInChildren<Renderer>(true)) if (r != null) r.enabled = false; } } catch { }
        }

        static GameObject _boomFx, _boomLight;
        static Il2CppReferenceArray<AudioClip> _boomClips;
        static bool _boomLooked;

        static void FindBoom()
        {
            if (_boomLooked && _boomFx != null) return;
            _boomLooked = true;
            try
            {
                var all = Resources.FindObjectsOfTypeAll<Il2Cpp.GrenadeExplosive>();
                if (all == null) return;
                for (int i = 0; i < all.Length; i++)
                {
                    var g = all[i];
                    if (g == null) continue;
                    try
                    {
                        if (g.isFlashbang) continue;
                        if (g.explosionEffect == null) continue;
                        _boomFx = g.explosionEffect;
                        _boomLight = g.LightSource;
                        _boomClips = g.explosionClips;
                        Log.Msg("drone: borrowing the frag grenade's explosion from '" + g.gameObject.name + "' (clips " +
                                (_boomClips != null ? _boomClips.Length : 0) + ", light " + (_boomLight != null) + ")");
                        break;
                    }
                    catch { }
                }
                if (_boomFx == null) Log.Detail("drone: no frag grenade explosion found in memory, using the synth boom");
            }
            catch (Exception e) { Log.Detail("drone: explosion lookup failed: " + e.Message); }
        }

        static void BigBoom(Vector3 at, float vol)
        {
            FindBoom();
            bool played = false;
            try
            {
                if (_boomFx != null)
                {
                    GameObject fx = null;
                    try { fx = Il2Cpp.ObjectPooler.InstantiatePooled(_boomFx, at, Quaternion.identity); } catch { fx = null; }
                    if (fx == null) { fx = UnityEngine.Object.Instantiate(_boomFx, at, Quaternion.identity); UnityEngine.Object.Destroy(fx, 10f); }
                    fx.SetActive(true);
                    played = true;
                }
                if (_boomLight != null)
                {
                    var l = UnityEngine.Object.Instantiate(_boomLight, at + Vector3.up * 0.4f, Quaternion.identity);
                    l.SetActive(true);
                    UnityEngine.Object.Destroy(l, 0.25f);
                }
                if (_boomClips != null && _boomClips.Length > 0)
                {
                    var clip = _boomClips[UnityEngine.Random.Range(0, _boomClips.Length)];
                    if (clip != null) { AudioSource.PlayClipAtPoint(clip, at, vol); played = true; }
                }
            }
            catch (Exception e) { Log.Detail("drone: game explosion failed: " + e.Message); }
            if (!played) { try { var clip = Synth.Boom(); if (clip != null) AudioSource.PlayClipAtPoint(clip, at, vol); } catch { } }

            try
            {
                float radius = Mathf.Clamp(Cfg.DroneBlastRadius.Value, 1f, 12f);
                var cols = Physics.OverlapSphere(at, radius, ~0, QueryTriggerInteraction.Ignore);
                var seen = new HashSet<int>();
                if (cols != null)
                    for (int i = 0; i < cols.Length; i++)
                    {
                        var rb = cols[i] != null ? cols[i].attachedRigidbody : null;
                        if (rb == null || rb.isKinematic || !seen.Add(rb.GetInstanceID())) continue;
                        rb.AddExplosionForce(28f, at, radius, 1.2f, ForceMode.Impulse);
                    }
            }
            catch { }
            Log.Fired("drone-boom");
        }

        static void Boom(float vol)
        {
            try
            {
                var clip = Synth.Boom();
                if (clip == null || _root == null) return;
                AudioSource.PlayClipAtPoint(clip, _root.transform.position, vol);
            }
            catch { }
        }

        static void Kill(string why, bool toast)
        {
            if (_mode == Mode.None && _root == null) return;
            if (_view) LeaveView(null);
            if (_mode != Mode.Dead && _root != null) Log.Msg("drone ended: " + why);
            Despawn();
            if (toast) Ui.Toast("drone " + why);
        }

        static void Despawn()
        {
            if (_view) LeaveView(null);
            Thermal(false);
            ReleaseCamera();
            try { if (_root != null) UnityEngine.Object.Destroy(_root); } catch { }
            _root = null; _rb = null; _col = null; _rotor = null;
            _mode = Mode.None;
            try { Net.SetSelf(Net.K_DRONE, ""); } catch { }
        }

        static bool AcquireCamera()
        {
            if (_borrowed || _cam != null) return true;
            try
            {
                _camGo = new GameObject("odg_drone_mount");
                _camGo.transform.SetParent(_root.transform, false);

                _camGo.transform.localPosition = new Vector3(0f, 0.035f, 0.11f);
                _camT = _camGo.transform;
                _camT.localRotation = Quaternion.Euler(-Cfg.DroneCamTilt.Value, 0f, 0f);

                if (_forcePlain || !BorrowIsr()) MakePlain();
                if (_cam == null) { ReleaseCamera(); return false; }
                if (_feed == null) Log.Warn("drone view: no feed texture, the goggles will show nothing");
                Log.Fired("drone-camera");
                return true;
            }
            catch (Exception e) { Err.Note("drone.camera", e); ReleaseCamera(); return false; }
        }

        static bool BorrowIsr()
        {
            var isr = Jtac.ActiveIsr() ?? FindIsr();
            Camera src = null;
            try { if (isr != null) src = isr.ISRcamera; } catch { }
            if (src == null) { Log.Msg("drone view: no ISR camera in this scene"); return false; }
            try
            {
                var t = src.transform;
                var cgo = src.gameObject;
                _camHome = t.parent; _camHomePos = t.localPosition; _camHomeRot = t.localRotation;
                _camHomeRt = src.targetTexture; _camHomeFov = src.fieldOfView; _camHomeNear = src.nearClipPlane;
                _camHomeFar = src.farClipPlane; _camHomeDepth = src.depth; _camHomeEnabled = src.enabled;
                _camHomeActive = cgo.activeSelf;
                try { _camHomeOrtho = src.orthographic; } catch { _camHomeOrtho = false; }

                string facts = "";
                try
                {
                    facts = " enabled=" + _camHomeEnabled + " activeSelf=" + _camHomeActive + " inHierarchy=" + cgo.activeInHierarchy +
                            " ortho=" + _camHomeOrtho + " display=" + src.targetDisplay + " depth=" + _camHomeDepth.ToString("0.#") +
                            " rt=" + (_camHomeRt != null ? _camHomeRt.width + "x" + _camHomeRt.height : "none");
                }
                catch { }

                _borrowed = true;
                _isrCam = isr;
                _cam = src; _plainCam = false;
                t.SetParent(_camT, false);
                t.localPosition = Vector3.zero; t.localRotation = Quaternion.identity;

                if (!cgo.activeSelf) cgo.SetActive(true);
                try
                {
                    var al = cgo.GetComponent<AudioListener>();
                    if (al != null && al.enabled) { al.enabled = false; _camListenerOff = true; }
                }
                catch { }
                if (!cgo.activeInHierarchy)
                {
                    Log.Warn("drone view: the ISR camera will not come alive under the drone (" + facts + "), using a plain camera");
                    ReturnIsrCamera();
                    return false;
                }

                _feed = MakeFeed(_camHomeRt);
                try { src.orthographic = false; } catch { }
                src.targetTexture = _feed;
                src.fieldOfView = VFov();
                src.nearClipPlane = 0.03f;
                if (src.farClipPlane < 2000f) src.farClipPlane = 2000f;
                src.enabled = true;
                Log.Msg("drone view: borrowed the ISR camera (was under '" + (_camHome != null ? _camHome.name : "none") + "', fov " +
                        _camHomeFov.ToString("0.#") + facts + ") -> feed " + (_feed != null ? _feed.width + "x" + _feed.height : "NONE") +
                        ", now active=" + cgo.activeInHierarchy + ", vfov " + VFov().ToString("0"));
                return true;
            }
            catch (Exception e)
            {
                Log.Warn("drone view: borrowing the ISR camera failed: " + e.Message);
                ReturnIsrCamera();
                return false;
            }
        }

        static void MakePlain()
        {
            try
            {
                var go = new GameObject("odg_drone_cam");
                go.transform.SetParent(_camT, false);
                _cam = go.AddComponent<Camera>();
                _feed = MakeFeed(null);
                _cam.targetTexture = _feed;
                _cam.fieldOfView = VFov();
                _cam.nearClipPlane = 0.03f; _cam.farClipPlane = 2000f;
                _plainCam = true;
                Log.Msg("drone view: plain camera -> feed " + (_feed != null ? _feed.width + "x" + _feed.height : "NONE") + ", vfov " + VFov().ToString("0"));
            }
            catch (Exception e) { Err.Note("drone.plaincam", e); _cam = null; }
        }

        static void ReturnIsrCamera()
        {
            try
            {
                if (_borrowed && _cam != null)
                {
                    var t = _cam.transform;
                    var cgo = _cam.gameObject;
                    _cam.targetTexture = _camHomeRt;
                    try { _cam.orthographic = _camHomeOrtho; } catch { }
                    _cam.fieldOfView = _camHomeFov; _cam.nearClipPlane = _camHomeNear; _cam.farClipPlane = _camHomeFar;
                    _cam.depth = _camHomeDepth; _cam.enabled = _camHomeEnabled;
                    try { if (_camListenerOff) { var al = cgo.GetComponent<AudioListener>(); if (al != null) al.enabled = true; } } catch { }
                    t.SetParent(_camHome, false);
                    t.localPosition = _camHomePos; t.localRotation = _camHomeRot;
                    if (!_camHomeActive) cgo.SetActive(false);
                    Log.Detail("drone view: ISR camera returned");
                }
            }
            catch (Exception e) { Log.Detail("drone view: camera return failed: " + e.Message); }
            _borrowed = false; _cam = null; _isrCam = null; _camHome = null; _camHomeRt = null; _camListenerOff = false;
        }

        static void ReleaseCamera()
        {
            if (_borrowed) ReturnIsrCamera();
            else if (_plainCam && _cam != null) { try { UnityEngine.Object.Destroy(_cam.gameObject); } catch { } }
            _plainCam = false; _cam = null;
            try { if (_camGo != null) UnityEngine.Object.Destroy(_camGo); } catch { }
            _camGo = null; _camT = null;
            try { if (_feed != null) { _feed.Release(); UnityEngine.Object.Destroy(_feed); } } catch { }
            _feed = null;
        }

        static RenderTexture MakeFeed(RenderTexture like)
        {
            try
            {
                int h = Mathf.Clamp(Cfg.DroneFeedHeight.Value, 240, 2160);
                int w = Mathf.Max(320, Mathf.RoundToInt(h * (float)Mathf.Max(1, Screen.width) / Mathf.Max(1, Screen.height)));
                RenderTexture rt = null;
                if (like != null)
                {
                    try
                    {
                        var desc = like.descriptor;
                        desc.width = w; desc.height = h;
                        rt = new RenderTexture(desc);
                    }
                    catch (Exception e) { Log.Detail("drone feed: descriptor copy failed: " + e.Message); rt = null; }
                }
                if (rt == null) rt = new RenderTexture(w, h, 24);
                rt.name = "odg_drone_feed";
                rt.Create();
                return rt;
            }
            catch (Exception e) { Err.Note("drone.feed", e); return null; }
        }

        static float VFov()
        {
            float hf = Mathf.Clamp(Cfg.DroneFov.Value, 60f, 150f);
            float aspect = _feed != null ? (float)_feed.width / Mathf.Max(1, _feed.height)
                                         : (float)Mathf.Max(1, Screen.width) / Mathf.Max(1, Screen.height);
            float v = 2f * Mathf.Atan(Mathf.Tan(hf * 0.5f * Mathf.Deg2Rad) / Mathf.Max(0.5f, aspect)) * Mathf.Rad2Deg;
            return Mathf.Clamp(v, 25f, 130f);
        }

        static void ProbeFeed()
        {
            _probeAt = 0f;
            if (_feed == null) return;
            float avg = -1f;
            try
            {
                var tiny = RenderTexture.GetTemporary(4, 4, 0);
                Graphics.Blit(_feed, tiny);
                var prev = RenderTexture.active;
                RenderTexture.active = tiny;
                var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0f, 0f, 4f, 4f), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(tiny);
                var px = tex.GetPixels();
                float sum = 0f; int n = 0;
                if (px != null)
                    for (int i = 0; i < px.Length; i++) { var c = px[i]; sum += c.r + c.g + c.b; n += 3; }
                avg = n > 0 ? sum / n : -1f;
                UnityEngine.Object.Destroy(tex);
            }
            catch (Exception e) { Log.Detail("drone feed probe failed: " + e.Message); return; }

            _probes++;
            bool black = avg >= 0f && avg < 0.004f;
            Log.Msg("drone feed probe " + _probes + ": mean brightness " + avg.ToString("0.000") + (black ? "  BLACK" : "") +
                    (_borrowed ? " (ISR camera)" : " (plain camera)"));
            if (black && _borrowed && _probes < 3)
            {
                Log.Warn("drone view: the ISR camera is not drawing into the feed, switching to a plain camera");
                _forcePlain = true;
                bool wasView = _view;
                ReleaseCamera();
                if (wasView && AcquireCamera()) _probeAt = Time.realtimeSinceStartup + 1.5f;
            }
        }

        static Il2Cpp.ISR FindIsr()
        {
            try
            {
                var all = Resources.FindObjectsOfTypeAll<Il2Cpp.ISR>();
                if (all == null) return null;
                for (int i = 0; i < all.Length; i++)
                {
                    var s = all[i]; if (s == null) continue;
                    try { if (!s.gameObject.scene.IsValid()) continue; } catch { continue; }
                    return s;
                }
            }
            catch { }
            return null;
        }

        static void Thermal(bool on)
        {
            if (!Cfg.DroneThermal.Value) return;
            if (on)
            {
                if (_thermalOn || _plainCam) return;
                var isr = Jtac.ActiveIsr() ?? FindIsr();
                if (isr == null) { Log.Msg("drone: no ISR in this scene, so the feed is plain video"); return; }
                _isr = isr;
                try
                {
                    _savedMode = isr.currentVisionMode;
                    _savedB = isr.BHot != null && isr.BHot.activeSelf;
                    _savedW = isr.WHot != null && isr.WHot.activeSelf;
                    bool white = Cfg.DroneThermalWhiteHot.Value;
                    isr.currentVisionMode = white ? 2 : 1;
                    if (isr.BHot != null) isr.BHot.SetActive(!white);
                    if (isr.WHot != null) isr.WHot.SetActive(white);
                    Globalise(white ? isr.WHot : isr.BHot);
                    _thermalOn = true;
                    DumpOnce(isr);
                    Log.Fired("drone-thermal");
                }
                catch (Exception e) { Err.Note("drone.thermal", e); }
            }
            else
            {
                if (!_thermalOn) return;
                _thermalOn = false;
                try
                {
                    for (int i = 0; i < _globalised.Count; i++) { try { if (_globalised[i] != null) _globalised[i].isGlobal = false; } catch { } }
                    _globalised.Clear();
                    if (_isr != null)
                    {
                        _isr.currentVisionMode = _savedMode;
                        if (_isr.BHot != null) _isr.BHot.SetActive(_savedB);
                        if (_isr.WHot != null) _isr.WHot.SetActive(_savedW);
                    }
                }
                catch { }
                _isr = null;
            }
        }

        static void Globalise(GameObject g)
        {
            if (g == null) return;
            try
            {
                var vols = g.GetComponentsInChildren<UnityEngine.Rendering.Volume>(true);
                if (vols == null) return;
                for (int i = 0; i < vols.Length; i++)
                {
                    var v = vols[i]; if (v == null) continue;
                    bool wasGlobal = false; try { wasGlobal = v.isGlobal; } catch { }
                    if (wasGlobal) continue;
                    v.isGlobal = true;
                    _globalised.Add(v);
                }
            }
            catch (Exception e) { Log.Detail("drone: volume globalise failed: " + e.Message); }
        }

        static void DumpOnce(Il2Cpp.ISR isr)
        {
            if (_dumped) return;
            _dumped = true;
            try
            {
                var sb = new System.Text.StringBuilder("drone thermal: mode=" + isr.currentVisionMode);
                foreach (var pair in new[] { new KeyValuePair<string, GameObject>("BHot", isr.BHot), new KeyValuePair<string, GameObject>("WHot", isr.WHot) })
                {
                    var g = pair.Value;
                    sb.Append("  ").Append(pair.Key).Append('=');
                    if (g == null) { sb.Append("null"); continue; }
                    sb.Append(g.name).Append('[').Append(g.activeSelf ? "on" : "off").Append("]{");
                    var comps = g.GetComponentsInChildren<Component>(true);
                    int n = 0;
                    if (comps != null)
                        for (int i = 0; i < comps.Length && n < 12; i++)
                        {
                            var c = comps[i]; if (c == null) continue;
                            string tn = "?"; try { tn = c.GetIl2CppType().Name; } catch { }
                            if (tn == "Transform") continue;
                            sb.Append(tn).Append(' '); n++;
                        }
                    sb.Append('}');
                }
                sb.Append("  globalised=").Append(_globalised.Count).Append(" borrowed=").Append(_borrowed);
                Log.Msg(sb.ToString());
            }
            catch (Exception e) { Log.Detail("drone thermal dump failed: " + e.Message); }
        }

        static bool _assetsFailed;

        static bool EnsureAssets()
        {
            if (_m1 != null && _mat1 != null) return true;

            if (_assetsFailed) return false;
            try
            {
                float t0 = Time.realtimeSinceStartup;
                string dir = ContentDir;
                float scale = Mathf.Clamp(Cfg.DroneScale.Value, 0.02f, 1f);
                string why;
                _m1 = ObjMesh.Load(Path.Combine(dir, "drone_m1.obj"), scale, out why);
                if (_m1 == null) { Log.Warn("drone: body mesh failed: " + why); _assetsFailed = true; return false; }
                _m2 = ObjMesh.Load(Path.Combine(dir, "drone_m2.obj"), scale, out why);
                if (_m2 == null) Log.Detail("drone: second part failed: " + why);
                _mat1 = BuildMat(dir, "m1", true);
                _mat2 = BuildMat(dir, "m2", false);
                if (_mat1 == null) { _assetsFailed = true; return false; }
                Log.Msg("drone assets: " + _m1.vertexCount + " + " + (_m2 != null ? _m2.vertexCount : 0) + " verts in " +
                        ((Time.realtimeSinceStartup - t0) * 1000f).ToString("0") + " ms");
                return true;
            }
            catch (Exception e) { Err.Note("drone.assets", e); _assetsFailed = true; return false; }
        }

        static float _warmAt;
        static bool _warmed;

        internal static void Warm()
        {
            _warmAt = Time.realtimeSinceStartup + 3f;
            _boomLooked = false;
        }

        static void WarmNow()
        {
            _warmAt = 0f;
            if (!Cfg.DroneEnabled.Value) return;
            try
            {
                float t0 = Time.realtimeSinceStartup;
                bool ok = _warmed || EnsureAssets();
                if (ok) { Synth.Rotor(); Synth.Boom(); }
                FindBoom();
                if (!_warmed && ok) Log.Msg("drone warmed in " + ((Time.realtimeSinceStartup - t0) * 1000f).ToString("0") + " ms");
                _warmed = ok;
            }
            catch (Exception e) { Log.Detail("drone warm-up failed: " + e.Message); }
        }

        static Material BuildMat(string dir, string tag, bool emissive)
        {
            Material m = null;
            try { var sh = Shader.Find("HDRP/Lit"); if (sh != null) m = new Material(sh); } catch { }
            if (m == null) { Log.Warn("drone: HDRP/Lit not found"); return null; }
            m.name = "odg_drone_" + tag;
            SetTex(m, "_BaseColorMap", LoadTex(dir, tag + "_base.png", false));
            SetTex(m, "_NormalMap", LoadTex(dir, tag + "_normal.png", true));
            SetTex(m, "_MaskMap", LoadTex(dir, tag + "_mask.png", true));
            try { if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 1f); } catch { }
            try { if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 1f); } catch { }
            try { m.EnableKeyword("_NORMALMAP"); m.EnableKeyword("_MASKMAP"); } catch { }
            if (emissive)
            {
                var em = LoadTex(dir, tag + "_emissive.png", false);
                if (em != null)
                {
                    SetTex(m, "_EmissiveColorMap", em);
                    try { m.SetColor("_EmissiveColor", Color.white * 4f); m.EnableKeyword("_EMISSIVE_COLOR_MAP"); } catch { }
                }
            }
            try { m.hideFlags = HideFlags.DontUnloadUnusedAsset; } catch { }
            return m;
        }

        static void SetTex(Material m, string prop, Texture2D t)
        {
            if (m == null || t == null) return;
            try { if (m.HasProperty(prop)) m.SetTexture(prop, t); } catch { }
        }

        static Texture2D LoadTex(string dir, string file, bool linear)
        {
            try
            {
                string p = Path.Combine(dir, file);
                if (!File.Exists(p)) { Log.Detail("drone texture missing: " + file); return null; }
                var bytes = File.ReadAllBytes(p);

                var arr = MeshCut.FromManaged(bytes);
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear);
                if (!ImageConversion.LoadImage(t, arr)) { Log.Detail("drone texture unreadable: " + file); return null; }
                t.wrapMode = TextureWrapMode.Repeat;
                t.hideFlags = HideFlags.DontUnloadUnusedAsset;
                return t;
            }
            catch (Exception e) { Log.Detail("drone texture " + file + ": " + e.Message); return null; }
        }

        static void Model(Transform parent)
        {
            var body = new GameObject("body");
            body.transform.SetParent(parent, false);
            body.transform.localRotation = Quaternion.Euler(0f, Cfg.DroneModelYaw.Value, 0f);
            Part(body.transform, "m1", _m1, _mat1);
            if (_m2 != null) Part(body.transform, "m2", _m2, _mat2 ?? _mat1);
        }

        static void Part(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
        }

        static void MakeAudio()
        {
            try
            {
                var clip = Synth.Rotor();
                if (clip == null) return;
                _rotor = _root.AddComponent<AudioSource>();
                _rotor.clip = clip; _rotor.loop = true;
                _rotor.spatialBlend = 1f; _rotor.minDistance = 2f; _rotor.maxDistance = 90f;
                _rotor.rolloffMode = AudioRolloffMode.Logarithmic;
                _rotor.dopplerLevel = 0.6f;
                _rotor.volume = 0.8f; _rotor.pitch = 0.9f;
                _rotor.Play();

                if (_monitorGo == null)
                {
                    _monitorGo = new GameObject("odg_drone_monitor");
                    UnityEngine.Object.DontDestroyOnLoad(_monitorGo);
                    _monitor = _monitorGo.AddComponent<AudioSource>();
                    _monitor.clip = clip; _monitor.loop = true; _monitor.spatialBlend = 0f; _monitor.volume = 0.28f;
                }
            }
            catch (Exception e) { Err.Note("drone.audio", e); }
        }

        static void Audio()
        {
            if (!_firstAudio) { _firstAudio = true; Log.Fired("drone-audio"); }
            try
            {
                float p = 0.8f + _throttle * 0.7f;
                float v = _mode == Mode.Dead ? 0f : 0.5f + _throttle * 0.5f;
                if (_rotor != null) { _rotor.pitch = Mathf.Lerp(_rotor.pitch, p, 0.12f); _rotor.volume = Mathf.Lerp(_rotor.volume, v, 0.1f); }
                if (_monitor != null && _view) { _monitor.pitch = _rotor != null ? _rotor.pitch : p; _monitor.volume = _mode == Mode.Dead ? 0f : 0.22f + _throttle * 0.12f; }
            }
            catch { }
        }

        static void Publish()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _nextPub || _root == null) return;
            _nextPub = now + 0.25f;
            if (!_firstPublish) { _firstPublish = true; Log.Fired("drone-publish"); }
            try
            {
                var p = _root.transform.position; var e = _root.transform.eulerAngles;
                _seq++;
                Net.SetSelf(Net.K_DRONE, _seq + "@" + Net.F(p.x) + "@" + Net.F(p.y) + "@" + Net.F(p.z)
                                      + "@" + Net.F(e.x) + "@" + Net.F(e.y) + "@" + Net.F(e.z) + "@" + (_mode == Mode.Dead ? 0 : 1));
            }
            catch { }
        }

        internal static void Poll()
        {
            if (!Cfg.DroneEnabled.Value || !State.Enabled) return;
            float now = Time.realtimeSinceStartup;
            if (now < _nextPoll) return;
            _nextPoll = now + 0.25f;
            try
            {
                var members = Net.Members();
                for (int i = 0; i < members.Count; i++)
                {
                    ulong from = members[i];
                    if (from == Net.Self) continue;
                    string raw = Net.Get(from, Net.K_DRONE);
                    Remote r; _remote.TryGetValue(from, out r);
                    if (string.IsNullOrEmpty(raw))
                    {
                        if (r != null) { try { if (r.Go != null) UnityEngine.Object.Destroy(r.Go); } catch { } _remote.Remove(from); }
                        continue;
                    }
                    var f = raw.Split('@');
                    if (f.Length < 8) continue;
                    int seq = Net.Int(f[0]);
                    if (r != null && seq == r.Seq) continue;
                    var pos = new Vector3(Net.Float(f[1]), Net.Float(f[2]), Net.Float(f[3]));
                    var rot = Quaternion.Euler(Net.Float(f[4]), Net.Float(f[5]), Net.Float(f[6]));
                    bool alive = Net.Int(f[7]) != 0;
                    if (r == null)
                    {
                        if (!EnsureAssets()) continue;
                        r = new Remote { Go = new GameObject("odg_drone_remote"), Pos = pos, Rot = rot };
                        r.Go.transform.position = pos; r.Go.transform.rotation = rot;
                        Model(r.Go.transform);
                        try
                        {
                            var clip = Synth.Rotor();
                            if (clip != null)
                            {
                                r.Src = r.Go.AddComponent<AudioSource>();
                                r.Src.clip = clip; r.Src.loop = true; r.Src.spatialBlend = 1f; r.Src.minDistance = 2f; r.Src.maxDistance = 90f;
                                r.Src.rolloffMode = AudioRolloffMode.Logarithmic; r.Src.dopplerLevel = 0.6f; r.Src.volume = 0.8f; r.Src.Play();
                            }
                        }
                        catch { }
                        _remote[from] = r;
                        Log.Msg("drone from " + State.NameOf(from) + " is up");
                        Log.Fired("drone-remote");
                    }
                    r.Pos = pos; r.Rot = rot; r.Seq = seq; r.Seen = now;
                    if (!alive)
                    {
                        if (r.Src != null) { try { r.Src.Stop(); } catch { } }
                        if (!r.Boomed) { r.Boomed = true; BigBoom(pos, 1f); }
                    }
                }
            }
            catch (Exception e) { Log.Detail("drone poll failed: " + e.Message); }
        }

        static void Remotes()
        {
            if (_remote.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            List<ulong> gone = null;
            foreach (var kv in _remote)
            {
                var r = kv.Value;
                if (r.Go == null || now - r.Seen > 6f) { if (gone == null) gone = new List<ulong>(); gone.Add(kv.Key); continue; }
                try
                {
                    var t = r.Go.transform;
                    t.position = Vector3.Lerp(t.position, r.Pos, 0.12f);
                    t.rotation = Quaternion.Slerp(t.rotation, r.Rot, 0.12f);
                }
                catch { }
            }
            if (gone != null)
                for (int i = 0; i < gone.Count; i++)
                {
                    Remote r; if (_remote.TryGetValue(gone[i], out r)) { try { if (r.Go != null) UnityEngine.Object.Destroy(r.Go); } catch { } _remote.Remove(gone[i]); }
                }
        }

        static float _hudK = -1f;

        static void EnsureHud(float k)
        {
            if (_white == null)
            {
                _white = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                _white.SetPixel(0, 0, Color.white); _white.Apply(); _white.hideFlags = HideFlags.HideAndDontSave;
            }
            if (_scan == null)
            {
                const int H = 256;
                _scan = new Texture2D(1, H, TextureFormat.RGBA32, false);
                for (int y = 0; y < H; y++) _scan.SetPixel(0, y, new Color(0f, 0f, 0f, (y % 3) == 1 ? 0.09f : 0f));
                _scan.wrapMode = TextureWrapMode.Clamp; _scan.filterMode = FilterMode.Point;
                _scan.Apply(); _scan.hideFlags = HideFlags.HideAndDontSave;
            }
            if (_vignette == null)
            {
                const int N = 64;
                _vignette = new Texture2D(N, N, TextureFormat.RGBA32, false);
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.SmoothStep(0.55f, 1.35f, r) * 0.75f;
                        _vignette.SetPixel(x, y, new Color(0f, 0f, 0f, a));
                    }
                _vignette.wrapMode = TextureWrapMode.Clamp; _vignette.Apply(); _vignette.hideFlags = HideFlags.HideAndDontSave;
            }
            if (_disc == null)
            {
                const int N = 64;
                _disc = new Texture2D(N, N, TextureFormat.RGBA32, false);
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = 1f - Mathf.SmoothStep(0.72f, 1f, r);
                        _disc.SetPixel(x, y, new Color(0.55f, 0.55f, 0.55f, a));
                    }
                _disc.wrapMode = TextureWrapMode.Clamp; _disc.Apply(); _disc.hideFlags = HideFlags.HideAndDontSave;
            }
            if (_noise == null)
            {
                const int N = 128;
                _noise = new Texture2D(N, N, TextureFormat.RGBA32, false);
                uint rng = 0x9e3779b9u;
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float g = Rubble.Rand(ref rng);
                        _noise.SetPixel(x, y, new Color(g, g, g, 1f));
                    }
                _noise.wrapMode = TextureWrapMode.Repeat; _noise.filterMode = FilterMode.Point;
                _noise.Apply(); _noise.hideFlags = HideFlags.HideAndDontSave;
            }
            if (_osd == null || Mathf.Abs(k - _hudK) > 0.01f)
            {
                _hudK = k;
                _osd = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(8, Mathf.RoundToInt(17f * k)), fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperLeft };
                _osd.normal.textColor = Color.white;
                _osdSmall = new GUIStyle(_osd) { fontSize = Mathf.Max(7, Mathf.RoundToInt(12.5f * k)), fontStyle = FontStyle.Normal };
                _osdSmall.normal.textColor = new Color(1f, 1f, 1f, 0.75f);
                _osdBig = new GUIStyle(_osd) { fontSize = Mathf.Max(16, Mathf.RoundToInt(46f * k)), alignment = TextAnchor.MiddleCenter };
            }
        }

        static void Osd(float x, float y, float w, string s, GUIStyle st, TextAnchor anchor)
        {
            st.alignment = anchor;
            var r = new Rect(x, y, w, st.fontSize + 10f);
            float o = Mathf.Max(1f, st.fontSize / 14f);
            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            GUI.Label(new Rect(r.x + o, r.y + o, r.width, r.height), s, st);
            GUI.Label(new Rect(r.x - o * 0.7f, r.y + o * 0.7f, r.width, r.height), s, st);
            GUI.color = Color.white;
            GUI.Label(r, s, st);
        }

        static void Bar(float x, float y, float w, float h, float frac, Color c)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.55f); GUI.DrawTexture(new Rect(x - 1f, y - 1f, w + 2f, h + 2f), _white);
            GUI.color = c; GUI.DrawTexture(new Rect(x, y, w * Mathf.Clamp01(frac), h), _white);
            GUI.color = Color.white;
        }

        internal static void DrawHud()
        {
            if (!_view || _root == null) return;
            if (!_firstHud) { _firstHud = true; Log.Fired("drone-hud"); }
            float W = Screen.width, H = Screen.height;

            float k = Mathf.Clamp(H / 1080f, 0.6f, 4f) * Mathf.Clamp(Cfg.DroneHudScale.Value, 0.5f, 3f);
            EnsureHud(k);
            float now = Time.realtimeSinceStartup;
            var saved = GUI.matrix;

            GUI.color = Color.white;
            if (_feed != null) { try { GUI.DrawTexture(new Rect(0f, 0f, W, H), _feed, ScaleMode.StretchToFill, false); } catch { } }

            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(0f, 0f, W, H), _vignette, ScaleMode.StretchToFill, true);
            GUI.DrawTexture(new Rect(0f, 0f, W, H), _scan, ScaleMode.StretchToFill, true);
            if (Cfg.DronePropsInView.Value && _mode != Mode.Dead)
            {
                float ds = H * 0.62f;
                float flick = 0.16f + 0.07f * Mathf.Abs(Mathf.Sin(now * 37f)) * (0.3f + _throttle);
                GUI.color = new Color(1f, 1f, 1f, flick);
                GUI.DrawTexture(new Rect(-ds * 0.42f, H - ds * 0.55f, ds, ds), _disc, ScaleMode.StretchToFill, true);
                GUI.DrawTexture(new Rect(W - ds * 0.58f, H - ds * 0.55f, ds, ds), _disc, ScaleMode.StretchToFill, true);
                GUI.color = Color.white;
            }

            float alt = 0f, spd = 0f, rng = 0f, bearing = 0f;
            try
            {
                var p = _root.transform.position;
                RaycastHit hit;
                if (Physics.Raycast(p, Vector3.down, out hit, 500f, ~0, QueryTriggerInteraction.Ignore)) alt = hit.distance;
                if (_rb != null) spd = _rb.linearVelocity.magnitude;
                var me = State.Me;
                if (me != null && me.Root != null)
                {
                    Vector3 to = me.Root.position - p;
                    rng = to.magnitude;
                    bearing = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg - _yaw;
                }
            }
            catch { }
            float bat = Mathf.Clamp01(_battery / Mathf.Max(1f, Cfg.DroneBattery.Value));
            float volts = 14.0f + 2.8f * bat;
            float lq = 100f * Mathf.Clamp01(1f - Mathf.Pow(rng / Mathf.Max(30f, Cfg.DroneRange.Value), 2f));
            float m = 26f * k;

            string state = _mode == Mode.Thrown ? "ARMING" : _mode == Mode.Hover ? "HOLD" : _mode == Mode.Dead ? "FAILSAFE" : "ARMED";
            bool inputs = Mathf.Abs(_thrustCmd) + Mathf.Abs(_strafeCmd) + Mathf.Abs(_climbCmd) > 0.05f;
            string fm = _mode == Mode.Flying ? (_turbo ? "TURBO" : (inputs ? "FLY" : "HOVER")) : "";
            float secs = Mathf.Max(0f, now - _launchedAt);
            Osd(m, m, 500f * k, state + "   " + fm, _osd, TextAnchor.UpperLeft);
            Osd(m, m + 24f * k, 500f * k, ((int)(secs / 60f)).ToString("00") + ":" + ((int)(secs % 60f)).ToString("00") + "   " +
                (_thermalOn ? (Cfg.DroneThermalWhiteHot.Value ? "FLIR  W-HOT" : "FLIR  B-HOT") : "EO") + (_plainCam ? "   AUX CAM" : ""), _osdSmall, TextAnchor.UpperLeft);

            Osd(W - m - 360f * k, m, 360f * k, volts.ToString("0.0") + "V   " + Mathf.RoundToInt(bat * 100f) + "%", _osd, TextAnchor.UpperRight);
            Bar(W - m - 180f * k, m + 28f * k, 180f * k, 8f * k, bat, bat > 0.25f ? new Color(0.75f, 1f, 0.75f) : new Color(1f, 0.4f, 0.3f));
            Osd(W - m - 360f * k, m + 42f * k, 360f * k, "LQ " + Mathf.RoundToInt(lq) + "   RSSI " + Mathf.RoundToInt(-40f - (100f - lq) * 0.6f) + " dBm", _osdSmall, TextAnchor.UpperRight);

            float cx = W * 0.5f, cy = H * 0.5f, t2 = 2f * k;
            GUI.color = new Color(1f, 1f, 1f, 0.9f);
            GUI.DrawTexture(new Rect(cx - 16f * k, cy - t2 * 0.5f, 10f * k, t2), _white); GUI.DrawTexture(new Rect(cx + 6f * k, cy - t2 * 0.5f, 10f * k, t2), _white);
            GUI.DrawTexture(new Rect(cx - t2 * 0.5f, cy - 16f * k, t2, 10f * k), _white); GUI.DrawTexture(new Rect(cx - t2 * 0.5f, cy + 6f * k, t2, 10f * k), _white);
            for (int i = -3; i <= 3; i++)
            {
                float yy = cy + i * 22f * k;
                float len = (i == 0 ? 14f : 8f) * k;
                GUI.DrawTexture(new Rect(cx - 150f * k - len, yy - t2 * 0.5f, len, t2), _white);
                GUI.DrawTexture(new Rect(cx + 150f * k, yy - t2 * 0.5f, len, t2), _white);
            }

            try
            {
                var pivot = new Vector2(cx, cy - 60f * k);
                GUIUtility.RotateAroundPivot(bearing, pivot);
                GUI.DrawTexture(new Rect(cx - 1.5f * k, cy - 74f * k, 3f * k, 20f * k), _white);
                GUI.DrawTexture(new Rect(cx - 7f * k, cy - 72f * k, 6f * k, 2.5f * k), _white);
                GUI.DrawTexture(new Rect(cx + 1f * k, cy - 72f * k, 6f * k, 2.5f * k), _white);
            }
            catch { }
            GUI.matrix = saved;
            GUI.color = Color.white;

            float by = H - m - 72f * k;
            Osd(m, by, 500f * k, "ALT " + alt.ToString("0.0") + " m", _osd, TextAnchor.UpperLeft);
            Osd(m, by + 24f * k, 500f * k, "SPD " + (spd * 3.6f).ToString("0") + " km/h", _osd, TextAnchor.UpperLeft);
            Osd(m, by + 48f * k, 500f * k, "HOME " + rng.ToString("0") + " m    HDG " + Mathf.RoundToInt(_yaw).ToString("000") + "    AIM " + Mathf.RoundToInt(-_aimPitch).ToString("+0;-0") + "    TILT " + Mathf.RoundToInt(_tilt), _osdSmall, TextAnchor.UpperLeft);

            float sb = 64f * k, sx = W - m - sb, sy = H - m - sb, ln = 1.5f * k;
            GUI.color = new Color(0f, 0f, 0f, 0.35f); GUI.DrawTexture(new Rect(sx, sy, sb, sb), _white);
            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            GUI.DrawTexture(new Rect(sx, sy, sb, ln), _white); GUI.DrawTexture(new Rect(sx, sy + sb - ln, sb, ln), _white);
            GUI.DrawTexture(new Rect(sx, sy, ln, sb), _white); GUI.DrawTexture(new Rect(sx + sb - ln, sy, ln, sb), _white);
            GUI.DrawTexture(new Rect(sx + sb * 0.5f - 0.5f, sy, 1f, sb), _white); GUI.DrawTexture(new Rect(sx, sy + sb * 0.5f - 0.5f, sb, 1f), _white);
            GUI.color = Color.white;
            float dot = 7f * k;
            float dotX = sx + sb * 0.5f + _strafeCmd * (sb * 0.42f), dotY = sy + sb * 0.5f - _thrustCmd * (sb * 0.42f);
            GUI.DrawTexture(new Rect(dotX - dot * 0.5f, dotY - dot * 0.5f, dot, dot), _white);
            float tx = sx - 22f * k, tw = 10f * k;
            GUI.color = new Color(0f, 0f, 0f, 0.45f); GUI.DrawTexture(new Rect(tx - 1f, sy - 1f, tw + 2f, sb + 2f), _white);
            GUI.color = _turbo ? new Color(1f, 0.75f, 0.35f) : new Color(0.8f, 1f, 0.8f);
            GUI.DrawTexture(new Rect(tx, sy + sb * (1f - _throttle), tw, sb * _throttle), _white);
            GUI.color = Color.white;
            Osd(W - m - 260f * k, sy - 24f * k, 260f * k, "THR " + Mathf.RoundToInt(_throttle * 100f) + "%   " + (_climbCmd > 0f ? "CLIMB" : _climbCmd < 0f ? "DESCEND" : "HOLD"), _osdSmall, TextAnchor.UpperRight);

            if (Cfg.DroneHudHints.Value && _mode != Mode.Dead)
            {
                string keys = "MOUSE AIM   W/S FORWARD-BACK   A/D STRAFE   SPACE/CTRL UP-DOWN   SHIFT TURBO   R LEVEL AIM   " +
                              Cfg.DroneKey.Value.ToUpperInvariant() + " BODY   BKSP DROP" + (Cfg.DroneDetonate.Value ? "   CLICK DETONATE" : "");
                Osd(0f, H - m - 18f * k, W, keys, _osdSmall, TextAnchor.UpperCenter);
            }

            if (_mode == Mode.Dead)
            {

                uint rng2 = (uint)(now * 1000f) | 1u;
                float ox = Rubble.Rand(ref rng2) * 40f, oy = Rubble.Rand(ref rng2) * 40f;
                GUI.color = new Color(1f, 1f, 1f, 0.55f);
                GUI.DrawTexture(new Rect(-ox, -oy, W + 40f, H + 40f), _noise, ScaleMode.StretchToFill, false);
                GUI.color = new Color(0f, 0f, 0f, 0.4f); GUI.DrawTexture(new Rect(0f, cy - 44f * k, W, 88f * k), _white);
                GUI.color = Color.white;
                Osd(0f, cy - 34f * k, W, _deathNote, _osdBig, TextAnchor.MiddleCenter);
            }
            GUI.matrix = saved;
            GUI.color = Color.white;
        }
    }
}
