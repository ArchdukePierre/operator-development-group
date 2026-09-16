// ODG Operations - MapLoader
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using UnityEngine;

namespace MapLoader
{

    public static class MapLights
    {
        static MelonLogger.Instance _log;
        public static void Init(MelonLogger.Instance log) { _log = log; }

        const string FileName = "lights.bin";
        const string Magic = "MLT1";

        public const int CatCount = 8;
        public const int CatUnspecified = 0;
        public const int CatStreet = 1;
        public const int CatExterior = 2;
        public const int CatInterior = 3;
        public const int CatBeacon = 4;
        public const int CatSign = 5;
        public const int CatTraffic = 6;

        static readonly string[] CatName =
            { "unspec", "street", "exterior", "interior", "beacon", "sign", "traffic", "?7" };

        public static int MaxEnabled = 240;

        public static readonly float[] RangeCapFor =
            { 12f, 16f, 12f, 6f, 10f, 3f, 2f, 12f };

        public static readonly float[] LumenCapFor =
            { 4000f, 1200f, 2000f, 1500f, 1000f, 150f, 100f, 4000f };

        public static readonly float[] FadeFor =
            { 60f, 110f, 70f, 45f, 70f, 35f, 35f, 60f };

        public static readonly int[] FloorFor =
            { 0, 80, 24, 72, 4, 0, 0, 0 };

        static readonly int[] FloorOrder = { CatStreet, CatBeacon, CatExterior, CatInterior };

        public static float RangeCap = 12f;
        public static float LumenCap = 4000f;
        public static float FadeDistance = 60f;

        public static float IntensityScale = 1f;

        public static bool Rolling = true;

        public static float RollOnDist = 60f;
        public static float RollOffDist = 78f;
        public static float RerankPeriod = 0.5f;

        public static int MaxTogglesPerFrame = 8;

        public static int BuiltCount { get; private set; }
        public static int EnabledCount { get; private set; }

        static GameObject _root;
        static bool _built;
        static int _staticEnabled;

        static int[] _gotPerCat;
        static int[] _onPerCat;

        static readonly List<GameObject> _rollGo = new List<GameObject>(300);
        static readonly List<Vector3> _rollPos = new List<Vector3>(300);
        static readonly List<int> _rollCat = new List<int>(300);
        static readonly List<bool> _rollOn = new List<bool>(300);
        static float[] _d2;
        static float _nextRank;

        static readonly List<int> _pendOff = new List<int>(64);
        static readonly List<int> _pendOn = new List<int>(64);
        static readonly List<int> _keep = new List<int>(300);
        static readonly List<int> _want = new List<int>(300);

        static Camera _cam;
        static Camera Cam
        {
            get
            {
                if (_cam == null) { try { _cam = Camera.main; } catch { } }
                return _cam;
            }
        }

        struct Rec
        {
            public string Name;
            public byte Type;
            public Vector3 Pos;
            public Quaternion Rot;
            public Color Col;
            public float Lumen, Range, Angle;
            public int Cat;
        }

        static int CatOf(int c) { return (c < 0 || c >= CatCount) ? CatUnspecified : c; }

        static bool IsRollingCat(int c)
        {
            return c == CatInterior || c == CatSign || c == CatTraffic;
        }

        static int RollBudget()
        {
            int b = MaxEnabled - _staticEnabled;
            return b > 0 ? b : 0;
        }

        public static float ScaleOverride = float.NaN;

        public static void Build()
        {

            IntensityScale = !float.IsNaN(ScaleOverride)
                           ? ScaleOverride
                           : MapIdentity.LightIntensityScale;

            MaxEnabled = MapIdentity.MaxLights;
            if (_built) return;
            try
            {
                string path = Path.Combine(MapRuntime.BundleDir, FileName);
                if (!File.Exists(path))
                {

                    _log?.Msg("[lights] no " + FileName + " in bundle, skipping");
                    _built = true;
                    return;
                }

                RangeCapFor[CatUnspecified] = RangeCap;
                LumenCapFor[CatUnspecified] = LumenCap;
                FadeFor[CatUnspecified] = FadeDistance;

                var recs = ReadAll(path);
                if (recs == null || recs.Count == 0) { _built = true; return; }

                var order = Rank(recs);

                var parent = MapRuntime.LightingRoot;
                if (parent == null && MapRuntime.Root != null)
                    parent = MapRuntime.Root.transform;
                if (parent == null)
                {
                    _log?.Warning("[lights] no map root to hang lights under");
                    return;
                }

                LogLightLoopBudget();

                _root = new GameObject("MAPLIGHTS");
                _root.transform.SetParent(parent, true);

                int built = 0, enabled = 0;
                var onPerCat = new int[CatCount];
                var gotPerCat = new int[CatCount];
                int rolling = 0;
                for (int k = 0; k < order.Count; k++)
                {
                    var rec = recs[order[k]];
                    int c = CatOf(rec.Cat);

                    bool roll = Rolling && IsRollingCat(c);
                    bool on = !roll && enabled < MaxEnabled;
                    var go = MakeLight(rec, _root.transform, on, c);
                    if (go == null) continue;
                    built++;
                    gotPerCat[c]++;
                    if (on) { enabled++; onPerCat[c]++; }
                    if (roll)
                    {
                        _rollGo.Add(go);
                        _rollPos.Add(rec.Pos);
                        _rollCat.Add(c);
                        _rollOn.Add(false);
                        rolling++;
                    }
                }
                BuiltCount = built; EnabledCount = enabled;
                _staticEnabled = enabled;
                _gotPerCat = gotPerCat; _onPerCat = onPerCat;
                _nextRank = 0f;
                _built = true;

                var sb = new System.Text.StringBuilder();
                for (int c = 0; c < CatCount; c++)
                {
                    if (gotPerCat[c] == 0) continue;
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(CatName[c]).Append(' ').Append(onPerCat[c])
                      .Append('/').Append(gotPerCat[c]);
                }
                _log?.Msg("[lights] " + built + " fixture(s) built (" + sb + "), "
                        + enabled + " always-on, " + rolling + " following the "
                        + "camera on a budget of " + RollBudget() + " (enable "
                        + RollOnDist.ToString("F0") + " m, release "
                        + RollOffDist.ToString("F0") + " m, "
                        + MaxTogglesPerFrame + " toggles/frame). Shadows off on "
                        + "every one.");
            }
            catch (Exception e)
            {
                _log?.Warning("[lights] build stopped: " + e.Message);
                _built = true;
            }
        }

        static List<Rec> ReadAll(string path)
        {
            var list = new List<Rec>(700);
            using (var fs = File.OpenRead(path))
            using (var r = new BinaryReader(fs))
            {
                if (new string(r.ReadChars(4)) != Magic)
                {
                    _log?.Warning("[lights] " + FileName + ": bad magic");
                    return null;
                }
                int n = r.ReadInt32();
                if (n < 0 || n > 100000)
                {
                    _log?.Warning("[lights] " + FileName + ": absurd count " + n);
                    return null;
                }
                for (int i = 0; i < n; i++)
                {
                    var rec = new Rec();
                    int len = r.ReadUInt16();
                    rec.Name = len > 0
                        ? System.Text.Encoding.UTF8.GetString(r.ReadBytes(len))
                        : ("light_" + i);
                    rec.Type = r.ReadByte();
                    rec.Pos = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    rec.Rot = new Quaternion(r.ReadSingle(), r.ReadSingle(),
                                             r.ReadSingle(), r.ReadSingle());
                    rec.Col = new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    rec.Lumen = r.ReadSingle();
                    rec.Range = r.ReadSingle();
                    rec.Angle = r.ReadSingle();

                    byte flags = r.ReadByte();
                    rec.Cat = (flags >> 1) & 7;
                    list.Add(rec);
                }
            }
            return list;
        }

        static List<int> Rank(List<Rec> recs)
        {
            var order = new List<int>(recs.Count);
            for (int i = 0; i < recs.Count; i++) order.Add(i);

            var anchors = MarkerPositions();
            if (anchors.Count < 8) return order;

            var score = new float[recs.Count];
            for (int i = 0; i < recs.Count; i++)
            {
                int c = CatOf(recs[i].Cat);
                bool outdoor = (c == CatStreet || c == CatExterior
                             || c == CatBeacon || c == CatTraffic);
                Vector3 p = recs[i].Pos;
                float best = float.MaxValue;
                for (int a = 0; a < anchors.Count; a++)
                {
                    Vector3 q = anchors[a];
                    float dx = q.x - p.x, dz = q.z - p.z;
                    float d = outdoor ? (dx * dx + dz * dz)
                                      : (dx * dx + (q.y - p.y) * (q.y - p.y) + dz * dz);
                    if (d < best) best = d;
                }
                score[i] = best;
            }

            var byCat = new List<int>[CatCount];
            for (int c = 0; c < CatCount; c++) byCat[c] = new List<int>();
            for (int i = 0; i < recs.Count; i++)
                byCat[CatOf(recs[i].Cat)].Add(i);
            for (int c = 0; c < CatCount; c++)
                byCat[c].Sort((a, b) => score[a].CompareTo(score[b]));

            var picked = new List<int>(recs.Count);
            var taken = new bool[recs.Count];
            for (int k = 0; k < FloorOrder.Length; k++)
            {
                int c = FloorOrder[k];
                int room = MaxEnabled - picked.Count;
                if (room <= 0) break;
                int n = Mathf.Min(Mathf.Min(FloorFor[c], byCat[c].Count), room);
                for (int i = 0; i < n; i++)
                {
                    picked.Add(byCat[c][i]);
                    taken[byCat[c][i]] = true;
                }
            }

            var rest = new List<int>(recs.Count);
            for (int i = 0; i < recs.Count; i++) if (!taken[i]) rest.Add(i);
            rest.Sort((a, b) => score[a].CompareTo(score[b]));
            picked.AddRange(rest);
            return picked;
        }

        static List<Vector3> MarkerPositions()
        {
            var pts = new List<Vector3>(300);
            try
            {
                var mt = MapRuntime.MarkersRoot;
                if (mt == null) return pts;

                for (int g = 0; g < mt.childCount; g++)
                {
                    var grp = mt.GetChild(g);
                    for (int i = 0; i < grp.childCount; i++)
                        pts.Add(grp.GetChild(i).position);
                }
            }
            catch { }
            return pts;
        }

        static GameObject MakeLight(Rec rec, Transform parent, bool on, int c)
        {
            try
            {
                var go = new GameObject(rec.Name);
                go.transform.SetParent(parent, false);

                go.transform.localPosition = rec.Pos;
                go.transform.localRotation = rec.Rot;

                go.SetActive(false);

                var l = go.AddComponent<Light>();
                l.type = rec.Type == 1 ? LightType.Spot : LightType.Point;
                l.color = rec.Col;
                l.range = Mathf.Min(rec.Range, RangeCapFor[c]);
                if (rec.Type == 1)
                    l.spotAngle = Mathf.Clamp(rec.Angle, 1f, 175f);
                l.shadows = LightShadows.None;
                l.cullingMask = ~0;

                var hd = go.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>();
                if (hd != null)
                {
                    float lm = Mathf.Min(rec.Lumen * IntensityScale, LumenCapFor[c]);

                    try { hd.SetIntensity(lm, UnityEngine.Rendering.LightUnit.Lumen); } catch { }
                    try { hd.EnableShadows(false); } catch { }

                    try { hd.affectsVolumetric = false; } catch { }
                    try { hd.fadeDistance = FadeFor[c]; } catch { }
                    if (rec.Type == 1)
                    {

                        try { hd.SetSpotAngle(Mathf.Clamp(rec.Angle, 1f, 175f), 0f); } catch { }
                    }
                }

                if (on) go.SetActive(true);
                return go;
            }
            catch (Exception e)
            {
                _log?.Warning("[lights] " + rec.Name + ": " + e.Message);
                return null;
            }
        }

        public static void Tick()
        {
            if (!_built || !Rolling || _root == null) return;
            if (_rollGo.Count == 0) return;

            if (_pendOff.Count > 0 || _pendOn.Count > 0) { Flush(); return; }

            float now = Time.realtimeSinceStartup;
            if (now < _nextRank) return;
            _nextRank = now + RerankPeriod;
            Rerank();
        }

        static void Rerank()
        {
            var cam = Cam;
            if (cam == null) return;
            Vector3 cp;

            try { cp = cam.transform.position; }
            catch { _cam = null; return; }

            int n = _rollGo.Count;
            if (_d2 == null || _d2.Length < n) _d2 = new float[n];
            float on2 = RollOnDist * RollOnDist;
            float off2 = RollOffDist * RollOffDist;

            _keep.Clear();
            for (int i = 0; i < n; i++)
            {
                Vector3 p = _rollPos[i];
                float dx = p.x - cp.x, dy = p.y - cp.y, dz = p.z - cp.z;
                _d2[i] = dx * dx + dy * dy + dz * dz;
                if (!_rollOn[i]) continue;
                if (_d2[i] > off2) _pendOff.Add(i);
                else _keep.Add(i);
            }

            int budget = RollBudget();

            if (_keep.Count > budget)
            {
                _keep.Sort((a, b) => _d2[b].CompareTo(_d2[a]));
                int drop = _keep.Count - budget;
                for (int i = 0; i < drop; i++) _pendOff.Add(_keep[i]);
            }
            else if (_keep.Count < budget)
            {
                _want.Clear();
                for (int i = 0; i < n; i++)
                    if (!_rollOn[i] && _d2[i] < on2) _want.Add(i);
                _want.Sort((a, b) => _d2[a].CompareTo(_d2[b]));
                int room = budget - _keep.Count;
                if (room > _want.Count) room = _want.Count;
                for (int i = 0; i < room; i++) _pendOn.Add(_want[i]);
            }
        }

        static void Flush()
        {
            int flips = 0;

            while (flips < MaxTogglesPerFrame && _pendOff.Count > 0)
            {
                int last = _pendOff.Count - 1;
                int i = _pendOff[last];
                _pendOff.RemoveAt(last);
                if (!_rollOn[i]) continue;
                var go = _rollGo[i];
                if (go != null) { try { go.SetActive(false); } catch { } }
                _rollOn[i] = false;
                EnabledCount--;
                if (_onPerCat != null) _onPerCat[_rollCat[i]]--;
                flips++;
            }

            while (flips < MaxTogglesPerFrame && _pendOn.Count > 0)
            {
                int i = _pendOn[0];
                _pendOn.RemoveAt(0);
                if (_rollOn[i]) continue;

                if (EnabledCount - _staticEnabled >= RollBudget()) { _pendOn.Clear(); break; }
                var go = _rollGo[i];
                if (go == null) continue;
                try { go.SetActive(true); } catch { continue; }
                _rollOn[i] = true;
                EnabledCount++;
                if (_onPerCat != null) _onPerCat[_rollCat[i]]++;
                flips++;
            }
        }

        static bool _budgetLogged;
        static void LogLightLoopBudget()
        {
            if (_budgetLogged) return;
            _budgetLogged = true;
            try
            {
                UnityEngine.Rendering.RenderPipelineAsset rpa = null;

                try { rpa = QualitySettings.renderPipeline; } catch { }
                if (rpa == null)
                {
                    try { rpa = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline; } catch { }
                }
                var hdrp = rpa != null
                    ? rpa.TryCast<UnityEngine.Rendering.HighDefinition.HDRenderPipelineAsset>()
                    : null;
                if (hdrp == null)
                {
                    _log?.Msg("[lights] HDRP asset not readable; punctual-light "
                            + "headroom unmeasured");
                    return;
                }
                int cap = hdrp.currentPlatformRenderPipelineSettings
                              .lightLoopSettings.maxPunctualLightsOnScreen;
                _log?.Msg("[lights] HDRP maxPunctualLightsOnScreen = " + cap
                        + " (our budget " + MaxEnabled + ", on-screen count "
                        + "further bounded by per-category fadeDistance)");
            }
            catch (Exception e)
            {
                _log?.Msg("[lights] punctual-light headroom unreadable: " + e.Message);
            }
        }

        public static string HandleConsole(string arg)
        {
            arg = (arg ?? "").Trim().ToLowerInvariant();

            if (arg.StartsWith("scale", StringComparison.Ordinal))
            {
                string v = arg.Length > 5 ? arg.Substring(5).Trim() : "";
                if (v.Length == 0)
                    return "fixture brightness scale is " + IntensityScale.ToString("F2")
                         + " (this map's own value is "
                         + MapIdentity.LightIntensityScale.ToString("F2")
                         + "). 'odg lights scale <x>' to change it, "
                         + "'odg lights scale reset' to go back.";
                if (v == "reset")
                {
                    ScaleOverride = float.NaN;
                    Forget(); Build();
                    return "brightness back to this map's own "
                         + IntensityScale.ToString("F2") + ", "
                         + BuiltCount + " fixture(s) rebuilt.";
                }
                float f;
                if (!float.TryParse(v, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out f)
                    || f <= 0f || f > 200f)
                    return "give a number between 0 and 200, e.g. 'odg lights scale 6'.";
                ScaleOverride = f;

                Forget(); Build();
                return "fixture brightness scale " + f.ToString("F2") + ", "
                     + BuiltCount + " fixture(s) rebuilt, " + EnabledCount + " lit.";
            }

            if (arg.Length == 0 || arg == "status")
            {
                if (!_built || BuiltCount == 0)
                    return "no fixture lights on this map (no lights.bin, or the "
                         + "map is not built yet).";
                var sb = new System.Text.StringBuilder();
                sb.Append("fixture lights: ").Append(EnabledCount).Append(" lit of ")
                  .Append(BuiltCount).Append(" built, budget ").Append(MaxEnabled)
                  .Append('\n');
                if (_gotPerCat != null)
                {
                    for (int c = 0; c < CatCount; c++)
                    {
                        if (_gotPerCat[c] == 0) continue;
                        sb.Append("  ").Append(CatName[c]).Append(' ')
                          .Append(_onPerCat[c]).Append('/').Append(_gotPerCat[c])
                          .Append(Rolling && IsRollingCat(c)
                                  ? "   lit near you" : "   always on")
                          .Append('\n');
                    }
                }
                if (Rolling && _rollGo.Count > 0)
                    sb.Append("  camera share ").Append(RollBudget())
                      .Append(" slots, enable ").Append(RollOnDist.ToString("F0"))
                      .Append(" m / release ").Append(RollOffDist.ToString("F0"))
                      .Append(" m\n");
                sb.Append("  odg lights <n>   move the total budget");
                return sb.ToString();
            }

            int n;
            if (int.TryParse(arg, out n))
            {
                MaxEnabled = Mathf.Clamp(n, 0, 2048);
                return "light budget " + MaxEnabled + ". The camera-following "
                     + "share adjusts within a second or two; the always-on "
                     + "street set only re-cuts when the map rebuilds.";
            }

            return "odg lights           status\n"
                 + "odg lights <n>       total lit-fixture budget (default 240)";
        }

        public static void Forget()
        {
            _root = null;
            _cam = null;
            _built = false;
            BuiltCount = 0;
            EnabledCount = 0;
            _staticEnabled = 0;
            _gotPerCat = null;
            _onPerCat = null;
            _rollGo.Clear();
            _rollPos.Clear();
            _rollCat.Clear();
            _rollOn.Clear();
            _pendOff.Clear();
            _pendOn.Clear();
            _keep.Clear();
            _want.Clear();
            _d2 = null;
            _nextRank = 0f;
        }
    }
}
