// ODG Operations - AirborneInfil
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using UnityEngine;

namespace AirborneInfil
{

    internal static class PoseDriver
    {
        class Pose
        {
            public float blendIn = 0.6f;
            public readonly Dictionary<string, Vector3> bones = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
        }

        static readonly Dictionary<string, Pose> _poses = new Dictionary<string, Pose>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, Transform> _bones = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
        static string _stage;
        static float _stageT0;
        static bool _loadTried;
        static Il2Cpp.PlayerNetworking _pn;
        static readonly System.Collections.Generic.List<Behaviour> _ikDisabled = new System.Collections.Generic.List<Behaviour>();
        static readonly System.Collections.Generic.Dictionary<string, Quaternion> _origRot = new System.Collections.Generic.Dictionary<string, Quaternion>();
        static Animator _bodyAnim;
        static float _bodyAnimSpeed = 1f;

        static string JsonPath => Path.Combine(AiPaths.GameRoot, "Mods", "AirborneInfil_Assets", "poses.json");

        internal static void CacheBones(Il2Cpp.PlayerNetworking pn)
        {
            _bones.Clear();
            _origRot.Clear();
            _pn = pn;
            try
            {
                if (pn == null) return;
                Transform fps = null;
                try { var rig = pn.myFPSRIG; fps = rig != null ? rig.transform : null; } catch { }
                var all = pn.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    var t = all[i]; if (t == null) continue;
                    string n = t.name;
                    int idx = n.IndexOf("mixamorig", StringComparison.OrdinalIgnoreCase);
                    if (idx < 0) continue;
                    if (fps != null) { try { if (t.IsChildOf(fps)) continue; } catch { } }
                    string suffix = n.Substring(idx + 9).TrimStart(':', '_', ' ');
                    if (suffix.Length == 0) continue;
                    if (!_bones.ContainsKey(suffix)) { _bones[suffix] = t; try { _origRot[suffix] = t.localRotation; } catch { } }
                }
                AirborneMod.Log("[pose] cached " + _bones.Count + " TP bones");
            }
            catch (Exception e) { AirborneMod.Log("[pose] CacheBones err: " + e.Message); }
        }

        internal static void SetStage(string stage)
        {
            if (_stage == stage) return;
            _stage = stage;
            _stageT0 = Time.time;
        }

        internal static void Release()
        {

            foreach (var kv in _origRot)
            {
                Transform t;
                if (_bones.TryGetValue(kv.Key, out t) && t != null)
                { try { t.localRotation = kv.Value; } catch { } }
            }
            SuppressRig(false);
            _bones.Clear();
            _origRot.Clear();
            _stage = null;
            _diagStage = null;
            _pn = null;
        }

        internal static void SuppressRig(bool on)
        {
            if (on)
            {
                try
                {

                    Transform hips;
                    _bones.TryGetValue("Hips", out hips);
                    _bodyAnim = null;
                    var t = hips; int guard = 0;
                    while (t != null && guard++ < 25)
                    {
                        Animator a = null;
                        try { a = t.GetComponent<Animator>(); } catch { }
                        if (a != null) { _bodyAnim = a; break; }
                        t = t.parent;
                    }
                    if (_bodyAnim != null)
                    {
                        _bodyAnimSpeed = _bodyAnim.speed;
                        _bodyAnim.speed = 0f;
                    }
                }
                catch { }
                try
                {
                    if (_pn != null)
                    {
                        Transform fps = null;
                        try { var rig = _pn.myFPSRIG; fps = rig != null ? rig.transform : null; } catch { }
                        var behs = _pn.GetComponentsInChildren<Behaviour>(true);
                        int off = 0;
                        var offNames = new System.Collections.Generic.List<string>();
                        for (int i = 0; i < behs.Length; i++)
                        {
                            var b = behs[i]; if (b == null || !b.enabled) continue;
                            if (fps != null) { try { if (b.transform.IsChildOf(fps)) continue; } catch { } }
                            string tn = "";
                            try { tn = b.GetIl2CppType().FullName ?? ""; } catch { }

                            bool isIk = tn.IndexOf("RootMotion", StringComparison.Ordinal) >= 0
                                     || tn.IndexOf("FinalIK", StringComparison.Ordinal) >= 0
                                     || tn.EndsWith("IK", StringComparison.Ordinal);
                            if (!isIk) continue;
                            try
                            {
                                b.enabled = false; _ikDisabled.Add(b); off++;
                                string sn = tn; int dot = sn.LastIndexOf('.'); if (dot >= 0) sn = sn.Substring(dot + 1);
                                if (!offNames.Contains(sn)) offNames.Add(sn);
                            }
                            catch { }
                        }
                        AirborneMod.Log("[pose] rig suppressed: animator " + (_bodyAnim != null ? "frozen" : "not found") +
                                        ", " + off + " limb solvers off" + (offNames.Count > 0 ? " [" + string.Join(",", offNames) + "]" : ""));
                    }
                }
                catch (Exception e) { AirborneMod.Log("[pose] SuppressRig err: " + e.Message); }
            }
            else
            {
                for (int i = 0; i < _ikDisabled.Count; i++)
                { try { var b = _ikDisabled[i]; if (b != null) b.enabled = true; } catch { } }
                if (_ikDisabled.Count > 0) AirborneMod.Log("[pose] rig restored (" + _ikDisabled.Count + " IK components back on)");
                _ikDisabled.Clear();
                try { if (_bodyAnim != null) _bodyAnim.speed = _bodyAnimSpeed <= 0f ? 1f : _bodyAnimSpeed; } catch { }
                _bodyAnim = null;
            }
        }

        static string _diagStage;

        internal static void Apply()
        {
            if (_stage == null || _bones.Count == 0) return;
            if (!_loadTried) Load(false);
            Pose p;
            if (!_poses.TryGetValue(_stage, out p) || p.bones.Count == 0) return;

            if (_diagStage != _stage)
            {
                _diagStage = _stage;
                int matched = 0; var miss = new System.Collections.Generic.List<string>();
                foreach (var kv in p.bones) { if (_bones.ContainsKey(kv.Key)) matched++; else miss.Add(kv.Key); }
                AirborneMod.Log("[pose] stage '" + _stage + "': " + matched + "/" + p.bones.Count + " bones resolved" +
                                (miss.Count > 0 ? "  MISSING: " + string.Join(",", miss) : ""));
            }

            float w = Mathf.Clamp01((Time.time - _stageT0) / Mathf.Max(0.05f, p.blendIn));
            foreach (var kv in p.bones)
            {
                Transform t;
                if (!_bones.TryGetValue(kv.Key, out t) || t == null) continue;

                Quaternion baseRot;
                if (!_origRot.TryGetValue(kv.Key, out baseRot)) baseRot = Quaternion.identity;
                Quaternion targetRot = baseRot * Quaternion.Euler(kv.Value);
                try { t.localRotation = Quaternion.Slerp(t.localRotation, targetRot, w); }
                catch { }
            }
        }

        internal static void Load(bool verbose)
        {
            _loadTried = true;
            try
            {
                EnsureDefaultFile();
                var txt = File.ReadAllText(JsonPath);
                var parsed = new Dictionary<string, Pose>(StringComparer.OrdinalIgnoreCase);
                using (var doc = JsonDocument.Parse(txt))
                {
                    foreach (var stageProp in doc.RootElement.EnumerateObject())
                    {
                        if (stageProp.Name.StartsWith("_")) continue;
                        var pose = new Pose();
                        if (stageProp.Value.TryGetProperty("blendIn", out var bi)) pose.blendIn = (float)bi.GetDouble();
                        if (stageProp.Value.TryGetProperty("bones", out var bones))
                            foreach (var b in bones.EnumerateObject())
                            {
                                var a = b.Value;
                                if (a.GetArrayLength() >= 3)
                                    pose.bones[b.Name] = new Vector3(
                                        (float)a[0].GetDouble(), (float)a[1].GetDouble(), (float)a[2].GetDouble());
                            }
                        parsed[stageProp.Name] = pose;
                    }
                }
                _poses.Clear();
                foreach (var kv in parsed) _poses[kv.Key] = kv.Value;
                if (verbose) AirborneMod.Log("[pose] reloaded poses.json: " + _poses.Count + " stages");
            }
            catch (Exception e) { AirborneMod.Log("[pose] load err (keeping previous): " + e.Message); }
        }

        static void EnsureDefaultFile()
        {
            if (File.Exists(JsonPath)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(JsonPath));
            File.WriteAllText(JsonPath, DefaultJson);
            AirborneMod.Log("[pose] wrote default poses.json");
        }

        const string DefaultJson = @"{
  ""_readme"": ""Edit values, press Home in-game to hot-reload. Bones = mixamorig suffixes. [x,y,z] local euler degrees. blendIn = seconds to ease in."",
  ""canopy"": {
    ""blendIn"": 0.6,
    ""bones"": {
      ""Spine"": [6, 0, 0],
      ""Spine1"": [4, 0, 0],
      ""Spine2"": [4, 0, 0],
      ""Head"": [-8, 0, 0],
      ""LeftArm"": [0, 0, 58],
      ""RightArm"": [0, 0, -58],
      ""LeftForeArm"": [0, 0, 28],
      ""RightForeArm"": [0, 0, -28],
      ""LeftHand"": [0, 0, 10],
      ""RightHand"": [0, 0, -10],
      ""LeftUpLeg"": [0, 0, 0],
      ""RightUpLeg"": [0, 0, 0],
      ""LeftLeg"": [0, 0, 0],
      ""RightLeg"": [0, 0, 0],
      ""LeftFoot"": [0, 0, 0],
      ""RightFoot"": [0, 0, 0]
    }
  },
  ""freefall"": {
    ""blendIn"": 0.4,
    ""bones"": {
      ""Spine"": [-10, 0, 0],
      ""Spine1"": [-6, 0, 0],
      ""Spine2"": [-4, 0, 0],
      ""Head"": [-15, 0, 0],
      ""LeftArm"": [0, -25, 25],
      ""RightArm"": [0, 25, -25],
      ""LeftForeArm"": [0, 0, 40],
      ""RightForeArm"": [0, 0, -40],
      ""LeftUpLeg"": [-12, 0, 10],
      ""RightUpLeg"": [-12, 0, -10],
      ""LeftLeg"": [35, 0, 0],
      ""RightLeg"": [35, 0, 0]
    }
  }
}
";
    }
}
