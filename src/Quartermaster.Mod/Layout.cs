// ODG Operations - Quartermaster
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using OdgUi;

namespace Quartermaster
{

    internal static class Layout
    {
        internal static Camera CamOf(Transform t)
        {
            try
            {
                var cv = t != null ? t.GetComponentInParent<Canvas>() : null;
                if (cv == null) return null;
                var root = cv.rootCanvas; if (root != null) cv = root;
                return cv.renderMode == RenderMode.ScreenSpaceOverlay ? null : cv.worldCamera;
            }
            catch { return null; }
        }

        internal static bool CanvasRect(RectTransform rt, out Rect r)
        {
            r = new Rect();
            if (rt == null) return false;
            try
            {
                var corners = new Il2CppStructArray<Vector3>(4);
                rt.GetWorldCorners(corners);
                var cam = CamOf(rt);
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                for (int i = 0; i < 4; i++)
                {
                    Vector2 sp = RectTransformUtility.WorldToScreenPoint(cam, corners[i]);
                    Vector2 lp;
                    if (!Skin.CanvasPoint(sp, out lp)) return false;
                    if (lp.x < minX) minX = lp.x; if (lp.x > maxX) maxX = lp.x;
                    if (lp.y < minY) minY = lp.y; if (lp.y > maxY) maxY = lp.y;
                }
                r = Rect.MinMaxRect(minX, minY, maxX, maxY);
                return true;
            }
            catch { return false; }
        }

        internal static bool VisibleExtent(Transform panel, out Rect ext, out int used)
        {
            ext = new Rect(); used = 0;
            if (panel == null) return false;
            try
            {
                if (!panel.gameObject.activeInHierarchy) return false;
                float cw = Skin.CanvasWidth(), ch = Skin.CanvasHeight();
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                var gs = panel.GetComponentsInChildren<Graphic>(false);
                for (int i = 0; gs != null && i < gs.Length; i++)
                {
                    var g = gs[i];
                    try
                    {
                        if (g == null || !g.enabled || g.color.a < 0.02f) continue;
                        var rt = g.rectTransform; if (rt == null) continue;
                        Rect r;
                        if (!CanvasRect(rt, out r)) continue;
                        if (r.width < 4f || r.height < 4f) continue;
                        if (r.width > cw * 0.6f) continue;
                        float x0 = Math.Max(r.xMin, -cw * 0.5f), x1 = Math.Min(r.xMax, cw * 0.5f);
                        float y0 = Math.Max(r.yMin, -ch * 0.5f), y1 = Math.Min(r.yMax, ch * 0.5f);
                        if (x1 <= x0 || y1 <= y0) continue;
                        if (x0 < minX) minX = x0; if (x1 > maxX) maxX = x1;
                        if (y0 < minY) minY = y0; if (y1 > maxY) maxY = y1;
                        used++;
                    }
                    catch { }
                }
                if (used == 0)
                {
                    var prt = panel.TryCast<RectTransform>();
                    Rect r;
                    if (prt != null && CanvasRect(prt, out r) && r.width >= 4f && r.width <= cw * 0.6f) { ext = r; used = 1; return true; }
                    return false;
                }
                ext = Rect.MinMaxRect(minX, minY, maxX, maxY);
                return true;
            }
            catch { return false; }
        }

        internal const float MIN_TILE = 150f, MAX_TILE = 275f, TILE_GAP = 6f, BAND_PAD = 10f;

        static float BandWidth(int cols, float tile) { return cols * tile + (cols - 1) * TILE_GAP + 2f * BAND_PAD; }

        internal static void Fit(float avail, float k0, out float designW, out float scale, out int cols, out string why)
        {
            int[] tries = { 6, 4, 3 };
            for (int i = 0; i < tries.Length; i++)
            {
                int c = tries[i];
                float minW = BandWidth(c, MIN_TILE), maxW = BandWidth(c, MAX_TILE);
                if (avail / k0 >= minW)
                {
                    if (avail / k0 <= maxW) { scale = k0; designW = avail / k0; }
                    else { scale = Math.Min(1.15f * k0, avail / maxW); designW = Math.Min(maxW, avail / scale); }
                    cols = c;
                    why = c + " columns at scale " + scale.ToString("0.00") + (scale > k0 + 0.001f ? " (upscaled to fill the span)" : " (the type size shared with the other panels)");
                    return;
                }
                float k = avail / minW;
                if (k >= 0.75f * k0)
                {
                    scale = k; designW = minW; cols = c;
                    why = c + " columns at scale " + scale.ToString("0.00") + " (type shrunk to fit the span)";
                    return;
                }
            }
            cols = 3;
            designW = BandWidth(3, MIN_TILE);
            scale = Math.Max(0.5f * k0, avail / designW);
            why = "3 columns at the floor scale " + scale.ToString("0.00") + "; the span is narrower than the band";
        }

        internal static int ColsFor(float designW)
        {
            if (designW >= BandWidth(6, MIN_TILE) - 0.5f) return 6;
            if (designW >= BandWidth(4, MIN_TILE) - 0.5f) return 4;
            return 3;
        }

        internal static int DetectBand(Transform partsPanel, Transform presetsPanel, out float left, out float right, out string why)
        {
            float cw = Skin.CanvasWidth();
            left = -cw * 0.5f + 8f; right = cw * 0.5f - 8f;
            var sb = new StringBuilder();
            int found = 0;
            var panels = new[] { partsPanel, presetsPanel };
            var names = new[] { "parts", "presets" };
            for (int i = 0; i < 2; i++)
            {
                Rect r; int used;
                if (!VisibleExtent(panels[i], out r, out used)) { sb.Append(names[i]).Append(" not visible; "); continue; }
                found++;
                if (r.center.x < 0f) { if (r.xMax > left) left = r.xMax; }
                else { if (r.xMin < right) right = r.xMin; }
                sb.Append(names[i]).Append('[').Append(r.xMin.ToString("0")).Append("..").Append(r.xMax.ToString("0")).Append("] ").Append(used).Append(" graphics; ");
            }
            why = "canvas " + cw.ToString("0") + " wide; " + sb;
            if (right - left < 600f)
            {
                left = -cw * 0.31f; right = cw * 0.31f;
                why += "too narrow, using the centre 62 %";
                return 0;
            }
            return found;
        }
    }
}
