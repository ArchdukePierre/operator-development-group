// ODG Operations - Quartermaster
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Il2CppTMPro;
using OdgUi;

namespace Quartermaster
{

    internal static class Bench
    {
        const float PAD = Layout.BAND_PAD, HEAD_H = 26f, TILE_H = 72f, GAP = Layout.TILE_GAP;
        const float STRIP_W = 270f, CARD_W = 480f, TIP_W = 320f;
        const int N = 12;
        static readonly string[] LABELS =
        {
            "DAMAGE", "PENETRATION", "VELOCITY", "RATE OF FIRE", "ACCURACY", "RELIABILITY",
            "VERTICAL RECOIL", "HORIZONTAL RECOIL", "ERGONOMICS", "WEIGHT", "MOBILITY", "SOUND"
        };
        static readonly string[] TIPS =
        {
            "Hit points taken from a target without armour, per bullet. Shotguns show pellets times damage per pellet. It comes from the round in the magazine, not from the gun or its parts.",
            "The plate level the round goes through every time. Against a higher plate it only gets through once the plate is worn down. The round's card lists every plate.",
            "Muzzle speed in metres per second. Faster flies flatter and arrives sooner. A short barrel loses some, a long barrel gains some. Under 340 m/s the round is subsonic: no crack, only the shot.",
            "Rounds per minute on full auto. It belongs to the gun; barrels and parts do not change it.",
            "The gun's own spread in minutes of angle. Lower is tighter: one MOA is about 3 cm at 100 m. Long barrels and precision chassis tighten it, short barrels and braces open it, match rounds tighten it while loaded.",
            "Misfires per thousand rounds, from the shell in the magazine. A misfire is a dead trigger pull and a clearance. Shotgun shells and .45 misfire the most, match rounds the least.",
            "How far the muzzle climbs per shot, against the bare gun. Brakes, stocks, grips, cans and long barrels bring it down. Braces, bare pistol grips and short barrels push it up.",
            "How far the muzzle wanders sideways per shot, against the bare gun. Compensators and grips help the most, stocks and cans a little.",
            "How fast the gun comes up to the sight and moves between ready and aimed, on a scale of 20 to 100. Light handguards, grips, red dots and short barrels add to it. Scopes, cans, big magazines and long barrels take from it.",
            "The gun as built with the magazine in it, in kilograms. It counts toward the kit mass that sets your speed. Weight changes nothing else: no sway, no stamina.",
            "Your run speed, from the mass of everything you carry: guns, magazines and their rounds, plates, clothing, grenades. Full speed up to 15 kg, down to 88 percent at 30 kg and never lower. Sprinting always works.",
            "Whether a can is on the gun and how far the shot carries. A can changes who hears you and nothing else: no damage, no range.",
        };
        const int T_DMG = 0, T_PEN = 1, T_VEL = 2, T_RPM = 3, T_ACC = 4, T_REL = 5, T_VR = 6, T_HR = 7, T_ERGO = 8, T_WT = 9, T_MOB = 10, T_SND = 11;
        static readonly string[] PLATE_ORDER = { "III", "III+", "IV", "V" };

        internal static bool Dirty = true;

        sealed class Snap
        {
            public readonly string[] Text = new string[N];
            public readonly float[] Frac = new float[N];
        }

        static Il2Cpp.WeaponModding _wm;
        static bool _open, _built, _giveUp;
        static int _fails;
        static float _buildAt, _refreshAt, _musAt;

        static GameObject _ownCanvasGo, _layerGo;
        static Canvas _canvas;
        static bool _ownCanvas;
        static Chrome _c;
        static float _designW = 1100f, _cardH = 206f;
        static int _cols = 6, _rows = 2;
        static TextMeshProUGUI _title, _sub, _kit, _foot;
        static Tile[] _tiles;
        static Snap _cur;
        static string _projKey = "";

        static RectTransform _strip, _stripEdge;
        static TextMeshProUGUI _stripTitle, _stripKind;
        static readonly Chip[] _chips = new Chip[8];

        static RectTransform _tip, _tipEdge;
        static TextMeshProUGUI _tipTitle, _tipBody;
        static int _tipIdx = -1, _tipPinned = -1;

        static RectTransform _card, _cardEdge;
        static TextMeshProUGUI _cardTitle, _cardKind, _cardSummary, _cardPlates, _cardBody, _cardBest, _cardHint;
        static readonly StatLine[] _lines = new StatLine[6];
        static bool _pinned, _cardFull;
        static RoundDef _pinnedRound, _cardRound;

        static string _hoverKey = "";
        static string _screenKey = "";
        static float _remeasureAt;
        static int _measureTries;
        const int LAYOUT_VERSION = 3;
        static float _flashUntil;
        static RectTransform _flashAnchor;

        static readonly List<Il2Cpp.ModUI> _mus = new List<Il2Cpp.ModUI>();

        static readonly List<TMP_Dropdown> _dds = new List<TMP_Dropdown>();
        static readonly List<bool> _wasOpen = new List<bool>();
        struct Box { public float X0, Y0, X1, Y1; public bool Has(Vector2 p) { return p.x >= X0 && p.x <= X1 && p.y >= Y0 && p.y <= Y1; } }
        sealed class ListCache
        {
            public RectTransform ListRt, ContentRt;
            public Camera Cam;
            public Box Content0;
            public float ReadyAt;
            public bool Filled;
            public readonly List<int> Idx = new List<int>();
            public readonly List<Box> Boxes = new List<Box>();
        }
        static readonly Dictionary<IntPtr, ListCache> _lists = new Dictionary<IntPtr, ListCache>();
        static readonly Dictionary<IntPtr, string> _shadow = new Dictionary<IntPtr, string>();
        static readonly List<TextMeshProUGUI> _magTexts = new List<TextMeshProUGUI>();
        static readonly List<RectTransform> _magRts = new List<RectTransform>();
        static RectTransform _magPanel;
        static Camera _magCam;
        static float _magRowsAt, _rowsAt, _hoverAt, _wakeUntil, _aliveAt;
        static Vector2 _lastMouse = new Vector2(-1f, -1f);
        static bool _flagSleep;
        static int _builds;
        static string[] _infoNames;
        static readonly Dictionary<string, string> _dispToName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        internal static void Post_StartModding(Il2Cpp.WeaponModding __instance)
        {
            Patches.Fired("WeaponModding.StartModding");
            bool sameTable = _wm != null && __instance != null && _wm.Pointer == __instance.Pointer;
            _wm = __instance; _open = true; _flagSleep = false;
            Dirty = true; Mobility.Dirty = true; _rowsAt = 0f;
            if (sameTable && _built && CardAlive() && ScreenKey() == _screenKey)
            {

                Wake();
                return;
            }
            Teardown();
            _buildAt = Time.unscaledTime + 0.5f;
            _measureTries = 0; _remeasureAt = 0f;
        }

        internal static void Post_StopModding(Il2Cpp.WeaponModding __instance)
        {
            Patches.Fired("WeaponModding.StopModding");
            Sleep();
        }

        static void Sleep()
        {
            _open = false;
            if (!_built) return;
            try
            {
                ResetHover();
                if (_c != null && _c.Root != null) _c.Root.SetActive(false);
                if (_layerGo != null) _layerGo.SetActive(false);
            }
            catch { Teardown(); }
        }

        static void Wake()
        {
            try
            {
                if (_c != null && _c.Root != null) _c.Root.SetActive(true);
                if (_layerGo != null) _layerGo.SetActive(true);
                ResetHover();
                _refreshAt = 0f; _hoverAt = 0f;
                var w = CurrentWeapon();
                if (w != null) Handling.Apply(w, "bench");
            }
            catch { Teardown(); _buildAt = Time.unscaledTime + 0.5f; }
        }

        static bool CardAlive()
        {
            try
            {
                if (_c == null || _c.Root == null || _layerGo == null || _canvas == null) return false;
                if (_c.Root.transform.parent == null) return false;
                var cv = FindBenchCanvas();
                if (cv == null) return _ownCanvas;
                return !_ownCanvas && cv.Pointer == _canvas.Pointer;
            }
            catch { return false; }
        }

        static string ScreenKey() { return Screen.width + "x" + Screen.height + "@" + LAYOUT_VERSION; }

        static void ResetHover()
        {
            _hoverKey = ""; _projKey = ""; _pinned = false; _pinnedRound = null; _cardRound = null;
            _tipPinned = -1; _flashUntil = 0f; _flashAnchor = null;
            _lists.Clear();
            for (int i = 0; i < _wasOpen.Count; i++) _wasOpen[i] = false;
            _magTexts.Clear(); _magRts.Clear(); _magPanel = null; _magRowsAt = 0f;
            HideStrip(); HideCard(); HideTip();
            if (_tiles != null) for (int i = 0; i < N; i++) { _tiles[i].ClearProjection(); _tiles[i].InfoHot(false); }
        }

        internal static void Post_AddAttachment(Il2Cpp.ModUI __instance, int index)
        {
            Patches.Fired("ModUI.AddAttachment");
            try { FlashPick(__instance, index); } catch { }
            Dirty = true; Mobility.Dirty = true; _rowsAt = 0f;
        }

        internal static void OnScene()
        {
            _open = false; _wm = null;
            Teardown();
            _giveUp = false; _fails = 0;
        }

        internal static void Tick()
        {
            if (_wm == null || !Tables.Loaded) return;
            if (!(Main.StatCard.Value || Main.DeltaChips.Value || Main.RoundCards.Value)) return;
            float now = Time.unscaledTime;
            if (!_open)
            {

                if (_flagSleep && _built && now >= _aliveAt)
                {
                    _aliveAt = now + 0.5f;
                    bool modding = false;
                    try { modding = Il2Cpp.WeaponModding.isModding; } catch { }
                    if (modding && CardAlive()) { _open = true; _flagSleep = false; Wake(); }
                }
                return;
            }

            if (now >= _aliveAt)
            {
                _aliveAt = now + 0.5f;
                bool modding = true;
                try { modding = Il2Cpp.WeaponModding.isModding; } catch { }
                if (!modding) { Sleep(); _flagSleep = true; return; }
            }
            if (!_built)
            {
                if (_giveUp || now < _buildAt) return;
                try
                {
                    if (!Build()) { _buildAt = now + 1f; return; }
                    _built = true;
                }
                catch (Exception e) { Fail("build: " + e.Message); return; }
            }
            try { Poll(); }
            catch (Exception e) { Fail("poll: " + e.Message); }
        }

        static void Fail(string why)
        {
            Main.Log("[bench] " + why);
            Teardown();
            _fails++;
            if (_fails >= 3) { _giveUp = true; Main.Log("[bench] failed three times; the card is off for this session"); }
            else _buildAt = Time.unscaledTime + 2f;
        }

        static void Teardown()
        {
            try { if (_c != null) _c.Destroy(); } catch { }
            try { if (_layerGo != null) UnityEngine.Object.Destroy(_layerGo); } catch { }
            try { if (_ownCanvasGo != null) UnityEngine.Object.Destroy(_ownCanvasGo); } catch { }
            _c = null; _layerGo = null; _ownCanvasGo = null; _canvas = null;
            _tiles = null; _strip = null; _card = null; _stripTitle = null; _tip = null; _cur = null;
            for (int i = 0; i < _chips.Length; i++) _chips[i] = null;
            for (int i = 0; i < _lines.Length; i++) _lines[i] = null;
            _built = false; _pinned = false; _pinnedRound = null; _cardRound = null; _hoverKey = ""; _projKey = "";
            _tipIdx = -1; _tipPinned = -1;
            _mus.Clear(); _dds.Clear(); _wasOpen.Clear(); _lists.Clear(); _shadow.Clear();
            _magTexts.Clear(); _magRts.Clear(); _magPanel = null; _magCam = null;
            _infoNames = null; _dispToName.Clear();
            _flagSleep = false; _lastMouse = new Vector2(-1f, -1f);
            Skin.Drop();
        }

        static Il2Cpp.WeaponV3 CurrentWeapon()
        {
            try { var m = _wm != null ? _wm.Weapon : null; return m != null ? m.Weapon : null; } catch { return null; }
        }

        static Canvas FindBenchCanvas()
        {
            try
            {
                Transform t = _wm.Mod_Dropdown_Parent;
                if (t == null) t = _wm.UI_Parent;
                var cv = t != null ? t.GetComponentInParent<Canvas>() : null;
                if (cv == null) return null;
                var root = cv.rootCanvas;
                return root != null ? root : cv;
            }
            catch { return null; }
        }

        static bool Build()
        {
            Teardown();
            _canvas = FindBenchCanvas();
            _ownCanvas = _canvas == null;
            if (_ownCanvas)
            {

                _ownCanvasGo = new GameObject("QM_BenchCanvas");
                _canvas = _ownCanvasGo.AddComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.sortingOrder = 4900;
                var sc = _ownCanvasGo.AddComponent<CanvasScaler>();
                sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                sc.referenceResolution = new Vector2(1920f, 1080f);
                sc.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                sc.matchWidthOrHeight = 1f;
            }
            if (!Skin.Bind(_canvas)) return false;
            BorrowFont();

            float k0 = Mathf.Clamp(Skin.CanvasHeight() / Chrome.AUTO_REF_H, Chrome.MIN_SCALE, Chrome.MAX_SCALE);
            float cw = Skin.CanvasWidth(), ch = Skin.CanvasHeight();
            float bottom = -ch * 0.5f + 12f;

            _screenKey = ScreenKey();
            bool haveSaved = Main.CardX.Value > Chrome.UNSET + 1f && Main.BandW.Value >= 720f && Main.BandScreen.Value == _screenKey;
            bool measure = !haveSaved && Main.AutoPlace.Value;
            float left = 0f, right = 0f, scale;
            string why;
            int found = 0;
            if (haveSaved)
            {

                _designW = Main.BandW.Value; scale = Main.UiScale.Value; _cols = Layout.ColsFor(_designW);
                why = "saved for " + _screenKey + (Main.AutoPlace.Value ? " (measured)" : " (dragged)");
            }
            else
            {
                Transform parts = null, presets = null;
                try { parts = _wm.Mod_Dropdown_Parent; } catch { }
                try { presets = _wm.WeaponPresetUI_Parent; } catch { }
                found = Layout.DetectBand(parts, presets, out left, out right, out why);
                float fitW, fitK; int fitCols; string fitWhy;
                Layout.Fit(right - left - 32f, k0, out fitW, out fitK, out fitCols, out fitWhy);
                why = fitWhy + "; band " + left.ToString("0") + ".." + right.ToString("0") + "; " + why;
                _designW = fitW; scale = fitK; _cols = fitCols;
            }
            _rows = (N + _cols - 1) / _cols;
            _cardH = PAD + HEAD_H + 6f + _rows * TILE_H + (_rows - 1) * GAP + 4f + 16f + PAD;

            _c = new Chrome();
            if (!_c.Build(_canvas, "QM_Card", _designW, _cardH, measure ? Chrome.UNSET : Main.CardX.Value, measure ? Chrome.UNSET : Main.CardY.Value,
                          scale, Chrome.AUTO_REF_H, _ownCanvas ? 30 : 1))
                return false;
            if (measure)
            {
                _c.PosX = cw * 0.5f + (left + right) * 0.5f - _designW * _c.Scale * 0.5f;
                _c.PosY = bottom + _cardH * _c.Scale * 0.5f;
                _c.Clamp();
                _c.Dirty = false;
                if (found == 2 || _measureTries >= 2)
                {
                    Main.PersistAuto(_c.PosX, _c.PosY, _c.Scale, _designW, _screenKey);
                    why = "measured and saved for " + _screenKey + (found == 2 ? "" : " against " + found + " panel(s)") + "; " + why;
                    _remeasureAt = 0f;
                }
                else
                {

                    why = "measured against " + found + " panel(s), not saved yet; " + why;
                    _remeasureAt = Time.unscaledTime + 2f;
                }
            }
            else if (!haveSaved) why = "dragged on another screen, clamped; " + why;

            var body = _c.Body;
            float iw = _designW - 2f * PAD;
            float y = PAD;
            var headBg = Skin.Rect(body, "HeaderBg", 2f, 2f, _designW - 4f, PAD + HEAD_H - 4f);
            Skin.Img(headBg, Skin.Fill, Skin.Head, false);
            var rule = Skin.Rect(body, "HeaderRule", 2f, PAD + HEAD_H - 2f, _designW - 4f, 1f);
            Skin.Img(rule, null, Skin.RowEdge, false);
            _title = Skin.Text_(Skin.Rect(body, "Title", PAD, y, 150f, HEAD_H), "QUARTERMASTER", 14f, true, Skin.Text, TextAlignmentOptions.Left);
            _title.characterSpacing = 2f;
            _sub = Skin.Text_(Skin.Rect(body, "Sub", PAD + 150f, y, iw * 0.5f - 150f, HEAD_H), "", 13f, false, Skin.Text, TextAlignmentOptions.Left);
            _kit = Skin.Text_(Skin.Rect(body, "Kit", PAD + iw * 0.5f, y, iw * 0.5f - 40f, HEAD_H), "", 13f, true, Skin.Text, TextAlignmentOptions.Right);
            y += HEAD_H + 6f;

            _tiles = new Tile[N];
            float tw = (iw - (_cols - 1) * GAP) / _cols;
            for (int i = 0; i < N; i++)
            {
                int col = i % _cols, row = i / _cols;
                _tiles[i] = Tile.Make(body, "T" + i, PAD + col * (tw + GAP), y + row * (TILE_H + GAP), tw, TILE_H, LABELS[i], TIPS[i]);
            }
            y += _rows * TILE_H + (_rows - 1) * GAP + 4f;
            _foot = Skin.Text_(Skin.Rect(body, "Foot", PAD, y, iw - 36f, 16f), "", 10.5f, false, Skin.TextDim, TextAlignmentOptions.Left);

            _c.AddChrome(PAD + HEAD_H);
            BuildLayer();
            BuildCatalog();

            var w = CurrentWeapon();
            if (w != null) Handling.Apply(w, "bench");
            Dirty = true;
            Main.Log("[bench] card built on " + (_ownCanvas ? "own overlay" : "bench canvas '" + _canvas.gameObject.name + "' " + _canvas.renderMode)
                   + ", font " + (Skin.Font != null ? Skin.Font.name : "<builtin>") + ", scale " + _c.Scale.ToString("0.00")
                   + ", width " + _designW.ToString("0") + " x " + _cardH.ToString("0") + " in " + _cols + " columns (" + why + "), build " + (++_builds) + " this session"
                   + ", catalog " + (_infoNames != null ? _infoNames.Length.ToString() : "none"));
            return true;
        }

        static void BorrowFont()
        {
            if (Skin.Font != null) return;
            TMP_Text best = null; int bestScore = int.MinValue;
            var names = new System.Text.StringBuilder();
            try
            {
                var all = _canvas.GetComponentsInChildren<TextMeshProUGUI>(true);
                var seen = new HashSet<string>();
                for (int i = 0; all != null && i < all.Length; i++)
                {
                    var t = all[i]; if (t == null || t.font == null) continue;
                    string fn = t.font.name ?? "";
                    if (!seen.Add(fn)) continue;
                    int sc = FontScore(fn);
                    names.Append(fn).Append('=').Append(sc).Append(' ');
                    if (sc > bestScore) { bestScore = sc; best = t; }
                }
            }
            catch { }
            if (best == null) { try { best = _wm.interactKeyUIText; } catch { } }
            if (best != null) Skin.BorrowFont(best);
            Main.Log("[bench] fonts seen: " + names + (best != null ? "-> " + best.font.name : "-> none"));
        }

        static int FontScore(string name)
        {
            string n = name.ToLowerInvariant();
            int s = 0;
            if (n.Contains("thin") || n.Contains("light") || n.Contains("hairline")) s -= 10;
            if (n.Contains("caps")) s -= 6;
            if (n.Contains("italic")) s -= 3;
            if (n.Contains("bold") || n.Contains("semibold") || n.Contains("medium") || n.Contains("heavy") || n.Contains("black")) s += 4;
            if (n.Contains("regular") || n.Contains("book")) s += 3;
            return s;
        }

        static void BuildLayer()
        {
            _layerGo = new GameObject("QM_Layer");
            _layerGo.layer = _canvas.gameObject.layer;
            var lrt = _layerGo.AddComponent<RectTransform>();
            lrt.SetParent(_canvas.transform, false);
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            lrt.localScale = Vector3.one;
            lrt.SetAsLastSibling();
            try
            {
                var sub = _layerGo.AddComponent<Canvas>();
                sub.overrideSorting = true;
                sub.sortingOrder = 31000;
            }
            catch { }
            try { if (_canvas.GetComponent<LayoutGroup>() != null) _layerGo.AddComponent<LayoutElement>().ignoreLayout = true; } catch { }
            var root = _layerGo.transform;
            float k = _c.Scale;

            _strip = Skin.Rect(root, "QM_Strip", 0f, 0f, STRIP_W, 60f, new Vector2(0.5f, 0.5f));
            _strip.pivot = new Vector2(0f, 1f);
            Skin.Img(_strip, Skin.Fill, Palette.Card, false);
            _stripEdge = Skin.Rect(_strip, "Edge", 0f, 0f, STRIP_W, 60f);
            Skin.Img(_stripEdge, Skin.Edge, Palette.CardEdge, false);
            _stripTitle = Skin.Text_(Skin.Rect(_strip, "T", 8f, 6f, STRIP_W - 16f, 20f), "", 13f, true, Skin.Text, TextAlignmentOptions.Left);
            _stripKind = Skin.Text_(Skin.Rect(_strip, "K", 8f, 26f, STRIP_W - 16f, 16f), "", 10.5f, true, Skin.TextDim, TextAlignmentOptions.Left);
            _stripKind.characterSpacing = 1.5f;
            for (int i = 0; i < _chips.Length; i++)
            {
                _chips[i] = Chip.Make(_strip, "C" + i, 8f, 46f + i * 26f, STRIP_W - 16f, 22f, 12f);
                _chips[i].Show(false);
            }
            _strip.localScale = new Vector3(k, k, 1f);
            _strip.gameObject.SetActive(false);

            _tip = Skin.Rect(root, "QM_Tip", 0f, 0f, TIP_W, 100f, new Vector2(0.5f, 0.5f));
            _tip.pivot = new Vector2(0f, 1f);
            Skin.Img(_tip, Skin.Fill, Palette.Card, false);
            _tipEdge = Skin.Rect(_tip, "Edge", 0f, 0f, TIP_W, 100f);
            Skin.Img(_tipEdge, Skin.Edge, Palette.CardEdge, false);
            _tipTitle = Skin.Text_(Skin.Rect(_tip, "T", 8f, 6f, TIP_W - 16f, 18f), "", 11f, true, Skin.TextDim, TextAlignmentOptions.Left);
            _tipTitle.characterSpacing = 1.5f;
            _tipBody = Wrap(Skin.Text_(Skin.Rect(_tip, "B", 8f, 26f, TIP_W - 16f, 60f), "", 12f, false, Skin.Text, TextAlignmentOptions.TopLeft));
            _tip.localScale = new Vector3(k, k, 1f);
            _tip.gameObject.SetActive(false);

            _card = Skin.Rect(root, "QM_RoundCard", 0f, 0f, CARD_W, 120f, new Vector2(0.5f, 0.5f));
            _card.pivot = new Vector2(0f, 1f);
            Skin.Img(_card, Skin.Fill, Palette.Card, false);
            _cardEdge = Skin.Rect(_card, "Edge", 0f, 0f, CARD_W, 120f);
            Skin.Img(_cardEdge, Skin.Edge, Palette.CardEdge, false);
            float cw = CARD_W - 16f;
            _cardTitle = Skin.Text_(Skin.Rect(_card, "Title", 8f, 6f, cw, 22f), "", 15.5f, true, Skin.Text, TextAlignmentOptions.Left);
            _cardTitle.characterSpacing = 1f;
            _cardKind = Skin.Text_(Skin.Rect(_card, "Kind", 8f, 28f, cw, 16f), "", 10.5f, true, Skin.TextDim, TextAlignmentOptions.Left);
            _cardKind.characterSpacing = 1.5f;
            _cardSummary = Skin.Text_(Skin.Rect(_card, "Sum", 8f, 46f, cw, 20f), "", 13f, true, Skin.Text, TextAlignmentOptions.Left);
            string[] ll = { "DAMAGE", "PENETRATION", "VELOCITY", "BLEEDING", "FRACTURE", "RELIABILITY" };
            for (int i = 0; i < _lines.Length; i++)
                _lines[i] = StatLine.Make(_card, "L" + i, 8f, 72f + i * 23f, cw, 22f, ll[i]);
            _cardPlates = Wrap(Skin.Text_(Skin.Rect(_card, "Plates", 8f, 214f, cw, 34f), "", 12f, false, Skin.Text, TextAlignmentOptions.TopLeft));
            _cardBody = Wrap(Skin.Text_(Skin.Rect(_card, "Body", 8f, 252f, cw, 80f), "", 12f, false, Skin.Text, TextAlignmentOptions.TopLeft));
            _cardBest = Wrap(Skin.Text_(Skin.Rect(_card, "Best", 8f, 336f, cw, 36f), "", 12f, true, Skin.Text, TextAlignmentOptions.TopLeft));
            _cardHint = Skin.Text_(Skin.Rect(_card, "Hint", 8f, 0f, cw, 16f), "", 10.5f, false, Skin.TextDim, TextAlignmentOptions.Right);
            _card.localScale = new Vector3(k, k, 1f);
            _card.gameObject.SetActive(false);
        }

        static TextMeshProUGUI Wrap(TextMeshProUGUI t)
        {
            t.enableWordWrapping = true;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        static void BuildCatalog()
        {
            _infoNames = null; _dispToName.Clear();
            try
            {
                var lm = Il2Cpp.LoadoutManager.singleton;
                var infos = lm != null ? lm.WeaponModInfo : null;
                if (infos == null) return;
                _infoNames = new string[infos.Length];
                for (int i = 0; i < infos.Length; i++)
                {
                    var inf = infos[i]; if (inf == null) continue;
                    string n = "", d = "";
                    try { n = inf.Name ?? ""; d = inf.DisplayName ?? ""; } catch { }
                    _infoNames[i] = n;
                    if (d.Length > 0 && !_dispToName.ContainsKey(d)) _dispToName[d] = n;
                }
            }
            catch (Exception e) { Main.Once("catalog: " + e.Message); }
        }

        static void Poll()
        {
            float now = Time.unscaledTime;
            Vector2 mp = Input.mousePosition;
            bool down = Input.GetMouseButtonDown(0);
            bool held = Input.GetMouseButton(0);
            bool up = Input.GetMouseButtonUp(0);
            bool esc = Input.GetKeyDown(KeyCode.Escape);
            bool moved = (mp - _lastMouse).sqrMagnitude > 0.25f;
            if (moved) _lastMouse = mp;
            bool scrolled = Input.mouseScrollDelta.sqrMagnitude > 0.0001f;
            if (down || up || scrolled || esc) _wakeUntil = now + 0.4f;
            bool flashEnd = _flashUntil > 0f && now >= _flashUntil;
            bool active = moved || held || now < _wakeUntil || flashEnd || now >= _hoverAt;

            if (_remeasureAt > 0f && now >= _remeasureAt) { _remeasureAt = 0f; Remeasure(); if (!_built) return; }

            if (active || _c.Dragging)
            {
                if (_c.Drag(mp, down))
                {
                    if (_c.Dirty) { Main.PersistCard(_c.PosX, _c.PosY, _c.Scale, _designW, _screenKey); _c.Dirty = false; }
                    return;
                }
                if (_c.Dirty) { Main.PersistCard(_c.PosX, _c.PosY, _c.Scale, _designW, _screenKey); _c.Dirty = false; }
            }

            if (Dirty || now >= _refreshAt)
            {
                _refreshAt = now + 2f;
                if (Dirty) _rowsAt = 0f;
                Dirty = false;
                Refresh();
            }
            if (esc) { _pinned = false; _pinnedRound = null; _tipPinned = -1; }
            if (!active) return;
            _hoverAt = now + 0.25f;
            if (flashEnd) _flashUntil = 0f;
            Hover(mp, down);
        }

        static void Remeasure()
        {
            _measureTries++;
            if (_c == null || !Main.AutoPlace.Value) return;
            Transform parts = null, presets = null;
            try { parts = _wm.Mod_Dropdown_Parent; } catch { }
            try { presets = _wm.WeaponPresetUI_Parent; } catch { }
            float left, right; string why;
            int found = Layout.DetectBand(parts, presets, out left, out right, out why);
            float k0 = Mathf.Clamp(Skin.CanvasHeight() / Chrome.AUTO_REF_H, Chrome.MIN_SCALE, Chrome.MAX_SCALE);
            float fitW, fitK; int fitCols; string fitWhy;
            Layout.Fit(right - left - 32f, k0, out fitW, out fitK, out fitCols, out fitWhy);
            bool last = found == 2 || _measureTries >= 2;
            bool resize = fitCols != _cols || Math.Abs(fitW - _designW) > 24f || Math.Abs(fitK - _c.Scale) > 0.03f;
            if (resize && last)
            {
                Main.Log("[bench] second look: " + fitWhy + "; " + why + " -> one rebuild for the new size");
                Teardown();
                _buildAt = Time.unscaledTime;
                return;
            }
            float cw = Skin.CanvasWidth(), ch = Skin.CanvasHeight();
            _c.PosX = cw * 0.5f + (left + right) * 0.5f - _designW * _c.Scale * 0.5f;
            _c.PosY = -ch * 0.5f + 12f + _cardH * _c.Scale * 0.5f;
            _c.Clamp(); _c.Dirty = false;
            if (last)
            {
                Main.PersistAuto(_c.PosX, _c.PosY, _c.Scale, _designW, _screenKey);
                Main.Log("[bench] second look: saved for " + _screenKey + " against " + found + " panel(s); " + why);
            }
            else _remeasureAt = Time.unscaledTime + 2f;
        }

        static Snap Compute(Il2Cpp.WeaponV3 w, List<PartDef> parts, RoundDef rd, float kitDeltaKg)
        {
            var s = new Snap();
            var b = Handling.BaseOf(w);
            var wd = Tables.Weapon(Handling.WeaponName(w));
            float rs = Mathf.Clamp(Main.RecoilScale.Value, 0f, 2f);
            var f = Model.Combine(parts, rs);
            Il2Cpp.GunStats gs = null; try { gs = w.gunStats; } catch { }

            float pellets = rd != null ? Math.Max(1f, rd.Eff("pellets")) : 1f;
            float dmg = rd != null ? rd.Eff("damage") : 0f;
            Put(s, T_DMG, rd == null ? "-" : (pellets > 1f ? pellets.ToString("0") + " x " + dmg.ToString("0") : dmg.ToString("0")), dmg * pellets / 400f);

            float ap = rd != null ? rd.Eff("ap") : 0f;
            Put(s, T_PEN, rd == null ? "-" : "Level " + Model.PenClass(ap), (ap - 1f) / 4f);

            float spd = rd != null ? rd.Eff("speed") * f.Vel : 0f;
            Put(s, T_VEL, rd == null ? "-" : spd.ToString("0") + " m/s" + (spd < 340f ? " sub" : ""), (spd - 200f) / 900f);

            bool manual = wd != null && wd.Manual;
            float tbr = 0f; try { if (gs != null) tbr = gs.TimeBetweenRounds; } catch { }
            int rpm = tbr > 0.0001f ? (int)Math.Round(60f / tbr) : (wd != null ? wd.Rpm : 0);
            Put(s, T_RPM, manual ? "manual" : rpm + " rpm", manual ? 0.1f : rpm / 1200f);

            float baseMoa = b != null && b.Moa > 0f ? b.Moa : (wd != null ? wd.Moa : 0f);
            if (baseMoa <= 0f) { try { if (gs != null) baseMoa = gs.MOA; } catch { } }
            float moa = baseMoa * f.Moa * (rd != null ? rd.MoaFactor : 1f) + (rd != null ? rd.Eff("selfMOA") : 0f);
            Put(s, T_ACC, moa.ToString("0.0") + " MOA", 1f - moa / 12f);

            float mis = rd != null ? rd.Misfire * 10f : 0f;
            Put(s, T_REL, rd == null ? "-" : mis.ToString("0.0") + " per 1000", 1f - mis / 20f);

            Put(s, T_VR, Model.Pct(f.V), (Model.VMax - f.V) / (Model.VMax - Model.VMin));
            Put(s, T_HR, Model.Pct(f.H), (Model.VMax - f.H) / (Model.VMax - Model.VMin));

            float ergo = Model.ErgoTotal(b != null && b.Ergo > 0f ? b.Ergo : (wd != null && wd.Ergo > 0f ? wd.Ergo : 60f), f.Ergo);
            Put(s, T_ERGO, ergo.ToString("0") + " / 100", (ergo - 20f) / 80f);

            float rec = wd != null ? wd.Weight : (b != null ? b.Weight : 0f);
            float kg = rec + f.Weight + Handling.SeatedMagMass(w);
            Put(s, T_WT, kg.ToString("0.00") + " kg", 1f - kg / 8f);

            float mult = Model.Mobility(Mobility.Mass + kitDeltaKg, Main.MobilityFreeUpTo.Value, Main.MobilityFullAt.Value, Main.MobilityFloor.Value);
            Put(s, T_MOB, (mult * 100f).ToString("0") + " % speed", (mult - 0.88f) / 0.12f);

            float sup = -1f;
            for (int i = 0; i < parts.Count; i++) { var p = parts[i]; if (p != null && p.Suppressed && (sup < 0f || p.Sup < sup)) sup = p.Sup; }
            float range = 0f; try { if (gs != null) range = gs.gunshotRange; } catch { }
            bool canned = sup >= 0f && sup < 0.99f;
            string snd = !canned ? "loud" : (sup <= 0.2f ? "very quiet" : (sup <= 0.5f ? "quiet" : "muffled"));
            Put(s, T_SND, snd + "  " + range.ToString("0") + " m", canned ? 1f - sup * 0.5f : 0.15f);
            return s;
        }

        static void Put(Snap s, int i, string text, float frac) { s.Text[i] = text; s.Frac[i] = Mathf.Clamp01(frac); }

        static void Refresh()
        {
            if (_tiles == null) return;
            var w = CurrentWeapon();
            if (w == null) { SetText(_sub, "no weapon on the bench"); return; }
            var b = Handling.BaseOf(w);
            if (b == null) Handling.Apply(w, "bench");
            var rd = Handling.SeatedRound(w);
            var parts = Handling.PartsOf(w, null, null);
            var f = Handling.FactorsFor(w);

            string disp = ""; try { disp = w.displayName ?? ""; } catch { }
            SetText(_sub, disp.ToUpperInvariant() + (rd != null ? "    " + rd.Shell : "    no round loaded"));

            _cur = Compute(w, parts, rd, 0f);
            for (int i = 0; i < N; i++) _tiles[i].Set(_cur.Text[i], _cur.Frac[i]);
            _projKey = "";

            SetText(_kit, "KIT " + Mobility.Mass.ToString("0.0") + " KG    SPEED " + (Mobility.Mult * 100f).ToString("0") + " %");
            string heavy = Mobility.Heavy(3);
            SetText(_foot, (heavy.Length > 0 ? "heaviest: " + heavy : "") + (f.Unknown > 0 ? "    " + f.Unknown + " part(s) not in the table" : "")
                         + (Main.ShowTableHash.Value ? "    table " + Tables.Hash : ""));
        }

        static void SetText(TextMeshProUGUI t, string s)
        {
            if (t == null || s == null) return;
            IntPtr key = t.Pointer;
            string last;
            if (_shadow.TryGetValue(key, out last) && last == s) return;
            t.text = s;
            _shadow[key] = s;
        }

        static void ShowProjection(Snap p)
        {
            if (_tiles == null || p == null) return;
            for (int i = 0; i < N; i++) _tiles[i].Project(p.Text[i], p.Frac[i]);
        }

        static void ClearProjection()
        {
            _projKey = "";
            if (_tiles == null) return;
            for (int i = 0; i < N; i++) _tiles[i].ClearProjection();
        }

        static void ProjectRound(RoundDef rd)
        {
            string key = "round:" + rd.Shell;
            if (key == _projKey) return;
            _projKey = key;
            var w = CurrentWeapon();
            if (w == null) return;
            ShowProjection(Compute(w, Handling.PartsOf(w, null, null), rd, 0f));
        }

        static void Hover(Vector2 mp, bool down)
        {
            float now = Time.unscaledTime;
            bool cursorOk = true;
            try { cursorOk = Cursor.visible; } catch { }
            if (now >= _rowsAt) { _rowsAt = now + 5f; RefreshRows(); }

            Il2Cpp.ModUI hitMu = null; int hitOpt = -1; RectTransform listRt = null; Camera listCam = null;
            if (cursorOk)
            {
                Vector2 lp;
                bool haveLp = Skin.CanvasPoint(mp, out lp);
                for (int m = 0; m < _dds.Count && hitMu == null; m++)
                {
                    try
                    {
                        var dd = _dds[m];
                        if (dd == null) continue;
                        IntPtr key = dd.Pointer;
                        bool open = dd.IsExpanded;
                        if (!open) { if (_wasOpen[m]) { _wasOpen[m] = false; _lists.Remove(key); } continue; }
                        ListCache lc;
                        if (!_wasOpen[m] || !_lists.TryGetValue(key, out lc))
                        {

                            _wasOpen[m] = true;
                            lc = OpenList(dd, now);
                            if (lc == null) continue;
                            _lists[key] = lc;
                        }
                        if (!haveLp || now < lc.ReadyAt) continue;
                        if (!lc.Filled) FillList(lc);
                        Rect lr;
                        if (!Layout.CanvasRect(lc.ListRt, out lr) || !lr.Contains(lp)) continue;
                        Rect cr;
                        if (!Layout.CanvasRect(lc.ContentRt, out cr)) continue;
                        float dx = cr.xMin - lc.Content0.X0, dy = cr.yMin - lc.Content0.Y0;
                        for (int k = 0; k < lc.Boxes.Count; k++)
                        {
                            var b = lc.Boxes[k];
                            b.X0 += dx; b.X1 += dx; b.Y0 += dy; b.Y1 += dy;
                            if (b.Has(lp)) { hitMu = _mus[m]; hitOpt = lc.Idx[k]; listRt = lc.ListRt; listCam = lc.Cam; break; }
                        }
                    }
                    catch { _rowsAt = 0f; }
                }
            }

            if (hitMu != null)
            {
                HideTip();
                string name = OptionName(hitMu, hitOpt);
                string key = "dd:" + hitOpt + ":" + name;
                var rd = Tables.RoundOfPart(name);
                var pd = rd == null ? Tables.Part(name) : null;
                if (rd != null && Main.RoundCards.Value)
                {
                    HideStrip();
                    ShowRoundCard(rd, listRt, listCam, _pinned && _pinnedRound == rd);
                    if (Main.StatCard.Value) ProjectRound(rd);
                    if (down) { _pinned = true; _pinnedRound = rd; }
                }
                else if (pd != null && Main.DeltaChips.Value)
                {
                    if (key != _hoverKey) { _hoverKey = key; FillPartChips(hitMu, pd); }
                    PlaceStrip(listRt, listCam);
                    if (!_pinned) HideCard();
                }
                else { HideStrip(); if (!_pinned) HideCard(); ClearProjection(); }
                return;
            }
            if (_hoverKey.Length > 0) { _hoverKey = ""; ClearProjection(); }
            if (_projKey.Length > 0) ClearProjection();

            int infoHit = -1;
            if (cursorOk && _tiles != null && Skin.In(_c.Rt, mp))
                for (int i = 0; i < N; i++) if (Skin.In(_tiles[i].InfoRt, mp)) { infoHit = i; break; }
            if (down)
            {
                if (infoHit >= 0) _tipPinned = _tipPinned == infoHit ? -1 : infoHit;
                else if (_tipPinned >= 0 && !InTip(mp)) _tipPinned = -1;
            }
            if (_tiles != null) for (int i = 0; i < N; i++) _tiles[i].InfoHot(i == infoHit || i == _tipPinned);
            int show = infoHit >= 0 ? infoHit : _tipPinned;
            if (show >= 0) ShowTip(show); else HideTip();

            RoundDef magRound = null; RectTransform magRt = null; Camera magCam = null;
            if (cursorOk && Main.RoundCards.Value)
            {
                try
                {
                    var mm = _wm.MagManager;
                    if (mm != null && mm.gameObject.activeInHierarchy)
                    {
                        if (now >= _magRowsAt) { _magRowsAt = now + 3f; RefreshMagRows(mm); }
                        Rect pr;
                        if (_magPanel == null || !Layout.CanvasRect(_magPanel, out pr) || InRect(pr, mp))
                        {
                            for (int i = 0; i < _magRts.Count; i++)
                            {
                                var rt = _magRts[i]; if (rt == null) continue;
                                if (RectTransformUtility.RectangleContainsScreenPoint(rt, mp, _magCam))
                                {
                                    magRound = RoundOfDisplay(_magTexts[i].text); magRt = rt; magCam = _magCam;
                                    break;
                                }
                            }
                        }
                    }
                    else if (_magRts.Count > 0) { _magTexts.Clear(); _magRts.Clear(); _magPanel = null; _magRowsAt = 0f; }
                }
                catch { _magRowsAt = 0f; }
            }
            if (magRound != null)
            {
                HideStrip();
                ShowRoundCard(magRound, magRt, magCam, _pinned && _pinnedRound == magRound);
                if (down) { _pinned = true; _pinnedRound = magRound; }
                return;
            }

            if (Time.unscaledTime < _flashUntil && _flashAnchor != null) PlaceStrip(_flashAnchor, Layout.CamOf(_flashAnchor));
            else HideStrip();

            if (_pinned && _pinnedRound != null)
            {
                ShowRoundCard(_pinnedRound, null, null, true);
                if (down && !InCard(mp)) { _pinned = false; _pinnedRound = null; HideCard(); }
            }
            else HideCard();
        }

        static void RefreshRows()
        {
            _mus.Clear(); _dds.Clear(); _wasOpen.Clear();
            try
            {
                var parent = _wm.Mod_Dropdown_Parent;
                var arr = parent != null ? parent.GetComponentsInChildren<Il2Cpp.ModUI>(true) : null;
                for (int i = 0; arr != null && i < arr.Length; i++)
                {
                    var mu = arr[i]; if (mu == null) continue;
                    TMP_Dropdown dd = null;
                    try { dd = mu.dropdown; } catch { }
                    _mus.Add(mu); _dds.Add(dd);
                    _wasOpen.Add(dd != null && _lists.ContainsKey(dd.Pointer));
                }
            }
            catch { }
        }

        static ListCache OpenList(TMP_Dropdown dd, float now)
        {
            GameObject list = null;
            try { list = dd.m_Dropdown; } catch { }
            if (list == null) { var lt = dd.transform.Find("Dropdown List"); list = lt != null ? lt.gameObject : null; }
            if (list == null) return null;
            var content = FindChild(list.transform, "Content", 0);
            if (content == null) return null;
            var lc = new ListCache();
            lc.ListRt = list.GetComponent<RectTransform>();
            lc.ContentRt = content.TryCast<RectTransform>();
            lc.Cam = Layout.CamOf(list.transform);
            lc.ReadyAt = now + 0.15f;
            return lc.ListRt != null && lc.ContentRt != null ? lc : null;
        }

        static void FillList(ListCache lc)
        {
            lc.Filled = true;
            Rect cr;
            if (!Layout.CanvasRect(lc.ContentRt, out cr)) return;
            lc.Content0 = new Box { X0 = cr.xMin, Y0 = cr.yMin, X1 = cr.xMax, Y1 = cr.yMax };
            int n = lc.ContentRt.childCount;
            for (int k = 0; k < n; k++)
            {
                var ch = lc.ContentRt.GetChild(k);
                if (ch == null || !ch.gameObject.activeSelf) continue;
                string nm = ch.name;
                if (nm == null || !nm.StartsWith("Item ")) continue;
                int colon = nm.IndexOf(':');
                if (colon < 6) continue;
                int idx;
                if (!int.TryParse(nm.Substring(5, colon - 5), out idx)) continue;
                var rt = ch.TryCast<RectTransform>();
                Rect r;
                if (rt == null || !Layout.CanvasRect(rt, out r)) continue;
                lc.Idx.Add(idx);
                lc.Boxes.Add(new Box { X0 = r.xMin, Y0 = r.yMin, X1 = r.xMax, Y1 = r.yMax });
            }
        }

        static void RefreshMagRows(Il2Cpp.MagManagerUI mm)
        {
            _magTexts.Clear(); _magRts.Clear(); _magPanel = null;
            try
            {
                _magPanel = mm.transform.TryCast<RectTransform>();
                _magCam = Layout.CamOf(mm.transform);
                var rows = mm.GetComponentsInChildren<Il2Cpp.MagManagerMagUI>(true);
                for (int i = 0; rows != null && i < rows.Length; i++)
                {
                    var r = rows[i]; if (r == null) continue;
                    var t = r.AmmoNameText; if (t == null) continue;
                    var rt = t.rectTransform; if (rt == null) continue;
                    _magTexts.Add(t); _magRts.Add(rt);
                }
            }
            catch { }
        }

        static bool InRect(Rect r, Vector2 screen)
        {
            Vector2 lp;
            return Skin.CanvasPoint(screen, out lp) && r.Contains(lp);
        }

        static string OptionName(Il2Cpp.ModUI mu, int opt)
        {
            try
            {
                var av = mu.availableModIndicies;
                if (av != null && opt >= 0 && opt < av.Count)
                {
                    int idx = av[opt];
                    if (_infoNames != null && idx >= 0 && idx < _infoNames.Length && !string.IsNullOrEmpty(_infoNames[idx])) return _infoNames[idx];
                }
            }
            catch { }
            try
            {
                var dd = mu.dropdown;
                var opts = dd != null ? dd.options : null;
                if (opts != null && opt >= 0 && opt < opts.Count)
                {
                    string d = opts[opt].text ?? "";
                    string n;
                    return _dispToName.TryGetValue(d, out n) ? n : d;
                }
            }
            catch { }
            return "";
        }

        static RoundDef RoundOfDisplay(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string n;
            if (_dispToName.TryGetValue(text.Trim(), out n)) { var r = Tables.RoundOfPart(n); if (r != null) return r; }
            return Tables.RoundOfPart(text) ?? Tables.RoundOfShell(text);
        }

        static Transform FindChild(Transform root, string name, int depth)
        {
            if (root == null || depth > 6) return null;
            int n = root.childCount;
            for (int i = 0; i < n; i++)
            {
                var c = root.GetChild(i);
                if (c != null && c.name == name) return c;
            }
            for (int i = 0; i < n; i++)
            {
                var r = FindChild(root.GetChild(i), name, depth + 1);
                if (r != null) return r;
            }
            return null;
        }

        static bool InCard(Vector2 mp)
        {
            try { return _card != null && _card.gameObject.activeSelf && RectTransformUtility.RectangleContainsScreenPoint(_card, mp, Skin.Cam); }
            catch { return false; }
        }

        static bool InTip(Vector2 mp)
        {
            try { return _tip != null && _tip.gameObject.activeSelf && RectTransformUtility.RectangleContainsScreenPoint(_tip, mp, Skin.Cam); }
            catch { return false; }
        }

        static string ClassLabel(PartDef pd)
        {
            string c = (pd.Cls ?? "").ToUpperInvariant();
            if (c.Length == 0 || c == "COSMETIC") c = (pd.Type ?? "PART").ToUpperInvariant();
            return c;
        }

        static void FillPartChips(Il2Cpp.ModUI mu, PartDef pd)
        {
            var w = CurrentWeapon();
            var names = new List<string>();
            var cur = Handling.PartsOf(w, names, null);
            string curName = "";
            try { var mp = mu.modParent; var am = mp != null ? mp.AttachedMod : null; if (am != null) curName = Handling.PartName(am); } catch { }
            bool fitted = curName.Length > 0 && string.Equals(curName, pd.Name, StringComparison.OrdinalIgnoreCase);

            var without = new List<PartDef>(cur);
            int ri = -1;
            for (int i = 0; i < names.Count; i++) if (string.Equals(names[i], curName, StringComparison.OrdinalIgnoreCase)) { ri = i; break; }
            if (ri >= 0) without.RemoveAt(ri);
            var with = new List<PartDef>(without); with.Add(pd);
            float rs = Mathf.Clamp(Main.RecoilScale.Value, 0f, 2f);
            var fFrom = Model.Combine(fitted ? without : cur, rs);
            var fTo = Model.Combine(with, rs);
            var curPart = ri >= 0 ? cur[ri] : null;
            float dW = pd.Weight - (fitted ? 0f : (curPart != null ? curPart.Weight : 0f));

            var list = new List<KeyValuePair<string, ChipKind>>();
            var ups = new List<bool>();
            AddPct(list, ups, "Vertical Recoil", fTo.V / fFrom.V, true);
            AddPct(list, ups, "Horizontal Recoil", fTo.H / fFrom.H, true);
            AddPct(list, ups, "Accuracy", fFrom.Moa / fTo.Moa, false);
            int dE = (int)Math.Round(Model.ErgoTotal(60f, fTo.Ergo) - Model.ErgoTotal(60f, fFrom.Ergo));
            if (dE != 0) { list.Add(new KeyValuePair<string, ChipKind>("Ergonomics " + (dE > 0 ? "+" : "") + dE, dE > 0 ? ChipKind.Good : ChipKind.Bad)); ups.Add(dE > 0); }
            AddPct(list, ups, "Velocity", fTo.Vel / fFrom.Vel, false);

            if (Math.Abs(dW) >= 0.005f) { list.Add(new KeyValuePair<string, ChipKind>("Weight " + (dW > 0 ? "+" : "") + dW.ToString("0.00") + " kg", dW > 0 ? ChipKind.Bad : ChipKind.Good)); ups.Add(dW < 0); }
            if (pd.Suppressed) { list.Add(new KeyValuePair<string, ChipKind>(pd.Cls == "integral" ? "Integral suppressor" : "Suppressed", ChipKind.Neutral)); ups.Add(true); }
            if (list.Count == 0) { list.Add(new KeyValuePair<string, ChipKind>("COSMETIC", ChipKind.Cosmetic)); ups.Add(true); }

            SetText(_stripTitle, (pd.Name.Length > 36 ? pd.Name.Substring(0, 35) + "…" : pd.Name));
            SetText(_stripKind, ClassLabel(pd) + (fitted ? "    FITTED NOW" : "    vs the part fitted"));
            int shown = 0;
            for (int i = 0; i < _chips.Length; i++)
            {
                if (i < list.Count) { _chips[i].Set(list[i].Key, list[i].Value, ups[i]); _chips[i].Show(true); shown++; }
                else _chips[i].Show(false);
            }
            float h = 46f + shown * 26f + 6f;
            _strip.sizeDelta = new Vector2(STRIP_W, h);
            _stripEdge.sizeDelta = new Vector2(STRIP_W, h);

            if (_tiles != null && Main.StatCard.Value && w != null)
            {
                _projKey = "part:" + pd.Name;
                ShowProjection(Compute(w, fitted ? cur : with, Handling.SeatedRound(w), fitted ? 0f : dW));
            }
        }

        static void AddPct(List<KeyValuePair<string, ChipKind>> list, List<bool> ups, string label, float ratio, bool lowerIsBetter)
        {
            int p = (int)Math.Round((ratio - 1f) * 100f);
            if (p == 0) return;
            bool better = lowerIsBetter ? ratio < 1f : ratio > 1f;
            list.Add(new KeyValuePair<string, ChipKind>(label + " " + (p > 0 ? "+" : "") + p + " %", better ? ChipKind.Good : ChipKind.Bad));
            ups.Add(better);
        }

        static void FlashPick(Il2Cpp.ModUI mu, int index)
        {
            if (!_built || mu == null || !Main.DeltaChips.Value) return;
            string name = OptionName(mu, index);
            var pd = Tables.Part(name);
            if (pd == null) return;
            _hoverKey = "";
            FillPartChips(mu, pd);
            ClearProjection();
            SetText(_stripKind, ClassLabel(pd) + "    FITTED NOW");
            _flashAnchor = mu.GetComponent<RectTransform>();
            _flashUntil = Time.unscaledTime + 3f;
        }

        static void ShowTip(int i)
        {
            if (_tip == null || _tiles == null || i < 0 || i >= N) return;
            if (_tipIdx != i)
            {
                _tipIdx = i;
                SetText(_tipTitle, LABELS[i]);
                SetText(_tipBody, _tiles[i].Tip);
                float bh = Mathf.Max(20f, _tipBody.preferredHeight + 4f);
                _tipBody.rectTransform.sizeDelta = new Vector2(TIP_W - 16f, bh);
                float h = 26f + bh + 8f;
                _tip.sizeDelta = new Vector2(TIP_W, h);
                _tipEdge.sizeDelta = new Vector2(TIP_W, h);
            }
            Rect r;
            if (Layout.CanvasRect(_tiles[i].InfoRt, out r))
            {
                float k = _c.Scale;
                float cw = Skin.CanvasWidth(), ch = Skin.CanvasHeight();
                float w = TIP_W * k, h = _tip.sizeDelta.y * k;
                float x = r.xMax - w, y = r.yMax + 8f + h;
                if (x < -cw * 0.5f + 10f) x = -cw * 0.5f + 10f;
                if (x + w > cw * 0.5f - 10f) x = cw * 0.5f - 10f - w;
                if (y > ch * 0.5f - 10f) y = ch * 0.5f - 10f;
                _tip.anchoredPosition = new Vector2(x, y);
            }
            if (!_tip.gameObject.activeSelf) _tip.gameObject.SetActive(true);
        }

        static void HideTip()
        {
            if (_tip != null && _tip.gameObject.activeSelf) _tip.gameObject.SetActive(false);
        }

        static void PlaceStrip(RectTransform anchor, Camera cam)
        {
            if (_strip == null) return;
            Vector2 local;
            if (!AnchorPoint(anchor, cam, true, out local)) { HideStrip(); return; }
            float k = _c.Scale;
            float cw = Skin.CanvasWidth(), ch = Skin.CanvasHeight();
            float w = STRIP_W * k, h = _strip.sizeDelta.y * k;
            float x = local.x + 10f, y = local.y;
            if (x + w > cw * 0.5f - 10f)
            {
                Vector2 tl;
                if (AnchorPoint(anchor, cam, false, out tl)) x = tl.x - w - 10f;
            }
            if (y - h < -ch * 0.5f + 10f) y = -ch * 0.5f + 10f + h;
            if (y > ch * 0.5f - 10f) y = ch * 0.5f - 10f;
            _strip.anchoredPosition = new Vector2(x, y);
            if (!_strip.gameObject.activeSelf) _strip.gameObject.SetActive(true);
        }

        static void HideStrip()
        {
            if (_strip != null && _strip.gameObject.activeSelf) _strip.gameObject.SetActive(false);
        }

        static bool AnchorPoint(RectTransform anchor, Camera cam, bool right, out Vector2 local)
        {
            local = Vector2.zero;
            Rect r;
            if (!Layout.CanvasRect(anchor, out r)) return false;
            local = new Vector2(right ? r.xMax : r.xMin, r.yMax);
            return true;
        }

        static string Kind(RoundDef rd)
        {
            var parts = new List<string>();
            if (rd.Has("ap")) parts.Add("ARMOUR PIERCING");
            if (rd.Has("hp")) parts.Add("HOLLOW POINT");
            if (rd.Has("match")) parts.Add("MATCH");
            if (rd.Has("ball")) parts.Add("BALL");
            if (rd.Has("shot")) parts.Add("SHOT");
            if (rd.Has("slug")) parts.Add("SLUG");
            if (rd.Has("explosive")) parts.Add("EXPLOSIVE");
            if (rd.Has("tracer")) parts.Add("TRACER");
            if (rd.Subsonic) parts.Add("SUBSONIC");
            return string.Join("  ·  ", parts.ToArray());
        }

        static string PlatesSentence(RoundDef rd)
        {
            var pass = new List<string>(); var roll = new List<string>();
            foreach (var k in PLATE_ORDER)
            {
                string v;
                if (!rd.Matrix.TryGetValue(k, out v)) continue;
                if (v == "PASS") pass.Add(k); else roll.Add(k);
            }
            if (rd.Has("explosive")) return "Blast weapon. Plates do not matter.";
            if (pass.Count == 0) return "Plates: stopped by every fresh plate. Only a worn plate lets it through.";
            if (roll.Count == 0) return "Plates: goes through every plate a man can wear.";
            return "Plates: goes through Level " + string.Join(", ", pass.ToArray()) + " every time. Needs a worn plate to get through Level " + string.Join(", ", roll.ToArray()) + ".";
        }

        static void ShowRoundCard(RoundDef rd, RectTransform anchor, Camera cam, bool full)
        {
            if (_card == null || rd == null) return;
            if (_cardRound != rd || _cardFull != full)
            {
                _cardRound = rd; _cardFull = full;
                FillRoundCard(rd, full);
            }
            if (anchor != null)
            {
                Vector2 local;
                if (AnchorPoint(anchor, cam, true, out local))
                {
                    float k = _c.Scale;
                    float cw = Skin.CanvasWidth(), ch = Skin.CanvasHeight();
                    float w = CARD_W * k, h = _card.sizeDelta.y * k;
                    float x = local.x + 10f, y = local.y;
                    if (x + w > cw * 0.5f - 10f)
                    {
                        Vector2 tl;
                        if (AnchorPoint(anchor, cam, false, out tl)) x = tl.x - w - 10f;
                    }
                    if (y - h < -ch * 0.5f + 10f) y = -ch * 0.5f + 10f + h;
                    if (y > ch * 0.5f - 10f) y = ch * 0.5f - 10f;
                    _card.anchoredPosition = new Vector2(x, y);
                }
            }
            if (!_card.gameObject.activeSelf) _card.gameObject.SetActive(true);
        }

        static void HideCard()
        {
            if (_card != null && _card.gameObject.activeSelf) _card.gameObject.SetActive(false);
            _cardRound = null;
        }

        static void FillRoundCard(RoundDef rd, bool full)
        {
            var w = CurrentWeapon();
            var sr = Handling.SeatedRound(w);
            bool rel = sr != null && sr != rd;
            SetText(_cardTitle, rd.Shell);
            SetText(_cardKind, Kind(rd) + "    " + rd.Cal + (sr == rd ? "    LOADED NOW" : (rel ? "    compared with " + sr.Shell : "")));
            SetText(_cardSummary, rd.Summary);

            float pel = Math.Max(1f, rd.Eff("pellets"));
            float dmg = rd.Eff("damage"), ap = rd.Eff("ap"), spd = rd.Eff("speed"), bleed = rd.Eff("bleed"), frac = rd.Eff("frac"), mis = rd.Misfire * 10f;
            _lines[0].Set(pel > 1f ? pel.ToString("0") + " x " + dmg.ToString("0") : dmg.ToString("0"), dmg * pel / 400f);
            _lines[1].Set("Level " + Model.PenClass(ap), (ap - 1f) / 4f);
            _lines[2].Set(spd.ToString("0") + " m/s" + (spd < 340f ? "  subsonic" : ""), (spd - 200f) / 900f);
            _lines[3].Set(bleed.ToString("0") + " %" + (rd.Eff("heavy") >= 20f ? "  heavy" : ""), bleed / 100f);
            _lines[4].Set(frac.ToString("0") + " %", frac / 100f);
            _lines[5].Set(mis.ToString("0.0") + " misfires per 1000", 1f - mis / 20f);
            if (rel)
            {
                float sp = Math.Max(1f, sr.Eff("pellets"));
                Delta(_lines[0], dmg * pel - sr.Eff("damage") * sp, "0", false, "");
                Delta(_lines[1], ap - sr.Eff("ap"), "0.00", false, "");
                Delta(_lines[2], spd - sr.Eff("speed"), "0", false, " m/s");
                Delta(_lines[3], bleed - sr.Eff("bleed"), "0", false, " %");
                Delta(_lines[4], frac - sr.Eff("frac"), "0", false, " %");
                Delta(_lines[5], mis - sr.Misfire * 10f, "0.0", true, "");
            }
            else for (int i = 0; i < _lines.Length; i++) _lines[i].HideDelta();

            SetText(_cardPlates, PlatesSentence(rd));
            float y = 214f + Mathf.Max(18f, _cardPlates.preferredHeight + 4f);
            _cardPlates.rectTransform.sizeDelta = new Vector2(CARD_W - 16f, Mathf.Max(18f, _cardPlates.preferredHeight + 4f));
            if (full)
            {
                SetText(_cardBody, rd.Body + (rd.Over.Count > 0 ? "  (Table edits: " + Edits(rd) + ".)" : ""));
                float bodyH = Mathf.Max(20f, _cardBody.preferredHeight + 4f);
                _cardBody.rectTransform.anchoredPosition = new Vector2(8f, -(y + 4f));
                _cardBody.rectTransform.sizeDelta = new Vector2(CARD_W - 16f, bodyH);
                y += 4f + bodyH + 4f;
                SetText(_cardBest, "Best for: " + rd.Best);
                float bestH = Mathf.Max(18f, _cardBest.preferredHeight + 4f);
                _cardBest.rectTransform.anchoredPosition = new Vector2(8f, -y);
                _cardBest.rectTransform.sizeDelta = new Vector2(CARD_W - 16f, bestH);
                y += bestH;
                _cardBody.gameObject.SetActive(true); _cardBest.gameObject.SetActive(true);
                SetText(_cardHint, "Escape or click away to close");
            }
            else
            {
                _cardBody.gameObject.SetActive(false); _cardBest.gameObject.SetActive(false);
                SetText(_cardHint, "click the round to read more");
            }
            _cardHint.rectTransform.anchoredPosition = new Vector2(8f, -(y + 2f));
            y += 20f;
            _card.sizeDelta = new Vector2(CARD_W, y);
            _cardEdge.sizeDelta = new Vector2(CARD_W, y);
        }

        static string Edits(RoundDef rd)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kv in rd.Over)
            {
                if (sb.Length > 0) sb.Append(", ");
                float stock; rd.Stock.TryGetValue(kv.Key, out stock);
                sb.Append(kv.Key).Append(' ').Append(stock.ToString("0.##")).Append(" to ").Append(kv.Value.ToString("0.##"));
            }
            return sb.ToString();
        }

        static void Delta(StatLine l, float d, string fmt, bool lowerIsBetter, string unit)
        {
            if (Math.Abs(d) < 0.0001f) { l.SetDelta("same", ChipKind.Neutral, true); return; }
            bool better = lowerIsBetter ? d < 0f : d > 0f;
            l.SetDelta((d > 0f ? "+" : "") + d.ToString(fmt) + unit, better ? ChipKind.Good : ChipKind.Bad, better);
        }
    }
}
