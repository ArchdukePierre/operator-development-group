// ODG Operations - shared
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using UnityEngine;
using UnityEngine.UI;
using Il2CppTMPro;

namespace OdgUi
{

    internal static class Skin
    {
        internal static readonly Color Panel     = new Color(0.075f, 0.090f, 0.086f, 0.93f);
        internal static readonly Color PanelEdge = new Color(0.30f,  0.345f, 0.330f, 0.60f);
        internal static readonly Color Head      = new Color(0.114f, 0.133f, 0.126f, 0.95f);
        internal static readonly Color Row       = new Color(0.129f, 0.152f, 0.145f, 0.90f);
        internal static readonly Color RowHot    = new Color(0.200f, 0.231f, 0.220f, 0.96f);
        internal static readonly Color RowEdge   = new Color(0.27f,  0.310f, 0.297f, 0.55f);
        internal static readonly Color Btn       = new Color(0.271f, 0.302f, 0.290f, 1.00f);
        internal static readonly Color BtnHot    = new Color(0.435f, 0.475f, 0.455f, 1.00f);
        internal static readonly Color BtnEdge   = new Color(0.42f,  0.465f, 0.447f, 0.45f);
        internal static readonly Color BtnOff    = new Color(0.160f, 0.180f, 0.173f, 0.85f);
        internal static       Color Text         = new Color(0.886f, 0.906f, 0.898f, 1f);
        internal static readonly Color TextDim   = new Color(0.545f, 0.580f, 0.565f, 1f);
        internal static readonly Color TextOff   = new Color(0.355f, 0.385f, 0.375f, 1f);
        internal static readonly Color TextOn    = new Color(0.96f,  0.97f,  0.965f, 1f);
        internal static readonly Color Accent    = new Color(0.62f,  0.70f,  0.66f, 1f);
        internal static readonly Color Danger    = new Color(0.78f,  0.33f,  0.29f, 1f);
        internal static readonly Color DangerBg  = new Color(0.48f,  0.16f,  0.14f, 0.98f);
        internal static readonly Color Grip      = new Color(0.45f,  0.49f,  0.47f, 0.75f);

        internal static Canvas        Canvas;
        internal static Camera        Cam;
        internal static RectTransform CanvasRect;
        internal static TMP_FontAsset Font;
        internal static Material      FontMat;
        internal static Sprite        Fill, Edge;

        internal static bool Bind(Canvas c)
        {
            if (c == null) return false;
            Canvas = c;
            Cam = c.renderMode == RenderMode.ScreenSpaceOverlay ? null : c.worldCamera;
            CanvasRect = c.transform.TryCast<RectTransform>();
            if (Fill == null) Fill = MakeRounded(32, 3, 0, false);
            if (Edge == null) Edge = MakeRounded(32, 3, 2, true);
            return true;
        }

        internal static void BorrowFont(TMP_Text src)
        {
            if (src == null || Font != null) return;
            try { Font = src.font; } catch { }
            try { FontMat = src.fontSharedMaterial; } catch { }
            try
            {
                var c = src.color;
                if (c.a > 0.5f && c.r + c.g + c.b > 1.5f) Text = c;
            }
            catch { }
        }

        internal static void Drop()
        {
            Canvas = null; Cam = null; CanvasRect = null;
            Font = null; FontMat = null;
            Fill = null; Edge = null;
        }

        internal static Vector2 CanvasSize()
        {
            try
            {
                if (CanvasRect != null)
                {
                    var r = CanvasRect.rect;
                    if (r.width > 1f && r.height > 1f) return new Vector2(r.width, r.height);
                }
                float sw = Screen.width, sh = Screen.height;
                var scaler = Canvas != null ? Canvas.GetComponent<CanvasScaler>() : null;
                if (scaler != null && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize
                    && scaler.referenceResolution.x > 1f && scaler.referenceResolution.y > 1f)
                {
                    float rw = scaler.referenceResolution.x, rh = scaler.referenceResolution.y, k;
                    switch (scaler.screenMatchMode)
                    {
                        case CanvasScaler.ScreenMatchMode.Expand: k = Mathf.Min(sw / rw, sh / rh); break;
                        case CanvasScaler.ScreenMatchMode.Shrink: k = Mathf.Max(sw / rw, sh / rh); break;
                        default:
                            float m = Mathf.Clamp01(scaler.matchWidthOrHeight);
                            k = Mathf.Pow(2f, Mathf.Lerp(Mathf.Log(sw / rw, 2f), Mathf.Log(sh / rh, 2f), m));
                            break;
                    }
                    if (k > 0.0001f) return new Vector2(sw / k, sh / k);
                }
                if (Canvas != null && Canvas.scaleFactor > 0.0001f) return new Vector2(sw / Canvas.scaleFactor, sh / Canvas.scaleFactor);
            }
            catch { }
            return new Vector2(Screen.width > 1 ? Screen.width : 1920f, Screen.height > 1 ? Screen.height : 1080f);
        }

        internal static float CanvasHeight() { return CanvasSize().y; }
        internal static float CanvasWidth() { return CanvasSize().x; }

        internal static bool In(RectTransform rt, Vector2 screenPoint)
        {
            if (rt == null) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(rt, screenPoint, Cam);
        }

        internal static bool CanvasPoint(Vector2 mp, out Vector2 p)
        {
            p = Vector2.zero;
            if (CanvasRect == null) return false;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(CanvasRect, mp, Cam, out p);
        }

        internal static bool Frac(RectTransform rt, Vector2 mp, out float f)
        {
            f = 0f;
            Vector2 local;
            if (rt == null) return false;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, mp, Cam, out local)) return false;
            float w = rt.rect.width;
            if (w <= 0f) return false;
            f = Mathf.Clamp01((local.x - rt.rect.xMin) / w);
            return true;
        }

        internal static RectTransform Rect(Transform parent, string name,
                                           float x, float y, float w, float h, Vector2 anchor)
        {
            var go = new GameObject(name);
            if (parent != null) go.layer = parent.gameObject.layer;
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = anchor;
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            rt.localScale = Vector3.one;
            return rt;
        }

        internal static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        { return Rect(parent, name, x, y, w, h, new Vector2(0f, 1f)); }

        internal static Image Img(RectTransform rt, Sprite sp, Color c, bool raycast)
        {
            var img = rt.gameObject.AddComponent<Image>();
            if (sp != null)
            {
                img.sprite = sp;
                img.type = Image.Type.Sliced;
                float refPpu = 100f;
                try { if (Canvas != null) refPpu = Canvas.referencePixelsPerUnit; } catch { }
                if (refPpu <= 0f) refPpu = 100f;
                img.pixelsPerUnitMultiplier = refPpu / 100f;
            }
            img.color = c;
            img.raycastTarget = raycast;
            return img;
        }

        internal static TextMeshProUGUI Text_(RectTransform rt, string s, float size, bool bold,
                                              Color c, TextAlignmentOptions align)
        {
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();

            if (Font != null) t.font = Font;
            if (FontMat != null) t.fontSharedMaterial = FontMat;
            t.fontSize = size;
            t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            t.color = c;
            t.alignment = align;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.richText = false;
            t.raycastTarget = false;
            t.margin = new Vector4(2f, 0f, 2f, 0f);
            t.text = s;
            return t;
        }

        internal static Sprite MakeRounded(int size, int radius, int stroke, bool ring)
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
                    if (ring) a = Mathf.Clamp01(a - Mathf.Clamp01(inside - stroke + 0.5f));

                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply(false, false);

            var sp = Sprite.Create(tex, new UnityEngine.Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f),
                                   100f, 0u, SpriteMeshType.FullRect,
                                   new Vector4(radius, radius, radius, radius));
            if (sp != null) sp.hideFlags = HideFlags.HideAndDontSave;
            return sp;
        }
    }

    internal class Btn
    {
        internal RectTransform Rt;
        internal Image Fill, Edge;
        internal TextMeshProUGUI Label;
        internal bool On = true, Hot, Danger, Armed;
        internal object Tag;
        Color _lf, _le, _lt; string _ll;

        internal static Btn Make(RectTransform parent, string name, float x, float y, float w, float h,
                                 string label, float size = 13f, float spacing = 2f)
        {
            var b = new Btn();
            b.Rt    = Skin.Rect(parent, name, x, y, w, h);
            b.Fill  = Skin.Img(b.Rt, Skin.Fill, Skin.Btn, false);
            var e   = Skin.Rect(b.Rt, "Edge", 0f, 0f, w, h);
            b.Edge  = Skin.Img(e, Skin.Edge, Skin.BtnEdge, false);
            var t   = Skin.Rect(b.Rt, "Label", 0f, 0f, w, h);
            b.Label = Skin.Text_(t, label, size, true, Skin.Text, TextAlignmentOptions.Center);
            b.Label.characterSpacing = spacing;
            return b;
        }

        internal void SetLabel(string s)
        {
            if (_ll == s || Label == null) return;
            Label.text = s; _ll = s;
        }

        internal void Paint()
        {
            if (Fill == null) return;
            Color f, e, t;
            if (!On)                       { f = Skin.BtnOff;   e = new Color(Skin.BtnEdge.r, Skin.BtnEdge.g, Skin.BtnEdge.b, 0.15f); t = Skin.TextOff; }
            else if (Danger && (Armed || Hot)) { f = Skin.DangerBg; e = Skin.Danger;  t = Skin.Text; }
            else if (Hot)                  { f = Skin.BtnHot;   e = Skin.BtnHot;  t = Skin.TextOn; }
            else if (Danger)               { f = Skin.Btn;      e = Skin.BtnEdge; t = Skin.TextDim; }
            else                           { f = Skin.Btn;      e = Skin.BtnEdge; t = Skin.Text; }

            if (_lf != f) { Fill.color = f;  _lf = f; }
            if (_le != e) { Edge.color = e;  _le = e; }
            if (_lt != t) { Label.color = t; _lt = t; }
        }

        internal bool Poll(Vector2 mp, bool down, bool blocked)
        {
            bool hot = On && !blocked && Skin.In(Rt, mp);
            if (hot != Hot) { Hot = hot; Paint(); }
            return hot && down;
        }
    }

    internal class OdgSlider
    {
        internal RectTransform Track;
        Image _fill, _knob;
        RectTransform _knobRt, _fillRt;
        internal float Min, Max, Value;
        internal bool Dragging;
        float _w, _h;

        internal static OdgSlider Make(RectTransform parent, string name, float x, float y, float w, float h,
                                    float min, float max, float value)
        {
            var s = new OdgSlider { Min = min, Max = max, Value = value, _w = w, _h = h };
            s.Track = Skin.Rect(parent, name, x, y, w, h);
            Skin.Img(s.Track, Skin.Fill, new Color(0f, 0f, 0f, 0.35f), false);

            s._fillRt = Skin.Rect(s.Track, "Fill", 0f, 0f, w, h);
            s._fill   = Skin.Img(s._fillRt, Skin.Fill, Skin.Btn, false);

            s._knobRt = Skin.Rect(s.Track, "Knob", 0f, -3f, 10f, h + 6f);
            s._knob   = Skin.Img(s._knobRt, Skin.Fill, Skin.BtnHot, false);

            s.Apply();
            return s;
        }

        internal void Apply()
        {
            float f = Max > Min ? Mathf.Clamp01((Value - Min) / (Max - Min)) : 0f;
            if (_fillRt != null) _fillRt.sizeDelta = new Vector2(Mathf.Max(2f, _w * f), _h);
            if (_knobRt != null) _knobRt.anchoredPosition = new Vector2(_w * f - 5f, 3f);
        }

        internal void Set(float v)
        {
            v = Mathf.Clamp(v, Min, Max);
            if (Mathf.Abs(v - Value) < 0.00001f) return;
            Value = v; Apply();
        }

        internal bool Poll(Vector2 mp, bool down, bool blocked)
        {
            if (!Input.GetMouseButton(0)) Dragging = false;

            bool over = !blocked && Skin.In(Track, mp);
            var kc = (over || Dragging) ? Skin.TextOn : Skin.BtnHot;
            if (_knob != null && _knob.color != kc) _knob.color = kc;

            if (down && over) Dragging = true;
            if (!Dragging) return false;

            float f;
            if (Skin.Frac(Track, mp, out f)) Set(Min + f * (Max - Min));
            return true;
        }
    }

    internal class Chrome
    {
        internal GameObject   Root;
        internal RectTransform Rt, Body, TitleGrab, GripRt;
        Image _gripA, _gripB;

        internal float PosX, PosY, Scale, DesignW, DesignH;
        internal bool  Dirty;
        internal bool  GripOn = true;
        bool  _move, _size;
        internal bool Dragging { get { return _move || _size; } }
        Vector2 _from;
        float _sizeFrom, _distFrom;

        internal const float MIN_SCALE = 0.35f, MAX_SCALE = 1.60f;
        const float GRIP = 26f;

        internal const float AUTO_REF_H = 1210f;

        internal const float UNSET = -99999f;

        internal bool Build(Canvas canvas, string name, float designW, float designH,
                            float x, float y, float scale, float refH, int sortOffset)
        {
            if (!Skin.Bind(canvas)) return false;
            DesignW = designW; DesignH = designH;

            Root = new GameObject(name);
            Root.layer = canvas.gameObject.layer;
            Rt = Root.AddComponent<RectTransform>();
            Rt.SetParent(canvas.transform, false);
            Rt.anchorMin = new Vector2(0f, 0.5f);
            Rt.anchorMax = new Vector2(0f, 0.5f);
            Rt.pivot     = new Vector2(0f, 0.5f);
            Rt.sizeDelta = new Vector2(designW, designH);
            Rt.SetAsLastSibling();

            try
            {
                var sub = Root.AddComponent<Canvas>();
                sub.overrideSorting = true;

                sub.sortingOrder = canvas.sortingOrder + sortOffset;
                Root.AddComponent<GraphicRaycaster>();
            }
            catch { }

            try
            {
                if (canvas.GetComponent<LayoutGroup>() != null)
                    Root.AddComponent<LayoutElement>().ignoreLayout = true;
            }
            catch { }

            float k = scale;
            if (k <= 0f) k = Skin.CanvasHeight() / (refH > 1f ? refH : AUTO_REF_H);
            Scale = Mathf.Clamp(k, MIN_SCALE, MAX_SCALE);
            Rt.localScale = new Vector3(Scale, Scale, 1f);

            float cw = Skin.CanvasWidth(), ch = Skin.CanvasHeight();
            PosX = x <= UNSET + 1f ? cw - designW * Scale - 40f : x;
            PosY = y <= UNSET + 1f ? ch * 0.5f - 170f - designH * Scale * 0.5f : y;
            Clamp();

            Body = Skin.Rect(Rt, "Panel", 0f, 0f, designW, designH);
            Skin.Img(Body, Skin.Fill, Skin.Panel, true);
            var edge = Skin.Rect(Body, "Edge", 0f, 0f, designW, designH);
            Skin.Img(edge, Skin.Edge, Skin.PanelEdge, false);
            return true;
        }

        internal void AddChrome(float headH)
        {
            TitleGrab = Skin.Rect(Body, "TitleGrab", 0f, 0f, DesignW, headH);

            GripRt = Skin.Rect(Body, "Grip", DesignW - GRIP - 3f, DesignH - GRIP - 3f, GRIP, GRIP);
            var a = Skin.Rect(GripRt, "a", 4f, -4f, 17f, 2.5f, new Vector2(0.5f, 0.5f));
            a.localRotation = Quaternion.Euler(0f, 0f, -45f);
            _gripA = Skin.Img(a, null, Skin.Grip, false);
            var b = Skin.Rect(GripRt, "b", 8f, -8f, 9f, 2.5f, new Vector2(0.5f, 0.5f));
            b.localRotation = Quaternion.Euler(0f, 0f, -45f);
            _gripB = Skin.Img(b, null, Skin.Grip, false);
        }

        internal void SetHeight(float h)
        {
            DesignH = h;
            if (Rt != null) Rt.sizeDelta = new Vector2(DesignW, h);
            if (Body != null)
            {
                Body.sizeDelta = new Vector2(DesignW, h);
                var e = Body.Find("Edge");
                if (e != null) { var ert = e.TryCast<RectTransform>(); if (ert != null) ert.sizeDelta = new Vector2(DesignW, h); }
            }
            if (GripRt != null)
            {
                GripRt.anchoredPosition = new Vector2(DesignW - GRIP - 3f, -(h - GRIP - 3f));
                GripRt.gameObject.SetActive(GripOn);
            }
            Clamp();
        }

        internal void Clamp()
        {
            if (Rt == null) return;
            float w = DesignW * Scale, h = DesignH * Scale;
            float cw = Skin.CanvasWidth(), ch = Skin.CanvasHeight();
            PosX = Mathf.Clamp(PosX, 150f - w, cw - 150f);
            float top = Mathf.Clamp(PosY + h * 0.5f, -ch * 0.5f + 90f, ch * 0.5f);
            PosY = top - h * 0.5f;
            Rt.anchoredPosition = new Vector2(PosX, PosY);
        }

        Vector2 Pivot() { return new Vector2(-Skin.CanvasWidth() * 0.5f + PosX, PosY); }

        internal bool Drag(Vector2 mp, bool down)
        {
            bool held = Input.GetMouseButton(0);
            if (!held && (_move || _size)) { _move = _size = false; return true; }

            bool overGrip = GripOn && Skin.In(GripRt, mp);
            var gc = (overGrip || _size) ? Skin.Text : Skin.Grip;
            if (_gripA != null && _gripA.color != gc) { _gripA.color = gc; _gripB.color = gc; }

            if (down && !_move && !_size)
            {
                Vector2 c;
                if (!Skin.CanvasPoint(mp, out c)) return false;
                if (overGrip)
                {

                    _size = true; _sizeFrom = Scale;
                    _distFrom = Mathf.Max(1f, (c - Pivot()).magnitude);
                    return true;
                }
                if (Skin.In(TitleGrab, mp)) { _move = true; _from = c; return true; }
                return false;
            }

            if (_size)
            {
                Vector2 c;
                if (Skin.CanvasPoint(mp, out c))
                {
                    Scale = Mathf.Clamp(_sizeFrom * ((c - Pivot()).magnitude / _distFrom), MIN_SCALE, MAX_SCALE);
                    Rt.localScale = new Vector3(Scale, Scale, 1f);
                    Clamp(); Dirty = true;
                }
                return true;
            }

            if (_move)
            {
                Vector2 c;
                if (Skin.CanvasPoint(mp, out c))
                {
                    PosX += c.x - _from.x; PosY += c.y - _from.y; _from = c;
                    Clamp(); Dirty = true;
                }
                return true;
            }
            return false;
        }

        internal void Destroy()
        {
            try { if (Root != null) UnityEngine.Object.Destroy(Root); } catch { }
            Root = null; Rt = null; Body = null; TitleGrab = null; GripRt = null;
            _gripA = null; _gripB = null; _move = false; _size = false;
        }
    }
}
