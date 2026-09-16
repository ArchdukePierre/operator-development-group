// ODG Operations - FireteamRoles
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace FireteamRoles
{

    internal static class Radar
    {
        sealed class Blip { internal Transform T; internal Il2Cpp.Health H; }

        static readonly List<Blip> _bots = new List<Blip>();
        static float _nextScan, _nextSlowScan;
        static Texture2D _disc, _dot, _ring;
        static GUIStyle _lbl;

        internal static bool Active
        {
            get
            {
                if (!Cfg.JtacRadar.Value || !State.Enabled || State.MyRole != Role.Jtac) return false;
                var me = State.Me;
                return me != null && me.Live && !Downed.AmDowned;
            }
        }

        internal static void Reset(string why) { _bots.Clear(); _nextScan = 0f; }

        internal static void Tick()
        {
            if (!Active) { if (_bots.Count > 0) _bots.Clear(); return; }
            float now = Time.realtimeSinceStartup;
            if (now < _nextScan) return;
            _nextScan = now + 2f;

            _bots.Clear();
            try
            {
                var teams = Il2Cpp.TeamManager.OperatorTeams;
                if (teams != null)
                    for (int teamId = 0; teamId <= 8; teamId++)
                    {
                        Il2Cpp.TeamRuntimeData trd = null;
                        try { if (!teams.TryGetValue(teamId, out trd)) continue; } catch { continue; }
                        if (trd == null) continue;
                        Il2CppSystem.Collections.Generic.List<Il2Cpp.BrainAI> bots = null;
                        try { bots = trd.TeamBots; } catch { }
                        if (bots == null) continue;
                        for (int i = 0; i < bots.Count; i++) Consider(bots[i]);
                    }
            }
            catch (Exception e) { Log.Detail("radar: team lists failed: " + e.Message); }

            if (_bots.Count == 0 && now >= _nextSlowScan)
            {
                _nextSlowScan = now + 6f;
                try
                {
                    var all = Resources.FindObjectsOfTypeAll<Il2Cpp.BrainAI>();
                    if (all != null)
                        for (int i = 0; i < all.Length; i++)
                        {
                            var b = all[i]; if (b == null) continue;
                            try { if (!b.gameObject.scene.IsValid() || !b.gameObject.activeInHierarchy) continue; } catch { continue; }
                            Consider(b);
                        }
                    Log.Detail("radar: scene scan found " + _bots.Count + " bot(s)");
                }
                catch (Exception e) { Log.Detail("radar: scene scan failed: " + e.Message); }
            }
        }

        static void Consider(Il2Cpp.BrainAI b)
        {
            if (b == null) return;
            try
            {
                Il2Cpp.Health h = null;
                try { h = b.health; } catch { }
                if (h == null) { try { h = b.GetComponent<Il2Cpp.Health>(); } catch { } }
                if (h != null && h.isDead) return;
                _bots.Add(new Blip { T = b.transform, H = h });
            }
            catch { }
        }

        static Texture2D Disc(int size, float inner, Color c)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Il2CppStructArray<Color32>(size * size);
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - r, dy = y + 0.5f - r;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / r;
                    float a = Mathf.Clamp01((1f - d) * r) * (d >= inner ? 1f : 0f);
                    if (inner > 0f) a *= Mathf.Clamp01((d - inner) * r);
                    px[y * size + x] = new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), (byte)(a * c.a * 255));
                }
            t.SetPixels32(px); t.Apply(false, false);
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }

        static void Ensure()
        {
            if (_disc == null) _disc = Disc(128, 0f, new Color(0.02f, 0.04f, 0.03f, 0.72f));
            if (_ring == null) _ring = Disc(128, 0.94f, new Color(0.55f, 0.70f, 0.60f, 0.9f));
            if (_dot == null) _dot = Disc(16, 0f, Color.white);
            if (_lbl == null)
            {
                _lbl = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.UpperLeft };
                _lbl.normal.textColor = new Color(0.80f, 0.88f, 0.82f);
            }
        }

        internal static void Draw()
        {
            if (!Active) return;
            var me = State.Me;
            if (me == null || me.Root == null) return;
            Ensure();

            float R = Mathf.Clamp(Cfg.JtacRadarSize.Value, 50f, 220f);
            float range = Mathf.Clamp(Cfg.JtacRadarRange.Value, 15f, 400f);
            float cx = 18f + R, cy = Screen.height - 18f - R;

            GUI.DrawTexture(new Rect(cx - R, cy - R, 2f * R, 2f * R), _disc);
            GUI.DrawTexture(new Rect(cx - R, cy - R, 2f * R, 2f * R), _ring);

            float yaw = 0f;
            try { var cam = Camera.main; if (cam != null) yaw = cam.transform.eulerAngles.y; } catch { }
            Vector3 origin = me.Root.position;
            var rot = Quaternion.Euler(0f, -yaw, 0f);
            Vector3 north = rot * Vector3.forward;
            GUI.color = new Color(0.9f, 0.9f, 0.85f, 0.8f);
            GUI.DrawTexture(new Rect(cx + north.x * (R - 6f) - 3f, cy - north.z * (R - 6f) - 3f, 6f, 6f), _dot);

            GUI.DrawTexture(new Rect(cx - 3f, cy - 3f, 6f, 6f), _dot);

            int shown = 0;
            for (int i = 0; i < _bots.Count; i++)
            {
                var b = _bots[i];
                Vector3 p;
                try
                {
                    if (b.T == null) continue;
                    if (b.H != null && b.H.isDead) continue;
                    p = b.T.position;
                }
                catch { continue; }
                Vector3 d = p - origin;
                float dy = d.y; d.y = 0f;
                float dist = d.magnitude;
                Vector3 l = rot * d;
                float f = dist / range;
                bool beyond = f > 1f;
                if (beyond) l = l / Mathf.Max(0.001f, f);
                float px = cx + (l.x / range) * R, py = cy - (l.z / range) * R;
                float size = beyond ? 4f : Mathf.Lerp(9f, 4f, Mathf.Clamp01(f));
                GUI.color = beyond ? new Color(0.9f, 0.25f, 0.2f, 0.45f) : new Color(1f, 0.22f, 0.18f, 0.95f);
                GUI.DrawTexture(new Rect(px - size * 0.5f, py - size * 0.5f, size, size), _dot);
                if (!beyond && Mathf.Abs(dy) > 3f)
                {
                    GUI.color = new Color(1f, 0.6f, 0.5f, 0.9f);
                    GUI.Label(new Rect(px + 4f, py - 9f, 12f, 12f), dy > 0f ? "^" : "v", _lbl);
                }
                shown++;
            }

            if (Cfg.JtacRadarShowTeam.Value)
            {
                try
                {
                    foreach (var kv in State.Peers)
                    {
                        var p = kv.Value;
                        if (p == null || p.Id == Net.Self || !p.Live || p.Root == null) continue;
                        Vector3 d = p.Root.position - origin; d.y = 0f;
                        float dist = d.magnitude;
                        Vector3 l = rot * d;
                        float f = dist / range;
                        if (f > 1f) l = l / f;
                        float px = cx + (l.x / range) * R, py = cy - (l.z / range) * R;
                        GUI.color = new Color(0.35f, 0.65f, 1f, f > 1f ? 0.45f : 0.95f);
                        GUI.DrawTexture(new Rect(px - 3f, py - 3f, 6f, 6f), _dot);
                    }
                }
                catch { }
            }
            GUI.color = Color.white;

            string strikes = Jtac.MyStrikes >= 0 ? "   HELLFIRE x" + Jtac.MyStrikes : "";
            GUI.Label(new Rect(cx - R, cy - R - 18f, 2f * R + 120f, 16f),
                      "ENEMY " + shown + "   " + range.ToString("0") + " m" + strikes, _lbl);
        }
    }
}
