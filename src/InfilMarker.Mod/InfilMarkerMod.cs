// ODG Operations - InfilMarker
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using MelonLoader;
using UnityEngine;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

[assembly: MelonInfo(typeof(InfilMarker.InfilMarkerMod), "ODG Infil Marker", "1.0.0", "ArchdukePierre")]
[assembly: MelonGame(null, null)]

namespace InfilMarker
{

    public class InfilMarkerMod : MelonMod
    {
        static MelonLogger.Instance _log;
        static Type _tIdentity, _tPlayerBody, _tConsole, _tInfils, _tOps;
        static bool _bound;
        static float _nextBind;
        static bool _appliedAtMenu;

        static readonly string[] Labels = { "ALPHA 1", "BRAVO 1" };
        static readonly string[] Names  = { "ALPHA_1", "BRAVO_1" };
        const float EyeHeight = 1.62f;

        public override void OnInitializeMelon()
        {
            _log = LoggerInstance;
            TryBind();
#if ODG_PUBLIC

            _log.Msg("insertion points ready");
#else
            _log.Msg("odg infil 1 | 2 | show | apply | clear   (hotkeys: Ctrl+Shift+1, Ctrl+Shift+2)");
#endif
        }

        public override void OnUpdate()
        {
            if (!_bound && Time.realtimeSinceStartup > _nextBind)
            {
                _nextBind = Time.realtimeSinceStartup + 2f;
                TryBind();
            }
            try
            {
#if !ODG_PUBLIC
                bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (ctrl && shift && Input.GetKeyDown(KeyCode.Alpha1)) Say(Mark(0));
                if (ctrl && shift && Input.GetKeyDown(KeyCode.Alpha2)) Say(Mark(1));
#endif
            }
            catch { }
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {

            if (sceneName == "3d Menu" && !_appliedAtMenu)
            {
                _appliedAtMenu = true;
                try { ApplyAll(); } catch (Exception e) { _log.Warning("re-apply: " + e.Message); }
            }
        }

        static void Say(string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            foreach (var line in s.Split('\n')) _log.Msg(line);
        }

        static bool TryBind()
        {
            if (_bound) return true;
            try
            {
                Assembly ml = null;
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                    if (a.GetName().Name == "MapLoader.Mod") { ml = a; break; }
                if (ml == null) return false;
                _tIdentity   = ml.GetType("MapLoader.MapIdentity");
                _tPlayerBody = ml.GetType("MapLoader.PlayerBody");
                _tConsole    = ml.GetType("MapLoader.OdgConsole");
                _tInfils     = ml.GetType("MapLoader.MapInfils");
                _tOps        = ml.GetType("MapLoader.CustomOps");

                bool verb = false;
#if !ODG_PUBLIC
                if (_tConsole != null)
                {
                    var f = _tConsole.GetField("External",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (f != null)
                    {
                        f.SetValue(null, new Func<string, string>(Handle));
                        verb = true;
                    }
                }
#endif
                _bound = true;
                _log.Msg("bound to MapLoader.Mod" + (verb
                    ? ": 'odg infil ...' is live in the ODG console"
                    : ", but this MapLoader has no console seam (older build); hotkeys only"));
                return true;
            }
            catch (Exception e) { _log.Warning("bind: " + e.Message); return false; }
        }

        static string Handle(string original)
        {
            if (original == null) return null;
            var t = original.Trim();
            if (!t.StartsWith("infil", StringComparison.OrdinalIgnoreCase)) return null;
            var p = t.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string sub = p.Length > 1 ? p[1].ToLowerInvariant() : "help";
            string r;
            switch (sub)
            {
                case "1": case "a": case "alpha": r = Mark(0); break;
                case "2": case "b": case "bravo": r = Mark(1); break;
                case "show":  r = Show(); break;
                case "apply":
                {
                    string map, dir;
                    r = CurrentMap(out map, out dir) ? Apply(map, true) : "no ODG map is running";
                    break;
                }
                case "clear": r = Clear(); break;
                default:
                    r = "odg infil 1|2   mark this spot as ALPHA 1 / BRAVO 1\n"
                      + "odg infil show  marks on file, and what the map files say\n"
                      + "odg infil apply re-write the map files from the marks\n"
                      + "odg infil clear forget the marks, restore the shipped files";
                    break;
            }
            Say(r);
            return r;
        }

        class Mk
        {
            public string Label, Name;
            public float X, Y, Z, Yaw, BodyY, EyeY;
            public string Scene, Host, Mission, When, Source;
        }

        static string Mark(int idx)
        {
            if (!TryBind()) return "MapLoader.Mod is not loaded; nothing to mark against";
            string map, bundle;
            if (!CurrentMap(out map, out bundle)) return "no ODG map is running";

            Vector3 body; string source;
            bool haveBody = BodyPos(out body);
            Vector3 eye = Vector3.zero, fwd = Vector3.forward;
            try { var cam = Camera.main; if (cam != null) { eye = cam.transform.position; fwd = cam.transform.forward; } }
            catch { }
            if (haveBody) source = "player rig";
            else
            {

                if (eye == Vector3.zero) return "neither a player rig nor a camera could be found";
                body = new Vector3(eye.x, eye.y - EyeHeight, eye.z);
                source = "camera minus " + EyeHeight.ToString("F2") + " m (no rig resolved)";
            }

            float floorY; string on;
            bool haveFloor = FloorUnder(body, out floorY, out on);
            float y = haveFloor ? floorY : body.y;
            float yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            if (yaw < 0) yaw += 360f;

            var mk = new Mk
            {
                Label = Labels[idx], Name = Names[idx],
                X = body.x, Y = y, Z = body.z, Yaw = yaw,
                BodyY = body.y, EyeY = eye.y,
                Scene = map, Host = ActiveScene(), Mission = MissionOf(map),
                When = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Source = source,
            };

            var marks = LoadMarks(map);
            while (marks.Count <= idx) marks.Add(null);
            marks[idx] = mk;
            if (marks.Count > 2) marks.RemoveRange(2, marks.Count - 2);
            SaveMarks(map, marks);

            var sb = new StringBuilder();
            sb.Append("MARKED ").Append(mk.Label).Append(" on '").Append(map).Append("' (")
              .Append(mk.Mission).Append(")\n");
            sb.Append("  position   (").Append(F(mk.X)).Append(", ").Append(F(mk.Y)).Append(", ")
              .Append(F(mk.Z)).Append(")  facing ").Append(F(mk.Yaw, 0)).Append(" deg\n");
            sb.Append("  body y ").Append(F(mk.BodyY)).Append("  floor ")
              .Append(haveFloor ? F(floorY) + " on '" + on + "'" : "NOT FOUND (using body y)")
              .Append("  eye y ").Append(F(mk.EyeY)).Append("  from ").Append(source).Append('\n');
            sb.Append("  host scene '").Append(mk.Host).Append("'  ").Append(mk.When).Append('\n');
            sb.Append(Apply(map, false));
            AppendLog(map, sb.ToString());
            return sb.ToString();
        }

        static bool FloorUnder(Vector3 at, out float y, out string what)
        {
            y = 0f; what = null;
            try
            {

                var hits = new Il2CppStructArray<RaycastHit>(16L);
                var origin = new Vector3(at.x, at.y + 0.6f, at.z);
                int n = Physics.RaycastNonAlloc(origin, Vector3.down, hits, 8f, ~0,
                                                QueryTriggerInteraction.Ignore);
                float best = float.NegativeInfinity; Collider bc = null;
                for (int i = 0; i < n && i < hits.Length; i++)
                {
                    var h = hits[i];
                    var c = h.collider;
                    if (c == null) continue;
                    if (h.point.y > origin.y) continue;
                    if (h.point.y > best) { best = h.point.y; bc = c; }
                }
                if (best == float.NegativeInfinity) return false;
                y = best;
                try { what = bc != null ? bc.name : "?"; } catch { what = "?"; }
                return true;
            }
            catch { return false; }
        }

        static bool CurrentMap(out string map, out string bundleDir)
        {
            map = null; bundleDir = null;
            try
            {
                if (_tIdentity == null) return false;
                var pc = _tIdentity.GetProperty("Current",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var cur = pc != null ? pc.GetValue(null, null) : null;
                if (cur == null) return false;
                var tt = cur.GetType();
                map = tt.GetField("Name").GetValue(cur) as string;
                bundleDir = tt.GetField("BundleDir").GetValue(cur) as string;
                return !string.IsNullOrEmpty(map);
            }
            catch { return false; }
        }

        static string MissionOf(string map)
        {
            try
            {
                var f = _tOps.GetField("_ops", BindingFlags.Static | BindingFlags.NonPublic);
                var list = f != null ? f.GetValue(null) as System.Collections.IEnumerable : null;
                if (list == null) return "?";
                foreach (var o in list)
                {
                    if (o == null) continue;
                    var ot = o.GetType();
                    var sn = ot.GetField("SceneName").GetValue(o) as string;
                    if (string.Equals(sn, map, StringComparison.OrdinalIgnoreCase))
                        return ot.GetField("PackageName").GetValue(o) as string ?? "?";
                }
            }
            catch { }
            return "?";
        }

        static bool BodyPos(out Vector3 p)
        {
            p = Vector3.zero;
            try
            {
                if (_tPlayerBody == null) return false;
                var m = _tPlayerBody.GetMethod("TryPosition", BindingFlags.Public | BindingFlags.Static);
                if (m == null) return false;
                var args = new object[] { null };
                bool ok = (bool)m.Invoke(null, args);
                if (ok) p = (Vector3)args[0];
                return ok;
            }
            catch { return false; }
        }

        static string ActiveScene()
        {
            try { return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; }
            catch { return "?"; }
        }

        static void Invalidate(string map)
        {
            try
            {
                var m = _tInfils != null ? _tInfils.GetMethod("Invalidate", BindingFlags.Public | BindingFlags.Static) : null;
                if (m != null) m.Invoke(null, new object[] { map });
            }
            catch (Exception e) { _log.Warning("invalidate: " + e.Message); }
        }

        static string GameRoot()
        {
            try { return MelonLoader.Utils.MelonEnvironment.GameRootDirectory; }
            catch { return Path.GetDirectoryName(Application.dataPath); }
        }
        static string AssetsRoot() { return Path.Combine(GameRoot(), "Mods", "MapLoader_Assets"); }
        static string MarksDir()
        {
            var d = Path.Combine(GameRoot(), "UserData", "ODG_Infils");
            Directory.CreateDirectory(d);
            return d;
        }
        static string MarksFile(string map) { return Path.Combine(MarksDir(), map + ".marks"); }
        static string GpFile(string map)    { return Path.Combine(AssetsRoot(), map, "gameplay.bin"); }
        static string PlFile(string map)    { return Path.Combine(AssetsRoot(), "placements", map + ".json"); }
        static string TxtFile(string map)   { return Path.Combine(AssetsRoot(), "preview", map + ".txt"); }
        static string PngFile(string map)   { return Path.Combine(AssetsRoot(), "preview", map + ".png"); }

        static void AppendLog(string map, string text)
        {
            try { File.AppendAllText(Path.Combine(MarksDir(), "marks.log"), text + "\n"); } catch { }
        }

        static List<Mk> LoadMarks(string map)
        {
            var list = new List<Mk>();
            try
            {
                var f = MarksFile(map);
                if (!File.Exists(f)) return list;
                foreach (var raw in File.ReadAllLines(f))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    var c = line.Split('\t');
                    if (c.Length < 6) continue;
                    var m = new Mk
                    {
                        Label = c[0], Name = c[1],
                        X = P(c[2]), Y = P(c[3]), Z = P(c[4]), Yaw = P(c[5]),
                        BodyY = c.Length > 6 ? P(c[6]) : 0f, EyeY = c.Length > 7 ? P(c[7]) : 0f,
                        Scene = map, Host = c.Length > 8 ? c[8] : "", Mission = c.Length > 9 ? c[9] : "",
                        When = c.Length > 10 ? c[10] : "", Source = c.Length > 11 ? c[11] : "",
                    };
                    list.Add(m);
                }
            }
            catch (Exception e) { _log.Warning("marks: " + e.Message); }
            return list;
        }

        static void SaveMarks(string map, List<Mk> marks)
        {
            var sb = new StringBuilder();
            sb.Append("# ODG infil marks for ").Append(map)
              .Append(". label\\tname\\tx\\ty\\tz\\tyaw\\tbodyY\\teyeY\\thost\\tmission\\twhen\\tsource\n");
            foreach (var m in marks)
            {
                if (m == null) continue;
                sb.Append(m.Label).Append('\t').Append(m.Name).Append('\t')
                  .Append(F(m.X)).Append('\t').Append(F(m.Y)).Append('\t').Append(F(m.Z)).Append('\t')
                  .Append(F(m.Yaw)).Append('\t').Append(F(m.BodyY)).Append('\t').Append(F(m.EyeY)).Append('\t')
                  .Append(m.Host).Append('\t').Append(m.Mission).Append('\t').Append(m.When).Append('\t')
                  .Append(m.Source).Append('\n');
            }
            File.WriteAllText(MarksFile(map), sb.ToString());
        }

        static string Apply(string map, bool verbose)
        {
            var marks = LoadMarks(map);
            marks.RemoveAll(m => m == null);
            if (marks.Count == 0) return "no marks on file for '" + map + "'";

            var sb = new StringBuilder();
            string gp = GpFile(map), pl = PlFile(map), txt = TxtFile(map), png = PngFile(map);
            if (!File.Exists(gp)) return "'" + map + "' has no gameplay.bin at " + gp + "; is the map installed?";

            Backup(gp); Backup(pl); Backup(txt); Backup(png);

            try
            {
                var groups = ReadGp(gp);
                var ins = new List<GpEntry>();
                foreach (var m in marks)
                {
                    float h = m.Yaw * Mathf.Deg2Rad * 0.5f;
                    ins.Add(new GpEntry
                    {
                        Name = m.Name, Label = m.Label,
                        Pos = new[] { m.X, m.Y, m.Z },
                        Rot = new[] { 0f, Mathf.Sin(h), 0f, Mathf.Cos(h) },
                        Scl = new[] { 1f, 1f, 1f },
                    });
                }
                bool put = false;
                for (int i = 0; i < groups.Count; i++)
                    if (groups[i].Key == "GBInsertionPoint") { groups[i] = new KeyValuePair<string, List<GpEntry>>("GBInsertionPoint", ins); put = true; }
                if (!put) groups.Insert(0, new KeyValuePair<string, List<GpEntry>>("GBInsertionPoint", ins));
                WriteGp(gp, groups);
                sb.Append("  gameplay.bin: ").Append(ins.Count).Append(" insertion(s) written\n");
            }
            catch (Exception e) { sb.Append("  gameplay.bin FAILED: ").Append(e.Message).Append('\n'); }

            try
            {
                var a = marks[0];
                string js = File.Exists(pl) ? File.ReadAllText(pl) : "{\n}";
                string spawn = "\"playerSpawn\": {\"x\": " + F(a.X, 1) + ", \"y\": " + F(a.Y, 2)
                             + ", \"z\": " + F(a.Z, 1) + "}";
                var rx = new Regex("\"playerSpawn\"\\s*:\\s*\\{[^}]*\\}");
                if (rx.IsMatch(js)) js = rx.Replace(js, spawn, 1);
                else
                {
                    int i = js.IndexOf('{');
                    js = i >= 0 ? js.Substring(0, i + 1) + "\n " + spawn + "," + js.Substring(i + 1) : "{\n " + spawn + "\n}";
                }
                Directory.CreateDirectory(Path.GetDirectoryName(pl));
                File.WriteAllText(pl, js);
                sb.Append("  placements: playerSpawn -> (").Append(F(a.X, 1)).Append(", ").Append(F(a.Y, 2))
                  .Append(", ").Append(F(a.Z, 1)).Append(")\n");
            }
            catch (Exception e) { sb.Append("  placements FAILED: ").Append(e.Message).Append('\n'); }

            try
            {
                if (!File.Exists(txt))
                    sb.Append("  preview: no ").Append(Path.GetFileName(txt)).Append(" -- the board cannot show a marker for this map\n");
                else
                {
                    float cx = 0, cz = 0, S = 0; bool haveProj = false;
                    var keep = new List<string>();
                    var oldUv = new List<Vector2>();
                    string origTxt = txt + ".orig";
                    foreach (var raw in File.ReadAllLines(File.Exists(origTxt) ? origTxt : txt))
                    {
                        var line = raw.TrimEnd();
                        var f = line.Split('\t');
                        if (f.Length >= 4 && f[0] == "proj")
                        { cx = P(f[1]); cz = P(f[2]); S = P(f[3]); haveProj = S > 0f; keep.Add(line); }
                        else if (f.Length >= 7 && f[0] == "infil") oldUv.Add(new Vector2(P(f[2]), P(f[3])));
                        else keep.Add(line);
                    }
                    if (!haveProj)
                        sb.Append("  preview: ").Append(Path.GetFileName(txt)).Append(" has no proj line; markers left as shipped\n");
                    else
                    {
                        var newUv = new List<Vector2>();
                        foreach (var m in marks)
                        {
                            float u = (m.X - cx) / S + 0.5f, v = (m.Z - cz) / S + 0.5f;
                            newUv.Add(new Vector2(u, v));
                            keep.Add("infil\t" + m.Label + "\t" + F(u, 5) + "\t" + F(v, 5) + "\t"
                                     + F(m.X, 2) + "\t" + F(m.Y, 2) + "\t" + F(m.Z, 2));
                            if (u < 0f || u > 1f || v < 0f || v > 1f)
                                sb.Append("  preview: ").Append(m.Label).Append(" is OFF the plan view (u ")
                                  .Append(F(u)).Append(" v ").Append(F(v)).Append(")\n");
                        }
                        File.WriteAllText(txt, string.Join("\n", keep.ToArray()) + "\n");
                        sb.Append("  preview: ").Append(marks.Count).Append(" marker(s) on the board\n");
                        try
                        {
                            string origPng = png + ".orig";
                            if (File.Exists(origPng)) { Repaint(origPng, png, oldUv, newUv); sb.Append("  preview: rings repainted\n"); }
                        }
                        catch (Exception e) { sb.Append("  preview png not repainted: ").Append(e.Message).Append('\n'); }
                    }
                }
            }
            catch (Exception e) { sb.Append("  preview FAILED: ").Append(e.Message).Append('\n'); }

            Invalidate(map);
            sb.Append("  the board rebuilds its map on next open; the mission uses it on next launch");
            if (verbose) sb.Insert(0, "applied " + marks.Count + " mark(s) to '" + map + "'\n");
            return sb.ToString();
        }

        static void ApplyAll()
        {
            if (!TryBind()) return;
            int n = 0;
            foreach (var f in Directory.GetFiles(MarksDir(), "*.marks"))
            {
                var map = Path.GetFileNameWithoutExtension(f);
                if (!File.Exists(GpFile(map))) continue;
                Say("re-applying marks for '" + map + "':\n" + Apply(map, false));
                n++;
            }
            if (n > 0) _log.Msg(n + " mark set(s) re-applied at the menu, so a launcher update cannot put the shipped insertions back.");
        }

        static string Show()
        {
            string map, bundle;
            if (!CurrentMap(out map, out bundle)) return "no ODG map is running";
            var sb = new StringBuilder();
            sb.Append("'").Append(map).Append("' (").Append(MissionOf(map)).Append(")\n");
            var marks = LoadMarks(map);
            if (marks.Count == 0) sb.Append("  marks: none on file\n");
            foreach (var m in marks)
                if (m != null)
                    sb.Append("  mark ").Append(m.Label).Append("  (").Append(F(m.X)).Append(", ").Append(F(m.Y))
                      .Append(", ").Append(F(m.Z)).Append(")  facing ").Append(F(m.Yaw, 0)).Append("  ")
                      .Append(m.When).Append('\n');
            try
            {
                foreach (var g in ReadGp(GpFile(map)))
                    if (g.Key == "GBInsertionPoint")
                        foreach (var e in g.Value)
                            sb.Append("  gameplay.bin ").Append(e.Label.Length > 0 ? e.Label : e.Name).Append("  (")
                              .Append(F(e.Pos[0])).Append(", ").Append(F(e.Pos[1])).Append(", ").Append(F(e.Pos[2])).Append(")\n");
            }
            catch (Exception e) { sb.Append("  gameplay.bin unreadable: ").Append(e.Message).Append('\n'); }
            try
            {
                var pl = PlFile(map);
                if (File.Exists(pl))
                {
                    var m = Regex.Match(File.ReadAllText(pl), "\"playerSpawn\"\\s*:\\s*\\{[^}]*\\}");
                    sb.Append("  placements ").Append(m.Success ? m.Value : "(no playerSpawn)").Append('\n');
                }
            }
            catch { }
            try
            {
                var txt = TxtFile(map);
                if (File.Exists(txt))
                    foreach (var line in File.ReadAllLines(txt))
                        if (line.StartsWith("infil\t")) sb.Append("  board ").Append(line.Replace('\t', ' ')).Append('\n');
            }
            catch { }
            return sb.ToString().TrimEnd();
        }

        static string Clear()
        {
            string map, bundle;
            if (!CurrentMap(out map, out bundle)) return "no ODG map is running";
            try { var f = MarksFile(map); if (File.Exists(f)) File.Delete(f); } catch { }
            int restored = 0;
            foreach (var f in new[] { GpFile(map), PlFile(map), TxtFile(map), PngFile(map) })
            {
                try
                {
                    if (File.Exists(f + ".orig")) { File.Copy(f + ".orig", f, true); File.Delete(f + ".orig"); restored++; }
                }
                catch (Exception e) { _log.Warning("restore " + f + ": " + e.Message); }
            }
            Invalidate(map);
            return "marks cleared for '" + map + "'; " + restored + " shipped file(s) restored";
        }

        static void Backup(string f)
        {
            try { if (File.Exists(f) && !File.Exists(f + ".orig")) File.Copy(f, f + ".orig"); } catch { }
        }

        static void Repaint(string origPng, string outPng, List<Vector2> oldUv, List<Vector2> newUv)
        {
            var bytes = File.ReadAllBytes(origPng);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(tex, (Il2CppStructArray<byte>)bytes))
                throw new Exception("LoadImage refused " + Path.GetFileName(origPng));
            int w = tex.width, h = tex.height;

            foreach (var uv in oldUv)
            {
                int cx = Mathf.RoundToInt(uv.x * w), cy = Mathf.RoundToInt(uv.y * h);
                int x0 = Mathf.Clamp(cx - 12, 0, w - 1), x1 = Mathf.Clamp(cx + 76, 0, w - 1);
                int y0 = Mathf.Clamp(cy - 14, 0, h - 1), y1 = Mathf.Clamp(cy + 14, 0, h - 1);
                var fill = tex.GetPixel(Mathf.Clamp(x0 - 2, 0, w - 1), Mathf.Clamp(y0 - 2, 0, h - 1));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                        tex.SetPixel(x, y, fill);
            }
            var ring = new Color(1f, 0.78f, 0.35f, 1f);
            foreach (var uv in newUv)
            {
                int cx = Mathf.RoundToInt(uv.x * w), cy = Mathf.RoundToInt(uv.y * h);
                for (int y = -10; y <= 10; y++)
                    for (int x = -10; x <= 10; x++)
                    {
                        float d = Mathf.Sqrt(x * x + y * y);
                        if (d < 5f || d > 8f) continue;
                        int px = cx + x, py = cy + y;
                        if (px < 0 || py < 0 || px >= w || py >= h) continue;
                        tex.SetPixel(px, py, ring);
                    }
            }
            tex.Apply();
            var enc = ImageConversion.EncodeToPNG(tex);
            var outb = new byte[enc.Length];
            for (int i = 0; i < outb.Length; i++) outb[i] = enc[i];
            File.WriteAllBytes(outPng, outb);
            try { UnityEngine.Object.Destroy(tex); } catch { }
        }

        class GpEntry
        {
            public string Name = "", Label = "";
            public float[] Pos = new float[3], Rot = new float[4], Scl = new float[3];
        }

        static List<KeyValuePair<string, List<GpEntry>>> ReadGp(string path)
        {
            var groups = new List<KeyValuePair<string, List<GpEntry>>>();
            using (var br = new BinaryReader(File.OpenRead(path)))
            {
                var magic = br.ReadBytes(4);
                if (Encoding.ASCII.GetString(magic) != "MGP1") throw new Exception("not an MGP1 file");
                int ng = br.ReadInt32();
                for (int g = 0; g < ng; g++)
                {
                    string cls = S(br);
                    int n = br.ReadInt32();
                    var list = new List<GpEntry>();
                    for (int i = 0; i < n; i++)
                    {
                        var e = new GpEntry { Name = S(br), Label = S(br) };
                        for (int k = 0; k < 3; k++) e.Pos[k] = br.ReadSingle();
                        for (int k = 0; k < 4; k++) e.Rot[k] = br.ReadSingle();
                        for (int k = 0; k < 3; k++) e.Scl[k] = br.ReadSingle();
                        list.Add(e);
                    }
                    groups.Add(new KeyValuePair<string, List<GpEntry>>(cls, list));
                }
            }
            return groups;
        }

        static void WriteGp(string path, List<KeyValuePair<string, List<GpEntry>>> groups)
        {
            var tmp = path + ".tmp";
            using (var bw = new BinaryWriter(File.Create(tmp)))
            {
                bw.Write(Encoding.ASCII.GetBytes("MGP1"));
                bw.Write(groups.Count);
                foreach (var g in groups)
                {
                    W(bw, g.Key);
                    bw.Write(g.Value.Count);
                    foreach (var e in g.Value)
                    {
                        W(bw, e.Name); W(bw, e.Label);
                        for (int k = 0; k < 3; k++) bw.Write(e.Pos[k]);
                        for (int k = 0; k < 4; k++) bw.Write(e.Rot[k]);
                        for (int k = 0; k < 3; k++) bw.Write(e.Scl[k]);
                    }
                }
            }
            File.Copy(tmp, path, true);
            File.Delete(tmp);
        }

        static string S(BinaryReader br)
        {
            int len = br.ReadUInt16();
            return Encoding.UTF8.GetString(br.ReadBytes(len));
        }
        static void W(BinaryWriter bw, string s)
        {
            var b = Encoding.UTF8.GetBytes(s ?? "");
            bw.Write((ushort)b.Length);
            bw.Write(b);
        }

        static float P(string s)
        {
            float v;
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0f;
        }
        static string F(float v, int d = 2) { return v.ToString("F" + d, CultureInfo.InvariantCulture); }
    }
}
