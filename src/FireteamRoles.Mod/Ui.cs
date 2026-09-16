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
        static GUIStyle _lbl, _hdr, _small, _box, _row, _btn, _tiny;
        static Texture2D _panelTex, _barTex, _warnTex, _okTex, _whiteTex;
        static Texture2D _rowTex, _rowHoverTex, _rowSelTex;

        internal static bool WantsCursor { get { return PickerIsModal || Treat.Open; } }

        static bool _cursorHeld;
        static CursorLockMode _prevLock;
        static bool _prevVisible;

        internal static void CursorTick()
        {
            bool want = WantsCursor;
            if (want && !_cursorHeld)
            {
                _cursorHeld = true;
                try { _prevLock = Cursor.lockState; _prevVisible = Cursor.visible; } catch { }
            }
            else if (!want && _cursorHeld)
            {
                _cursorHeld = false;
                try { Cursor.lockState = _prevLock; Cursor.visible = _prevVisible; } catch { }
                return;
            }

            if (!want) return;
            HoldCursor();
        }

        internal static void HoldCursor()
        {
            if (!_cursorHeld || !WantsCursor) return;
            try
            {
                if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
                if (!Cursor.visible) Cursor.visible = true;
            }
            catch { }
        }

        internal static bool PickerOpen;
        internal static int LimbIndex;
        internal static int ItemIndex;

        internal static bool PickerIsModal { get { return PickerOpen && !State.InMission; } }

        internal static bool BlocksKeys { get { return State.Enabled && (PickerIsModal || Treat.Open || Aim.GiveOpen || Drone.Piloting); } }
        internal static bool BlocksMouse { get { return State.Enabled && (PickerIsModal || Treat.Open || Drone.Piloting); } }

        class Msg { internal string Text; internal float Until; }
        static readonly List<Msg> _toasts = new List<Msg>();

        internal static void Toast(string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            _toasts.Add(new Msg { Text = s, Until = Time.realtimeSinceStartup + 4.5f });
            if (_toasts.Count > 6) _toasts.RemoveAt(0);
            Log.Detail("toast: " + s);
        }

        static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.SetPixel(0, 0, c);
            t.Apply();
            t.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return t;
        }

        static bool _texReady, _styleReady;

        static void EnsureTextures()
        {
            if (_texReady) return;
            _panelTex = Solid(new Color(0.07f, 0.08f, 0.09f, 0.97f));
            _barTex = Solid(new Color(0.85f, 0.30f, 0.30f, 0.9f));
            _warnTex = Solid(new Color(0.80f, 0.45f, 0.15f, 0.9f));
            _okTex = Solid(new Color(0.35f, 0.70f, 0.40f, 0.95f));
            _rowTex = Solid(new Color(0.14f, 0.15f, 0.17f, 1f));
            _rowHoverTex = Solid(new Color(0.24f, 0.26f, 0.29f, 1f));
            _rowSelTex = Solid(new Color(0.20f, 0.30f, 0.24f, 1f));

            _whiteTex = Solid(Color.white);
            _texReady = true;
        }

        static void EnsureStyles()
        {
            if (_styleReady || !_texReady) return;

            var skin = GUI.skin;
            if (skin == null) return;

            var box = new GUIStyle(skin.box);
            box.normal.background = _panelTex;
            box.border = new RectOffset(2, 2, 2, 2);

            var row = new GUIStyle(skin.button);
            row.normal.background = _rowTex;
            row.hover.background = _rowHoverTex;
            row.active.background = _rowSelTex;
            row.onNormal.background = _rowSelTex;
            row.onHover.background = _rowHoverTex;
            row.border = new RectOffset(2, 2, 2, 2);
            row.margin = new RectOffset(0, 0, 0, 0);
            row.padding = new RectOffset(10, 10, 4, 4);
            row.alignment = TextAnchor.MiddleLeft;

            var lbl = new GUIStyle(skin.label);
            lbl.fontSize = 13;
            lbl.normal.textColor = new Color(0.88f, 0.88f, 0.86f);

            var hdr = new GUIStyle(lbl);
            hdr.fontSize = 15;
            hdr.fontStyle = FontStyle.Bold;

            var small = new GUIStyle(lbl);
            small.fontSize = 11;
            small.normal.textColor = new Color(0.65f, 0.65f, 0.63f);

            var btn = new GUIStyle(row);
            btn.alignment = TextAnchor.MiddleCenter;
            btn.padding = new RectOffset(0, 0, 0, 2);
            btn.fontSize = 14;
            btn.fontStyle = FontStyle.Bold;
            btn.normal.textColor = new Color(0.72f, 0.72f, 0.70f);
            btn.hover.textColor = Color.white;

            var tiny = new GUIStyle(lbl);
            tiny.fontSize = 10;
            tiny.fontStyle = FontStyle.Bold;
            tiny.normal.textColor = new Color(0.62f, 0.63f, 0.62f);

            _box = box; _row = row; _lbl = lbl; _hdr = hdr; _small = small; _btn = btn; _tiny = tiny;
            _styleReady = true;
        }

        static bool Ready
        {
            get { return _texReady && _styleReady && _lbl != null && _hdr != null && _small != null && _box != null && _btn != null && _tiny != null; }
        }

        internal static void Prewarm()
        {
            EnsureTextures();
            TreatInit();
        }

        internal static void Draw()
        {
            HoldCursor();
            EnsureTextures();
            EnsureStyles();
            if (!Ready) return;
            if (!State.Enabled) return;

            DrawToasts();

            if (PickerOpen || Cfg.BarAlwaysVisibleInRaid.Value || !State.InMission)
            {
                if (Bar.Minimized && !PickerOpen) DrawBarTab();
                else DrawBar();
            }

            if (PickerOpen && !Menu.Active) DrawPicker();
            Radar.Draw();
            Drone.DrawHud();
            DrawPrompt();
            if (Treat.Open) DrawTreat();
            if (Aim.GiveOpen) DrawGive();
            DrawSquadMarkers();
            DrawDownedBanner();
        }

        internal static float ManDownUntil;

        static void DrawSquadMarkers()
        {
            var self = State.Me;
            bool meDown = self != null && self.Downed;
            var cam = Camera.main;

            foreach (var kv in State.Peers)
            {
                var p = kv.Value;
                if (p == null || !p.Downed || p.Id == Net.Self) continue;
                if (meDown || cam == null || !p.Live) continue;

                Vector3 world = p.Position + Vector3.up * 0.6f;
                Vector3 sp = cam.WorldToScreenPoint(world);
                if (sp.z <= 0f) continue;
                float dist = Vector3.Distance(cam.transform.position, world);
                if (dist < 6f) continue;

                float x = sp.x, y = Screen.height - sp.y;
                var c = GUI.color;
                float pulse = 0.75f + 0.25f * Mathf.Sin(Time.realtimeSinceStartup * 5f);
                GUI.color = new Color(0.92f, 0.25f, 0.25f, pulse);
                var prev = _lbl.alignment;
                _lbl.alignment = TextAnchor.MiddleCenter;
                GUI.Label(new Rect(x - 60, y - 26, 120, 16), "+", _hdr);
                GUI.Label(new Rect(x - 80, y - 8, 160, 16),
                    p.Name + "  " + dist.ToString("0") + "m  " + Mathf.CeilToInt(p.Remaining) + "s", _lbl);
                _lbl.alignment = prev;
                GUI.color = c;
            }

            if (Time.realtimeSinceStartup < ManDownUntil && !meDown)
            {
                var prev = _hdr.alignment;
                _hdr.alignment = TextAnchor.MiddleCenter;
                var c = GUI.color;
                float a = Mathf.Clamp01(ManDownUntil - Time.realtimeSinceStartup);
                GUI.color = new Color(0.92f, 0.25f, 0.25f, a);
                GUI.Label(new Rect(0, Screen.height * 0.12f, Screen.width, 30), "MAN DOWN", _hdr);
                GUI.color = c;
                _hdr.alignment = prev;
            }
        }

        const float RowH = 20f;
        const float HeadH = 22f;
        const float BtnW = 22f;

        static int _measuredGen = -1;
        static float _barW = 272f, _nameW = 150f, _roleW = 92f;

        static void MeasureBar(List<Bar.Row> rows)
        {
            if (_measuredGen == Bar.Generation) return;
            _measuredGen = Bar.Generation;

            float minW = Mathf.Clamp(Cfg.BarWidth.Value, 190f, 520f);

            float maxW = Mathf.Max(minW, Mathf.Max(Cfg.BarMaxWidth.Value, Screen.width * 0.30f));

            float widestName = 0f, widestRole = 0f;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                r.Display = r.Name;
                widestName = Mathf.Max(widestName, Width(_lbl, r.Name));
                widestRole = Mathf.Max(widestRole, Width(_small, r.RightText));
            }

            float headNeed = Width(_tiny, "F I R E T E A M") + 20f + BtnW * 2f +
                             Mathf.Max(Width(_small, Bar.TallyText), Width(_small, Bar.DownedText));

            _roleW = Mathf.Clamp(widestRole + 6f, 46f, 150f);
            float need = 13f + widestName + 14f + _roleW + 12f;
            _barW = Mathf.Clamp(Mathf.Max(need, headNeed + 20f), minW, maxW);
            _nameW = _barW - _roleW - 27f;

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                if (Width(_lbl, r.Display) <= _nameW) continue;
                string s = r.Display;
                while (s.Length > 1 && Width(_lbl, s + "...") > _nameW) s = s.Substring(0, s.Length - 1);
                r.Display = s + "...";
            }
        }

        static bool CanClick
        {
            get
            {
                try { return Cursor.lockState == CursorLockMode.None && Cursor.visible; }
                catch { return false; }
            }
        }

        static bool Btn(Rect r, string label, GUIStyle st)
        {
            if (!CanClick) { GUI.Label(r, label, st); return false; }
            return GUI.Button(r, label, st);
        }

        static float Width(GUIStyle st, string s)
        {
            if (st == null || string.IsNullOrEmpty(s)) return 0f;
            try { return st.CalcSize(new GUIContent(s)).x; }
            catch { return s.Length * 7f; }
        }

        static void DrawBarTab()
        {
            float w = Mathf.Max(160f, Width(_hdr, Bar.TabText) + Width(_small, Bar.TallyText) + 40f);
            float h = 20f;
            float x = Mathf.Clamp(Cfg.BarX.Value, 0f, Mathf.Max(0f, Screen.width - w));
            float y = Screen.height - h - Mathf.Max(0f, Cfg.BarTabBottomMargin.Value);

            if (CanClick && GUI.Button(new Rect(x, y, w, h), GUIContent.none, GUIStyle.none))
                Bar.SetMinimized(false);

            bool down = Bar.DownedCount > 0;
            Rule(x, y + 2f, h - 4f, down ? Red : Dim);
            Shadow(new Rect(x + 8f, y, w - 16f, h), "^ " + Bar.TabText, _lbl,
                   down ? Red : new Color(0.86f, 0.86f, 0.84f));

            var prev = _small.alignment;
            _small.alignment = TextAnchor.MiddleRight;
            Shadow(new Rect(x + w - 196f, y + 1f, 186f, h - 2f),
                   down ? Bar.DownedText : Bar.TallyText, _small,
                   down ? Red : new Color(0.62f, 0.62f, 0.60f));
            _small.alignment = prev;
        }

        static readonly Color Dim = new Color(1f, 1f, 1f, 0.16f);
        static readonly Color Red = new Color(0.95f, 0.33f, 0.31f);
        const string TitleSpaced = "F I R E T E A M";

        static void Shadow(Rect r, string text, GUIStyle st, Color col)
        {
            if (string.IsNullOrEmpty(text)) return;
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.75f);
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, st);
            GUI.color = col;
            GUI.Label(r, text, st);
            GUI.color = prev;
        }

        static void Rule(float x, float y, float h, Color col)
        {
            if (_whiteTex == null) return;
            var prev = GUI.color;
            GUI.color = col;
            GUI.DrawTexture(new Rect(x, y, 1f, h), _whiteTex);
            GUI.color = prev;
        }

        static void DrawBar()
        {
            var rows = Bar.Rows();
            MeasureBar(rows);

            float x = Cfg.BarX.Value, y = Cfg.BarY.Value;
            float w = _barW;
            bool collapsed = Bar.Collapsed;
            int downed = Bar.DownedCount;

            float h = collapsed ? HeadH : HeadH + Mathf.Max(1, rows.Count) * RowH;

            if (Cfg.BarBackdrop.Value && _panelTex != null)
            {
                var pc = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.55f);
                GUI.DrawTexture(new Rect(x - 6f, y - 2f, w + 12f, h + 6f), _panelTex);
                GUI.color = pc;
            }

            Rule(x, y + 3f, h - 6f, downed > 0 ? Red : Dim);

            DrawBarHeader(x, y, w, rows.Count, downed, collapsed);
            if (collapsed) return;

            float ry = y + HeadH;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];

                if (r.Self) Shadow(new Rect(x + 8f, ry, 10f, 18f), ">", _small, new Color(0.55f, 0.56f, 0.58f));

                Color nameCol = r.Downed ? Red
                              : r.Self ? new Color(0.98f, 0.98f, 0.96f)
                                       : new Color(0.78f, 0.78f, 0.76f);
                Shadow(new Rect(x + 18f, ry, _nameW, 18f), r.Display, _lbl, nameCol);

                var prev = _small.alignment;
                _small.alignment = TextAnchor.MiddleRight;
                Shadow(new Rect(x + w - _roleW - 4f, ry + 1f, _roleW, 17f), r.RightText, _small,
                       r.Downed ? Red : r.Tint);
                _small.alignment = prev;

                ry += RowH;
            }
        }

        static void DrawBarHeader(float x, float y, float w, int count, int downed, bool collapsed)
        {
            float right = x + w;

            if (CanClick)
            {
                float bx = right - BtnW;
                if (Btn(new Rect(bx, y + 1f, BtnW, 17f), "_", _btn)) Bar.SetMinimized(true);
                bx -= BtnW + 2f;
                if (Btn(new Rect(bx, y + 1f, BtnW, 17f), collapsed ? "+" : "-", _btn)) Bar.Toggle();
                right = bx - 8f;
            }
            else
            {
                var prevA = _small.alignment;
                _small.alignment = TextAnchor.MiddleRight;
                Shadow(new Rect(right - 70f, y + 1f, 70f, 16f), Bar.HintText, _small,
                       new Color(0.52f, 0.53f, 0.54f));
                _small.alignment = prevA;
                right -= 78f;
            }

            Shadow(new Rect(x + 8f, y, 200f, 16f), TitleSpaced, _tiny, new Color(0.62f, 0.63f, 0.62f));

            string info = downed > 0 ? Bar.DownedText : Bar.TallyText;
            if (!string.IsNullOrEmpty(info))
            {

                float titleEnd = x + 12f + Width(_tiny, TitleSpaced);
                float left = Mathf.Max(titleEnd, right - 190f);
                if (right - left > 24f)
                {
                    var prev = _small.alignment;
                    _small.alignment = TextAnchor.MiddleRight;
                    Shadow(new Rect(left, y + 1f, right - left, 16f), info, _small,
                           downed > 0 ? Red : new Color(0.60f, 0.61f, 0.59f));
                    _small.alignment = prev;
                }
            }
        }

        static readonly Role[] PickOrder =
        {
            Role.Assaulter, Role.Breacher, Role.Sniper, Role.Medic, Role.Jtac, Role.None
        };

        static void DrawPicker()
        {
            const float pad = 12f, headerH = 26f, rowH = 42f, gap = 4f, footerH = 20f;

            float need = 0f;
            for (int i = 0; i < PickOrder.Length; i++)
                need = Mathf.Max(need, Width(_small, RoleInfo.Perk(PickOrder[i])));
            need = Mathf.Max(need, Width(_small, "press the number (weapon keys still live in a raid)"));
            float w = Mathf.Clamp(need + 74f, 340f, Mathf.Max(360f, Screen.width * 0.42f));
            float h = pad + headerH + PickOrder.Length * (rowH + gap) + footerH + pad;

            float barH = Bar.Collapsed ? HeadH : HeadH + Mathf.Max(1, Bar.TotalCount) * RowH;
            float x = Cfg.BarX.Value, y = Cfg.BarY.Value + barH + 8f;

            GUI.Box(new Rect(x, y, w, h), GUIContent.none, _box);
            GUI.Label(new Rect(x + pad, y + pad - 4f, w, 22f),
                State.RolesLocked ? "PICK A ROLE (locked)" : "PICK A ROLE", _hdr);

            float ry = y + pad + headerH;
            for (int i = 0; i < PickOrder.Length; i++)
            {
                var r = PickOrder[i];
                bool cur = State.MyRole == r;
                int num = r == Role.None ? 0 : i + 1;
                var rect = new Rect(x + pad, ry, w - pad * 2f, rowH);

                if (GUI.Button(rect, GUIContent.none, cur ? _rowSel() : _row) && !State.RolesLocked)
                    Aim.PickRole(r);

                _lbl.normal.textColor = new Color(0.55f, 0.56f, 0.58f);
                GUI.Label(new Rect(rect.x + 10f, rect.y + 4f, 26f, 20f), num.ToString(), _lbl);

                _lbl.normal.textColor = cur ? RoleInfo.Colour(r) : new Color(0.93f, 0.93f, 0.91f);
                GUI.Label(new Rect(rect.x + 32f, rect.y + 4f, 200f, 20f), RoleInfo.Name(r), _lbl);

                string perk = RoleInfo.Perk(r);
                if (!string.IsNullOrEmpty(perk))
                {
                    _small.normal.textColor = cur ? new Color(0.80f, 0.81f, 0.78f)
                                                  : new Color(0.60f, 0.61f, 0.58f);
                    GUI.Label(new Rect(rect.x + 32f, rect.y + 21f, rect.width - 42f, 18f), perk, _small);
                }

                ry += rowH + gap;
            }

            _lbl.normal.textColor = new Color(0.88f, 0.88f, 0.86f);
            _small.normal.textColor = new Color(0.65f, 0.65f, 0.63f);
            GUI.Label(new Rect(x + pad, y + h - footerH - 4f, w - pad * 2f, 18f),
                PickerIsModal ? "click, or press the number" : "press the number (weapon keys still live in a raid)", _small);
        }

        static GUIStyle _rowSelStyle;

        static GUIStyle _rowSel()
        {
            if (_rowSelStyle == null)
            {
                _rowSelStyle = new GUIStyle(_row);
                _rowSelStyle.normal.background = _rowSelTex;
                _rowSelStyle.hover.background = _rowHoverTex;
            }
            return _rowSelStyle;
        }

        static void DrawPrompt()
        {
            string s = Aim.PromptText();
            if (string.IsNullOrEmpty(s)) return;
            float w = 460f;
            var r = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.62f, w, 24f);
            var prev = _lbl.alignment;
            _lbl.alignment = TextAnchor.MiddleCenter;
            GUI.Label(r, s, _lbl);
            _lbl.alignment = prev;
        }

        static void DrawToasts()
        {
            float now = Time.realtimeSinceStartup;
            for (int i = _toasts.Count - 1; i >= 0; i--)
                if (_toasts[i].Until < now) _toasts.RemoveAt(i);
            if (_toasts.Count == 0) return;

            float w = 380f, x = (Screen.width - w) * 0.5f, y = Screen.height * 0.70f;
            for (int i = 0; i < _toasts.Count; i++)
            {
                var prev = _lbl.alignment;
                _lbl.alignment = TextAnchor.MiddleCenter;
                GUI.Label(new Rect(x, y + i * 18f, w, 18f), _toasts[i].Text, _lbl);
                _lbl.alignment = prev;
            }
        }

        internal static float DramaStart = -999f;

        static GUIStyle _drama;

        static void DrawDownedBanner()
        {
            var me = State.Me;
            if (me == null || !me.Downed) return;

            float t = Time.realtimeSinceStartup - DramaStart;
            if (t >= 0f && t < 3.2f)
            {
                float sw = Screen.width, sh = Screen.height;

                float barMax = sh * 0.14f;
                float barIn = Mathf.Clamp01(t / 0.25f);
                float barOut = t > 2.4f ? 1f - Mathf.Clamp01((t - 2.4f) / 0.8f) : 1f;
                float bar = barMax * barIn * barOut;
                GUI.DrawTexture(new Rect(0, 0, sw, bar), _panelTex);
                GUI.DrawTexture(new Rect(0, sh - bar, sw, bar), _panelTex);

                float flash = Mathf.Clamp01(1f - t / 0.9f);
                if (flash > 0f)
                {
                    var fc = GUI.color;
                    GUI.color = new Color(0.55f, 0.05f, 0.05f, flash * 0.55f);
                    GUI.DrawTexture(new Rect(0, 0, sw, sh), _barTex);
                    GUI.color = fc;
                }

                if (t > 0.35f)
                {
                    if (_drama == null)
                    {
                        _drama = new GUIStyle(_hdr);
                        _drama.fontSize = 64;
                        _drama.alignment = TextAnchor.MiddleCenter;
                    }
                    float a = Mathf.Clamp01((t - 0.35f) / 0.25f) * (t > 2.6f ? 1f - Mathf.Clamp01((t - 2.6f) / 0.6f) : 1f);
                    var wc = GUI.color;
                    GUI.color = new Color(0.92f, 0.15f, 0.15f, a);
                    GUI.Label(new Rect(0, sh * 0.34f, sw, 90f), "DOWN", _drama);
                    GUI.color = wc;
                }

                if (t < 2.4f) return;
            }

            float w = 420f, x = (Screen.width - w) * 0.5f, y = Screen.height * 0.18f;
            GUI.Box(new Rect(x, y, w, 74f), GUIContent.none, _box);

            var prev = _hdr.alignment;
            _hdr.alignment = TextAnchor.MiddleCenter;
            var c = GUI.color;
            GUI.color = new Color(0.92f, 0.28f, 0.28f);
            GUI.Label(new Rect(x, y + 6, w, 24), "DOWN", _hdr);
            GUI.color = c;
            _hdr.alignment = prev;

            var ps = _lbl.alignment;
            _lbl.alignment = TextAnchor.MiddleCenter;
            GUI.Label(new Rect(x, y + 30, w, 20), Mathf.CeilToInt(me.Remaining) + " seconds", _lbl);
            GUI.Label(new Rect(x, y + 50, w, 20),
                me.Dragger != 0UL ? State.NameOf(me.Dragger) + " has you" : "a teammate has to reach you", _small);
            _lbl.alignment = ps;
        }

        static void DrawGive()
        {
            var t = Aim.GiveTarget;
            if (t == null) return;

            float w = 300f, h = 40f + (Medical.AllTypes.Length + 1) * 20f;
            float x = (Screen.width - w) * 0.5f, y = Screen.height * 0.30f;
            GUI.Box(new Rect(x, y, w, h), GUIContent.none, _box);
            GUI.Label(new Rect(x + 10, y + 6, w, 20), "GIVE TO " + t.Name.ToUpperInvariant(), _hdr);

            for (int i = 0; i < Medical.AllTypes.Length; i++)
            {
                var type = Medical.AllTypes[i];
                int n = Medical.CachedUses(i);
                var c = GUI.color;
                GUI.color = i == ItemIndex ? Color.white : new Color(0.6f, 0.6f, 0.6f);
                GUI.Label(new Rect(x + 14, y + 30 + i * 20f, w - 28f, 18f),
                    (i == ItemIndex ? "> " : "  ") + Medical.Name(type) + "   x" + n, _lbl);
                GUI.color = c;
            }

            int ei = Medical.AllTypes.Length;
            var ec = GUI.color;
            GUI.color = ItemIndex == ei ? Color.white : new Color(0.6f, 0.6f, 0.6f);
            GUI.Label(new Rect(x + 14, y + 30 + ei * 20f, w - 28f, 18f),
                (ItemIndex == ei ? "> " : "  ") + Epi.ItemName + "   x" + Medical.EpiCount, _lbl);
            GUI.color = ec;

            GUI.Label(new Rect(x + 14, y + h - 20f, w - 28f, 18f), "wheel to pick, release to hand over", _small);
        }

    }
}
