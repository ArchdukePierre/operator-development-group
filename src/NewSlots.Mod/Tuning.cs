// ODG Operations - NewSlots
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NewSlots
{

    internal static class Tuning
    {
        internal sealed class Entry
        {
            internal float Scale = 1f;
            internal float OutMm = 0f;

            internal float CTL, CTR, CBR, CBL;
            internal Entry() { }
            internal Entry(float s, float o) { Scale = s; OutMm = o; }
            internal Entry(float s, float o, float tl, float tr, float br, float bl)
            { Scale = s; OutMm = o; CTL = tl; CTR = tr; CBR = br; CBL = bl; }
            internal Entry Copy() { return new Entry(Scale, OutMm, CTL, CTR, CBR, CBL); }
            internal bool AnyCorner { get { return CTL != 0f || CTR != 0f || CBR != 0f || CBL != 0f; } }

            internal float Corner(int i)
            { return i == 0 ? CTL : i == 1 ? CTR : i == 2 ? CBR : CBL; }
        }

        static readonly Dictionary<string, Entry> _map = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        static readonly Dictionary<uint, Dictionary<string, Entry>> _peers = new Dictionary<uint, Dictionary<string, Entry>>();
        internal static bool RemoteActive { get { return _peers.Count > 0; } }
        internal static int PeerCount { get { return _peers.Count; } }

        internal static int Count { get { return _map.Count; } }

        static string PathOf() { return Path.Combine(Art.ContentDir, "patch_tuning.json"); }

        internal static string Key(string garment, string socket, string patch)
        {
            return (garment ?? "?") + "|" + (socket ?? "?") + "|" + (patch ?? "?");
        }

        internal static Entry Get(string garment, string socket, string patch)
        {
            Entry e;
            if (garment != null && _map.TryGetValue(Key(garment, socket, patch), out e)) return e;
            if (_map.TryGetValue(patch ?? "?", out e)) return e;
            return new Entry();
        }

        internal static Entry Get(uint owner, string garment, string socket, string patch)
        {
            if (owner != 0)
            {
                Dictionary<string, Entry> peer;
                if (_peers.TryGetValue(owner, out peer))
                {
                    Entry e;
                    if (garment != null && peer.TryGetValue(Key(garment, socket, patch), out e)) return e;
                    if (peer.TryGetValue(patch ?? "?", out e)) return e;
                }
            }
            return Get(garment, socket, patch);
        }

        internal static void SetPeer(uint peer, Dictionary<string, Entry> incoming)
        {
            if (incoming == null || incoming.Count == 0) { _peers.Remove(peer); return; }
            _peers[peer] = incoming;
        }

        internal static void ClearPeers() { _peers.Clear(); }

        const char TAB = (char)9;
        const char NL = (char)10;

        internal static string Wire()
        {
            var sb = new StringBuilder();
            foreach (var kv in _map)
            {
                var e = kv.Value;

                int bar = kv.Key.LastIndexOf('|');
                if (bar >= 0)
                {
                    Entry dflt;
                    if (_map.TryGetValue(kv.Key.Substring(bar + 1), out dflt) &&
                        dflt.Scale == e.Scale && dflt.OutMm == e.OutMm && dflt.CTL == e.CTL &&
                        dflt.CTR == e.CTR && dflt.CBR == e.CBR && dflt.CBL == e.CBL)
                        continue;
                }
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                sb.Append(kv.Key.Replace(TAB, ' ').Replace(NL, ' ')).Append(TAB)
                  .Append(e.Scale.ToString("0.###", ci)).Append(TAB)
                  .Append(e.OutMm.ToString("0.###", ci)).Append(TAB)
                  .Append(e.CTL.ToString("0.##", ci)).Append(TAB)
                  .Append(e.CTR.ToString("0.##", ci)).Append(TAB)
                  .Append(e.CBR.ToString("0.##", ci)).Append(TAB)
                  .Append(e.CBL.ToString("0.##", ci)).Append(NL);
            }
            return sb.ToString();
        }

        internal static Dictionary<string, Entry> FromWire(string wire)
        {
            var d = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(wire)) return d;
            var lines = wire.Split(NL);
            for (int i = 0; i < lines.Length; i++)
            {
                var ln = lines[i];
                if (ln.Length == 0) continue;
                var parts = ln.Split(TAB);
                if (parts.Length < 3) continue;
                float sc, om;
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                if (!float.TryParse(parts[1], System.Globalization.NumberStyles.Float, ci, out sc)) continue;
                if (!float.TryParse(parts[2], System.Globalization.NumberStyles.Float, ci, out om)) continue;

                float bl = 0f, br = 0f, bt = 0f, bb = 0f;
                if (parts.Length >= 7)
                {
                    float.TryParse(parts[3], System.Globalization.NumberStyles.Float, ci, out bl);
                    float.TryParse(parts[4], System.Globalization.NumberStyles.Float, ci, out br);
                    float.TryParse(parts[5], System.Globalization.NumberStyles.Float, ci, out bt);
                    float.TryParse(parts[6], System.Globalization.NumberStyles.Float, ci, out bb);
                }
                d[parts[0]] = new Entry(sc, om, bl, br, bt, bb);
            }
            return d;
        }

        internal static void Set(string garment, string socket, string patch, float scale, float outMm)
        {
            var cur = Get(garment, socket, patch);
            Put(garment, socket, patch, new Entry(scale, outMm, cur.CTL, cur.CTR, cur.CBR, cur.CBL));
        }

        internal static void SetCorners(string garment, string socket, string patch, float tl, float tr, float br, float bl)
        {
            var cur = Get(garment, socket, patch);
            Put(garment, socket, patch, new Entry(cur.Scale, cur.OutMm, tl, tr, br, bl));
        }

        internal static void EnsureOwn(string garment, string socket, string patch)
        {
            if (garment == null || string.IsNullOrEmpty(patch)) return;
            string k = Key(garment, socket, patch);
            if (_map.ContainsKey(k)) return;
            Entry e;
            _map[k] = _map.TryGetValue(patch, out e) ? e.Copy() : new Entry();
        }

        static void Put(string garment, string socket, string patch, Entry e)
        {
            _map[Key(garment, socket, patch)] = e;

            _map[patch] = e.Copy();

            if (garment == null)
                Seat.EachWornOf(patch, (g, sck) => { _map[Key(g, sck, patch)] = e.Copy(); });
        }

        internal static void Load()
        {
            _map.Clear();
            string p = PathOf();
            if (!File.Exists(p)) { NewSlotsMod.Log("tuning: none saved yet"); return; }
            try
            {
                string json = File.ReadAllText(p);
                int i = 0;
                while (true)
                {
                    int q = json.IndexOf('"', i);
                    if (q < 0) break;
                    int q2 = json.IndexOf('"', q + 1);
                    if (q2 < 0) break;
                    string key = json.Substring(q + 1, q2 - q - 1);
                    int open = json.IndexOf('{', q2);
                    if (open < 0) break;
                    int close = json.IndexOf('}', open);
                    if (close < 0) break;
                    string body = json.Substring(open, close - open + 1);
                    _map[key] = new Entry(Num(body, "scale", 1f), Num(body, "out", 0f),
                                          Num(body, "ctl", 0f), Num(body, "ctr", 0f), Num(body, "cbr", 0f), Num(body, "cbl", 0f));
                    i = close + 1;
                }
                NewSlotsMod.Log("tuning: " + _map.Count + " entr(ies) loaded");
            }
            catch (Exception e) { NewSlotsMod.Log("tuning load failed: " + e.Message); }
        }

        static float Num(string body, string key, float dflt)
        {
            int k = body.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (k < 0) return dflt;
            int c = body.IndexOf(':', k);
            if (c < 0) return dflt;
            int e = c + 1;
            while (e < body.Length && (char.IsDigit(body[e]) || body[e] == '.' || body[e] == '-' || body[e] == ' ')) e++;
            float v;
            if (float.TryParse(body.Substring(c + 1, e - c - 1).Trim(),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v))
                return v;
            return dflt;
        }

        internal static bool Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("{\n");
                bool first = true;
                foreach (var kv in _map)
                {
                    if (!first) sb.Append(",\n");
                    first = false;
                    var ci = System.Globalization.CultureInfo.InvariantCulture;
                    var e = kv.Value;
                    sb.Append(" \"").Append(kv.Key.Replace("\"", "'")).Append("\": {\"scale\": ")
                      .Append(e.Scale.ToString("0.###", ci))
                      .Append(", \"out\": ")
                      .Append(e.OutMm.ToString("0.###", ci));

                    if (e.AnyCorner)
                        sb.Append(", \"ctl\": ").Append(e.CTL.ToString("0.##", ci))
                          .Append(", \"ctr\": ").Append(e.CTR.ToString("0.##", ci))
                          .Append(", \"cbr\": ").Append(e.CBR.ToString("0.##", ci))
                          .Append(", \"cbl\": ").Append(e.CBL.ToString("0.##", ci));
                    sb.Append("}");
                }
                sb.Append("\n}\n");
                File.WriteAllText(PathOf(), sb.ToString());
                NewSlotsMod.Log("tuning saved: " + _map.Count + " entr(ies) -> " + PathOf());
                return true;
            }
            catch (Exception e) { NewSlotsMod.Log("tuning save failed: " + e.Message); return false; }
        }
    }
}
