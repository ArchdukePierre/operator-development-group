// ODG Operations - M107
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace M107
{

    internal static class AdsProbe
    {
        const int Ring = 240;
        const float SummarySec = 1f;
        const float QuietSummarySec = 5f;
        const float AnchorBobMm = 4f;
        const float ChainBobMm = 8f;
        const float CameraBobMm = 15f;
        const float StillSpeed = 0.25f;
        const int MaxBursts = 12;
        const float BurstGapSec = 3f;
        const long SideFileCap = 40L * 1024 * 1024;

        class Series
        {
            internal string Name;
            internal Transform T;
            internal bool World;
            internal readonly Vector3[] Pos = new Vector3[Ring];
            internal int Writes;
        }

        static readonly List<Series> _series = new List<Series>();
        static readonly float[] _dt = new float[Ring];
        static readonly bool[] _aimed = new bool[Ring];
        static readonly float[] _speed = new float[Ring];
        static int _head, _count;
        static float _lastSummary, _lastBurst;
        static int _bursts;

        static int _rifleId;
        static Transform _root;
        static Vector3 _rootLast; static bool _haveRootLast;

        static Il2Cpp.ADS _adsComp;
        static Il2Cpp.ArmStamina _arm;
        static Il2Cpp.PlayerNetworking _pn;
        static Il2Cpp.WeaponMods _wm;
        static Il2Cpp.WeaponCollision _wc;
        static Il2Cpp.MasterController _mc;

        static readonly Dictionary<string, int> _writes = new Dictionary<string, int>();
        static readonly List<string> _events = new List<string>();

        static StreamWriter _file; static long _fileBytes; static float _flushAt; static bool _fileDead;

        internal static void ResetForScene()
        {
#if ODG_PUBLIC

            return;
#endif
            Unbind("scene changed");
        }

        internal static void Event(string what)
        {
#if ODG_PUBLIC

            return;
#endif
            try
            {
                if (_events.Count < 40) _events.Add(Time.unscaledTime.ToString("F2") + ":" + what);
                Side("EVENT " + what);
            }
            catch { }
        }

        internal static void Wrote(string node)
        {
#if ODG_PUBLIC

            return;
#endif
            try
            {
                if (node == null) node = "?";
                int n; _writes.TryGetValue(node, out n); _writes[node] = n + 1;
            }
            catch { }
        }

        internal static void Tick(GameObject held)
        {
#if ODG_PUBLIC

            return;
#endif
            try
            {
                if (held == null) { if (_rifleId != 0) Unbind("rifle put away"); return; }
                int id = 0; try { id = held.GetInstanceID(); } catch { return; }
                if (id != _rifleId) Bind(held, id);
                if (_rifleId == 0) return;
                if ((++_adoptFrame & 63) == 0) AdoptLateAnchors();
                Sample();
                float now = Time.unscaledTime;
                bool aimed = _aimed[(_head + Ring - 1) % Ring];
                float every = aimed ? SummarySec : QuietSummarySec;
                if (now - _lastSummary >= every) { Summarise(now, aimed); _lastSummary = now; }
                if (now - _flushAt > 1f) { _flushAt = now; try { if (_file != null) _file.Flush(); } catch { } }
            }
            catch (Exception e) { M107Mod.Log("adsprobe: tick err " + e.Message); }
        }

        static void Bind(GameObject held, int id)
        {
            Unbind(_rifleId != 0 ? "rifle changed" : null);
            _rifleId = id;
            _series.Clear(); _head = 0; _count = 0; _haveRootLast = false; _writes.Clear();
            try { _root = held.transform.root; } catch { _root = null; }

            var sb = new StringBuilder();
            sb.Append("adsprobe: bound to '").Append(held.name).Append("' id ").Append(id).Append('\n');

            string[] chain = { "TransformCompensation", "Sway", "Deadzone", "ADS", "ArmStamina", "WeaponCollision",
                               "RecoilParentTransform", "RecoilTransform", "WeaponBobSwayParent", "WeaponBobSway",
                               "RIG_Parent", "WeaponStance" };
            Transform cur = held.transform;
            for (int i = 0; i < chain.Length && cur != null; i++)
            {
                Transform next = null;
                try { next = cur.Find(chain[i]); } catch { }
                if (next == null)
                {

                    try
                    {
                        for (int c = 0; c < cur.childCount && next == null; c++)
                        {
                            var ch = cur.GetChild(c);
                            if (ch != null && ch.name == chain[i]) next = ch;
                        }
                    }
                    catch { }
                }
                if (next == null) { sb.Append("   chain: '").Append(chain[i]).Append("' not found under '").Append(cur.name).Append("'\n"); break; }
                Add("chain:" + chain[i], next, false);
                sb.Append("   chain ").Append(chain[i]).Append(" local ").Append(F(next.localPosition))
                  .Append(" rot ").Append(F(next.localEulerAngles)).Append('\n');
                cur = next;
            }

            var anchors = new List<Transform>(); var names = new List<string>();
            try { Alignment.ProbeAnchors(anchors, names); } catch { }
            for (int i = 0; i < anchors.Count; i++)
            {
                Add("anchor:" + names[i], anchors[i], false);
                sb.Append("   anchor ").Append(names[i]).Append(" local ").Append(F(anchors[i].localPosition))
                  .Append(" under '").Append(Path(anchors[i], 3)).Append("'\n");
            }
            var cams = new List<Transform>();
            try { Alignment.ProbeCams(cams); } catch { }
            for (int i = 0; i < cams.Count; i++)
            {
                Add("scopecam", cams[i], false);
                sb.Append("   scope camera local ").Append(F(cams[i].localPosition)).Append(" at '").Append(Path(cams[i], 4)).Append("'\n");
            }

            try
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    Add("cam.local", cam.transform, false);
                    Add("cam.world", cam.transform, true);
                    sb.Append("   camera '").Append(cam.name).Append("' parent chain '").Append(Path(cam.transform, 6)).Append("'\n");
                }
                else sb.Append("   camera: Camera.main is null\n");
            }
            catch { }

            try { _adsComp = held.GetComponentInChildren<Il2Cpp.ADS>(true); } catch { _adsComp = null; }
            try { _arm = held.GetComponentInChildren<Il2Cpp.ArmStamina>(true); } catch { _arm = null; }
            try { _wc = held.GetComponentInChildren<Il2Cpp.WeaponCollision>(true); } catch { _wc = null; }
            try { _pn = held.GetComponentInParent<Il2Cpp.PlayerNetworking>(); } catch { _pn = null; }
            try { _mc = held.GetComponentInParent<Il2Cpp.MasterController>(); } catch { _mc = null; }
            try { _wm = held.GetComponent<Il2Cpp.WeaponMods>(); if (_wm == null) _wm = held.GetComponentInChildren<Il2Cpp.WeaponMods>(true); } catch { _wm = null; }

            sb.Append("   ADS component: ").Append(_adsComp == null ? "none" : "speed=" + S(() => _adsComp.speed) + " base=" + S(() => _adsComp.baseSpeed)
                + " IsScope=" + S(() => _adsComp.IsScope) + " holo=" + S(() => _adsComp.IsHolographicOrRedDot) + " AdsZOffset=" + S(() => F(_adsComp.AdsZOffset))
                + " adsZ='" + S(() => _adsComp.adsZ == null ? "null" : Path(_adsComp.adsZ, 3)) + "'").Append('\n');
            sb.Append("   ArmStamina: ").Append(_arm == null ? "none" : "drain=" + S(() => _arm.drain) + " drainRate=" + S(() => _arm.drainRate)
                + " recovery=" + S(() => _arm.recoveryRate) + " stamina=" + S(() => _arm.armStamina) + " breath=" + S(() => _arm.Breath)
                + " breathMin=" + S(() => _arm.breathMin) + " breathMax=" + S(() => _arm.breathMax) + " mounted=" + S(() => _arm.isMounted)).Append('\n');
            sb.Append("   WeaponCollision: ").Append(_wc == null ? "none" : "animOutput=" + S(() => _wc.animOutput) + " speed=" + S(() => _wc.Speed)
                + " stab=" + S(() => _wc.weaponMountStablization) + "/" + S(() => _wc.weaponMountStablizationAmount)).Append('\n');
            sb.Append("   WeaponMods: ").Append(_wm == null ? "none" : "hasMagnifiedOptic=" + S(() => _wm.hasMagnifiedOptic) + " EyeOffset=" + S(() => _wm.EyeOffset)).Append('\n');
            sb.Append("   Player: isADS=").Append(_pn == null ? "?" : S(() => _pn.isADS)).Append(" armStamina=").Append(_pn == null ? "?" : S(() => _pn.armStamina))
              .Append(" drainArm=").Append(_pn == null ? "?" : S(() => _pn.drainArmStamina)).Append(" GunAimOver=").Append(_pn == null ? "?" : S(() => _pn.GunAimOver)).Append('\n');
            sb.Append("   MasterController: ").Append(_mc == null ? "none" : "sprintStamina=" + S(() => _mc.sprintStamina)
                + " drain=" + S(() => _mc.staminaDrainRate)).Append('\n');
            sb.Append("   fps ").Append((1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime)).ToString("F0")).Append(", ")
              .Append(_series.Count).Append(" series, RailPos ").Append(F(Alignment.RailPos)).Append(", scope fitted ").Append(Alignment.ScopeFitted);
            M107Mod.Log(sb.ToString());
            Side(sb.ToString());
            Side("# columns: t dt aimed isADS speed breath stamina " + Columns());
        }

        static int _adoptFrame;

        static void AdoptLateAnchors()
        {
            try
            {
                var anchors = new List<Transform>(); var names = new List<string>();
                try { Alignment.ProbeAnchors(anchors, names); } catch { }
                var cams = new List<Transform>();
                try { Alignment.ProbeCams(cams); } catch { }
                int added = 0;
                for (int i = 0; i < anchors.Count; i++) if (!Tracked(anchors[i])) { Add("anchor:" + names[i], anchors[i], false); added++; }
                for (int i = 0; i < cams.Count; i++) if (!Tracked(cams[i])) { Add("scopecam", cams[i], false); added++; }
                if (added > 0)
                {
                    M107Mod.Log("adsprobe: adopted " + added + " late anchor/camera series, now " + _series.Count);
                    Side("# columns: t dt aimed isADS speed breath stamina " + Columns());
                }
            }
            catch { }
        }

        static bool Tracked(Transform t)
        {
            for (int i = 0; i < _series.Count; i++) if (_series[i].T == t) return true;
            return false;
        }

        static void Unbind(string why)
        {
            if (_rifleId != 0 && why != null) M107Mod.Log("adsprobe: unbound (" + why + ")");
            _rifleId = 0; _series.Clear(); _count = 0; _head = 0;
            _adsComp = null; _arm = null; _pn = null; _wm = null; _wc = null; _mc = null; _root = null;
        }

        static void Add(string name, Transform t, bool world)
        {
            if (t == null) return;
            _series.Add(new Series { Name = name, T = t, World = world });
        }

        static void Sample()
        {
            int i = _head;
            _dt[i] = Time.unscaledDeltaTime;
            bool aimed = false;
            try { if (_adsComp != null) aimed = _adsComp.IsInADS; } catch { }
            if (!aimed) { try { if (_pn != null) aimed = _pn.isADS; } catch { } }
            _aimed[i] = aimed;

            float speed = 0f;
            try
            {
                if (_root != null)
                {
                    var p = _root.position;
                    if (_haveRootLast && _dt[i] > 0f) speed = (p - _rootLast).magnitude / _dt[i];
                    _rootLast = p; _haveRootLast = true;
                }
            }
            catch { }
            _speed[i] = speed;

            for (int s = 0; s < _series.Count; s++)
            {
                var se = _series[s];
                Vector3 v = se.Pos[(i + Ring - 1) % Ring];
                try { if (se.T != null) v = se.World ? se.T.position : se.T.localPosition; } catch { }
                se.Pos[i] = v;
            }

            if (_file != null || !_fileDead)
            {
                var sb = new StringBuilder(256);
                sb.Append(Time.unscaledTime.ToString("F3")).Append(' ').Append((_dt[i] * 1000f).ToString("F1")).Append(' ')
                  .Append(aimed ? 1 : 0).Append(' ').Append(S(() => _pn != null && _pn.isADS ? 1 : 0)).Append(' ')
                  .Append(speed.ToString("F2")).Append(' ').Append(S(() => _arm == null ? "-" : _arm.Breath.ToString("F3"))).Append(' ')
                  .Append(S(() => _arm == null ? "-" : _arm.armStamina.ToString("F1")));
                for (int s = 0; s < _series.Count; s++)
                {
                    var v = _series[s].Pos[i];
                    sb.Append(' ').Append((v.x * 1000f).ToString("F1")).Append(',').Append((v.y * 1000f).ToString("F1")).Append(',').Append((v.z * 1000f).ToString("F1"));
                }
                Side(sb.ToString());
            }

            _head = (i + 1) % Ring;
            if (_count < Ring) _count++;
        }

        static void Summarise(float now, bool aimedNow)
        {

            int n = 0; float t = 0f; int aimedN = 0; float speedSum = 0f;
            for (int k = 1; k <= _count; k++)
            {
                int i = (_head + Ring - k) % Ring;
                t += _dt[i]; n++;
                if (_aimed[i]) aimedN++;
                speedSum += _speed[i];
                if (t >= 1f) break;
            }
            if (n < 2) return;
            float fps = n / Mathf.Max(0.001f, t);
            float speedAvg = speedSum / n;
            bool still = speedAvg < StillSpeed;

            var sb = new StringBuilder(512);
            sb.Append("adsprobe: ").Append(fps.ToString("F0")).Append("fps aimed ").Append(aimedN).Append('/').Append(n)
              .Append(" speed ").Append(speedAvg.ToString("F2")).Append("m/s");
            try { if (_arm != null) sb.Append(" breath ").Append(_arm.Breath.ToString("F3")).Append(" stamina ").Append(_arm.armStamina.ToString("F0")); } catch { }
            try { if (_wc != null) sb.Append(" collide ").Append(_wc.animOutput.ToString("F2")); } catch { }

            var offenders = new List<Series>();
            for (int s = 0; s < _series.Count; s++)
            {
                var se = _series[s];
                float minY = float.MaxValue, maxY = float.MinValue, path = 0f; int flips = 0; float lastD = 0f;
                Vector3 prev = Vector3.zero; bool havePrev = false;
                for (int k = n; k >= 1; k--)
                {
                    int i = (_head + Ring - k) % Ring;
                    var v = se.Pos[i];
                    if (v.y < minY) minY = v.y; if (v.y > maxY) maxY = v.y;
                    if (havePrev)
                    {
                        float d = v.y - prev.y;
                        path += (v - prev).magnitude;
                        if (Mathf.Abs(d) > 0.0002f) { if (lastD != 0f && Mathf.Sign(d) != Mathf.Sign(lastD)) flips++; lastD = d; }
                    }
                    prev = v; havePrev = true;
                }
                float p2p = (maxY - minY) * 1000f;
                float hz = flips / 2f / Mathf.Max(0.001f, t);
                int w; _writes.TryGetValue(WriteKey(se), out w);
                se.Writes = w;
                if (p2p >= 0.5f || w > 0)
                {
                    sb.Append(" | ").Append(se.Name).Append(' ').Append(p2p.ToString("F1")).Append("mm");
                    if (flips > 1) sb.Append(' ').Append(hz.ToString("F1")).Append("Hz");
                    if (w > 0) sb.Append(" wrote×").Append(w);
                }
                float limit = se.Name.StartsWith("cam", StringComparison.Ordinal) ? CameraBobMm
                            : se.Name.StartsWith("chain", StringComparison.Ordinal) ? ChainBobMm : AnchorBobMm;

                bool osc = flips >= 3;
                bool anchorSpike = se.Name.StartsWith("anchor", StringComparison.Ordinal) && w > 0;
                bool bob = p2p >= limit && (osc || anchorSpike)
                        && (se.Name.StartsWith("cam", StringComparison.Ordinal) ? (still && aimedN > n / 2) : aimedN > n / 2);
                if (bob) offenders.Add(se);
            }
            if (_events.Count > 0) { sb.Append(" | events ").Append(string.Join(" ", _events.ToArray())); _events.Clear(); }
            _writes.Clear();

            bool interesting = aimedN > 0 || speedAvg > StillSpeed || sb.ToString().Contains("wrote") || sb.ToString().Contains("events");
            if (interesting || now - _lastSummary >= QuietSummarySec) M107Mod.Log(sb.ToString());

            if (offenders.Count > 0 && _bursts < MaxBursts && now - _lastBurst >= BurstGapSec)
            {
                _bursts++; _lastBurst = now;
                var b = new StringBuilder(1024);
                b.Append("adsprobe BOB: ").Append(offenders.Count).Append(" transform(s) over the limit while ")
                 .Append(aimedN > n / 2 ? "aimed" : "not aimed").Append(still ? ", standing still" : ", moving").Append(". Last 60 samples, y in mm:\n");
                foreach (var se in offenders)
                {
                    b.Append("   ").Append(se.Name).Append(se.Writes > 0 ? " (mod wrote it " + se.Writes + "x this second): " : " (mod did not write it): ");
                    int m = Math.Min(60, _count);
                    for (int k = m; k >= 1; k--)
                    {
                        int i = (_head + Ring - k) % Ring;
                        b.Append((se.Pos[i].y * 1000f).ToString("F1")).Append(' ');
                    }
                    b.Append('\n');
                }
                b.Append("   dt(ms): ");
                { int m = Math.Min(60, _count); for (int k = m; k >= 1; k--) b.Append((_dt[(_head + Ring - k) % Ring] * 1000f).ToString("F0")).Append(' '); }
                M107Mod.Log(b.ToString());
                Side(b.ToString());
            }
        }

        static string WriteKey(Series se)
        {

            int c = se.Name.IndexOf(':');
            var s = c >= 0 ? se.Name.Substring(c + 1) : se.Name;
            int c2 = s.IndexOf(':');
            return c2 >= 0 ? s.Substring(c2 + 1) : s;
        }

        static void Side(string line)
        {
            if (_fileDead) return;
            try
            {
                if (_file == null)
                {
                    var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(M107Mod.LogPath), "M107_adsprobe.log");
                    _file = new StreamWriter(path, true, Encoding.UTF8);
                    _file.WriteLine("\n===== adsprobe " + DateTime.Now + " =====");
                }
                _file.WriteLine(line);
                _fileBytes += line.Length + 2;
                if (_fileBytes > SideFileCap) { _file.WriteLine("# cap reached, trace stops here"); _file.Flush(); _file.Dispose(); _file = null; _fileDead = true; }
            }
            catch { _fileDead = true; }
        }

        static string Columns()
        {
            var sb = new StringBuilder();
            for (int s = 0; s < _series.Count; s++) sb.Append(_series[s].Name).Append("(x,y,z mm) ");
            return sb.ToString();
        }

        static string F(Vector3 v) { return "(" + v.x.ToString("F4") + ", " + v.y.ToString("F4") + ", " + v.z.ToString("F4") + ")"; }
        static string S(Func<object> f) { try { var o = f(); return o == null ? "null" : o.ToString(); } catch { return "err"; } }
        static string Path(Transform t, int levels)
        {
            try
            {
                var sb = new StringBuilder(); int g = 0;
                while (t != null && g++ < levels) { if (sb.Length > 0) sb.Insert(0, '/'); sb.Insert(0, t.name); t = t.parent; }
                return sb.ToString();
            }
            catch { return "?"; }
        }
    }
}
