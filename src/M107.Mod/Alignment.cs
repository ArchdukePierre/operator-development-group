// ODG Operations - M107
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace M107
{

    internal static class Alignment
    {

        internal static Vector3 HeldPos = new Vector3(0f, 0.045f, 0.685f);
        internal static Vector3 HeldRot = Vector3.zero;
        internal static float HeldScale = 1f;
        internal static Vector3 DispPos, DispRot;
        internal static float DispScale = 1f;

        internal static bool Loaded;
        static bool _seeded;
        static float _nextCheck;
        static DateTime _stamp;

        static readonly List<Transform> _held = new List<Transform>();
        static readonly List<Transform> _disp = new List<Transform>();

        static readonly List<KeyValuePair<Transform, Transform>> _partArt =
            new List<KeyValuePair<Transform, Transform>>();

        class AdsNode
        {
            internal Transform T; internal Vector3 Home; internal bool Authored;
            internal Settle St = new Settle();

            internal GameObject Root;
            internal bool OnBench; internal int BenchCheckedFrame;
        }

        static bool BenchNow(AdsNode a)
        {
            int f = Time.frameCount;
            if (f - a.BenchCheckedFrame < 12) return a.OnBench;
            a.BenchCheckedFrame = f;
            a.OnBench = IsBenchCopy(a.T);
            return a.OnBench;
        }
        static readonly List<AdsNode> _ads = new List<AdsNode>();

        internal static void ProbeAnchors(List<Transform> into, List<string> names)
        {
            for (int i = 0; i < _ads.Count; i++)
            {
                var a = _ads[i];
                if (a == null || a.T == null) continue;
                into.Add(a.T);
                string n; try { n = a.T.name; } catch { n = "?"; }
                names.Add((a.Authored ? "authored:" : "game:") + n);
            }
        }
        internal static void ProbeCams(List<Transform> into)
        {
            for (int i = 0; i < _scopeCams.Count; i++) if (_scopeCams[i] != null) into.Add(_scopeCams[i]);
        }
        static readonly string[] AdsNames = { "ADS", "ADSZ", "real ads" };

        static readonly Dictionary<int, Vector3> _adsHome = new Dictionary<int, Vector3>();

        static readonly Dictionary<string, Vector3> _adsTemplateHome =
            new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);

        internal static void LearnTemplateAnchors(GameObject template)
        {
            try
            {
                if (template == null) return;
                var all = template.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    var t = all[i]; if (t == null || t.parent == null) continue;
                    string nm, pn;
                    try { nm = t.name; pn = t.parent.name; } catch { continue; }
                    if (pn != "RIG_Parent") continue;
                    bool match = false;
                    foreach (var k in AdsNames)
                        if (string.Equals(nm, k, StringComparison.OrdinalIgnoreCase)) { match = true; break; }
                    if (!match || _adsTemplateHome.ContainsKey(nm)) continue;
                    _adsTemplateHome[nm] = t.localPosition;
                    M107Mod.Log("ads: template anchor '" + nm + "' authored at " + BarrettArt.F(t.localPosition));
                }
                M107Mod.Log("ads: learned " + _adsTemplateHome.Count + " authored anchor pose(s) from the template");
            }
            catch (Exception e) { M107Mod.Log("ads: template learn err: " + e.Message); }
        }

        internal static Vector3 RailPos = new Vector3(0f, 0.022f, 0f);
        internal static Vector3 RailRot = Vector3.zero;
        static float _railCheck;
        class RailEntry
        {
            internal Il2Cpp.WeaponModParent Socket;
            internal Transform SocketT;
            internal Vector3 Home;
            internal bool HasHome;
            internal string LastReport;
        }
        static readonly List<RailEntry> _rails = new List<RailEntry>();

        static readonly HashSet<int> _scopeCamQuiet = new HashSet<int>();

        internal static bool UnderTemplate(Transform t)
        {
            int g = 0;
            while (t != null && g++ < 40)
            {
                string n; try { n = t.name; } catch { return false; }
                if (n != null && n.StartsWith("M107_TemplateHolder", StringComparison.Ordinal)) return true;
                t = t.parent;
            }
            return false;
        }

        internal static bool IsBenchCopy(Transform t)
        {
            int g = 0;
            while (t != null && g++ < 40)
            {
                string n; try { n = t.name; } catch { return false; }
                if (n != null && n.StartsWith("WeaponModding", StringComparison.Ordinal)) return true;
                t = t.parent;
            }
            return false;
        }

        internal static void RegisterRailSocket(Il2Cpp.WeaponModParent socket)
        {
            if (socket == null) return;
            Transform st = null;
            try { st = socket.transform; } catch { return; }
            if (IsBenchCopy(st)) return;
            _rails.RemoveAll(p => p.SocketT == null);
            foreach (var p in _rails) if (p.SocketT == st) return;

            var e = new RailEntry { Socket = socket, SocketT = st };
            try { e.Home = st.localPosition; e.HasHome = true; } catch { }
            _rails.Add(e);
        }

        internal static void InvalidateRailChain() { }

        internal static void InvalidateAds()
        {
        }

        static readonly List<Transform> _scopeCams = new List<Transform>();
        static readonly List<Settle> _scopeCamSettle = new List<Settle>();
        static Transform _scopeCamOwner;

        static void ScopeCamerasFollow(Transform socket, bool report)
        {
            try
            {
                Transform weapon = socket; int g = 0;
                while (weapon != null && g++ < 40 &&
                       !(weapon.name != null && weapon.name.StartsWith("M107A1", StringComparison.Ordinal)))
                    weapon = weapon.parent;
                if (weapon == null) return;

                if (_scopeCamOwner != weapon) { _scopeCamOwner = weapon; _scopeCams.Clear(); _scopeCamSettle.Clear(); }

                var fx = weapon.GetComponentsInChildren<Il2Cpp.RealisticScopeEffectV2>(true);
                if (fx == null) return;

                for (int i = 0; i < fx.Length; i++)
                {
                    var f = fx[i]; if (f == null) continue;
                    for (int k = 0; k < 2; k++)
                    {
                        Camera cam = null;
                        try { cam = k == 0 ? f.ScopeCamera : f.NVGScopeCamera; } catch { }
                        if (cam == null) continue;
                        Transform ct = null; try { ct = cam.transform; } catch { }
                        if (ct == null) continue;
                        if (IsBenchCopy(ct)) continue;

                        if (ct.IsChildOf(socket))
                        {

                            if (k == 0)
                            {
                                int cid = 0; try { cid = ct.GetInstanceID(); } catch { }
                                if (_scopeCamQuiet.Add(cid))
                                    M107Mod.Log("scope cam: '" + ct.name + "' is under the socket, follows on its own");
                            }
                            continue;
                        }

                        int idx = _scopeCams.IndexOf(ct);
                        if (idx < 0)
                        {
                            _scopeCams.Add(ct); _scopeCamSettle.Add(new Settle()); idx = _scopeCams.Count - 1;
                            M107Mod.Log("scope cam: '" + ct.name + "' sits outside the socket at " +
                                        BarrettArt.Path(ct) + " - lifted with the glass once it settles");
                        }

                        var p = ct.parent; if (p == null) continue;
                        Vector3 worldDelta = socket.parent != null
                            ? socket.parent.TransformVector(RailPos)
                            : RailPos;
                        Vector3 camLift = p.InverseTransformVector(worldDelta);
                        Vector3 want2;
                        var st2 = _scopeCamSettle[idx];
                        Vector3 camLive = ct.localPosition;

                        LiftWhenSettled(st2, camLive, camLift, out want2);
                        if (Time.unscaledTime < _adsDiagUntil && _adsDiagPassAt == Time.unscaledTime)
                            M107Mod.Log("cam diag: " + ct.name + " live=" + BarrettArt.F(camLive) + " origin=" +
                                        (st2.HasOrigin ? BarrettArt.F(st2.Origin) : "-") + " still=" + st2.Still +
                                        " want=" + BarrettArt.F(want2));
                    }
                }
            }
            catch (Exception ex) { M107Mod.Log("scope cam err: " + ex.Message); }
        }

        static void PlaceRails()
        {
            long _p = Perf.Begin();
            try { PlaceRailsInner(); } finally { Perf.End(Perf.Rails, _p); }
        }

        static void PlaceRailsInner()
        {
            try
            {
                _rails.RemoveAll(p => p.SocketT == null);
                if (_rails.Count == 0) return;

                bool report = Time.unscaledTime - _railCheck > 2f;
                if (report) _railCheck = Time.unscaledTime;

                foreach (var e in _rails)
                {
                    var s = e.SocketT; if (s == null) continue;
                    if (!e.HasHome) { try { e.Home = s.localPosition; e.HasHome = true; } catch { continue; } }

                    Vector3 want = e.Home + RailPos;
                    if ((s.localPosition - want).sqrMagnitude > 1e-10f) { s.localPosition = want; try { AdsProbe.Wrote("rail:" + s.name); } catch { } }
                    if (RailRot != Vector3.zero) s.localRotation = Quaternion.Euler(RailRot);

                    ScopeCamerasFollow(s, report);

                    if (report)
                    {
                        string held = "-";
                        try { var am = e.Socket.AttachedMod; if (am != null) held = am.name; } catch { }
                        string now = held + "|" + s.childCount + "|" + BarrettArt.F(s.localPosition);
                        if (e.LastReport != now)
                        {
                            e.LastReport = now;
                            M107Mod.Log("rail: '" + s.name + "' home " + BarrettArt.F(e.Home) +
                                        " + offset " + BarrettArt.F(RailPos) +
                                        " -> " + BarrettArt.F(s.localPosition) +
                                        "   holding '" + held + "'  children=" + s.childCount);
                        }
                    }
                }
            }
            catch { }
        }

        internal static int RegisterAdsAnchors(GameObject weapon)
        {
            int n = 0;
            try
            {
                var all = weapon.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    var t = all[i]; if (t == null || t.parent == null) continue;
                    string nm, pn;
                    try { nm = t.name; pn = t.parent.name; } catch { continue; }
                    if (pn != "RIG_Parent") continue;
                    if (IsBenchCopy(t)) continue;
                    bool match = false;
                    foreach (var k in AdsNames)
                        if (string.Equals(nm, k, StringComparison.OrdinalIgnoreCase)) { match = true; break; }
                    if (!match) continue;

                    bool known = false;
                    foreach (var a in _ads) if (a.T == t) { known = true; break; }
                    if (known) continue;

                    if (UnderTemplate(t)) continue;

                    int iid; try { iid = t.GetInstanceID(); } catch { continue; }
                    bool onTemplate = _adsTemplateHome.TryGetValue(nm, out var authored);
                    Vector3 home = onTemplate ? authored : t.localPosition;

                    if (!_adsHome.ContainsKey(iid))
                    {
                        if (_adsHome.Count > 256) _adsHome.Clear();
                        _adsHome[iid] = home;
                        M107Mod.Log(onTemplate
                            ? "ads: anchor '" + nm + "' authored " + BarrettArt.F(authored) + " - ours to place"
                            : "ads: anchor '" + nm + "' is not on the template, the game places it (now " +
                              BarrettArt.F(t.localPosition) + ") - left alone");
                    }

                    _ads.Add(new AdsNode { T = t, Home = home, Authored = onTemplate, Root = weapon });
                    AdsDiagArm();
                    n++;
                }

                int nested = DropNestedAnchors();
                if (nested > 0) M107Mod.Log("ads: dropped " + nested + " nested anchor(s) - a child would have taken the trim twice");
                if (n > 0)
                {
                    M107Mod.Log("ads: " + _ads.Count + " anchor(s) held, trim=" +
                                BarrettArt.F(ScopeFitted ? AdsScopeTrim : AdsTrim) +
                                (ScopeFitted ? "  (scope)" : "  (irons)"));
                    PlaceAds();
                }
            }
            catch (Exception e) { M107Mod.Log("ads: scan err: " + e.Message); }
            return n;
        }

        static int DropNestedAnchors()
        {
            var keep = new List<AdsNode>(_ads.Count);
            int dropped = 0;
            for (int i = 0; i < _ads.Count; i++)
            {
                var a = _ads[i];
                bool under = false;
                try
                {
                    for (int k = 0; k < _ads.Count && !under; k++)
                    {
                        var b = _ads[k];
                        if (k == i || b.T == null || a.T == null) continue;
                        var p = a.T.parent; int g = 0;
                        while (p != null && g++ < 40) { if (p == b.T) { under = true; break; } p = p.parent; }
                    }
                }
                catch { }
                if (under) { dropped++; continue; }
                keep.Add(a);
            }
            if (dropped > 0) { _ads.Clear(); _ads.AddRange(keep); }
            return dropped;
        }

        const float EyeBurstHold = 1.0f;
        static float _eyeBurstAt;
        static bool _eyeBurst, _haveEyeEntry;
        static Vector3 _eyeEntry;
        static int _eyeRefId;

        internal static void EyeSliderMoved()
        {

            if (!_eyeBurst) { M107Mod.Log("ads: eye slider in use - standing off the aim anchors"); AdsProbe.Event("eye-slider"); }
            _eyeBurstAt = Time.unscaledTime;
            _eyeBurst = true;
        }

        static Transform EyeRef()
        {
            for (int i = 0; i < _ads.Count; i++)
            {
                var a = _ads[i];
                if (a.T == null || a.Authored) continue;
                return a.T;
            }
            return null;
        }

        static bool EyeToolHasTheAnchors()
        {
            if (!_eyeBurst) return false;
            float since = Time.unscaledTime - _eyeBurstAt;
            Transform r = null;
            try { r = EyeRef(); } catch { }

            if (since < EyeBurstHold)
            {

                if (!_haveEyeEntry && since > 0.05f && r != null)
                {
                    try { _eyeEntry = r.localPosition; _eyeRefId = r.GetInstanceID(); _haveEyeEntry = true; }
                    catch { }
                }
                return true;
            }

            _eyeBurst = false;
            if (_haveEyeEntry && r != null)
            {
                try
                {

                    if (r.GetInstanceID() == _eyeRefId)
                    {

                        var moved = r.localPosition - _eyeEntry;
                        M107Mod.Log("ads: eye slider moved the anchor " + BarrettArt.F(moved) +
                                    " - it will settle and be lifted from there");
                    }
                    else M107Mod.Log("ads: the rifle was rebuilt mid-drag, eye slider change discarded");
                }
                catch { }
            }
            _haveEyeEntry = false;
            return false;
        }

        static void PlaceAds()
        {
            try
            {
                _ads.RemoveAll(a => a.T == null || a.Root == null);
                if (_ads.Count == 0) return;

                if (EyeToolHasTheAnchors()) return;

                var anchor = AdsMove ? ReferenceSpace(true) : null;
                if (AdsMove && anchor == null) return;

                foreach (var a in _ads)
                {

                    if (BenchNow(a)) continue;

                    var p = a.T.parent; if (p == null) continue;

                    Vector3 lift = ScopeFitted ? new Vector3(0f, RailPos.y, 0f) : Vector3.zero;

                    if (!a.Authored)
                    {

                        Vector3 live = a.T.localPosition;
                        Vector3 want;
                        bool write = LiftWhenSettled(a.St, live, lift, out want);

                        string anm = null; try { anm = a.T.name; } catch { }
                        if (anm == "real ads") write = false;
                        if (write) { a.T.localPosition = want; a.St.LastSeen = want; AdsProbe.Wrote(anm ?? "anchor"); }
                        AdsDiag(a, live, want, lift);
                        continue;
                    }

                    Vector3 liveZ = a.T.localPosition;
                    Vector3 wantZ;
                    bool writeZ = LiftWhenSettled(a.St, liveZ, lift, out wantZ);
                    if (writeZ) { a.T.localPosition = wantZ; a.St.LastSeen = wantZ; try { AdsProbe.Wrote(a.T.name); } catch { } }
                    AdsDiag(a, liveZ, wantZ, lift);
                }
            }
            catch { }
        }

        internal class Settle
        {
            internal Vector3 LastSeen; internal bool HasLast;
            internal int Still;
            internal Vector3 Origin; internal bool HasOrigin;
            internal Vector3 PrevOrigin; internal bool HadOrigin;
            internal Vector3 LastWrite; internal bool HasLastWrite;
        }

        const int SettleFrames = 30;

        const float SettleEps = 0.002f;

        static float _refuseLogAt = -100f;
        static bool LiftWhenSettled(Settle st, Vector3 live, Vector3 lift, out Vector3 want)
        {
            want = live;
            if (st.HasLast && (live - st.LastSeen).sqrMagnitude > SettleEps * SettleEps)
            { st.Still = 0; st.HasOrigin = false; }
            else st.Still++;
            st.LastSeen = live; st.HasLast = true;
            if (st.Still < SettleFrames) return false;
            if (!st.HasOrigin)
            {

                bool ours = st.HasLastWrite && (live - st.LastWrite).sqrMagnitude <= SettleEps * SettleEps;

                const float near = 0.003f;
                bool atPrevLift = st.HadOrigin && (live - (st.PrevOrigin + lift)).sqrMagnitude < near * near;
                bool atPrevOrigin = st.HadOrigin && (live - st.PrevOrigin).sqrMagnitude < near * near;
                if (atPrevLift || atPrevOrigin) st.Origin = st.PrevOrigin;
                else st.Origin = ours ? live - lift : live;
                st.HasOrigin = true; st.PrevOrigin = st.Origin; st.HadOrigin = true;
                if ((atPrevLift || atPrevOrigin) && Time.unscaledTime - _refuseLogAt > 10f)
                { _refuseLogAt = Time.unscaledTime; M107Mod.Log("ads: settle landed on the lift itself - origin kept, no second lift"); }
            }
            want = st.Origin + lift;
            bool write = (want - live).sqrMagnitude > 1e-12f;
            if (write) { st.LastWrite = want; st.HasLastWrite = true; }
            return write;
        }

        static float _adsDiagUntil, _adsDiagNext;
        internal static void AdsDiagArm() { _adsDiagUntil = Time.unscaledTime + 20f; _adsDiagNext = 0f; }
        static float _adsDiagPassAt = -1f;
        static void AdsDiag(AdsNode a, Vector3 live, Vector3 want, Vector3 lift)
        {
            try
            {
                float now = Time.unscaledTime;
                if (now > _adsDiagUntil) return;

                if (now >= _adsDiagNext) { _adsDiagNext = now + 1f; _adsDiagPassAt = now; }
                if (_adsDiagPassAt != now) return;
                string nm; try { nm = a.T.name; } catch { return; }
                M107Mod.Log("ads diag: '" + nm + "' live=" + BarrettArt.F(live) +
                            " origin=" + (a.St.HasOrigin ? BarrettArt.F(a.St.Origin) : "-") +
                            " still=" + a.St.Still + " want=" + BarrettArt.F(want) +
                            " lift.y=" + lift.y.ToString("F3") + (ScopeFitted ? " scope" : " irons") +
                            (a.Authored ? " [authored]" : ""));
            }
            catch { }
        }

        internal static void RegisterPartArt(Transform art, Transform anchor)
        {
            if (art == null || anchor == null) return;
            _partArt.RemoveAll(p => p.Key == null || p.Value == null);
            foreach (var p in _partArt)
                if (p.Key == art) { PlaceWorld(art, p.Value); return; }
            _partArt.Add(new KeyValuePair<Transform, Transform>(art, anchor));
            PlaceWorld(art, anchor);
        }

        static void PlaceWorld(Transform art, Transform anchor)
        {
            try
            {
                art.position = anchor.TransformPoint(HeldPos);
                art.rotation = anchor.rotation * Quaternion.Euler(HeldRot);
                art.localScale = new Vector3(HeldScale, HeldScale, HeldScale);
            }
            catch { }
        }

        static string GameRoot
        {
            get { try { return MelonLoader.Utils.MelonEnvironment.GameRootDirectory; } catch { return AppContext.BaseDirectory; } }
        }
        static string Dir => Path.Combine(GameRoot, "UserData");
        static string CfgPath => Path.Combine(Dir, "M107_alignment.cfg");

        internal static void ResetForScene()
        {
            _held.Clear();
            _disp.Clear();
            _partArt.Clear();
            _ads.Clear();
            _rails.Clear();
            _scopeCams.Clear();
            _scopeCamQuiet.Clear();
            _scopeCamSettle.Clear();
            _scopeCamOwner = null;
            _wasAds = false;
        }

        internal static void Register(Transform t, bool isDisplay)
        {
            if (t == null) return;
            var list = isDisplay ? _disp : _held;
            list.RemoveAll(x => x == null);
            if (!list.Contains(t)) list.Add(t);
            ApplyTo(t, isDisplay);
        }

        internal static void SeedHeld(Transform art)
        {
            if (_seeded) return;
            _seeded = true;
            try
            {
                if (File.Exists(CfgPath)) { Load(true); return; }

                Loaded = true;
                M107Mod.Log("alignment: using tuned defaults " + BarrettArt.F(HeldPos) +
                            " (auto-align suggested " + (art == null ? "n/a" : BarrettArt.F(art.localPosition)) + ")");
            }
            catch (Exception e) { M107Mod.Log("alignment seed err: " + e.Message); }
        }

        internal static void SeedDisplay(Transform art)
        {
            try
            {
                if (_dispSeeded) return;
                _dispSeeded = true;
                if (File.Exists(CfgPath)) return;
                if (art == null) return;
                DispPos = art.localPosition;
                DispRot = art.localEulerAngles;
                DispScale = art.localScale.x;
                Save("seeded from automatic placement");
            }
            catch (Exception e) { M107Mod.Log("alignment display seed err: " + e.Message); }
        }
        static bool _dispSeeded;

        static bool _wasAds;
        internal static void ReportAds(Il2Cpp.PlayerNetworking pn)
        {
            try
            {
                if (pn == null) return;

                bool ads = false;
                try { ads = pn.isADS; } catch { }
                if (!ads) { try { ads = pn.NetworkisADS; } catch { } }
                if (ads == _wasAds) return;
                _wasAds = ads;
                if (!ads) { M107Mod.Log("aim: released"); return; }

                var sb = new System.Text.StringBuilder();
                sb.Append("aim: ENGAGED (game reports isADS=true)  anchors=").Append(_ads.Count)
                  .Append("  displacement=").Append(AdsMove ? "on" : "OFF (ads.move=0)").Append('\n');
                var cam = Camera.main;
                foreach (var a in _ads)
                {
                    if (a.T == null) continue;
                    sb.Append("   '").Append(a.T.name).Append("' world ").Append(BarrettArt.F(a.T.position));
                    if (cam != null) sb.Append("  camera is ").Append(Vector3.Distance(a.T.position, cam.transform.position).ToString("F3")).Append("m away");
                    sb.Append("  (home ").Append(BarrettArt.F(a.Home)).Append(" -> now ").Append(BarrettArt.F(a.T.localPosition)).Append(")\n");
                }
                if (cam != null) sb.Append("   camera at ").Append(BarrettArt.F(cam.transform.position));
                M107Mod.Log(sb.ToString());
            }
            catch { }
        }

        internal static void LateTick()
        {
            try { if (_rails.Count > 0) PlaceRails(); } catch { }

            try { if (_ads.Count > 0) PlaceAds(); } catch { }
        }

        internal static void Tick()
        {
            try
            {
                Keys();

                if (Time.unscaledTime < _nextCheck) return;
                _nextCheck = Time.unscaledTime + 0.5f;
                if (!File.Exists(CfgPath)) return;
                var st = File.GetLastWriteTimeUtc(CfgPath);
                if (st == _stamp) return;
                _stamp = st;
                Load(false);
            }
            catch { }
        }

        static readonly string[] Fields =
        { "held.x", "held.y", "held.z", "held.pitch", "held.yaw", "held.roll", "held.scale",
          "disp.x", "disp.y", "disp.z", "disp.pitch", "disp.yaw", "disp.roll", "disp.scale" };

        internal static Vector3 AdsTrim = Vector3.zero;

        internal static Vector3 AdsScopeTrim = new Vector3(-0.0022f, 0.1556f, 0.1816f);
        internal static bool ScopeFitted;

        internal static bool AdsMove = false;

        static ref Vector3 LiveAds() { if (ScopeFitted) return ref AdsScopeTrim; return ref AdsTrim; }

        const float MoveRate = 0.30f;
        const float TapStep = 0.010f;
        const float FineRate = 0.035f;
        const float FineTap = 0.001f;
        const float RotRate = 60f;
        const float ScaleRate = 0.35f;
        static float _announce;

        static bool HoldingOurs()
        {
            _held.RemoveAll(x => x == null);
            _partArt.RemoveAll(p => p.Key == null || p.Value == null);
            foreach (var t in _held)
                try { if (t.gameObject.activeInHierarchy) return true; } catch { }
            foreach (var p in _partArt)
                try { if (p.Key.gameObject.activeInHierarchy) return true; } catch { }
            return false;
        }

        static Transform ReferenceSpace(bool held)
        {
            try
            {
                if (held)
                {
                    foreach (var p in _partArt)
                        if (p.Key != null && p.Value != null && p.Key.gameObject.activeInHierarchy) return p.Value;
                    foreach (var t in _held)
                        if (t != null && t.gameObject.activeInHierarchy && t.parent != null) return t.parent;
                }
                else
                {
                    foreach (var t in _disp)
                        if (t != null && t.parent != null) return t.parent;
                }
            }
            catch { }
            return null;
        }

        static readonly HashSet<KeyCode> _reservedKeys = new HashSet<KeyCode>();

        internal static void ReserveKeys(KeyCode up, KeyCode down)
        {
            bool a = up != KeyCode.None && _reservedKeys.Add(up);
            bool b = down != KeyCode.None && _reservedKeys.Add(down);
            if (a || b) M107Mod.Log("keys: " + up + " and " + down + " belong to the scope's zeroing now - placement keys yield to them");
        }

        static bool K(KeyCode k) => !_reservedKeys.Contains(k) && Input.GetKey(k);
        static bool KDown(KeyCode k) => !_reservedKeys.Contains(k) && Input.GetKeyDown(k);

        static void Keys()
        {
            try
            {

                if (M107Mod.PlacementKeys == null || !M107Mod.PlacementKeys.Value) return;

                if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) return;

                float dt = Time.unscaledDeltaTime;
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

                float lr = (K(KeyCode.RightArrow) ? 1f : 0f) - (K(KeyCode.LeftArrow) ? 1f : 0f);
                float ud = (K(KeyCode.UpArrow) ? 1f : 0f) - (K(KeyCode.DownArrow) ? 1f : 0f);
                float fb = (K(KeyCode.PageUp) ? 1f : 0f) - (K(KeyCode.PageDown) ? 1f : 0f);

                if (KDown(KeyCode.Comma) || KDown(KeyCode.Backslash))
                { Save("aim position confirmed"); return; }

                float tap = 0f;
                if (KDown(KeyCode.RightArrow) || KDown(KeyCode.LeftArrow) ||
                    KDown(KeyCode.UpArrow) || KDown(KeyCode.DownArrow) ||
                    KDown(KeyCode.PageUp) || KDown(KeyCode.PageDown)) tap = TapStep;

                if (lr == 0f && ud == 0f && fb == 0f) return;
                if (!HoldingOurs()) return;

                if (!alt)
                {

                    Transform frame2 = null;
                    foreach (var a in _ads) if (a.T != null && a.T.parent != null) { frame2 = a.T.parent; break; }
                    if (frame2 == null) return;

                    float rate = shift ? FineRate : MoveRate;
                    float step = shift ? FineTap : TapStep;
                    float amt = rate * dt + (tap > 0f ? step : 0f);

                    var c2 = Camera.main;
                    Vector3 d = c2 != null
                        ? frame2.InverseTransformVector((c2.transform.right * lr + c2.transform.up * ud + c2.transform.forward * fb) * amt)
                        : new Vector3(lr, ud, fb) * amt;
                    if (ScopeFitted) AdsScopeTrim += d; else AdsTrim += d;
                    PlaceAds();

                    if (Time.unscaledTime - _announce > 0.5f)
                    {
                        _announce = Time.unscaledTime;
                        M107Mod.Log("eye" + (ScopeFitted ? "(scope) " : "(irons) ") +
                                    BarrettArt.F(ScopeFitted ? AdsScopeTrim : AdsTrim) +
                                    (shift ? "  [fine]" : "") + "   (comma to save)");
                    }
                    return;
                }

                if (shift)
                {
                    float step = RotRate * dt + (tap > 0f ? 0.5f : 0f);
                    if (ud != 0f) RailRot.x += ud * step;
                    if (lr != 0f) RailRot.y += lr * step;
                    PlaceRails();
                }
                else
                {

                    Transform frame = null;
                    foreach (var e in _rails)
                        if (e.SocketT != null && e.SocketT.parent != null) { frame = e.SocketT.parent; break; }
                    if (frame == null) return;

                    float amount = MoveRate * dt + tap;
                    var cam = Camera.main;
                    Vector3 local = cam != null
                        ? frame.InverseTransformVector((cam.transform.right * lr + cam.transform.up * ud + cam.transform.forward * fb) * amount)
                        : new Vector3(lr, ud, fb) * amount;
                    RailPos += local;
                    PlaceRails();
                }

                if (Time.unscaledTime - _announce > 0.5f)
                {
                    _announce = Time.unscaledTime;
                    M107Mod.Log("optic offset " + BarrettArt.F(RailPos) + "  cant " + BarrettArt.F(RailRot) +
                                "   (comma to save; use the bench slider for eye relief)");
                }
            }
            catch { }
        }

        static float Get(int i)
        {
            switch (i)
            {
                case 0: return HeldPos.x; case 1: return HeldPos.y; case 2: return HeldPos.z;
                case 3: return HeldRot.x; case 4: return HeldRot.y; case 5: return HeldRot.z;
                case 6: return HeldScale;
                case 7: return DispPos.x; case 8: return DispPos.y; case 9: return DispPos.z;
                case 10: return DispRot.x; case 11: return DispRot.y; case 12: return DispRot.z;
                default: return DispScale;
            }
        }

        static void Set(int i, float v)
        {
            switch (i)
            {
                case 0: HeldPos.x = v; break; case 1: HeldPos.y = v; break; case 2: HeldPos.z = v; break;
                case 3: HeldRot.x = v; break; case 4: HeldRot.y = v; break; case 5: HeldRot.z = v; break;
                case 6: HeldScale = Mathf.Max(0.05f, v); break;
                case 7: DispPos.x = v; break; case 8: DispPos.y = v; break; case 9: DispPos.z = v; break;
                case 10: DispRot.x = v; break; case 11: DispRot.y = v; break; case 12: DispRot.z = v; break;
                default: DispScale = Mathf.Max(0.05f, v); break;
            }
        }

        internal static void ApplyAll()
        {
            _held.RemoveAll(x => x == null);
            _disp.RemoveAll(x => x == null);
            _partArt.RemoveAll(p => p.Key == null || p.Value == null);
            foreach (var t in _held) ApplyTo(t, false);
            foreach (var t in _disp) ApplyTo(t, true);
            foreach (var p in _partArt) PlaceWorld(p.Key, p.Value);
            PlaceAds();
            PlaceRails();
        }

        static void ApplyTo(Transform t, bool isDisplay)
        {
            if (t == null) return;
            try
            {
                t.localPosition = isDisplay ? DispPos : HeldPos;
                t.localRotation = Quaternion.Euler(isDisplay ? DispRot : HeldRot);
                float s = isDisplay ? DispScale : HeldScale;
                t.localScale = new Vector3(s, s, s);
            }
            catch { }
        }

        const int CfgVersion = 2;

        static void Load(bool quiet)
        {
            try
            {
                var lines = File.ReadAllLines(CfgPath);
                int fileVersion = 0;
                foreach (var raw in lines)
                {
                    var t = raw.Trim();
                    if (t.StartsWith("version", StringComparison.OrdinalIgnoreCase))
                    {
                        int eq0 = t.IndexOf('=');
                        if (eq0 > 0)
                        {
                            var num = t.Substring(eq0 + 1).Trim();
                            int hash = num.IndexOf('#'); if (hash >= 0) num = num.Substring(0, hash).Trim();
                            int.TryParse(num, out fileVersion);
                        }
                    }
                }
                bool stale = fileVersion < CfgVersion;
                if (stale) M107Mod.Log("alignment: file predates v" + CfgVersion + " - its eye and rail values are ignored "
                                     + "(a stray keypress could have saved them) and the shipped ones stand");

                foreach (var raw in lines)
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                    if (k == "version") continue;
                    if (!float.TryParse(line.Substring(eq + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) continue;
                    if (stale && (k.StartsWith("ads") || k.StartsWith("rail"))) continue;
                    switch (k)
                    {
                        case "ads.x": AdsTrim.x = v; continue;
                        case "ads.y": AdsTrim.y = v; continue;
                        case "ads.z": AdsTrim.z = v; continue;
                        case "ads.move": AdsMove = v > 0.5f; InvalidateAds(); continue;
                        case "adsscope.x": AdsScopeTrim.x = v; continue;
                        case "adsscope.y": AdsScopeTrim.y = v; continue;
                        case "adsscope.z": AdsScopeTrim.z = v; continue;
                        case "rail.x": RailPos.x = v; continue;
                        case "rail.y": RailPos.y = v; continue;
                        case "rail.z": RailPos.z = v; continue;
                        case "rail.pitch": RailRot.x = v; continue;
                        case "rail.yaw": RailRot.y = v; continue;
                        case "rail.roll": RailRot.z = v; continue;
                    }
                    for (int i = 0; i < Fields.Length; i++)
                        if (Fields[i] == k) { Set(i, v); break; }
                }
                Loaded = true;

                ApplyAll();
                _stamp = File.GetLastWriteTimeUtc(CfgPath);
                if (stale) Save("stamped v" + CfgVersion + ", eye and rail values reset to shipped");
                if (!quiet) M107Mod.Log("alignment reloaded from file: held pos " + BarrettArt.F(HeldPos) +
                                        " rot " + BarrettArt.F(HeldRot) + " scale " + HeldScale.ToString("F3"));
            }
            catch (Exception e) { M107Mod.Log("alignment load err: " + e.Message); }
        }

        internal static void Save(string why)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("# Barrett M107A1 art placement. Edit and save - the game picks it up within half a second.");
                sb.AppendLine("version = " + CfgVersion + "   # do not edit; a file without this line has its eye and rail values ignored");
                sb.AppendLine("# held.*  = the rifle in your hands.   disp.* = the one hanging on the rack.");
                sb.AppendLine("# In game the keys move the OPTIC RAIL, which carries the mount and scope:");
                sb.AppendLine("#   LEFT/RIGHT          shift your eye left and right");
                sb.AppendLine("#   UP/DOWN             raise and lower it");
                sb.AppendLine("#   PAGE UP/PAGE DOWN   towards you / away");
                sb.AppendLine("#   SHIFT + those       the same, but fine - 1mm a tap instead of 10mm");
                sb.AppendLine("#   ALT + those         move the OPTIC instead (already set at 22mm up)");
                sb.AppendLine("#   ALT + SHIFT + U/D   cant the optic, for zeroing");
                sb.AppendLine("#   COMMA               save     PERIOD  dump renderers to the log");
                sb.AppendLine("# Hold aim while setting the eye, or there is nothing to judge against.");
                sb.AppendLine("# A tap moves 1cm; holding sweeps. Relative to your view.");
                sb.AppendLine("# " + why + "  (" + DateTime.Now + ")");
                sb.AppendLine();
                for (int i = 0; i < Fields.Length; i++)
                {
                    sb.AppendLine(Fields[i] + " = " + Get(i).ToString("F4", CultureInfo.InvariantCulture));
                    if (i == 6) sb.AppendLine();
                }
                sb.AppendLine();
                sb.AppendLine("# ads.move = 0 leaves the aim anchors exactly where the donor had them.");
                sb.AppendLine("# Try that if aiming does nothing at all - it tells us whether moving");
                sb.AppendLine("# them is what breaks it. Takes effect within half a second of saving.");
                sb.AppendLine("ads.move = " + (AdsMove ? "1" : "0"));
                sb.AppendLine();
                sb.AppendLine("# Aim position, set in game with the arrows and PageUp/PageDown and");
                sb.AppendLine("# confirmed with comma. Two of them: whichever the rifle is currently");
                sb.AppendLine("# built with is the one the keys edit.");
                sb.AppendLine("ads.x = " + AdsTrim.x.ToString("F4", CultureInfo.InvariantCulture) + "        # over iron sights");
                sb.AppendLine("ads.y = " + AdsTrim.y.ToString("F4", CultureInfo.InvariantCulture));
                sb.AppendLine("ads.z = " + AdsTrim.z.ToString("F4", CultureInfo.InvariantCulture));
                sb.AppendLine("adsScope.x = " + AdsScopeTrim.x.ToString("F4", CultureInfo.InvariantCulture) + "   # through a fitted optic");
                sb.AppendLine("adsScope.y = " + AdsScopeTrim.y.ToString("F4", CultureInfo.InvariantCulture));
                sb.AppendLine("adsScope.z = " + AdsScopeTrim.z.ToString("F4", CultureInfo.InvariantCulture));
                sb.AppendLine();
                sb.AppendLine("# Where the optic rail sits on the rifle, in the rifle's own space.");
                sb.AppendLine("rail.x = " + RailPos.x.ToString("F4", CultureInfo.InvariantCulture) + "   # keep at 0: off the bore line displaces the sight picture");
                sb.AppendLine("rail.y = " + RailPos.y.ToString("F4", CultureInfo.InvariantCulture) + "   # height off the receiver");
                sb.AppendLine("rail.z = " + RailPos.z.ToString("F4", CultureInfo.InvariantCulture) + "   # fore and aft along the rail");
                sb.AppendLine("rail.pitch = " + RailRot.x.ToString("F4", CultureInfo.InvariantCulture));
                sb.AppendLine("rail.yaw = " + RailRot.y.ToString("F4", CultureInfo.InvariantCulture));
                sb.AppendLine("rail.roll = " + RailRot.z.ToString("F4", CultureInfo.InvariantCulture));
                File.WriteAllText(CfgPath, sb.ToString());
                _stamp = File.GetLastWriteTimeUtc(CfgPath);
                M107Mod.Log("alignment written to " + CfgPath + "  (" + why + ")");
            }
            catch (Exception e) { M107Mod.Log("alignment save err: " + e.Message); }
        }
    }
}
