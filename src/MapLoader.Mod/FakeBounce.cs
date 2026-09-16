// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using MelonLoader;
using UnityEngine;

namespace MapLoader
{

    public static class FakeBounce
    {
        static MelonLogger.Instance _log;
        public static void Init(MelonLogger.Instance log) { _log = log; }

        const int Count = 8;

        const float Reach = 14f;

        const float Lift = 0.45f;

        public static float Strength = 4f;
        const float MinLumen = 0.5f;
        const float MaxLumen = 12f;

        const float Follow = 6f;

        static readonly Color BounceTint = new Color(0.80f, 0.76f, 0.70f);

        static readonly GameObject[] _go = new GameObject[Count];
        static readonly Light[] _light = new Light[Count];
        static readonly UnityEngine.Rendering.HighDefinition.HDAdditionalLightData[] _hd
            = new UnityEngine.Rendering.HighDefinition.HDAdditionalLightData[Count];
        static readonly Vector3[] _target = new Vector3[Count];
        static readonly float[] _want = new float[Count];

        static readonly float[] _lumen = new float[Count];

        static Camera _cam;
        static Camera Cam
        {
            get
            {
                if (_cam == null) { try { _cam = Camera.main; } catch { } }
                return _cam;
            }
        }

        static bool _built;
        public static bool Enabled = true;

        static Vector3 _moonDir = new Vector3(0f, -1f, 0f);
        public static void SetMoonDirection(Vector3 forward)
        {
            if (forward.sqrMagnitude > 1e-6f) _moonDir = forward.normalized;
        }

        public static void Build(Transform parent)
        {
            if (_built) return;
            try
            {
                for (int i = 0; i < Count; i++)
                {
                    var go = new GameObject("BOUNCE_" + i);
                    go.transform.SetParent(parent, false);

                    go.SetActive(false);

                    var l = go.AddComponent<Light>();
                    l.type = LightType.Point;
                    l.color = BounceTint;
                    l.range = 9f;

                    l.shadows = LightShadows.None;
                    l.cullingMask = ~0;

                    var hd = go.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>();
                    if (hd != null)
                    {
                        try { hd.SetIntensity(MinLumen, UnityEngine.Rendering.LightUnit.Lumen); } catch { }
                        try { hd.EnableShadows(false); } catch { }
                        try { hd.affectsVolumetric = false; } catch { }
                        try { hd.affectSpecular = false; } catch { }
                    }

                    try { go.SetActive(true); } catch { }
                    try { hd.SetIntensity(0f, UnityEngine.Rendering.LightUnit.Lumen); } catch { }

                    _go[i] = go; _light[i] = l; _hd[i] = hd;
                    _target[i] = Vector3.zero; _want[i] = 0f;
                }
                _built = true;
                _log?.Msg("[bounce] " + Count + " bounce probes built (the map has no "
                        + "lightmaps, so this is where shadow detail comes from)");
            }
            catch (Exception e) { _log?.Warning("[bounce] build: " + e.Message); }
        }

        public static void Tick()
        {
            if (!_built) return;
            try
            {
                var cam = Cam;
                if (cam == null) { Park(); return; }
                if (!Enabled) { Park(); return; }

                Vector3 eye = cam.transform.position;
                float dt = Time.deltaTime;

                float t = Mathf.Clamp01(dt * Follow);

                for (int i = 0; i < Count; i++)
                {
                    var go = _go[i];
                    if (go == null) continue;

                    float a = (Mathf.PI * 2f / Count) * i;
                    Vector3 dir = new Vector3(Mathf.Cos(a) * 0.75f, -1f, Mathf.Sin(a) * 0.75f).normalized;

                    RaycastHit hit;
                    bool got = false;
                    try { got = Physics.Raycast(eye, dir, out hit, Reach, ~0, QueryTriggerInteraction.Ignore); }
                    catch { got = false; hit = default(RaycastHit); }

                    if (!got) { Fade(i, t); continue; }

                    float facing = Vector3.Dot(hit.normal, -_moonDir);
                    if (facing <= 0.05f) { Fade(i, t); continue; }

                    float d = Vector3.Distance(eye, hit.point);
                    if (d < 0.25f) { Fade(i, t); continue; }

                    _target[i] = hit.point + hit.normal * Lift;

                    _want[i] = Mathf.Clamp(Strength / d, MinLumen, MaxLumen) * facing;

                    var tr = go.transform;

                    tr.position = tr.position == Vector3.zero
                        ? _target[i]
                        : Vector3.Lerp(tr.position, _target[i], t);

                    var hd = _hd[i];
                    if (hd != null)
                    {
                        _lumen[i] = Mathf.Lerp(_lumen[i], _want[i], t);
                        try { hd.SetIntensity(_lumen[i], UnityEngine.Rendering.LightUnit.Lumen); } catch { }
                    }
                }
            }
            catch (Exception e) { _log?.Warning("[bounce] tick: " + e.Message); }
        }

        static void Fade(int i, float t)
        {
            try
            {
                var hd = _hd[i];
                if (hd == null) return;
                _lumen[i] = Mathf.Lerp(_lumen[i], 0f, t);
                if (_lumen[i] < 0.05f)
                {

                    _lumen[i] = 0f;
                    try { hd.SetIntensity(0f, UnityEngine.Rendering.LightUnit.Lumen); } catch { }
                    return;
                }
                hd.SetIntensity(_lumen[i], UnityEngine.Rendering.LightUnit.Lumen);
            }
            catch { }
        }

        static void Park()
        {
            for (int i = 0; i < Count; i++)
                try { if (_go[i] != null && _go[i].activeSelf) _go[i].SetActive(false); } catch { }
        }

        public static void Toggle()
        {
            Enabled = !Enabled;
            if (!Enabled) Park();
            _log?.Msg("[bounce] bounced light " + (Enabled ? "ON" : "OFF"));
        }

        public static void ScaleStrength(float factor)
        {
            Strength = Mathf.Clamp(Strength * factor, 2f, 400f);
            _log?.Msg("[bounce] strength " + Strength.ToString("F1")
                    + " (higher lifts the shadows, lower keeps it night)");
        }

        public static void Forget()
        {
            for (int i = 0; i < Count; i++)
            { _go[i] = null; _light[i] = null; _hd[i] = null; _lumen[i] = 0f; _want[i] = 0f; }
            _cam = null;
            _built = false;
        }
    }
}
