// ODG Operations - FireteamRoles
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using UnityEngine;

namespace FireteamRoles
{

    internal static class Rubble
    {
        class Chunk
        {
            internal GameObject Go;
            internal float Die;
        }

        static readonly List<Chunk> _live = new List<Chunk>();
        static Mesh _cube;

        internal static void Spawn(Material mat, int layer, Hole h)
        {
            int want = Mathf.Clamp(Cfg.BreachChunks.Value, 0, 60);
            if (want <= 0 || mat == null) return;
            var cube = Cube();
            if (cube == null) return;

            uint rng = (uint)(h.Seed ^ 0x5bf03635);
            float life = Mathf.Clamp(Cfg.BreachChunkLife.Value, 5f, 300f);
            int made = 0;

            for (int i = 0; i < want; i++)
            {
                try
                {

                    float u = (Rand(ref rng) * 2f - 1f) * h.HalfW * 0.8f;
                    float v = (Rand(ref rng) * 2f - 1f) * h.HalfH * 0.8f;
                    Vector3 pos = h.C + h.Right * u + Vector3.up * (h.VMid + v) + h.Fwd * (h.Front + Rand(ref rng) * h.Thick);

                    float s = Mathf.Lerp(0.06f, 0.24f, Rand(ref rng));
                    var go = Make(mat, layer, cube, pos, s, ref rng, true);
                    var rb = go.GetComponent<Rigidbody>();

                    float sign = Rand(ref rng) < 0.78f ? 1f : -1f;
                    Vector3 kick = h.Fwd * sign * (2.5f + Rand(ref rng) * 5f)
                                   + Vector3.up * (1.2f + Rand(ref rng) * 2.6f)
                                   + h.Right * ((Rand(ref rng) - 0.5f) * 3f);
                    rb.AddForce(kick, ForceMode.VelocityChange);
                    rb.AddTorque(new Vector3(Rand(ref rng) - 0.5f, Rand(ref rng) - 0.5f, Rand(ref rng) - 0.5f) * 12f, ForceMode.VelocityChange);

                    _live.Add(new Chunk { Go = go, Die = Time.realtimeSinceStartup + life });
                    made++;
                }
                catch (Exception e) { Log.Detail("rubble chunk failed: " + e.Message); break; }
            }
            if (made > 0) Log.Detail("rubble: " + made + " chunk(s) thrown");
        }

        internal static void Frame(Material mat, int layer, Hole h)
        {
            int want = Mathf.Clamp(Cfg.BreachFrameChunks.Value, 0, 80);
            if (want <= 0 || mat == null) return;
            var cube = Cube();
            if (cube == null) return;

            uint rng = (uint)(h.Seed ^ 0x2545f491);
            int made = 0;
            for (int i = 0; i < want; i++)
            {
                try
                {
                    float ang = (i + Rand(ref rng) * 0.8f) / want * Mathf.PI * 2f;
                    float radial = 0.92f + Rand(ref rng) * 0.14f;
                    float depth = h.Front - 0.03f + Rand(ref rng) * (h.Thick + 0.06f);
                    Vector3 pos = h.RimPoint(ang, radial, depth);
                    float s = Mathf.Lerp(0.05f, 0.17f, Rand(ref rng));
                    var go = Make(mat, layer, cube, pos, s, ref rng, false);
                    _live.Add(new Chunk { Go = go, Die = float.PositiveInfinity });
                    made++;
                }
                catch (Exception e) { Log.Detail("rim chunk failed: " + e.Message); break; }
            }

            int sill = Mathf.Min(10, want / 2);
            for (int i = 0; i < sill; i++)
            {
                try
                {
                    float u = (Rand(ref rng) * 2f - 1f) * h.HalfW * 0.9f;
                    float depth = h.Front - 0.25f + Rand(ref rng) * (h.Thick + 0.7f);
                    float s = Mathf.Lerp(0.04f, 0.12f, Rand(ref rng));
                    Vector3 pos = h.C + h.Right * u + h.Fwd * depth + Vector3.up * (-h.Bottom + s * 0.45f);
                    var go = Make(mat, layer, cube, pos, s, ref rng, false);
                    _live.Add(new Chunk { Go = go, Die = float.PositiveInfinity });
                    made++;
                }
                catch { break; }
            }
            if (made > 0) Log.Detail("rubble: " + made + " rim and sill piece(s)");
        }

        internal static void Smash(Renderer prop, Hole h, ref uint rng)
        {
            if (prop == null) return;
            Material mat = null;
            int layer = 0;
            Bounds b;
            try { mat = prop.sharedMaterial; layer = prop.gameObject.layer; b = prop.bounds; }
            catch { return; }
            if (mat == null) return;
            var cube = Cube();
            if (cube == null) return;

            float life = Mathf.Clamp(Cfg.BreachChunkLife.Value, 5f, 300f);
            int want = Mathf.Clamp(Mathf.RoundToInt(b.size.magnitude * 3f), 3, 9);
            for (int i = 0; i < want; i++)
            {
                try
                {
                    Vector3 pos = new Vector3(
                        Mathf.Lerp(b.min.x, b.max.x, Rand(ref rng)),
                        Mathf.Lerp(b.min.y, b.max.y, Rand(ref rng)),
                        Mathf.Lerp(b.min.z, b.max.z, Rand(ref rng)));
                    float s = Mathf.Lerp(0.08f, Mathf.Clamp(b.size.magnitude * 0.18f, 0.1f, 0.4f), Rand(ref rng));
                    var go = Make(mat, layer, cube, pos, s, ref rng, true);
                    var rb = go.GetComponent<Rigidbody>();
                    Vector3 kick = h.Fwd * (3f + Rand(ref rng) * 4f) + Vector3.up * (1f + Rand(ref rng) * 2.5f)
                                   + h.Right * ((Rand(ref rng) - 0.5f) * 3f);
                    rb.AddForce(kick, ForceMode.VelocityChange);
                    rb.AddTorque(new Vector3(Rand(ref rng) - 0.5f, Rand(ref rng) - 0.5f, Rand(ref rng) - 0.5f) * 10f, ForceMode.VelocityChange);
                    _live.Add(new Chunk { Go = go, Die = Time.realtimeSinceStartup + life });
                }
                catch { break; }
            }
        }

        static GameObject Make(Material mat, int layer, Mesh cube, Vector3 pos, float s, ref uint rng, bool physics)
        {
            var go = new GameObject("odg_rubble");
            go.layer = layer;
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(Rand(ref rng) * 360f, Rand(ref rng) * 360f, Rand(ref rng) * 360f);
            go.transform.localScale = new Vector3(s * (0.6f + Rand(ref rng) * 0.8f),
                                                  s * (0.6f + Rand(ref rng) * 0.8f),
                                                  s * (0.5f + Rand(ref rng) * 0.6f));
            var mfc = go.AddComponent<MeshFilter>();
            mfc.sharedMesh = cube;
            var mrc = go.AddComponent<MeshRenderer>();
            mrc.sharedMaterial = mat;
            if (physics)
            {
                var col = go.AddComponent<BoxCollider>();
                col.size = Vector3.one;
                var rb = go.AddComponent<Rigidbody>();
                rb.mass = Mathf.Max(0.2f, s * 12f);
            }
            return go;
        }

        internal static void Tick()
        {
            if (_live.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var c = _live[i];
                bool gone = false;
                try { gone = c.Go == null; } catch { gone = true; }
                if (!gone && now < c.Die) continue;

                _live.RemoveAt(i);
                try { if (c.Go != null) UnityEngine.Object.Destroy(c.Go); } catch { }
            }
        }

        internal static void Clear(string why)
        {
            for (int i = 0; i < _live.Count; i++)
            {
                try { if (_live[i].Go != null) UnityEngine.Object.Destroy(_live[i].Go); } catch { }
            }
            if (_live.Count > 0) Log.Detail("rubble cleared: " + _live.Count + " (" + why + ")");
            _live.Clear();
        }

        static Mesh Cube()
        {
            if (_cube != null) return _cube;
            try
            {
                var m = new Mesh();
                m.name = "odg_rubble_cube";

                var v = new Vector3[]
                {
                    new Vector3(-.5f,-.5f,-.5f), new Vector3(.5f,-.5f,-.5f), new Vector3(.5f,.5f,-.5f), new Vector3(-.5f,.5f,-.5f),
                    new Vector3(-.5f,.5f,.5f),   new Vector3(.5f,.5f,.5f),   new Vector3(.5f,-.5f,.5f),  new Vector3(-.5f,-.5f,.5f),
                    new Vector3(-.5f,.5f,-.5f),  new Vector3(.5f,.5f,-.5f),  new Vector3(.5f,.5f,.5f),   new Vector3(-.5f,.5f,.5f),
                    new Vector3(-.5f,-.5f,.5f),  new Vector3(.5f,-.5f,.5f),  new Vector3(.5f,-.5f,-.5f), new Vector3(-.5f,-.5f,-.5f),
                    new Vector3(.5f,-.5f,-.5f),  new Vector3(.5f,-.5f,.5f),  new Vector3(.5f,.5f,.5f),   new Vector3(.5f,.5f,-.5f),
                    new Vector3(-.5f,-.5f,.5f),  new Vector3(-.5f,-.5f,-.5f),new Vector3(-.5f,.5f,-.5f), new Vector3(-.5f,.5f,.5f)
                };
                var tri = new int[]
                {
                    0,2,1, 0,3,2, 4,6,5, 4,7,6, 8,10,9, 8,11,10,
                    12,14,13, 12,15,14, 16,18,17, 16,19,18, 20,22,21, 20,23,22
                };
                var uv = new Vector2[24];
                for (int f = 0; f < 6; f++)
                {
                    uv[f * 4 + 0] = new Vector2(0f, 0f);
                    uv[f * 4 + 1] = new Vector2(1f, 0f);
                    uv[f * 4 + 2] = new Vector2(1f, 1f);
                    uv[f * 4 + 3] = new Vector2(0f, 1f);
                }

                m.SetVertices(MeshCut.FromManaged(v));
                m.SetUVs(0, MeshCut.FromManaged(uv));
                m.SetTriangles(MeshCut.FromManaged(tri), 0);
                m.RecalculateNormals();
                m.RecalculateBounds();
                _cube = m;
            }
            catch (Exception e) { Log.Detail("rubble cube failed: " + e.Message); return null; }
            return _cube;
        }

        internal static float Rand(ref uint s)
        {
            unchecked
            {
                s ^= s << 13; s ^= s >> 17; s ^= s << 5;
                return (s & 0xFFFFFF) / 16777215f;
            }
        }
    }
}
