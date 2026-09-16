// ODG Operations - M107
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Text;

namespace M107
{

    internal static class LoadoutRepair
    {
        static int _repairs;

        internal static string Fix(string json, out int filled)
        {
            filled = 0;
            try
            {
                if (string.IsNullOrEmpty(json) || Parts.NameByPath.Count == 0) return json;
                if (json.IndexOf("\"WeaponModName\"", StringComparison.Ordinal) < 0) return json;
                if (json.IndexOf("\"\"", StringComparison.Ordinal) < 0) return json;

                if (!Slice(json, "\"WeaponModName\":[", out int nOpen, out int nClose)) return json;
                if (!Slice(json, "\"ModParentDirectory\":[", out int pOpen, out int pClose)) return json;

                var names = SplitQuoted(json.Substring(nOpen, nClose - nOpen));
                var paths = SplitPaths(json.Substring(pOpen, pClose - pOpen));
                if (names.Count == 0 || names.Count != paths.Count) return json;

                for (int i = 0; i < names.Count; i++)
                {
                    if (!string.IsNullOrEmpty(names[i])) continue;
                    if (!Parts.NameByPath.TryGetValue(paths[i], out var nm)) continue;
                    names[i] = nm;
                    filled++;
                }
                if (filled == 0) return json;

                var sb = new StringBuilder();
                for (int i = 0; i < names.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append('"').Append(names[i]).Append('"');
                }
                return json.Substring(0, nOpen) + sb + json.Substring(nClose);
            }
            catch { return json; }
        }

        internal static void Note(int filled, string where)
        {
            if (filled <= 0) return;
            if (_repairs++ > 12) return;
            M107Mod.Log("loadout: filled in " + filled + " blank part name(s) from their socket paths (" + where +
                        ") - a blank MOD_Upper is what took the scope with it");
        }

        static bool Slice(string s, string tag, out int open, out int close)
        {
            open = close = -1;
            int i = s.IndexOf(tag, StringComparison.Ordinal);
            if (i < 0) return false;
            open = i + tag.Length;
            int depth = 1;
            for (int k = open; k < s.Length; k++)
            {
                char c = s[k];
                if (c == '[') depth++;
                else if (c == ']') { depth--; if (depth == 0) { close = k; return true; } }
            }
            return false;
        }

        static List<string> SplitQuoted(string body)
        {
            var outp = new List<string>();
            bool inStr = false; var cur = new StringBuilder();
            for (int i = 0; i < body.Length; i++)
            {
                char c = body[i];
                if (c == '"') { if (inStr) { outp.Add(cur.ToString()); cur.Length = 0; } inStr = !inStr; continue; }
                if (inStr) cur.Append(c);
            }
            return outp;
        }

        static List<string> SplitPaths(string body)
        {
            var outp = new List<string>();
            int i = 0;
            while (i < body.Length)
            {
                int b = body.IndexOf('{', i);
                if (b < 0) break;
                int depth = 0, e = b;
                for (; e < body.Length; e++)
                {
                    if (body[e] == '{') depth++;
                    else if (body[e] == '}') { depth--; if (depth == 0) break; }
                }
                if (e >= body.Length) break;
                var seg = body.Substring(b, e - b + 1);
                var parts = SplitQuoted(seg);
                var joined = new StringBuilder();
                for (int k = 0; k < parts.Count; k++)
                {
                    if (parts[k] == "path") continue;
                    if (joined.Length > 0) joined.Append('/');
                    joined.Append(parts[k]);
                }
                outp.Add(joined.ToString());
                i = e + 1;
            }
            return outp;
        }
    }
}
