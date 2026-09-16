// ODG Operations - Quartermaster
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using UnityEngine;
using UnityEngine.UI;
using Il2CppTMPro;
using OdgUi;

namespace Quartermaster
{
    internal enum ChipKind { Good, Bad, Neutral, Cosmetic }

    internal static class Palette
    {
        internal static readonly Color GoodFill = new Color(0.059f, 0.165f, 0.102f, 0.97f);
        internal static readonly Color GoodEdge = new Color(0.239f, 0.863f, 0.518f, 1f);
        internal static readonly Color BadFill  = new Color(0.165f, 0.059f, 0.063f, 0.97f);
        internal static readonly Color BadEdge  = new Color(0.898f, 0.282f, 0.302f, 1f);
        internal static readonly Color GreyFill = new Color(0.102f, 0.110f, 0.125f, 0.97f);
        internal static readonly Color GreyEdge = new Color(0.541f, 0.561f, 0.596f, 1f);
        internal static readonly Color Card     = new Color(0.035f, 0.040f, 0.045f, 0.97f);
        internal static readonly Color CardEdge = new Color(0.40f, 0.42f, 0.45f, 0.70f);
        internal static readonly Color Seg      = new Color(0.78f, 0.80f, 0.82f, 0.95f);
        internal static readonly Color SegOff   = new Color(0.30f, 0.32f, 0.34f, 0.50f);
    }

    internal sealed class Chip
    {
        internal RectTransform Rt;
        Image _fill, _edge, _a, _b;
        RectTransform _chev, _ra, _rb, _lrt;
        TextMeshProUGUI _t;
        string _last;
        int _kind = -1;
        bool _up = true;
        float _w, _h;

        internal static Chip Make(RectTransform parent, string name, float x, float y, float w, float h, float fontSize)
        {
            var c = new Chip { _w = w, _h = h };
            c.Rt = Skin.Rect(parent, name, x, y, w, h);
            c._fill = Skin.Img(c.Rt, Skin.Fill, Palette.GreyFill, false);
            var e = Skin.Rect(c.Rt, "Edge", 0f, 0f, w, h);
            c._edge = Skin.Img(e, Skin.Edge, Palette.GreyEdge, false);

            c._chev = Skin.Rect(c.Rt, "Chev", 7f, (h - 10f) * 0.5f, 12f, 10f);
            c._ra = Skin.Rect(c._chev, "a", -2.6f, 0f, 7.5f, 2.2f, new Vector2(0.5f, 0.5f));
            c._a = Skin.Img(c._ra, null, Palette.GreyEdge, false);
            c._rb = Skin.Rect(c._chev, "b", 2.6f, 0f, 7.5f, 2.2f, new Vector2(0.5f, 0.5f));
            c._b = Skin.Img(c._rb, null, Palette.GreyEdge, false);

            c._lrt = Skin.Rect(c.Rt, "Label", 22f, 0f, w - 26f, h);
            c._t = Skin.Text_(c._lrt, "", fontSize, true, Skin.Text, TextAlignmentOptions.Left);
            c._t.characterSpacing = 0.5f;
            c.SetChevron(true, true);
            return c;
        }

        internal void Set(string text, ChipKind kind, bool up)
        {
            if (_t != null && _last != text) { _t.text = text; _last = text; }
            if ((int)kind != _kind)
            {
                _kind = (int)kind;
                Color f, e;
                switch (kind)
                {
                    case ChipKind.Good: f = Palette.GoodFill; e = Palette.GoodEdge; break;
                    case ChipKind.Bad:  f = Palette.BadFill;  e = Palette.BadEdge;  break;
                    default:            f = Palette.GreyFill; e = Palette.GreyEdge; break;
                }
                _fill.color = f; _edge.color = e; _t.color = e; _a.color = e; _b.color = e;
                bool chev = kind == ChipKind.Good || kind == ChipKind.Bad;
                if (_chev.gameObject.activeSelf != chev) _chev.gameObject.SetActive(chev);
                _lrt.anchoredPosition = new Vector2(chev ? 22f : 8f, 0f);
                _lrt.sizeDelta = new Vector2(_w - (chev ? 26f : 12f), _h);
            }
            SetChevron(up, false);
        }

        void SetChevron(bool up, bool force)
        {
            if (_up == up && !force) return;
            _up = up;
            float a = up ? 45f : -45f;
            _ra.localRotation = Quaternion.Euler(0f, 0f, a);
            _rb.localRotation = Quaternion.Euler(0f, 0f, -a);
        }

        internal void Show(bool on)
        {
            if (Rt != null && Rt.gameObject.activeSelf != on) Rt.gameObject.SetActive(on);
        }

        internal void Place(float x, float y) { if (Rt != null) Rt.anchoredPosition = new Vector2(x, -y); }
    }

    internal sealed class Tile
    {
        internal RectTransform Rt, InfoRt;
        internal string Tip = "";
        TextMeshProUGUI _label, _value, _sub, _infoT;
        Image _infoRing;
        readonly RectTransform[] _fill = new RectTransform[5], _delta = new RectTransform[5];
        readonly Image[] _deltaImg = new Image[5];
        float _segW;
        string _curText = "";
        float _cur = -1f;
        string _shownText, _shownSub;
        float _shownLo = -2f, _shownHi = -2f;
        int _shownTone = -1;
        bool _infoHot;

        internal static Tile Make(RectTransform parent, string name, float x, float y, float w, float h, string label, string tip)
        {
            var t = new Tile { Tip = tip };
            t.Rt = Skin.Rect(parent, name, x, y, w, h);
            Skin.Img(t.Rt, Skin.Fill, Skin.Row, false);
            var l = Skin.Rect(t.Rt, "L", 8f, 4f, w - 40f, 16f);
            t._label = Skin.Text_(l, label, 10.5f, true, Skin.TextDim, TextAlignmentOptions.Left);
            t._label.characterSpacing = 1.5f;

            t.InfoRt = Skin.Rect(t.Rt, "Info", w - 24f, 5f, 15f, 15f);
            t._infoRing = Skin.Img(t.InfoRt, Skin.Edge, Skin.TextDim, false);
            var it = Skin.Rect(t.InfoRt, "i", 0f, 0f, 15f, 15f);
            t._infoT = Skin.Text_(it, "i", 10f, true, Skin.TextDim, TextAlignmentOptions.Center);
            t._infoT.margin = Vector4.zero;

            var v = Skin.Rect(t.Rt, "V", 8f, 19f, w - 16f, 26f);
            t._value = Skin.Text_(v, "", 18f, true, Skin.Text, TextAlignmentOptions.Left);
            var sub = Skin.Rect(t.Rt, "Sub", 8f, 43f, w - 16f, 13f);
            t._sub = Skin.Text_(sub, "", 9.5f, false, Skin.TextDim, TextAlignmentOptions.Left);

            t._segW = (w - 16f - 16f) / 5f;
            for (int i = 0; i < 5; i++)
            {
                var bg = Skin.Rect(t.Rt, "S" + i, 8f + i * (t._segW + 4f), h - 12f, t._segW, 5f);
                Skin.Img(bg, Skin.Fill, Palette.SegOff, false);
                t._fill[i] = Skin.Rect(bg, "F", 0f, 0f, 0f, 5f);
                Skin.Img(t._fill[i], Skin.Fill, Palette.Seg, false);
                t._delta[i] = Skin.Rect(bg, "D", 0f, 0f, 0f, 5f);
                t._deltaImg[i] = Skin.Img(t._delta[i], Skin.Fill, Palette.GoodEdge, false);
            }
            return t;
        }

        internal void Set(string value, float frac)
        {
            _curText = value ?? "";
            _cur = Mathf.Clamp01(frac);
            if (_shownSub == null || _shownSub.Length == 0) Draw(_curText, "", _cur, _cur, 0);
        }

        internal void Project(string value, float frac)
        {
            float p = Mathf.Clamp01(frac);
            bool same = value == _curText && Math.Abs(p - _cur) < 0.003f;
            if (same) { Draw(_curText, "", _cur, _cur, 0); return; }
            int tone = p > _cur + 0.003f ? 1 : (p < _cur - 0.003f ? -1 : 0);
            Draw(value ?? "", "now " + _curText, _cur, p, tone);
        }

        internal void ClearProjection() { Draw(_curText, "", _cur, _cur, 0); }

        void Draw(string text, string sub, float cur, float proj, int tone)
        {
            if (_shownText != text) { _value.text = text; _shownText = text; }
            if (_shownSub != sub) { _sub.text = sub; _shownSub = sub; }
            float lo = Math.Min(cur, proj), hi = Math.Max(cur, proj);
            if (tone != _shownTone)
            {
                _shownTone = tone;
                var c = tone > 0 ? Palette.GoodEdge : (tone < 0 ? Palette.BadEdge : Skin.Text);
                _value.color = c;
                for (int i = 0; i < 5; i++) _deltaImg[i].color = c;
            }
            if (Math.Abs(lo - _shownLo) < 0.0005f && Math.Abs(hi - _shownHi) < 0.0005f) return;
            _shownLo = lo; _shownHi = hi;
            for (int i = 0; i < 5; i++)
            {
                float a = Mathf.Clamp01(lo * 5f - i), b = Mathf.Clamp01(hi * 5f - i);
                _fill[i].sizeDelta = new Vector2(_segW * a, 5f);
                _delta[i].anchoredPosition = new Vector2(_segW * a, 0f);
                _delta[i].sizeDelta = new Vector2(_segW * (b - a), 5f);
            }
        }

        internal void InfoHot(bool on)
        {
            if (_infoHot == on) return;
            _infoHot = on;
            var c = on ? Skin.Text : Skin.TextDim;
            _infoRing.color = c; _infoT.color = c;
        }
    }

    internal sealed class StatLine
    {
        internal RectTransform Rt;
        TextMeshProUGUI _label, _value;
        readonly Image[] _segs = new Image[5];
        internal Chip Chip;
        string _lv;
        int _lf = -1;

        internal static StatLine Make(RectTransform parent, string name, float x, float y, float w, float h, string label)
        {
            var s = new StatLine();
            s.Rt = Skin.Rect(parent, name, x, y, w, h);
            var l = Skin.Rect(s.Rt, "L", 0f, 0f, 104f, h);
            s._label = Skin.Text_(l, label, 11f, true, Skin.TextDim, TextAlignmentOptions.Left);
            s._label.characterSpacing = 1.5f;
            for (int i = 0; i < 5; i++)
            {
                var sr = Skin.Rect(s.Rt, "S" + i, 108f + i * 15f, (h - 6f) * 0.5f, 12f, 6f);
                s._segs[i] = Skin.Img(sr, Skin.Fill, Palette.SegOff, false);
            }
            var v = Skin.Rect(s.Rt, "V", 188f, 0f, w - 188f - 96f, h);
            s._value = Skin.Text_(v, "", 12.5f, false, Skin.Text, TextAlignmentOptions.Left);
            s.Chip = Chip.Make(s.Rt, "Chip", w - 92f, (h - 20f) * 0.5f, 92f, 20f, 11f);
            s.Chip.Show(false);
            return s;
        }

        internal void Set(string value, float frac)
        {
            if (_lv != value) { _value.text = value; _lv = value; }
            int f = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(frac) * 5f), 0, 5);
            if (f != _lf)
            {
                _lf = f;
                for (int i = 0; i < 5; i++) _segs[i].color = i < f ? Palette.Seg : Palette.SegOff;
            }
        }

        internal void SetDelta(string text, ChipKind kind, bool up) { Chip.Set(text, kind, up); Chip.Show(true); }
        internal void HideDelta() { Chip.Show(false); }
    }
}
