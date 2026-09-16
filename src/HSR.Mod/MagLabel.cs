// ODG Operations - HSR
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using MelonLoader;
using UnityEngine;

namespace HSR
{

    internal static class MagLabel
    {

        static readonly string[][] Swaps =
        {

            new[] { "338 Lapua Magnum",  "408 CheyTac" },
            new[] { "338 Lapua",         "408 CheyTac" },

            new[] { "338 Lupua",         ".408 CheyTac" },
            new[] { "EXT_338_R700_Mag",  "M200 Extended Magazine" },
            new[] { "338_R700_Mag",      "M200 10rnd Magazine" },
            new[] { "338 R700 Mag",      "M200 10rnd Magazine" },
        };

        static readonly System.Collections.Generic.HashSet<string> _seen =
            new System.Collections.Generic.HashSet<string>();

        static MelonPreferences_Entry<bool> _enabled;
        internal static void Init(MelonPreferences_Category cat)
        {
            _enabled = cat.CreateEntry("RelabelMagazine", true);
        }

        static bool Enabled { get { try { return _enabled != null && _enabled.Value; } catch { return false; } } }

        static int _reported;
        internal static void ResetForScene() { _reported = 0; }

        internal static void Apply(Transform root, MelonLogger.Instance log)
        {
            if (!Enabled || root == null) return;
            try
            {
                var texts = root.GetComponentsInChildren<Il2CppTMPro.TextMeshProUGUI>(true);
                if (texts == null) return;

                int hit = 0;
                for (int i = 0; i < texts.Length; i++)
                {
                    var t = texts[i];
                    if (t == null) continue;

                    string cur;
                    try { cur = t.text; } catch { continue; }
                    if (string.IsNullOrEmpty(cur)) continue;

                    string next = Rewrite(cur);
                    if (ReferenceEquals(next, cur) || next == cur) continue;

                    try { t.text = next; hit++; } catch { }
                    if (log != null && _seen.Add(cur) && _seen.Count <= 24)
                        log.Msg("maglabel: '{0}' -> '{1}'", cur, next);
                }

                if (hit > 0 && _reported++ < 3 && log != null)
                    log.Msg("maglabel: corrected {0} label(s) on our table to read .408 CheyTac.", hit);
            }
            catch { }
        }

        internal static string Rewrite(string cur)
        {
            if (string.IsNullOrEmpty(cur)) return cur;
            string next = cur;
            for (int k = 0; k < Swaps.Length; k++)
            {
                if (next.IndexOf(Swaps[k][0], StringComparison.OrdinalIgnoreCase) < 0) continue;
                next = Replace(next, Swaps[k][0], Swaps[k][1]);
            }
            return next;
        }

        static string Replace(string s, string find, string with)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            int i = 0;
            while (i < s.Length)
            {
                int at = s.IndexOf(find, i, StringComparison.OrdinalIgnoreCase);
                if (at < 0) { sb.Append(s, i, s.Length - i); break; }
                sb.Append(s, i, at - i);
                sb.Append(with);
                i = at + find.Length;
            }
            return sb.ToString();
        }
    }
}
