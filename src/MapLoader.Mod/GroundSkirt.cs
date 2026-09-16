// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace MapLoader
{

    public static class GroundSkirt
    {
        static MelonLogger.Instance _log;
        static GameObject _root;

        static MelonPreferences_Entry<bool>  _enabled;
        static MelonPreferences_Entry<float> _width;
        static MelonPreferences_Entry<float> _yOffset;
        static MelonPreferences_Entry<float> _tile;

        public static void Init(MelonLogger.Instance log)
        {
            _log = log;
            try
            {
                var cfg = MelonPreferences.GetCategory("MapLoader")
                       ?? MelonPreferences.CreateCategory("MapLoader");
                _enabled = cfg.GetEntry<bool>("GroundSkirtEnabled")
                        ?? cfg.CreateEntry("GroundSkirtEnabled", true,
                             description: "Extend flat ground beyond the map so the host's trees have something to stand on.");
                _width = cfg.GetEntry<float>("GroundSkirtWidthM")
                      ?? cfg.CreateEntry("GroundSkirtWidthM", 100f,
                             description: "How far the ground extends past the map on every side, in metres.");
                _yOffset = cfg.GetEntry<float>("GroundSkirtYOffset")
                        ?? cfg.CreateEntry("GroundSkirtYOffset", 0f,
                             description: "Nudge the skirt up or down if it does not meet our terrain edge cleanly.");
                _tile = cfg.GetEntry<float>("GroundSkirtTileM")
                     ?? cfg.CreateEntry("GroundSkirtTileM", 4f,
                             description: "Metres per texture tile on the skirt. Lower repeats more often.");
            }
            catch (Exception e) { _log?.Warning("[skirt] prefs: " + e.Message); }
        }

        public static void Reset()
        {

            _root = null;
            _skipped = false;
            _inner = default(Bounds);
            _outer = default(Bounds);
        }

        public static bool Exists { get { return _root != null; } }

        static Bounds _inner, _outer;
        public static Bounds InnerEdge { get { return _inner; } }
        public static Bounds OuterEdge { get { return _outer; } }

        public static bool IsSkirt(Collider c)
        {
            if (c == null || _root == null) return false;
            try { return c.transform.IsChildOf(_root.transform); }
            catch { return false; }
        }

        public static bool OutsideOuterEdge(Vector3 p, float pad)
        {
            if (_root == null) return false;
            if (_outer.size.x <= 1f || _outer.size.z <= 1f) return false;
            return p.x < _outer.min.x - pad || p.x > _outer.max.x + pad
                || p.z < _outer.min.z - pad || p.z > _outer.max.z + pad;
        }

        static bool _skipped;

        public static void Tick(bool mapBuilt)
        {
            if (!mapBuilt || _root != null || _skipped) return;
            if (_enabled != null && !_enabled.Value) return;

            if (!MapIdentity.WantsGroundSkirt)
            {
                _log?.Msg("[skirt] '" + MapIdentity.SceneName + "' does not stand on "
                        + "ground, so no skirt is built. There is nothing past the edge "
                        + "of this map to extend.");
                _skipped = true;
                return;
            }
            try { Build(); }
            catch (Exception e) { _log?.Error("[skirt] build: " + e.Message); }
        }

        public static void Rebuild()
        {
            try
            {
                if (_root != null) { UnityEngine.Object.Destroy(_root); _root = null; }
                Build();
            }
            catch (Exception e) { _log?.Error("[skirt] rebuild: " + e.Message); }
        }

        static void Build()
        {
            var ourRoot = MapRuntime.OurRoot;
            if (ourRoot == null) { _log?.Warning("[skirt] no map root yet"); return; }

            var b = MapRuntime.Bounds();
            if (b.size.x <= 1f || b.size.z <= 1f)
            {
                _log?.Warning("[skirt] map bounds look wrong (" + b.size + "); not building");
                return;
            }

            float w = _width != null ? _width.Value : 100f;
            if (w <= 1f) { _log?.Msg("[skirt] width " + w + " m, nothing to build"); return; }

            float y;
            if (!SampleGroundHeight(b, out y))
            {

                y = b.min.y;
                _log?.Warning("[skirt] could not sample our terrain height; falling back to "
                            + "the map bounds floor y=" + y.ToString("F2"));
            }
            y += _yOffset != null ? _yOffset.Value : 0f;

            var mat = BorrowTerrainMaterial(ourRoot);
            float tile = _tile != null ? Mathf.Max(0.5f, _tile.Value) : 4f;

            _root = new GameObject("ODG Ground Skirt");
            _root.transform.SetParent(ourRoot, false);

            b = ChooseFootprint(b, w, y);
            _inner = b;
            _outer = new Bounds(b.center, b.size);
            _outer.Encapsulate(new Vector3(b.min.x - w, b.center.y, b.min.z - w));
            _outer.Encapsulate(new Vector3(b.max.x + w, b.center.y, b.max.z + w));

            float x0 = b.min.x, x1 = b.max.x, z0 = b.min.z, z1 = b.max.z;

            Quad("Skirt N", x0 - w, z1,     x1 + w, z1 + w, y, mat, tile);
            Quad("Skirt S", x0 - w, z0 - w, x1 + w, z0,     y, mat, tile);
            Quad("Skirt E", x1,     z0,     x1 + w, z1,     y, mat, tile);
            Quad("Skirt W", x0 - w, z0,     x0,     z1,     y, mat, tile);

            _log?.Msg("[skirt] built a " + w.ToString("F0") + " m ground skirt at y="
                    + y.ToString("F2") + " around a map of "
                    + b.size.x.ToString("F0") + " x " + b.size.z.ToString("F0") + " m"
                    + (mat != null ? " using our terrain material" : " with a plain fallback material")
                    + ". Total ground now "
                    + (b.size.x + 2f * w).ToString("F0") + " x "
                    + (b.size.z + 2f * w).ToString("F0") + " m.");
        }

        static void Quad(string name, float x0, float z0, float x1, float z1,
                         float y, Material mat, float tile)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);

            var t = _root.transform;
            Vector3 p00 = t.InverseTransformPoint(new Vector3(x0, y, z0));
            Vector3 p10 = t.InverseTransformPoint(new Vector3(x1, y, z0));
            Vector3 p11 = t.InverseTransformPoint(new Vector3(x1, y, z1));
            Vector3 p01 = t.InverseTransformPoint(new Vector3(x0, y, z1));

            var vTmp = new Vector3[] { p00, p10, p11, p01 };
            var nTmp = new Vector3[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };

            var uTmp = new Vector2[]
            {
                new Vector2(x0 / tile, z0 / tile),
                new Vector2(x1 / tile, z0 / tile),
                new Vector2(x1 / tile, z1 / tile),
                new Vector2(x0 / tile, z1 / tile),
            };
            var iTmp = new int[] { 0, 2, 1, 0, 3, 2 };

            Il2CppStructArray<Vector3> verts = vTmp;
            Il2CppStructArray<Vector3> norms = nTmp;
            Il2CppStructArray<Vector2> uvs   = uTmp;
            Il2CppStructArray<int>     tris  = iTmp;

            var mesh = new Mesh { name = name + " Mesh" };
            mesh.vertices  = verts;
            mesh.normals   = norms;
            mesh.uv        = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();

            if (mat != null) mr.sharedMaterial = mat;
            else
            {
                var sh = Shader.Find("HDRP/Lit");
                if (sh != null)
                {
                    var fallback = new Material(sh) { name = "SKIRT_FALLBACK" };
                    var grey = new Color(0.22f, 0.22f, 0.21f, 1f);
                    if (fallback.HasProperty("_BaseColor")) fallback.SetColor("_BaseColor", grey);
                    if (fallback.HasProperty("_Color")) fallback.SetColor("_Color", grey);
                    if (fallback.HasProperty("_Metallic")) fallback.SetFloat("_Metallic", 0f);
                    if (fallback.HasProperty("_Smoothness")) fallback.SetFloat("_Smoothness", 0.05f);
                    mr.sharedMaterial = fallback;
                }
                else _log?.Warning("[skirt] no HDRP/Lit shader for the fallback material; "
                                 + "the skirt will render as an error colour.");
            }
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = true;

            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
        }

        const int   MinGroundObjects  = 8;
        const float MaxBuriedFraction = 0.05f;
        const float BuryBand          = 3.0f;
        const int   BuryRays          = 2000;

        static Bounds ChooseFootprint(Bounds all, float w, float y)
        {
            try
            {
                bool fromGround; int nGround, nMarkers;
                var fb = MapRuntime.WalkableFootprint(MinGroundObjects, out fromGround,
                                                      out nGround, out nMarkers);
                if (!fromGround || fb.size.x <= 1f || fb.size.z <= 1f)
                {
                    _log?.Warning("[skirt] only " + nGround + " object(s) on this map "
                            + "are classified as ground, too few to measure a walkable "
                            + "footprint from. The ring will be sized from ALL "
                            + "renderers -- on a map whose skyline reaches past its "
                            + "streets that puts the ring beyond the last floor, and a "
                            + "player who ends up on it cannot walk back in. Fix "
                            + "IsGroundName for this map.");
                    return all;
                }

                int rays;
                float buried = BuriedFraction(fb, w, y, out rays);
                if (buried > MaxBuriedFraction)
                {
                    _log?.Warning("[skirt] REFUSING the walkable footprint x "
                            + fb.min.x.ToString("F1") + ".." + fb.max.x.ToString("F1")
                            + ", z " + fb.min.z.ToString("F1") + ".." + fb.max.z.ToString("F1")
                            + ": a ring around it would sit on top of our own floor over "
                            + (buried * 100f).ToString("F1") + " % of itself, over "
                            + rays + " rays, against a limit of "
                            + (MaxBuriedFraction * 100f).ToString("F0") + " %. That is "
                            + nGround + " ground object(s) covering only part of this "
                            + "map, and a ring sized from them would pave the streets it "
                            + "was meant to stop at. Falling back to ALL renderers, "
                            + "which leaves the ring beyond the last floor. Fix "
                            + "IsGroundName for this map.");
                    return all;
                }

                _log?.Msg("[skirt] sized from the WALKABLE FOOTPRINT x "
                        + fb.min.x.ToString("F1") + ".." + fb.max.x.ToString("F1")
                        + ", z " + fb.min.z.ToString("F1") + ".." + fb.max.z.ToString("F1")
                        + " -- " + nGround + " ground object(s) and " + nMarkers
                        + " authored marker(s) -- and NOT from all renderers, which "
                        + "reach x " + all.min.x.ToString("F1") + ".." + all.max.x.ToString("F1")
                        + ", z " + all.min.z.ToString("F1") + ".." + all.max.z.ToString("F1")
                        + ". The difference is backdrop nobody can stand on. " + rays
                        + " rays say the ring lands on our own floor over "
                        + (buried * 100f).ToString("F2") + " % of itself.");
                return fb;
            }
            catch (Exception e)
            {
                _log?.Warning("[skirt] footprint: " + e.Message
                            + " -- sizing the ring from all renderers.");
                return all;
            }
        }

        static float BuriedFraction(Bounds fb, float w, float y, out int rays)
        {
            rays = 0;
            var root = MapRuntime.Root;
            if (root == null) return 0f;

            float area = 2f * (fb.size.x + 2f * w) * w + 2f * fb.size.z * w;
            if (area <= 0f) return 0f;

            float step = Mathf.Max(4f, Mathf.Sqrt(area / BuryRays));

            float x0 = fb.min.x, x1 = fb.max.x, z0 = fb.min.z, z1 = fb.max.z;
            var hits = new Il2CppStructArray<RaycastHit>(32L);
            int buried = 0, full = 0;

            try { Physics.SyncTransforms(); } catch { }

            for (float px = x0 - w + step * 0.5f; px < x1 + w; px += step)
            {
                for (float pz = z0 - w + step * 0.5f; pz < z1 + w; pz += step)
                {

                    if (px > x0 && px < x1 && pz > z0 && pz < z1) continue;
                    rays++;
                    int n = 0;
                    try
                    {
                        n = Physics.RaycastNonAlloc(new Vector3(px, y + BuryBand, pz),
                                                    Vector3.down, hits, 2f * BuryBand,
                                                    ~0, QueryTriggerInteraction.Ignore);
                    }
                    catch { continue; }
                    if (n >= hits.Length) full++;
                    if (n > hits.Length) n = hits.Length;
                    for (int i = 0; i < n; i++)
                    {
                        var c = hits[i].collider;
                        if (c == null) continue;
                        try { if (!c.transform.IsChildOf(root.transform)) continue; }
                        catch { continue; }
                        buried++;
                        break;
                    }
                }
            }
            if (full > 0)
                _log?.Warning("[skirt] the bury test's 32-hit buffer filled on " + full
                            + " of " + rays + " ray(s). It counts ANY hit, so a full "
                            + "buffer cannot hide one, but the number is worth knowing.");
            if (rays == 0) return 0f;
            return buried / (float)rays;
        }

        static bool SampleGroundHeight(Bounds b, out float y)
        {
            y = 0f;
            var hits = new Il2CppStructArray<RaycastHit>(32L);
            var found = new List<float>();

            const int Per = 6;
            float inset = Mathf.Min(8f, Mathf.Min(b.size.x, b.size.z) * 0.1f);
            for (int i = 0; i < Per; i++)
            {
                float fx = b.min.x + inset + (b.size.x - 2f * inset) * i / (float)(Per - 1);
                float fz = b.min.z + inset + (b.size.z - 2f * inset) * i / (float)(Per - 1);
                TrySample(new Vector3(fx, b.max.y + 50f, b.min.z + inset), hits, found, true);
                TrySample(new Vector3(fx, b.max.y + 50f, b.max.z - inset), hits, found, true);
                TrySample(new Vector3(b.min.x + inset, b.max.y + 50f, fz), hits, found, true);
                TrySample(new Vector3(b.max.x - inset, b.max.y + 50f, fz), hits, found, true);
            }

            if (found.Count == 0)
            {
                var authored = MapRuntime.AuthoredGroundY();
                if (authored.HasValue)
                {
                    y = authored.Value;
                    _log?.Msg("[skirt] no terrain on this map; using the authored ground "
                            + "level y=" + y.ToString("F2") + " (median of the gameplay "
                            + "markers), not edge rays -- on a city those hit rooftops.");
                    return true;
                }
            }

            if (found.Count == 0) return false;
            found.Sort();
            y = found[found.Count / 2];
            _log?.Msg("[skirt] sampled our terrain at " + found.Count + " point(s) around the edge; "
                    + "median y=" + y.ToString("F2")
                    + "  (lowest " + found[0].ToString("F2")
                    + ", highest " + found[found.Count - 1].ToString("F2") + ")");
            return true;
        }

        static void TrySample(Vector3 origin, Il2CppStructArray<RaycastHit> hits,
                              List<float> into, bool terrainOnly)
        {
            try
            {
                int n = Physics.RaycastNonAlloc(origin, Vector3.down, hits, 400f,
                                                ~0, QueryTriggerInteraction.Ignore);
                if (n > hits.Length) n = hits.Length;
                float best = float.NegativeInfinity;
                var root = MapRuntime.Root;
                for (int k = 0; k < n; k++)
                {
                    var h = hits[k];
                    if (terrainOnly)
                    {
                        if (!MapRuntime.IsOurGround(h.collider)) continue;
                    }
                    else
                    {

                        var c = h.collider;
                        if (c == null || root == null) continue;
                        try { if (!c.transform.IsChildOf(root.transform)) continue; }
                        catch { continue; }
                    }
                    if (h.point.y > best) best = h.point.y;
                }
                if (best > float.NegativeInfinity) into.Add(best);
            }
            catch { }
        }

        static Material BorrowTerrainMaterial(Transform ourRoot)
        {
            try
            {
                var rends = ourRoot.GetComponentsInChildren<Renderer>(false);
                Material first = null;
                for (int i = 0; i < rends.Length; i++)
                {
                    var r = rends[i];
                    if (r == null) continue;
                    string n;
                    try { n = r.gameObject.name; } catch { continue; }
                    if (n == null) continue;
                    if (n.IndexOf("terrain", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Material m = null;
                    try { m = r.sharedMaterial; } catch { }
                    if (m == null) continue;
                    if (first == null) first = m;

                    if (n.IndexOf("terrain_0", StringComparison.OrdinalIgnoreCase) >= 0) return m;
                }
                if (first != null) return first;

                Material road = null; string roadName = null;
                for (int i = 0; i < rends.Length; i++)
                {
                    var r = rends[i];
                    if (r == null) continue;
                    string n;
                    try { n = r.gameObject.name; } catch { continue; }
                    if (n == null) continue;
                    string l = n.ToLowerInvariant();
                    if (l.IndexOf("road", StringComparison.Ordinal) < 0
                        && l.IndexOf("asphalt", StringComparison.Ordinal) < 0
                        && l.IndexOf("street", StringComparison.Ordinal) < 0
                        && l.IndexOf("sidewalk", StringComparison.Ordinal) < 0)
                        continue;
                    Material m = null;
                    try { m = r.sharedMaterial; } catch { }
                    if (m == null) continue;

                    if (road == null || l.IndexOf("road", StringComparison.Ordinal) >= 0)
                    { road = m; roadName = n; }
                    if (l.IndexOf("road_large", StringComparison.Ordinal) >= 0) break;
                }
                if (road != null)
                {
                    _log?.Msg("[skirt] no terrain on this map; the skirt wears the road's "
                            + "material (borrowed from '" + roadName + "').");
                    return road;
                }
                _log?.Warning("[skirt] no terrain material found under the map root; "
                            + "the skirt will use a default material and will not match.");
            }
            catch (Exception e) { _log?.Warning("[skirt] material: " + e.Message); }
            return null;
        }

        public static void Report()
        {
            if (_root == null) { _log?.Msg("[skirt] not built."); return; }
            var b = MapRuntime.Bounds();
            float w = _width != null ? _width.Value : 100f;
            _log?.Msg("[skirt] built, width " + w.ToString("F0") + " m, "
                    + _root.transform.childCount + " panel(s). Map "
                    + b.size.x.ToString("F0") + " x " + b.size.z.ToString("F0")
                    + " m, ground now " + (b.size.x + 2f * w).ToString("F0")
                    + " x " + (b.size.z + 2f * w).ToString("F0") + " m.");
        }
    }
}
