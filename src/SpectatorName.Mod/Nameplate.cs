// ODG Operations - SpectatorName
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using UnityEngine;
using UnityEngine.UI;

namespace SpectatorName
{

    internal sealed class Nameplate
    {
        private const float PanelHeightWithLabel = 74f;
        private const float PanelHeightNameOnly = 54f;
        private const float MinPanelWidth = 340f;
        private const float SidePadding = 72f;

        private const int NameFontSize = 34;
        private const int LabelFontSize = 15;

        private static readonly Color Backing = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color Accent = new Color(1f, 0.85f, 0.35f, 0.95f);
        private static readonly Color LabelColour = new Color(1f, 0.85f, 0.35f, 0.95f);
        private static readonly Color NameColour = new Color(1f, 1f, 1f, 1f);
        private static readonly Color OutlineColour = new Color(0f, 0f, 0f, 0.85f);

        private Canvas _canvas;
        private RectTransform _root;
        private RectTransform _backing;
        private RectTransform _accent;
        private Text _label;
        private Text _name;

        private bool _visible = true;
        private string _appliedName;
        private float _appliedOffset = float.NaN;
        private float _appliedScale = float.NaN;
        private bool _appliedShowLabel = true;

        internal Nameplate()
        {
            var font = ResolveFont();

            _canvas = BuildCanvas();
            _root = BuildRoot(_canvas.transform);

            _backing = BuildBacking(_root);
            _accent = BuildAccent(_root);
            _label = BuildLabel(_root, font);
            _name = BuildName(_root, font);
        }

        internal void SetVisible(bool visible)
        {
            if (_visible == visible) return;
            _visible = visible;
            try { _root.gameObject.SetActive(visible); } catch { }
        }

        internal void SetName(string text)
        {
            if (text == _appliedName) return;
            _appliedName = text;

            try
            {
                _name.text = text ?? "";
                Resize();
            }
            catch { }
        }

        internal void Apply(float bottomOffset, float scale, bool showLabel)
        {
            if (scale <= 0.01f) scale = 1f;

            if (bottomOffset == _appliedOffset && scale == _appliedScale && showLabel == _appliedShowLabel)
                return;

            _appliedOffset = bottomOffset;
            _appliedScale = scale;
            _appliedShowLabel = showLabel;

            try
            {
                _root.anchoredPosition = new Vector2(0f, bottomOffset);
                _root.localScale = new Vector3(scale, scale, 1f);

                _label.gameObject.SetActive(showLabel);

                _name.rectTransform.anchoredPosition = new Vector2(0f, showLabel ? -30f : -8f);

                Resize();
            }
            catch { }
        }

        private void Resize()
        {
            float w = MinPanelWidth;
            try
            {
                float needed = _name.preferredWidth + SidePadding;
                if (needed > w) w = needed;
            }
            catch { }

            float h = _appliedShowLabel ? PanelHeightWithLabel : PanelHeightNameOnly;

            _root.sizeDelta = new Vector2(w, h);
            _backing.sizeDelta = new Vector2(w, h);
            _accent.sizeDelta = new Vector2(w, 3f);
            _label.rectTransform.sizeDelta = new Vector2(w, 20f);
            _name.rectTransform.sizeDelta = new Vector2(w, 40f);
        }

        private static Canvas BuildCanvas()
        {
            var go = new GameObject("SpectatorName_Canvas");
            GameObject.DontDestroyOnLoad(go);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 400;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            return canvas;
        }

        private static RectTransform BuildRoot(Transform parent)
        {
            var go = new GameObject("SpectatorName_Root");
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 150f);
            rt.sizeDelta = new Vector2(MinPanelWidth, PanelHeightWithLabel);
            return rt;
        }

        private static RectTransform Child(string name, Transform parent, float y, Vector2 size)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta = size;
            return rt;
        }

        private static RectTransform BuildBacking(RectTransform root)
        {
            var rt = Child("Backing", root, 0f, new Vector2(MinPanelWidth, PanelHeightWithLabel));
            var img = rt.gameObject.AddComponent<Image>();
            img.color = Backing;
            img.raycastTarget = false;
            return rt;
        }

        private static RectTransform BuildAccent(RectTransform root)
        {
            var rt = Child("Accent", root, 0f, new Vector2(MinPanelWidth, 3f));
            var img = rt.gameObject.AddComponent<Image>();
            img.color = Accent;
            img.raycastTarget = false;
            return rt;
        }

        private static Text BuildLabel(RectTransform root, Font font)
        {
            var rt = Child("Label", root, -8f, new Vector2(MinPanelWidth, 20f));
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font;
            t.text = "S P E C T A T I N G";
            t.fontSize = LabelFontSize;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.color = LabelColour;
            AddOutline(rt, 1f);
            return t;
        }

        private static Text BuildName(RectTransform root, Font font)
        {
            var rt = Child("Name", root, -30f, new Vector2(MinPanelWidth, 40f));
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font;
            t.text = "";
            t.fontSize = NameFontSize;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.color = NameColour;
            AddOutline(rt, 1.6f);
            return t;
        }

        private static void AddOutline(RectTransform rt, float distance)
        {
            try
            {
                var o = rt.gameObject.AddComponent<Outline>();
                o.effectColor = OutlineColour;
                o.effectDistance = new Vector2(distance, distance);
                o.useGraphicAlpha = true;
            }
            catch { }
        }

        private static Font ResolveFont()
        {
            Font f = null;

            try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            if (f != null) return f;

            try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
            if (f != null) return f;

            try
            {
                var all = Resources.FindObjectsOfTypeAll<Font>();
                if (all != null && all.Length > 0) f = all[0];
            }
            catch { }
            if (f != null) return f;

            try { f = Font.CreateDynamicFontFromOSFont("Arial", 16); } catch { }
            return f;
        }
    }
}
