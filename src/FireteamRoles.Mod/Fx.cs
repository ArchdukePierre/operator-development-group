// ODG Operations - FireteamRoles
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using UnityEngine;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace FireteamRoles
{

    internal static class Fx
    {
        static GameObject _boomPrefab;
        static Il2CppReferenceArray<AudioClip> _clips;
        static string _source = "";
        static bool _searched;

        sealed class Live { internal GameObject Go; internal float Die; }
        static readonly List<Live> _live = new List<Live>();

        static void Find()
        {
            if (_searched) return;
            _searched = true;
            try
            {
                GameObject best = null;
                int bestScore = -1;
                var gs = Resources.FindObjectsOfTypeAll<Il2Cpp.GrenadeExplosive>();
                if (gs != null)
                    for (int i = 0; i < gs.Length; i++)
                    {
                        var g = gs[i];
                        if (g == null) continue;
                        try
                        {
                            if (g.isFlashbang) continue;
                            var eff = g.explosionEffect;
                            if (eff == null) continue;
                            int score = 0;
                            var clips = g.explosionClips;
                            if (clips != null && clips.Length > 0) score += 3;
                            if (g.radius > 3f) score += 2;
                            string n = (g.gameObject.name ?? "").ToLowerInvariant();
                            if (n.Contains("frag") || n.Contains("m67") || n.Contains("he")) score += 4;
                            if (score > bestScore)
                            {
                                bestScore = score; best = eff; _clips = clips;
                                _source = "grenade '" + g.gameObject.name + "'";
                            }
                        }
                        catch { }
                    }

                if (best == null)
                {
                    var mines = Resources.FindObjectsOfTypeAll<Il2Cpp.Landmine>();
                    if (mines != null)
                        for (int i = 0; i < mines.Length && best == null; i++)
                        {
                            try { if (mines[i] != null && mines[i].ExplosionVFX != null) { best = mines[i].ExplosionVFX; _source = "landmine"; } } catch { }
                        }
                }
                if (best == null)
                {
                    var sc = Resources.FindObjectsOfTypeAll<Il2Cpp.SlapChargeExplosive>();
                    if (sc != null)
                        for (int i = 0; i < sc.Length && best == null; i++)
                        {
                            try { if (sc[i] != null && sc[i].ExplosionVFX != null) { best = sc[i].ExplosionVFX; _source = "slap charge"; } } catch { }
                        }
                }
                _boomPrefab = best;
                Log.Msg("explosion effect: " + (best != null ? "'" + best.name + "' from the " + _source : "none found in memory, using the synthesised boom") +
                        ", " + (_clips != null ? _clips.Length : 0) + " clip(s)");
            }
            catch (Exception e) { Log.Detail("explosion effect search failed: " + e.Message); }
        }

        internal static void Explosion(Vector3 pos, float scale, bool alsoAtListener)
        {
            Find();
            bool played = false;
            try
            {
                if (_boomPrefab != null)
                {
                    var go = UnityEngine.Object.Instantiate(_boomPrefab, pos, Quaternion.identity);
                    go.name = "odg_boom";
                    if (Mathf.Abs(scale - 1f) > 0.01f) go.transform.localScale = go.transform.localScale * scale;
                    go.SetActive(true);
                    try
                    {
                        var pss = go.GetComponentsInChildren<ParticleSystem>(true);
                        if (pss != null) for (int i = 0; i < pss.Length; i++) { try { if (pss[i] != null && !pss[i].isPlaying) pss[i].Play(true); } catch { } }
                    }
                    catch { }
                    try
                    {
                        var vfx = go.GetComponentsInChildren<UnityEngine.VFX.VisualEffect>(true);
                        if (vfx != null) for (int i = 0; i < vfx.Length; i++) { try { if (vfx[i] != null) vfx[i].Play(); } catch { } }
                    }
                    catch { }
                    _live.Add(new Live { Go = go, Die = Time.realtimeSinceStartup + 12f });
                    played = true;
                }
            }
            catch (Exception e) { Log.Detail("explosion effect failed: " + e.Message); }

            AudioClip clip = null;
            try { if (_clips != null && _clips.Length > 0) clip = _clips[UnityEngine.Random.Range(0, _clips.Length)]; } catch { clip = null; }
            if (clip == null) { try { clip = Synth.Boom(); } catch { clip = null; } }
            if (clip != null)
            {
                try { AudioSource.PlayClipAtPoint(clip, pos, 1f); } catch { }
                if (alsoAtListener)
                {
                    try
                    {
                        var cam = Camera.main;
                        if (cam != null && Vector3.Distance(cam.transform.position, pos) > 25f)
                            AudioSource.PlayClipAtPoint(clip, cam.transform.position + cam.transform.forward * 2f, 0.35f);
                    }
                    catch { }
                }
            }
            Log.Fired(played ? "fx-explosion" : "fx-explosion-fallback");
        }

        internal static void Tick()
        {
            if (_live.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var l = _live[i];
                bool gone = false;
                try { gone = l.Go == null; } catch { gone = true; }
                if (!gone && now < l.Die) continue;
                _live.RemoveAt(i);
                try { if (l.Go != null) UnityEngine.Object.Destroy(l.Go); } catch { }
            }
        }

        internal static void Reset()
        {
            for (int i = 0; i < _live.Count; i++) { try { if (_live[i].Go != null) UnityEngine.Object.Destroy(_live[i].Go); } catch { } }
            _live.Clear();

            _searched = false; _boomPrefab = null; _clips = null;
        }
    }
}
