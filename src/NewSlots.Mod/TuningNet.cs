// ODG Operations - NewSlots
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Text;

namespace NewSlots
{

    internal static class TuningNet
    {
        const string TAG = "#ODGPT1#";
        const int MAX_B64 = 6000;

        internal static bool Enabled = true;
        static bool _broken;
        static string _lastSentDigest;
        static readonly System.Collections.Generic.Dictionary<uint, string> _lastSeen =
            new System.Collections.Generic.Dictionary<uint, string>();

        static void Break(string why, Exception e)
        {
            if (_broken) return;
            _broken = true;
            NewSlotsMod.Log("seating sharing DISABLED for this session (" + why + "): " + (e == null ? "" : e.Message));
            NewSlotsMod.FlushLog();
        }

        internal static bool Live { get { return Enabled && !_broken; } }

        internal static string Attach(string jsonMods)
        {
            if (!Live || string.IsNullOrEmpty(jsonMods)) return jsonMods;
            try
            {
                if (Tuning.Count == 0) return jsonMods;
                if (jsonMods.IndexOf(TAG, StringComparison.Ordinal) >= 0) return jsonMods;

                string wire = Tuning.Wire();
                if (string.IsNullOrEmpty(wire)) return jsonMods;
                string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(wire));
                if (b64.Length > MAX_B64)
                {
                    NewSlotsMod.Log("seating too large to share (" + b64.Length + " chars); outfit sent unchanged");
                    return jsonMods;
                }

                string spliced = Splice(jsonMods, TAG + b64);
                if (spliced == null) return jsonMods;

                if (b64 != _lastSentDigest)
                {
                    _lastSentDigest = b64;
                    NewSlotsMod.Log("sharing seating: " + Tuning.Count + " entr(ies), " + b64.Length + " chars");
                    NewSlotsMod.FlushLog();
                }
                return spliced;
            }
            catch (Exception e) { Break("attach", e); return jsonMods; }
        }

        static string Splice(string json, string entry)
        {
            const string A = "\"CharacterModName\":[";
            const string B = "\"CharacterModParentName\":[";
            int ia = json.IndexOf(A, StringComparison.Ordinal);
            int ib = json.IndexOf(B, StringComparison.Ordinal);
            if (ia < 0 || ib < 0) return null;

            int ea = json.IndexOf(']', ia + A.Length);
            int eb = json.IndexOf(']', ib + B.Length);
            if (ea < 0 || eb < 0) return null;

            bool aEmpty = json.Substring(ia + A.Length, ea - (ia + A.Length)).Trim().Length == 0;
            bool bEmpty = json.Substring(ib + B.Length, eb - (ib + B.Length)).Trim().Length == 0;

            string addA = (aEmpty ? "" : ",") + "\"" + entry + "\"";
            string addB = (bEmpty ? "" : ",") + "\"None\"";

            if (ea < eb)
                return json.Substring(0, ea) + addA + json.Substring(ea, eb - ea) + addB + json.Substring(eb);
            return json.Substring(0, eb) + addB + json.Substring(eb, ea - eb) + addA + json.Substring(ea);
        }

        internal static string Detach(string jsonMods, uint peer, bool local)
        {
            if (string.IsNullOrEmpty(jsonMods)) return jsonMods;
            int i = jsonMods.IndexOf(TAG, StringComparison.Ordinal);
            if (i < 0) return jsonMods;

            try
            {
                int end = jsonMods.IndexOf('"', i);
                if (end < 0) return jsonMods;
                string b64 = jsonMods.Substring(i + TAG.Length, end - (i + TAG.Length));

                if (local || b64 == _lastSentDigest)
                    return Strip(jsonMods, i, end);

                string seen;
                if (Live && (!_lastSeen.TryGetValue(peer, out seen) || seen != b64))
                {
                    _lastSeen[peer] = b64;
                    string wire = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
                    var map = Tuning.FromWire(wire);
                    Tuning.SetPeer(peer, map);
                    NewSlotsMod.Log("received seating from player " + peer + ": " + map.Count +
                                    " entr(ies) - applied to that player's worn patches only");
                    NewSlotsMod.FlushLog();
                }

                return Strip(jsonMods, i, end);
            }
            catch (Exception e) { Break("detach", e); return jsonMods; }
        }

        static string Strip(string json, int tagAt, int quoteEnd)
        {
            try
            {
                int qs = json.LastIndexOf('"', tagAt);
                if (qs < 0) return json;
                int cut = qs, cutEnd = quoteEnd + 1;
                if (cut > 0 && json[cut - 1] == ',') cut--;
                else if (cutEnd < json.Length && json[cutEnd] == ',') cutEnd++;
                string outp = json.Substring(0, cut) + json.Substring(cutEnd);

                const string B = "\"CharacterModParentName\":[";
                int ib = outp.IndexOf(B, StringComparison.Ordinal);
                if (ib >= 0)
                {
                    int eb = outp.IndexOf(']', ib + B.Length);
                    if (eb > 0)
                    {
                        string body = outp.Substring(ib + B.Length, eb - (ib + B.Length));
                        int last = body.LastIndexOf("\"None\"", StringComparison.Ordinal);
                        if (last >= 0 && body.Substring(last).Trim() == "\"None\"")
                        {
                            int s2 = ib + B.Length + last, e2 = ib + B.Length + body.Length;
                            if (s2 > 0 && outp[s2 - 1] == ',') s2--;
                            outp = outp.Substring(0, s2) + outp.Substring(e2);
                        }
                    }
                }
                return outp;
            }
            catch { return json; }
        }

        internal static void Solo()
        {
            if (!Tuning.RemoteActive) return;
            Tuning.ClearPeers();
            _lastSeen.Clear();
            NewSlotsMod.Log("shared seating ended - back to this machine's own patch_tuning.json");
            NewSlotsMod.FlushLog();
        }
    }
}
