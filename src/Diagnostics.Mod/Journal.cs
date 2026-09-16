// ODG Operations - Diagnostics
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace ODGDiag
{

    internal static unsafe class Journal
    {

        const int HeaderBytes = 64;
        const int RingBytes = 1 << 20;
        const int MaxRecord = 8192;

        const int RecHeader = 13;

        static MemoryMappedFile _mmf;
        static MemoryMappedViewAccessor _view;
        static byte* _base;
        static byte* _ring;
        static readonly object _gate = new object();

        static FileStream _logFs;
        static StreamWriter _log;
        static Thread _mirror;
        static volatile bool _stop, _dead;

        static DateTime _epochWall;
        static long _epochSw;

        [ThreadStatic] static byte[] _enc;

        static int _mainThreadId;
        static long _mainBeats;

        public static string RingPath { get; private set; }
        public static string LogPath { get; private set; }
        public static bool UsingRing => _base != null;

        static long _selfTicks, _selfCalls;

        public static void Start(string ringPath, string logPath)
        {

            _stop = false;
            _dead = false;

            RingPath = ringPath;
            LogPath = logPath;
            _epochWall = DateTime.Now;
            _epochSw = System.Diagnostics.Stopwatch.GetTimestamp();

            _mainThreadId = Environment.CurrentManagedThreadId;

            try { Directory.CreateDirectory(Path.GetDirectoryName(logPath)); } catch { }

            try
            {
                _logFs = new FileStream(logPath, FileMode.Create, FileAccess.Write,
                                        FileShare.ReadWrite, 4096, FileOptions.None);
                _log = new StreamWriter(_logFs, new UTF8Encoding(false)) { AutoFlush = false };
            }
            catch { _dead = true; return; }

            try
            {
                _mmf = MemoryMappedFile.CreateFromFile(ringPath, FileMode.Create, null,
                                                       HeaderBytes + RingBytes);
                _view = _mmf.CreateViewAccessor(0, HeaderBytes + RingBytes);
                _view.SafeMemoryMappedViewHandle.AcquirePointer(ref _base);
                _ring = _base + HeaderBytes;

                *(long*)(_base + 0) = 0x314749524744_4F44L;
                Cursor = 0;
                Mirrored = 0;
                *(int*)(_base + 24) = RingBytes;
                *(int*)(_base + 28) = SafePid();
                *(long*)(_base + 32) = _epochWall.Ticks;
                *(long*)(_base + 40) = _epochSw;
                *(long*)(_base + 48) = System.Diagnostics.Stopwatch.Frequency;
            }
            catch
            {

                _base = null;
            }

            try
            {
                _mirror = new Thread(Pump);
                _mirror.IsBackground = true;

                _mirror.Priority = ThreadPriority.Normal;
                _mirror.Name = "ODG diagnostics mirror";
                _mirror.Start();
            }
            catch { }
        }

        static int SafePid()
        {
            try { return System.Diagnostics.Process.GetCurrentProcess().Id; } catch { return 0; }
        }

        static long Cursor
        {
            get => Volatile.Read(ref Unsafe.AsRef<long>(_base + 8));
            set => Volatile.Write(ref Unsafe.AsRef<long>(_base + 8), value);
        }
        static long Mirrored
        {
            get => Volatile.Read(ref Unsafe.AsRef<long>(_base + 16));
            set => Volatile.Write(ref Unsafe.AsRef<long>(_base + 16), value);
        }

        public static void NoteMainThread()
        {
            _mainThreadId = Environment.CurrentManagedThreadId;
        }

        public static string EpochLine()
        {
            try
            {
                var off = TimeZoneInfo.Local.GetUtcOffset(_epochWall);
                return "clock: session began " + _epochWall.ToString("yyyy-MM-dd HH:mm:ss")
                     + " local, UTC" + (off < TimeSpan.Zero ? "-" : "+")
                     + Math.Abs(off.Hours).ToString("00") + ":" + Math.Abs(off.Minutes).ToString("00")
                     + " (times below are local, same day unless a date line says otherwise)";
            }
            catch { return "clock: unavailable"; }
        }

        public static void Write(string text)
        {
            if (_dead || text == null) return;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                var buf = _enc ?? (_enc = new byte[MaxRecord]);
                int n;

                const int cap = MaxRecord - RecHeader - 1;
                try { n = Encoding.UTF8.GetBytes(text, 0, Math.Min(text.Length, cap), buf, 0); }
                catch
                {

                    int lo = 0, hi = Math.Min(text.Length, cap);
                    while (lo < hi)
                    {
                        int mid = (lo + hi + 1) / 2;
                        int bytes;
                        try { bytes = Encoding.UTF8.GetByteCount(text, 0, mid); } catch { break; }
                        if (bytes <= cap) lo = mid; else hi = mid - 1;
                    }
                    if (lo <= 0) return;
                    try { n = Encoding.UTF8.GetBytes(text, 0, lo, buf, 0); }
                    catch { return; }
                }

                long ticks = System.Diagnostics.Stopwatch.GetTimestamp();

                if (Environment.CurrentManagedThreadId == _mainThreadId)
                    Interlocked.Increment(ref _mainBeats);

                lock (_gate)
                {
                    if (_dead) return;
                    if (_base != null) AppendRing(buf, n, ticks);
                    else AppendDirect(buf, n, ticks);
                }
            }
            catch { }
            finally
            {

                Interlocked.Add(ref _selfTicks, System.Diagnostics.Stopwatch.GetTimestamp() - t0);
                Interlocked.Increment(ref _selfCalls);
            }
        }

        public static void WriteNow(string text) => Write(text);

        static void AppendRing(byte[] src, int n, long ticks)
        {
            int total = RecHeader + n;
            if (total > RingBytes) return;

            long start = Cursor;
            int off = (int)(start % RingBytes);

            byte* hdr = stackalloc byte[RecHeader];
            hdr[0] = 0x01;
            *(long*)(hdr + 1) = ticks;
            *(int*)(hdr + 9) = n;
            CopyWrap(hdr, RecHeader, ref off);
            fixed (byte* p = src) { CopyWrap(p, n, ref off); }

            Cursor = start + total;
        }

        static void CopyWrap(byte* src, int n, ref int off)
        {
            int first = Math.Min(n, RingBytes - off);
            Buffer.MemoryCopy(src, _ring + off, first, first);
            if (first < n) Buffer.MemoryCopy(src + first, _ring, n - first, n - first);
            off = (off + n) % RingBytes;
        }

        static void AppendDirect(byte[] src, int n, long ticks)
        {
            try
            {
                _log.Write(Stamp(ticks));
                _log.Write("  ");
                _log.WriteLine(Encoding.UTF8.GetString(src, 0, n));
                _log.Flush();
            }
            catch { _dead = true; }
        }

        static string Stamp(long ticks)
        {
            double secs = (ticks - _epochSw) / (double)System.Diagnostics.Stopwatch.Frequency;
            return _epochWall.AddSeconds(secs).ToString("HH:mm:ss.fff");
        }

        public static int WatchdogSeconds = 30;

        static void Pump()
        {
            var chunk = new byte[64 * 1024];
            long wdBaseline = -1;
            double wdSilentSince = 0, wdNextAllowed = 0;
            TimeSpan wdPrevCpu = TimeSpan.Zero;
            double wdPrevCpuAt = 0;

            while (true)
            {
                bool more = false;
                try { more = MirrorOnce(chunk); } catch { }
                if (_stop && !more) break;

                try
                {
                    double now = System.Diagnostics.Stopwatch.GetTimestamp()
                               / (double)System.Diagnostics.Stopwatch.Frequency;

                    long cur = _mainThreadId != 0 ? Volatile.Read(ref _mainBeats)
                             : (_base != null ? Cursor : -1);
                    if (cur != wdBaseline)
                    {

                        wdBaseline = cur;
                        wdSilentSince = now;
                    }
                    else if (!_stop && cur >= 0
                             && now - wdSilentSince >= WatchdogSeconds
                             && now >= wdNextAllowed)
                    {
                        string sample = "";
                        try
                        {
                            var pr = System.Diagnostics.Process.GetCurrentProcess();
                            pr.Refresh();
                            var cpu = pr.TotalProcessorTime;
                            if (wdPrevCpuAt > 0)
                            {
                                double pct = (cpu - wdPrevCpu).TotalSeconds
                                           / (now - wdPrevCpuAt) * 100.0
                                           / Math.Max(1, Environment.ProcessorCount);
                                sample = "  cpu=" + pct.ToString("F0") + "% of the machine";
                            }
                            wdPrevCpu = cpu; wdPrevCpuAt = now;
                            sample += "  processMB=" + (pr.WorkingSet64 / (1024 * 1024))
                                    + "  threads=" + pr.Threads.Count;
                        }
                        catch { }
                        Write("WATCHDOG: nothing has been recorded for "
                            + (int)(now - wdSilentSince) + " s. The game process is "
                            + "alive but the main thread is not ticking -- a HANG, "
                            + "not a crash. (A game paused while minimised also "
                            + "looks like this; trust the line only if the game was "
                            + "on screen.)" + sample);
                        wdNextAllowed = now + 60;
                    }
                }
                catch { }

                if (!more) Thread.Sleep(100);
            }

            try { while (MirrorOnce(chunk)) { } } catch { }
            try { _log?.Flush(); _log?.Dispose(); } catch { }
            _log = null; _logFs = null;
        }

        static bool MirrorOnce(byte[] chunk)
        {
            if (_base == null || _log == null) return false;
            long cursor = Cursor, mirrored = Mirrored;
            if (cursor <= mirrored) return false;

            long behind = cursor - mirrored;
            if (behind > RingBytes)
            {

                long lost = behind - RingBytes;
                mirrored = Resync(cursor - RingBytes, cursor);
                _log.WriteLine("            [" + lost
                             + " byte(s) overwritten before the mirror caught up]");
            }

            while (mirrored < cursor)
            {
                int off = (int)(mirrored % RingBytes);

                if (_ring[off] != 0x01)
                {
                    long at = Resync(mirrored, cursor);
                    if (at <= mirrored) { mirrored = cursor; break; }
                    mirrored = at;
                    continue;
                }
                long ticks = ReadWrapLong(off + 1);
                int len = ReadWrapInt(off + 9);
                if (len < 0 || len > MaxRecord)
                {
                    long at = Resync(mirrored + 1, cursor);
                    if (at <= mirrored) { mirrored = cursor; break; }
                    mirrored = at;
                    continue;
                }
                if (mirrored + RecHeader + len > cursor) break;

                int p = (off + RecHeader) % RingBytes;
                int first = Math.Min(len, RingBytes - p);
                Buffer.BlockCopy(ToArray(_ring + p, first), 0, chunk, 0, first);
                if (first < len) Buffer.BlockCopy(ToArray(_ring, len - first), 0, chunk, first, len - first);

                _log.Write(Stamp(ticks));
                _log.Write("  ");
                _log.WriteLine(Encoding.UTF8.GetString(chunk, 0, len));

                mirrored += RecHeader + len;
            }

            _log.Flush();
            Mirrored = mirrored;

            return true;
        }

        static long Resync(long from, long cursor)
        {
            if (from < cursor - RingBytes) from = cursor - RingBytes;
            if (from < 0) from = 0;
            long limit = Math.Min(cursor, from + RingBytes);
            for (long q = from; q < limit; q++)
            {
                if (_ring[(int)(q % RingBytes)] != 0x01) continue;
                if (q + RecHeader > cursor) break;
                int len = ReadWrapInt((int)((q + 9) % RingBytes));
                if (len < 0 || len > MaxRecord) continue;
                long end = q + RecHeader + len;
                if (end > cursor) continue;
                if (end == cursor) return q;
                if (_ring[(int)(end % RingBytes)] == 0x01) return q;
            }
            return cursor;
        }

        static byte[] ToArray(byte* p, int n)
        {
            var a = new byte[n];
            for (int i = 0; i < n; i++) a[i] = p[i];
            return a;
        }

        static long ReadWrapLong(int off)
        {
            long v = 0;
            for (int i = 0; i < 8; i++) v |= (long)_ring[(off + i) % RingBytes] << (i * 8);
            return v;
        }

        static int ReadWrapInt(int off)
        {
            int v = 0;
            for (int i = 0; i < 4; i++) v |= _ring[(off + i) % RingBytes] << (i * 8);
            return v;
        }

        public static string SelfCost()
        {
            long calls = _selfCalls;
            if (calls == 0) return "0 lines";
            double us = (_selfTicks * 1_000_000.0 / System.Diagnostics.Stopwatch.Frequency) / calls;
            return calls + " lines, " + us.ToString("F2") + " us each"
                 + (UsingRing ? " (mapped ring)" : " (direct write)");
        }

        public static void Stop()
        {
            try
            {
                _stop = true;

                _mirror?.Join(5000);
            }
            catch { }

            bool joined = _mirror == null || !_mirror.IsAlive;
            if (!joined) return;

            lock (_gate)
            {
                _dead = true;
                try
                {
                    if (_base != null) { _view.SafeMemoryMappedViewHandle.ReleasePointer(); _base = null; }
                    _ring = null;
                    _view?.Dispose(); _mmf?.Dispose();
                }
                catch { }
                _view = null; _mmf = null;

                if (_mirror == null)
                {
                    try { _log?.Flush(); _log?.Dispose(); } catch { }
                    _log = null; _logFs = null;
                }
            }
        }

        public static int RecoverInto(string ringPath, string logPath)
        {
            try
            {
                var raw = File.ReadAllBytes(ringPath);
                if (raw.Length < HeaderBytes) return 0;

                long cursor = BitConverter.ToInt64(raw, 8);
                long mirrored = BitConverter.ToInt64(raw, 16);
                int ringBytes = BitConverter.ToInt32(raw, 24);
                long epochWall = BitConverter.ToInt64(raw, 32);
                long epochSw = BitConverter.ToInt64(raw, 40);
                long freq = BitConverter.ToInt64(raw, 48);
                if (ringBytes <= 0 || raw.Length < HeaderBytes + ringBytes) return 0;
                if (cursor <= mirrored) return 0;
                if (freq <= 0) freq = System.Diagnostics.Stopwatch.Frequency;

                var wall = new DateTime(epochWall);
                long behind = cursor - mirrored;
                long overwritten = 0;
                if (behind > ringBytes)
                {
                    overwritten = behind - ringBytes;
                    mirrored = ResyncRaw(raw, cursor - ringBytes, cursor, ringBytes);
                }

                int recovered = 0;
                using (var w = new StreamWriter(new FileStream(logPath, FileMode.Append,
                            FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false)))
                {
                    w.WriteLine("            [recovered from the crash ring: the mirror never wrote these]");
                    if (overwritten > 0)
                        w.WriteLine("            [" + overwritten + " earlier byte(s) had already been overwritten]");
                    while (mirrored < cursor)
                    {
                        int off = (int)(mirrored % ringBytes);
                        if (raw[HeaderBytes + off] != 0x01)
                        {
                            long at = ResyncRaw(raw, mirrored + 1, cursor, ringBytes);
                            if (at <= mirrored) break;
                            mirrored = at;
                            continue;
                        }
                        long ticks = 0;
                        for (int i = 0; i < 8; i++)
                            ticks |= (long)raw[HeaderBytes + (off + 1 + i) % ringBytes] << (i * 8);
                        int len = 0;
                        for (int i = 0; i < 4; i++)
                            len |= raw[HeaderBytes + (off + 9 + i) % ringBytes] << (i * 8);
                        if (len < 0 || len > MaxRecord)
                        {

                            long at2 = ResyncRaw(raw, mirrored + 1, cursor, ringBytes);
                            if (at2 <= mirrored) break;
                            mirrored = at2;
                            continue;
                        }
                        if (mirrored + RecHeader + len > cursor) break;

                        var txt = new byte[len];
                        for (int i = 0; i < len; i++)
                            txt[i] = raw[HeaderBytes + (off + RecHeader + i) % ringBytes];

                        double secs = (ticks - epochSw) / (double)freq;
                        w.Write(wall.AddSeconds(secs).ToString("HH:mm:ss.fff"));
                        w.Write("  ");
                        w.WriteLine(Encoding.UTF8.GetString(txt));
                        mirrored += RecHeader + len;
                        recovered++;
                    }
                }
                return recovered;
            }
            catch { return 0; }
        }

        static long ResyncRaw(byte[] raw, long from, long cursor, int ringBytes)
        {
            if (from < cursor - ringBytes) from = cursor - ringBytes;
            if (from < 0) from = 0;
            long limit = Math.Min(cursor, from + ringBytes);
            for (long q = from; q < limit; q++)
            {
                if (raw[HeaderBytes + (int)(q % ringBytes)] != 0x01) continue;
                if (q + RecHeader > cursor) break;
                int len = 0;
                for (int i = 0; i < 4; i++)
                    len |= raw[HeaderBytes + (int)((q + 9 + i) % ringBytes)] << (i * 8);
                if (len < 0 || len > MaxRecord) continue;
                long end = q + RecHeader + len;
                if (end > cursor) continue;
                if (end == cursor) return q;
                if (raw[HeaderBytes + (int)(end % ringBytes)] == 0x01) return q;
            }
            return cursor;
        }
    }
}
