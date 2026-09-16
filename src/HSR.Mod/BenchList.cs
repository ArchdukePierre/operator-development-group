// ODG Operations - HSR
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace HSR
{

    internal static class BenchList
    {

        static readonly string[] KeepExact = { "SIGHT", "MAGAZINE", "AMMUNITION", "AMMO" };

        static readonly string[] KeepContains = { "SCOPE", "CAP", "NVG", "OPTIC", "RETICLE" };

        static readonly List<GameObject> _hidden = new List<GameObject>();
        static readonly HashSet<string> _census = new HashSet<string>();

        internal static void ResetForScene() { _hidden.Clear(); }

        internal static string Norm(string s)
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

        static bool Keep(string key)
        {
            for (int i = 0; i < KeepExact.Length; i++)
                if (key == KeepExact[i]) return true;
            for (int i = 0; i < KeepContains.Length; i++)
                if (key.IndexOf(KeepContains[i], StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        internal static void NoteBox(Il2Cpp.ModUI ui, MelonLogger.Instance log)
        {
            try
            {
                if (ui == null) return;
                var go = ui.gameObject; if (go == null) return;

                string label; try { label = ui.attachmentTypeName; } catch { return; }
                string key = Norm(label);
                bool keep = Keep(key);
                bool ours = OwnedByUs(ui);

                if (_census.Add(key + "|" + (ours ? "1" : "0")))
                {
                    string compat = "?"; try { compat = ui.Compatibility ?? "null"; } catch { }
                    string owner = "none";
                    try { var p = ui.modParent; if (p != null) owner = Path(p.transform); } catch { }
                    log.Msg("  bench box '{0}' ours={1} keep={2} compat='{3}' socket={4}",
                        label, ours, keep, compat, owner);
                }

                if (!ours) return;
                if (keep) return;
                for (int i = 0; i < _hidden.Count; i++) if (_hidden[i] == go) return;
                _hidden.Add(go);
                try { go.SetActive(false); } catch { }
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

        static string Path(Transform t)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                int g = 0;
                while (t != null && g++ < 6)
                {
                    if (sb.Length > 0) sb.Insert(0, "/");
                    sb.Insert(0, t.gameObject.name);
                    t = t.parent;
                }
                return sb.ToString();
            }
            catch { return "?"; }
        }

        internal static bool OwnedByUs(Il2Cpp.ModUI ui)
        {
            try
            {
                var p = ui.modParent;
                if (p != null && p.transform != null && OnOurs(p.transform)) return true;
            }
            catch { }
            try
            {
                var wm = ui.weaponModding;
                if (wm != null && wm.transform != null && OnOurs(wm.transform)) return true;
            }
            catch { }
            return false;
        }

        static bool OnOurs(Transform t)
        {
            int g = 0;
            while (t != null && g++ < 40)
            {
                string n; try { n = t.name; } catch { return false; }
                if (n != null && (n.StartsWith("HSR", StringComparison.Ordinal) ||
                                  n.StartsWith(HSRMod.WeaponPrefix, StringComparison.Ordinal))) return true;
                t = t.parent;
            }
            return false;
        }
    }
}
