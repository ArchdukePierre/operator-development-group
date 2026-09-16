// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using MelonLoader;
using UnityEngine;
using PF = Il2CppPathfinding;

namespace MapLoader
{

    public static class MapDump
    {
        static MelonLogger.Instance _log;
        public static void Init(MelonLogger.Instance log) { _log = log; }

        static readonly List<string> _suspects = new List<string>();
        static readonly List<string> _headline = new List<string>();

        static string F(float v, int dp = 2)
        {
            if (float.IsNaN(v)) return "NaN";
            if (float.IsInfinity(v)) return "inf";
            return v.ToString("F" + dp, CultureInfo.InvariantCulture);
        }

        static string V(Vector3 v)
        {
            return "(" + F(v.x, 1) + ", " + F(v.y, 1) + ", " + F(v.z, 1) + ")";
        }

        static string ColStr(Color c)
        {
            return "(" + F(c.r, 2) + "," + F(c.g, 2) + "," + F(c.b, 2) + ")";
        }

        static void Suspect(string s)
        {
            if (!_suspects.Contains(s)) _suspects.Add(s);
        }

        static void Head(string s) { _headline.Add(s); }

        public static string Run(float radius)
        {
            if (radius <= 0f) radius = 25f;
            if (radius > 300f) radius = 300f;
            _suspects.Clear();
            _headline.Clear();

            var sb = new StringBuilder(96000);
            int lightsN = 0, litN = 0, stairsN = 0, fixN = 0, transN = 0, doorsN = 0;

            Camera cam = null;
            try { cam = Camera.main; } catch { }
            Vector3 eye = Vector3.zero, fwd = Vector3.forward;
            try { if (cam != null) { eye = cam.transform.position; fwd = cam.transform.forward; } }
            catch { }

            CharacterController cc = FindController();
            Vector3 me = cc != null ? SafePos(cc.transform) : eye;
            if (me == Vector3.zero) me = eye;

            var mapRoot = SafeRoot();

            sb.Append("map            : ").Append(MapIdentity.SceneName)
              .Append("   built=").Append(MapRuntime.Done ? "yes" : "NO")
              .Append("   radius=").Append(F(radius, 0)).Append(" m\n");
            sb.Append("player at      : ").Append(V(me)).Append('\n');
            sb.Append("camera at      : ").Append(V(eye)).Append("  looking ").Append(V(fwd)).Append('\n');
            Head("map " + MapIdentity.SceneName + " at " + V(me));

            sb.Append("\n--- 1. PLAYER / WHAT CAN BE CLIMBED ---\n");
            float step = 0.3f;
            if (cc == null)
            {
                sb.Append("  CharacterController: NOT FOUND\n");
                try
                {
                    var all = UnityEngine.Object.FindObjectsOfType<CharacterController>();
                    int n = all != null ? all.Length : 0;
                    for (int i = 0; i < n && i < 10; i++)
                        if (all[i] != null) sb.Append("    exists: ").Append(all[i].gameObject.name).Append('\n');
                    Suspect("no CharacterController for the player (" + n + " in scene) - "
                          + "StairStep cannot act and the step offset cannot be read");
                }
                catch { }
                Head("controller NOT FOUND");
            }
            else
            {
                step = cc.stepOffset;
                sb.Append("  stepOffset=").Append(F(cc.stepOffset)).Append(" m   slopeLimit=")
                  .Append(F(cc.slopeLimit, 0)).Append(" deg   radius=").Append(F(cc.radius))
                  .Append(" m   height=").Append(F(cc.height)).Append(" m\n");
                sb.Append("  map asks for stepOffset >= ").Append(F(MapIdentity.MinStepOffset)).Append(" m");
                bool ok = !(MapIdentity.MinStepOffset > 0f
                            && cc.stepOffset + 0.001f < MapIdentity.MinStepOffset);
                sb.Append(ok ? "   (satisfied)\n" : "   <-- NOT APPLIED\n");
                if (!ok)
                    Suspect("stepOffset is " + F(cc.stepOffset) + " m, map asks "
                          + F(MapIdentity.MinStepOffset) + " m: StairStep did not take effect");
                Head("stepOffset " + F(cc.stepOffset) + " m (map wants "
                     + F(MapIdentity.MinStepOffset) + ")" + (ok ? "" : "  <-- NOT APPLIED"));
            }

            sb.Append("\n--- 2. WHAT YOU ARE STANDING ON ---\n");
            try
            {
                RaycastHit gh;
                if (Physics.Raycast(me + Vector3.up * 0.5f, Vector3.down, out gh, 12f))
                {
                    var go = gh.collider != null ? gh.collider.gameObject : null;
                    bool ours = IsOurs(go, mapRoot);
                    sb.Append("  ").Append(F(gh.distance)).Append(" m below you: '")
                      .Append(SafeName(go)).Append("'  ").Append(ours ? "OURS" : "HOST/other")
                      .Append("  layer=").Append(go != null ? LayerMask.LayerToName(go.layer) : "?")
                      .Append('\n');
                    Head("standing on " + (ours ? "our " : "HOST ") + SafeName(go));
                    if (!ours)
                        Suspect("you are standing on '" + SafeName(go)
                              + "', which is not part of our map");
                }
                else
                {
                    sb.Append("  NOTHING within 12 m below you.\n");
                    Suspect("no floor found beneath the player within 12 m");
                    Head("NO FLOOR beneath you");
                }
            }
            catch (Exception e) { sb.Append("  ").Append(e.Message).Append('\n'); }

            sb.Append("\n--- 3. FLOOR AROUND YOU (7x7 grid, 1 m spacing) ---\n");
            try
            {
                int hit = 0, miss = 0;
                float lo = 1e9f, hi = -1e9f;
                var missAt = new StringBuilder();
                for (int gx = -3; gx <= 3; gx++)
                    for (int gz = -3; gz <= 3; gz++)
                    {
                        Vector3 p = me + new Vector3(gx, 1.0f, gz);
                        RaycastHit h;
                        if (Physics.Raycast(p, Vector3.down, out h, 15f))
                        {
                            hit++;
                            if (h.point.y < lo) lo = h.point.y;
                            if (h.point.y > hi) hi = h.point.y;
                        }
                        else
                        {
                            miss++;
                            if (miss <= 8)
                                missAt.Append(" (").Append(gx).Append(',').Append(gz).Append(')');
                        }
                    }
                sb.Append("  floor found at ").Append(hit).Append(" of 49 probes");
                if (hit > 0)
                    sb.Append(", height ").Append(F(lo)).Append(" .. ").Append(F(hi))
                      .Append(" m (spread ").Append(F(hi - lo)).Append(" m)");
                sb.Append('\n');
                if (miss > 0)
                {
                    sb.Append("  NO FLOOR at ").Append(miss).Append(" probe(s):")
                      .Append(missAt).Append('\n');
                    if (miss > 6)
                        Suspect(miss + " of 49 floor probes around you found nothing - "
                              + "there is a hole here");
                }
                Head("floor " + hit + "/49 probes" + (miss > 0 ? ("  " + miss + " HOLES") : ""));
            }
            catch (Exception e) { sb.Append("  ").Append(e.Message).Append('\n'); }

            sb.Append("\n--- 4. UNDER THE CROSSHAIR (camera ray, 80 m) ---\n");
            try
            {
                if (cam != null)
                {
                    var hits = Physics.RaycastAll(eye, fwd, 80f);
                    int shown = 0;
                    for (int i = 0; i < (hits != null ? hits.Length : 0) && shown < 8; i++)
                    {
                        var h = hits[i];
                        if (h.collider == null) continue;
                        shown++;
                        var go = h.collider.gameObject;
                        sb.Append("  [").Append(shown).Append("] ").Append(F(h.distance))
                          .Append(" m  '").Append(SafeName(go)).Append("'  ")
                          .Append(IsOurs(go, mapRoot) ? "OURS" : "HOST/other").Append('\n');
                        sb.Append("       layer=").Append(LayerMask.LayerToName(go.layer))
                          .Append("  collider=").Append(h.collider.GetType().Name)
                          .Append(h.collider.enabled ? "" : " DISABLED")
                          .Append("  scale=").Append(V(go.transform.lossyScale)).Append('\n');
                        DescribeRenderer(sb, go, "       ");
                        if (shown == 1) Head("looking at " + SafeName(go) + " at " + F(h.distance) + " m");
                    }
                    if (shown == 0) sb.Append("  the ray hit nothing within 80 m\n");
                }
                else sb.Append("  no camera\n");
            }
            catch (Exception e) { sb.Append("  ray failed: ").Append(e.Message).Append('\n'); }

            sb.Append("\n--- 5. LIGHTING AT YOUR POSITION ---\n");
            try
            {
                sb.Append("  MapLights: built=").Append(MapLights.BuiltCount)
                  .Append("  lit now=").Append(MapLights.EnabledCount)
                  .Append("  budget=").Append(MapLights.MaxEnabled)
                  .Append("  intensityScale=").Append(F(MapLights.IntensityScale))
                  .Append(" (map's own ").Append(F(MapIdentity.LightIntensityScale)).Append(")\n");
                Head("lights " + MapLights.EnabledCount + " lit of " + MapLights.BuiltCount
                     + ", budget " + MapLights.MaxEnabled + ", scale " + F(MapLights.IntensityScale));
                if (MapLights.BuiltCount > 0 && MapLights.EnabledCount == 0)
                    Suspect("all " + MapLights.BuiltCount + " fixture lights are switched off");

                var probe = RenderSettings.ambientProbe;
                float amb = Mathf.Abs(probe[0, 0]) + Mathf.Abs(probe[1, 0]) + Mathf.Abs(probe[2, 0]);
                sb.Append("  ambient probe DC: r=").Append(F(probe[0, 0], 3))
                  .Append(" g=").Append(F(probe[1, 0], 3)).Append(" b=").Append(F(probe[2, 0], 3)).Append('\n');
                if (amb < 0.01f) Suspect("the ambient probe is black - no indirect light at all");

                Light sun = null; float best = -1f;
                var ls = UnityEngine.Object.FindObjectsOfType<Light>();
                for (int i = 0; i < (ls != null ? ls.Length : 0); i++)
                {
                    var L = ls[i];
                    if (L == null || L.type != LightType.Directional) continue;
                    if (!L.enabled || !L.gameObject.activeInHierarchy) continue;
                    if (L.intensity > best) { best = L.intensity; sun = L; }
                }
                if (sun != null)
                {
                    sb.Append("  sun '").Append(SafeName(sun.gameObject)).Append("' intensity=")
                      .Append(F(sun.intensity, 1)).Append("  colour=").Append(ColStr(sun.color))
                      .Append("  elevation=").Append(F(Mathf.Asin(Mathf.Clamp(-sun.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg, 0))
                      .Append(" deg  shadows=").Append(sun.shadows.ToString()).Append('\n');
                    Head("sun " + F(sun.intensity, 0) + " @ "
                         + F(Mathf.Asin(Mathf.Clamp(-sun.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg, 0) + " deg");
                }
                else { sb.Append("  sun: NONE active\n"); Suspect("no active directional light"); }

                DescribeExposure(sb);
            }
            catch (Exception e) { sb.Append("  ").Append(e.Message).Append('\n'); }

            sb.Append("\n--- 6. FIXTURE LIGHTS WITHIN RADIUS ---\n");
            sb.Append("   dist  state  type    intens  range  colour           name\n");
            try
            {

                Transform root = null;
                var g = GameObject.Find("MAPLIGHTS");
                if (g != null) root = g.transform;
                if (root == null)
                {
                    sb.Append("  no MAPLIGHTS root in the scene\n");
                    if (MapLights.BuiltCount > 0)
                        Suspect("MapLights says " + MapLights.BuiltCount
                              + " built but there is no MAPLIGHTS root");
                }
                else
                {
                    var rows = new List<KeyValuePair<float, string>>();
                    for (int i = 0; i < root.childCount; i++)
                    {
                        var c = root.GetChild(i);
                        if (c == null) continue;
                        float d = Vector3.Distance(SafePos(c), me);
                        if (d > radius) continue;
                        var L = c.GetComponent<Light>();
                        lightsN++;
                        bool on = L != null && L.enabled && c.gameObject.activeInHierarchy;
                        if (on) litN++;
                        rows.Add(new KeyValuePair<float, string>(d,
                            "  " + F(d, 1).PadLeft(5) + "  " + (on ? "ON " : "off")
                          + "  " + (L != null ? L.type.ToString().PadRight(6) : "??????")
                          + "  " + (L != null ? F(L.intensity, 0).PadLeft(6) : "     ?")
                          + "  " + (L != null ? F(L.range, 1).PadLeft(5) : "    ?")
                          + "  " + (L != null ? ColStr(L.color).PadRight(15) : "".PadRight(15))
                          + "  " + SafeName(c.gameObject) + "\n"));
                    }
                    rows.Sort((a, b) => a.Key.CompareTo(b.Key));
                    for (int i = 0; i < rows.Count && i < 60; i++) sb.Append(rows[i].Value);
                    if (rows.Count > 60) sb.Append("  ... and ").Append(rows.Count - 60).Append(" more\n");
                    if (rows.Count == 0) sb.Append("  (none within radius)\n");
                    else if (litN == 0)
                        Suspect(lightsN + " fixture light(s) are within " + F(radius, 0)
                              + " m and NONE is on");
                }
                Head("near you: " + lightsN + " fixture light(s), " + litN + " lit");
            }
            catch (Exception e) { sb.Append("  ").Append(e.Message).Append('\n'); }

            var stairSb = new StringBuilder();
            var fixSb = new StringBuilder();
            var transSb = new StringBuilder();
            try
            {
                if (mapRoot == null) stairSb.Append("  (no map root)\n");
                else
                {
                    var t = mapRoot.transform;
                    for (int i = 0; i < t.childCount; i++)
                    {
                        var c = t.GetChild(i);
                        if (c == null) continue;
                        string n = SafeName(c.gameObject);
                        if (n == null) continue;
                        float d = Vector3.Distance(SafePos(c), me);
                        if (d > radius) continue;
                        string l = n.ToLowerInvariant();
                        bool isStair = l.Contains("stair");
                        bool isFix = l.Contains("lamp") || l.Contains("light")
                                  || l.Contains("flouresc") || l.Contains("led")
                                  || l.Contains("searchlight");
                        var rend = c.GetComponent<Renderer>();
                        var col = c.GetComponent<Collider>();

                        if (rend != null)
                        {
                            var m0 = rend.sharedMaterial;
                            if (m0 != null && IsTransparent(m0))
                            {
                                transN++;
                                transSb.Append("  ").Append(F(d, 1).PadLeft(5)).Append(" m  ").Append(n)
                                       .Append("  mat=").Append(m0.name).Append("  queue=").Append(m0.renderQueue);
                                if (LooksStructural(n))
                                {
                                    transSb.Append("   <-- STRUCTURAL, SHOULD BE SOLID");
                                    Suspect("'" + n + "' renders transparent but is structurally named "
                                          + "(material " + m0.name + ")");
                                }
                                transSb.Append('\n');
                            }
                        }

                        if (isStair)
                        {
                            stairsN++;
                            stairSb.Append("  ").Append(F(d, 1).PadLeft(5)).Append(" m  ").Append(n)
                                   .Append(col != null
                                       ? ("  collider=" + col.GetType().Name + (col.enabled ? "" : " DISABLED"))
                                       : "  NO COLLIDER");
                            if (col == null) Suspect("stair '" + n + "' has NO collider");
                            if (rend != null && !rend.enabled) stairSb.Append("  renderer=OFF");
                            stairSb.Append('\n');
                        }
                        else if (isFix)
                        {
                            fixN++;
                            fixSb.Append("  ").Append(F(d, 1).PadLeft(5)).Append(" m  ").Append(n);
                            if (rend != null && rend.sharedMaterial != null)
                            {
                                var m0 = rend.sharedMaterial;
                                fixSb.Append("  mat=").Append(m0.name);
                                try
                                {
                                    if (m0.HasProperty("_EmissiveColor"))
                                    {
                                        var ec = m0.GetColor("_EmissiveColor");
                                        fixSb.Append("  emissive=").Append(ColStr(ec))
                                             .Append(ec.r + ec.g + ec.b <= 0.001f ? " (NOT GLOWING)" : "");
                                    }
                                    else fixSb.Append("  (no _EmissiveColor)");
                                }
                                catch { }
                            }
                            fixSb.Append('\n');
                        }
                    }
                }
            }
            catch (Exception e) { stairSb.Append("  ").Append(e.Message).Append('\n'); }

            sb.Append("\n--- 7. STAIRS WITHIN RADIUS ---\n");
            sb.Append(stairSb.Length > 0 ? stairSb.ToString() : "  (none)\n");

            sb.Append("\n--- 8. STAIR WALKABILITY (raycast, world space) ---\n");
            MeasureStairs(sb, me, radius, step);

            sb.Append("\n--- 9. LIGHT FIXTURES WITHIN RADIUS ---\n");
            sb.Append(fixSb.Length > 0 ? fixSb.ToString() : "  (none)\n");

            sb.Append("\n--- 10. TRANSPARENT SURFACES (see-through audit) ---\n");
            sb.Append(transSb.Length > 0 ? transSb.ToString() : "  (none)\n");

            sb.Append("\n--- 11. DOORS WITHIN RADIUS ---\n");
            try
            {
                var doors = UnityEngine.Object.FindObjectsOfType<Il2Cpp.DoorV2>();
                var rows = new List<KeyValuePair<float, string>>();
                for (int i = 0; i < (doors != null ? doors.Length : 0); i++)
                {
                    var dv = doors[i];
                    if (dv == null) continue;
                    float d = Vector3.Distance(SafePos(dv.transform), me);
                    if (d > radius) continue;
                    doorsN++;
                    var line = new StringBuilder();
                    line.Append("  ").Append(F(d, 1).PadLeft(5)).Append(" m  ")
                        .Append(SafeName(dv.gameObject)).Append("  enabled=").Append(dv.enabled);
                    try { line.Append("  hitbox=").Append(dv.DoorHitBox != null ? "yes" : "NULL"); } catch { }
                    try
                    {
                        line.Append("  handles=").Append(dv.HandleFront != null ? "F" : "-")
                            .Append(dv.HandleBack != null ? "B" : "-");
                    }
                    catch { }
                    rows.Add(new KeyValuePair<float, string>(d, line.Append('\n').ToString()));
                }
                rows.Sort((a, b) => a.Key.CompareTo(b.Key));
                for (int i = 0; i < rows.Count && i < 30; i++) sb.Append(rows[i].Value);
                if (rows.Count == 0) sb.Append("  (none)\n");
            }
            catch (Exception e) { sb.Append("  ").Append(e.Message).Append('\n'); }

            sb.Append("\n--- 12. NAVIGATION AND BOTS ---\n");
            try
            {
                int nodes = -1;
                try { if (NavForge.Graph != null) nodes = NavForge.Graph.CountNodes(); } catch { }
                sb.Append("  our A* graph: ").Append(NavForge.Graph != null ? "present" : "MISSING")
                  .Append("  nodes=").Append(nodes < 0 ? "?" : nodes.ToString()).Append('\n');
                if (NavForge.Graph == null)
                    Suspect("there is no A* graph - bots cannot path anywhere on this map");
                else if (nodes == 0)
                    Suspect("the A* graph has zero nodes - the scan found no walkable surface");
                Head("nav " + (NavForge.Graph != null ? (nodes + " nodes") : "MISSING"));

                var agents = UnityEngine.Object.FindObjectsOfType<Il2Cpp.AgentController>();
                int near = 0;
                for (int i = 0; i < (agents != null ? agents.Length : 0); i++)
                {
                    var a = agents[i];
                    if (a == null) continue;
                    Vector3 p = SafePos(a.transform);
                    float d = Vector3.Distance(p, me);
                    if (d > radius) continue;
                    near++;
                    if (near <= 12)
                        sb.Append("  bot ").Append(F(d, 1)).Append(" m at ").Append(V(p))
                          .Append("  '").Append(SafeName(a.gameObject)).Append("'\n");
                }
                sb.Append("  bots within radius: ").Append(near)
                  .Append("   (total in scene: ").Append(agents != null ? agents.Length : 0).Append(")\n");
            }
            catch (Exception e) { sb.Append("  ").Append(e.Message).Append('\n'); }

            sb.Append("\n--- 13. HOST GEOMETRY STILL VISIBLE NEAR YOU ---\n");
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<Renderer>();
                int leaked = 0;
                for (int i = 0; i < (all != null ? all.Length : 0) && leaked < 15; i++)
                {
                    var r = all[i];
                    if (r == null || !r.enabled) continue;
                    var go = r.gameObject;
                    if (IsOurs(go, mapRoot)) continue;
                    string n = SafeName(go);
                    if (n == null) continue;

                    if (n.IndexOf("MAPLIGHT", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (Vector3.Distance(SafePos(go.transform), me) > radius) continue;
                    leaked++;
                    sb.Append("  '").Append(n).Append("'  at ")
                      .Append(V(SafePos(go.transform))).Append('\n');
                }
                if (leaked == 0) sb.Append("  none - the shell hide is clean here\n");
                else sb.Append("  (").Append(leaked).Append(" shown; these are NOT our map)\n");
            }
            catch (Exception e) { sb.Append("  ").Append(e.Message).Append('\n'); }

            sb.Append("\n--- 14. SHIMMER / JITTER INPUTS ---\n");
            sb.Append("  detail tile   : ").Append(F(MeshFactory.DetailTile, 1))
              .Append(" repeats/material (map's own ").Append(F(MapIdentity.DetailTile, 1)).Append(")")
              .Append(MeshFactory.DetailTileOverridden ? "  [console override]" : "").Append('\n');
            sb.Append("  smoothness cap: ").Append(F(MeshFactory.SmoothnessCeiling)).Append('\n');
            sb.Append("  frame time    : ").Append(F(Time.smoothDeltaTime * 1000f, 1)).Append(" ms (~")
              .Append(F(Time.smoothDeltaTime > 0f ? 1f / Time.smoothDeltaTime : 0f, 0)).Append(" fps)\n");
            sb.Append("  try 'odg detail off' - if the jitter stops, the detail tile is the cause.\n");
            try
            {
                int tri = 0, bil = 0, pt = 0, aniso = 0, sampled = 0;
                if (mapRoot != null)
                {
                    var rs = mapRoot.GetComponentsInChildren<Renderer>(false);
                    for (int i = 0; i < rs.Length && sampled < 40; i++)
                    {
                        if (rs[i] == null) continue;
                        if (Vector3.Distance(SafePos(rs[i].transform), me) > radius) continue;
                        var m = rs[i].sharedMaterial;
                        if (m == null || !m.HasProperty("_BaseColorMap")) continue;
                        var tx = m.GetTexture("_BaseColorMap");
                        if (tx == null) continue;
                        sampled++;
                        if (tx.filterMode == FilterMode.Trilinear) tri++;
                        else if (tx.filterMode == FilterMode.Bilinear) bil++;
                        else pt++;
                        aniso += tx.anisoLevel;
                    }
                }
                sb.Append("  textures sampled=").Append(sampled).Append("  trilinear=").Append(tri)
                  .Append("  bilinear=").Append(bil).Append("  point=").Append(pt)
                  .Append("  mean aniso=").Append(sampled > 0 ? F((float)aniso / sampled, 1) : "-").Append('\n');
                if (bil + pt > 0)
                    Suspect((bil + pt) + " of " + sampled + " sampled textures are not trilinear - "
                          + "the classic TAA mip-flicker cause");
            }
            catch (Exception e) { sb.Append("  ").Append(e.Message).Append('\n'); }

            sb.Append("\n--- 15. SUSPECTS ---\n");
            if (_suspects.Count == 0) sb.Append("  nothing obviously wrong was detected here.\n");
            else for (int i = 0; i < _suspects.Count; i++)
                sb.Append("  * ").Append(_suspects[i]).Append('\n');

            var top = new StringBuilder(2048);
            top.Append("ODG MAP DUMP\n============\n");
            top.Append("PASTE THIS BLOCK IF YOU ONLY SEND ONE THING\n");
            for (int i = 0; i < _headline.Count; i++)
                top.Append("  ").Append(_headline[i]).Append('\n');
            top.Append("  suspects: ").Append(_suspects.Count).Append('\n');
            for (int i = 0; i < _suspects.Count && i < 8; i++)
                top.Append("    ! ").Append(_suspects[i]).Append('\n');
            top.Append("--------------------------------------------\n\n");

            string body = top.ToString() + sb.ToString();

            string path = null, shot = null;
            try
            {
                string dir = Path.Combine(
                    MelonLoader.Utils.MelonEnvironment.GameRootDirectory,
                    "UserData", "ODG Diagnostics");
                Directory.CreateDirectory(dir);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                File.WriteAllText(Path.Combine(dir, "map_dump_" + stamp + ".txt"), body);
                path = Path.Combine(dir, "map_dump.txt");
                File.WriteAllText(path, body);

                try
                {
                    shot = Path.Combine(dir, "map_dump_" + stamp + ".png");
                    ScreenCapture.CaptureScreenshot(shot, 1);
                }
                catch { shot = null; }
            }
            catch (Exception e) { _log?.Warning("[dump] write: " + e.Message); }

            try { _log?.Msg("[dump]\n" + body); } catch { }

            return "dumped: " + lightsN + " light(s) (" + litN + " lit), " + stairsN
                 + " stair piece(s), " + fixN + " fixture(s), " + transN + " transparent, "
                 + doorsN + " door(s) within " + F(radius, 0) + " m."
                 + (_suspects.Count > 0 ? ("  " + _suspects.Count + " SUSPECT(S).") : "  nothing obviously wrong.")
                 + (path != null ? ("\n" + path) : "")
                 + (shot != null ? "\n+ screenshot beside it" : "")
                 + "\n(also in the session log)";
        }

        static void DescribeExposure(StringBuilder sb)
        {
            try
            {
                var vols = UnityEngine.Object.FindObjectsOfType<UnityEngine.Rendering.Volume>();
                int shown = 0;
                for (int i = 0; i < (vols != null ? vols.Length : 0) && shown < 4; i++)
                {
                    var v = vols[i];
                    if (v == null || !v.enabled || v.profile == null) continue;
                    var comps = v.profile.components;
                    for (int k = 0; k < (comps != null ? comps.Count : 0); k++)
                    {
                        var c = comps[k];
                        if (c == null) continue;
                        string tn = c.GetType().Name;
                        if (tn.IndexOf("Exposure", StringComparison.Ordinal) < 0) continue;
                        var ex = c.TryCast<UnityEngine.Rendering.HighDefinition.Exposure>();
                        if (ex == null) continue;
                        shown++;
                        sb.Append("  exposure volume '").Append(SafeName(v.gameObject))
                          .Append("' priority=").Append(F(v.priority, 0))
                          .Append("  mode=").Append(ex.mode.value.ToString())
                          .Append("  fixed EV=").Append(F(ex.fixedExposure.value, 2))
                          .Append("  compensation=").Append(F(ex.compensation.value, 2)).Append('\n');
                    }
                }
                if (shown == 0) sb.Append("  no exposure volume found\n");
            }
            catch (Exception e) { sb.Append("  exposure: ").Append(e.Message).Append('\n'); }
        }

        static void MeasureStairs(StringBuilder sb, Vector3 me, float radius, float step)
        {
            try
            {
                var root = SafeRoot();
                if (root == null) { sb.Append("  (no map root)\n"); return; }
                var t = root.transform;
                int done = 0;
                for (int i = 0; i < t.childCount && done < 4; i++)
                {
                    var c = t.GetChild(i);
                    if (c == null) continue;
                    string n = SafeName(c.gameObject);
                    if (n == null || n.ToLowerInvariant().IndexOf("stair", StringComparison.Ordinal) < 0)
                        continue;
                    var rend = c.GetComponent<Renderer>();
                    if (rend == null) continue;
                    var b = rend.bounds;
                    if (Vector3.Distance(b.center, me) > radius) continue;
                    if (b.size.y < 0.8f) continue;
                    done++;

                    bool alongX = b.size.x >= b.size.z;
                    float len = alongX ? b.size.x : b.size.z;
                    int steps = Mathf.Clamp(Mathf.RoundToInt(len / 0.12f), 8, 90);
                    var ys = new List<float>();
                    for (int s = 0; s <= steps; s++)
                    {
                        float f = (float)s / steps;
                        Vector3 p = new Vector3(
                            alongX ? Mathf.Lerp(b.min.x, b.max.x, f) : b.center.x,
                            b.max.y + 1.5f,
                            alongX ? b.center.z : Mathf.Lerp(b.min.z, b.max.z, f));
                        RaycastHit hit;
                        if (Physics.Raycast(p, Vector3.down, out hit, b.size.y + 3f))
                            ys.Add(hit.point.y);
                    }
                    sb.Append("  '").Append(n).Append("'  rise=").Append(F(b.size.y))
                      .Append(" m  run=").Append(F(len)).Append(" m  pitch=")
                      .Append(F(Mathf.Atan2(b.size.y, len) * Mathf.Rad2Deg, 0)).Append(" deg\n");
                    if (ys.Count < 3)
                    {
                        sb.Append("      rays found no surface - NO USABLE COLLIDER\n");
                        Suspect("stair '" + n + "' returned no raycast hits: nothing to stand on");
                        continue;
                    }
                    float maxUp = 0f;
                    for (int k = 1; k < ys.Count; k++)
                        if (ys[k] - ys[k - 1] > maxUp) maxUp = ys[k] - ys[k - 1];
                    sb.Append("      ").Append(ys.Count)
                      .Append(" samples, biggest single rise=").Append(F(maxUp))
                      .Append(" m vs stepOffset ").Append(F(step)).Append(" m");
                    if (maxUp > step + 0.005f)
                    {
                        sb.Append("   <-- TOO TALL TO CLIMB\n");
                        Suspect("stair '" + n + "' rises " + F(maxUp) + " m against a "
                              + F(step) + " m step offset: unclimbable");
                        Head("STAIR '" + n + "' rise " + F(maxUp) + " > step " + F(step));
                    }
                    else sb.Append("   (climbable)\n");
                }
                if (done == 0) sb.Append("  (no full stair flights within radius)\n");
            }
            catch (Exception e) { sb.Append("  ").Append(e.Message).Append('\n'); }
        }

        static void DescribeRenderer(StringBuilder sb, GameObject go, string pad)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) { sb.Append(pad).Append("(no renderer)\n"); return; }
            sb.Append(pad).Append("renderer=").Append(r.enabled ? "on" : "OFF")
              .Append("  shadows=").Append(r.shadowCastingMode.ToString())
              .Append("  probes=").Append(r.lightProbeUsage.ToString()).Append('\n');
            var mats = r.sharedMaterials;
            for (int k = 0; k < (mats != null ? mats.Length : 0); k++)
            {
                var m = mats[k];
                if (m == null)
                {
                    sb.Append(pad).Append('[').Append(k).Append("] NULL MATERIAL - renders magenta\n");
                    Suspect("'" + SafeName(go) + "' material slot " + k
                          + " is NULL, which HDRP draws as magenta");
                    continue;
                }
                sb.Append(pad).Append('[').Append(k).Append("] ").Append(m.name)
                  .Append("  shader=").Append(m.shader != null ? m.shader.name : "NULL")
                  .Append("  queue=").Append(m.renderQueue).Append('\n').Append(pad).Append("     ");
                try
                {
                    if (m.HasProperty("_SurfaceType"))
                        sb.Append("surface=").Append(m.GetFloat("_SurfaceType") > 0.5f ? "TRANSPARENT " : "opaque ");
                    if (m.HasProperty("_AlphaCutoffEnable"))
                        sb.Append("alphaClip=").Append(F(m.GetFloat("_AlphaCutoffEnable"), 0)).Append(' ');
                    if (m.HasProperty("_BaseColor")) sb.Append("base=").Append(ColStr(m.GetColor("_BaseColor"))).Append(' ');
                    if (m.HasProperty("_Metallic")) sb.Append("metal=").Append(F(m.GetFloat("_Metallic"))).Append(' ');
                    if (m.HasProperty("_Smoothness")) sb.Append("smooth=").Append(F(m.GetFloat("_Smoothness"))).Append(' ');
                    if (m.HasProperty("_EmissiveColor")) sb.Append("emissive=").Append(ColStr(m.GetColor("_EmissiveColor"))).Append(' ');
                }
                catch { }
                sb.Append('\n').Append(pad).Append("     ");
                AppendTex(sb, m, "_BaseColorMap", "albedo");
                AppendTex(sb, m, "_NormalMap", "normal");
                AppendTex(sb, m, "_MaskMap", "mask");
                AppendTex(sb, m, "_DetailMap", "detail");
                sb.Append('\n');
                if (IsTransparent(m) && LooksStructural(SafeName(go)))
                    Suspect("you are looking at '" + SafeName(go)
                          + "' which renders TRANSPARENT but is structurally named");
            }
        }

        static void AppendTex(StringBuilder sb, Material m, string prop, string label)
        {
            try
            {
                if (!m.HasProperty(prop)) return;
                var t = m.GetTexture(prop);
                if (t == null) { sb.Append(label).Append("=none  "); return; }
                sb.Append(label).Append('=').Append(t.width).Append('x').Append(t.height)
                  .Append('/').Append(t.filterMode.ToString().Substring(0, 3))
                  .Append("/af").Append(t.anisoLevel).Append("  ");
            }
            catch { }
        }

        static bool IsTransparent(Material m)
        {
            try
            {
                if (m.renderQueue >= 2900) return true;
                if (m.HasProperty("_SurfaceType") && m.GetFloat("_SurfaceType") > 0.5f) return true;
            }
            catch { }
            return false;
        }

        static readonly string[] Structural =
        {
            "Handle", "Frame", "Hinge", "Wall", "Floor", "Deck", "Beam", "Pipe",
            "Rail", "Stair", "Plate", "Support", "Bracket", "Locker", "Box",
        };

        static bool LooksStructural(string n)
        {
            if (string.IsNullOrEmpty(n)) return false;
            if (n.IndexOf("Window", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Pane", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            for (int i = 0; i < Structural.Length; i++)
                if (n.IndexOf(Structural[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        static bool IsOurs(GameObject go, GameObject mapRoot)
        {
            try
            {
                if (go == null || mapRoot == null) return false;
                var t = go.transform;
                var rt = mapRoot.transform;
                for (int i = 0; i < 12 && t != null; i++)
                {
                    if (t == rt) return true;
                    t = t.parent;
                }
            }
            catch { }
            return false;
        }

        static GameObject SafeRoot()
        {
            try { return MapRuntime.Root; } catch { return null; }
        }

        static string SafeName(GameObject go)
        {
            try { return go != null ? go.name : null; } catch { return null; }
        }

        static Vector3 SafePos(Transform t)
        {
            try { return t != null ? t.position : Vector3.zero; } catch { return Vector3.zero; }
        }

        static CharacterController FindController()
        {
            try
            {
                var mv = UnityEngine.Object.FindObjectsOfType<Il2Cpp.PlayerMovement>();
                for (int i = 0; i < (mv != null ? mv.Length : 0); i++)
                {
                    if (mv[i] == null) continue;
                    CharacterController c = null;
                    try { c = mv[i].controller; } catch { }
                    if (c == null) { try { c = mv[i].GetComponent<CharacterController>(); } catch { } }
                    if (c != null) return c;
                }
            }
            catch { }
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<CharacterController>();
                if (all != null && all.Length > 0) return all[0];
            }
            catch { }
            return null;
        }
    }
}
