// ODG Operations - NewSlots
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Security.Cryptography;

namespace NewSlots
{

    internal static class Pack
    {
        static readonly byte[] MAGIC = new byte[] { (byte)'O', (byte)'D', (byte)'G', (byte)'P', (byte)'A', (byte)'K', 1, 0 };

        struct Entry { internal long Off; internal int Len; }

        static Dictionary<string, Entry> _idx;
        static string _pakPath;
        static bool _tried;

        internal static string PakPath
        {
            get { try { return Path.Combine(Art.ContentDir, "content.odgpak"); } catch { return null; } }
        }

        static void EnsureOpen()
        {
            if (_tried) return;
            _tried = true;
            _pakPath = PakPath;
            if (_pakPath == null || !File.Exists(_pakPath)) { NewSlotsMod.Log("pack: no content.odgpak"); return; }

            try
            {
                using (var fs = File.OpenRead(_pakPath))
                {
                    var hdr = ReadExactly(fs, MAGIC.Length + 4);
                    for (int i = 0; i < MAGIC.Length; i++)
                        if (hdr[i] != MAGIC[i]) { NewSlotsMod.Log("pack: bad magic in content.odgpak"); return; }
                    int idxBlobLen = BitConverter.ToInt32(hdr, MAGIC.Length);
                    var idxBlob = ReadExactly(fs, idxBlobLen);
                    ParseIndex(Gcm(idxBlob, 0, idxBlob.Length));
                }
                NewSlotsMod.Log("pack: opened content.odgpak, " + (_idx == null ? 0 : _idx.Count) + " entries");
            }
            catch (Exception e) { NewSlotsMod.Log("pack: open failed: " + e.GetType().Name + ": " + e.Message); _idx = null; }
        }

        static void ParseIndex(byte[] p)
        {
            var d = new Dictionary<string, Entry>(StringComparer.Ordinal);
            int q = 0;
            int count = RI(p, ref q);
            for (int i = 0; i < count; i++)
            {
                int nl = RI(p, ref q);
                string name = Encoding.UTF8.GetString(p, q, nl); q += nl;
                long off = RL(p, ref q);
                int len = RI(p, ref q);
                d[name] = new Entry { Off = off, Len = len };
            }
            _idx = d;
        }

        internal static bool Has(string name)
        {
            EnsureOpen();
            return _idx != null && name != null && _idx.ContainsKey(name);
        }

        internal static byte[] ReadBytes(string name)
        {
            EnsureOpen();
            if (_idx == null || name == null || !_idx.TryGetValue(name, out var e)) return null;
            try
            {
                using (var fs = File.OpenRead(_pakPath))
                {
                    fs.Seek(e.Off, SeekOrigin.Begin);
                    var blob = ReadExactly(fs, 12 + e.Len + 16);
                    return Gcm(blob, 0, blob.Length);
                }
            }
            catch (Exception ex) { NewSlotsMod.Log("pack: read '" + name + "' failed: " + ex.Message); return null; }
        }

        static byte[] Gcm(byte[] buf, int off, int len)
        {
            int clen = len - 12 - 16;
            if (clen < 0) throw new InvalidDataException("blob too short");
            var nonce = new byte[12]; Array.Copy(buf, off, nonce, 0, 12);
            var cipher = new byte[clen]; Array.Copy(buf, off + 12, cipher, 0, clen);
            var tag = new byte[16]; Array.Copy(buf, off + 12 + clen, tag, 0, 16);
            var plain = new byte[clen];
            using (var gcm = new AesGcm(ContentKey.Bytes))
                gcm.Decrypt(nonce, cipher, tag, plain);
            return plain;
        }

        static byte[] ReadExactly(Stream s, int n)
        {
            var b = new byte[n]; int read = 0;
            while (read < n) { int r = s.Read(b, read, n - read); if (r <= 0) throw new EndOfStreamException(); read += r; }
            return b;
        }
        static int RI(byte[] b, ref int p) { int v = b[p] | (b[p + 1] << 8) | (b[p + 2] << 16) | (b[p + 3] << 24); p += 4; return v; }
        static long RL(byte[] b, ref int p) { long v = BitConverter.ToInt64(b, p); p += 8; return v; }
    }
}
