// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Globalization;
using System.IO;
using MelonLoader;
using UnityEngine;

namespace MapLoader
{

    public static class MapGlass
    {
        static MelonLogger.Instance _log;
        public static void Init(MelonLogger.Instance log) { _log = log; }

        sealed class Pane
        {
            public Vector3 Centre;
            public Vector3 Normal;
            public float W, H;
        }

        public static void Place()
        {
            try
            {
                var root = MapRuntime.Root;
                if (root == null) return;
                string path = Path.Combine(MapRuntime.BundleDir, "glass_panes.txt");
                if (!File.Exists(path)) return;

                var panes = new System.Collections.Generic.List<Pane>();
                foreach (var line in File.ReadAllLines(path))
                {
                    if (line.Length == 0 || line[0] == '#') continue;
                    var f = line.Split('\t');
                    if (f.Length < 8 || f[0] != "pane") continue;
                    try
                    {
                        var ci = CultureInfo.InvariantCulture;
                        panes.Add(new Pane
                        {
                            Centre = new Vector3(float.Parse(f[1], ci), float.Parse(f[2], ci),
                                                 float.Parse(f[3], ci)),
                            Normal = new Vector3(float.Parse(f[4], ci), 0f, float.Parse(f[5], ci)),
                            W = float.Parse(f[6], ci),
                            H = float.Parse(f[7], ci),
                        });
                    }
                    catch { }
                }
                if (panes.Count == 0) return;

                var mat = GlassMaterial();
                var made = new System.Collections.Generic.List<GameObject>();
                var spawnAt = new System.Collections.Generic.List<Vector3>();
                var size = new System.Collections.Generic.List<Vector2>();
                var normal = new System.Collections.Generic.List<Vector3>();
                foreach (var p in panes)
                {
                    try
                    {
                        var go = new GameObject("ODG_GlassPane_" + made.Count);
                        go.SetActive(false);
                        go.transform.SetParent(root.transform, false);
                        go.transform.position = p.Centre;
                        var n = p.Normal.sqrMagnitude > 1e-6f ? p.Normal.normalized : Vector3.forward;
                        go.transform.rotation = Quaternion.LookRotation(n, Vector3.up);

                        var mf = go.AddComponent<MeshFilter>();
                        var mr = go.AddComponent<MeshRenderer>();
                        mr.material = mat;

                        var bs = go.AddComponent<Il2CppGK.BreakableSurface>();
                        var poly = new Il2CppSystem.Collections.Generic.List<Vector2>();
                        float hw = p.W / 2f, hh = p.H / 2f;
                        poly.Add(new Vector2(-hw, -hh));
                        poly.Add(new Vector2(hw, -hh));
                        poly.Add(new Vector2(hw, hh));
                        poly.Add(new Vector2(-hw, hh));
                        bs.Polygon = poly;
                        bs.Thickness = 0.02f;

                        go.SetActive(true);
                        made.Add(go);
                        spawnAt.Add(go.transform.position);
                        size.Add(new Vector2(p.W, p.H));
                        normal.Add(n);
                    }
                    catch (Exception pe)
                    {
                        if (made.Count == 0) _log?.Warning("[glass] pane spawn: " + pe.Message);
                    }
                }
                _log?.Msg("[glass] spawned " + made.Count + " breakable pane(s) of "
                        + panes.Count + " authored; verdict follows a frame later");
                if (made.Count > 0)
                    MelonCoroutines.Start(Verdict(made, spawnAt, size, normal));
            }
            catch (Exception e) { _log?.Warning("[glass] Place: " + e.Message); }
        }

        static System.Collections.IEnumerator Verdict(
            System.Collections.Generic.List<GameObject> made,
            System.Collections.Generic.List<Vector3> spawnAt,
            System.Collections.Generic.List<Vector2> size,
            System.Collections.Generic.List<Vector3> normal)
        {

            yield return null;

            int reoriented = 0;
            for (int i = 0; i < made.Count; i++)
            {
                var go = made[i];
                if (go == null) continue;
                try
                {
                    var rb = go.GetComponent<Rigidbody>();
                    if (rb != null) { rb.isKinematic = true; rb.useGravity = false; }
                    var mr = go.GetComponent<MeshRenderer>();
                    if (mr == null) continue;
                    float wantH = size[i].y * 0.6f;
                    if (mr.bounds.size.y < wantH)
                    {

                        go.transform.rotation =
                            Quaternion.LookRotation(Vector3.up, normal[i]);
                        go.transform.position = spawnAt[i];
                        reoriented++;
                    }
                }
                catch { }
            }
            if (reoriented > 0)
                _log?.Msg("[glass] " + reoriented + " pane(s) came out flat and were "
                        + "turned upright: BreakableSurface extrudes its polygon along "
                        + "local Y, not local Z");

            yield return new WaitForSeconds(0.5f);
            int built = 0, dead = 0, drifted = 0;
            for (int i = 0; i < made.Count; i++)
            {
                var go = made[i];
                bool ok = false;
                try
                {
                    if (go != null)
                    {
                        var mf = go.GetComponent<MeshFilter>();
                        ok = mf != null && mf.sharedMesh != null
                             && mf.sharedMesh.vertexCount > 0;

                        if (ok && i < spawnAt.Count
                            && (go.transform.position - spawnAt[i]).sqrMagnitude > 0.0025f)
                        {
                            _log?.Warning("[glass] pane '" + go.name + "' MOVED "
                                        + (go.transform.position - spawnAt[i]).magnitude.ToString("F2")
                                        + " m from its window; destroying it");
                            ok = false;
                            drifted++;
                        }
                        if (ok)
                        {
                            var mr2 = go.GetComponent<MeshRenderer>();
                            if (mr2 != null && mr2.bounds.size.y < size[i].y * 0.6f)
                            {
                                _log?.Warning("[glass] pane '" + go.name + "' is still LYING "
                                            + "FLAT (world height " + mr2.bounds.size.y.ToString("F2")
                                            + " m against a pane " + size[i].y.ToString("F2")
                                            + " m tall); destroying it so it cannot trap anyone");
                                ok = false;
                                drifted++;
                            }
                        }
                    }
                }
                catch { }
                if (ok) built++;
                else
                {
                    dead++;
                    try { if (go != null) UnityEngine.Object.Destroy(go); } catch { }
                }
            }
            if (drifted > 0)
                _log?.Warning("[glass] " + drifted + " pane(s) fell out of their frames "
                            + "and were removed. The static glass behind them still stands.");
            if (built == 0)
            {
                _log?.Warning("[glass] BreakableSurface built no sheets (" + dead
                            + " dead); destroying the panes and keeping the static "
                            + "fallback glass + blockers. Windows are solid, not shatterable.");
                foreach (var go in made)
                { try { if (go != null) UnityEngine.Object.Destroy(go); } catch { } }
                yield break;
            }

            int off = 0;
            var root = MapRuntime.Root;
            if (dead > 0)
            {
                _log?.Warning("[glass] " + dead + " of " + made.Count + " pane(s) failed; "
                            + "keeping the static fallback glass for every window so "
                            + "none of them is a hole. " + built + " pane(s) are breakable.");
            }
            else if (root != null)
            {
                var t = root.transform;
                for (int i = 0; i < t.childCount; i++)
                {
                    var c = t.GetChild(i);
                    string n = null;
                    try { n = c.name; } catch { }
                    if (n == "glazing_panes" || n == "glasspaneblock")
                    { try { c.gameObject.SetActive(false); off++; } catch { } }
                }
            }
            _log?.Msg("[glass] SURVIVAL CHECK: " + built + " breakable pane(s) built"
                    + (dead > 0 ? (", " + dead + " dead") : "")
                    + "; " + off + " fallback object(s) retired. The windows are "
                    + "the game's own glass now: solid both ways until shot out.");
        }

        static Material GlassMaterial()
        {
            var sh = Shader.Find("HDRP/Lit");
            var m = new Material(sh);
            m.name = "ODG_PaneGlass";
            void SetIf(string p, float v) { try { if (m.HasProperty(p)) m.SetFloat(p, v); } catch { } }
            try { m.SetColor("_BaseColor", new Color(0.75f, 0.84f, 0.87f, 0.42f)); } catch { }
            SetIf("_Metallic", 0f);
            SetIf("_Smoothness", 0.95f);
            SetIf("_SurfaceType", 1f);
            SetIf("_BlendMode", 0f);
            SetIf("_SrcBlend", 1f);
            SetIf("_DstBlend", 10f);
            SetIf("_AlphaSrcBlend", 1f);
            SetIf("_AlphaDstBlend", 10f);
            SetIf("_ZWrite", 0f);
            SetIf("_TransparentZWrite", 0f);
            SetIf("_ZTestTransparent", 4f);
            SetIf("_EnableFogOnTransparent", 1f);
            SetIf("_EnableBlendModePreserveSpecularLighting", 1f);
            try { m.SetOverrideTag("RenderType", "Transparent"); } catch { }
            try
            {
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.EnableKeyword("_ENABLE_FOG_ON_TRANSPARENT");
                m.EnableKeyword("_BLENDMODE_ALPHA");
            }
            catch { }
            m.renderQueue = 3000;
            return m;
        }
    }
}
