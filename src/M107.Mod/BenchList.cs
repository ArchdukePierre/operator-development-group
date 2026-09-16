// ODG Operations - M107
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using UnityEngine;

namespace M107
{

    internal static class BenchList
    {
        static int _fixes;

        static readonly string[] HideCategories =
        {
            "BARREL",
            "MUZZLE",
            "SUPPRESSOR",
            "FRONTIRONSIGHTS", "FRONTIRONSIGHT",
            "PICCATINNY", "PICATINNY",
            "UPPERRECIEVER", "UPPERRECEIVER",
            "TOPRAIL",
            "CANTEDSIGHT",
            "CHARGINGHANDLE",
        };

        static readonly List<GameObject> _hidden = new List<GameObject>();

        internal static void ResetForScene() { _hidden.Clear(); }

        static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == ' ' || c == '_' || c == '-') continue;
                sb.Append(char.ToUpperInvariant(c));
            }
            return sb.ToString();
        }

        internal static void NoteBox(Il2Cpp.ModUI ui)
        {
            try
            {
                if (ui == null) return;
                var go = ui.gameObject; if (go == null) return;
                if (!OnM107(ui.transform) && !BoxIsOurs(ui)) return;

                string label; try { label = ui.attachmentTypeName; } catch { return; }
                string key = Norm(label);
                bool hide = false;
                for (int i = 0; i < HideCategories.Length; i++)
                    if (key == HideCategories[i]) { hide = true; break; }
                if (!hide) return;

                for (int i = 0; i < _hidden.Count; i++) if (_hidden[i] == go) return;
                _hidden.Add(go);
                try { go.SetActive(false); } catch { }
                M107Mod.Log("bench: hiding category '" + label + "' (part stays fitted, box not drawn)");
            }
            catch { }
        }

        internal static void HoldHidden()
        {
            try
            {
                for (int i = _hidden.Count - 1; i >= 0; i--)
                {
                    var go = _hidden[i];
                    if (go == null) { _hidden.RemoveAt(i); continue; }
                    try { if (go.activeSelf) go.SetActive(false); } catch { }
                }
            }
            catch { }
        }

        static bool BoxIsOurs(Il2Cpp.ModUI ui)
        {
            try
            {
                var p = ui.modParent;
                if (p != null && OnM107(p.transform)) return true;
                string c; try { c = ui.Compatibility; } catch { return false; }
                return string.Equals(c, Parts.Compat, StringComparison.Ordinal);
            }
            catch { return false; }
        }

        internal static void Fix(Il2Cpp.WeaponModParent parent)
        {
            try
            {
                if (parent == null || Parts.Offers.Count == 0) return;
                if (!OnM107(parent.transform)) return;

                var types = parent.attachmentType;
                if (types == null || types.Count == 0) return;

                var mine = new List<Parts.Offer>();
                bool allExclusive = true;
                for (int i = 0; i < Parts.Offers.Count; i++)
                {
                    var o = Parts.Offers[i];
                    if (!Accepts(types, o.Type)) continue;
                    mine.Add(o);
                    if (!o.Exclusive) allExclusive = false;
                }
                if (mine.Count == 0) return;

                var old = parent.CompatibleModIds;
                bool hadNone = false;
                if (old != null)
                    for (int i = 0; i < old.Count; i++) if (old[i] < 0) { hadNone = true; break; }

                var ids = new Il2CppSystem.Collections.Generic.List<int>();

                if (hadNone) ids.Add(-1);
                for (int i = 0; i < mine.Count; i++) ids.Add(mine[i].Id);

                if (!allExclusive && old != null)
                    for (int i = 0; i < old.Count; i++)
                    {
                        int id = old[i];
                        if (id < 0) continue;
                        if (Has(ids, id)) continue;
                        ids.Add(id);
                    }

                if (Same(old, ids)) return;

                parent.CompatibleModIds = ids;

                if (_fixes++ < 24)
                {
                    var sb = new System.Text.StringBuilder();
                    sb.Append("bench: socket '").Append(Name(parent)).Append("' now offers ");
                    for (int i = 0; i < mine.Count; i++) { if (i > 0) sb.Append(", "); sb.Append('\'').Append(mine[i].Name).Append('\''); }
                    sb.Append(allExclusive ? "  (donor parts removed)" : "  (plus the game's own)");
                    sb.Append("  was ").Append(old == null ? 0 : old.Count).Append(" -> ").Append(ids.Count);
                    M107Mod.Log(sb.ToString());
                }
            }
            catch (Exception e) { M107Mod.Log("bench: list fix err: " + e.Message); }
        }

        static bool Accepts(Il2CppSystem.Collections.Generic.List<Il2Cpp.WeaponMod.AttachmentType> types,
                            Il2Cpp.WeaponMod.AttachmentType want)
        {
            try
            {
                for (int i = 0; i < types.Count; i++) if (types[i] == want) return true;
            }
            catch { }
            return false;
        }

        static bool Has(Il2CppSystem.Collections.Generic.List<int> list, int v)
        {
            try { for (int i = 0; i < list.Count; i++) if (list[i] == v) return true; } catch { }
            return false;
        }

        static bool Same(Il2CppSystem.Collections.Generic.List<int> a, Il2CppSystem.Collections.Generic.List<int> b)
        {
            try
            {
                if (a == null || b == null) return false;
                if (a.Count != b.Count) return false;
                for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
                return true;
            }
            catch { return false; }
        }

        static string Name(Il2Cpp.WeaponModParent p)
        {
            try { var n = p.ModParentName; if (!string.IsNullOrEmpty(n)) return n; } catch { }
            try { return p.name; } catch { return "?"; }
        }

        static bool OnM107(Transform t)
        {
            int g = 0;
            while (t != null && g++ < 40)
            {
                string n; try { n = t.name; } catch { return false; }
                if (n != null && (n.StartsWith(WeaponTemplate.ObjectName, StringComparison.Ordinal) ||
                                  n.StartsWith(Parts.Prefix, StringComparison.Ordinal))) return true;

                try { var wm = t.gameObject.GetComponent<Il2Cpp.WeaponMods>(); if (wm != null) return M107Mod.IsOurs(wm); }
                catch { }
                t = t.parent;
            }
            return false;
        }
    }
}
