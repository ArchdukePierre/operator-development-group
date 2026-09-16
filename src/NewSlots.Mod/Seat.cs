// ODG Operations - NewSlots
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace NewSlots
{

    internal static class Seat
    {
        sealed class Worn
        {
            internal Transform T;
            internal Def D;
            internal string Garment, Socket;
            internal uint Owner;
            internal Vector3 RestPos, RestScale;
            internal bool Rested;
            internal int Id;

            internal Vector3 LastWrote;
            internal bool HasWritten;
            internal bool Reported;

            internal MeshFilter Mf;
            internal Mesh Own;
            internal Vector3[] Rest;
            internal float[] ArtS, ArtT;
            internal Vector3 N;
            internal bool GeomReady, GeomFailed, Bent;
            internal float A0, A1, A2, A3;

            internal Vector3 ArtCentre, ArtRight, ArtUp;
            internal float HalfS, HalfT;
        }

        internal struct PatchEdges
        {
            internal Transform T;
            internal Def D;
            internal string Garment, Socket;
            internal Vector3[] Corners;
            internal Vector3 Lift;
        }

        internal static int CollectLocalPatchEdges(List<PatchEdges> into)
        {
            int n = 0;
            for (int i = 0; i < _worn.Count; i++)
            {
                var w = _worn[i];
                try
                {
                    if (w == null || w.D == null || w.D.Type != Kind.Patch || w.Owner != 0 || w.T == null) continue;
                    if (!w.GeomReady && !PrepareGeom(w)) continue;
                    if (w.Mf == null) continue;
                    var tune = Tuning.Get(w.Garment, w.Socket, w.D.ModName);
                    float lossy = 1f;
                    var ls = w.Mf.transform.lossyScale;
                    float a = w.D.TA == 1 ? ls.y : (w.D.TA == 2 ? ls.z : ls.x);
                    if (a > 1e-6f || a < -1e-6f) lossy = Mathf.Abs(a);
                    float k = (1f / 1000f) / lossy;
                    var lift = w.N * (NewSlotsMod.PatchDepthFlip ? -1f : 1f);
                    var C = w.ArtCentre; var R = w.ArtRight * w.HalfS; var U = w.ArtUp * w.HalfT;
                    var e = new PatchEdges
                    {
                        T = w.Mf.transform, D = w.D, Garment = w.Garment, Socket = w.Socket, Lift = lift,
                        Corners = new Vector3[]
                        {
                            C - R + U + lift * (tune.CTL * k),
                            C + R + U + lift * (tune.CTR * k),
                            C + R - U + lift * (tune.CBR * k),
                            C - R - U + lift * (tune.CBL * k),
                        },
                    };
                    into.Add(e); n++;
                }
                catch { }
            }
            return n;
        }

        static readonly List<Worn> _worn = new List<Worn>();

        internal static void Register(Def d, GameObject go, string garment, string socket)
        {
            if (d == null || go == null) return;
            try
            {
                int id = go.GetInstanceID();
                uint owner = OwnerOf(go);

                if (d.Type == Kind.Patch && owner == 0) Tuning.EnsureOwn(garment, socket, d.ModName);
                for (int i = 0; i < _worn.Count; i++)
                    if (_worn[i].Id == id) { _worn[i].D = d; _worn[i].Garment = garment; _worn[i].Socket = socket; _worn[i].Owner = owner; return; }

                _worn.Add(new Worn
                {
                    T = go.transform,
                    D = d,
                    Garment = garment,
                    Socket = socket,
                    Owner = owner,
                    Id = id,
                });

                NewSlotsMod.Detail("seat: registered " + d.ModName + (garment == null ? " (top-level)" : " on " + garment) +
                                   (owner == 0 ? " (local)" : " (player " + owner + ")"));
                if (owner != 0 && !_ownersSeen.Contains(owner))
                {
                    _ownersSeen.Add(owner);
                    NewSlotsMod.Log("seat: worn items of player " + owner + " found - drawn with that player's own seating");
                }
                if (_worn.Count > 64) _worn.RemoveAt(0);
            }
            catch { }
        }

        static readonly HashSet<uint> _ownersSeen = new HashSet<uint>();

        static bool _noPlayerLogged;
        static uint OwnerOf(GameObject go)
        {
            try
            {
                var p = go.GetComponentInParent<Il2Cpp.PlayerNetworking>(true);
                if (p != null)
                {
                    bool mine = false;
                    try { mine = p.isLocalPlayer; } catch { }
                    if (!mine)
                    {
                        try { var me = Il2Cpp.GameManager.myPlayerNetworking; mine = me != null && me.Pointer == p.Pointer; } catch { }
                    }
                    if (mine) return 0;
                    uint id = 0;
                    try { id = p.netId; } catch { }
                    if (id == 0) { try { id = (uint)(p.GetInstanceID() & 0x7fffffff); } catch { } }
                    return id;
                }
                var sync = go.GetComponentInParent<Il2Cpp.CharacterModSync>(true);
                if (sync != null)
                {
                    bool local = false;
                    try { local = sync.isLocalPlayer; } catch { }
                    if (local) return 0;
                    uint id = 0;
                    try { id = sync.netId; } catch { }
                    return id;
                }
                if (!_noPlayerLogged)
                {
                    _noPlayerLogged = true;
                    NewSlotsMod.Log("seat: no player object above " + go.name + " (path " + Path(go.transform) + ") - treated as local");
                    NewSlotsMod.FlushLog();
                }
                return 0;
            }
            catch { return 0; }
        }

        static string Path(Transform t)
        {
            var sb = new System.Text.StringBuilder(); int guard = 0;
            while (t != null && guard++ < 12) { sb.Insert(0, "/" + t.name); t = t.parent; }
            return sb.ToString();
        }

        internal static void EachWornOf(string modName, Action<string, string> f)
        {
            if (string.IsNullOrEmpty(modName) || f == null) return;
            for (int i = 0; i < _worn.Count; i++)
            {
                var w = _worn[i];
                if (w == null || w.D == null || w.Garment == null || w.Owner != 0) continue;
                if (!string.Equals(w.D.ModName, modName, StringComparison.OrdinalIgnoreCase)) continue;
                try { f(w.Garment, w.Socket); } catch { }
            }
        }

        static int _frame;

        internal static void Tick()
        {
            _frame++;
            bool checkMats = (_frame % 30) == 0;
            if (checkMats) HealTemplates();

            for (int i = _worn.Count - 1; i >= 0; i--)
            {
                var w = _worn[i];
                Transform t = w.T;
                bool dead = false;
                try { dead = (t == null) || (t.gameObject == null); } catch { dead = true; }
                if (dead)
                {

                    try { if (w.Own != null) UnityEngine.Object.Destroy(w.Own); } catch { }
                    _worn.RemoveAt(i); continue;
                }

                if (w.D.Type == Kind.Garment)
                {
                    if (checkMats) Reassert(w);
                    continue;
                }

                try
                {
                    if (!w.Rested)
                    {
                        w.RestPos = t.localPosition;
                        w.RestScale = t.localScale;
                        w.Rested = true;
                    }

                    var tune = Tuning.Get(w.Owner, w.Garment, w.Socket, w.D.ModName);

                    var scale = new Vector3(w.RestScale.x * tune.Scale,
                                            w.RestScale.y * tune.Scale,
                                            w.RestScale.z * tune.Scale);
                    if (t.localScale != scale) t.localScale = scale;

                    var axis = Vector3.right;
                    if (w.D.TA == 1) axis = Vector3.up; else if (w.D.TA == 2) axis = Vector3.forward;
                    float dir = NewSlotsMod.PatchDepthFlip ? -1f : 1f;

                    float pscale = 1f;
                    try
                    {
                        var par = t.parent;
                        if (par != null)
                        {
                            var ls = par.lossyScale;
                            float a = w.D.TA == 1 ? ls.y : (w.D.TA == 2 ? ls.z : ls.x);
                            if (a > 1e-6f || a < -1e-6f) pscale = Mathf.Abs(a);
                        }
                    }
                    catch { }

                    var pos = w.RestPos + axis * (dir * (tune.OutMm / 1000f) / pscale);
                    if (t.localPosition != pos) t.localPosition = pos;

                    Diagnose(w, t, tune, pscale, pos);
                    w.LastWrote = t.localPosition;
                    w.HasWritten = true;

                    Peel(w, tune);
                }
                catch { _worn.RemoveAt(i); }
            }
        }

        static void Peel(Worn w, Tuning.Entry tune)
        {
            bool want = tune.AnyCorner;
            if (!want && !w.Bent) return;
            if (!w.GeomReady && !PrepareGeom(w)) return;
            if (tune.CTL == w.A0 && tune.CTR == w.A1 && tune.CBR == w.A2 && tune.CBL == w.A3) return;

            float lossy = 1f;
            try
            {
                var ls = w.Mf.transform.lossyScale;
                float a = w.D.TA == 1 ? ls.y : (w.D.TA == 2 ? ls.z : ls.x);
                if (a > 1e-6f || a < -1e-6f) lossy = Mathf.Abs(a);
            }
            catch { }
            float k = (1f / 1000f) / lossy;
            float dir = NewSlotsMod.PatchDepthFlip ? -1f : 1f;

            int n = w.Rest.Length;
            var verts = new Il2CppStructArray<Vector3>(n);
            for (int i = 0; i < n; i++)
            {
                float sx = w.ArtS[i], ty = w.ArtT[i];
                float off = tune.CTL * PeelW(1f - sx, ty) + tune.CTR * PeelW(sx, ty)
                          + tune.CBR * PeelW(sx, 1f - ty) + tune.CBL * PeelW(1f - sx, 1f - ty);
                verts[i] = w.Rest[i] + w.N * (dir * off * k);
            }
            try
            {
                w.Own.vertices = verts;
                w.Own.RecalculateBounds();
                w.Own.RecalculateNormals();
                try { w.Own.RecalculateTangents(); } catch { }
            }
            catch (Exception e)
            {
                w.GeomFailed = true;
                NewSlotsMod.Log("peel " + w.D.ModName + ": vertex write failed: " + e.Message);
                NewSlotsMod.FlushLog();
                return;
            }
            w.A0 = tune.CTL; w.A1 = tune.CTR; w.A2 = tune.CBR; w.A3 = tune.CBL;
            w.Bent = want;
        }

        internal static float PeelW(float u, float v) { return P(u) * P(v); }
        static float P(float u)
        {
            float x = (u - 0.35f) / 0.65f;
            if (x <= 0f) return 0f;
            if (x >= 1f) return 1f;
            return Mathf.Pow(x, 1.4f);
        }

        static void Fail(Worn w, string why)
        {
            w.GeomFailed = true;
            NewSlotsMod.Log("peel " + w.D.ModName + ": geometry unavailable - " + why);
            NewSlotsMod.FlushLog();
        }

        static Vector3 AxisVec(int axis, float sign)
        {
            var v = axis == 1 ? Vector3.up : (axis == 2 ? Vector3.forward : Vector3.right);
            return sign < 0f ? -v : v;
        }

        static float Curl(float x)
        {
            float u = (x - 0.5f) * 2f;
            return u <= 0f ? 0f : Mathf.Pow(u, 1.5f);
        }

        static bool PrepareGeom(Worn w)
        {
            if (w.GeomFailed) return false;
            try
            {
                var mf = w.T.GetComponentInChildren<MeshFilter>(true);
                if (mf == null) { Fail(w, "no MeshFilter under the worn patch"); return false; }

                bool tallFamily = w.D.DonorContains == null
                               || w.D.DonorContains.IndexOf("GreyFox", StringComparison.OrdinalIgnoreCase) < 0;

                var data = MeshFile.LoadData(tallFamily ? "patchmesh_tall.odgm" : "patchmesh_wide.odgm");
                if (data == null) { Fail(w, "patch mesh file missing from the pack"); return false; }
                var rest = data.Verts;
                var uv = data.UVs;
                int n = rest.Length;
                if (n < 3 || uv == null || uv.Length != n) { Fail(w, "patch mesh data malformed"); return false; }

                float u0, u1, v0, v1;
                if (tallFamily) { u0 = -0.03f; u1 = 0.51f; v0 = -0.03f; v1 = 0.74f; }
                else            { u0 = 0.45f;  u1 = 0.75f; v0 = 0.66f;  v1 = 0.91f; }
                int wa = w.D.WA, ha = w.D.HA, ta = w.D.TA;

                var front = new bool[n]; int cnt = 0;
                for (int i = 0; i < n; i++)
                {
                    var q = uv[i];
                    if (q.x < u0 || q.x > u1 || q.y < v0 || q.y > v1) continue;
                    front[i] = true; cnt++;
                }
                if (cnt < 3) { for (int i = 0; i < n; i++) front[i] = true; cnt = n; }

                double mAx = 0, mAy = 0, mPw = 0, mPh = 0;
                for (int i = 0; i < n; i++)
                {
                    if (!front[i]) continue;
                    var q = uv[i];
                    mAx += tallFamily ? q.y : q.x; mAy += tallFamily ? -q.x : q.y;
                    mPw += rest[i][wa]; mPh += rest[i][ha];
                }
                mAx /= cnt; mAy /= cnt; mPw /= cnt; mPh /= cnt;
                double cxw = 0, cxh = 0, cyw = 0, cyh = 0;
                for (int i = 0; i < n; i++)
                {
                    if (!front[i]) continue;
                    var q = uv[i];
                    double ax = (tallFamily ? q.y : q.x) - mAx, ay = (tallFamily ? -q.x : q.y) - mAy;
                    double pw = rest[i][wa] - mPw, ph = rest[i][ha] - mPh;
                    cxw += ax * pw; cxh += ax * ph; cyw += ay * pw; cyh += ay * ph;
                }
                int sAxis, tAxis; float sSign, tSign;
                if (Math.Abs(cxw) >= Math.Abs(cxh)) { sAxis = wa; sSign = cxw >= 0 ? 1f : -1f; tAxis = ha; tSign = cyh >= 0 ? 1f : -1f; }
                else                                { sAxis = ha; sSign = cxh >= 0 ? 1f : -1f; tAxis = wa; tSign = cyw >= 0 ? 1f : -1f; }

                float sMin = float.MaxValue, sMax = float.MinValue, tMin = float.MaxValue, tMax = float.MinValue;
                for (int i = 0; i < n; i++)
                {
                    float ps = rest[i][sAxis], pt = rest[i][tAxis];
                    if (ps < sMin) sMin = ps; if (ps > sMax) sMax = ps;
                    if (pt < tMin) tMin = pt; if (pt > tMax) tMax = pt;
                }
                float sSpan = Mathf.Max(1e-9f, sMax - sMin), tSpan = Mathf.Max(1e-9f, tMax - tMin);
                var S = new float[n]; var T = new float[n]; var restArr = new Vector3[n];
                for (int i = 0; i < n; i++)
                {
                    restArr[i] = rest[i];
                    float sx = (rest[i][sAxis] - sMin) / sSpan; if (sSign < 0f) sx = 1f - sx;
                    float ty = (rest[i][tAxis] - tMin) / tSpan; if (tSign < 0f) ty = 1f - ty;
                    S[i] = sx; T[i] = ty;
                }

                var own = MeshFile.Build(data, w.D.ModName);
                if (own == null) { Fail(w, "patch mesh build failed"); return false; }
                mf.sharedMesh = own;
                w.Mf = mf; w.Own = own; w.Rest = restArr; w.ArtS = S; w.ArtT = T;
                w.N = ta == 1 ? Vector3.up : (ta == 2 ? Vector3.forward : Vector3.right);

                w.ArtRight = AxisVec(sAxis, sSign);
                w.ArtUp = AxisVec(tAxis, tSign);
                w.HalfS = sSpan * 0.5f; w.HalfT = tSpan * 0.5f;
                double depth = 0; int dc = 0;
                for (int i = 0; i < n; i++) { if (!front[i]) continue; depth += rest[i][ta]; dc++; }
                var centre = Vector3.zero;
                centre[sAxis] = (sMin + sMax) * 0.5f;
                centre[tAxis] = (tMin + tMax) * 0.5f;
                centre[ta] = dc > 0 ? (float)(depth / dc) : 0f;
                w.ArtCentre = centre;
                w.GeomReady = true;
                NewSlotsMod.Log("peel " + w.D.ModName + ": own mesh (" + n + " verts, " + cnt + " front), art right = "
                              + (sSign > 0 ? "+" : "-") + "axis" + sAxis + ", art up = " + (tSign > 0 ? "+" : "-") + "axis" + tAxis
                              + ", normal = axis" + ta);
                NewSlotsMod.FlushLog();
                return true;
            }
            catch (Exception e)
            {
                w.GeomFailed = true;
                NewSlotsMod.Log("peel " + w.D.ModName + ": geometry prep failed: " + e.Message);
                NewSlotsMod.FlushLog();
                return false;
            }
        }

        static void HealTemplates()
        {
            foreach (var d in Catalog.Live)
            {
                if (d.Type != Kind.Garment || d.OwnMats == null) continue;
                for (int i = 0; i < d.OwnMats.Length; i++)
                {
                    var m = d.OwnMats[i];
                    if (m == null) continue;
                    bool dead;
                    try { dead = (m.shader == null); }
                    catch { dead = true; }
                    if (!dead) continue;

                    string want = (d.OwnShaderNames != null && i < d.OwnShaderNames.Length) ? d.OwnShaderNames[i] : null;
                    Shader repl = null;
                    try { if (!string.IsNullOrEmpty(want)) repl = Shader.Find(want); } catch { }
                    bool fixedUp = false;
                    if (repl != null) { try { m.shader = repl; fixedUp = true; } catch { } }

                    if (!d.ShaderDeadLogged)
                    {
                        d.ShaderDeadLogged = true;
                        NewSlotsMod.Log("SHADER DIED under " + d.ModName + " r" + i + " (was \"" + (want ?? "?") + "\") - " +
                                        (fixedUp ? "rebound to a live copy (the pin failed; if it renders off, that copy is a different variant)"
                                                 : "NO live copy exists to rebind to (the pin failed and Shader.Find came back empty)"));
                        NewSlotsMod.FlushLog();
                    }
                }

                if (d.OwnSlots != null)
                    for (int i = 0; i < d.OwnSlots.Length; i++)
                    {
                        var slots = d.OwnSlots[i]; if (slots == null) continue;
                        for (int si = 1; si < slots.Length; si++)
                        {
                            var m = slots[si]; if (m == null) continue;
                            bool dead;
                            try { dead = (m.shader == null); } catch { dead = true; }
                            if (!dead) continue;
                            string want = (d.OwnShaderNames != null && i < d.OwnShaderNames.Length) ? d.OwnShaderNames[i] : null;
                            Shader repl = null;
                            try { if (!string.IsNullOrEmpty(want)) repl = Shader.Find(want); } catch { }
                            if (repl != null) { try { m.shader = repl; } catch { } }
                            if (!d.ShaderDeadLogged)
                            {
                                d.ShaderDeadLogged = true;
                                NewSlotsMod.Log("SHADER DIED under " + d.ModName + " r" + i + " slot " + si + " (was \"" + (want ?? "?") + "\") - " +
                                                (repl != null ? "rebound to a live copy" : "NO live copy exists to rebind to"));
                                NewSlotsMod.FlushLog();
                            }
                        }
                    }
            }
        }

        static void Diagnose(Worn w, Transform t, Tuning.Entry tune, float pscale, Vector3 wanted)
        {
            if (w.Reported) return;
            try
            {

                if (w.HasWritten)
                {
                    Vector3 now = t.localPosition;
                    if ((now - w.LastWrote).sqrMagnitude > 1e-12f)
                    {
                        w.Reported = true;
                        NewSlotsMod.Log("HEIGHT OVERWRITTEN on " + w.D.ModName + ": we wrote " + w.LastWrote +
                                        " and it came back " + now + " - something moves this patch after LateUpdate");
                        NewSlotsMod.FlushLog();
                        return;
                    }
                }

                if (tune.OutMm > -0.05f && tune.OutMm < 0.05f) return;
                w.Reported = true;
                float applied = (tune.OutMm / 1000f) / pscale;
                NewSlotsMod.Log("height " + w.D.ModName + " on " + (w.Garment ?? "?") + " > " + (w.Socket ?? "?") +
                                ": want " + tune.OutMm.ToString("0.0") + "mm, axis=" + w.D.TA +
                                ", parentScale=" + pscale.ToString("0.###") +
                                ", local offset=" + applied.ToString("0.######") +
                                ", rest=" + w.RestPos + " -> " + wanted);
                NewSlotsMod.FlushLog();
            }
            catch { }
        }

        static void Reassert(Worn w)
        {
            var mats = w.D.OwnMats;
            if (mats == null || mats.Length == 0) return;
            try
            {
                var rends = w.T.GetComponentsInChildren<Renderer>(true);
                if (rends == null) return;
                for (int i = 0; i < rends.Length && i < mats.Length; i++)
                {
                    var r = rends[i];
                    var mine = mats[i];
                    if (r == null || mine == null) continue;

                    var slots = (w.D.OwnSlots != null && i < w.D.OwnSlots.Length) ? w.D.OwnSlots[i] : null;
                    if (slots != null && slots.Length > 1)
                    {
                        Il2CppReferenceArray<Material> curs = null;
                        try { curs = r.sharedMaterials; } catch { }
                        bool allSame = curs != null && curs.Length == slots.Length;
                        if (allSame)
                            for (int si = 0; si < slots.Length; si++)
                            {
                                if (slots[si] == null) continue;
                                var c = curs[si];
                                bool eq = false;
                                try { eq = c != null && c.GetInstanceID() == slots[si].GetInstanceID(); } catch { }
                                if (!eq) { allSame = false; break; }
                            }
                        if (allSame) continue;
                        if (!w.D.ShaderWarned)
                        {
                            w.D.ShaderWarned = true;
                            NewSlotsMod.Log("worn " + w.D.ModName + " r" + i + " (" + slots.Length + " slots) was not on ours -> put back on all " + slots.Length);
                            NewSlotsMod.FlushLog();
                        }
                        var arr = new Il2CppReferenceArray<Material>(slots.Length);
                        for (int si = 0; si < slots.Length; si++)
                            arr[si] = slots[si] != null ? slots[si] : (curs != null && si < curs.Length ? curs[si] : null);
                        try { r.sharedMaterials = arr; } catch (Exception e) { NewSlotsMod.Log("reassert slots " + w.D.ModName + ": " + e.Message); }
                        continue;
                    }

                    var cur = r.sharedMaterial;
                    bool same = false;
                    try { same = (cur != null) && cur.GetInstanceID() == mine.GetInstanceID(); }
                    catch { }
                    if (same) continue;

                    if (!w.D.ShaderWarned)
                    {
                        w.D.ShaderWarned = true;
                        NewSlotsMod.Log("worn " + w.D.ModName + " r" + i + " was on " + Describe(cur) +
                                        "  ->  put on ours " + Describe(mine));
                        NewSlotsMod.FlushLog();
                    }
                    r.sharedMaterial = mine;
                }
            }
            catch { }
        }

        static string Describe(UnityEngine.Material m)
        {
            try
            {
                if (m == null) return "NULL material";
                string sh = "?";
                try { sh = m.shader == null ? "NULL shader" : m.shader.name + (m.shader.isSupported ? "" : " UNSUPPORTED"); }
                catch { }
                string bc = "";
                try
                {
                    foreach (var p in new[] { "_BaseColorMap", "_MainTex", "_BaseMap" })
                        if (m.HasProperty(p)) { var t = m.GetTexture(p); bc = " " + p + "=" + (t == null ? "NULL" : t.name); break; }
                }
                catch { }
                return "\"" + m.name + "\" [" + sh + "]" + bc;
            }
            catch { return "(unreadable)"; }
        }

        internal static int Count { get { return _worn.Count; } }
    }
}
