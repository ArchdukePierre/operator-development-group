// ODG Operations - HSR
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using MelonLoader;
using UnityEngine;

namespace HSR
{

    internal static class WeaponAudio
    {
        static MelonPreferences_Entry<float> _shotPitch, _shotGain, _boltPitch, _boltGain;
        static MelonPreferences_Entry<bool> _enabled;

        internal static void Init(MelonPreferences_Category cat)
        {
            _enabled = cat.CreateEntry("RetuneAudio", true);

            _shotPitch = cat.CreateEntry("ShotPitch", 0.82f);

            _shotGain = cat.CreateEntry("ShotVolume", 1.6f);

            _boltPitch = cat.CreateEntry("BoltPitch", 0.78f);
            _boltGain = cat.CreateEntry("BoltVolume", 1.5f);
        }

        static bool Enabled { get { try { return _enabled != null && _enabled.Value; } catch { return false; } } }
        static float F(MelonPreferences_Entry<float> e, float dflt)
        {
            try { return e == null ? dflt : e.Value; } catch { return dflt; }
        }

        static readonly string[] BoltAudio = { "BoltOpen", "BoltClose", "BoltPull", "BoltPush" };

        static int _reported;

        internal static void Apply(Il2Cpp.WeaponV3 w, MelonLogger.Instance log)
        {
            if (!Enabled || w == null) return;
            try
            {
                float sp = F(_shotPitch, 0.82f), sg = F(_shotGain, 1.6f);
                float bp = F(_boltPitch, 0.78f), bg = F(_boltGain, 1.5f);
                int shots = 0, bolts = 0;

                try
                {
                    var sfx = w.GetComponentInChildren<Il2Cpp.WeaponSFX>(true);
                    if (sfx != null)
                    {
                        shots += Tune(Src(() => sfx.ShotClose), sp, sg);
                        shots += Tune(Src(() => sfx.ShotFar), sp, sg);
                        shots += Tune(Src(() => sfx.Suppressed), sp, sg);
                        shots += Tune(Src(() => sfx.SuppressedFar), sp, sg);
                    }
                }
                catch { }

                try
                {
                    var all = w.transform.GetComponentsInChildren<AudioSource>(true);
                    if (all != null)
                    {
                        for (int i = 0; i < all.Length; i++)
                        {
                            var a = all[i];
                            if (a == null || a.gameObject == null) continue;
                            var n = a.gameObject.name;
                            for (int k = 0; k < BoltAudio.Length; k++)
                            {
                                if (!string.Equals(n, BoltAudio[k], StringComparison.OrdinalIgnoreCase)) continue;
                                bolts += Tune(a, bp, bg);
                                break;
                            }
                        }
                    }
                }
                catch { }

                if (_reported++ < 3 && log != null)
                    log.Msg("audio: retuned {0} shot source(s) to pitch {1:F2} x{2:F2}, "
                          + "{3} bolt source(s) to pitch {4:F2} x{5:F2}.",
                            shots, sp, sg, bolts, bp, bg);
            }
            catch { }
        }

        static AudioSource Src(Func<Il2Cpp.WeaponSfxAudioSource> get)
        {
            try { var w = get(); return w == null ? null : w.audioSource; } catch { return null; }
        }

        static readonly System.Collections.Generic.Dictionary<int, float> _baseVol =
            new System.Collections.Generic.Dictionary<int, float>();

        static int Tune(AudioSource a, float pitch, float gain)
        {
            if (a == null) return 0;
            try
            {
                int id = a.GetInstanceID();
                float base_;
                if (!_baseVol.TryGetValue(id, out base_)) { base_ = a.volume; _baseVol[id] = base_; }

                a.pitch = pitch;
                float v = base_ * gain;
                a.volume = v > 1f ? 1f : v;
                return 1;
            }
            catch { return 0; }
        }
    }
}
