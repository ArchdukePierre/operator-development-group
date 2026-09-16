// ODG Operations - HSR
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace HSR
{

    internal static class MagBox
    {

        internal const string DonorMagName = "338_R700_Mag";
        const int ScanEvery = 10;

        static int _magId = -1;
        static Sprite _donorSprite;
        static bool _resolved, _listed, _reported;
        static int _listFixes, _tick;

        static Il2Cpp.ModUI _ourUi;
        static Transform _canvasRoot;

        static readonly List<Image> _painted = new List<Image>();

        static int _ourModding;

        internal static void ResetForScene()
        {
            _painted.Clear();
            _ourUi = null; _canvasRoot = null; _ourModding = 0;
            _listed = false; _reported = false;

        }

        static readonly HashSet<string> _once = new HashSet<string>();
        static void Once(MelonLogger.Instance log, string msg)
        { if (_once.Add(msg)) log.Msg(msg); }

        static string NameOf(int id)
        {
            try
            {
                var lm = Il2Cpp.LoadoutManager.singleton;
                var infos = lm == null ? null : lm.WeaponModInfo;
                if (infos == null || id < 0 || id >= infos.Length) return id < 0 ? "None" : "?";
                var inf = infos[id]; if (inf == null) return "?";
                return inf.Name ?? "?";
            }
            catch { return "?"; }
        }

        static bool IsMagBox(Il2Cpp.ModUI ui)
        {
            string label; try { label = ui.attachmentTypeName; } catch { return false; }
            return BenchList.Norm(label) == "MAGAZINE";
        }

        static void Resolve(MelonLogger.Instance log)
        {
            if (_resolved) return;
            try
            {
                var lm = Il2Cpp.LoadoutManager.singleton;
                var infos = lm == null ? null : lm.WeaponModInfo;
                if (infos == null) return;
                for (int i = 0; i < infos.Length; i++)
                {
                    var inf = infos[i]; if (inf == null) continue;
                    string n; try { n = inf.Name; } catch { continue; }
                    if (!string.Equals(n, DonorMagName, StringComparison.Ordinal)) continue;
                    _magId = i;
                    try { _donorSprite = inf.UiIcon; } catch { }
                    _resolved = true;
                    log.Msg("magbox: '" + DonorMagName + "' is id " + i + ", emblem '"
                          + (_donorSprite == null ? "none" : _donorSprite.name) + "'");
                    return;
                }
                log.Warning("magbox: '" + DonorMagName + "' not in the registry — list and emblem "
                          + "left as the game built them");
                _resolved = true;
            }
            catch (Exception e) { log.Warning("magbox: resolve failed: " + e.Message); _resolved = true; }
        }

        internal static void FilterList(Il2Cpp.ModUI ui, MelonLogger.Instance log)
        {
            try
            {
                if (ui == null || !IsMagBox(ui) || !BenchList.OwnedByUs(ui)) return;
                Resolve(log);
                if (_magId < 0) return;

                var parent = ui.modParent;
                if (parent == null) { Once(log, "magbox: magazine box has no modParent"); return; }
                var old = parent.CompatibleModIds;
                if (old == null) { Once(log, "magbox: socket has a null CompatibleModIds"); return; }

                if (!_listed)
                {
                    _listed = true;
                    var sb = new System.Text.StringBuilder();
                    sb.Append("magbox: socket offers ").Append(old.Count).Append(" entr")
                      .Append(old.Count == 1 ? "y" : "ies").Append(':');
                    for (int i = 0; i < old.Count; i++)
                        sb.Append("  [").Append(old[i]).Append("] '").Append(NameOf(old[i])).Append('\'');
                    log.Msg(sb.ToString());
                }

                bool hadNone = false, hasOurs = false;
                for (int i = 0; i < old.Count; i++)
                {
                    if (old[i] < 0) hadNone = true;
                    else if (old[i] == _magId) hasOurs = true;
                }
                if (!hasOurs)
                {
                    Once(log, "magbox: id " + _magId + " is not in the socket's offer list — leaving "
                            + "it alone rather than emptying the box");
                    return;
                }

                int want = (hadNone ? 1 : 0) + 1;
                if (old.Count == want) return;

                var ids = new Il2CppSystem.Collections.Generic.List<int>();
                if (hadNone) ids.Add(-1);
                ids.Add(_magId);
                parent.CompatibleModIds = ids;

                if (_listFixes++ < 8)
                    log.Msg("magbox: magazine list cut from " + old.Count + " to " + ids.Count);
            }
            catch { }
        }

        internal static void Note(Il2Cpp.ModUI ui, MelonLogger.Instance log)
        {
            try
            {
                if (ui == null || !IsMagBox(ui)) return;
                bool ours = BenchList.OwnedByUs(ui);

                try
                {
                    var wm = ui.weaponModding;
                    if (wm != null)
                    {
                        int id = wm.GetInstanceID();
                        if (ours) { _ourModding = id; _canvasRoot = wm.transform; }
                        else if (_ourModding == id) { _ourModding = 0; _canvasRoot = null; }
                    }
                }
                catch { }

                if (!ours) return;
                Resolve(log);
                _ourUi = ui;
                if (!MagIcon.Ready) MagIcon.Build(FindMagGroup(ui), log);
            }
            catch { }
        }

        internal static void Tick(MelonLogger.Instance log)
        {
            try
            {
                HoldIcons();

                if (++_tick < ScanEvery) return;
                _tick = 0;

                if (_canvasRoot == null) return;

                if (_ourModding == 0) return;

                bool ours = false;
                try { ours = _ourUi != null && _ourUi.gameObject != null
                          && _ourUi.gameObject.activeInHierarchy; }
                catch { ours = false; }
                if (!ours) return;

                MagLabel.Apply(_canvasRoot, log);

                if (_donorSprite == null) return;
                if (!MagIcon.Ready) return;
                var mine = MagIcon.Sprite;

                var imgs = _canvasRoot.GetComponentsInChildren<Image>(true);
                int repainted = 0;
                for (int i = 0; i < imgs.Length; i++)
                {
                    var img = imgs[i]; if (img == null) continue;
                    Sprite s; try { s = img.sprite; } catch { continue; }
                    if (s == null || s.GetInstanceID() != _donorSprite.GetInstanceID()) continue;

                    if (!UnderOurTable(img.transform)) continue;
                    try { img.sprite = mine; } catch { continue; }
                    if (!_painted.Contains(img)) _painted.Add(img);
                    repainted++;
                }

                if (!_reported && repainted > 0)
                {
                    _reported = true;
                    log.Msg("magbox: scan repainted " + repainted + " emblem(s)");
                }
            }
            catch { }
        }

        static void HoldIcons()
        {
            if (!MagIcon.Ready) return;
            var mine = MagIcon.Sprite;
            for (int i = _painted.Count - 1; i >= 0; i--)
            {
                var img = _painted[i];
                if (img == null) { _painted.RemoveAt(i); continue; }
                try
                {
                    if (_ourModding == 0 || !UnderOurTable(img.transform))
                    {
                        if (_donorSprite != null) img.sprite = _donorSprite;
                        _painted.RemoveAt(i);
                        continue;
                    }
                    if (img.sprite != mine) img.sprite = mine;
                }
                catch { _painted.RemoveAt(i); }
            }
        }

        static bool _pouchProbed;
        static int _cardsSeen;

        internal static void PouchBox(Il2Cpp.MagManagerMagUI box, MelonLogger.Instance log)
        {
            try
            {
                if (box == null) return;
                if (!_pouchProbed) { _pouchProbed = true; ProbePouch(box, log); }
                if (_cardsSeen < 12) { _cardsSeen++; ProbeCard(box, log); }

                bool mine = UnderOurTable(box.transform) || HSRMod.LocalHasOurs;
                if (!mine) return;

                MagLabel.Apply(box.transform, log);

                if (_donorSprite == null || !MagIcon.Ready) return;

                Image img; try { img = box.MagImage; } catch { return; }
                if (img == null) return;
                Sprite s; try { s = img.sprite; } catch { return; }
                if (s == null || s.GetInstanceID() != _donorSprite.GetInstanceID()) return;

                try { img.sprite = MagIcon.Sprite; } catch { return; }
                if (!_painted.Contains(img)) _painted.Add(img);
                Once(log, "magbox: pouch and magazine-manager cards now show the M200 emblem");
            }
            catch { }
        }

        static bool UnderOurTable(Transform t)
        {
            try
            {
                int g = 0;
                while (t != null && g++ < 10)
                {
                    var wm = t.GetComponent<Il2Cpp.WeaponModding>();
                    if (wm != null) return wm.GetInstanceID() == _ourModding;
                    t = t.parent;
                }
            }
            catch { }
            return false;
        }

        static void ProbeCard(Il2Cpp.MagManagerMagUI box, MelonLogger.Instance log)
        {
            try
            {
                int idx = -1; try { idx = box.MagSaveIndex; } catch { }
                string mag = "?", ammo = "?";
                try { var t = box.MagNameText; if (t != null) mag = (t.text ?? "").Trim(); } catch { }
                try { var t = box.AmmoNameText; if (t != null) ammo = (t.text ?? "").Trim(); } catch { }
                string owner = _ourModding != 0 && UnderOurTable(box.transform) ? "OURS" : "other";
                log.Msg(string.Format("MAGCARD: slot={0,-3} table={1,-5} mag='{2}' ammo='{3}'",
                                      idx, owner, mag, ammo));
            }
            catch { }
        }

        static void ProbePouch(Il2Cpp.MagManagerMagUI box, MelonLogger.Instance log)
        {
            try
            {
                var t = box.GetType();
                var sb = new System.Text.StringBuilder();
                sb.Append("POUCHPROBE: ").Append(t.FullName).Append('\n');

                const System.Reflection.BindingFlags F =
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance;

                sb.Append("  properties:");
                foreach (var p in t.GetProperties(F))
                {
                    var n = p.Name;
                    if (n.StartsWith("Pointer", StringComparison.Ordinal)) continue;
                    sb.Append("\n    ").Append(p.PropertyType.Name).Append(' ').Append(n);

                    if (Interesting(n)) sb.Append(" = ").Append(SafeRead(() => p.GetValue(box)));
                }
                sb.Append("\n  fields:");
                foreach (var f in t.GetFields(F))
                {
                    var n = f.Name;
                    if (n.StartsWith("NativeField", StringComparison.Ordinal)) continue;
                    if (n.StartsWith("NativeMethod", StringComparison.Ordinal)) continue;
                    sb.Append("\n    ").Append(f.FieldType.Name).Append(' ').Append(n);
                    if (Interesting(n)) sb.Append(" = ").Append(SafeRead(() => f.GetValue(box)));
                }

                try
                {
                    var mgr = box.MagUI;
                    if (mgr != null)
                    {
                        var mt = mgr.GetType();
                        sb.Append("\n  MagUI = ").Append(mt.FullName).Append("\n    properties:");
                        foreach (var p in mt.GetProperties(F))
                        {
                            var n = p.Name;
                            if (n.StartsWith("Pointer", StringComparison.Ordinal)) continue;
                            if (n == "transform" || n == "gameObject" || n == "name") continue;
                            sb.Append("\n      ").Append(p.PropertyType.Name).Append(' ').Append(n);
                            if (Interesting(n)) sb.Append(" = ").Append(SafeRead(() => p.GetValue(mgr)));
                        }
                        sb.Append("\n    fields:");
                        foreach (var f2 in mt.GetFields(F))
                        {
                            var n = f2.Name;
                            if (n.StartsWith("NativeField", StringComparison.Ordinal)) continue;
                            if (n.StartsWith("NativeMethod", StringComparison.Ordinal)) continue;
                            sb.Append("\n      ").Append(f2.FieldType.Name).Append(' ').Append(n);
                            if (Interesting(n)) sb.Append(" = ").Append(SafeRead(() => f2.GetValue(mgr)));
                        }
                    }
                    else sb.Append("\n  MagUI = null");
                }
                catch (Exception e) { sb.Append("\n  MagUI probe err: ").Append(e.Message); }

                sb.Append("\n  parents:");
                var tr = box.transform; int g = 0;
                while (tr != null && g++ < 8)
                {
                    sb.Append("\n    ").Append(tr.gameObject.name).Append("  [");
                    var comps = tr.GetComponents<Component>();
                    for (int i = 0; i < comps.Length; i++)
                    {
                        if (comps[i] == null) continue;
                        if (i > 0) sb.Append(", ");
                        sb.Append(SafeRead(() => comps[i].GetType().Name));
                    }
                    sb.Append(']');
                    tr = tr.parent;
                }
                log.Msg(sb.ToString());
            }
            catch (Exception e) { log.Warning("POUCHPROBE failed: " + e.Message); }
        }

        static bool Interesting(string n)
        {
            if (string.IsNullOrEmpty(n)) return false;
            var u = n.ToUpperInvariant();
            return u.Contains("WEAPON") || u.Contains("LOADOUT") || u.Contains("SLOT")
                || u.Contains("MAGAZINE") || u.Contains("MODNAME") || u.Contains("INDEX")
                || u.Contains("OWNER") || u.Contains("PARENT");
        }

        static string SafeRead(Func<object> f)
        {
            try { var v = f(); return v == null ? "null" : v.ToString(); }
            catch { return "<err>"; }
        }

        static Transform FindMagGroup(Il2Cpp.ModUI ui)
        {
            try
            {
                var p = ui.modParent; if (p == null) return null;
                Transform root = p.transform; if (root == null) return null;
                int g = 0;
                while (root.parent != null && g++ < 40) root = root.parent;

                var all = root.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    var c = all[i]; if (c == null) continue;
                    string n; try { n = c.gameObject.name; } catch { continue; }

                    if (string.Equals(n, ObjBuilder.MagGroup, StringComparison.OrdinalIgnoreCase)) return c;
                }
                return null;
            }
            catch { return null; }
        }
    }
}
