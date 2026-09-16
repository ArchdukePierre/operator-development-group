// ODG Operations - FireteamRoles
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FireteamRoles
{

    internal static class Ladder
    {
        class Placed
        {
            internal GameObject Go;
            internal Vector3 Base;
            internal Vector3 Top;
            internal Vector3 Axis;
            internal Vector3 Normal;
            internal Vector3 Face;
            internal ulong Owner;
        }

        static readonly List<Placed> _live = new List<Placed>();
        static readonly HashSet<string> _seen = new HashSet<string>();
        static readonly List<string> _recent = new List<string>();
        static int _seq;
        static float _nextPoll;

        static Mesh _mesh;
        static Material _mat;

        internal const float MeshLength = 3.21f;
        internal static float Length { get { return Mathf.Clamp(Cfg.LadderLength.Value, 2.5f, 5.5f); } }
        static Vector3 Stretch(float worldLen) { return new Vector3(1f, Mathf.Max(0.3f, worldLen / MeshLength), 1f); }

        static string ContentDir
        {
            get
            {
                try { return Path.Combine(Epi.ContentDir, "ladder"); }
                catch { return "Mods/FireteamRoles_Content/ladder"; }
            }
        }

        static Renderer DonorNear(Vector3 at)
        {
            try
            {
                var hits = Physics.OverlapSphere(at, 6f, ~0, QueryTriggerInteraction.Ignore);
                if (hits == null) return null;
                for (int i = 0; i < hits.Length; i++)
                {
                    if (hits[i] == null) continue;
                    var r = hits[i].GetComponent<MeshRenderer>();
                    if (r != null && r.sharedMaterial != null) return r;
                }
            }
            catch { }
            return null;
        }

        static bool EnsureAsset(Renderer shaderDonor)
        {
            if (_mesh != null && _mat != null) return true;

            try
            {
                string dir = ContentDir;
                if (_mesh == null)
                {
                    string why;
                    _mesh = ObjMesh.Load(Path.Combine(dir, "ladder.obj"),
                                         Mathf.Max(0.0001f, Cfg.LadderScale.Value), out why);
                    if (_mesh == null) { Log.Err("ladder mesh failed: " + why); return false; }
                }

                if (_mat == null) _mat = BuildMaterial(dir, shaderDonor);
                try { if (_mat != null) _mat.hideFlags = HideFlags.DontUnloadUnusedAsset; } catch { }
                return _mesh != null && _mat != null;
            }
            catch (Exception e) { Err.Note("ladder.asset", e); return false; }
        }

        static Material BuildMaterial(string dir, Renderer donor)
        {
            Material m = null;
            try
            {
                var sh = Shader.Find("HDRP/Lit");
                if (sh != null) m = new Material(sh);
            }
            catch { }

            if (m == null && donor != null)
            {
                try
                {
                    var src = donor.sharedMaterial;
                    if (src != null) { m = new Material(src); Log.Detail("ladder material cloned from " + donor.name); }
                }
                catch { }
            }
            if (m == null) { Log.Err("ladder: no usable shader to build a material from"); return null; }

            m.name = "odg_ladder";
            SetTex(m, "_BaseColorMap", LoadTex(dir, "basecolor.png", false));
            SetTex(m, "_MainTex", LoadTex(dir, "basecolor.png", false));
            SetTex(m, "_NormalMap", LoadTex(dir, "normal.png", true));
            SetTex(m, "_BumpMap", LoadTex(dir, "normal.png", true));

            var mask = BuildMask(dir);
            SetTex(m, "_MaskMap", mask);
            try { if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 1f); } catch { }
            try { if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 1f); } catch { }
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
                if (!File.Exists(p)) { Log.Detail("ladder texture missing: " + file); return null; }
                var bytes = File.ReadAllBytes(p);
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear);
                var arr = MeshCut.FromManaged(bytes);
                if (!UnityEngine.ImageConversion.LoadImage(t, arr, false))
                { Log.Detail("ladder texture decode failed: " + file); return null; }
                t.name = "odg_ladder_" + file;
                t.hideFlags = HideFlags.DontUnloadUnusedAsset;
                return t;
            }
            catch (Exception e) { Log.Detail("ladder texture " + file + " failed: " + e.Message); return null; }
        }

        static Texture2D BuildMask(string dir)
        {
            try
            {
                var met = LoadTex(dir, "metallic.png", true);
                var rgh = LoadTex(dir, "roughness.png", true);
                if (met == null && rgh == null) return null;

                int w = met != null ? met.width : rgh.width;
                int h = met != null ? met.height : rgh.height;
                var outT = new Texture2D(w, h, TextureFormat.RGBA32, true, true);
                outT.name = "odg_ladder_mask";

                var px = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Color32>(w * h);
                for (int y = 0; y < h; y++)
                {
                    float fy = (float)y / Mathf.Max(1, h - 1);
                    for (int x = 0; x < w; x++)
                    {
                        float fx = (float)x / Mathf.Max(1, w - 1);
                        byte mtl = met != null ? (byte)(Mathf.Clamp01(met.GetPixelBilinear(fx, fy).r) * 255f) : (byte)0;

                        byte smooth = rgh != null
                            ? (byte)((1f - Mathf.Clamp01(rgh.GetPixelBilinear(fx, fy).r)) * 255f)
                            : (byte)128;
                        px[y * w + x] = new Color32(mtl, 255, 0, smooth);
                    }
                }
                outT.SetPixels32(px);
                outT.Apply(true, false);
                outT.hideFlags = HideFlags.DontUnloadUnusedAsset;
                return outT;
            }
            catch (Exception e) { Log.Detail("ladder mask failed: " + e.Message); return null; }
        }

        internal static bool Aiming { get; private set; }
        static Vector3 _pBase, _pTop, _pAxis, _pNormal;
        static bool _valid;
        static string _why = "";

        static bool _armed;

        static bool _nearLadder;

        internal static string PromptText()
        {

            if (_mantling) return "over the top";
            if (_climbing && _atTop) return _haveLanding ? "forward = onto the roof   -   left/right = step off   -   back = down"
                                                        : "hold forward = over the top   -   left/right = step off   -   back = down";
            if (_climbing) return "toward the wall = up   -   away = down";
            if (_nearLadder) return "LADDER     walk into it to climb";
            if (!_armed) return "";
            string k = Cfg.LadderKey.Value.ToUpperInvariant();
            return _valid ? "[" + k + "] place ladder"
                          : "[" + k + "] cancel   -   " + _why;
        }

        internal static void Tick()
        {
            Aiming = false;

            if (!Cfg.LadderEnabled.Value || !State.Enabled) { StopClimb(); return; }

            VaultWatchdog();
            PumpMantle();

            if (_climbing)
            {
                var m = State.Me;
                bool ok = m != null && m.Live && !Downed.AmDowned
                          && _onLadder != null && _onLadder.Go != null
                          && Time.realtimeSinceStartup < _climbUntil;
                if (!ok) { StopClimb(); return; }
            }

            ClimbAssist();
            PumpPreview();
        }

        static float _climbUntil;

        static void PumpPreview()
        {
            bool eligible = State.MyRole == Role.Assaulter && !Downed.AmDowned;
            var me = State.Me;
            if (me == null || !me.Live) eligible = false;
            if (!eligible) { _armed = false; return; }

            bool pressed = Input.GetKeyDown(Cfg.Key(Cfg.LadderKey, KeyCode.L));

            if (!_armed)
            {
                if (pressed)
                {
                    if (!EnsureAsset(null)) { Ui.Toast("ladder asset missing"); return; }
                    _armed = true;
                    Ui.Toast("ladder ready - place it with " + Cfg.LadderKey.Value.ToUpperInvariant());
                }
                return;
            }

            var cam = Cam();
            if (cam == null) { _armed = false; return; }

            _valid = Solve(cam.transform.position, cam.transform.forward, out _pBase, out _pTop,
                           out _pAxis, out _pNormal, out _why);
            Aiming = true;

            if (_valid && _mesh != null && _mat != null)
            {
                try
                {
                    Graphics.DrawMesh(_mesh, Matrix4x4.TRS((_pBase + _pTop) * 0.5f,
                                                           Prop(_pAxis, _pNormal),
                                                           Stretch(Vector3.Distance(_pBase, _pTop))), _mat, 0);
                }
                catch { }
            }

            if (!pressed) return;
            if (_valid) { Deploy(); _armed = false; }
            else { _armed = false; Ui.Toast("ladder cancelled"); }
        }

        static Quaternion Prop(Vector3 axis, Vector3 normal)
        {
            Vector3 across = Vector3.Cross(Vector3.up, normal);
            if (across.sqrMagnitude < 1e-6f) across = Vector3.Cross(Vector3.forward, normal);
            across = across.normalized;

            Vector3 fwd = -Vector3.Cross(across, axis).normalized;
            if (fwd.sqrMagnitude < 1e-6f) fwd = -normal;
            return Quaternion.LookRotation(fwd, axis);
        }

        static bool Solve(Vector3 eye, Vector3 fwd, out Vector3 baseP, out Vector3 topP,
                          out Vector3 axis, out Vector3 normal, out string why)
        {
            baseP = topP = Vector3.zero; axis = Vector3.up; normal = Vector3.forward; why = "";

            RaycastHit hit;
            float range = Mathf.Clamp(Cfg.LadderRange.Value, 1f, 12f);
            if (!Physics.Raycast(eye, fwd, out hit, range, ~0, QueryTriggerInteraction.Ignore))
            { why = "no wall in reach"; return false; }

            normal = hit.normal;
            if (Mathf.Abs(normal.y) > 0.55f) { why = "not a wall"; return false; }
            normal.y = 0f;
            if (normal.sqrMagnitude < 1e-4f) { why = "not a wall"; return false; }
            normal = normal.normalized;

            float standoff = Mathf.Clamp(Cfg.LadderStandoff.Value, 0.2f, 1.6f);
            float maxSlope = Mathf.Clamp(Cfg.LadderMaxSlope.Value, 5f, 60f);
            Vector3 face = hit.point;

            RaycastHit down;
            if (!DropFoot(face + normal * standoff, out down)) { why = "no floor for the feet"; return false; }
            Vector3 plainFoot = down.point;

            if (Vector3.Angle(down.normal, Vector3.up) > maxSlope) { why = "ground too steep"; return false; }

            float len, wallTop;
            if (Cfg.LadderAutoLength.Value)
            {
                float maxLen = Mathf.Clamp(Cfg.LadderMaxLength.Value, 3f, 9f);
                float minLen = Mathf.Clamp(Cfg.LadderMinLength.Value, 1.5f, maxLen);
                float reachRise = Mathf.Sqrt(Mathf.Max(0.01f, maxLen * maxLen - standoff * standoff));
                wallTop = FeelWallTop(face, plainFoot.y, normal, reachRise + 0.3f);
                if (wallTop >= reachRise + 0.25f) { why = "too tall for the ladder"; return false; }
                float rise = wallTop + 0.7f;
                len = Mathf.Clamp(Mathf.Sqrt(rise * rise + standoff * standoff), minLen, maxLen);
            }
            else
            {
                len = Length;
                float rise0 = Mathf.Sqrt(Mathf.Max(0.01f, len * len - standoff * standoff));
                wallTop = FeelWallTop(face, plainFoot.y, normal, rise0 + 0.6f);
            }

            float maxRise = Mathf.Sqrt(Mathf.Max(0.01f, len * len - standoff * standoff));

            string block = "";
            if (wallTop > 0.8f && wallTop < maxRise - 0.3f)
            {
                float shift = (maxRise - wallTop) / maxRise * standoff;
                RaycastHit d2;
                if (DropFoot(face + normal * (standoff - shift), out d2)
                    && Vector3.Angle(d2.normal, Vector3.up) <= maxSlope)
                {
                    Vector3 b = d2.point;
                    Vector3 t = new Vector3(face.x, b.y + maxRise, face.z) - normal * shift;
                    if (Clear(b, t, normal, out block))
                    { baseP = b; topP = t; axis = (t - b).normalized; return true; }
                }
            }

            {
                Vector3 b = plainFoot;
                Vector3 t = new Vector3(face.x, b.y + maxRise, face.z);
                if (Clear(b, t, normal, out block))
                { baseP = b; topP = t; axis = (t - b).normalized; return true; }
            }

            why = block;
            return false;
        }

        static bool DropFoot(Vector3 at, out RaycastHit down)
        {
            return Physics.Raycast(at + Vector3.up * 0.6f, Vector3.down, out down, 6f, ~0,
                                   QueryTriggerInteraction.Ignore);
        }

        static bool Clear(Vector3 b, Vector3 t, Vector3 normal, out string why)
        {
            why = "";
            Vector3 axis = t - b;
            if (axis.sqrMagnitude < 0.01f) { why = "no room"; return false; }
            axis = axis.normalized;
            Vector3 off = normal * 0.12f;
            if (Physics.CheckCapsule(b + axis * 0.35f + off, t - axis * 0.35f + off, 0.10f, ~0,
                                     QueryTriggerInteraction.Ignore))
            { why = "blocked"; return false; }
            return true;
        }

        static float FeelWallTop(Vector3 face, float footY, Vector3 normal, float maxRise)
        {
            try
            {
                const int Steps = 18;
                float last = 0f, step = maxRise / Steps;
                for (int i = 1; i <= Steps; i++)
                {
                    float h = step * i;
                    if (!WallAt(face, footY + h, normal))
                    {

                        float lo = last, hi = h;
                        for (int k = 0; k < 4; k++)
                        {
                            float mid = (lo + hi) * 0.5f;
                            if (WallAt(face, footY + mid, normal)) lo = mid; else hi = mid;
                        }
                        return lo;
                    }
                    last = h;
                }
            }
            catch { }
            return maxRise;
        }

        static bool WallAt(Vector3 face, float y, Vector3 normal)
        {
            Vector3 o = new Vector3(face.x, y, face.z) + normal * 0.45f;
            RaycastHit hh;
            return Physics.Raycast(o, -normal, out hh, 0.9f, ~0, QueryTriggerInteraction.Ignore);
        }

        static void Deploy()
        {
            if (!_valid) { Ui.Toast("ladder: " + _why); return; }

            int cap = Mathf.Max(1, Cfg.LadderMaxPerPlayer.Value);
            int mine = 0;
            for (int i = 0; i < _live.Count; i++) if (_live[i].Owner == Net.Self) mine++;
            while (mine >= cap)
            {
                int oldest = -1;
                for (int i = 0; i < _live.Count; i++) { if (_live[i].Owner == Net.Self) { oldest = i; break; } }
                if (oldest < 0) break;
                Remove(oldest);
                mine--;
                Ui.Toast("ladder picked back up");
            }

            Renderer donor = null;
            try
            {
                RaycastHit h;
                var cam = Cam();
                if (cam != null && Physics.Raycast(cam.transform.position, cam.transform.forward, out h,
                                                   Cfg.LadderRange.Value, ~0, QueryTriggerInteraction.Ignore))
                    donor = h.collider != null ? h.collider.GetComponent<Renderer>() : null;
            }
            catch { }

            if (!EnsureAsset(donor)) { Ui.Toast("ladder asset missing"); return; }

            Spawn(_pBase, _pTop, _pNormal, Net.Self);
            Announce(_pBase, _pTop, _pNormal);
            Log.Fired("ladder-deployed");
            Ui.Toast("LADDER DOWN     walk into it to climb");
        }

        static void Spawn(Vector3 baseP, Vector3 topP, Vector3 normal, ulong owner)
        {
            try
            {
                if (!EnsureAsset(DonorNear(baseP))) return;

                Vector3 axis = (topP - baseP).normalized;

                Vector3 face = new Vector3(topP.x, baseP.y, topP.z);
                try
                {
                    RaycastHit fh;
                    Vector3 from = baseP + Vector3.up * 0.5f + normal * 0.2f;
                    if (Physics.Raycast(from, -normal, out fh, 3f, ~0, QueryTriggerInteraction.Ignore))
                        face = new Vector3(fh.point.x, baseP.y, fh.point.z);
                }
                catch { }

                var go = new GameObject("odg_ladder");
                go.transform.position = (baseP + topP) * 0.5f;
                go.transform.rotation = Prop(axis, normal);
                go.transform.localScale = Stretch(Vector3.Distance(baseP, topP));

                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = _mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _mat;

                var col = go.AddComponent<BoxCollider>();
                col.center = Vector3.zero;
                col.size = new Vector3(0.58f, MeshLength, 0.15f);

                col.isTrigger = true;

                _live.Add(new Placed { Go = go, Base = baseP, Top = topP, Axis = axis,
                                       Normal = normal, Face = face, Owner = owner });
                float over = Vector3.Dot(face - topP, normal);
                Log.Msg("ladder placed at " + baseP.ToString("0.0") + " -> " + topP.ToString("0.0") +
                        " by " + State.NameOf(owner) + (over > 0.02f ? ", head " + over.ToString("0.00") + "m past the edge" : ""));
            }
            catch (Exception e) { Err.Note("ladder.spawn", e); }
        }

        static void Remove(int i)
        {
            if (i < 0 || i >= _live.Count) return;
            try { if (_live[i].Go != null) UnityEngine.Object.Destroy(_live[i].Go); } catch { }
            _live.RemoveAt(i);
        }

        static float _stockMaxHeight = -1f, _stockDetect = -1f;
        static bool _assistOn;

        static void ClimbAssist()
        {
            var me = State.Me;
            if (me == null || !me.Live || Downed.AmDowned) { StopClimb(); return; }

            var near = NearestBase(me);
            bool at = near != null;
            _nearLadder = at;

            if (!at && _climbing && !_mantling) StopClimb();
            if (at && !_vaultRunning) Climb(near);

            VaultAssist(me, at, near != null ? Vector3.Distance(near.Base, near.Top) : Length);
        }

        static void VaultAssist(Peer me, bool at, float ladderLen)
        {
            Il2Cpp.PlayerVaultSystem vs = null;
            try
            {
                var mc = me != null && me.Pn != null ? me.Pn.masterController : null;
                vs = mc != null ? mc.vaultSystem : null;
            }
            catch { }
            if (vs == null)
            {
                if (!_vaultMissingLogged) { _vaultMissingLogged = true; Log.Detail("ladder: no vault system on this player, mantle assist off"); }
                return;
            }

            if (!Cfg.LadderRaiseVaultLimits.Value) at = false;

            if (_stockMaxHeight < 0f)
            {
                try { _stockMaxHeight = vs.maxHeight; _stockDetect = vs.detectionRange; }
                catch { return; }
            }

            if (at && !_assistOn)
            {
                _assistOn = true;
                try
                {
                    vs.maxHeight = Mathf.Max(_stockMaxHeight, ladderLen + Cfg.LadderVaultBonus.Value);
                    vs.detectionRange = Mathf.Max(_stockDetect, Cfg.LadderVaultReach.Value);
                }
                catch { }
                Log.Fired("ladder-climb-assist");
            }
            else if (!at && _assistOn)
            {
                _assistOn = false;
                try { vs.maxHeight = _stockMaxHeight; vs.detectionRange = _stockDetect; } catch { }
            }
        }

        static bool _vaultMissingLogged;

        static Placed NearestBase(Peer me)
        {
            if (me == null || me.Root == null || _live.Count == 0) return null;
            float best = Mathf.Max(0.5f, Cfg.LadderAssistRadius.Value);
            Placed found = null;
            Vector3 p = me.Root.position;
            for (int i = 0; i < _live.Count; i++)
            {
                var l = _live[i];
                if (l.Go == null) continue;

                Vector3 ab = l.Top - l.Base;
                float t = Mathf.Clamp01(Vector3.Dot(p - l.Base, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                float d = Vector3.Distance(p, l.Base + ab * t);
                if (d < best) { best = d; found = l; }
            }
            return found;
        }

        static bool _climbing;
        static Placed _onLadder;
        static bool _wasWalker, _wasMover, _wasKinematic;
        static Il2CppCMF.AdvancedWalkerController _walker;
        static Il2CppCMF.Mover _mover;
        static Rigidbody _rb;

        internal static bool Climbing { get { return _climbing; } }

        static Vector3 _lastPos;
        static float _lastPosAt;

        static float Intent(Placed l, Transform root, out float side)
        {

            side = 0f;
            Vector3 into = -l.Normal;
            Vector3 across = Across(l);

            Vector3 want = Vector3.zero;
            bool got = false;
            try
            {
                var w = _walker;
                if (w == null)
                {
                    var me = State.Me;
                    var mc = me != null && me.Pn != null ? me.Pn.masterController : null;
                    w = mc != null ? mc.walkerController : null;
                }
                if (w != null) { want = w.CalculateMovementDirection(); got = want.sqrMagnitude > 0.01f; }
            }
            catch { }

            if (!got)
            {

                var cam = Cam();
                if (cam != null)
                {
                    Vector3 f = cam.transform.forward; f.y = 0f; f = f.normalized;
                    Vector3 r = cam.transform.right; r.y = 0f; r = r.normalized;
                    if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) want += f;
                    if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) want -= f;
                    if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) want += r;
                    if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) want -= r;
                    got = want.sqrMagnitude > 0.01f;
                }
            }

            if (!got) return 0f;
            want.y = 0f;
            if (want.sqrMagnitude < 1e-4f) return 0f;
            Vector3 wn = want.normalized;
            side = Vector3.Dot(wn, across);
            return Vector3.Dot(wn, into);
        }

        static Vector3 Across(Placed l)
        {
            Vector3 a = Vector3.Cross(Vector3.up, l.Normal);
            return a.sqrMagnitude > 1e-6f ? a.normalized : Vector3.right;
        }

        static Vector3 Rail(Placed l, float along, float lat)
        {
            return l.Base + l.Axis * along + l.Normal * 0.35f + Across(l) * lat;
        }

        static float _lat;
        static float _topHold = -1f;
        static bool _atTop;

        static void Climb(Placed l)
        {
            if (_mantling) return;

            var me = State.Me;

            Transform root = null;
            try { root = _rb != null ? _rb.transform : null; } catch { }
            if (root == null) root = me != null ? me.Root : null;
            if (root == null) { StopClimb(); return; }

            float side;
            float intent = Intent(l, root, out side);
            float len = Vector3.Distance(l.Base, l.Top);

            if (!_climbing)
            {

                bool pressing = intent > 0.35f;

                bool moving = false;
                float dt = Time.realtimeSinceStartup - _lastPosAt;
                if (dt > 0.001f && dt < 0.5f)
                {
                    Vector3 v = (root.position - _lastPos) / dt; v.y = 0f;
                    Vector3 toLadder = l.Base - root.position; toLadder.y = 0f;
                    if (v.sqrMagnitude > 0.2f && toLadder.sqrMagnitude > 1e-4f)
                        moving = Vector3.Dot(v.normalized, toLadder.normalized) > 0.5f;
                }
                _lastPos = root.position; _lastPosAt = Time.realtimeSinceStartup;

                if (!pressing && !moving) return;

                if (Cfg.LadderVaultFromGround.Value && TryVaultRoute(l, root)) return;

                if (!StartClimb(l)) return;

                _lat = 0f; _topHold = -1f; _atTop = false;
                float a0 = Mathf.Clamp(Vector3.Dot(root.position - l.Base, l.Axis), 0.05f, len - 0.3f);
                Vector3 onLine = Rail(l, a0, 0f);
                if (!Blocked(onLine)) Place(root, onLine);
            }

            _lastPos = root.position; _lastPosAt = Time.realtimeSinceStartup;

            float along = Vector3.Dot(root.position - l.Base, l.Axis);
            float speed = Mathf.Clamp(Cfg.LadderClimbSpeed.Value, 0.2f, 6f);
            _atTop = along >= len - 0.9f;

            float dirF = intent > 0.25f ? 1f : (intent < -0.25f ? -1f : 0f);
            float dirS = _atTop ? (side > 0.4f ? 1f : (side < -0.4f ? -1f : 0f)) : 0f;

            if (dirS != 0f && Mathf.Abs(side) >= Mathf.Abs(intent))
            {
                _topHold = -1f;
                StepOff(l, root, along, dirS * speed * 0.8f * Time.deltaTime);
                return;
            }

            if (dirF == 0f) { _topHold = -1f; return; }

            float want = along + dirF * speed * Time.deltaTime;

            if (dirF < 0f)
            {
                _topHold = -1f;

                if (want < 0.05f)
                {
                    Vector3 off = l.Base + l.Normal * 0.4f;
                    StopClimb();
                    if (!Blocked(off)) root.position = off;
                    return;
                }

                if (want < len - 0.9f) _lat = Mathf.MoveTowards(_lat, 0f, speed * Time.deltaTime);
                Vector3 p = Rail(l, want, _lat);
                if (Blocked(p)) return;
                Place(root, p);
                _climbUntil = Time.realtimeSinceStartup + 60f;
                Log.Fired("ladder-climbing");
                return;
            }

            Vector3 feet = Rail(l, want, _lat);
            bool atLanding = _haveLanding && feet.y >= _edge.y - 0.45f;
            bool railsOut = want > len - 0.3f;
            bool stuck = want > len - 1.0f && Blocked(feet);
            if (atLanding || (railsOut && _haveLanding) || stuck)
            {
                _topHold = -1f;
                TopOut(l, root, atLanding ? "reached the landing" : railsOut ? "ran out of rungs" : "blocked near the top");
                return;
            }
            if (railsOut)
            {

                if (_topHold < 0f) _topHold = Time.realtimeSinceStartup;
                if (Time.realtimeSinceStartup - _topHold >= 0.35f)
                {
                    _topHold = -1f;
                    TopOut(l, root, "held forward at the top");
                }
                return;
            }
            _topHold = -1f;
            if (Blocked(feet)) return;
            Place(root, feet);
            _climbUntil = Time.realtimeSinceStartup + 60f;
            Log.Fired("ladder-climbing");
        }

        static void StepOff(Placed l, Transform root, float along, float delta)
        {
            float lat = _lat + delta;
            Vector3 p = Rail(l, along, lat);
            try
            {
                RaycastHit h;
                if (Physics.Raycast(p + Vector3.up * 0.6f, Vector3.down, out h, 1.7f, ~0, QueryTriggerInteraction.Ignore)
                    && h.normal.y > 0.5f)
                {
                    Vector3 land = h.point + Vector3.up * 0.05f;
                    if (land.y <= p.y + 0.55f && land.y >= p.y - 1.1f && !Blocked(land))
                    {
                        Place(root, land);
                        StopClimb();
                        Ui.Toast("stepped off");
                        Log.Msg("ladder: stepped off sideways onto " + land.ToString("0.0"));
                        Log.Fired("ladder-step-off");
                        return;
                    }
                }
            }
            catch { }
            if (Mathf.Abs(lat) > 0.55f) return;
            if (Blocked(p)) return;
            _lat = lat;
            Place(root, p);
        }

        static void Place(Transform root, Vector3 p)
        {
            root.position = p;
            if (_rb != null) { try { _rb.position = p; } catch { } }
        }

        static Vector3 Nearest(Placed l, Vector3 p)
        {
            Vector3 ab = l.Top - l.Base;
            float t = Mathf.Clamp01(Vector3.Dot(p - l.Base, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
            return l.Base + ab * t + l.Normal * 0.35f;
        }

        static bool Blocked(Vector3 p)
        {
            try
            {
                var cols = Physics.OverlapCapsule(p + Vector3.up * 0.35f, p + Vector3.up * 1.55f, 0.2f, ~0, QueryTriggerInteraction.Ignore);
                if (cols == null) return false;
                var me = State.Me;
                IntPtr mine = me != null && me.Pn != null ? me.Pn.Pointer : IntPtr.Zero;
                for (int i = 0; i < cols.Length; i++)
                {
                    var c = cols[i];
                    if (c == null) continue;
                    if (mine != IntPtr.Zero)
                    {
                        try
                        {
                            var pn = c.GetComponentInParent<Il2Cpp.PlayerNetworking>();
                            if (pn != null && pn.Pointer == mine) continue;
                        }
                        catch { }
                    }
                    try { if (c.gameObject.name.StartsWith("odg_")) continue; } catch { }
                    return true;
                }
                return false;
            }
            catch { return false; }
        }

        static bool _haveLanding;
        static Vector3 _edge, _landing;
        static string _landingHow = "";

        static readonly float[] Heights = { -0.9f, -0.6f, -0.3f, 0.1f, 0.4f, 0.7f, 1.0f, 1.3f };
        static readonly float[] Depths = { 0.9f, 0.6f, 1.3f, 0.3f };

        static bool FindLanding(Placed l, out Vector3 edge, out Vector3 landing, out string how)
        {
            edge = landing = Vector3.zero; how = "";
            Collider own = null;
            try { own = l.Go != null ? l.Go.GetComponent<Collider>() : null; if (own != null) own.enabled = false; }
            catch { own = null; }
            try
            {
                float reach = Mathf.Clamp(Cfg.LadderTopReach.Value, 0f, 2f);
                Vector3 face = new Vector3(l.Face.x, l.Top.y, l.Face.z);
                Vector3 n = l.Normal;
                int tried = 0;
                for (int hi = 0; hi < Heights.Length; hi++)
                {
                    float h = Heights[hi];
                    if (h > reach + 0.05f) break;
                    Vector3 outP = face + Vector3.up * h + n * 0.35f;
                    for (int di = 0; di < Depths.Length; di++)
                    {
                        float d = Depths[di];
                        Vector3 inP = face + Vector3.up * h - n * d;
                        tried++;

                        if (Physics.Linecast(outP, inP, ~0, QueryTriggerInteraction.Ignore)) continue;

                        RaycastHit hit;
                        float drop = Mathf.Max(0.6f, h + 1.4f);
                        if (!Physics.Raycast(inP, Vector3.down, out hit, drop, ~0, QueryTriggerInteraction.Ignore)) continue;
                        if (hit.normal.y < 0.5f) continue;
                        Vector3 land = hit.point + Vector3.up * 0.05f;

                        if (Blocked(land)) continue;

                        float lipY = Mathf.Max(land.y, l.Top.y + h - 0.3f);
                        RaycastHit lip;
                        Vector3 lipProbe = face + Vector3.up * (h + 0.05f) - n * 0.12f;
                        if (Physics.Raycast(lipProbe, Vector3.down, out lip, 0.7f, ~0, QueryTriggerInteraction.Ignore))
                            lipY = Mathf.Max(land.y, lip.point.y);

                        edge = new Vector3(face.x, lipY, face.z);
                        landing = land;
                        how = "lip " + (lipY - l.Top.y).ToString("+0.00;-0.00") + "m from the head, floor "
                            + d.ToString("0.0") + "m in at " + (land.y - l.Top.y).ToString("+0.00;-0.00") + "m";
                        return true;
                    }
                }
                how = "no floor within reach behind the wall (" + tried + " probes)";
                return false;
            }
            catch (Exception e) { how = "search threw: " + e.Message; return false; }
            finally { try { if (own != null) own.enabled = true; } catch { } }
        }

        static bool FindOver(Placed l, out Vector3 edge, out Vector3 drop, out string how)
        {
            edge = drop = Vector3.zero; how = "";
            try
            {
                float reach = Mathf.Clamp(Cfg.LadderTopReach.Value, 0f, 2f);
                Vector3 face = new Vector3(l.Face.x, l.Top.y, l.Face.z);
                Vector3 n = l.Normal;
                for (int hi = 0; hi < Heights.Length; hi++)
                {
                    float h = Heights[hi];
                    if (h > reach + 0.05f) break;
                    Vector3 outP = face + Vector3.up * h + n * 0.35f;
                    Vector3 inP = face + Vector3.up * h - n * 0.7f;
                    if (Physics.Linecast(outP, inP, ~0, QueryTriggerInteraction.Ignore)) continue;
                    float lipY = l.Top.y + h;
                    RaycastHit lip;
                    if (Physics.Raycast(face + Vector3.up * (h + 0.05f) - n * 0.12f, Vector3.down, out lip, 0.7f, ~0, QueryTriggerInteraction.Ignore))
                        lipY = lip.point.y;
                    edge = new Vector3(face.x, lipY, face.z);
                    drop = edge - n * 0.7f + Vector3.up * 0.1f;
                    if (Blocked(drop)) continue;
                    how = "lip " + (lipY - l.Top.y).ToString("+0.00;-0.00") + "m from the head, nothing to land on within reach";
                    return true;
                }
                how = "the wall does not end within reach of the top rung";
                return false;
            }
            catch (Exception e) { how = "search threw: " + e.Message; return false; }
        }

        static float _topRefusedAt;

        static void TopOut(Placed l, Transform root, string reason)
        {
            Vector3 edge = _edge, landing = _landing;
            string how = _landingHow;
            bool have = _haveLanding;

            if (!have) have = FindLanding(l, out edge, out landing, out how);

            if (!have)
            {

                Vector3 oedge, odrop; string ohow;
                if (FindOver(l, out oedge, out odrop, out ohow))
                {
                    Log.Msg("ladder: at the top (" + reason + "), over the lip and down  [" + ohow + "]");
                    Log.Fired("ladder-top-over");
                    BeginSelfMantle(l, root, oedge, odrop);
                    return;
                }
                if (Time.realtimeSinceStartup > _topRefusedAt + 2f)
                {
                    _topRefusedAt = Time.realtimeSinceStartup;
                    Ui.Toast("the wall goes on above the ladder");
                    Log.Msg("ladder: at the top (" + reason + ") with no way over: " + ohow + " / " + how);
                    Log.Fired("ladder-top-nothing");
                }
                return;
            }

            Log.Msg("ladder: at the top (" + reason + "), mantling onto " + landing.ToString("0.0")
                    + " over " + edge.ToString("0.0") + "  [" + how + "]");
            Log.Fired("ladder-top-mantle");

            string why = "";
            if (Cfg.LadderUseVault.Value && GameMantle(l, root, edge, landing, out why)) return;
            if (!string.IsNullOrEmpty(why)) Log.Detail("ladder: game mantle not used (" + why + "), doing it myself");
            BeginSelfMantle(l, root, edge, landing);
        }

        static bool _watch;
        static float _watchAt;
        static Transform _watchRoot;
        static Vector3 _watchStart, _watchEdge, _watchLanding;
        static Placed _watchL;

        static bool GameMantle(Placed l, Transform root, Vector3 edge, Vector3 landing, out string why)
        {
            why = "";
            try
            {
                var me = State.Me;
                var mc = me != null && me.Pn != null ? me.Pn.masterController : null;
                var vs = mc != null ? mc.vaultSystem : null;
                if (vs == null) { why = "no vault system on this player"; return false; }
                if (vs.IsVaulting) { StopClimb(); why = ""; return true; }

                Vector3 start = root.position;
                float height = edge.y - start.y;
                float depth = Mathf.Max(0.3f, Vector2.Distance(new Vector2(edge.x, edge.z), new Vector2(landing.x, landing.z)));
                var type = height < 0.6f ? Il2Cpp.PlayerVaultSystem.VaultType.StepOver
                         : height < 1.25f ? Il2Cpp.PlayerVaultSystem.VaultType.Mantle
                         : Il2Cpp.PlayerVaultSystem.VaultType.ClimbUp;

                StopClimb();
                vs.IsVaulting = true;
                var routine = vs.ExecuteMovement(start, edge, landing, -l.Normal, l.Normal, type,
                                                 Mathf.Max(0.3f, height), depth);
                mc.StartCoroutine(routine);

                _vaultRunning = true;
                _vaultUntil = Time.realtimeSinceStartup + 6f;
                _watch = true; _watchAt = Time.realtimeSinceStartup; _watchRoot = root; _watchStart = start;
                _watchL = l; _watchEdge = edge; _watchLanding = landing;
                Log.Msg("ladder: game mantle " + type + ", " + height.ToString("0.00") + "m up, " + depth.ToString("0.00") + "m in");
                Log.Fired("ladder-game-mantle");
                return true;
            }
            catch (Exception e)
            {
                Err.Note("ladder.gamemantle", e);
                why = e.Message;
                try
                {
                    var me = State.Me;
                    var mc = me != null && me.Pn != null ? me.Pn.masterController : null;
                    if (mc != null && mc.vaultSystem != null) mc.vaultSystem.IsVaulting = false;
                }
                catch { }
                return false;
            }
        }

        static bool _mantling;
        static Vector3 _mFrom, _mCtrl, _mTo;
        static float _mT0, _mDur;

        static void BeginSelfMantle(Placed l, Transform root, Vector3 edge, Vector3 landing)
        {
            if (!_climbing && !StartClimb(l)) { Log.Warn("ladder: could not hold the body for the mantle"); return; }
            Vector3 from = root.position;
            Vector3 toward = landing - edge; toward.y = 0f;
            toward = toward.sqrMagnitude > 1e-4f ? toward.normalized : -l.Normal;
            Vector3 mid = new Vector3(edge.x, Mathf.Max(edge.y, landing.y) + 0.15f, edge.z) + toward * 0.15f;
            _mFrom = from; _mTo = landing;
            _mCtrl = mid * 2f - (from + landing) * 0.5f;
            float dist = Vector3.Distance(from, mid) + Vector3.Distance(mid, landing);
            _mDur = Mathf.Clamp(0.2f + dist * 0.4f, 0.35f, 1.4f);
            _mT0 = Time.realtimeSinceStartup;
            _mantling = true;
            _climbUntil = Time.realtimeSinceStartup + 10f;
            Log.Msg("ladder: own mantle, " + dist.ToString("0.00") + "m in " + _mDur.ToString("0.00") + "s");
            Log.Fired("ladder-self-mantle");
        }

        static void PumpMantle()
        {
            if (!_mantling) return;
            Transform root = null;
            try { root = _rb != null ? _rb.transform : null; } catch { }
            if (root == null) { var me = State.Me; root = me != null ? me.Root : null; }
            if (root == null) { _mantling = false; StopClimb(); return; }

            float t = Mathf.Clamp01((Time.realtimeSinceStartup - _mT0) / Mathf.Max(0.05f, _mDur));
            float s = t * t * (3f - 2f * t);
            float u = 1f - s;
            Vector3 p = _mFrom * (u * u) + _mCtrl * (2f * u * s) + _mTo * (s * s);
            Place(root, p);

            if (t >= 1f)
            {
                _mantling = false;
                StopClimb();
                Ui.Toast("over the top");
                Log.Fired("ladder-mantled");
            }
        }

        static bool _vaultRunning;
        static float _vaultUntil;

        static bool TryVaultRoute(Placed l, Transform root)
        {
            try
            {
                var me = State.Me;
                var mc = me != null && me.Pn != null ? me.Pn.masterController : null;
                var vs = mc != null ? mc.vaultSystem : null;
                if (vs == null) return false;
                if (vs.IsVaulting) return true;

                Vector3 edge, landing; string how;
                if (!FindLanding(l, out edge, out landing, out how))
                {
                    Log.Detail("ladder: no landing for a ground vault (" + how + "), climbing the rungs instead");
                    return false;
                }
                Vector3 start = root.position;
                float height = Mathf.Max(0.5f, edge.y - start.y);

                vs.IsVaulting = true;
                var routine = vs.ExecuteMovement(start, edge, landing, -l.Normal, l.Normal,
                                                 Il2Cpp.PlayerVaultSystem.VaultType.ClimbUp, height, 0.6f);
                mc.StartCoroutine(routine);

                _vaultRunning = true;
                _vaultUntil = Time.realtimeSinceStartup + 8f;
                Log.Msg("ladder: vaulting up " + height.ToString("0.0") + "m to " + landing.ToString("0.0"));
                Log.Fired("ladder-vault");
                Ui.Toast("climbing");
                return true;
            }
            catch (Exception e)
            {
                Err.Note("ladder.vault", e);
                try
                {
                    var me = State.Me;
                    var mc = me != null && me.Pn != null ? me.Pn.masterController : null;
                    if (mc != null && mc.vaultSystem != null) mc.vaultSystem.IsVaulting = false;
                }
                catch { }
                return false;
            }
        }

        static void VaultWatchdog()
        {
            if (_watch && Time.realtimeSinceStartup > _watchAt + 0.5f)
            {
                _watch = false;
                try
                {
                    bool moved = _watchRoot != null && Vector3.Distance(_watchRoot.position, _watchStart) > 0.2f;
                    if (!moved && _watchL != null && _watchL.Go != null)
                    {
                        Log.Warn("ladder: the game's mantle did not move the body, doing it myself");
                        Log.Fired("ladder-game-mantle-bailed");
                        try
                        {
                            var me = State.Me;
                            var mc = me != null && me.Pn != null ? me.Pn.masterController : null;
                            if (mc != null && mc.vaultSystem != null) mc.vaultSystem.IsVaulting = false;
                        }
                        catch { }
                        _vaultRunning = false;
                        BeginSelfMantle(_watchL, _watchRoot, _watchEdge, _watchLanding);
                    }
                }
                catch (Exception e) { Err.Note("ladder.watch", e); }
            }

            if (!_vaultRunning) return;
            try
            {
                var me = State.Me;
                var mc = me != null && me.Pn != null ? me.Pn.masterController : null;
                var vs = mc != null ? mc.vaultSystem : null;
                bool over = vs == null || !vs.IsVaulting;
                if (!over && Time.realtimeSinceStartup > _vaultUntil)
                {
                    Log.Warn("ladder: vault did not finish in time, releasing the player");
                    try { vs.IsVaulting = false; } catch { }
                    over = true;
                }
                if (over) { _vaultRunning = false; Log.Fired("ladder-vault-done"); }
            }
            catch { _vaultRunning = false; }
        }

        static bool StartClimb(Placed l)
        {
            try
            {
                var me = State.Me;
                var mc = me != null && me.Pn != null ? me.Pn.masterController : null;
                if (mc == null) return false;

                _walker = mc.walkerController;
                var vs = mc.vaultSystem;
                _mover = vs != null ? vs.mover : null;
                _rb = vs != null ? vs.rb : null;
                if (_rb == null && _mover != null) { try { _rb = _mover.GetRigidBody(); } catch { } }

                if (_walker != null) { _wasWalker = _walker.enabled; _walker.enabled = false; }
                if (_mover != null) { _wasMover = _mover.enabled; _mover.enabled = false; }
                if (_rb != null)
                {
                    _wasKinematic = _rb.isKinematic;

                    try { if (!_rb.isKinematic) _rb.linearVelocity = Vector3.zero; } catch { }
                    _rb.isKinematic = true;
                }

                _climbing = true;
                _onLadder = l;
                _climbUntil = Time.realtimeSinceStartup + 60f;

                _haveLanding = FindLanding(l, out _edge, out _landing, out _landingHow);
                Log.Msg(_haveLanding ? "climbing the ladder; landing: " + _landingHow
                                     : "climbing the ladder; no landing found yet: " + _landingHow);
                Log.Fired("ladder-climb-start");
                return true;
            }
            catch (Exception e) { Err.Note("ladder.climbstart", e); StopClimb(); return false; }
        }

        internal static void StopClimb()
        {
            _mantling = false;
            _haveLanding = false;
            _atTop = false; _topHold = -1f; _lat = 0f;
            if (!_climbing && _walker == null && _mover == null && _rb == null) return;
            try
            {
                if (_rb != null)
                {
                    _rb.isKinematic = _wasKinematic;

                    if (!_wasKinematic) _rb.linearVelocity = Vector3.zero;
                }
            }
            catch { }
            try { if (_mover != null) _mover.enabled = _wasMover; } catch { }
            try { if (_walker != null) _walker.enabled = _wasWalker; } catch { }
            if (_climbing) Log.Detail("off the ladder");
            _climbing = false;
            _onLadder = null;
            _walker = null; _mover = null; _rb = null;
        }

        static void Announce(Vector3 b, Vector3 t, Vector3 n)
        {
            try
            {
                _seq++;
                string ev = _seq.ToString() + "@" + Net.F(b.x) + "@" + Net.F(b.y) + "@" + Net.F(b.z)
                          + "@" + Net.F(t.x) + "@" + Net.F(t.y) + "@" + Net.F(t.z)
                          + "@" + Net.F(n.x) + "@" + Net.F(n.z);
                _recent.Insert(0, ev);
                while (_recent.Count > 4) _recent.RemoveAt(_recent.Count - 1);
                _seen.Add(Net.Self + ":" + _seq);
                Net.SetSelf(Net.K_LADDER, string.Join("|", _recent.ToArray()));
            }
            catch (Exception e) { Err.Note("ladder.announce", e); }
        }

        internal static void Poll()
        {
            if (!Cfg.LadderEnabled.Value || !State.Enabled) return;
            if (Time.realtimeSinceStartup < _nextPoll) return;
            _nextPoll = Time.realtimeSinceStartup + 0.4f;

            try
            {
                var members = Net.Members();
                for (int i = 0; i < members.Count; i++)
                {
                    ulong from = members[i];
                    if (from == Net.Self) continue;
                    string raw = Net.Get(from, Net.K_LADDER);
                    if (string.IsNullOrEmpty(raw)) continue;

                    var evs = raw.Split('|');
                    for (int e = evs.Length - 1; e >= 0; e--)
                    {
                        var f = evs[e].Split('@');
                        if (f.Length < 9) continue;
                        int seq = Net.Int(f[0]);
                        if (seq == 0) continue;
                        if (!_seen.Add(from + ":" + seq)) continue;

                        var b = new Vector3(Net.Float(f[1]), Net.Float(f[2]), Net.Float(f[3]));
                        var t = new Vector3(Net.Float(f[4]), Net.Float(f[5]), Net.Float(f[6]));
                        var n = new Vector3(Net.Float(f[7]), 0f, Net.Float(f[8]));
                        if (n.sqrMagnitude < 1e-4f) continue;
                        Log.Msg("ladder arrived from " + State.NameOf(from));
                        Log.Fired("ladder-remote");
                        Spawn(b, t, n.normalized, from);
                    }
                }
            }
            catch (Exception ex) { Err.Note("ladder.poll", ex); }
        }

        internal static void Reset(string why)
        {
            StopClimb();
            _nearLadder = false;
            for (int i = _live.Count - 1; i >= 0; i--) Remove(i);
            _seen.Clear();
            _recent.Clear();
            _armed = false;
            _assistOn = false;
            _stockMaxHeight = -1f;
            _stockDetect = -1f;
            _vaultRunning = false;
            Log.Detail("ladders cleared (" + why + ")");
        }

        static Camera Cam()
        {
            try
            {
                var m = Camera.main;
                if (m != null) return m;
                var all = Camera.allCameras;
                Camera best = null;
                if (all != null)
                    for (int i = 0; i < all.Length; i++)
                        if (all[i] != null && all[i].enabled && (best == null || all[i].depth > best.depth))
                            best = all[i];
                return best;
            }
            catch { return null; }
        }
    }
}
