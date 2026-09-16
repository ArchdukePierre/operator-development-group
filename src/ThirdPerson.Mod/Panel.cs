// ODG Operations - ThirdPerson
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Il2CppTMPro;
using OdgUi;

namespace ThirdPerson
{

    internal sealed class Panel
    {
        private const float PanelW = 482f;
        private const float Pad = 16f;
        private const float HeadH = 44f;
        private const float HintH = 22f;
        private const float RowH = 30f;
        private const float RowGap = 4f;
        private const float FootH = 56f;

        private const float ToastBottom = 56f;
        private const float ToastPad = 18f;

        private static readonly Color RowEdgeHot = new Color(0.38f, 0.430f, 0.412f, 0.80f);
        private static readonly Color Divider = new Color(0.72f, 0.745f, 0.765f, 0.28f);

        private readonly Canvas _canvas;
        private readonly List<TextMeshProUGUI> _texts = new List<TextMeshProUGUI>();

        private RectTransform _root;
        private TextMeshProUGUI _title, _hint, _footer;
        private Image[] _rowFill, _rowEdge;
        private TextMeshProUGUI[] _label, _value;
        private string[] _cached;
        private int _selected = -1;
        private bool _visible = true;

        private RectTransform _toastRoot, _toastEdge, _toastRule, _toastMainRt, _toastSubRt;
        private TextMeshProUGUI _toastMain, _toastSub;
        private float _toastUntil;
        private bool _toastOn = true;

        private bool _gameFont;

        internal Panel(string[] labels)
        {
            _canvas = BuildCanvas();
            Skin.Bind(_canvas);
            if (Skin.Font == null) BorrowDefaultFont();

            BuildToast(_canvas.transform);
            BuildPanel(_canvas.transform, labels);

            SetVisible(false);
            ShowToast(false);
        }

        internal int RowCount { get { return _label == null ? 0 : _label.Length; } }
        internal bool HasGameFont { get { return _gameFont; } }

        internal void SetVisible(bool visible)
        {
            if (_visible == visible) return;
            _visible = visible;
            try { _root.gameObject.SetActive(visible); } catch { }
        }

        internal void SetScale(float scale)
        {
            if (scale <= 0.2f) scale = 1f;
            try
            {
                _root.localScale = new Vector3(scale, scale, 1f);
                _toastRoot.localScale = new Vector3(scale, scale, 1f);
            }
            catch { }
        }

        internal void SetTitle(string s)  { Set(_title, s); }
        internal void SetHint(string s)   { Set(_hint, s); }
        internal void SetFooter(string s) { Set(_footer, s); }

        private static void Set(TextMeshProUGUI t, string s)
        {
            try { if (t != null && t.text != s) t.text = s ?? ""; } catch { }
        }

        internal void SetValue(int row, string s)
        {
            if (row < 0 || row >= RowCount) return;
            if (_cached[row] == s) return;
            _cached[row] = s;
            try { _value[row].text = s; } catch { }
        }

        internal void SetSelected(int row)
        {
            if (row == _selected) return;
            try
            {
                if (_selected >= 0 && _selected < RowCount)
                {
                    _rowFill[_selected].color = Skin.Row;
                    _rowEdge[_selected].color = Skin.RowEdge;
                }
                _selected = row;
                if (_selected >= 0 && _selected < RowCount)
                {
                    _rowFill[_selected].color = Skin.RowHot;
                    _rowEdge[_selected].color = RowEdgeHot;
                }
            }
            catch { }
        }

        internal void Toast(string main, string sub, float seconds)
        {
            try
            {
                bool hasSub = !string.IsNullOrEmpty(sub);
                _toastMain.text = main ?? "";
                _toastSub.text = hasSub ? sub : "";
                _toastSubRt.gameObject.SetActive(hasSub);

                float w = _toastMain.preferredWidth;
                if (hasSub) w = Mathf.Max(w, _toastSub.preferredWidth);
                w = Mathf.Max(220f, w + ToastPad * 2f + 12f);
                float h = hasSub ? 60f : 42f;

                _toastRoot.sizeDelta = new Vector2(w, h);
                _toastEdge.sizeDelta = new Vector2(w, h);
                _toastRule.sizeDelta = new Vector2(w - 4f, 1f);
                _toastMainRt.sizeDelta = new Vector2(w - ToastPad * 2f, 24f);
                _toastMainRt.anchoredPosition = new Vector2(ToastPad, hasSub ? -8f : -9f);
                _toastSubRt.sizeDelta = new Vector2(w - ToastPad * 2f, 18f);
                _toastSubRt.anchoredPosition = new Vector2(ToastPad, -33f);

                ShowToast(true);
                _toastUntil = Time.unscaledTime + Mathf.Max(0.2f, seconds);
            }
            catch { }
        }

        internal void Tick()
        {
            if (_toastUntil > 0f && Time.unscaledTime >= _toastUntil)
            {
                _toastUntil = 0f;
                ShowToast(false);
            }
        }

        private void ShowToast(bool on)
        {
            if (_toastOn == on) return;
            _toastOn = on;
            try { _toastRoot.gameObject.SetActive(on); } catch { }
        }

        internal void TryBorrowFont(Il2Cpp.PlayerNetworking pn)
        {
            if (_gameFont || pn == null) return;
            TMP_Text src = null;
            try
            {
                var go = pn.CanvasObject;
                if (go != null)
                {
                    var all = go.GetComponentsInChildren<TextMeshProUGUI>(true);
                    for (int i = 0; all != null && i < all.Length; i++)
                        if (all[i] != null && all[i].font != null) { src = all[i]; break; }
                }
            }
            catch { }
            if (src == null) return;

            Skin.Font = null;
            Skin.FontMat = null;
            Skin.BorrowFont(src);
            if (Skin.Font == null) return;

            _gameFont = true;
            for (int i = 0; i < _texts.Count; i++)
            {
                var t = _texts[i];
                if (t == null) continue;
                try
                {
                    t.font = Skin.Font;
                    if (Skin.FontMat != null) t.fontSharedMaterial = Skin.FontMat;
                }
                catch { }
            }
        }

        private static void BorrowDefaultFont()
        {
            try
            {
                var f = TMP_Settings.defaultFontAsset;
                if (f != null) Skin.Font = f;
            }
            catch { }
        }

        private static Canvas BuildCanvas()
        {
            var go = new GameObject("ThirdPerson_Canvas");
            GameObject.DontDestroyOnLoad(go);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 420;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            return canvas;
        }

        private TextMeshProUGUI T(RectTransform rt, string s, float size, bool bold, Color c, TextAlignmentOptions align)
        {
            var t = Skin.Text_(rt, s, size, bold, c, align);
            _texts.Add(t);
            return t;
        }

        private void BuildToast(Transform parent)
        {

            _toastRoot = Skin.Rect(parent, "ThirdPerson_Toast", 0f, -ToastBottom, 300f, 42f, new Vector2(0.5f, 0f));
            Skin.Img(_toastRoot, Skin.Fill, Skin.Panel, false);

            _toastEdge = Skin.Rect(_toastRoot, "Edge", 0f, 0f, 300f, 42f);
            Skin.Img(_toastEdge, Skin.Edge, Skin.PanelEdge, false);

            _toastRule = Skin.Rect(_toastRoot, "Rule", 2f, 2f, 296f, 1f);
            Skin.Img(_toastRule, null, Skin.RowEdge, false);

            _toastMainRt = Skin.Rect(_toastRoot, "Main", ToastPad, 9f, 300f - ToastPad * 2f, 24f);
            _toastMain = T(_toastMainRt, "", 15f, true, Skin.Text, TextAlignmentOptions.Center);
            _toastMain.characterSpacing = 4f;

            _toastSubRt = Skin.Rect(_toastRoot, "Sub", ToastPad, 33f, 300f - ToastPad * 2f, 18f);
            _toastSub = T(_toastSubRt, "", 12f, false, Skin.TextDim, TextAlignmentOptions.Center);
            _toastSub.characterSpacing = 1f;
        }

        private void BuildPanel(Transform parent, string[] labels)
        {
            int n = labels.Length;
            float y = Pad + HeadH + HintH + 8f;
            float rowsH = n * RowH + (n - 1) * RowGap;
            float panelH = y + rowsH + 12f + 1f + 12f + FootH + Pad;

            _root = Skin.Rect(parent, "ThirdPerson_Panel", 40f, 0f, PanelW, panelH, new Vector2(0f, 0.5f));
            Skin.Img(_root, Skin.Fill, Skin.Panel, false);
            var edge = Skin.Rect(_root, "Edge", 0f, 0f, PanelW, panelH);
            Skin.Img(edge, Skin.Edge, Skin.PanelEdge, false);

            var headBg = Skin.Rect(_root, "HeaderBg", 2f, 2f, PanelW - 4f, Pad + HeadH - 4f);
            Skin.Img(headBg, Skin.Fill, Skin.Head, false);
            var headRule = Skin.Rect(_root, "HeaderRule", 2f, Pad + HeadH - 2f, PanelW - 4f, 1f);
            Skin.Img(headRule, null, Skin.RowEdge, false);

            var head = Skin.Rect(_root, "Header", Pad, Pad, PanelW - Pad * 2f, HeadH);
            _title = T(head, "THIRD PERSON CAMERA", 19f, true, Skin.Text, TextAlignmentOptions.Left);
            _title.characterSpacing = 6f;

            var hint = Skin.Rect(_root, "Hint", Pad, Pad + HeadH, PanelW - Pad * 2f, HintH);
            _hint = T(hint, "", 13f, false, Skin.TextDim, TextAlignmentOptions.Left);

            _rowFill = new Image[n];
            _rowEdge = new Image[n];
            _label = new TextMeshProUGUI[n];
            _value = new TextMeshProUGUI[n];
            _cached = new string[n];

            float w = PanelW - Pad * 2f;
            for (int i = 0; i < n; i++)
            {
                float ry = y + i * (RowH + RowGap);
                var row = Skin.Rect(_root, "Row" + i, Pad, ry, w, RowH);
                _rowFill[i] = Skin.Img(row, Skin.Fill, Skin.Row, false);
                var re = Skin.Rect(row, "Edge", 0f, 0f, w, RowH);
                _rowEdge[i] = Skin.Img(re, Skin.Edge, Skin.RowEdge, false);

                var lab = Skin.Rect(row, "Label", 10f, 0f, w * 0.5f, RowH);
                _label[i] = T(lab, labels[i], 15f, false, Skin.Text, TextAlignmentOptions.Left);

                float vw = w * 0.5f;
                var val = Skin.Rect(row, "Value", w - 10f - vw, 0f, vw, RowH);
                _value[i] = T(val, "", 15f, true, Skin.Text, TextAlignmentOptions.Right);
                _cached[i] = "";
            }
            y += rowsH + 12f;

            var div = Skin.Rect(_root, "Divider", Pad, y, PanelW - Pad * 2f, 1f);
            Skin.Img(div, null, Divider, false);
            y += 1f + 12f;

            var foot = Skin.Rect(_root, "Footer", Pad, y, PanelW - Pad * 2f, FootH);
            _footer = T(foot, "", 13f, false, Skin.TextDim, TextAlignmentOptions.TopLeft);
            _footer.enableWordWrapping = true;
            _footer.overflowMode = TextOverflowModes.Overflow;
        }
    }
}
