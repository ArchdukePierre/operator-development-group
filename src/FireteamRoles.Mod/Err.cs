// ODG Operations - FireteamRoles
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Text;

namespace FireteamRoles
{

    internal static class Err
    {
        const int TripAfter = 12;
        const float RelogSeconds = 30f;

        class Site
        {
            internal int Count;
            internal float NextLog;
            internal bool Tripped;
            internal string FirstMessage;
        }

        static readonly Dictionary<string, Site> _sites = new Dictionary<string, Site>();

        static readonly Dictionary<string, int> _tripHistory = new Dictionary<string, int>();
        const int PermanentAfterTrips = 3;
        static readonly Queue<string> _crumbs = new Queue<string>();
        static readonly HashSet<string> _riskDone = new HashSet<string>();
        static readonly object _lock = new object();

        static float Now
        {
            get { try { return UnityEngine.Time.realtimeSinceStartup; } catch { return 0f; } }
        }

        internal static void Note(string site, Exception e)
        {
            Site s;
            bool trip = false, report = false;
            int count;

            lock (_lock)
            {
                if (!_sites.TryGetValue(site, out s)) { s = new Site(); _sites[site] = s; }
                s.Count++;
                count = s.Count;
                if (s.FirstMessage == null) s.FirstMessage = Describe(e);

                if (!s.Tripped && s.Count >= TripAfter) { s.Tripped = true; trip = true; }
                else if (Now >= s.NextLog) { s.NextLog = Now + RelogSeconds; report = true; }
            }

            if (trip)
            {
                Log.Err("DISABLED  " + site + " after " + count + " failures. First: " + s.FirstMessage);
                Log.Msg("breadcrumbs: " + Crumbs());
                Log.Flush();

                try { Ui.Toast("Fireteam Roles: " + Friendly(site) + " disabled after repeated errors"); }
                catch { }
            }
            else if (report)
            {
                Log.Err(site + " failed (" + count + "x): " + Describe(e));
                if (count == 1) Log.Detail("  first occurrence, full detail: " + e);
            }
        }

        static string Friendly(string site)
        {
            if (site == null) return "a feature";
            if (site.StartsWith("drag")) return "dragging";
            if (site.StartsWith("treat")) return "the treat screen";
            if (site.StartsWith("downed")) return "the downed state";
            if (site.StartsWith("icons")) return "medical icons";
            if (site.StartsWith("ui")) return "the interface";
            if (site.StartsWith("perks") || site.StartsWith("hook.nade")) return "role perks";
            if (site.StartsWith("medical") || site.StartsWith("kit") || site.StartsWith("epi")) return "the medic kit";
            if (site.StartsWith("net") || site.StartsWith("state")) return "team sync";
            if (site.StartsWith("pose")) return "the downed pose";
            return site;
        }

        static string Describe(Exception e)
        {
            if (e == null) return "null exception";
            var m = e.Message;
            if (string.IsNullOrEmpty(m)) m = e.GetType().Name;
            return e.GetType().Name + ": " + m;
        }

        internal static bool Tripped(string site)
        {
            lock (_lock)
            {
                Site s;
                return _sites.TryGetValue(site, out s) && s.Tripped;
            }
        }

        internal static void ResetAll(string why)
        {
            int revived = 0;
            var keptOff = new List<string>();
            lock (_lock)
            {
                foreach (var kv in _sites)
                {
                    if (!kv.Value.Tripped) continue;
                    int trips;
                    _tripHistory.TryGetValue(kv.Key, out trips);
                    trips++;
                    _tripHistory[kv.Key] = trips;
                    if (trips >= PermanentAfterTrips) keptOff.Add(kv.Key); else revived++;
                }
                _sites.Clear();
                _riskDone.Clear();

                for (int i = 0; i < keptOff.Count; i++)
                    _sites[keptOff[i]] = new Site { Tripped = true, Count = TripAfter, FirstMessage = "kept off: failed in " + PermanentAfterTrips + " separate scenes" };
            }

            if (revived > 0) Log.Msg("re-enabling " + revived + " disabled subsystem(s) (" + why + ")");
            if (keptOff.Count > 0)
                Log.Warn("staying off for this session (repeatedly broken): " + string.Join(", ", keptOff.ToArray()));
        }

        internal static bool Guard(string site, Action body)
        {
            if (body == null) return false;
            if (Tripped(site)) return false;
            try { body(); return true; }
            catch (Exception e) { Note(site, e); return false; }
        }

        internal static T Try<T>(string site, Func<T> body, T fallback)
        {
            if (body == null) return fallback;
            if (Tripped(site)) return fallback;
            try { return body(); }
            catch (Exception e) { Note(site, e); return fallback; }
        }

        internal static void Crumb(string what)
        {
            if (string.IsNullOrEmpty(what)) return;
            lock (_lock)
            {
                _crumbs.Enqueue(what);
                while (_crumbs.Count > 20) _crumbs.Dequeue();
            }
        }

        internal static string Crumbs()
        {
            lock (_lock)
            {
                if (_crumbs.Count == 0) return "(none)";
                var sb = new StringBuilder();
                foreach (var c in _crumbs)
                {
                    if (sb.Length > 0) sb.Append(" -> ");
                    sb.Append(c);
                }
                return sb.ToString();
            }
        }

        internal static bool Risky(string tag, Action body)
        {
            if (body == null) return false;
            if (Tripped(tag)) return false;

            bool first;
            lock (_lock) { first = _riskDone.Add(tag); }

            if (!first)
            {
                try { body(); return true; }
                catch (Exception e) { Note(tag, e); return false; }
            }

            Crumb(tag);
            Log.Msg("about to: " + tag);
            Log.Flush();
            try
            {
                body();
                Log.Msg("survived: " + tag);
                return true;
            }
            catch (Exception e) { Note(tag, e); return false; }
        }

        internal static string Report()
        {
            var sb = new StringBuilder();
            lock (_lock)
            {
                if (_sites.Count == 0) return "no failures recorded";
                foreach (var kv in _sites)
                {
                    sb.Append("\n    ").Append(kv.Value.Tripped ? "[OFF] " : "      ")
                      .Append(kv.Key).Append("  x").Append(kv.Value.Count)
                      .Append("  ").Append(kv.Value.FirstMessage);
                }
            }
            return sb.ToString();
        }

        internal static int FailingSites
        {
            get { lock (_lock) { return _sites.Count; } }
        }
    }
}
