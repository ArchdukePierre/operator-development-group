// ODG Operations - NewSlots
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using UnityEngine;

namespace NewSlots
{

    internal static class Ui
    {
        internal static bool CabinetOpen;
        internal static Il2Cpp.CharacterModParent CurrentSocket;
        internal static Def LastSelected;

        internal static Il2Cpp.CharacterCustomisationController Controller;
        static bool _sawOpen;

        static Def _target;
        static bool _dirty;
        static float _manualUntil;

        internal static Def Target { get { return _target; } }
        internal static bool DirtyPublic { get { return _dirty; } set { _dirty = value; } }
        internal static bool Minimised { get { return _minimised; } set { _minimised = value; } }
        internal static bool StillOpenPublic() { return StillOpen(); }
        internal static List<Def> TunablePublic() { return Tunable(); }

        internal static void AdvanceTarget(List<Def> tunable)
        {
            var pick = PickTarget(tunable);
            bool manual = Time.realtimeSinceStartup < _manualUntil;
            if (pick != null && (!manual || !tunable.Contains(_target))) _target = pick;
            if (_target == null || !tunable.Contains(_target)) _target = pick ?? (tunable.Count > 0 ? tunable[0] : null);
        }

        internal static void SelectManual(Def d)
        {
            if (d == null) return;
            _target = d;
            LastSelected = d;
            _manualUntil = Time.realtimeSinceStartup + 8f;
        }

        static Vector2 _pos = new Vector2(float.NaN, float.NaN);
        static bool _minimised;
        static bool _dragging;
        static Vector2 _dragOff;
        static bool _placementDirty;
        static float _saveAt;
        static bool _styled;
        static GUIStyle _sBox, _sHead, _sName, _sLabel, _sBtn, _sSave, _sRight, _sDim;
        static Texture2D _texPanel, _texBtn, _texAccent, _texHandle;
        const float EDGES_H = 40f;
        static int _grab = -1;
        static float _grabY0, _grabV0;

        static Texture2D Solid(float r, float g, float b, float a)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.SetPixel(0, 0, new Color(r, g, b, a));
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }

        static void EnsureStyles()
        {
            if (_styled) return;
            _texPanel = Solid(0.045f, 0.045f, 0.055f, 0.92f);
            _texBtn = Solid(0.16f, 0.17f, 0.19f, 1f);
            _texAccent = Solid(0.85f, 0.62f, 0.16f, 1f);
            _texHandle = Solid(0.50f, 0.54f, 0.58f, 1f);

            _sBox = new GUIStyle(GUI.skin.box) { padding = new RectOffset(12, 12, 8, 8) };
            _sBox.normal.background = _texPanel;

            _sHead = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold };
            _sHead.normal.textColor = new Color(0.85f, 0.62f, 0.16f);

            _sName = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _sName.normal.textColor = Color.white;

            _sLabel = new GUIStyle(GUI.skin.label) { fontSize = 11 };
            _sLabel.normal.textColor = new Color(0.78f, 0.80f, 0.83f);

            _sBtn = new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Bold };
            _sBtn.normal.background = _texBtn;
            _sBtn.hover.background = _texAccent;
            _sBtn.normal.textColor = Color.white;
            _sBtn.hover.textColor = Color.black;

            _sSave = new GUIStyle(_sBtn);
            _sSave.normal.background = _texAccent;
            _sSave.normal.textColor = Color.black;

            _sRight = new GUIStyle(_sLabel) { alignment = TextAnchor.MiddleRight };
            _sRight.normal.textColor = new Color(0.92f, 0.86f, 0.70f);

            _sDim = new GUIStyle(_sLabel) { fontSize = 10 };
            _sDim.normal.textColor = new Color(0.58f, 0.60f, 0.63f);

            _sName.clipping = TextClipping.Clip;
            _sLabel.clipping = TextClipping.Clip;
            _sDim.clipping = TextClipping.Clip;

            _styled = true;
        }

        static List<Def> Tunable()
        {
            var list = new List<Def>();
            foreach (var d in Catalog.Live)
                if (d.Type == Kind.Patch) list.Add(d);
            return list;
        }

        static Def PickTarget(List<Def> tunable)
        {
            try
            {
                var sock = CurrentSocket;
                if (sock != null)
                {
                    var cur = sock.currentMod;
                    if (cur != null)
                    {
                        int ari = cur.AssetReferenceIndex;
                        foreach (var d in tunable)
                            if (d.Index == ari) return d;
                    }
                }
            }
            catch { }
            if (LastSelected != null && tunable.Contains(LastSelected)) return LastSelected;
            return tunable.Count > 0 ? tunable[0] : null;
        }

        internal static bool IsOpenNow { get { return CabinetOpen && StillOpen(); } }

        static bool StillOpen()
        {
            try
            {
                var c = Controller;
                if (c == null) return false;
                bool live = c.isLocalCustomising;
                if (live) { _sawOpen = true; return true; }
                return !_sawOpen;
            }
            catch { return false; }
        }

        internal static void Close()
        {
            CabinetOpen = false;
            CurrentSocket = null;
            Controller = null;
            _sawOpen = false;
            _dragging = false;
            FlushPlacement();
        }

        internal static void LoadPlacement(float x, float y, bool minimised)
        {
            _pos = new Vector2(x, y);
            _minimised = minimised;
        }

        static void FlushPlacement()
        {
            if (!_placementDirty) return;
            _placementDirty = false;
            try { NewSlotsMod.SavePanelPlacement(_pos.x, _pos.y, _minimised); } catch { }
        }

        internal static void Draw()
        {
            if (!CabinetOpen) return;
            if (!StillOpen()) { Close(); return; }
            var tunable = Tunable();
            if (tunable.Count == 0) return;

            EnsureStyles();

            AdvanceTarget(tunable);
            if (_target == null) return;

            const float W = 360f, PAD = 14f, GAP = 8f;
            const float ROW = 18f, BTN = 24f, SLIDER = 16f;
            float IW = W - PAD * 2f;

            string patchName = _target == null ? "-" : (_target.Display ?? _target.ModName);
            string garment, socket;
            Hooks.SocketIdentity(CurrentSocket, out garment, out socket);
            bool live = CurrentSocket != null && garment != null;
            string where = live ? garment + "  >  " + socket : "open a patch slot to place it";

            float full = PAD + ROW + GAP + BTN + 4f + ROW + GAP
                       + (ROW + SLIDER + GAP) * 2f + ROW + EDGES_H + GAP + GAP + BTN + 4f + ROW + PAD;
            float headerOnly = PAD + ROW + PAD;
            float h = _minimised ? headerOnly : full;

            if (float.IsNaN(_pos.x)) _pos = new Vector2(Screen.width - W - 14f, 170f);
            _pos.x = Mathf.Clamp(_pos.x, 0f, Mathf.Max(0f, Screen.width - W));
            _pos.y = Mathf.Clamp(_pos.y, 0f, Mathf.Max(0f, Screen.height - h));

            var r = new Rect(_pos.x, _pos.y, W, h);
            _lastRect = r;
            GUI.Box(r, GUIContent.none, _sBox);
            GUI.DrawTexture(new Rect(r.x, r.y, 3f, r.height), _texAccent);

            float x = r.x + PAD, y = r.y + PAD;

            int idx = Mathf.Max(0, tunable.IndexOf(_target));
            var minBtn = new Rect(x + IW - 22f, y - 2f, 22f, ROW + 4f);
            GUI.Label(new Rect(x, y, IW - 90f, ROW), "PATCH ADJUSTER", _sHead);
            if (!_minimised)
                GUI.Label(new Rect(x + IW - 86f, y, 60f, ROW), (idx + 1) + " / " + tunable.Count, _sRight);

            if (GUI.Button(minBtn, _minimised ? "+" : "–", _sBtn))
            {
                _minimised = !_minimised;
                _placementDirty = true;
                _saveAt = Time.realtimeSinceStartup + 0.5f;
            }

            HandleDrag(new Rect(r.x, r.y, W - 30f, ROW + PAD));

            if (_placementDirty && !_dragging && Time.realtimeSinceStartup >= _saveAt) FlushPlacement();

            if (_minimised) return;

            y += ROW + GAP;

            if (GUI.Button(new Rect(x, y, 28f, BTN), "<", _sBtn))
                SelectManual(tunable[(idx - 1 + tunable.Count) % tunable.Count]);
            if (GUI.Button(new Rect(x + IW - 28f, y, 28f, BTN), ">", _sBtn))
                SelectManual(tunable[(idx + 1) % tunable.Count]);
            var nameBox = new Rect(x + 32f, y, IW - 64f, BTN);
            GUI.Label(nameBox, Fit(patchName, _sName, nameBox.width), _sName);
            y += BTN + 4f;

            var whereBox = new Rect(x, y, IW, ROW);
            GUI.Label(whereBox, Fit(where, _sLabel, whereBox.width), live ? _sLabel : _sDim);
            y += ROW + GAP;

            var t = Tuning.Get(garment, socket, _target.ModName);
            float scale = t.Scale, outMm = t.OutMm;

            GUI.Label(new Rect(x, y, IW - 70f, ROW), "SIZE", _sLabel);
            GUI.Label(new Rect(x + IW - 70f, y, 70f, ROW), Mathf.RoundToInt(scale * 100f) + " %", _sRight);
            y += ROW;
            float ns = GUI.HorizontalSlider(new Rect(x, y + 2f, IW - 62f, SLIDER), scale, 0.5f, 2.5f);
            if (GUI.Button(new Rect(x + IW - 58f, y - 2f, 26f, 22f), "-", _sBtn)) ns = scale - 0.05f;
            if (GUI.Button(new Rect(x + IW - 28f, y - 2f, 28f, 22f), "+", _sBtn)) ns = scale + 0.05f;
            y += SLIDER + GAP;

            GUI.Label(new Rect(x, y, IW - 70f, ROW), "HEIGHT", _sLabel);
            GUI.Label(new Rect(x + IW - 70f, y, 70f, ROW), outMm.ToString("0.0") + " mm", _sRight);
            y += ROW;
            float no = GUI.HorizontalSlider(new Rect(x, y + 2f, IW - 62f, SLIDER), outMm, -10f, 10f);
            if (GUI.Button(new Rect(x + IW - 58f, y - 2f, 26f, 22f), "-", _sBtn)) no = outMm - 0.2f;
            if (GUI.Button(new Rect(x + IW - 28f, y - 2f, 28f, 22f), "+", _sBtn)) no = outMm + 0.2f;
            y += SLIDER + GAP;

            ns = Mathf.Clamp(Mathf.Round(ns * 100f) / 100f, 0.5f, 2.5f);
            no = Mathf.Clamp(Mathf.Round(no * 10f) / 10f, -10f, 10f);
            if (Mathf.Abs(ns - scale) > 0.0001f || Mathf.Abs(no - outMm) > 0.0001f)
            {
                Tuning.Set(garment, socket, _target.ModName, ns, no);
                ApplyLive(_target, ns, no);
                _dirty = true;
            }

            GUI.Label(new Rect(x, y, IW, ROW), "CORNERS   drag a corner of the patch on your character to peel it", _sLabel);
            y += ROW;
            string cv = "TL " + Fmt(t.CTL) + "   TR " + Fmt(t.CTR) + "   BR " + Fmt(t.CBR) + "   BL " + Fmt(t.CBL) + "  mm";
            GUI.Label(new Rect(x, y + 4f, IW - 100f, ROW), cv, _sDim);
            if (GUI.Button(new Rect(x + IW - 92f, y, 92f, BTN), "FLATTEN", _sBtn) && t.AnyCorner)
            {
                Tuning.SetCorners(garment, socket, _target.ModName, 0f, 0f, 0f, 0f);
                _dirty = true;
            }
            y += EDGES_H + GAP;

            float bw = (IW - GAP * 2f) / 3f;
            if (GUI.Button(new Rect(x, y, bw * 2f + GAP, BTN), _dirty ? "SAVE  *" : "SAVE", _sSave))
                if (Tuning.Save()) _dirty = false;
            if (GUI.Button(new Rect(x + bw * 2f + GAP * 2f, y, bw, BTN), "RESET", _sBtn))
            {
                Tuning.Set(garment, socket, _target.ModName, 1f, 0f);
                Tuning.SetCorners(garment, socket, _target.ModName, 0f, 0f, 0f, 0f);
                ApplyLive(_target, 1f, 0f);
                _dirty = true;
            }
            y += BTN + 4f;

            string foot = _dirty ? "unsaved - SAVE writes it into the pack for everyone"
                                 : "saved in the pack, same for every player";
            if (Seat.Count == 0) foot = "no worn patch to move - equip one to adjust it";

            if (Tuning.RemoteActive) foot = "seating shared with " + Tuning.PeerCount + " teammate(s) - your changes send when you SAVE";
            var footBox = new Rect(x, y, IW, ROW);
            GUI.Label(footBox, Fit(foot, _sDim, footBox.width), _sDim);
        }

        static Rect _lastRect;
        internal static bool PointerOver(Vector2 mp)
        {
            if (!CabinetOpen || (NewSlotsMod.WantNativeUi && NativeUi.Suppress)) return false;
            if (float.IsNaN(_pos.x)) return false;
            return _lastRect.Contains(new Vector2(mp.x, Screen.height - mp.y));
        }

        static string Fmt(float v) { return (v >= 0f ? "+" : "") + v.ToString("0.0"); }

        static void ApplyLive(Def d, float scale, float outMm)
        {
            d.PerScale = scale;
            d.PerOutMm = outMm;
        }

        static void HandleDrag(Rect handle)
        {
            var e = Event.current;
            if (e == null) return;
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0 && handle.Contains(e.mousePosition))
                    {
                        _dragging = true;
                        _dragOff = e.mousePosition - _pos;
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (_dragging)
                    {
                        _pos = e.mousePosition - _dragOff;
                        _placementDirty = true;
                        e.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (_dragging)
                    {
                        _dragging = false;
                        _saveAt = Time.realtimeSinceStartup;
                        e.Use();
                    }
                    break;
            }
        }

        static string Fit(string s, GUIStyle style, float width)
        {
            if (string.IsNullOrEmpty(s)) return "";
            try
            {
                if (style.CalcSize(new GUIContent(s)).x <= width) return s;
                for (int n = s.Length - 1; n > 3; n--)
                {
                    string cut = s.Substring(0, n) + "..";
                    if (style.CalcSize(new GUIContent(cut)).x <= width) return cut;
                }
            }
            catch { }
            return s;
        }
    }
}
