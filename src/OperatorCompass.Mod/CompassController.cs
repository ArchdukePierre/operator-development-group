// ODG Operations - OperatorCompass
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using UnityEngine;
using UnityEngine.UI;

namespace OperatorCompass
{

    public class CompassController
    {
        private Transform _player;

        private readonly RectTransform _root;
        private readonly RectTransform _caret;
        private readonly RectTransform _objectiveMarker;
        private readonly Text _headingText;

        private readonly TickEntry[] _ticks;
        private readonly LabelEntry[] _labels;

        private float _smoothedHeading;
        private float _rotationVelocity;
        private const float RotationSmoothTime = 0.06f;

        private const float PixelsPerDegree = 5f;
        private const float VisibleHalfRangeDegrees = 55f;
        private const int MinorTickStepDegrees = 5;
        private const int LabelStepDegrees = 15;

        private const float BandWidth = 560f;
        private const float BandHeight = 62f;

        private float _appliedOffset = float.NaN;
        private float _appliedScale = float.NaN;
        private bool _appliedHeadingNumber;
        private bool _visible = true;
        private int _lastHeadingShown = -1;

        private static readonly string[] CardinalNames = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        private static readonly Color TickMajor = new Color(1f, 1f, 1f, 0.92f);
        private static readonly Color TickMinor = new Color(1f, 1f, 1f, 0.40f);
        private static readonly Color LabelCardinal = new Color(1f, 1f, 1f, 1f);
        private static readonly Color LabelNorth = new Color(1f, 0.42f, 0.35f, 1f);
        private static readonly Color LabelMinor = new Color(1f, 1f, 1f, 0.62f);

        private struct TickEntry
        {
            public int Degrees;
            public RectTransform Rect;
        }

        private struct LabelEntry
        {
            public int Degrees;
            public RectTransform Rect;
        }

        public CompassController(Transform hudCanvas, Transform player)
        {
            _player = player;

            var font = ResolveFont();

            _root = BuildRoot(hudCanvas);

            BuildBacking(_root);
            _ticks = BuildTicks(_root);
            _labels = BuildLabels(_root, font);
            _objectiveMarker = BuildObjectiveMarker(_root);
            _caret = BuildCaret(_root);
            _headingText = BuildHeadingText(_root, font);
        }

        public void SetPlayer(Transform player)
        {
            _player = player;
        }

        public void SetVisible(bool visible)
        {
            if (_visible == visible) return;
            _visible = visible;
            _root.gameObject.SetActive(visible);
        }

        public void Apply(float topOffset, float scale, bool showHeadingNumber)
        {
            if (scale <= 0.01f) scale = 1f;

            if (topOffset == _appliedOffset && scale == _appliedScale
                && showHeadingNumber == _appliedHeadingNumber)
                return;

            _appliedOffset = topOffset;
            _appliedScale = scale;
            _appliedHeadingNumber = showHeadingNumber;

            _root.anchorMin = new Vector2(0.5f, 1f);
            _root.anchorMax = new Vector2(0.5f, 1f);
            _root.pivot = new Vector2(0.5f, 1f);
            _root.anchoredPosition = new Vector2(0f, -topOffset);

            _root.localScale = new Vector3(scale, scale, 1f);

            if (_headingText.gameObject.activeSelf != showHeadingNumber)
                _headingText.gameObject.SetActive(showHeadingNumber);
        }

        public void Tick()
        {
            float rawHeading = _player.eulerAngles.y;

            _smoothedHeading = Mathf.SmoothDampAngle(
                _smoothedHeading, rawHeading, ref _rotationVelocity, RotationSmoothTime);

            UpdateTicks();
            UpdateLabels();
            UpdateHeadingText();
            UpdateObjectiveMarker();
        }

        private void UpdateTicks()
        {
            for (int i = 0; i < _ticks.Length; i++)
            {
                var entry = _ticks[i];
                float delta = Mathf.DeltaAngle(_smoothedHeading, entry.Degrees);
                bool onScreen = Mathf.Abs(delta) <= VisibleHalfRangeDegrees;

                if (entry.Rect.gameObject.activeSelf != onScreen)
                    entry.Rect.gameObject.SetActive(onScreen);
                if (!onScreen) continue;

                entry.Rect.anchoredPosition = new Vector2(delta * PixelsPerDegree, entry.Rect.anchoredPosition.y);
            }
        }

        private void UpdateLabels()
        {
            for (int i = 0; i < _labels.Length; i++)
            {
                var entry = _labels[i];
                float delta = Mathf.DeltaAngle(_smoothedHeading, entry.Degrees);
                bool onScreen = Mathf.Abs(delta) <= VisibleHalfRangeDegrees;

                if (entry.Rect.gameObject.activeSelf != onScreen)
                    entry.Rect.gameObject.SetActive(onScreen);
                if (!onScreen) continue;

                entry.Rect.anchoredPosition = new Vector2(delta * PixelsPerDegree, entry.Rect.anchoredPosition.y);
            }
        }

        private void UpdateHeadingText()
        {
            if (!_appliedHeadingNumber) return;

            int shown = Mathf.RoundToInt(_smoothedHeading);
            shown = ((shown % 360) + 360) % 360;
            if (shown == _lastHeadingShown) return;

            _lastHeadingShown = shown;
            _headingText.text = shown.ToString("000");
        }

        private void UpdateObjectiveMarker()
        {

            var objective = FindNearestObjective();
            if (objective == null)
            {
                if (_objectiveMarker.gameObject.activeSelf)
                    _objectiveMarker.gameObject.SetActive(false);
                return;
            }

            Vector3 toObjective = objective.position - _player.position;
            toObjective.y = 0f;
            float bearing = Vector3.SignedAngle(Vector3.forward, toObjective, Vector3.up);
            float delta = Mathf.DeltaAngle(_smoothedHeading, bearing);
            bool onScreen = Mathf.Abs(delta) <= VisibleHalfRangeDegrees;

            if (_objectiveMarker.gameObject.activeSelf != onScreen)
                _objectiveMarker.gameObject.SetActive(onScreen);
            if (!onScreen) return;

            _objectiveMarker.anchoredPosition =
                new Vector2(delta * PixelsPerDegree, _objectiveMarker.anchoredPosition.y);
        }

        private Transform FindNearestObjective()
        {
            return null;
        }

        private static string LabelTextFor(int degrees)
        {
            int normalized = ((degrees % 360) + 360) % 360;
            if (normalized % 45 == 0) return CardinalNames[normalized / 45];
            return normalized.ToString();
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

        private static RectTransform NewChild(string name, Transform parent, float y, Vector2 size)
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

        private static Image NewImage(RectTransform rt, Color color)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private RectTransform BuildRoot(Transform hudCanvas)
        {
            var go = new GameObject("Compass_Root");
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(hudCanvas, false);
            rt.sizeDelta = new Vector2(BandWidth, BandHeight);

            var mask = go.AddComponent<RectMask2D>();
            mask.softness = new Vector2Int(48, 0);

            return rt;
        }

        private void BuildBacking(RectTransform root)
        {
            var rt = NewChild("Compass_Backing", root, 0f, new Vector2(BandWidth, BandHeight));
            NewImage(rt, new Color(0f, 0f, 0f, 0.32f));
        }

        private TickEntry[] BuildTicks(RectTransform root)
        {
            var ticks = new TickEntry[360 / MinorTickStepDegrees];
            int i = 0;

            for (int degrees = 0; degrees < 360; degrees += MinorTickStepDegrees)
            {
                bool isMajor = degrees % LabelStepDegrees == 0;

                var rt = NewChild("Tick_" + degrees, root, -8f,
                    isMajor ? new Vector2(2f, 14f) : new Vector2(1f, 8f));
                NewImage(rt, isMajor ? TickMajor : TickMinor);

                ticks[i++] = new TickEntry { Degrees = degrees, Rect = rt };
            }

            return ticks;
        }

        private LabelEntry[] BuildLabels(RectTransform root, Font font)
        {
            var labels = new LabelEntry[360 / LabelStepDegrees];
            int i = 0;

            for (int degrees = 0; degrees < 360; degrees += LabelStepDegrees)
            {
                var rt = NewChild("Label_" + degrees, root, -26f, new Vector2(44f, 22f));

                bool isCardinal = degrees % 45 == 0;

                var text = rt.gameObject.AddComponent<Text>();
                text.font = font;
                text.alignment = TextAnchor.MiddleCenter;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.verticalOverflow = VerticalWrapMode.Overflow;
                text.raycastTarget = false;
                text.text = LabelTextFor(degrees);
                text.fontSize = isCardinal ? 18 : 13;
                text.fontStyle = isCardinal ? FontStyle.Bold : FontStyle.Normal;
                text.color = degrees == 0 ? LabelNorth : (isCardinal ? LabelCardinal : LabelMinor);

                labels[i++] = new LabelEntry { Degrees = degrees, Rect = rt };
            }

            return labels;
        }

        private RectTransform BuildCaret(RectTransform root)
        {
            var rt = NewChild("Compass_Caret", root, -4f, new Vector2(2f, 20f));
            NewImage(rt, new Color(1f, 0.85f, 0.35f, 1f));
            return rt;
        }

        private Text BuildHeadingText(RectTransform root, Font font)
        {
            var rt = NewChild("Compass_Heading", root, -40f, new Vector2(80f, 22f));

            var text = rt.gameObject.AddComponent<Text>();
            text.font = font;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.fontSize = 15;
            text.fontStyle = FontStyle.Bold;
            text.color = new Color(1f, 0.85f, 0.35f, 0.95f);
            text.text = "000";

            return text;
        }

        private RectTransform BuildObjectiveMarker(RectTransform root)
        {
            var rt = NewChild("Compass_ObjectiveMarker", root, -2f, new Vector2(10f, 10f));
            NewImage(rt, new Color(1f, 0.8f, 0.2f, 1f));
            rt.gameObject.SetActive(false);
            return rt;
        }
    }
}
