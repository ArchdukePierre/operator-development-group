// ODG Operations - FireteamRoles
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using UnityEngine;

namespace FireteamRoles
{

    internal static class Bar
    {
        internal class Row
        {
            internal ulong Id;
            internal string Name = "";
            internal Role Role;
            internal bool HasMod;
            internal bool Downed;
            internal float Remaining;
            internal bool Self;
            internal Color Tint;
            internal string RightText = "";

            internal string Display = "";
        }

        static readonly List<Row> _rows = new List<Row>();
        static readonly int[] _tally = new int[RoleInfo.Count];
        static float _next;
        static int _lastCount = -1;

        internal static int DownedCount;
        internal static int TotalCount { get { return _rows.Count; } }

        internal static int Generation;

        internal static string TallyText = "";
        internal static string DownedText = "";
        internal static string TitleText = "FIRETEAM 0";
        internal static string TabText = "FIRETEAM 0";
        internal static string HintText = "F8";

        static bool _autoOverride;

        internal static bool Minimized
        {
            get { return Cfg.BarMinimized.Value; }
        }

        internal static void SetMinimized(bool v)
        {
            if (Cfg.BarMinimized.Value == v) return;
            Cfg.BarMinimized.Value = v;
            try { MelonLoader.MelonPreferences.Save(); } catch { }
            Log.Msg("fireteam window: " + (v ? "minimised to the tab" : "restored"));
            Refresh(true);
        }

        internal static bool Collapsed
        {
            get
            {
                if (Cfg.BarCollapsed.Value) return true;
                if (_autoOverride) return false;
                int auto = Cfg.BarAutoCollapseAbove.Value;
                return auto > 0 && _rows.Count > auto;
            }
        }

        internal static bool AutoCollapsed
        {
            get
            {
                if (_autoOverride) return false;
                int auto = Cfg.BarAutoCollapseAbove.Value;
                return !Cfg.BarCollapsed.Value && auto > 0 && _rows.Count > auto;
            }
        }

        internal static void Toggle()
        {

            if (Minimized) { SetMinimized(false); return; }

            if (AutoCollapsed)
            {
                _autoOverride = true;
                Log.Msg("fireteam bar: showing all " + _rows.Count + " for now");
            }
            else if (_autoOverride)
            {
                _autoOverride = false;
                Log.Msg("fireteam bar: auto-collapse back on");
            }
            else
            {
                Cfg.BarCollapsed.Value = !Cfg.BarCollapsed.Value;
                try { MelonLoader.MelonPreferences.Save(); } catch { }
                Log.Msg("fireteam bar: " + (Cfg.BarCollapsed.Value ? "collapsed" : "expanded"));
            }
            Refresh(true);
        }

        internal static List<Row> Rows()
        {
            Refresh(false);
            return _rows;
        }

        internal static int[] Tally()
        {
            Refresh(false);
            return _tally;
        }

        internal static void Tick() { Refresh(false); }

        internal static void Invalidate() { _next = 0f; }

        static void Refresh(bool force)
        {
            int count;
            try { count = State.Peers.Count; } catch { return; }

            if (!force && count == _lastCount && Time.realtimeSinceStartup < _next) return;
            _next = Time.realtimeSinceStartup + 0.2f;
            _lastCount = count;

            try
            {
                _rows.Clear();
                for (int i = 0; i < _tally.Length; i++) _tally[i] = 0;
                DownedCount = 0;

                foreach (var kv in State.Peers)
                {
                    var p = kv.Value;
                    if (p == null) continue;

                    var r = new Row
                    {
                        Id = p.Id,
                        Name = string.IsNullOrEmpty(p.Name) ? p.Id.ToString() : p.Name,
                        Role = p.Role,
                        HasMod = p.HasMod,
                        Downed = p.Downed,
                        Remaining = p.Remaining,
                        Self = p.Id == Net.Self
                    };

                    if (r.Name.Length > 64) r.Name = r.Name.Substring(0, 64);
                    r.Display = r.Name;

                    if (r.Downed)
                    {
                        r.Tint = new Color(0.93f, 0.33f, 0.33f);
                        r.RightText = "DOWN " + Mathf.Max(0, Mathf.CeilToInt(r.Remaining)) + "s";
                        DownedCount++;
                    }
                    else if (!r.HasMod)
                    {
                        r.Tint = new Color(0.45f, 0.45f, 0.45f);
                        r.RightText = "--";
                    }
                    else
                    {
                        r.Tint = RoleInfo.Colour(r.Role);
                        r.RightText = RoleInfo.Name(r.Role);
                        int idx = (int)r.Role;
                        if (idx >= 0 && idx < _tally.Length) _tally[idx]++;
                    }
                    _rows.Add(r);
                }

                _rows.Sort(Compare);
                BuildStrings();
                Generation++;
            }
            catch (Exception e) { Log.Detail("bar refresh failed: " + e.Message); }
        }

        static int Compare(Row a, Row b)
        {
            if (a.Self != b.Self) return a.Self ? -1 : 1;
            int n = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            if (n != 0) return n;
            return a.Id.CompareTo(b.Id);
        }

        static readonly System.Text.StringBuilder _sb = new System.Text.StringBuilder(64);

        static void BuildStrings()
        {
            _sb.Length = 0;
            for (int i = 1; i < _tally.Length; i++)
            {
                if (_tally[i] <= 0) continue;
                if (_sb.Length > 0) _sb.Append("  ");
                _sb.Append(_tally[i]).Append(' ').Append(RoleInfo.Short((Role)i));
            }
            TallyText = _sb.ToString();

            DownedText = DownedCount > 0 ? (DownedCount + " DOWN") : "";
            TitleText = "FIRETEAM " + _rows.Count;

            try
            {
                string k = Cfg.BarCollapseKey.Value;
                HintText = string.IsNullOrEmpty(k) ? "" : k.ToUpperInvariant();
            }
            catch { HintText = ""; }

            TabText = "FIRETEAM " + _rows.Count +
                      (string.IsNullOrEmpty(HintText) ? "" : "   [" + HintText + "]");
        }

        internal static void Reset(string why)
        {
            _rows.Clear();
            DownedCount = 0;
            _lastCount = -1;
            _next = 0f;
            _autoOverride = false;
            Generation++;
        }
    }
}
