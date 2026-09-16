// ODG Operations - M107
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using UnityEngine;

namespace M107
{

    internal static class MagFollow
    {
        const int SettleFrames = 10;
        const float StillEpsilon = 0.001f;

        class Follower
        {
            internal Transform Ours;
            internal Transform Donor;
            internal string Key;
            internal Vector3 LastProbe;
            internal int StillFrames;
            internal bool Probing;
        }

        static readonly List<Follower> _follow = new List<Follower>();

        static readonly Dictionary<string, KeyValuePair<Vector3, Quaternion>> _offset =
            new Dictionary<string, KeyValuePair<Vector3, Quaternion>>(StringComparer.Ordinal);

        static Vector3 _heldAt;
        static bool _haveHeld;
        static int _logged;

        internal static void Reset()
        {
            _follow.Clear();
            _offset.Clear();
            _haveHeld = false;
        }

        internal static void Bind(Transform ours, Transform donor)
        {
            try
            {
                if (ours == null || donor == null) return;
                string key; try { key = ours.name; } catch { return; }
                if (string.IsNullOrEmpty(key)) return;

                for (int i = 0; i < _follow.Count; i++)
                {
                    var e = _follow[i];
                    if (e.Ours != ours) continue;
                    if (e.Donor != donor)
                    {
                        e.Donor = donor;
                        e.Probing = !_offset.ContainsKey(key);
                        e.StillFrames = 0;
                    }
                    return;
                }

                _follow.Add(new Follower
                {
                    Ours = ours,
                    Donor = donor,
                    Key = key,
                    Probing = !_offset.ContainsKey(key),
                });
            }
            catch { }
        }

        internal static void Tick()
        {
            try
            {
                if (_follow.Count == 0) return;

                if (_haveHeld && (Alignment.HeldPos - _heldAt).sqrMagnitude > 1e-8f)
                {
                    _offset.Clear();
                    for (int i = 0; i < _follow.Count; i++) { _follow[i].Probing = true; _follow[i].StillFrames = 0; }
                    _haveHeld = false;
                    M107Mod.Log("mag follow: rifle placement changed - re-measuring the magazine offsets");
                }

                for (int i = _follow.Count - 1; i >= 0; i--)
                {
                    var e = _follow[i];

                    if (e.Ours == null) { _follow.RemoveAt(i); continue; }
                    if (e.Donor == null) continue;

                    KeyValuePair<Vector3, Quaternion> off;
                    if (_offset.TryGetValue(e.Key, out off))
                    {
                        e.Ours.position = e.Donor.position + e.Donor.rotation * off.Key;
                        e.Ours.rotation = e.Donor.rotation * off.Value;
                        continue;
                    }

                    TryMeasure(e);
                }
            }
            catch { }
        }

        static void TryMeasure(Follower e)
        {
            try
            {
                if (!e.Probing) return;

                if (!SeatedInAMagwell(e.Donor)) { e.StillFrames = 0; return; }

                var frame = e.Ours.parent;
                if (frame == null) return;
                var probe = frame.InverseTransformPoint(e.Donor.position);
                if ((probe - e.LastProbe).sqrMagnitude > StillEpsilon * StillEpsilon)
                {
                    e.LastProbe = probe;
                    e.StillFrames = 0;
                    return;
                }
                if (++e.StillFrames < SettleFrames) return;

                var inv = Quaternion.Inverse(e.Donor.rotation);
                var offPos = inv * (e.Ours.position - e.Donor.position);
                var offRot = inv * e.Ours.rotation;
                _offset[e.Key] = new KeyValuePair<Vector3, Quaternion>(offPos, offRot);
                e.Probing = false;

                if (!_haveHeld) { _heldAt = Alignment.HeldPos; _haveHeld = true; }

                if (_logged++ < 6)
                    M107Mod.Log("mag follow: '" + e.Key + "' locked to '" + e.Donor.name + "' at a fixed " +
                                BarrettArt.F(offPos) + " in its own frame (" +
                                Vector3.Distance(e.Ours.position, e.Donor.position).ToString("F3") +
                                "m apart), measured after " + SettleFrames + " still frames");
            }
            catch { }
        }

        static bool SeatedInAMagwell(Transform donor)
        {
            try
            {
                var p = donor.parent; int g = 0;
                while (p != null && g++ < 12)
                {
                    string n; try { n = p.name; } catch { return false; }
                    if (n != null && n.StartsWith("MOD_MAGAZINE", StringComparison.OrdinalIgnoreCase)) return true;
                    p = p.parent;
                }
            }
            catch { }
            return false;
        }
    }
}
