// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using UnityEngine;
using MelonLoader;
using Arr = Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace MapLoader
{

    public static class MapBody
    {
        static MelonLogger.Instance _log;

        public static float Smoothness = 0.05f;

        public static void Init(MelonLogger.Instance log)
        {
            _log = log;
            try
            {
                var cfg = MelonPreferences.CreateCategory("MapLoader");
                var e = cfg.CreateEntry("OsamaSmoothness", 0.05f,
                    description: "0 = fully matte, 1 = glossy. One value for the whole body.");
                Smoothness = Mathf.Clamp01(e.Value);
            }
            catch { }
        }

        class Skin
        {
            public Mesh MeshObj;
            public Material[] Mats;
            public string[] BoneNames;
        }
        static Skin _skin;
        static bool _tried;

        public static void ResetForNewLevel()
        {
            _skin = null;
            _tried = false;
            _worn = null;
            _issued = null;
        }

        static SkinnedMeshRenderer _worn;
        static Arr.Il2CppArrayBase<SkinnedMeshRenderer> _issued;
        static float _nextHide;

        public static string Describe()
        {
            try
            {
                if (_worn == null) return "body: not fitted on this machine";
                Vector3 c = _worn.bounds.center;
                string owner = "?";
                Vector3 at = c;
                try
                {
                    var r = _worn.transform.root;
                    owner = r.name; at = r.position;
                }
                catch { }
                float gap = (c - at).magnitude;
                return "body: fitted to '" + owner + "' at (" + at.x.ToString("F1") + ", "
                     + at.y.ToString("F1") + ", " + at.z.ToString("F1") + "), draws at ("
                     + c.x.ToString("F1") + ", " + c.y.ToString("F1") + ", "
                     + c.z.ToString("F1") + "), gap " + gap.ToString("F1") + " m"
                     + (gap > 3f ? "  <-- ADRIFT, wrong skeleton" : "  (correct)")
                     + ", hiding " + (_issued != null ? _issued.Length : 0) + " issued renderer(s)";
            }
            catch (Exception e) { return "body: unreadable (" + e.Message + ")"; }
        }

        static int HideIssued()
        {
            int n = 0;
            if (_issued == null) return 0;
            for (int i = 0; i < _issued.Length; i++)
            {
                var r = _issued[i];
                if (r == null || r == _worn) continue;
                try { if (r.enabled) { r.enabled = false; n++; } } catch { }
            }
            return n;
        }

        public static void Tick()
        {
            if (_issued == null || _worn == null) return;
            float now = Time.realtimeSinceStartup;
            if (now < _nextHide) return;
            _nextHide = now + 0.5f;
            try
            {
                int n = HideIssued();
                if (n > 0) _log?.Msg("[body] the game switched " + n
                                   + " issued renderer(s) back on; hidden again.");
            }
            catch { }
        }

        static string AssetDir()
        {
            try
            {
                var root = Path.GetDirectoryName(Application.dataPath);
                var p = Path.Combine(root, "Mods", "MapLoader_Assets", "characters");
                if (Directory.Exists(p)) return p;
            }
            catch { }
            try
            {
                var p = Path.Combine(Path.GetDirectoryName(MapRuntime.BundleDir.TrimEnd('\\')),
                                     "characters");
                if (Directory.Exists(p)) return p;
            }
            catch { }
            return null;
        }

        static Skin Load()
        {
            if (_tried) return _skin;
            _tried = true;

            var dir = AssetDir();
            if (dir == null) { _log?.Msg("[body] no characters folder; the custom body is not installed"); return null; }
            var file = Path.Combine(dir, "osama.odgchar");
            if (!File.Exists(file)) { _log?.Msg("[body] no osama.odgchar in " + dir); return null; }

            try
            {
                var raw = File.ReadAllBytes(file);

                try
                {
                    using (var md5 = System.Security.Cryptography.MD5.Create())
                        _log?.Msg("[body] osama.odgchar md5 "
                            + BitConverter.ToString(md5.ComputeHash(raw), 0, 5).Replace("-", "").ToLowerInvariant());
                }
                catch { }
                int o = 0;
                if (raw.Length < 8 || System.Text.Encoding.ASCII.GetString(raw, 0, 8) != "ODGCHAR3")
                { _log?.Error("[body] not an ODGCHAR3 file (rebuild the asset)"); return null; }
                o = 8;

                int nn = BitConverter.ToInt32(raw, o); o += 4;
                var boneNames = new string[nn];
                for (int i = 0; i < nn; i++)
                {
                    int len = BitConverter.ToInt32(raw, o); o += 4;
                    boneNames[i] = System.Text.Encoding.UTF8.GetString(raw, o, len); o += len;
                }
                int nv = BitConverter.ToInt32(raw, o); o += 4;
                int nb = BitConverter.ToInt32(raw, o); o += 4;
                int ns = BitConverter.ToInt32(raw, o); o += 4;

                var verts = new Arr.Il2CppStructArray<Vector3>(nv);
                var norms = new Arr.Il2CppStructArray<Vector3>(nv);
                var uvs   = new Arr.Il2CppStructArray<Vector2>(nv);
                for (int i = 0; i < nv; i++)
                {
                    verts[i] = new Vector3(BitConverter.ToSingle(raw, o), BitConverter.ToSingle(raw, o + 4),
                                           BitConverter.ToSingle(raw, o + 8)); o += 12;
                }
                for (int i = 0; i < nv; i++)
                {
                    norms[i] = new Vector3(BitConverter.ToSingle(raw, o), BitConverter.ToSingle(raw, o + 4),
                                           BitConverter.ToSingle(raw, o + 8)); o += 12;
                }
                for (int i = 0; i < nv; i++)
                {
                    uvs[i] = new Vector2(BitConverter.ToSingle(raw, o), BitConverter.ToSingle(raw, o + 4));
                    o += 8;
                }

                var bwts = new Arr.Il2CppStructArray<BoneWeight>(nv);
                int idxBase = o, wBase = o + nv * 16;
                for (int i = 0; i < nv; i++)
                {
                    var bw = new BoneWeight();
                    bw.boneIndex0 = BitConverter.ToInt32(raw, idxBase + i * 16 + 0);
                    bw.boneIndex1 = BitConverter.ToInt32(raw, idxBase + i * 16 + 4);
                    bw.boneIndex2 = BitConverter.ToInt32(raw, idxBase + i * 16 + 8);
                    bw.boneIndex3 = BitConverter.ToInt32(raw, idxBase + i * 16 + 12);
                    bw.weight0 = BitConverter.ToSingle(raw, wBase + i * 16 + 0);
                    bw.weight1 = BitConverter.ToSingle(raw, wBase + i * 16 + 4);
                    bw.weight2 = BitConverter.ToSingle(raw, wBase + i * 16 + 8);
                    bw.weight3 = BitConverter.ToSingle(raw, wBase + i * 16 + 12);
                    bwts[i] = bw;
                }
                o = wBase + nv * 16;

                var binds = new Arr.Il2CppStructArray<Matrix4x4>(nb);
                for (int i = 0; i < nb; i++)
                {
                    var m = new Matrix4x4();
                    for (int c = 0; c < 4; c++)
                        for (int r = 0; r < 4; r++)
                        { m[r, c] = BitConverter.ToSingle(raw, o); o += 4; }
                    binds[i] = m;
                }

                var mesh = new Mesh();
                mesh.name = "ODG_Osama";
                mesh.hideFlags = HideFlags.DontUnloadUnusedAsset;
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.vertices = verts;
                mesh.normals = norms;
                mesh.uv = uvs;
                mesh.boneWeights = bwts;
                mesh.bindposes = binds;
                mesh.subMeshCount = ns;

                var mats = new Material[ns];
                var shader = Shader.Find("HDRP/Lit");
                for (int s = 0; s < ns; s++)
                {
                    int ni = BitConverter.ToInt32(raw, o); o += 4;
                    var tri = new Arr.Il2CppStructArray<int>(ni);
                    for (int i = 0; i < ni; i++) { tri[i] = BitConverter.ToInt32(raw, o); o += 4; }
                    mesh.SetTriangles(tri, s, true);

                    int tn = BitConverter.ToInt32(raw, o); o += 4;
                    string texName = tn > 0 ? System.Text.Encoding.UTF8.GetString(raw, o, tn) : "";
                    o += tn;

                    var authored = new Color(BitConverter.ToSingle(raw, o),
                                             BitConverter.ToSingle(raw, o + 4),
                                             BitConverter.ToSingle(raw, o + 8),
                                             BitConverter.ToSingle(raw, o + 12));

                    o += 20;

                    var mat = new Material(shader);
                    mat.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    var tex = LoadTexture(dir, texName);
                    if (tex != null)
                    {

                        try { mat.SetTexture("_BaseColorMap", tex); } catch { }
                        try { mat.mainTexture = tex; } catch { }
                        try { mat.SetColor("_BaseColor", Color.white); } catch { }
                    }
                    else
                    {
                        try { mat.SetColor("_BaseColor", authored); } catch { }
                    }

                    try { mat.SetFloat("_Smoothness", Smoothness); } catch { }

                    try
                    {
                        mat.SetFloat("_CullMode", 0f);
                        mat.SetFloat("_CullModeForward", 0f);
                        mat.SetFloat("_DoubleSidedEnable", 1f);
                        mat.EnableKeyword("_DOUBLESIDED_ON");
                    }
                    catch { }

                    try { mat.SetFloat("_SpecularOcclusionMode", 0f); } catch { }
                    try { mat.SetFloat("_Metallic", 0f); } catch { }

                    try { mat.SetFloat("_DoubleSidedEnable", 1f); } catch { }
                    try { mat.SetFloat("_CullMode", 0f); } catch { }
                    try { mat.SetFloat("_CullModeForward", 0f); } catch { }
                    try { mat.EnableKeyword("_DOUBLESIDED_ON"); } catch { }
                    try { mat.SetFloat("_DoubleSidedNormalMode", 1f); } catch { }
                    mats[s] = mat;
                }
                mesh.RecalculateBounds();

                try { mesh.RecalculateTangents(); } catch { }

                _skin = new Skin { MeshObj = mesh, Mats = mats, BoneNames = boneNames };
                _log?.Msg("[body] shader HDRP/Lit resolved: " + (shader != null));
                _log?.Msg("[body] loaded osama.odgchar: " + nv + " verts, " + nb
                        + " bones, " + ns + " submesh(es)");
                return _skin;
            }
            catch (Exception e) { _log?.Error("[body] load: " + e.Message); return null; }
        }

        static Texture2D LoadTexture(string dir, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            try
            {
                var f = Path.Combine(dir, name);
                if (!File.Exists(f)) { _log?.Warning("[body] missing texture " + name); return null; }
                var bytes = File.ReadAllBytes(f);
                var arr = new Arr.Il2CppStructArray<byte>(bytes.Length);
                for (int i = 0; i < bytes.Length; i++) arr[i] = bytes[i];
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                t.hideFlags = HideFlags.DontUnloadUnusedAsset;
                if (!ImageConversion.LoadImage(t, arr)) { _log?.Warning("[body] decode failed " + name); return null; }
                return t;
            }
            catch (Exception e) { _log?.Warning("[body] texture " + name + ": " + e.Message); return null; }
        }

        public static bool Wear(Il2Cpp.BrainAI who)
        {
            if (who == null) return false;
            var skin = Load();
            if (skin == null) return false;

            try
            {
                var smrs = who.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (smrs == null || smrs.Length == 0) return false;

                var byName = new System.Collections.Generic.Dictionary<string, Transform>();
                var all = who.transform.root.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    var t = all[i];
                    if (t == null) continue;
                    var n = t.name;
                    if (!byName.ContainsKey(n)) byName[n] = t;
                }

                int need = skin.BoneNames.Length;
                var bones = new Arr.Il2CppReferenceArray<Transform>(need);
                int missing = 0;
                string firstMissing = null;
                for (int i = 0; i < need; i++)
                {
                    Transform t;
                    if (byName.TryGetValue(skin.BoneNames[i], out t)) bones[i] = t;
                    else { missing++; if (firstMissing == null) firstMissing = skin.BoneNames[i]; }
                }
                if (missing > 0)
                {
                    _log?.Error("[body] " + missing + " of " + need + " bones not found on this "
                              + "character (first: '" + firstMissing + "'). Not grafting: a partial "
                              + "skeleton renders as nothing at all, which is what an invisible "
                              + "body looks like.");
                    return false;
                }

                Transform rootBone = null;
                byName.TryGetValue(skin.BoneNames[0], out rootBone);

                var go = new GameObject("ODG_OsamaBody");
                go.transform.SetParent(who.transform, false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                var smr = go.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = skin.MeshObj;
                smr.bones = bones;
                smr.rootBone = rootBone;
                var mats = new Arr.Il2CppReferenceArray<Material>(skin.Mats.Length);
                for (int i = 0; i < skin.Mats.Length; i++) mats[i] = skin.Mats[i];
                smr.sharedMaterials = mats;
                smr.updateWhenOffscreen = true;

                Vector3 wantAt;
                try { wantAt = who.transform.root.position; }
                catch { wantAt = Vector3.zero; }
                Vector3 landedAt;
                try { landedAt = smr.bounds.center; } catch { landedAt = wantAt; }
                float miss = (landedAt - wantAt).magnitude;
                if (miss > 4f)
                {
                    _log?.Error("[body] REFUSED: the body drew at " + landedAt
                              + " but the character is at " + wantAt + ", "
                              + miss.ToString("F1") + " m away. That is the wrong "
                              + "skeleton, so this is almost certainly not the target "
                              + "yet. Undoing and leaving him in his issued kit; the "
                              + "caller will try again.");
                    try { UnityEngine.Object.Destroy(smr); } catch { }
                    try { if (go != null) UnityEngine.Object.Destroy(go); } catch { }
                    _worn = null;
                    _issued = null;
                    return false;
                }

                _worn = smr;
                _issued = smrs;
                int hidden = HideIssued();

                string where = "?";
                try { var b = smr.bounds; where = "centre " + b.center + " size " + b.size; } catch { }
                _log?.Msg("[body] " + MapHVT.HvtName + " is wearing the custom body: "
                        + need + " bones resolved BY NAME, root '"
                        + (rootBone != null ? rootBone.name : "<null>") + "', hid " + hidden
                        + " issued renderer(s). World bounds: " + where);
                return true;
            }
            catch (Exception e) { _log?.Error("[body] wear: " + e.Message); return false; }
        }
    }
}
