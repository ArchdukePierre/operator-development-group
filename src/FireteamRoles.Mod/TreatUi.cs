// ODG Operations - FireteamRoles
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using UnityEngine;

namespace FireteamRoles
{

    internal static partial class Ui
    {
        internal static int DragItem = -1;
        static int _hoverLimb = -1;
        static Texture2D _limbOk, _limbBad, _limbHot, _slotBg, _slotSel;

        struct LimbBox { internal Rect R; internal int Index; }
        static readonly List<LimbBox> _limbBoxes = new List<LimbBox>();

        static void TreatInit()
        {
            if (_limbOk != null) return;
            _limbOk = Solid(new Color(0.20f, 0.38f, 0.24f, 0.85f));
            _limbBad = Solid(new Color(0.58f, 0.16f, 0.16f, 0.92f));
            _limbHot = Solid(new Color(0.95f, 0.78f, 0.32f, 0.95f));
            _slotBg = Solid(new Color(0.15f, 0.16f, 0.18f, 1f));
            _slotSel = Solid(new Color(0.26f, 0.30f, 0.34f, 1f));
        }

        static void DrawTreat()
        {
            var t = Treat.Target;
            if (t == null) { DragItem = -1; return; }
            TreatInit();
            var h = t.Health;

            float w = Mathf.Min(1180f, Screen.width * 0.92f);
            float ph = Mathf.Min(780f, Screen.height * 0.92f);
            float x = (Screen.width - w) * 0.5f, y = (Screen.height - ph) * 0.5f;
            GUI.Box(new Rect(x, y, w, ph), GUIContent.none, _box);

            _hdr.fontSize = 24;
            GUI.Label(new Rect(x + 26, y + 16, w - 52, 32), t.Name.ToUpperInvariant(), _hdr);
            _hdr.fontSize = 15;

            bool clean = Wounds.IsClean(h);
            bool needsEpi = Treat.NeedsEpi(t);

            _small.fontSize = 14;
            GUI.Label(new Rect(x + 26, y + 50, w - 52, 22),
                "bleedout " + Mathf.CeilToInt(t.Remaining) + "s        pulse " + t.Hr.ToString("0") +
                "        revives left " + t.Revives +
                "        closing a wound buys +" + Cfg.TreatmentTimeBonus.Value.ToString("0") + "s", _small);
            _small.fontSize = 11;

            float bx = x + 40f, by = y + 104f;
            float bw = w * 0.42f, bh = ph - 230f;
            DrawBody(bx, by, bw, bh, h);

            float sx = bx + bw + 50f, sy = by;
            float sw = w - (sx - x) - 40f;
            _hdr.fontSize = 16;
            GUI.Label(new Rect(sx, sy - 30f, sw, 24), "YOUR SUPPLIES", _hdr);
            _hdr.fontSize = 15;
            DrawSupplies(sx, sy, sw);

            float gy = y + ph - 104f;
            var gc = GUI.color;
            GUI.color = (clean && !needsEpi) ? new Color(0.45f, 0.85f, 0.45f) : new Color(0.95f, 0.38f, 0.38f);
            _hdr.fontSize = 18;
            string gate = needsEpi
                ? "PULSE " + t.Hr.ToString("0") + " - GIVE EPINEPHRINE BEFORE REVIVING"
                : (clean ? "STABILISED - getting up" : "CLOSE THESE AND THEY GET UP - " + Wounds.OpenSummary(h));
            GUI.Label(new Rect(x + 26, gy, w - 52f, 26), gate, _hdr);
            _hdr.fontSize = 15;
            GUI.color = gc;

            if (Treat.ReviveHold > 0f)
            {
                float frac = Mathf.Clamp01(Treat.ReviveHold / Mathf.Max(0.5f, Cfg.ReviveHoldSeconds.Value));
                GUI.DrawTexture(new Rect(x + 26, gy + 30f, (w - 52f) * frac, 8f), _okTex);
            }

            if (!string.IsNullOrEmpty(Treat.LastResult))
                GUI.Label(new Rect(x + 26, gy + 44f, w - 52f, 20), Treat.LastResult, _lbl);

            GUI.Label(new Rect(x + 26, y + ph - 30f, w - 52f, 18),
                "drag a med onto a body part      close every wound and they get up      " +
                Cfg.TreatKey.Value + " or ESC to close", _small);

            DrawCarried();
        }

        static void DrawBody(float bx, float by, float bw, float bh, Il2Cpp.Health h)
        {
            _limbBoxes.Clear();
            float cx = bx + bw * 0.5f;
            float u = Mathf.Min(bw / 5f, bh / 9f);

            Add(new Rect(cx - u * 0.60f, by, u * 1.20f, u * 1.10f), 0);
            Add(new Rect(cx - u * 1.10f, by + u * 1.20f, u * 2.20f, u * 1.70f), 1);
            Add(new Rect(cx - u * 1.10f, by + u * 3.00f, u * 2.20f, u * 1.50f), 2);
            Add(new Rect(cx - u * 2.30f, by + u * 1.20f, u * 1.10f, u * 2.60f), 3);
            Add(new Rect(cx + u * 1.20f, by + u * 1.20f, u * 1.10f, u * 2.60f), 4);
            Add(new Rect(cx - u * 1.10f, by + u * 4.60f, u * 1.00f, u * 3.00f), 5);
            Add(new Rect(cx + u * 0.10f, by + u * 4.60f, u * 1.00f, u * 3.00f), 6);

            _hoverLimb = -1;
            Vector2 mouse = Event.current != null ? Event.current.mousePosition : new Vector2(-9999f, -9999f);

            for (int i = 0; i < _limbBoxes.Count; i++)
            {
                var lb = _limbBoxes[i];
                var part = Wounds.Parts[lb.Index];
                bool bleeding = Wounds.Bleed(h, part) > 0.001f;
                bool broken = Wounds.Fractured(h, part);
                bool hot = lb.R.Contains(mouse);
                if (hot) _hoverLimb = lb.Index;

                var tex = (bleeding || broken) ? _limbBad : _limbOk;
                if (hot && DragItem >= 0) tex = _limbHot;
                GUI.DrawTexture(lb.R, tex);

                var prevAlign = _small.alignment;
                var prevSize = _small.fontSize;
                var prevCol = _small.normal.textColor;
                _small.alignment = TextAnchor.MiddleCenter;
                _small.fontSize = 12;
                _small.normal.textColor = Color.white;

                string label = Wounds.Label(part);
                if (broken && bleeding) label += "\nBLEEDING + FRACTURED";
                else if (broken) label += "\nFRACTURED";
                else if (bleeding) label += "\nBLEEDING " + Wounds.Bleed(h, part).ToString("0.#");
                else label += "\n" + Wounds.HP(h, part).ToString("0") + " hp";
                GUI.Label(lb.R, label, _small);

                _small.normal.textColor = prevCol;
                _small.fontSize = prevSize;
                _small.alignment = prevAlign;
            }
        }

        static void Add(Rect r, int index)
        {
            _limbBoxes.Add(new LimbBox { R = r, Index = index });
        }

        static void DrawSupplies(float sx, float sy, float sw)
        {
            const int cols = 3;
            float cell = Mathf.Min(sw / cols, 150f);
            Vector2 mouse = Event.current != null ? Event.current.mousePosition : new Vector2(-9999f, -9999f);

            for (int i = 0; i <= Medical.AllTypes.Length; i++)
            {
                bool isEpi = i == Medical.AllTypes.Length;
                int n = isEpi ? Medical.EpiCount : Medical.CachedUses(i);
                int col = i % cols, row = i / cols;
                var r = new Rect(sx + col * cell, sy + row * (cell + 8f), cell - 10f, cell - 10f);

                GUI.DrawTexture(r, (i == ItemIndex && DragItem < 0) ? _slotSel : _slotBg);

                var icon = new Rect(r.x + 12f, r.y + 8f, r.width - 24f, r.height - 38f);
                if (!MedIcons.Draw(icon, i, isEpi))
                {
                    var prevAlign = _small.alignment;
                    _small.alignment = TextAnchor.MiddleCenter;
                    GUI.Label(icon, isEpi ? Epi.ItemName : Medical.Name(Medical.AllTypes[i]), _small);
                    _small.alignment = prevAlign;
                }

                var c = GUI.color;
                GUI.color = n <= 0 ? new Color(0.45f, 0.45f, 0.45f) : Color.white;
                var prevSize = _small.fontSize;
                _small.fontSize = 12;
                GUI.Label(new Rect(r.x + 8f, r.yMax - 24f, r.width - 16f, 20f),
                    (isEpi ? "EPI" : Medical.Name(Medical.AllTypes[i]).ToUpperInvariant()) + "   x" + n, _small);
                _small.fontSize = prevSize;
                GUI.color = c;

                if (Event.current != null && Event.current.type == EventType.MouseDown &&
                    Event.current.button == 0 && r.Contains(mouse) && n > 0 && !Treat.Busy)
                {
                    DragItem = i;
                    ItemIndex = i;
                    Event.current.Use();
                }
            }
        }

        static void DrawCarried()
        {
            if (DragItem < 0) return;
            var e = Event.current;
            if (e == null) return;
            Vector2 mouse = e.mousePosition;

            var r = new Rect(mouse.x - 36f, mouse.y - 36f, 72f, 72f);
            bool isEpi = DragItem >= Medical.AllTypes.Length;
            if (!MedIcons.Draw(r, DragItem, isEpi))
            {
                GUI.DrawTexture(r, _slotBg);
                GUI.Label(r, isEpi ? Epi.ItemName : Medical.Name(Medical.AllTypes[DragItem]), _small);
            }

            if (e.type == EventType.MouseUp && e.button == 0)
            {
                int limb = _hoverLimb;
                int item = DragItem;
                DragItem = -1;
                if (limb >= 0)
                {
                    LimbIndex = limb;
                    if (Treat.CanApply(item)) Treat.ApplyIndex(item, Wounds.Parts[limb]);
                    else Treat.LastResult = "none left";
                }
                e.Use();
            }
        }
    }
}
