// ODG Operations - LoadoutPresets
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Il2CppTMPro;

namespace LoadoutPresets
{

    internal static class NativeUI
    {

        const int   VISIBLE_ROWS = 10;
        const float PANEL_W = 482f;
        const float PAD     = 16f;
        const float HEAD_H  = 44f;
        const float HINT_H  = 22f;
        const float ROW_H   = 46f;
        const float ROW_GAP = 6f;
        const float FOOT_H  = 44f;
        const float BAR_X   = 460f;
        const float BAR_W   = 6f;
        const float BAR_HIT_W = 18f;
        const float BAR_INSET = (BAR_HIT_W - BAR_W) * 0.5f;
        const float THUMB_MIN = 28f;
        const float MIN_SCALE = 0.35f, MAX_SCALE = 1.60f;
        const float GRIP = 26f;

        const float ROW_W   = BAR_X - BAR_INSET - PAD;

        const float COL_NUM_X = 0f,   COL_NUM_W = 40f;
        const float COL_NAM_X = 46f,  COL_NAM_W = 190f;
        const float COL_SAV_X = 242f, COL_BTN_W = 64f;
        const float COL_LOA_X = 312f;
        const float COL_DEL_X = 382f, COL_DEL_W = 56f;

        static readonly Color C_PANEL     = new Color(0.075f, 0.090f, 0.086f, 0.93f);
        static readonly Color C_PANEL_EDGE= new Color(0.30f, 0.345f, 0.330f, 0.60f);
        static readonly Color C_HEAD      = new Color(0.114f, 0.133f, 0.126f, 0.95f);
        static readonly Color C_ROW       = new Color(0.129f, 0.152f, 0.145f, 0.90f);
        static readonly Color C_ROW_HOT   = new Color(0.200f, 0.231f, 0.220f, 0.96f);
        static readonly Color C_ROW_EDGE  = new Color(0.27f, 0.310f, 0.297f, 0.55f);
        static readonly Color C_ROW_EDGE_F= new Color(0.38f, 0.430f, 0.412f, 0.80f);
        static readonly Color C_BTN       = new Color(0.271f, 0.302f, 0.290f, 1.00f);
        static readonly Color C_BTN_HOT   = new Color(0.435f, 0.475f, 0.455f, 1.00f);
        static readonly Color C_BTN_EDGE  = new Color(0.42f, 0.465f, 0.447f, 0.45f);
        static readonly Color C_BTN_OFF   = new Color(0.160f, 0.180f, 0.173f, 0.85f);
        static       Color C_TEXT         = new Color(0.886f, 0.906f, 0.898f, 1f);
        static readonly Color C_TEXT_DIM  = new Color(0.545f, 0.580f, 0.565f, 1f);
        static readonly Color C_TEXT_OFF  = new Color(0.355f, 0.385f, 0.375f, 1f);
        static readonly Color C_TEXT_ON   = new Color(0.96f, 0.97f, 0.965f, 1f);
        static readonly Color C_DANGER    = new Color(0.78f, 0.33f, 0.29f, 1f);
        static readonly Color C_DANGER_BG = new Color(0.48f, 0.16f, 0.14f, 0.98f);
        static readonly Color C_GRIP      = new Color(0.45f, 0.49f, 0.47f, 0.75f);

        static GameObject   _root;
        static RectTransform _panel;
        static Canvas       _canvas;
        static Camera       _cam;
        static TMP_FontAsset _font;
        static Material     _fontMat;
        static Sprite       _spFill, _spEdge;
        static Row[]        _rows;
        static Btn[]        _btns;
        static TextMeshProUGUI _tHint, _tCount, _tToast;
        static int          _fails;
        static bool         _giveUp;

        static RectTransform _viewport, _content, _track, _thumb;
        static Image  _thumbImg;
        static float  _scroll;
        static float  _maxScroll;
        static float  _viewportH, _contentH;
        static bool   _dragBar;
        static float  _dragGrab;

        static RectTransform _rootRt, _canvasRect, _grabBar, _grip;
        static Image  _gripA, _gripB;
        static float  _posX, _posY, _scaleK, _panelH;
        static bool   _dragMove, _dragSize, _dirty;
        static Vector2 _dragFrom;
        static float  _sizeFrom, _distFrom;

        static int    _edit = -1;
        static string _editBuf = "";
        static string _editWas = "";
        static int    _armed = -1;
        static float  _armedT = -9f;

        internal static bool Active { get { return _root != null; } }
        internal static bool GaveUp { get { return _giveUp; } }

        const int STALL_FRAMES = 300;
        static int  _waitFrames;
        static bool _stalled;

        internal static bool Suppress { get { return !_giveUp && !_stalled; } }

        internal static bool Editing { get { return _edit >= 0; } }

        internal static void Tick(LoadoutPresetsMod mod, Il2Cpp.CharacterCustomisationController ctrl, bool open)
        {
            try
            {
                if (!open || ctrl == null)
                {
                    if (_root != null) Teardown();
                    return;
                }
                if (_giveUp) return;

                if (_root == null)
                {
                    if (!Build(mod, ctrl))
                    {
                        if (++_waitFrames > STALL_FRAMES && !_stalled)
                        {
                            _stalled = true;
                            LoadoutPresetsMod.Log("NativeUI: the cabinet canvas never resolved after "
                                                + STALL_FRAMES + " frames; letting the IMGUI panel draw.");
                        }
                        return;
                    }
                    _waitFrames = 0; _stalled = false;
                }

                if (Time.unscaledTime - _armedT > 3f && _armed != -1) _armed = -1;

                Refresh(mod);
                Interact(mod);
            }
            catch (Exception e)
            {
                LoadoutPresetsMod.Log("NativeUI.Tick: " + e.Message);
                Teardown();
                if (++_fails >= 3)
                {
                    _giveUp = true;
                    LoadoutPresetsMod.Log("NativeUI failed three times; falling back to the IMGUI panel "
                                        + "for the rest of this session. Presets themselves are untouched.");
                }
            }
        }

        static bool Build(LoadoutPresetsMod mod, Il2Cpp.CharacterCustomisationController ctrl)
        {

            GameObject canvasGo = null;
            try { canvasGo = ctrl.Canvas; } catch { }
            if (canvasGo == null) return false;

            _canvas = canvasGo.GetComponent<Canvas>();
            if (_canvas == null) _canvas = canvasGo.GetComponentInParent<Canvas>();
            if (_canvas == null) _canvas = canvasGo.GetComponentInChildren<Canvas>(true);
            if (_canvas == null) { LoadoutPresetsMod.Log("NativeUI: cabinet Canvas object has no Canvas"); return false; }

            _cam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;

            ResolveFont(ctrl, canvasGo);
            if (_spFill == null) _spFill = MakeRounded(32, 3, 0, false);
            if (_spEdge == null) _spEdge = MakeRounded(32, 3, 2, true);

            int slots = mod.SlotCount;
            int shown = Mathf.Min(VISIBLE_ROWS, slots);
            _contentH  = slots * ROW_H + (slots - 1) * ROW_GAP;
            _viewportH = shown * ROW_H + (shown - 1) * ROW_GAP;
            _maxScroll = Mathf.Max(0f, _contentH - _viewportH);
            _scroll    = Mathf.Clamp(_scroll, 0f, _maxScroll);
            float panelH = PAD + HEAD_H + HINT_H + 8f + _viewportH + 12f + 1f + 12f + FOOT_H + PAD;

            _root = new GameObject("ODG_LoadoutPresets");
            _root.layer = canvasGo.layer;
            var rootRt = _root.AddComponent<RectTransform>();
            rootRt.SetParent(_canvas.transform, false);
            rootRt.anchorMin = new Vector2(0f, 0.5f);
            rootRt.anchorMax = new Vector2(0f, 0.5f);
            rootRt.pivot     = new Vector2(0f, 0.5f);
            _rootRt = rootRt;
            _panelH = panelH;
            _canvasRect = _canvas.transform.TryCast<RectTransform>();
            _posX = mod.PanelX; _posY = mod.PanelY;
            rootRt.anchoredPosition = new Vector2(_posX, _posY);
            rootRt.sizeDelta = new Vector2(PANEL_W, panelH);
            rootRt.SetAsLastSibling();

            try
            {
                var sub = _root.AddComponent<Canvas>();
                sub.overrideSorting = true;
                sub.sortingOrder = _canvas.sortingOrder + 10;
                _root.AddComponent<GraphicRaycaster>();
            }
            catch (Exception e) { LoadoutPresetsMod.Log("NativeUI: no sub-canvas (" + e.Message + "); harmless"); }

            try
            {
                if (_canvas.GetComponent<LayoutGroup>() != null)
                {
                    var le = _root.AddComponent<LayoutElement>();
                    le.ignoreLayout = true;
                    LoadoutPresetsMod.Log("NativeUI: canvas has a LayoutGroup; opted the panel out of it");
                }
            }
            catch { }

            float canvasH = 1080f;
            var scaler = _canvas.GetComponent<CanvasScaler>();
            if (scaler != null && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize
                && scaler.referenceResolution.y > 1f)
                canvasH = scaler.referenceResolution.y;
            else if (_canvasRect != null && _canvasRect.rect.height > 1f)
                canvasH = _canvasRect.rect.height;
            else
                canvasH = Screen.height;

            const float FIT_H = 0.57f;
            float k = mod.UiScale;
            if (k <= 0f) k = (FIT_H * canvasH) / panelH;
            _scaleK = Mathf.Clamp(k, MIN_SCALE, MAX_SCALE);
            rootRt.localScale = new Vector3(_scaleK, _scaleK, 1f);
            ClampPlacement();

            _panel = MakeRT(rootRt, "Panel", 0f, 0f, PANEL_W, panelH, new Vector2(0f, 1f));
            MakeImg(_panel, _spFill, C_PANEL, true);
            var edge = MakeRT(_panel, "Edge", 0f, 0f, PANEL_W, panelH, new Vector2(0f, 1f));
            MakeImg(edge, _spEdge, C_PANEL_EDGE, false);

            float y = PAD;

            var headBg = MakeRT(_panel, "HeaderBg", 2f, 2f, PANEL_W - 4f, PAD + HEAD_H - 4f, new Vector2(0f, 1f));
            MakeImg(headBg, _spFill, C_HEAD, false);
            var headRule = MakeRT(_panel, "HeaderRule", 2f, PAD + HEAD_H - 2f, PANEL_W - 4f, 1f, new Vector2(0f, 1f));
            MakeImg(headRule, null, C_ROW_EDGE, false);

            var head = MakeRT(_panel, "Header", PAD, y, PANEL_W - PAD * 2f, HEAD_H, new Vector2(0f, 1f));
            var tTitle = MakeText(head, "LOADOUT PRESETS", 19f, true, C_TEXT, TextAlignmentOptions.Left);
            tTitle.characterSpacing = 6f;
            y += HEAD_H;

            var hint = MakeRT(_panel, "Hint", PAD, y, PANEL_W - PAD * 2f, HINT_H, new Vector2(0f, 1f));
            _tHint = MakeText(hint, "", 13f, false, C_TEXT_DIM, TextAlignmentOptions.Left);
            y += HINT_H + 8f;

            _viewport = MakeRT(_panel, "Viewport", 0f, y, PANEL_W, _viewportH, new Vector2(0f, 1f));
            _viewport.gameObject.AddComponent<RectMask2D>();

            _content = MakeRT(_viewport, "Content", 0f, 0f, PANEL_W, _contentH, new Vector2(0f, 1f));

            _rows = new Row[slots];
            var btns = new System.Collections.Generic.List<Btn>();
            for (int i = 0; i < slots; i++)
            {
                _rows[i] = BuildRow(i, PAD, i * (ROW_H + ROW_GAP), ROW_W, btns);
                foreach (var b in _rows[i].Buttons()) b.clipped = true;
            }
            BuildScrollBar(y);
            ApplyScroll();

            y += _viewportH + 12f;

            var div = MakeRT(_panel, "Divider", PAD, y, PANEL_W - PAD * 2f, 1f, new Vector2(0f, 1f));
            MakeImg(div, null, new Color(0.72f, 0.745f, 0.765f, 0.28f), false);
            y += 1f + 12f;

            var foot = MakeRT(_panel, "Count", PAD, y + 6f, 220f, 22f, new Vector2(0f, 1f));
            _tCount = MakeText(foot, "", 14f, false, C_TEXT_DIM, TextAlignmentOptions.Left);

            var clear = BuildBtn(_panel, "ClearAll", PANEL_W - PAD - 22f - 130f, y, 130f, 32f,
                                 "CLEAR ALL", 3, -1, true);
            clear.danger = true;
            btns.Add(clear);

            var toast = MakeRT(rootRt, "Toast", 0f, panelH + 6f, PANEL_W, 24f, new Vector2(0f, 1f));
            _tToast = MakeText(toast, "", 14f, false, C_TEXT, TextAlignmentOptions.Center);

            BuildChrome(panelH);

            _btns = btns.ToArray();
            _fails = 0;
            LoadoutPresetsMod.Log("NativeUI built on '" + canvasGo.name + "' (render=" + _canvas.renderMode
                                + ", scale=" + k.ToString("0.00") + ", font="
                                + (_font != null ? _font.name : "<builtin>") + ")");
            return true;
        }

        static Row BuildRow(int i, float x, float y, float w, System.Collections.Generic.List<Btn> btns)
        {
            var r = new Row();
            r.rt = MakeRT(_content, "Row" + i, x, y, w, ROW_H, new Vector2(0f, 1f));
            r.fill = MakeImg(r.rt, _spFill, C_ROW, false);

            var e = MakeRT(r.rt, "Edge", 0f, 0f, w, ROW_H, new Vector2(0f, 1f));
            r.edge = MakeImg(e, _spEdge, C_ROW_EDGE, false);

            var num = MakeRT(r.rt, "Num", COL_NUM_X, 0f, COL_NUM_W, ROW_H, new Vector2(0f, 1f));
            r.num = MakeText(num, (i + 1).ToString("00"), 19f, true, C_TEXT, TextAlignmentOptions.Center);

            r.nameHit = MakeRT(r.rt, "Name", COL_NAM_X, 0f, COL_NAM_W, ROW_H, new Vector2(0f, 1f));
            r.name = MakeText(r.nameHit, "", 16f, false, C_TEXT, TextAlignmentOptions.Left);

            r.save = BuildBtn(r.rt, "Save", COL_SAV_X, (ROW_H - 30f) * 0.5f, COL_BTN_W, 30f, "SAVE", 0, i, true);
            r.load = BuildBtn(r.rt, "Load", COL_LOA_X, (ROW_H - 30f) * 0.5f, COL_BTN_W, 30f, "LOAD", 1, i, true);
            r.del  = BuildBtn(r.rt, "Del",  COL_DEL_X, (ROW_H - 30f) * 0.5f, COL_DEL_W, 30f, "X",    2, i, true);
            r.del.danger = true;

            btns.Add(r.save); btns.Add(r.load); btns.Add(r.del);
            return r;
        }

        static void BuildChrome(float panelH)
        {
            _grabBar = MakeRT(_panel, "TitleGrab", 0f, 0f, PANEL_W, PAD + HEAD_H, new Vector2(0f, 1f));

            _grip = MakeRT(_panel, "Grip", PANEL_W - GRIP - 3f, panelH - GRIP - 3f, GRIP, GRIP,
                           new Vector2(0f, 1f));

            var a = MakeRT(_grip, "a", 4f, -4f, 17f, 2.5f, new Vector2(0.5f, 0.5f));
            a.localRotation = Quaternion.Euler(0f, 0f, -45f);
            _gripA = MakeImg(a, null, C_GRIP, false);

            var b = MakeRT(_grip, "b", 8f, -8f, 9f, 2.5f, new Vector2(0.5f, 0.5f));
            b.localRotation = Quaternion.Euler(0f, 0f, -45f);
            _gripB = MakeImg(b, null, C_GRIP, false);
        }

        static void ClampPlacement()
        {
            if (_rootRt == null) return;
            float w = PANEL_W * _scaleK, h = _panelH * _scaleK;
            float cw = 1920f, ch = 1080f;
            if (_canvasRect != null && _canvasRect.rect.width > 1f)
            { cw = _canvasRect.rect.width; ch = _canvasRect.rect.height; }

            _posX = Mathf.Clamp(_posX, 150f - w, cw - 150f);
            float top = _posY + h * 0.5f;
            top = Mathf.Clamp(top, -ch * 0.5f + 90f, ch * 0.5f);
            _posY = top - h * 0.5f;

            _rootRt.anchoredPosition = new Vector2(_posX, _posY);
        }

        static bool CanvasPoint(Vector2 mp, out Vector2 p)
        {
            p = Vector2.zero;
            if (_canvasRect == null) return false;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, mp, _cam, out p);
        }

        static bool DragChrome(LoadoutPresetsMod mod, Vector2 mp, bool down)
        {
            bool held = Input.GetMouseButton(0);
            if (!held && (_dragMove || _dragSize))
            {
                _dragMove = _dragSize = false;
                if (_dirty) { mod.SavePlacement(_posX, _posY, _scaleK); _dirty = false; }
                return true;
            }

            bool overGrip = In(_grip, mp);
            var gripCol = (overGrip || _dragSize) ? C_TEXT : C_GRIP;
            if (_gripA != null && _gripA.color != gripCol) { _gripA.color = gripCol; _gripB.color = gripCol; }

            if (down && !_dragMove && !_dragSize)
            {
                Vector2 c;
                if (!CanvasPoint(mp, out c)) return false;

                if (overGrip)
                {

                    _dragSize = true;
                    _sizeFrom = _scaleK;
                    _distFrom = Mathf.Max(1f, (c - PivotCanvas()).magnitude);
                    return true;
                }
                if (In(_grabBar, mp))
                {
                    _dragMove = true;
                    _dragFrom = c;
                    return true;
                }
                return false;
            }

            if (_dragSize)
            {
                Vector2 c;
                if (CanvasPoint(mp, out c))
                {
                    float d = (c - PivotCanvas()).magnitude;
                    _scaleK = Mathf.Clamp(_sizeFrom * (d / _distFrom), MIN_SCALE, MAX_SCALE);
                    _rootRt.localScale = new Vector3(_scaleK, _scaleK, 1f);
                    ClampPlacement();
                    _dirty = true;
                }
                return true;
            }

            if (_dragMove)
            {
                Vector2 c;
                if (CanvasPoint(mp, out c))
                {
                    _posX += c.x - _dragFrom.x;
                    _posY += c.y - _dragFrom.y;
                    _dragFrom = c;
                    ClampPlacement();
                    _dirty = true;
                }
                return true;
            }
            return false;
        }

        static Vector2 PivotCanvas()
        {
            float cw = _canvasRect != null ? _canvasRect.rect.width : 1920f;
            return new Vector2(-cw * 0.5f + _posX, _posY);
        }

        static void BuildScrollBar(float topY)
        {
            if (_maxScroll <= 0f) return;

            _track = MakeRT(_panel, "ScrollTrack", BAR_X - BAR_INSET, topY, BAR_HIT_W, _viewportH,
                            new Vector2(0f, 1f));

            var line = MakeRT(_track, "Line", BAR_INSET, 0f, BAR_W, _viewportH, new Vector2(0f, 1f));
            MakeImg(line, _spFill, new Color(1f, 1f, 1f, 0.07f), false);

            _thumb = MakeRT(_track, "ScrollThumb", BAR_INSET, 0f, BAR_W, ThumbH(), new Vector2(0f, 1f));
            _thumbImg = MakeImg(_thumb, _spFill, C_BTN_EDGE, false);
        }

        static float ThumbH()
        {
            if (_contentH <= 0f) return _viewportH;
            return Mathf.Max(THUMB_MIN, _viewportH * (_viewportH / _contentH));
        }

        static void ApplyScroll()
        {
            if (_content == null) return;
            _scroll = Mathf.Clamp(_scroll, 0f, _maxScroll);
            _content.anchoredPosition = new Vector2(0f, _scroll);

            if (_thumb == null || _maxScroll <= 0f) return;
            float th = ThumbH();
            float travel = _viewportH - th;
            float f = _maxScroll > 0f ? _scroll / _maxScroll : 0f;
            _thumb.anchoredPosition = new Vector2(BAR_INSET, -f * travel);
        }

        static Btn BuildBtn(RectTransform parent, string name, float x, float y, float w, float h,
                            string label, int kind, int slot, bool on)
        {
            var b = new Btn { kind = kind, slot = slot, on = on };
            b.rt   = MakeRT(parent, name, x, y, w, h, new Vector2(0f, 1f));
            b.fill = MakeImg(b.rt, _spFill, C_BTN, false);
            var e  = MakeRT(b.rt, "Edge", 0f, 0f, w, h, new Vector2(0f, 1f));
            b.edge = MakeImg(e, _spEdge, C_BTN_EDGE, false);
            var t  = MakeRT(b.rt, "Label", 0f, 0f, w, h, new Vector2(0f, 1f));
            b.txt  = MakeText(t, label, 13f, true, C_TEXT, TextAlignmentOptions.Center);
            b.txt.characterSpacing = 2f;
            return b;
        }

        static void Refresh(LoadoutPresetsMod mod)
        {
            int used = 0;
            for (int i = 0; i < _rows.Length; i++)
            {
                var r = _rows[i];
                bool filled = mod.IsFilled(i);
                if (filled) used++;

                string shown;
                if (_edit == i)
                {
                    bool caret = ((int)(Time.unscaledTime * 2f) & 1) == 0;
                    shown = _editBuf + (caret ? "|" : " ");
                }
                else
                {
                    string lab = mod.GetLabel(i);
                    shown = lab.Length > 0 ? lab : "Slot " + (i + 1);
                }
                if (r.lastName != shown) { r.name.text = shown; r.lastName = shown; }

                var nameCol = _edit == i ? C_TEXT : (filled ? C_TEXT : C_TEXT_OFF);
                if (r.lastNameCol != nameCol) { r.name.color = nameCol; r.lastNameCol = nameCol; }

                var numCol = filled ? C_TEXT : C_TEXT_OFF;
                if (r.lastNumCol != numCol) { r.num.color = numCol; r.lastNumCol = numCol; }

                var edgeCol = filled ? C_ROW_EDGE_F : C_ROW_EDGE;
                if (r.lastEdge != edgeCol) { r.edge.color = edgeCol; r.lastEdge = edgeCol; }

                r.load.on = filled;
                r.del.on  = filled;
                r.del.armed = _armed == i;
                SetBtnLabel(r.del, _armed == i ? "OK?" : "X");
                Paint(r.save); Paint(r.load); Paint(r.del);
            }

            for (int i = 0; i < _btns.Length; i++)
            {
                if (_btns[i].kind != 3) continue;
                _btns[i].on = used > 0;
                _btns[i].armed = _armed == -2;
                SetBtnLabel(_btns[i], _armed == -2 ? "CONFIRM?" : "CLEAR ALL");
                Paint(_btns[i]);
            }

            string hint = _edit >= 0
                ? "typing slot name  ·  Enter to keep  ·  Esc to cancel"
                : (_armed != -1 ? "click again to confirm"
                                : "click a name to rename  ·  drag the title to move  ·  corner to resize");
            if (_tHint.text != hint) _tHint.text = hint;
            var hintCol = _armed != -1 ? C_DANGER : C_TEXT_DIM;
            if (_tHint.color != hintCol) _tHint.color = hintCol;

            string count = used + " / " + _rows.Length + " SAVED";
            if (_tCount.text != count) _tCount.text = count;

            string toast = Time.time - mod.ToastTime < 3f ? mod.ToastText : "";
            if (_tToast.text != toast) _tToast.text = toast;
        }

        static void SetBtnLabel(Btn b, string s)
        {
            if (b.lastLabel == s) return;
            b.txt.text = s; b.lastLabel = s;
        }

        static void Paint(Btn b)
        {
            Color fill, edge, text;
            if (!b.on)                    { fill = C_BTN_OFF;   edge = new Color(C_BTN_EDGE.r, C_BTN_EDGE.g, C_BTN_EDGE.b, 0.15f); text = C_TEXT_OFF; }
            else if (b.danger && b.armed) { fill = C_DANGER_BG; edge = C_DANGER;   text = C_TEXT; }
            else if (b.danger && b.hot)   { fill = C_DANGER_BG; edge = C_DANGER;   text = C_TEXT; }
            else if (b.hot)               { fill = C_BTN_HOT;   edge = C_BTN_HOT;  text = C_TEXT_ON; }
            else if (b.danger)            { fill = C_BTN;       edge = C_BTN_EDGE; text = C_TEXT_DIM; }
            else                          { fill = C_BTN;       edge = C_BTN_EDGE; text = C_TEXT; }

            if (b.lastFill != fill) { b.fill.color = fill; b.lastFill = fill; }
            if (b.lastEdge != edge) { b.edge.color = edge; b.lastEdge = edge; }
            if (b.lastText != text) { b.txt.color  = text; b.lastText = text; }
        }

        static void Interact(LoadoutPresetsMod mod)
        {
            Vector2 mp = Input.mousePosition;
            bool down = Input.GetMouseButtonDown(0);
            bool overPanel = In(_panel, mp);

            if (DragChrome(mod, mp, down)) return;
            if (Scroll(mp, down, overPanel)) return;

            Btn fired = null;
            for (int i = 0; i < _btns.Length; i++)
            {
                var b = _btns[i];
                bool hot = b.on && In(b.rt, mp) && (!b.clipped || In(_viewport, mp));
                if (hot != b.hot) { b.hot = hot; Paint(b); }
                if (hot && down && fired == null) fired = b;
            }

            bool inView = In(_viewport, mp);
            for (int i = 0; i < _rows.Length; i++)
            {
                var r = _rows[i];
                bool hot = _edit != i && fired == null && inView && In(r.rt, mp);
                var col = hot ? C_ROW_HOT : C_ROW;
                if (r.lastFill != col) { r.fill.color = col; r.lastFill = col; }
            }

            if (!down) return;

            if (fired != null) { Commit(mod); Fire(mod, fired); return; }

            if (inView)
            {
                for (int i = 0; i < _rows.Length; i++)
                {
                    if (!In(_rows[i].nameHit, mp)) continue;
                    if (_edit != i) { Commit(mod); BeginEdit(mod, i); }
                    return;
                }
            }

            Commit(mod);
        }

        static bool Scroll(Vector2 mp, bool down, bool overPanel)
        {
            if (_maxScroll <= 0f) return false;

            if (overPanel)
            {
                float d = Input.mouseScrollDelta.y;
                if (Mathf.Abs(d) > 0.0001f)
                {
                    _scroll -= d * (ROW_H + ROW_GAP) * 2f;
                    ApplyScroll();
                }
            }

            if (_track == null) return false;

            if (!Input.GetMouseButton(0)) _dragBar = false;

            if (down && !_dragBar && In(_track, mp))
            {
                Vector2 local;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_track, mp, _cam, out local))
                {
                    float th = ThumbH();
                    float travel = Mathf.Max(1f, _viewportH - th);
                    float thumbTop = -(_scroll / _maxScroll) * travel;
                    float grab = local.y - thumbTop;
                    if (grab <= 0f && grab >= -th)
                    {
                        _dragBar = true; _dragGrab = grab;
                    }
                    else
                    {

                        _dragBar = true; _dragGrab = -th * 0.5f;
                        DragTo(local.y);
                    }
                }
                return true;
            }

            if (_dragBar)
            {
                Vector2 local;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_track, mp, _cam, out local))
                    DragTo(local.y);
                if (_thumbImg != null) _thumbImg.color = C_BTN_HOT;
                return true;
            }

            if (_thumbImg != null)
            {
                var c = In(_track, mp) ? C_BTN_HOT : C_BTN_EDGE;
                if (_thumbImg.color != c) _thumbImg.color = c;
            }
            return false;
        }

        static void DragTo(float localY)
        {
            float th = ThumbH();
            float travel = Mathf.Max(1f, _viewportH - th);
            float top = Mathf.Clamp(localY - _dragGrab, -travel, 0f);
            _scroll = (-top / travel) * _maxScroll;
            ApplyScroll();
        }

        static bool In(RectTransform rt, Vector2 screenPoint)
        {
            if (rt == null) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(rt, screenPoint, _cam);
        }

        static void Fire(LoadoutPresetsMod mod, Btn b)
        {
            switch (b.kind)
            {
                case 0: mod.DoSave(b.slot); _armed = -1; break;
                case 1: mod.DoLoad(b.slot); _armed = -1; break;
                case 2:
                    if (_armed == b.slot) { mod.DoDelete(b.slot); _armed = -1; }
                    else { _armed = b.slot; _armedT = Time.unscaledTime; }
                    break;
                case 3:
                    if (_armed == -2) { mod.DoClearAll(); _armed = -1; }
                    else { _armed = -2; _armedT = Time.unscaledTime; }
                    break;
            }
        }

        static void BeginEdit(LoadoutPresetsMod mod, int i)
        {
            _edit = i;
            _editWas = mod.GetLabel(i);
            _editBuf = _editWas;
            _armed = -1;
        }

        static void Commit(LoadoutPresetsMod mod)
        {
            if (_edit < 0) return;
            int i = _edit; _edit = -1;
            if (_editBuf != _editWas) { mod.SetLabel(i, _editBuf); mod.SavePrefs(); }
            _rows[i].lastName = null;
        }

        static void Cancel(LoadoutPresetsMod mod)
        {
            if (_edit < 0) return;
            int i = _edit; _edit = -1;
            _editBuf = _editWas;
            _rows[i].lastName = null;
        }

        internal static bool FeedKey(LoadoutPresetsMod mod, Event e)
        {
            if (_edit < 0 || e == null || e.type != EventType.KeyDown) return false;

            char c = e.character;
            if (c == '\b') { if (_editBuf.Length > 0) _editBuf = _editBuf.Substring(0, _editBuf.Length - 1); return true; }
            if (c == '\n' || c == '\r') { Commit(mod); return true; }
            if (c >= ' ' && c != (char)127)
            {
                if (_editBuf.Length < 22) _editBuf += c;
                return true;
            }
            if (e.keyCode == KeyCode.Escape)    { Cancel(mod); return true; }
            if (e.keyCode == KeyCode.Backspace) { if (_editBuf.Length > 0) _editBuf = _editBuf.Substring(0, _editBuf.Length - 1); return true; }
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { Commit(mod); return true; }
            return false;
        }

        internal static void Teardown()
        {
            try { if (_root != null) UnityEngine.Object.Destroy(_root); } catch { }
            _root = null; _panel = null; _canvas = null; _cam = null;
            _rows = null; _btns = null;
            _rootRt = null; _canvasRect = null; _grabBar = null; _grip = null;
            _gripA = null; _gripB = null;
            _dragMove = false; _dragSize = false; _dirty = false;
            _viewport = null; _content = null; _track = null; _thumb = null; _thumbImg = null;
            _scroll = 0f; _maxScroll = 0f; _viewportH = 0f; _contentH = 0f;
            _dragBar = false; _dragGrab = 0f;
            _tHint = null; _tCount = null; _tToast = null;
            _spFill = null; _spEdge = null;
            _font = null; _fontMat = null;
            _edit = -1; _editBuf = ""; _editWas = ""; _armed = -1;
            _waitFrames = 0; _stalled = false;
        }

        static void ResolveFont(Il2Cpp.CharacterCustomisationController ctrl, GameObject canvasGo)
        {
            if (_font != null) return;
            TMP_Text src = null;
            try { src = ctrl.HeadGear; } catch { }
            if (src == null) { try { src = ctrl.Shirt; } catch { } }
            if (src == null) { try { src = ctrl.Vest;  } catch { } }
            if (src == null)
            {
                try
                {
                    var all = canvasGo.GetComponentsInChildren<TextMeshProUGUI>(true);
                    for (int i = 0; all != null && i < all.Length; i++)
                        if (all[i] != null && all[i].font != null) { src = all[i]; break; }
                }
                catch { }
            }
            if (src == null) { LoadoutPresetsMod.Log("NativeUI: no TMP text on the cabinet to borrow a font from"); return; }

            try { _font = src.font; } catch { }
            try { _fontMat = src.fontSharedMaterial; } catch { }
            try
            {
                var c = src.color;
                if (c.a > 0.5f && c.r + c.g + c.b > 1.5f) C_TEXT = c;
            }
            catch { }
        }

        static Sprite MakeRounded(int size, int radius, int stroke, bool ring)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.HideAndDontSave;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            float r = radius;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float cx = Mathf.Clamp(px, r, size - r);
                    float cy = Mathf.Clamp(py, r, size - r);
                    float dx = px - cx, dy = py - cy;
                    float inside = r - Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(inside + 0.5f);
                    if (ring)
                    {
                        float innerA = Mathf.Clamp01(inside - stroke + 0.5f);
                        a = Mathf.Clamp01(a - innerA);
                    }

                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply(false, false);

            var sp = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f),
                                   100f, 0u, SpriteMeshType.FullRect,
                                   new Vector4(radius, radius, radius, radius));
            if (sp != null) sp.hideFlags = HideFlags.HideAndDontSave;
            return sp;
        }

        static RectTransform MakeRT(Transform parent, string name, float x, float y, float w, float h, Vector2 anchor)
        {
            var go = new GameObject(name);
            go.layer = parent.gameObject.layer;
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot     = anchor;
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            rt.localScale = Vector3.one;
            return rt;
        }

        static Image MakeImg(RectTransform rt, Sprite sp, Color c, bool raycast)
        {
            var img = rt.gameObject.AddComponent<Image>();
            if (sp != null)
            {
                img.sprite = sp;
                img.type = Image.Type.Sliced;
                float refPpu = 100f;
                try { if (_canvas != null) refPpu = _canvas.referencePixelsPerUnit; } catch { }
                if (refPpu <= 0f) refPpu = 100f;
                img.pixelsPerUnitMultiplier = refPpu / 100f;
            }
            img.color = c;
            img.raycastTarget = raycast;
            return img;
        }

        static TextMeshProUGUI MakeText(RectTransform rt, string text, float size, bool bold,
                                        Color c, TextAlignmentOptions align)
        {
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();

            if (_font != null) t.font = _font;
            if (_fontMat != null) t.fontSharedMaterial = _fontMat;
            t.fontSize = size;
            t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            t.color = c;
            t.alignment = align;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.richText = false;
            t.raycastTarget = false;
            t.margin = new Vector4(2f, 0f, 2f, 0f);
            t.text = text;
            return t;
        }

        class Row
        {
            public RectTransform rt, nameHit;
            public Image fill, edge;
            public TextMeshProUGUI num, name;
            public Btn save, load, del;
            public Btn[] Buttons() { return new[] { save, load, del }; }
            public string lastName;
            public Color lastFill, lastEdge, lastNumCol, lastNameCol;
        }

        class Btn
        {
            public RectTransform rt;
            public Image fill, edge;
            public TextMeshProUGUI txt;
            public int kind;
            public int slot;
            public bool on = true;
            public bool hot;
            public bool danger;
            public bool armed;
            public bool clipped;
            public string lastLabel;
            public Color lastFill, lastEdge, lastText;
        }
    }
}
