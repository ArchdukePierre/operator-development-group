// ODG Operations - HSR
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace HSR
{

    internal static class MagProbe
    {
        const int ScanEvery = 3;
        const int RendRefreshScans = 10;
        const float SummarySec = 5f;
        const int MaxEpisodeLogs = 40;
        const long SideFileCap = 40L * 1024 * 1024;

        class Rend
        {
            internal Renderer R; internal string Name; internal bool Skin;
            internal bool On, Fro; internal bool Known;
        }

        class Mag
        {
            internal Transform T; internal int Id; internal string Name; internal string Path;
            internal int FirstFrame; internal float FirstTime; internal int LastSeenScan;
            internal readonly List<Rend> Rends = new List<Rend>(); internal int RendScan = -1; internal int ChildCount = -1;
            internal string Where = "?"; internal string Weapon = "-"; internal bool Socketed, HasSkin, InHold;
            internal int VisibleSince = -1; internal float VisibleSinceTime; internal int Episodes; internal int LongestFrames;
            internal string LastWriter = "-"; internal int LastWriterFrame = -1; internal int LastWriterCount;
            internal readonly List<string> History = new List<string>();
            internal int VisibleCount;
            internal string Kind = "-";
            internal bool Ours;
            internal string Owner = "?";
        }

        static readonly Dictionary<int, Mag> _mags = new Dictionary<int, Mag>();
        static readonly List<int> _gone = new List<int>();
        static int _frame, _scan;
        static float _lastSummary;
        static int _episodeLogs;
        static string _lastReload = "-"; static float _lastReloadTime = -1f; static int _lastReloadFrame = -1;
        static readonly List<string> _events = new List<string>();
        static int _episodesTotal, _longestTotal;
        static StreamWriter _file; static long _fileBytes; static float _flushAt; static bool _fileDead;

        internal static void ResetForScene()
        {
#if ODG_PUBLIC

            return;
#endif
            _mags.Clear(); _events.Clear(); _lastReload = "-"; _lastReloadTime = -1f; _lastReloadFrame = -1;
            Side("# scene reset");
        }

        internal static void Event(string what)
        {
#if ODG_PUBLIC

            return;
#endif
            try
            {
                if (what == null) return;
                float now = Time.unscaledTime;
                string w = what.ToLowerInvariant();
                if (w.Contains("reload") || w.Contains("magout") || w.Contains("magin"))
                { _lastReload = what; _lastReloadTime = now; _lastReloadFrame = _frame; }
                if (_events.Count < 40) _events.Add(now.ToString("F2") + ":" + what);
                Side("EVENT f" + _frame + " " + what);
            }
            catch { }
        }

        internal static void Wrote(Transform mag, string who, int count)
        {
#if ODG_PUBLIC

            return;
#endif
            try
            {
                if (mag == null || count <= 0) return;
                Mag m; int id = mag.GetInstanceID();
                if (!_mags.TryGetValue(id, out m)) return;
                m.LastWriter = who; m.LastWriterFrame = _frame; m.LastWriterCount = count;
                Note(m, who + " off×" + count);
            }
            catch { }
        }

        internal static void Tick(List<Transform> roots, Func<Transform, bool> isMine, Func<Transform, bool> inHold, MelonLoader.MelonLogger.Instance log)
        {
#if ODG_PUBLIC

            return;
#endif
            _frame++;
            try
            {
                float now = Time.unscaledTime;
                if (roots != null && roots.Count > 0 && _frame % ScanEvery == 0)
                {
                    _scan++;
                    for (int r = 0; r < roots.Count; r++)
                    {
                        var root = roots[r];
                        if (root == null) continue;
                        string owner = "remote";
                        try { if (isMine != null && isMine(root)) owner = "mine"; } catch { }
                        Scan(root, owner, inHold, log);
                    }
                    Prune(log);
                }
                if (_mags.Count > 0) Watch(log);
                if (now - _lastSummary >= SummarySec) { _lastSummary = now; Summarise(log); }
                if (now - _flushAt > 1f) { _flushAt = now; try { if (_file != null) _file.Flush(); } catch { } }
            }
            catch (Exception e) { if (log != null) log.Msg("magprobe: tick err " + e.Message); }
        }

        static void Scan(Transform root, string owner, Func<Transform, bool> inHold, MelonLoader.MelonLogger.Instance log)
        {
            Il2Cpp.Magazine[] comps = null;
            try { comps = root.GetComponentsInChildren<Il2Cpp.Magazine>(true); } catch { }
            if (comps == null) return;

            for (int i = 0; i < comps.Length; i++)
            {
                var c = comps[i]; if (c == null) continue;
                Transform t; try { t = c.transform; } catch { continue; }
                if (t == null) continue;
                int id; try { id = t.GetInstanceID(); } catch { continue; }

                Mag m;
                bool fresh = !_mags.TryGetValue(id, out m);
                if (fresh)
                {
                    m = new Mag { T = t, Id = id, FirstFrame = _frame, FirstTime = Time.unscaledTime };
                    try { m.Name = t.gameObject.name; } catch { m.Name = "?"; }
                    m.Path = Path(t, 8);
                    m.Ours = m.Name != null && m.Name.IndexOf(MagBox.DonorMagName, StringComparison.Ordinal) >= 0;
                    m.Owner = owner;
                    _mags[id] = m;
                }
                m.LastSeenScan = _scan;
                Locate(m);
                try { m.InHold = inHold != null && inHold(t); } catch { m.InHold = false; }

                int cc = -1; try { cc = t.childCount; } catch { }
                if (m.RendScan < 0 || (_scan - m.RendScan) >= RendRefreshScans || cc != m.ChildCount)
                {
                    RefreshRends(m);
                    m.RendScan = _scan; m.ChildCount = cc;
                }

                if (fresh)
                {
                    var sb = new StringBuilder();
                    sb.Append("magprobe: NEW magazine '").Append(m.Name).Append("' id ").Append(id).Append(" owner=").Append(m.Owner).Append(" at ").Append(m.Where)
                      .Append(" weapon=").Append(m.Weapon).Append(" socketed=").Append(m.Socketed).Append(" skin=").Append(m.HasSkin)
                      .Append(" held=").Append(m.InHold).Append(" renderers=").Append(m.Rends.Count).Append(" drawing=").Append(Drawing(m))
                      .Append(" skinReady=").Append(MagSkin.Ready).Append(" sinceReload=").Append(SinceReload())
                      .Append("\n   path ").Append(m.Path);
                    if (log != null) log.Msg(sb.ToString());
                    Side(sb.ToString());
                    Note(m, "new " + m.Where);
                }
            }

        }

        static void Prune(MelonLoader.MelonLogger.Instance log)
        {
            _gone.Clear();
            foreach (var kv in _mags) if (kv.Value.LastSeenScan < _scan - 1) _gone.Add(kv.Key);
            for (int g = 0; g < _gone.Count; g++)
            {
                var m = _mags[_gone[g]];
                if (m.VisibleSince >= 0) EndEpisode(m, "destroyed", log);
                var line = "magprobe: gone '" + m.Name + "' id " + m.Id + " owner=" + m.Owner + " after " + (_frame - m.FirstFrame) + " frames, episodes " + m.Episodes
                         + " longest " + m.LongestFrames + " frames, last " + m.Where;
                Side(line);
                if (m.Episodes > 0 && log != null) log.Msg(line);
                _mags.Remove(_gone[g]);
            }
        }

        static void Locate(Mag m)
        {
            m.Socketed = false; m.Where = "OTHER"; m.Weapon = "-"; m.HasSkin = false;
            try
            {
                try { m.Socketed = HSRMod.IsMagazineSocketed(m.T); } catch { }
                try { m.HasSkin = m.T.Find("M200_MagSkin") != null; } catch { }
                var p = m.T.parent; int g = 0; bool hand = false; string weapon = null;
                while (p != null && g++ < 24)
                {
                    string n = null; try { n = p.gameObject.name; } catch { }
                    if (n != null)
                    {
                        if (weapon == null) { try { if (p.GetComponent<Il2Cpp.WeaponV3>() != null) weapon = n; } catch { } }
                        if (!hand && n.IndexOf("hand", StringComparison.OrdinalIgnoreCase) >= 0) hand = true;
                    }
                    p = p.parent;
                }
                if (weapon != null) { m.Weapon = weapon; m.Where = m.Socketed ? "SEATED" : "WEAPON"; }
                else if (hand) m.Where = "HAND";
                else m.Where = "POUCH";
            }
            catch { }
        }

        static void RefreshRends(Mag m)
        {
            var old = new Dictionary<int, Rend>();
            for (int i = 0; i < m.Rends.Count; i++) { try { old[m.Rends[i].R.GetInstanceID()] = m.Rends[i]; } catch { } }
            m.Rends.Clear();
            Renderer[] rr = null;
            try { rr = m.T.GetComponentsInChildren<Renderer>(true); } catch { }
            if (rr == null) return;
            Transform skin = null; try { skin = m.T.Find("M200_MagSkin"); } catch { }
            for (int i = 0; i < rr.Length; i++)
            {
                var r = rr[i]; if (r == null) continue;
                int rid; try { rid = r.GetInstanceID(); } catch { continue; }
                Rend re;
                if (!old.TryGetValue(rid, out re))
                {
                    re = new Rend { R = r };
                    try { re.Name = r.gameObject.name; } catch { re.Name = "?"; }
                    try { var rt = r.transform; re.Skin = skin != null && (rt == skin || rt.IsChildOf(skin)); } catch { }
                    if (m.RendScan >= 0) Side("f" + _frame + " '" + m.Name + "' id " + m.Id + " renderer appeared '" + re.Name + "' skin=" + re.Skin + " at " + m.Where);
                }
                m.Rends.Add(re);
            }
        }

        static void Watch(MelonLoader.MelonLogger.Instance log)
        {
            foreach (var kv in _mags)
            {
                var m = kv.Value;
                bool anyDrawing = false; int drawing = 0;
                bool gameShows = false, hasSkin = false, skinOn = false;
                for (int i = 0; i < m.Rends.Count; i++)
                {
                    var re = m.Rends[i];
                    bool on, fro;
                    try { on = re.R.enabled; fro = re.R.forceRenderingOff; } catch { continue; }
                    if (re.Known && (on != re.On || fro != re.Fro))
                        Side("f" + _frame + " '" + m.Name + "' id " + m.Id + " '" + re.Name + "' enabled " + re.On + ">" + on + " fro " + re.Fro + ">" + fro
                             + " skin=" + re.Skin + " at " + m.Where + (m.LastWriterFrame == _frame ? " writer=" + m.LastWriter : ""));
                    re.On = on; re.Fro = fro; re.Known = true;
                    if (re.Skin) { hasSkin = true; if (on && !fro) skinOn = true; }
                    else
                    {
                        if (on) gameShows = true;
                        if (on && !fro) { anyDrawing = true; drawing++; }
                    }
                }
                m.VisibleCount = drawing;

                string problem = null;
                if (m.Ours)
                {
                    if (anyDrawing) problem = "BLACK";
                    else if (hasSkin && gameShows && !skinOn) problem = "NO MAG";
                }
                if (problem != null)
                {
                    if (m.VisibleSince >= 0 && m.Kind != problem) EndEpisode(m, "became " + problem, log);
                    if (m.VisibleSince < 0) StartEpisode(m, problem, log);
                }
                else if (m.VisibleSince >= 0)
                {
                    EndEpisode(m, m.LastWriterFrame >= m.VisibleSince ? m.LastWriter : "nobody we know", log);
                }
            }
        }

        static void StartEpisode(Mag m, string kind, MelonLoader.MelonLogger.Instance log)
        {
            m.VisibleSince = _frame; m.VisibleSinceTime = Time.unscaledTime; m.Episodes++; _episodesTotal++; m.Kind = kind;
            Note(m, kind + " start");
            if (_episodeLogs >= MaxEpisodeLogs) return;
            _episodeLogs++;
            var sb = new StringBuilder(768);
            sb.Append(kind == "BLACK" ? "magprobe: BLACK MAGAZINE DRAWING: '" : "magprobe: NO MAGAZINE, skin disabled by the game: '").Append(m.Name).Append("' owner=").Append(m.Owner).Append(" '").Append("' id ").Append(m.Id).Append(" at ").Append(m.Where)
              .Append(" weapon=").Append(m.Weapon).Append(" socketed=").Append(m.Socketed).Append(" skin=").Append(m.HasSkin)
              .Append(" held=").Append(m.InHold).Append(" skinReady=").Append(MagSkin.Ready).Append(" follow=").Append(MagFollow.StateForHealth())
              .Append(" age=").Append(_frame - m.FirstFrame).Append("f sinceReload=").Append(SinceReload())
              .Append(" lastWriter=").Append(m.LastWriter).Append('@').Append(m.LastWriterFrame < 0 ? "-" : (_frame - m.LastWriterFrame) + "f ago")
              .Append(" fps=").Append((1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime)).ToString("F0")).Append('\n');
            sb.Append("   renderers drawing: ");
            for (int i = 0; i < m.Rends.Count; i++) { var re = m.Rends[i]; if (!re.Skin && re.On && !re.Fro) sb.Append('\'').Append(re.Name).Append("' "); }
            sb.Append("\n   renderers dark: ");
            for (int i = 0; i < m.Rends.Count; i++) { var re = m.Rends[i]; if (!(re.On && !re.Fro)) sb.Append('\'').Append(re.Name).Append(re.Skin ? "(skin)' " : "' "); }
            sb.Append("\n   history: ").Append(string.Join(" > ", m.History.ToArray()));
            sb.Append("\n   path ").Append(m.Path);
            if (log != null) log.Msg(sb.ToString());
            Side(sb.ToString());
        }

        static void EndEpisode(Mag m, string endedBy, MelonLoader.MelonLogger.Instance log)
        {
            int frames = _frame - m.VisibleSince;
            float ms = (Time.unscaledTime - m.VisibleSinceTime) * 1000f;
            if (frames > m.LongestFrames) m.LongestFrames = frames;
            if (frames > _longestTotal) _longestTotal = frames;
            m.VisibleSince = -1;
            Note(m, "dark after " + frames + "f by " + endedBy);
            var line = "magprobe: " + (m.Kind == "BLACK" ? "dark again '" : "skin back on '") + m.Name + "' id " + m.Id + " after " + frames + " frame(s) / " + ms.ToString("F0") + "ms, ended by " + endedBy
                     + " at " + m.Where + " sinceReload=" + SinceReload();
            Side(line);
            if (_episodeLogs <= MaxEpisodeLogs && log != null) log.Msg(line);
        }

        static void Summarise(MelonLoader.MelonLogger.Instance log)
        {
            if (_mags.Count == 0 && _events.Count == 0) return;
            int seated = 0, weapon = 0, hand = 0, pouch = 0, skinned = 0, visible = 0;
            var vis = new StringBuilder();
            foreach (var kv in _mags)
            {
                var m = kv.Value;
                switch (m.Where) { case "SEATED": seated++; break; case "WEAPON": weapon++; break; case "HAND": hand++; break; default: pouch++; break; }
                if (m.HasSkin) skinned++;
                if (m.VisibleSince >= 0) { visible++; vis.Append('\'').Append(m.Name).Append("'@").Append(m.Where).Append('(').Append(_frame - m.VisibleSince).Append("f) "); }
            }
            var sb = new StringBuilder(384);
            sb.Append("magprobe: ").Append(_mags.Count).Append(" magazine(s): seated ").Append(seated).Append(" weapon ").Append(weapon)
              .Append(" hand ").Append(hand).Append(" pouch ").Append(pouch).Append(", skinned ").Append(skinned)
              .Append(", episodes ").Append(_episodesTotal).Append(" longest ").Append(_longestTotal).Append("f, drawing now ").Append(visible);
            if (visible > 0) sb.Append(" [").Append(vis.ToString().TrimEnd()).Append(']');
            sb.Append(", skinReady=").Append(MagSkin.Ready).Append(" follow=").Append(MagFollow.StateForHealth());
            if (_events.Count > 0) { sb.Append(" | events ").Append(string.Join(" ", _events.ToArray())); _events.Clear(); }
            bool interesting = visible > 0 || _episodesTotal > 0 || sb.ToString().Contains("events");
            if (interesting && log != null) log.Msg(sb.ToString());
            Side(sb.ToString());
        }

        static void Note(Mag m, string s)
        {
            m.History.Add("f" + _frame + " " + s);
            if (m.History.Count > 12) m.History.RemoveAt(0);
        }

        static int Drawing(Mag m)
        {
            int n = 0;
            for (int i = 0; i < m.Rends.Count; i++)
            {
                var re = m.Rends[i];
                try { if (!re.Skin && re.R.enabled && !re.R.forceRenderingOff) n++; } catch { }
            }
            return n;
        }

        static string SinceReload()
        {
            if (_lastReloadFrame < 0) return "never";
            return (_frame - _lastReloadFrame) + "f/" + ((Time.unscaledTime - _lastReloadTime) * 1000f).ToString("F0") + "ms('" + _lastReload + "')";
        }

        static string Path(Transform t, int levels)
        {
            try
            {
                var sb = new StringBuilder(); int g = 0;
                while (t != null && g++ < levels) { if (sb.Length > 0) sb.Insert(0, '/'); sb.Insert(0, t.name); t = t.parent; }
                return sb.ToString();
            }
            catch { return "?"; }
        }

        static void Side(string line)
        {
            if (_fileDead) return;
            try
            {
                if (_file == null)
                {
                    var dir = System.IO.Path.Combine(MelonLoader.Utils.MelonEnvironment.GameRootDirectory, "Mods");
                    var path = System.IO.Path.Combine(dir, "HSR_magprobe.log");
                    _file = new StreamWriter(path, true, Encoding.UTF8);
                    _file.WriteLine("\n===== magprobe " + DateTime.Now + " =====");
                }
                _file.WriteLine(line);
                _fileBytes += line.Length + 2;
                if (_fileBytes > SideFileCap) { _file.WriteLine("# cap reached, trace stops here"); _file.Flush(); _file.Dispose(); _file = null; _fileDead = true; }
            }
            catch { _fileDead = true; }
        }
    }
}
