// ODG Operations - FireteamRoles
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UI;

namespace FireteamRoles
{

    internal static class Epi
    {
        internal const string ItemName = "Epinephrine";

        static readonly HashSet<IntPtr> _marked = new HashSet<IntPtr>();
        static Il2Cpp.InventoryItem _mine;
        static GameObject _slotGo;
        static Vector2 _basePos;
        static Vector2 _appliedOffset;
        static Sprite _icon;

        internal static string ContentDir
        {
            get { return Path.Combine(Log.GameRoot, "Mods", "FireteamRoles_Content"); }
        }

        internal static bool IsEpi(Il2Cpp.InventoryItem it)
        {
            if (it == null) return false;
            try
            {
                if (_marked.Contains(it.Pointer)) return true;
                return it.ItemName == ItemName;
            }
            catch { return false; }
        }

        internal static int Count
        {
            get { try { return _mine != null ? _mine.currentUses : 0; } catch { return 0; } }
        }

        internal static void Sync(Il2Cpp.MedicalSystem med)
        {
            if (med == null) return;
            bool want = State.MyRole == Role.Medic && State.Enabled;

            if (!want) { Remove(); return; }
            if (_mine != null && Alive(_mine)) { Reposition(); return; }

            Build(med);
        }

        static void Reposition()
        {
            if (_slotGo == null) return;
            Vector2 want;
            try { want = new Vector2(Cfg.EpiSlotOffsetX.Value, Cfg.EpiSlotOffsetY.Value); }
            catch { return; }
            if (want == _appliedOffset) return;
            try
            {
                var rt = _slotGo.GetComponent<RectTransform>();
                if (rt == null) return;
                rt.anchoredPosition = _basePos + want;
                _appliedOffset = want;
                Log.Msg("epi: slot moved to " + rt.anchoredPosition + " (offset " + want + ")");
            }
            catch { }
        }

        static bool Alive(Il2Cpp.InventoryItem it)
        {
            try { return it != null && it.gameObject != null; }
            catch { return false; }
        }

        static void Build(Il2Cpp.MedicalSystem med)
        {
            try
            {
                var items = med.inventoryItems;
                if (items == null || items.Count == 0)
                {
                    Log.Detail("epi: medical inventory not populated yet");
                    return;
                }

                Il2Cpp.InventoryItem donor = null;
                for (int i = 0; i < items.Count; i++)
                {
                    var it = items[i];
                    if (it == null || IsEpi(it)) continue;
                    if (donor == null) donor = it;
                    try
                    {
                        if (it.itemType == Il2Cpp.InventoryItem.ItemType.Morphine ||
                            it.itemType == Il2Cpp.InventoryItem.ItemType.HemostaticInjector)
                        { donor = it; break; }
                    }
                    catch { }
                }
                if (donor == null) { Log.Warn("epi: no donor item"); return; }

                var donorSlot = donor.inventorySlot;
                if (donorSlot == null)
                {
                    try
                    {
                        var slots = med.inventorySlots;
                        if (slots != null)
                        {
                            for (int i = 0; i < slots.Count; i++)
                            {
                                var s = slots[i];
                                if (s == null) continue;
                                Il2Cpp.InventoryItem cur = null;
                                try { cur = s.currentItem; } catch { }
                                if (cur == null || IsEpi(cur)) continue;
                                donorSlot = s;
                                donor = cur;
                                try
                                {
                                    if (cur.itemType == Il2Cpp.InventoryItem.ItemType.Morphine ||
                                        cur.itemType == Il2Cpp.InventoryItem.ItemType.HemostaticInjector) break;
                                }
                                catch { }
                            }
                        }
                    }
                    catch (Exception e) { Log.Detail("epi: slot-side donor search failed: " + e.Message); }
                }

                if (donorSlot == null)
                {
                    try
                    {
                        var slots = med.inventorySlots;
                        if (slots != null && slots.Count > 0) donorSlot = slots[0];
                    }
                    catch { }
                }
                if (donorSlot == null) { Log.Warn("epi: no slot anywhere to clone, vial skipped this pass"); return; }

                var parent = donorSlot.transform.parent;
                var cloneObj = UnityEngine.Object.Instantiate(donorSlot.gameObject, parent);
                var clone = cloneObj != null ? cloneObj.TryCast<GameObject>() : null;
                if (clone == null) { Log.Warn("epi: slot clone failed"); return; }

                clone.name = "ODG_EpiSlot";
                var slot = clone.GetComponent<Il2Cpp.InventorySlot>();
                var item = clone.GetComponentInChildren<Il2Cpp.InventoryItem>(true);
                if (slot == null || item == null)
                {
                    Log.Warn("epi: clone missing slot or item component");
                    UnityEngine.Object.Destroy(clone);
                    return;
                }

                item.gameObject.name = "ODG_EpiItem";
                item.ItemName = ItemName;
                item.maxUses = Mathf.Max(1, Cfg.EpiVials.Value);
                item.currentUses = item.maxUses;
                try { item.UseTime = Mathf.Max(0.5f, donor.UseTime); } catch { }

                try { item.itemType = Il2Cpp.InventoryItem.ItemType.Morphine; } catch { }

                _marked.Add(item.Pointer);

                try { item.Init(slot); } catch (Exception e) { Log.Detail("epi: Init threw: " + e.Message); }
                try { slot.currentItem = item; } catch { }

                Icon(item);
                Place(clone, donorSlot, med);

                try { med.inventoryItems.Add(item); } catch (Exception e) { Log.Warn("epi: inventoryItems.Add failed: " + e.Message); }
                try { if (med.inventorySlots != null) med.inventorySlots.Add(slot); } catch { }

                _mine = item;
                _slotGo = clone;
                Log.Msg("epinephrine added to the medic kit: " + item.maxUses + " vials");
                Log.Fired("epi-build");
            }
            catch (Exception e) { Log.Err("epi build failed: " + e.Message); }
        }

        static void Place(GameObject clone, Il2Cpp.InventorySlot donorSlot, Il2Cpp.MedicalSystem med)
        {
            try
            {
                var parent = clone.transform.parent;
                try { if (parent != null && parent.GetComponent<LayoutGroup>() != null) return; }
                catch { }

                var rt = clone.GetComponent<RectTransform>();
                if (rt == null) return;

                var taken = new List<Vector2>();
                var cols = new List<float>();
                var rows = new List<float>();

                var slots = med.inventorySlots;
                if (slots != null)
                {
                    for (int i = 0; i < slots.Count; i++)
                    {
                        var s = slots[i];
                        if (s == null) continue;
                        RectTransform srt = null;
                        try { srt = s.GetComponent<RectTransform>(); } catch { }
                        if (srt == null || srt.Pointer == rt.Pointer) continue;
                        var ap = srt.anchoredPosition;
                        taken.Add(ap);
                        if (!Near(cols, ap.x)) cols.Add(ap.x);
                        if (!Near(rows, ap.y)) rows.Add(ap.y);
                    }
                }

                if (cols.Count == 0 || rows.Count == 0)
                {
                    var drt = donorSlot.GetComponent<RectTransform>();
                    if (drt != null) rt.anchoredPosition = drt.anchoredPosition + new Vector2(0f, -(drt.rect.height + 8f));
                    Log.Detail("epi: no grid readable, offset from donor to " + rt.anchoredPosition);
                    return;
                }

                cols.Sort();
                rows.Sort();
                rows.Reverse();

                for (int r = 0; r < rows.Count; r++)
                    for (int c = 0; c < cols.Count; c++)
                    {
                        var cell = new Vector2(cols[c], rows[r]);
                        if (Occupied(taken, cell)) continue;
                        rt.anchoredPosition = Nudge(cell);
                        Log.Msg("epi: placed in free grid cell " + rt.anchoredPosition);
                        return;
                    }

                float pitch = rows.Count >= 2 ? Mathf.Abs(rows[0] - rows[1]) : rt.rect.height + 8f;
                if (pitch < 1f) pitch = rt.rect.height + 8f;
                rt.anchoredPosition = Nudge(new Vector2(cols[0], rows[rows.Count - 1] - pitch));
                Log.Msg("epi: grid full, started a new row at " + rt.anchoredPosition);
            }
            catch (Exception e) { Log.Detail("epi place failed: " + e.Message); }
        }

        static Vector2 Nudge(Vector2 p)
        {
            _basePos = p;
            try
            {
                _appliedOffset = new Vector2(Cfg.EpiSlotOffsetX.Value, Cfg.EpiSlotOffsetY.Value);
                return p + _appliedOffset;
            }
            catch { _appliedOffset = Vector2.zero; return p; }
        }

        static bool Near(List<float> list, float v)
        {
            for (int i = 0; i < list.Count; i++) if (Mathf.Abs(list[i] - v) < 4f) return true;
            return false;
        }

        static bool Occupied(List<Vector2> taken, Vector2 cell)
        {
            for (int i = 0; i < taken.Count; i++)
                if (Mathf.Abs(taken[i].x - cell.x) < 4f && Mathf.Abs(taken[i].y - cell.y) < 4f) return true;
            return false;
        }

        internal static void Forget()
        {
            _mine = null;
            _slotGo = null;
            _marked.Clear();
        }

        internal static void Remove()
        {
            if (_slotGo == null && _mine == null) return;
            try
            {
                var med = Medical.Local();
                if (med != null && _mine != null && med.inventoryItems != null)
                {
                    try { med.inventoryItems.Remove(_mine); } catch { }
                }
            }
            catch { }
            try { if (_slotGo != null) UnityEngine.Object.Destroy(_slotGo); } catch { }
            _slotGo = null;
            _mine = null;
        }

        internal static void Refund()
        {
            try { if (_mine != null) _mine.currentUses = Mathf.Min(_mine.maxUses, _mine.currentUses + 1); }
            catch { }
        }

        internal static bool Spend()
        {
            try
            {
                if (_mine == null || _mine.currentUses <= 0) return false;
                _mine.currentUses = _mine.currentUses - 1;
                return true;
            }
            catch { return false; }
        }

        static float _boostUntil;
        static float _peak;
        static Il2Cpp.MedicalSystem _boostOn;

        internal static bool Boosting
        {
            get { return _boostOn != null && Time.realtimeSinceStartup < _boostUntil; }
        }

        internal static void Apply(Il2Cpp.MedicalSystem med)
        {
            if (med == null) return;
            try
            {
                float now = med.HeartRate;
                float ceiling = med.maxBpm > 0f ? med.maxBpm : 180f;
                _peak = Mathf.Min(ceiling, Mathf.Max(now, 60f) + Mathf.Max(5f, Cfg.EpiHeartRateBoost.Value));
                _boostOn = med;
                _boostUntil = Time.realtimeSinceStartup + Mathf.Max(5f, Cfg.EpiBoostSeconds.Value);

                Raise(med, _peak);
                Log.Msg("epinephrine: pulse " + now.ToString("0") + " -> " + _peak.ToString("0"));
                Ui.Toast("Epinephrine administered");
                Log.Fired("epi-effect");
            }
            catch (Exception e) { Err.Note("epi.apply", e); }
        }

        internal static void Tick()
        {
            if (_boostOn == null) return;
            if (Time.realtimeSinceStartup >= _boostUntil) { _boostOn = null; return; }

            try
            {
                float left = Mathf.Clamp01((_boostUntil - Time.realtimeSinceStartup) /
                                           Mathf.Max(1f, Cfg.EpiBoostSeconds.Value));
                Raise(_boostOn, Mathf.Lerp(0f, _peak, left));
            }
            catch { _boostOn = null; }
        }

        static void Raise(Il2Cpp.MedicalSystem med, float want)
        {
            try { if (med.targetHeartRate < want) med.targetHeartRate = want; }
            catch { }
        }

        static void Icon(Il2Cpp.InventoryItem item)
        {
            try
            {
                if (_icon == null) _icon = BuildIcon();
                if (_icon == null) return;

                var imgs = item.gameObject.GetComponentsInChildren<Image>(true);
                if (imgs == null || imgs.Length == 0) { Log.Detail("epi: no Image to skin"); return; }

                var names = new System.Text.StringBuilder();
                for (int i = 0; i < imgs.Length; i++)
                {
                    if (i > 0) names.Append(", ");
                    names.Append(imgs[i].gameObject.name);
                }
                Log.Msg("epi: images on the clone: " + names);

                Image target = null;
                for (int i = 0; i < imgs.Length; i++)
                {
                    try { if (imgs[i].gameObject.Pointer == item.gameObject.Pointer) { target = imgs[i]; break; } }
                    catch { }
                }
                if (target == null)
                {
                    for (int i = 0; i < imgs.Length; i++)
                    {
                        if (UnderTooltip(imgs[i].transform)) continue;
                        target = imgs[i];
                        break;
                    }
                }
                if (target == null) target = imgs[0];

                target.sprite = _icon;
                target.color = Color.white;
                Log.Msg("epi: icon applied to '" + target.gameObject.name + "'");

                for (int i = 0; i < imgs.Length; i++)
                {
                    if (imgs[i] == target) continue;
                    var n = imgs[i].gameObject.name.ToLowerInvariant();
                    if (!n.Contains("icon")) continue;
                    try { imgs[i].sprite = _icon; imgs[i].color = Color.white; }
                    catch { }
                }
            }
            catch (Exception e) { Log.Detail("epi icon failed: " + e.Message); }
        }

        static bool UnderTooltip(Transform t)
        {
            try
            {
                for (var p = t; p != null; p = p.parent)
                    if (p.gameObject.name.ToLowerInvariant().Contains("tooltip")) return true;
            }
            catch { }
            return false;
        }

        static Sprite BuildIcon()
        {
            Texture2D tex = LoadOverride();
            if (tex == null) tex = DrawVial();
            if (tex == null) return null;
            try
            {
                var s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                s.name = "ODG_epi_icon";
                s.hideFlags = HideFlags.DontUnloadUnusedAsset;
                return s;
            }
            catch { return null; }
        }

        static byte[] ReadEmbedded(string logicalName)
        {
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                using (var s = asm.GetManifestResourceStream(logicalName))
                {
                    if (s == null) return null;
                    using (var ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        return ms.ToArray();
                    }
                }
            }
            catch { return null; }
        }

        static Texture2D LoadOverride()
        {
            try
            {
                var bytes = ReadEmbedded("epi.png");
                if (bytes == null) { Log.Msg("epi: embedded icon missing, drawing one instead"); return null; }

                var raw = new Il2CppStructArray<byte>(bytes.Length);
                for (int i = 0; i < bytes.Length; i++) raw[i] = bytes[i];

                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                t.name = "ODG_epi_png";
                if (!ImageConversion.LoadImage(t, raw)) { Log.Warn("epi: embedded icon failed to decode"); return null; }

                if (Cfg.EpiIconTrimBackground.Value) Trim(t);

                t.wrapMode = TextureWrapMode.Clamp;
                t.hideFlags = HideFlags.DontUnloadUnusedAsset;
                Log.Msg("epi: icon loaded (embedded)  " + t.width + "x" + t.height);
                return t;
            }
            catch (Exception e) { Log.Detail("epi image load failed: " + e.Message); return null; }
        }

        static void Trim(Texture2D t)
        {
            try
            {
                int w = t.width, h = t.height;
                if (w < 4 || h < 4 || (long)w * h > 4_000_000L) return;

                var src = t.GetPixels32();
                int n = w * h;
                var px = new Color32[n];
                for (int i = 0; i < n; i++) px[i] = src[i];

                int border = 0, clear = 0;
                for (int x = 0; x < w; x++)
                {
                    if (px[x].a < 16) clear++;
                    if (px[(h - 1) * w + x].a < 16) clear++;
                    border += 2;
                }
                for (int y = 0; y < h; y++)
                {
                    if (px[y * w].a < 16) clear++;
                    if (px[y * w + w - 1].a < 16) clear++;
                    border += 2;
                }
                if (border > 0 && clear * 2 > border)
                {
                    Log.Msg("epi: image already has a cut-out background, trim skipped");
                    return;
                }

                var seen = new bool[n];
                var queue = new int[n];
                int head = 0, tail = 0;

                for (int x = 0; x < w; x++)
                {
                    Seed(px, seen, queue, ref tail, x);
                    Seed(px, seen, queue, ref tail, (h - 1) * w + x);
                }
                for (int y = 0; y < h; y++)
                {
                    Seed(px, seen, queue, ref tail, y * w);
                    Seed(px, seen, queue, ref tail, y * w + w - 1);
                }

                int cleared = 0;
                while (head < tail)
                {
                    int idx = queue[head++];
                    px[idx] = new Color32(0, 0, 0, 0);
                    cleared++;

                    int x = idx % w, y = idx / w;
                    if (x > 0) Visit(px, seen, queue, ref tail, idx - 1);
                    if (x < w - 1) Visit(px, seen, queue, ref tail, idx + 1);
                    if (y > 0) Visit(px, seen, queue, ref tail, idx - w);
                    if (y < h - 1) Visit(px, seen, queue, ref tail, idx + w);
                }

                var outp = new Il2CppStructArray<Color32>(n);
                for (int i = 0; i < n; i++) outp[i] = px[i];
                t.SetPixels32(outp);
                t.Apply(false, false);
                Log.Msg("epi: background trimmed, " + cleared + " of " + n + " pixels cleared");
            }
            catch (Exception e) { Log.Detail("epi trim failed: " + e.Message); }
        }

        static bool IsBackdrop(Color32 c)
        {
            if (c.a < 16) return true;
            return c.r > 232 && c.g > 232 && c.b > 232;
        }

        static void Seed(Color32[] px, bool[] seen, int[] q, ref int tail, int idx)
        {
            if (seen[idx] || !IsBackdrop(px[idx])) return;
            seen[idx] = true;
            q[tail++] = idx;
        }

        static void Visit(Color32[] px, bool[] seen, int[] q, ref int tail, int idx)
        {
            if (seen[idx] || !IsBackdrop(px[idx])) return;
            seen[idx] = true;
            q[tail++] = idx;
        }

        static Texture2D DrawVial()
        {
            const int N = 128;
            try
            {
                var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
                tex.name = "ODG_epi_gen";
                var px = new Il2CppStructArray<Color32>(N * N);

                var clear = new Color32(0, 0, 0, 0);
                var glass = new Color32(214, 222, 228, 235);
                var glassHi = new Color32(245, 250, 252, 245);
                var glassLo = new Color32(168, 180, 190, 235);
                var cap = new Color32(122, 76, 174, 255);
                var capHi = new Color32(160, 112, 210, 255);
                var crimp = new Color32(196, 199, 203, 255);
                var label = new Color32(246, 245, 241, 255);
                var ink = new Color32(52, 54, 60, 255);
                var accent = new Color32(122, 76, 174, 255);

                int bodyL = 40, bodyR = 88;
                int capT = 8, capB = 30;
                int crimpB = 40;
                int bodyB = 118;
                int labT = 62, labB = 104;

                for (int y = 0; y < N; y++)
                {
                    for (int x = 0; x < N; x++)
                    {
                        Color32 c = clear;
                        bool inX = x >= bodyL && x < bodyR;

                        if (inX && y >= capT && y < capB)
                        {
                            c = (x < bodyL + 10) ? capHi : cap;
                        }
                        else if (inX && y >= capB && y < crimpB)
                        {
                            c = crimp;
                            if ((y & 1) == 0 && x > bodyL + 2 && x < bodyR - 2) c = new Color32(172, 176, 181, 255);
                        }
                        else if (inX && y >= crimpB && y < bodyB)
                        {
                            int edge = 4;
                            if (x < bodyL + edge) c = glassLo;
                            else if (x > bodyR - edge) c = glassLo;
                            else if (x < bodyL + edge + 6) c = glassHi;
                            else c = glass;

                            if (y >= labT && y < labB && x > bodyL + 2 && x < bodyR - 2)
                            {
                                c = label;

                                if (y == labT + 6 && x > bodyL + 7 && x < bodyR - 7) c = ink;
                                if (y == labT + 16 && x > bodyL + 7 && x < bodyR - 12) c = ink;
                                if (y == labT + 21 && x > bodyL + 7 && x < bodyR - 16) c = ink;
                                if (y >= labT + 28 && y <= labT + 31 && x > bodyL + 7 && x < bodyR - 7) c = accent;
                            }

                            if (y >= bodyB - 4 && (x < bodyL + 3 || x > bodyR - 4)) c = clear;
                        }

                        px[y * N + x] = c;
                    }
                }

                tex.SetPixels32(px);
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                tex.Apply(false, false);
                tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                return tex;
            }
            catch (Exception e) { Log.Detail("epi draw failed: " + e.Message); return null; }
        }
    }
}
