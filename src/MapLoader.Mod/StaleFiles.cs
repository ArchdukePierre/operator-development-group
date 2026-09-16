// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;

namespace MapLoader
{

    static class StaleFiles
    {
        const string ShippedList = ".odg-shipped-files.txt";
        const string ObsoleteList = ".odg-obsolete.txt";

        public struct Entry
        {
            public string Rel;
            public long Bytes;
            public bool IsDir;
        }

        static MelonLogger.Instance _log;

        static string GameRoot()
        {
            try { return MelonLoader.Utils.MelonEnvironment.GameRootDirectory; } catch { return null; }
        }

        static bool Allowlisted(string nameLower)
        {
            switch (nameLower)
            {
                case ".installed_release":
                case ".installed_mapdata":
                case ".modpack_version":
                case "door_placements.json":
                case "ai_skeleton.json":
                case "thumbs.db":
                case "desktop.ini":
                    return true;
            }
            return nameLower.EndsWith(".log", StringComparison.Ordinal)
                || nameLower.EndsWith(".odg-off", StringComparison.Ordinal)
                || nameLower.EndsWith(".tmp", StringComparison.Ordinal)
                || nameLower.Contains(".bak");
        }

        static string Norm(string rel)
        {
            var s = (rel ?? "").Trim().Replace('\\', '/');
            while (s.StartsWith("./", StringComparison.Ordinal)) s = s.Substring(2);
            return s.TrimStart('/');
        }

        static List<string> ReadList(string path)
        {
            var outp = new List<string>();
            try
            {
                if (!File.Exists(path)) return outp;
                foreach (var raw in File.ReadAllLines(path))
                {
                    var l = raw.Trim();
                    if (l.Length == 0 || l.StartsWith("#", StringComparison.Ordinal)) continue;
                    var n = Norm(l);
                    if (n.Length > 0) outp.Add(n);
                }
            }
            catch { }
            return outp;
        }

        static bool SafeRel(string rel)
        {
            var lower = rel.ToLowerInvariant();
            if (!lower.StartsWith("mods/", StringComparison.Ordinal) || lower == "mods/") return false;
            if (rel.Contains(":")) return false;
            foreach (var seg in rel.Split('/')) if (seg == "..") return false;
            return true;
        }

        static long DirBytes(string dir)
        {
            long total = 0;
            try
            {
                foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                    try { total += new FileInfo(f).Length; } catch { }
            }
            catch { }
            return total;
        }

        static void Walk(string root, string dir, List<KeyValuePair<string, long>> outp)
        {
            try
            {
                foreach (var f in Directory.GetFiles(dir))
                {
                    var fi = new FileInfo(f);
                    if ((fi.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    var rel = Norm(f.Substring(root.Length));
                    outp.Add(new KeyValuePair<string, long>(rel, fi.Length));
                }
                foreach (var d in Directory.GetDirectories(dir))
                {
                    var di = new DirectoryInfo(d);
                    if ((di.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    Walk(root, d, outp);
                }
            }
            catch { }
        }

        public static List<Entry> Scan()
        {
            var found = new List<Entry>();
            var root = GameRoot();
            if (string.IsNullOrEmpty(root)) return found;
            root = root.TrimEnd('\\', '/');
            var mods = Path.Combine(root, "Mods");
            if (!Directory.Exists(mods)) return found;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var shipped = ReadList(Path.Combine(mods, ShippedList));
            if (shipped.Count > 0)
            {
                var shippedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var ownedAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var s in shipped)
                {
                    shippedSet.Add(s);
                    var lower = s.ToLowerInvariant();
                    if (!lower.StartsWith("mods/", StringComparison.Ordinal)) continue;
                    var rest = s.Substring(5);
                    int slash = rest.IndexOf('/');
                    if (slash < 0) continue;
                    var top = rest.Substring(0, slash);
                    owned.Add(top);
                    if (string.Equals(top, "MapLoader_Assets", StringComparison.OrdinalIgnoreCase))
                    {
                        var more = rest.Substring(slash + 1);
                        int s2 = more.IndexOf('/');
                        if (s2 > 0) ownedAssets.Add(more.Substring(0, s2));
                    }
                }
                ownedAssets.Remove("Custom");

                var files = new List<KeyValuePair<string, long>>();
                try
                {
                    foreach (var d in Directory.GetDirectories(mods))
                    {
                        var di = new DirectoryInfo(d);
                        if ((di.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        if (!owned.Contains(di.Name)) continue;
                        Walk(root, d, files);
                    }
                }
                catch { }
                foreach (var kv in files)
                {
                    var rel = kv.Key;
                    if (shippedSet.Contains(rel)) continue;
                    var name = rel.Substring(rel.LastIndexOf('/') + 1).ToLowerInvariant();
                    if (Allowlisted(name)) continue;
                    var lower = rel.ToLowerInvariant();
                    const string ma = "mods/maploader_assets/";
                    if (lower.StartsWith(ma, StringComparison.Ordinal))
                    {
                        var rest = rel.Substring(ma.Length);
                        int s2 = rest.IndexOf('/');
                        if (s2 > 0)
                        {
                            var sub = rest.Substring(0, s2);
                            if (string.Equals(sub, "Custom", StringComparison.OrdinalIgnoreCase)
                                || !ownedAssets.Contains(sub)) continue;
                        }
                    }
                    if (seen.Add(rel)) found.Add(new Entry { Rel = rel, Bytes = kv.Value, IsDir = false });
                }
            }

            foreach (var line in ReadList(Path.Combine(mods, ObsoleteList)))
            {
                bool isDir = line.EndsWith("/", StringComparison.Ordinal);
                var clean = line.TrimEnd('/');
                if (!SafeRel(clean)) continue;
                var path = Path.Combine(root, clean.Replace('/', Path.DirectorySeparatorChar));
                try
                {
                    if (isDir)
                    {
                        var di = new DirectoryInfo(path);
                        if (!di.Exists || (di.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        if (seen.Add(clean)) found.Add(new Entry { Rel = clean, Bytes = DirBytes(path), IsDir = true });
                    }
                    else
                    {
                        var fi = new FileInfo(path);
                        if (!fi.Exists || (fi.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        if (seen.Add(clean)) found.Add(new Entry { Rel = clean, Bytes = fi.Length, IsDir = false });
                    }
                }
                catch { }
            }

            found.Sort((a, b) => string.Compare(a.Rel, b.Rel, StringComparison.OrdinalIgnoreCase));
            return found;
        }

        public static string Delete(List<Entry> entries, out int removed, out int failed)
        {
            removed = 0; failed = 0;
            var sb = new System.Text.StringBuilder();
            var root = GameRoot();
            if (string.IsNullOrEmpty(root)) return "no game folder";
            root = root.TrimEnd('\\', '/');
            var mods = Path.GetFullPath(Path.Combine(root, "Mods"));
            foreach (var e in entries)
            {
                var rel = e.Rel.TrimEnd('/');
                if (!SafeRel(rel)) { failed++; sb.Append("  refused (outside Mods): ").Append(e.Rel).Append('\n'); continue; }
                string path;
                try { path = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar))); }
                catch { failed++; continue; }
                if (!path.StartsWith(mods + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                { failed++; sb.Append("  refused (outside Mods): ").Append(e.Rel).Append('\n'); continue; }
                try
                {
                    if (Directory.Exists(path))
                    {
                        if ((new DirectoryInfo(path).Attributes & FileAttributes.ReparsePoint) != 0)
                        { failed++; sb.Append("  refused (a link): ").Append(e.Rel).Append('\n'); continue; }
                        Directory.Delete(path, true);
                    }
                    else if (File.Exists(path))
                    {
                        if ((new FileInfo(path).Attributes & FileAttributes.ReparsePoint) != 0)
                        { failed++; sb.Append("  refused (a link): ").Append(e.Rel).Append('\n'); continue; }
                        File.Delete(path);
                    }
                    removed++;
                    sb.Append("  removed: ").Append(e.Rel).Append('\n');
                    _log?.Msg("[stale] removed " + e.Rel);
                }
                catch (Exception ex)
                {
                    failed++;
                    sb.Append("  NOT removed: ").Append(e.Rel).Append(" (").Append(ex.Message).Append(")\n");
                    _log?.Warning("[stale] could not remove " + e.Rel + ": " + ex.Message);
                }
            }
            return sb.ToString().TrimEnd();
        }

        static string Fmt(long bytes)
        {
            if (bytes >= 1048576) return (bytes / 1048576.0).ToString("0.0") + " MB";
            if (bytes >= 1024) return (bytes / 1024.0).ToString("0") + " KB";
            return bytes + " B";
        }

        public static void Report(MelonLogger.Instance log)
        {
            _log = log;
            try
            {
                var root = GameRoot();
                if (string.IsNullOrEmpty(root)) return;
                var mods = Path.Combine(root, "Mods");
                if (!File.Exists(Path.Combine(mods, ShippedList)) && !File.Exists(Path.Combine(mods, ObsoleteList)))
                {
                    _log?.Msg("[stale] no shipped-file list in Mods (a pack before v91, or a dev deploy); "
                            + "the leftover check has nothing to compare against.");
                    return;
                }
                var found = Scan();
                if (found.Count == 0) { _log?.Msg("[stale] no leftovers from earlier packs; this install holds the current pack and nothing older."); return; }
                long total = 0; foreach (var e in found) total += e.Bytes;
                var sb = new System.Text.StringBuilder();
                sb.Append("[stale] ").Append(found.Count).Append(" leftover file(s) from earlier packs still installed (")
                  .Append(Fmt(total)).Append("): ");
                for (int i = 0; i < found.Count && i < 6; i++) { if (i > 0) sb.Append(", "); sb.Append(found[i].Rel); }
                if (found.Count > 6) sb.Append(", ...");
                sb.Append(". The launcher offers UPDATE + DELETE on its next run; in game, 'odg stale delete' removes them.");
                _log?.Warning(sb.ToString());
            }
            catch (Exception e) { _log?.Warning("[stale] " + e.Message); }
        }

        public static string Console(string args)
        {
            try
            {
                var found = Scan();
                if (found.Count == 0) return "no leftovers from earlier packs; this install holds the current pack and nothing older.";
                long total = 0; foreach (var e in found) total += e.Bytes;
                if (string.Equals(args, "delete", StringComparison.OrdinalIgnoreCase))
                {
                    int removed, failed;
                    var detail = Delete(found, out removed, out failed);
                    return "leftovers: " + removed + " removed, " + failed + " not removed" + (failed > 0 ? " (is a file in use? try again from the launcher with the game closed)" : "") + "\n" + detail;
                }
                var sb = new System.Text.StringBuilder();
                sb.Append(found.Count).Append(" leftover(s) from earlier packs, ").Append(Fmt(total)).Append(" -- 'odg stale delete' removes them:\n");
                foreach (var e in found) sb.Append("  ").Append(e.Rel).Append(e.IsDir ? "/" : "").Append("  ").Append(Fmt(e.Bytes)).Append('\n');
                return sb.ToString().TrimEnd();
            }
            catch (Exception e) { return "stale: " + e.Message; }
        }
    }
}
