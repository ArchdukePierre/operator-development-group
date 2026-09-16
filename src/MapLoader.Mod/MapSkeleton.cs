// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using MelonLoader;

namespace MapLoader
{

    public static class MapSkeleton
    {
        static MelonLogger.Instance _log;
        static bool _done;
        static float _next;

        public static void Init(MelonLogger.Instance log) { _log = log; }
        public static void ResetForNewLevel() { _done = false; _next = 0f; }

        public static void Tick()
        {
            if (_done) return;
            if (!MapRuntime.Done) return;
            float now = Time.realtimeSinceStartup;
            if (now < _next) return;
            _next = now + 2f;
            try { Dump(); } catch (Exception e) { _log?.Warning("[skel] " + e.Message); }
        }

        static void Dump()
        {
            var brains = UnityEngine.Object.FindObjectsOfType<Il2Cpp.BrainAI>();
            if (brains == null || brains.Length == 0) return;

            Il2Cpp.BrainAI subject = null;
            SkinnedMeshRenderer best = null;
            int bestBones = 0;

            for (int i = 0; i < brains.Length && best == null; i++)
            {
                var b = brains[i];
                if (b == null) continue;
                SkinnedMeshRenderer[] smrs;
                try { smrs = b.GetComponentsInChildren<SkinnedMeshRenderer>(true); }
                catch { continue; }
                if (smrs == null) continue;
                for (int j = 0; j < smrs.Length; j++)
                {
                    var r = smrs[j];
                    if (r == null) continue;
                    int n = 0;
                    try
                    {
                        var bs = r.bones;
                        if (bs != null)
                        {
                            var seen = new System.Collections.Generic.HashSet<string>();
                            for (int k = 0; k < bs.Length; k++)
                                if (bs[k] != null) seen.Add(bs[k].name);
                            n = seen.Count;
                        }
                    }
                    catch { }

                    if (n > bestBones) { bestBones = n; best = r; subject = b; }
                }
            }

            if (best == null || bestBones == 0) return;
            _done = true;

            var bones = best.bones;
            var sb = new StringBuilder();
            sb.Append("{\n  \"source\": \"live bot in Farmstead\",\n");
            sb.Append("  \"renderer\": \"").Append(Esc(best.name)).Append("\",\n");
            sb.Append("  \"boneCount\": ").Append(bones.Length).Append(",\n");
            string rootName = "";
            try { rootName = best.rootBone != null ? best.rootBone.name : ""; } catch { }
            sb.Append("  \"rootBone\": \"").Append(Esc(rootName)).Append("\",\n");
            sb.Append("  \"bones\": [\n");

            for (int i = 0; i < bones.Length; i++)
            {
                var t = bones[i];
                if (t == null) { sb.Append("    null"); }
                else
                {
                    string nm = "", parent = "";
                    Vector3 lp = Vector3.zero, ls = Vector3.one;
                    Quaternion lr = Quaternion.identity;
                    try { nm = t.name; } catch { }
                    try { parent = t.parent != null ? t.parent.name : ""; } catch { }
                    try { lp = t.localPosition; lr = t.localRotation; ls = t.localScale; } catch { }
                    sb.Append("    {\"i\":").Append(i)
                      .Append(",\"name\":\"").Append(Esc(nm))
                      .Append("\",\"parent\":\"").Append(Esc(parent))
                      .Append("\",\"pos\":[").Append(F(lp.x)).Append(',').Append(F(lp.y)).Append(',').Append(F(lp.z))
                      .Append("],\"rot\":[").Append(F(lr.x)).Append(',').Append(F(lr.y)).Append(',')
                      .Append(F(lr.z)).Append(',').Append(F(lr.w))
                      .Append("],\"scale\":[").Append(F(ls.x)).Append(',').Append(F(ls.y)).Append(',').Append(F(ls.z))
                      .Append("]}");
                }
                if (i < bones.Length - 1) sb.Append(',');
                sb.Append('\n');
            }
            sb.Append("  ],\n");

            sb.Append("  \"bindposes\": [\n");
            try
            {
                var bp = best.sharedMesh != null ? best.sharedMesh.bindposes : null;
                int bn = bp != null ? bp.Length : 0;
                int wrote = 0;
                var done = new System.Collections.Generic.HashSet<string>();
                for (int i = 0; i < bn && i < bones.Length; i++)
                {
                    var t = bones[i];
                    if (t == null) continue;
                    string nm = t.name;
                    if (!done.Add(nm)) continue;
                    var m = bp[i];
                    if (wrote > 0) sb.Append(",\n");
                    sb.Append("    {\"name\":\"").Append(Esc(nm)).Append("\",\"m\":[");
                    for (int c = 0; c < 4; c++)
                        for (int r = 0; r < 4; r++)
                        {
                            if (c > 0 || r > 0) sb.Append(',');
                            sb.Append(F(m[r, c]));
                        }
                    sb.Append("]}");
                    wrote++;
                }
                _log?.Msg("[skel] bind poses written for " + wrote + " bone(s)");
            }
            catch (Exception e) { _log?.Warning("[skel] bindposes: " + e.Message); }
            sb.Append("\n  ],\n");

            sb.Append("  \"renderers\": [\n");
            var all = subject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < all.Length; i++)
            {
                var r = all[i];
                string nm = "", mesh = "";
                int nb = 0, verts = 0;
                try { nm = r.name; } catch { }
                try { nb = r.bones != null ? r.bones.Length : 0; } catch { }
                try { mesh = r.sharedMesh != null ? r.sharedMesh.name : ""; } catch { }
                try { verts = r.sharedMesh != null ? r.sharedMesh.vertexCount : 0; } catch { }
                sb.Append("    {\"name\":\"").Append(Esc(nm))
                  .Append("\",\"mesh\":\"").Append(Esc(mesh))
                  .Append("\",\"bones\":").Append(nb)
                  .Append(",\"verts\":").Append(verts).Append('}');
                if (i < all.Length - 1) sb.Append(',');
                sb.Append('\n');
            }
            sb.Append("  ]\n}\n");

            string dir = null;
            try
            {
                var root = Path.GetDirectoryName(Application.dataPath);
                dir = Path.Combine(root, "Mods", "MapLoader_Assets", "skeleton");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "ai_skeleton.json"), sb.ToString(),
                                  new UTF8Encoding(false));
            }
            catch (Exception e) { _log?.Warning("[skel] write: " + e.Message); return; }

            _log?.Msg("[skel] wrote the AI armature (" + bones.Length + " bones, "
                    + all.Length + " skinned renderer(s)) to " + dir
                    + "\\ai_skeleton.json -- this is what the custom body gets rigged to.");
        }

        static string F(float v) => v.ToString("0.######", CultureInfo.InvariantCulture);
        static string Esc(string s) => s == null ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
