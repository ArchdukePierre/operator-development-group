// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;

namespace MapLoader
{

    internal static class CustomMaps
    {
        public const string FolderName = "Custom";

        internal sealed class Meta
        {
            public string Title;
            public string Briefing;
            public string Duration;
            public string Threat;
            public string OpType;
            public int MinAI = -1;
            public int MaxAI = -1;
            public int PrepSeconds = -1;
            public string[] Infils;
        }

        static MelonLogger.Instance _log;
        public static void Init(MelonLogger.Instance log) { _log = log; }

        public static string Root()
        {
            try
            {
                var root = MelonLoader.Utils.MelonEnvironment.GameRootDirectory;
                if (!string.IsNullOrEmpty(root))
                    return Path.Combine(root, "Mods", "MapLoader_Assets", FolderName);
            }
            catch { }
            return null;
        }

        public static string[] Scan()
        {
            var found = new List<string>();
            var root = Root();
            if (root == null || !Directory.Exists(root)) return found.ToArray();
            try
            {
                foreach (var dir in Directory.GetDirectories(root))
                {
                    var name = Path.GetFileName(dir);
                    if (string.IsNullOrEmpty(name)) continue;
                    if (File.Exists(Path.Combine(dir, "manifest.bin")))
                    {
                        found.Add(name);
                    }
                    else
                    {
                        _log?.Warning("[custom] '" + name + "' has no manifest.bin, so it is not a "
                                    + "map and gets no row. A map is the folder the build tool "
                                    + "wrote, copied in whole.");
                    }
                }
            }
            catch (Exception e) { _log?.Warning("[custom] scan: " + e.Message); }
            found.Sort(StringComparer.OrdinalIgnoreCase);
            if (found.Count > 0)
                _log?.Msg("[custom] " + found.Count + " map(s) under " + root + ": "
                        + string.Join(", ", found.ToArray()));
            return found.ToArray();
        }

        public static bool IsCustom(string name)
        {
            var root = Root();
            if (root == null || string.IsNullOrEmpty(name)) return false;
            try { return File.Exists(Path.Combine(root, name, "manifest.bin")); }
            catch { return false; }
        }

        public static bool TryLoadMeta(string name, out Meta meta)
        {
            meta = null;
            var root = Root();
            if (root == null || string.IsNullOrEmpty(name)) return false;
            var path = Path.Combine(root, name, "map.json");
            if (!File.Exists(path)) return false;
            try
            {
                var json = File.ReadAllText(path);
                var m = new Meta();
                m.Title = Str(json, "title");
                m.Briefing = Str(json, "briefing");
                m.Duration = Str(json, "duration");
                m.Threat = Str(json, "threat");
                m.OpType = Str(json, "type");
                m.MinAI = Int(json, "minAI");
                m.MaxAI = Int(json, "maxAI");
                m.PrepSeconds = Int(json, "prepSeconds");
                m.Infils = StrList(json, "infils");
                meta = m;
                _log?.Msg("[custom] map.json for '" + name + "': title='" + (m.Title ?? "") + "'");
                return true;
            }
            catch (Exception e)
            {
                _log?.Warning("[custom] map.json for '" + name + "' could not be read: " + e.Message
                            + ". The map still gets a row with a plain name.");
                return false;
            }
        }

        public static CustomOp Apply(CustomOp op, Meta m)
        {
            if (op == null || m == null) return op;
            if (!string.IsNullOrEmpty(m.Title))
            {
                op.PackageName = m.Title.ToUpperInvariant();
                op.AreaOfOperation = m.Title.ToUpperInvariant();
            }
            if (!string.IsNullOrEmpty(m.Briefing)) op.Description = m.Briefing;
            if (!string.IsNullOrEmpty(m.Duration)) op.Duration = m.Duration.ToUpperInvariant();
            if (!string.IsNullOrEmpty(m.Threat)) op.ThreatLevel = m.Threat.ToUpperInvariant();
            if (!string.IsNullOrEmpty(m.OpType)) op.OpType = m.OpType.ToUpperInvariant();
            if (m.MinAI >= 0) op.MinAI = m.MinAI;
            if (m.MaxAI >= 0) op.MaxAI = m.MaxAI;
            if (m.MinAI >= 0 && m.MaxAI >= 0 && op.MaxAI < op.MinAI) op.MaxAI = op.MinAI;
            if (m.PrepSeconds >= 0) op.PrepSeconds = m.PrepSeconds;
            if (m.Infils != null && m.Infils.Length > 0) op.Infils = m.Infils;
            return op;
        }

        static string Str(string json, string key)
        {
            int i = json.IndexOf("\"" + key + "\"", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            int c = json.IndexOf(':', i); if (c < 0) return null;
            int q = json.IndexOf('"', c); if (q < 0) return null;
            var sb = new System.Text.StringBuilder();
            for (int p = q + 1; p < json.Length; p++)
            {
                char ch = json[p];
                if (ch == '\\' && p + 1 < json.Length)
                {
                    char n = json[p + 1];
                    if (n == 'n') { sb.Append('\n'); p++; continue; }
                    if (n == '"') { sb.Append('"'); p++; continue; }
                    if (n == '\\') { sb.Append('\\'); p++; continue; }
                }
                if (ch == '"') break;
                sb.Append(ch);
            }
            return sb.ToString();
        }

        static int Int(string json, string key)
        {
            int i = json.IndexOf("\"" + key + "\"", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return -1;
            int c = json.IndexOf(':', i); if (c < 0) return -1;
            int e = c + 1;
            while (e < json.Length && json[e] != ',' && json[e] != '}' && json[e] != '\n') e++;
            int v;
            return int.TryParse(json.Substring(c + 1, e - c - 1).Trim(), out v) && v >= 0 ? v : -1;
        }

        static string[] StrList(string json, string key)
        {
            int i = json.IndexOf("\"" + key + "\"", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            int open = json.IndexOf('[', i); if (open < 0) return null;
            int close = json.IndexOf(']', open); if (close < 0) return null;
            var body = json.Substring(open + 1, close - open - 1);
            var list = new List<string>();
            int p = 0;
            while (true)
            {
                int q1 = body.IndexOf('"', p); if (q1 < 0) break;
                int q2 = body.IndexOf('"', q1 + 1); if (q2 < 0) break;
                var s = body.Substring(q1 + 1, q2 - q1 - 1).Trim();
                if (s.Length > 0) list.Add(s);
                p = q2 + 1;
            }
            return list.Count > 0 ? list.ToArray() : null;
        }
    }
}
