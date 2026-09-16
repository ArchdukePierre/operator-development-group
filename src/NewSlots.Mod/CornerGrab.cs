// ODG Operations - NewSlots
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using UnityEngine;

namespace NewSlots
{

    internal static class CornerGrab
    {
        const float GRAB_PX = 22f;
        const float MAX_MM = 12f;
        const float SMOOTH = 22f;

        static readonly List<Seat.PatchEdges> _frames = new List<Seat.PatchEdges>();
        static Camera _cam;
        static int _camAge = 999;
        static int _hoverPatch = -1, _hoverCorner = -1;

        static bool _dragging;
        static int _dragCorner;
        static Vector3 _g0, _liftW;
        static float _t0, _b0, _value, _target, _lastWritten;
        static string _dGarment, _dSocket, _dName;
        static Transform _dT;
        static bool _warned, _announced;

        internal static bool Dragging { get { return _dragging; } }
        static readonly string[] CornerName = { "top-left", "top-right", "bottom-right", "bottom-left" };

        internal static void Tick()
        {
            try
            {

                if (!Ui.IsOpenNow) { Cancel(); return; }
                var cam = FindCamera();
                Vector2 mp = Input.mousePosition;

                if (_dragging) { if (cam != null) Drag(cam, mp); else EndDrag(); return; }

                _frames.Clear();
                Seat.CollectLocalPatchEdges(_frames);
                if (!_announced)
                {

                    _announced = true;
                    NewSlotsMod.Log("corner grab: camera " + (cam == null ? "NONE" : cam.name) + ", " +
                                    _frames.Count + " worn patch(es) with geometry");
                    NewSlotsMod.FlushLog();
                }
                _hoverPatch = _hoverCorner = -1;
                if (cam == null || _frames.Count == 0) return;
                if (NativeUi.PointerOver(mp) || Ui.PointerOver(mp)) return;

                float best = GRAB_PX * UiScale();
                for (int p = 0; p < _frames.Count; p++)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        Vector2 c;
                        if (!CornerOnScreen(cam, _frames[p], k, out c)) continue;
                        float d = (mp - c).magnitude;
                        if (d < best) { best = d; _hoverPatch = p; _hoverCorner = k; }
                    }
                }
                if (_hoverPatch >= 0 && Input.GetMouseButtonDown(0)) BeginDrag(cam, mp);
            }
            catch (Exception e)
            {
                Cancel();
                if (!_warned)
                {
                    _warned = true;
                    NewSlotsMod.Log("corner grab: " + e.GetType().Name + ": " + e.Message);
                    NewSlotsMod.FlushLog();
                }
            }
        }

        static void BeginDrag(Camera cam, Vector2 mp)
        {
            var f = _frames[_hoverPatch];
            if (f.Garment == null) return;
            _dragCorner = _hoverCorner;
            _dT = f.T;
            _g0 = f.T.TransformPoint(f.Corners[_dragCorner]);
            _liftW = f.T.TransformDirection(f.Lift).normalized;
            float t;
            if (!Solve(cam, mp, out t)) return;
            _t0 = t;
            var tune = Tuning.Get(f.Garment, f.Socket, f.D.ModName);
            _b0 = _value = _target = _lastWritten = tune.Corner(_dragCorner);
            _dGarment = f.Garment; _dSocket = f.Socket; _dName = f.D.ModName;
            _dragging = true;
        }

        static void Drag(Camera cam, Vector2 mp)
        {
            if (!Input.GetMouseButton(0)) { EndDrag(); return; }
            float t;
            if (Solve(cam, mp, out t))
                _target = Mathf.Clamp(_b0 + (t - _t0) * 1000f, -MAX_MM, MAX_MM);
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * SMOOTH);
            _value = Mathf.Lerp(_value, _target, k);
            if (Mathf.Abs(_value - _lastWritten) > 0.02f) Write(_value);
        }

        static void EndDrag()
        {
            float final = Mathf.Clamp(Mathf.Round(_target * 10f) / 10f, -MAX_MM, MAX_MM);
            Write(final);
            _dragging = false;
            bool saved = false;
            try { saved = Tuning.Save(); } catch { }
            if (saved) Ui.DirtyPublic = false;
            NewSlotsMod.Log("corner grab: " + _dName + " " + CornerName[_dragCorner] + " -> " + final.ToString("0.0") + " mm" +
                            (saved ? " (saved)" : " (NOT saved - use SAVE)"));
            NewSlotsMod.FlushLog();
        }

        static void Write(float v)
        {
            var tune = Tuning.Get(_dGarment, _dSocket, _dName);
            float[] c = { tune.CTL, tune.CTR, tune.CBR, tune.CBL };
            c[_dragCorner] = v;
            Tuning.SetCorners(_dGarment, _dSocket, _dName, c[0], c[1], c[2], c[3]);
            Ui.DirtyPublic = true;
            _lastWritten = v;
        }

        internal static void Cancel()
        {
            _dragging = false;
            _announced = false;
            _hoverPatch = _hoverCorner = -1;
            _frames.Clear();
        }

        static bool Solve(Camera cam, Vector2 mp, out float t)
        {
            t = 0f;
            var ray = cam.ScreenPointToRay(new Vector3(mp.x, mp.y, 0f));
            Vector3 P = ray.origin, D = ray.direction.normalized, L = _liftW;
            Vector3 w0 = P - _g0;
            float b = Vector3.Dot(D, L), d = Vector3.Dot(D, w0), e = Vector3.Dot(L, w0);
            float denom = 1f - b * b;
            if (denom < 1e-3f) return false;
            t = (e - b * d) / denom;
            return true;
        }

        static bool CornerOnScreen(Camera cam, Seat.PatchEdges f, int k, out Vector2 c)
        {
            c = Vector2.zero;
            var sp = cam.WorldToScreenPoint(f.T.TransformPoint(f.Corners[k]));
            if (sp.z <= 0f) return false;
            c = new Vector2(sp.x, sp.y);
            return true;
        }

        static float UiScale() { return Mathf.Clamp(Screen.height / 1080f, 0.75f, 2.5f); }

        static Camera FindCamera()
        {
            if (_cam != null && _camAge < 30)
            {
                _camAge++;
                try { if (_cam.enabled) return _cam; } catch { }
            }
            _camAge = 0; _cam = null;
            try
            {
                var ctl = Ui.Controller;
                if (ctl != null)
                {
                    var go = ctl._camera;
                    if (go != null)
                    {
                        var c = go.GetComponent<Camera>();
                        if (c == null) c = go.GetComponentInChildren<Camera>(true);
                        if (c != null && c.enabled) _cam = c;
                    }
                }
            }
            catch { }
            if (_cam == null) { try { _cam = Camera.main; } catch { } }
            return _cam;
        }

        static Texture2D _white;
        static GUIStyle _label;
        static readonly int[,] Edges = { { 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 0 } };

        internal static void Draw()
        {
            if (!Ui.IsOpenNow) return;
            if (_frames.Count == 0 && !_dragging) return;
            var cam = _cam; if (cam == null) return;
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            if (_white == null)
            {
                _white = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                _white.SetPixel(0, 0, Color.white); _white.Apply();
                _label = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
                _label.normal.textColor = Color.white;
            }
            float s = UiScale();
            var dim = new Color(1f, 1f, 1f, 0.30f);
            var hot = new Color(1f, 0.62f, 0.12f, 0.95f);
            var live = new Color(0.35f, 0.9f, 1f, 1f);

            for (int p = 0; p < _frames.Count; p++)
            {
                var f = _frames[p];
                var sc = new Vector2[4]; var ok = new bool[4];
                for (int k = 0; k < 4; k++) ok[k] = CornerOnScreen(cam, f, k, out sc[k]);
                for (int k = 0; k < 4; k++)
                    if (ok[Edges[k, 0]] && ok[Edges[k, 1]]) Line(sc[Edges[k, 0]], sc[Edges[k, 1]], 1.5f * s, dim);
                for (int k = 0; k < 4; k++)
                {
                    if (!ok[k]) continue;
                    bool isHot = !_dragging && p == _hoverPatch && k == _hoverCorner;
                    bool isLive = _dragging && f.T == _dT && k == _dragCorner;
                    Dot(sc[k], (isHot || isLive ? 8f : 4.5f) * s, isLive ? live : (isHot ? hot : dim));
                    if (isHot || isLive)
                    {
                        string txt = isLive ? (_value >= 0 ? "+" : "") + _value.ToString("0.0") + " mm"
                                            : CornerName[k] + ": pull out to peel, push in to tuck";
                        Tag(sc[k] + new Vector2(14f * s, 14f * s), txt);
                    }
                }
            }
        }

        static Vector2 Gui(Vector2 screen) { return new Vector2(screen.x, Screen.height - screen.y); }

        static void Line(Vector2 a, Vector2 b, float width, Color c)
        {
            a = Gui(a); b = Gui(b);
            var save = GUI.matrix; var col = GUI.color;
            GUI.color = c;
            float len = (b - a).magnitude;
            float ang = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
            GUIUtility.RotateAroundPivot(ang, a);
            GUI.DrawTexture(new Rect(a.x, a.y - width * 0.5f, len, width), _white);
            GUI.matrix = save; GUI.color = col;
        }

        static void Dot(Vector2 centre, float r, Color c)
        {
            var g = Gui(centre); var col = GUI.color; GUI.color = c;
            GUI.DrawTexture(new Rect(g.x - r, g.y - r, 2f * r, 2f * r), _white);
            GUI.color = col;
        }

        static void Tag(Vector2 at, string text)
        {
            var g = Gui(at);
            var size = _label.CalcSize(new GUIContent(text));
            var r = new Rect(g.x, g.y, size.x + 10f, size.y + 4f);
            var col = GUI.color; GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.DrawTexture(r, _white); GUI.color = col;
            GUI.Label(new Rect(r.x + 5f, r.y + 2f, size.x, size.y), text, _label);
        }
    }
}
