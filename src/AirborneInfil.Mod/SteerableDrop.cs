// ODG Operations - AirborneInfil
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace AirborneInfil
{

    internal static class SteerableDrop
    {
        const int ST_IDLE = 0, ST_WAIT_RELEASE = 1, ST_DESCENT = 2, ST_LANDED = 3, ST_WAIT_GROUND = 4;
        const float LANDED_GUARD = 2f;
        const float LANDED_HARD_CAP = 30f;
        const float RELEASE_TIMEOUT = 12f;
        const float DESCENT_TIMEOUT = 180f;
        const float GROUND_CONFIRM = 0.25f;

        static AirborneMod Mod;

        static MelonPreferences_Entry<bool> _on, _fullAuth, _noFallDamage, _chuteVisible, _freefall, _showHUD, _planeFlyby;
        static MelonPreferences_Entry<bool> _realFlight, _poseOverride;
        static MelonPreferences_Entry<float> _altitude, _gravity, _airControl, _airFriction, _steerSpeed, _descentSpeed;
        static MelonPreferences_Entry<float> _freefallSpeed, _pullAlt, _canopyDrive;

        static int _state = ST_IDLE;
        static float _stateT0, _landedT0;
        static int _infilIndex = -1;
        static bool _sawAirborne;
        static float _groundedSince = -1f, _stillSince = -1f;
        static int _probeFails;
        static bool _snapPending;
        static volatile bool _touchdown;
        static int _aliveThrottle;

        static bool _skyArmed;
        static int _airborneIndex = -1;
        static bool _skyDiagLogged;
        static float _skyFallbackT0 = -1f;
        static float _placedT = -1f;
        internal static void SetAirborneIndex(int idx) { _airborneIndex = idx; }

        internal static int AirborneIndex => _airborneIndex;

        static Il2Cpp.MasterController _mc;
        static Il2CppCMF.AdvancedWalkerController _walker;
        static Il2CppCMF.Mover _mover;
        static Il2Cpp.InteractableChair _watchChair;
        static GameObject _chute, _wind, _dropPlane;
        static AudioSource _windSrc;

        static bool _canopy;
        static float _deployT0 = -999f;
        static float _capAtDeploy;
        static volatile float _sinkCapEff = 7f;
        static volatile float _driveEff;
        static float _drive = 9f;
        static float _heightAGL = 999f, _heightT;
        static Vector3 _planeOrigin, _planeDirV = Vector3.forward;
        static float _planeT0 = -999f;
        const float _planeSpeed = 38f;
        static AudioSource _flutterSrc;
        static Vector3 _spawnFallback;
        static bool _airSpawn;
        static GameObject _detached;
        static float _detachT0;
        static Vector3 _detachDrift;
        static float _liftY;
        static float _holdUntil;

        static float _savGravity, _savAirControl, _savAirFriction;
        static bool _savCanJump, _haveSaved;

        static bool _lgPF, _lgStand, _lgProne;

        internal static void Init(AirborneMod mod)
        {
            Mod = mod;
            var c = MelonPreferences.CreateCategory("SteerableDrop");
            _on           = c.CreateEntry("Enabled", true);
            _altitude     = c.CreateEntry("DropAltitude", 400f);
            if (Mathf.Abs(_altitude.Value - 300f) < 1f) _altitude.Value = 400f;
            _gravity      = c.CreateEntry("DescentGravity", 4f);
            _airControl   = c.CreateEntry("AirControlRate", 2.5f);
            _airFriction  = c.CreateEntry("AirFriction", 0.6f);
            _fullAuth     = c.CreateEntry("FullAuthoritySteering", false);
            _steerSpeed   = c.CreateEntry("SteerSpeed", 8f);
            _descentSpeed = c.CreateEntry("DescentSpeed", 7f);
            _noFallDamage = c.CreateEntry("NoFallDamage", true);
            _chuteVisible = c.CreateEntry("ChuteVisible", true);
            _freefall     = c.CreateEntry("FreefallStage", true);
            _freefallSpeed= c.CreateEntry("FreefallSpeed", 28f);
            _pullAlt      = c.CreateEntry("AutoPullAltitude", 120f);
            _showHUD      = c.CreateEntry("ShowAltimeterHUD", true);
            _planeFlyby   = c.CreateEntry("C130AtJump", true);
            _realFlight   = c.CreateEntry("RealisticCanopyFlight", true);
            _canopyDrive  = c.CreateEntry("CanopyDriveSpeed", 9f);
            _poseOverride = c.CreateEntry("BodyPoseOverride", true);

            Patch(typeof(Il2Cpp.InfiltrationManager), "UserCode_RPC_StartInfil__Int32", nameof(Post_StartInfilIndex));
            PatchPrefix(typeof(Il2Cpp.InfiltrationManager), "UserCode_RPC_SetSpawnPosition__NetworkConnectionToClient__Vector3__Quaternion", nameof(Pre_SpawnPos));

            PatchPrefix(typeof(Il2Cpp.GameManager), "MovePlayerToSpawn", nameof(Pre_MovePlayerToSpawn));
            AirborneMarker.Init(mod);
            AirborneBoard.Init(mod);
            Patch(typeof(Il2Cpp.HelicopterV3), "UserCode_RPC_AutoExitHelicopter", nameof(Post_AutoExitUser));
            Patch(typeof(Il2Cpp.HelicopterV3), "AutoExitHelicopter", nameof(Post_AutoExitDirect));
            PatchPrefix(typeof(Il2Cpp.MasterController), "ProcessFall", nameof(Pre_ProcessFall));
            PatchPrefix(typeof(Il2Cpp.MasterController), "TakeFallDamageStanding", nameof(Pre_FallDmgStand));
            PatchPrefix(typeof(Il2Cpp.MasterController), "TakeFallDamageProne", nameof(Pre_FallDmgProne));
            Patch(typeof(Il2CppCMF.AdvancedWalkerController), "HandleMomentum", nameof(Post_HandleMomentum));
            Log("SteerableDrop armed. Enabled=" + _on.Value + " alt=" + _altitude.Value +
                "m. Insert=test drop (unseated only), End=abort.");
        }

        static void Patch(Type t, string m, string handler)
        {
            try
            {
                var mi = AccessTools.Method(t, m);
                if (mi == null) { Log("DROP HOOK MISS " + t.Name + "." + m); return; }
                Mod.HarmonyInstance.Patch(mi, postfix: new HarmonyMethod(typeof(SteerableDrop), handler));
                Log("drop hooked " + t.Name + "." + m);
            }
            catch (Exception e) { Log("DROP HOOK FAIL " + t.Name + "." + m + ": " + e.Message); }
        }

        static void PatchPrefix(Type t, string m, string handler)
        {
            try
            {
                var mi = AccessTools.Method(t, m);
                if (mi == null) { Log("DROP HOOK MISS " + t.Name + "." + m); return; }
                Mod.HarmonyInstance.Patch(mi, prefix: new HarmonyMethod(typeof(SteerableDrop), handler));
                Log("drop prefixed " + t.Name + "." + m);
            }
            catch (Exception e) { Log("DROP HOOK FAIL " + t.Name + "." + m + ": " + e.Message); }
        }

        static bool _missionLaunched;
        static float _missionLaunchedAt;

        static void Post_StartInfilIndex(int index)
        {
            _infilIndex = index;
            _missionLaunched = true;
            _missionLaunchedAt = Time.time;

            int mine = MyInfilIndex();

            bool air = _airborneIndex >= 0 && mine == _airborneIndex
                    && !_airSpawn && _state == ST_IDLE;
            _skyArmed = air;
            _skyDiagLogged = false; _skyFallbackT0 = -1f; _placedT = -1f;
            Log("mission start: rpc infilMethods idx=" + index + ", my marker idx=" + mine +
                ", airborne marker idx=" + _airborneIndex + " -> " +
                (air ? "AIRBORNE sky-spawn ARMED" : "normal insertion"));
        }

        static int MyInfilIndex()
        {
            try
            {
                var pm = Il2Cpp.PlayerMaster.MyPlayerMaster;
                return pm != null ? pm.CurrentInfilIndex : -1;
            }
            catch { return -1; }
        }

        static void Pre_SpawnPos(Il2CppMirror.NetworkConnectionToClient target, ref Vector3 pos, Quaternion rot)
        { RewriteSpawn(ref pos, "receive"); }

        static void Pre_MovePlayerToSpawn(ref Vector3 position, Quaternion rotation)
        {
            if (!_skyDiagLogged) { _skyDiagLogged = true; Log("[diag] MovePlayerToSpawn fired pos=(" + F(position) + ") skyArmed=" + _skyArmed); }
            _placedT = Time.time;
            RewriteSpawn(ref position, "local");
        }

        static void RewriteSpawn(ref Vector3 pos, string via)
        {
            try
            {
                if (!_skyArmed || !Active() || _state != ST_IDLE) return;
                _spawnFallback = pos;
                pos += Vector3.up * _altitude.Value;
                _airSpawn = true; _skyArmed = false;
                _state = ST_WAIT_GROUND; _stateT0 = Time.time;
                Log("AIRBORNE: spawn rewritten (+" + _altitude.Value.ToString("F0") + "m, " + via + ") - sky start, no ground");
            }
            catch (Exception e) { Log("RewriteSpawn err: " + e.Message); }
        }

        static void Post_AutoExitUser(Il2Cpp.HelicopterV3 __instance) => OnExit(__instance, "UserCode_RPC");
        static void Post_AutoExitDirect(Il2Cpp.HelicopterV3 __instance) => OnExit(__instance, "direct");

        static void OnExit(Il2Cpp.HelicopterV3 heli, string via)
        {
            try
            {
                if (_state == ST_LANDED) { ReleaseRefs(); _state = ST_IDLE; }
                if (!Active() || _state != ST_IDLE) return;

                if (!_skyArmed) { Log("regular heli ride complete - no drop (AIRBORNE not chosen)"); return; }

                bool confirmedInfil = false;
                try { var iva = heli != null ? heli.infilVehicleAnimation : null; confirmedInfil = iva != null && !iva.NetworkisExfil; }
                catch { }
                if (!confirmedInfil) { Log("handoff not confirmed as infil - drop skipped"); return; }
                _skyArmed = false;

                Il2Cpp.InteractableChair myChair = null;
                try { var pn = LocalPN(); myChair = pn != null ? pn.chair : null; } catch { }
                if (myChair == null) { Log("local player was not aboard - drop skipped"); return; }

                _watchChair = myChair;
                _state = ST_WAIT_RELEASE; _stateT0 = Time.time;
                Log("handoff detected (" + via + ") - waiting for OUR seat to release...");
            }
            catch (Exception e) { Log("OnExit err: " + e.Message); }
        }

        static bool Pre_ProcessFall(Il2Cpp.MasterController __instance)
        {
            if (!IsLocalGuarded(__instance)) return true;
            if (!_lgPF) { _lgPF = true; Log("ProcessFall intercepted (patch NOT inlined) - fall damage off for drop"); }
            if (_state == ST_DESCENT) _touchdown = true;
            return false;
        }
        static bool Pre_FallDmgStand(Il2Cpp.MasterController __instance)
        {
            if (!IsLocalGuarded(__instance)) return true;
            if (!_lgStand) { _lgStand = true; Log("TakeFallDamageStanding intercepted - skipped"); }
            return false;
        }
        static bool Pre_FallDmgProne(Il2Cpp.MasterController __instance)
        {
            if (!IsLocalGuarded(__instance)) return true;
            if (!_lgProne) { _lgProne = true; Log("TakeFallDamageProne intercepted - skipped"); }
            return false;
        }
        static bool IsLocalGuarded(Il2Cpp.MasterController inst)
        {
            if (!_noFallDamage.Value || _mc == null || inst == null) return false;
            if (inst.Pointer != _mc.Pointer) return false;
            if (_state == ST_DESCENT) return true;
            return _state == ST_LANDED;
        }

        static void Post_HandleMomentum(Il2CppCMF.AdvancedWalkerController __instance)
        {
            try
            {
                if (_state != ST_DESCENT || _walker == null || __instance == null) return;
                if (__instance.Pointer != _walker.Pointer) return;

                try { if (_sawAirborne && __instance.IsGrounded()) return; } catch { }

                if (_realFlight.Value)
                {
                    var cam = Camera.main;
                    Vector3 fwd = Vector3.forward, right = Vector3.right;
                    if (cam != null)
                    {
                        fwd = cam.transform.forward; fwd.y = 0f;
                        fwd = fwd.sqrMagnitude > 0.001f ? fwd.normalized : Vector3.forward;
                        right = cam.transform.right; right.y = 0f;
                        right = right.sqrMagnitude > 0.001f ? right.normalized : Vector3.right;
                    }
                    if (!_canopy)
                    {

                        Vector3 track = Vector3.zero;
                        if (Input.GetKey(KeyCode.W)) track += fwd;
                        if (Input.GetKey(KeyCode.S)) track -= fwd;
                        if (Input.GetKey(KeyCode.D)) track += right;
                        if (Input.GetKey(KeyCode.A)) track -= right;
                        if (track.sqrMagnitude > 1f) track.Normalize();
                        __instance.SetMomentum(track * 6f + Vector3.down * _sinkCapEff);
                    }
                    else
                    {

                        float lat = 0f;
                        if (Input.GetKey(KeyCode.D)) lat += 2.5f;
                        if (Input.GetKey(KeyCode.A)) lat -= 2.5f;
                        __instance.SetMomentum(fwd * _driveEff + right * lat + Vector3.down * _sinkCapEff);
                    }
                }
                else if (_fullAuth.Value)
                {
                    Vector3 steer = Vector3.zero;
                    var cam = Camera.main;
                    if (cam != null)
                    {
                        var fwd2 = cam.transform.forward; fwd2.y = 0f; fwd2.Normalize();
                        var right2 = cam.transform.right; right2.y = 0f; right2.Normalize();
                        if (Input.GetKey(KeyCode.W)) steer += fwd2;
                        if (Input.GetKey(KeyCode.S)) steer -= fwd2;
                        if (Input.GetKey(KeyCode.D)) steer += right2;
                        if (Input.GetKey(KeyCode.A)) steer -= right2;
                        if (steer.sqrMagnitude > 1f) steer.Normalize();
                    }
                    __instance.SetMomentum(steer * _steerSpeed.Value + Vector3.down * _sinkCapEff);
                }
                else
                {

                    var m = __instance.GetMomentum();
                    if (m.y < -_sinkCapEff) { m.y = -_sinkCapEff; __instance.SetMomentum(m); }
                }

                try
                {
                    var rbc = _mc != null ? _mc.rigidBody : null;
                    if (rbc != null)
                    {
                        var v = rbc.linearVelocity;
                        if (v.y < -_sinkCapEff) { v.y = -_sinkCapEff; rbc.linearVelocity = v; }
                    }
                }
                catch { }

                if (_noFallDamage.Value && _mc != null)
                {
                    try { _mc.velocityLastFrame = Vector3.zero; } catch { }
                }
            }
            catch { }
        }

        internal static void Update()
        {
            try
            {

                if (Input.GetKeyDown(KeyCode.Insert) && Active() && _state == ST_IDLE)
                {
                    if (IsSeated()) Log("airborne refused - get out of the seat/vehicle first");
                    else { Log("AIRBORNE drop triggered (Insert)"); Lift(false); }
                }
                if (Input.GetKeyDown(KeyCode.End) && (_state == ST_DESCENT || _state == ST_WAIT_RELEASE || _state == ST_WAIT_GROUND))
                    SoftAbort("aborted (End)");
                if (Input.GetKeyDown(KeyCode.Home)) PoseDriver.Load(true);

                AirborneMarker.Update();
                AirborneBoard.Update();

                if (_state == ST_DESCENT && ++_aliveThrottle % 20 == 0)
                {
                    bool aliveNow = true;
                    try { var p = Il2Cpp.PlayerMaster.MyPlayerMaster; aliveNow = p == null || p.currentlySpawnedAndAlive; } catch { }
                    if (!aliveNow) Restore("died mid-descent");
                }

                if (_skyArmed && _state == ST_IDLE && Active())
                {
                    try
                    {
                        var im = Il2Cpp.InfiltrationManager.instance;
                        var pm = Il2Cpp.PlayerMaster.MyPlayerMaster;
                        bool alive = pm != null && pm.currentlySpawnedAndAlive;
                        var pnA = LocalPN();
                        if (im != null && alive && pnA != null && pnA.masterController != null && !IsSeated())
                        {
                            if (_skyFallbackT0 < 0f) _skyFallbackT0 = Time.time;

                            Vector3 rp = Vector3.zero;
                            try { var rbB = pnA.masterController.rigidBody; rp = rbB != null ? rbB.position : pnA.masterController.transform.position; } catch { }
                            bool atLZ = new Vector2(rp.x, rp.z).magnitude > 8f;
                            bool placed = _placedT > 0f && Time.time - _placedT > 0.2f;
                            bool safety = Time.time - _skyFallbackT0 > 6f;
                            if ((atLZ && Time.time - _skyFallbackT0 > 0.1f) || placed || safety)
                            {
                                Log("AIRBORNE lift (" + (atLZ ? "at LZ " + F(rp) : placed ? "post-placement" : "safety - LZ may be near origin") + ")");
                                _skyArmed = false; _skyFallbackT0 = -1f; _placedT = -1f;
                                Lift(false);
                            }
                        }
                        else _skyFallbackT0 = -1f;
                    }
                    catch { }
                }

                switch (_state)
                {
                    case ST_WAIT_RELEASE: TickWaitRelease(); break;
                    case ST_WAIT_GROUND: TickWaitGround(); break;
                    case ST_DESCENT: TickDescent(); break;
                    case ST_LANDED: TickLanded(); break;
                }
            }
            catch { }
        }

        internal static void LateTick()
        {
            if (_state == ST_DESCENT && _poseOverride != null && _poseOverride.Value)
            {
                try { PoseDriver.Apply(); } catch { }
            }
            if (_state != ST_DESCENT || _chute == null || _mc == null || Mod == null) return;
            try
            {

                float t = Time.time;
                float pop = EaseOutBack(Mathf.Clamp01((t - _deployT0) / 0.7f));
                Vector3? body = null;
                try { body = _mc.transform.position; } catch { }
                Mod.PlaceChuteAnchored(_chute, Mathf.Max(0.05f, pop), body);
            }
            catch { }
        }

        static void TickWaitRelease()
        {
            if (Time.time - _stateT0 > RELEASE_TIMEOUT) { _watchChair = null; _state = ST_IDLE; Log("seat release timed out - drop skipped"); return; }
            try
            {

                bool released = false;
                try { released = _watchChair == null || !_watchChair.NetworkinUse; } catch { released = false; }
                var pn = LocalPN();
                bool blending = false;
                try { var mc = pn != null ? pn.masterController : null; blending = mc != null && mc.IsSeatBlending; } catch { }
                if (released && !blending && Time.time - _stateT0 > 0.5f) { _watchChair = null; Lift(false); }
            }
            catch (Exception e) { Log("WaitRelease err: " + e.Message); }
        }

        static float _freeSince = -1f;
        static bool _waitLoggedSeated;

        static void TickWaitGround()
        {
            if (Time.time - _stateT0 > 150f)
            {
                _skyArmed = false;
                _state = ST_IDLE;
                Log("insertion wait timed out - airborne skipped");
                return;
            }
            try
            {
                var pm = Il2Cpp.PlayerMaster.MyPlayerMaster;
                bool alive = false;
                try { alive = pm != null && pm.currentlySpawnedAndAlive; } catch { }
                var pn = LocalPN();
                var mc = pn != null ? pn.masterController : null;

                if (_airSpawn)
                {
                    if (IsSeated())
                    {

                        _airSpawn = false;
                        return;
                    }
                    if (!alive || mc == null) return;

                    float haveY = float.NaN;
                    try { var rbW = mc.rigidBody; haveY = rbW != null ? rbW.position.y : mc.transform.position.y; } catch { }
                    bool up = !float.IsNaN(haveY) && haveY > _spawnFallback.y + 0.5f * _altitude.Value;
                    if (!up && Time.time - _stateT0 < 4f) return;

                    _skyArmed = false;
                    _missionLaunched = false;
                    _airSpawn = false;
                    if (up) Lift(false, null, true);
                    else
                    {
                        Log("sky spawn never reached altitude (y=" + haveY.ToString("F0") + ", LZ y=" +
                            _spawnFallback.y.ToString("F0") + ") - lifting from the LZ instead");
                        Lift(false, _spawnFallback.sqrMagnitude > 1f ? _spawnFallback : (Vector3?)null);
                    }
                    return;
                }

                if (!alive || mc == null || Time.time - _stateT0 < 0.5f) return;

                if (IsSeated())
                {
                    _freeSince = -1f;
                    if (!_waitLoggedSeated)
                    { _waitLoggedSeated = true; Log("insertion ride detected - jumping when it finishes"); }
                    return;
                }
                if (_freeSince < 0f) { _freeSince = Time.time; return; }

                if (Time.time - _freeSince < (_waitLoggedSeated ? 1.0f : 0.3f)) return;

                _skyArmed = false;
                _missionLaunched = false;
                bool rodeIn = _waitLoggedSeated;
                _waitLoggedSeated = false; _freeSince = -1f;

                Lift(false, !rodeIn && _spawnFallback.sqrMagnitude > 1f ? _spawnFallback : (Vector3?)null);
            }
            catch (Exception e) { Log("WaitGround err: " + e.Message); }
        }

        static void Lift(bool testDrop, Vector3? groundOverride = null, bool alreadyAirborne = false)
        {
            bool teleported = false;
            Vector3 preLift = Vector3.zero;
            Rigidbody rb = null;
            try
            {
                var pn = LocalPN();
                if (pn == null) { Log("no local player - drop aborted"); _state = ST_IDLE; return; }
                _mc = pn.masterController;
                if (_mc == null) { Log("no MasterController - drop aborted"); _state = ST_IDLE; return; }
                _walker = _mc.walkerController;
                _mover = _mc.mover;
                rb = _mc.rigidBody;
                if (_walker == null || rb == null) { Log("no walker/rigidbody - drop aborted"); ReleaseRefs(); _state = ST_IDLE; return; }

                preLift = rb.position;
                Vector3 ground = groundOverride.HasValue ? groundOverride.Value : preLift;
                if (ground.sqrMagnitude < 1f && preLift.sqrMagnitude >= 1f)
                {
                    Log("drop point looked like world origin - using current position instead");
                    ground = preLift;
                }
                float alt = testDrop ? Mathf.Min(150f, _altitude.Value) : _altitude.Value;
                Vector3 target;

                if (alreadyAirborne)
                {

                    Vector3 intended = _spawnFallback + Vector3.up * alt;
                    bool stale = _spawnFallback.sqrMagnitude > 1f && preLift.y < _spawnFallback.y + 0.5f * alt;
                    if (stale) Log("sky spawn: body reads " + F(preLift) + " but spawn was " + F(intended) + " - using the spawn");
                    target = stale ? intended : preLift;
                }
                else
                {

                    target = ground + Vector3.up * alt;
                }

                _savGravity = _walker.gravity; _savAirControl = _walker.airControlRate;
                _savAirFriction = _walker.airFriction; _savCanJump = _walker.canJump;
                _haveSaved = true;

                if (!alreadyAirborne)
                {
                    try { rb.linearVelocity = Vector3.zero; } catch { }
                    rb.position = target;
                    try { _mc.transform.position = target; } catch { }
                    teleported = true;
                    try { _walker.SetMomentum(Vector3.down * 2f); } catch { }

                    try
                    {
                        var ss = pn.GetComponentInChildren<Il2CppSmooth.SmoothSyncMirror>(true);
                        if (ss != null) ss.teleportOwnedObjectFromOwner();
                    }
                    catch (Exception e) { Log("SmoothSync teleport skipped: " + e.Message); }
                    SnapCamera(pn);
                    _snapPending = true;

                    try { _walker.OnGroundContactLost(); } catch { }
                    try { if (_mover != null) _mover.SetVelocity(Vector3.down * 3f); } catch { }
                }

                _walker.gravity = _gravity.Value;
                _walker.airControlRate = _airControl.Value;
                _walker.airFriction = _airFriction.Value;
                _walker.canJump = false;

                _canopy = !(_freefall.Value && alt > _pullAlt.Value + 50f);
                _deployT0 = _canopy ? Time.time : -999f;
                _capAtDeploy = _freefallSpeed.Value;
                _sinkCapEff = _canopy ? _descentSpeed.Value : _freefallSpeed.Value;
                _drive = _canopyDrive.Value; _driveEff = _canopy ? _drive : 0f;
                _heightAGL = alt; _heightT = 0f;

                if (_poseOverride.Value)
                {
                    try
                    {
                        PoseDriver.CacheBones(pn);
                        PoseDriver.SetStage(_canopy ? "canopy" : "freefall");
                        PoseDriver.SuppressRig(true);
                    }
                    catch (Exception e) { Log("pose cache failed (continuing): " + e.Message); }
                }

                if (_canopy) SpawnChute();
                try
                {
                    if (Mod != null)
                    {
                        _wind = Mod.MakeWindPlayer();
                        _windSrc = _wind != null ? _wind.GetComponent<AudioSource>() : null;
                        SetWind(_canopy ? 0.5f : 0.85f);
                    }
                }
                catch (Exception e) { Log("wind failed (continuing): " + e.Message); }

                if (_planeFlyby.Value)
                {
                    try
                    {
                        _dropPlane = Mod != null ? Mod.GetPlaneClone() : null;
                        if (_dropPlane != null)
                        {
                            var camT = Camera.main != null ? Camera.main.transform : null;
                            var d = camT != null ? camT.forward : Vector3.forward;
                            d.y = 0f;
                            _planeDirV = d.sqrMagnitude > 0.04f ? d.normalized : Vector3.forward;

                            _planeOrigin = target + Vector3.up * 22f - _planeDirV * (_planeSpeed * 4.5f);
                            _planeT0 = Time.time;
                            Sounds.AttachPlaneAudio(_dropPlane, 1f);
                        }
                    }
                    catch (Exception e) { Log("C130 flyby failed (continuing): " + e.Message); }
                }

                _sawAirborne = false; _groundedSince = -1f; _stillSince = -1f; _probeFails = 0;
                _touchdown = false;
                _lgPF = _lgStand = _lgProne = false;
                _liftY = target.y;
                _holdUntil = alreadyAirborne ? 0f : Time.time + 0.6f;
                _state = ST_DESCENT; _stateT0 = Time.time;
                Log("DROP: lifted to " + F(target) + " (alt " + alt.ToString("F0") + "m, " +
                    (testDrop ? "test" : "infil " + _infilIndex) + ") " +
                    (_canopy ? "CANOPY" : "FREEFALL (auto-pull " + _pullAlt.Value.ToString("F0") + "m AGL, F = pull now)") +
                    (_fullAuth.Value ? " FULL-AUTH" : " native steering"));
            }
            catch (Exception e)
            {
                Log("Lift err: " + e.Message);

                if (teleported && rb != null)
                {
                    try { rb.position = preLift; if (_mc != null) _mc.transform.position = preLift; } catch { }
                    Log("lift failed after teleport - returned player to " + F(preLift));
                }
                Restore("lift failed");
            }
        }

        static void SpawnChute()
        {
            try
            {
                if (_chuteVisible.Value && Mod != null && _chute == null)
                {
                    _chute = Mod.GetChuteClone();
                    Mod.ResetChuteAnchor();
                    _flutterSrc = Sounds.AttachFlutter(_chute);
                }
            }
            catch (Exception e) { Log("chute visual failed (continuing): " + e.Message); }
        }

        static void PullChute()
        {
            _canopy = true;
            _deployT0 = Time.time;
            try { _capAtDeploy = Mathf.Abs(_walker != null ? _walker.GetVelocity().y : _freefallSpeed.Value); }
            catch { _capAtDeploy = _freefallSpeed.Value; }
            SpawnChute();
            Sounds.PlayWhump();
            SetWind(0.5f);
            _driveEff = _drive;
            if (_poseOverride.Value) { try { PoseDriver.SetStage("canopy"); } catch { } }
            Log("CANOPY DEPLOYED at " + _heightAGL.ToString("F0") + "m AGL");
        }

        static void SetWind(float vol)
        {
            try { if (_windSrc != null) _windSrc.volume = vol; } catch { }
        }

        static void SnapCamera(Il2Cpp.PlayerNetworking pn)
        {
            try
            {
                var cam = pn != null ? pn.GetComponentInChildren<Il2Cpp.Character_FollowCamera>(true) : null;
                if (cam != null) cam.SnapToTarget();
            }
            catch { }
        }

        static void TickDescent()
        {
            if (_mc == null || _walker == null) { Restore("refs lost"); return; }
            if (Time.time - _stateT0 > DESCENT_TIMEOUT) { Restore("descent timeout"); return; }

            if (_snapPending) { _snapPending = false; SnapCamera(LocalPN()); }

            if (Time.time < _holdUntil)
            {
                try
                {
                    var rbh = _mc.rigidBody;
                    if (rbh != null && rbh.position.y < _liftY - 20f)
                    {
                        var hp = rbh.position; hp.y = _liftY;
                        rbh.position = hp; _mc.transform.position = hp;
                        _walker.SetMomentum(Vector3.down * 4f);
                        try { _walker.OnGroundContactLost(); } catch { }
                        try { if (_mover != null) _mover.SetVelocity(Vector3.down * 4f); } catch { }
                    }
                }
                catch { }
            }

            if (Time.time - _heightT > 0.2f)
            {
                _heightT = Time.time;
                try
                {
                    Vector3 p = _walker.transform.position;
                    RaycastHit hit;
                    _heightAGL = Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, out hit, 600f) ? hit.distance : 999f;
                }
                catch { }
            }

            bool manualPull = Input.GetKeyDown(KeyCode.F) && Time.time - _stateT0 > 0.75f;
            if (!_canopy && (_heightAGL < _pullAlt.Value || manualPull || Time.time - _stateT0 > 4f))
                PullChute();

            float cap;
            if (!_canopy) cap = _freefallSpeed.Value;
            else
            {
                float ease = Mathf.Clamp01((Time.time - _deployT0) / 1.2f);
                cap = Mathf.Lerp(_capAtDeploy, _descentSpeed.Value, ease * ease * (3f - 2f * ease));
                bool flaring = Input.GetKey(KeyCode.S) && _heightAGL < 25f;
                if (flaring) cap = Mathf.Min(cap, 2.5f);
                if (_heightAGL < 15f) cap = Mathf.Lerp(2f, cap, _heightAGL / 15f);

                if (Input.GetKey(KeyCode.W)) _drive += 4f * Time.deltaTime;
                if (Input.GetKey(KeyCode.S)) _drive -= 5f * Time.deltaTime;
                _drive = Mathf.Clamp(_drive, 3f, 13f);
                float ease2 = Mathf.Clamp01((Time.time - _deployT0) / 1.2f);
                _driveEff = (flaring ? 3f : _drive) * ease2;
            }
            _sinkCapEff = cap;

            try
            {
                var v = _walker.GetVelocity();
                float speed = v.magnitude;
                if (_windSrc != null)
                {
                    _windSrc.pitch = 0.75f + speed / 55f;
                    float baseVol = _canopy ? 0.4f : 0.85f;
                    _windSrc.volume = baseVol * Mathf.Clamp01(0.35f + speed / 30f);
                }
                if (_flutterSrc != null && _canopy)
                    _flutterSrc.volume = Mathf.Clamp01(speed / 18f) * 0.55f + 0.1f;
            }
            catch { }

            if (_dropPlane != null)
            {
                float ft = Time.time - _planeT0;
                if (ft > 60f) { try { UObject.Destroy(_dropPlane); } catch { } _dropPlane = null; }
                else if (Mod != null) Mod.PlaceDropPlane(_dropPlane, _planeOrigin + _planeDirV * (_planeSpeed * ft), _planeDirV);
            }

            float descended = 0f;
            try { descended = _liftY - _walker.transform.position.y; } catch { }
            if (descended < 25f && _heightAGL > 8f) return;

            if (_touchdown && _sawAirborne && _heightAGL < 12f) { Land(); return; }

            bool grounded = false, probeOK = false;
            try { grounded = _mover != null ? _mover.IsGrounded() : _walker.IsGrounded(); probeOK = true; }
            catch { }
            if (!probeOK)
            {
                if (++_probeFails > 120) { Restore("grounded probe dead"); return; }
            }
            else
            {
                _probeFails = 0;

                if (!grounded) { _sawAirborne = true; _groundedSince = -1f; _touchdown = false; }
                else if (_sawAirborne)
                {
                    if (_groundedSince < 0f) _groundedSince = Time.time;
                    if (Time.time - _groundedSince >= GROUND_CONFIRM) { Land(); return; }
                }
            }

            try
            {
                float vy = Mathf.Abs(_walker.GetVelocity().y);
                if (_sawAirborne && vy < 0.5f)
                {
                    if (_stillSince < 0f) _stillSince = Time.time;
                    else if (Time.time - _stillSince > 1.5f) { Land(); return; }
                }
                else _stillSince = -1f;
            }
            catch { }
        }

        static void TickLanded()
        {
            SuppressImpact();

            if (_detached != null)
            {
                float age = Time.time - _detachT0;
                if (age > 2.6f) { try { UObject.Destroy(_detached); } catch { } _detached = null; }
                else
                {
                    try
                    {
                        var t = _detached.transform;
                        t.position += (_detachDrift + Vector3.down * 3.2f) * Time.deltaTime;
                        t.rotation = Quaternion.Slerp(t.rotation,
                            Quaternion.Euler(80f, t.eulerAngles.y, 15f), Time.deltaTime * 2.5f);
                        var s = t.localScale;
                        t.localScale = Vector3.Lerp(s, new Vector3(s.x, s.y * 0.2f, s.z), Time.deltaTime * 1.6f);

                        try { var fs = _detached.GetComponent<AudioSource>(); if (fs != null) fs.volume *= 1f - 2.5f * Time.deltaTime; } catch { }
                    }
                    catch { }
                }
            }

            bool grounded = true;
            try { grounded = _mover != null ? _mover.IsGrounded() : true; } catch { }
            bool guardDone = grounded && Time.time - _landedT0 > LANDED_GUARD && _detached == null;
            if (guardDone || Time.time - _landedT0 > LANDED_HARD_CAP)
            {
                ReleaseRefs();
                _state = ST_IDLE;
                Log("drop complete - all refs released");
            }
        }

        static void SuppressImpact()
        {
            if (!_noFallDamage.Value || _mc == null) return;
            try { _mc.velocityLastFrame = Vector3.zero; } catch { }
        }

        static void Land()
        {
            _landedT0 = Time.time;
            Sounds.PlayCollapse();

            if (_chute != null)
            {
                _detached = _chute; _chute = null;
                try { _detached.transform.SetParent(null, true); } catch { }
                _detachT0 = Time.time;
                Vector3 drift = Vector3.back;
                try
                {
                    var v = _walker != null ? _walker.GetVelocity() : Vector3.zero;
                    var flat = new Vector3(v.x, 0f, v.z);
                    drift = flat.sqrMagnitude > 0.5f ? -flat.normalized : Vector3.back;
                }
                catch { }
                _detachDrift = drift * 2.2f;
            }
            RestoreTuningAndProps("LANDED");
            _state = ST_LANDED;
        }

        static void SoftAbort(string why)
        {
            if (_state == ST_WAIT_RELEASE || _state == ST_WAIT_GROUND)
            { _watchChair = null; _skyArmed = false; _state = ST_IDLE; Log("drop cancelled [" + why + "]"); return; }
            _landedT0 = Time.time;
            RestoreTuningAndProps(why);
            _state = ST_LANDED;
        }

        internal static void Abort(string why)
        {
            if (_state != ST_IDLE) Restore(why);
            _watchChair = null;
            _infilIndex = -1;
            _airSpawn = false; _waitLoggedSeated = false; _freeSince = -1f;

            _skyArmed = false; _skyFallbackT0 = -1f; _placedT = -1f;
            _state = ST_IDLE;
        }

        static void RestoreTuningAndProps(string why)
        {
            try
            {
                if (_haveSaved && _walker != null)
                {
                    try
                    {
                        _walker.gravity = _savGravity;
                        _walker.airControlRate = _savAirControl;
                        _walker.airFriction = _savAirFriction;
                        _walker.canJump = _savCanJump;
                    }
                    catch { }
                }
                _haveSaved = false;
                if (_chute != null) { try { UObject.Destroy(_chute); } catch { } _chute = null; }
                if (_wind != null) { try { UObject.Destroy(_wind); } catch { } _wind = null; }
                _windSrc = null; _flutterSrc = null;
                if (_dropPlane != null) { try { UObject.Destroy(_dropPlane); } catch { } _dropPlane = null; }
                _canopy = false; _deployT0 = -999f; _driveEff = 0f;
                try { PoseDriver.SuppressRig(false); } catch { }
                try { PoseDriver.Release(); } catch { }
                Log("drop end [" + why + "] - walker restored");
            }
            catch (Exception e) { Log("RestoreTuning err: " + e.Message); }
        }

        static void ReleaseRefs()
        {
            _mc = null; _walker = null; _mover = null; _watchChair = null;
            _infilIndex = -1;
            if (_detached != null) { try { UObject.Destroy(_detached); } catch { } _detached = null; }
        }

        static void Restore(string why)
        {
            RestoreTuningAndProps(why);
            ReleaseRefs();
            _state = ST_IDLE;
        }

        static bool Active() => _on != null && _on.Value && Mod != null && AirborneMod.MasterEnabled;

        internal static bool TakesOver => _on != null && _on.Value;

        static bool IsSeated()
        {
            try
            {
                var pn = LocalPN();
                if (pn == null) return false;
                if (pn.chair != null) return true;
                var mc = pn.masterController;
                return mc != null && mc.IsSeatBlending;
            }
            catch { return false; }
        }

        static Il2Cpp.PlayerNetworking LocalPN()
        {
            try
            {
                var pm = Il2Cpp.PlayerMaster.MyPlayerMaster;
                return pm != null ? pm.PlayerSpawnedObject : null;
            }
            catch { return null; }
        }

        static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        static GUIStyle _hudStyle;
        internal static void DrawHUD()
        {
            if (_state != ST_DESCENT || _showHUD == null || !_showHUD.Value) return;
            try
            {
                if (_hudStyle == null)
                {
                    _hudStyle = new GUIStyle();
                    _hudStyle.fontSize = 18;
                    _hudStyle.fontStyle = FontStyle.Bold;
                    _hudStyle.alignment = TextAnchor.UpperCenter;
                }
                float sink = 0f, spd = 0f;
                try { var v = _walker != null ? _walker.GetVelocity() : Vector3.zero; sink = -v.y; spd = new Vector3(v.x, 0f, v.z).magnitude; }
                catch { }
                string alt = _heightAGL > 900f ? "---" : _heightAGL.ToString("F0");
                string mode = _canopy
                    ? (_realFlight != null && _realFlight.Value
                        ? "CANOPY  DRIVE " + _drive.ToString("F0") + "  -  fly where you look, [W/S] speed, hold [S] low = flare"
                        : "CANOPY")
                    : "FREEFALL  [F] deploy  [WASD] track";
                string txt = "ALT " + alt + "m    SINK " + sink.ToString("F1") + " m/s    SPD " + spd.ToString("F1") + " m/s\n" + mode;
                var r = new Rect(Screen.width / 2f - 250f, 46f, 500f, 60f);
                _hudStyle.normal.textColor = Color.black;
                GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), txt, _hudStyle);
                _hudStyle.normal.textColor = _canopy ? Color.white : new Color(1f, 0.85f, 0.3f);
                GUI.Label(r, txt, _hudStyle);
            }
            catch { }
        }

        static string F(Vector3 v) => v.x.ToString("F0") + "," + v.y.ToString("F0") + "," + v.z.ToString("F0");
        static void Log(string s) => AirborneMod.Log("[drop] " + s);
    }
}
