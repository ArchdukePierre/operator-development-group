// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace MapLoader
{

    internal sealed class FastRope
    {
        public const float Length = 17f;
        public const float Radius = 0.021f;
        const int Points = 44;
        const int Ring = 8;
        const float Gravity = 9.81f;
        const float Damping = 0.996f;
        const int Iterations = 10;
        const float BraidPitchM = 0.24f;
        const float RetractS = 1.1f;

        public enum State { Stowed, Hanging, Retracting, Released, Settled }
        public State Mode { get; private set; }
        public float DeployedAt { get; private set; } = -1f;
        public bool Deployed { get { return Mode == State.Hanging; } }
        public int Side { get; private set; }

        readonly Vector3[] _p = new Vector3[Points];
        readonly Vector3[] _pp = new Vector3[Points];
        readonly Vector3[] _from = new Vector3[Points];
        readonly Vector3[] _verts = new Vector3[Points * Ring];
        readonly Vector3[] _norms = new Vector3[Points * Ring];
        readonly float _seg = Length / (Points - 1);
        GameObject _go, _top;
        Mesh _mesh;
        Transform _anchor, _stow, _airframe;
        float _deckY, _padX, _padZ, _releasedAt, _retractAt, _accum;
        Vector3 _lastNormal = Vector3.right;
        MelonLogger.Instance _log;

        public FastRope(MelonLogger.Instance log) { _log = log; }

        public void Build(Transform airframe, Transform anchor, Transform stow, int side, Vector3 deck, string texDir)
        {
            _airframe = airframe; _anchor = anchor; _stow = stow; Side = side; _deckY = deck.y; _padX = deck.x; _padZ = deck.z;
            _go = new GameObject("ODG_FastRope_" + (side > 0 ? "R" : "L"));
            _mesh = new Mesh(); _mesh.name = "fast_rope"; _mesh.MarkDynamic();
            var uvs = new Vector2[Points * Ring];
            var tris = new int[(Points - 1) * Ring * 6];
            int t = 0;
            for (int i = 0; i < Points; i++)
                for (int k = 0; k < Ring; k++)
                {
                    uvs[i * Ring + k] = new Vector2(k / (float)Ring, i * _seg / BraidPitchM);
                    if (i < Points - 1)
                    {
                        int a = i * Ring + k, b = i * Ring + (k + 1) % Ring, c = (i + 1) * Ring + k, d = (i + 1) * Ring + (k + 1) % Ring;
                        tris[t++] = a; tris[t++] = c; tris[t++] = b;
                        tris[t++] = b; tris[t++] = c; tris[t++] = d;
                    }
                }
            Stow(); WriteMesh();
            _mesh.vertices = _verts; _mesh.normals = _norms; _mesh.uv = uvs; _mesh.triangles = tris; _mesh.RecalculateBounds();
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var mr = _go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Mat(texDir, "rope", "rope_albedo.png", "rope_normal.png", "rope_mask.png");
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            BuildTop(texDir);
            Mode = State.Stowed;
            _log?.Msg("[rope] " + (side > 0 ? "right" : "left") + " fast rope built: " + Length.ToString("0") + " m, " + Points + " points, deck at y=" + _deckY.ToString("0.00"));
        }

        static Material Mat(string texDir, string name, string alb, string nrm, string mask)
        {
            try
            {
                var def = new MapBundle.MaterialDef { Name = name, Albedo = alb, Normal = nrm, Mask = mask, BaseColor = new float[] { 1f, 1f, 1f, 1f } };
                return MeshFactory.BuildMaterial(def, texDir, false, name);
            }
            catch { return new Material(MeshFactory.GetShader()); }
        }

        void BuildTop(string texDir)
        {
            _top = new GameObject("rope_top"); _top.transform.SetParent(_go.transform, false);
            var metal = Mat(texDir, "rope_metal", "metal_albedo.png", "metal_normal.png", "metal_mask.png");

            var ferrule = new List<Vector3>(); for (int i = 0; i <= 4; i++) ferrule.Add(new Vector3(0f, 0.02f - 0.045f * i, 0f));
            Part("ferrule", Tube(ferrule, 0.036f, 12, false, true), metal);
            var ring = new List<Vector3>(); for (int i = 0; i < 28; i++) { float a = i / 28f * Mathf.PI * 2f; ring.Add(new Vector3(Mathf.Cos(a) * 0.055f, 0.085f + Mathf.Sin(a) * 0.055f, 0f)); }
            Part("ring", Tube(ring, 0.009f, 8, true, false), metal);
            var wire = new List<Vector3>(); for (int i = 0; i < 30; i++) { float a = i / 30f * Mathf.PI * 2f; wire.Add(new Vector3(0.06f + Mathf.Sin(a) * 0.05f, 0.03f - 0.16f * (0.5f - 0.5f * Mathf.Cos(a)), 0.02f * Mathf.Sin(a * 0.5f))); }
            Part("wire", Tube(wire, 0.003f, 6, true, false), metal);
        }

        void Part(string name, Mesh m, Material mat)
        {
            var go = new GameObject(name); go.transform.SetParent(_top.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
        }

        public static Mesh Tube(List<Vector3> path, float radius, int ring, bool closed, bool caps)
        {
            int n = path.Count;
            var v = new List<Vector3>(); var nn = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            Vector3 lastN = Vector3.right; float dist = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector3 tan = (i < n - 1 ? path[i + 1] - path[i] : (closed ? path[0] - path[i] : path[i] - path[i - 1])).normalized;
                if (i > 0) dist += Vector3.Distance(path[i], path[i - 1]);
                Vector3 nrm = Vector3.ProjectOnPlane(lastN, tan); if (nrm.sqrMagnitude < 1e-6f) nrm = Vector3.ProjectOnPlane(Mathf.Abs(tan.y) < 0.9f ? Vector3.up : Vector3.right, tan);
                nrm.Normalize(); lastN = nrm; Vector3 bi = Vector3.Cross(tan, nrm);
                for (int k = 0; k < ring; k++)
                {
                    float a = k / (float)ring * Mathf.PI * 2f; Vector3 d = nrm * Mathf.Cos(a) + bi * Mathf.Sin(a);
                    v.Add(path[i] + d * radius); nn.Add(d); uv.Add(new Vector2(k / (float)ring, dist / (radius * 6f)));
                }
            }
            int segs = closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
                for (int k = 0; k < ring; k++)
                {
                    int i2 = (i + 1) % n;
                    int a = i * ring + k, b = i * ring + (k + 1) % ring, c = i2 * ring + k, d = i2 * ring + (k + 1) % ring;
                    tri.Add(a); tri.Add(c); tri.Add(b); tri.Add(b); tri.Add(c); tri.Add(d);
                }
            if (caps && !closed)
            {
                for (int e = 0; e < 2; e++)
                {
                    int baseIdx = e == 0 ? 0 : (n - 1) * ring; Vector3 centre = path[e == 0 ? 0 : n - 1];
                    Vector3 tan = (path[1] - path[0]).normalized * (e == 0 ? -1f : 1f);
                    int ci = v.Count; v.Add(centre); nn.Add(tan); uv.Add(new Vector2(0.5f, 0.5f));
                    for (int k = 0; k < ring; k++)
                    {
                        int a = baseIdx + k, b = baseIdx + (k + 1) % ring;
                        if (e == 0) { tri.Add(ci); tri.Add(a); tri.Add(b); } else { tri.Add(ci); tri.Add(b); tri.Add(a); }
                    }
                }
            }
            var m = new Mesh(); m.vertices = v.ToArray(); m.normals = nn.ToArray(); m.uv = uv.ToArray(); m.triangles = tri.ToArray(); m.RecalculateBounds();
            return m;
        }

        void CoilPose(Vector3[] into)
        {
            Vector3 c = _stow != null ? _stow.position : (_anchor != null ? _anchor.position - Vector3.up * 1.3f : Vector3.zero);
            Vector3 fwd = _airframe != null ? _airframe.forward : Vector3.forward, right = _airframe != null ? _airframe.right : Vector3.right;
            for (int i = 0; i < Points; i++)
            {
                float a = i * 0.62f; float r = 0.16f + 0.19f * ((i * 7) % 3) / 2f;
                into[i] = c + right * (Mathf.Cos(a) * r) + fwd * (Mathf.Sin(a) * r) + Vector3.up * (0.02f + 0.021f * (i % 4));
            }
            if (_anchor != null) into[0] = _anchor.position;
        }

        void Stow()
        {
            CoilPose(_p);
            for (int i = 0; i < Points; i++) _pp[i] = _p[i];
        }

        public void Deploy()
        {
            if (Mode != State.Stowed) return;
            Mode = State.Hanging; DeployedAt = Time.realtimeSinceStartup;
            Vector3 outward = (_airframe != null ? _airframe.right : Vector3.right) * Side;
            Vector3 fwd = _airframe != null ? _airframe.forward : Vector3.forward;
            for (int i = 1; i < Points; i++) _pp[i] = _p[i] - (outward * 2.6f + Vector3.down * 1.4f + fwd * (0.3f * Mathf.Sin(i))) * (1f / 60f);
            _log?.Msg("[rope] " + (Side > 0 ? "right" : "left") + " rope out the door");
        }

        public void Retract()
        {
            if (Mode != State.Hanging) return;
            Mode = State.Retracting; _retractAt = Time.realtimeSinceStartup;
            for (int i = 0; i < Points; i++) _from[i] = _p[i];
            _log?.Msg("[rope] " + (Side > 0 ? "right" : "left") + " rope coming back in");
        }

        public void Release()
        {
            if (Mode != State.Hanging) return;
            Mode = State.Released; _releasedAt = Time.realtimeSinceStartup;
            _log?.Msg("[rope] rope cut loose");
        }

        public void Tick(float dt)
        {
            if (_go == null) return;
            if (Mode == State.Stowed) { Stow(); WriteMesh(); Push(); return; }
            if (Mode == State.Settled) return;
            if (Mode == State.Retracting)
            {
                float u = Mathf.Clamp01((Time.realtimeSinceStartup - _retractAt) / RetractS);
                float e = u * u * (3f - 2f * u);
                var coil = new Vector3[Points]; CoilPose(coil);
                for (int i = 0; i < Points; i++) { _p[i] = Vector3.Lerp(_from[i], coil[i], e); _pp[i] = _p[i]; }
                if (u >= 1f) { Mode = State.Stowed; _log?.Msg("[rope] " + (Side > 0 ? "right" : "left") + " rope stowed"); }
                WriteMesh(); Push(); return;
            }
            _accum += Mathf.Min(dt, 0.05f);
            const float h = 1f / 120f;
            int steps = 0;
            while (_accum >= h && steps < 8) { Step(h); _accum -= h; steps++; }
            if (Mode == State.Released && Time.realtimeSinceStartup - _releasedAt > 7f) { Mode = State.Settled; }
            WriteMesh(); Push();
        }

        void Step(float h)
        {
            bool topPinned = Mode == State.Hanging;
            if (topPinned && _anchor != null) { _p[0] = _anchor.position; _pp[0] = _p[0]; }
            Vector3 g = Vector3.down * Gravity * h * h;
            for (int i = topPinned ? 1 : 0; i < Points; i++)
            {
                Vector3 v = (_p[i] - _pp[i]) * Damping;
                _pp[i] = _p[i];
                _p[i] += v + g;
            }
            for (int it = 0; it < Iterations; it++)
            {
                for (int i = 0; i < Points - 1; i++)
                {
                    Vector3 d = _p[i + 1] - _p[i]; float len = d.magnitude; if (len < 1e-5f) continue;
                    Vector3 corr = d * ((len - _seg) / len);
                    if (i == 0 && topPinned) _p[1] -= corr;
                    else { _p[i] += corr * 0.5f; _p[i + 1] -= corr * 0.5f; }
                }

                for (int i = topPinned ? 1 : 0; i < Points; i++)
                {
                    float floor = _deckY + Radius;
                    if (_p[i].y < floor && Mathf.Abs(_p[i].x - _padX) < 16f && Mathf.Abs(_p[i].z - _padZ) < 16f)
                    {
                        _p[i].y = floor;
                        _pp[i].x = Mathf.Lerp(_pp[i].x, _p[i].x, 0.5f); _pp[i].z = Mathf.Lerp(_pp[i].z, _p[i].z, 0.5f);
                    }
                }
            }
        }

        public void PointAt(float arc, out Vector3 pos, out Vector3 down)
        {
            float f = Mathf.Clamp(arc / _seg, 0f, Points - 1.001f);
            int i = (int)f; float u = f - i;
            pos = Vector3.Lerp(_p[i], _p[i + 1], u);
            down = (_p[i + 1] - _p[i]).normalized;
        }

        public float HangLength
        {
            get
            {
                for (int i = 1; i < Points; i++) if (_p[i].y <= _deckY + 0.35f) return i * _seg;
                return Length;
            }
        }

        public Vector3 Bottom { get { float a = HangLength; Vector3 p, d; PointAt(a, out p, out d); return p; } }
        public Vector3 Top { get { return _p[0]; } }

        void WriteMesh()
        {
            Vector3 lastN = _lastNormal;
            for (int i = 0; i < Points; i++)
            {
                Vector3 tan = (i < Points - 1 ? _p[i + 1] - _p[i] : _p[i] - _p[i - 1]);
                if (tan.sqrMagnitude < 1e-8f) tan = Vector3.down; tan.Normalize();
                Vector3 nrm = Vector3.ProjectOnPlane(lastN, tan);
                if (nrm.sqrMagnitude < 1e-6f) nrm = Vector3.ProjectOnPlane(Mathf.Abs(tan.y) < 0.9f ? Vector3.up : Vector3.right, tan);
                nrm.Normalize(); if (i == 0) _lastNormal = nrm; lastN = nrm;
                Vector3 bi = Vector3.Cross(tan, nrm);
                for (int k = 0; k < Ring; k++)
                {
                    float a = k / (float)Ring * Mathf.PI * 2f; Vector3 d = nrm * Mathf.Cos(a) + bi * Mathf.Sin(a);
                    _verts[i * Ring + k] = _p[i] + d * Radius; _norms[i * Ring + k] = d;
                }
            }
            if (_top != null)
            {
                Vector3 dir = (_p[1] - _p[0]); if (dir.sqrMagnitude < 1e-6f) dir = Vector3.down;
                _top.transform.position = _p[0];
                _top.transform.rotation = Quaternion.FromToRotation(Vector3.down, dir.normalized);
            }
        }

        void Push()
        {
            try { _mesh.vertices = _verts; _mesh.normals = _norms; _mesh.RecalculateBounds(); } catch { }
        }

        public void Destroy()
        {
            try { if (_go != null) UnityEngine.Object.Destroy(_go); } catch { }
            _go = null; _top = null; _mesh = null;
        }
    }
}
