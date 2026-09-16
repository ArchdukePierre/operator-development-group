// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace MapLoader
{

    internal static class BlackHawk
    {
        static MelonLogger.Instance _log;
        public static bool Enabled = true;

        const float ApproachDist = 300f;
        const float ApproachAlt  = 60f;
        const float HoverAlt     = 26f;
        const float RopeHoverAlt = 11.5f;
        const float GearClear    = 0.04f;
        const float ApproachS    = 14f;
        const float DescendS     = 5f;
        const float ClimbS       = 5f;
        const float DepartS      = 14f;
        const float RotorRpm     = 258f;
        const float TailRpm      = 1190f;
        const float SpinUpS      = 6f;
        const float BoardRangeM  = 5f;
        const float ArriveM      = 1.3f;
        const float DismountWaitS = 30f;
        const float SitFallbackS = 3f;
        const float RopeOutS     = 0.3f;
        const float SlideReadyS  = 0.8f;
        const float DoorS        = 0.35f;
        const float SlideSpeed   = 5f;
        const float FadeS        = 3f;
        const float BodyRootAboveFloor = 1.1f;

        static readonly Vector3 CabinCentre = new Vector3(0f, 1.15f, 2.3f);
        static readonly Vector3[] RopeAnchorLocal = { new Vector3(1.75f, 2.5f, 2.2f), new Vector3(-1.75f, 2.5f, 2.2f) };
        static readonly Vector3[] RopeStowLocal   = { new Vector3(1.0f, 1.2f, 2.2f),  new Vector3(-1.0f, 1.2f, 2.2f) };

        enum Mode { None, Infil, Exfil }
        enum Leg  { Idle, Approach, Descend, Hover, Landed, Climb, Depart, Done }
        enum Exit { Auto, Free, Pad, Rope }

        sealed class Seat
        {
            public int Index;
            public Transform Anchor;
            public Il2Cpp.InteractableChair Chair;
            public Il2Cpp.FullBodyInteraction Fbi;
            public Transform SitPoint { get { try { if (Chair != null && Chair.playerPosition != null) return Chair.playerPosition; } catch { } return Anchor; } }
            public int Side;
            public Vector3 FaceLocal = Vector3.right;
            public Quaternion Facing(Transform airframe)
            {
                Vector3 f = airframe != null ? airframe.TransformDirection(FaceLocal) : FaceLocal; f.y = 0f;
                if (f.sqrMagnitude < 1e-4f) f = Vector3.right;
                return Quaternion.LookRotation(f.normalized, Vector3.up);
            }
        }

        sealed class Bolt
        {
            public Seat Seat; public Il2Cpp.PlayerNetworking Pn; public Transform Root; public Rigidbody Rb;
            public uint NetId; public bool Local;
            public readonly List<Il2CppSmooth.SmoothSyncMirror> Syncs = new List<Il2CppSmooth.SmoothSyncMirror>();
            public float SitAskedAt; public bool Seated;
            public float StandAskedAt; public bool Standing;
            public Transform CamParent; public Vector3 CamPos; public Quaternion CamRot; public bool CamSaved;
            public RigidbodyInterpolation Interp; public bool InterpSaved;
            public Transform RootParent; public bool Parented; public bool ReparentNoted;
            public float DiagAt;

            public int RopeIndex = -1; public float SlideStart = -1f; public Vector3 HoldPos; public Vector3 SlidePos; public Quaternion SlideRot; public bool SlidePlaced;
            public Il2CppRootMotion.FinalIK.FullBodyBipedIK Ik; public bool HandsOn;
        }

        static Mode _mode; static Leg _leg; static float _legStart;
        static Vector3 _pad;
        static Vector3 _from, _to; static Quaternion _fromRot, _toRot;
        static GameObject _root;
        static Transform _mainRotor, _tailRotor;
        static readonly Transform[] _ropeAnchors = new Transform[2], _ropeStows = new Transform[2];
        static readonly FastRope[] _ropes = new FastRope[2];
        static readonly Bolt[] _ropeSlider = new Bolt[2];
        static Il2Cpp.HelicopterV3 _heli;
        static string _texDir;
        static readonly List<Seat> _seats = new List<Seat>();
        static readonly List<Bolt> _bolts = new List<Bolt>();
        static readonly List<Bolt> _sliders = new List<Bolt>();
        static readonly List<Bolt> _queue = new List<Bolt>();
        static readonly HashSet<IntPtr> _ourChairs = new HashSet<IntPtr>();
        static AudioSource _rotorSrc, _turbineSrc, _tailSrc; static bool _rotorFromFile;
        static float _rotorSpin, _gain, _gainTarget;
        static bool _armed, _infilFlown, _exfilStarted, _zonesOnHeli, _forceLift, _startSeen, _ropesOut, _retracting;
        static float _nextPoll, _nextStatus, _touchdownAt, _hoverAt, _liftAt, _nextLate, _ridingSince;
        static float _localReleasedAt = -1e9f;
        static int _pilots, _aboardLast;
        static string _padSource = "", _cabinSource = "";

        public static bool Riding { get; private set; }
        public static float RidingFor { get { return Riding ? Time.realtimeSinceStartup - _ridingSince : 0f; } }

        public static void Init(MelonLogger.Instance log) { _log = log; }

        public static void Install(HarmonyLib.Harmony h, MelonLogger.Instance log)
        {
            _log = log;
            int ok = 0;
            ok += Hook(h, typeof(Il2Cpp.InfiltrationManager), "UserCode_RPC_StartInfil__Int32", null, nameof(Post_StartInfil));

            ok += Hook(h, typeof(Il2Cpp.InteractableChair), "CMD_StartInteraction", nameof(Pre_CmdStartInteraction), null);
            ok += Hook(h, typeof(Il2Cpp.InteractableChair), "CMD_Stand", nameof(Pre_CmdStand), null);
            ok += Hook(h, typeof(Il2Cpp.InteractableChair), "CMD_StartStanding", nameof(Pre_CmdStartStanding), null);
            ok += Hook(h, typeof(Il2Cpp.InteractableChair), "Stand", nameof(Pre_Stand), null);

            ok += Hook(h, typeof(Il2Cpp.MasterController), "TakeFallDamageStanding", nameof(Pre_FallStanding), null);
            ok += Hook(h, typeof(Il2Cpp.MasterController), "TakeFallDamageProne", nameof(Pre_FallProne), null);
            _log?.Msg("[heli] Black Hawk: " + ok + "/7 hooks. The ride in arms on the game's mission-start RPC; the ride out on global extraction.");
        }

        static int Hook(HarmonyLib.Harmony h, Type t, string method, string prefix, string postfix)
        {
            try
            {
                var m = AccessTools.Method(t, method);
                if (m == null) { _log?.Warning("[heli] hook: " + t.Name + "." + method + " not found"); return 0; }
                h.Patch(m,
                        prefix:  prefix  != null ? new HarmonyMethod(typeof(BlackHawk), prefix)  : null,
                        postfix: postfix != null ? new HarmonyMethod(typeof(BlackHawk), postfix) : null);
                return 1;
            }
            catch (Exception e) { _log?.Warning("[heli] hook " + t.Name + "." + method + ": " + e.Message); return 0; }
        }

        public static void ResetForNewLevel(string why)
        {
            try { ReleaseAll("scene change", Exit.Free); } catch { }
            try { EvictStrangers(); } catch { }
            for (int i = 0; i < 2; i++) { try { if (_ropes[i] != null) _ropes[i].Destroy(); } catch { } _ropes[i] = null; _ropeSlider[i] = null; _ropeAnchors[i] = null; _ropeStows[i] = null; }
            try { if (_root != null) UnityEngine.Object.Destroy(_root); } catch { }
            _root = null; _mainRotor = _tailRotor = null; _heli = null; _seats.Clear(); _ourChairs.Clear(); _queue.Clear();
            _rotorSrc = _turbineSrc = _tailSrc = null; _rotorFromFile = false;
            _mode = Mode.None; _leg = Leg.Idle; _rotorSpin = 0f; _gain = _gainTarget = 0f;
            _armed = _infilFlown = _exfilStarted = _zonesOnHeli = _forceLift = _startSeen = _ropesOut = _retracting = false;
            _nextPoll = _nextStatus = _touchdownAt = _hoverAt = _liftAt = _nextLate = _ridingSince = 0f;
            _localReleasedAt = -1e9f; _pilots = 0; _aboardLast = -1;
            Riding = false;
        }

        public static bool HoldsVeil(bool buildDone, float sinceBuild)
        {
            try
            {
                if (!Enabled || !MapIdentity.HeliInfil || !buildDone || Riding) return false;
                if (sinceBuild > 20f) return false;
                if (_infilFlown && _mode != Mode.Infil) return false;
                return true;
            }
            catch { return false; }
        }

        public static void OnMapBuilt()
        {
            if (!Enabled || !MapIdentity.HeliInfil || _armed) return;
            try
            {
                if (!ReadPad()) { _log?.Warning("[heli] no pad: the marked spawn could not be read, no ride"); return; }
                BuildAirframe();
                _armed = _root != null;
                if (_armed)
                {
                    _log?.Msg("[heli] armed: deck at " + F(_pad) + " (" + _padSource + "); " + _seats.Count + " seat(s) from "
                            + _cabinSource + ", " + _pilots + " pilot(s), ropes " + (_ropes[0] != null && _ropes[1] != null ? "ready" : "MISSING") + ". The team flies in from "
                            + ApproachDist.ToString("0") + " m out and ropes down from " + RopeHoverAlt.ToString("0.0") + " m.");
                    if (_startSeen && !_infilFlown) Begin(Mode.Infil, "the mission started while the map was still building");
                }
            }
            catch (Exception e) { _log?.Warning("[heli] arm: " + e.Message); }
        }

        static bool ReadPad()
        {
            try
            {
                Vector3 v;
                if (!MapRuntime.TryMarkedSpawn(out v)) return false;
                bool found;
                var g = MapRuntime.GroundedSpawn(v, out found);
                float deck = found ? g.y - BodyRootAboveFloor : v.y;
                if (found && Mathf.Abs(deck - v.y) > 0.6f)
                {

                    _log?.Msg("[heli] deck: the ray says " + deck.ToString("0.00") + " but the marker is authored at " + v.y.ToString("0.00") + "; using the marker");
                    deck = v.y;
                }
                _pad = new Vector3(v.x, deck, v.z);
                _padSource = found ? "our floor under the marked spawn (ray " + (g.y - BodyRootAboveFloor).ToString("0.00") + ", marker " + v.y.ToString("0.00") + ")" : "the marker's height, nothing of ours found under it";
                return true;
            }
            catch (Exception e) { _log?.Warning("[heli] pad: " + e.Message); return false; }
        }

        static void BuildAirframe()
        {
            try
            {
                _root = new GameObject("ODG_BlackHawk");
                _root.transform.position = _pad + Vector3.up * 5000f;
                string dir = Path.Combine(Path.GetDirectoryName(MapIdentity.BundleDir.TrimEnd('\\')), "vehicles", "blackhawk");
                _texDir = Path.Combine(dir, "textures");
                if (!File.Exists(Path.Combine(dir, "manifest.bin")))
                    _log?.Warning("[heli] no Seahawk at " + dir + " -- the ride flies invisible. vehicles\\blackhawk must ship with the pack.");
                else BuildSeahawk(dir);
                BuildCabin();
                for (int i = 0; i < 2; i++)
                {
                    _ropeAnchors[i] = new GameObject("rope_anchor_" + i).transform; _ropeAnchors[i].SetParent(_root.transform, false); _ropeAnchors[i].localPosition = RopeAnchorLocal[i];
                    _ropeStows[i] = new GameObject("rope_stow_" + i).transform; _ropeStows[i].SetParent(_root.transform, false); _ropeStows[i].localPosition = RopeStowLocal[i];
                }
                BuildRopes();
                MakeSound(dir);
            }
            catch (Exception e) { _log?.Warning("[heli] build: " + e.Message); }
        }

        static void BuildRopes()
        {
            for (int i = 0; i < 2; i++)
            {
                try
                {
                    if (_ropes[i] != null) _ropes[i].Destroy();
                    _ropes[i] = new FastRope(_log);
                    _ropes[i].Build(_root.transform, _ropeAnchors[i], _ropeStows[i], i == 0 ? 1 : -1, _pad, _texDir);
                }
                catch (Exception e) { _log?.Warning("[heli] rope " + i + ": " + e.Message); _ropes[i] = null; }
                _ropeSlider[i] = null;
            }
            _ropesOut = false; _retracting = false;
        }

        static void BuildSeahawk(string dir)
        {
            var visual = new GameObject("Seahawk"); visual.transform.SetParent(_root.transform, false);
            var man = MapBundle.ReadManifest(dir);
            string texDir = Path.Combine(dir, "textures");
            int parts = 0;
            foreach (var md in man.Meshes)
            {
                try
                {
                    var g = MapBundle.ReadGeometry(dir, md.Id);
                    var mesh = MeshFactory.Build(g, "bh_" + md.Id);
                    var go = new GameObject(md.Id); go.transform.SetParent(visual.transform, false);
                    var inst = md.Instances.Count > 0 ? md.Instances[0] : null;
                    go.transform.localPosition = inst != null ? new Vector3(inst.px, inst.py, inst.pz) : Vector3.zero;
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    int subs = Mathf.Max(1, mesh.subMeshCount);
                    var mats = new Material[subs];
                    for (int s = 0; s < subs; s++)
                    {
                        var def = md.Materials.Count > 0 ? md.Materials[Mathf.Min(s, md.Materials.Count - 1)] : null;
                        mats[s] = def != null ? MeshFactory.BuildMaterial(def, texDir, false, md.Id) : new Material(MeshFactory.GetShader());
                    }
                    mr.sharedMaterials = mats;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    if (md.Id.EndsWith("main_rotor")) _mainRotor = go.transform;
                    else if (md.Id.EndsWith("tail_rotor")) _tailRotor = go.transform;
                    parts++;
                }
                catch (Exception pe) { _log?.Warning("[heli] part " + md.Id + ": " + pe.Message); }
            }
            _log?.Msg("[heli] Seahawk built: " + parts + " part(s), main rotor " + (_mainRotor != null ? "found" : "MISSING")
                    + ", tail rotor " + (_tailRotor != null ? "found" : "MISSING"));
        }

        static void BuildCabin()
        {
            _seats.Clear(); _ourChairs.Clear(); _pilots = 0; _heli = null;
            GameObject prefab = FindHeliPrefab();
            if (prefab != null)
            {
                try { BuildCabinFrom(prefab); }
                catch (Exception e) { _log?.Warning("[heli] cabin from '" + prefab.name + "': " + e.Message + " -- falling back to bare anchors"); _seats.Clear(); _ourChairs.Clear(); _heli = null; }
            }
            if (_seats.Count == 0)
            {
                _cabinSource = prefab == null ? "bare anchors (this host level ships no helicopter prefab, so no seats to borrow and no pilots)"
                                              : "bare anchors (the prefab's seats could not be used)";
                for (int i = 0; i < 6; i++)
                {
                    int side = (i % 2 == 0) ? 1 : -1; float row = 1.6f + 0.7f * (i / 2);
                    var a = new GameObject("seat_" + i); a.transform.SetParent(_root.transform, false);
                    a.transform.localPosition = new Vector3(0.8f * side, CabinCentre.y, row);
                    a.transform.localRotation = Quaternion.Euler(0f, side > 0 ? 90f : -90f, 0f);
                    _seats.Add(new Seat { Index = i, Anchor = a.transform, Side = side });
                }
            }
        }

        static Vector3 SeatFacing(Il2Cpp.InteractableChair c, int side)
        {
            Vector3 f = Vector3.zero;
            try
            {
                Transform[] cands = { c.rotationTrackingTarget, c.playerPosition, c.transform };
                for (int i = 0; i < cands.Length && f.sqrMagnitude < 1e-4f; i++)
                {
                    var t = cands[i]; if (t == null) continue;
                    Vector3 w = t.forward; if (Mathf.Abs(w.y) > 0.9f) w = t.up;
                    w = _root.transform.InverseTransformDirection(w); w.y = 0f;
                    if (w.sqrMagnitude > 0.05f) f = w.normalized;
                }
            }
            catch { }
            if (f.sqrMagnitude < 1e-4f) f = new Vector3(side, 0f, 0f);
            return f;
        }

        static GameObject FindHeliPrefab()
        {
            try { var im = Il2Cpp.InfiltrationManager.instance; if (im != null && im.InfiltrationVehicle != null) return im.InfiltrationVehicle; } catch { }
            try
            {
                var all = Resources.FindObjectsOfTypeAll<Il2Cpp.HelicopterV3>();
                for (int i = 0; i < (all != null ? all.Length : 0); i++)
                {
                    var h = all[i]; if (h == null) continue;
                    bool inScene = false; try { inScene = h.gameObject.scene.IsValid(); } catch { }
                    if (!inScene) return h.gameObject;
                }
            }
            catch { }
            return null;
        }

        static void BuildCabinFrom(GameObject prefab)
        {
            bool was = prefab.activeSelf;
            GameObject tmp = null;
            try { prefab.SetActive(false); tmp = UnityEngine.Object.Instantiate(prefab); }
            finally { try { prefab.SetActive(was); } catch { } }
            if (tmp == null) return;
            tmp.name = "Cabin";
            tmp.transform.SetParent(_root.transform, false);
            tmp.transform.localPosition = Vector3.zero; tmp.transform.localRotation = Quaternion.identity; tmp.transform.localScale = Vector3.one;

            int killed = 0, disabled = 0;
            try
            {
                var nbs = tmp.GetComponentsInChildren<Il2CppMirror.NetworkBehaviour>(true);
                for (int i = 0; i < (nbs != null ? nbs.Length : 0); i++)
                {
                    var nb = nbs[i]; if (nb == null) continue;
                    string tn = ""; try { tn = nb.GetIl2CppType().Name; } catch { }
                    if (tn == "InteractableChair") continue;
                    if (tn == "HelicopterV3") { try { nb.enabled = false; } catch { } disabled++; continue; }
                    if (tn == "SmoothSyncMirror" || tn == "InfilVehicleAnimation" || tn.Contains("NetworkTransform"))
                    { try { UnityEngine.Object.DestroyImmediate(nb); killed++; } catch { try { nb.enabled = false; disabled++; } catch { } } continue; }
                    try { nb.enabled = false; disabled++; } catch { }
                }
            }
            catch (Exception e) { _log?.Warning("[heli] cabin strip (network): " + e.Message); }
            try
            {
                var rbs = tmp.GetComponentsInChildren<Rigidbody>(true);
                for (int i = 0; i < (rbs != null ? rbs.Length : 0); i++) { try { rbs[i].isKinematic = true; rbs[i].detectCollisions = false; } catch { } }
                var cols = tmp.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < (cols != null ? cols.Length : 0); i++) { try { cols[i].enabled = false; } catch { } }
                var srcs = tmp.GetComponentsInChildren<AudioSource>(true);
                for (int i = 0; i < (srcs != null ? srcs.Length : 0); i++) { try { srcs[i].enabled = false; } catch { } }
                var ps = tmp.GetComponentsInChildren<ParticleSystem>(true);
                for (int i = 0; i < (ps != null ? ps.Length : 0); i++) { try { ps[i].gameObject.SetActive(false); } catch { } }
                var lights = tmp.GetComponentsInChildren<Light>(true);
                for (int i = 0; i < (lights != null ? lights.Length : 0); i++) { try { lights[i].enabled = false; } catch { } }
            }
            catch (Exception e) { _log?.Warning("[heli] cabin strip (physics): " + e.Message); }

            try
            {
                var mbs = tmp.GetComponentsInChildren<MonoBehaviour>(true);
                var names = new List<string>();
                for (int i = 0; i < (mbs != null ? mbs.Length : 0); i++)
                {
                    var mb = mbs[i]; if (mb == null) continue;
                    string tn = "", ns = ""; try { var t = mb.GetIl2CppType(); tn = t.Name; ns = t.Namespace ?? ""; } catch { }
                    if (tn == "InteractableChair" || tn == "FullBodyInteraction" || tn == "HelicopterV3" || ns.StartsWith("RootMotion") || ns.StartsWith("Mirror")) continue;
                    try { if (mb.enabled) { mb.enabled = false; disabled++; if (names.Count < 24 && !names.Contains(tn)) names.Add(tn); } } catch { }
                }
                if (names.Count > 0) _log?.Msg("[heli] cabin scripts switched off: " + string.Join(", ", names));
            }
            catch (Exception e) { _log?.Warning("[heli] cabin strip (scripts): " + e.Message); }

            int hidden = 0, kept = 0;
            try
            {
                var skins = tmp.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var boneRoots = new List<Transform>();
                for (int i = 0; i < (skins != null ? skins.Length : 0); i++)
                {
                    var s = skins[i]; if (s == null) continue;
                    Transform rb = null; try { rb = s.rootBone; } catch { }
                    if (rb != null) boneRoots.Add(rb);
                    _pilots++;
                }
                var mrs = tmp.GetComponentsInChildren<MeshRenderer>(true);
                for (int i = 0; i < (mrs != null ? mrs.Length : 0); i++)
                {
                    var r = mrs[i]; if (r == null) continue;
                    bool crew = false;
                    for (int b = 0; b < boneRoots.Count && !crew; b++) { try { crew = r.transform.IsChildOf(boneRoots[b]); } catch { } }
                    if (crew) { kept++; continue; }
                    try { r.enabled = false; hidden++; } catch { }
                }
                if (_pilots > 2) _pilots = 2;
            }
            catch (Exception e) { _log?.Warning("[heli] cabin hide: " + e.Message); }

            Il2Cpp.HelicopterV3 heli = null;
            try { heli = tmp.GetComponentInChildren<Il2Cpp.HelicopterV3>(true); } catch { }
            var chairs = new List<Il2Cpp.InteractableChair>();
            try
            {
                if (heli != null && heli.helicopterSeats != null)
                    for (int i = 0; i < heli.helicopterSeats.Count; i++)
                    { var hs = heli.helicopterSeats[i]; if (hs != null && hs.interactableChair != null) chairs.Add(hs.interactableChair); }
            }
            catch { }
            if (chairs.Count == 0)
            {
                try { var cs = tmp.GetComponentsInChildren<Il2Cpp.InteractableChair>(true); for (int i = 0; i < (cs != null ? cs.Length : 0); i++) if (cs[i] != null) chairs.Add(cs[i]); } catch { }
            }
            if (chairs.Count == 0) { _log?.Warning("[heli] the helicopter prefab '" + prefab.name + "' has no seats we can find"); UnityEngine.Object.Destroy(tmp); return; }

            Vector3 mean = Vector3.zero; int n = 0;
            for (int i = 0; i < chairs.Count; i++) { try { var pp = chairs[i].playerPosition; if (pp != null) { mean += tmp.transform.InverseTransformPoint(pp.position); n++; } } catch { } }
            if (n > 0) mean /= n;
            Vector3 crewMean = Vector3.zero; int cn = 0;
            try { var skins = tmp.GetComponentsInChildren<SkinnedMeshRenderer>(true); for (int i = 0; i < (skins != null ? skins.Length : 0); i++) { crewMean += tmp.transform.InverseTransformPoint(skins[i].transform.position); cn++; } } catch { }
            if (cn > 0) crewMean /= cn;
            bool flip = cn > 0 && n > 0 && crewMean.z < mean.z - 0.5f;
            if (flip) { tmp.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); mean = new Vector3(-mean.x, mean.y, -mean.z); crewMean = new Vector3(-crewMean.x, crewMean.y, -crewMean.z); }
            tmp.transform.localPosition = CabinCentre - mean;

            for (int i = 0; i < chairs.Count && i < 8; i++)
            {
                var c = chairs[i];
                try
                {
                    c.enabled = false;
                    try { c.gameObject.SetActive(true); } catch { }
                    var fbi = c.fullBodyInteraction;
                    if (fbi != null) { try { fbi.interactableChair = c; } catch { } }

                    try { if (c.helicopter == null && heli != null) c.helicopter = heli; } catch { }
                    var a = new GameObject("seat_" + i); a.transform.SetParent(_root.transform, false);
                    try { var pp = c.playerPosition; if (pp != null) { a.transform.position = pp.position; a.transform.rotation = pp.rotation; } else a.transform.localPosition = CabinCentre; } catch { a.transform.localPosition = CabinCentre; }
                    var seat = new Seat { Index = i, Anchor = a.transform, Chair = c, Fbi = fbi };
                    seat.Side = _root.transform.InverseTransformPoint(seat.SitPoint.position).x < 0f ? -1 : 1;
                    seat.FaceLocal = SeatFacing(c, seat.Side);
                    _seats.Add(seat);
                    _ourChairs.Add(c.Pointer);
                }
                catch (Exception e) { _log?.Warning("[heli] seat " + i + ": " + e.Message); }
            }
            _heli = heli;
            tmp.SetActive(true);
            try { if (heli != null) heli.ActivateSeats(); } catch { }
            for (int i = 0; i < _seats.Count; i++) { try { _seats[i].Chair.gameObject.SetActive(true); _seats[i].Chair.enabled = false; } catch { } }
            try { if (heli != null && heli.pilotCommsSource != null) { heli.pilotCommsSource.enabled = true; heli.pilotCommsSource.mute = false; } } catch { }

            _cabinSource = "the game's '" + prefab.name + "'";
            _log?.Msg("[heli] cabin from '" + prefab.name + "': " + _seats.Count + " seat(s), " + _pilots + " crew, " + hidden + " renderer(s) hidden, "
                    + kept + " crew renderer(s) kept, " + killed + " component(s) removed, " + disabled + " switched off" + (flip ? ", turned to face forward" : "")
                    + ". Seats sit at " + FL(mean) + " of the prefab, crew at " + FL(crewMean) + "; copy offset " + FL(tmp.transform.localPosition) + ".");
            for (int i = 0; i < _seats.Count; i++)
            {
                var s = _seats[i]; string extra = "";
                try
                {
                    string ikAt = "";
                    try { if (s.Fbi != null && s.Fbi.bodyIK != null) ikAt = " bodyIK at " + FL(_root.transform.InverseTransformPoint(s.Fbi.bodyIK.transform.position)); } catch { }
                    try { if (s.Chair.helicopter == null && heli != null) s.Chair.helicopter = heli; } catch { }
                    extra = " side=" + (s.Side > 0 ? "R" : "L") + " faces " + FL(s.FaceLocal) + ikAt + " sit='" + (s.Chair.SitAnimation ?? "") + "' stand='" + (s.Chair.StandAnimation ?? "") + "' heli=" + (s.Chair.helicopter != null);
                }
                catch { }
                _log?.Msg("[heli]   seat " + i + " at " + FL(_root.transform.InverseTransformPoint(s.SitPoint.position)) + extra);
            }
        }

        public static void Post_StartInfil()
        {
            try
            {
                if (!Enabled || !MapIdentity.HeliInfil) return;
                _startSeen = true;
                if (_armed && !_infilFlown) Begin(Mode.Infil, "the game's mission start");
                else _log?.Msg("[heli] the game's mission start arrived before the map was built; the ride begins when it is");
            }
            catch (Exception e) { _log?.Warning("[heli] start: " + e.Message); }
        }

        static void Begin(Mode mode, string why)
        {
            if (_root == null) return;
            _mode = mode;
            if (mode == Mode.Infil) _infilFlown = true; else _exfilStarted = true;
            if (mode == Mode.Infil && (_ropes[0] == null || _ropes[1] == null || _ropes[0].Mode != FastRope.State.Stowed || _ropes[1].Mode != FastRope.State.Stowed)) BuildRopes();
            Vector3 dir = ApproachDirection();
            Vector3 start = _pad + dir * ApproachDist + Vector3.up * ApproachAlt;
            _root.transform.position = start; _root.transform.rotation = LookAlong(-dir);
            SetLeg(Leg.Approach, start, _pad + Vector3.up * HoverAlt, LookAlong(-dir), LookAlong(-dir));
            _rotorSpin = 1f; _gainTarget = 1f; _zonesOnHeli = false; _forceLift = false; _aboardLast = -1; _liftAt = 0f; _ropesOut = false; _retracting = false;
            _queue.Clear(); _ropeSlider[0] = _ropeSlider[1] = null;
            _log?.Msg("[heli] " + (mode == Mode.Infil ? "RIDE IN" : "RIDE OUT") + " begins (" + why + "): from " + F(start)
                    + " to the pad at " + F(_pad) + ", " + (ApproachS + DescendS).ToString("0") + " s to the " + (mode == Mode.Infil ? "hover" : "pad"));
            if (mode == Mode.Infil)
            {
                SeatEveryone();
                try { if (Il2CppMirror.NetworkServer.active) SpawnShield.ReArmAll("the Black Hawk ride in", ApproachS + DescendS + 5f); } catch { }
            }
            Comms(Il2Cpp.HelicopterV3.CommsSet.OneMinute);
        }

        static Vector3 ApproachDirection() { return new Vector3(0.6f, 0f, 0.8f).normalized; }

        static List<KeyValuePair<uint, Il2Cpp.PlayerNetworking>> Players()
        {
            var outp = new List<KeyValuePair<uint, Il2Cpp.PlayerNetworking>>();
            try
            {
                var all = Il2Cpp.PlayerMaster.AllPlayerMasters;
                for (int i = 0; i < (all != null ? all.Count : 0); i++)
                {
                    var pm = all[i]; if (pm == null) continue;
                    var pn = pm.PlayerSpawnedObject; if (pn == null) continue;
                    bool dead = false; try { dead = pn.health != null && pn.health.isDead; } catch { }
                    if (dead) continue;
                    uint id = 0; try { id = pn.netId; } catch { }
                    outp.Add(new KeyValuePair<uint, Il2Cpp.PlayerNetworking>(id, pn));
                }
            }
            catch { }
            outp.Sort((a, b) => a.Key.CompareTo(b.Key));
            return outp;
        }

        static bool IsLocal(Il2Cpp.PlayerNetworking pn)
        {
            if (pn == null) return false;
            try { if (pn.isOwned) return true; } catch { }
            try { if (pn.isLocalPlayer) return true; } catch { }
            return Same(pn, LocalPlayer());
        }
        static Il2Cpp.PlayerNetworking LocalPlayer() { try { var pm = Il2Cpp.PlayerMaster.MyPlayerMaster; return pm != null ? pm.PlayerSpawnedObject : null; } catch { return null; } }
        static Transform RootOf(Il2Cpp.PlayerNetworking pn) { try { var mc = pn.masterController; return mc != null ? mc.transform : pn.transform; } catch { return null; } }
        static Rigidbody RbOf(Il2Cpp.PlayerNetworking pn) { try { var mc = pn.masterController; return mc != null ? mc.rigidBody : null; } catch { return null; } }
        static bool Same(Il2Cpp.PlayerNetworking a, Il2Cpp.PlayerNetworking b) { try { return a != null && b != null && a.Pointer == b.Pointer; } catch { return false; } }
        static Bolt BoltOf(Il2Cpp.PlayerNetworking pn) { for (int i = 0; i < _bolts.Count; i++) if (Same(_bolts[i].Pn, pn)) return _bolts[i]; return null; }
        static Bolt SliderOf(Il2Cpp.PlayerNetworking pn) { for (int i = 0; i < _sliders.Count; i++) if (Same(_sliders[i].Pn, pn)) return _sliders[i]; return null; }
        static Bolt BoltOfChair(Il2Cpp.InteractableChair c) { for (int i = 0; i < _bolts.Count; i++) { try { if (_bolts[i].Seat.Chair != null && c != null && _bolts[i].Seat.Chair.Pointer == c.Pointer) return _bolts[i]; } catch { } } return null; }
        static bool SeatFree(Seat s) { for (int i = 0; i < _bolts.Count; i++) if (_bolts[i].Seat == s) return false; return true; }
        static Seat FreeSeat() { for (int i = 0; i < _seats.Count; i++) if (SeatFree(_seats[i])) return _seats[i]; return null; }
        static bool IsOurs(Il2Cpp.InteractableChair c) { try { return c != null && _ourChairs.Contains(c.Pointer); } catch { return false; } }
        static void Recount()
        {
            bool local = false;
            for (int i = 0; i < _bolts.Count; i++) if (_bolts[i].Local) local = true;
            for (int i = 0; i < _sliders.Count; i++) if (_sliders[i].Local) local = true;
            if (local && !Riding) _ridingSince = Time.realtimeSinceStartup;
            Riding = local;
        }

        static void SeatEveryone()
        {
            var ps = Players();
            int n = 0;
            for (int i = 0; i < ps.Count && n < _seats.Count; i++) { if (Board(ps[i].Value, ps[i].Key, _seats[n], "ride in")) n++; }
            _log?.Msg("[heli] " + n + " player(s) seated for the ride in (" + ps.Count + " alive)");
        }

        static void SeatLatecomers()
        {
            var ps = Players();
            for (int i = 0; i < ps.Count; i++)
            {
                if (BoltOf(ps[i].Value) != null || SliderOf(ps[i].Value) != null) continue;
                var s = FreeSeat(); if (s == null) return;
                Board(ps[i].Value, ps[i].Key, s, "late to the ride in");
            }
        }

        static bool Board(Il2Cpp.PlayerNetworking pn, uint netId, Seat seat, string why)
        {
            if (pn == null || seat == null || !SeatFree(seat) || BoltOf(pn) != null || SliderOf(pn) != null) return false;
            var root = RootOf(pn); if (root == null) return false;
            var b = new Bolt { Seat = seat, Pn = pn, Root = root, Rb = RbOf(pn), NetId = netId, Local = IsLocal(pn), SitAskedAt = Time.realtimeSinceStartup };
            if (!b.Local)
            {

                try
                {
                    var ss = root.root.GetComponentsInChildren<Il2CppSmooth.SmoothSyncMirror>(true);
                    for (int i = 0; i < (ss != null ? ss.Length : 0); i++) if (ss[i] != null && ss[i].enabled) { ss[i].enabled = false; b.Syncs.Add(ss[i]); }
                }
                catch { }
            }
            _bolts.Add(b);
            Recount();
            _log?.Msg("[heli] seat " + seat.Index + ": " + (b.Local ? "you" : "player " + netId) + " (" + why + ")");
            if (seat.Chair == null) { Attach(b); return true; }
            if (b.Local)
            {

                try
                {
                    if (seat.Fbi != null) { LogSyncShape(pn); pn.SitInHelicopter(seat.Fbi); }
                    else SitNow(b);
                }
                catch (Exception e) { _log?.Warning("[heli] sit (game path): " + e.Message); SitNow(b); }
            }
            else SitNow(b);
            return true;
        }

        static void SitNow(Bolt b)
        {
            if (b == null || b.Seated) return;
            var c = b.Seat.Chair; if (c == null) { b.Seated = true; return; }
            try
            {
                var cr = b.Pn.CameraRoot;
                if (cr != null) { b.CamParent = cr.parent; b.CamPos = cr.localPosition; b.CamRot = cr.localRotation; b.CamSaved = true; }
            }
            catch { }
            try { c.UserCode_RPC_ChairStartInteraction__PlayerNetworking(b.Pn); }
            catch (Exception e) { _log?.Warning("[heli] seat " + b.Seat.Index + " StartInteraction: " + e.Message); }
            try { c.inUse = true; } catch { }
            b.Seated = true;
            Attach(b);
            bool ok = false; try { ok = b.Pn.chair != null && b.Pn.chair.Pointer == c.Pointer; } catch { }
            _log?.Msg("[heli] seat " + b.Seat.Index + ": " + (b.Local ? "you are" : "player " + b.NetId + " is") + " sitting" + (ok ? "" : " (the game did not record the chair)"));
        }

        static void Attach(Bolt b)
        {
            try
            {
                if (b.Root == null) return;
                var sp = b.Seat.SitPoint; if (sp == null) return;
                if (!b.Parented) b.RootParent = b.Root.parent;
                b.Root.SetParent(sp, true);
                b.Root.localPosition = Vector3.zero;
                b.Parented = true;
            }
            catch (Exception e) { _log?.Warning("[heli] attach: " + e.Message); }
        }

        static void Detach(Bolt b)
        {
            try
            {
                if (!b.Parented || b.Root == null) return;
                b.Root.SetParent(b.RootParent, true);
                b.Parented = false;
            }
            catch (Exception e) { _log?.Warning("[heli] detach: " + e.Message); }
        }

        static void LogSyncShape(Il2Cpp.PlayerNetworking pn)
        {
            try
            {
                var ss = pn.GetComponentInChildren<Il2CppSmooth.SmoothSyncMirror>(true);
                if (ss == null) { _log?.Msg("[heli] your body carries no SmoothSync"); return; }
                _log?.Msg("[heli] your SmoothSync: local=" + ss.useLocalTransformOnly + " when=" + ss.whenToUpdateTransform + " target='" + (ss.realObjectToSync != null ? ss.realObjectToSync.name : "self") + "'");
            }
            catch { }
        }

        static void AskStand(Bolt b, string why)
        {
            if (b == null || b.Standing) return;
            b.Standing = true; b.StandAskedAt = Time.realtimeSinceStartup;
            var c = b.Seat.Chair;
            if (c == null) { Unbolt(b, Exit.Auto, why); return; }
            _log?.Msg("[heli] seat " + b.Seat.Index + ": " + (b.Local ? "you" : "player " + b.NetId) + " standing up (" + why + ")");
            try { c.HeliStartStanding(); }
            catch (Exception e) { _log?.Warning("[heli] stand animation: " + e.Message + " -- standing directly"); StandNow(c, why); }
        }

        static void StandNow(Il2Cpp.InteractableChair c, string why)
        {
            var b = BoltOfChair(c);
            try { c.UserCode_RPC_Stand__PlayerNetworking(b != null ? b.Pn : c.playerNetworking); }
            catch (Exception e) { _log?.Warning("[heli] stand restore: " + e.Message); }
            try
            {
                var h = c.helicopter;
                if (h != null)
                {
                    try { var seats = h.helicopterSeats; if (seats != null && c.SeatIndex >= 0 && c.SeatIndex < seats.Count && seats[c.SeatIndex] != null) seats[c.SeatIndex].isSeated = false; } catch { }
                    try { h.RestoreEngineAudioParent(); } catch { }
                }
            }
            catch { }
            try { c.inUse = false; } catch { }
            try { c.standingUp = false; } catch { }
            if (b != null) Unbolt(b, Exit.Auto, why);
        }

        static void RestoreCamera(Bolt b)
        {
            try
            {
                var cr = b.Pn != null ? b.Pn.CameraRoot : null;
                if (cr != null && b.CamSaved && b.CamParent != null && cr.parent != b.CamParent && _root != null && cr.IsChildOf(_root.transform))
                { cr.SetParent(b.CamParent, false); cr.localPosition = b.CamPos; cr.localRotation = b.CamRot; }
            }
            catch { }
        }

        static void Unbolt(Bolt b, Exit how, string why)
        {
            _bolts.Remove(b);
            if (how == Exit.Auto)
            {
                if (_mode == Mode.Infil && _leg == Leg.Hover && _ropes[0] != null) how = Exit.Rope;
                else if (_leg == Leg.Landed || _leg == Leg.Idle || _leg == Leg.Done) how = Exit.Pad;
                else how = Exit.Free;
            }
            RestoreCamera(b);
            Detach(b);
            if (how == Exit.Rope) { StartSlide(b, why); return; }
            for (int k = 0; k < b.Syncs.Count; k++) { try { if (b.Syncs[k] != null) b.Syncs[k].enabled = true; } catch { } }
            try { if (b.Rb != null && b.InterpSaved) b.Rb.interpolation = b.Interp; } catch { }
            if (how == Exit.Pad) PlaceBesideDoor(b);
            if (b.Local) _localReleasedAt = Time.realtimeSinceStartup;
            Recount();
            _log?.Msg("[heli] seat " + b.Seat.Index + ": " + (b.Local ? "you are" : "player " + b.NetId + " is") + " off (" + why + "), " + _bolts.Count + " still seated");
            try { if (Il2CppMirror.NetworkServer.active) SpawnShield.ReArmAll("a player stepped off the Black Hawk", 6f); } catch { }
        }

        static void PlaceBesideDoor(Bolt b)
        {
            try
            {
                var sp = b.Seat.SitPoint;
                Vector3 outward = Vector3.right;
                if (_root != null) { float sx = _root.transform.InverseTransformPoint(sp.position).x; outward = _root.transform.right * (sx < 0f ? -1f : 1f); }
                outward.y = 0f; outward.Normalize();
                Vector3 p = sp.position + outward * 1.7f;
                p.y = _pad.y + 0.05f;
                SetDown(b, p, Quaternion.LookRotation(outward, Vector3.up));
            }
            catch (Exception e) { _log?.Warning("[heli] place: " + e.Message); }
        }

        static void SetDown(Bolt b, Vector3 p, Quaternion rot)
        {
            if (b.Root == null) return;
            try { var mc = b.Pn.masterController; if (mc != null) { if (mc.rigidBody != null) mc.rigidBody.isKinematic = false; if (mc.characterCollider != null) mc.characterCollider.isTrigger = false; } } catch { }
            if (b.Local)
            {
                try { if (b.Rb != null) { b.Rb.linearVelocity = Vector3.zero; b.Rb.position = p; } } catch { }
                b.Root.position = p; b.Root.rotation = rot;
                try { var mc = b.Pn.masterController; if (mc != null) { if (mc.walkerController != null) mc.walkerController.SetMomentum(Vector3.zero); if (mc.mover != null) mc.mover.SetVelocity(Vector3.zero); } } catch { }
                try { var ss = b.Pn.GetComponentInChildren<Il2CppSmooth.SmoothSyncMirror>(true); if (ss != null) ss.teleportOwnedObjectFromOwner(); } catch { }
                try { var fc = b.Pn.GetComponentInChildren<Il2Cpp.Character_FollowCamera>(true); if (fc != null) fc.SnapToTarget(); } catch { }
                try { MapRuntime.SkipMarkedSpawn("the Black Hawk put the player on the deck at " + F(p)); } catch { }
            }
            else { b.Root.position = p; try { if (b.Rb != null) b.Rb.position = p; } catch { } }
        }

        static void ReleaseAll(string why, Exit how)
        {
            for (int i = _bolts.Count - 1; i >= 0; i--)
            {
                var b = _bolts[i];
                try
                {
                    if (b.Seat.Chair != null && b.Seated && how != Exit.Free) StandNow(b.Seat.Chair, why);
                    else Unbolt(b, how, why);
                }
                catch { try { Unbolt(b, how, why); } catch { } }
            }
            _bolts.Clear();
            for (int i = _sliders.Count - 1; i >= 0; i--) { try { EndSlide(_sliders[i], how == Exit.Free ? "released" : why, how != Exit.Free); } catch { } }
            _sliders.Clear();
            Recount();
        }

        static void EvictStrangers()
        {
            if (_root == null) return;
            var ps = Players();
            for (int i = 0; i < ps.Count; i++)
            {
                var pn = ps[i].Value;
                try { var cr = pn.CameraRoot; var rt = RootOf(pn); if (cr != null && rt != null && cr.IsChildOf(_root.transform)) cr.SetParent(rt, true); } catch { }
                try { var rt = RootOf(pn); if (rt != null && rt.IsChildOf(_root.transform)) rt.SetParent(null, true); } catch { }
            }
        }

        static void Hold()
        {
            float now = Time.realtimeSinceStartup;
            for (int i = _bolts.Count - 1; i >= 0; i--)
            {
                var b = _bolts[i];
                if (b.Root == null || b.Pn == null) { _bolts.RemoveAt(i); Recount(); continue; }
                try
                {
                    bool dead = false; try { dead = b.Pn.health != null && b.Pn.health.isDead; } catch { }
                    if (dead) { Unbolt(b, Exit.Free, "died"); continue; }
                    var sp = b.Seat.SitPoint;
                    if (b.Parented && b.Root.parent != sp)
                    {

                        if (!b.ReparentNoted) { b.ReparentNoted = true; _log?.Msg("[heli] seat " + b.Seat.Index + ": the root's parent became '" + (b.Root.parent != null ? b.Root.parent.name : "none") + "', re-attaching"); }
                        b.RootParent = b.Root.parent != null && !b.Root.parent.IsChildOf(_root.transform) ? b.Root.parent : b.RootParent;
                        b.Root.SetParent(sp, true);
                    }
                    var q = b.Seat.Facing(_root.transform);
                    KillSeatBlend(b, sp.position);
                    Pin(b, sp.position, q);
                    if (b.Local && now >= b.DiagAt) { b.DiagAt = now + 3f; SeatDiag(b, sp); }
                }
                catch { }
            }
            for (int i = _sliders.Count - 1; i >= 0; i--)
            {
                var b = _sliders[i];
                if (b.Root == null || b.Pn == null) { _sliders.RemoveAt(i); Recount(); continue; }
                if (b.SlidePlaced) Pin(b, b.SlidePos, b.SlideRot);
            }
        }

        static void KillSeatBlend(Bolt b, Vector3 seatPos)
        {
            try
            {
                var mc = b.Pn.masterController; if (mc == null) return;
                mc._seatBlendStartPos = seatPos;
                mc._seatBlendTimer = 10f;
                mc._seatBlendDuration = 0f;
            }
            catch { }
        }

        static void Pin(Bolt b, Vector3 p, Quaternion q)
        {
            if (b.Rb != null)
            {
                try
                {
                    if (!b.InterpSaved) { b.Interp = b.Rb.interpolation; b.InterpSaved = true; }
                    if (b.Rb.interpolation != RigidbodyInterpolation.None) b.Rb.interpolation = RigidbodyInterpolation.None;
                    if (!b.Rb.isKinematic) b.Rb.isKinematic = true;
                    b.Rb.position = p; b.Rb.rotation = q;
                }
                catch { }
            }
            if (b.Parented && b.Root.parent != null) b.Root.localPosition = Vector3.zero; else b.Root.position = p;
            b.Root.rotation = q;
            if (b.Local)
            {

                try { var mc = b.Pn.masterController; if (mc != null) { if (mc.walkerController != null) mc.walkerController.SetMomentum(Vector3.zero); if (mc.mover != null) mc.mover.SetVelocity(Vector3.zero); } } catch { }
            }
        }

        static void SeatDiag(Bolt b, Transform sp)
        {
            try
            {
                Vector3 d = b.Root.position - sp.position;
                string pelvis = "";
                try { var sk = b.Pn.GetComponentInChildren<SkinnedMeshRenderer>(true); if (sk != null && sk.rootBone != null) pelvis = " pelvis " + FL(sk.rootBone.position - sp.position); } catch { }
                bool blend = false; try { blend = b.Pn.masterController != null && b.Pn.masterController.IsSeatBlending; } catch { }
                bool kin = false; try { kin = b.Rb != null && b.Rb.isKinematic; } catch { }
                string cam = ""; try { var cr = b.Pn.CameraRoot; cam = cr != null && cr.parent != null ? cr.parent.name : "none"; } catch { }
                bool chair = false; try { chair = b.Pn.chair != null; } catch { }
                _log?.Msg("[heli] seat " + b.Seat.Index + " hold: root off by " + FL(d) + pelvis + " euler " + FL(b.Root.rotation.eulerAngles) + " parent='" + (b.Root.parent != null ? b.Root.parent.name : "none")
                        + "' kinematic=" + kin + " blending=" + blend + " chair=" + chair + " cameraRootUnder='" + cam + "'");
            }
            catch { }
        }

        public static void LateTick() { try { Hold(); } catch { } }

        static void StartSlide(Bolt b, string why)
        {
            b.SlideStart = -1f; b.SlidePlaced = false; b.HandsOn = false;
            try { b.HoldPos = b.Root.position; } catch { b.HoldPos = b.Seat.SitPoint.position; }
            try { var mc = b.Pn.masterController; if (mc != null) { if (mc.rigidBody != null) mc.rigidBody.isKinematic = true; if (mc.characterCollider != null) mc.characterCollider.isTrigger = true; } } catch { }
            try { b.Ik = b.Pn.GetComponentInChildren<Il2CppRootMotion.FinalIK.FullBodyBipedIK>(true); } catch { b.Ik = null; }
            if (b.RopeIndex < 0) b.RopeIndex = b.Seat.Side < 0 ? 1 : 0;
            _sliders.Add(b);
            Recount();
            _log?.Msg("[heli] " + (b.Local ? "you are" : "player " + b.NetId + " is") + " up for the " + (b.RopeIndex == 0 ? "right" : "left") + " rope (" + why + ")");
        }

        static void RopeTick(float now)
        {
            if (!_ropesOut)
            {
                if (now - _hoverAt < RopeOutS) return;
                for (int i = 0; i < 2; i++) { try { if (_ropes[i] != null) _ropes[i].Deploy(); } catch { } }
                _ropesOut = true;
                if (_queue.Count == 0)
                {
                    for (int i = 0; i < _bolts.Count; i++) _queue.Add(_bolts[i]);
                    _queue.Sort((a, c) => a.Seat.Index.CompareTo(c.Seat.Index));
                    for (int i = 0; i < _queue.Count; i++) _queue[i].RopeIndex = _queue[i].Seat.Side < 0 ? 1 : 0;
                    int r0 = 0, r1 = 0; for (int i = 0; i < _queue.Count; i++) if (_queue[i].RopeIndex == 0) r0++; else r1++;
                    _log?.Msg("[heli] ropes out: " + r0 + " on the right rope, " + r1 + " on the left, one at a time");
                }
                return;
            }
            for (int r = 0; r < 2; r++)
            {
                var rope = _ropes[r]; if (rope == null || !rope.Deployed) continue;
                var cur = _ropeSlider[r];
                if (cur != null && !_sliders.Contains(cur)) { _ropeSlider[r] = null; cur = null; }
                if (cur != null) continue;
                if (now - rope.DeployedAt < SlideReadyS) continue;
                Bolt next = NextFor(r, null);
                if (next == null) continue;
                if (_sliders.Contains(next))
                {
                    if (next.SlideStart < 0f)
                    {
                        next.SlideStart = now; _ropeSlider[r] = next;
                        _log?.Msg("[heli] " + (next.Local ? "you are" : "player " + next.NetId + " is") + " on the " + (r == 0 ? "right" : "left") + " rope");
                        var after = NextFor(r, next);
                        if (after != null && _bolts.Contains(after) && !after.Standing) AskStand(after, "next on the " + (r == 0 ? "right" : "left") + " rope");
                    }
                }
                else if (_bolts.Contains(next) && !next.Standing) AskStand(next, (r == 0 ? "right" : "left") + " rope");
            }
        }

        static Bolt NextFor(int rope, Bolt after)
        {
            bool passed = after == null;
            for (int i = 0; i < _queue.Count; i++)
            {
                var b = _queue[i];
                if (!passed) { if (b == after) passed = true; continue; }
                if (b.RopeIndex != rope) continue;
                bool seated = _bolts.Contains(b), sliding = _sliders.Contains(b);
                if (!seated && !sliding) continue;
                if (sliding && b.SlideStart >= 0f) continue;
                return b;
            }
            return null;
        }

        static void SlideTick(float now)
        {
            for (int i = _sliders.Count - 1; i >= 0; i--)
            {
                var b = _sliders[i];
                try
                {
                    bool dead = false; try { dead = b.Pn.health != null && b.Pn.health.isDead; } catch { }
                    if (dead) { EndSlide(b, "died", false); continue; }
                    var rope = _ropes[Mathf.Clamp(b.RopeIndex, 0, 1)];
                    if (rope == null) { EndSlide(b, "no rope", true); continue; }
                    if (b.SlideStart < 0f)
                    {

                        b.SlidePos = b.Seat.SitPoint.position; b.SlideRot = Quaternion.Euler(0f, b.Root.rotation.eulerAngles.y, 0f); b.SlidePlaced = true;
                        Pin(b, b.SlidePos, b.SlideRot); continue;
                    }
                    Vector3 top = rope.Top;
                    Vector3 o = top - _root.transform.position; o.y = 0f; o = o.sqrMagnitude > 0.01f ? o.normalized : _root.transform.right * (b.RopeIndex == 0 ? 1f : -1f);
                    float t = now - b.SlideStart;
                    if (t < DoorS)
                    {

                        Vector3 pos0, d0; rope.PointAt(0.9f, out pos0, out d0);
                        Vector3 goal = pos0 + o * 0.30f;
                        float u = t / DoorS;
                        b.SlidePos = Vector3.Lerp(b.Seat.SitPoint.position, goal, u * u * (3f - 2f * u)); b.SlideRot = Quaternion.LookRotation(-o, Vector3.up); b.SlidePlaced = true;
                        Pin(b, b.SlidePos, b.SlideRot); continue;
                    }
                    float s = 0.9f + SlideSpeed * (t - DoorS);
                    float hang = rope.HangLength;
                    if (s >= hang - 0.15f) { EndSlide(b, "on the deck", true); continue; }
                    Vector3 pos, down; rope.PointAt(s, out pos, out down);
                    Vector3 ro = pos - _root.transform.position; ro.y = 0f; ro = ro.sqrMagnitude > 0.01f ? ro.normalized : o;
                    b.SlidePos = pos + ro * 0.30f; b.SlideRot = Quaternion.LookRotation(-ro, Vector3.up); b.SlidePlaced = true;
                    Pin(b, b.SlidePos, b.SlideRot);
                    Hands(b, rope, s, ro, true);
                }
                catch (Exception e) { _log?.Warning("[heli] slide: " + e.Message); }
            }
        }

        static void Hands(Bolt b, FastRope rope, float s, Vector3 ro, bool on)
        {
            if (b.Ik == null) return;
            try
            {
                var solver = b.Ik.solver; if (solver == null) return;
                var l = solver.leftHandEffector; var r = solver.rightHandEffector;
                if (on && rope != null)
                {
                    Vector3 lp, ld, rp, rd; rope.PointAt(Mathf.Max(0.05f, s - 1.95f), out lp, out ld); rope.PointAt(Mathf.Max(0.05f, s - 1.65f), out rp, out rd);
                    Vector3 side = Vector3.Cross(Vector3.up, ro).normalized * 0.05f;
                    if (l != null) { l.position = lp + ro * 0.02f - side; l.positionWeight = 1f; }
                    if (r != null) { r.position = rp + ro * 0.02f + side; r.positionWeight = 1f; }
                    b.HandsOn = true;
                }
                else
                {
                    if (l != null) l.positionWeight = 0f;
                    if (r != null) r.positionWeight = 0f;
                    b.HandsOn = false;
                }
            }
            catch { }
        }

        static void EndSlide(Bolt b, string why, bool onDeck)
        {
            _sliders.Remove(b);
            for (int r = 0; r < 2; r++) if (_ropeSlider[r] == b) _ropeSlider[r] = null;
            try { Hands(b, null, 0f, Vector3.right, false); } catch { }
            for (int k = 0; k < b.Syncs.Count; k++) { try { if (b.Syncs[k] != null) b.Syncs[k].enabled = true; } catch { } }
            try { if (b.Rb != null && b.InterpSaved) b.Rb.interpolation = b.Interp; } catch { }
            var rope = _ropes[Mathf.Clamp(b.RopeIndex, 0, 1)];
            if (onDeck && rope != null)
            {
                Vector3 bottom = rope.Bottom;
                Vector3 o = bottom - (_root != null ? _root.transform.position : bottom); o.y = 0f; o = o.sqrMagnitude > 0.01f ? o.normalized : Vector3.right;
                Vector3 p = bottom + o * 0.9f; p.y = _pad.y + 0.05f;
                SetDown(b, p, Quaternion.LookRotation(o, Vector3.up));
            }
            else
            {
                try { var mc = b.Pn.masterController; if (mc != null) { if (mc.rigidBody != null) mc.rigidBody.isKinematic = false; if (mc.characterCollider != null) mc.characterCollider.isTrigger = false; } } catch { }
            }
            if (b.Local) _localReleasedAt = Time.realtimeSinceStartup;
            Recount();
            _log?.Msg("[heli] " + (b.Local ? "you are" : "player " + b.NetId + " is") + " off the rope (" + why + "), " + _bolts.Count + " seated, " + _sliders.Count + " on the ropes");
            try { if (Il2CppMirror.NetworkServer.active) SpawnShield.ReArmAll("a player came down the rope", 6f); } catch { }
        }

        public static bool GuardsFall(out string why)
        {
            why = null;
            try
            {
                for (int i = 0; i < _bolts.Count; i++) if (_bolts[i].Local) { why = "seated in the Black Hawk"; return true; }
                for (int i = 0; i < _sliders.Count; i++) if (_sliders[i].Local) { why = "on the fast rope"; return true; }
                float since = Time.realtimeSinceStartup - _localReleasedAt;
                if (since < 6f) { why = since.ToString("0.0") + " s after leaving the Black Hawk"; return true; }
            }
            catch { }
            return false;
        }

        static bool Pre_FallStanding(Il2Cpp.MasterController __instance) { string w; return !(IsLocalController(__instance) && GuardsFall(out w)); }
        static bool Pre_FallProne(Il2Cpp.MasterController __instance) { string w; return !(IsLocalController(__instance) && GuardsFall(out w)); }
        static bool IsLocalController(Il2Cpp.MasterController mc) { try { return mc != null && IsLocal(mc.playerNetworking); } catch { return false; } }

        static bool Pre_CmdStartInteraction(Il2Cpp.InteractableChair __instance, Il2Cpp.PlayerNetworking __0)
        {
            try
            {
                if (!IsOurs(__instance)) return true;
                var b = BoltOfChair(__instance);
                if (b == null && __0 != null)
                {

                    Seat s = null; for (int i = 0; i < _seats.Count; i++) { try { if (_seats[i].Chair != null && _seats[i].Chair.Pointer == __instance.Pointer) s = _seats[i]; } catch { } }
                    uint id = 0; try { id = __0.netId; } catch { }
                    if (s != null && SeatFree(s) && BoltOf(__0) == null)
                    {
                        var root = RootOf(__0);
                        if (root != null) { b = new Bolt { Seat = s, Pn = __0, Root = root, Rb = RbOf(__0), NetId = id, Local = IsLocal(__0), SitAskedAt = Time.realtimeSinceStartup }; _bolts.Add(b); Recount(); }
                    }
                }
                if (b != null) SitNow(b);
                return false;
            }
            catch (Exception e) { _log?.Warning("[heli] cmd start: " + e.Message); return false; }
        }

        static bool Pre_CmdStand(Il2Cpp.InteractableChair __instance) { return !IsOurs(__instance); }
        static bool Pre_CmdStartStanding(Il2Cpp.InteractableChair __instance) { return !IsOurs(__instance); }

        static bool Pre_Stand(Il2Cpp.InteractableChair __instance)
        {
            try
            {
                if (!IsOurs(__instance)) return true;
                StandNow(__instance, "stood up");
                return false;
            }
            catch (Exception e) { _log?.Warning("[heli] stand: " + e.Message); return false; }
        }

        static void SetLeg(Leg leg, Vector3 from, Vector3 to, Quaternion fromRot, Quaternion toRot)
        { _leg = leg; _legStart = Time.realtimeSinceStartup; _from = from; _to = to; _fromRot = fromRot; _toRot = toRot; }

        static float LegDuration()
        {
            switch (_leg)
            {
                case Leg.Approach: return ApproachS;
                case Leg.Descend:  return DescendS;
                case Leg.Climb:    return ClimbS;
                case Leg.Depart:   return DepartS;
            }
            return float.PositiveInfinity;
        }

        static Quaternion LookAlong(Vector3 d) { d.y = 0f; if (d.sqrMagnitude < 1e-4f) d = Vector3.forward; return Quaternion.LookRotation(d.normalized, Vector3.up); }
        static float Ease(float u) { u = Mathf.Clamp01(u); return u * u * (3f - 2f * u); }

        public static void Tick()
        {
            if (!Enabled || !_armed || _root == null) return;
            float now = Time.realtimeSinceStartup;
            try
            {
                if (_infilFlown && !_exfilStarted && now >= _nextPoll)
                {
                    _nextPoll = now + 1f;
                    if (ExtractionOpen())
                    {
                        try { MapExfil.ParkAll(_pad + Vector3.up * 400f); } catch { }
                        Begin(Mode.Exfil, "the game opened extraction");
                        _log?.Msg("[heli] extraction zones parked in the sky until everyone is aboard");
                    }
                }
                if (_mode == Mode.None) { RopesTick(); Audio(); return; }

                SeatingTick(now);

                float dur = LegDuration();
                float u = float.IsInfinity(dur) ? 0f : Mathf.Clamp01((now - _legStart) / dur);
                var t = _root.transform; Vector3 pos; Quaternion rot;
                switch (_leg)
                {
                    case Leg.Approach:
                    {
                        float e = Ease(u); pos = Vector3.Lerp(_from, _to, e);
                        rot = Quaternion.Slerp(_fromRot, _toRot, e) * Quaternion.Euler(Mathf.Lerp(-10f, 5f, e), 0f, 0f);
                        if (u >= 1f)
                        {
                            SetLeg(Leg.Descend, pos, _pad + Vector3.up * (_mode == Mode.Infil ? RopeHoverAlt : GearClear), _toRot, _toRot);
                            Comms(Il2Cpp.HelicopterV3.CommsSet.S30Seconds);
                        }
                        break;
                    }
                    case Leg.Descend:
                    {
                        float e = Ease(u); pos = Vector3.Lerp(_from, _to, e);
                        rot = _toRot * Quaternion.Euler(Mathf.Lerp(5f, 0f, e), 0f, Mathf.Sin(now * 1.3f) * 1.2f * (1f - e));
                        if (u >= 1f)
                        {
                            if (_mode == Mode.Infil) { SetLeg(Leg.Hover, _to, _to, _toRot, _toRot); HoverArrived(now); }
                            else { SetLeg(Leg.Landed, _to, _to, _toRot, _toRot); Touchdown(now); }
                        }
                        break;
                    }
                    case Leg.Hover:
                    {

                        pos = _to + Vector3.up * (0.18f * Mathf.Sin(now * 2.1f)) + _toRot * new Vector3(0.10f * Mathf.Sin(now * 0.7f), 0f, 0.06f * Mathf.Sin(now * 0.5f));
                        rot = _toRot * Quaternion.Euler(1.0f * Mathf.Sin(now * 0.9f), 2.0f * Mathf.Sin(now * 0.45f), 0.8f * Mathf.Sin(now * 1.3f));
                        if (ReadyToLift(now)) { LiftOff(); SetLeg(Leg.Climb, pos, _pad + Vector3.up * (HoverAlt + 14f), rot, _toRot); }
                        break;
                    }
                    case Leg.Landed:
                    {
                        pos = _to; rot = _toRot;
                        if (ReadyToLift(now)) { LiftOff(); SetLeg(Leg.Climb, pos, _pad + Vector3.up * (HoverAlt + 14f), rot, rot); }
                        break;
                    }
                    case Leg.Climb:
                    {
                        float e = Ease(u); pos = Vector3.Lerp(_from, _to, e);
                        rot = Quaternion.Slerp(_fromRot, _toRot, e) * Quaternion.Euler(Mathf.Lerp(0f, -8f, e), 0f, 0f);
                        if (_mode == Mode.Exfil && !_zonesOnHeli && now - _legStart > 2f) HandOverToTheGame();
                        if (u >= 1f) { Vector3 d = -ApproachDirection(); SetLeg(Leg.Depart, pos, _pad + d * ApproachDist + Vector3.up * ApproachAlt, rot, LookAlong(d)); }
                        break;
                    }
                    case Leg.Depart:
                    {
                        float e = Ease(u); pos = Vector3.Lerp(_from, _to, e);
                        rot = Quaternion.Slerp(_fromRot, _toRot, Mathf.Clamp01(u * 2.5f)) * Quaternion.Euler(-9f * (1f - e), 0f, 0f);
                        if (u >= 1f) { Finish(); return; }
                        break;
                    }
                    case Leg.Done:
                    {
                        pos = t.position; rot = t.rotation;
                        break;
                    }
                    default: return;
                }
                t.position = pos; t.rotation = rot;
                RopesTick();
                if (_leg == Leg.Hover && _mode == Mode.Infil) RopeTick(now);
                SlideTick(now);
                Hold();
                Rotors(); Audio();

                if (now >= _nextStatus)
                {
                    _nextStatus = now + 10f;
                    _log?.Msg("[heli] " + _mode + " " + _leg + " " + (u * 100f).ToString("0") + "% at " + F(pos) + " seated=" + _bolts.Count + " onRopes=" + _sliders.Count
                            + (_leg == Leg.Landed && _mode == Mode.Exfil ? " aboard=" + _aboardLast : "")
                            + " ropes=" + (_ropes[0] != null ? _ropes[0].Mode.ToString() : "none") + "/" + (_ropes[1] != null ? _ropes[1].Mode.ToString() : "none"));
                }
            }
            catch (Exception e) { _log?.Warning("[heli] tick: " + e.Message); }
        }

        static void RopesTick() { for (int i = 0; i < 2; i++) { try { if (_ropes[i] != null) _ropes[i].Tick(Time.deltaTime); } catch { } } }

        static void HoverArrived(float now)
        {
            _hoverAt = now; _forceLift = false; _ropesOut = false; _retracting = false; _queue.Clear(); _ropeSlider[0] = _ropeSlider[1] = null;
            Comms(Il2Cpp.HelicopterV3.CommsSet.Land);
            _log?.Msg("[heli] holding " + RopeHoverAlt.ToString("0.0") + " m over the deck at " + F(_to) + "; ropes out in " + RopeOutS + " s, then everyone down, one at a time per rope");
        }

        static void Touchdown(float now)
        {
            _touchdownAt = now; _liftAt = 0f; _forceLift = false; _aboardLast = -1;
            Comms(Il2Cpp.HelicopterV3.CommsSet.Land);
            _log?.Msg("[heli] touchdown on the pad at " + F(_to) + ". F within " + BoardRangeM.ToString("0") + " m of a free seat boards you; the pilot waits for everyone alive.");
        }

        static void SeatingTick(float now)
        {

            if (_mode == Mode.Infil && _leg == Leg.Approach && now - _legStart < 12f && now >= _nextLate) { _nextLate = now + 0.5f; SeatLatecomers(); }

            for (int i = _bolts.Count - 1; i >= 0; i--)
            {
                var b = _bolts[i];
                if (b.Seat.Chair == null) continue;
                try
                {
                    if (!b.Seated && now - b.SitAskedAt > SitFallbackS) { _log?.Msg("[heli] seat " + b.Seat.Index + ": the game's sit path did not arrive in " + SitFallbackS + " s, seating directly"); SitNow(b); }
                    if (b.Standing && now - b.StandAskedAt > SitFallbackS)
                    {
                        bool still = false; try { still = b.Pn.chair != null && b.Pn.chair.Pointer == b.Seat.Chair.Pointer; } catch { }
                        _log?.Msg("[heli] seat " + b.Seat.Index + ": the stand animation did not finish in " + SitFallbackS + " s, standing directly");
                        if (still) StandNow(b.Seat.Chair, "stand fallback"); else Unbolt(b, Exit.Auto, "stand fallback");
                    }
                }
                catch { }
            }

            bool landed = _leg == Leg.Landed && _mode == Mode.Exfil;
            if (!landed) return;

            bool f = false; try { f = Input.GetKeyDown(KeyCode.F); } catch { }
            if (f)
            {
                var me = LocalPlayer();
                if (me != null && BoltOf(me) == null && SliderOf(me) == null)
                {
                    var root = RootOf(me);
                    Seat best = null; float bestD = BoardRangeM;
                    for (int i = 0; i < _seats.Count; i++) { if (!SeatFree(_seats[i]) || root == null) continue; float d = Vector3.Distance(root.position, _seats[i].SitPoint.position); if (d < bestD) { bestD = d; best = _seats[i]; } }
                    if (best != null) { uint id = 0; try { id = me.netId; } catch { } Board(me, id, best, "F"); }
                    else _log?.Msg("[heli] no free seat within " + BoardRangeM.ToString("0") + " m");
                }
            }

            if (now - _touchdownAt < 1.5f) return;
            var ps = Players();
            for (int i = 0; i < ps.Count; i++)
            {
                var pn = ps[i].Value; if (IsLocal(pn) || BoltOf(pn) != null || SliderOf(pn) != null) continue;
                var root = RootOf(pn); if (root == null) continue;
                for (int s = 0; s < _seats.Count; s++)
                {
                    if (!SeatFree(_seats[s])) continue;
                    if (Vector3.Distance(root.position, _seats[s].SitPoint.position) <= ArriveM) { Board(pn, ps[i].Key, _seats[s], "arrived at the seat"); break; }
                }
            }
        }

        static bool ReadyToLift(float now)
        {
            if (_forceLift) return true;
            if (_mode == Mode.Infil)
            {
                bool everyoneDown = _bolts.Count == 0 && _sliders.Count == 0;
                bool overdue = _ropesOut && now - _hoverAt > DismountWaitS;
                if ((everyoneDown || overdue) && !_retracting)
                {
                    _retracting = true;
                    if (overdue && !everyoneDown) _log?.Msg("[heli] " + DismountWaitS.ToString("0") + " s on the hover: leaving with " + _bolts.Count + " seated and " + _sliders.Count + " on the ropes");
                    for (int i = 0; i < 2; i++) { try { if (_ropes[i] != null) _ropes[i].Retract(); } catch { } }
                }
                if (!_retracting) return false;
                for (int i = 0; i < 2; i++) if (_ropes[i] != null && _ropes[i].Mode != FastRope.State.Stowed) return false;
                return true;
            }

            var ps = Players();
            int aboard = 0; for (int i = 0; i < ps.Count; i++) if (BoltOf(ps[i].Value) != null) aboard++;
            if (aboard != _aboardLast) { _aboardLast = aboard; _log?.Msg("[heli] " + aboard + " of " + ps.Count + " aboard"); }
            bool all = ps.Count > 0 && aboard >= ps.Count;
            if (!all) { _liftAt = 0f; return false; }
            if (_liftAt <= 0f) _liftAt = now + 2f;
            return now >= _liftAt;
        }

        static void LiftOff()
        {
            _forceLift = false;
            if (_mode == Mode.Exfil) Comms(Il2Cpp.HelicopterV3.CommsSet.RTB);
            _log?.Msg("[heli] lifting off" + (_mode == Mode.Exfil ? " with " + _bolts.Count + " aboard" : ""));
        }

        static void Finish()
        {
            _log?.Msg("[heli] " + _mode + " ride complete");
            if (_mode == Mode.Infil)
            {

                for (int i = _sliders.Count - 1; i >= 0; i--) { try { EndSlide(_sliders[i], "the ride ended", true); } catch { } }
                for (int i = _bolts.Count - 1; i >= 0; i--) { try { var b = _bolts[i]; if (b.Seat.Chair != null && b.Seated) StandNow(b.Seat.Chair, "the ride ended"); else Unbolt(b, Exit.Pad, "the ride ended"); } catch { } }
                _gainTarget = 0f;
                try { _root.transform.position = _pad + Vector3.up * 5000f; } catch { }
                _mode = Mode.None; _leg = Leg.Idle;
            }
            else
            {

                _leg = Leg.Done;
            }
        }

        static bool ExtractionOpen()
        {
            try
            {
                var zones = Resources.FindObjectsOfTypeAll<Il2Cpp.ExfilZone>();
                for (int i = 0; i < (zones != null ? zones.Length : 0); i++)
                {
                    var z = zones[i]; if (z == null) continue;
                    bool inScene = false; try { inScene = z.gameObject.scene.IsValid(); } catch { }
                    if (inScene && z.canExtract) return true;
                }
            }
            catch { }
            return false;
        }

        static void HandOverToTheGame()
        {
            _zonesOnHeli = true;
            try
            {
                MapExfil.PlaceAllOn(_root.transform);
                _log?.Msg("[heli] airborne with everyone: the extraction zones now ride with the airframe, so the game's own "
                        + "extraction counts the passengers and ends the mission its own way");
            }
            catch (Exception e) { _log?.Warning("[heli] hand over: " + e.Message); }
        }

        static void Comms(Il2Cpp.HelicopterV3.CommsSet set)
        {
            try { if (_heli != null && _heli.pilotCommsSource != null) _heli.LocalPlayCommsSet(set, 0); } catch { }
        }

        static void Rotors()
        {
            _rotorSpin = Mathf.MoveTowards(_rotorSpin, 1f, Time.deltaTime / SpinUpS);
            float dt = Time.deltaTime;
            if (_mainRotor != null) _mainRotor.Rotate(0f, RotorRpm * 6f * _rotorSpin * dt, 0f, Space.Self);
            if (_tailRotor != null) _tailRotor.Rotate(TailRpm * 6f * _rotorSpin * dt, 0f, 0f, Space.Self);
        }

        static void MakeSound(string dir)
        {
            try
            {
                AudioClip loop = null;
                try { loop = LoadWav(Path.Combine(dir, "audio", "rotor_loop.wav"), "bh_rotor_loop"); } catch (Exception e) { _log?.Warning("[heli] rotor loop: " + e.Message); }
                if (loop != null)
                {
                    _rotorFromFile = true;
                    _rotorSrc = Src("rotor", loop, 900f);
                    _log?.Msg("[heli] sound: the rotor loop cut from his recording (" + loop.length.ToString("0") + " s, " + loop.frequency + " Hz), spatial with Doppler, faded in and out"
                            + (_heli != null && _heli.pilotCommsSource != null ? "; pilot comms from the game's crew" : ""));
                }
                else
                {
                    _rotorSrc   = Src("rotor",   Clip("bh_rotor",   2.0f, RotorWave),   900f);
                    _turbineSrc = Src("turbine", Clip("bh_turbine", 2.0f, TurbineWave), 500f);
                    _tailSrc    = Src("tail",    Clip("bh_tail",    1.0f, TailWave),    350f);
                    _log?.Msg("[heli] sound: no rotor_loop.wav shipped, synthesised rotor slap, turbine whine and tail buzz instead");
                }
            }
            catch (Exception e) { _log?.Warning("[heli] sound: " + e.Message); }
        }

        static AudioClip LoadWav(string path, string name)
        {
            if (!File.Exists(path)) return null;
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 44 || bytes[0] != (byte)'R' || bytes[1] != (byte)'I' || bytes[2] != (byte)'F' || bytes[3] != (byte)'F') return null;
            int pos = 12, channels = 1, rate = 44100, bits = 16, format = 1; int dataOff = -1, dataLen = 0;
            while (pos + 8 <= bytes.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(bytes, pos, 4); int len = BitConverter.ToInt32(bytes, pos + 4);
                if (id == "fmt ") { format = BitConverter.ToInt16(bytes, pos + 8); channels = BitConverter.ToInt16(bytes, pos + 10); rate = BitConverter.ToInt32(bytes, pos + 12); bits = BitConverter.ToInt16(bytes, pos + 22); }
                else if (id == "data") { dataOff = pos + 8; dataLen = Math.Min(len, bytes.Length - dataOff); break; }
                pos += 8 + len + (len & 1);
            }
            if (dataOff < 0 || channels < 1) return null;
            int frames;
            float[] data;
            if (format == 3 && bits == 32) { frames = dataLen / (4 * channels); data = new float[frames]; for (int i = 0; i < frames; i++) { float s = 0f; for (int c = 0; c < channels; c++) s += BitConverter.ToSingle(bytes, dataOff + (i * channels + c) * 4); data[i] = s / channels; } }
            else if (bits == 16) { frames = dataLen / (2 * channels); data = new float[frames]; for (int i = 0; i < frames; i++) { float s = 0f; for (int c = 0; c < channels; c++) s += BitConverter.ToInt16(bytes, dataOff + (i * channels + c) * 2) / 32768f; data[i] = s / channels; } }
            else return null;
            var clip = AudioClip.Create(name, frames, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static AudioSource Src(string name, AudioClip clip, float maxDist)
        {
            var go = new GameObject("snd_" + name); go.transform.SetParent(_root.transform, false);
            var s = go.AddComponent<AudioSource>();
            s.clip = clip; s.loop = true; s.spatialBlend = 1f; s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.minDistance = 25f; s.maxDistance = maxDist; s.dopplerLevel = 1.2f; s.volume = 0f; s.playOnAwake = false;
            s.Play();
            return s;
        }

        const int Rate = 44100;
        static AudioClip Clip(string name, float seconds, Func<int, float> wave)
        {
            int n = (int)(Rate * seconds);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = wave(i);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float RotorWave(int i)
        {
            float t = i / (float)Rate;
            float bps = RotorRpm / 60f * 4f;
            float phase = (t * bps) % 1f;
            float thump = Mathf.Exp(-phase * 14f) * Mathf.Sin(2f * Mathf.PI * 62f * phase / bps);
            float noise = (Hash(i) - 0.5f) * Mathf.Exp(-phase * 9f) * 0.9f;
            float drone = 0.25f * Mathf.Sin(2f * Mathf.PI * 34.4f * t) + 0.12f * Mathf.Sin(2f * Mathf.PI * 68.8f * t);
            return Mathf.Clamp(0.55f * thump + noise + drone, -1f, 1f) * 0.8f;
        }

        static float TurbineWave(int i)
        {
            float t = i / (float)Rate;
            float f = 1120f + 18f * Mathf.Sin(2f * Mathf.PI * 0.7f * t);
            float s = 0.5f * Mathf.Sin(2f * Mathf.PI * f * t) + 0.25f * Mathf.Sin(2f * Mathf.PI * 2f * f * t + 0.3f)
                    + 0.12f * Mathf.Sin(2f * Mathf.PI * 3f * f * t) + 0.15f * (Hash(i) - 0.5f);
            return Mathf.Clamp(s, -1f, 1f) * 0.6f;
        }

        static float TailWave(int i)
        {
            float t = i / (float)Rate;
            float f = TailRpm / 60f * 4f;
            float saw = 2f * ((t * f) % 1f) - 1f;
            return Mathf.Clamp(0.5f * saw + 0.2f * Mathf.Sin(2f * Mathf.PI * f * 3f * t) + 0.1f * (Hash(i) - 0.5f), -1f, 1f) * 0.5f;
        }

        static float Hash(int i) { uint x = (uint)i * 2654435761u; x ^= x >> 13; x *= 0x5bd1e995u; x ^= x >> 15; return (x & 0xFFFFFF) / 16777216f; }

        static void Audio()
        {
            _gain = Mathf.MoveTowards(_gain, _gainTarget, Time.deltaTime / FadeS);
            float p = 0.85f + 0.2f * _rotorSpin;
            float g = _rotorSpin * _gain;
            try { if (_rotorSrc != null) { _rotorSrc.pitch = _rotorFromFile ? 0.96f + 0.04f * _rotorSpin : p; _rotorSrc.volume = (_rotorFromFile ? 1.0f : 0.9f) * g; } } catch { }
            try { if (_turbineSrc != null) { _turbineSrc.pitch = 0.9f + 0.15f * _rotorSpin; _turbineSrc.volume = 0.35f * Mathf.Clamp01(_rotorSpin * 1.5f) * _gain; } } catch { }
            try { if (_tailSrc != null) { _tailSrc.pitch = p; _tailSrc.volume = 0.25f * g; } } catch { }
        }

        public static string Console(string arg)
        {
            arg = (arg ?? "").Trim().ToLowerInvariant();
            if (!MapIdentity.HeliInfil) return "the Black Hawk only flies on maps that ask for it (Rig).";
            if (!_armed) return "not armed: the map is not built, or the pad could not be read.";
            if (arg == "fly")     { _infilFlown = false; Begin(Mode.Infil, "console"); return "ride in started."; }
            if (arg == "exfil")   { Begin(Mode.Exfil, "console"); return "ride out started."; }
            if (arg == "go")      { _forceLift = true; return "the pilot leaves now."; }
            if (arg == "rope")    { BuildRopes(); for (int i = 0; i < 2; i++) if (_ropes[i] != null) _ropes[i].Deploy(); _ropesOut = true; return "ropes out."; }
            if (arg == "release") { ReleaseAll("console", Exit.Auto); return "everyone stood up and released."; }
            if (arg == "board")
            {
                var me = LocalPlayer(); var s = FreeSeat();
                if (me == null || s == null) return "no body or no free seat.";
                uint id = 0; try { id = me.netId; } catch { }
                return Board(me, id, s, "console") ? "seated." : "could not seat you.";
            }
            return "Black Hawk: mode=" + _mode + " leg=" + _leg + " seated=" + _bolts.Count + " onRopes=" + _sliders.Count + " riding=" + Riding + " seats=" + _seats.Count
                 + " ropes=" + (_ropes[0] != null ? _ropes[0].Mode.ToString() : "none") + "/" + (_ropes[1] != null ? _ropes[1].Mode.ToString() : "none")
                 + " cabin=" + _cabinSource + " pilots=" + _pilots + " deck=" + F(_pad)
                 + ". 'odg heli fly' / 'exfil' / 'go' / 'rope' / 'board' / 'release'.";
        }

        static string F(Vector3 v) { return "(" + v.x.ToString("0.0") + ", " + v.y.ToString("0.0") + ", " + v.z.ToString("0.0") + ")"; }
        static string FL(Vector3 v) { return "(" + v.x.ToString("0.00") + ", " + v.y.ToString("0.00") + ", " + v.z.ToString("0.00") + ")"; }
    }
}
