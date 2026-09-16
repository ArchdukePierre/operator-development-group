// ODG Operations - FireteamRoles
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FireteamRoles
{

    internal static class MedIcons
    {
        const int MaxSide = 192;

        static readonly Dictionary<int, Texture2D> _owned = new Dictionary<int, Texture2D>();
        static readonly HashSet<int> _failed = new HashSet<int>();
        static float _nextScan;

        internal static bool Ready { get { return _owned.Count > 0; } }

        internal static void Forget()
        {
            foreach (var kv in _owned)
            {
                try { if (kv.Value != null) UnityEngine.Object.Destroy(kv.Value); }
                catch { }
            }
            _owned.Clear();
            _failed.Clear();
            _nextScan = 0f;
        }

        internal static void Scan(Il2Cpp.MedicalSystem med)
        {
            if (med == null) return;
            if (!Cfg.TreatShowIcons.Value) return;
            if (Time.realtimeSinceStartup < _nextScan) return;
            _nextScan = Time.realtimeSinceStartup + 3f;

            try
            {
                var slots = med.inventorySlots;
                if (slots == null) return;
                for (int i = 0; i < slots.Count; i++)
                {
                    var s = slots[i];
                    if (s == null) continue;
                    Il2Cpp.InventoryItem it = null;
                    try { it = s.currentItem; } catch { }
                    if (it == null) continue;

                    bool epi = Epi.IsEpi(it);
                    int key = epi ? -1 : IndexOfType(it);
                    if (key == -2) continue;
                    if (_owned.ContainsKey(key) || _failed.Contains(key)) continue;

                    var copy = CopyIcon(it, key);
                    if (copy != null) _owned[key] = copy;
                    else _failed.Add(key);
                }
            }
            catch (Exception e) { Log.Detail("icons: scan failed: " + e.Message); }
        }

        static int IndexOfType(Il2Cpp.InventoryItem it)
        {
            try
            {
                var t = it.itemType;
                for (int k = 0; k < Medical.AllTypes.Length; k++)
                    if (Medical.AllTypes[k] == t) return k;
            }
            catch { }
            return -2;
        }

        static Texture2D CopyIcon(Il2Cpp.InventoryItem it, int key)
        {
            try
            {
                var imgs = it.gameObject.GetComponentsInChildren<Image>(true);
                if (imgs == null) return null;

                for (int i = 0; i < imgs.Length; i++)
                {
                    var img = imgs[i];
                    if (img == null) continue;
                    if (UnderTooltip(img.transform)) continue;

                    Sprite sp = null;
                    try { sp = img.sprite; } catch { }
                    if (sp == null) continue;

                    Texture2D src = null;
                    try { src = sp.texture; } catch { }
                    if (src == null) continue;

                    Rect r;
                    try { r = sp.textureRect; } catch { continue; }
                    if (r.width < 2f || r.height < 2f) continue;

                    int tw = src.width, th = src.height;
                    if (tw < 2 || th < 2) continue;

                    int w = Mathf.Clamp(Mathf.RoundToInt(r.width), 2, MaxSide);
                    int h = Mathf.Clamp(Mathf.RoundToInt(r.height), 2, MaxSide);

                    var scale = new Vector2(r.width / tw, r.height / th);
                    var offset = new Vector2(r.x / tw, r.y / th);

                    RenderTexture rt = null;
                    RenderTexture prev = null;
                    Texture2D outp = null;
                    try
                    {
                        rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
                        Graphics.Blit(src, rt, scale, offset);

                        prev = RenderTexture.active;
                        RenderTexture.active = rt;

                        outp = new Texture2D(w, h, TextureFormat.RGBA32, false);
                        outp.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                        outp.Apply(false, false);
                        outp.name = "ODG_medicon_" + key;
                        outp.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    }
                    finally
                    {
                        try { RenderTexture.active = prev; } catch { }
                        try { if (rt != null) RenderTexture.ReleaseTemporary(rt); } catch { }
                    }

                    if (outp != null)
                        Log.Detail("icons: copied " + (key == -1 ? "epi" : Medical.Name(Medical.AllTypes[key])) +
                                   " as " + w + "x" + h);
                    return outp;
                }
            }
            catch (Exception e) { Log.Detail("icons: copy failed for key " + key + ": " + e.Message); }
            return null;
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

        internal static bool Draw(Rect where, int typeIndex, bool isEpi)
        {
            if (!Cfg.TreatShowIcons.Value) return false;

            Texture2D tex;
            if (!_owned.TryGetValue(isEpi ? -1 : typeIndex, out tex)) return false;
            if (tex == null) return false;

            bool ok = false;
            Err.Risky("icons.firstdraw", () =>
            {
                GUI.DrawTexture(where, tex, ScaleMode.ScaleToFit, true);
                ok = true;
            });
            return ok;
        }
    }
}
