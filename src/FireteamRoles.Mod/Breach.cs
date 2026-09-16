// ODG Operations - FireteamRoles
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using UnityEngine;

namespace FireteamRoles
{

    internal static class Breach
    {
        static readonly HashSet<string> _done = new HashSet<string>();
        static int _seq;

        static readonly List<string> _recent = new List<string>();
        const int RecentKept = 4;

        static float _nextPoll;

        sealed class Hidden { internal Renderer[] R; internal Collider[] Cols; }
        static readonly List<Hidden> _hidden = new List<Hidden>();
        static readonly HashSet<int> _hiddenIds = new HashSet<int>();
        sealed class Framed { internal Collider Original; internal readonly List<Collider> Added = new List<Collider>(); }
        static readonly List<Framed> _framed = new List<Framed>();
        static readonly HashSet<int> _framedIds = new HashSet<int>();
        static readonly List<Collider> _disabled = new List<Collider>();
        static readonly List<GameObject> _scorches = new List<GameObject>();
        static readonly List<GameObject> _insides = new List<GameObject>();
        static readonly HashSet<int> _insideIds = new HashSet<int>();

        static readonly List<GameObject> _shown = new List<GameObject>();
        static readonly HashSet<int> _shownIds = new HashSet<int>();

        static readonly List<Hole> _openings = new List<Hole>();
        static float _nextRecheck;

        internal static void NoteReplacedBox(Collider box, Collider added)
        {
            var f = new Framed { Original = box };
            f.Added.Add(added);
            _framed.Add(f);
            try { _framedIds.Add(box.GetInstanceID()); } catch { }
        }

        internal static void Reset(string why) { Reset(why, true); }

        internal static void Reset(string why, bool forgetEvents)
        {
            if (forgetEvents) { _done.Clear(); _recent.Clear(); }
            _jobs.Clear();
            MeshCut.Forget();
            Rubble.Clear(why);

            for (int i = 0; i < _hidden.Count; i++)
            {
                var hd = _hidden[i];
                try { if (hd.R != null) for (int k = 0; k < hd.R.Length; k++) if (hd.R[k] != null) hd.R[k].enabled = true; } catch { }
                try { if (hd.Cols != null) for (int k = 0; k < hd.Cols.Length; k++) if (hd.Cols[k] != null) hd.Cols[k].enabled = true; } catch { }
            }
            for (int i = 0; i < _framed.Count; i++)
            {
                var f = _framed[i];
                try { if (f.Original != null) f.Original.enabled = true; } catch { }
                for (int k = 0; k < f.Added.Count; k++) { try { if (f.Added[k] != null) UnityEngine.Object.Destroy(f.Added[k]); } catch { } }
            }
            for (int i = 0; i < _disabled.Count; i++) { try { if (_disabled[i] != null) _disabled[i].enabled = true; } catch { } }
            for (int i = 0; i < _scorches.Count; i++) { try { if (_scorches[i] != null) UnityEngine.Object.Destroy(_scorches[i]); } catch { } }
            for (int i = 0; i < _insides.Count; i++)
            {
                try
                {
                    var g = _insides[i];
                    if (g == null) continue;
                    var mf = g.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null) UnityEngine.Object.Destroy(mf.sharedMesh);
                    UnityEngine.Object.Destroy(g);
                }
                catch { }
            }
            for (int i = 0; i < _shown.Count; i++)
            {

                try { if (_shown[i] != null) UnityEngine.Object.Destroy(_shown[i]); } catch { }
            }
            int n = _hidden.Count + _framed.Count + _disabled.Count + _insides.Count + _shown.Count;
            _hidden.Clear(); _hiddenIds.Clear(); _framed.Clear(); _framedIds.Clear(); _disabled.Clear(); _scorches.Clear();
            _insides.Clear(); _insideIds.Clear();
            _shown.Clear(); _shownIds.Clear(); _openings.Clear();
            _renderers = null;
            Log.Detail("breach state cleared (" + why + ")" + (n > 0 ? ", " + n + " object(s) put back" : ""));
        }

        internal static void OnCharge(Il2Cpp.SlapChargeExplosive charge, Vector3 pos)
        {
            if (!Cfg.BreachEnabled.Value || !State.Enabled) return;

            ulong owner = OwnerOf(charge);
            if (!AllowedToBreach(owner))
            {
                Log.Detail("breach refused: " + (owner == 0UL ? "unknown owner" : State.NameOf(owner)) + " is not a breacher");
                return;
            }

            Vector3 dir = IntoTheWall(charge, pos);
            var h = Measure(pos, dir);
            if (h == null) return;
            Announce(h);
            Apply(h, true);
        }

        static Vector3 IntoTheWall(Il2Cpp.SlapChargeExplosive charge, Vector3 pos)
        {
            try
            {
                Vector3 f = Vector3.zero;
                try { if (charge != null) f = charge.transform.forward; } catch { }

                if (f.sqrMagnitude > 1e-4f)
                {
                    f = f.normalized;
                    float a = Solidity(pos, f), b = Solidity(pos, -f);
                    if (a > 0.01f || b > 0.01f) return a >= b ? f : -f;
                }

                try
                {
                    var det = charge != null ? charge.linkedDetonator : null;
                    var pn = det != null ? det.playerNetworking : null;
                    var tr = pn != null ? pn.transform : null;
                    if (tr != null)
                    {
                        Vector3 g = (pos - tr.position); g.y *= 0.2f;
                        if (g.sqrMagnitude > 1e-4f) return g.normalized;
                    }
                }
                catch { }
            }
            catch { }
            return Vector3.zero;
        }

        static float Solidity(Vector3 from, Vector3 dir)
        {
            float hitCount = 0f;
            try
            {
                for (int i = 1; i <= 6; i++)
                {
                    Vector3 p = from + dir * (i * 0.25f);
                    if (Physics.CheckSphere(p, 0.12f, ~0, QueryTriggerInteraction.Ignore)) hitCount += 1f;
                }
            }
            catch { }
            return hitCount;
        }

        static bool AllowedToBreach(ulong owner)
        {
            if (!Cfg.BreachBreacherOnly.Value) return true;
            if (owner == 0UL) return false;
            var p = State.Find(owner);
            if (p == null) return false;
            if (!p.HasMod) return false;
            return p.Role == Role.Breacher;
        }

        static ulong OwnerOf(Il2Cpp.SlapChargeExplosive charge)
        {
            try
            {
                if (charge == null) return 0UL;
                var det = charge.linkedDetonator;
                if (det == null) return 0UL;
                var pn = det.playerNetworking;
                if (pn == null) return 0UL;
                foreach (var kv in State.Peers)
                {
                    var p = kv.Value;
                    if (p == null || p.Pn == null) continue;
                    if (p.Pn.Pointer == pn.Pointer) return p.Id;
                }
            }
            catch (Exception e) { Log.Detail("breach owner lookup failed: " + e.Message); }
            return 0UL;
        }

        static Hole Measure(Vector3 pos, Vector3 dir)
        {
            if (dir.sqrMagnitude < 1e-4f) { Log.Msg("breach at " + Fmt(pos) + ": could not tell which way the wall is"); return null; }
            dir = dir.normalized;

            if (Mathf.Abs(dir.y) > 0.7f) { Log.Msg("breach at " + Fmt(pos) + ": the charge is on a floor or ceiling, walls only"); return null; }

            Vector3 fwd = dir; fwd.y = 0f; fwd = fwd.normalized;
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
            var h = new Hole { C = pos, Fwd = fwd, Right = right, Seed = MeshCut.SeedFrom(pos) };
            h.HalfW = Mathf.Clamp(Cfg.BreachHoleWidth.Value, 0.6f, 3f) * 0.5f;
            h.Clear = Mathf.Clamp(Cfg.BreachClearDepth.Value, 0f, 4f);
            h.Rough = Mathf.Clamp(Cfg.BreachEdgeRoughness.Value, 0f, 0.9f);
            float height = Mathf.Clamp(Cfg.BreachHoleHeight.Value, 1f, 3.2f);
            float maxThick = Mathf.Clamp(Cfg.BreachDepth.Value, 0.3f, 3f);

            var fronts = new List<float>(); var backs = new List<float>();
            Vector3[] offs = { Vector3.zero, right * 0.3f, -right * 0.3f, Vector3.up * 0.35f, -Vector3.up * 0.35f };
            for (int i = 0; i < offs.Length; i++)
            {
                try
                {
                    RaycastHit hit;
                    if (Physics.Raycast(pos + offs[i] - fwd * 0.6f, fwd, out hit, 1.2f, ~0, QueryTriggerInteraction.Ignore)
                        && Mathf.Abs(hit.normal.y) < 0.6f && !SkipForMeasure(hit.collider))
                        fronts.Add(Vector3.Dot(hit.point - pos, fwd));

                    var all = Physics.RaycastAll(pos + offs[i] + fwd * (maxThick + 0.05f), -fwd, maxThick, ~0, QueryTriggerInteraction.Ignore);
                    float best = float.NaN;
                    if (all != null)
                        for (int k = 0; k < all.Length; k++)
                        {
                            var hk = all[k];
                            if (hk.collider == null || SkipForMeasure(hk.collider)) continue;
                            if (Mathf.Abs(hk.normal.y) > 0.6f || Vector3.Dot(hk.normal, fwd) < 0.3f) continue;
                            float w = Vector3.Dot(hk.point - pos, fwd);
                            if (w > 0.02f && (float.IsNaN(best) || w < best)) best = w;
                        }
                    if (!float.IsNaN(best)) backs.Add(best);
                }
                catch { }
            }
            float front = fronts.Count > 0 ? Mathf.Clamp(Median(fronts), -0.3f, 0.3f) : -0.01f;
            float back = float.NaN;
            if (backs.Count > 0) { back = Median(backs); h.Measured = true; }
            if (!h.Measured || back - front > 0.8f || back - front < 0.02f)
            {
                back = front + Mathf.Clamp(Cfg.BreachWallThickness.Value, 0.05f, 1f);
                h.Measured = false;
            }
            h.Front = front; h.Back = back;

            float floor, ceil;
            FloorAndCeiling(pos, fwd, back, out floor, out ceil);
            float bottom = float.IsInfinity(floor) ? 1.25f : (pos.y - (floor + 0.02f));
            bottom = Mathf.Clamp(bottom, 0.3f, 2.4f);
            float top = height - bottom;
            if (!float.IsInfinity(ceil)) top = Mathf.Min(top, ceil - 0.06f - pos.y);
            top = Mathf.Clamp(top, 0.35f, 2.5f);
            h.Bottom = bottom; h.Top = top;
            h.CornerR = Mathf.Min(Mathf.Clamp(Cfg.BreachCornerRadius.Value, 0f, 0.6f), h.HalfW * 0.6f, h.HalfH * 0.6f);
            return h;
        }

        static bool SkipForMeasure(Collider col)
        {
            try
            {
                if (col == null || col.isTrigger) return true;
                if (col.GetComponentInParent<Il2CppMirror.NetworkIdentity>() != null) return true;
                if (col.GetComponentInParent<Il2Cpp.Health>() != null) return true;
                string ln = col.gameObject.name.ToLowerInvariant();
                return ln.StartsWith("odg_");
            }
            catch { return true; }
        }

        static float Median(List<float> v)
        {
            v.Sort();
            int n = v.Count;
            return (n & 1) == 1 ? v[n / 2] : (v[n / 2 - 1] + v[n / 2]) * 0.5f;
        }

        static void FloorAndCeiling(Vector3 centre, Vector3 fwd, float back, out float floor, out float ceil)
        {
            floor = float.NegativeInfinity; ceil = float.PositiveInfinity;
            try
            {
                Vector3[] probes = { centre - fwd * 0.35f, centre + fwd * (back + 0.5f) };
                for (int i = 0; i < probes.Length; i++)
                {
                    RaycastHit h;
                    if (Physics.Raycast(probes[i], Vector3.down, out h, 3.5f, ~0, QueryTriggerInteraction.Ignore)
                        && h.normal.y > 0.5f && h.point.y > floor && !SkipForMeasure(h.collider)) floor = h.point.y;
                    if (Physics.Raycast(probes[i], Vector3.up, out h, 4f, ~0, QueryTriggerInteraction.Ignore)
                        && h.normal.y < -0.5f && h.point.y < ceil && !SkipForMeasure(h.collider)) ceil = h.point.y;
                }
            }
            catch { }
        }

        static void Announce(Hole h)
        {
            try
            {
                _seq++;
                string ev = _seq.ToString() + "@" + Net.F(h.C.x) + "@" + Net.F(h.C.y) + "@" + Net.F(h.C.z) +
                            "@" + Net.F(h.HalfW) + "@" + Net.F(h.Fwd.x) + "@" + Net.F(h.Fwd.y) + "@" + Net.F(h.Fwd.z) +
                            "@" + Net.F(h.Front) + "@" + Net.F(h.Back) + "@" + Net.F(h.Top) + "@" + Net.F(h.Bottom) +
                            "@" + Net.F(h.Clear) + "@" + (h.Measured ? "1" : "0");
                _recent.Insert(0, ev);
                while (_recent.Count > RecentKept) _recent.RemoveAt(_recent.Count - 1);

                _done.Add(Key(Net.Self, _seq));
                Net.SetSelf(Net.K_BREACH, string.Join("|", _recent.ToArray()));
            }
            catch (Exception e) { Err.Note("breach.announce", e); }
        }

        internal static void Poll()
        {
            Rubble.Tick();
            PumpCuts();
            RecheckTick();

            if (!Cfg.BreachEnabled.Value || !State.Enabled) return;
            if (Time.realtimeSinceStartup < _nextPoll) return;
            _nextPoll = Time.realtimeSinceStartup + 0.25f;

            try
            {
                var members = Net.Members();
                for (int i = 0; i < members.Count; i++)
                {
                    ulong from = members[i];
                    if (from == Net.Self) continue;

                    string raw = Net.Get(from, Net.K_BREACH);
                    if (string.IsNullOrEmpty(raw)) continue;

                    var events = raw.Split('|');
                    for (int e = events.Length - 1; e >= 0; e--)
                    {
                        var f = events[e].Split('@');
                        if (f.Length < 5) continue;

                        int seq = Net.Int(f[0]);
                        if (seq == 0) continue;
                        if (!_done.Add(Key(from, seq))) continue;

                        float x = Net.Float(f[1], float.NaN);
                        float y = Net.Float(f[2], float.NaN);
                        float z = Net.Float(f[3], float.NaN);
                        if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(z)) continue;

                        Hole h = null;
                        if (f.Length >= 14) h = FromWire(f);
                        else if (f.Length >= 8)
                        {

                            var d = new Vector3(Net.Float(f[5]), Net.Float(f[6]), Net.Float(f[7]));
                            h = Measure(new Vector3(x, y, z), d);
                        }
                        if (h != null) Apply(h, false);
                    }
                }
            }
            catch (Exception ex) { Err.Note("breach.poll", ex); }
        }

        static Hole FromWire(string[] f)
        {
            try
            {
                var h = new Hole();
                h.C = new Vector3(Net.Float(f[1]), Net.Float(f[2]), Net.Float(f[3]));
                h.HalfW = Mathf.Clamp(Net.Float(f[4]), 0.3f, 1.5f);
                Vector3 fwd = new Vector3(Net.Float(f[5]), Net.Float(f[6]), Net.Float(f[7]));
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-4f) return null;
                h.Fwd = fwd.normalized;
                h.Right = Vector3.Cross(Vector3.up, h.Fwd).normalized;
                h.Front = Mathf.Clamp(Net.Float(f[8]), -0.5f, 0.5f);
                h.Back = Mathf.Clamp(Net.Float(f[9]), h.Front + 0.02f, h.Front + 1.5f);
                h.Top = Mathf.Clamp(Net.Float(f[10]), 0.2f, 3f);
                h.Bottom = Mathf.Clamp(Net.Float(f[11]), 0.2f, 3f);
                h.Clear = Mathf.Clamp(Net.Float(f[12]), 0f, 4f);
                h.Measured = Net.Int(f[13]) != 0;
                h.Seed = MeshCut.SeedFrom(h.C);
                h.Rough = Mathf.Clamp(Cfg.BreachEdgeRoughness.Value, 0f, 0.9f);
                h.CornerR = Mathf.Min(Mathf.Clamp(Cfg.BreachCornerRadius.Value, 0f, 0.6f), h.HalfW * 0.6f, h.HalfH * 0.6f);
                return h;
            }
            catch { return null; }
        }

        static string Key(ulong who, int seq) { return who.ToString() + ":" + seq.ToString(); }

        enum Kind { Ignore, Structure, Prop }

        sealed class Job
        {
            internal Hole H;
            internal bool Mine;
            internal readonly List<MeshFilter> Cuts = new List<MeshFilter>();
            internal readonly List<Hole> CutWith = new List<Hole>();
            internal readonly List<MeshCollider> Shells = new List<MeshCollider>();
            internal readonly List<Hole> ShellWith = new List<Hole>();
            internal readonly List<BoxCollider> Boxes = new List<BoxCollider>();
            internal readonly List<Collider> Stray = new List<Collider>();
            internal readonly List<Renderer> Props = new List<Renderer>();
            internal readonly List<Rigidbody> Loose = new List<Rigidbody>();
            internal int I;
            internal bool PropsDone;
            internal int Opened;
            internal Material WallMat;
            internal int WallLayer = -1;
            internal readonly List<string> Refusals = new List<string>();
            internal int Steps { get { return Cuts.Count + Shells.Count; } }
        }

        static readonly List<Job> _jobs = new List<Job>();

        static void Apply(Hole h, bool mine)
        {
            try
            {

                if (Cfg.BreachPreferAuthored.Value && AuthoredNearby(h))
                {
                    Log.Detail("authored destructible in the opening, leaving the breach to the game");
                    return;
                }

                var job = new Job { H = h, Mine = mine };
                Gather(job);
                if (job.Cuts.Count == 0 && job.Shells.Count == 0 && job.Boxes.Count == 0 && job.Props.Count == 0 && job.Stray.Count == 0)
                {
                    Log.Msg("breach at " + Fmt(h.C) + ": nothing in reach of the opening (" + h.Describe() + ")");
                    return;
                }

                _jobs.Add(job);
                while (_jobs.Count > 4)
                {
                    var head = _jobs[0];
                    Log.Detail("breach queue full, finishing " + head.Steps + " step(s) at once");
                    while (head.I < head.Steps) CutOne(head);
                    Finish(head);
                    _jobs.RemoveAt(0);
                }

                Log.Detail("breach queued at " + Fmt(h.C) + ": " + job.Cuts.Count + " structure(s), " +
                           job.Shells.Count + " collision shell(s), " + job.Boxes.Count + " box collider(s), " +
                           job.Props.Count + " prop(s), " + job.Loose.Count + " loose; " + h.Describe());
            }
            catch (Exception e) { Err.Note("breach.apply", e); }
        }

        static void Gather(Job j)
        {
            var h = j.H;
            var seen = new HashSet<int>();
            int cap = Mathf.Clamp(Cfg.BreachMaxObjects.Value, 1, 64);

            Collider[] inHole = null, inClear = null;
            try { inHole = Physics.OverlapBox(h.Centre, h.HalfExtents + new Vector3(0.05f, 0.05f, 0.05f), h.Frame, ~0, QueryTriggerInteraction.Ignore); } catch { }
            try { if (h.Clear > 0.05f) inClear = Physics.OverlapBox(h.ClearCentre, h.ClearHalfExtents, h.Frame, ~0, QueryTriggerInteraction.Ignore); } catch { }

            if (inHole != null)
                for (int i = 0; i < inHole.Length; i++) Consider(j, inHole[i], null, true, seen, cap);

            try
            {
                var rends = RenderersIn(h.WorldBounds(0.05f));
                for (int i = 0; i < rends.Count; i++) Consider(j, null, rends[i], true, seen, cap);
            }
            catch (Exception e) { Log.Detail("renderer sweep failed: " + e.Message); }

            if (inClear != null)
                for (int i = 0; i < inClear.Length; i++) Consider(j, inClear[i], null, false, seen, cap);
        }

        static void Consider(Job j, Collider col, Renderer rend, bool inOpening, HashSet<int> seen, int cap)
        {
            GameObject go = null;
            try { go = col != null ? col.gameObject : (rend != null ? rend.gameObject : null); } catch { return; }
            if (go == null) return;
            int id = go.GetInstanceID();
            bool first = seen.Add(id);

            var kind = Classify(go, col);
            if (kind == Kind.Ignore) return;
            var h = j.H;

            if (kind == Kind.Prop)
            {
                if (!first) return;
                Renderer r = rend;
                try { if (r == null) r = go.GetComponent<Renderer>(); } catch { }
                Rigidbody rb = null;
                try { rb = col != null ? col.attachedRigidbody : go.GetComponent<Rigidbody>(); } catch { }
                if (rb != null && !rb.isKinematic) { if (!j.Loose.Contains(rb)) j.Loose.Add(rb); return; }
                if (r != null) { if (j.Props.Count < 48 && !j.Props.Contains(r)) j.Props.Add(r); }
                else if (col != null && !j.Stray.Contains(col)) j.Stray.Add(col);
                return;
            }

            if (!inOpening)
            {

                Bounds b;
                try { b = col != null ? col.bounds : rend.bounds; } catch { return; }
                float extF = Mathf.Abs(b.size.x * h.Fwd.x) + Mathf.Abs(b.size.z * h.Fwd.z);
                if (extF < 0.6f) return;
                AddStructure(j, go, col, rend, h.Deep(), cap);
                return;
            }
            AddStructure(j, go, col, rend, h, cap);
        }

        static void AddStructure(Job j, GameObject go, Collider col, Renderer rend, Hole hole, int cap)
        {
            MeshFilter mf = null;
            try { mf = go.GetComponent<MeshFilter>(); } catch { }
            Renderer r = rend;
            try { if (r == null) r = go.GetComponent<Renderer>(); } catch { }
            Mesh visual = null;
            try { if (mf != null) visual = mf.sharedMesh; } catch { }

            bool cutsVisual = mf != null && visual != null && r != null;
            if (cutsVisual)
            {
                int at = j.Cuts.IndexOf(mf);
                if (at >= 0) { if (hole.Back > j.CutWith[at].Back) j.CutWith[at] = hole; }
                else if (j.Cuts.Count < cap) { j.Cuts.Add(mf); j.CutWith.Add(hole); }
            }

            if (col == null) return;
            try
            {
                var mc = col.TryCast<MeshCollider>();
                if (mc != null)
                {

                    bool sameMesh = false;
                    try { var cm = mc.sharedMesh; sameMesh = cutsVisual && cm != null && cm.GetInstanceID() == visual.GetInstanceID(); } catch { }
                    if (!sameMesh)
                    {
                        int at = j.Shells.IndexOf(mc);
                        if (at >= 0) { if (hole.Back > j.ShellWith[at].Back) j.ShellWith[at] = hole; }
                        else if (j.Shells.Count < cap) { j.Shells.Add(mc); j.ShellWith.Add(hole); }
                    }
                    return;
                }
                var bc = col.TryCast<BoxCollider>();
                if (bc != null)
                {

                    if (cutsVisual) return;
                    if (!j.Boxes.Contains(bc)) j.Boxes.Add(bc);
                    return;
                }
                if (!j.Stray.Contains(col)) j.Stray.Add(col);
            }
            catch { }
        }

        static readonly string[] PropWords =
        {
            "shelf", "shelves", "crate", "barrel", "cabinet", "cupboard", "table", "chair", "locker", "pallet",
            "bottle", "bag", "sack", "tyre", "tire", "plant", "bush", "hedge", "fence", "pane", "glass", "sign",
            "lamp", "bin", "trash", "cloth", "debris", "rubble", "rock", "stone", "pipe", "bucket", "bed", "sofa",
            "couch", "desk", "drawer", "monitor", "computer", "box_", "_box", "props", "prop_", "sm_prop", "clutter",
            "furniture", "book", "cone", "sandbag", "curtain", "poster", "rack", "cart", "trolley", "cable", "wire"
        };

        static readonly string[] StructureWords =
        {
            "wall", "building", "house", "roof", "ceiling", "floor", "stair", "pillar", "column", "beam",
            "container", "exo_mesh", "oilrig", "shoothouse", "structure", "hull", "deck", "bulkhead", "concrete",
            "brick", "facade", "frame", "arch", "tower", "bunker", "barn", "shed", "garage",
            "siding", "panel", "plank", "cladding", "drywall", "plaster", "partition", "trim", "skirting", "lintel"
        };

        static bool Has(string ln, string[] words)
        {
            for (int i = 0; i < words.Length; i++) if (ln.Contains(words[i])) return true;
            return false;
        }

        static Kind Classify(GameObject go, Collider col)
        {
            try
            {
                if (go == null) return Kind.Ignore;
                if (col != null && col.isTrigger) return Kind.Ignore;
                if (go.GetComponentInParent<Il2CppMirror.NetworkIdentity>() != null) return Kind.Ignore;
                if (go.GetComponentInParent<Il2Cpp.Health>() != null) return Kind.Ignore;

                string ln = (go.name ?? "").ToLowerInvariant();
                if (ln.StartsWith("odg_") || ln.Contains("terrain") || ln.Contains("water")) return Kind.Ignore;

                Bounds b = new Bounds();
                bool haveB = false;
                try
                {
                    if (col != null) { b = col.bounds; haveB = true; }
                    else { var r = go.GetComponent<Renderer>(); if (r != null) { b = r.bounds; haveB = true; } }
                }
                catch { }
                if (haveB && b.size.y < 0.4f && Mathf.Min(b.size.x, b.size.z) > 1.2f) return Kind.Ignore;

                if (Has(ln, PropWords)) return Kind.Prop;
                if (Has(ln, StructureWords)) return Kind.Structure;

                if (haveB)
                {
                    float minDim = Mathf.Min(b.size.x, Mathf.Min(b.size.y, b.size.z));
                    bool thin = minDim < 0.3f && minDim < b.size.y;
                    bool tall = b.size.y > 1.0f;
                    bool wide = Mathf.Max(b.size.x, b.size.z) > 0.8f;
                    if (thin && tall && wide) return Kind.Structure;
                }
                float diag = haveB ? b.size.magnitude : 0f;
                return diag > Mathf.Max(0.5f, Cfg.BreachDemolishMaxSize.Value) ? Kind.Structure : Kind.Prop;
            }
            catch { return Kind.Ignore; }
        }

        static bool IsSlab(Collider col)
        {
            try
            {
                if (col == null) return false;
                Vector3 s = col.bounds.size;
                return s.y < 0.4f && Mathf.Min(s.x, s.z) > 1.2f;
            }
            catch { return false; }
        }

        sealed class RendEntry { internal Renderer R; internal Bounds B; }
        static List<RendEntry> _renderers;
        static float _renderersAt;

        static List<Renderer> RenderersIn(Bounds box)
        {
            var outp = new List<Renderer>();
            float now = Time.realtimeSinceStartup;
            if (_renderers == null || now - _renderersAt > 60f)
            {
                _renderers = new List<RendEntry>();
                _renderersAt = now;
                try
                {
                    var all = Resources.FindObjectsOfTypeAll<MeshRenderer>();
                    if (all != null)
                        for (int i = 0; i < all.Length; i++)
                        {
                            var r = all[i];
                            if (r == null) continue;
                            try
                            {
                                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                                if (!r.gameObject.scene.IsValid()) continue;
                                if (r.GetComponent<Collider>() != null) continue;
                                _renderers.Add(new RendEntry { R = r, B = r.bounds });
                            }
                            catch { }
                        }
                }
                catch (Exception e) { Log.Detail("renderer index failed: " + e.Message); }
                Log.Detail("breach: indexed " + _renderers.Count + " collider-less renderer(s)");
            }
            for (int i = 0; i < _renderers.Count; i++)
            {
                var e = _renderers[i];
                if (e.B.Intersects(box)) { try { if (e.R != null) outp.Add(e.R); } catch { } }
            }
            return outp;
        }

        static bool AuthoredNearby(Hole h)
        {
            try
            {
                var hits = Physics.OverlapBox(h.Centre, h.HalfExtents + new Vector3(0.3f, 0.3f, 0.3f), h.Frame, ~0, QueryTriggerInteraction.Ignore);
                if (hits == null) return false;
                for (int i = 0; i < hits.Length; i++)
                {
                    if (hits[i] == null) continue;
                    if (hits[i].GetComponentInParent<Il2Cpp.Destructible>() != null) return true;
                }
            }
            catch { }
            return false;
        }

        static void PumpCuts()
        {
            if (_jobs.Count == 0) return;
            int budget = Mathf.Clamp(Cfg.BreachCutsPerFrame.Value, 1, 16);

            while (budget > 0 && _jobs.Count > 0)
            {
                var j = _jobs[0];
                if (!j.PropsDone) { j.PropsDone = true; BlowIn(j); Frames(j); }
                if (j.I >= j.Steps) { Finish(j); _jobs.RemoveAt(0); continue; }
                budget--;
                CutOne(j);
            }
        }

        static void CutOne(Job j)
        {
            if (!j.PropsDone) { j.PropsDone = true; BlowIn(j); Frames(j); }
            if (j.I < j.Cuts.Count)
            {
                var mf = j.Cuts[j.I];
                var hole = j.CutWith[j.I];
                j.I++;
                if (mf == null) return;
                try
                {
                    var rend = mf.GetComponent<MeshRenderer>();
                    if (rend != null && !rend.enabled) return;
                }
                catch { }

                var res = MeshCut.Cut(mf, hole, Collar(j.H));
                if (res.Ok)
                {
                    j.Opened++;
                    RememberWall(j, mf);
                    if (res.Erased)
                    {
                        Hide(mf.gameObject);
                        Log.Msg("removed " + mf.name + ": nothing of it lay outside the opening");
                    }
                    else
                        Log.Msg("cut " + mf.name + ": -" + res.TrianglesRemoved + " +" + res.TrianglesAdded + " tris, " +
                                res.CollarQuads + " collar quads, channels [" + res.Channels + "]" +
                                (res.Recovered ? "  (recovered off the GPU)" : "") + (hole.Back > j.H.Back + 0.01f ? "  (deep: runs into the room)" : ""));
                }
                else j.Refusals.Add(mf.name + " (" + res.Why + ")");
                return;
            }

            int s = j.I - j.Cuts.Count;
            if (s < j.Shells.Count)
            {
                var mc = j.Shells[s];
                var hole = j.ShellWith[s];
                j.I++;
                if (mc == null) return;
                var res = MeshCut.CutCollider(mc, hole);
                string name = "?"; try { name = mc.gameObject.name; } catch { }
                if (res.Ok)
                {
                    if (res.Erased) Disable(mc);
                    Log.Detail("cut the collision shell " + name + (res.Erased ? " (removed)" : ""));
                }
                else j.Refusals.Add(name + " collision (" + res.Why + ")");
            }
        }

        static float Collar(Hole h)
        {
            return h.Measured ? h.Thick * 0.5f + 0.01f : Mathf.Clamp(Cfg.BreachWallThickness.Value, 0.02f, 1f);
        }

        static void RememberWall(Job j, MeshFilter mf)
        {
            if (j.WallMat != null) return;
            try
            {
                var rend = mf.GetComponent<MeshRenderer>();
                if (rend != null) j.WallMat = rend.sharedMaterial;
                j.WallLayer = mf.gameObject.layer;
            }
            catch { }
        }

        static void BlowIn(Job j)
        {
            var h = j.H;
            uint rng = (uint)(h.Seed ^ 0x9e3779b9);
            var names = new List<string>();
            for (int i = 0; i < j.Props.Count; i++)
            {
                var r = j.Props[i];
                try
                {
                    if (r == null) continue;
                    var go = r.gameObject;
                    if (_hiddenIds.Contains(go.GetInstanceID())) continue;
                    Rubble.Smash(r, h, ref rng);
                    Hide(go);
                    j.Opened++;
                    if (names.Count < 8) names.Add(go.name);
                }
                catch (Exception e) { Log.Detail("blow-in failed: " + e.Message); }
            }
            for (int i = 0; i < j.Stray.Count; i++) { try { if (j.Stray[i] != null) { Disable(j.Stray[i]); j.Opened++; } } catch { } }
            for (int i = 0; i < j.Loose.Count; i++)
            {
                try
                {
                    var rb = j.Loose[i];
                    if (rb == null) continue;
                    rb.AddExplosionForce(Cfg.BreachDebrisForce.Value * 2.5f, h.C - h.Fwd * 0.3f, h.Clear + h.HalfW + 1f, 0.6f, ForceMode.Impulse);
                }
                catch { }
            }
            if (names.Count > 0)
            {
                Log.Msg("blew " + j.Props.Count + " prop(s) into the room: " + string.Join(", ", names.ToArray()) + (j.Props.Count > names.Count ? ", ..." : ""));
                Log.Fired("breach-blowin");
            }
        }

        static void Frames(Job j)
        {
            for (int i = 0; i < j.Boxes.Count; i++) { try { if (j.Boxes[i] != null && Frame(j.Boxes[i], j.H)) j.Opened++; } catch { } }
        }

        static bool Frame(BoxCollider bc, Hole h)
        {
            try
            {
                int id = bc.GetInstanceID();
                if (_framedIds.Contains(id)) return false;
                var t = bc.transform;

                Vector3 he = new Vector3(h.HalfW * (1f + h.Rough * 0.5f), h.HalfH, (h.DMax - h.DMin) * 0.5f + 0.6f);
                Vector3 c = h.C + h.Fwd * ((h.DMin + h.DMax) * 0.5f) + Vector3.up * h.VMid;
                Quaternion q = h.Frame;
                Vector3 lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                Vector3 hi = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                for (int i = 0; i < 8; i++)
                {
                    Vector3 s = new Vector3((i & 1) == 0 ? -he.x : he.x, (i & 2) == 0 ? -he.y : he.y, (i & 4) == 0 ? -he.z : he.z);
                    Vector3 lp = t.InverseTransformPoint(c + q * s);
                    lo = Vector3.Min(lo, lp); hi = Vector3.Max(hi, lp);
                }

                Vector3 bc0 = bc.center, bs = bc.size;
                Vector3 bmin = bc0 - bs * 0.5f, bmax = bc0 + bs * 0.5f;
                if (lo.x > bmax.x || hi.x < bmin.x || lo.y > bmax.y || hi.y < bmin.y || lo.z > bmax.z || hi.z < bmin.z) return false;

                int thin = (bs.x <= bs.y && bs.x <= bs.z) ? 0 : (bs.y <= bs.z ? 1 : 2);
                Vector3 up = t.InverseTransformDirection(Vector3.up);
                int vert;
                if (thin == 0) vert = Mathf.Abs(up.y) >= Mathf.Abs(up.z) ? 1 : 2;
                else if (thin == 1) vert = Mathf.Abs(up.x) >= Mathf.Abs(up.z) ? 0 : 2;
                else vert = Mathf.Abs(up.x) >= Mathf.Abs(up.y) ? 0 : 1;
                int horiz = 3 - thin - vert;

                float hloA = Mathf.Max(lo[horiz], bmin[horiz]), hhiA = Mathf.Min(hi[horiz], bmax[horiz]);
                float hloB = Mathf.Max(lo[vert], bmin[vert]), hhiB = Mathf.Min(hi[vert], bmax[vert]);
                if (hhiA - hloA < 0.02f || hhiB - hloB < 0.02f) return false;

                var f = new Framed { Original = bc };
                Piece(bc, f, horiz, vert, bmin[horiz], bmax[horiz], bmin[vert], hloB);
                Piece(bc, f, horiz, vert, bmin[horiz], bmax[horiz], hhiB, bmax[vert]);
                Piece(bc, f, horiz, vert, bmin[horiz], hloA, hloB, hhiB);
                Piece(bc, f, horiz, vert, hhiA, bmax[horiz], hloB, hhiB);
                bc.enabled = false;
                _framed.Add(f);
                _framedIds.Add(id);
                Log.Detail("framed " + bc.gameObject.name + ": " + f.Added.Count + " piece(s) round the opening");
                return true;
            }
            catch (Exception e) { Log.Detail("frame failed: " + e.Message); return false; }
        }

        static void Piece(BoxCollider src, Framed f, int a, int b, float a0, float a1, float b0, float b1)
        {
            if (a1 - a0 < 0.015f || b1 - b0 < 0.015f) return;
            var nb = src.gameObject.AddComponent<BoxCollider>();
            Vector3 c = src.center, s = src.size;
            c[a] = (a0 + a1) * 0.5f; s[a] = a1 - a0;
            c[b] = (b0 + b1) * 0.5f; s[b] = b1 - b0;
            nb.center = c; nb.size = s;
            try { nb.sharedMaterial = src.sharedMaterial; } catch { }
            nb.isTrigger = false;
            f.Added.Add(nb);
        }

        static void Hide(GameObject go)
        {
            try
            {
                if (go == null) return;
                if (!_hiddenIds.Add(go.GetInstanceID())) return;
                var rs = go.GetComponentsInChildren<Renderer>(true);
                var cs = go.GetComponentsInChildren<Collider>(true);
                var hd = new Hidden { R = rs, Cols = cs };
                if (rs != null) for (int i = 0; i < rs.Length; i++) { try { if (rs[i] != null) rs[i].enabled = false; } catch { } }
                if (cs != null) for (int i = 0; i < cs.Length; i++) { try { if (cs[i] != null) cs[i].enabled = false; } catch { } }
                _hidden.Add(hd);
            }
            catch (Exception e) { Log.Detail("hide failed: " + e.Message); }
        }

        static void Disable(Collider col)
        {
            try
            {
                if (col == null || !col.enabled) return;
                col.enabled = false;
                _disabled.Add(col);
            }
            catch { }
        }

        static void Prove(Job j)
        {
            if (!Cfg.BreachProveClear.Value) return;
            var h = j.H;
            for (int round = 0; round < 3; round++)
            {
                var blockers = new List<Collider>();
                var names = new List<string>();
                int rays, blocked;
                Rays(h, blockers, names, out rays, out blocked);
                if (blocked == 0)
                {
                    Log.Msg("breach passage: CLEAR, " + rays + " rays through" + (round > 0 ? " after " + round + " round(s) of clearing" : ""));
                    Log.Fired("breach-clear");
                    return;
                }
                if (round == 2 || blockers.Count == 0)
                {
                    Log.Warn("breach passage: " + blocked + " of " + rays + " rays still blocked by " + string.Join(", ", names.ToArray()));
                    return;
                }
                int fixedN = 0;
                for (int i = 0; i < blockers.Count; i++) if (Escalate(blockers[i], j)) fixedN++;
                Log.Detail("breach passage round " + (round + 1) + ": " + blocked + "/" + rays + " blocked, cleared " + fixedN + " of " + blockers.Count + " (" + string.Join(", ", names.ToArray()) + ")");
                if (fixedN == 0)
                {
                    Log.Warn("breach passage: " + blocked + " of " + rays + " rays blocked and nothing more can be done about " + string.Join(", ", names.ToArray()));
                    return;
                }
            }
        }

        static void Rays(Hole h, List<Collider> blockers, List<string> names, out int rays, out int blocked)
        {
            rays = 0; blocked = 0;
            float[] us = { -0.6f, -0.3f, 0f, 0.3f, 0.6f };
            float[] vs = { 0.2f, 0.5f, 0.8f };
            float len = 0.5f + h.Back + 0.45f;
            var seen = new HashSet<int>();
            for (int a = 0; a < us.Length; a++)
                for (int b = 0; b < vs.Length; b++)
                {
                    float uu = us[a] * h.HalfW;
                    float vv = -h.Bottom + vs[b] * (h.Top + h.Bottom);
                    Vector3 from = h.C + h.Right * uu + Vector3.up * vv - h.Fwd * 0.5f;
                    rays++;
                    bool hitAny = false;
                    try
                    {
                        var hits = Physics.RaycastAll(from, h.Fwd, len, ~0, QueryTriggerInteraction.Ignore);
                        if (hits != null)
                            for (int k = 0; k < hits.Length; k++)
                            {
                                var col = hits[k].collider;
                                if (col == null) continue;
                                try { if (!col.enabled) continue; } catch { continue; }
                                if (Classify(col.gameObject, col) == Kind.Ignore) continue;
                                hitAny = true;
                                if (seen.Add(col.GetInstanceID()))
                                {
                                    blockers.Add(col);
                                    names.Add(col.gameObject.name + " at " + hits[k].distance.ToString("0.0") + " m");
                                }
                            }
                    }
                    catch { }
                    if (hitAny) blocked++;
                }
        }

        static bool Escalate(Collider col, Job j)
        {
            try
            {
                var go = col.gameObject;
                var h = j.H;
                var kind = Classify(go, col);
                if (kind == Kind.Ignore) return false;

                Renderer r = null;
                try { r = go.GetComponent<Renderer>(); } catch { }
                MeshFilter mf = null;
                try { mf = go.GetComponent<MeshFilter>(); } catch { }

                if (kind == Kind.Prop)
                {
                    if (r != null) { uint rng = (uint)(h.Seed ^ go.GetInstanceID()); Rubble.Smash(r, h, ref rng); }
                    Hide(go);
                    j.Opened++;
                    Log.Msg("cleared " + go.name + " out of the doorway");
                    return true;
                }

                var bc = col.TryCast<BoxCollider>();
                if (bc != null && Frame(bc, h)) { j.Opened++; return true; }

                var mc = col.TryCast<MeshCollider>();
                if (mc != null)
                {
                    if (mf != null && r != null && !MeshCut.IsCut(mf))
                    {
                        var res = MeshCut.Cut(mf, h, Collar(h));
                        if (res.Ok)
                        {
                            if (res.Erased) Hide(go);
                            j.Opened++;
                            RememberWall(j, mf);
                            Log.Msg("cut " + mf.name + ": it stood in the way of the passage");
                            return true;
                        }
                        j.Refusals.Add(mf.name + " (" + res.Why + ")");
                    }
                    if (mf == null || r == null || !MeshCut.IsCut(mf))
                    {
                        var res = MeshCut.CutCollider(mc, h);
                        if (res.Ok)
                        {
                            if (res.Erased) Disable(mc);
                            Log.Msg("cut the collision shell on " + go.name + ": it stood in the way of the passage");
                            return true;
                        }
                    }
                }

                bool visualOpen = r == null || !r.enabled || (mf != null && MeshCut.IsCut(mf));
                if (visualOpen)
                {
                    Disable(col);
                    Log.Warn("switched off the collider on " + go.name + ": it could not be cut and it stood in the doorway");
                    Log.Fired("breach-collider-off");
                    return true;
                }
                return false;
            }
            catch (Exception e) { Log.Detail("escalate failed: " + e.Message); return false; }
        }

        static void Finish(Job j)
        {
            try
            {
                if (!j.PropsDone) { j.PropsDone = true; BlowIn(j); Frames(j); }
                Prove(j);
                if (j.Opened > 0)
                {
                    Log.Msg("BREACH  opened " + j.Opened + " object(s) at " + Fmt(j.H.C) + ", " + j.H.Describe() + (j.Mine ? " (mine)" : " (remote)"));
                    Log.Fired("breach-cut");
                    _openings.Add(j.H);
                    while (_openings.Count > 12) _openings.RemoveAt(0);
                    LookThrough(j.H);
                    if (j.WallMat != null)
                    {
                        int layer = j.WallLayer >= 0 ? j.WallLayer : 0;
                        Rubble.Frame(j.WallMat, layer, j.H);
                        Rubble.Spawn(j.WallMat, layer, j.H);
                    }
                    Debris(j.H);
                    Doors(j);
                    Insides(j);
                }
                else Log.Warn("breach at " + Fmt(j.H.C) + " opened nothing");
                if (j.Refusals.Count > 0) Log.Msg("breach left alone: " + string.Join("; ", j.Refusals.ToArray()));
            }
            catch (Exception e) { Err.Note("breach.finish", e); }
        }

        static void Insides(Job j)
        {
            if (!Cfg.BreachShowInsides.Value) return;
            var h = j.H;
            var found = new List<Collider>();
            var seen = new HashSet<int>();
            bool prev = false;
            try
            {
                prev = Physics.queriesHitBackfaces;
                Physics.queriesHitBackfaces = true;
                Vector3 origin = h.C + Vector3.up * h.VMid - h.Fwd * 0.4f;
                for (int a = -2; a <= 2; a++)
                    for (int bb = -1; bb <= 1; bb++)
                    {
                        Vector3 dir = (h.Fwd + h.Right * (a * 0.28f) + Vector3.up * (bb * 0.22f)).normalized;
                        var hits = Physics.RaycastAll(origin, dir, 30f, ~0, QueryTriggerInteraction.Ignore);
                        if (hits == null) continue;

                        int best = -1; float bestD = float.MaxValue;
                        for (int k = 0; k < hits.Length; k++)
                        {
                            var col = hits[k].collider;
                            if (col == null) continue;
                            if (hits[k].distance < h.Back + 0.6f) continue;
                            try { if (!col.enabled) continue; } catch { continue; }
                            if (Classify(col.gameObject, col) == Kind.Ignore) continue;
                            if (hits[k].distance < bestD) { bestD = hits[k].distance; best = k; }
                        }
                        if (best < 0) continue;
                        bool backface = Vector3.Dot(hits[best].normal, dir) > 0f;
                        if (!backface) continue;
                        var c = hits[best].collider;
                        if (seen.Add(c.GetInstanceID())) found.Add(c);
                    }
            }
            catch (Exception e) { Log.Detail("inside probe failed: " + e.Message); }
            finally { try { Physics.queriesHitBackfaces = prev; } catch { } }

            int made = 0, tris = 0;
            for (int i = 0; i < found.Count && made < 12 && tris < 400000; i++)
            {
                int t;
                if (Inside(found[i].gameObject, out t)) { made++; tris += t; }
            }
            if (made > 0)
            {
                Log.Msg("breach: drew the inside of " + made + " shell surface(s) the opening looks onto (" + tris + " tris)");
                Log.Fired("breach-insides");
            }
        }

        static bool Inside(GameObject go, out int tris)
        {
            tris = 0;
            try
            {
                if (go == null) return false;
                int id = go.GetInstanceID();
                if (_insideIds.Contains(id)) return false;
                var mf = go.GetComponent<MeshFilter>();
                var mr = go.GetComponent<MeshRenderer>();
                if (mf == null || mr == null || mf.sharedMesh == null) return false;
                var src = mf.sharedMesh;
                int count = 0;
                try { for (int sm = 0; sm < Mathf.Max(1, src.subMeshCount); sm++) count += (int)src.GetIndexCount(sm) / 3; } catch { count = 0; }
                if (count > 200000) { Log.Detail("inside: " + go.name + " is " + count + " tris, too large to mirror"); return false; }

                string why;
                var flipped = MeshCut.FlippedCopy(src, out why);
                if (flipped == null) { Log.Detail("inside: " + go.name + " could not be mirrored (" + why + ")"); return false; }

                var g = new GameObject("odg_inside");
                g.layer = go.layer;
                g.transform.SetParent(go.transform, false);
                g.transform.localPosition = Vector3.zero; g.transform.localRotation = Quaternion.identity; g.transform.localScale = Vector3.one;
                g.AddComponent<MeshFilter>().sharedMesh = flipped;
                var r2 = g.AddComponent<MeshRenderer>();
                r2.sharedMaterials = mr.sharedMaterials;
                try { r2.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; } catch { }
                try { r2.lightmapIndex = mr.lightmapIndex; r2.lightmapScaleOffset = mr.lightmapScaleOffset; } catch { }
                _insides.Add(g);
                _insideIds.Add(id);
                tris = count;
                return true;
            }
            catch (Exception e) { Log.Detail("inside failed: " + e.Message); return false; }
        }

        static void Debris(Hole h)
        {
            if (!Cfg.BreachDebris.Value) return;
            try
            {
                var hits = Physics.OverlapBox(h.ClearCentre, h.ClearHalfExtents + new Vector3(0.6f, 0.4f, 0.6f), h.Frame, ~0, QueryTriggerInteraction.Ignore);
                if (hits == null) return;
                int pushed = 0;
                var seen = new HashSet<int>();
                for (int i = 0; i < hits.Length && pushed < 24; i++)
                {
                    var rb = hits[i] != null ? hits[i].attachedRigidbody : null;
                    if (rb == null || rb.isKinematic || !seen.Add(rb.GetInstanceID())) continue;

                    try { if (rb.GetComponentInParent<Il2CppMirror.NetworkIdentity>() != null) continue; } catch { }
                    rb.AddExplosionForce(Cfg.BreachDebrisForce.Value, h.C - h.Fwd * 0.2f, h.Clear + h.HalfW + 1f, 0.4f, ForceMode.Impulse);
                    pushed++;
                }
            }
            catch { }
        }

        static void Doors(Job j)
        {
            if (!j.Mine) return;
            try
            {
                var h = j.H;
                var centre = h.C + h.Fwd * ((h.Front + h.Back) * 0.5f) + Vector3.up * h.VMid;
                var half = new Vector3(h.HalfW + 0.5f, h.HalfH + 0.5f, h.Thick * 0.5f + 0.6f);
                var hits = Physics.OverlapBox(centre, half, h.Frame, ~0, QueryTriggerInteraction.Ignore);
                if (hits == null) return;
                var seen = new HashSet<int>();
                int done = 0;
                for (int i = 0; i < hits.Length; i++)
                {
                    if (hits[i] == null) continue;
                    Il2Cpp.DoorV2 door = null;
                    try { door = hits[i].GetComponentInParent<Il2Cpp.DoorV2>(); } catch { }
                    if (door == null || !seen.Add(door.GetInstanceID())) continue;
                    bool dead = false; try { dead = door.isDead; } catch { }
                    if (dead) continue;
                    string dn = "?"; try { dn = door.gameObject.name; } catch { }
                    bool blow = false; try { blow = door.canBlowup && door.DestroyedDoor != null; } catch { }
                    try
                    {
                        if (blow)
                        {
                            door.CMD_ExplodeDoor(h.C);
                            Log.Msg("breach: door '" + dn + "' stood in the opening; exploded through the game's own networked path");
                        }
                        else
                        {
                            Il2Cpp.PlayerNetworking pn = null;
                            try { var me = State.Me; pn = me != null ? me.Pn : null; } catch { }
                            door.AddForceAndUnlatch(Cfg.BreachDebrisForce.Value * 4f, pn);
                            Log.Msg("breach: door '" + dn + "' stood in the opening and cannot blow up; flung open and unlatched (networked)");
                        }
                        done++;
                    }
                    catch (Exception e) { Log.Detail("breach door '" + dn + "': " + e.Message); }
                }
                if (done > 0) Log.Fired("breach-door");
            }
            catch (Exception e) { Log.Detail("breach doors: " + e.Message); }
        }

        internal static void Survey()
        {
            try
            {
                var me = State.Me;
                Vector3 at = me != null && me.Root != null ? me.Root.position : Vector3.zero;

                var hits = Physics.OverlapSphere(at, 18f, ~0, QueryTriggerInteraction.Ignore);
                if (hits == null || hits.Length == 0) { Log.Msg("BREACH SURVEY: nothing in reach"); return; }

                int structures = 0, props = 0, ignored = 0, readable = 0, batched = 0, tooBig = 0, noMesh = 0, shells = 0, boxes = 0;
                var examples = new List<string>();
                var seen = new HashSet<int>();

                for (int i = 0; i < hits.Length; i++)
                {
                    var col = hits[i];
                    if (col == null) continue;
                    var go = col.gameObject;
                    if (!seen.Add(go.GetInstanceID())) continue;
                    var kind = Classify(go, col);
                    if (kind == Kind.Ignore) { ignored++; continue; }
                    if (kind == Kind.Prop) { props++; continue; }
                    structures++;

                    MeshFilter mf = null; try { mf = go.GetComponent<MeshFilter>(); } catch { }
                    Renderer r = null; try { r = go.GetComponent<Renderer>(); } catch { }
                    if (col.TryCast<BoxCollider>() != null && (mf == null || r == null)) boxes++;
                    if (col.TryCast<MeshCollider>() != null && (mf == null || r == null)) shells++;

                    Mesh m = null;
                    try { m = mf != null ? mf.sharedMesh : null; } catch { }
                    if (m == null) { noMesh++; continue; }

                    bool isBatched = false;
                    try { var rend = mf.GetComponent<MeshRenderer>(); isBatched = rend != null && rend.isPartOfStaticBatch; } catch { }
                    bool canRead = false;
                    try { canRead = m.isReadable; } catch { }
                    int tris = -1;
                    try { tris = canRead ? m.triangles.Length / 3 : -1; } catch { }

                    if (isBatched) batched++;
                    else if (!canRead) { }
                    else if (tris > Cfg.BreachTriangleBudget.Value) tooBig++;
                    else readable++;

                    if (examples.Count < 12)
                        examples.Add("    " + go.name + "  readable=" + canRead + " batched=" + isBatched + " tris=" + tris);
                }

                Log.Msg("BREACH SURVEY at " + Fmt(at) + ": " + structures + " structure(s), " + props + " prop(s), " + ignored + " ignored, within 18 m");
                Log.Msg("    structures: cuttable=" + readable + "  notReadable(GPU readback)=" + (structures - readable - batched - tooBig - noMesh) +
                        "  staticBatched=" + batched + "  overBudget=" + tooBig + "  noMesh=" + noMesh + "  collisionShells=" + shells + "  boxColliders=" + boxes);
                for (int i = 0; i < examples.Count; i++) Log.Msg(examples[i]);
            }
            catch (Exception e) { Err.Note("breach.survey", e); }
        }

        static void LookThrough(Hole h) { LookThrough(h, Mathf.Clamp(Cfg.BreachRoomMax.Value, 0, 400), true); }

        static void LookThrough(Hole h, int budget, bool announce)
        {

            float depth = Mathf.Clamp(Cfg.BreachRoomDepth.Value, 1f, 30f);
            Vector3 he = new Vector3(h.HalfW + 2.5f, h.HalfH + 1f, depth * 0.5f);
            Vector3 c = h.C + h.Fwd * (h.Back + depth * 0.5f) + Vector3.up * h.VMid;
            Quaternion q = h.Frame;

            var seen = new HashSet<int>();
            var toShow = new List<Renderer>();
            int present = 0, drawn = 0, hidden = 0, offScreen = 0;

            try
            {

                var cam = GameCamera();
                var planes = FrustumOf(cam);

                var cols = Physics.OverlapBox(c, he, q, ~0, QueryTriggerInteraction.Ignore);
                if (cols != null)
                    for (int i = 0; i < cols.Length; i++)
                    {
                        var col = cols[i];
                        if (col == null) continue;
                        Renderer r = null;
                        try { r = col.GetComponent<MeshRenderer>(); } catch { }
                        if (r == null) continue;
                        Weigh(r, planes, seen, toShow, ref present, ref drawn, ref hidden, ref offScreen);
                    }

                var loose = RenderersIn(Aabb(c, he, q, 0f));
                for (int i = 0; i < loose.Count; i++)
                    Weigh(loose[i], planes, seen, toShow, ref present, ref drawn, ref hidden, ref offScreen);
            }
            catch (Exception e) { Log.Detail("room census failed: " + e.Message); }

            if (announce)
                Log.Msg("breach: " + present + " renderer(s) in the room behind the opening, " + drawn +
                        " drawn, " + hidden + " hidden by the bake, " + offScreen + " off screen");

            if (hidden == 0 || !Cfg.BreachShowRoom.Value) return;

            int cap = Mathf.Max(0, budget);
            int made = 0, skipped = 0;
            for (int i = 0; i < toShow.Count && made < cap; i++)
            {
                if (Show(toShow[i])) made++; else skipped++;
            }
            if (made > 0)
            {
                Log.Msg("breach: the bake was hiding the room, so " + made + " object(s) in it now draw from anywhere" +
                        (announce ? "" : " (on a second look from where you are standing)") +
                        (skipped > 0 ? " (" + skipped + " left alone)" : "") +
                        (toShow.Count > cap ? ", " + (toShow.Count - cap) + " over the cap" : ""));
                Log.Fired("breach-room");
            }
        }

        static void Weigh(Renderer r, object planes, HashSet<int> seen, List<Renderer> toShow,
                          ref int present, ref int drawn, ref int hidden, ref int offScreen)
        {
            try
            {
                if (r == null) return;
                if (!seen.Add(r.GetInstanceID())) return;
                if (!r.enabled || !r.gameObject.activeInHierarchy) return;
                present++;
                if (r.isVisible) { drawn++; return; }
                if (!InFrustum(planes, r.bounds)) { offScreen++; return; }
                hidden++;
                toShow.Add(r);
            }
            catch { }
        }

        static object FrustumOf(Camera cam)
        {
            try { return cam != null ? GeometryUtility.CalculateFrustumPlanes(cam) : null; }
            catch (Exception e) { Log.Detail("frustum planes unavailable: " + e.Message); return null; }
        }

        static bool InFrustum(object planes, Bounds b)
        {
            if (planes == null) return true;
            try
            {
                var p = planes as Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Plane>;
                if (p == null) return true;
                return GeometryUtility.TestPlanesAABB(p, b);
            }
            catch { return true; }
        }

        static bool Show(Renderer src)
        {
            try
            {
                if (src == null) return false;
                var go = src.gameObject;
                if (!_shownIds.Add(src.GetInstanceID())) return false;
                if (go.name != null && go.name.StartsWith("odg_")) return false;

                MeshFilter mf = null;
                try { mf = go.GetComponent<MeshFilter>(); } catch { }
                if (mf == null || mf.sharedMesh == null) return false;

                try
                {
                    if (go.GetComponentInParent<LODGroup>() != null)
                    {
                        string u = (go.name ?? "").ToUpperInvariant();
                        for (int k = 1; k <= 9; k++) if (u.EndsWith("LOD" + k)) return false;
                    }
                }
                catch { }

                var mats = src.sharedMaterials;
                if (mats == null || mats.Length == 0) return false;

                try
                {
                    for (int i = 0; i < mats.Length; i++)
                        if (mats[i] != null && mats[i].renderQueue >= 3000) return false;
                }
                catch { }

                var g = new GameObject("odg_shown");
                g.layer = go.layer;
                g.transform.SetParent(go.transform, false);
                g.transform.localPosition = Vector3.zero;
                g.transform.localRotation = Quaternion.identity;
                g.transform.localScale = Vector3.one;
                g.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var r2 = g.AddComponent<MeshRenderer>();
                r2.sharedMaterials = mats;
                try { r2.lightmapIndex = src.lightmapIndex; r2.lightmapScaleOffset = src.lightmapScaleOffset; } catch { }
                try { r2.probeAnchor = src.probeAnchor; } catch { }

                try { r2.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; } catch { }
                _shown.Add(g);
                return true;
            }
            catch (Exception e) { Log.Detail("show failed: " + e.Message); return false; }
        }

        static void RecheckTick()
        {
            if (!Cfg.BreachEnabled.Value || !Cfg.BreachShowRoom.Value) return;
            if (_openings.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            if (now < _nextRecheck) return;
            _nextRecheck = now + 0.5f;

            try
            {
                var cam = GameCamera();
                if (cam == null) return;
                Vector3 p = cam.transform.position;
                float near = Mathf.Clamp(Cfg.BreachRoomDepth.Value, 1f, 30f) + 12f;
                for (int i = 0; i < _openings.Count; i++)
                {
                    var h = _openings[i];
                    if (h == null) continue;
                    if (Vector3.Distance(p, h.C) > near) continue;
                    LookThrough(h, 24, false);
                    return;
                }
            }
            catch (Exception e) { Log.Detail("room recheck failed: " + e.Message); }
        }

        static Bounds Aabb(Vector3 centre, Vector3 he, Quaternion q, float pad)
        {
            he += Vector3.one * pad;
            var b = new Bounds(centre, Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                Vector3 s = new Vector3((i & 1) == 0 ? -he.x : he.x, (i & 2) == 0 ? -he.y : he.y, (i & 4) == 0 ? -he.z : he.z);
                b.Encapsulate(centre + q * s);
            }
            return b;
        }

        static Camera GameCamera()
        {
            try
            {
                var m = Camera.main;
                if (m != null) return m;
            }
            catch { }
            try
            {
                Camera best = null;
                var all = Camera.allCameras;
                if (all == null) return null;
                for (int i = 0; i < all.Length; i++)
                {
                    var c = all[i];
                    if (c == null || !c.enabled) continue;
                    if (best == null || c.depth > best.depth) best = c;
                }
                return best;
            }
            catch { return null; }
        }

        static string Fmt(Vector3 v)
        {
            return "(" + v.x.ToString("0.#") + ", " + v.y.ToString("0.#") + ", " + v.z.ToString("0.#") + ")";
        }
    }
}
