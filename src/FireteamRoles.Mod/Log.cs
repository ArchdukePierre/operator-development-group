// ODG Operations - FireteamRoles
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Text;
using MelonLoader;

namespace FireteamRoles
{

    internal static class Log
    {
        static readonly object _lock = new object();
        static readonly StringBuilder _buf = new StringBuilder();
        static readonly System.Collections.Generic.HashSet<string> _fired = new System.Collections.Generic.HashSet<string>();
        static int _lastFlush;

        internal static string GameRoot
        {
            get { try { return MelonLoader.Utils.MelonEnvironment.GameRootDirectory; } catch { return AppContext.BaseDirectory; } }
        }

        internal static string LogPath => Path.Combine(GameRoot, "Mods", "FireteamRoles.log");

        internal static void Msg(string s)
        {
            try { MelonLogger.Msg(s); } catch { }
            Detail(s);
        }

        internal static void Warn(string s)
        {
            try { MelonLogger.Warning(s); } catch { }
            Detail("WARN " + s);
        }

        internal static void Err(string s)
        {
            try { MelonLogger.Error(s); } catch { }
            Detail("ERR  " + s);
        }

        internal static void Detail(string s)
        {
            try
            {
                lock (_lock)
                {
                    _buf.Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append("  ").Append(s).Append('\n');

                    if (_buf.Length > 8192 || Environment.TickCount - _lastFlush > 1500) FlushLocked();
                }
            }
            catch { }
        }

        internal static void Fired(string tag)
        {
            try
            {
                lock (_lock)
                {
                    if (!_fired.Add(tag)) return;
                }
                Msg("FIRED  " + tag);

                Flush();
            }
            catch { }
        }

        internal static void Flush()
        {
            try { lock (_lock) { FlushLocked(); } } catch { }
        }

        static void FlushLocked()
        {
            if (_buf.Length == 0) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                File.AppendAllText(LogPath, _buf.ToString());
            }
            catch { }
            _buf.Length = 0;
            _lastFlush = Environment.TickCount;
        }
    }
}
