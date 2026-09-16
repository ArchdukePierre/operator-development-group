// ODG Operations - NewSlots
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Il2CppTMPro;
using OdgUi;

namespace NewSlots
{

    internal static class NativeUi
    {

        const float W        = 384f;
        const float PAD      = 14f;
        const float HEAD_H   = 38f;
        const float SEL_H    = 30f;
        const float WHERE_H  = 20f;
        const float LBL_H    = 18f;
        const float SLIDE_H  = 12f;
        const float ACT_H    = 30f;
        const float FOOT_H   = 20f;
        const float GAP      = 8f;
        const float IW       = W - PAD * 2f;
        const float EDGE_AREA = 52f;

        static readonly float FULL_H =
            PAD + HEAD_H + GAP + SEL_H + 4f + WHERE_H + GAP
            + (LBL_H + SLIDE_H + 8f) * 2f + 10f
            + LBL_H + 4f + EDGE_AREA + 8f + GAP
            + ACT_H + 6f + FOOT_H + PAD;
        static readonly float ROLLED_H = PAD + HEAD_H + PAD * 0.5f;

        static Chrome _c;
        static TextMeshProUGUI _tTitle, _tCount, _tName, _tWhere, _tSize, _tSizeV, _tHgt, _tHgtV, _tFoot;
        static Btn _bMin, _bPrev, _bNext, _bSizeDn, _bSizeUp, _bHgtDn, _bHgtUp, _bSave, _bReset;
        static OdgSlider _sSize, _sHgt;
        static RectTransform _bodyGroup;

        static TextMeshProUGUI _tEdges, _tCorners;
        static Btn _bFlat;
        static string _lastCorners;
        static int  _fails;
        static bool _giveUp;
        static string _lastName, _lastWhere, _lastSizeV, _lastHgtV, _lastFoot, _lastCount;

        internal static bool Active { get { return _c != null && _c.Root != null; } }
        internal static bool GaveUp { get { return _giveUp; } }

        const int STALL_FRAMES = 300;
        static int  _waitFrames;
        static bool _stalled;

        internal static bool Suppress { get { return !_giveUp && !_stalled; } }

        internal static void Tick()
        {
            try
            {
                if (!Ui.CabinetOpen || !Ui.StillOpenPublic()) { if (Active) Teardown(); return; }
                if (_giveUp) return;

                var tunable = Ui.TunablePublic();
                if (tunable.Count == 0) { if (Active) Teardown(); return; }

                if (!Active)
                {
                    if (!Build())
                    {
                        if (++_waitFrames > STALL_FRAMES && !_stalled)
                        {
                            _stalled = true;
                            NewSlotsMod.Log("[ui] the cabinet canvas never resolved after "
                                          + STALL_FRAMES + " frames; letting the IMGUI adjuster draw.");
                        }
                        return;
                    }
                    _waitFrames = 0; _stalled = false;
                }

                Ui.AdvanceTarget(tunable);
                Refresh(tunable);
                Interact(tunable);
            }
            catch (Exception e)
            {
                NewSlotsMod.Log("[ui] native panel: " + e.Message);
                Teardown();
                if (++_fails >= 3)
                {
                    _giveUp = true;
                    NewSlotsMod.Log("[ui] native panel failed three times; falling back to the IMGUI "
                                  + "adjuster for the rest of this session. Tuning is untouched.");
                }
            }
        }

        static bool Build()
        {
            GameObject canvasGo = null;
            try { canvasGo = Ui.Controller != null ? Ui.Controller.Canvas : null; } catch { }
            if (canvasGo == null) return false;

            var canvas = canvasGo.GetComponent<Canvas>();
            if (canvas == null) canvas = canvasGo.GetComponentInParent<Canvas>();
            if (canvas == null) canvas = canvasGo.GetComponentInChildren<Canvas>(true);
            if (canvas == null) { NewSlotsMod.Log("[ui] cabinet Canvas object has no Canvas"); return false; }

            Skin.Bind(canvas);
            BorrowFont(canvasGo);

            _c = new Chrome();

            if (!_c.Build(canvas, "ODG_PatchAdjuster", W, FULL_H,
                          NewSlotsMod.PanelPosX, NewSlotsMod.PanelPosY, NewSlotsMod.PanelUiScale,
                          Chrome.AUTO_REF_H, 20))
            { _c = null; return false; }

            var body = _c.Body;

            var headBg = Skin.Rect(body, "HeaderBg", 2f, 2f, W - 4f, PAD + HEAD_H - 4f);
            Skin.Img(headBg, Skin.Fill, Skin.Head, false);
            var rule = Skin.Rect(body, "HeaderRule", 2f, PAD + HEAD_H - 2f, W - 4f, 1f);
            Skin.Img(rule, null, Skin.RowEdge, false);

            float y = PAD;
            var head = Skin.Rect(body, "Title", PAD, y, IW - 120f, HEAD_H);
            _tTitle = Skin.Text_(head, "PATCH ADJUSTER", 17f, true, Skin.Text, TextAlignmentOptions.Left);
            _tTitle.characterSpacing = 6f;

            var cnt = Skin.Rect(body, "Count", PAD + IW - 118f, y, 78f, HEAD_H);
            _tCount = Skin.Text_(cnt, "", 13f, false, Skin.TextDim, TextAlignmentOptions.Right);

            _bMin = Btn.Make(body, "Min", PAD + IW - 30f, y + (HEAD_H - 26f) * 0.5f, 30f, 26f,
                             NewSlotsMod.NativeMinimised ? "+" : "–", 15f, 0f);

            _c.AddChrome(PAD + HEAD_H - 2f);
            if (_c.TitleGrab != null) _c.TitleGrab.sizeDelta = new Vector2(W - 44f, PAD + HEAD_H - 2f);

            _bodyGroup = Skin.Rect(body, "Body", 0f, 0f, W, FULL_H);
            y += HEAD_H + GAP;

            _bPrev = Btn.Make(_bodyGroup, "Prev", PAD, y, 30f, SEL_H, "<", 15f, 0f);
            _bNext = Btn.Make(_bodyGroup, "Next", PAD + IW - 30f, y, 30f, SEL_H, ">", 15f, 0f);
            var nameRt = Skin.Rect(_bodyGroup, "Name", PAD + 34f, y, IW - 68f, SEL_H);
            _tName = Skin.Text_(nameRt, "", 16f, true, Skin.Text, TextAlignmentOptions.Center);
            y += SEL_H + 4f;

            var whereRt = Skin.Rect(_bodyGroup, "Where", PAD, y, IW, WHERE_H);
            _tWhere = Skin.Text_(whereRt, "", 13f, false, Skin.TextDim, TextAlignmentOptions.Left);
            y += WHERE_H + GAP;

            y = Row(_bodyGroup, y, "SIZE", 0.5f, 2.5f, out _tSize, out _tSizeV, out _sSize, out _bSizeDn, out _bSizeUp);
            y += 10f;
            y = Row(_bodyGroup, y, "HEIGHT", -10f, 10f, out _tHgt, out _tHgtV, out _sHgt, out _bHgtDn, out _bHgtUp);
            y += 10f;

            var el = Skin.Rect(_bodyGroup, "EdgesL", PAD, y, IW, LBL_H);
            _tEdges = Skin.Text_(el, "CORNERS   drag a corner of the patch on your character to peel it", 13f, true, Skin.TextDim, TextAlignmentOptions.Left);
            _tEdges.characterSpacing = 2f;
            y += LBL_H + 4f;
            var cr = Skin.Rect(_bodyGroup, "CornersV", PAD, y + 6f, IW - 100f, LBL_H);
            _tCorners = Skin.Text_(cr, "", 12f, false, Skin.TextDim, TextAlignmentOptions.Left);
            _bFlat = Btn.Make(_bodyGroup, "Flatten", PAD + IW - 92f, y, 92f, ACT_H, "FLATTEN", 12f, 0f);
            y += EDGE_AREA + 8f + GAP;

            float bw = (IW - GAP) / 3f;
            _bSave  = Btn.Make(_bodyGroup, "Save",  PAD, y, bw * 2f, ACT_H, "SAVE");
            _bReset = Btn.Make(_bodyGroup, "Reset", PAD + bw * 2f + GAP, y, bw - 26f, ACT_H, "RESET");
            _bReset.Danger = true;
            y += ACT_H + 6f;

            var footRt = Skin.Rect(_bodyGroup, "Foot", PAD, y, IW - 30f, FOOT_H);
            _tFoot = Skin.Text_(footRt, "", 12f, false, Skin.TextDim, TextAlignmentOptions.Left);

            _bodyGroup.gameObject.SetActive(!NewSlotsMod.NativeMinimised);
            if (NewSlotsMod.NativeMinimised) { _c.GripOn = false; _c.SetHeight(ROLLED_H); }
            ClearCache();
            _fails = 0;

            NewSlotsMod.Log("[ui] native patch adjuster built on '" + canvasGo.name + "' (scale "
                          + _c.Scale.ToString("0.00") + ", font "
                          + (Skin.Font != null ? Skin.Font.name : "<builtin>")
                          + ", at x=" + _c.PosX.ToString("0") + " y=" + _c.PosY.ToString("0")
                          + ", size " + (W * _c.Scale).ToString("0") + "x" + (FULL_H * _c.Scale).ToString("0")
                          + " on a " + Skin.CanvasWidth().ToString("0") + "x" + Skin.CanvasHeight().ToString("0")
                          + " canvas, sort +20, rolled=" + NewSlotsMod.NativeMinimised + ")");
            return true;
        }

        static float Row(RectTransform parent, float y, string label, float min, float max,
                         out TextMeshProUGUI lbl, out TextMeshProUGUI val,
                         out OdgSlider sl, out Btn dn, out Btn up)
        {
            var l = Skin.Rect(parent, label + "L", PAD, y, IW - 90f, LBL_H);
            lbl = Skin.Text_(l, label, 13f, true, Skin.TextDim, TextAlignmentOptions.Left);
            lbl.characterSpacing = 3f;
            var v = Skin.Rect(parent, label + "V", PAD + IW - 90f, y, 90f, LBL_H);
            val = Skin.Text_(v, "", 13f, false, Skin.Text, TextAlignmentOptions.Right);
            y += LBL_H;

            sl = OdgSlider.Make(parent, label + "S", PAD, y + 4f, IW - 68f, SLIDE_H, min, max, min);
            dn = Btn.Make(parent, label + "-", PAD + IW - 62f, y, 28f, SLIDE_H + 8f, "-", 15f, 0f);
            up = Btn.Make(parent, label + "+", PAD + IW - 30f, y, 30f, SLIDE_H + 8f, "+", 15f, 0f);
            return y + SLIDE_H + 8f;
        }

        static void BorrowFont(GameObject canvasGo)
        {
            if (Skin.Font != null) return;
            TMP_Text src = null;
            try { src = Ui.Controller.HeadGear; } catch { }
            if (src == null) { try { src = Ui.Controller.Shirt; } catch { } }
            if (src == null)
            {
                try
                {
                    var all = canvasGo.GetComponentsInChildren<TextMeshProUGUI>(true);
                    for (int i = 0; all != null && i < all.Length; i++)
                        if (all[i] != null && all[i].font != null) { src = all[i]; break; }
                }
                catch { }
            }
            Skin.BorrowFont(src);
        }

        static void Refresh(List<Def> tunable)
        {
            var t = Ui.Target;
            int idx = Mathf.Max(0, tunable.IndexOf(t));

            string count = (idx + 1) + " / " + tunable.Count;
            if (_lastCount != count) { _tCount.text = count; _lastCount = count; }

            _bMin.SetLabel(NewSlotsMod.NativeMinimised ? "+" : "–");
            _bMin.Paint();
            if (NewSlotsMod.NativeMinimised) return;

            string patchName = t == null ? "-" : (t.Display ?? t.ModName);
            if (_lastName != patchName) { _tName.text = patchName; _lastName = patchName; }

            string garment, socket;
            Hooks.SocketIdentity(Ui.CurrentSocket, out garment, out socket);
            bool live = Ui.CurrentSocket != null && garment != null;
            string where = live ? garment + "  >  " + socket : "open a patch slot to place it";
            if (_lastWhere != where)
            {
                _tWhere.text = where;
                _tWhere.color = live ? Skin.TextDim : Skin.TextOff;
                _lastWhere = where;
            }

            var tune = Tuning.Get(garment, socket, t.ModName);
            if (!_sSize.Dragging) _sSize.Set(tune.Scale);
            if (!_sHgt.Dragging)  _sHgt.Set(tune.OutMm);

            string sv = Mathf.RoundToInt(tune.Scale * 100f) + " %";
            if (_lastSizeV != sv) { _tSizeV.text = sv; _lastSizeV = sv; }
            string hv = tune.OutMm.ToString("0.0") + " mm";
            if (_lastHgtV != hv) { _tHgtV.text = hv; _lastHgtV = hv; }

            string cv = "TL " + Fmt(tune.CTL) + "   TR " + Fmt(tune.CTR) + "   BR " + Fmt(tune.CBR) + "   BL " + Fmt(tune.CBL) + "  mm";
            if (_lastCorners != cv) { _tCorners.text = cv; _lastCorners = cv; }
            _bFlat.On = tune.AnyCorner;
            _bFlat.Paint();

            _bSave.SetLabel(Ui.DirtyPublic ? "SAVE *" : "SAVE");
            _bPrev.On = _bNext.On = tunable.Count > 1;

            string foot = Ui.DirtyPublic ? "unsaved - SAVE writes it into the pack for everyone"
                                         : "saved in the pack, same for every player";
            if (Seat.Count == 0) foot = "no worn patch to move - equip one to adjust it";
            if (Tuning.RemoteActive) foot = "seating shared with " + Tuning.PeerCount + " teammate(s) - your changes send when you SAVE";
            if (_lastFoot != foot) { _tFoot.text = foot; _lastFoot = foot; }

            _bPrev.Paint(); _bNext.Paint(); _bSizeDn.Paint(); _bSizeUp.Paint();
            _bHgtDn.Paint(); _bHgtUp.Paint(); _bSave.Paint(); _bReset.Paint();
        }

        static void Interact(List<Def> tunable)
        {
            Vector2 mp = Input.mousePosition;
            bool down = Input.GetMouseButtonDown(0);

            if (_c.Drag(mp, down))
            {
                if (_c.Dirty && !Input.GetMouseButton(0))
                { NewSlotsMod.SaveNativePlacement(_c.PosX, _c.PosY, _c.Scale); _c.Dirty = false; }
                return;
            }

            if (_bMin.Poll(mp, down, false))
            {

                NewSlotsMod.NativeMinimised = !NewSlotsMod.NativeMinimised;
                bool rolled = NewSlotsMod.NativeMinimised;
                _bodyGroup.gameObject.SetActive(!rolled);
                _c.GripOn = !rolled;
                _c.SetHeight(rolled ? ROLLED_H : FULL_H);
                NewSlotsMod.SaveNativePlacement(_c.PosX, _c.PosY, _c.Scale);
                return;
            }
            if (NewSlotsMod.NativeMinimised) return;

            var t = Ui.Target;
            if (t == null) return;
            int idx = Mathf.Max(0, tunable.IndexOf(t));

            string garment, socket;
            Hooks.SocketIdentity(Ui.CurrentSocket, out garment, out socket);
            var tune = Tuning.Get(garment, socket, t.ModName);
            float scale = tune.Scale, outMm = tune.OutMm;

            bool held = _sSize.Poll(mp, down, false);
            held |= _sHgt.Poll(mp, down, held);

            float ns = _sSize.Value, no = _sHgt.Value;

            if (!held && _bFlat.Poll(mp, down, false))
            {
                if (tune.AnyCorner) { Tuning.SetCorners(garment, socket, t.ModName, 0f, 0f, 0f, 0f); Ui.DirtyPublic = true; }
                return;
            }

            if (!held)
            {
                if (_bPrev.Poll(mp, down, false)) { Ui.SelectManual(tunable[(idx - 1 + tunable.Count) % tunable.Count]); return; }
                if (_bNext.Poll(mp, down, false)) { Ui.SelectManual(tunable[(idx + 1) % tunable.Count]); return; }
                if (_bSizeDn.Poll(mp, down, false)) ns = scale - 0.05f;
                if (_bSizeUp.Poll(mp, down, false)) ns = scale + 0.05f;
                if (_bHgtDn.Poll(mp, down, false))  no = outMm - 0.2f;
                if (_bHgtUp.Poll(mp, down, false))  no = outMm + 0.2f;

                if (_bSave.Poll(mp, down, false)) { if (Tuning.Save()) Ui.DirtyPublic = false; return; }
                if (_bReset.Poll(mp, down, false))
                {
                    ns = 1f; no = 0f;
                    if (tune.AnyCorner) { Tuning.SetCorners(garment, socket, t.ModName, 0f, 0f, 0f, 0f); Ui.DirtyPublic = true; }
                }
            }

            ns = Mathf.Clamp(Mathf.Round(ns * 100f) / 100f, 0.5f, 2.5f);
            no = Mathf.Clamp(Mathf.Round(no * 10f) / 10f, -10f, 10f);
            if (Mathf.Abs(ns - scale) > 0.0001f || Mathf.Abs(no - outMm) > 0.0001f)
            {

                Tuning.Set(garment, socket, t.ModName, ns, no);
                t.PerScale = ns; t.PerOutMm = no;
                Ui.DirtyPublic = true;
                _sSize.Set(ns); _sHgt.Set(no);
            }
        }

        internal static bool PointerOver(Vector2 mp)
        {
            try { return Active && _c != null && _c.Rt != null && Skin.In(_c.Rt, mp); } catch { return false; }
        }

        static void ClearCache()
        {
            _lastName = _lastWhere = _lastSizeV = _lastHgtV = _lastFoot = _lastCount = _lastCorners = null;
        }

        static string Fmt(float v) { return (v >= 0f ? "+" : "") + v.ToString("0.0"); }

        internal static void Teardown()
        {
            try { if (_c != null) _c.Destroy(); } catch { }
            _c = null; _bodyGroup = null;
            _tTitle = _tCount = _tName = _tWhere = _tSize = _tSizeV = _tHgt = _tHgtV = _tFoot = null;
            _bMin = _bPrev = _bNext = _bSizeDn = _bSizeUp = _bHgtDn = _bHgtUp = _bSave = _bReset = null;
            _sSize = _sHgt = null;
            _tEdges = null; _tCorners = null; _bFlat = null;
            ClearCache();
            Skin.Drop();
            _waitFrames = 0; _stalled = false;
        }
    }
}
