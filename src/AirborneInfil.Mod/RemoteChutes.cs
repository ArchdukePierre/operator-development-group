// ODG Operations - AirborneInfil
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace AirborneInfil
{

    internal static class RemoteChutes
    {
        internal delegate GameObject SpawnFn(GameObject tpl);
        internal delegate void PlaceFn(GameObject go, Vector3 anchorPos, float baseYaw,
                                       float lean, float pop, float phase);

        sealed class Tracked
        {
            public GameObject Chute;
            public Il2Cpp.PlayerMaster Master;
            public Transform Body;
            public float GroundedFor;
            public float Phase;

            public Vector3 PrevPos;
            public bool HavePrev;
            public Vector3 Vel;
            public float PopT;

            public bool Landed;
        }

        static readonly Dictionary<int, Tracked> _tracked = new Dictionary<int, Tracked>();
        static readonly List<int> _dead = new List<int>();
        static float _nextScan;
        static int _phaseSeed;

        const float ScanInterval = 1.0f;

        const float ArmHeight = 8.0f;
        const float DisarmHeight = 4.0f;

        const float ProbeReach = 40.0f;

        const float GroundedGrace = 0.6f;

        internal static int Count => _tracked.Count;

        internal static void Tick(GameObject tpl, SpawnFn spawn, PlaceFn place,
                                 int airborneIndex, Action<string> log)
        {
            if (tpl == null || airborneIndex < 0) { Clear(); return; }

            if (Time.time >= _nextScan)
            {
                _nextScan = Time.time + ScanInterval;
                Rescan(airborneIndex, log);
            }

            _dead.Clear();
            foreach (var kv in _tracked)
            {
                var t = kv.Value;

                Vector3 p;
                try
                {
                    if (t.Body == null) { _dead.Add(kv.Key); continue; }
                    p = t.Body.position;
                }
                catch { _dead.Add(kv.Key); continue; }

                float dt = Mathf.Max(Time.deltaTime, 1e-4f);
                if (t.HavePrev)
                    t.Vel = Vector3.Lerp(t.Vel, (p - t.PrevPos) / dt, 0.15f);
                t.PrevPos = p;
                t.HavePrev = true;

                float clearance = GroundClearance(p);

                if (t.Landed)
                {

                    if (clearance > ArmHeight)
                    {
                        t.Landed = false;
                        t.GroundedFor = 0f;
                    }
                    else continue;
                }

                t.GroundedFor = clearance > DisarmHeight ? 0f : t.GroundedFor + Time.deltaTime;

                if (t.GroundedFor > GroundedGrace)
                {
                    Destroy(t);
                    t.Landed = true;
                    continue;
                }

                if (t.Chute == null)
                {

                    if (clearance < ArmHeight) continue;
                    t.Chute = spawn(tpl);
                    if (t.Chute == null) continue;
                    t.PopT = Time.time;
                }

                var flat = new Vector3(t.Vel.x, 0f, t.Vel.z);
                float speed = flat.magnitude;
                float yaw = speed > 0.4f
                    ? Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg
                    : SafeYaw(t.Body);
                float lean = Mathf.Min(12f, speed * 0.8f);
                float pop = EaseOutBack(Mathf.Clamp01((Time.time - t.PopT) / 0.9f));

                place(t.Chute, p, yaw, lean, pop, t.Phase);
            }
            for (int i = 0; i < _dead.Count; i++) _tracked.Remove(_dead[i]);
        }

        static float SafeYaw(Transform t)
        {
            try { return t.eulerAngles.y; } catch { return 0f; }
        }

        static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            t = Mathf.Clamp01(t);
            float i = 1f - t;
            return 1f + c3 * (i * i * i) - c1 * (i * i);
        }

        static float GroundClearance(Vector3 p)
        {
            try
            {
                var origin = p + Vector3.up * 1.6f;
                RaycastHit hit;
                if (!Physics.Raycast(origin, Vector3.down, out hit, ProbeReach + 1.6f,
                                     ~0, QueryTriggerInteraction.Ignore))
                    return ProbeReach;
                return Mathf.Max(0f, p.y - hit.point.y);
            }
            catch { return 0f; }
        }

        static void Rescan(int airborneIndex, Action<string> log)
        {
            Il2Cpp.PlayerMaster local = null;
            try { local = Il2Cpp.PlayerMaster.MyPlayerMaster; } catch { }

            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<Il2Cpp.PlayerMaster> all;
            try { all = Resources.FindObjectsOfTypeAll<Il2Cpp.PlayerMaster>(); }
            catch { return; }
            if (all == null) return;

            for (int i = 0; i < all.Length; i++)
            {
                var pm = all[i];
                try
                {
                    if (pm == null || pm.gameObject == null) continue;
                    if (!pm.gameObject.scene.IsValid()) continue;

                    if (local != null && pm.Pointer == local.Pointer) continue;

                    int id = pm.GetInstanceID();
                    bool wants = pm.CurrentInfilIndex == airborneIndex;

                    if (!wants)
                    {

                        Tracked existing;
                        if (_tracked.TryGetValue(id, out existing))
                        {
                            Destroy(existing);
                            _tracked.Remove(id);
                        }
                        continue;
                    }

                    var avatar = pm.PlayerSpawnedObject;
                    if (avatar == null || avatar.gameObject == null) continue;

                    Tracked t;
                    if (!_tracked.TryGetValue(id, out t))
                    {
                        t = new Tracked
                        {
                            Master = pm,
                            Body = avatar.transform,
                            PrevPos = avatar.transform.position,

                            Phase = 1.9f + (_phaseSeed++ % 8) * 1.7f,
                        };
                        _tracked[id] = t;
                        if (log != null)
                            log("remote canopy tracking '" + SafeName(pm) + "' (infil "
                              + airborneIndex + ")");
                    }
                    else if (!ReferenceEquals(t.Body, avatar.transform))
                    {

                        t.Body = avatar.transform;
                        t.HavePrev = false;
                        t.Vel = Vector3.zero;
                        t.Landed = false;
                        t.GroundedFor = 0f;
                    }
                }
                catch { }
            }
        }

        static string SafeName(Il2Cpp.PlayerMaster pm)
        {
            try { return pm.gameObject.name; } catch { return "?"; }
        }

        static void Destroy(Tracked t)
        {
            try { if (t.Chute != null) UObject.Destroy(t.Chute); } catch { }
            t.Chute = null;
        }

        internal static void Clear()
        {
            foreach (var kv in _tracked) Destroy(kv.Value);
            _tracked.Clear();
            _dead.Clear();
            _nextScan = 0f;
        }
    }
}
