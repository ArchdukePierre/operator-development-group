// ODG Operations - FireteamRoles
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Il2CppTMPro;

namespace FireteamRoles
{

    internal static class Menu
    {
        class RowUi
        {
            internal Role Role;
            internal RectTransform Rect;
            internal Image Bg, Chip;
            internal TextMeshProUGUI Num, Name, Count;
        }

        static GameObject _root;
        static RectTransform _panel;
        static TextMeshProUGUI _title, _status, _perk, _hint;
        static readonly List<RowUi> _rows = new List<RowUi>();
        static bool _buildFailed, _shown;
        static float _nextRefresh;
        static string _builtIn = "";
        static int _hover = -1;

        static Sprite _white;
        static TMP_FontAsset _fontHead, _fontBody, _fontAny;
        static Material _fontMatHead, _fontMatBody;

        static readonly Color Body = new Color(0.050f, 0.055f, 0.060f, 0.92f);
        static readonly Color Edge = new Color(0.32f, 0.34f, 0.35f, 0.40f);
        static readonly Color RowHover = new Color(1f, 1f, 1f, 0.07f);
        static readonly Color Ink = new Color(0.90f, 0.90f, 0.88f, 1f);
        static readonly Color Dim = new Color(0.62f, 0.64f, 0.64f, 1f);
        static readonly Color Faint = new Color(0.44f, 0.46f, 0.47f, 1f);
        static readonly Color Warn = new Color(0.88f, 0.48f, 0.36f, 1f);

        const float W = 236f, Pad = 10f, RowH = 22f, Margin = 16f;

        static readonly Role[] Order = { Role.Assaulter, Role.Breacher, Role.Sniper, Role.Medic, Role.Jtac, Role.None };

        internal static bool Active { get { return Cfg.PickerNative.Value && _root != null && !_buildFailed; } }

        internal static void Tick()
        {
            if (!Cfg.PickerNative.Value) { Hide(); return; }

            string scene = "";
            try { scene = SceneManager.GetActiveScene().name; } catch { }
            if (_root != null && scene != _builtIn) Teardown();

            bool want = Ui.PickerOpen && State.Enabled;
            if (want && _root == null && !_buildFailed) Build(scene);
            if (_root == null) return;

            if (want != _shown)
            {
                _shown = want;
                try { _root.SetActive(want); } catch { }
                if (want) { _hover = -1; _nextRefresh = 0f; Refresh(); }
            }
            if (!want) return;

            if (Time.realtimeSinceStartup >= _nextRefresh) { _nextRefresh = Time.realtimeSinceStartup + 0.25f; Refresh(); }
            Pointer();
        }

        internal static void Hide()
        {
            if (_root != null && _shown) { try { _root.SetActive(false); } catch { } }
            _shown = false;
        }

        static void Teardown()
        {
            try { if (_root != null) UnityEngine.Object.Destroy(_root); } catch { }
            _root = null; _rows.Clear(); _shown = false; _buildFailed = false;
        }

        static void Discover()
        {
            _white = MakeWhite();

            try
            {
                var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                if (fonts != null)
                    for (int i = 0; i < fonts.Length; i++)
                    {
                        var f = fonts[i]; if (f == null) continue;
                        string n = f.name;
                        if (n.StartsWith("Rajdhani-Medium") || n.StartsWith("Rajdhani-Bold")) _fontHead = _fontHead ?? f;
                        else if (n.StartsWith("Rajdhani-Regular")) { _fontBody = _fontBody ?? f; _fontHead = _fontHead ?? f; }
                        else if (n.StartsWith("Rajdhani-Light")) _fontBody = _fontBody ?? f;
                    }
            }
            catch (Exception e) { Log.Detail("font discovery failed: " + e.Message); }

            try
            {
                var labels = Resources.FindObjectsOfTypeAll<TextMeshProUGUI>();
                var count = new Dictionary<int, int>(); int bestN = 0;
                if (labels != null)
                    for (int i = 0; i < labels.Length; i++)
                    {
                        var t = labels[i]; if (t == null) continue;
                        TMP_FontAsset f = null; try { f = t.font; } catch { }
                        if (f == null) continue;
                        int n; count.TryGetValue(f.GetInstanceID(), out n); n++; count[f.GetInstanceID()] = n;
                        if (n > bestN) { bestN = n; _fontAny = f; }
                        if (_fontHead != null && f.GetInstanceID() == _fontHead.GetInstanceID() && _fontMatHead == null)
                            try { _fontMatHead = t.fontSharedMaterial; } catch { }
                        if (_fontBody != null && f.GetInstanceID() == _fontBody.GetInstanceID() && _fontMatBody == null)
                            try { _fontMatBody = t.fontSharedMaterial; } catch { }
                    }
            }
            catch { }
            if (_fontHead == null) _fontHead = _fontAny;
            if (_fontBody == null) _fontBody = _fontHead;

            Log.Msg("fireteam menu: head font " + (_fontHead != null ? _fontHead.name : "none") +
                    ", body font " + (_fontBody != null ? _fontBody.name : "none"));
        }

        static void Build(string scene)
        {
            try
            {
                Discover();
                _builtIn = scene;

                _root = new GameObject("odg_fireteam_menu");
                UnityEngine.Object.DontDestroyOnLoad(_root);
                var canvas = _root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 5000;
                var scaler = _root.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 1f;

                float H = Pad + 16f + 6f + Order.Length * RowH + 6f + 14f + 12f + Pad;

                var edge = Img(_root.transform, "edge", _white, Edge);
                Anchor(edge.rectTransform, 0f, 1f, Margin, -Margin, W + 2f, H + 2f, 0f, 1f);
                var body = Img(_root.transform, "body", _white, Body);
                Anchor(body.rectTransform, 0f, 1f, Margin + 1f, -(Margin + 1f), W, H, 0f, 1f);
                _panel = body.rectTransform;

                _title = Txt(_panel, "title", "FIRETEAM", 12.5f, Ink, TextAlignmentOptions.Left, true, true);
                Anchor(_title.rectTransform, 0f, 1f, Pad, -Pad + 1f, 120f, 16f, 0f, 1f);
                _title.characterSpacing = 4f;

                _status = Txt(_panel, "status", "", 9.5f, Faint, TextAlignmentOptions.Right, false, false);
                Anchor(_status.rectTransform, 1f, 1f, -Pad, -Pad, 130f, 16f, 1f, 1f);
                _status.characterSpacing = 1f;

                float y = -(Pad + 16f + 6f);
                for (int i = 0; i < Order.Length; i++)
                {
                    var r = new RowUi { Role = Order[i] };
                    var bg = Img(_panel, "row" + i, _white, Color.clear);
                    Anchor(bg.rectTransform, 0f, 1f, 4f, y, W - 8f, RowH, 0f, 1f);
                    r.Rect = bg.rectTransform; r.Bg = bg;

                    var chip = Img(r.Rect, "chip", _white, RoleInfo.Colour(r.Role));
                    Anchor(chip.rectTransform, 0f, 0.5f, 0f, 0f, 2f, RowH - 6f, 0f, 0.5f);
                    r.Chip = chip;

                    r.Num = Txt(r.Rect, "num", NumFor(r.Role), 10.5f, Faint, TextAlignmentOptions.Center, true, true);
                    Anchor(r.Num.rectTransform, 0f, 0.5f, 8f, 0f, 16f, RowH, 0f, 0.5f);

                    r.Name = Txt(r.Rect, "name", RoleInfo.Name(r.Role), 12f, Ink, TextAlignmentOptions.Left, true, true);
                    Anchor(r.Name.rectTransform, 0f, 0.5f, 28f, 0f, 140f, RowH, 0f, 0.5f);
                    r.Name.characterSpacing = 1f;

                    r.Count = Txt(r.Rect, "count", "", 9.5f, Faint, TextAlignmentOptions.Right, false, false);
                    Anchor(r.Count.rectTransform, 1f, 0.5f, -8f, 0f, 60f, RowH, 1f, 0.5f);

                    _rows.Add(r);
                    y -= RowH;
                }

                _perk = Txt(_panel, "perk", "", 10f, Dim, TextAlignmentOptions.Left, false, false);
                Anchor(_perk.rectTransform, 0f, 1f, Pad, y - 4f, W - 2f * Pad, 14f, 0f, 1f);

                _hint = Txt(_panel, "hint", "", 9f, Faint, TextAlignmentOptions.Left, false, false);
                Anchor(_hint.rectTransform, 0f, 0f, Pad, Pad - 2f, W - 2f * Pad, 12f, 0f, 0f);

                _root.SetActive(false);
                _shown = false;
                Log.Fired("menu-native");
            }
            catch (Exception e)
            {
                _buildFailed = true;
                Err.Note("menu.build", e);
                try { if (_root != null) UnityEngine.Object.Destroy(_root); } catch { }
                _root = null;
                Log.Warn("native menu could not be built, falling back to the drawn panel");
            }
        }

        static Sprite MakeWhite()
        {
            if (_white != null) return _white;
            try
            {
                var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                var px = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Color32>(16);
                for (int i = 0; i < 16; i++) px[i] = new Color32(255, 255, 255, 255);
                tex.SetPixels32(px); tex.Apply(false, false);
                return Sprite.Create(tex, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 100f);
            }
            catch { return null; }
        }

        static Image Img(Transform parent, string name, Sprite sprite, Color col)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            if (sprite != null) img.sprite = sprite;
            img.type = Image.Type.Simple;
            img.color = col;
            img.raycastTarget = false;
            return img;
        }

        static TextMeshProUGUI Txt(Transform parent, string name, string text, float size, Color col,
                                   TextAlignmentOptions align, bool head, bool bold)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            var f = head ? _fontHead : _fontBody;
            var m = head ? _fontMatHead : _fontMatBody;
            if (f != null) { try { t.font = f; } catch { } }
            if (m != null) { try { t.fontSharedMaterial = m; } catch { } }
            t.text = text; t.fontSize = size; t.color = col; t.alignment = align;
            t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            t.raycastTarget = false; t.enableWordWrapping = false; t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        static void Anchor(RectTransform rt, float ax, float ay, float x, float y, float w, float h, float px = 0.5f, float py = 0.5f)
        {
            rt.anchorMin = new Vector2(ax, ay); rt.anchorMax = new Vector2(ax, ay);
            rt.pivot = new Vector2(px, py);
            rt.anchoredPosition = new Vector2(x, y); rt.sizeDelta = new Vector2(w, h);
        }

        static void Refresh()
        {
            try
            {
                bool locked = State.RolesLocked;
                _status.text = locked ? "LOCKED" : (State.InMission ? "IN OPERATION" : "READY ROOM");
                _status.color = locked ? Warn : Faint;

                var counts = new int[RoleInfo.Count];
                try { foreach (var kv in State.Peers) { var p = kv.Value; if (p != null && p.HasMod && p.Id != Net.Self) counts[(int)p.Role]++; } } catch { }

                for (int i = 0; i < _rows.Count; i++)
                {
                    var r = _rows[i];
                    bool cur = State.MyRole == r.Role;
                    var colour = RoleInfo.Colour(r.Role);
                    r.Name.color = cur ? colour : (locked ? Dim : Ink);
                    var chip = colour; chip.a = cur ? 1f : 0.35f; r.Chip.color = chip;
                    var sel = colour; sel.a = 0.16f;
                    r.Bg.color = cur ? sel : (i == _hover ? RowHover : Color.clear);
                    r.Count.text = counts[(int)r.Role] > 0 && r.Role != Role.None ? "x" + counts[(int)r.Role] : "";
                }

                Role show = _hover >= 0 && _hover < _rows.Count ? _rows[_hover].Role : State.MyRole;
                _perk.text = RoleInfo.Perk(show);

                _hint.text = Ui.PickerIsModal ? "click or press the number      esc closes"
                                              : "press the number      esc closes";
            }
            catch (Exception e) { Log.Detail("menu refresh failed: " + e.Message); }
        }

        static void Pointer()
        {
            bool canClick = false;
            try { canClick = Cursor.lockState == CursorLockMode.None && Cursor.visible; } catch { }
            if (!canClick) { if (_hover != -1) { _hover = -1; Refresh(); } return; }

            Vector2 mp = Input.mousePosition;
            bool down = Input.GetMouseButtonDown(0);
            int hover = -1;
            for (int i = 0; i < _rows.Count; i++)
            {
                bool over = false;
                try { over = RectTransformUtility.RectangleContainsScreenPoint(_rows[i].Rect, mp, null); } catch { }
                if (over) { hover = i; if (down && !State.RolesLocked) Aim.PickRole(_rows[i].Role); }
            }
            if (hover != _hover) { _hover = hover; Refresh(); }
        }

        static string NumFor(Role r)
        {
            switch (r)
            {
                case Role.Assaulter: return "1";
                case Role.Breacher: return "2";
                case Role.Sniper: return "3";
                case Role.Medic: return "4";
                case Role.Jtac: return "5";
                default: return "0";
            }
        }
    }
}
