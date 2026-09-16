// ODG Operations - Diagnostics
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace ODGDiag
{

    internal static class Folders
    {
        public const string RootName = "ODG Diagnostics";

        public static string Root      { get; private set; }
        public static string Sessions  { get; private set; }
        public static string Report    { get; private set; }

        public static string GameRoot()
        {
            try
            {
                var root = Path.GetDirectoryName(Application.dataPath);
                if (!string.IsNullOrEmpty(root)) return root;
            }
            catch { }
            return Directory.GetCurrentDirectory();
        }

        public static void Prepare()
        {
            Root     = Path.Combine(GameRoot(), "UserData", RootName);
            Sessions = Path.Combine(Root, "Sessions");
            Report   = Path.Combine(Root, "SEND THIS FILE.txt");
            Directory.CreateDirectory(Sessions);
            WriteFolderNote();
        }

        public static string LiveFileForThisProcess()
        {
            int pid = 0;
            try { pid = System.Diagnostics.Process.GetCurrentProcess().Id; } catch { }
            return Path.Combine(Sessions, "_live " + pid + ".log");
        }

        public static string RingFor(string liveLog)
            => Path.ChangeExtension(liveLog, ".ring");

        static void WriteFolderNote()
        {
            try
            {
                var note = Path.Combine(Root, "What this folder is.txt");
                if (File.Exists(note)) return;
                File.WriteAllText(note,
@"ODG OPERATIONS - DIAGNOSTICS
===========================

This folder records what the game was doing, so that when something goes
wrong there is something to look at. It is created by the ODG Operations
pack and is not part of the game.

IF SOMETHING BREAKS
   Send Archduke the file named SEND THIS FILE.txt, in this folder.
   That one file is enough to start with.

IF SOMETHING LOOKS WRONG WHILE PLAYING
   Press the COMMA key ( , ) the moment you see it. That stamps the
   moment, and where you were standing, into the record.

WHAT IS IN HERE
   SEND THIS FILE.txt   A plain-English summary. Always the newest one.
   Sessions\            One record per time you played, named by date.
                        The name says whether that session ended normally
                        or crashed.

It is safe to delete anything in this folder. It will be recreated.
Nothing in here is sent anywhere on its own.
", new UTF8Encoding(false));
            }
            catch { }
        }

        public static string Retire(string liveFile, bool crashed, DateTime when)
        {
            try
            {
                if (!File.Exists(liveFile)) return null;
                string name = when.ToString("yyyy-MM-dd HHmm") + (crashed ? " - crashed.log" : " - ok.log");
                string dest = Path.Combine(Sessions, name);

                int n = 2;
                while (File.Exists(dest) && n < 50)
                {
                    dest = Path.Combine(Sessions, when.ToString("yyyy-MM-dd HHmm")
                         + " (" + n + ")" + (crashed ? " - crashed.log" : " - ok.log"));
                    n++;
                }
                if (File.Exists(dest))
                    dest = Path.Combine(Sessions, when.ToString("yyyy-MM-dd HHmm") + " ("
                         + Guid.NewGuid().ToString("N").Substring(0, 8) + ")"
                         + (crashed ? " - crashed.log" : " - ok.log"));

                File.Move(liveFile, dest);

                try { File.Delete(RingFor(liveFile)); } catch { }
                return dest;
            }
            catch
            {

                try
                {

                    var fallback = Path.Combine(Sessions,
                        when.ToString("yyyy-MM-dd HHmm")
                        + (crashed ? " - crashed" : " - ok") + " (rename failed).log");
                    if (!File.Exists(fallback)) { File.Move(liveFile, fallback); return fallback; }
                }
                catch { }
                return liveFile;
            }
        }

        public static void Prune(int keep)
        {
            try
            {
                var files = Directory.GetFiles(Sessions, "*.log");

                long total = 0;
                foreach (var f in files) try { total += new FileInfo(f).Length; } catch { }
                bool overSize = total > 150L * 1024 * 1024;

                if (files.Length <= keep && !overSize) return;
                Array.Sort(files, (a, b) =>
                    File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
                for (int i = files.Length - 1; i >= 0; i--)
                {
                    bool overCount = i >= keep;
                    if (!overCount && !overSize) break;

                    if (Path.GetFileName(files[i]).StartsWith("_live", StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        long sz = new FileInfo(files[i]).Length;
                        File.Delete(files[i]);
                        total -= sz;
                        overSize = total > 150L * 1024 * 1024;
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
